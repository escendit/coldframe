---
title: 'Story 5.4: Set Thresholds in the app and see them on the chart'
type: 'feature'
created: '2026-10-08'
baseline_revision: 'ae0a03a17d8d124cd319a43dd23938e18ca71004'
status: 'in-progress'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-5-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-5-2-calibrate-from-the-app.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-5-3-thresholds-on-the-server.md'
  - '{project-root}/apps/cs/README.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Story 5.3 can store Thresholds, but no client sets or shows them: the History chart has no band or below-low bars, the Server never sends `moisturePercent` or `lowThresholdPercent`, soil-moisture History days stay raw after Calibration, and web, iOS and Android have no Thresholds screen.

**Approach:** Close the three Server gaps (Lot `moisturePercent`/`lowThresholdPercent`, calibrated soil History in %), then build the Thresholds modal (a Threshold column per Sensor) and the chart band on web, in the shared Kotlin core with Android, and as thin iOS shells over the same core, reachable from Lot detail, a Sensor cell and right after Calibration.

## Boundaries & Constraints

**Always:** Only Owners and Administrators edit; a Member sees Thresholds read-only with no edit control (hidden, not disabled; UX-DR84), and the API answers 403 (a 403 race shows "You can't change this on <Site>. Ask an Owner or Administrator."). Low is required on an alerting Sensor, high optional ("Add high" / clear), a calibrated soil Sensor moves in 5 % steps within 0-100 %, "Low must stay below high." appears inline under the field and Save is disabled while invalid; the Server stays the only validator (the client check only gates Save; Problem Details errors state what happened, what did not change and the next action; UX-DR91). A Sensor without a default offers the Server's `proposedLow` when alerts are turned on, never a proposed high. The Threshold column: vertical track on `layer-01`, 2 px `primary-text` low line, current Reading marker, dashed "no high" marker, values to the right, drag or type (UX-DR45); the modal has Cancel/Save (UX-DR69). The 30-day chart draws the band (`chart-band`), 2 px low line, 1 px dashed `chart-high-line`, daily lows below low as solid `chart-bar-below-low` (a non-colour cue), legend "solid bar = below N %", and the accessible summary names the below-low days (UX-DR5, UX-DR32/33); the band is drawn only when the History unit is `%` for soil moisture. A calibrated, in-range Lot tile shows ~% (5 % steps) and *OK*. Rules and copy logic live in the Server/Kotlin core, shells never compute (AD-14); copy via i18n/strings/L10n with identical keys on web, Android and iOS; design tokens only. Test-first (NFR16): named failing tests before implementation, in light and dark, at the largest text size; every UX-DR id above in a test name; every changed endpoint in OpenAPI, README, authorization matrix.

**Never:** No Alert evaluation, Notification or *needs water* (Epic 6), no Calibration math change, no editing existing migrations, no writing `sprint-status.yaml`, no change to stored Readings, no client-side status logic. Swift is not buildable on this host: keep iOS thin, mirror the 5.2 pattern exactly, and say so in the result.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Set low | Admin opens Thresholds, types 25 %, Save | PUT override, modal closes, chart band and tile refresh | — |
| Add / clear high | "Add high", set 70, later clear | high override then `cleared`; dashed "no high" marker when empty | — |
| Low not below high | low 70, high 60 | "Low must stay below high." inline, Save disabled | Server 400 shown with what did not change |
| Proposal | Watched Sensor, no default, alerts turned on | low prefilled from `proposedLow`, no high | — |
| After Calibration | Calibrate confirmation | Entry to Thresholds for the same Lot | — |
| Member | Member opens Lot detail | Thresholds shown read-only, no edit control; direct PUT 403 | 403 race notice |
| Chart | Calibrated soil, low 30 %, daily low 20 % | band + low line; that bar solid below-low; summary names it | uncalibrated/raw: no band |
| Tile | Calibrated, in range | ~% and *OK* | — |
| Cancel | Edits then Cancel | nothing sent | — |
| Not delivered | 503 / unreachable on Save | edits kept, "not saved" notice, retry | mapped failure |

</intent-contract>

## Code Map

