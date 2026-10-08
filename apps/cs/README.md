# apps/cs

.NET runtimes.

| Folder | What | Arrives in |
| --- | --- | --- |
| `server/` | The Server: Orleans silo and Edge API in one ASP.NET Core host, with the event journal and projectors | Epic 1 |
| `migrations/` | The migration job: applies the one forward-only migration set and keeps the Readings partitions ahead, then exits; also the restore step `advance-replay` | Story 1.2, Story 4.5 |

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
migration through one FluentMigrator runner and one version table (`versions`), then ensures the
monthly partitions of the ingestion tables, and exits 0, or 1 when either fails. The runner applies the Orleans cluster schema from
`Escendit.Orleans.Migrations.Cluster.PostgreSQL` together with Coldframe's own migrations. The AppHost
starts the Server only after the job has completed; in a deployment it becomes the Kubernetes Job.

Run it on its own against any database:

```sh
ConnectionStrings__coldframe="Host=localhost;Port=5432;Database=coldframe;Username=postgres;Password=…" \
  dotnet run --project apps/cs/migrations
```

### Commands

| Arguments | What it does |
| --- | --- |
| none | Applies the pending migrations, then does what `partitions` does. The AppHost and the Helm hook Job run it this way. |
| `partitions [--months-ahead N]` | For `readings` and `device_reports`: creates the partition of the current month (UTC) and of the `N` months after it (default 3, at least 2), and of every other month that has rows waiting in the default partition (a Reading older than the first partition, a month a run missed), unless it exists. The waiting rows of a month move into its new partition in the transaction that creates it, so a run leaves the default partitions empty. Safe to run at any time and as often as you like; the chart's daily CronJob does. |
| `advance-replay [--uplink-margin N] [--downlink-margin N]` | The restore step ([`docs/operations/restore.md`](../../docs/operations/restore.md), step 5), **with the apps stopped**: in one transaction, every Device's uplink high-water mark moves up by the uplink margin (default 64) with its window marked fully seen, and every downlink counter by the downlink margin (default 1 048 576). A margin is 1 to 4 294 967 296; a larger one is refused as a usage error. An enrolled Device without replay state gets one. Each run adds the margins again, so run it once per restore. |

Anything else (an unknown command or option, a missing or out-of-range value) prints the usage and
exits 2 before the database is touched. A command that fails exits 1 and leaves no partial change.

```sh
ConnectionStrings__coldframe="…" dotnet run --project apps/cs/migrations -- partitions --months-ahead 6
```

Partitions are named `<table>_y<yyyy>m<MM>` (`readings_y2026m10`). A migration never creates one:
the migrations create the tables and their `_default` partitions, the commands follow the calendar.

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
| `GET /sites/{siteId}/sensors/{sensorId}/calibration` | `Administrator` | The Sensor's Calibration state and its recent stored Readings with their `readingSeq` (Story 5.2, see [Calibration](#calibration)) |
| `POST /sites/{siteId}/sensors/{sensorId}/calibration` | `Administrator` | Saves the dry and/or wet point of a Sensor's Calibration (Story 5.1, see [Calibration](#calibration)): 200 with where the Calibration stands |
| `POST /device/heartbeat` | `Device` | A Hub's signed heartbeat: `Device(id).Heartbeat(…)` verifies it and journals `device.seen`; 200 `{serverTime}` |
| `POST /device/ingest` | `Device` | A Hub relays sealed Node frames: `Device(hubId).AuthenticateRelay(…)`, then `Device(nodeId).Ingest(…)` per frame; 200 `{results: [{status, downlink?}]}` |

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

### Ingesting Node frames, step by step

Ingestion (AD-8, AD-9, AD-11, AD-17, AD-19) is served by `POST /device/ingest` in
`server/Edge/EdgeApi.cs`. The handler only parses the envelope and calls grains; everything else
happens in the Device grain (`server/Devices/DeviceGrain.cs`), the only writer of `readings`,
`device_reports`, `reading_keys` and `device_replay` (through `DeviceIngestionStore`).

1. The handler parses the four Device headers (401 `device-unauthorized`), reads at most 16 KiB and
   parses the envelope: a JSON object whose `frames` holds at most 32 strings of at most 1 024
   characters (400 `validation` otherwise).
