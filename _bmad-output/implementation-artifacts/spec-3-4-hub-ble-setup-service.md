---
title: 'Story 3.4: Hub BLE setup service'
type: 'feature'
created: '2026-09-29'
baseline_revision: '2bcaa830cf5fd8d7220b2c7a894103bf5bcbf585'
status: 'awaiting-operator'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md'
warnings: ['oversized']
operator_actions:
  - "On a bench ESP32-S3 that ran an older partition table, run `espflash erase-region 0xC000 0x2000`. Then flash `cargo run --release` (or `--features dev-mode` on a spare board) from apps/rs/hub and record the `setup code=` line the first boot prints."
  - "Follow docs/bench/hub-setup-checklist.md with `coldframe-setup-client` (tests/rs/setup-client) on a Linux machine with BlueZ. Confirm that `scan` finds `Coldframe Hub XXXX`, that a run with a wrong setup code is refused with `wrong setup code`, and that a full setup on a real WPA2 network ends with CONNECTED."
  - "During that run, confirm the idle connection drops after 300 s and the Hub advertises again. After provisioning, confirm `scan` no longer finds the Hub in the same boot and after a reboot, and that the reboot prints no setup code. Record the logged free heap; it passes at 32768 bytes or more."
  - "Record the results (date, board, Device ID, pass or fail, free heap) in the Result table of docs/bench/hub-setup-checklist.md and commit it."
deferred:
  - summary: >-
      A wrong setup code gets a reply sealed under keys derived from the real code, which gives a nearby attacker an offline test for the 40-bit code.
    evidence: |-
      After SessionHello the attacker knows the X25519 shared secret. The Hub then answers the attacker's first sealed message with a SetupError sealed under HKDF(real code, shared secret). Checking the Poly1305 tag for each of the 2^40 candidate codes is offline work, fast on GPUs. setup.proto (Story 3.1) mandates this reply.
      A second route exists by design: X25519 with the code in the HKDF salt is not a PAKE. A fake Hub can take the app's first sealed message and brute-force it the same way.
      Closing this needs an AD-25 decision: a real PAKE (for example CPace or SPAKE2) or a much longer code. As a stop-gap, the Hub could answer a wrong code with random bytes of the same length, but that means changing the setup.proto rule.
    location: >-
      packages/rs/setup/src/session.rs (on_sealed, WrongSetupCode branch); packages/proto/coldframe/setup/v1/setup.proto (header comment)
    severity: high
  - summary: >-
      The esp-radio AuthenticationMethod and DisconnectReason mappings in the Hub Wi-Fi adapter are not checked by any automated test.
    evidence: |-
      board/wifi.rs maps esp-radio types. apps/rs/hub is outside the workspace and CI, and MockWifi bypasses these functions. A wrong mapping would report a wrong password as INTERNAL, or refuse transition-mode networks, without any test failing. Fix by mirroring the reason codes into packages/rs, or by making the bench wrong-password step mandatory.
    location: >-
      apps/rs/hub/src/board/wifi.rs (security, join_error)
    severity: medium
---

<intent-contract>

## Intent

**Problem:** An unprovisioned Hub has no way to receive Wi-Fi credentials, a Site or the Server's enrolment key. The AD-25 setup protocol exists only as `packages/proto/coldframe/setup/v1/setup.proto` and the `coldframe_crypto::setup` session. No Rust Protobuf code exists (`packages/rs/protocol` is planned but empty), no GATT service exists, and no setup code (PoP code) is ever generated.

**Approach:**
- Generate no_std micropb types from `setup.proto` in a new `packages/rs/protocol` crate.
- Put the device-side setup logic in a new host-tested crate, `packages/rs/setup`. It holds the setup-code store, BLE framing and the session state machine, over new `coldframe-hal` traits for the BLE link and Wi-Fi plus the existing `Flash`/`Trng`.
- Implement those traits in `apps/rs/hub` on trouble-host and esp-radio.
- Add a btleplug desktop client in `tests/rs` for the bench checklist.

## Boundaries & Constraints

**Always:**
- **Setup code (PoP code).** 8 characters from the Crockford base32 alphabet `0123456789ABCDEFGHJKMNPQRSTVWXYZ` (40 bits), drawn from the TRNG once. It is stored as record `b"CFPC" ‖ 0x01 ‖ code[8] ‖ SHA-256(prefix‖code)[0..4]` at offset 0 of the new `cf_setup` partition.
  - It is printed as one serial line `setup code=K7Q492MX` when generated, and again on every boot while the Hub is unprovisioned. It is never printed after provisioning, never logged elsewhere, and never put in any BLE message.
  - A corrupt record halts the Hub (`SetupStoreError::CorruptSetupCode`); it is never silently regenerated.
