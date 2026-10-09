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
| `GET /sites/{siteId}/lots/{lotId}/history` | `Member` | Story 4.8 |
| `PATCH /sites/{siteId}/lots/{lotId}` | `Administrator` | Story 1.9 |
| `DELETE /sites/{siteId}/lots/{lotId}` | `Administrator` | Story 1.9 |
| `GET /enrolment-key` | `Authenticated` | Story 3.3 (contract: Story 3.1) |
| `POST /sites/{siteId}/devices` | `Administrator` | Story 3.3 (contract: Story 3.1); a Node's `lotId` since Story 4.2 |
| `POST /sites/{siteId}/devices/{deviceId}/move` | `Administrator` | Story 4.9 |
| `POST /sites/{siteId}/devices/{deviceId}/unassign` | `Administrator` | Story 4.9 |
| `POST /sites/{siteId}/sensors/{sensorId}/calibration` | `Administrator` | Story 5.1 |
| `GET /sites/{siteId}/sensors/{sensorId}/calibration` | `Administrator` | Story 5.2 |
| `GET /sites/{siteId}/sensors/{sensorId}/thresholds` | `Member` | Story 5.3 |
| `PUT /sites/{siteId}/sensors/{sensorId}/thresholds` | `Administrator` | Story 5.3 |
| `GET /sites/{siteId}/alerts` | `Member` | Story 6.2 |
| `POST /device/heartbeat` | `Device` | Contract: Story 3.1; served since Story 3.5 |
| `POST /device/ingest` | `Device` | Placeholder: Story 3.1; contract and served since Story 4.5 |

An operation with `x-coldframe-planned: "<story>"` is in the contract ahead of the Server. The Server's
endpoint test requires that it is not mapped yet; the story that serves it removes the mark. A planned
operation was never served, so it may still change: the compatibility check leaves it out. No operation
is planned today.

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
  `device-unauthorized` (401, Device authentication failed), `device-on-another-site` (409),
  `device-assigned` (409, the Node is in another Lot already), `device-not-found` (404, no such Node on this
  Site: moving or unassigning an unknown Device, a Hub or a Device of another Site), `ingest-unavailable` (503, no frame of an
  ingest envelope could be committed), `sensor-not-found` (404, no such Sensor on a Node of this Site),
  `calibration-not-delivered` (503, the Calibration is saved but the Node's Device grain has not acknowledged it
  yet; the Server keeps delivering it). Enrolling a Node with a `lotId` answers
  `lot-not-found` (404) for a Lot that is unknown, removed or of another Site, and `lot-claimed` (409)
  when the Lot already has a Node. The set grows with the API, so `ProblemDetails.type` is an
  `x-extensible-enum`.
- **Creating POSTs** take an `Idempotency-Key` header, 1 to 200 printable ASCII characters, kept per User
  for 24 h after the request once the creation completes. A request still pending (Keycloak was down)
  keeps its key until a retry completes it. A retry returns the original result; the same key with a
  different request answers 422. For `POST /sites/{siteId}/devices` the key covers the Device's
  registration on the Site only: a Node's `lotId` is checked against the Device on every request, and a
  refused claim persists nothing for the Device, so a retry with the same key may name another Lot.
- **Ingest.** `POST /device/ingest` takes `{frames: [...]}`: at most 32 sealed Node frames, each a
  serialized `SealedEnvelope` of [`packages/proto`](../proto) in standard base64 with padding, in a
  body of at most 16 384 bytes. It answers 200 whenever the envelope parses, with one
  `{status, downlink?}` per frame in request order: `stored`, `duplicate`, `rejected_auth`,
  `rejected_replay`, `rejected_time`, `unknown_device` or `retry` (snake case, as AD-9 names them).
  Only `stored` and `duplicate` carry a `downlink`, which the Hub relays to the Node unchanged. 400
  is only an envelope that does not parse, 401 a failed Hub authentication, 503 an envelope whose
  every frame would be `retry`.
