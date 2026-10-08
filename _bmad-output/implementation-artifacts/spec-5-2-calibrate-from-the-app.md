---
title: 'Story 5.2: Calibrate from the app'
type: 'feature'
created: '2026-10-07'
baseline_revision: 'e3246cf1b5e86e859c97fa13af4ffbdaf7139a47'
status: 'in-progress'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-5-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-5-1-calibration-on-the-server.md'
  - '{project-root}/apps/cs/README.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Story 5.1 can calibrate a Sensor, but no client can: Lot responses carry no `sensorId`, nothing lists stored Readings with their `reading_seq`, nothing reports the kept dry point, and web, iOS and Android have no Calibrate flow.

**Approach:** Add a small Administrator-only read to the Server (Sensor id and calibratable flag on Lot Sensors; recent stored Readings plus the pending point per Sensor), then build the two-step Calibrate flow (dry, then wet) in the Kotlin core with Android and iOS shells, and as a SvelteKit page on web, reachable from the needs-calibration tile and Lot detail (and the Node-added outcome on mobile).

## Boundaries & Constraints

**Always:** Admin/Owner only; Calibrate is hidden (not disabled) for Members and the API answers 403. Only Sensors whose Specification says `calibration: true` show it. Reference points are stored Readings named by `readingSeq`, sent over REST via the 5.1 endpoint; no BLE. The dry point is kept server-side (5.1 pending point) so leaving and returning resumes at wet. A step enables "Record dry"/"Record wet" only when a Reading taken after that step started has arrived; a "Recent Readings" list is the alternative. Waiting text is never announced; a fresh Reading is announced politely ("New Reading 07:17, raw 612. Record dry is available."). On a paused Device the flow explains that Readings resume after the Pause ends and does not wait; Resume is offered only to Admin+ when the Pause is the Device's own. Confirmation shows both raw values and "% appears with the next Reading", updating in place when the first calibrated Reading arrives; no % before the Server stores a calibrated Reading. Rules, roles and copy logic live in the Server/Kotlin core, shells never compute (AD-14); copy via i18n/strings/L10n; design tokens only. Test-first (NFR16): named failing tests before implementation; snapshot/render tests in light and dark; every new endpoint in the OpenAPI contract, the authorization matrix and the endpoint catalog.

**Never:** No Threshold work (5.3/5.4), no new Calibration math or change to 5.1 behaviour, no BLE, no `needsWater`, no editing of existing migrations, no writing `sprint-status.yaml`. Web has no Node-added outcome (4-3 was "no web change"), so none is added there. Real Pause is Epic 8: paused behaviour is fixture-tested via `pausedBy`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Start | Admin opens Calibrate on a needs-calibration Lot (tile, Lot detail, Node-added outcome) | Step 1 (dry) with waiting panel: last raw value and time, hint to short-press the Node's button | — |
| Fresh Reading | Reading stored with `measuredAt` after the step started | "Record dry"/"Record wet" enabled; polite announcement | — |
| Pick instead | Recent Readings list shown | Selecting one records that `readingSeq` | — |
| Record dry | Dry chosen | POST with dry only; Sensor stays uncalibrated; flow moves to wet | Failure shown inline, nothing advances |
| Resume | Pending dry point kept, user returns | Opens at wet step | — |
| Confirm | Wet recorded | Both raw values + "% appears with the next Reading"; updates in place to "<Lot> reads ~NN %" when a calibrated Reading arrives | — |
| Indistinct points | Server 400 | Message states what happened, what did not change, next action | Problem Details mapped |
| Paused Device | Lot `pausedBy` set | Explanation, no waiting; Resume only for Admin+ on Device's own Pause | — |
| Member | Member opens Lot | No Calibrate control; direct endpoint call 403 | 403 |
| Not delivered | Server 503 on save | Flow keeps the recorded point and says save was not confirmed, retry | Mapped failure |

</intent-contract>

## Code Map

