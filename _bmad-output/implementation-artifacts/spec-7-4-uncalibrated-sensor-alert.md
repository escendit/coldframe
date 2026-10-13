---
title: 'Story 7.4: Uncalibrated Sensor Alert'
type: 'feature'
created: '2026-10-13'
baseline_revision: 725edaf5bd6dd828bc7669ea2db9c4050ac76537
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-7-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-7-3-low-battery-alert.md'
  - '{project-root}/apps/cs/README.md'
warnings: ['oversized']
deferred:
  - summary: >-
      A Sensor that a new Specification set drops from its Node is never told anything again, so its open
      uncalibrated Alert (and an open Threshold Alert) stays open with its daily Reminder.
    evidence: |-
      DeviceGrain.DeclareAsync replaces State.Sensors with the new set (DeviceState.Apply of
      DeviceSpecificationsDeclared), and Sensor IDs derive from Device, slot and quantity. PushContextsAsync
      iterates State.Sensors only, so the dropped Sensor grain hears of no later unassignment, move or Pause and
      gets no Reading. The orphaning predates this story (Story 4.6); the uncalibrated Alert is one more thing
      it leaves behind. Close reason "removed" exists for it. It needs a changed Specification set, which V1
      firmware does not produce.
    location: >-
      apps/cs/server/Devices/DeviceGrain.cs (DeclareAsync, PushContextsAsync)
    severity: medium
  - summary: >-
      The uncalibrated Alert notifies at once: during onboarding, before the Owner had a chance to calibrate, and
      for every never-calibrated Sensor on the first report after the upgrade.
    evidence: |-
      The Alert opens with the frame that declares the Sensor of an assigned Node, as FR21 and the story say
      ("opens when a Node is assigned to a Lot and one of its calibration: true Sensors has no Calibration"), and
      is delivered like any Alert. MorningNudgeJourneyTests shows it opening and closing during setup. A grace
      period or a quiet first hour would be a new rule on FR21: a product decision.
    location: >-
      apps/cs/server/Sensors/UncalibratedRule.cs
    severity: medium
  - summary: >-
      After a silo restart the reminder table can stall for 30 s; the integration fixture now waits for it, and
      whether the production reminder store shows the same stall is not known.
    evidence: |-
      IdentityCluster.RestartSiloAsync reads the reminder table once after a restart (ReminderTableReadyAsync):
      without it a pause test timed out in 3 of 11 class runs, with "Grain placement operation timed out" and
      "ReminderService failed initial load of reminders" in the silo log. A User grain registers a reminder in
      the turn that tells it of an Alert, which a Device's wake or frame awaits. The TestCluster uses the
      in-memory table; reading the production silo's reminder configuration and a restart under load would
      settle it. Probably the cause of the known NodeMoveTests flake.
    location: >-
      tests/cs/server.integration/Identity/IdentityCluster.cs (ReminderTableReadyAsync)
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** A Node assigned to a Lot whose soil Sensor was never calibrated shows the Lot as *needs calibration*, but nobody is told: no Alert opens, so the step is easy to forget. The three clients already render an `uncalibrated` Health row and the push route already leads to Lot detail, but nothing on the Server produces the kind, and its push text is the neutral "Peppers needs attention".

**Approach:** The Device grain sends each of its Sensor grains an evaluation context (Site, Lot or none, paused, epoch) whenever assignment or Pause changes and when a Sensor is first declared. The Sensor grain journals the context and opens a Health Alert of kind `uncalibrated` while it is assigned, not paused, requires calibration and has none; it closes that Alert when a Calibration is saved or the context ends, and closes its Threshold Alert on an unassigned or paused context (AD-8). Push copy gets the UX wording; contract descriptions, client tests and docs catch up.

## Boundaries & Constraints

**Always:**
- A Sensor **needs calibration** when its Specification says `calibration: true` and it has no Calibration in force. The `uncalibrated` Alert is open exactly while the Sensor needs calibration and its newest context is assigned and not paused.
- The Device grain sends the context after an assignment at enrolment, a move, an unassignment, a Pause change, and after a frame declares or changes a Sensor's Specification. A context that did not reach a Sensor is sent again on the Device's next report, wake or activation until confirmed; Enrol, Move and Unassign answer as they do today even when the push fails. Readings keep carrying the epoch: an evaluation with a newer epoch than the Sensor holds is taken as an assigned, unpaused context for that Lot.
- The Sensor ignores a context whose epoch is not newer than the one it holds. The held context, the `uncalibrated` episode counter and the open Alert are journaled Sensor state; the episode is journaled before the Alert grain is called. Alert ID = UUIDv5 of `sensor:{sensorId}:uncalibrated:{episode}` in the existing namespace. At most one open `uncalibrated` Alert per Sensor; only the Sensor grain closes it.
- Close reasons: `calibrated` when a Calibration is saved; `unassigned` when the context has no Lot or names another Lot (a move then opens a new episode for the new Lot); `paused` when the context is paused; `removed` when a Specification change stops requiring calibration.
- An unassigned or paused context also closes the Sensor's open Threshold Alert with reason `unassigned` or `paused` and resets its streak.
- The Alert carries Site, Lot, Sensor ID, quantity, Device ID and Device kind `node` on the existing nullable members. Opens and closes go through the Sensor grain's existing journaled, ordered, retried Alert delivery.
- Every contract change is additive (`packages/proto/check-compat.sh` and the OpenAPI compatibility check pass); new journaled members are nullable or defaulted so journals written before this story replay.
- Health rules apply unchanged: Site fan-out, windows, summaries, mute, and a Reminder at most once per 24 h.

