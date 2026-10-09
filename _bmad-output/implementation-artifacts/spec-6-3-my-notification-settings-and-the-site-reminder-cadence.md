---
title: 'Story 6.3: My notification settings and the Site Reminder cadence'
type: 'feature'
created: '2026-10-09'
baseline_revision: fe271f5a6be2c2c3b8749265b5d458b668e876a3
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
      The iOS App adapters for notification settings and the Site Reminder cadence (CoreNotificationSettingsService, CoreLotsService) are checked only by source text and a build.
    evidence: |-
      CoreNotificationSettingsService maps 26 snapshot fields and 11 actions, and CoreLotsService four new fields and two actions, into presentations whose initialisers have defaults. Dropping reminderCadence there, or sending setWindowTo to core.setWindowFromTime, compiles and passes every test: the presentation tests use their own fixtures, RenderTests use a spy service, and CI only builds the App target. An executed test needs a test target for apps/swift/ios/App (the same gap as DW-20 and the Story 6.2 item).
    location: >-
      apps/swift/ios/App/{CoreNotificationSettingsService,CoreLotsService}.swift
    severity: medium
---

<intent-contract>

## Intent

**Problem:** A User cannot say when Coldframe may notify them: there is no Notification Window, no time zone on the Server (the zone confirmed on Create Site stays on the device, DW-23), no mute, and no Reminder cadence for a User or a Site (DW-33). Story 6.4 needs all of these as persisted settings before it can time a delivery.

**Approach:** The User grain gains event-sourced notification settings (window, time zone, mute per Site, cadence per Site, cached Site cadence) and the Site grain a Reminder cadence. New endpoints expose them, and web, the shared Kotlin core, Android and iOS get a My notifications surface plus a Reminders section in Site settings. Clients send the device's stored or detected zone once and afterwards read it from the Server.

## Boundaries & Constraints

**Always:**
- Contract first. `GET`/`PATCH /me/notification-settings` (`getMyNotificationSettings`, `updateMyNotificationSettings`, authenticated caller): response `{ window: { from, to }, timeZone?, timeZoneConfirmed }`; request `{ window?: { from, to? }, timeZone?, detectedTimeZone? }`. `GET`/`PUT /sites/{siteId}/notification-settings` (`getSiteNotificationSettings`, `setSiteNotificationSettings`, Member): `{ muted, reminderCadence?, siteReminderCadence }`; request `{ muted, reminderCadence? }` (absent = use Site setting). `GET`/`PUT /sites/{siteId}/reminder-cadence` (`getSiteReminderCadence` Member, `setSiteReminderCadence` Administrator): `{ cadence }`. `ReminderCadence` is `daily | every2Days`. Times are `"HH:mm"` (24 h). Every write answers 200 with the state in force.
- Defaults without any event: window 07:00-22:00, no time zone, not muted, personal cadence unset, Site cadence `daily`. `window.to` omitted means 22:00. A window needs `from < to`.
- Time zone: `timeZone` in a request is the User's own choice and always wins. `detectedTimeZone` is stored only while the User has chosen none; it never replaces a chosen zone. `timeZoneConfirmed` is true only after a choice. Zones are IANA IDs the Server knows, at most 64 characters.
- Detection precedence for the proposal shown to the User: device or browser zone, then the Server's stored detected zone, then an IP lookup behind a Server seam (`IIpTimeZoneLookup`; the default knows no zone), then none (the User picks from the list).
- Every accepted change is one past-tense event on the grain that owns it (`user/{sub}` or `site/{id}`); an unchanged value journals nothing. New events get an alias, a `journal.json` fixture row and survive a silo restart. Time comes only from `TimeProvider`.
- Mute, personal cadence and the cached Site cadence are per User per Site and are dropped when the Membership ends. Another User's settings never change.
- The Site cadence reaches every member's User grain: after `setSiteReminderCadence` (also when unchanged, so a retry repairs a partial fan-out) and with every Membership sync from reconciliation. The User grain resolves cadence as own setting, else cached Site cadence, else `daily`.
- The role decides the Site cadence control: Owner and Administrator edit, Member sees the value as text with the existing read-only notice. Controls are hidden, never disabled.
- My notifications sits in Settings above Site settings on all three clients: Notification Window control (UX-DR47), Time-zone confirm panel (UX-DR48, the existing component reused), "Mute <Site>" native switch (UX-DR49), my Reminder cadence (UX-DR50). Mute and cadence need a current Site; without one only window and time zone show. Segmented choices and the switch apply at once; the window has a Save button. The 24 h bar is decorative and hidden from screen readers; the helper names the window's own start time.
- Strings are externalised, identical keys and values on Android and iOS, glossary terms capitalised. Test-first (NFR16): UX-DR47, 48, 49, 50 and 72 each name a failing test per client before implementation.

