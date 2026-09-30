//! One wake: measure every Sensor, report the battery, reserve `reading_seq`, schedule the sleep.
//!
//! Order of a wake, [`run_wake`]:
//! 1. Capture `measured_at` once (AD-11): `Synced` when the wall clock is set, otherwise
//!    `Unsynced` with the boot ID and the RTC uptime.
//! 2. Probe switch on, wait [`PROBE_SETTLE_MS`], average [`SOIL_SAMPLES`] raw conversions, probe
//!    switch off.
//! 3. One forced BME680 measurement (it returns to sleep by itself, so it is not switched).
//! 4. Divider switch on, wait [`DIVIDER_SETTLE_MS`], average [`BATTERY_SAMPLES`] millivolt
//!    conversions, divider switch off.
//! 5. Read the charger status pin.
//! 6. Reserve one `reading_seq` per successful Reading ([`ReservedCounter::reserve`]), so the
//!    raised ceiling is in flash before any value is used. If that fails, no Reading is issued;
//!    the battery and charging status are still reported.
//! 7. Sleep for the rest of the [`WAKE_PERIOD_MS`] period, at least [`MIN_SLEEP_MS`].
//!
//! A switch is turned off on every path out of its measurement, errors included. A failed Sensor
//! costs only its own Readings; an invalid gas value costs only the gas Reading.

use core::ops::Range;

use coldframe_hal::{
    Adc, AdcError, EnvError, EnvSensor, Flash, GpioError, InputPin, OutputPin, RawAdc, Rtc, Timer,
};
use heapless::Vec;

use crate::battery::{BatteryLevel, Divider};
use crate::counter::{CounterError, ReservedCounter};

/// Time between the starts of two wakes.
pub const WAKE_PERIOD_MS: u32 = 900_000;

/// The shortest sleep, when a wake ran for a whole period or longer.
pub const MIN_SLEEP_MS: u32 = 1_000;

/// How long the soil probe is powered before it is read.
pub const PROBE_SETTLE_MS: u32 = 100;

/// Raw conversions averaged into one soil Reading.
pub const SOIL_SAMPLES: u32 = 8;

/// How long the battery divider is connected before it is read.
pub const DIVIDER_SETTLE_MS: u32 = 10;

/// Millivolt conversions averaged into one battery value.
pub const BATTERY_SAMPLES: u32 = 4;

/// The charger status pin is low while charging (TP4056 `CHRG`, open drain with a pull-up).
pub const CHARGER_ACTIVE_LOW: bool = true;

/// Readings per wake: one per Sensor.
pub const READINGS_PER_WAKE: usize = 4;

/// When the Readings of one wake were taken (AD-11).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum MeasuredAt {
    /// The wall clock was set: Unix time in milliseconds, UTC.
    Synced {
        /// Unix time in milliseconds.
        unix_ms: u64,
    },
    /// The wall clock was not set: the Server rebases the time from the boot ID and uptime.
    Unsynced {
        /// The boot counter value of this power-on.
        boot_id: u64,
        /// RTC uptime since power-on, in milliseconds.
        uptime_ms: u64,
    },
}

/// One Sensor value. The slot order is fixed: the Sensor Specification set indexes it.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ReadingValue {
    /// Slot 0: soil moisture, the mean raw ADC count (uncalibrated).
    SoilRaw(u16),
    /// Slot 1: air temperature in milli-degrees Celsius.
    TemperatureMilliC(i32),
    /// Slot 2: relative humidity in milli-percent.
    HumidityMilliPct(u32),
    /// Slot 3: raw gas resistance in ohms.
    GasOhms(u32),
}

impl ReadingValue {
    /// The Sensor slot of this value.
    #[must_use]
    pub const fn slot(&self) -> u8 {
        match self {
            Self::SoilRaw(_) => 0,
            Self::TemperatureMilliC(_) => 1,
            Self::HumidityMilliPct(_) => 2,
            Self::GasOhms(_) => 3,
        }
    }
}

/// One Reading: a Sensor value with its `reading_seq`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Reading {
    /// The Sensor slot, `value.slot()`.
    pub slot: u8,
    /// The per-Device `reading_seq`, never repeated.
    pub seq: u64,
    /// The value.
    pub value: ReadingValue,
}

/// The charger's state.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ChargeStatus {
    /// The charger is charging the cell.
    Charging,
    /// The charger is not charging (no sun, or full).
    NotCharging,
    /// The status pin could not be read.
    Unknown,
}

