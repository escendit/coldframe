//! The Device side of one BLE setup session, as a state machine without I/O.
//!
//! The service feeds it whole frames and the outcomes of the Wi-Fi and flash work it asks for;
//! the session answers with an [`Action`] and, for a reply, the frame to send in `out`.
//!
//! # Rules (Hub)
//!
//! - The first frame is a `SessionHello` (protocol version 1, a 32-byte key). The Device answers
//!   `SessionHelloReply` with a fresh TRNG X25519 key. A malformed hello closes the connection.
//! - Every later frame is a `SealedSetupMessage` opened by [`SetupSession`] (`Role::Device`):
//!   - the first message does not open → wrong setup code: one sealed `SetupError` under the
//!     Device's own keys, then close;
//!   - a later message does not open, or its counter does not increase → close, no reply.
//! - `IdentityRequest` and `WifiScanRequest` may come at any time.
//! - `SiteBinding` needs a non-empty `site_id` and no `lot_id`; it has no reply on success.
//! - `EnrolmentRequest` needs a 32-byte key whose fingerprint matches; the reply is `K_dev`
//!   sealed with HPKE, from 32 fresh TRNG bytes.
//! - `WifiConfig` is accepted only after a `SiteBinding` and an `EnrolmentResponse` in this
//!   session. The Device scans, takes the strongest BSSID for the SSID and joins it unless that
//!   network is WPA3-only or otherwise unsupported. Only after a successful join is the
//!   provisioning record written, and only then is `CONNECTED` sent.
//! - Messages only the Device sends (`Identity`, `WifiScanList`, `WifiResult`,
//!   `EnrolmentResponse`) are `UNEXPECTED_MESSAGE` when the app sends them; a `SetupError` from
//!   the app is ignored.
//! - Once this session has stored the provisioning record, `SiteBinding`, `EnrolmentRequest` and
//!   `WifiConfig` are `UNEXPECTED_MESSAGE`: nothing is joined or stored a second time.

use coldframe_crypto::hpke::{fingerprint, public_key, seal_enrolment};
use coldframe_crypto::setup::{Role, SetupSession};
use coldframe_crypto::spec::X25519_KEY_LENGTH;
use coldframe_crypto::{DeviceKeys, Error as CryptoError};
use coldframe_hal::wifi::SSID_MAX_LENGTH;
use coldframe_hal::{AccessPoint, JoinError, Security, Trng, WifiError};
use coldframe_protocol::setup_v1::SetupMessage_::Body;
use coldframe_protocol::setup_v1::{
    DeviceKind, EnrolmentRequest, EnrolmentResponse, Identity, SealedSetupMessage, SessionHello,
    SessionHelloReply, SetupError, SetupErrorCode, SetupMessage, SiteBinding, WifiConfig,
    WifiNetwork, WifiResult, WifiScanList, WifiSecurity, WifiStatus,
};
use coldframe_protocol::{PROTOCOL_VERSION, SETUP_MESSAGE_MAX_SIZE, decode, encode};

use crate::code::SetupCode;
use crate::store::ProvisioningRecord;
use crate::{MAX_NETWORKS, PASSWORD_MAX_LENGTH, SCAN_CAPACITY, SITE_ID_MAX_LENGTH};

/// What the service does next.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Action {
    /// Nothing to send; wait for the next frame.
    Nothing,
    /// Send the frame in `out[..n]`, then wait for the next frame.
    Send(usize),
    /// Send the frame in `out[..n]`, then disconnect.
    SendThenClose(usize),
    /// Disconnect without a reply.
    Close,
    /// Scan Wi-Fi and pass the result to [`Session::on_scan`].
    Scan,
    /// Join [`Session::join_target`] and pass the result to [`Session::on_join`].
    Join,
    /// Store [`Session::provisioning_record`] and pass the outcome to [`Session::on_persisted`].
    Persist,
}

/// The access point to join. Deliberately not `Debug`: it holds the passphrase.
pub struct JoinTarget<'a> {
    /// The SSID.
    pub ssid: &'a str,
    /// The passphrase; empty for an open network.
    pub password: &'a str,
    /// The strongest BSSID heard for the SSID.
    pub bssid: [u8; 6],
    /// Its channel.
    pub channel: u8,
}

