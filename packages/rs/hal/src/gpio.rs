//! Digital pins.

use core::fmt;

/// Why a pin operation failed.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum GpioError {
    /// The pin hardware failed.
    Hardware,
}

impl fmt::Display for GpioError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::Hardware => "GPIO hardware failure",
        })
    }
}

impl core::error::Error for GpioError {}

/// A digital output.
pub trait OutputPin {
    /// Drives the pin high.
    ///
    /// # Errors
    ///
    /// [`GpioError::Hardware`] when the pin cannot be driven.
    fn set_high(&mut self) -> Result<(), GpioError>;

    /// Drives the pin low.
    ///
    /// # Errors
    ///
    /// [`GpioError::Hardware`] when the pin cannot be driven.
    fn set_low(&mut self) -> Result<(), GpioError>;
}

/// A digital input.
pub trait InputPin {
    /// Whether the pin reads high.
    ///
    /// # Errors
    ///
    /// [`GpioError::Hardware`] when the pin cannot be read.
    fn is_high(&mut self) -> Result<bool, GpioError>;
}
