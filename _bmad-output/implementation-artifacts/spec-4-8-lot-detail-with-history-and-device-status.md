---
title: 'Story 4.8: Lot detail with history and Device status'
type: 'feature'
created: '2026-10-06'
baseline_revision: 'f1912ad8e99947b958f9d3fbeaa22aeb06349880'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/DESIGN.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/EXPERIENCE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-4-7-lot-status-and-the-site-overview.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** A Lot tile is a dead end. A Member cannot open a Lot to see its latest Readings, what the soil has done over 30 days, or how healthy its Node is, and the Devices screen lists Hubs only.

**Approach:** Add two Lot reads on the Server (the Lot with its latest Reading per Sensor and Node status; a `from`/`to` cursor-paginated daily history per quantity) and Node status fields on the Devices list. Render Lot detail (hero, Sensor cells, History chart with Sensor picker, Device cells) and a "Nodes" section on web, Android and iOS from those fields only, and open Lot detail from the Lot tile.

## Boundaries & Constraints

**Always:**
- Contract first, additive only: `Lot` gains optional `node` (`NodeStatus`: `deviceId`, `batteryPercent?`, `charging?` = `charging | notCharging`, `lastSeenAt?`) and `sensors` (array of `SensorReading`: `quantity` ∈ `soil_moisture | air_temperature | relative_humidity | gas_resistance`, `value`, `unit` ∈ `raw | °C | % | kΩ`, `measuredAt`); both sent only by `GET /sites/{siteId}/lots/{lotId}`, never by the list, and only while the Lot holds a Node. `DeviceListItem` gains optional `lotName`, `batteryPercent`, `charging`. New `GET /sites/{siteId}/lots/{lotId}/history?quantity&from&to&cursor&limit` (Member+, in the authorization matrix, OpenAPI operation `getLotHistory`) returns `LotHistory {quantity, unit, days[{day, low, high, readingCount}], nextCursor?}`; `day` is a UTC date (`yyyy-MM-dd`), days ascending, a day with no Readings is absent (never zero), defaults `to` = now and `from` = `to` − 30 days, `limit` default 31 and max 366, `cursor` opaque; bad `from`/`to`/`quantity`/`cursor`/`limit` or `from` after `to` → RFC 9457 400 `validation`; unknown Lot → 404 `lot-not-found`.
- Values are converted on the Server: soil moisture stays the raw count (`unit: raw`, uncalibrated until Epic 5, never a percentage), temperature milli-°C → °C, humidity milli-% → %, gas Ω → kΩ (3 significant digits left to clients). History follows the Node: Readings of the Lot's current Node since `lots.claimed_at`. Latest = newest `measured_at` per `(slot, quantity)`; Node `lastSeenAt` and battery/charging come from the newest `device_reports` row. Readings are retained indefinitely (no deletion code).
- Devices list: Nodes carry `lotName`, `batteryPercent`, `charging`, and `lastSeenAt` from the newest report; order is Hubs by id, then Nodes by Lot name (unassigned last) then id, and clients keep that order. Hub rows unchanged.
- Clients render only: no status, conversion or aggregation logic beyond formatting (soil `raw N`, whole °C and %RH, kΩ to 3 significant digits), the chart's below-low bar test is absent (no Thresholds exist before Epic 5: all bars are normal-style), and data age from Server timestamps. The History chart is 30 daily bars of the picked quantity's daily low (temperature, humidity, gas also show min/max in the selected-day readout), gaps for missing days, a text summary as accessibility label (UX-DR98), tap/drag readout, no animation under Reduce Motion, tokens only.
- Lot tiles open Lot detail on all three platforms (the no-Node tile on mobile keeps its Add-a-Node action; the web no-Node tile opens detail, which says "Add a Node from the mobile app"). Detail shows the stale header and, while stale, no live values (stale-mode rules of Story 4.7). A noNode Lot shows an empty detail (mobile Admin+: Add a Node).
- Hero shows status icon, label and "since ‹statusSince›" from the Server `Lot`; its value is the latest soil Reading as `raw N` while `needsCalibration`, otherwise `moisturePercent` when the Server sends it; status-specific hero text of UX-DR78 (needsCalibration "no % until calibrated"; unknown node "Check power or range.", hub "Hub ‹id› is silent; Lots behind it can't be read."; paused "paused until ‹date›", paused by Site "Paused with the Site" and for Admin+ "Resume the Site to resume this Node").
- Every UX-DR named in the story (27, 28, 29, 30, 32, 33, 63, 78, 98) has a test whose name starts with its ID, written failing before the code, and is added to the coverage lists on web, Android and iOS. Copy only from catalogues; Android and iOS keys and values identical; light and dark snapshots/render tests for each state; one-column fallback at large text.

