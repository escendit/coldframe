---
title: 'Story 9.1: Invite someone to my Site'
type: 'feature'
created: '2026-10-14'
status: 'awaiting-operator'
baseline_revision: 'a5be0248dbac166872ec8880b0a058cd39ed9f16'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-9-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/EXPERIENCE.md'
warnings: ['oversized']
operator_actions:
  - "Create the coldframe-smtp Secret (host, port, from, username, password) with the real SMTP account in the production namespace before deploying this release; the keycloak chart now requires it."
  - "In the Keycloak admin console of the existing coldframe realm, set Realm settings > Themes > Email theme to coldframe."
  - "In the same realm, fill in Realm settings > Email (SMTP host, port, from address, authentication, encryption) and send a test email."
  - "In the same realm, enable Authentication > Required actions > Invitation (invitation-required-action)."
  - "Set keycloak.invitationRedirectUri under the server key of the site values to https://<domain>/garden and redeploy."
  - "On the home network, invite a test address from the app, open the emailed link on the garden's Wi-Fi, accept, and confirm the person appears in Members with the invited Role."
deferred:
  - summary: >-
      A Phase Two invitation whose email cannot be sent is reported to the Owner as sent.
    evidence: |-
      keycloak-orgs 0.183 InvitationsResource.createInvitation (and resendEmail) catch the send exception, log
      "Unable to send invitation email" and still answer 201/204, so the Server cannot tell; the Owner sees
      "Invitation sent to <email>" and a retry gets 409 already-invited. Detecting it would need a new approach
      (e.g. an SMTP precheck needing realm view permission) that this story's intent does not settle.
    location: >-
      apps/cs/server/Identity/PhaseTwoOrganizations.cs InviteAsync
    severity: medium
  - summary: >-
      No test follows the emailed invitation link through the web app's /garden to the Phase Two page.
    evidence: |-
      InvitationTests asserts the link equals the configured redirect URI and accepts by signing in on Keycloak
      directly; the web app is not part of the AppHost, so link -> web sign-in -> invitation required action is
      unverified end to end. Settle by running the web app against the AppHost and following the Mailpit link.
    location: >-
      tests/cs/server.integration/Edge/InvitationTests.cs
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** An Owner cannot share a Site. There is no invite endpoint, no Members surface on web, iOS or Android, no invitation email that says "accept on the home network", and accepting a Phase Two invitation never reaches the Site and User grains: Phase Two's accept step emits only a Keycloak *user* event (`CUSTOM_REQUIRED_ACTION` with details `org_id`, `invitation_id`), and the Server completes every user event without acting on it.

**Approach:** Add an Owner-only `POST /sites/{siteId}/invitations` that the Site grain turns into a Phase Two native invitation (no Server-side invitation state), and an Owner-only `GET /sites/{siteId}/members`. Route the accept user event into the existing Site reconciliation, which already fans out to User grains (they pull open Alerts). Build the Members surface (tiles + Invite form) on all three apps, the "Accepted an invitation?" Create Site state, a Coldframe email theme with the invitation template, and Mailpit + SMTP wiring so it is testable end to end.

## Boundaries & Constraints