enum Phase {
    AwaitHello,
    Open(SetupSession),
}

/// The Wi-Fi work in flight.
#[derive(Clone, Copy, PartialEq, Eq)]
enum Pending {
    None,
    /// A `WifiScanRequest` waits for its scan.
    ScanList,
    /// A `WifiConfig` waits for its scan.
    ConfigScan,
    /// A `WifiConfig` waits for its join.
    Join,
    /// A successful join waits for the provisioning record.
    Persist,
}

/// A requested network. Deliberately not `Debug`.
struct RequestedWifi {
    ssid: heapless::String<SSID_MAX_LENGTH>,
    password: heapless::String<PASSWORD_MAX_LENGTH>,
    bssid: [u8; 6],
    channel: u8,
}

/// One BLE session, Device side. Deliberately not `Debug`: it holds the session keys and, while
/// joining, the passphrase.
pub struct Session<'a> {
    code: &'a SetupCode,
    keys: &'a DeviceKeys,
    firmware_version: &'a str,
    phase: Phase,
    site_id: Option<heapless::String<SITE_ID_MAX_LENGTH>>,
    enrolled: bool,
    wifi: Option<RequestedWifi>,
    pending: Pending,
    provisioned: bool,
}

/// Wipes a plaintext buffer on every return path.
struct Wiped<const N: usize>([u8; N]);

impl<const N: usize> Drop for Wiped<N> {
    fn drop(&mut self) {
        self.0.fill(0);
        core::hint::black_box(&mut self.0);
    }
}

impl<'a> Session<'a> {
    /// A session that waits for its `SessionHello`.
    #[must_use]
    pub fn new(code: &'a SetupCode, keys: &'a DeviceKeys, firmware_version: &'a str) -> Self {
        Self {
            code,
            keys,
            firmware_version,
            phase: Phase::AwaitHello,
            site_id: None,
            enrolled: false,
            wifi: None,
            pending: Pending::None,
            provisioned: false,
        }
    }

    /// Whether this session stored a provisioning record.
    #[must_use]
    pub fn is_provisioned(&self) -> bool {
        self.provisioned
    }

    /// Handles one reassembled frame.
    pub fn on_frame<T: Trng>(&mut self, trng: &mut T, frame: &[u8], out: &mut [u8]) -> Action {
        if self.pending != Pending::None {
            // The service finishes Wi-Fi and flash work before it reads the next frame.
            return self.error(SetupErrorCode::UnexpectedMessage, out);
        }
        match self.phase {
            Phase::AwaitHello => self.on_hello(trng, frame, out),
            Phase::Open(_) => self.on_sealed(trng, frame, out),
        }
    }

    /// Handles a payload that could not be reassembled: a sealed `MALFORMED_MESSAGE` once the
    /// session is open, a disconnect before.
    pub fn on_frame_error(&mut self, out: &mut [u8]) -> Action {
        match self.phase {
            Phase::AwaitHello => Action::Close,
            Phase::Open(_) => self.error(SetupErrorCode::MalformedMessage, out),
        }
    }

    fn on_hello<T: Trng>(&mut self, trng: &mut T, frame: &[u8], out: &mut [u8]) -> Action {
        let Ok(hello) = decode::<SessionHello>(frame) else {
            return Action::Close;
        };
        let Ok(app_public) = <[u8; X25519_KEY_LENGTH]>::try_from(hello.app_public_key.as_slice())
        else {
            return Action::Close;
        };
        if hello.protocol_version != PROTOCOL_VERSION {
            return Action::Close;
        }
        let mut private = Wiped([0u8; X25519_KEY_LENGTH]);
        if trng.fill(&mut private.0).is_err() {
            return Action::Close;
        }
        let Ok(session) =
            SetupSession::new(Role::Device, &private.0, &app_public, self.code.as_str())
        else {
            return Action::Close;
        };
        let reply = SessionHelloReply {
            protocol_version: PROTOCOL_VERSION,
            device_public_key: heapless::Vec::from_array(public_key(&private.0)),
        };
        match encode(&reply, out) {
            Ok(length) => {
                self.phase = Phase::Open(session);
                Action::Send(length)
            }
            Err(_) => Action::Close,
        }
    }

