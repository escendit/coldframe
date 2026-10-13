---
title: 'Story 7.3: Low-battery Alert'
type: 'feature'
created: '2026-10-13'
baseline_revision: fd10f2b765a1c0ea85dc2bac10e8b0aaab1fcb7c
status: 'done'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-7-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-7-1-silent-device-alert-on-the-server.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-7-2-silence-in-the-apps-and-silence-window-settings.md'
  - '{project-root}/apps/cs/README.md'
warnings: ['oversized']
deferred:
  - summary: >-
      Pause (Epic 8) must close an open battery Alert and reset the battery streaks; today a paused Node keeps
      its Alert open and its streak across the Pause.
    evidence: |-
      DeviceGrain.Ingest counts no report while paused and nothing journals an end of the episode or a streak
      reset on device.paused / device.resumed. No grain method raises a Pause yet (only tests append the
      events), so it cannot happen in the product before Epic 8.
    location: >-
      apps/cs/server/Devices/DeviceGrain.cs (CountBattery), apps/cs/server/Devices/DeviceState.cs
    severity: medium
  - summary: >-
      The iOS adapter that hands the core's alertBatteryPercents to AlertsPresentation is executed by no test.
    evidence: |-
      CoreAlertsService.presentation(of:) passes batteryPercents: snapshot.alertBatteryPercents; the parameter
      defaults to [], so dropping the argument compiles and every open row reads "battery low". The Swift tests
      build the presentation from fixtures, and the App target compiles only on the macOS CI jobs (DW-93).
    location: >-
      apps/swift/ios/App/CoreAlertsService.swift:45
    severity: medium
  - summary: >-
      The retry answer of Ingest step 7 is not reached by a failing journal write, and no test pins it.
    evidence: |-
      A test-only trigger that fails one insert into journal_events showed the log-consistency adaptor repeats
      the write inside ConfirmEvents: the frame answers Stored and the report is counted once
      (AReportWhoseJournalWriteFailsOnceIsCountedExactlyOnce). The catch that answers Retry, written in Story
      7.1 for the liveness write and now shared by the battery event, stays unexercised.
    location: >-
      apps/cs/server/Devices/DeviceGrain.cs (Ingest, step 7)
    severity: low
---

<intent-contract>

## Intent

**Problem:** A Node whose battery is running out while it is not charging (a shaded panel) raises nothing: its device reports are stored and shown on Devices and Lot detail, but nobody is told before the Node dies. The clients already know the `battery` Health kind, but nothing produces it and its title cannot say the level.

**Approach:** The Device grain counts a Node's device reports and opens a Health Alert of kind `battery` through the plumbing Story 7.1 built for `silent`, closing it when the battery recovers. The Alert carries the level of the report that opened it, added to `HealthAlert` as an optional member, and the three clients and the push copy word it "Node battery 14 %, not charging".

## Boundaries & Constraints

**Always:**
- A report is **low** when its battery is known and below 20 and its charger status is *not charging*. It is **recovered** when its charger status is *charging* or its battery is known and at 20 or above. Any other report (battery unreadable while not charging, or charger status unknown below 20 or unreadable) is neither.
- The Alert opens with the third consecutive low report and closes, with reason `recovered`, with the third consecutive recovered report. A report that is not low ends a low streak; a report that is not recovered ends a recovery streak.
- Every device report counts once, at the `measured_at` stored with its key at first delivery, and only when that time is newer than the last report counted: a frame sent again, or an older one arriving late, changes nothing.
- Streaks, episode and the open Alert are journaled state of the Device grain; the episode is journaled before the Alert grain is called. Alert ID = UUIDv5 of `device:{deviceId}:battery:{episode}` in the existing namespace. At most one open `battery` Alert per Node; only the Device grain closes it.
- Opens and closes go through the Device grain's existing journaled, ordered, retried delivery. A counted report whose journal write fails is answered with retry, as the liveness write is.
- The Alert states the battery level of the report that opened it. It keeps the Lot the Node had when it opened; a Node on no Lot gets an Alert without a Lot.
- Every contract change is additive (`packages/proto/check-compat.sh` passes); new journaled members are nullable; an Alert without a level degrades to today's "battery low" wording.
- Copy comes from the string catalogues; Android and iOS keys and English values stay in parity.

