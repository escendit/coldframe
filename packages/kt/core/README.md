# packages/kt/core

The shared Kotlin Multiplatform core (Gradle project `:core`). The overview of the whole module is
in [`packages/kt`](../README.md); this file covers Lot status and stale mode (Story 4.7) and what was built on
them since, up to Pause and Resume (Story 8.2). Tests
live in [`tests/kt/core`](../../../tests/kt/core).

## Lot status

The Server computes a Lot's status once (AD-14). The core mirrors the `Lot` schema of
`packages/openapi/coldframe.openapi.json` in `api/LotDto` (`OpenApiContractTest` holds the two
together) and maps it one to one to `lots/LotSummary`: `status`, `statusSince`, `lastReadingAt`,
`unknownCause` (`node`, `hub`), `silentSince`, `silentHubId`, `pausedBy` (`device`, `site`),
`pausedUntil`, `moisturePercent` and `lowThresholdPercent`. Times become epoch milliseconds; a value this client does not know is
left out, and a status it does not know reads as `unknown`, never as fine.

| The Server decides | The core decides |
| --- | --- |
| The status of every Lot and its precedence | Whether the shown Lots are live or stale (transport only) |
| The order of the Lots; the core never re-sorts | One retry of a failed read |
| `statusSince`, `lastReadingAt`, `unknownCause`, `silentSince`, `silentHubId`, `pausedBy`, `pausedUntil` | The last good Lots per Site, kept on the device |
| The percentages (`moisturePercent`, `lowThresholdPercent`) | How they are written: `~` and the nearest 5 % for soil moisture |
| | Which headline, counts and tile a set of statuses gets, and durations measured from the Server's timestamps |

The core counts statuses and measures time. It never derives a status, and it has no age
threshold: old data from a Server that answers is live.

### The overview model

`lots/LotsOverview.of(ready, nowEpochMs)` turns a `LotsState.Ready` into what both shells draw.
It is structured data; the strings are in the shells' catalogues.

- `LotsHeadline` — the first case that applies: `NoReadings` (no Lot has a Node), `NeedsWater`
  (with the Lot's name for one Lot, else the count), `CantBeRead` (the count of unknown and
  needs-calibration Lots), `Paused` (every Lot with a Node is paused by the Site; with the end
  only when they all share it; paused ink), `NothingNeedsWater`.
- `LotCounts` — the non-zero counts in the Server's order without needs water: needs calibration,
  unknown, OK, paused, without Node. Empty when no Lot has a Node.
- `LotTile` — one per Lot, in the Server's order: the variant (six statuses and `Stale`), the
  status label (`Silent` / `HubSilent` from `unknownCause`, `PausedBySite` when `pausedBy` has the
  Site), the value, the foot line, which spoken label applies, and their parts. The silence of an
  unknown Lot is measured from the Server's `silentSince` (when the silent Node or Hub was last
  heard). Without it (a Server that did not record it) it is measured as before Story 7.2: from the
  last Reading for a silent Node and from `statusSince` for a silent Hub or a Lot without a Reading.
  A needs-calibration tile never carries a percentage. A field the variant does not show is `null`.
- `HubNotice` — one per silent Hub of the Site (`LotList.silentHubs`, kept in
  `LotsState.Ready.silentHubs`), in the Server's order, whether or not a Lot is behind the Hub:
  the Hub's short name and its silence. None in stale mode, and none for Lots shown from this
  device on a cold start: what is not live does not say "silent for".
- `SoilMoisture`, `LotDuration` ("min" under 1 h, "h" under 24 h, "d" from 24 h) and `StaleAge`
  (days, hours, minutes for the stale header) are the shared formatters.

The shells build the overview again on their minute tick, so the stale age and the durations
move. The core has no timer.

### Stale mode

`lots/LotsEngine` decides it, for the Site overview only. Devices and Site settings have no stale
mode.

