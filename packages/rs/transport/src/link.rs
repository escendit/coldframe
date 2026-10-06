//! What the Node remembers about its link between wakes: the Hub's channel, the miss count, the
//! last burst it sent, the clock it was given, and whether the Server asked for its Specification
//! set. Every wake is a reboot, so this lives in flash.
//!
//! Layout: two 4096-byte sectors of 64 slots of 64 bytes, used like the counters of
//! `coldframe-sensing`: records are appended to the active sector, and when it is full the
//! *other* sector is erased and takes the next record, so the old one stays valid until then. The
//! state is the valid record with the highest generation. A record is
//!
//! | Bytes | Content |
//! | --- | --- |
//! | 0..4 | [`LINK_MAGIC`] |
//! | 4 | [`RECORD_VERSION`] |
//! | 5 | flags: bit 0 Specifications requested, bit 1 a burst, bit 2 a clock |
//! | 6 | the channel, 0 for none |
//! | 7 | consecutive misses |
//! | 8 | scan hold-off, in wakes |
//! | 9 | frames of the burst |
//! | 10 | one bit per frame of the burst a fresh downlink has acknowledged |
//! | 12..16 | generation |
//! | 16..40 | the burst: first counter, boot ID, uptime at the send |
//! | 40..56 | the clock: boot ID, Unix milliseconds minus uptime |
//! | 56..60 | the first bytes of SHA-256 over bytes 0..56 |
//!
//! Integers are big endian. Nothing here is a secret, and nothing here is trusted for delivery:
//! a lost or corrupt state costs a channel scan and an unsynced clock, never a Reading. A region
//! with written slots and no valid record therefore reads as the default state.

use core::fmt;

use coldframe_hal::{Flash, FlashError};
use sha2::{Digest, Sha256};

/// Size of one sector, as on the ESP32-S3.
pub const SECTOR_SIZE: u32 = 4096;

/// Size of one record slot.
pub const SLOT_SIZE: usize = 64;

/// Slots per sector.
pub const SLOTS_PER_SECTOR: u32 = SECTOR_SIZE / SLOT_SIZE as u32;

/// Number of sectors the link state uses.
pub const SECTORS: u32 = 2;

/// Magic of a link-state record (`cf_link`).
pub const LINK_MAGIC: [u8; 4] = *b"CFLK";

/// Version byte of a record.
pub const RECORD_VERSION: u8 = 1;

const FLAG_SPECIFICATIONS: u8 = 1 << 0;
const FLAG_BURST: u8 = 1 << 1;
const FLAG_CLOCK: u8 = 1 << 2;
const BODY_LENGTH: usize = 56;
const CHECK_LENGTH: usize = 4;

/// Slots read from flash at once while scanning.
const SCAN_SLOTS: usize = 8;

/// The frames one wake sent, sealed under consecutive counters.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Burst {
    /// The counter of its first frame.
    pub first_counter: u64,
    /// How many frames it has.
    pub frames: u8,
    /// Bit `i` is set once a fresh downlink has acknowledged frame `first_counter + i`: a
    /// downlink counts as fresh once per frame.
    pub acked: u8,
    /// The boot ID of the power-on that sent it.
    pub boot_id: u64,
    /// The uptime when it was sent, in milliseconds.
    pub uptime_ms: u64,
}

impl Burst {
    /// The index of the frame sealed under `counter`, when it is a frame of this burst.
    #[must_use]
    pub fn index_of(&self, counter: u64) -> Option<u8> {
        let index = counter.checked_sub(self.first_counter)?;
        // A burst has at most eight frames, one per bit of `acked`.
        (index < u64::from(self.frames.min(8))).then_some(index as u8)
    }

    /// Whether a frame of this burst was sealed under `counter`.
    #[must_use]
    pub fn contains(&self, counter: u64) -> bool {
        self.index_of(counter).is_some()
    }

    /// Records the first downlink that acknowledges `counter`. Returns whether it is fresh: a
    /// frame of this burst, not acknowledged before.
    pub fn acknowledge(&mut self, counter: u64) -> bool {
        match self.index_of(counter) {
            Some(index) if self.acked & (1 << index) == 0 => {
                self.acked |= 1 << index;
                true
            }
            _ => false,
        }
    }

