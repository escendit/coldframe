---
title: 'Story 8.2: Pause and resume in the apps'
type: 'feature'
created: '2026-10-13'
baseline_revision: c9fe6a79956d2109e39e27109bb4075c93aaa43e
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-8-context.md'
  - '{project-root}/apps/cs/README.md'
  - '{project-root}/docs/quickstart.md'
warnings: ['oversized']
deferred:
  - summary: >-
      The iOS bridge and the App/ adapters for Pause are compiled only; no test runs them.
    evidence: |-
      IosLots.watch, IosLotDetail.watch and IosDevices.watch now combine the engine state with its pause state,
      IosPause is new, and CorePauseService and the Core*Service adapters map each new snapshot field by position.
      tests/kt/core has commonTest and jvmTest only, CI compiles the iOS targets without running anything, and the
      App/ target is only built. Swapping canPause and canResume in CoreDevicesService.presentation, or dropping
      the pause state from IosLots.watch, compiles and passes every suite. The same holds for every earlier
      story's iOS adapters (DW-93); closing it needs an iosTest source set or an app-hosted test target.
    location: >-
      packages/kt/core/src/iosMain/kotlin/com/escendit/coldframe/core/sites/IosPause.kt, apps/swift/ios/App/CoreLotsService.swift
    severity: medium
  - summary: >-
      The paused overview does not say who paused the Site and when ("Paused by Simon (Owner) on 2 Nov.", UX-DR83).
    evidence: |-
      UX-DR83 and EXPERIENCE.md (State Patterns, All paused) give the line as "No Alerts are sent while the Site
      is paused. Paused by <name> (<Role>) on <date>." Story 8.2's acceptance text asks for the first sentence
      only, and Story 8.1 stores no attribution by decision: site.paused carries the end, a revision and a
      timestamp, and SitePause exposes paused and pausedUntil. Showing it needs the acting User on the event and
      a member on the Site read.
    location: >-
      packages/cs/contracts/Sites/SiteEvents.cs (SitePaused), apps/cs/server/Edge/EdgeApi.cs (ToSiteResponse)
    severity: low
  - summary: >-
      Calibrate on a paused Device still offers no Resume and keeps its Story 5.2 wording; Device rows have no
      swipe-to-Pause.
    evidence: |-
      UX-DR87 asks for "<ID> is paused, so it sends no Readings. Calibrate after the Pause ends." with Resume
      where the Pause is the Device's own; the three clients show "This Node is paused. Readings resume after the
      Pause ends…" and no control (CalibrateScreen.kt, CalibrateViews.swift, calibrate/+page.svelte). The resume
      endpoint and the core PauseEngine now exist, so the branch can be completed. Swipe on a Device row
      (UX-DR31) is still marked [ASSUMPTION] in EXPERIENCE.md. Neither is in Story 8.2's acceptance criteria.
    location: >-
      packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/calibrate/CalibrateEngine.kt, apps/ts/web/src/routes/(app)/garden/[lotId]/calibrate/+page.svelte
    severity: low
---

<intent-contract>

## Intent

**Problem:** Story 8.1 gave the Server four pause endpoints, but no app can call them. The web, iOS and Android apps already draw a paused Lot (tile, headline, hero note) from `Lot.pausedBy`, yet have no Pause sheet, no working Pause or Resume control, and nothing that says whether the Site itself, a Hub or a Node without a Lot is paused, because `Site` and `DeviceListItem` carry no pause members (DW-112).

**Approach:** Give Pause a read surface on the Server (`Site.paused` / `pausedUntil`, `DeviceListItem.pausedBy` / `pausedUntil`), then build the Pause sheet and the Pause and Resume controls on the three entry points (Site menu, Lot detail, Devices row) in web, iOS and Android, with the paused-Site line on the overview and the Member read-only state. Prove the winter journey (UJ-4) end to end on the real stack with the manual clock, in light and dark.

## Boundaries & Constraints

**Always:**
- Clients only render what the Server says: paused state comes from `Lot.status` / `pausedBy` / `pausedUntil`, `Site.paused` / `pausedUntil` and `DeviceListItem.pausedBy` / `pausedUntil`. No client computes or remembers "paused".
- New contract members are optional (the `contracts` CI job fails a newly required property) and omitted, never `null`, when there is nothing to say. Read models project them from the journaled events (`site.paused`, `site.resumed`, `device.paused`, `device.resumed`); pause state itself stays where AD-8 put it.
- A read made right after a pause or resume answers shows it for the thing addressed: `GET /sites` and `GET /sites/{siteId}` after a Site call, the Devices list and the Lot after a Device call. Devices reached by a Site Pause follow through delivery; clients re-read on the live hint or their next refresh.
- Pause and Resume controls are for Administrators and Owners only and are hidden, never disabled, for Members. The Site menu items alone are disabled with "Needs your Server" in stale mode (already built).
- The Pause sheet: native sheet (iOS), `ModalBottomSheet` (Android), DS Modal (web); Cancel and a primary button naming the result ("Pause Home garden", "Pause 7C19"); scope as a Segmented choice; optional "Until" date with helper "Leave empty to pause until someone resumes."; it stays open and says why when the Server refuses.
- The date sent is the calendar date the User picked (`until: YYYY-MM-DD`); the Server resolves it. Shown ends are formatted from `pausedUntil` with the existing day formatters.
- Purple and the pause icon mean paused and nothing else. Copy follows the voice rules (no "OK" for a paused Lot, no exclamation marks, no "successfully"); every string goes through each platform's catalogue, and Android and iOS keys stay in parity.
- Web: the browser never calls the Server; pause and resume are SvelteKit form actions (AD-14).
- Test-first, per platform: failing tests before the code they cover. Snapshot tests in light and dark for every new or changed paused surface.

