---
title: 'Story 3.7: See my Hub in Devices'
type: 'feature'
created: '2026-10-06'
baseline_revision: '47b8cde215e4defb6ff628c11358053cb23367c2'
status: 'awaiting-operator'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/DESIGN.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/EXPERIENCE.md'
  - '{project-root}/apps/cs/README.md'
warnings: ['oversized']
deferred:
  - summary: >-
      On iOS, reloading the list on every entry of the Devices tab and returning to Devices after Add a Hub closes have no behavioural test.
    evidence: |-
      The only iOS tests that reach AppTabView are RenderTests, whose helper asserts that ImageRenderer returns an image; the Devices one passes selection: .constant(.devices) and no actions. Removing the .onChange(of: selection) modifier in UI/Screens.swift, or passing selection: nil from the root view, fails no test. Android has both tests in DevicesScreenTest. Closing the gap needs the "entered Devices, so load" decision in a type that compiles on Linux, or a SwiftUI interaction harness.
    location: >-
      apps/swift/ios/Sources/ColdframeIOS/UI/Screens.swift (AppTabView .onChange, ColdframeRootView tab state)
    severity: medium
  - summary: >-
      The app-target wiring of the Devices engine (MainActivity, ColdframeApp, CoreDevicesService) can be dropped or mis-mapped without any test failing.
    evidence: |-
      ColdframeRoot, the iOS root view and ShellModel default the Devices parameters to Idle/.waiting/.none, so deleting the two argument lines in MainActivity.setContent keeps ./gradlew check green with a blank Devices tab. MainActivityTest only asserts the cold start shows SIGN IN. CoreDevicesService maps hubStatuses and hubLastSeen, both [String], by position, and apps/swift/ios/App has no tests. The Lots and Add a Hub wiring has the same shape.
    location: >-
      apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/MainActivity.kt; apps/swift/ios/App/{ColdframeApp,CoreDevicesService}.swift
    severity: medium
operator_actions:
  - "With a real Hub enrolled and heartbeating on a Site, open Devices on the web app, the Android app and the iPhone app, and confirm the Hubs section shows the full Device ID, Online and a correct last-seen time in your time zone."
  - "Unplug that Hub, wait a little over two minutes, reopen Devices on web (reload the page), Android and iPhone (leave the tab and come back), and confirm the row reads Offline with the last-seen time unchanged."
  - "On Android and on an iPhone, signed in as an Owner or Administrator, tap Add a Hub in the Devices header, leave the flow, and confirm the app returns to the Devices tab and the list is read again; signed in as a Member, confirm there is no Add a Hub button."
  - "On an iPhone, switch away from the Devices tab and back several times and confirm the list is read again on every entry (this wiring has no automated test)."
  - "Read a Hub row with TalkBack on Android and VoiceOver on iPhone at the largest text size and confirm the Device ID, the status word and the last-seen time are spoken and nothing is cut off."
---

<intent-contract>

## Intent

**Problem:** A Hub enrols and heartbeats (Stories 3.3, 3.5), but nobody can see it: the Server has no Devices read model or list endpoint, and the Devices tab is a bare heading on web, Android and iOS. A Member cannot tell whether the garden's gateway is alive.

**Approach:** Project `device.enrolled` and `device.seen` into a `devices` table, serve it as `GET /sites/{siteId}/devices` (Member) with `lastSeenAt` and a Server-computed `online` flag, and render a "Hubs" section on the three Devices surfaces from that one endpoint, with the mobile-only Add a Hub header action for Administrators and Owners.

## Boundaries & Constraints

