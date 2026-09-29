pluginManagement {
    repositories {
        google()
        gradlePluginPortal()
        mavenCentral()
    }
}

dependencyResolutionManagement {
    repositoriesMode = RepositoriesMode.FAIL_ON_PROJECT_REPOS
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "coldframe"

// Kotlin modules keep the monorepo layout: code under packages/kt or apps/kt, tests under tests/kt.
include(":core")
project(":core").projectDir = file("packages/kt/core")

include(":design-tokens")
project(":design-tokens").projectDir = file("packages/kt/design-tokens")

include(":android")
project(":android").projectDir = file("apps/kt/android")
