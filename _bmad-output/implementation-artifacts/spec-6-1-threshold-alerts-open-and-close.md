---
title: 'Story 6.1: Threshold Alerts open and close'
type: 'feature'
created: '2026-10-08'
baseline_revision: 'c928cb1a6e0b2c4ea17328044e4577f39e26c884'
status: 'blocked'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-5-3-thresholds-on-the-server.md'
  - '{project-root}/apps/cs/README.md'
warnings: ['oversized']
deferred:
  - summary: >-
      An open Threshold Alert never closes once its Sensor stops being eligible, for example when an Administrator turns alerts off (clears the low) while the Alert is open.
    evidence: |-
      SensorGrain.Evaluate returns NotEligible before it looks at State.OpenAlert, so no Reading is evaluated again; the Site keeps listing the Alert and the Lot stays needsWater until a low is set again. AD-7 lists the close reasons (recovered, paused, unassigned, calibrated, removed) and none covers Thresholds that were removed, so closing here needs an architecture decision: which reason, and whether at once or after three Readings.
    location: >-
      apps/cs/server/Sensors/SensorGrain.cs (Evaluate, step 2)
    severity: medium
  - summary: >-
      An open Alert of a Sensor that leaves its Node's accepted Specification set stays in Site.OpenAlerts() while the Lot status no longer shows it.
    evidence: |-
      DeviceGrain.EvaluateAsync stops handing that Sensor Readings, so it can never recover; the lots projector hides the Alert through "a.sensor_id = ANY (d.sensor_ids)", so the Site set and the Lot status disagree. The close reason "removed" (AD-7) belongs to a later epic. No test isolates the sensor_ids clause.
    location: >-
      apps/cs/server/Devices/DeviceGrain.cs (EvaluateAsync), apps/cs/server/Lots/LotsProjector.cs (InputsSql)
    severity: medium
  - summary: >-
      The open Alert of a Node that was moved keeps naming the Lot it was opened for, while the Lot status follows the Node to its new Lot.
    evidence: |-
      AlertOpened.LotId, SiteAlert.LotId and AlertSnapshot.LotId are set at open and never change; the projector finds an Alert by Device. Story 6.2 and the notification text would name the old Lot. AD-8 closes a Sensor's Alerts on an unassigned or paused context (reasons unassigned, paused), which is not built yet.
    location: >-
      packages/cs/contracts/Alerts/AlertGrains.cs, apps/cs/server/Devices/DeviceGrain.cs (Move, Unassign)
    severity: medium
  - summary: >-
      IDeviceGrain.SetCalibration is now [AlwaysInterleave] and no test runs a Calibration delivery concurrently with an ingest.
    evidence: |-
      Without it Device.Ingest (awaiting Sensor.Evaluate) and Sensor.Calibrate (awaiting Device.SetCalibration) wait on each other until a call times out. With it SetCalibration's RaiseEvent/ConfirmEvents can run inside Ingest's, Move's or Unassign's awaits. Whether a failed append then fails both turns is not shown. Settling it needs a fixture hook that holds one grain call mid-turn and a test that runs Calibrate and Ingest of the same Node together.
    location: >-
      packages/cs/contracts/Devices/DeviceGrains.cs:111
    severity: medium (unverified)
  - summary: >-
      A Sensor hovering at a Threshold journals sensor.streak-changed on almost every Reading, and every Alert episode adds two events to the Site stream; both streams replay in full (DW-7, DW-45).
    evidence: |-
      Evaluation uses the 5 %-rounded percentage, so a value near the low alternates between below and within and each alternation changes the streak: up to about 96 events a day per Sensor. site.alert-opened and site.alert-closed grow the Site stream the same way. Harmless until streams are long; journal snapshots (DW-7) are the fix.
    location: >-
      apps/cs/server/Sensors/SensorGrain.cs (Evaluate, step 6), apps/cs/server/Identity/SiteGrain.cs
    severity: low
  - summary: >-
      An Alert's openedAt and closedAt are the Server clock at evaluation, not the Reading's measured_at.
    evidence: |-
      A Node that uploads a buffered backlog in order opens and closes Alerts for conditions hours old with times seconds apart, so status_since and the "started" label of Story 6.2 would be off. measuredAt is on the Sensor's episode events but is not passed to OpenAlert, AlertOpened or SiteAlert; adding it changes the contracts.
    location: >-
      apps/cs/server/Sensors/SensorGrain.cs (Evaluate), packages/cs/contracts/Alerts/AlertEvents.cs
    severity: low
  - summary: >-
      If ConfirmEvents throws after RaiseEvent queued an episode or streak event, a resent frame might raise the same event again.
    evidence: |-
      Believed not to happen: JournaledGrain.State is the tentative view, which already holds the unconfirmed event, so the resend sees the episode. Not shown by a test; the same raise-then-confirm pattern is used by every journaled grain. A FaultyJournalStore test that fails one append during Evaluate would settle it.
    location: >-
      apps/cs/server/Sensors/SensorGrain.cs (Evaluate, ConfirmEvents)
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** Thresholds are stored (5.3) but nothing acts on them: no Reading reaches a Sensor grain, no Alert exists anywhere on the Server, the Site grain knows no open Alerts, and the LotStatus projection hardcodes `OpenLowAlert: false`, so *needs water* never appears.

