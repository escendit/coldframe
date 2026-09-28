# apps/kt

Kotlin runtimes.

| Folder | What | Arrives in |
| --- | --- | --- |
| `android/` | The Android app (Gradle project `:android`), a Jetpack Compose shell over the shared core | Story 1.5 |

The shell holds UI only: it renders the core's `SignInState` and `ThemePreference` and never sees
a token. Every string is in `android/src/main/res/values/strings.xml`, with the same keys and
English values as the iOS String Catalog. The shared core lives in
[`packages/kt/core`](../../packages/kt/core). Tests live in [`tests/kt/android`](../../tests/kt/android).
Build and run it as described in [`docs/quickstart.md`](../../docs/quickstart.md#run-the-android-app).
