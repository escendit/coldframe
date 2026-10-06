---
title: 'Story 4.5: Server ingestion and acknowledgements'
type: 'feature'
created: '2026-10-06'
baseline_revision: '2f8e0b6b223b86240e886991b072f3e621224767'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
  - '{project-root}/apps/cs/README.md'
warnings: ['oversized']
deferred:
  - summary: >-
      A Node whose clock runs more than 5 minutes ahead is answered rejected_time on every frame and never receives the server time that would correct it.
    evidence: |-
      AD-9 gives a downlink only to stored and duplicate, and AD-11 makes the sealed downlink the Node's only time source. DeviceGrain.Ingest follows both, so a rejected_time frame carries no server_time_ms. The rule comes from the architecture, not from this story; it needs a decision there (for example a time-only downlink).
    location: >-
      apps/cs/server/Devices/DeviceGrain.cs (Ingest, rejected_time branch)
    severity: medium
  - summary: >-
      No test makes the journal append of DeviceRelayChanged fail, so the retry answer for that failure is unproven.
    evidence: |-
      IdentityCluster's FaultyIngestionStore can fail the replay load and the commit, but the fixture has no hook that fails a journal append. JournaledGrain with custom storage may also retry a failed write instead of throwing, in which case the catch in DeviceGrain.Ingest is never reached; a failing-journal fixture would settle both.
    location: >-
      apps/cs/server/Devices/DeviceGrain.cs (Ingest, relay change after the commit)
    severity: low
  - summary: >-
      A Node enrolled after a restore's recovery point and enrolled again afterwards starts its downlink counter at 0 under the same ack/v1 key.
    evidence: |-
      advance-replay moves the device_replay rows it finds and inserts rows for Devices with a device.enrolled event in the restored journal. A Device whose enrolment was lost with the restore has neither, so after re-enrolment (same K_dev, same Device ID) DeviceIngestionStore reserves counters from 0 again, repeating nonces the Server used before the restore.
    location: >-
      apps/cs/migrations/ReadingsMaintenance.cs (AdvanceReplayAsync)
    severity: medium
  - summary: >-
      The integration test host runs close to PostgreSQL's 100-connection limit.
    evidence: |-
      Before the ingestion suites were put in a serial xUnit collection, full runs failed twice with "too many clients"; the sampled peak is now 87 to 88 of 100. CommitOrderTests alone opens 34 connections. Raising max_connections on the AppHost's PostgreSQL would remove the risk.
    location: >-
      aspire/Coldframe.AppHost/AppHost.cs
    severity: low
---

<intent-contract>

## Intent

**Problem:** `POST /device/ingest` is a contract-only placeholder (octet-stream, 202, no handler), and the Node frame, the Reading, the acknowledged `reading_seq` ranges and the JSON envelope are not defined anywhere. Readings sent by a Node therefore have nowhere to go, and Story 4.4's firmware has no contract to build against.

**Approach:** Define the wire contracts (Node frame and Downlink ranges in `packages/proto`, envelope and per-frame statuses in `packages/openapi`, a Node-frame vector and the Sensor ID rule in `packages/crypto-spec`). Implement ingestion in the Device grain: open the seal, check the replay window, insert Readings and the device report exactly once into monthly-partitioned tables, and seal an `ack/v1` downlink only after the PostgreSQL commit. Add partition maintenance and the post-restore replay advance as commands of the migrations executable.

## Boundaries & Constraints

**Always:**
- AD-9 and AD-17 as written in `ARCHITECTURE-SPINE.md`. The Readings, device-report, key and replay tables are written only by the Device grain. The Edge handler only parses the envelope and calls grains.
- Order per frame: decode `SealedEnvelope` → resolve the Node's Device grain → open the seal with the `seal/v1` key → replay window → decode the Node frame → time check → pause gate → one PostgreSQL transaction → seal the downlink.
- One transaction per frame writes the Reading keys, Reading rows, the device report, the replay window and the reserved downlink counter. The downlink is sealed and returned only after that commit. A failed transaction changes neither the stored nor the in-memory replay state.
- Every downlink uses a fresh, committed downlink counter. A counter is never reused for a Device key, including after a silo restart or a restore.
- A resend (new frame counter, same `reading_seq` values) is `duplicate`, is acknowledged, and adds no row.
- HTTP 200 whenever the envelope parses, with one result per frame in request order. 400 for an unparseable envelope, 401 `device-unauthorized` for a failed Hub signature, 503 only when every frame ended in `retry`.
- `TimeProvider`/`Clock` only (wall clock is banned). No Reading payload, key or frame bytes in logs.
- Tests go through the Device simulator and `packages/crypto-spec` vectors, never hand-built payloads (AD-24). Tests are written first.
- Proto changes are additive. `acked_counter` and the existing vectors keep their meaning.