**Never:**
- No "Paused by <name> (<Role>) on <date>" line: Story 8.1 stores no attribution and this story's acceptance text asks only for the no-Alerts line.
- No replay of the `identity` projector from position 0: until it caught up it would show revoked Memberships and deleted Sites as live, and the access rules read that table.
- No change to pause semantics, to the four pause endpoints' requests or answers, or to who may call them. No pause for a Hub's relayed Nodes (DW-111 stays open).
- No paused-Site notice on the Add a Hub and Add a Node outcome screens (DW-58), no Resume button inside Calibrate, no swipe-to-Pause on Device rows: not in this story's acceptance criteria.
- No client-side status sort or recomputation; no new modal level on top of an open modal.
- Do not write `sprint-status.yaml`. Do not edit planning artifacts.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Site read, paused | `GET /sites/{s}` and `GET /sites` after `PUT /sites/{s}/pause {"until":"2027-03-01"}` | the Site carries `paused: true` and `pausedUntil` (UTC instant); after resume `paused: false`, no `pausedUntil` | No error expected |
| Devices list, paused | Hub paused by itself; Node paused by its Site with an end | Hub row `pausedBy: ["device"]`; Node row `pausedBy: ["site"]`, `pausedUntil`; an unpaused Device has neither member | No error expected |
| Open from Site menu | Administrator, Site not paused | sheet with scope Whole Site (the only segment), primary "Pause Home garden" | No error expected |
| Open from Lot detail or a Devices row | Administrator, Device 7C19 not paused by itself | sheet with This Device (selected) and Whole Site; primary follows the scope: "Pause 7C19" / "Pause Home garden" | No error expected |
| Save, no date | Whole Site, Until empty | `PUT /sites/{s}/pause {}`; sheet closes; headline "Paused", line "No Alerts are sent while the Site is paused.", every tile PAUSED BY SITE | No error expected |
| Save with a date | Until 1 Mar 2027 | request `{"until":"2027-03-01"}`; headline "Paused until 1 Mar" | No error expected |
| Date refused | Server answers 400 | sheet stays open, date field marked: "Choose a date after today." | nothing changed |
| Role lost meanwhile | Server answers 403 | "You can't change this on Home garden. Ask an Owner or Administrator." | nothing changed |
| Server unreachable or 404 | transport failure, unknown Device | the surface's existing failure notice with its retry; the sheet keeps the entries | nothing changed |
| Resume the Site | Site menu "Resume Home garden" | one tap, no sheet; `DELETE /sites/{s}/pause`; tiles return to live statuses | as above |
| Resume a Device | Lot detail or Devices row, `pausedBy` has `device` | one tap; `DELETE …/devices/{d}/pause`; still paused by the Site if `site` remains | as above |
| Paused by the Site only | Lot detail or Devices row, `pausedBy: ["site"]` | no Resume for the Device; "Resume the Site to resume this Node" (Hub row: "… this Hub"); Pause for the Device stays available | No error expected |
| Member | paused Site, paused Lot, Devices | paused state as for anyone; no Pause or Resume anywhere; one Inline notice "Only Owners and Administrators can resume it." on the paused overview and paused Lot detail | No error expected |
| Stale mode | Server unreachable, last-good data | Site menu pause item disabled with "Needs your Server" | No error expected |

</intent-contract>

## Code Map

Server (read surface, DW-112):
- `packages/openapi/coldframe.openapi.json` -- `Site` `:1092`, `DeviceListItem` `:1642` (examples `:1686`), pause schemas `:1838-1873`, `LotPauseSource` `:1134`. `packages/openapi/README.md` operations table and Pause convention.
- `apps/cs/server/Edge/EdgeApi.cs` -- `SiteResponse` `:37`, built at `:748,769,782,814`; `ListSitesAsync` `:763`, `GetSiteAsync` `:772`; `DeviceListItemResponse` `:365`, `ToDeviceListItem` `:1898`; pause handlers `:2188-2275`; `RenameSiteAsync` `:809` (grain brings the identity projection up to date before answering: the read-your-writes model for Site pause).
- `apps/cs/server/Identity/IdentityProjector.cs:68-109` (no case for `SitePaused` / `SiteResumed`), `IdentityReadModel.cs:13-37` (`SiteAccessView`, `MemberSiteView`, `SelectSql`, `ListSql`; other readers: `Edge/SiteAccessAuthorization.cs:57`, `Notifications/Push/PushLookups.cs:82`). Events: `packages/cs/contracts/Sites/SiteEvents.cs:160-188`.
- `apps/cs/server/Devices/DevicesProjector.cs:97-164` (ignores `DevicePaused` / `DeviceResumed`; `ExecuteAsync` `:169-185` types every null parameter as bigint), `DevicesReadModel.cs:18-99` (`DeviceView`, `ListSql`, ordinal reader). Events: `packages/cs/contracts/Devices/DeviceEvents.cs:118-177`.
- Reuse: `apps/cs/server/Lots/LotStatusRule.cs:138,150` (`PausedUntil`, `SourceNames`), `LotsProjector.cs:94-105,527` (per-source columns `device_paused`, `device_paused_until`, `site_paused`, `site_paused_until`).
- `apps/cs/migrations/Migrations/` -- FluentMigrator, forward-only; newest `M20261013090000AddAlertBatteryPercent`; precedent `M20261012090000AddSilenceColumns.cs` (nullable columns on `devices`, rows kept, `devices` checkpoint deleted so the projector replays). Recipe `apps/cs/README.md:56`; gap notes to replace `:999-1000`, `:1502-1515`.
- Tests to imitate: `tests/cs/server.integration/Devices/DevicesListTests.cs:161`, `PauseEndpointTests.cs:34-257`, `Edge/ListSitesTests.cs:18`, `Edge/EdgeApiTests.cs:17`, `MigrationTests.cs:33-43,160-210`; `tests/cs/server.tests/Devices/DeviceLivenessTests.cs:52-113`, `Edge/SiteCreationResponseTests.cs`.
- `apps/cs/server/Testing/TestingHostingExtensions.cs:93-144` -- `/testing/clock` (absolute, forward only), device, user and Site wakes (DW-113: the Site wake has no caller).

Kotlin core (shared by Android and iOS), `packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/`:
- `api/ApiDtos.kt:12` (`SiteDto`), `:55-56` (Lot pause members), `:175` (`DeviceListItemDto`); `api/ColdframeApi.kt:223-235` (`setSilenceWindow`: the call to imitate), `:404` (`failureOf`). `tests/kt/core/jvmTest/.../api/OpenApiContractTest.kt:58,170-190,296,303` pins the DTOs to the contract.
- `sites/SiteMenu.kt:23-34` (`items(role, pauseAvailable, stale, paused)`); callers `lots/LotsOverview.kt:590`, `sites/SitesSnapshot.kt:64` leave `pauseAvailable` false. Headline rule `lots/LotsOverview.kt:98-166`.
- `lots/LotDetail.kt:199-218,386-390,437,497-501` (hero, `can*` gates, hidden when stale); `devices/Devices.kt:104,134,298-312`, `devices/DevicesEngine.kt:239-279` (write, working id, failure, refresh: the engine pattern); snapshots `lots/LotsSnapshot.kt`, `LotDetailSnapshot.kt`, `devices/DevicesSnapshot.kt`; iOS bridge `iosMain/.../Ios{Lots,LotDetail,Devices}.kt`.