    /// Whether every frame of this burst has been acknowledged.
    #[must_use]
    pub fn is_acknowledged(&self) -> bool {
        let all = match self.frames {
            0 => return false,
            frames @ 1..8 => (1u8 << frames) - 1,
            _ => u8::MAX,
        };
        self.acked & all == all
    }
}

/// The wall clock a downlink gave the Node, valid for one boot ID.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct ClockSync {
    /// The boot ID it was set on.
    pub boot_id: u64,
    /// Unix time in milliseconds minus the uptime in milliseconds.
    pub offset_ms: u64,
}

/// The link state.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct LinkState {
    /// The channel a Hub last answered on.
    pub channel: Option<u8>,
    /// Consecutive wakes without a fresh downlink.
    pub misses: u8,
    /// After a full scan that found no Hub: the next scan waits until this has counted down to 1.
    pub scan_holdoff: u8,
    /// The last fresh downlink said `specifications_unknown`: the next frame carries the set.
    pub specifications_requested: bool,
    /// The last burst sent.
    pub burst: Option<Burst>,
    /// The clock, when a downlink set it.
    pub clock: Option<ClockSync>,
}

/// Why the link state could not be read or written.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum LinkError {
    /// The flash failed.
    Flash(FlashError),
    /// The region is smaller than two sectors.
    TooSmall,
    /// A record read back differently from what was written.
    NotVerified,
}

impl From<FlashError> for LinkError {
    fn from(error: FlashError) -> Self {
        Self::Flash(error)
    }
}

impl fmt::Display for LinkError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Flash(error) => write!(f, "link state flash: {error}"),
            Self::TooSmall => f.write_str("link state region smaller than two sectors"),
            Self::NotVerified => f.write_str("link state record not verified after write"),
        }
    }
}

impl core::error::Error for LinkError {}

fn be_u64(bytes: &[u8]) -> u64 {
    let mut value = [0u8; 8];
    value.copy_from_slice(bytes);
    u64::from_be_bytes(value)
}

fn check(body: &[u8]) -> [u8; CHECK_LENGTH] {
    let digest = Sha256::digest(body);
    let mut check = [0u8; CHECK_LENGTH];
    check.copy_from_slice(&digest[..CHECK_LENGTH]);
    check
}

/// Encodes the record of `state` with `generation`.
#[must_use]
pub fn encode_record(state: &LinkState, generation: u32) -> [u8; SLOT_SIZE] {
    let mut record = [0u8; SLOT_SIZE];
    record[..4].copy_from_slice(&LINK_MAGIC);
    record[4] = RECORD_VERSION;
    if state.specifications_requested {
        record[5] |= FLAG_SPECIFICATIONS;
    }
    record[6] = state.channel.unwrap_or(0);
    record[7] = state.misses;
    record[8] = state.scan_holdoff;
    record[12..16].copy_from_slice(&generation.to_be_bytes());
    if let Some(burst) = state.burst {
        record[5] |= FLAG_BURST;
        record[9] = burst.frames;
        record[10] = burst.acked;
        record[16..24].copy_from_slice(&burst.first_counter.to_be_bytes());
        record[24..32].copy_from_slice(&burst.boot_id.to_be_bytes());
        record[32..40].copy_from_slice(&burst.uptime_ms.to_be_bytes());
    }
    if let Some(clock) = state.clock {
        record[5] |= FLAG_CLOCK;
        record[40..48].copy_from_slice(&clock.boot_id.to_be_bytes());
        record[48..56].copy_from_slice(&clock.offset_ms.to_be_bytes());
    }
    let check = check(&record[..BODY_LENGTH]);
    record[BODY_LENGTH..BODY_LENGTH + CHECK_LENGTH].copy_from_slice(&check);
    record[BODY_LENGTH + CHECK_LENGTH..].fill(0xFF);
    record
}

