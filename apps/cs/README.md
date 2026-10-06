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
| `GET /sites` | any authenticated User | Lists the caller's `Active` Sites with the caller's Role from the identity projection, oldest first then by Site ID: 200 `{sites: [{id, name, role}]}`; `[]` without a Membership |
| `POST /sites` | any authenticated User | `User(sub).CreateSite(key, name)`: 201 `{id, name, role}` with `Location: /sites/{id}` |
| `GET /sites/{siteId}` | `Member` | Reads the Site and the caller's Role from the identity projection; 404 once the Site is `Deleted` |
| `GET /enrolment-key` | any authenticated User | The Server's X25519 enrolment public key: 200 `{publicKey, fingerprint}` (base64url, lowercase hex SHA-256) |
| `GET /sites/{siteId}/devices` | `Member` | Lists every enrolled Device of the Site from the devices projection, by Device ID: 200 `{devices: [{id, kind, lotId?, lastSeenAt?, online}]}`; `online` is computed when the Server answers |
| `POST /sites/{siteId}/devices` | `Administrator` | Opens the sealed `K_dev`, wraps it, and `Device(id).Enrol(…)`: 201 `{id, kind, siteId}` |
| `POST /device/heartbeat` | `Device` | A Hub's signed heartbeat: `Device(id).Heartbeat(…)` verifies it and journals `device.seen`; 200 `{serverTime}` |

- **Authentication.** JWT bearer against `Identity:Authority` (the realm URL), audience
  `Identity:Audience` (`coldframe-server`); `Identity:RequireHttpsMetadata` defaults to `true` and only
  the local stack turns it off. Inbound claims are not mapped, so the User ID is the claim `sub`.
  Every request needs a token except the health endpoints under `/.well-known/healthz` and the
  `Device` endpoints (`/device/*`), which are anonymous to JWT and authenticate the Device's
  signature instead (a user token there is ignored).
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
4. `UserGrain` journals `user.site-creation-completed` and `user.site-membership-changed` (Owner). The same
   key returns the same result for 24 h.

### Reconciliation from Keycloak

Changes made in Keycloak itself (a break-glass edit in the admin console, a member added or removed, a
role granted or revoked, an Organization renamed or deleted) reach the Server through Temporal, which
carries nothing else (AD-3, AD-5). `keycloak-temporal-extensions` starts the workflow `IdentityAdminEvent`
on `keycloak-admin-queue` for every admin event and `IdentityUserEvent` on `keycloak-user-queue` for every
user event. `AddKeycloakEventPipeline()` hosts a worker for each; the user-event workflow only completes.
Everything lives in `server/Identity/Reconciliation/`.

1. The workflow runs one activity, `Reconcile` (start-to-close 45 s; retries after 1 s, doubling, at most
   1 min apart, without limit). The workflow does no I/O.
2. `AdminEventRoute` picks the Site from the event's `resourcePath` and what the event says the roster now
   shows. It ignores other realms (by realm **ID**), failed operations, resource types other than
   `ORGANIZATION`, `ORGANIZATION_MEMBERSHIP` and `ORGANIZATION_ROLE_MAPPING`, unparseable paths and roles
   other than `owner`, `administrator` and `member`. The event's `representation` and `authDetails` never
   drive state.
3. `SiteGrain.Reconcile` does nothing unless the Site is `Active`. It reads the Organization's display
   name, members and role holders from Phase Two (only reads, under `Keycloak:OperationBudget`) and
   journals only the differences: `site.renamed`, `site.membership-granted`, `site.membership-revoked`, or
   `site.deleted` when the Organization is gone. A member's Role is the highest of the three roles it
   holds; a member with none has no Membership. Then it catches the identity projection up.
4. The listener fires before Keycloak commits. When the pull does not show yet what the event says, the
   grain journals nothing and the activity fails retryably; from attempt 5 (about 15 s) the pull is
   taken as the truth. An unreachable Keycloak also fails retryably.
5. The activity then calls `SyncSiteMembership` on the User grain of every current and former member,
   with `null` for former members and for a deleted Site. It does so on every run, so a retry finishes a
   fan-out that was cut short. The Site grain never calls User grains, so nothing can deadlock with Create
   Site.

