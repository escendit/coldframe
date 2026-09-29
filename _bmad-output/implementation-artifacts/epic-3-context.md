# Epic 3 Context: Bring the Hub online

<!-- Generated from planning artifacts. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Simon adds a Hub from his phone over BLE using its setup code. The Hub joins his Wi-Fi, enrols with the Server under a hardware-bound identity, and appears on the Site within a minute; Devices then shows it online with its last-seen time. The epic lays the whole Device trust and contract base — shared wire/crypto contracts, the eFuse-rooted key hierarchy, sealed enrolment, the proof-of-possession BLE setup session and HMAC-authenticated heartbeats — which Epic 4 (Nodes) reuses. Firmware is tested host-side only; on-device behaviour is checked manually against a bench checklist.

## Stories

- Story 3.1: Wire and crypto contracts with shared test vectors
- Story 3.2: Hub firmware foundation with a hardware-bound identity
- Story 3.3: Server-side Device enrolment
- Story 3.4: Hub BLE setup service
- Story 3.5: Hub joins Wi-Fi and heartbeats to the Server
- Story 3.6: Add a Hub from my phone
- Story 3.7: See my Hub in Devices

## Requirements & Constraints

- Only an Administrator or Owner provisions a Hub, from iOS/Android over BLE, binding it to a Site. The firmware image holds no Wi-Fi credentials. The Hub must appear on the Site within 1 minute of the credentials being sent.
- Devices authenticate as Devices (no Keycloak tokens). All other endpoints need a Keycloak token plus per-Site Role authorization, and every new endpoint goes into the generated authorization matrix.
- TLS everywhere with public roots only; the Hub checks certificate validity dates.
- Hub radio: scan all channels and join the strongest BSSID for the SSID (never first match); re-join after disconnect. WPA3-only networks are unsupported — document it and show it in the UI. BLE costs ~30 KB heap; budget a single TLS connection (~40 KiB) and keep ≥32 KiB heap free.
- Test-first (red → green): firmware host-side unit tests with hardware behind traits (no on-device CI); KMP core unit tests with mocked BLE; Server integration tests on the Aspire AppHost; mobile snapshot tests in light and dark.
- High risk: custom crypto implemented inconsistently across languages. Mitigate with shared vectors in Rust, C# and Kotlin plus negative tests for wrong setup code, tampering and replay.
- Accessibility/i18n as elsewhere: screen-reader labels, text scaling, no status by colour alone, externalised strings.

## Technical Decisions