**Never:**
- No Threshold evaluation for a Sensor that needs calibration (already so; keep it pinned by a test).
- No change to Lot status rules: `needsCalibration` stays computed from the Sensor's state, not from Alerts.
- No new property on `HealthAlert`, no change to the push route or the AsyncAPI schemas, no migration.
- No change to the row title the clients ship ("Soil Sensor on Lot '…' needs Calibration", glossary-capitalised and pinned by parity tests) and no body on Alert rows.
- No grain method that raises a Pause (Epic 8); no change to what a move does to an open Threshold Alert (DW-85); no call from the Sensor grain back into a non-interleaving Device method; no wall clock.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Open on assignment | Node enrolled on Lot "Peppers"; its first frame declares a `calibration: true` soil Sensor | One open `uncalibrated` Alert, episode 1, with Site, Lot, Sensor ID, Device ID; Site and members told; no Alert for the Node's other Sensors | Undelivered open retried by the Sensor's delivery timer and reminder |
| Assigned later | Node declared while on no Lot, then moved onto a Lot | No Alert before; one after the move | A failed context push is sent again on the next report, wake or activation |
| Close on calibration | Open Alert; dry and wet points saved | Closed `calibrated`; a single point leaves it open; saving the same pair again changes nothing | Close retried the same way |
| Close on unassignment | Open Alert; Node unassigned | Closed `unassigned`; no new Alert while on no Lot | — |
| Move | Open Alert on Lot A; Node moved to Lot B | Alert for A closed `unassigned`; new Alert, episode 2, naming B | — |
| Assigned again | Unassigned (closed), then assigned, still uncalibrated | New Alert, next episode, another ID | — |
| Already calibrated | Calibrated Sensor assigned or moved | No Alert | — |
| Paused | Open Alert; `device.paused` then `device.resumed` appended to the Device's journal | Closed `paused`; none opens while paused; a new episode opens on resume | — |
| Stale context | A context with an epoch not newer than the held one | Ignored, nothing journaled | — |
| Specification change | `calibration` turns true on an assigned Sensor / false with an open Alert | Alert opens / closes `removed` | — |
| Threshold Alert, unassigned | Calibrated Sensor with an open Threshold Alert; Node unassigned | Threshold Alert closed `unassigned`; streak reset | — |
| No Threshold while uncalibrated | Uncalibrated Sensor, three Readings below its default Threshold | No Threshold Alert; the `uncalibrated` Alert stays the only one | — |
| Alerts list | Open Alert on Lot "Peppers" | `healthAlerts` item `kind` `uncalibrated`, `deviceKind` `node`, `lotId`, `lotName`; counted in `openCount`; after closing: `closedAt` and `reason` | — |
| Push, single | Same Alert | Title "Soil sensor on Lot 'Peppers' needs calibration", body "An Owner or Administrator can calibrate it.", route to Lot detail of that Lot | Lot name unknown: today's neutral text |
| Reminder | A day later, still open | Same title; body "Still not calibrated. An Owner or Administrator can calibrate it."; not before 24 h | — |
| Summary | Morning summary containing it | Line "Peppers: soil sensor needs calibration"; counted under "to check" | — |

</intent-contract>

## Code Map