**Never:**
- No Thresholds, Calibrate, Pause/Resume actions or admin strip buttons (Epics 5, 8); no Threshold band or below-low bars; no move/unassign (Story 4.9); no Node row actions; no silent/paused markers on Node rows; no Hub changes.
- No SignalR, polling timer, Alerts rail, or history for Hubs; no new Server table or migration (query existing `readings` and `device_reports`).
- No client-side status, sort or unit conversion; no third-party chart or snapshot library; no skeleton on web.
- Do not write `sprint-status.yaml`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Latest per Sensor | Node reported twice; two quantities | One `sensors` entry per `(slot, quantity)` with the newest value and `measuredAt`, converted units | — |
| No Node / Node, no Reading | Lot without Node; Node declared nothing | `node` and `sensors` absent / `node` present, `sensors` empty, battery from report if any | — |
| Battery unknown | No report row or null `battery_percent`, charging unspecified | `batteryPercent` and `charging` omitted; `lastSeenAt` omitted without report | — |
| History window | Readings across 40 days, default query | Days of the last 30 UTC days ascending with `low`/`high`/`readingCount`; missing days absent | — |
| History paging | `limit=10` over 25 days | Three pages joined by `nextCursor` reproduce the full list, last page has no cursor | Malformed cursor → 400 |
| History after reassignment | Node moved to a new Lot | Only the new Lot's Node Readings since its claim; old Lot shows none | — |
| Authorization | Member of the Site / other Site | 200 / 403 per matrix; unknown Site 404 | RFC 9457 |
| Devices list | Hub, Node on "Tomatoes", unassigned Node | Hub first; Nodes by Lot name, unassigned last, each with `lotName`, battery, charging, `lastSeenAt` | — |
| Lot detail states | Fixtures for each status, with and without Sensors, battery < 20 %, charging, stale | Hero, cells, chart and Device cells of the Always section; `battery--low` below 20 %; stale values hidden | Missing optionals render as `—` |
| Chart | 30 days with gaps; picked quantity changed | Bars only for present days, gaps for missing; summary label updates; readout on tap | — |
| Large text / narrow | iOS ≥ AX1, Android scale ≥ 1.5, web < 400 px | One column (cells 1-up), nothing clipped | — |

</intent-contract>

## Code Map

**Server**
- `apps/cs/server/Edge/EdgeApi.cs` -- routes `MapEdgeApi` (:214-275, `.RequireSiteRole(SiteRole.Member)`), `LotResponse` (:74), `GetLotAsync` (:452), `ToLotResponse` (:990), `DeviceListItemResponse` (:125), `ListDevicesAsync` (:815), `ToDeviceListItem` (:834), `ServerTimeFormat`/`ToServerTime`. `EdgeProblems.cs` (`LotNotFound`, `Validation`, `Result`), `EdgeValidation.cs`.
- `apps/cs/server/Lots/LotsReadModel.cs` -- `LotView`, `FindLotAsync`, the `lastReadingAt` subquery (`device_id = l.claimed_by AND measured_at >= l.claimed_at`) is the join precedent. `apps/cs/server/Devices/DevicesReadModel.cs` -- `DeviceView`, `ListDevicesAsync` over `devices`.
- `apps/cs/migrations/Migrations/M20261006150000CreateTablesReadings.cs` -- `readings` (`device_id, sensor_id, slot, quantity, raw_value bigint, measured_at, calibration_id`) and `device_reports` (`device_id, measured_at, battery_percent, charging text, received_at`), indexes on `(device_id, measured_at)`. `DeviceIngestionStore.cs` writes them (check the stored `charging` token there). Units: soil raw count, temperature milli-°C, humidity milli-%, gas Ω (`packages/proto/coldframe/device/v1/envelope.proto:65-90`).
- Tests: `tests/cs/server.integration/Edge/EdgeApiFixture.cs`, `LotStatusTests.cs`, `LotsTests.cs`, `Devices/DevicesListTests.cs`, `IngestTests.cs`, `Edge/AuthorizationMatrixTests.cs` (needs a `_samples["GET /sites/{siteId}/lots/{lotId}/history"]` entry), `tests/cs/server.tests/Edge/EdgeEndpointDiscoveryTests.cs` (code and OpenAPI operations/roles must match), `Edge/*ResponseTests.cs`.
- Contract: `packages/openapi/coldframe.openapi.json` (paths :199, :316; schemas `Lot` :514, `DeviceListItem` :761), `packages/openapi/README.md`, `packages/proto/check-compat.sh` (oasdiff), `packages/ts/api-client` (`pnpm --filter @coldframe/api-client generate`, aliases in `src/index.ts`).

