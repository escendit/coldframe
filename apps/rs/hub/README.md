# coldframe-hub

Hub firmware for the ESP32-S3, in `no_std` Rust on esp-hal.

Story 3.2 brings the chip up with a hardware-bound identity (AD-12):

1. It starts the radio, which is also the entropy source of the TRNG.
2. It finds its identity root in an eFuse key block, or burns one on first boot.
3. It derives `K_dev`, the purpose keys and the Device ID, logs the Device ID and idles.

BLE setup, Wi-Fi join and heartbeats come with Stories 3.4 and 3.5.

The identity logic is not in this crate. It lives in [`coldframe-crypto`](../../../packages/rs/crypto)
(`identity` module) behind the traits of [`coldframe-hal`](../../../packages/rs/hal), and it is tested
on the host with their mocks (`cargo test --workspace` in the repository root). This crate only
implements those traits for the chip, in `src/board/`, and wires them up in `src/main.rs`.

## Layout

| Path | What |
| --- | --- |
| `src/main.rs` | Boot sequence, the dev-mode guard, the identity log line, the idle loop |
| `src/board/radio.rs` | `Radio` over the esp-radio Wi-Fi controller |
| `src/board/rng.rs` | `Trng` over the esp-hal TRNG, which refuses to run while the radio is off |
| `src/board/efuse.rs` | `Efuse`: reads through `esp_hal::efuse`, burns through the ROM routine `ets_efuse_write_key`. The only `unsafe` code of the Hub. Builds without `dev-mode` only |
| `src/board/hmac.rs` | `HmacPeripheral` over the HMAC accelerator in upstream mode. Builds without `dev-mode` only |
| `src/board/flash.rs` | `Flash` over the `cf_ident` partition through esp-storage. Dev-mode builds only |
| `partitions.csv` | The partition table, with the dev-mode identity partition `cf_ident` |

The crate is standalone and not a member of the host workspace. `Cargo.lock` is committed.

## Prerequisites

- The `esp` Rust toolchain, installed with `espup`. Run `source ~/export-esp.sh` in every shell
  before you build.
- `espflash` 4.x.
- An ESP32-S3 board on USB.

## Build and flash

Run everything from this folder.

```sh
source ~/export-esp.sh
cargo build --release            # release firmware: eFuse identity
cargo build --features dev-mode  # debug firmware: software identity in flash
cargo fmt --check
cargo clippy --release -- -D warnings
```

`cargo run --release` flashes the board with `partitions.csv` and opens the serial monitor (the
runner is `espflash flash --monitor --partition-table partitions.csv`).

> [!WARNING]
> **The first boot of any build without `--features dev-mode` (debug or release) burns an eFuse
> key block, and that cannot be undone.** A plain `cargo build` or `cargo run` burns too.
> One of the six key blocks becomes a read-protected, write-protected HMAC key with purpose
> `HMAC_UP`. The Hub then keeps that identity for life. Only flash a build without
> `--features dev-mode` (debug or release) onto a board meant to be a Coldframe Hub, and check its key purposes first (see the
> [bench checklist](../../../docs/bench/hub-identity-checklist.md)).

## Identity modes

### eFuse (default: any build without `--features dev-mode`, debug or release)

- A key block with purpose `HMAC_UP` (8) is the identity. If there is one, it is used and nothing
  is burned.
- On first boot, the Hub takes 32 bytes from the TRNG with the radio on and rejects all-zero or
  all-`0xFF` output. It burns the bytes into the first unused key block with purpose `HMAC_UP`,
  which also sets read and write protection. It then re-reads the block and checks the purpose
  and the read protection.
- `K_dev = HMAC-SHA256(root, "coldframe/device/v1")` is computed by the HMAC peripheral. The root
  never leaves the eFuse.
- It refuses to go on, and burns nothing, when:
  - two blocks have purpose `HMAC_UP`;
  - the `HMAC_UP` block is not read-protected;
  - no key block is unused.

If power fails during the burn, the block can end up holding data with purpose `USER`. The chip
then reports that block as used, so the next boot burns a different free block. One block is
lost, but a key is never reused.

A burn can also set purpose `HMAC_UP` without setting the block's read protection (`RD_DIS`). The
first boot then reports `BurnNotVerified`, and every later boot halts with `IdentityNotProtected`.
Such a board can no longer hold a Hub identity: retire it, or use it only with `dev-mode`.

### Dev mode (`--features dev-mode`)

- The root is a TRNG-generated software key, stored in the `cf_ident` partition as a 41-byte record:
  `CFID ‖ 0x01 ‖ root[32] ‖ SHA-256(prefix ‖ root)[0..4]`.
- On first boot the partition is erased (`0xFF`), so the Hub writes the record and reads it back.
  Later boots read the record.
- A record that is neither erased nor valid stops the Hub. It is never replaced silently. To start
  over with a new identity on purpose, erase the partition:
  `espflash erase-region 0xE000 0x1000`.
- A dev-mode build compiles no eFuse or HMAC code at all, so it cannot burn anything.
- **Release builds refuse the feature:** `cargo build --release --features dev-mode` fails with a
  `compile_error!`. Dev mode is for debug builds only.

## Log lines

| Line | Meaning |
| --- | --- |
| `identity mode=efuse source=burned KEYn device_id=<16 hex>` | First boot: the root was burned into key block `n` just now |
| `identity mode=efuse source=existing KEYn device_id=<16 hex>` | A later boot: the identity in key block `n` was used, and nothing was burned |
| `identity mode=dev source=generated device_id=<16 hex>` | Dev mode, first boot: a new root was written to `cf_ident` |
| `identity mode=dev source=stored device_id=<16 hex>` | Dev mode, later boot: the root was read from `cf_ident` |
| `identity mode=… failed stage=… error=…; halted` | Provisioning failed. The Hub stops and does not retry. The error names only the kind of failure |
| `uptime s=… device_id=…` | Once a minute while idle; the radio stays on |

The Device ID is the only identity value that is ever logged. No root, key or HMAC output is
logged.

## Why `cf_ident` has subtype `undefined`

espflash 4.5.0 parses partition tables with esp-idf-part 0.6.0, which only accepts the named ESP-IDF
data subtypes and panics on a custom one such as `0x40`. `cf_ident` therefore uses `undefined`
(0x06), and the firmware finds it by its label. A build without `dev-mode` does not need
`cf_ident`; dev mode does.

## Risks to prove on the bench

- The eFuse burn goes through the ROM routine `ets_efuse_write_key`, because esp-hal has no eFuse
  write API. Whether it programs correctly under esp-hal's clock setup can only be shown on real
  hardware. The re-read check after the burn (`BurnNotVerified`) catches a burn that did not take.
- The [bench checklist](../../../docs/bench/hub-identity-checklist.md) is the acceptance test for
  the on-device behaviour. CI builds and tests only the host crates.
