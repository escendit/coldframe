---
title: 'Story 1.5: Sign in on iOS and Android'
type: 'feature'
created: '2026-09-28'
status: 'awaiting-operator'
baseline_revision: 'b614cd95041a2b2bb0b0c4dc14cb85f44da61db5'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/EXPERIENCE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/DESIGN.md'
warnings:
  - oversized
deferred:
  - summary: >-
      The iOS certificate classifier (CertificateErrors.ios.kt, NSURLErrorDomain -1200...-1206) has no test, and it is unverified whether a DarwinHttpRequestException stays in the cause chain after the library wraps it.
    evidence: |-
      Unverified (maybe-false). Only the JVM classifier is tested; iOS tests cannot run on Linux and the ios CI job only runs xcodebuild build. Settle it with an iosTest mirroring CertificateErrorsTest run via ./gradlew :core:iosSimulatorArm64Test on macOS, and a real untrusted-certificate sign-in on an iPhone.
    location: >-
      packages/kt/core/src/iosMain/kotlin/com/escendit/coldframe/core/signin/CertificateErrors.ios.kt
    severity: medium (unverified)
  - summary: >-
      If the browser flow never returns (activity destroyed mid-flow, lost ASWebAuthenticationSession callback), the engine may stay in Working with SIGN IN disabled and signOut waiting on the mutex.
    evidence: |-
      Unverified (maybe-false). Depends on whether kotlin-multiplatform-oidc's flows always resume or throw when the hosting activity/session goes away; process death is covered by canContinueLogin. Settle it by rotating/backgrounding the app during the Custom Tab on a device and by reading PlatformCodeAuthFlow's suspension handling.
    location: >-
      packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/signin/SignInEngine.kt
    severity: medium (unverified)
  - summary: >-
      The iOS UX-DR104 and UX-DR126 render tests only assert that ImageRenderer produces an image; they do not check the announcement text/priority or clipping.
    evidence: |-
      RenderTests.swift asserts renders(...) == true only; deleting .onAppear { announce() } in Components.swift breaks no test. Needs a macOS-only seam (e.g. a tested builder for the announcement AttributedString).
    location: >-
      tests/swift/ios/ColdframeIOSTests/RenderTests.swift, apps/swift/ios/Sources/ColdframeIOS/UI/Components.swift
    severity: low
operator_actions:
  - "On a Mac with Xcode 26.6, run `swift build && swift test && swift format lint --strict -r .` at the repository root and fix any compile or test failure in the never-compiled SwiftUI code under apps/swift/ios/Sources/ColdframeIOS/UI."
  - "On that Mac, install XcodeGen 2.46.0, run `xcodegen generate` in apps/swift/ios, then `xcodebuild build -scheme Coldframe -destination 'generic/platform=iOS Simulator' CODE_SIGNING_ALLOWED=NO`, and confirm ColdframeCore links into the app."
  - "Push the branch and confirm the GitHub Actions kotlin (with the Android SDK step), swift and new ios jobs pass."
  - "Register the public client coldframe-mobile (standard flow, PKCE S256, redirect com.escendit.coldframe:/signin/callback, post-logout com.escendit.coldframe:/signout/callback) in the coldframe realm of your TLS-served Keycloak, and delete or re-import the local Aspire realm so it picks up the new client."
  - "Build the Android app with -Pcoldframe.serverUrl and -Pcoldframe.keycloakIssuer pointing at your Server and Keycloak, install it on a phone, and verify: sign-in through Custom Tabs lands on the Garden tab, cancel returns silently, Server off shows the unreachable notice, and Sign out returns to Sign in."
  - "Set the Server URL and issuer in apps/swift/ios/Config/Coldframe.xcconfig, run the iOS app on an iPhone, and verify the same sign-in, cancel, notice and sign-out behaviour through ASWebAuthenticationSession."
  - "On both phones, at the largest system text size with TalkBack or VoiceOver on, check that the Sign-in, tab shell, Settings and Appearance screens clip nothing and that each control is read with its role and state."
---

<intent-contract>

## Intent

**Problem:** There are no mobile apps. Simon cannot sign in on his phone, and every later mobile story (1.8, 1.9, BLE setup) needs the shared Kotlin core's sign-in and session, the native tab shell, the theme and the brand components this story sets up (FR5, FR19, AD-14, AD-23, epics.md lines 628–663).

**Approach:** Give the KMP core (`packages/kt/core`) Android and iOS targets and a platform-neutral sign-in engine — probes, failure classifier, session state machine, token store and refresh — on top of `kotlin-multiplatform-oidc` 0.18.3 (spine pin), with the network mocked in `tests/kt/core`. Add a Jetpack Compose app `apps/kt/android` and a SwiftUI app `apps/swift/ios` that only render the core's state: Sign-in surface, native tab shell Garden · Alerts · Devices · Settings, Settings → Appearance (System/Light/Dark from the generated tokens), with every string in platform catalogues. Add a public `coldframe-mobile` client to the local realm.

## Boundaries & Constraints

