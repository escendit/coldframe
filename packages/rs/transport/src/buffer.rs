//! The Node's report buffer (AD-17): wake reports kept in flash until a sealed downlink
//! acknowledges them.
//!
//! - It survives deep sleep and power loss, and holds [`CAPACITY`] reports: 24 h at one wake
//!   every 15 minutes. When a report arrives with [`CAPACITY`] buffered, the oldest is dropped.
//! - The region is up to [`MAX_SECTORS`] sectors of [`SLOTS_PER_SECTOR`] record slots
//!   ([`crate::report`]). A report is written to a free slot and never rewritten; an
//!   acknowledgement clears bits of its live word. Order is by `report_seq`, not by position.
//! - Space comes back one sector at a time: the reports of the sector with the most dead slots
//!   are first copied to free slots elsewhere, then the sector is erased. The buffer starts that
//!   while a whole sector's worth of slots is still free, so the copies always fit. A power cut
//!   between the copy and the erase leaves two copies of a report, which read as one.
//! - Every write is a plain NOR program of whole 4-byte words onto erased bytes, or of one live
//!   word that only clears bits. The flash adapter must program only the bytes it is given: one
//!   that rewrites the sector (read, erase, write back) could lose every report in it on power
//!   loss.
//!
//! [`ReportBuffer`] is the index of the region, built by [`ReportBuffer::open`] on each wake. It
//! does not own the flash: every operation takes it, so the Node can use one partition at a time.

use core::fmt;

use coldframe_hal::{Flash, FlashError};

use crate::report::{LIVE_OFFSET, RECORD_SIZE, REPORT_BIT, Report, live_bits};

/// Size of one sector, as on the ESP32-S3.
pub const SECTOR_SIZE: u32 = 4096;

/// Record slots per sector.
pub const SLOTS_PER_SECTOR: usize = SECTOR_SIZE as usize / RECORD_SIZE;

/// The most sectors the buffer uses.
pub const MAX_SECTORS: usize = 8;

/// The fewest sectors the buffer works with: room for [`CAPACITY`] reports, the reserve of one
/// sector, and a sector of acknowledged records waiting to be erased.
pub const MIN_SECTORS: usize = 5;

/// The most record slots.
pub const MAX_SLOTS: usize = MAX_SECTORS * SLOTS_PER_SECTOR;

/// How many wake reports the buffer holds: 24 h at 15 minutes.
pub const CAPACITY: usize = 96;

/// Free slots kept back so the reports of any one sector can be copied out of it.
const RESERVE: usize = SLOTS_PER_SECTOR;

/// How many slots an append tries when a write does not read back.
const WRITE_ATTEMPTS: usize = 3;

/// Why a buffer operation failed.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum BufferError {
    /// The flash failed.
    Flash(FlashError),
    /// The region is smaller than [`MIN_SECTORS`] sectors.
    TooSmall,
    /// A record read back differently from what was written.
    NotVerified,
    /// No slot is free and no sector can be reclaimed.
    Full,
}

impl From<FlashError> for BufferError {
    fn from(error: FlashError) -> Self {
        Self::Flash(error)
    }
}

impl fmt::Display for BufferError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Flash(error) => write!(f, "report buffer flash: {error}"),
            Self::TooSmall => f.write_str("report buffer region too small"),
            Self::NotVerified => f.write_str("report record not verified after write"),
            Self::Full => f.write_str("report buffer has no free slot"),
        }
    }
}

impl core::error::Error for BufferError {}

/// What one slot holds.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Slot {
    /// Erased.
    Free,
    /// An acknowledged or dropped record, or a torn write.
    Dead,
    /// A record with at least one live bit.
    Live {
        /// Its `report_seq`.
        seq: u64,
        /// Its live bits.
        live: u8,
    },
}

/// The index of the report buffer region.
#[derive(Clone)]
pub struct ReportBuffer {
    sectors: usize,
    slots: [Slot; MAX_SLOTS],
}

fn slot_offset(index: usize) -> u32 {
    // At most 256 slots of 128 bytes.
    (index * RECORD_SIZE) as u32
}

fn read_slot<F: Flash>(flash: &mut F, index: usize) -> Result<[u8; RECORD_SIZE], BufferError> {
    let mut record = [0u8; RECORD_SIZE];
    flash.read(slot_offset(index), &mut record)?;
    Ok(record)
}

impl ReportBuffer {
    /// Reads every slot of the region and builds the index.
    ///
    /// # Errors
    ///
    /// [`BufferError::TooSmall`] or [`BufferError::Flash`].
    pub fn open<F: Flash>(flash: &mut F) -> Result<Self, BufferError> {
        let sectors = (flash.capacity() / SECTOR_SIZE as usize).min(MAX_SECTORS);
        if sectors < MIN_SECTORS {
            return Err(BufferError::TooSmall);
        }
        let mut slots = [Slot::Free; MAX_SLOTS];
        for (index, slot) in slots
            .iter_mut()
            .enumerate()
            .take(sectors * SLOTS_PER_SECTOR)
        {
            let record = read_slot(flash, index)?;
            *slot = if record.iter().all(|&byte| byte == 0xFF) {
                Slot::Free
            } else {
                match Report::decode(&record) {
                    Some((report, live)) if live != 0 => Slot::Live {
                        seq: report.report_seq,
                        live,
                    },
                    _ => Slot::Dead,
                }
            };
        }
        Ok(Self { sectors, slots })
    }

