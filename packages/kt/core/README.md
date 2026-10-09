# packages/kt/core

The shared Kotlin Multiplatform core (Gradle project `:core`). The overview of the whole module is
in [`packages/kt`](../README.md); this file covers Lot status and stale mode (Story 4.7). Tests
live in [`tests/kt/core`](../../../tests/kt/core).

## Lot status

The Server computes a Lot's status once (AD-14). The core mirrors the `Lot` schema of
`packages/openapi/coldframe.openapi.json` in `api/LotDto` (`OpenApiContractTest` holds the two
together) and maps it one to one to `lots/LotSummary`: `status`, `statusSince`, `lastReadingAt`,
`unknownCause` (`node`, `hub`), `pausedBy` (`device`, `site`), `pausedUntil`, `moisturePercent`
and `lowThresholdPercent`. Times become epoch milliseconds; a value this client does not know is
left out, and a status it does not know reads as `unknown`, never as fine.

| The Server decides | The core decides |
| --- | --- |
| The status of every Lot and its precedence | Whether the shown Lots are live or stale (transport only) |
| The order of the Lots; the core never re-sorts | One retry of a failed read |
| `statusSince`, `lastReadingAt`, `unknownCause`, `pausedBy`, `pausedUntil` | The last good Lots per Site, kept on the device |
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
  unknown Lot is measured from the last Reading for a silent Node and from `statusSince` for a
  silent Hub or a Lot without a Reading. A needs-calibration tile never carries a percentage. A
  field the variant does not show is `null`.
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
- **Back home.** `LotsEngine.refresh()` also reads the Sites again while they are `fromCache`.
- **Events.** `LotsEngine.events` sends `EnteredStale(siteId, fetchedAtEpochMs)` once when a read
  and its retry fail, and `LeftStale(siteId)` on the first success after that. Showing stored Lots
  on a cold start, a first load, a refresh that changes nothing and a further failed refresh send
  nothing. Events are not replayed.
- **In stale mode** every tile is `Stale` (no value, "as of"), a *no Node* tile does not start Add
  a Node, and `LotsState.siteMenu()` returns every Site menu item disabled ("Needs your Server").

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
`target` (Threshold and uncalibrated rows open Lot detail of `lotId`; silent, battery and unknown rows open
Devices). A kind this app does not know, and a Threshold Alert without a side or with a quantity it does not
know, is a Health row with `help`. An Alert carries no value and no Threshold (DW-88). For Swift, `IosAlerts`
(`IosSignIn.alerts`) observes a flat `AlertsSnapshot` of parallel `alert…` lists.

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

For Swift, `IosNotificationSettings` (`IosSignIn.notifications`) observes a flat `NotificationSettingsSnapshot`,
and `LotsSnapshot` gains `reminderCadence`, `reminderCadenceWorking`, `canSetReminderCadence` and
`actionNoticeTryAgain` with `IosLots.setReminderCadence` / `retryReminderCadence`.

Absent until later epics: no Hub ID in the "Hub is silent" hero text (the Server names none; Epic 7), no Health
Alert is produced by the Server (Epic 7), and no delivery, Reminder scheduling, push token, permission prompt or
live update (Stories 6.4 to 6.6).
