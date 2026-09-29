---
title: 'Story 1.2: Event journal, migrations and projection pipeline'
type: 'feature'
created: '2026-09-28'
status: 'done'
baseline_revision: '4dff9cede2fc6bb165fc4b2b9333c1afe179db5c'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md'
warnings:
  - oversized
deferred:
  - summary: 'AD-21 asks for journal snapshots on a fixed event interval; this story has no acceptance criterion for them and no grain yet has a long stream, so the CustomStorage read replays the full stream.'
    evidence: 'Story 1.2 acceptance criteria in epics.md (lines 527-561) name append, outbox, projectors, polling, time and replay, not snapshots.'
    location: 'apps/cs/server/Journal/'
    severity: 'low'
  - summary: 'Escendit.Orleans.Migrations.Cluster.PostgreSQL 10.3.1-rc.1 is published; the architecture and the story pin 10.3.1-rc.0, which this story keeps.'
    evidence: 'nuget.org flat container index lists 10.3.1-rc.0 and 10.3.1-rc.1 (checked 2026-09-28).'
    location: 'Directory.Packages.props'
    severity: 'low'
---

<intent-contract>

## Intent

**Problem:** The Server has an Orleans silo on localhost clustering and nothing durable: no cluster schema, no event journal, no outbox, no projectors, and no rule that keeps wall-clock time out of Server code. Every grain in Stories 1.6, 1.7 and 1.9 needs these to persist and project state the same way (AD-2, AD-21, AD-22, AD-6, NFR-3).

**Approach:** Add a forward-only FluentMigrator set, run by a separate migration job before the silo starts, that creates the Orleans cluster schema (from the pinned Escendit package) and Coldframe's journal, outbox and checkpoint tables. Add a CustomStorage journal for `JournaledGrain`s, an event-type registry with upcasters, a polling projection runner with checkpoints and optional Orleans stream wake-up hints, an injected `TimeProvider` with a build-failing ban on wall-clock reads, and tests that prove each acceptance criterion with a test-only sample grain and projector.

## Boundaries & Constraints

**Always:**
- Test-first: each acceptance criterion gets a failing test before its implementation.
- Pins through Central Package Management with lock files: FluentMigrator, `FluentMigrator.Runner.Postgres`, `FluentMigrator.Extensions.Postgres` 8.0.1; `Escendit.Orleans.Migrations.Cluster.PostgreSQL` 10.3.1-rc.0; Npgsql 10.0.3; Orleans packages 10.3.1 (add `Microsoft.Orleans.TestingHost`); `Microsoft.Extensions.TimeProvider.Testing` 10.10.0. No `PackageReference` carries a version.
- One migration set, one runner, one version table: the cluster package's migrations and Coldframe's own run together. Coldframe migrations derive from `ForwardOnlyMigration`.
- The Server project never references FluentMigrator; only the migration job does. The Server never runs DDL.
- Journal row: stream ID, version, type alias, schema version, `jsonb` payload (System.Text.Json), `recorded_at` (UTC from `TimeProvider`), global position. `(stream_id, version)` is unique. Event rows and their outbox rows commit in one transaction.
- Global positions become visible in commit order (see Design Notes), so a projector reading `position > checkpoint` never skips an event.
- A projector applies events in global-position order and writes its read model and its checkpoint in the same transaction; applying an event it has already applied is a no-op.
- Projectors converge with streams disabled. Stream messages carry no event data; they only wake the runner.
- Server code reads time only from an injected `TimeProvider`; the Server registers `TimeProvider.System` with `TryAdd` so any host can substitute `FakeTimeProvider`.
- Tests wait on journal positions or checkpoints with a bounded timeout, never `Task.Delay`/`Thread.Sleep` as a wait.
- Existing tests in `tests/cs/server.integration` keep passing.