The event is only a trigger: duplicates, echoes of the Server's own writes and events out of order diff
to nothing, and any later event on the Site repairs a lost one.

**Break-glass: a Site never loses its last Owner.** When Keycloak shows a Site without an Owner, every
current Owner keeps Owner in the journal and the projection; all other differences still apply, and
nothing is written back to Keycloak. Every such reconciliation logs an `Error` with EventId 3
(`OwnerlessEditRefused`, category `Coldframe.Server.Identity.SiteGrain`), and the first one of an episode
journals `site.ownerless-edit-refused` with the kept Owners. The episode ends with
`site.ownerless-edit-resolved` on the first reconciliation that finds an Owner in Keycloak again. To find
an open episode, filter the Server's logs in the Aspire dashboard for that EventId, or look for a Site
stream whose last episode event is `site.ownerless-edit-refused`. The fix is made in Keycloak: give the
Organization an `owner` again.

Settings, section `KeycloakEvents`: `TargetHost` (Temporal frontend, `host:port`), `Namespace` and
`RealmId` (the realm's ID, `coldframe` in the local stack) are required and checked at start;
`AdminTaskQueue` and `UserTaskQueue` default to the extension's queues.

### Enrol a Device, step by step

Device enrolment (AD-12, AD-18) lives in `server/Devices/`.

1. The handler validates the `Idempotency-Key` and the body: `deviceId` (16 lowercase hex digits), `kind`
   (`hub` or `node`), `enc` (32 bytes) and `ciphertext` (48 bytes), both base64url without padding.
2. `EnrolmentKeyring` opens `K_dev` with the enrolment private key, `deviceId` being the HPKE associated
   data, and checks that `K_dev` derives that Device ID. Any failure is 400 `validation`; the reason is
   never echoed. `DeviceKeyVault` wraps `K_dev` at once: plaintext `K_dev` never leaves the handler, is
   never journaled and never logged.
3. `DeviceGrain` (`device/{id}`) refuses a Device enrolled on another Site (409
   `device-on-another-site`), then calls `SiteGrain.RegisterDevice`, which owns the roster and the
   Idempotency-Key rule (`{sub}:{key}`, 24 h; a key reused for another Device is 422). The Site journals
   `site.device-registered` once per Device, and its reply carries the Site's Pause (never paused until
   Epic 8). Only then does the Device journal `device.enrolled` with the wrapped key. Enrolling the same
   Device on the same Site again journals nothing and answers the same 201.

The wrapped key is `{kekId, nonce, sealed}`: ChaCha20-Poly1305 under
`HKDF-SHA256(UTF-8(kek), info "coldframe/device-kek/v1")` with a random nonce and associated data
`"coldframe/device-key/v1" ‖ deviceId`; `kekId` is the first 16 hex digits of SHA-256 of that key. The KEK
is separate from the enrolment key, so rotating the enrolment key invalidates only pending enrolments.
Re-wrapping under a new KEK is not built yet: changing the KEK makes every stored `K_dev` unreadable.

Settings, section `Enrolment`, both required and checked at start: `PrivateKeyPem` (PKCS#8 X25519, as
`openssl genpkey -algorithm X25519` writes it; Secret `coldframe-enrolment-key`) and
`DeviceKeyEncryptionKey` (at least 32 characters; Secret `coldframe-device-kek`). The AppHost generates
both.

### A Hub heartbeat, step by step

Heartbeats (AD-12, FR-13) are served by `POST /device/heartbeat` in `server/Edge/EdgeApi.cs` and
verified by the Device grain.

1. The handler parses the four headers (`X-Coldframe-Device`, 16 lowercase hex digits;
   `X-Coldframe-Timestamp`, 1–19 digits that fit a `long`; `X-Coldframe-Nonce`, 32 lowercase hex
   digits; `X-Coldframe-Signature`, 64 lowercase hex digits), exactly one of each. A missing or
   malformed one is 401 `device-unauthorized`.
2. It reads the raw body, at most 4 KiB, and parses it: a JSON object with `protocolVersion` 1 and
   an optional non-negative `uptimeMs`; other properties are ignored. Anything else is 400
   `validation`. Nothing is verified yet.
