#!/bin/bash
# Reproducible Android APK build inside this (ephemeral, arm64) Linux container.
# Toolchains live under /opt which is NOT persistent across pod restarts, so this script
# reinstalls everything each time. On a normal x86-64 machine you would NOT need the qemu
# aapt2 shim below — just use Android Studio / gradlew (see docs/BUILD_ANDROID.md).
set -e
export DEBIAN_FRONTEND=noninteractive
export JAVA_HOME=/usr/lib/jvm/java-17-openjdk-arm64
export ANDROID_SDK_ROOT=/opt/android-sdk ANDROID_HOME=/opt/android-sdk
export GRADLE_USER_HOME=/opt/.gradle

echo "[1/6] JDK + qemu (arm64 host runs x86-64 aapt2 under emulation)"
apt-get install -y openjdk-17-jdk-headless qemu-user-static >/tmp/s_apt.log 2>&1 || {
  apt-get update >/tmp/s_aptup.log 2>&1
  apt-get install -y openjdk-17-jdk-headless qemu-user-static >/tmp/s_apt.log 2>&1; }
dpkg --add-architecture amd64 >/dev/null 2>&1 || true
apt-get update >/tmp/s_aptup2.log 2>&1
apt-get install -y libc6:amd64 libstdc++6:amd64 zlib1g:amd64 >/tmp/s_amd.log 2>&1

echo "[2/6] Gradle 8.7"
cd /tmp && curl -fsSL -o gradle.zip https://services.gradle.org/distributions/gradle-8.7-bin.zip 2>/dev/null
unzip -q -o gradle.zip -d /opt >/dev/null

echo "[3/6] Android SDK (platform 34, build-tools 34)"
mkdir -p /opt/android-sdk/cmdline-tools
cd /tmp && curl -fsSL -o cmdtools.zip https://dl.google.com/android/repository/commandlinetools-linux-11076708_latest.zip 2>/dev/null
unzip -q -o cmdtools.zip -d /opt/android-sdk/cmdline-tools >/dev/null
[ -d /opt/android-sdk/cmdline-tools/cmdline-tools ] && mv /opt/android-sdk/cmdline-tools/cmdline-tools /opt/android-sdk/cmdline-tools/latest
yes | /opt/android-sdk/cmdline-tools/latest/bin/sdkmanager --licenses >/tmp/s_lic.log 2>&1 || true
/opt/android-sdk/cmdline-tools/latest/bin/sdkmanager "platform-tools" "platforms;android-34" "build-tools;34.0.0" >/tmp/s_sdk.log 2>&1

echo "[4/6] qemu aapt2 shim (arm64-only workaround)"
MA=$(find /opt/android-sdk/build-tools/34.0.0 -name aapt2 | head -1)
cp "$MA" /opt/real-aapt2 && chmod +x /opt/real-aapt2
mkdir -p /opt/aapt2dir
printf '#!/bin/sh\nexec /usr/bin/qemu-x86_64-static /opt/real-aapt2 "$@"\n' > /opt/aapt2dir/aapt2
chmod +x /opt/aapt2dir/aapt2
printf 'sdk.dir=/opt/android-sdk\n' > /app/android/local.properties

echo "[5/6] gradle assembleDebug"
cd /app/android
/opt/gradle-8.7/bin/gradle :app:assembleDebug --no-daemon --console=plain \
  -Pandroid.aapt2FromMavenOverride=/opt/aapt2dir/aapt2

echo "[6/6] copy APK to persistent /app/artifacts"
mkdir -p /app/artifacts
cp app/build/outputs/apk/debug/app-debug.apk /app/artifacts/SecondScreenLocal-debug.apk
ls -la /app/artifacts/
echo "DONE_BUILD_OK"