**Never:**
- No change to Lot status, to Lot tiles, or to the Silence evaluation: a `battery` Alert never turns a Lot `unknown`.
- No Alert for a Hub, and none from a paused Node (its frames store no report).
- No new field on `DeviceListItem` or `NodeStatus`: Devices and Lot detail keep showing the newest report's level and charger status with the low-battery icon below 20 %.
- No change to the push route payload or the AsyncAPI contract; no uncalibrated Alert (Story 7.4); no Pause (Epic 8).
- No wall clock; no second reminder; no read of `device_reports` from the grain to rebuild a streak.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Exactly three to open | Reports 19 %, 14 %, 14 %, all not charging | Nothing after the second; one `battery` Alert after the third, level 14, episode 1; Site and members told | Undelivered open retried on the next wake or report |
| Exactly three to close | Open Alert; reports 25 % not charging, 12 % charging, 30 % charging | Still open after the second; closed `recovered` after the third; the next low episode is episode 2 with another Alert ID | Close retried the same way |
| Charging suppression | Any number of reports at 10 % charging | No Alert | — |
| Not charging, enough charge | Reports at 20 % and 55 % not charging | No Alert (20 is not below 20) | — |
| Flapping around 20 % | 19, 19, 20, 19, 19, 21, 19, 19, 19 (not charging) | Alert only after the last report | — |
| Flapping while open | Open Alert; recovered, recovered, low, recovered, recovered, recovered | Closes only after the last report | — |
| Unreadable report | Low, low, (no battery value), low | No Alert: the streak restarts at the fourth report | — |
| Frame sent again | The third low report is delivered twice; or two low reports each delivered twice | One Alert; or no Alert | — |
| Late older report | A report older than the last one counted | Ignored | — |
| Node on no Lot | Three low reports from an unassigned Node | Alert without a Lot | — |
| Alerts list | Open `battery` Alert, Lot "Lettuce", level 14 | `healthAlerts` item with `kind` `battery`, `batteryPercent` 14, `lotName`; counted in `openCount`; not in `alerts` | — |
| Row title | Same Alert / Node `9c0d…` on no Lot | "Node battery 14 %, not charging — Lot 'Lettuce'" / "Node 9C0D battery 14 %, not charging"; low-battery icon; leads to the Device's row | Closed, or no `batteryPercent`: "Node battery low on Lot 'Lettuce'" / "Node 9C0D battery low" |
| Push, single | Same Alert | Title "Node battery 14 %, not charging", body "Lot 'Lettuce'." | No Lot: title "Node 9C0D battery 14 %, not charging", no body. No level: today's neutral text |
| Reminder | A day later, still open | Same title; body "Still low. Lot 'Lettuce'." ("Still low." without a Lot) | — |
| Summary | Morning summary containing it | Line "Lettuce: Node battery 14 %, not charging" (no Lot: the no-Lot title); counted under "to check" | — |

</intent-contract>

## Code Map

