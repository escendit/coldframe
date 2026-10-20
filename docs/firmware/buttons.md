# The setup button

The Node has one button. A short press asks for a report now; a long press opens the BLE setup
window. This page shows how a sleeping chip notices the press, how the firmware tells a bounce, a
short press and a long press apart, and how the Node goes back to sleep afterwards. It is part of
the [firmware reference](README.md). Read [deep-sleep.md](deep-sleep.md) first, because every press
is a wake from deep sleep. The setup window itself is on the next page, [ble-setup.md](ble-setup.md).

> [!NOTE]
> Only the Node has a button. The Hub firmware (`apps/rs/hub/src/main.rs`) reads no button: it
> advertises the setup service from boot until it is provisioned, and never after.

## How it works

### Wiring: one switch to ground

The button is a momentary switch from GPIO7 to ground. It is active low: the internal pull-up holds
the pin high while it is released, and pressing it pulls the pin low. GPIO7 is one of the ESP32-S3's
low-power (RTC) pads, so it can wake the chip from deep sleep. The full pin table is in
`apps/rs/node/src/board/pins.rs` and in the [device hardware spec](../../_bmad-output/specs/spec-coldframe/device-hardware.md).

### Waking on the button

The Node spends almost all its time in deep sleep, with the CPU and RAM off. Just before it sleeps,
it arms two wake sources: the RTC timer for the next scheduled measurement, and a low level on the
button. `BoardButton::arm_wake` asks esp-hal for a wakeup with the low-power path and listens for
`Event::LowLevel`. esp-hal then assigns the pad to the `ext0` or `ext1` wake source at sleep entry,
holds the pad and keeps its pull-up on through the sleep.

Every wake from deep sleep is a reset into `main`. `wake_cause` reads why the chip woke: `ext0` or
`ext1` means the button, the timer means a scheduled wake, and anything else is a cold boot.

There is one guard. `arm_button_wake` arms the button only while it reads released. A stuck or
still-held button would otherwise wake the Node again at once, in a loop that drains the cell. In
that case the Node sleeps with the timer wake only and logs `setup button still held; timer wake
only`.

> [!NOTE]
> The button is only looked at on a button wake. A press while the Node is already awake (during a
> scheduled measurement) does nothing, and a button held at power-on is not timed either.

### Timing the press

A press can only be timed while the finger is still on the button. So on a button wake, `main`
calls `classify_press` before any flash or identity work. Only the bootloader delay eats into the
press.

`classify_press` polls the pin every 10 ms and remembers the time of the last poll that saw the
button held. Then:

- **Released before 50 ms**: a bounce, or a tap already over by the first poll. That is
  `Press::Spurious`.
- **Released before 3 s**: `Press::Short`, "report now".
- **Still held at 3 s**: `Press::Long`. It returns at once, without waiting for the release, so the
  Node enters setup mode while the finger is still on the button.
- **A pin read error**: `Press::Spurious`.

The debounce is a floor, not a filter: the first poll that reads the button released ends the
timing. A contact that opens for a moment and is caught by a poll ends the press there, so anything
shorter than 50 ms counts as nothing rather than as a short press.

### What each press does

`wake_plan` turns the wake cause and the press into one of four plans, logged as
`wake plan=…`:

| Wake | Plan | What happens |
| --- | --- | --- |
| Timer or cold boot | `measure` | The scheduled measurement and send. |
| Button, short press | `report-now` | Measure and send at once, outside the 15-minute schedule (N-3). |
| Button, long press (>= 3 s) | `setup` | The BLE setup window, then a measurement that is buffered, not sent. |
| Button, bounce | `sleep-again` | No measurement; straight back to sleep. |

A short press is how you get a fresh value during calibration in seconds instead of waiting up to
15 minutes. It does not start BLE.

A long press is the only path that ever starts BLE. `setup_mode` loads the setup code from
`cf_setup` (drawing it on the first long press), prints it as `setup code=…`, raises the RTC
watchdog to the window plus 30 s, and runs the setup window for at most 180 s. The window starts
when advertising starts, not at the press. It ends early once an enrolment session's connection has
ended. After the window the Node measures as usual, but the report only goes into the flash buffer:
it is sent at the next wake. [ble-setup.md](ble-setup.md) covers the window and the protocol (AD-25).

### Sleeping again

`sleep_after` decides how long the next sleep is. A scheduled `measure` wake sleeps for the rest of
its period, so the schedule keeps its rhythm. Every press (`report-now`, `setup` and
`sleep-again`) restarts the schedule: the Node sleeps a full 15-minute period from the end of the
wake. The log line `sleep ms=900000 button_armed=true` shows it.

Before sleeping, `arm_button_wake` checks the button again. After a long press the finger is
normally off the button by the time the window has run, so the button wake is armed again.

## Key constants and timings

| Constant | Value | Where |
| --- | --- | --- |
| `PRESS_POLL_MS` | Poll interval 10 ms | `packages/rs/sensing/src/button.rs` |
| `DEBOUNCE_MS` | Debounce floor 50 ms | `packages/rs/sensing/src/button.rs` |
| `LONG_PRESS_MS` | Long press 3 000 ms | `packages/rs/sensing/src/button.rs` |
| `SETUP_WINDOW_MS` | Setup window 180 000 ms (3 min) | `packages/rs/setup/src/node.rs` |
| `NODE_SESSION_IDLE_TIMEOUT_MS` | Idle connection dropped after 60 000 ms | `packages/rs/setup/src/node.rs` |
| `SETUP_WATCHDOG_MS` | Watchdog in setup mode: window + 30 s | `apps/rs/node/src/main.rs` |
| `WAKE_PERIOD_MS` | Full sleep after a press 900 000 ms (15 min) | `packages/rs/sensing/src/wake.rs` |
| `take_pins!` (macro) | Button pin GPIO7, active low | `apps/rs/node/src/board/pins.rs` |

## Where to read the code

- `packages/rs/sensing/src/button.rs` (`classify_press`): the press timing, host-tested against a
  fake pin and timer in `tests/rs/sensing/tests/button.rs`.
- `packages/rs/sensing/src/button.rs` (`wake_plan`, `sleep_after`): the four plans and the sleep
  each one takes; BLE is reachable only from `WakePlan::Setup`.
- `packages/rs/sensing/src/button.rs` (`arm_button_wake`): the stuck-button guard.
- `apps/rs/node/src/board/button.rs` (`BoardButton`): the pull-up and the deep-sleep wake on a low
  level.
- `apps/rs/node/src/board/sleep.rs` (`wake_cause`, `deep_sleep`): how `ext0`/`ext1` becomes a
  button wake, and where the wake is armed.
- `apps/rs/node/src/main.rs` (`main`, `setup_mode`): the press timed first, then the plan, the
  setup window and the final sleep.
- `packages/rs/setup/src/node.rs` (`run_node_setup`): the window's deadline and how it ends.

Related reading:

- [Node setup bench checklist](../bench/node-setup-checklist.md): the hand test of each press,
  including the shortest press that still counts.
- [Node README](../../apps/rs/node/README.md): the wake sequence as a whole.
- [Device hardware spec](../../_bmad-output/specs/spec-coldframe/device-hardware.md): N-3 (report
  now) and the Story 4.2 button and setup-mode notes.
- [Architecture spine](../../_bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md):
  AD-25, the BLE setup protocol the long press opens.
