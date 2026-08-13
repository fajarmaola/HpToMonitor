# What was actually compiled / executed in the authoring environment (Linux arm64 container)

This file records real, executed results — not just static inspection.

## ✅ Android APK — BUILT SUCCESSFULLY
- Command: `gradle :app:assembleDebug` (Gradle 8.7, AGP 8.5.2, JDK 17, compileSdk 34).
- Output artifact (persistent): **`/app/artifacts/SecondScreenLocal-debug.apk`** (~14.8 MB).
- Verified APK contains `classes.dex`, `AndroidManifest.xml`, `resources.arsc`.
- One real compile error was found and fixed during the build:
  `MonitorActivity.kt` — a synthetic-property (`visibility`) resolution issue inside a nested
  `apply { setOnClickListener { ... } }` lambda; refactored into a `toggleOverlay()` method.
- Note: this is an **arm64** container but Google's `aapt2` ships as **x86-64 only**, so the
  build used a `qemu-x86_64` shim for aapt2 (see `scripts/build_apk_here.sh`). On a normal
  x86-64 machine / GitHub Actions, no shim is needed — use Android Studio or `./gradlew`.

## ✅ Android unit tests — 5/5 PASSED
- Command: `gradle :app:testDebugUnitTest`.
- `com.secondscreen.local.CryptoInteropTest`: tests=5, failures=0, errors=0.
- Includes `sessionKey_matches_vector`, `trustedKey_matches_vector`,
  `decrypt_wireFrame_produced_by_csharp`, `aesgcm_roundtrip`,
  `ecdh_both_sides_derive_same_session_key`.

## ✅ C# crypto/protocol tests — 7/7 PASSED
- Command: `dotnet test SecondScreen.Tests` (net8.0, cross-platform — runs on Linux).
- Compiles `CryptoUtil.cs`, `VideoPacketizer.cs`, `Protocol.cs` (same source as the Windows build).
- Includes the exact `TEST_VECTORS.md` checks (SESSION_KEY, TRUSTED_KEY, AES-GCM wire frame),
  ECDH agreement, and video packet header layout.

## ✅ Cross-implementation interop — PROVEN byte-identical
- Both C# and Kotlin independently produce
  `SESSION_KEY = 34758394738a5f6a28968ed494eb58f8f041a289758e8f76b5bdf7c6810a96b3`
  for `IKM=00..1f, PIN=482771`, and Kotlin decrypts the exact AES-GCM wire frame a C# host
  would send back to plaintext `"SecondScreen"`.

## ⛔ NOT buildable in this environment (require real Windows) — surfaced via GitHub Actions CI
- `SecondScreen.Native` (C++ DXGI + Media Foundation) — needs MSVC + Windows SDK.
- `SecondScreen.Desktop` / `SecondScreen.Core` (`net8.0-windows`, WPF) — Windows-only TFM.
- `SecondScreen.DisplayDriver` (IddCx) — needs WDK + test signing.
- CI workflows added: `.github/workflows/windows.yml` (native + .NET + tests + self-contained
  `.exe` + best-effort driver) and `.github/workflows/android.yml` (APK + unit tests). These run
  on x86-64 runners and upload the `.exe`/`.apk` as artifacts.

## Reproduce the APK build here
`bash scripts/build_apk_here.sh`  (reinstalls JDK/SDK/Gradle/qemu because /opt is not persistent).