Android, `apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/`:
- `ui/sites/SiteMenu.kt:41,97-98` (Pause and Resume clicks do nothing), `GardenScreen.kt:103,139-142,197,384`, `OverviewCopy.kt:88-92`, `LotDetailScreen.kt:118,233-296,334-409`, `LotDetailActions.kt:13`, `ui/devices/DevicesScreen.kt:261,384-409,310-337,424,738,772`, `DevicesActions.kt:14,47`.
- Imitate: `ui/sites/SiteSwitcherSheet.kt:54-70` (the only `ModalBottomSheet`; content is its own composable), `ui/components/SegmentedChoice.kt`, `Buttons.kt:33,48`, `ui/notifications/NotificationWindowControl.kt:216-248` (Material picker in a dialog; no date picker exists yet), `ui/format/Formats.kt` (`day`).
- `src/main/res/values/strings.xml:92,120-122,188-240,275,492-495`. Tests in `tests/kt/android/test/kotlin/com/escendit/coldframe/android/`: `SnapshotTest.kt:131,878,985,1114` (Roborazzi, baselines `tests/kt/android/snapshots/`), `CoverageTest.kt:9,111` (`storyIds`, `iosIds`), `StringsTest.kt:78` (parity with iOS), `SourceScanTest.kt`, fixtures `LotFixtures.kt:100-116`.

iOS, `apps/swift/ios/Sources/ColdframeIOS/` (no Swift API layer; `App/Core*Service.swift` adapters map core snapshots field by field):
- `SitesPresentation.swift:175-189,310,329`, `UI/SitesViews.swift:240,365,431,573-613` (`SiteMenu` acts on Site settings only), `LotsPresentation.swift:535-579,697-710`, `LotDetailPresentation.swift:66-176,674-688`, `UI/LotDetailViews.swift:9-33,139-150,282-336` (hero is one accessibility element: no button inside it), `DevicesPresentation.swift:156-643`, `UI/DevicesViews.swift:92-108,211,309-353,436-551` (`SilenceWindowSheet` `:478`: the sheet to imitate), `UI/Components.swift:201`, `L10n.swift`, `Resources/Localizable.xcstrings`, `Formats.swift:51`.
- Tests `tests/swift/ios/ColdframeIOSTests/`: `Support.swift:172-210,451-470`, `CatalogueTests.swift:103-135` (`coveredUxDrs`), `RenderTests.swift:64-69,480-496,694-702` (light and dark by `arguments: [false, true]`; macOS CI only), `LotDetailPresentationTests.swift:338` (asserts no Pause action today).

Web, `apps/ts/web/src/`:
- `lib/site-menu.ts:27-36`, `lib/components/SiteMenu.svelte:95-100` (`href === null` renders a disabled span), `AppHeader.svelte:33`, `lib/server/shell.ts:161` (`currentSite`), `lib/garden.ts:41-63`, `lib/components/SiteSummaryHeader.svelte:27,66-68`, `routes/(app)/garden/+page.svelte:37-40,136,157`.
- `routes/(app)/garden/[lotId]/+page.svelte:34,83-119,150-157` (no `actions` in its `+page.server.ts`), `lib/lot-detail.ts:150-178`; `routes/(app)/devices/+page.svelte:76-95,136-147,178-243,265-358,366-383`, `lib/devices.ts:67,176,182`, `lib/server/devices.ts:37,143,173`, `lib/server/sites.ts:44-69` (`call`, status to error kind).
- Primitives `lib/components/`: `Modal.svelte` (confirm only, no body slot), `SegmentedChoice.svelte`, `TextInput.svelte` (no `date` type), `InlineNotice.svelte`, `Button.svelte`, `modal-stack.ts`. Copy `lib/i18n/en.json:87-88,126,223,225`, `lib/i18n/format.ts:80`. Generated types `packages/ts/api-client/src/schema.ts:197-215,457-475,494,1028,1290-1345`.
- Unit tests `tests/ts/web/` (Vitest, `svelte/server` render): `garden.test.ts:119-121` pins "Pause is hidden until it exists"; `coverage.test.ts` (`storyIds`), `copy.test.ts`, `no-hard-coded-copy.test.ts`, `fakes.ts`.
- `tests/ts/web.e2e/` (Playwright against an in-memory fake Server): `fixtures/fake-idp.ts:72-73,234,758-797`, `specs/helpers.ts:40-188`, `specs/silence.spec.ts:260-399` (closest template), `specs/lot-status.spec.ts:141-177`; baselines `specs/*.spec.ts-snapshots/*-{light,dark}-chromium-linux.png`.
- `tests/cs/web.e2e/` (Microsoft.Playwright on the real stack with the manual clock): `SilentNodeJourneyTests.cs:136-206`, `JourneyTests.cs:140-255`, `MorningNudgeFixture.cs:67`.

## Tasks & Acceptance

