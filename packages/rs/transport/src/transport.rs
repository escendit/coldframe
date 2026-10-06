//! The transport step of one wake (AD-9, AD-11, AD-17; requirements N-1 and N-2).
//!
//! [`begin`] runs before the measurement: it reads the link state and gives the clock back the
//! time a downlink set on this boot, so the wake's Readings are stamped with it. [`run`] runs
//! after the measurement, with the radio on:
//!
//! 1. **Buffer** the wake report ([`ReportBuffer::append`]).
//! 2. **Find a Hub.** Probe the known channel and wait [`PROBE_WAIT_MS`] for the reply. Without a
//!    known channel, or after [`RESCAN_AFTER_MISSES`] consecutive misses, probe the known channel
//!    and then channels 1 to 13, and use the first channel where a Hub answers. A scan that finds
//!    no Hub is not repeated before [`SCAN_BACKOFF_WAKES`] wakes have passed. No reply: nothing
//!    is sealed or sent, and the wake is a miss.
//! 3. **Late acknowledgements.** The reply's `pending` says how many downlinks the Hub kept
//!    from earlier wakes. The Node listens until it has applied that many, or for
//!    [`PENDING_WAIT_MS`].
//! 4. **Send.** Reserve one frame counter per report in flash, then seal and send up to
//!    [`BURST_MAX`] buffered reports, oldest first, each under its own fresh counter. A counter
//!    fault seals nothing; the reports stay buffered.
//! 5. **Listen** until [`ACK_WINDOW_MS`] after the last send, or until every frame of the burst
//!    is acknowledged.
//! 6. **Remember.** A wake without a *fresh* downlink is a miss. The link state is written back
//!    when it changed.
//!
//! A downlink is applied only when it opens under the `ack/v1` key with this Node's Device ID and
//! protocol version 1 ([`open_downlink`]). It then deletes exactly the Readings and reports in
//! its `acked_readings` ranges. It is *fresh* when its `acked_counter` lies in the burst sent
//! this wake or in the previous one, and no downlink has acknowledged that frame before (the Hub
//! sends a downlink when it arrives and offers it once more at the next probe, so the same one
//! can arrive twice). Only a fresh
//! downlink resets the miss count, sets the clock and decides whether the Specification set is
//! sent. An authentic but older or repeated downlink can only delete what the Server has already
//! stored. What the radio reports for a send is never read.
//!
//! The clock (AD-11): the first fresh downlink of a boot sets it to `server_time_ms` plus the
//! time since the burst it acknowledges was sent. On a later wake one moves it forward in full
//! and back by at most [`CLOCK_MAX_BACK_STEP_MS`]. A wake corrects the clock once, from its
//! first fresh downlink that can, however many arrive: a burst's worth of kept downlinks moves
//! it back by one step, not by one each. A burst of another boot ID has no usable uptime, so its
//! downlink leaves the clock alone.
//!
//! The flash partitions are used one at a time through [`Partitions`], because the Node's flash
//! adapter lends one partition at a time.

use coldframe_crypto::DeviceKeys;
use coldframe_hal::{BROADCAST, DATAGRAM_MAX, DatagramRadio, Flash, MacAddress, Rtc, Trng};
use coldframe_protocol::device_v1::Downlink;
use coldframe_protocol::radio::{Message, NONCE_LENGTH, PROBE_LENGTH};
use coldframe_sensing::ReservedCounter;
use coldframe_sensing::counter::CounterError;

use crate::buffer::{BufferError, ReportBuffer};
use crate::frame::{
    SealError, node_frame, open_downlink, seal_uplink, spec_hash, specification_set,
};
use crate::link::{self, Burst, ClockSync, LinkError, LinkState};
use crate::report::Report;

/// How long the Node listens after the last send of a wake (N-2).
pub const ACK_WINDOW_MS: u32 = 300;

