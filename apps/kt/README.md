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

## Add a Hub (Story 3.6)

The first-run "Add a Hub" tile opens a full-screen five-step flow for Administrators and Owners
(`ui/setup`). The core's `HubSetupEngine` does all of it: BLE through Kable, the setup session,
enrolment and every error rule; the Compose screens render its `HubSetupState` and forward taps.
The screen stays on while the flow is open, focus moves to each step title, and announcements are
live regions; while TalkBack is on the core's 90 s timeout waits for an estimated reading time.

- **Permissions**: `BLUETOOTH_SCAN` (`neverForLocation`) and `BLUETOOTH_CONNECT` from Android 12;
  `BLUETOOTH`, `BLUETOOTH_ADMIN` and `ACCESS_FINE_LOCATION` up to Android 11 (`maxSdkVersion` 30),
  where BLE scanning also needs location services on. The flow asks at runtime when it opens;
  denied or Bluetooth off shows "Coldframe needs Bluetooth to find the Hub." with Open Settings.
- **WPA3-only networks** are listed but cannot be chosen: the Hub joins WPA2 and WPA2/WPA3
  transition networks only.
- ADD A NODE on "Hub is online" closes the flow to the Garden until the Node flow (Epic 4).
