# coldframe-node

Node firmware for the ESP32-S3, in `no_std` Rust on esp-hal.

Story 4.1 gives the Node its wake cycle: every 15 minutes it wakes, takes one Reading per Sensor
with one shared `measured_at` and a `reading_seq` that never repeats, reads its battery and charger,
and deep-sleeps again (AD-11, AD-17, FR4, NFR4). It does not send anything yet: ESP-NOW, the
Reading buffer, acknowledgements and the clock follow in Story 4.4; the setup button, BLE and
pairing in Story 4.2.

The logic is not in this crate. The wake cycle, the counters and the battery curve live in
[`coldframe-sensing`](../../../packages/rs/sensing), the identity in
[`coldframe-crypto`](../../../packages/rs/crypto) (`identity` module), all behind the traits of
[`coldframe-hal`](../../../packages/rs/hal) and tested on the host with their mocks
(`cargo test --workspace` in the repository root). This crate only implements those traits for the
chip, in `src/board/`, and wires them up in `src/main.rs`.

## Boot steps

A deep-sleep wake is a reset, so every wake runs all of these:

1. esp-hal at 80 MHz (esp-radio's minimum, to cut the active current), then the RTC watchdog armed
   at 30 s: a hung wake or a halted panic resets the chip instead of draining the battery awake.
   The heap and esp-rtos. The RTC uptime at this point is the wake start.
2. The wake cause: a deep-sleep timer wake, or a cold boot (power-on or any other reset).
3. The boot ID from `cf_boot`: a cold boot raises the counter by one and takes the new value; a
   timer wake reuses the stored value (and reserves like a cold boot if the counter is still 0).
   It comes before the identity, so once the boot-counter reservation succeeds, a later failure
   cannot leave a power-on without its own boot ID. The residual case: if the boot-counter stage
   itself fails on a cold boot, the Node sleeps one period and the next (timer) wake reuses the
   previously stored boot ID.
4. The identity, exactly as the Hub's (Story 3.2): the eFuse root in a release build, the
   `cf_ident` flash root under `dev-mode`. The radio starts only if a new root has to be drawn
   (first boot); a normal wake never powers it.
5. The measurement (`run_wake`):
   1. `measured_at` is captured once: `Unsynced { boot_id, uptime_ms }` until Story 4.4 sets the
      clock from an authenticated downlink, then `Synced { unix_ms }`.
   2. Probe switch on, 100 ms settle, the mean of 8 raw ADC counts, probe switch off.
   3. One forced BME680 measurement (heater 300 °C for 150 ms): temperature, humidity, raw gas Ω.
      The driver's floats are converted by `coldframe_sensing::env_sample_from`; a gas value the
      sensor flags invalid (heater unstable) costs only the gas Reading.
   4. Divider switch on, 10 ms settle, the mean of 4 millivolt conversions, divider switch off; the
      % from a LiPo discharge curve, always marked approximate.
   5. The charger status pin (active low).
   6. One `reading_seq` per Reading is reserved in `cf_seq`: the raised ceiling is written and read
      back before any value is used. If that fails, no Reading is issued this wake.
6. One log line with the seq range (`seq=none` when no Reading was issued) and the counts.
7. Deep sleep with an RTC timer wakeup for the rest of the 15-minute period (at least 1 s).

A switch is turned off on every path out of its measurement. A failed Sensor costs only its own
Readings. A failure of the identity or of a counter partition logs its kind and deep-sleeps one
period instead of parking awake.

Slot order is fixed: 0 soil (raw count), 1 temperature (milli-°C), 2 humidity (milli-%RH),
3 gas (Ω).

## Layout

| Path | What |
| --- | --- |
| `src/main.rs` | The boot steps, the dev-mode guard, the log lines |
| `src/board/pins.rs` | The reference wiring, the divider resistors and the BME680 address: the one place to change pins |
| `src/board/sensors.rs` | `OutputPin` switches, the charger `InputPin`, ADC1 shared by the soil `RawAdc` and the calibrated battery `Adc`, the BME680 `EnvSensor` over bosch-bme680 |
| `src/board/sleep.rs` | Wake cause, reset reason, deep sleep with the RTC timer wakeup |
| `src/board/rtc.rs` | `Rtc`: uptime from the RTC timer, which keeps counting through deep sleep; no wall clock yet |
| `src/board/radio.rs` | `Radio`: esp-radio's Wi-Fi controller, started only on `enable()` |
| `src/board/flash.rs` | `Flash` over the data partitions; word-aligned writes are plain NOR programs |
| `src/board/rng.rs`, `timer.rs` | `Trng` and `Timer`, copied from the Hub |
| `src/board/efuse.rs`, `hmac.rs` | `Efuse` and `HmacPeripheral`, copied from the Hub; the only `unsafe` code. Builds without `dev-mode` only |
| `partitions.csv` | The partition table below |

The eFuse, HMAC, flash, TRNG and timer adapters are copies of the Hub's, because `apps/rs` is outside
the host workspace and the Hub is not changed here. Extracting a shared ESP32-S3 board crate is a
deferred item. The crate is standalone and not a member of the host workspace. `Cargo.lock` is
committed.

## Build and flash

Prerequisites: the `esp` toolchain from `espup` (CI pins espup 0.17.1 and toolchain 1.95.0.0),
`espflash` 4.x and an ESP32-S3 board on USB. No cmake or ninja: the Node has no TLS.

```sh
source ~/export-esp.sh
cargo build --release            # release firmware: eFuse identity
cargo build --features dev-mode  # debug firmware: software identity in flash, logs Reading values
cargo fmt --check
cargo clippy --release -- -D warnings
cargo clippy --features dev-mode -- -D warnings
```

`cargo run --release` flashes the board with `partitions.csv` and opens the serial monitor (the
runner is `espflash flash --monitor --partition-table partitions.csv`). The USB serial link drops
while the chip sleeps; `espflash monitor` reconnects on the next wake.

> [!WARNING]
> **The first boot of any build without `--features dev-mode` (debug or release) burns an eFuse
> key block, and that cannot be undone.** A plain `cargo build` or `cargo run` burns too. One of
> the six key blocks becomes a read-protected, write-protected HMAC key with purpose `HMAC_UP`, and
> the Node keeps that identity for life. Only flash such a build onto a board meant to be a
> Coldframe Node, and check its key purposes first (see the Hub's
> [identity checklist](../../../docs/bench/hub-identity-checklist.md), which applies unchanged).

## Identity modes

The same as the Hub's, see [its README](../hub/README.md#identity-modes):

- **eFuse** (any build without `dev-mode`): the root is a read-protected `HMAC_UP` key block, burned
  once on first boot from TRNG bytes drawn with the radio on; `K_dev` comes from the HMAC
  peripheral and the root never leaves the chip.
- **Dev mode** (`--features dev-mode`): the root is a TRNG key in `cf_ident`
  (`espflash erase-region 0xD000 0x1000` starts over). No eFuse code is compiled in.
  `cargo build --release --features dev-mode` fails with a `compile_error!`.

## Partitions

| Name | Offset | Size | What |
| --- | --- | --- | --- |
| `nvs` | `0x9000` | `0x3000` | ESP-IDF default; the firmware never writes it |
| `phy_init` | `0xC000` | `0x1000` | PHY calibration |
| `cf_ident` | `0xD000` | `0x1000` | Dev-mode identity root (`CFID` record) |
| `cf_seq` | `0xE000` | `0x2000` | The `reading_seq` counter (`CFSQ` records) |
| `cf_boot` | `0x10000` | `0x2000` | The boot counter (`CFBT` records) |
| — | `0x12000` | `0xE000` | Free, for `cf_setup` (Story 4.2) |
| `factory` | `0x20000` | `0x3E0000` | The firmware |

The Coldframe partitions have subtype `undefined` (0x06) and are found by label, because espflash
4.5 panics on custom subtypes (as for the Hub).

Each counter partition is two 4096-byte sectors of 32-byte slots:
`magic(4) ‖ 0x01 ‖ 0x00×3 ‖ ceiling u64 BE ‖ SHA-256(first 16 bytes)[0..4] ‖ 0xFF×12`. A wake
appends one record; when a sector is full the other one is erased and written, so one of them always
holds a valid ceiling. The ceiling is the maximum over both sectors. That is about 96 writes a day
into `cf_seq` and one sector erase every ~1.3 days, far below NOR endurance.

- A counter with written slots but no valid record logs `reading_seq failed error=counter corrupt`
  (seq) or `wake failed stage=boot_counter` (boot) and never restarts at 0. Erasing it is an
  operator decision: an erased `cf_seq` restarts `reading_seq` at 0, and the Server would take the
  repeated values for duplicates. `espflash erase-region 0xE000 0x2000` (seq),
  `espflash erase-region 0x10000 0x2000` (boot).

## Pins

The reference wiring of `src/board/pins.rs` (no Node board exists yet; Epic 10 designs one):

| Signal | Pin | Notes |
| --- | --- | --- |
| Soil probe analogue out | GPIO1 (ADC1_CH0) | raw count, 11 dB |
| Battery divider midpoint | GPIO2 (ADC1_CH1) | calibrated mV, 11 dB, 100 kΩ / 100 kΩ |
| Probe power switch | GPIO4 | high-side switch (P-MOSFET or load switch), high = on; control pulled down externally |
| Divider switch | GPIO5 | high-side switch between the cell and the divider top, high = on; control pulled down externally |
| Charger status (`CHRG`) | GPIO6 | active low, internal pull-up |
| I²C SDA / SCL | GPIO8 / GPIO9 | BME680 at 0x77 (SDO high) |

Only ADC1 is used, because ADC2 conflicts with the radio. The digital pads float in deep sleep, so
the switch controls need external pull-downs to stay off. The BME680 is not switched: forced mode puts
it back to sleep (~0.15 µA).

The probe feed and the battery divider are switched on the **high side** (a P-MOSFET with a
level-shifting driver, or a load switch, enabled from the GPIO), off while the GPIO is low or
pulled down. A low-side N-MOSFET would leave the divider midpoint, and GPIO2, at up to 4.2 V through
the top resistor while off, beyond the pad's rating, and would leak through the pad. With the
divider switched high-side, the ADC pins see 0 V while off. The divider midpoint may carry at most
10 nF: with 100 kΩ / 100 kΩ that is RC = 50 kΩ × 10 nF = 0.5 ms, so the 10 ms `DIVIDER_SETTLE_MS`
is well over 5·RC. A larger capacitor needs `DIVIDER_SETTLE_MS` raised to at least 5·RC.

## Log lines

| Line | Meaning |
| --- | --- |
| `coldframe-node <version> wake cause=cold_boot\|timer reset=… identity mode=efuse\|dev` | Every wake |
| `identity mode=… source=… device_id=<16 hex>` | As on the Hub: `burned KEYn`, `existing KEYn`, `generated`, `stored` |
| `boot id=<n>` | The boot ID of this power-on |
| `bme680 not found error=…` | The sensor did not answer at boot; the wake goes on without it |
| `soil failed error=…`, `bme680 failed error=…`, `battery failed error=…`, `charger status failed error=…`, `power switch failed error=…` | A Sensor or switch failed; only its own values are missing |
| `reading_seq failed error=…; no Readings issued` | The seq counter could not be raised and verified; battery and charging are still reported |
| `wake done readings=<n> seq=<a>..<b>\|none measured_at=synced\|unsynced battery=ok\|none charging=… sleep_ms=<ms>` | Every wake: the counts and the seq range, never values |
| `wake failed stage=… error=…; sleeping ms=900000` | The identity or a counter partition failed; the Node sleeps one period and tries again |
| `dev reading slot=… seq=… value=…`, `dev battery …`, `dev measured_at=…` | `dev-mode` builds only: the values |

A release build never logs Reading values, keys or roots. The Device ID is the only identity value
that is logged.

## Bench

Sleep current and wake duration are measured by hand:
[`docs/bench/node-power-checklist.md`](../../../docs/bench/node-power-checklist.md).
