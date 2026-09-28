# packages/kt

Kotlin libraries.

| Folder | What | Arrives in |
| --- | --- | --- |
| `core/` | The shared Kotlin Multiplatform core: sign-in and session (story 1.5), later BLE setup, API client and push tokens | Story 1.5 for sign-in |
| `design-tokens/` | Colours, typography, spacing, radii and Carbon icon paths (Gradle project `:design-tokens`) | Story 1.3; `generated/` is written by [`packages/design-tokens`](../design-tokens) |

`core/` builds for the JVM (tests), Android and iOS (`iosArm64`, `iosSimulatorArm64`, the static
framework and XCFramework `ColdframeCore`). It runs OIDC Authorization Code + PKCE through
`kotlin-multiplatform-oidc`, keeps the tokens in the library's store and exposes one observable
state per concern (`SignInEngine.state`, `AppearanceStore.theme`); `jvmShared` holds the JVM and
Android certificate classifier. `design-tokens/` builds for the JVM and Android; iOS uses the
Swift package in [`packages/swift/design-tokens`](../swift/design-tokens).
Tests live in [`tests/kt`](../../tests/kt).