Server (`apps/cs/server`, contracts `packages/cs/contracts`)
- `Sensors/SensorGrain.cs` -- `Evaluate` (:154-239; `EvaluationContext(SiteId, LotId, Epoch)` per Reading, throws on a blank Site or Lot :158; eligibility gate :166-178 already skips an uncalibrated Sensor); `Calibrate` (:242-327; `SensorCalibrated` raised :300, early return for a repeated pair :293-296 must also re-deliver); `DeliverAlertsAsync` (:367-415) with timer and reminder `deliver-alerts` (:417, :440) -- reuse for the new kind, do not fork.
- `Sensors/SensorState.cs` -- next free `[Id(16)]`; `PendingAlertDelivery` (:33-41, `Side` non-nullable, no kind, next `[Id(8)]`); Threshold episode `Apply` overloads (:226-279) are the pattern. The state holds no Site, Lot, epoch-of-context or assigned flag today.
- `packages/cs/contracts/Sensors/SensorGrains.cs` (`ISensorGrain` :11, `EvaluationContext` :110, next Id 3), `SensorEvents.cs` -- new context method, record and events go here with stable aliases.
- `Devices/DeviceGrain.cs` -- context hooks: `Enrol` after :194 (no Sensors yet), `Move` after :269, `Unassign` after :295, `Ingest` after `DeclareAsync` :703 (`DeclareAsync` :1162-1182; Sensors first exist here); `EvaluateAsync` :1071-1109 returns early on no Lot; evaluation skipped while paused :720. `Devices/DeviceState.cs` -- `EvaluationEpoch` `[Id(13)]` bumped by assigned/moved/unassigned/paused/resumed (:267-323); next free `[Id(26)]`. No production code raises `DevicePaused`; tests append it (`SilentDeviceAlertGrainTests.cs:571-593`).
- `packages/cs/contracts/Devices/DeviceGrains.cs:111` -- `SetCalibration` is `[AlwaysInterleave]` because `Ingest` awaits `Sensor.Evaluate` (DW-86); the new Device-to-Sensor call relies on the same.
- `packages/cs/contracts/Alerts/AlertGrains.cs` -- `AlertKind` (:60-76, next value 3), `AlertCloseReason` (:101-127, all five exist and are named), `OpenAlert` (:165, next `[Id(8)]`), `OpenDeviceAlert` (:198). `Alerts/AlertGrain.cs` -- `Open` hardcodes Threshold (:96-105), `OpenForDevice` accepts Silent and Battery (:132-137), `Close` allows the Sensor grain when `State.SensorId` is set (:183-185); `AlertIds.cs` (names `sensor:{id:D}:threshold:{n}`), `AlertNames.cs:16-22` (throws on an unnamed kind; `AlertsProjector.cs:81` calls it), `Lots/LotsProjector.cs:508` `KindName`.
- `Alerts/AlertsReadModel.cs` -- `HealthSql` (:130-143) lists every non-threshold kind but needs a Device kind (:139): open with `DeviceKind.Node`. `alerts.sensor_id`, `lot_id`, `quantity` are nullable; `kind` has no CHECK.
- `Notifications/ReminderIntervalRule.cs:39` -- Health is `kind != Threshold`; the 24 h floor applies by itself.
- `Notifications/Push/PushText.cs` (`Title` :94-140, `Body` :152-201, `SummaryBody` :230-249, `IsBattery`/`BatteryTitle` :323-349 as the pattern), `PushText.en.json` (no uncalibrated keys), `PushContent.cs:143-176` (Lot name is looked up for any entry with a Lot).
- Tests: `tests/cs/server.integration/Devices/ThresholdAlertGrainTests.cs` (helpers `GardenAsync` :786, `CalibratedGardenAsync` :809, `SensorStateAsync` :917, `RestartAsync` :966), `CalibrationGrainTests.cs` (:352, `DeclaredNodeAsync` :382), `BatteryAlertGrainTests.cs` (:518 who may open and close, :578 read models, :637 delivery and Reminder), `Edge/AlertsListTests.cs:385`, `Identity/IdentityCluster.cs` (`SensorFaults` :458 matches by method name). Guards: `tests/cs/server.tests/Fixtures/journal.json` + `Journal/FixtureJournalReplayTests.cs` (:306 a row per alias; hard counts at :221, :278), `Alerts/AlertsReadModelUnitTests.cs:61`, `Notifications/PushTextTests.cs:465-560` (:212 pins the neutral text), `ReminderIntervalRuleTests.cs:40`.
- **Existing tests that change meaning:** every simulated Node on a Lot declares an uncalibrated soil Sensor (`tests/cs/device-simulator/SimulatedDevice.cs:80`), so it now gets a Health Alert: `ThresholdAlertGrainTests.cs:290-337, :389`, the `OpenAlerts`/count assertions in `BatteryAlertGrainTests` and `SilentDeviceAlertGrainTests`, and the journeys `tests/cs/web.e2e/SilentNodeJourneyTests.cs` ("1 to check", one notification), `LowBatteryJourneyTests.cs` (one Health row), `MorningNudgeJourneyTests.cs` (one notification).
- Docs: `apps/cs/README.md` :515-530, :568-597, :806-843 (:823 "the Server produces…"), :983, :1060, :1085-1108, :1266; `packages/cs/README.md:86-143`.

Contract and clients (already render the kind; tests and wording only)
- `packages/openapi/coldframe.openapi.json` -- `AlertKind` description :1596 ("uncalibrated arrives later"), `listAlerts` :663, `HealthAlert` :1666-1703 (device-centric description :1668, examples :1699); `packages/openapi/README.md:135-142`; regenerate `packages/ts/api-client/src/schema.ts` (`pnpm --filter @coldframe/api-client generate`).
- Web: `apps/ts/web/src/lib/alerts.ts:181-187` (tools icon, `/garden/{lotId}`), `lib/i18n/en.json:41`; Calibrate gating `routes/(app)/garden/[lotId]/+page.svelte:34-56,155`. Tests `tests/ts/web/alerts.test.ts:482`; `tests/ts/web.e2e/specs/battery.spec.ts` is the template (own Site ID, `healthAlert()` factory, per-theme screenshot, click-through); `apps/ts/web/README.md:294-309`.
- Kotlin: `packages/kt/core/.../alerts/Alerts.kt:284-318`; `tests/kt/core/commonTest/.../alerts/AlertsEngineTest.kt` (:226, :270, :715 feed the kind through the Threshold list; `AlertTitleKind.Uncalibrated` is asserted nowhere; snapshot tests :523, :953); `tests/kt/android/test/.../AlertFixtures.kt:60` (Threshold-shaped), `CoverageTest.kt:7`; READMEs `apps/kt/README.md:89`, `packages/kt/core/README.md:279`.
- iOS: `tests/swift/ios/ColdframeIOSTests/CatalogueTests.swift:103-114` (`coveredUxDrs` ends at 7.3); `apps/swift/README.md:97`.

## Tasks & Acceptance