/// How long the Node waits for a probe reply on one channel.
pub const PROBE_WAIT_MS: u32 = 120;

/// How long the Node waits for the downlinks a probe reply announced.
pub const PENDING_WAIT_MS: u32 = 120;

/// After this many consecutive misses the Node scans every channel.
pub const RESCAN_AFTER_MISSES: u8 = 3;

/// After a scan that found no Hub, the next scan is no sooner than this many wakes later.
pub const SCAN_BACKOFF_WAKES: u8 = 4;

/// The most frames one wake sends.
pub const BURST_MAX: usize = 8;

/// The first Wi-Fi channel a scan probes.
pub const FIRST_CHANNEL: u8 = 1;

/// The last Wi-Fi channel a scan probes.
pub const LAST_CHANNEL: u8 = 13;

/// The most one downlink moves a set clock back.
pub const CLOCK_MAX_BACK_STEP_MS: u64 = 1_000;

/// Magic of the frame counter (`cf_frame`).
pub const FRAME_MAGIC: [u8; 4] = *b"CFFC";

/// A flash partition of the transport.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Partition {
    /// The frame counter: a `ReservedCounter` under [`FRAME_MAGIC`], two sectors.
    Frame,
    /// The report buffer, five to eight sectors.
    Buffer,
    /// The link state, two sectors.
    Link,
}

impl Partition {
    /// A short, stable name for logs.
    #[must_use]
    pub const fn name(self) -> &'static str {
        match self {
            Self::Frame => "frame",
            Self::Buffer => "buffer",
            Self::Link => "link",
        }
    }
}

/// The transport's flash partitions, lent one at a time.
pub trait Partitions {
    /// One open partition.
    type Flash<'a>: Flash
    where
        Self: 'a;

    /// Opens `partition`, or `None` when it cannot be opened.
    fn open(&mut self, partition: Partition) -> Option<Self::Flash<'_>>;
}

/// What became of the wake report.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum Buffered {
    /// The wake had no report to buffer.
    #[default]
    NoReport,
    /// Buffered; `dropped` older reports made room (the buffer was full).
    Stored {
        /// How many reports were dropped, oldest first.
        dropped: u32,
    },
    /// Not buffered: the report is lost.
    Failed,
}

/// How the search for a Hub went.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum Hub {
    /// The radio was not used.
    #[default]
    NotTried,
    /// A Hub answered on `channel`.
    Found {
        /// The channel.
        channel: u8,
        /// Whether every channel was probed to find it.
        scanned: bool,
    },
    /// No Hub answered.
    NotFound {
        /// Whether every channel was probed.
        scanned: bool,
    },
}

/// What a wake did to the clock.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum ClockChange {
    /// Nothing.
    #[default]
    Unchanged,
    /// The first sync of this boot.
    Set,
    /// Moved forward, or not at all.
    Forward,
    /// Moved back, by at most [`CLOCK_MAX_BACK_STEP_MS`].
    Back,
}

/// What failed during a wake's transport, for the log. Carries no payload.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct Faults {
    /// A partition could not be opened.
    pub partition: Option<Partition>,
    /// The report buffer failed.
    pub buffer: Option<BufferError>,
    /// The frame counter failed: nothing was sealed.
    pub counter: Option<CounterError>,
    /// The link state could not be read or written.
    pub link: Option<LinkError>,
    /// A frame could not be sealed.
    pub seal: Option<SealError>,
}

impl Faults {
    /// Whether nothing failed.
    #[must_use]
    pub fn is_empty(&self) -> bool {
        *self == Self::default()
    }
}