2. `DeviceGrain.AuthenticateRelay` on the signer's grain: the heartbeat's HMAC check, bound to the
   path, within 5 min of `Clock`, each nonce once while the grain is active, and the signer must be
   an enrolled **Hub**. It journals nothing. A refusal is 401, and no frame is looked at. Any
   enrolled Hub may relay any Node.
3. Each frame, in request order: standard base64 → `SealedEnvelope` → the Node's grain by its
   Device ID. A frame that is not base64, not a `SealedEnvelope`, or has no 8-byte Device ID is
   `rejected_auth` without a grain call. A grain call that throws or times out is `retry` for that
   frame only, logged with the Device ID.
4. `DeviceGrain.Ingest`, in this order:
   1. Not an enrolled Node → `unknown_device`. Another `protocol_version` → `rejected_auth`.
   2. Open the seal with the `seal/v1` key (unwrapped `K_dev`, zeroed afterwards) → `rejected_auth`.
   3. The replay window, loaded from `device_replay` on first use → `rejected_replay`.
   4. Decode the `NodeFrame` (`NodeFrameReader`). An authentic plaintext that is no valid frame is
      `rejected_auth`; a synced `measured_at` more than 5 min ahead is `rejected_time`. Both
      consume the counter (the window is stored, without rows and without a downlink).
   5. The Pause gate: a paused Device (`device.paused` without a matching `device.resumed` for
      every source) is acknowledged and nothing is stored.
   6. One transaction: `device_replay` (window and `downlink_counter + 1`), then `reading_keys`
      with `ON CONFLICT DO NOTHING`, and a `readings` or `device_reports` row only for a key that
      was new. The device report's key is the nil UUID with `report_seq`. The transaction fails
      (`retry`) when the stored high-water mark is above the one being written: the window was
      moved underneath the grain, which then reads it again.
   7. The relay Hub, after the commit: `device.relay-changed` is journaled only when it differs
      from the last one; a frame that is not committed records none.
   8. The Specification set, when the frame's `spec_hash` is not the known one and a set is
      attached (see [Sensor Specifications](#sensor-specifications)).
   9. Only after that, the `Downlink` (`acked_counter`, `server_time_ms`, the frame's
      `reading_seq` ranges, no commands, `specifications_unknown`) is sealed with the `ack/v1` key
      under the reserved counter. `stored` when at least one key was new (or the Device is paused),
      `duplicate` otherwise.
5. The handler answers 200 with one `{status, downlink?}` per frame, or 503 `ingest-unavailable`
   when there were frames and every one was `retry`.

A failed transaction answers `retry` and leaves no trace: the grain drops its in-memory window and
reads it again for the next frame, so memory never runs ahead of the database. The downlink counter
is never kept in memory, so no counter is reused after a restart. No event is journaled per frame.
Two kinds of `retry` do leave rows behind: when the frame committed but the changed relay Hub could
not be journaled, or its Specification set could not be declared, the frame is not acknowledged, and
the Node's resend is a `duplicate`.

Time: a synced Reading keeps the Node's `measured_at`. An unsynced one (`time_unsynced`, with its
`boot_id` and `uptime_ms` kept) is rebased: taken by the boot that sealed the frame, it is
`receive time − (frame uptime − Reading uptime)`, a negative difference counting as 0; taken by an
earlier boot, it is the receive time.

A Sensor ID is `UUIDv5(namespace, "{deviceIdHex}:{slot}:{quantity}")` from
[`packages/crypto-spec`](../../packages/crypto-spec) (`Coldframe.Crypto.SensorIds`); `slot` is the
Sensor's index in the Node's Specification set. `calibration_id` is the Calibration in force for the
Sensor when the Reading is stored (the Device grain's cache, see [Calibration](#calibration)), `NULL` for a Sensor
without one. Pause has no
API yet: `device.paused` and `device.resumed` exist as events and state only (Epic 8).

Tests drive ingestion through the Device simulator only (`SimulatedDevice.Wake`, `SealFrame`,
`IngestBody`, `IngestRequest`, `ReadIngestResponse`, `OpenDownlink`).

### Sensor Specifications

A Node declares its Sensors on its first report (AD-19, FR-3). The wire side is in
[`packages/proto`](../../packages/proto/README.md#specifications); the Server side is the Device grain
and the Sensor grain (`server/Sensors/SensorGrain.cs`, `sensor/{sensorId}`).

1. Every frame carries `spec_hash`. The Device grain keeps one **known hash** per Device: the hash
   of the last set it accepted. It never recomputes a hash, it only compares. An empty hash is never
   known.
2. A frame whose hash is not the known one is stored and acknowledged as always, and its `Downlink`
   says `specifications_unknown`. The Node then attaches its `SpecificationSet` to its next frame,
   once per request; a lost frame is asked for again by the next `Downlink`.
3. A frame with an unknown hash and a valid set, after its commit and relay change: the Device grain
   calls `ISensorGrain.Declare` on every Sensor of the set (the Sensor ID of slot *i* and its
   quantity), and only then journals `device.specifications-declared` `{specHash, sensors: [{slot,
   quantity, sensorId}], declaredAt}`. From then on the hash is known and the `Downlink` stops
   asking. When a step fails, the frame answers `retry` and the hash stays unknown; a declaration is
   idempotent, so the Node's next set repeats it harmlessly. A declaration that failed partway can
   leave `sensor.declared` or `sensor.specification-changed` on Sensor streams for a set the Device
   never accepted: the Device's Sensor list, not the existence of a Sensor stream, says which
   Sensors a Node has.
4. A set that breaks the contract (a hash of 0 or more than 32 bytes, 0 or more than 32
   Specifications, an unknown quantity or unit, `range_min >= range_max`, default low >= default
   high, a default outside 0–100 for a calibrating Sensor or outside the range for any other) is
   ignored: the Readings are stored and acknowledged, nothing is declared, the `Downlink` keeps
   asking, and one warning names the Device (EventId 2, category
   `Coldframe.Server.Devices.DeviceGrain`). `NodeFrameReader` decides this; it never changes the
   frame's status.
5. A known hash needs nothing, with or without a set attached: no event on any stream.

A paused or unassigned Node declares too; only Readings pass the Pause gate.

| Event on `sensor/{id}` | Journaled when |
| --- | --- |
| `sensor.declared` `{deviceId, slot, specification, declaredAt}` | The first declaration. Both Thresholds follow the Specification's defaults |
| `sensor.specification-changed` `{specification, changedAt}` | A declaration with another Specification for the same slot and quantity. The same Specification journals nothing |
| `sensor.thresholds-changed` `{low, high, changedAt}` | Never yet: Thresholds have no API until Story 5.3. Tests seed it |
| `sensor.calibration-point-recorded` `{point, readingSeq, rawValue, recordedAt}` | One point (`Dry` or `Wet`) of a Calibration was saved and the other is missing ([Calibration](#calibration)) |
| `sensor.calibrated` `{calibrationId, dryRaw, wetRaw, calibratedAt}` | Both points are known and distinct: the new Calibration in force |
| `sensor.calibration-delivered` `{calibrationId, deliveredAt}` | The Device grain acknowledged the Calibration as in force |

A `specification` is `{quantity, unit, rangeMin, rangeMax, calibration, defaultLow?, defaultHigh?}`;
the defaults are in percent for a calibrating Sensor, otherwise in `unit`.

- **Thresholds.** Each side is `Default`, `Override` (with a value) or `Cleared`. A side in `Default`
  reads the Specification's default, which may be absent. A declaration replaces the Specification
  only: an override keeps its value and a cleared side stays cleared. `ISensorGrain.Describe` returns
  the Specification and each side's kind and effective value, or `null` for a Sensor that was never
  declared. Nothing validates Thresholds yet, and no endpoint or read model shows a Sensor.
- **A new quantity at a slot is a new Sensor**, because the Sensor ID changes. The old Sensor's stream
  is left as it is, and the Device's Sensor list holds only the Sensors of the last accepted set.
- **Undeclared slots.** A Reading whose slot and quantity are not in the Device's Sensor list (sent
  before the declaration, for a slot beyond the set, or with another quantity) is stored and
  acknowledged under its derived Sensor ID and creates no Sensor stream. A later declaration produces
  that same ID, so nothing is migrated. Nothing is evaluated yet: no Reading reaches a Sensor grain
  before Epic 6, which will evaluate only the Sensors of that list.

The simulator plays the Node: `SimulatedDevice.Specifications` (four Sensors matching
`DefaultReadings`, settable), `SpecHash`, and `Wake`, which attaches the set once after `OpenDownlink`
saw `specifications_unknown` (`attachSpecifications` attaches or withholds it explicitly).

### Calibration

`POST /sites/{siteId}/sensors/{sensorId}/calibration` (Story 5.1; Administrator and up, no Bluetooth) takes
`{dry?: {readingSeq}, wet?: {readingSeq}}`: each point names a Reading the Server already stored for the Sensor,
and its raw value is read from the `readings` table, never sent. The Sensor grain is the only validator and the
only writer of a Calibration (AD-9); the Device grain only caches which Calibration ID to stamp.

1. The handler asks the Sensor grain for its Device and the Device grain for its Site: a Sensor that is unknown,
   was never declared or belongs to a Node of another Site is 404 `sensor-not-found`. A body without a point, or
   with a `readingSeq` that is no non-negative integer, is 400 `validation`.
2. `SensorGrain.Calibrate` refuses (400 `validation`, nothing journaled) a Sensor whose Specification has no
   `calibration: true`, a `readingSeq` with no stored Reading of this Sensor, and two points that are
   indistinct: equal, or closer than `SensorCalibrationLimits.MinimumSpan` (16 raw counts). Either orientation
   of the raw values is a Calibration; the dry point reads 0 %, the wet one 100 %.
3. One point (the other missing) journals `sensor.calibration-point-recorded` and answers 200 with
   `calibrated: false` and the `pendingDry` or `pendingWet` point; the Sensor stays as calibrated as it was, also
   while a Calibration is in force and the Sensor is recalibrated. A point of the same kind replaces the kept one,
   and the points may come in either order, also across a silo restart.
4. Two points (in the request, or one kept) journal `sensor.calibrated` with a new Calibration ID (UUIDv7 from the
   `TimeProvider`) and both raw values, clear the kept points, and catch the lots projector up: the
   `calibrations` table has its points and the Lot leaves *needs calibration* before the call returns.
5. Only then the Sensor grain calls `IDeviceGrain.SetCalibration` (Sensor ID, Calibration ID, revision), which
   journals `device.calibration-set` and returns once it is persisted; the Sensor then journals
   `sensor.calibration-delivered` and the call answers 200 `{calibrated: true, calibrationId, dry, wet}`. The
   revision is the Sensor's Calibration count: the Device keeps the highest, so a redelivery, or one that arrives
   late, changes nothing.
6. When the Device call fails the event stays: the call answers 503 `calibration-not-delivered`, and the Sensor
   grain delivers the persisted Calibration again from its state, on a 5 s grain timer while it is active, by the
   `deliver-calibration` reminder, and on activation, until the Device acknowledges. The same request again
   (the pair already in force, nothing kept) creates no new Calibration; it finishes the delivery and answers 200.

A Reading is stored with the Calibration in force for its Sensor at that moment (`DeviceGrain.Ingest` stamps
`calibration_id`; no Calibration means `NULL`), so recalibration only affects later Readings and history keeps its
own. The percentage is derived, never stored: `CalibrationMath.Percent` is linear between the points of the
Reading's Calibration, clamped to 0 to 100 and rounded to the nearest 5 (a half rounds up), and the Lot detail's
`sensors` shows a soil-moisture Reading that has a Calibration as `%`. A Reading stored before the first
Calibration stays `raw`, so a percentage appears with the next Reading, never before. The history endpoint is
unchanged: it still reports soil moisture raw. Threshold percentages never change on recalibration.

**Reading the state from a client (Story 5.2).** `GET /sites/{siteId}/sensors/{sensorId}/calibration`
(Administrator and up; a Sensor of another Site is 404 `sensor-not-found`) answers 200
`{calibrated, calibrationId?, dry?, wet?, pendingDry?, pendingWet?, readings}`: the Calibration in force, the point
the Sensor grain kept (so a flow left after the dry point resumes at wet), and `readings`, the Sensor's own recent
stored Readings newest first (`readingSeq`, `rawValue`, `measuredAt`), which are what the POST names.
`SensorReadings.RecentAsync` bounds the query by `measured_at` (the last 2 days, at most 20), so it only visits the
partitions of that window; `readings` is empty for a Sensor whose Specification has no Calibration. Each Sensor of
the Lot detail's `sensors` also carries `sensorId` and `calibratable` (its Specification says `calibration: true`,
read from the Sensor grain), so a client finds the Sensor to calibrate without another call.

### Moving and unassigning a Node

`POST /sites/{siteId}/devices/{deviceId}/move` (body `{lotId}`) and `/unassign` (Story 4.9, Administrator and
up, no Bluetooth) call `IDeviceGrain.Move` / `Unassign`. The order is AD-18's: the Device grain claims the new
Lot (`ILotGrain.Claim`; a Lot another Node holds is 409 `lot-claimed`, a missing or removed one 404
`lot-not-found`, and either refusal changes nothing), journals `device.moved` (or `device.unassigned`), and
only then releases the old Lot. The old Lot is recorded in the Device's journaled `PendingReleases`
(`DeviceState`, `[Id(11)]`); `device.lot-released` removes it once `ILotGrain.Release` answered released or
unchanged. A release that throws leaves the move in place and is retried by a 5 s grain timer while the grain
is active, by the `release-pending-lots` grain reminder, and on activation, so a crash between the journal and
the release still frees the old Lot. Moving back to a Lot that is still pending release drops it from the
list. Moving to the current Lot, or unassigning an unassigned Node, answers 200 and journals nothing; a Move
of a Node on no Lot journals `device.assigned`. Readings stay keyed by the Node: nothing deletes them, and an
unassigned Node's frames are stored and not evaluated, since no Lot claims it.

### The Devices list

`GET /sites/{siteId}/devices` (Story 3.7) reads the `devices` table, which `DevicesProjector`
(`server/Devices/`, projector name `devices`) builds from the Device streams only:

| Event on `device/{id}` | Row |
| --- | --- |
| `device.enrolled` | Creates the row: `site_id`, `kind` (`hub` or `node`), `enrolled_at` |
| `device.assigned` | Sets `lot_id` |
| `device.moved` | Sets `lot_id` to the new Lot (Story 4.9) |
| `device.unassigned` | Sets `lot_id` to `NULL`: the Node lists as unassigned (Story 4.9) |
| `device.lot-released` | Nothing: it only clears a Lot from the Device's pending releases |
| `device.seen` | Sets `last_seen_at` to `seenAt`; it never moves backwards |
| `device.relay-changed` | Nothing (Epic 7 reads it) |
| `device.paused`, `device.resumed`, `device.specifications-declared` | Nothing here; the lots projector reads them ([Lot status](#lot-status)) |

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

### Lot status

Every Lot has exactly one status, computed once on the Server (AD-14) and returned by
`GET /sites/{siteId}/lots` and `GET /sites/{siteId}/lots/{lotId}`. Clients render it; they never
compute a status and never re-sort the list.

**The rule** is `LotStatusRule.Evaluate` (`server/Lots/LotStatusRule.cs`), a pure function and the only
place that decides a status. The first line that holds wins:

| Input | Status | Supporting field |
| --- | --- | --- |
| No Node on the Lot | `noNode` | |
| The Node has a Pause source | `paused` | `pausedBy`: `device` and/or `site`; `pausedUntil`: the latest end, absent when any source has no end |
| The Node or its relay Hub is silent | `unknown` | `unknownCause`: `node` or `hub` |
| A `calibration: true` soil-moisture Sensor has no Calibration | `needsCalibration` | |
| A low-side Threshold Alert is open on a soil-moisture Sensor | `needsWater` | |
| Otherwise | `ok` | |

The list is ordered `needsWater`, `needsCalibration`, `unknown`, `ok`, `paused`, `noNode`, then by
creation time, then by Lot ID (`LotsReadModel.StatusOrder`).

**The projection.** `LotsProjector` (projector name `lots`) is the only writer of `lots` and of its two
support tables. It reads three kinds of streams and, after every event that can change a status, runs
the Lots it touches through the rule again:

| Event | Effect |
| --- | --- |
| `lot.created`, `lot.renamed`, `lot.removed` | The row, its name, its tombstone |
| `lot.claimed`, `lot.released` | `claimed_by` and `claimed_at`; the Lot is evaluated |
| `device.paused`, `device.resumed` | The Device's Pause sources and their ends in `lot_status_devices`; its Lot is evaluated |
| `device.specifications-declared` | The Sensor IDs of the Device's accepted set in `lot_status_devices`; its Lot is evaluated |
| `sensor.declared`, `sensor.specification-changed` | The Sensor's Device, quantity and `calibration` flag in `lot_status_sensors`; the Lot of its Device is evaluated |
| `sensor.calibrated` | The Calibration's points in `calibrations` (by Calibration ID) and `lot_status_sensors.calibrated`; the Lot of its Device is evaluated |

`status_since` moves only when the status changes, to the time of the event that changed it: the
journal's `recorded_at` for a Lot event (Lot events carry no time), the event's own time otherwise.
Applying an event again therefore changes nothing. A Sensor is declared before its Device's set
(`sensor.declared` precedes `device.specifications-declared` in the journal); until the set names the
Sensor, the Lot keeps its status. To rebuild, delete the rows of the four tables (`lots`, `lot_status_devices`, `lot_status_sensors`,
`calibrations`) and the `lots` checkpoint;
the migration that added the status columns does exactly that, so Lots from before it get their
status and its time from the journal when the Server starts.

**Live and fixture-only inputs.** Three inputs are live in Epic 4: the Node on the Lot, the Pause
sources (journaled only by tests until Epic 8 adds the commands), and the uncalibrated soil Sensor.
A declared `calibration: true` soil-moisture Sensor counts as uncalibrated until its `sensor.calibrated` is
projected (Story 5.1). The other two have no producer yet:

- **Silence.** No Silent Alert exists before Epic 7. The only silence the projector knows is a Node
  that has declared no Sensor: it has never reported, so its Lot is `unknown` with `unknownCause: node`
  (`LotsProjector.InputsOf`). Without this a freshly assigned Node would read `ok`. `unknownCause: hub`
  is never produced.
- **Open low-side Alert.** No Threshold Alert exists before Epic 6; the projector always passes
  "none", so `needsWater` is never produced.

Tests and client fixtures cover `needsWater` and `unknown` by Hub with seeded rows.

**`lastReadingAt` is read, not projected.** Both queries of `LotsReadModel` take the newest
`measured_at` of the Readings of the Lot's Node with `measured_at >= claimed_at`, through
`ix_readings_device_id_measured_at`. It is absent without a Node or before its first Reading since it
took the Lot. `moisturePercent` and `lowThresholdPercent` are in the contract for the clients'
fixtures, but `LotResponse` has no such property: the Server sends them from Epics 5 and 6 on.

### Lot detail and history

`GET /sites/{siteId}/lots/{lotId}` (Story 4.8) is the one Lot read that also carries what the Lot detail
screen shows; the list never does. While the Lot holds a Node (`lots.claimed_by`), `LotDetailReadModel`
(`server/Lots/LotDetailReadModel.cs`) adds, straight from `readings` and `device_reports`:

- `sensors`: the newest Reading of every `(slot, quantity)` of the Node with `measured_at >= claimed_at`
  (`DISTINCT ON`), in slot order, converted by `SensorConversion`: soil moisture is the raw count
  (`unit: raw`) unless the Reading was stored with a Calibration, then the percentage of that Calibration rounded
  to the nearest 5 (`unit: %`, see [Calibration](#calibration)), temperature milli-°C to `°C`, humidity milli-% to `%`,
  gas resistance Ω to `kΩ`. Clients only format (whole numbers, 3 significant digits for kΩ).
- `node`: `deviceId`, and from the Node's newest `device_reports` row `batteryPercent`, `charging`
  (`charging` or `notCharging`; the stored `unknown` and a null battery are omitted) and `lastSeenAt` (the
  report's `measured_at`). `devices.last_seen_at` is written from Hub heartbeats only, so it is not a Node's
  last seen. A Node without a report has only its `deviceId`; one without Readings has `sensors: []`.

`GET /sites/{siteId}/lots/{lotId}/history?quantity&from&to&cursor&limit` (`getLotHistory`, Member+) returns
one entry per UTC day with Readings of the Lot's current Node since it claimed the Lot: `low`, `high`
(converted like `sensors`) and `readingCount`, ascending; a day without Readings is absent. `to` defaults to
now and `from` to `to` minus 30 days, rounded down to the start of its UTC day so the first bar is a whole
day (a default read is at most 31 days, the default `limit`; `limit` is at most 366). Paging is keyset on the
day: `nextCursor` is the opaque last day of the page (`LotDetailReadModel.EncodeCursor`), present only when
more days follow. A bad `quantity`, `from`, `to`, `cursor` or `limit`, or `from` after `to`, is a 400
`validation` problem; an unknown Lot is 404 `lot-not-found`. After a reassignment only the new Lot's Node
Readings count, and the old Lot has none. Readings and device reports are never deleted (FR8).

**Devices list.** `lotName`, `batteryPercent` and `charging` are added to Node items from `lots` and the
Node's newest report; a Node's `lastSeenAt` is that report's time (the heartbeat time as a fallback). The
order is Hubs by Device ID, then Nodes by Lot name (byte order, unassigned last) and Device ID; clients keep it.

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
