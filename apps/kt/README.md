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
- ADD A NODE on "Hub is online" closes the flow and opens Add a Node for the Hub's Site.

## Add a Node (Story 4.3)

Administrators and Owners start Add a Node from three places: "Add a Node" in the Devices header
(beside "Add a Hub"), a *no Node* Lot tile on the Garden (which preselects that Lot), and ADD A
NODE on "Hub is online". A Member has none of them: the header actions are hidden and the tile is
not a button. The flow (`ui/setup/AddNodeFlow.kt`) renders the core's `NodeSetupState` in the same
Setup flow shell as Add a Hub; the candidate tile, the outcome layout, the leave dialog and the
announcement region are shared by both flows.

1. **Press** — "Press the setup button on the Node": hold it for 3 seconds; the Node then listens
   for 3 minutes. No radio yet.
2. **Scan** — only adverts named `Coldframe Node XXXX`, strongest first; "Pressed just now" marks
   the one first heard last. After 30 s without one a notice says how to wake it; scanning goes on.
3. **Code** — one BLE session per attempt: connect, hello, `IdentityRequest` (a reply that does
   not open is the wrong code), `GET /enrolment-key` with the fingerprint checked, then
   `EnrolmentRequest` → `EnrolmentResponse`. The core keeps the sealed key in memory, disconnects,
   and only then shows the accepted chip. A Node is never sent a Site binding or Wi-Fi message.
4. **Lot** — no BLE. Lots from `GET /sites/{siteId}/lots`; a Lot that has a Node says "Has a Node"
   and cannot be picked; "+ New Lot" creates one inline with the Create Lot copy. "Put 7C19 in
   Tomatoes" posts `POST /sites/{siteId}/devices` (`kind: node`, the sealed key, `lotId`) with one
   Idempotency-Key per Device and Site. A Lot taken meanwhile, a Lot that is gone and an
   unreachable Server are answered on this step, and another Lot needs no second BLE session.
5. **Outcome** — "Tomatoes has a Node"; the Lots and the Devices are read again.

Top-left is Cancel on step 1 and Back on steps 2 and 3; Back on step 4 and system back once a Node
is selected ask "Stop setting up Node 7C19? Nothing is saved on the Node." The candidate tile shows
no battery or Sensor count (neither the advert nor the Node's identity carries them), and the
outcome shows no Sensors or Calibrate action yet (see `deferred-work.md` DW-55 to DW-58). At font
scale 1.5 and larger the two Devices header actions stack, so the heading keeps its width.

## Alerts (Story 6.2)

The Alerts tab (`ui/alerts/`) renders the core's `AlertsState`: "Threshold Alerts", then "Health
Alerts", then "Closed" (the last 7 days), each in the Server's order; a group without rows is not
shown. Without an open Alert it says "No open Alerts.", followed by Closed when it has rows. A
failed load shows the notice with Try again and no rows.

- **Row** (`AlertRow.kt`, DESIGN.md `alert-row-*`): needs water is the only solid orange row;
  another Threshold Alert is `layer-01` with a 2 dp `border-strong` outline and an arrow for its
  side; a Health Alert is hatched and dashed with its text on a plate; a closed Alert is an outline
  in `text-secondary` whose eyebrow ends in "closed 06:40". Which variant, icon and eyebrow a row
  has is the core's answer; `AlertsCopy.kt` only turns it into the catalogue's words. A row shows no
  value and no Threshold.
- **One tap target.** A row is one button of at least 48 dp with one spoken label ("Tomatoes needs
  water, since 05:45"; a closed one adds "closed 06:40") and no other action. Threshold and
  uncalibrated rows switch to the Garden tab and open Lot detail there, so Back returns to the
  overview; silent and battery rows open the Devices tab.
- **Tab label.** "Alerts · 5" while the current Site has 5 open Alerts, spoken "Alerts, 5 open";
  "Alerts" otherwise, also while loading and after a failed load. The core reads the Alerts when a
  Site becomes current, `MainActivity` reads them again on every foreground, and the shell on
  every entry of the tab; pull-to-refresh shows the `primary` bar of the Site overview.

Health Alerts have no producer before Epic 7: their rows are built and tested from fixtures
(`tests/kt/android/test/.../AlertFixtures.kt`).

## My notifications and Reminders (Story 6.3)

Settings lists My notifications first, above Site settings. The screen (`ui/notifications/`) renders the
core's `NotificationSettingsState` and reads it again on every entry.

- **Notification Window control** (`NotificationWindowControl.kt`): two time fields that open the Material
  time input (24 h), a 24 h bar (`primary` inside the window, hatched outside; decorative, with no
  semantics), the window in big type ("07:00 to 22:00"), the helper naming the window's own start, and
  Save, which reads "Saving…" in place and is followed by a polite "Saved.".
- **Time-zone confirm panel** (`ui/components/TimeZonePanel.kt`): the same component as on Create Site. It
  proposes the phone's zone until the User has chosen one, then names the chosen zone; without a zone to
  propose it shows the list at once.
- **Mute** is a Material `Switch` whose row is the control, labelled "Mute ‹Site›". **My Reminder cadence**
  is a Segmented choice "Use Site setting" / "Daily" / "Every 2 days" with the helper "Site setting: Daily".
  Both apply at once and need a current Site; without one only the window and the time zone show.
- A change that was not saved puts its control back at the Server's value and shows an Inline notice under
  it, with Try again where that can help.

Site settings ends with **Reminders**: Owners and Administrators pick the Site's Reminder cadence, "Daily"
or "Every 2 days"; a Member reads it as text. The section is absent until the Server has answered.

The zone confirmed on Create Site is no longer kept on the phone: the core sends it to the Server and reads
it from there. Nothing here delivers a notification, asks for the notification permission or registers a
push token (Stories 6.4 to 6.6).