- **Provisioning record.** At offset 0x1000 of `cf_setup`, in its own sector: `b"CFWP" ‖ 0x01 ‖ u8 len ‖ ssid ‖ u8 len ‖ password ‖ u8 len ‖ site_id ‖ SHA-256(prefix‖…)[0..4]`.
  - It is written only after a successful join, so a failure changes nothing on the Hub.
  - An erased sector means unprovisioned. A corrupt one is treated as unprovisioned and logged as `corrupt provisioning record`.
- **Advertising.** The Hub advertises the setup service while unprovisioned and never after the provisioning record exists.
- **Transport contract** (documented in `packages/proto/README.md`, constants in `coldframe-setup`):
  - Service `c01d0001-5e70-4c0d-8f00-00000000c0de`, write characteristic (app → Device) `c01d0002-5e70-4c0d-8f00-00000000c0de`, notify characteristic (Device → app) `c01d0003-5e70-4c0d-8f00-00000000c0de`.
  - Every GATT write or notification is `header(1) ‖ fragment`, where header `0x01` marks the last fragment of a frame and `0x00` means more follow. The fragment is at most ATT MTU − 4 bytes.
  - A reassembled frame is ≤ `MAX_FRAME` (a const that is compile-time asserted ≥ the micropb `MAX_SIZE` of `SealedSetupMessage`).
  - The first frame of a connection is `SessionHello`. The Device answers `SessionHelloReply` with a fresh TRNG X25519 key, and every later frame is a `SealedSetupMessage`.
- **Session rules** (Hub):
  - `IdentityRequest` and `WifiScanRequest` may come at any time.
  - `SiteBinding` needs a non-empty `site_id` ≤ 36 bytes and no `lot_id`.
  - `EnrolmentRequest` needs a 32-byte key whose fingerprint equals `coldframe_crypto::hpke::fingerprint`. It is answered with `seal_enrolment` using 32 fresh TRNG bytes as `ikm_e`.
  - `WifiConfig` is accepted only after a `SiteBinding` and an `EnrolmentResponse` in this session. The Device scans, takes the strongest BSSID for that SSID, and joins it unless the network is WPA3-only or unsupported.
- The session crypto is only `coldframe_crypto::setup::SetupSession` (`Role::Device`). No other key derivation is allowed.
- Firmware logic lives in `packages/rs/*` and names no esp type; only `apps/rs/hub/src/board/*` touches esp crates (AD-24). `coldframe-protocol` and `coldframe-setup` are `no_std` without `alloc`, have `rust-version = "1.95"`, and use `unsafe_code = forbid`.
- Keys, the setup code, Wi-Fi passwords and decrypted payloads never appear in logs, `Debug` output or errors. Generated types that can hold a password (`WifiConfig`, `SetupMessage` and its body enum) get `no_debug_impl`.
- Pin exact versions (AD-15):
  - micropb and micropb-gen 0.6.0, protox 0.9.1, heapless 0.9;
  - trouble-host 0.7.0 with features `default-packet-pool-mtu-255` and `derive`, versions as in the spike's `Cargo.lock`;
  - esp-radio features gain `ble` and `coex`;
  - btleplug 0.13.3.
  - No `protoc` dependency.

**Never:**
- No Server reachability, DHCP-dependent checks, SNTP/TLS, heartbeat or re-join logic (Story 3.5). `NO_SERVER` is never produced here.
- No app, KMP or Kable code (Story 3.6). No Node behaviour, although the crate must not preclude it.
- No `setup.proto` field changes. Comments and README edits are allowed.
- No on-device tests or firmware builds in CI.
- No flash encryption or secure boot.
- Don't edit `_bmad-output/implementation-artifacts/sprint-status.yaml`.
- Never commit real Wi-Fi credentials or setup codes.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| First boot | `cf_setup` erased | Code generated from TRNG, record written, `setup code=…` printed, advertising | none |
| Later boot, unprovisioned | Valid CFPC, no CFWP | Same code reprinted, no flash write, advertising | none |
| Provisioned boot | Valid CFWP | No advertising, code not printed | none |
| Happy session | Code from vector `setup[0]`, app key `appPrivateKey`, Device TRNG yields `hubPrivateKey` then `enrolment[0].ikmE`, identity from `enrolment[0].rootKey` | Reply key = `hubPublicKey`. Identity carries the Device ID, `HUB` and the firmware version. The scan list is deduped per SSID, strongest first, ≤16 entries, WPA3-only as `WIFI_SECURITY_WPA3_ONLY`. The enrolment `enc`/`ciphertext` equal the vector. `WifiConfig` gives `CONNECTED` and a CFWP record with ssid, password and site | none |
| Wrong code | App uses `wrongPopCode` | Device sends one sealed `SetupError` under its own keys, then disconnects; app `open` gives `Error::WrongSetupCode`. Nothing stored | session ends |
| Wrong Wi-Fi password | Join fails as wrong password | `WIFI_STATUS_WRONG_PASSWORD`, nothing stored, session stays open for a retry | none |
| SSID not heard / WPA3-only / other | No scan match / `Wpa3Only` or `Other` | `NETWORK_NOT_FOUND` / `UNSUPPORTED_SECURITY`, no join attempted | none |
| Out of order | `WifiConfig` before binding+enrolment; Device → app message types sent by the app; `lot_id` on a Hub | `SetupError UNEXPECTED_MESSAGE` (the `lot_id` case: `MALFORMED_MESSAGE`) | session continues |
| Malformed | Undecodable sealed plaintext, `protocol_version` ≠ 1, bad field length, oversize frame, bad header byte | `SetupError MALFORMED_MESSAGE` (sealed once a session exists); a malformed `SessionHello` disconnects | partial frame dropped |
| Fingerprint mismatch | Fingerprint of another key | `SetupError FINGERPRINT_MISMATCH`, nothing sealed | session continues |
| Tamper/replay | Later message fails to open, or counter not increasing | Disconnect without reply | session ends |
| Idle timeout | No write for `SESSION_IDLE_TIMEOUT` (300 s) | Disconnect, back to advertising | session ends |
| Join failed otherwise | `JoinError::Failed` | `SetupError INTERNAL` | session continues |

