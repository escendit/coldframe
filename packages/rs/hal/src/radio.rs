//! The radio.
//!
//! Story 3.2 needs only to turn it on: on the ESP32-S3 the running radio is the entropy source of
//! the TRNG, so the root key is drawn only after [`Radio::enable`]. BLE and Wi-Fi have their own
//! traits, [`crate::SetupLink`] (Story 3.4) and [`crate::Wifi`]; this one stays the power switch.

use core::fmt;

/// Why the radio could not be turned on.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum RadioError {
    /// The radio driver failed to start.
    StartFailed,
}

impl fmt::Display for RadioError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::StartFailed => "radio failed to start",
        })
    }
}

impl core::error::Error for RadioError {}

/// The Device's radio.
pub trait Radio {
    /// Turns the radio on. Calling it while the radio is on does nothing.
    ///
    /// # Errors
    ///
    /// [`RadioError::StartFailed`] when the driver does not start.
    fn enable(&mut self) -> Result<(), RadioError>;

    /// Whether the radio is on.
    fn is_enabled(&self) -> bool;
}