- **Lot status.** Every `Lot` carries the Server's `status` (AD-14) and `statusSince`, the time it got
  that status. The supporting fields are present only where they apply: `lastReadingAt` (the newest
  `measured_at` of the Node's Readings since it was put in the Lot), `unknownCause` (`node` or `hub`,
  only for `unknown`), `pausedBy` (`device` and/or `site`, only for `paused`) and `pausedUntil` (the
  latest end of the Pause sources, absent when any of them has no end). `moisturePercent` (a multiple
  of 5) and `lowThresholdPercent` (the soil Sensor's effective low Threshold) are sent by the list and by
  Lot detail when the newest soil-moisture Reading was stored under a Calibration (Story 5.4); the low is
  absent when the Sensor has none. Clients render these fields; they never compute a
  status and never re-sort the list.
- **Lot detail and history.** `GET /sites/{siteId}/lots/{lotId}` alone also carries `node` (the Node's
  `deviceId`, `batteryPercent`, `charging` and `lastSeenAt` from its newest device report) and
  `sensors` (the newest Reading per Sensor), both only while the Lot holds a Node. The Server converts
  values: soil moisture is the raw count (`unit: raw`), or, for a Reading stored under a Calibration (Story 5.1),
  a percentage rounded to the nearest 5 (`unit: %`),
  temperature is in `°C`, humidity in `%`, gas resistance in `kΩ`. `GET /sites/{siteId}/lots/{lotId}/history`
  returns one entry per UTC day with Readings (`low`, `high`, `readingCount`), ascending (for soil moisture a
  page with Readings stored under a Calibration is in `unit: %`, from those Readings only, Story 5.4), paged by
  `from`/`to`, `limit` and an opaque `cursor`. Devices list items of Nodes add `lotName`,
  `batteryPercent` and `charging`.
- **Calibration.** `POST /sites/{siteId}/sensors/{sensorId}/calibration` takes `{dry?: {readingSeq}, wet?:
  {readingSeq}}`, each point naming a Reading the Server stored for the Sensor by its `reading_seq`; the raw value
  comes from that Reading. One point is kept until the other arrives; two points at least 16 raw counts apart
  (either orientation) save a Calibration with a new ID. It answers 200 `{calibrated, calibrationId?, dry?, wet?,
  pendingDry?, pendingWet?}` once the Node's Device grain holds the Calibration, 400 `validation` for an
  indistinct pair, a Reading the Sensor has not stored, or a Sensor without Calibration, 404 `sensor-not-found`,
  403 for a Member, and 503 `calibration-not-delivered` while the Device has not acknowledged it. Only Readings
  stored afterwards use the new Calibration. `GET` on the same path (Story 5.2, Administrator) answers 200
  `{calibrated, calibrationId?, dry?, wet?, pendingDry?, pendingWet?, readings}`, where `readings` are the Sensor's
  recent stored Readings (`readingSeq`, `rawValue`, `measuredAt`), newest first. Each Lot detail Sensor carries
  `sensorId` and `calibratable` (its Specification says `calibration: true`).
- **Thresholds.** `GET /sites/{siteId}/sensors/{sensorId}/thresholds` (Member, read-only) answers 200 `{unit, low:
  {kind, value?}, high: {kind, value?}, proposedLow?}`: `kind` is `default`, `override` or `cleared`, `value` the
  effective Threshold in the display unit (`%` for a calibrating Sensor, whole percent 0 to 100, otherwise `°C`, `%`
  or `kΩ`), `proposedLow` `Min + 20 % x (Max - Min)` when the Specification has no default low (never a proposed
  high). `PUT` (Administrator) takes `{low?, high?}` with sides `{kind, value?}` (an absent side stays) and answers
  the same 200, also when nothing changed (then nothing is saved); 400 `validation` for a high without a low, a low
  not below the high, a malformed side or a value out of range, 403 for a Member, 404 `sensor-not-found`. A saved
  change starts a new evaluation epoch for Threshold Alerts.
- **JSON** is camelCase with enums as strings; absent optional fields are omitted.
- Resources are plural nouns under `/sites/{siteId}/...`. The Site ID is the Keycloak Organization ID;
  Lot IDs are UUIDv7. A removed Lot stays readable by ID with `removed: true`; lists omit it.

The Server's tests compare the endpoints it maps with the operations here, including each access
rule, so the two cannot drift. CI's `contracts` job fails a change that breaks this file for existing
clients (`oasdiff breaking --fail-on ERR`, see [`packages/proto`](../proto/README.md#checks)).

## Golden Hub fixtures

The Hub's `no_std` JSON structs are hand-written (`coldframe_uplink::json`), so they are checked
against fixtures generated from this file (AD-10, AD-24). `scripts/generate-fixtures.ts` writes
`fixtures/hub/` from the `deviceHeartbeat` and `deviceIngest` request and 200 response schemas and
their `examples`. Heartbeat: the minimal and full request, a response with and without a fraction of
a second, and a response with an extra property the Hub must ignore. Ingest: a request without and
with frames, a response without results, with a stored and a rejected frame, with every status, and
with extra properties on the response and on every result. `schemas.json` holds each schema's
properties and required keys, the frame limit and the statuses. The generator refuses an ingest
example whose `downlink` is on another status than `stored` or `duplicate`. The ingest fixtures are
read by the Hub relay of Story 4.4 (`encode_ingest_request` and `IngestResponse`).
`tests/rs/uplink` decodes every response fixture and encodes the request fixtures' values back to
the same JSON. The Hub puts at most 8 frames into one request, a quarter of the contract's 32.

```sh
pnpm --filter @coldframe/openapi run generate   # after changing the heartbeat or ingest schemas or examples
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
