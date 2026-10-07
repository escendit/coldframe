---
title: 'Story 5.1: Calibration on the Server'
type: 'feature'
created: '2026-10-07'
baseline_revision: 'fb570e18a6cad9810aebfee5a15220a8053cf7c0'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-5-context.md'
  - '{project-root}/apps/cs/README.md'
warnings: ['oversized']
deferred:
  - summary: >-
      The Server exposes no way for a client to learn a stored Reading's reading_seq, which the Calibration endpoint requires.
    evidence: |-
      No OpenAPI surface lists Readings with reading_seq; the new tests read it from the readings table. Story 5.2 ("pick a recent stored Reading from the list") must add it.
    location: >-
      packages/openapi/coldframe.openapi.json
    severity: medium
  - summary: >-
      SensorReadings.FindRawValueAsync looks up readings by sensor_id and reading_seq without a measured_at bound, so it visits every monthly partition.
    evidence: |-
      readings is partitioned by measured_at and only indexed on (sensor_id, measured_at); the lookup is rare (admin calibration) but grows with history. An index on (sensor_id, reading_seq) or a bounded time window would fix it.
    location: >-
      apps/cs/server/Sensors/SensorReadings.cs:13
    severity: low
---

<intent-contract>

## Intent

**Problem:** Soil moisture is stored and shown as a raw probe count; there is no Calibration state, event, endpoint or `calibration_id` writer, so no Lot can leave *needs calibration* and no % exists.

