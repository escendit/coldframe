# apps/rs

Firmware for the ESP32-S3, written in Rust.

| Folder | What | Arrives in |
| --- | --- | --- |
| `hub/` | Hub firmware: relay between Nodes and the Server | Epic 3 |
| `node/` | Node firmware: measure, seal, send, sleep | Epic 4 |
| `spike-hub-radio/` | Throwaway spike for radio coexistence, see [the write-up](../../docs/spikes/hub-radio-coexistence.md) | Present |

Firmware crates build with the `esp` toolchain for `xtensa-esp32s3-none-elf`, so they stay outside
the host Cargo workspace in the repository root. Logic that can be tested on the host lives in
[`packages/rs`](../../packages/rs).
