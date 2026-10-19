# Power and placement

The Node runs on an 800 mAh LiPo cell charged by a small solar panel. This page covers where to put
the panel, the sleep-current budget the Node is designed for, and why the soil and battery
percentages are approximate. The parts are in [`hardware/bom.md`](../../hardware/bom.md).

## Panel placement

- **Above the canopy.** Plants grow over the panel during summer, and shading is the real risk, not
  cloud. Mount the panel on a stake higher than the plants will grow by the end of the season.
- **Angled to the sun.** Face it south in the northern hemisphere (north in the southern), tilted
  at about your latitude. A steeper tilt sheds rain and dirt better.
- **Not shaded by the plant**, a fence or the house at any time of day. Even a partly shaded panel
  delivers little.
- Keep it clean, and keep the Node's antenna end free of the panel's metal frame
  ([enclosure.md](../../hardware/enclosure.md#node-enclosure)).

A shaded panel shows up in the app as falling battery % while the Node reports *not charging*,
until the low-battery Alert fires. If you see that in midsummer, look for leaves on the panel.

## Solar and autonomy

- **About 1 W** is enough. A ~1 W panel yields roughly 500 mAh a day into the cell in full sun, and
  25–50 mAh a day in heavy overcast. Both are far above what a well-built Node uses.
- So the question is not runtime but **autonomy**: how many days without sun the cell bridges. The
  design target is **at least 14 days without sun**, on top of a season on solar.
- The charger stops charging below 0 °C and above about 45 °C, through the cell's NTC. In spring
  frost or a sun-heated box the Node runs on the cell alone for a while; that is expected.

## Sleep-current budget

The Node wakes every 15 minutes (`WAKE_PERIOD_MS` in
[`packages/rs/sensing/src/wake.rs`](../../packages/rs/sensing/src/wake.rs)), measures for a fraction
of a second, sends over ESP-NOW and sleeps again. Between wakes it draws only its sleep current, so
the sleep current decides the autonomy.

**The budget is an average sleep current of at most about 100 µA**, with short wakes (about
0.3–0.5 s). That covers a 6-month season even with no sun at all (about 3.5 mAh a day from about
640 mAh usable), and far more than 14 dark days on solar.

| Board | Sleep current | Use per day | Days without sun |
| --- | --- | --- | --- |
| Optimized custom board | ~20 µA | ~1.6 mAh | about a year (self-discharge limits it) |
| Reasonable custom board | ~50 µA | ~2.5 mAh | about 250 |
| Budget | ~100 µA | ~3.5 mAh | about 180 |
| Stock dev board | ~1 mA | ~25 mAh | about 25 |

These are estimates from the design, **not measurements**. The sleep current of the reference
design has **not been measured yet**.

**Why a stock dev board draws about 1 mA asleep:** its USB-UART bridge, its power LED and its
always-on regulator draw current all the time, whatever the ESP32-S3 does. The chip itself sleeps
at a few microamps. A dev board works for a Node on the bench and bridges about 25 dark days on
solar, but it misses the budget by design. The [reference Node](../../hardware/node.md) avoids all
three: a bare module, no USB-UART bridge (it flashes over the chip's own USB), no LED, and a
low-quiescent LDO. The probe and the battery divider are switched off while asleep, and the BME680
sleeps between forced-mode measurements.

### Measure it

Follow [`docs/bench/node-power-checklist.md`](../bench/node-power-checklist.md). In short:

- Flash a **release build** (a dev-mode build logs more and is not representative).
- Put a power profiler or a µA meter **in series with the battery**, not on USB.
- Record **at least three full periods** (45 minutes or more), so the capture holds at least three
  wakes.
- Read the average current while asleep and the average over whole periods. **Pass: about
  100 µA or less.**
- Check that the probe and divider switches are off while asleep; if either draws current, check
  the pull-downs on their enable pins.

## Why the readings are approximate

### Soil moisture

Soil moisture % is an estimate, not a measurement of water content:

- **Two-point calibration.** The Server maps the probe's raw count linearly between two points you
  record in the app, a dry one (0 %) and a wet one (100 %), clamps it to 0–100 and rounds it to the
  nearest 5 % ([`CalibrationMath.cs`](../../apps/cs/server/Sensors/CalibrationMath.cs)). Real
  probes are not linear between the two points.
- **Temperature and salinity** shift the raw reading. There is no temperature compensation in V1.
  The Node records the air temperature with every Reading, so the effect can be analysed later.
- **Settling.** A freshly inserted probe needs **2–3 days** to settle before its readings are
  stable. Calibrate it in the soil it will stay in, after it has settled.
- **Drift.** Water creeping into an unsealed probe makes it drift over the season. Seal it
  ([enclosure.md](../../hardware/enclosure.md#sealing-the-soil-probe)).

Use the Thresholds as "drier than usual" and "wetter than usual" for that one probe, not as
absolute values that compare across Nodes.

### Battery

Battery % is approximate too:

- It comes from the cell voltage, read through the switched divider, mapped with a typical
  single-cell LiPo **discharge curve** of 21 points, linear between them
  ([`battery.rs`](../../packages/rs/sensing/src/battery.rs)). Every cell, and every cell's age and
  temperature, gives a slightly different curve.
- The curve is flat in the middle, so a few millivolts of ADC error move the percentage by several
  points.
- **It is worse while charging:** the charger lifts the cell voltage, so the percentage reads high
  while the panel is in sun, and drops back once charging stops.

Watch the trend over days, and the *charging* / *not charging* status, rather than a single value.
