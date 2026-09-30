# packages/rs

Rust crates that firmware shares and that build and test on the host.

| Folder | What | Arrives in |
| --- | --- | --- |
| `hal/` | `coldframe-hal`: hardware traits (radio, TRNG, eFuse, HMAC, flash, ADC, RTC, GPIO, the BLE setup link with a bounded `accept_within`, the Wi-Fi station with its link state, the IP uplink `Net` (DHCP, SNTP, one HTTPS `POST` at a time), `Timer`, and the Node's raw soil ADC `RawAdc` and BME680-style `EnvSensor`), `no_std` with no required dependencies; optional `hmac`/`sha2` and std behind the `mock` feature, which adds the mocks for host-side tests (AD-24) | Story 3.2, extended in 3.4, 3.5, 4.1 and 4.2 |
| `protocol/` | `coldframe-protocol`: micropb types generated at build time from `packages/proto` (protox, no `protoc`), heapless containers with fixed capacities, `encode`/`decode` helpers and the `MAX_SIZE` constants; `no_std`, no `alloc` | Story 3.4 |
| `setup/` | `coldframe-setup`: the BLE setup service (AD-25): setup code, the `cf_setup` records (provisioning record version 2 with the Server address), BLE framing, the Device session state machine with the Server check after the join, `run_setup` over the hal traits, the Node profile of the session and the Node's time-bounded setup window `run_node_setup` (enrolment only, no Wi-Fi, nothing stored), and the app-side client used by tests and the bench client; `no_std`, no `alloc` | Story 3.4, extended in 3.5 and 4.2 |
| `uplink/` | `coldframe-uplink`: the Hub's uplink (Story 3.5): `select_bssid` (strongest supported BSSID, H-1), reconnect `Backoff`, `HeartbeatSchedule` (30–60 s from the TRNG), `ServerUrl`, RFC 3339 `…Z` parsing and `MonotonicStamp` (AD-11), the hand-written heartbeat JSON on serde-json-core (checked against the OpenAPI golden fixtures, AD-10), `sign_heartbeat` (AD-12), `check_server` for setup and `Uplink::step`/`run`; `no_std`, no `alloc` | Story 3.5 |
| `sensing/` | `coldframe-sensing`: the Node's wake cycle (Story 4.1): `run_wake` (probe and divider switched on only while read, one shared `measured_at`, fixed slot order, sleep to the next 15-minute wake), `ReservedCounter` (a `u64` ceiling reserved in flash before use, two sectors of 32-byte records, AD-17) for `reading_seq` and the boot ID, the LiPo discharge curve, and the setup button's `classify_press`, `wake_plan`, `arm_button_wake` and `sleep_after` (Story 4.2); `no_std`, no `alloc` | Stories 4.1, 4.2 |
| `crypto/` | `coldframe-crypto`: key hierarchy, frame sealing and replay window, HPKE enrolment, BLE setup session, heartbeat signing, all from the constants [`packages/crypto-spec`](../crypto-spec) generates into `src/spec.rs`; the Device identity (find or burn the eFuse root, or the dev-mode flash root) over the `coldframe-hal` traits; `no_std`, no `alloc` | Stories 3.1 and 3.2 |

These crates are members of the Cargo workspace in the repository root. The Hub firmware links all
of them but `sensing`, and the Node firmware links all of them (`setup` brings in `uplink`, which the
Node never runs), with the `esp` toolchain, which is Rust 1.95. That is why they declare `rust-version = "1.95"`. None depends on an
esp crate.
Unit tests stay inline next to the code. Integration tests live in [`tests/rs`](../../tests/rs).
