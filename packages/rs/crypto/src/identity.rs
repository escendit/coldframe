//! The Device identity: find or create the root, then derive the key hierarchy (AD-12).
//!
//! Hardware-agnostic: every function takes only `coldframe-hal` traits.
//!
//! - [`provision_efuse`] (release): the root lives in a read-protected eFuse key block with
//!   purpose HMAC upstream. It is burned once, on first boot, from TRNG bytes drawn with the radio
//!   on, and never leaves the chip; `K_dev` comes from the HMAC peripheral.
//! - [`provision_dev`] (dev mode): the root is a TRNG-generated software key kept in a flash
//!   region, and no eFuse is touched. The signature takes no eFuse or HMAC type.
//!
//! Both end in [`DeviceKeys::from_device_key`], so hardware and software derive the same keys from
//! the same root. No root, key or HMAC output is logged, formatted or returned in an error.

use core::fmt;

use coldframe_hal::{
    Efuse, EfuseError, Flash, FlashError, HmacError, HmacPeripheral, KeyBlock, KeyPurpose, Radio,
    RadioError, Trng, TrngError,
};
use sha2::{Digest, Sha256};

use crate::keys::DeviceKeys;
use crate::spec::{DEVICE_KEY_LABEL, ROOT_KEY_LENGTH};

/// Magic at the start of the dev-mode identity record.
pub const DEV_RECORD_MAGIC: [u8; 4] = *b"CFID";

/// Version of the dev-mode identity record.
pub const DEV_RECORD_VERSION: u8 = 1;

const DEV_RECORD_PREFIX_LENGTH: usize = DEV_RECORD_MAGIC.len() + 1;
const DEV_RECORD_CHECK_LENGTH: usize = 4;

/// Length of the dev-mode identity record: `b"CFID" ‖ 0x01 ‖ root[32] ‖ SHA-256(prefix ‖ root)[0..4]`.
pub const DEV_RECORD_LENGTH: usize =
    DEV_RECORD_PREFIX_LENGTH + ROOT_KEY_LENGTH + DEV_RECORD_CHECK_LENGTH;

/// Where the root of a provisioned identity came from.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum IdentitySource {
    /// Burned into this key block on this boot.
    Burned(KeyBlock),
    /// Found in this key block, burned on an earlier boot.
    Existing(KeyBlock),
    /// Dev mode: generated and stored in flash on this boot.
    DevGenerated,
    /// Dev mode: read from flash, stored on an earlier boot.
    DevStored,
}

impl fmt::Display for IdentitySource {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Burned(block) => write!(f, "burned {block}"),
            Self::Existing(block) => write!(f, "existing {block}"),
            Self::DevGenerated => f.write_str("generated"),
            Self::DevStored => f.write_str("stored"),
        }
    }
}

/// A provisioned identity. Deliberately not `Debug`: it holds every key.
pub struct Provisioned {
    /// `K_dev`, the purpose keys and the Device ID.
    pub keys: DeviceKeys,
    /// Where the root came from.
    pub source: IdentitySource,
}

/// Which hardware operation failed. Carries only the trait's error kind.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum HalError {
    /// The radio did not start.
    Radio(RadioError),
    /// The TRNG produced no bytes.
    Trng(TrngError),
    /// An eFuse operation failed.
    Efuse(EfuseError),
    /// The HMAC peripheral failed.
    Hmac(HmacError),
    /// A flash operation failed.
    Flash(FlashError),
}

impl fmt::Display for HalError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Radio(error) => write!(f, "radio: {error}"),
            Self::Trng(error) => write!(f, "trng: {error}"),
            Self::Efuse(error) => write!(f, "efuse: {error}"),
            Self::Hmac(error) => write!(f, "hmac: {error}"),
            Self::Flash(error) => write!(f, "flash: {error}"),
        }
    }
}

/// Why no identity could be provisioned. Carries no key material.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum IdentityError {
    /// More than one key block has purpose HMAC upstream; nothing was burned.
    AmbiguousIdentity,
    /// The HMAC upstream block is not read-protected; it was not used.
    IdentityNotProtected,
    /// No identity exists and no key block is unused; nothing was burned.
    NoFreeKeyBlock,
    /// The TRNG returned all-zero or all-`0xFF` bytes; nothing was burned or stored.
    BadEntropy,
    /// The burn reported success, but the block does not read back as a read-protected HMAC
    /// upstream key.
    BurnNotVerified,
    /// The dev-mode record in flash is neither erased nor valid; nothing was written.
    CorruptDevIdentity,
    /// The dev-mode record written to flash did not read back unchanged.
    DevIdentityNotVerified,
    /// A hardware operation failed.
    Hal(HalError),
}