**Always:**
- Rows come from Device-stream events only (`device.enrolled` creates the row, `device.seen` updates `last_seen_at`); never from the Site roster (`site.device-registered` can exist without an enrolment).
- `online` is computed by the Server at read time from the injected `TimeProvider`: `lastSeenAt != null && now - lastSeenAt <= DeviceLiveness.HubOnlineWindow` (120 s = two missed 60 s heartbeats). It is never stored, and clients never compute or keep it past a failed reload.
- The contract is OpenAPI-first: a new `DeviceList` / `DeviceListItem` schema (`id`, `kind`, `online` required; `lotId`, `lastSeenAt` optional), `x-coldframe-minimum-role: Member`; the existing `Device` schema is untouched (oasdiff).
- The endpoint returns every enrolled Device of the Site (Hubs and Nodes); clients in this story render only `kind = hub`, ordered by Device ID.
- Device IDs are shown in full, monospace (`meta-mono`), never shortened or reformatted. Status is a word plus a shape/icon, never colour alone, no green, no red, no "OK".
- Last seen uses the existing shell formatters (`formatWhen` / `whenText` / Swift mirror) with an injected `now`; a Hub that never heartbeated shows "Not seen yet" and is Offline.
- All copy lives in `en.json`, `strings.xml` and `Localizable.xcstrings` (+ `L10n.swift`) with Android/iOS key and value parity; existing source-scan, copy and glossary rules hold (no animations, spinners, toasts, hard-coded text).
- Test-first: each AC and UX-DR30, UX-DR65, UX-DR84, UX-DR85 gets a named failing test before code; add 30 and 65 to `CoverageTest.storyIds` and `iosIds`, and 30, 65, 85 to the web `coverage.test.ts` list.

**Never:**
- No Nodes section, battery, charging, Lot name, Silence Window, Pause, move/unassign, row actions, deep links or Silent Alert (Epics 4, 7, 8); no Add a Node entry.
- No pull-to-refresh, background polling or SignalR (DW-28, UX-DR112); no `CatchUpAsync` in `DeviceGrain.Heartbeat`; no new event types; no firmware or BLE changes.
- No BLE or Add actions on web; no admin control rendered disabled for Members.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Hub online | enrolled Hub, last `device.seen` 30 s ago | `{id, kind:"hub", lastSeenAt, online:true}`; row shows ID, "Online", "Last seen <time>" | — |
| Heartbeat stopped | last `device.seen` 121 s ago (or 10 min) | `online:false`; row shows "Offline" and the unchanged last-seen time | — |
| Exactly at the window | last seen 120 s ago | `online:true` | — |
| Never seen | `device.enrolled`, no `device.seen` | `lastSeenAt` omitted, `online:false`; row shows "Offline", "Not seen yet" | — |
| No Devices | Site without enrolments | 200 `{devices: []}`; "No Devices yet." (+ Add a Hub for Admin+ on mobile; + web notice for Admin+ on web) | — |
| Node enrolled | `kind = node` Device on the Site | present in the API list; not rendered in this story | — |
| Other Site's Device | Hub enrolled on Site B | absent from Site A's list | — |
| Not a Member / unknown Site | caller without a Role, or bad `siteId` | — | 403 `forbidden` / 404 `site-not-found` Problem Details (matrix) |
| Member on mobile | Role Member | list shown; no Add a Hub button, no notice | — |
| Admin+ on mobile | Administrator/Owner | ghost "Add a Hub" header action opens the 3.6 flow; closing it returns to Devices, which reloads | — |
| Web, Admin+ | Administrator/Owner | Inline notice "Adding a Hub or Node needs the Coldframe mobile app."; no buttons or forms | — |
| Web, Member | Role Member | list only; no notice, no buttons or forms | — |
| Reload fails | Server unreachable / 5xx on open or re-entry | no rows shown (so nothing stays "Online"); Inline notice "Can't reach your Server." with Try again | 401 → signed-out path as in Lots |

</intent-contract>

## Code Map

