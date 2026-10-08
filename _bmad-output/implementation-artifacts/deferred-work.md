### DW-1: The architecture Stack pins Temporal Server 1.31.3, but no public container image exists for that version; the local stack uses the Temporal CLI development server instead.
origin: spec-deferred d24a3b6afbf1
location: _bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md (Stack table)
source_spec: `spec-1-1-monorepo-scaffold-ci-and-local-dev-stack.md`
severity: low
reason: Docker Hub temporalio/server lists 1.31.0, 1.31.1, 1.31.2 and 1.32.0 only; temporalio/auto-setup stops at 1.29.7 (checked 2026-09-28).
status: open

### DW-2: The Escendit hosting packages extend the concrete HostApplicationBuilder only, so an ASP.NET Core host cannot call AddServiceDefaults() or any Orleans extension of Escendit.Extensions.Hosting.Orleans;
origin: spec-deferred bc3a356a215e
location: apps/cs/server/Hosting/ServiceDefaultsExtensions.cs
source_spec: `spec-1-1-monorepo-scaffold-ci-and-local-dev-stack.md`
severity: medium
reason: The XML documentation of Escendit.Extensions.Hosting.ServiceDefaults 0.1.0-rc.4 lists AddServiceDefaults(HostApplicationBuilder, ...) and no overload for IHostApplicationBuilder or WebApplicationBuilder. Story 1.2 meets the same limit when it adds AdoNet clustering and NATS streams. Needs an upstream change (target IHostApplicationBuilder) or a decision to keep the shim.
status: open

### DW-3: No test asserts that the server exports telemetry when OTEL_EXPORTER_OTLP_ENDPOINT is set.
origin: spec-deferred 5f08a759f34d
location: apps/cs/server/Hosting/ServiceDefaultsExtensions.cs
source_spec: `spec-1-1-monorepo-scaffold-ci-and-local-dev-stack.md`
severity: low
reason: The integration run executes the branch but asserts nothing about telemetry. An assertion needs an OTLP collector in the test host.
status: open

### DW-4: The health test may fail on a slow runner when the server process is running but does not listen within the retry budget of the HTTP resilience handler.
origin: spec-deferred a7f34e0af370
location: tests/cs/server.integration/ServerHealthTests.cs
source_spec: `spec-1-1-monorepo-scaffold-ci-and-local-dev-stack.md`
reason: Not observed in any local run. To settle it, measure the time from the Running state to the first accepted connection on a GitHub runner and compare it with the retry budget of the standard resilience handler.
status: open

### DW-5: The CI jobs are not required status checks on main, so a failing job does not block a merge.
origin: spec-deferred eb356faf4c50
location: .github/workflows/ci.yml
source_spec: `spec-1-1-monorepo-scaffold-ci-and-local-dev-stack.md`
severity: medium
reason: The GitHub API reports no branch protection on main and a ruleset with deletion and non_fast_forward only. This is a repository setting; it is listed under operator_actions.
status: open

### DW-6: The CI workflow has never run on GitHub; the macOS Swift job and the Docker-based .NET and secrets jobs are verified only by running their commands locally.
origin: spec-deferred 7c23aad3bce0
location: .github/workflows/ci.yml
source_spec: `spec-1-1-monorepo-scaffold-ci-and-local-dev-stack.md`
severity: medium
reason: This run may not push or open a pull request. It is listed under operator_actions.
status: open

### DW-7: AD-21 asks for journal snapshots on a fixed event interval; this story has no acceptance criterion for them and no grain yet has a long stream, so the CustomStorage read replays the full stream.
origin: spec-deferred a59fe982da88
location: apps/cs/server/Journal/
source_spec: `spec-1-2-event-journal-migrations-and-projection-pipeline.md`
severity: low
reason: Story 1.2 acceptance criteria in epics.md (lines 527-561) name append, outbox, projectors, polling, time and replay, not snapshots.
status: open

### DW-8: Escendit.Orleans.Migrations.Cluster.PostgreSQL 10.3.1-rc.1 is published; the architecture and the story pin 10.3.1-rc.0, which this story keeps.
origin: spec-deferred 9c186f8991dd
location: Directory.Packages.props
source_spec: `spec-1-2-event-journal-migrations-and-projection-pipeline.md`
severity: low
reason: nuget.org flat container index lists 10.3.1-rc.0 and 10.3.1-rc.1 (checked 2026-09-28).
status: open

### DW-9: DESIGN.md sets hero-value, tile-value and tile-value-web to Ubuntu Condensed weight 300, but Ubuntu Condensed exists only in 400, so those roles render at 400.
origin: spec-deferred c0e9a91eb7ae
location: packages/design-tokens/tokens/tokens.json (hero-value, tile-value, tile-value-web)
source_spec: `spec-1-3-design-tokens-and-themes.md`
severity: low
reason: google/fonts ufl/ubuntucondensed ships only UbuntuCondensed-Regular.ttf; generated fonts.css has no Ubuntu Condensed 300 face. The token copies DESIGN.md faithfully; the design needs a decision (use 400, or Ubuntu Light for values).
status: open

### DW-10: No test drives a real authorization-code exchange between apps/ts/web and the coldframe realm in Keycloak; e2e tests use a fake OIDC provider and the realm is checked only as configuration.
origin: spec-deferred 7ffd886da898
location: aspire/keycloak/realms/coldframe-realm.json, tests/ts/web.e2e/fixtures/fake-idp.ts
source_spec: `spec-1-4-sign-in-on-the-web.md`
reason: Unverified (maybe-false). KeycloakTests.cs asserts the discovery document and the coldframe-web client settings; every browser test targets tests/ts/web.e2e/fixtures/fake-idp.ts. To settle it, sign in to the web app against the Aspire stack's Keycloak (docs/quickstart.md "Run the web app") with a registered user, or add an AppHost-hosted e2e run.
status: open

### DW-11: The CI workflow, including the new Playwright install and report-upload steps, has not run on GitHub; it is verified only by running the same commands locally.
origin: spec-deferred 9a6aaa58a7b5
location: .github/workflows/ci.yml
source_spec: `spec-1-4-sign-in-on-the-web.md`
severity: low
reason: Pre-existing and already tracked as DW-6 (spec-1-1). This run does not push.
status: open

### DW-12: The UX-DR92 unreachable notice says "Check that this phone is on your home Wi-Fi." on the web too; EXPERIENCE.md has no web variant.
origin: spec-deferred 1ad0b8ebf510
location: apps/ts/web/src/lib/i18n/en.json
source_spec: `spec-1-4-sign-in-on-the-web.md`
severity: low
reason: EXPERIENCE.md lines 155-164 give one copy for all platforms; the catalogue uses it verbatim. Changing it needs a UX decision in EXPERIENCE.md, which this story may not edit.
status: open

### DW-13: The iOS tab bar marks the selected tab with the native selected state and the primary-text tint, not the filled icon DESIGN.md asks for.
origin: spec-deferred fba6f0588ff2
location: apps/swift/ios/Sources/ColdframeIOS/UI/Screens.swift (AppTabView)
source_spec: `spec-1-5-sign-in-on-ios-and-android.md`
severity: low
reason: The vendored Carbon set in packages/design-tokens has no filled variants of grid, notification, box or settings. Needs filled glyphs added to the token subset or a UX decision; the spec forbids inventing glyphs.
status: open