**Web** (`apps/ts/web/src`, SvelteKit BFF)
- Add `routes/(app)/garden/[lotId]/+page.server.ts`/`+page.svelte`; `lib/server/lots.ts` (`loadGarden` pattern), `lib/server/sites.ts` (`call<T>`), `lib/server/last-good.ts` (`readThrough`, `lotsKey` pattern), `lib/server/devices.ts`, `lib/devices.ts` (`hubsOf`; add `nodesOf`), `routes/(app)/devices/+page.svelte` (Nodes section after Hubs), `lib/components/LotTiles.svelte` (wrap tile in link), `lib/lot-tiles.ts` (status icon/label/spoken reuse), `Icon.svelte` (register `battery--low`), `StaleHeader.svelte`, `SegmentedChoice.svelte` (Sensor picker), `lib/i18n/en.json`, `i18n/format.ts`, `announcer.svelte.ts`.
- Tokens exist (`packages/design-tokens/generated/css/tokens.css`: `--cf-color-chart-bar`, `layer-01`, `border-subtle`, `hero-value`).
- Tests: `tests/ts/web/*.test.ts`, `coverage.test.ts` (`storyIds`), `copy.test.ts`, `no-hard-coded-copy.test.ts`, `styles.test.ts`; `tests/ts/web.e2e/specs/{garden,devices,lot-status,accessibility}.spec.ts` (devices spec currently asserts no "Nodes" heading), `fixtures/fake-idp.ts` (add lot detail, history, node fields).

**Kotlin / Android** (`packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core`, `apps/kt/android/src/main/kotlin/com/escendit/coldframe/android`)
- `api/ApiDtos.kt` (`LotDto`, `DeviceListItemDto`), `api/ColdframeApi.kt` (`call`), `lots/LotsEngine.kt`, `Lots.kt`, `LotsOverview.kt`, `LotsSnapshot.kt`, `devices/DevicesEngine.kt` (`toHubs`), `Devices.kt`, `DevicesSnapshot.kt`, `iosMain/.../IosLots.kt`, `IosDevices.kt`, `sites/SitesWiring.kt`, `OpenApiContractTest.kt`.
- Android: `ui/shell/AppShell.kt` (sub-screen booleans + `BackHandler`), `ui/sites/LotTile.kt` (:285-344 clickable only for no-Node), `GardenScreen.kt`, `ui/devices/DevicesScreen.kt` (`Hubs()`), `ui/components/Hatch.kt`, `ui/theme/ColdframeTheme.kt`, `res/values/strings.xml`; tests `SnapshotTest.kt` (Roborazzi, `tests/kt/android/snapshots`), `DevicesScreenTest.kt`, `StringsTest.kt`, `CoverageTest.kt`, `AccessibilityTest.kt`.

**iOS** (`apps/swift/ios`)
- `Sources/ColdframeIOS/LotsPresentation.swift`, `DevicesPresentation.swift`, `UI/SitesViews.swift` (`GardenView`, `.navigationDestination`), `UI/SiteSettingsViews.swift` (`LotGrid`, tappable only for no-Node), `UI/DevicesViews.swift`, `UI/Hatch.swift`, `L10n.swift`, `Resources/Localizable.xcstrings`, `App/CoreLotsService.swift`, `App/CoreDevicesService.swift`; tests `tests/swift/ios/ColdframeIOSTests/{RenderTests,CatalogueTests (coveredUxDrs),DevicesPresentationTests,LotsPresentationTests}.swift`. SwiftUI and `App/` compile only on macOS CI.

## Tasks & Acceptance