- `apps/cs/server/Edge/EdgeApi.cs` (`LotResponse` ~l.60-85, `ToLotResponse` ~l.1788, list ~l.597, `GetLotAsync` ~l.640, history ~l.745/807) -- add `moisturePercent` and `lowThresholdPercent` (set only for a Lot whose status is calibrated: soil Sensor with a Calibration, newest Reading as %, effective low from the Sensor grain); bounded lookups, additive. Update the stale remark.
- `apps/cs/server/Lots/LotDetailReadModel.cs` (`HistorySql` ~l.71, `LatestReadingsAsync` joins `calibrations` ~l.57) -- soil-moisture History aggregates calibrated Readings as % (join the Calibration per Reading, CalibrationMath.Percent in SQL or per row); with none calibrated in the page, raw as today. `SensorConversion.cs`, `CalibrationMath` as reference.
- `packages/openapi/coldframe.openapi.json`, `README.md`, `packages/ts/api-client/src/{schema,index}.ts` -- describe the Lot fields and History units; `pnpm --filter @coldframe/api-client generate`.
- `tests/cs/server.integration/**`, `tests/cs/server.tests/**` -- named tests beside the Lot detail and History endpoint tests; `EdgeEndpointDiscoveryTests`.
- `packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/`: `api/ColdframeApi.kt` (+`ApiDtos.kt`, `ApiResult.kt`) `getSensorThresholds`/`setSensorThresholds`; new `thresholds/{Thresholds,ThresholdsEngine,ThresholdsSnapshot}.kt` modelled on `calibrate/CalibrateEngine`; `lots/LotDetail.kt` (`HistoryChart.of` ~l.236, `ChartBar` ~l.213, `SensorCell`, `canSetThresholds`), `LotDetailEngine.kt`, `LotDetailSnapshot.kt` (flat fields for Swift), `LotsOverview.kt` (~l.442 ~%), `sites/SitesWiring.kt`; `androidMain/.../AndroidSignIn.kt`, `iosMain/.../IosSignIn.kt` + `IosThresholds.kt`.
- `apps/kt/android/.../ui/`: `sites/LotDetailScreen.kt` (`drawChart` ~l.640, entry button beside Calibrate ~l.243), `sites/LotDetailCopy.kt`, `theme/ColdframeTheme.kt` (wire `chartBarBelowLow`, `chartBand`, `chartHighLine`), `calibrate/CalibrateScreen.kt` (confirmation entry), new `thresholds/{ThresholdsScreen,ThresholdsActions}.kt`, `ColdframeRoot.kt`, `MainActivity.kt`, `res/values/strings.xml`.
- `apps/swift/ios`: `Sources/ColdframeIOS/{LotDetailPresentation,L10n,Presentations}.swift` + new `ThresholdsPresentation.swift`, `UI/{LotDetailViews,Screens,ThresholdsViews}.swift`, `App/{CoreThresholdsService,ColdframeApp}.swift`, `Resources/Localizable.xcstrings`.
- `apps/ts/web/src`: `lib/components/HistoryChart.svelte`, `lib/lot-detail.ts` (`historyChart`), `lib/server/lot-detail.ts` (`readDetail`), `routes/(app)/garden/[lotId]/+page.svelte`, new `garden/[lotId]/thresholds/{+page.server.ts,+page.svelte}`, `lib/server/thresholds.ts`, `lib/thresholds.ts`, `lib/components/` Threshold column, `lib/i18n/en.json`; calibrate route confirmation entry. `T/lot-detail.test.ts:209` asserts no `below-low|chart-band` and must change.
- Tests: `tests/kt/core/commonTest` (`ThresholdsEngineTest`, `LotDetail*Test`, `OpenApiContractTest`), `tests/kt/android` (`ThresholdsScreenTest`, `SnapshotTest`, `AccessibilityTest`, `StringsTest`, `CoverageTest` add 5/45/69/91), `tests/swift/ios` (`ThresholdsPresentationTests`, `RenderTests`, `CatalogueTests`), `tests/ts/web` (`coverage.test.ts` ids, thresholds/lot-detail tests), `tests/ts/web.e2e` (`fixtures/fake-idp.ts` thresholds GET/PUT, new spec: calibrate, set low Threshold, tile ~% *OK*, chart band; `-${theme}` screenshots, largest text).
- `apps/cs/README.md` -- document the new Lot fields and History units.

## Tasks & Acceptance

**Execution:**
- `tests/**` -- write failing tests first per matrix row and UX-DR id (NFR16)
- `apps/cs/**`, `packages/openapi/**`, `packages/ts/api-client/**` -- Lot `moisturePercent`/`lowThresholdPercent`, calibrated History in %, contract and docs
- `packages/kt/core/**` -- API calls, Thresholds engine, chart band/below-low fields, tile ~%, wiring
- `apps/kt/android/**` -- Thresholds screen, Threshold column, chart band, entry points, strings
- `apps/swift/ios/**` -- presentation, service adapter, views, catalogue strings (CI-only verification)
- `apps/ts/web/**` -- Thresholds page/modal, Threshold column, chart band, Member read-only, e2e

**Acceptance Criteria:**
- Given an Owner or Administrator on web, iOS or Android, when they open Thresholds from Lot detail, a Sensor cell or right after Calibration, then each Sensor has a Threshold column with required low, optional high and 5 % steps for calibrated soil, "Low must stay below high." inline and Save disabled while invalid.
- Given saved Thresholds, when they view Lot detail, then the 30-day chart shows the band and solid below-low bars for daily lows under the low Threshold, and a calibrated in-range Lot tile shows ~% and *OK*.
- Given a Member, when they open Lot detail, then Thresholds are visible read-only with no edit control.
- Given snapshot tests and a Playwright flow (calibrate, set a low Threshold, tile shows ~% *OK*), then they pass in light and dark at the largest text size.
- Given the UX contract, then UX-DR5, 32, 33, 45, 69, 84 and 91 each have a named test written failing first.

## Spec Change Log

## Review Triage Log

## Design Notes

Server-side gap analysis (verified): `LotResponse` never carries `moisturePercent`/`lowThresholdPercent`, and `HistorySql` groups raw values without the Calibration, so a % band over raw days would be wrong. Both are fixed on the Server so no client converts (AD-14). Lot detail may read Thresholds per Sensor with the 5.3 GET (Member allowed) for the column and the band; the chart band uses the soil Sensor.

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror` and `dotnet format --verify-no-changes --no-restore` -- expected: success
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- expected: pass (known host flake: Keycloak rename test)
- `pnpm -r test`, `pnpm lint`, `pnpm typecheck`, `pnpm --filter @coldframe/web-e2e test` -- expected: success
- `./gradlew :core:jvmTest :android:testDebugUnitTest ktlintFormat check --offline` (set `ANDROID_HOME=/var/home/simon/Android/Sdk`) -- expected: success; baselines via `:android:recordRoborazziDebug`