</intent-contract>

## Code Map

- `packages/proto/coldframe/setup/v1/setup.proto` -- message set and wrong-code rule (header comment). `packages/proto/README.md` -- gets a "BLE transport" section (UUIDs, fragment header, frame order, Hub message order) and the Rust row.
- `packages/rs/crypto/src/setup.rs` -- `SetupSession::{new, seal, open}`, `Role::Device`; `open` maps the first failure to `Error::WrongSetupCode`. `src/hpke.rs:178-212` -- `seal_enrolment(server_pub, ikm_e, &DeviceKeys) -> SealedEnrolment{device_id, enc, ciphertext[48]}`, `fingerprint(&[u8;32]) -> [u8;64]` lowercase hex. `src/keys.rs` -- `DeviceKeys::from_root_key`, `DeviceId::{as_bytes,to_hex}`. `src/identity.rs` -- CFID record pattern and `IdentityError` style to mirror.
- `packages/rs/hal/src/{lib,radio,flash,rng}.rs`, `src/mock.rs` -- trait and error style (small `Copy` enums, `Display`). `MockTrng::with_bytes(&radio, bytes)` is scripted sequentially (mock.rs:302), and `MockFlash` has NOR semantics.
- `packages/crypto-spec/vectors.json` -- `setup[0]`: `popCode`, `wrongPopCode`, `appPrivateKey`, `hubPrivateKey`, `hubPublicKey`. `enrolment[0]`: `rootKey`, `recipientPublicKey`, `fingerprint`, `ikmE`, `deviceId`, `enc`, `ciphertext`. `tests/rs/crypto/tests/common/mod.rs` -- vector loader to copy.
- `packages/openapi/coldframe.openapi.json` `EnrolDeviceRequest` -- `{deviceId, kind, enc, ciphertext}`, where the bytes are base64url without padding. The desktop client prints this body.
- micropb 0.6.0 facts (verified in a scratch crate):
  - build.rs: `protox::Compiler::new([proto_root])` then `.open_file(..)` and `.encode_file_descriptor_set()`, written to `OUT_DIR`, then `micropb_gen::Generator::{use_container_heapless, configure(path, Config::new().max_bytes(n)/.max_len(n).no_debug_impl(true)), calculate_max_size(true), compile_fdset_file}`.
  - Deps: `micropb` with `default-features = false`, features `encode`, `decode`, `enable-64bit` (needed for `uint64`) and `container-heapless-0-9`; plus `heapless` 0.9.
  - Include the generated file in a module with `#[allow(clippy::all, clippy::pedantic, nonstandard_style, unused, missing_docs)]`. It contains no `unsafe`.
  - Module path: `coldframe_::setup_::v1_`. The oneof is `SetupMessage_::Body`. Enums are newtypes with consts (`WifiSecurity::Wpa3Only`). `optional lot_id` uses a hazzer (`lot_id()`). `decode_from_bytes` merges into `Default`.
  - Capacities: ssid 32, password 64, device_id 8, keys and enc 32, ciphertext (enrolment) 48, fingerprint 64, site_id and lot_id 36, networks 16; size `SealedSetupMessage.ciphertext` from the `SetupMessage` max plus 16.
