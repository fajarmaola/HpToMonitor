// SecondScreen Local — IddCx indirect display driver (implementation).
//
// This is a REAL IddCx driver skeleton following Microsoft's IddSampleDriver. When installed
// (docs/DRIVER_SETUP.md) and instantiated by SwDevice (SecondScreen.Native/SwDevice.cpp),
// Windows enumerates a genuine additional monitor and offers "Extend these displays".
//
// TODO(hardware): must be built with the WDK and signed; cannot be compiled/tested in the
// authoring Linux container. The frame path in SwapChainProcessor::ProcessFrame is where the
// OS-rendered Display-2 content is delivered — that texture is what gets encoded and streamed
// to Android. Wiring it into SecondScreen.Native's encoder via a shared texture (zero-copy)
// is the optimal integration and is marked below.

#include "Driver.h"

using namespace Microsoft::WRL;
using namespace ssl_driver;

// Advertised monitor modes. The primary entry should match the Android resolution; the app
// passes it via SwDevice today as a static default, extendable to a dynamic value.
struct DisplayMode { DWORD Width; DWORD Height; DWORD VSync; };
static const DisplayMode s_modes[] = {
    { 1080, 2400, 60 },   // typical phone portrait (default target)
    { 1920, 1080, 60 },
    { 1600,  900, 60 },
    { 1280,  720, 60 },
};

// Valid 128-byte EDID 1.4 base block (checksum verified: sum(bytes) % 256 == 0).
// Manufacturer "SSL", monitor name "SecondScreen", preferred DTD 1920x1080@60, range limits
// 50-75Hz / 30-160kHz. IddCx requires a real EDID for the monitor to enumerate; the actual
// selectable modes are still supplied by the mode callbacks below.
static const BYTE s_edid[128] = {
    0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x4E, 0x6C, 0x01, 0x00,
    0x01, 0x00, 0x00, 0x00, 0x01, 0x22, 0x01, 0x04, 0xA5, 0x07, 0x0F, 0x78,
    0x06, 0xEE, 0x91, 0xA3, 0x54, 0x4C, 0x99, 0x26, 0x0F, 0x50, 0x54, 0x00,
    0x00, 0x00, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
    0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x02, 0x3A, 0x80, 0x18, 0x71, 0x38,
    0x2D, 0x40, 0x58, 0x2C, 0x45, 0x00, 0x58, 0xC2, 0x10, 0x00, 0x00, 0x1E,
    0x00, 0x00, 0x00, 0xFC, 0x00, 0x53, 0x65, 0x63, 0x6F, 0x6E, 0x64, 0x53,
    0x63, 0x72, 0x65, 0x65, 0x6E, 0x0A, 0x00, 0x00, 0x00, 0xFD, 0x00, 0x32,
    0x4B, 0x1E, 0xA0, 0x1E, 0x00, 0x0A, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20,
    0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20,
    0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x00, 0x38
};

extern "C" DRIVER_INITIALIZE DriverEntry;

static EVT_WDF_DRIVER_DEVICE_ADD EvtDeviceAdd;
static EVT_WDF_DEVICE_D0_ENTRY EvtDeviceD0Entry;
static EVT_IDD_CX_ADAPTER_INIT_FINISHED EvtIddCxAdapterInitFinished;
static EVT_IDD_CX_ADAPTER_COMMIT_MODES EvtIddCxAdapterCommitModes;
static EVT_IDD_CX_PARSE_MONITOR_DESCRIPTION EvtIddCxParseMonitorDescription;
static EVT_IDD_CX_MONITOR_GET_DEFAULT_DESCRIPTION_MODES EvtIddCxMonitorGetDefaultModes;
static EVT_IDD_CX_MONITOR_QUERY_TARGET_MODES EvtIddCxMonitorQueryModes;
static EVT_IDD_CX_MONITOR_ASSIGN_SWAPCHAIN EvtIddCxMonitorAssignSwapChain;
static EVT_IDD_CX_MONITOR_UNASSIGN_SWAPCHAIN EvtIddCxMonitorUnassignSwapChain;