**Execution:**
- [x] `tests/cs/server.integration/Devices/DevicesListTests.cs`, `Edge/ListSitesTests.cs`, `Edge/EdgeApiTests.cs`, `MigrationTests.cs`, `tests/cs/server.tests/Devices/DeviceLivenessTests.cs`, `Edge/` site response tests -- failing tests first for the two matrix read rows, read-your-writes, and the migration -- DW-112.
- [x] `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md`, `packages/ts/api-client/src/schema.ts` (regenerated), `packages/ts/api-client/src/index.ts` -- optional `paused`, `pausedUntil` on `Site`; optional `pausedBy`, `pausedUntil` on `DeviceListItem`; aliases for the pause types.
- [x] `apps/cs/migrations/Migrations/M20261014090000AddPauseReadColumns.cs` (new) -- pause columns on `identity_sites` and `devices` (per source, as `lot_status_devices` has them); delete the `devices` checkpoint and keep its rows (the silence migration's way); fill `identity_sites` from the newest `site.paused` / `site.resumed` of each Site stream in `journal_events` -- Pauses journaled before this change show too.
- [x] `apps/cs/server/Identity/IdentityProjector.cs`, `IdentityReadModel.cs`, `apps/cs/server/Devices/DevicesProjector.cs`, `DevicesReadModel.cs`, `apps/cs/server/Edge/EdgeApi.cs`, grain call sites as needed -- project the four events, expose the members, live hints for `Sites` and `Devices`, read-your-writes as the Always list states.
- [x] `apps/cs/README.md`, `packages/cs/README.md` -- replace the DW-112 gap notes with what now holds.
- [x] `packages/kt/core/.../api/ApiDtos.kt`, `ColdframeApi.kt`, `tests/kt/core/jvmTest/.../OpenApiContractTest.kt`, `tests/kt/core/commonTest/.../api/ColdframeApiTest.kt` -- pause DTOs, the new members, four calls.
- [x] `packages/kt/core/.../sites/`, `lots/`, `devices/` (engines, state, `SiteMenu`, snapshots, `iosMain` bridges) with `tests/kt/core/commonTest/` (`SiteMenuTest`, `LotsOverviewTest`, `LotDetailTest`, `LotDetailEngineTest`, `DevicesEngineTest`, snapshot tests) -- Site and Device pause and resume as engine actions with working, refused-date, forbidden and failed outcomes; `pauseAvailable` on, `paused` from the Site; `canPause` / `canResume` gates; paused state on Hub and Node summaries; the paused-Site line and Member notice as state.
- [x] `apps/kt/android/.../ui/pause/PauseSheet.kt` (new), `ui/sites/{SiteMenu,GardenScreen,OverviewCopy,LotDetailScreen,LotDetailActions}.kt`, `ui/devices/{DevicesScreen,DevicesActions}.kt`, `ColdframeRoot.kt`, `MainActivity.kt`, `res/values/strings.xml` -- sheet, three entry points, Resume, paused rows, notices.
- [x] `tests/kt/android/test/.../{SnapshotTest,SitesScreensTest,LotOverviewTest,LotDetailScreenTest,DevicesScreenTest,CoverageTest}.kt`, fixtures, `tests/kt/android/snapshots/` -- behaviour tests and light and dark baselines for the sheet (both scopes, refused date), paused Site overview, paused Devices rows, Lot detail with Resume, Member views; add 46, 70, 83 to the coverage lists.
- [x] `apps/swift/ios/Sources/ColdframeIOS/PausePresentation.swift` (new), `{Sites,Lots,LotDetail,Devices}Presentation.swift`, `L10n.swift`, `Resources/Localizable.xcstrings`, `UI/PauseViews.swift` (new), `UI/{SitesViews,LotDetailViews,DevicesViews}.swift`, `App/Core{Lots,LotDetail,Devices}Service.swift` -- the same on iOS.
- [x] `tests/swift/ios/ColdframeIOSTests/PausePresentationTests.swift` (new), `{Sites,Overview,LotDetail,Devices}PresentationTests.swift`, `CatalogueTests.swift`, `Support.swift`, `RenderTests.swift` -- presentation tests, catalogue parity, light and dark render tests for the same surfaces.
- [x] `apps/ts/web/src/lib/server/pause.ts` (new), `lib/pause.ts` (new), `lib/components/PauseModal.svelte` (new) or a body snippet on `Modal.svelte`, `TextInput.svelte`, `lib/site-menu.ts`, `lib/components/{SiteMenu,AppHeader,SiteSummaryHeader}.svelte`, `lib/garden.ts`, `lib/lot-detail.ts`, `lib/devices.ts`, `lib/server/{shell,devices}.ts`, `routes/(app)/garden/+page.svelte` and `+page.server.ts`, `routes/(app)/garden/[lotId]/+page.svelte` and `+page.server.ts`, `routes/(app)/devices/+page.svelte` and `+page.server.ts`, `lib/i18n/en.json` -- the same on web, through form actions.
- [x] `tests/ts/web/pause.test.ts` (new), `garden.test.ts`, `overview.test.ts`, `lot-detail.test.ts`, `devices.test.ts`, `components.test.ts`, `coverage.test.ts` -- unit tests for every matrix row the web owns.
- [x] `tests/ts/web.e2e/fixtures/fake-idp.ts`, `specs/helpers.ts`, `specs/pause.spec.ts` (new) with baselines; refresh baselines of existing specs that change -- pause routes and members in the fake Server; flows and light and dark screenshots.
- [x] `tests/cs/web.e2e/WinterPauseJourneyTests.cs` (new), `JourneyTests.cs` (`WakeSiteAsync`) -- UJ-4 on the real stack, in light and dark -- the story's end-to-end AC; first caller of the Site wake (DW-113).
- [x] `apps/ts/web/README.md`, `apps/kt/README.md`, `apps/swift/README.md`, `packages/kt/core/README.md`, `docs/quickstart.md`, stale "before Epic 8" comments -- say what now holds.
- [x] `_bmad-output/implementation-artifacts/deferred-work.md` -- mark DW-24, DW-112, DW-113 resolved and the Pause part of DW-72 done, where the work closes them; leave other entries.

**Acceptance Criteria:**
- Given an Administrator or Owner on web, iOS or Android, when they choose Pause from the Site menu, from Lot detail or from a Devices row, then the Pause sheet opens with the scope and the optional "Until" date as the matrix states, and saving calls the matching endpoint.
- Given a paused Site, when the overview renders, then every tile with a Node shows the paused variant, a Device paused only by the Site reads "PAUSED BY SITE" on its tile and its Devices row, the headline reads "Paused until 1 Mar" or "Paused" in paused ink, and the line "No Alerts are sent while the Site is paused." shows.
- Given a paused Site, when an Administrator chooses Resume from the Site menu, or the end date passes and the Site wakes, then the menu offers Pause again and the tiles show their live statuses.
- Given a Member, when they view a paused Site, a paused Lot and the Devices list, then they see the paused state, the one Inline notice, and no Pause or Resume control on any platform.
- Given `WinterPauseJourneyTests` on the real stack with the manual clock, when an Owner pauses the Site until a date through the web sheet, the clock passes the Nodes' Silence Windows, and then passes the date and the Site wakes, then no Health Alert exists while paused, the overview shows the paused state, and after the date the tiles are live and the menu offers Pause; it passes in light and dark.
- Given the snapshot suites (web Playwright screenshots, Android Roborazzi, iOS render tests), when they run, then every new paused surface is covered in light and dark and they pass.

## Spec Change Log

## Review Triage Log

### 2026-10-14 — Review pass
- verdicts: 46 findings — high 0, medium 8, low 28, false 10, maybe-false 0
- findings:
  - `[low]` `[patch]` Blind: the core maps every 400 to `DateRefused`, also without a date and on a Resume — real: `PauseEngine.noticeOf` took no account of the request. Patched: a 400 is `DateRefused` only for a Pause that carried a date, else `Unexpected`; tests for a Pause without a date and both Resumes.
  - `[low]` `[patch]` Blind: a Site-scope 404 is handled as a missing Device on mobile — real: `changed(PauseScope.Device, …)` whatever was asked, and the Device copy. Patched: `changed()` takes the request's scope and a new `PauseNotice.SiteGone` shows the existing Site-gone string on Android and iOS.
  - `[medium]` `[patch]` Blind: a Sites read already in flight overwrites the pause answer — real: `refresh()` returned early, so the older list landed last and the menu offered the wrong item until the next refresh. Patched: a refresh asked while one runs is followed by one more read; test in `SitesEngineTest`.
  - `[low]` `[patch]` Blind: the web Pause modal can be closed while the request runs, and the late refusal shows at the next opening — real (`closed()` cleared `failure`, the callback set it again). Patched: a refusal of an abandoned save is ignored; a success still reads the page again.
  - `[low]` `[patch]` Blind: the refused-date mark stays after the date is edited on the web — real. Patched: the field is marked only while `until` equals the refused date.
  - `[low]` `[patch]` Blind: "Resume the Site to resume this Node" follows different rules on web and mobile — real: core `resumeSiteHint` was `bySite`, the web `bySite && !byDevice`, and the matrix names the line for a Device paused by the Site only. Patched: the core follows the web; tests and Android baselines adjusted.
  - `[low]` `[reject]` Blind: an unknown Pause source reads as not paused on mobile and as paused on the web — the contract's `LotPauseSource` has two values and no third exists; a rule for one would be a guess about DW-111.
  - `[low]` `[reject]` Blind: iOS selects tomorrow when the calendar opens — real and deliberate: a graphical `DatePicker` always has a selection, "Clear date" empties it, and the Server still judges the date; a different default is not a direct correction and cannot be tried on this host.
  - `[low]` `[reject]` Blind: the Site and Devices reads can show a Pause whose end has passed until the next wake — real and bounded by the one-minute reminder (AD-6); `Lot.pausedUntil` has lagged the same way since Story 8.1, and a clock comparison in the mapper would be a second place that decides "paused".
  - `[low]` `[reject]` Blind: every Pause change runs two catch-ups in sequence in the Device's turn — carried from Story 8.1's review of the lots catch-up: the same read-your-writes step the other grains await, on a home garden's roster.
  - `[low]` `[reject]` Blind: the first start after the migration replays `devices` with a burst of hints and a short window in which paused Devices read as not paused — real and brief, the same shape as the silence migration it copies; the projection of the events is tested in `DevicesListTests`.
  - `[low]` `[reject]` Blind: the migration test writes journal payloads by hand, and the backfill casts `endsAt` unguarded — the names it uses (`type_alias`, `payload`, `endsAt`, `site/…`) are the ones the journal fixture and `FixtureJournalReplayTests` pin; the payloads come from the Server's own serializer.
  - `[low]` `[reject]` Blind: `paused` is absent from `POST /sites` and `false` on the reads — as the contract says; the matrix asks for `paused: false` after a resume, the create answer is not built from the projection, and every client reads an absent member as not paused.
  - `[low]` `[patch]` Blind: the Site menu's Resume on the web has no in-flight guard — real: a second choice sent a second `DELETE` (harmless, idempotent). Patched: `ResumeControl.submit()` does nothing while working. The notice dropped on navigation is as intended.
  - `[low]` `[patch]` Blind: spec and ledger not in a reviewable state — the task boxes and logs are this spec's bookkeeping; DW-24's old `progress:` line contradicted its resolution. Patched: that line.
  - `[low]` `[patch]` Blind: hygiene (logger order in `SiteGrain.cs`, an over-long comment line in `DeviceGrain.cs`, an unconditional hint in `IdentityProjector`, a re-tapped day in the Android calendar) — patched: the first two. A Site event is not applied twice (the identity projection is never replayed), and the calendar closes with its field.
  - `[medium]` `[patch]` Edge: Sites refresh in flight when the Site is paused — same defect as the Blind row; patched there.
  - `[low]` `[patch]` Edge: 400 without a date or on a Resume — same defect as the first row; patched there.
  - `[low]` `[reject]` Edge: the Role drops to Member while the sheet is open, and the primary button then does nothing — needs a Role change and a Sites read during an open sheet; a request sent meanwhile gets the 403 notice. A close-on-demotion branch is new behaviour for a rare race.
  - `[medium]` `[patch]` Edge: the Lot detail pause state is kept across Lots — real: `LotDetailEngine` reset its `PauseEngine` only with the Site. Patched: `open()` and `close()` drop it; test shows Lot B starts clean.
  - `[medium]` `[patch]` Edge: Android shows Lot A's failed Resume on Lot B, and Try again resumes A's Node — same defect; patched in the core.
  - `[medium]` `[patch]` Edge: iOS likewise — same defect; patched in the core.
  - `[low]` `[reject]` Edge: a Pause accepted while a Move, Unassign or Silence Window save is in flight drops that answer — two writes within one round trip on the same list; the dropped answer is followed by a fresh read of the rows.
  - `[low]` `[reject]` Edge: a Resume that answers 404 shows nothing because its row is gone — the list is read again and the Device is no longer in it, which is the news.
  - `[low]` `[patch]` Edge: hint beside Resume when paused by both — same as the Blind row; patched there.
  - `[low]` `[patch]` Edge: Cancel or Esc while the Pause runs — same as the Blind row; patched there.
  - `[low]` `[reject]` Edge: the Lot detail modal may reopen by itself when `canPause` turned false while it was open — needs a live update that pauses the same Node during an open modal, and a second one that lifts it.
  - `[low]` `[patch]` Edge: a second Site-menu Resume while the first runs — same as the Blind row; patched there.
  - `[low]` `[reject]` Edge: a Resume posted before hydration that the Server refuses is shown nowhere on Devices and Lot detail — the window before hydration only; the page then reads the Server's state.
  - `[low]` `[reject]` Edge: a failed-Resume notice stays after the Site was resumed elsewhere, and Try again resumes a Site that is not paused — the `DELETE` is idempotent and answers 200; the notice goes with the next navigation.
  - `[medium]` `[patch]` Gap: the projectors' live hints for a Pause change are asserted by no test — as filed. Patched: `ProjectorHintsTests` applies `site.paused`, `site.pause-delivered`, `site.resumed` and five `device.paused` / `device.resumed` events with one repeat.
  - `[low]` `[patch]` Gap: the `PATCH /sites/{siteId}` answer's pause members are read by no test — as filed. Patched: `RenameSiteTests` asserts them for a paused and an unpaused Site.
  - `[medium]` `[patch]` Gap: the Android glue from the screens to the core `PauseEngine` runs in no test — as filed. Patched: `PauseActionsTest` (Android) and `PauseWiringTest` (core).
  - `[medium]` `[defer]` Gap: the iOS bridge and `App/` adapters for Pause are only compiled — as filed, and true of every earlier story's iOS adapters; needs an iOS test source set. In `deferred`.
  - `[false]` `[reject]` Intent: the line reads "No Alerts are sent while the Site is paused." where the story abbreviates it — the story cites UX-DR83 for the line, and that is its string (Design Notes).
  - `[low]` `[defer]` Intent: the attribution "Paused by <name> (<Role>) on <date>" is left out — real against UX-DR83; Story 8.1 stores none by decision and this story's acceptance text does not ask for it. In `deferred`.
  - `[false]` `[reject]` Intent: from the Site menu the sheet offers Whole Site only — there is no Device in that context to offer; from a Device both scopes show.
  - `[false]` `[reject]` Intent: additions beyond the epic's text (Member notice, resume-the-Site hints, paused Hub rows) — each is a cited requirement (UX-DR84, UX-DR27 and UX-DR31, UX-DR30).
  - `[low]` `[defer]` Intent: swipe-to-Pause, Resume inside Calibrate and the paused-Site notice on the outcome screens are excluded — real against UX-DR31, UX-DR87 and UX-DR86, none in this story's acceptance criteria; the outcome notice is DW-58 already, the other two are in `deferred`.
  - `[false]` `[reject]` Intent: the real-stack journey covers the Site menu, an Owner and the automatic resume only — the acceptance criterion names UJ-4, which is that path; the other entries, roles and refusals run in `pause.spec.ts` and the unit suites.
  - `[false]` `[reject]` Intent: the real-stack theme check reads classes and ink, not pixels — the criterion asks for the journey "plus snapshot tests"; pixel baselines are in the Playwright fake-Server suite and Roborazzi.
  - `[false]` `[reject]` Intent: the iOS render tests are not image comparisons — true since Story 1.8 and filed as DW-20; no divergence of this change.
  - `[false]` `[reject]` Intent: the SwiftUI views and `App/` adapters are verified only by the macOS CI jobs — as the spec states; a CI wait on the pull request, not an action owed by a human.
  - `[false]` `[reject]` Intent: mobile has no end-to-end path to a Server — the end-to-end criterion names Playwright; consistent with the story.
  - `[false]` `[reject]` Intent: existing baselines were re-recorded — expected: an Owner's Devices rows and Lot detail now carry Pause; two of each kind were looked at before finalizing.
  - `[false]` `[reject]` Intent: no finalized status, commit or Auto Run Result — the diff was taken before finalization.

### 2026-10-14 — Review pass (follow-up)
- verdicts: 41 findings — high 0, medium 2, low 25, false 14, maybe-false 0
- findings:
  - `[low]` `[reject]` Blind: the `SiteGone` notice is dropped as soon as the Sites are read again — real: the re-read drops the Site, the current Site changes and `PauseEngine` resets; the test's fake keeps listing the Site. It needs a Site deleted or a Membership revoked while the sheet is open; the app then shows another Site, and keeping the notice across the reset is new state, not a direct correction.
  - `[low]` `[patch]` Blind: an accepted Pause that answers after Cancel closes the modal that was opened since — real, shown by a failing Playwright case. Patched: `PauseModal.svelte` closes the modal only for the save that is still current; the page is still read again and the Pause announced.
  - `[low]` `[reject]` Blind: `Site.pausedUntil` is rendered nowhere — real and as designed (Design Notes, "Which end a surface shows"): the headline keeps its Lot-derived rule, the line and the menu follow `Site.paused`. Showing the Site's own end is a new display.
  - `[low]` `[reject]` Blind: no test shows the overview following a Site Pause on the live hint alone — real as stated; the Server's hints (`ProjectorHintsTests`) and the web's re-read on a hint (Story 6.6) are each tested, and a real-stack case for the pair is a new journey.
  - `[low]` `[reject]` Blind: the second read in `SitesEngine.refresh()` does not cover a `load()` in flight — real: it needs a Pause answered while the sign-in read of the Sites is still on its way; the next refresh repairs it. `pauseAnswered()` not writing the cache is followed by `refresh()`, which does.
  - `[low]` `[reject]` Blind: Android and iOS announce nothing after a Pause or a Resume — real; the mobile apps announce stale mode and Calibrate only, the row and the headline change in place, and the intent asks for no announcement.
  - `[low]` `[reject]` Blind: a Site Resume from the menu shows no progress — real for one round trip (`workingOnSite` has no reader); the menu item changes when the answer lands. A progress state on the overview is a new surface.
  - `[false]` `[reject]` Blind: Android puts every Devices row into the working state — the row buttons have no working label; `working` only ignores presses, which is the one-request-at-a-time rule the engine and iOS (`isEnabled`) apply too.
  - `[low]` `[reject]` Blind: the web's Pause opener on a row is named "Pause" only — real, and the page's convention: "Unassign" beside it is named the same way inside the group "Actions for Node ‹id›". One button named differently would not help; a page-wide change is not this story's.
  - `[low]` `[reject]` Blind: the same Pause sent again does not repeat a catch-up that failed — real after a logged `PauseCatchUpFailed` only; the projection follows on its next poll, as the log line says.
  - `[low]` `[patch]` Blind: `PauseDate.parse` accepts signs ("+027-03-01") — real, though no picker produces one. Patched: digits and the two hyphens only; test cases added first.
  - `[false]` `[reject]` Blind: the bookkeeping contradicts itself — `sprint-status.yaml` is written by the orchestrator, not by this build; the spec's `in-review` is this pass under way; correcting the task list is an edit of this spec.
  - `[low]` `[reject]` Edge: `SiteGone` and the reset — same as the first Blind row.
  - `[low]` `[reject]` Edge: a Pause answered while `load()`, Create Site or rename reads the Sites — same as the Blind row on `refresh()`.
  - `[low]` `[reject]` Edge: a refresh in flight when Create Site or rename answers can land an older list — real and older than this story (`created()` and `refresh()` are unchanged in substance); it needs a read begun before the write and landing two round trips later.
  - `[low]` `[reject]` Edge: a Resume that answers 404 shows nothing because its row is gone — carried: the list is read again and the Device is no longer in it, which is the news.
  - `[low]` `[reject]` Edge: a failed read of the Devices after an accepted Pause replaces the rows with the failure surface — real and the engine's pattern for every write on that list (`refresh()` after Move and Unassign); Try again reads them.
  - `[low]` `[reject]` Edge: mobile reads the Lots once right after a Site Pause, possibly before delivery reached the Devices — real, and what the intent states: Devices follow through delivery and clients re-read on the live hint or their next refresh. A timed second read would be a guess; awaiting delivery changes the endpoint. Named under residual risks.
  - `[low]` `[patch]` Edge: a re-tapped day leaves the Android calendar open — carried (the first pass's hygiene row, where this part was left as it is): the calendar closes with its field. Not patched again.
  - `[low]` `[reject]` Edge: the Devices list leaving Ready while its Pause sheet is open hides the sheet until the rows return — it needs a failed re-read during an open sheet, which only a 404 on the sheet's own save starts.
  - `[false]` `[reject]` Edge: a second submit of the Pause form counts as current too — the primary button ignores presses while working, and an implicit submit clicks that button; Try again shows only when nothing is on its way.
  - `[low]` `[reject]` Edge: a Resume posted before hydration that the Server refuses is shown nowhere — carried: the window before hydration only; the page then reads the Server's state.
  - `[low]` `[patch]` Edge: `IdentityProjector` sends a hint for a Site without a row — carried (the first pass's hygiene row, where this part was left as it is): a Site event is not applied twice and a hint for an unchanged row costs one read. Not patched again.
  - `[low]` `[patch]` Gap: the refusal of an abandoned save is guarded by source-text assertions only — as filed. Patched: the fake Server can hold pause writes (`holdPauses`), and `pause.spec.ts` cancels while a Pause is on its way and reopens, for a 403 and for an accepted Pause. Both fail with `attempts.abandon()` removed.
  - `[low]` `[patch]` Gap: the Site menu's one-Resume-at-a-time guard is asserted as source text — as filed, taken with the row above: `pause.spec.ts` chooses Resume twice while the first is held and expects one `DELETE`. It fails with the guard removed.
  - `[medium]` `[patch]` Gap: the app's own Pause wiring runs in no test, and leaving it out is a silent no-op — as filed. Patched: `SitesWiring.lots`, `lotDetail` and `devices` take `pause` without a default, so `AndroidSignIn` and `IosSignIn` no longer compile without it; a `ColdframeRoot` test shows Lot detail gets its own Pause state (it fails when `AppShell` passes the Garden's). The `MainActivity` collection stays unverified, like its siblings.
  - `[low]` `[reject]` Gap, other: two more `PauseModal` tests assert source text (one modal level, the re-read after a save) — the re-read is observed by every `pause.spec.ts` flow; a second modal cannot be opened over an open one, because a modal `<dialog>` makes the page behind it inert.
  - `[medium]` `[patch]` Gap, other: `PauseWiringTest` pins the silent no-op — same root as the wiring row: the test now passes `pause = null` on purpose.
  - `[low]` `[reject]` Intent: no client test of the re-read on the live hint after a Site Pause — same as the Blind row.
  - `[false]` `[reject]` Intent: only the Site-menu Pause is driven on the real stack — carried: the acceptance criterion names UJ-4, which is that path.
  - `[false]` `[reject]` Intent: mobile is covered by fixtures; the iOS bridge is compiled only and the iOS render tests are not image comparisons — carried: the bridge is in `deferred`, the render tests are DW-20.
  - `[false]` `[reject]` Intent: the Devices-list row is seeded with journal events and the migration test with hand-written payloads — the read right after the endpoint calls is `PauseEndpointTests.AReadRightAfterAPauseOrAResumeAnsweredShowsItForTheSiteAndForTheDevice`; the payloads are carried from the first pass.
  - `[false]` `[reject]` Intent: the real-stack theme check reads classes and ink — carried.
  - `[false]` `[reject]` Intent: `sprint-status.yaml` is written — by the orchestrator; this build does not touch it.
  - `[false]` `[reject]` Intent: a 404 has no retry, and a Pause failure shows in the sheet — the existing notice for a Device or Site that is gone has no retry, because sending the same again cannot help; the sheet keeping its entries and saying why is the Always list's wording.
  - `[false]` `[reject]` Intent: "Mar 1" on web and Android, "1 Mar" on iOS — the existing day formatters follow the locale, as the intent asks.
  - `[false]` `[reject]` Intent: the web uses a new `PauseModal.svelte`, not `Modal.svelte` — the task list allows either; it is a native `<dialog>` on the same modal stack.
  - `[false]` `[reject]` Intent: the web action marks a text that is no calendar date without calling the Server — a date field produces none; the Server's 400 remains the refusal the matrix describes.
  - `[low]` `[reject]` Intent: iOS selects tomorrow when the calendar opens; Android adds "Clear date" and "No end date" — carried for iOS; the Android texts are how an optional date is shown empty and emptied.
  - `[false]` `[reject]` Intent: the Lot detail hero shows "Resume the Site to resume this Node" also when both sources pause the Node — the hero note is older than this story and the same on web and mobile (`lot-detail.ts`, `LotDetail.kt`); the matrix row is about the Site-only case and holds.
  - `[false]` `[reject]` Intent: stale mode hides the line, the Member notice and the Lot detail controls — nothing shown in stale mode is live and a change needs the Server; the Lot detail gates were hidden in stale mode before this story.

## Design Notes

- **Line wording.** The story's acceptance text abbreviates the line as "No Alerts are sent while paused."; the UX source (UX-DR83, EXPERIENCE.md "All paused") gives the product string "No Alerts are sent while the Site is paused." followed by an attribution. The first sentence is used verbatim and the attribution is left out (see Never).
- **Scope from the Site menu.** There is no Device in that context, so the Segmented choice offers Whole Site alone rather than a disabled "This Device" (controls are hidden, not disabled). From a Device context, choosing Whole Site pauses the Site and leaves the Device's own source untouched.
- **Which end a surface shows.** The Server's value: `pausedUntil` is the later end, or none if any source is open-ended (`LotStatusRule.PausedUntil`). The headline keeps its existing Lot-derived rule; the line and the menu follow `Site.paused`, so a Site without Nodes can still be resumed.
- **Day boundary.** The Server resolves `until` at 00:00 in the User's notification-settings zone while apps format `pausedUntil` in the device zone; where the two zones differ the shown day can be one off. Accepted; no client-side zone logic.
- **iOS on this host.** Presentation, catalogue and source-rule tests run in the `swift:6.3.3` container; SwiftUI views, `App/` adapters and `RenderTests` compile and run only in the macOS CI jobs (`Swift`, `iOS app`), so those are verified on the pull request.

## Verification

**Commands** (repo root):
- `dotnet build --no-restore -warnaserror` and `dotnet format --verify-no-changes --no-restore` -- expected: clean
- `dotnet test --no-build --project tests/cs/server.tests` -- expected: all pass
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build --project tests/cs/server.integration` -- expected: all pass (the three `NodeMoveTests` reminder timeouts are a known flake; rerun once; run nothing else beside it)
- `pnpm --filter @coldframe/api-client generate && git diff --exit-code packages/ts/api-client/src/schema.ts` after staging the regenerated file -- expected: no diff
- `pnpm -r typecheck && pnpm -r lint && pnpm -r test && pnpm --filter @coldframe/openapi run check` -- expected: all pass (includes the Vitest and Playwright fake-Server suites)
- `pnpm --filter @coldframe/web build && ASPIRE_CONTAINER_RUNTIME=podman dotnet test --project tests/cs/web.e2e` -- expected: all pass, including `WinterPauseJourneyTests` in both themes
- `ANDROID_HOME=$HOME/Android/Sdk ./gradlew ktlintFormat check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64` -- expected: pass, Roborazzi verify included (record new baselines with `:android:recordRoborazziDebug` first)
- `podman run --rm --security-opt label=disable -v "$PWD":/work -w /work docker.io/library/swift:6.3.3 sh -c 'swift build && swift test && swift format lint --strict -r .'` -- expected: pass

**Manual checks (if no CLI):**
- The `Swift` and `iOS app` CI jobs on the pull request are green: they are the only build of the SwiftUI views, the `App/` adapters and the render tests.

## Auto Run Result

Status: done

**Summary.** Follow-up review of Story 8.2 after its first run (PR #102, merged). The story itself is unchanged: Pause and Resume work in the web, Android and iOS apps over the Server's new read surface (`Site.paused` / `pausedUntil`, `DeviceListItem.pausedBy` / `pausedUntil`), with the winter journey (UJ-4) on the real stack. This pass read the whole change again, the first pass's patches included, and made four small corrections: one web defect in the Pause modal, one stricter date parse in the Kotlin core, executed browser tests in place of source-text assertions for two guards, and Pause wiring that no longer compiles when it is left out.

**Files changed in this pass.**
- `apps/ts/web/src/lib/components/PauseModal.svelte` -- an accepted Pause that answers after Cancel no longer closes the modal opened since.
- `packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/sites/Pause.kt` -- `PauseDate.parse` reads digits and the two hyphens only.
- `packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/sites/SitesWiring.kt` -- `pause` has no default on `lots`, `lotDetail` and `devices`.
- `tests/ts/web.e2e/fixtures/fake-idp.ts`, `specs/helpers.ts` -- the fake Server can keep pause writes waiting (`holdPauses`).
- `tests/ts/web.e2e/specs/pause.spec.ts` -- three cases: a refusal after Cancel, an accepted Pause after Cancel, a second Resume while the first is on its way.
- `tests/kt/core/commonTest/.../sites/PauseEngineTest.kt`, `PauseWiringTest.kt`, `push/PushWiringTest.kt` -- signed dates are refused; `pause = null` is passed on purpose.
- `tests/kt/android/test/.../LotDetailScreenTest.kt` -- the shell hands Lot detail its own Pause state.
- This spec: the triage log of this pass and this result. `deferred-work.md` and `sprint-status.yaml` carry the loop's own bookkeeping and were not written by this build.

**Review.** One pass, four layers, 41 findings (medium 2, low 25, false 14; no high, no intent gap, no spec fault). Every row and its reason is in the Review Triage Log.
- Patched (4 entries; by verdict: medium 1, low 3): the Pause wiring that could be left out silently, with the test that pinned it (medium); the modal closed by an older accepted Pause (low); signed dates in `PauseDate.parse` (low); the two web guards that only source text asserted (low).
- Deferred: none new. The three entries of the first pass stay in `deferred`.
- Carried from the first pass, verdict and route kept, nothing patched again (8 rows): the re-tapped day in the Android calendar; the hint for a Site without a row; a 404 Resume whose row is gone; a refusal before hydration; iOS selecting tomorrow; the journey's scope; the iOS bridge and render tests; the theme check on the real stack.
- Rejected (low, 16 new rows): the `SiteGone` notice dropped by the reset that follows it (two rows); a Pause answered during the sign-in read of the Sites (two rows); a refresh in flight during Create Site; `Site.pausedUntil` shown nowhere; no test of the overview following the hint alone (two rows); no announcement on mobile; no progress on a Site Resume; the unnamed "Pause" opener on web rows; a failed catch-up not repeated by the same Pause; the failure surface after a failed re-read of the Devices; the mobile Lots read before delivery; the sheet hidden while the Devices list is not Ready; two more source-text tests of the modal.
- Rejected (false, 11 new rows): every Android row "working"; the bookkeeping; a second submit of the Pause form; journal-seeded rows; `sprint-status.yaml`; the 404 without retry; the date format; `PauseModal.svelte`; the client-side date check; the hero's resume-the-Site note; stale mode.

**Follow-up review: not recommended.** This is a follow-up pass and it patched no high entry.

**Verification** (run after the patches, repo root, one command at a time):
- `dotnet build --no-restore -warnaserror` -- 0 warnings, 0 errors; `dotnet format --verify-no-changes --no-restore` -- no changes.
- `dotnet test --no-build --project tests/cs/server.tests` -- 1055 of 1055 passed.
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build --project tests/cs/server.integration` -- 602 of 602 passed (a first attempt was cut off by the session's ten-minute command limit at 460 passed, none failed; the full run took about 20 minutes).
- `pnpm --filter @coldframe/api-client generate` -- `schema.ts` unchanged; `pnpm -r typecheck`, `pnpm -r lint`, `pnpm -r test` (web unit 816, Playwright fake-Server suite 159), `pnpm --filter @coldframe/openapi run check` -- all exit 0.
- `pnpm --filter @coldframe/web build && ASPIRE_CONTAINER_RUNTIME=podman dotnet test --project tests/cs/web.e2e` -- 5 of 5 passed.
- `ANDROID_HOME=$HOME/Android/Sdk ./gradlew ktlintFormat check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64` -- exit 0, Roborazzi verify included; no baseline changed.
- Swift container (`swift build && swift test && swift format lint --strict -r .`) -- 396 tests passed, lint clean.
- The new tests fail without what they guard: the accepted-Pause case failed before the `PauseModal.svelte` patch; all three browser cases fail with `attempts.abandon()` and the Resume guard removed; the shell test fails when `AppShell` passes the Garden's Pause state to Lot detail; the signed-date cases failed before the parse was tightened.
- Not verified on this host: the SwiftUI views, the `App/` adapters and `RenderTests`, which only the macOS `Swift` and `iOS app` CI jobs build and run. No Swift file changed in this pass; `IosSignIn.kt` already passes `pause`, and both iOS targets compile.

**Residual risks.**
- Mobile, after a Site Pause: the Lots are read once right after the answer. If that read lands before the Pause reached the Devices, the overview shows the paused line over live tiles until the next refresh (foreground or pull). The intent accepts this; it is the most visible of the rejected findings.
- A Site that is gone while its Pause sheet is open: the notice saying so is dropped when the Sites are read again and the app moves to another Site. The core test of that notice keeps the Site listed, so it does not show this.
- A Pause answered while the sign-in read of the Sites is still on its way can be overwritten by that older list until the next refresh.
- The iOS bridge and `App/` adapters for Pause are still compiled only (in `deferred`, DW-115).
- A Site or Device read can show a Pause for up to about a minute after its end, until the owning grain's next wake.

