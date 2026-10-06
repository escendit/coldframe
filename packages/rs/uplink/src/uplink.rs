//! The Hub's uplink loop: stay joined, set the clock once per boot, heartbeat forever.
//!
//! [`Uplink::step`] does one unit of work and returns how long to wait before the next:
//!
//! 1. **Link.** Link loss is detected before each heartbeat and after any network error. While
//!    the station is not associated, the step scans every channel, joins the strongest supported
//!    BSSID for the SSID ([`crate::select_bssid`]) and returns 0 on success. Every failure kind
//!    retries after [`Backoff`]: 1 s, doubling, at most 60 s, reset on a successful join.
//! 2. **Clock** (AD-11), once per boot, before any TLS: DHCP, then SNTP against
//!    [`crate::SNTP_SERVER`]; retried with the same backoff. No heartbeat is sent before it.
//! 3. **Heartbeat** (AD-12, FR-13): one signed `POST /device/heartbeat`. An accepted one sets
//!    the clock to `serverTime`. Whatever the answer, the next one follows in 30–60 s
//!    ([`HeartbeatSchedule`]); a network error with the link down re-joins at once instead.
//!
//! [`Uplink::relay_step`] is the Hub's side of the Node transport (Story 4.4, AD-9). It takes
//! every uplink the radio task queued in the [`Relay`] and sends them as one signed
//! `POST /device/ingest`: each sealed envelope unread, in standard padded base64, in arrival
//! order. The downlink of `results[i]` is kept for the sender of `frames[i]` and goes out over
//! the radio unchanged: several frames of one Node get several downlinks. The Hub stores no Reading, parses no envelope and builds no
//! acknowledgement, and it never posts the same bytes twice: the queue is emptied before the
//! request, and a request that fails (a transport error, any status but 200, a reply that does
//! not match) drops its batch. The Node sends again, freshly sealed. While the station is not
//! associated or the clock is not set, queued uplinks are dropped, nothing is posted, and the
//! relay is told not to answer probes ([`Relay::set_relaying`]).
//!
//! [`Uplink::run`] loops over the steps forever: after each step it relays whatever arrives
//! until the next step is due ([`Uplink::relay_until`]), so an uplink never waits for a
//! heartbeat. A request that is in flight when the next step falls due finishes first, so a
//! heartbeat can be late by up to one ingest request.
//!
//! [`Relay`]: crate::relay::Relay
//! [`Relay::set_relaying`]: crate::relay::Relay::set_relaying

use coldframe_crypto::DeviceKeys;
use coldframe_crypto::spec::HEARTBEAT_NONCE_LENGTH;
use coldframe_hal::{
    AccessPoint, HttpRequest, JoinError, Net, NetError, Rtc, Timer, Trng, Wifi, WifiError,
};
use coldframe_protocol::radio::ENVELOPE_MAX;

use crate::backoff::Backoff;
use crate::bssid::{SelectError, select_bssid};
use crate::heartbeat::{HeartbeatOutcome, send_heartbeat};
use crate::json::{IngestResponse, encode_ingest_request, unescape};
use crate::relay::{Batch, RelayPort};
use crate::schedule::HeartbeatSchedule;
use crate::sign::sign_request;
use crate::time::MonotonicStamp;
use crate::timeout::with_timeout;
use crate::url::ServerUrl;
use crate::{
    FRAME_BASE64_MAX, INGEST_METHOD, INGEST_PATH, INGEST_REQUEST_MAX, INGEST_RESPONSE_MAX,
    IP_TIMEOUT_MS, SCAN_CAPACITY, SNTP_TIMEOUT_MS, base64,
};

/// Why a join attempt failed.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum JoinFailure {
    /// The scan failed.
    Scan(WifiError),
    /// No access point with the SSID was heard.
    NotHeard,
    /// Only unsupported access points have the SSID.
    Unsupported,
    /// The join itself failed.
    Join(JoinError),
}

impl JoinFailure {
    /// A short, stable name for logs.
    #[must_use]
    pub const fn kind(&self) -> &'static str {
        match self {
            Self::Scan(_) => "scan-failed",
            Self::NotHeard => "not-heard",
            Self::Unsupported => "unsupported",
            Self::Join(JoinError::WrongPassword) => "wrong-password",
            Self::Join(JoinError::NotFound) => "not-found",
            Self::Join(JoinError::Unsupported) => "join-unsupported",
            Self::Join(JoinError::Failed) => "join-failed",
        }
    }
}

/// Why the clock was not set.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ClockError {
    /// DHCP or SNTP failed.
    Net(NetError),
    /// The clock hardware refused the time.
    Rtc,
}

impl ClockError {
    /// A short, stable name for logs.
    #[must_use]
    pub const fn kind(&self) -> &'static str {
        match self {
            Self::Net(error) => error.kind(),
            Self::Rtc => "rtc",
        }
    }
}

