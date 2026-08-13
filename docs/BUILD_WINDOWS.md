# Building the Windows side

## Prerequisites
- Windows 10 (1903+) or Windows 11 x64.
- Visual Studio 2022 with workloads:
  - **Desktop development with C++** (MSVC v143, Windows 11 SDK).
  - **.NET desktop development** (.NET 8 SDK).
- (Driver only) **Windows Driver Kit (WDK)** matching your SDK, and the "WDK Visual Studio
  extension".

## 1. Native DLL (`SecondScreen.Native`)
Provides DXGI capture + Media Foundation H.264 encode as `SecondScreen.Native.dll`.

```
cd windows/SecondScreen.Native
cmake -S . -B build -A x64
cmake --build build --config Release
```
Output: `windows/SecondScreen.Native/build/Release/SecondScreen.Native.dll`.

The build links `d3d11 dxgi mf mfplat mfuuid mfreadwrite dxguid`.

## 2. Core + Desktop (C#/.NET 8)
```
cd windows
dotnet restore SecondScreenLocal.sln
dotnet build SecondScreenLocal.sln -c Release
```
The Desktop project copies `SecondScreen.Native.dll` next to `SecondScreen.Desktop.exe`
(see the `<None ... CopyToOutputDirectory>` item in `SecondScreen.Desktop.csproj`). If you
built the DLL to a different path, set env var `SSL_NATIVE_DLL` before building or drop the
DLL into the Desktop output folder manually.

Run:
```
cd windows/SecondScreen.Desktop/bin/Release/net8.0-windows
SecondScreen.Desktop.exe
```

## 3. Installer (`SecondScreenLocal-Setup.exe`)
Two supported options (pick one):

### Option A — WiX Toolset (recommended, produces `.msi`/bootstrapper `.exe`)
1. Install WiX v4 (`dotnet tool install --global wix`).
2. `cd windows/installer && wix build Product.wxs -o SecondScreenLocal-Setup.msi`
   (or use the provided `build_installer.ps1`).

### Option B — Inno Setup (produces `SecondScreenLocal-Setup.exe`)
1. Install Inno Setup 6.
2. Build the Release output above, then:
   `iscc windows/installer/SecondScreenLocal.iss`
   Output: `windows/installer/Output/SecondScreenLocal-Setup.exe`.

The installer bundles: `SecondScreen.Desktop.exe`, `SecondScreen.Native.dll`, the .NET 8
runtime dependency (or publish self-contained with `dotnet publish -c Release -r win-x64
--self-contained true /p:PublishSingleFile=true`), and optionally the signed driver package
(`SecondScreen.DisplayDriver`) installed via `pnputil` in a custom action.

## Firewall
On first run the app binds UDP 47800/47802 and TCP 47801 on the **private** network profile
only. Allow it when Windows prompts, or pre-add rules with `netsh advfirewall` (the app also
attempts to add loopback + private rules on first launch; see `Program.EnsureFirewall`).

## Notes / limitations
- Without the IddCx driver installed, the host captures the **primary display** (still a full
  working stream + touch demo). With the driver installed and a virtual monitor created, it
  captures **Display 2** — the true "extend desktop onto Android" experience.
- Hardware H.264 encode requires a GPU MFT (Intel QSV / NVENC / AMD VCE). The encoder falls
  back to the Microsoft software H.264 MFT if no hardware MFT is present.
