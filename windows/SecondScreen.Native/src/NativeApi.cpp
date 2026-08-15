// Implements the C ABI (ssl_native.h): a capture+encode worker thread that pulls DXGI frames
// and pushes encoded H.264 to the managed callback.
#include "ssl_native.h"
#include "DxgiCapture.h"
#include "MediaFoundationH264Encoder.h"
#include <windows.h>
#include <thread>
#include <atomic>
#include <mutex>
#include <chrono>
#include <string>
#include <cstring>
#include <vector>
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
    std::atomic<int> g_inputs{0};   // frames fed into the encoder
    std::atomic<int> g_outputs{0};  // encoded H.264 access units emitted (callback fired)
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

    // Capture a screen region via GDI (BitBlt) into a BGRA D3D11 texture. Unlike Desktop
    // Duplication, GDI can grab a STATIC screen at any time, so it bootstraps the first frame and
    // keeps an idle/empty extended display visible on the phone. Slower, used only when duplication
    // reports no change. Returns false on failure.
    bool GdiGrab(ID3D11Device* dev, int left, int top, int w, int h,
                 Microsoft::WRL::ComPtr<ID3D11Texture2D>& out) {
        if (!dev || w <= 0 || h <= 0) return false;
        HDC screen = GetDC(nullptr);
        if (!screen) return false;
        HDC mem = CreateCompatibleDC(screen);
        HBITMAP bmp = CreateCompatibleBitmap(screen, w, h);
        HGDIOBJ oldObj = SelectObject(mem, bmp);
        bool ok = BitBlt(mem, 0, 0, w, h, screen, left, top, SRCCOPY | CAPTUREBLT) != FALSE;

        std::vector<uint8_t> pixels;
        if (ok) {
            BITMAPINFO bi{};
            bi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
            bi.bmiHeader.biWidth = w;
            bi.bmiHeader.biHeight = -h;            // negative = top-down rows
            bi.bmiHeader.biPlanes = 1;
            bi.bmiHeader.biBitCount = 32;          // BGRA (matches encoder ARGB32 input)
            bi.bmiHeader.biCompression = BI_RGB;
            pixels.resize((size_t)w * (size_t)h * 4);
            ok = GetDIBits(mem, bmp, 0, (UINT)h, pixels.data(), &bi, DIB_RGB_COLORS) != 0;
        }
        SelectObject(mem, oldObj);
        DeleteObject(bmp);
        DeleteDC(mem);
        ReleaseDC(nullptr, screen);
        if (!ok) return false;

        D3D11_TEXTURE2D_DESC d{};
        d.Width = (UINT)w; d.Height = (UINT)h; d.MipLevels = 1; d.ArraySize = 1;
        d.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        d.SampleDesc.Count = 1;
        d.Usage = D3D11_USAGE_DEFAULT;
        d.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        D3D11_SUBRESOURCE_DATA init{};
        init.pSysMem = pixels.data();
        init.SysMemPitch = (UINT)w * 4;
        return SUCCEEDED(dev->CreateTexture2D(&d, &init, out.GetAddressOf()));
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
            g_outputs.fetch_add(1);
            if (g_cb) g_cb(id, ts, key ? 1 : 0, d, n, g_user);
        });
        g_status = 1; // capture + encoder initialized; frames should now flow

        const int left = g_capture.Left(), top = g_capture.Top();
        const int gw = g_capture.Width(), gh = g_capture.Height();
        auto lastEncode = std::chrono::steady_clock::now() - std::chrono::seconds(1);
        g_encoder.RequestKeyframe(); // make the very first encoded frame an IDR

        while (g_running) {
            int targetFps = g_fps.load();
            auto frameStart = std::chrono::steady_clock::now();

            Microsoft::WRL::ComPtr<ID3D11Texture2D> tex;
            int r = g_capture.AcquireFrame(tex, 8);
            if (r == 1 && tex) {
                // Fast path: real screen change captured via Desktop Duplication (motion/video).
                if (g_encoder.EncodeFrame(tex.Get(), NowUs())) g_inputs.fetch_add(1);
                else SetLastError("encoder(dxgi): " + g_encoder.LastError());
                g_capture.ReleaseFrame();
                lastEncode = std::chrono::steady_clock::now();
            } else if (r < 0) {
                // Access lost (resolution change / mode switch). Reinitialize + refresh keyframe.
                g_capture.Shutdown();
                if (g_capture.Initialize(outputIndex)) {
                    g_encoder.RequestKeyframe();
                } else {
                    std::this_thread::sleep_for(std::chrono::milliseconds(200));
                }
            } else {
                // No new duplication frame. A static/empty extended desktop NEVER presents (not even
                // the first frame), so grab it with GDI so the phone always shows current content.
                // This also bootstraps frame #1 and lets UDP loss recover via periodic keyframes.
                auto sinceMs = std::chrono::duration_cast<std::chrono::milliseconds>(
                    std::chrono::steady_clock::now() - lastEncode).count();
                if (sinceMs >= 200) {
                    Microsoft::WRL::ComPtr<ID3D11Texture2D> gtex;
                    if (GdiGrab(g_capture.Device(), left, top, gw, gh, gtex) && gtex) {
                        g_encoder.RequestKeyframe();
                        if (g_encoder.EncodeFrame(gtex.Get(), NowUs())) g_inputs.fetch_add(1);
                        else SetLastError("encoder(gdi): " + g_encoder.LastError());
                        lastEncode = std::chrono::steady_clock::now();
                    } else {
                        SetLastError("GDI capture failed (BitBlt/GetDIBits/CreateTexture2D) for rect " +
                                     std::to_string(gw) + "x" + std::to_string(gh) +
                                     " @(" + std::to_string(left) + "," + std::to_string(top) + ")");
                    }
                }
            }

            // Pinpoint diagnostic: if we fed frames but the encoder emitted no H.264 output, the
            // problem is the encoder/MFT (not capture) — surface that so it isn't a silent black.
            if (g_outputs.load() == 0 && g_inputs.load() >= 3) {
                SetLastError("Encoder menerima " + std::to_string(g_inputs.load()) +
                             " frame tetapi output H.264 = 0 (MFT tidak mengeluarkan frame). " +
                             g_encoder.LastError());
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
    g_status = 0; g_inputs = 0; g_outputs = 0; SetLastError("");
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
