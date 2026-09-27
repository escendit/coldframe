//! Hardware abstraction for Coldframe firmware.
//!
//! Firmware logic depends only on the traits of this crate (radio, eFuse, HMAC, flash, ADC, RTC,
//! GPIO), so it can be tested on the host with mock implementations. The traits and their mocks
//! arrive with the firmware epics. Until then the crate holds only what proves that the workspace
//! builds, tests and lints.

#![cfg_attr(not(test), no_std)]

/// Name of this crate.
pub const NAME: &str = env!("CARGO_PKG_NAME");

/// Version of this crate, as written in its Cargo manifest.
pub const VERSION: &str = env!("CARGO_PKG_VERSION");

#[cfg(test)]
mod tests {
    use super::NAME;

    #[test]
    fn name_is_the_package_name() {
        assert_eq!(NAME, "coldframe-hal");
    }
}