// ---------------------------------------------------------------------------------------
// Direct3D device that renders/reads our virtual monitor's swap chain.
HRESULT Direct3DDevice::Init() {
    HRESULT hr = CreateDXGIFactory2(0, IID_PPV_ARGS(&DxgiFactory));
    if (FAILED(hr)) return hr;
    hr = DxgiFactory->EnumAdapterByLuid(AdapterLuid, IID_PPV_ARGS(&Adapter));
    if (FAILED(hr)) return hr;
    hr = D3D11CreateDevice(Adapter.Get(), D3D_DRIVER_TYPE_UNKNOWN, nullptr, 0, nullptr, 0,
                           D3D11_SDK_VERSION, &Device, nullptr, &DeviceContext);
    if (FAILED(hr)) {
        // Fallback to WARP if a hardware adapter is unavailable.
        hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0, nullptr, 0,
                               D3D11_SDK_VERSION, &Device, nullptr, &DeviceContext);
    }
    return hr;
}

// ---------------------------------------------------------------------------------------
// Swap-chain processor: pulls the OS-composited frames for our monitor.
SwapChainProcessor::SwapChainProcessor(IDDCX_SWAPCHAIN sc, std::shared_ptr<Direct3DDevice> dev,
                                       HANDLE newFrameEvent)
    : m_swapChain(sc), m_device(std::move(dev)), m_newFrameEvent(newFrameEvent) {
    m_terminate.Attach(CreateEvent(nullptr, FALSE, FALSE, nullptr));
    m_thread = CreateThread(nullptr, 0, RunThread, this, 0, nullptr);
}

SwapChainProcessor::~SwapChainProcessor() {
    SetEvent(m_terminate.Get());
    if (m_thread) { WaitForSingleObject(m_thread, INFINITE); CloseHandle(m_thread); }
}

DWORD CALLBACK SwapChainProcessor::RunThread(LPVOID arg) {
    reinterpret_cast<SwapChainProcessor*>(arg)->Run();
    return 0;
}

void SwapChainProcessor::Run() {
    // Raise thread priority for smooth frame delivery (latency target).
    SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL);
    RunCore();
    // Report we are done so IddCx can tear down cleanly.
    WdfObjectDelete((WDFOBJECT)m_swapChain);
    m_swapChain = nullptr;
}

void SwapChainProcessor::RunCore() {
    ComPtr<IDXGIDevice> dxgiDevice;
    if (FAILED(m_device->Device.As(&dxgiDevice))) return;

    IDARG_IN_SWAPCHAINSETDEVICE setDevice = {};
    setDevice.pDevice = dxgiDevice.Get();
    if (FAILED(IddCxSwapChainSetDevice(m_swapChain, &setDevice))) return;

    for (;;) {
        ComPtr<IDXGIResource> acquiredBuffer;
        IDARG_OUT_RELEASEANDACQUIREBUFFER buffer = {};
        HRESULT hr = IddCxSwapChainReleaseAndAcquireBuffer(m_swapChain, &buffer);

        if (hr == E_PENDING) {
            // No new frame yet: wait for either a new frame or termination.
            HANDLE waits[] = { m_newFrameEvent, m_terminate.Get() };
            DWORD w = WaitForMultipleObjects(ARRAYSIZE(waits), waits, FALSE, 16);
            if (w == WAIT_OBJECT_0 + 1) break; // terminate
            continue;
        }
        if (FAILED(hr)) break;

        // buffer.MetaData.pSurface is the frame the OS rendered onto our virtual monitor.
        ComPtr<ID3D11Texture2D> frame;
        if (SUCCEEDED(buffer.MetaData.pSurface->QueryInterface(IID_PPV_ARGS(&frame))))
            ProcessFrame(frame);

        // Signal we finished with this buffer (current IddCx API takes only the swap chain).
        IddCxSwapChainFinishedProcessingFrame(m_swapChain);

        if (WaitForSingleObject(m_terminate.Get(), 0) == WAIT_OBJECT_0) break;
    }
}

