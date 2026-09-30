---
title: 'Story 4.1: Node firmware foundation: wake, measure, sleep'
type: 'feature'
created: '2026-09-30'
baseline_revision: 'd95c26cf5b42a17b8f5416f6e48420e1d6832cc9'
status: 'awaiting-operator'
review_loop_iteration: 1
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
  - '{project-root}/_bmad-output/specs/spec-coldframe/device-hardware.md'
warnings: ['oversized']
operator_actions:
  - "On an ESP32-S3 Node wired to the reference wiring in apps/rs/node/src/board/pins.rs (high-side probe and divider switches with pulled-down controls, divider midpoint capacitor at most 10 nF, BME680 at 0x77), flash the release build from apps/rs/node and run Part A of docs/bench/node-power-checklist.md: measure the average sleep current over at least three full 15-minute periods and the steady-state wake duration."
  - "Run the now-required Part B of docs/bench/node-power-checklist.md on the same Node: confirm timer wakes log reset=deep_sleep with an unchanged boot id and the RTC watchdog does not fire during sleep, a reset continues reading_seq without overlap, and at least 20 random power cuts during wakes never produce an overlapping seq range or a decreasing boot id."
  - "Run Part D of the checklist: record the first-boot wake (identity provisioning with the radio on) separately from steady-state wakes, and confirm normal wakes show no radio current spike."
  - "Record the results (date, board, build, average sleep µA, period average µA, wake ms, pass or fail) in the Result table of docs/bench/node-power-checklist.md, replace the 'pending' Measured row under Power budget in _bmad-output/specs/spec-coldframe/device-hardware.md with the measured value against the ~100 µA budget, and commit both."
deferred:
  - summary: >-
      The Node flash adapter's plain-NOR write path, which the counters' power-loss safety depends on, is not checked by any automated test.
    evidence: |-
      Host counter tests run over MockFlash only; apps/rs/node/src/board/flash.rs is only cross-compiled in CI. Reverting BoardFlash::write to esp-storage's read-erase-rewrite would pass every automated check. Mitigated by the bench power-cut step in docs/bench/node-power-checklist.md Part B and the DW-51 note; a host test needs the board adapter extracted into a crate that can run over a fake NOR backend (DW-51).
    location: >-
      apps/rs/node/src/board/flash.rs (BoardFlash::write)
    severity: low
---

<intent-contract>

## Intent

**Problem:** There is no Node firmware. Epic 4 needs a Node that wakes every 15 minutes, takes one Reading per Sensor with a shared `measured_at` and a `reading_seq` that never repeats across resets (AD-17, FR4), reports battery % and charging status, and deep-sleeps on a season's power budget (NFR4, NFR12, NFR13).

**Approach:** Put all decision logic (wake cycle, power sequencing, persisted counter reservation, battery mapping, sleep scheduling) in a new host-tested no_std crate `packages/rs/sensing` behind `coldframe-hal` traits. Add a thin ESP32-S3 firmware `apps/rs/node`, modelled on `apps/rs/hub`, whose `src/board` adapts esp-hal to those traits and reuses `coldframe-crypto` for the eFuse/dev-mode identity exactly as Story 3.2 does. Ship a bench checklist for the sleep-current measurement, which only a human can do.

## Boundaries & Constraints

**Always:**
- Only `apps/rs/node/src/board` names esp-hal types. Logic crates stay `no_std` and build without the `mock` feature.
- Pin every new dependency exactly (`=x.y.z`), as AD-15 and the Hub do. The Node uses the same esp-hal/esp-rtos/esp-radio versions as `apps/rs/hub/Cargo.toml`.
- A `reading_seq` value is handed out only after the raised ceiling has been written to flash and read back. On boot, the counter jumps to the stored ceiling. Values are strictly increasing for the life of the partition, and gaps are allowed.
- The probe and battery-divider switches are turned off on every path out of the measurement, including every error path.
- All Readings of one wake share one `measured_at`, captured once. It is `Synced{unix_ms}` when `Rtc::unix_time_millis()` is `Some`. Otherwise it is `Unsynced{boot_id, uptime_ms}` (AD-11).
- Battery % comes from a LiPo discharge-curve table with linear interpolation, clamped to 0–100, and is always flagged `approximate: true`.
- The `dev-mode` feature keeps the Hub's `compile_error!` guard, so it can never be combined with a release build (AD-12).
- Write host tests first, and see them fail before implementing (red → green).

