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
//! - [`ble`]: the BLE setup link (advertise, one connection, opaque payloads).
//! - [`wifi`]: Wi-Fi station scan, join, link state and leave.
//! - [`net`]: the IP uplink: DHCP, SNTP and one HTTPS `POST` at a time.
//! - [`timer`]: waiting for a while.
//! - [`adc`], [`rtc`], [`gpio`]: analogue input, clocks and digital pins.
//!
//! Every trait has its own small `Copy` error type. None of them carries key material, a
//! payload or a password. The BLE, Wi-Fi, network and timer traits are `async` for a
//! single-threaded executor.

#![cfg_attr(not(any(test, feature = "mock")), no_std)]

pub mod adc;
pub mod ble;
pub mod efuse;
pub mod flash;
pub mod gpio;
pub mod hmac;
#[cfg(feature = "mock")]
pub mod mock;
pub mod net;
pub mod radio;
pub mod rng;
pub mod rtc;
pub mod timer;
pub mod wifi;

pub use adc::{Adc, AdcError};
pub use ble::{LinkError, SetupLink};
pub use efuse::{Efuse, EfuseError, KeyBlock, KeyPurpose};
pub use flash::{Flash, FlashError};
pub use gpio::{GpioError, InputPin, OutputPin};
pub use hmac::{HmacError, HmacPeripheral};
pub use net::{HttpRequest, HttpResponse, Net, NetError};
pub use radio::{Radio, RadioError};
pub use rng::{Trng, TrngError};
pub use rtc::{Rtc, RtcError};
pub use timer::Timer;
pub use wifi::{AccessPoint, JoinError, Security, Wifi, WifiError};

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
