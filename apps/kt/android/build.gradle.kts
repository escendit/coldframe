import com.android.build.api.variant.ApplicationAndroidComponentsExtension

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.ktlint)
}

// Build-time configuration (AD-23): `-Pcoldframe.serverUrl=… -Pcoldframe.keycloakIssuer=…`, or the
// same keys in ~/.gradle/gradle.properties. Unset values fall back to never-resolvable defaults in
// the core, so an unconfigured build shows the Unreachable notice.
fun coldframeProperty(name: String): String = providers.gradleProperty("coldframe.$name").orNull.orEmpty()

fun String.asBuildConfigString(): String = "\"" + replace("\\", "\\\\").replace("\"", "\\\"") + "\""

// Tests live under tests/kt/, mirroring the module (see docs/quickstart.md).
val testRoot = rootDir.resolve("tests/kt/android")

android {
    namespace = "com.escendit.coldframe"
    compileSdk =
        libs.versions.android.compile.sdk
            .get()
            .toInt()

    defaultConfig {
        applicationId = "com.escendit.coldframe"
        minSdk =
            libs.versions.android.min.sdk
                .get()
                .toInt()
        targetSdk =
            libs.versions.android.target.sdk
                .get()
                .toInt()
        versionCode = 1
        versionName = "0.0.0"

        buildConfigField("String", "SERVER_URL", coldframeProperty("serverUrl").asBuildConfigString())
        buildConfigField("String", "KEYCLOAK_ISSUER", coldframeProperty("keycloakIssuer").asBuildConfigString())
        buildConfigField("String", "KEYCLOAK_CLIENT_ID", coldframeProperty("keycloakClientId").asBuildConfigString())

        // The redirect activity of kotlin-multiplatform-oidc listens on this scheme.
        manifestPlaceholders["oidcRedirectScheme"] = "com.escendit.coldframe"
    }

    buildFeatures {
        buildConfig = true
        compose = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    sourceSets {
        getByName("test") {
            kotlin.directories.add(testRoot.resolve("test/kotlin").path)
            resources.directories.add(testRoot.resolve("test/resources").path)
        }
    }

    testOptions {
        unitTests.isIncludeAndroidResources = true
        unitTests.all { test ->
            // Robolectric reaches into FileDescriptor internals, which JDK 25 no longer exports.
            test.jvmArgs(
                "--add-exports=java.base/jdk.internal.access=ALL-UNNAMED",
                "--add-opens=java.base/java.io=ALL-UNNAMED",
            )
        }
    }

    lint {
        warningsAsErrors = false
        abortOnError = true
    }
}

kotlin {
    jvmToolchain(25)
}

/** Copies the bundled Ubuntu fonts into a generated `res/font` with resource-safe names. */
abstract class CopyFonts : DefaultTask() {
    @get:InputDirectory
    @get:PathSensitive(PathSensitivity.RELATIVE)
    abstract val fonts: DirectoryProperty

    @get:OutputDirectory
    abstract val outputDir: DirectoryProperty

    @TaskAction
    fun copy() {
        val out = outputDir.get().asFile.resolve("font")
        out.deleteRecursively()
        out.mkdirs()
        mapOf(
            "Ubuntu-Regular.ttf" to "ubuntu_regular.ttf",
            "Ubuntu-Light.ttf" to "ubuntu_light.ttf",
            "UbuntuCondensed-Regular.ttf" to "ubuntu_condensed_regular.ttf",
            "UbuntuMono-Regular.ttf" to "ubuntu_mono_regular.ttf",
        ).forEach { (from, to) ->
            fonts
                .get()
                .asFile
                .resolve(from)
                .copyTo(out.resolve(to))
        }
    }
}

val copyFonts =
    tasks.register<CopyFonts>("copyFonts") {
        fonts.set(rootProject.layout.projectDirectory.dir("packages/design-tokens/fonts"))
    }

extensions.getByType<ApplicationAndroidComponentsExtension>().onVariants { variant ->
    variant.sources.res?.addGeneratedSourceDirectory(copyFonts, CopyFonts::outputDir)
}

dependencies {
    implementation(project(":core"))
    implementation(project(":design-tokens"))

    implementation(platform(libs.compose.bom))
    implementation(libs.compose.ui)
    implementation(libs.compose.material3)
    implementation(libs.activity.compose)
    implementation(libs.lifecycle.runtime.compose)
    implementation(libs.lifecycle.viewmodel.compose)
    implementation(libs.androidx.browser)

    testImplementation(platform(libs.compose.bom))
    testImplementation(libs.compose.ui.test.junit4)
    testImplementation(libs.robolectric)
    testImplementation(libs.androidx.test.ext.junit)
    testImplementation(libs.junit)
    testImplementation(libs.kotlinx.coroutines.test)
    testImplementation(libs.multiplatform.settings.test)
    testImplementation(kotlin("test"))
    debugImplementation(libs.compose.ui.test.manifest)
}

ktlint {
    version.set(libs.versions.ktlint.cli)
}
