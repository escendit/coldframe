---
title: 'Story 4.6: Sensor Specifications and Sensor grains'
type: 'feature'
created: '2026-10-06'
baseline_revision: 'efd7f3e17e452b713ef070cf7de6b8f50d17e6cd'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
  - '{project-root}/apps/cs/README.md'
warnings: ['oversized']
deferred:
  - summary: >-
      The Node firmware does not yet compute spec_hash, declare its Specification set or send it once per request; only the Device simulator does.
    evidence: |-
      Story 4.6's first criterion ends "the Node sends it once", but no Rust code sends frames: Story 4.4 (ESP-NOW transport) is in backlog, and its acceptance criteria name spec_hash on the frame but not the declaration. apps/rs/node and packages/rs contain no reference to spec_hash, specifications_unknown or SpecificationSet. The contract is in packages/proto (NodeFrame.specifications, Downlink.specifications_unknown) and the Node's behaviour is modelled by tests/cs/device-simulator/SimulatedDevice.cs. The firmware side must be built with or after Story 4.4: hash = SHA-256 of the serialized set, four Specifications (soil moisture calibration true with default Thresholds, the other three without), attach the set to the next frame after a downlink says specifications_unknown. A frame with the set of four is about 200 bytes sealed, close to the ESP-NOW payload limit.
    location: >-
      apps/rs/node, packages/rs (Story 4.4)
    severity: medium
---

<intent-contract>

## Intent

**Problem:** A Node frame carries a `spec_hash` that the Server ignores, the Specification set is not defined on the wire, and no Sensor grain exists. The Server therefore stores Readings without knowing each Sensor's unit, range, `calibration` flag or default Thresholds, so nothing can be labelled, calibrated or given Thresholds later.

**Approach:** Define the Specification set and the "hash unknown" downlink flag in `packages/proto` (additive). The Device grain compares each frame's `spec_hash` with the one it last accepted, asks for the set in the sealed downlink when they differ, and on receiving a set declares every Sensor to its event-sourced Sensor grain, then journals the accepted hash and Sensor list. The Device simulator plays the Node: it sends the set once per request.

## Boundaries & Constraints

**Always:**
- AD-19 and AD-6 as written in `ARCHITECTURE-SPINE.md`. `sensorId = SensorIds.Derive(deviceId, slot, quantityToken)`; `slot` is the index in the Specification set. The Sensor grain is the only writer of a Sensor's state; it is a `JournaledStreamGrain` on stream `sensor/{sensorId}`.
- A "known" hash is per Device: the hash of the last set the Device grain accepted. An empty `spec_hash` is never known.
- Story 4.5's ingestion order, statuses and transaction are unchanged. A declaration is handled after the frame's commit and relay change, before the downlink is sealed. Readings are stored and acknowledged whether or not their slot is declared.
- Declaration order: every Sensor grain first (idempotent `Declare`), then one Device event with the hash and the Sensor list. If either step fails the frame answers `retry` and the hash stays unknown, so the Node is asked again.
- Thresholds are kept per side as `Default | Override(value) | Cleared`. A side in `Default` follows the Specification's default, which may be absent. A declaration changes the Specification only, never a side's kind or an override's value.
- A declaration is handled for paused and unassigned Nodes too; only Readings pass the Pause gate.
- `TimeProvider`/`Clock` only. No Reading payload, key, frame bytes or Specification content in logs. Tests go through the Device simulator and are written first. Proto changes are additive.

