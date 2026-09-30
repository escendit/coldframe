//! A strictly increasing counter reserved in flash (AD-17).
//!
//! The counter keeps a *ceiling* in flash: every value below it may have been handed out. On
//! boot it jumps to the stored ceiling. [`ReservedCounter::reserve`] raises the ceiling, writes it
//! and reads it back, and only then hands out the values. Values therefore never repeat for the
//! life of the region, across resets and power loss; gaps are allowed.
//!
//! Layout: two 4096-byte sectors of 128 slots of 32 bytes. A record is
//! `magic(4) ‖ 0x01 ‖ 0x00×3 ‖ ceiling u64 BE ‖ SHA-256(first 16 bytes)[0..4] ‖ 0xFF×12`.
//! Records are appended to the active sector. When it is full, the *other* sector is erased and
//! the next record goes to its first slot, so the old sector stays valid until the new one holds
//! a record. The ceiling is the maximum over the valid records of both sectors, which makes a
//! torn write or an interrupted erase harmless.
//!
//! Power-loss safety needs a flash adapter whose write programs only the given bytes. An adapter
//! that rewrites the whole sector (read, erase, write back) could lose the active sector's records
//! on power loss; the Node's adapter uses plain NOR writes for that reason.
//!
//! - Every slot erased: a fresh region, ceiling 0.
//! - Some slot written but no valid record anywhere: [`CounterError::Corrupt`]. The counter never
//!   falls back to 0; erasing the partition is an operator decision.

use core::fmt;
use core::ops::Range;

use coldframe_hal::{Flash, FlashError};
use sha2::{Digest, Sha256};

/// Size of one sector, as on the ESP32-S3.
pub const SECTOR_SIZE: u32 = 4096;

/// Size of one record slot.
pub const SLOT_SIZE: usize = 32;

/// Slots per sector.
pub const SLOTS_PER_SECTOR: u32 = SECTOR_SIZE / SLOT_SIZE as u32;

/// Number of sectors the counter uses.
pub const SECTORS: u32 = 2;

/// Version byte of a record.
pub const RECORD_VERSION: u8 = 1;

/// Bytes covered by the check: magic, version, padding and ceiling.
const BODY_LENGTH: usize = 16;

/// Length of the check, the first bytes of SHA-256 over the body.
const CHECK_LENGTH: usize = 4;

/// Slots read from flash at once while scanning.
const SCAN_SLOTS: usize = 8;

/// Magic of the `reading_seq` counter (`cf_seq`).
pub const SEQ_MAGIC: [u8; 4] = *b"CFSQ";

/// Magic of the boot counter (`cf_boot`).
pub const BOOT_MAGIC: [u8; 4] = *b"CFBT";

/// Why the counter cannot hand out values.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum CounterError {
    /// The flash failed.
    Flash(FlashError),
    /// Slots were written but none holds a valid record.
    Corrupt,
    /// A record read back differently from what was written.
    NotVerified,
    /// The region is smaller than two sectors.
    TooSmall,
    /// The ceiling would overflow `u64`.
    Exhausted,
}

impl From<FlashError> for CounterError {
    fn from(error: FlashError) -> Self {
        Self::Flash(error)
    }
}

impl fmt::Display for CounterError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Flash(error) => write!(f, "counter flash: {error}"),
            Self::Corrupt => f.write_str("counter corrupt: no valid record"),
            Self::NotVerified => f.write_str("counter record not verified after write"),
            Self::TooSmall => f.write_str("counter region smaller than two sectors"),
            Self::Exhausted => f.write_str("counter exhausted"),
        }
    }
}

impl core::error::Error for CounterError {}

/// Encodes the record for `ceiling` under `magic`.
#[must_use]
pub fn encode_record(magic: [u8; 4], ceiling: u64) -> [u8; SLOT_SIZE] {
    let mut record = [0xFF; SLOT_SIZE];
    record[..4].copy_from_slice(&magic);
    record[4] = RECORD_VERSION;
    record[5..8].fill(0x00);
    record[8..BODY_LENGTH].copy_from_slice(&ceiling.to_be_bytes());
    let digest = Sha256::digest(&record[..BODY_LENGTH]);
    record[BODY_LENGTH..BODY_LENGTH + CHECK_LENGTH].copy_from_slice(&digest[..CHECK_LENGTH]);
    record
}

/// The ceiling of a valid record under `magic`, or `None`.
#[must_use]
pub fn decode_record(magic: [u8; 4], slot: &[u8; SLOT_SIZE]) -> Option<u64> {
    let mut ceiling = [0u8; 8];
    ceiling.copy_from_slice(&slot[8..BODY_LENGTH]);
    let ceiling = u64::from_be_bytes(ceiling);
    (*slot == encode_record(magic, ceiling)).then_some(ceiling)
}

