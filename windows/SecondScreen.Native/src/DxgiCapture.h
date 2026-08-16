// DXGI Desktop Duplication capture of a chosen output (display). Produces GPU textures; no
// CPU readback into bitmaps (the encoder consumes the texture directly).
#pragma once
#include <d3d11.h>
#include <dxgi1_2.h>
#include <dxgi1_5.h>
#include <wrl/client.h>
#include <cstdint>
#include <string>

namespace ssl {

using Microsoft::WRL::ComPtr;

class DxgiCapture {
public:
    bool Initialize(int outputIndex);
    // Acquires the next frame. On success, out receives a texture owned by the caller's device
    // context lifetime (valid until the next AcquireFrame/Release). timeoutMs limits the wait.
    // Returns: 1 = new frame, 0 = timeout/no change, -1 = error (must Reinitialize).
    int AcquireFrame(ComPtr<ID3D11Texture2D>& out, int timeoutMs = 8);
    void ReleaseFrame();
    void Shutdown();

    ID3D11Device* Device() const { return device_.Get(); }
    int Width() const { return width_; }
    int Height() const { return height_; }
    int Left() const { return left_; }
    int Top() const { return top_; }
    // True when the desktop is rotated (portrait/landscape flip): duplication frames then come
    // UNROTATED with rotation metadata, so callers should capture via GDI instead.
    bool Rotated() const { return rotated_; }
    static int GetOutputCount();
    const std::string& LastError() const { return lastError_; }

private:
    bool CreateDeviceForAdapter(ComPtr<IDXGIAdapter1> adapter);

    ComPtr<ID3D11Device> device_;
    ComPtr<ID3D11DeviceContext> context_;
    ComPtr<IDXGIOutputDuplication> duplication_;
    ComPtr<ID3D11Texture2D> acquired_;
    int width_ = 0, height_ = 0;
    int left_ = 0, top_ = 0;
    int outputIndex_ = 0;
    bool frameHeld_ = false;
    bool rotated_ = false;
    std::string lastError_;
};

} // namespace ssl
