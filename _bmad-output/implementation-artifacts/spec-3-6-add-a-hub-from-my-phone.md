---
title: 'Story 3.6: Add a Hub from my phone'
type: 'feature'
created: '2026-09-29'
baseline_revision: '9d63bfe1fe57c08ef6df0c333f2ca2c1eae554ff'
status: 'awaiting-operator'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/EXPERIENCE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/DESIGN.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-9-manage-my-site-and-lots.md'
  - '{project-root}/packages/proto/README.md'
  - '{project-root}/apps/swift/README.md'
warnings: ['oversized']
deferred:
  - summary: >-
      The engine's check that the EnrolmentResponse device_id matches the accepted Identity has no test.
    evidence: |-
      FakeHub always answers device_id = HUB_DEVICE_ID (tests/kt/core/commonTest/.../setup/SetupFakes.kt); deleting the comparison in HubSetupEngine.startProgress leaves every test green. The Server would likely reject a mismatched seal, so this is hardening.
    location: >-
      packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/setup/HubSetupEngine.kt (startProgress, device_id comparison)
    severity: low
  - summary: >-
      iOS may briefly show and announce the Bluetooth-needed notice on every open because IosRadioState starts at Off and maps Unknown/Resetting to Off before CoreBluetooth reports.
    evidence: |-
      IosRadioState initialises MutableStateFlow(RadioState.Off) and maps every non-listed CBManagerState to Off. Whether the notice renders (and VoiceOver announces it) before centralManagerDidUpdateState arrives can only be settled on a real iPhone; RadioState has no Unknown value today.
    location: >-
      packages/kt/core/src/iosMain/kotlin/com/escendit/coldframe/core/setup/IosRadioState.kt:18-29
    severity: medium (unverified)
operator_actions:
  - "On a Mac with Xcode 26.6, run swift build && swift test && swift format lint --strict -r . at the repo root and fix any compile or test failure in the never-compiled SwiftUI files (UI/HubSetupViews.swift and the new RenderTests at .accessibility5, light and dark)."
  - "On that Mac, in apps/swift/ios, run xcodegen generate and xcodebuild build -scheme Coldframe -destination 'generic/platform=iOS Simulator' CODE_SIGNING_ALLOWED=NO, and confirm App/CoreHubSetupService.swift compiles against ColdframeCore (check the Kotlin/Native exported names such as announcing(active:) and networkSupported)."
  - "Install the Android app against the real Server and Keycloak, power on an unprovisioned bench Hub, run Add a Hub from the first-run tile as an Owner, and confirm Hub is online appears within 1 minute of tapping send on the Wi-Fi step (FR1)."
  - "Repeat the Add a Hub run on an iPhone against the same Server with a freshly erased Hub (espflash erase of the cf_setup record, see DW-46), and confirm the same 1-minute outcome."
  - "On both phones, walk the error cases with a real Hub: Bluetooth off, wrong setup code, wrong Wi-Fi password, a WPA3-only network row, and a Server URL the Hub cannot reach; confirm each shows its UX-DR94/UX-DR95 copy and recovery and that the Hub re-advertises afterwards."
  - "Run the Android flow with TalkBack at font scale 2 and the iOS flow with VoiceOver at the largest Dynamic Type size; confirm candidate, progress and error announcements are spoken and the 90 s timeout never fires while one is being read."
---

<intent-contract>

## Intent

**Problem:** A Hub can only be provisioned today with the Rust bench client (`tests/rs/setup-client`); the mobile apps have no BLE, no production crypto, no Kotlin protobuf and no Add a Hub flow, so Simon cannot bring a Hub online from his phone (FR1).

**Approach:** Build the setup client once in the KMP core — pure-Kotlin AD-25 crypto checked against the shared vectors, Wire-generated `setup.proto` types, the framing, a Kable-backed radio port and a `HubSetupEngine` state machine — then render its state as the five-step Add a Hub flow in Jetpack Compose and SwiftUI, entered from the first-run "Add a Hub" tile for Administrators and Owners.

