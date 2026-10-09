---
title: 'Story 6.1: Threshold Alerts open and close'
type: 'feature'
created: '2026-10-08'
baseline_revision: af331ad9ecf77473ec9ef633bdbe2d9aeacaf09c
status: done
review_loop_iteration: 0
followup_review_recommended: false
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
- Monotonic: a Reading whose `measured_at` is not newer than `lastEvaluatedAt` is not evaluated, so a resent frame changes nothing. A frame is evaluated on first delivery and on a duplicate resend alike, and a failed evaluation answers the frame `retry`. A Reading is evaluated with the `measured_at` stored with its key at first delivery, never with the time of the frame that carries it again: a Node without synced time has `measured_at = receivedAt - age`, which every resend would move later, so the store keeps the time with the Reading key and a duplicate frame is evaluated with the stored one. Unsynced Readings count toward streaks like any other.
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
| Unsynced resend | Node without synced time; one dry Reading sent three times (lost acknowledgements, or answered `retry`) | counted once: streak 1, no Alert (a Reading in range, sent three times, closes nothing); each resend is evaluated with the `measured_at` stored at first delivery, so the Sensor's guard finds it not newer | — |
| Unsynced, first evaluation failed | Evaluate throws after the frame committed; frame answered `retry` and sent again | the resend is a duplicate and evaluates the Reading exactly once, with the stored `measured_at` | frame answered `retry` until it succeeds |
| Not eligible | uncalibrated, no low, undeclared slot, unassigned Node | stored, no Alert | — |
| Reset | streak 2, then Thresholds changed or epoch changed | streak 0; three more needed | — |
| Needs water | open low Alert, soil-moisture Sensor | Lot status `needsWater`, first in the list; back to `ok` on close | high side or other quantity: unchanged |
| Restart | streak 2 or open Alert, silo restarts | next Reading continues; Site still lists the Alert | — |

</intent-contract>

## Code Map