    fn on_sealed<T: Trng>(&mut self, trng: &mut T, frame: &[u8], out: &mut [u8]) -> Action {
        let Phase::Open(session) = &mut self.phase else {
            return Action::Close;
        };
        let Ok(sealed) = decode::<SealedSetupMessage>(frame) else {
            return self.error(SetupErrorCode::MalformedMessage, out);
        };
        if sealed.protocol_version != PROTOCOL_VERSION {
            return self.error(SetupErrorCode::MalformedMessage, out);
        }
        let mut plain = Wiped([0u8; SETUP_MESSAGE_MAX_SIZE]);
        let length = match session.open(sealed.counter, &sealed.ciphertext, &mut plain.0) {
            Ok(length) => length,
            Err(CryptoError::WrongSetupCode) => {
                // Sealed under the Device's own keys: the app cannot open it either, and says
                // "wrong setup code".
                return match self.error(SetupErrorCode::MalformedMessage, out) {
                    Action::Send(length) => Action::SendThenClose(length),
                    _ => Action::Close,
                };
            }
            // Tampered or replayed: end the session without a word.
            Err(_) => return Action::Close,
        };
        let Ok(message) = decode::<SetupMessage>(&plain.0[..length]) else {
            return self.error(SetupErrorCode::MalformedMessage, out);
        };
        drop(plain);
        if message.protocol_version != PROTOCOL_VERSION {
            return self.error(SetupErrorCode::MalformedMessage, out);
        }
        let Some(body) = message.body else {
            return self.error(SetupErrorCode::MalformedMessage, out);
        };
        match body {
            // Once provisioned, nothing may change what was stored: no second bind, enrolment,
            // join or persist in this session.
            Body::SiteBinding(_) | Body::EnrolmentRequest(_) | Body::WifiConfig(_)
                if self.provisioned =>
            {
                self.error(SetupErrorCode::UnexpectedMessage, out)
            }
            Body::IdentityRequest(_) => self.identity(out),
            Body::WifiScanRequest(_) => {
                self.pending = Pending::ScanList;
                Action::Scan
            }
            Body::SiteBinding(binding) => self.site_binding(&binding, out),
            Body::EnrolmentRequest(request) => self.enrolment(trng, &request, out),
            Body::WifiConfig(config) => self.wifi_config(&config, out),
            Body::Identity(_)
            | Body::WifiScanList(_)
            | Body::WifiResult(_)
            | Body::EnrolmentResponse(_) => self.error(SetupErrorCode::UnexpectedMessage, out),
            Body::Error(_) => Action::Nothing,
        }
    }

    fn identity(&mut self, out: &mut [u8]) -> Action {
        let Ok(firmware_version) = self.firmware_version.try_into() else {
            return self.error(SetupErrorCode::Internal, out);
        };
        let identity = Identity {
            device_id: heapless::Vec::from_array(*self.keys.device_id.as_bytes()),
            kind: DeviceKind::Hub,
            firmware_version,
        };
        self.reply(Body::Identity(identity), out)
    }

    fn site_binding(&mut self, binding: &SiteBinding, out: &mut [u8]) -> Action {
        if binding.site_id.is_empty() || binding.lot_id().is_some() {
            return self.error(SetupErrorCode::MalformedMessage, out);
        }
        self.site_id = Some(binding.site_id.clone());
        Action::Nothing
    }