**Approach:** After a frame is committed the Device grain hands each declared Sensor its Reading with an evaluation context (Site, Lot, epoch). The Sensor grain counts streaks, persists a new episode and opens or closes an Alert through a new event-sourced Alert grain, which reports per Site to the Site grain. The Lots projector derives `needsWater` from the Alert events.

## Boundaries & Constraints

**Always:**
- Opening needs 3 consecutive evaluated Readings beyond the same side (value `<` effective low, or `>` effective high); closing needs 3 consecutive within. Any Reading off the running side resets that streak. An empty high never alerts.
- A calibrating Sensor is compared as the percentage `CalibrationMath.Percent` gives under the grain's current Calibration; any other Sensor in its Specification unit.
- No evaluation when: the calibrating Sensor is uncalibrated, there is no effective low, the slot is not in the Device's declared Sensor list (AD-19), or the Node is unassigned (AD-8). The Reading is still stored.
- Monotonic: a Reading whose `measured_at` is not newer than `lastEvaluatedAt` is not evaluated, so a resent frame changes nothing. A frame is evaluated on first delivery and on a duplicate resend alike, and a failed evaluation answers the frame `retry`.
- `SensorThresholdsChanged` and a changed epoch reset both streaks; an open Alert stays open. The epoch is Device state derived from its existing assignment and Pause events.
- `alertId = UUIDv5(AlertNamespace, "sensor:{sensorId:D}:threshold:{episode}")` with a Server-only namespace constant. The episode is journaled on the Sensor stream before `Alert.Open`; an unacknowledged open or close is redelivered (timer, reminder, activation) like Calibration delivery. `Open` and `Close` are idempotent; at most one open Threshold Alert per Sensor; the Alert records side, Site, Lot, Sensor, Device and quantity; `Close` is accepted only from the opening Sensor, with a reason from `recovered | paused | unassigned | calibrated | removed` (only `recovered` is produced here).
- The Alert grain reports each open and close to its Site grain idempotently and redelivers until acknowledged; the Site grain journals them and answers `OpenAlerts()` from state replayed from its own stream.
- Streak, side, episode, open Alert and pending deliveries survive a silo restart. Time only from `Clock`. Contracts carry `[GenerateSerializer]`, stable `[Alias]`, `[property: Id]`; events are past-tense with `[EventType]`.
- Test-first (NFR16, AD-24): the named tests fail before implementation.

**Never:** No REST endpoint, OpenAPI or client change (6.2). No Notification, Reminder, window or User grain work (6.3-6.6). No closing on Pause, unassign, recalibration or removal (Epics 7-8). No Health Alerts. No new messaging mechanism (AD-5): grain calls and the journal only. A grain never reads a read model (AD-1). No edit to a shipped migration, no change to `packages/crypto-spec`, no write to `sprint-status.yaml`, no journal event per steady in-range Reading (DW-7: streams replay in full).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Exactly three open | low 30 %, Readings 20, 20, 20 | none after 2; third journals episode 1, Alert open, side low | — |
| 2-of-3 | 20, 20, 40, 20, 20 | no Alert, no episode | — |
| Exactly three close | open low; 40, 40, 40 | closed after the third, reason `recovered` | — |
| Flapping | open low; 40, 40, 20, 40, 40 | stays open | — |
| Low-to-high switch | open low, high 60; 70, 70, 70 | low Alert closed `recovered`, episode 2 opens with side high | — |
| Out-of-order backlog | Reading older than `lastEvaluatedAt` | stored, not evaluated, streak unchanged | — |
| Retried evaluation | Alert or Site grain call fails once; frame resent | one Alert, one episode, one entry in the Site set | frame answered `retry`; redelivery completes |
| Not eligible | uncalibrated, no low, undeclared slot, unassigned Node | stored, no Alert | — |
| Reset | streak 2, then Thresholds changed or epoch changed | streak 0; three more needed | — |
| Needs water | open low Alert, soil-moisture Sensor | Lot status `needsWater`, first in the list; back to `ok` on close | high side or other quantity: unchanged |
| Restart | streak 2 or open Alert, silo restarts | next Reading continues; Site still lists the Alert | — |

</intent-contract>

## Code Map