**Never:** No delivery, deadline, summary, Reminder scheduling, Notifier, push token or SignalR work (6.4-6.6). No "Browser notifications while Coldframe is open" toggle (UX-DR123, 6.6) and no notifications-off notice (UX-DR88, 6.5). No GeoIP database or external geolocation call. No "Never" cadence. No call from the Site grain to a User grain. No read model or migration for settings (grain reads, as Thresholds). No change to `POST /sites`. No write to `sprint-status.yaml`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| New User | `GET /me/notification-settings`, no events | window 07:00-22:00, no `timeZone`, `timeZoneConfirmed: false` | — |
| From only | `PATCH { window: { from: "06:30" } }` | window 06:30-22:00, one `user.notification-window-changed` | — |
| Bad window | `from` = `to`, `to` before `from`, `"7:00"`, `"24:00"` | nothing journaled | 400 `validation` |
| Detected, none chosen | `PATCH { detectedTimeZone: "Europe/Zurich" }` | `timeZone` Europe/Zurich, `timeZoneConfirmed: false` | — |
| Detected after choice | chosen Europe/Zurich, then `detectedTimeZone: "America/New_York"` | still Europe/Zurich, confirmed, nothing journaled | — |
| Chosen | `PATCH { timeZone: "Europe/Vienna" }` | Europe/Vienna, `timeZoneConfirmed: true` | — |
| Unknown zone | `timeZone: "Mars/Olympus"` or a Windows ID | nothing journaled | 400 `validation` |
| Mute only me | A mutes Site S; B is a member of S | A: `muted: true`; B: `muted: false` | — |
| My cadence | `PUT { muted: false, reminderCadence: "every2Days" }`, then `{ muted: false }` | first: `every2Days`; second: field absent (use Site setting) | — |
| Site cadence | Administrator `PUT { cadence: "every2Days" }` | 200; Site grain event; each member's User grain resolves `every2Days` unless it has its own | Member: 403; unknown value: 400 |
| Membership ends | muted Site, then Role removed | mute, cadence and cache for that Site are gone | — |
| Other Site | caller not a member of the Site | — | 404/403 as the matrix says |
| Stored device zone | device holds a zone from Create Site, Server unconfirmed | client sends it as `timeZone` once, then drops the device copy | send fails: copy kept for the next start |
| Save fails | Server unreachable on any write | control returns to the Server's value, notice with Try again | 401 signs out as elsewhere |

</intent-contract>

## Code Map

**Server**
- `packages/cs/contracts/Sites/SiteGrains.cs` -- `IUserGrain` l.10-32, `ISiteGrain` l.38-140, outcome/result model `SiteRenameOutcome` l.316-346; `UserEvents.cs`, `SiteEvents.cs` (alias + `[GenerateSerializer]` pattern). `packages/cs/README.md` l.33-37 table.
- `apps/cs/server/Identity/UserGrain.cs` (l.93-106 `SyncSiteMembership`), `UserState.cs` (next `Id(2)`; `Apply(SiteMembershipChanged)` l.77 removes the Site: clear per-Site settings there), `SiteGrain.cs` (`Rename` l.133-169 is the mutation model, `Result(...)` l.355-360 feeds reconciliation), `SiteState.cs` (next `Id(9)`).
- `apps/cs/server/Identity/Reconciliation/IdentityReconciliationActivities.cs` l.82-94 -- the only per-member fan-out; pass the Site cadence here. `SiteGrain.cs` l.14-15: the Site grain never calls User grains.
- `apps/cs/server/Edge/EdgeApi.cs` -- records l.27-366, `MapEdgeApi` l.409-502, Thresholds GET/PUT l.465-471 and handlers l.1677-1754 (grain read, outcome to HTTP, `InvalidThresholds`), `CallerId` l.1963, `ReadJsonAsync` l.2006; `EdgeValidation.cs` normalisers; `EdgeAccessRule.cs` l.30, l.62, l.73 (`RequireAuthenticatedCaller`, `RequireSiteRole`); `EdgeProblems.cs` l.69, l.105.
- `packages/openapi/coldframe.openapi.json` -- Thresholds path l.373 and schemas l.1179-1208 as template; `README.md` l.6-28.
- Tests: `tests/cs/server.tests/Identity/UserStateTests.cs`, `Edge/EdgeValidationTests.cs`, `Edge/EdgeEndpointDiscoveryTests.cs` l.40-60, `Fixtures/journal.json` (+ `Journal/FixtureJournalReplayTests.cs` l.207-218); `tests/cs/server.integration/Devices/ThresholdGrainTests.cs` (set, unchanged, restart l.119), `Devices/ThresholdEndpointTests.cs` (`SeedSiteAsync` l.255-276), `Identity/IdentityCluster.cs` (`User`, `Site`, `AliasesAsync` l.157, `RestartSiloAsync`), `Identity/IdentityReconciliationActivitiesTests.cs` l.59, `Edge/AuthorizationMatrixTests.cs` `_samples` l.37-157 (Sites are raw `site.*` events: the User grain there has no Membership, so per-Site writes must not require one).
- `apps/cs/server/Dockerfile` l.43 (`aspnet:10.0.12`): confirm IANA zones resolve (`TimeZoneInfo.TryFindSystemTimeZoneById` + `HasIanaId`). `BannedSymbols.txt` bans wall-clock reads.
- `apps/cs/README.md` -- endpoint table l.113-132, "Add an event type" l.74-81, Create Site l.151-163, fan-out l.189-192, l.484-487.