**Never:**
- No domain grains, domain events or read models (Site, Lot, User, … belong to Stories 1.6–1.9). The sample grain, its events and its projector live in test code only.
- No journal snapshots (deferred above), no Readings tables or partitions (AD-9, Epic 4), no Helm Job (Epic 2).
- No DDL at application startup, including `EnsureCreated`-style helpers. Test code may create databases and a sample read-model table.
- No `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow` or `DateTime.Today` in `apps/cs` or `packages/cs`.
- Never write `sprint-status.yaml`. Never push or open a pull request.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Fresh database | empty PostgreSQL database | job exits 0; Orleans and Coldframe tables exist | — |
| Re-run | already migrated database | job exits 0, applies nothing | — |
| Concurrent writer | append with a stale expected version | no row written; CustomStorage returns `false` so Orleans re-reads | unique-violation is caught, not surfaced |
| Old event version | journal row with schema version 1 of a type now at 2 | read through the registered upcaster, grain state as if v2 | — |
| Unknown alias | journal row whose alias has no registered type | read fails with an exception naming alias and schema version | fixture replay test fails |
| Duplicate apply | projector sees an event at or below its checkpoint | no read-model change | — |
| Streams off | no stream provider configured | projector catches up on the next poll | — |

</intent-contract>

## Code Map

- `apps/cs/server/Program.cs` -- composition root; `AddServiceDefaults().AddSilo().AddHealthCheckDefaults(...)`. Add journal and time registration here.
- `apps/cs/server/Hosting/SiloExtensions.cs` -- `UseLocalhostClustering` with a comment promising Story 1.2; replace with ADO.NET clustering and reminders (invariant `Npgsql`, connection string `coldframe`), and the NATS stream provider when streams are enabled.
- `apps/cs/server/Hosting/ServiceDefaultsExtensions.cs` -- local shim for the Escendit defaults (DW-2: the package extends only `HostApplicationBuilder`). The migration job is a plain `HostApplicationBuilder` and can call the package's `AddServiceDefaults()` directly.
- `aspire/Coldframe.AppHost/AppHost.cs` -- defines `postgres`, database `coldframe`, `nats` with JetStream, and `server` with `.WaitFor(...)`. Add the migration job and make the server `.WaitForCompletion(...)` it.
- `tests/cs/server.integration/AppHostFixture.cs` -- one AppHost per test assembly, `WaitForRunningAsync`, `WaitForHealthyAsync`, `GetResource`. Reuse its PostgreSQL for journal tests (`App.GetConnectionStringAsync("coldframe")`).
- `Directory.Packages.props`, `Directory.Build.props`, `Coldframe.slnx` -- CPM, transitive pinning, lock files, warnings as errors; add new projects to the solution.
- `.github/workflows/ci.yml` -- the `dotnet` job runs `dotnet test --no-build` over the solution; new test projects are picked up without workflow changes.
- Package facts (decompiled with ilspycmd):
  - `Escendit.Orleans.Migrations.Cluster.PostgreSQL` 10.3.1-rc.0: `services.AddClusterMigrationRunner(connectionString)` (also on any `IHostApplicationBuilder`, connection string name `orleans`) calls `AddFluentMigratorCore()`, registers `IVersionTableMetaData` = `OrleansVersionTableMetadata` (table `versions`, default schema) and `ConfigureRunner(r => r.AddPostgres15_0().WithGlobalConnectionString(cs).ScanIn(asm).For.EmbeddedResources().WithMigrationsIn(asm))`. Add Coldframe's assembly with a second `ConfigureRunner(r => r.ScanIn(ours).For.Migrations())`/`WithMigrationsIn(ours)`; migration version numbers must not collide with the package's (`2023…`, `20260926…`).
  - It creates `orleansquery`, `orleansstorage`, `orleansmembershiptable`, `orleansmembershipversiontable`, `orleansreminderstable`; no streaming or grain-directory scripts.
  - `Microsoft.Orleans.Streaming.NATS` 10.3.1-alpha.1: `siloBuilder.AddNatsStreams(name, Action<NatsOptions>)`; `NatsOptions.StreamName`, `NatsClientOptions` (`NatsOpts`, URL from the Aspire `nats` connection string). Use implicit subscriptions so no `PubSubStore` is needed.
  - `FluentMigrator` 8.0.1 ships `ForwardOnlyMigration`.

## Tasks & Acceptance