    fn slot_count(&self) -> usize {
        self.sectors * SLOTS_PER_SECTOR
    }

    fn used(&self) -> &[Slot] {
        &self.slots[..self.slot_count()]
    }

    fn free(&self) -> usize {
        self.used()
            .iter()
            .filter(|slot| **slot == Slot::Free)
            .count()
    }

    /// The oldest buffered report above `after` (or the oldest of all): its `report_seq`, and
    /// the live bits every copy of it agrees on.
    fn oldest_after(&self, after: Option<u64>) -> Option<(u64, u8)> {
        let mut oldest: Option<(u64, u8)> = None;
        for slot in self.used() {
            let Slot::Live { seq, live } = *slot else {
                continue;
            };
            if after.is_some_and(|after| seq <= after) {
                continue;
            }
            oldest = match oldest {
                Some((best, bits)) if best == seq => Some((best, bits & live)),
                Some((best, _)) if best < seq => oldest,
                _ => Some((seq, live)),
            };
        }
        oldest
    }

    /// How many reports are buffered.
    #[must_use]
    pub fn len(&self) -> usize {
        let mut count = 0;
        let mut after = None;
        while let Some((seq, _)) = self.oldest_after(after) {
            count += 1;
            after = Some(seq);
        }
        count
    }

    /// Whether no report is buffered.
    #[must_use]
    pub fn is_empty(&self) -> bool {
        self.oldest_after(None).is_none()
    }

    /// The oldest buffered report whose `report_seq` is above `after`, or the oldest of all for
    /// `None`, with the Readings not yet acknowledged.
    ///
    /// # Errors
    ///
    /// [`BufferError::Flash`].
    pub fn next<F: Flash>(
        &self,
        flash: &mut F,
        after: Option<u64>,
    ) -> Result<Option<Report>, BufferError> {
        let mut after = after;
        while let Some((seq, live)) = self.oldest_after(after) {
            after = Some(seq);
            for (index, slot) in self.used().iter().enumerate() {
                if !matches!(slot, Slot::Live { seq: held, .. } if *held == seq) {
                    continue;
                }
                if let Some((mut report, _)) = Report::decode(&read_slot(flash, index)?) {
                    report.retain_live(live);
                    return Ok(Some(report));
                }
            }
            // No copy of it reads back any more: skip it.
        }
        Ok(None)
    }

    /// Buffers `report`. With [`CAPACITY`] reports buffered, the oldest is dropped first; returns
    /// how many were dropped. A report whose `report_seq` is already buffered is not added again.
    ///
    /// # Errors
    ///
    /// See [`BufferError`]. On error the reports already buffered are kept.
    pub fn append<F: Flash>(&mut self, flash: &mut F, report: &Report) -> Result<u32, BufferError> {
        let held =
            |slot: &Slot| matches!(slot, Slot::Live { seq, .. } if *seq == report.report_seq);
        if self.used().iter().any(held) {
            return Ok(0);
        }
        let mut dropped = 0;
        while self.len() >= CAPACITY {
            self.drop_oldest(flash)?;
            dropped += 1;
        }
        while self.free() <= RESERVE {
            if !self.reclaim(flash)? {
                break;
            }
        }
        let record = report.encode();
        for _ in 0..WRITE_ATTEMPTS {
            let index = self
                .used()
                .iter()
                .position(|slot| *slot == Slot::Free)
                .ok_or(BufferError::Full)?;
            // Whatever becomes of the write, the slot is no longer erased.
            self.slots[index] = Slot::Dead;
            flash.write(slot_offset(index), &record)?;
            if read_slot(flash, index)? == record {
                self.slots[index] = Slot::Live {
                    seq: report.report_seq,
                    live: report.present_bits(),
                };
                return Ok(dropped);
            }
        }
        Err(BufferError::NotVerified)
    }

