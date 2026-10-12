---
title: 'Story 7.2: Silence in the apps and Silence Window settings'
type: 'feature'
created: '2026-10-12'
baseline_revision: 4100e921fc9683e89c9639256cbc8f199de87d6a
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-7-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-7-1-silent-device-alert-on-the-server.md'
  - '{project-root}/apps/cs/README.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Story 7.1 opens `silent` Health Alerts on the Server, but no client can see them: the Alerts list leaves them out, Devices shows no silence, nothing names a silent Hub, notifications say "A device needs attention", and the Silence Window can only be changed by a grain call.

**Approach:** Extend the contract additively (silent Hubs and silence times on Lots, silence fields on Devices, a `healthAlerts` list beside `alerts`, one Administrator endpoint for the Silence Window), give silent notifications their final copy, and render all of it on web, Android and iOS, where the unknown tile, the Lot detail hero and the Health group already exist.

## Boundaries & Constraints

**Always:**
- Every OpenAPI change is additive: `packages/proto/check-compat.sh` passes against `origin/main`. `Alert` and `AlertList.alerts` keep their shape and stay Threshold-only; Health Alerts travel in the new optional `AlertList.healthAlerts`.
- **Silent since** is one value everywhere: the time the Device was last heard (Node: newest accepted `measured_at`; Hub: last accepted heartbeat), or its enrolment time if it never reported. The Device grain puts it on the Alert when it opens. Every displayed silence duration is `now − silentSince`, rounded down: under 1 h in minutes, under 24 h in hours, otherwise days.
- A Hub is named by the first four hex digits of its Device ID in upper case ("Hub 3F2A"), as the firmware advertises it. Device rows keep the full ID.
- Clients render what the Server sends: no client sorts Lots or derives a status. Unknown Lots stay above every OK Lot through the Server's order.
- Silence Window: 1 min to 30 days in whole seconds; no value means "use the default" (Node 6 h, Hub 5 min). Only Administrators and Owners change it: the API answers a Member with 403 Problem Details and journals nothing; clients hide the control for a Member, never disable it.
- A value a client does not know, or a field that is missing (an Alert opened before this story has no silent-since or Device kind), degrades to today's rendering and today's neutral notification text; nothing fails to decode.
- All copy comes from the string catalogues, in sentence case (upper case is style); Android and iOS keys stay in parity; glossary terms keep their capitals.