## Boundaries & Constraints

**Always:**
- The core follows the existing engine pattern: one `StateFlow<HubSetupState>`, a flat `HubSetupSnapshot` + `IosHubSetup` facade for Swift, actions as methods; shells hold UI only (AD: KMP core alone implements BLE).
- Protocol exactly as the firmware implements it (`packages/rs/setup`, reference client `packages/rs/setup/src/app.rs`): service `c01d0001-…c0de`, write `c01d0002`, notify `c01d0003` (subscribe before first write), `header(1)‖fragment` framing with fragments ≤ MTU−4 and frames ≤ 1152, request a larger MTU; plaintext `SessionHello`/`SessionHelloReply`, then `SealedSetupMessage` with per-direction counters from 0, nonce `00000000‖counter_be64`, AAD `0x01`, keys from HKDF(salt = normalized code, ikm = X25519, info = `coldframe/setup/v1`‖app_pub‖hub_pub, L = 64).
- Setup-code normalization in the app: trim, drop spaces and `-`, uppercase, map `O→0`, `I→1`, `L→1`; the code is never sent over BLE or logged.
- Message order per session: `IdentityRequest` (validates the code) → `WifiScanRequest` → on step 5: `SiteBinding{site_id, server_url = CoreConfig.serverUrl}` → `EnrolmentRequest{server key, fingerprint}` → `POST /sites/{siteId}/devices` with the sealed `K_dev` relayed unread → `WifiConfig` → wait for `WifiResult`. Binding and enrolment run once per BLE session; a retry after `WRONG_PASSWORD`/`NO_SERVER` resends only `WifiConfig`.
- `GET /enrolment-key` is fetched before enrolment; the app recomputes SHA-256 of the decoded key and refuses a mismatching fingerprint; the fingerprint is shown on step 4.
- Enrolment `POST` carries an `Idempotency-Key` generated once per flow attempt and reused on retries; `deviceId` = lowercase hex of `Identity.device_id`, `enc`/`ciphertext` base64url without padding.
- Segments advance only on real events; announcements, copy and error recoveries exactly as in the I/O matrix and UX-DR37/39/40/41/42/55/66/94/95/103/105; all copy in `strings.xml` + `Localizable.xcstrings` with matching keys; no animations, spinners, toasts or long-press (existing source-scan rules).
- Every AC and each UX-DR above first gets a failing test named after its id (AD-24), and the ids are added to `CoverageTest.storyIds` and `iosIds`.

