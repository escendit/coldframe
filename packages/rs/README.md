# packages/rs

Rust crates that firmware shares and that build and test on the host.

| Folder | What | Arrives in |
| --- | --- | --- |
| `hal/` | `coldframe-hal`: hardware traits (radio, TRNG, eFuse, HMAC, flash, ADC, RTC, GPIO, the BLE setup link and the Wi-Fi station), `no_std` with no required dependencies; optional `hmac`/`sha2` and std behind the `mock` feature, which adds the mocks for host-side tests (AD-24) | Story 3.2, extended in 3.4 and Epic 4 |
| `protocol/` | `coldframe-protocol`: micropb types generated at build time from `packages/proto` (protox, no `protoc`), heapless containers with fixed capacities, `encode`/`decode` helpers and the `MAX_SIZE` constants; `no_std`, no `alloc` | Story 3.4 |
| `setup/` | `coldframe-setup`: the BLE setup service (AD-25): setup code, the `cf_setup` records, BLE framing, the Device session state machine, `run_setup` over the hal traits, and the app-side client used by tests and the bench client; `no_std`, no `alloc` | Story 3.4 |
| `crypto/` | `coldframe-crypto`: key hierarchy, frame sealing and replay window, HPKE enrolment, BLE setup session, heartbeat signing, all from the constants [`packages/crypto-spec`](../crypto-spec) generates into `src/spec.rs`; the Device identity (find or burn the eFuse root, or the dev-mode flash root) over the `coldframe-hal` traits; `no_std`, no `alloc` | Stories 3.1 and 3.2 |

These crates are members of the Cargo workspace in the repository root. The Hub firmware also links
all four with the `esp` toolchain, which is Rust 1.95. That is why they declare
`rust-version = "1.95"`. None depends on an esp crate.
Unit tests stay inline next to the code. Integration tests live in [`tests/rs`](../../tests/rs).
