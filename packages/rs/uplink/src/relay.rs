//! The Hub's relay state between the radio and the Server (AD-9).
//!
//! The Hub relays sealed envelopes as opaque bytes. It reads only the kind byte of a radio
//! message: it parses no envelope, stores no Reading and builds no acknowledgement.
//!
//! - **Uplink queue.** Envelopes heard since the last `POST /device/ingest`, with the MAC address
//!   they came from, in arrival order: at most [`UPLINK_QUEUE`]. With the queue full the newest is
//!   dropped (the Node sends it again). An envelope already in the queue is not queued twice.
//!   [`Relay::take_batch`] empties the queue, so no envelope is ever posted twice.
//! - **Kept downlinks.** Up to [`DOWNLINKS_PER_NODE`] downlinks per Node, keyed by the MAC address
//!   its frames came from, in arrival order, out of one pool of [`DOWNLINK_POOL`] in RAM only. A
//!   Node with that many kept loses its oldest; with the pool full, the Node heard from longest
//!   ago loses all of its. Each downlink is sent once when it arrives ([`Relay::next_unsent`]):
//!   with a fresh TLS session per request the Node has usually gone back to sleep by then. At
//!   that Node's next probe every kept downlink is offered once more, oldest first, and dropped
//!   ([`Relay::take_kept`]), so a backlog sent as one burst is acknowledged at the next wake.
//! - **Probes** are answered from here without any request to the Server, and only while the Hub
//!   can relay ([`Relay::set_relaying`]: the station associated and the clock set). Otherwise a
//!   Node would spend a burst and its frame counters on frames the Hub drops. [`Relay::hear`]
//!   gives the reply, whose `pending` is the number of downlinks kept for the sender.
//!
//! The radio task and the uplink share the state through a [`RelayPort`], which never holds it
//! across an `await`: a probe is answered while a request is in flight.

use coldframe_hal::{DATAGRAM_MAX, MacAddress};
use coldframe_protocol::radio::{ENVELOPE_MAX, Message, PENDING_MAX, PROBE_REPLY_LENGTH};
use heapless::Vec;

/// How many uplinks wait for one request, and so the most frames of one `POST /device/ingest`.
pub const UPLINK_QUEUE: usize = 8;

/// How many downlinks the Hub keeps for one Node: one burst.
pub const DOWNLINKS_PER_NODE: usize = PENDING_MAX as usize;

/// How many downlinks the Hub keeps in all.
pub const DOWNLINK_POOL: usize = 24;

/// One sealed envelope, exactly as it was heard or received.
#[derive(Clone, Copy)]
pub struct Envelope {
    len: u8,
    bytes: [u8; ENVELOPE_MAX],
}

// The length fits its byte.
const _: () = assert!(ENVELOPE_MAX <= u8::MAX as usize);

impl Envelope {
    const EMPTY: Self = Self {
        len: 0,
        bytes: [0; ENVELOPE_MAX],
    };

    /// A copy of `bytes`, or `None` when they are empty or longer than one radio message holds.
    fn new(bytes: &[u8]) -> Option<Self> {
        if bytes.is_empty() || bytes.len() > ENVELOPE_MAX {
            return None;
        }
        let mut envelope = Self::EMPTY;
        // At most 249 bytes.
        envelope.len = bytes.len() as u8;
        envelope.bytes[..bytes.len()].copy_from_slice(bytes);
        Some(envelope)
    }

    /// The envelope's bytes.
    #[must_use]
    pub fn as_bytes(&self) -> &[u8] {
        &self.bytes[..usize::from(self.len)]
    }
}

/// One uplink waiting for the Server.
#[derive(Clone, Copy)]
pub struct QueuedUplink {
    /// The MAC address the frame came from: where its downlink goes.
    pub source: MacAddress,
    /// The sealed envelope.
    pub envelope: Envelope,
}

/// The uplinks of one request, in arrival order.
pub type Batch = Vec<QueuedUplink, UPLINK_QUEUE>;