**Web**
- `apps/ts/web/src/routes/(app)/settings/+page.svelte` l.18-33 (index rows), `settings/site/+page.{server.ts,svelte}` (pattern B: named actions, `{action, done}`, "Saved." announcement l.94-127, role gate l.17, l.142-175), `src/lib/server/site-settings.ts`, `src/lib/lots.ts` l.8-68 (`siteSettingsOf`).
- `src/lib/server/sites.ts` `call` l.70-93, `statusError` l.44-63; `shell.ts` l.13-14 `cf_time_zone`, `rememberChoice` l.34, `signedOutRedirect` l.42, `loadShell`; `create-site.ts` l.23-33 `isTimeZone`, l.61-73 (cookie only); six loaders read the cookie (`lots.ts`, `lot-detail.ts`, `devices.ts`, `alerts.ts`, `calibrate.ts`, `thresholds.ts`).
- Components: `TimeZonePanel.svelte` (reusable as is), `SegmentedChoice.svelte` (no `name`: add form submission), `Hatch.svelte`, `InlineNotice`, `TextInput` (no `time` type). No Toggle exists.
- `src/lib/i18n/en.json` (`timeZone.helper` l.105 says "on this browser"), `format.ts`. `packages/ts/api-client` generate + aliases `src/index.ts` l.10-44.
- Tests: `tests/ts/web/{lots,thresholds,sites}.test.ts`, `fakes.ts`, `coverage.test.ts` l.4-11, `components.test.ts` l.246-268, `styles.test.ts` (no transition, no gradient, no hex, 44 px), `copy.test.ts`; `tests/ts/web.e2e/fixtures/fake-idp.ts` (state l.259-283, reset l.351-373, Thresholds routes l.705-772), `specs/helpers.ts` (`shellPages` l.159), `specs/site-settings.spec.ts`. `apps/ts/web/README.md` l.76-94, l.196, l.241-258.

**Shared Kotlin core** (`packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/`)
- `api/ColdframeApi.kt` l.48-61, GET l.241, PUT l.248, PATCH l.83, `failureOf` l.304; `api/ApiDtos.kt`; `tests/kt/core/jvmTest/.../api/OpenApiContractTest.kt` l.58-78, l.368-386.
- `thresholds/{Thresholds,ThresholdsEngine,ThresholdsSnapshot}.kt` -- editable engine, generation guard, 401 handling, flat snapshot; `alerts/AlertsEngine.kt` l.40-62 follows the Site.
- `sites/DeviceChoices.kt` l.18-26 (`timeZone.chosen`, survives sign-out), `SitesEngine.kt` l.44-52, l.67-82, l.153, l.187-207, l.336, `TimeZones.kt`, `Sites.kt` l.57-65, `SitesWiring.kt`, `signin/{AndroidSignIn,IosSignIn}.kt`, `iosMain/.../thresholds/IosThresholds.kt`.
- `lots/Lots.kt` l.116-127 (`SiteSettings.of(role)`), `LotsEngine.kt` l.319-336, `LotsSnapshot.kt` l.46-62, `iosMain/.../lots/IosLots.kt`.
- Tests `tests/kt/core/commonTest/.../{api/ColdframeApiTest,thresholds/ThresholdsEngineTest,sites/SitesEngineTest l.497-510,lots/*}.kt`. READMEs `packages/kt/core/README.md` l.154, `packages/kt/README.md` l.13-29.

**Android** (`apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/`)
- `ui/shell/AppShell.kt` l.132-145, l.179-183, l.304-315 (sub-screens), `ui/settings/SettingsScreen.kt` l.40-63, `SiteSettingsScreen.kt` l.106-137, `ui/sites/CreateSiteScreen.kt` l.131-241 (`TimeZonePanel` typed to `SitesActions`), `ui/components/{SegmentedChoice,InlineNotice,Hatch,DashedBorder}.kt`, `ui/thresholds/ThresholdsActions.kt`, `ColdframeRoot.kt`, `MainActivity.kt`, `res/values/strings.xml` (`time_zone_helper` l.137).
- Tests `tests/kt/android/test/.../{SnapshotTest l.123-212,ShellTest l.85,SiteSettingsScreenTest,StringsTest,CoverageTest l.9-174,AccessibilityTest,SourceScanTest}.kt`; baselines `tests/kt/android/snapshots/`.