**Execution:**
- [x] `tests/cs/server.tests/` -- new xUnit v3 project (no containers): event registry and upcaster tests, fixture journal replay (`Fixtures/journal.json`), `TimeProvider` substitution test, and an IL scan that fails when `Coldframe.Server` or `Coldframe.Contracts` reference the banned `DateTime`/`DateTimeOffset` getters -- written first, seen failing
- [x] `tests/cs/server.integration/Journal/` -- `JournalDatabase` helper (fresh database per test class on the AppHost PostgreSQL, migrated by the same runner the job uses), `JournalWait` helper (bounded wait on a position or checkpoint), test-only `SampleGrain` (`JournaledGrain` with CustomStorage), its events (one with a v1 and a v2), a sample projector with a test-created read-model table, and tests for every acceptance criterion below on a `TestCluster` with `FakeTimeProvider` -- written first, seen failing
- [x] `tests/cs/server.integration/MigrationTests.cs` -- on the AppHost: the migration resource finishes with exit code 0 before the server runs, and the Orleans and Coldframe tables exist in `coldframe`
- [x] `packages/cs/contracts/Coldframe.Contracts.csproj` -- `EventTypeAttribute(alias, schemaVersion)` and the upcaster contract that later domain events use
- [x] `apps/cs/migrations/` -- console `Coldframe.Migrations` project: `Migrations/` with forward-only migrations for `journal_events`, `journal_outbox`, `projection_checkpoints`; one public registration method used by both the job and the tests; `Program.cs` runs `MigrateUp()` and exits non-zero on failure
- [x] `apps/cs/server/Journal/` -- event registry and serializer, `JournalStore` (append with outbox in one transaction, read a stream in order, read from a global position), CustomStorage base for `JournaledGrain`s, projection runner (hosted service per projector, poll interval via `TimeProvider`, woken by hints), outbox dispatcher that publishes hints when streams are enabled
- [x] `apps/cs/server/Hosting/SiloExtensions.cs`, `apps/cs/server/Program.cs` -- ADO.NET clustering and reminders, CustomStorage log-consistency provider, NATS hint stream behind one seam and switchable off by configuration, `TryAddSingleton(TimeProvider.System)`, journal registration
- [x] `apps/cs/Directory.Build.props`, `packages/cs/Directory.Build.props`, `BannedSymbols.txt` -- import the root props and enable `Microsoft.CodeAnalysis.BannedApiAnalyzers` for the banned getters, so the build fails at compile time too
- [x] `aspire/Coldframe.AppHost/AppHost.cs` -- `migrations` project resource referencing `coldframe`; `server` waits for its completion
- [x] `Directory.Packages.props`, `Coldframe.slnx`, `packages.lock.json` files -- new pins and projects
- [x] `docs/quickstart.md`, `apps/cs/README.md`, `packages/cs/README.md` -- the migration job, how to add a migration, an event type, an upcaster and a projector

**Acceptance Criteria:**
- Given an empty PostgreSQL database, when the migration job runs, then the Orleans clustering, persistence and reminders tables and `journal_events`, `journal_outbox` and `projection_checkpoints` exist, every Coldframe migration is a `ForwardOnlyMigration`, and the `Coldframe.Server` assembly references no FluentMigrator assembly.
- Given the AppHost, when it starts, then the migration job finishes with exit code 0 before the server starts, and the server health endpoint is healthy on ADO.NET clustering.
- Given the sample grain on CustomStorage, when it raises an event, then one `journal_events` row holds its stream ID, version, alias, schema version, System.Text.Json payload, `recorded_at` equal to the `FakeTimeProvider` time and a global position, and one `journal_outbox` row for it exists; when the outbox insert fails, neither row exists.
- Given the sample projector with a checkpoint, when events are appended across several streams, then it applies them in global-position order and records the last position as its checkpoint; after its read model and checkpoint are deleted, it rebuilds from position 0 to the same read model.
- Given no stream provider, when events are appended and the `FakeTimeProvider` is advanced by the poll interval, then the projector converges; given the hint stream enabled and time not advanced, the projector converges from the hint alone.
- Given Server code that calls `DateTime.UtcNow`, when CI builds and tests, then the build fails (analyzer) and the IL scan test fails.
- Given a `TestCluster` or a host built from the Server's registrations with `FakeTimeProvider` registered first, when a grain or projector asks for the time, then it gets the fake time.
- Given `tests/cs/server.tests/Fixtures/journal.json`, when the replay test runs, then every row deserializes directly or through its registered upcaster and applies to its sample state without error, and every registered alias and schema version appears in the fixture at least once.

## Spec Change Log

## Review Triage Log

