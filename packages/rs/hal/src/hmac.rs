//! The HMAC peripheral, keyed by an eFuse block.

use core::fmt;

use crate::efuse::KeyBlock;

/// Length of an HMAC-SHA256 output in bytes.
pub const HMAC_LENGTH: usize = 32;

/// Why the HMAC peripheral produced no output.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum HmacError {
    /// The key block's purpose is not HMAC upstream.
    KeyPurposeMismatch,
    /// The peripheral failed.
    Hardware,
}

impl fmt::Display for HmacError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::KeyPurposeMismatch => "HMAC key purpose mismatch",
            Self::Hardware => "HMAC hardware failure",
        })
    }
}

impl core::error::Error for HmacError {}

/// The HMAC peripheral in upstream mode: software supplies the message and reads the result.
pub trait HmacPeripheral {
    /// `HMAC-SHA256(key = block, msg)`, where the key never leaves the eFuse block.
    ///
    /// # Errors
    ///
    /// [`HmacError::KeyPurposeMismatch`] unless `block` has purpose
    /// [`KeyPurpose::HmacUp`](crate::efuse::KeyPurpose::HmacUp); [`HmacError::Hardware`] when the
    /// peripheral fails.
    fn hmac_sha256(&mut self, block: KeyBlock, msg: &[u8]) -> Result<[u8; HMAC_LENGTH], HmacError>;
}