/// What became of one batch of uplinks. Carries no envelope, nonce or signature.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum IngestOutcome {
    /// HTTP 200 with one result per frame; `downlinks` of them carried a downlink, now kept for
    /// their Nodes.
    Relayed {
        /// How many downlinks came back.
        downlinks: u8,
    },
    /// Any status other than 200 (401, 503): the batch is dropped.
    Rejected {
        /// The HTTP status.
        status: u16,
    },
    /// HTTP 200 whose body is not the ingest response, or has another number of results than
    /// the request had frames: the batch is dropped and no downlink is passed on.
    BadReply,
    /// The request did not complete: the batch is dropped.
    Failed(NetError),
    /// Not sent: the station is not associated. The batch is dropped.
    NotLinked,
    /// Not sent: the clock is not set. The batch is dropped.
    NoClock,
    /// Not sent: the TRNG gave no nonce. The batch is dropped.
    NoEntropy,
}

impl IngestOutcome {
    /// A short, stable name for logs.
    #[must_use]
    pub const fn kind(&self) -> &'static str {
        match self {
            Self::Relayed { .. } => "ok",
            Self::Rejected { .. } => "rejected",
            Self::BadReply => "bad-reply",
            Self::Failed(error) => error.kind(),
            Self::NotLinked => "not-linked",
            Self::NoClock => "no-clock",
            Self::NoEntropy => "no-entropy",
        }
    }
}

/// What one step did, for the caller to log. Carries no key, nonce, signature, password or body.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Event {
    /// The station was found not associated after having been.
    LinkLost,
    /// Joined the access point on `channel` heard at `rssi` dBm.
    Joined {
        /// The channel.
        channel: u8,
        /// The signal strength at the scan, dBm.
        rssi: i8,
    },
    /// A join attempt failed; the next one follows in `retry_ms`.
    JoinFailed {
        /// Why.
        failure: JoinFailure,
        /// The wait before the next attempt.
        retry_ms: u32,
    },
    /// SNTP set the clock.
    ClockSet {
        /// The time set, Unix milliseconds.
        unix_ms: u64,
    },
    /// The clock was not set; the next attempt follows in `retry_ms` (0: re-join first).
    ClockFailed {
        /// Why.
        error: ClockError,
        /// The wait before the next attempt.
        retry_ms: u32,
    },
    /// A heartbeat went out (or could not); the next step follows in `next_ms` (0: re-join).
    Heartbeat {
        /// What became of it.
        outcome: HeartbeatOutcome,
        /// The wait before the next step.
        next_ms: u32,
    },
    /// A batch of `frames` uplinks was relayed, or dropped.
    Ingest {
        /// How many frames the batch had.
        frames: u8,
        /// What became of it.
        outcome: IngestOutcome,
    },
    /// Uplinks arrived while the queue was full and were dropped; the Nodes send them again.
    UplinksLost {
        /// How many.
        dropped: u32,
    },
}

/// The uplink of a provisioned Hub. Deliberately not `Debug`: it holds the Wi-Fi password and
/// the Device keys.
pub struct Uplink<'a> {
    ssid: &'a str,
    password: &'a str,
    server: ServerUrl,
    keys: &'a DeviceKeys,
    join_backoff: Backoff,
    clock_backoff: Backoff,
    schedule: HeartbeatSchedule,
    stamp: MonotonicStamp,
    clock_set: bool,
    linked: bool,
}

impl<'a> Uplink<'a> {
    /// An uplink to `server` over the network `ssid`, signing with `keys`, whose clock has not
    /// been set on this boot.
    #[must_use]
    pub fn new(ssid: &'a str, password: &'a str, server: ServerUrl, keys: &'a DeviceKeys) -> Self {
        Self {
            ssid,
            password,
            server,
            keys,
            join_backoff: Backoff::new(),
            clock_backoff: Backoff::new(),
            schedule: HeartbeatSchedule,
            stamp: MonotonicStamp::new(),
            clock_set: false,
            linked: false,
        }
    }

    /// The same uplink after the setup session's Server check: the clock is already set on this
    /// boot, and the next heartbeat's timestamp goes above `last_stamp_ms`.
    #[must_use]
    pub fn after_setup(mut self, last_stamp_ms: u64) -> Self {
        self.clock_set = true;
        self.stamp = MonotonicStamp::after(last_stamp_ms);
        self
    }

    /// Whether the clock has been set on this boot.
    #[must_use]
    pub const fn clock_set(&self) -> bool {
        self.clock_set
    }

