---
title: 'Story 8.1: Pause and resume on the Server'
type: 'feature'
created: '2026-10-13'
baseline_revision: 25c2c7cc230f758d6dfd62e99f5bcb6a5036358d
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-8-context.md'
  - '{project-root}/apps/cs/README.md'
warnings: ['oversized']
deferred:
  - summary: >-
      A Hub paused by itself does not excuse the Nodes it relays: when a paused Hub is switched off, each Node
      behind it opens its own silent Alert once its Silence Window passes.
    evidence: |-
      Pause is a state of one Device (PRD glossary; FR-18 cascades only from a Site), and the spec's Never list
      follows that. DeviceGrain.RelayHubAllowsAsync holds a Node back only while its Hub has an open silent Alert;
      a paused Hub has none (its Alert closed as paused, and LastRecoveredAt no longer moves for that close).
      PauseGrainTests.APausedHubsClosedAlertIsNoRecoveryForTheNodesItRelays pins it. Whether "maintenance never
      raises false Alerts" should also cover Nodes behind a Hub paused for maintenance is a product decision the
      story text does not make; a yes would be one more condition in RelayHubAllowsAsync (hub.Paused) and a Lot
      status question (unknown with cause hub, or paused).
    location: >-
      apps/cs/server/Devices/DeviceGrain.cs (RelayHubAllowsAsync)
    severity: medium (unverified)
  - summary: >-
      Pause can be read back only through a Lot: the Site's Pause has no GET, the Devices list carries no pause
      fields, and a paused Hub (no Lot) shows in no read model.
    evidence: |-
      Story 8.1 asks only for the LotStatus projection, which shows paused with pausedBy and pausedUntil. The
      four pause endpoints answer with the state in force, and ISiteGrain.GetPause is grain-only.
      DevicesProjector ignores device.paused and device.resumed, and GET /sites/{siteId} has no pause member.
      Story 8.2 needs a Site headline ("Paused until 1 Mar"), "PAUSED BY SITE" Device rows and a paused Hub row,
      so it has to add these to the contract and the read models.
    location: >-
      apps/cs/server/Devices/DevicesReadModel.cs, apps/cs/server/Edge/EdgeApi.cs (GetSiteAsync, ListDevicesAsync)
    severity: medium
  - summary: >-
      The test-only POST /testing/sites/{siteId}/wake has no caller yet.
    evidence: |-
      It was added for Story 8.2's end-to-end run of the winter journey, which must pass a Site end date on the
      manual clock. Nothing in this change requests the route (IdentityCluster.WakeSiteAsync calls the grain),
      so a mistyped route or a wake that does not wait for delivery would go unnoticed until that journey is
      written; the Device and User wakes are exercised by the web.e2e journeys in the same way.
    location: >-
      apps/cs/server/Testing/TestingHostingExtensions.cs
    severity: low
  - summary: >-
      Enrolling an already-enrolled Device again while it is still pending for its Site's Pause is not tested.
    evidence: |-
      DeviceGrain.Enrol applies the join reply's Pause also for a Device that is enrolled already and confirms
      and tells the Sensors when that changed something; every test that enrols into a paused Site uses a new
      Device. The window is narrow (a re-enrolment during a pending delivery) and the Site's own redelivery
      reaches the same state.
    location: >-
      apps/cs/server/Devices/DeviceGrain.cs (Enrol, step 4a)
    severity: low
---

<intent-contract>

## Intent

**Problem:** Pause exists on the Server only as events that tests append behind the grains: `device.paused` / `device.resumed`, the `pausedBy` set, the paused evaluation context and the *paused* Lot status are all built, but nothing can raise a Pause. There is no command on the Device grain, no Site Pause at all, no end date that lifts by itself, no HTTP endpoint, and a Pause leaves the Device's own `silent` and `battery` Alerts open (DW-104).

**Approach:** Add the commands. The Device grain gains pause and resume for its `device` source and a level-triggered "the Site's Pause is now X" call for its `site` source; on every change it closes its own Alerts with reason `paused` and pushes the new evaluation context at once. The Site grain owns the Site Pause and its end date, and delivers it to every Device on its roster from persisted pending state until each acknowledges; the join reply carries it to a Device enrolled meanwhile. End dates are persisted UTC `due-at` deadlines woken by Reminders. Four Administrator endpoints expose it.

## Boundaries & Constraints

