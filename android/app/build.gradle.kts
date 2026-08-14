plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.secondscreen.local"
    compileSdk = 34

    defaultConfig {
        applicationId = "com.secondscreen.local"
        minSdk = 26
        targetSdk = 34
        versionCode = (project.findProperty("versionCode") as String?)?.toIntOrNull() ?: 1
        versionName = (project.findProperty("versionName") as String?) ?: "1.0.0"
    }

    // Pull in the shared protocol constants so Android and Windows agree byte-for-byte.
    sourceSets {
        getByName("main") {
            java.srcDirs("src/main/java", "../../shared/protocol/kotlin")
        }
    }

    signingConfigs {
        // Stable signing key shipped in the repo so EVERY build (and every auto-update) shares the
        // SAME signature. Without this, each CI build uses a random debug key and installs/updates
        // fail with "Aplikasi tidak terinstal". (For stronger security, move this to a GitHub secret.)
        create("stable") {
            storeFile = file("hptomonitor.p12")
            storePassword = "hptomonitor"
            keyAlias = "hptomonitor"
            keyPassword = "hptomonitor"
            storeType = "PKCS12"
        }
    }

    buildTypes {
        debug {
            signingConfig = signingConfigs.getByName("stable")
        }
        release {
            isMinifyEnabled = false
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            signingConfig = signingConfigs.getByName("stable")
        }
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }
    composeOptions {
        kotlinCompilerExtensionVersion = "1.5.14"
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
}

dependencies {
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.4")
    implementation("androidx.activity:activity-compose:1.9.1")

    val composeBom = platform("androidx.compose:compose-bom:2024.08.00")
    implementation(composeBom)
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-graphics")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")

    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.8.1")

    testImplementation("junit:junit:4.13.2")
}