**Approach:** An Administrator POSTs a dry and/or wet point (each naming a stored Reading's `reading_seq`) for a `calibration: true` Sensor. The Sensor grain is the only validator/writer: it persists `SensorCalibrated`, sets the Calibration in force on the Device grain before confirming (redelivering from persisted state on failure), later Readings are stamped with the Calibration ID, and % is derived from the stored Calibration and rounded to 5 %.

## Boundaries & Constraints

**Always:** Sensor grain owns Calibration and validates it; the Device grain is a read-only cache (AD-9). Events are past tense (`SensorCalibrated`), PRD glossary terms verbatim. Calibration is two-point linear, either orientation of dry/wet raw values, % clamped to 0–100 then rounded to nearest 5. Endpoint is `RequireSiteRole(SiteRole.Administrator)` under `/sites/{siteId}/...`, in the OpenAPI contract and the authorization matrix. Test-first (NFR16): named failing tests before implementation. Use `TimeProvider`, never wall clock. Each new `[EventType]` gets a `journal.json` row.

**Never:** No BLE path. No Threshold work (5.3), no app/UI work (5.2), no change to history already stored (recalibration affects only later Readings), no Threshold % changes. No `needsWater`. Do not write `sprint-status.yaml`. No edits to existing migrations.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Both points | `calibration: true` Sensor, stored Readings; Admin POSTs dry+wet `readingSeq` | `SensorCalibrated` (new Calibration ID, both raw values) persisted; Device grain set before 200; response carries Calibration ID and raw values | — |
| Dry only | Only the dry point submitted | Sensor stays uncalibrated; dry point kept; wet later completes it (same for wet first) | — |
| Recalibration | Calibration in force, new pair saved | New Calibration ID; only later Readings use it; stored Readings keep theirs | — |
| Indistinct points | Equal raw values or span below named minimum | Rejected, nothing persisted | 400 Problem Details |
| Unknown Reading | `readingSeq` not stored for this Sensor | Rejected | 400/404 Problem Details |
| Not calibratable | Sensor spec `calibration: false` / unknown Sensor / other Site | Rejected | 404 or 400 Problem Details |
| Member | Member caller | Rejected | 403 |
| Device call fails | Device grain call throws after persist | Persisted event kept; delivery retried until Device acknowledges (also after silo restart) | Request does not claim confirmation until delivered |
| First calibrated Reading | Calibration in force, new Reading ingested | Row stored with that `calibration_id`; % = derived, nearest 5 | — |
| Lot status | Soil Sensor was the only uncalibrated one | Lot leaves *needs calibration* when saved; % appears with the next Reading | — |

</intent-contract>

## Code Map

- `packages/cs/contracts/Sensors/SensorEvents.cs` -- add `SensorCalibrated` (+ pending-point event if needed) modelled on `SensorThresholdsChanged`: `[EventType("sensor.calibrated")]`, `[GenerateSerializer]`, `[Alias]`, `[property: Id(n)]`.
- `packages/cs/contracts/Sensors/SensorGrains.cs` -- `ISensorGrain` (`coldframe.sensor`), `SensorSnapshot`: add calibrate method/DTOs and Calibration in snapshot.
- `apps/cs/server/Sensors/SensorGrain.cs`, `SensorState.cs` -- `JournaledStreamGrain` pattern `RaiseEvent` + `ConfirmEvents`; next free `[Id]` is 5; one `Apply(Event)` overload per event; pending point + delivery state live here.
- `packages/cs/contracts/Devices/DeviceGrains.cs`, `apps/cs/server/Devices/DeviceGrain.cs` (`Ingest` ~l.474-640, commit ~l.560), `DeviceState.cs` (next free `[Id]` is 12), `DeviceEvents.cs` -- Device-side "calibration in force" cache event (idempotent per Calibration ID) and per-Sensor lookup during ingest. DW-45: Device journal replay cost grows with events.
- `apps/cs/server/Devices/DeviceIngestionStore.cs` -- `InsertReadingsSql` hard-codes `NULL` for `calibration_id` (l.99-103); `ReadingWrite` lacks a field: add Calibration ID per row (unnest array parameter).
- `apps/cs/migrations/Migrations/M20261006170000AddLotStatusToLots.cs` (latest) -- new migration later than this; `readings.calibration_id uuid NULL` already exists (`M20261006150000CreateTablesReadings.cs`), lookups by `sensor_id` + `reading_seq` use `ix_readings_sensor_id_measured_at`. Add table for Calibration points by ID (and `lot_status_sensors.calibrated` if used).
- `apps/cs/server/Lots/LotsProjector.cs` (upsert ~l.94-102, uncalibrated subquery ~l.119, handlers ~l.285-300), `LotStatusRule.cs` (l.127 `needsCalibration`) -- add `sensor.calibrated` handling; checkpoint reset as 4.7 migration did for old events.
- `apps/cs/server/Lots/SensorConversion.cs`, `LotsReadModel.cs` -- soil moisture is raw "until Calibration exists"; derive % (nearest 5) from the reading's Calibration; check how 4.8 uses `moisturePercent`.
- `apps/cs/server/Edge/EdgeApi.cs` (route table ~l.275-340, `MoveDeviceAsync` ~l.1231 as template), `EdgeAccessRule.cs`, `EdgeProblems.cs`, `EdgeValidation.cs` -- new POST endpoint, Site ownership check via Device -> Site, Problem Details.
- `packages/openapi/coldframe.openapi.json` + `packages/openapi/README.md` -- operation with `x-coldframe-minimum-role: "Administrator"`, `BadRequest/Unauthorized/Forbidden` refs; regenerate Kotlin/TS clients (`pnpm -r test` checks staleness). `EdgeEndpointDiscoveryTests` fails on any mapped/contract mismatch.
- `tests/cs/server.integration/Edge/AuthorizationMatrixTests.cs` (`_samples`), `tests/cs/server.tests/Edge/EdgeEndpointCatalog.cs` -- add the endpoint.
- `tests/cs/server.integration/Identity/IdentityCluster.cs` (`SensorFaults` call filter l.64-66, 200-266) -- extend with a Device-call fault to test redelivery; `Devices/SpecificationGrainTests.cs`, `IngestGrainTests.cs` -- model for TestCluster tests (`SeedNodeAsync`, `IngestAsync`, `CountAsync`); `tests/cs/server.tests/Fixtures/journal.json` + `FixtureJournalReplayTests`; `tests/cs/server.tests/Sensors/SensorStateTests.cs`.
- `apps/cs/README.md` (sections "Add an event type" l.74, "Sensor Specifications" l.327, "Lot status" l.429, "Add an endpoint" l.519) -- document Calibration.

## Tasks & Acceptance

**Execution:**
- `tests/cs/server.tests/Sensors/`, `tests/cs/server.tests/Lots/` -- write failing unit tests first for calibration state, % math (both orientations, clamp, nearest 5), distinctness rule, half-finished state -- NFR16
- `tests/cs/server.integration/**` -- failing TestCluster/integration tests for each matrix row incl. redelivery of Calibration in force and Lot leaving *needs calibration* -- NFR16
- `packages/cs/contracts/Sensors/*`, `packages/cs/contracts/Devices/*` -- events, grain methods, DTOs -- AD-9
- `apps/cs/server/Sensors/*`, `apps/cs/server/Devices/*` -- Sensor-owned calibration, persisted pending point, Device set-in-force with retry from persisted state, Calibration ID on ingested Readings -- AD-9
- `apps/cs/migrations/Migrations/*`, `apps/cs/server/Lots/*` -- Calibration points store, projector, status rule, % derivation
- `apps/cs/server/Edge/*`, `packages/openapi/*`, generated clients -- endpoint `POST /sites/{siteId}/sensors/{sensorId}/calibration` taking `{dry?: {readingSeq}, wet?: {readingSeq}}`; Administrator role; Problem Details
- `tests/cs/server.tests/Fixtures/journal.json`, `apps/cs/README.md` -- fixture rows and docs

**Acceptance Criteria:**
- Given a `calibration: true` Sensor with stored Readings, when an Administrator submits dry and wet points naming stored `reading_seq`s, then `SensorCalibrated` is persisted with a new Calibration ID and both raw values, and the Device grain has it in force before the call confirms.
- Given the Device call fails, when the Sensor grain recovers or the silo restarts, then the persisted Calibration is redelivered until the Device holds it.
- Given a Calibration in force, when new Readings arrive, then each is stored with that Calibration ID and its % is derived from it, rounded to nearest 5 %.
- Given a recalibration, then only later Readings use the new Calibration and history keeps its own.
- Given only one point, then the Sensor stays uncalibrated and the point is kept until the other arrives.
- Given indistinct points, then 400 Problem Details; given a Member, then 403 and the endpoint is in the authorization matrix.
- Given the Calibration is saved, then the Lot leaves *needs calibration*; its % appears with the next Reading.

## Spec Change Log

## Review Triage Log

### 2026-10-07 — Review pass
- verdicts: 28 findings (blind 11, edge 16, verification-gap 1; intent-alignment reported none) — high 0, medium 2, low 23, false 3, maybe-false 0; rows below group findings that share a cause
- findings:
  - `[medium]` `[patch]` Nothing tests the `deliver-calibration` reminder (verification-gap) — added `TheDeliveryReminderExistsWhileTheCalibrationIsUndeliveredAndIsGoneOnceTheDeviceAcknowledges`.
  - `[low]` `[patch]` Unused `ix_calibrations_sensor_id` (blind) — removed from the migration.
  - `[medium]` `[defer]` No client way to learn `reading_seq` (blind, intent) — deferred to Story 5.2.
  - `[low]` `[defer]` Reading lookup without `measured_at` bound scans partitions (blind, edge) — deferred.
  - `[low]` `[reject]` Permanent Device-call failures retry forever (blind, edge) — SetCalibration only throws for a Node that is not enrolled, which the Sensor's Device always is; no unenrol path exists.
  - `[low]` `[reject]` CatchUpLotsAsync failure still answers 200 (edge) — the events are journaled and the projector catches up on its next poll; logged.
  - `[low]` `[reject]` Same-pair shortcut skipped while a point is pending (edge) — yields a new Calibration with identical points; harmless.
  - `[low]` `[reject]` Pending point with same raw value but different readingSeq not re-recorded (edge) — only raw values matter; seq is provenance only.
  - `[low]` `[reject]` Delivered-event confirm failure answers 503 although delivered (edge) — redelivery is idempotent by revision.
  - `[low]` `[reject]` DeviceGrain.SetCalibration does not check the Sensor belongs to the Node (edge) — only callable by the Sensor grain, whose Device is derived from the Sensor ID.
  - `[low]` `[reject]` Percent throws on equal points / Reading shows raw until calibrations row projected (edge) — only reachable with hand-seeded rows or a failed catch-up, which the code comments cover.
  - `[low]` `[reject]` Guid Describe activates grains; reminder registration failure leaves only timer/activation (edge) — admin-only, narrow, and activation still delivers.
  - `[low]` `[reject]` PointRecorded answers calibrated:true while recalibrating (edge) — documented in the README; the spec says the Sensor stays as calibrated as it was.
  - `[false]` `[reject]` FindSql LIMIT 1 may pick an arbitrary row (edge) — `reading_keys` PK (device_id, sensor_id, reading_seq) allows exactly one stored row.
  - `[false]` `[reject]` Stamped gives older buffered Readings the new Calibration (edge) — the AC stamps Readings that arrive after the Calibration is in force.
  - `[false]` `[reject]` Malformed readingSeq body could 500 (edge) — `IndistinctPointsAnUnknownReadingAndMalformedBodiesAre400…` covers it.
  - `[low]` `[reject]` Kotlin client lacks calibrateSensor; provenance of reading_seq not in SensorCalibrated; no recency/claim check on Readings; fixed MinimumSpan; Lot/percent gap during 503; missing concurrency/replay/HTTP-503 tests; process remarks on empty logs and epic context file (blind, intent) — app clients are Story 5.2, and the spec/intent set none of the others.


## Design Notes

**Minimum span:** distinctness is a named constant in the Sensor grain (initially 16 raw counts, `|dry - wet| >= 16`); identical values are always rejected. **Redelivery:** persist delivery state on the Sensor stream; retry on activation and by a durable reminder/timer until the Device acknowledges; Device apply is idempotent by Calibration ID. **Reading discovery:** exposing `reading_seq` to clients (recent Readings list) belongs to 5.2; tests read it from the `readings` table.

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror` -- expected: success
- `dotnet format --verify-no-changes --no-restore` -- expected: no changes
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- expected: all pass
- `pnpm -r test` -- expected: success (generated clients not stale)

## Auto Run Result

Status: done

**Summary:** Administrators can calibrate a `calibration: true` Sensor over `POST /sites/{siteId}/sensors/{sensorId}/calibration`. The Sensor grain validates the named stored Readings, keeps a single point until the other arrives, rejects indistinct points, journals `SensorCalibrated`, and sets the Calibration in force on the Device grain before confirming, redelivering from persisted state (timer, reminder, activation) on failure. Readings are stored with the Calibration ID; % is derived (linear, clamped, nearest 5) in Lot detail; the Lot leaves *needs calibration* on save.

**Files changed:** contracts (Sensor/Device events and grain interfaces); `apps/cs/server` Sensor grain/state, `CalibrationMath`, `SensorReadings`, Device grain/state/ingestion store, Lots projector/read model/conversion, Edge endpoint and problems; migration `M20261007120000CreateTableCalibrations`; OpenAPI contract, README and TS schema; unit, TestCluster and endpoint tests plus journal fixtures; `apps/cs/README.md`.

**Review:** 28 findings. Patched: 2 (reminder test added; unused index removed). Deferred: 2 (client `reading_seq` discovery for 5.2; partition-wide Reading lookup). Rejected: the rest, with reasons in the Review Triage Log.

**Follow-up review recommended:** false (one medium patched, no high).

**Verification:** `dotnet restore --locked-mode`, `dotnet build -warnaserror` and `dotnet format --verify-no-changes` clean; `dotnet test` 875 passed, 0 failed (rerun after patches); `pnpm -r test` passed. Matrix rows each covered by a passing test.

**Residual risks:** the 503 path is covered at grain and mapping level only; the history endpoint still reports soil moisture raw; no client can yet discover `reading_seq` (Story 5.2).
