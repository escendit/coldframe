//! The Hub's JSON bodies, hand-written for `no_std` (AD-10) and checked against the golden
//! fixtures `packages/openapi` generates from its schemas (`tests/rs/uplink`).
//!
//! - [`HeartbeatRequest`] encodes as `{"protocolVersion":1,"uptimeMs":<n>}`.
//! - [`HeartbeatResponse`] decodes `{"serverTime":"…Z"}` and ignores fields it does not know.
//! - [`encode_ingest_request`] writes `{"frames":["<base64>",…]}`: each frame the bytes of one
//!   sealed envelope in standard padded base64.
//! - [`IngestResponse`] decodes `{"results":[{"status":"…","downlink":"<base64>"},…]}` and
//!   ignores fields it does not know. A status it does not know is [`IngestStatus::Other`].

use serde::{Deserialize, Serialize};

use crate::time::parse_rfc3339_ms;
use crate::{HEARTBEAT_PROTOCOL_VERSION, INGEST_FRAMES_MAX, base64};

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

/// Writes `{"frames":[…]}` with one base64 string per frame into `out`; returns its length.
///
/// # Errors
///
/// [`JsonError::BufferTooSmall`].
pub fn encode_ingest_request<'a>(
    frames: impl IntoIterator<Item = &'a [u8]>,
    out: &mut [u8],
) -> Result<usize, JsonError> {
    fn put(out: &mut [u8], length: &mut usize, text: &[u8]) -> Result<(), JsonError> {
        let target = out
            .get_mut(*length..*length + text.len())
            .ok_or(JsonError::BufferTooSmall)?;
        target.copy_from_slice(text);
        *length += text.len();
        Ok(())
    }
    let mut length = 0;
    put(out, &mut length, b"{\"frames\":[")?;
    for (index, frame) in frames.into_iter().enumerate() {
        put(out, &mut length, if index == 0 { b"\"" } else { b",\"" })?;
        let rest = out.get_mut(length..).ok_or(JsonError::BufferTooSmall)?;
        length += base64::encode(frame, rest).ok_or(JsonError::BufferTooSmall)?;
        put(out, &mut length, b"\"")?;
    }
    put(out, &mut length, b"]}")?;
    Ok(length)
}

/// `IngestFrameStatus` of `packages/openapi`. The Hub only logs it: no status is an
/// acknowledgement, and the Hub never builds one.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum IngestStatus {
    /// New Readings or a new device report were committed.
    Stored,
    /// Everything in the frame was stored before.
    Duplicate,
    /// The frame did not open or is not a valid Node frame.
    RejectedAuth,
    /// The frame's counter was seen or is below the replay window.
    RejectedReplay,
    /// A synced `measured_at` lies too far in the future.
    RejectedTime,
    /// The Device ID is not an enrolled Node.
    UnknownDevice,
    /// The Server could not finish the frame.
    Retry,
    /// A status this firmware does not know.
    Other,
}

impl IngestStatus {
    fn parse(text: &str) -> Self {
        match text {
            "stored" => Self::Stored,
            "duplicate" => Self::Duplicate,
            "rejected_auth" => Self::RejectedAuth,
            "rejected_replay" => Self::RejectedReplay,
            "rejected_time" => Self::RejectedTime,
            "unknown_device" => Self::UnknownDevice,
            "retry" => Self::Retry,
            _ => Self::Other,
        }
    }
}

/// `IngestFrameResult` of `packages/openapi`, as received.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct IngestFrameResult<'a> {
    /// What the Server did with the frame.
    pub status: IngestStatus,
    /// The sealed downlink for the Node, base64, as the JSON text has it: escapes such as
    /// `\u002B` (how the Server writes `+`) are still in it. See [`unescape`].
    pub downlink: Option<&'a str>,
}

/// Resolves the JSON string escapes of `text` into the front of `out`; returns the length.
/// `text` is a JSON string's content without its quotes. `None` for a malformed escape, a
/// `\uXXXX` outside ASCII (base64 has none), or an `out` that is too small.
pub fn unescape(text: &str, out: &mut [u8]) -> Option<usize> {
    let mut bytes = text.bytes();
    let mut length = 0;
    while let Some(byte) = bytes.next() {
        let resolved = if byte == b'\\' {
            match bytes.next()? {
                b'u' => {
                    let mut code = 0u32;
                    for _ in 0..4 {
                        code = code << 4 | char::from(bytes.next()?).to_digit(16)?;
                    }
                    u8::try_from(code).ok().filter(u8::is_ascii)?
                }
                b'b' => 0x08,
                b'f' => 0x0C,
                b'n' => b'\n',
                b'r' => b'\r',
                b't' => b'\t',
                escaped @ (b'/' | b'\\' | b'"') => escaped,
                _ => return None,
            }
        } else {
            byte
        };
        *out.get_mut(length)? = resolved;
        length += 1;
    }
    Some(length)
}

#[derive(Deserialize)]
struct RawResult<'a> {
    status: &'a str,
    #[serde(default)]
    downlink: Option<&'a str>,
}

#[derive(Deserialize)]
struct RawResponse<'a> {
    #[serde(borrow)]
    results: heapless::Vec<RawResult<'a>, INGEST_FRAMES_MAX>,
}

/// `IngestResponse` of `packages/openapi`, as received: one result per frame, in request order.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct IngestResponse<'a> {
    /// The results.
    pub results: heapless::Vec<IngestFrameResult<'a>, INGEST_FRAMES_MAX>,
}

impl<'a> IngestResponse<'a> {
    /// Decodes a response body.
    ///
    /// # Errors
    ///
    /// [`JsonError::Malformed`] when it is not a JSON object with a `results` list of at most
    /// [`INGEST_FRAMES_MAX`] objects that each have a string `status`.
    pub fn decode(body: &'a [u8]) -> Result<Self, JsonError> {
        let (raw, _) = serde_json_core::from_slice::<RawResponse<'a>>(body)
            .map_err(|_| JsonError::Malformed)?;
        let mut results = heapless::Vec::new();
        for result in &raw.results {
            // Both lists have the same capacity.
            let _ = results.push(IngestFrameResult {
                status: IngestStatus::parse(result.status),
                downlink: result.downlink,
            });
        }
        Ok(Self { results })
    }
}
