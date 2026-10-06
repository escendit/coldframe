# coldframe-node

Node firmware for the ESP32-S3, in `no_std` Rust on esp-hal.

Story 4.1 gives the Node its wake cycle: every 15 minutes it wakes, takes one Reading per Sensor
with one shared `measured_at` and a `reading_seq` that never repeats, reads its battery and charger,
and deep-sleeps again (AD-11, AD-17, FR4, NFR4). Story 4.2 adds the setup button: a short press
takes a Reading now, a long press opens a three-minute BLE setup window in which the app enrols the
Node (FR2, N-3, AD-25). Story 4.4 adds the [transport](#transport): every wake report goes into a
flash buffer and is sent, sealed, over ESP-NOW through a Hub to the Server; only a sealed
acknowledgement deletes it, and only a sealed downlink sets the clock (AD-9, AD-11, AD-17).

The logic is not in this crate. The wake cycle, the counters, the battery curve, the press
classification and the wake plan live in [`coldframe-sensing`](../../../packages/rs/sensing), the
report buffer, the frames and the transport step in
[`coldframe-transport`](../../../packages/rs/transport), the
setup session and window in [`coldframe-setup`](../../../packages/rs/setup), the identity in
[`coldframe-crypto`](../../../packages/rs/crypto) (`identity` module), all behind the traits of
[`coldframe-hal`](../../../packages/rs/hal) and tested on the host with their mocks
(`cargo test --workspace` in the repository root). This crate only implements those traits for the
chip, in `src/board/`, and wires them up in `src/main.rs`.

## Boot steps

A deep-sleep wake is a reset, so every wake runs all of these:

1. esp-hal at 80 MHz (esp-radio's minimum, to cut the active current), then the RTC watchdog armed
   at 30 s: a hung wake or a halted panic resets the chip instead of draining the battery awake.
   The heap and esp-rtos (whose timer the press is timed with). The RTC uptime at this point is the
   wake start. The setup button input (GPIO7, pull-up).
2. The wake cause: the setup button, a deep-sleep timer wake, or a cold boot (power-on or any other
   reset). On a button wake the press is timed right here, before any flash or identity work, by
   polling the button every 10 ms (`classify_press`): held under 50 ms is a bounce, released
   before 3 s is a short press, held for 3 s is a long press (returned at once, without waiting
   for the release); a read error counts as a bounce. The press is only observable while the
   finger is on the button, so only the bootloader delay eats into a short press.
3. The boot ID from `cf_boot`: a cold boot raises the counter by one and takes the new value; a
   button or timer wake reuses the stored value (and reserves like a cold boot if the counter is
   still 0).
   It comes before the identity, so once the boot-counter reservation succeeds, a later failure
   cannot leave a power-on without its own boot ID. The residual case: if the boot-counter stage
   itself fails on a cold boot, the Node sleeps one period and the next (timer) wake reuses the
   previously stored boot ID.
4. The identity, exactly as the Hub's (Story 3.2): the eFuse root in a release build, the
   `cf_ident` flash root under `dev-mode`. The radio starts only if a new root has to be drawn
   (first boot); a normal wake never powers it.
5. `wake_plan` decides the wake from the cause and the press, and logs `wake plan=…`:
   - `measure`: a timer wake or cold boot, steps 6 to 10 as scheduled;
   - `report-now`: a short press, steps 6 to 10 now, then a full period;
   - `sleep-again`: a bounce, no measurement, a full period;
   - `setup`: a long press, [setup mode](#setup-mode), then steps 6 to 10 **without the radio**
     (the report is buffered and goes out at the next wake) and a full period.
6. The link state from `cf_link` (`coldframe_transport::begin`). When a downlink has set the clock
   on this boot ID, the wall clock is restored from it (the stored offset plus the RTC uptime), so
   this wake's Readings are stamped with it.
7. The measurement (`run_wake`):
   1. `measured_at` is captured once: `Synced { unix_ms }` when a downlink has set the clock on
      this boot ID, otherwise `Unsynced { boot_id, uptime_ms }`.
   2. Probe switch on, 100 ms settle, the mean of 8 raw ADC counts, probe switch off.
   3. One forced BME680 measurement (heater 300 °C for 150 ms): temperature, humidity, raw gas Ω.
      The driver's floats are converted by `coldframe_sensing::env_sample_from`; a gas value the
      sensor flags invalid (heater unstable) costs only the gas Reading.
   4. Divider switch on, 10 ms settle, the mean of 4 millivolt conversions, divider switch off; the
      % from a LiPo discharge curve, always marked approximate.
   5. The charger status pin (active low).
   6. One `reading_seq` per Reading is reserved in `cf_seq`: the raised ceiling is written and read
      back before any value is used. If that fails, no Reading is issued this wake.
   7. `issue_report_seq` reserves one more value of the same counter for the wake report itself
      (its battery and charging status), also when the wake has no Reading. It is above every
      `reading_seq` of the wake and is acknowledged like one. A report without it (the counter
      failed) cannot be identified, so it is not buffered.
8. The [transport](#transport) (`coldframe_transport::run`).
9. Two log lines: `wake done …` with the seq range (`seq=none` when no Reading was issued), the
   `report_seq`, the counts and the sleep that follows, and `transport …`.
10. Deep sleep for `sleep_after(plan, …)`: the rest of the 15-minute period (at least 1 s) after a
   scheduled wake, measured from the wake start with the transport included, a full period after any press. The setup button is armed as a second wakeup
   (low level) only if it reads released (`arm_button_wake`): a stuck button leaves the timer wake
   alone, so it never re-wakes the Node in a loop. The sleep actually taken is logged as
   `sleep ms=<n> button_armed=<bool>`.

A switch is turned off on every path out of its measurement. A failed Sensor costs only its own
Readings. A failure of the identity or of a counter partition logs its kind and deep-sleeps one
period instead of parking awake. A failure in the transport costs only the send: the report stays
in the buffer.

Slot order is fixed: 0 soil (raw count), 1 temperature (milli-°C), 2 humidity (milli-%RH),
3 gas (Ω).

## Transport

Every wake that measured ends with the transport step. The Node never joins Wi-Fi: the radio runs
ESP-NOW only, for the transport, and is off again before the deep sleep. The messages are in
[`packages/proto/README.md`](../../../packages/proto/README.md#esp-now-transport).

1. **Buffer.** The wake report is written to `cf_buf` before anything else. The buffer holds 96
   reports (24 h); with 96 buffered, the oldest is dropped and the drop is logged.
2. **Find a Hub.** One Probe on the channel a Hub last answered on, and 120 ms for the reply.
   Without a known channel, or after 3 consecutive misses, the Node probes that channel and then
   channels 1 to 13 and uses the first where a Hub answers. A scan that finds no Hub is not repeated
   before 4 wakes have passed. No reply: nothing is sealed or sent, and the wake is a miss.
3. **Late acknowledgements.** The reply's `pending` is the number of Downlinks the Hub kept from
   the last wake, one per frame of that burst. The Node applies them before it sends anything, and
   waits for them until it has that many or 120 ms have passed. This is the normal path today: the
   Hub's HTTPS request takes longer than the window.
4. **Send.** One frame counter per report is reserved in `cf_frame` (the raised ceiling written
   and read back), then up to 8 buffered reports go out, oldest first, each a `NodeFrame` sealed
   under its own fresh counter. A resend is the same report with the boot ID and uptime of the
   resend. A counter fault seals nothing.
5. **Listen** for 300 ms after the last send, or until every frame of the burst is acknowledged.
6. **Remember.** The link state goes back to `cf_link`: the channel, the miss count, the burst
   just sent, the clock, and whether the Server asked for the Specification set.

- **Delivery (N-1).** Only a Downlink that opens under the Node's `ack/v1` key, names its Device ID
  and protocol version 1 deletes anything, and only the `reading_seq` values in its ranges. What
  the radio reports for a send is never read.
- **Fresh.** A Downlink is fresh when it acknowledges a frame of the burst sent this wake or the
  wake before, for the first time. Only a fresh one ends a run of misses, sets the clock, or asks
  for the Specification set. An older or repeated one can only delete what the Server has stored.
- **Clock (AD-11).** The first fresh Downlink of a boot ID sets the clock to `server_time_ms` plus
  the time since that burst was sent. On a later wake one moves it forward in full and back by at
  most 1 s. A wake corrects the clock once, however many Downlinks it applies.
  The offset is kept in `cf_link` with its boot ID, because every wake is a reboot. A new boot ID
  is unsynced until its own first Downlink.
- **Specifications (AD-19).** Every frame carries `spec_hash`, the SHA-256 of the Node's
  serialized Specification set: soil moisture (raw count 0 to 4095, calibrated, default Thresholds
  30 % and 80 %), air temperature, relative humidity and gas resistance (watched only). When a
  fresh Downlink says `specifications_unknown`, the next frame carries the set, once.
- **Setup wakes** buffer their report and do not transmit.
- **Worst case.** A full scan is 13 probes (the known channel, then the others), each a send and
  a 120 ms wait; then 120 ms for kept Downlinks, 8 frames and the 300 ms window. That is about
  2.1 s of radio with normal sends. A send gives up after 200 ms, so the bound is
  13 × 320 + 120 + 8 × 200 + 300 ms = 6.2 s, far inside the 30 s watchdog.

After an outage the Node sends its backlog at the first wake that finds a Hub, 8 reports a wake,
so the Server has them then. Their acknowledgements arrive late; the Hub keeps one Downlink per
frame, and the Node collects all of them at its next wake. A backlog of up to 8 is therefore gone
one wake after its burst, and nothing is sent twice. A kept-alive Hub connection would bring the
acknowledgements inside the window and is later work.

## Setup mode

A long press (3 s) of the setup button opens the BLE setup window (AD-25). BLE is never started on
any other path, and continuous advertising would break the energy budget.

1. The setup code is loaded from `cf_setup` (the `CFPC` record at offset 0, the Hub's format). The
   first long press draws it from the TRNG and stores it; the Wi-Fi radio starts only for that
   draw. A corrupt record is never replaced: the Node logs `setup skipped: corrupt setup code
   record …` and goes on with a normal measurement.
2. The code is printed as a plain serial line `setup code=XXXXXXXX`, as on the Hub. Record it on a
   sticker for the Node, never in the repository.
3. The RTC watchdog is raised to the window plus 30 s (210 s).
4. The BLE controller starts and advertises the setup service as `Coldframe Node XXXX` (the first
   four Device ID hex digits, uppercase). A `dev-mode` build also starts the Wi-Fi controller and
   the ESP-NOW coexistence probe (below).
5. `run_node_setup` serves Node sessions until one has enrolled and its connection has ended, or
   until 180 s from the start of the window, whichever is first. The window starts when setup mode
   starts advertising (`setup mode window_ms=` in the log), not at the press. A connection with no
   write for 60 s is dropped and the Node advertises again. A session:
   - answers `Identity` with kind `NODE`;
   - needs a `SiteBinding` with a Site and a Lot and no Server, and only checks it: the Server, not
     the Node, holds the Lot assignment (AD-18), so a Lot the Server refuses (409) needs no second
     BLE session;
   - seals `K_dev` to the Server key of an `EnrolmentRequest`, exactly as the Hub does;
   - refuses `WifiScanRequest` and `WifiConfig` (`UNEXPECTED_MESSAGE`): a Node never uses Wi-Fi;
   - answers a wrong setup code with one sealed error and a disconnect; the Node advertises again
     for the rest of the window.
6. The log says `setup end=enrolled` or `setup end=window-closed`. The BLE (and probe) controllers
   are dropped, so advertising stops, and the watchdog goes back to 30 s.
7. A normal measurement follows, then a full period of sleep, so the Readings after pairing arrive
   promptly.

The Node stores nothing in setup mode but the setup code: no Site, Lot or Wi-Fi settings.

**Coexistence probe (`dev-mode` only):** while in setup mode, a `dev-mode` build broadcasts
`CFCOEX1 ‖ device_id` over ESP-NOW on channel 1 every 2 s (`coex espnow tx ok=…`) and logs every
probe it hears (`coex espnow rx from=<mac>`), next to a live BLE session. It exists to check BLE +
ESP-NOW coexistence on one Node on the bench (the device-hardware open item). A release build has
no probe: its only ESP-NOW is the [transport](#transport), which never runs next to BLE.

## Layout

| Path | What |
| --- | --- |
| `src/main.rs` | The boot steps, setup mode, the dev-mode guard, the log lines |
| `src/board/pins.rs` | The reference wiring, the divider resistors and the BME680 address: the one place to change pins |
| `src/board/sensors.rs` | `OutputPin` switches, the charger `InputPin`, ADC1 shared by the soil `RawAdc` and the calibrated battery `Adc`, the BME680 `EnvSensor` over bosch-bme680 |
| `src/board/sleep.rs` | Wake cause (button, timer, cold boot), reset reason, deep sleep with the RTC timer wakeup and, when armed, the button |
| `src/board/button.rs` | The setup button `InputPin` (GPIO7, pull-up) and its deep-sleep wake (low level, low-power path) |
| `src/board/ble.rs` | `SetupLink` over trouble-host: the setup GATT service, copied from the Hub, run as a future so setup mode can drop it |
| `src/board/coex.rs` | `dev-mode` only: the ESP-NOW coexistence probe |
| `src/board/espnow.rs` | `DatagramRadio` over esp-radio's ESP-NOW: set the channel, send to a MAC address or to everyone, receive with a timeout |
| `src/board/rtc.rs` | `Rtc`: uptime from the RTC timer, which keeps counting through deep sleep; the wall clock is that uptime plus the offset the transport restores and sets |
| `src/board/radio.rs` | `Radio`: esp-radio's Wi-Fi controller, started only on `enable()` |
| `src/board/flash.rs` | `Flash` over the data partitions; word-aligned writes are plain NOR programs. Also the transport's `Partitions`: `cf_frame`, `cf_buf` and `cf_link`, one at a time |
| `src/board/rng.rs`, `timer.rs` | `Trng` and `Timer`, copied from the Hub; `RadioTrng` starts the radio on the first fill only |
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
| `cf_setup` | `0x12000` | `0x2000` | The setup code (`CFPC` record at offset 0); a Node writes nothing else here |
| `cf_frame` | `0x14000` | `0x2000` | The frame counter (`CFFC` records): the counter every sealed frame is sent under |
| `cf_link` | `0x16000` | `0x2000` | The link state (`CFLK` records): channel, miss count, last burst, clock |
| `cf_buf` | `0x18000` | `0x8000` | The report buffer: wake reports not yet acknowledged |
| `factory` | `0x20000` | `0x3E0000` | The firmware |

The Coldframe partitions have subtype `undefined` (0x06) and are found by label, because espflash
4.5 panics on custom subtypes (as for the Hub).

Each counter partition is two 4096-byte sectors of 32-byte slots:
`magic(4) ‖ 0x01 ‖ 0x00×3 ‖ ceiling u64 BE ‖ SHA-256(first 16 bytes)[0..4] ‖ 0xFF×12`. A wake
appends one record to `cf_frame` when it sends, and two to `cf_seq` (the Readings, then the
`report_seq`); when a sector is full the other one is erased and written, so one of them always
holds a valid ceiling. The ceiling is the maximum over both sectors. That is about 192 writes a day
into `cf_seq` and one sector erase every ~0.7 days, far below NOR endurance.

`cf_link` works the same way with 64-byte records and a generation number instead of a ceiling:
one record per wake, the newest valid one is the state. It holds nothing that delivery depends on,
so a region with no valid record reads as "no channel, no clock" and is written over.

`cf_buf` is eight sectors of 128-byte record slots, 256 in all. A report is written once to a free
slot; an acknowledgement clears bits of one word of its record (one bit per Reading and one for the
report), and a record with no bit left is dead. Order is by `report_seq`, not by position. With one
sector's worth of slots left free, the reports still live in the sector with the most dead slots are
copied to free slots and that sector is erased: about one erase every 32 wakes, spread over the
eight sectors. A power cut at any write leaves every other buffered report intact; a cut between
the copy and the erase leaves two copies, which read as one.

> [!NOTE]
> Story 4.4 took the range `0x14000`–`0x1FFFF`, which was free before. The firmware never wrote it,
> so it is normally erased. If a board flashed with an older build logs
> `transport frame counter failed error=counter corrupt` or keeps reporting a non-empty
> `backlog` it cannot send, erase the range once: `espflash erase-region 0x14000 0xC000`.

- A counter with written slots but no valid record logs `reading_seq failed error=counter corrupt`
  (seq) or `wake failed stage=boot_counter` (boot) and never restarts at 0. Erasing it is an
  operator decision: an erased `cf_seq` restarts `reading_seq` at 0, and the Server would take the
  repeated values for duplicates. `espflash erase-region 0xE000 0x2000` (seq),
  `espflash erase-region 0x10000 0x2000` (boot).
- A corrupt setup-code record in `cf_setup` is never regenerated: the code may already be on a
  sticker. Erasing it (`espflash erase-region 0x12000 0x2000`) makes the next long press draw a new
  code.
- A frame counter with written slots but no valid record logs
  `transport frame counter failed error=counter corrupt; nothing sealed` on every wake and never
  restarts at 0: a repeated counter would reuse a nonce under the `seal/v1` key. The reports stay
  buffered. **Do not erase `cf_frame` on a Node that has ever sent a frame** unless the Device is
  enrolled again with a new identity: the Server refuses the low counters as replays, and a nonce
  would repeat. A never-used board may be erased (`espflash erase-region 0x14000 0x2000`).
- Erasing `cf_buf` (`espflash erase-region 0x18000 0x8000`) discards every unacknowledged report.
  Erasing `cf_link` (`espflash erase-region 0x16000 0x2000`) costs one channel scan and leaves the
  clock unsynced until the next downlink.

## Pins

The reference wiring of `src/board/pins.rs` (no Node board exists yet; Epic 10 designs one):

| Signal | Pin | Notes |
| --- | --- | --- |
| Soil probe analogue out | GPIO1 (ADC1_CH0) | raw count, 11 dB |
| Battery divider midpoint | GPIO2 (ADC1_CH1) | calibrated mV, 11 dB, 100 kΩ / 100 kΩ |
| Probe power switch | GPIO4 | high-side switch (P-MOSFET or load switch), high = on; control pulled down externally |
| Divider switch | GPIO5 | high-side switch between the cell and the divider top, high = on; control pulled down externally |
| Charger status (`CHRG`) | GPIO6 | active low, internal pull-up |
| Setup button | GPIO7 | momentary switch to ground, active low, internal pull-up (kept in deep sleep); wakes the Node |
| I²C SDA / SCL | GPIO8 / GPIO9 | BME680 at 0x77 (SDO high) |

GPIO7 is a low-power (RTC) pad, so it can wake the chip from deep sleep: esp-hal puts it on `ext0`
or `ext1` at sleep entry and holds the pad with its pull-up through the sleep.

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
| `coldframe-node <version> wake cause=cold_boot\|timer\|button reset=… identity mode=efuse\|dev` | Every wake |
| `identity mode=… source=… device_id=<16 hex>` | As on the Hub: `burned KEYn`, `existing KEYn`, `generated`, `stored` |
| `boot id=<n>` | The boot ID of this power-on |
| `wake plan=measure\|report-now\|setup\|sleep-again` | Every wake that got this far: what the wake does |
| `setup code=XXXXXXXX` | Setup mode: a plain serial line, not a log record |
| `setup mode window_ms=180000` | Setup mode started |
| `ble advertising setup service as Coldframe Node XXXX`, `ble connected`, `ble disconnected` | Setup mode: the BLE link |
| `setup end=enrolled\|window-closed` | Setup mode ended |
| `setup skipped …`, `setup failed stage=… error=…` | Setup mode could not run (a corrupt setup code is never regenerated); the wake goes on |
| `coex espnow probing …`, `coex espnow tx ok=…`, `coex espnow rx from=<mac>` | `dev-mode` setup mode only: the coexistence probe |
| `setup button still held; timer wake only` | The button was low at sleep: only the timer can wake the Node |
| `sleep ms=<n> button_armed=true\|false` | The last line of every wake: the sleep actually taken (a full period after any press), and whether the button can wake the Node |
| `bme680 not found error=…` | The sensor did not answer at boot; the wake goes on without it |
| `soil failed error=…`, `bme680 failed error=…`, `battery failed error=…`, `charger status failed error=…`, `power switch failed error=…` | A Sensor or switch failed; only its own values are missing |
| `reading_seq failed error=…; no Readings issued` | The seq counter could not be raised and verified; battery and charging are still reported |
| `wake done readings=<n> seq=<a>..<b>\|none report_seq=<b>\|none measured_at=synced\|unsynced battery=ok\|none charging=… sleep_ms=<ms>` | Every wake that measured, logged after the transport: the counts, the seq range and the `report_seq`, never values; `sleep_ms` is the sleep that follows, the same value as `sleep ms=` |
| `link state unreadable error=…; starting from none` | `cf_link` could not be read: no known channel, clock unsynced |
| `wake report has no report_seq; not buffered` | The seq counter failed, so the report cannot be identified or sent |
| `transport buffered=ok\|none\|failed hub=found\|none\|off channel=<n> scanned=<bool> pending=<n> sent=<n> downlinks=<n> deleted=<n> fresh=<bool> misses=<n> clock=unchanged\|set\|forward\|back specs_sent=<bool> backlog=<n>` | Every wake that measured: whether the report was buffered; whether a Hub answered (`off`: a setup wake, the radio stayed off) and on which channel, after a full scan or not; how many kept downlinks it announced; frames sent; authentic downlinks applied and the Readings and reports they deleted; whether one was fresh (otherwise the wake is a miss); consecutive misses; what happened to the clock; whether a frame carried the Specification set; reports still buffered |
| `transport buffer full; oldest reports dropped=<n>` | 96 reports were buffered: the oldest made room |
| `transport frame counter failed error=…; nothing sealed` | `cf_frame` could not be raised and verified; the reports stay buffered |
| `transport buffer failed error=…`, `transport partition failed name=…`, `transport partition=… error=…`, `transport link state failed error=…`, `transport seal failed; frame not sent`, `transport radio failed error=…; report buffered only` | A part of the transport failed; whatever is buffered stays buffered |
| `wake failed stage=… error=…` | The identity or a counter partition failed; the Node sleeps one period and tries again |
| `dev reading slot=… seq=… value=…`, `dev battery …`, `dev measured_at=…` | `dev-mode` builds only: the values |

A release build never logs Reading values, keys, roots or sealed payloads. The Device ID is the only
identity value that is logged.

## Bench

Sleep current and wake duration are measured by hand:
[`docs/bench/node-power-checklist.md`](../../../docs/bench/node-power-checklist.md). Setup mode,
enrolment into a Lot and BLE + ESP-NOW coexistence:
[`docs/bench/node-setup-checklist.md`](../../../docs/bench/node-setup-checklist.md). The transport,
on the bench and in the garden (reach from the farthest Lot, an hour without the Hub, a router
channel change, counters across power cuts, the clock):
[`docs/bench/node-transport-checklist.md`](../../../docs/bench/node-transport-checklist.md).
