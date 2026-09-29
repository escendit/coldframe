//! One signed heartbeat (AD-12, FR-13), and the setup session's one-shot Server check.
//!
//! A heartbeat is `POST /device/heartbeat` with `{"protocolVersion":1,"uptimeMs":<n>}`, signed
//! with the Hub's `hub-auth/v1` key over a strictly increasing timestamp and a fresh 16-byte TRNG
//! nonce. Only an HTTP 200 whose body carries a valid `serverTime` counts as accepted; the Hub
//! then sets its clock to that time (AD-11), for its own timing only.

use core::fmt;

use coldframe_crypto::DeviceKeys;
use coldframe_crypto::spec::HEARTBEAT_NONCE_LENGTH;
use coldframe_hal::{HttpRequest, Net, NetError, Rtc, Timer, Trng};

use crate::json::{HeartbeatRequest, server_time_ms};
use crate::sign::sign_heartbeat;
use crate::time::MonotonicStamp;
use crate::timeout::with_timeout;
use crate::url::ServerUrl;
use crate::{HEARTBEAT_PATH, RESPONSE_MAX, SERVER_CHECK_TIMEOUT_MS, SNTP_TIMEOUT_MS};

/// Longest heartbeat body: `{"protocolVersion":4294967295,"uptimeMs":18446744073709551615}`.
const REQUEST_MAX: usize = 64;

/// What became of one heartbeat. Carries no body, nonce or signature.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum HeartbeatOutcome {
    /// HTTP 200 with a valid `serverTime`, which the Hub adopted.
    Accepted {
        /// The Server clock, Unix milliseconds.
        server_time_ms: u64,
    },
    /// Any status other than 200 (401 for an unknown Hub, a bad signature, skew or a replay).
    Rejected {
        /// The HTTP status.
        status: u16,
    },
    /// HTTP 200 whose body is not JSON, lacks `serverTime`, or has an invalid one.
    BadReply,
    /// The request did not complete.
    Failed(NetError),
    /// Not sent: the wall clock has never been set.
    NoClock,
    /// Not sent: the TRNG gave no nonce.
    NoEntropy,
}

impl HeartbeatOutcome {
    /// A short, stable name for logs.
    #[must_use]
    pub const fn kind(&self) -> &'static str {
        match self {
            Self::Accepted { .. } => "ok",
            Self::Rejected { .. } => "rejected",
            Self::BadReply => "bad-reply",
            Self::Failed(error) => error.kind(),
            Self::NoClock => "no-clock",
            Self::NoEntropy => "no-entropy",
        }
    }
}

/// Sends one heartbeat to `server` and adopts the Server's time when it is accepted.
pub(crate) async fn send_heartbeat<N: Net, R: Rtc, T: Trng>(
    net: &mut N,
    rtc: &mut R,
    trng: &mut T,
    keys: &DeviceKeys,
    server: &ServerUrl,
    stamp: &mut MonotonicStamp,
) -> HeartbeatOutcome {
    let Some(now) = rtc.unix_time_millis() else {
        return HeartbeatOutcome::NoClock;
    };
    let mut nonce = [0u8; HEARTBEAT_NONCE_LENGTH];
    if trng.fill(&mut nonce).is_err() {
        return HeartbeatOutcome::NoEntropy;
    }
    let mut body = [0u8; REQUEST_MAX];
    let Ok(length) = HeartbeatRequest::new(rtc.uptime_millis()).encode(&mut body) else {
        return HeartbeatOutcome::NoEntropy;
    };
    let timestamp = stamp.next(now);
    let headers = sign_heartbeat(keys, &body[..length], timestamp, &nonce);
    nonce.fill(0);
    let pairs = headers.pairs();
    let request = HttpRequest {
        host: server.host(),
        port: server.port(),
        path: HEARTBEAT_PATH,
        headers: &pairs,
        body: &body[..length],
    };
    let mut response = [0u8; RESPONSE_MAX];
    let answer = net.post(&request, &mut response).await;
    let outcome = match answer {
        Err(error) => HeartbeatOutcome::Failed(error),
        Ok(answer) if answer.status != 200 => HeartbeatOutcome::Rejected {
            status: answer.status,
        },
        Ok(answer) => match response.get(..answer.body_len).map(server_time_ms) {
            Some(Ok(server_time_ms)) => HeartbeatOutcome::Accepted { server_time_ms },
            _ => HeartbeatOutcome::BadReply,
        },
    };
    response.fill(0);
    if let HeartbeatOutcome::Accepted { server_time_ms } = outcome {
        // The clock follows the Server. A failure leaves the old time, which only shifts the
        // next timestamp; the stamp stays increasing either way.
        let _ = rtc.set_unix_time_millis(server_time_ms);
    }
    outcome
}