    fn enrolment<T: Trng>(
        &mut self,
        trng: &mut T,
        request: &EnrolmentRequest,
        out: &mut [u8],
    ) -> Action {
        let Ok(server_key) =
            <[u8; X25519_KEY_LENGTH]>::try_from(request.server_public_key.as_slice())
        else {
            return self.error(SetupErrorCode::MalformedMessage, out);
        };
        if request.fingerprint.len() != 2 * X25519_KEY_LENGTH {
            return self.error(SetupErrorCode::MalformedMessage, out);
        }
        if request.fingerprint.as_bytes() != fingerprint(&server_key) {
            return self.error(SetupErrorCode::FingerprintMismatch, out);
        }
        let mut ikm_e = Wiped([0u8; X25519_KEY_LENGTH]);
        if trng.fill(&mut ikm_e.0).is_err() {
            return self.error(SetupErrorCode::Internal, out);
        }
        let Ok(sealed) = seal_enrolment(&server_key, &ikm_e.0, self.keys) else {
            // A small-order key: the app sent something that is not an X25519 public key.
            return self.error(SetupErrorCode::MalformedMessage, out);
        };
        drop(ikm_e);
        let response = EnrolmentResponse {
            device_id: heapless::Vec::from_array(*sealed.device_id.as_bytes()),
            enc: heapless::Vec::from_array(sealed.enc),
            ciphertext: heapless::Vec::from_array(sealed.ciphertext),
        };
        match self.reply(Body::EnrolmentResponse(response), out) {
            action @ Action::Send(_) => {
                self.enrolled = true;
                action
            }
            other => other,
        }
    }

    fn wifi_config(&mut self, config: &WifiConfig, out: &mut [u8]) -> Action {
        if self.site_id.is_none() || !self.enrolled {
            return self.error(SetupErrorCode::UnexpectedMessage, out);
        }
        if config.ssid.is_empty() {
            return self.error(SetupErrorCode::MalformedMessage, out);
        }
        self.wifi = Some(RequestedWifi {
            ssid: config.ssid.clone(),
            password: config.password.clone(),
            bssid: [0; 6],
            channel: 0,
        });
        self.pending = Pending::ConfigScan;
        Action::Scan
    }

    /// Handles the outcome of the scan an [`Action::Scan`] asked for.
    pub fn on_scan(&mut self, result: Result<&[AccessPoint], WifiError>, out: &mut [u8]) -> Action {
        let pending = core::mem::replace(&mut self.pending, Pending::None);
        let Ok(heard) = result else {
            self.wifi = None;
            return self.error(SetupErrorCode::Internal, out);
        };
        match pending {
            Pending::ScanList => {
                let list = scan_list(heard);
                self.reply(Body::WifiScanList(list), out)
            }
            Pending::ConfigScan => self.pick_target(heard, out),
            _ => self.error(SetupErrorCode::Internal, out),
        }
    }

    fn pick_target(&mut self, heard: &[AccessPoint], out: &mut [u8]) -> Action {
        let Some(wifi) = self.wifi.as_mut() else {
            return self.error(SetupErrorCode::Internal, out);
        };
        let strongest = heard
            .iter()
            .filter(|ap| ap.ssid() == wifi.ssid.as_bytes())
            .max_by_key(|ap| ap.rssi);
        let status = match strongest {
            None => WifiStatus::NetworkNotFound,
            Some(ap) if !ap.security.is_supported() => WifiStatus::UnsupportedSecurity,
            Some(ap) => {
                wifi.bssid = ap.bssid;
                wifi.channel = ap.channel;
                self.pending = Pending::Join;
                return Action::Join;
            }
        };
        self.wifi = None;
        self.wifi_result(status, out)
    }

