# SecondScreen Local — Architecture

## High-level

```
                         WINDOWS PC                                   ANDROID
┌───────────────────────────────────────────────┐        ┌────────────────────────────┐
│  SecondScreen.Desktop (WPF UI)                  │        │  MainActivity / Compose UI │
│      │ orchestrates                             │        │      │                     │
│  SecondScreen.Core (C# .NET)                    │        │  DiscoveryManager          │
│   ├ DiscoveryService (UDP 47800)                │◄──────►│  PairingManager (ECDH+PIN) │
│   ├ PairingService (ECDH+PIN, AES-GCM)          │  LAN   │  ConnectionManager (TCP)   │
│   ├ SessionManager (state machine)              │        │  VideoReceiver (UDP)       │
│   ├ ITransport: Lan/Usb/WifiDirect              │        │      │ NAL reassembly      │
│   ├ VideoStreamer  ── P/Invoke ──┐              │        │  MediaCodecDecoder         │
│   ├ InputInjector (SendInput)    │              │        │      │ → Surface           │
│   └ VirtualDisplayController      │              │        │  SurfaceRenderer           │
│                                   ▼              │        │  MonitorModeManager        │
│  SecondScreen.Native (C++ DLL)                   │        │  TouchInputManager ────────┼─► TOUCH msgs
│   ├ DxgiCapture (Desktop Duplication)            │        │  DiagnosticsOverlay        │
│   └ MediaFoundationH264Encoder (HW, SW fallback) │        └────────────────────────────┘
│                                                  │
│  SecondScreen.DisplayDriver (C++/WDK, IddCx)     │
│   └ virtual "Display 2" that Windows extends onto │
└───────────────────────────────────────────────┘
```

## Windows components

### SecondScreen.Core (C#)
Pure orchestration, no UI. Testable. Key types:
- `DiscoveryService` — UDP announce/listen.
- `PairingService` — P-256 ECDH, HKDF, AES-GCM, PIN, `TrustedDeviceStore`.
- `ITransport` / `LanTransport` / `UsbTransport` / `WifiDirectTransport` — logical channels
  (control TCP, video UDP). Only `LanTransport` is fully implemented in the MVP; the others
  compile with `NotImplementedException` bodies and explicit `TODO(hardware)`.
- `SessionManager` — state machine: `Idle → Discovering → Pairing → Configuring → Streaming
  → Reconnecting → Disconnected`.
- `VideoStreamer` — owns the native encoder/capturer via `NativeInterop` P/Invoke, packetizes
  per `PROTOCOL.md §5`, pushes to transport video channel, handles `REQUEST_KEYFRAME` and
  adaptive quality (`AdaptiveController`).
- `InputInjector` — maps normalized touch → virtual-desktop coords → `SendInput`.
- `VirtualDisplayController` — talks to the IddCx driver via its software device interface to
  create/destroy the virtual monitor and set its mode from the Android resolution.

### SecondScreen.Native (C++ DLL)
Performance-critical capture + encode. Exposes a small C ABI consumed by `NativeInterop`:
- `DxgiCapture` — `IDXGIOutputDuplication` on a chosen output (the virtual display once the
  driver is installed, or the primary display before that). Keeps frames as GPU textures.
- `MediaFoundationH264Encoder` — `IMFTransform` H.264 encoder, prefers a hardware MFT
  (`MF_TRANSFORM_ASYNC`), falls back to the software encoder. Input is an NV12 surface fed
  from the captured `ID3D11Texture2D` via video processor; output is Annex-B NALs delivered
  through a callback.
- Codec is behind `IVideoEncoder`; `H264Encoder` today, `HEVCEncoder` later.

### SecondScreen.DisplayDriver (C++/WDK, IddCx)
A real **Indirect Display Driver** using the modern **IddCx** framework (the officially
supported way to add software monitors on Windows 10 1903+/11). It advertises monitor modes,
receives swap-chain frames from the OS, and hands them to the capture/encode path. Requires
WDK, a test/EV signature, and `devcon`/`pnputil` install (see `DRIVER_SETUP.md`). This is the
piece that makes Windows show a genuine **Display 2** and offer *Extend these displays*.

## Android components (Kotlin)
- `DiscoveryManager` — UDP broadcast probe + parse announces.
- `PairingManager` — matching P-256 ECDH / HKDF / AES-GCM, PIN entry.
- `ConnectionManager` — TCP control channel, framing, heartbeat, reconnect.
- `VideoReceiver` — UDP socket, packet reassembly per frame.
- `MediaCodecDecoder` — async `MediaCodec` configured directly to a `Surface` (no Bitmap).
- `SurfaceRenderer` — `SurfaceView` in the monitor activity.
- `MonitorModeManager` — immersive fullscreen, keep-screen-on, foreground service, optional
  lock-task / DND (only with granted permission).
- `TouchInputManager` — gesture detection → normalized `TOUCH` messages.
- `DiagnosticsOverlay` — FPS / latency / bitrate / codec / resolution.

## Session state machine
```
Idle ──discover──► Discovering ──select──► Connecting(TCP)
   └─► Pairing (if not trusted) ──ok──► Configuring ──ack──► Streaming
Streaming ──heartbeat lost──► Reconnecting ──(≤5, grace 15s)──► Streaming | Disconnected
any ── user Disconnect / fatal error ──► Disconnected ──► Idle
```

## Input mapping (touch → Display 2)
Windows exposes the virtual-desktop rectangle `SM_XVIRTUALSCREEN, SM_YVIRTUALSCREEN,
SM_CXVIRTUALSCREEN, SM_CYVIRTUALSCREEN`. The virtual display's bounds are looked up from its
`MonitorInfo`. For a normalized touch `(nx, ny)`:
```
screenX = display2.Left + nx * display2.Width
screenY = display2.Top  + ny * display2.Height
absX = round((screenX - SM_XVIRTUALSCREEN) * 65535 / SM_CXVIRTUALSCREEN)
absY = round((screenY - SM_YVIRTUALSCREEN) * 65535 / SM_CYVIRTUALSCREEN)
SendInput(MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | MOVE, absX, absY)
```
This makes the cursor land exactly where the user touched on Display 2, regardless of where
Windows places Display 2 in the layout.

## Adaptive quality
`AdaptiveController` consumes RTT (from heartbeat), packet-loss/keyframe-request rate, and
receiver `STATS`. Policy:
- loss < 1% & RTT stable → step bitrate up toward target.
- loss 1–5% → reduce bitrate 20%.
- loss > 5% or RTT spikes → drop FPS tier, then resolution tier.
Changes are sent as `SET_QUALITY` and applied to the encoder live.

## Extensibility
- New transport = new `ITransport` impl; session/video/input untouched.
- New codec = new `IVideoEncoder`/`IVideoDecoder` pair + negotiation string.
- Multiple displays = `SessionManager` already keyed by `deviceId`; `VirtualDisplayController`
  can create N virtual monitors. MVP wires a single device but the collections are plural.
