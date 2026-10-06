//! The Hub's uplink to its Server (Story 3.5), over the `coldframe-hal` traits only (AD-24).
//!
//! - [`bssid`]: [`select_bssid`], the strongest supported access point for an SSID (H-1).
//! - [`backoff`]: [`Backoff`], 1 s doubling to 60 s between failed joins and clock attempts.
//! - [`schedule`]: [`HeartbeatSchedule`], 30–60 s between heartbeats from the TRNG.
//! - [`url`]: [`ServerUrl`], the `https://host[:port]` the app binds the Hub to.
//! - [`time`]: RFC 3339 `…Z` to Unix ms, and [`MonotonicStamp`] for strictly increasing
//!   request timestamps (AD-11).
//! - [`json`]: the hand-written `HeartbeatRequest` and `HeartbeatResponse` of
//!   `packages/openapi`, checked against its golden fixtures (AD-10).
//! - [`sign`]: [`sign_request`] and [`sign_heartbeat`], the `X-Coldframe-*` header values
//!   (AD-12).
//! - [`heartbeat`]: one signed heartbeat, and [`check_server`], the one-shot check the BLE
//!   setup session runs after its join.
//! - [`base64`]: standard padded base64, as the ingest bodies carry sealed frames.
//! - [`relay`]: [`Relay`], the Hub's state between the radio and the Server (Story 4.4, AD-9):
//!   the uplink queue, the latest downlink per Node, and the probe replies.
//! - [`uplink`]: [`Uplink`], which keeps the Hub joined, sets the clock once per boot by SNTP
//!   and heartbeats forever. [`Uplink::step`] does one unit of work; [`Uplink::relay_step`]
//!   posts the queued uplinks to `/device/ingest` and keeps the downlinks; [`Uplink::run`] loops.
//!
//! The crate is `no_std` without `alloc`. No type holding a key or the Wi-Fi password implements
//! `Debug`, and nothing here logs: the caller logs the [`Event`]s, which carry no key, nonce,
//! signature, password or body.

#![cfg_attr(not(test), no_std)]

pub mod backoff;
pub mod base64;
pub mod bssid;
pub mod heartbeat;
pub mod json;
pub mod relay;
pub mod schedule;
pub mod sign;
pub mod time;
mod timeout;
pub mod uplink;
pub mod url;

pub use backoff::Backoff;
pub use bssid::{SelectError, select_bssid};
pub use heartbeat::{CheckError, HeartbeatOutcome, ServerCheck, check_server};
pub use relay::{Heard, Relay, RelayPort};
pub use schedule::HeartbeatSchedule;
pub use sign::{HeartbeatHeaders, sign_heartbeat, sign_request};
pub use time::{MonotonicStamp, parse_rfc3339_ms};
pub use uplink::{Event, IngestOutcome, JoinFailure, Uplink};
pub use url::{ServerUrl, UrlError};

/// The heartbeat operation's method.
pub const HEARTBEAT_METHOD: &str = "POST";

/// The heartbeat operation's path (`packages/openapi`, `deviceHeartbeat`).
pub const HEARTBEAT_PATH: &str = "/device/heartbeat";

/// The ingest operation's method.
pub const INGEST_METHOD: &str = "POST";

/// The ingest operation's path (`packages/openapi`, `deviceIngest`).
pub const INGEST_PATH: &str = "/device/ingest";

/// The most frames the contract allows in one ingest request, and results in its response.
pub const INGEST_FRAMES_MAX: usize = 32;

/// The most frames the Hub puts into one ingest request: its uplink queue.
pub const INGEST_BATCH_MAX: usize = relay::UPLINK_QUEUE;

/// The longest base64 text of one sealed envelope.
pub const FRAME_BASE64_MAX: usize = base64::encoded_len(coldframe_protocol::radio::ENVELOPE_MAX);

/// The largest ingest request body: `{"frames":[` … `]}` around [`INGEST_BATCH_MAX`] quoted
/// frames and the commas between them.
pub const INGEST_REQUEST_MAX: usize = 13 + INGEST_BATCH_MAX * (FRAME_BASE64_MAX + 3);

/// The largest ingest response body read, in bytes: [`INGEST_BATCH_MAX`] results that each
/// carry a downlink, with room for fields this firmware does not know.
pub const INGEST_RESPONSE_MAX: usize = 4_096;

/// The wire major a heartbeat body carries in `protocolVersion`.
pub const HEARTBEAT_PROTOCOL_VERSION: u32 = 1;

/// The shortest wait between two heartbeats (AD-7: every 30–60 s).
pub const HEARTBEAT_MIN_INTERVAL_MS: u32 = 30_000;

/// The longest wait between two heartbeats.
pub const HEARTBEAT_MAX_INTERVAL_MS: u32 = 60_000;

/// The first wait after a failed join or clock attempt; it doubles per failure.
pub const BACKOFF_INITIAL_MS: u32 = 1_000;

/// The longest wait between two failed attempts.
pub const BACKOFF_MAX_MS: u32 = 60_000;

/// The whole budget of the setup session's Server check: IP, clock and one heartbeat.
pub const SERVER_CHECK_TIMEOUT_MS: u32 = 40_000;

/// How long a heartbeat waits for DHCP.
pub const IP_TIMEOUT_MS: u32 = 15_000;

/// How long one SNTP exchange may take.
pub const SNTP_TIMEOUT_MS: u32 = 10_000;

/// The SNTP server the Hub asks once per boot.
pub const SNTP_SERVER: &str = "pool.ntp.org";

/// The largest response body read, in bytes.
pub const RESPONSE_MAX: usize = 512;

/// How many access points one scan keeps (the strongest).
pub const SCAN_CAPACITY: usize = 32;