**Execution:**
- [x] `tests/cs/server.tests/Sensors/SensorStateContextTests.cs` (new), `tests/cs/server.integration/Devices/UncalibratedAlertGrainTests.cs` (new) -- failing first: every server row of the matrix on the TestCluster with the fake clock, plus restart mid-episode, a failed context push that is sent again, a failed Alert report that is retried, and "only the Sensor grain closes it".
- [x] `packages/cs/contracts/Alerts/AlertGrains.cs`, `apps/cs/server/Alerts/AlertGrain.cs`, `AlertIds.cs`, `AlertNames.cs`, `apps/cs/server/Lots/LotsProjector.cs` -- `AlertKind.Uncalibrated`, name `uncalibrated`, the ID helper, and an open for a Sensor-owned Health Alert that journals the Sensor ID.
- [x] `packages/cs/contracts/Sensors/SensorGrains.cs`, `SensorEvents.cs` -- the context method and record (Site, Lot or none, paused, epoch) and the events for a context change and for the start and end of an uncalibrated episode.
- [x] `apps/cs/server/Sensors/SensorState.cs`, `SensorGrain.cs` -- hold the context; decide open and close as a pure rule on context, Specification and Calibration; evaluate it on a context, a declaration or Specification change, and a saved Calibration; close the Threshold Alert on an unassigned or paused context; deliver through `DeliverAlertsAsync`.
- [x] `packages/cs/contracts/Devices/DeviceEvents.cs` (if a marker is journaled), `apps/cs/server/Devices/DeviceState.cs`, `DeviceGrain.cs` -- push the context at the hooks of the Code Map and send it again until each Sensor confirmed it.
- [x] `apps/cs/server/Notifications/Push/PushText.cs`, `PushText.en.json` -- the title, body, Reminder body and summary line of the matrix.
- [x] `tests/cs/server.tests/**`, `tests/cs/server.integration/**`, `tests/cs/web.e2e/*JourneyTests.cs` -- journal fixture rows for the new aliases, kind-name and Reminder guards, `PushTextTests` per copy row, the Alerts list row; adjust the existing tests listed in the Code Map to the Alert that now exists, without weakening what they pin.
- [x] `tests/cs/web.e2e/UncalibratedSensorJourneyTests.cs` (new) -- real Server and web app: a Node on "Peppers" reports; the Owner sees the Health row with the tools icon, follows it to Lot detail, finds Calibrate; after the calibration call the row is closed.
- [x] `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md`, `packages/ts/api-client/src/schema.ts` -- descriptions say the Server produces `uncalibrated` and that a Health Alert may concern a Node's Sensor; an `uncalibrated` example; regenerate the client.
- [x] `tests/ts/web/alerts.test.ts`, `tests/ts/web.e2e/specs/uncalibrated.spec.ts` (new, with baselines) -- open and closed rows from `healthAlerts`; light and dark screenshots; click-through to Lot detail where an Administrator sees Calibrate and a Member does not.
- [x] `tests/kt/core/commonTest/**/alerts/AlertsEngineTest.kt`, `tests/kt/android/test/**/AlertFixtures.kt`, `CoverageTest.kt`, `tests/swift/ios/ColdframeIOSTests/CatalogueTests.swift` -- the kind through `healthAlerts` (title kind, icon, Lot target, closed, snapshot lists); a Health-shaped Android fixture; coverage registers name Story 7.4.
- [x] `apps/cs/README.md`, `packages/cs/README.md`, `apps/ts/web/README.md`, `apps/kt/README.md`, `apps/swift/README.md`, `packages/kt/core/README.md` -- the evaluation context, the uncalibrated rule and close reasons; remove "no producer before Story 7.4".
- [x] `_bmad-output/implementation-artifacts/deferred-work.md` -- narrow DW-85 to what remains (a moved Node's open Threshold Alert).

**Acceptance Criteria:**
- Given an Owner's Site with a Node newly assigned to Lot "Peppers", when the Node's first report is accepted, then the web Alerts page shows a Health row with the tools icon that leads to Lot detail of "Peppers", where Calibrate is offered to an Owner and an Administrator and not to a Member.
- Given that row, when the Calibration is saved, then the Lot leaves *needs calibration* and the Alert is listed as closed.
- Given a member tracking the Alert, when it is delivered as push or browser notification, then title and body are the UX wording and the route opens Lot detail.
- Given journals written before this story (the fixture), when they are replayed, then every row applies and Sensors without a journaled context open no Alert until a context arrives.
- Given the working tree, when the compatibility checks, the Kotlin `OpenApiContractTest` and the TypeScript staleness test run, then all pass.

## Spec Change Log

## Review Triage Log

### 2026-10-13 — Review pass
- verdicts: 33 findings — high 0, medium 7, low 17, false 8, maybe-false 1
- findings:
  - `[medium]` `[defer]` Blind: a Sensor dropped from a Node's Specification set keeps its uncalibrated Alert open forever — verified: `DeclareAsync` replaces the Sensor list and `PushContextsAsync` reads only that list. The orphaning predates this story (it leaves a Threshold Alert the same way); deferred.
  - `[low]` `[reject]` Blind: the rule is not limited to soil moisture while the copy says "Soil sensor" — only soil-moisture Specifications carry `calibration: true` (Design Notes); a guard would cover a state no firmware produces.
  - `[low]` `[reject]` Blind: a Node with two calibrating Sensors gets two rows that read the same — the README already says "a Sensor at most one"; a V1 Node has one soil probe, and telling them apart needs a new `HealthAlert` property, which the intent excludes.
  - `[low]` `[reject]` Blind: Move, Unassign, Enrol and Ingest wait for the Alert fan-out — true, as Ingest already did for Threshold and battery Alerts; it only shows when that chain stalls, and a bound or a deferred delivery adds a second delivery path.
  - `[false]` `[reject]` Blind: `NotNewer` counts as confirmed when the Sensor's epoch is ahead of the Device's — the epoch is derived from the Device's own journal and only grows; no path makes it fall behind.
  - `[low]` `[reject]` Blind: a lost unassign context followed within a minute by a new assignment skips the intermediate state — needs a failed push and a reassignment before the next wake; the Alert that carries on is still true of the Sensor.
  - `[medium]` `[patch]` Blind: the resend on activation has no test — same gap as the verification-gap row below; test added.
  - `[low]` `[reject]` Blind: every Device activation calls all its Sensors, and every Sensor journals each context — a handful of calls per Node per activation and one event per assignment change; a journaled marker on the Device would add an event per Sensor instead.
  - `[medium]` `[defer]` Blind: no grace period: the Alert notifies during onboarding and for every uncalibrated Sensor after the upgrade — the opening moment is FR21's and the story's; deferred for a product decision.
  - `[low]` `[reject]` Blind: Threshold and uncalibrated deliveries share one ordered queue that stops at the first failure — the queue is Story 6.1's; a delivery that fails forever needs a refused ID, which the Sensor cannot produce, and a transient failure delays the other kind by one timer tick.
  - `[low]` `[reject]` Blind: the battery and silent suites leave the new kind out through helpers, and a journey counts relative to a baseline — they pin what they pinned, for their kinds; the uncalibrated Alert of the same Nodes is pinned in its own class, and exact sets would repeat that in two suites.
  - `[maybe-false]` `[defer]` Blind: `ReminderTableReadyAsync` works around a reminder-table stall that production may share — the stall is the in-memory table's after a restart and predates this story; whether the production store shows it needs a restart test against it. Deferred, unverified.
  - `[false]` `[reject]` Blind: `OpenForSensor` does not check its caller — like `Open` and `OpenForDevice`: the ID is derived from the Sensor and episode and checked, grain interfaces are reachable only inside the silo, and no other caller exists.
  - `[medium]` `[patch]` Blind: no test for a close the Alert grain did not take — verified (only a failed open and a failed Site report were tested); `ACloseTheAlertGrainDidNotTakeLeavesTheCalibrationAnswerAsItIsAndIsDeliveredAgainByTheSensor` added.
  - `[low]` `[patch]` Blind: artifacts — the two `uncalibrated` examples of `HealthAlert` read as two episodes at once, and two doc-comment lines overran their wrap: the closed example is now its own Node and Lot, the client is regenerated, the lines are rewrapped. The spec's unticked boxes and empty logs are this file's bookkeeping, written at finalization.
  - `[medium]` `[defer]` Edge: a dropped or renamed Sensor gets no further context — same defect as the first row; one deferred entry.
  - `[low]` `[reject]` Edge: while the report of an uncalibrated Alert fails, every frame of the Node is answered Retry — that is Story 6.1's rule for any undelivered Alert of the Sensor, and `AReportTheSiteDidNotTakeAnswersRetryAndTheResentFrameCompletesWithOneAlert` pins it; the Readings are stored either way.
  - `[low]` `[reject]` Edge: a stuck delivery of one kind delays the other — same as the Blind queue row.
  - `[low]` `[reject]` Edge: the push names a "Soil sensor" whatever the quantity — same as the Blind soil row.
  - `[low]` `[reject]` Edge: a slow `SetContext` delays Move, Unassign, Enrol and Ingest — same as the Blind fan-out row.
  - `[false]` `[reject]` Edge: `NotNewer` from a Sensor ahead of a restored Device journal — same refutation as the Blind row: no path restores a Device journal behind its Sensors.
  - `[low]` `[reject]` Edge: a Pause change reaches the Sensors only with the next wake or frame — no grain method raises a Pause before Epic 8, which the intent excludes; the method that will raise it calls the push as Move does.
  - `[medium]` `[patch]` Gap: assigning a declared Node through a later enrolment was never checked to tell its Sensors — `ANodeGivenItsLotByASecondEnrolmentTellsItsSensorsAtOnce` added; it fails with the push in `Enrol` removed.
  - `[medium]` `[patch]` Gap: the one-shot activation timer was observed by no test — `AContextLostBeforeASiloRestartIsSentAgainOnceTheDeviceIsActivatedWithNobodyWakingIt` added (activation by `Describe`, no wake, no frame); it fails with the timer removed.
  - `[false]` `[reject]` Intent: the Sensor is not named on the Alerts list — the Alert journals the Sensor ID and quantity (`AlertOpened`, `SiteAlert`, `NotificationEntry`, asserted in the grain tests); no client behaviour needs it on the row, and the intent forbids a new `HealthAlert` property.
  - `[low]` `[reject]` Intent: title, body and route are asserted on the built content, not on a tapped notification — the channels and the clients show the Server's text and route by `lotId` whatever the Alert kind (`PushNotificationsTest`, `PushEngineTest`, `browser-notifications.ts`); a tap test per kind would repeat them.
  - `[low]` `[reject]` Intent: the Administrator and Member cases run against the fake Server and the journey calibrates through the API — the gating is one role check shared by both; the Calibrate flow has its own spec (`calibrate.spec.ts`).
  - `[false]` `[reject]` Intent: a new Node's Alert opens with its declaring frame, not at enrolment — a Sensor exists only once declared; the matrix row says "its first frame declares".
  - `[false]` `[reject]` Intent: the row title and the push title differ in capitals — on purpose (Design Notes): the row shipped in Story 6.2 under the glossary rule, the push follows the UX table.
  - `[low]` `[reject]` Intent: assertions of other stories' tests changed — same as the Blind suites row; "2 to check" and two Health rows are the new truth for a Node that was never calibrated.
  - `[false]` `[reject]` Intent: iOS changes one coverage line — the presentation, catalogue and render tests of the row exist since Story 6.2 and still pass (354 Swift tests).
  - `[false]` `[reject]` Intent: the macOS CI result is listed as a manual check, not as an operator action — it is a pull-request check the pipeline reads before it merges; nothing is owed by a human outside the repository.
  - `[low]` `[reject]` Intent: the spec's boxes, logs and status were not final in the reviewed diff — this file's bookkeeping, written at finalization.

### 2026-10-13 — Review pass (follow-up)
- verdicts: 37 findings — high 0, medium 6, low 25, false 6, maybe-false 0
- findings:
  - `[low]` `[reject]` Blind: a Specification that starts requiring calibration leaves an open Threshold Alert stuck — verified in `SensorGrain.Declare` and the eligibility gate of `Evaluate`, and it was so before this story (the gate is Story 6.1's). It needs a changed Specification set, which V1 firmware does not produce; closing it adds a branch and a reason for a state nobody reaches.
  - `[low]` `[reject]` Blind: a context that is both moved and paused closes the Threshold Alert as `paused` and the uncalibrated one as `unassigned` — verified (`RaiseContext`, `UncalibratedRule.Decide`); both reasons are true of that context, and it needs a Pause, which nothing raises before Epic 8, together with a lost push.
  - `[false]` `[reject]` Blind: `removed` is given a meaning that conflicts with the one DW-84 plans — the intent names `removed` for a Specification that stops requiring calibration; a dropped Sensor also no longer requires one, so the wording holds for both, and the fix would edit the intent.
  - `[low]` `[reject]` Blind: DW-108 repeats DW-84 without a link — the ledger entries are written and resolved by the orchestrator, not by this run.
  - `[low]` `[reject]` Blind: the OpenAPI `Alert` schema does not say a Threshold Alert can close as `unassigned` or `paused` — `AlertCloseReason` lists both for every Alert and `reason` is described without a kind; an example and a client test per reason would repeat what the generic reason rendering already pins.
  - `[low]` `[patch]` Blind: the `HealthAlert` description says an uncalibrated Alert "always names the Lot the Node is on" — a closed one names the Lot the Node left, and `lotId`/`lotName` are optional. Now "names the Lot the Node was on when it opened (lotId and lotName are absent only while that Lot cannot be read)" in `coldframe.openapi.json` and the README; `schema.ts` regenerated.
  - `[low]` `[reject]` Blind: `Calibrate` and `Declare` also wait for the Alert fan-out — true, by the same delivery the first pass kept for Move, Unassign, Enrol and Ingest; answering first would add a second delivery path.
  - `[low]` `[reject]` Blind: a Sensor that answers `NotDeclared` is asked again on every frame without a log line, and `_contextConfirmed` keeps dropped Sensors — it needs a declaration that failed and was never repeated, which the next frame repairs; the map holds a handful of IDs for one activation.
  - `[low]` `[reject]` Blind: no test applies a first context to a Sensor journaled before the story that has an open Threshold Alert — the close is raised in `RaiseContext` whatever context was held before, and the Threshold tests pin it; only the null check in `SetContext` differs.
  - `[false]` `[reject]` Blind: `SetContext` adds wait cycles DW-86 does not name — `Sensor.Calibrate` awaits `Device.SetCalibration`, which interleaves, so a Device waiting in `SetContext` is not waited on in turn; DW-86 already names the awaits of Move and Unassign.
  - `[low]` `[patch]` Blind: a gone Lot was asserted for the summary only — `PushTextTests` now also pins that the Alert and its Reminder, sent alone, keep the neutral text.
  - `[medium]` `[patch]` Blind: `ReminderTableReadyAsync` swallows three failures and then fails the test — verified on CI: run 38417960926 of this branch failed `NotificationSettingsGrainTests.EverySettingSurvivesASiloRestart` after four reads of 30 s each timed out, in a test that needs no reminder. The wait now gives up after three reads and never throws, so a restart is what it was before the wait existed; the comment no longer says "once". The remarks on DW-110's text are the orchestrator's ledger.
  - `[low]` `[reject]` Blind: carried — the spec's status, loop counter and empty change log were not final in the reviewed diff; this file's bookkeeping, written at finalization, and `sprint-status.yaml` is the orchestrator's.
  - `[low]` `[reject]` Edge: carried — the rule is not limited to soil moisture while the copy says "Soil sensor"; only soil-moisture Specifications carry `calibration: true`.
  - `[low]` `[reject]` Edge: carried — a slow `SetContext` delays Move, Unassign, Enrol and Ingest; as Ingest already waited for Alerts.
  - `[low]` `[reject]` Edge: carried — a Pause change reaches the Sensors only with the next wake or frame; no grain method raises a Pause before Epic 8.
  - `[medium]` `[patch]` Gap: a lost context was never shown to be sent again by the Node's next report — `AContextThatDidNotReachTheSensorsIsSentAgainWithTheNodesNextReport` added: after an unassignment whose contexts failed, one frame sends four contexts and closes the Alert as `unassigned`; the frame after it sends none.
  - `[low]` `[patch]` Gap: a Reading as the first context of a Sensor that holds none was not exercised — filed as defer; the test was a few lines, so `ASensorThatHoldsNoContextTakesItsFirstOneFromAReading` was added instead (every context of the declaring frame fails, its Reading opens the Alert).
  - `[medium]` `[patch]` Gap: "another wake sends nothing" did not observe whether anything was sent — `SensorFaults.PassedContexts` counts the contexts that reached a Sensor, and the wake test and the new report test assert it stays put. With the confirmation assignment removed from `PushContextsAsync` both tests fail (checked, then restored).
  - `[low]` `[patch]` Gap: the repeated-pair path of `Calibrate` was not seen to deliver a pending close — filed as defer; `TheSamePairSentAgainDeliversTheCloseTheAlertGrainDidNotTake` added instead.
  - `[low]` `[reject]` Intent: carried — title, body and route are asserted on the built content, not on a tapped notification.
  - `[low]` `[reject]` Intent: the Reminder's timing and its text are asserted in two tests, never together — the timing test names the Alert the Reminder is for and the text test builds from that kind; one more integration test would join two things that share no code path.
  - `[low]` `[reject]` Intent: carried — Administrator and Member run against the fake Server and the journey calibrates through the API.
  - `[low]` `[reject]` Intent: `AlertsListTests` lists a hand-written `AlertOpened` — the grain-produced Alert is read through `AlertsReadModel` in the grain tests and through the web page in the journey; the Edge test pins the JSON shape.
  - `[false]` `[reject]` Intent: carried — iOS changes one coverage line; the tests it points at exist since Story 6.2.
  - `[medium]` `[patch]` Intent: the resend "on the next report" has no test — same gap as the Gap row; one entry.
  - `[low]` `[reject]` Intent: "only the Sensor grain closes it" is shown for a client, not for another grain — the check compares the caller with the one opener (`AlertGrain.Close`), the same line Story 6.1 tests for Threshold Alerts; no other grain calls it.
  - `[low]` `[patch]` Intent: replay of older journals does not exercise the first Reading of a Sensor without a context — same gap as the Gap row; one entry.
  - `[false]` `[reject]` Intent: a gone Lot is handled beside the matrix — the matrix row "Lot name unknown" holds as written; a gone Lot follows the rule of a Threshold Alert, and the behaviour is now pinned for the summary, the Alert and the Reminder.
  - `[low]` `[reject]` Intent: carried — other suites filter the new kind out, a journey counts from a baseline, and stream shapes were pinned again.
  - `[medium]` `[patch]` Intent: `ReminderTableReadyAsync` is new test infrastructure after every restart — same defect as the Blind row; one entry.
  - `[medium]` `[defer]` Intent: carried — the dropped Sensor and the missing grace period are recorded, not closed; both deferred in the first pass.
  - `[low]` `[reject]` Intent: carried — `sprint-status.yaml` says done while the spec says in-review; bookkeeping.
  - `[false]` `[reject]` Intent: `PendingAlertDelivery.Side` was retyped to nullable — the record is state rebuilt from events (CustomStorage journal), never written; the fixture holds no such row and replays.
  - `[low]` `[reject]` Intent: carried — a Pause reaches the Sensors with the next wake.
  - `[low]` `[reject]` Intent: carried — "Soil sensor" is fixed copy.
  - `[false]` `[reject]` Intent: carried — the Alert opens with the declaring frame, the Node's second.

## Design Notes

- **Context push, not a query.** The Sensor never asks the Device (deadlock, DW-86); it journals what it was told. The epoch orders contexts, and Readings carry it as a second path, so a lost push heals with the next Reading; unassignment has no Reading after it, hence the Device-side resend.
- **A move closes and reopens.** The Alert's purpose is the deep link to the Lot where Calibrate is offered; an Alert naming the Lot the Node left would lead to a Lot without that Sensor. `unassigned` is the reason because the Sensor was unassigned from that Lot. Threshold Alerts keep today's behaviour on a move (DW-85 remains for them).
- **`removed` for a Specification that stops requiring calibration:** otherwise the Alert could never close, since such a Sensor refuses a Calibration. Of the five reasons it is the nearest.
- **Two wordings on purpose.** Push text follows the UX table and the story ("Soil sensor … needs calibration"); the row title shipped in Story 6.2 with glossary capitals. "Soil sensor" is fixed copy: only soil-moisture Specifications carry `calibration: true`. The summary line is the one in the UX ledger import.
- **Reminder body** follows the rule "the same text with still"; the UX gives no uncalibrated example.
- **Pause** is honoured in the context so Epic 8 only has to raise the event; it is testable now by appending `device.paused`, as the silent tests do.
- **Not verifiable on this host:** SwiftUI views and the iOS App target compile only on the macOS CI jobs (DW-93).

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror && dotnet format --verify-no-changes --no-restore` -- expected: no error, no change.
- `dotnet test --no-build --project tests/cs/server.tests` -- expected: all pass.
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build --project tests/cs/server.integration` -- expected: all pass, none skipped (run by class if the whole suite exceeds the command limit).
- `pnpm --filter @coldframe/web build && ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build --project tests/cs/web.e2e` -- expected: all journeys pass.
- `packages/proto/check-compat.sh` (tools from `packages/proto/install-tools.sh <dir>` on `PATH`) -- expected: no breaking change.
- `pnpm --filter @coldframe/api-client generate && pnpm --filter @coldframe/openapi run check && pnpm -r typecheck && pnpm -r lint && pnpm --filter @coldframe/web-tests test && pnpm --filter @coldframe/api-client-tests test` -- expected: pass, no stale schema.
- `pnpm --filter @coldframe/web-e2e test` -- expected: pass, with the new screenshots committed.
- `ANDROID_HOME=$HOME/Android/Sdk ./gradlew check` -- expected: pass.
- `podman run --rm --security-opt label=disable -v "$PWD":/work -w /work docker.io/library/swift:6.3.3 sh -c 'swift build && swift test && swift format lint --strict -r .'` -- expected: pass.

**Manual checks (if no CLI):**
- The macOS CI jobs `swift` and `ios` on the pull request are the only compile check of the iOS test edit; read their result before the merge.

## Auto Run Result

Status: done

**Summary:** A Node's Device grain tells each of its Sensor grains where the Node is (Site, Lot or none, paused, epoch) after an enrolment that assigns a Lot, a move, an unassignment and a declared Specification set, and again on the next report, wake or activation until the Sensor confirmed it. The Sensor grain journals that context and keeps a Health Alert of kind `uncalibrated` open exactly while it requires calibration, has none, and its Node is on a Lot and not paused; it closes as `calibrated`, `unassigned`, `paused` or `removed`. An unassigned or paused context also closes the Sensor's open Threshold Alert. Push text is "Soil sensor on Lot 'Peppers' needs calibration" / "An Owner or Administrator can calibrate it.", routed to Lot detail. This follow-up pass changed no production code: it fixed the test fixture that failed the pull request's `.NET` job, added four tests and corrected one contract description.

**Files changed (first pass, commit 39b0d70):**
- `packages/cs/contracts/Sensors/**`, `packages/cs/contracts/Alerts/**` -- `ISensorGrain.SetContext`, `SensorContext`, the three Sensor events, `AlertKind.Uncalibrated`, `IAlertGrain.OpenForSensor`.
- `apps/cs/server/Sensors/**`, `Devices/**`, `Alerts/**`, `Lots/LotsProjector.cs`, `Notifications/**` -- the rule, the held context, the context push, the kind's ID, name and open, the push text.
- `packages/openapi/**`, `packages/ts/api-client/src/schema.ts` -- descriptions and examples.
- `tests/**`, the six READMEs, `deferred-work.md` (DW-85 narrowed).

**Files changed (this pass):**
- `tests/cs/server.integration/Identity/IdentityCluster.cs` -- `ReminderTableReadyAsync` reads three times and never throws; `SensorFaults.PassedContexts` counts the contexts that reached a Sensor.
- `tests/cs/server.integration/Devices/UncalibratedAlertGrainTests.cs` -- three tests (resend with the next report, first context from a Reading, the same pair delivering a pending close) and the "sends nothing" assertion of the wake test.
- `tests/cs/server.tests/Notifications/PushTextTests.cs` -- a gone Lot for the Alert and its Reminder sent alone.
- `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md`, `packages/ts/api-client/src/schema.ts` -- an uncalibrated Alert "names the Lot the Node was on when it opened".

**Review (this pass):** 37 findings from four layers. Patched 7 entries: the reminder-table wait that failed CI, three test gaps on the Device-side resend and its confirmation, two smaller test gaps, one description. Deferred none; the three entries of the first pass stand. Rejected 20 low and 6 false findings; 13 rows are carried from the first pass. Each reason is in the triage log. Patched counts by verdict: high 0, medium 3, low 4.

**Follow-up review recommended: false.** No `high` entry was patched in this follow-up pass.

**Verification (this pass):**
- `dotnet build --no-restore -warnaserror && dotnet format --verify-no-changes --no-restore` -- clean.
- `tests/cs/server.tests` -- 999 passed.
- `tests/cs/server.integration`, by class -- `UncalibratedAlertGrainTests` 26 passed; `NotificationSettingsGrainTests` and `NodeMoveTests` (the restart-heavy classes) 35 passed. The whole suite and `tests/cs/web.e2e` were not run again: the pass changed tests and a description only, and the `.NET` job runs them.
- Mutation check: with the confirmation assignment removed from `DeviceGrain.PushContextsAsync`, the wake test and the new report test fail; restored.
- `packages/proto/check-compat.sh` -- no breaking Protobuf or OpenAPI change.
- `pnpm --filter @coldframe/api-client generate`, `@coldframe/openapi` check, `pnpm -r typecheck`, `pnpm -r lint`, web tests (744), api-client tests (14) -- pass.
- `./gradlew :core:jvmTest` -- successful.
- Not run again: `web-e2e`, the Android checks, the Swift container (none of their inputs changed).

**Residual risks:**
- The reminder-table stall itself is not fixed (DW-110): the fixture no longer fails a test on it, but a test that needs a reminder right after a restart can still time out on CI, as `NodeMoveTests` did before this story. The stall did not occur locally, so the fixture change is confirmed only by the next `.NET` job.
- The context confirmation is in memory: every Device activation calls each of its Sensors once.
- Move, Unassign, an assigning Enrol, Calibrate and a changed declaration wait for the Sensors' Alert delivery.
- The Alert notifies as soon as a newly assigned Node declares its Sensor, and once for every never-calibrated Sensor after the upgrade (deferred).
- A Pause reaches the Sensors with the Node's next wake or frame; Epic 8 has to call the push where it raises the Pause.
- A moved Node's open Threshold Alert still names the Lot it left (DW-85).
