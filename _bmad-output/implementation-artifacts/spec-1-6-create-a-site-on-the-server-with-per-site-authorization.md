---
title: 'Story 1.6: Create a Site on the Server with per-Site authorization'
type: 'feature'
created: '2026-09-28'
status: 'done'
baseline_revision: '8494104b5b9d366f891125f3311ee00818ceebd9'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/apps/cs/README.md'
  - '{project-root}/packages/cs/README.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** The Server has a silo, a journal and projectors, but no Edge API, no authentication, no Site or User grain, and no link to Phase Two Organizations. A newly signed-in User cannot create a Site, and nothing authorizes a request per Site (FR-6, NFR-2, NFR-6).

**Approach:** Add the first Edge API endpoints, contract-first in `packages/openapi`: `POST /sites` and `GET /sites/{siteId}`. Validate Keycloak access tokens. `POST /sites` goes to `User(sub).CreateSite(key, name)`, which persists the request, creates a tagged Phase Two Organization, and calls `Site(orgId).Initialize(name, owner)`. The Site grain writes the membership and the Owner role to Phase Two, journals `SiteCreated` and `MembershipGranted`, and brings the identity projection up to date before it returns. One shared policy reads the caller's Role from that projection. A generated matrix test proves the policy for every endpoint (AD-3, AD-4, AD-24).

## Boundaries & Constraints

**Always:**
- Test-first (AD-24): write each acceptance criterion as a failing test before the code that makes it pass.
- Grains are the only writers of their state (AD-1). The User grain creates the Organization and nothing else in Keycloak. Only the Site grain writes memberships and roles to Phase Two. Handlers call grains and read only the identity read model. Grains never read read models.
- Use the Phase Two API at `{keycloak}/realms/coldframe/orgs`, not Keycloak's native `/organizations`. The Organization `id` is the Site ID and is chosen by the User grain (UUIDv7, persisted before the call). `name` is the Site ID, because Phase Two names are unique per realm. `displayName` is the Site name. Attribute `coldframe.idempotencyKey` = `"{sub}:{key}"`.
- The caller's Role comes from the identity projection, never from token claims. User ID = `sub` (`MapInboundClaims = false`). Tokens must carry audience `coldframe-server`. Roles are ordered `Owner > Administrator > Member` and serialize as those strings.
- Every error is RFC 9457 Problem Details (`application/problem+json`) with a stable `type` of the form `urn:coldframe:problem:<slug>`: `unauthorized` (401), `forbidden` (403), `site-not-found` (404), `validation` (400), `idempotency-key-missing` (400), `idempotency-key-reused` (422), `identity-provider-unavailable` (503).
- Every Edge API endpoint declares exactly one access rule in metadata: a minimum `SiteRole`, or "any authenticated user". Endpoints without a rule are rejected by a test. Health endpoints stay anonymous.
- Time comes from `TimeProvider` / `Clock` only. Idempotency keys expire 24 h after the request.
- JSON is camelCase with enums as strings, and absent optional fields are omitted.

**Never:**
- No Lot, rename, invite, Role-change, list-Sites or Temporal reconciliation endpoints; those are Stories 1.7, 1.9 and Epic 9. No client or UI changes and no generated clients; those are Story 1.8.
- Nothing in token claims (`organization`, `realm_access`, and so on) is used for authorization.
- No Keycloak admin (master) credentials in the Server. It uses only its own `coldframe-server` service account (`view-organizations` and `manage-organizations`).
- The Server runs no DDL. Nothing is hard-deleted. No `DateTime.Now` or `UtcNow`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Create | Valid token, `Idempotency-Key: k1`, `{"name":"Home"}`, no Membership | 201, `Location: /sites/{id}`, body `{"id","name":"Home","role":"Owner"}`. The Organization (displayName Home, tagged `sub:k1`) has the caller as a member with org role `owner`. Stream `site/{id}` holds `site.created` and `site.membership-granted`. | — |
| Read-your-writes | The next request, `GET /sites/{id}` | 200 `{"id","name","role":"Owner"}`, even with hints off and a long poll interval | — |
| Retry | Same caller, same key, same name, within 24 h (including after a failure following Organization creation) | Same 201 and body. One Organization, one Site stream. | — |
| Key reused with a different name | Same key, `{"name":"Other"}` | 422 `idempotency-key-reused`; nothing created | — |
| Key after 24 h | Same key after 24 h (FakeTimeProvider) | Treated as new: a second Site is created | — |
| Same key, other User | User B sends A's key | An independent Site for B | — |
| Missing or bad key | No header, empty, over 200 chars, or not printable ASCII | 400 `idempotency-key-missing` or `validation` | — |
| Bad name | Missing, blank after trimming, or over 100 chars | 400 `validation` | — |
| Keycloak down | Phase Two unreachable or 5xx | 503 `identity-provider-unavailable`. The request stays pending, and a retry with the same key resumes with the same Site ID. | HTTP timeout budget stays under the Orleans call timeout |
| No token or bad token | Missing, expired, wrong audience or issuer | 401 `unauthorized` Problem Details | — |
| Unknown Site | `GET /sites/{random}` (or a non-GUID) | 404 `site-not-found` | — |
| Other Site | A member of Site A calls Site B | 403 `forbidden` | — |