- **Transport failures only.** `ApiFailure.transport` is true for no answer (unreachable,
  timeout), a 5xx and an answer that is not the expected one. Nothing else is served stale.
- **Retry.** A read of the Lots, and of the Sites, that fails for a transport reason is tried once
  more, at once.
- **Entering.** When a read and its retry fail over shown Lots, they stay, with
  `staleReason = Unreachable`. This holds for `refresh()` (pull-to-refresh, coming to the front)
  and for the reload after a change.
- **403 and 404** on the Lots read drop the Site's stored Lots, and the load notice replaces the
  Lots that were showing, live or not.
- **Certificate failure** always shows its notice (`LotsState.Failed`, `SitesState.Failed`), with
  or without stored data, and is not retried.
- **401** ends the session and clears everything stored (below).
- **Leaving.** The first read that succeeds clears `staleReason`.
- **`fetchedAtEpochMs`** is the time of the last successful read, from the engine's clock
  (`now`, injected in tests). It is the "as of" of stale tiles and the "Last data" of the stale
  header, and it does not move while reads fail.
- **Cache.** Every successful read stores the Server's rows and their time under
  `lots.lastGood.‹siteId›` in the core's key-value `Settings` (the app's SharedPreferences on
  Android, `NSUserDefaults` on iOS), and every successful read of the Sites stores them under
  `sites.lastGood`. An entry that cannot be read is dropped.
- **One user's data is never shown to the next.** The core does not know a user ID (only a display
  name), so the keys are not per user. Instead the stored Sites and the stored Lots of every Site
  are removed when the session ends: on `SignInState.SignedOut` and on a 401 from any Sites or
  Lots call. A cold start that is still restoring its session removes nothing.
- **Cold start.** `sites/SitesEngine` shows the stored Sites at once
  (`SitesState.Ready.fromCache`) while it reads them, and keeps them when the read and its retry
  fail for a transport reason, so there is a current Site without the Server. A Site with stored
  Lots shows them at once with `staleReason = Cached` and `refreshing`, until the first read lands;
  when that read fails they turn `Unreachable`. Without stored Lots the state is `Loading`
  (skeleton tiles, "Loading ‹Site›"), and `Failed` with the load notice when the Server does not
  answer. Without stored Sites the Sites load notice shows, as before.
- **Back home.** `LotsEngine.refresh()` also reads the Sites again: as `load` does while they are `fromCache`,
  else quietly, keeping the shown ones (`SitesEngine.refresh`).
- **Events.** `LotsEngine.events` sends `EnteredStale(siteId, fetchedAtEpochMs)` once when a read
  and its retry fail, and `LeftStale(siteId)` on the first success after that. Showing stored Lots
  on a cold start, a first load, a refresh that changes nothing and a further failed refresh send
  nothing. Events are not replayed.
- **In stale mode** every tile is `Stale` (no value, "as of"), a *no Node* tile does not start Add
  a Node, and `LotsState.siteMenu()` returns every Site menu item disabled ("Needs your Server"), Pause or
  Resume included.

`fromCache` is not a stale mode: the Site switcher and Create Site work on the stored list, the
Devices list shows its own notice, and Site settings reads `LotsState.stale` if it wants to say
that its Lots are not live.

iOS gets all of it through the flat `LotsSnapshot` (`IosLots.watch`, `IosLots.current()` for the
minute tick, `IosLots.watchEvents`, `IosLots.refresh()`); Android reads `AndroidSignIn.lots`.

## Lot detail and Device status (Story 4.8)

`lots/LotDetailEngine` reads one Lot (`getLot`, which carries `node` and `sensors` for a Lot that
holds a Node) and the paged daily history of the picked quantity (`getLotHistory`, following
`nextCursor` up to four pages). It has the Lots engine's stale rules: a transport failure is
retried once, then the last good detail stays with `staleReason = Unreachable`; a cold start shows
the stored detail (`lotDetail.lastGood.‹siteId›.‹lotId›`, with the histories read for it) as
`Cached`; 403/404 drop it and show a notice; the stored entries are cleared when the session ends.
`events` sends the same `EnteredStale` / `LeftStale` pair.