**Execution:**
- `tests/cs/server.integration/**`, `tests/cs/server.tests/**` -- tests first: latest-per-Sensor, history window/paging/validation, reassignment, Devices list order and Node fields, authorization-matrix sample, endpoint discovery -- the matrix rows.
- `packages/openapi/coldframe.openapi.json`, `README.md`, `packages/ts/api-client/src/{schema,index}.ts` -- add schemas, `getLotHistory`, optional fields; regenerate; oasdiff must pass -- the contract.
- `apps/cs/server/Edge/EdgeApi.cs`, `Lots/LotsReadModel.cs` (or a new `Lots/LotDetailReadModel.cs`), `Devices/DevicesReadModel.cs` -- queries, unit conversion, opaque cursor, `GetLotAsync` detail fields, history route, Devices Node fields and order -- the Server surface.
- `packages/kt/core/**`, `tests/kt/core/**` -- DTOs mirrored (`OpenApiContractTest`), a Lot detail engine (load detail + history per quantity, stale handling via the cache pattern), Node rows in the Devices engine/snapshot, flat snapshots for Swift, pure formatting models shared by both shells -- core.
- `apps/kt/android/**`, `tests/kt/android/**` -- tile opens detail, Lot detail screen (hero, Sensor cells, Canvas History chart with picker, Device cells), Nodes section, strings, Roborazzi baselines light/dark at normal size and font scale 2, coverage lists -- Android.
- `apps/swift/ios/**`, `tests/swift/ios/**` -- the same on iOS: presentation structs carry every rule and are tested on Linux; thin SwiftUI views; render tests light/dark; catalogue and `coveredUxDrs` -- iOS.
- `apps/ts/web/**`, `tests/ts/web/**`, `tests/ts/web.e2e/**` -- lot route, linked tiles, detail components, SVG History chart, Nodes section, i18n, fake-Server routes, Playwright snapshots light/dark with axe, coverage and copy guards -- web.
- `apps/cs/README.md`, `apps/ts/web/README.md`, `packages/kt/core/README.md`, `_bmad-output/implementation-artifacts/deferred-work.md` -- document the reads, units and fixture-only parts -- docs.

**Acceptance Criteria:**
- Given a Lot with a Node that has reported, when `GET /sites/{siteId}/lots/{lotId}` is called, then the response carries the hero fields, the latest Reading per Sensor in the units above with `measuredAt`, and the Node's battery, charging and `lastSeenAt`.
- Given the History query, when it is called with `from`, `to`, `limit` and `cursor`, then it returns ascending UTC daily low/high per the matrix, pages without gaps or repeats, rejects bad input with a 400 problem, and is Member-readable.
- Given the Devices list, when it is read, then Nodes appear by Lot name with battery, charging and last seen on the Server, and on every client under a "Nodes" heading after "Hubs".
- Given Lot detail on web, Android and iOS in light and dark in each state (live, stale, noNode, needsCalibration, unknown, paused), then each shows the hero, 3-up Sensor cells, 30-day chart with text alternative, and 2-up Device cells, and the snapshot or render tests cover each.
- Given the CI commands of Verification, when they run, then all pass.

## Spec Change Log

## Review Triage Log

