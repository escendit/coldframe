//! Sensors: a raw ADC count and a BME680-style environment sensor in forced mode.

use core::fmt;

use crate::adc::AdcError;

/// An analogue input read as the raw converter count, uncalibrated.
///
/// The soil probe is read this way: its value is calibrated per probe later (two-point
/// Calibration), so millivolts would add nothing. [`crate::Adc`] stays the calibrated input.
pub trait RawAdc {
    /// Reads one raw conversion.
    ///
    /// # Errors
    ///
    /// [`AdcError::Hardware`] when the conversion fails.
    fn read_raw(&mut self) -> Result<u16, AdcError>;
}

/// One forced-mode measurement of an environment sensor, in integer units.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct EnvSample {
    /// Air temperature in milli-degrees Celsius.
    pub temperature_milli_c: i32,
    /// Relative humidity in milli-percent (0–100 000).
    pub humidity_milli_pct: u32,
    /// Raw gas resistance in ohms, or `None` when the sensor flagged the gas conversion invalid
    /// or the heater unstable. That costs only the gas Reading.
    pub gas_ohms: Option<u32>,
}

/// Why an environment measurement failed.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum EnvError {
    /// The bus to the sensor failed, or the sensor did not answer.
    Bus,
    /// The sensor did not finish the measurement in time.
    Timeout,
    /// The sensor returned a non-finite or unrepresentable temperature or humidity.
    OutOfRange,
}

impl fmt::Display for EnvError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::Bus => "environment sensor bus failure",
            Self::Timeout => "environment sensor measurement timed out",
            Self::OutOfRange => "environment sensor value out of range",
        })
    }
}

impl core::error::Error for EnvError {}

/// A temperature, humidity and gas sensor measured in forced mode (BME680).
///
/// One call runs one complete measurement, heater included, and leaves the sensor asleep.
pub trait EnvSensor {
    /// Runs one forced-mode measurement.
    ///
    /// # Errors
    ///
    /// See [`EnvError`].
    fn measure_forced(&mut self) -> Result<EnvSample, EnvError>;
}