- `apps/cs/server/Devices/DeviceGrain.cs` -- `Ingest` l.474-632: pause gate l.554, `CommitAsync` l.558, declaration l.584-606. Add the per-Sensor evaluate call after the commit, also when `NewKeys == 0`; gate on `State.LotId`, `State.Sensors`. `Move` l.178, `Unassign` l.234. `DeviceState` (`SiteId` Id 0, `LotId` Id 6): add the derived epoch in the existing `Apply` methods.
- `apps/cs/server/Devices/DeviceIngestionStore.cs` -- `ReadingWrite(SensorId, ReadingSeq, Slot, Quantity, RawValue, CalibrationId)` l.22, `FrameRows(MeasuredAt, …)` l.42: the data to pass on.
- `packages/cs/contracts/Sensors/{SensorGrains,SensorEvents}.cs` -- `ISensorGrain` l.9; add `Evaluate` and its context/result records; `SensorThresholdsChanged` l.45 is the pattern for the new evaluation events.
- `apps/cs/server/Sensors/SensorGrain.cs` -- `JournaledStreamGrain<SensorState>, IRemindable`; `SetThresholds` l.106; `Calibrate`/`DeliverAsync` l.185-284 is the persist, call, acknowledge, retry model; never call back into the Device grain from `Evaluate` (non-reentrant).
- `apps/cs/server/Sensors/SensorState.cs` -- Ids 0-8 used, next `Id(9)`; `EffectiveLow`/`EffectiveHigh`/`Calibrated` l.88-93; `Apply(SensorThresholdsChanged)` l.111 resets streaks. `CalibrationMath.cs`, `ThresholdRules.cs` for conversion and sides; put the streak rule in a pure static class beside them.
- `packages/cs/contracts/Alerts/`, `apps/cs/server/Alerts/` (new) -- `IAlertGrain`, `AlertState`, events `alert.opened`/`alert.closed`, `[GrainType("alert")]`, stream `alert/{id}`, `AlertsHostingExtensions` wired in `Program.cs`.
- `packages/cs/crypto/SensorIds.cs` -- reuse `UuidV5(Guid, string)`; the namespace constant lives in the Server.
- `packages/cs/contracts/Sites/SiteGrains.cs` l.38, `apps/cs/server/Identity/{SiteGrain,SiteState}.cs` (Ids 0-7 used) -- add `AlertOpened`/`AlertClosed`/`OpenAlerts`, events `site.alert-opened`/`site.alert-closed`; `CreateLot` l.170-210 is the requested/completed precedent. The Site grain never calls User grains.
- `apps/cs/server/Lots/LotsProjector.cs` -- `InputsSql` l.125-140, `InputsOf` l.181-187 (`OpenLowAlert: false`), `ApplyAsync` l.190 (add an `alert/` branch), `EvaluateAsync` l.368. `LotStatusRule.cs` l.111 and `LotsReadModel.StatusOrder` l.51 already handle `needsWater`: read-only.
- `apps/cs/migrations/Migrations/` -- new `M<yyyyMMddHHmmss>…` after `M20261007120000CreateTableCalibrations` for the projector's open-alert support table; delete the `lots` rows and checkpoint as `M20261006170000AddLotStatusToLots` did.
- `tests/cs/server.integration/Identity/IdentityCluster.cs` -- fixture, `Time`, `RestartSiloAsync`, fault filters (`SensorFaults` is the model for Alert and Site faults). Model on `Devices/CalibrationGrainTests.cs` (`DeclaredNodeAsync` l.380, `StoreSoilAsync` l.393) and `Devices/ThresholdGrainTests.cs`; `[Collection(IngestSuites.Name)]`. Frames more than 5 min ahead of the fake clock are rejected.
- `tests/cs/server.tests/` -- `Fixtures/journal.json` and `FixtureJournalReplayTests` `States` l.20-28 (every new alias, `["alert"]` state); `Lots/LotStateTests.cs` l.120; `Sensors/{ThresholdRulesTests,SensorStateTests}.cs` as the model for pure rule tests. `tests/cs/server.integration/Edge/LotStatusTests.cs` hand-writes a `needsWater` row.
- `apps/cs/README.md` -- "Sensor Specifications", "Thresholds", "Lot status" say "before Epic 6" / "no producer yet"; add an Alerts section.

## Tasks & Acceptance

**Execution:**
- `tests/cs/server.tests/Sensors/`, `tests/cs/server.integration/Devices/ThresholdAlertGrainTests.cs`, `tests/cs/server.integration/Edge/LotStatusTests.cs` -- write the failing tests first, one per matrix row -- NFR16
- `packages/cs/contracts/{Sensors,Alerts,Sites}/**` -- grain methods, records, events -- contracts first
- `apps/cs/server/Sensors/**` -- streak rule, state, `Evaluate`, episode, open/close delivery with retry
- `apps/cs/server/Alerts/**`, `apps/cs/server/Program.cs` -- Alert grain, Site reporting with retry, hosting
- `apps/cs/server/Identity/{SiteGrain,SiteState}.cs` -- open-Alert set and `OpenAlerts()`
- `apps/cs/server/Devices/{DeviceGrain,DeviceState}.cs` -- evaluate after commit, epoch, eligibility gates
- `apps/cs/server/Lots/LotsProjector.cs`, `apps/cs/migrations/Migrations/**` -- `OpenLowAlert` from Alert events
- `tests/cs/server.tests/Fixtures/journal.json`, `FixtureJournalReplayTests.cs`, `Lots/LotStateTests.cs` -- new aliases and inputs
- `apps/cs/README.md` -- document evaluation, Alerts, the Site set; remove the stale statements