- trouble-host 0.7.0 (spike `apps/rs/spike-hub-radio/src/bin/hub.rs:241-850` is the template):
  - `#[gatt_server]`/`#[gatt_service(uuid=..)]` with `heapless::Vec<u8, 248>` characteristics (`write` and `notify`). `WriteEvent::with_data(|offset, data|)`, then `event.accept()?.send().await`. `Characteristic::notify_raw(conn, &buf[..n], false)`.
  - `conn.raw().att_mtu()` gives the MTU (default 23; up to 251 when the client asks).
  - Advertising: `AdStructure::{Flags, CompleteServiceUuids128(&[uuid_u128.to_le_bytes()])}` in `adv_data`, `CompleteLocalName` in `scan_data`.
  - Host: `HostResources<_, DefaultPacketPool, 1, 2>`, `trouble_host::new(ExternalController::<_,1>::new(BleConnector::new(peripherals.BT, Default::default())?), &mut res)`. Create BLE before `WifiController::new` (coex).
- esp-radio 1.0.0-beta.1 Wi-Fi:
  - Scan: `WifiController::scan_async(&ScanConfig::default())` scans all channels actively and returns an alloc `Vec<AccessPointInfo{ssid, bssid, channel, signal_strength, auth_method: Option<AuthenticationMethod>}>`.
  - `AuthenticationMethod` mapping:
    - `None` → Open.
    - `Wpa`, `Wpa2Personal`, `WpaWpa2Personal` → Wpa2Personal.
    - `Wpa2Wpa3Personal` → Wpa3Transition.
    - `Wpa3Personal`, `Wpa3ExtPsk`, `Wpa3ExtPskMixed` → Wpa3Only.
    - Everything else (WEP, OWE, enterprise, WAPI, DPP) → Other.
  - Join: `set_config(&Config::Station(StationConfig::default().with_ssid(..).with_bssid(..).with_channel(..).with_authentication(AuthenticationMethodConfig::{Open | Wpa2Personal(pw)})))`, then `connect_async()`. It is `Err(ConnectionError::Failed(DisconnectedInfo{reason}))` on failure:
    - reasons 2, 14, 15, 202, 204 → WrongPassword;
    - 201 → NotFound;
    - 210, 211 → Unsupported;
    - otherwise Failed.
  - Don't scan during a connect. esp-radio has no WPA3-SAE station config.
- `apps/rs/hub/src/main.rs` -- boot sequence to extend. `src/board/flash.rs` -- one `FlashStorage` in a `StaticCell` plus `PartitionError`; generalise it so `cf_ident` and `cf_setup` share one storage. `src/board/{rng,radio}.rs` -- adapters. `partitions.csv` -- nvs `0x9000`/`0x5000`, `cf_ident` `0xE000`. `Cargo.toml` -- exact pins, `unsafe_code = deny`.
- `Cargo.toml` (workspace) -- add members `packages/rs/{protocol,setup}` and `tests/rs/{protocol,setup,setup-client}`. `.github/workflows/ci.yml:157-188` -- the rust job builds and tests the workspace. Don't change ci.yml; the client uses vendored libdbus (below).
- Docs: `apps/rs/README.md`, `packages/rs/README.md`, `apps/rs/hub/README.md`, `docs/quickstart.md`, `docs/bench/hub-identity-checklist.md` (style template).

## Tasks & Acceptance

**Execution:**
- `packages/rs/protocol/{Cargo.toml,build.rs,src/lib.rs}` -- `coldframe-protocol`: generate `setup.proto` as described above. Re-export `setup_v1`. Also export `SETUP_MESSAGE_MAX_SIZE` and `SEALED_SETUP_MESSAGE_MAX_SIZE` consts.
- `packages/rs/hal/src/{ble,wifi}.rs`, `lib.rs`, `mock.rs`, `radio.rs` doc -- async traits (`#[allow(async_fn_in_trait)]`, single-threaded executor).
  - `SetupLink`: `accept` (advertise until connected), `receive(buf, timeout_ms) -> Result<usize, LinkError>` (one write payload), `send(payload)`, `max_payload()`, `disconnect`. `LinkError`: `Disconnected`, `Timeout`, `Transport`.
  - `Wifi`: `scan(&mut [AccessPoint]) -> Result<usize, WifiError>` and `join(ssid, password, bssid, channel) -> Result<(), JoinError>`. `AccessPoint` holds ssid bytes and len, bssid, channel, rssi and `Security`. `Security` is `Open`, `Wpa2Personal`, `Wpa3Transition`, `Wpa3Only` or `Other`. `JoinError` is `WrongPassword`, `NotFound`, `Unsupported` or `Failed`.
  - Mocks: `MockSetupLink` delivers scripted incoming payloads and records sent ones, with a peer callback `FnMut(&[u8]) -> Vec<Vec<u8>>` so a test can answer each send. `MockWifi` takes a scripted scan and a join outcome per SSID/password, and counts joins.
