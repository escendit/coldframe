//! The `coldframe-hal` traits over esp-hal, esp-radio, trouble-host, esp-storage and the
//! ESP32-S3 ROM.
//!
//! This is the only place in the Hub that names chip types. Everything above it takes the traits,
//! so the logic is tested on the host with the mocks of `coldframe-hal`.
//!
//! A dev-mode build compiles no eFuse or HMAC adapter at all, so it cannot touch the fuses.

pub mod ble;
#[cfg(not(feature = "dev-mode"))]
pub mod efuse;
pub mod flash;
#[cfg(not(feature = "dev-mode"))]
pub mod hmac;
pub mod radio;
pub mod rng;
pub mod wifi;
