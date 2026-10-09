# apps/swift

Swift runtimes.

| Folder | What | Arrives in |
| --- | --- | --- |
| `ios/` | The iOS app, a SwiftUI shell over the shared core | Story 1.5 |

- `ios/Sources/ColdframeIOS` is the part of the app without Kotlin: presentation models, the
  String Catalog and, where SwiftUI exists, the views. It is a SwiftPM target of the
  `Package.swift` in the repository root, so it builds and tests without Xcode. The views talk to
  the core only through service protocols such as `SignInService`, `AppearanceService`,
  `SitesService`, `LotsService`, `AlertsService` and `NotificationSettingsService`.
  `SitesService` (Story 1.8) carries the Sites of the signed-in user as a `SitesPresentation`
  (Create Site, the empty Garden, the Site switcher and the Site menu) built from the core's flat
  `SitesSnapshot`, plus the actions `load`, `select`, `newSite`, `cancelNewSite`, `setName`,
  `confirmTimeZone`, `changeTimeZone`, `pickTimeZone`, `submit` and `availableTimeZones`.
  `LotsService` (Story 1.9) carries the current Site's Lots and Site settings as a
  `LotsPresentation` built from the core's flat `LotsSnapshot`: the Lot tiles on Garden in the
  Server's order, and Site settings (rename Site for an Owner; create, rename and remove Lots for
  Owners and Administrators; read-only with one notice for a Member). Its actions are `load`,
  `setSiteName`, `renameSite`, `setNewLotName`, `createLot`, `startRename`, `setRename`, `rename`,
  `cancelRename`, `askRemove`, `confirmRemove`, `cancelRemove`, `setReminderCadence` and
  `retryReminderCadence`. Site settings ends with Reminders (Story 6.3): Owners and
  Administrators pick the Site's Reminder cadence, "Daily" or "Every 2 days", in a Segmented
  choice that applies at once; a Member reads it as text. The section is absent until the Server
  has answered, and a cadence that was not saved shows its notice there with Try again.
  `HubSetupService` (Story 3.6) carries Add a Hub as a `HubSetupPresentation` built from the
  core's flat `HubSetupSnapshot`: the five steps in the Setup flow shell, the Setup progress and
  the outcome screens. BLE (Kable over Core Bluetooth), the session crypto and every rule stay in
  the core; the views keep the idle timer off while the flow is open, move VoiceOver focus to
  each step title or headline, post the announcements and report `announcing` until VoiceOver
  finishes one, so the progress timeout waits (UX-DR103). The app needs
  `NSBluetoothAlwaysUsageDescription` (both Info.plists) and links `CoreBluetooth`. WPA3-only
  networks are shown but cannot be chosen.
  `NodeSetupService` (Story 4.3) carries Add a Node as a `NodeSetupPresentation` built from the
  core's flat `NodeSetupSnapshot`: press the setup button, pick the Node ("Pressed just now" on
  the one first heard last), its setup code, the Lot picker ("Has a Node" rows cannot be picked,
  "+ New Lot" creates one inline) and the outcome "Tomatoes has a Node". It opens from "Add a
  Node" in Devices, from a *no Node* Lot tile (with that Lot preselected) and from "Add a Node" on
  "Hub is online", for Administrators and Owners only (`canAddNode` of the Devices and Lots
  snapshots). The session order is the core's: the BLE session runs on the code step and ends
  before the accepted chip shows; the Lot step needs no BLE and posts the sealed key with the Lot.
  The shell, the leave question, the candidate tile, the code field, the outcome view and the
  announcements (`SetupFlowEffects`) are shared with Add a Hub.
  `AlertsService` (Story 6.2) carries the current Site's Alerts as an `AlertsPresentation` built
  from the core's flat `AlertsSnapshot`: open Threshold Alerts, then open Health Alerts, then the
  Alerts closed in the last 7 days under "Closed", in the Server's order, or "No open Alerts.".
  The core names each row's group, variant, condition, eyebrow, icon and target; the shell words
  it and draws the four variants (orange only for *needs water*, neutral Threshold, hatched
  Health, outlined Closed). A row is one button with one spoken label ("Tomatoes needs water,
  since 05:45"): a Threshold or uncalibrated row pushes Lot detail on the Alerts tab's own stack,
  a silent or battery row opens Devices. The tab label reads "Alerts · 5" while Alerts are open
  (spoken "Alerts, 5 open"); the core reads the count when a Site becomes current, and the app
  calls `refresh` whenever it comes to the front and on pull-to-refresh, `load` on every entry
  of the tab and for Try again. A failed load shows the notice, no rows and no count.
  `NotificationSettingsService` (Story 6.3) carries My notifications as a
  `NotificationSettingsPresentation` built from the core's flat `NotificationSettingsSnapshot`.
  Settings lists My notifications first, above Site settings; the screen calls `load` on every
  entry. It shows the Notification Window control (two native time pickers, a 24 h bar that is
  `primary` inside the window and hatched outside, decorative and hidden from VoiceOver, the
  window in big type "07:00 to 22:00", the helper naming the window's own start, and Save, which
  reads "Saving…" in place and is followed by a polite "Saved."), the time-zone confirm panel
  (the same `TimeZonePanel` as on Create Site, now built over `TimeZoneActions`; without a zone
  to propose it shows the list at once), and for the current Site the "Mute ‹Site›" native
  `Toggle` and my Reminder cadence, a Segmented choice "Use Site setting" / "Daily" / "Every 2
  days" with the helper "Site setting: Daily". The switch and the cadence apply at once and need
  a current Site; without one only the window and the time zone show. A change that was not
  saved puts its control back at the Server's value and shows an Inline notice under it, with Try
  again where that can help. The zone confirmed on Create Site is no longer kept on the phone:
  the core sends it to the Server and reads it from there. Nothing here delivers a notification,
  asks for the notification permission or registers a push token (Stories 6.4 to 6.6).
- `ios/App` is the app target: `ColdframeApp` and the adapters that import `ColdframeCore`
  (`CoreSignInService`, `CoreAppearanceService`, `CoreSitesService`, `CoreLotsService`, `CoreDevicesService`,
  `CoreHubSetupService`, `CoreNodeSetupService`, `CoreAlertsService`,
  `CoreNotificationSettingsService`), the
  static framework Gradle builds from [`packages/kt/core`](../../packages/kt/core).
- `ios/project.yml` is the XcodeGen spec; `xcodegen generate` writes `Coldframe.xcodeproj`, which
  is not committed. `ios/Config` holds the build-time configuration and the Info.plists.

Tests live in [`tests/swift`](../../tests/swift). Build and run the app as described in
[`docs/quickstart.md`](../../docs/quickstart.md#run-the-ios-app).