    /// Does one unit of work, reports what it did through `on_event`, and returns how long to
    /// wait before the next step, in milliseconds.
    pub async fn step<W, N, R, T>(
        &mut self,
        wifi: &mut W,
        net: &mut N,
        rtc: &mut R,
        trng: &mut T,
        on_event: &mut impl FnMut(&Event),
    ) -> u32
    where
        W: Wifi,
        N: Net,
        R: Rtc,
        T: Trng,
    {
        if !wifi.is_connected() {
            if self.linked {
                self.linked = false;
                on_event(&Event::LinkLost);
            }
            return match self.join(wifi).await {
                Ok(joined) => {
                    self.join_backoff.reset();
                    self.linked = true;
                    on_event(&joined);
                    0
                }
                Err(failure) => {
                    let retry_ms = self.join_backoff.fail();
                    on_event(&Event::JoinFailed { failure, retry_ms });
                    retry_ms
                }
            };
        }
        self.linked = true;

        if !self.clock_set {
            let result = match net.wait_ip(IP_TIMEOUT_MS).await {
                Ok(()) => net.sntp(SNTP_TIMEOUT_MS).await.map_err(ClockError::Net),
                Err(error) => Err(ClockError::Net(error)),
            };
            let result = result.and_then(|unix_ms| {
                rtc.set_unix_time_millis(unix_ms)
                    .map(|()| unix_ms)
                    .map_err(|_| ClockError::Rtc)
            });
            return match result {
                Ok(unix_ms) => {
                    self.clock_set = true;
                    self.clock_backoff.reset();
                    on_event(&Event::ClockSet { unix_ms });
                    0
                }
                Err(error) => {
                    let retry_ms = if wifi.is_connected() {
                        self.clock_backoff.fail()
                    } else {
                        0
                    };
                    on_event(&Event::ClockFailed { error, retry_ms });
                    retry_ms
                }
            };
        }

        let outcome = match net.wait_ip(IP_TIMEOUT_MS).await {
            Ok(()) => {
                send_heartbeat(net, rtc, trng, self.keys, &self.server, &mut self.stamp).await
            }
            Err(error) => HeartbeatOutcome::Failed(error),
        };
        let next_ms = if matches!(outcome, HeartbeatOutcome::Failed(_)) && !wifi.is_connected() {
            0
        } else {
            self.schedule.next_delay_ms(trng)
        };
        on_event(&Event::Heartbeat { outcome, next_ms });
        next_ms
    }

    /// Relays the uplinks queued in the relay of `port`: one signed `POST /device/ingest`, the
    /// downlinks kept for their Nodes. Returns whether there was a batch.
    ///
    /// The relay is never held across an `await`: the radio task answers probes and queues
    /// uplinks while the request is in flight.
    pub async fn relay_step<W, N, R, T, P>(
        &mut self,
        wifi: &W,
        net: &mut N,
        rtc: &R,
        trng: &mut T,
        port: &P,
        on_event: &mut impl FnMut(&Event),
    ) -> bool
    where
        W: Wifi,
        N: Net,
        R: Rtc,
        T: Trng,
        P: RelayPort,
    {
        // The Hub can relay while it is associated and has a clock: only then are probes answered.
        let relaying = wifi.is_connected() && self.clock_set && rtc.unix_time_millis().is_some();
        let (batch, dropped) = port.with(|relay| {
            relay.set_relaying(relaying);
            relay.take_batch()
        });
        if dropped > 0 {
            on_event(&Event::UplinksLost { dropped });
        }
        if batch.is_empty() {
            return false;
        }
        let outcome = self.ingest(wifi, net, rtc, trng, port, &batch).await;
        on_event(&Event::Ingest {
            // At most eight frames.
            frames: batch.len() as u8,
            outcome,
        });
        true
    }

