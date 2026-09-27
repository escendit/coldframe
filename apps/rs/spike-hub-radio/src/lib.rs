//! Shared code for the Hub radio coexistence spike (PRD Open Question 1).
//!
//! THROWAWAY SPIKE CODE. Not the production wire format: the real ESP-NOW
//! messages live in `packages/proto` (AD-10) and are sealed end to end (AD-12).
//! This crate only needs something small enough to measure latency and loss.

#![no_std]

use core::cell::RefCell;

use critical_section::Mutex;

/// `static` allocation helper (same pattern as the esp-hal examples).
#[macro_export]
macro_rules! mk_static {
    ($t:ty, $val:expr) => {{
        static STATIC_CELL: static_cell::StaticCell<$t> = static_cell::StaticCell::new();
        #[deny(unused_attributes)]
        let x = STATIC_CELL.uninit().write(($val));
        x
    }};
}

// ---------------------------------------------------------------------------
// Spike ESP-NOW wire format (little endian)
//
//   byte 0..2  magic  b"CF"
//   byte 2     version (1)
//   byte 3     kind
//   ...        kind-specific body
// ---------------------------------------------------------------------------

pub const MAGIC: [u8; 2] = *b"CF";
pub const VERSION: u8 = 1;

pub const KIND_PROBE: u8 = 0x01;
pub const KIND_PROBE_REPLY: u8 = 0x02;
pub const KIND_DATA: u8 = 0x10;
pub const KIND_ACK: u8 = 0x11;

/// Ack was produced after a real HTTPS round trip to the server.
pub const ACK_MODE_SERVER: u8 = 1;
/// Ack was sent immediately on reception (radio-only latency).
pub const ACK_MODE_IMMEDIATE: u8 = 2;

/// Server round-trip outcome carried in the ack.
pub const SERVER_OK: u8 = 0;
pub const SERVER_FAILED: u8 = 1;
pub const SERVER_SKIPPED: u8 = 2;

/// Size of the fake sealed reading payload in a DATA frame. Roughly the size of
/// a sealed Node frame (nonce + ciphertext + tag) so airtime is realistic.
pub const DATA_PAYLOAD_LEN: usize = 48;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Frame {
    /// Node → broadcast: "is a Hub on this channel?"
    Probe { nonce: u32, channel: u8 },
    /// Hub → Node: "yes, I am on `channel`".
    ProbeReply { nonce: u32, channel: u8 },
    /// Node → Hub: a (fake) reading with a sequence number.
    Data { seq: u32 },
    /// Hub → Node: acknowledgement for `seq`.
    Ack {
        seq: u32,
        mode: u8,
        server: u8,
        /// Hub-side time between frame reception and ack transmission (µs).
        hub_us: u32,
    },
}

impl Frame {
    /// Encode into `buf`, returning the used length.
    pub fn encode(&self, buf: &mut [u8; 64]) -> usize {
        buf[0..2].copy_from_slice(&MAGIC);
        buf[2] = VERSION;
        match *self {
            Frame::Probe { nonce, channel } => {
                buf[3] = KIND_PROBE;
                buf[4..8].copy_from_slice(&nonce.to_le_bytes());
                buf[8] = channel;
                9
            }
            Frame::ProbeReply { nonce, channel } => {
                buf[3] = KIND_PROBE_REPLY;
                buf[4..8].copy_from_slice(&nonce.to_le_bytes());
                buf[8] = channel;
                9
            }
            Frame::Data { seq } => {
                buf[3] = KIND_DATA;
                buf[4..8].copy_from_slice(&seq.to_le_bytes());
                // Deterministic filler standing in for the sealed payload.
                for (i, b) in buf[8..8 + DATA_PAYLOAD_LEN].iter_mut().enumerate() {
                    *b = (seq as u8).wrapping_add(i as u8);
                }
                8 + DATA_PAYLOAD_LEN
            }
            Frame::Ack {
                seq,
                mode,
                server,
                hub_us,
            } => {
                buf[3] = KIND_ACK;
                buf[4..8].copy_from_slice(&seq.to_le_bytes());
                buf[8] = mode;
                buf[9] = server;
                buf[10..14].copy_from_slice(&hub_us.to_le_bytes());
                14
            }
        }
    }

    pub fn decode(data: &[u8]) -> Option<Frame> {
        if data.len() < 8 || data[0..2] != MAGIC || data[2] != VERSION {
            return None;
        }
        let u32_at =
            |i: usize| u32::from_le_bytes([data[i], data[i + 1], data[i + 2], data[i + 3]]);
        match data[3] {
            KIND_PROBE if data.len() >= 9 => Some(Frame::Probe {
                nonce: u32_at(4),
                channel: data[8],
            }),
            KIND_PROBE_REPLY if data.len() >= 9 => Some(Frame::ProbeReply {
                nonce: u32_at(4),
                channel: data[8],
            }),
            KIND_DATA => Some(Frame::Data { seq: u32_at(4) }),
            KIND_ACK if data.len() >= 14 => Some(Frame::Ack {
                seq: u32_at(4),
                mode: data[8],
                server: data[9],
                hub_us: u32_at(10),
            }),
            _ => None,
        }
    }
}

