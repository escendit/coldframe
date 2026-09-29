# Bench checklist: Hub identity (Story 3.2)

This checklist is the acceptance test for the Hub's hardware-bound identity (AD-12) on a real
ESP32-S3. CI builds and tests only the host crates. What happens on the chip is checked here, by
hand.

> [!WARNING]
> Part B burns an eFuse key block. **This cannot be undone.** Any build without
> `--features dev-mode` burns on first boot, debug or release, including a plain `cargo run`. Use a board meant to be a Coldframe
> Hub, and do Part A first.

## What you need

- Two ESP32-S3 boards: board **R** for the release build, board **D** for the dev-mode build. Board
  D must never run any build without `--features dev-mode` (debug or release), because such a
  build burns an eFuse on first boot. If one board has to serve for both, run Part C first.
- The `esp` Rust toolchain and `espflash` 4.x, as in [`apps/rs/hub/README.md`](../../apps/rs/hub/README.md).
- A way to read the eFuse key purposes. The Story 3.2 plan names `espflash board-info`, but
  espflash 4.5.0 prints only chip type, crystal, flash size, features and MAC, not the key
  purposes. Use `espefuse summary` from esptool (`pipx install esptool`) until espflash can show
  them. In its output, look at `KEY_PURPOSE_0` to `KEY_PURPOSE_5` and at `RD_DIS`.

Run every command from `apps/rs/hub` after `source ~/export-esp.sh`. Record the port, the board
and the date for each run.

## A. Before flashing (board R)

- [ ] Run `espflash board-info` and write down the chip revision and MAC.
- [ ] Run `espefuse --port <port> summary` and write down `KEY_PURPOSE_0` to `KEY_PURPOSE_5`.
- [ ] Check that no key block has purpose `HMAC_UP`. If one does, the board already has an
      identity: Part B must then report `existing KEYn`.
- [ ] Check that at least one key block has purpose `USER` and is unused. Otherwise the Hub stops
      with `NoFreeKeyBlock`, and that board cannot become a Hub.

## B. Release build, first boot and reboot (board R)

- [ ] `cargo build --release` succeeds.
- [ ] Flash and monitor with `cargo run --release`.
- [ ] The first boot logs `identity mode=efuse source=burned KEYn device_id=<16 hex>`. Write down
      `n` and the Device ID.
- [ ] No log line contains anything but the Device ID: no root, no key, no HMAC output.
- [ ] `espefuse --port <port> summary` shows `KEY_PURPOSE_n = HMAC_UP` for that one block, and
      `RD_DIS` has bit `n` set. Every other key purpose is unchanged from Part A.
- [ ] Reset the board, for example with `espflash reset` and then `espflash monitor`. The log shows
      `identity mode=efuse source=existing KEYn device_id=<same hex>`, with the same `n` and the
      same Device ID.
- [ ] `espefuse --port <port> summary` shows no new `HMAC_UP` purpose and no other change.
- [ ] Power-cycle the board by unplugging it. The same `existing KEYn` line and Device ID appear.
- [ ] The board stays up and logs `uptime s=…` about once a minute.

## C. Dev-mode build (board D)

- [ ] Write down the key purposes with `espefuse --port <port> summary`.
- [ ] `cargo build --features dev-mode` succeeds.
- [ ] Flash with `cargo run --features dev-mode`. The first boot logs
      `identity mode=dev source=generated device_id=<16 hex>`.
- [ ] Reset the board. It logs `identity mode=dev source=stored` with the same Device ID.
- [ ] `espefuse --port <port> summary` shows no eFuse change at all compared with the first step.
- [ ] Optional corrupt-record check: write one byte over the record, for example with
      `printf 'X' > x.bin && espflash write-bin 0xE000 x.bin`, then reset. The Hub logs
      `failed stage=provision error=dev identity record is corrupt; halted` and writes nothing. To
      recover, run `espflash erase-region 0xE000 0x1000`. The next boot generates a new identity.

## D. Release builds refuse dev mode

- [ ] `cargo build --release --features dev-mode` fails with
      `the \`dev-mode\` feature is refused in release builds`.

## Result

| Date | Board | Part | Pass / fail | Device ID | Notes |
| --- | --- | --- | --- | --- | --- |
| | | | | | |