### DW-14: The Android app and the Android targets compile against API 37 (platform android-37.0), not the compileSdk 36 the story names; targetSdk stays 36.
origin: spec-deferred 333a9b4f984d
location: gradle/libs.versions.toml (android-compile-sdk)
source_spec: `spec-1-5-sign-in-on-ios-and-android.md`
severity: low
reason: Compose 1.12.1 from BOM 2026.09.00, Lifecycle 2.11.0 and OkHttp 5.5.0 (through Ktor 3.6.0) declare minCompileSdk 37 in their AAR metadata, so checkDebugAarMetadata fails at 36. Every pinned version was kept; CI installs platforms;android-37.0.
status: open

### DW-15: kotlin-multiplatform-oidc 0.18.3 falls back to a WebView on Android when no Custom Tabs browser is installed, even with useWebView = false.
origin: spec-deferred 9396e567c2cc
location: packages/kt/core/src/androidMain/kotlin/com/escendit/coldframe/core/signin/AndroidSignIn.kt
source_spec: `spec-1-5-sign-in-on-ios-and-android.md`
severity: medium
reason: AndroidCodeAuthFlowFactory.createWebFlow picks WebViewFlow when getCustomTabProviders() is empty. The story forbids WebView sign-in. Needs an upstream option, a wrapper factory that refuses to start without a Custom Tabs provider (and a notice for that case), or a UX decision.
status: open

### DW-16: The Android and iOS apps have no app icon; DESIGN.md says the icon uses the Coldframe mark, and no mark asset exists in packages/design-tokens.
origin: spec-deferred 819e72a47758
location: apps/kt/android/src/main/AndroidManifest.xml, apps/swift/ios/project.yml
source_spec: `spec-1-5-sign-in-on-ios-and-android.md`
severity: low
reason: Android lint reports MissingApplicationIcon (warning). The web app renders the mark as the word Coldframe in the headline role. Needs the mark as a vector asset.
status: open

### DW-17: Material 3 NavigationBar and AlertDialog animate their indicator, ripple and entry internally; UX-DR101 asks for no animations.
origin: spec-deferred 8b023ac942ea
location: apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/ui/shell/AppShell.kt
source_spec: `spec-1-5-sign-in-on-ios-and-android.md`
severity: low
reason: The shell's own code has no animation (source-scan test). The Material components honour the system animator duration scale (Remove animations) but cannot be switched off per app without replacing them.
status: open

### DW-18: The iOS certificate classifier (CertificateErrors.ios.kt, NSURLErrorDomain -1200...-1206) has no test, and it is unverified whether a DarwinHttpRequestException stays in the cause chain after the
origin: spec-deferred 9b6d4d207d17
location: packages/kt/core/src/iosMain/kotlin/com/escendit/coldframe/core/signin/CertificateErrors.ios.kt
source_spec: `spec-1-5-sign-in-on-ios-and-android.md`
reason: Unverified (maybe-false). Only the JVM classifier is tested; iOS tests cannot run on Linux and the ios CI job only runs xcodebuild build. Settle it with an iosTest mirroring CertificateErrorsTest run via ./gradlew :core:iosSimulatorArm64Test on macOS, and a real untrusted-certificate sign-in on an iPhone.
status: open

### DW-19: If the browser flow never returns (activity destroyed mid-flow, lost ASWebAuthenticationSession callback), the engine may stay in Working with SIGN IN disabled and signOut waiting on the mutex.
origin: spec-deferred 285c44893882
location: packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/signin/SignInEngine.kt
source_spec: `spec-1-5-sign-in-on-ios-and-android.md`
reason: Unverified (maybe-false). Depends on whether kotlin-multiplatform-oidc's flows always resume or throw when the hosting activity/session goes away; process death is covered by canContinueLogin. Settle it by rotating/backgrounding the app during the Custom Tab on a device and by reading PlatformCodeAuthFlow's suspension handling.
status: open

### DW-20: The iOS UX-DR104 and UX-DR126 render tests only assert that ImageRenderer produces an image; they do not check the announcement text/priority or clipping.
origin: spec-deferred 4f517ad1edcd
location: tests/swift/ios/ColdframeIOSTests/RenderTests.swift, apps/swift/ios/Sources/ColdframeIOS/UI/Components.swift
source_spec: `spec-1-5-sign-in-on-ios-and-android.md`
severity: low
reason: RenderTests.swift asserts renders(...) == true only; deleting .onAppear { announce() } in Components.swift breaks no test. Needs a macOS-only seam (e.g. a tested builder for the announcement AttributedString).
status: open

### DW-21: Keycloak events dropped while Temporal is down are never replayed, so a quiet Site stays drifted until another event touches it.
origin: spec-deferred 9866fdc345ff
location: apps/cs/server/Identity/Reconciliation/IdentityReconciliationActivities.cs
source_spec: `spec-1-7-reconcile-identity-changes-from-keycloak.md`
severity: medium
reason: keycloak-temporal-extensions v0.0.1-rc.2 logs and drops an event when its workflow start fails (upstream, pre-existing). Reconciliation is event-triggered only. A periodic full-roster sweep (for example a Temporal schedule or an Orleans reminder per Site) would close the gap.
status: open

### DW-22: A single GET /orgs/{id} 404 during any reconcile deletes the Site permanently, and a misconfigured service account might cause such 404s.
origin: spec-deferred 764627bf3268
location: apps/cs/server/Identity/SiteGrain.cs (Reconcile, RaiseDeleted)
source_spec: `spec-1-7-reconcile-identity-changes-from-keycloak.md`
reason: Unverified: whether Phase Two answers 404 (not 403) to GET /orgs/{id} when the coldframe-server service account lacks view-organizations. If it does, one event per Site would move every touched Site to Deleted (terminal, AD-20). Settle it on a live container by removing the role and calling the endpoint.
status: open

### DW-23: The time zone confirmed on Create Site is kept per device (web cookie cf_time_zone, mobile DeviceChoices) and never reaches the Server.
origin: spec-deferred f53f05a8f999
location: packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/sites/DeviceChoices.kt, apps/ts/web/src/lib/server/create-site.ts
source_spec: `spec-1-8-create-site-and-the-empty-garden-in-the-apps.md`
severity: low
reason: AD-11 and FR16 make the time zone a User preference; POST /sites takes only {name}. The Notification Window story (epics.md 1667-1696, UX-DR48) adds the User time-zone endpoint and must send the stored per-device choice once, then read it from the Server.
status: open