- **Contracts (single source, generated):** `packages/proto` (BLE setup messages — identity, Wi-Fi scan list, Wi-Fi config/result, Site binding, enrolment request/response — and the sealed downlink with an empty reserved `commands` field; same set serves Hub and Node). `packages/openapi` (`POST /device/heartbeat`, `POST /device/ingest` placeholder, `GET /enrolment-key`, enrolment endpoint). `packages/crypto-spec` (labels, algorithms, nonce layout, vectors → constants for Rust/C#/Kotlin). Every frame carries `protocol_version`; Server accepts current and previous major; changes within a major are additive, field numbers never reused; CI fails on breaking `.proto`/OpenAPI changes. Devices use micropb, Server Google.Protobuf, KMP generated clients; Hub no_std JSON structs are hand-written but checked against OpenAPI golden fixtures.
- **Key hierarchy:** first boot generates a root from the TRNG (radio on), burns it once into a read-protected eFuse block (HMAC purpose ToUser). `K_dev = HMAC-SHA256_eFuse(root, "coldframe/device/v1")`; purpose keys `HKDF-SHA256(K_dev, label)` for `seal/v1`, `ack/v1`, `hub-auth/v1`. AEAD is ChaCha20-Poly1305 with a Device-ID + counter nonce. A documented dev-mode Cargo feature uses a software key and never touches eFuses; release builds refuse to compile with it.
- **Enrolment:** app fetches the Server's X25519 enrolment public key + fingerprint over REST and writes it to the Hub over BLE; Hub returns `K_dev` sealed with HPKE (X25519-HKDF-SHA256-ChaCha20Poly1305); app relays it unread. Server stores `K_dev` encrypted at rest; enrolment keypair lives in a fixed-name Secret. Device grain calls `Site.RegisterDevice(deviceId, kind)` then persists `DeviceEnrolled`; the Site grain owns the roster and its reply carries Pause state (empty until Epic 8). Device ID derives from the eFuse-bound identity. Malformed/wrongly sealed requests, Member callers, or a Device already on another Site get RFC 9457 Problem Details and nothing is persisted.
- **BLE setup session:** custom GATT service (trouble-host 0.7.0 on firmware, Kable 0.45 in the KMP core) — not Espressif provisioning or Improv. X25519 with the PoP code mixed in via HKDF-SHA256; every message ChaCha20-Poly1305; a wrong code fails with a distinct error. PoP code is per-Device, printed to serial at first boot, persisted, never sent over BLE. Hub advertises until provisioned.
- **Heartbeat:** SNTP clock bootstrap before first TLS; TLS via mbedtls-rs 0.3.0 (`hook-wall-clock`) and reqwless 0.14.0 (CI needs cmake + ninja). POST `/device/heartbeat` every 30–60 s with an HMAC (`hub-auth/v1`) over method, path, body hash, timestamp and nonce; no mTLS. Server rejects bad HMAC, timestamp beyond ±5 min, replayed nonce; valid heartbeat updates Device-grain last-seen. Hub adopts `serverTime` for local timing only, never for stamping data.
- **Firmware structure:** ESP32-S3 on esp-hal 1.2.2 (`unstable` for HMAC/AES/SHA) and esp-radio 1.0.0-beta.1. Logic depends only on HAL traits in `packages/rs/hal` (radio, eFuse, HMAC, flash, ADC, RTC, GPIO) and their mocks; key derivation in `packages/rs/crypto`; generated Protobuf in `packages/rs/protocol`.
- **Test infrastructure:** a Device simulator library in `tests/cs` (identities, sealed enrolment, signed heartbeats, sealed frames/downlinks) — all Server and E2E Device-path tests use it, never hand-built payloads. Vector tests in `tests/rs`, `tests/cs`, `tests/kt`; a desktop BLE client in `tests/rs` (e.g. btleplug) drives the bench checklist.
- **Conventions:** UTC timestamps (ISO-8601 `Z` in JSON, Unix ms in Protobuf); camelCase JSON, absent optionals omitted; creating `POST`s accept `Idempotency-Key`; never log keys, tokens or payloads; Conventional Commits scoped per app/package.

## UX & Interaction Patterns

- **Add a Hub (mobile only):** full-screen five-step modal in the Setup flow shell with an "NN / 05" counter, Cancel on step 1 then Back, and a leave-confirmation ("Nothing is saved on the Hub."). Screen stays awake during BLE; focus moves to the step title on each change. Steps: (1) Scan — candidate tiles for in-range Devices, strongest first, scanning continues; (2) Setup code — monospace, auto-uppercase, validated by opening the BLE session, ACCEPTED chip on success; (3) Wi-Fi — networks as the Hub sees them, WPA3-only rows hatched and unselectable with inline reason, "Other network", password with reveal; (4) Site; (5) Progress — BLUETOOTH · WI-FI SENT · JOINING… · SERVER segments advance only on real events, elapsed time, "Usually under a minute.", 90 s timeout to an error screen; success ends on "Hub is online" with ADD A NODE.
- **Errors** never auto-dismiss and each has a specific message and recovery: Bluetooth off/permission denied, no Hub found in 30 s, lost connection, wrong setup code (field kept for re-entry), wrong Wi-Fi password, Hub on Wi-Fi but can't reach the Server. No failure leaves Hub or Server half-configured.
- **Announcements:** progress and new candidates polite (once per Device); errors and timeouts assertive; setup never times out while a screen reader is speaking.
- **Devices list:** "Hubs" section before "Nodes"; rows show Device ID (monospace), online state, last seen, from a projection of Device-grain events; never online past the last heartbeat. Add a Hub is a ghost header button, mobile-only, Admin+. On web an Inline notice "Adding a Hub or Node needs the Coldframe mobile app." replaces Add actions. Members see the list with admin controls hidden, not disabled.

## Cross-Story Dependencies

- 3.1 first: its contracts, constants, vectors and simulator feed every other story. 3.2 precedes 3.4 and 3.5. 3.3 precedes 3.4's enrolment step and 3.6. 3.4 and 3.5 precede 3.6's end-to-end flow. 3.3's Device-grain events and 3.5's heartbeats feed 3.7.
- Relies on Epic 1 (Site grain, per-Site authorization and matrix, event journal and projectors, design tokens, sign-in) and Epic 2 (public-root TLS on the real domain, fixed-name enrolment-key Secret).
- Epic 4 reuses the BLE protocol, enrolment path, sealed downlink and simulator for Nodes and replaces the `/device/ingest` placeholder; Epic 7 builds the Hub Silent Alert on last-seen; Epic 8 fills Pause state in the `RegisterDevice` reply.