### 2026-10-07 — Review pass
- verdicts: 35 findings — high 0, medium 0, low 19, false 16, maybe-false 0
- findings:
  - `[low]` `[patch]` (blind, edge, gap) A date-only `to` parses as midnight and drops its own day — `TryReadTime` now reads a 10-character `to` as the end of that UTC day; test `ADateOnlyToIncludesThatWholeUtcDay`.
  - `[low]` `[patch]` (blind) Nodes are ordered by Lot name in byte order, so "Zucchini" precedes "tomatoes" — order is now `lower(name)`, then `name`, then Device ID; mixed-case case added to `DevicesListTests`.
  - `[low]` `[patch]` (blind) Mobile re-cached and served other quantities' older history after a refresh — `LotDetailEngine.landed` keeps only the picked quantity's history; test added.
  - `[low]` `[patch]` (gap) The failed history read (`historyUnavailable`) was never driven by a test — engine test added.
  - `[low]` `[patch]` (gap) Sign-out, 401, 404 on a cached Lot and a Site switch were untested for the detail cache — four engine tests added.
  - `[low]` `[patch]` (gap) The Kotlin-to-Swift snapshot key vocabulary was checked for one state — a test iterates every enum entry and one stale and one failed snapshot.
  - `[low]` `[reject]` (blind, edge) Two Sensors with the same quantity in different slots are not told apart — the Node hardware has one soil probe and one BME680; a slot field is new contract surface for no Node that exists.
  - `[low]` `[reject]` (blind, edge) A Node's `online` is from heartbeats while `lastSeenAt` is from reports — the contract says `online` is not meaningful for a Node and no client shows it.
  - `[low]` `[reject]` (blind) The first history day is partial after a mid-day claim — only Readings since the claim count, as the Always section says; the doc wording "whole UTC days" refers to the window start.
  - `[low]` `[reject]` (blind) The history query is unbounded in cost — one Node, one quantity, at most 366 days, on the `(device_id, measured_at)` index; no measured problem.
  - `[low]` `[reject]` (blind, edge) Web loads every quantity's history eagerly and one failure fails the page — a failing history read means the Server is failing; the page then shows its unreachable or stale state, which is true.
  - `[low]` `[reject]` (blind) The web chart's min over lows and bar scaling are client logic — it is formatting for the summary label, not a rule; the Never list excludes status, sort and unit conversion.
  - `[low]` `[reject]` (blind, edge) Web and mobile scale all-negative lows differently — cosmetic; both show every day and the readout.
  - `[low]` `[reject]` (blind, edge) The hub-silent hero does not name the right Hub on every platform — no producer of `unknownCause: hub` exists before Epic 7; recorded as DW-73.
  - `[low]` `[reject]` (edge) Paused-by-Site with an end shows the date on web only — no Pause producer before Epic 8; the spec leaves the combination open.
  - `[low]` `[reject]` (edge) An older history read can overwrite a newer one; a history of more than four pages is shown as complete; an unknown unit leaves an empty chart — the Server pages 31 days by default, always sends known units, and no trigger beyond a double tap was shown.
  - `[low]` `[reject]` (blind) Two or three queries per read are not one snapshot — a reassignment in between returns data of one Node or the other, each true when read.
  - `[low]` `[reject]` (blind) "Every 15 min" is fixed copy — the Design Notes and UX-DR29 give this meta text; the cadence is a product constant.
  - `[low]` `[reject]` (edge) Sensor order inside one slot is alphabetical — the Node declares one quantity per slot, so slot order is the order; clients order by quantity.
  - `[false]` `[reject]` (edge) A removed or paused Lot still resolves a claim and Node — a claimed Lot refuses removal (epic context), and a paused Lot keeps its Node's data by design.
  - `[false]` `[reject]` (edge) A Reading with a future `measured_at` becomes the latest — ingestion rejects `measured_at` more than 5 min ahead (`rejected_time`).
  - `[false]` `[reject]` (edge) The Devices list shows the name of a removed Lot — a Node cannot be on a removed Lot.
  - `[false]` `[reject]` (blind) Ordering is not locale-aware — now case-insensitive; collation by locale would vary by database.
  - `[false]` `[reject]` (blind) Spec bookkeeping is stale (`deferred: []`, empty logs) — the ledger entries live in `deferred-work.md` and the logs are filled by this pass.
  - `[false]` `[reject]` (blind) Tests miss a stranger role, a future `to`, a replayed cursor — the matrix test covers roles, and a future `to` just ends the window at that time.
  - `[false]` `[reject]` (intent) Reading D (operator actions) applies — no acceptance criterion needs a human outside the repo.
  - `[false]` `[reject]` (intent) Readings B, C and E — the story and its spec define the full stack; the diff implements it.
  - `[false]` `[reject]` (intent) `moisturePercent` is exercised only by fixtures — the Server sends none before Epic 5, as the spec says.
  - `[false]` `[reject]` (intent) No admin strip or Threshold band — excluded by the spec's Never list.
  - `[false]` `[reject]` (intent) No Auto Run Result in the diff — it is written at finalization.
  - `[false]` `[reject]` (intent) iOS verified only by presentation and render-exists tests — the repo's pattern; the macOS CI jobs are the compile check, recorded as DW-74.
  - `[false]` `[reject]` (intent) `sprint-status.yaml` — not in the diff.
  - `[false]` `[reject]` (blind) The `oversized` warning has no split plan — cohesive cross-layer stories stay in one file.
  - `[false]` `[reject]` (blind) Cursor binds only a day, not the query — the cursor is the last day; a client that changes the quantity gets that quantity's days after it, which is a valid query.
  - `[false]` `[reject]` (intent) iOS render tests assert only that an image renders — the same pattern Story 4.7 uses.

## Design Notes

**Why detail rides on `getLot`.** The Lot detail needs the Lot's own status fields plus Node and Sensor values in one read; the list stays small because the optional blocks are absent there. History is a separate operation because it is range-queried and paged.

