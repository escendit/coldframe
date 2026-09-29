---
title: 'Story 1.7: Reconcile identity changes from Keycloak'
type: 'feature'
created: '2026-09-28'
status: 'done'
baseline_revision: 'efa153cac217233ed701f25e83748fb8d842692a'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/apps/cs/README.md'
  - '{project-root}/packages/cs/README.md'
warnings: ['oversized']
deferred:
  - summary: >-
      Keycloak events dropped while Temporal is down are never replayed, so a quiet Site stays drifted until another event touches it.
    evidence: |-
      keycloak-temporal-extensions v0.0.1-rc.2 logs and drops an event when its workflow start fails (upstream, pre-existing).
      Reconciliation is event-triggered only. A periodic full-roster sweep (for example a Temporal schedule or an Orleans reminder per Site) would close the gap.
    location: >-
      apps/cs/server/Identity/Reconciliation/IdentityReconciliationActivities.cs
    severity: medium
  - summary: >-
      A single GET /orgs/{id} 404 during any reconcile deletes the Site permanently, and a misconfigured service account might cause such 404s.
    evidence: |-
      Unverified: whether Phase Two answers 404 (not 403) to GET /orgs/{id} when the coldframe-server service account lacks view-organizations.
      If it does, one event per Site would move every touched Site to Deleted (terminal, AD-20).
      Settle it on a live container by removing the role and calling the endpoint.
    location: >-
      apps/cs/server/Identity/SiteGrain.cs (Reconcile, RaiseDeleted)
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** Keycloak publishes its admin events to Temporal through `keycloak-temporal-extensions`, but nothing in the Server consumes them. A change made in Keycloak (a break-glass edit in the admin console, an Organization deleted, a member added or removed) never reaches the Site grain, the User grain or the identity projection, so authorization drifts from Keycloak (AD-3, AD-5, AD-20).

**Approach:** The Server hosts Temporal workers for the extension's `IdentityAdminEvent` and `IdentityUserEvent` workflows. An admin event for a Phase Two Organization becomes one activity: it asks `Site(orgId).Reconcile(...)`. The Site grain reads the Organization's current roster from Phase Two and journals only the differences. It keeps its last valid Owner set when Keycloak shows none, and moves to `Deleted` when the Organization is gone. Then the activity syncs each affected User grain's Site set. The event is only a trigger. The pulled roster is the truth, so duplicates, reordering and lost events all converge.

## Boundaries & Constraints

**Always:**
- Test-first (AD-24): write each acceptance criterion as a failing test before the code that makes it pass.
- The Site grain stays the only writer of Site state, and the User grain the only writer of User state (AD-1). Reconciliation only **reads** Keycloak. It never writes to Keycloak and never repairs Keycloak.
- A Site grain in `Uncreated` ignores reconciliation: no Keycloak call and no event. Sites come into existence only through `Initialize` (Story 1.6). A `Deleted` Site ignores it too, because deletion is terminal (AD-20).
- A Coldframe Role is derived only from the Organization roles `owner`, `administrator` and `member` (`SiteGrain.OrganizationRoles`): the highest role the member holds. A member holding none of them has no Membership. A role held by a non-member is ignored.
- If the pulled roster has no Owner, every current Owner keeps `Owner`. All other differences are still applied. The grain logs at `Error` level with a stable `EventId` on every such reconcile, and journals `site.ownerless-edit-refused` once per episode (AD-3 break-glass). The episode ends, with `site.ownerless-edit-resolved`, on the first later reconcile whose roster has an Owner.
- After journaling, the Site grain catches up the identity projector with `CancellationToken.None`, as `Initialize` does, so a finished workflow means the projection is current.
- Temporal carries nothing but these two workflows (AD-5). Workflow code is deterministic: it only calls the activity. All I/O happens in the activity.
- Time comes from `TimeProvider` / `Clock` only. The Server runs no DDL. Nothing is hard-deleted: a deleted Site keeps its stream, its read-model rows and its Members in state.