</intent-contract>

## Code Map

- `apps/cs/server/Program.cs`: the composition root, `AddServiceDefaults().AddJournal().AddSilo()`. Add authentication, authorization, Problem Details, the Keycloak client, the identity projector and `MapEdgeApi()`.
- `apps/cs/server/Journal/JournaledStreamGrain.cs`: the base for Site and User grains. The stream is the grain ID. Use `Clock` for time. `RaiseEvent` + `ConfirmEvents` are atomic with the outbox.
- `apps/cs/server/Journal/JournalServiceCollectionExtensions.cs:59-70`: `AddProjector<T>` registers the runner only as an `IHostedService`. Also register it so it can be resolved per projector type; the read-your-writes catch-up needs it. `ProjectionRunner.CatchUpAsync` is safe to call while the background loop runs, because `ApplyAsync` locks the checkpoint and re-reads when it has moved.
- `apps/cs/server/Journal/IProjector.cs`: the projector contract. `tests/cs/server.integration/Journal/SampleProjector.cs` shows the pattern: switch on `Data`, write through `transaction.Connection`, ignore other events.
- `tests/cs/server.integration/Journal/SampleCluster.cs:136-177`: the TestCluster pattern. Register `FakeTimeProvider` first, pin the Orleans keyed clocks, call `AddJournal(cs, o => …)`, `AddProjector`, and `AddJournalGrains(hints?)`. Per-test databases come from `JournalDatabase.cs`, which migrates through `AddColdframeMigrations`. `JournalWait.UntilAsync` / `UntilCheckpointAsync` handle waits, never sleeps.
- `tests/cs/server.tests/Samples/SampleEvents.cs`, `SampleState.cs`: the event and state shape. `[EventType]`, `[GenerateSerializer]`, `[Alias]`, `[property: Id(n)]`, and a public `Apply(T)` with `ThrowIfNull`.
- `tests/cs/server.tests/Journal/FixtureJournalReplayTests.cs:19-22` + `Fixtures/journal.json`: every new alias needs a fixture row. The `States` map is keyed by alias prefix, so add `site` → `SiteState` and `user` → `UserState`. States must be `public`, because the test binds `Apply` with `dynamic`.
- `packages/cs/contracts/`: event contracts are scanned by the Server's registry. There is no Orleans reference yet; add `Microsoft.Orleans.Sdk` (10.3.1, CPM) for serializer generation.
- `apps/cs/migrations/Migrations/M20260928120200…`: the migration pattern (`ForwardOnlyMigration`, snake_case, `timestamptz`). The next versions must be greater than 20260928120200.
- `aspire/Coldframe.AppHost/AppHost.cs:37-69,82-94`: the parameters, the Keycloak realm import (`--import-realm`, applied only when the realm is absent; the local Postgres has no volume), and the server resource (already `.WaitFor(keycloak)`).
- `aspire/keycloak/realms/coldframe-realm.json`: `${ENV}` substitution works on import, as the web client secret shows.
- `tests/cs/server.integration/KeycloakTests.cs:342-372`: admin token via `admin-cli` on `master`, from the AppHost parameters. `ServerHealthTests.cs:56-60`: `fixture.App.CreateHttpClient("server", "http")`.
- Phase Two keycloak-orgs 0.182 facts, verified on a live 26.6.7 container:
  - `POST /orgs {id?, name, displayName, attributes: {k: [v]}}` returns 201 with an empty body. The id is in `Location`. A duplicate id or name returns 409.
  - `GET /orgs?q=coldframe.idempotencyKey:"<v>"` does an exact attribute match. Quote values that contain `:`. Without `view-organizations` it returns `[]` rather than 401.
  - `GET /orgs/{id}` returns 200, or 404.
  - `PUT /orgs/{id}/members/{userId}` returns 201 and is idempotent.
  - `POST /orgs/{id}/roles {name}` returns 201, or 409 if the role exists; treat 409 as done.
  - `PUT /orgs/{id}/roles/{name}/users/{userId}` returns 201 and is idempotent; the user must already be a member.
  - Default: a placeholder `org-admin-{id}` user is created per Organization unless the realm attribute `_providerConfig.orgs.config.createAdminUser` is `"false"`.
  - A service account needs `realm-management` roles `view-organizations` and `manage-organizations`.
  - `sub` is present in access tokens. Audience comes from an `oidc-audience-mapper` with `included.client.audience: coldframe-server` on each client, and the target client must exist.