- `packages/rs/setup/{Cargo.toml,src/lib.rs,code.rs,store.rs,framing.rs,session.rs,service.rs,app.rs}` -- `coldframe-setup`, depending on crypto, hal and protocol:
  - `code`: generate and validate.
  - `store`: CFPC and CFWP over `Flash`; `SetupStoreError` holds no secrets.
  - `framing`: `Reassembler` and fragment iterator.
  - `session`: the state machine per the matrix, emitting replies and actions without doing I/O.
  - `service`: `async fn run_setup(link, wifi, trng, flash, keys, firmware_version) -> Result<Provisioned, SetupError>` loops accept → session until a CFWP is written, then finishes the session (disconnect or timeout) and returns.
  - `app`: the app-side client (hello, seal/open, framing, message helpers), used by tests and the desktop client.
  - Also the UUID constants, `MAX_FRAME` and `SESSION_IDLE_TIMEOUT_MS`.
- `tests/rs/protocol/**` -- round trip of every `SetupMessage` body; `MAX_SIZE` ≤ `MAX_FRAME`; decoding over-capacity strings fails.
- `tests/rs/setup/**` -- every I/O-matrix row through `run_setup` with mocks and a noop-waker `block_on` (`std::task::Waker::noop`), plus the vector assertions. Also unit coverage for code, store and framing: first, later and provisioned boot, corrupt records, fragments at MTU 23 and 251.
  - Also an image guard: a test scanning `apps/rs/hub/{src/**,Cargo.toml,.cargo/config.toml,build.rs?}` fails on any `env!`/`option_env!` other than `CARGO_PKG_*` and on any `[env]` table (FR1).
- `tests/rs/setup-client/{Cargo.toml,src/main.rs}` -- `coldframe-setup-client` binary on btleplug 0.13.3 and tokio, depending directly on `libdbus-sys` with the `vendored` feature (no system libdbus).
  - `scan` lists Hubs advertising the service.
  - `setup --code --ssid --password --site --enrolment-key <base64url> [--fingerprint]` requests MTU implicitly, runs identity, scan list, site, enrolment and Wi-Fi config, prints each result and prints the `EnrolDeviceRequest` JSON for `POST /sites/{siteId}/devices`. A wrong code exits non-zero with `wrong setup code`.
- `apps/rs/hub/{Cargo.toml,Cargo.lock,partitions.csv,src/main.rs,src/board/{ble,wifi,flash,mod}.rs}`:
  - Add trouble-host, heapless 0.9, embassy-sync/futures and `coldframe-{protocol,setup}`, and the esp-radio `ble`/`coex` features.
  - Partitions: nvs `0x9000`/`0x3000`, `cf_setup` data `undefined` `0xC000`/`0x2000`, `cf_ident` unchanged.
  - Board adapters: `BoardSetupLink` bridges the trouble-host GATT task through embassy-sync channels. It advertises as name `Coldframe Hub XXXX` (the first 4 Device ID hex, uppercase). `BoardWifi` uses the mappings in the Code Map.
  - Boot: after identity, load or create the code (print rule), then run `run_setup` when unprovisioned. Log `setup provisioned` (no ssid or site) and idle.
- `apps/rs/hub/check-image.sh` -- builds release with canary env vars (`WIFI_SSID`, `WIFI_PASSWORD`, `SSID`, `PASSWORD`, `CF_WIFI_SSID`), runs `espflash save-image`, and fails if a canary appears in the ELF or the image.
- `docs/bench/hub-setup-checklist.md` -- bench procedure: flash, record the code, `scan`, a wrong-code run refused, a full setup, a reboot showing no advertising and no code, a Result table. The READMEs, `packages/proto/README.md` and `docs/quickstart.md` get updated.

**Acceptance Criteria:**
- Given host tests with the hal mocks, when `cargo test --workspace --locked` runs, then every I/O-matrix row passes, and the happy path's Session reply key and enrolment `enc`/`ciphertext` equal the crypto-spec vectors.
- Given `packages/rs/{protocol,setup}`, when inspected, then neither depends on an esp crate, and both build `no_std` (`cargo build -p coldframe-protocol -p coldframe-setup --locked`).
- Given `apps/rs/hub`, when `cargo build --release` and `cargo build --features dev-mode` run with the esp toolchain, then both compile, and `check-image.sh` passes (no canary in the image).
- Given a bench Hub and `coldframe-setup-client`, when the operator follows `docs/bench/hub-setup-checklist.md`, then a full setup completes and a wrong code is refused (manual, operator action).

## Spec Change Log

## Review Triage Log

