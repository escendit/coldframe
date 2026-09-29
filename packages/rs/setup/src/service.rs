//! The setup service: accept a BLE connection, run a [`Session`] over it, repeat until the
//! Device is provisioned.
//!
//! [`run_setup`] owns the loop. Per connection it reassembles frames, feeds them to the session
//! and carries out what the session asks for: send a frame, scan or join Wi-Fi, store the
//! provisioning record, or disconnect. A connection ends when the app disconnects, when the
//! session closes it, or after [`SESSION_IDLE_TIMEOUT_MS`] without a write; the Device then
//! advertises again. Once a session has stored the provisioning record, the service lets that
//! connection finish and returns.

use core::fmt;

use coldframe_crypto::DeviceKeys;
use coldframe_hal::{AccessPoint, Flash, LinkError, SetupLink, Trng, Wifi};

use crate::code::SetupCode;
use crate::framing::{Reassembler, fragments};
use crate::session::{Action, Session};
use crate::store::{
    CodeSource, ProvisioningRecord, ProvisioningState, SetupStoreError, load_or_create_code,
    load_provisioning, store_provisioning,
};
use crate::{MAX_FRAME, SCAN_CAPACITY, SESSION_IDLE_TIMEOUT_MS};

/// The largest BLE payload the service handles; a larger ATT MTU is used only up to this.
pub const MAX_PAYLOAD: usize = 512;

/// The outcome of a completed setup. Deliberately not `Debug`: it holds the passphrase.
pub struct Provisioned {
    /// What was stored in the provisioning record.
    pub record: ProvisioningRecord,
}

/// What the Device found in `cf_setup` at boot. Deliberately not `Debug`: it holds the code.
pub struct Boot {
    /// The setup code, loaded or generated on this boot.
    pub code: SetupCode,
    /// Whether it was generated on this boot.
    pub code_source: CodeSource,
    /// The provisioning sector.
    pub provisioning: ProvisioningState,
}

impl Boot {
    /// Whether the Hub still needs setting up: no valid provisioning record. A corrupt record
    /// counts as none.
    #[must_use]
    pub fn needs_setup(&self) -> bool {
        !self.provisioning.is_provisioned()
    }

    /// Whether this boot prints the setup code: when it was generated, and on every boot while
    /// unprovisioned; never once provisioned.
    #[must_use]
    pub fn shows_code(&self) -> bool {
        self.needs_setup()
    }

    /// Whether this boot advertises the setup service: only while unprovisioned.
    #[must_use]
    pub fn advertises(&self) -> bool {
        self.needs_setup()
    }
}

/// Loads (or on first boot creates) the setup code and reads the provisioning sector.
///
/// # Errors
///
/// See [`load_or_create_code`]; [`SetupStoreError::CorruptSetupCode`] halts the Device.
pub fn boot<T: Trng, F: Flash>(trng: &mut T, flash: &mut F) -> Result<Boot, SetupStoreError> {
    let (code, code_source) = load_or_create_code(trng, flash)?;
    let provisioning = load_provisioning(flash)?;
    Ok(Boot {
        code,
        code_source,
        provisioning,
    })
}

/// Why the setup service stopped before the Device was provisioned. Carries no payload.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SetupError {
    /// The BLE link could not advertise or accept a connection.
    Link(LinkError),
}

impl fmt::Display for SetupError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Link(error) => write!(f, "setup link: {error}"),
        }
    }
}

impl core::error::Error for SetupError {}

/// Runs BLE setup sessions until one stores the provisioning record.
///
/// `keys` are the Device's keys (for the Device ID and enrolment); `code` is its setup code;
/// `firmware_version` goes into `Identity`.
///
/// # Errors
///
/// [`SetupError::Link`] when the link cannot accept a connection.
pub async fn run_setup<L, W, T, F>(
    link: &mut L,
    wifi: &mut W,
    trng: &mut T,
    flash: &mut F,
    keys: &DeviceKeys,
    code: &SetupCode,
    firmware_version: &str,
) -> Result<Provisioned, SetupError>
where
    L: SetupLink,
    W: Wifi,
    T: Trng,
    F: Flash,
{
    loop {
        link.accept().await.map_err(SetupError::Link)?;
        let mut session = Session::new(code, keys, firmware_version);
        let record = serve(link, wifi, trng, flash, &mut session).await;
        link.disconnect().await;
        if let Some(record) = record {
            return Ok(Provisioned { record });
        }
    }
}

/// Serves one connection until it ends; returns the provisioning record it stored, if any.
async fn serve<L, W, T, F>(
    link: &mut L,
    wifi: &mut W,
    trng: &mut T,
    flash: &mut F,
    session: &mut Session<'_>,
) -> Option<ProvisioningRecord>
where
    L: SetupLink,
    W: Wifi,
    T: Trng,
    F: Flash,
{
    let mut reassembler = Reassembler::new();
    let mut payload = [0u8; MAX_PAYLOAD];
    let mut out = [0u8; MAX_FRAME];
    let mut stored = None;
    loop {
        let Ok(length) = link.receive(&mut payload, SESSION_IDLE_TIMEOUT_MS).await else {
            // Disconnected, idle for too long, or the stack failed: the connection is over.
            return stored;
        };
        let mut action = match reassembler.push(&payload[..length]) {
            Ok(Some(frame)) => session.on_frame(trng, frame, &mut out),
            Ok(None) => continue,
            Err(_) => session.on_frame_error(&mut out),
        };
        payload.fill(0);
        loop {
            action = match action {
                Action::Nothing => break,
                Action::Send(length) => {
                    if send_frame(link, &out[..length]).await.is_err() {
                        return stored;
                    }
                    break;
                }
                Action::SendThenClose(length) => {
                    let _ = send_frame(link, &out[..length]).await;
                    return stored;
                }
                Action::Close => return stored,
                Action::Scan => {
                    let mut heard = [AccessPoint::EMPTY; SCAN_CAPACITY];
                    let result = wifi.scan(&mut heard).await;
                    session.on_scan(
                        result.map(|count| &heard[..count.min(SCAN_CAPACITY)]),
                        &mut out,
                    )
                }
                Action::Join => {
                    let result = match session.join_target() {
                        Some(target) => {
                            wifi.join(target.ssid, target.password, target.bssid, target.channel)
                                .await
                        }
                        None => Err(coldframe_hal::JoinError::Failed),
                    };
                    session.on_join(result, &mut out)
                }
                Action::Persist => {
                    let record = session.provisioning_record();
                    let saved = match &record {
                        Some(record) => store_provisioning(flash, record).is_ok(),
                        None => false,
                    };
                    if saved {
                        stored = record;
                    }
                    session.on_persisted(saved, &mut out)
                }
            };
        }
        out.fill(0);
    }
}

async fn send_frame<L: SetupLink>(link: &mut L, frame: &[u8]) -> Result<(), LinkError> {
    let max_payload = link.max_payload().clamp(2, MAX_PAYLOAD);
    let mut buffer = [0u8; MAX_PAYLOAD];
    for fragment in fragments(frame, max_payload) {
        let length = fragment.write_to(&mut buffer);
        link.send(&buffer[..length]).await?;
    }
    Ok(())
}