/// What the transport step of one wake did, for the caller to log. Carries no Reading, key or
/// sealed payload.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct Outcome {
    /// What became of the wake report.
    pub buffered: Buffered,
    /// How the search for a Hub went.
    pub hub: Hub,
    /// How many kept downlinks the Hub announced.
    pub pending: u8,
    /// Frames sent.
    pub sent: u8,
    /// Authentic downlinks applied.
    pub downlinks: u8,
    /// Readings and reports deleted by them.
    pub deleted: u32,
    /// Whether a fresh downlink arrived: otherwise the wake is a miss.
    pub fresh: bool,
    /// Consecutive misses after this wake.
    pub misses: u8,
    /// What happened to the clock.
    pub clock: ClockChange,
    /// Whether a frame carried the Specification set.
    pub specifications_sent: bool,
    /// Reports still buffered.
    pub backlog: usize,
    /// What failed.
    pub faults: Faults,
}

/// Reads the link state and, when a downlink set the clock on this boot, sets the wall clock
/// from it. Call before the measurement, so its Readings are stamped `Synced`.
///
/// A link state that cannot be read counts as the default one: the fault is returned next to it.
pub fn begin<P: Partitions, R: Rtc>(
    parts: &mut P,
    rtc: &mut R,
    boot_id: u64,
) -> (LinkState, Option<LinkError>) {
    let Some(mut flash) = parts.open(Partition::Link) else {
        return (LinkState::default(), None);
    };
    let (state, fault) = match link::load(&mut flash) {
        Ok(state) => (state, None),
        Err(error) => (LinkState::default(), Some(error)),
    };
    if let Some(clock) = state.clock
        && clock.boot_id == boot_id
    {
        // A clock that refuses the time leaves the wake unsynced, which is safe.
        let _ = rtc.set_unix_time_millis(clock.offset_ms.saturating_add(rtc.uptime_millis()));
    }
    (state, fault)
}

/// Buffers the wake report without touching the radio: what a wake that entered setup mode does.
pub fn buffer_only<P: Partitions>(parts: &mut P, report: Option<&Report>) -> Outcome {
    let mut outcome = Outcome::default();
    store(parts, report, &mut outcome);
    outcome
}

/// Opens the buffer and appends the wake report.
fn store<P: Partitions>(
    parts: &mut P,
    report: Option<&Report>,
    outcome: &mut Outcome,
) -> Option<ReportBuffer> {
    let failed = |outcome: &mut Outcome| {
        if report.is_some() {
            outcome.buffered = Buffered::Failed;
        }
    };
    let Some(mut flash) = parts.open(Partition::Buffer) else {
        outcome.faults.partition = Some(Partition::Buffer);
        failed(outcome);
        return None;
    };
    let mut buffer = match ReportBuffer::open(&mut flash) {
        Ok(buffer) => buffer,
        Err(error) => {
            outcome.faults.buffer = Some(error);
            failed(outcome);
            return None;
        }
    };
    if let Some(report) = report {
        match buffer.append(&mut flash, report) {
            Ok(dropped) => outcome.buffered = Buffered::Stored { dropped },
            Err(error) => {
                outcome.faults.buffer = Some(error);
                outcome.buffered = Buffered::Failed;
            }
        }
    }
    outcome.backlog = buffer.len();
    Some(buffer)
}

/// The Hub that answered a probe.
#[derive(Clone, Copy)]
struct Found {
    mac: MacAddress,
    channel: u8,
    pending: u8,
}

/// The state of one wake's exchange.
struct Session<'a> {
    keys: &'a DeviceKeys,
    boot_id: u64,
    link: &'a mut LinkState,
    /// The burst the last wake sent.
    previous: Option<Burst>,
    /// The burst this wake sent.
    current: Option<Burst>,
    /// The clock was corrected this wake: once is all a wake does.
    clock_corrected: bool,
    outcome: &'a mut Outcome,
}

