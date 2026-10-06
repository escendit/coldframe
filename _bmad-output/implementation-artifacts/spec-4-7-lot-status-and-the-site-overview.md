---
title: 'Story 4.7: Lot status and the Site overview'
type: 'feature'
created: '2026-10-06'
baseline_revision: '035f4dbc12a896f835ee792292c938d8c306df53'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/DESIGN.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/EXPERIENCE.md'
  - '{project-root}/docs/quickstart.md'
warnings: ['oversized']
deferred:
  - summary: >-
      The iOS App target's mapping from the core's Lots snapshot to LotsPresentation, and its foreground refresh, are compiled in CI but never executed by a test.
    evidence: |-
      CoreLotsService.presentation(of:) maps about 28 parallel snapshot fields by hand. The CI ios job runs xcodebuild build only; the swift job tests the package, where Support.swift hand-builds a LotsPresentation, and OverviewPresentationTests assert on the source text of CoreLotsService.swift and ColdframeApp.swift. Swapping two same-typed arguments (for example lotValues and lotFoots) compiles and passes. The App target has no test bundle and needs the Kotlin framework; close this when one exists, or move the mapping into the tested package behind a protocol. The same holds for the Android activity: MainActivity's foreground refresh is checked by a regex on its source, because there is no seam to inject a fake core.
    location: >-
      apps/swift/ios/App/CoreLotsService.swift:26-85, apps/swift/ios/App/ColdframeApp.swift:42, apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/MainActivity.kt:79-84
    severity: medium
---

<intent-contract>

## Intent

**Problem:** A Lot's status is only `noNode` or `unknown`, the `Lot` contract carries nothing else, and the three clients draw only the no-Node tile under a fixed "No Readings yet" header. A Node that reports is still shown as unknown, and when the Server can't be reached the overview shows a notice or old data without saying how old it is.

**Approach:** Compute LotStatus once on the Server (AD-14) with one pure precedence rule fed by the Lot, Device and Sensor events, and return it with its supporting fields on the existing Lots endpoints. Render all six tile variants, the headline and the counts on web, Android and iOS from those fields only, and add client stale mode (stale header, stale tiles, skeleton, refresh) around the last good data.

## Boundaries & Constraints

**Always:**
- AD-14, AD-1, AD-21, AD-22 as written in `ARCHITECTURE-SPINE.md`. Precedence `noNode > paused > unknown > needsCalibration > needsWater > ok`; order `needsWater, needsCalibration, unknown, ok, paused, noNode`, then creation order.
- One pure Server function maps the inputs (has Node, pause sources, silence cause, uncalibrated soil Sensor, open low-side Alert) to status, `unknownCause` and `pausedBy`. The projector is the only writer of the read model and is idempotent and rebuildable from position 0. Rows that exist before the migration end up correct.
- Live inputs in this story: Node on the Lot (`lot.claimed`/`lot.released`), pause sources (`device.paused`/`device.resumed`), and "uncalibrated soil Sensor" = a Sensor in the Device's declared list (`device.specifications-declared`) whose Specification is `soil_moisture` with `calibration: true` (no Calibration exists before Epic 5). A Lot whose Node has declared no Sensor yet stays `unknown` with `unknownCause: node`, as today. Silence and the low-side Alert have no producer: the rule takes them, the projector passes "none".
- `statusSince` is stored when the status changes (event time; journal `RecordedAt` for Lot events). `lastReadingAt` is the newest `measured_at` of the Node's Readings since it claimed the Lot.
- Contract first: `Lot` gains required `statusSince` and optional `lastReadingAt`, `unknownCause`, `pausedBy`, `pausedUntil`, `moisturePercent`, `lowThresholdPercent` (additive; absent when not applicable). The Server never sends the last two before Epics 5 and 6. TS schema regenerated, Kotlin `LotDto` mirrored.
- Clients render only: no status computed or re-sorted. They may count statuses for the headline and compute durations from Server timestamps and their clock.
- Stale mode is transport-only (EXPERIENCE.md State Patterns; no numeric threshold exists): entered when a refresh and one retry fail, or on a mobile cold start with cached Lots until the first refresh lands; left on the first successful refresh. "As of" and "Last data" show the time of the last successful refresh.
- Every UX-DR named in the story has a test whose name starts with its ID, written before the code, and is added to the coverage lists. Copy only from catalogues; Android and iOS keys and values identical; tokens only; no animation.