void SwapChainProcessor::ProcessFrame(const ComPtr<ID3D11Texture2D>& frame) {
    // TODO(hardware): This texture is the live Display-2 content. The optimal path is to open
    // it as a shared resource (IDXGIResource1::CreateSharedHandle) and hand the shared handle
    // to SecondScreen.Native's encoder so it can encode without a CPU copy (zero-copy).
    // Until that shared-texture handshake is validated on real GPUs, the app uses the
    // DXGI Desktop Duplication fallback (SecondScreen.Native/DxgiCapture.cpp), which captures
    // the same monitor by output index. Both feed the identical MF H.264 encoder.
    UNREFERENCED_PARAMETER(frame);
}

// ---------------------------------------------------------------------------------------
// Mode reporting helpers.
static void FillMonitorMode(DISPLAYCONFIG_VIDEO_SIGNAL_INFO& mode, const DisplayMode& m) {
    mode = {};
    mode.totalSize.cx = mode.activeSize.cx = m.Width;
    mode.totalSize.cy = mode.activeSize.cy = m.Height;
    mode.vSyncFreq.Numerator = m.VSync * 1000;
    mode.vSyncFreq.Denominator = 1000;
    mode.hSyncFreq.Numerator = m.VSync * m.Height;
    mode.hSyncFreq.Denominator = 1;
    mode.scanLineOrdering = DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE;
    mode.pixelRate = (UINT64)m.Width * m.Height * m.VSync;
}

// ---------------------------------------------------------------------------------------
extern "C" NTSTATUS DriverEntry(PDRIVER_OBJECT pDriverObject, PUNICODE_STRING pRegistryPath) {
    WDF_DRIVER_CONFIG config;
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    WDF_DRIVER_CONFIG_INIT(&config, EvtDeviceAdd);
    config.DriverPoolTag = 'lSS2';

    return WdfDriverCreate(pDriverObject, pRegistryPath, &attributes, &config, WDF_NO_HANDLE);
}

NTSTATUS EvtDeviceAdd(WDFDRIVER, PWDFDEVICE_INIT pDeviceInit) {
    // Register IddCx first (it edits the device init).
    IDD_CX_CLIENT_CONFIG cfg;
    IDD_CX_CLIENT_CONFIG_INIT(&cfg);
    cfg.EvtIddCxAdapterInitFinished = EvtIddCxAdapterInitFinished;
    cfg.EvtIddCxAdapterCommitModes = EvtIddCxAdapterCommitModes;
    cfg.EvtIddCxParseMonitorDescription = EvtIddCxParseMonitorDescription;
    cfg.EvtIddCxMonitorGetDefaultDescriptionModes = EvtIddCxMonitorGetDefaultModes;
    cfg.EvtIddCxMonitorQueryTargetModes = EvtIddCxMonitorQueryModes;
    cfg.EvtIddCxMonitorAssignSwapChain = EvtIddCxMonitorAssignSwapChain;
    cfg.EvtIddCxMonitorUnassignSwapChain = EvtIddCxMonitorUnassignSwapChain;

    NTSTATUS status = IddCxDeviceInitConfig(pDeviceInit, &cfg);
    if (!NT_SUCCESS(status)) return status;

    WDF_PNPPOWER_EVENT_CALLBACKS pnp;
    WDF_PNPPOWER_EVENT_CALLBACKS_INIT(&pnp);
    pnp.EvtDeviceD0Entry = EvtDeviceD0Entry;
    WdfDeviceInitSetPnpPowerEventCallbacks(pDeviceInit, &pnp);

    WDF_OBJECT_ATTRIBUTES attr;
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attr, AdapterContext);
    WDFDEVICE device;
    status = WdfDeviceCreate(&pDeviceInit, &attr, &device);
    if (!NT_SUCCESS(status)) return status;

    return IddCxDeviceInitialize(device);
}

