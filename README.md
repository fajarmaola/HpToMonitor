# SecondScreen Local

Turn an Android phone/tablet into a **real secondary display** for a Windows PC — fully
offline (no cloud, no internet dependency for the app itself).

The Windows PC does the rendering + audio. The Android device is a **receiver** for a
Windows display stream, plus an optional touch/input device.

> ⚠️ **Build environment note.** This repository was authored in a Linux container that
> **cannot compile or test** Windows `.exe`, the IddCx kernel driver, or the Android `.apk`
> (those require Windows + Visual Studio + WDK and Android Studio + real hardware
> encoders). The code here is **real, modular, compilable-intent source** meant to be built
> on your own machines using the instructions in `/docs`. Where a feature genuinely needs
> hardware/OS that is unavailable, it is marked with an explicit `TODO(hardware)` and an
> explanation — never a fake mockup.

## Repository layout

```
/shared/protocol        Canonical wire protocol (spec + C#, Kotlin, C++ mirrors)
/windows
  /SecondScreen.Core         C# .NET — orchestration: discovery, pairing, transport, session
  /SecondScreen.Desktop      C# WPF — dashboard UI
  /SecondScreen.Native       C++ — DXGI capture + Media Foundation H.264 encoder (DLL)
  /SecondScreen.DisplayDriver C++/WDK — IddCx indirect display (virtual "Display 2")
/android/app            Kotlin — receiver: discovery, pairing, MediaCodec decode → Surface, touch
/docs                   README, ARCHITECTURE, PROTOCOL, BUILD_WINDOWS, BUILD_ANDROID, DRIVER_SETUP
```

## Status by milestone

| Milestone | Component | State |
|-----------|-----------|-------|
| M1 | Protocol, discovery, pairing (ECDH+PIN), connection mgr, heartbeat, reconnect | Source complete |
| M2 | Android receiver (MediaCodec → Surface, immersive, touch) | Source complete |
| M3 | Windows stream host (capture, MF H.264 encode, transport, WPF UI) | Source complete |
| M4 | IddCx virtual display driver | Source complete (needs WDK build + signing) |
| M5 | Touch → Display 2 coordinate mapping (SendInput) | Source complete |

"Source complete" = code is written to compile and run on the target toolchain; it has
**not** been executed in this authoring environment. See per-file `TODO(hardware)` notes.

## Quick start (on your machines)

1. Windows: build `SecondScreen.Native` (C++), then `SecondScreen.Desktop` (C#). See
   `docs/BUILD_WINDOWS.md`. Optionally build + install the IddCx driver
   (`docs/DRIVER_SETUP.md`).
2. Android: open `/android` in Android Studio, build `SecondScreenLocal.apk`. See
   `docs/BUILD_ANDROID.md`.
3. Put both devices on the same LAN (or connect via USB later). Launch both apps, pair
   using the 6-digit code shown on Windows.

## Audio

By design the **first version does not stream audio** to Android. Audio stays on Windows.
This reduces bandwidth and avoids A/V sync problems. The video pipeline targets minimal
latency so video on Android stays close to Windows audio.

## License

Provided as project source for the requester. Add your preferred license before shipping.
