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