### DW-24: The Site menu renders only Site settings; Pause/Resume and the stale-mode disabled state exist only in the menu models (core SiteMenu.items, web site-menu.ts).
origin: spec-deferred fb1a514d4b3c
location: packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/sites/SiteMenu.kt, apps/ts/web/src/lib/site-menu.ts
source_spec: `spec-1-8-create-site-and-the-empty-garden-in-the-apps.md`
severity: medium
reason: UX-DR22 in full needs the Pause sheet (UX-DR46/70) and stale mode, which arrive in later epics; rendering a Pause item that does nothing would break controls-you-cannot-use-are-hidden. Turn on pauseAvailable with the Pause story and stale with stale mode, and render the Needs your Server state on all three clients.
status: open
progress: Story 4.7 (`spec-4-7-lot-status-and-the-site-overview.md`) closes the stale part: in stale mode the Site menu items are disabled with "Needs your Server" on all three clients (core `LotsState.siteMenu()`, web `site-menu.ts` fed from the layout). Pause/Resume stays hidden until the Pause story (Epic 8).

### DW-25: Site settings in the Site menu opens the Settings index, not a Site settings surface.
origin: spec-deferred cd4566c213ff
location: apps/ts/web/src/lib/components/SiteMenu.svelte, apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/ui/sites/SiteMenu.kt, apps/swift/ios/Sources/ColdframeIOS/UI/SitesViews.swift
source_spec: `spec-1-8-create-site-and-the-empty-garden-in-the-apps.md`
severity: low
reason: The Site settings surface arrives in Story 1.9; point the item at it there.
status: resolved
resolution: Story 1.9 (`spec-1-9-manage-my-site-and-lots.md`): the Site menu item opens Site settings on every client (web `/settings/site`, Android and iOS the Site settings sub-screen).