**Never:**
- No Sensor grains, Specification handling, `spec_hash` lookup or evaluation (Story 4.6). `spec_hash` is carried and ignored.
- No LotStatus, history API, or Node "last seen" (Stories 4.7, 4.8).
- No Pause API or Site Pause (Epic 8). Pause exists only as Device-grain events and state that tests seed.
- No Calibration (Epic 5). `calibration_id` is a nullable column, always null.
- No firmware, Hub relay or Rust codec work (Story 4.4). No change to `packages/rs` beyond keeping its tests green.
- No DDL from the Server process. No update or delete of a Readings or device-report row.
- No journal event per frame (DW-45). Do not write `sprint-status.yaml`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Stored | Enrolled Node, fresh counter, new `reading_seq` values, signed by an enrolled Hub | `stored`; rows in `readings` and `device_reports`; downlink opens with the Node's `ack/v1` key and carries `acked_counter`, `server_time_ms`, the frame's `reading_seq` ranges, empty `commands` | No error expected |
| Resend | Same Readings, new counter | `duplicate`; downlink acknowledges the same ranges; row counts unchanged | No error expected |
| Partly new | Frame mixing stored and new `reading_seq` values | `stored`; only the new rows are added; all ranges acknowledged | No error expected |
| Replay | The same sealed bytes twice, or a counter below the 64-entry window, or a seen slot | `rejected_replay`, no downlink, no row | Window unchanged |
| Out of order | Counter below the high-water mark in an unseen slot | Accepted and processed normally | No error expected |
| Tampered | Ciphertext, counter or Device ID altered; bad base64; not a `SealedEnvelope`; unsupported `protocol_version`; authentic but undecodable plaintext | `rejected_auth`, no downlink | Other frames in the envelope are unaffected |
| Unknown Device | Device ID not enrolled, or enrolled as a Hub | `unknown_device`, no downlink | No journal event, no row |
| Future time | Synced `measured_at` more than 5 min after the Server clock | `rejected_time`, no downlink, no row; the counter is consumed | No error expected |
| Unsynced, same boot | `time_unsynced` Reading whose `boot_id` equals the frame's | `measured_at` = receive time − (frame `uptime_ms` − Reading `uptime_ms`); row keeps `time_unsynced`, `boot_id`, `uptime_ms` | A negative difference is treated as 0 |
| Unsynced, earlier boot | Reading `boot_id` differs from the frame's | `measured_at` = receive time; row keeps the flag and the original values | No error expected |
| Paused Device | Device grain state has a Pause source | `stored` with a full downlink; no Reading, report or key row | No error expected |
| Unassigned Node | Enrolled Node without a Lot | Stored and acknowledged like any other | No error expected |
| Database failure | The transaction fails for one frame | `retry`, no downlink; in-memory replay state restored | 200 if another frame succeeded; 503 problem if all frames are `retry` |
| Crash after commit | Commit succeeds, the grain or silo dies before answering, the Node resends | `duplicate` with a downlink; one row per Reading; the new downlink counter is above the lost one | No error expected |
| Bad Hub signature | Wrong HMAC, stale timestamp, reused nonce, signer is a Node or not enrolled | 401 `device-unauthorized`; no frame is processed | — |
| Bad envelope | Not JSON, missing `frames`, more than 32 frames, body over 16 KiB | 400 problem | — |
| Empty envelope | `frames: []` | 200 with an empty result list | — |
| Relay change | Frame accepted through a Hub other than the recorded one | One `DeviceRelayChanged` event; none when the Hub is unchanged | Failure to persist gives `retry` |
| Month not prepared | `measured_at` in a month with no partition | The row lands in the default partition | The next partition run moves it into a new monthly partition |
| Restore | `advance-replay` run with the apps stopped | Every Device's high-water mark +64 with the window marked fully seen; downlink counter +1,048,576 | Non-zero exit and no partial change on failure |

</intent-contract>

## Code Map

