---
title: 'Story 6.5: Push notifications on my phone'
type: 'feature'
created: '2026-10-09'
baseline_revision: e031c31ad26de848be5cdf964eb7da58ef5c0d6c
status: 'awaiting-operator'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
  - '{project-root}/apps/cs/README.md'
  - '{project-root}/apps/kt/README.md'
  - '{project-root}/apps/swift/README.md'
warnings: ['oversized']
deferred:
  - summary: >-
      No test protects the [AlwaysInterleave] on ISensorGrain.GetThresholds that keeps a push lookup from deadlocking with the Alert fan-out.
    evidence: |-
      grep -rniE "AlwaysInterleave|interleav" tests/cs returns nothing. Every push test awaits Ingest to completion before the wake, so GetThresholds always finds the Sensor grain idle; removing the attribute should fail no test. A deterministic test needs a gate inside the Sensor's call filter while a User grain wake is in flight.
    location: >-
      packages/cs/contracts/Sensors/SensorGrains.cs (GetThresholds), apps/cs/server/Notifications/Push/PushLookups.cs (ThresholdAsync)
    severity: medium
  - summary: >-
      Nothing fails if Program.cs stops calling AddPushChannels() or the AppHost stops forwarding the Push settings.
    evidence: |-
      PushHostingTests calls the IServiceCollection overload on a bare ServiceCollection, and IdentityCluster adds the channels directly with AddApnsChannel()/AddFcmChannel(). The AppHost suites run without credentials, where "no channel" is also correct. The repository has no in-process host test for Program.cs; an AppHost fixture that reads the Server's start-up log (PushChannelNotConfigured) would close it.
    location: >-
      apps/cs/server/Program.cs:21, aspire/Coldframe.AppHost/AppHost.cs (pushSettings loop)
    severity: medium
  - summary: >-
      No test runs MainActivity handing a tapped notification's intent to the core (onCreate and onNewIntent, launchMode singleTop).
    evidence: |-
      PushNotificationsTest tests PushIntents.open/routing in isolation; grep for onNewIntent, singleTop and launchMode in tests/kt/android returns nothing. Deleting openedFrom(intent) in onNewIntent fails no test. The signed-out real core drops a tap, so observing it needs a seam in MainActivity. The permission report on launch is covered since the review (MainActivityTest).
    location: >-
      apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/MainActivity.kt (onCreate, onNewIntent)
    severity: medium
  - summary: >-
      The iOS App/ push adapter (AppDelegate, PushInbox, CorePushService) is checked only by compilation on macOS CI and a source-text scan.
    evidence: |-
      PushPresentationTests.noRestCallFromTheShell asserts that CorePushService.swift contains certain strings; nothing executes PushInbox buffering, the delegate callbacks or registerForRemoteNotifications. App/ has no test target. Moving PushInbox into Sources/ColdframeIOS would make its buffering testable; the rest needs a device (docs/bench/push-checklist.md).
    location: >-
      apps/swift/ios/App/AppDelegate.swift, apps/swift/ios/App/CorePushService.swift
    severity: medium
operator_actions:
  - "Read the macOS CI jobs swift and ios on the pull request of this story: apps/swift/ios/App/AppDelegate.swift, App/CorePushService.swift, UI/PushViews.swift and the edited SwiftUI views were never compiled on the Linux build host; fix any compile error they report."
  - "In the Apple Developer account, create an APNs auth key (Keys, Apple Push Notifications service), note its Key ID and your Team ID, and make sure the App ID of the iOS bundle identifier has the Push Notifications capability."
  - "Create a Firebase project with an Android app for the package com.escendit.coldframe, enable the Firebase Cloud Messaging API (V1), and generate a service-account key with the role Firebase Cloud Messaging API Admin."
  - "Create the Secret coldframe-push in the cluster namespace with apns-key.p8, apns-key-id, apns-team-id and fcm-service-account.json (deploy/SECRETS.md), set push.apns.enabled, push.apns.topic and push.fcm.enabled in the server chart values, roll out the server, and confirm the start-up log says PushChannelConfigured for APNs and FCM."
  - "Build the iOS app on a Mac with COLDFRAME_PUSH = YES and your DEVELOPMENT_TEAM in apps/swift/ios/Config/Coldframe.local.xcconfig, and install it on an iPhone."
  - "Build the Android app with -Pcoldframe.firebaseProjectId, -Pcoldframe.firebaseApplicationId, -Pcoldframe.firebaseApiKey and -Pcoldframe.firebaseSenderId of your Firebase project, and install it on an Android phone."
  - "Run docs/bench/push-checklist.md on the iPhone and on the Android phone and record the results: the permission prompt on the first Site overview, a real Alert push with the expected text, grouping per Site without a badge, the Reminder, the morning summary, the tap routes, the notifications-off notice, and sign-out."
---

<intent-contract>

## Intent

**Problem:** The User grain decides when a notification is due (6.4), but no channel exists: nothing reaches a phone. The apps never ask for notification permission, hold no push token, and cannot open the right screen from a notification.

