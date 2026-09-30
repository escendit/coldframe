//! The wake cycle: Readings, switch order on every path, measured_at, the boot ID and the sleep.

mod common;

use coldframe_hal::mock::{
    MockAdc, MockEnvSensor, MockFlash, MockPin, MockRawAdc, MockRtc, MockTimer,
};
use coldframe_hal::{AdcError, EnvError, FlashError, GpioError, Rtc};
use coldframe_sensing::counter::{BOOT_MAGIC, SECTOR_SIZE, SEQ_MAGIC, SLOT_SIZE, encode_record};
use coldframe_sensing::wake::{
    BATTERY_SAMPLES, DIVIDER_SETTLE_MS, MIN_SLEEP_MS, PROBE_SETTLE_MS, SOIL_SAMPLES, WAKE_PERIOD_MS,
};
use coldframe_sensing::{
    BatteryLevel, ChargeStatus, CounterError, Divider, MeasuredAt, Reading, ReadingValue,
    ReservedCounter, Sensors, WakeCause, WakeOutcome, boot_id, run_wake, sleep_ms,
};
use common::{Battery, Env, Event, Log, SAMPLE, Soil, Switch, Wait, block_on, erased, log};

const BOOT_ID: u64 = 7;
const SOIL_RAW: u16 = 2_345;
/// Half of 3870 mV through an equal divider: 60 %.
const BATTERY_ADC_MV: u16 = 1_935;
const EQUAL: Divider = Divider {
    top_ohms: 100_000,
    bottom_ohms: 100_000,
};

/// A seq partition whose stored ceiling is 40.
fn ceiling_40() -> MockFlash {
    let mut flash = erased();
    flash.contents_mut()[..SLOT_SIZE].copy_from_slice(&encode_record(SEQ_MAGIC, 40));
    flash
}

/// A Node on the bench: healthy sensors, charger charging, clock unset, seq ceiling 40.
struct Rig {
    log: Log,
    probe: Switch,
    divider: Switch,
    soil: Soil,
    env: Env,
    battery: Battery,
    charger: MockPin,
    rtc: MockRtc,
    timer: Wait,
    seq: ReservedCounter<MockFlash>,
    wake_start_ms: u64,
}

impl Rig {
    fn new() -> Self {
        let log = log();
        Self {
            probe: Switch::new("probe", &log),
            divider: Switch::new("divider", &log),
            soil: Soil {
                adc: MockRawAdc::new(SOIL_RAW),
                log: log.clone(),
            },
            env: Env {
                sensor: MockEnvSensor::new(SAMPLE),
                log: log.clone(),
            },
            battery: Battery {
                adc: MockAdc::new(BATTERY_ADC_MV),
                queued: std::collections::VecDeque::new(),
                log: log.clone(),
            },
            // Low: charging (the status pin is active low).
            charger: MockPin::new(),
            rtc: MockRtc::new(),
            timer: Wait {
                timer: MockTimer::new(),
                log: log.clone(),
            },
            seq: ReservedCounter::new(ceiling_40(), SEQ_MAGIC),
            wake_start_ms: 0,
            log,
        }
    }

    fn run(&mut self) -> WakeOutcome {
        block_on(run_wake(
            Sensors {
                probe_switch: &mut self.probe,
                divider_switch: &mut self.divider,
                soil: &mut self.soil,
                env: &mut self.env,
                battery: &mut self.battery,
                charger: &mut self.charger,
                divider: EQUAL,
            },
            &self.rtc,
            &mut self.timer,
            &mut self.seq,
            BOOT_ID,
            self.wake_start_ms,
        ))
    }

    fn events(&self) -> Vec<Event> {
        self.log.borrow().clone()
    }

    fn stored_ceiling(self) -> Result<u64, CounterError> {
        ReservedCounter::new(self.seq.into_inner(), SEQ_MAGIC).current()
    }
}

fn soil_samples() -> Vec<Event> {
    vec![Event::Soil; SOIL_SAMPLES as usize]
}

/// Divider on, settle, the battery conversions, divider off.
fn battery_sequence() -> Vec<Event> {
    let mut events = vec![Event::High("divider"), Event::Wait(DIVIDER_SETTLE_MS)];
    events.extend(vec![Event::Battery; BATTERY_SAMPLES as usize]);
    events.push(Event::Low("divider"));
    events
}