- NuGet: `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12 matches the installed runtime `Microsoft.AspNetCore.App` 10.0.12.

## Tasks & Acceptance

**Execution:**
- `packages/openapi/coldframe.openapi.json` (OpenAPI 3.1, JSON) and `packages/openapi/README.md`: contract first. `POST /sites` (header `Idempotency-Key`, body `CreateSiteRequest{name}`, 201 `Site{id,name,role}` + `Location`, 400/401/422/503). `GET /sites/{siteId}` (200 `Site`, 401/403/404). `SiteRole` enum. `ProblemDetails` schema. Bearer security scheme. Per operation, the extension `x-coldframe-minimum-role`: a `SiteRole` or `"Authenticated"`.
- `packages/cs/contracts/`: add the events `SiteCreated(Name, CreatedBy)` (`site.created`), `MembershipGranted(UserId, Role)` (`site.membership-granted`), `SiteCreationRequested(IdempotencyKey, SiteId, Name)` (`user.site-creation-requested`) and `SiteCreationCompleted(IdempotencyKey, SiteId)` (`user.site-creation-completed`). Add `SiteRole`, the grain interfaces `IUserGrain : IGrainWithStringKey` and `ISiteGrain : IGrainWithStringKey`, and serializable result records. Grains return result values, not exceptions, for expected outcomes (created, reused key, identity provider unavailable, not found). Update `packages/cs/README.md`.
- `apps/cs/server/Identity/`:
  - **Keycloak client.** An `IPhaseTwoOrganizations` seam and an HttpClient implementation that uses a client-credentials token, cached until shortly before expiry on `TimeProvider`. Its total timeout is under 20 s. Options: `Keycloak:BaseUrl`, `Realm`, `ClientId`, `ClientSecret`.
  - **`UserGrain`** (`[GrainType("user")]`, state `UserState` with keys and `requestedAt`). It expires completed keys older than 24 h. A pending key resumes: search by tag, else create with the persisted id, and a 409 means read by id. It then calls `Site.Initialize` and persists `SiteCreationCompleted`.
  - **`SiteGrain`** (`[GrainType("site")]`, `SiteState` with lifecycle `Uncreated|Active|Deleted`, name and owners). `Initialize` ensures the org roles `owner`, `administrator` and `member`, adds the member, and grants `owner`. It then raises both events. If the Site is already `Active` with the same owner, the call is idempotent. Before returning, it catches up the identity projector.
  - **`IdentityProjector`** (`Name = "identity"`). It upserts `identity_sites` and `identity_memberships`.
  - **`IdentityReadModel`.** Reads the Site (exists, name) and the caller's Role.
- `apps/cs/migrations/Migrations/`: add `identity_sites(site_id text pk, name text, lifecycle text, created_at timestamptz)` and `identity_memberships(site_id text, user_id text, role text, pk(site_id,user_id))`, plus an index on `user_id`.
- `apps/cs/server/Edge/`:
  - **Wiring.** `MapEdgeApi(this IEndpointRouteBuilder)` holds the two endpoints. `RequireSiteRole(SiteRole)` and `RequireAuthenticatedCaller()` add metadata.
  - **Policy.** One `IAuthorizationHandler` reads the `siteId` route value and the projection. It decides with the pure `SiteAccess.Decide(minimum, siteExists, callerRole)` → `Allow | Forbidden | NotFound`. Forbidden becomes 403 and NotFound becomes 404, both as Problem Details (`IAuthorizationMiddlewareResultHandler`).
  - **Authentication.** JWT bearer with `Identity:Authority`, `Identity:Audience` (`coldframe-server`) and `Identity:RequireHttpsMetadata` (default `true`). The 401 challenge is written as Problem Details. The fallback policy requires an authenticated user, and health endpoints are exempt.
  - **Endpoint rules.** Request validation. `Location` points to `/sites/{id}`. `GET /sites/{siteId}` reads the projection (minimum `Member`). `POST /sites` requires an authenticated caller.
  - **Package.** Add `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12 to CPM and refresh the lock files.
- `aspire/keycloak/realms/coldframe-realm.json`:
  - Add the confidential client `coldframe-server`: service account on, standard flow off, secret `${COLDFRAME_SERVER_CLIENT_SECRET}`.
  - Add its service-account user with `realm-management` `view-organizations` and `manage-organizations`.
  - Add the audience mapper to `coldframe-web` and `coldframe-mobile`.
  - Add the realm attribute `_providerConfig.orgs.config.createAdminUser: "false"`.
