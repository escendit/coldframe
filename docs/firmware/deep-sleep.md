# Deep sleep and the wake cycle

A Coldframe Node spends almost all of its life in deep sleep. It wakes every 15 minutes, measures,
sends, and sleeps again. This page explains how that cycle is built on the ESP32-S3: why every wake
is a reboot, where the Node keeps its state between wakes, what bounds a wake, and how the sleep
time is computed. It follows [ESP-NOW transport](espnow.md) and comes before
[the setup button](buttons.md); the index is [README.md](README.md). For panel placement and the
power budget in numbers, read [Power and placement](../build/power.md).

## How it works

### Every wake is a reboot

Deep sleep on the ESP32-S3 powers down the CPU and RAM. Only the RTC domain keeps running: the RTC
timer, the wake sources and the held pads. When the timer fires, the chip resets and the bootloader
starts the firmware again from the top. There is no "resume" after the sleep call.

So `apps/rs/node/src/main.rs` (`main`) is written as one straight run from top to bottom, ending in
a call that never returns (`-> !`). The module comment at the top of that file lists the ten steps
of a wake in order. The deep-sleep call itself is in `apps/rs/node/src/board/sleep.rs`
(`deep_sleep`): it sets an RTC timer deadline and calls esp-hal's `sleep_deep`.

Because the wake is a reset, the firmware first has to find out why it is running.
`apps/rs/node/src/board/sleep.rs` (`wake_cause`) reads the chip's wakeup cause:

- `ext0`/`ext1` (the setup button) gives `WakeCause::Button`;
- the RTC timer gives `WakeCause::Timer`;
- anything else, such as power-on, a watchdog reset or a flash, is `WakeCause::ColdBoot`.

A cold boot raises the boot ID; a timer or button wake reuses it
(`packages/rs/sensing/src/wake.rs`, `boot_id`). The boot ID and the RTC uptime together let the
Server place Readings in time before the Node's clock has been set (AD-11).

### Where the state lives

Nothing in RAM survives the sleep. The Node keeps its state in two places:

- **Flash partitions.** The boot counter (`cf_boot`), the `reading_seq` counter (`cf_seq`), the
  frame counter (`cf_frame`), the link state (`cf_link`) and the report buffer (`cf_buf`) are all
  in flash. Their names are in `apps/rs/node/src/board/flash.rs`.
- **The RTC timer.** It keeps counting through deep sleep, so the uptime read in
  `apps/rs/node/src/board/rtc.rs` (`BoardRtc::uptime_millis`) grows across the wakes of one boot.

The firmware does not use RTC RAM. The planning notes in
[device-hardware.md](../../_bmad-output/specs/spec-coldframe/device-hardware.md) allow either RTC
RAM or flash for the buffer; the code uses flash, which also survives a flat battery.

The wall clock is a good example. `BoardRtc` holds the clock as an offset from the uptime, but that
offset is lost at every reset. The transport stores it in `cf_link` together with the boot ID it
belongs to. At the start of each wake, `packages/rs/transport/src/transport.rs` (`begin`) hands it
back, so the Readings can be stamped `Synced`. A cold boot has a new boot ID, so the old offset no longer
applies, and the Readings are `Unsynced` until a sealed downlink sets the clock again (AD-11).

### A short, cheap wake

Two choices keep the active current of a wake low:

- **80 MHz CPU clock.** `main` starts esp-hal with `CpuClock::_80MHz`. The comment there gives the
  reason: it cuts the active current, and 80 MHz is also esp-radio's minimum, so the radio can
  still start when needed.
- **Switched loads.** The soil probe and the battery divider sit behind high-side switches.
  `packages/rs/sensing/src/wake.rs` (`run_wake`) turns each switch on only for its own
  measurement: probe on, settle 100 ms, eight samples, probe off; divider on, settle 10 ms, four
  samples, divider off. A switch is turned off on every path out, errors included. The BME680 is
  not switched: one forced measurement returns it to sleep by itself.

The radio is dropped before the sleep, so it is off while the Node sleeps.

### The watchdog bounds every wake

A Node that hangs awake would drain its cell in hours. So the first thing `main` does after
`esp_hal::init` is arm the RTC watchdog: `apps/rs/node/src/board/rtc.rs` (`arm_watchdog`). Stage 0
resets the whole chip if the wake has not reached deep sleep within `WATCHDOG_MS` (30 s).

The watchdog is never fed during a wake, and it pauses in sleep. It is fed once when armed, so the
count starts from zero rather than from what the previous boot left behind. It also catches a
panic: `esp-backtrace` halts instead of resetting, and the watchdog turns that halt into a reset.
A watchdog reset is not a timer or button wake, so the next run is a cold boot with a new boot ID.

Setup mode is the one long wake. While the BLE window is open, `setup_mode` raises the watchdog to
`SETUP_WATCHDOG_MS` (the window plus 30 s) and restores 30 s afterwards. A normal wake is far
inside 30 s; the module comment in `main.rs` works out the transport's worst case at about 6.2 s.

### The `fail` path: sleep, do not park

