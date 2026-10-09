---
title: 'Story 6.2: See Alerts in the apps'
type: 'feature'
created: '2026-10-09'
baseline_revision: 98fba9e6b70ac8f005c1ead86add36d1202ffe05
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/DESIGN.md'
  - '{project-root}/apps/cs/README.md'
warnings: ['oversized']
deferred:
  - summary: >-
      On iOS, Lot detail opened from an Alert row shares one core Lot-detail state with the Garden tab's stack, and each stack opens it on appear and closes it on disappear.
    evidence: |-
      AlertsView.navigationDestination and GardenView.navigationDestination both call lotDetailActions.open on appear and close() on disappear, unguarded. With Lot detail pushed on both tabs, a tab switch may run the leaving stack's close() after the entering stack's open(), leaving the shown Lot detail idle. Whether it happens depends on SwiftUI's appear/disappear order across TabView tabs; the views do not compile on this host. Running both stacks on a simulator settles it; a guard that closes only the Lot this destination opened would fix it.
    location: >-
      apps/swift/ios/Sources/ColdframeIOS/UI/AlertsViews.swift (navigationDestination), UI/SitesViews.swift:273-283
    severity: medium (unverified)
  - summary: >-
      The contract's Alert requires quantity (and the alerts table sensor_id and quantity), which a silent or battery Health Alert of Epic 7 may not have.
    evidence: |-
      Alert.quantity is required in packages/openapi, alerts.quantity and alerts.sensor_id are NOT NULL, and AlertDto.quantity is a non-null String, so one item without quantity fails the whole list on a shipped mobile client. Today AlertOpened itself requires Quantity and SensorId, so the contract mirrors the Server. Whether this bites depends on how Epic 7 shapes Health Alerts; its Alert events settle it. If they carry no quantity, make the field optional before any mobile release.
    location: >-
      packages/openapi/coldframe.openapi.json (Alert), apps/cs/migrations/Migrations/M20261009093000CreateTableAlerts.cs
    severity: medium (unverified)
  - summary: >-
      No test shows that a foreground of the Android or iOS app reaches AlertsEngine.refresh(), nor that CoreAlertsService maps the snapshot's parallel lists to the right fields.
    evidence: |-
      MainActivity builds the real core inline; its foreground lambda is checked only by a source-text regex in LotOverviewTest that names lots.refresh(). Removing alerts.refresh() from MainActivity.kt or ColdframeApp.swift fails no test, and swapping alertLotIds with alertLotNames in CoreAlertsService compiles and passes. The iOS App target has no test target. An executable test needs the foreground lambda extracted (Android) and a test target for apps/swift/ios/App.
    location: >-
      apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/MainActivity.kt (onForeground), apps/swift/ios/App/{ColdframeApp,CoreAlertsService}.swift
    severity: medium
  - summary: >-
      The SwiftUI Alerts views, the Screens.swift edits, the App target wiring and the iOS render tests have never been compiled or run; only macOS CI does that.
    evidence: |-
      No Swift toolchain with SwiftUI exists on this host. The Linux container compiles and tests AlertsPresentation, L10n and the catalogue only. The render tests assert that an image comes out, not what it shows (DW-20). The VoiceOver label of the tab ("Alerts, N open") is set on the Label inside .tabItem, which SwiftUI may ignore; a device or UI test settles it.
    location: >-
      apps/swift/ios/Sources/ColdframeIOS/UI/{AlertsViews,Screens}.swift, apps/swift/ios/App/
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** Story 6.1 opens and closes Threshold Alerts on the Server, but nothing serves them: there is no Alerts read model or endpoint, the web `/alerts` page and both mobile Alerts tabs show only a heading, and the mobile tab never carries a count. A Member cannot see what is wrong now or what resolved itself.

**Approach:** A new `alerts` projector builds an Alerts read model from the `alert/` streams, and `GET /sites/{siteId}/alerts` serves it: open Alerts first, then Alerts closed in the last 7 days, cursor-paginated, with the open count. The web app, the shared Kotlin core, Android and iOS read it and render the Alerts surface with the Alert row in its four variants.

## Boundaries & Constraints

