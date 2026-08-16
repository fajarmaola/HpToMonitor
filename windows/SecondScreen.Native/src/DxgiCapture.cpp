#include "DxgiCapture.h"
#include <vector>
#include <cstdio>

namespace ssl {

// Enumerate (adapter, output) pairs so an index maps to a specific display.
static bool ResolveOutput(int index, ComPtr<IDXGIAdapter1>& adapterOut,
                          ComPtr<IDXGIOutput1>& outputOut) {
    ComPtr<IDXGIFactory1> factory;
    if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) return false;

    int running = 0;
    for (UINT a = 0; ; ++a) {
        ComPtr<IDXGIAdapter1> adapter;
        if (factory->EnumAdapters1(a, &adapter) == DXGI_ERROR_NOT_FOUND) break;
        for (UINT o = 0; ; ++o) {
            ComPtr<IDXGIOutput> output;
            if (adapter->EnumOutputs(o, &output) == DXGI_ERROR_NOT_FOUND) break;
            if (running == index) {
                ComPtr<IDXGIOutput1> out1;
                if (FAILED(output.As(&out1))) return false;
                adapterOut = adapter;
                outputOut = out1;
                return true;
            }
            ++running;
        }
    }
    return false;
}

int DxgiCapture::GetOutputCount() {
    ComPtr<IDXGIFactory1> factory;
    if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) return 0;
    int count = 0;
    for (UINT a = 0; ; ++a) {
        ComPtr<IDXGIAdapter1> adapter;
        if (factory->EnumAdapters1(a, &adapter) == DXGI_ERROR_NOT_FOUND) break;
        for (UINT o = 0; ; ++o) {
            ComPtr<IDXGIOutput> output;
            if (adapter->EnumOutputs(o, &output) == DXGI_ERROR_NOT_FOUND) break;
            ++count;
        }
    }
    return count;
}

bool DxgiCapture::CreateDeviceForAdapter(ComPtr<IDXGIAdapter1> adapter) {
    D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0 };
    D3D_FEATURE_LEVEL got;
    HRESULT hr = D3D11CreateDevice(adapter.Get(), D3D_DRIVER_TYPE_UNKNOWN, nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
        levels, 2, D3D11_SDK_VERSION, &device_, &got, &context_);
    if (FAILED(hr)) { lastError_ = "D3D11CreateDevice failed"; return false; }
    return true;
}

bool DxgiCapture::Initialize(int outputIndex) {
    outputIndex_ = outputIndex;
    ComPtr<IDXGIAdapter1> adapter;
    ComPtr<IDXGIOutput1> output1;
    if (!ResolveOutput(outputIndex, adapter, output1)) {
        lastError_ = "output index " + std::to_string(outputIndex) +
                     " not found (is the virtual display active/extended?)";
        return false;
    }
    if (!CreateDeviceForAdapter(adapter)) return false;

    DXGI_OUTPUT_DESC desc{};
    output1->GetDesc(&desc);
    width_  = desc.DesktopCoordinates.right - desc.DesktopCoordinates.left;
    height_ = desc.DesktopCoordinates.bottom - desc.DesktopCoordinates.top;
    left_   = desc.DesktopCoordinates.left;
    top_    = desc.DesktopCoordinates.top;
    if (!desc.AttachedToDesktop || width_ <= 0 || height_ <= 0) {
        lastError_ = "output " + std::to_string(outputIndex) +
                     " is not attached to the desktop (inactive display — 'Show only on 1'?)";
        return false;
    }

    char err[128] = {0};
    HRESULT hr = E_FAIL;

    // Prefer DuplicateOutput1 (DXGI 1.5) with an explicit format list — IddCx/virtual displays
    // frequently reject the legacy DuplicateOutput but accept DuplicateOutput1.
    ComPtr<IDXGIOutput5> output5;
    if (SUCCEEDED(output1.As(&output5))) {
        const DXGI_FORMAT fmts[] = {
            DXGI_FORMAT_B8G8R8A8_UNORM,
            DXGI_FORMAT_R8G8B8A8_UNORM,
            DXGI_FORMAT_R10G10B10A2_UNORM,
            DXGI_FORMAT_R16G16B16A16_FLOAT,
        };
        hr = output5->DuplicateOutput1(device_.Get(), 0,
                 (UINT)(sizeof(fmts) / sizeof(fmts[0])), fmts, &duplication_);
        if (FAILED(hr)) {
            snprintf(err, sizeof(err), "DuplicateOutput1=0x%08X; ", (unsigned)hr);
            duplication_.Reset();
        }
    }

    // Fallback to the classic path (also covers non-per-monitor-DPI-aware processes where
    // DuplicateOutput1 returns E_INVALIDARG).
    if (!duplication_) {
        hr = output1->DuplicateOutput(device_.Get(), &duplication_);
        if (FAILED(hr)) {
            char err2[192];
            snprintf(err2, sizeof(err2), "%sDuplicateOutput=0x%08X", err, (unsigned)hr);
            lastError_ = err2; // 0x887A0004 = DXGI_ERROR_UNSUPPORTED, 0x80070005 = E_ACCESSDENIED
            return false;
        }
    }
    DXGI_OUTDUPL_DESC dupDesc{};
    duplication_->GetDesc(&dupDesc);
    rotated_ = dupDesc.Rotation != DXGI_MODE_ROTATION_IDENTITY &&
               dupDesc.Rotation != DXGI_MODE_ROTATION_UNSPECIFIED;
    return true;
}

int DxgiCapture::AcquireFrame(ComPtr<ID3D11Texture2D>& out, int timeoutMs) {
    if (!duplication_) return -1;
    if (frameHeld_) ReleaseFrame();

    DXGI_OUTDUPL_FRAME_INFO info{};
    ComPtr<IDXGIResource> resource;
    HRESULT hr = duplication_->AcquireNextFrame(timeoutMs, &info, &resource);
    if (hr == DXGI_ERROR_WAIT_TIMEOUT) return 0;
    if (hr == DXGI_ERROR_ACCESS_LOST) { lastError_ = "access lost"; return -1; }
    if (FAILED(hr)) { lastError_ = "AcquireNextFrame failed"; return -1; }

    frameHeld_ = true;
    if (FAILED(resource.As(&acquired_))) { lastError_ = "QI ID3D11Texture2D failed"; return -1; }
    out = acquired_;
    return 1;
}

void DxgiCapture::ReleaseFrame() {
    if (frameHeld_ && duplication_) {
        duplication_->ReleaseFrame();
        frameHeld_ = false;
        acquired_.Reset();
    }
}

void DxgiCapture::Shutdown() {
    ReleaseFrame();
    duplication_.Reset();
    context_.Reset();
    device_.Reset();
}

} // namespace ssl