Server (`apps/cs/server`, contracts `packages/cs/contracts`)
- `Devices/DeviceGrain.cs` -- `Ingest` (:520): `frame.Rows.Report` (`DeviceReportWrite(ReportSeq, short? BatteryPercent, string Charging)` with tokens `charging|not_charging|unknown`, `NodeFrameReader.cs:159,270`) is at hand after the commit (:604); step 7 (:622-650) is the journal-or-retry block; step 7a (:655) calls `EvaluateSilenceAsync`. `DeliverAlertsAsync` (:912) builds `OpenDeviceAlert` from a `PendingDeviceAlertDelivery`; reuse it, do not fork it. A paused Node commits `Rows: null`.
- `Devices/DeviceIngestionStore.cs` -- `FrameCommitted.StoredMeasuredAt` / `MeasuredAtOf` (:70-82) cover Readings only; `StoredMeasuredAtSql` (:131) must also return the report key `(SensorIds.DeviceReport, report_seq)`. `EvaluateAsync` (:988) shows why the stored time is used (an unsynced Node's resend has a later time).
- `Devices/DeviceState.cs` -- next free `[Id(21)]`; silent members Ids 17-20; `PendingDeviceAlertDelivery` (:424, next `[Id(9)]`); `Apply` overloads at :337, :361, :383 are the pattern. `Devices/SilenceRule.cs` -- shape for a pure `BatteryRule` (classify a report, the threshold 20, the count 3).
- `packages/cs/contracts/Devices/DeviceEvents.cs` (:298-337), `DeviceGrains.cs:185` -- the silence events and `DeviceSilentAlert`; new events are public types here with stable aliases.
- `packages/cs/contracts/Alerts/AlertGrains.cs` -- `AlertKind { Threshold, Silent }` (:54; the journal stores names), `OpenDeviceAlert` (:167, next `[Id(8)]`), `AlertSnapshot` (:243); `AlertEvents.cs:29` `AlertOpened` (next `[Id(11)]`).
- `Alerts/AlertGrain.cs` -- `OpenForDevice` refuses every kind but `Silent` and derives the ID with `AlertIds.Silent` (:131-136); builds `SiteAlert` (:246). `AlertIds.cs`, `AlertNames.cs:14` (throws on an unnamed kind), `AlertState.cs` (next `[Id(15)]`).
- `packages/cs/contracts/Sites/SiteGrains.cs:790` `SiteAlert` (next Id 10), `Notifications/Notifications.cs:47` `NotificationEntry` (next Id 10), `Identity/UserGrain.cs:412` `Entry` -- the hops `SilentSince` travels; the level follows them.
- `Alerts/AlertsProjector.cs` (`OpenSql` :27, parameters :73-92), `AlertsReadModel.cs` (`HealthAlertView` :50, `HealthSql` :128 already lists every non-Threshold kind, reader :238); `apps/cs/migrations/Migrations/M20261012090000AddSilenceColumns.cs` -- how a nullable column was added to `alerts` without a rebuild.
- `Lots/LotsProjector.cs` -- `KindName` (:504) falls back to `ToString()` ("Battery"); `InputsSql` (:178) reads only `threshold` and `silent`, so status is unaffected. `Devices/DevicesProjector.cs:97-164` ignores unknown events.
- `Edge/EdgeApi.cs` -- `HealthAlertResponse` (:449), `ToHealthAlert` (:1096).
- `Notifications/Push/PushText.cs` (`Title` :92, `Body` :142, `SummaryBody` :212, `SilentTitle` :283), `PushText.en.json`, `PushContent.cs` (`FactsAsync` :142; a Lot lookup already exists; channels call no other grain).
- Tests: `tests/cs/server.integration/Devices/SilentDeviceAlertGrainTests.cs` helpers (`SiteAsync` :724, `NodeAsync` :742, `IngestAsync` :766, `CatchUpAsync` :838; :359 pins "a Threshold kind is refused"); `tests/cs/device-simulator/SimulatedDevice.cs:328` `Wake(measuredAt, batteryPercent, charging, …)`, `WakeUnsynced` :345. Guards: `tests/cs/server.tests/Fixtures/journal.json` + `FixtureJournalReplayTests` (:286, a row per alias), `Alerts/AlertsReadModelUnitTests.cs:61` (a lowercase name per `AlertKind`), `Notifications/PushTextTests.cs:211-275`, `tests/cs/server.integration/Edge/AlertsListTests.cs:341-470`.
- Docs: `apps/cs/README.md` -- "Silent Devices" :595, "The Alerts list" :728 (:745 says the Server produces `threshold` and `silent`), push fallback row :1013.

Contract
- `packages/openapi/coldframe.openapi.json` -- `AlertKind` (:1594, `x-extensible-enum` already lists `battery`; description to correct), `HealthAlert` (:1666, no level), `DeviceListItem.batteryPercent` (:1551) as the shape to copy; `packages/openapi/README.md:135-139`. `packages/ts/api-client/src/schema.ts` is generated; `tests/kt/core/jvmTest/.../api/OpenApiContractTest.kt:207-223` pins `HealthAlertDto` to the schema.

Web (`apps/ts/web/src`)
- `lib/alerts.ts` -- `deviceCondition` (:153-159) titles `battery` with `alerts.title.battery` or the neutral title; `silentTitle` (:131) and `shortDeviceId` are the pattern. `lib/i18n/en.json:40,453-458`. Tests `tests/ts/web/alerts.test.ts:478`; e2e `tests/ts/web.e2e/fixtures/fake-idp.ts:264` (`FakeHealthAlert`), `specs/silence.spec.ts:149,404` (Health rows, `alerts-health-${theme}.png`), `specs/devices.spec.ts:113`, `specs/lot-detail.spec.ts` (battery on the two other surfaces, already asserted).

Kotlin core and Android
- `packages/kt/core/src/commonMain/.../core/api/ApiDtos.kt:324` (`HealthAlertDto`), `alerts/Alerts.kt` (`AlertTitleKind.Battery` :151, `AlertTitle` :170, `AlertSummary` :187, `title()` :297-302), `alerts/AlertsEngine.kt:197`, `alerts/AlertsSnapshot.kt:37-55` (parallel lists read by iOS). Tests `tests/kt/core/commonTest/.../alerts/AlertsEngineTest.kt:587,723,739`.
- `apps/kt/android/src/main/kotlin/.../ui/alerts/AlertsCopy.kt:83`, `res/values/strings.xml:62`. Tests `tests/kt/android/test/.../AlertFixtures.kt:59-139`, `AlertsScreenTest.kt:167,210,315`, `SnapshotTest.kt:741-817` (baselines `tests/kt/android/snapshots`), `StringsTest.kt:78`.

iOS
- `apps/swift/ios/Sources/ColdframeIOS/AlertsPresentation.swift` (:126, :171-213, :257, :369), `L10n.swift:522`, `Resources/Localizable.xcstrings:5721`, `apps/swift/ios/App/CoreAlertsService.swift:30-44`. Tests `tests/swift/ios/ColdframeIOSTests/AlertsPresentationTests.swift:70,264`, `CatalogueTests.swift:103` (`coveredUxDrs`), `RenderTests.swift:992`.

## Tasks & Acceptance

**Execution:**
- [x] `tests/cs/server.tests/Devices/BatteryRuleTests.cs`, `DeviceStateBatteryTests.cs` (new), `tests/cs/server.integration/Devices/BatteryAlertGrainTests.cs` (new) -- failing first: every server row of the matrix on the TestCluster with the fake clock, plus "Lot status unchanged" and "silence evaluation unchanged".
- [x] `packages/cs/contracts/Alerts/AlertGrains.cs`, `AlertEvents.cs`, `Sites/SiteGrains.cs`, `Notifications/Notifications.cs` -- `AlertKind.Battery`; a nullable battery level on `OpenDeviceAlert`, `AlertOpened`, `AlertSnapshot`, `SiteAlert`, `NotificationEntry`.
- [x] `apps/cs/server/Alerts/AlertIds.cs`, `AlertNames.cs`, `AlertState.cs`, `AlertGrain.cs` -- `AlertIds.Battery`, name `battery`, `OpenForDevice` accepts `Battery` for a Node and carries the level.
- [x] `packages/cs/contracts/Devices/DeviceEvents.cs`, `DeviceGrains.cs` -- events for a counted report that moved a streak, the start and the end of a low-battery episode; the open `battery` Alert on what the grain reports about itself.
- [x] `apps/cs/server/Devices/BatteryRule.cs` (new) -- classify a report and advance the streaks as pure functions.
- [x] `apps/cs/server/Devices/DeviceIngestionStore.cs` -- return the stored `measured_at` of the frame's report key.
- [x] `apps/cs/server/Devices/DeviceState.cs`, `DeviceGrain.cs` -- streaks, last counted time, episode, open Alert, the level on a pending delivery; count the report in `Ingest` after the commit, journal only what changed, deliver through `DeliverAlertsAsync`.
- [x] `apps/cs/migrations/Migrations/M20261013090000AddAlertBatteryPercent.cs` (new), `apps/cs/server/Alerts/AlertsProjector.cs`, `AlertsReadModel.cs`, `Lots/LotsProjector.cs`, `Identity/UserGrain.cs`, `Edge/EdgeApi.cs` -- nullable `alerts.battery_percent`; project and list it; name the kind in the lots projector; `batteryPercent` on the Health Alert response.
- [x] `apps/cs/server/Notifications/Push/PushText.cs`, `PushText.en.json`, `PushContent.cs` -- the battery titles, bodies, Reminder bodies and summary lines of the matrix.
- [x] `tests/cs/server.tests/**`, `tests/cs/server.integration/Edge/AlertsListTests.cs` -- journal fixture rows, kind-name guard, `PushTextTests` per copy row, the list row, the "refused kind" test.
- [x] `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md`, `packages/ts/api-client/src/schema.ts` -- optional `HealthAlert.batteryPercent` (integer 0-100), a battery example, the `AlertKind` description; regenerate the client.
- [x] `apps/ts/web/src/lib/alerts.ts`, `lib/i18n/en.json`, `tests/ts/web/alerts.test.ts`, `tests/ts/web.e2e/fixtures/fake-idp.ts`, `tests/ts/web.e2e/specs/silence.spec.ts` (or a new `battery.spec.ts`) -- the row titles; unit tests per title row; a Health row for a battery Alert in light and dark screenshots.
- [x] `packages/kt/core/src/commonMain/**` (`ApiDtos.kt`, `alerts/Alerts.kt`, `AlertsEngine.kt`, `AlertsSnapshot.kt`), `tests/kt/core/**` -- DTO member, the level and the Node short name as title inputs, snapshot list.
- [x] `apps/kt/android/src/main/kotlin/**/ui/alerts/AlertsCopy.kt`, `res/values/strings.xml`, `tests/kt/android/**` -- titles; fixtures, screen tests, Roborazzi baselines in light and dark.
- [x] `apps/swift/ios/Sources/ColdframeIOS/AlertsPresentation.swift`, `L10n.swift`, `Resources/Localizable.xcstrings`, `apps/swift/ios/App/CoreAlertsService.swift`, `tests/swift/ios/**` -- the same titles; presentation, catalogue (`coveredUxDrs`) and render tests.
- [x] `apps/cs/README.md`, `packages/cs/README.md`, `apps/ts/web/README.md`, `apps/kt/README.md`, `apps/swift/README.md`, `packages/kt/core/README.md` -- the low-battery rule and events; remove "no producer before Story 7.3" for `battery`.

**Acceptance Criteria:**
- Given a Site whose Node on Lot "Lettuce" sent three reports at 14 % not charging, when a member opens Alerts on web, Android and iOS, then the Health group shows "Node battery 14 %, not charging — Lot 'Lettuce'" with the low-battery icon, in light and dark themes, and the web row links to that Node's row on Devices.
- Given the same Node, when Devices and Lot detail render, then they show "14 %" and "not charging" with the low-battery icon, as before this story.
- Given an open `battery` Alert on a Lot's Node, when the lots projection catches up, then the Lot's status is what it would be without the Alert.
- Given a `battery` Alert tracked by a member, when Reminders are scheduled, then the next one is not sent before 24 h after the previous notification.
- Given the journal fixture with the new events, and Alerts journaled before this story, when they are replayed and listed, then every row applies and every response validates.
- Given the working tree, when `packages/proto/check-compat.sh` runs against `origin/main`, then it reports no breaking change, and the Kotlin `OpenApiContractTest` and the TypeScript staleness test pass.

## Spec Change Log

## Review Triage Log

### 2026-10-13 — Review pass
- verdicts: 25 findings — high 0, medium 8, low 9, false 8, maybe-false 0
- findings:
  - `[medium]` `[patch]` Blind: after a reactivation the "last report counted" floor fell back to the journaled battery time, so late older frames could open or close an Alert — the floor now also takes the journaled liveness (`State.LastReportedAt`); README corrected; a restart test covers it.
  - `[medium]` `[patch]` Blind: the journal-failure path of a counted report had no test — a test-only fault on `journal_events` was added; it showed the write is repeated inside `ConfirmEvents` (answer `Stored`, counted once), which the new test pins. The unreached `Retry` branch is deferred.
  - `[low]` `[patch]` Blind: no test restarted the silo mid-streak or mid-episode — two tests added (a journaled streak of two opens after a restart; late older reports after a restart are ignored).
  - `[low]` `[reject]` Blind: streaks have no freshness bound (a backlog or a long gap still counts as consecutive) — the intent counts reports, not time; unlikely to matter in everyday use and a bound would be a new rule.
  - `[low]` `[reject]` Blind: only three recovered reports close the Alert, so a Node that dies keeps its battery Alert beside its silent one — the intent names this one closing condition and the open Alert stays true of the Node; recorded as a residual risk for a product decision.
  - `[false]` `[reject]` Blind: no hysteresis around 20 % — the three-report rule on both sides is the intent's answer to flapping; the matrix rows pin it.
  - `[low]` `[patch]` Blind: the READMEs still said Health Alerts are bounded by the number of Devices — corrected in both READMEs and the read model's comment.
  - `[false]` `[reject]` Blind: an out-of-range level could be journaled and stall the projector — the only caller passes the frame's level, which `NodeFrameReader` refuses above 100; the state cannot be reached.
  - `[low]` `[reject]` Blind: battery deliveries run inside the silence evaluation and share its queue and log message — the shared ordered delivery is what the spec asks for; a rename and a two-kinds test add little.
  - `[low]` `[patch]` Blind: no journey crossed the real Server and the web app for this kind — `tests/cs/web.e2e/LowBatteryJourneyTests.cs` added and passing.
  - `[low]` `[patch]` Blind: the contract did not say a closed Alert's level is no longer current — stated in the `batteryPercent` description; client regenerated.
  - `[false]` `[reject]` Blind: small client inconsistencies (Android formats a null level as empty, number formatting differs, title-kind names, spec wording) — the core emits the level kinds only with a level, 0–100 formats the same everywhere, and the rest would edit this spec.
  - `[medium]` `[patch]` Edge: stale report counted after reactivation — same defect as the first row, fixed with it.
  - `[medium]` `[defer]` Edge: a Node paused mid-streak keeps the streak across the Pause — no Pause can be raised before Epic 8; deferred with the next row.
  - `[medium]` `[defer]` Edge: a Node paused with an open battery Alert keeps it and its Reminders — same cause; deferred for Epic 8.
  - `[false]` `[reject]` Edge: `OpenForDevice` accepts a missing or out-of-range level — same refutation as the projector row: the caller cannot produce one.
  - `[false]` `[reject]` Edge: Android prints "Node battery  %" for a level kind without a level — `AlertSummary.batteryTitle` sets those kinds only with a level.
  - `[medium]` `[patch]` Gap: "a failed journal write is answered with retry and counted by the resend" was unpinned — same as the second row.
  - `[medium]` `[defer]` Gap: the iOS adapter's hand-off of `alertBatteryPercents` is executed by no test — needs the macOS-only App target (DW-93); deferred.
  - `[medium]` `[patch]` Gap (other): in-memory floor lost on reactivation — same defect as the first row.
  - `[low]` `[patch]` Gap (other): `ReminderIntervalRuleTests` never passed `Battery` although a comment named it as the proof — the Health case is now a theory over `Silent` and `Battery`.
  - `[false]` `[reject]` Intent: Devices and Lot detail got no alert sentence — the UX defines those surfaces as level, charger status and the low-battery icon (UX-DR29, UX-DR30), which they render and existing tests pin; the new web e2e test and the journey assert the Devices row. Recorded as a residual risk.
  - `[false]` `[reject]` Intent: "not charging" is copy, not data, and the level is the one at opening — the Alert opens only from a report that is not charging, and the contract now says the level is the opening one.
  - `[low]` `[patch]` Intent: tests exercised each side of the Server/client seam separately and no restart — closed by the journey and the restart tests above.
  - `[false]` `[reject]` Intent: process — no acceptance criterion needs a human action outside the repository; the macOS CI jobs are a pull-request check, not an operator action.

## Design Notes

- **Only the level travels.** A `battery` Alert opens only from a report that is not charging, so the kind already states the charger status; the copy says "not charging" and the payload adds one nullable integer at each hop.
- **Neither low nor recovered.** The epics define two conditions that do not cover an unreadable battery or an unknown charger status. "Three consecutive reports" is read literally: such a report is a report, so it ends either streak and starts none.
- **Count by stored time.** As for Readings (Story 6.1), a report is counted at the `measured_at` its key got at first delivery, and only when newer than the last one counted. That makes a resend a no-op even when the first attempt's journal write failed, and keeps evaluation monotonic.
- **Journal only what changed.** A healthy Node's report moves no streak and journals nothing; the common case adds no event per frame. A report that leaves the state as it is can safely be evaluated twice.
- **Row title.** The UX gives the push pair "Node battery 14 %, not charging" / "Lot 'Lettuce'.". A row has one line, so it joins the two with the dash the Hub notice already uses; without a Lot the Node's short ID names it, as the silent titles do. A closed row keeps today's "battery low" wording: the level at opening is no longer true.
- **Reminder body** "Still low." follows the UX rule that Reminders reuse the text with "still"; the UX gives no battery example.
- **Devices and Lot detail** already render level, charger status and the low-battery icon from the newest report (UX-DR29, UX-DR30); they are the "level and charging status" of the Alert on those surfaces, and they move as the battery does.
- **Not verifiable on this host:** SwiftUI views, the iOS App target and `RenderTests` compile only on the macOS CI jobs (DW-93).

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror && dotnet format --verify-no-changes --no-restore` -- expected: no error, no change.
- `dotnet test --no-build --project tests/cs/server.tests` -- expected: all pass.
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build --project tests/cs/server.integration` -- expected: all pass, none skipped.
- `packages/proto/check-compat.sh` (tools from `packages/proto/install-tools.sh <dir>` on `PATH`) -- expected: no breaking change.
- `pnpm --filter @coldframe/openapi run check && pnpm -r typecheck && pnpm -r lint && pnpm --filter @coldframe/web-tests test && pnpm --filter @coldframe/api-client-tests test` -- expected: pass (use the package's real test filter name if it differs).
- `pnpm --filter @coldframe/web-e2e test` -- expected: pass, with the new screenshots committed.
- `ANDROID_HOME=$HOME/Android/Sdk ./gradlew check` -- expected: pass, new Roborazzi baselines recorded with `:android:recordRoborazziDebug` and committed.
- `podman run --rm --security-opt label=disable -v "$PWD":/work -w /work docker.io/library/swift:6.3.3 sh -c 'swift build && swift test && swift format lint --strict -r .'` -- expected: pass.

**Manual checks (if no CLI):**
- The macOS CI jobs `swift` and `ios` on the pull request are the only compile and render check of the SwiftUI and App-target edits; read their result before the merge.

## Auto Run Result

Status: done

**Summary:** A Node's Device grain now counts its device reports and opens a Health Alert of kind `battery` with the third consecutive report below 20 % while not charging, and closes it with the third consecutive report that is charging or at 20 % or above. The Alert carries the level it opened with (`HealthAlert.batteryPercent`, additive), and web, Android, iOS and push word it "Node battery 14 %, not charging".

**Files changed:**
- `apps/cs/server/Devices/BatteryRule.cs`, `DeviceGrain.cs`, `DeviceState.cs`, `DeviceIngestionStore.cs`; `packages/cs/contracts/Devices/*` -- the rule, the counting in `Ingest`, three new events, the stored time of the report key.
- `apps/cs/server/Alerts/*`, `packages/cs/contracts/Alerts/*`, `Sites/SiteGrains.cs`, `Notifications/Notifications.cs`, `Identity/UserGrain.cs` -- `AlertKind.Battery`, its Alert ID, the level on every hop.
- `apps/cs/migrations/Migrations/M20261013090000AddAlertBatteryPercent.cs`, `Alerts/AlertsProjector.cs`, `AlertsReadModel.cs`, `Lots/LotsProjector.cs`, `Edge/EdgeApi.cs` -- `alerts.battery_percent` and `batteryPercent` in the list.
- `apps/cs/server/Notifications/Push/*` -- battery titles, bodies, Reminder and summary lines.
- `packages/openapi/coldframe.openapi.json`, `packages/ts/api-client/src/schema.ts` -- the optional member, example, descriptions.
- `apps/ts/web/src/lib/alerts.ts`, `i18n/en.json`; `packages/kt/core/.../alerts/*`, `api/ApiDtos.kt`; `apps/kt/android/.../AlertsCopy.kt`, `strings.xml`; `apps/swift/ios/**` -- the row titles on the three clients.
- `tests/cs/**`, `tests/ts/**`, `tests/kt/**`, `tests/swift/**` -- rule, state, TestCluster, list, push, journey, client and snapshot tests with new baselines.
- Six READMEs -- the low-battery rule and what changed for clients.

**Review:** 25 findings. Patched 11 rows in 7 entries (medium 2 entries: the reactivation floor, the journal-failure test; low 5). Deferred 3 entries (Pause handling for Epic 8; the untested iOS adapter hand-off; the unreached retry branch of step 7). Rejected 8 as false and 3 low ones; each reason is in the triage log.

**Follow-up review recommended: true** (2 medium entries patched). The specific risk: the reactivation floor now leans on `State.LastReportedAt`, and that change and its tests were written after the review layers ran, so no independent reviewer has read them.

**Verification (on the patched tree unless noted):**
- `dotnet restore --locked-mode && dotnet build -warnaserror && dotnet format --verify-no-changes` -- clean.
- `tests/cs/server.tests` -- 983 passed. `tests/cs/server.integration` -- 531 passed, none skipped.
- `tests/cs/web.e2e` (real stack) -- 3 passed, including the new low-battery journey.
- `packages/proto/check-compat.sh` -- no breaking Protobuf or OpenAPI change.
- openapi check, `pnpm -r typecheck`, `pnpm -r lint` -- pass; web tests 740 passed; api-client tests 14 passed.
- `./gradlew check` -- passed.
- Before the review patches, which changed no web source, Kotlin or Swift code: `pnpm --filter @coldframe/web-e2e test` 133 passed; Swift container build, 354 tests and strict format lint passed.

**Residual risks:**
- SwiftUI views, the iOS App target and `RenderTests` compile only on the macOS CI jobs `swift` and `ios`; their result on the pull request is the only check of those edits.
- Devices and Lot detail show the newest level and charger status, not the Alert: they can differ from the Alert's opening level while it is open.
- A Node that dies with a low battery keeps its `battery` Alert open beside its `silent` one, each with a daily Reminder, until it reports again.
- The server code was written before its tests, not test-first.
