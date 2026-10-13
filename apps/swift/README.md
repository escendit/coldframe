# apps/swift

Swift runtimes.

| Folder | What | Arrives in |
| --- | --- | --- |
| `ios/` | The iOS app, a SwiftUI shell over the shared core | Story 1.5 |

- `ios/Sources/ColdframeIOS` is the part of the app without Kotlin: presentation models, the
  String Catalog and, where SwiftUI exists, the views. It is a SwiftPM target of the
  `Package.swift` in the repository root, so it builds and tests without Xcode. The views talk to
  the core only through service protocols such as `SignInService`, `AppearanceService`,
  `SitesService`, `LotsService`, `AlertsService`, `NotificationSettingsService`, `PushService`
  and `PauseService`.
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
  Silence (Story 7.2) is the Server's everywhere: no view derives a status, sorts Lots or measures
  a silence. The core hands over what is silent, since when, the short names ("3F2A") and every
  duration (`now − silentSince`, rounded down: minutes under 1 h, hours under 24 h, then days);
  the shell adds the catalogue's words. A value the core did not send, or one this build does not
  know, leaves the row as it was before.
  - *Hub notice* (`LotsPresentation`, `GardenView`, UX-DR81): one Inline notice per silent Hub of
    the Site above the tiles, under the push notices: "Hub 3F2A silent for 12 min — Lots behind
    it can't be read." It has no action, cannot be closed, shows also when no Lot is behind the
    Hub and is absent in stale mode. Its duration moves with the minute tick, so the notice posts
    nothing itself: `ShellModel` announces a Hub once, politely, when its notice appears
    (`HubNoticeAnnouncements`), and again only after it recovered and went silent again.
  - *Lot detail hero*: a Lot behind a silent Hub says "Hub 3F2A is silent; Lots behind it can't
    be read.", or the sentence without an ID when the Server names no Hub. A silent Node keeps
    "Check power or range."
  - *Devices rows* (`DevicesPresentation`, `DevicesView`): a silent Hub or Node shows the `help`
    icon and "Silent 6 h" with its last seen, in sentence case in the catalogue and upper case
    by style, and every row whose Device has a Silence Window from the Server names it:
    "Silence Window · 8 h".
  - *Silence Window sheet*: a row action on every Hub and Node, also a custom accessibility
    action of the row, for Administrators and Owners (`canSetSilenceWindow` of the snapshot). For
    a Member the control does not exist. The sheet shows the window in force, a number field
    with a unit of min, h or d (a Segmented choice), Save, Cancel, and "Use default" only when
    the Device has its own window. Save hands what was typed to the core
    (`setSilenceWindowText`), which refuses anything that is no whole number from 1 min to 30
    days and sends nothing; the sheet then stays open with "Enter a whole number from 1 min to
    30 d." under the field. A change the Server refused shows under the row; a 403 says "You
    can't change this on ‹Site›. Ask an Owner or Administrator."
  - *Health titles* (`AlertsPresentation`, UX-DR117): the core names the sentence and its parts:
    "Node on Lot 'Beans' silent for 6 h", "Hub 3F2A silent for 5 min", "Node 9C0D silent for
    6 h" for a Node on no Lot. A closed row, and an open one without a silent-since, drops
    " for …". A kind the app does not know is "Health Alert on Lot '‹Lot›'", or "Health Alert"
    without a Lot. Every silent row, and every row without a Lot, opens Devices.
  - Devices and Alerts each have a one-minute tick like the overview's: it reads the core's
    snapshot again (`current()`), so the silences move, and fetches nothing. A tapped silent
    notification still opens the overview: the push route carries no Device ID.
  - Low batteries (Story 7.3) are Health rows too: "Node battery 14 %, not charging — Lot
    'Lettuce'", or "Node 9C0D battery 14 %, not charging" for a Node on no Lot, with the level
    the Alert opened with (`alertBatteryPercents` of the core's snapshot). A closed row, and one
    without a level, reads "Node battery low on Lot 'Lettuce'" or "Node 9C0D battery low". They
    open Devices, which shows the Node's newest level and charger status, as before.
  An uncalibrated Sensor (Story 7.4, produced by the Server for a soil Sensor of a Node on a Lot) reads
  "Soil Sensor on Lot 'Carrots' needs Calibration" with the tools icon and opens Lot detail of that Lot.
  Pause (Story 8.2) is the Server's everywhere too: no view decides that something is paused, and
  none keeps it. The core hands over what is paused (`Lot.status` and `pausedBy`, `Site.paused`,
  the Devices' `pausedBy`), which control a Role has, and the state of the Pause sheet. Each of
  the three surfaces has its own `PauseService` (`LotsService.pause`, `LotDetailService.pause`,
  `DevicesService.pause`, adapted from the core's `IosPause` by `CorePauseService`) and carries
  its state as a `PausePresentation` (`pause` of the surface's presentation). Its actions are
  `openForSite`, `openForDevice`, `setScope`, `setUntil`, `cancel`, `save`, `resumeSite`,
  `resumeDevice` and `retry`. The controls are for Administrators and Owners and do not exist for
  a Member: hidden, never disabled.
  - *Pause sheet* (`PauseSheet`, UX-DR46): a native sheet, open while the core has one open.
    The scope is a Segmented choice: "Whole Site" alone from the Site menu, "This Device"
    (selected) and "Whole Site" from a Device. "Until" is optional: the field reads "No end
    date" with the helper "Leave empty to pause until someone resumes.", and a tap opens the
    native calendar inside the sheet, never on top of it. The native calendar always marks a
    day, so opening it on an empty field picks tomorrow, and "Clear date" empties the field
    again. The picked calendar date goes to the core in numbers (`PauseDay`), the day the
    calendar showed: no time zone moves it, and no date is greyed out, since which dates are
    allowed is the Server's rule. Cancel stands beside a primary button that names the
    result, "Pause Home garden" or "Pause 7C19", and reads "Pausing…" while the request runs;
    the sheet then cannot be swiped away. A date the Server refused marks the field ("Choose a
    date after today."); a 403 says "You can't change this on ‹Site›. Ask an Owner or
    Administrator."; any other refusal is the surface's notice, with Try again where that can
    help. The sheet stays open with what was entered. It is never opened over another sheet of
    its surface (the Site switcher, the Move picker, the Silence Window sheet).
  - *Site menu* (`SiteMenu`, UX-DR22): "Pause ‹Site›" opens the sheet, or "Resume ‹Site›" while
    the Server says the Site is paused, which resumes with one tap; then "Site settings". A
    Member has only "Site settings". In stale mode every item is disabled with "Needs your
    Server".
  - *Overview* (`GardenView`, UX-DR83): a paused Site reads "Paused until 1 Mar" or "Paused" in
    paused ink, adds the pause icon with "No Alerts are sent while the Site is paused." under
    the summary, and every tile with a Node is the paused one ("Paused by Site"). A Member also
    reads the Inline notice "Only Owners and Administrators can resume it."
  - *Lot detail* (`LotDetailView`, UX-DR70): "Pause" under the hero opens the sheet for the
    Lot's Node; "Resume" lifts the Node's own Pause with one tap and reads "Resuming…" meanwhile.
    A Node paused only by its Site has no Resume: the hero says "Resume the Site to resume this
    Node", and Pause stays. The hero is one accessibility element, so the controls are below
    it. A Member has neither control and reads the same Inline notice on a paused Lot.
  - *Devices rows* (`DevicesView`): a paused Hub or Node shows the pause icon and "Paused" or
    "Paused by Site" in paused ink, in sentence case in the catalogue and upper case by style.
    Pause and Resume are row actions, also custom accessibility actions of the row ("Pause Hub
    ‹ID›", "Resume Node ‹ID›"); while one request runs no row starts another. A Device paused
    only by its Site says "Resume the Site to resume this Node" (a Hub row: "… this Hub").
  - A Resume that did not happen is an Inline notice on its surface (under its row on Devices),
    with Try again where that can help. Shown ends are the Server's `pausedUntil`, written by
    the day formatter in the phone's zone. Purple and the pause icon mean paused and nothing
    else. There is no "Paused by ‹name›" line, no paused notice on the Add a Hub and Add a Node
    outcome screens, no Resume inside Calibrate and no swipe action.
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
  the core sends it to the Server and reads it from there. Nothing here delivers a notification;
  the permission and the push token belong to `PushService`.
  `PushService` (Story 6.5) carries push on this phone as a `PushPresentation` built from the
  core's flat `PushSnapshot`: whether the notice shows, whether the prompt is due, and where a
  tapped notification leads. Its actions are `refresh`, `ask`, `openSettings` and `routeHandled`.
  On the first landing on a Site overview an Inline notice above the tiles reads "Coldframe tells
  you when a Lot needs water." with Continue, which opens the OS prompt for alerts and sound,
  once per phone (UX-DR122). While the permission is denied or revoked, "Notifications are off
  for Coldframe on this phone. You won't get Alerts." with Open Settings stands first in My
  notifications and above the tiles of the overview; it cannot be closed, and the app reads the
  permission again every time it comes to the front (UX-DR88). A tapped notification hands the
  `coldframe` object of its payload to the core, which switches to its Site and names the route
  once the Sites are loaded, also after a cold start: Lot detail for an Alert or a Reminder, the
  overview for a summary or an unknown Lot. The root hoists the Garden tab's stack (`GardenPath`)
  next to the selected tab, so the shell shows the route on the Garden tab from wherever it is
  and then calls `routeHandled` (UX-DR120). The Server writes every notification and groups it
  per Site; in the foreground it shows as banner and in the list with sound. The app sets no
  badge, adds no category or action and posts no notification of its own (UX-DR121). The core
  sends the device token to the Server after sign-in and removes the registration at sign-out;
  Swift never calls the Server.
- `ios/App` is the app target: `ColdframeApp` and the adapters that import `ColdframeCore`
  (`CoreSignInService`, `CoreAppearanceService`, `CoreSitesService`, `CoreLotsService`, `CoreDevicesService`,
  `CoreHubSetupService`, `CoreNodeSetupService`, `CoreAlertsService`,
  `CoreNotificationSettingsService`, `CorePushService`, `CorePauseService`), the
  static framework Gradle builds from [`packages/kt/core`](../../packages/kt/core).
  `CorePushService` holds the OS calls of push: it reads `UNUserNotificationCenter`'s
  authorization, shows the prompt, opens the app's notification settings and, once the
  permission is granted, calls `registerForRemoteNotifications`. `AppDelegate`
  (`UIApplicationDelegateAdaptor`) receives the APNs device token, which goes to the core as
  lowercase hex with the build's APNs environment, and is the notification centre's delegate for
  taps and for notifications in the foreground; `PushInbox` keeps what arrives before the
  adapter exists.
- `ios/project.yml` is the XcodeGen spec; `xcodegen generate` writes `Coldframe.xcodeproj`, which
  is not committed. `ios/Config` holds the build-time configuration and the Info.plists.
- Push is off in a build from this repository: `COLDFRAME_PUSH = NO` in
  `ios/Config/Coldframe.xcconfig` leaves `CODE_SIGN_ENTITLEMENTS` empty, so the app builds with
  any team, with a free personal team and with signing off (CI), asks for the permission as
  usual, gets no device token and registers nothing. For real pushes set `COLDFRAME_PUSH = YES`
  and `DEVELOPMENT_TEAM` in `ios/Config/Coldframe.local.xcconfig` (not committed): the app is
  then signed with `ios/Config/Coldframe.entitlements`, whose `aps-environment` is
  `COLDFRAME_APS_ENVIRONMENT`, `development` (APNs sandbox) for Debug and `production` for
  Release. The Info.plists repeat that value, and the app sends it with the device token so
  the Server uses the matching APNs host. The team needs the Apple Developer Program and the
  Push Notifications capability on the App ID, and the Server needs the APNs key of the same
  team with the app's bundle identifier as its topic. No provisioning profile, key or team ID
  is committed.

Tests live in [`tests/swift`](../../tests/swift). Build and run the app as described in
[`docs/quickstart.md`](../../docs/quickstart.md#run-the-ios-app).
