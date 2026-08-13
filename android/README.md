# Android project notes

Open the `/android` folder in **Android Studio**; it will generate the Gradle wrapper JAR
(`gradle/wrapper/gradle-wrapper.jar`) and `local.properties` (with your `sdk.dir`)
automatically on first sync. If you prefer the command line, run once:

```
cd android
gradle wrapper --gradle-version 8.7
./gradlew assembleDebug
```

(You need a system Gradle 8.x for that first `gradle wrapper` call; afterwards `./gradlew`
works standalone.)

The shared protocol constants are compiled from `../shared/protocol/kotlin` via the
`sourceSets` entry in `app/build.gradle.kts`, so Android and Windows stay in lockstep.

See `../docs/BUILD_ANDROID.md` for full build + signing instructions.
