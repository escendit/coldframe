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

## The Edge API

The Server serves the contract in [`packages/openapi`](../../packages/openapi) from `server/Edge/`.
`MapEdgeApi()` in `EdgeApi.cs` maps every endpoint:

| Endpoint | Access | What it does |
| --- | --- | --- |
| `POST /sites` | any authenticated User | `User(sub).CreateSite(key, name)`: 201 `{id, name, role}` with `Location: /sites/{id}` |
| `GET /sites/{siteId}` | `Member` | Reads the Site and the caller's Role from the identity projection |

- **Authentication.** JWT bearer against `Identity:Authority` (the realm URL), audience
  `Identity:Audience` (`coldframe-server`); `Identity:RequireHttpsMetadata` defaults to `true` and only
  the local stack turns it off. Inbound claims are not mapped, so the User ID is the claim `sub`.
  Every request needs a token except the health endpoints under `/.well-known/healthz`.
- **Authorization.** The caller's Role comes from the identity projection, never from token claims.
  One policy reads the `siteId` route value, looks the Site and the caller's Role up in
  `IdentityReadModel`, and decides with `SiteAccess.Decide(minimum, siteExists, callerRole)`: 404
  `site-not-found` when the Site does not exist, 403 `forbidden` when the Role is missing or too low.
- **Errors** are Problem Details with `type` `urn:coldframe:problem:<slug>`; `EdgeProblems` holds them.
- **Keycloak.** Only the Server's own service account (`Keycloak:BaseUrl`, `Realm`, `ClientId`,
  `ClientSecret`) talks to Phase Two at `{keycloak}/realms/coldframe/orgs`. Each request is bounded by
  `Keycloak:RequestTimeout` (10 s) and one Site creation by `Keycloak:OperationBudget` (20 s), below the
  Orleans call timeout. An unreachable Keycloak answers 503 and leaves the request pending; a retry
  with the same key resumes it with the same Site ID.

### Create Site, step by step

1. The handler validates the `Idempotency-Key` (1–200 printable ASCII) and the name (trimmed, 1–100).
2. `UserGrain` (`user/{sub}`) journals `user.site-creation-requested` with a new UUIDv7 Site ID before
   any Keycloak call. It looks the Organization up by its tag `coldframe.idempotencyKey = "{sub}:{key}"`,
   otherwise creates it with that ID (`name` = Site ID, `displayName` = Site name); a 409 is resolved by
   reading the ID. It creates the Organization and writes nothing else to Keycloak.
3. `SiteGrain` (`site/{id}`) ensures the Organization roles `owner`, `administrator` and `member`, adds
   the caller as a member with role `owner`, journals `site.created` and `site.membership-granted`, and
   runs `CatchUpAsync` on the identity projector before it returns, so the next request sees the
   Membership (read-your-writes). It is the only writer of Memberships and Roles.
4. `UserGrain` journals `user.site-creation-completed`. The same key returns the same result for 24 h.

### Add an endpoint

1. **Contract.** Add the operation to `packages/openapi/coldframe.openapi.json` with its
   `x-coldframe-minimum-role`: a `SiteRole` or `Authenticated`.
2. **Rule.** Map it in `MapEdgeApi()` with exactly one of `.RequireSiteRole(SiteRole.X)` (the route
   must contain `{siteId}`) or `.RequireAuthenticatedCaller()`. Handlers call grains and read only read
   models.
3. **Matrix sample.** Add a sample request to `Samples` in
   [`AuthorizationMatrixTests`](../../tests/cs/server.integration/Edge/AuthorizationMatrixTests.cs): how to
   call the endpoint on a given Site and the status it answers when allowed.

`EdgeEndpointDiscoveryTests` fails when an endpoint declares no rule or more than one, or when the
mapped endpoints and rules differ from the contract. `AuthorizationMatrixTests` fails when an endpoint
has no sample; otherwise it runs every endpoint as Owner, Administrator and Member on their own Site and
on another Site, and expects exactly what the declared minimum implies.
