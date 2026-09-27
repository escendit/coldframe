plugins {
    alias(libs.plugins.kotlin.multiplatform)
    alias(libs.plugins.ktlint)
}

group = "com.escendit.coldframe"
version = "0.0.0"

// Tests live under tests/kt/, mirroring the module (see docs/quickstart.md).
val testRoot = rootDir.resolve("tests/kt/core")

kotlin {
    explicitApi()
    jvmToolchain(25)

    // Android and iOS targets arrive with the mobile sign-in story.
    jvm()

    sourceSets {
        commonTest {
            kotlin.setSrcDirs(listOf(testRoot.resolve("commonTest/kotlin")))
            resources.setSrcDirs(listOf(testRoot.resolve("commonTest/resources")))

            dependencies {
                implementation(kotlin("test"))
            }
        }
    }
}

ktlint {
    version.set(libs.versions.ktlint.cli)
}
