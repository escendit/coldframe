# coldframe-hub

Hub firmware for the ESP32-S3, in `no_std` Rust on esp-hal.

Story 3.2 brings the chip up with a hardware-bound identity (AD-12):

1. It starts the radio, which is also the entropy source of the TRNG.
2. It finds its identity root in an eFuse key block, or burns one on first boot.
3. It derives `K_dev`, the purpose keys and the Device ID, logs the Device ID and idles.

Story 3.4 adds the BLE setup service (AD-25), see [BLE setup](#ble-setup):

4. It loads its setup code from the `cf_setup` partition, or draws and stores one on first boot.
5. While unprovisioned, it prints the code on the serial console, advertises the setup service and
   runs setup sessions until the app has bound it to a Site, enrolled it and given it a Wi-Fi
   network it could join.

Server reachability and heartbeats come with Story 3.5.

The logic is not in this crate. The identity lives in [`coldframe-crypto`](../../../packages/rs/crypto)
(`identity` module) and the setup service in [`coldframe-setup`](../../../packages/rs/setup), both
behind the traits of [`coldframe-hal`](../../../packages/rs/hal), and both are tested on the host with
their mocks (`cargo test --workspace` in the repository root). This crate only implements those
traits for the chip, in `src/board/`, and wires them up in `src/main.rs`.

## Layout

| Path | What |
| --- | --- |
| `src/main.rs` | Boot sequence, the dev-mode guard, the identity log line, the setup code line, `run_setup`, the idle loop |
| `src/board/radio.rs` | `Radio` over the esp-radio Wi-Fi controller |
| `src/board/ble.rs` | `SetupLink` over trouble-host: the setup GATT service, advertising and one connection, bridged to the setup service through embassy-sync channels |
| `src/board/wifi.rs` | `Wifi` over the esp-radio station: active all-channel scan, join pinned to a BSSID and channel |
| `src/board/rng.rs` | `Trng` over the esp-hal TRNG, which refuses to run while the radio is off |
| `src/board/efuse.rs` | `Efuse`: reads through `esp_hal::efuse`, burns through the ROM routine `ets_efuse_write_key`. The only `unsafe` code of the Hub. Builds without `dev-mode` only |
| `src/board/hmac.rs` | `HmacPeripheral` over the HMAC accelerator in upstream mode. Builds without `dev-mode` only |
| `src/board/flash.rs` | `Flash` over the Coldframe data partitions (`cf_setup`, and `cf_ident` in dev mode), one esp-storage instance shared by both |
| `partitions.csv` | The partition table, with `cf_setup` (setup code and provisioning record) and the dev-mode identity partition `cf_ident` |
| `check-image.sh` | FR-1: builds the release image with Wi-Fi credential canaries in the environment and fails if one appears in the ELF or the flash image |

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

FR-1 says the image holds no Wi-Fi credentials; credentials arrive only over BLE setup. Two checks
look for the build-time ways one could get in; they do not prove FR-1 on their own:

- `tests/rs/setup/tests/image_guard.rs` (host, every CI run) scans the sources the image links, this
  crate and `packages/rs/{setup,protocol,crypto,hal}`, plus their build scripts. It fails on any
  environment read other than Cargo's `CARGO_PKG_*`, `OUT_DIR` and `CARGO_MANIFEST_DIR`, on any
  `include_str!`/`include_bytes!`, and on an `[env]` table in this crate's Cargo configuration
  (`.cargo/config.toml` has none; the log level is set in code).
- `./check-image.sh` (by hand, with the esp toolchain) builds the release image with canary values
  in `WIFI_SSID`, `WIFI_PASSWORD`, `SSID`, `PASSWORD` and `CF_WIFI_SSID`, and fails if one appears
  in the ELF or the flash image.

> [!NOTE]
> Story 3.4 moved the partition table: `nvs` shrank to `0x3000` and `cf_setup` sits at `0xC000`,
> inside the old `nvs` range. `cf_ident` stays at `0xE000`. The firmware never writes `nvs`, so the
> range is normally erased; if a board flashed with an older build halts with
> `setup code record is corrupt`, erase it once: `espflash erase-region 0xC000 0x2000`.

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
| `setup code=XXXXXXXX` | A plain serial line (not a log record), on first boot and on every boot while unprovisioned. Never shown once provisioned |
| `corrupt provisioning record` | The `CFWP` record is neither erased nor valid; the Hub treats itself as unprovisioned |
| `ble advertising setup service as Coldframe Hub XXXX` | The setup service is waiting for the app |
| `ble connected` / `ble disconnected` | One setup session begins or ends |
| `setup provisioned` | A session stored the provisioning record; the Hub stops advertising. No SSID or Site is logged |
| `heap free=… used=…` | Once, right after setup, with BLE and Wi-Fi both up. The budget is at least 32 KiB free |
| `setup provisioned earlier; not advertising` | A provisioned boot. The Hub does not re-join Wi-Fi on such a boot until Story 3.5 |
| `setup failed stage=… error=…; halted` | The setup store or the BLE link failed (for example a corrupt setup code record). The Hub stops |
| `uptime s=… device_id=… heap_free=… heap_used=…` | Once a minute while idle; the radio stays on |

The Device ID is the only identity value that is ever logged. No root, key or HMAC output is
logged, and neither is the setup code (outside its one serial line), a Wi-Fi password, a Site ID or
any setup message.

## BLE setup

The transport and message order are in [`packages/proto/README.md`](../../../packages/proto/README.md#ble-transport).
On the Hub:

- **Setup code.** 8 Crockford base32 characters (`0123456789ABCDEFGHJKMNPQRSTVWXYZ`, 40 bits)
  drawn from the TRNG once, stored at offset 0 of `cf_setup` as
  `CFPC ‖ 0x01 ‖ code[8] ‖ SHA-256(prefix ‖ code)[0..4]`. A corrupt record halts the Hub
  (`setup failed stage=store error=setup code record is corrupt`); it is never regenerated,
  because the code may already be written down. Erasing `cf_setup` (`espflash erase-region 0xC000 0x2000`)
  gives a new code on the next boot and unprovisions the Hub.
- **Provisioning record.** At offset `0x1000` of `cf_setup`:
  `CFWP ‖ 0x01 ‖ len ‖ ssid ‖ len ‖ password ‖ len ‖ site_id ‖ SHA-256(prefix ‖ …)[0..4]`,
  written only after a successful join. Its presence stops advertising.
- **Advertising.** Name `Coldframe Hub XXXX` (the first four Device ID hex digits, uppercase), the
  setup service UUID in the advertising data, only while unprovisioned.
- **Wi-Fi.** The Hub scans every channel, answers with one entry per SSID (its strongest access
  point, strongest first, at most 16), and joins the strongest BSSID of the chosen SSID with
  WPA2-Personal, or open for an empty password. WPA3-only networks are listed as
  `WIFI_SECURITY_WPA3_ONLY` and refused with `UNSUPPORTED_SECURITY`: esp-radio has no WPA3-SAE
  station configuration. `CONNECTED` means association and key handshake succeeded; Story 3.5
  adds the Server check and `NO_SERVER`. After `CONNECTED` the session refuses further Site,
  enrolment and Wi-Fi messages, so nothing is stored twice.
- **Provisioned reboots.** A provisioned Hub neither advertises nor shows its code, and in Story 3.4
  it does not re-join its Wi-Fi after a reboot either: joining from the stored record arrives with
  Story 3.5.
- **Residual risk.** The Wi-Fi password sits in plain flash: there is no flash encryption (by
  design since Story 3.2).

The bench procedure is [`docs/bench/hub-setup-checklist.md`](../../../docs/bench/hub-setup-checklist.md),
driven by the desktop client in [`tests/rs/setup-client`](../../../tests/rs/setup-client).

## Why `cf_ident` and `cf_setup` have subtype `undefined`

espflash 4.5.0 parses partition tables with esp-idf-part 0.6.0, which only accepts the named ESP-IDF
data subtypes and panics on a custom one such as `0x40`. Both partitions therefore use `undefined`
(0x06), and the firmware finds them by label. A build without `dev-mode` does not need `cf_ident`;
every build needs `cf_setup`.

## Risks to prove on the bench

- The eFuse burn goes through the ROM routine `ets_efuse_write_key`, because esp-hal has no eFuse
  write API. Whether it programs correctly under esp-hal's clock setup can only be shown on real
  hardware. The re-read check after the burn (`BurnNotVerified`) catches a burn that did not take.
- BLE and Wi-Fi share the radio (coex). A Wi-Fi scan or join while a BLE connection is open is
  what the [setup checklist](../../../docs/bench/hub-setup-checklist.md) exercises.
- The [identity](../../../docs/bench/hub-identity-checklist.md) and
  [setup](../../../docs/bench/hub-setup-checklist.md) bench checklists are the acceptance tests for
  the on-device behaviour. CI builds and tests only the host crates.