**Always:**
- Endpoint `GET /sites/{siteId}/alerts`, `operationId` `listAlerts`, minimum Role Member, query `cursor` and `limit` (default 50, 1-200). Response `{ alerts, openCount, nextCursor? }`. Order: open Alerts newest `openedAt` first, then closed Alerts newest `closedAt` first, ties by ID. A closed Alert is served only while `closedAt` is within 7 days of the Server clock (`TimeProvider`, never SQL `now()`). `openCount` counts every open Alert of the Site on every page. A bad `cursor` or `limit` is 400 `validation`; the cursor is opaque and versioned.
- An Alert item: `id`, `kind`, `side?` (`low`/`high`), `quantity`, `lotId`, `lotName`, `deviceId`, `openedAt`, `closedAt?`, `reason?` (`recovered | paused | unassigned | calibrated | removed`). Enums are lowercase strings mapped by hand; absent optionals are omitted. `kind` is an extensible enum: the Server produces only `threshold`; `silent`, `battery` and `uncalibrated` are named for Epic 7.
- The read model is written only by its own projector (AD-21), is idempotent on a replayed event, and rebuilds from position 0 to the same rows. The Lot name is joined from `lots` at query time.
- Clients group open Alerts as Threshold (kind `threshold`) then Health (any other kind), newest first in each, then "Closed". The empty state is "No open Alerts.", followed by Closed when it has rows. Every client follows `nextCursor` to the end, up to a fixed page cap.
- Row variants follow DESIGN.md `alert-row-*`: needs water only for an open, low-side, soil-moisture Threshold Alert (solid orange, `rain-drop`, eyebrow "Needs water" + time); other open Threshold (`layer-01`, 2 px `border-strong`, `arrow--down` below low / `arrow--up` above high, eyebrow "Threshold Alert · below low" / "· above high" + time); Health (hatch, 1 px dashed, text on a plate, icon `help`/`battery--low`/`tools`, eyebrow "Health Alert"); Closed (transparent, 1 px `border-subtle`, `text-secondary`, eyebrow "… · closed <time>"). Uppercase comes from style. Title in `section` type: "<Lot> needs water", "<Lot> too wet", "<Lot> <quantity> too low" / "too high".
- A row is one tap target with one screen-reader label stating the condition and when it started, e.g. "Tomatoes needs water, since 05:45" (closed adds "closed 06:40"). Threshold and uncalibrated rows open Lot detail of `lotId`; silent and battery rows open Devices. Rows have no other action.
- Times use each client's existing "when" rule (today clock time, earlier weekday, older date). All strings are externalised with identical keys and values on Android and iOS.
- The mobile Alerts tab label is "Alerts · N" while N > 0 open Alerts (spoken "Alerts, N open"), else "Alerts". The count is loaded when a Site becomes current and on foreground, not only when the tab is opened. Mobile Alerts has pull-to-refresh; web refetches on focus.
- Test-first (NFR16): every named test fails before its implementation. UX-DR14, 25, 26, 64, 82 and 98 each name at least one test per client.

**Never:** No Health Alert production, no notification, Reminder, window, push or SignalR work (6.3-6.6, Epic 7). No value, Threshold or `measuredAt` on the row (DW-88), and no fix for a moved Node's Lot (DW-85). No web Open Alerts rail, no count in the web side nav, no stale mode for Alerts. No change to Alert, Site or Sensor grains, events or `lot_status_alerts`. No "All good", no orange outside needs water, no Alert actions. No edit to a shipped migration, no write to `sprint-status.yaml`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Open and closed | 2 open, 1 closed 2 days ago, 1 closed 8 days ago | 3 items: open newest first, then the 2-day one; `openCount` 2 | — |
| Other Site | Alerts on Site B, caller asks Site A | none of B's Alerts | not a Member of B: 404/403 as the matrix says |
| Paging | 3 Alerts, `limit=2` | 2 items + `nextCursor`; next page 1 item, no cursor; no item twice | — |
| Bad query | `limit=0`, `limit=201`, repeated `limit`, malformed `cursor` | — | 400 `validation` |
| Closed row | `alert.closed` reason `recovered` | item has `closedAt`, `reason: "recovered"`; leaves `openCount` | — |
| Replay | the same event applied twice | one unchanged row | — |
| No Alerts | Site without Alerts | `{ alerts: [], openCount: 0 }`; clients show "No open Alerts." | — |
| Only closed | no open, 1 closed | "No open Alerts." then Closed with the row | — |
| Too wet | open, high side, soil moisture | neutral Threshold row, `arrow--up`, never orange | — |
| Unknown kind | `kind` a client does not know | Health group, `help` icon | — |
| Load fails | Server unreachable | notice with Try again, no rows; tab label without count | 401 signs out as elsewhere |