impl fmt::Display for IdentityError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::AmbiguousIdentity => f.write_str("more than one HMAC upstream key block"),
            Self::IdentityNotProtected => f.write_str("identity key block is not read-protected"),
            Self::NoFreeKeyBlock => f.write_str("no unused eFuse key block"),
            Self::BadEntropy => f.write_str("TRNG output failed the sanity check"),
            Self::BurnNotVerified => f.write_str("eFuse burn did not verify"),
            Self::CorruptDevIdentity => f.write_str("dev identity record is corrupt"),
            Self::DevIdentityNotVerified => f.write_str("dev identity record did not verify"),
            Self::Hal(error) => write!(f, "hardware failure: {error}"),
        }
    }
}

impl core::error::Error for IdentityError {}

impl From<RadioError> for IdentityError {
    fn from(error: RadioError) -> Self {
        Self::Hal(HalError::Radio(error))
    }
}

impl From<TrngError> for IdentityError {
    fn from(error: TrngError) -> Self {
        Self::Hal(HalError::Trng(error))
    }
}

impl From<EfuseError> for IdentityError {
    fn from(error: EfuseError) -> Self {
        Self::Hal(HalError::Efuse(error))
    }
}

impl From<HmacError> for IdentityError {
    fn from(error: HmacError) -> Self {
        Self::Hal(HalError::Hmac(error))
    }
}

impl From<FlashError> for IdentityError {
    fn from(error: FlashError) -> Self {
        Self::Hal(HalError::Flash(error))
    }
}

/// A secret buffer that is overwritten with zeros when dropped, on every return path.
struct Wiped<const N: usize>([u8; N]);

impl<const N: usize> Drop for Wiped<N> {
    fn drop(&mut self) {
        self.0.fill(0);
        // The crate forbids `unsafe`, so no `write_volatile`: `black_box` makes the compiler treat
        // the zeroed buffer as read, so the stores above cannot be removed as dead.
        core::hint::black_box(&mut self.0);
    }
}

/// `K_dev = HMAC-SHA256(key = root in block, msg = "coldframe/device/v1")` on the HMAC
/// peripheral, then every purpose key and the Device ID.
///
/// # Errors
///
/// [`IdentityError::Hal`] when the peripheral fails, for example because `block` is not an HMAC
/// upstream key.
pub fn device_keys_from_hmac<H: HmacPeripheral>(
    hmac: &mut H,
    block: KeyBlock,
) -> Result<DeviceKeys, IdentityError> {
    let device_key = hmac.hmac_sha256(block, DEVICE_KEY_LABEL.as_bytes())?;
    Ok(DeviceKeys::from_device_key(device_key))
}

fn draw_root<R: Radio, T: Trng>(
    radio: &mut R,
    trng: &mut T,
    root: &mut [u8; ROOT_KEY_LENGTH],
) -> Result<(), IdentityError> {
    // The radio is the TRNG's entropy source: it runs before any root byte is drawn.
    radio.enable()?;
    trng.fill(root)?;
    if root.iter().all(|&byte| byte == 0x00) || root.iter().all(|&byte| byte == 0xFF) {
        return Err(IdentityError::BadEntropy);
    }
    Ok(())
}

/// Release identity: finds the eFuse root, or burns one on first boot, and derives the keys.
///
/// 1. A key block with purpose HMAC upstream is the identity. Two are ambiguous; one that is not
///    read-protected is refused. Either way nothing is burned.
/// 2. Otherwise: turn the radio on, draw 32 TRNG bytes, check them, burn them into the first
///    unused block with purpose HMAC upstream, and re-read the block to confirm purpose and read
///    protection.
/// 3. Derive `K_dev` through the HMAC peripheral ([`device_keys_from_hmac`]).
///
/// The root exists in RAM only between steps 2 and the burn, and is wiped on every path.
///
/// # Errors
///
/// See [`IdentityError`]; every error leaves the eFuses as they were, except
/// [`IdentityError::BurnNotVerified`] and a failure after the burn.
pub fn provision_efuse<R: Radio, T: Trng, E: Efuse, H: HmacPeripheral>(
    radio: &mut R,
    trng: &mut T,
    efuse: &mut E,
    hmac: &mut H,
) -> Result<Provisioned, IdentityError> {
    let mut existing = None;
    for block in KeyBlock::ALL {
        if efuse.key_purpose(block) == KeyPurpose::HmacUp {
            if existing.is_some() {
                return Err(IdentityError::AmbiguousIdentity);
            }
            existing = Some(block);
        }
    }
    if let Some(block) = existing {
        if !efuse.is_read_protected(block) {
            return Err(IdentityError::IdentityNotProtected);
        }
        return Ok(Provisioned {
            keys: device_keys_from_hmac(hmac, block)?,
            source: IdentitySource::Existing(block),
        });
    }

    let mut root = Wiped([0u8; ROOT_KEY_LENGTH]);
    draw_root(radio, trng, &mut root.0)?;
    let block = KeyBlock::ALL
        .into_iter()
        .find(|&block| efuse.is_unused(block))
        .ok_or(IdentityError::NoFreeKeyBlock)?;
    efuse.burn_key(block, &root.0, KeyPurpose::HmacUp)?;
    drop(root);

    if efuse.key_purpose(block) != KeyPurpose::HmacUp || !efuse.is_read_protected(block) {
        return Err(IdentityError::BurnNotVerified);
    }
    Ok(Provisioned {
        keys: device_keys_from_hmac(hmac, block)?,
        source: IdentitySource::Burned(block),
    })
}