**Always:**
- Only an Owner invites and lists Members; Administrator and Member get 403 `forbidden` from the API, and every app *hides* (never disables) the Members surface and its nav/Settings entry for them.
- The Site grain is the only caller of the Phase Two invitations API (`POST /realms/{realm}/orgs/{orgId}/invitations`, body `{email, roles:[<owner|administrator|member>], send:true, redirectUri?}`, `roles` from `SiteGrain.OrganizationRoles`). It raises no journal event and stores nothing for an invitation.
- Role on the Members list comes from the identity projection; display name and email come from Phase Two's `orgs/{orgId}/members` read at request time (nothing new persisted about Users).
- Copy, exactly: Role descriptions — Member "Member sees everything and gets the same Alerts, but can't change settings.", Administrator "Administrator also changes Devices, Thresholds, Calibration and Pause, but not Members.", Owner "Owner can do everything, including inviting people and changing Roles."; button "Send invite"; success notice "Invitation sent to <email>. They accept from the email, on the garden's Wi-Fi."; Create Site hint "Accepted an invitation? It can take a minute to appear."
- Every new UX surface has a named failing-first test (UX-DR ids per the repo's existing naming conventions); the OpenAPI contract is updated first and the TS client regenerated, KT DTOs updated by hand.

**Never:**
- No pending-invitation list, no in-app accept surface, no invitation table, column, event or deadline on the Server.
- No Role change or Membership removal (Story 9.2); Member tiles in 9.1 are display-only.
- No auto-switch to a newly joined Site when the user already has Sites; only the no-Site (Create Site) state opens the new Site automatically.
- Never use token claims for the Role; never call Phase Two from anywhere but the Site grain (invite) and the members read.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Invite | Owner, `{email:"ana@example.com", role:"Member"}` | 202 Accepted, empty body; Phase Two creates and emails the invitation with role `member` | — |
| Bad input | email blank/invalid or role missing/unknown | 400 `validation` with field detail; no Phase Two call | Web/mobile show one line under the field |
| Already invited / already a member | Phase Two answers 409 | 409 `urn:coldframe:problem:already-invited`, title "That person is already invited or a member of this Site." | Inline notice with that text; form keeps its values |
| Not Owner | Administrator or Member caller | 403 `forbidden` | Members surface not rendered |
| Phase Two down / email send fails | 5xx, timeout, or Phase Two 500 | 503 `identity-provider-unavailable`, detail "The invitation was not sent. Try again." | Retry-able Inline notice |
| Invitee accepts | `CUSTOM_REQUIRED_ACTION` user event, no error, `details.org_id` = Site id | Site reconciled with expectation `MemberPresent(userId)`; User grain gets `SyncSiteMembership(siteId, role)` and pulls open Alerts | Same retry/`acceptUnconfirmed` policy as admin events |
| Invitee rejects / other user events | event with `error` set, other type, other realm, missing `org_id` | Ignored (workflow completes) | — |

</intent-contract>

## Code Map

Server (`apps/cs/server`, contracts `packages/cs/contracts`):
- `Identity/IPhaseTwoOrganizations.cs:46` + `Identity/PhaseTwoOrganizations.cs` -- add `InviteAsync(orgId, email, role, redirectUri?, ct) → PhaseTwoInviteOutcome {Sent, Conflict}` and `GetMemberProfilesAsync(orgId, ct) → IReadOnlyList<PhaseTwoMemberProfile(UserId, Name, Email)>?`; reuse `SendAuthorizedAsync` (:314) / `SendAsync` (:220, maps 5xx → `IdentityProviderUnavailableException`, which covers Phase Two's 500 on email failure); `EnsureStatus` (:295). Name = `firstName lastName` trimmed, else `username`.
- `Identity/SiteGrain.cs` -- `OrganizationRoles` (:78); copy the `Rename` pattern (:180-216: Active check, `OperationBudget` CTS, catch unavailable/cancel) for `Invite(email, role, ct) → SiteInviteResult(SiteInviteOutcome {Sent, NotFound, AlreadyInvited, IdentityProviderUnavailable})`; no `RaiseEvent`. Result records live near `SiteRenameOutcome` (:754-784).
- `packages/cs/contracts/Sites/SiteGrains.cs:186` -- add `Invite` to `ISiteGrain`; `SiteRole.cs` enum Member<Administrator<Owner.
- `Identity/KeycloakOptions.cs` -- add optional `InvitationRedirectUri` (absolute URI); passed as `redirectUri` when set.
- `Edge/EdgeApi.cs:571` `MapEdgeApi` -- map `POST /sites/{siteId}/invitations` (`.WithName("inviteToSite").RequireSiteRole(SiteRole.Owner)`) and `GET /sites/{siteId}/members` (`listSiteMembers`, Owner); follow `RenameSiteAsync` (:805) + `ToHttpResult` (:845). Members: read projection memberships, join profiles, sort Owners first then by name.
- `Edge/EdgeValidation.cs` -- add `NormalizeEmail` (trim, one `@`, `System.Net.Mail.MailAddress` parse, ≤ 254 chars) and role parsing by name.
- `Edge/EdgeProblems.cs` -- add `already-invited` problem type; README "Errors" list in `packages/openapi/README.md`.
- `Identity/IdentityReadModel.cs` -- add `ListMembersAsync(siteId) → (userId, role)[]` over `identity_memberships`.
- `Identity/Reconciliation/IdentityEventWorkflows.cs` -- `IdentityUserEventWorkflow` runs a new activity for routable events; reuse `IdentityAdminEventWorkflow.ReconcileOptions`.
- `Identity/Reconciliation/IdentityReconciliationActivities.cs:40` -- extract the body after routing into a shared `ReconcileRouteAsync(AdminEventRoute route, string? eventId)`; add `[Activity("ReconcileUserEvent")] ReconcileUserEventAsync(KeycloakUserEvent)`. Register it wherever the existing activity is registered (grep `ReconcileAsync`/`AddActivity`).
- `Identity/Reconciliation/UserEventRoute.cs` (new) -- pure `From(KeycloakUserEvent, realmId) → AdminEventRoute?`: realm matches, `Error is null`, `Type == "CUSTOM_REQUIRED_ACTION"`, `Details["org_id"]` and `UserId` non-empty → `new AdminEventRoute(orgId, new RosterExpectation(MemberPresent, userId))`. `KeycloakEvents.cs` already carries `Details`.
- `UserGrain.cs:158-178` `SyncSiteMembership` -- already journals the Membership and pulls `ISiteGrain.OpenAlerts()`; read-only, no change expected.

Contract & clients:
- `packages/openapi/coldframe.openapi.json` -- add both operations with `x-coldframe-minimum-role: Owner`, schemas `InvitationRequest {email, role}` and `SiteMember {userId, name, email, role}`; README access table rows. `oasdiff breaking` must stay clean (additions only).
- `packages/ts/api-client` -- `pnpm --filter @coldframe/api-client generate` refreshes `src/schema.ts` (staleness test in `tests/ts/api-client`).
- `packages/kt/core/.../api/ApiDtos.kt`, `ColdframeApi.kt:52` -- new `MembersApi` (`listMembers`, `invite`) implemented by `ColdframeApi`; checked by `tests/kt/core/jvmTest/.../OpenApiContractTest.kt`.

Web (`apps/ts/web/src`):
- `routes/(app)/members/+page.svelte` (stub today) + new `+page.server.ts` -- load members (redirect to `/garden` if not Owner), `invite` form action; pattern: `lib/server/site-settings.ts:131` and `settings/site/+page.server.ts`; error mapping `lib/server/sites.ts:100` (add 409 → `alreadyInvited`).
- `lib/members.ts` (new) -- `membersAccessOf(role)`, role descriptions, initials; like `lib/lots.ts:62`.
- `lib/components/SideNav.svelte:18-25` + `AppShell.svelte:30` + `(app)/+layout.svelte:89` -- add `role` prop; hide Members unless Owner (update `components.test.ts:182`).
- `lib/components/MemberTile.svelte` (new) -- initials square (current user `primary` fill + `ink-on-bright`; others `layer-01` + `text-primary`), name, Role line uppercase. Reuse `TextInput` (`type="email"`), `SegmentedChoice` (no `name`; `helper` = description), `Button`, `InlineNotice`.
- `routes/(app)/sites/new/+page.svelte` -- hint + `invalidateAll()` every 10 s while mounted; `shell.ts:308` already routes no-Site users here, and `/` → `/garden` once a Site exists.
- `lib/i18n/en.json` -- new `members.*` / `createSite.invitationHint` keys.

Mobile:
- KMP `packages/kt/core/.../sites/` -- `MembersEngine` (StateFlow, `canManageMembers = role == Owner`, email/role state, `sendInvite()`, notices) + `MembersSnapshot` + `iosMain` `IosMembers`; wire in `sites/SitesWiring.kt`, expose via `AndroidSignIn`/`IosSignIn`; follow `lots/LotsEngine.kt:446-495` and `lots/Lots.kt:132-147`.
- `sites/SitesEngine.kt` (`select` :275) -- while the Site list is empty, refresh every 10 s; when it turns non-empty, select the first Site (opens overview; `push/PushEngine.kt:200-216` then prompts).
- Android `ui/settings/SettingsScreen.kt:40,136` (Members row, Owner only), `ui/shell/AppShell.kt:156-380` (`membersOpen` like `siteSettingsOpen`), new `ui/settings/MembersScreen.kt`, `ui/sites/CreateSiteScreen.kt` hint, `res/values/strings.xml`; components `TextInput` (email `keyboardOptions`), `SegmentedChoice`, `PrimaryButton`, `InlineNotice`.
- iOS `Sources/ColdframeIOS/Presentations.swift:88` `SettingsRow.members`, `UI/Screens.swift:233`, new `MembersPresentation.swift` + `UI/MembersViews.swift`, `UI/SitesViews.swift` Create Site hint, `UI/Components.swift:131` `TextInputField` gains an email keyboard/content-type parameter, `Localizable.xcstrings` + `L10n.swift`, app adapter `App/CoreMembersService.swift` like `App/CoreLotsService.swift`.

Deployment:
- `aspire/keycloak/themes/coldframe/email/` (new) -- `theme.properties` (`parent=base`), `html/invitation-email.ftl`, `text/invitation-email.ftl`, `messages/messages_en.properties` overriding `invitationEmailSubject`/`invitationEmailBody`/`invitationEmailBodyHtml` (args `{0}` email, `{1}` realm, `{2}` Site, `{3}` inviter, `{4}` link) to add: "Open the link at home, on the garden's Wi-Fi: accepting needs the home network." Template args verified in `keycloak-orgs-0.183.jar`.
- `aspire/keycloak/Dockerfile` -- `COPY aspire/keycloak/themes /opt/keycloak/themes/` in the final stage (owned 2000:2000); `deploy/images/keycloak.cst.yaml` file-existence test.
- `aspire/keycloak/realms/coldframe-realm.json` -- `"emailTheme": "coldframe"`, `smtpServer` with `${COLDFRAME_SMTP_HOST}`, `${COLDFRAME_SMTP_PORT}`, `${COLDFRAME_SMTP_FROM}`, `${COLDFRAME_SMTP_USER}`, `${COLDFRAME_SMTP_PASSWORD}` placeholders (auth/starttls via placeholders too).
- `aspire/Coldframe.AppHost/AppHost.cs:71-138` -- add Mailpit container (`axllent/mailpit`, pinned tag), feed SMTP env to Keycloak, set `Keycloak__InvitationRedirectUri` to the web endpoint `/garden`.
- `deploy/charts/keycloak/templates/deployment.yaml` + `values.yaml` -- SMTP env from Secret `coldframe-smtp` (`host`,`port`,`username`,`password`,`from`); server chart value for `Keycloak__InvitationRedirectUri`; `deploy/SECRETS.md:23` now "consumed".

Tests:
- `tests/cs/server.tests/` -- `PhaseTwoOrganizationsTests.cs` (invite body/409/5xx, profiles), `Identity/Reconciliation/UserEventRouteTests.cs` (new), EdgeValidation email tests, response mapping like `Edge/SiteRenameResponseTests.cs`, `EdgeEndpointDiscoveryTests.cs` + `EdgeEndpointCatalog.cs:37` samples.
- `tests/cs/server.integration/` -- `Identity/FakePhaseTwoOrganizations.cs:21` gains invitations + profiles; `Edge/AuthorizationMatrixTests.cs` samples (`_samples[...]` :37/:113; allowed Owner status for invite on a matrix Site without an Organization = the 503/404 path, as rename does at :58-62); new `Edge/InvitationTests.cs` on the AppHost: Owner invites → Mailpit has the mail with the home-network sentence and link; accept via Phase Two (admin API on behalf of the invitee or driving the required action) → `GET members` lists the invitee with Role within the reconciliation timeout.
- `tests/ts/web/` (`members.test.ts`, `components.test.ts`, `coverage.test.ts:8`), `tests/ts/web.e2e/specs/members.spec.ts` + `fixtures/fake-idp.ts` server routes; `tests/kt/core` (`MembersEngineTest`, `SitesEngineTest`), `tests/kt/android` (`MembersScreenTest`, `CreateSiteScreenTest`, guard tests `StringsTest`/`CoverageTest`/`AccessibilityTest`), `tests/swift/ios` (`MembersPresentationTests`).

## Tasks & Acceptance

**Execution:**
- `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md` -- add the two operations, schemas, problem type and access rows -- contract-first (AD-10).
- `tests/cs/server.tests/**`, `tests/cs/server.integration/**` -- write the failing tests listed in the Code Map first (I/O matrix rows each get a test) -- NFR16.
- `apps/cs/server/Identity/IPhaseTwoOrganizations.cs`, `PhaseTwoOrganizations.cs`, `KeycloakOptions.cs`, `SiteGrain.cs`, `packages/cs/contracts/Sites/SiteGrains.cs` -- invitation call and grain method, no state.
- `apps/cs/server/Identity/IdentityReadModel.cs`, `Edge/EdgeApi.cs`, `Edge/EdgeValidation.cs`, `Edge/EdgeProblems.cs` -- endpoints, validation, problems.
- `apps/cs/server/Identity/Reconciliation/UserEventRoute.cs`, `IdentityEventWorkflows.cs`, `IdentityReconciliationActivities.cs` (+ worker registration) -- accept event → reconciliation.
- `aspire/keycloak/themes/**`, `aspire/keycloak/Dockerfile`, `aspire/keycloak/realms/coldframe-realm.json`, `deploy/images/keycloak.cst.yaml`, `aspire/Coldframe.AppHost/AppHost.cs`, `deploy/charts/keycloak/**`, `deploy/charts/server/templates/deployment.yaml` + `deploy/charts/server/values.yaml`, `deploy/SECRETS.md` -- email theme, Mailpit, SMTP, redirect URI.
- `packages/ts/api-client/src/schema.ts` -- regenerate.
- `apps/ts/web/src/**` + `tests/ts/web/**` + `tests/ts/web.e2e/**` -- Members page, nav gating, Member tile, Invite form, Create Site hint and refresh.
- `packages/kt/core/**` + `tests/kt/core/**` -- MembersApi, MembersEngine/Snapshot/IosMembers, SitesEngine first-Site selection.
- `apps/kt/android/**` + `tests/kt/android/**` -- Members row/screen, Create Site hint; re-record Roborazzi snapshots that change.
- `apps/swift/ios/**` + `tests/swift/ios/**` -- Members row/presentation/views, Create Site hint, email text field.
- `docs/` -- one paragraph in the operator/deploy docs on SMTP + email theme for existing realms.

**Acceptance Criteria:**
- Given an Owner on web, iOS or Android, when they open Members (web side nav; mobile Settings → Members), then Member tiles show every Membership with initials, name and Role, plus the Invite form with an email field, the Member/Administrator/Owner Segmented choice whose one-line description updates on selection, and "Send invite".
- Given the Owner submits a valid email and Role, when the Server answers 202, then the form clears and the Inline notice reads "Invitation sent to <email>. They accept from the email, on the garden's Wi-Fi.", and the Members list is unchanged.
- Given the AppHost stack, when an Owner invites, then Mailpit receives an email from the Coldframe theme whose body says accepting needs the home network and whose link opens the Phase Two page; the Server's database has no row or event about the invitation.
- Given the invitee accepts, when the `CUSTOM_REQUIRED_ACTION` event reaches the Server, then `GET /sites/{siteId}/members` lists them with the invited Role, their User grain holds the Site and its open Alerts, and an app with no Site (Create Site showing "Accepted an invitation? It can take a minute to appear.") opens that Site's overview on its next refresh, where the mobile push prompt of Story 6.5 appears.
- Given an Administrator or Member, when they look for Members, then no Members nav item, Settings row or form is rendered, and `POST /sites/{siteId}/invitations` and `GET /sites/{siteId}/members` return 403 (covered by the authorization matrix).

## Spec Change Log

## Review Triage Log

### 2026-10-14 — Review pass
- verdicts: 33 findings — high 0, medium 7, low 18, false 7, maybe-false 1
- findings:
  - `[medium]` `[patch]` Contract/README/web comment said 202 = email sent and 503 = email not sent — reworded: 202 = Phase Two created the invitation and was asked to email it (failed send not reported); 503 = Keycloak unreachable/5xx/budget. TS client regenerated.
  - `[medium]` `[patch]` Keycloak chart now requires the `coldframe-smtp` Secret, so an upgrade without it leaves Keycloak in CreateContainerConfigError — install.md now says the Secret must exist before upgrading; also an operator action.
  - `[low]` `[patch]` Site name reaches the HTML invitation email unescaped — `orgName` escaped with `?replace` for & < > " before `msg`; covered by the AppHost InvitationTests run.
  - `[low]` `[reject]` Timeout after Phase Two created the invitation makes Try again answer 409 — rare (20 s budget) and the 409 text is truthful; fix needs idempotency/outcome redesign.
  - `[low]` `[reject]` Client abort logged as Keycloak unavailable — same pattern as the existing Rename; fix adds a guard for a log-only effect.
  - `[low]` `[reject]` Create Site membership poll runs while hidden/backgrounded and forever — one GET per 10 s only while the user has no Site; lifecycle-aware polling adds complexity.
  - `[low]` `[patch]` Web and KMP initials differ (first+last vs first two words; '' vs '?') — KMP now first and last word with email local-part fallback, web returns '?' when empty; tests updated.
  - `[false]` `[reject]` IdentityUserEventWorkflow untested / filter duplicated — the real workflow runs end to end in InvitationTests on the AppHost; an other-realm event only costs one activity that `UserEventRoute.From` ignores.
  - `[false]` `[reject]` Web invite trusts hidden siteId/siteName — the Server authorizes Owner on that siteId, the form invites to the Site the page shows, and siteName is rendered escaped by Svelte.
  - `[low]` `[reject]` Members list is all-or-nothing when Keycloak is down — 503 is the specified behaviour; a partial list needs a new notice state.
  - `[low]` `[reject]` AppHost hardcodes http://localhost:5173/garden — the realm's coldframe-web client hardcodes the same registered origin; consistent.
  - `[low]` `[reject]` No rate limit on invitations — Owner-only on a self-hosted Server, duplicates refused with 409; throttling adds new surface.
  - `[low]` `[reject]` Mobile shows any 400 as a bad email — clients send the Role enum, so only a contract mismatch reaches it; same as web.
  - `[low]` `[reject]` Spec and code drift (Android path, matrix label, empty logs) — fix edits this build's spec.
  - `[low]` `[reject]` Phase Two 400/403 on invite escapes as 500 — only exotic emails or a misconfigured service account; mapping needs a new contract outcome.
  - `[low]` `[reject]` Members read non-404 4xx or malformed body gives 500 — same handling as the existing roster read.
  - `[low]` `[reject]` Timeout-after-create retry gets "already invited" — same as the earlier timeout row.
  - `[low]` `[patch]` KMP MembersEngine kept a failed-read notice after a successful re-read — read notices are dropped on success, invitation notices kept; tests added.
  - `[low]` `[patch]` KMP MembersEngine Try again on a read notice re-sent an old failed invitation — `loadFailed` clears `failedInvite`; test added.
  - `[medium]` `[patch]` `coldframe-smtp` required on upgrade (edge-case view) — grouped with the install.md fix above.
  - `[false]` `[reject]` Empty or stale siteId posts to another Site — the page always renders the current Site's id; the Server authorizes it.
  - `[false]` `[reject]` An invalid Role gets a retryable notice that re-sends Member — unreachable from the page, whose Segmented choice posts only the three Roles.
  - `[low]` `[patch]` Web initials '' vs KMP '?' — grouped with the initials fix above.
  - `[low]` `[patch]` Web Members load on a deleted Site showed "couldn't list" with a usable form — 404 now redirects to Garden; test extended.
  - `[low]` `[reject]` Android `membersOpen` survives a Role drop and reopens when Owner again — unlikely sequence; fix adds an effect guard.
  - `[medium]` `[patch]` GET members 503 path untested — handler mapping extracted to `MembersResultAsync`, two tests added (503 on unavailable, 200 empty names on missing Organization).
  - `[medium]` `[patch]` Create Site poll 401 path untested — `uxDr90A401DuringTheMembershipPollEndsTheSessionAndStopsThePoll` added.
  - `[medium]` `[patch]` InvitationRedirectUri absolute-URL validation untested — `KeycloakOptionsTests` added.
  - `[medium]` `[defer]` Email send failure returns 202 (spec row label assumed a Phase Two 500) — Phase Two swallows send failures; deferred with evidence; the row's defined inputs (5xx, timeout, Phase Two 500) do map to 503 and are tested.
  - `[false]` `[reject]` Spec not at awaiting-operator with operator_actions — finalization sets it; the review ran mid-run.
  - `[maybe-false]` `[defer]` AC3 link not followed end to end — needs the web app running against the AppHost; deferred.
  - `[false]` `[reject]` AC4 User grain / push prompt not asserted on the real flow — `AnAcceptedInvitationReachesTheSiteAndTheNewMembersUserGrain` covers the User grain; the prompt is the existing PushEngine path reached via the tested first-Site selection.
  - `[false]` `[reject]` Existing production realm untested — not a code defect; recorded as operator actions.

## Design Notes

Why a user event: Phase Two's `InvitationRequiredAction.processAction` calls `OrganizationModel.grantMembership` and `OrganizationRoleModel.grantRole` directly and only emits `EventType.CUSTOM_REQUIRED_ACTION` (details `org_id`, `invitation_id`; rejection emits the same type with error "User invitation revoked."). No `ORGANIZATION_MEMBERSHIP` admin event fires, so without the new route the Membership would only appear after some unrelated admin event on that Organization. Routing to the existing `ISiteGrain.Reconcile(MemberPresent(user))` keeps one reconciliation path: the grain pulls the roster, derives the highest Role (`SiteRosterPlan`), persists, and the activity fans out to User grains.

```csharp
// UserEventRoute.From — sketch
if (e.RealmId != realmId || e.Error is not null || e.Type != "CUSTOM_REQUIRED_ACTION"
    || e.UserId is not { Length: > 0 } user
    || e.Details?.GetValueOrDefault("org_id") is not { Length: > 0 } org) return null;
return new AdminEventRoute(org, new RosterExpectation(RosterExpectationKind.MemberPresent, user));
```

Invitation link target: `redirectUri` = `Keycloak:InvitationRedirectUri` (web `/garden`), so a web invitee lands in the app; with no Site yet the shell sends them to Create Site, whose 10-second refresh opens the Site once the Membership arrives.

## Verification

**Commands:**
- `dotnet build Coldframe.slnx -warnaserror` -- expected: 0 warnings, 0 errors
- `dotnet test --project tests/cs/server.tests` -- expected: all pass
- `dotnet test --project tests/cs/server.integration` -- expected: all pass, including `InvitationTests` and the authorization matrix (needs Podman/Docker)
- `pnpm --filter @coldframe/api-client generate && git diff --exit-code packages/ts/api-client/src/schema.ts` after commit -- expected: no diff
- `pnpm lint && pnpm typecheck && pnpm test` -- expected: all pass
- `pnpm --filter @coldframe/web-e2e test` -- expected: all pass
- `./gradlew :core:jvmTest :android:testDebugUnitTest ktlintCheck` -- expected: all pass
- `docker run --rm --security-opt label=disable -v "$PWD":/work -w /work swift:6.3.3 sh -c 'swift build && swift test && swift format lint --strict -r .'` -- expected: pass (presentation layer; SwiftUI views build on the macOS CI runner)

**Manual checks (if no CLI):**
- The production realm already exists and is not re-imported: `emailTheme` and SMTP must be set on it by an operator (recorded as an operator action).

## Auto Run Result

Status: awaiting-operator

**Summary.** An Owner can now invite someone by email with a Role on web, iOS and Android. The Site grain asks Phase Two to create and email a native invitation (`POST /sites/{siteId}/invitations`, Owner only, 202/400/409/503); the Server stores nothing about it. Owners see Members (`GET /sites/{siteId}/members`, Roles from the identity projection, names/emails from Phase Two). Phase Two's accept step emits only a `CUSTOM_REQUIRED_ACTION` user event, so `UserEventRoute` now routes it into the existing Site reconciliation, which fans out to the invitee's User grain (it pulls open Alerts). Create Site shows "Accepted an invitation? It can take a minute to appear." and opens the first Site that arrives. A Coldframe email theme says to accept on the home network; Mailpit, realm SMTP, the Invitation required action and Helm SMTP wiring make it work end to end.

**Files changed (142).**
- `packages/openapi/*`, `packages/ts/api-client/src/*`: two Owner-only operations, `already-invited` problem, regenerated client.
- `apps/cs/server/Identity/{IPhaseTwoOrganizations,PhaseTwoOrganizations,SiteGrain,KeycloakOptions,IdentityReadModel,IdentityHostingExtensions}.cs`, `packages/cs/contracts/Sites/SiteGrains.cs`: invitation call, member profiles, grain `Invite`, redirect option.
- `apps/cs/server/Identity/Reconciliation/{UserEventRoute,IdentityEventWorkflows,IdentityReconciliationActivities}.cs`: accept event → reconciliation.
- `apps/cs/server/Edge/{EdgeApi,EdgeValidation,EdgeProblems}.cs`: endpoints, email/Role validation, problem type.
- `apps/ts/web/src/**`: Members page, MemberTile, Owner-only nav and Settings row, Create Site hint and 10 s refresh.
- `packages/kt/core/**`: MembersApi, MembersEngine/Snapshot/IosMembers, SitesEngine membership poll and first-Site selection.
- `apps/kt/android/**`, `apps/swift/ios/**`: Members screens, Settings rows, Create Site hint, email field.
- `aspire/keycloak/{Dockerfile,realms,themes}`, `aspire/Coldframe.AppHost/AppHost.cs`, `deploy/charts/{keycloak,server}/**`, `deploy/images/keycloak.cst.yaml`, `deploy/fleet/*`, `deploy/SECRETS.md`, `docs/operations/install.md`: email theme, FreeMarker email provider, Mailpit, SMTP, Invitation required action, redirect URI, operator docs.
- `tests/**`: server unit/integration (incl. AppHost invite → Mailpit → accept), web unit and e2e, KMP core, Android, iOS presentation tests; snapshots re-recorded.

**Review findings.** 33 findings (high 0, medium 7, low 18, false 7, maybe-false 1). Patched: 13 rows in 10 fixes (contract wording, install.md Secret note, HTML-escaped Site name in the email, aligned initials, two MembersEngine notice/retry fixes, web 404 redirect, tests for members 503, poll 401 and redirect-URI validation). Deferred: 2 (Phase Two swallows email send failures so the Owner sees "sent"; emailed link not followed end to end through the web app). Rejected: 18 with reasons in the Review Triage Log.

**Follow-up review recommendation: true.** Patched by verdict: medium 5 entries, low 5 entries, high 0. Named unverified risk: the iOS SwiftUI views, `App/CoreMembersService.swift` and the Kotlin `iosMain` bridge (`IosMembers`) were never compiled on this Linux host; only the macOS CI runner will build them.

**Verification.** `dotnet build Coldframe.slnx -warnaserror` 0 warnings/0 errors; `tests/cs/server.tests` 1112 passed; `tests/cs/server.integration` 614 passed (19 min, includes `InvitationTests` against Phase Two + Mailpit and the authorization matrix); `pnpm lint && pnpm typecheck && pnpm test` passed (web 856, e2e 168); `pnpm --filter @coldframe/web-e2e test` 168 passed; regenerating the TS client gives no diff; `./gradlew :core:jvmTest :android:testDebugUnitTest ktlintCheck` passed; Swift container `swift build && swift test && swift format lint --strict` passed (405 tests). Matrix audit: every I/O row has a passing covering test.

**Residual risks.**
- Phase Two reports a failed email send as success (201); the Owner sees "Invitation sent" when SMTP is broken (deferred).
- Existing installations need the operator actions above: the realm is not re-imported, and the keycloak chart now requires `coldframe-smtp`.
- The AppHost now builds the Keycloak image from the repository root (to copy the theme).
- iOS UI code is unbuilt on this host (CI macOS runner builds it).
- A mobile invitee who already has Sites is not switched to the new Site (by design); only the no-Site state opens it automatically.