### 2026-09-29 — Review pass
- verdicts: 36 findings — high 3, medium 7, low 25, false 1, maybe-false 0
- findings:
  - `[medium]` `[patch]` (verification-gap) A failed provisioning-record write inside `run_setup` is untested. Patched with a session test for a failed persist: INTERNAL is returned, the Hub is not provisioned, and the sector stays erased.
  - `[low]` `[patch]` (verification-gap) No test joins a WPA2/WPA3 transition network. Patched with a `mixed` join test.
  - `[medium]` `[defer]` (verification-gap) The esp-radio auth and reason mappings have only an optional manual check. They take esp types, which are kept out of host tests and CI; deferred.
  - `[low]` `[patch]` (verification-gap) The client's EnrolDeviceRequest body is unchecked against OpenAPI. Patched with a unit test built from the enrolment[0] vector.
  - `[low]` `[reject]` (verification-gap, other) GATT writes over 248 bytes are truncated and are dropped when the queue is full. The ATT MTU caps writes at 248, the app waits for each reply before writing again, and prepared writes are disabled. Rejecting with an ATT error would add branches for a case normal use does not reach.
  - `[high]` `[patch]` (blind) `BoardSetupLink::send` hangs forever if the central drops while the NOTIFY queue is full. Confirmed: after the disconnect nothing drains the queue. Patched so `send` returns Disconnected.
  - `[low]` `[reject]` (blind) A dead BLE task is never reported and `accept` waits forever. It is reachable only if static advertising data does not fit or the GATT server fails to build, neither of which happens in normal use.
  - `[low]` `[reject]` (blind) One nearby central can hold the only connection slot using the 300 s idle timeout. A nearby attacker can deny BLE service anyway, for example by jamming, and a separate handshake timeout adds a branch for no real gain.
  - `[medium]` `[patch]` (blind) The session stays live after CONNECTED, so a second WifiConfig re-joins and re-persists. Patched: after provisioning, mutating messages get UNEXPECTED_MESSAGE.
  - `[low]` `[reject]` (blind) SiteBinding has no success reply, so the bench client can misattribute a binding error. It needs a failing binding, but the client always sends a valid Site; the proto README documents the rule for 3.6.
  - `[low]` `[reject]` (blind) SCAN_CAPACITY 32 before dedup can miss an SSID in dense areas. Only an SSID weaker than the 32 strongest access points is lost, and such a network is too weak to join well.
  - `[low]` `[patch]` (blind) The FR-1 guards cover less than the README claims. Patched: the guard also scans the linked packages/rs crates, and the README wording is softened.
  - `[low]` `[reject]` (blind) Writes are acknowledged even when dropped. Same root cause and reason as the truncation row.
  - `[low]` `[reject]` (blind) The client takes the Wi-Fi password on the command line. It is a bench tool run by the operator on their own machine, and the checklist warns about it.
  - `[low]` `[reject]` (blind) Setup-code aliases (O/I/L) are not normalized. Input normalization belongs to the app (3.6, "format-agnostic"); the bench operator copies the code from serial.
  - `[low]` `[reject]` (blind) Secrets are wiped inconsistently (password strings, records, code). Wiping is best-effort as in 3.2, and a zeroize dependency or Drop impls on every type are more than a direct fix.
  - `[medium]` `[patch]` (blind) The heap budget (≥ 32 KiB free) is never measured. Patched: free heap is logged after setup and in the uptime line, and the checklist gains a step for it.
  - `[low]` `[patch]` (blind) The bench checklist misses on-device behaviours. Patched with steps for advertising stopping after provisioning, coex during setup, and the idle timeout.
  - `[medium]` `[patch]` (blind) The persist-failure path is untested. Grouped with the verification-gap persist row; same patch.
  - `[low]` `[reject]` (blind) A non-hex 64-character password reaches the driver. It is rare, and it fails with a wrong-password result anyway.
  - `[low]` `[patch]` (blind) A provisioned reboot never re-joins Wi-Fi, and this is undocumented. Patched with a README note that re-join comes in Story 3.5.
  - `[high]` `[patch]` (edge) `send` hangs when the app disconnects with the NOTIFY queue full. Duplicate of the blind `send` row; same patch.
  - `[low]` `[reject]` (edge) EVENTS overflow drops a fragment silently. Same reason as the truncation row.
  - `[low]` `[reject]` (edge) A truncated or nonzero-offset write is fed on. The MTU caps writes and prepared writes are disabled; same reason as the truncation row.
  - `[low]` `[reject]` (edge) `notify_raw` can fail after `send` returned Ok. It is rare, the app times out on the missing reply, and a DROP_LINK on error adds a branch.
  - `[high]` `[defer]` (edge) The wrong-code sealed reply is an offline oracle for the 40-bit code. Confirmed real. setup.proto (3.1) mandates the reply, and the handshake is not a PAKE, so a fake Hub can take the app's first message too. It needs an AD-25 decision, and the matrix row in this intent contract prescribes the reply; deferred as high.
  - `[medium]` `[patch]` (edge) A second WifiConfig after provisioning. Grouped with the blind session-live row; same patch.
  - `[medium]` `[patch]` (edge) A failed later persist leaves an erased sector while `run_setup` returns the earlier record. The same guard makes a second persist impossible.
  - `[low]` `[reject]` (edge) Power loss while the CFPC record is written on first boot halts the Hub with CorruptSetupCode. The intent contract says a corrupt record halts, so the fix would edit the spec. The README documents erasing cf_setup to recover.
  - `[low]` `[patch]` (edge) Stale NVS bytes at 0xC000 on boards that ran the old table read as a corrupt code. Patched with a checklist step to erase `cf_setup` before the first flash of an older board.
  - `[low]` `[reject]` (edge) If the strongest BSSID of an SSID is WPA3-only, a weaker supported BSSID is not tried. Mixed security under one SSID is very unusual.
  - `[low]` `[reject]` (edge) An open access point with a password, or a secured one with an empty password, gives a misleading status. User error in a rare case; the app knows the security from the scan list.
  - `[low]` `[reject]` (edge) Crockford aliases in `app.rs`. Duplicate of the blind alias row.
  - `[low]` `[reject]` (edge) The passphrase stays in RAM after drop. Duplicate of the blind wipe row.
  - `[false]` `[reject]` (edge) An uppercase fingerprint is rejected. The contract defines the fingerprint as lowercase hex, so rejecting uppercase is correct.
  - `[low]` `[reject]` (edge) The `run_setup` signature adds `code` compared with the spec. It is a recorded deviation with no caller harm, and the fix would edit the spec.

