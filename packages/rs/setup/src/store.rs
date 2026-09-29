//! The `cf_setup` flash partition: the setup code and the provisioning record.
//!
//! | Offset | Record |
//! | --- | --- |
//! | `0x0000` | `b"CFPC" ‖ 0x01 ‖ code[8] ‖ SHA-256(prefix ‖ code)[0..4]` |
//! | `0x1000` | `b"CFWP" ‖ 0x02 ‖ u8 len ‖ ssid ‖ u8 len ‖ password ‖ u8 len ‖ site_id ‖ u8 len ‖ server_url ‖ SHA-256(prefix ‖ …)[0..4]` |
//!
//! Each record has its own sector, so writing the provisioning record never touches the code.
//!
//! - The setup code is written once, on first boot. A corrupt code record halts the Device
//!   ([`SetupStoreError::CorruptSetupCode`]); it is never silently regenerated, because the code
//!   may already be on a sticker.
//! - The provisioning record is written only after a successful Wi-Fi join and Server check. An
//!   erased sector means unprovisioned; a corrupt one reads as [`ProvisioningState::Corrupt`],
//!   which the caller treats as unprovisioned. Any version other than
//!   [`PROVISIONING_VERSION`] is corrupt, including Story 3.4's `0x01` record, which has no
//!   Server address: such a Hub is set up again.
//!
//! No error here carries the code, a password or any record byte.

use core::fmt;

use coldframe_hal::wifi::SSID_MAX_LENGTH;
use coldframe_hal::{Flash, FlashError, Trng, TrngError};
use coldframe_uplink::ServerUrl;
use coldframe_uplink::url::SERVER_URL_MAX_LENGTH;
use sha2::{Digest, Sha256};

use crate::code::{CODE_LENGTH, SetupCode};
use crate::{PASSWORD_MAX_LENGTH, SITE_ID_MAX_LENGTH};

/// Flash sector size: each record owns one.
pub const SECTOR_SIZE: u32 = 0x1000;

/// Offset of the setup-code record.
pub const SETUP_CODE_OFFSET: u32 = 0;

/// Offset of the provisioning record.
pub const PROVISIONING_OFFSET: u32 = 0x1000;

/// Smallest `cf_setup` partition: two sectors.
pub const PARTITION_SIZE: usize = 0x2000;

/// Magic of the setup-code record.
pub const SETUP_CODE_MAGIC: [u8; 4] = *b"CFPC";

/// Magic of the provisioning record.
pub const PROVISIONING_MAGIC: [u8; 4] = *b"CFWP";

/// Version of the setup-code record.
pub const SETUP_CODE_VERSION: u8 = 1;

/// Version of the provisioning record: 2 adds the Server address (Story 3.5).
pub const PROVISIONING_VERSION: u8 = 2;

const PREFIX_LENGTH: usize = 5;
const CHECK_LENGTH: usize = 4;

/// Length of the setup-code record.
pub const SETUP_CODE_RECORD_LENGTH: usize = PREFIX_LENGTH + CODE_LENGTH + CHECK_LENGTH;

/// Longest provisioning record.
pub const PROVISIONING_RECORD_MAX_LENGTH: usize = PREFIX_LENGTH
    + 4
    + SSID_MAX_LENGTH
    + PASSWORD_MAX_LENGTH
    + SITE_ID_MAX_LENGTH
    + SERVER_URL_MAX_LENGTH
    + CHECK_LENGTH;

// The record fits its sector.
const _: () = assert!(PROVISIONING_RECORD_MAX_LENGTH <= SECTOR_SIZE as usize);

/// Why the setup store failed. Carries no code, password or record content.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SetupStoreError {
    /// The setup-code record is neither erased nor valid. Erasing `cf_setup` is an operator
    /// decision; nothing was written.
    CorruptSetupCode,
    /// A record written to flash did not read back unchanged.
    NotVerified,
    /// A provisioning field is empty where required, too long, not UTF-8, or (the Server
    /// address) not a valid server URL.
    InvalidField,
    /// The TRNG produced no code.
    Trng(TrngError),
    /// A flash operation failed.
    Flash(FlashError),
}

impl fmt::Display for SetupStoreError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::CorruptSetupCode => f.write_str("setup code record is corrupt"),
            Self::NotVerified => f.write_str("setup record did not verify"),
            Self::InvalidField => f.write_str("invalid provisioning field"),
            Self::Trng(error) => write!(f, "trng: {error}"),
            Self::Flash(error) => write!(f, "flash: {error}"),
        }
    }
}

impl core::error::Error for SetupStoreError {}

impl From<FlashError> for SetupStoreError {
    fn from(error: FlashError) -> Self {
        Self::Flash(error)
    }
}

impl From<TrngError> for SetupStoreError {
    fn from(error: TrngError) -> Self {
        Self::Trng(error)
    }
}