- `packages/proto/coldframe/device/v1/envelope.proto` -- `SealedEnvelope`, `Downlink` (`acked_counter = 2`, `server_time_ms = 3`, `commands = 4`). Add the Node frame here; `buf lint` STANDARD and `buf breaking` FILE apply.
- `packages/proto/check-compat.sh` -- `buf breaking` and `oasdiff breaking --fail-on ERR` (`:154`). No exemption for planned operations today.
- `packages/openapi/coldframe.openapi.json:44` -- `deviceIngest` placeholder with `x-coldframe-planned: "4.x"`. `packages/openapi/scripts/generate-fixtures.ts` builds golden Hub fixtures (heartbeat only).
- `packages/crypto-spec/crypto-spec.json:16-42` -- labels, nonce `device_id[0..4] || counter_u64_be`, AAD, `replayWindow: 64`. `vectors.json`: `frames[0..3]` (placeholder plaintexts, `frames[3]` is a Downlink), `replay.steps`. Generated C# in `packages/cs/crypto/Generated/CryptoSpec.g.cs` (`pnpm --filter @coldframe/crypto-spec run generate`).
- `packages/cs/crypto/Frames.cs` -- `Frames.Seal` (`:38`), `Open` (`:44,51`), `ReplayWindow` (`:65-121`): `_seen` is private, no way to load, export or advance it.
- `packages/cs/crypto/DeviceKeys.cs` -- `KeyHierarchy.DerivePurposeKey` (`:78`), `DeviceKeys.FromDeviceKey` (`:147`): `SealKey`, `AckKey`, `HubAuthKey`.
- `packages/cs/protocol/Coldframe.Protocol.csproj` -- generates `Coldframe.Protocol.Device.V1`; the Server does not reference it yet.
- `packages/cs/contracts/Devices/DeviceGrains.cs:8` -- `IDeviceGrain` (`Enrol`, `Heartbeat`). `DeviceEvents.cs:27-64` -- `DeviceEnrolled`, `DeviceAssigned`, `DeviceSeen`.
- `apps/cs/server/Devices/DeviceGrain.cs` -- `Heartbeat` (`:130`), `Verify` (`:169-206`, unwraps `K_dev` through `DeviceKeyVault.Unwrap` and zeroes it). `DeviceState.cs` -- `Kind`, `LotId`, `LastHeartbeatTimestampMs`; no Pause, relay or replay state.
- `apps/cs/server/Journal/JournaledStreamGrain.cs:17`, `JournalStore.cs:53-116` -- journal append and the Npgsql transaction pattern (`NpgsqlDataSource`, no ORM).
- `apps/cs/server/Edge/EdgeApi.cs` -- `MapEdgeApi` (`:162-219`), `DeviceHeartbeatAsync` (`:469`), `ReadHeartbeatAsync` (`:495`), `ReadCappedBodyAsync` (`:555`), `device-unauthorized` (`:547`). `EdgeValidation.cs:32,177` -- body cap 4096 and `ParseDeviceHeaders`. `EdgeAccessRule.cs:85` -- `RequireDevice()`.
- `apps/cs/migrations/Program.cs:35` -- only `MigrateUp()`, no arguments. `Migrations/M<yyyyMMddHHmmss><What>.cs`, `ForwardOnlyMigration`, raw SQL through `Execute.Sql` (see `M20260928120100CreateTableJournalOutbox.cs`). `M20261006120000CreateTableDevices.cs` shows the Device ID column type.
- `deploy/charts/server/templates/migrations-job.yaml` -- the Helm job, no args. `aspire/Coldframe.AppHost/AppHost.cs:99-135` -- runs migrations before the Server.
- `docs/operations/restore.md:131-136,198-200` -- step 5 is a no-op "until Story 4.5"; the Fleet paragraph lets the apps start before it.
- `tests/cs/device-simulator/SimulatedDevice.cs` -- `SealFrame(payload)` (`:175`), `OpenDownlink` (`:189`), `SignHeartbeat(method, path, body)` (`:122`); no Node frame, envelope or ingest helper.
- `tests/cs/server.integration` -- `AppHostFixture.cs`, `Edge/EdgeApiFixture.cs` (`AppendAsync`, `Database`), `Identity/IdentityCluster.cs` (TestCluster with `FakeTimeProvider`, `Device(id)`), `Devices/HeartbeatTests.cs` (model), `Devices/Vectors.cs`, `MigrationTests.cs:28` (`ColdframeTables`), `Edge/AuthorizationMatrixTests.cs:78-80,103` (`_samples`).
- `tests/cs/server.tests/Edge/EdgeEndpointDiscoveryTests.cs:71` -- fails if a planned operation is mapped; `Edge/EdgeEndpointCatalog.cs:37`; `Fixtures/journal.json` needs a row per new `[EventType]`.
- `packages/rs/sensing/src/wake.rs:56-135` -- firmware shapes the frame must carry: `MeasuredAt::{Synced, Unsynced{boot_id, uptime_ms}}`, `Reading{slot, seq, value}`, slots 0–3 (soil raw count, milli-°C, milli-%RH, Ω), `WakeReport{battery, charging}`.
- `.github/workflows/ci.yml:123-154,408-444` -- the .NET and contracts jobs.

