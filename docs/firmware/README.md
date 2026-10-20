# Firmware reference

The Coldframe firmware is written in Rust for the ESP32-S3, and it is meant to be read. This
reference explains how the Node and the Hub use ESP-NOW, BLE provisioning, eFuse keys, frame
sealing and deep sleep, and points to the code that does it. To build and flash the firmware, use
the [build guide](../build/README.md) instead; this reference assumes you have the repository open
next to it.

Each page explains one mechanism in prose, lists its key constants and timings, and ends with
**Where to read the code**: repository paths plus the symbol to look for. The architecture
decisions (AD-n) are in the
[architecture spine](../../_bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md),
and the Node and Hub requirements (N-n, H-n) in the
[device hardware spec](../../_bmad-output/specs/spec-coldframe/device-hardware.md). Where those
planning documents and the code disagree, the pages describe the code and say so.

## Reading order

1. [Identity](identity.md): the eFuse root key, the HKDF purpose keys and the Device ID.
2. [Sealing](sealing.md): ChaCha20-Poly1305 frames, the frame counter and the replay window.
3. [ESP-NOW](espnow.md): how a Node finds its Hub, sends a burst and follows the Hub's channel.
4. [Deep sleep](deep-sleep.md): the Node's wake cycle and the power budget.
5. [Buttons](buttons.md): the setup button, short press and long press.
6. [BLE setup](ble-setup.md): the custom setup protocol and its proof-of-possession code.
7. [Radio coexistence](radio-coexistence.md): Wi-Fi, ESP-NOW and BLE on one Hub radio.

Identity and sealing come first because every later page sends or stores something sealed with
those keys.

## Crate map

The logic lives in host-testable crates under `packages/rs`, behind the hardware traits of
`coldframe-hal` (AD-24). Only the `src/board` module of a firmware crate names esp-hal types.
[`packages/rs/README.md`](../../packages/rs/README.md) and
[`apps/rs/README.md`](../../apps/rs/README.md) describe each crate in full.

| Crate | Folder | What to learn from it |
| --- | --- | --- |
| `coldframe-hal` | `packages/rs/hal` | The hardware traits (radio, eFuse, HMAC, flash, BLE, Wi-Fi) and their host mocks |
| `coldframe-crypto` | `packages/rs/crypto` | The key hierarchy, frame sealing, the replay window, HPKE enrolment, the setup session keys |
| `coldframe-protocol` | `packages/rs/protocol` | The wire messages generated from `packages/proto`, and the ESP-NOW message codec |
| `coldframe-setup` | `packages/rs/setup` | The BLE setup service: setup code, framing, session state machine, stored records |
| `coldframe-uplink` | `packages/rs/uplink` | The Hub's Wi-Fi uplink: access-point choice, heartbeats, the relay of Node frames |
| `coldframe-sensing` | `packages/rs/sensing` | The Node's wake cycle, the button, the flash-reserved counter |
| `coldframe-transport` | `packages/rs/transport` | The Node's ESP-NOW transport, report buffer and channel following |
| `coldframe-hub` | `apps/rs/hub` | The Hub firmware: boot order, board drivers. See [its README](../../apps/rs/hub/README.md) |
| `coldframe-node` | `apps/rs/node` | The Node firmware: one wake per boot, board drivers. See [its README](../../apps/rs/node/README.md) |

## Two toolchains

- **Host, Rust 1.97.** The Cargo workspace in the repository root holds the seven `packages/rs`
  crates and their tests in `tests/rs`. `rust-toolchain.toml` pins the host toolchain, and the
  workspace runs clippy, the tests and the API docs with it.
- **esp, Rust 1.95.** The firmware crates in `apps/rs/hub` and `apps/rs/node` build for
  `xtensa-esp32s3-none-elf` with the `esp` toolchain from `espup`, so they stay outside the
  workspace. Because the firmware links the `packages/rs` crates, those crates declare
  `rust-version = "1.95"` and must not use anything newer. [flash.md](../build/flash.md#toolchain)
  has the versions CI pins.

## Open the API docs

Every public item of the `packages/rs` crates is documented, including the wire types generated
from the `.proto` comments. CI builds the docs with warnings as errors. To browse one crate's docs
from the repository root:

<!-- ci:manual -->

```sh
cargo doc --no-deps -p coldframe-transport --open
```

Replace `coldframe-transport` with any crate of the map above that lives in `packages/rs`. The
firmware crates themselves need the esp toolchain: run `cargo doc --no-deps --open` in `apps/rs/node` or
`apps/rs/hub` after `source ~/export-esp.sh`.