/// What one wake reports.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct WakeReport {
    /// Shared by every Reading of this wake.
    pub measured_at: MeasuredAt,
    /// The Readings in slot order; a failed Sensor has none.
    pub readings: Vec<Reading, READINGS_PER_WAKE>,
    /// The battery, or `None` when it could not be read.
    pub battery: Option<BatteryLevel>,
    /// The charger status.
    pub charging: ChargeStatus,
}

impl WakeReport {
    /// The `reading_seq` values issued this wake, or `None` when no Reading was issued.
    #[must_use]
    pub fn seq_range(&self) -> Option<Range<u64>> {
        match (self.readings.first(), self.readings.last()) {
            (Some(first), Some(last)) => Some(first.seq..last.seq + 1),
            _ => None,
        }
    }
}

/// What went wrong during a wake, for the log. Carries no value.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct Faults {
    /// The soil ADC failed.
    pub soil: Option<AdcError>,
    /// The environment sensor failed.
    pub env: Option<EnvError>,
    /// The battery ADC failed.
    pub battery: Option<AdcError>,
    /// The charger status pin failed.
    pub charger: Option<GpioError>,
    /// A probe or divider switch failed.
    pub switch: Option<GpioError>,
    /// The `reading_seq` counter failed, so no Reading was issued.
    pub counter: Option<CounterError>,
}

impl Faults {
    /// Whether nothing went wrong.
    #[must_use]
    pub fn is_empty(&self) -> bool {
        *self == Self::default()
    }
}

/// The result of one wake.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct WakeOutcome {
    /// What to report.
    pub report: WakeReport,
    /// What failed.
    pub faults: Faults,
    /// How long to deep-sleep before the next wake.
    pub sleep_ms: u32,
}

/// Why the Node is running.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum WakeCause {
    /// Power-on or any reset other than the deep-sleep timer: a new boot ID.
    ColdBoot,
    /// The deep-sleep timer: the same boot ID as before the sleep.
    Timer,
}

/// The boot ID: a cold boot reserves one value of the boot counter and takes the new ceiling; a
/// timer wake reads the stored ceiling.
///
/// A timer wake that finds the counter at 0 (no cold boot ever completed its reservation)
/// reserves like a cold boot, so a Reading never carries boot ID 0.
///
/// # Errors
///
/// See [`CounterError`].
pub fn boot_id<F: Flash>(
    counter: &mut ReservedCounter<F>,
    cause: WakeCause,
) -> Result<u64, CounterError> {
    match cause {
        WakeCause::ColdBoot => counter.reserve(1).map(|range| range.end),
        WakeCause::Timer => match counter.current()? {
            0 => counter.reserve(1).map(|range| range.end),
            boot => Ok(boot),
        },
    }
}

/// The sleep after a wake that ran `elapsed_ms`: the rest of the period, at least
/// [`MIN_SLEEP_MS`].
#[must_use]
pub fn sleep_ms(elapsed_ms: u64) -> u32 {
    let rest = u64::from(WAKE_PERIOD_MS).saturating_sub(elapsed_ms);
    u32::try_from(rest.max(u64::from(MIN_SLEEP_MS))).unwrap_or(WAKE_PERIOD_MS)
}

/// Turns a switch off, recording (the first) failure.
fn switch_off<P: OutputPin>(switch: &mut P, faults: &mut Faults) {
    if let Err(error) = switch.set_low() {
        faults.switch.get_or_insert(error);
    }
}

/// The rounded mean of `samples` conversions from `read`; the first failure ends it.
fn mean_of(samples: u32, mut read: impl FnMut() -> Result<u16, AdcError>) -> Result<u16, AdcError> {
    let mut sum = 0u32;
    for _ in 0..samples {
        sum += u32::from(read()?);
    }
    let mean = (sum + samples / 2) / samples;
    Ok(u16::try_from(mean).unwrap_or(u16::MAX))
}

/// Powers the probe, lets it settle, samples it, and powers it off on every path.
async fn measure_soil<P: OutputPin, S: RawAdc, T: Timer>(
    switch: &mut P,
    adc: &mut S,
    timer: &mut T,
    faults: &mut Faults,
) -> Option<u16> {
    if let Err(error) = switch.set_high() {
        faults.switch.get_or_insert(error);
        switch_off(switch, faults);
        return None;
    }
    timer.sleep_ms(PROBE_SETTLE_MS).await;
    let mean = mean_of(SOIL_SAMPLES, || adc.read_raw());
    switch_off(switch, faults);
    mean.map_err(|error| faults.soil = Some(error)).ok()
}