- `apps/cs/server/Devices/DeviceGrain.cs` -- `Ingest` l.474-632: pause gate l.554, `CommitAsync` l.558, declaration l.584-606. Add the per-Sensor evaluate call after the commit, also when `NewKeys == 0`; gate on `State.LotId`, `State.Sensors`. `EvaluateAsync` l.691 passes each Sensor's stored `measured_at` from `FrameCommitted`, not `rows.MeasuredAt`. `Move` l.178, `Unassign` l.234. `DeviceState` (`SiteId` Id 0, `LotId` Id 6): add the derived epoch in the existing `Apply` methods.
- `apps/cs/server/Devices/DeviceIngestionStore.cs` -- `ReadingWrite(SensorId, ReadingSeq, Slot, Quantity, RawValue, CalibrationId)` l.22, `FrameRows(MeasuredAt, …)` l.42: the data to pass on. `reading_keys` (l.93, l.112) gains a nullable `measured_at`, written with a new key; `FrameCommitted` (l.65) returns, per `(sensor_id, reading_seq)` of the frame, the stored time: the frame's own for a new key, the stored one for a duplicate, and the frame's own for a key stored before the migration (null).
- `packages/cs/contracts/Sensors/{SensorGrains,SensorEvents}.cs` -- `ISensorGrain` l.9; add `Evaluate` and its context/result records; `SensorThresholdsChanged` l.45 is the pattern for the new evaluation events.
- `apps/cs/server/Sensors/SensorGrain.cs` -- `JournaledStreamGrain<SensorState>, IRemindable`; `SetThresholds` l.106; `Calibrate`/`DeliverAsync` l.185-284 is the persist, call, acknowledge, retry model; never call back into the Device grain from `Evaluate` (non-reentrant).
- `apps/cs/server/Sensors/SensorState.cs` -- Ids 0-8 used, next `Id(9)`; `EffectiveLow`/`EffectiveHigh`/`Calibrated` l.88-93; `Apply(SensorThresholdsChanged)` l.111 resets streaks. `CalibrationMath.cs`, `ThresholdRules.cs` for conversion and sides; put the streak rule in a pure static class beside them.
- `packages/cs/contracts/Alerts/`, `apps/cs/server/Alerts/` (new) -- `IAlertGrain`, `AlertState`, events `alert.opened`/`alert.closed`, `[GrainType("alert")]`, stream `alert/{id}`, `AlertsHostingExtensions` wired in `Program.cs`.
- `packages/cs/crypto/SensorIds.cs` -- reuse `UuidV5(Guid, string)`; the namespace constant lives in the Server.
- `packages/cs/contracts/Sites/SiteGrains.cs` l.38, `apps/cs/server/Identity/{SiteGrain,SiteState}.cs` (Ids 0-7 used) -- add `AlertOpened`/`AlertClosed`/`OpenAlerts`, events `site.alert-opened`/`site.alert-closed`; `CreateLot` l.170-210 is the requested/completed precedent. The Site grain never calls User grains.
- `apps/cs/server/Lots/LotsProjector.cs` -- `InputsSql` l.125-140, `InputsOf` l.181-187 (`OpenLowAlert: false`), `ApplyAsync` l.190 (add an `alert/` branch), `EvaluateAsync` l.368. `LotStatusRule.cs` l.111 and `LotsReadModel.StatusOrder` l.51 already handle `needsWater`: read-only.
- `apps/cs/migrations/Migrations/` -- a new migration that adds nullable `measured_at` to `reading_keys` (no backfill; no edit of `M20261006150000CreateTablesReadings`), and new `M<yyyyMMddHHmmss>…` after `M20261007120000CreateTableCalibrations` for the projector's open-alert support table; delete the `lots` rows and checkpoint as `M20261006170000AddLotStatusToLots` did.
- `tests/cs/server.integration/Identity/IdentityCluster.cs` -- fixture, `Time`, `RestartSiloAsync`, fault filters (`SensorFaults` is the model for Alert and Site faults). Model on `Devices/CalibrationGrainTests.cs` (`DeclaredNodeAsync` l.380, `StoreSoilAsync` l.393) and `Devices/ThresholdGrainTests.cs`; `[Collection(IngestSuites.Name)]`. Frames more than 5 min ahead of the fake clock are rejected.
- `tests/cs/server.tests/` -- `Fixtures/journal.json` and `FixtureJournalReplayTests` `States` l.20-28 (every new alias, `["alert"]` state); `Lots/LotStateTests.cs` l.120; `Sensors/{ThresholdRulesTests,SensorStateTests}.cs` as the model for pure rule tests. `tests/cs/server.integration/Edge/LotStatusTests.cs` hand-writes a `needsWater` row.
- `apps/cs/README.md` -- "Sensor Specifications", "Thresholds", "Lot status" say "before Epic 6" / "no producer yet"; add an Alerts section.

## Tasks & Acceptance

**Execution:**
- `tests/cs/server.tests/Sensors/`, `tests/cs/server.integration/Devices/ThresholdAlertGrainTests.cs`, `tests/cs/server.integration/Edge/LotStatusTests.cs` -- write the failing tests first, one per matrix row, including the two unsynced rows (an unsynced frame resent three times through the real ingest path counts once; a duplicate whose first evaluation failed evaluates once with the stored time) -- NFR16
- `packages/cs/contracts/{Sensors,Alerts,Sites}/**` -- grain methods, records, events -- contracts first
- `apps/cs/server/Sensors/**` -- streak rule, state, `Evaluate`, episode, open/close delivery with retry
- `apps/cs/server/Alerts/**`, `apps/cs/server/Program.cs` -- Alert grain, Site reporting with retry, hosting
- `apps/cs/server/Identity/{SiteGrain,SiteState}.cs` -- open-Alert set and `OpenAlerts()`
- `apps/cs/server/Devices/{DeviceGrain,DeviceState}.cs` -- evaluate after commit, epoch, eligibility gates; evaluate each Reading with the stored `measured_at`
- `apps/cs/server/Devices/DeviceIngestionStore.cs`, `apps/cs/migrations/Migrations/**` -- `reading_keys.measured_at`, returned per key by the commit; a duplicate frame carries the stored time -- the unsynced-resend rule
- `apps/cs/server/Lots/LotsProjector.cs`, `apps/cs/migrations/Migrations/**` -- `OpenLowAlert` from Alert events
- `tests/cs/server.tests/Fixtures/journal.json`, `FixtureJournalReplayTests.cs`, `Lots/LotStateTests.cs` -- new aliases and inputs
- `apps/cs/README.md` -- document evaluation, Alerts, the Site set; remove the stale statements