### DW-26: The first-run step tiles are never actionable (flowAvailable = false on every client).
origin: spec-deferred 501fdb82f35d
location: packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/sites/FirstRunSteps.kt, apps/ts/web/src/lib/first-run.ts
source_spec: `spec-1-8-create-site-and-the-empty-garden-in-the-apps.md`
severity: medium
reason: The Add a Hub flow is a later epic. When it lands, pass flowAvailable = true on mobile so the next step starts the flow for Administrators and Owners; the tile states and the Member notice are already real. Done steps (checkmark) also need Hub/Node data.
status: open
progress: Story 3.6 (`spec-3-6-add-a-hub-from-my-phone.md`) makes the Add a Hub tile start the flow on Android and iOS for Administrators and Owners (`FirstRunSteps.of` defaults `flowAvailable` to true in the mobile core). Still open: the Node, Calibrate and Threshold steps, and done checkmarks from Hub/Node data. Story 3.7 (`spec-3-7-see-my-hub-in-devices.md`) adds the Hub data a done checkmark needs (`GET /sites/{siteId}/devices`, the core's `DevicesEngine`), but the Add a Hub tile does not read it yet: it stays "next" after a Hub is enrolled. Story 4.3 (`spec-4-3-add-a-node-from-my-phone.md`) adds the Add a Node flow but leaves the first-run Add a Node tile without an action (see DW-57).

### DW-27: The Kotlin API client and DTOs are hand-written instead of generated from coldframe.openapi.json (AD-10 deviation).
origin: spec-deferred ec8f62ba78ca
location: packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/api/
source_spec: `spec-1-8-create-site-and-the-empty-garden-in-the-apps.md`
severity: low
reason: openapi-generator's multiplatform output does not fit explicitApi(), Ktor 3.6 and the value-result style. OpenApiContractTest (tests/kt/core/jvmTest) fails when an operation, path, method, header or DTO property the core uses is missing from the contract; it does not detect new contract fields the core ignores. Revisit if a generator fits later.
status: open

### DW-28: Pull-to-refresh on the mobile Garden and refetch on focus on the web (UX-DR62) are not built.
origin: spec-deferred 1d32e1d55b52
location: apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/ui/sites/GardenScreen.kt, apps/swift/ios/Sources/ColdframeIOS/UI/SitesViews.swift, apps/ts/web/src/routes/(app)/+layout.server.ts
source_spec: `spec-1-8-create-site-and-the-empty-garden-in-the-apps.md`
severity: low
reason: Story 1.8 forbids a polling loop and has no Lot data to refresh yet; the Sites list reloads on sign-in (mobile) and on every navigation (web). Add refresh with the Lot grid.
status: resolved
resolution: Story 4.7 (`spec-4-7-lot-status-and-the-site-overview.md`): the Garden has pull-to-refresh and a reload when the app returns to the foreground on Android and iOS (`LotsEngine.refresh()`), and the web refetches on focus. The Devices part of the progress note below moved to DW-70.
progress: Story 3.7 (`spec-3-7-see-my-hub-in-devices.md`) adds Devices without pull-to-refresh, refetch on focus, polling or SignalR (UX-DR112): the list is read on a page load (web) and on every entry of the Devices tab (mobile). A Devices screen left open is not read again, so a Hub whose heartbeat stops keeps reading "Online" with its old last-seen time until the user leaves and re-enters the tab or reloads the page; returning to the app from the background does not reload it either. The same refresh primitives close this for Devices (apps/ts/web/src/routes/(app)/devices/+page.server.ts, apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/ui/shell/AppShell.kt, apps/swift/ios/Sources/ColdframeIOS/UI/Screens.swift).

### DW-29: On iOS, "New Site" in the Site switcher may never open Create Site, because the Create Site sheet is requested while the switcher sheet is still closing.
origin: spec-deferred review-1-8-ios-sheet
location: apps/swift/ios/Sources/ColdframeIOS/UI/SitesViews.swift (GardenView onNewSite), apps/swift/ios/Sources/ColdframeIOS/UI/Screens.swift (ColdframeRootView .sheet)
source_spec: `spec-1-8-create-site-and-the-empty-garden-in-the-apps.md`
severity: medium (unverified)
reason: Unverified: SwiftUI often drops a sheet presentation requested during another sheet's dismissal, and `creating` would then stay set, so later New Site taps are ignored. Settle it on a Mac or iPhone; if it happens, call actions.newSite() from the switcher sheet's onDismiss.
status: open

### DW-30: On iOS, "New Site" in the switcher may never open Create Site, because the root view asks for the Create Site sheet while GardenView's switcher sheet is still closing.
origin: spec-deferred 6c80305c2a33
location: apps/swift/ios/Sources/ColdframeIOS/UI/SitesViews.swift (GardenView onNewSite), apps/swift/ios/Sources/ColdframeIOS/UI/Screens.swift (ColdframeRootView .sheet)
source_spec: `spec-1-8-create-site-and-the-empty-garden-in-the-apps.md`
reason: Unverified (maybe-false): SwiftUI often drops a sheet presentation requested during another sheet's dismissal. If it does, `creating` stays set and `newSite()` then ignores every tap, so Create Site cannot be reached until the app restarts. Settle it on a Mac or iPhone: open the switcher, tap New Site, and check that Create Site appears. If it does not, call actions.newSite() from the switcher sheet's onDismiss. SwiftUI cannot compile or run on Linux.
status: open

### DW-31: Lot tiles are not tappable: no Lot detail, and a no-Node tile does not start Add a Node (UX-DR20 tap behaviour).
origin: spec-deferred story-1-9-1
location: apps/ts/web/src/lib/components/LotTiles.svelte, apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/ui/sites/LotTile.kt, apps/swift/ios/Sources/ColdframeIOS/UI/SiteSettingsViews.swift (LotTile)
source_spec: `spec-1-9-manage-my-site-and-lots.md`
severity: low
reason: Lot detail and the Add a Node flow arrive in Epic 4. The web tile is exposed as role="img" so it reads as one element; switch it to a link or button (and Android/iOS to a button role) when tiles become tappable.
status: open

### DW-32: Only the no-Node tile variant is drawn; any other LotStatus renders the Lot name alone (UX-DR18 needsWater, needsCalibration, unknown, ok, paused variants).
origin: spec-deferred story-1-9-2
location: apps/ts/web/src/lib/components/LotTiles.svelte, apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/ui/sites/LotTile.kt, apps/swift/ios/Sources/ColdframeIOS/LotsPresentation.swift
source_spec: `spec-1-9-manage-my-site-and-lots.md`
severity: low
reason: The other statuses need Readings, Calibration and Pause (Epic 5). In 1.9 the projection sets unknown for a claimed Lot (reachable only through a fixture claim), noNode otherwise.
status: resolved
resolution: Story 4.7 (`spec-4-7-lot-status-and-the-site-overview.md`): all six variants, the stale variant and (mobile) the skeleton are drawn on web, Android and iOS from the Server's fields. `needsCalibration`, `ok`, `unknown` (Node that declared nothing) and `noNode` are live; `needsWater`, `unknown` by Hub and `paused` render from fixtures until Epics 6, 7 and 8.

### DW-33: Site settings has no Site Reminder cadence control (UX-DR50, UX-DR74 part).
origin: spec-deferred story-1-9-3
location: apps/ts/web/src/routes/(app)/settings/site/+page.svelte, apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/ui/settings/SiteSettingsScreen.kt, apps/swift/ios/Sources/ColdframeIOS/UI/SiteSettingsViews.swift
source_spec: `spec-1-9-manage-my-site-and-lots.md`
severity: low
reason: Reminders arrive in a later epic; add the Segmented choice (Owner/Admin) to the same surface then.
status: open

### DW-34: The Lot grain has no Claim/Release methods; LotClaimed/LotReleased are only journaled by fixtures.
origin: spec-deferred story-1-9-4
location: apps/cs/server/Lots/LotGrain.cs, packages/cs/contracts/Lots/LotGrains.cs
source_spec: `spec-1-9-manage-my-site-and-lots.md`
severity: low
reason: Node assignment arrives in Epic 4 (AD-18); the removal refusal is tested with a fixture LotClaimed event. The Lot read model's refetch on readmodel.changed also waits for that epic; DW-28 stays open.
status: open

### DW-35: Renaming a Site reads the whole Keycloak Organization and writes it back with the new displayName, so a concurrent Organization edit between the read and the write can be overwritten.
origin: spec-deferred story-1-9-5
location: apps/cs/server/Identity/PhaseTwoOrganizations.cs (UpdateDisplayNameAsync)
source_spec: `spec-1-9-manage-my-site-and-lots.md`
severity: low
reason: Phase Two's organization update takes the full representation. RenameSiteTests checks that the creation tag survives; other attributes are not re-checked. Reconciliation (Story 1.7) repairs displayName drift but not other fields.
status: open

### DW-36: On Android, the per-row "Rename Lot" and "Remove Lot" buttons in Site settings do not carry the Lot name, so TalkBack reads the same label on every row.
origin: spec-deferred story-1-9-6
location: apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/ui/settings/SiteSettingsScreen.kt
source_spec: `spec-1-9-manage-my-site-and-lots.md`
severity: low
reason: Add a content description with the Lot name (new string keys on Android and iOS, parity-checked by StringsTest) and check the web and iOS rows the same way.
status: open

### DW-37: Nothing alerts when WAL archiving or base backups fail.
origin: spec-deferred 3f8131c18a6e
location: deploy/charts/database/templates/cluster.yaml
source_spec: `spec-2-3-database-cluster-with-off-node-backups-and-tested-restore.md`
severity: medium
reason: ContinuousArchiving turning False is only visible through a manual kubectl check in restore.md; no PodMonitor or alert rule exists, and Epic 2 has no monitoring stack.
status: open

### DW-38: The backup ObjectStore offers no region or endpointCA setting.
origin: spec-deferred b15e11d2e666
location: deploy/charts/database/templates/objectstore.yaml
source_spec: `spec-2-3-database-cluster-with-off-node-backups-and-tested-restore.md`
reason: Unverified: settle by archiving to an S3 provider that requires a non-default signing region (AWS outside us-east-1, some Backblaze or Wasabi endpoints) with only endpointURL set. Adding a region key changes the Secret contract.
status: open

### DW-39: Password rotation of coldframe-db-* through CNPG managed roles is documented but never exercised.
origin: spec-deferred c610cc8e55f9
location: deploy/SECRETS.md
source_spec: `spec-2-3-database-cluster-with-off-node-backups-and-tested-restore.md`
severity: medium
reason: SECRETS.md says updating a Secret labelled cnpg.io/reload=true changes the role password; no smoke step rotates a Secret and checks the new password logs in and the old one does not.
status: open

### DW-40: The Traefik checks render rke2-traefik without the values RKE2 itself injects into the chart.
origin: spec-deferred 38a6cbe94bbd
location: deploy/rke2/rke2-traefik-config.yaml
source_spec: `spec-2-4-tls-with-public-certificates-on-my-home-network.md`
reason: Unverified: test.sh and smoke.sh layer only the HelmChartConfig valuesContent over the chart defaults. If RKE2 v1.36.4's own HelmChart values set anything under ports.web, the node could differ. Settle on the home server with `kubectl -n kube-system get helmchart rke2-traefik -o jsonpath='{.spec.valuesContent}'` and `kubectl -n kube-system get svc rke2-traefik` (443 only).
status: open

### DW-41: The Fleet smoke has only run on kind; the k3d path that CI uses, and the Images job timeout of 170 minutes, are unproven.
origin: spec-deferred f6fa9df6ca1f
location: .github/workflows/images-verify.yml (Fleet smoke); deploy/fleet/smoke.sh
source_spec: `spec-2-5b-fleet-smoke-proves-ordered-install-upgrade-and-restart-durability.md`
reason: Unverified: .github/ has no SMOKE_CLUSTER setting, so CI runs the k3d path (the real k3s HelmChartConfig CRD, k3d image import, docker restart of the k3d node), and every local run used kind. It is settled by one green Images job on GitHub Actions that includes the "Fleet smoke" step, together with its step duration.
status: open

### DW-42: A partial ets_efuse_write_key failure might leave a block that a later boot accepts as the identity.
origin: spec-deferred 584bf06928df
location: apps/rs/hub/src/board/efuse.rs (burn_key)
source_spec: `spec-3-2-hub-firmware-foundation-with-a-hardware-bound-identity.md`
reason: maybe-false. BoardEfuse::burn_key maps a nonzero ROM return to BurnFailed and halts. If the ROM had already set the HMAC_UP purpose and RD_DIS before failing on the key data, the next boot would take the Existing path with a partial key. Settle it with the ESP32-S3 ROM efuse source or a bench fault test.
status: open

### DW-43: The Hub firmware's dev-mode release guard and board eFuse/HMAC index mappings are not checked by any automated test.
origin: spec-deferred 71ac7f6c56f0
location: apps/rs/hub/src/main.rs, apps/rs/hub/src/board/efuse.rs, apps/rs/hub/src/board/hmac.rs
source_spec: `spec-3-2-hub-firmware-foundation-with-a-hardware-bound-identity.md`
severity: medium
reason: apps/rs is excluded from the workspace, and CI installs no esp toolchain. The mocks index blocks directly, so rom_block (4 + n), the RD_DIS bit n and the KeyId mapping are verified only by a local build and the bench checklist. Fix with an esp-toolchain CI job, or by moving the mappings into coldframe-hal where host tests can pin them.
status: open

### DW-44: A Device enrolled on a Site that is later deleted can never be enrolled again: re-enrolling on that Site answers 404, and every other Site answers 409.
origin: spec-deferred 118ab73be7a0
location: apps/cs/server/Devices/DeviceGrain.cs (Enrol, step 1)
source_spec: `spec-3-3-server-side-device-enrolment.md`
severity: medium
reason: DeviceGrain.Enrol refuses any Site other than State.SiteId, and nothing un-enrols a Device or releases it when its Site is deleted. This follows the story's "Device already enrolled on another Site → 409" rule literally. Releasing or moving a Device (AD-2 "moved, unassigned") belongs to the later Device lifecycle and Site-deletion work.
status: open

### DW-45: A Device grain replays its whole journal stream on activation, with no snapshot, and each Hub now adds a device.seen event every 30-60 s (about 700k a year).
origin: spec-deferred 5734c6c6f117
location: apps/cs/server/Journal/JournaledStreamGrain.cs (ReadStateFromStorage); apps/cs/server/Devices/DeviceGrain.cs (Heartbeat)
source_spec: `spec-3-5-hub-joins-wi-fi-and-heartbeats-to-the-server.md`
severity: medium
reason: JournaledStreamGrain.ReadStateFromStorage reads Store.ReadStreamAsync(StreamId) in full; there is no snapshot anywhere in apps/cs/server/Journal. Story 3.5 journals every accepted heartbeat by design (spec Design Notes). A silo restart after months of heartbeats reactivates each Device by replaying hundreds of thousands of rows. Fix with journal snapshots or a retention/compaction rule for Device streams.
status: open

### DW-46: A provisioned Hub has no way back into BLE setup, so a changed Wi-Fi password, a re-enrolled or removed Device, or a new Server host leaves it retrying forever until cf_setup is erased by hand.
origin: spec-deferred f95370c2deb3
location: apps/rs/hub/src/main.rs (provisioned path); packages/rs/setup/src/service.rs (Boot)
source_spec: `spec-3-5-hub-joins-wi-fi-and-heartbeats-to-the-server.md`
severity: medium
reason: Since Story 3.4 the Hub advertises only while unprovisioned (apps/rs/hub/src/main.rs, Boot::advertises). Story 3.5's uplink retries joins and heartbeats forever with backoff. The only recovery is espflash erase-region 0xC000 0x2000. It needs a Device lifecycle decision: a reset button, a factory-reset gesture, or re-provisioning after N failures.
status: open

### DW-47: A Hub abandoned after its enrolment answered 201 stays enrolled on that Site; setting it up on another Site later answers 409 and the app shows "Hub 3F2A is on another Site".
origin: spec-deferred f50597d6e0ec
location: packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/setup/HubSetupEngine.kt (startProgress); apps/cs/server/Devices/DeviceGrain.cs (Enrol)
source_spec: `spec-3-6-add-a-hub-from-my-phone.md`
severity: medium
reason: Enrolment must precede WifiConfig (the Server refuses heartbeats from unknown Devices), so a flow left after the 201 leaves an idempotent same-Site enrolment and an unprovisioned Hub that advertises again. Setting it up again on the same Site converges; moving it needs the Device release/move lifecycle (see DW-44).
status: open

### DW-48: ADD A NODE on the "Hub is online" outcome closes the flow to the Garden instead of starting Add a Node.
origin: spec-deferred 6cd4c3d9230c
location: packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/setup/HubSetupEngine.kt (outcomeAction AddNode); apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/ui/setup/SetupOutcome.kt; apps/swift/ios/Sources/ColdframeIOS/UI/HubSetupViews.swift
source_spec: `spec-3-6-add-a-hub-from-my-phone.md`
severity: low
reason: The Add a Node flow arrives in Epic 4; wire OutcomeAction.AddNode to it there.
status: resolved
resolution: Story 4.3 (`spec-4-3-add-a-node-from-my-phone.md`): `HubSetupEngine` calls `onAddNode` with the Hub's Site after closing, and `SitesWiring` passes `NodeSetupEngine.open(siteId)` on both platforms.

### DW-49: The engine's check that the EnrolmentResponse device_id matches the accepted Identity has no test.
origin: spec-deferred 14b884d696f1
location: packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/setup/HubSetupEngine.kt (startProgress, device_id comparison)
source_spec: `spec-3-6-add-a-hub-from-my-phone.md`
severity: low
reason: FakeHub always answers device_id = HUB_DEVICE_ID (tests/kt/core/commonTest/.../setup/SetupFakes.kt); deleting the comparison in HubSetupEngine.startProgress leaves every test green. The Server would likely reject a mismatched seal, so this is hardening.
status: open

### DW-50: iOS may briefly show and announce the Bluetooth-needed notice on every open because IosRadioState starts at Off and maps Unknown/Resetting to Off before CoreBluetooth reports.
origin: spec-deferred 7d8cd8036969
location: packages/kt/core/src/iosMain/kotlin/com/escendit/coldframe/core/setup/IosRadioState.kt:18-29
source_spec: `spec-3-6-add-a-hub-from-my-phone.md`
reason: IosRadioState initialises MutableStateFlow(RadioState.Off) and maps every non-listed CBManagerState to Off. Whether the notice renders (and VoiceOver announces it) before centralManagerDidUpdateState arrives can only be settled on a real iPhone; RadioState has no Unknown value today.
status: open

### DW-51: The Node copies the Hub's ESP32-S3 board adapters (eFuse, HMAC, flash, TRNG, timer) instead of sharing them.
origin: spec-deferred 0a96fec2fb86
location: apps/rs/node/src/board/{efuse,hmac,flash,rng,timer}.rs; apps/rs/hub/src/board/{efuse,hmac,flash,rng,timer}.rs
source_spec: `spec-4-1-node-firmware-foundation-wake-measure-sleep.md`
severity: low
reason: apps/rs is outside the host workspace and Story 4.1 must not change the Hub, so the adapters were copied (the Node's flash adapter already differs: word-aligned writes are plain NOR programs, which the counters need for power-loss safety). Extract a shared ESP32-S3 board crate (for example packages/rs/board-esp32s3, built only with the esp toolchain) and move both firmwares onto it; decide then whether the Hub's flash writes should also avoid read-erase-rewrite. A shared board adapter must keep plain NOR writes for the counter partitions (`cf_seq`, `cf_boot`), never read-erase-rewrite.
status: open

### DW-52: The Node flash adapter's plain-NOR write path, which the counters' power-loss safety depends on, is not checked by any automated test.
origin: spec-deferred 47f7c720ad7c
location: apps/rs/node/src/board/flash.rs (BoardFlash::write)
source_spec: `spec-4-1-node-firmware-foundation-wake-measure-sleep.md`
severity: low
reason: Host counter tests run over MockFlash only; apps/rs/node/src/board/flash.rs is only cross-compiled in CI. Reverting BoardFlash::write to esp-storage's read-erase-rewrite would pass every automated check. Mitigated by the bench power-cut step in docs/bench/node-power-checklist.md Part B and the DW-51 note; a host test needs the board adapter extracted into a crate that can run over a fake NOR backend (DW-51).
status: open

### DW-53: On iOS, reloading the list on every entry of the Devices tab and returning to Devices after Add a Hub closes have no behavioural test.
origin: spec-deferred e07df2630c2f
location: apps/swift/ios/Sources/ColdframeIOS/UI/Screens.swift (AppTabView .onChange, ColdframeRootView tab state)
source_spec: `spec-3-7-see-my-hub-in-devices.md`
severity: medium
reason: The only iOS tests that reach AppTabView are RenderTests, whose helper asserts that ImageRenderer returns an image; the Devices one passes selection: .constant(.devices) and no actions. Removing the .onChange(of: selection) modifier in UI/Screens.swift, or passing selection: nil from the root view, fails no test. Android has both tests in DevicesScreenTest. Closing the gap needs the "entered Devices, so load" decision in a type that compiles on Linux, or a SwiftUI interaction harness.
status: open

### DW-54: The app-target wiring of the Devices engine (MainActivity, ColdframeApp, CoreDevicesService) can be dropped or mis-mapped without any test failing.
origin: spec-deferred 4240a8dab832
location: apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/MainActivity.kt; apps/swift/ios/App/{ColdframeApp,CoreDevicesService}.swift
source_spec: `spec-3-7-see-my-hub-in-devices.md`
severity: medium
reason: ColdframeRoot, the iOS root view and ShellModel default the Devices parameters to Idle/.waiting/.none, so deleting the two argument lines in MainActivity.setContent keeps ./gradlew check green with a blank Devices tab. MainActivityTest only asserts the cold start shows SIGN IN. CoreDevicesService maps hubStatuses and hubLastSeen, both [String], by position, and apps/swift/ios/App has no tests. The Lots and Add a Hub wiring has the same shape.
status: open

### DW-55: The Node candidate tile shows no battery % and no Sensor count, which UX-DR37 asks for.
origin: spec-deferred ddbb054be520
location: apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/ui/setup/CandidateTile.kt; apps/swift/ios/Sources/ColdframeIOS/UI/HubSetupViews.swift (CandidateTileView); packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/setup/NodeSetupState.kt (NodeCandidate)
source_spec: `spec-4-3-add-a-node-from-my-phone.md`
severity: low
reason: Neither the setup-mode advertisement (name `Coldframe Node XXXX` and RSSI) nor `Identity` (device_id, kind, firmware_version) carries a battery level or the Sensor set, and the tile is shown before any session exists. Showing them needs a firmware and proto change (advertisement manufacturer data or new `Identity` fields); the tile then takes two more strings.
status: open

### DW-56: Add a Node and Add a Hub candidates never expire: a Device that stopped advertising stays in the list until the step is left.
origin: spec-deferred 1c8e910e303c
location: packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/setup/NodeSetupEngine.kt (onAdvert); packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/setup/HubSetupEngine.kt (onAdvert)
source_spec: `spec-4-3-add-a-node-from-my-phone.md`
severity: low
reason: A Node listens for 180 s; a Node whose window closed while the list is shown is still listed, and picking it ends on "7C19 stopped listening" after the code is typed. "Pressed just now" likewise stays on the candidate first heard last, however long ago. Pruning needs a last-heard time per candidate and a timer in the engine (the same rejected finding exists for Add a Hub in `spec-3-6-add-a-hub-from-my-phone.md`).
status: open

### DW-57: The first-run Add a Node tile on the Garden is not actionable, though UX-DR67 lists first-run step 2 as an entry point.
origin: spec-deferred 6bd3db5dd179
location: packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/sites/FirstRunSteps.kt; apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/ui/sites/GardenScreen.kt (FirstRunTiles); apps/swift/ios/Sources/ColdframeIOS/UI/SitesViews.swift
source_spec: `spec-4-3-add-a-node-from-my-phone.md`
severity: low
reason: Story 4.3 names three entry points (Devices, a *no Node* tile, "Hub is online"). The tile's state (next only once a Hub is online, done once a Node is assigned) needs Hub and Node knowledge the Garden does not have; the Add a Hub tile has the same gap (DW-26). Make the tile start the flow when `FirstRunSteps` is computed from the Devices list.
status: open

### DW-58: The "‹Lot› has a Node" outcome shows no Sensors, no CALIBRATE SOIL MOISTURE action and no paused-Site note.
origin: spec-deferred d4006c8485c3
location: packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/setup/NodeSetupState.kt (NodeOutcome); apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/ui/setup/AddNodeFlow.kt; apps/swift/ios/Sources/ColdframeIOS/UI/NodeSetupViews.swift
source_spec: `spec-4-3-add-a-node-from-my-phone.md`
severity: medium
reason: UX-DR67 and UJ-1 step 6 show the Node's Sensors with "needs calibration" and a Calibrate action, and State Patterns adds "Home garden is paused, so 7C19 starts paused" with Resume Site. Sensors exist only after the first Readings (Stories 4.4 to 4.6), Calibrate arrives in Epic 5 and Pause in Epic 8. Until then the outcome says "Its Sensors appear with its first Readings." and offers Done.
status: open

### DW-59: On iOS the Devices header now has two ghost actions and no handling for the largest text sizes; whether the heading and both buttons fit is unknown.
origin: spec-deferred 294575244e1d
location: apps/swift/ios/Sources/ColdframeIOS/UI/DevicesViews.swift (toolbar)
source_spec: `spec-4-3-add-a-node-from-my-phone.md`
reason: DevicesViews.swift adds a second .primaryAction toolbar button. Android stacks the two from font scale 1.5; the iOS Devices tests are presentation-only and RenderTests only assert that an image renders. Settle it by rendering the Devices tab for an Owner at .accessibility5 on a Mac or iPhone.
status: open

### DW-60: On iOS, Add a Node opened from "Hub is online" may lose keep-awake if the Hub flow's onDisappear runs after the Node flow's onAppear.
origin: spec-deferred 1a3d37bc233c
location: apps/swift/ios/Sources/ColdframeIOS/UI/HubSetupViews.swift (SetupFlowEffects)
source_spec: `spec-4-3-add-a-node-from-my-phone.md`
reason: SetupFlowEffects sets isIdleTimerDisabled in onAppear and clears it in onDisappear; the root swaps AddHubFlowView for AddNodeFlowView in one update. SwiftUI's ordering of the two callbacks decides it. Settle it on an iPhone: open Add a Node from the Hub outcome and watch whether the screen dims and locks.
status: open

### DW-61: No test joins the Hub outcome's Add a Node action to an open Node flow through the platform wiring.
origin: spec-deferred 086143815222
location: packages/kt/core/src/androidMain/.../signin/AndroidSignIn.kt; packages/kt/core/src/iosMain/.../signin/IosSignIn.kt; packages/kt/core/src/commonMain/.../sites/SitesWiring.kt
source_spec: `spec-4-3-add-a-node-from-my-phone.md`
severity: medium
reason: HubSetupEngineTest passes its own onAddNode lambda; the join is onAddNode = { nodeSetup.open(it) } in AndroidSignIn.kt and IosSignIn.kt, forwarded by SitesWiring.hubSetup, and every hop defaults to {}. Dropping the argument compiles and Add a Node closes to the Garden as before this story, with every test green. Same shape as DW-54.
status: open

### DW-62: The iOS entry points of Add a Node (Devices action, no-Node tile) are only checked by render tests that assert an image exists.
origin: spec-deferred 5b13a24ea572
location: apps/swift/ios/Sources/ColdframeIOS/UI/{Screens,DevicesViews,SiteSettingsViews}.swift
source_spec: `spec-4-3-add-a-node-from-my-phone.md`
severity: medium
reason: LotGrid's Button, the Devices toolbar item and the root's onAddNode: nodeSetupActions.open each default to a no-op closure; no Swift test passes or observes onAddNode. Removing the root's argument compiles and both entry points do nothing. Taps are covered on Android only. Needs a view-interaction harness or the tap decision in Linux-compiled code.
status: open

### DW-63: The app-target wiring of Add a Node (MainActivity, ColdframeApp, CoreNodeSetupService, canAddNode in CoreLotsService and CoreDevicesService) can be dropped or mis-mapped without a test failing.
origin: spec-deferred a388e9f8f485
location: apps/kt/android/.../MainActivity.kt; apps/swift/ios/App/{ColdframeApp,CoreNodeSetupService,CoreLotsService,CoreDevicesService}.swift
source_spec: `spec-4-3-add-a-node-from-my-phone.md`
severity: medium
reason: ColdframeRoot, ColdframeRootView, ShellModel and the Lots and Devices presentations default the new parameters to closed, none or false. Deleting MainActivity's two nodeSetup lines, or canAddNode: snapshot.canAddNode in CoreLotsService, keeps every check green. CoreNodeSetupService maps 43 snapshot fields by hand and apps/swift/ios/App has no tests. Extends DW-54.
status: open

### DW-64: A Node whose clock runs more than 5 minutes ahead is answered rejected_time on every frame and never receives the server time that would correct it.
origin: spec-deferred 87dfdf9eecc4
location: apps/cs/server/Devices/DeviceGrain.cs (Ingest, rejected_time branch)
source_spec: `spec-4-5-server-ingestion-and-acknowledgements.md`
severity: medium
reason: AD-9 gives a downlink only to stored and duplicate, and AD-11 makes the sealed downlink the Node's only time source. DeviceGrain.Ingest follows both, so a rejected_time frame carries no server_time_ms. The rule comes from the architecture, not from this story; it needs a decision there (for example a time-only downlink).
status: open

### DW-65: No test makes the journal append of DeviceRelayChanged fail, so the retry answer for that failure is unproven.
origin: spec-deferred 5f96a42808ab
location: apps/cs/server/Devices/DeviceGrain.cs (Ingest, relay change after the commit)
source_spec: `spec-4-5-server-ingestion-and-acknowledgements.md`
severity: low
reason: IdentityCluster's FaultyIngestionStore can fail the replay load and the commit, but the fixture has no hook that fails a journal append. JournaledGrain with custom storage may also retry a failed write instead of throwing, in which case the catch in DeviceGrain.Ingest is never reached; a failing-journal fixture would settle both.
status: open

### DW-66: A Node enrolled after a restore's recovery point and enrolled again afterwards starts its downlink counter at 0 under the same ack/v1 key.
origin: spec-deferred 454ee2a3ef0e
location: apps/cs/migrations/ReadingsMaintenance.cs (AdvanceReplayAsync)
source_spec: `spec-4-5-server-ingestion-and-acknowledgements.md`
severity: medium
reason: advance-replay moves the device_replay rows it finds and inserts rows for Devices with a device.enrolled event in the restored journal. A Device whose enrolment was lost with the restore has neither, so after re-enrolment (same K_dev, same Device ID) DeviceIngestionStore reserves counters from 0 again, repeating nonces the Server used before the restore.
status: open

### DW-67: The integration test host runs close to PostgreSQL's 100-connection limit.
origin: spec-deferred bf435ce74d75
location: aspire/Coldframe.AppHost/AppHost.cs
source_spec: `spec-4-5-server-ingestion-and-acknowledgements.md`
severity: low
reason: Before the ingestion suites were put in a serial xUnit collection, full runs failed twice with "too many clients"; the sampled peak is now 87 to 88 of 100. CommitOrderTests alone opens 34 connections. Raising max_connections on the AppHost's PostgreSQL would remove the risk.
status: open

### DW-68: A Node that hears a probe reply on a channel next to the Hub's may store the wrong channel and keep choosing it at every re-scan.
origin: spec-deferred 67da475712cb
location: packages/rs/transport/src/transport.rs (find_hub)
source_spec: `spec-4-4-esp-now-transport-from-node-to-hub.md`
reason: find_hub stores the channel it probed on, not the Hub's own channel, and scans ascending. Whether an ESP32-S3 answers a probe heard one channel off is not known; a bench run at close range with a sniffer, or carrying the Hub's channel in ProbeReply, would settle it.
status: open

### DW-69: Hub main-task stack headroom fell to about 34 KiB with the relay state and ingest buffers and is not measured on hardware.
origin: spec-deferred cd82032990b2
location: apps/rs/hub/src/main.rs, packages/rs/uplink/src/uplink.rs (ingest)
source_spec: `spec-4-4-esp-now-transport-from-node-to-hub.md`
reason: The ingest request and response buffers and the batch copy live in the main task; only Part I of the transport checklist watches for a reset during ingest. A stack high-water log on a real board would settle it.
status: open

### DW-70: Devices has no pull-to-refresh, refetch on focus or stale mode; a Hub whose heartbeat stops keeps reading "Online" until the user re-enters the tab or reloads the page.
origin: spec-deferred story-4-7-1
location: apps/ts/web/src/routes/(app)/devices/+page.server.ts, apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/ui/shell/AppShell.kt, apps/swift/ios/Sources/ColdframeIOS/UI/Screens.swift
source_spec: `spec-4-7-lot-status-and-the-site-overview.md`
severity: low
reason: Carried over from DW-28. Story 4.7 builds the refresh primitives and stale mode for the Site overview only and forbids stale mode on Devices and Site settings (UX-DR79 names Devices, Lot detail and Alerts too). Reuse the overview's primitives when those surfaces get theirs (Story 4.8 for Devices and Lot detail).
status: open

### DW-72: Lot detail has no Threshold band, below-low bars, admin strip (Thresholds, Calibrate, Pause/Resume) or Sensor-cell tap target; the hero's percentage and low Threshold are fixture-only.
origin: spec-deferred story-4-8-1
location: apps/ts/web/src/routes/(app)/garden/[lotId]/, apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/ui/sites/LotDetailScreen.kt, apps/swift/ios/Sources/ColdframeIOS/UI/LotDetailViews.swift
source_spec: `spec-4-8-lot-detail-with-history-and-device-status.md`
severity: low
reason: No Thresholds exist before Epic 5, no Calibrate, Pause or Resume destination before Epics 5 and 8, so Story 4.8 draws no strip, no band and no below-low bars (every bar is normal-style). `moisturePercent` and `lowThresholdPercent` are never sent by the Server before Epics 5 and 6; the hero's `~N %` path is exercised only by fixtures. Epics 5 and 8 add the strip and the band.
status: open

### DW-73: Devices (all clients) and Lot detail on mobile have no refetch on focus; the Hub-silent hero text has no Hub ID.
origin: spec-deferred story-4-8-2
location: apps/ts/web/src/routes/(app)/devices/+page.server.ts, apps/ts/web/src/lib/lot-detail.ts, packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/lots/LotDetail.kt
source_spec: `spec-4-8-lot-detail-with-history-and-device-status.md`
severity: low
reason: Carried from DW-70: Devices still has no stale mode or pull-to-refresh (the Nodes section inherits that). The `Lot` contract carries no Hub ID, so "Hub ‹id› is silent" falls back to the first Hub of the Devices list (web) or "The Hub is silent" (mobile); nothing produces a hub-silent Lot before Epic 7, which should add the Hub ID to `Lot` or `unknownCause`.
status: open

### DW-74: The Lot detail iOS views and App target are compiled only on macOS CI, and the web chart's 30 days end on the browser's clock day.
origin: spec-deferred story-4-8-3
location: apps/swift/ios/Sources/ColdframeIOS/UI/LotDetailViews.swift, apps/swift/ios/App/CoreLotDetailService.swift, apps/ts/web/src/lib/lot-detail.ts
source_spec: `spec-4-8-lot-detail-with-history-and-device-status.md`
severity: medium
reason: Same limit as DW-71: the SwiftUI views and the `Int32`/`KotlinInt` mapping in CoreLotDetailService are unproven until the macOS `swift` and `ios` jobs run, and iOS render tests assert only that an image renders. On the web the chart's day axis uses the browser clock while the Server's days are UTC, so around midnight the last bar can differ by one day from the Server's.
status: open

### DW-75: The Server exposes no way for a client to learn a stored Reading's reading_seq, which the Calibration endpoint requires.
origin: spec-deferred 0249033db860
location: packages/openapi/coldframe.openapi.json
source_spec: `spec-5-1-calibration-on-the-server.md`
severity: medium
reason: No OpenAPI surface lists Readings with reading_seq; the new tests read it from the readings table. Story 5.2 ("pick a recent stored Reading from the list") must add it.
status: open

### DW-76: SensorReadings.FindRawValueAsync looks up readings by sensor_id and reading_seq without a measured_at bound, so it visits every monthly partition.
origin: spec-deferred 967503731841
location: apps/cs/server/Sensors/SensorReadings.cs:13
source_spec: `spec-5-1-calibration-on-the-server.md`
severity: low
reason: readings is partitioned by measured_at and only indexed on (sensor_id, measured_at); the lookup is rare (admin calibration) but grows with history. An index on (sensor_id, reading_seq) or a bounded time window would fix it.
status: open

### DW-77: The Add-a-Node outcome offers Calibrate before the Node has reported, so the flow opens on "Node has not sent a Reading".
origin: code-review 58
location: packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/setup/NodeSetupEngine.kt
source_spec: `spec-5-2-calibrate-from-the-app.md`
severity: low
reason: The spec names the Node-added outcome as an entry point, but a new Node has stored no Readings, so the Lot has no calibratable Sensor id to open Calibrate on. The failure already says what to do and offers Try again. A waiting state that polls the Lot until the Sensor appears, or hiding the action until the Lot has one, would remove the dead end.
status: open

### DW-78: Lot Sensor responses carry sensorId and calibratable for every role, including Members, who cannot use the Calibration endpoint.
origin: code-review 58
location: apps/cs/server/Edge/EdgeApi.cs
source_spec: `spec-5-2-calibrate-from-the-app.md`
severity: low
reason: GET /lots/{id} is a Member read, so a read-only Member now receives Sensor ids that key the Administrator-only calibration endpoint. Nothing is writable with them, but the ids were not exposed to that role before; return them from Administrator up only if that matters.
status: open

### DW-79: CalibrateEngine.open and reset can race with the init collector that resets on site state changes.
origin: code-review 58
location: packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/calibrate/CalibrateEngine.kt
source_spec: `spec-5-2-calibrate-from-the-app.md`
severity: low
reason: If the engine is built lazily and open runs before the collector's first emission, that emission resets to Idle and cancels the load. No occurrence is known (the engine is built with the Sites core), only the ordering.
status: open

### DW-80: SensorReadings.RecentAsync reads reading_seq as decimal and casts to ulong, while the contract and the Kotlin and TypeScript clients use signed 64-bit.
origin: code-review 58
location: apps/cs/server/Sensors/SensorReadings.cs
source_spec: `spec-5-2-calibrate-from-the-app.md`
severity: low
reason: A reading_seq above long.MaxValue would wrap in Kotlin and lose precision past 2^53 in TypeScript. Not reachable in practice; validate or narrow the type if the sequence is ever widened.
status: open

### DW-81: The Lot list asks the Sensor grain for the low Threshold once per Lot that shows a percentage, and its read model runs a second newest-Reading subquery.
origin: code-review 65
location: apps/cs/server/Edge/EdgeApi.cs, apps/cs/server/Lots/LotsReadModel.cs
source_spec: `spec-5-4-set-thresholds-in-the-app-and-see-them-on-the-chart.md`
severity: low
reason: Every list refresh costs one concurrent grain call per such Lot, and the apps reload the list on foreground. The low Threshold changes rarely and could come from a projection column, as the rest of the Lot row does; the two newest-Reading lookups per row could share one lateral subquery. Fine for a home garden; revisit if Sites grow.
status: open

### DW-82: The Thresholds modal's Try again after a failed open loses the Sensor cell it was opened from, and Add high can start at or above the low.
origin: code-review 65
location: packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/thresholds/ThresholdsEngine.kt
source_spec: `spec-5-4-set-thresholds-in-the-app-and-see-them-on-the-chart.md`
severity: low
reason: retry() reopens with the Lot only, so the focus on the Sensor column is dropped; with a soil low at 98 or 100, addHigh snaps to 100 and Save shows "Low must stay below high" before the person has typed. Cosmetic: nothing is saved wrongly.
status: open