- `apps/cs/server/Edge/EdgeApi.cs` (route table ~l.357-381, `CalibrateSensorAsync` ~l.1346 as template for the Sensor-in-Site check), `EdgeProblems.cs` -- add `GET /sites/{siteId}/sensors/{sensorId}/calibration` (Administrator): pending dry/wet raw, Calibration in force, and recent stored Readings (`readingSeq`, `rawValue`, `measuredAt`), bounded by `measured_at`.
- `apps/cs/server/Sensors/SensorReadings.cs` -- add the recent-Readings query beside `FindRawValueAsync` (5.1 deferred: bound by `measured_at`). `SensorSnapshot` (`packages/cs/contracts/Sensors/SensorGrains.cs` ~l.210) already carries `PendingDryRaw`, `PendingWetRaw`, `Calibration`, `Specification`.
- `apps/cs/server/Lots/LotDetailReadModel.cs` (`LatestSql`), `SensorReadingResponse` in `EdgeApi.cs` ~l.90 -- add `sensorId` and `calibratable` to each Lot Sensor reading (additive).
- `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md` -- operation with `x-coldframe-minimum-role: "Administrator"`, new schemas, Lot Sensor fields; `pnpm --filter @coldframe/api-client generate` rewrites `packages/ts/api-client/src/schema.ts`; `src/index.ts` aliases.
- `tests/cs/server.tests/Edge/EdgeEndpointCatalog.cs`, `tests/cs/server.integration/Edge/AuthorizationMatrixTests.cs` (`_samples`, l.109 calibration sample) -- register the endpoint; integration tests next to the 5.1 ones.
- `packages/kt/core/.../core/api/ColdframeApi.kt` (`call()` pattern ~l.172-190, failure mapping ~l.227-267), `ApiDtos.kt`, `tests/kt/core/jvmTest/.../api/OpenApiContractTest.kt` -- hand-written client: add `calibrateSensor`, `getSensorCalibration` DTOs and `ApiFailure` cases (400 indistinct, 503 not delivered).
- `packages/kt/core/.../core/lots/LotDetail.kt` (~l.343-389, 434), `LotsOverview.kt` (~l.376-424), `LotDetailEngine.kt`, `core/sites/SitesWiring.kt`, `core/setup/NodeSetupEngine.kt` (~l.690-696, 775-780: `NodeOutcomeAction`) -- add `canCalibrate` to models, a new Calibrate engine (state machine, cancellable wait loop with injectable clock/delay, announcement kinds, paused branch) and a `Calibrate` outcome action (Admin+, calibratable Sensor only).
- Android `apps/kt/android`: `ui/sites/LotTile.kt` (~l.102-345), `LotDetailScreen.kt` (~l.229-378 Add a Node button pattern), `ui/setup/AddNodeFlow.kt` (~l.305-399), `ui/setup/SetupFlowShell.kt` (+ `SetupAnnouncementRegion`), `MainActivity.kt`, `ColdframeRoot.kt` (~l.165-200 full-screen branch chain), `AppShell.kt`, `*Actions` bags, `res/values/strings.xml` -- Calibrate flow, entry points. Tests: `tests/kt/android/.../SnapshotTest.kt` (Roborazzi light/dark/large), `AccessibilityTest`, `StringsTest`, `CoverageTest`; core tests under `tests/kt/core/{commonTest,jvmTest}` modelled on `NodeSetupEngine`/`DevicesEngine` tests.
- iOS `apps/swift/ios`: `Sources/ColdframeIOS/*Presentation.swift`, `UI/LotDetailViews.swift`, `UI/Screens.swift` (~l.340-350 root swap), `UI/NodeSetupViews.swift` `OutcomeView` (~l.101), `App/CoreXxxService.swift`, `ColdframeApp.swift`, `Resources/Localizable.xcstrings`; tests `tests/swift/ios/ColdframeIOSTests/` (`RenderTests.renders(view, dark:)`, presentation tests). Swift is not buildable on this host (CI only): keep the shell thin and mirror existing patterns exactly.
- Web `apps/ts/web`: `routes/(app)/garden/[lotId]/+page.svelte` (admin at l.31, hero l.33) and `+page.server.ts`, `lib/components/LotTiles.svelte` (tile is one `<a>`; Calibrate needs a sibling control and a role/admin prop from `garden/+page.svelte`), `lib/lot-tiles.ts`, `lib/lot-detail.ts`, `lib/server/lot-detail.ts`, `lib/server/sites.ts` (`call`/`statusError`: add 503), `lib/components/Modal.svelte`/`modal-stack.ts`, `lib/announcer.svelte.ts`, `lib/i18n/en.json`, `lib/roles.ts` -- Calibrate as a full-screen page/route (`garden/[lotId]/calibrate`) using form actions and a short interval `invalidate` only while the flow waits (explicit exception to the focus-only refetch rule). Tests: `tests/ts/web` (vitest; UX-DR ids in `coverage.test.ts`), `tests/ts/web.e2e` (Playwright: extend `fixtures/fake-idp.ts` with the new GET/POST and `sensorId`/`readingSeq`; `lot-detail.spec.ts` as model; screenshots `*-${theme}.png`).
- `apps/cs/README.md` -- document the new read and Lot Sensor fields.

