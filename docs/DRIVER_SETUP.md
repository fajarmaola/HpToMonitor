# IddCx Virtual Display Driver — build, sign, install

`SecondScreen.DisplayDriver` is a real **Indirect Display Driver** built on Microsoft's
**IddCx** framework (the modern, supported way to add a software monitor on Windows 10
1903+/11). When installed and activated, Windows shows a genuine **Display 2** and offers
*Extend these displays*, so real apps (Chrome, YouTube, VS Code, Explorer, media players)
can be moved onto the Android screen.

> This driver **cannot be built or tested in the authoring Linux container.** It requires the
> Windows Driver Kit and a Windows test machine. The source is complete and structured per
> Microsoft's IddCx sample; follow the steps below on Windows.

## Prerequisites
- Visual Studio 2022 + **Desktop C++** workload.
- **Windows Driver Kit (WDK)** for your SDK version + the WDK VS extension.
- Test machine (or VM) where you can enable test signing.

## 1. Build
Open `windows/SecondScreen.DisplayDriver/SecondScreen.DisplayDriver.vcxproj` in VS (it is
included in `SecondScreenLocal.sln` but not built by `dotnet`). Build **x64 / Release**.
Output: a driver package folder containing:
- `SecondScreenDisplay.dll`
- `SecondScreenDisplay.inf`
- `SecondScreenDisplay.cat`

## 2. Sign
For local testing you can use a **test certificate** and enable test signing:
```
REM elevated prompt
bcdedit /set testsigning on
REM reboot

REM create + trust a test cert (once)
makecert -r -pe -ss PrivateCertStore -n "CN=SecondScreenTest" SecondScreenTest.cer
certutil -addstore -f Root SecondScreenTest.cer
certutil -addstore -f TrustedPublisher SecondScreenTest.cer

REM sign the .cat / .sys(.dll)
signtool sign /v /s PrivateCertStore /n SecondScreenTest /t http://timestamp.digicert.com ^
  x64\Release\SecondScreenDisplay\SecondScreenDisplay.dll
inf2cat /driver:x64\Release\SecondScreenDisplay /os:10_X64
signtool sign /v /s PrivateCertStore /n SecondScreenTest ^
  x64\Release\SecondScreenDisplay\SecondScreenDisplay.cat
```
For distribution to other machines **without test signing**, you need an **EV code-signing
certificate** and a Microsoft **attestation / WHQL** submission via the Partner Center
Hardware Dashboard. This is a business/administrative step outside the code.

## 3. Install
The driver is a **software device** enumerated by a root device or by our user-mode
installer. Two paths:

### A. Manual (device is created by the app)
1. Install the driver package so Windows knows the INF:
   ```
   pnputil /add-driver x64\Release\SecondScreenDisplay\SecondScreenDisplay.inf /install
   ```
2. The Desktop app's `VirtualDisplayController` creates the software device via
   `SwDeviceCreate` (see `windows/SecondScreen.Native/SwDevice.cpp`), which causes Windows to
   load the IddCx driver and enumerate the monitor.

### B. Manual test with a root-enumerated device
Use the WDK `devcon` tool:
```
devcon install SecondScreenDisplay.inf Root\SecondScreenDisplay
```

## 4. Verify
- `Settings → System → Display` shows a second display.
- Choose **Extend these displays**, arrange its position.
- The app captures that display index and streams it to Android.

## 5. Uninstall
```
pnputil /delete-driver SecondScreenDisplay.inf /uninstall /force
REM if root-enumerated:
devcon remove Root\SecondScreenDisplay
```

## Behavior on disconnect
`VirtualDisplayController` keeps the virtual monitor alive during the reconnect grace window
(default 15 s). Only after the session is truly abandoned does it call `SwDeviceClose` to
remove Display 2, restoring normal single-display layout. Brief packet loss never tears down
the monitor.

## What is genuinely TODO(hardware)
- The IddCx `EvtIddCxMonitorAssignSwapChain` frame path (`SwapChainProcessor.cpp`) copies the
  OS-provided frame. Wiring those frames **directly** into the MF encoder (zero-copy shared
  texture) is the optimal path; the source shows the shared-texture handoff and marks the
  exact spot where a real GPU is required to validate zero-copy vs. the DXGI-duplication
  fallback used when capturing an existing display.