**Acceptance Criteria:**
- Given a calibrated Sensor with Thresholds on an assigned Node, when three consecutive Readings at 15-minute `measured_at` steps cross a Threshold, then one Alert is open with the UUIDv5 ID and side, the Sensor stream holds the episode before the Alert stream holds `alert.opened`, and `Site.OpenAlerts()` lists it.
- Given an open Alert, when another grain or Sensor calls `Close`, then it is refused and the Alert stays open.
- Given Alert events in the journal, when the `lots` read model is rebuilt from position 0, then every Lot has the same status as before.
- Given the TestCluster suite, when it runs, then it covers exactly-three opening and closing, flapping, a retried evaluation with no duplicate, the low-to-high switch, the out-of-order backlog and the 2-of-3 cases, each written failing first.

## Spec Change Log

## Review Triage Log

### 2026-10-08 — Review pass
- verdicts: 53 findings — high 0, medium 9, low 28, false 12, maybe-false 4
- findings:
  - `[medium]` `[defer]` (blind) An open Alert never closes once its Sensor stops being eligible (low cleared) — verified: `Evaluate` returns `NotEligible` before `State.OpenAlert` is read; the contract mandates "no evaluation without an effective low" and AD-7's close reasons do not cover it, so it needs an architecture decision; recorded in `deferred`.
  - `[medium]` `[defer]` (blind) Same for a Sensor that leaves the accepted Specification set; Site set and Lot status disagree — verified in `EvaluateAsync` and `InputsSql`; reason `removed` belongs to a later epic; recorded in `deferred`.
  - `[false]` `[reject]` (blind) A delivery that can never succeed blocks acknowledgements for good — `Refused`/`NotOpened` cannot come back for a delivery built from the Sensor's own state (ID derived from its own Sensor ID and episode, opens delivered before closes, the caller is the opening Sensor); a transient failure answering `retry` is what the matrix asks.
  - `[low]` `[reject]` (blind) The acknowledgement waits on a four-grain chain — real, but only on the Reading that opens or closes an Alert, and the matrix row "Retried evaluation" requires the frame to answer `retry` until the Site holds the Alert; bounding it adds timeouts and state.
  - `[maybe-false]` `[defer]` (blind) `[AlwaysInterleave]` on `SetCalibration` is an untested concurrency change — needs a test that runs Calibrate and Ingest together; recorded in `deferred` (medium, unverified).
  - `[low]` `[defer]` (blind) A hovering Sensor journals an event on nearly every Reading — verified; bounded at about 96 a day; the cost is the missing snapshots (DW-7), pre-existing; recorded in `deferred`.
  - `[low]` `[defer]` (blind) The Site stream grows by two events per episode — same root cause (DW-7); recorded with the row above.
  - `[medium]` `[defer]` (blind) A moved Node's Alert names the old Lot — verified: `LotId` is set at open only; closing on move or unassign is AD-8 work of a later epic; recorded in `deferred`.
  - `[low]` `[defer]` (blind) `openedAt`/`closedAt` are the Server clock, not `measured_at` — verified; follows the codebase's event-time convention, changing it alters the contracts; recorded in `deferred` for Story 6.2 to decide.
  - `[low]` `[reject]` (blind) `IAlertGrain.Open` is not caller-restricted — grains are reachable only from Server code; no untrusted caller exists, and the contract restricts `Close` only.
  - `[low]` `[reject]` (blind) The close refusal is tested with the test client only — no production path lets another Sensor call `Close`; a second-grain test would need test-only surface.
  - `[low]` `[patch]` (blind) A Site that is not Active acknowledges and drops the report, untested — added `ASiteThatIsNotActiveKeepsNoAlerts`.
  - `[low]` `[reject]` (blind) `lot_status_alerts` keeps closed Alerts under a plain index — negligible at a home garden's volume; a partial index needs raw SQL in the migration.
  - `[low]` `[reject]` (blind) The migration empties `calibrations` without saying what readers see meanwhile — migrations run before the Server starts and the projector rebuilds from position 0 at start, as `M20261006170000` already did; documentation only.
  - `[low]` `[patch]` (blind) `EvaluateAsync` says "Never throws" but built its calls outside the `try` — the calls are now built inside it. (Its `KindName` remark is the edge-case row below.)
  - `[low]` `[reject]` (blind) The reminder paths of redelivery have no test of their own — timer, activation and the next evaluation are tested; isolating the reminder needs fixture work beyond a direct fix.
  - `[medium]` `[defer]` (edge) Low cleared or Sensor uncalibrated while its Alert is open — same defect as the first row; shares its route.
  - `[low]` `[reject]` (edge) Backlog Readings measured before the current assignment open an Alert for the new Lot — needs a buffered backlog across a move; the fix journals a new epoch time on the Device.
  - `[low]` `[patch]` (edge) `sensor.calibrated` / `sensor.specification-changed` mid-streak mix Readings compared differently — both `Apply` methods now reset the streak; two unit tests added.
  - `[low]` `[patch]` (edge) A Reading seen while not eligible did not advance `lastEvaluatedAt` — it now does, in memory, before `NotEligible` is returned.
  - `[low]` `[reject]` (edge) No short bound on the evaluation calls — rare (a slow Site or Alert grain); a separate timeout adds a second failure mode next to the Orleans one.
  - `[low]` `[patch]` (edge) Two failing deliveries could stack in one turn — the second attempt now runs only when the first succeeded.
  - `[low]` `[reject]` (edge) `Open` accepts any caller — same as the blind row: no untrusted caller.
  - `[low]` `[reject]` (edge) `Site.AlertOpened/AlertClosed` accept any caller — same reason.
  - `[low]` `[reject]` (edge) `Sensor.Evaluate` accepts any caller — same reason.
  - `[false]` `[reject]` (edge) A far-ahead `measured_at` stalls evaluation — ingestion rejects synced frames more than 5 minutes ahead (`rejected_time`), so the stall is at most 5 minutes.
  - `[maybe-false]` `[defer]` (edge) `ConfirmEvents` throwing after `RaiseEvent` duplicates the episode on a resend — `State` is believed to be the tentative view, which would prevent it; a failing-append test would settle it; recorded in `deferred` (medium, unverified).
  - `[low]` `[reject]` (edge) `KindName` stores an unknown kind under its C# name — only `Threshold` exists; throwing instead would stop the projector when Epic 7 adds kinds.
  - `[maybe-false]` `[defer]` (edge) An append failing while `SetCalibration` interleaves with `Ingest` fails both turns — same root as the interleave row; shares its `deferred` entry.
  - `[medium]` `[patch]` (verification-gap) A new epoch at a streak of exactly one was not pinned — added `ANewEpochAfterOneReadingNeedsThreeMoreAndTheThirdOpensTheAlert`.
  - `[medium]` `[patch]` (verification-gap) The acknowledgement test could not tell an open from a close of one Alert — the unit test now asserts the close is still pending after the open is acknowledged.
  - `[medium]` `[patch]` (verification-gap) Nothing showed a paused Node is not evaluated — added `APausedNodesReadingsAreNotEvaluated`.
  - `[low]` `[patch]` (verification-gap) Evaluation on the rounded percentage was not observed — added `AReadingIsComparedAsThePercentageRoundedToFiveThatTheLotTileShows`.
  - `[medium]` `[defer]` (verification-gap) `[AlwaysInterleave]` has no test that fails without it — filed as defer; shares the interleave `deferred` entry.
  - `[low]` `[reject]` (verification-gap) The Alert grain's own re-reporting never finishes a report in a test — filed as defer, but only low: the Sensor's redelivery reaches the same outcome today.
  - `[low]` `[patch]` (verification-gap) A Site that is not Active is untested — same test as the blind row.
  - `[low]` `[reject]` (verification-gap) The migration's rebuild SQL only runs against an empty database — filed as defer, but only low and the pattern predates this story (`M20261006170000`).
  - `[medium]` `[defer]` (verification-gap, other) A Sensor that stops being eligible cannot close its Alert — same defect as the first row.
  - `[low]` `[defer]` (verification-gap, other) The projector's `sensor_ids` clause is not isolated by a test — part of the Specification-set row; shares its `deferred` entry.
  - `[false]` `[reject]` (intent) Tests enter at the Device grain, not the HTTP ingest endpoint — the story asks for TestCluster tests; the endpoint only forwards to `IDeviceGrain.Ingest`.
  - `[low]` `[reject]` (intent) No single test runs frame to HTTP Lot list — the grain suite asserts the read model from real Readings and `LotStatusTests` asserts the HTTP list from Alert events; a combined test adds no new path.
  - `[false]` `[reject]` (intent) The client surface is untouched — the clients already render `needsWater` (contract enum since Epic 4); the Alerts surface is Story 6.2.
  - `[false]` `[reject]` (intent) "Rebuildable from the journal" is exercised by replay, restart and rebuild tests — no divergence.
  - `[false]` `[reject]` (intent) Evaluation is monotonic, not sorted — AD-7 defines the order by exactly this rule; one call carries one frame.
  - `[low]` `[reject]` (intent) `lastEvaluatedAt` can lag after a reactivation — accepted in Design Notes; the worst case is a streak of 1, and journaling every Reading is what DW-7 forbids.
  - `[false]` `[reject]` (intent) "Published per Site" is a grain call — AD-5 allows grain calls and the journal only.
  - `[false]` `[reject]` (intent) "Below" uses the rounded percentage — a calibrated soil Threshold moves in 5 % steps and the Server has one percentage; see Design Notes.
  - `[false]` `[reject]` (intent) The low-to-high switch closes and reopens — the story names this test case.
  - `[false]` `[reject]` (intent) The epoch takes effect with the next Reading — a streak is only read when a Reading arrives, so the outcome is the same; closing on move is the deferred row above.
  - `[false]` `[reject]` (intent) Only the opening Sensor closes, by caller identity — conforms.
  - `[low]` `[reject]` (intent) The migration rebuilds the read model and its support tables — same as the blind migration row.
  - `[maybe-false]` `[defer]` (intent) `SetCalibration` became `[AlwaysInterleave]` — shares the interleave `deferred` entry.
  - `[false]` `[reject]` (intent) Ingestion gained a `retry` path — required by the matrix row "Retried evaluation".