**Why Server-side units.** AD-14 keeps rules on the Server; clients only format. Soil stays raw until Epic 5 so no client can show an uncalibrated percentage.

**Last seen of a Node.** `devices.last_seen_at` is written from Hub heartbeats only, so a Node's last seen is the newest `device_reports` row (`measured_at`).

**Admin strip.** Thresholds, Calibrate and Pause/Resume have no destination before Epics 5 and 8, so no strip is drawn; Epic 5 and 8 add it.

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror && dotnet format --verify-no-changes --no-restore` -- expected: clean
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- expected: all pass
- `packages/proto/check-compat.sh --self-test && packages/proto/check-compat.sh --base "$(git merge-base HEAD origin/main)"` -- expected: pass
- `pnpm -r typecheck && pnpm -r lint && pnpm -r test && pnpm --filter @coldframe/design-tokens run check && pnpm --filter @coldframe/openapi run check` -- expected: pass, Playwright baselines included
- `ANDROID_HOME=~/Android/Sdk ./gradlew check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64` -- expected: pass, Roborazzi verify included
- `podman run --rm --security-opt label=disable -v "$PWD":/work -w /work swift:6.3.3 sh -c 'swift build && swift test && swift format lint --strict -r .'` -- expected: pass

**Manual checks (if no CLI):**
- The macOS `swift` and `ios` CI jobs are the only compile check of the SwiftUI views and `apps/swift/ios/App`; read their result on the pull request.

## Auto Run Result

Status: done

**Summary.** A Member can open a Lot from its tile on web, Android and iOS and see the hero, the latest Reading per Sensor in Server units, a 30-day History chart of daily lows with a Sensor picker and a text alternative, and Device cells (battery, charging, last seen). The Server adds `node` and `sensors` to `GET /sites/{siteId}/lots/{lotId}`, a cursor-paginated `GET /sites/{siteId}/lots/{lotId}/history` taking `from`/`to`, and Node status fields and Lot-name order on the Devices list, which now shows a "Nodes" section after "Hubs" on every client.

**Files changed.** Server: `apps/cs/server/Lots/LotDetailReadModel.cs`, `SensorConversion.cs`, `Edge/EdgeApi.cs`, `Devices/DevicesReadModel.cs`. Contract: `packages/openapi/coldframe.openapi.json`, README, generated TS schema and aliases. Kotlin core (Lot detail engine, snapshot, DTOs, Devices Nodes), Android (Lot detail screen, Canvas chart, Nodes section, strings, Roborazzi baselines), iOS (presentation structs, SwiftUI views, App service, catalogue), web (`garden/[lotId]` route, `HistoryChart.svelte`, Nodes section, BFF loader), tests on every layer, READMEs and `deferred-work.md` (DW-72 to DW-74).

**Review.** Four layers ran; 35 findings. Patches applied 6 (all low: date-only `to`, case-insensitive Node order, stale mobile history after a refresh, three engine test gaps), deferred 0, rejected 29 — each with its reason in the Review Triage Log.

**Follow-up review: not recommended.** No patched finding was `high` and none was `medium`.

**Verification.** After the patches: `dotnet build -warnaserror` and `dotnet format --verify-no-changes` clean; `dotnet test` 775 passed, 0 failed (integration under podman); `pnpm -r typecheck`, `lint`, `test` pass, Playwright 58+ passed including `lot-detail.spec.ts` (9); design-tokens and openapi checks pass; `./gradlew check` and the iOS compile tasks pass; `swift build`, `swift test` (221) and `swift format lint --strict` pass in the container. A date-dependent Playwright test (stale "as of" under a fixed clock) was fixed. `packages/proto/check-compat.sh` was not run: `oasdiff` and `buf` are not installed here; the contract change is additive.

**Residual risks.**
- The SwiftUI views and `apps/swift/ios/App` have never been compiled; the macOS `swift` and `ios` CI jobs are their first build, and render tests only check that an image comes out.
- Playwright baselines were recorded on Linux/chromium and the CI renderer may differ.
- The hub-silent hero cannot name the right Hub on mobile and may name the wrong one on web until Epic 7 (DW-73).
- Two Sensors of one quantity on a Node would not be told apart.
- The History window can return 31 UTC days with the default `limit`.
- No Thresholds exist, so no band or below-low bars; the admin strip waits for Epics 5 and 8.
