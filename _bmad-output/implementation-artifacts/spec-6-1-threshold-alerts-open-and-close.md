---
title: 'Story 6.1: Threshold Alerts open and close'
type: 'feature'
created: '2026-10-08'
baseline_revision: 'c928cb1a6e0b2c4ea17328044e4577f39e26c884'
status: 'done'
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

**Summary:** Threshold Alerts now open and close on the Server. After a frame is committed the Device grain hands each declared Sensor of an assigned, unpaused Node its Reading with the Site, Lot and evaluation epoch. The Sensor grain counts the streak (`ThresholdStreakRule`), journals the episode, and opens or closes the Alert through the new event-sourced Alert grain, redelivering until acknowledged. The Alert grain reports to the Site grain, which journals the set of open Alerts and answers `OpenAlerts()`. The Lots projector reads `alert.opened`/`alert.closed` into `lot_status_alerts`, so a Lot with an open low-side soil-moisture Alert is `needsWater` and sorts first. No REST, OpenAPI or client change.

**Files changed:**
- `packages/cs/contracts/Alerts/{AlertGrains,AlertEvents}.cs` -- `IAlertGrain`, its records, `alert.opened`, `alert.closed`, `alert.site-notified`
- `packages/cs/contracts/Sensors/{SensorGrains,SensorEvents}.cs` -- `Evaluate`, evaluation context and result, streak and episode events
- `packages/cs/contracts/Sites/{SiteGrains,SiteEvents}.cs` -- `AlertOpened`, `AlertClosed`, `OpenAlerts`, `site.alert-opened`, `site.alert-closed`
- `packages/cs/contracts/Devices/DeviceGrains.cs` -- `SetCalibration` is `[AlwaysInterleave]` (Device and Sensor now call each other)
- `apps/cs/server/Sensors/{SensorGrain,SensorState,ThresholdStreakRule}.cs` -- evaluation, streak rule, episode, delivery with retry
- `apps/cs/server/Alerts/{AlertGrain,AlertState,AlertIds,AlertsHostingExtensions}.cs`, `apps/cs/server/Program.cs` -- the Alert grain, the UUIDv5 namespace, hosting
- `apps/cs/server/Identity/{SiteGrain,SiteState}.cs` -- the Site's open-Alert set
- `apps/cs/server/Devices/{DeviceGrain,DeviceState}.cs` -- evaluate after commit, derived epoch, `retry` on failure
- `apps/cs/server/Lots/LotsProjector.cs`, `apps/cs/migrations/Migrations/M20261008120000CreateTableLotStatusAlerts.cs` -- `OpenLowAlert` from Alert events; the migration also rebuilds the lots read model and its support tables
- `tests/cs/server.integration/Devices/ThresholdAlertGrainTests.cs` (22 tests), `Edge/LotStatusTests.cs`, `Identity/IdentityCluster.cs` (Alert and Site fault filters)
- `tests/cs/server.tests/**` -- streak rule, Sensor, Alert, Site and Device state tests, fixture journal rows, replay map
- `apps/cs/README.md` -- Alerts section; stale "before Epic 6" statements removed
- `_bmad-output/implementation-artifacts/epic-6-context.md` -- compiled Epic 6 context

**Review findings:** 53 findings (high 0, medium 9, low 28, false 12, maybe-false 4).
- Patched: 9 rows (medium 3, low 6). The three medium ones are missing tests, now added: a new epoch at a streak of one, an open and a close of one Alert pending together, a paused Node. The low ones: streak reset on `sensor.calibrated` and `sensor.specification-changed`, `lastEvaluatedAt` advanced for a not-eligible Reading, no second delivery attempt after a failed one, grain calls built inside the `try`, and tests for the rounded percentage and a Site that is not Active.
- Deferred: 7 entries in frontmatter `deferred` (13 rows).
- Rejected: 31 rows, each with its reason in the Review Triage Log.

**Follow-up review recommended: true.** Three medium entries were patched (all tests). The unverified risk a second pass should look at is `[AlwaysInterleave]` on `IDeviceGrain.SetCalibration`: it is needed to avoid a Device/Sensor wait cycle, and no test runs a Calibration delivery during an ingest.

**Verification:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror` -- success, 0 warnings
- `dotnet format --verify-no-changes --no-restore` -- no changes
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- 981 passed, 0 failed, 0 skipped (after the review patches; 975 before them)
- Matrix audit: every row has a passing test in `ThresholdAlertGrainTests`, `ThresholdStreakRuleTests` or `LotStatusTests`.
- The Verification command for the unit project needed `--project`; corrected above.

**Residual risks:**
- An Alert stays open when its Sensor stops being eligible (alerts turned off, Sensor dropped from the Specification set) and keeps naming the old Lot after a move. These need the non-recovery close reasons of AD-7/AD-8 and a decision for cleared Thresholds; all three are in `deferred`.
- While an open or close is undelivered (Alert or Site grain unreachable) every frame of that Node answers `retry`; Readings are stored, but the Node keeps resending.
- The migration empties `lots`, `lot_status_devices`, `lot_status_sensors` and `calibrations`; they rebuild from the journal when the Server starts, and the Garden is empty until the projector has caught up.
- `lastEvaluatedAt` is journaled only with streak changes, so after a reactivation one stale Reading can start a streak of 1.