## Tasks & Acceptance

**Execution:**
- `tests/cs/**`, `tests/kt/**`, `tests/ts/**`, `tests/swift/**` -- write failing tests first, named for each matrix row, step, resume path, paused explanation, Member hiding, announcements -- NFR16
- `apps/cs/server/**`, `packages/openapi/**`, `tests/cs/**` -- GET calibration-state/recent-Readings endpoint, `sensorId`/`calibratable` on Lot Sensors, authorization matrix and catalog entries -- unblock clients
- `packages/ts/api-client/**` -- regenerate schema, add aliases
- `packages/kt/core/**`, `tests/kt/core/**` -- client calls, `canCalibrate`, Calibrate engine, Node-added outcome action
- `apps/kt/android/**`, `tests/kt/android/**` -- Calibrate flow UI, entry points, strings, Roborazzi baselines (light/dark/large)
- `apps/swift/ios/**`, `tests/swift/ios/**` -- presentation, service adapter, views, catalogue strings, render tests (CI-only verification)
- `apps/ts/web/**`, `tests/ts/web/**`, `tests/ts/web.e2e/**` -- Calibrate page, tile/detail entry points, Member hiding, announcements, snapshots in both themes and the e2e flow (record dry, record wet, confirm)
- `apps/cs/README.md` -- docs

**Acceptance Criteria:**
- Given a needs-calibration Lot and an Administrator or Owner, when they start Calibrate from the tile, Lot detail or (mobile) the Node-added outcome, then the dry-then-wet flow runs on web, iOS and Android with no BLE.
- Given the dry step, when a Reading taken after the step started arrives, then it shows the last raw value and time while waiting, enables "Record dry", and a recent stored Reading can be picked instead.
- Given both points are recorded, when they confirm, then both raw values and "% appears with the next Reading" are shown and update in place when that Reading arrives.
- Given they leave after the dry point, when they return, then the dry point is kept and the flow resumes at wet.
- Given a paused Device, when they open Calibrate, then it explains Readings resume after the Pause (fixture `pausedBy`) instead of waiting.
- Given a screen reader, when a fresh Reading arrives, then the polite announcement is made and the waiting text is not.
- Given a Member, when they open the Lot, then Calibrate is not shown and the endpoint answers 403.
- Given snapshot and client tests, when they run, then each step, the resume path and the paused explanation pass in light and dark.

## Spec Change Log

## Review Triage Log

## Design Notes

**Why a new read:** 5.1 deferred "no client can learn `reading_seq`" and the pending point is only on the POST 200, so one Administrator-only GET returns both; Lot Sensors gain `sensorId` and `calibratable` so entry points can find the Sensor without another call. **Waiting:** the Reading list is re-read on a short interval only while the flow is open and not paused; a Reading counts as fresh when `measuredAt` is after the step's start time taken from the injected clock.

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror` -- expected: success
- `dotnet format --verify-no-changes --no-restore` -- expected: no changes
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- expected: all pass
- `pnpm -r test` and `pnpm lint && pnpm typecheck` -- expected: success (generated client not stale)
- `pnpm --filter @coldframe/web-e2e test` -- expected: pass with committed snapshots
- `./gradlew :core:jvmTest :android:testDebugUnitTest --offline` and `./gradlew ktlintFormat check --offline` -- expected: success
- Swift/Xcode: not runnable here; CI `swift` and app jobs verify.
