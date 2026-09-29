# packages/rs

Rust crates that firmware shares and that build and test on the host.

| Folder | What | Arrives in |
| --- | --- | --- |
| `hal/` | `coldframe-hal`: hardware traits (radio, TRNG, eFuse, HMAC, flash, ADC, RTC, GPIO), `no_std` with no required dependencies; optional `hmac`/`sha2` and std behind the `mock` feature, which adds the mocks for host-side tests (AD-24) | Story 3.2, extended in Epics 3 and 4 |
| `protocol/` | Code generated from `packages/proto` | Epic 3 |
| `crypto/` | `coldframe-crypto`: key hierarchy, frame sealing and replay window, HPKE enrolment, BLE setup session, heartbeat signing, all from the constants [`packages/crypto-spec`](../crypto-spec) generates into `src/spec.rs`; the Device identity (find or burn the eFuse root, or the dev-mode flash root) over the `coldframe-hal` traits; `no_std`, no `alloc` | Stories 3.1 and 3.2 |

These crates are members of the Cargo workspace in the repository root. The Hub firmware also links
`hal` and `crypto` with the `esp` toolchain, which is Rust 1.95. That is why these two crates declare
`rust-version = "1.95"`. Neither depends on an esp crate.
Unit tests stay inline next to the code. Integration tests live in [`tests/rs`](../../tests/rs).