`lots/LotDetail` is the shared presentation model, built for one `now`: `LotDetail.of(ready, now)`
gives the hero (the tile's variant, label and spoken label, `raw N` while `needsCalibration`,
`moisturePercent` only when the Server sends it, the UX-DR78 note), the Sensor cells, the Device
cells (`battery--low` below 20 %) and the History chart (30 UTC days ending today, gaps for days
without Readings, daily lows scaled to the axis). While stale, the Sensor and Device cells are
`null`: no live value is drawn. `SensorFormat` only formats: soil `raw N`, whole °C and %RH, kΩ to
3 significant digits. Units and quantities are converted by the Server; the client never converts,
aggregates or computes a status.

The Devices engine lists Nodes (`NodeSummary`: Lot name, battery, charging, last seen) after the
Hubs, in the Server's order (Lot name, unassigned last, Device ID); the client does not sort them.
For Swift, `IosLotDetail` observes a flat `LotDetailSnapshot`, and `DevicesSnapshot` gains the
parallel `node…` lists.

**Silence and the Silence Window (Story 7.2).** The Server says what is silent and since when; the
core measures and names, so Android and iOS agree.

- **Names and durations.** `devices/DeviceName.short` is the first four hex digits of a Device ID
  in upper case ("Hub 3F2A"); rows keep the full ID. Every silence is `LotDuration.between(silentSince,
  now)`, rounded down: minutes under 1 h, hours under 24 h, then days.
- **Devices.** `HubSummary` and `NodeSummary` carry `silentSinceEpochMs` (set exactly while the
  Server lists an open silent Alert), `silenceWindowSeconds` (the window in force, `null` when the
  Server sent none) and `silenceWindowOverridden`, with `silence(now)`, `silenceWindow`
  (`SilenceWindowLength`: the largest of min, h, d that holds the window exactly) and `shortName`.
- **Setting it.** `DevicesEngine.setSilenceWindow(deviceId, windowSeconds)` over
  `PUT /sites/{siteId}/devices/{deviceId}/silence-window` (`DevicesApi.setSilenceWindow`); `null` is
  "use the default" and sends a body without `windowSeconds`. `DevicesState.canSetSilenceWindow` is
  true for an Administrator or Owner; a Member's call is ignored. `SilenceWindow` holds the range
  (60 to 2 592 000 s) and `secondsOf(text, unit)`, which reads a whole number of minutes, hours or
  days; a value outside the range is refused with `SilenceWindowNotice.OutOfRange` before anything
  is sent. The list is read again after the Server accepted. A refusal is
  `DevicesState.Ready.silenceWindowFailure` (403 `Forbidden`, 404 `NotFound`, 400 `OutOfRange`,
  `Unreachable`, `Certificate`, `Unexpected`) and nothing else changes; a 401 ends the session.
- **Lot detail.** `LotDetailHero.silentHubName` is the short name of `Lot.silentHubId` for the
  `HubSilent` note, `null` when the Server names no Hub.

For Swift, `DevicesSnapshot` gains the parallel `hub…` and `node…` silence lists,
`canSetSilenceWindow` and the `silenceWindow…` fields, with `IosDevices.setSilenceWindow`,
`setSilenceWindowText`, `useDefaultSilenceWindow` and `current()`; `LotsSnapshot` gains the
`hubNotice…` lists and `LotDetailSnapshot` `heroSilentHubName`.