/// Probe on, settle, samples, probe off, BME680, then the battery sequence.
fn full_sequence() -> Vec<Event> {
    let mut events = vec![Event::High("probe"), Event::Wait(PROBE_SETTLE_MS)];
    events.extend(soil_samples());
    events.extend([Event::Low("probe"), Event::Env]);
    events.extend(battery_sequence());
    events
}

fn assert_switches_off(rig: &Rig) {
    assert!(!rig.probe.high, "probe switch left on");
    assert!(!rig.divider.high, "divider switch left on");
}

fn reading(slot: u8, seq: u64, value: ReadingValue) -> Reading {
    Reading { slot, seq, value }
}

#[test]
fn a_happy_wake_reports_four_readings_with_consecutive_seqs() {
    let mut rig = Rig::new();
    let outcome = rig.run();
    let report = &outcome.report;
    assert_eq!(
        report.readings.as_slice(),
        &[
            reading(0, 40, ReadingValue::SoilRaw(SOIL_RAW)),
            reading(1, 41, ReadingValue::TemperatureMilliC(21_500)),
            reading(2, 42, ReadingValue::HumidityMilliPct(55_250)),
            reading(3, 43, ReadingValue::GasOhms(120_000)),
        ]
    );
    for reading in &report.readings {
        assert_eq!(reading.slot, reading.value.slot());
    }
    assert_eq!(report.seq_range(), Some(40..44));
    assert_eq!(
        report.battery,
        Some(BatteryLevel {
            cell_millivolts: 3_870,
            percent: 60,
            approximate: true,
        })
    );
    assert_eq!(report.charging, ChargeStatus::Charging);
    assert!(outcome.faults.is_empty());
    assert_eq!(rig.events(), full_sequence());
    assert_eq!(
        rig.timer.timer.sleeps(),
        &[PROBE_SETTLE_MS, DIVIDER_SETTLE_MS]
    );
    assert_eq!(rig.env.sensor.call_count(), 1);
    assert_switches_off(&rig);
    assert_eq!(rig.stored_ceiling(), Ok(44));
}

#[test]
fn the_ceiling_is_persisted_before_the_seqs_are_used() {
    let mut rig = Rig::new();
    let outcome = rig.run();
    let flash = rig.seq.into_inner();
    // The flash already holds 44 when the report leaves run_wake.
    let mut copy = MockFlash::new(0x2000);
    copy.contents_mut().copy_from_slice(flash.contents());
    assert_eq!(ReservedCounter::new(copy, SEQ_MAGIC).current(), Ok(44));
    assert_eq!(outcome.report.seq_range(), Some(40..44));
}

#[test]
fn a_fresh_device_starts_at_seq_zero() {
    let mut rig = Rig::new();
    rig.seq = ReservedCounter::new(erased(), SEQ_MAGIC);
    let outcome = rig.run();
    assert_eq!(outcome.report.seq_range(), Some(0..4));
}

#[test]
fn consecutive_wakes_never_repeat_a_seq() {
    let mut rig = Rig::new();
    let first = rig.run();
    // A deep-sleep wake is a reboot: a new counter over the same flash.
    let flash = std::mem::replace(&mut rig.seq, ReservedCounter::new(erased(), SEQ_MAGIC));
    rig.seq = ReservedCounter::new(flash.into_inner(), SEQ_MAGIC);
    let second = rig.run();
    assert_eq!(first.report.seq_range(), Some(40..44));
    assert_eq!(second.report.seq_range(), Some(44..48));
}

#[test]
fn the_soil_reading_is_the_rounded_mean_of_eight_samples() {
    let mut rig = Rig::new();
    for raw in 100..108 {
        rig.soil.adc.push(Ok(raw));
    }
    let outcome = rig.run();
    // (100 + … + 107) / 8 = 103.5, rounded to 104.
    assert_eq!(outcome.report.readings[0].value, ReadingValue::SoilRaw(104));
    assert_eq!(rig.soil.adc.call_count(), SOIL_SAMPLES as usize);
}

#[test]
fn a_failed_bme680_costs_only_its_three_readings() {
    let mut rig = Rig::new();
    rig.env.sensor.set(Err(EnvError::Bus));
    let outcome = rig.run();
    assert_eq!(
        outcome.report.readings.as_slice(),
        &[reading(0, 40, ReadingValue::SoilRaw(SOIL_RAW))]
    );
    assert_eq!(outcome.faults.env, Some(EnvError::Bus));
    assert!(outcome.report.battery.is_some());
    assert_eq!(rig.events(), full_sequence());
    assert_switches_off(&rig);
    // Exactly one seq was reserved.
    assert_eq!(rig.stored_ceiling(), Ok(41));
}

