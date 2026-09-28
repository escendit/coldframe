//! Device cryptography for Coldframe firmware, as `packages/crypto-spec` defines it.
//!
//! - [`aead`]: ChaCha20-Poly1305 over caller buffers.
//! - [`keys`]: the key hierarchy, `K_dev` → purpose keys and Device ID (AD-12).
//! - [`frame`]: ChaCha20-Poly1305 frame and downlink sealing with the Device-ID nonce and the
//!   64-entry replay window (AD-12, AD-17).
//! - [`hpke`]: RFC 9180 base-mode sealing of `K_dev` to the Server's enrolment key.
//! - [`setup`]: the proof-of-possession BLE setup session (AD-25).
//! - [`heartbeat`]: the HMAC over a Hub request (AD-12).
//!
//! Every label, size and header name comes from [`spec`], which `packages/crypto-spec` generates.
//! The crate is `no_std` without `alloc`: callers pass output buffers. No type here implements
//! `Debug` for key material, so keys cannot end up in a log by accident.

#![cfg_attr(not(test), no_std)]

pub mod aead;
mod error;
pub mod frame;
pub mod heartbeat;
pub mod hex;
pub mod hpke;
pub mod keys;
pub mod setup;
pub mod spec;

pub use error::Error;
pub use keys::{DeviceId, DeviceKeys};