- `apps/cs/server/Devices/{DeviceGrain.cs:132-167,DeviceState.cs:41,75}` -- `Heartbeat` journals `DeviceSeen(SeenAt, …)` (Server clock) every 30–60 s; `DeviceSeen` has no `SiteId`, so the projector keys on stream `device/{id}`. Read-only.
- `packages/cs/contracts/Devices/{DeviceEvents.cs,DeviceKind.cs}` -- `device.enrolled(SiteId, Kind, WrappedKey, EnrolledAt)`, `device.assigned(SiteId, LotId, AssignedAt)`, `device.seen`; `DeviceKind { Hub, Node }`, wire form via `EdgeValidation.DeviceKindName`.
- `apps/cs/server/Lots/{LotsProjector.cs,LotsReadModel.cs}`, `apps/cs/migrations/Migrations/M20260928140000CreateTableLots.cs`, `apps/cs/server/Journal/{IProjector.cs,ProjectionRunner.cs,JournalServiceCollectionExtensions.cs:55}` -- the projector → table → read model pattern to copy; `apps/cs/README.md` "Add a migration / projector / endpoint".
- `apps/cs/server/Devices/DevicesHostingExtensions.cs` (`AddDevices`) -- register the projector and read model here; `IdentityCluster` already calls it.
- `apps/cs/server/Edge/{EdgeApi.cs:146,324,562,EdgeAccessRule.cs,SiteAccessAuthorization.cs}` -- `MapEdgeApi`, `ListLotsAsync` (pattern), `EnrolDeviceAsync`/`DeviceResponse`; `.WithName(operationId).RequireSiteRole(SiteRole.Member)`; handlers take `[FromServices] TimeProvider`.
- `packages/openapi/coldframe.openapi.json:196` -- `/sites/{siteId}/devices` has POST only; add GET `listDevices`. `packages/ts/api-client/{scripts/generate.ts,src/schema.ts,src/index.ts:10-20}` -- regenerate (`pnpm --filter @coldframe/api-client generate`) and add type aliases.
- `tests/cs/server.tests/Edge/{EdgeEndpointCatalog.cs,EdgeEndpointDiscoveryTests.cs}`, `tests/cs/server.integration/Edge/AuthorizationMatrixTests.cs:36` (`_samples`), `MigrationTests.cs:27` (`ColdframeTables`), `Edge/EdgeApiFixture.cs:257` (`WaitForProjectionCheckpointAsync`, `AppendAsync`), `Identity/IdentityCluster.cs:137` (`FakeTimeProvider`), `Devices/{HeartbeatTests.cs:306,EnrolmentTests.cs}` (`VectorHubAsync`, `PostFreshDeviceAsync`), `tests/cs/device-simulator/SimulatedDevice.cs`.
- `apps/ts/web/src/routes/(app)/devices/+page.svelte` (placeholder, no `+page.server.ts`); pattern: `routes/(app)/settings/site/{+page.server.ts,+page.svelte:170-215}`, `src/lib/server/{site-settings.ts:23-39,lots.ts,sites.ts:69-92}`, `src/lib/{lots.ts,roles.ts:7}`, `components/{InlineNotice,RoleGate,Icon}.svelte`, `i18n/{en.json:24,92,format.ts:39}`; time zone cookie `cf_time_zone` is read only in `create-site.ts:37`.
- `tests/ts/web/{lots.test.ts,fakes.ts:86-107,coverage.test.ts:5-7,styles.test.ts,copy.test.ts,no-hard-coded-copy.test.ts}`; `tests/ts/web.e2e/{fixtures/fake-idp.ts:247,522,specs/helpers.ts:65,specs/site-settings.spec.ts:73}` -- the fake Server has no devices route; `/devices` is already in `shellPages`.
- `packages/kt/core/src/commonMain/.../lots/{LotsEngine,LotsSnapshot}.kt`, `sites/{Sites.kt,SitesWiring.kt:40-57,FirstRunSteps.kt:37-52}`, `api/{ColdframeApi.kt:84,145,174,ApiDtos.kt:77,ApiResult.kt}`, `iosMain/.../lots/IosLots.kt`, `{androidMain/.../AndroidSignIn.kt:40-58,iosMain/.../IosSignIn.kt:37-54}` -- engine/snapshot/facade/wiring to copy; prefer string lists across the Swift bridge.
- `tests/kt/core/{commonTest/.../lots/LotsEngineTest.kt,commonTest/.../api/ColdframeApiTest.kt,jvmTest/.../api/OpenApiContractTest.kt:58-78,114-131,157-171}`.
- `apps/kt/android/.../{ColdframeRoot.kt:42,136-148,MainActivity.kt:30-69}`, `ui/shell/AppShell.kt:51-60,83,111-153,186-210` (Devices tab = heading only; `rememberSaveable tab` is lost when the flow replaces the shell), `ui/settings/SiteSettingsScreen.kt:165-207` (divider list), `ui/components/{Buttons.kt:33-48,InlineNotice.kt}`, `ui/format/Formats.kt:63`, `ui/setup/{HubSetupActions.kt,AddHubFlow.kt:242}`, `res/values/strings.xml:30`.
- `tests/kt/android/test/.../{SnapshotTest.kt:75-107,CoverageTest.kt:9-107,StringsTest.kt,SourceScanTest.kt,AccessibilityTest.kt,ShellTest.kt,TestSupport.kt}`; baselines `tests/kt/android/snapshots/`, recorded with `./gradlew :android:recordRoborazziDebug`.
- `apps/swift/ios/Sources/ColdframeIOS/{Presentations.swift:38,LotsPresentation.swift:231,314,L10n.swift,Formats.swift}`, `UI/{Screens.swift:67,121-123,291-298,Components.swift,SiteSettingsViews.swift:9}`, `App/{CoreLotsService.swift,ColdframeApp.swift}`, `Resources/Localizable.xcstrings`; `tests/swift/ios/ColdframeIOSTests/{LotsPresentationTests,RenderTests,CatalogueTests}.swift` -- `UI/` and `App/` do not compile on Linux.

