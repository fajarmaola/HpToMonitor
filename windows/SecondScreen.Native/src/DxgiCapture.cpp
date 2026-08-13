#include "DxgiCapture.h"
#include <vector>

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
        lastError_ = "output index not found (is the virtual display active?)";
        return false;
    }
    if (!CreateDeviceForAdapter(adapter)) return false;

    DXGI_OUTPUT_DESC desc{};
    output1->GetDesc(&desc);
    width_  = desc.DesktopCoordinates.right - desc.DesktopCoordinates.left;
    height_ = desc.DesktopCoordinates.bottom - desc.DesktopCoordinates.top;

    HRESULT hr = output1->DuplicateOutput(device_.Get(), &duplication_);
    if (FAILED(hr)) {
        // E_ACCESSDENIED often means a secure/UAC desktop or another duplicator holds it.
        lastError_ = "DuplicateOutput failed (0x" + std::to_string((unsigned)hr) + ")";
        return false;
    }
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
