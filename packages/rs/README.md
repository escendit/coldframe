# packages/rs

Rust crates that firmware shares and that build and test on the host.

| Folder | What | Arrives in |
| --- | --- | --- |
| `hal/` | `coldframe-hal`: hardware traits (radio, TRNG, eFuse, HMAC, flash, ADC, RTC, GPIO, the BLE setup link, the Wi-Fi station with its link state, the IP uplink `Net` (DHCP, SNTP, one HTTPS `POST` at a time) and `Timer`), `no_std` with no required dependencies; optional `hmac`/`sha2` and std behind the `mock` feature, which adds the mocks for host-side tests (AD-24) | Story 3.2, extended in 3.4, 3.5 and Epic 4 |
| `protocol/` | `coldframe-protocol`: micropb types generated at build time from `packages/proto` (protox, no `protoc`), heapless containers with fixed capacities, `encode`/`decode` helpers and the `MAX_SIZE` constants; `no_std`, no `alloc` | Story 3.4 |
| `setup/` | `coldframe-setup`: the BLE setup service (AD-25): setup code, the `cf_setup` records (provisioning record version 2 with the Server address), BLE framing, the Device session state machine with the Server check after the join, `run_setup` over the hal traits, and the app-side client used by tests and the bench client; `no_std`, no `alloc` | Story 3.4, extended in 3.5 |
| `uplink/` | `coldframe-uplink`: the Hub's uplink (Story 3.5): `select_bssid` (strongest supported BSSID, H-1), reconnect `Backoff`, `HeartbeatSchedule` (30–60 s from the TRNG), `ServerUrl`, RFC 3339 `…Z` parsing and `MonotonicStamp` (AD-11), the hand-written heartbeat JSON on serde-json-core (checked against the OpenAPI golden fixtures, AD-10), `sign_heartbeat` (AD-12), `check_server` for setup and `Uplink::step`/`run`; `no_std`, no `alloc` | Story 3.5 |
| `crypto/` | `coldframe-crypto`: key hierarchy, frame sealing and replay window, HPKE enrolment, BLE setup session, heartbeat signing, all from the constants [`packages/crypto-spec`](../crypto-spec) generates into `src/spec.rs`; the Device identity (find or burn the eFuse root, or the dev-mode flash root) over the `coldframe-hal` traits; `no_std`, no `alloc` | Stories 3.1 and 3.2 |

These crates are members of the Cargo workspace in the repository root. The Hub firmware also links
all five with the `esp` toolchain, which is Rust 1.95. That is why they declare
`rust-version = "1.95"`. None depends on an esp crate.
Unit tests stay inline next to the code. Integration tests live in [`tests/rs`](../../tests/rs).