**Never:**
- No change to `Alert`'s required members, no removed or renamed contract member, no new `LotStatus`.
- No reads through `IDeviceGrain.Silence()` from HTTP handlers (it evaluates and may open Alerts).
- No change to the push route payload: a tapped silent notification still opens the overview. Routing a push to a Device row needs a `deviceId` in the AsyncAPI `PushRoute` and is left out.
- No battery or uncalibrated Alerts (Stories 7.3, 7.4), no Pause (Epic 8), no pixel-snapshot library for iOS.
- No wall clock in tests.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Node silent | Lot "Beans", Node last heard 01:05, now 07:10 | Lot: `unknown`, cause `node`, `silentSince` 01:05. Tile hatched, "6 h", foot "was ~40 % at" that time; listed above OK Lots | No `silentSince`: duration from `lastReadingAt`, then `statusSince` (as today) |
| Hub silent | Hub `3f2a…` last heartbeat 12 min ago, 2 Lots behind it | Both Lots: `unknown`, cause `hub`, `silentHubId`, `silentSince`. `LotList.silentHubs` has the Hub. Notice above the tiles: "Hub 3F2A silent for 12 min — Lots behind it can't be read.", announced politely once. Hero: "Hub 3F2A is silent; Lots behind it can't be read." | No `silentHubId`: the copy without an ID |
| Silent Hub, no Lot behind it | Hub silent, no Node relayed by it | The notice still shows; no Lot changes | — |
| Hub recovers | Its Alert closes | Notice gone on the next load or live update; Lots return to their status | — |
| Devices, silent row | Device with an open `silent` Alert | Row shows the help icon and "Silent 6 h" (upper case by style) beside last seen | Not silent: row as today |
| Set window | Administrator sends `windowSeconds` 28800 for a Node | 200 `{windowSeconds: 28800, overridden: true}`; the row shows 8 h | Same value again: 200, nothing journaled |
| Back to default | Administrator sends no `windowSeconds` | 200 `{windowSeconds: 21600, overridden: false}` (300 for a Hub) | — |
| Out of range | 59, 2592001, 90.5 or a string | 400 `validation`, nothing journaled | Clients refuse it before sending, with the range in the message |
| Member | Member calls the endpoint / opens Devices | 403 `forbidden`, nothing journaled / no Silence Window control | A 403 race in a client shows the existing "You can't change this on ‹Site›…" notice |
| Unknown Device | Device of another Site, unenrolled or malformed ID | 404 `device-not-found` (400 for a malformed ID, as move/unassign do) | — |
| Alerts list | Open `silent` Alerts for a Node on "Beans" and for a Hub | `healthAlerts` has both (Node: `lotId`, `lotName`; Hub: neither), `alerts` has neither, `openCount` counts them | Later pages (with `cursor`) carry no `healthAlerts` |
| Alert copy | Same two Alerts, and a Node on no Lot | "Node on Lot 'Beans' silent for 6 h", "Hub 3F2A silent for 5 min", "Node 9C0D silent for 6 h"; closed rows drop "for …" | Unknown Health kind: the neutral Health row, as today |
| Push, single | Silent Node Alert delivered at 07:05, last heard 01:05 | Title "Node on Lot 'Beans' silent for 6 h", body "No Reading since 01:05. Check power or range." (time in the User's zone) | Never reported: "No Reading yet. Check power or range." |
| Push, Hub | Silent Hub Alert | "Hub 3F2A silent for 5 min" / "Lots behind it can't be read." | — |
| Reminder | Same Alerts a day later | Title with the duration at send time; body "Still no Reading since 01:05. Check power or range." / "Lots behind it still can't be read." | — |
| Summary | Morning summary with a silent Node and a silent Hub | Lines "Beans: Node silent for 6 h" and "Hub 3F2A silent for 5 min"; counted under "to check" | Node on no Lot: "Node 9C0D silent for 6 h" |

</intent-contract>

## Code Map

Contract
- `packages/openapi/coldframe.openapi.json` -- `Lot` (:996), `LotList`, `DeviceListItem` (:1456), `AlertKind` (:1501, description says "until 7.2"), `Alert` (:1516, do not touch `required`), `AlertList` (:1543), `setSensorThresholds` (:373) as the pattern for an Administrator PUT with `x-coldframe-minimum-role`; `DeviceId` path parameter exists. `packages/openapi/README.md` has the operation table.
- `packages/proto/check-compat.sh` -- `oasdiff breaking --fail-on ERR`; relaxing `Alert.required` was tried and fails with `response-property-became-optional`. Tools: `packages/proto/install-tools.sh <dir>`.
- `packages/ts/api-client/src/schema.ts` -- generated (`pnpm --filter @coldframe/api-client generate`); `tests/ts/api-client/index.test.ts` fails when stale.
- `packages/kt/core/.../core/api/ApiDtos.kt`, `ColdframeApi.kt` -- hand-written DTOs and Ktor client; `tests/kt/core/jvmTest/.../api/OpenApiContractTest.kt` (`assertMirrors`) pins them to the contract.

Server (`apps/cs/server`)
- `Edge/EdgeApi.cs` -- `MapEdgeApi` (:480-602); `ListLotsAsync` (:1054), `GetLotAsync` (:1107), `ToLotResponse` (:2288); `ListDevicesAsync` (:1734), `ToDeviceListItem` (:1753); `ListAlertsAsync` (:989), `ToAlert` (:1024); `MoveDeviceAsync` (:1895) for Device-path handling (`EdgeValidation.NormalizeDeviceId`, `DeviceNotFound()` :2341 whose title says "Node"); `SetSensorThresholdsAsync` (:2054) for body reading and 400s. Response records at the top (:79, :350, :379).
- `Edge/EdgeAccessRule.cs` (`RequireSiteRole`), `tests/cs/server.integration/Edge/AuthorizationMatrixTests.cs` (`_samples` :37-194; `EveryEndpointHasASampleRequest`), `tests/cs/server.tests/Edge/EdgeEndpointDiscoveryTests.cs` (endpoints must equal the contract). Recipe: `apps/cs/README.md:1254-1272`.
- `Devices/DeviceGrain.cs` (`SetSilenceWindow` :748, silence evaluation and `OpenForDevice` call), `DeviceState.cs` (last report, enrolment), `SilenceRule.cs` (defaults, bounds), `packages/cs/contracts/Devices/DeviceEvents.cs` (`device.silence-started` :293, `-ended` :312, `-window-set` :241), `DeviceGrains.cs:125` (`DeviceSilenceWindowResult`: Set, Unchanged, NotFound, OutOfRange; `Window` is the one in force).
- `Devices/DevicesProjector.cs` (:70-136, ignores the silence events), `DevicesReadModel.cs` (`ListSql` :31); table `devices(device_id, site_id, kind, lot_id, enrolled_at, last_seen_at)`.
- `packages/cs/contracts/Alerts/AlertGrains.cs`, `AlertEvents.cs` (`OpenDeviceAlert`, `AlertOpened`), `Sites/SiteGrains.cs:788` (`SiteAlert`), `Notifications/Notifications.cs:43` (`NotificationEntry`), `Identity/UserGrain.cs:412` (`Entry`) -- the chain a new nullable member travels; all are journaled, so additions are nullable.
- `Alerts/AlertsProjector.cs`, `AlertsReadModel.cs` (`ListedKind` :66, `ListSql` :72, `CountOpenSql` :90, both inner-join `lots`; `AlertView` :21).
- `Lots/LotsProjector.cs` (`lot_status_alerts`, `lot_status_devices.relay_hub_id`, `InputsSql` :165), `LotsReadModel.cs` (`ListSql` :53, `FindSql` :70).
- `Notifications/Push/PushText.cs` (`Title` :82, `Body` :126, summary lines :186), `PushText.en.json`, `PushContent.cs` (`FactsAsync` :141); channels may call no other grain. Tests `tests/cs/server.tests/Notifications/PushTextTests.cs:210-265` pin the neutral copy.
- `Journal/ReadModelChanges.cs` -- `changes.Touch(siteId, resource, id)` for live hints; `tests/cs/server.integration/Live/ProjectorHintsTests.cs`.
- `Testing/TestingHostingExtensions.cs:88-116` -- `/testing/clock`, `/testing/users/{userId}/wake`; no Device wake.
- Tests that pin 7.1's "stored, not listed": `tests/cs/server.integration/Edge/AlertsListTests.cs:341`, `Devices/SilentDeviceAlertGrainTests.cs:595`, `tests/cs/server.tests/Alerts/AlertsReadModelUnitTests.cs:97`. Journal fixture: `tests/cs/server.tests/Fixtures/journal.json`.

Web (`apps/ts/web/src`, tests `tests/ts/web`, `tests/ts/web.e2e`)
- `lib/lot-tiles.ts:127-189` (unknown tile: label, duration source, spoken label), `lib/lot-detail.ts:142-147,176-208` (hero notes; `hubSilentNoId` fallback), `lib/server/lot-detail.ts:70-74` (guesses the first Hub: remove), `lib/garden.ts`, `routes/(app)/garden/+page.svelte:58-67,124-133` (announce-once pattern, notice slot), `lib/components/InlineNotice.svelte`, `lib/announcer.svelte.ts`.
- `lib/devices.ts` (`statusOf` :109, `lastSeenText` :114, `devicesAccessOf` :62), `lib/server/devices.ts:126-147` (`deviceAction`, 403 mapping), `routes/(app)/devices/+page.svelte` (:116-147 rows, :185 per-row notice), `+page.server.ts`. Form exemplar: `lib/thresholds.ts` (`validityOf`), `lib/server/thresholds.ts:96-175`.
- `lib/alerts.ts:103-117` (Health row: icon, target `/devices`), `lib/components/AlertRows.svelte`, `lib/i18n/en.json:39` (`alerts.title.silent`), `lib/i18n/format.ts` (`formatDuration`, `formatWhen`), `lib/live/rule.ts:31-47`.
- Guards: `tests/ts/web/coverage.test.ts` (UX-DR ids per story), `no-hard-coded-copy.test.ts`, `copy.test.ts`, `styles.test.ts`.
- `tests/ts/web.e2e/fixtures/fake-idp.ts:231-258` (fake Server: `FakeDevice`, `FakeAlert`, no Silence Window route), `specs/helpers.ts`, theme loop with `toHaveScreenshot('name-${theme}.png')` in `specs/lot-status.spec.ts`, `devices.spec.ts`, `alerts.spec.ts`.
- `tests/cs/web.e2e/MorningNudgeFixture.cs`, `MorningNudgeJourneyTests.cs` -- real stack with `Testing:ManualClock`, `SimulatedDevice` (`tests/cs/device-simulator`), `POST /testing/clock`; the fixture is shared, so the clock only moves forward and a Hub must heartbeat after a jump to stay heard.

Kotlin core and Android (`packages/kt/core/src/commonMain/.../core` = CORE, `apps/kt/android/src/main/kotlin/.../android` = AND)
- `CORE/lots/Lots.kt`, `LotsEngine.kt:715-722`, `LotsOverview.kt:406-488` (unknown tile, `LotDuration.between` :37), `LotDetail.kt:165-177,495-518` (hero notes), `LotsSnapshot.kt`, `LotDetailSnapshot.kt`.
- `CORE/devices/Devices.kt` (`HubSummary` :12, `NodeSummary` :23, role gates :131-142), `DevicesEngine.kt` (`moveNode` :162 as the action pattern), `DevicesSnapshot.kt:26-51`.
- `CORE/alerts/Alerts.kt` (`AlertGroup.Health`, `AlertCondition.Silent`, target `Devices` :195-200, non-null `lotId`/`lotName` :110-120), `AlertsSnapshot.kt`.
- `packages/kt/core/src/iosMain/.../{lots,devices,alerts}/Ios*.kt` -- iOS wrappers.
- `AND/ui/sites/GardenScreen.kt:194-198` (notice slot), `LotDetailCopy.kt:73-84`, `AND/ui/components/InlineNotice.kt`, `AND/ui/devices/DevicesScreen.kt` (`HubRow` :464, `NodeRow` :209, `MoveNodeDialog` :341, `manage` gate :158), `AND/ui/alerts/AlertsCopy.kt:64`, `AND/ui/format/Formats.kt`, `res/values/strings.xml` (:61, :202-203, :490-491).
- Tests: `tests/kt/core` (`DevicesEngineTest`, `LotsOverviewTest`, `LotDetailTest`, `AlertsEngineTest`, `ColdframeApiTest`, snapshot tests), `tests/kt/android/test/.../SnapshotTest.kt` (Roborazzi, light and dark, baselines in `tests/kt/android/snapshots`), `DevicesScreenTest`, `AlertsScreenTest`, `StringsTest` (iOS parity, no upper case), `CoverageTest`.

iOS (`apps/swift/ios/Sources/ColdframeIOS` = IOS, `apps/swift/ios/App`)
- `IOS/LotsPresentation.swift`, `LotDetailPresentation.swift:61-65,154-178`, `DevicesPresentation.swift` (:94, :125, :201, :241-300), `AlertsPresentation.swift`, `L10n.swift`, `Resources/Localizable.xcstrings`, `Formats.swift`.
- `IOS/UI/SitesViews.swift:300-306` (notice slot), `Components.swift:67-116` (`InlineNotice`, one `%@`), `DevicesViews.swift` (:166-227 rows, :71 sheet, :273 `moveSheet`), `LotDetailViews.swift`, `AlertsViews.swift`.
- `App/CoreDevicesService.swift`, `CoreLotsService.swift`, `CoreLotDetailService.swift`, `CoreAlertsService.swift` -- map Kotlin snapshots to presentations field by field.
- Tests `tests/swift/ios/ColdframeIOSTests`: presentation tests and `CatalogueTests` (parity, `coveredUxDrs`) run on Linux in the `swift:6.3.3` container; `RenderTests.swift` (light and dark render checks), `IOS/UI/*` and `App/*` compile only on the macOS CI jobs.

## Tasks & Acceptance

**Execution:**
- [x] `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md` -- add `Lot.silentHubId`, `Lot.silentSince`, `LotList.silentHubs` (`SilentHub {deviceId, silentSince}`), `DeviceListItem.silentSince|silenceWindowSeconds|silenceWindowOverridden`, `HealthAlert {id, kind, deviceId, deviceKind, openedAt, lotId?, lotName?, silentSince?, closedAt?, reason?}`, `AlertList.healthAlerts`, operation `setDeviceSilenceWindow` (`PUT /sites/{siteId}/devices/{deviceId}/silence-window`, Administrator, `SetSilenceWindowRequest {windowSeconds?}` → `SilenceWindow {windowSeconds, overridden}`, 400/401/403/404); correct the `AlertKind` and `openCount` descriptions; examples for each -- the contract every client mirrors.
- [x] `packages/ts/api-client/src/schema.ts` -- regenerate.
- [x] `packages/cs/contracts/Alerts/*`, `Sites/SiteGrains.cs`, `Notifications/Notifications.cs`, `Devices/DeviceEvents.cs`, `apps/cs/server/Alerts/AlertGrain.cs`, `AlertState.cs`, `Identity/UserGrain.cs`, `Devices/DeviceGrain.cs` -- carry nullable Device kind and silent-since from the Device grain's open through `AlertOpened`, `SiteAlert` and `NotificationEntry`, and on `device.silence-started`.
- [x] `apps/cs/migrations/Migrations/M20261012090000AddSilenceColumns.cs` (new) -- `alerts.device_kind`, `alerts.silent_since`, `lot_status_alerts.silent_since`, `devices.silent_since`, `devices.silence_window_seconds`; rebuild the projections that need old events replayed.
- [x] `apps/cs/server/Devices/DevicesProjector.cs`, `DevicesReadModel.cs` -- project silence started, ended and window set; list silent-since, effective window and whether it is overridden; touch the `devices` hint.
- [x] `apps/cs/server/Alerts/AlertsProjector.cs`, `AlertsReadModel.cs` -- store the two new columns; a Health query (kind other than Threshold, left join on Lots, open newest first then closed in the last 7 days, at most 200); the open count covers every kind.
- [x] `apps/cs/server/Lots/LotsProjector.cs`, `LotsReadModel.cs` -- `silentHubId` and `silentSince` per unknown Lot; the Site's silent Hubs for the list.
- [x] `apps/cs/server/Edge/EdgeApi.cs`, `EdgeProblems.cs` -- the new response members; `SetDeviceSilenceWindowAsync` mapping Set/Unchanged → 200, OutOfRange and bad bodies → 400, NotFound → 404 with a title that fits a Hub too; `healthAlerts` only without `cursor`.
- [x] `apps/cs/server/Notifications/Push/PushText.cs`, `PushText.en.json`, `PushContent.cs` -- the silent titles, bodies, Reminder bodies and summary lines of the matrix; neutral text when kind or silent-since is missing.
- [x] `apps/cs/server/Testing/TestingHostingExtensions.cs` -- `POST /testing/devices/{deviceId}/wake`, firing the Device's silence evaluation as its reminder does -- the journey cannot wait a real minute.
- [x] `tests/cs/server.tests/**`, `tests/cs/server.integration/**` -- failing first: `SilenceWindowEndpointTests` (every endpoint row of the matrix, journal unchanged on 403/400/404), the matrix sample, lots, devices and alerts list tests for the new members, hints for a silent Device and a changed window, `PushTextTests` for every copy row, `Fixtures/journal.json`, and the three tests that pinned "not listed".
- [x] `apps/ts/web/src/lib/**`, `routes/(app)/garden/**`, `routes/(app)/devices/**`, `routes/(app)/alerts/**`, `lib/i18n/en.json` -- tile and hero from `silentSince`/`silentHubId`; the Hub notice from `silentHubs` (announce once); silent Device rows; the Silence Window row action (current value, number with a unit of min, h or d, Save, "Use default" when overridden) for Administrators and Owners; Health rows from `healthAlerts` with the titles of the matrix, linking to `/devices#device-{id}` with row anchors; `devices` hints refresh the overview.
- [x] `tests/ts/web/**`, `tests/ts/web.e2e/fixtures/fake-idp.ts`, `specs/*.spec.ts` -- unit tests per matrix row and guard updates (UX-DR18, 81, 84, 117, 31); fake Server fields and route; light and dark screenshots of the overview with the Hub notice, Devices with a silent Hub and Node and the open Silence Window form, and Alerts with Node and Hub Health rows; Member sees no control.
- [x] `tests/cs/web.e2e/SilentNodeJourneyTests.cs` (new; share the fixture and helpers of `MorningNudgeJourneyTests`) -- UJ-3 on the real stack with the manual clock.
- [x] `packages/kt/core/src/commonMain/**`, `src/iosMain/**`, `tests/kt/core/**` -- DTOs, API call, models, snapshots and engine action (`setSilenceWindow`) for all of the above; the Hub short name and the titles' inputs live in the core so both apps agree.
- [x] `apps/kt/android/src/main/**`, `tests/kt/android/**` -- Hub notice, hero Hub ID, silent rows, Silence Window dialog (Administrator and Owner), Health titles; strings; Roborazzi baselines in light and dark for the same three surfaces.
- [x] `apps/swift/ios/Sources/ColdframeIOS/**`, `apps/swift/ios/App/Core*Service.swift`, `tests/swift/ios/**` -- the same on iOS: presentations, catalogue, views, adapters; presentation and catalogue tests; render tests in light and dark for the new states.
- [x] `apps/cs/README.md`, `apps/ts/web/README.md`, `apps/kt/README.md`, `apps/swift/README.md`, `packages/kt/core/README.md` -- replace "stored, not listed" and the gaps they list with what now exists.

**Acceptance Criteria:**
- Given the working tree, when `packages/proto/check-compat.sh` runs against `origin/main`, then it reports no breaking change, and the Kotlin `OpenApiContractTest` and the TypeScript staleness test pass.
- Given a Site whose Node on Lot "Beans" reported with the simulator and then stopped, and a Hub that keeps heartbeating, when the Server clock is moved 6 h on and the Device is woken, then in the browser the Beans tile is the unknown tile with "6 h", stands before every OK tile, in the light and in the dark theme, and the summary delivered when the User's Notification Window opens contains the line "Beans: Node silent for 6 h".
- Given a Member on web, Android and iOS, when Devices renders with a silent Device, then the silence and last seen are shown and no Silence Window control exists; given an Administrator or Owner, then the control exists, saves a valid value and returns to the default.
- Given the Playwright, Roborazzi and iOS render suites, when they run, then the new states pass in light and dark themes.
- Given an Alert, a Lot or a Device journaled before this story (no silent-since, no Device kind), when lists are read and notifications built, then every response validates and the text falls back as the matrix says.

## Spec Change Log

## Review Triage Log

### 2026-10-12 — Review pass
- verdicts: 8 findings — high 0, medium 1, low 7
- findings:
  - `[medium]` `[patch]` In the Kotlin core a move or an unassign started while a Silence Window change was in flight read the list again and dropped the change's answer, so a refusal showed no notice. Fixed: the core runs one Device change at a time (`DevicesEngine.kt`), with a test.
  - `[low]` `[defer]` Save closes the Android dialog and the iOS sheet although the core sent nothing. Deferred as DW-97.
  - `[low]` `[defer]` On iOS a refused entry leaves the range error under the row after Cancel. Deferred as DW-98.
  - `[low]` `[defer]` A window that is not a whole number of minutes is shown rounded and rewritten by Save. Deferred as DW-99.
  - `[low]` `[defer]` A silence open at deploy time gets the Alert's opening time as its silent-since. Deferred as DW-100.
  - `[low]` `[defer]` The migration replays the devices projection over live rows. Deferred as DW-101.
  - `[low]` `[defer]` The open count can exceed the Health Alerts the list shows. Deferred as DW-102.
  - `[low]` `[defer]` The Android dialog state is lost on rotation. Deferred as DW-103.

## Design Notes

- **Why `healthAlerts` and not a relaxed `Alert`.** A Hub's Alert has no Lot and no quantity, and making `quantity`, `lotId` or `lotName` optional is an oasdiff error (`response-property-became-optional`, verified with the pinned 1.32.1). A second list is additive, and an older client keeps decoding `alerts`. This settles DW-91 without touching `Alert`. Health Alerts are bounded by the number of Devices, so the list is not paged.
- **Why silent-since rides on the Alert.** The grain's clock start can be a Server start or a Hub recovery, which is not when the Device went quiet. Notification channels may not call other grains, and three read models need the same value, so the Device grain states it once at open. "Silent for 6 h" at the moment a default Node Alert opens then follows from the definition.
- **`silentHubs` on the Lots list.** The notice must also show for a silent Hub with no Lot behind it, and mobile loads only Lots for the overview. One optional array on `LotList` serves all three clients; `Lot.silentHubId` settles the Hub-ID half of DW-73.
- **Silence Window control.** The UX names the action but not the control. A number with a unit covers the whole range (the UX journey "lengthens the Beans Node's Silence Window to 8 h") without a preset list to maintain.
- **Web time format.** The web locale prints "1:05 AM" and "~40%"; the spec's "01:05" and "40 %" are the copy's shape, not a new formatter.
- **Not verifiable on this host:** SwiftUI views, the iOS App target and `RenderTests` compile only on the macOS CI jobs (DW-93).

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror && dotnet format --verify-no-changes --no-restore` -- expected: no error, no change.
- `dotnet test --no-build --project tests/cs/server.tests` -- expected: all pass.
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build --project tests/cs/server.integration` -- expected: all pass, none skipped.
- `packages/proto/check-compat.sh` (tools from `packages/proto/install-tools.sh <dir>` on `PATH`) -- expected: no breaking change.
- `pnpm --filter @coldframe/openapi run check && pnpm -r typecheck && pnpm -r lint && pnpm --filter @coldframe/web-tests test && pnpm --filter @coldframe/api-client-tests test` -- expected: pass (use the package's real test filter name if it differs).
- `pnpm --filter @coldframe/web-e2e test` -- expected: pass, with the new screenshots committed.
- `pnpm --filter @coldframe/web build && ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build --project tests/cs/web.e2e` -- expected: both journeys pass.
- `ANDROID_HOME=$HOME/Android/Sdk ./gradlew check` -- expected: pass, new Roborazzi baselines recorded with `:android:recordRoborazziDebug` and committed.
- `podman run --rm --security-opt label=disable -v "$PWD":/work -w /work docker.io/library/swift:6.3.3 sh -c 'swift build && swift test && swift format lint --strict -r .'` -- expected: pass.

**Manual checks (if no CLI):**
- The macOS CI jobs `swift` and `ios` on the pull request are the only compile and render check of the SwiftUI and App-target edits; read their result before the merge.
