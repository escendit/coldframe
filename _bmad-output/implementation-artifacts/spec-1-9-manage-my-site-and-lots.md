---
title: 'Story 1.9: Manage my Site and Lots'
type: 'feature'
created: '2026-09-28'
status: 'awaiting-operator'
baseline_revision: '2f8774e69e2c1558c5abba4a1286592f4114d3e8'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-8-create-site-and-the-empty-garden-in-the-apps.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/EXPERIENCE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/DESIGN.md'
warnings: ['oversized']
deferred: []
operator_actions:
  - "On a Mac with Xcode 26.6, run `swift build && swift test && swift format lint --strict -r .` at the repository root and fix any compile or test failure in the never-compiled SwiftUI code (apps/swift/ios/Sources/ColdframeIOS/UI/SiteSettingsViews.swift, SitesViews.swift, Screens.swift), including the new RenderTests for Site settings and Garden with Lots at .accessibility5."
  - "On that Mac, run `xcodegen generate` and `xcodebuild build -scheme Coldframe -destination 'generic/platform=iOS Simulator' CODE_SIGNING_ALLOWED=NO` in apps/swift/ios, and confirm apps/swift/ios/App/CoreLotsService.swift compiles against the ColdframeCore framework (check the Kotlin/Native names of the IosLots methods and adjust if they differ)."
  - "Install the Android app against a real Server and Keycloak, sign in as an Owner, and verify: renaming the Site in Site settings shows the new name in the Garden header and the Site switcher; creating Tomatoes and Beans shows two no-Node tiles on Garden in the Server's order; renaming and removing a Lot (with the confirmation) updates Garden; a Member sees Site settings read-only; at font scale 2 with TalkBack nothing clips."
  - "Run the iOS app against the same Server on an iPhone and verify the same Site settings, Lot tile and Member read-only behaviour at the largest Dynamic Type size with VoiceOver."
---

<intent-contract>

## Intent

**Problem:** An Owner cannot rename a Site, and nobody can create, rename or remove Lots. The Server has no Lot grain, no Lot read model and no Lot endpoints. No client has a Site settings surface; the Site menu's "Site settings" item still opens the Settings index (DW-25). Garden shows no Lot tiles (epics.md 744–782; FR-6; AD-2, AD-3, AD-18, AD-20; UX-DR18, 20, 74, 84).

**Approach:**
- **Server:** `PATCH /sites/{siteId}` (Owner) renames the Site through the Site grain, which first updates the Keycloak Organization `displayName`. A new event-sourced Lot grain, a `lots` projection and read model, and five Lot endpoints under `/sites/{siteId}/lots`. Lot creation is idempotent through the Site grain.
- **Clients (web BFF, KMP core + Android, iOS):** one Site settings surface (rename Site, Lots list with create/rename/remove), reached from the Settings index and the Site menu. Garden shows the Site's Lots as *no Node* tiles in the Server's order, below the Story 1.8 empty-state content.

## Boundaries & Constraints

**Always:**
- Test-first (AD-24). Each AC and each UX-DR in this story (18, 20, 74, 84) first gets a failing test named `UX-DRnn …` (web/Swift) or `` `UX-DRnn …` ``/`uxDrNn…` (Kotlin). Add the ids to `tests/ts/web/coverage.test.ts` `storyIds` and to `tests/kt/android/.../CoverageTest.kt` `storyIds` and `iosIds`.
- **Domain (AD-1/2/18/20):** the Lot grain is the only writer of a Lot. Events: `LotCreated(SiteId, Name)`, `LotRenamed(Name)`, `LotClaimed(NodeId)`, `LotReleased(NodeId)`, `LotRemoved`. A Lot holding a claim refuses removal and journals nothing. Removal is `LotRemoved` plus a tombstone: the read-model row stays, marked removed, and the ID stays resolvable. Lot IDs are UUIDv7 from the injected `TimeProvider`.
- **Site rename (AD-3):** the Site grain checks it is `Active`, calls Phase Two to set the Organization `displayName`, then journals `SiteRenamed` and catches up the identity projection (read-your-writes). A Keycloak failure journals nothing and returns 503. Renaming to the current name makes no call and no event.
- **Read-your-writes:** after each Lot event the Lot grain catches up the lots projection before returning, so the next `GET` shows the change.
- **Idempotency:** `POST /sites/{siteId}/lots` requires `Idempotency-Key`. The Site grain stores `{callerSub}:{key}` → Lot ID for 24 h, as `UserGrain`/`UserState.FindLive` do for Sites. The same key with the same name returns the first Lot (201, same body). The same key with a different name returns 422 `idempotency-key-reused`. Clients keep one key per create attempt: the same key after 503 or a network failure, a new key after 422 or success.
- **Names:** Site and Lot names are trimmed, 1–100 characters (`EdgeValidation.MaxSiteNameLength`); otherwise 400 `validation`. Lot names need not be unique.
- **Order:** `GET /sites/{siteId}/lots` returns live Lots in the AD-14 status order (`needsWater, needsCalibration, unknown, ok, paused, noNode`), then `created_at`, then `lot_id`. Clients never re-sort (UX-DR20).
- **Status:** the projection sets `noNode` when unclaimed and `unknown` when claimed. The contract `LotStatus` enum lists all six AD-14 values.
- **Clients render only** (AD-14): the browser never calls the Server; Swift and Compose never see a token or URL; Roles come from the Server. Controls a Role cannot use are hidden, not disabled (UX-DR84).
- **Copy:** every string externalised (`en.json`; `strings.xml` + `Localizable.xcstrings` with identical keys, plus `L10n` cases). Calm, literal, glossary capitals, no exclamation marks. Buttons name the result, with in-place working labels: "Rename Site"/"Renaming Site…", "Create Lot"/"Creating Lot…", "Rename Lot"/"Renaming Lot…", "Remove Lot"/"Removing Lot…".
- Square corners, 1 px borders, no shadows, no animation, Carbon icons from the tokens, light and dark, no clipping at the largest text size.