**Never:**
- No Lot detail, Sensor cells, history, Devices "Nodes" section (Story 4.8); no move or unassign (4.9); no Calibration, Thresholds or Alerts (Epics 5–7); no Pause commands or expiry (Epic 8).
- No SignalR, AsyncAPI or `readmodel.changed` (Story 6.6); no Open Alerts rail; no Site clock; no polling timer.
- No new endpoint, no client-side status or sort logic, no stale mode on Devices or Site settings.
- No new tile tap targets: only the existing mobile no-Node → Add a Node entry stays.
- No third-party snapshot library on iOS. No skeleton on the web (its first paint is server-rendered).
- Do not write `sprint-status.yaml`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Every combination | All 32 combinations of the five rule inputs | Exactly one status by precedence; `unknownCause` only for `unknown`; `pausedBy` only for `paused` | — |
| No Node | Lot created or released | `noNode`, `statusSince` = that event's time | — |
| Node, nothing declared | `lot.claimed`, no declaration for that Node (also a Node ID with no Device stream) | `unknown`, `unknownCause: node`, no `lastReadingAt` | — |
| Node reports | Declaration with an uncalibrated soil Sensor; Readings stored | `needsCalibration`; `statusSince` = declaration time; `lastReadingAt` = newest `measured_at`; no `moisturePercent` | — |
| No calibrating Sensor | Declared set without `calibration: true` soil moisture | `ok` | — |
| Paused | `device.paused` (device and/or site, with or without end) then `device.resumed` | `paused` with `pausedBy`; `pausedUntil` = latest end, absent if any source is open-ended; previous status back after the last resume | — |
| Events repeated / out of stream order | Same event applied twice; `sensor.declared` before the Device event | Same rows; `statusSince` unchanged | — |
| List order | One Lot per status (rows seeded for `needsWater`/`unknown` hub) | Server order; every field mapped; absent optionals omitted | — |
| Tile variants | Fixture Lots of all six statuses, both `unknownCause`, `pausedBy` with and without site/end | Shape, icon, label, value, foot and spoken label of Design Notes | Unknown status string renders as `unknown` |
| Headline | Status mixes of Design Notes | Headline and counts subline of Design Notes | — |
| Stale | Lots shown, then refresh + retry fail | Stale header with ticking age, every tile stale, Site menu items disabled with "Needs your Server", polite announcement; live again after a success | Age ticks are never announced |
| Cold start (mobile) | Cached Lots / no cache | Cached Lots in stale mode until the refresh lands / outline skeleton tiles, not focusable, "Loading ‹Site›" | No cache and unreachable: the existing notice |
| Large text / narrow | iOS ≥ AX1, Android font scale ≥ 1.5, web grid < 400 px | One column, value directly under the status label, nothing clipped | — |

</intent-contract>

## Code Map

**Server**
- `apps/cs/server/Lots/LotsProjector.cs` -- only `lot/` streams today; `NoNodeStatus`, `UnknownStatus` (claim → `unknown`). `LotsReadModel.cs:12,22-30` -- `LotView`, `StatusOrder`, `ListSql`. `LotGrain.cs:163` `CatchUpLotsAsync` (read-your-writes; `EdgeApi.FindLotAsync` `:979` throws if the row is missing).
- `apps/cs/server/Journal/IProjector.cs`, `ProjectionRunner.cs`, `JournalServiceCollectionExtensions.cs:57` -- one projector sees every stream in global order; registration in `Lots/LotsHostingExtensions.cs:14`.
- `packages/cs/contracts/Devices/DeviceEvents.cs:71-150` -- `DevicePauseSource`, `DevicePaused(Source, EndsAt?, PausedAt)`, `DeviceResumed`, `DeviceSpecificationsDeclared(SpecHash, Sensors[Slot, Quantity, SensorId], DeclaredAt)`. `Sensors/SensorEvents.cs:16,32` -- `SensorDeclared(DeviceId, Slot, Specification, DeclaredAt)`, `SensorSpecificationChanged`. `Lots/LotEvents.cs:29-41` -- `LotClaimed(NodeId)` (live value = Device ID), `LotReleased`; no timestamps.
- `apps/cs/server/Edge/EdgeApi.cs:65-71` `LotResponse`, `LotListResponse`; `:382` `ListLotsAsync`; `:434` `GetLotAsync`; `:971` `ToLotResponse`; `:816` `ToDeviceListItem` (read-time `TimeProvider` precedent); `:186` `ServerTimeFormat`.
- `apps/cs/migrations/Migrations/M20260928140000CreateTableLots.cs`, `M20261006150000CreateTablesReadings.cs` (`readings.device_id`, `measured_at`, `ix_readings_device_id_measured_at`) -- naming `M<yyyyMMddHHmmss><What>`, forward only (`MigrationSetTests`).
- Tests: `tests/cs/server.tests/Devices/DeviceLivenessTests.cs` (pure-function model), `Lots/LotStateTests.cs:38-47` (pins `StatusOrder`, `ToLotResponse`), `Fixtures/journal.json`; `tests/cs/server.integration/Edge/LotsTests.cs:37,164`, `Devices/DevicesListTests.cs` (`AppendAsync`, `WaitForProjectionCheckpointAsync`, seeded timestamps; the AppHost runs on the real clock), `Edge/EdgeApiFixture.cs`.
- Contract: `packages/openapi/coldframe.openapi.json:255-275,499-514`, `packages/openapi/README.md`; `packages/proto/check-compat.sh` (oasdiff).