### 2026-09-28 — Review pass
- verdicts: 40 findings — high 0, medium 6, low 27, false 5, maybe-false 2
- findings:
  - `[low]` `[reject]` Blind: hints wake runners only on the silo hosting the one hint grain — the deployment is single-silo (AD-15), other silos still converge by polling; per-silo fan-out adds a design, not a correction.
  - `[low]` `[reject]` Blind: every silo runs every projector — correct via the checkpoint row lock, only wasteful with several silos, which AD-15 does not have; leader election is new design.
  - `[low]` `[reject]` Blind: `journal_outbox` grows without bound, and with hints off rows are never dispatched — one small row per event, the same growth as the journal itself; a retention job is new surface. Listed under residual risks.
  - `[low]` `[reject]` Blind: one unreadable event stalls a projector forever — deliberate: skipping would silently corrupt the read model; the registry is validated when built and the fixture replay guards contracts in CI. A dead-letter policy is new design.
  - `[low]` `[patch]` Blind: registry remarks claim it fails at startup, but it is built lazily — remark reworded to "fails when the registry is built, before any row is read".
  - `[low]` `[reject]` Blind: deserialization is lenient about unmapped or missing members — stricter options break reading rows with additive fields; a contract rename without a schema bump is caught by the fixture rows. Trade-off, not a defect.
  - `[medium]` `[patch]` Blind: nothing tests the commit-order guarantee (with untested projector failure and hint switch) — grouped with the verification-gap ordering finding; `CommitOrderTests` added (lock blocks a second append; 32 parallel streams project every position).
  - `[low]` `[reject]` Blind: the migration job has no guard against concurrent runs — one Job or one AppHost resource runs it; FluentMigrator runs each migration in a transaction, so an overlap fails loudly. A lock is added complexity for a state not shown.
  - `[low]` `[patch]` Blind: ADO.NET clustering leaves ClusterId and ServiceId at "default" — set both to "coldframe" in `SiloExtensions`.
  - `[low]` `[reject]` Blind: the stored stream ID depends on `GrainId.ToString()` and is virtual — the format is pinned by tests that insert rows under `SampleGrain.StreamIdOf(key)` and read them through the grain; the override is intentional.
  - `[maybe-false]` `[reject]` Blind: the outbox dispatcher holds row locks across a publish with no timeout — depends on NATS.Net's own request timeout, not checked; if true only low, because projectors keep converging by polling.
  - `[low]` `[patch]` Blind: `JournalHintGrain.OnErrorAsync` drops errors silently — now logs a warning that projectors fall back to polling.
  - `[low]` `[reject]` Blind: the global append lock is an undocumented throughput limit and appends take no cancellation token — the apps README states appends are serialized; `ICustomStorageInterface` has no token parameter.
  - `[low]` `[reject]` Blind: duplicated `TryAdd(TimeProvider.System)` and "coldframe" constants, misleading "schema version 0" message, `Activator.CreateInstance` without a constructor check — harmless duplicates across assemblies; the version-0 path is unreachable because `GetInfo` throws first; a missing constructor still fails at registry build.
  - `[medium]` `[patch]` Edge: a checkpoint deleted between the catch-up read and the locked re-read makes a rebuild skip events below the old checkpoint — the internal apply now refuses when the locked checkpoint differs from the one the batch was read after, and catch-up re-reads.
  - `[low]` `[patch]` Edge: a concurrent delete between the ensure insert and `SELECT … FOR UPDATE` unboxes null — a null result now returns without applying.
  - `[low]` `[reject]` Edge: two projectors with the same Name share a checkpoint — developer error not shown in any registration; a guard adds a check for an unlikely state.
  - `[low]` `[reject]` Edge: a non-positive PollInterval or BatchSize breaks the runner — options are set only in code, never bound from configuration; validation adds surface.
  - `[low]` `[reject]` Edge: an invalid PollInterval makes `Task.Delay` throw outside the try and stop the host — same root as above.
  - `[maybe-false]` `[reject]` Edge: `OnNextAsync` may hang while outbox rows are locked — same as the Blind finding; if true only low.
  - `[low]` `[reject]` Edge: the outbox grows when hints are disabled — same as the Blind outbox finding.
  - `[low]` `[reject]` Edge: an upcaster returning null or throwing surfaces as a wrapped exception — the inner exception is preserved; a null return violates the non-nullable contract; the guard adds branches.
  - `[low]` `[reject]` Edge: an alias over 200 characters fails at append — aliases are short dotted names by convention; unlikely.
  - `[low]` `[reject]` Edge: a stream ID over 512 characters fails at append — grain IDs are a type name plus a UUID; unlikely.
  - `[false]` `[reject]` Edge: stale or missing membership rows make the migration-order test flaky — the AppHost PostgreSQL has no data volume, so each run starts empty, and the test waits for a healthy silo, which has written its row.
  - `[low]` `[patch]` Edge: localhost clustering set ClusterId/ServiceId, the replacement does not — same fix as the Blind cluster-ID finding.
  - `[medium]` `[patch]` Gap: out-of-commit-order visibility would not fail any test — `CommitOrderTests` added (lock-held append blocks; parallel appends all projected).
  - `[medium]` `[patch]` Gap: read-model and checkpoint atomicity untested on failure — `SampleProjector.FailOnceOn` plus `AProjectorFailureRollsBackReadModelAndCheckpointTogether`.
  - `[medium]` `[patch]` Gap: the migration job's exit 1 paths are never run — `MigrationJobExitCodeTests` runs the built job without a connection string and against an unreachable database, expecting exit 1.
  - `[medium]` `[patch]` Gap: the production NATS hint wiring is never exercised — `HintStreamAppHostTests` inserts a journal and outbox row into the AppHost database and waits for the Server's dispatcher to stamp `dispatched_at` after publishing to NATS.
  - `[low]` `[patch]` Gap: upcaster shape validation and multi-hop chains untested — tests added for a skipped version, a cross-alias upcaster, two upcasters from one version and a v1→v2→v3 read.
  - `[low]` `[patch]` Intent: journal behaviour is proven on a TestCluster, and the Server's NATS hint path and hint switch are untested — grouped with the NATS gap; the AppHost test now covers the NATS publish.
  - `[low]` `[reject]` Intent: no test restarts a silo — grain activation over existing journal rows and the rebuild from position 0 exercise the same mechanism a restart uses.
  - `[low]` `[reject]` Intent: "Aspire test hosts can substitute FakeTimeProvider" is proven on a host built from the Server's registrations, not on the AppHost's out-of-process Server — injecting a fake there needs a test hook in production code; later stories meet it only when an AppHost test needs fake time. Listed under residual risks.
  - `[low]` `[reject]` Intent: "startup never runs DDL" is checked structurally, not behaviourally — a behavioural check needs a restricted database role; the Server's SQL is confined to the journal classes in the diff.
  - `[false]` `[reject]` Intent: "a test fails the build" is only a manual check — `WallClockBanTests` runs in the CI `dotnet test` step and fails it, and `TheScanFindsAWallClockRead` proves the scan works.
  - `[low]` `[reject]` Intent: the fixture replay is in memory and covers only sample types — no production event type exists yet; the coverage test extends automatically to the first one.
  - `[false]` `[reject]` Intent: outbox atomicity is shown at the store, not through `RaiseEvent` — the grain's `ApplyUpdatesToStorage` calls the same `AppendAsync`; no different outcome.
  - `[false]` `[reject]` Intent: the wait helper lives only in the integration project and `UntilPositionAsync` is unused — the criterion asks that a helper exists; it does and `UntilCheckpointAsync` is used throughout.
  - `[false]` `[reject]` Intent: the job-as-a-process test may not start from an empty database — the AppHost PostgreSQL has no volume, so `coldframe` is new on every run.

