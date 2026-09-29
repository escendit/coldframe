//! The app side of a setup session: what the KMP core does over Kable, in Rust, for host tests
//! and the desktop bench client (`tests/rs/setup-client`).
//!
//! 1. [`AppClient::hello`] makes the `SessionHello` frame.
//! 2. [`AppClient::on_hello_reply`] reads `SessionHelloReply` and derives the session keys from
//!    the setup code the user typed.
//! 3. [`AppClient::seal`] frames each request; [`AppClient::open`] reads each reply. A first reply
//!    that does not open is [`AppError::WrongSetupCode`].
//!
//! Frames still need [`crate::framing`] to cross BLE.

use core::fmt;

use coldframe_crypto::Error as CryptoError;
use coldframe_crypto::hpke::{fingerprint, public_key};
use coldframe_crypto::setup::{Role, SetupSession};
use coldframe_crypto::spec::{AEAD_TAG_LENGTH, X25519_KEY_LENGTH};
use coldframe_protocol::setup_v1::SetupMessage_::Body;
use coldframe_protocol::setup_v1::{
    EnrolmentRequest, IdentityRequest, SealedSetupMessage, SessionHello, SessionHelloReply,
    SetupMessage, SiteBinding, WifiConfig, WifiScanRequest,
};
use coldframe_protocol::{PROTOCOL_VERSION, SETUP_MESSAGE_MAX_SIZE, decode, encode};

/// Why an app-side step failed. Carries no key, code or message content.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum AppError {
    /// The Device's first reply did not open: the setup code is wrong.
    WrongSetupCode,
    /// A reply did not authenticate, or its counter did not increase.
    Tampered,
    /// A frame did not decode, or carried another protocol version.
    Malformed,
    /// A request did not fit its field or buffer.
    InvalidRequest,
    /// [`AppClient::seal`] or [`AppClient::open`] before the key exchange.
    NoSession,
    /// The key exchange failed (a small-order key, or an unusable setup code).
    KeyExchange,
}

impl fmt::Display for AppError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::WrongSetupCode => "wrong setup code",
            Self::Tampered => "reply did not authenticate",
            Self::Malformed => "malformed frame",
            Self::InvalidRequest => "invalid request",
            Self::NoSession => "no session",
            Self::KeyExchange => "key exchange failed",
        })
    }
}

impl core::error::Error for AppError {}

/// The app end of one session. Deliberately not `Debug`: it holds the session keys.
pub struct AppClient {
    private_key: [u8; X25519_KEY_LENGTH],
    session: Option<SetupSession>,
}

impl Drop for AppClient {
    fn drop(&mut self) {
        self.private_key.fill(0);
        core::hint::black_box(&mut self.private_key);
    }
}

impl AppClient {
    /// A client with this ephemeral X25519 private key (32 random bytes).
    #[must_use]
    pub fn new(private_key: [u8; X25519_KEY_LENGTH]) -> Self {
        Self {
            private_key,
            session: None,
        }
    }

    /// The app's public key.
    #[must_use]
    pub fn public_key(&self) -> [u8; X25519_KEY_LENGTH] {
        public_key(&self.private_key)
    }

    /// The `SessionHello` frame, written into `out`; returns its length.
    ///
    /// # Errors
    ///
    /// [`AppError::InvalidRequest`] when `out` is too small.
    pub fn hello(&self, out: &mut [u8]) -> Result<usize, AppError> {
        let hello = SessionHello {
            protocol_version: PROTOCOL_VERSION,
            app_public_key: heapless::Vec::from_array(self.public_key()),
        };
        encode(&hello, out).map_err(|_| AppError::InvalidRequest)
    }

    /// Reads the `SessionHelloReply` frame and derives the session keys with `code` (case does
    /// not matter); returns the Device's public key.
    ///
    /// # Errors
    ///
    /// [`AppError::Malformed`] or [`AppError::KeyExchange`].
    pub fn on_hello_reply(
        &mut self,
        frame: &[u8],
        code: &str,
    ) -> Result<[u8; X25519_KEY_LENGTH], AppError> {
        let reply = decode::<SessionHelloReply>(frame).map_err(|_| AppError::Malformed)?;
        if reply.protocol_version != PROTOCOL_VERSION {
            return Err(AppError::Malformed);
        }
        let device_public = <[u8; X25519_KEY_LENGTH]>::try_from(reply.device_public_key.as_slice())
            .map_err(|_| AppError::Malformed)?;
        let session = SetupSession::new(Role::App, &self.private_key, &device_public, code)
            .map_err(|_| AppError::KeyExchange)?;
        self.session = Some(session);
        Ok(device_public)
    }

    /// Seals `body` into a `SealedSetupMessage` frame in `out`; returns its length.
    ///
    /// # Errors
    ///
    /// [`AppError::NoSession`] or [`AppError::InvalidRequest`].
    pub fn seal(&mut self, body: Body, out: &mut [u8]) -> Result<usize, AppError> {
        let session = self.session.as_mut().ok_or(AppError::NoSession)?;
        let message = SetupMessage {
            protocol_version: PROTOCOL_VERSION,
            body: Some(body),
        };
        let mut plain = [0u8; SETUP_MESSAGE_MAX_SIZE];
        let length = encode(&message, &mut plain).map_err(|_| AppError::InvalidRequest)?;
        let mut sealed = SealedSetupMessage {
            protocol_version: PROTOCOL_VERSION,
            counter: 0,
            ciphertext: heapless::Vec::new(),
        };
        sealed
            .ciphertext
            .resize_default(length + AEAD_TAG_LENGTH)
            .map_err(|_| AppError::InvalidRequest)?;
        let sealed_result = session.seal(&plain[..length], &mut sealed.ciphertext);
        plain.fill(0);
        let (counter, _) = sealed_result.map_err(|_| AppError::InvalidRequest)?;
        sealed.counter = counter;
        encode(&sealed, out).map_err(|_| AppError::InvalidRequest)
    }

