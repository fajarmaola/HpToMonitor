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

## Build/verify results (this session — actually executed)
- Android APK BUILT: /app/artifacts/SecondScreenLocal-debug.apk (14.8MB, valid dex/arsc/manifest).
  Gradle wrapper committed; qemu aapt2 shim only needed on this arm64 container.
- Fixed 1 real Kotlin compile error found by the build (MonitorActivity visibility in nested lambda).
- Android unit tests 5/5 PASSED; C# crypto/protocol tests 7/7 PASSED; C#↔Kotlin interop proven
  byte-identical against TEST_VECTORS.md.
- EDID STUB REPLACED with a valid 128-byte block (checksum verified) in Driver.cpp.
- CI added: .github/workflows/{android,windows}.yml (build .apk + .exe + tests + best-effort driver).
- Windows .exe / native DLL / IddCx driver still REQUIRE a Windows runner (CI) — cannot compile on Linux.
  See docs/BUILD_RESULTS.md.

## Update (Jun 2026) — WDK driver now builds in CI via NuGet
- ROOT CAUSE of the CI `wudfwdm.h not found`: GitHub `windows-latest` has VS + SDK but NO WDK,
  and the old choco `windowsdriverkit11` + VSIX step never populated the WDK include paths.
- FIX: switched the IddCx driver to the modern **WDK NuGet** delivery (Microsoft.Windows.WDK.x64
  + SDK.CPP, v10.0.28000.2526), mirroring microsoft/Windows-driver-samples.
  New files: windows/SecondScreen.DisplayDriver/{packages.config, Directory.Build.props};
  vcxproj restructured to the official IddSampleDriver layout (IndirectDisplayDriver + IDDCX 1.4,
  DriverTargetPlatform=Universal), removed the manual include-path hack.
- CI (.github/workflows/windows.yml `windows-driver` job): now does `nuget restore` then msbuild,
  no choco/VSIX, no hardcoded WindowsTargetPlatformVersion.
- STATUS: pushed for CI verification (cannot compile Windows drivers in the Linux container).

## RESOLVED (Aug 14, 2026) — IddCx driver CI build is GREEN
- The entire Windows workflow now passes on the user's repo (teleraya-official/SecondScreenLocal),
  run "Windows #13", commit 62f543e — verified via user's GitHub Actions screenshot (green).
- Full fix chain for the WDK/IddCx driver CI:
  1. WDK via NuGet (Microsoft.Windows.WDK.x64 + SDK.CPP 10.0.28000.2526) -> fixes wudfwdm.h/wdf.h/iddcx.h not found.
  2. INF rewritten to Microsoft's current IddSampleDriver pattern (DIRID 12\UMDF + WUDFRD Include/Needs,
     TargetOS 10.0...17763) -> fixes InfVerif error 1199 + warning 2084.
  3. Driver.cpp updated to current IddCx 1.4 API: IddCxSwapChainFinishedProcessingFrame(swapchain) [no hBuffer],
     IDARG_OUT_MONITORARRIVAL -> fixes 12 C++ compile errors. TreatWarningAsError=false added.
  4. vcxproj <FilesToPackage Include="$(TargetPath)"/> -> DLL packaged so Inf2Cat finds it, .cat generated.
- Driver compiles, links, passes ApiValidator (Universal), and Inf2Cat produces the catalog.
- Artifacts available: SecondScreenLocal-Windows-x64 (.exe+.dll), SecondScreenLocal-DisplayDriver (.dll/.inf/.cat),
  SecondScreenLocal-debug.apk.
- NOTE (recurring during this session): user's "Save to Github" pushes sometimes lagged, so CI built stale code;
  resolved by verifying the file contents on GitHub before each run.
- REMAINING (hardware/runtime, cannot be done in CI or Linux container): test-sign the driver + install on a
  Windows PC (bcdedit /set testsigning on), then E2E LAN streaming test. USB + Wi-Fi Direct transports are still stubs.

## Fix (Code 43) — IddCx target-mode vSyncFreqDivider
- Symptom: after the Code 10 fix (firmware/hardware version), device now reported Code 43
  ("Windows has stopped this device because it has reported problems").
