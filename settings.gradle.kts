pluginManagement {
    repositories {
        gradlePluginPortal()
        mavenCentral()
    }
}

dependencyResolutionManagement {
    repositoriesMode = RepositoriesMode.FAIL_ON_PROJECT_REPOS
    repositories {
        mavenCentral()
    }
}

rootProject.name = "coldframe"

// Kotlin modules keep the monorepo layout: code under packages/kt or apps/kt, tests under tests/kt.
include(":core")
project(":core").projectDir = file("packages/kt/core")
