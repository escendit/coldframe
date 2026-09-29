//! The heartbeat JSON bodies, hand-written for `no_std` (AD-10) and checked against the golden
//! fixtures `packages/openapi` generates from its schemas (`tests/rs/uplink`).
//!
//! - [`HeartbeatRequest`] encodes as `{"protocolVersion":1,"uptimeMs":<n>}`.
//! - [`HeartbeatResponse`] decodes `{"serverTime":"…Z"}` and ignores fields it does not know.

use serde::{Deserialize, Serialize};

use crate::HEARTBEAT_PROTOCOL_VERSION;
use crate::time::parse_rfc3339_ms;

/// Why a body could not be encoded or decoded. Carries none of the body.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum JsonError {
    /// The output buffer is too small.
    BufferTooSmall,
    /// Not the expected JSON.
    Malformed,
    /// `serverTime` is not an RFC 3339 UTC time.
    BadTime,
}

impl core::fmt::Display for JsonError {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        f.write_str(match self {
            Self::BufferTooSmall => "JSON buffer too small",
            Self::Malformed => "malformed JSON",
            Self::BadTime => "serverTime is not an RFC 3339 UTC time",
        })
    }
}

impl core::error::Error for JsonError {}

/// `HeartbeatRequest` of `packages/openapi`.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct HeartbeatRequest {
    /// The wire major, [`HEARTBEAT_PROTOCOL_VERSION`].
    pub protocol_version: u32,
    /// Milliseconds since the Hub booted. Optional in the schema; the Hub always sends it.
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub uptime_ms: Option<u64>,
}

impl HeartbeatRequest {
    /// The Hub's heartbeat at `uptime_ms`.
    #[must_use]
    pub const fn new(uptime_ms: u64) -> Self {
        Self {
            protocol_version: HEARTBEAT_PROTOCOL_VERSION,
            uptime_ms: Some(uptime_ms),
        }
    }

    /// Writes the JSON into `out`; returns its length.
    ///
    /// # Errors
    ///
    /// [`JsonError::BufferTooSmall`].
    pub fn encode(&self, out: &mut [u8]) -> Result<usize, JsonError> {
        serde_json_core::to_slice(self, out).map_err(|_| JsonError::BufferTooSmall)
    }
}

/// `HeartbeatResponse` of `packages/openapi`, as received.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct HeartbeatResponse<'a> {
    /// The Server clock, RFC 3339 UTC with `Z`.
    pub server_time: &'a str,
}

impl<'a> HeartbeatResponse<'a> {
    /// Decodes a response body.
    ///
    /// # Errors
    ///
    /// [`JsonError::Malformed`] when it is not a JSON object with a string `serverTime`.
    pub fn decode(body: &'a [u8]) -> Result<Self, JsonError> {
        let (response, _) =
            serde_json_core::from_slice::<Self>(body).map_err(|_| JsonError::Malformed)?;
        Ok(response)
    }

    /// `serverTime` as Unix milliseconds.
    ///
    /// # Errors
    ///
    /// [`JsonError::BadTime`].
    pub fn server_time_ms(&self) -> Result<u64, JsonError> {
        parse_rfc3339_ms(self.server_time).map_err(|_| JsonError::BadTime)
    }
}

/// Decodes a response body straight to its `serverTime` in Unix milliseconds.
///
/// # Errors
///
/// [`JsonError::Malformed`] or [`JsonError::BadTime`].
pub fn server_time_ms(body: &[u8]) -> Result<u64, JsonError> {
    HeartbeatResponse::decode(body)?.server_time_ms()
}