    /// Deletes what an authentic downlink acknowledged: every buffered Reading whose
    /// `reading_seq`, and every report whose `report_seq`, lies in `first..=last`. Returns how
    /// many were deleted; acknowledging something already gone does nothing.
    ///
    /// # Errors
    ///
    /// [`BufferError::Flash`]. Whatever was deleted before the failure stays deleted; the rest
    /// is sent again.
    pub fn ack<F: Flash>(
        &mut self,
        flash: &mut F,
        first: u64,
        last: u64,
    ) -> Result<u32, BufferError> {
        let mut deleted = 0;
        for index in 0..self.slot_count() {
            let Slot::Live { seq, live } = self.slots[index] else {
                continue;
            };
            // A report_seq is above every reading_seq of its report.
            if seq < first {
                continue;
            }
            let Some((report, _)) = Report::decode(&read_slot(flash, index)?) else {
                self.slots[index] = Slot::Dead;
                continue;
            };
            let acked = |seq: u64| (first..=last).contains(&seq);
            let mut clear = 0u8;
            for (bit, reading) in report.readings.iter().enumerate() {
                if acked(reading.seq) {
                    clear |= 1 << bit;
                }
            }
            if acked(seq) {
                clear |= REPORT_BIT;
            }
            clear &= live;
            if clear != 0 {
                self.clear_bits(flash, index, clear)?;
                deleted += clear.count_ones();
            }
        }
        Ok(deleted)
    }

    /// Clears live bits of one slot: one aligned word write that only clears bits.
    fn clear_bits<F: Flash>(
        &mut self,
        flash: &mut F,
        index: usize,
        bits: u8,
    ) -> Result<(), BufferError> {
        let Slot::Live { seq, live } = self.slots[index] else {
            return Ok(());
        };
        let offset = slot_offset(index) + LIVE_OFFSET as u32;
        let mut word = [0u8; 4];
        flash.read(offset, &mut word)?;
        word[0] &= !bits;
        flash.write(offset, &word)?;
        let live = live & !bits;
        self.slots[index] = if live == 0 {
            Slot::Dead
        } else {
            Slot::Live { seq, live }
        };
        Ok(())
    }

    /// Drops the oldest report, every copy of it.
    fn drop_oldest<F: Flash>(&mut self, flash: &mut F) -> Result<(), BufferError> {
        let Some((oldest, _)) = self.oldest_after(None) else {
            return Ok(());
        };
        for index in 0..self.slot_count() {
            if let Slot::Live { seq, live } = self.slots[index]
                && seq == oldest
            {
                self.clear_bits(flash, index, live)?;
            }
        }
        Ok(())
    }

    fn sector_slots(sector: usize) -> core::ops::Range<usize> {
        sector * SLOTS_PER_SECTOR..(sector + 1) * SLOTS_PER_SECTOR
    }

    /// Whether the report in `index` must be copied before its sector is erased: no other copy
    /// outside the sector, and the first copy inside it.
    fn must_move(&self, sector: usize, index: usize) -> bool {
        let Slot::Live { seq, .. } = self.slots[index] else {
            return false;
        };
        let slots = Self::sector_slots(sector);
        !self.used().iter().enumerate().any(|(other, slot)| {
            matches!(slot, Slot::Live { seq: held, .. } if *held == seq)
                && (!slots.contains(&other) || other < index)
        })
    }

    /// Erases the sector that gives the most slots back, after copying its reports elsewhere.
    /// Returns whether a sector was reclaimed.
    fn reclaim<F: Flash>(&mut self, flash: &mut F) -> Result<bool, BufferError> {
        let free = self.free();
        let mut best: Option<(usize, usize)> = None;
        for sector in 0..self.sectors {
            let slots = Self::sector_slots(sector);
            let free_here = self.slots[slots.clone()]
                .iter()
                .filter(|slot| **slot == Slot::Free)
                .count();
            let moving = slots
                .clone()
                .filter(|index| self.must_move(sector, *index))
                .count();
            if moving > free - free_here {
                continue;
            }
            let gain = SLOTS_PER_SECTOR - free_here - moving;
            if gain > 0 && best.is_none_or(|(_, most)| gain > most) {
                best = Some((sector, gain));
            }
        }
        let Some((sector, _)) = best else {
            return Ok(false);
        };
        let slots = Self::sector_slots(sector);
        for index in slots.clone() {
            if !self.must_move(sector, index) {
                continue;
            }
            let Slot::Live { seq, .. } = self.slots[index] else {
                continue;
            };
            let record = read_slot(flash, index)?;
            let target = self
                .used()
                .iter()
                .enumerate()
                .position(|(other, slot)| *slot == Slot::Free && !slots.contains(&other))
                .ok_or(BufferError::Full)?;
            self.slots[target] = Slot::Dead;
            flash.write(slot_offset(target), &record)?;
            if read_slot(flash, target)? != record {
                return Err(BufferError::NotVerified);
            }
            // The live bits as they are in flash: the copy carries the same word.
            let live = match Report::decode(&record) {
                Some((_, live)) => live,
                None => live_bits(&record),
            };
            self.slots[target] = if live == 0 {
                Slot::Dead
            } else {
                Slot::Live { seq, live }
            };
        }
        // Slot offsets fit u32: at most eight sectors.
        flash.erase((sector as u32) * SECTOR_SIZE, SECTOR_SIZE)?;
        self.slots[slots].fill(Slot::Free);
        Ok(true)
    }
}