- ROOT CAUSE: FillMonitorMode always zero-inited the mode (vSyncFreqDivider=0) for ALL callbacks,
  including EvtIddCxMonitorQueryTargetModes. IddCx DDI validation REJECTS a zero divider on
  TARGET modes -> Code 43. (Per MS docs "indirect-display-debugging" + IddSampleDriver FillSignalInfo.)
- FIX (windows/SecondScreen.DisplayDriver/Driver.cpp):
  * FillMonitorMode now takes bool bMonitorMode; vSyncFreqDivider = bMonitorMode ? 0 : 1.
  * Added AdditionalSignalInfo.videoStandard = 255 (matches sample).
  * vSyncFreq set to VSync/1 (was VSync*1000/1000); pixelRate = VSync*Width*Height.
  * Monitor callbacks (ParseMonitorDescription, GetDefaultDescriptionModes) call with true;
    QueryTargetModes calls with false.
  * IDDCX_ENDPOINT_VERSION now also sets MinorVer=0.
- VERIFICATION: cannot be tested in this Linux container (no Windows runtime). Requires: Save to
  GitHub -> CI (windows-driver job) rebuild -> install signed driver on Windows PC -> Device Manager
  status OK. Automated web testing agents are N/A for a native UMDF display driver.

## Rebrand + One-step installer (this session)
- REBRAND to "HP ke Monitor" by PT Teleraya Digital Group (company.teleraya.com). Only USER-FACING
  strings + icons changed; internal namespaces (SecondScreen.*), Android package (com.secondscreen.local),
  INF filename, hardware id (Root\SecondScreenDisplay), service name kept to avoid breaking CI/build.
  * Android: strings.xml app_name, MainActivity/MonitorActivity/MonitorService UI -> Indonesian,
    manifest label -> @string/app_name + roundIcon.
  * WPF: MainWindow.xaml (title/header/labels/buttons/footer), MainWindow.xaml.cs (badges/messageboxes),
    Driver.cpp endpoint names, INF [Strings], SwDevice.cpp device description, installer .iss
    (AppName/Publisher/URL/group/OutputBaseFilename=HPkeMonitor-Setup), README + PANDUAN.
  * ICONS: scripts/gen_icons.py (PIL) -> windows/SecondScreen.Desktop/appicon.ico (multi-size, wired via
    <ApplicationIcon>), android mipmap-*/ic_launcher(.round).png, new adaptive foreground vector
    (phone -> arrow -> monitor, green #2ED47A on #0E1116). Preview: artifacts/app_icon_512.png.
- ONE-STEP INSTALL (no manual PowerShell): new Core/VirtualDisplay/DriverInstaller.cs.
  * GetInstalledVersion() parses `pnputil /enum-drivers` label-agnostically (works on ID Windows).
  * EnsureInstalledAsync(): compares bundled INF DriverVer vs installed -> SKIP if >=, else installs via
    elevated `pnputil /add-driver <inf> /install` (single UAC prompt, ExitCode 0/3010=ok/reboot).
  * EnableTestSigningAsync()/UninstallAsync() elevated helpers. app.manifest stays asInvoker (elevation
    per-action via Verb=runas) so SendInput still works.
  * MainWindow: Loaded shows driver status; "Pasang & Mulai" runs EnsureInstalled -> on failure offers
    YES(enable test-signing)/NO(fallback primary capture)/CANCEL. Desktop.csproj bundles built driver to
    output "driver\" (also SSL_DRIVER_DIR) so DriverInstaller.FindInfPath finds it next to the exe.
- VERIFICATION: cannot build/run in Linux container (no .NET/Android/Windows runtime). Verify via
  Save to GitHub -> CI build -> run .exe (Pasang & Mulai) + install APK on device.

## Session: Language toggle + Health panel + Clean uninstall + Splash + modern UI + Android connect screen
- USER DECISIONS: driver check/install ONLY in installer (NOT auto in app); default language Indonesian
  (toggle ID/EN persisted); keep dark+green but modern; Android success/instruction screen designer's choice.
- WINDOWS (WPF, SecondScreen.Desktop):
  * Localization.cs (Loc, AppLang, runtime dict ID/EN, T(key)) + AppSettings.cs (persist lang in
    %AppData%\HPkeMonitor\settings.json). MainWindow builds strings via ApplyLanguage()+Loc.Changed;
    header EN/ID chip (LangButton). Default ID.
  * Start flow no longer installs driver; button renamed to "Mulai". Driver install lives in the installer
    (.iss installdriver task, checked by default). App shows a neutral hint only (no pnputil on launch).
  * HealthWindow.xaml(.cs) "Cek Kesehatan": rows Driver / Test Signing / Network with status + manual
    Fix/Enable/Open buttons + Clean-uninstall (UninstallAsync + VirtualDisplayController.Remove, confirm).
    DriverInstaller.IsTestSigningOn() added (best-effort bcdedit, null=unknown).
  * SplashWindow.xaml(.cs) ~1.6s branded splash; App.xaml.cs OnStartup shows splash->main (removed
    StartupUri). Modern App.xaml: WindowBg/CardGrad/AccentGrad gradients, CardStyle w/ DropShadow,
    gradient Primary button, Chip style. assets/logo.png (scripts/gen_splash.py) as <Resource>.
- ANDROID (Compose):
  * ui/I18n.kt (object, SharedPreferences "hpkemonitor", default ID, Compose mutableState -> recompose on
    toggle). MainActivity: header EN/ID OutlinedButton, all strings via I18n.t, footer.
  * Removed auto-launch of MonitorActivity; on Streaming shows ConnectedInstructions (check badge, title,
    3 numbered StepCards, big "Mulai Tampilkan" -> starts MonitorActivity). Modern rounded cards.
  * MonitorActivity connecting/stats strings via I18n.t.
- VERIFY (cannot build/run in Linux container): Save to GitHub -> CI build .exe/.apk -> installer installs
  driver at setup -> app: language toggle, Cek Kesehatan fixes, splash, Android connect screen. Windows
  UMDF driver + streaming still need real hardware.

## Session: single installer artifact + (planned) GitHub auto-update
- CI windows.yml collapsed to ONE job -> ONE artifact "HPkeMonitor-Setup.exe" (Inno Setup via choco),
  bundles app + driver; driver installed automatically during setup ([Run] pnputil, always, elevated).
  .iss: #ifndef guards for AppDir/DriverDir/AppVersion (CI passes /D...); removed optional installdriver
  task. Version = 1.0.<run_number> baked via dotnet publish -p:Version.
- AUTO-UPDATE: pending user confirm of GitHub repo owner/name + public/private. Plan: CI creates a
  GitHub Release (assets: installer .exe + app-debug.apk); app checks api.github.com/releases/latest,
  compares version, downloads asset, runs installer (Win) / package installer (Android). Internet used
  ONLY for update check/download; all config/streaming stays offline/LAN.

## Session: single installer + GitHub auto-update (IMPLEMENTED)
- Repo for updates: fajarmaola/HpToMonitor (MUST be Public for anonymous update download).
- Versioning: /app/VERSION (e.g. "1.0") + GITHUB_RUN_NUMBER -> 1.0.<run>, monotonic. Bump VERSION for big versions.
- CI: NEW .github/workflows/release.yml (jobs: windows installer, android apk, release). release job (push to
  main/master only) downloads both artifacts, writes version.json, publishes moving "latest" GitHub Release
  via softprops/action-gh-release (permissions: contents: write). windows.yml/android.yml set to
  workflow_dispatch-only to avoid duplicate builds. Single installer artifact HPkeMonitor-Setup.exe (Inno).
- Windows updater: Core/Update/Updater.cs (GitHub /releases/latest -> version.json + .exe asset, compare,
  download to temp, RunInstaller + Application.Shutdown). MainWindow "Cek Pembaruan" button + Loc keys.
- Android updater: update/Updater.kt (HttpURLConnection + org.json, /releases/latest -> version.json + .apk,
  download to cacheDir, FileProvider install intent). Manifest: REQUEST_INSTALL_PACKAGES + FileProvider
  (${applicationId}.fileprovider) + res/xml/file_paths.xml. build.gradle.kts: versionName/Code from
  -P props, buildConfig=true. MainActivity "Cek Pembaruan" button + AlertDialog + I18n keys.
- VERIFY: Save to GitHub (repo Public) -> release.yml builds + publishes "latest" release with the 3 assets
  -> install app -> "Cek Pembaruan" fetches & updates. Cannot test in Linux container.

## Fix: Windows CI "DRIVER_COMPILE_FAILED" (false negative)
- Driver ACTUALLY builds fine (dll+inf+cat). Bug was the post-build path check: WDK DriverPackage
  output folder is named after the PROJECT = "SecondScreen.DisplayDriver", NOT "SecondScreenDisplay".
- Fixed in release.yml + windows.yml: check x64\Release\SecondScreen.DisplayDriver\SecondScreenDisplay.dll
  (fallback flat dll); stage installer driver from that package folder (has the .cat) into publish\driver.
- Also fixed .iss default DriverDir + Desktop.csproj glob to the SecondScreen.DisplayDriver package folder.

## Fix: driver not loading in Test Mode (unsigned .cat) + wrong Test Signing status
- Symptom: Test Mode ON (watermark) + Secure Boot off + phone connected, but no virtual display device.
  Panel wrongly showed Test Signing "nonaktif".
- ROOT CAUSE: driver built with SignMode=Off -> catalog UNSIGNED. Test Mode allows self/test-signed
  drivers but rejects TOTALLY unsigned ones (Code 52) -> SwDeviceCreate device never loads.
- FIX: added windows/SecondScreen.DisplayDriver/testcert.pfx (self-signed code-signing, EKU CodeSigning,
  pass hptomonitor). release.yml now test-signs publish\driver\*.cat via signtool (SHA256 + timestamp)
  after staging, before building installer. Keeps Test Mode requirement.
- Also fixed DriverInstaller.IsTestSigningOn(): return null (unknown) instead of false when bcdedit can't
  be read (non-elevated/localized), so panel no longer mislabels active Test Signing as "nonaktif".
- VERIFY on user PC: Save to GitHub -> new build test-signs cat -> reinstall -> keep Test Mode -> connect
  phone -> "HP ke Monitor Virtual Display" should load.

## ROOT CAUSE FOUND: virtual display silently mirrors (E_ACCESSDENIED) — Jun 2026
- Symptom: driver installed (v21.5.10.160) + Test Signing ON + phone connected & streaming,
  BUT Windows Display Settings shows "no other display found" (mirror, not a real Display 2).
- Diagnostic from user log %LOCALAPPDATA%\SecondScreenLocal\logs\ssl-*.log:
  "Virtual display create failed (-2147024891); falling back to primary capture"
  -2147024891 = 0x80070005 = E_ACCESSDENIED.
- ROOT CAUSE: SwDeviceCreate for a ROOT-enumerated device REQUIRES admin elevation. App manifest
  was asInvoker -> SwDeviceCreate denied -> SessionManager silently falls back to primary-display
  mirroring. (Same requirement as Microsoft's IddSampleApp.)
- FIX (code, needs GitHub CI rebuild — cannot compile in Linux):
  * windows/SecondScreen.Desktop/app.manifest: requestedExecutionLevel -> requireAdministrator.
  * windows/SecondScreen.Native/src/SwDevice.cpp: capture real creation HRESULT from callback,
    10s timeout, return actual error (was ignoring result -> "fake success").
  * DriverInstaller.UninstallAsync: purge ALL stale oemNN.inf copies (was only the first).
  * Diagnostics.UsingVirtualDisplay + SessionManager sets it; MainWindow shows "LAN • Layar Virtual"
    vs "LAN • Mirror" so the user can confirm a real Display 2.
- VERIFY on user PC: Save to GitHub -> reinstall -> launch (accept UAC) -> connect phone ->
  ConnText should read "Layar Virtual" and Windows Display Settings should show Display 2.

## Code 31 fix: driver built with WDK newer than target OS — Jun 2026
- Symptom AFTER the elevation fix: device NOW enumerates in Device Manager (SwDeviceCreate ok),
  but shows "Code 31 — Windows cannot load the drivers... {Operation Failed} (STATUS_UNSUCCESSFUL)".
  Code 31 = driver DLL fails to LOAD into WUDFHost (not a runtime init error).
- ROOT CAUSE: driver was built with WDK 10.0.28000.2526 (Windows 11 25H2 / Insider kit) but the
  user runs Windows 11 24H2 (Build 26100). MS docs: building an IddCx/UMDF driver with a WDK newer
  than the target OS makes it install but fail to load with Code 31. Rule: build against the OLDEST
  OS you want to support.
- FIX: pinned WDK + SDK.CPP NuGet to stable 24H2 kit 10.0.26100.4204 in
  windows/SecondScreen.DisplayDriver/packages.config + Directory.Build.props (import paths updated).
  INF stays UmdfLibraryVersion 2.25.0 / IddCx0104 (both well below 24H2 so safe). CI reads
  packages.config (no hardcoded WDK version in workflows).
- VERIFY on user PC: Save to GitHub -> new CI build (26100 WDK) -> reinstall -> run as admin ->
  connect phone -> device should load without Code 31 and ConnText should show "Layar Virtual".

## CI fix: pin windows-2022 runner (VS2026 vs WDK toolchain mismatch) — Jun 2026
- After pinning WDK to stable 26100.4204, CI build FAILED with MSB4062: "ValidateNTTargetVersion task
  could not be loaded from ...Microsoft.DriverKit.Build.Tasks.18.0.dll ... file not found".
- CAUSE: GitHub migrated windows-latest/windows-2025 to Visual Studio 2026 (MSBuild 18) in June 2026.
  MSBuild 18 looks for the WDK task DLL "...Tasks.18.0.dll", which ONLY ships in the VS2026-era
  WDK 28000. The stable 24H2 WDK 26100 ships "...Tasks.17.0.dll" (VS2022 / MSBuild 17).
  So WDK 26100 (needed to avoid Code 31 on 24H2) is incompatible with the VS2026 runner.
- FIX: pinned the `windows` job in BOTH .github/workflows/release.yml and windows.yml to
  `runs-on: windows-2022` (VS2022 / MSBuild 17). This matches WDK 26100 tasks AND targets 24H2 so the
  driver loads on the user's Build 26100 PC. windows-2022 is still an available hosted image (Jun 2026).
- NOTE for future: if windows-2022 is retired, either (a) install VS2022 build tools on windows-2025,
  or (b) move to WDK 28000 + VS2026 but ALSO set WindowsTargetPlatformVersion to a 26100 SDK so the
  driver still targets 24H2 (avoids Code 31).

## REAL Code 31 root cause: wrong UmdfExtensions (IddCx0104) — Jun 2026
- After WDK/runner fixes, driver still Code 31 / STATUS_UNSUCCESSFUL (installs + node enumerates,
  but fails to LOAD).
- ROOT CAUSE: INF had `UmdfExtensions = IddCx0104`. "IddCx0102" is the FIXED registered name of the
  IddCx UMDF class extension — it stays IddCx0102 even for IddCx 1.4/1.6 drivers (verified against
  Microsoft's official IddSampleDriver.inf on main, which supports up to IddCx 1.6 yet still declares
  IddCx0102). A previous agent wrongly changed 0102->0104 to "match versions". Windows has no
  "IddCx0104" extension -> cannot find/load it -> Code 31.
- FIX (SecondScreenDisplay.inf):
  * UmdfExtensions = IddCx0102  (reverted from IddCx0104 — THE fix)
  * Added HKR, "WUDF", "DeviceGroupId", %REG_SZ%, "SecondScreenDisplayGroup" (matches official sample)
  * UmdfLibraryVersion stays 2.25.0 (matches vcxproj UMDF_VERSION_MINOR=25; valid on 24H2)
- Reference: official INF https://github.com/microsoft/Windows-driver-samples video/IndirectDisplay/
  IddSampleDriver/IddSampleDriver.inf  (UmdfExtensions=IddCx0102, has DeviceGroupId).
- VERIFY: Save to GitHub -> reinstall -> run as admin -> connect phone -> driver loads (no Code 31),
  ConnText "Layar Virtual", Display 2 appears.

## Mirror/Duplicate fix: force EXTEND topology on connect — Jul 2025
- Symptom AFTER driver loads (Display 2 exists): Windows Display Settings shows both monitors
  collapsed into one "1|2" box set to "Duplikasikan tampilan ini" (Duplicate/mirror). Phone mirrors
  the PC instead of being a separate desktop.
- ROOT CAUSE: SessionManager created the IddCx virtual display but never told Windows to switch to
  the EXTEND topology, so Windows kept the last-active topology (Duplicate) -> mirror.
- FIX (code, needs GitHub CI rebuild):
  * NEW windows/SecondScreen.Core/VirtualDisplay/DisplayTopology.cs — P/Invokes user32
    SetDisplayConfig(SDC_APPLY | SDC_TOPOLOGY_EXTEND) with retry (fresh IddCx monitor needs a moment
    to enumerate). Equivalent to Win+P -> Extend, done automatically.
  * SessionManager.StartStreamingSession: after CreateVirtualDisplay succeeds, await
    DisplayTopology.EnableExtendModeAsync() BEFORE computing the capture output index (so Display 2
    is a distinct DXGI output when captured).
  * Deliberately did NOT auto-restore internal-only on disconnect (would disable a user's real
    external monitor). Windows collapses the layout on its own when the virtual display is removed.
- VERIFY on user PC: Save to GitHub -> new CI build -> reinstall -> run as admin -> connect phone ->
  Display Settings should auto-switch to "Perluas tampilan ini" (Extend), two separate boxes 1 and 2,
  and the phone shows an empty extended desktop (drag windows onto it).

## Extend fix v2: virtual head stayed INACTIVE ("Show only on 1") — Jul 2025
- Symptom after extend fix v1: Display Settings now shows TWO separate boxes (1 and 2) but mode is
  "Hanya tampilkan di 1" (Show only on 1). Display 2 exists but is NOT active -> phone shows nothing,
  desktop can't spill onto it.
- ROOT CAUSE: SetDisplayConfig(SDC_TOPOLOGY_EXTEND) returned ERROR_SUCCESS immediately, before the
  freshly-created IddCx monitor finished enumerating, so Windows extended only across the displays
  ready at that instant and left the new virtual head inactive. (DXGI GetOutputCount then = 1 ->
  capture grabbed Display 1 / failed -> phone blank.)
- FIX (rewrote windows/SecondScreen.Core/VirtualDisplay/DisplayTopology.cs):
  * Full CCD supplied-config: GetDisplayConfigBufferSizes + QueryDisplayConfig(QDC_ALL_PATHS), find
    each inactive-but-available path (targetAvailable && id!=0 = the virtual head), mark it
    DISPLAYCONFIG_PATH_ACTIVE with invalid mode indices, then SetDisplayConfig with
    SDC_APPLY|SDC_USE_SUPPLIED_DISPLAY_CONFIG|SDC_ALLOW_CHANGES|SDC_SAVE_TO_DATABASE.
  * Retries up to 20x300ms until the virtual head appears (NotReady) and activation succeeds.
  * Falls back to SDC_TOPOLOGY_EXTEND shortcut only as a last resort.
  * Note: activates ALL available inactive heads (could switch on a user's detached real monitor too;
    acceptable trade-off). Verified struct sizes: PATH_INFO=72B, MODE_INFO=64B.
- Once Display 2 is active, DXGI GetOutputCount=2 -> SessionManager captures output index 1 (the
  virtual head) via existing cross-adapter ResolveOutput; no native change needed.
- VERIFY on user PC: Save to GitHub -> CI build -> reinstall -> run as admin -> connect phone ->
  Display Settings should read "Perluas tampilan ini" with box 2 ACTIVE; phone shows an empty
  extended desktop you can drag windows onto.

## Black phone / zero bitrate fix: capture pipeline silent failure — Jul 2025
- Symptom: phone connects (Streaming, 60fps) but screen is BLACK and BITRATE shows "—" (0). Windows
  side sent ~0 bytes -> native capture->encode produced no frames. Dragging windows onto Display 2
  shows nothing (it's an empty extended desktop AND capture is dead).
- ROOT CAUSE (troubleshoot_agent RCA, 4 compounding bugs):
  1. g_lastError was thread_local -> WorkerLoop errors invisible to the thread calling LastError().
  2. SslNativeStart returns 0 immediately (async worker) -> C# never sees capture/encoder init failure.
  3. IDXGIOutput1::DuplicateOutput fails on IddCx virtual displays (DXGI_ERROR_UNSUPPORTED 0x887A0004).
  4. No fallback / no user-visible error -> silent black screen.
- FIX:
  * NativeApi.cpp: g_lastError now global + std::mutex (SetLastError); added g_status atomic
    (0 idle, 1 capturing, <0 failed) + SslNativeGetStatus(); reset status/error on Start; buffered
    LastError() copy under lock. WorkerLoop: on capture-init failure, FALL BACK to primary display
    (index 0) mirror so something streams + records why; sets g_status accordingly.
  * DxgiCapture.cpp: prefer IDXGIOutput5::DuplicateOutput1 with format list (BGRA/RGBA/RGB10A2/
    RGBA16F) for IddCx compatibility, fall back to DuplicateOutput; verify DXGI_OUTPUT_DESC
    AttachedToDesktop + non-zero size (catches inactive "Show only on 1" head); errors include HRESULT
    hex. DxgiCapture.h: +#include <dxgi1_5.h>.
  * ssl_native.h: +SslNativeGetStatus. NativeInterop.cs: +SslNativeGetStatus.
  * VideoStreamer.cs: watchdog task polls status after Start; if status<0 or FramesSent==0 (or a
    fallback note exists) raises FatalError(realReason). SessionManager subscribes -> Diagnostics
    .VideoStatus + Error event -> MainWindow MessageBox shows the ACTUAL reason (no more silent black).
  * Diagnostics.cs: +VideoStatus string.
- EXPECTED after rebuild: if DuplicateOutput1 works, Display 2 capture succeeds -> phone shows the
  extended desktop WALLPAPER (empty desktop is normal; drag windows onto it). If capture still fails,
  a MessageBox now shows the exact HRESULT/reason (and the phone mirrors the primary as a fallback).
- STILL TODO (user asked): phone ROTATION detection — Android should send orientation; Windows
  recreates/rotates the virtual display + re-negotiates W/H. Deferred until a picture renders.
- VERIFY on user PC: Save to GitHub -> CI build -> reinstall -> run as admin -> connect phone ->
  either wallpaper appears on the phone, OR a popup states the exact capture error to report back.

## Static-screen black fix + visible version label — Jul 2025
- User reports "no change at all" across rebuilds (not even the new error popup). Two-pronged fix:
  1) VISIBLE VERSION: MainWindow footer now shows "Versi {AssemblyVersion}" (CI sets Version=VERSION.
     run_number). If this number does NOT change after Save to GitHub + reinstall, the new build is
     NOT reaching the PC (install/deploy issue), not a code bug. FooterText named in MainWindow.xaml,
     set in ctor from Assembly.GetName().Version.
  2) STATIC-SCREEN BLACK: DXGI Desktop Duplication only yields a frame on screen CHANGE, so an empty/
     idle extended Display 2 produced ZERO frames -> phone black; UDP keyframe loss never recovered.
     NativeApi.cpp WorkerLoop now: forces an initial IDR; keeps a persistent CopyResource of the last
     frame; when idle (no new frame) re-encodes that copy as a keyframe every 250ms so the desktop
     always shows and UDP loss self-heals. On access-lost, resets the copy + forces keyframe.
- VERIFY: rebuild -> app footer version must change; phone should show the desktop wallpaper even when
  idle. If version DID change but phone still black -> code issue (report popup text). If version did
  NOT change -> deploy issue (Save-to-GitHub / release / download of installer).

## THE fix for "No video frames" (black phone): GDI fallback capture — Jul 2025
- CONFIRMED via popup "Status video HP: No video frames were produced (capture returned nothing)":
  new build IS running (deploy OK). Capture init succeeded (status=1) but Desktop Duplication
  delivered ZERO frames -> phone black.
- ROOT CAUSE: IDXGIOutputDuplication only yields a frame on a screen CHANGE. A static/empty EXTENDED
  Display 2 never presents (not even the first frame), so AcquireNextFrame times out forever.
- FIX (NativeApi.cpp WorkerLoop + DxgiCapture Left/Top):
  * Added GdiGrab(): BitBlt of the display's virtual-screen rect (left/top/w/h) into a BGRA
    D3D11 texture (SRCCOPY|CAPTUREBLT, top-down GetDIBits, CreateTexture2D w/ initial data).
  * Loop: Desktop Duplication = fast path for motion; when no duplication frame for >=200ms, GDI-grab
    the current screen and encode it as a keyframe. This bootstraps frame #1 AND keeps a static/idle
    extended desktop visible + recovers from UDP loss.
  * DxgiCapture now exposes Left()/Top() (from DXGI_OUTPUT_DESC.DesktopCoordinates) for the GDI rect.
  * Added <windows.h>,<vector> to NativeApi.cpp.
- Trade-off: GDI may render some hardware-accelerated/protected windows as black, but wallpaper +
  normal windows show fine; motion still uses fast Duplication path.
- VERIFY: rebuild -> connect phone -> phone shows the extended desktop wallpaper; drag a window onto
  Display 2 -> it appears on the phone. BITRATE should now show a real number (not "—").

## In-app silent update + clickable company link — Jul 2025
- User wants "Cek Pembaruan" to UPDATE in place (no manual reinstall wizard) and the company link
  clickable.
- Silent update:
  * Updater.RunInstaller now launches the installer with "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART
    /CLOSEAPPLICATIONS" (one unavoidable UAC prompt for Program Files + driver install).
  * SecondScreenLocal.iss [Setup]: CloseApplications=yes, RestartApplications=no; added a silent-only
    relaunch [Run] entry "Filename: {app}\SecondScreenLocal.exe; Flags: nowait; Check: WizardSilent"
    so the app reopens after a silent update (postinstall entry is skipped when silent). Avoids double
    launch by not using Restart Manager restart.
  * MainWindow already calls Application.Shutdown() after RunInstaller so files unlock.
  * CAVEAT: the CURRENTLY installed app still has the old (wizard) RunInstaller, so the NEXT update
    shows the wizard once; every update AFTER this build is silent.
- Clickable link: MainWindow.xaml footer is now a TextBlock with a <Hyperlink> to
  https://company.teleraya.com (RequestNavigate -> Process.Start ShellExecute) + a VersionRun showing
  "Versi {AssemblyVersion}". Replaces the old FooterText.

## LIKELY real root cause of black phone: async hardware H.264 MFT driven synchronously — Jul 2025
- Popup still "No video frames were produced" with HW encoder checkbox ON. Capture inits (status=1)
  but 0 frames reach the phone.
- ROOT CAUSE (high confidence): hardware H.264 MFTs are ASYNCHRONOUS (MF_TRANSFORM_ASYNC). The code
  drove the encoder with plain ProcessInput/ProcessOutput (sync model). An unlocked async MFT driven
  synchronously accepts nothing / emits nothing -> 0 output -> black.
- FIX (MediaFoundationH264Encoder): detect MF_TRANSFORM_ASYNC at CreateEncoder; if async, unlock it
  and grab IMFMediaEventGenerator. New EncodeAsync() pumps the event model: wait METransformNeedInput
  -> ProcessInput; drain METransformHaveOutput -> PullOutput (NO_WAIT, 1s bootstrap deadline).
  Refactored the old DrainEncoder into PullOutput() (1=got/0=need-input/-1=err) shared by both paths.
  Sync (software) MFTs still use ProcessInput + DrainEncoder.
- Also added pinpoint diagnostics in NativeApi.cpp WorkerLoop: counts encoder inputs vs outputs and
  sets a precise LastError ("GDI capture failed ...", "encoder(gdi|dxgi): ...", or "Encoder menerima
  N frame tetapi output H.264=0 ...") so the popup names the exact failing stage instead of a generic
  "capture returned nothing".
- VERIFY: rebuild -> connect -> phone should finally show the desktop; BITRATE shows a number. If
  still failing, the popup will now state the precise stage/HRESULT to report back.