NTSTATUS EvtDeviceD0Entry(WDFDEVICE device, WDF_POWER_DEVICE_STATE) {
    auto* ctx = GetAdapterContext(device);
    if (ctx->Adapter != nullptr) return STATUS_SUCCESS; // already initialized

    // Create the IddCx adapter. This kicks off EvtIddCxAdapterInitFinished, where we create
    // and plug in the monitor. Without this call the OS never enumerates our display — this
    // was the missing link in the first revision.
    IDDCX_ADAPTER_CAPS caps = {};
    caps.Size = sizeof(caps);
    caps.MaxMonitorsSupported = 1; // single-display MVP (array-ready)
    caps.EndPointDiagnostics.Size = sizeof(caps.EndPointDiagnostics);
    caps.EndPointDiagnostics.GammaSupport = IDDCX_FEATURE_IMPLEMENTATION_NONE;
    caps.EndPointDiagnostics.TransmissionType = IDDCX_TRANSMISSION_TYPE_WIRED_OTHER;
    caps.EndPointDiagnostics.pEndPointFriendlyName = L"SecondScreen Local Display";
    caps.EndPointDiagnostics.pEndPointManufacturerName = L"SecondScreen Local";
    caps.EndPointDiagnostics.pEndPointModelName = L"Virtual Display";

    // Firmware/hardware versions are required by IddCx; omitting them fails adapter power-up
    // (Code 10 / STATUS_DEVICE_POWER_FAILURE).
    IDDCX_ENDPOINT_VERSION version = {};
    version.Size = sizeof(version);
    version.MajorVer = 1;
    caps.EndPointDiagnostics.pFirmwareVersion = &version;
    caps.EndPointDiagnostics.pHardwareVersion = &version;

    WDF_OBJECT_ATTRIBUTES adapterAttr;
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&adapterAttr, AdapterContext);

    IDARG_IN_ADAPTER_INIT init = {};
    init.WdfDevice = device;
    init.pCaps = &caps;
    init.ObjectAttributes = &adapterAttr;

    IDARG_OUT_ADAPTER_INIT out = {};
    NTSTATUS status = IddCxAdapterInitAsync(&init, &out);
    if (NT_SUCCESS(status)) ctx->Adapter = out.AdapterObject;
    return status;
}

NTSTATUS EvtIddCxAdapterInitFinished(IDDCX_ADAPTER adapter, const IDARG_IN_ADAPTER_INIT_FINISHED* args) {
    if (!NT_SUCCESS(args->AdapterInitStatus)) return STATUS_SUCCESS;

    auto* ctx = GetAdapterContext(adapter);
    ctx->Adapter = adapter;
    // Create and plug in one monitor (single-display MVP; array-ready for multi-display).
    // A valid 128-byte EDID (s_edid) is supplied so Windows enumerates the monitor; the
    // selectable modes come from the mode callbacks below.
    IDDCX_MONITOR_INFO monitorInfo = {};
    monitorInfo.Size = sizeof(monitorInfo);
    monitorInfo.MonitorType = DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL;
    monitorInfo.ConnectorIndex = 0;
    monitorInfo.MonitorDescription.Size = sizeof(monitorInfo.MonitorDescription);
    monitorInfo.MonitorDescription.Type = IDDCX_MONITOR_DESCRIPTION_TYPE_EDID;
    monitorInfo.MonitorDescription.DataSize = sizeof(s_edid);
    monitorInfo.MonitorDescription.pData = const_cast<BYTE*>(s_edid);
    CoCreateGuid(&monitorInfo.MonitorContainerId);

    IDARG_IN_MONITORCREATE in = {};
    in.ObjectAttributes = WDF_NO_OBJECT_ATTRIBUTES;
    WDF_OBJECT_ATTRIBUTES monAttr;
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&monAttr, MonitorContext);
    in.ObjectAttributes = &monAttr;
    in.pMonitorInfo = &monitorInfo;

    IDARG_OUT_MONITORCREATE out = {};
    NTSTATUS status = IddCxMonitorCreate(adapter, &in, &out);
    if (!NT_SUCCESS(status)) return status;

    IDARG_OUT_MONITORARRIVAL arrival = {};
    status = IddCxMonitorArrival(out.MonitorObject, &arrival);
    UNREFERENCED_PARAMETER(ctx);
    return status;
}

