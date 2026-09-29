//! Hardware abstraction for Coldframe firmware (AD-24).
//!
//! Firmware logic depends only on the traits of this crate, never on esp-hal types, so it can be
//! tested on the host. The firmware implements them over the chip; the `mock` feature adds std-only
//! implementations for host-side tests.
//!
//! - [`radio`]: turning the radio on (it is also the TRNG's entropy source).
//! - [`rng`]: the true random number generator.
//! - [`efuse`]: the key blocks: purpose, read protection, burning.
//! - [`hmac`]: the HMAC peripheral keyed by an eFuse block.
//! - [`flash`]: a flash region with NOR semantics.
//! - [`adc`], [`rtc`], [`gpio`]: analogue input, clocks and digital pins.
//!
//! Every trait has its own small `Copy` error type. None of them carries key material.

#![cfg_attr(not(any(test, feature = "mock")), no_std)]

pub mod adc;
pub mod efuse;
pub mod flash;
pub mod gpio;
pub mod hmac;
#[cfg(feature = "mock")]
pub mod mock;
pub mod radio;
pub mod rng;
pub mod rtc;

pub use adc::{Adc, AdcError};
pub use efuse::{Efuse, EfuseError, KeyBlock, KeyPurpose};
pub use flash::{Flash, FlashError};
pub use gpio::{GpioError, InputPin, OutputPin};
pub use hmac::{HmacError, HmacPeripheral};
pub use radio::{Radio, RadioError};
pub use rng::{Trng, TrngError};
pub use rtc::{Rtc, RtcError};

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
