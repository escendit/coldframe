//! The `coldframe-hal` traits over esp-hal, esp-radio, esp-storage and the ESP32-S3 ROM.
//!
//! This is the only place in the Hub that names chip types. Everything above it takes the traits,
//! so the logic is tested on the host with the mocks of `coldframe-hal`.
//!
//! A dev-mode build compiles no eFuse or HMAC adapter at all, so it cannot touch the fuses.

#[cfg(not(feature = "dev-mode"))]
pub mod efuse;
#[cfg(feature = "dev-mode")]
pub mod flash;
#[cfg(not(feature = "dev-mode"))]
pub mod hmac;
pub mod radio;
pub mod rng;
