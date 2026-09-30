//! The BME680's floating-point output converted to the integer units of a Reading.
//!
//! [`env_sample_from`] is the only float-to-integer conversion of BME680 output: the board adapter
//! passes the driver's values straight in, so the arithmetic is tested on the host.

use coldframe_hal::{EnvError, EnvSample};

/// Milli-units per unit.
const MILLI: f64 = 1_000.0;

/// The largest humidity, 100 % in milli-percent.
const HUMIDITY_MAX_MILLI_PCT: u32 = 100_000;

/// Rounds half away from zero. `value` is finite; `as` saturates outside `i64`.
#[allow(
    clippy::cast_possible_truncation,
    reason = "saturating float to int after rounding"
)]
fn round(value: f64) -> i64 {
    if value >= 0.0 {
        (value + 0.5) as i64
    } else {
        (value - 0.5) as i64
    }
}

/// One BME680 measurement from the driver's values: temperature in °C, relative humidity in %,
/// and gas resistance in Ω (`None` when the driver flagged it invalid).
///
/// Temperature and humidity round half away from zero to milli-units; humidity is clamped to
/// 0–100 000. Gas rounds to whole ohms and saturates at `u32::MAX`; a missing, non-finite or
/// negative gas value is `None`, which costs only the gas Reading.
///
/// # Errors
///
/// [`EnvError::OutOfRange`] when the temperature or humidity is not finite, or the temperature
/// does not fit in milli-degrees as an `i32`.
pub fn env_sample_from(
    temperature_c: f32,
    humidity_pct: f32,
    gas_ohms: Option<f32>,
) -> Result<EnvSample, EnvError> {
    if !temperature_c.is_finite() || !humidity_pct.is_finite() {
        return Err(EnvError::OutOfRange);
    }
    let temperature_milli_c =
        i32::try_from(round(f64::from(temperature_c) * MILLI)).map_err(|_| EnvError::OutOfRange)?;
    let humidity =
        round(f64::from(humidity_pct) * MILLI).clamp(0, i64::from(HUMIDITY_MAX_MILLI_PCT));
    let humidity_milli_pct = u32::try_from(humidity).unwrap_or(HUMIDITY_MAX_MILLI_PCT);
    let gas_ohms = gas_ohms
        .filter(|gas| gas.is_finite() && *gas >= 0.0)
        .map(|gas| u32::try_from(round(f64::from(gas))).unwrap_or(u32::MAX));
    Ok(EnvSample {
        temperature_milli_c,
        humidity_milli_pct,
        gas_ohms,
    })
}