**Web** (`apps/ts/web`, SvelteKit BFF, server loads only)
- `src/lib/server/lots.ts`, `sites.ts` (`call<T>`), `shell.ts` (`loadShell`), `site-settings.ts` (`loadLots`); `src/routes/(app)/+layout.svelte` (blanks the page when `GET /sites` fails); `routes/(app)/garden/+page.server.ts`, `+page.svelte`.
- `src/lib/components/LotTiles.svelte` (grid, container queries 400/672/1056, only `noNode` drawn), `SiteSummaryHeader.svelte`, `Icon.svelte` (register `rain-drop`, `tools`, `pause--outline`, `cloud--offline`, `time`), `LiveRegions.svelte`, `src/lib/announcer.svelte.ts`, `src/lib/site-menu.ts` (`stale` option unused), `src/lib/i18n/en.json`, `format.ts`.
- `packages/ts/api-client` -- `pnpm --filter @coldframe/api-client generate`; stale check in `tests/ts/api-client/index.test.ts`.
- Tests: `tests/ts/web/lots.test.ts:318-356`, `garden.test.ts`, `coverage.test.ts`, `copy.test.ts:32-37` (bans `OK`), `styles.test.ts:64-84` (no colour literals, no `gradient(`); `tests/ts/web.e2e/specs/site-settings.spec.ts:34-88`, `garden.spec.ts`, `helpers.ts`, `fixtures/fake-idp.ts:46-52,307-344` (`FakeLot`, `lotView`; no way to fail Sites/Lots after a success).

**Kotlin** (`packages/kt/core`, `apps/kt/android`)
- `core/api/ApiDtos.kt:36-49` `LotDto`; `core/lots/Lots.kt` (`LotStatus`, `LotSummary`, `LotsState`), `LotsEngine.kt:110-151` (`load`, `fetch`, `refresh` swallows failures; no clock, no cache), `LotsSnapshot.kt` + `iosMain/.../IosLots.kt` (flat parallel lists for Swift); `core/sites/SiteMenu.kt:26` (`stale` unused), `SitesWiring.kt:43`; key-value persistence pattern in `core/appearance/AppearanceStore.kt`.
- Android: `ui/sites/LotTile.kt:45-166`, `GardenScreen.kt:62-284`, `ColdframeRoot.kt:52-70` (`now` seam), `MainActivity.kt:63`, `ui/theme/ColdframeTheme.kt:20-88`, `ui/setup/WifiNetworkRow.kt:120-140` (hatch + plate to extract), `ui/components/DottedBorder.kt`, `ui/format/Formats.kt`, `res/values/strings.xml`.
- Tests: `tests/kt/core/jvmTest/.../api/OpenApiContractTest.kt:177-191`; `tests/kt/android/test/.../SnapshotTest.kt` (Roborazzi, baselines in `tests/kt/android/snapshots`), `SitesScreensTest.kt:277`, `AccessibilityTest.kt`, `StringsTest.kt:78-137`, `CoverageTest.kt:9-115`, `SourceScanTest.kt`.

**iOS** (`apps/swift/ios`; views and `App/` compile only on macOS CI)
- `Sources/ColdframeIOS/LotsPresentation.swift:5-62,228-321`, `SitesPresentation.swift:231-270` (`GardenPresentation`), `UI/SitesViews.swift:196-284`, `UI/SiteSettingsViews.swift:220-268` (`LotGrid`, `LotTile`), `UI/HubSetupViews.swift:636-661` (`Hatch`, `plate`), `Formats.swift`, `L10n.swift`, `Resources/Localizable.xcstrings`, `App/CoreLotsService.swift:16-41`, `App/ColdframeApp.swift:38`.
- Tests: `tests/swift/ios/ColdframeIOSTests/LotsPresentationTests.swift:40-120`, `SitesPresentationTests.swift`, `RenderTests.swift:7-15`, `CatalogueTests.swift`.

## Tasks & Acceptance