impl Session<'_> {
    /// Applies one received datagram when it is an authentic downlink for this Node; anything
    /// else is dropped silently. Returns whether a downlink was applied.
    fn receive<F: Flash, R: Rtc>(
        &mut self,
        buffer: &mut ReportBuffer,
        flash: &mut F,
        rtc: &mut R,
        datagram: &[u8],
    ) -> bool {
        let Ok(Message::Downlink(envelope)) = Message::decode(datagram) else {
            return false;
        };
        let Some(downlink) = open_downlink(self.keys, envelope) else {
            return false;
        };
        self.apply(buffer, flash, rtc, &downlink);
        true
    }

    fn apply<F: Flash, R: Rtc>(
        &mut self,
        buffer: &mut ReportBuffer,
        flash: &mut F,
        rtc: &mut R,
        downlink: &Downlink,
    ) {
        self.outcome.downlinks = self.outcome.downlinks.saturating_add(1);
        for range in &downlink.acked_readings {
            if range.first > range.last {
                continue;
            }
            match buffer.ack(flash, range.first, range.last) {
                Ok(deleted) => self.outcome.deleted += deleted,
                Err(error) => {
                    self.outcome.faults.buffer.get_or_insert(error);
                }
            }
        }

        let counter = downlink.acked_counter;
        let fresh = |burst: &mut Option<Burst>| {
            burst
                .as_mut()
                .is_some_and(|burst| burst.acknowledge(counter))
                .then_some(*burst)
                .flatten()
        };
        // Authentic but old, or heard before: it deleted what the Server stored, and changes
        // nothing else.
        let Some(burst) = fresh(&mut self.current).or_else(|| fresh(&mut self.previous)) else {
            return;
        };
        self.outcome.fresh = true;
        self.link.specifications_requested = downlink.specifications_unknown;
        self.sync_clock(rtc, downlink.server_time_ms, &burst);
    }

    /// The clock rule (AD-11).
    fn sync_clock<R: Rtc>(&mut self, rtc: &mut R, server_time_ms: i64, burst: &Burst) {
        if self.clock_corrected {
            return;
        }
        if burst.boot_id != self.boot_id {
            // The burst's uptime belongs to another power-on.
            return;
        }
        let Ok(server_time_ms) = u64::try_from(server_time_ms) else {
            return;
        };
        let since_send = rtc.uptime_millis().saturating_sub(burst.uptime_ms);
        let target = server_time_ms.saturating_add(since_send);
        let synced = self
            .link
            .clock
            .is_some_and(|clock| clock.boot_id == self.boot_id);
        let (time, change) = match rtc.unix_time_millis() {
            Some(current) if synced && target < current => (
                target.max(current.saturating_sub(CLOCK_MAX_BACK_STEP_MS)),
                ClockChange::Back,
            ),
            Some(_) if synced => (target, ClockChange::Forward),
            _ => (target, ClockChange::Set),
        };
        if rtc.set_unix_time_millis(time).is_ok() {
            self.link.clock = Some(ClockSync {
                boot_id: self.boot_id,
                offset_ms: time.saturating_sub(rtc.uptime_millis()),
            });
            self.clock_corrected = true;
            self.outcome.clock = change;
        }
    }

    fn burst_acked(&self) -> bool {
        self.current.is_some_and(|burst| burst.is_acknowledged())
    }

    /// The burst the next wake calls the previous one: this wake's, or else the one before.
    fn last_burst(&self) -> Option<Burst> {
        self.current.or(self.previous)
    }
}

/// How long is left of a wait of `total_ms` that began at uptime `since_ms`.
fn remaining<R: Rtc>(rtc: &R, since_ms: u64, total_ms: u32) -> u32 {
    let spent = rtc.uptime_millis().saturating_sub(since_ms);
    u32::try_from(u64::from(total_ms).saturating_sub(spent)).unwrap_or(0)
}

/// A probe nonce: from the TRNG, or from the clock when the TRNG gives nothing. The nonce only
/// matches a reply to its probe; it protects nothing.
fn probe_nonce<R: Rtc, T: Trng>(rtc: &R, trng: &mut T, boot_id: u64, channel: u8) -> [u8; 8] {
    let mut nonce = [0u8; NONCE_LENGTH];
    if trng.fill(&mut nonce).is_err() {
        let mixed = rtc
            .uptime_millis()
            .rotate_left(17)
            .wrapping_add(boot_id.rotate_left(41))
            ^ u64::from(channel);
        nonce = mixed.to_be_bytes();
    }
    nonce
}

