//! Analogue input.

use core::fmt;

/// Why an ADC reading failed.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum AdcError {
    /// The converter failed.
    Hardware,
}

impl fmt::Display for AdcError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::Hardware => "ADC hardware failure",
        })
    }
}

impl core::error::Error for AdcError {}

/// One calibrated analogue input.
pub trait Adc {
    /// Reads the input in millivolts.
    ///
    /// # Errors
    ///
    /// [`AdcError::Hardware`] when the conversion fails.
    fn read_millivolts(&mut self) -> Result<u16, AdcError>;
}
