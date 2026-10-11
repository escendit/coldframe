---
title: 'Story 7.1: Silent Device Alert on the Server'
type: 'feature'
created: '2026-10-11'
baseline_revision: c6dea57d116fb07479dcd93c9488ea6aadd47fa8
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-7-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-6-1-threshold-alerts-open-and-close.md'
  - '{project-root}/apps/cs/README.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** A Node or Hub that stops reporting raises nothing today: only Threshold Alerts exist, the Lot keeps its last status, and a dead Device looks like a healthy Lot.

**Approach:** The Device grain measures silence against a persisted deadline and opens a Health Alert of kind `silent` through the existing Alert grain, closing it when the Device reports again. A Node behind a silent Hub opens nothing. The lots projector turns an open `silent` Alert into Lot status `unknown` with `unknownCause` `node` or `hub`. Delivery reuses the Site and User fan-out, where Health Alerts already remind at most once per day.

## Boundaries & Constraints

**Always:**
- Silence Window defaults: Node 6 h, Hub 5 min; a per-Device override replaces the default.
- A Node's liveness is the latest `measured_at` the Server accepted from it, never the arrival time. A Hub's liveness is its last accepted heartbeat.
- The silence clock starts at the latest of: enrolment, last accepted report, last Server start, last resume, and (for a Node) the recovery of its last relay Hub. The expiry (clock start + window) is state derived from the Device's journal; a reminder (period 1 min) only wakes the grain, and every wake, activation and accepted report processes what is overdue. Time comes only from `TimeProvider`.
- Alert ID = UUIDv5 of `device:{deviceId}:silent:{episode}` in the existing Alert namespace; the episode is journaled before `Alert.Open` is called. At most one open `silent` Alert per Device. Only the Device grain that opened it closes it, with reason `recovered`.
- An open or close the Alert grain did not acknowledge is delivered again from journaled state, open before close.
- A paused Device opens no `silent` Alert.
- Existing journal rows stay readable without an upcaster: new record members are nullable or new.