</intent-contract>

## Code Map

**Server**
- `packages/cs/contracts/Alerts/AlertEvents.cs` l.18-43, `AlertGrains.cs` l.46-103 -- `AlertOpened`/`AlertClosed` fields, `AlertKind`, `ThresholdSide`, `AlertCloseReason`. Read-only.
- `apps/cs/server/Lots/LotsProjector.cs` -- `alert/` branch `ApplyAlertAsync` l.392-438, idempotent SQL l.136-148, `KindName`/`SideName` l.441-448, `IdOf` l.256-261: the model for the new projector. `apps/cs/server/Devices/DevicesProjector.cs` is the single-prefix model.
- `apps/cs/server/Alerts/AlertsHostingExtensions.cs` l.13-18 -- empty `AddAlerts()`; register the read model and `AddProjector<…>()` here (test silo already calls it: `tests/cs/server.integration/Identity/IdentityCluster.cs` l.208-213).
- `apps/cs/server/Lots/LotDetailReadModel.cs` l.86, l.210-284 -- the only cursor precedent: `limit + 1`, versioned Base64Url cursor, `DecodeCursor` null on anything else. `DevicesReadModel.cs` l.35 joins `lots`.
- `apps/cs/server/Edge/EdgeApi.cs` -- records l.26-340, `MapEdgeApi` l.375-463 (`listLots` l.439-441), `ListLotsAsync` l.589-599, `TryReadLimit`/`TryReadCursor`/`SingleValue`/`InvalidHistoryQuery` l.859-914, `ToServerTime` l.1848.
- `apps/cs/migrations/Migrations/` -- latest `M20261009065300…`; `M20261008120000CreateTableLotStatusAlerts.cs` is the table-creation model. `tests/cs/server.integration/MigrationTests.cs` l.28-45 lists tables.
- `packages/openapi/coldframe.openapi.json` -- `listLots` l.430-450, `getLotHistory` cursor/limit l.564-623, `LotHistory.nextCursor` l.919; `ThresholdSide` (l.1161) is taken, name the enum `AlertSide`. `packages/openapi/README.md` l.6-27 operations table.
- `tests/cs/server.tests/Edge/EdgeEndpointDiscoveryTests.cs` l.51-60 (contract = mapped endpoints), `tests/cs/server.integration/Edge/AuthorizationMatrixTests.cs` `_samples` l.37-155 (add a sample), `Edge/LotStatusTests.cs` (`SeedSiteAsync` l.631, `OpenAlertAsync` l.566-585, close l.441-446, replay l.431-437, `Names` l.498), `Devices/ThresholdAlertGrainTests.cs` l.703-733 (rebuild from 0), `tests/cs/server.tests/Lots/LotDetailUnitTests.cs` l.39-53 (cursor tests). `[Collection(IngestSuites.Name)]`.
- `apps/cs/README.md` -- endpoint table l.113-131, "Alerts" l.483-547 (l.485 says the read model is Story 6.2).

**Web**
- `packages/ts/api-client/src/{schema.ts,index.ts}` -- regenerate (`pnpm --filter @coldframe/api-client generate`); re-export aliases l.10-39.
- `apps/ts/web/src/lib/server/devices.ts` l.36-65, `routes/(app)/devices/+page.server.ts` -- loader model (no stale mode); `lib/server/sites.ts` `call` l.69-92.
- `apps/ts/web/src/routes/(app)/alerts/+page.svelte` -- title-only today; nav entry exists (`SideNav.svelte` l.18-23).
- `apps/ts/web/src/lib/lot-tiles.ts` l.153-159, l.227 and `components/LotTiles.svelte` l.59, l.163-202 -- pure row-model pattern, orange, outline, hatch (`Hatch.svelte` `plate`), one `<a aria-label>` to `/garden/{lotId}`. `devices/+page.svelte` l.82-125, l.232-256 -- sections, empty paragraph, notice; `garden/+page.svelte` l.69-104 -- refetch on focus.
- `apps/ts/web/src/lib/components/Icon.svelte` l.2-40 -- add `arrow--up`, `arrow--down`. `lib/i18n/en.json` (`alerts.title`, `lotDetail.since`, `lotDetail.quantity.*`, `list.dot`), `lib/i18n/format.ts` `formatWhen` l.51.
- `tests/ts/web/` -- `devices.test.ts` (page render, `fakes.ts`), `lot-tiles.test.ts`, `coverage.test.ts` l.9-12 `storyIds`, `copy.test.ts`, `no-hard-coded-copy.test.ts`, `styles.test.ts`. `tests/ts/web.e2e/` -- `fixtures/fake-idp.ts` l.311-557 (add the route and seed), `specs/helpers.ts` l.20-34 `resetSites`, `specs/devices.spec.ts` l.69-134 and `lot-status.spec.ts` l.140-176 (light/dark screenshots, fixed clock).
- `apps/ts/web/README.md` -- add "Alerts"; `## Tests` l.218-233.