**Thresholds (Story 5.4).** `thresholds/ThresholdsEngine` is Set Thresholds of one Lot: it reads the Lot and each
Sensor's `getSensorThresholds` (Member allowed) into a `ThresholdColumn` per Sensor (draft `low`/`high`, the
Server's `proposedLow`, the current Reading, the 5 % step of calibrated soil, a 0 to 100 track for a percentage),
edits are drafts (`setLow`, `setHigh`, the `…Text` variants, `addHigh`, `clearHigh`, `turnOnAlerts`,
`turnOffAlerts`), `canSave` gates Save on "low below high" and "a high needs a low" (the Server stays the only
validator), `save` sends only the sides that changed (`override` or `cleared`) one Sensor after the other, keeps
every edit on an error (`ThresholdsNotice`: 400, 403, not delivered) and, once saved, makes Lot detail and the
Lots read again (`SitesWiring.thresholds`). Only an Owner or Administrator edits: a Member's `canEdit` is false
and every edit call is ignored. Lot detail reads the soil Sensor's Thresholds for the chart: `HistoryChart.band`
(only soil moisture in `%`, a fixed 0 to 100 axis), `ChartBar.belowLow` (a daily low under the low Threshold),
`HistoryChart.belowLowDays` for the summary; `LotDetail.canSetThresholds` / `canViewThresholds`. For Swift,
`IosThresholds` observes a flat `ThresholdsSnapshot`.

**Alerts (Story 6.2).** `alerts/AlertsEngine` is the Alerts surface of the current Site over
`GET /sites/{siteId}/alerts` (`AlertsApi.listAlerts`, Member). It follows the Sites engine and reads as soon as
a Site is current, so the Alerts tab has its count before the tab is opened; the shells call `load` on every
entry of the surface and `refresh` for pull-to-refresh and on every foreground. A read follows `nextCursor` to
the end, at most `AlertsEngine.MAX_PAGES` (20) pages, and lists each Alert once. Rows stay while a read runs
(`AlertsState.Ready.refreshing`); there is no stale mode and no cache: a read that fails on any page leaves
`AlertsState.Failed` (`AlertsNotice`) without rows, and a 401 leaves `Idle`. `AlertsState.openCount` is the
Server's `openCount`, 0 unless `Ready`. `AlertSummary` keeps the Server's order and derives what a row is, so
neither shell decides it: `group` (`Threshold`, `Health`, `Closed`), `variant` (`NeedsWater` only for an open
low-side soil-moisture Threshold Alert, `Threshold`, `Health`, `Closed`), `condition`, `eyebrow`, `icon` and
`target` (Threshold and uncalibrated rows open Lot detail of `lotId`; silent, battery and unknown rows, and any
row without a Lot, open Devices). A kind this app does not know, and a Threshold Alert without a side or with a
quantity it does not know, is a Health row with `help`. An Alert carries no value and no Threshold (DW-88). For
Swift, `IosAlerts` (`IosSignIn.alerts`) observes a flat `AlertsSnapshot` of parallel `alert…` lists.

**Health Alerts (Story 7.2).** Threshold Alerts come from the contract's `alerts`, Health Alerts from
`healthAlerts` (`HealthAlertDto`), which is not paged: the engine takes it from the first page. A Health Alert is
about a Device, so `AlertSummary.lotId` and `lotName` are `null` for a Hub and for a Node on no Lot, and it
carries `deviceKind` (`Node`, `Hub`, `null` when unknown) and `silentSinceEpochMs`. `AlertsState.Ready.alerts`
is one list: the open Threshold Alerts, the open Health Alerts, then the closed ones; each list keeps the
Server's order, and the two closed runs are only interleaved by their closing time. `AlertSummary.title(now)` is
the row's title as data (`AlertTitle`): `NodeOnLotSilent` ("Node on Lot 'Beans' silent for 6 h"), `HubSilent`
("Hub 3F2A silent for 5 min"), `NodeSilent` for a Node on no Lot ("Node 9C0D silent for 6 h"), the Threshold
kinds, `Battery`, `Uncalibrated`, and the neutral `HealthOnLot` / `Health` for a kind this app does not know or a
Lot-bound kind without a Lot. Its `duration` is `null` for a closed row and for an Alert without a silent-since,
which drops " for …". `AlertsSnapshot` carries it as `alertTitles`, `alertDeviceNames`, `alertDurationValues` and
`alertDurationUnits`, with `IosAlerts.current()` for a minute tick.

