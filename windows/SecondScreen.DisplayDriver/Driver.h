// SecondScreen Local — IddCx indirect display driver (header).
// Built on Microsoft's IddCx framework (Indirect Display Driver). Structure follows the
// official IddSampleDriver; trimmed to the pieces SecondScreen needs.
#pragma once

#include <windows.h>
#include <bugcodes.h>
#include <wudfwdm.h>
#include <wdf.h>
#include <iddcx.h>

#include <dxgi1_5.h>
#include <d3d11_2.h>
#include <wrl.h>
#include <memory>
#include <vector>

namespace ssl_driver {

// Owns the D3D device used to receive the OS-provided swap-chain frames for our monitor.
struct Direct3DDevice {
    Direct3DDevice() = default;
    explicit Direct3DDevice(LUID adapterLuid) : AdapterLuid(adapterLuid) {}
    HRESULT Init();

    LUID AdapterLuid{};
    Microsoft::WRL::ComPtr<IDXGIFactory5> DxgiFactory;
    Microsoft::WRL::ComPtr<IDXGIAdapter1> Adapter;
    Microsoft::WRL::ComPtr<ID3D11Device> Device;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> DeviceContext;
};

// Pulls frames from the IddCx swap chain on a dedicated thread. Each acquired frame is the
// content Windows rendered onto our virtual monitor — i.e. exactly what must be streamed to
// Android. The frames are handed to the capture/encode path (see ProcessFrame()).
class SwapChainProcessor {
public:
    SwapChainProcessor(IDDCX_SWAPCHAIN swapChain, std::shared_ptr<Direct3DDevice> device,
                       HANDLE newFrameEvent);
    ~SwapChainProcessor();

private:
    static DWORD CALLBACK RunThread(LPVOID arg);
    void Run();
    void RunCore();
    void ProcessFrame(const Microsoft::WRL::ComPtr<ID3D11Texture2D>& frame);

    IDDCX_SWAPCHAIN m_swapChain;
    std::shared_ptr<Direct3DDevice> m_device;
    HANDLE m_newFrameEvent;
    Microsoft::WRL::Wrappers::HandleT<Microsoft::WRL::Wrappers::HandleTraits::HANDLENullTraits> m_terminate;
    HANDLE m_thread = nullptr;
};

struct MonitorContext {
    IDDCX_MONITOR Monitor = nullptr;
    std::unique_ptr<SwapChainProcessor> Processor;
};

struct AdapterContext {
    IDDCX_ADAPTER Adapter = nullptr;
    std::shared_ptr<Direct3DDevice> Device;
};

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(AdapterContext, GetAdapterContext);
WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(MonitorContext, GetMonitorContext);

} // namespace ssl_driver