**Execution:**
- [ ] `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md`, `packages/ts/api-client/src/schema.ts` -- add the `Lot` fields of the Always section; regenerate the TS schema -- the contract (AD-10, AD-14).
- [ ] `tests/cs/server.tests/Lots/LotStatusRuleTests.cs`, `apps/cs/server/Lots/LotStatusRule.cs` -- test first: all 32 input combinations, then the pure rule -- the precedence in one place.
- [ ] `apps/cs/migrations/Migrations/M<now>*.cs` -- `lots` gains status-since, claim time, pause and cause columns; projector-owned support tables for Devices and Sensors; existing rows rebuilt or backfilled -- storage (AD-22).
- [ ] `apps/cs/server/Lots/LotsProjector.cs`, `LotsReadModel.cs`, `Edge/EdgeApi.cs` -- consume the Device and Sensor events, recompute affected Lots through the rule, keep `statusSince` stable; read `lastReadingAt` with the list and single-Lot queries; map the new response fields -- the projection and its surface.
- [ ] `tests/cs/server.integration/Edge/LotStatusTests.cs`, `LotsTests.cs`, `tests/cs/server.tests/Lots/LotStateTests.cs` -- AppHost tests for the matrix rows "No Node" to "List order" plus one through the Device simulator (enrol, assign, declare, report → `needsCalibration` with `lastReadingAt`); adjust existing assertions the new rule changes -- acceptance at the REST surface.
- [ ] `packages/kt/core/.../api/ApiDtos.kt`, `lots/Lots.kt`, `LotsEngine.kt`, `LotsSnapshot.kt`, `iosMain/.../IosLots.kt`, `sites/SiteMenu.kt`, `SitesWiring.kt`, `tests/kt/core/**` -- DTO and `LotSummary` fields; engine with clock, one retry, `fetchedAt`, stale flag, persisted last-good Lots per Site, cold start from cache, enter/leave-stale events; headline and counts model shared by both shells; soil formatter (`~`, nearest 5) -- staleness and rules live in the core (AD-14).
- [ ] `apps/kt/android/.../ui/components/Hatch.kt`, `ui/sites/LotTile.kt`, `GardenScreen.kt`, `ui/theme/ColdframeTheme.kt`, `ColdframeRoot.kt`, `MainActivity.kt`, `res/values/strings.xml`, `tests/kt/android/**` -- reusable hatch + plate (Wi-Fi row uses it), six variants + stale + skeleton, header and stale header with a minute tick, pull-to-refresh, reload on start, announcements, disabled Site menu; Roborazzi snapshots of every variant in light and dark at normal size (2 columns) and font scale 2 (1 column) -- Android overview.
- [ ] `apps/swift/ios/Sources/ColdframeIOS/LotsPresentation.swift`, `SitesPresentation.swift`, `UI/Hatch.swift`, `UI/SiteSettingsViews.swift`, `UI/SitesViews.swift`, `L10n.swift`, `Resources/Localizable.xcstrings`, `App/CoreLotsService.swift`, `App/ColdframeApp.swift`, `tests/swift/ios/**` -- the same on iOS: presentation structs carry every rule and are tested per variant on Linux; views stay thin; `.refreshable`; render tests per variant in light and dark -- iOS overview.
- [ ] `apps/ts/web/src/lib/server/lots.ts`, `shell.ts`, `src/lib/server/last-good.ts`, `src/lib/lot-tiles.ts`, `garden.ts`, `components/LotTiles.svelte`, `Hatch.svelte`, `SiteSummaryHeader.svelte`, `StaleHeader.svelte`, `Icon.svelte`, `routes/(app)/+layout.svelte`, `garden/+page.server.ts`, `+page.svelte`, `site-menu.ts`, `i18n/en.json`, `tests/ts/web/**`, `tests/ts/web.e2e/**` -- BFF keeps the last good Sites and Lots per user in memory and serves them as stale after one failed retry; tile view models, headline, variants, stale header (age ticks in the browser), refetch on focus, announcements; a fake-Server switch that fails reads after a success; Playwright snapshots of every variant in light and dark at 1, 2, 3 and 4 columns, with axe -- web overview.
- [ ] `tests/ts/web/coverage.test.ts`, `copy.test.ts`, `tests/kt/android/.../CoverageTest.kt`, `StringsTest.kt`, `tests/swift/ios/.../CatalogueTests.swift` -- add the story's UX-DR IDs; allow `OK` only in the keys that name the `ok` status -- guards.
- [ ] `apps/cs/README.md`, `apps/ts/web/README.md`, `packages/kt/core/README.md`, `_bmad-output/implementation-artifacts/deferred-work.md` -- "Lot status" section (rule, live vs. fixture-only inputs, `lastReadingAt`); stale mode per client; mark DW-24 (stale part), DW-28 and DW-32 resolved -- docs.

**Acceptance Criteria:**
- Given a Site with one Lot per status, when `GET /sites/{siteId}/lots` is called, then the Lots come in Server order with `statusSince` on each and the supporting fields only where they apply, and a Member may read it.
- Given a Node enrolled on a Lot through the Device simulator, when it declares its Sensors and reports, then the Lot is `needsCalibration` with `lastReadingAt` and no percentage on any client.
- Given fixture Lots of every status, when the overview renders on web, Android and iOS in light and dark, then each tile shows the shape, Carbon icon, label, value and foot of its variant in the given order, with one complete spoken label, and the snapshot or render tests cover each.
- Given the Server stops answering, when the overview refreshes, then after one retry the stale header and stale tiles show "as of ‹time›" and no value, and the first successful refresh restores the live view.
- Given the CI commands of Verification, when they run, then all pass.

## Spec Change Log

## Review Triage Log