## Design Notes

- **Printing the code on unprovisioned reboots** satisfies "printed at first boot" and lets a builder who missed the first line still set up the Hub. Anyone who can read the serial console already holds the board. After provisioning the code is never shown again.
- **`CONNECTED` in this story** means Wi-Fi association and handshake succeeded. Story 3.5 extends `BoardWifi::join`, or the service, to confirm the Server before reporting `CONNECTED`, and to add `NO_SERVER`. `setup.proto` already documents the final meaning.
- **Message order** (binding and enrolment before `WifiConfig`) matches UX step 5. The app has enrolled the Device before the Hub first heartbeats (3.5), and a failed join leaves nothing stored.
- **No lockout after a wrong code.** 40 bits at one BLE session per attempt is out of reach of online guessing, and the session ends on every wrong code.
- **Residual risk:** the Wi-Fi password is stored in plain flash (no flash encryption, by design in 3.2).

## Verification

**Commands:**
- `cargo fmt --all --check && cargo clippy --workspace --all-targets --locked -- -D warnings` -- expected: clean
- `cargo test --workspace --locked` -- expected: all pass
- `cargo build -p coldframe-hal -p coldframe-crypto -p coldframe-protocol -p coldframe-setup --locked` -- expected: pass
- `cd apps/rs/hub && source ~/export-esp.sh && cargo build --release && cargo build --features dev-mode && cargo clippy --release -- -D warnings && cargo fmt --check && ./check-image.sh` -- expected: all pass

**Manual checks (if no CLI):**
- Operator runs `docs/bench/hub-setup-checklist.md` with a real ESP32-S3 and `coldframe-setup-client`.

## Auto Run Result

Status: awaiting-operator

**Summary.** Story 3.4 gives an unprovisioned Hub the AD-25 BLE setup service.
- New `coldframe-protocol`: micropb 0.6.0 types generated from `setup.proto`, with protox and no `protoc`.
- New `coldframe-setup`, built over new `coldframe-hal` traits (`SetupLink`, `Wifi`) with mocks:
  - an 8-character Crockford setup code, generated once, stored as a CFPC record in the new `cf_setup` partition, and printed on serial while the Hub is unprovisioned;
  - a CFWP provisioning record, written only after a successful join;
  - `header ‖ fragment` BLE framing;
  - the session state machine: a PoP-bound X25519 session, identity, a deduplicated scan list with WPA3-only rows flagged, Site binding, HPKE-sealed `K_dev`, and Wi-Fi join and result;
  - `run_setup`;
  - an app-side client.
- The Hub firmware implements the traits on trouble-host (GATT service `c01d0001-…`) and esp-radio Wi-Fi with coexistence. It advertises only while unprovisioned.
- A btleplug desktop client (`coldframe-setup-client`) and a bench checklist drive the manual acceptance.