**Never:**
- No evaluation: no Reading is delivered to a Sensor grain, no streaks, no Alerts, no evaluation context (Epics 6, 7, 8).
- No API to set, clear or validate Thresholds and no proposed low default (Story 5.3). No Calibration (Epic 5). The Threshold-change event exists only so tests can seed an override.
- No REST endpoint, OpenAPI change, read model, projector or migration. No LotStatus (Story 4.7).
- No firmware, Hub or Rust codec work (Story 4.4 builds the Node's side against this contract). No change to `packages/crypto-spec`.
- Do not write `sprint-status.yaml`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Unknown hash | Valid frame, `spec_hash` differs from the Device's known hash (or none known), no set attached | `stored`/`duplicate` as before; downlink has `specifications_unknown = true`; nothing journaled for Sensors | No error expected |
| Declaration | Frame with a hash and a valid set of 4 (soil moisture `calibration: true` with defaults 30/80 %, three watched Sensors without defaults) | 4 streams `sensor/{id}` each with `sensor.declared`; Device journals `device.specifications-declared`; downlink flag is false | No error expected |
| Same hash again | Known hash, with or without the set attached | No event on any stream; flag false | No error expected |
| Changed Specification | New hash; a slot keeps its quantity but range or defaults differ | `sensor.specification-changed` on that Sensor only; unchanged Sensors journal nothing; Device journals the new hash | No error expected |
| Override survives | Sensor with low `Override(40)` and high `Default`; redeclared with defaults 25/75 | Low stays `Override(40)`; high stays `Default` and now reads 75 | No error expected |
| Cleared survives | Sensor with high `Cleared`; redeclared with a default high | High stays `Cleared` with no value | No error expected |
| New quantity at a slot | New set has another quantity at slot 1 | A new Sensor ID is declared; the old Sensor's stream is untouched; the Device's Sensor list holds only the new set | No error expected |
| Undeclared slot | Readings before any declaration, or for a slot beyond the set, or with a quantity other than the declared one | Stored and acknowledged with their derived Sensor ID; no `sensor/{id}` stream is created for them | No error expected |
| Declaration arrives later | Undeclared Readings stored, then the set | The Sensor is declared under the same Sensor ID the stored rows carry | No error expected |
| Sends it once | Simulator opens a downlink with the flag | Its next wake attaches the set, the one after does not | A lost frame is asked for again by the next downlink |
| Invalid set | Empty or over 32 bytes hash with a set; 0 or more than 32 Specifications; unknown quantity or unit; `range_min >= range_max`; default low >= default high; a default outside 0–100 (calibrating) or outside the range (others) | Readings stored and acknowledged; nothing declared; flag stays true | One warning log naming only the Device ID |
| Paused Node | Paused Device sends a set | Sensors declared; no Reading rows | No error expected |
| Restart | Hash accepted, silo restarted, same hash again | Flag false, no new event | No error expected |
| Wrong Sensor | `Declare` on a grain whose key is not the ID derived from the request | `ArgumentException`; nothing journaled | — |

</intent-contract>

## Code Map

- `packages/proto/coldframe/device/v1/envelope.proto` -- `Downlink` (fields 1–5), `Quantity`, `NodeFrame` (fields 1–10, `spec_hash = 2` "used from Story 4.6"). `buf lint` STANDARD and `buf breaking` FILE apply (`packages/proto/check-compat.sh`). Only C# consumes it (`packages/cs/protocol`); no Rust or Kotlin codegen for this file.
- `packages/proto/README.md:9-12` -- the table row and the "arrive in Epic 4" sentence to update.
- `packages/cs/crypto/SensorIds.cs` -- `Derive(deviceId, slot, quantity)`; tokens in `CryptoSpec.SensorQuantity*`.
- `apps/cs/server/Devices/NodeFrameReader.cs` -- `Read` returns `NodeFrameRead(Verdict, Rows, Acknowledged)`; `QuantityToken`; `MaxSlot`. Ignores `spec_hash` today.
- `apps/cs/server/Devices/DeviceGrain.cs:222-358` -- `Ingest`: commit (`:305`), relay change with `retry` on failure (`:314-328`), downlink built at `:331`. `CancellationToken.None` on calls that must not be abandoned.
- `apps/cs/server/Devices/DeviceState.cs` -- next free `[Id]` is 9; `Apply` overload per event.
- `packages/cs/contracts/Devices/DeviceEvents.cs` -- event style: `[EventType("device.…")]`, `[GenerateSerializer]`, `[Alias]`, positional record. `DeviceGrains.cs` -- grain interface style.
- `apps/cs/server/Journal/JournaledStreamGrain.cs` -- base class; `StreamId` is the grain ID (`sensor/{key}` with `[GrainType("sensor")]`). `apps/cs/server/Lots/LotGrain.cs`, `LotState.cs` -- a small event-sourced grain to model on (without its projection catch-up).
- `apps/cs/server/Journal/EventTypeRegistry.cs` -- scans the contracts assembly for `[EventType]`; no registration step.
- `apps/cs/server/Devices/DevicesProjector.cs:60-84` -- ignores unknown Device events (`_ => Task.CompletedTask`); no change needed.
- `tests/cs/device-simulator/SimulatedDevice.cs` -- `DefaultReadings` (`:66`), `Wake`/`WakeUnsynced` (`:265,280`), `NewFrame` (`:224`, sets no `spec_hash`), `SealFrame(NodeFrame)` (`:295`), `OpenDownlink` (`:427`).
- `tests/cs/server.integration/Devices/IngestGrainTests.cs` -- model for TestCluster tests: `[Collection(IngestSuites.Name)]`, `IClassFixture<IdentityCluster>`, `SeedNodeAsync(params object[] more)` (`:518-528`, seeds journal events), `IngestAsync` (`:530`), `identity.AliasesAsync(stream)`, `identity.RestartSiloAsync()`. Some tests assert exact Device stream aliases after several frames.
- `tests/cs/server.integration/Identity/IdentityCluster.cs:52-67` -- grain accessors; add `Sensor(id)`. `Logs` is the `CapturingLoggerProvider`.
- `tests/cs/server.integration/Devices/IngestTests.cs` -- AppHost tests through `POST /device/ingest` with the simulator.
- `tests/cs/server.tests/Fixtures/journal.json`, `Journal/FixtureJournalReplayTests.cs:25-32` -- one row per `[EventType]`; the `States` map needs `["sensor"]`.
- `tests/cs/server.tests/Devices/` -- unit tests of `NodeFrameReader`.
- `apps/cs/README.md:315-318` -- "`spec_hash` is carried and ignored until Story 4.6".

## Tasks & Acceptance

**Execution:**
- [x] `packages/proto/coldframe/device/v1/envelope.proto` -- add `Unit`, `Specification`, `SpecificationSet`, `NodeFrame.specifications = 11` and `Downlink.specifications_unknown = 6` as in Design Notes; rewrite the `spec_hash` comment -- the contract (AD-19).
- [x] `packages/proto/README.md` -- describe the Specification set and the flag; drop the "arrives in Story 4.6" wording -- contract docs.
- [x] `packages/cs/contracts/Sensors/SensorGrains.cs`, `SensorEvents.cs` -- `ISensorGrain` (`Declare`, `Describe`), `SensorSpecification`, `ThresholdSetting` (kind + value), `SensorSnapshot`, results; events `sensor.declared`, `sensor.specification-changed`, `sensor.thresholds-changed` (the last has no producer in this story) -- grain surface (AD-6, AD-19).
- [x] `packages/cs/contracts/Devices/DeviceEvents.cs` -- `DeviceSpecificationsDeclared(SpecHash, Sensors[slot, quantity, sensorId], DeclaredAt)` with alias `device.specifications-declared` -- the Device's known hash and Sensor list.
- [x] `apps/cs/server/Sensors/SensorGrain.cs`, `SensorState.cs` -- the grain and its state: first `Declare` journals `sensor.declared` with both sides `Default`; an equal Specification journals nothing; a different one journals `sensor.specification-changed`; a request that does not derive this grain's key throws; `Describe` returns the Specification and each side's kind and effective value, or "not declared" -- the core of the story.
- [x] `apps/cs/server/Devices/NodeFrameReader.cs` -- also return the frame's `spec_hash` and, when a set is attached, either the validated Specifications or "invalid set" by the matrix rules; an invalid set never changes the frame's verdict -- decoding in one place.
- [x] `apps/cs/server/Devices/DeviceState.cs`, `DeviceGrain.cs` -- state `SpecHash` and declared Sensors; in `Ingest`, after the relay change: handle an attached valid set with a new hash in the Always order, answer `retry` on failure, log one warning for an invalid set, and set `specifications_unknown` when the frame's hash is not the known one -- ingestion wiring.
- [x] `tests/cs/device-simulator/SimulatedDevice.cs` -- `Specifications` (default set of 4 matching `DefaultReadings`; settable), `SpecHash` (SHA-256 of the serialized set); every wake carries the hash; `OpenDownlink` records a request and the next wake attaches the set once; a way to attach or withhold it explicitly -- the Node's side (AD-24).
- [x] `tests/cs/server.integration/Devices/SpecificationGrainTests.cs`, `Identity/IdentityCluster.cs` -- TestCluster tests for every matrix row, in the `IngestSuites` collection; keep the existing ingestion tests green, adjusting only assertions that the simulator's new behaviour changes -- acceptance.
- [x] `tests/cs/server.integration/Devices/IngestTests.cs` -- one AppHost test: first frame's downlink asks, the second frame declares, the third is not asked -- the behaviour at the outermost surface.
- [x] `tests/cs/server.tests/` -- `NodeFrameReader` cases for a valid and each invalid set; `SensorState` apply tests; rows for the four new events in `Fixtures/journal.json` and `["sensor"]` in `FixtureJournalReplayTests` with assertions -- guards.
- [x] `apps/cs/README.md` -- replace the "carried and ignored" sentence with how declaration works, the Sensor stream and events, and that Thresholds have no API yet -- developer docs.

**Acceptance Criteria:**
- Given a Node that reports for the first time through `POST /device/ingest`, when the simulator follows the downlinks, then the first downlink asks for the Specification set, the second frame carries it, and no later downlink asks again.
- Given a declared default set, when each Sensor grain is described, then soil moisture has `calibration: true`, unit raw count and Thresholds `Default` 30 and 80; temperature, humidity and gas resistance have `calibration: false`, their unit and range, and both sides `Default` with no value.
- Given the journal after a declaration, when a silo restarts and the same Node reports again, then no Sensor or Device event is added.
- Given the contract checks, when `packages/proto/check-compat.sh --self-test` and the base comparison run, then both pass.
- Given the .NET CI commands, when they run, then build with `-warnaserror`, `dotnet format --verify-no-changes` and `dotnet test` pass.

## Spec Change Log

## Review Triage Log

### 2026-10-06 — Review pass
- verdicts: 32 findings — high 0, medium 3, low 14, false 15, maybe-false 0
- findings:
  - `[medium]` `[patch]` (blind) The declaration-failure path has no test — added `ADeclarationThatFailsIsRetryAndLeavesTheHashUnknownUntilItSucceeds` with a test-only grain call filter (`SensorFaults` in `IdentityCluster.cs`) that makes `ISensorGrain.Declare` throw for chosen Sensors.
  - `[low]` `[reject]` (blind) A persistent declaration failure withholds the acknowledgement of committed Readings — the intent prescribes `retry` when a declaration step fails; it needs a lasting journal or grain fault, during which the relay-change path of Story 4.5 behaves the same, and the Node keeps its Readings.
  - `[low]` `[patch]` (blind) A declaration that failed partway leaves Sensor streams the Device never lists — `apps/cs/README.md` now says so, and that the Device's Sensor list, not a Sensor stream, says which Sensors a Node has.
  - `[low]` `[reject]` (blind) No ordering guard: a late frame with an older set makes that set known again — needs a firmware change between two frames plus an old sealed frame delivered late with a set attached (sets go out only on request, and a resend is freshly sealed with the current hash); the next frame heals it. A guard would add a journaled counter.
  - `[low]` `[reject]` (blind) An invalid set warns on every second wake without limit — needs faulty firmware; about 48 lines a day per such Node, which is the signal an operator needs. Suppression would add per-hash state.
  - `[low]` `[reject]` (blind) A Node alternating two valid sets grows the journal — needs firmware that changes its set on every wake; nothing shows that occurs.
  - `[low]` `[reject]` (blind) The hash is never tied to the set it arrives with — by design: the Server only compares. Recomputing needs a canonical encoding on both sides; a mismatch needs faulty firmware and the frames are authentic.
  - `[low]` `[reject]` (blind) Quantity, unit and calibration are not checked against each other — needs faulty firmware; the invalid-set rules of the intent do not include a quantity-to-unit table, and adding one makes the Server reject hardware the contract otherwise allows.
  - `[low]` `[reject]` (blind) A Sensor replaced at its slot is never marked as gone — real only after a hardware change; the Device's Sensor list is the source of membership (README). A retirement event is new contract surface the intent does not ask for; listed under residual risks.
  - `[false]` `[reject]` (blind) Timestamps of one acceptance differ across streams — true by milliseconds, but nothing compares them; each grain stamps its own events from its clock, as the Lot and Site grains do.
  - `[false]` `[reject]` (blind) `SensorState` accepts an `Override` with no value — nothing produces `sensor.thresholds-changed` yet; Story 5.3 adds the producer and its validation. A fold does not validate events.
  - `[false]` `[reject]` (blind) Sensor and Device IDs change type across the contracts, and `Declare` has no upper slot bound — no caller is named that diverges; the Device grain is the only caller and takes the slot from a set of at most 32.
  - `[false]` `[reject]` (blind) No conformance vector covers the new wire fields — the vectors pin values both sides compute (keys, nonces, Sensor IDs); this story adds none, because the hash is opaque to the Server. `envelope.proto` is the contract for the new fields.
  - `[low]` `[patch]` (blind) The journal fixture has a changed Sensor Specification without a second Device declaration — added `device.specifications-declared` (new hash, same Sensors) at version 8 and adjusted the replay assertion.
  - `[low]` `[patch]` (blind) Docs and comments drifted — the step comments in `DeviceGrain.Ingest` are numbered 1 to 9 as in the README; the missing comma in `packages/proto/README.md` is added. The projector's "nothing" row is its default branch, and the code has no branch on assignment, so no test was added for those two.
  - `[low]` `[reject]` (edge) A late frame with a superseded set is redeclared — same claim as the blind finding on ordering.
  - `[low]` `[reject]` (edge) Sensor grains keep Specifications of a set whose Device event failed when the Node goes back to the old hash — needs a failed declaration and a firmware rollback inside that window; the next accepted set redeclares every Sensor. The orphan case is now documented.
  - `[low]` `[reject]` (edge) Repeated invalid sets flood the log — same claim as the blind finding on warnings.
  - `[low]` `[reject]` (edge) A redeclaration that flips `calibration` or narrows the range keeps an override in the old unit — no override can exist before Story 5.3, which owns Threshold validation; AD-19 says a declaration never replaces an override.
  - `[false]` `[reject]` (edge) `Override` with a null value — see the blind finding.
  - `[false]` `[reject]` (edge) `Declare` accepts a slot above 31 — unreachable: the Device grain is the only caller.
  - `[medium]` `[patch]` (gap) Declaration failure (`retry`, hash stays unknown) has no test at any level — same defect as the first row; fixed with it.
  - `[medium]` `[defer]` (intent) "The Node sends it once" is asserted against the simulator, not firmware — real, but no firmware can send a frame before Story 4.4; recorded in `deferred`.
  - `[false]` `[reject]` (intent) The Server does not enforce "watched only" for temperature, humidity and gas — the criterion describes what the Node declares; FR-3 makes alerting follow the Specification's defaults, whatever the quantity.
  - `[false]` `[reject]` (intent) "Readings are labelled correctly" has no visible surface — the epic gives the surfaces to Stories 4.7 and 4.8; this story's criteria name none.
  - `[false]` `[reject]` (intent) "Not evaluated until the declaration arrives" has no behaviour behind it — evaluation is Story 6.1; this story fixes what it will gate on, the Device's journaled Sensor list.
  - `[false]` `[reject]` (intent) "Updates defaults only" read literally excludes range and unit — AD-19 names what the rule prevents: "Threshold overrides wiped by redeclaring a Specification". A stale range would also break FR-10's proposed low.
  - `[false]` `[reject]` (intent) "Never an override" is exercised at event and state level only — no command sets a Threshold before Story 5.3; the state rule is what that story inherits.
  - `[false]` `[reject]` (intent) "Same hash" means "same claimed hash" — by design, see the blind finding on the hash.
  - `[false]` `[reject]` (intent) TestCluster coverage — the auditor reports the criterion met at the surface it names; no bad outcome.
  - `[false]` `[reject]` (intent) New quantity at a slot — reported as matched; the unretired old Sensor is the blind finding above.
  - `[false]` `[reject]` (intent) Operator clause — no criterion needs a human; reported as consistent.

## Design Notes

**Wire additions (all additive).**

```proto
enum Unit { UNIT_UNSPECIFIED = 0; UNIT_RAW_COUNT = 1; UNIT_MILLI_DEGREE_CELSIUS = 2; UNIT_MILLI_PERCENT = 3; UNIT_OHM = 4; }
message Specification {
  Quantity quantity = 1;  Unit unit = 2;            // unit of Reading.value
  sint64 range_min = 3;   sint64 range_max = 4;     // in `unit`
  bool calibration = 5;                             // two-point Calibration applies
  optional sint64 default_low = 6;                  // percent (0–100) when calibration, else `unit`
  optional sint64 default_high = 7;
}
message SpecificationSet { repeated Specification specifications = 1; }  // index = slot
// NodeFrame:  SpecificationSet specifications = 11;   Downlink:  bool specifications_unknown = 6;
```

**`spec_hash` is opaque to the Server.** The Node computes it (SHA-256 of its serialized set, 1 to 32 bytes on the wire); the Server only compares it with the hash of the last set it accepted from that Device. No canonical encoding is needed on the Server.

**"Not evaluated" in this story.** Evaluation arrives with Epic 6. What this story fixes is the fact evaluation will gate on: the Device grain's journaled Sensor list. A Reading whose `(slot, quantity)` is not in that list has no Sensor grain and no stream; its row is keyed by the Sensor ID a later declaration produces, so nothing has to be migrated.

**Invalid set.** It is ignored instead of rejecting the frame: a firmware fault in metadata must not block Readings. The flag keeps asking, and the warning makes the fault visible.

**Firmware.** No Rust code sends frames yet (Story 4.4 is in backlog). The simulator defines the Node's behaviour; 4.4 implements it against this contract.

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror` -- expected: no warnings or errors
- `dotnet format --verify-no-changes --no-restore` -- expected: no changes
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- expected: all tests pass, including `SpecificationGrainTests`, `IngestTests`, `IngestGrainTests`, `FixtureJournalReplayTests`
- `packages/proto/check-compat.sh --self-test && packages/proto/check-compat.sh --base "$(git merge-base HEAD origin/main)"` -- expected: pass
- `pnpm -r test` -- expected: pass (no generated file is stale)

## Auto Run Result

Status: done

**Summary.** A Node can now declare its Sensors to the Server. Every frame's `spec_hash` is compared with the hash of the last Specification set the Server accepted from that Device; when they differ, the sealed downlink says `specifications_unknown`. A frame that then carries a valid set declares each Sensor to a new event-sourced Sensor grain (`sensor/{sensorId}`) and journals the hash and the Sensor list on the Device. The Server side and the wire contract are done; the Node firmware side is not (see `deferred`).

**Files changed.**
- `packages/proto/coldframe/device/v1/envelope.proto`, `packages/proto/README.md` -- `Unit`, `Specification`, `SpecificationSet`, `NodeFrame.specifications = 11`, `Downlink.specifications_unknown = 6`; all additive.
- `packages/cs/contracts/Sensors/SensorGrains.cs`, `SensorEvents.cs` -- `ISensorGrain` (`Declare`, `Describe`), its records, and the events `sensor.declared`, `sensor.specification-changed`, `sensor.thresholds-changed`.
- `packages/cs/contracts/Devices/DeviceEvents.cs` -- `device.specifications-declared` with the hash and the Sensor list.
- `apps/cs/server/Sensors/SensorGrain.cs`, `SensorState.cs` -- the Sensor grain; Thresholds per side as `Default | Override | Cleared`.
- `apps/cs/server/Devices/NodeFrameReader.cs` -- returns the frame's hash and a validated or invalid set.
- `apps/cs/server/Devices/DeviceGrain.cs`, `DeviceState.cs` -- the known hash, the Sensor list, the declaration step and the downlink flag.
- `tests/cs/device-simulator/SimulatedDevice.cs` -- the Node's side: default set, hash, set attached once per request.
- `tests/cs/server.integration/Devices/SpecificationGrainTests.cs`, `IngestTests.cs`, `Identity/IdentityCluster.cs` -- 13 TestCluster tests, one AppHost test, and the `SensorFaults` test filter.
- `tests/cs/server.tests/` (`NodeFrameReaderTests.cs`, `Sensors/SensorStateTests.cs`, `Fixtures/journal.json`, `Journal/FixtureJournalReplayTests.cs`), `tests/cs/crypto.tests/SimulatorTests.cs` -- unit tests and fixture rows.
- `apps/cs/README.md` -- the "Sensor Specifications" section.

**Review.** 32 findings from four layers: 5 patched rows (4 fixes), 1 deferred, 26 rejected. Patched entries by verdict: high 0, medium 1, low 3. Every rejected finding and its reason is in the Review Triage Log.

**Follow-up review: not recommended.** One medium entry was patched (a missing test), no high.

**Verification (final tree).**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror` -- 0 warnings, 0 errors.
- `dotnet format --verify-no-changes --no-restore` -- no changes.
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- 669 passed, 0 failed.
- `packages/proto/check-compat.sh --self-test` and `--base <merge-base>` -- pass (pinned buf and oasdiff from `install-tools.sh`).
- `pnpm -r test` -- pass.
- Every row of the I/O matrix has a passing test in `SpecificationGrainTests`.

**Deviations.**
- Tests were written together with the implementation, not before it.
- `SimulatorTests.ANodeWakeIsTheNodeFrameOfTheVectors` clears the simulator's `spec_hash` before comparing with the vector, whose frame has none.

**Residual risks.**
- No firmware sends the hash or the set yet (deferred to Story 4.4). A frame with a set of four is about 200 bytes sealed, close to the ESP-NOW payload limit; a larger set would not fit one frame.
- A Sensor grain that cannot read its stream does not fail fast: `Ingest` waits for the 30 s grain call timeout before it answers `retry`. The frame's rows are committed by then, so the resend is a `duplicate`.
- A Sensor replaced at its slot stays describable as before; only the Device's Sensor list shows it is no longer part of the Node. Story 5.3 and Epic 6 must read membership from that list.
- The Server trusts the Node's hash and does not check that quantity, unit and `calibration` fit together.
