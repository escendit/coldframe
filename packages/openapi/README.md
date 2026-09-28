# packages/openapi

The REST contract, written before the code that serves it (AD-10):
[`coldframe.openapi.json`](coldframe.openapi.json), OpenAPI 3.1.

| Operation | Access (`x-coldframe-minimum-role`) | Since |
| --- | --- | --- |
| `GET /sites` | `Authenticated` | Story 1.8 |
| `POST /sites` | `Authenticated` | Story 1.6 |
| `GET /sites/{siteId}` | `Member` | Story 1.6 |

The Device endpoints `POST /device/ingest` and `POST /device/heartbeat` arrive in Epics 3 and 4.

## Conventions

- **Authentication.** Every operation takes a Keycloak access token of the `coldframe` realm with
  audience `coldframe-server` (security scheme `bearer`). The User ID is the token's `sub`.
- **Access.** Every operation declares `x-coldframe-minimum-role`: a `SiteRole` (`Owner`,
  `Administrator`, `Member`) on the Site named by `siteId`, or `Authenticated` for any signed-in
  User. The Server reads the caller's Role from its identity projection, never from token claims.
- **Errors** are RFC 9457 Problem Details (`application/problem+json`) with a stable `type`,
  `urn:coldframe:problem:<slug>`: `unauthorized` (401), `forbidden` (403, the Site exists but the
  caller's Role is missing or too low), `site-not-found` (404), `validation` (400),
  `idempotency-key-missing` (400), `idempotency-key-reused` (422), `identity-provider-unavailable` (503).
- **Creating POSTs** take an `Idempotency-Key` header, 1 to 200 printable ASCII characters, kept per User
  for 24 h after the request once the creation completes. A request still pending (Keycloak was down)
  keeps its key until a retry completes it. A retry returns the original result; the same key with a
  different request answers 422.
- **JSON** is camelCase with enums as strings; absent optional fields are omitted.
- Resources are plural nouns under `/sites/{siteId}/...`. The Site ID is the Keycloak Organization ID.

The Server's tests compare the endpoints it maps with the operations here, including each access
rule, so the two cannot drift.

## Clients

- **TypeScript** (`packages/ts/api-client`): `src/schema.ts` is generated from this file by
  `openapi-typescript`; regenerate with `pnpm --filter @coldframe/api-client generate`. A test fails
  when the committed schema is stale.
- **Kotlin** (`packages/kt/core`, `api/`): the DTOs are hand-written (openapi-generator's
  multiplatform output does not fit `explicitApi()`, Ktor 3.6 and the value-result style). A jvmTest
  parses this file and fails if an operation, path, method, header or property the core uses is
  missing here.