**Acceptance Criteria:**
- Given a Node without synced time, when one Reading is sent three times (the resend gets a later receive time each time), then it counts once, so three resends of one dry Reading open no Alert and three resends of one in-range Reading close none; and when the first evaluation of a frame failed, then the resend evaluates its Reading once, with the `measured_at` stored at first delivery.
- Given a calibrated Sensor with Thresholds on an assigned Node, when three consecutive Readings at 15-minute `measured_at` steps cross a Threshold, then one Alert is open with the UUIDv5 ID and side, the Sensor stream holds the episode before the Alert stream holds `alert.opened`, and `Site.OpenAlerts()` lists it.
- Given an open Alert, when another grain or Sensor calls `Close`, then it is refused and the Alert stays open.
- Given Alert events in the journal, when the `lots` read model is rebuilt from position 0, then every Lot has the same status as before.
- Given the TestCluster suite, when it runs, then it covers exactly-three opening and closing, flapping, a retried evaluation with no duplicate, the low-to-high switch, the out-of-order backlog and the 2-of-3 cases, each written failing first.

## Spec Change Log

- 2026-10-09 -- Resolved after the follow-up review of the merged story (PR #67) found that a resent frame of an unsynced Node moved a Reading's `measured_at` later and so counted again. Decided with the human: a Reading is evaluated with the `measured_at` stored with its key at first delivery (a nullable `reading_keys.measured_at`, new migration), so the monotonic guard, the contract text and AD-7 stay as written; unsynced Readings count toward streaks. Rejected: a `reading_seq` guard (changes `EvaluateReading`, the evaluation events, `SensorState` and AD-7) and skipping the Reading of a duplicate frame (a Reading whose first evaluation failed would never be evaluated). The follow-up work is the stored-time rule on top of the merged story; the other review patches (tests, wording) stay open in the Review Triage Log.

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

### 2026-10-09 — Review pass
- verdicts: 27 findings — high 0, medium 0, low 20, false 7, maybe-false 0
- findings:
  - `[low]` `[reject]` (blind) A key stored before the migration has no time, so an unsynced resend of its frame still counts again; `readings` could backfill it — verified: `MeasuredAtOf` falls back to the frame's time and nothing fills the null. It needs an unsynced frame first delivered before the migration and sent again after it, which is one resend cycle at one deploy. The Code Map asks for exactly this (no backfill, the frame's own time for a null key), and a join on the partitioned `readings` table on every ingest costs more than the case is worth.
  - `[low]` `[reject]` (blind) The fallback is silent: a missing entry for a new key would bring the double count back unseen — no path produces a miss for a key that holds a time, and the unsynced tests fail if one appears; a log or counter guards a state that was not shown.
  - `[low]` `[reject]` (blind) `AReadingWhoseKeyWasStoredWithoutATimeIs…` pins the fallback as wanted — it pins what the Code Map specifies for a pre-migration key; same case as the first row.
  - `[low]` `[reject]` (blind) No test with two declared Sensors where one evaluation fails — both halves are tested on one Sensor (a resend after a successful evaluation, a resend after a failed one), and each Sensor's time is looked up by its own key; a combined test needs a per-Sensor fault hook.
  - `[low]` `[reject]` (blind) No store-level test of `StoredMeasuredAt` (mixed new and known keys, two Readings of one Sensor, an age above 0, a synced resend) — the SQL joins per key and has no branch for any of these; the grain tests read the stored time through the real store.
  - `[low]` `[reject]` (blind) One more query on every ingest — verified: one indexed lookup of the frame's keys, inside the transaction that just wrote them; a Node sends a frame every 15 minutes.
  - `[low]` `[reject]` (blind) The stored time has microsecond precision, the fallback 100 ns — only the null-key fallback passes the frame's own time; the common path reads the time back, which the new precision test pins (see the verification-gap row).
  - `[low]` `[patch]` (blind) README steps 6 and 9 say "for every Reading" and "always" while a key without a time uses the frame's own — both sentences now name the exception.
  - `[false]` `[reject]` (blind) Only the time is taken from the first delivery, not the value — a Reading key is `(sensor, reading_seq)` and `reading_seq` is monotonic and kept across resets (AD-17, epics l.1168), so a frame that carries a key again carries the same Reading and value.
  - `[low]` `[reject]` (blind) `reading_keys` grows by a `timestamptz` per row, device report keys included — 8 bytes per key; retention of the table is unchanged by this story, and writing null for report keys adds a special case for no reader.
  - `[low]` `[reject]` (blind) `FailNextEvaluations` is a process-wide counter with no reset on teardown — test fixture only; it is armed for one call directly before the ingest that consumes it, as the existing `FaultyIngestionStore` counters are.
  - `[false]` `[reject]` (blind) The restart test's longer wait is unrelated to this change — no wrong outcome; the test failed in 2 of 4 runs of the suite during implementation because it asserted before the Sensor had journaled its delivery. Named under Auto Run Result.
  - `[low]` `[reject]` (edge) A key with a null time falls back to the frame's time on every resend — same case as the first blind row; shares its route.
  - `[low]` `[reject]` (edge) A Node whose `reading_seq` was erased sends new Readings under stored keys, which are then evaluated with the old time and never count — `reading_seq` persists across resets (AD-17); such Readings were already not stored before this change (their keys exist), so only an erased flash reaches this, and the guard adds an age parameter the contract does not have.
  - `[low]` `[patch]` (edge) `SensorFaults` decrements and then writes 0, which can erase a count armed meanwhile — replaced by a compare-and-swap loop that only takes a failure that is left.
  - `[low]` `[patch]` (edge) `Assert.Null` on the key's time also passes when no row matches — the test now counts exactly one row with `measured_at IS NULL`.
  - `[low]` `[reject]` (edge) Claim "a duplicate frame carries the stored time" does not hold for pre-migration keys — same case as the first blind row; the Code Map states the exception.
  - `[low]` `[patch]` (verification-gap) No test tells the read-back time from the frame's own for a new key, because the fake clock only yields whole seconds — added `AFirstDeliveryIsEvaluatedWithTheTimeItsKeyHoldsAtTheDatabasesPrecision`: the receive time carries half a microsecond, the first delivery is evaluated with the time the key holds, and a resend adds nothing. The test puts the shared clock back on a whole millisecond. Filed as defer; patched because the test is small.
  - `[low]` `[reject]` (verification-gap, other) `FailNextEvaluations` has no reset — same as the blind row.
  - `[low]` `[reject]` (intent) The fallback contradicts a strict reading of "never" and a test pins it — same case as the first blind row: the intent states the rule for a key that holds a time, and the Code Map settles the key that holds none.
  - `[low]` `[reject]` (intent) The unsynced tests use an age of 0 only — same as the blind store-level row: the stored time is used whatever the frame's own time was computed from; `IngestGrainTests` covers how an unsynced time is computed.
  - `[low]` `[reject]` (intent) A `retry` caused by a failing Alert or Site call is not combined with an unsynced Node — a resend takes the same path whatever made the first answer `retry`; both are tested separately.
  - `[false]` `[reject]` (intent) Device report keys get a time nothing reads — no wrong outcome; one insert shape for both kinds of key.
  - `[false]` `[reject]` (intent) One test edit lies outside the unsynced rule — same as the blind row on the restart test.
  - `[false]` `[reject]` (intent) Most of the matrix is not touched by this diff — those rows shipped with PR #67; their tests are unchanged and ran in this pass's suite.
  - `[false]` `[reject]` (intent) Test-first cannot be seen in the diff — carried: true of any single diff; the implementation run saw three of the new tests fail before the change.
  - `[false]` `[reject]` (intent) The spec file is not in the reviewed diff — by design: the review layers read the code change, and only the edge-case layer reads the spec.

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