fn dev_record_check(prefix_and_root: &[u8]) -> [u8; DEV_RECORD_CHECK_LENGTH] {
    let digest = Sha256::digest(prefix_and_root);
    let mut check = [0u8; DEV_RECORD_CHECK_LENGTH];
    check.copy_from_slice(&digest[..DEV_RECORD_CHECK_LENGTH]);
    check
}

/// Encodes the dev-mode identity record for `root`.
fn encode_dev_record(root: &[u8; ROOT_KEY_LENGTH], record: &mut [u8; DEV_RECORD_LENGTH]) {
    let (prefix, rest) = record.split_at_mut(DEV_RECORD_PREFIX_LENGTH);
    prefix[..DEV_RECORD_MAGIC.len()].copy_from_slice(&DEV_RECORD_MAGIC);
    prefix[DEV_RECORD_MAGIC.len()] = DEV_RECORD_VERSION;
    rest[..ROOT_KEY_LENGTH].copy_from_slice(root);
    let check = dev_record_check(&record[..DEV_RECORD_PREFIX_LENGTH + ROOT_KEY_LENGTH]);
    record[DEV_RECORD_PREFIX_LENGTH + ROOT_KEY_LENGTH..].copy_from_slice(&check);
}

/// The root of a valid dev-mode identity record, or `None`.
fn decode_dev_record(
    record: &[u8; DEV_RECORD_LENGTH],
    root: &mut [u8; ROOT_KEY_LENGTH],
) -> Option<()> {
    let body = &record[..DEV_RECORD_PREFIX_LENGTH + ROOT_KEY_LENGTH];
    let valid = record[..DEV_RECORD_MAGIC.len()] == DEV_RECORD_MAGIC
        && record[DEV_RECORD_MAGIC.len()] == DEV_RECORD_VERSION
        && record[DEV_RECORD_PREFIX_LENGTH + ROOT_KEY_LENGTH..] == dev_record_check(body);
    if !valid {
        return None;
    }
    root.copy_from_slice(
        &record[DEV_RECORD_PREFIX_LENGTH..DEV_RECORD_PREFIX_LENGTH + ROOT_KEY_LENGTH],
    );
    Some(())
}

/// Dev-mode identity: a software root in `flash` (the `cf_ident` partition), never an eFuse.
///
/// - Erased region (every record byte `0xFF`): turn the radio on, draw a checked TRNG root, write
///   the `CFID` record at offset 0 and read it back.
/// - Valid record: use its root; nothing is written.
/// - Anything else: [`IdentityError::CorruptDevIdentity`]. A corrupt identity is never replaced
///   silently; erasing the partition is an operator decision.
///
/// # Errors
///
/// See [`IdentityError`].
pub fn provision_dev<R: Radio, T: Trng, F: Flash>(
    radio: &mut R,
    trng: &mut T,
    flash: &mut F,
) -> Result<Provisioned, IdentityError> {
    let mut record = Wiped([0u8; DEV_RECORD_LENGTH]);
    let mut root = Wiped([0u8; ROOT_KEY_LENGTH]);
    flash.read(0, &mut record.0)?;

    if record.0.iter().all(|&byte| byte == 0xFF) {
        draw_root(radio, trng, &mut root.0)?;
        encode_dev_record(&root.0, &mut record.0);
        flash.write(0, &record.0)?;
        let mut stored = Wiped([0u8; DEV_RECORD_LENGTH]);
        flash.read(0, &mut stored.0)?;
        if stored.0 != record.0 {
            return Err(IdentityError::DevIdentityNotVerified);
        }
        return Ok(Provisioned {
            keys: DeviceKeys::from_root_key(&root.0),
            source: IdentitySource::DevGenerated,
        });
    }

    decode_dev_record(&record.0, &mut root.0).ok_or(IdentityError::CorruptDevIdentity)?;
    Ok(Provisioned {
        keys: DeviceKeys::from_root_key(&root.0),
        source: IdentitySource::DevStored,
    })
}
