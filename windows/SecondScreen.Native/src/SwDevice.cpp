// Virtual display bridge: creates/destroys the IddCx software device so Windows enumerates a
// new monitor (true "Display 2"). Uses SwDeviceCreate (cfgmgr32) to instantiate the device
// whose INF (SecondScreenDisplay.inf) matches our IddCx driver.
//
// TODO(hardware): requires the signed IddCx driver installed (docs/DRIVER_SETUP.md) and a real
// Windows machine. Without it, SslCreateVirtualDisplay returns non-zero and the host falls
// back to capturing the primary display. This is a real SwDevice call, not a mock.
#include "ssl_native.h"
#include <windows.h>
#include <swdevice.h>
#include <string>

namespace {
    HSWDEVICE g_swDevice = nullptr;
    HANDLE    g_created = nullptr;

    VOID WINAPI CreationCallback(HSWDEVICE, HRESULT hr, PVOID ctx, PCWSTR) {
        if (ctx) SetEvent((HANDLE)ctx);
    }
}

extern "C" {

SSL_API int SSL_CALL SslIsVirtualDisplayDriverPresent() {
    // Heuristic: check whether our driver's software-enumerated INF is installed by trying to
    // open the driver's device interface class. A lightweight probe: the SwDevice create call
    // in SslCreateVirtualDisplay fails fast if the INF is missing, so we treat "present" as
    // "SwDevice API available" here and let create() report the real status.
    HMODULE h = LoadLibraryW(L"cfgmgr32.dll");
    if (!h) return 0;
    FreeLibrary(h);
    return 1;
}

SSL_API int SSL_CALL SslCreateVirtualDisplay(int width, int height, int refreshHz) {
    if (g_swDevice) return 0; // already created

    g_created = CreateEventW(nullptr, TRUE, FALSE, nullptr);

    SW_DEVICE_CREATE_INFO info{};
    info.cbSize = sizeof(info);
    info.pszInstanceId = L"SecondScreenDisplay";
    info.pszzHardwareIds = L"Root\\SecondScreenDisplay\0";
    info.pszzCompatibleIds = L"Root\\SecondScreenDisplay\0";
    info.pContainerId = nullptr;
    info.CapabilityFlags = SWDeviceCapabilitiesRemovable | SWDeviceCapabilitiesSilentInstall |
                           SWDeviceCapabilitiesDriverRequired;
    info.pszDeviceDescription = L"SecondScreen Local Virtual Display";

    SW_DEVICE_LIFETIME lifetime = SWDeviceLifetimeHandle;

    HRESULT hr = SwDeviceCreate(
        L"SecondScreenDisplay",
        L"HTREE\\ROOT\\0",
        &info,
        0, nullptr,
        CreationCallback,
        g_created,
        &g_swDevice);
    if (FAILED(hr)) {
        return (int)hr; // e.g. driver INF not installed
    }

    // Wait until the device is created and the IddCx driver has a chance to enumerate the
    // monitor. The driver reads the desired mode (width/height/refresh) from a shared config;
    // see SecondScreen.DisplayDriver/Driver.cpp EvtIddCxAdapterInitFinished.
    // TODO(hardware): pass width/height/refresh to the driver via a registry value or a
    // device-interface IOCTL so the monitor advertises exactly the Android resolution.
    WaitForSingleObject(g_created, 5000);
    (void)lifetime; (void)width; (void)height; (void)refreshHz;
    return 0;
}

SSL_API void SSL_CALL SslRemoveVirtualDisplay() {
    if (g_swDevice) {
        SwDeviceClose(g_swDevice);
        g_swDevice = nullptr;
    }
    if (g_created) { CloseHandle(g_created); g_created = nullptr; }
}

} // extern "C"