#[test]
fn a_failed_soil_adc_costs_only_the_soil_reading_and_switches_the_probe_off() {
    let mut rig = Rig::new();
    rig.soil.adc.push(Ok(1));
    rig.soil.adc.push(Ok(2));
    rig.soil.adc.push(Err(AdcError::Hardware));
    let outcome = rig.run();
    assert_eq!(
        outcome.report.readings.as_slice(),
        &[
            reading(1, 40, ReadingValue::TemperatureMilliC(21_500)),
            reading(2, 41, ReadingValue::HumidityMilliPct(55_250)),
            reading(3, 42, ReadingValue::GasOhms(120_000)),
        ]
    );
    assert_eq!(outcome.faults.soil, Some(AdcError::Hardware));
    let events = rig.events();
    assert_eq!(
        &events[..6],
        &[
            Event::High("probe"),
            Event::Wait(PROBE_SETTLE_MS),
            Event::Soil,
            Event::Soil,
            Event::Soil,
            Event::Low("probe"),
        ]
    );
    assert_switches_off(&rig);
    assert_eq!(rig.stored_ceiling(), Ok(43));
}

#[test]
fn a_failed_battery_adc_reports_no_battery_and_switches_the_divider_off() {
    let mut rig = Rig::new();
    rig.battery.adc.reading = Err(AdcError::Hardware);
    let outcome = rig.run();
    assert_eq!(outcome.report.battery, None);
    assert_eq!(outcome.faults.battery, Some(AdcError::Hardware));
    assert_eq!(outcome.report.readings.len(), 4);
    assert_eq!(outcome.report.charging, ChargeStatus::Charging);
    // The first failed conversion ends the battery read; the divider still goes off.
    let events = rig.events();
    assert_eq!(
        &events[events.len() - 4..],
        &[
            Event::High("divider"),
            Event::Wait(DIVIDER_SETTLE_MS),
            Event::Battery,
            Event::Low("divider"),
        ]
    );
    assert_switches_off(&rig);
}

#[test]
fn a_battery_conversion_failing_after_the_first_still_reports_no_battery() {
    let mut rig = Rig::new();
    rig.battery
        .queued
        .extend([Ok(1_935), Ok(1_935), Err(AdcError::Hardware)]);
    let outcome = rig.run();
    assert_eq!(outcome.report.battery, None);
    assert_eq!(outcome.faults.battery, Some(AdcError::Hardware));
    assert_eq!(rig.events().last(), Some(&Event::Low("divider")));
    assert_switches_off(&rig);
}

#[test]
fn the_battery_is_the_mean_of_four_conversions_read_after_the_settle_delay() {
    let mut rig = Rig::new();
    // Mean 1 935.5 mV, rounded to 1 936, doubled by the equal divider: 3 872 mV.
    rig.battery
        .queued
        .extend([Ok(1_930), Ok(1_934), Ok(1_937), Ok(1_941)]);
    let outcome = rig.run();
    assert_eq!(
        outcome.report.battery.map(|level| level.cell_millivolts),
        Some(3_872)
    );
    assert_eq!(BATTERY_SAMPLES, 4);
    assert_eq!(DIVIDER_SETTLE_MS, 10);
    assert_eq!(rig.events(), full_sequence());
}

#[test]
fn an_invalid_gas_value_costs_only_the_gas_reading() {
    let mut rig = Rig::new();
    rig.env.sensor.set(Ok(coldframe_hal::EnvSample {
        gas_ohms: None,
        ..SAMPLE
    }));
    let outcome = rig.run();
    assert_eq!(
        outcome.report.readings.as_slice(),
        &[
            reading(0, 40, ReadingValue::SoilRaw(SOIL_RAW)),
            reading(1, 41, ReadingValue::TemperatureMilliC(21_500)),
            reading(2, 42, ReadingValue::HumidityMilliPct(55_250)),
        ]
    );
    assert!(outcome.faults.is_empty());
    assert_eq!(rig.stored_ceiling(), Ok(43));
}

#[test]
fn a_wake_without_readings_has_no_seq_range() {
    let mut rig = Rig::new();
    let mut flash = ceiling_40();
    flash.ignore_writes(true);
    rig.seq = ReservedCounter::new(flash, SEQ_MAGIC);
    assert_eq!(rig.run().report.seq_range(), None);
}