    /// Seals arbitrary `plaintext` (not necessarily a valid `SetupMessage`) into a frame in `out`,
    /// for negative tests of the Device's parser; returns the frame's length.
    ///
    /// # Errors
    ///
    /// [`AppError::NoSession`] or [`AppError::InvalidRequest`].
    pub fn seal_raw(&mut self, plaintext: &[u8], out: &mut [u8]) -> Result<usize, AppError> {
        let session = self.session.as_mut().ok_or(AppError::NoSession)?;
        let mut sealed = SealedSetupMessage {
            protocol_version: PROTOCOL_VERSION,
            counter: 0,
            ciphertext: heapless::Vec::new(),
        };
        sealed
            .ciphertext
            .resize_default(plaintext.len() + AEAD_TAG_LENGTH)
            .map_err(|_| AppError::InvalidRequest)?;
        let (counter, _) = session
            .seal(plaintext, &mut sealed.ciphertext)
            .map_err(|_| AppError::InvalidRequest)?;
        sealed.counter = counter;
        encode(&sealed, out).map_err(|_| AppError::InvalidRequest)
    }

    /// Opens a `SealedSetupMessage` frame from the Device.
    ///
    /// # Errors
    ///
    /// [`AppError::WrongSetupCode`] for a first reply that does not open, [`AppError::Tampered`]
    /// for a later one or a replay, [`AppError::Malformed`], or [`AppError::NoSession`].
    pub fn open(&mut self, frame: &[u8]) -> Result<SetupMessage, AppError> {
        let session = self.session.as_mut().ok_or(AppError::NoSession)?;
        let sealed = decode::<SealedSetupMessage>(frame).map_err(|_| AppError::Malformed)?;
        if sealed.protocol_version != PROTOCOL_VERSION {
            return Err(AppError::Malformed);
        }
        let mut plain = [0u8; SETUP_MESSAGE_MAX_SIZE];
        let length = session
            .open(sealed.counter, &sealed.ciphertext, &mut plain)
            .map_err(|error| match error {
                CryptoError::WrongSetupCode => AppError::WrongSetupCode,
                CryptoError::InvalidLength => AppError::Malformed,
                _ => AppError::Tampered,
            })?;
        let message = decode::<SetupMessage>(&plain[..length]);
        plain.fill(0);
        let message = message.map_err(|_| AppError::Malformed)?;
        if message.protocol_version != PROTOCOL_VERSION {
            return Err(AppError::Malformed);
        }
        Ok(message)
    }
}

/// `IdentityRequest`.
#[must_use]
pub fn identity_request() -> Body {
    Body::IdentityRequest(IdentityRequest {})
}

/// `WifiScanRequest`.
#[must_use]
pub fn wifi_scan_request() -> Body {
    Body::WifiScanRequest(WifiScanRequest {})
}

/// `SiteBinding` for a Hub: no Lot, and the Server it reports to (`https://host[:port]`, see
/// `coldframe_uplink::ServerUrl`; the Hub checks it, so a test can send an invalid one).
///
/// # Errors
///
/// [`AppError::InvalidRequest`] when `site_id` is longer than 36 bytes or `server_url` longer
/// than 100.
pub fn site_binding(site_id: &str, server_url: &str) -> Result<Body, AppError> {
    let mut binding = SiteBinding {
        site_id: site_id.try_into().map_err(|_| AppError::InvalidRequest)?,
        ..SiteBinding::default()
    };
    binding.set_server_url(
        server_url
            .try_into()
            .map_err(|_| AppError::InvalidRequest)?,
    );
    Ok(Body::SiteBinding(binding))
}

/// `EnrolmentRequest` for `server_public_key`, with its fingerprint computed here unless
/// `fingerprint_override` gives one (a test of the Device's check).
///
/// # Errors
///
/// [`AppError::InvalidRequest`] when the override is longer than 64 bytes.
pub fn enrolment_request(
    server_public_key: &[u8; X25519_KEY_LENGTH],
    fingerprint_override: Option<&str>,
) -> Result<Body, AppError> {
    let computed = fingerprint(server_public_key);
    let text = match fingerprint_override {
        Some(text) => text,
        None => core::str::from_utf8(&computed).map_err(|_| AppError::InvalidRequest)?,
    };
    Ok(Body::EnrolmentRequest(EnrolmentRequest {
        server_public_key: heapless::Vec::from_array(*server_public_key),
        fingerprint: text.try_into().map_err(|_| AppError::InvalidRequest)?,
    }))
}

/// `WifiConfig`.
///
/// # Errors
///
/// [`AppError::InvalidRequest`] when the SSID is over 32 bytes or the password over 64.
pub fn wifi_config(ssid: &str, password: &str) -> Result<Body, AppError> {
    Ok(Body::WifiConfig(WifiConfig {
        ssid: ssid.try_into().map_err(|_| AppError::InvalidRequest)?,
        password: password.try_into().map_err(|_| AppError::InvalidRequest)?,
    }))
}