**Approach:** The mobile apps ask for permission on the first Site overview and register the device's push token through the shared Kotlin core; the User grain owns the tokens. Two channels behind the Notifier seam (APNs, FCM) look up names and values, write the text on the Server, and send it grouped per Site. A tap opens Lot detail or the Site overview, and a denied permission is shown as a persistent notice.

## Boundaries & Constraints

**Always:**
- Tokens: a device registration (installation id, platform `apns | fcm`, token, and for APNs its environment `production | sandbox`) is owned by the User grain as events on `user/{sub}`. Registering the same installation again replaces its token; an unchanged registration writes no event. New REST operations under `/me` register and remove one registration (authenticated caller, Problem Details, authorization-matrix sample, OpenAPI first).
- A channel runs inside the notified User grain's turn, so it never calls that grain. The grain puts what only it knows into the `Notification` (the registrations, the time zone and window the summary footer needs); invalid tokens come back through the seam and the grain journals their removal. Channels hold no timing, window, mute or cadence logic.
- Channel results: a User without a registration for a channel is a success with nothing sent. A token the provider reports as invalid or unregistered (APNs `410`, `BadDeviceToken`, `DeviceTokenNotForTopic`; FCM `UNREGISTERED`, invalid registration) is removed and is not a failure. A transient failure is retried inside the channel within a bound of a few seconds, then the channel throws. Every send carries a stable collapse identity (Alert or Site, kind, `dueAt`), so a repeated send replaces the earlier one on the device.
- Text is written on the Server from externalised English templates and is self-contained: title is the condition, body is value and context (UX-DR116). Reminders reuse the Alert text with "Still" (UX-DR119). A summary is one notification: title `<Site>: 2 need water, 3 to check` with plural rules, one line per entry with needs-water first, footer `Held overnight, 22:00–07:00` from the User's window (UX-DR118). Soil moisture is rounded to 5 % with the `~` prefix, using the Server's existing rounding.
- Presentation (UX-DR121): grouped per Site (APNs `thread-id`, Android notification group), standard interruption level, no badge. The payload carries the Site, kind, Lot and Alert ids that tap routing needs. The payload contract and example fixtures are authored in `packages/asyncapi`; Server and Kotlin core tests read those fixtures.
- Credentials: read from configuration bound to the reserved Secret `coldframe-push` (optional in the `server` chart). A provider without credentials registers no channel and logs that once at start; the Server starts and tests pass without any credential. Provider base URLs are configurable so tests point them at stubs. Time for provider tokens comes from `TimeProvider`.
- Kotlin core owns registration, permission state and tap routing; shells hold UI and OS calls only. The core sends the token after sign-in and when it changes, and removes the registration (best effort) at sign-out.
- Permission (UX-DR122): asked once per device on the first landing on a Site overview, after the line "Coldframe tells you when a Lot needs water." Android below 13 has no prompt. Denied or revoked (UX-DR88): My notifications shows the persistent notice "Notifications are off for Coldframe on this phone. You won't get Alerts." with Open Settings, the overview shows the same notice above the tiles, not dismissable; rechecked on every foreground.
- Tap (UX-DR120): switch to the notification's Site, then Lot detail for `alert` and `reminder`, the Site overview for `summary`; on a cold start the route waits until Sites are loaded. An unknown Site or Lot ends on the overview of the current Site.
- Android and iOS builds, tests and CI pass without Firebase or APNs credentials; an app built without them works and registers nothing.
- Strings externalised with key parity between Android and iOS. Test-first (NFR16): named tests for UX-DR88 and UX-DR115 to UX-DR122.