If the Node cannot get a boot ID, an identity or the `reading_seq` partition, it cannot produce a
valid report. Staying awake to retry would only cost battery. Instead `fail` in
`apps/rs/node/src/main.rs` logs `wake failed stage=… error=…` and deep-sleeps one full period
(`WAKE_PERIOD_MS`). The next wake tries again from the top. A failed Sensor or a failed transport
is not a `fail`: the wake goes on, and the report stays buffered for the next wake.

### How long to sleep

The Node aims for one wake every `WAKE_PERIOD_MS`, measured from the start of one wake to the
start of the next. `main` records the RTC uptime right after the watchdog is armed
(`wake_start_ms`). After the transport it takes the elapsed time and calls `sleep_ms`:

```rust
pub fn sleep_ms(elapsed_ms: u64) -> u32 {
    let rest = u64::from(WAKE_PERIOD_MS).saturating_sub(elapsed_ms);
    u32::try_from(rest.max(u64::from(MIN_SLEEP_MS))).unwrap_or(WAKE_PERIOD_MS)
}
```

So a wake that took 1.2 s sleeps 898.8 s. A wake that somehow ran a whole period still sleeps at
least `MIN_SLEEP_MS`. The time before `wake_start_ms` (the bootloader and `esp_hal::init`) is not
counted, so the real period is slightly longer than 15 minutes.

`run_wake` also returns a `sleep_ms`, but it is measured before the transport. `main` does not use
it; it computes the sleep again after the radio work, so the radio time is included.

A press changes the schedule. `sleep_after` in `packages/rs/sensing/src/button.rs` keeps the
measured rest only for a scheduled wake (`WakePlan::Measure`). After a short press, a long press
or a bounce, the Node sleeps a full period from now. The [buttons page](buttons.md) covers the
presses.

### Waking on the button

`sleep` in `main.rs` arms the setup button as a second wake source, next to the timer, but only if
the button reads released (`arm_button_wake`). A button held or stuck low would otherwise wake the
Node again at once, in a loop. In that case the Node logs a warning and sleeps on the timer alone.
`apps/rs/node/src/board/button.rs` (`arm_wake`) enables the pad's low-power wake path, which keeps
the pull-up through the sleep. Every sleep logs `sleep ms=<n> button_armed=<bool>`.

## Power budget

The design budget is an average sleep current of about 100 µA or less, with short wakes. The
board options and the arithmetic behind it are in [Power and placement](../build/power.md) and in
the power budget of [device-hardware.md](../../_bmad-output/specs/spec-coldframe/device-hardware.md).

> [!WARNING]
> The ~100 µA average sleep-current budget is **not yet measured**. No reference Node has been
> through the bench run. Until it has, treat every current figure as a design estimate.

To measure it, follow the [Node power checklist](../bench/node-power-checklist.md). Any build
without `dev-mode` burns an eFuse key block on first boot, and that cannot be undone
([identity.md](identity.md)); the checklist says how to prepare the board.

## Key constants and timings

| Constant | Value | Where |
| --- | --- | --- |
| `WAKE_PERIOD_MS` | 900 000 ms (15 min) | `packages/rs/sensing/src/wake.rs` |
| `MIN_SLEEP_MS` | 1 000 ms | `packages/rs/sensing/src/wake.rs` |
| `PROBE_SETTLE_MS` | 100 ms | `packages/rs/sensing/src/wake.rs` |
| `DIVIDER_SETTLE_MS` | 10 ms | `packages/rs/sensing/src/wake.rs` |
| `WATCHDOG_MS` | 30 000 ms | `apps/rs/node/src/main.rs` |
| `SETUP_WATCHDOG_MS` | `SETUP_WINDOW_MS` + 30 s = 210 s | `apps/rs/node/src/main.rs` |
| `CpuClock::_80MHz` | 80 MHz CPU clock | `apps/rs/node/src/main.rs` (`main`) |

## Where to read the code

- `apps/rs/node/src/main.rs` (module comment, `main`): the ten steps of a wake, the 80 MHz
  setting and the sleep computed after the transport.
- `apps/rs/node/src/main.rs` (`sleep`, `fail`): the single exit of every wake, and the failure
  path that sleeps a full period.
- `apps/rs/node/src/board/sleep.rs` (`wake_cause`, `deep_sleep`): reading the wake cause and
  entering deep sleep with a timer deadline.
- `apps/rs/node/src/board/rtc.rs` (`arm_watchdog`, `BoardRtc`): the RTC watchdog, and the uptime
  that keeps counting through sleep.
- `apps/rs/node/src/board/button.rs` (`arm_wake`): the setup button as a deep-sleep wake source.
- `packages/rs/sensing/src/wake.rs` (`run_wake`, `sleep_ms`, `boot_id`): the measurement order,
  the switched loads, the sleep arithmetic and the boot ID. Host-tested, no hardware needed.
- `packages/rs/sensing/src/button.rs` (`sleep_after`, `arm_button_wake`): how a press resets the
  schedule, and why a held button is not armed.
- [`apps/rs/node/README.md`](../../apps/rs/node/README.md): boot steps, partitions, pins and log
  lines.

Governing items: AD-11 (time, boot ID and uptime) and AD-17 (frame sequence and replay, which the spine also cites for bounded radio time) in the
[architecture spine](../../_bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md);
the power budget (NFR-4) in
[device-hardware.md](../../_bmad-output/specs/spec-coldframe/device-hardware.md).