### 2026-09-28 — Review pass
- verdicts: 42 findings — high 0, medium 3, low 31, false 5, maybe-false 3
- findings:
  - `[low]` `[reject]` Blind: `journal_outbox` is never pruned, and with hints off nothing dispatches it — carried: one small row per event, the same growth as the journal; a retention job is new surface.
  - `[low]` `[reject]` Blind: hints wake only the silo hosting the hint grain, and every silo runs every projector — carried: single silo (AD-15); others converge by polling; fan-out or leader election is new design.
  - `[low]` `[patch]` Blind: the stream ID comes from `GetGrainId()`, so a grain without `[GrainType]` orphans its journal when its class is renamed — `apps/cs/README.md` now says every event-sourced grain must pin `[GrainType("<entity>")]`, and why.
  - `[low]` `[reject]` Blind: one bad event blocks a projector forever and readiness stays green — carried: deliberate, since skipping would corrupt the read model; a dead-letter policy or lag health check is new design.
  - `[low]` `[reject]` Blind: readiness checks only the silo, and the bare `NpgsqlDataSource` has no health check or tracing — ADO.NET clustering already depends on PostgreSQL and the silo degrades without it; a database health check is new surface that this story does not ask for.
  - `[low]` `[reject]` Blind: `HintStreamAppHostTests` writes a test-only `sample.created` row into the AppHost journal — the test AppHost has its own PostgreSQL with no volume; it only matters once the Server registers a projector, and then that AppHost test fails loudly.
  - `[low]` `[patch]` Blind: the README says the registry "refuses to start", but it is built lazily — reworded: the registry cannot be built, so the first grain activation, append or projection that needs it fails.
  - `[maybe-false]` `[reject]` Blind: the dispatcher publishes to NATS while it holds outbox row locks — carried: depends on NATS.Net's own request timeout; even if true it is only low, because projectors still converge by polling.
  - `[low]` `[patch]` Blind: the public `ProjectionRunner.ApplyAsync(batch, ct)` moves the checkpoint past a gap before the batch — both `ApplyAsync` overloads are now `internal`, and `InternalsVisibleTo` was added for the two test assemblies; the only caller outside the class was a test.
  - `[low]` `[reject]` Blind: no test reaches the `23505` fallback, and there is no grain-level stale-writer test — the version pre-check runs under the global lock, so the catch is a defensive branch with no reachable bad outcome; a grain-level race needs two activations of one grain, which a one-silo TestCluster does not produce.
  - `[low]` `[reject]` Blind: two migration jobs can run at once — carried: one Job or one AppHost resource runs it, and FluentMigrator runs each migration in a transaction.
  - `[low]` `[reject]` Blind: ClusterId and ServiceId are hard-coded to "coldframe" — one deployment per database (AD-15); making them configurable is new surface.
  - `[low]` `[reject]` Blind: duplicated `TryAdd(TimeProvider.System)` and "coldframe" constants, and `AddProjector<T>` twice starts two runners — carried: harmless duplicates across assemblies, and a duplicate registration is a developer error not shown anywhere.
  - `[low]` `[reject]` Blind: an unknown alias is reported as schema version 0, and upcaster exceptions arrive wrapped in `TargetInvocationException` — carried: the inner exception is preserved.
  - `[low]` `[reject]` Edge: a poison event halts its projector — carried, same as the Blind finding.
  - `[low]` `[reject]` Edge: a string containing U+0000 fails the `jsonb` cast — PostgreSQL rejects NUL in `jsonb`, but no event carries free text yet; input validation belongs to the stories that accept user input, and a guard here adds branches for a state not shown.
  - `[low]` `[reject]` Edge: a stream ID over 512 characters or an alias over 200 characters fails at append — carried: unlikely by convention.
  - `[low]` `[reject]` Edge: a non-positive PollInterval or BatchSize — carried: options are set only in code.
  - `[low]` `[reject]` Edge: two projectors with the same Name share a checkpoint — carried: developer error not shown.
  - `[low]` `[patch]` Edge: the public `ApplyAsync` gets a batch that starts after a gap — same root and fix as the Blind `ApplyAsync` finding.
  - `[low]` `[reject]` Edge: the outbox grows when hints are off — carried, same as the Blind outbox finding.
  - `[low]` `[reject]` Edge: with several silos, only one wakes — carried, same as the Blind multi-silo finding.
  - `[maybe-false]` `[reject]` Edge: `OnNextAsync` can hang while outbox rows are locked — carried; even if true it is only low.
  - `[low]` `[reject]` Edge: an upcaster that returns null, throws, or returns a subtype — carried: a null return breaks the non-nullable contract, and the inner exception is preserved.
  - `[low]` `[reject]` Edge: an open-generic upcaster, or one with no parameterless constructor, fails with a raw exception — carried: it still fails when the registry is built.
  - `[false]` `[reject]` Edge: `recorded_at` loses sub-microsecond ticks, so equality checks fail — no code compares the stored value with the provider's value; projectors and replays read it back from the database, so they always agree.
  - `[maybe-false]` `[reject]` Edge: an empty append with a stale expected version returns true — Orleans' CustomStorage adaptor calls `ApplyUpdatesToStorage` only when updates are pending; to settle it, trace `LogViewAdaptor` in Orleans 10.3.1. Even if true it is only low: nothing is written.
  - `[low]` `[reject]` Edge: the ban covers only the five getters, not `TimeProvider.System` or provider-less timers — the intent names exactly those five; the Server's only `TimeProvider.System` uses are the two `TryAdd` registrations, and both `Task.Delay` calls pass the injected provider.
  - `[medium]` `[patch]` Gap: the Server's `Journal:HintStream:Enabled` switch is never tested — added `tests/cs/server.tests/Hosting/HintStreamSwitchTests.cs` (off: builds with no `nats` and no `OutboxDispatcher`; on: the dispatcher is registered; on with no `nats`: `InvalidOperationException`). Inverting the switch fails all three tests.
  - `[medium]` `[patch]` Gap: the runner's rebuild-race guard has no test — added `ABatchReadBeforeARebuildDeletedTheCheckpointIsNotApplied`, which calls the now-internal guarded `ApplyAsync` after deleting the checkpoint, expects `false` and no checkpoint row, then shows the rebuild applies every position. Disabling the guard fails it. Routed to patch rather than the layer's defer, because no pause seam was needed.
  - `[low]` `[reject]` Gap (other): `journal_outbox` grows without bound — carried, same as the Blind outbox finding.
  - `[low]` `[reject]` Intent: the concurrent-writer row is shown at the store, not through a grain, and the unique-violation catch is unreachable — same as the Blind `23505` finding.
  - `[low]` `[reject]` Intent: the job's exit code on a re-run is not seen at the process surface — the re-run goes through the same `MigrateUp()` path the fresh-database AppHost test runs as a process; a second process run adds little.
  - `[medium]` `[patch]` Intent: the Server's streams-off switch is untested, and most TestCluster tests wake the runner by hand — same root and fix as the Gap hint-switch finding. The poll-only path is covered by `WithoutAStreamProviderTheProjectorConvergesOnTheNextPoll`.
  - `[low]` `[reject]` Intent: journal behaviour is proven on a TestCluster and fake time on the Server's registrations, not on the AppHost Server — carried: injecting a fake there needs a test hook in production code. Listed under residual risks.
  - `[false]` `[reject]` Intent: waits target things other than positions or checkpoints (`Faults`, `IsWaiting`, `pg_locks`, `dispatched_at`) — the constraint forbids `Task.Delay`/`Thread.Sleep` as a wait. Every wait is a bounded condition poll in `JournalWait` or `WaitAsync(timeout)`.
  - `[low]` `[reject]` Intent: the commit-order tests prove the mechanism and the final state, not a skipped-event interleaving — building that interleaving needs the lock removed; `AnAppendWaitsForTheJournalLock` shows the mechanism holds.
  - `[false]` `[reject]` Intent: the build-failure half is only a manual check — carried: the IL scan fails the CI test step.
  - `[low]` `[reject]` Intent: the FluentMigrator check reads compiled references only, so a transitive package would go unseen — `apps/cs/server/packages.lock.json` has no FluentMigrator entry; checking the lock file too would be a stronger test for a state that does not exist.
  - `[false]` `[reject]` Intent: the outbox test's trigger is DDL beyond "databases and a sample read-model table" — the Never rule forbids DDL at application startup; the Design Notes name this trigger explicitly.
  - `[low]` `[reject]` Intent: an unknown alias reports schema version 0 on the `GetNewestSchemaVersion` path — carried, same as the Blind finding.
  - `[false]` `[reject]` Intent: nothing shows the existing integration tests still pass — this pass ran the full suite: 57 of 57 passed.

