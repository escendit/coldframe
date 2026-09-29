# packages/openapi

The REST contract, written before the code that serves it (AD-10):
[`coldframe.openapi.json`](coldframe.openapi.json), OpenAPI 3.1.

| Operation | Access (`x-coldframe-minimum-role`) | Since |
| --- | --- | --- |
| `GET /sites` | `Authenticated` | Story 1.8 |
| `POST /sites` | `Authenticated` | Story 1.6 |
| `GET /sites/{siteId}` | `Member` | Story 1.6 |
| `PATCH /sites/{siteId}` | `Owner` | Story 1.9 |
| `GET /sites/{siteId}/lots` | `Member` | Story 1.9 |
| `POST /sites/{siteId}/lots` | `Administrator` | Story 1.9 |
| `GET /sites/{siteId}/lots/{lotId}` | `Member` | Story 1.9 |
| `PATCH /sites/{siteId}/lots/{lotId}` | `Administrator` | Story 1.9 |
| `DELETE /sites/{siteId}/lots/{lotId}` | `Administrator` | Story 1.9 |
| `GET /enrolment-key` | `Authenticated` | Story 3.3 (contract: Story 3.1) |
| `POST /sites/{siteId}/devices` | `Administrator` | Story 3.3 (contract: Story 3.1) |
| `POST /device/heartbeat` | `Device` | Contract: Story 3.1; served since Story 3.5 |
| `POST /device/ingest` (placeholder) | `Device` | Contract: Story 3.1; served: Epic 4 |

An operation with `x-coldframe-planned: "<story>"` is in the contract ahead of the Server. The Server's
endpoint test requires that it is not mapped yet; the story that serves it removes the mark.

## Conventions

- **Authentication.** Every operation takes a Keycloak access token of the `coldframe` realm with
  audience `coldframe-server` (security scheme `bearer`). The User ID is the token's `sub`.
- **Device authentication.** `/device/*` operations take no Keycloak token (security scheme
  `deviceHmac`): the Hub signs each request with its `hub-auth/v1` key and sends `X-Coldframe-Device`,
  `X-Coldframe-Timestamp` (Unix ms, within ±300000 ms), `X-Coldframe-Nonce` (16 random bytes as hex,
  never reused) and `X-Coldframe-Signature`. The canonical string and its vectors are in
  [`packages/crypto-spec`](../crypto-spec).
- **Access.** Every operation declares `x-coldframe-minimum-role`: a `SiteRole` (`Owner`,
  `Administrator`, `Member`) on the Site named by `siteId`, `Authenticated` for any signed-in
  User, or `Device` for an authenticated Device. The Server reads the caller's Role from its identity
  projection, never from token claims.
- **Errors** are RFC 9457 Problem Details (`application/problem+json`) with a stable `type`,
  `urn:coldframe:problem:<slug>`: `unauthorized` (401), `forbidden` (403, the Site exists but the
  caller's Role is missing or too low), `site-not-found` (404), `lot-not-found` (404, no such Lot on
  this Site), `validation` (400), `idempotency-key-missing` (400), `lot-claimed` (409, a Node is
  assigned to the Lot), `idempotency-key-reused` (422), `identity-provider-unavailable` (503),
  `device-unauthorized` (401, Device authentication failed), `device-on-another-site` (409). The set
  grows with the API, so `ProblemDetails.type` is an `x-extensible-enum`.
- **Creating POSTs** take an `Idempotency-Key` header, 1 to 200 printable ASCII characters, kept per User
  for 24 h after the request once the creation completes. A request still pending (Keycloak was down)
  keeps its key until a retry completes it. A retry returns the original result; the same key with a
  different request answers 422.
- **JSON** is camelCase with enums as strings; absent optional fields are omitted.
- Resources are plural nouns under `/sites/{siteId}/...`. The Site ID is the Keycloak Organization ID;
  Lot IDs are UUIDv7. A removed Lot stays readable by ID with `removed: true`; lists omit it.

The Server's tests compare the endpoints it maps with the operations here, including each access
rule, so the two cannot drift. CI's `contracts` job fails a change that breaks this file for existing
clients (`oasdiff breaking --fail-on ERR`, see [`packages/proto`](../proto/README.md#checks)).

## Golden Hub fixtures

The Hub's `no_std` JSON structs are hand-written (`coldframe_uplink::json`), so they are checked
against fixtures generated from this file (AD-10, AD-24). `scripts/generate-fixtures.ts` writes
`fixtures/hub/` from the `deviceHeartbeat` request and 200 response schemas and their `examples`:
the minimal and full request, a response with and without a fraction of a second, a response with an
extra property the Hub must ignore, and `schemas.json` (each schema's properties and required keys).
`tests/rs/uplink` decodes every response fixture and encodes the request fixtures' values back to
the same JSON.

```sh
pnpm --filter @coldframe/openapi run generate   # after changing the heartbeat schemas or examples
pnpm --filter @coldframe/openapi run check      # CI: fails when a committed fixture is stale
```

## Clients

- **TypeScript** (`packages/ts/api-client`): `src/schema.ts` is generated from this file by
  `openapi-typescript`; regenerate with `pnpm --filter @coldframe/api-client generate`. A test fails
  when the committed schema is stale.
- **Kotlin** (`packages/kt/core`, `api/`): the DTOs are hand-written (openapi-generator's
  multiplatform output does not fit `explicitApi()`, Ktor 3.6 and the value-result style). A jvmTest
  parses this file and fails if an operation, path, method, header or property the core uses is
  missing here.
