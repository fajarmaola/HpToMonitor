using System.Runtime.InteropServices;

namespace SecondScreen.Core;

// Bridges to the IddCx virtual display driver. Creating the software device causes Windows to
// load the driver and enumerate a new monitor (true "Display 2"); removing it restores the
// single-display layout. The actual SwDeviceCreate/Close calls live in the native DLL
// (windows/SecondScreen.Native/SwDevice.cpp) because they use C++ WDF/SwDevice headers.
//
// TODO(hardware): requires the signed IddCx driver installed (docs/DRIVER_SETUP.md) and a real
// Windows machine. If the driver is absent, CreateVirtualDisplay returns false and the host
// falls back to capturing the primary display (still a working stream + touch path).
public sealed class VirtualDisplayController : IDisposable
{
    private const string Dll = "SecondScreen.Native";

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SslCreateVirtualDisplay(int width, int height, int refreshHz);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SslRemoveVirtualDisplay();
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SslIsVirtualDisplayDriverPresent();

    public bool IsActive { get; private set; }

    public bool IsDriverPresent()
    {
        try { return SslIsVirtualDisplayDriverPresent() != 0; }
        catch (DllNotFoundException) { return false; }
    }

    // Returns true if a virtual monitor with the Android resolution was created.
    public bool CreateVirtualDisplay(int width, int height, int refreshHz)
    {
        try
        {
            int rc = SslCreateVirtualDisplay(width, height, refreshHz);
            IsActive = rc == 0;
            if (IsActive) Log.Info($"Virtual display created {width}x{height}@{refreshHz}");
            else Log.Warn($"Virtual display create failed ({rc}); falling back to primary capture");
            return IsActive;
        }
        catch (DllNotFoundException)
        {
            Log.Warn("Native DLL not found; virtual display unavailable, using primary display.");
            return false;
        }
    }

    public void Remove()
    {
        if (!IsActive) return;
        try { SslRemoveVirtualDisplay(); } catch { }
        IsActive = false;
        Log.Info("Virtual display removed");
    }

    public void Dispose() => Remove();
}