**iOS** (`apps/swift/ios/`)
- `Sources/ColdframeIOS/{Presentations.swift l.88-111,SitesPresentation.swift l.70-101,LotsPresentation.swift l.807-865,ThresholdsPresentation.swift,L10n.swift,Resources/Localizable.xcstrings}`; `UI/{Screens.swift l.200-295, l.444-560,SiteSettingsViews.swift l.131-166,SitesViews.swift l.128-195,Components.swift l.190}`; `App/{CoreThresholdsService,CoreLotsService,ColdframeApp}.swift`.
- Tests `tests/swift/ios/ColdframeIOSTests/{ShellPresentationTests l.72-82,CatalogueTests l.102-130,ThresholdsPresentationTests,RenderTests,SourceRulesTests}.swift`. `apps/swift/README.md` l.12-56.

## Tasks & Acceptance

**Execution:**
- `tests/cs/server.tests/{Identity/UserStateTests.cs,Identity/SiteStateTests.cs,Edge/EdgeValidationTests.cs,Notifications/TimeZoneProposalTests.cs,Fixtures/journal.json}`, `tests/cs/server.integration/{Identity/NotificationSettingsGrainTests.cs,Edge/NotificationSettingsEndpointTests.cs,Edge/AuthorizationMatrixTests.cs,Identity/IdentityReconciliationActivitiesTests.cs}` -- failing tests first: one per matrix row, defaults, restart, mute scope, cadence resolution, fan-out and its retry, detection precedence -- NFR16
- `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md` -- the six operations and schemas `NotificationSettings`, `NotificationWindow`, `UpdateNotificationSettingsRequest`, `SiteNotificationSettings`, `SetSiteNotificationSettingsRequest`, `ReminderCadence`, `SiteReminderCadence` -- contract first
- `packages/cs/contracts/Sites/{SiteGrains,UserEvents,SiteEvents}.cs`, `packages/cs/README.md` -- grain methods, results, `ReminderCadence`, events `user.notification-window-changed`, `user.time-zone-detected`, `user.time-zone-chosen`, `user.site-mute-changed`, `user.site-reminder-cadence-changed`, `user.site-reminder-cadence-synced`, `site.reminder-cadence-changed`
- `apps/cs/server/Identity/{UserGrain,UserState,SiteGrain,SiteState}.cs`, `Reconciliation/IdentityReconciliationActivities.cs`, `apps/cs/server/Notifications/{IIpTimeZoneLookup,TimeZoneProposal}.cs` -- state, rules, resolution, cadence passed with Membership sync, lookup seam
- `apps/cs/server/Edge/{EdgeApi,EdgeValidation}.cs`, `apps/cs/README.md` -- records, mapping, handlers, validation, member fan-out after the Site write (a failed fan-out answers 503)
- `packages/ts/api-client/src/**` -- regenerate and re-export
- `tests/ts/web/{notifications.test.ts,lots.test.ts,sites.test.ts,coverage.test.ts,components.test.ts}`, `tests/ts/web.e2e/{fixtures/fake-idp.ts,specs/helpers.ts,specs/notifications.spec.ts,specs/site-settings.spec.ts}` -- failing tests first: page per role and without a Site, each action and its failures, zone hand-over, light/dark screenshots with axe
- `apps/ts/web/src/lib/{notifications.ts,server/notifications.ts,server/shell.ts,server/create-site.ts,server/site-settings.ts,lots.ts,components/{NotificationWindow,Toggle,SegmentedChoice}.svelte,i18n/en.json}`, `routes/(app)/settings/{+page.svelte,notifications/+page.server.ts,notifications/+page.svelte,site/+page.server.ts,site/+page.svelte}`, `apps/ts/web/README.md` -- surface, actions, Site cadence section, zone hand-over through the BFF
- `tests/kt/core/**` -- failing tests first: API calls and problem mapping, DTO mirrors, engine states, zone hand-over and sign-out, Site cadence in Site settings, snapshots
- `packages/kt/core/src/**/{api,notifications,sites,lots,signin}/**`, `packages/kt/core/README.md`, `packages/kt/README.md` -- `NotificationSettingsApi`, DTOs, `NotificationSettingsEngine`, snapshot, `IosNotificationSettings`, Site cadence in `LotsEngine`, wiring
- `tests/kt/android/test/**`, `tests/kt/android/snapshots/my-notifications-*.png`, `site-settings-*` -- failing tests first, then recorded baselines
- `apps/kt/android/src/main/**` -- `ui/notifications/{MyNotificationsScreen,NotificationWindowControl,NotificationSettingsActions}.kt`, shared `TimeZonePanel`, `SettingsScreen.kt`, `SiteSettingsScreen.kt`, `AppShell.kt`, `ColdframeRoot.kt`, `MainActivity.kt`, `strings.xml`, `apps/kt/README.md`
- `tests/swift/ios/ColdframeIOSTests/{NotificationSettingsPresentationTests,ShellPresentationTests,LotsPresentationTests,CatalogueTests,RenderTests}.swift` -- failing tests first
- `apps/swift/ios/**` -- `NotificationSettingsPresentation.swift`, `UI/NotificationSettingsViews.swift`, `Presentations.swift`, `LotsPresentation.swift`, `UI/{Screens,SiteSettingsViews,SitesViews}.swift`, `L10n.swift`, `Localizable.xcstrings`, `App/{CoreNotificationSettingsService,CoreLotsService,ColdframeApp}.swift`, `apps/swift/README.md` (SwiftUI and `App/` compile only in macOS CI)