/// Where the setup code came from on this boot.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum CodeSource {
    /// Drawn from the TRNG and written on this boot.
    Generated,
    /// Read from flash, written on an earlier boot.
    Stored,
}

fn check(body: &[u8]) -> [u8; CHECK_LENGTH] {
    let digest = Sha256::digest(body);
    let mut out = [0u8; CHECK_LENGTH];
    out.copy_from_slice(&digest[..CHECK_LENGTH]);
    out
}

fn encode_code_record(code: &SetupCode) -> [u8; SETUP_CODE_RECORD_LENGTH] {
    let mut record = [0u8; SETUP_CODE_RECORD_LENGTH];
    record[..4].copy_from_slice(&SETUP_CODE_MAGIC);
    record[4] = SETUP_CODE_VERSION;
    record[PREFIX_LENGTH..PREFIX_LENGTH + CODE_LENGTH].copy_from_slice(code.as_bytes());
    let digest = check(&record[..PREFIX_LENGTH + CODE_LENGTH]);
    record[PREFIX_LENGTH + CODE_LENGTH..].copy_from_slice(&digest);
    record
}

fn decode_code_record(record: &[u8; SETUP_CODE_RECORD_LENGTH]) -> Option<SetupCode> {
    let body = &record[..PREFIX_LENGTH + CODE_LENGTH];
    let valid = record[..4] == SETUP_CODE_MAGIC
        && record[4] == SETUP_CODE_VERSION
        && record[PREFIX_LENGTH + CODE_LENGTH..] == check(body);
    if !valid {
        return None;
    }
    SetupCode::parse(&record[PREFIX_LENGTH..PREFIX_LENGTH + CODE_LENGTH])
}

/// Writes `code` as the setup-code record, into an erased sector, and reads it back.
///
/// # Errors
///
/// [`SetupStoreError::NotVerified`] or a flash error.
pub fn write_setup_code<F: Flash>(flash: &mut F, code: &SetupCode) -> Result<(), SetupStoreError> {
    let record = encode_code_record(code);
    flash.erase(SETUP_CODE_OFFSET, SECTOR_SIZE)?;
    flash.write(SETUP_CODE_OFFSET, &record)?;
    let mut stored = [0u8; SETUP_CODE_RECORD_LENGTH];
    flash.read(SETUP_CODE_OFFSET, &mut stored)?;
    if stored != record {
        return Err(SetupStoreError::NotVerified);
    }
    Ok(())
}

/// Loads the setup code, or on first boot (record erased) draws one and writes it.
///
/// # Errors
///
/// [`SetupStoreError::CorruptSetupCode`] for a record that is neither erased nor valid (nothing
/// is written then), a TRNG or flash error, or [`SetupStoreError::NotVerified`].
pub fn load_or_create_code<T: Trng, F: Flash>(
    trng: &mut T,
    flash: &mut F,
) -> Result<(SetupCode, CodeSource), SetupStoreError> {
    let mut record = [0u8; SETUP_CODE_RECORD_LENGTH];
    flash.read(SETUP_CODE_OFFSET, &mut record)?;
    if record.iter().all(|&byte| byte == 0xFF) {
        let code = SetupCode::generate(trng)?;
        write_setup_code(flash, &code)?;
        return Ok((code, CodeSource::Generated));
    }
    let code = decode_code_record(&record).ok_or(SetupStoreError::CorruptSetupCode)?;
    Ok((code, CodeSource::Stored))
}

/// What the Hub was provisioned with. Deliberately not `Debug`: it holds the passphrase.
#[derive(Clone)]
pub struct ProvisioningRecord {
    ssid: heapless::String<SSID_MAX_LENGTH>,
    password: heapless::String<PASSWORD_MAX_LENGTH>,
    site_id: heapless::String<SITE_ID_MAX_LENGTH>,
    server: ServerUrl,
}

impl ProvisioningRecord {
    /// A record from its fields: a non-empty SSID of at most 32 bytes, a passphrase of at most
    /// 64 bytes (empty for an open network), a non-empty Site ID of at most 36 bytes, and the
    /// Server address, a valid [`ServerUrl`].
    ///
    /// # Errors
    ///
    /// [`SetupStoreError::InvalidField`].
    pub fn new(
        ssid: &str,
        password: &str,
        site_id: &str,
        server_url: &str,
    ) -> Result<Self, SetupStoreError> {
        if ssid.is_empty() || site_id.is_empty() {
            return Err(SetupStoreError::InvalidField);
        }
        Ok(Self {
            ssid: ssid.try_into().map_err(|_| SetupStoreError::InvalidField)?,
            password: password
                .try_into()
                .map_err(|_| SetupStoreError::InvalidField)?,
            site_id: site_id
                .try_into()
                .map_err(|_| SetupStoreError::InvalidField)?,
            server: ServerUrl::parse(server_url).map_err(|_| SetupStoreError::InvalidField)?,
        })
    }

    /// The SSID.
    #[must_use]
    pub fn ssid(&self) -> &str {
        &self.ssid
    }