Status: done

**Summary:** This run built the stored-time rule on top of the merged story (PR #67). A Reading is now evaluated with the `measured_at` that `reading_keys` keeps from its first delivery. A frame sent again by a Node without synced time gets a later time from the Server, but its Reading is evaluated with the stored one, so the Sensor's guard finds it not newer and it counts once. The Sensor grain, the contracts and the events are unchanged.

**Files changed:**
- `apps/cs/migrations/Migrations/M20261009065300AddMeasuredAtToReadingKeys.cs` -- new migration: nullable `reading_keys.measured_at`, no backfill.
- `apps/cs/server/Devices/DeviceIngestionStore.cs` -- a new key is written with the frame's time; the commit reads the stored times back in the same transaction and returns them per `(sensor_id, reading_seq)`.
- `apps/cs/server/Devices/DeviceGrain.cs` -- `EvaluateAsync` passes each Sensor the stored time, or the frame's own for a key that holds none.
- `tests/cs/server.integration/Devices/ThresholdAlertGrainTests.cs` -- five new tests (below) and a longer wait in the restart test.
- `tests/cs/server.integration/Identity/IdentityCluster.cs` -- `SensorFaults.FailNextEvaluations` makes `Evaluate` throw.
- `apps/cs/README.md` -- ingest steps 6 and 9, the time paragraph and the Alerts section describe the rule.

**New tests:**
- `AnUnsyncedDryReadingSentThreeTimesCountsOnceAndOpensNoAlert` and `AnUnsyncedReadingInRangeSentThreeTimesClosesNoAlert` -- matrix row "Unsynced resend".
- `AnUnsyncedReadingWhoseFirstEvaluationFailedIsEvaluatedOnceWithTheStoredTime` -- matrix row "Unsynced, first evaluation failed". It also covers the open row of the earlier follow-up review "no test makes `Evaluate` throw".
- `AReadingWhoseKeyWasStoredWithoutATimeIsEvaluatedWithTheTimeOfTheFrameThatCarriesIt` -- a key stored before the migration.
- `AFirstDeliveryIsEvaluatedWithTheTimeItsKeyHoldsAtTheDatabasesPrecision` -- added in review.

**Review findings:** 27 findings (high 0, medium 0, low 20, false 7). No intent gap and no spec defect.
- Patched: 4 entries, all low (high 0, medium 0, low 4): the README wording of steps 6 and 9, the race in `SensorFaults`, the `Assert.Null` that could pass on a missing row, and the precision test.
- Deferred: none.
- Rejected: 23 rows, each with its reason in the Review Triage Log of 2026-10-09. The main one: a key stored before the migration holds no time, so an unsynced frame first delivered before the migration and sent again after it still counts once per resend. The Code Map specifies this, and it can only happen around one deploy.

**Change outside the stored-time rule:** `AnUndeliveredOpenIsDeliveredWithNobodyCallingAndAfterASiloRestart` now also waits until the Sensor has journaled its delivery. It failed in 2 of 4 runs of the suite during implementation because it asserted too early. Production code is not involved.

**Follow-up review recommended:** `false`. This pass patched no high entry and no medium entry.

**Verification:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror` -- success, 0 warnings.
- `dotnet format --verify-no-changes --no-restore` -- no changes.
- `dotnet test --project tests/cs/server.tests --no-build` -- 580 passed.
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- 986 passed, 0 failed, 0 skipped, after the review patches. The known host flakes did not show up.
- One run between the patches failed `ThreeConsecutiveReadingsBelowTheLowOpenOneAlertOnceTheEpisodeIsJournaled`: the first version of the precision test left the shared fake clock one microsecond off a whole millisecond. The test now restores a whole millisecond, and the next full run passed.
- Matrix audit: every matrix row has a test in `ThresholdAlertGrainTests`, `ThresholdStreakRuleTests` or `LotStatusTests`, and all of them ran and passed in the full suite.

**Residual risks:**
- A key stored before the migration (see Rejected above).
- The patch rows of the follow-up review of 2026-10-08 are still open, as the Spec Change Log says: a test for an older Reading after steady Readings, tests for the Sensor's redelivery by timer and reminder, a failed delivery of a close, and the README wording about which tables the earlier migration empties.
- The entries in `deferred` are unchanged.
- The precision test moves the shared fake clock by one millisecond in total. If it fails midway it still restores the clock in `finally`.