NTSTATUS EvtIddCxAdapterCommitModes(IDDCX_ADAPTER, const IDARG_IN_COMMITMODES*) {
    // Accept whatever path the OS commits (single monitor MVP).
    return STATUS_SUCCESS;
}

NTSTATUS EvtIddCxParseMonitorDescription(const IDARG_IN_PARSEMONITORDESCRIPTION* in,
                                         IDARG_OUT_PARSEMONITORDESCRIPTION* out) {
    out->MonitorModeBufferOutputCount = ARRAYSIZE(s_modes);
    if (in->MonitorModeBufferInputCount < ARRAYSIZE(s_modes)) {
        return (in->MonitorModeBufferInputCount > 0) ? STATUS_BUFFER_TOO_SMALL : STATUS_SUCCESS;
    }
    for (DWORD i = 0; i < ARRAYSIZE(s_modes); ++i) {
        in->pMonitorModes[i].Size = sizeof(IDDCX_MONITOR_MODE);
        in->pMonitorModes[i].Origin = IDDCX_MONITOR_MODE_ORIGIN_MONITORDESCRIPTOR;
        FillMonitorMode(in->pMonitorModes[i].MonitorVideoSignalInfo, s_modes[i]);
    }
    out->PreferredMonitorModeIdx = 0;
    return STATUS_SUCCESS;
}

NTSTATUS EvtIddCxMonitorGetDefaultModes(IDDCX_MONITOR, const IDARG_IN_GETDEFAULTDESCRIPTIONMODES* in,
                                        IDARG_OUT_GETDEFAULTDESCRIPTIONMODES* out) {
    out->DefaultMonitorModeBufferOutputCount = ARRAYSIZE(s_modes);
    if (in->DefaultMonitorModeBufferInputCount < ARRAYSIZE(s_modes)) return STATUS_SUCCESS;
    for (DWORD i = 0; i < ARRAYSIZE(s_modes); ++i) {
        in->pDefaultMonitorModes[i].Size = sizeof(IDDCX_MONITOR_MODE);
        in->pDefaultMonitorModes[i].Origin = IDDCX_MONITOR_MODE_ORIGIN_DRIVER;
        FillMonitorMode(in->pDefaultMonitorModes[i].MonitorVideoSignalInfo, s_modes[i]);
    }
    return STATUS_SUCCESS;
}

NTSTATUS EvtIddCxMonitorQueryModes(IDDCX_MONITOR, const IDARG_IN_QUERYTARGETMODES* in,
                                   IDARG_OUT_QUERYTARGETMODES* out) {
    out->TargetModeBufferOutputCount = ARRAYSIZE(s_modes);
    if (in->TargetModeBufferInputCount < ARRAYSIZE(s_modes)) return STATUS_SUCCESS;
    for (DWORD i = 0; i < ARRAYSIZE(s_modes); ++i) {
        in->pTargetModes[i].Size = sizeof(IDDCX_TARGET_MODE);
        FillMonitorMode(in->pTargetModes[i].TargetVideoSignalInfo.targetVideoSignalInfo, s_modes[i]);
    }
    return STATUS_SUCCESS;
}

NTSTATUS EvtIddCxMonitorAssignSwapChain(IDDCX_MONITOR monitor, const IDARG_IN_SETSWAPCHAIN* in) {
    auto* ctx = GetMonitorContext(monitor);
    ctx->Processor.reset(); // release any previous

    // Create the D3D device on the render adapter the OS chose.
    auto device = std::make_shared<Direct3DDevice>(in->RenderAdapterLuid);
    if (FAILED(device->Init())) {
        // Signal a fault so the OS can retry on another adapter.
        WdfObjectDelete((WDFOBJECT)in->hSwapChain);
        return STATUS_SUCCESS;
    }
    ctx->Processor = std::make_unique<SwapChainProcessor>(in->hSwapChain, device, in->hNextSurfaceAvailable);
    return STATUS_SUCCESS;
}

NTSTATUS EvtIddCxMonitorUnassignSwapChain(IDDCX_MONITOR monitor) {
    auto* ctx = GetMonitorContext(monitor);
    ctx->Processor.reset();
    return STATUS_SUCCESS;
}