- `aspire/Coldframe.AppHost/AppHost.cs`:
  - Add the generated secret parameter `coldframe-server-client-secret`, and pass it to Keycloak and the server.
  - Pass the server `Identity__Authority` (`{keycloak http}/realms/coldframe`), `Identity__Audience`, `Identity__RequireHttpsMetadata=false` (local HTTP only) and `Keycloak__*`.
- `tests/cs/server.tests/`:
  - **Access rules.** `SiteAccess.Decide` table. `SiteRole` order.
  - **Endpoint discovery.** Enumerate `EndpointDataSource` from a throwaway `WebApplication` that calls `MapEdgeApi`, and check that every endpoint declares exactly one access rule. The (method, route, access) set must equal the operations and `x-coldframe-minimum-role` values in `coldframe.openapi.json`.
  - **Validation.** Idempotency-key and name rules.
  - **Fixture journal.** Rows and `States` entries for `site` and `user`.
- `tests/cs/server.integration/Identity/`:
  - **TestCluster suite** with a fake `IPhaseTwoOrganizations` and hints off (`PollInterval` 10 min). It covers:
    - creation and both journaled events;
    - read-your-writes, where the projection rows exist when `CreateSite` returns;
    - retry after the fake fails once after creating the Organization, giving one Organization and the same result;
    - key reuse with a different name;
    - expiry after 24 h (FakeTimeProvider);
    - `Initialize` idempotency.
  - **AppHost HTTP suite** with a runtime-created test client (direct grants plus audience mapper, via the admin API) and test users. It covers:
    - 201 and the Organization in Phase Two (tag, member, `owner` role, no `org-admin-*` user);
    - the immediate `GET` returning 200;
    - a retry giving the same body and one Organization for the tag;
    - the 404 path;
    - 401 without a token.
  - **Authorization matrix.** It lists endpoints the same way the unit test does and needs a sample request per endpoint; a missing sample fails the test. Seed Site A (Owner, Administrator, Member users) and Site B (another owner) by appending `site.*` events through a `JournalStore` on the AppHost `coldframe` database, then wait on the `identity` checkpoint. Assert every endpoint × role × {own Site, other Site}. Expected results are derived only from the declared minimum.
  - **KeycloakTests.** Add assertions for the `coldframe-server` client, its service-account roles, the audience mappers and the realm attribute.
- `apps/cs/README.md`, `docs/quickstart.md`: document the Edge API endpoints, the access declaration, how to add an endpoint (contract row, rule, matrix sample), and the new AppHost parameter.

**Acceptance Criteria:**
- Given an authenticated User with no Membership, when `POST /sites` is called with `Idempotency-Key` and name "Home", then a Phase Two Organization tagged with the key exists with the User as member and org role `owner`, the Site stream journals `site.created` and `site.membership-granted`, and the response is 201 `{"id","name":"Home","role":"Owner"}`.
- Given that 201, when the same User immediately calls `GET /sites/{id}`, then it returns 200 with role `Owner`.
- Given a retry with the same `Idempotency-Key`, when it is processed, then the original result is returned and Phase Two holds exactly one Organization with that tag.
- Given a Site ID that does not exist, when any Site-scoped endpoint is called, then the API returns 404 Problem Details with type `urn:coldframe:problem:site-not-found`.
- Given the generated matrix over every endpoint, every Role, own Site and other Site, when it runs, then each outcome matches the endpoint's declared minimum Role, and adding an endpoint to `MapEdgeApi` without an access rule, a contract entry or a matrix sample fails a test.
- Given `dotnet build -warnaserror`, `dotnet format --verify-no-changes` and `dotnet test`, when they run locally with podman, then all pass, including the TestCluster and AppHost suites.

## Spec Change Log

## Review Triage Log