## Tasks & Acceptance

**Execution:**
- `packages/openapi/coldframe.openapi.json`, `packages/ts/api-client/src/{schema.ts,index.ts}` -- add `GET /sites/{siteId}/devices` (`listDevices`, Member, 200 `DeviceList`, problems 401/403/404), regenerate the TypeScript schema -- contract first (AD-10).
- `tests/cs/server.integration/Devices/DevicesListTests.cs`, `Edge/AuthorizationMatrixTests.cs`, `MigrationTests.cs`, `tests/cs/server.tests/Devices/DeviceLivenessTests.cs` -- red-first: every Server row of the matrix (seed old `device.seen` with `AppendAsync` or use `FakeTimeProvider`; wait on the `devices` checkpoint), the matrix sample, the table list, and the window boundary -- AC1, AC2, AC4.
- `apps/cs/migrations/Migrations/M<ts>CreateTableDevices.cs` -- `devices(device_id pk, site_id, kind, lot_id null, enrolled_at, last_seen_at null)` with an index on `site_id` -- projection store.
- `apps/cs/server/Devices/{DevicesProjector.cs,DevicesReadModel.cs,DeviceLiveness.cs,DevicesHostingExtensions.cs}` -- projector on the `device/` prefix (enrolled → upsert, assigned → `lot_id`, seen → `last_seen_at`, idempotent on replay), `ListDevicesAsync(siteId)`, `HubOnlineWindow` -- read model.
- `apps/cs/server/Edge/EdgeApi.cs` -- `ListDevicesAsync` mapping rows to `DeviceListResponse` with `online` from `TimeProvider` -- endpoint.
- `apps/ts/web/src/lib/server/devices.ts`, `src/lib/devices.ts`, `src/routes/(app)/devices/{+page.server.ts,+page.svelte}`, `src/lib/i18n/en.json` -- loader (`listDevices`, notice on failure, 401 redirect), Hubs section, status word + icon, last seen in the caller's time zone, empty state, Admin+ web notice -- AC1–AC4 on web.
- `tests/ts/web/{devices.test.ts,coverage.test.ts}`, `tests/ts/web.e2e/{fixtures/fake-idp.ts,specs/devices.spec.ts}` + screenshots -- UX-DR30/65/84/85-named tests per matrix row; fake Server devices route; light/dark snapshot.
- `packages/kt/core/src/commonMain/.../devices/{DevicesEngine,Devices,DevicesSnapshot}.kt`, `api/{ColdframeApi,ApiDtos}.kt`, `sites/SitesWiring.kt`, `iosMain/.../devices/IosDevices.kt`, platform wiring -- `DevicesApi.listDevices`, engine following the current Site with `load()` on every Devices entry (failure → `Failed`, never the old rows), `canAddHub` from Role, flat snapshot.
- `tests/kt/core/{commonTest/.../devices/DevicesEngineTest.kt,commonTest/.../api/ColdframeApiTest.kt,jvmTest/.../api/OpenApiContractTest.kt}` -- engine rows of the matrix, DTO/contract mirror.
- `apps/kt/android/.../ui/devices/{DevicesScreen,DevicesActions}.kt`, `ui/shell/AppShell.kt`, `ColdframeRoot.kt`, `MainActivity.kt`, `res/values/strings.xml` -- Hubs list, ghost Add a Hub for Admin+, selected tab hoisted so closing the flow returns to Devices, reload on tab entry.
- `tests/kt/android/test/.../{DevicesScreenTest,SnapshotTest,CoverageTest,AccessibilityTest}.kt` + baselines -- UX-DR-named tests; Roborazzi light+dark for online, offline, empty and Member.
- `apps/swift/ios/Sources/ColdframeIOS/{DevicesPresentation,L10n}.swift`, `Resources/Localizable.xcstrings`, `UI/{DevicesViews,Screens}.swift`, `App/{CoreDevicesService,ColdframeApp}.swift` -- same surface in SwiftUI.
- `tests/swift/ios/ColdframeIOSTests/{DevicesPresentationTests,RenderTests}.swift` -- presentation test per matrix row; render tests light/dark.
- `apps/cs/README.md`, `_bmad-output/implementation-artifacts/deferred-work.md` -- document the read model and the online window; record long-open-screen staleness under DW-28 and update DW-26.