### 2026-10-06 — Review pass
- verdicts: 44 findings — high 0, medium 6, low 17, false 21, maybe-false 0
- findings:
  - `[low]` `[patch]` (blind) Kept data can survive a sign-out on mobile: the clear and the session bump ran from two collectors — `SitesEngine.forget()` now bumps `session` with the clear and the Lots listener bumps `generation`; three tests with a read in flight.
  - `[low]` `[patch]` (blind) A 403/404 on a refresh over live Lots left them on screen without a notice — `LotsEngine.refused` now always goes to `Failed` with the load notice; test renamed and extended.
  - `[low]` `[reject]` (blind) Web: stale header above the unreachable notice when the Sites are kept and the Lots are not — real but cosmetic: the Site data is stale and the tiles are unavailable, both true; the alternative (summary header saying "No Readings yet") is worse.
  - `[low]` `[reject]` (blind) Web stale mode ends only on a refocus; no background retry — the intent contract forbids a polling timer; staying stale is the safe direction and the next focus or navigation ends it. Listed under residual risks (UX-DR79 says "retries in background").
  - `[false]` `[reject]` (blind) `lastReadingAt` compares a Node clock with the Server's — an unsynced Reading is rebased by the Server before it is stored (`DeviceIngestionStore`), a synced clock is set from Server downlinks, and a Node is enrolled and assigned together, so no Reading predates the claim.
  - `[low]` `[reject]` (blind) A Pause with an end never ends in the projection — nothing journals `device.paused` before Epic 8, which owns Pause commands and expiry (intent: "no Pause commands or expiry").
  - `[low]` `[reject]` (blind) The Lot list is empty while the projector rebuilds after the migration — a one-time window of seconds on upgrade that heals itself; a readiness gate is new surface. The rebuild itself is now tested (see the migration row).
  - `[false]` `[reject]` (blind) A Node that declares an empty Sensor set reads `ok` — `NodeFrameReader` rejects a set of 0 Specifications (Story 4.6), so an empty declaration is never journaled.
  - `[false]` `[reject]` (blind) `status_since` mixes two clocks — both are the Server's clock (grain `TimeProvider` and journal `recorded_at`); a Device event before the claim evaluates no Lot.
  - `[low]` `[reject]` (blind) `unknownCause` can change without `statusSince` moving — unreachable before Epic 7 produces `hub`; that story decides the duration's source.
  - `[low]` `[patch]` (blind) Web and Kotlin tile models disagree on edge cases — the spoken low Threshold is now rounded on the web. The other two were not changed: `statusSince` is required by the contract, and a moisture of 3e9 is not a value the Server can send.
  - `[false]` `[reject]` (blind) An unrecognised status is shown as a silent Node — the intent's matrix says an unknown status string renders as `unknown`.
  - `[low]` `[reject]` (blind) Overlapping mobile refreshes are not coalesced — each costs one small request and the newest answer wins; coalescing adds state for no shown harm.
  - `[low]` `[reject]` (blind) The web last-good store is per process and not cleared on sign-out — entries are keyed by the user's subject, bounded and dropped on a 401; no other user can read them. One BFF instance is the deployment.
  - `[low]` `[reject]` (blind) Mobile Site settings shows kept Lots with working forms on a cold start — the first read lands within moments, and an action against an unreachable Server shows the existing failure notice; a stale state there is new UI. Listed under residual risks.
  - `[low]` `[patch]` (blind) Doc and default nits — the `SitesEngine` KDoc now says the listeners are run, not cleared. The default of `fetchedAtEpochMs` and the double `removed` filter were left.
  - `[low]` `[reject]` (edge) A timed Pause never expires — same claim as the blind finding.
  - `[false]` `[reject]` (edge) `lastReadingAt` null or in the future — same claim as the blind finding; a `measured_at` more than 5 min ahead is rejected at ingestion.
  - `[low]` `[reject]` (edge) Empty list served while the projector replays — same claim as the blind finding.
  - `[low]` `[reject]` (edge) Kept Lots of a Site the user lost stay on the device until sign-out — the Site is no longer listed, so nothing shows them.
  - `[low]` `[patch]` (edge) A Lot paused by the Site with an end is spoken differently on web and mobile — the web now says "paused until ‹date›" as mobile does; the unused key is deleted.
  - `[medium]` `[patch]` (edge) Web: a focus while the browser cannot reach the web app replaced the overview with SvelteKit's error page — the page now checks that the web app answers (one retry); on two network errors it keeps the Lots and enters stale mode "as of" the last good load, and leaves it on the next focus that gets through. Unit tests and one Playwright case.
  - `[false]` `[reject]` (edge) Unknown Lot without `statusSince` shows "0 min" on the web — `statusSince` is required by the contract and always sent.
  - `[low]` `[patch]` (edge) Fractional low Threshold spoken unrounded — same defect as the blind row; fixed with it.
  - `[low]` `[reject]` (edge) A kept empty Sites list served stale redirects to Create Site — needs a user without any Site and a Server failing twice; Create Site then shows its own failure notice.
  - `[false]` `[reject]` (edge) A failed re-read after a Lot change now flips the list to stale — that is stale mode as specified: a refresh and one retry failed.
  - `[medium]` `[patch]` (gap) `sensor.specification-changed` never re-evaluates a Lot under test — added a test: `needsCalibration` → `ok` → back, `statusSince` = `ChangedAt`.
  - `[medium]` `[patch]` (gap) A re-declared set that differs, and a declaration after a Pause row, were untested — added both tests.
  - `[medium]` `[patch]` (gap) The migration only ran on empty databases — added a test that migrates to 20261006150000, seeds a `lots` row and its checkpoint, migrates to the end and asserts the rebuild preconditions.
  - `[low]` `[patch]` (gap) Android refresh wiring is checked by source text only — added `LotsActionsTest` for `LotsActions.of(engine).refresh`; the activity-level part is in `deferred`.
  - `[medium]` `[defer]` (gap) iOS: the snapshot-to-presentation mapping and foreground refresh in the App target are never run — no test bundle exists for the App target; recorded in `deferred`.
  - `[low]` `[patch]` (gap) The Site menu being disabled when only the Lots are stale was not asserted — the `failReads('lots')` Playwright step now opens the menu and asserts it.
  - `[low]` `[patch]` (gap) Create Site staying open when live Sites replace kept ones was untested — added the case to `SitesEngineTest`.
  - `[low]` `[reject]` (intent) No age-based stale trigger: a screen left open with a reachable Server stays live — the planning documents define no stale threshold; EXPERIENCE.md and UX-DR79 define stale mode by transport failure, and AD-14 gives data that is old on the Server to the `unknown` status. Every Reading shown carries its time. Listed under residual risks for a human decision.
  - `[medium]` `[patch]` (intent) Web stale mode covered only the BFF-to-Server hop — same defect as the edge row on focus without the web app; fixed with it. A full reload away from home still shows the browser's own error.
  - `[false]` `[reject]` (intent) AC1 is split across layers and two statuses are seeded by `UPDATE` — the story makes `unknown`, `paused` and `needsWater` fixture rows; no producer exists for a low-side Alert or a silent Hub.
  - `[false]` `[reject]` (intent) `needsCalibration` uses the Specification flag as a stand-in — no Calibration exists before Epic 5; the story says calibrating soil Sensors are uncalibrated at this point.
  - `[false]` `[reject]` (intent) `unknown` is live now — a claimed Lot was `unknown` before this story; it stays so until its Node declares.
  - `[false]` `[reject]` (intent) No test crosses the real Server to a client — the Server is tested at REST, the TS types are generated from the contract and the Kotlin DTO is held to it by `OpenApiContractTest`; no bad outcome is named.
  - `[low]` `[reject]` (intent) iOS has no pixel baselines — none can be recorded on Linux and the intent excludes a third-party library; presentation tests per variant plus render tests in light and dark are the repo's pattern. Listed under residual risks.
  - `[false]` `[reject]` (intent) Cited UX-DRs are implemented only for the overview — the epics give Lot detail to 4.8, SignalR to 6.6, Alerts and the rail to Epic 6; DW-31 stays open for tile roles.
  - `[low]` `[reject]` (intent) "Written failing before implementation" is not observable, and was not strictly followed in the core and iOS — cannot be repaired after the fact; recorded under deviations.
  - `[false]` `[reject]` (intent) Orchestrator constraints — reported as met.
  - `[low]` `[reject]` (intent) The migration deletes `lots` and its checkpoint — same claim as the blind finding on the rebuild window.

