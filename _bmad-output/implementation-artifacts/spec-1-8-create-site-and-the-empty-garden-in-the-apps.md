---
title: 'Story 1.8: Create Site and the empty Garden in the apps'
type: 'feature'
created: '2026-09-28'
status: 'awaiting-operator'
baseline_revision: 'd4a9f11ffdc58e796d4a5c589a2ebe429feb1165'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/EXPERIENCE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/DESIGN.md'
warnings: ['oversized']
deferred:
  - summary: >-
      On iOS, "New Site" in the switcher may never open Create Site, because the root view asks for the Create Site sheet while GardenView's switcher sheet is still closing.
    evidence: |-
      Unverified (maybe-false): SwiftUI often drops a sheet presentation requested during another sheet's dismissal. If it does, `creating` stays set and `newSite()` then ignores every tap, so Create Site cannot be reached until the app restarts.
      Settle it on a Mac or iPhone: open the switcher, tap New Site, and check that Create Site appears. If it does not, call actions.newSite() from the switcher sheet's onDismiss.
      SwiftUI cannot compile or run on Linux.
    location: >-
      apps/swift/ios/Sources/ColdframeIOS/UI/SitesViews.swift (GardenView onNewSite), apps/swift/ios/Sources/ColdframeIOS/UI/Screens.swift (ColdframeRootView .sheet)
    severity: medium (unverified)
operator_actions:
  - "On a Mac with Xcode 26.6, run `swift build && swift test && swift format lint --strict -r .` at the repository root and fix any compile or test failure in the never-compiled SwiftUI code (apps/swift/ios/Sources/ColdframeIOS/UI/SitesViews.swift, Screens.swift), including the new RenderTests for Create Site, Garden and the Site switcher at .accessibility5."
  - "On that Mac, run `xcodegen generate` and `xcodebuild build -scheme Coldframe -destination 'generic/platform=iOS Simulator' CODE_SIGNING_ALLOWED=NO` in apps/swift/ios, and confirm apps/swift/ios/App/CoreSitesService.swift compiles against the ColdframeCore framework (it calls `doNewSite()` on the assumption that Kotlin/Native renames `newSite`; adjust if the exported name differs)."
  - "Install the Android app against a real Server and Keycloak, sign in with a new user, and verify: Create Site (name, time-zone Confirm and Change) lands on the empty Garden; the switcher lists Sites with Role and New Site; the Site menu holds Site settings; Server off shows the Unreachable notice; at font scale 2 with TalkBack nothing clips."
  - "Run the iOS app against the same Server on an iPhone and verify the same Create Site, Garden, switcher and menu behaviour at the largest Dynamic Type size with VoiceOver, including tapping New Site in the Site switcher and checking that Create Site opens (deferred item in this spec)."
---

<intent-contract>

## Intent

**Problem:** After sign-in, no app can create a Site or show one. The web Garden page and the mobile Garden tabs show only a heading. Nothing calls the Story 1.6 `POST /sites`. No Server read tells a client which Sites the user belongs to, so no client can detect "no Membership" or list Sites in a switcher. `@coldframe/api-client` is an empty placeholder (epics.md 719–742, UX-DR21/22/23/54/61/62/82).

**Approach:**
- **Server:** add one read, `GET /sites` (Authenticated), which lists the caller's Active Sites with their Role from the identity projection.
- **Clients:** generate the TypeScript client from the OpenAPI file. Give the KMP core a Server API client and a `SitesEngine` that owns the Sites state.
- **Surfaces** on web (SvelteKit BFF), Android (Compose) and iOS (SwiftUI):
  - Create Site: a name field, the time-zone confirm panel, and an `Idempotency-Key`.
  - The empty Garden: the Site summary header, "No Readings yet", and the four first-run step tiles.
  - The Site switcher, with Role and "New Site".
  - The Site menu.

## Boundaries & Constraints

**Always:**
- Test-first (AD-24). Each acceptance criterion and each UX-DR in this story (21, 22, 23, 54, 61, 62, 82) first gets a failing test, named `UX-DRnn …` where the coverage lists expect it. Add the ids to `tests/ts/web/coverage.test.ts` `storyIds`, and to `tests/kt/android/.../CoverageTest.kt` `storyIds` and `iosIds`.
- Clients render only (AD-14):
  - Sites appear in the Server's order and are never re-sorted.
  - The Role comes from the Server.
  - The browser never calls the Server; the SvelteKit server does, with the session's `accessTokenRaw`.
  - Swift and Compose never see a token or a URL. The KMP core alone calls the API.
- **Idempotency-Key:** one key per Create Site attempt. The client makes the key when the form opens and reuses it for every retry of the same submission, including after a 503. It makes a new key only after a 422 `idempotency-key-reused` or after success.
- **Time zone** (AD-11: it is the User's, not the Site's):
  - The panel proposes the OS or browser zone: "Is your time zone {zone}?" with Confirm and Change. Change opens a searchable list of IANA zone IDs.
  - A zone the user confirmed or picked is stored per device and is never overwritten by detection.
  - It is **not** sent to the Server. The request body stays `{name}`.
- Every string is externalised:
  - **web:** `en.json`.
  - **Android and iOS:** `strings.xml` and `Localizable.xcstrings`, with identical keys and English values. The `L10n` enum gets a case for each new key.
- Copy is calm and literal, uses glossary capitals, and has no exclamation marks. Buttons name the result: **"Create Site"**, with the working label **"Creating Site…"** (no spinner).
- Controls a Role cannot use are hidden, not disabled. Square corners, 1 px borders, no shadows, Carbon icons from the tokens, and no animations (UX-DR101).
- Every screen has a light and a dark theme and holds up at the largest text size.