**Shared Kotlin core** (`packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/`)
- `api/ColdframeApi.kt` l.47-59, l.119-131 (cursor parameter), `api/ApiDtos.kt` l.97-104; `tests/kt/core/jvmTest/.../api/OpenApiContractTest.kt` `assertMirrors` l.58.
- `devices/{DevicesEngine,Devices,DevicesSnapshot}.kt`, `iosMain/.../devices/IosDevices.kt`, `sites/SitesWiring.kt` l.96-100, `signin/{AndroidSignIn,IosSignIn}.kt` -- engine, state, flat snapshot, bridge and wiring to mirror. `lots/LotsEngine.kt` l.155-198 -- `refresh()` with `refreshing`. `lots/LotDetailEngine.kt` l.399-417 -- cursor loop.
- `packages/kt/core/README.md` l.125-139, `packages/kt/README.md` l.16-20.

**Android** (`apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/`)
- `ui/shell/AppShell.kt` -- `Tab` l.70-79, bar label l.230, Alerts placeholder l.247, Lot detail l.127-135, l.277, `onOpenDevices` l.259-262, reload on entry l.138-140; `MainActivity.kt` l.32-65, l.105-109; `ColdframeRoot.kt` l.70-238.
- `ui/sites/LotTile.kt` l.99-246, l.362-377; `ui/sites/GardenScreen.kt` l.132-146, l.252-260 (pull-to-refresh, `RefreshBar`); `ui/devices/{DevicesScreen,DevicesActions}.kt`; `ui/components/{Hatch,DashedBorder,InlineNotice}.kt`; `ui/format/Formats.kt` `whenText` l.63; `res/values/strings.xml` (`nav_alerts`, `count_open_alerts`).
- `tests/kt/android/test/` -- `SnapshotTest.kt` l.118-163, l.219-258, l.485-588; `ShellTest.kt` l.61-82; `StringsTest.kt`; `CoverageTest.kt` l.9, l.94; `AccessibilityTest.kt`; `SourceScanTest.kt`. Baselines `tests/kt/android/snapshots/`.

**iOS** (`apps/swift/ios/`)
- `Sources/ColdframeIOS/{Presentations.swift l.38-75, DevicesPresentation.swift l.325, LotsPresentation.swift l.47-160, L10n.swift, Resources/Localizable.xcstrings}`; `UI/{Screens.swift l.77-181, l.424+, DevicesViews.swift, SitesViews.swift l.261-283, SiteSettingsViews.swift l.314-426, Hatch.swift}`; `App/{CoreDevicesService,ColdframeApp}.swift`.
- `tests/swift/ios/ColdframeIOSTests/` -- `DevicesPresentationTests.swift`, `ShellPresentationTests.swift`, `CatalogueTests.swift`, `SourceRulesTests.swift`, `RenderTests.swift` l.457-497 (light/dark render, macOS only). `apps/swift/README.md` l.11-45.

## Tasks & Acceptance