### 2026-10-06 — Review pass (follow-up)
- verdicts: 32 findings — high 0, medium 0, low 15, false 17, maybe-false 0
- findings:
  - `[low]` `[reject]` (blind) carried: a Pause with an end is never evaluated against time — same claim as the first pass; Epic 8 owns Pause commands and expiry.
  - `[false]` `[reject]` (blind) carried: `status_since` mixes clocks — same claim as the first pass; the code at `LotsProjector.StoreStatusSql` is unchanged.
  - `[false]` `[reject]` (blind) carried: `lastReadingAt` compares a Node clock with the Server's — same claim as the first pass; ingestion rebases unsynced Readings.
  - `[low]` `[reject]` (blind) carried: the migration empties the live read model — same one-time rebuild window as the first pass.
  - `[low]` `[reject]` (blind) No test compares a full rebuild with incremental projection, and a rebuild does not reset the support tables — status stays correct (inputs are final state) and only `statusSince` of a replayed Lot could read earlier; a reset hook is new surface and the migration test covers the one rebuild that exists.
  - `[low]` `[reject]` (blind) `EvaluateAsync` rewrites unchanged rows and builds its filter by concatenation — the filter strings are two literals in this file, and the extra writes are one row per event; a `IS DISTINCT FROM` guard adds a branch for no shown harm.
  - `[low]` `[reject]` (blind) carried: the web last-good store is per process and keyed by user — same claim as the first pass.
  - `[low]` `[reject]` (blind) carried in substance: device caches are plaintext and not per user — cleared on sign-out and on a 401 (first pass); the core README states the limit.
  - `[low]` `[reject]` (blind) Web tile durations do not tick while the header does — values are minutes or hours and the next focus reloads them; a tick would be a render timer, not a data timer, but it is no defect anyone meets in use.
  - `[false]` `[reject]` (blind) carried: an unknown future status renders as a silent Node — the intent matrix says it renders as `unknown`.
  - `[low]` `[reject]` (blind) Clients tolerate a missing `statusSince` — deliberate for a Server one release behind; the contract still requires it and the Server always sends it.
  - `[low]` `[reject]` (blind) Retry without backoff, probe path hard-coded, any HTTP answer counts as reachable — one immediate retry is what the intent specifies; the probe path is the app's own version file; a proxy 5xx then shows the normal error page, as before this story.
  - `[low]` `[reject]` (blind) Web tile values `—` and `+` are literals while Android uses a resource — glyphs, not words; nothing to translate.
  - `[false]` `[reject]` (edge) carried: device-supplied event times in `status_since` — same claim as the first pass's clock rows.
  - `[false]` `[reject]` (edge) carried: an empty Specification set reads `ok` — `NodeFrameReader` rejects an empty set.
  - `[low]` `[reject]` (edge) A Sensor re-declared under another Device leaves the old Device's Lot unevaluated — needs a Sensor ID reused across Devices; Sensor IDs are made per Device at declaration.
  - `[low]` `[reject]` (edge) carried: a past Pause end never expires — Epic 8.
  - `[false]` `[reject]` (edge) An unknown pause source is written as a Device pause — `DevicePauseSource` has no other value than Device and Site.
  - `[false]` `[reject]` (edge) carried: `measured_at` against `claimed_at` — same claim as the first pass.
  - `[false]` `[reject]` (edge) The migration leaves support tables non-empty — it creates `lot_status_devices` and `lot_status_sensors` itself, so they are empty when the projector replays.
  - `[low]` `[reject]` (edge) A moisture of 3e9 overflows the rounding on the core — the Server never sends `moisturePercent` before Epic 5; carried from the first pass.
  - `[low]` `[reject]` (edge) A low Threshold outside 0..100 is shown unclamped — the Server never sends it before Epic 6.
  - `[low]` `[reject]` (edge) `LotsEngine.missed` drops kept Lots on any non-transport failure — `transport` already covers no answer, 5xx and unexpected answers; what is left is a 4xx about the caller, which the KDoc defines as refused.
  - `[false]` `[reject]` (edge) The focus handler clears the unreachable marker when `invalidateAll` fails — a failed load shows the page's own error state, which replaces the overview; the marker has no meaning there.
  - `[false]` `[reject]` (edge) A 401 on a Lots read leaves Sites `Ready` — `SitesEngine` handles its own 401 with the same `forget()`, and the sign-in engine ends the session.
  - `[false]` `[reject]` (edge) claim: a Node that has declared no Sensor is `unknown` — the rule gets `LotSilence.Node` for `hasNode && !hasDeclared`; the claim holds.
  - `[false]` `[reject]` (edge) claim: `status_since` is reproducible on rebuild — see the rebuild row above; the migration rebuilds from empty support tables.
  - `[false]` `[reject]` (intent) Silence passed as `Node` for a Node that declared nothing, not "none" — recorded as a deviation in the first pass; it keeps the rule at five inputs and today's behaviour.
  - `[false]` `[reject]` (intent) `unknownCause: hub` has no producer — the intent says silence has none.
  - `[false]` `[reject]` (intent) Web stale data lives in the BFF — Design Notes decide this.
  - `[false]` `[reject]` (intent) Clients are never run against a real Server — same claim as the first pass.
  - `[false]` `[reject]` (intent) `needsWater` and `ok`-with-moisture are reachable only from fixtures — the intent gives them no producer before Epics 5 and 6.