/// One kept downlink.
#[derive(Clone, Copy)]
struct Slot {
    /// 0 while the slot is empty; otherwise when it was written, as a running number.
    stamp: u64,
    mac: MacAddress,
    envelope: Envelope,
    /// The downlink has not gone out over the radio yet.
    unsent: bool,
}

impl Slot {
    const EMPTY: Self = Self {
        stamp: 0,
        mac: [0; 6],
        envelope: Envelope::EMPTY,
        unsent: false,
    };

    fn holds(&self, mac: &MacAddress) -> bool {
        self.stamp != 0 && self.mac == *mac
    }
}

/// What the Hub does with one radio message it heard.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Heard {
    /// A probe: send `reply` to the sender, then `pending` kept downlinks
    /// ([`Relay::take_kept`]).
    Probe {
        /// The encoded probe reply.
        reply: [u8; PROBE_REPLY_LENGTH],
        /// How many downlinks are kept for the sender.
        pending: u8,
    },
    /// A probe left unanswered: the Hub cannot relay now.
    NotRelaying,
    /// An uplink, queued for the Server.
    Queued,
    /// An uplink dropped because the queue is full.
    QueueFull,
    /// An uplink whose bytes are already in the queue.
    Duplicate,
    /// Not a message for a Hub.
    Ignored,
}

/// The relay state.
pub struct Relay {
    queue: Batch,
    slots: [Slot; DOWNLINK_POOL],
    stamp: u64,
    overflow: u32,
    relaying: bool,
}

impl Default for Relay {
    fn default() -> Self {
        Self::new()
    }
}

impl Relay {
    /// An empty relay.
    #[must_use]
    pub const fn new() -> Self {
        Self {
            queue: Vec::new(),
            slots: [Slot::EMPTY; DOWNLINK_POOL],
            stamp: 0,
            overflow: 0,
            relaying: false,
        }
    }

    /// Says whether the Hub can relay now: the station is associated and the clock is set. A
    /// probe is answered only while it can. `false` until the uplink says otherwise.
    pub fn set_relaying(&mut self, relaying: bool) {
        self.relaying = relaying;
    }

    /// How many downlinks are kept for the Node at `mac`.
    #[must_use]
    pub fn kept(&self, mac: &MacAddress) -> u8 {
        // At most eight per Node.
        self.slots.iter().filter(|slot| slot.holds(mac)).count() as u8
    }

    /// The index of the oldest downlink kept for `mac`.
    fn oldest(&self, mac: &MacAddress) -> Option<usize> {
        self.slots
            .iter()
            .enumerate()
            .filter(|(_, slot)| slot.holds(mac))
            .min_by_key(|(_, slot)| slot.stamp)
            .map(|(index, _)| index)
    }

    /// Takes in one radio message from `source`.
    pub fn hear(&mut self, source: &MacAddress, datagram: &[u8]) -> Heard {
        match Message::decode(datagram) {
            Ok(Message::Probe { nonce }) => {
                if !self.relaying {
                    return Heard::NotRelaying;
                }
                let pending = self.kept(source);
                let mut reply = [0u8; PROBE_REPLY_LENGTH];
                // The buffer is exactly one reply.
                let _ = Message::ProbeReply { nonce, pending }.encode(&mut reply);
                Heard::Probe { reply, pending }
            }
            Ok(Message::Uplink(bytes)) => {
                let Some(envelope) = Envelope::new(bytes) else {
                    return Heard::Ignored;
                };
                if self
                    .queue
                    .iter()
                    .any(|queued| queued.envelope.as_bytes() == bytes)
                {
                    return Heard::Duplicate;
                }
                let queued = QueuedUplink {
                    source: *source,
                    envelope,
                };
                if self.queue.push(queued).is_err() {
                    self.overflow = self.overflow.saturating_add(1);
                    return Heard::QueueFull;
                }
                Heard::Queued
            }
            _ => Heard::Ignored,
        }
    }