**Never:**
- No Lot endpoints or Lot UI, no Site settings surface, rename, Pause sheet or pause endpoint, Hub/Node flows, notification permission prompt, or User time-zone endpoint or event. Those belong to Story 1.9 and later epics.
- No client-side status computation, no polling loop, and no change to `POST /sites` or `GET /sites/{siteId}`.
- No hand-written TypeScript API types. The TypeScript types come only from `openapi-typescript`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| List my Sites | Caller holds Owner on A and Member on B; B's sibling C belongs to someone else; D is `Deleted` | `GET /sites` → 200 `{sites:[A Owner, B Member]}` in creation order (created_at, then id). C and D are absent | 401 without a token |
| No Membership | Signed in; `GET /sites` returns `[]` | web: every app route redirects (303) to `/sites/new`. Mobile: the Create Site surface replaces the tab shell | — |
| Create Site | name "Home", zone confirmed | `POST /sites` with `Idempotency-Key` and `{name}` → 201. The new Site becomes current (web cookie `cf_site`; mobile per-device setting). The app lands on Garden | — |
| Invalid name | blank, or longer than 100 characters after trim | The field is invalid and a one-line reason sits under it. On a client check, no request is sent. A 400 `validation` maps to the same field error | — |
| Identity provider down | 503 | Inline notice: the Site was not created, try again. Try again reuses the **same** key | Key kept |
| Key reused | 422 | Inline notice: try again. A new key is made | — |
| Server unreachable / certificate | network or TLS failure | The existing Unreachable or Certificate notice. The Certificate notice is never retried insecurely | — |
| Session expired | 401 on any call | web: the existing sign-in redirect. Mobile: the engine signs out with the SignedOut notice | — |
| Switch Site | Pick B in the switcher | The whole app shows B. The choice persists per device. An unknown or stale current id falls back to the first listed Site | — |
| New Site | "New Site" in the switcher | Create Site opens. On mobile, Cancel returns to the Garden; the web route has a back link | — |
| Empty Garden, Owner or Admin | Any Site in 1.8 | Header: Site name, headline "No Readings yet" (exposed as a heading), subline "Nothing is measuring, so there's no status to show.", Site menu trigger. Tiles STEP 1–4: "Add a Hub" is next (solid `primary`, `ink-on-bright`); the others are later (1 px dashed `border-strong`) | — |
| Empty Garden, web or Member | web: any Role. Mobile: Member | Tiles without actions, plus an Inline notice. web: "Adding a Hub or Node needs the Coldframe mobile app." Member: "Only Owners and Administrators can add Devices." | — |
| Site menu | Trigger (mobile: header right; web: on the current Site tab) | The item "Site settings" opens the Settings index. Pause/Resume is not rendered in 1.8 (see Design Notes) | — |

</intent-contract>

## Code Map

- `packages/openapi/coldframe.openapi.json`: add `GET /sites` (`listSites`, `x-coldframe-minimum-role` the same as `createSite`) returning `SiteList {sites: Site[]}`. Copy the file to `tests/cs/server.tests/Fixtures/coldframe.openapi.json` (read by `EdgeEndpointDiscoveryTests`).
- `apps/cs/server/Edge/EdgeApi.cs:46-52`: map endpoints here; follow `RequireAuthenticatedCaller()` (`EdgeAccessRule.cs:11-26,47-58`) and the `SiteResponse` mapping.
- `apps/cs/server/Identity/IdentityReadModel.cs:141-176`: `FindSiteAsync` shows the query style. Add `ListSitesAsync(userId)`: `identity_memberships` JOIN `identity_sites`, `lifecycle='Active'`, `ORDER BY created_at, site_id`. The index `ix_identity_memberships_user_id` already exists.
- `tests/cs/server.integration/Edge/AuthorizationMatrixTests.cs:26-40`: `Samples` needs a `"GET /sites"` entry (`EveryEndpointHasASampleRequest` fails until it has one). `EdgeApiFixture` provides users and tokens.
- `packages/ts/api-client/`: a placeholder (`src/index.ts`). `packages/openapi/README.md` says clients are generated in Story 1.8. `tests/ts/api-client/index.test.ts`.
- **web** (`apps/ts/web/src/`):
  - Routes and auth:
    - `routes/(app)/+layout.server.ts:8`: `guardShell`, returns `{user}`.
    - `routes/(app)/garden/+page.svelte`: heading only.
    - `hooks.server.ts`: config and session.
    - `lib/server/config.ts:8`: `serverUrl`.
    - `lib/server/probe.ts` and `failures.ts`: an injectable `Fetch` and `classifyFailure`.
    - `src/app.d.ts:7-15`: the session identity. `accessTokenRaw` is set by `@escendit/sveltekit-auth-keycloak` and refreshed there.
  - Components (`lib/components/`):
    - Shell: `AppShell`, `AppHeader` (no Site tabs yet), `SideNav`.
    - Controls: `Button` (working label), `TextInput`, `InlineNotice`, `Modal`, `RoleGate`, `Icon` (register `overflow-menu--vertical`, `chevron--down` and `checkmark` from `@coldframe/design-tokens/icons`).
  - i18n: `lib/i18n/en.json` + `t()` (type-checked keys), `glossary.json`. Also `lib/roles.ts` (`hasRole`).
- **web tests:**
  - `tests/ts/web/components.test.ts:174` asserts the shell has no "New Site" and no `tablist`. Replace this with the UX-DR23 assertions.
  - Guards: `no-hard-coded-copy.test.ts`, `copy.test.ts`, `styles.test.ts`.
  - e2e:
    - `tests/ts/web.e2e/fixtures/fake-idp.ts` also stands in for the Server (healthz) and takes `POST /control/mode`.
    - `specs/helpers.ts:28` `signIn()` expects `/garden`; `:48` `shellPages`.
    - `accessibility.spec.ts` loops light/dark with axe; `theme.spec.ts`.