## Design Notes

**Tile content** (label is sentence case in catalogues, uppercase by style; `‹t›` = locale clock time):

| Status | Shape / icon | Label | Value | Foot | Spoken label |
|---|---|---|---|---|---|
| needsWater | solid water fill, level edge, low tick / `rain-drop` | Needs water | `~20` | `‹t lastReadingAt› · low 30 %` | "‹Lot›, needs water, about 20 percent, low 30 percent, Reading ‹t›" |
| ok | soil fill + level edge, 1 px solid / `checkmark--outline` | OK | `~35` | `‹t› · low 25 %` (parts present only) | "‹Lot›, OK, about 35 percent, low 25 percent" |
| unknown | hatch + plate, 1 px dashed / `help` | Silent · unknown / Hub silent · unknown | duration since `lastReadingAt` (node) or `statusSince` (hub, or no Reading) | `was ~40 % at ‹t›`; without percent `last Reading ‹t›`; without Reading `no Readings yet` | "‹Lot›, unknown, Node silent for 6 hours, last about 40 percent at ‹t›" (Hub variant likewise) |
| needsCalibration | hatch + plate, 2 px dashed calibration border / `tools` | Needs calibration | `raw` | `no % until calibrated` | "‹Lot›, needs calibration, no percentage until calibrated" |
| paused | flat paused fill, 2 px solid / `pause--outline` | Paused / Paused by Site (`pausedBy` has site) | `—` | `until 1 Nov` / `paused` | "‹Lot›, paused until 1 November" / "…, paused with the Site" / "…, paused" |
| noNode | empty, 1 px dotted / `add` | No Node | `+` | `add a Node` | unchanged |
| stale (any) | empty, 1 px solid `stale-border`, no level / `cloud--offline` | Was ‹status label› | none | `as of ‹t fetch›` | "‹Lot›, was ‹status›, not live, as of ‹t›" |