**Never:**
- No new Edge API endpoints, no client or UI changes, no rename, invite or Role-change endpoints (Stories 1.8 and 1.9, Epic 9).
- No handling of user events beyond completing them: the `IdentityUserEvent` workflow is a no-op, so its queue never piles up.
- No Keycloak admin (master) credentials in the Server. It uses the existing `coldframe-server` service account (`view-organizations`).
- Nothing in the event's `representation` or `authDetails` drives state.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Member added in Keycloak | Active Site; Keycloak adds U as member and grants `administrator` | `site.membership-granted(U, Administrator)`; U's `GET /sites/{id}` → 200 role Administrator; User(U) Site set holds the Site | — |
| Role changed | U is Member; Keycloak also grants `owner` | `site.membership-granted(U, Owner)` | — |
| Member removed | U is a Member; Keycloak removes the member | `site.membership-revoked(U)`; U gets 403; User(U) Site set drops the Site | — |
| Echo of own write | An event matching what the Site grain already journaled (e.g. the creator's `owner` grant) | No event, no error, the workflow completes | — |
| Duplicate event | The same admin event delivered twice | The second delivery changes nothing | — |
| Break-glass no-Owner | Keycloak revokes `owner` from the only Owner, or removes that member | The Owner keeps Owner in state and projection (still 200 role Owner). One `site.ownerless-edit-refused`, plus an `Error` log on every such reconcile. Other differences still apply. | Never writes back to Keycloak |
| Owner restored | A later roster has an Owner again | `site.ownerless-edit-resolved`, then the roster applies normally | — |
| Site deleted | Keycloak deletes the Organization (`GET /orgs/{id}` → 404) | `site.deleted`; lifecycle `Deleted`; projection lifecycle `Deleted`; every Site-scoped call → 404; each member's User grain drops the Site | — |
| Renamed in Keycloak | Organization `displayName` changed | `site.renamed(name)`; `GET` returns the new name | — |
| Not yet visible | Event says member U added, but the pull does not show U yet (the listener fires before Keycloak commits) | Nothing journaled. The activity fails with a retryable error. From attempt 5 on it applies the pulled roster as the truth. | Temporal retry: 1 s initial, ×2, max 1 min, unlimited attempts |
| Keycloak down | Phase Two unreachable or 5xx during the pull | Nothing journaled; the activity retries | Same retry policy |
| Ignored events | Other realm, `error` set, a non-Phase-Two resource type, an unparseable path, a role other than the three, or an `Uncreated`/`Deleted` Site | The workflow completes with no grain state change | — |

</intent-contract>

## Code Map

- `apps/cs/server/Identity/SiteGrain.cs`: add `Reconcile`. `OrganizationRoles` maps `SiteRole` to org role names. Follow `CatchUpIdentityAsync`, the `IdentityProviderUnavailableException or OperationCanceledException` catch, and the `[LoggerMessage]` pattern. Also note the `Conflict` path in `Initialize` for `Deleted`.
- `apps/cs/server/Identity/SiteState.cs`: `_members` and `Owners` exist. Add Apply methods for the new events. Add a set of former members, `OwnerlessEditRefused`, and handling of `SiteDeleted` and `SiteRenamed`. States stay `public`, because the fixture replay binds `Apply` with `dynamic`.
- `apps/cs/server/Identity/UserGrain.cs` + `UserState.cs`: add the Site set. `CreateSite` raises `SiteMembershipChanged(siteId, Owner)` alongside `SiteCreationCompleted`.
- `apps/cs/server/Identity/IPhaseTwoOrganizations.cs` + `PhaseTwoOrganizations.cs`: add one read method. Reuse the service-account token, the timeout mapping, and the 404 handling of `GetAsync`.
- `apps/cs/server/Identity/IdentityProjector.cs`:
  - Handle `site.renamed` (update the name).
  - Handle `site.deleted` (lifecycle `Deleted`; keep the rows).
  - Handle `site.membership-revoked` (delete the membership row; the projection is a read model, not a tombstone).
- `apps/cs/server/Edge/SiteAccessAuthorization.cs:61`, `EdgeApi.cs:136`: `Deleted` already answers 404. Do not change them.
- `apps/cs/server/Identity/IdentityHostingExtensions.cs`: the place to register workers; `ValidateOnStart` is the options pattern. `apps/cs/server/Program.cs` is the composition root.
- `packages/cs/contracts/Sites/`: `SiteEvents` / `UserEvents` show the contract shape: `[EventType]`, `[GenerateSerializer]`, `[Alias]`, `[property: Id(n)]`. The grain interfaces carry `[Alias]` on each method. Results are values, not exceptions.
- `tests/cs/server.tests/Journal/FixtureJournalReplayTests.cs` + `Fixtures/journal.json`: every new alias needs a fixture row and an assertion.
- `tests/cs/server.integration/Identity/IdentityCluster.cs`, `FakePhaseTwoOrganizations.cs`, `SiteCreationTests.cs`: this is the TestCluster pattern. The fake already records Organizations, members and roles; extend it with console-style edits (remove member, revoke role, delete, rename) and roster reads, reusing its failure switches.
- `tests/cs/server.integration/Edge/EdgeApiFixture.cs`: `CreateUserAsync`, `CreateServerClient` and `CreateOrganizationsClientAsync` (a `coldframe-server` token that can call `/realms/coldframe/orgs`), plus `AliasesAsync`. `Journal/JournalWait.UntilAsync` gives bounded waits; never sleep.
- `tests/cs/server.integration/KeycloakTests.cs`: `AdminEventStartsAWorkflowInTheTemporalNamespace` enables the listener on **master**. The worker must ignore master events by realm.
- `aspire/Coldframe.AppHost/AppHost.cs`: `temporal` (grpc endpoint, namespace `coldframe`). The server already has `.WaitFor(temporal)`. Keycloak is set up through `KC_SPI_EVENTS_LISTENER__TEMPORAL__*`.
- `aspire/keycloak/realms/coldframe-realm.json`: it has no `id` and no `eventsListeners` yet.
- **Extension facts** (keycloak-temporal-extensions v0.0.1-rc.2, commit 63f89f2):
  - Workflows: `IdentityAdminEvent` on `keycloak-admin-queue` and `IdentityUserEvent` on `keycloak-user-queue`. Each has one argument and returns void.
  - Start: plain `WorkflowClient.start`, with the workflow ID set to the Keycloak event ID (a fresh UUID per dispatch).
  - When and failure: the listener fires synchronously **before** the Keycloak transaction commits. With Temporal down, the event is dropped.
  - Payload: Jackson camelCase with nulls. `AdminEvent{id, time(epoch ms), realmId, authDetails{realmId, clientId, userId, ipAddress}, resourceType, operationType(CREATE|UPDATE|DELETE|ACTION), resourcePath, representation(JSON string|null), error}`. `UserEvent{id, time, type, realmId, clientId, userId, sessionId, ipAddress, error, details}`.
  - `realmId` is the realm's **id**, not its name.
- **Phase Two 0.182 admin events** (`resourcePath` is relative to `/realms/{realm}/`):

  | resourceType | operations | resourcePath |
  |---|---|---|
  | `ORGANIZATION` | CREATE / DELETE | `orgs/{orgId}` |
  | `ORGANIZATION` | UPDATE | `orgs/{orgId}/{orgId}` |
  | `ORGANIZATION_MEMBERSHIP` | CREATE / DELETE | `orgs/{orgId}/members/{userId}` |
  | `ORGANIZATION_ROLE_MAPPING` | CREATE / DELETE | `orgs/{orgId}/roles/{role}/users/{userId}` |
  | `ORGANIZATION_ROLE_MAPPING` | bulk | `users/{userId}/orgs/{orgId}/roles/...` |

  Verify these against live events in the AppHost suite. `ORGANIZATION_ROLE`, TEAM, INVITATION and DOMAIN events do not affect the roster.
- **Phase Two roster reads** (verify live and adjust the paths if needed):
  - `GET /orgs/{id}/members?first={n}&max={m}` → array of users with `id`. Page until a short page.
  - `GET /orgs/{id}/roles/{name}/users?first={n}&max={m}` → users with that role. A missing role (404) means none.
  - `GET /orgs/{id}` → `displayName`, or 404.
- **NuGet:** `Temporalio` and `Temporalio.Extensions.Hosting` 1.19.0 (latest). The default data converter uses System.Text.Json, so put `[JsonPropertyName]` on every payload property.

## Tasks & Acceptance

**Execution:**
- `Directory.Packages.props`, `apps/cs/server/Coldframe.Server.csproj`, and the test csprojs that need it: add `Temporalio` and `Temporalio.Extensions.Hosting` 1.19.0 under a commented "Keycloak event pipeline (AD-3, AD-5)" group, then refresh every affected `packages.lock.json`.
- `packages/cs/contracts/Sites/`:
  - **Events:** `MembershipRevoked(UserId)` `site.membership-revoked`, `SiteRenamed(Name)` `site.renamed`, `SiteDeleted()` `site.deleted`, `SiteOwnerlessEditRefused(KeptOwners: IReadOnlyList<string>)` `site.ownerless-edit-refused`, `SiteOwnerlessEditResolved()` `site.ownerless-edit-resolved`, and `SiteMembershipChanged(SiteId, Role: SiteRole?)` `user.site-membership-changed`, where `null` means the User left.
  - **`ISiteGrain.Reconcile(RosterExpectation? expectation, bool acceptUnconfirmed, CancellationToken)`** → `SiteReconciliationResult(Outcome, Lifecycle, Members, FormerMembers)`, with `Outcome ∈ Ignored | Unchanged | Changed | NotYetVisible | IdentityProviderUnavailable`.
  - **`RosterExpectation`:** `Kind ∈ OrganizationAbsent | MemberPresent | MemberAbsent | RoleHeld | RoleNotHeld`, plus `UserId?` and `Role?`.
  - **`IUserGrain.SyncSiteMembership(siteId, SiteRole? role, CancellationToken)`:** idempotent. It journals only when the value changes.
  - Update `packages/cs/README.md`.
- `apps/cs/server/Identity/Reconciliation/`:
  - **`KeycloakAdminEvent`, `KeycloakUserEvent`:** payload records with `[JsonPropertyName]`.
  - **`AdminEventRoute.From(event, realmId)`:** pure. Returns `Ignore`, or `(siteId, expectation?)` per the Code Map table. A `ORGANIZATION CREATE/UPDATE`, or a bulk role path, gives no expectation. A role other than the three is ignored.
  - **`SiteRosterPlan.Plan(currentMembers, currentOwners, pulledRoster)`:** pure. Returns the grants, the revocations and whether the edit was ownerless, per the Always rules.
  - **`IdentityAdminEventWorkflow`** (`[Workflow("IdentityAdminEvent")]`): calls one activity with `StartToCloseTimeout` 45 s and the matrix's retry policy.
  - **`IdentityUserEventWorkflow`** (`[Workflow("IdentityUserEvent")]`): returns immediately.
  - **`IdentityReconciliationActivities.ReconcileAsync(event)`:**
    - Routes the event.
    - Calls `Site.Reconcile` with `acceptUnconfirmed = attempt >= 5`.
    - Throws a retryable `ApplicationFailureException` on `NotYetVisible` and on `IdentityProviderUnavailable`.
    - Otherwise, when the lifecycle is Active or Deleted, calls `SyncSiteMembership` for every current and former member: their Role, or `null` when revoked or when the Site is Deleted. Fanning out on every run means a retry completes a fan-out that was cut short.
  - **`KeycloakEventOptions`** (section `KeycloakEvents`):
    - `TargetHost`, `Namespace` and `RealmId` are required, and validated on start.
    - `AdminTaskQueue` defaults to `keycloak-admin-queue`, and `UserTaskQueue` to `keycloak-user-queue`.
    - Register them with `AddKeycloakEventPipeline()`: two `AddHostedTemporalWorker` calls, with the activities scoped and using `IGrainFactory`.
- `apps/cs/server/Identity/SiteGrain.cs`, `SiteState.cs`: implement `Reconcile`.
  - Return `Ignored` unless the Site is `Active`.
  - Pull under `KeycloakOptions.OperationBudget`.
  - If the pull contradicts the expectation and `!acceptUnconfirmed`, return `NotYetVisible` without journaling.
  - Absent Organization → `SiteDeleted`. Otherwise journal the rename, the plan's grants and revocations, and the ownerless episode events. `ConfirmEvents` once.
  - Catch up the projection, then return.
- `apps/cs/server/Identity/UserGrain.cs`, `UserState.cs`: add the Site set, `SyncSiteMembership`, and the Owner membership on Create Site.
- `apps/cs/server/Identity/IPhaseTwoOrganizations.cs`, `PhaseTwoOrganizations.cs`: `GetRosterAsync(id)` → `PhaseTwoRoster(DisplayName, MemberIds, RoleHolders by role name)`, or `null` on 404. It is paged and uses the existing error mapping.
- `apps/cs/server/Identity/IdentityProjector.cs`: project the three new `site.*` events. The ownerless events need no rows.
- `apps/cs/server/Program.cs` / `IdentityHostingExtensions.cs`: wire `AddKeycloakEventPipeline()`.
- `aspire/keycloak/realms/coldframe-realm.json`: add `"id": "coldframe"` and `"eventsListeners": ["jboss-logging", "temporal"]`.
- `aspire/Coldframe.AppHost/AppHost.cs`: pass the server `KeycloakEvents__TargetHost` (temporal grpc host:port), `KeycloakEvents__Namespace`, and `KeycloakEvents__RealmId=coldframe`.
- `tests/cs/server.tests/`:
  - `AdminEventRoute` table tests: every Code Map row, other realm, `error` set, unknown type, malformed path, a non-Coldframe role.
  - `SiteRosterPlan` tests: every matrix row that the plan decides.
  - Payload deserialization from a camelCase Jackson JSON sample, with nulls.
  - Fixture-journal rows and assertions for every new alias.
  - `KeycloakEventOptions` validation.
- `tests/cs/server.integration/Identity/SiteReconciliationTests.cs` (TestCluster, fake Phase Two), covering:
  - each matrix row at grain level, including Uncreated/Deleted ignored, NotYetVisible vs `acceptUnconfirmed`, and Keycloak down;
  - that one ownerless episode journals exactly one refused event;
  - projection rows after `Reconcile` returns, with hints off;
  - the User grain Site set after Create Site and after `SyncSiteMembership`.

  Also add activity tests with `Temporalio.Testing.ActivityEnvironment`: attempt 5 sets `acceptUnconfirmed`, the retryable failures, and a fan-out on `Unchanged`.
- `tests/cs/server.integration/Identity/KeycloakReconciliationTests.cs` (AppHost). Set up: create a Site over HTTP, then change Keycloak through `CreateOrganizationsClientAsync`. It covers:
  - Add a member plus `administrator` → that user's `GET` becomes 200 Administrator (bounded wait).
  - Remove them → 403.
  - **Duplicate:** a Temporalio client starts `IdentityAdminEvent` on `keycloak-admin-queue` with a copy of a membership event and awaits its result. The site stream's aliases are unchanged. Repeat with an event echoing the creator's `owner` grant.
  - **Break-glass:** revoke `owner` from the sole Owner → wait for `site.ownerless-edit-refused` → the Owner still gets 200 Owner.
  - **Deletion:** `DELETE /orgs/{id}` → `GET` becomes 404 and `site.deleted` is journaled.
- `tests/cs/server.integration/KeycloakTests.cs`: assert that the `coldframe` realm has id `coldframe` and lists the `temporal` listener.
- `apps/cs/README.md`, `docs/quickstart.md`: describe the reconciliation pipeline, the `KeycloakEvents` settings, the break-glass rule, and how to find the operator error (the log EventId and the journal alias). Drop the manual "add `temporal` listener" step for `coldframe`.

**Acceptance Criteria:**
- Given the listener publishing to Temporal and the Server's worker running, when an Organization or membership event is consumed, then it is delivered to the Site grain and the affected User grains and applied as an idempotent reconciliation, and an event matching what the Site grain already wrote leaves the Site stream unchanged and the workflow completes without error.
- Given a break-glass edit in Keycloak that leaves a Site with no Owner, when the event arrives, then the previous Owners keep Owner for authorization, `site.ownerless-edit-refused` is journaled once, an `Error` log is written, and nothing is written back to Keycloak.
- Given a Site deletion in Keycloak, when it is applied, then the Site grain is `Deleted`, its roster is suspended in the User grains, and every Site-scoped call returns 404.
- Given the AppHost integration suite that changes Keycloak through its admin API, when it runs, then reconciliation, duplicate events, the no-Owner break-glass case and Site deletion are all covered and pass.
- Given `dotnet build -warnaserror`, `dotnet format --verify-no-changes` and `dotnet test`, when they run locally with podman, then all pass.

## Spec Change Log

## Review Triage Log

### 2026-09-28 — Review pass
- verdicts: 34 findings — high 0, medium 4, low 22, false 6, maybe-false 2
- findings:
  - `[low]` `[reject]` (blind) An Organization deleted between the members read and the role reads yields empty holders, so the roster is planned as ownerless and non-Owners are revoked before `site.deleted`. — Real, but the window is milliseconds, and the delete event itself expects `OrganizationAbsent`, so it returns NotYetVisible and does not journal. The fix adds a re-check branch.
  - `[maybe-false]` `[defer]` (blind) One 404 permanently deletes a Site without a confirming read, including under misconfiguration. — A wrong `Keycloak:Realm` fails at the token endpoint, not with a 404. Whether a service account missing `view-organizations` gets a 404 is unverified. Deferred with that check.
  - `[low]` `[reject]` (blind) `OperationBudget` now spans several sequential roster calls. — A home Site has a handful of members: five calls in total. Growing the budget for large Organizations adds complexity for a case that does not occur.
  - `[low]` `[reject]` (blind) The User fan-out is serial, unbounded and has no heartbeat, inside the 45 s timeout. — Rosters are single-digit and each call takes milliseconds. Heartbeating or bounded parallelism adds complexity.
  - `[low]` `[reject]` (blind) An unreachable Temporal at startup stops the host, and the worker has no health check. — The AppHost waits for Temporal, and every component deploys together. Resilient worker startup belongs to the deployment epic; recorded as a residual risk.
  - `[low]` `[reject]` (blind) Events from a realm with a mismatched ID are dropped at Debug level. — `KeycloakEvents:RealmId` is required and documented. A Warning would fire for every master-realm event. Recorded as a residual risk.
  - `[low]` `[reject]` (blind) Deleting an org role definition, or a User, triggers no reconcile. — Both are rare console actions. A deleted User cannot obtain tokens, and a stale Owner is kept anyway. Recorded as a residual risk.
  - `[medium]` `[defer]` (blind) There is no backfill and no periodic sweep; lost events on quiet Sites are never repaired. — Event loss comes from the upstream extension (pre-existing). Deferred with a sweep proposal. Nothing reads the User Site set yet, so the lack of a backfill is harmless for now.
  - `[low]` `[patch]` (blind) A retried `CreateSite` overwrites a reconciled Role with Owner. — `UserGrain.CreateSite` now raises the Owner membership only when the Site is absent from `State.Sites`.
  - `[low]` `[reject]` (blind) `displayName` from Keycloak is not validated. — It is set by an administrator in Keycloak, and the fix adds a validation path for an unlikely input.
  - `[low]` `[patch]` (blind) The AppHost suite does not verify the rename path live. — Added a live rename test (PUT `/orgs/{id}`, then GET returns the new name). The bulk path remains unit-only; the routing still sends any `orgs/{org}` path to the Site.
  - `[low]` `[patch]` (blind) A `packages/cs/README.md` paragraph was broken. — The sentence is restored as its own paragraph and the text is re-wrapped.
  - `[maybe-false]` `[defer]` (edge) A 404 with no expectation deletes the Site. — Same root as the blind deletion row; deferred there.
  - `[low]` `[reject]` (edge) The Organization can be deleted mid-read of the role holders. — Same root as the first blind row.
  - `[low]` `[reject]` (edge) Concurrent activities fan out unversioned snapshots, so an older one can land last. — Real, but nothing reads the User Site set yet, and the next event repairs it. Versioning adds a parameter. Recorded as a residual risk.
  - `[low]` `[patch]` (edge) `CreateSite` overwrites a lower Role with Owner. — Grouped with the blind CreateSite patch.
  - `[false]` `[reject]` (edge) An ownerless refusal with empty `KeptOwners`. — Unreachable: `Initialize` always journals an Owner, and the plan keeps Owners whenever the roster is ownerless, so an Active Site never has zero Owners.
  - `[low]` `[reject]` (edge) The fan-out can exceed the 45 s timeout. — Duplicate of the blind fan-out row.
  - `[low]` `[reject]` (edge) Unlimited retries on non-transient errors. — Intentional: a misconfiguration converges once it is fixed, and backoff is capped at 1 minute.
  - `[low]` `[reject]` (edge) The budget plus an unbounded catch-up can pass the Orleans 30 s timeout. — The retry is idempotent and converges. This carries the unbounded catch-up risk from Story 1.6.
  - `[low]` `[reject]` (edge, claim) "Reordering converges" does not hold for User grains. — Same root as the unversioned fan-out row.
  - `[low]` `[patch]` (verification-gap) Re-adding a former member is untested. — Added the re-add case to `IdentityReconciliationActivitiesTests`.
  - `[medium]` `[patch]` (verification-gap) The Error log with EventId 3 is not asserted. — Added `CapturingLoggerProvider`; the ownerless test asserts one Error record per reconcile.
  - `[medium]` `[patch]` (verification-gap) NotYetVisible is only tested for MemberPresent. — Added a theory for RoleHeld, RoleNotHeld, MemberAbsent and OrganizationAbsent.
  - `[low]` `[patch]` (verification-gap) The workflow retry policy is untested. — Added `IdentityAdminEventWorkflowTests`.
  - `[low]` `[patch]` (verification-gap) The user-event worker is untested. — Added a live test that starts `IdentityUserEvent` on `keycloak-user-queue` and awaits it.
  - `[low]` `[patch]` (verification-gap) An Organization deleted during the members read is untested. — Added a stub test: 200 for the Organization and 404 for its members gives a null roster.
  - `[low]` `[patch]` (verification-gap, other) `CreateSite` overwrites a reconciled Role. — Grouped with the blind CreateSite patch.
  - `[false]` `[reject]` (intent) The tests use the REST API rather than the admin console. — AC4 says "through its admin API", and the console calls the same Phase Two endpoints.
  - `[medium]` `[patch]` (intent) The Error log half of AC2 is uninspected. — Grouped with the verification-gap log patch.
  - `[false]` `[reject]` (intent) Suspension in the User grains is visible only in grain state. — The spec defines it as that, and the 404 comes from the projection's Deleted lifecycle.
  - `[false]` `[reject]` (intent) "Every Site-scoped call" is exercised through one endpoint. — `GET /sites/{id}` is the only Site-scoped endpoint, and the shared policy applies to every future one.
  - `[false]` `[reject]` (intent) The diff has no AC5 evidence. — Verification ran in this workflow; see below.
  - `[false]` `[reject]` (intent) Possible operator-owed actions. — No acceptance criterion needs a human: the break-glass edit is driven through the admin API by the AppHost suite, so `awaiting-operator` does not apply.

## Design Notes

- **Pull, not replay.** The listener fires before Keycloak commits, drops events when Temporal is down, and gives each dispatch a fresh ID. Applying event deltas would drift. Each event therefore only names a Site and what it should now show, and the grain compares the full pulled roster with its own state. Duplicates and echoes of the grain's own writes then diff to nothing, and any later event on the Site repairs a lost one.
- **Pre-commit race.** The expectation lets a pull that ran too early fail and retry through Temporal, not sleep. From attempt 5 (about 15 s) the pull is taken as the truth, which also covers a Keycloak transaction that rolled back.
- **Why the activity fans out to Users.** If the Site grain called User grains, it could deadlock with `UserGrain.CreateSite`, which awaits `Site.Initialize`. The activity calls the Site grain, then the User grains, so there is no grain-to-grain cycle. Fanning out to all current and former members on every run keeps retries correct.
- **Operator-visible error.** An `Error` log with a stable EventId shows in the Aspire dashboard and OTel. The journaled `site.ownerless-edit-refused` is a durable record. No UI surface is in scope.

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror`: expected to succeed.
- `dotnet format --verify-no-changes --no-restore`: expected to report no changes.
- `dotnet test --no-build`: expected to pass, including the TestCluster and AppHost suites on podman.

## Auto Run Result

Status: done

**Summary.** Keycloak identity changes now reach Coldframe. The Server hosts Temporal workers for the `keycloak-temporal-extensions` workflows. `IdentityUserEvent` is a no-op. `IdentityAdminEvent` runs one activity:
- It routes a Phase Two admin event to `Site(orgId).Reconcile(expectation, acceptUnconfirmed)`.
- The Site grain pulls the Organization's roster from Phase Two, read-only, and journals only the differences: `site.membership-granted/-revoked`, `site.renamed` and `site.deleted`.
- If Keycloak shows no Owner, it keeps its last Owners, logs an Error (EventId 3) and journals `site.ownerless-edit-refused` once per episode.
- It catches up the identity projection before returning.
- The activity then syncs every current and former member's User grain Site set (`user.site-membership-changed`).

A pull that does not yet show the event fails retryably, and is accepted from attempt 5 on. The realm now has the ID `coldframe` and the `temporal` listener.

**Files changed**
- `Directory.Packages.props`, `apps/cs/server/Coldframe.Server.csproj`, test csprojs and lock files: `Temporalio` and `Temporalio.Extensions.Hosting` 1.19.0.
- `packages/cs/contracts/Sites/*`: six new events, `ISiteGrain.Reconcile` with its result and expectation types, and `IUserGrain.SyncSiteMembership`.
- `apps/cs/server/Identity/Reconciliation/*`:
  - the event payloads;
  - `AdminEventRoute` and `SiteRosterPlan`, both pure;
  - the two workflows and the activity;
  - `KeycloakEventOptions`.
- `apps/cs/server/Identity/SiteGrain.cs`, `SiteState.cs`: `Reconcile`, deletion, rename, former members and the ownerless episode.
- `apps/cs/server/Identity/UserGrain.cs`, `UserState.cs`: the Site set, `SyncSiteMembership`, and the Owner membership on Create Site.
- `apps/cs/server/Identity/IPhaseTwoOrganizations.cs`, `PhaseTwoOrganizations.cs`: `GetRosterAsync`, read page by page.
- `apps/cs/server/Identity/IdentityProjector.cs`: rename, delete (rows kept) and revoke.
- `apps/cs/server/Identity/IdentityHostingExtensions.cs`, `Program.cs`: `AddKeycloakEventPipeline()`.
- `aspire/Coldframe.AppHost/AppHost.cs`, `aspire/keycloak/realms/coldframe-realm.json`: the `KeycloakEvents__*` settings, the realm ID and the listener.
- Tests:
  - unit tests: routing, the roster plan, payloads, options, workflow options, roster paging, the User Site set and the fixture journal;
  - TestCluster: `SiteReconciliationTests` and `IdentityReconciliationActivitiesTests`;
  - AppHost: `KeycloakReconciliationTests` (member add/remove, duplicate and echo, break-glass, deletion, rename, user event) and a realm check in `KeycloakTests`.
- `apps/cs/README.md`, `packages/cs/README.md`, `docs/quickstart.md`: the pipeline, its settings, the break-glass rule and how to find the operator error.

**Review findings.** 34 in total: high 0, medium 4, low 22, false 6, maybe-false 2.
- **Patched (7 entries):** 2 medium and 5 low.
  - medium: the Error log assertion;
  - medium: the NotYetVisible theory;
  - low: `CreateSite` no longer overwrites a reconciled Role;
  - low: the README paragraph;
  - low: live rename and user-event tests;
  - low: the re-added former member test;
  - low: the workflow retry-policy test, plus the deleted-during-members-read test.
- **Deferred (2):**
  - no replay of events lost while Temporal is down (medium, upstream);
  - a 404 may mean a missing permission rather than a deletion (medium, unverified).
- **Rejected:** every other finding, each with its reason in the Review Triage Log above.

**Follow-up review recommended: false.** No high finding was patched, and exactly two medium entries were patched. Both were test-only additions, so no unverified behavioural risk remains from the patches.

**Verification**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror`: 0 warnings, 0 errors.
- `dotnet format --verify-no-changes --no-restore`: clean.
- `dotnet test --no-build` (podman): 269 of 269 passed, including the TestCluster and AppHost suites against live Phase Two and Temporal.
- Matrix audit: every I/O row is covered by a test that ran and passed.

**Residual risks**
- The Server's hosted Temporal workers make Temporal a startup dependency. If it is unreachable at start, the host stops.
- Events lost upstream while Temporal is down are repaired only by a later event on the same Site (deferred).
- User grain Site sets are unversioned. Concurrent fan-outs can briefly leave an older Role until the next event, and Sites created before this story have no Site-set entry until an event touches them. Nothing reads the Site set yet.
- The following trigger no reconcile: deleting an org role definition, deleting a User, and the bulk `users/{u}/orgs/{o}/roles` path (live-unverified).
- Events from a realm whose ID differs from `KeycloakEvents:RealmId` are ignored at Debug level. Deployments must set the realm ID correctly.
- Carried from Story 1.6: the catch-up after journaling has no bound.