## Tasks & Acceptance

**Execution:**
- [x] `packages/proto/coldframe/device/v1/envelope.proto` -- add `NodeFrame`, `Reading`, `Quantity`, `ChargeStatus`, `ReadingSeqRange` and `Downlink.acked_readings = 5` as in Design Notes -- the contract Story 4.4 builds against.
- [x] `packages/crypto-spec/crypto-spec.json`, `vectors.json`, generator and generated outputs -- add the Sensor ID rule (namespace, name format, one vector), a sealed `NodeFrame` vector and a Downlink vector with ranges; keep the Rust and C# vector tests green -- identical derivation on both sides (AD-19, AD-12).
- [x] `packages/openapi/coldframe.openapi.json`, `packages/openapi/scripts/generate-fixtures.ts`, `packages/ts/api-client/src/schema.ts` -- replace the placeholder with the JSON envelope, the 200 response with per-frame `status` and optional `downlink`, 400/401/503; remove `x-coldframe-planned`; add golden ingest fixtures; regenerate the client schema -- AD-9 contract.
- [x] `packages/proto/check-compat.sh` -- skip the breaking check for an operation that carries `x-coldframe-planned` in the baseline, with a self-test case -- the placeholder was never served, so replacing it is not a break.
- [x] `packages/cs/crypto/Frames.cs` -- let `ReplayWindow` be created from, and export, `(highest, seen bitmap)`, and advance by a margin marking the window seen; cover with unit tests against `replay.steps` -- the window must survive restarts and restores.
- [x] `apps/cs/migrations/Migrations/` -- one migration creating `readings` and `device_reports` (range-partitioned by `measured_at`, each with a default partition), `reading_keys` and `device_replay` as in Design Notes -- storage (AD-9, AD-22).
- [x] `apps/cs/migrations/Program.cs` (and a small command class beside it) -- commands: no argument = migrate then ensure partitions; `partitions [--months-ahead N]` (default 3, minimum 2) creates the current and following monthly partitions for both tables idempotently and moves default-partition rows of a month into its new partition in one transaction; `advance-replay [--uplink-margin 64] [--downlink-margin 1048576]`; unknown arguments exit non-zero -- AD-22 and AD-15.
- [x] `deploy/charts/server/` -- a daily CronJob running `partitions` with the migrations image and the job's database secret, with the chart's existing test coverage extended -- partitions stay two months ahead without an upgrade.
- [x] `packages/cs/contracts/Devices/` -- `IDeviceGrain.AuthenticateRelay` and `IDeviceGrain.Ingest`, their result types, and events `DeviceRelayChanged`, `DevicePaused`, `DeviceResumed` (source `device|site`, optional end date); add rows to `tests/cs/server.tests/Fixtures/journal.json` -- grain surface and Pause fixture (AD-8).
- [x] `apps/cs/server/Devices/` -- an ingestion store (Npgsql) owned by the Device grain, state for `PausedBy`, `LastRelayHubId`, and `DeviceGrain.Ingest` / `AuthenticateRelay` following the Always order; replay state is loaded from `device_replay` on first use -- the core of the story.
- [x] `apps/cs/server/Edge/EdgeApi.cs`, `EdgeValidation.cs` -- map `POST /device/ingest` with `RequireDevice()`, a 16 KiB cap, at most 32 frames, Hub authentication through the signer's grain, frames of one Device processed in request order -- the endpoint.
- [x] `tests/cs/device-simulator/SimulatedDevice.cs` -- build and seal Node frames (synced, unsynced, battery, resend with a new counter), build the ingest envelope, sign and post it, open downlinks and read ranges -- AD-24.
- [x] `tests/cs/server.integration/Devices/IngestTests.cs` (AppHost) and `IngestGrainTests.cs` (TestCluster) -- every matrix row; `retry`, Pause, clock-dependent rows and crash-after-commit (commit, restart the silo without using the answer, resend) on the TestCluster -- acceptance.
- [x] `tests/cs/server.integration/MigrationTests.cs`, `MigrationJobExitCodeTests`, `Edge/AuthorizationMatrixTests.cs`, `tests/cs/server.tests/Edge/*` -- new tables, partitions two months ahead plus defaults, `advance-replay` effect, the ingest sample (401 for every role), discovery of the now-mapped operation -- guards.
- [x] `docs/operations/restore.md`, `apps/cs/README.md`, and the chart restore smoke if it runs the restore steps -- document `advance-replay`, its margins and their effect on Nodes; rewrite the Fleet paragraph so the apps start only after step 5; document `partitions` -- operator contract.