/// Connects the divider, lets it settle, averages the battery, and disconnects it on every path.
async fn measure_battery<D: OutputPin, B: Adc, T: Timer>(
    switch: &mut D,
    adc: &mut B,
    timer: &mut T,
    divider: Divider,
    faults: &mut Faults,
) -> Option<BatteryLevel> {
    if let Err(error) = switch.set_high() {
        faults.switch.get_or_insert(error);
        switch_off(switch, faults);
        return None;
    }
    timer.sleep_ms(DIVIDER_SETTLE_MS).await;
    let millivolts = mean_of(BATTERY_SAMPLES, || adc.read_millivolts());
    switch_off(switch, faults);
    millivolts
        .map(|millivolts| BatteryLevel::from_cell_millivolts(divider.cell_millivolts(millivolts)))
        .map_err(|error| faults.battery = Some(error))
        .ok()
}

/// The charger status from its pin level.
fn charge_status<C: InputPin>(pin: &mut C, faults: &mut Faults) -> ChargeStatus {
    match pin.is_high() {
        Ok(high) if high == CHARGER_ACTIVE_LOW => ChargeStatus::NotCharging,
        Ok(_) => ChargeStatus::Charging,
        Err(error) => {
            faults.charger = Some(error);
            ChargeStatus::Unknown
        }
    }
}

/// The Node's measurement hardware for one wake.
pub struct Sensors<'a, P, D, S, E, B, C> {
    /// Powers the soil probe while high.
    pub probe_switch: &'a mut P,
    /// Connects the battery divider while high.
    pub divider_switch: &'a mut D,
    /// The soil probe's raw ADC input.
    pub soil: &'a mut S,
    /// The BME680.
    pub env: &'a mut E,
    /// The battery divider's calibrated ADC input.
    pub battery: &'a mut B,
    /// The charger status pin.
    pub charger: &'a mut C,
    /// The battery divider's resistors.
    pub divider: Divider,
}

/// Runs one wake; see the module documentation for the order.
///
/// `wake_start_ms` is the RTC uptime when this wake began; the sleep is measured from it.
pub async fn run_wake<P, D, S, E, B, C, R, T, F>(
    sensors: Sensors<'_, P, D, S, E, B, C>,
    rtc: &R,
    timer: &mut T,
    seq: &mut ReservedCounter<F>,
    boot_id: u64,
    wake_start_ms: u64,
) -> WakeOutcome
where
    P: OutputPin,
    D: OutputPin,
    S: RawAdc,
    E: EnvSensor,
    B: Adc,
    C: InputPin,
    R: Rtc,
    T: Timer,
    F: Flash,
{
    let mut faults = Faults::default();
    let measured_at = match rtc.unix_time_millis() {
        Some(unix_ms) => MeasuredAt::Synced { unix_ms },
        None => MeasuredAt::Unsynced {
            boot_id,
            uptime_ms: rtc.uptime_millis(),
        },
    };

    let soil = measure_soil(sensors.probe_switch, sensors.soil, timer, &mut faults).await;
    let env = sensors
        .env
        .measure_forced()
        .map_err(|error| faults.env = Some(error))
        .ok();
    let battery = measure_battery(
        sensors.divider_switch,
        sensors.battery,
        timer,
        sensors.divider,
        &mut faults,
    )
    .await;
    let charging = charge_status(sensors.charger, &mut faults);

    // Slot order: soil, temperature, humidity, gas. At most four values fit.
    let mut values: Vec<ReadingValue, READINGS_PER_WAKE> = Vec::new();
    if let Some(raw) = soil {
        let _ = values.push(ReadingValue::SoilRaw(raw));
    }
    if let Some(sample) = env {
        let _ = values.push(ReadingValue::TemperatureMilliC(sample.temperature_milli_c));
        let _ = values.push(ReadingValue::HumidityMilliPct(sample.humidity_milli_pct));
        if let Some(gas) = sample.gas_ohms {
            let _ = values.push(ReadingValue::GasOhms(gas));
        }
    }

    let mut readings = Vec::new();
    // `usize` to `u64` never truncates on the targets this runs on.
    match seq.reserve(values.len() as u64) {
        Ok(range) => {
            for (seq, value) in range.zip(values) {
                let _ = readings.push(Reading {
                    slot: value.slot(),
                    seq,
                    value,
                });
            }
        }
        Err(error) => faults.counter = Some(error),
    }

    let elapsed = rtc.uptime_millis().saturating_sub(wake_start_ms);
    WakeOutcome {
        report: WakeReport {
            measured_at,
            readings,
            battery,
            charging,
        },
        faults,
        sleep_ms: sleep_ms(elapsed),
    }
}
