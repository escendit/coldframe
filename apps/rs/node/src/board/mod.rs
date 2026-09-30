//! The `coldframe-hal` traits over esp-hal, esp-radio, esp-storage, bosch-bme680 and the ESP32-S3
//! ROM.
//!
//! This is the only place in the Node that names chip types. Everything above it takes the traits,
//! so the logic (`coldframe-sensing`, `coldframe-crypto`) is tested on the host with the mocks of
//! `coldframe-hal`.
//!
//! The eFuse, HMAC, flash, TRNG and timer adapters are copies of the Hub's (a shared ESP32-S3
//! board crate is a deferred item). A dev-mode build compiles no eFuse or HMAC adapter at all, so
//! it cannot touch the fuses.

#[cfg(not(feature = "dev-mode"))]
pub mod efuse;
pub mod flash;
#[cfg(not(feature = "dev-mode"))]
pub mod hmac;
pub mod pins;
pub mod radio;
pub mod rng;
pub mod rtc;
pub mod sensors;
pub mod sleep;
pub mod timer;