**Never:**
- No Site Reminder cadence, Members management, Pause, Lot detail, Add a Node flow, `Claim`/`Release` grain methods, `readmodel.changed` refetch, pull-to-refresh, or inline "+ New Lot" in a Lot picker. Those belong to later epics.
- Lot tiles are not tappable in 1.9 (no Lot detail or Add a Node yet). No client status computation.
- No hand-written TypeScript API types. No change to `POST /sites`, `GET /sites` or `GET /sites/{siteId}` behaviour.
- No new Carbon icons: `add` is already vendored; Lot row actions are text buttons.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Rename Site | Owner, `PATCH /sites/{A}` `{name:"Home garden"}` | 200 `Site` with the new name. Keycloak Organization `displayName` updated. `GET /sites` and the web tabs / mobile switcher and Garden header show it | 400 invalid name; 503 Keycloak down, nothing changes |
| Rename Site, not Owner | Administrator or Member | Control hidden on every client; API 403 `forbidden` | 403 race → "You can't change this on {site}. Ask an Owner or Administrator." |
| Create Lots | Admin+, `POST …/lots` "Tomatoes" then "Beans", each with a new key | 201 `Lot {id, name, status:"noNode"}` + Location. Garden shows both as *no Node* tiles; Site settings lists them | 400 invalid name |
| Create retried | Same key, same name | 201 with the first Lot; one Lot exists | same key, other name → 422, new key on the client |
| List Lots | Member+ | 200 `{lots:[…]}` live Lots in Server order; removed Lots absent | 404 unknown Site (policy) |
| Rename Lot | Admin+, `PATCH …/lots/{id}` `{name}` | 200 `Lot`; Garden and Site settings show the new name | 400; 404 `lot-not-found` if the Lot is removed or on another Site |
| Remove Lot, no Node | Admin+, confirm "Remove Lot Tomatoes?" → `DELETE …/lots/{id}` | 204. `lot.removed` in the journal, row tombstoned, Lot gone from Garden and lists. `GET …/lots/{id}` → 200 with `removed: true`. A second DELETE → 204, no new event | 404 other Site or unknown id |
| Remove Lot holding a Node | fixture `LotClaimed` on the stream | 409 `lot-claimed`; no event, Lot still listed. Clients show "Move or unassign the Node on {lot} first." | — |
| Member in Site settings | Member | Site name and Lots shown read-only, no create/rename/remove controls, one notice "Only Owners and Administrators can change Lots." API 403 for the mutations | — |
| Garden with Lots | Any Role, Lots exist, no Node has Readings | Story 1.8 header, "No Readings yet", first-run tiles and notices stay; below them the Lot grid: each tile shows the Lot name, the `add` icon, a large "+", foot "add a Node", transparent fill, 1 px dotted `status-no-node-border`, ink `status-no-node-ink`; spoken as "{lot}, no Node, add a Node" | Lot load failure → the existing Unreachable/Certificate notice in place of the grid |
| Other status | a Lot with status ≠ `noNode` (only via fixture in 1.9) | Tile shows only the Lot name (no icon, value or status claim) | — |
| Session expired | 401 on any call | web: sign-in redirect; mobile: signed out with the SignedOut notice | — |

</intent-contract>

## Code Map