**Acceptance Criteria:**
- Given a new User who reaches a Site on any client, when My notifications or Create Site shows the time zone, then the device or browser zone is proposed for Confirm or Change, and the window reads 07:00 to 22:00.
- Given a User who chose a zone, when any client starts on a device in another zone, then the Server and every client still show the chosen zone.
- Given My notifications on web, iOS or Android, when I change the window, the zone, the mute switch or my cadence, then the next load from the Server shows the change, and one event for it is on my User stream.
- Given an Owner or Administrator in Site settings, when they pick "Every 2 days", then the Site keeps it and every member's User grain resolves it; given a Member, then the cadence is text and no control is present.
- Given the endpoint discovery and authorization matrix tests, when they run, then all six operations are in the contract with their minimum Roles and pass every Role and Site case.
- Given the snapshot suites and the iOS render tests, when they run, then My notifications (with and without a Site, zone unconfirmed and confirmed) and Site settings with Reminders (editable and read-only) are covered in light and dark themes.

## Spec Change Log

## Review Triage Log

### 2026-10-09 — Review pass
- verdicts: 56 findings — high 0, medium 2, low 47, false 6, maybe-false 1
- findings:
  - `[low]` `[reject]` Blind: two concurrent Site cadence writes, or a write racing reconciliation, can leave a member's copy stale — real, but it needs two Administrators writing at once; the next write or reconciliation corrects it, and a version on the event adds state on both grains.
  - `[low]` `[patch]` Blind: after a 503 `reminder-cadence-not-delivered` web and the core showed the old cadence although the Site holds the new one — the control now shows the pick with the notice and Try again (web notice `cadenceNotDelivered`, `LotsEngine.cadenceFailed`); tests and comments updated.
  - `[low]` `[reject]` Blind: the per-Site PUT replaces mute and cadence together, so a stale second device can undo the other value — real for two devices with stale state; the full-replace shape is the contract, and a merge read per write adds a call and a race of its own.
  - `[low]` `[reject]` Blind: on a shared browser a zone left by a User who never signed out is sent as the next User's choice after a browser restart — needs two Users in different zones on one browser profile; binding the legacy cookie to a User would drop the DW-23 hand-over it exists for.
  - `[low]` `[patch]` Blind: the IP lookup is handed the connection's address, which is the BFF or a proxy for web traffic — the limitation is now stated on `IIpTimeZoneLookup`; no lookup ships.
  - `[low]` `[reject]` Blind: an unknown `detectedTimeZone` refuses the whole PATCH — no client sends a detected zone together with another field, and a refused proposal leaves the User without a stored zone, which the clients handle.
  - `[low]` `[reject]` Blind: on mobile a failed per-Site read fails the whole surface — real for a transient failure between two reads; Try again repairs it, and a partial state adds a third surface state on two platforms.
  - `[low]` `[reject]` Blind: `LotsEngine.readCadence` swallows a failed read, so Site settings on mobile shows no Reminders section and no notice — real for a transient failure; the next refresh reads again, and a notice adds state and strings on two platforms.
  - `[low]` `[reject]` Blind: an unknown cadence value from the Server is coerced and may be written back — the Server produces only `daily` and `every2Days`; not reachable today.
  - `[low]` `[reject]` Blind: a zone picked on Create Site while settings are loading is dropped when the Server already holds a chosen zone — needs a second Create Site with a different pick inside the load window; the Server's chosen zone stays, which My notifications shows and can change.
  - `[low]` `[patch]` Blind: on mobile a mute or cadence notice and its retry survived a Site switch — `NotificationSettingsEngine.follow` now drops the held per-Site change and clears the notice; test added.
  - `[low]` `[reject]` Blind: web offers Try again for requests the Server refused (`validation` on mute or cadence, an empty `siteId`) — the forms cannot produce those requests.
  - `[low]` `[patch]` Blind: the comment on `UserState.Set` claimed an entry is pruned once a cadence was synced — comment corrected.
  - `[false]` `[reject]` Blind: there is no way to un-choose a zone — the story asks that a chosen zone is never overwritten by detection; Change picks another one.
  - `[low]` `[reject]` Edge: interleaved fan-outs of two cadence writes — same as the first row.
  - `[low]` `[reject]` Edge: a caller that disconnects mid fan-out leaves it unlogged — the request is cancelled and unanswered; repeating it or reconciliation finishes the fan-out.
  - `[low]` `[reject]` Edge: a member removed between the member list and the fan-out regains an entry — reconciliation's `SyncSiteMembership(null)` drops it (the grain journals the end when it holds settings).
  - `[low]` `[reject]` Edge: a registered lookup that throws answers 500 — no lookup is registered; a deployment adding one owns its failures.
  - `[low]` `[patch]` Edge: `posixrules` and `Factory` were accepted as time zones — `TimeZoneProposal.IsKnown` refuses both; tests added.
  - `[low]` `[reject]` Edge: failed per-Site read fails the mobile surface — as the Blind row above.
  - `[low]` `[reject]` Edge: a failed refresh on mobile replaces the surface and loses an unsaved window draft — real for a transient failure on re-entry; keeping stale values without a notice is not a direct correction.
  - `[low]` `[patch]` Edge: notice and retry survive a Site switch — patched with the Blind row above.
  - `[low]` `[reject]` Edge: Create Site pick dropped when the Server is already confirmed — as the Blind row above.
  - `[low]` `[reject]` Edge: a Create Site zone that fails in transport after the hand-over is not sent again in that session — the device copy is kept and the next start sends it.
  - `[low]` `[reject]` Edge: full-replace PUT from stale state, or with an unknown cadence — as the two Blind rows above.
  - `[low]` `[reject]` Edge: a change made while the entry read is in flight drops that read — the change's own answer refreshes its resource; the other resource is read on the next entry.
  - `[low]` `[reject]` Edge: an older cadence GET may land after a pick's PUT in `LotsEngine` — a timing window of one request; the next read corrects it.
  - `[low]` `[patch]` Edge: `LotsEngine.cadenceFailed` reverts on 503 not-delivered — patched with the Blind row above.
  - `[low]` `[reject]` Edge: `ready()` rebuilt from Failed drops a pending pick — needs a list failure and recovery inside one cadence request.
  - `[low]` `[patch]` Edge: Android zone rows stayed tappable while a choice was on its way — `enabled = !working`.
  - `[low]` `[reject]` Edge: shared-browser zone after a restart — as the Blind row above.
  - `[low]` `[reject]` Edge: a Create Site zone the Server could not be asked about is lost when the Server holds another chosen zone — the Server's chosen zone stays and My notifications can change it.
  - `[low]` `[patch]` Edge: web Site cadence segment shows the old value after 503 — patched with the Blind row above.
  - `[low]` `[reject]` Edge: web mute or cadence submitted from a stale page — as the full-replace row above.
  - `[low]` `[reject]` Edge: web checks only `siteReminderCadence` for unknown values — the Server produces no other value.
  - `[low]` `[reject]` Edge: a zone the browser knows and the Server refuses is dropped on Create Site — pages then format in the browser zone, as before a choice.
  - `[low]` `[patch]` Edge: on a fresh browser the Create Site load read `cf_time_zone` before the layout's hand-over wrote it — the page load awaits `parent()` first (no unit test: the test package cannot import a route's `+page.server.ts`).
  - `[low]` `[reject]` Edge: mobile Create Site proposes the device zone while the settings read is pending — a window of one read; Confirm there is the User's own choice.
  - `[low]` `[patch]` Edge: the `LotsNoticeKind` doc claimed the control returns to the Server's value — patched with the 503 row.
  - `[medium]` `[patch]` Gap: the 503 `reminder-cadence-not-delivered` and 404 answers of `ToHttpResult` were asserted by no test — `SiteReminderCadenceResponseTests` added.
  - `[low]` `[patch]` Gap: the `if (!deleted)` guard in reconciliation was pinned by no test — case added (deleted Site with `Every2Days`, two runs, no new event).
  - `[low]` `[patch]` Gap: `SitesWiring.lots(cadence = null)` let a call site drop the cadence API silently — default removed.
  - `[medium]` `[defer]` Gap: the iOS `App/` adapters for the new fields and actions are checked only by source text — the App target has no test target (pre-existing, DW-20 and the 6.2 item); deferred.
  - `[low]` `[patch]` Gap: `NotificationSettingsActions.of` and the new `LotsActions.of` mappings were exercised by no test — `NotificationSettingsActionsTest` and one `LotsActionsTest` case added.
  - `[low]` `[patch]` Gap: sign-out deleting `cf_time_zone` and `cf_zone_sync` was not asserted — case added to `auth-handle.test.ts`.
  - `[low]` `[reject]` Gap: the IP lookup is never observed through the endpoint — the shipped lookup knows no zone, so nothing a deployment shows can regress; an endpoint test needs a second host configuration. To close when a real lookup is added.
  - `[low]` `[patch]` Gap (other): 503 not-delivered contradicts the Site's value — patched with the Blind row above.
  - `[low]` `[reject]` Intent: the IP tier is an ordering and a seam, no working lookup — a User without a device or browser zone gets no proposal and picks from the list; every supported phone and browser reports a zone, and a real lookup needs a geolocation data source nobody has chosen. Named under residual risks.
  - `[low]` `[reject]` Intent: a User who joins another's Site is not prompted to confirm until they open My notifications — the detected zone is stored and in force; UX-DR48 places the panel on Create Site and My notifications only.
  - `[false]` `[reject]` Intent: two UX-DR72 elements are absent — the epic gives the notifications-off notice to Story 6.5 (UX-DR88) and the browser toggle to Story 6.6 (UX-DR123).
  - `[false]` `[reject]` Intent: "from 07:00" defaults the end instead of keeping the current one — UX-DR47 says "defaulting the end to 22:00"; the To field always shows the current end.
  - `[false]` `[reject]` Intent: the cached Site cadence is read by nothing shipped — the story asks for the cache; Story 6.4 consumes it. Grain tests cover the resolution.
  - `[false]` `[reject]` Intent: mute is verified as state only — delivery is Story 6.4.
  - `[low]` `[reject]` Intent: no test crosses a client and the real Server — the repo's layering since Epic 1; the OpenAPI document is the joint, and Story 6.6 adds the end-to-end run.
  - `[maybe-false]` `[reject]` Intent: test-first cannot be seen in one diff — the implementer wrote tests first everywhere and ran them red on web and iOS; commit history would settle it. Low if true.
  - `[false]` `[reject]` Intent: the zone hand-over, dropping settings at Membership end and the 503 type are not named by the story — the epic context names the hand-over (DW-23); the others follow from "cached in each member" and "only me".

