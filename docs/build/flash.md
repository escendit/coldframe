# Flash the Hub and the Node

> [!WARNING]
> **The first boot of any build without `--features dev-mode`, debug or release, burns an eFuse
> key block, and that cannot be undone.** One of the chip's six key blocks becomes a
> read-protected, write-protected HMAC key with purpose `HMAC_UP`: the Device's identity for life.
> A plain `cargo build` or `cargo run` burns too, and **the release binaries always burn**: they
> are release builds without dev mode.
>
> - Flash such a build only onto a board meant to be a Coldframe Hub or Node, and
>   [check its key purposes first](#before-you-flash-check-the-key-purposes).
> - **On a test board, use dev mode:** build from source with `--features dev-mode`. A dev-mode
>   build compiles no eFuse code at all and keeps a software identity in the `cf_ident` partition
>   instead, which you can erase. Dev mode is for debug builds only:
>   `cargo build --release --features dev-mode` refuses to compile.

Both Devices are flashed over USB with `espflash`. There is no over-the-air update. You can flash
the **release binaries** of a GitHub Release, or build **from source**. Do the Hub first: a Node
needs a Hub to send to.

On Linux, add yourself to the group that owns the serial port (`dialout` or `uucp`) and log in
again, or `espflash` cannot open the board.

## Toolchain

You need the `esp` Rust toolchain from `espup`, and `espflash`. These are the versions CI pins:
espup 0.17.1, the esp toolchain 1.95.0.0 and espflash 4.5.0. First install Rust with
[rustup](https://rustup.rs), and the system packages:

- To build `espflash` on Linux: `libudev-dev` and `pkg-config` (Debian and Ubuntu:
  `sudo apt install libudev-dev pkg-config`).
- To build the **Hub** from source: **cmake and ninja** (Debian and Ubuntu:
  `sudo apt install cmake ninja-build`, or `uv tool install cmake ninja`). The Hub rebuilds the
  mbedTLS C sources. The Node needs neither.

Then install the tools at the pinned versions:

<!-- ci:run . -->
```bash
cargo install espup --version 0.17.1 --locked
espup install --toolchain-version 1.95.0.0 --targets esp32s3
cargo install espflash --version 4.5.0 --locked
```

`espup install` writes `~/export-esp.sh`. Source it in **every** shell before you build, and check
the versions:

<!-- ci:run . -->
```bash
source ~/export-esp.sh
espup --version
espflash --version
rustup run esp rustc --version
```

To flash only the release binaries you need just `espflash`, not the esp toolchain.

## Before you flash: check the key purposes

A board can become a Hub or a Node only if one of its eFuse key blocks is still unused. A board that
already has an `HMAC_UP` key keeps that identity (it reports `existing KEYn`). `espflash 4.5.0`
does not show key purposes, so use `espefuse` from esptool (`pipx install esptool`):

<!-- ci:hardware -->
```bash
espflash list-ports
espflash board-info
espefuse --port <port> summary
```

In the `espefuse` output, look at `KEY_PURPOSE_0` to `KEY_PURPOSE_5`: at least one must be `USER`
and unused. The full procedure is Part A of the
[Hub identity checklist](../bench/hub-identity-checklist.md), which applies to the Node unchanged.

## Flash from the release binaries

Every GitHub Release carries a merged flash image per Device (bootloader, partition table and
firmware in one file, written at offset 0) and a `SHA256SUMS` file
([what a release holds](../operations/release.md)). Download `coldframe-hub-X.Y.Z.bin`,
`coldframe-node-X.Y.Z.bin` and `SHA256SUMS` from the Release page, or with the GitHub CLI, then
check them and flash. Replace `X.Y.Z` with the version, and use the same version as your Server.

<!-- ci:hardware -->
```bash
gh release download vX.Y.Z --repo escendit/coldframe --pattern 'coldframe-*.bin' --pattern SHA256SUMS
sha256sum --check --ignore-missing SHA256SUMS
espflash write-bin --monitor 0x0 coldframe-hub-X.Y.Z.bin
```

`sha256sum` must print `OK` for both images. Then connect the Node's flashing header to USB
([Connecting USB](../../hardware/node.md#connecting-usb)) and flash its image the same way:

<!-- ci:hardware -->
```bash
espflash write-bin --monitor 0x0 coldframe-node-X.Y.Z.bin
```

If no port appears for the Node, put it in download mode. The Node sleeps almost all the time, and
its USB Serial/JTAG port disappears while it sleeps, so a sleeping or crash-looping Node may never
show a port. Hold `BOOT` (GPIO0), press and release `RESET` (EN), release `BOOT`, then flash. The
chip stays in the bootloader until the flash finishes and resets it. The buttons are on the board,
inside the enclosure ([BOM N14](../../hardware/bom.md#node)), so a deployed Node has to be opened.

`--monitor` keeps the serial output open after flashing, so you see the
[first boot](#first-boot-and-the-setup-code). Press `Ctrl+C` to leave it.

## Flash from source

Build from the release tag you run (`git checkout vX.Y.Z`), or from `main` for development. Every
crate has a committed `Cargo.lock`; `--locked` keeps to it.

### Hub

Build the release firmware and turn it into the same merged image a Release carries:

<!-- ci:run apps/rs/hub -->
```bash
source ~/export-esp.sh
cargo build --release --locked
espflash save-image --chip esp32s3 --merge --partition-table partitions.csv \
  target/xtensa-esp32s3-none-elf/release/coldframe-hub target/coldframe-hub.bin
```

Flash the image, or let `cargo run` flash the ELF with the partition table and open the monitor
(the runner is `espflash flash --monitor --partition-table partitions.csv`):

<!-- ci:hardware -->
```bash
espflash write-bin --monitor 0x0 target/coldframe-hub.bin
# or, the same in one step:
cargo run --release
```

### Node

Connect the Node over its flashing header ([Connecting USB](../../hardware/node.md#connecting-usb)).

<!-- ci:run apps/rs/node -->
```bash
source ~/export-esp.sh
cargo build --release --locked
espflash save-image --chip esp32s3 --merge --partition-table partitions.csv \
  target/xtensa-esp32s3-none-elf/release/coldframe-node target/coldframe-node.bin
```

<!-- ci:hardware -->
```bash
espflash write-bin --monitor 0x0 target/coldframe-node.bin
# or:
cargo run --release
```

### Dev mode, for test boards

A dev-mode build burns nothing. It is a debug build, keeps its identity root in the `cf_ident`
partition, and the Node also logs its Reading values. Build both:

<!-- ci:run apps/rs/hub -->
```bash
source ~/export-esp.sh
cargo build --features dev-mode --locked
```

<!-- ci:run apps/rs/node -->
```bash
source ~/export-esp.sh
cargo build --features dev-mode --locked
```

and flash with `cargo run` from the crate's folder:

<!-- ci:hardware -->
```bash
cargo run --features dev-mode
```

Never flash a build without `--features dev-mode` onto a board you want to keep as a test board.
A dev-mode Device is for the bench, not the garden: its identity is not bound to the chip.

## First boot and the setup code

Watch the serial output with `espflash monitor`. It resets the board when it connects, so you see
the boot from the start.

<!-- ci:hardware -->
```bash
espflash monitor
```

The **Hub** logs its version and identity, then, while it is unprovisioned, prints its setup code
and advertises:

```text
coldframe-hub X.Y.Z starting, identity mode=efuse
identity mode=efuse source=burned KEY0 device_id=<16 hex>
setup code=XXXXXXXX
ble advertising setup service as Coldframe Hub XXXX
```

- `source=burned KEYn` appears once, on the first boot: the key was burned just now. Every later
  boot says `source=existing KEYn` with the same Device ID. A dev-mode build says
  `identity mode=dev source=generated` and then `source=stored`.
- `setup code=XXXXXXXX` is printed **on every boot while the Hub is unprovisioned**, which includes
  its first boot. Once the app has set it up, it is never shown again. To see it again before setup,
  press the board's `RST` button with the monitor open.

The **Node** logs its identity on every wake, then measures and goes to sleep:

```text
coldframe-node X.Y.Z wake cause=cold_boot reset=… identity mode=efuse
identity mode=efuse source=burned KEY0 device_id=<16 hex>
boot id=1
wake plan=measure
```

The Node prints its setup code **only when it enters setup mode**, on a **long press (3 s)** of the
setup button. Keep the monitor open: the USB serial link drops while the Node sleeps and
`espflash monitor` reconnects when it wakes. Then:

```text
coldframe-node X.Y.Z wake cause=button reset=… identity mode=efuse
identity mode=efuse source=existing KEY0 device_id=<16 hex>
boot id=1
wake plan=setup
setup code=XXXXXXXX
setup mode window_ms=180000
```

The code is drawn on the first long press and stored; every later long press prints the same code.
If you missed the line, long-press again.

**Write each code on a sticker on its Device**, never in the repository or a chat. The app asks for
it when you add the Device: it proves that you hold the Device. Every log line is explained in the
[Hub README](../../apps/rs/hub/README.md#log-lines) and the
[Node README](../../apps/rs/node/README.md#log-lines).

## The buttons

The Hub has no button of its own: an unprovisioned Hub is always ready for setup.

The Node has one setup button:

| Press | What it does |
| --- | --- |
| **Short** (50 ms to under 3 s) | Takes and sends one Reading now, then sleeps a full period |
| **Long** (held 3 s) | Opens the BLE setup window for 180 s and prints the setup code; then measures once and sleeps |

A press shorter than 50 ms is a bounce and does nothing. BLE runs only in the setup window; the
Node never advertises otherwise.

## Wi-Fi

The Hub joins a **2.4 GHz WPA2-Personal** network, or a **mixed WPA2/WPA3** (transition mode)
network over WPA2. **WPA3-only networks are not supported**: the app lists them, and the Hub refuses
them with "unsupported security". If your router runs WPA3 only, switch it to WPA2/WPA3 mixed mode
for the 2.4 GHz band, or add a WPA2 network for the Hub. The Node never uses Wi-Fi: it talks to the
Hub over ESP-NOW.

## Recovery: erase a region

`espflash erase-region <offset> <size>` erases one partition. It never undoes an eFuse burn: a
release-build Device keeps its identity whatever you erase.

| Device | Partition | Command | Effect |
| --- | --- | --- | --- |
| Hub | `cf_setup` | `espflash erase-region 0xC000 0x2000` | A new setup code on the next boot, and the Hub is unprovisioned: set it up again from the app |
| Hub | `cf_ident` | `espflash erase-region 0xE000 0x1000` | Dev mode only: a new identity on the next boot |
| Node | `cf_setup` | `espflash erase-region 0x12000 0x2000` | A new setup code on the next long press |
| Node | `cf_ident` | `espflash erase-region 0xD000 0x1000` | Dev mode only: a new identity on the next boot |

For example, to give an unprovisioned Hub a new setup code:

<!-- ci:hardware -->
```bash
espflash erase-region 0xC000 0x2000
espflash monitor
```

The offsets come from each crate's `partitions.csv`
([Hub](../../apps/rs/hub/partitions.csv), [Node](../../apps/rs/node/partitions.csv)).

> [!CAUTION]
> Do not run `espflash erase-flash` on a Node that has ever sent a Reading. It also erases the frame
> counter, and the Server then refuses the Node's frames as replays. The Node's other partitions,
> and when erasing them is safe, are in the
> [Node README](../../apps/rs/node/README.md#partitions).

Next: [add the Hub and the Node from the app](README.md#6-add-them-from-the-app).