**Server (C#)**
- `packages/cs/contracts/Sites/SiteEvents.cs:42` — `SiteRenamed(Name)` already exists (alias `site.renamed`); reuse it. Every event carries `[EventType("…")] [GenerateSerializer] [Alias("coldframe.…")]`. `EventTypeRegistry` scans the Contracts assembly, so new events need no registration.
- `packages/cs/contracts/Sites/SiteGrains.cs` — `ISiteGrain` (`Initialize`, `Reconcile`), result records with outcome enums, `[Alias]` per method, trailing `CancellationToken`. Add `Rename` and `CreateLot` here; add `Lots/LotGrains.cs` (`ILotGrain : IGrainWithStringKey`) and `Lots/LotEvents.cs`.
- `packages/cs/contracts/Sites/UserEvents.cs` + `apps/cs/server/Identity/UserState.cs:31,54` + `UserGrain.cs:344` — the idempotency pattern to copy: `…Requested(key, id, name, requestedAt)` confirmed before the side effect, then `…Completed`; `FindLive` with a 24 h lifetime; a pending entry never expires.
- `apps/cs/server/Identity/SiteGrain.cs` — `Initialize` (37) shows Keycloak-then-journal, the `IdentityProviderUnavailableException`/`OperationCanceledException` → `IdentityProviderUnavailable` mapping, `OperationBudget` (95), and `CatchUpIdentityAsync` (205). `Reconcile` raises `SiteRenamed` when `displayName` differs (156), so a rename that skips Keycloak would be reverted.
- `apps/cs/server/Identity/SiteState.cs` — `Apply` overloads per event; add the Lot-creation idempotency entries.
- `apps/cs/server/Identity/IPhaseTwoOrganizations.cs`, `PhaseTwoOrganizations.cs` (`SendAuthorizedAsync`, `EnsureStatus`), `UserGrain.EnsureOrganizationAsync:447` (DisplayName = Site name) — add `UpdateDisplayNameAsync(siteId, displayName, ct)`; also in `tests/cs/server.integration/Identity/FakePhaseTwoOrganizations.cs`.
- `apps/cs/server/Journal/JournaledStreamGrain.cs` (stream id = grain id, e.g. `lot/{id}`; `Clock`), `IProjector.cs`, `JournalServiceCollectionExtensions.cs:55` `AddProjector<T>()`, `ProjectionRunner` (`GetProjectionRunner<T>().CatchUpAsync`).
- `apps/cs/server/Identity/IdentityProjector.cs`, `IdentityReadModel.cs` (`ListSitesAsync`, ordering), `IdentityHostingExtensions.AddSiteIdentity:267` — the patterns for `LotsProjector` (`"lots"`), `LotsReadModel` and a `AddLots` registration.
- `apps/cs/migrations/Migrations/` — `M{yyyyMMddHHmmss}CreateTable{Name}`, `[Migration(…)]`, `ForwardOnlyMigration`, `AsCustom("text"|"timestamptz")`, `pk_…`/`ix_…`. Latest is 20260928130100; `MigrationSetTests` guards.
- `apps/cs/server/Edge/EdgeApi.cs:48` `MapEdgeApi`, `CreateSiteAsync:67`, `ToHttpResult:114`, `SiteResponse`; `EdgeValidation.cs` (`CheckIdempotencyKey`, `NormalizeSiteName`, limits); `EdgeAccessRule.cs` (`RequireSiteRole`, `RequireAuthenticatedCaller`); `EdgeProblems.cs` (problem types); `SiteAccessHandler.Canonicalize`.
- `packages/openapi/coldframe.openapi.json` — the contract; linked into `tests/cs/server.tests` as `Fixtures/coldframe.openapi.json` (edit only this file). `EdgeEndpointDiscoveryTests` requires the `"METHOD /route Rule"` set to match exactly.
- Tests: `tests/cs/server.integration/Edge/AuthorizationMatrixTests.cs:26` `Samples` (Owner/Admin/Member on Site A, other Site B, seeded via `edge.AppendAsync`, no Keycloak Organization); `EdgeApiFixture` (`AppendAsync`, `WaitForIdentityCheckpointAsync` hard-coded to `identity`); `Identity/IdentityCluster.cs:~135` (TestCluster, `FakeTimeProvider`, projector registration); `tests/cs/server.tests/Journal/FixtureJournalReplayTests.cs:21` `States` + `Fixtures/journal.json` (every alias/schemaVersion needs a row).

**TypeScript client and web** (`apps/ts/web/src`)
- `packages/ts/api-client` — `pnpm --filter @coldframe/api-client generate` rewrites `src/schema.ts`; `src/index.ts` type aliases; staleness test `tests/ts/api-client/index.test.ts:29`.
- `lib/server/sites.ts` — `SitesError`, `statusError:30` (no 403/404/409 yet), `call:45`, `createSite:70`. `lib/server/create-site.ts:51` — the action/key pattern. `lib/server/shell.ts` — `loadShell`, `pickCurrentSite`; the layout reloads Sites on every navigation.
- `routes/(app)/garden/+page.svelte` (no server load yet), `routes/(app)/settings/+page.svelte` (Appearance row + Account), `routes/(app)/sites/new/*` (enhance, working state, `announce`).
- `lib/site-menu.ts` (item links to `/settings`), `lib/sites.ts` (`checkSiteName`), `lib/roles.ts`, components `Button`, `TextInput`, `InlineNotice`, `Modal` (no form submit: use `onaction` → `requestSubmit()` on a hidden form), `RoleGate`, `Icon` (`add` registered), `SiteSummaryHeader`, `FirstRunSteps`.
- `lib/i18n/en.json` (flat keys, plural objects), `glossary.json` has Lot/Node/Site.
- Tests: `tests/ts/web/*` (svelte/server `render`, fake `server(answer)` in `sites.test.ts:27`, `FakeCookies`), guards `no-hard-coded-copy`, `copy`, `styles`, `components.test.ts:246` (component list). e2e: `tests/ts/web.e2e/fixtures/fake-idp.ts` (fake Server, exact `/sites` match, `/control/sites`), `specs/helpers.ts` (`shellPages`, `resetSites`), `specs/garden.spec.ts` (+ `-snapshots/`), `specs/accessibility.spec.ts`.

**KMP core** (`packages/kt/core/src`)
- `commonMain/.../api/ColdframeApi.kt` (`call`, `failureOf:92`, GET/POST only), `ApiResult.kt` (`ApiFailure`), `ApiDtos.kt`.
- `commonMain/.../sites/SitesEngine.kt` (`SitesApi` interface, session guard, `created()` re-list), `Sites.kt` (`CreateSiteForm.validate`, `SitesNotice`), `SiteMenu.kt`, `SitesSnapshot.kt` (flat, parallel lists, `Enum.key()`), `SitesWiring.kt`; `iosMain/.../sites/IosSites.kt`; `androidMain/.../signin/AndroidSignIn.kt:37`.
- Tests: `tests/kt/core/commonTest/.../api/ColdframeApiTest.kt` (MockEngine), `sites/SitesEngineTest.kt` (`FakeSitesApi`), `SitesSnapshotTest`, `SiteMenuTest`; `jvmTest/.../api/OpenApiContractTest.kt` (`operation`, `assertMirrors`, problem-type enum).

**Android** (`apps/kt/android/src/main/kotlin/com/escendit/coldframe/android`)
- `ColdframeRoot.kt`, `ui/shell/AppShell.kt` (Tab enum; `appearanceOpen` sub-screen pattern at 164–179; Garden gets `onOpenSiteSettings`), `ui/settings/SettingsScreen.kt` (rows; `AlertDialog` confirm at 47–89), `ui/sites/{GardenScreen (StepTile 181–228), SiteMenu, SitesActions}.kt`, `ui/components/{Buttons,TextInput,InlineNotice,DashedBorder}.kt` (no dotted border yet), `ui/theme/ColdframeIcons.kt` (add `add = of(CarbonIcon.ADD)`), `ColorTokens.statusNoNodeBorder/Ink` (map in `ColdframeTheme` if absent), `res/values/strings.xml`.
- Tests: `tests/kt/android/.../{SitesScreensTest (350–372 assert menu → Settings index; change), SnapshotTest + snapshots/, CoverageTest, SourceScanTest, StringsTest, AccessibilityTest, ShellTest, TestSupport}.kt`.

**iOS** (`apps/swift/ios`)
- `Sources/ColdframeIOS/SitesPresentation.swift` (`SiteMenuItem.opensTab:175`, `GardenPresentation`, `SitesPresentation.init:284`, `SitesService:362`), `Presentations.swift` (`SettingsRow:79`, `ConfirmationPresentation`), `L10n.swift`, `Resources/Localizable.xcstrings`, `UI/Screens.swift` (`AppTabView`, `SettingsView:119`), `UI/SitesViews.swift` (`SitesActions`, `GardenView`, `FirstRunTile` dash at 314, `SiteMenu:382`), `UI/Components.swift`; `App/CoreSitesService.swift`.
- Tests: `tests/swift/ios/ColdframeIOSTests/{SitesPresentationTests (snapshot helper 6–27), ShellPresentationTests:41, SourceRulesTests, CatalogueTests, RenderTests}.swift`.

## Tasks & Acceptance

**Execution:**

*Contract*
- `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md` -- add `renameSite` (`PATCH /sites/{siteId}`, Owner, body `RenameSiteRequest {name}`, 200 `Site`, 503); `listLots` (`GET /sites/{siteId}/lots`, Member, `LotList {lots}` "In the Server's order"); `createLot` (`POST …/lots`, Administrator, `IdempotencyKey`, `CreateLotRequest {name}`, 201 `Lot` + Location, 422); `getLot` (`GET …/lots/{lotId}`, Member, 200 `Lot`, 404); `renameLot` (`PATCH …/lots/{lotId}`, Administrator, `RenameLotRequest`, 200 `Lot`, 404); `removeLot` (`DELETE …/lots/{lotId}`, Administrator, 204, 404, 409). Schemas `Lot {id uuid, name, status: LotStatus, removed?: boolean}`, `LotStatus` (six AD-14 values), problem types `lot-not-found`, `lot-claimed`, responses `LotNotFound`, `LotClaimed`, parameter `LotId` -- contract-first (AD-10); ASP.NET routes must match the templates.

*Server*
- `packages/cs/contracts/Lots/{LotEvents,LotGrains,LotLifecycle}.cs`, `packages/cs/contracts/Sites/{SiteGrains,SiteEvents}.cs` -- Lot events (aliases `lot.created|renamed|claimed|released|removed`); `ILotGrain` (`Create(siteId, name)` idempotent for the same Site, `Rename(siteId, name)`, `Remove(siteId)`, `Get(siteId)`) with outcome records (`NotFound`, `Claimed`, `Removed`, `AlreadyRemoved`, `Unchanged`…); `ISiteGrain.Rename(name)` and `ISiteGrain.CreateLot(callerId, key, name)`; Site events `LotCreationRequested(Key, LotId, Name, RequestedAt)` / `LotCreationCompleted(Key)` (aliases `site.lot-creation-requested|completed`) -- AD-2/18/20.
- `apps/cs/server/Lots/{LotGrain,LotState,LotsProjector,LotsReadModel,LotsHostingExtensions}.cs` -- `[GrainType("lot")]` grain: `Uncreated`/`Active`/`Removed`; wrong Site → `NotFound`; `Remove` refuses while `ClaimedBy` is set; after events, `CatchUpAsync` on the lots runner. Projector upserts `lots` (tombstone sets `removed_at`); read model `ListLotsAsync(siteId)` (live, ordered as Always) and `FindLotAsync(siteId, lotId)` (includes removed). Register grain, projector and read model at startup and in `IdentityCluster`.
- `apps/cs/server/Identity/{SiteGrain,SiteState,IPhaseTwoOrganizations,PhaseTwoOrganizations}.cs`, `tests/cs/server.integration/Identity/FakePhaseTwoOrganizations.cs` -- `Rename` (Active check, no-op on same name, Keycloak `displayName`, journal, identity catch-up); `CreateLot` (Active check, `FindLive`, `Guid.CreateVersion7(Clock.GetUtcNow())`, confirm Requested, call `ILotGrain.Create`, confirm Completed); `UpdateDisplayNameAsync`.
- `apps/cs/migrations/Migrations/M20260928140000CreateTableLots.cs` -- `lots(lot_id text pk, site_id text, name text, status text, claimed_by text null, created_at timestamptz, removed_at timestamptz null)`, `ix_lots_site_id`.
- `apps/cs/server/Edge/{EdgeApi,EdgeProblems,EdgeValidation}.cs` -- map the six operations with their `RequireSiteRole`; `NormalizeLotName` (same rule); `LotResponse`, `LotListResponse`; outcome → 200/201/204/400/404 `lot-not-found`/409 `lot-claimed`/422/503; canonicalise `lotId` (Guid parse, else 404).
- `tests/cs/server.integration/Edge/AuthorizationMatrixTests.cs` -- samples for all six: `renameSite` sends Site A's current name (no Keycloak call, 200); `createLot` a fresh key each call (201); `renameLot`/`removeLot`/`getLot` seed a fresh `lot/{uuidv7}` stream with `LotCreated(siteA, …)` via `edge.AppendAsync` and wait for the lots checkpoint; `listLots` 200 -- AD-4 matrix.
- `tests/cs/server.integration/Edge/EdgeApiFixture.cs` -- `WaitForProjectionCheckpointAsync(name, position)` (generalise the identity wait).
- New `tests/cs/server.integration/Edge/{RenameSiteTests,LotsTests}.cs` -- every server row of the matrix: real Site via `POST /sites` then rename (Keycloak `displayName` checked via `CreateOrganizationsClientAsync`, `GET /sites` shows it), invalid names; create two Lots → order and `noNode`; idempotent retry; key reuse 422; rename; remove → 204, journal alias `lot.removed`, tombstone via `GET …/lots/{id}`, second DELETE no new event; fixture claim (append `LotCreated` + `LotClaimed` before first activation) → 409 and unchanged journal version and list; other Site's Lot → 404.
- `tests/cs/server.integration/Identity/*` -- TestCluster tests for `SiteGrain.Rename` with the fake (Keycloak down → no event; same name → no call) and `CreateLot` pending-entry retry.
- `tests/cs/server.tests/Journal/FixtureJournalReplayTests.cs`, `Fixtures/journal.json` -- `["lot"] = () => new LotState()`; append one row per new alias.

*TypeScript client + web*
- `packages/ts/api-client/src/{schema,index}.ts` -- regenerate; export `Lot`, `LotList`, `LotStatus`, request types.
- `apps/ts/web/src/lib/server/{sites,lots}.ts` -- `renameSite`, `listLots`, `createLot`, `renameLot`, `removeLot`; errors add `forbidden` (403), `notFound` (404), `lotClaimed` (409).
- `apps/ts/web/src/lib/server/site-settings.ts`, `routes/(app)/settings/site/{+page.server.ts,+page.svelte}` -- load Lots of `currentSite`; named actions `renameSite`, `createLot` (hidden key, rules as Always), `renameLot`, `removeLot`; success invalidates so the layout's Site tabs show the new name. Owner sees the Site name field + "Rename Site"; others see the name as text. Admin+: "Lot name" field + "Create Lot"; each Lot row a name field + "Rename Lot" + ghost "Remove Lot" → `Modal` "Remove Lot {lot}?" / "Its history stays in Coldframe." / "Remove Lot". Member: read-only list + notice. Field errors under the field; 403 and 409 copy per the matrix.
- `apps/ts/web/src/routes/(app)/settings/+page.svelte`, `lib/site-menu.ts` -- a "Site settings" row first in the Settings index; the Site menu item goes to `/settings/site` (closes DW-25).
- `apps/ts/web/src/routes/(app)/garden/{+page.server.ts,+page.svelte}`, `lib/components/LotTiles.svelte` -- load Lots; the grid (1/2/3/4 columns at <400/<672/<1056/≥1056 px, `tile-gap`, `tile-padding-web`, tiles ≥ 1:0.82) below the 1.8 content; each tile one element with `aria-label` "{lot}, no Node, add a Node".
- `apps/ts/web/src/lib/i18n/en.json` -- every new string.
- `tests/ts/web/*` -- UX-DR-named load/action/render tests for each web row (Owner/Admin/Member gating, key reuse, 403/409/404 copy, order kept); update `coverage.test.ts`, `components.test.ts`.
- `tests/ts/web.e2e/fixtures/fake-idp.ts`, `specs/{helpers,garden,site-settings,accessibility}.spec.ts` -- the fake Server gains PATCH `/sites/{id}` and the Lot routes (in-memory, claimed flag seedable via `/control/sites`); new `site-settings.spec.ts` (rename Site → tab shows it; create Tomatoes and Beans → Garden tiles in order; rename; remove with confirm; claimed Lot shows the 409 copy; Member read-only) in light and dark at `zoom: 2`, axe clean, screenshots; update `garden` baselines; add `/settings/site` to `shellPages`.

*KMP core*
- `packages/kt/core/src/commonMain/.../api/{ColdframeApi,ApiResult,ApiDtos}.kt` -- `renameSite`, `listLots`, `createLot`, `renameLot`, `removeLot` (PATCH/DELETE, 204 → `Unit`); DTOs `LotDto`, `LotListDto`, `CreateLotRequestDto`, `RenameRequestDto`; `ApiFailure` adds `Forbidden`, `NotFound`, `LotClaimed`.
- `packages/kt/core/src/commonMain/.../sites/SitesEngine.kt`, `Sites.kt` -- `renameSite(name)` for the current Site (Owner), re-lists Sites on success so the switcher and header update.
- `packages/kt/core/src/commonMain/.../lots/{LotsEngine,Lots,LotsSnapshot}.kt` -- `LotsEngine` follows the current Site of `SitesEngine` (`Idle`/`Loading`/`Failed(notice)`/`Ready(siteId, lots, createForm, renaming, removing, notice)`), actions `load`, `setNewName`, `create`, `startRename`, `setRename`, `rename`, `cancelRename`, `askRemove`, `confirmRemove`, `cancelRemove`; reloads after each mutation; session and current-Site guards; `SiteSettings.of(role)` gating (`canRenameSite` Owner, `canEditLots` Admin+, `readOnlyNotice` Member).
- `packages/kt/core/src/iosMain/.../lots/IosLots.kt`, `IosSignIn.kt`, `androidMain/.../AndroidSignIn.kt`, `SitesWiring.kt` -- wire and expose; flat `LotsSnapshot` for Swift.
- `tests/kt/core/**` -- MockEngine tests per endpoint and failure; `LotsEngineTest` per matrix row (key reuse/renewal, order kept, 403/409/404, Member gating, Site switch reloads); `SitesEngineTest` rename; `OpenApiContractTest` for the new operations, DTOs and problem types.

*Android*
- `apps/kt/android/.../ui/settings/SiteSettingsScreen.kt`, `SettingsScreen.kt`, `ui/shell/AppShell.kt`, `ui/sites/{SiteMenu,GardenScreen,LotTile,SitesActions,LotsActions}.kt`, `ui/components/DottedBorder.kt`, `ui/theme/*`, `ColdframeRoot.kt`, `MainActivity.kt`, `res/values/strings.xml` -- Site settings sub-screen (like Appearance) from a "Site settings" row and the Site menu; rename Lot in an `AlertDialog` with `TextInput`; remove confirm `AlertDialog`; Garden Lot tiles (2 columns, 1 at font scale ≥ 1.5, merged semantics, content description as web).
- `tests/kt/android/**` -- UX-DR-named Robolectric tests; Roborazzi `site-settings-{light,dark}`, `site-settings-member-light`, re-recorded `garden-*` with Lots, at `AtFontScale(2f)` with `assertNothingOverflows`; update `SitesScreensTest` menu test and `CoverageTest`.

*iOS*
- `apps/swift/ios/Sources/ColdframeIOS/{LotsPresentation,SitesPresentation,Presentations,L10n}.swift`, `Resources/Localizable.xcstrings`, `UI/{SiteSettingsViews,SitesViews,Screens}.swift`, `App/{CoreLotsService,CoreSitesService,ColdframeApp}.swift` -- `LotsPresentation` from the snapshot, `LotsService` protocol, `SettingsRow.siteSettings`, `SiteMenuItem` opens Site settings, `SiteSettingsView` (rename alert with a field, destructive `confirmationDialog`), Lot tiles in `GardenView` (dotted `StrokeStyle`, one accessibility element).
- `tests/swift/ios/ColdframeIOSTests/{LotsPresentationTests,SitesPresentationTests,ShellPresentationTests,RenderTests}.swift` -- Linux-runnable UX-DR tests; render tests for Site settings and Garden with Lots at `.accessibility5` (macOS, operator).

*Docs and ledger*
- `docs/quickstart.md`, `packages/openapi/README.md`, `apps/ts/web/README.md`, `apps/swift/README.md`, `packages/kt/README.md` -- new endpoints, surfaces and test commands.
- `_bmad-output/implementation-artifacts/deferred-work.md` -- mark DW-25 resolved; add DW entries per Design Notes deferral.

**Acceptance Criteria:**
- Given I am the Owner of "Home", when I rename it in Site settings on any client, then the new name appears in Site settings, the Garden header and the Site switcher/tabs, and the Keycloak Organization `displayName` matches.
- Given the lots endpoints and `PATCH /sites/{siteId}`, when the generated authorization matrix runs, then each is covered for every Role on own and other Site, and `EveryEndpointHasASampleRequest` passes.
- Given Lots "Tomatoes" and "Beans" created by an Administrator, when Garden opens on web, Android or iOS, then both show as *no Node* tiles with `+` and "add a Node" in the Server's order, and each Lot has a `lot/{id}` stream starting with `lot.created`.
- Given `LotRemoved` recorded, when Garden and Site settings reload, then the Lot is gone and `GET /sites/{siteId}/lots/{id}` still returns it with `removed: true`.
- Given the coverage tests, when they run, then UX-DR18, 20, 74 and 84 each name at least one web, one Android and one Swift test.
- Given `dotnet build -warnaserror`, `dotnet format --verify-no-changes`, `dotnet test`, `pnpm -r lint typecheck test`, `./gradlew check` plus the iOS klib compile, and the Swift container build/test/lint, when they run on this machine, then they all pass.

## Spec Change Log

## Review Triage Log

### 2026-09-28 — Review pass
- verdicts: 29 findings — high 0, medium 0, low 22, false 6, maybe-false 1
- findings:
  - `[low]` `[reject]` (blind) A missing Keycloak Organization makes `UpdateDisplayNameAsync` throw `InvalidOperationException`, so `PATCH /sites/{id}` answers 500. — Every real Site has an Organization (the User grain creates it before the Site exists), and an Organization deleted in Keycloak reaches the Site grain through reconciliation. Handling it adds an outcome and a mapping for a case users should not meet.
  - `[low]` `[reject]` (blind) A client disconnect during the Keycloak call is logged as a Keycloak outage and answered 503; if the PUT landed, nothing is journaled. — The client is gone, so nobody reads the 503. The Keycloak admin event for the update routes to reconciliation, which journals `SiteRenamed`, so the name converges. Rare, and the fix adds a branch.
  - `[low]` `[reject]` (blind) `SiteState._lotCreations` never drops expired entries. — Growth is one small entry per Lot ever created on a Site, which a garden keeps to tens. The same pattern exists in `UserState` for Sites. Pruning adds logic for no user-visible gain.
  - `[low]` `[reject]` (blind) Web `statusError` and KMP `failureOf` treat every 409 as Lot claimed and every 404 as not found, whatever the problem type. — The only 409 in the contract is `lot-claimed`. A `site-not-found` 404 on a Lot route needs the Site deleted mid-session, a rare case where the copy is still close. Checking problem types adds branches.
  - `[low]` `[reject]` (blind) KMP maps every `NotFound` to the Lot-not-found notice, even for `renameSite` and `createLot` when the Site is gone. — Needs the Site deleted while its settings are open. The fix adds a notice kind and a string on both platforms.
  - `[low]` `[patch]` (blind) KMP shows "The Site was not renamed" for a 503 on create, rename or remove Lot. — `failed()` now takes `renamingSite`; Lot actions map a 503 to the Unexpected notice, as the web does. `LotsEngineTest` covers a 503 on `createLot`, including key reuse.
  - `[low]` `[reject]` (blind) KMP turns any server 400 into Blank or TooLong by guessing from the length. — The client check mirrors the Server rule, so a 400 on a name the client accepted is not reachable in practice. Same reasoning as the Story 1.8 row.
  - `[low]` `[reject]` (blind) On Android and iOS, a failed Lot load hides the Site rename form. — A failed load means the Server is unreachable, so a rename could not succeed either; the notice carries Try again. Splitting the states adds a branch.
  - `[low]` `[reject]` (blind) On the web, a successful rename or remove after a failed Create Lot drops the retry key. — The user started other work in between, so the next create is effectively a new attempt, and a duplicate only happens if the first attempt landed unseen. Rare.
  - `[low]` `[reject]` (blind) A same-key create retry after the Lot was removed answers 201 with `removed: true`. — Needs a retry of an old create after a removal within 24 h; clients retry only straight after a failure. The fix adds an outcome.
  - `[low]` `[reject]` (blind) The OpenAPI 404 for Lot routes documents only `LotNotFound`, though the policy can answer `site-not-found`. — Both use the same `ProblemDetails` schema, whose `type` enum lists both, so generated clients are unaffected. Documentation only.
  - `[false]` `[reject]` (blind) The spec does not match the implementation: `deferred: []` despite DW-31 to DW-36, `Describe` instead of `Get`, status `in-review`. — `deferred` lists review deferrals, not the planned ledger entries. `in-review` is the correct status at review time. `Get` is renamed `Describe` because code analysis (warnings as errors) rejects `Get`; the implementation report records it.
  - `[low]` `[reject]` (edge) A budget timeout after Keycloak applied the PUT leaves Keycloak renamed, the journal not, and the client told "not renamed". — Reconciliation of the admin event journals `SiteRenamed`, so the name converges. Rare, and the guard adds a re-read.
  - `[low]` `[reject]` (edge) A caller abort is reported as a Keycloak outage. — Same root and reason as the blind client-disconnect row.
  - `[low]` `[reject]` (edge) A 404 or other 4xx from Phase Two becomes a 500. — Same root and reason as the blind missing-Organization row.
  - `[low]` `[reject]` (edge) A concurrent Keycloak Organization edit between the GET and the PUT can be overwritten. — Already ledgered as DW-35. The Owner rarely edits the Organization in Keycloak at the same moment, and Phase Two offers no partial update.
  - `[low]` `[reject]` (edge) A same-key create retry after removal returns the removed Lot as created. — Same root and reason as the blind row.
  - `[low]` `[reject]` (edge) `_lotCreations` grows without bound. — Same root and reason as the blind row.
  - `[low]` `[patch]` (edge) KMP shows Site-rename copy for a 503 on a Lot action. — Same root as the blind row; patched there.
  - `[low]` `[reject]` (edge) KMP reports a missing Site as a missing Lot. — Same root and reason as the blind row.
  - `[low]` `[reject]` (edge) On mobile, editing the name after an Unreachable create and resubmitting gives 422, then a second Lot. — Every step is still correct, and the reloaded list shows both Lots. The spec keeps one key per attempt. Same as the Story 1.8 row.
  - `[low]` `[reject]` (edge) The web has the same edited-name retry path. — Same root and reason as the mobile row.
  - `[false]` `[reject]` (edge) After a 404 on rename or remove Lot, the reload drops the row, so its notice never shows. — A failed action does not invalidate: the page's `update({reset: false})` only reloads on success, so the row and its notice stay.
  - `[false]` `[reject]` (edge, claim) The spec names `ILotGrain.Get`, but the code has `Describe`. — No caller uses `Get`. The rename is forced by the analyzer and recorded in the implementation report. The fix would edit this build's spec.
  - `[low]` `[patch]` (verification-gap) `PATCH /sites/{id}` maps `IdentityProviderUnavailable` to 503 inline, with no HTTP-level test. — Extracted to `EdgeApi.ToHttpResult(SiteRenameOutcome, SiteResponse?)`. The new `SiteRenameResponseTests` asserts 200, 503 `identity-provider-unavailable` and 404 per outcome.
  - `[low]` `[patch]` (verification-gap) The lots projector's `LotReleased` path never runs in a test. — Added `LotsTests.OnlyTheClaimingNodesReleaseFreesTheLot`. A release by another Node keeps `unknown`; a release by the claiming Node gives `noNode`.
  - `[low]` `[reject]` (verification-gap, other) A same-key retry after removal returns a removed Lot as created. — Same root and reason as the blind row.
  - `[maybe-false]` `[reject]` (verification-gap, other) Mobile shows Lot copy for a missing Site. — Same root as the blind row. Settle it by deleting a Site in Keycloak while its Site settings are open on a device; even if true it is only low, so it is rejected.
  - `[false]` `[reject]` (intent) The spec has no `operator_actions`, no `awaiting-operator` status, no Auto Run Result, and the work is uncommitted. — Finalization writes those and commits; the diff was taken before that.

## Design Notes

- **Garden keeps the 1.8 empty state above the Lot grid.** EXPERIENCE.md shows the first-run tiles "while the Site has no Node with Readings" and keeps the headline "No Readings yet" for that case; the story requires Lot tiles on Garden. Both conditions hold, so both render: header and steps first, then the grid.
- **Lot creation goes through the Site grain.** The Lot ID must exist before the Lot grain does, and the spine says the owning grain stores the key for 24 h. The Site grain owns "which Lots may be created on me" (Active check) and already serialises Site calls, so it holds the key → Lot ID map, exactly like `UserGrain.CreateSite`. Lot rename and remove go straight to the Lot grain.
- **Status for claimed Lots.** Only `noNode` is specified for 1.9. A claimed Lot has a Node but no Readings, so the projection says `unknown`; clients render any non-`noNode` tile with the name only until the Epic 5 variants arrive. Ledger it.
- **The matrix sample for `renameSite`** reuses the current name, so it tests authorization without Keycloak (the matrix Sites have no Organization). The real rename runs in `RenameSiteTests` against the AppHost Keycloak.
- **Deferrals to ledger:** Lot tile tap → Lot detail / Add a Node (Epic 4); non-`noNode` tile variants (Epic 5); Site Reminder cadence in Site settings (UX-DR50); `Claim`/`Release` grain methods (Epic 4); refetch on `readmodel.changed` and DW-28 stay open.
- **Operator-owed work**, as in 1.5 and 1.8: Linux cannot compile SwiftUI, link `ColdframeCore`, run the macOS render tests, or drive devices. Those become `operator_actions` with `status: awaiting-operator`.

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror && dotnet format --verify-no-changes --no-restore` -- expected: success, no changes.
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- expected: all pass, including the matrix, `RenameSiteTests`, `LotsTests` and the journal replay.
- `pnpm install --frozen-lockfile && pnpm -r lint && pnpm -r typecheck && pnpm -r test` -- expected: exit 0, including the api-client staleness test and the Playwright specs with screenshots.
- `./gradlew check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64` -- expected: exit 0, including Roborazzi verification.
- `podman run --rm --security-opt label=disable -v "$PWD":/work -w /work swift:6.3.3 sh -c 'swift build && swift test && swift format lint --strict -r .'` -- expected: exit 0.

## Auto Run Result

Status: awaiting-operator

**Summary.** Owners can rename their Site, and Owners and Administrators can create, rename and remove Lots, on the Server, the web, Android and iOS. Garden shows the Site's Lots as no-Node tiles.
- **Server:**
  - `PATCH /sites/{siteId}` (Owner). The Site grain sets the Keycloak Organization `displayName` first, then journals `SiteRenamed`. Renaming to the same name does nothing. If Keycloak is down, it answers 503 and nothing changes.
  - A new event-sourced Lot grain, a `lots` projection and read model, and migration `M20260928140000`.
  - Endpoints: `GET`/`POST /sites/{siteId}/lots`, and `GET`/`PATCH`/`DELETE /sites/{siteId}/lots/{lotId}`.
  - Creating a Lot goes through the Site grain and is idempotent per caller and key for 24 h.
  - A Lot holding a Node refuses removal with 409 `lot-claimed` and records nothing.
  - Removal records `LotRemoved` and keeps a tombstone, so `GET` still returns the Lot with `removed: true`.
  - All six operations are in the authorization matrix.
- **Contract and TypeScript client:** the OpenAPI file gains the six operations, the Lot schemas, the six-value `LotStatus`, and the `lot-not-found` and `lot-claimed` problem types. The TypeScript client is regenerated from it.
- **Web:**
  - New page `/settings/site`. It is the first row of the Settings index, and the Site menu now opens it, which resolves DW-25.
  - Controls a Role cannot use are hidden. Removing a Lot asks for confirmation in a Modal. The 403, 409 and 503 messages match the spec.
  - Garden shows the Lot grid below the Story 1.8 content.
- **KMP core and Android:**
  - `ColdframeApi` gains the new calls, and `SitesEngine.renameSite` is added.
  - A new `LotsEngine` holds the Site settings state and the Lots, with a `LotsSnapshot` and `IosLots` bridge for Swift.
  - Android has the Site settings screen, Lot tiles with a dotted border, and new Roborazzi snapshots.
- **iOS:** `LotsPresentation`, `SiteSettingsView`, Lot tiles in `GardenView`, the `CoreLotsService` adapter and the string catalogue entries.

**Files changed** (listed by area)
- `packages/openapi/*` and `packages/ts/api-client/src/*`: the contract and the generated client.
- `packages/cs/contracts/{Lots/*,Sites/SiteEvents.cs,Sites/SiteGrains.cs}`: the Lot events, grain interface, lifecycle, and the Site rename and create-Lot contracts.
- `apps/cs/server/Lots/*`: the Lot grain, its state, the projector, the read model and registration.
- `apps/cs/server/Identity/{SiteGrain,SiteState,IPhaseTwoOrganizations,PhaseTwoOrganizations}.cs`: renaming a Site and creating a Lot.
- `apps/cs/server/Edge/*`: the endpoints, problem types and name validation.
- `apps/cs/migrations/Migrations/M20260928140000CreateTableLots.cs`: the `lots` table.
- `tests/cs/**`:
  - authorization matrix samples;
  - `LotsTests`, `RenameSiteTests`, `SiteLotsAndRenameTests`, `SiteRenameResponseTests`, `LotStateTests` and `SiteStateLotCreationTests`;
  - journal fixture rows;
  - fixture helpers.
- `apps/ts/web/src/**`:
  - `lib/server/{lots,site-settings,sites}.ts`, `lib/lots.ts`, `LotTiles.svelte`;
  - the `settings/site` route, the Garden load and page, the Settings index and the Site menu;
  - `en.json`.
- `tests/ts/web/*` and `tests/ts/web.e2e/*`: unit tests, the fake Server's Lot routes, `site-settings.spec.ts`, and new screenshots.
- `packages/kt/core/src/**`:
  - `api/*` and `lots/*`;
  - changes to `SitesEngine`, `SitesWiring` and the Android/iOS sign-in wiring.
- `tests/kt/core/**`: the API, `LotsEngine`, `LotsSnapshot`, `SitesEngine` and OpenAPI contract tests.
- `apps/kt/android/**`:
  - `SiteSettingsScreen`, `LotTile`, `LotsActions` and `DottedBorder`;
  - the Settings, AppShell, Garden and Site menu wiring;
  - `strings.xml`.
- `tests/kt/android/**`: `SiteSettingsScreenTest`, the snapshots, and `CoverageTest` ids.
- `apps/swift/ios/**`:
  - `LotsPresentation`, `SiteSettingsViews`, and the Sites, Screens and Components changes;
  - `L10n` and the xcstrings catalogue;
  - `CoreLotsService` and the app wiring.
- `tests/swift/ios/**`: `LotsPresentationTests`, render tests, and updates to the Shell and Sites tests.
- `docs/quickstart.md` and the READMEs (web, Swift, Kotlin, OpenAPI).
- `deferred-work.md`: DW-25 resolved; DW-31 to DW-36 added.

**Review findings.** 29 in total: high 0, medium 0, low 22, false 6, maybe-false 1.
- **Patched (3 entries, 4 rows, all low):**
  - KMP showed Site-rename copy for a 503 on a Lot action.
  - The HTTP mapping of the Site rename outcome is extracted and tested, including the 503.
  - The lots projector's release path is now tested.
- **Deferred:** none.
- **Rejected:** every other finding, with its reason in the Review Triage Log. Among them:
  - a missing Keycloak Organization gives 500;
  - a client abort or timeout during the Keycloak rename;
  - the idempotency map never shrinks;
  - 404 and 409 are read without checking the problem type;
  - mobile shows Lot copy for a missing Site;
  - a create retry after removal;
  - retrying with an edited name;
  - OpenAPI 404 documentation;
  - the GET/PUT race (already DW-35);
  - six false claims.

**Follow-up review recommended: false.** Patched entries: high 0, medium 0, low 3.

**Verification** (this machine, after the patches)
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror && dotnet format --verify-no-changes --no-restore`: 0 warnings, 0 errors, format clean.
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build`: 308 of 308 passed.
- `pnpm -r lint && pnpm -r typecheck && pnpm -r test`: exit 0. That is 599 design-token, 5 api-client and 307 web unit tests, and 35 Playwright tests with screenshots.
- `./gradlew check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64`: BUILD SUCCESSFUL, including the Roborazzi verification.
- `swift build && swift test && swift format lint --strict -r .` in `swift:6.3.3` under podman: 82 tests passed, lint clean.
- Matrix audit: each I/O matrix row is covered by a test that ran. The mobile 401 row got an extra `ColdframeApiTest` case for the Site settings calls.

**Residual risks**
- SwiftUI views and `CoreLotsService` have never been compiled, and nothing has run on a device (see `operator_actions`).
- The web e2e runs against a fake Server. The real ordering, idempotency and tombstone behaviour are tested against the AppHost.
- A Site rename can overwrite a concurrent Keycloak Organization edit (DW-35).
- Android row buttons do not name their Lot for TalkBack (DW-36).
- A 403 shows its notice but does not reload the Roles.