**Never:**
- No change to the OpenAPI contract, the alerts list response, the Devices list or any app: `listAlerts` and its `openCount` keep returning Threshold Alerts only (Story 7.2 extends the contract together with the clients).
- No HTTP endpoint for the Silence Window (Story 7.2); only the grain command.
- No final push copy for silence (Story 7.2): a `silent` notification must be delivered without failing, with neutral text.
- No Pause commands and no close with reason `paused` (Epic 8); no battery or uncalibrated Alerts (Stories 7.3, 7.4).
- The Hub relation never decides whether a frame is accepted.
- No wall clock, no in-memory-only deadline, no per-Node Alert while the relay Hub is silent.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Node goes quiet | Last accepted `measured_at` T, wake at T + 6 h | One `silent` Alert opens for the Node (episode 1), Site and members are told | Undelivered open is retried each wake |
| Hub goes quiet | Last heartbeat T, wake at T + 5 min | One `silent` Alert opens for the Hub, without a Lot | Same |
| Before the window | Wake at T + window − 1 s | Nothing is journaled | — |
| Per-Device override | Node window set to 8 h | No Alert at 6 h, Alert at 8 h; clearing the override returns to 6 h | Window outside 1 min … 30 days is refused, nothing journaled |
| Recovery | Silent Device reports with a time inside its window | Alert closes with reason `recovered`; the next silence is episode 2 with another Alert ID | Close retried each wake |
| Old backlog only | Silent Node delivers a frame whose `measured_at` is older than its window | Alert stays open | — |
| Server restart | Node last reported T, silo down from T + 5 h, back at T + 7 h | No Alert at restart; Alert only at T + 13 h (6 h after the start) | — |
| Hub silence suppression | 5 Nodes whose last relay Hub has an open `silent` Alert; their windows expire | Exactly one Alert (the Hub's); no Node Alert | Hub unreachable: nothing opens, retried next wake |
| Suppression lifts | That Hub heartbeats again at R | Hub Alert closes; each Node's window restarts at R; a Node still quiet opens its Alert at R + 6 h | — |
| Relay Hub unknown | Node's relay Hub is not an enrolled Device | The Node's Alert opens normally | — |
| Paused Device | Window expires while paused; later resumed at P | No Alert while paused; window restarts at P | — |
| Never reported | Enrolled Device that never reports | Alert opens one window after enrolment | — |

</intent-contract>

## Code Map

- `apps/cs/server/Devices/DeviceGrain.cs` -- the evaluator. `Heartbeat` (:398) journals `DeviceSeen`; `Ingest` (:475) commits the frame, journals `DeviceRelayChanged` at step 7 (:575) and has `frame.Rows.MeasuredAt`. Existing reminder pattern: `ReceiveReminder` (:259), `RegisterReminderAsync` (:354). `OnActivateAsync` (:166).
- `apps/cs/server/Devices/DeviceState.cs` -- journaled state; next free `[Id]` is 14. `Apply(DeviceEnrolled|DeviceSeen|DeviceResumed)` are where the silence clock moves.
- `packages/cs/contracts/Devices/DeviceEvents.cs`, `DeviceGrains.cs` -- new events and the `IDeviceGrain` additions go here (events must be public types in Contracts: `EventTypeRegistry` scans that assembly).
- `apps/cs/server/Sensors/SensorGrain.cs` (`DeliverAlertsAsync`) and `SensorState` (`PendingAlertDeliveries`) -- the pattern to copy for journaled, ordered, retried Alert delivery.
- `packages/cs/contracts/Alerts/AlertGrains.cs`, `AlertEvents.cs` -- `AlertKind` (only `Threshold`), `OpenAlert`, `AlertOpened`, `AlertSnapshot`: `LotId`, `SensorId`, `Quantity` are non-nullable today.
- `apps/cs/server/Alerts/AlertGrain.cs` -- `Open` derives the ID with `AlertIds.Threshold` (:91); `Close` accepts only `sensor/{SensorId}` as caller (:131); builds `SiteAlert` with `State.LotId!` (:198). `AlertIds.cs`, `AlertNames.cs` (`Kind` throws on an unnamed kind, which stalls the alerts projector), `AlertState.cs`.
- `packages/cs/contracts/Sites/SiteGrains.cs:786` `SiteAlert`, `packages/cs/contracts/Notifications/Notifications.cs:41` `NotificationEntry` -- carry `LotId`, `SensorId`, `Quantity` opaquely; journaled inside `site.alert-opened` and `user.alert-tracked`.
- `apps/cs/server/Identity/UserGrain.cs:412` `Entry(SiteAlert)`; `apps/cs/server/Notifications/ReminderIntervalRule.cs` -- `IsHealth(kind) => kind != Threshold` and `HealthFloor` 24 h already exist.
- `apps/cs/server/Notifications/Push/PushContent.cs` (:100, :140-180), `PushText.cs`, `PushText.en.json` -- a null Lot name is treated as "Lot gone" and dropped from summaries; reading and Threshold lookups run for every entry.
- `apps/cs/server/Alerts/AlertsProjector.cs`, `AlertsReadModel.cs` (`ListSql`, `CountOpenSql` inner-join `lots`), `apps/cs/migrations/Migrations/M20261009093000CreateTableAlerts.cs` -- `lot_id`, `sensor_id`, `quantity` are NOT NULL.
- `apps/cs/server/Lots/LotsProjector.cs` -- sees `device/` and `alert/` streams; maps an Alert to a Lot by `device_id = lots.claimed_by` (:395-441); `KindName` (:444); the only silence source is "Node declared nothing" (:220); `InputsSql` (:151). `LotStatusRule.cs` already has `LotSilence.Node|Hub` and `unknown`. Tables: `lot_status_devices`, `lot_status_alerts` (`sensor_id`, `quantity` NOT NULL); rebuild pattern in `M20261008120000CreateTableLotStatusAlerts.cs`.
- `apps/cs/server/Hosting/SiloExtensions.cs`, `Program.cs` -- no record of the Server start exists.
- Tests: `tests/cs/server.integration/Identity/IdentityCluster.cs` (TestCluster, `FakeTimeProvider`, in-memory reminders fired by hand, `RestartSiloAsync` resets the clock to `Start`); helpers to reuse in `Devices/ThresholdAlertGrainTests.cs` (`GardenAsync`, `IngestAsync`, `CatchUpLotsAsync`, `LotStatusesAsync`) and `Devices/IngestGrainTests.cs` (`SeedDeviceAsync`); `Identity/DeliveryTimingGrainTests.cs` (`SiteAsync`, `SentAsync`).
- Guards: `tests/cs/server.tests/Journal/FixtureJournalReplayTests.cs` needs a `Fixtures/journal.json` row per new alias; `Alerts/AlertsReadModelUnitTests.cs` needs a lowercase name per `AlertKind`; `Lots/LotStateTests.cs:112` pins the projector's silence inputs; `Notifications/ReminderIntervalRuleTests.cs:14` fakes the Health kind.

## Tasks & Acceptance

**Execution:**
- [x] `packages/cs/contracts/Alerts/AlertGrains.cs`, `AlertEvents.cs` -- add `AlertKind.Silent`; add `OpenDeviceAlert` and `IAlertGrain.OpenForDevice`; make `LotId`, `SensorId`, `Quantity` nullable on `AlertOpened` and `AlertSnapshot` -- a Device Alert has no Sensor and maybe no Lot.
- [x] `packages/cs/contracts/Sites/SiteGrains.cs`, `packages/cs/contracts/Notifications/Notifications.cs` -- same three members nullable on `SiteAlert` and `NotificationEntry`.
- [x] `apps/cs/server/Alerts/AlertIds.cs`, `AlertNames.cs`, `AlertState.cs`, `AlertGrain.cs` -- `AlertIds.Silent`, name `silent`, `OpenForDevice`, and `Close` accepted from `device/{DeviceId}` for a Device Alert.
- [x] `packages/cs/contracts/Devices/DeviceEvents.cs` -- events `device.reported`, `device.silence-window-set`, `device.silence-clock-restarted`, `device.silence-started`, `device.silence-ended`, `device.alert-delivered`.
- [x] `packages/cs/contracts/Devices/DeviceGrains.cs` -- `SetSilenceWindow(siteId, window)` and `Silence()` returning window, clock start, due-at, open Alert and last recovery.
- [x] `apps/cs/server/Devices/SilenceRule.cs` (new) -- defaults, bounds and the due-at arithmetic as pure functions.
- [x] `apps/cs/server/Devices/ServerStart.cs` (new), `DevicesHostingExtensions.cs` -- the Server start time, read once from `TimeProvider` when the host starts.
- [x] `apps/cs/server/Devices/DeviceState.cs` -- clock start, window override, episode, open Alert, last recovery, pending Alert deliveries, with their `Apply` overloads.
- [x] `apps/cs/server/Devices/DeviceGrain.cs` -- journal `DeviceReported` for a newer accepted `measured_at`; evaluate on the `silence-window` reminder, on activation and after every accepted heartbeat or frame; Hub suppression through the relay Hub's `Silence()`; deliver opens and closes; keep the reminder registered for every enrolled Device.
- [x] `apps/cs/server/Hosting/SiloExtensions.cs` -- a startup task that activates every enrolled Device so a Device enrolled before this change gets its reminder; a failure only logs.
- [x] `apps/cs/migrations/Migrations/M20261011090000AllowDeviceAlerts.cs` (new) -- drop NOT NULL on `alerts.lot_id|sensor_id|quantity` and `lot_status_alerts.sensor_id|quantity`; add `lot_status_devices.relay_hub_id`; rebuild the lots projection.
- [x] `apps/cs/server/Alerts/AlertsProjector.cs`, `AlertsReadModel.cs` -- store a `silent` Alert; the list and the open count select Threshold Alerts only.
- [x] `apps/cs/server/Lots/LotsProjector.cs` -- record the relay Hub from `DeviceRelayChanged`; an open `silent` Alert on the Lot's Node gives `LotSilence.Node`, otherwise one on its relay Hub gives `LotSilence.Hub`; re-evaluate every Lot behind a Hub when its Alert opens or closes; store kind `silent`.
- [x] `apps/cs/server/Identity/UserGrain.cs`, `apps/cs/server/Notifications/Push/PushContent.cs`, `PushText.cs`, `PushText.en.json` -- pass the nullable members through; no reading or Threshold lookup for a non-Threshold entry; an entry without a Lot is kept and titled neutrally.
- [x] `tests/cs/server.integration/Identity/IdentityCluster.cs` -- `RestartSiloAsync(DateTimeOffset)` starting the fake clock at a given time; a helper that fires the Device's silence reminder.
- [x] `tests/cs/server.integration/Devices/SilentDeviceAlertGrainTests.cs` (new) -- every row of the matrix, plus Lot status and the once-per-day Reminder.
- [x] `tests/cs/server.tests/Devices/SilenceRuleTests.cs`, `DeviceStateSilenceTests.cs` (new); update `Alerts/*`, `Lots/LotStateTests.cs`, `Notifications/ReminderIntervalRuleTests.cs`, `Notifications/PushTextTests.cs`, `Fixtures/journal.json` -- unit coverage and the guards named in the Code Map.
- [x] `apps/cs/README.md`, `packages/cs/README.md`, `packages/openapi/coldframe.openapi.json` (description text of `AlertKind` only, if it states what the Server produces) -- document silence, the new events and what is left to Story 7.2.

**Acceptance Criteria:**
- Given an open `silent` Alert on a Lot's Node, when the lots projection catches up, then the Lot's status is `unknown` with `unknownCause` `node`; given one on the Node's last relay Hub only, then `unknown` with `unknownCause` `hub`; when the Alert closes, the Lot returns to the status it would have without it.
- Given a `silent` Alert tracked by a member, when Reminders are scheduled, then the next Reminder is not sent before 24 h after the previous notification.
- Given a `silent` Alert without a Lot, when it is delivered alone or in a summary, then a notification is sent and nothing throws.
- Given the journal fixture with the new events, when it is replayed, then every row applies and the existing assertions still hold.
- Given the `listAlerts` endpoint and a Site with an open `silent` Alert, when it is called, then the response validates against the unchanged contract and lists no `silent` Alert.
- Given TestCluster tests with a fake clock, when they run, then they cover Node and Hub default windows, a per-Device override, recovery, a Server restart that causes no false silence, Hub-silence suppression with 5 Nodes (one Alert), and suppression lifting on Hub recovery.

## Spec Change Log

## Review Triage Log

### 2026-10-11 — Review pass
- verdicts: 4 findings — high 0, medium 1, low 3
- findings:
  - `[medium]` `[patch]` The startup task activated every enrolled Device one after another inside the silo start, so heartbeats and frames waited on a replay of every Device journal. Fixed: the pass runs beside the start and ends when the host stops (`SiloExtensions.cs`).
  - `[low]` `[defer]` No test shows that a Device enrolled before this story gets its silence reminder: the TestCluster does not go through `SiloExtensions`. Deferred as DW-95.
  - `[low]` `[defer]` The Server start is read per process, so with more than one silo, or restarts more often than a Node's window, a dead Node's Alert is postponed. It follows the "last Server start" rule of the intent. Deferred as DW-96.
  - `[low]` `[patch]` The once-per-day assertion in `SilentDeviceAlertGrainTests` cannot fail for the reason its comment gave (the default cadence is also 24 h). The comment now names `ReminderIntervalRuleTests` as the proof.

## Design Notes

- **Due-at as derived state.** `DeviceState` keeps the clock start (`SilenceFrom`) and the window override; `SilenceDueAt = SilenceFrom + window`. Both come from journaled events, so the deadline survives a restart and a changed window moves it without another event. `DeviceSeen` and the new `DeviceReported` move the clock; `device.silence-clock-restarted` records a Server start or a Hub recovery that moved it.
- **Why `device.reported`.** A Node journals nothing per frame today, so its liveness would not be in grain state. One small event per accepted frame with a newer `measured_at` (about one per 15 min per Node) keeps the deadline in the journal, as AD-6 asks. A paused Node journals none: its window restarts on resume.
- **Open means overdue.** After any accepted report the grain compares `now` with the due-at: an open Alert closes only when the due-at is in the future again. A Hub heartbeat always satisfies this; a Node flushing an old backlog does not.
- **Suppression is pulled, not pushed.** A Hub does not know the Nodes behind it. A Node whose window expired asks its last relay Hub's grain: silent → open nothing; recovered after the Node's clock start → restart the clock at the Hub's recovery time; otherwise open. The Hub's `Silence()` processes its own overdue deadline first, so the answer does not depend on which reminder fired first.
- **Node and Hub both silent.** If a Node's Alert was already open when its Hub went silent, both stay open and the Lot shows cause `node` (the more specific one).
- **Contract stays closed.** `Alert` in OpenAPI requires `quantity`, `lotId` and `lotName`; relaxing that is a breaking change for the generated clients, so it moves with the apps in Story 7.2. Until then `silent` Alerts are stored in the read model but not listed.

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror && dotnet format --verify-no-changes --no-restore` -- expected: no error, no change.
- `dotnet test --no-build --project tests/cs/server.tests` -- expected: all pass.
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build --project tests/cs/server.integration` -- expected: all pass, none skipped.
- `packages/proto/check-compat.sh` -- expected: no breaking OpenAPI or Protobuf change.
- `pnpm --filter @coldframe/openapi run check` -- expected: passes (only if the OpenAPI file was edited).