**Always:**
- The core alone runs OIDC Authorization Code + PKCE (S256) through `kotlin-multiplatform-oidc` (`AndroidCodeAuthFlowFactory` → Custom Tabs, `IosCodeAuthFlowFactory` → `ASWebAuthenticationSession`, not ephemeral). Tokens live only in the library's token store (`AndroidSettingsTokenStore`, `IosKeychainTokenStore`); shells never see or log a token string.
- The core exposes one observable state per concern, e.g. `SignInState` = `SignedOut(notice: Notice?)` | `Working` | `SignedIn(displayName)` and `Notice` = `Unreachable` | `Certificate` | `Keycloak` | `SignedOut`; each notice carries its action (`TryAgain`, `SignIn` or none). Shells map state to UI; no branching on errors, URLs or tokens in Swift or Compose code.
- SIGN IN mirrors the web (`apps/ts/web/src/lib/server/probe.ts`): GET `{serverUrl}/.well-known/healthz` (any HTTP response = reachable), then `{issuer}/.well-known/openid-configuration` (non-2xx or no `authorization_endpoint` = Keycloak), each bounded to 5 s, then the auth flow. Failure mapping: a TLS/certificate failure anywhere → `Certificate` (no action, never retried insecurely); Server probe no response → `Unreachable` (Try again); issuer probe failure, discovery failure, `OpenIdConnectException` other than cancel, failed code exchange → `Keycloak` (Try again), except a failed exchange re-probes the issuer first so a certificate failure still shows `Certificate`; `OpenIdConnectException.AuthenticationCancelled` → `SignedOut(null)`, no notice.
- Session: on start and on return to foreground, stored tokens with a valid access token → `SignedIn`; an expired access token is refreshed through `TokenRefreshHandler`; a rejected refresh (token endpoint 400/401, e.g. `invalid_grant`) clears the store and yields `SignedOut(SignedOut)` ("You're signed out. Sign in again to see live data." + Sign in); a transient refresh failure (no response, 5xx) keeps `SignedIn`. A deliberate sign-out (Settings → Account → Sign out, confirmed in a native dialog per UX-DR113) clears the store first, calls end-session best effort, and yields `SignedOut(null)`.
- Build-time config only (AD-23): Server URL, issuer and client id (default `coldframe-mobile`) come from Gradle properties `coldframe.serverUrl`, `coldframe.keycloakIssuer`, `coldframe.keycloakClientId` → `BuildConfig` on Android, and from `apps/swift/ios/Config/Coldframe.xcconfig` → Info.plist on iOS. Unset values default to `https://server.coldframe.invalid` and `https://keycloak.coldframe.invalid/realms/coldframe` (never resolvable, so an unconfigured build shows the Unreachable notice). Redirect URI `com.escendit.coldframe:/signin/callback`, post-logout `com.escendit.coldframe:/signout/callback`.
- TLS: release builds trust system roots only and allow no cleartext (Android `network_security_config.xml` with `<certificates src="system"/>`, iOS default ATS). Only the Android debug build type permits cleartext to `10.0.2.2` and `localhost`, and only the iOS Debug configuration sets `NSAllowsLocalNetworking`, for the local Aspire Keycloak.
- Theme: `ThemePreference` System (default) / Light / Dark persisted per device by the core (`multiplatform-settings`), applied immediately with no Save; System follows the OS appearance; colours, type, spacing and icons come only from the generated tokens (`packages/kt/design-tokens`, `packages/swift/design-tokens`); square corners, 1 px borders, no shadows, no animations or transitions (UX-DR101), signature radial gradient only behind the Sign-in card, 44 pt / 48 dp minimum targets, buttons and segments grow and wrap instead of clipping.
- Strings: every user-visible string, content description and accessibility label comes from `apps/kt/android/src/main/res/values/strings.xml` (Android) or `apps/swift/ios/Sources/ColdframeIOS/Resources/Localizable.xcstrings` (iOS); both carry the same keys and English values; notice copy is verbatim from EXPERIENCE.md lines 159–163 (as in the web `en.json`); uppercase only by style; plurals via Android `<plurals>` / catalogue plural variations; "browser" copy becomes "phone" ("Applies at once and only on this phone.", "Sign out of Coldframe on this phone?").
- Test-first; each UX requirement test's name starts with its id (Kotlin backtick name `` `UX-DR56 …` `` in JVM/Android tests, `uxDr56…` in common tests; Swift `@Test("UX-DR56 …")`). Kotlin tests live under `tests/kt/`, Swift under `tests/swift/`. Exact version pins in `gradle/libs.versions.toml`.

**Never:**
- No Server address field, no "continue anyway", no custom trust manager or certificate bypass, no WebView sign-in, no tokens in logs, shells or `SharedPreferences`/`UserDefaults` outside the library's store, no Role from token claims.
- No Create Site, Site switcher, Garden empty state, Alerts count data, API client calls, push registration or BLE — later stories. Tabs show their heading only; Settings shows Appearance and Account → Sign out only.
- No toasts, snackbars that auto-dismiss, spinners, long-press actions, hover-only affordances, carousels or celebratory motion (UX-DR114).
- No edits to DESIGN.md, EXPERIENCE.md, epics.md, the web app's copy, or `sprint-status.yaml`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Cold start signed out | empty token store | Sign-in surface: gradient, card, mark, SIGN IN only | No error expected |
| Sign in | probes OK, auth flow returns code, exchange OK | `SignedIn`; tab shell on Garden; tokens in store | No error expected |
| Working | SIGN IN pressed | button shows "Signing in…" in place, disabled against double tap | second press ignored |
| Server unreachable | healthz probe: connect refused / DNS / timeout | Unreachable notice + Try again | no auth flow started |
| Untrusted certificate | TLS failure on either probe or on exchange (re-probe) | Certificate notice, no action | never retried insecurely |
| Keycloak error | issuer non-2xx, discovery fails, non-cancel OIDC exception | Keycloak notice + Try again | discovery retried next press |
| Cancel | `AuthenticationCancelled` | Sign-in, no notice | none |
| Restart signed in | valid access token stored | `SignedIn` without a browser | none |
| Refresh OK | expired access token, refresh 200 | `SignedIn`, new tokens saved | none |
| Refresh rejected | refresh 400 `invalid_grant` / 401 | store cleared, signed-out notice + Sign in | none |
| Refresh transient | refresh no response / 5xx | stays `SignedIn` | retried next foreground |
| Sign out | Settings → Sign out → confirm | store cleared, Sign-in, no notice | end-session failure ignored |
| Theme | choose Dark, restart | dark tokens immediately and after restart; System follows OS | unknown stored value → System |