/// Probes one channel: the Hub that answered within [`PROBE_WAIT_MS`], if any.
#[allow(clippy::too_many_arguments, reason = "the HAL of one wake")]
async fn probe<D: DatagramRadio, R: Rtc, T: Trng, F: Flash>(
    session: &mut Session<'_>,
    radio: &mut D,
    rtc: &mut R,
    trng: &mut T,
    buffer: &mut ReportBuffer,
    flash: &mut F,
    channel: u8,
) -> Option<Found> {
    radio.set_channel(channel).ok()?;
    let nonce = probe_nonce(rtc, trng, session.boot_id, channel);
    let mut message = [0u8; PROBE_LENGTH];
    Message::Probe { nonce }.encode(&mut message).ok()?;
    // The radio's send status says nothing about delivery (N-1): only a reply counts.
    let _ = radio.send(&BROADCAST, &message).await;
    let started = rtc.uptime_millis();
    let mut datagram = [0u8; DATAGRAM_MAX];
    loop {
        let left = remaining(rtc, started, PROBE_WAIT_MS);
        if left == 0 {
            return None;
        }
        let received = radio.receive(&mut datagram, left).await?;
        let bytes = &datagram[..received.len.min(DATAGRAM_MAX)];
        match Message::decode(bytes) {
            Ok(Message::ProbeReply {
                nonce: echoed,
                pending,
            }) if echoed == nonce => {
                return Some(Found {
                    mac: received.source,
                    channel,
                    pending,
                });
            }
            // A downlink that arrives while probing is as good as any other.
            Ok(Message::Downlink(_)) => {
                session.receive(buffer, flash, rtc, bytes);
            }
            _ => {}
        }
    }
}

/// Step 2: the known channel, or a scan when one is due and allowed.
async fn find_hub<D: DatagramRadio, R: Rtc, T: Trng, F: Flash>(
    session: &mut Session<'_>,
    radio: &mut D,
    rtc: &mut R,
    trng: &mut T,
    buffer: &mut ReportBuffer,
    flash: &mut F,
) -> Option<Found> {
    let known = session.link.channel;
    let scan_due = known.is_none() || session.link.misses >= RESCAN_AFTER_MISSES;
    let scan = scan_due && session.link.scan_holdoff <= 1;
    if scan_due && !scan {
        session.link.scan_holdoff -= 1;
    }

    let mut found = None;
    if let Some(channel) = known {
        found = probe(session, radio, rtc, trng, buffer, flash, channel).await;
    }
    if found.is_none() && scan {
        for channel in FIRST_CHANNEL..=LAST_CHANNEL {
            if Some(channel) == known {
                continue;
            }
            found = probe(session, radio, rtc, trng, buffer, flash, channel).await;
            if found.is_some() {
                break;
            }
        }
    }
    match found {
        Some(hub) => {
            session.link.channel = Some(hub.channel);
            session.link.scan_holdoff = 0;
            session.outcome.hub = Hub::Found {
                channel: hub.channel,
                scanned: scan && Some(hub.channel) != known,
            };
            session.outcome.pending = hub.pending;
        }
        None => {
            if scan {
                session.link.scan_holdoff = SCAN_BACKOFF_WAKES;
            }
            session.outcome.hub = Hub::NotFound { scanned: scan };
        }
    }
    found
}

/// When a listen may stop before its window is over.
#[derive(Clone, Copy)]
enum Until {
    /// Once the wake has applied this many downlinks: the ones a probe reply announced.
    Downlinks(u8),
    /// Once every frame of the burst is acknowledged.
    BurstAcked,
}