#[test]
fn a_high_charger_pin_is_not_charging() {
    let mut rig = Rig::new();
    rig.charger.high = true;
    assert_eq!(rig.run().report.charging, ChargeStatus::NotCharging);
}

#[test]
fn a_failed_charger_pin_is_unknown() {
    let mut rig = Rig::new();
    rig.charger.failure = Some(GpioError::Hardware);
    let outcome = rig.run();
    assert_eq!(outcome.report.charging, ChargeStatus::Unknown);
    assert_eq!(outcome.faults.charger, Some(GpioError::Hardware));
    assert_eq!(outcome.report.readings.len(), 4);
    assert!(outcome.report.battery.is_some());
}

#[test]
fn a_failed_counter_write_issues_no_readings_but_still_reports_the_battery() {
    let mut rig = Rig::new();
    let mut flash = ceiling_40();
    flash.ignore_writes(true);
    rig.seq = ReservedCounter::new(flash, SEQ_MAGIC);
    let outcome = rig.run();
    assert!(outcome.report.readings.is_empty());
    assert_eq!(outcome.faults.counter, Some(CounterError::NotVerified));
    assert!(outcome.report.battery.is_some());
    assert_eq!(outcome.report.charging, ChargeStatus::Charging);
    assert_switches_off(&rig);
    assert_eq!(outcome.sleep_ms, WAKE_PERIOD_MS);
}

#[test]
fn a_flash_error_issues_no_readings() {
    let mut rig = Rig::new();
    let mut flash = ceiling_40();
    flash.fail_with(Some(FlashError::Storage));
    rig.seq = ReservedCounter::new(flash, SEQ_MAGIC);
    let outcome = rig.run();
    assert!(outcome.report.readings.is_empty());
    assert_eq!(
        outcome.faults.counter,
        Some(CounterError::Flash(FlashError::Storage))
    );
    assert!(outcome.report.battery.is_some());
}

#[test]
fn a_corrupt_counter_issues_no_readings_and_never_restarts_at_zero() {
    let mut rig = Rig::new();
    let mut flash = erased();
    flash.contents_mut()[..SLOT_SIZE].fill(0x00);
    rig.seq = ReservedCounter::new(flash, SEQ_MAGIC);
    let outcome = rig.run();
    assert!(outcome.report.readings.is_empty());
    assert_eq!(outcome.faults.counter, Some(CounterError::Corrupt));
    assert!(outcome.report.battery.is_some());
    assert_eq!(rig.seq.into_inner().write_count(), 0);
}

#[test]
fn a_probe_switch_that_will_not_turn_on_skips_the_soil_and_still_turns_it_off() {
    let mut rig = Rig::new();
    rig.probe.fail_high = true;
    let outcome = rig.run();
    assert_eq!(outcome.faults.switch, Some(GpioError::Hardware));
    assert!(
        outcome
            .report
            .readings
            .iter()
            .all(|reading| reading.slot != 0)
    );
    assert_eq!(outcome.report.readings.len(), 3);
    let mut expected = vec![Event::Low("probe"), Event::Env];
    expected.extend(battery_sequence());
    assert_eq!(rig.events(), expected);
}

#[test]
fn a_probe_switch_that_will_not_turn_off_is_a_fault_but_keeps_the_reading() {
    let mut rig = Rig::new();
    rig.probe.fail_low = true;
    let outcome = rig.run();
    assert_eq!(outcome.faults.switch, Some(GpioError::Hardware));
    assert_eq!(outcome.report.readings.len(), 4);
    // The divider is still switched on and off.
    assert!(!rig.divider.high);
}

#[test]
fn a_divider_switch_that_will_not_turn_on_skips_the_battery_and_still_turns_it_off() {
    let mut rig = Rig::new();
    rig.divider.fail_high = true;
    let outcome = rig.run();
    assert_eq!(outcome.report.battery, None);
    assert_eq!(outcome.faults.switch, Some(GpioError::Hardware));
    let events = rig.events();
    assert_eq!(events.last(), Some(&Event::Low("divider")));
    assert!(!events.contains(&Event::Battery));
    assert_switches_off(&rig);
}

