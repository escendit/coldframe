//! Hub request authentication (AD-12): an HMAC-SHA256 with the `hub-auth/v1` key over
//! `METHOD\nPATH\nhex(SHA-256(body))\nTIMESTAMP_MS\nNONCE_HEX`, sent as lowercase hex in the
//! `X-Coldframe-Signature` header next to `X-Coldframe-Device`, `X-Coldframe-Timestamp` and
//! `X-Coldframe-Nonce`.

use core::fmt::{self, Write};

use hmac::{Hmac, KeyInit, Mac};
use sha2::{Digest, Sha256};

use crate::spec::{HEARTBEAT_NONCE_LENGTH, HEARTBEAT_SEPARATOR, PURPOSE_KEY_LENGTH};
use crate::{Error, hex};

/// Length of the signature in bytes.
pub const SIGNATURE_LENGTH: usize = 32;

/// The parts of a signed request.
#[derive(Clone, Copy, Debug)]
pub struct Request<'a> {
    /// The HTTP method, such as `POST`.
    pub method: &'a str,
    /// The request path, such as `/device/heartbeat`.
    pub path: &'a str,
    /// The exact body bytes sent.
    pub body: &'a [u8],
    /// The request time, Unix milliseconds.
    pub timestamp_ms: u64,
    /// A fresh random nonce.
    pub nonce: &'a [u8; HEARTBEAT_NONCE_LENGTH],
}

impl Request<'_> {
    /// Writes the canonical string.
    pub fn write_canonical<W: Write>(&self, out: &mut W) -> fmt::Result {
        let body_hash: [u8; 32] = Sha256::digest(self.body).into();
        let body_hex: [u8; 64] = hex::encode_array(&body_hash);
        let nonce_hex: [u8; 2 * HEARTBEAT_NONCE_LENGTH] = hex::encode_array(self.nonce);
        out.write_str(self.method)?;
        out.write_str(HEARTBEAT_SEPARATOR)?;
        out.write_str(self.path)?;
        out.write_str(HEARTBEAT_SEPARATOR)?;
        out.write_str(core::str::from_utf8(&body_hex).map_err(|_| fmt::Error)?)?;
        out.write_str(HEARTBEAT_SEPARATOR)?;
        write!(out, "{}", self.timestamp_ms)?;
        out.write_str(HEARTBEAT_SEPARATOR)?;
        out.write_str(core::str::from_utf8(&nonce_hex).map_err(|_| fmt::Error)?)
    }

    fn mac(&self, hub_auth_key: &[u8; PURPOSE_KEY_LENGTH]) -> Hmac<Sha256> {
        let mut mac = MacWriter(
            <Hmac<Sha256> as KeyInit>::new_from_slice(hub_auth_key)
                .expect("HMAC accepts a key of any length"),
        );
        self.write_canonical(&mut mac)
            .expect("writing to a MAC never fails");
        mac.0
    }

    /// The signature with the `hub-auth/v1` key.
    #[must_use]
    pub fn sign(&self, hub_auth_key: &[u8; PURPOSE_KEY_LENGTH]) -> [u8; SIGNATURE_LENGTH] {
        self.mac(hub_auth_key).finalize().into_bytes().into()
    }

    /// The signature as the header carries it: 64 lowercase hex digits.
    #[must_use]
    pub fn signature_hex(
        &self,
        hub_auth_key: &[u8; PURPOSE_KEY_LENGTH],
    ) -> [u8; 2 * SIGNATURE_LENGTH] {
        hex::encode_array(&self.sign(hub_auth_key))
    }

    /// Checks a raw signature in constant time.
    pub fn verify(
        &self,
        hub_auth_key: &[u8; PURPOSE_KEY_LENGTH],
        signature: &[u8],
    ) -> Result<(), Error> {
        self.mac(hub_auth_key)
            .verify_slice(signature)
            .map_err(|_| Error::AuthenticationFailed)
    }
}

/// Feeds formatted text straight into the MAC, so the canonical string needs no buffer.
struct MacWriter(Hmac<Sha256>);

impl Write for MacWriter {
    fn write_str(&mut self, text: &str) -> fmt::Result {
        self.0.update(text.as_bytes());
        Ok(())
    }
}
