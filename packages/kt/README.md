# packages/kt

Kotlin libraries.

| Folder | What | Arrives in |
| --- | --- | --- |
| `core/` | The shared Kotlin Multiplatform core: sign-in and session (story 1.5), the Server API client and Sites (story 1.8), BLE setup of a Hub (story 3.6), later push tokens | Story 1.5 for sign-in, 1.8 for the API, 3.6 for BLE |
| `design-tokens/` | Colours, typography, spacing, radii and Carbon icon paths (Gradle project `:design-tokens`) | Story 1.3; `generated/` is written by [`packages/design-tokens`](../design-tokens) |

`core/` builds for the JVM (tests), Android and iOS (`iosArm64`, `iosSimulatorArm64`, the static
framework and XCFramework `ColdframeCore`). It runs OIDC Authorization Code + PKCE through
`kotlin-multiplatform-oidc`, keeps the tokens in the library's store and exposes one observable
state per concern (`SignInEngine.state`, `SitesEngine.state`, `AlertsEngine.state`, `AppearanceStore.theme`); `jvmShared`
holds the JVM and Android certificate classifier and time-zone lookup.

`api/ColdframeApi` calls the Server (`GET /sites`, `POST /sites` with an `Idempotency-Key`,
`PATCH /sites/{siteId}`, the Lot endpoints under `/sites/{siteId}/lots` and the paged
`GET /sites/{siteId}/alerts`) with the
session's access token, refreshed by `SignInEngine` when expired; a 401 signs out with the SignedOut
notice. Results are values (`ApiResult.Ok` / `ApiResult.Failed(ApiFailure)`), never exceptions. The
DTOs are hand-written and held to `packages/openapi/coldframe.openapi.json` by `OpenApiContractTest`.
`sites/SitesEngine` owns the Sites state (Loading, Failed, NeedsSite, Ready with the current Site and
Create Site), the per-device choices (current Site; the confirmed time zone only until the Server has it) and the models of the
first-run tiles and the Site menu. Android reads `AndroidSignIn.sites`; iOS observes the flat
`SitesSnapshot` through `IosSignIn.sites`. `lots/LotsEngine` follows the current Site and owns Site
settings (rename Site for the Owner, create/rename/remove Lots for Owners and Administrators, the
Member read-only notice, one `Idempotency-Key` per create attempt) and the Lots Garden shows, in the
Server's order; Android reads `AndroidSignIn.lots`, iOS the flat `LotsSnapshot` through
`IosSignIn.lots`. `notifications/NotificationSettingsEngine` owns My notifications (Story 6.3): the
Notification Window, the time zone and its hand-over from the device to the Server, and the mute and
Reminder cadence of the current Site over `/me/notification-settings` and
`/sites/{siteId}/notification-settings`; `LotsEngine` carries the Site's Reminder cadence
(`/sites/{siteId}/reminder-cadence`) for Site settings. Android reads `AndroidSignIn.notifications`, iOS
the flat `NotificationSettingsSnapshot` through `IosSignIn.notifications`. `design-tokens/` builds for the JVM and Android; iOS uses the
Swift package in [`packages/swift/design-tokens`](../swift/design-tokens).
`core/generated/` holds `crypto/CryptoSpec`, the Device crypto constants written by
[`packages/crypto-spec`](../crypto-spec) into `commonMain`.

**Add a Hub (Story 3.6).** `crypto/` is pure Kotlin and runs on every target (Android 29, iOS):
SHA-256, HMAC and HKDF, X25519 (RFC 7748 ladder, clamped, all-zero secret refused) and
ChaCha20-Poly1305, with `SetupCipher` for the AD-25 session (per-direction counters, nonce
`0x00000000 ‖ counter_be64`, AAD the protocol major). `commonTest` runs them against
`packages/crypto-spec/vectors.json` (compiled in as a constant by `generateTestVectors`) and the
RFC anchors; `jvmTest` cross-checks them against the JDK (`JdkCrypto`). Wire 7.1.0 generates the
`coldframe.setup.v1` messages from [`packages/proto`](../proto). `setup/` has the framing, the
setup-code normalization, the app end of the session (`SetupSession`), the `SetupRadio` port with
its Kable 0.45 adapter (`KableSetupRadio`, radio state from `AndroidRadioState` / `IosRadioState`)
and `HubSetupEngine`, which runs the flow and every error rule and exposes one `HubSetupState`;
Android reads `AndroidSignIn.hubSetup`, iOS the flat `HubSetupSnapshot` through
`IosSignIn.hubSetup`. `ColdframeApi` gains `GET /enrolment-key` and `POST /sites/{siteId}/devices`.

**Add a Node (Story 4.3).** `NodeSetupEngine` reuses the same radio port, session, framing and
enrolment calls for the five steps Press, Scan, Code, Lot and Outcome, on one Site (the one passed
to `open`, else the current one; Administrators and Owners only). It lists only adverts named
`Coldframe Node XXXX`. Session order, once per code attempt on the code step: connect → hello →
`IdentityRequest` (the kind must be `NODE`) → `GET /enrolment-key` (fingerprint recomputed) →
`EnrolmentRequest` → `EnrolmentResponse`; the engine keeps `enc`/`ciphertext` in memory,
disconnects, then shows the accepted chip. A Node drops an idle connection after 60 s and listens
for 180 s, so nothing is held open through the Lot picker, and a Node is never sent `SiteBinding`,
`WifiScanRequest` or `WifiConfig`. The Lot step reads `GET /sites/{siteId}/lots` (only `noNode`
Lots can be picked), creates a Lot inline, and `assign` posts `POST /sites/{siteId}/devices` with
`kind: node`, the sealed key relayed unread, `lotId` and one Idempotency-Key per Device and Site.
409 `lot-claimed` and 404 stay on the Lot step; 409 `device-assigned` (`ApiFailure.DeviceAssigned`),
409 `device-on-another-site` and 403 end on an outcome. On 201 the Lots and Devices engines reload
(`SitesWiring.nodeSetup`). "Add a Node" on the Hub's outcome closes that flow and opens this one
for the Hub's Site (`HubSetupEngine.onAddNode`). Android reads `AndroidSignIn.nodeSetup`, iOS the
flat `NodeSetupSnapshot` through `IosSignIn.nodeSetup`.
Tests live in [`tests/kt`](../../tests/kt).