/// The setup session's Server check succeeded.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct ServerCheck {
    /// The Server clock from the heartbeat's answer, which the Hub adopted.
    pub server_time_ms: u64,
    /// The timestamp the heartbeat was signed with; later heartbeats go above it.
    pub stamp_ms: u64,
}

/// Why the setup session's Server check failed: the session answers `NO_SERVER`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum CheckError {
    /// No IP, DNS, SNTP or TLS, or the HTTP exchange failed.
    Net(NetError),
    /// The Server answered this status instead of 200 (401: the Hub is not enrolled).
    Status(u16),
    /// The Server answered 200 without a valid `serverTime`.
    BadReply,
    /// The clock could not be set, or the TRNG gave no nonce.
    Internal,
    /// [`SERVER_CHECK_TIMEOUT_MS`] passed.
    Timeout,
}

impl fmt::Display for CheckError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Net(error) => write!(f, "server check: {error}"),
            Self::Status(status) => write!(f, "server check: HTTP {status}"),
            Self::BadReply => f.write_str("server check: bad reply"),
            Self::Internal => f.write_str("server check: internal failure"),
            Self::Timeout => f.write_str("server check: timed out"),
        }
    }
}

impl core::error::Error for CheckError {}

/// The one-shot Server check of the BLE setup session, run right after a successful join: wait
/// for DHCP, set the clock by SNTP, send one signed heartbeat to `server`. Everything together
/// takes at most [`SERVER_CHECK_TIMEOUT_MS`].
///
/// # Errors
///
/// See [`CheckError`]. Nothing is retried: the app retries the whole `WifiConfig`.
pub async fn check_server<N: Net, R: Rtc, M: Timer, T: Trng>(
    net: &mut N,
    rtc: &mut R,
    timer: &mut M,
    trng: &mut T,
    keys: &DeviceKeys,
    server: &ServerUrl,
) -> Result<ServerCheck, CheckError> {
    let started = rtc.uptime_millis();
    let check = async {
        let remaining = |rtc: &R| {
            let spent = rtc.uptime_millis().saturating_sub(started);
            u32::try_from(u64::from(SERVER_CHECK_TIMEOUT_MS).saturating_sub(spent)).unwrap_or(0)
        };
        let budget = remaining(rtc);
        if budget == 0 {
            return Err(CheckError::Timeout);
        }
        net.wait_ip(budget).await.map_err(CheckError::Net)?;
        let budget = remaining(rtc).min(SNTP_TIMEOUT_MS);
        if budget == 0 {
            return Err(CheckError::Timeout);
        }
        let now = net.sntp(budget).await.map_err(CheckError::Net)?;
        rtc.set_unix_time_millis(now)
            .map_err(|_| CheckError::Internal)?;
        if remaining(rtc) == 0 {
            return Err(CheckError::Timeout);
        }
        let mut stamp = MonotonicStamp::new();
        match send_heartbeat(net, rtc, trng, keys, server, &mut stamp).await {
            HeartbeatOutcome::Accepted { server_time_ms } => Ok(ServerCheck {
                server_time_ms,
                stamp_ms: stamp.last(),
            }),
            HeartbeatOutcome::Rejected { status } => Err(CheckError::Status(status)),
            HeartbeatOutcome::BadReply => Err(CheckError::BadReply),
            HeartbeatOutcome::Failed(error) => Err(CheckError::Net(error)),
            HeartbeatOutcome::NoClock | HeartbeatOutcome::NoEntropy => Err(CheckError::Internal),
        }
    };
    with_timeout(timer, SERVER_CHECK_TIMEOUT_MS, check)
        .await
        .unwrap_or(Err(CheckError::Timeout))
}