**Acceptance Criteria:**
- Given an envelope with three frames where the second is tampered, when a Hub posts it, then the response is 200 with `stored`, `rejected_auth`, `stored` in that order and downlinks only on the first and third.
- Given a stored frame, when its downlink is opened by the simulator with the Node's `ack/v1` key, then `server_time_ms` is within the test clock tolerance, `acked_readings` covers exactly the frame's `reading_seq` values including the report sequence, and `commands` is empty.
- Given a fresh database after the migration job, when the catalog is read, then `readings` and `device_reports` each have partitions for the current month and at least the two following months, and a default partition.
- Given a Device with stored replay state, when `advance-replay` runs and the Server starts, then a frame with a counter at or below the old high-water mark + 64 is `rejected_replay`, a higher counter is accepted, and the next downlink counter is at least 1,048,576 above the last one used.
- Given the contract checks, when `packages/proto/check-compat.sh --self-test` and the base comparison run, then both pass.
- Given the .NET CI commands, when they run, then build with `-warnaserror`, `dotnet format --verify-no-changes` and `dotnet test` pass.

## Spec Change Log

## Review Triage Log

### 2026-10-06 — Review pass
- verdicts: 45 findings — high 0, medium 8, low 27, false 10, maybe-false 0
- findings:
  - `[medium]` `[defer]` (blind) A Node with a fast clock never recovers: `rejected_time` carries no downlink, the only time source — real, but AD-9 and AD-11 prescribe it; recorded in `deferred` for an architecture decision.
  - `[low]` `[reject]` (blind) An unknown `Quantity` drops the whole frame as `rejected_auth` — real only for firmware newer than the Server, which AD-23 rules out (the Server ships first); skipping the Reading would acknowledge data that was not stored.
  - `[medium]` `[patch]` (blind) Rows older than the current month stay in the default partition forever, against the docs — `EnsurePartitionsAsync` now also creates the partition of every month with rows waiting in a default partition; test `ARowOfAMonthLongPastLeavesTheDefaultPartitionOnTheNextRun`.
  - `[medium]` `[patch]` (blind) `SaveReplaySql` overwrites the stored window unconditionally — the upsert now updates only when the stored `high_water` is at or below the written one, otherwise the commit throws (`retry`, window reloaded); test `AWindowAdvancedUnderneathALiveGrainIsNotOverwritten`.
  - `[low]` `[patch]` (blind) `advance-replay` margins have no upper bound — margins above 4294967296 are a usage error (exit 2); `JobCommandTests`.
  - `[false]` `[reject]` (blind) `IsPaused` ignores `EndsAt` — nothing in the product raises `DevicePaused` yet; the events exist only as a test fixture, and how a Pause ends (event or clock) is Epic 8's design.
  - `[low]` `[reject]` (blind) A Node alternating between two Hubs journals an event per frame — needs a Node that changes its relay on every wake; a frame heard by two Hubs is `rejected_replay` on the second and journals nothing. Hysteresis would add state for a case not shown to occur.
  - `[low]` `[patch]` (blind) The relay change is journaled before the frame commits — now journaled after the successful commit, before the downlink is sealed; the failed-commit test asserts no relay change.
  - `[false]` `[reject]` (blind) Any enrolled Hub can relay and probe Nodes of other Sites — the epic states "any enrolled Hub may relay any Node", and `unknown_device` is a contract status; downlinks are sealed for the Node.
  - `[low]` `[reject]` (blind) The ingest size limits do not fit together (32 × 1024 characters exceeds 16 KiB) — each limit is documented and enforced on its own; a relayed ESP-NOW frame is about 340 base64 characters, so 32 real frames fit.
  - `[low]` `[patch]` (blind) Ingest failures are invisible in the Edge handler — the catch now logs a warning with the Device ID and the exception. Per-status counters and `Retry-After` were not added (no requirement names them).
  - `[false]` `[reject]` (blind) `reading_keys` has no retention — Readings are retained indefinitely by requirement, so their keys are too; no partition is ever dropped.
  - `[low]` `[patch]` (blind) `retry` does not say whether to resend the same bytes — the OpenAPI descriptions now say every resend is freshly sealed and that neither `retry` nor `rejected_replay` lets a Node drop Readings; client schema regenerated.
  - `[low]` `[reject]` (blind) `EnsurePartitionAsync` treats any relation with the partition's name as done — needs an operator to detach or hand-create a table of that name; a `pg_inherits` check would still leave the name taken.
  - `[low]` `[patch]` (blind) The runbook's one-off Job can run twice and hard-codes the CronJob name — the jq step in `restore.md` and `smoke.sh` now sets `backoffLimit` 0. The name `server-partitions` matches the runbook's other fixed names (`deployment/server`) and was kept.
  - `[low]` `[reject]` (blind) The restore advance exists twice (`ReplayWindow.Advance` and SQL) — the SQL is pinned by an integration test with exact values; `Advance` is unused in production but harmless and specified by this spec's task list.
  - `[low]` `[patch]` (edge) A lone surrogate in a frame string answers 500 — `ParseIngestBody` also catches `InvalidOperationException`; two cases added to `BadEnvelopes`.
  - `[false]` `[reject]` (edge) A throwing Hub grain answers 500 instead of 503 — nothing was processed, and AD-9 asks for a 5xx in that case; the heartbeat endpoint behaves the same.
  - `[low]` `[reject]` (edge) A long envelope keeps committing after the Hub hangs up — intended: a commit is never abandoned, and the Node's resend is a `duplicate` with a fresh acknowledgement.
  - `[low]` `[reject]` (edge) A key that cannot be unwrapped is reported as `rejected_auth`, unlogged — needs a misconfigured key-encryption key; the Node keeps its Readings, and a separate branch would add a path for a deployment fault.
  - `[low]` `[patch]` (edge) Relay change journaled before the commit — same defect as the blind finding; fixed with it.
  - `[false]` `[reject]` (edge) A Hub of another Site can relay a Node — intended, see the blind finding.
  - `[false]` `[reject]` (edge) `IsPaused` ignores `EndsAt` — see the blind finding.
  - `[low]` `[patch]` (edge) A clamped downlink counter overflows the cast in `CommitAsync` — unreachable now that margins are capped at 4294967296.
  - `[low]` `[patch]` (edge) `ParseMargin` accepts any ulong — capped, see the blind finding.
  - `[medium]` `[patch]` (edge) `advance-replay` under a live grain is undone by the next frame — guarded, see the blind finding.
  - `[low]` `[patch]` (edge) The one-off Job inherits `backoffLimit: 1` — set to 0 in the runbook and the smoke.
  - `[medium]` `[patch]` (edge) Rows before the first partition never leave the default partition — fixed, see the blind finding.
  - `[low]` `[reject]` (edge) No lower bound on `measured_at` — the intent sets only the future bound; a past bound would refuse a Node's genuinely old buffered Readings. Such rows now get their month's partition on the next run.
  - `[low]` `[reject]` (edge) `to_regclass` check accepts a detached relation — see the blind finding.
  - `[low]` `[reject]` (edge) The chart accepts `partitions.monthsAhead` above 120 — needs a value of more than ten years; the job then exits 2 and says why.
  - `[false]` `[reject]` (edge) The job no longer takes configuration from arguments — deployed services are configured by environment variables only (architecture conventions); the AppHost and the chart pass none.
  - `[medium]` `[patch]` (gap) `retry` when the replay state cannot be read is never exercised — `FaultyIngestionStore.FailNextLoads` and test `WhenTheReplayWindowCannotBeReadTheFrameIsRetryAndNothingChanges`. The failing relay-journal half is recorded in `deferred` (the fixture cannot fail a journal append).
  - `[medium]` `[patch]` (gap) The Edge handler's "a grain that cannot answer is `retry`" has no test — the frame loop is now `EdgeApi.IngestFramesAsync`; test `ANodeGrainThatThrowsIsRetryForItsFrameOnly`.
  - `[low]` `[patch]` (gap) `NodeFrameReader` limits are tested only from the rejecting side — test `AFrameExactlyAtTheLimitsIsValid` (64 Readings, slot 255, battery 100).
  - `[low]` `[patch]` (gap, other) Relay change journaled before the commit — same defect as above; fixed with it.
  - `[low]` `[reject]` (gap, other) `ReplayWindow.Advance` has no production caller — see the blind finding.
  - `[low]` `[reject]` (gap, other) `NoPlannedOperationIsMappedYet` passes vacuously — the contract has no planned operation left; the test guards again as soon as one is added.
  - `[medium]` `[patch]` (intent) Several status paths are proven by grain calls, not through the AppHost endpoint — `rejected_time` now has an AppHost test (`ATimeTenMinutesAheadIsRejectedTimeAndTheNextValidFrameIsStored`). `retry`, the 503, Pause and crash-after-commit stay on the TestCluster: the AppHost has no fault injection or clock control, and a Pause cannot be seeded into a live grain.
  - `[false]` `[reject]` (intent) The integration tests do not read the vectors directly — they go through the Device simulator, which `crypto.tests` proves byte-identical to the vectors; AD-24 asks for exactly that.
  - `[low]` `[reject]` (intent) The documented restore pipeline is run only by the chart smoke, which does not check the counters — the command's effect is asserted against a database in `ReadingsMaintenanceTests` and at the grain in `AfterAdvanceReplayOldCountersAreRejectedAndTheDownlinkCounterJumps`.
  - `[low]` `[reject]` (intent) The CronJob is never observed firing — its manifest is unit-tested and the `partitions` command runs as a process in `TheJobRunsItsCommandsAgainstADatabase`; waiting for a schedule in CI would add a slow test for Kubernetes' own behaviour.
  - `[false]` `[reject]` (intent) Semantic choices (paused frame answers `stored`, undecodable plaintext is `rejected_auth`, relay as an event, `server_time_ms`) — each follows the intent contract's matrix and AD-8, AD-9, AD-11.
  - `[false]` `[reject]` (intent) `packages/rs/crypto/src/spec.rs` changed — it is the crypto-spec generator's output; the freshness check fails without it.
  - `[low]` `[reject]` (intent) The crash test restarts the silo after the grain call returned, not during it — the stored state is the same in both cases (commit durable, answer lost), which is what the resend depends on.

