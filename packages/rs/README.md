# packages/rs

Rust crates that firmware shares and that build and test on the host.

| Folder | What | Arrives in |
| --- | --- | --- |
| `hal/` | Hardware traits with mocks for host-side tests | Present as a skeleton, filled in Epics 3 and 4 |
| `protocol/` | Code generated from `packages/proto` | Epic 3 |
| `crypto/` | Key derivation and sealing | Epic 3 |

These crates are members of the Cargo workspace in the repository root.
Unit tests stay inline next to the code. Integration tests live in [`tests/rs`](../../tests/rs).