### 2026-09-28 — Review pass
- verdicts: 29 findings — high 0, medium 5, low 18, false 2, maybe-false 4
- findings:
  - `[low]` `[reject]` (blind) The tag search builds `q=` from a key that may contain `"` or `\`, and a 400 is read as "no match". — Real, but the persisted-ID create and the 409→GET path still converge on one Organization. Such keys are unlikely from generated UUID keys, and escaping semantics in Keycloak are unverified.
  - `[low]` `[reject]` (blind) SiteGrain logs "Keycloak unavailable" when the caller aborts. — The only harm is a misleading warning for a disconnected client; the response is never seen. A fix needs a new outcome.
  - `[low]` `[reject]` (blind) Unexpected failures (Phase Two 403, token 401, Conflict, Orleans timeout) give 500 without a `urn:coldframe:problem:*` type. — `UseExceptionHandling` still answers Problem Details; only misconfiguration or bugs reach it. Adding a type and changing the contract is more than a direct correction.
  - `[low]` `[reject]` (blind) Completed keys are never pruned, and expiry counts from the request rather than the completion. — Growth is one small entry per Site created. The spec says 24 h from the request. A retry after a successful 201 that arrives past 24 h is rare.
  - `[low]` `[reject]` (blind) A pending key with a different name answers 422 for ever. — That is the specified reuse rule; clients issue a new key per submission (Story 1.8).
  - `[low]` `[reject]` (blind) `HttpClient.Timeout` wraps the resilience pipeline, so timeouts are not retried. — The outcome is still 503 within budget. Only the retry count is reduced.
  - `[maybe-false]` `[reject]` (blind) The read-your-writes catch-up is unbounded and may exceed the Orleans call timeout on a large backlog. — The background runner catches up at start. Settling it needs a backlog measurement; if true it would be low (a 500, then an idempotent retry).
  - `[low]` `[reject]` (blind) `GET /sites/{id}` reads the projection twice. — Performance only, on a single-user system.
  - `[low]` `[reject]` (blind) The 401 drops RFC 6750 `error="invalid_token"`, and the title reads as an instruction. — Clients refresh on any 401. The fix adds branching for a cosmetic gain.
  - `[medium]` `[patch]` (blind; same root as the first verification-gap finding) There are no tests for the real Phase Two client's status mapping, 401 re-token or token caching. — Added `PhaseTwoOrganizationsTests` with a stub handler and FakeTimeProvider (5xx, HttpRequestException, timeout, 409, 400, one 401 retry, token renewal).
  - `[low]` `[patch]` (blind) The 401 checks are hard-coded, and the matrix has no unauthenticated caller. — Added `EveryEndpointRefusesACallerWithoutAToken`, driven by the matrix samples.
  - `[low]` `[reject]` (blind) The hint probe writes `site.created` on a `hint-probe/` stream. — No projector routes by type alone; this only matters for a hypothetical future projector.
  - `[low]` `[reject]` (blind) `AddProjector<T>` called twice would start one runner twice. — Nothing registers a projector twice; before this change a double registration already produced two loops.
  - `[medium]` `[patch]` (blind; same root as the edge-case finding on names) Site names accept control characters and NUL. — `NormalizeSiteName` now rejects control characters and unpaired surrogates, and emoji stay valid. Unit tests were added.
  - `[low]` `[reject]` (blind) Integration tests never delete Keycloak clients, users or Organizations. — The local Keycloak database has no volume and is fresh on every AppHost run.
  - `[maybe-false]` `[reject]` (edge) An unknown JSON charset throws `InvalidOperationException`, which gives 500 instead of 400. — Unverified which exception `ReadFromJsonAsync` raises. If true, low: a malformed client request only.
  - `[medium]` `[patch]` (edge) A name containing U+0000 or a lone surrogate makes the jsonb append fail, so every retry answers 500. — Grouped with the name-validation patch above.
  - `[low]` `[reject]` (edge) Unreachable Keycloak discovery/JWKS answers 401, not 503. — Signing keys are cached after the first fetch, and during an outage sign-in cannot succeed either.
  - `[false]` `[reject]` (edge) An already-member add returns 409, so the retry is stuck. — Verified on live Phase Two 26.6.7: `PUT /orgs/{id}/members/{userId}` answers 201 again for an existing member.
  - `[low]` `[reject]` (edge) A non-5xx Phase Two refusal crosses the grain boundary as a 500 and leaves the key pending. — Same root as the "unexpected failures" row: misconfiguration only, and still Problem Details.
  - `[maybe-false]` `[reject]` (edge) A catch-up failure after `ConfirmEvents` answers 500 although the Site exists. — A retry takes the idempotent path. A persistent failure means the projector itself is broken (AD-21). If true, low.
  - `[low]` `[reject]` (edge) `AddProjector` twice leads to a double start. — Duplicate of the blind finding above.
  - `[maybe-false]` `[reject]` (edge, claim) The spec lists "not found" as a result outcome, but grains throw on Conflict or on 409 without an Organization. — Both are invariant violations, not expected outcomes, and neither is reachable through `UserGrain`'s own planned ID. If true, low.
  - `[medium]` `[patch]` (verification-gap) Real-client failure mapping is untested. — Grouped with the Phase Two client-test patch above.
  - `[medium]` `[patch]` (verification-gap) Keycloak failing during `Site.Initialize` is untested. — Added the `FailOnceOn(SiteWrite)` fake switch and a theory that checks 503, then no site events, then a resume with the same Site ID and the owner grant.
  - `[low]` `[patch]` (verification-gap) The matrix does not check the returned Role for Administrator and Member. — Allowed `GET` samples now assert that the body's `role` equals the seeded Role.
  - `[low]` `[reject]` (verification-gap, other) Test clients and users are never cleaned up. — Duplicate of the blind finding above.
  - `[low]` `[patch]` (intent-alignment) The TestCluster suite has no 404-path coverage, which AC5 names. — Added `ASiteThatWasNeverCreatedIsNotInTheProjection`.
  - `[false]` `[reject]` (intent-alignment) The Aspire immediate GET does not isolate the read-your-writes mechanism. — The TestCluster test isolates it, with hints off and a 10-minute poll interval. The AppHost test covers the outer surface.

### 2026-09-28 — Review pass
- verdicts: 32 findings — high 0, medium 1, low 24, false 4, maybe-false 3
- findings:
  - `[false]` `[reject]` (blind) The spec and sprint status disagree, and the Spec Change Log omits three departures: `RequestedAt`, pending keys that never expire, and completed keys that are never pruned. — `in-review` is this pass's own transient status. The Code Map names `requestedAt` in `UserState`, says a pending key resumes, and says completed keys expire, which `FindLive` does without pruning. None of these is a departure.
  - `[low]` `[patch]` (blind) The contract and README say a key is "kept 24 h", but a pending key is kept until it completes. — Reworded the `IdempotencyKey` description in `coldframe.openapi.json` and the README convention. The 422-for-ever part is carried from the earlier reject row (clients issue a new key per submission).
  - `[low]` `[patch]` (blind; same root as edge finding 6) The server counts name length in UTF-16 units, but the contract's `maxLength` counts code points, so 51–100 emoji get a 400. — `NormalizeSiteName` now counts runes after the surrogate check, and refuses input longer than 200 UTF-16 units before copying. Added a unit test: 100 emoji are valid, 101 are not.
  - `[low]` `[reject]` (blind) Names made only of zero-width or bidi-format (Cf) characters pass validation. — Real, but only a deliberate client sends them, and a Site name is shown only to that Site's own members. The fix adds a new character-class rule.
  - `[low]` `[reject]` (blind) Unmapped routes and 405s return empty bodies, and 500s have no typed `urn:coldframe:problem:*`. — The 500 part is carried from the earlier reject row. Clients call only contracted routes, and status-code pages would still not produce one of the seven typed problems.
  - `[low]` `[reject]` (blind) `EdgeProblems.WriteAsync` and `TypedResults.Problem` may differ in `traceId`/`instance`. — Cosmetic only. The contract's `ProblemDetails` needs neither field, and `type`, `title` and `status` are the same on both paths.
  - `[low]` `[reject]` (blind) JWT bearer settings are copied from configuration once, not bound through options. — No test and no runtime path reconfigures `Identity` after start, and `ValidateOnStart` checks the same section values.
  - `[low]` `[reject]` (blind) The contract-parity test does not require 401, 403 or 404 responses per operation. — This matters only for a hypothetical future endpoint. Both current operations document them.
  - `[low]` `[reject]` (blind) There are no tests for a token without `sub`, direct projector cases, a non-JSON body, or a Deleted lifecycle through the handler. — Keycloak access tokens always carry `sub`, and Deleted is unreachable before Epic 9. The rest are malformed-client paths that already answer 400.
  - `[low]` `[reject]` (blind) The identity read-model tables have no FK or CHECK constraints. — The projector is the only writer, and it writes only enum names.
  - `[low]` `[reject]` (blind) Bad `coldframe-server` credentials surface only at the first `POST /sites`. — Misconfiguration gives a Problem Details 500, as in the carried 500 row. A readiness probe is new surface.
  - `[low]` `[reject]` (blind) `KeycloakServiceAccount.Invalidate()` clears the token without taking the lock. — The worst case is one extra token request under concurrent 401s. The reference write is atomic, and a locked or compare-and-swap clear adds complexity.
  - `[maybe-false]` `[reject]` (edge) Once the 20 s budget is nearly spent, journaling and the unbounded catch-up can pass the 30 s Orleans timeout. — carried: the earlier unbounded-catch-up row still applies unchanged. Settling it needs a backlog measurement; if true it is low (a 500, then an idempotent retry).
  - `[low]` `[reject]` (edge) Concurrent `POST /sites` from one User during a Keycloak stall queue on the grain and exceed the Orleans timeout. — This needs one User to send parallel creations while Keycloak hangs, and the outcome is a 500 that an idempotent retry recovers from. A fix needs reentrancy or queue limits.
  - `[low]` `[reject]` (edge) A 200 token response without `access_token` would be cached as null. — Keycloak's client-credentials grant always returns it. A null token just means 401, then Invalidate, then one more request.
  - `[low]` `[reject]` (edge) `expires_in` of 30 s or less means a token request before every call. — The realm's token lifespan is minutes, and the fix adds a branch for a config the realm does not have.
  - `[low]` `[reject]` (edge) Zero-width and format-only names pass. — Duplicate of the blind Cf row.
  - `[low]` `[patch]` (edge) A name of 51–100 non-BMP characters gets a 400 that the contract allows. — Grouped with the blind rune-count patch.
  - `[low]` `[patch]` (edge) An idempotent re-`Initialize` passes the caller's budget token to the catch-up, so a budget that expires there answers 503 for a finished Site. — `SiteGrain` now catches up with `CancellationToken.None`, as it already does on the first initialization.
  - `[false]` `[reject]` (edge, claim) The spec says the Keycloak client's "total timeout is under 20 s", but `OperationBudget` allows up to 30 s. — The Code Map sentence is about the HTTP client: `HttpClient.Timeout` = `RequestTimeout`, which defaults to 10 s and is validated under 20 s. `OperationBudget` is the separate budget under the Orleans timeout that the intent names.
  - `[medium]` `[patch]` (verification-gap) Nothing tests `OperationBudget` running out while Keycloak hangs, so the 503 path for a stalled Phase Two is unverified. — Added `FakePhaseTwoOrganizations.StallOnce` and the theory `AKeycloakThatNeverAnswersEndsInUnavailableWhenTheBudgetRunsOutAndARetryResumes` (tag lookup, EnsureRole, GrantRole). It checks 503, no Site events, and a resume with the same Site ID. Removing the grain's OCE arm makes all three cases fail (checked by mutation).
  - `[low]` `[reject]` (intent) Pending keys never expire, although the intent says keys expire 24 h after the request. — carried: the earlier 422-for-ever reject row. The intent's Keycloak-down row requires a pending request to resume with the same Site ID, and expiring it would orphan its Organization.
  - `[false]` `[reject]` (intent) Read-your-writes with hints off is never combined with HTTP in one test. — carried: the earlier false row. The TestCluster isolates the mechanism, and the AppHost test covers the outer surface.
  - `[low]` `[reject]` (intent) The 24 h, other-User, Keycloak-down, retry-after-failure and 422-creates-nothing rows are proved at grain level, not over HTTP. — The handler is a thin call to the grain, and its outcome-to-HTTP mapping is unit-tested. The AppHost has no FakeTimeProvider or Keycloak fault injection.
  - `[low]` `[reject]` (intent) Expired tokens are tested only against the configured validation parameters, not over HTTP. — Expiry over HTTP needs real Keycloak time control. The HTTP tests already cover missing, wrong-issuer and wrong-audience tokens through the same bearer handler.
  - `[low]` `[reject]` (intent) Empty and non-ASCII keys are tested only on `CheckIdempotencyKey`. — The HTTP tests for a missing and an over-length key prove the handler calls that same function.
  - `[low]` `[reject]` (intent) The matrix seeds Sites A and B through journal events, bypassing the Site grain and Phase Two. — A documented design note: the policy reads only the projection, and non-Owner Roles cannot be created through the API before Epic 9.
  - `[false]` `[reject]` (intent) The matrix derives its expectations from the same rule metadata the policy reads. — `TheMappedEndpointsAndTheirRulesAreExactlyTheContractOperations` pins each rule to `x-coldframe-minimum-role` in the OpenAPI contract, so the expectations have an independent source.
  - `[low]` `[reject]` (intent) The missing-rule test sees only endpoints `MapEdgeApi` registers. — The Edge API is the only mapping, and the fallback policy still requires authentication for anything else.
  - `[low]` `[reject]` (intent) Only seven problem types exist, and other failures are untyped 500s. — carried: the earlier "unexpected failures" reject row.
  - `[maybe-false]` `[reject]` (intent) Only `/.well-known/healthz` is exempt from authentication, so other health paths may need a token. — Settling it needs a list of the paths `UseHealthCheckDefaults` maps. The AppHost health checks and the anonymous healthz test pass. If true, low.
  - `[maybe-false]` `[reject]` (intent) The budget covers only the Keycloak work, not the whole call. — carried: same as the edge budget row above.

## Design Notes

- **Read-your-writes without waiting on hints.** `Site.Initialize` confirms its events, then runs `CatchUpAsync` on the identity projector's runner. The projector remains the only writer of the read model; the grain only drives it. This keeps AD-1 while guaranteeing that the next request sees the membership. The TestCluster test proves it with hints off and a 10-minute poll interval.
- **Idempotency is layered.** `SiteCreationRequested` is persisted before any Keycloak call, so a retry resumes with the same Site ID. Recovery first looks up the Organization by its AD-3 tag. Creation then uses the persisted `id`, so a 409 resolves by `GET /orgs/{id}`. Both paths converge on one Organization.
- **Seeding the matrix through the journal.** Only Owners can be created through the API before Epic 9. The matrix therefore seeds Administrator and Member users as `site.membership-granted` events on fresh Site streams, so the projection, and with it the policy, sees exactly what real events would produce. Those Sites have no Organization. That is harmless, because the policy reads only the projection.
- **403 versus 404.** Per the spine's Errors convention, a Site that exists but where the caller has no Role gives 403, and a Site that does not exist gives 404. The projection holds a Site only once its grain is `Active`, which read-your-writes makes immediate.
- **Name and key limits.** Neither the PRD nor the UX specifies them. The Site name is trimmed and 1–100 characters. The key is 1–200 printable ASCII characters and is required on `POST /sites`, because Story 1.8's clients always send one.

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror`: expected to succeed.
- `dotnet format --verify-no-changes --no-restore`: expected to report no changes.
- `dotnet test --no-build`: expected to pass, including `Coldframe.Server.IntegrationTests` (Aspire AppHost on podman; the first run builds the Keycloak image).