**Execution:**
- `tests/cs/server.tests/Alerts/AlertsReadModelUnitTests.cs`, `tests/cs/server.integration/Edge/AlertsListTests.cs`, `AuthorizationMatrixTests.cs` -- failing tests first: cursor round-trip and malformed, response property names, one test per Server matrix row, rebuild from 0, the matrix sample -- NFR16
- `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md` -- `listAlerts`, `Alert`, `AlertList`, `AlertKind`, `AlertSide`, `AlertCloseReason` -- contract first
- `apps/cs/migrations/Migrations/M<yyyyMMddHHmmss>CreateTableAlerts.cs` -- table `alerts` with an index for the Site query
- `apps/cs/server/Alerts/{AlertsProjector,AlertsReadModel,AlertsHostingExtensions}.cs` -- projector `alerts` on `alert/`, keyset query, count, cursor
- `apps/cs/server/Edge/EdgeApi.cs` -- records, mapping, handler, mapper
- `apps/cs/README.md` -- endpoint row and the read-model section
- `packages/ts/api-client/src/**` -- regenerate and re-export
- `tests/ts/web/alerts.test.ts`, `tests/ts/web/coverage.test.ts`, `tests/ts/web.e2e/{fixtures/fake-idp.ts,specs/helpers.ts,specs/alerts.spec.ts}` -- failing tests first: row model per variant, page groups, empty and only-closed states, loader paging and failure, light/dark screenshots with axe
- `apps/ts/web/src/lib/{alerts.ts,server/alerts.ts,components/AlertRows.svelte,components/Icon.svelte,i18n/en.json}`, `routes/(app)/alerts/{+page.server.ts,+page.svelte}`, `apps/ts/web/README.md` -- row model, loader, rows, page, strings
- `tests/kt/core/**` -- failing tests first: API call and paging, engine states, snapshot, DTO mirror of the contract
- `packages/kt/core/src/{commonMain,iosMain,androidMain}/**/{api,alerts,sites,signin}/**`, `packages/kt/core/README.md`, `packages/kt/README.md` -- `AlertsApi`, DTOs, `AlertsEngine` (follows the current Site, `load`, `refresh`), state, flat snapshot, `IosAlerts`, wiring
- `tests/kt/android/test/**`, `tests/kt/android/snapshots/alerts-*.png` -- failing tests first, then recorded baselines: every row variant, both groups, empty and only-closed, light and dark; tab label with count; row label and tap
- `apps/kt/android/src/main/**` -- `ui/alerts/{AlertsScreen,AlertRow,AlertsActions,AlertsCopy}.kt`, `AppShell.kt`, `ColdframeRoot.kt`, `MainActivity.kt`, `strings.xml`, `apps/kt/README.md`
- `tests/swift/ios/ColdframeIOSTests/{AlertsPresentationTests,ShellPresentationTests,RenderTests,CatalogueTests}.swift` -- failing tests first: presentation per variant, grouping, tab label, catalogue parity, light/dark render
- `apps/swift/ios/**` -- `AlertsPresentation.swift`, `UI/AlertsViews.swift`, `Screens.swift`, `Presentations.swift`, `L10n.swift`, `Localizable.xcstrings`, `App/{CoreAlertsService,ColdframeApp}.swift`, `apps/swift/README.md` (SwiftUI and `App/` compile only in macOS CI)

**Acceptance Criteria:**
- Given open and recently closed Alerts on a Site, when a Member opens Alerts on web, iOS or Android, then Threshold rows come before Health rows, newest first, and Alerts closed in the last 7 days sit under "Closed".
- Given an open low-side soil-moisture Alert and an open high-side one, when Alerts renders, then only the first is orange and reads "<Lot> needs water"; the other uses the neutral Threshold treatment.
- Given a Threshold Alert row, when it is tapped, then Lot detail of its Lot opens, and its screen-reader label states the condition and when it started.
- Given no open Alerts, when Alerts renders, then it shows "No open Alerts." and never "All good".
- Given 5 open Alerts on the current Site, when the mobile tab bar renders on any tab, then the Alerts tab reads "Alerts · 5".
- Given the endpoint, when the endpoint discovery and authorization matrix tests run, then `GET /sites/{siteId}/alerts` is in the contract with minimum Role Member and passes every Role and Site case.
- Given the snapshot suites (Playwright, Roborazzi) and the iOS render tests, when they run, then each row variant, both groups and the empty state are covered in light and dark themes.

## Spec Change Log

## Review Triage Log

