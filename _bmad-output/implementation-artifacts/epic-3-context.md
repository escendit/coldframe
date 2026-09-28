# Epic 3 Context: Bring the Hub online

<!-- Generated from planning artifacts. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Simon adds a Hub from his phone over BLE using its setup code. The Hub joins his Wi-Fi, enrols with the Server under a hardware-bound identity, and appears on the Site within a minute. Devices then shows it online with its last-seen time. This epic sets up the whole Device trust and contract base: the shared wire and crypto contracts, the eFuse-rooted key hierarchy, sealed enrolment, the proof-of-possession BLE setup session, and HMAC-authenticated heartbeats. Epic 4 (Nodes, ESP-NOW, ingestion) reuses all of it. Firmware is tested host-side only, and on-device behaviour is checked by hand against a checklist.

## Stories

- Story 3.1: Wire and crypto contracts with shared test vectors
- Story 3.2: Hub firmware foundation with a hardware-bound identity
- Story 3.3: Server-side Device enrolment
- Story 3.4: Hub BLE setup service
- Story 3.5: Hub joins Wi-Fi and heartbeats to the Server
- Story 3.6: Add a Hub from my phone
- Story 3.7: See my Hub in Devices

## Requirements & Constraints

- **Hub provisioning:** only an Administrator or Owner can provision a Hub, from the iOS or Android app over BLE, and bind it to a Site. The firmware image contains no Wi-Fi credentials; they exist only after BLE provisioning. The Hub appears on the Site within 1 minute of the Wi-Fi credentials being sent. The setup session is encrypted and bound to the Device's proof-of-possession (PoP) code.
- **Device authentication:** Devices authenticate as Devices, not with Keycloak tokens. Every other endpoint needs a Keycloak token and per-Site Role authorization. New endpoints go into the generated authorization matrix.
- **TLS everywhere:** the Hub and the apps trust public roots only, and the Hub checks certificate validity dates.
- **Hub radio constraints:**
  - It scans all channels and joins the strongest BSSID for the SSID, never the first match, and re-joins after a disconnect.
  - WPA3-only networks are unsupported. Document this and show it in the UI.
  - Keep at least 32 KiB of free heap under full load. The BLE stack costs about 30 KB.
- **Test-first (red → green):**
  - Firmware has host-side unit tests only, with hardware behind traits and no on-device tests in CI.
  - The shared Kotlin core has unit tests with a mocked BLE layer.
  - The Server has integration tests on the Aspire AppHost via `Aspire.Hosting.Testing`.
  - Mobile has snapshot tests in light and dark themes.
  - BLE is tested at the protocol level, and a bench checklist covers real hardware.
- **Security risk (high):** custom crypto may be implemented inconsistently. Mitigate with shared vectors in three languages, plus negative tests for a wrong setup code, tampering and replay.
- **Accessibility and i18n:** follow the same rules as the rest of the product: screen-reader labels, text scaling, no status shown by colour alone, and externalised strings.

## Technical Decisions

- **Contracts (single source, generated):**
  - `packages/proto` holds the BLE setup messages (identity, Wi-Fi scan list, Wi-Fi config and result, Site binding, enrolment request and response). It also holds the sealed downlink with an empty reserved `commands` field. The same message set serves both Hub and Node.
  - `packages/openapi` holds `POST /device/heartbeat`, `POST /device/ingest` (placeholder), `GET /enrolment-key` and the enrolment endpoint.
  - `packages/crypto-spec` holds labels, algorithms, nonce layout and test vectors, emitted as constants for Rust, C# and Kotlin.
  - Every frame and envelope carries `protocol_version`, and the Server accepts the current and previous major. Changes within a major are additive only, and Protobuf field numbers are never reused. A CI compatibility check fails on breaking `.proto` or OpenAPI changes.
  - Devices use micropb, the Server uses Google.Protobuf, and the KMP core uses generated clients. The Hub's no_std JSON structs are hand-written but validated against golden fixtures generated from OpenAPI.
- **Key hierarchy:**
  - On first boot the Device generates a root key from the TRNG with the radio on. It burns it into a read-protected eFuse block (HMAC purpose ToUser) and never burns again.
  - The root derives `K_dev = HMAC-SHA256_eFuse(root, "coldframe/device/v1")`.
  - Purpose keys are `HKDF-SHA256(K_dev, label)` with the labels `seal/v1`, `ack/v1` and `hub-auth/v1`.
  - AEAD is ChaCha20-Poly1305, with a nonce built from the Device ID and a counter.
  - A documented dev-mode Cargo feature uses a software key and never touches eFuses. Release builds refuse to compile with it enabled.
- **Enrolment:**
  - The app fetches the Server's X25519 enrolment public key (with its fingerprint) over REST and writes it to the Hub over BLE.
  - The Hub returns `K_dev` sealed with HPKE (X25519-HKDF-SHA256-ChaCha20Poly1305). The app relays the ciphertext unread.
  - The Server stores `K_dev` encrypted at rest. The enrolment keypair lives in a fixed-name Secret.
  - The Device grain calls `Site.RegisterDevice(deviceId, kind)` and then persists `DeviceEnrolled`. The Site grain owns the roster, and the reply carries the Site's Pause state, which stays empty until Epic 8.
  - The Device ID is derived from the eFuse-bound identity.
  - A malformed request, a Member caller, or a Device already on another Site gets an RFC 9457 Problem Details response, and nothing is persisted.