</intent-contract>

## Code Map

- `gradle/libs.versions.toml` -- only kotlin 2.4.20 and ktlint pins today. Add (all verified stable on 2026-09-28): AGP `9.3.3` (same minor line as KGP 2.4.20's highest tested AGP 9.3.1; 9.4.1 is outside the tested range) for `com.android.application` and `com.android.kotlin.multiplatform.library`; `org.jetbrains.kotlin.plugin.compose` 2.4.20; `kotlin-multiplatform-oidc` 0.18.3 (`io.github.kalinjul.kotlin.multiplatform:oidc-appsupport`, `oidc-tokenstore`; built with Kotlin 2.3.0 / Ktor 3.3.3 / settings 1.3.0 / compileSdk 36 / minSdk 21); Ktor 3.6.0 (`ktor-client-core`, `-okhttp`, `-darwin`, `-mock`); `kotlinx-coroutines` 1.11.0 (`-core`, `-test`); `com.russhwolf:multiplatform-settings` + `-test` 1.3.0; Compose BOM `2026.09.00` (material3 1.4.0, ui/ui-test-junit4/ui-test-manifest 1.12.1); `androidx.activity:activity-compose` 1.13.0; `androidx.lifecycle:lifecycle-viewmodel-compose` / `lifecycle-runtime-compose` 2.11.0; `androidx.browser:browser` 1.10.0; Robolectric 4.17; `androidx.test.ext:junit` 1.3.0. compileSdk/targetSdk 36, minSdk 29.
- `settings.gradle.kts` -- add `google()` to `pluginManagement` and `dependencyResolutionManagement` repositories; include `:android` at `apps/kt/android`. `build.gradle.kts` (root) -- declare the new plugins `apply false`.
- `packages/kt/core/build.gradle.kts` -- `jvm()` only, `explicitApi()`, `jvmToolchain(25)`, commonTest pointed at `tests/kt/core/commonTest`. Add `com.android.kotlin.multiplatform.library` (`kotlin { android { namespace = "com.escendit.coldframe.core"; compileSdk = 36; minSdk = 29 } }` — AGP 9 DSL; `androidLibrary {}` is deprecated), `iosArm64()`, `iosSimulatorArm64()` with a static framework `ColdframeCore` and `XCFramework("ColdframeCore")`. Keep `jvm()` so `commonTest` runs on the JVM under `check`. Share the JVM certificate classifier between `jvmMain` and `androidMain` through an intermediate `jvmShared` source set. Use `jvmTarget` 17 for the Android compilation. On Linux, iOS klibs cross-compile (`kotlin.native.enableKlibsCrossCompilation` defaults to true in 2.4.20); linking and iOS tests are skipped, not failed.
- `packages/kt/design-tokens/build.gradle.kts` -- add the same Android library target (namespace `com.escendit.coldframe.designtokens`) so the app can depend on it; iOS uses the Swift tokens package, so no iOS target here.
- `packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/Module.kt` -- skeleton to keep; new code goes beside it in `…/core/signin/` and `…/core/appearance/`.
- `kotlin-multiplatform-oidc` 0.18.3 API (read the jars' sources before wiring): `OpenIdConnectClient(discoveryUri) { clientId; scope = "openid profile"; codeChallengeMethod = CodeChallengeMethod.S256; redirectUri; postLogoutRedirectUri }` (check for an overload taking an `HttpClient` for tests); `discover()`, `refreshToken(rt)`, `endSession(idToken)`; `CodeAuthFlowFactory.createAuthFlow(client)` → `getAccessToken()`; `canContinueLogin()`/`continueLogin()` for process death on Android; `AndroidCodeAuthFlowFactory(useWebView = false).registerActivity(activity)` in `onCreate`; manifest placeholder `oidcRedirectScheme = "com.escendit.coldframe"`; `TokenStore.saveTokens/removeTokens/getAccessToken…`; `TokenRefreshHandler(store).refreshAndSaveToken(client, old)`; sealed `OpenIdConnectException` (`AuthenticationCancelled`, `UnsuccessfulTokenRequest`, `DiscoveryFailure`, `TechnicalFailure`, …). Wrap the library behind core interfaces (`AuthFlow`, `TokenVault`) so commonTest can drive fakes while a real `OpenIdConnectClient` over Ktor `MockEngine` covers discovery, exchange and refresh.
- Certificate detection -- JVM/Android: walk `cause` for `javax.net.ssl.SSLHandshakeException`, `SSLPeerUnverifiedException`, `java.security.cert.CertificateException`, `CertPathValidatorException`; iOS: `DarwinHttpRequestException.origin` (NSError) in `NSURLErrorDomain` with codes −1200…−1206 (secure connection failed, cert has bad date, untrusted, unknown root, not yet valid, rejected, required).
- `packages/kt/design-tokens/generated/kotlin/…` -- `ColorTokens` (`ThemedColor(light, dark)` in `0xAARRGGBB`, `.resolve(isDark)`), `Typography` (14 `TypeRole`s with `sizeSp`, `fontWeight`, `lineHeight`, `uppercase`, `maxFontScale`), `Spacing` (`BUTTON_HEIGHT` 48, `GUTTER_MOBILE` 16, `FOCUS_RING` 2), `Radius` (0), `CarbonIcon` (`grid`, `notification`, `box`, `settings`, `checkmark`, `error--filled`, `view`) + `IconPath.parse` (32×32 M/L/C/Z) → build Compose `ImageVector`s from these. Fonts: Ubuntu family TTFs in `packages/design-tokens/fonts/` (copy into `res/font` via a Gradle copy task or reference; DW-9: Ubuntu Condensed only weight 400).
- `packages/swift/design-tokens/Sources/ColdframeDesignTokens/` -- `ColorTokens` (`ThemedColor` `0xRRGGBBAA`, `components(of:)`), `Typography` (`TypeRole.textStyle`, `maximumPointSize`), `Spacing`, `Radius`, `CarbonIcon`, `CarbonIconShape` (SwiftUI only).
- `Package.swift` -- `ColdframeIOS` target at `apps/swift/ios/Sources/ColdframeIOS` (Foundation-only `Module.swift` today); add dependency on `ColdframeDesignTokens` and the catalogue resource. `Localizable.xcstrings` processing is not available on Linux: declare `resources: [.process("Resources")]` only when the manifest is not compiled on Linux (`#if os(Linux)` → `exclude: ["Resources"]`). SwiftUI code sits under `#if canImport(SwiftUI)` as in `CarbonIconShape.swift`. Existing tests use Swift Testing (`import Testing`, `@Test`).
- `apps/ts/web/src/lib/i18n/en.json` lines 4–38 -- reuse keys and copy (`signin.*`, `notice.*`, `nav.*`, `settings.*`, `appearance.*`, `theme.*`, `modal.cancel`, `count.openAlerts`), adapting only "browser" copy. `apps/ts/web/src/lib/server/probe.ts` + `failures.ts` -- the probe order and classification the core mirrors.
- `tests/ts/web/coverage.test.ts` -- pattern for the UX-DR coverage test (hard-coded id list, regex over test names).
- `aspire/keycloak/realms/coldframe-realm.json` (one client `coldframe-web`) and `tests/cs/server.integration/KeycloakTests.cs` (`ColdframeRealmHasTheConfidentialWebClient`, line 138, pattern: admin client, GET `/admin/realms/coldframe/clients?clientId=`, field asserts). The realm is imported only when absent (AppHost.cs:54).
- `.gitignore` line 21 `[Dd]ebug/` ignores `apps/kt/android/src/debug/` -- add a `!apps/kt/android/src/debug/` negation (and `!…/src/debug/**`); add `local.properties` and `apps/swift/ios/Coldframe.xcodeproj/`.
- `.github/workflows/ci.yml` lines 82–103 (`kotlin`, ubuntu-24.04, Java 25, `./gradlew check`) and 158–180 (`swift`, macos-26, Xcode 26.6, `swift build/test/format lint`).
- Local tooling: JDK 25, Gradle 9.8.0 wrapper, podman + `swift:6.3.3` image (Linux Swift, no SwiftUI), no Android SDK and no Xcode. Install the SDK for verification: `commandlinetools-linux-16111833_latest.zip` into `$HOME/Android/Sdk/cmdline-tools/latest`, then `sdkmanager "platforms;android-36" "build-tools;36.0.0"` and accept licenses; point `local.properties` `sdk.dir` at it (~300 MB). Kotlin/Native downloads its toolchain to `~/.konan`.

## Tasks & Acceptance

**Execution:**
- `tests/kt/core/commonTest/kotlin/com/escendit/coldframe/core/signin/` -- write failing tests first (kotlinx-coroutines-test, Ktor `MockEngine`, `MapSettings`): probe order and 5 s bound; every I/O matrix row through `SignInEngine` with a fake `AuthFlow`/`TokenVault`; a real `OpenIdConnectClient` over `MockEngine` for discovery, code exchange, refresh 200, refresh 400 `invalid_grant`, refresh 503; process-death `continueLogin`; sign-out order (store cleared before end-session; end-session failure ignored); config defaults; `ThemePreference` persistence and unknown value → System. `tests/kt/core/jvmTest/…` -- certificate classifier over nested `SSLHandshakeException`/`CertPathValidatorException` causes. -- Proves AC "unit tests cover OIDC, refresh and session-expired with the network mocked".
- `packages/kt/core/build.gradle.kts`, `packages/kt/core/src/{commonMain,jvmShared,jvmMain,androidMain,iosMain}/kotlin/com/escendit/coldframe/core/…` -- targets and dependencies; `signin/` (`CoreConfig`, `Probe`, `Failure`/`Notice`/`NoticeAction`, `CertificateErrors` expect/actual, `AuthFlow`, `TokenVault`, `SignInEngine` exposing `StateFlow<SignInState>` plus `signIn()`, `resume()` (start/foreground), `signOut()`), platform factories (`AndroidSignIn.create(context, activity registration)`, `IosSignIn.create()` with a Swift-friendly callback/`StateFlow` watcher since Swift cannot collect flows directly), `appearance/` (`ThemePreference`, `AppearanceStore`). -- The engine AD-14 requires.
- `packages/kt/design-tokens/build.gradle.kts`, `gradle/libs.versions.toml`, `settings.gradle.kts`, `build.gradle.kts`, `gradle.properties` (`android.useAndroidX=true`) -- build wiring.
- `apps/kt/android/` (Gradle `:android`, `com.android.application` + compose plugin, `applicationId`/`namespace` `com.escendit.coldframe`, test sources pointed at `tests/kt/android/test/kotlin`) -- `build.gradle.kts` (BuildConfig fields from Gradle properties, `oidcRedirectScheme` placeholder, `testOptions.unitTests.isIncludeAndroidResources = true`), `src/main/AndroidManifest.xml`, `res/xml/network_security_config.xml`, `src/debug/res/xml/network_security_config.xml`, `res/values/strings.xml` (+ `plurals`), `res/font/` Ubuntu, `MainActivity` (registers the auth flow factory, collects state), `ui/theme/` (`ColdframeTheme` from `ColorTokens`/`Typography`/`Radius`, `ColdframeIcons` from `CarbonIcon` path data), `ui/components/` (`PrimaryButton` with working label, `TextInput`, `SegmentedChoice`, `InlineNotice` with `liveRegion = Polite`/`Assertive`, `ThemeSwitcher`), `ui/signin/SignInScreen`, `ui/shell/AppShell` (M3 `NavigationBar`: grid, notification, box, settings; `TopAppBar` headings), `ui/settings/SettingsScreen` (Appearance row, Account → Sign out with M3 `AlertDialog` naming the result), `ui/settings/AppearanceScreen`. -- Compose shell, UI only (UX-DR57, 110).
- `tests/kt/android/test/kotlin/com/escendit/coldframe/android/` -- Robolectric + `createComposeRule` tests written first: Sign-in surface has one button and no text field (AD-23); SIGN IN shows "Signing in…" then each notice with the right action or none; nav labels and selected state; theme switch recolours immediately and persists across recreation; 2.0 font scale (`LocalDensity` with `fontScale = 2f`) on Sign-in, shell and Appearance with no text node whose `TextLayoutResult` overflows; every clickable has `Role.Button`/`Role.Tab`/selected semantics and a content description from resources; ≥ 48 dp touch targets; no `animate*`/`AnimatedVisibility`/`Crossfade` usage (source scan); `strings.xml` voice and glossary rules (no "!", emoji, "successfully", "OK", "fine", "all good"; glossary capitalised), plurals resolve for 1 and 5, no literal strings in `apps/kt/android/src/main/kotlin` composables (source scan for `Text("`/`contentDescription = "`); `strings.xml` and `Localizable.xcstrings` have identical keys and values; UX-DR coverage test (below).
- `apps/swift/ios/Sources/ColdframeIOS/` -- `SignInPresentation` (Swift mirror of the core state: notice key, action key or none, working flag), `SignInService` protocol (the SwiftUI side's only view of the core, so `swift test` needs no Kotlin framework), `ThemePreference` + `ColorScheme` mapping, `L10n` keys; under `#if canImport(SwiftUI)`: `ColdframeTheme` (tokens → `Color`/`Font` with `relativeTo:` Dynamic Type and `maximumPointSize`), `PrimaryButton`, `TextInputField`, `SegmentedChoice`, `InlineNotice` (posts `AccessibilityNotification.Announcement`), `ThemeSwitcher`, `SignInView`, `AppTabView` (`TabView` + `NavigationStack`, Carbon icons), `SettingsView` (native `confirmationDialog` for Sign out), `AppearanceView`; `Resources/Localizable.xcstrings`. -- SwiftUI shell, UI only (UX-DR109).
- `apps/swift/ios/App/` + `apps/swift/ios/project.yml` (XcodeGen) + `apps/swift/ios/Config/Coldframe.xcconfig` + `Info.plist` -- `ColdframeApp` (`@main`), `CoreSignInService` adapting `ColdframeCore`'s `IosSignIn` to `SignInService`; app target depends on the local package products `ColdframeIOS` and `ColdframeDesignTokens`; a pre-build script runs `./gradlew :core:embedAndSignAppleFrameworkForXcode`; bundle id `com.escendit.coldframe`; iOS 17.
- `tests/swift/ios/ColdframeIOSTests/` -- Swift Testing tests written first: presentation mapping for every notice, action and working state; theme preference mapping; catalogue has every `L10n` key (macOS only); SwiftUI render smoke tests via `ImageRenderer` at `.accessibility5` Dynamic Type for Sign-in, shell and Appearance (macOS only, `#if canImport(SwiftUI)`); accessibility traits (`.isButton`, `.isSelected`) set by `SegmentedChoice`/tab items through their presentation models.
- `aspire/keycloak/realms/coldframe-realm.json`, `tests/cs/server.integration/KeycloakTests.cs` -- add public client `coldframe-mobile` (standard flow only, PKCE S256, redirect `com.escendit.coldframe:/signin/callback`, post-logout `com.escendit.coldframe:/signout/callback`, no secret) and `ColdframeRealmHasThePublicMobileClient`.
- `.github/workflows/ci.yml` -- `kotlin` job: a step running the runner's preinstalled `$ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager "platforms;android-36" "build-tools;36.0.0"` before `./gradlew check`; new `ios` job on macos-26 (Java 25, Xcode 26.6, XcodeGen pinned via Homebrew, `xcodegen generate`, `xcodebuild build -scheme Coldframe -destination 'generic/platform=iOS Simulator' CODE_SIGNING_ALLOWED=NO`).
- `.gitignore`, `docs/quickstart.md` (prerequisites: Android SDK 36, Xcode 26 + XcodeGen; "Run the Android app" and "Run the iOS app" with the Gradle properties / xcconfig and the local realm; Kotlin and Swift test sections), `apps/kt/README.md`, `apps/swift/README.md`, `packages/kt/README.md` -- wiring and docs.

**Acceptance Criteria:**
- Given an Android build with `coldframe.serverUrl` and `coldframe.keycloakIssuer` set, when the app opens signed out, then the Robolectric test sees the Sign-in surface with exactly one button, SIGN IN, and no text field; the same holds for `SignInView` on iOS by construction (no input in the view) and an unconfigured build uses the `.invalid` defaults.
- Given SIGN IN and a fake auth flow that authenticates, when the engine completes, then state is `SignedIn`, tokens are in the vault, and the Android shell shows `NavigationBar` Garden · Alerts · Devices · Settings with Garden selected (UX-DR57, UX-DR110).
- Given the Server unreachable, the certificate untrusted, Keycloak failing, or cancel, when sign-in runs, then the matching UX-DR92 notice (or none for cancel) appears in the card with the specified action, in core tests and in the Android UI test.
- Given Settings → Appearance, when System, Light or Dark is chosen, then the Compose theme recolours from `ColorTokens` in the same frame and the choice survives activity recreation and a new `AppearanceStore` over the same settings.
- Given `tests/kt/core`, when `./gradlew check` runs, then the OIDC flow, token refresh (200, rejected, transient) and session-expired transitions pass with the network mocked.
- Given font scale 2.0, when the Sign-in, shell and Appearance screens render in Robolectric, then no text overflows and every control exposes its role and state (UX-DR96, UX-DR98); on iOS the `.accessibility5` render tests pass on macOS.
- Given the UX-DR ids 15, 34, 35, 36, 53, 56, 57, 59, 60, 71, 75, 76, 92, 93, 96, 98, 100, 101, 104, 109, 110, 113, 114, 124, 125, 126, 127, 130, 131, when the coverage test in `tests/kt/android` scans test names in `tests/kt/**` and `tests/swift/**`, then each id is named by at least one Kotlin test (109 excepted) and each of 34, 35, 36, 56, 71, 75, 76, 100, 101, 104, 109, 113, 114, 125, 126, 127, 130, 131 by at least one Swift test.
- Given the workspace, when `./gradlew check`, `./gradlew :core:compileKotlinIosSimulatorArm64`, `swift build && swift test && swift format lint --strict -r .` (Linux container) and `dotnet test --project tests/cs/server.integration` run, then all pass.

## Spec Change Log

## Review Triage Log

### 2026-09-28 — Review pass
- verdicts: 31 findings — high 0, medium 6, low 19, false 3, maybe-false 3
- findings:
  - `[low]` `[patch]` Blind: resume() can overwrite Working, re-enabling SIGN IN and queuing a second browser flow — runResume now writes through `showUnlessSigningIn`, which skips when Working; test presses SIGN IN while resume is held in vault.read().
  - `[medium]` `[patch]` Blind: store/flow exceptions escape the engine's coroutines (SupervisorJob with no handler → crash on Android, or stuck state) — read/canContinue failures on resume → SignedOut(null) with best-effort clear; save failure → Keycloak notice; signOut store failure → SignedOut(null); diagnose failure → Keycloak; 6 tests with a throwing vault/flow.
  - `[low]` `[patch]` Blind: no dataExtractionRules, so Android 12+ device transfer can copy the token store without its Keystore key — added `res/xml/data_extraction_rules.xml` excluding all domains, `fullBackupContent="false"`, `tools:replace` for the library's backup attributes.
  - `[low]` `[reject]` Blind: a failed end-session leaves the SSO cookie so the next SIGN IN is silent — only when the best-effort logout fails; fixing needs prompt=login state or a retry queue, more than a direct correction.
  - `[maybe-false]` `[defer]` Blind: iOS certificate classifier untested and cause-chain survival unverified — medium if true; settled by an iosTest on macOS and a real untrusted-certificate sign-in.
  - `[low]` `[reject]` Blind: build-time config is not validated, so a bad URL shows the unreachable notice — adopter misconfiguration only; validation adds build logic for a case the notice already surfaces.
  - `[low]` `[patch]` Blind: the OIDC client shares the 5 s probe timeout, so a slow code exchange shows the Keycloak notice — library client now uses its own 30 s `OIDC_TIMEOUT`.
  - `[low]` `[reject]` Blind: the library falls back to WebView when no Custom Tabs provider exists (confirmed in AndroidCodeAuthFlowFactory.createWebFlow bytecode) — only on devices with no Custom Tabs-capable browser; a guard needs a new UX notice; tracked as DW-15.
  - `[maybe-false]` `[reject]` Blind: release variant never configured or built — minify is off by default so no R8 breakage exists to show; release network config is the static system-only file; would be low at most.
  - `[low]` `[reject]` Blind: displayName is computed but never shown — no user-visible harm; the Account row in a later story is its consumer.
  - `[low]` `[reject]` Blind: Info.plist and Info-Debug.plist duplicate each other — developer-only drift risk; merging needs build-setting work that cannot be verified without Xcode.
  - `[low]` `[reject]` Blind: frontchannelLogout on the public client is meaningless and webOrigins is unasserted — harmless flag; no bad outcome.
  - `[medium]` `[patch]` Edge: vault.save throws after a successful exchange — grouped with the store-exception fix above.
  - `[medium]` `[patch]` Edge: canContinue()/vault.read() throw on start — grouped with the store-exception fix above.
  - `[medium]` `[patch]` Edge: vault.read()/clear() throw during sign-out — grouped with the store-exception fix above.
  - `[low]` `[patch]` Edge: SIGN IN during resume is overwritten — grouped with the resume race fix above.
  - `[maybe-false]` `[defer]` Edge: a browser flow that never returns leaves the engine stuck in Working — medium if true; settled by device testing and reading the library's flow suspension.
  - `[false]` `[reject]` Edge: no expires_in means tokens never refresh — Keycloak always returns expires_in for the coldframe realm's token endpoint.
  - `[false]` `[reject]` Edge: a blank name claim gives an empty display name — displayName is not rendered anywhere in this story.
  - `[low]` `[reject]` Edge: Swift duration formatting traps on NaN — no caller formats durations yet (same call as the web review).
  - `[low]` `[reject]` Edge: Kotlin duration day count overflows Int — no caller; needs Duration.INFINITE or >5.8 million years.
  - `[low]` `[reject]` Edge: SegmentedChoice throws in an unbounded-width parent — its only parent is the bounded Appearance column.
  - `[medium]` `[patch]` Verification gap: MainActivity's resume() on ON_START is untested — added `MainActivityTest` launching the real activity; it fails with the resume line removed.
  - `[medium]` `[patch]` Verification gap: end-session after a cold start (discover branch) never runs — added the cold-start sign-out case in `OidcFlowTest` asserting one logout with id_token_hint.
  - `[low]` `[patch]` Verification gap: emptied vault while SignedIn → signed-out notice unpinned — added to `SignInEngineTest`.
  - `[low]` `[patch]` Verification gap: the font-scale-2 working-label test never renders Working — `AccessibilityTest` now renders Working at 2.0 and checks no overflow.
  - `[low]` `[defer]` Verification gap: iOS announcement and render tests are render-only — needs a macOS-only seam; filed disposition defer.
  - `[low]` `[reject]` Verification gap (other): WebView fallback untested — same root as the Blind WebView finding; tracked as DW-15.
  - `[false]` `[reject]` Intent: spec frontmatter not finalized to awaiting-operator — the review ran before Finalize; the spec now carries `status: awaiting-operator` and `operator_actions`.
  - `[low]` `[reject]` Intent: deferred-work.md gained DW-13–DW-17 while spec `deferred` was empty — the ledger holds implementation deviations; the spec's list holds review defers, and the Auto Run Result lists both.
  - `[low]` `[reject]` Intent: device-level ACs (real browser round trip, screen readers, largest text on a phone, Xcode link) are verified only by fakes, Robolectric and macOS-only tests — only a person with a Mac and phones can do that; enumerated under `operator_actions` as the intent directs.

## Design Notes

- **What this machine can and cannot prove.** Linux verifies the core (JVM tests, iOS klib compile), the Android app (Robolectric), the Foundation-only Swift code (container) and the realm. SwiftUI rendering, linking `ColdframeCore.framework`, the Xcode project, VoiceOver, and a real sign-in on devices against a TLS Keycloak need a Mac or a phone. Those become `operator_actions` with `status: awaiting-operator`, not `blocked`.
- **Swift talks to a protocol, not to Kotlin.** `SignInService` keeps the SwiftPM target free of the Kotlin framework so `swift test` stays Xcode-free (as `apps/swift/README.md` promises); only `apps/swift/ios/App/CoreSignInService.swift` imports `ColdframeCore`.
- **iOS selected-tab cue.** DESIGN.md asks for a filled icon, but the Carbon set in the tokens has no filled variants; iOS uses the native `TabView` selected state and tint. Record this as a deferred UX item rather than inventing glyphs.
- **Settings index (UX-DR71).** Only Appearance and Account exist in 1.5; My notifications, Members and Site settings rows arrive with their stories, so the index shows the two rows in the specified order.

## Verification

**Commands:**
- `./gradlew check` (with `local.properties` `sdk.dir`) -- expected: exit 0; core commonTest/jvmTest, Android Robolectric tests, lint and ktlint pass
- `./gradlew :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64` -- expected: exit 0 on Linux
- `./gradlew :android:assembleDebug -Pcoldframe.serverUrl=https://server.example -Pcoldframe.keycloakIssuer=https://id.example/realms/coldframe` -- expected: APK produced
- `podman run --rm --security-opt label=disable -v "$PWD":/work -w /work swift:6.3.3 sh -c 'swift build && swift test && swift format lint --strict -r .'` -- expected: exit 0
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --project tests/cs/server.integration` -- expected: all pass including the mobile client test
- `pnpm -r test` -- expected: unchanged, exit 0

**Manual checks (if no CLI):**
- `git grep -nE "accessToken|refreshToken|idToken" apps/kt/android apps/swift/ios` shows no matches (shells never touch tokens).

## Auto Run Result

Status: awaiting-operator

**Summary.** The shared Kotlin core (`packages/kt/core`) now builds for JVM (tests), Android and iOS (static `ColdframeCore` framework and XCFramework). `SignInEngine` owns sign-in and session on top of `kotlin-multiplatform-oidc` 0.18.3: Server then issuer probes (5 s each), certificate/unreachable/Keycloak/cancel mapping to the UX-DR92 notices, token store, refresh (200 / rejected / transient), the UX-DR93 signed-out notice, sign-out, and continuation after process death. `AppearanceStore` persists System/Light/Dark per device. `apps/kt/android` is a Compose app (Sign-in surface, M3 NavigationBar Garden · Alerts · Devices · Settings, Settings → Appearance and Account → Sign out, brand components from the generated tokens, all strings in `strings.xml`); `apps/swift/ios` has Foundation-only presentation models, a String Catalog with identical keys and values, SwiftUI views, and an XcodeGen app target that alone imports `ColdframeCore`. The local realm has a public `coldframe-mobile` client. Everything Linux can check passes; the SwiftUI and Xcode parts and all on-device behaviour are owed by the operator (see `operator_actions`).

**Files changed.**
- `packages/kt/core/` -- Android/iOS targets; `signin/` (config, probes, failures/notices, certificate classifiers, auth flow, token vault, identity provider, engine, Swift snapshot/watcher, platform factories) and `appearance/`
- `packages/kt/design-tokens/build.gradle.kts` -- Android library target
- `apps/kt/android/` -- the Compose app: manifest, network security configs (release system-only, debug cleartext to 10.0.2.2/localhost), data extraction rules, strings, theme, icons, components, screens
- `apps/swift/ios/` -- `ColdframeIOS` presentation models, catalogue, SwiftUI UI; `App/` (`ColdframeApp`, `CoreSignInService`), `project.yml`, xcconfig, Info plists
- `tests/kt/core/`, `tests/kt/android/`, `tests/swift/ios/` -- core engine/OIDC/probe/config/appearance tests, Robolectric UI, accessibility, strings, source-scan, activity and UX-DR coverage tests; Swift presentation, catalogue, formats, source-rule and render tests
- `aspire/keycloak/realms/coldframe-realm.json`, `tests/cs/server.integration/KeycloakTests.cs` -- `coldframe-mobile` client and its test
- `.github/workflows/ci.yml` -- Android SDK step, iOS klib compile, new macOS `ios` job
- `gradle/libs.versions.toml`, `settings.gradle.kts`, `build.gradle.kts`, `gradle.properties`, `Package.swift`, `.gitignore`, `.editorconfig` -- wiring
- `docs/quickstart.md`, `apps/kt/README.md`, `apps/swift/README.md`, `packages/kt/README.md`, `_bmad-output/implementation-artifacts/deferred-work.md` (DW-13–DW-17) -- docs and ledger

**Deviations from the spec (from the implementer).**
- compileSdk is 37 (targetSdk 36): the pinned Compose BOM, Lifecycle 2.11.0 and OkHttp 5.5.0 require it; CI installs `platforms;android-37.0` (DW-14).
- A `Restoring` state was added so the app does not flash the Sign-in surface while reading stored tokens.
- Gradle heap raised to 4 GB; Robolectric needs `--add-exports` on JDK 25.
- Ledger items: DW-13 iOS selected tab has no filled icon; DW-15 library WebView fallback without Custom Tabs; DW-16 no app icon; DW-17 Material NavigationBar/dialog animate internally (UX-DR101).

**Review.** 31 findings.
- **Patched (13 rows, 8 fixes):** engine catches store/flow/diagnose failures (medium); resume no longer overwrites Working; 30 s timeout for the OIDC client; data extraction rules; tests for MainActivity resume (medium), cold-start sign-out end-session (medium), emptied vault while signed in, Working label at font scale 2.
- **Deferred (3):** iOS certificate classifier untested (medium, unverified); browser flow that never returns (medium, unverified); iOS render-only announcement tests (low).
- **Rejected (15):** reasons in the triage log (WebView fallback tracked as DW-15; best-effort logout; config validation; release variant; displayName; plist duplication; realm flag; three false; duration/segment guards; ledger vs spec deferred; device-level ACs owed as operator actions).

**Follow-up review recommended: true.** Patched entries: high 0, medium 3, low 5. Risk to re-check: the new error-handling and race paths in `SignInEngine.kt` (`showUnlessSigningIn`, failure fallbacks) were written in the patch pass and reviewed only by their own tests.

**Verification** (this machine, after the patches):
- `./gradlew clean check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64 :android:assembleDebug -Pcoldframe.*` with `--no-build-cache`: exit 0; 141 Kotlin tests (core, Android Robolectric, design tokens), 0 failed; ktlint and Android lint pass; debug APK produced.
- `swift:6.3.3` container (podman): `swift build`, `swift test` (49 passed), `swift format lint --strict -r .` pass (before the patches, which touched no Swift).
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --project tests/cs/server.integration`: 28/28 passed, including `ColdframeRealmHasThePublicMobileClient`.
- `pnpm -r test`: exit 0 (web unit 198, e2e 27, design-tokens 599, api-client 1).
- Matrix audit: every I/O row has a passing core or Android test.

**Residual risks.**
- SwiftUI views, the Xcode project and the `ColdframeCore` link have never compiled; the macOS-only Swift tests have never run.
- No real sign-in against a TLS Keycloak on a device; no screen reader run; new CI jobs have not run on GitHub.
- WebView fallback on devices without a Custom Tabs browser (DW-15); internal Material animations (DW-17).
- A local Keycloak whose database survives keeps the old realm without `coldframe-mobile` until the realm is re-imported.
