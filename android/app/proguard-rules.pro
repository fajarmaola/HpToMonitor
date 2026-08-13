# Keep MediaCodec + crypto reflective usage safe. Minify is off by default (see build.gradle.kts).
-keep class com.secondscreen.local.** { *; }
-dontwarn org.jetbrains.annotations.**