### 2026-10-09 — Review pass
- verdicts: 41 findings — high 0, medium 4, low 27, false 6, maybe-false 4
- findings:
  - `[low]` `[patch]` Blind: the web README says an unknown kind opens Lot detail; the code opens Devices — sentence corrected.
  - `[low]` `[patch]` Blind: web kept a Threshold Alert of an unknown quantity as a Threshold row with the raw contract name, mobile makes it the unknown Health row — web now does the same (`alerts.title.other`, `help`, `/devices`), with a test.
  - `[maybe-false]` `[defer]` Blind: the contract requires `quantity`, which Epic 7's silent and battery Alerts may not have — today `AlertOpened` requires it too; Epic 7's events settle it. Deferred as medium (unverified).
  - `[low]` `[reject]` Blind: page caps truncate silently and differ (web 200 x 10, core 50 x 20) — real, but it needs over 1000 Alerts open or closed within 7 days on one Site; aligning and surfacing it adds state on three clients. The false comment is patched below.
  - `[low]` `[reject]` Blind: an Alert that closes between two page reads keeps its open copy — real, needs a multi-page list and a close inside one read; the next read corrects it. Last-wins de-duplication would reorder sections.
  - `[low]` `[reject]` Blind: `openCount` and the rows are two statements without a shared snapshot — real for one projector write between them; the next read corrects it, and a transaction adds a connection hold per request.
  - `[low]` `[reject]` Blind: the index does not serve the list's sort — the index does serve the Site filter and both section predicates; the sort is over one Site's listed rows (tens). Partial indexes and a UNION are not warranted at this size.
  - `[maybe-false]` `[defer]` Blind: iOS Lot detail from Alerts shares one core state with the Garden stack — depends on SwiftUI's appear/disappear order; cannot be compiled or run here. Deferred as medium (unverified).
  - `[maybe-false]` `[defer]` Blind: the iOS spoken tab label inside `.tabItem` may never reach VoiceOver — needs a device; recorded with the iOS deferred item.
  - `[low]` `[reject]` Blind: iOS pull-to-refresh returns before the read ends and `refreshing` is unused — the same `.refreshable` pattern as the Garden overview (SitesViews.swift:261); awaiting the read needs new bridge plumbing.
  - `[low]` `[patch]` Blind: an unreadable `closedAt` turned a closed Alert into an open one — `toSummaries` now drops it, with a test.
  - `[low]` `[reject]` Blind: dead fallbacks (`AlertsCopy.quantity(null)`, unused `noticeTryAgain`, a leading `+` accepted in a cursor) — none reachable with a wrong result: the core never names a too-low/high condition without a quantity, and `+n` decodes to the same cursor.
  - `[low]` `[patch]` Blind: missing Server tests (removed Lot, retention edge, `alert.site-notified`, missing Lot row) — four tests added to `AlertsListTests`.
  - `[false]` `[reject]` Blind: the projector halts on an enum value it cannot name, unnoticed — `EveryEnumValueHasALowercaseContractName` fails the build when a value of `AlertKind`, `ThresholdSide` or `AlertCloseReason` has no name, before any grain can journal it.
  - `[low]` `[patch]` Edge: unreadable `closedAt` (same as above) — patched with it.
  - `[low]` `[reject]` Edge: the core's list is cut at 20 pages without notice — as the caps row above.
  - `[low]` `[reject]` Edge: the web list is cut at 10 pages without notice — as the caps row above.
  - `[low]` `[reject]` Edge: an unparseable `openedAt` makes the web page throw — the BFF reads only the Server, whose `ToServerTime` always writes an instant; a guard would hide a contract break.
  - `[maybe-false]` `[defer]` Edge: iOS `onDisappear` close after the other tab's open — same root as the shared Lot-detail state; deferred with it.
  - `[low]` `[reject]` Edge: a closed Alert with reason `removed` opens a removed Lot on mobile — `removed` is not produced before Epic 8, and Lot detail already shows its not-found notice.
  - `[low]` `[reject]` Edge: the same on web — as above.
  - `[low]` `[reject]` Edge: `openCount` may disagree with the rows — as the snapshot row above.
  - `[low]` `[reject]` Edge: iOS shows "No open Alerts." beside a count when a row was dropped for an unreadable time — needs a time the Server never writes.
  - `[low]` `[patch]` Edge: README claim about unknown kinds — patched (first row).
  - `[low]` `[patch]` Edge: the `listAlerts` comment claimed the list is never partial — comment and the `AlertsEngine` KDoc now say what happens at the cap.
  - `[low]` `[patch]` Gap: de-duplication across pages was pinned by no test — one case each in `alerts.test.ts` and `AlertsEngineTest` repeats an ID on page 2.
  - `[low]` `[patch]` Gap: Android pull-to-refresh on Alerts had no test — pull-down test added to `AlertsScreenTest`.
  - `[medium]` `[defer]` Gap: foreground refresh of the Alerts count is unverified on Android — `MainActivity` builds the core inline (pre-existing); deferred.
  - `[medium]` `[patch]` Gap: iOS Alerts wiring has no executing test — spy test for `AlertsActions` added (macOS only); `CoreAlertsService` and the scene-phase refresh deferred, the App target has no test target.
  - `[low]` `[patch]` Gap: the same-Site branch of `AlertsEngine.follow()` was untested — rename test added.
  - `[low]` `[patch]` Gap (other): README claim — patched (first row).
  - `[low]` `[patch]` Gap (other): unknown quantity differs between web and mobile — patched (second row).
  - `[low]` `[reject]` Intent: web, Android and the core never meet the real Server — the repo's test layering since Epic 1; the OpenAPI document is the bridge, and Story 6.6 adds the end-to-end run against the AppHost.
  - `[medium]` `[defer]` Intent: the iOS surface is the least exercised — pre-existing host limit (DW-20); deferred with the iOS item.
  - `[false]` `[reject]` Intent: Health Alerts exist only on the clients — the epic gives their production to Epic 7; the story asks for both groups to be covered, which fixtures do.
  - `[false]` `[reject]` Intent: "a row opens Lot detail" is narrowed for silent and battery rows — the story's own criterion names UX-DR26, which sends those rows to the Devices list.
  - `[medium]` `[defer]` Intent: the tab count's foreground refresh is wired but not asserted — same as the foreground gap; deferred with it.
  - `[low]` `[patch]` Intent: mobile pull-to-refresh is wired but not asserted — Android test added; iOS covered by the spy test.
  - `[false]` `[reject]` Intent: the row label carries no value or Threshold — the epic asks for the Lot and the side; the Alert events carry no value (DW-88).
  - `[false]` `[reject]` Intent: `ThresholdsPresentationTests.swift` is edited outside the story — it did not compile on Linux since Story 5.4, which stopped every Swift test there; the two guarded checks still run on macOS.
  - `[false]` `[reject]` Intent: spec not finalised and nothing committed — review runs before finalisation.