## Design Notes

**Node frame (plaintext of an uplink `SealedEnvelope`).** One frame is one wake report, so `measured_at` is shared.

```proto
message NodeFrame {
  uint32 protocol_version = 1;
  bytes spec_hash = 2;                 // carried; used from Story 4.6
  uint64 boot_id = 3;                  // the boot sealing this frame
  uint64 uptime_ms = 4;                // uptime when sealed
  oneof measured { int64 measured_at_ms = 5; Unsynced unsynced = 6; }  // Unsynced{boot_id, uptime_ms}
  uint64 report_seq = 7;               // from the reading_seq counter; keys the device report
  repeated Reading readings = 8;       // Reading{slot, reading_seq, quantity, sint64 value}
  optional uint32 battery_percent = 9;
  ChargeStatus charging = 10;
}
```

`Quantity` is `SOIL_MOISTURE | AIR_TEMPERATURE | RELATIVE_HUMIDITY | GAS_RESISTANCE`; `value` is the firmware's raw integer for that quantity. Carrying the quantity lets both sides compute the AD-19 Sensor ID before any Specification exists: `sensorId = UUIDv5(ns, "{deviceIdHex}:{slot}:{quantity}")` with `ns = UUIDv5(URL namespace, "https://github.com/escendit/coldframe/sensor")` and lower-snake quantity tokens.