## Design Notes

- **Commit-ordered positions.** An identity column hands out positions at insert time, but transactions can commit out of order, so a projector could pass position 7 before 6 commits. Every append takes `pg_advisory_xact_lock(<fixed journal key>)` before inserting. With one silo the cost is negligible, and it keeps "read after checkpoint" correct.
- **Separate migration job.** A `Coldframe.Migrations` console app rather than a `migrate` switch in the Server keeps FluentMigrator out of the Server assembly, which makes "startup never runs DDL" checkable by a reference test. In Epic 2 it becomes the Kubernetes Job image.
- **Sample grain in tests.** No domain entity exists yet, so the sample grain, events and projector live in test code. The registry scans configured assemblies: the Server scans `Coldframe.Contracts` (empty today); the tests add their own. The fixture test covers both, so the first domain event added in a later story fails CI until it has a fixture row.
- **Time substitution in the Aspire host.** The AppHost runs the Server in another process, where no fake can be injected. Substitution is proved on the `TestCluster` and on a host built from the Server's own registration methods; both rely on `TryAddSingleton(TimeProvider.System)`.
- **Hint path.** Outbox dispatcher → Orleans stream `hints` (NATS in the AppHost, memory streams in tests) → implicit-subscription grain → local signal that wakes the runner. Hints carry only a position.
- **Failing outbox insert.** The test adds a trigger on `journal_outbox` in its own test database that raises an error, appends through the store, and asserts `journal_events` is unchanged.

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror` -- expected: 0 warnings, 0 errors
- `dotnet format --verify-no-changes --no-restore` -- expected: exit 0
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- expected: all tests in both test projects pass

**Manual checks (if no CLI):**
- Adding `var now = DateTime.UtcNow;` to a Server file fails `dotnet build` with RS0030; removing it restores the build.
- Removing the outbox insert makes the outbox test fail.

## Auto Run Result

Status: done

**Summary.** This was a follow-up review of Story 1.2: the event journal, migrations and projection pipeline, built and reviewed in an earlier pass. Four review layers ran against the diff since `4dff9cede2fc6bb165fc4b2b9333c1afe179db5c`. Five small patches were applied:
- tests for the Server's hint-stream switch
- a test for the projection runner's rebuild-race guard
- `ProjectionRunner.ApplyAsync` narrowed to `internal`
- two README corrections

Nothing is owed by an operator.

**Files changed in this pass.**
- `apps/cs/server/Journal/ProjectionRunner.cs` -- both `ApplyAsync` overloads are now `internal`, so the unguarded one is no longer public API
- `apps/cs/server/Coldframe.Server.csproj` -- `InternalsVisibleTo` for `Coldframe.Server.Tests` and `Coldframe.Server.IntegrationTests`
- `apps/cs/README.md` -- event-sourced grains must pin `[GrainType]`; the registry's failure is described accurately (it fails when first needed, not at startup)
- `tests/cs/server.tests/Hosting/HintStreamSwitchTests.cs` -- the `Journal:HintStream:Enabled` switch: off, on, and on without `nats`
- `tests/cs/server.integration/Journal/ProjectionTests.cs` -- `ABatchReadBeforeARebuildDeletedTheCheckpointIsNotApplied`

**Review findings.** 42 findings: high 0, medium 3, low 31, false 5, maybe-false 3.
- **Patches applied:** 5 entries covering 8 findings.
  - Medium (2): hint-switch tests, rebuild-guard test.
  - Low (3): `[GrainType]` note, registry wording, `ApplyAsync` made internal.
- **Deferred:** none. The two deferrals from planning are unchanged.
- **Rejected:** 34 findings. Most are carried from the first pass. Each reason is in the second Review Triage Log entry above.

**Follow-up review recommended: false.** This is a follow-up pass and it patched no `high` finding.

**Verification.** Run on this machine from the repository root, on the final tree:
- `dotnet restore --locked-mode` then `dotnet build --no-restore -warnaserror`: 0 warnings, 0 errors.
- `dotnet format --verify-no-changes --no-restore`: exit 0.
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build`: 57 of 57 passed.
- Mutation checks, each reverted afterwards:
  - Inverting the hint switch in `SiloExtensions.CreateHintStream` failed all three `HintStreamSwitchTests`.
  - Disabling the `readAfter` guard in `ProjectionRunner` failed `ABatchReadBeforeARebuildDeletedTheCheckpointIsNotApplied` (`Assert.False`).

**Residual risks.**
- `journal_outbox` is never pruned, and with hints disabled its rows are never dispatched.
- Hints wake only the silo that hosts the single hint grain, and every silo runs every projector. This is correct for today's single silo only.
- A poison event stops its projector until it is fixed, and readiness does not show it.
- `FakeTimeProvider` substitution is proven in-process, not in the Server that the AppHost launches.
- `HintStreamAppHostTests` leaves a test-only `sample.created` row in the test AppHost's journal. The first Server-registered projector will fail that AppHost run until the test cleans up after itself.
- The finalization commit leaves `sprint-status.yaml` and `deferred-work.md` uncommitted on purpose. The orchestrator owns them.