**Files changed.**
- `packages/rs/protocol/**` (new): micropb build and types.
- `packages/rs/setup/src/{lib,code,store,framing,session,service,app}.rs` (new): setup logic.
- `packages/rs/hal/src/{ble,wifi}.rs` (new), plus `lib.rs`, `mock.rs` and `radio.rs`: the traits and `MockSetupLink`/`MockWifi`.
- `tests/rs/protocol/**`, `tests/rs/setup/**` and `tests/rs/setup-client/**` (new): protocol round trips; the I/O matrix, boot, framing and image-guard tests; the bench client.
- `apps/rs/hub/src/{main.rs,board/{ble,wifi,flash,mod,radio}.rs}`, `Cargo.toml`, `Cargo.lock`, `partitions.csv` and `.cargo/config.toml` (the `[env]` table is removed): firmware wiring, `cf_setup` at `0xC000`/`0x2000`, and a free-heap log.
- `apps/rs/hub/check-image.sh` (new): canary check on the built image.
- `docs/bench/hub-setup-checklist.md` (new); `packages/proto/README.md` (BLE transport section); `setup.proto` (comment only); `apps/rs/README.md`, `packages/rs/README.md`, `apps/rs/hub/README.md` and `docs/quickstart.md` updated.
- Workspace `Cargo.toml` and `Cargo.lock`: new members.
- `_bmad-output/implementation-artifacts/epic-3-context.md`: recompiled, because `epics.md` was newer.

**Deviations recorded here, not in the spec.**
- `run_setup` also takes `code: &SetupCode`.
- `SiteBinding` has no success reply.
- The wrong-code `SetupError` carries `MALFORMED_MESSAGE`.
- A `SetupError` sent by the app is ignored.
- Hidden and non-UTF-8 SSIDs are left out of the scan list.
- After provisioning, the session answers mutating messages with `UNEXPECTED_MESSAGE` (review patch).

**Review findings.** 36 findings: high 3, medium 7, low 25, false 1.
- **Patched: 13 rows in 8 entries.** At entry verdict: 1 high, 3 medium, 4 low.
  - high: the `send` hang on disconnect.
  - medium: nothing mutable after provisioning, plus a failed-persist test.
  - medium: the heap measurement.
  - low: the transition-network join test.
  - low: the enrolment-body test.
  - low: the image guard now covers the linked crates, and the README wording is fixed.
  - low: checklist and README steps (erase `cf_setup`, advertising stops, coexistence, idle timeout, no re-join until 3.5).
- **Deferred: 2.**
  - **high:** the wrong-code sealed reply is an offline oracle for the 40-bit setup code. A fake Hub can do the same with the app's first message, because the handshake is not a PAKE. `setup.proto` (3.1) mandates the reply. Closing this needs an AD-25 decision: a PAKE, or a longer code.
  - **medium:** the esp-radio reason and auth mappings have no automated test.
- **Rejected: 21, each reason in the Review Triage Log.**
  - Writes truncated or dropped (×4 rows), writes acknowledged when dropped, and `notify_raw` failing after `send`: not reachable in normal use.
  - A dead BLE task, and the single-slot denial of service.
  - SiteBinding misattribution, and the 32-access-point scan cap.
  - The password on the command line, and code aliases (×2, the app's concern).
  - Secret wiping (×2, best-effort).
  - The 64-character non-hex password.
  - A first-boot power loss halting the Hub (the intent says corrupt halts).
  - Mixed security under one SSID, and open-network or empty-password mismatch.
  - An uppercase fingerprint (false).
  - The `run_setup` signature (a spec edit).

**Follow-up review recommended: true.** A high entry was patched. The named unverified risk is that the new disconnect race in `BoardSetupLink::send` and the NOTIFY drain in `ble_task` have been compiled but never run on a board. No host test reaches `board/ble.rs`.

**Verification** (after the patches):
- `cargo fmt --all --check` passes.
- `cargo clippy --workspace --all-targets --locked -- -D warnings` is clean.
- `cargo test --workspace --locked`: 113 passed, 0 failed. The session tests cover every I/O-matrix row, including the vector checks on the happy path.
- `cargo build -p coldframe-hal -p coldframe-crypto -p coldframe-protocol -p coldframe-setup --locked` passes.
- In `apps/rs/hub` with the esp toolchain:
  - `cargo build --release` and `cargo build --features dev-mode` pass;
  - `cargo clippy --release -- -D warnings` and `cargo fmt --check` are clean;
  - `./check-image.sh` passes: no canary in the ELF or the image.
  - Before the review, the implementer planted `option_env!("WIFI_SSID")` as a negative control, and the script caught it.

**Residual risks.**
- **Not yet run on hardware:** advertising, notifications, MTU, join reason mapping, coexistence and heap. The operator bench run covers these.
- The offline code oracle (deferred, high).
- The Wi-Fi password is kept in plain flash, since flash encryption is excluded by design.
- CI now compiles vendored libdbus for the bench client, which needs a C compiler on the runner.
- A provisioned reboot stays off Wi-Fi until Story 3.5.