pub fn mode_name(mode: u8) -> &'static str {
    match mode {
        ACK_MODE_SERVER => "server",
        ACK_MODE_IMMEDIATE => "immediate",
        _ => "?",
    }
}

// ---------------------------------------------------------------------------
// Latency statistics
// ---------------------------------------------------------------------------

/// Number of most-recent samples kept for percentile calculation.
pub const WINDOW: usize = 256;

/// Rolling latency window (last `WINDOW` samples) plus all-time count/max.
///
/// Percentiles are over the rolling window; `max_all` and `count` are
/// all-time so a single spike over a 2 h run is never lost.
pub struct Latency {
    samples: [u32; WINDOW],
    next: usize,
    len: usize,
    pub count: u32,
    pub max_all: u32,
    pub sum_all: u64,
}

impl Latency {
    pub const fn new() -> Self {
        Self {
            samples: [0; WINDOW],
            next: 0,
            len: 0,
            count: 0,
            max_all: 0,
            sum_all: 0,
        }
    }

    pub fn record(&mut self, v: u32) {
        self.samples[self.next] = v;
        self.next = (self.next + 1) % WINDOW;
        if self.len < WINDOW {
            self.len += 1;
        }
        self.count = self.count.wrapping_add(1);
        self.sum_all += v as u64;
        if v > self.max_all {
            self.max_all = v;
        }
    }

    /// Returns (p50, p95, window max, all-time max). All zero when empty.
    pub fn summary(&self) -> Summary {
        if self.len == 0 {
            return Summary::default();
        }
        let mut sorted = [0u32; WINDOW];
        let s = &mut sorted[..self.len];
        s.copy_from_slice(&self.samples[..self.len]);
        s.sort_unstable();
        let pct = |p: usize| s[((self.len - 1) * p) / 100];
        Summary {
            n: self.count,
            p50: pct(50),
            p95: pct(95),
            max_window: s[self.len - 1],
            max_all: self.max_all,
            mean_all: (self.sum_all / self.count.max(1) as u64) as u32,
        }
    }
}

impl Default for Latency {
    fn default() -> Self {
        Self::new()
    }
}

#[derive(Debug, Default, Clone, Copy)]
pub struct Summary {
    pub n: u32,
    pub p50: u32,
    pub p95: u32,
    pub max_window: u32,
    pub max_all: u32,
    pub mean_all: u32,
}

/// A `Latency` behind a critical-section mutex so any task can record into it.
pub struct SharedLatency(Mutex<RefCell<Latency>>);

impl SharedLatency {
    pub const fn new() -> Self {
        Self(Mutex::new(RefCell::new(Latency::new())))
    }

    pub fn record(&self, v: u32) {
        critical_section::with(|cs| self.0.borrow_ref_mut(cs).record(v));
    }

    pub fn summary(&self) -> Summary {
        critical_section::with(|cs| self.0.borrow_ref(cs).summary())
    }
}

impl Default for SharedLatency {
    fn default() -> Self {
        Self::new()
    }
}

/// Format a MAC address as `aa:bb:cc:dd:ee:ff`.
pub struct Mac<'a>(pub &'a [u8; 6]);

impl core::fmt::Display for Mac<'_> {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        let m = self.0;
        write!(
            f,
            "{:02x}:{:02x}:{:02x}:{:02x}:{:02x}:{:02x}",
            m[0], m[1], m[2], m[3], m[4], m[5]
        )
    }
}

/// Parse a decimal build-time env value, falling back to `default`.
pub const fn parse_u32(s: Option<&str>, default: u32) -> u32 {
    match s {
        None => default,
        Some(s) => {
            let b = s.as_bytes();
            if b.is_empty() {
                return default;
            }
            let mut i = 0;
            let mut v: u32 = 0;
            while i < b.len() {
                let c = b[i];
                if c < b'0' || c > b'9' {
                    return default;
                }
                v = v * 10 + (c - b'0') as u32;
                i += 1;
            }
            v
        }
    }
}

/// `const` string equality (for build-time env switches).
pub const fn str_eq(a: &str, b: &str) -> bool {
    let (a, b) = (a.as_bytes(), b.as_bytes());
    if a.len() != b.len() {
        return false;
    }
    let mut i = 0;
    while i < a.len() {
        if a[i] != b[i] {
            return false;
        }
        i += 1;
    }
    true
}