**Low battery (Story 7.3).** A `battery` Health Alert carries `batteryPercent` (`HealthAlertDto`,
`AlertSummary`), the level of the device report that opened it; a value outside 0 to 100 is dropped and the
Alert stays. Its title is `NodeOnLotBattery` ("Node battery 14 %, not charging — Lot 'Lettuce'") or, for a Node
on no Lot, `NodeBattery` ("Node 9C0D battery 14 %, not charging"), with the level in `AlertTitle.batteryPercent`.
A closed row, and an Alert without a level, is `Battery` ("Node battery low on Lot 'Lettuce'") or
`NodeBatteryLow` ("Node 9C0D battery low"): the level at opening is no longer true. The kind states the charger
status, because the Server opens such an Alert only from a report that is not charging. `AlertsSnapshot` carries
the level as `alertBatteryPercents`, empty for every other title. The row opens Devices, where `NodeSummary`
shows the newest report's level and charger status.

**My notifications (Story 6.3).** `notifications/NotificationSettingsEngine` is My notifications over
`GET`/`PATCH /me/notification-settings` and `GET`/`PUT /sites/{siteId}/notification-settings`
(`NotificationSettingsApi`). It reads when the session becomes signed in and on every `load` (the shells call it
on every entry of the surface), and follows the current Site; without one only the window and the time zone are
read. Every value is the Server's.

- **Notification Window.** `NotificationSettingsState.Ready.window` is what the Server holds, `draft` what the
  control shows (`"HH:mm"`, 24 h). `setWindowFrom` / `setWindowTo` edit the draft, `saveWindow` sends both times.
  `canSaveWindow` gates Save on "changed" and "starts before it ends" (`windowOutOfOrder`); the Server stays the
  only validator.
- **Time zone.** `NotificationTimeZone.detected` is the proposal: the device's zone, else the zone the Server
  stored as detected, else none (the User picks from the list). `chosen` is the zone the User chose, as the Server
  holds it; the confirm panel shows until there is one. `confirmTimeZone` and `pickTimeZone` send `timeZone`.
- **Hand-over of the device zone (DW-23).** On the first read of a session, while the Server holds no chosen
  zone: a zone kept in `DeviceChoices.timeZone` (`timeZone.chosen`, confirmed on Create Site) is sent as
  `timeZone` and removed once the Server answered, a 400 included; it stays when the Server did not answer (no
  response, a 5xx, a certificate failure) and the next read sends it. Without a kept zone the device's zone goes
  as `detectedTimeZone`, unless the Server already holds that one. A zone chosen on the Server wins: the kept one
  is dropped and nothing is sent. The kept zone is also removed at sign-out and on a 401 (`SitesEngine.forget`).
  Create Site hands a confirmed or picked zone to this engine, which sends it as `timeZone`; `POST /sites` carries
  no zone. The Create Site panel then names the zone the Server holds.
- **Mute and my Reminder cadence.** `SiteNotificationSettings` of the current Site: `muted`, `reminderCadence`
  (`null` is "Use Site setting") and `siteReminderCadence` for the helper. Every `PUT` carries both values,
  because the Server reads a missing cadence as "use Site setting".
- **Changes.** The switch and the segmented choice apply at once and the window has Save. One change is sent at a
  time, and the state already shows it (`working`). A change that was not saved puts its control back at the
  Server's value with a `NotificationSettingsNotice` and its `NotificationControl`; `retry` sends it again when
  the notice has `tryAgain`. A 401 ends the session. A failed load is `Failed` with its notice.