3. `DeviceGrain.Heartbeat` verifies inside the grain, so the plaintext `K_dev` never crosses a grain
   boundary. It refuses (401, reason never told) an unenrolled Device, a timestamp more than
   `HeartbeatMaxSkewMs` (300 000 ms) off `Clock`, a timestamp at or below the persisted
   `LastHeartbeatTimestampMs`, a nonce it has seen, and a bad signature: it unwraps `K_dev` with
   `DeviceKeyVault`, derives the `hub-auth/v1` key, checks the HMAC with
   `Coldframe.Crypto.Heartbeat.Verify` and zeroes both keys.
4. An accepted heartbeat journals `device.seen` `{seenAt, deviceTimestampMs, uptimeMs?}` and the
   handler answers 200 `{"serverTime":"yyyy-MM-ddTHH:mm:ss.fffZ"}` from `TimeProvider`.

Replay protection needs no shared cache: Orleans keeps one activation per Device, so the in-memory
nonce set (pruned beyond the skew window) is authoritative while it is active, and the persisted
timestamp rule covers a reactivation. The Hub's timestamps strictly increase, so neither rule
refuses a legitimate Hub. Every accepted heartbeat is journaled (about 2 000 small rows per Hub per
day), so the Devices projection (Story 3.7) and Silence evaluation (Epic 7) read real events.

Tests drive the Device path through the Device simulator only
(`SimulatedDevice.HeartbeatRequest`), never through hand-built headers.

### The Devices list

`GET /sites/{siteId}/devices` (Story 3.7) reads the `devices` table, which `DevicesProjector`
(`server/Devices/`, projector name `devices`) builds from the Device streams only:

| Event on `device/{id}` | Row |
| --- | --- |
| `device.enrolled` | Creates the row: `site_id`, `kind` (`hub` or `node`), `enrolled_at` |
| `device.assigned` | Sets `lot_id` |
| `device.seen` | Sets `last_seen_at` to `seenAt`; it never moves backwards |

`site.device-registered` creates no row: the Site's roster can hold a Device whose enrolment was
never journaled. `device.seen` carries no Site, so the projector keys on the stream ID. The projector
is not caught up inside `DeviceGrain.Heartbeat`; a heartbeat shows in the list after the next hint or
poll (`JournalOptions.PollInterval`).

**Online is never stored.** The handler computes it for every answer with
`DeviceLiveness.IsOnline(lastSeenAt, now)`, `now` from the injected `TimeProvider`: a Device is online
when it was seen and `now - lastSeenAt <= DeviceLiveness.HubOnlineWindow`. The window is 120 s, two
missed heartbeats at the slowest interval of 60 s, so one late beat does not flip a Hub to offline.
It is not the Hub Silence Window of the Silent Alert (Epic 7). A Device that never sent a heartbeat
has no `lastSeenAt` and is offline. Clients show `online` as received and read the list again to
refresh it; they never compute it and never keep it past a failed reload.

### Add an endpoint

1. **Contract.** Add the operation to `packages/openapi/coldframe.openapi.json` with its
   `x-coldframe-minimum-role`: a `SiteRole` or `Authenticated` (or `Device` for `/device/*`). If the
   contract already has it with `x-coldframe-planned`, remove that mark in the same change.
2. **Rule.** Map it in `MapEdgeApi()` with exactly one of `.RequireSiteRole(SiteRole.X)` (the route
   must contain `{siteId}`), `.RequireAuthenticatedCaller()` or, for `/device/*`, `.RequireDevice()`
   (anonymous to JWT; the handler authenticates the Device). Handlers call grains and read only read
   models.
3. **Matrix sample.** Add a sample request to `Samples` in
   [`AuthorizationMatrixTests`](../../tests/cs/server.integration/Edge/AuthorizationMatrixTests.cs): how to
   call the endpoint on a given Site and the status it answers when allowed.

`EdgeEndpointDiscoveryTests` fails when an endpoint declares no rule or more than one, or when the
mapped endpoints and rules differ from the contract. `AuthorizationMatrixTests` fails when an endpoint
has no sample; otherwise it runs every endpoint as Owner, Administrator and Member on their own Site and
on another Site, and expects exactly what the declared minimum implies. A `Device` endpoint's sample
carries no Device headers, and every caller, whatever its token and Role, must get 401
`device-unauthorized`.
