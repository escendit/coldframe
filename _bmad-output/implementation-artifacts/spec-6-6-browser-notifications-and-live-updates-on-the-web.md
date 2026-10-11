---
title: 'Story 6.6: Browser notifications and live updates on the web'
type: 'feature'
created: '2026-10-10'
baseline_revision: f531775cbb40ef97cb60d73c5904d76b889cbd94
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
  - '{project-root}/apps/cs/README.md'
  - '{project-root}/apps/ts/web/README.md'
  - '{project-root}/packages/asyncapi/README.md'
warnings: ['oversized']
deferred:
  - summary: >-
      No test shows that a hint arriving while a form is being edited leaves the typed input alone.
    evidence: |-
      A sites hint refetches every app page and a lots hint the Thresholds and Calibrate sub-pages, through invalidateAll(). Thresholds keeps its edits in $state(untrack(...)) and Site settings keys its rename input on the Site name, which suggests input survives, but nothing verifies it. A Playwright case that types into Thresholds and Site settings, sends a hint through the fake hub's /control endpoint and reads the inputs again would settle it.
    location: >-
      apps/ts/web/src/lib/live/rule.ts, apps/ts/web/src/routes/(app)/+layout.svelte
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** The web app learns of nothing while it is open: an Alert that is due reaches phones only, and every page shows what it loaded until the User reloads or refocuses. The Server has no SignalR hub, `packages/asyncapi` describes no SignalR message, and no test runs the morning-nudge journey (UJ-2) from a Reading to a browser notification.

**Approach:** Add one SignalR hub on the Server that carries exactly `notification.delivered` (a new channel behind the Notifier seam, same text as push) and `readmodel.changed { resource, id, version }` (a hint after a read model changed). The browser reaches it only through a SvelteKit BFF route that attaches the access token. The web app shows deliveries as browser notifications when the User turned on "Browser notifications while Coldframe is open", and refetches the open page on a hint. The message shapes live in `packages/asyncapi` with generated TypeScript types, and a Playwright test runs UJ-2 against the Aspire AppHost with a controllable clock.

## Boundaries & Constraints