**Site Reminder cadence (Story 6.3).** `lots/LotsEngine` carries it for Site settings over
`GET`/`PUT /sites/{siteId}/reminder-cadence` (`SiteReminderCadenceApi`): `LotsState.Ready.reminderCadence`
(`SiteReminderCadence`: the Server's `value`, `null` until it is read, and a `pending` pick). It is read with
every read of the Lots and never kept on the device. `SiteSettings.canSetReminderCadence` is true for an Owner or
Administrator; `setReminderCadence` shows the pick at once and puts the Server's value back when it was not saved,
with `LotsNoticeKind.ReminderCadenceNotSaved` and `retryReminderCadence`, which sends the same pick again. After a
503 `reminder-cadence-not-delivered` the Site holds the pick, so the control keeps showing it with the same notice,
and Try again repairs a cadence that reached only some members.

**Pause and Resume (Story 8.2).** Whether anything is paused is the Server's answer and nothing else: the core
mirrors `Site.paused` / `pausedUntil` (`SiteSummary`), the Lot's `pausedBy` / `pausedUntil` (`LotSummary`) and
`DeviceListItem.pausedBy` / `pausedUntil` (`HubSummary`, `NodeSummary`). It never computes or keeps "paused".

- **Calls.** `sites/PauseApi`: `pauseSite`, `resumeSite`, `pauseDevice`, `resumeDevice` over `PUT` / `DELETE
  /sites/{siteId}/pause` and `…/devices/{deviceId}/pause`. `until` is the calendar date the User picked
  (`yyyy-MM-dd`, `PauseDate`), sent as it is; without one the body is `{}`.
- **`sites/PauseEngine`**, one per surface: `LotsEngine.pause` (the Site menu), `LotDetailEngine.pause`,
  `DevicesEngine.pause`. `openForSite()` gives a `PauseForm` with Whole Site as its only scope; `openForDevice(id)`
  one with This Device selected and Whole Site. `setScope`, `setUntil`, `cancel`, `save`. `PauseForm.target` is what
  the primary button names (the Device's short name, or the Site). Accepted, the sheet closes; refused, it stays
  with `PauseForm.notice`: `DateRefused` (400, the date field is marked), `Forbidden` (403), `NotFound` (404),
  `Unreachable`, `Certificate`, `Unexpected`. `resumeSite()` and `resumeDevice(id)` are one call without a sheet;
  one that did not happen is `PauseState.failure`, and `retry()` sends it again. One request runs at a time, a
  Member's calls are ignored, and a change of Site or the end of the session drops everything.
- **Afterwards.** The Server's answer to a Site call is shown on the Site at once (`SitesEngine.pauseAnswered`),
  the Sites are read again without leaving the shell (`SitesEngine.refresh`), and the Lots, an open Lot detail
  and a shown Devices list read again (`SitesEngine.onPauseChanged`). `LotsEngine.refresh()` reads the Sites with
  the Lots, so a Pause that ended by itself shows on the next refresh.
- **What a surface offers.** `SiteMenu.items(site, stale)`: `Pause`, or `Resume` while `site.paused`, for an
  Administrator or Owner; nothing for a Member; disabled in stale mode. `DevicePause` (from `pausedBy`):
  `canPause` (no Pause of the Device's own), `canResume` (its own Pause), `resumeSiteHint` (paused by the Site
  alone, so there is no Resume for the Device). `LotDetail.canPause` / `canResume` / `pauseDeviceId` add "live, with a Node";
  `LotDetail.memberPausedNotice` and `LotsOverview.memberPausedNotice` are the Member's one notice.
  `LotsOverview.sitePaused` is the line "No Alerts are sent while the Site is paused."; it follows `Site.paused`,
  while the headline keeps its rule over the Lots. Neither shows in stale mode.

For Swift, `LotsSnapshot`, `LotDetailSnapshot` and `DevicesSnapshot` each carry `pause` (a `PauseSnapshot`: the
sheet, the Resume under way, the last failure), and `IosLots.pause`, `IosLotDetail.pause` and `IosDevices.pause`
(`IosPause`) hold the calls; each surface's `watch` also fires when its Pause state changes. `SitesSnapshot` and
`LotsSnapshot` carry the Site's `paused` members, `LotDetailSnapshot` the gates, and `DevicesSnapshot` the
parallel `hubPaused…` / `nodePaused…` lists.

For Swift, `IosNotificationSettings` (`IosSignIn.notifications`) observes a flat `NotificationSettingsSnapshot`,
and `LotsSnapshot` gains `reminderCadence`, `reminderCadenceWorking`, `canSetReminderCadence` and
`actionNoticeTryAgain` with `IosLots.setReminderCadence` / `retryReminderCadence`.

**Push (Story 6.5).** `push/PushEngine` owns everything about push that is not an OS call, over
`PUT`/`DELETE /me/push-registrations/{installationId}` (`PushApi`). The shells report, the engine decides, and
`PushState` is what they draw.

- **Permission.** The shell calls `reportPermission(OsPermission)` on start and on every foreground. `promptDue` is
  true while a Site is current, the OS says `NotDetermined` and this device never asked: the overview shows the
  why-line, then the OS prompt, and the shell calls `promptAnswered`. "Asked" is kept per device
  (`DeviceChoices.notificationPermissionAsked`, `push.permissionAsked`) and outlives sign-out, so the prompt shows
  once; from then on anything but `Granted` is `PushPermission.Denied` and `noticeVisible`. Android below 13 reports
  `Granted` (nothing to ask) unless its notifications are turned off.
- **Registration.** `tokenReceived(token, environment)` takes the token the OS gave (FCM, or APNs as hex with
  `production` / `sandbox`). The engine sends it once per session, after sign-in, and again when it changes; the
  same token is not sent twice in a session. A registration the Server did not answer is sent again with the next
  `reportPermission`; one it refused (400) only when the token changes. The installation ID is a UUID made at the
  first registration and kept in `DeviceChoices.installationId` (`push.installationId`); it is never cleared.
- **Sign-out.** `SignInEngine.onSigningOut` runs before the tokens are cleared, so the removal is still this User's
  call. `SitesWiring.push` registers `PushEngine.unregister` there: a registration on its way is cancelled, then
  the `DELETE` goes out. It is held to 5 s and its outcome is ignored. A session the Server ended (401) removes
  nothing; the next User's registration of the same token replaces it on the Server.
- **Payload and tap.** `PushPayload.parse` reads the keys of `packages/asyncapi` (`fcm.message.data`, or the APNs
  `coldframe` object); `PushFixturesTest` holds it to the fixtures' `route`. `opened(data)` starts the route: wait
  for the Sites (a cold start; Sites kept on the device count when they hold the Site), `SitesEngine.select`, and
  for `alert` / `reminder` wait for that Site's Lots. Then `PushState.route` is `LotDetail(siteId, lotId,
  lotName)` or `Overview(siteId)`; the shell shows it and calls `routeHandled`. An unknown kind, a Lot that is gone
  and Lots that could not be read end on the Site's overview, an unknown Site on the current Site's. The tap is
  dropped when the session ends or there is no Site.

For Swift, `IosPush` (`IosSignIn.push`) observes a flat `PushSnapshot`; Android reads `AndroidSignIn.push`.

The Server produces the uncalibrated kind from Story 7.4 on, in `healthAlerts`: `AlertCondition.Uncalibrated`,
`AlertTitleKind.Uncalibrated` with the Lot's name, `AlertIcon.Tools` and `AlertTarget.Lot`, open and closed; a
tapped notification of one carries the Lot and routes to Lot detail like a Threshold Alert's.

Absent until later stories:
a silent or low-battery row or a tapped notification of one opens the Devices surface, not the Device's row (the push route
carries no Device ID), and there is no live update: the shells read again on entry, on pull-to-refresh and on
every foreground.