- **core** (`packages/kt/core/`):
  - Sign-in:
    - `signin/SignInEngine.kt:25` (the state and mutex; refresh only in `runResume` 88–138; expiry check at 198).
    - `TokenVault`, `IdentityProvider.refresh`, `SignInClients.kt` (Ktor 3.6.0; `oidcHttpClient` shows ContentNegotiation and the 30 s timeout).
    - `CoreConfig.serverUrl`.
    - `CertificateErrors` classifiers, `Failure` / `Notice`.
  - Platform and Swift bridge: `appearance/AppearanceStore` (the per-device Settings pattern); `Watcher.kt` + `iosMain/.../IosSignIn.kt` + `SignInSnapshot.kt` (the Swift bridge pattern); `androidMain/.../AndroidSignIn.kt`.
  - Build: `build.gradle.kts` (no serialization plugin yet; `explicitApi()`). `gradle/libs.versions.toml` has no kotlinx-serialization plugin entry.
- **Android** (`apps/kt/android/src/main/`):
  - `kotlin/.../ColdframeRoot.kt:20` (state → surface), `ui/shell/AppShell.kt` (the `Tab` enum at 45; Garden content box at 158–165), `ColdframeApplication.kt`, `MainActivity.kt`.
  - `ui/components/` (`Buttons.kt`, `InlineNotice`, `TextInput`, `SegmentedChoice`), `ui/theme/` (`Coldframe.colors`, `TypeRole`), `ColdframeIcons.of(CarbonIcon.X)`.
  - `res/values/strings.xml`.
- **Android tests** (`tests/kt/android/test/kotlin/...`):
  - `AccessibilityTest` (`AtFontScale`, `assertNothingOverflows`), `ThemeTest` (pixel sampling).
  - `CoverageTest.kt:84,118`.
  - `SourceScanTest` (no animation, Toast or progress indicator; no literal strings; no token names in the shell).
  - `StringsTest.kt:78` (Android/iOS parity, glossary, plurals).
- **iOS:**
  - `apps/swift/ios/Sources/ColdframeIOS/`:
    - `L10n.swift`, `Resources/Localizable.xcstrings`.
    - `Presentations.swift` (`AppTab`, `SettingsRow`).
    - `SignInPresentation.swift` (the `SignInService` protocol pattern).
    - `UI/Screens.swift:57` `AppTabView`: Garden is an empty background. The `SourceRulesTests` rule forbids `TextField` in `Screens.swift` before `AppTabView`, so put new views in new UI files.
    - `UI/Components.swift` (`TextInputField`, `PrimaryButton`, `InlineNotice`).
  - `apps/swift/ios/App/CoreSignInService.swift` is the only `ColdframeCore` importer; `ColdframeApp.swift`.
  - Tests: `tests/swift/ios/ColdframeIOSTests/` (Linux: presentation, catalogue and source rules; macOS-only: `RenderTests.swift` at `.accessibility5`).

## Tasks & Acceptance

**Execution:**