    /// Whether an uplink waits for the Server.
    #[must_use]
    pub fn has_uplinks(&self) -> bool {
        !self.queue.is_empty()
    }

    /// Empties the queue: the uplinks in arrival order, and how many were dropped for a full
    /// queue since the last batch.
    pub fn take_batch(&mut self) -> (Batch, u32) {
        (
            core::mem::take(&mut self.queue),
            core::mem::take(&mut self.overflow),
        )
    }

    /// Keeps `envelope` as the newest downlink of the Node at `mac`, to be sent once and offered
    /// at its next probe. Returns `false` for an empty envelope or one too long for a radio
    /// message.
    pub fn keep(&mut self, mac: &MacAddress, envelope: &[u8]) -> bool {
        let Some(envelope) = Envelope::new(envelope) else {
            return false;
        };
        if usize::from(self.kept(mac)) >= DOWNLINKS_PER_NODE
            && let Some(oldest) = self.oldest(mac)
        {
            self.slots[oldest] = Slot::EMPTY;
        }
        let free = |slots: &[Slot]| slots.iter().position(|slot| slot.stamp == 0);
        let index = match free(&self.slots) {
            Some(index) => index,
            None => {
                // The pool is full: the Node heard from longest ago gives up everything kept for
                // it (this Node, if it is the one, gives up its oldest).
                let newest_of = |mac: &MacAddress| {
                    self.slots
                        .iter()
                        .filter(|slot| slot.holds(mac))
                        .map(|slot| slot.stamp)
                        .max()
                        .unwrap_or(0)
                };
                let victim = self
                    .slots
                    .iter()
                    .map(|slot| slot.mac)
                    .min_by_key(newest_of)
                    .unwrap_or(*mac);
                if victim == *mac {
                    if let Some(oldest) = self.oldest(mac) {
                        self.slots[oldest] = Slot::EMPTY;
                    }
                } else {
                    for slot in &mut self.slots {
                        if slot.holds(&victim) {
                            *slot = Slot::EMPTY;
                        }
                    }
                }
                free(&self.slots).unwrap_or(0)
            }
        };
        self.stamp += 1;
        self.slots[index] = Slot {
            stamp: self.stamp,
            mac: *mac,
            envelope,
            unsent: true,
        };
        true
    }

    /// Takes the oldest downlink kept for the Node at `mac`, as the radio message to send; its
    /// length. It is offered this once: the relay no longer holds it.
    pub fn take_kept(&mut self, mac: &MacAddress, out: &mut [u8; DATAGRAM_MAX]) -> Option<usize> {
        let index = self.oldest(mac)?;
        let slot = core::mem::replace(&mut self.slots[index], Slot::EMPTY);
        Message::Downlink(slot.envelope.as_bytes()).encode(out).ok()
    }

    /// The next downlink that has not gone out yet, oldest first: where to send it, and the
    /// length of the radio message written to `out`. It is not handed out again by this call;
    /// it stays kept for the Node's next probe.
    pub fn next_unsent(&mut self, out: &mut [u8; DATAGRAM_MAX]) -> Option<(MacAddress, usize)> {
        let slot = self
            .slots
            .iter_mut()
            .filter(|slot| slot.stamp != 0 && slot.unsent)
            .min_by_key(|slot| slot.stamp)?;
        slot.unsent = false;
        let length = Message::Downlink(slot.envelope.as_bytes())
            .encode(out)
            .ok()?;
        Some((slot.mac, length))
    }
}

/// Access to the one [`Relay`] the radio task and the uplink share.
///
/// `async` for a single-threaded executor, like the `coldframe-hal` traits.
#[allow(async_fn_in_trait)]
pub trait RelayPort {
    /// Runs `f` on the relay state. It must not block and is never held across an `await`.
    fn with<T>(&self, f: impl FnOnce(&mut Relay) -> T) -> T;

    /// Completes when an uplink has been queued since the last call.
    async fn uplink_queued(&self);
}
