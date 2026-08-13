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
#include <combaseapi.h>

using namespace ssl;

namespace {
    std::thread g_worker;
    std::atomic<bool> g_running{false};
    DxgiCapture g_capture;
    MediaFoundationH264Encoder g_encoder;
    SslEncodedFrameCallback g_cb = nullptr;
    void* g_user = nullptr;
    std::atomic<int> g_fps{60};
    thread_local std::string g_lastError;

    uint64_t NowUs() {
        using namespace std::chrono;
        return duration_cast<microseconds>(steady_clock::now().time_since_epoch()).count();
    }

    void WorkerLoop(int outputIndex, EncoderConfig cfg) {
        CoInitializeEx(nullptr, COINIT_MULTITHREADED);

        if (!g_capture.Initialize(outputIndex)) {
            g_lastError = "capture init: " + g_capture.LastError();
            g_running = false; CoUninitialize(); return;
        }
        cfg.width = g_capture.Width();
        cfg.height = g_capture.Height();
        if (!g_encoder.Initialize(g_capture.Device(), cfg)) {
            g_lastError = "encoder init: " + g_encoder.LastError();
            g_running = false; CoUninitialize(); return;
        }
        g_encoder.SetCallback([](uint32_t id, uint64_t ts, bool key, const uint8_t* d, int n) {
            if (g_cb) g_cb(id, ts, key ? 1 : 0, d, n, g_user);
        });

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
    g_cb = nullptr; g_user = nullptr;
}

SSL_API const char* SSL_CALL SslNativeLastError() {
    return g_lastError.c_str();
}

} // extern "C"
