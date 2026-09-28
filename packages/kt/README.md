# packages/kt

Kotlin libraries.

| Folder | What | Arrives in |
| --- | --- | --- |
| `core/` | The shared Kotlin Multiplatform core: sign-in and session (story 1.5), the Server API client and Sites (story 1.8), later BLE setup and push tokens | Story 1.5 for sign-in, 1.8 for the API |
| `design-tokens/` | Colours, typography, spacing, radii and Carbon icon paths (Gradle project `:design-tokens`) | Story 1.3; `generated/` is written by [`packages/design-tokens`](../design-tokens) |

`core/` builds for the JVM (tests), Android and iOS (`iosArm64`, `iosSimulatorArm64`, the static
framework and XCFramework `ColdframeCore`). It runs OIDC Authorization Code + PKCE through
`kotlin-multiplatform-oidc`, keeps the tokens in the library's store and exposes one observable
state per concern (`SignInEngine.state`, `SitesEngine.state`, `AppearanceStore.theme`); `jvmShared`
holds the JVM and Android certificate classifier and time-zone lookup.

`api/ColdframeApi` calls the Server (`GET /sites`, `POST /sites` with an `Idempotency-Key`,
`PATCH /sites/{siteId}` and the Lot endpoints under `/sites/{siteId}/lots`) with the
session's access token, refreshed by `SignInEngine` when expired; a 401 signs out with the SignedOut
notice. Results are values (`ApiResult.Ok` / `ApiResult.Failed(ApiFailure)`), never exceptions. The
DTOs are hand-written and held to `packages/openapi/coldframe.openapi.json` by `OpenApiContractTest`.
`sites/SitesEngine` owns the Sites state (Loading, Failed, NeedsSite, Ready with the current Site and
Create Site), the per-device choices (current Site, confirmed time zone) and the models of the
first-run tiles and the Site menu. Android reads `AndroidSignIn.sites`; iOS observes the flat
`SitesSnapshot` through `IosSignIn.sites`. `lots/LotsEngine` follows the current Site and owns Site
settings (rename Site for the Owner, create/rename/remove Lots for Owners and Administrators, the
Member read-only notice, one `Idempotency-Key` per create attempt) and the Lots Garden shows, in the
Server's order; Android reads `AndroidSignIn.lots`, iOS the flat `LotsSnapshot` through
`IosSignIn.lots`. `design-tokens/` builds for the JVM and Android; iOS uses the
Swift package in [`packages/swift/design-tokens`](../swift/design-tokens).
Tests live in [`tests/kt`](../../tests/kt).