    /// The access point an [`Action::Join`] asks to join.
    #[must_use]
    pub fn join_target(&self) -> Option<JoinTarget<'_>> {
        if self.pending != Pending::Join {
            return None;
        }
        self.wifi.as_ref().map(|wifi| JoinTarget {
            ssid: &wifi.ssid,
            password: &wifi.password,
            bssid: wifi.bssid,
            channel: wifi.channel,
        })
    }

    /// Handles the outcome of the join an [`Action::Join`] asked for.
    pub fn on_join(&mut self, result: Result<(), JoinError>, out: &mut [u8]) -> Action {
        self.pending = Pending::None;
        let status = match result {
            Ok(()) => {
                self.pending = Pending::Persist;
                return Action::Persist;
            }
            Err(JoinError::WrongPassword) => WifiStatus::WrongPassword,
            Err(JoinError::NotFound) => WifiStatus::NetworkNotFound,
            Err(JoinError::Unsupported) => WifiStatus::UnsupportedSecurity,
            Err(JoinError::Failed) => {
                self.wifi = None;
                return self.error(SetupErrorCode::Internal, out);
            }
        };
        self.wifi = None;
        self.wifi_result(status, out)
    }

    /// The record an [`Action::Persist`] asks to store: the joined network and the bound Site.
    #[must_use]
    pub fn provisioning_record(&self) -> Option<ProvisioningRecord> {
        if self.pending != Pending::Persist {
            return None;
        }
        let wifi = self.wifi.as_ref()?;
        let site_id = self.site_id.as_ref()?;
        ProvisioningRecord::new(&wifi.ssid, &wifi.password, site_id).ok()
    }

    /// Handles the outcome of storing the provisioning record: `CONNECTED` when it was stored.
    pub fn on_persisted(&mut self, stored: bool, out: &mut [u8]) -> Action {
        self.pending = Pending::None;
        self.wifi = None;
        if !stored {
            return self.error(SetupErrorCode::Internal, out);
        }
        self.provisioned = true;
        self.wifi_result(WifiStatus::Connected, out)
    }

    fn wifi_result(&mut self, status: WifiStatus, out: &mut [u8]) -> Action {
        self.reply(Body::WifiResult(WifiResult { status }), out)
    }

    fn error(&mut self, code: SetupErrorCode, out: &mut [u8]) -> Action {
        self.reply(Body::Error(SetupError { code }), out)
    }

    /// Seals `body` into a `SealedSetupMessage` frame in `out`.
    fn reply(&mut self, body: Body, out: &mut [u8]) -> Action {
        let Phase::Open(session) = &mut self.phase else {
            return Action::Close;
        };
        let message = SetupMessage {
            protocol_version: PROTOCOL_VERSION,
            body: Some(body),
        };
        let mut plain = Wiped([0u8; SETUP_MESSAGE_MAX_SIZE]);
        let Ok(length) = encode(&message, &mut plain.0) else {
            return Action::Close;
        };
        let mut sealed = SealedSetupMessage {
            protocol_version: PROTOCOL_VERSION,
            counter: 0,
            ciphertext: heapless::Vec::new(),
        };
        if sealed
            .ciphertext
            .resize_default(length + coldframe_crypto::spec::AEAD_TAG_LENGTH)
            .is_err()
        {
            return Action::Close;
        }
        let Ok((counter, _)) = session.seal(&plain.0[..length], &mut sealed.ciphertext) else {
            return Action::Close;
        };
        sealed.counter = counter;
        match encode(&sealed, out) {
            Ok(length) => Action::Send(length),
            Err(_) => Action::Close,
        }
    }
}

fn security(security: Security) -> WifiSecurity {
    match security {
        Security::Open => WifiSecurity::Open,
        Security::Wpa2Personal => WifiSecurity::Wpa2Personal,
        Security::Wpa3Transition => WifiSecurity::Wpa3Transition,
        Security::Wpa3Only => WifiSecurity::Wpa3Only,
        Security::Other => WifiSecurity::Other,
    }
}

/// The scan list: one entry per SSID (its strongest access point), strongest first, at most
/// [`MAX_NETWORKS`]. Hidden (empty) and non-UTF-8 SSIDs are left out.
#[must_use]
pub fn scan_list(heard: &[AccessPoint]) -> WifiScanList {
    let mut list = WifiScanList::default();
    // Strongest first; ties keep scan order.
    let mut order: heapless::Vec<(usize, &AccessPoint), SCAN_CAPACITY> = heapless::Vec::new();
    for entry in heard.iter().take(SCAN_CAPACITY).enumerate() {
        let _ = order.push(entry);
    }
    order.sort_unstable_by_key(|(index, ap)| (core::cmp::Reverse(ap.rssi), *index));
    for (_, ap) in order {
        if list.networks.is_full() {
            break;
        }
        let Ok(ssid) = core::str::from_utf8(ap.ssid()) else {
            continue;
        };
        if ssid.is_empty() || list.networks.iter().any(|network| network.ssid == ssid) {
            continue;
        }
        let Ok(ssid) = ssid.try_into() else {
            continue;
        };
        let _ = list.networks.push(WifiNetwork {
            ssid,
            bssid: heapless::Vec::from_array(ap.bssid),
            rssi: i32::from(ap.rssi),
            security: security(ap.security),
            channel: u32::from(ap.channel),
        });
    }
    list
}

const _: () = assert!(MAX_NETWORKS == 16);
