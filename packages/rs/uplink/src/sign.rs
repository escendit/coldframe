//! The `X-Coldframe-*` headers of a signed Hub request (AD-12, `packages/crypto-spec`): a
//! heartbeat, or an ingest of relayed frames.

use coldframe_crypto::DeviceKeys;
use coldframe_crypto::heartbeat::{Request, SIGNATURE_LENGTH};
use coldframe_crypto::spec::{
    DEVICE_ID_HEX_LENGTH, HEARTBEAT_DEVICE_HEADER, HEARTBEAT_NONCE_HEADER, HEARTBEAT_NONCE_LENGTH,
    HEARTBEAT_SIGNATURE_HEADER, HEARTBEAT_TIMESTAMP_HEADER,
};

use crate::{HEARTBEAT_METHOD, HEARTBEAT_PATH};

/// Longest decimal `u64`.
const TIMESTAMP_MAX_LENGTH: usize = 20;

/// The header values of one signed request. Deliberately not `Debug`: the nonce and the
/// signature are never logged.
pub struct HeartbeatHeaders {
    device: [u8; DEVICE_ID_HEX_LENGTH],
    timestamp: heapless::String<TIMESTAMP_MAX_LENGTH>,
    nonce: [u8; 2 * HEARTBEAT_NONCE_LENGTH],
    signature: [u8; 2 * SIGNATURE_LENGTH],
}

impl HeartbeatHeaders {
    /// `X-Coldframe-Device`: the Device ID, 16 lowercase hex digits.
    #[must_use]
    pub fn device(&self) -> &str {
        core::str::from_utf8(&self.device).unwrap_or("")
    }

    /// `X-Coldframe-Timestamp`: Unix milliseconds in decimal.
    #[must_use]
    pub fn timestamp(&self) -> &str {
        &self.timestamp
    }

    /// `X-Coldframe-Nonce`: 32 lowercase hex digits.
    #[must_use]
    pub fn nonce(&self) -> &str {
        core::str::from_utf8(&self.nonce).unwrap_or("")
    }

    /// `X-Coldframe-Signature`: 64 lowercase hex digits.
    #[must_use]
    pub fn signature(&self) -> &str {
        core::str::from_utf8(&self.signature).unwrap_or("")
    }

    /// The four headers as name–value pairs, in the order `packages/crypto-spec` lists them.
    #[must_use]
    pub fn pairs(&self) -> [(&'static str, &str); 4] {
        [
            (HEARTBEAT_DEVICE_HEADER, self.device()),
            (HEARTBEAT_TIMESTAMP_HEADER, self.timestamp()),
            (HEARTBEAT_NONCE_HEADER, self.nonce()),
            (HEARTBEAT_SIGNATURE_HEADER, self.signature()),
        ]
    }
}

impl Drop for HeartbeatHeaders {
    fn drop(&mut self) {
        self.nonce.fill(0);
        self.signature.fill(0);
        core::hint::black_box(&mut self.nonce);
        core::hint::black_box(&mut self.signature);
    }
}

/// Signs `method` `path` with `body`, `timestamp_ms` and `nonce` under the Hub's `hub-auth/v1`
/// key; returns the header values.
#[must_use]
pub fn sign_request(
    keys: &DeviceKeys,
    method: &str,
    path: &str,
    body: &[u8],
    timestamp_ms: u64,
    nonce: &[u8; HEARTBEAT_NONCE_LENGTH],
) -> HeartbeatHeaders {
    let request = Request {
        method,
        path,
        body,
        timestamp_ms,
        nonce,
    };
    let signature = request.signature_hex(&keys.hub_auth_key);
    let mut nonce_hex = [0u8; 2 * HEARTBEAT_NONCE_LENGTH];
    // The buffer is exactly twice the nonce, so encoding cannot fail.
    let _ = coldframe_crypto::hex::encode(nonce, &mut nonce_hex);
    let mut timestamp = heapless::String::new();
    // A u64 has at most 20 digits.
    let _ = core::fmt::write(&mut timestamp, format_args!("{timestamp_ms}"));
    HeartbeatHeaders {
        device: keys.device_id.to_hex(),
        timestamp,
        nonce: nonce_hex,
        signature,
    }
}

/// Signs `POST /device/heartbeat` with `body`, `timestamp_ms` and `nonce` under the Hub's
/// `hub-auth/v1` key; returns the header values.
#[must_use]
pub fn sign_heartbeat(
    keys: &DeviceKeys,
    body: &[u8],
    timestamp_ms: u64,
    nonce: &[u8; HEARTBEAT_NONCE_LENGTH],
) -> HeartbeatHeaders {
    sign_request(
        keys,
        HEARTBEAT_METHOD,
        HEARTBEAT_PATH,
        body,
        timestamp_ms,
        nonce,
    )
}
