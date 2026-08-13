using System.Runtime.InteropServices;

namespace SecondScreen.Core;

// P/Invoke bridge to SecondScreen.Native.dll (C++: DXGI capture + Media Foundation H.264).
// The C ABI is defined in windows/SecondScreen.Native/include/ssl_native.h.
//
// The native side runs its own capture+encode loop on a dedicated thread and delivers each
// encoded access unit through the callback. We never copy frames into managed Bitmaps.
public static class NativeInterop
{
    private const string Dll = "SecondScreen.Native"; // resolved as SecondScreen.Native.dll

    // Callback: encoded Annex-B H.264 frame ready.
    //   frameId       monotonic frame counter
    //   timestampUs   host capture timestamp (microseconds)
    //   isKeyframe    1 if IDR
    //   data/dataLen  pointer+length to the encoded bytes (valid only during the call)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void EncodedFrameCallback(uint frameId, ulong timestampUs, int isKeyframe,
        IntPtr data, int dataLen, IntPtr user);

    // Returns number of connected display outputs (adapters * outputs). Used to pick which
    // display to capture (the virtual display once the IddCx driver is installed).
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SslNativeGetOutputCount();

    // Start capture+encode of the given output index. Returns 0 on success, negative on error.
    // useHardware != 0 requests a hardware MFT encoder (falls back to software internally).
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SslNativeStart(int outputIndex, int fps, int bitrateKbps,
        int useHardware, EncodedFrameCallback cb, IntPtr user);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SslNativeRequestKeyframe();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SslNativeSetBitrate(int kbps);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SslNativeSetFps(int fps);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SslNativeStop();

    // Query the last error string (thread-local) from the native layer.
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SslNativeLastError();

    public static string LastError()
    {
        IntPtr p = SslNativeLastError();
        return p == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(p) ?? "";
    }
}
