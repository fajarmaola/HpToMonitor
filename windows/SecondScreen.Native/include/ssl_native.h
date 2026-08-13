// SecondScreen.Native — C ABI consumed by SecondScreen.Core via P/Invoke (NativeInterop.cs).
// All functions are extern "C" + __cdecl so the managed DllImport signatures match exactly.
#pragma once

#ifdef _WIN32
  #define SSL_API __declspec(dllexport)
  #define SSL_CALL __cdecl
#else
  #define SSL_API
  #define SSL_CALL
#endif

#include <cstdint>

extern "C" {

// Encoded H.264 access unit callback. Buffer is valid only for the duration of the call.
typedef void (SSL_CALL *SslEncodedFrameCallback)(
    uint32_t frameId, uint64_t timestampUs, int isKeyframe,
    const uint8_t* data, int dataLen, void* user);

// ---- Capture + encode ----------------------------------------------------------------
SSL_API int  SSL_CALL SslNativeGetOutputCount();
SSL_API int  SSL_CALL SslNativeStart(int outputIndex, int fps, int bitrateKbps,
                                     int useHardware, SslEncodedFrameCallback cb, void* user);
SSL_API void SSL_CALL SslNativeRequestKeyframe();
SSL_API void SSL_CALL SslNativeSetBitrate(int kbps);
SSL_API void SSL_CALL SslNativeSetFps(int fps);
SSL_API void SSL_CALL SslNativeStop();
SSL_API const char* SSL_CALL SslNativeLastError();

// ---- Virtual display (IddCx) ---------------------------------------------------------
// Creates a software device that loads the IddCx driver -> Windows enumerates a new monitor.
SSL_API int  SSL_CALL SslCreateVirtualDisplay(int width, int height, int refreshHz);
SSL_API void SSL_CALL SslRemoveVirtualDisplay();
SSL_API int  SSL_CALL SslIsVirtualDisplayDriverPresent();

} // extern "C"