## Design Notes

- **Two scopes, three resources.** Window and zone belong to the User (`/me/…`, the first User-scoped path); mute and my cadence belong to the User on one Site, so they sit under `/sites/{siteId}` and get the Membership check and 404 from the policy. The Site cadence is its own resource read from the Site grain; `siteReminderCadence` in the per-Site response also comes from the Site grain, so the surface never shows a stale cache.
- **Fan-out without a cycle.** The Edge handler writes the Site grain, then calls `IUserGrain.SyncSiteReminderCadence` for each member the grain returned, as the reconciliation activity does for Memberships. It runs even when the value is unchanged, so repeating the request repairs a fan-out that failed half-way; reconciliation repairs it too.
- **Detected versus chosen.** Storing the detected zone gives 6.4 a zone before the User confirms. Only a choice sets `timeZoneConfirmed`; clients show the confirm panel until then. The IP lookup is a seam only: on a home network there is nothing to look up, and the default implementation returns no zone.
- **Hand-over of the device zone (DW-23).** Mobile: at session start the core reads the settings; a stored `DeviceChoices.timeZone` is sent as `timeZone` when the Server is unconfirmed and removed once the Server answered; otherwise the OS zone goes as `detectedTimeZone`. The copy is also removed at sign-out. Web: the BFF does the same from `cf_time_zone` once per browser session (a session cookie marks it), then keeps the cookie equal to the Server's zone so the existing loaders keep formatting with it. Create Site sends the confirmed zone to the Server with the same call.
- **Window as wall-clock minutes.** `from < to` within one day; a window across midnight is not offered. Events store minutes since midnight, the contract `"HH:mm"`.
- **Helper copy.** "Outside this window, anything waits for one summary at {from}." follows the window; the personal cadence helper names the Site setting ("Site setting: Daily").

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror && dotnet format --verify-no-changes --no-restore` -- expected: success, no changes
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- expected: pass (known host flakes: `KeycloakReconciliationTests.ARenameInKeycloakRenamesTheSite`, three `NodeMoveTests` reminder timeouts)
- `pnpm --filter @coldframe/api-client generate && pnpm -r typecheck && pnpm -r lint && pnpm -r test && pnpm --filter @coldframe/openapi run check` -- expected: pass, no drift; new screenshots recorded with `--update-snapshots` and committed
- `ANDROID_HOME=$HOME/Android/Sdk ./gradlew ktlintFormat check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64 --offline` -- expected: pass after `:android:recordRoborazziDebug` recorded the new baselines
- `podman run --rm --security-opt label=disable -v "$PWD":/work -w /work swift:6.3.3 sh -c 'swift build && swift test && swift format lint --strict -r .'` -- expected: pass

**Manual checks (if no CLI):**
- The macOS `swift` and `ios` CI jobs are the only compile check of the SwiftUI views and `apps/swift/ios/App`; read their result on the pull request.

## Auto Run Result

Status: done

**Summary:** A User now has notification settings on the Server: a Notification Window (default 07:00 to 22:00), an IANA time zone that is detected as a proposal and never replaces one the User chose, a mute per Site and a personal Reminder cadence per Site. A Site has a Reminder cadence ("Daily" / "Every 2 days") that Owners and Administrators set and that reaches every member's User grain. Six endpoints expose this, and web, the shared Kotlin core, Android and iOS have a My notifications surface and a Reminders section in Site settings. The zone confirmed on Create Site now goes to the Server (DW-23), and DW-33 is closed. Nothing is delivered or scheduled yet; that is Story 6.4.

**Files changed:**
- `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md`, `packages/ts/api-client/src/{schema,index}.ts` -- six operations, seven schemas, problem type `reminder-cadence-not-delivered`; regenerated client.
- `packages/cs/contracts/Sites/{SiteGrains,UserEvents,SiteEvents}.cs` -- grain methods, `ReminderCadence`, `NotificationWindow`, six User events and `site.reminder-cadence-changed`.
- `apps/cs/server/Identity/{UserGrain,UserState,SiteGrain,SiteState}.cs`, `Reconciliation/IdentityReconciliationActivities.cs` -- settings state and rules, cadence resolution, cadence passed with every Membership sync.
- `apps/cs/server/Notifications/{SiteReminderCadenceFanOut,TimeZoneProposal,IIpTimeZoneLookup}.cs`, `apps/cs/server/Edge/{EdgeApi,EdgeValidation,EdgeProblems,EdgeAuthentication}.cs` -- endpoints, validation, member fan-out, detection precedence and the IP lookup seam.
- `apps/ts/web/src/**` -- `/settings/notifications`, Reminders in Site settings, `Toggle`, `NotificationWindow`, `RetryNotice`, zone hand-over in the BFF (`shell.ts`, `create-site.ts`, `auth-handle.ts`).
- `packages/kt/core/src/**` -- `NotificationSettingsApi`, DTOs, `NotificationSettingsEngine`, snapshot, `IosNotificationSettings`, Site cadence in `LotsEngine`, wiring.
- `apps/kt/android/src/main/**` -- `ui/notifications/*`, shared `TimeZonePanel`, Settings row, Reminders in Site settings, strings.
- `apps/swift/ios/**` -- `NotificationSettingsPresentation.swift`, `UI/NotificationSettingsViews.swift`, Site settings, `App/CoreNotificationSettingsService.swift`, catalogue.
- `tests/**` -- Server unit and integration tests, web unit and Playwright tests with light/dark screenshots, core and Android tests with Roborazzi baselines, Swift presentation and render tests. Re-recorded baselines: `create-site-*` (web, Android) and web `site-settings-*`.
- READMEs of `apps/cs`, `packages/cs`, `apps/ts/web`, `apps/kt`, `apps/swift`, `packages/kt`, `packages/kt/core`.

**Review findings:** 56 findings from four layers. Patched 17 rows (12 distinct fixes: 1 medium, 11 low). Deferred 1 (medium: iOS App adapters checked only by source text). Rejected 38: 31 low, 6 false and 1 maybe-false; each reason is in the Review Triage Log. The rejected low findings are mostly races and stale-state cases (concurrent cadence writes, full-replace per-Site PUT from a second device, a shared browser after a restart, transient read failures on mobile) that the next read, write or reconciliation corrects.

**Follow-up review recommendation:** false. Patched by verdict: high 0, medium 1, low 11.

**Verification:**
- `dotnet restore --locked-mode`, `dotnet build -warnaserror`, `dotnet format --verify-no-changes` -- pass; `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- 1134 passed, 0 failed.
- `pnpm` generate, typecheck, lint, test, openapi check -- pass: web unit 643, Playwright 101, no schema drift.
- `./gradlew ktlintFormat check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64 --offline` -- BUILD SUCCESSFUL, Roborazzi baselines verified.
- `swift build && swift test && swift format lint --strict -r .` in `swift:6.3.3` -- 309 tests passed, lint clean.
- Every row of the I/O matrix has a covering test that ran and passed.

**Residual risks:**
- The IP tier of time-zone detection is an ordering and a seam only: no lookup ships, so a User whose device or browser reports no zone gets no proposal and picks from the list.
- The SwiftUI views, `apps/swift/ios/App` and the new iOS render tests are first compiled and run by the macOS `swift` and `ios` CI jobs; read their result on the pull request.
- No device, emulator or screen-reader run on any platform. Native time fields follow the device's 12/24 h setting while the large range reads 24 h.
- A User who joins another User's Site is not asked to confirm the zone until they open My notifications; the detected zone is in force until then.
- On web, My notifications without a current Site is unreachable (no Membership redirects to Create Site), so that state has unit tests and no screenshot.