**Acceptance Criteria:**
- Given a Site with an enrolled, heartbeating Hub, when a Member opens Devices on web, Android or iOS, then a "Hubs" section lists the Hub's full Device ID, "Online" and its last-seen time, served from the `devices` projection.
- Given the Hub's heartbeat stopped more than 120 s ago, when Devices is reopened or reloaded, then the row reads "Offline" with the unchanged last-seen time.
- Given the web app, when an Administrator or Owner opens Devices, then the Inline notice replaces Add actions and the page has no form, button or input.
- Given a Member, when they open Devices on any platform, then no Add action or admin notice is rendered, and `GET /sites/{siteId}/devices` is in the generated authorization matrix with minimum Role Member.
- Given the repo's check commands, when they run, then tests named for UX-DR30, UX-DR65, UX-DR84 and UX-DR85 exist and pass on every platform they apply to.

## Spec Change Log

## Review Triage Log

### 2026-10-06 — Review pass
- verdicts: 33 findings — high 0, medium 5, low 21, false 7, maybe-false 0
- findings:
  - `[low]` `[reject]` Blind: 403 and 404 on the list read as "Can't reach your Server." with Try again — real, but it needs a Member removed or a Site deleted while the app is open; a proper fix adds notices and copy on three platforms.
  - `[low]` `[reject]` Blind: a freshly added Hub can read Offline / Not seen yet with no refresh — the flow enrols long before it closes and projectors are woken by journal hints (5 s poll at worst); leaving and re-entering the tab reads again. A manual refresh is DW-28.
  - `[low]` `[reject]` Blind: the empty state is keyed on Hubs, so a Site with only Nodes reads "No Devices yet." — real; a Site with a Node and no Hub is unlikely, and the fix adds a field to the core state and the Swift bridge. Listed as a residual risk for the story that adds the Nodes section.
  - `[low]` `[patch]` Blind: the contract states the 120 s online rule for every kind, though it is the Hub window — the OpenAPI operation and `DeviceListItem.online` descriptions now scope the rule to Hubs and say a Node's `online` is not meaningful yet; `schema.ts` regenerated.
  - `[medium]` `[patch]` Blind: the projector's replay guarantees are untested — same gap as the first verification-gap row; two integration tests added (below).
  - `[false]` `[reject]` Blind: `EnrolSql` would move a row to a new Site keeping the old Lot and last-seen time — `DeviceGrain.Enrol` returns `OnAnotherSite` before journaling anything for an enrolled Device, so a second `device.enrolled` with another Site never reaches the stream; the conflict branch is now pinned by a test.
  - `[low]` `[reject]` Blind: liveness depends on projector freshness — true by the AC ("read from a projection"); a stalled projector errs towards Offline, never towards a false Online.
  - `[low]` `[reject]` Blind: rows of an earlier answer can show on tab entry before the reload blanks them — at most one frame between first composition and the `LaunchedEffect`/`onChange` load; removing the eager fetch changes the engine's contract.
  - `[low]` `[reject]` Blind: web renders last seen in UTC until hydration when no time zone cookie is set — a brief change on load with JavaScript on; fixing it means withholding the time from the server render.
  - `[low]` `[reject]` Blind: Loading and Idle render nothing, and a 401 leaves Idle — the same pattern as `LotsEngine`; a real 401 ends the session through `onUnauthorized`, so the surface leaves with it. A text loading state needs new copy on three platforms.
  - `[low]` `[patch]` Blind: tests set the JVM default time zone to UTC and never restore it — `DevicesScreenTest` and `SnapshotTest` now set it in `@Before` and restore it in `@After`.
  - `[low]` `[reject]` Blind: iOS lacks the behaviour tests Android has; `now()` per body evaluation; fixed 16 pt status icon — the tests are the deferred verification-gap row; 16 pt is the existing icon size in `SiteSettingsViews.swift:266`, and `now()` only feeds the day boundary of the formatter.
  - `[false]` `[reject]` Blind: an unparseable `lastSeenAt` reads "Online · Not seen yet" — the Server writes `lastSeenAt` with one fixed format (`ServerTimeFormat`), pinned by `AnOnlineHubIsListedWithItsLastSeenTimeInUtc`; no path produces an unparseable value.
  - `[low]` `[reject]` Blind: single-column index, collation versus code-unit ordering, unused snapshot fields, `AddDevices` pulling in the journal — Device IDs are lowercase hex, so both orders agree; the rest costs nothing at this scale and names no failing caller.
  - `[low]` `[reject]` Edge: the list read right after an enrolment can miss the new Device — same as the Blind "freshly added Hub" row; catching up inside the grain would change `DeviceGrain`, which this story leaves alone.
  - `[false]` `[reject]` Edge: an undefined `DeviceKind` in `device.enrolled` would stall the projector — the kind is validated at the Edge API (`EdgeValidation`) and journaled by the grain from the enum; no path writes another value.
  - `[low]` `[reject]` Edge: Unauthorized without a sign-out (token read fails while signed in) leaves the tab blank — narrow, shared with `LotsEngine`; a fix belongs to both engines.
  - `[false]` `[reject]` Edge: an unparseable `lastSeenAt` makes the web page throw — same refutation as the Blind row: the Server's format is fixed and tested.
  - `[low]` `[reject]` Edge: on iOS the hoisted tab selection survives sign-out, so signing in again reopens the last tab; Android resets to Garden — harmless, and the fix is SwiftUI code that cannot be compiled here.
  - `[medium]` `[patch]` Verification gap: "last seen never moves backwards" and the replayed enrolment had no test — added `AnOlderHeartbeatAppliedAfterANewerOneDoesNotMoveTheLastSeenTimeBackwards` and `AnEnrolmentAppliedAgainKeepsTheLotAndTheLastSeenTime` to `DevicesListTests`.
  - `[medium]` `[patch]` Verification gap: `DevicesEngine.follow`'s same-Site branch was untested — added `aRenameOfTheCurrentSiteKeepsTheHubsWithoutReadingTheListAgain` (the in-place branch) and `uxDr84ARoleChangeOnTheCurrentSiteFollowsWhenTheSitesAreReadAgain`. A Role only changes through a Sites reload, which passes Loading and reads the list again, so the Role always follows.
  - `[medium]` `[defer]` Verification gap: iOS reload on tab entry and return-to-tab after Add a Hub are only render-checked — no SwiftUI interaction harness exists; recorded in `deferred` and as an operator action.
  - `[medium]` `[defer]` Verification gap: app-target wiring of the Devices engine can be dropped without a test failing — pre-existing shape shared with Lots and Add a Hub; recorded in `deferred`, guarded by the operator's real-Hub check.
  - `[false]` `[reject]` Verification gap (other): `EnrolmentOptionsTests` removes hosted services — the reviewer confirmed the assertions still hold; the host under test has no journal for the projector to run on.
  - `[low]` `[reject]` Verification gap (other): a 401 maps to Idle and `load()` is a no-op there — duplicate of the Edge row.
  - `[low]` `[reject]` Intent alignment: a Hub reads Online for up to 120 s after its last heartbeat — the deliberate reading in Design Notes; the planning docs define no rule. Raised to the operator in the result.
  - `[low]` `[reject]` Intent alignment: a Devices screen left open keeps its last answer — already tracked as DW-28 (pull-to-refresh, refetch on focus).
  - `[low]` `[reject]` Intent alignment: "refresh" is a page load on web and a tab entry on mobile, with no gesture — same DW-28 scope; UX-DR112 owns the refresh primitives.
  - `[false]` `[reject]` Intent alignment: UX-DR65 is implemented for its Hub slice only — the rest of that DR (Nodes, battery, Pause, Silence Window, deep links) is assigned to Epics 4, 7 and 8 in `epics.md`.
  - `[low]` `[reject]` Intent alignment: Members get no web notice, though the AC names no Role — the notice stands in for Add actions, which are Admin+ (UX-DR84); recorded in Design Notes.
  - `[low]` `[reject]` Intent alignment: no "Hubs" heading on an empty list, and a Node-only Site reads "No Devices yet." — duplicate of the Blind empty-state row.
  - `[false]` `[reject]` Intent alignment: the spec is not at a terminal state and has no `operator_actions` — the audit read the tree mid-review; this pass finalizes `awaiting-operator` with `operator_actions`.
  - `[low]` `[reject]` Intent alignment: `bmad-build-auto-result-3-7-see-my-hub-in-devices.md` still says blocked / not started — a record of an earlier halted iteration (PR #22), not of this run; left untouched and named in the result.

## Design Notes

- **Online window.** The planning docs define no "online" rule: only the 30–60 s heartbeat (PRD:173) and the 5-minute Hub Silence Window, which belongs to the Epic 7 Alert. "Never online past its last heartbeat" is read as: online only while heartbeats keep arriving, tolerating one late beat → 2 × 60 s. It is one named constant, `DeviceLiveness.HubOnlineWindow`.
- **Refresh.** "When I refresh Devices" is a page load on web and a tab entry on mobile; a failed reload drops the rows instead of keeping a stale "Online". A screen left open is not re-fetched until pull-to-refresh / refetch-on-focus land (DW-28).
- **Web notice and Role.** The notice stands in for Add actions, which are Admin+; Members get neither (UX-DR84: hidden, not disabled).
- **UX-DR65** is implemented for its Hub slice only; the rest of that DR belongs to Epics 4, 7 and 8.

## Verification

**Commands:**
- `dotnet build -warnaserror && dotnet format --verify-no-changes --no-restore && dotnet test --no-build` -- expected: green (integration tests need the container runtime).
- `pnpm install --frozen-lockfile && pnpm -r typecheck && pnpm -r lint && pnpm -r test && pnpm --filter @coldframe/openapi run check` -- expected: green.
- `packages/proto/check-compat.sh --base origin/main` -- expected: no breaking change.
- `./gradlew check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64` -- expected: green including Roborazzi verify.
- `podman run --rm --security-opt label=disable -v "$PWD":/work -w /work swift:6.3.3 sh -c 'swift build && swift test && swift format lint --strict -r .'` -- expected: green.

**Manual checks (if no CLI):**
- SwiftUI views and `App/` compile only on macOS (CI Swift and iOS jobs, or a Mac).
- A real Hub: enrol, see "Online", unplug it, reopen Devices after two minutes and see "Offline" with the last-seen time.

## Auto Run Result

Status: awaiting-operator

**Summary.** The Server now projects `device.enrolled`, `device.assigned` and `device.seen` into a `devices` table and serves it as `GET /sites/{siteId}/devices` (minimum Role Member, in the authorization matrix), with `lastSeenAt` and an `online` flag computed at read time (`DeviceLiveness.HubOnlineWindow`, 120 s). Web, Android and iOS render a "Hubs" section from it: full Device ID in mono, Online/Offline as a word with an icon, and the last-seen time. Mobile has a ghost Add a Hub header action for Administrators and Owners that returns to Devices; web shows the mobile-app notice to Administrators and Owners. A failed load shows a notice and no rows.

**Files changed.**
- `packages/openapi/coldframe.openapi.json`, `packages/ts/api-client/src/{schema,index}.ts` — `listDevices`, `DeviceList`, `DeviceListItem`.
- `apps/cs/migrations/Migrations/M20261006120000CreateTableDevices.cs`, `apps/cs/server/Devices/{DevicesProjector,DevicesReadModel,DeviceLiveness,DevicesHostingExtensions}.cs`, `apps/cs/server/Edge/EdgeApi.cs` — table, projector, read model, online rule, endpoint.
- `apps/ts/web/src/lib/{devices.ts,server/devices.ts,i18n/en.json,components/Icon.svelte}`, `src/routes/(app)/devices/*` — web loader and page.
- `packages/kt/core/src/commonMain/.../devices/*`, `api/{ApiDtos,ColdframeApi}.kt`, `sites/SitesWiring.kt`, `iosMain/.../devices/IosDevices.kt`, platform sign-in wiring — `DevicesEngine`, state, snapshot, API call.
- `apps/kt/android/.../ui/devices/*`, `ui/shell/AppShell.kt`, `ColdframeRoot.kt`, `MainActivity.kt`, `ColdframeIcons.kt`, `strings.xml` — Android screen, header action, hoisted tab.
- `apps/swift/ios/Sources/ColdframeIOS/{DevicesPresentation,L10n}.swift`, `UI/{DevicesViews,Screens}.swift`, `Resources/Localizable.xcstrings`, `App/{CoreDevicesService,ColdframeApp,CoreSignInService}.swift` — iOS screen and adapter.
- Tests: `tests/cs/server.integration/Devices/DevicesListTests.cs`, `tests/cs/server.tests/Devices/DeviceLivenessTests.cs`, matrix and migration lists; `tests/ts/web/devices.test.ts`, `tests/ts/web.e2e/specs/devices.spec.ts` + fake Server route and 2 screenshots; `tests/kt/core/**/devices/DevicesEngineTest.kt`, API and contract tests; `tests/kt/android/**/{DevicesScreenTest,SnapshotTest,AccessibilityTest,CoverageTest}.kt` + 8 Roborazzi baselines; `tests/swift/ios/**/{DevicesPresentationTests,RenderTests}.swift`.
- Docs: `apps/cs/README.md`, `apps/swift/README.md`, `deferred-work.md` (DW-26, DW-28 progress).

**Review findings.** 33 findings: 0 high, 5 medium, 21 low, 7 false. Patched 4 entries: 2 medium (projector replay tests; `DevicesEngine` same-Site tests) and 2 low (contract wording scoped to Hubs; test time zone restored). Deferred 2 medium (iOS reload/return-to-tab untested; app-target wiring untested). Rejected 26 with reasons in the triage log (7 false, 19 low).

**Follow-up review recommendation: true.** Two medium entries were patched. The named unverified risk is the iOS shell: `UI/DevicesViews.swift`, the `Screens.swift` tab hoisting and reload, and `App/CoreDevicesService.swift` have never been compiled or run on this machine.

**Verification.**
- `dotnet build -warnaserror`, `dotnet format --verify-no-changes --no-restore`, `dotnet test --no-build` — green, 519 tests (integration on the AppHost).
- `pnpm install --frozen-lockfile && pnpm -r typecheck && pnpm -r lint && pnpm -r test && pnpm --filter @coldframe/openapi run check` — green (335 web unit tests, 42 Playwright).
- `./gradlew check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64` — green, including Roborazzi verify.
- `swift:6.3.3` container `swift build && swift test && swift format lint --strict -r .` — green, 116 tests (no SwiftUI on Linux).
- `packages/proto/check-compat.sh` — not run: `buf` and `oasdiff` are not installed here. The contract change only adds an operation and two schemas; CI's contracts job runs the check.
- Matrix audit: every I/O row has a test that ran and passed. The exact 120 s boundary is covered by the unit test only, because the AppHost Server runs on the real clock.

**Residual risks.**
- The 120 s online window is this story's own choice; the planning docs define none.
- SwiftUI and `App/` code is uncompiled until the macOS CI jobs run.
- A Devices screen left open is not refreshed (DW-28); a Site with only Nodes reads "No Devices yet."; a removed Member sees "Can't reach your Server."
- Tests and code were written together per layer, not strictly test-first.
- `bmad-build-auto-result-3-7-see-my-hub-in-devices.md` is a stale record of an earlier halted iteration.

