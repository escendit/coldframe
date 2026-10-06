//! The `coldframe-hal` traits over esp-hal, esp-radio, esp-storage, bosch-bme680 and the ESP32-S3
//! ROM.
//!
//! This is the only place in the Node that names chip types. Everything above it takes the traits,
//! so the logic (`coldframe-sensing`, `coldframe-transport`, `coldframe-crypto`) is tested on the
//! host with the mocks of `coldframe-hal`.
//!
//! The eFuse, HMAC, flash, TRNG and timer adapters are copies of the Hub's (a shared ESP32-S3
//! board crate is a deferred item), and so is the BLE setup link. A dev-mode build compiles no eFuse
//! or HMAC adapter at all, so it cannot touch the fuses; only a dev-mode build compiles the ESP-NOW
//! coexistence probe of setup mode. The ESP-NOW transport (`espnow`) is in every build.

pub mod ble;
pub mod button;
#[cfg(feature = "dev-mode")]
pub mod coex;
#[cfg(not(feature = "dev-mode"))]
pub mod efuse;
pub mod espnow;
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