/// Listens for up to `window_ms` from uptime `since_ms`, applying downlinks; stops early as
/// `until` says.
#[allow(clippy::too_many_arguments, reason = "the HAL of one wake")]
async fn listen<D: DatagramRadio, R: Rtc, F: Flash>(
    session: &mut Session<'_>,
    radio: &mut D,
    rtc: &mut R,
    buffer: &mut ReportBuffer,
    flash: &mut F,
    since_ms: u64,
    window_ms: u32,
    until: Until,
) {
    let mut datagram = [0u8; DATAGRAM_MAX];
    loop {
        let left = remaining(rtc, since_ms, window_ms);
        if left == 0 {
            return;
        }
        let Some(received) = radio.receive(&mut datagram, left).await else {
            return;
        };
        let bytes = &datagram[..received.len.min(DATAGRAM_MAX)];
        session.receive(buffer, flash, rtc, bytes);
        let done = match until {
            Until::Downlinks(count) => session.outcome.downlinks >= count,
            Until::BurstAcked => session.burst_acked(),
        };
        if done {
            return;
        }
    }
}

/// Reserves `frames` frame counters: the raised ceiling is in flash, and read back, before any
/// of them seals a frame.
fn reserve_counters<P: Partitions>(
    parts: &mut P,
    frames: usize,
    faults: &mut Faults,
) -> Option<u64> {
    let Some(flash) = parts.open(Partition::Frame) else {
        faults.partition = Some(Partition::Frame);
        return None;
    };
    // `usize` to `u64` never truncates on the targets this runs on.
    match ReservedCounter::new(flash, FRAME_MAGIC).reserve(frames as u64) {
        Ok(range) => Some(range.start),
        Err(error) => {
            faults.counter = Some(error);
            None
        }
    }
}

/// Step 4: seals and sends one frame per buffered report, oldest first. Returns how many went
/// out.
#[allow(clippy::too_many_arguments, reason = "the HAL of one wake")]
async fn send_burst<D: DatagramRadio, R: Rtc, F: Flash>(
    session: &mut Session<'_>,
    radio: &mut D,
    rtc: &R,
    buffer: &ReportBuffer,
    flash: &mut F,
    hub: &Found,
    first_counter: u64,
    frames: usize,
) -> u8 {
    let hash = spec_hash();
    let mut message = [0u8; DATAGRAM_MAX];
    let mut after = None;
    let mut sent = 0u8;
    for counter in (first_counter..).take(frames) {
        let report = match buffer.next(flash, after) {
            Ok(Some(report)) => report,
            Ok(None) => break,
            Err(error) => {
                session.outcome.faults.buffer.get_or_insert(error);
                break;
            }
        };
        after = Some(report.report_seq);
        let uptime_ms = rtc.uptime_millis();
        let mut sealed = None;
        if session.link.specifications_requested {
            let frame = node_frame(
                &report,
                session.boot_id,
                uptime_ms,
                &hash,
                Some(specification_set()),
            );
            // A frame too long with the set goes without it; the request stays open.
            if let Ok(length) = seal_uplink(session.keys, counter, &frame, &mut message) {
                session.link.specifications_requested = false;
                session.outcome.specifications_sent = true;
                sealed = Some(length);
            }
        }
        let length = match sealed {
            Some(length) => length,
            None => {
                let frame = node_frame(&report, session.boot_id, uptime_ms, &hash, None);
                match seal_uplink(session.keys, counter, &frame, &mut message) {
                    Ok(length) => length,
                    Err(error) => {
                        session.outcome.faults.seal.get_or_insert(error);
                        continue;
                    }
                }
            }
        };
        // The radio's send status is not delivery (N-1): only a sealed downlink deletes.
        let _ = radio.send(&hub.mac, &message[..length]).await;
        sent += 1;
    }
    message.fill(0);
    sent
}

