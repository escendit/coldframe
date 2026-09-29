//! The eFuse key blocks.
//!
//! The ESP32-S3 has six 256-bit key blocks, `KEY0` to `KEY5` (eFuse blocks 4 to 9). Each has a
//! 4-bit purpose, a read-disable bit and a write-disable bit. Burning is irreversible: a bit once
//! set stays set.

use core::fmt;

/// Length of a key block in bytes.
pub const KEY_LENGTH: usize = 32;

/// One of the six key blocks.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum KeyBlock {
    /// `KEY0`, eFuse block 4.
    Key0,
    /// `KEY1`, eFuse block 5.
    Key1,
    /// `KEY2`, eFuse block 6.
    Key2,
    /// `KEY3`, eFuse block 7.
    Key3,
    /// `KEY4`, eFuse block 8.
    Key4,
    /// `KEY5`, eFuse block 9.
    Key5,
}

impl KeyBlock {
    /// Every key block, in order.
    pub const ALL: [Self; 6] = [
        Self::Key0,
        Self::Key1,
        Self::Key2,
        Self::Key3,
        Self::Key4,
        Self::Key5,
    ];

    /// The key number, 0 to 5.
    #[must_use]
    pub const fn index(self) -> u8 {
        match self {
            Self::Key0 => 0,
            Self::Key1 => 1,
            Self::Key2 => 2,
            Self::Key3 => 3,
            Self::Key4 => 4,
            Self::Key5 => 5,
        }
    }

    /// The key block with number `index`, if it is 0 to 5.
    #[must_use]
    pub const fn from_index(index: u8) -> Option<Self> {
        match index {
            0 => Some(Self::Key0),
            1 => Some(Self::Key1),
            2 => Some(Self::Key2),
            3 => Some(Self::Key3),
            4 => Some(Self::Key4),
            5 => Some(Self::Key5),
            _ => None,
        }
    }
}

impl fmt::Display for KeyBlock {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "KEY{}", self.index())
    }
}

/// The purpose of a key block, with the ESP32-S3 values.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum KeyPurpose {
    /// 0: user data, the purpose of an unused block.
    User,
    /// 1: reserved.
    Reserved,
    /// 2: first half of an XTS-AES-256 flash encryption key.
    XtsAes256Key1,
    /// 3: second half of an XTS-AES-256 flash encryption key.
    XtsAes256Key2,
    /// 4: XTS-AES-128 flash encryption key.
    XtsAes128Key,
    /// 5: HMAC downstream for both JTAG and the digital signature peripheral.
    HmacDownAll,
    /// 6: HMAC downstream for re-enabling JTAG.
    HmacDownJtag,
    /// 7: HMAC downstream for the digital signature peripheral.
    HmacDownDigitalSignature,
    /// 8: HMAC upstream: software supplies the message and reads the result. The Device identity.
    HmacUp,
    /// 9: secure boot digest 0.
    SecureBootDigest0,
    /// 10: secure boot digest 1.
    SecureBootDigest1,
    /// 11: secure boot digest 2.
    SecureBootDigest2,
    /// Any other value, preserved as read.
    Unknown(u8),
}

impl KeyPurpose {
    /// The purpose that the raw 4-bit value `raw` encodes.
    #[must_use]
    pub const fn from_raw(raw: u8) -> Self {
        match raw {
            0 => Self::User,
            1 => Self::Reserved,
            2 => Self::XtsAes256Key1,
            3 => Self::XtsAes256Key2,
            4 => Self::XtsAes128Key,
            5 => Self::HmacDownAll,
            6 => Self::HmacDownJtag,
            7 => Self::HmacDownDigitalSignature,
            8 => Self::HmacUp,
            9 => Self::SecureBootDigest0,
            10 => Self::SecureBootDigest1,
            11 => Self::SecureBootDigest2,
            other => Self::Unknown(other),
        }
    }

    /// The raw value of this purpose.
    #[must_use]
    pub const fn raw(self) -> u8 {
        match self {
            Self::User => 0,
            Self::Reserved => 1,
            Self::XtsAes256Key1 => 2,
            Self::XtsAes256Key2 => 3,
            Self::XtsAes128Key => 4,
            Self::HmacDownAll => 5,
            Self::HmacDownJtag => 6,
            Self::HmacDownDigitalSignature => 7,
            Self::HmacUp => 8,
            Self::SecureBootDigest0 => 9,
            Self::SecureBootDigest1 => 10,
            Self::SecureBootDigest2 => 11,
            Self::Unknown(raw) => raw,
        }
    }

    /// Whether the chip read-protects a key burned with this purpose, so software cannot read it.
    #[must_use]
    pub const fn is_read_protected_on_burn(self) -> bool {
        matches!(
            self,
            Self::XtsAes256Key1
                | Self::XtsAes256Key2
                | Self::XtsAes128Key
                | Self::HmacDownAll
                | Self::HmacDownJtag
                | Self::HmacDownDigitalSignature
                | Self::HmacUp
        )
    }
}

/// Why an eFuse operation failed.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum EfuseError {
    /// The block to burn is not unused.
    BlockInUse,
    /// Programming the block failed.
    BurnFailed,
}

impl fmt::Display for EfuseError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::BlockInUse => "eFuse key block in use",
            Self::BurnFailed => "eFuse burn failed",
        })
    }
}

impl core::error::Error for EfuseError {}

/// The eFuse key blocks of the chip.
///
/// There is deliberately no way to read a key: once burned with a protected purpose, only the
/// peripheral the purpose names can use it.
pub trait Efuse {
    /// The purpose of `block`, as the chip currently reports it.
    fn key_purpose(&self, block: KeyBlock) -> KeyPurpose;

    /// Whether software reads of `block` are disabled.
    fn is_read_protected(&self, block: KeyBlock) -> bool;

    /// Whether `block` is unused: purpose [`KeyPurpose::User`], all-zero data and no protection
    /// bit set.
    fn is_unused(&self, block: KeyBlock) -> bool;

    /// Burns `key` into the unused `block` with `purpose`, together with write protection and,
    /// for a protected purpose, read protection. Irreversible.
    ///
    /// # Errors
    ///
    /// [`EfuseError::BlockInUse`] when `block` is not unused, [`EfuseError::BurnFailed`] when
    /// programming fails. Callers re-read the block to confirm a burn.
    fn burn_key(
        &mut self,
        block: KeyBlock,
        key: &[u8; KEY_LENGTH],
        purpose: KeyPurpose,
    ) -> Result<(), EfuseError>;
}