/// What the scan found: the ceiling and where the next record goes.
#[derive(Clone, Copy, Debug)]
struct State {
    /// The highest valid ceiling, 0 on a fresh region.
    ceiling: u64,
    /// The sector records are appended to.
    sector: u32,
    /// The slot of that sector the next record goes to; `SLOTS_PER_SECTOR` when it is full.
    next_slot: u32,
}

fn sector_offset(sector: u32) -> u32 {
    sector * SECTOR_SIZE
}

fn slot_offset(sector: u32, slot: u32) -> u32 {
    sector_offset(sector) + slot * SLOT_SIZE as u32
}

/// A counter reserved in a flash region of at least two sectors.
pub struct ReservedCounter<F> {
    flash: F,
    magic: [u8; 4],
    state: Option<State>,
}

impl<F: Flash> ReservedCounter<F> {
    /// A counter over `flash` with records tagged `magic`. Nothing is read until first use.
    pub fn new(flash: F, magic: [u8; 4]) -> Self {
        Self {
            flash,
            magic,
            state: None,
        }
    }

    /// The flash region back.
    pub fn into_inner(self) -> F {
        self.flash
    }

    /// The stored ceiling: the next value [`Self::reserve`] hands out.
    ///
    /// # Errors
    ///
    /// [`CounterError::Flash`], [`CounterError::Corrupt`] or [`CounterError::TooSmall`].
    pub fn current(&mut self) -> Result<u64, CounterError> {
        Ok(self.state()?.ceiling)
    }

    /// Reserves `n` values: writes the raised ceiling, reads it back, and only then returns
    /// `ceiling..ceiling + n`. `n = 0` returns an empty range and writes nothing.
    ///
    /// # Errors
    ///
    /// See [`CounterError`]. On error no value is handed out.
    pub fn reserve(&mut self, n: u64) -> Result<Range<u64>, CounterError> {
        let mut state = self.state()?;
        let start = state.ceiling;
        if n == 0 {
            return Ok(start..start);
        }
        let end = start.checked_add(n).ok_or(CounterError::Exhausted)?;

        if state.next_slot >= SLOTS_PER_SECTOR {
            // Roll over: the full sector keeps the ceiling until the other one holds a record.
            let other = (state.sector + 1) % SECTORS;
            self.flash.erase(sector_offset(other), SECTOR_SIZE)?;
            state.sector = other;
            state.next_slot = 0;
        }
        let offset = slot_offset(state.sector, state.next_slot);
        // The slot is used from here on, even if the write fails: a retry takes a fresh one.
        state.next_slot += 1;
        self.state = Some(state);

        let record = encode_record(self.magic, end);
        self.flash.write(offset, &record)?;
        let mut stored = [0u8; SLOT_SIZE];
        self.flash.read(offset, &mut stored)?;
        if stored != record {
            return Err(CounterError::NotVerified);
        }
        state.ceiling = end;
        self.state = Some(state);
        Ok(start..end)
    }

    /// The scanned state, scanning on first use. A failed scan is not cached.
    fn state(&mut self) -> Result<State, CounterError> {
        if let Some(state) = self.state {
            return Ok(state);
        }
        let state = self.scan()?;
        self.state = Some(state);
        Ok(state)
    }

    /// Reads every slot of both sectors: the maximum valid ceiling, and the slot after the last
    /// written one in its sector.
    fn scan(&mut self) -> Result<State, CounterError> {
        if self.flash.capacity() < (SECTORS * SECTOR_SIZE) as usize {
            return Err(CounterError::TooSmall);
        }
        let mut best: Option<(u64, u32)> = None;
        let mut any_written = false;
        let mut last_written = [None::<u32>; SECTORS as usize];
        let mut chunk = [0u8; SCAN_SLOTS * SLOT_SIZE];
        for sector in 0..SECTORS {
            for first in (0..SLOTS_PER_SECTOR).step_by(SCAN_SLOTS) {
                self.flash.read(slot_offset(sector, first), &mut chunk)?;
                for (index, bytes) in (first..).zip(chunk.chunks_exact(SLOT_SIZE)) {
                    if bytes.iter().all(|&byte| byte == 0xFF) {
                        continue;
                    }
                    any_written = true;
                    last_written[sector as usize] = Some(index);
                    let mut slot = [0u8; SLOT_SIZE];
                    slot.copy_from_slice(bytes);
                    if let Some(ceiling) = decode_record(self.magic, &slot)
                        && best.is_none_or(|(highest, _)| ceiling > highest)
                    {
                        best = Some((ceiling, sector));
                    }
                }
            }
        }
        match best {
            Some((ceiling, sector)) => Ok(State {
                ceiling,
                sector,
                next_slot: last_written[sector as usize].map_or(0, |last| last + 1),
            }),
            None if any_written => Err(CounterError::Corrupt),
            None => Ok(State {
                ceiling: 0,
                sector: 0,
                next_slot: 0,
            }),
        }
    }
}