**Never:** No Health Alert payloads or Devices-row routing (Epic 7). No SignalR or browser notification (6.6). No change to when a notification is due. No notification originated by a client, no actions on a notification, no badge. No committed credential, `google-services.json` or provisioning profile. No new REST call from a shell. No write to `sprint-status.yaml`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Low soil Alert | Lot "Tomatoes" at 20 %, low 30 % | "Tomatoes needs water" / "~20 % in the soil, your low is 30 %." | value or Threshold not found: title only |
| High soil Alert | 65 %, high 60 % | "Herbs too wet" / "~65 % in the soil, your high is 60 %." | — |
| Other quantity | temperature below low 5 °C | "Tomatoes temperature below 5 °C" / "Reading 4 °C at 05:15." (User's zone) | — |
| Reminder | same Alert, kind `reminder` | same title, body "Still ~20 % in the soil, your low is 30 %." | — |
| Summary | 2 needs-water and 3 other entries | one notification, title "Home garden: 2 need water, 3 to check", 5 lines, footer | Lot gone: its line is left out |
| Two devices | iPhone and Android registered | one APNs and one FCM send, same text | one provider down: the other still delivers |
| Invalid token | APNs `410` or FCM `UNREGISTERED` | registration removed from the grain; send counts as delivered | — |
| Transient failure | provider `503` on every try | channel throws; grain sends again at the next wake | repeat replaces the earlier one (collapse identity) |
| No registration | User with no device | nothing sent, no failure, no retry | — |
| No credentials | Secret absent | no channel registered, one log entry, Server healthy | — |
| Token rotated | same installation, new token | one registration with the new token | — |
| Register invalid | empty token, unknown platform | `400` Problem Details | — |
| First overview | permission never asked | why-line, then the OS prompt, once | — |
| Denied | permission off | notice in My notifications and on the overview | granted later: both gone at the next foreground |
| Tap Alert | `alert` for another Site | Site switched, Lot detail open | Lot unknown: overview |
| Tap summary | `summary` | that Site's overview | — |
| Sign out | registered device | registration removed before the session ends | request fails: sign-out still completes |

</intent-contract>

## Code Map

Server (`apps/cs/server`, `packages/cs/contracts`, `tests/cs`):
- `Notifications/INotificationChannel.cs:20`, `INotifier.cs:20`, `Notifier.cs:76-114` (all channels tried; throws only when every channel threw; EventId 2), `NotificationsHostingExtensions.cs:16-25` (channel = `AddSingleton<INotificationChannel, T>`). `packages/cs/contracts/Notifications/Notifications.cs`: `Notification` Id 0-5, `NotificationEntry` Id 0-7, no names or tokens.
- `Identity/UserGrain.cs:520-545` awaits the Notifier in the grain turn, not reentrant (a call back deadlocks); settings-write pattern l.222-319; next LoggerMessage EventId 6. `UserState.cs` next `Id(9)`. `packages/cs/contracts/Sites/UserEvents.cs` event pattern; `IUserGrain` in `Sites/SiteGrains.cs:11-108`. Event checklist `apps/cs/README.md:74-81`; fixture `tests/cs/server.tests/Fixtures/journal.json` (user stream at version 17) and `Journal/FixtureJournalReplayTests.cs:198,227`.
- `Edge/EdgeApi.cs:539-545,713-760,260-300` (`/me/notification-settings` pattern), `EdgeProblems`, `EdgeValidation.cs`; endpoint checklist `apps/cs/README.md:910-930`; `packages/openapi/coldframe.openapi.json` hand-authored (`x-coldframe-minimum-role`), table in `packages/openapi/README.md`; guards `tests/cs/server.tests/Edge/{EdgeEndpointDiscoveryTests,EdgeEndpointCatalog}.cs`, `tests/cs/server.integration/Edge/AuthorizationMatrixTests.cs:72-77`; `pnpm --filter @coldframe/api-client generate` refreshes `packages/ts/api-client/src/schema.ts`.
- Lookups: `Identity/IdentityReadModel.cs:52,81` (Site name), `Lots/LotsReadModel.cs:26-37,118` (`LotView.Name`, `MoisturePercent`), thresholds via `EdgeApi.EffectiveLowPercentAsync` l.1082-1099 / `ISensorGrain.GetThresholds()` (check for a call cycle with the Alert fan-out before calling a grain from a channel), `Sensors/CalibrationMath.cs:22-51` (5 % step), `Lots/SensorConversion.cs:35`.
- Config: options pattern `Identity/IdentityHostingExtensions.cs:24-43`; `aspire/Coldframe.AppHost/AppHost.cs:35-65,112-125`; `deploy/SECRETS.md:22,84-87`, `deploy/charts/server/templates/deployment.yaml:65-83`, `deploy/charts/test.sh`. No APNs or Firebase package; BCL `ECDsa`/`RSA` and `Microsoft.IdentityModel.JsonWebTokens` are present; restore is locked (a new package means regenerated lock files). `BannedSymbols.txt` bans wall-clock reads.
- Tests: `tests/cs/server.integration/Identity/{IdentityCluster.cs:246-247,RecordingNotificationChannel.cs,DeliveryTimingGrainTests.cs}` (cluster on the AppHost's Postgres), `Edge/EdgeApiFixture.cs`; HTTP stub pattern `tests/cs/server.tests/Identity/PhaseTwoOrganizationsTests.cs:303-330`; `tests/cs/server.tests/Notifications/NotifierTests.cs`.
- `packages/asyncapi/README.md` is the only file there; CI job `contracts` covers the folder.

Kotlin core and Android (`packages/kt/core`, `apps/kt/android`, `tests/kt`):
- `api/ColdframeApi.kt:50,333`, `api/ApiDtos.kt`; `tests/kt/core/jvmTest/.../api/OpenApiContractTest.kt`. Pattern to copy: `notifications/{NotificationSettings,NotificationSettingsEngine,NotificationSettingsSnapshot}.kt` (session hand-over l.85-96, 260-318), `iosMain/.../notifications/IosNotificationSettings.kt`, wiring `sites/SitesWiring.kt:71`, `signin/{AndroidSignIn.kt:75,IosSignIn.kt}`, `sites/DeviceChoices.kt`, `sites/SitesEngine.kt:149,187` (`onSessionEnded` runs after the session ended; `select` needs `Ready`), `signin/SignInEngine.kt:67`, `lots/LotDetailEngine.kt:165-175` (needs the Site and a name; resets on a Site change).
- Android: `ColdframeRoot.kt:74,107`, `MainActivity.kt:114-120` (no `onNewIntent`, no `launchMode`), `ui/shell/AppShell.kt:79,110,139,144,291-294`, `ui/sites/GardenScreen.kt:94,188,209`, `ui/notifications/MyNotificationsScreen.kt:51,84`, `ui/components/InlineNotice.kt:37`, permission pattern `ui/setup/SetupFlowShell.kt:138-164,244`, `src/main/AndroidManifest.xml` (no `POST_NOTIFICATIONS`, no service), `res/values/strings.xml`, `gradle/libs.versions.toml` (no Firebase entry; earlier runs were `--offline`).
- Tests: `tests/kt/core/commonTest` fakes, Ktor `MockEngine` (`signin/Fakes.kt:253`); `tests/kt/android` Robolectric, `SnapshotTest.kt:219,483` (Roborazzi, `snapshots/<name>-{light,dark}.png`, record with `:android:recordRoborazziDebug`), guards `StringsTest`, `SourceScanTest`, `CoverageTest` (`storyIds`).

iOS (`apps/swift/ios`, `tests/swift`): `project.yml` (XcodeGen, no entitlements, no AppDelegate), `App/ColdframeApp.swift:54-63` (foreground hook), `App/CoreNotificationSettingsService.swift` (adapter pattern; only `App/` imports `ColdframeCore`), `Sources/ColdframeIOS/UI/{Screens.swift:79,324,478,SitesViews.swift:234,286-288,308-332,AlertsViews.swift:82,NotificationSettingsViews.swift:56,96,HubSetupViews.swift:352-355,384-390,Components.swift:67}`, `Resources/Localizable.xcstrings`, `L10n.swift`; tests `tests/swift/ios/ColdframeIOSTests/{*PresentationTests,CatalogueTests,SourceRulesTests,RenderTests}.swift`. SwiftUI views and `App/` compile only in the macOS CI jobs `swift` and `ios`.

Docs: `docs/bench/*-checklist.md` (checklist form), `docs/quickstart.md:167-186`, `docs/operations/install.md`.

## Tasks & Acceptance

**Execution:**
- `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md`, `packages/ts/api-client/src/schema.ts`, `packages/asyncapi/{coldframe.asyncapi.json,fixtures/*,README.md}` -- the `/me` registration operations; the push payload contract with one fixture per kind -- contracts first
- `tests/cs/server.tests/Notifications/{PushTextTests,ApnsChannelTests,FcmChannelTests}.cs`, `tests/cs/server.tests/Identity/UserStatePushTests.cs`, `Fixtures/journal.json`, `tests/cs/server.integration/Identity/PushDeliveryGrainTests.cs`, `Edge/{PushRegistrationTests,AuthorizationMatrixTests}.cs` -- failing tests first: every Server row of the matrix, request shape and grouping against stub handlers, fixtures equal to the sent payload, restart keeps registrations -- NFR16
- `packages/cs/contracts/{Notifications/Notifications.cs,Sites/SiteGrains.cs,Sites/UserEvents.cs}`, `apps/cs/server/Identity/{UserGrain,UserState}.cs`, `apps/cs/server/Notifications/{INotifier,Notifier,INotificationChannel}.cs` -- registrations as events, registrations and zone in the `Notification`, invalid tokens returned through the seam and journaled
- `apps/cs/server/Notifications/Push/*`, `NotificationsHostingExtensions.cs`, `apps/cs/server/Edge/EdgeApi.cs` -- options, text templates, APNs and FCM channels, lookups, endpoints
- `aspire/Coldframe.AppHost/AppHost.cs`, `deploy/charts/server/**`, `deploy/SECRETS.md`, `deploy/charts/test.sh` -- optional credentials from `coldframe-push`
- `tests/kt/core/**/push/*`, `tests/kt/core/jvmTest/.../api/OpenApiContractTest.kt`, `packages/kt/core/src/**/{api,push,sites,signin}/*` -- failing tests first, then the registration API, the push engine (permission state, ask-once, token hand-over, sign-out removal), payload parsing and the tap route, iOS and Android facades
- `tests/kt/android/**`, `apps/kt/android/**`, `gradle/libs.versions.toml` -- failing tests first (including light and dark snapshots of the why-line and both notices), then permission flow, notices, Firebase messaging service with manual options from build properties, notification group and channel without badge, tap intent routing
- `tests/swift/ios/ColdframeIOSTests/*`, `apps/swift/ios/**` -- presentation tests first, then entitlements, app delegate and notification-centre adapter in `App/`, notices, hoisted Lot-detail route, strings
- `docs/bench/push-checklist.md`, `docs/quickstart.md`, `apps/cs/README.md`, `apps/kt/README.md`, `apps/swift/README.md` -- real-push checklist for an iPhone and an Android phone, credential setup, how the channels work

**Acceptance Criteria:**
- Given a real Sensor crossing its low Threshold in the integration cluster and a member with an APNs and an FCM registration, when the Alert opens inside the window, then each stub provider receives one request whose text, Site grouping and routing data equal the `packages/asyncapi` fixture, with no badge.
- Given a provider that answers "unregistered" for a token, when a notification is sent, then the User stream holds the removal and the next notification is not sent to that token.
- Given the Server running on the Aspire AppHost, when a signed-in User registers and removes a device over REST, then both succeed, a second identical registration changes nothing, and the operations pass the authorization matrix.
- Given the Android app with permission denied, when My notifications and the overview are rendered in light and dark, then both show the notice with Open Settings, and the snapshots are recorded.
- Given a Server, an Android build and an iOS build without push credentials, when their test suites run, then all pass.
- Given the manual checklist, when an operator follows it with their own credentials, then it leads to a real push on an iPhone and on an Android phone and names the expected text, grouping and tap result.

## Spec Change Log

## Review Triage Log

### 2026-10-09 — Review pass
- verdicts: 54 findings — high 0, medium 22, low 24, false 8, maybe-false 0
- findings:
  - `[medium]` `[patch]` Blind: a channel that delivered to one device and failed on another threw, losing the delivered count and the invalid tokens of that call — `PushChannel.SendAsync` now answers delivered and invalid installations whenever a device has the push, and throws only when none has it and one failed transiently; that exception still names the invalid installations (`IInvalidInstallationsSource`, read by the Notifier). Tests added in `ApnsChannelTests`, `FcmChannelTests`.
  - `[medium]` `[patch]` Blind: a permanent refusal left the delivery due and was sent to the provider again on every 5 s wake — a `Refused` outcome is now logged as an error (`PushRefused`, status and reason, no token) and does not leave the delivery due; the two refusal tests and the README table follow.
  - `[low]` `[reject]` Blind: a summary of 12 Lots with long non-ASCII names can pass 4 KiB — needs twelve held Alerts with names near the 100-character limit; since the refusal fix the outcome is one lost summary with an error log, and measuring and trimming the payload is more than a direct correction.
  - `[medium]` `[patch]` Blind: a Lot-name lookup that failed or ran out of time was read as "the Lot is gone", so a short database stall dropped the whole summary, which was then journaled as sent — a failed lookup now keeps the line under the `lot.unknown` name; only a Lot looked up and not found is left out (`PushEntryFacts.LotGone`). Test added in `PushTextTests`.
  - `[medium]` `[patch]` Blind: the `push-apns` and `push-fcm` clients inherited the standard resilience handler (`ServiceDefaultsExtensions.cs:50-54`), whose retry and circuit breaker sat under the channel's own bounded retry — both clients now call `RemoveAllResilienceHandlers()`; `PushHostingTests` checks they carry none.
  - `[medium]` `[reject]` Blind: `DeviceTokenNotForTopic` and `BadDeviceToken` also come back when `push.apns.topic` or the environment is wrong, so a Server misconfiguration removes iPhone registrations — real, but the intent contract lists both as invalid tokens, so the fix would edit this build's spec. The app registers again at its next start. Named under residual risks.
  - `[low]` `[reject]` Blind: the time inside one User grain turn is not bounded across channels and notifications (about 12 s per notification with both providers down) — an outage case; callers that time out repeat (the Alert report loop, the app at its next foreground). Concurrent channels or a wake budget change the 6.4 seam. Named under residual risks.
  - `[low]` `[reject]` Blind: a phone whose sign-out removal failed still shows the previous User's Alerts, and no client drops a push while signed out or deletes its token — the intent makes the removal best effort and the design notes name the risk; deleting the OS token needs a new registration path on both platforms.
  - `[medium]` `[patch]` Blind: `ColdframeMessagingService.onNewToken` called `PushEngine.tokenReceived` on a Firebase worker thread while the engine is otherwise used on the main dispatcher — the token is now posted to the main looper.
  - `[low]` `[reject]` Blind: on iOS the installation ID and the asked flag live in `NSUserDefaults`, which a device transfer copies — needs a restore onto a second iPhone while the first stays in use; a store that never migrates is new platform code that cannot be compiled here. Named under residual risks.
  - `[low]` `[reject]` Blind: the token is registered whatever the permission, so a phone with notifications off still receives data messages — a User who turned notifications off sees nothing either way; unregistering on denial adds a state machine.
  - `[low]` `[patch]` Blind: a repeated send replaced the Android notification but sounded again — `setOnlyAlertOnce(true)`, asserted in `PushNotificationsTest`.
  - `[low]` `[reject]` Blind: a tapped notification can wait without bound — the signed-out part is refuted by `uxDr120ATapWhileSignedOutLeadsNowhere`; what remains needs a tap for a Site this phone has not cached while the Server cannot be read.
  - `[low]` `[patch]` Blind: a Lot or Site named like a placeholder (`{unit}`) was rewritten by a later argument — `PushText.Format` substitutes in a single pass; test added.
  - `[low]` `[patch]` Blind: iOS never asked APNs again in the same run after a failed registration — the `registering` flag is gone; `CorePushService` asks on every report that finds the permission granted (uncompiled here).
  - `[low]` `[reject]` Blind: the AppHost hands the push credentials on as plain environment values — the local development stack only, with the operator's own variables; nothing is written to the repository.
  - `[medium]` `[patch]` Edge: one device delivered and another of the same channel failed — same defect and fix as the first Blind row.
  - `[medium]` `[patch]` Edge: `Refused` thrown as a failure and retried without end — same as the second Blind row.
  - `[low]` `[reject]` Edge: summary of long non-ASCII names over 4 KiB — as the third Blind row.
  - `[low]` `[patch]` Edge: a name containing a later placeholder — same as the placeholder Blind row.
  - `[medium]` `[reject]` Edge: `DeviceTokenNotForTopic` removes registrations on a wrong topic — as the Blind row on APNs reasons: the intent contract lists it.
  - `[low]` `[reject]` Edge: the same token registered by a second User is not taken from the first — registrations are per User by intent; a token-to-User index is a new read model. As the shared-phone Blind row.
  - `[low]` `[reject]` Edge: a failed sign-out removal is not kept for a later retry — after sign-out there is no session to send it with; as the shared-phone Blind row.
  - `[low]` `[reject]` Edge: an FCM message that arrives while signed out is shown — a guard on the sign-in state would also drop a legitimate push that starts the process before the session is restored; as the shared-phone Blind row.
  - `[medium]` `[patch]` Edge: `onNewToken` off the main thread — same as the Blind row.
  - `[low]` `[reject]` Edge: the asked flag set while the OS says not determined (restored backup) leaves the notice without a prompt — Android reports "not held" as not determined, so the flag cannot simply be ignored; needs a restore onto another phone.
  - `[low]` `[reject]` Edge: the installation ID copied by an iOS backup — as the Blind row.
  - `[low]` `[reject]` Edge: a tap for a Site absent from the cache waits while the Server read fails — as the Blind row on waiting taps.
  - `[low]` `[reject]` Edge: several notifications in one wake with a slow provider exceed the response timeout — as the Blind row on the turn's time.
  - `[low]` `[patch]` Edge: `registering` stays true after a failed APNs registration — same as the Blind row.
  - `[medium]` `[patch]` Edge: the hoisted `gardenPath` outlived sign-out, so the next User started on the previous User's Lot detail route — the root view resets it to `.root` when the surface leaves signed-in (`Screens.swift`, uncompiled here).
  - `[medium]` `[patch]` Edge (claim): "a provider answering unregistered means the removal is journaled" did not hold while a sibling device failed — same fix as the first Blind row; `PushDeliveryGrainTests` now has APNs 410 with FCM 503.
  - `[medium]` `[patch]` Gap: the grain's removal of invalid tokens after a failed send had no test — `PushDeliveryGrainTests`: APNs 410 and FCM 503 on every try journal `user.push-device-removed` and no `user.notification-sent`.
  - `[medium]` `[patch]` Gap: the real `PushLookups` ran only for a calibrated soil Sensor on a live Lot — added an air-temperature push through the real lookups ("Tomatoes temperature below 5 °C" / "Reading 4 °C at 12:00.") and a summary whose Lot was removed.
  - `[medium]` `[defer]` Gap: `[AlwaysInterleave]` on `GetThresholds` is protected by no test — a deterministic test needs a gate in the Sensor's call filter; recorded in `deferred`.
  - `[medium]` `[patch]` Gap: no test exercised the time bounds — added a lookup that never answers (cut off by the lookup budget), a provider that never answers (ends within the send budget) and out-of-range options that fail validation.
  - `[medium]` `[defer]` Gap: nothing fails if `Program.cs` stops calling `AddPushChannels()` — the repository has no host test for `Program.cs`; recorded in `deferred`.
  - `[medium]` `[patch]` Gap: `MainActivity`'s push glue ran in no test — `MainActivityTest` launches the real activity with `POST_NOTIFICATIONS` granted and waits for the core to report granted; the tapped-intent half needs a seam and is recorded in `deferred`.
  - `[medium]` `[defer]` Gap: the iOS `App/` adapter is checked by compilation and a text scan only — `App/` has no test target; recorded in `deferred` and covered by the operator checklist.
  - `[medium]` `[patch]` Gap (other): invalid tokens discarded when a sibling device failed — same as the first Blind row.
  - `[false]` `[reject]` Intent: a real push on a phone is exercised only against stub providers — the intent assigns the real-phone run to an operator; it is in `operator_actions` and `docs/bench/push-checklist.md`.
  - `[false]` `[reject]` Intent: the spec was `in-review` without `operator_actions` and nothing was committed — the review read the tree before finalization; the frontmatter now carries both, and the commit is the pipeline's hand-off.
  - `[low]` `[reject]` Intent: the "integration test on the Aspire AppHost" is an Orleans cluster on the AppHost's PostgreSQL with the channels added directly — the REST registration test runs against the AppHost itself; pointing the AppHost's Server at a stub provider needs a listener outside the process.
  - `[medium]` `[defer]` Intent: credential gating at start is covered by unit tests only — same gap as the `Program.cs` row; shares its `deferred` entry.
  - `[false]` `[reject]` Intent: no Server-side test touches the permission-denied state — permission is client state; it is covered in the core, on Android and in the Swift presentation tests.
  - `[false]` `[reject]` Intent: the tap is tested at the core, the intent and the presentation model, not at the OS — the OS delivery is in the operator checklist.
  - `[medium]` `[defer]` Intent: no test runs the iOS OS glue — same gap as the iOS `App/` row; shares its `deferred` entry.
  - `[low]` `[reject]` Intent: Android receipt is tested without Firebase and `FirebasePush.start` only for the no-configuration path — a configured start needs a real Firebase project; operator checklist.
  - `[false]` `[reject]` Intent: grouping and badge are asserted on the request and on the built notification, not on a lock screen — operator checklist.
  - `[low]` `[reject]` Intent: a summary lists at most 12 Alerts and then "and N more" where the intent says one line per entry — a push payload is 4 KiB; affects a User with more than twelve Alerts held on one Site. Named under residual risks.
  - `[false]` `[reject]` Intent: the payload names no Device — Health Alert payloads are Epic 7, which the intent excludes.
  - `[false]` `[reject]` Intent: the Notifier's failure rule and `[AlwaysInterleave]` change behaviour outside the story's surface — neither changes when a notification is due: the first decides whether a failed send is repeated, the second avoids a deadlock the channel would otherwise cause.
  - `[false]` `[reject]` Intent: on Android the app builds the visible notification — FCM display notifications cannot set a group; the text still comes only from the Server (design notes).
  - `[low]` `[reject]` Intent: the snapshot criterion names Android only — the Swift package has no snapshot library; iOS has render tests for the same states.

## Design Notes

- **Why invalid tokens return through the seam.** The Notifier is awaited inside the User grain's turn and the grain is not reentrant, so a channel that called `IUserGrain` would wait on itself until the call timeout. The grain passes the registrations in and journals what comes back.
- **Android shows its own notification.** FCM's display notifications cannot set a notification group, so the Server sends title, body and routing as data and the app's messaging service builds the notification (group = Site, channel without badge). The text still comes only from the Server.
- **No vendor SDK on the Server.** APNs (HTTP/2, ES256 provider token) and FCM HTTP v1 (service-account OAuth) are a few requests each; the BCL covers the signing, and the locked restore stays untouched.
- **Firebase without `google-services.json`.** Adopters build with their own credentials (architecture l.352), so the Android app initialises Firebase from build properties and skips it when they are absent.
- **One device, two Users.** Registrations are per User, so sign-out removes the registration while the session is still valid; if that request fails the previous User's Alerts can still reach the phone until the token is replaced or reported invalid. Named as a residual risk.
- **Operator work.** Creating the APNs key and the Firebase project, creating `coldframe-push`, building the apps with those credentials and running the real-push checklist cannot be done by an agent: finish as `awaiting-operator` with `operator_actions`.

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror && dotnet format --verify-no-changes --no-restore` -- expected: success, no changes
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- expected: pass (known host flakes: `KeycloakReconciliationTests.ARenameInKeycloakRenamesTheSite`, three `NodeMoveTests` reminder timeouts)
- `ANDROID_HOME=$HOME/Android/Sdk ./gradlew ktlintFormat check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64` -- expected: BUILD SUCCESSFUL (one online run for the new dependency)
- `podman run --rm --security-opt label=disable -v "$PWD":/work -w /work swift:6.3.3 sh -c 'swift build && swift test && swift format lint --strict -r .'` -- expected: pass
- `pnpm -r typecheck && pnpm -r lint && pnpm -r test && pnpm --filter @coldframe/openapi run check` -- expected: pass
- `packages/proto/check-compat.sh` and `deploy/charts/test.sh` -- expected: pass

**Manual checks (if no CLI):**
- SwiftUI views and `apps/swift/ios/App` compile only in the macOS CI jobs `swift` and `ios`: read their result on the pull request.
- A real push on an iPhone and an Android phone: `docs/bench/push-checklist.md`, operator only.

## Auto Run Result

Status: awaiting-operator

**Summary:** A due notification now reaches phones. The mobile apps ask for notification permission on the first Site overview after one line of why, and register the device's push token through the shared Kotlin core; the User grain owns the registrations as events. Two channels behind the Notifier seam, APNs and FCM, look up the Site, Lot, value and Threshold, write the text on the Server from English templates and send it grouped per Site without a badge. A token a provider reports as invalid is removed from the User grain. A tap opens Lot detail or the Site overview, and a denied permission shows a persistent notice in My notifications and on the overview. Everything runs without credentials: a provider without them gets no channel. A real push needs the operator's own Apple and Firebase credentials and two phones, which is why the story ends as awaiting-operator.

**Files changed:**
- `packages/openapi/coldframe.openapi.json`, `packages/ts/api-client/src/schema.ts`, `packages/asyncapi/{coldframe.asyncapi.json,fixtures/*}` -- `PUT`/`DELETE /me/push-registrations/{installationId}`; the push payload contract with one fixture per kind.
- `packages/cs/contracts/{Notifications/Notifications.cs,Sites/SiteGrains.cs,Sites/UserEvents.cs,Sensors/SensorGrains.cs}` -- registrations, two `user.push-device-*` events, registrations, zone and window in the `Notification`, `[AlwaysInterleave]` on `GetThresholds`.
- `apps/cs/server/Identity/{UserGrain,UserState}.cs`, `apps/cs/server/Notifications/{INotifier,Notifier,INotificationChannel}.cs` -- the grain owns registrations and journals removals; the seam returns delivered counts and invalid installations.
- `apps/cs/server/Notifications/Push/*`, `apps/cs/server/Sensors/SensorDisplay.cs`, `apps/cs/server/Edge/{EdgeApi,EdgeValidation}.cs`, `Program.cs` -- options, text templates, lookups, APNs and FCM channels, the endpoints.
- `aspire/Coldframe.AppHost/AppHost.cs`, `deploy/charts/server/**`, `deploy/charts/{test.sh,README.md}`, `deploy/SECRETS.md` -- optional credentials from `coldframe-push`, enabled per provider by chart values.
- `packages/kt/core/src/**/{api,push,sites,signin}/*` -- registration API, the push engine (permission, ask once, token hand-over, sign-out removal, tap route), iOS and Android facades.
- `apps/kt/android/**`, `gradle/libs.versions.toml` -- permission flow, both notices, Firebase messaging from build properties, notification group and channel, tap intents.
- `apps/swift/ios/**` -- push presentation and views, notices, hoisted Garden route, `AppDelegate` and `CorePushService` in `App/`, opt-in entitlement, strings.
- `tests/cs/**`, `tests/kt/**`, `tests/swift/**` -- text, channel, hosting, state, grain and REST tests; core engine and contract tests; Android tests with six snapshots; Swift presentation and render tests.
- `docs/bench/push-checklist.md`, `docs/quickstart.md`, `docs/operations/install.md`, READMEs -- the real-push checklist, credential setup, how the channels work.

**Review findings:** 54 findings from four layers. Patched 12 entries (9 medium, 3 low) covering 20 rows. Deferred 4 entries (6 rows), all test gaps: the interleaving of `GetThresholds`, the host wiring in `Program.cs`, the tapped intent in `MainActivity`, and the iOS `App/` adapter. Rejected 28 rows: 2 medium (APNs topic and token reasons, listed as invalid by the intent contract), 18 low and 8 false; each reason is in the Review Triage Log.

**Follow-up review recommendation:** true. Patched by verdict: high 0, medium 9, low 3. The unverified risk: the channel's failure rule changed after the review (a channel that reached one device no longer repeats for a sibling that failed, a refusal is logged and not repeated, invalid installations travel on the thrown exception through `IInvalidInstallationsSource`), and no review layer has read that rule or the tests added with it. The two iOS patches are uncompiled.

**Verification:**
- `dotnet restore --locked-mode`, `dotnet build --no-restore -warnaserror`, `dotnet format --verify-no-changes --no-restore` -- pass, after the review patches.
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- 1311 passed, 0 failed, 0 skipped (1296 before the review patches).
- `ANDROID_HOME=$HOME/Android/Sdk ./gradlew ktlintFormat check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64` -- BUILD SUCCESSFUL, after the review patches.
- Swift build, test and format lint in the `swift:6.3.3` container -- 326 tests pass, after the review patches.
- `pnpm -r typecheck`, `lint`, `test` and the OpenAPI fixture check -- pass (run before the review patches, which changed no TypeScript or contract file).
- `packages/proto/check-compat.sh` -- no breaking OpenAPI change.
- `deploy/charts/test.sh` -- 118 passed, 50 failed. Every failure is a `kubeconform` or Fleet check; neither tool is installed on this host. The helm lint, unit and Secret-contract checks pass, including the push cases. The CI job `charts` runs the rest.
- Every row of the I/O matrix has a covering test that ran and passed.
- Not verified here: the SwiftUI views and `apps/swift/ios/App` (macOS CI jobs `swift` and `ios`), and a real push on a phone (operator).

**Residual risks:**
- iOS app-target code is uncompiled: `AppDelegate.swift`, `CorePushService.swift`, `PushViews.swift`, the edits to four view files and the render tests. The Swift 6 isolation around the notification-centre delegate is the likeliest place for an error.
- A wrong `push.apns.topic`, or an environment that does not match the signing profile, makes APNs answer `DeviceTokenNotForTopic` or `BadDeviceToken`, which removes iPhone registrations until each app starts again.
- Shared phone: when the sign-out removal does not reach the Server, the previous User's Alerts still reach the phone until the token is replaced or reported invalid.
- A refused push (bad credentials, refused payload) is logged as an error and not sent again; a channel that reached one device does not repeat for a sibling device that failed.
- With a provider down, one User grain turn can take several seconds per notification; calls that queue behind it may time out and are repeated by their callers.
- A summary lists at most 12 Alerts, then "and N more"; long non-ASCII Lot names can still pass the 4 KiB payload limit.
- On iOS the installation ID and the asked flag are in `NSUserDefaults` and travel with a device transfer.
- Android registers its token also while notifications are off.
- Additions the story is silent on: at most 20 registrations per User, a token moves to the installation that registers it last, pushes expire after 24 h, the why-line has a Continue button.
- Test-first (NFR16) was not followed literally on Android: tests and implementation were written together.
