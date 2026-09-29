# apps/swift

Swift runtimes.

| Folder | What | Arrives in |
| --- | --- | --- |
| `ios/` | The iOS app, a SwiftUI shell over the shared core | Story 1.5 |

- `ios/Sources/ColdframeIOS` is the part of the app without Kotlin: presentation models, the
  String Catalog and, where SwiftUI exists, the views. It is a SwiftPM target of the
  `Package.swift` in the repository root, so it builds and tests without Xcode. The views talk to
  the core only through the `SignInService`, `AppearanceService`, `SitesService` and
  `LotsService` protocols.
  `SitesService` (Story 1.8) carries the Sites of the signed-in user as a `SitesPresentation`
  (Create Site, the empty Garden, the Site switcher and the Site menu) built from the core's flat
  `SitesSnapshot`, plus the actions `load`, `select`, `newSite`, `cancelNewSite`, `setName`,
  `confirmTimeZone`, `changeTimeZone`, `pickTimeZone`, `submit` and `availableTimeZones`.
  `LotsService` (Story 1.9) carries the current Site's Lots and Site settings as a
  `LotsPresentation` built from the core's flat `LotsSnapshot`: the Lot tiles on Garden in the
  Server's order, and Site settings (rename Site for an Owner; create, rename and remove Lots for
  Owners and Administrators; read-only with one notice for a Member). Its actions are `load`,
  `setSiteName`, `renameSite`, `setNewLotName`, `createLot`, `startRename`, `setRename`, `rename`,
  `cancelRename`, `askRemove`, `confirmRemove` and `cancelRemove`.
  `HubSetupService` (Story 3.6) carries Add a Hub as a `HubSetupPresentation` built from the
  core's flat `HubSetupSnapshot`: the five steps in the Setup flow shell, the Setup progress and
  the outcome screens. BLE (Kable over Core Bluetooth), the session crypto and every rule stay in
  the core; the views keep the idle timer off while the flow is open, move VoiceOver focus to
  each step title or headline, post the announcements and report `announcing` until VoiceOver
  finishes one, so the progress timeout waits (UX-DR103). The app needs
  `NSBluetoothAlwaysUsageDescription` (both Info.plists) and links `CoreBluetooth`. WPA3-only
  networks are shown but cannot be chosen.
- `ios/App` is the app target: `ColdframeApp` and the adapters that import `ColdframeCore`
  (`CoreSignInService`, `CoreAppearanceService`, `CoreSitesService`, `CoreLotsService`,
  `CoreHubSetupService`), the
  static framework Gradle builds from [`packages/kt/core`](../../packages/kt/core).
- `ios/project.yml` is the XcodeGen spec; `xcodegen generate` writes `Coldframe.xcodeproj`, which
  is not committed. `ios/Config` holds the build-time configuration and the Info.plists.

Tests live in [`tests/swift`](../../tests/swift). Build and run the app as described in
[`docs/quickstart.md`](../../docs/quickstart.md#run-the-ios-app).