**Never:**
- No ESP-NOW transport, frame sealing, Reading buffer, acknowledgement, downlink or RTC time setting (Story 4.4). No setup button, BLE or pairing (Story 4.2).
- Nothing is written to the ESP-IDF `nvs` partition. Firmware changes never touch `apps/rs/hub`.
- In release builds, Reading values, keys or roots never appear in logs. Values may be logged only in `dev-mode` builds.
- A corrupt or unreadable counter never falls back to 0.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Happy wake | all sensors OK, counter ceiling 40 | 4 Readings (slots 0–3) with seq 40,41,42,43, one `measured_at`; battery level + charging; ceiling 44 persisted before use | none |
| Fresh device | counter partition fully erased | seq starts at 0 | none |
| BME680 fails | `EnvSensor` returns error | only the soil Reading (1 seq reserved); switches off | warn log, still sleeps |
| Soil ADC fails | `RawAdc` error | the 3 env Readings; probe switch off | warn log |
| Battery ADC / charger pin fails | error | `battery: None` / `ChargeStatus::Unknown`; Readings unaffected | warn log |
| Counter write fails or read-back differs | `Flash` error / mismatch | no Readings issued this wake; battery/charging still reported | error log, still sleeps |
| Counter corrupt | non-erased slots exist but no valid record in either sector | `CounterError::Corrupt`; no Readings | error log, never restarts at 0 |
| Torn last record | one slot partially written | ignored; the max of the valid records wins; the next write goes to the next erased slot | none |
| Sector full | every slot in the active sector used | erase the *other* sector, write the new record there; the old sector stays valid until then | none |
| Long wake | elapsed wake time ≥ period | sleep = `MIN_SLEEP_MS` (1 000) | none |
| Cold boot vs timer wake | reset reason ≠ deep-sleep timer wake | boot counter raised by 1 → new `boot_id`; a timer wake reuses the stored `boot_id` | as for the counter |

</intent-contract>

## Code Map