**Always:**
- Pause state lives only on the Device grain (`pausedBy ⊆ {device, site}`, each with its own optional end). The Site grain holds the Site Pause and its end date, and which Devices have not yet acknowledged the current one; nothing else stores or re-derives "paused" (AD-8). Read models keep projecting from `device.paused` / `device.resumed`.
- The owner of an end date ends it: the Device grain lifts its own `device` source at its due-at; the Site grain lifts the Site Pause at its due-at and delivers the resume. A Device never lifts its `site` source by itself.
- Every command is idempotent and journals nothing when nothing changes. A redelivered Site Pause must not raise the evaluation epoch or move the silence clock (today `Apply(DevicePaused)` and `Apply(DeviceResumed)` do both unconditionally, so the grain must refuse no-ops before raising).
- Deadlines follow AD-6: a UTC due-at in the owning grain's state, processed on every wake and every activation, Reminder period at least 1 minute, time only from `TimeProvider` (`Clock`).
- An end date is a calendar date. It resolves to 00:00 on that date in the IANA time zone of the User who set the Pause (the User grain's time zone; UTC when the User has none, as `NotificationWindowRule.ZoneOf` already does). A local midnight that does not exist resolves to the first instant of that date that does.
- Only the grain that opened an Alert closes it. On becoming paused the Device grain closes its open `silent` and `battery` Alerts with `AlertCloseReason.Paused` and clears its low-battery streaks; Sensor grains close theirs through the paused context (already built).
- Site-to-Device delivery must not deadlock against `DeviceGrain.Enrol` awaiting `ISiteGrain.RegisterDevice` (see DW-86 for the precedent), and must survive a failed call, a failed journal write and a silo restart.
- Endpoints follow the standing recipe ("Add an endpoint", `apps/cs/README.md`): exactly one access rule each, `x-coldframe-minimum-role: administrator` in the contract, Problem Details errors, a sample in the authorization matrix, regenerated TypeScript client.
- Test-first: write the failing TestCluster tests before the grain code they cover.

**Never:**
- No client work (web, iOS, Android): that is Story 8.2. No pause fields on the Devices list or on `GET /sites/{siteId}`; 8.2 decides what its screens need (see Design Notes).
- No "who paused, and when" attribution stored or exposed beyond the event timestamps.
- A paused Hub does not pause, or suppress Alerts for, the Nodes it relays: Pause is a state of one Device (PRD glossary). Closing a Hub's `silent` Alert as `paused` must not count as a Hub recovery for its Nodes.
- Do not write `sprint-status.yaml`. Do not edit planning artifacts. No wall-clock reads, no new scheduling mechanism besides Orleans Reminders and grain timers.
- Do not make a paused Device answer a frame differently from today (`Stored`, acknowledged, nothing kept).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Pause a Device | `PUT /sites/{s}/devices/{d}/pause` `{}` by an Administrator | 200 `{"pausedBy":["device"],"pausedUntil":null}`; `device.paused` journaled; its Lot reads `status: paused`, `pausedBy: ["device"]` | No error expected |
| Pause until a date | body `{"until":"2026-11-01"}`, caller's zone `Europe/Zurich` | 200 with `pausedUntil` `2026-10-31T23:00:00Z`; at that instant the next wake journals `device.resumed` | No error expected |
| Same Pause again | identical request | 200, same body, nothing journaled, epoch unchanged | No error expected |
| Change the end | Pause again with another `until`, or none | 200 with the new end in force | No error expected |
| Resume a Device | `DELETE /sites/{s}/devices/{d}/pause` | 200 `{"pausedBy":[],"pausedUntil":null}`; Silence Window counts from now | No error expected |
| Resume, Site still paused | `DELETE` on a Device paused by both | 200 `{"pausedBy":["site"],...}`; still paused, silence clock not moved | No error expected |
| Resume, not paused | `DELETE` on an unpaused Device | 200 with empty `pausedBy`; nothing journaled | No error expected |
| Pause a Site | `PUT /sites/{s}/pause` `{"until":"2027-03-01"}` | 200 `{"paused":true,"pausedUntil":"<UTC>"}`; every Device on the roster gains `site` | A Device call that fails stays pending and is retried |
| Resume a Site | `DELETE /sites/{s}/pause`, or the Site's due-at passing | 200 `{"paused":false,"pausedUntil":null}`; every Device loses `site` | Same redelivery |
| Enrol into a paused Site | Device enrolled while the Site is paused | the Device starts with `pausedBy: ["site"]` and the Site's end; no Alert opens with its first frame | No error expected |
| Frame while paused | signed frame from a paused Node | acknowledged as today; no Reading stored, no Alert opened, no liveness counted | No error expected |
| Bad date | `until` not a date, or resolving to an instant not after now | 400 `validation`; nothing changed | Problem Details |
| Bad Device ID / unknown Device / Device of another Site | — | 400 `validation` / 404 `device-not-found` | Problem Details |
| Member, or not on the Site | any of the four endpoints | 403 `forbidden` / 404 `site-not-found`, as the matrix decides | Problem Details |

</intent-contract>

## Code Map

Already built; reuse, do not rebuild:
- `packages/cs/contracts/Devices/DeviceEvents.cs:118,155,168` -- `DevicePauseSource`, `DevicePaused(Source, EndsAt?, PausedAt)`, `DeviceResumed(Source, ResumedAt)`. `:318,395` -- `DeviceSilenceEnded` and `DeviceBatteryLowEnded` already carry a `Reason`.
- `apps/cs/server/Devices/DeviceState.cs:23,202,207` -- `_pausedBy`, `PausedBy`, `IsPaused`. `:317-335` -- `Apply(DevicePaused/DeviceResumed)`: always bump the epoch; resume with an empty set moves `SilenceFrom`. `:423` -- `Apply(DeviceSilenceEnded)` sets `LastRecoveredAt` whatever the reason (must become recovered-only). `:220,539` -- `PendingReleases`, `PendingDeviceAlertDelivery`.
- `apps/cs/server/Devices/DeviceGrain.cs` -- `Enrol:150-223` (ignores `registration.Pause`), `OnActivateAsync:238-252`, `ReceiveReminder:347-371` (`silence-window`, every enrolled Device, 1 min: the wake for the `device` end date; no new Device reminder needed), `Ingest:659-797` (paused branch already discards), `SetSilenceWindow:833` (command shape to imitate: checks the Site, returns an outcome enum), `EvaluateSilenceAsync:890-929`, `RelayHubAllowsAsync:974-984`, `DeliverAlertsAsync:994`, `CountBattery:1028`, `PushContextsAsync:1088-1106`. Stale comments at `:37,47-51`.
- `apps/cs/server/Devices/SilenceRule.cs:34-79` -- silence clock rules.
- `apps/cs/server/Sensors/SensorGrain.cs:272,442-464`, `UncalibratedRule.cs:71`, `SensorState.cs:319-337` -- paused context closes Sensor Alerts and resets streaks. No change expected.
- `packages/cs/contracts/Alerts/AlertGrains.cs:118-143`, `apps/cs/server/Alerts/AlertNames.cs:42-49`, `AlertIds.cs:70,90` -- `Paused` reason and the Device's Alert IDs exist.
- `apps/cs/server/Lots/LotStatusRule.cs:111-150`, `LotsProjector.cs:94-105,358-365,527`, `LotsReadModel.cs:40-41`, `apps/cs/server/Edge/EdgeApi.cs:81-90` -- *paused* status, `pausedBy`, `pausedUntil` end to end. No migration needed.

To change or add:
- `packages/cs/contracts/Devices/DeviceGrains.cs:10-135` -- `IDeviceGrain`; `:222-231` `DeviceSilence.Paused`.
- `apps/cs/server/Identity/SiteGrain.cs:19,219-249` -- event-sourced, not `IRemindable`; `RegisterDevice` always answers `SitePause.NotPaused`. `SiteState.cs:112,207` -- `Devices` roster (Devices never leave it).
- `packages/cs/contracts/Sites/SiteGrains.cs:756-773` -- `DeviceRegistrationResult`, `SitePause(Paused, EndsAt?)`. `SiteEvents.cs` -- no pause events yet.
- `apps/cs/server/Edge/EdgeApi.cs:544-672` (map), `:2064-2115` (Silence Window handler and `ToHttpResult`), `EdgeAccessRule.cs:55-90`, `EdgeProblems.cs:18-118`, `SiteAccessAuthorization.cs`.
- `apps/cs/server/Identity/UserState.cs:121-132`, `IUserGrain.GetNotificationSettings()` -- the caller's time zone. `apps/cs/server/Notifications/NotificationWindowRule.cs:21,92-118` -- `ZoneOf` and gap/overlap handling to imitate; `TimeZoneProposal.cs:17`.
- `packages/openapi/coldframe.openapi.json` -- Silence Window path `:315`, schemas `:1706,1719`, `LotPauseSource` `:1015-1071`. `packages/openapi/README.md:6-37` operations table. `packages/ts/api-client/src/schema.ts` -- generated, committed.
- `apps/cs/server/Testing/TestingHostingExtensions.cs:53-138` -- test-only clock and wake endpoints.

Patterns for persisted pending + redelivery: Device to Lot release (`DeviceGrain.cs:398-442`, 5 s timer plus a 1 min reminder that removes itself, restarted on activation) is the closest; also `AlertGrain.cs:267-311`. Reminder plus due-at: `UserGrain.cs:42,657`, `UserState.cs:195`. Not a model: `SiteReminderCadenceFanOut`.

Tests:
- `tests/cs/server.integration/Identity/IdentityCluster.cs` -- `Time` (`FakeTimeProvider`, `:53`), `RestartSiloAsync(startAt)` `:144`, `ReminderTableReadyAsync` `:160` (DW-110), `WakeDeviceAsync` `:184`, `FailNextJournalWriteAsync` `:203`, fault filters `:487-820`, `SilenceWakes` `:576-594` (drops real-clock ticks of `silence-window` only: a Site reminder needs the same and a `WakeSiteAsync`).
- `tests/cs/server.integration/Devices/` -- `SilentDeviceAlertGrainTests.cs:571,731-870`, `BatteryAlertGrainTests.cs:550`, `UncalibratedAlertGrainTests.cs:399,528`, `ThresholdAlertGrainTests.cs:399,993`, `IngestGrainTests.cs:282`: existing pause tests append events behind the grain ("Pause has no command before Epic 8"). `SilenceWindowEndpointTests.cs:109,125` -- HTTP template. `tests/cs/server.integration/Edge/LotStatusTests.cs:199-263`, `AuthorizationMatrixTests.cs:37-205,354`, `Identity/SiteDeviceRegistrationTests.cs:25`.
- `tests/cs/server.tests/` -- `Edge/EdgeEndpointCatalog.cs`, `Edge/EdgeEndpointDiscoveryTests.cs:48`, `Devices/DeviceStateSilenceTests.cs:147-156`, `Devices/DeviceStateEvaluationEpochTests.cs:26`, `Fixtures/journal.json` (a row per new event type), `WallClockBanTests.cs`.

## Tasks & Acceptance

**Execution:**
- [x] `tests/cs/server.integration/Devices/PauseGrainTests.cs` (new) -- TestCluster tests, written first, for the Device side: pause and resume by command; idempotent repeats; both sources held; `silent` and `battery` Alerts closing as `paused` and streaks cleared; Sensor Alerts closing; a frame while paused; a `device` end date lifting on the wake and across `RestartSiloAsync(startAt)`; no `silent` Alert right after a resume that follows a long quiet Pause; a paused Hub's close not restarting its Nodes' clocks -- the story's test AC.
- [x] `tests/cs/server.integration/Identity/SitePauseGrainTests.cs` (new) -- Site pause reaching every Device; redelivery after a failed Device call and after a failed journal write; Site resume; Site end date across a restart; a Device enrolled into a paused Site starting paused; pause, resume and pause again delivering only the latest state.
- [x] `packages/cs/contracts/Devices/DeviceGrains.cs`, `packages/cs/contracts/Sites/SiteGrains.cs`, `packages/cs/contracts/Sites/SiteEvents.cs` -- add the Device commands (own source; Site's Pause), the Site commands and read, their result types, and the Site Pause events -- the grain surface.
- [x] `apps/cs/server/Devices/DeviceState.cs`, `apps/cs/server/Devices/DeviceGrain.cs` -- implement the commands; refuse no-ops; on becoming paused close own Alerts as `paused` and clear battery streaks; push contexts at once; process the `device` due-at on wake and activation; read the join reply in `Enrol`; `LastRecoveredAt` only for a recovered close -- AD-8, DW-104.
- [x] `apps/cs/server/Identity/SiteState.cs`, `apps/cs/server/Identity/SiteGrain.cs` -- Site Pause state, due-at, pending-acknowledgement set, reminder and timer delivery, activation catch-up, `RegisterDevice` answering the real Pause -- AD-8, AD-6.
- [x] `apps/cs/server/Devices/PauseEndRule.cs` (new), `tests/cs/server.tests/Devices/PauseEndRuleTests.cs` (new) -- pure rule: date plus zone to UTC due-at, with the gap, UTC-fallback and not-after-now cases -- the matrix's date rows.
- [x] `apps/cs/server/Edge/EdgeApi.cs` -- `PUT` and `DELETE` on `/sites/{siteId}/pause` and `/sites/{siteId}/devices/{deviceId}/pause`, Administrator minimum role -- the HTTP surface.
- [x] `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md`, `packages/ts/api-client/src/schema.ts` -- contract, operations table, regenerated client.
- [x] `tests/cs/server.integration/Devices/PauseEndpointTests.cs` (new), `tests/cs/server.integration/Edge/AuthorizationMatrixTests.cs`, `tests/cs/server.tests/Fixtures/journal.json`, `tests/cs/server.integration/Identity/IdentityCluster.cs`, `tests/cs/server.integration/Identity/SiteDeviceRegistrationTests.cs` -- the matrix rows over HTTP (incl. Lot status through `GET` lots), matrix samples, journal fixture rows, the Site wake helper and reminder filter.
- [x] `apps/cs/server/Testing/TestingHostingExtensions.cs` -- test-only Site wake, beside the Device and User ones -- Story 8.2's end-to-end run needs it to pass a Site end date.
- [x] `apps/cs/README.md`, `packages/cs/README.md`, stale comments in `SiteGrain.cs`, `DeviceGrain.cs`, `LotsProjector.cs` -- replace every "until Epic 8" statement with what now holds.
- [x] `_bmad-output/implementation-artifacts/deferred-work.md` -- mark DW-104 resolved if the work closes it; leave other entries.

**Acceptance Criteria:**
- Given a Device paused by itself and by its Site, when either source alone is lifted, then `GET` of its Lot still reads `paused` with the remaining source, and only lifting the second returns its live status.
- Given a Node with open `silent`, `battery`, Threshold and `uncalibrated` Alerts, when it becomes paused, then `GET /sites/{s}/alerts` shows each closed with reason `paused`, and no Alert opens for it while paused however long it stays quiet.
- Given a Site paused with an end date and a silo that restarts after that date has passed, when the Site wakes, then every Device has lost `site` and no `silent` Alert opens until a full Silence Window after the resume.
- Given a Device call that fails during Site Pause delivery, when the Site next wakes, then that Device is paused too, and Devices that had acknowledged journal nothing more.
- Given a Member, when they call any of the four endpoints, then the answer is 403 `forbidden`, and `EdgeEndpointDiscoveryTests` and `AuthorizationMatrixTests` pass with the four operations listed.

## Spec Change Log

## Review Triage Log

### 2026-10-13 — Review pass
- verdicts: 50 findings — high 0, medium 10, low 26, false 11, maybe-false 3
- findings:
  - `[medium]` `[patch]` Blind: `Ingest` and `Heartbeat` do not lift an overdue own Pause, so a frame between the due-at and the next wake is discarded — real: `Ingest` read `State.IsPaused` without `EndOwnPauseWhenDueAsync`. Patched: both lift it first and tell the Sensors and the lots projection; new test `AFrameOrAHeartbeatThatArrivesAfterTheEndAndBeforeAnyWakeLiftsThePauseFirst`.
  - `[medium]` `[patch]` Blind: a Site Pause with an end date can lose its only wake when the reminder registration failed and nothing is pending — real: `PauseDeliveryTickAsync` disposed the timer whatever the reminder's state. Patched: the tick retries the registration and the timer lives until the reminder holds or nothing needs waking.
  - `[low]` `[reject]` Blind: one slow Device holds up the delivery round (`Task.WhenAll`, single flight) — real but bounded by the grain call timeout, on a roster of a home garden's handful of Devices; batching or per-Device journaling is more machinery than the harm warrants.
  - `[low]` `[reject]` Blind: the stale-revision guard is partly in memory, so an older delivery after an `Unchanged` answer and a reactivation would be applied — needs a superseded call to overtake a newer one across a deactivation, while the Site delivers one attempt at a time; closing it means journaling every acknowledgement on the Device.
  - `[low]` `[reject]` Blind: every Pause change runs a `LotsProjector` catch-up in the Device's turn, also for Hubs and during a Site fan-out — the same read-your-writes catch-up already runs in `LotGrain`, `SensorGrain` and `DeviceGrain` (Devices list); a Hub's is wasted work only.
  - `[low]` `[patch]` Blind: `Silence()` now journals a resume and its doc does not say so — patched: the doc of `IDeviceGrain.Silence` says it lifts an overdue own Pause.
  - `[low]` `[reject]` Blind: `ISiteGrain.DeliverPause` is a test seam on the grain contract and `SetSitePause` does not check its caller — no HTTP path reaches either, grain interfaces are internal to the Server, and a caller check is a new guard for a caller that does not exist.
  - `[low]` `[reject]` Blind: spec and implementation disagree (`pausedUntil` null against omitted, empty logs, unticked boxes) — the fix is an edit of this build's spec; the omission follows the contract's rule for every optional member and is recorded under Design Notes.
  - `[low]` `[patch]` Blind: `until: null` is accepted but pinned nowhere — patched: `PauseResponseTests` pins `TryReadPauseDate` for absent, `null`, dates and non-dates. The required `{}` body matches `setDeviceSilenceWindow`.
  - `[low]` `[patch]` Blind: the journal fixture has no row with the new members of existing events — patched: a stream with `siteRevision` on `device.paused` / `device.resumed` and both `…-ended` events with reason `Paused`, asserted by `FixtureJournalReplayTests`.
  - `[low]` `[reject]` Blind: several behaviours untested (heartbeat of a paused Hub, changed Site end reaching paused Devices, Site Pause closing `silent`/`battery`, enrolment during a delivery, registration failure, frame after the due-at) — the frame case is patched above, the rest share code paths the suites exercise (`RaisePaused`, level-triggered `SetSitePause`); the enrolment case is deferred below.
  - `[false]` `[reject]` Blind: an overdue Pause is reported as in force until the wake, and `PUT {}` then turns it open-ended — the same end again is refused as `InvalidEnd` once it has passed, and a `PUT {}` is an explicit request for an open-ended Pause, which is what results; the commands now also meet a lifted Pause wherever a frame or heartbeat came first.
  - `[medium]` `[defer]` Blind: known gaps live only in prose (Hub Pause in no read model, no `GET` for the Site's Pause, Devices list) — real, and Story 8.2's to close; now in `deferred`.
  - `[false]` `[reject]` Blind: `Devices` depends on `Lots` and `Identity` on `Devices` for small helpers — no caller diverges: `LotStatusRule.PausedUntil` stays the one definition of "latest end, or none", which is the point of reusing it.
  - `[medium]` `[patch]` Edge: frame after the Device's own due-at and before the wake — same defect as the first row; patched there.
  - `[medium]` `[patch]` Edge: timer stops with no reminder — same defect as the second row; patched there.
  - `[medium]` `[patch]` Edge: a faulted registration is never retried — same defect; the tick now retries while a wake is needed.
  - `[low]` `[patch]` Edge: `SetPause` validates the end before a wait of up to 5 s — patched: the clock is read and the end checked again after the wait.
  - `[maybe-false]` `[defer]` Edge: a paused, silent Hub lets its Nodes open `silent` Alerts — the behaviour is as described and deliberate (Pause is a state of one Device); whether it is wrong is a product decision. Deferred as medium (unverified).
  - `[false]` `[reject]` Edge: a failed `ConfirmEvents` leaves `device.resumed` tentative and the next wake journals a second — the log-consistency adaptor retries a failed write until it lands (`AJournalWriteThatFailsOnTheSiteOrOnADeviceStillPausesEveryDeviceExactlyOnce` shows one event after a lost write); the confirm does not return with the event unwritten.
  - `[false]` `[reject]` Edge: a failed confirm in `SetSitePause` makes a redelivery journal twice — same refutation, and that test covers exactly this call.
  - `[low]` `[reject]` Edge: `_sitePauseRevisionSeen` is set before the confirm — it only ever makes an equal or older revision a no-op, which the level-triggered call is anyway.
  - `[low]` `[reject]` Edge: a bodiless `PUT` is 400 — the same rule as `setDeviceSilenceWindow`, and the contract marks the body required.
  - `[false]` `[reject]` Edge: a User grain that throws gives an unhandled 500 — every handler that calls a grain answers a failed call that way, through the host's Problem Details handler; nothing is changed by then.
  - `[low]` `[reject]` Edge: a date centuries ahead is accepted — it is a Pause with an end the caller chose; a cap would be a new rule.
  - `[low]` `[reject]` Edge: a Site that leaves `Active` keeps delivering and cannot be resumed — delivery ends with the last acknowledgement and an end date still lifts; clearing Pause on deletion is a new behaviour for a state the Site's deletion does not define for its Devices either.
  - `[medium]` `[patch]` Gap: the lift on activation is not pinned (the restart test called `Silence()` first) — patched: the first call after the restart is a frame, stored, with exactly one `device.resumed`.
  - `[medium]` `[patch]` Gap: Site catch-up after a restart and the reminder kept for a pending delivery are not pinned — patched: `APendingDeliveryKeepsTheReminderAndIsFinishedByTheSitesActivationAfterARestart`.
  - `[low]` `[patch]` Gap: the stale-revision guard is tested only where both halves agree — patched: `ARevisionOlderThanOneTheDeviceWasToldOrJournaledIsStale`.
  - `[low]` `[patch]` Gap: three outcome-to-HTTP mappings have no test — patched: `PauseResponseTests`.
  - `[low]` `[defer]` Gap: `POST /testing/sites/{siteId}/wake` has no caller — as filed; Story 8.2's journey is its first caller. In `deferred`.
  - `[medium]` `[patch]` Gap: read-your-writes of the Lot status is never observed — patched: the first Device test reads the `lots` row straight after `SetPause` and `LiftPause`.
  - `[low]` `[defer]` Gap: re-enrolment applying the join reply is not exercised — as filed; in `deferred`.
  - `[low]` `[reject]` Intent: the AC5 chain (date over HTTP, midnight in the zone, automatic lift) is never run whole — each link is pinned (rule, endpoint, grain) and the whole chain needs the manual-clock host of the browser journeys, which Story 8.2's AC requires.
  - `[low]` `[reject]` Intent: "resumes automatically" is shown through hand-fired wakes — the convention for every deadline in this cluster, whose reminder ticks are dropped by design; registration is asserted.
  - `[false]` `[reject]` Intent: "both sources held" bypasses the Site grain — `PauseEndpointTests.ASitePauseReachesEveryDeviceAndADevicePausedByBothResumesOnlyWhenBothAreLifted` drives it through the Site grain over HTTP.
  - `[maybe-false]` `[defer]` Intent: Hub semantics, R4a against R4b — same question as the Edge row; deferred with it.
  - `[medium]` `[defer]` Intent: no read surface for a Site's or a Hub's Pause — same as the Blind row; deferred with it.
  - `[false]` `[reject]` Intent: the zone is the caller's notification-settings zone, not where they are — the story names "the time zone of the User who set the Pause" and the User grain holds exactly one (chosen, else detected), which every other deadline uses.
  - `[false]` `[reject]` Intent: Sensor streak reset is not asserted by the new tests — `SensorStateContextTests` and `ThresholdAlertGrainTests` assert it for the paused context, which is the path a command now takes.
  - `[low]` `[reject]` Intent: the 5 s timer retry itself is unobserved — the restart test added above shows delivery without a hand-fired wake; waiting on a real 5 s tick would add a slow test for the same code.
  - `[low]` `[reject]` Intent: additions beyond the story (test wake, `DeliverPause`, DW-104, unenforced `SetSitePause`) — the wake and `DeliverPause` are the seams the story's own tests need, DW-104 is the story's AC; the rest is the Blind row above.
  - `[low]` `[reject]` Intent: task boxes unticked — bookkeeping of this spec.
  - `[false]` `[reject]` Intent: AC1 to AC4 and AC6 to AC8 implemented as read — descriptive rows with no divergence reported.
  - `[false]` `[reject]` Intent: process clauses respected (`sprint-status.yaml` untouched, no operator actions) — no divergence reported.
  - `[false]` `[reject]` Intent: R1 with R4a and R5a is the reading implemented — descriptive; the two open readings have their own rows.
  - `[low]` `[reject]` Intent: an unknown zone silently becomes UTC — the documented fallback of `NotificationWindowRule.ZoneOf`, stated in the contract.
  - `[maybe-false]` `[reject]` Intent: a journaled Site Pause can exist without its reminder (best-effort registration) — the path the second row patches; whether a registration can still fail for good afterwards cannot be shown without a failing reminder service. If true it would be low: the next call or activation registers it.
  - `[low]` `[reject]` Intent: the Site endpoint answers before its Devices are told — as the spec and the contract say; the Lot may read its old status for a moment.
  - `[low]` `[reject]` Intent: `site` source in `PauseGrainTests` uses hand-made revisions — the Device's side of a contract the Site suite drives for real.

### 2026-10-13 — Review pass (follow-up)
- verdicts: 29 findings — high 0, medium 1, low 20, false 6, maybe-false 2
- findings:
  - `[low]` `[reject]` Blind: the `site-pause` reminder ticks every minute for the whole of a Pause with an end, and each tick starts a timer that finds nothing — real and cheap: AD-6 asks for a due-at looked at on every wake with a period of at least a minute, and every enrolled Device already runs a one-minute `silence-window` reminder; a reminder due at `EndsAt` is a second scheduling shape for one tick per paused Site.
  - `[low]` `[reject]` Blind: a Device that never answers is retried every 5 s with a warning each time, with no backoff and no operator signal — real; the Device-to-Lot release this imitates retries the same way, and a Device grain that fails every call is a Server fault its own logs show. Backoff state is new machinery.
  - `[low]` `[reject]` Blind: every Pause change awaits a lots catch-up in the Device's turn, also on the Site fan-out — carried: the same read-your-writes catch-up already runs in `LotGrain`, `SensorGrain` and `DeviceGrain`; wasted work for a Hub only.
  - `[low]` `[reject]` Blind: one `site.pause-delivered` per Device and revision grows the Site stream — real, a handful of events per Pause on a home garden's roster; a batched event is a second event shape for the same fact.
  - `[false]` `[reject]` Blind: only the UTC instant is journaled; date, zone and acting User are lost — the intent forbids attribution, fixes the end as the instant of 00:00 in the setting User's zone, and the matrix answers with that instant; the same date from another zone is a different end and is journaled as one.
  - `[low]` `[reject]` Blind: `until: null` is accepted and the contract's `until` is not nullable — real leniency: a client generated from the contract never sends it, and the first pass pinned the tolerant read on purpose (`PauseResponseTests`); no caller is harmed.
  - `[low]` `[reject]` Blind: `until` has no upper bound — carried: an end the caller chose; a cap would be a new rule.
  - `[low]` `[reject]` Blind: the late-revision guard is half in memory — carried: needs a superseded call to overtake a newer one across a deactivation; closing it means journaling every acknowledgement.
  - `[false]` `[reject]` Blind: a failed `ConfirmEvents` in `EndOwnPauseWhenDueAsync` could journal a second `device.resumed` — carried: the log-consistency adaptor retries a failed write until it lands; the confirm does not return with the event unwritten.
  - `[low]` `[reject]` Blind: `SetSitePause` does not check its caller — carried: no HTTP path reaches it and grain interfaces are internal to the Server.
  - `[false]` `[reject]` Blind: the overdue-Pause lift is copied into five call sites with three different follow-ups — the follow-ups differ because the call sites do: the reminder wake pushes contexts for every tick anyway, and an activation sends them from its own timer (`PushContextsOnceAsync`) because a Sensor may be waiting on that activation; no site misses a step.
  - `[low]` `[reject]` Blind: missing tests (a command during a delivery in the grain, a failing reminder registration, a real timer or reminder tick, `NotEnrolled` then the join reply, 404 on the Site endpoints, a DST day over HTTP) — the 404 is the access rule's, sampled by `AuthorizationMatrixTests`; DST is `PauseEndRuleTests`'; the rest need a failing reminder service or a real-clock wait, as the first pass recorded.
  - `[maybe-false]` `[defer]` Blind: pausing a silent Hub releases its Nodes into their own `silent` Alerts — carried: the product question already deferred (DW-111); not deferred again.
  - `[low]` `[reject]` Edge: a newer revision answered `Unchanged`, a deactivation, then an older one arriving — carried: the same in-memory guard as the Blind row.
  - `[low]` `[reject]` Edge: the lots catch-up has no time limit where activation, ingest, heartbeat or `SetSitePause` await it — carried with the Blind catch-up row: the same unbounded catch-up the other grains await; a failure is caught and logged.
  - `[low]` `[reject]` Edge: the delivery timer retries without backoff — same as the Blind row above; rejected with it.
  - `[maybe-false]` `[defer]` Edge: a paused silent Hub lets its Nodes open `silent` Alerts — carried: DW-111; not deferred again.
  - `[low]` `[defer]` Gap: re-enrolment into a Site whose Pause changed is not exercised — carried: already deferred (DW-114); not deferred again.
  - `[medium]` `[patch]` Gap: the activation-time lift is masked, because the first call after the restart was a frame and `Ingest` now lifts by itself — real, a result of the first pass's own two patches. Patched: `APauseWhoseEndPassedWhileTheServerWasDownLiftsWhenTheDeviceIsActivated` now makes a call that lifts nothing (`SetSitePause` with `NotPaused`) the first one and asserts the single `device.resumed` before the frame. With the lift removed from `OnActivateAsync` the test fails.
  - `[low]` `[patch]` Gap: the lift in `IDeviceGrain.Silence()` has no test — real. Patched: new `AReadOfTheSilenceAfterTheEndAndBeforeAnyWakeLiftsThePauseFirst`; with the lift removed from `Silence()` it fails.
  - `[low]` `[defer]` Gap: `POST /testing/sites/{siteId}/wake` is neither tested nor consumed — carried: already deferred (DW-113); not deferred again.
  - `[low]` `[reject]` Intent: `pausedUntil` is omitted where the matrix writes `null` — carried: the contract's rule for every optional member, under Design Notes; the fix would edit this spec.
  - `[false]` `[reject]` Intent: with two sources the later end is reported — the intent leaves it open and `LotStatusRule.PausedUntil` predates this story; no divergence.
  - `[low]` `[reject]` Intent: the Site endpoint answers before its Devices are told — carried: as the spec and the contract say.
  - `[low]` `[reject]` Intent: the end-date chain is proven in pieces — carried: each link is pinned; the whole chain needs Story 8.2's manual-clock journey.
  - `[false]` `[reject]` Intent: lifting on a frame, a heartbeat and a silence read goes beyond "every wake and every activation" — a Device past its due-at is not meant to be paused any more; the answer to a frame is `Stored` as before, and a Device that still holds a source with no passed end keeps nothing (`AFrameOfAPausedNodeIsAcknowledgedAndNothingIsKeptOrCounted`).
  - `[low]` `[reject]` Intent: several matrix rows are checked below HTTP (epoch, silence clock, a frame while paused, Alerts closing as `paused` through `AlertsReadModel`) — real; the read model row is what `GET /sites/{s}/alerts` serialises and that mapping has its own endpoint tests, and epoch and silence clock have no HTTP surface. An HTTP journey with open Alerts of all four kinds is a large fixture for the same code.
  - `[low]` `[reject]` Intent: no test runs an enrolment concurrently with a delivery — real; the constraint is met by structure (interleaving timer, `TentativeState`), and a deterministic test needs a hook that holds an enrolment open mid-call.
  - `[false]` `[reject]` Intent: test-first order and `sprint-status.yaml` cannot be judged from the diff — no divergence reported; the working-tree change of `sprint-status.yaml` is the orchestrator's.

## Design Notes

- **Level-triggered delivery.** The Site tells a Device its current Pause (`SitePause`), not "pause" or "resume" deltas, with a revision so a stale delivery is recognised. The Device makes its `site` source match and answers; any answer from a Device that is not enrolled in this Site counts as acknowledged, because its next enrolment reads the join reply.
- **Enrolment race.** `RegisterDevice` puts a Device on the roster before the Device journals `DeviceEnrolled`. The join reply and the pending delivery may both reach it; idempotency makes that harmless.
- **Site endpoints answer when the Site has journaled**, not when every Device has acknowledged; delivery starts at once and continues from pending state.
- **Left for Story 8.2 to decide:** pause fields on the Devices list and on `GET /sites/{siteId}` (a Hub has no Lot, so its Pause shows in no read model today); which "until" a tile shows when both sources have one (the Server already reports the later, or none if either is open-ended, `LotStatusRule.PausedUntil`).
- **Absent, not `null`.** The matrix writes `"pausedUntil":null` for "no end"; the API omits the member, as the contract does for every optional member (`Lot.pausedUntil`), and timestamps carry milliseconds.
- **Related open entries, not in scope:** DW-108 (a Sensor dropped by a new Specification set hears of no Pause), DW-110 (reminder table stall after a restart; the fixture already waits).

## Verification

**Commands** (repo root):
- `dotnet build --no-restore -warnaserror` -- expected: no warnings, no errors
- `dotnet format --verify-no-changes --no-restore` -- expected: no changes
- `dotnet test --no-build --project tests/cs/server.tests` -- expected: all pass
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build --project tests/cs/server.integration` -- expected: all pass (the three `NodeMoveTests` reminder timeouts are a known flake; rerun once before treating them as failures)
- `pnpm --filter @coldframe/api-client generate && git diff --exit-code packages/ts/api-client/src/schema.ts` after committing the regenerated file -- expected: no diff
- `pnpm -r typecheck && pnpm -r lint && pnpm -r test && pnpm --filter @coldframe/openapi run check` -- expected: all pass

## Auto Run Result

Status: done

**Summary.** Pause now has commands. The Device grain pauses and resumes its own source (`SetPause`, `LiftPause`) and is told its Site's Pause (`SetSitePause`, level-triggered, with a revision); a command that changes nothing journals nothing. Becoming paused closes the Device's own `silent` and `battery` Alerts as `paused` in the same write and clears its battery streaks (DW-104), and the Sensors get the paused context before the command answers. The Site grain owns the Site Pause, its end date and the set of Devices that have not acknowledged it, and delivers from that persisted state on an interleaving timer backed by a `site-pause` reminder; the join reply carries the Pause to a Device enrolled meanwhile. End dates are UTC due-ats, resolved by `PauseEndRule` as 00:00 on the date in the caller's time zone and lifted by the owning grain on a wake, an activation, and (for a Device) its next frame or heartbeat. Four Administrator endpoints expose it: `PUT` and `DELETE` on `/sites/{siteId}/pause` and `/sites/{siteId}/devices/{deviceId}/pause`.

**Files changed.**
- `packages/cs/contracts/Devices/DeviceGrains.cs`, `DeviceEvents.cs` -- Device pause commands and results; optional `siteRevision` on `device.paused` / `device.resumed`.
- `packages/cs/contracts/Sites/SiteGrains.cs`, `SiteEvents.cs` -- Site pause commands, `SitePause.Revision`, `site.paused`, `site.resumed`, `site.pause-delivered`.
- `apps/cs/server/Devices/DeviceGrain.cs`, `DeviceState.cs` -- the commands, closing own Alerts on pause, own due-at, join reply, recovered-only `LastRecoveredAt`.
- `apps/cs/server/Devices/PauseEndRule.cs` -- end date and zone to UTC due-at.
- `apps/cs/server/Identity/SiteGrain.cs`, `SiteState.cs` -- Site Pause state, pending deliveries, timer and reminder.
- `apps/cs/server/Edge/EdgeApi.cs` -- the four endpoints and their response mapping.
- `apps/cs/server/Testing/TestingHostingExtensions.cs` -- test-only `POST /testing/sites/{siteId}/wake`.
- `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md`, `packages/ts/api-client/src/schema.ts` -- contract, operations table, regenerated client.
- `apps/cs/README.md`, `packages/cs/README.md` -- a Pause section; every "until Epic 8" statement replaced.
- `tests/cs/server.integration/Devices/PauseGrainTests.cs` (two tests added or tightened by the follow-up review), `PauseEndpointTests.cs`, `Identity/SitePauseGrainTests.cs` -- new suites; `IdentityCluster.cs` (Site wake, reminder filter), `AuthorizationMatrixTests.cs`, `SiteDeviceRegistrationTests.cs`, `LotStatusTests.cs` and three Alert suites adjusted.
- `tests/cs/server.tests/` -- `PauseEndRuleTests`, `PauseResponseTests`, `SiteStatePauseTests`, state tests, journal fixture rows and replay expectations.
- `_bmad-output/implementation-artifacts/deferred-work.md` -- DW-104 resolved; `epic-8-context.md` -- compiled epic context.

**Review.** Two passes, four layers each.
- First pass: 50 findings; 16 rows patched in 10 fixes (medium 5, low 5), 6 rows deferred as 4 items, 28 rejected. Its rows and reasons are in the Review Triage Log.
- Follow-up pass: 29 findings; 2 patched (medium 1, low 1, high 0), 0 newly deferred, 27 not acted on. Both patches are tests in `PauseGrainTests.cs`; no production code changed.
  - Patched: the activation-time lift of an overdue own Pause is now pinned by itself (the first call after the restart lifts nothing), and the lift in `IDeviceGrain.Silence()` has a test. Each test fails when its lift is removed.
  - Carried from the first pass, same verdict and route (14 rows): the lots catch-up in the Device's turn (2), no upper bound on `until`, the in-memory half of the revision guard (2), the second `device.resumed` after a failed confirm (false), no caller check on `SetSitePause`, `pausedUntil` omitted, the Site endpoint answering before delivery, the end-date chain proven in pieces; and four rows already deferred as DW-111 (2), DW-113 and DW-114, not deferred again.
  - Rejected as false (5): date, zone and User not journaled (the intent forbids attribution and fixes the end as an instant); the lift copied into five call sites (the follow-ups differ because the call sites do); the later end reported for two sources (predates the story); lifting on a frame, heartbeat or silence read (a Device past its due-at is not meant to be paused; the frame's answer is unchanged); test-first order and `sprint-status.yaml` (nothing to judge).
  - Rejected as low (8): a one-minute `site-pause` reminder for the length of a Pause; no backoff on delivery retries (2); one `site.pause-delivered` per Device; `until: null` tolerated beyond the contract; six further untested paths that need a failing reminder service or a real-clock wait; matrix rows checked below HTTP; no concurrent enrolment-and-delivery test. Reasons are in the log.

**Follow-up review: not recommended.** This pass patched no `high` entry and changed tests only; the two paths the first pass left unread (`PauseDeliveryTickAsync` retrying the reminder registration, `Ingest` and `Heartbeat` lifting an overdue Pause) were read by all four layers and drew no finding that verified as a defect.

**Verification** (run after the follow-up patches, repo root):
- `dotnet build --no-restore -warnaserror` -- 0 warnings, 0 errors.
- `dotnet format --verify-no-changes --no-restore` -- no changes.
- `dotnet test --no-build --project tests/cs/server.tests` -- 1048 of 1048 passed.
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build --project tests/cs/server.integration` -- 594 of 597 passed (19 min 22 s). The three failures (`SiteCreationTests` x2, `SiteLotsAndRenameTests` x1) were `Npgsql.PostgresException 53300: too many clients already`, while the `pnpm` checks ran beside the suite; both classes passed 22 of 22 when run again alone. `PauseGrainTests` passed 21 of 21. The full suite was not run a second time.
- `pnpm --filter @coldframe/api-client generate` with `git diff --exit-code` on `schema.ts` -- no diff; `pnpm -r typecheck`, `pnpm -r lint`, `pnpm -r test`, `pnpm --filter @coldframe/openapi run check` -- all exit 0.
- Mutation check of the two patches: with the lift removed from `OnActivateAsync` and from `Silence()`, exactly those two tests fail; the production file was restored and rebuilt before the runs above.

**Residual risks.**
- The stale-revision guard on the Device is partly in memory: an older Site Pause delivery that overtakes a newer one across a deactivation would be applied. It needs message reordering to occur.
- A Site that is deleted while paused keeps delivering until its Devices acknowledge, and cannot be resumed by command.
- A Device grain that fails every call is retried every 5 s for as long as the Site grain is active, with a warning each time and no backoff.
- Automatic resume is proven through hand-fired wakes, as every deadline in the TestCluster is; a real reminder tick is not exercised.
- `Apply(DevicePaused)` now clears battery streaks, which changes how a stream with earlier pause events replays; only test-appended streams hold any.
- The integration suite can exhaust the test Postgres's connections when other work runs beside it (seen once, in this run).