/// Runs the transport step of one wake; see the module documentation for the order.
///
/// `link` is the state [`begin`] returned; it is updated and, when it changed, written back.
/// `report` is the wake report, `None` when the wake has none to send.
#[allow(clippy::too_many_arguments, reason = "the HAL of one wake")]
pub async fn run<P, D, R, T>(
    parts: &mut P,
    radio: &mut D,
    rtc: &mut R,
    trng: &mut T,
    keys: &DeviceKeys,
    boot_id: u64,
    link: &mut LinkState,
    report: Option<&Report>,
) -> Outcome
where
    P: Partitions,
    D: DatagramRadio,
    R: Rtc,
    T: Trng,
{
    let loaded = *link;
    let mut outcome = Outcome::default();
    let mut session = Session {
        keys,
        boot_id,
        link,
        previous: loaded.burst,
        current: None,
        clock_corrected: false,
        outcome: &mut outcome,
    };

    'radio: {
        // 1. Buffer the report. Without the buffer there is nothing to send or to acknowledge.
        let Some(mut buffer) = store(parts, report, session.outcome) else {
            break 'radio;
        };

        // 2 and 3. Find a Hub, and collect the downlink it kept.
        let hub = {
            let Some(mut flash) = parts.open(Partition::Buffer) else {
                session.outcome.faults.partition = Some(Partition::Buffer);
                break 'radio;
            };
            let hub = find_hub(&mut session, radio, rtc, trng, &mut buffer, &mut flash).await;
            if let Some(hub) = hub
                && hub.pending > 0
            {
                let since = rtc.uptime_millis();
                let wanted = session.outcome.downlinks.saturating_add(hub.pending);
                listen(
                    &mut session,
                    radio,
                    rtc,
                    &mut buffer,
                    &mut flash,
                    since,
                    PENDING_WAIT_MS,
                    Until::Downlinks(wanted),
                )
                .await;
            }
            hub
        };
        session.outcome.backlog = buffer.len();
        let Some(hub) = hub else {
            break 'radio;
        };

        // 4. One fresh counter per frame, in flash before anything is sealed.
        let frames = buffer.len().min(BURST_MAX);
        if frames == 0 {
            break 'radio;
        }
        let Some(first_counter) = reserve_counters(parts, frames, &mut session.outcome.faults)
        else {
            break 'radio;
        };
        let Some(mut flash) = parts.open(Partition::Buffer) else {
            session.outcome.faults.partition = Some(Partition::Buffer);
            break 'radio;
        };
        let burst = Burst {
            first_counter,
            // At most eight frames.
            frames: frames as u8,
            acked: 0,
            boot_id,
            uptime_ms: rtc.uptime_millis(),
        };
        session.current = Some(burst);
        let sent = send_burst(
            &mut session,
            radio,
            rtc,
            &buffer,
            &mut flash,
            &hub,
            first_counter,
            frames,
        )
        .await;
        session.outcome.sent = sent;
        if sent == 0 {
            session.current = None;
            break 'radio;
        }

        // 5. The acknowledgement window.
        let last_send = rtc.uptime_millis();
        listen(
            &mut session,
            radio,
            rtc,
            &mut buffer,
            &mut flash,
            last_send,
            ACK_WINDOW_MS,
            Until::BurstAcked,
        )
        .await;
        session.outcome.backlog = buffer.len();
    }

    // 6. Only a fresh downlink ends a run of misses.
    session.link.misses = if session.outcome.fresh {
        0
    } else {
        session.link.misses.saturating_add(1)
    };
    session.outcome.misses = session.link.misses;
    session.link.burst = session.last_burst();
    let link = *session.link;
    if link != loaded {
        match parts.open(Partition::Link) {
            Some(mut flash) => {
                if let Err(error) = link::save(&mut flash, &link) {
                    outcome.faults.link = Some(error);
                }
            }
            None => {
                outcome.faults.partition.get_or_insert(Partition::Link);
            }
        }
    }
    outcome
}
