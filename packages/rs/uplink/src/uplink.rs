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
//! [`Uplink::run`] loops over the steps forever.

use coldframe_crypto::DeviceKeys;
use coldframe_hal::{AccessPoint, JoinError, Net, NetError, Rtc, Timer, Trng, Wifi, WifiError};

use crate::backoff::Backoff;
use crate::bssid::{SelectError, select_bssid};
use crate::heartbeat::{HeartbeatOutcome, send_heartbeat};
use crate::schedule::HeartbeatSchedule;
use crate::time::MonotonicStamp;
use crate::url::ServerUrl;
use crate::{IP_TIMEOUT_MS, SCAN_CAPACITY, SNTP_TIMEOUT_MS};

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

    /// Runs [`Uplink::step`] forever, waiting on `timer` between steps.
    pub async fn run<W, N, R, M, T>(
        &mut self,
        wifi: &mut W,
        net: &mut N,
        rtc: &mut R,
        timer: &mut M,
        trng: &mut T,
        mut on_event: impl FnMut(&Event),
    ) -> !
    where
        W: Wifi,
        N: Net,
        R: Rtc,
        M: Timer,
        T: Trng,
    {
        loop {
            let wait = self.step(wifi, net, rtc, trng, &mut on_event).await;
            if wait > 0 {
                timer.sleep_ms(wait).await;
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
