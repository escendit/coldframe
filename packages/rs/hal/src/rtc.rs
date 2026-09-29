//! Clocks: uptime and wall-clock time.

use core::fmt;

/// Why the clock could not be set.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum RtcError {
    /// The clock hardware failed.
    Hardware,
}

impl fmt::Display for RtcError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::Hardware => "RTC hardware failure",
        })
    }
}

impl core::error::Error for RtcError {}

/// The real-time clock.
pub trait Rtc {
    /// Milliseconds since boot. Never goes back.
    fn uptime_millis(&self) -> u64;

    /// Unix time in milliseconds, UTC, or `None` until it has been set.
    fn unix_time_millis(&self) -> Option<u64>;

    /// Sets the wall clock to `unix_millis`, Unix time in milliseconds, UTC.
    ///
    /// # Errors
    ///
    /// [`RtcError::Hardware`] when the clock cannot be set.
    fn set_unix_time_millis(&mut self, unix_millis: u64) -> Result<(), RtcError>;
}
