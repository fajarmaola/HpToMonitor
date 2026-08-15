// Implements the C ABI (ssl_native.h): a capture+encode worker thread that pulls DXGI frames
// and pushes encoded H.264 to the managed callback.
#include "ssl_native.h"
#include "DxgiCapture.h"
#include "MediaFoundationH264Encoder.h"
#include <thread>
#include <atomic>
#include <mutex>
#include <chrono>
#include <string>
#include <cstring>
#include <combaseapi.h>

using namespace ssl;

namespace {
    std::thread g_worker;
    std::atomic<bool> g_running{false};
    std::atomic<int> g_status{0};   // 0 = idle/starting, 1 = capturing OK, <0 = init failed
    DxgiCapture g_capture;
    MediaFoundationH264Encoder g_encoder;
    SslEncodedFrameCallback g_cb = nullptr;
    void* g_user = nullptr;
    std::atomic<int> g_fps{60};
    std::mutex g_errMutex;
    std::string g_lastError;        // shared across threads — guarded by g_errMutex (was thread_local: bug)

    void SetLastError(const std::string& msg) {
        std::lock_guard<std::mutex> lk(g_errMutex);
        g_lastError = msg;
    }

    uint64_t NowUs() {
        using namespace std::chrono;
        return duration_cast<microseconds>(steady_clock::now().time_since_epoch()).count();
    }

    void WorkerLoop(int outputIndex, EncoderConfig cfg) {
        CoInitializeEx(nullptr, COINIT_MULTITHREADED);

        bool captureOk = g_capture.Initialize(outputIndex);
        if (!captureOk) {
            std::string first = g_capture.LastError();
            g_capture.Shutdown();
            // Fallback: capture the PRIMARY display (index 0) so at least SOMETHING streams
            // (mirror). This proves the encode+network+decode path works and isolates a
            // virtual-display-specific capture problem from a whole-pipeline problem.
            if (outputIndex != 0 && g_capture.Initialize(0)) {
                SetLastError("Virtual-display capture failed (" + first +
                             "). Fell back to PRIMARY display mirror.");
                captureOk = true;
            } else {
                SetLastError("capture init (output " + std::to_string(outputIndex) + "): " + first);
                g_status = -1; g_running = false; CoUninitialize(); return;
            }
        }
        cfg.width = g_capture.Width();
        cfg.height = g_capture.Height();
        if (!g_encoder.Initialize(g_capture.Device(), cfg)) {
            SetLastError("encoder init: " + g_encoder.LastError());
            g_status = -2; g_running = false; g_capture.Shutdown(); CoUninitialize(); return;
        }
        g_encoder.SetCallback([](uint32_t id, uint64_t ts, bool key, const uint8_t* d, int n) {
            if (g_cb) g_cb(id, ts, key ? 1 : 0, d, n, g_user);
        });
        g_status = 1; // capture + encoder initialized; frames should now flow

        while (g_running) {
            int targetFps = g_fps.load();
            auto frameStart = std::chrono::steady_clock::now();

            Microsoft::WRL::ComPtr<ID3D11Texture2D> tex;
            int r = g_capture.AcquireFrame(tex, 8);
            if (r == 1 && tex) {
                g_encoder.EncodeFrame(tex.Get(), NowUs());
                g_capture.ReleaseFrame();
            } else if (r < 0) {
                // Access lost (e.g. resolution change / mode switch). Reinitialize.
                g_capture.Shutdown();
                if (!g_capture.Initialize(outputIndex)) {
                    std::this_thread::sleep_for(std::chrono::milliseconds(200));
                }
            }

            // Pace to target FPS (encoder itself also rate-limits via duration).
            auto elapsed = std::chrono::steady_clock::now() - frameStart;
            auto period = std::chrono::microseconds(1'000'000 / (targetFps > 0 ? targetFps : 60));
            if (elapsed < period) std::this_thread::sleep_for(period - elapsed);
        }

        g_encoder.Shutdown();
        g_capture.Shutdown();
        CoUninitialize();
    }
}

extern "C" {

SSL_API int SSL_CALL SslNativeGetOutputCount() {
    return DxgiCapture::GetOutputCount();
}

SSL_API int SSL_CALL SslNativeStart(int outputIndex, int fps, int bitrateKbps,
                                    int useHardware, SslEncodedFrameCallback cb, void* user) {
    if (g_running) return -100; // already running
    g_cb = cb; g_user = user; g_fps = fps > 0 ? fps : 60;
    g_status = 0; SetLastError("");
    EncoderConfig cfg;
    cfg.fps = g_fps.load();
    cfg.bitrateKbps = bitrateKbps > 0 ? bitrateKbps : 8000;
    cfg.useHardware = useHardware != 0;
    g_running = true;
    g_worker = std::thread(WorkerLoop, outputIndex, cfg);
    return 0;
}

SSL_API void SSL_CALL SslNativeRequestKeyframe() { g_encoder.RequestKeyframe(); }
SSL_API void SSL_CALL SslNativeSetBitrate(int kbps) { g_encoder.SetBitrate(kbps); }
SSL_API void SSL_CALL SslNativeSetFps(int fps) { if (fps > 0) { g_fps = fps; g_encoder.SetFps(fps); } }

SSL_API void SSL_CALL SslNativeStop() {
    if (!g_running) return;
    g_running = false;
    if (g_worker.joinable()) g_worker.join();
    g_status = 0;
    g_cb = nullptr; g_user = nullptr;
}

SSL_API int SSL_CALL SslNativeGetStatus() { return g_status.load(); }

SSL_API const char* SSL_CALL SslNativeLastError() {
    static char buf[512];
    std::lock_guard<std::mutex> lk(g_errMutex);
    strncpy_s(buf, sizeof(buf), g_lastError.c_str(), _TRUNCATE);
    return buf;
}

} // extern "C"