## Auto Run Result

Status: done

**Summary.** This was a follow-up review pass on Story 1.6, which added `POST /sites` and `GET /sites/{siteId}` with Keycloak JWT bearer auth. `UserGrain` persists the request and creates the tagged Phase Two Organization. `SiteGrain` writes the roles, the membership and the Owner grant, journals `site.created` and `site.membership-granted`, and catches the identity projection up before it returns. One policy reads the caller's Role from that projection, and a generated matrix proves every endpoint's access rule. The first pass's summary and file list are in commit `09b7d820835afc92689415ba5bfde0993930c0b2`. This pass made four small patches.

**Files changed in this pass**
- `apps/cs/server/Edge/EdgeValidation.cs`: the Site name length now counts code points, matching the contract's `maxLength`, with a cheap early length cap.
- `apps/cs/server/Identity/SiteGrain.cs`: an idempotent re-`Initialize` catches the projection up without the caller's budget token.
- `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md`: the key lifetime is described accurately. A completed key lasts 24 h from the request; a pending one lasts until it completes.
- `tests/cs/server.integration/Identity/FakePhaseTwoOrganizations.cs`: a `StallOnce` switch that hangs a call until it is cancelled.
- `tests/cs/server.integration/Identity/SiteCreationTests.cs`: a theory where the budget runs out during a Keycloak stall, followed by a resume with the same Site ID.
- `tests/cs/server.tests/Edge/EdgeValidationTests.cs`: 100 emoji are valid and 101 are not.

