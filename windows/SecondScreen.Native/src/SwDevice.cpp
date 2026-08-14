// Virtual display bridge: creates/destroys the IddCx software device so Windows enumerates a
// new monitor (true "Display 2"). SwDeviceCreate/SwDeviceClose are resolved DYNAMICALLY via
// GetProcAddress from cfgmgr32.dll — their import library is not consistently present across
// Windows SDK versions, and dynamic loading also lets the app run/link cleanly when the
// virtual-display feature is unavailable (falls back to primary-display capture).
//
// TODO(hardware): requires the signed IddCx driver installed (docs/DRIVER_SETUP.md) and a real
// Windows machine. This is a real SwDevice call (resolved at runtime), not a mock.
#include "ssl_native.h"
#include <windows.h>
#include <swdevice.h>
#include <string>

namespace {
    typedef HRESULT (WINAPI *PFN_SwDeviceCreate)(PCWSTR, PCWSTR, const SW_DEVICE_CREATE_INFO*,
        ULONG, const DEVPROPERTY*, SW_DEVICE_CREATE_CALLBACK, PVOID, PHSWDEVICE);
    typedef VOID (WINAPI *PFN_SwDeviceClose)(HSWDEVICE);

    HMODULE           g_cfgmgr = nullptr;
    PFN_SwDeviceCreate g_pSwDeviceCreate = nullptr;
    PFN_SwDeviceClose  g_pSwDeviceClose = nullptr;
    HSWDEVICE g_swDevice = nullptr;
    HANDLE    g_created = nullptr;

    bool LoadSwDevice() {
        if (g_pSwDeviceCreate && g_pSwDeviceClose) return true;
        if (!g_cfgmgr) g_cfgmgr = LoadLibraryW(L"cfgmgr32.dll");
        if (!g_cfgmgr) return false;
        g_pSwDeviceCreate = reinterpret_cast<PFN_SwDeviceCreate>(GetProcAddress(g_cfgmgr, "SwDeviceCreate"));
        g_pSwDeviceClose  = reinterpret_cast<PFN_SwDeviceClose>(GetProcAddress(g_cfgmgr, "SwDeviceClose"));
        return g_pSwDeviceCreate && g_pSwDeviceClose;
    }

    VOID WINAPI CreationCallback(HSWDEVICE, HRESULT, PVOID ctx, PCWSTR) {
        if (ctx) SetEvent((HANDLE)ctx);
    }
}

extern "C" {

SSL_API int SSL_CALL SslIsVirtualDisplayDriverPresent() {
    // "Present" here means the SwDevice API is available to attempt device creation. The real
    // status is reported by SslCreateVirtualDisplay (fails fast if the driver INF is missing).
    return LoadSwDevice() ? 1 : 0;
}

SSL_API int SSL_CALL SslCreateVirtualDisplay(int width, int height, int refreshHz) {
    if (g_swDevice) return 0; // already created
    if (!LoadSwDevice()) return -1;

    g_created = CreateEventW(nullptr, TRUE, FALSE, nullptr);

    SW_DEVICE_CREATE_INFO info{};
    info.cbSize = sizeof(info);
    info.pszInstanceId = L"SecondScreenDisplay";
    info.pszzHardwareIds = L"Root\\SecondScreenDisplay\0";
    info.pszzCompatibleIds = L"Root\\SecondScreenDisplay\0";
    info.pContainerId = nullptr;
    info.CapabilityFlags = SWDeviceCapabilitiesRemovable | SWDeviceCapabilitiesSilentInstall |
                           SWDeviceCapabilitiesDriverRequired;
    info.pszDeviceDescription = L"HP ke Monitor Virtual Display";

    HRESULT hr = g_pSwDeviceCreate(
        L"SecondScreenDisplay",
        L"HTREE\\ROOT\\0",
        &info,
        0, nullptr,
        CreationCallback,
        g_created,
        &g_swDevice);
    if (FAILED(hr)) return (int)hr; // e.g. driver INF not installed

    // Wait until the device is created and the IddCx driver enumerates the monitor.
    // TODO(hardware): pass width/height/refresh to the driver via a registry value or a
    // device-interface IOCTL so the monitor advertises exactly the Android resolution.
    WaitForSingleObject(g_created, 5000);
    (void)width; (void)height; (void)refreshHz;
    return 0;
}

SSL_API void SSL_CALL SslRemoveVirtualDisplay() {
    if (g_swDevice && g_pSwDeviceClose) {
        g_pSwDeviceClose(g_swDevice);
        g_swDevice = nullptr;
    }
    if (g_created) { CloseHandle(g_created); g_created = nullptr; }
}

} // extern "C"