- **BLE setup session:**
  - A custom GATT service built on trouble-host 0.7.0 (firmware) and Kable 0.45 (KMP core), not Espressif provisioning or Improv.
  - The session runs X25519, mixes in the PoP code via HKDF-SHA256, and encrypts every message with ChaCha20-Poly1305.
  - A wrong code fails the session with a distinct error.
  - The PoP code is unique per Device, printed to the serial console at first boot, persisted, and never sent over BLE.
  - The Hub advertises the service until it is provisioned.
- **Heartbeat:**
  - The Hub bootstraps its clock over SNTP before its first TLS connection. TLS uses mbedtls-rs 0.3.0 with `hook-wall-clock` and reqwless 0.14.0, and CI needs cmake and ninja to build it.
  - The Hub POSTs `/device/heartbeat` every 30–60 s. Each request carries an HMAC over method, path, body hash, timestamp and nonce, using the `hub-auth/v1` key. There is no mTLS.
  - The Server rejects a bad HMAC, a timestamp more than ±5 minutes off, and a replayed nonce. A valid heartbeat updates the Device grain's last-seen time.
  - The Hub adopts `serverTime` from the response for its own local timing only and never stamps data.
- **Firmware structure:**
  - Targets: esp-hal 1.2.2 (`unstable` for HMAC/AES/SHA) and esp-radio 1.0.0-beta.1 on ESP32-S3.
  - Logic depends only on the HAL-trait crate `packages/rs/hal` (radio, eFuse, HMAC, flash, ADC, RTC, GPIO) and its mocks, never directly on esp-hal types.
  - Key derivation lives in `packages/rs/crypto` and generated Protobuf in `packages/rs/protocol`.
- **Test infrastructure:**
  - A Device simulator library in `tests/cs`, built on the proto and crypto-spec packages, generates identities, seals enrolments, signs heartbeats and seals and verifies frames. All Server and E2E Device-path tests use it, never hand-built payloads.
  - The vector tests live in `tests/rs`, `tests/cs` and `tests/kt`. A desktop BLE client in `tests/rs` (for example btleplug) drives the bench checklist.
- **Conventions:**
  - Timestamps are UTC: ISO-8601 with `Z` in JSON, Unix milliseconds in Protobuf.
  - JSON uses camelCase, and absent optional fields are omitted.
  - Every creating `POST` accepts `Idempotency-Key`.
  - Keys, tokens and payloads never appear in logs.
  - Commits use Conventional Commits with a scope per app or package.

## UX & Interaction Patterns

- **Add a Hub (mobile only):** a full-screen, five-step modal in the Setup flow shell. It shows a "NN / 05" step counter, Cancel on step 1 and Back after that, and a confirmation when leaving mid-flow ("Nothing is saved on the Hub."). The screen stays awake during BLE steps, and focus moves to the step title on each step change. The steps are:
  1. **Scan:** Device candidate tiles, only for Devices in range and strongest first. Scanning continues, and there is no "already set up" tile.
  2. **Setup code:** a monospace field that auto-uppercases. It is validated by opening the BLE session and shows an ACCEPTED chip on success.
  3. **Wi-Fi:** networks as the Hub sees them. WPA3-only rows are hatched, cannot be selected, and give the reason inline. There is an "Other network" entry and a password field with reveal.
  4. **Site.**
  5. **Setup progress:** segments BLUETOOTH · WI-FI SENT · JOINING… · SERVER, advancing only on real events. It shows elapsed time and "Usually under a minute.", and times out at 90 s into an error screen. The flow then ends on the "Hub is online" success outcome with an ADD A NODE action.
- **Errors:** each has a specific message and recovery step, and errors never auto-dismiss. They cover:
  - Bluetooth off or permission denied
  - no Hub found in 30 s
  - lost connection
  - wrong setup code (the field is kept for re-entry)
  - wrong Wi-Fi password
  - Hub on Wi-Fi but unable to reach the Server

  No failure leaves the Hub or the Server half-configured.
- **Announcements:**
  - Progress advances and new scan candidates are announced politely, once per Device.
  - Errors and timeouts are announced assertively.
  - Setup never times out while a screen reader is speaking.
- **Devices list:** a "Hubs" section appears before "Nodes". Each row shows the Device ID (monospace), online state and last seen, and is read from a projection of Device-grain events. A Hub is never shown as online past its last heartbeat; the Silent Alert itself comes in Epic 7. Add a Hub is a ghost header button, on mobile only and for Admin+. On web, the Inline notice "Adding a Hub or Node needs the Coldframe mobile app." replaces the Add actions. Members see the list with admin controls hidden, not disabled.

## Cross-Story Dependencies

- **Within the epic:**
  - 3.1 comes first. Its contracts, constants, vectors and Device simulator feed every other story.
  - 3.2 (HAL traits, key derivation) comes before 3.4 and 3.5.
  - 3.3 (enrolment endpoints) comes before 3.4's enrolment step and 3.6.
  - 3.4 and 3.5 come before 3.6's end-to-end flow.
  - 3.5's heartbeats and 3.3's Device-grain events feed the 3.7 projection.
- **Earlier epics:**
  - Epic 1 provides the Site grain, the per-Site authorization and matrix, the event journal and projectors, design tokens, and mobile and web sign-in.
  - Epic 2 provides public-root TLS on the real domain and the fixed-name Server enrolment private key Secret.
- **Later epics:**
  - Epic 4 reuses the BLE setup protocol, the enrolment path, the sealed downlink and the simulator for Nodes, and replaces the `/device/ingest` placeholder.
  - Epic 7 builds the Hub Silent Alert on last-seen.
  - Epic 8 fills the Pause state in the `RegisterDevice` reply.