**Headline** (first match): no Lot has a Node → "No Readings yet" with today's detail line; any `needsWater` → "‹Lot› needs water" / "N Lots need water"; any `unknown` or `needsCalibration` → "1 Lot can't be read" / "N Lots can't be read"; every Lot with a Node is paused by the Site → "Paused until ‹date›" when all share one end, else "Paused", in paused ink; otherwise "Nothing needs water". **Counts subline** when any Lot has a Node: non-zero counts in Server order without `needsWater`, joined by " · ": "N need calibration" (plural rule), "N unknown", "N OK", "N paused", "N without Node". First-run steps stay as they are.

**Why `unknown` before a first report.** AD-14 ties `unknown` to a Silent Alert (Epic 7). Until then a Node that has declared nothing would fall through to `ok`, which EXPERIENCE.md forbids ("Silence never looks fine") and which would change today's behaviour for claimed Lots.

**Web stale mode lives in the BFF.** The browser never calls the Server, so the last good Sites and Lots are kept in the BFF process, keyed by user and Site, bounded, and lost on restart (then the existing unreachable notice shows). This keeps the stale view server-rendered and testable. Mobile persists the last good Lots with the core's key-value `Settings`.

**Hatch on the web.** `gradient(` and colour literals are banned outside `SignInCard.svelte`; draw the hatch as an inline SVG pattern coloured by tokens.

**iOS "snapshot" tests.** The established pattern is used: presentation tests per variant (run on Linux in `swift:6.3.3`) plus render-exists tests in light and dark on macOS CI. SwiftUI and `App/` errors surface only in CI.

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror && dotnet format --verify-no-changes --no-restore` -- expected: clean
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- expected: all pass, including `LotStatusRuleTests`, `LotStatusTests`, `LotsTests`
- `packages/proto/check-compat.sh --self-test && packages/proto/check-compat.sh --base "$(git merge-base HEAD origin/main)"` -- expected: pass
- `pnpm -r typecheck && pnpm -r lint && pnpm -r test && pnpm --filter @coldframe/design-tokens run check && pnpm --filter @coldframe/openapi run check` -- expected: pass, Playwright baselines included
- `ANDROID_HOME=~/Android/Sdk ./gradlew check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64` -- expected: pass, Roborazzi verify included
- `podman run --rm --security-opt label=disable -v "$PWD":/work -w /work swift:6.3.3 sh -c 'swift build && swift test && swift format lint --strict -r .'` -- expected: pass

**Manual checks (if no CLI):**
- The macOS `swift` and `ios` CI jobs are the only compile check of the SwiftUI views and `apps/swift/ios/App`; read their result on the pull request.

## Auto Run Result

Status: done

**Summary.** Follow-up review pass on a spec that was already `done`. A Lot's status is computed once on the Server by one pure rule and returned with `statusSince`, `lastReadingAt`, `unknownCause`, `pausedBy` and `pausedUntil` on the existing Lots endpoints; web, Android and iOS draw all six tile variants, the headline and counts, the stale header and stale tiles, and refresh on focus, foreground or pull. This pass changed no code.

**Files changed.** None in this pass; the story's files are listed in the first pass's result (Server rule, projector, read model, migration `M20261006170000`, OpenAPI and generated TS schema, Kotlin core and Android, iOS views and App target, web overview and stale paths, tests). This pass added only the follow-up entry to the Review Triage Log and this section.

**Review.** Four layers ran against the diff since the baseline; 32 findings. Patches applied 0, deferred 0 (the first pass's deferral stays), rejected 32 — 17 `false`, 15 `low` — each with its reason in the Review Triage Log. Several repeat first-pass rows and were carried.

**Follow-up review: not recommended.** No patch was applied in this pass.

**Verification.** No code changed, so the first pass's verification of the final tree stands and was not re-run here (Server tests 736 passed, `pnpm -r` typecheck, lint and tests, `./gradlew check`, Swift container build and tests). The macOS `swift` and `ios` CI jobs remain the first compile of the SwiftUI views and the iOS App target.

**Residual risks.**
- No stale threshold exists in the planning documents; stale mode is transport-only and a screen left open stays as last fetched until a focus, foreground or pull. A human should decide whether a refresh interval or age limit is wanted before Story 6.6.
- iOS views and App target are unproven until macOS CI runs; no device, emulator or screen reader run on any platform.
- After the migration the Lot list is empty while the projector rebuilds.
- A Pause with an end stays `paused` after that date until Epic 8 journals the resume.
- A rebuild does not reset the support tables, so a replayed Lot's `statusSince` could read earlier than its real change; the status itself is correct.
- The web's stale data lives in the BFF process and is lost on restart.