**Review findings.** 32 in total: high 0, medium 1, low 24, false 4, maybe-false 3.
- **Patched (4 entries):** 1 medium and 3 low.
  - medium: the budget-expiry test;
  - low: the name length in code points;
  - low: the catch-up token on re-`Initialize`;
  - low: the key-lifetime docs.
- **Deferred:** none.
- **Rejected:** every other finding, each with its reason in the 2026-09-28 Review Triage Log entry above. They are false claims, carried rows, or low-impact paths whose fix would add complexity.

**Follow-up review recommended: false.** This is a follow-up pass and no high finding was patched, so the work has converged. Patched this pass: high 0, medium 1, low 3.

**Verification**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror`: 0 warnings, 0 errors.
- `dotnet format --verify-no-changes --no-restore`: clean (exit 0).
- `dotnet test --no-build`: 184 of 184 passed, including the TestCluster and AppHost suites on podman.
- Mutation check: with `UserGrain`'s `OperationCanceledException` arm removed, all three cases of the new stall theory fail. The code was restored afterwards.

**Residual risks**
- **Unbounded catch-up:** after journaling, the projector catch-up has no bound. With a large backlog, a call could still pass the 30 s Orleans timeout (maybe-false, carried).
- **Concurrent creations:** parallel `POST /sites` from one User while Keycloak stalls can queue past the Orleans timeout and answer 500. An idempotent retry recovers.
- **Unusual names:** names made only of zero-width or bidi-format characters are accepted.
- **Carried from the first pass:**
  - unexpected 500s have no typed problem;
  - JwtBearer keeps its default 5-minute `ClockSkew`;
  - idempotency keys are never pruned;
  - realm changes apply only on a fresh import;
  - CI has never run on GitHub (DW-6).