/// The state and generation of a valid record, or `None`.
#[must_use]
pub fn decode_record(record: &[u8; SLOT_SIZE]) -> Option<(LinkState, u32)> {
    if record[..4] != LINK_MAGIC
        || record[4] != RECORD_VERSION
        || record[BODY_LENGTH..BODY_LENGTH + CHECK_LENGTH] != check(&record[..BODY_LENGTH])
    {
        return None;
    }
    let flags = record[5];
    let mut generation = [0u8; 4];
    generation.copy_from_slice(&record[12..16]);
    let state = LinkState {
        channel: (record[6] != 0).then_some(record[6]),
        misses: record[7],
        scan_holdoff: record[8],
        specifications_requested: flags & FLAG_SPECIFICATIONS != 0,
        burst: (flags & FLAG_BURST != 0).then(|| Burst {
            first_counter: be_u64(&record[16..24]),
            frames: record[9],
            acked: record[10],
            boot_id: be_u64(&record[24..32]),
            uptime_ms: be_u64(&record[32..40]),
        }),
        clock: (flags & FLAG_CLOCK != 0).then(|| ClockSync {
            boot_id: be_u64(&record[40..48]),
            offset_ms: be_u64(&record[48..56]),
        }),
    };
    Some((state, u32::from_be_bytes(generation)))
}

/// What a scan of the region found.
struct Scan {
    /// The newest valid record: its state, generation and sector.
    best: Option<(LinkState, u32, u32)>,
    /// The last written slot of each sector.
    last_written: [Option<u32>; SECTORS as usize],
}

fn slot_offset(sector: u32, slot: u32) -> u32 {
    sector * SECTOR_SIZE + slot * SLOT_SIZE as u32
}

fn scan<F: Flash>(flash: &mut F) -> Result<Scan, LinkError> {
    if flash.capacity() < (SECTORS * SECTOR_SIZE) as usize {
        return Err(LinkError::TooSmall);
    }
    let mut found = Scan {
        best: None,
        last_written: [None; SECTORS as usize],
    };
    let mut chunk = [0u8; SCAN_SLOTS * SLOT_SIZE];
    for sector in 0..SECTORS {
        for first in (0..SLOTS_PER_SECTOR).step_by(SCAN_SLOTS) {
            flash.read(slot_offset(sector, first), &mut chunk)?;
            for (index, bytes) in (first..).zip(chunk.chunks_exact(SLOT_SIZE)) {
                if bytes.iter().all(|&byte| byte == 0xFF) {
                    continue;
                }
                found.last_written[sector as usize] = Some(index);
                let mut slot = [0u8; SLOT_SIZE];
                slot.copy_from_slice(bytes);
                if let Some((state, generation)) = decode_record(&slot)
                    && found.best.is_none_or(|(_, newest, _)| generation > newest)
                {
                    found.best = Some((state, generation, sector));
                }
            }
        }
    }
    Ok(found)
}

/// Reads the link state: the newest valid record, or the default state when there is none.
///
/// # Errors
///
/// [`LinkError::TooSmall`] or [`LinkError::Flash`].
pub fn load<F: Flash>(flash: &mut F) -> Result<LinkState, LinkError> {
    Ok(scan(flash)?
        .best
        .map(|(state, _, _)| state)
        .unwrap_or_default())
}

/// Writes `state` as the newest record and reads it back.
///
/// # Errors
///
/// See [`LinkError`]. The previous state stays readable after a failure.
pub fn save<F: Flash>(flash: &mut F, state: &LinkState) -> Result<(), LinkError> {
    let found = scan(flash)?;
    let (generation, mut sector, mut slot) = match found.best {
        Some((_, generation, sector)) => (
            generation.saturating_add(1),
            sector,
            found.last_written[sector as usize].map_or(0, |last| last + 1),
        ),
        None => {
            if found.last_written[0].is_some() {
                // Written slots and no valid record: start over.
                flash.erase(0, SECTOR_SIZE)?;
            }
            (1, 0, 0)
        }
    };
    if slot >= SLOTS_PER_SECTOR {
        // Roll over: the full sector keeps the state until the other one holds a record.
        sector = (sector + 1) % SECTORS;
        flash.erase(sector * SECTOR_SIZE, SECTOR_SIZE)?;
        slot = 0;
    }
    let record = encode_record(state, generation);
    let offset = slot_offset(sector, slot);
    flash.write(offset, &record)?;
    let mut stored = [0u8; SLOT_SIZE];
    flash.read(offset, &mut stored)?;
    if stored == record {
        Ok(())
    } else {
        Err(LinkError::NotVerified)
    }
}