*Server*
- `packages/openapi/coldframe.openapi.json` + fixture copy + `packages/openapi/README.md`: add `listSites` and `SiteList`.
- `apps/cs/server/Identity/IdentityReadModel.cs`, `apps/cs/server/Edge/EdgeApi.cs`: add `ListSitesAsync` and `GET /sites` → `SiteListResponse(IReadOnlyList<SiteResponse> Sites)`, with `RequireAuthenticatedCaller()`.
- Tests:
  - `tests/cs/server.integration/Edge/AuthorizationMatrixTests.cs`: a `GET /sites` sample.
  - New `tests/cs/server.integration/Edge/ListSitesTests.cs`: the matrix row "List my Sites" (order, other user's Site absent, Deleted absent, empty list), against the AppHost like the Story 1.6 Edge tests.

*TypeScript client*
- `packages/ts/api-client/`:
  - Add `openapi-typescript` (dev dependency) and `openapi-fetch`, pinned exact.
  - A `generate` script writes `src/schema.ts` from `../../openapi/coldframe.openapi.json`.
  - `src/index.ts` exports `createColdframeClient({baseUrl, accessToken, fetch?})` plus the schema types.
  - `tests/ts/api-client/`:
    - A test that regenerates into memory and fails if `src/schema.ts` is stale.
    - A test that the client sends `Authorization: Bearer` and `Idempotency-Key`.

*web*
- `apps/ts/web/package.json`: depend on `@coldframe/api-client` (`workspace:*`).
- `apps/ts/web/src/lib/server/sites.ts`: server-only.
  - `listSites(locals)` and `createSite(locals, name, key)` through the client, with the session's `accessTokenRaw`.
  - Map results to `{ok}` / `{error: 'validation' | 'unavailable' | 'keyReused' | 'unreachable' | 'certificate' | 'unauthorized'}`, reusing `classifyFailure`.
- `apps/ts/web/src/routes/(app)/+layout.server.ts`:
  - Load `sites`.
  - An empty list redirects (303) to `/sites/new`, except on that route itself.
  - Resolve `currentSite` from the `?site=` query (set the `cf_site` cookie and redirect without the query) or from the cookie, falling back to the first Site.
- `apps/ts/web/src/routes/(app)/sites/new/+page.server.ts` + `+page.svelte`: the Create Site form.
  - Fields:
    - the name;
    - a hidden `idempotencyKey` (from `crypto.randomUUID()` in `load`, echoed back on failure, replaced after a 422);
    - `timeZone`: filled on the client from `Intl.DateTimeFormat().resolvedOptions().timeZone` unless the `cf_time_zone` cookie holds a choice.
  - The action validates, calls `createSite`, sets `cf_site` and the `cf_time_zone` cookie on success, and redirects (303) to `/garden`.
- New components in `apps/ts/web/src/lib/components/`:
  - `TimeZonePanel.svelte`: the dashed `support-warning` panel with Confirm and Change. Change shows a filter `TextInput` over `Intl.supportedValuesOf('timeZone')`.
  - `SiteTabs.svelte`: inside `AppHeader`. `role="tablist"`, one tab per Site in Server order with name and Role, the current tab underlined in `primary` and `aria-selected`, then a last tab "New Site".
  - `SiteMenu.svelte`: an overflow trigger on the current tab, a DS menu holding "Site settings" → `/settings`.
  - `SiteSummaryHeader.svelte`.
  - `FirstRunSteps.svelte`: 2×2 tiles. It takes `actionable: boolean`, and the web always passes `false`.
- `apps/ts/web/src/routes/(app)/garden/+page.svelte`: the header, the steps and the web Inline notice.
- `apps/ts/web/src/lib/i18n/en.json`: every new string.
- web tests:
  - `tests/ts/web/*`: UX-DR-named SSR component and load/action tests for every matrix row that concerns web, including key reuse after a 503 and a new key after a 422.
  - Update `components.test.ts:174` and the `coverage.test.ts` ids.
- e2e:
  - `tests/ts/web.e2e/fixtures/fake-idp.ts`: an in-memory Server fake.
    - `GET /sites` and `POST /sites`: it records the `Idempotency-Key`, returns 201, and answers 400 for a blank name.
    - `POST /control/sites` to reset or seed Sites.
    - It seeds one Site by default, so existing specs still reach `/garden`.
  - `specs/helpers.ts`: add `resetSites()`.
  - New `specs/garden.spec.ts`, for light and dark, each at a 1280×800 viewport with `html { zoom: 2 }` (largest text):
    - sign in with no Sites → Create Site;
    - type "Home" and Confirm the zone → Create Site;
    - assert the fake got one POST with a key, the Garden header "Home", "No Readings yet", four tiles, the web notice, and the tabs "Home · Owner" and "New Site";
    - axe clean;
    - `toHaveScreenshot` baselines for Create Site and Garden (committed, Linux Chromium).
  - Add `/sites/new` to the a11y page loop.

*KMP core*
- `gradle/libs.versions.toml`, `packages/kt/core/build.gradle.kts`: add the `org.jetbrains.kotlin.plugin.serialization` plugin at the Kotlin version, and the kotlinx-serialization-json runtime.
- `packages/kt/core/src/commonMain/.../api/`:
  - `ColdframeApi` (Ktor, JSON, 30 s timeout): `listSites()` and `createSite(name, idempotencyKey)`.
  - DTOs match the OpenAPI names (`SiteDto`, `SiteListDto`, `CreateSiteRequestDto`, `ProblemDto`).
  - Results are values: `ApiResult.Ok` or `ApiResult.Failed(ApiFailure)`, with `ApiFailure ∈ Validation | IdentityProviderUnavailable | KeyReused | Unauthorized | Unreachable | Certificate | Unexpected`.
  - Bearer tokens come from `SignInEngine.accessToken()`: a new internal method under the mutex. It returns the vault token, refreshes it if expired, and returns null when signed out.
  - A 401 calls `signOutExpired()`, which sets `SignedOut(Notice.SignedOut)`.
- `packages/kt/core/src/commonMain/.../sites/`:
  - `SitesEngine` exposes one `StateFlow<SitesState>`:
    - `Idle` (signed out);
    - `Loading`;
    - `Failed(Notice)` (TryAgain);
    - `NeedsSite(form)`;
    - `Ready(sites, current, creating: CreateSiteForm?)`.
  - `CreateSiteForm(name, nameError, working, notice, idempotencyKey, timeZone: TimeZoneProposal(detected, chosen, changing), cancellable)`.
  - Actions: `load()`, `select(id)`, `newSite()`, `cancelNewSite()`, `setName()`, `confirmTimeZone()`, `changeTimeZone()`, `pickTimeZone(id)`, `submit()`.
  - `SiteSummary(id, name, role)`.
  - `FirstRunSteps.of(role, flowAvailable = false)`: four steps (`AddHub` next, the rest later), with `actionable = flowAvailable && role >= Administrator` and `memberNotice = role == Member`.
  - `SiteMenu.items(role, pauseAvailable = false, stale = false)`: "Site settings" always. Pause/Resume only when `pauseAvailable` and Admin+. All items are disabled with "Needs your Server" when `stale`.
  - `DeviceChoices` (Settings-backed): the current Site id and the chosen time zone.
  - `expect fun detectedTimeZone(): String` and `expect fun availableTimeZones(): List<String>`. On Android and the JVM these come from `java.util.TimeZone` / `java.time.ZoneId`; on iOS from `NSTimeZone`.
  - Key generation uses `kotlin.uuid.Uuid.random()`.
- Wiring:
  - `androidMain/.../AndroidSignIn.kt`: expose `sites: SitesEngine`.
  - `iosMain/.../IosSites.kt`: `watch { SitesSnapshot }` (flat: surface kind, sites as parallel lists of id, name and role key, current id, form fields, notice key, the step list, the menu item keys) plus the actions.
  - `SitesEngine` loads when the sign-in state becomes `SignedIn`.
- `tests/kt/core/`: Ktor `MockEngine` tests for the API (headers, body, every failure mapping, 401 sign-out), plus `SitesEngine` tests for every matrix row: key reuse and renewal, stale current id, persisted choices, detection that never overwrites a choice, validation without a request, Member notice, and menu gating including `pauseAvailable` and `stale`.

*Android*
- `tests/kt/android` build: add Roborazzi (latest 1.x, pinned in the catalog) for Robolectric snapshot tests. Baselines go under `tests/kt/android/snapshots/`, and `check` verifies them.
- `apps/kt/android/.../ColdframeRoot.kt`: `SignedIn` + `SitesState` → `Loading` (background), `Failed` (notice + Try again), `NeedsSite` (`CreateSiteScreen`), `Ready` (`AppShell`, with `CreateSiteScreen` over it while `creating`).
- New UI files in `apps/kt/android/.../ui/sites/`:
  - `CreateSiteScreen.kt`: `TextInput`, the time-zone panel, and Change → a filter field plus a list.
  - `GardenScreen.kt`: `SiteSummaryHeader` (the name is tappable with `chevron--down` to open the switcher; the menu trigger sits at the right), `FirstRunTiles`, and the Member notice.
  - `SiteSwitcherSheet.kt`: a `ModalBottomSheet`. Each row shows name and Role, the current row is marked with `checkmark` in `primary-text` and `selected` semantics, and "New Site" comes last.
  - `SiteMenu.kt`: a Material `DropdownMenu`.
  - Garden is wired into `AppShell.kt`.
  - `ColdframeApplication` / `MainActivity` pass the `SitesEngine`.
- `apps/kt/android/src/main/res/values/strings.xml`: the new keys.
- `tests/kt/android/`:
  - UX-DR-named Robolectric tests for each surface: roles and labels, Member and Admin tiles, switcher selection, menu items.
  - Roborazzi snapshots of Create Site and Garden in light and dark at `AtFontScale(2f)`, with `assertNothingOverflows`.
  - Update the `CoverageTest` ids.

*iOS*
- `apps/swift/ios/Sources/ColdframeIOS/`:
  - `SitesPresentation.swift` (Foundation-only): `SitesSurface`, `CreateSitePresentation`, `GardenPresentation`, `SiteSwitcherRow`, `SiteMenuItem`, and the `@MainActor protocol SitesService`. It is built from the flat snapshot strings.
  - New `L10n` cases and catalogue entries.
  - `UI/SitesViews.swift` (`#if canImport(SwiftUI)`): `CreateSiteView`, `GardenView`, `SiteSwitcherSheet` (a native sheet), `SiteMenu` (a native `Menu`), and the `AppTabView` Garden wiring.
- `apps/swift/ios/App/CoreSitesService.swift`: the adapter over `IosSites`. `ColdframeApp.swift`: inject it.
- `tests/swift/ios/ColdframeIOSTests/`:
  - `SitesPresentationTests.swift`: UX-DR-named and Linux-runnable, one per matrix row the presentation decides.
  - Extend `RenderTests.swift`: Create Site and Garden, light and dark, at `.accessibility5`.
- `apps/swift/README.md`: list the `SitesService` protocol.

*Docs and ledger*
- `docs/quickstart.md`, `apps/ts/web/README.md`, `packages/kt/README.md`, `packages/ts` README if present: describe `GET /sites`, client generation (`pnpm --filter @coldframe/api-client generate`), and the new e2e and snapshot commands.
- `_bmad-output/implementation-artifacts/deferred-work.md`: add a DW entry for each Design Notes deferral.

**Acceptance Criteria:**
- **Create Site.** Given a signed-in user with no Membership, when they reach the web app, the Android app or the iOS app:
  - they see Create Site with a Site name field and the detected time zone to confirm;
  - submitting sends `POST /sites` with an `Idempotency-Key` header and `{name}`;
  - they land on the new Site's Garden.
- **Empty Garden.** Given a Site with no Hub and no Node, when Garden opens on any client, then it shows:
  - the Site summary header;
  - "No Readings yet";
  - the four first-run step tiles.
- **Site switcher.** On the same Garden, the Site switcher lists the user's Sites, each with its Role, and offers "New Site" last.
- **Snapshots and e2e.** Given the Playwright e2e test (sign in, then Create Site, then the empty Garden) and the Roborazzi snapshots, when they run in light and dark at the largest text size, then they pass. The iOS render tests at `.accessibility5` exist, but they run only on macOS (operator).
- **UX-DR tests.** Given the coverage tests, when they run, then UX-DR21, 22, 23, 54, 61, 62 and 82 each name at least one web test, one Android test and one Swift test. UX-DR22 behaves as the matrix row and Design Notes define it.
- **Build.** Given `dotnet build -warnaserror`, `dotnet format --verify-no-changes`, `dotnet test`, `pnpm -r lint typecheck test`, `./gradlew check` plus the iOS klib compile, and the swift container build/test/lint, when they run on this machine, then they all pass.

## Spec Change Log

## Review Triage Log

### 2026-09-28 — Review pass
- verdicts: 36 findings — high 0, medium 1, low 30, false 3, maybe-false 2
- findings:
  - `[medium]` `[patch]` (blind) On Android, Create Site from "New Site" was drawn over AppShell, so TalkBack could reach and activate the hidden shell. — `ColdframeRoot` now renders `CreateSiteScreen` instead of `AppShell` while `creating` is set.
  - `[low]` `[reject]` (blind) A failed Sites load (mobile `Failed`, web shell notice) hides Settings and Sign out. With a Certificate failure there is no action at all. — Certificates are publicly trusted and the sign-in probe already checks them. The other notices carry Try again. A Sign out escape would add a new action and branch to the notice design for a rare case.
  - `[low]` `[patch]` (blind) Kotlin and Swift doc comments cited the wrong UX-DR ids. — Corrected to epics.md: 21 is the header, 54 the tiles, 61 Create Site and the zone panel, 62 and 82 the Garden. Comments only.
  - `[low]` `[patch]` (blind) iOS Create Site had no Try again on its notice. — `CreateSiteView` now passes `notice.action` wired to `actions.submit`.
  - `[low]` `[patch]` (blind) The web mapped any status other than 400, 401 and 422 to `unavailable`, so a 500 said the sign-in service didn't answer. — Added `unexpected`: 503 stays `unavailable`, and other statuses show "The Site was not created: your Server returned an error. Try again." Tests cover 500 and 404.
  - `[low]` `[reject]` (blind) Mobile shows the Blank reason for a Server 400 on a non-empty name. — The client check mirrors the Server rule (1–100 characters after trimming), and keys are client-made UUIDs, so this is not reachable in practice. The fix adds a new error kind to the core and both catalogues.
  - `[low]` `[reject]` (blind) Per-device choices (current Site, confirmed zone) survive sign-out, so a second User on the same device inherits them. — The zone stays local and is sent nowhere in 1.8. A stale Site ID falls back to the first Site. Shared devices with several Users are unlikely. The later story that sends the zone (DW-23) must take the User into account.
  - `[low]` `[reject]` (blind) Time-zone Confirm behaves slightly differently per client: when it is stored, and whether Confirm shows while the list is open. — Cosmetic. Every client meets the spec rule (only a confirmed or picked zone is stored, and detection never overwrites it).
  - `[low]` `[reject]` (blind) The JVM/Android zone list includes legacy IDs, and a custom detected ID is saved unchecked. — Legacy IDs are still tzdb names, and a custom offset zone only happens with a manual OS setting. Filtering adds complexity for a rare case.
  - `[low]` `[reject]` (blind) Zone search matches differently per client (whole words on iOS, one substring elsewhere). — Cosmetic. Every client finds a zone by city or region name.
  - `[maybe-false]` `[defer]` (blind) On iOS, New Site may fail to open, because the root sheet is requested while the switcher sheet is still closing. — SwiftUI cannot run on Linux. Deferred (medium, unverified), and added to the iOS operator action.
  - `[low]` `[reject]` (blind) The web Site tabs use tab roles without a tabpanel or arrow-key behaviour. — The spec's task asks for `role="tablist"`, and the fix would edit this build's spec. Enter activates each tab link, and axe passes.
  - `[low]` `[reject]` (blind) Without JavaScript the time-zone panel shows no proposal. — The form still submits and nothing is sent anyway. No-JS use is unlikely, and the fix adds a server-rendered branch.
  - `[low]` `[reject]` (blind) `ListSitesTests` does not exercise the created_at tie-break or a revoked Membership. — Revocation deletes the membership row (Story 1.7 projector tests), and the tie-break is a deterministic SQL `ORDER BY`. These test additions guard improbable regressions.
  - `[low]` `[reject]` (blind) Android keeps 2 tile columns at font scale 2, while iOS switches to 1. — The committed font-scale-2 snapshots show no clipping. Cosmetic.
  - `[low]` `[patch]` (blind) An iOS done tile had a solid outline and helper typography, unlike Android and web. — Done tiles are now dashed and use `statusLabel`, and the test asserts `isDashed`.
  - `[low]` `[patch]` (edge) `SitesEngine.created()` applied the follow-up list without checking the session, so a sign-out in between was overwritten. — It now takes `started` and returns when the session changed. Test added.
  - `[low]` `[reject]` (edge) A token vault that cannot be read while signed in gives `Unauthorized`, then `Idle`, then a blank screen. — Rare, and it recovers on the next foreground: `resume()` reads an empty vault and shows the SignedOut notice. The fix adds a branch.
  - `[low]` `[reject]` (edge) A Transient refresh sends the expired token, the Server answers 401, and the user is signed out during a Keycloak outage. — The token is unusable anyway. After Keycloak recovers the user signs in again. Returning a distinct result would change the `accessToken()` contract.
  - `[low]` `[reject]` (edge) A Certificate `Failed` state has no action and no Sign out. — Same root and reason as the blind failed-load row.
  - `[low]` `[reject]` (edge) A Server 400 on a valid-looking name shows Blank. — Same root and reason as the blind row.
  - `[low]` `[reject]` (edge) Editing the name after a 503 and resubmitting with the same key costs an extra 422 round trip. — The outcome is still correct: 422, a new key, then success. The spec reuses the key for the same form attempt.
  - `[low]` `[patch]` (edge) iOS Create Site had no Try again. — Same root as the blind row; patched there.
  - `[maybe-false]` `[defer]` (edge) The iOS switcher-to-Create-Site sheet hand-off may be dropped. — Same root as the blind row; deferred.
  - `[low]` `[patch]` (edge) `Intl.supportedValuesOf` throws in older browsers. — Guarded with a fallback to an empty list.
  - `[low]` `[patch]` (edge) The web showed the unavailable copy for any unexpected status. — Same root as the blind row; patched there.
  - `[low]` `[patch]` (edge) The web Site menu stayed open after Tab moved focus out. — Added a focusout handler that closes the menu when focus leaves the root.
  - `[false]` `[reject]` (edge, claim) An unconfirmed detected zone is never stored, which was said to contradict the spec. — The spec's Always rule stores only a zone the user confirmed or picked, so this is the intended behaviour.
  - `[low]` `[patch]` (verification-gap) SitesWiring's `accessToken` and `onUnauthorized` wiring was untested. — Added `SitesWiringTest`: a 401 through `SitesWiring.api` gives `SignedOut(Notice.SignedOut)` and a cleared vault.
  - `[low]` `[patch]` (verification-gap) Create success followed by a failed follow-up list was untested. — Added a `SitesEngineTest` case: Ready([new], new) and the stored current Site.
  - `[low]` `[patch]` (verification-gap) The web shell's Sites notice and a 5xx `loadShell` were untested. — Moved the mapping to `sitesNoticeOf()` and tested every notice. `loadShell` is tested with 503 and 500.
  - `[low]` `[patch]` (verification-gap) The snapshot enum keys that Swift parses were only partly pinned. — `SitesSnapshotTest` now asserts every key of the six enums against the Swift raw values.
  - `[low]` `[reject]` (verification-gap, other) A Server 400 maps to Blank on mobile. — Same root and reason as the blind row.
  - `[low]` `[reject]` (verification-gap, other) An unreadable vault while signed in gives a blank screen. — Same root and reason as the edge row.
  - `[false]` `[reject]` (intent) The spec status is `in-review`, not `awaiting-operator`, and the work is uncommitted. — Finalization sets `awaiting-operator` and commits. The diff was taken before that.
  - `[false]` `[reject]` (intent) The web e2e runs against a fake Server, mobile is tested at state and render level, and iOS has not been compiled. — The intent sends human-only steps (Mac build, devices, a live Keycloak) to `operator_actions`, where they are listed. The Server surface runs for real in the AppHost suite.

## Design Notes

- **Why `GET /sites` is in scope.** The AC's "have no Membership" check and the Site switcher both need the caller's Sites with their Role. AD-4 forbids reading them from token claims. The read comes from the existing projection and index (whose migration already names "my Sites"). It is Authenticated like `createSite`, so the generated matrix covers it.
- **Time zone not sent.** The AC asks to show and confirm the zone, and says submit calls the Story 1.6 endpoint, which takes only `{name}`. AD-11 and FR16 make the zone a User preference, which the Notification Window story (epics.md 1667–1696, UX-DR48) persists on the User grain. Story 1.8 keeps the confirmed zone per device, so that story can send it later. Ledger this.
- **Site menu (UX-DR22) in 1.8.** The Pause sheet (UX-DR46/70) and stale mode arrive in later epics, and Site settings arrives in 1.9. The menu model carries the full specification (Pause/Resume Admin+ and hidden for Members, "Site settings", disabled with "Needs your Server" when stale) and is tested at the model level. Rendered, it shows only "Site settings", which opens the Settings index, because showing a Pause item that does nothing breaks "controls you cannot use are hidden". `pauseAvailable` turns on with the Pause story. Ledger this.
- **First-run tiles are not actionable yet.** The Add Hub flow is a later epic, so `flowAvailable = false` everywhere in 1.8. The tile states and the Member notice are real. Ledger this.
- **KMP client is hand-written.** AD-10 asks for generated clients. The TypeScript client is generated. For Kotlin, openapi-generator's multiplatform output does not fit `explicitApi()`, Ktor 3.6 and the value-result style. So the core's DTOs are hand-written, and a jvmTest parses `coldframe.openapi.json` and asserts that every operation, path, method, required header and DTO property the core uses exists there. Ledger the deviation.
- **Operator-owed work.** Linux cannot compile SwiftUI, link `ColdframeCore`, run the macOS render tests, or drive real devices. As in Story 1.5, those steps become `operator_actions` with `status: awaiting-operator`.

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror && dotnet format --verify-no-changes --no-restore`: expected to succeed and report no changes.
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build`: expected to pass everything, including the matrix and `ListSitesTests`.
- `pnpm install --frozen-lockfile && pnpm -r lint && pnpm -r typecheck && pnpm -r test`: expected to pass, including the api-client staleness test and the Playwright specs with their screenshots.
- `./gradlew check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64`: expected exit 0, including the Roborazzi verification.
- `podman run --rm --security-opt label=disable -v "$PWD":/work -w /work swift:6.3.3 sh -c 'swift build && swift test && swift format lint --strict -r .'`: expected exit 0.

## Auto Run Result

Status: awaiting-operator

**Summary.** Signed-in users can now create a Site and see its empty Garden on web, Android and iOS.
- **Server:** a new `GET /sites` (`listSites`, Authenticated) lists the caller's Active Sites with their Role from the identity projection, oldest first. It is in the OpenAPI file, the authorization matrix and `ListSitesTests`.
- **TypeScript client:** `@coldframe/api-client` is generated from the contract (openapi-typescript 7.13.0, openapi-fetch 0.17.0), and a test fails if the committed schema is stale.
- **Web (BFF):**
  - With no Site, every app route redirects to `/sites/new`.
  - Create Site takes a name and shows the time-zone panel. It sends one `Idempotency-Key` per attempt: the same key after a 503, a new one after a 422.
  - The empty Garden shows the Site header, "No Readings yet", four step tiles and a notice.
  - The AppHeader has Site tabs with the Role and "New Site" last, and a Site menu (Site settings).
- **KMP core:**
  - `ColdframeApi` returns results as values; a 401 signs the user out with the notice.
  - `SitesEngine` owns the Sites state and keeps the current Site and the confirmed zone per device.
  - Models for the first-run tiles and the Site menu, and a flat `SitesSnapshot` for Swift.
- **Android (Compose) and iOS (SwiftUI):** Create Site, Garden, the switcher sheet and the Site menu. Android has Roborazzi snapshots.
- **Not sent to the Server:** the time zone. It stays per device (AD-11, DW-23).
- **Owed by the operator:** SwiftUI and Xcode are not verified on this machine, and neither is anything that runs on a device. See `operator_actions`.

**Files changed**
- `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md`: `listSites` and `SiteList`.
- `apps/cs/server/Edge/EdgeApi.cs`, `apps/cs/server/Identity/IdentityReadModel.cs`: the `GET /sites` endpoint and `ListSitesAsync`.
- `tests/cs/server.integration/Edge/{AuthorizationMatrixTests,ListSitesTests}.cs`: a matrix sample and the list tests.
- `packages/ts/api-client/*`: the generator script, the generated `schema.ts`, and `createColdframeClient`. `tests/ts/api-client/*`: the staleness and header tests.
- `apps/ts/web/src/lib/server/{sites,shell,create-site}.ts`: Server calls, the shell load and the Create Site action.
- `apps/ts/web/src/lib/{sites,first-run,site-menu}.ts`: client-side models.
- `apps/ts/web/src/lib/components/{SiteTabs,SiteMenu,SiteSummaryHeader,FirstRunSteps,TimeZonePanel,AppHeader,AppShell,Icon}.svelte`: the UI.
- `apps/ts/web/src/routes/(app)/{+layout.server.ts,+layout.svelte,garden/+page.svelte,sites/new/*}`: routes. `apps/ts/web/src/lib/i18n/en.json`: copy.
- `tests/ts/web/{sites,garden,components,coverage}.test.ts`: unit tests.
- `tests/ts/web.e2e/*`: the fake Server, `garden.spec.ts` with screenshots, and the a11y loop.
- `gradle/libs.versions.toml`, `build.gradle.kts`, `gradle.properties`, `packages/kt/core/build.gradle.kts`: the serialization plugin, kotlinx-serialization and Roborazzi 1.75.0.
- `packages/kt/core/src/**/{api,sites}/*`: the API client, `SitesEngine`, `DeviceChoices`, time zones and `IosSites`.
- `packages/kt/core/src/commonMain/.../SignInEngine.kt`: `accessToken()` and `signOutExpired()`. `AndroidSignIn.kt`, `IosSignIn.kt`: wiring.
- `tests/kt/core/**`: the API, access token, wiring, engine, snapshot, OpenAPI contract and time-zone tests.
- `apps/kt/android/**`:
  - `ColdframeRoot`, `AppShell` and `MainActivity`: wiring;
  - `ui/sites/*`: Create Site, Garden, switcher and menu;
  - `DashedBorder`, the icons, the `supportWarning` colour and `strings.xml`.
- `tests/kt/android/**`: `SitesScreensTest`, `SnapshotTest` with baselines in `snapshots/`, coverage ids and support.
- `apps/swift/ios/Sources/ColdframeIOS/{SitesPresentation.swift,UI/SitesViews.swift,UI/Screens.swift,UI/Localization.swift,L10n.swift,Resources/Localizable.xcstrings}`: presentation, views and strings.
- `apps/swift/ios/App/{CoreSitesService,CoreSignInService,ColdframeApp}.swift`: the core adapter.
- `tests/swift/ios/ColdframeIOSTests/{SitesPresentationTests,RenderTests}.swift`: Swift tests.
- `docs/quickstart.md` and the READMEs (`apps/cs`, `apps/ts/web`, `apps/swift`, `packages/kt`, `packages/ts`, `packages/openapi`), plus `deferred-work.md` (DW-23 to DW-29).

**Review findings.** 36 in total: high 0, medium 1, low 30, false 3, maybe-false 2.
- **Patched (12 entries, 14 rows):** 1 medium and 11 low.
  - medium: on Android, Create Site now replaces the shell rather than overlaying it, so TalkBack can't reach the shell.
  - low: UX-DR ids in comments; iOS Try again; iOS done-tile style; web `unexpected` error copy; the session guard in `created()`; the `Intl.supportedValuesOf` guard; the Site menu closing on focusout; four test gaps (SitesWiring 401, create with a failed follow-up list, web shell notice and 5xx, snapshot enum keys).
- **Deferred (1):** the iOS switcher to Create Site sheet hand-off (medium, unverified). Added to the iOS operator action and to DW-29.
- **Rejected:** every other finding, each with its reason in the Review Triage Log. Among them: a failed load with no sign-out escape, the mobile 400 mapped to Blank, per-device choices not keyed by User, time-zone differences between clients, the tab ARIA pattern (spec-mandated), no-JS time zone, ListSitesTests extras, Android tile columns, an unreadable vault, Transient refresh, the key after a name edit, and three false claims.

**Follow-up review recommended: false.** Patched entries: high 0, medium 1, low 11. Only one medium was patched, and it is a render swap covered by the existing Android tests.

**Verification** (this machine, after the patches)
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror && dotnet format --verify-no-changes --no-restore`: 0 warnings, 0 errors, format clean.
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build`: 273 of 273 passed, including the AppHost matrix and `ListSitesTests`.
- `pnpm install --frozen-lockfile && pnpm -r lint && pnpm -r typecheck && pnpm -r test`: exit 0.
  - web unit: 264;
  - api-client: 4;
  - design-tokens: 599;
  - e2e: 31, including `garden.spec.ts` in light and dark at 200 % zoom, with axe and the screenshot comparison.
- `./gradlew --rerun-tasks check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64`: exit 0, 116 tasks run, 239 Kotlin tests with 0 failures, Roborazzi verified, ktlint and lint clean.
- `swift:6.3.3` container (podman): `swift build && swift test && swift format lint --strict -r .` gives exit 0 with 67 tests.
- Matrix audit: every I/O row is covered by a web, core or Android test (and Server tests for List my Sites), and all of them ran and passed.

**Residual risks**
- The SwiftUI views, `CoreSitesService` (including the `doNewSite()` export name) and the macOS render tests have never compiled.
- The iOS sheet hand-off is unverified (deferred).
- No device run against a real Server and Keycloak yet.
- The web e2e runs against a fake Server.
- The time zone stays per device, is not keyed by User, and is not sent (DW-23).
- The Site menu has no Pause, and the tiles are not actionable (DW-24 to DW-26).
- The Kotlin client is hand-written and held to the contract by a test (DW-27).
- There is no pull-to-refresh or refetch on focus (DW-28).
- Keeping the current Transient refresh behaviour means an expired token can sign the user out during a Keycloak outage.