**Never:**
- No server changes, no new endpoints, no Devices list or Devices-tab entry point (Story 3.7), no Node flow (Epic 4), no Hub firmware changes.
- No Espressif provisioning/Improv, no JDK-only crypto APIs in `commonMain` (`javax.crypto.KDF` needs JDK 24, X25519 `KeyAgreement` needs Android 33 — minSdk is 29), no third-party crypto library.
- Never log keys, codes, passwords or payloads; never keep the Wi-Fi password after the flow ends.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Happy path | Admin taps Add a Hub, picks "Hub 3F2A", code `k7m2-q9xp`, Novak-Home + password, Site | Steps 01→05; BLUETOOTH ✓ after enrolment 201, WI-FI SENT ✓ after write, JOINING… active, SERVER ✓ on `WifiResult CONNECTED`; outcome "Hub is online" with ADD A NODE | — |
| Bluetooth off / permission denied | radio unavailable or unauthorized on step 1 | Inline notice "Coldframe needs Bluetooth to find the Hub." + Open Settings | Scanning resumes when the radio becomes ready |
| No Hub in 30 s | no advert with the service UUID | Notice "No Hub in range yet. Power it on within a few metres; its LED blinks orange while it waits."; scan continues, "Still scanning…" | — |
| New candidate | first advert from a Device | Tile added, sorted by RSSI; polite "Hub 3F2A found, strong signal." once per Device | Order/signal changes not announced |
| Wrong setup code | first sealed reply fails to open | "That setup code doesn't match Hub 3F2A. Check its label or the serial console."; field kept; next Continue reconnects with a new hello | Assertive announcement |
| Code accepted | `Identity` opens | ACCEPTED chip; full Device ID kept; step 3 requests scan list | — |
| WPA3-only row | `WifiNetwork.security = WPA3_ONLY` | Row hatched, not selectable, reason "Not supported: the Hub needs WPA2 or mixed WPA2/WPA3." | `OTHER` security rows likewise disabled |
| Other network | user types an SSID | Hidden SSID sent as typed | — |
| Wrong Wi-Fi password | `WifiResult WRONG_PASSWORD` | Error outcome "STEP 5 STOPPED" / "Wrong Wi-Fi password"; actions Re-enter password (step 3, network kept, password cleared) / Other network | Assertive "Wrong Wi-Fi password. Re-enter password." |
| Network not found / unsupported | `NETWORK_NOT_FOUND` / `UNSUPPORTED_SECURITY` | Error outcome back to step 3 with specific headline | — |
| Server not reached | `WifiResult NO_SERVER` | "Hub 3F2A is on Novak-Home but can't reach your Server." → Try again (resend `WifiConfig`) / Help (reveals troubleshooting text) | Session kept open |
| Progress timeout | no `WifiResult` 90 s after `WifiConfig` | Error outcome "Hub 3F2A didn't come online" → Try again (back to step 1) | Deferred while an announcement is being read (UX-DR103) |
| Lost connection | disconnect before outcome | "Lost the connection to Hub 3F2A. Nothing was saved." → Try again (step 1) | — |
| Enrolment refused | 409 / 403 / 404 / 5xx / unreachable | Error outcome with specific copy (on another Site / not allowed / Site gone / can't reach Server); Hub gets no `WifiConfig` | Disconnect |
| Fingerprint mismatch | server key hash ≠ fingerprint, or Hub `FINGERPRINT_MISMATCH` | Error outcome "The Server's enrolment key doesn't check out." | Disconnect, nothing enrolled |
| Leave mid-flow | Back/Cancel after a Hub is selected | Confirm "Stop setting up Hub 3F2A? Nothing is saved on the Hub." | Confirm disconnects and closes |

</intent-contract>

## Code Map

- `packages/kt/core/build.gradle.kts` -- KMP targets jvm/android/iosArm64/iosSimulatorArm64; `commonMain` already includes `generated/kotlin`; tests live in `tests/kt/core/{commonTest,jvmTest}`.
- `gradle/libs.versions.toml` -- add Kable `com.juul.kable:kable-core:0.45.0` (has jvm, android, ios targets) and Wire 7.1.0 (plugin + runtime).
- `packages/kt/core/generated/kotlin/com/escendit/coldframe/core/crypto/CryptoSpec.kt` -- generated constants (`SETUP_LABEL`, offsets, `SETUP_MAX_CODE_LENGTH`, `ENROLMENT_*`); reuse, never hand-edit.
- `packages/proto/coldframe/setup/v1/setup.proto` -- message set (`SessionHello`, `SealedSetupMessage`, `SetupMessage` oneof, `WifiSecurity`, `WifiStatus`, `SetupErrorCode`); README line ~49 says the Kotlin core arrives with 3.6.
- `packages/rs/setup/src/{framing,session,code,app}.rs`, `packages/rs/crypto/src/setup.rs` -- authoritative protocol; `app.rs` is the reference client to mirror.
- `packages/crypto-spec/vectors.json` -- `setup[0]` (`popCode "k7m2q9xp"`, wrong code, keys, 3 sealed messages) + RFC anchors.
- `tests/kt/core/jvmTest/kotlin/com/escendit/coldframe/core/crypto/{Vectors,CryptoVectorsTest,CryptoAnchorsTest,CryptoNegativeTest,JdkCrypto}.kt` -- JVM-only reference; production crypto must pass the same vectors from `commonTest`.
- `packages/kt/core/src/commonMain/.../api/{ColdframeApi,ApiDtos,ApiResult}.kt` -- hand-written Ktor client (DW-27); add `enrolmentKey()` and `enrolDevice()`; `tests/kt/core/jvmTest/.../api/OpenApiContractTest.kt` must cover the new DTOs.
- `packages/openapi/coldframe.openapi.json` -- `GET /enrolment-key` → `{publicKey (b64url 43), fingerprint (64 hex)}`; `POST /sites/{siteId}/devices` (Admin+, `Idempotency-Key` required) → 201 `Device`, problems 400/401/403/404/409/422.
- `packages/kt/core/src/commonMain/.../sites/{SitesEngine,SitesSnapshot,FirstRunSteps,Sites}.kt` -- engine/snapshot pattern to copy; `FirstRunSteps.of(role, flowAvailable)` (DW-26); `SiteRole` rank order.
- `packages/kt/core/src/commonMain/.../signin/CoreConfig.kt` -- `serverUrl` for `SiteBinding.server_url`.
- `packages/kt/core/src/{iosMain/.../sites/IosSites.kt, androidMain/.../signin/AndroidSignIn.kt}` -- facade and wiring patterns.
- `apps/kt/android/.../{MainActivity,ColdframeRoot}.kt`, `ui/sites/{GardenScreen,SitesActions,CreateSiteScreen}.kt`, `ui/components/*` -- state-driven navigation, full-screen replacement like Create Site, first-run tiles (not clickable yet), `InlineNotice` + `Announcement`.
- `apps/kt/android/src/main/AndroidManifest.xml`, `res/values/strings.xml` -- only `INTERNET` today; strings must match iOS catalogue (`StringsTest`).
- `tests/kt/android/test/.../{SnapshotTest,TestSupport,CoverageTest,SourceScanTest,StringsTest,AccessibilityTest}.kt`, baselines `tests/kt/android/snapshots/<surface>-{light,dark}.png` -- Roborazzi at font scale 2, `assertNothingOverflows`.
- `apps/swift/ios/Sources/ColdframeIOS/{SitesPresentation,L10n,SignInPresentation}.swift`, `UI/{Screens,SitesViews,Components}.swift`, `App/{ColdframeApp,CoreSitesService}.swift`, `project.yml`, `Config/Info*.plist` -- presentation/service/adapter pattern; Bluetooth usage key missing.
- `tests/swift/ios/ColdframeIOSTests/{SitesPresentationTests,RenderTests,SourceRulesTests}.swift` -- Linux-runnable presentation tests; macOS-only render tests `arguments: [false, true]`.

## Tasks & Acceptance

**Execution:**
- `gradle/libs.versions.toml`, `packages/kt/core/build.gradle.kts` -- add Kable and Wire (generate Kotlin for `coldframe.setup.v1` only into the core); if the Wire plugin cannot run beside the AGP KMP library plugin, run the Wire compiler from a Gradle task into `packages/kt/core/generated/kotlin` and commit the output -- generated contract per AD-10.
- `packages/kt/core/src/commonMain/.../crypto/{Sha256,Hkdf,X25519,ChaCha20Poly1305,SetupCrypto}.kt` + `expect fun secureRandom(n)` actuals in `jvmShared`/`iosMain` -- pure-Kotlin constant-time primitives (X25519 per RFC 7748 ladder with clamping, reject all-zero shared secret) and the AD-25 session seal/open with counter checks -- portable to Android 29 and iOS.
- `tests/kt/core/commonTest/.../crypto/*` -- run `setup` vectors, RFC 5869/7748/8439 anchors and the wrong-code/tamper/replay/small-order negatives against the production code; keep `JdkCrypto` as a JVM cross-check -- mitigates the epic's cross-language crypto risk.
- `packages/kt/core/src/commonMain/.../setup/{Framing,SetupCode,SetupSession,SetupRadio,KableSetupRadio,HubSetupEngine,HubSetupState,HubSetupSnapshot}.kt` -- framing/reassembly, code normalization, `SetupRadio` port (radio state, scan adverts with name prefix + RSSI, connect → write/notifications/MTU/close) with the Kable adapter, and the engine implementing every matrix row, 30 s no-Hub notice, 90 s progress timeout held while `announcing(true)`, polite/assertive announcement events, keep-awake flag.
- `packages/kt/core/src/commonMain/.../api/{ColdframeApi,ApiDtos}.kt` + `OpenApiContractTest` -- `enrolmentKey()` and `enrolDevice(siteId, request, idempotencyKey)` with problem mapping.
- `packages/kt/core/src/commonMain/.../sites/FirstRunSteps.kt` and callers -- Add a Hub tile actionable on mobile for Administrator/Owner (closes DW-26 for the Hub step).
- `packages/kt/core/src/{iosMain/.../setup/IosHubSetup.kt, androidMain/.../signin/AndroidSignIn.kt}` -- facade (`watch {}` + actions) and wiring.
- `tests/kt/core/commonTest/.../setup/*` -- `FakeSetupRadio` + `FakeHub` running the real session crypto, Ktor `MockEngine`; one test per matrix row and step transition, snapshot key tests pinning Swift raw values.
- `apps/kt/android/.../ui/setup/{AddHubFlow,SetupFlowShell,CandidateTile,SetupCodeField,WifiNetworkRow,SetupProgress,SetupOutcome}.kt`, `HubSetupActions.kt`, `MainActivity.kt`, `GardenScreen.kt`, `AndroidManifest.xml` (BLUETOOTH_SCAN neverForLocation + BLUETOOTH_CONNECT; BLUETOOTH/BLUETOOTH_ADMIN/ACCESS_FINE_LOCATION maxSdk 30), `strings.xml` -- Compose flow with runtime permission request, keep-screen-on, focus to step title/headline, live regions.
- `tests/kt/android/test/.../AddHubFlowTest.kt`, `SnapshotTest.kt`, `CoverageTest.kt` + baselines -- UX-DR-named tests; Roborazzi light+dark for steps 1–5, success and one error outcome.
- `apps/swift/ios/Sources/ColdframeIOS/{HubSetupPresentation,L10n}.swift`, `Resources/Localizable.xcstrings`, `UI/HubSetupViews.swift`, `App/{CoreHubSetupService,ColdframeApp}.swift`, `Config/Info*.plist` (`NSBluetoothAlwaysUsageDescription`), `apps/swift/README.md` -- SwiftUI flow, `isIdleTimerDisabled`, `AccessibilityNotification` announcements, announcement-finished → `announcing(false)`.
- `tests/swift/ios/ColdframeIOSTests/{HubSetupPresentationTests,RenderTests}.swift` -- one presentation test per matrix row; render tests light/dark at `.accessibility5`.
- `packages/proto/README.md`, `apps/kt/README.md` -- document the Kotlin client, permissions, and the WPA3-only limitation.

**Acceptance Criteria:**
- Given an Administrator or Owner on Android or iOS, when they tap the first-run Add a Hub tile, then the full-screen flow shows "01 / 05" with Cancel, and later steps show Back; a Member's tile stays non-actionable.
- Given the core with a fake radio and fake Hub, when the happy path runs, then the Hub receives `SiteBinding`, `EnrolmentRequest` and `WifiConfig` in that order, the server receives the Hub's `enc`/`ciphertext` byte-identical, and no `WifiConfig` is sent unless enrolment returned 201.
- Given `CONNECTED` arrives, when the outcome renders, then "Hub is online" shows with focus on the headline and ADD A NODE closes the flow to the Garden.
- Given a screen reader is announcing, when the 90 s progress timeout expires, then the timeout waits until `announcing(false)` (UX-DR103).
- Given `./gradlew check`, when it runs, then crypto vectors, engine, Android UI and Roborazzi light/dark snapshots for every step pass.

## Spec Change Log

## Review Triage Log

### 2026-09-29 — Review pass
- verdicts: 30 findings — high 1, medium 7, low 13, false 5, maybe-false 4
- findings:
  - `[false]` `[reject]` Blind: Idempotency-Key reused across BLE sessions gives 422 — SiteGrain.RegisterDevice returns IdempotencyKeyReused only when the cached registration names a different deviceId; the key is cached per deviceId/siteId, so a same-Hub retry registers idempotently.
  - `[low]` `[patch]` Blind: setup code and Wi-Fi password leak through data-class toString — CodeForm, WifiForm and HubSetupSnapshot now redact both; test theSetupCodeAndThePasswordNeverAppearInToString.
  - `[low]` `[reject]` Blind: Wi-Fi password length not checked for WPA2 before enrolment — the Hub answers WRONG_PASSWORD with a working recovery; the fix adds new errors and copy on both platforms.
  - `[low]` `[reject]` Blind: non-network enrolment failures (400, 422, certificate, 5xx) shown as can't reach your Server — rare after the key step already succeeded over the same TLS; the fix adds outcome kinds and copy.
  - `[low]` `[reject]` Blind: Android 10–11 with Location Services off reads Ready while scans return nothing — narrow slice of devices, documented in apps/kt/README.md; a proper fix needs a new radio state and copy. Listed as residual risk.
  - `[maybe-false]` `[defer]` Blind: iOS shows Bluetooth off before CoreBluetooth reports — needs a real iPhone to see whether the notice renders/announces before the first delegate call; deferred as medium (unverified).
  - `[medium]` `[patch]` Blind: Android live region created already filled and identical repeats not re-announced — AnnouncementRegion now always exists and empties then refills per announcement id.
  - `[medium]` `[patch]` Blind: iOS UX-DR103 hold released by a stale timer or unrelated finish — HubSetupViews tracks the current announcement id/text and only releases for it.
  - `[low]` `[reject]` Blind: Lost connection copy says nothing was saved after a 201 enrolment — "nothing was saved" is about the Hub, which stores nothing before CONNECTED; the Server side is recorded as DW-47.
  - `[low]` `[reject]` Blind: candidates re-sort on every advert and are never pruned — one Hub in range is the normal case; the fix adds smoothing/expiry logic.
  - `[low]` `[patch]` Blind: pasted code with NBSP/newline/en dash reported blank — normalize now drops every whitespace char and the Unicode dashes; tests extended.
  - `[low]` `[reject]` Blind: no rescan on the Wi-Fi step — Other network covers a missing SSID; the fix adds a new action and state.
  - `[high]` `[patch]` Edge: connect/subscribe timeout surfaces as cancellation and leaves step 2 stuck working — KableSetupRadio catches TimeoutCancellationException first, closes the peripheral and throws SetupLinkException.
  - `[low]` `[reject]` Edge: Back ignored while the code check is working — bounded by the 20 s connect timeout after the high fix; adding cancellation paths is more than a direct correction.
  - `[medium]` `[patch]` Edge: Back→Back during an in-flight Wi-Fi scan shows LostConnection over step 1 — backToScan cancels workJob before disconnecting; test uxDr39BackToStep1WhileTheHubScansEndsOnStep1WithoutAnOutcome.
  - `[low]` `[reject]` Edge: reassembly failures are dropped and the reply waits for its timeout — only a misbehaving Hub triggers it, and it still ends on an outcome.
  - `[low]` `[patch]` Edge: backToScan keeps the selected Hub though the list is cleared — selected is now reset to null (same patch as above).
  - `[false]` `[reject]` Edge: duplicate SSID with different security refuses the supported row — the Hub sends one entry per SSID (packages/rs/setup scan list), so duplicates do not occur.
  - `[false]` `[reject]` Edge: SwiftUI ForEach duplicate ids for duplicate SSIDs — same refutation: one entry per SSID.
  - `[false]` `[reject]` Edge: iOS shows the blank message for SsidTooLong on a listed network — listed SSIDs come from the Hub and are at most 32 bytes, so only SsidBlank can occur outside Other network.
  - `[medium]` `[patch]` Edge: iOS stale hold release (duplicate of the Blind finding) — same fix.
  - `[maybe-false]` `[defer]` Edge: iOS initial Off state (duplicate of the Blind finding) — same deferral.
  - `[low]` `[reject]` Edge: Android 10–11 location services off (duplicate of the Blind finding) — same reason.
  - `[false]` `[reject]` Edge: Android hold is an estimate, so a long announcement can be cut off — the estimated hold is the Design Notes decision, since Android has no finish callback; not a defect against the spec.
  - `[medium]` `[patch]` Verification gap: UX-DR103 hold never driven from the Android shell in tests — added `UX-DR103 an announcement holds the timeout while TalkBack reads it` with ShadowAccessibilityManager.
  - `[medium]` `[patch]` Verification gap: iOS tile actionability not pinned at the snapshot boundary — added uxDr66AnOwnersSnapshotMakesTheAddAHubTileActionable and refreshed the stale Swift test title.
  - `[medium]` `[patch]` Verification gap: Swift→Kotlin outcome action parsing unverified — parse moved to commonMain `outcomeActionOf`, round-trip test uxDr55EveryOutcomeActionKeyParsesBackToItself.
  - `[maybe-false]` `[defer]` Verification gap: enrolment device_id check untested — whether removing it causes harm depends on the Server rejecting a mismatched seal; deferred as low hardening.
  - `[maybe-false]` `[reject]` Verification gap (other): enrolment failures mapped to ServerUnreachable — duplicate of the Blind low finding; same reason.
  - `[low]` `[reject]` Intent alignment: spec still in-review without operator_actions — finalization sets awaiting-operator and operator_actions (this pass), so nothing to change in code.

## Design Notes

- "Waits for the first heartbeat": no Server endpoint exposes last-seen until 3.7, but the Hub answers `CONNECTED` only after its signed heartbeat got a 200 (spec 3-5), so `CONNECTED` is the SERVER segment's real event.
- Enrolment must precede `WifiConfig` (the Server rejects heartbeats from unenrolled Devices → `NO_SERVER`). A flow abandoned after enrolment leaves an idempotent same-Site enrolment and an unprovisioned Hub that re-advertises; setting it up again on the same Site converges. Moving it to another Site hits 409 (DW-44) — record as deferred.
- Timeout copy (not in UX): headline "Hub 3F2A didn't come online", body "Nothing was saved on the Hub. Keep it near your router and try again." NO_SERVER Help reveals "The Hub reached {ssid} but not {server host}. Check that this name resolves on your home network."
- Android has no "announcement finished" callback: when an accessibility service is on, hold `announcing(true)` for an estimated reading time (≥ 1 s + 60 ms per character).
- ADD A NODE closes the flow to the Garden until Epic 4 wires the Node flow — record as deferred.

## Verification

**Commands:**
- `./gradlew check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64` -- expected: all green including Roborazzi verify.
- `podman run --rm --security-opt label=disable -v "$PWD":/work -w /work swift:6.3.3 sh -c 'swift build && swift test && swift format lint --strict -r .'` -- expected: green.
- `pnpm install --frozen-lockfile && pnpm -r lint && pnpm -r typecheck && pnpm -r test` -- expected: green (coverage and contract checks).

**Manual checks (if no CLI):**
- Operator: Mac build of the iOS app and SwiftUI render tests; bench run with a real Hub on Android and iPhone measuring "Hub is online" within 1 minute of sending Wi-Fi (FR1), with TalkBack/VoiceOver.

## Auto Run Result

Status: awaiting-operator

**Summary.** The KMP core now carries the whole Add a Hub setup client: pure-Kotlin SHA-256/HMAC/HKDF, X25519 and ChaCha20-Poly1305 checked against `packages/crypto-spec/vectors.json` and the RFC anchors (JDK cross-check on the JVM), Wire 7.1.0-generated `setup.proto` types, BLE framing, setup-code normalization, a `SetupRadio` port with a Kable 0.45.0 adapter and platform radio-state sources, the enrolment API calls, and `HubSetupEngine`, which implements every I/O-matrix row. Android (Compose) and iOS (SwiftUI) render the five-step flow from the first-run Add a Hub tile for Administrators and Owners, with Bluetooth permissions, keep-awake, focus moves, announcements and the UX-DR103 hold.

**Files changed.**
- `gradle/libs.versions.toml`, `build.gradle.kts`, `packages/kt/core/build.gradle.kts` — Kable, Wire, proto generation.
- `packages/kt/core/src/commonMain/.../crypto/*` (+ `jvmShared`/`iosMain` secure random) — production AD-25 crypto.
- `packages/kt/core/src/commonMain/.../setup/*` — framing, code, session, radio port, Kable adapter, engine, state, snapshot.
- `packages/kt/core/src/{androidMain,iosMain}/.../setup/*` — radio state sources and the `IosHubSetup` facade; `AndroidSignIn`/`IosSignIn`/`SitesWiring` wiring.
- `packages/kt/core/src/commonMain/.../api/*` — `enrolmentKey()`, `enrolDevice()`, problem mapping; `sites/FirstRunSteps.kt` Add a Hub actionable on mobile for Admin+.
- `apps/kt/android/.../ui/setup/*`, `MainActivity.kt`, `ColdframeRoot.kt`, `AppShell.kt`, `GardenScreen.kt`, `AndroidManifest.xml`, `strings.xml` — Compose flow.
- `apps/swift/ios/Sources/ColdframeIOS/{HubSetupPresentation,L10n}.swift`, `UI/HubSetupViews.swift`, `Resources/Localizable.xcstrings`, `App/CoreHubSetupService.swift`, `ColdframeApp.swift`, `Config/Info*.plist`, `project.yml` — SwiftUI flow.
- `tests/kt/core/**` (crypto, framing, session, engine, snapshot, API), `tests/kt/android/**` (AddHubFlowTest, snapshots, coverage, strings), 14 Roborazzi baselines, `tests/swift/ios/**` — tests.
- READMEs (`packages/proto`, `packages/kt`, `apps/kt`, `apps/swift`) and `deferred-work.md` (DW-47, DW-48, DW-26 progress).

**Review findings.** 30 findings: 11 patched rows (1 high, 7 medium counting the duplicate iOS-hold row, 3 low — connect timeout, Back-during-scan and stale selection, toString redaction, code normalization, Android live region, iOS hold release, three test gaps), 3 deferred rows as 2 items (enrolment device_id check test; iOS initial Off state, unverified), 16 rejected with reasons in the triage log (5 false, the rest low with non-trivial fixes or duplicates).

**Follow-up review recommendation: true.** A high was patched (connect timeout left step 2 stuck), plus six distinct medium entries; the named unverified risk is the Kable adapter and the iOS announcement-hold code, which no test on Linux exercises and which change real-device behaviour.

**Verification.** `./gradlew check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64` green (core JVM tests, Android Robolectric + Roborazzi verify, ktlint, Android lint); `swift:6.3.3` container `swift build && swift test && swift format lint --strict -r .` green (104 tests); `pnpm install --frozen-lockfile && pnpm -r lint && pnpm -r typecheck && pnpm -r test` green. Matrix audit: every I/O row has an engine test that ran and passed.

**Residual risks.** SwiftUI views and the iOS adapter have never been compiled (Linux); Kable and the platform radio-state code are exercised only through fakes; FR1's 1-minute outcome needs a real Hub; on Android 10–11 with Location Services off the scan finds nothing and only the No Hub notice appears; see `operator_actions`.
