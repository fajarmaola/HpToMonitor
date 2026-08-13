# SecondScreen Local — PRD / Build Log

## Original problem statement
Production-ready **offline** desktop + Android app that turns an Android phone/tablet into a
real **secondary extended display** for a Windows PC (not screen mirroring). Windows renders
+ keeps audio; Android is a receiver for a Windows display stream + optional touch/mouse/
keyboard bridge. No cloud/internet dependency for the app. Final artifacts: `.exe` +
`.apk`. Full spec captured in conversation (connection modes USB/Wi-Fi Direct/LAN/Bluetooth-
control, IddCx virtual display, low-latency H.264 pipeline, pairing/security, monitor mode,
adaptive quality, statistics overlay, phased MVP).

## Environment constraint (important)
Authored in a **Linux container** that CANNOT compile/test Windows `.exe`, the IddCx driver,
or the Android `.apk` (needs Windows + VS + WDK + signing; Android Studio + real hardware).
Therefore: delivered as **real, modular, compilable-intent source** to build on the user's
own machines. Nothing here was executed/compiled in this environment. No web mockup was
created (per the user's explicit engineering rule).

## Architecture (delivered)
- `/shared/protocol` — canonical wire protocol + C#/Kotlin/C++ constant mirrors.
- `/windows/SecondScreen.Core` (C#/.NET 8) — discovery (UDP), pairing (P-256 ECDH + PIN +
  HKDF + AES-256-GCM), `ITransport` (LAN done; USB/Wi-Fi Direct stubbed with TODO(hardware)),
  `SessionManager` state machine, video packetizer, `VideoStreamer` (P/Invoke), adaptive
  controller, `InputInjector` (SendInput + Display-2 mapping), `VirtualDisplayController`.
- `/windows/SecondScreen.Desktop` (WPF) — dark dashboard, PIN flow, live stats.
- `/windows/SecondScreen.Native` (C++ DLL) — DXGI Desktop Duplication capture + Media
  Foundation H.264 encoder (HW MFT + SW fallback) + SwDevice virtual-display bridge.
- `/windows/SecondScreen.DisplayDriver` (C++/WDK) — real IddCx indirect display driver +
  INF + vcxproj.
- `/android/app` (Kotlin) — discovery, pairing (mirror crypto), TCP control, UDP video
  receiver + reassembly, MediaCodec→Surface decoder (no Bitmap), touch input, monitor mode
  (immersive/keep-on/foreground service/lock-task), Compose UI + fullscreen MonitorActivity
  with diagnostics overlay.
- `/windows/installer` — Inno Setup script → `SecondScreenLocal-Setup.exe`.
- `/docs` — README, ARCHITECTURE, PROTOCOL, BUILD_WINDOWS, BUILD_ANDROID, DRIVER_SETUP.

## Milestone status (source complete, unbuilt/untested here)
- M1 protocol/discovery/pairing/heartbeat/reconnect — done (source).
- M2 Android receiver — done (source).
- M3 Windows stream host — done (source).
- M4 IddCx virtual display — done (source; needs WDK build + signing).
- M5 touch → Display-2 mapping — done (source).

## Explicit TODO(hardware) (cannot be done without real HW/OS)
- IddCx driver build/sign/install + zero-copy swap-chain→encoder handoff.
- Media Foundation HW encoder per-GPU attribute tuning + real latency validation.
- USB transport (adb reverse/forward tunnels) — Phase 3.
- Wi-Fi Direct transport (Windows.Devices.WiFiDirect P2P adapter) — Phase 4.

## Backlog / next
- P0 (fixed in audit): C# shared namespace blocker, native link libs, IddCx adapter-init, encoder include.
- P1: Build on Windows + Android, fix any compile errors surfaced by the real toolchains.
- P1 BLOCKER (Display-2 path): supply a valid 128-byte EDID in Driver.cpp + build/sign the WDK driver.
- P1: pass Android resolution to the driver (registry/IOCTL) so Display 2 matches the phone.
- P2: zero-copy swap-chain→encoder, Android→Windows keyboard, audio toggle, multi-display, HEVC.

## Audit result (this session)
- Working MVP path (LAN + primary-display capture + H.264 + touch) is theoretically build-ready.
- Display-2 (IddCx) path is INCOMPLETE: EDID STUB + WDK build/sign remain (cannot be done in Linux env).
- Crypto interop verified by design + test vectors (shared/protocol/TEST_VECTORS.md); not runtime-tested.
