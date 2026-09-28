# packages/kt

Kotlin libraries.

| Folder | What | Arrives in |
| --- | --- | --- |
| `core/` | The shared Kotlin Multiplatform core: BLE setup, sign-in, API client, push tokens | Present as a skeleton, filled from Epic 1 on |
| `design-tokens/` | Colours, typography, spacing, radii and Carbon icon paths, Kotlin common code only (Gradle project `:design-tokens`) | Story 1.3; `generated/` is written by [`packages/design-tokens`](../design-tokens) |

`core/` and `design-tokens/` build for the JVM only until the mobile sign-in story adds the Android and iOS targets.
Tests live in [`tests/kt`](../../tests/kt).
