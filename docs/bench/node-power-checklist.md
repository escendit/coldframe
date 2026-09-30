# Bench checklist: Node power (Story 4.1)

This checklist is the acceptance test for the Node's sleep current and wake duration (NFR4, NFR12,
NFR13) on a real ESP32-S3. CI builds the firmware and tests the wake-cycle logic on the host; the
current a board draws can only be measured here, by hand.

The budget, from [`device-hardware.md`](../../_bmad-output/specs/spec-coldframe/device-hardware.md#power-budget-nfr-4):
an **average sleep current of at most ~100 µA** and a **short wake** (about 0.3–0.5 s), for a season
on solar and at least 14 days without sun.

> [!WARNING]
> A build without `--features dev-mode` burns an eFuse key block on first boot, and **that cannot
> be undone**. Use a board meant to be a Coldframe Node, and check its key purposes first (Part A of
> the [Hub identity checklist](hub-identity-checklist.md) applies unchanged).

## What you need

- One ESP32-S3 Node wired as in [`apps/rs/node/README.md`](../../apps/rs/node/README.md#pins):
  capacitive soil probe and battery divider behind **high-side** switches (controls pulled down, so
  the ADC pins see 0 V while off; divider midpoint capacitor ≤ 10 nF), BME680 on
  I²C at 0x77, charger status on GPIO6, LiPo cell.
- A power profiler (for example a Nordic PPK2 or a Joulescope) or a µA meter with a burden voltage
  low enough for a 100 mA wake peak, **in series with the battery**, not on USB.
- The `esp` toolchain and `espflash` 4.x. Run commands from `apps/rs/node` after
  `source ~/export-esp.sh`.
- Note the board: a stock dev board with a USB-UART bridge, LDO and power LED draws about 1 mA
  asleep and fails this budget by design. Remove the LED and, if possible, power the module
  through a low-quiescent regulator. Record what you changed under Notes.

## A. Build and flash the release build

- [ ] `cargo build --release --locked` succeeds.
- [ ] `cargo build --release --features dev-mode` fails with
      `the \`dev-mode\` feature is refused in release builds`.
- [ ] Flash with `cargo run --release`. The log shows
      `coldframe-node … wake cause=cold_boot`, then `identity mode=efuse source=…`, `boot id=<n>`
      and `wake done readings=4 seq=<a>..<a+4> … sleep_ms=<≈899 000>`.
- [ ] No line shows a Reading value (no `dev reading`), a key or a root.
- [ ] Disconnect USB. From here the Node runs on the battery only.

## B. Wake behaviour (required, with USB reconnected or a UART tap)

- [ ] After about 15 minutes the Node logs `wake cause=timer` with the **same** `boot id` and a seq
      range that continues the last one (`<a+4>..<a+8>`).
- [ ] Press reset. The next wake logs `wake cause=cold_boot` and `boot id=<n+1>`, and the seq range
      still continues upwards (never restarts, gaps allowed).
- [ ] Unplug the BME680's SDA. The next wake logs `bme680 failed` (or `bme680 not found`) and
      `readings=1`, and the Node still sleeps.
- [ ] Power cut: over **at least 20 wakes**, cut the battery power at random points (during a
      wake and during sleep), then restore it. From the log, confirm that no seq range overlaps an
      earlier one (gaps are allowed) and that the `boot id` never decreases.

## C. Sleep current

- [ ] Put the profiler in series with the battery (or the µA meter, shorted during the wake peak if
      it cannot take it).
- [ ] Record the current over **at least three full periods** (≥ 45 minutes), so each capture holds
      at least three wakes.
- [ ] From the trace, read the **average current while asleep** (between wakes) and the **average
      over whole periods**.
- [ ] With a multimeter or the profiler's GPIO view, check that the probe switch (GPIO4) and the
      divider switch (GPIO5) are **off during sleep**: no current through the probe or the divider.
      The pads float in deep sleep, so if either draws current, check the external pull-downs on
      the switch gates.

## D. Wake duration

- [ ] From the current trace, measure each wake: from the rise out of sleep to the fall back into
      it. Note the typical duration and the peak current.
- [ ] The duration is short: in the order of the 0.3–0.5 s the budget assumes (the probe settle of
      100 ms and the BME680 heater of about 150 ms are the known fixed costs).
- [ ] Record the **first-boot wake** (identity provisioning, radio on to draw the root) separately
      from the steady-state wakes: its duration and peak current go under Notes.
- [ ] On the steady-state (normal) wakes, confirm there is **no radio current spike**: the radio
      must stay off unless a new identity root is drawn.

## E. Record the result

- [ ] Fill in the table below.
- [ ] Copy the average sleep current into the "Measured" row under Power budget in
      [`device-hardware.md`](../../_bmad-output/specs/spec-coldframe/device-hardware.md#power-budget-nfr-4),
      against the ≤ ~100 µA budget, even when it fails.

Pass: average sleep current ≤ ~100 µA, wake short, switches off during sleep, release build.

## Result

| Date | Board | Build | Avg sleep µA | Period avg µA | Wake ms | Pass / fail | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| | | | | | | | |