**Always:**
- The browser talks only to the web origin (AD-14). The BFF route forwards to the Server's hub with `Authorization: Bearer <access token of the session>` and forwards nothing else of the session (no cookies, no token in a URL). A request without a session gets 401 and reaches the Server never.
- The hub requires an authenticated caller and has no client-to-server methods. A connection receives `notification.delivered` only for its own User (`sub`), and `readmodel.changed` only for Sites the User has a Membership on *now*: a Membership added or removed while connected takes effect without a reconnect.
- `notification.delivered` is sent only by an `INotificationChannel` called from the Notifier; the channel does no timing, window or mute logic. Title and body are the Server's push text (`PushContentBuilder`), so web and phone show the same words. With no open connection of the User nothing is sent, nothing is queued, and the delivery still counts as sent.
- `readmodel.changed` carries only `resource`, `id` and `version` (plus nothing derived from domain data). It is sent after the change is committed, never before. `version` is a positive integer that never decreases for the same `resource` and `id`.
- The toggle is per browser (it follows the browser's own permission) and is off by default. The browser's permission prompt appears only as the result of turning the toggle on. The UI shows a notification only for a received `notification.delivered` and only while the toggle is on and permission is granted; it never composes one itself.
- One Alert appears once per device: notifications carry the delivery's collapse ID as `tag`, so two tabs or a repeated delivery do not stack.
- Message shapes are authored in `packages/asyncapi/coldframe.asyncapi.json` first; the Server's serialised messages equal the fixtures there, and the TypeScript types are generated from that file and checked for staleness in CI.
- Tests first: each surface has a named test written failing before its implementation; tests for UX-DR89 and UX-DR123 carry those IDs in their names. All strings come from `en.json`.
- Test-only clock control is honoured by the Server only when the host environment is Development *and* `Testing:ManualClock` is `true`; otherwise no clock endpoint is mapped and `TimeProvider.System` is used. Access-token validation keeps the real clock in every mode.

**Never:**
- No background web push, no service worker, no WebSocket upgrade in the BFF (SvelteKit 2.70 with `adapter-node` cannot upgrade): the hub is offered over Server-Sent Events and long polling only.
- No domain data in a hint, no notification text composed in the browser, no recovery or "connected" notices, no connection indicator, no polling timer.
- No Server-side "browser notifications" preference, no new event on the User grain, no change to window, mute, Reminder or summary logic.
- No SignalR backplane (one Server replica, as the chart pins it). No SignalR for the mobile apps.
- No test-only code path reachable in a Production host.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Connect through the BFF | Signed-in tab opens any app page | `POST /live/negotiate` then a stream on the web origin; the Server sees the bearer token and puts the connection in the User's and its Sites' groups | Hub unreachable: the page works as before, no notice, the client retries with back-off |
| No session | `/live/*` without a session cookie | 401 from the BFF; no request to the Server | — |
| Direct, no token | Request to the Server's hub without a bearer token | 401 Problem Details | — |
| Alert due in window, toggle on | Notifier sends an Alert for the User; a tab is open, permission granted | One browser notification, title "Tomatoes needs water", body "~20 % in the soil, your low is 30 %."; click focuses the tab and opens Lot detail on that Site | — |
| Summary | Summary delivery | Notification with the summary title and lines; click opens the Site overview of that Site | — |
| Toggle off or permission not granted | `notification.delivered` arrives | Nothing is shown | — |
| No tab open | Notifier sends | Channel reports 0 connections; nothing is stored for later; the User grain records the delivery as sent | — |
| Muted Site / outside window | Alert opens | The User grain does not call the Notifier; no message | — |
| Turn toggle on, allow | Permission `default`, User allows | Toggle stays on, stored for this browser | — |
| Turn toggle on, blocked | Permission `denied`, or the User denies, or the browser has no Notification API | Toggle returns to off; inline notice "Your browser blocks notifications for Coldframe. Allow them in this site's browser settings, then turn this on again." | — |
| Prompt dismissed | Permission stays `default` | Toggle returns to off, no notice | — |
| Permission revoked later | Stored on, permission no longer `granted` | Toggle renders off; nothing is shown | — |
| Two tabs | Same delivery in both | One visible notification (same `tag`) | — |
| Hint for the open page | `readmodel.changed` for a resource the page shows (table in Design Notes) | The page refetches over REST without a reload and without animation; several hints during one refetch cause one more refetch, not one each | Refetch fails: the page keeps its data and its existing stale handling |
| Hint for another resource or another Lot | e.g. `devices` while on Alerts | No refetch | — |
| Reconnect | Connection lost and restored | One refetch of the open page (hints may have been missed) | — |
| Membership removed while connected | User loses the Site | No further hint of that Site reaches the connection | — |
| Token expires | Access token of a connection expires | The Server closes the connection; the client reconnects through the BFF with the refreshed token; signed out means 401 and no further retries until the next navigation | — |
| Manual clock off | Production host, or flag absent | `/testing/*` answers 404; time is the system clock | — |

</intent-contract>

## Code Map

Server (`apps/cs/server`, `packages/cs/contracts`):
- `Program.cs:11-34` -- composition; map the hub in its own `Map…` call beside `app.MapEdgeApi()`, not inside it: `tests/cs/server.tests/Edge/EdgeEndpointCatalog.cs` throws on any endpoint of `MapEdgeApi()` without HTTP-method metadata, and `EdgeEndpointDiscoveryTests` / `AuthorizationMatrixTests.cs:196-211` compare that set with OpenAPI. No exemption mechanism exists.
- `Coldframe.Server.csproj` is `Microsoft.NET.Sdk.Web`: SignalR server needs no package. A hub client for tests (`Microsoft.AspNetCore.SignalR.Client` 10.0.12) and `Microsoft.Playwright` 1.63.0 are new entries in `Directory.Packages.props`; restore is locked, so regenerate the lock files of the projects that use them.
- `Edge/EdgeAuthentication.cs:73-114,130` -- JwtBearer (`MapInboundClaims = false`, user ID = `sub`), fallback policy = authenticated; the token arrives as a header, so no `access_token` query handling.
- `Notifications/INotificationChannel.cs` (`Task<ChannelDelivery> SendAsync(Notification, DateTimeOffset sentAt, CancellationToken)`, `ChannelDelivery.To(n)` / `.Nothing`; runs in the User grain's turn, must bound its own time, tolerate a repeat, never filter), `Notifier.cs:229` (throws only when a channel failed and nothing was delivered), `NotificationsHostingExtensions.cs` (`AddSingleton<INotificationChannel, T>()`), `Identity/UserGrain.cs:594-636` (journals `NotificationSent` after any non-throwing return).
- `Notifications/Push/PushContent.cs:57,78` -- `PushContentBuilder.BuildAsync(notification, sentAt)` returns `PushContent(Kind, SiteId, SiteName, LotId, AlertId, CollapseId, Title, Body)` or `null`, cached per `Notification` instance; registered by `PushHostingExtensions.AddPushContent` (:47). Reuse it; do not write a second text path.
- `Journal/ProjectionRunner.cs:150` -- the one commit point of every projector (`identity`, `lots`, `devices`, `alerts` in `Identity/IdentityProjector.cs`, `Lots/LotsProjector.cs`, `Devices/DevicesProjector.cs`, `Alerts/AlertsProjector.cs`). `IProjector.ApplyAsync` returns nothing today: projectors must report which `(siteId, resource, id)` they touched. `JournalEvent.Position` is the global, monotonic number to use as `version`.
- Readings never pass a projector: `DeviceIngestionStore` writes them and `LotDetailReadModel.LatestReadingsAsync` reads them; the `readings` hint comes from the ingest path after its commit.
- `Identity/IdentityReadModel.cs` (`ListSitesAsync(userId)`) -- the Sites of a connecting User.
- Time: `Hosting/JournalHostingExtensions.cs:26`, `NotificationsHostingExtensions.cs:22` register `TimeProvider.System` with `TryAddSingleton`; grains read `Clock`. `BannedSymbols.txt` and `WallClockBanTests` ban wall-clock reads. `UserGrain.WakeReminderName`; a wake is `IRemindable.ReceiveReminder(...)` (`tests/cs/server.integration/Identity/IdentityCluster.cs:156`). `Devices/NodeFrameReader.cs:64` `MaxClockLeadMs = 300_000`: simulated frames must be stamped from the Server's clock.
- Logging: source-generated `[LoggerMessage]`, `EventId` from 1 per class with an `EventName`.
- `aspire/Coldframe.AppHost/AppHost.cs:111-138` -- the `server` resource; no web resource and no clock switch today.

Contracts:
- `packages/asyncapi/{coldframe.asyncapi.json,fixtures/push.*.json,README.md}` -- AsyncAPI 3.0.0; today only `ApnsPush` / `FcmPush` with schemas `NotificationKind`, `CollapseId`, `PushRoute`. `README.md:4-5` announces this story. Not a pnpm workspace member. Fixtures are linked into `tests/cs/server.tests/Coldframe.Server.Tests.csproj:24` and `tests/cs/server.integration/…csproj:29`; `tests/kt/core/.../PushFixturesTest.kt` reads the push fixtures (leave them unchanged).
- `packages/ts/api-client/scripts/generate.ts` (openapi-typescript 7.13.0 → `src/schema.ts`), staleness test `tests/ts/api-client/index.test.ts:29-31` -- the pattern for generated types; no AsyncAPI tool is installed.
- `.github/workflows/ci.yml`: `changes` filter (:78), `typescript` job (:339-402), `dotnet` job, `contracts` job (:408-445, runs only `packages/proto/check-compat.sh`, which ignores AsyncAPI).

Web (`apps/ts/web`, `tests/ts/web`, `tests/ts/web.e2e`):
- `src/lib/server/sites.ts:37-40,69` (`accessTokenOf`, 10 s timeout that a stream must not use), `src/lib/server/config.ts:54-56` (`COLDFRAME_SERVER_URL`), `src/hooks.server.ts:50`, `src/lib/server/auth-handle.ts`, `shell.ts:12,42,182-190` (`cf_site` cookie; `?site=<id>` selects a Site of the caller and redirects). The only `+server.ts` is `routes/signin/start`; no streaming route exists.
- `src/routes/(app)/settings/notifications/+page.svelte` (:201-215 mute toggle pattern), `components/{Toggle,InlineNotice}.svelte`, `lib/notifications.ts`, `lib/announcer.svelte.ts`, `lib/i18n/en.json:398-425`.
- Pages and their refresh today (all `invalidateAll()`, no `depends` keys): `garden/+page.svelte:72-101`, `alerts/+page.svelte:26-47` (focus); `garden/[lotId]`, `devices` none. `lib/alerts.ts:99-115` holds the Alert-to-route rule; `components/AppShell.svelte` is the shell of every app page.
- Guards: `tests/ts/web/overview.test.ts:415` (Garden page source must not match `setInterval|setTimeout|EventSource|WebSocket|signalr`), `styles.test.ts:26,37`, `no-hard-coded-copy.test.ts`, `copy.test.ts`, `coverage.test.ts:8-11` (`storyIds`; add 89 and 123), `vitest.config.ts` stubs for `$app/*` (`tests/ts/web/stubs`). Tests that assert the toggle's absence: `tests/ts/web/notifications.test.ts:493`, `tests/ts/web.e2e/specs/notifications.spec.ts:75`.
- `tests/ts/web.e2e`: `playwright.config.ts`, `fixtures/{stack.ts,fake-idp.ts}` (the fake IdP is also the fake Server, with `/control/*` seeding), `specs/helpers.ts` (`signIn`, `useTheme`, `axeClean`), committed snapshots `*-{light,dark}-chromium-linux.png`. `@playwright/test` 1.63.0 with Chromium 1243 is installed locally.
- `@microsoft/signalr` (10.0.11) is not in `pnpm-lock.yaml` or the local store: adding it needs the network; CI installs with `--frozen-lockfile`.
- README lines to update: `apps/ts/web/README.md:64,125,191,277`, `apps/cs/README.md:502,764`, `packages/asyncapi/README.md:4`.

End-to-end pieces that exist: `tests/cs/server.integration/AppHostFixture.cs` (starts the AppHost with `DistributedApplicationTestingBuilder`), `Edge/EdgeApiFixture.cs` (admin-created Keycloak Users with a password, direct-grant tokens, enrolment helpers), `tests/cs/device-simulator/SimulatedDevice.cs` (`SealEnrolment`, `Wake` :328, `IngestRequest` :463). `aspire/keycloak/realms/coldframe-realm.json`: the `coldframe-web` client allows only `http://localhost:5173/.oidc/signin/callback`. `docs/quickstart.md:119-139` lists the web app's environment for the local stack. Open: DW-10 (no test signs in through the real Keycloak).

## Tasks & Acceptance

**Execution:**
- `packages/asyncapi/coldframe.asyncapi.json`, `packages/asyncapi/fixtures/live.*.json`, `packages/asyncapi/README.md` -- add the hub channel with the messages `notification.delivered` (`kind`, `siteId`, `siteName`, `lotId?`, `alertId?`, `collapseId`, `title`, `body`) and `readmodel.changed` (`resource` enum `sites | lots | readings | devices | alerts`, `id`, `version`); one fixture per notification kind and one hint -- the contract comes first (AD-10).
- `packages/ts/api-client/scripts/generate.ts`, `packages/ts/api-client/src/*`, `tests/ts/api-client/*` -- generate and export the TypeScript message types from the AsyncAPI schemas with the tool already installed; a test fails when the committed file is stale and when a fixture does not fit its type.
- `tests/cs/server.tests/Live/*`, `tests/cs/server.integration/Live/*` -- failing tests first: serialised messages equal the fixtures; the channel sends to the User's connections only and reports their count; hints after a projector commit and after an ingest, with site scoping and membership changes; hub 401 without a token; no client-callable method; the manual clock is ignored outside Development.
- `apps/cs/server/Live/*` (hub, connection registry, `INotificationChannel`, hint publisher, hosting extension), `apps/cs/server/Program.cs` -- the hub over Server-Sent Events and long polling, closing a connection when its token expires.
- `apps/cs/server/Journal/ProjectionRunner.cs`, the four projectors, the ingest path -- report touched resources and publish hints after commit; a failing publish never fails or repeats the commit.
- `apps/cs/server/Testing/*`, `apps/cs/server/Program.cs`, `aspire/Coldframe.AppHost/AppHost.cs` -- a manual clock (real time plus an offset that only moves forward), `GET`/`POST /testing/clock` and `POST /testing/users/{userId}/wake`, behind Development plus `Testing:ManualClock`; the AppHost hands the flag on only when it is given it.
- `apps/ts/web/src/routes/(app)/live/[...path]/+server.ts`, `apps/ts/web/src/lib/server/live.ts` -- the streaming BFF proxy: only the hub's own paths and methods, the session's token, no time limit on the stream, upstream closed when the browser leaves.
- `apps/ts/web/package.json`, `pnpm-lock.yaml` -- add `@microsoft/signalr`.
- `apps/ts/web/src/lib/live/*`, `apps/ts/web/src/lib/components/AppShell.svelte` (or the `(app)` layout) -- one connection per tab, the page-to-resource rule, coalesced refetch, refetch on reconnect, quiet failure.
- `apps/ts/web/src/lib/browser-notifications.ts`, `src/routes/(app)/settings/notifications/+page.svelte`, `src/lib/i18n/en.json` -- the toggle, the blocked notice, showing a delivery with `tag` and the click route.
- `tests/ts/web/*`, `tests/ts/web.e2e/fixtures/fake-idp.ts`, `tests/ts/web.e2e/specs/*` -- failing tests first: `UX-DR89` and `UX-DR123` named tests, every matrix row of the browser side, the proxy's rules, a fake hub in the fake Server with a `/control` endpoint, light and dark snapshots of My notifications with the toggle and with the blocked notice; replace the two absence assertions; add 89 and 123 to `coverage.test.ts`.
- `tests/cs/web.e2e/*` (new test project), `Directory.Packages.props`, lock files, the solution file -- the UJ-2 Playwright test against its own AppHost instance with the manual clock: a Keycloak User signs in to the built web app on port 5173, turns the toggle on, a simulated Node's Readings cross the low Threshold at 02:40 local, the Alert is held, at 07:00 one browser notification with the expected title and body arrives and the open overview shows *needs water* without a reload, recovery Readings close the Alert, and a day later no Reminder arrives.
- `.github/workflows/ci.yml` -- AsyncAPI changes run the TypeScript and .NET jobs; a job with .NET, Node and Chromium builds the web app and runs `tests/cs/web.e2e`; the `.NET` job does not run that project.
- `apps/ts/web/README.md`, `apps/cs/README.md`, `docs/quickstart.md`, `deploy/charts/README.md` -- how live updates work, the BFF route, the clock switch and its guard, that the ingress must not buffer the event stream; update `deferred-work.md` DW-10 only through the result notes, not by editing the ledger.

**Acceptance Criteria:**
- Given the web app open and signed in, when it connects, then every hub request of the browser goes to the web origin and the Server receives it with the session's access token; a direct request without a token gets 401.
- Given the toggle turned on and permission allowed, when the Notifier delivers for that User, then a browser notification with the Server's title and body appears, and none appears for a muted Site, outside the window, or with no tab open.
- Given a read model changed on the Server, when the hint arrives, then the open page that shows that resource refetches over REST, and the hint holds only `resource`, `id` and `version`.
- Given CI, when a message shape in the Server or the committed TypeScript types differs from `packages/asyncapi`, then a test fails.
- Given the Aspire AppHost with the manual clock, when the UJ-2 test runs, then it passes.
- Given UX-DR89 and UX-DR123, when the test suite is listed, then each has a named test, and `coverage.test.ts` lists both.
- Given a Production host, when `/testing/clock` is requested with or without the flag, then it answers 404.

## Spec Change Log

## Review Triage Log

### 2026-10-10 — Review pass
- verdicts: 38 findings — high 0, medium 9, low 24, false 4, maybe-false 1
- findings:
  - `[medium]` `[patch]` Blind: a connection the Server closes right after the handshake is retried with no delay, with a refetch every round — `live.ts` reset `failures` on every successful start and the first delay is 0 ms. Fixed: the back-off is reset only after a connection held 5 s; new test in `tests/ts/web/live.test.ts`.
  - `[medium]` `[patch]` Blind: no test shows the reconnect after token expiry carries the refreshed token — the fake hub accepted any token ever issued. Fixed: `short-refresh` mode in the fake, which records `jti` and refuses an expired token; new case in `live.spec.ts` passes, so the BFF needed no change.
  - `[low]` `[reject]` Blind: a 401 ends retries on an idle tab while the toggle still reads on — the intent contract prescribes it ("signed out means 401 and no further retries until the next navigation") and forbids connection notices; each reconnect at token expiry refreshes the session, so an idle tab stays signed in.
  - `[low]` `[reject]` Blind: hints are not Site-scoped in the browser, so a User with several Sites refetches for another Site's activity — real but only a spare refetch; the contract fixes a hint to `resource`, `id`, `version`.
  - `[low]` `[patch]` Blind: hint fan-out per ingest is unthrottled and the README sentence "A row that did not change is not reported" is untrue — the fan-out is at most three hints per Node frame, coalesced while a refetch runs (not worth a throttle, and timers are excluded); the README sentence was corrected.
  - `[maybe-false]` `[defer]` Blind: `invalidateAll()` on a hint may reset form input in progress — Thresholds keeps its edits in `$state(untrack(...))` and Site settings keys its rename on the Site name, which suggests no; no test types into a form and then sends a hint. Deferred, medium (unverified).
  - `[low]` `[reject]` Blind: a revoked Membership rides on a queue that drops the oldest of 4096 — needs a backlog of 4096 hints with connections open; the ex-member would get hints (IDs only) until the connection closes at token expiry. Unlikely, and the fix adds a second queue.
  - `[low]` `[patch]` Blind: the Membership read in the hint loop has no time bound — bounded only by the database command timeout. Fixed: same 5 s budget as the send; new test in `LiveHintsTests.cs`.
  - `[low]` `[reject]` Blind: `LiveHints._versions` grows without bound — one small entry per resource and ID ever hinted while the process runs; years of a garden's Alerts stay negligible, and evicting breaks the never-decreases rule.
  - `[medium]` `[patch]` Blind: the channel reports open connections as delivered, which cancels the retry of a failed push (`Notifier.cs:119`). Fixed: the channel returns `ChannelDelivery.Nothing` also after a send; a failed send still throws; Notifier-level test added. This departs from the task line "reports their count"; the matrix row (0 with no tab) still holds.
  - `[low]` `[patch]` Blind: `WatchExpiry` throws for a token that expires more than about 49.7 days ahead. Fixed: due time clamped, out-of-range `exp` gets no watch; two tests.
  - `[low]` `[reject]` Blind: plain `dotnet test` fails without a built web app, and a stale build is accepted — it fails with the command to run; the verification builds first and CI separates the jobs. A freshness check is more than a direct correction.
  - `[low]` `[reject]` Blind: CI gaps (grep-based test discovery, no artefacts on failure, `Web end-to-end` not a required check) — every current project matches and an empty list fails; only `Images gate` is a required check for any job, which predates this story.
  - `[low]` `[reject]` Blind: the UJ-2 test proves "no Reminder" by waiting about 10 s, and the per-Alert text is not on the real stack — absence can only be waited for; the test also reads the journal; the per-Alert text is covered by fixture and fake-hub tests.
  - `[low]` `[reject]` Blind: turning the toggle off while the permission prompt is pending, then allowing, stores on; tabs are not synced — real but needs a click during the prompt; the fix is an added guard.
  - `[false]` `[reject]` Blind: the BFF buffers the whole POST body before the size check — `adapter-node` refuses a body above `BODY_SIZE_LIMIT` (512 kB by default) before the route runs, so the read is bounded.
  - `[medium]` `[patch]` Edge: zero-delay reconnect loop — same defect as the first row; fixed there.
  - `[low]` `[patch]` Edge: far `exp` throws in `WatchExpiry` — same defect as above; fixed there.
  - `[medium]` `[patch]` Edge: the budget timer can cancel a disposed `CancellationTokenSource`, an unhandled exception on a timer thread. Fixed: `CancellationTokenSource(SendBudget, clock)` linked to the caller's token.
  - `[medium]` `[patch]` Edge: an open tab cancels a failed push's retry — same defect as above; fixed there.
  - `[low]` `[reject]` Edge: a dropped `sites` change keeps a revoked member's Site — same as the queue row above.
  - `[low]` `[patch]` Edge: a hanging Membership read stalls the loop — same as above; fixed there.
  - `[low]` `[reject]` Edge: `_versions` grows — same as above.
  - `[low]` `[reject]` Edge: after a failed read following a Site-level change a valid member loses that Site's hints until reconnect — the deliberate safe side, logged; pages still refetch on focus and on reconnect.
  - `[false]` `[reject]` Edge: `Touch` throws on an empty Site or row ID and wedges the projector — every call passes an event's Site ID or a `RETURNING site_id` matched with `is string`; no path hands it an empty value.
  - `[low]` `[reject]` Edge: a Device enrolled again on another Site sends no hint to the previous Site — rare; the fix rewrites the statement.
  - `[low]` `[reject]` Edge: an upstream that accepts the GET but never answers leaves the client connecting — a Server in that state serves nothing else either; a header timer guards a state not shown to occur.
  - `[low]` `[reject]` Edge: toggle race — same as above.
  - `[low]` `[reject]` Edge: `POST /testing/clock` near the maximum date overflows — test-only, authenticated, Development only.
  - `[medium]` `[patch]` Gap: the stream's "no time limit" was pinned by a regex over the source. Fixed: `LiveDependencies.timeoutMs`; a behavioural test (a hanging POST is aborted, the GET is not).
  - `[medium]` `[patch]` Gap: refreshed token after expiry not observed — same as the second row; fixed there.
  - `[medium]` `[patch]` Gap (other): reconnect burst at token expiry — same defect as the first row; fixed there.
  - `[low]` `[reject]` Gap (other): the .NET job's grep discovery can skip a future project — same as the CI row.
  - `[low]` `[reject]` Intent: the Notification API is replaced by a recorder in every browser test — a headless browser shows no notification that a test could observe; the recorder sees the title, body and tag the page hands the API.
  - `[false]` `[reject]` Intent: mute is not driven through the hub — mute is decided in the User grain before the Notifier is called (Story 6.4 tests); the channel test shows it adds no filter.
  - `[low]` `[reject]` Intent: the per-Alert text is not on the real path, the journey shows the summary — an Alert held overnight is delivered as a summary by Story 6.4's rules; the per-Alert text is covered at the channel and in the browser suite.
  - `[low]` `[reject]` Intent: no AsyncAPI tool validates the document or the wire — fixtures equal the Server's output and fit the schema, and the types are generated from the schema; no tool is installed.
  - `[false]` `[reject]` Intent: the web app is not an AppHost resource and the User grain is woken through the test endpoint — the Server, Keycloak and PostgreSQL are the AppHost's; Orleans reminders run on real time, so a moved clock needs the wake.

## Design Notes

- **Why Server-Sent Events.** SvelteKit 2.70 on `adapter-node` has no upgrade hook and production runs plain `node build`. SignalR's SSE and long-polling transports are ordinary HTTP, so a streaming `+server.ts` can proxy them and AD-14 holds without a custom server.
- **Why the toggle is per browser.** Permission belongs to the browser. A Server-side preference would show "on" in a second browser that blocks notifications, which contradicts UX-DR89. So the Server sends to every open connection of the User and each browser decides whether to show.
- **Hint resources and who refetches** (the page calls its existing `invalidateAll()`):

| `resource` | `id` | Source | Pages |
|---|---|---|---|
| `sites` | Site | identity projector (sent to the affected User and the Site) | every app page |
| `lots` | Lot | lots projector | overview; Lot detail and its sub-pages of that Lot |
| `readings` | Lot | ingest commit (`version` = newest `measured_at` in Unix ms) | overview; Lot detail of that Lot |
| `alerts` | Alert | alerts projector | Alerts; overview |
| `devices` | Device | devices projector | Devices |

- **Why a clock switch in the Server.** The planning says Aspire test hosts use a fake clock, and in an Aspire host the Server is its own process. The switch needs two conditions (Development and the flag); the chart never sets either. It is still test-only code in the production binary: named as a residual risk.
- **Why the UJ-2 test is C#.** Enrolment and sealed Readings exist only as C# helpers, and `Microsoft.Playwright` 1.63.0 uses the Chromium revision already installed. The browser-side details stay covered by the TypeScript suite against the fake hub.
- **Message example:**

```json
{ "resource": "lots", "id": "0198c0de-…", "version": 48213 }
```

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror && dotnet format --verify-no-changes --no-restore` -- expected: no error, no change.
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- expected: all pass, none skipped.
- `pnpm --filter @coldframe/web build && ASPIRE_CONTAINER_RUNTIME=podman dotnet test tests/cs/web.e2e` -- expected: the UJ-2 test passes.
- `pnpm install --frozen-lockfile && pnpm -r typecheck && pnpm -r lint && pnpm -r test` -- expected: all pass, including the Playwright suite and its snapshots.
- `pnpm --filter @coldframe/api-client generate && git diff --exit-code packages/ts/api-client` -- expected: no difference.
- `packages/proto/check-compat.sh` -- expected: no breaking OpenAPI or Protobuf change.
- `ANDROID_HOME=$HOME/Android/Sdk ./gradlew :core:jvmTest` -- expected: `PushFixturesTest` still passes.

**Manual checks (if no CLI):**
- Read the CI run of the pull request: the new end-to-end job and the changed path filters run only on GitHub.