## Design Notes

- **One list, one cursor.** Open then closed in a single ordered list keeps every client to one call chain; `openCount` on each page gives the tab count without loading everything. The cursor carries the section (open/closed), the timestamp and the ID.
- **Lot name by join.** The query inner-joins `lots`, so a row is briefly absent only while `lots` is being rebuilt. The Lot is the one named at open (DW-85).
- **Row copy without numbers.** Alert events carry no value or Threshold; "<Lot> temperature too low" names the Lot and the side, which is what the epic requires. Numbers arrive with the notification payloads in 6.4.
- **Health rows are client-only here.** The variant, group and tap routing are built and tested from fixture data so Epic 7 only has to produce the Alerts. Titles: "Node on Lot '<Lot>' silent", "Node battery low on Lot '<Lot>'", "Soil sensor on Lot '<Lot>' needs calibration".
- **Lot detail from a row.** Android switches to the Garden tab and opens Lot detail there (Back returns to the overview); iOS pushes the existing Lot detail view on the Alerts tab's own stack. Web links to `/garden/{lotId}`.
- **iOS "snapshots".** As in 4.7: presentation tests per variant run in the `swift:6.3.3` container; render tests in light and dark and the App target build only in macOS CI.

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror && dotnet format --verify-no-changes --no-restore` -- expected: success, no changes
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- expected: pass (known host flakes: `KeycloakReconciliationTests.ARenameInKeycloakRenamesTheSite`, three `NodeMoveTests` reminder timeouts)
- `pnpm --filter @coldframe/api-client generate && pnpm -r typecheck && pnpm -r lint && pnpm -r test && pnpm --filter @coldframe/openapi run check` -- expected: pass, no drift; new `alerts-*.png` screenshots recorded with `--update-snapshots` and committed
- `ANDROID_HOME=$HOME/Android/Sdk ./gradlew ktlintFormat check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64 --offline` -- expected: pass after `:android:recordRoborazziDebug` recorded the new baselines
- `podman run --rm --security-opt label=disable -v "$PWD":/work -w /work swift:6.3.3 sh -c 'swift build && swift test && swift format lint --strict -r .'` -- expected: pass

**Manual checks (if no CLI):**
- The macOS `swift` and `ios` CI jobs are the only compile check of the SwiftUI views and `apps/swift/ios/App`; read their result on the pull request.

## Auto Run Result

Status: done

**Summary:** The Server now builds an `alerts` read model from the Alert streams and serves it at `GET /sites/{siteId}/alerts` (Member, cursor-paginated, with `openCount`): open Alerts newest first, then Alerts closed in the last 7 days. The web app, the shared Kotlin core, Android and iOS read it and show the Alerts surface: Threshold Alerts, Health Alerts, then Closed, with the four row variants, "No open Alerts." when none is open, and "Alerts · N" on the mobile tab.

**Files changed:**
- `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md`, `packages/ts/api-client/src/{schema,index}.ts` -- `listAlerts` and its schemas; regenerated client.
- `apps/cs/migrations/Migrations/M20261009093000CreateTableAlerts.cs` -- table `alerts`.
- `apps/cs/server/Alerts/{AlertsProjector,AlertsReadModel,AlertNames,AlertsHostingExtensions}.cs`, `apps/cs/server/Edge/EdgeApi.cs` -- projector, keyset query, count, cursor, endpoint.
- `apps/ts/web/src/lib/{alerts.ts,server/alerts.ts,components/AlertRows.svelte,components/Icon.svelte,i18n/en.json}`, `routes/(app)/alerts/*` -- row model, loader, rows, page.
- `packages/kt/core/src/**/{alerts,api,sites,signin}/**` -- `AlertsApi`, DTOs, `AlertsEngine`, snapshot, `IosAlerts`, wiring.
- `apps/kt/android/src/main/**` -- `ui/alerts/*`, tab label with count, row routing, foreground refresh, strings.
- `apps/swift/ios/**` -- `AlertsPresentation.swift`, `UI/AlertsViews.swift`, `Screens.swift`, `App/CoreAlertsService.swift`, catalogue.
- `tests/**` -- Server unit and integration tests, web unit and Playwright tests with light/dark screenshots, core and Android tests with Roborazzi baselines, Swift presentation and render tests; `ThresholdsPresentationTests.swift` guarded so the Swift tests compile on Linux again.
- READMEs of `apps/cs`, `apps/ts/web`, `apps/kt`, `apps/swift`, `packages/kt`, `packages/kt/core`.

**Review findings:** 41 findings from four layers. Patched 14 rows (9 distinct fixes: 1 medium, 8 low). Deferred 7 rows into 4 items (iOS shared Lot-detail state, `quantity` required for Epic 7 Health Alerts, foreground refresh and App wiring untested, iOS views unverified). Rejected 20: 14 low (page caps, close-between-pages race, count snapshot, index, iOS refresh end, dead fallbacks, unparseable times, removed-Lot target, test layering) and 6 false; each reason is in the Review Triage Log.

**Follow-up review recommendation:** false. Patched by verdict: high 0, medium 1, low 8.

**Verification:**
- `dotnet build -warnaserror`, `dotnet format --verify-no-changes` -- pass; `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- 1023 passed, 0 failed.
- `pnpm` generate, typecheck, lint, test, openapi check -- pass: web unit 565, Playwright 88, no schema drift.
- `./gradlew ktlintFormat check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64 --offline` -- BUILD SUCCESSFUL, Roborazzi baselines verified.
- `swift build && swift test && swift format lint --strict -r .` in `swift:6.3.3` -- 285 tests passed, lint clean.
- Every row of the I/O matrix has a covering test that ran and passed.

**Residual risks:**
- The SwiftUI Alerts views, the `Screens.swift` edits, `apps/swift/ios/App` and the iOS render and spy tests are first compiled by the macOS `swift` and `ios` CI jobs; read their result on the pull request.
- No device, emulator or screen-reader run on any platform.
- The row names the Lot the Alert opened for (DW-85) and uses the Server's open time (DW-88).