### 2026-10-08 — Review pass (follow-up)
- verdicts: 51 findings — high 0, medium 15, low 22, false 13, maybe-false 1
- intent gap: no change was attempted in this pass, so there is no patch file and nothing was reverted; the story's code stays as merged in `5be1e97`. The rows routed `patch` were not applied (moot under the intent gap) and are still open.
- findings:
  - `[medium]` `[defer]` (blind) An open Alert never closes once the low is cleared — carried: `Evaluate` step 2 still returns `NotEligible` before `State.OpenAlert` is read; already in `deferred`.
  - `[medium]` `[defer]` (blind) A Sensor dropped from the Specification set, or an unassigned Node, leaves its Alert in the Site set — carried: `EvaluateAsync` and `InputsSql` unchanged; already in `deferred`.
  - `[low]` `[reject]` (blind) A mix of within and above-high Readings does not close a low Alert — verified in `ThresholdStreakRule.Advance`, and it is what the contract says ("closing needs 3 consecutive within", "any Reading off the running side resets that streak"). The Alert closes as soon as three Readings in a row are on one side; counting "not below" instead needs a second counter.
  - `[false]` `[reject]` (blind) Alert delivery has no poison handling — carried: `Refused`/`NotOpened` cannot come back for a delivery built from the Sensor's own state, and `retry` on a transient failure is the matrix row "Retried evaluation".
  - `[medium]` `[intent_gap]` (blind) A resent frame of an unsynced Node is evaluated again — verified: `NodeFrameReader` l.144-153 sets `measured_at = receivedAt - age` for an unsynced Reading, so a resend carries a later `measured_at` (`IngestGrainTests` l.271 says so: "rebased to another time, and still stored once"). `DeviceGrain` step 9 evaluates a duplicate like a first delivery, and `SensorGrain.Evaluate` step 3 lets it pass because it is newer. The same Reading then adds to the streak on every resend: three resends open or close an Alert from one Reading. Resends happen on a lost acknowledgement and on the `retry` this story added. The contract promises "a resent frame changes nothing" and names `measured_at` as the guard; it does not say what holds for a Reading whose `measured_at` is not stable. See the questions under Auto Run Result.
  - `[low]` `[reject]` (blind) `IAlertGrain.Open` and the Site's Alert methods do not check the caller — carried: no untrusted caller reaches a grain.
  - `[false]` `[reject]` (blind) `Close` answers `NotOpened` before the caller check — before an Alert is opened there is no opening Sensor to compare the caller with, and only Server code reaches the grain, so no caller learns anything it should not.
  - `[medium]` `[defer]` (blind) The Alert of a moved Node keeps naming the old Lot — carried: `LotId` is still set at open only; already in `deferred`.
  - `[low]` `[reject]` (blind) The new Lot shows `needsWater` from Readings taken in another Lot — verified and stated in Design Notes; the contract excludes closing on a move ("No closing on Pause, unassign, …"), and which Lot the Alert names is the deferred row above.
  - `[low]` `[defer]` (blind) A hovering Sensor and every episode grow the Sensor and Site streams — carried; already in `deferred` (DW-7).
  - `[low]` `[reject]` (blind) `lot_status_alerts` keeps closed rows under a plain index — carried.
  - `[low]` `[reject]` (blind) `KindName` stores an unknown kind under its C# name — carried: only `Threshold` exists.
  - `[low]` `[patch]` (blind) The README says the migration deletes the rows of five tables, the SQL deletes four (the new `lot_status_alerts` is empty), and the migration summary calls the table "the open Alerts" — verified in `apps/cs/README.md` l.626-628 and the migration l.47-51; wording only. Not applied in this pass.
  - `[false]` `[reject]` (blind) `DeliverAlertsAsync` throws to reach its own `catch` — a style remark; no wrong outcome follows from it.
  - `[false]` `[reject]` (blind) A second deadlock path through the Site grain — `SiteGrain` calls no Device, Sensor or Alert grain, so a Device call queued on the Site waits behind `AlertOpened` and then runs; there is no cycle.
  - `[medium]` `[intent_gap]` (edge) An unsynced frame sent again counts its Reading again — same defect as the blind row above; shares its route.
  - `[medium]` `[defer]` (edge) Low cleared while an Alert is open — carried; already in `deferred`.
  - `[low]` `[reject]` (edge) After a reactivation up to three stale backlog Readings count, not one — verified: the in-memory mark is gone and each backlog Reading is newer than the one before, so all three are evaluated. It needs a reactivation between steady Readings and an out-of-order backlog of three Readings beyond a Threshold; the fix is to journal the mark on every Reading, which DW-7 forbids. The README's "worst case is one stale Reading" understates it.
  - `[false]` `[reject]` (edge) A permanent refusal is retried for good — carried.
  - `[low]` `[reject]` (edge) `Sensor.Evaluate` accepts any caller — carried.
  - `[low]` `[reject]` (edge) `Alert.Open` accepts any caller — carried.
  - `[low]` `[reject]` (edge) `AlertClosed` journals on a Site that is no longer Active while `AlertOpened` does not — verified in `SiteGrain.AlertClosed`; it only removes an Alert the Site still lists, and `OpenAlerts()` answers empty for such a Site anyway. No reader sees a difference.
  - `[low]` `[reject]` (edge) A streak runs across a long silence of the Node — verified: no gap rule exists; the contract counts consecutive evaluated Readings, and the third Reading is a current one. A gap limit adds a parameter the contract does not have.
  - `[medium]` `[patch]` (verification-gap) No test sends an older Reading after steady Readings, so the in-memory half of the "not newer" guard is unpinned — filed with evidence (`SensorGrain.cs` l.174, l.229 can be deleted with all tests green). Not applied in this pass.
  - `[medium]` `[patch]` (verification-gap) The Sensor's own redelivery (timer at `SensorGrain.cs` l.408, reminder branch l.342) is never exercised — filed with evidence: the two `AFailed…` tests finish through the resent frame, and the "nobody calling" test restarts the silo, so it goes through `OnActivateAsync`. This corrects the first pass's row, which took the timer as tested. Not applied in this pass.
  - `[low]` `[reject]` (verification-gap) The Alert grain's own report retry is only reached through the Sensor repeating its call — carried.
  - `[medium]` `[patch]` (verification-gap) No test makes `Evaluate` throw, so the `retry` answer of `DeviceGrain.EvaluateAsync`'s catch is unpinned — filed with evidence (`SensorFaults` only fails `Declare`). Not applied in this pass.
  - `[low]` `[reject]` (verification-gap) The migration's rebuild has no test of its own — carried.
  - `[medium]` `[defer]` (verification-gap) `[AlwaysInterleave]` on `SetCalibration` has no test — carried; already in `deferred`.
  - `[low]` `[reject]` (verification-gap) "Only the opening Sensor closes" is tested with the client only — carried.
  - `[low]` `[defer]` (verification-gap) The projector's `sensor_ids` clause is not isolated by a test — carried; part of the Specification-set entry in `deferred`.
  - `[medium]` `[defer]` (verification-gap, other) A Sensor that stops alerting keeps its Alert open — carried; same defect as the first row.
  - `[false]` `[reject]` (intent) Evaluation compares the rounded percentage — carried.
  - `[medium]` `[defer]` (intent) After a Move or Unassign the Site set names the old Lot, and no test drives it — carried; already in `deferred`.
  - `[false]` `[reject]` (intent) A Site that is not Active acknowledges a report and lists nothing — a Site is `Uncreated`, `Active` or gone; only an Active Site has Nodes on Lots and Users to show an Alert to, and `ASiteThatIsNotActiveKeepsNoAlerts` pins the behaviour.
  - `[low]` `[reject]` (intent) The migration also rebuilds the lots read model — carried.
  - `[maybe-false]` `[defer]` (intent) `SetCalibration` became `[AlwaysInterleave]` — carried; already in `deferred`.
  - `[false]` `[reject]` (intent) `alert.site-notified` is an event the contract does not name — it is how "redelivers until acknowledged" survives a restart.
  - `[false]` `[reject]` (intent) `IAlertGrain.Describe` is an extra method — a read of the Alert's own state, used by the tests; it changes nothing.
  - `[false]` `[reject]` (intent) `AddAlerts()` registers nothing — it is the hosting hook every feature folder has; no wrong outcome.
  - `[false]` `[reject]` (intent) Tests enter at the Device grain, not the HTTP endpoint — carried.
  - `[false]` `[reject]` (intent) Nothing wakes the lots projector when an Alert opens — Alert events go through the journal like every other event, so the outbox sends the usual hint and polling covers the rest (AD-21); the test's manual catch-up only removes the wait.
  - `[low]` `[reject]` (intent) No test runs from a frame to the HTTP Lot list — carried.
  - `[low]` `[reject]` (intent) The "first in the list" assertion of the grain suite compares with a Lot that has no Node, which sorts last anyway — verified at `ThresholdAlertGrainTests.cs` l.462; the line before it asserts `needsWater` directly and `LotStatusTests` pins the order. A sharper check needs a second Node with Readings.
  - `[low]` `[reject]` (intent) The close refusal is tested with the client only — carried.
  - `[medium]` `[patch]` (intent) Only opens are fault-injected: no integration test fails the delivery of a close — verified: both `AFailed…` tests fail the third opening Reading; the pending close is pinned by the unit test only. Not applied in this pass.
  - `[medium]` `[patch]` (intent) The "nobody calling" test activates the Sensor through `Describe` — same gap as the redelivery row of the verification-gap layer; shares its route.
  - `[medium]` `[patch]` (intent) The timer and the reminders are not isolated by a test — same gap; shares its route.
  - `[low]` `[reject]` (intent) The Alert grain's own redelivery is only reached through the Sensor — carried.
  - `[low]` `[reject]` (intent) The lag of `lastEvaluatedAt` after a restart is documented but not tested — carried.
  - `[false]` `[reject]` (intent) Test-first cannot be seen in a squashed diff — true of any squashed change; not a defect of this one.