- `apps/rs/hub/Cargo.toml`, `rust-toolchain.toml`, `.cargo/config.toml`, `partitions.csv` -- the template for the Node crate: exact esp crate versions, `dev-mode` feature, `opt-level="s"`, target `xtensa-esp32s3-none-elf`, `build-std`, no `[env]`. Partitions use subtype `undefined` (0x06) and are looked up by label, because espflash 4.5 panics on custom subtypes.
- `apps/rs/hub/src/main.rs:32-35,78-81,152-207,91` -- the dev-mode guard, the `MODE` const, the boot sequence (`esp_hal::init`, heap, `esp_rtos::start`), identity via `provision_efuse`/`provision_dev`, and `halt()`.
- `apps/rs/hub/src/board/{efuse,hmac,flash,radio,rng,rtc,timer}.rs` -- adapters to copy into the Node's `board/`. `flash.rs:19,64` has `BoardStorage::partition(label)` with 4096-byte sectors. The radio is the TRNG entropy source.
- `packages/rs/crypto/src/identity.rs:193-205,222,311` -- `provision_*` only calls `radio.enable()` when generating a new root. The Node's radio adapter must therefore start esp-radio lazily, inside `enable()`, so a normal wake never powers the radio.
- `packages/rs/hal/src/{adc.rs:23,gpio.rs,rtc.rs:23,timer.rs,flash.rs:36,lib.rs:42-53,mock.rs}` -- the existing traits and mocks: `Adc` (mV only), `OutputPin`/`InputPin`, `Rtc`, async `Timer`, NOR `Flash`, and `MockFlash`/`MockAdc`/`MockRtc`/`MockPin`/`MockTimer`. What is missing: a raw ADC count and a BME680-style environment sensor.
- `packages/rs/setup/src/store.rs:33-63` and `crypto/src/identity.rs:26-36` -- the record convention to follow: `MAGIC ‖ version ‖ payload ‖ SHA-256(...)[0..4]`.
- `tests/rs/uplink/` -- the test-crate pattern (`coldframe-<x>-tests`, `.workspace = true`, hal `features=["mock"]`, `tests/common/mod.rs`, `block_on` for async).
- `Cargo.toml` (root) -- workspace members; `apps/rs` is excluded.
- `.github/workflows/ci.yml:156-189` (the Rust job's no-mock build list) and `:195-267` (the Firmware job, `working-directory: apps/rs/hub`, cache `workspaces: apps/rs/hub`).
- `docs/bench/hub-identity-checklist.md` -- the checklist shape, ending in a `## Result` table.
- `_bmad-output/specs/spec-coldframe/device-hardware.md:26-49` -- the power budget (≤ ~100 µA), the charger status pin examples, the switched divider, and BME680 forced mode (heater ~150 ms, raw gas Ω, no BSEC). It has no pin table and no measured-value row yet.
- `apps/rs/spike-hub-radio/src/bin/node.rs` -- the only prior Node code (ESP-NOW spike); reference only.
- `bosch-bme680 = 1.0.4` (crates.io, no_std, embedded-hal 1.0) -- the candidate BME680 driver behind the board adapter.

## Tasks & Acceptance

**Execution:**
- `packages/rs/hal/src/{sensor.rs,lib.rs,mock.rs}` -- add `trait RawAdc { fn read_raw(&mut self) -> Result<u16, AdcError> }` and `trait EnvSensor { fn measure_forced(&mut self) -> Result<EnvSample, EnvError> }`. `EnvSample` has `temperature_milli_c: i32`, `humidity_milli_pct: u32` and `gas_ohms: Option<u32>`: `None` when the BME680 flags the gas conversion invalid or the heater unstable, which costs only the gas Reading. `EnvError` includes `OutOfRange` for a non-finite temperature or humidity. Add `MockRawAdc` and `MockEnvSensor` (settable result, call count). This adds traits instead of changing `Adc`, so no existing implementor breaks.
- `packages/rs/sensing/{Cargo.toml,src/lib.rs,src/battery.rs,src/counter.rs,src/wake.rs}` -- new crate `coldframe-sensing` (`no_std`, rust-version 1.95, deps: `coldframe-hal`, `sha2`, `heapless`).
  - `env`: `pub fn env_sample_from(temperature_c: f32, humidity_pct: f32, gas_ohms: Option<f32>) -> Result<EnvSample, EnvError>`. This is the only float-to-integer conversion of BME680 output, and the board adapter calls it with the driver's values. It rounds half away from zero to milli-units, and clamps humidity to 0..=100 000. A non-finite temperature or humidity gives `Err(OutOfRange)`. A non-finite or negative gas value gives `None`, and gas saturates at `u32::MAX`.
  - `battery`: divider scaling, the discharge table (4200→100, 4150→95, 4110→90, 4080→85, 4020→80, 3980→75, 3950→70, 3910→65, 3870→60, 3850→55, 3840→50, 3820→45, 3800→40, 3790→35, 3770→30, 3750→25, 3730→20, 3710→15, 3690→10, 3610→5, 3270→0 mV) → `BatteryLevel{cell_millivolts, percent, approximate}`.
  - `counter`: `ReservedCounter<F: Flash>` with a magic, over two 4096-byte sectors of 32-byte slots. Each record is `magic(4) ‖ 0x01 ‖ 0x00×3 ‖ ceiling u64 BE ‖ SHA-256(first 16 bytes)[0..4] ‖ 0xFF×12`. It provides `reserve(n) -> Result<Range<u64>, CounterError>` and `current() -> Result<u64, CounterError>`, with the matrix semantics above.
  - `wake`: `async fn run_wake(...) -> WakeOutcome{report, sleep_ms}`.
    - Inputs: probe and divider switches (`OutputPin`), the soil `RawAdc` (mean of 8 samples after `PROBE_SETTLE_MS = 100`), `EnvSensor`, the battery `Adc`, the charger `InputPin` (active-low const), `Rtc`, `Timer`, the seq counter, `boot_id`, and the wake-start uptime.
    - The report is `WakeReport{measured_at, readings: heapless::Vec<Reading,4>, battery, charging}`.
    - The slot order is fixed: 0 soil raw (u16), 1 temperature (milli-°C), 2 humidity (milli-%RH), 3 gas (Ω).
    - The period is `WAKE_PERIOD_MS = 900_000`, and `sleep_ms = max(period − elapsed, MIN_SLEEP_MS)`.
  - Battery: after the divider switch goes on, wait `DIVIDER_SETTLE_MS = 10`, then take the mean of 4 conversions. The divider is off on every path.
  - A gas value of `None` omits only slot 3. Temperature and humidity are still issued.
  - A `boot_id` helper: a cold boot calls `reserve(1)` and takes the new ceiling. A timer wake reads `current()`, but when that is 0 (no cold boot ever completed its reservation) it reserves like a cold boot, so a Reading never carries `boot_id` 0.
- `tests/rs/sensing/{Cargo.toml,tests/battery.rs,tests/counter.rs,tests/wake.rs,tests/env.rs,tests/common/mod.rs}` -- red-first tests for every matrix row, plus:
  - `env_sample_from` converts known values: (21.5, 55.25, Some(120 000.4)) → 21 500 / 55 250 / Some(120 000); (−3.4996, …) → −3 500; humidity 101.2 → 100 000; gas `None`/NaN/negative → `None`; NaN temperature → `OutOfRange`;
  - a gas `None` sample yields soil + temperature + humidity with consecutive seqs;
  - the battery value is the mean of 4 conversions, read after the settle delay;
  - a timer wake with an erased boot counter reserves and never returns 0;
  - table endpoints, clamping and interpolation;
  - no seq repeats across simulated resets that share `MockFlash` contents;
  - the ceiling is persisted before the range is returned;
  - the sector rollover never leaves both sectors without a valid record;
  - the switch high→low order on success and on each failure;
  - `Synced` vs `Unsynced` `measured_at`;
  - the sleep arithmetic.
- `Cargo.toml` (root) -- add `packages/rs/sensing` and `tests/rs/sensing` as members.
- `apps/rs/node/{Cargo.toml,Cargo.lock,rust-toolchain.toml,.cargo/config.toml,partitions.csv}` -- crate `coldframe-node`, with the Hub's settings but without mbedtls/reqwless/trouble/embassy-net. Add `bosch-bme680 =1.0.4`. Partitions:
  - `nvs` 0x9000/0x3000
  - `phy_init` 0xC000/0x1000
  - `cf_ident` 0xD000/0x1000
  - `cf_seq` 0xE000/0x2000
  - `cf_boot` 0x10000/0x2000
  - `factory` 0x20000/0x3E0000

  The gap 0x12000–0x1FFFF is left free for `cf_setup` in Story 4.2.
- `apps/rs/node/src/main.rs` and `src/board/{mod,pins,efuse,hmac,flash,radio,rng,rtc,timer,sensors,sleep}.rs` -- the boot flow:
  1. init, then arm the RTC watchdog (RWDT) at 30 s so a hung wake or a halted panic resets the chip instead of staying awake
  2. wake cause (`Rtc` reset/wakeup reason)
  3. `boot_id` from `cf_boot`. This comes *before* identity, so a later failure cannot leave a power-on without its own boot ID.
  4. identity (release eFuse / `dev-mode` `cf_ident`), log `identity mode= source= device_id=`
  5. `run_wake`
  6. log the counts and the seq range, printing `seq=none` when no Reading was issued (values only in `dev-mode`)
  7. deep sleep with an RTC timer wakeup source for `sleep_ms`

  Run the CPU at `CpuClock::_80MHz` to cut active current. If esp-radio `=1.0.0-beta.1` refuses to start at 80 MHz (it only starts on first boot), keep `CpuClock::max()` and say why in a comment.
  - `board/flash.rs` must program aligned writes as plain NOR writes (esp-storage `as_nor_flash()`, with esp-bootloader-esp-idf's `embedded-storage` feature and `embedded-storage =0.3.2`). It must never read-erase-rewrite a counter sector.
  - `BoardEnv::measure_forced` only maps the driver's result: it calls `coldframe_sensing::env_sample_from` and does no arithmetic of its own.

  Board adapters:
  - `board/pins.rs` holds the reference wiring (GPIO1 soil ADC1, GPIO2 battery ADC1, GPIO4 probe switch, GPIO5 divider switch, GPIO6 charger status with pull-up, I²C SDA GPIO8 / SCL GPIO9, BME680 at 0x77). Use ADC1 only, because ADC2 conflicts with the radio.
  - The `Rtc` adapter's `uptime_millis` comes from the RTC timer, which keeps counting through deep sleep.
  - The radio adapter starts esp-radio lazily.
  - Identity or counter-open failures log the error and deep-sleep one period rather than parking awake.
- `apps/rs/node/README.md` -- purpose, boot steps, build/flash commands, identity modes (with the Hub's irreversible-burn warning), the partition table, the pin table and log lines.
- `.github/workflows/ci.yml` -- in the Rust job, add `-p coldframe-sensing` to the no-mock build. In the Firmware job, add a Node step that asserts `cargo build --release --features dev-mode` fails with the `compile_error!` message. In the Firmware job, add Node steps (build release + `dev-mode`, `cargo fmt --check`, both clippy runs with `-D warnings`) using a step-level `working-directory: apps/rs/node`, and add `apps/rs/node` to the cache `workspaces`. Keep the job name `Firmware`.
- `docs/bench/node-power-checklist.md` -- the bench procedure (power profiler or µA meter in series with the battery; average current over ≥3 full periods; wake duration from the current trace; probe/divider off during sleep; release build) and a `## Result` table: `| Date | Board | Build | Avg sleep µA | Wake ms | Pass / fail | Notes |`.
- `_bmad-output/specs/spec-coldframe/device-hardware.md` -- add a "Node reference wiring" table matching `pins.rs`, and a "Measured" row under Power budget that points to the checklist result (value pending).

**Acceptance Criteria:**
- Given `apps/rs/node`, when `cargo build --release` and `cargo build --features dev-mode` run on the esp toolchain, then both succeed with clippy clean, and the release build refuses `dev-mode`.
- Given the host workspace, when `cargo test --workspace` runs, then the wake-cycle, `reading_seq` persistence and battery-mapping tests pass, and they were observed failing before the implementation.
- Given a Node on the bench, when the operator follows `docs/bench/node-power-checklist.md`, then the average sleep current is ≤ ~100 µA, or the measured value is recorded against the budget in device-hardware.md, and the wake stays short. This is a manual operator action.

## Spec Change Log

### 2026-09-30 — Review pass 1 loopback
- **Triggering findings:**
  - `BoardEnv::measure_forced` failed the whole sample when the BME680 flagged gas invalid, so valid temperature and humidity Readings were lost. The spec fixed `EnvSample.gas_ohms` as `u32`.
  - The BME680 float-to-integer conversion lived in the board adapter, where no host test runs it.
- **Amended:**
  - `EnvSample.gas_ohms: Option<u32>` and `EnvError::OutOfRange`.
  - A new pure `coldframe_sensing::env::env_sample_from`, with its tests.
  - Folded in alongside:
    - the boot ID is reserved before identity, and a timer wake whose boot counter is 0 reserves like a cold boot;
    - battery settle and averaging;
    - the RWDT at 30 s;
    - an 80 MHz CPU;
    - `seq=none` logging;
    - a CI assertion that release + `dev-mode` fails;
    - the rule that flash writes are plain NOR;
    - a note on switch pull-downs.
- **Known-bad state avoided:**
  - Temperature and humidity dropped whenever the gas heater is unstable.
  - Untested unit conversion shipping wrong values.
  - Readings stamped with a previous power-on's boot ID after a failed cold boot.
  - A hung wake draining the battery awake.
- **KEEP:** Attempt 1 (saved at `/tmp/claude-1000/-var-home-simon-work-escendit-coldframe/8174ac65-7641-4fe4-97ba-e7304fedcff8/scratchpad/spec-4-1-attempt-1.patch`; apply it with `git apply` and then change it) was otherwise sound and must survive:
  - `ReservedCounter` (scan with max over both sectors, a slot consumed even on a failed write, erase-other rollover, `Corrupt` when slots are written but none is valid) and its 21 tests;
  - `run_wake` structure with `Faults` and `WakeOutcome`, the `Sensors` struct, and switch-off on every path;
  - the battery table and `Divider`;
  - `fail()`, which deep-sleeps one period;
  - the lazy radio;
  - the partition table, `pins.rs`, README, bench checklist, device-hardware.md wiring table and "Measured: pending" row;
  - DW-51 in deferred-work.md;
  - the CI Node steps.

## Review Triage Log

### 2026-09-30 — Review pass
- verdicts: 20 findings — high 0, medium 5, low 7, false 4, maybe-false 4
- findings:
  - `[medium]` `[patch]` A failed cold boot (identity or partition `fail()` before `boot_id`) makes the next timer wake reuse the previous power-on's boot ID while uptime restarted — folded into the loopback spec: reserve the boot ID before identity.
  - `[medium]` `[bad_spec]` An invalid gas reading (`gas_resistance` None) fails the whole BME680 sample, dropping valid temperature and humidity — spec amended: `gas_ohms: Option<u32>`, only slot 3 omitted.
  - `[maybe-false]` `[defer]` The battery is read immediately after the divider switch turns on, with a single sample — whether it reads low depends on the ADC input capacitance on the board. Folded into the amended spec as a 10 ms settle plus the mean of 4.
  - `[maybe-false]` `[defer]` No watchdog bounds a wake, so a hung driver or a panic halt stays awake — it is unproven that any hang is reachable. Folded into the amended spec as an RWDT at 30 s.
  - `[low]` `[patch]` The BME680 measurement busy-waits at 240 MHz — folded into the amended spec: 80 MHz CPU.
  - `[low]` `[reject]` The period drifts by the bootloader time each wake — about 0.3 s per 15 min, and the fix needs RTC-memory deadlines; users would not notice.
  - `[maybe-false]` `[defer]` Switch pads float in deep sleep with no pad hold — depends on the reference board's pull-downs; settle it with checklist step "probe/divider off during sleep". The amended spec adds documentation.
  - `[low]` `[patch]` CI does not assert that release + `dev-mode` fails — folded into the amended spec as a CI step. The FR-1 canary part is `false`: the Node carries no credentials, and dev logging is cfg'd out.
  - `[false]` `[reject]` A default build burns an eFuse — this is the designed AD-12 first-boot path, identical to the Hub and warned about in the README.
  - `[false]` `[reject]` The flash adapter falls back to erase-rewrite for unaligned writes — counter records are 32 bytes at 32-byte-aligned offsets, so counters never take that path.
  - `[low]` `[reject]` Out-of-range temperature/humidity/gas are mislabelled or saturated — the values are physically unreachable (±2 million °C, >4 GΩ). Superseded anyway by `env_sample_from`'s `OutOfRange`.
  - `[low]` `[patch]` The log prints `seq=0..0` when no Reading was issued and double-logs a missing BME680 — folded into the amended spec: `seq=none`. The double log is accurate, so it stays.
  - `[medium]` `[patch]` (edge) Timer wake reuses the old boot ID after a failed cold boot — same root cause as the first row.
  - `[low]` `[patch]` (edge) A timer wake with an erased `cf_boot` returns boot_id 0 — folded into the amended spec: reserve when `current()` is 0.
  - `[medium]` `[bad_spec]` (edge) Gas None discards temperature and humidity — same root cause as the second row.
  - `[maybe-false]` `[reject]` (edge) A NaN from the driver becomes a 0 Reading — unproven that the driver emits NaN; if true it is only `low`. The amended `env_sample_from` returns `OutOfRange`.
  - `[medium]` `[bad_spec]` (verification-gap) The BME680 unit conversion in the board adapter is untested on the host — spec amended: pure `env_sample_from` in coldframe-sensing with tests.
  - `[low]` `[defer]` (verification-gap) The plain-NOR flash write that the counters' power-loss safety relies on is not checked by any test — needs hardware or the DW-51 board-crate extraction. The amended spec makes the rule explicit.
  - `[false]` `[reject]` (intent) The spec frontmatter lacks `awaiting-operator`/`operator_actions` — finalization sets them; this is not a code defect.
  - `[false]` `[reject]` (intent) The AC1 on-device behaviour is only host-tested — covered by the operator's bench actions at finalization (checklist parts A and B). The code surface matches the AC3 host-test reading.

### 2026-09-30 — Review pass
- verdicts: 19 findings — high 0, medium 1, low 14, false 4, maybe-false 0
- findings:
  - `[false]` `[reject]` `wake_cause()` trusts only the wakeup-cause register, so a watchdog reset after a timer wake would keep the old boot ID — disproved: esp-hal 1.2.2 `rtc_cntl::wakeup_cause()` (mod.rs:740-745) returns an empty set unless the reset reason is `CoreDeepSleep`.
  - `[false]` `[reject]` The RWDT may keep counting through the 15-minute deep sleep and reset every wake — disproved: esp-hal `Rwdt::set_enabled` sets `wdt_pause_in_slp` (rtc_cntl/mod.rs:563). The bench Part B still confirms it on hardware.
  - `[medium]` `[patch]` The switch topology was unstated, and "high = on, gate pulled down" reads as a low-side switch that leaves GPIO2 at up to 4.2 V while off — patched: pins.rs, the README and device-hardware.md require high-side switching (P-MOSFET or load switch).
  - `[low]` `[patch]` The 10 ms divider settle is unjustified against the ADC source impedance and any midpoint capacitor — patched: the docs require a midpoint capacitor ≤ 10 nF (RC 0.5 ms), or raising `DIVIDER_SETTLE_MS` to ≥ 5·RC.
  - `[low]` `[defer]` carried: The plain-NOR flash adapter that the counters' power-loss safety relies on is untested — recorded in frontmatter `deferred`. The missing "torn slot 0 after rollover" case was traced: the scan keeps the full sector as best, so it rolls over again and erases before writing. Correct, so no test was added.
  - `[low]` `[reject]` `Faults::switch` does not say which switch failed — a switch GPIO failing is rare, and the fix adds public fields; the bench checklist localises a stuck switch by current.
  - `[low]` `[reject]` The BME680 heater target uses a fixed 20 °C ambient on every wake — gas is raw and watched only, with no Thresholds. A fixed target is consistent across wakes; improving it needs persisted state.
  - `[low]` `[reject]` carried: The period drifts by the bootloader time — about 0.3 s per 15 min, and the fix needs RTC-memory deadlines.
  - `[low]` `[reject]` `image_guard.rs` checks only the Hub for a build-time `[env]` — FR-1 concerns Wi-Fi credentials, which the Node never handles, and the Node's config has no `[env]`.
  - `[low]` `[patch]` The bench checklist did not cover first-boot vs steady-state wakes, radio-off wakes or power cuts, and marked Part B optional — patched: Part B required, a power-cut step, first-boot recorded separately, a radio-spike check, and a "Period avg µA" column.
  - `[low]` `[reject]` The pin table is repeated in pins.rs, the README and device-hardware.md — a docs-structure change with no functional harm.
  - `[low]` `[patch]` New lines exceed 120 columns — patched: ci.yml and packages/rs/README.md prose reflowed. One Markdown table row stays long, like its neighbours.
  - `[low]` `[patch]` The CI comment did not match the Node steps; the Node shares the Hub's cache key — patched the comment. The cache part is `false`: rust-cache hashes every listed workspace's lockfile.
  - `[low]` `[reject]` (edge) If the cold-boot boot-counter stage itself fails, the next timer wake reuses the stored boot ID — it needs a flash failure on `cf_boot` during a cold boot, in which case `cf_seq` is likely failing too and no Readings are issued. The fix needs RTC-memory state. The wording was corrected (next row).
  - `[low]` `[reject]` (edge) carried: the wake start is read after the bootloader, so the period drifts.
  - `[false]` `[reject]` (edge) When every Sensor fails and the seq scan errors, the log says "reading_seq failed" — the log is truthful: the counter partition really is failing.
  - `[low]` `[patch]` (edge) The claim "boot ID first, so a later failure cannot leave a power-on without its own boot ID" is overstated — patched the wording in main.rs and the README to name the residual case.
  - `[low]` `[patch]` (verification-gap) Nothing verifies that counter writes reach the chip as plain NOR programs — patched: a bench power-cut step (≥ 20 random cuts, no overlapping seq ranges, no decreasing boot id) and a DW-51 line requiring plain NOR writes for `cf_seq`/`cf_boot`. The automated gap is deferred (see above).
  - `[false]` `[reject]` (intent) The finalization (`awaiting-operator`, `operator_actions`, Auto Run Result, commit) is not done yet — done at this finalization. The operator actions include the now-required Part B on-device check of AC1.

## Design Notes

- **Why every wake writes flash:** a deep-sleep wake is a reboot, so "jump to the stored ceiling and raise it" (AD-17) runs on every wake. It reserves exactly the number of Readings about to be issued. That is about 96 writes/day into 32-byte slots over two sectors, or one sector erase every ~1.3 days (128 slots per sector), which is far below NOR endurance. No RTC-RAM state (and no `unsafe`) is needed. Taking the maximum across both sectors makes the ping-pong rollover power-loss safe.
- **BME680 stays powered:** its sleep current is ~0.15 µA and forced mode returns it to sleep, so only the probe and the divider are switched (device-hardware.md L40).
- **Copied board adapters:** the Hub's eFuse/HMAC/flash adapters are copied, not shared, because `apps/rs` is outside the host workspace and changing the Hub is out of scope. Record a deferred item to extract a shared ESP32-S3 board crate.
- **Switch pads in deep sleep:** pads float while the chip sleeps. The reference wiring needs external pull-downs on the probe and divider switch gates, and the README and checklist must say so.
- **Pins are a reference wiring:** there is no board choice yet (hardware/ is empty until Epic 10). `pins.rs` is the single place to change them.

## Verification

**Commands:**
- `cargo test --workspace --locked` -- all green, including `tests/rs/sensing`
- `cargo clippy --workspace --all-targets --locked -- -D warnings && cargo fmt --all --check` -- clean
- `cargo build -p coldframe-hal -p coldframe-crypto -p coldframe-protocol -p coldframe-setup -p coldframe-uplink -p coldframe-sensing --locked` -- builds without mocks
- `cd apps/rs/node && source ~/export-esp.sh && cargo build --release --locked && cargo build --features dev-mode --locked && cargo clippy --release --locked -- -D warnings && cargo clippy --features dev-mode --locked -- -D warnings && cargo fmt --check` -- all succeed

**Manual checks (if no CLI):**
- Sleep current and wake duration on real hardware (operator, see the checklist).

## Auto Run Result

Status: awaiting-operator

**Summary:** Story 4.1 adds the Node firmware foundation.
- A new host-tested `no_std` crate, `coldframe-sensing`, holds all the decision logic:
  - `ReservedCounter`: a flash-reserved `reading_seq` and boot counter (AD-17), ping-pong over two sectors, safe across power loss;
  - `run_wake`: soil, BME680, battery and charger, with one shared `measured_at` (AD-11) and switches off on every path;
  - the LiPo discharge-curve battery % (always approximate);
  - `env_sample_from`: the BME680 unit conversion;
  - the 15-minute sleep scheduling.
- A thin ESP32-S3 firmware, `apps/rs/node`, has these properties:
  - It adapts esp-hal to the traits.
  - It reuses `coldframe-crypto` for the eFuse or `dev-mode` identity, as in Story 3.2.
  - It reserves the boot ID before identity and arms a 30 s RWDT.
  - It runs at 80 MHz and deep-sleeps on the RTC timer.
- A bench checklist covers the human-only sleep-current measurement.

**Files changed:**
- `packages/rs/hal/src/{sensor.rs,lib.rs,mock.rs}` -- the `RawAdc` and `EnvSensor` traits, `EnvSample` (gas `Option`), `EnvError`, and their mocks.
- `packages/rs/sensing/**` -- new crate: `battery`, `counter`, `env`, `wake`.
- `tests/rs/sensing/**` -- 77 host tests (battery 10, counter 24, env 8, wake 35).
- `Cargo.toml`, `Cargo.lock` -- workspace members.
- `apps/rs/node/**` -- the Node firmware: manifest, toolchain, cargo config, partition table (`cf_ident`, `cf_seq`, `cf_boot`), `main.rs`, `board/*` adapters (copied from the Hub; DW-51), and the README.
- `.github/workflows/ci.yml` -- `coldframe-sensing` in the no-mock build. Node steps in the Firmware job: the release + `dev-mode` refusal, the release and `dev-mode` builds, fmt, and clippy.
- `docs/bench/node-power-checklist.md` -- the bench procedure and Result table.
- `_bmad-output/specs/spec-coldframe/device-hardware.md` -- the Node reference wiring and a "Measured: pending" row.
- `apps/rs/README.md`, `packages/rs/README.md` -- crate rows.
- `_bmad-output/implementation-artifacts/deferred-work.md` -- DW-51 (extract a shared ESP32-S3 board crate that keeps plain NOR writes).
- `_bmad-output/implementation-artifacts/epic-4-context.md` -- the compiled Epic 4 context.

**Review findings:**
- **Pass 1:** 20 findings, triaged as bad_spec. The spec was amended (gas `Option`, the pure `env_sample_from`, boot ID before identity, battery settle and averaging, RWDT, 80 MHz, `seq=none`, the CI refusal check), the code reverted, and the story re-derived.
- **Pass 2:** 19 findings.
  - Patched: 1 medium (high-side switch topology) and 7 low (divider capacitor rule, bench checklist additions and power-cut step, the boot-ID claim wording, the CI comment, line lengths, the DW-51 NOR rule).
  - Deferred: 1 (the NOR flash adapter is not covered by any automated test).
  - Rejected: 4 false (wakeup-cause reset check, RWDT pause in sleep, a truthful counter log, finalization pending). 7 low for the reasons in the triage log: which switch failed, the heater ambient, period drift ×2, the Node image_guard, the pin-table duplication, the residual boot-counter-failure case.

**Follow-up review recommended:** false. Pass 2 patched 0 high and 1 medium.

**Verification:**
- `cargo test --workspace --locked`: 228 passed, 0 failed.
- `cargo clippy --workspace --all-targets --locked -- -D warnings` and `cargo fmt --all --check`: clean.
- The no-mock build including `coldframe-sensing`: OK.
- `apps/rs/node` on the esp toolchain: the release and `dev-mode` builds, both clippy runs with `-D warnings`, and `cargo fmt --check` all OK.
- `cargo build --release --features dev-mode` fails with the `compile_error!`.
- Red → green was observed by the implementer for every new test file.
- The matrix test audit found every edge-case row covered by a passing test.

**Residual risks:**
- Nothing has run on hardware yet. These are compile-checked only: the RWDT across sleep, the esp-radio start at 80 MHz on first boot, the plain-NOR flash writes, ADC1 calibration, and BME680 forced mode through `bosch-bme680 =1.0.4`, which panics on an unknown variant ID (the RWDT then resets the chip).
- If the boot-counter stage fails on a cold boot, the next timer wake reuses the stored boot ID.
- Each period runs about 15 min plus the boot time.

**Operator actions owed:** the frontmatter `operator_actions` (the bench sleep-current and wake measurement, the required on-device Part B including power cuts, the first-boot wake, and recording the results in the checklist and device-hardware.md).
