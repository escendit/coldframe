//! The true random number generator.

use core::fmt;

/// Why the TRNG produced no bytes.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum TrngError {
    /// No entropy source is running: on the ESP32-S3 the radio is off.
    EntropySourceDisabled,
    /// The generator failed.
    Hardware,
}

impl fmt::Display for TrngError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::EntropySourceDisabled => "TRNG entropy source disabled",
            Self::Hardware => "TRNG hardware failure",
        })
    }
}

impl core::error::Error for TrngError {}

/// A true random number generator, fit for key material.
///
/// An implementation must fail rather than hand out pseudo-random bytes: while its entropy source
/// is off, [`Trng::fill`] returns [`TrngError::EntropySourceDisabled`].
pub trait Trng {
    /// Fills `buffer` with true random bytes.
    ///
    /// # Errors
    ///
    /// [`TrngError::EntropySourceDisabled`] while no entropy source runs, [`TrngError::Hardware`]
    /// when the generator fails. `buffer` holds no usable bytes after an error.
    fn fill(&mut self, buffer: &mut [u8]) -> Result<(), TrngError>;
}