## Design Notes

- **Low-to-high switch:** Readings above high are not "within", so a strict reading would leave "needs water" on a Lot that is too wet. Three consecutive Readings on the opposite side therefore close the Alert as `recovered` and open the next episode on that side in the same evaluation.
- **Journal volume:** journal a Sensor evaluation event only when streak, side, episode or Alert state changes. `lastEvaluatedAt` advances in memory on steady in-range Readings, so after a restart it may lag to the last journaled value; the worst case is one stale Reading starting a streak of 1.
- **Rounded percentage:** evaluation uses the same 5 %-rounded value the Lot tile shows, so an Alert never contradicts what the User reads ("~30 %, your low is 30 %" is not below).
- **Site and Lot travel in the call:** the Sensor grain knows only its Device; calling back would deadlock. A moved Node's open Alert follows its Device to the new Lot until three Readings recover; closing on unassign belongs to Epic 8.

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror` -- expected: success
- `dotnet format --verify-no-changes --no-restore` -- expected: no changes
- `dotnet test --project tests/cs/server.tests --no-build` -- expected: pass
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- expected: pass (known host flakes: `KeycloakReconciliationTests.ARenameInKeycloakRenamesTheSite`, three `NodeMoveTests` reminder timeouts)

## Auto Run Result

Status: blocked

Blocking condition: intent gap

**What happened:** This was a follow-up review of the merged story (PR #67, `5be1e97`). No code was changed. The review found one defect that the intent contract does not settle, so the pass stops here for a decision.

**The defect:** A Node without synced time sends Readings marked unsynced. The Server gives such a Reading the time `receivedAt - age` (`apps/cs/server/Devices/NodeFrameReader.cs` l.144-153), so the same Reading gets a later `measured_at` each time its frame is sent again. The Device grain evaluates a duplicate frame like a first delivery, and the Sensor grain's guard (`measured_at` not newer than `lastEvaluatedAt`) lets it through. Each resend then adds one to the streak: one dry Reading sent three times opens an Alert, and one in-range Reading sent three times closes one. Resends happen when an acknowledgement is lost and when a frame is answered `retry`, which this story added. A Node is unsynced until its first acknowledgement after a boot, which is also when acknowledgements are most likely to be missing. Found by reading the code; no test was run for it.

**Why it needs a decision:** The contract says "a Reading whose `measured_at` is not newer than `lastEvaluatedAt` is not evaluated, so a resent frame changes nothing" and "a frame is evaluated on first delivery and on a duplicate resend alike". For an unsynced Reading both cannot hold with `measured_at` as the guard. The guard comes from AD-7.

**Questions:**
1. What identifies a Reading that was already evaluated when its `measured_at` is not stable? Options seen:
   - Guard on `reading_seq` as well (or instead). Exact, but `EvaluateReading`, the Sensor's evaluation events and `SensorState` gain a field, and AD-7 changes.
   - Do not evaluate the Reading of a duplicate unsynced frame; only deliver what is pending. Small, but a Reading whose first evaluation failed is then never evaluated.
   - Evaluate a duplicate with the `measured_at` stored at first delivery. Keeps the contract, but the Device grain needs the stored time back from the commit.
2. Should an unsynced Reading count toward a streak at all?

**Review findings:** 51 findings (high 0, medium 15, low 22, false 13, maybe-false 1). 27 repeat rows of the first pass and keep their verdict and route.
- Intent gap: 1 entry (2 rows), above.
- Patch, not applied (moot under the intent gap, still open): 5 entries (7 rows), all tests or wording:
  - an older Reading after steady Readings (the in-memory half of the "not newer" guard);
  - the Sensor's own redelivery by timer and by the `deliver-alerts` reminder, which no test reaches (the first pass took the timer as tested);
  - `Evaluate` throwing and the frame answering `retry`;
  - a failed delivery of a close;
  - the README and migration summary wording about which tables the migration empties.
- Deferred: nothing new. 10 rows repeat entries already in `deferred`.
- Rejected: 32 rows, each with its reason in the Review Triage Log.

**Follow-up review recommended:** unchanged (`true`). Nothing was patched in this pass. The story needs another review after the decision is built.

**Verification:** none run; the tree's code is identical to `5be1e97`. The diff reviewed is `git diff c928cb1a6e0b2c4ea17328044e4577f39e26c884` without `_bmad-output/`.

**Residual risks:**
- The defect above is live on `main`.
- After a reactivation up to three stale backlog Readings can count, not one as the README says (rejected as low: it needs an out-of-order backlog right after a reactivation).
- A low Alert stays open while Readings alternate between within and above the high.
- The risks listed by the first pass still hold.
