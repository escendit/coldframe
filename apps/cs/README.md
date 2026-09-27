# apps/cs

.NET runtimes.

| Folder | What | Arrives in |
| --- | --- | --- |
| `server/` | The Server: Orleans silo and Edge API in one ASP.NET Core host, with the event journal and projectors | Epic 1 |
| `migrations/` | The migration job: applies the one forward-only migration set, then exits | Story 1.2 |

Tests live in [`tests/cs`](../../tests/cs): `server.tests` needs no containers, `server.integration`
starts the AppHost.

## Rules that hold for every project here

- **Time comes from an injected `TimeProvider`** (AD-6). `DateTime.Now`, `DateTime.UtcNow`,
  `DateTime.Today`, `DateTimeOffset.Now` and `DateTimeOffset.UtcNow` fail the build (RS0030, listed in
  [`BannedSymbols.txt`](../../BannedSymbols.txt)), and `WallClockBanTests` fails if one reaches the IL.
  In a grain, read `Clock`; elsewhere, take `TimeProvider` in the constructor. The Server registers
  `TimeProvider.System` with `TryAdd`, so a test host registers `FakeTimeProvider` first.
- **The Server never runs DDL** (AD-22). Only the migration job references FluentMigrator.

## The migration job

`migrations/` is a console app. It reads the connection string `coldframe`, runs every pending
migration through one FluentMigrator runner and one version table (`versions`), and exits 0, or 1 when
a migration fails. The runner applies the Orleans cluster schema from
`Escendit.Orleans.Migrations.Cluster.PostgreSQL` together with Coldframe's own migrations. The AppHost
starts the Server only after the job has completed; in a deployment it becomes the Kubernetes Job.

Run it on its own against any database:

```sh
ConnectionStrings__coldframe="Host=localhost;Port=5432;Database=coldframe;Username=postgres;Password=…" \
  dotnet run --project apps/cs/migrations
```

### Add a migration

1. Add a class to `migrations/Migrations/` named `M<yyyyMMddHHmmss><What>`, with
   `[Migration(<yyyyMMddHHmmss>, "<what>")]`, deriving from `ForwardOnlyMigration`. Take the current UTC
   time for the version; it must not collide with the cluster package (`MigrationSetTests` checks).
2. Write `Up()` only. Migrations are forward-only: a mistake is fixed by the next migration, never by
   editing or reverting one that has shipped.
3. Snake-case names (`journal_events`, `ix_journal_outbox_undispatched`), `timestamptz` for times,
   `jsonb` for JSON.

## The event journal

Every event-sourced grain (AD-2) derives from `JournaledStreamGrain<TState>` in `server/Journal/`. Its
stream is its grain ID (`site/…`), so give every such grain a `[GrainType("<entity>")]`: without it Orleans
derives the type from the class name, and renaming the class would orphan its journal. `RaiseEvent` plus `ConfirmEvents` appends to `journal_events` and
`journal_outbox` in one transaction; appends are serialized by an advisory lock, so global positions
become visible in commit order. On activation the grain replays its stream through the public `Apply`
overloads of its state.

### Add an event type

1. Add a public record to `packages/cs/contracts` (see its README) with
   `[EventType("<entity>.<verb-ed>")]`. The alias is stable forever; the class name is not stored.
2. Add a row for it to [`tests/cs/server.tests/Fixtures/journal.json`](../../tests/cs/server.tests/Fixtures/journal.json).
   `FixtureJournalReplayTests` fails until every registered alias and schema version has a row, and
   until the alias prefix maps to a state in that test.
3. Add an `Apply(TheEvent)` overload to the grain state.

### Add an upcaster

A breaking change to a payload never edits the old contract:

1. Rename the old record to `<Name>V<n>` and keep its alias and `[EventType(alias, n)]`.
2. Add the new record under the old name with `[EventType(alias, n + 1)]`.
3. Add a public class implementing `IEventUpcaster<<Name>V<n>, <Name>>` in the same assembly. It must be
   pure: no clock, no I/O.
4. Keep the fixture rows for version *n* and add one for *n + 1*.

The registry cannot be built when an alias has a gap in its versions or an old version has no upcaster,
so the first grain activation, append or projection that needs it fails.
Only the newest version can be written; grains and projectors only ever see the newest version.

### Add a projector

1. Implement `IProjector` with a stable `Name`. `ApplyAsync` receives each event in global-position
   order, inside the transaction that also moves the projector's checkpoint in `projection_checkpoints`.
   Write only through that transaction and ignore events you do not project.
2. Create its read-model tables in a migration.
3. Register it with `services.AddProjector<TProjector>()`. Its `ProjectionRunner` catches up at start,
   then on every hint and at least every `JournalOptions.PollInterval`.

To rebuild a read model, delete its rows and its row in `projection_checkpoints`; the projector starts
again from position 0.

Hints travel over the Orleans stream provider `hints` (NATS JetStream in the AppHost), carry only a
position, and only wake runners. Set `Journal__HintStream__Enabled=false` to run without them;
projectors then converge by polling alone.
