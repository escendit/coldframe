# apps/rs

Firmware for the ESP32-S3, written in Rust.

| Folder | What | Arrives in |
| --- | --- | --- |
| `hub/` | `coldframe-hub`, the Hub firmware. Story 3.2 gives it a hardware-bound identity (eFuse root, or a flash root under `dev-mode`); Story 3.4 the BLE setup service (setup code, Wi-Fi join, enrolment); heartbeats and relay between Nodes and the Server follow. See [its README](hub/README.md) | Stories 3.2, 3.4 |
| `node/` | `coldframe-node`, the Node firmware. Story 4.1 gives it the Hub's identity and its wake cycle: wake every 15 minutes, one Reading per Sensor with a shared `measured_at` and a persisted `reading_seq`, battery and charging, deep sleep. Sealing and sending follow in Story 4.4. See [its README](node/README.md) | Story 4.1 |
| `spike-hub-radio/` | Throwaway spike for radio coexistence, see [the write-up](../../docs/spikes/hub-radio-coexistence.md) | Present |

Firmware crates build with the `esp` toolchain for `xtensa-esp32s3-none-elf`, so they stay outside
the host Cargo workspace in the repository root. Logic that can be tested on the host lives in
[`packages/rs`](../../packages/rs), behind the traits of `coldframe-hal`. Only a firmware crate's
`src/board` module names esp-hal types.

Build a firmware crate from its folder:

```sh
source ~/export-esp.sh
cd apps/rs/hub
cargo build --release
cargo build --features dev-mode
./check-image.sh
```

On-device behaviour is checked by hand against the checklists in [`docs/bench`](../../docs/bench).