    /// Posts one batch and keeps the downlinks that come back.
    async fn ingest<W, N, R, T, P>(
        &mut self,
        wifi: &W,
        net: &mut N,
        rtc: &R,
        trng: &mut T,
        port: &P,
        batch: &Batch,
    ) -> IngestOutcome
    where
        W: Wifi,
        N: Net,
        R: Rtc,
        T: Trng,
        P: RelayPort,
    {
        if !wifi.is_connected() {
            return IngestOutcome::NotLinked;
        }
        if !self.clock_set {
            return IngestOutcome::NoClock;
        }
        let Some(now) = rtc.unix_time_millis() else {
            return IngestOutcome::NoClock;
        };
        if let Err(error) = net.wait_ip(IP_TIMEOUT_MS).await {
            return IngestOutcome::Failed(error);
        }
        let mut nonce = [0u8; HEARTBEAT_NONCE_LENGTH];
        if trng.fill(&mut nonce).is_err() {
            return IngestOutcome::NoEntropy;
        }
        let mut body = [0u8; INGEST_REQUEST_MAX];
        let frames = batch.iter().map(|queued| queued.envelope.as_bytes());
        let Ok(length) = encode_ingest_request(frames, &mut body) else {
            return IngestOutcome::BadReply;
        };
        let headers = sign_request(
            self.keys,
            INGEST_METHOD,
            INGEST_PATH,
            &body[..length],
            self.stamp.next(now),
            &nonce,
        );
        nonce.fill(0);
        let pairs = headers.pairs();
        let request = HttpRequest {
            host: self.server.host(),
            port: self.server.port(),
            path: INGEST_PATH,
            headers: &pairs,
            body: &body[..length],
        };
        let mut response = [0u8; INGEST_RESPONSE_MAX];
        let answer = match net.post(&request, &mut response).await {
            Ok(answer) => answer,
            Err(error) => return IngestOutcome::Failed(error),
        };
        if answer.status != 200 {
            return IngestOutcome::Rejected {
                status: answer.status,
            };
        }
        let Some(Ok(decoded)) = response.get(..answer.body_len).map(IngestResponse::decode) else {
            return IngestOutcome::BadReply;
        };
        // results[i] belongs to frames[i]: a reply of another length cannot be matched.
        if decoded.results.len() != batch.len() {
            return IngestOutcome::BadReply;
        }
        let mut downlinks = 0u8;
        for (queued, result) in batch.iter().zip(&decoded.results) {
            let Some(text) = result.downlink else {
                continue;
            };
            // The downlink is passed on as it came: its JSON escapes resolved (the Server writes
            // `+` as `\u002B`), decoded from base64, never opened.
            let mut encoded = [0u8; FRAME_BASE64_MAX];
            let mut envelope = [0u8; ENVELOPE_MAX];
            let Some(length) = unescape(text, &mut encoded)
                .and_then(|length| base64::decode(&encoded[..length], &mut envelope))
            else {
                continue;
            };
            if port.with(|relay| relay.keep(&queued.source, &envelope[..length])) {
                downlinks += 1;
            }
        }
        IngestOutcome::Relayed { downlinks }
    }

    /// Runs [`Uplink::step`] forever. Between two steps it relays the uplinks of `port` as they
    /// arrive ([`Uplink::relay_step`]), waiting on `timer` for the next step.
    #[allow(clippy::too_many_arguments, reason = "the HAL of the Hub")]
    pub async fn run<W, N, R, M, T, P>(
        &mut self,
        wifi: &mut W,
        net: &mut N,
        rtc: &mut R,
        timer: &mut M,
        trng: &mut T,
        port: &P,
        mut on_event: impl FnMut(&Event),
    ) -> !
    where
        W: Wifi,
        N: Net,
        R: Rtc,
        M: Timer,
        T: Trng,
        P: RelayPort,
    {
        loop {
            let wait = self.step(wifi, net, rtc, trng, &mut on_event).await;
            self.relay_until(wifi, net, rtc, timer, trng, port, wait, &mut on_event)
                .await;
        }
    }

    /// Relays for `wait_ms`: whatever is queued now, then each batch as it arrives
    /// ([`RelayPort::uplink_queued`]), and returns when `wait_ms` have passed since the call. A
    /// request in flight at that moment finishes first.
    #[allow(clippy::too_many_arguments, reason = "the HAL of the Hub")]
    pub async fn relay_until<W, N, R, M, T, P>(
        &mut self,
        wifi: &W,
        net: &mut N,
        rtc: &R,
        timer: &mut M,
        trng: &mut T,
        port: &P,
        wait_ms: u32,
        on_event: &mut impl FnMut(&Event),
    ) where
        W: Wifi,
        N: Net,
        R: Rtc,
        M: Timer,
        T: Trng,
        P: RelayPort,
    {
        let started = rtc.uptime_millis();
        loop {
            self.relay_step(wifi, net, rtc, trng, port, on_event).await;
            let spent = rtc.uptime_millis().saturating_sub(started);
            let left = u32::try_from(u64::from(wait_ms).saturating_sub(spent)).unwrap_or(0);
            if left == 0
                || with_timeout(timer, left, port.uplink_queued())
                    .await
                    .is_none()
            {
                return;
            }
        }
    }

    /// Scans, picks the strongest supported BSSID for the SSID and joins it.
    async fn join<W: Wifi>(&mut self, wifi: &mut W) -> Result<Event, JoinFailure> {
        let mut heard = [AccessPoint::EMPTY; SCAN_CAPACITY];
        let count = wifi.scan(&mut heard).await.map_err(JoinFailure::Scan)?;
        let heard = &heard[..count.min(SCAN_CAPACITY)];
        let target = select_bssid(heard, self.ssid.as_bytes()).map_err(|error| match error {
            SelectError::NotHeard => JoinFailure::NotHeard,
            SelectError::Unsupported => JoinFailure::Unsupported,
        })?;
        wifi.join(self.ssid, self.password, target.bssid, target.channel)
            .await
            .map_err(JoinFailure::Join)?;
        Ok(Event::Joined {
            channel: target.channel,
            rssi: target.rssi,
        })
    }
}
