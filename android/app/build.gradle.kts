import java.util.Properties

plugins {
    alias(libs.plugins.android.application)
}

abstract class MobileControlAssets : Sync() {
    @get:OutputDirectory
    abstract val assetsDirectory: DirectoryProperty
}

val verifyExternalSources by tasks.registering(Exec::class) {
    workingDir(rootDir)
    commandLine(
        "powershell",
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        "../scripts/checks/check-native-sources.ps1"
    )
}

val prepareLauncherAssets by tasks.registering(Exec::class) {
    dependsOn(verifyExternalSources)
    workingDir(rootDir)
    commandLine(
        "powershell",
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        "../scripts/build/prepare-launcher.ps1"
    )
}

val prepareLauncherUi by tasks.registering(Exec::class) {
    workingDir(rootDir)
    commandLine("powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "../scripts/build/prepare-launcher-ui.ps1")
    inputs.dir("../../ui/launcher/src")
    inputs.dir("../../ui/launcher/public")
    inputs.files("../../ui/launcher/package.json", "../../ui/launcher/package-lock.json", "../../ui/launcher/vite.config.ts", "../../ui/launcher/index.html", "../../ui/launcher/game.html")
    outputs.dir("src/main/assets/ui")
}

val prepareMobileControlAssets by tasks.registering(MobileControlAssets::class) {
    assetsDirectory.set(layout.buildDirectory.dir("generated/mobileControlAssets"))
    from("../../managed/MobileLauncher.Controls/Assets/APK-SPRITES.json") {
        into("mobile-controls")
        rename { "control-sprites.json" }
    }
    from("../../managed/MobileLauncher.Controls/Assets/TexturePacks/AndroidMobileControls.json") {
        into("mobile-controls")
        rename { "control-textures.json" }
    }
    from("../../managed/MobileLauncher.Controls/Assets/asset-aliases.json") {
        into("mobile-controls")
    }
    into(assetsDirectory)
}
androidComponents {
    onVariants(selector().all()) { variant ->
        variant.sources.assets?.addGeneratedSourceDirectory(prepareMobileControlAssets) { it.assetsDirectory }
    }
}

tasks.named("preBuild") {
    dependsOn(prepareLauncherAssets)
    dependsOn(prepareLauncherUi)
}

android {
    namespace = "app.com.tmlpocket"
    compileSdk = 37
    ndkVersion = "30.0.14904198 rc1"

    defaultConfig {
        applicationId = "app.com.tmlpocket"
        minSdk = 28
        targetSdk = 37
        versionCode = 1040422
        versionName = "0.1.12"

        externalNativeBuild {
            cmake {
                arguments += listOf(
                    "-DCMAKE_C_STANDARD=11",
                    "-DCMAKE_CXX_STANDARD=20"
                )
                arguments += "-DTML_MOBILE_TERRARIA_CONVERTER=ON"
                for (dependency in listOf("MOBILE_VGMSTREAM", "MOBILE_OGG", "MOBILE_VORBIS", "MOBILE_LZ4")) {
                    val key = "FETCHCONTENT_SOURCE_DIR_$dependency"
                    providers.gradleProperty(key).orNull?.let { arguments += "-D$key=${file(it).absolutePath}" }
                }
            }
        }
    }

    externalNativeBuild {
        cmake {
            path = file("../native/CMakeLists.txt")
            version = "3.22.1"
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_21
        targetCompatibility = JavaVersion.VERSION_21
    }

    kotlin {
        jvmToolchain(21)
    }

    splits {
        abi {
            isEnable = true
            reset()
            include("arm64-v8a")
            isUniversalApk = false
        }
    }

    val releaseKeyDirectory = File(System.getProperty("user.home"), ".android/tml-pocket-release")
    val releaseCredentials = releaseKeyDirectory.resolve("signing.properties")
    val localReleaseSigning = if (releaseCredentials.isFile) {
        val credentials = Properties().apply { releaseCredentials.inputStream().use { load(it) } }
        signingConfigs.create("localRelease") {
            storeFile = releaseKeyDirectory.resolve("release.p12")
            storeType = "PKCS12"
            storePassword = requireNotNull(credentials.getProperty("storePassword"))
            keyAlias = "tml-pocket"
            keyPassword = storePassword
        }
    } else null

    buildTypes {
        debug {
            isDebuggable = true
            isJniDebuggable = true
            isMinifyEnabled = false
        }
        release {
            signingConfig = localReleaseSigning
            isMinifyEnabled = false
            isShrinkResources = false
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
    }

    buildFeatures {
        buildConfig = true
    }

    androidResources.ignoreAssetsPatterns.add("tmodloader-default")

    packaging {
        jniLibs {
            useLegacyPackaging = true
            pickFirsts += "lib/arm64-v8a/libc++_shared.so"
        }
    }
}

dependencies {
    implementation("androidx.fragment:fragment:1.8.5")
    implementation(libs.webkit)
    implementation(libs.commons.compress)
    implementation(libs.xz)
}