**Exactly-once across partitions.** A unique index on a partitioned table must contain the partition key, and a rebased `measured_at` differs between resends. `reading_keys (device_id, sensor_id, reading_seq)` (not partitioned, primary key) is inserted with `ON CONFLICT DO NOTHING`; a Reading row is inserted only for a key that was new. The device report uses the nil UUID as `sensor_id` and `report_seq` as `reading_seq` ("the same keys"). `stored` means at least one key was new.

**Replay state lives in `device_replay`**, not in the journal: `device_id` primary key, `high_water numeric(20,0)`, `seen bigint` (64-bit bitmap), `downlink_counter numeric(20,0)`. It commits with the Readings, which gives crash safety, and it lets `advance-replay` be plain SQL while the apps are stopped.

**Restore margins.** Uplink +64 with the window fully seen: a Node's next frames up to that margin answer `rejected_replay`; it resends with higher counters and its 24 h buffer covers the gap. Downlink +1,048,576: far above the acknowledgements one Node can receive between a backup and a restore.

**Hub authentication for ingest** reuses the heartbeat HMAC check (path-bound, 5 min skew, in-memory nonce set) but raises no event; frames carry their own replay protection. Any enrolled Hub may relay any Node.

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror` -- expected: no warnings or errors
- `dotnet format --verify-no-changes --no-restore` -- expected: no changes
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- expected: all tests pass, including `IngestTests`, `IngestGrainTests`, `MigrationTests`, `AuthorizationMatrixTests`
- `packages/proto/check-compat.sh --self-test && packages/proto/check-compat.sh --base "$(git merge-base HEAD origin/main)"` -- expected: pass
- `pnpm --filter @coldframe/crypto-spec run check && pnpm --filter @coldframe/openapi run check && pnpm -r test` -- expected: pass, no stale generated files
- `cargo test --workspace` (host targets) -- expected: crypto vector tests pass with the new vectors
- `./gradlew :core:jvmTest` -- expected: the OpenAPI contract test passes

## Auto Run Result

Status: done

**Summary.** `POST /device/ingest` is implemented end to end on the Server. A Hub posts a JSON envelope of base64 sealed Node frames; each Node's Device grain opens the seal, checks the 64-entry replay window, stores Readings and the device report exactly once, and seals an `ack/v1` downlink only after the PostgreSQL commit. The story also defines the wire contracts Story 4.4 will build against (Node frame, acknowledged `reading_seq` ranges, envelope and per-frame statuses, Sensor ID rule), adds partition maintenance and the post-restore `advance-replay` command to the migration job, and rewrites restore step 5.

**Files changed.**
- `packages/proto/coldframe/device/v1/envelope.proto` -- `NodeFrame`, `Reading`, `Quantity`, `ChargeStatus`, `ReadingSeqRange`, `Downlink.acked_readings`.
- `packages/crypto-spec/*`, generated `CryptoSpec` in C#, Kotlin and Rust, `tests/ts/crypto-spec` -- Sensor ID rule, Node-frame and Downlink-with-ranges vectors.
- `packages/openapi/*`, `packages/ts/api-client/src/schema.ts` -- the ingest envelope, per-frame statuses, 400/401/503, six golden Hub fixtures.
- `packages/proto/check-compat.sh` -- operations planned in the baseline are not compared.
- `packages/cs/crypto/Frames.cs`, `SensorIds.cs` -- a replay window that can be stored and reloaded; UUIDv5 Sensor IDs.
- `packages/cs/contracts/Devices/*` -- `AuthenticateRelay`, `Ingest`, `DeviceRelayChanged`, `DevicePaused`, `DeviceResumed`.
- `apps/cs/server/Devices/*` -- `DeviceGrain.Ingest`, `DeviceIngestionStore`, `NodeFrameReader`, Pause and relay state.
- `apps/cs/server/Edge/*` -- the endpoint, envelope validation, `ingest-unavailable` problem.
- `apps/cs/server/Dockerfile`, `Coldframe.Server.csproj` -- the Server now builds the protocol project.
- `apps/cs/migrations/*` -- tables `readings`, `device_reports`, `reading_keys`, `device_replay`; commands `partitions` and `advance-replay`.
- `deploy/charts/server/*`, `deploy/charts/smoke.sh` -- daily partition CronJob, `stopped` value, restore step 5 in the smoke.
- `docs/operations/restore.md`, `apps/cs/README.md`, package and chart READMEs -- operator and developer documentation.
- `tests/cs/*` -- simulator helpers; `IngestTests` (AppHost), `IngestGrainTests` (TestCluster), `ReadingsMaintenanceTests`, unit tests for the request, the frame reader and the job commands; guards updated.

**Review.** 45 findings from four layers: 19 patched rows (12 fixes in 12 entries), 1 deferred, 25 rejected. Three more items are in `deferred`: the untested half of one patched gap finding and two risks the implementation surfaced. Patched entries by verdict: high 0, medium 5, low 7. Every rejected finding and its reason is in the Review Triage Log above.

**Follow-up review: recommended.** More than one medium entry was patched, and the patches were verified by tests but not reviewed: the guard that refuses a stale replay window, the move of the relay-change event to after the commit, and the sweep of past months out of the default partition.

**Verification (final tree).**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror` -- 0 warnings, 0 errors.
- `dotnet format --verify-no-changes --no-restore` -- no changes.
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- 644 passed, 0 failed, 0 skipped.
- `packages/proto/check-compat.sh --self-test` and `--base <merge-base>` -- pass.
- crypto-spec and openapi `check`, `pnpm -r test` -- pass.
- `cargo test --workspace`, `./gradlew :core:jvmTest` -- pass.
- `helm lint --strict` and `helm unittest` on the server chart -- 33 passed.
- Matrix audit: every row of the I/O matrix has a passing test.

**Decisions made by this run.**
- Each Reading carries its quantity, so the Sensor ID is computable before Specifications exist (Story 4.6).
- Exactly-once uses the unpartitioned `reading_keys` table; the device report is keyed by a `report_seq` from the `reading_seq` counter.
- Replay state lives in `device_replay`, not in the journal.
- Restore margins: uplink +64, downlink +1,048,576.
- Frames are standard padded base64, at most 1024 characters, 32 per envelope, 16 KiB per body.
- The server chart has a `stopped` value so a Fleet redeploy can keep the Server down until restore step 5 has run.

**Residual risks.**
- `deploy/charts/smoke.sh` now runs `advance-replay` as a one-off Job; it was only syntax-checked locally and first runs in CI.
- The partition CronJob fires at 03:17 UTC; a run during the smoke's restore window would fail and could trip its stack check.
- Story 4.4 is not implemented, so no firmware has exercised these contracts; the Rust side has the Sensor ID constants but no derivation.
- Pause has no producer and no expiry handling until Epic 8.
- The four `deferred` items above.