#[test]
fn without_a_wall_clock_measured_at_is_unsynced_with_boot_id_and_uptime() {
    let mut rig = Rig::new();
    rig.rtc.advance(123_456);
    rig.wake_start_ms = 123_000;
    let outcome = rig.run();
    assert_eq!(
        outcome.report.measured_at,
        MeasuredAt::Unsynced {
            boot_id: BOOT_ID,
            uptime_ms: 123_456,
        }
    );
}

#[test]
fn with_a_wall_clock_measured_at_is_synced() {
    let mut rig = Rig::new();
    rig.rtc.advance(1_000);
    rig.rtc.set_unix_time_millis(1_790_000_000_000).unwrap();
    let outcome = rig.run();
    assert_eq!(
        outcome.report.measured_at,
        MeasuredAt::Synced {
            unix_ms: 1_790_000_000_000,
        }
    );
}

#[test]
fn the_sleep_is_the_rest_of_the_period() {
    let mut rig = Rig::new();
    rig.rtc.advance(5_300);
    rig.wake_start_ms = 5_000;
    assert_eq!(rig.run().sleep_ms, WAKE_PERIOD_MS - 300);
}

#[test]
fn a_long_wake_sleeps_the_minimum() {
    let mut rig = Rig::new();
    rig.rtc.advance(2_000_000);
    rig.wake_start_ms = 1_000;
    assert_eq!(rig.run().sleep_ms, MIN_SLEEP_MS);
}

#[test]
fn sleep_arithmetic() {
    assert_eq!(sleep_ms(0), 900_000);
    assert_eq!(sleep_ms(1_234), 898_766);
    assert_eq!(sleep_ms(898_999), 1_001);
    assert_eq!(sleep_ms(899_000), 1_000);
    assert_eq!(sleep_ms(899_500), MIN_SLEEP_MS);
    assert_eq!(sleep_ms(900_000), MIN_SLEEP_MS);
    assert_eq!(sleep_ms(u64::MAX), MIN_SLEEP_MS);
}

#[test]
fn a_cold_boot_takes_a_new_boot_id_and_a_timer_wake_keeps_it() {
    let mut boots = ReservedCounter::new(erased(), BOOT_MAGIC);
    assert_eq!(boot_id(&mut boots, WakeCause::ColdBoot), Ok(1));
    let mut boots = ReservedCounter::new(boots.into_inner(), BOOT_MAGIC);
    assert_eq!(boot_id(&mut boots, WakeCause::Timer), Ok(1));
    let mut boots = ReservedCounter::new(boots.into_inner(), BOOT_MAGIC);
    assert_eq!(boot_id(&mut boots, WakeCause::Timer), Ok(1));
    let mut boots = ReservedCounter::new(boots.into_inner(), BOOT_MAGIC);
    assert_eq!(boot_id(&mut boots, WakeCause::ColdBoot), Ok(2));
    let mut boots = ReservedCounter::new(boots.into_inner(), BOOT_MAGIC);
    assert_eq!(boot_id(&mut boots, WakeCause::Timer), Ok(2));
}

#[test]
fn a_timer_wake_with_an_erased_boot_counter_reserves_and_never_returns_zero() {
    let mut boots = ReservedCounter::new(erased(), BOOT_MAGIC);
    assert_eq!(boot_id(&mut boots, WakeCause::Timer), Ok(1));
    let mut boots = ReservedCounter::new(boots.into_inner(), BOOT_MAGIC);
    // Now a boot ID exists, so a timer wake reuses it.
    assert_eq!(boot_id(&mut boots, WakeCause::Timer), Ok(1));
}

#[test]
fn a_timer_wake_with_an_erased_boot_counter_that_cannot_be_written_is_an_error() {
    let mut flash = erased();
    flash.ignore_writes(true);
    let mut boots = ReservedCounter::new(flash, BOOT_MAGIC);
    assert_eq!(
        boot_id(&mut boots, WakeCause::Timer),
        Err(CounterError::NotVerified)
    );
}

#[test]
fn a_corrupt_boot_counter_is_an_error_on_either_path() {
    let mut flash = erased();
    flash.contents_mut()[SECTOR_SIZE as usize] = 0x00;
    let mut boots = ReservedCounter::new(flash, BOOT_MAGIC);
    assert_eq!(
        boot_id(&mut boots, WakeCause::Timer),
        Err(CounterError::Corrupt)
    );
    assert_eq!(
        boot_id(&mut boots, WakeCause::ColdBoot),
        Err(CounterError::Corrupt)
    );
}
