# Building the Android side

## Prerequisites
- Android Studio (Koala/2024.1+).
- Android SDK Platform 34, Build-Tools 34.
- A physical device recommended (API 26+/Android 8+) — emulators often lack a real hardware
  H.264 decoder and won't reflect true latency.

## Build (Android Studio)
1. `File → Open` → select the `/android` folder.
2. Let Gradle sync.
3. `Build → Build Bundle(s)/APK(s) → Build APK(s)`.
4. Output: `android/app/build/outputs/apk/debug/app-debug.apk`
   (rename/ship as `SecondScreenLocal.apk`).

## Build (command line)
```
cd android
./gradlew assembleDebug        # debug APK
./gradlew assembleRelease      # release (configure signing in app/build.gradle.kts)
```
Release output: `android/app/build/outputs/apk/release/app-release.apk`.

## Signing a release APK
Add to `android/app/build.gradle.kts` (a placeholder `signingConfigs` block is present):
```
signingConfigs {
    create("release") {
        storeFile = file(System.getenv("SSL_KEYSTORE") ?: "release.keystore")
        storePassword = System.getenv("SSL_KEYSTORE_PW")
        keyAlias = System.getenv("SSL_KEY_ALIAS")
        keyPassword = System.getenv("SSL_KEY_PW")
    }
}
```
Generate a keystore:
```
keytool -genkey -v -keystore release.keystore -alias ssl -keyalg RSA -keysize 2048 -validity 10000
```

## Permissions the app requests
- `INTERNET`, `ACCESS_NETWORK_STATE`, `ACCESS_WIFI_STATE`, `CHANGE_WIFI_MULTICAST_STATE`
  (LAN sockets / discovery — note: no internet servers are contacted, only local peers).
- `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_MEDIA_PROJECTION`/`CONNECTED_DEVICE`,
  `WAKE_LOCK` (monitor mode keep-alive).
- `POST_NOTIFICATIONS` (Android 13+, for the foreground service notification).
- Optional at runtime: DND access (`ACCESS_NOTIFICATION_POLICY`) and battery-optimization
  exemption — only requested when the user opts in.

## Notes / limitations
- Screen pinning (lock task) for a non-DPC app requires the user to enable **Screen pinning**
  in Android settings and confirm the pin action; the app requests it politely and degrades
  gracefully if denied.
- Hiding the status/navigation bars uses `WindowInsetsController` immersive mode. Android may
  briefly reveal system bars on swipe — this is an OS policy, not a bug.