    /// The passphrase. Never log it.
    #[must_use]
    pub fn password(&self) -> &str {
        &self.password
    }

    /// The Site ID.
    #[must_use]
    pub fn site_id(&self) -> &str {
        &self.site_id
    }

    /// The Server address as the app sent it.
    #[must_use]
    pub fn server_url(&self) -> &str {
        self.server.as_str()
    }

    /// The Server address, parsed.
    #[must_use]
    pub fn server(&self) -> &ServerUrl {
        &self.server
    }

    fn encode(&self, out: &mut [u8; PROVISIONING_RECORD_MAX_LENGTH]) -> usize {
        out[..4].copy_from_slice(&PROVISIONING_MAGIC);
        out[4] = PROVISIONING_VERSION;
        let mut at = PREFIX_LENGTH;
        for field in [
            self.ssid.as_bytes(),
            self.password.as_bytes(),
            self.site_id.as_bytes(),
            self.server.as_str().as_bytes(),
        ] {
            // Every field is at most 100 bytes.
            out[at] = u8::try_from(field.len()).unwrap_or(u8::MAX);
            out[at + 1..at + 1 + field.len()].copy_from_slice(field);
            at += 1 + field.len();
        }
        let digest = check(&out[..at]);
        out[at..at + CHECK_LENGTH].copy_from_slice(&digest);
        at + CHECK_LENGTH
    }

    fn decode(record: &[u8; PROVISIONING_RECORD_MAX_LENGTH]) -> Option<Self> {
        if record[..4] != PROVISIONING_MAGIC || record[4] != PROVISIONING_VERSION {
            return None;
        }
        let mut at = PREFIX_LENGTH;
        let mut fields: [&str; 4] = [""; 4];
        for (field, max) in fields.iter_mut().zip([
            SSID_MAX_LENGTH,
            PASSWORD_MAX_LENGTH,
            SITE_ID_MAX_LENGTH,
            SERVER_URL_MAX_LENGTH,
        ]) {
            let length = usize::from(*record.get(at)?);
            if length > max {
                return None;
            }
            *field = core::str::from_utf8(record.get(at + 1..at + 1 + length)?).ok()?;
            at += 1 + length;
        }
        if record.get(at..at + CHECK_LENGTH)? != check(&record[..at]) {
            return None;
        }
        Self::new(fields[0], fields[1], fields[2], fields[3]).ok()
    }
}

/// The provisioning sector as found at boot.
#[allow(
    clippy::large_enum_variant,
    reason = "no alloc to box into; read once at boot"
)]
pub enum ProvisioningState {
    /// Erased: the Hub has never been provisioned.
    Unprovisioned,
    /// A valid record.
    Provisioned(ProvisioningRecord),
    /// Neither erased nor valid. Treated as unprovisioned; the caller logs
    /// `corrupt provisioning record`.
    Corrupt,
}

impl ProvisioningState {
    /// Whether a valid record exists.
    #[must_use]
    pub fn is_provisioned(&self) -> bool {
        matches!(self, Self::Provisioned(_))
    }
}

/// Reads the provisioning sector.
///
/// # Errors
///
/// A flash error only; a corrupt record is [`ProvisioningState::Corrupt`].
pub fn load_provisioning<F: Flash>(flash: &mut F) -> Result<ProvisioningState, SetupStoreError> {
    let mut record = [0u8; PROVISIONING_RECORD_MAX_LENGTH];
    flash.read(PROVISIONING_OFFSET, &mut record)?;
    let state = if record.iter().all(|&byte| byte == 0xFF) {
        ProvisioningState::Unprovisioned
    } else {
        ProvisioningRecord::decode(&record)
            .map_or(ProvisioningState::Corrupt, ProvisioningState::Provisioned)
    };
    record.fill(0);
    Ok(state)
}

/// Erases the provisioning sector, writes `record` and reads it back.
///
/// # Errors
///
/// [`SetupStoreError::NotVerified`] or a flash error.
pub fn store_provisioning<F: Flash>(
    flash: &mut F,
    record: &ProvisioningRecord,
) -> Result<(), SetupStoreError> {
    let mut encoded = [0xFFu8; PROVISIONING_RECORD_MAX_LENGTH];
    let length = record.encode(&mut encoded);
    flash.erase(PROVISIONING_OFFSET, SECTOR_SIZE)?;
    let written = flash.write(PROVISIONING_OFFSET, &encoded[..length]);
    let mut stored = [0u8; PROVISIONING_RECORD_MAX_LENGTH];
    let verified = written
        .and_then(|()| flash.read(PROVISIONING_OFFSET, &mut stored[..length]))
        .map(|()| stored[..length] == encoded[..length]);
    encoded.fill(0);
    stored.fill(0);
    match verified {
        Ok(true) => Ok(()),
        Ok(false) => Err(SetupStoreError::NotVerified),
        Err(error) => Err(error.into()),
    }
}
