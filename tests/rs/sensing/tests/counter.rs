//! The reserved counter: reservation before use, resets, torn records, rollover, corruption.

mod common;

use std::ops::Range;

use coldframe_hal::mock::MockFlash;
use coldframe_hal::{Flash, FlashError};
use coldframe_sensing::counter::{
    BOOT_MAGIC, SECTOR_SIZE, SEQ_MAGIC, SLOT_SIZE, SLOTS_PER_SECTOR, decode_record, encode_record,
};
use coldframe_sensing::{CounterError, ReservedCounter};
use common::{COUNTER_SIZE, erased};

/// Writes a valid record for `ceiling` into `slot` of `sector`.
fn put(flash: &mut MockFlash, sector: u32, slot: u32, ceiling: u64) {
    let record = encode_record(SEQ_MAGIC, ceiling);
    let offset = (sector * SECTOR_SIZE) as usize + slot as usize * SLOT_SIZE;
    flash.contents_mut()[offset..offset + SLOT_SIZE].copy_from_slice(&record);
}

/// The raw slot `slot` of `sector`.
fn slot(flash: &MockFlash, sector: u32, slot: u32) -> [u8; SLOT_SIZE] {
    let offset = (sector * SECTOR_SIZE) as usize + slot as usize * SLOT_SIZE;
    flash.contents()[offset..offset + SLOT_SIZE]
        .try_into()
        .unwrap()
}

/// The ceiling a fresh boot reads from `flash`.
fn stored(flash: &MockFlash) -> Result<u64, CounterError> {
    let mut copy = MockFlash::new(COUNTER_SIZE);
    copy.contents_mut().copy_from_slice(flash.contents());
    ReservedCounter::new(copy, SEQ_MAGIC).current()
}

#[test]
fn a_record_has_the_documented_layout() {
    let record = encode_record(*b"CFSQ", 0x0102_0304_0506_0708);
    assert_eq!(&record[0..4], b"CFSQ");
    assert_eq!(record[4], 0x01);
    assert_eq!(&record[5..8], &[0, 0, 0]);
    assert_eq!(&record[8..16], &[1, 2, 3, 4, 5, 6, 7, 8]);
    assert_eq!(&record[20..32], &[0xFF; 12]);
    // The check is SHA-256 over the first 16 bytes; flipping any of them breaks it.
    assert_eq!(
        decode_record(*b"CFSQ", &record),
        Some(0x0102_0304_0506_0708)
    );
    for index in 0..20 {
        let mut broken = record;
        broken[index] ^= 0x01;
        assert_eq!(decode_record(*b"CFSQ", &broken), None, "byte {index}");
    }
    let mut trailing = record;
    trailing[31] = 0x00;
    assert_eq!(decode_record(*b"CFSQ", &trailing), None);
    assert_eq!(decode_record(*b"CFBT", &record), None);
}

#[test]
fn a_fresh_partition_starts_at_zero() {
    let mut counter = ReservedCounter::new(erased(), SEQ_MAGIC);
    assert_eq!(counter.current(), Ok(0));
    assert_eq!(counter.reserve(4), Ok(0..4));
    assert_eq!(counter.current(), Ok(4));
}

#[test]
fn a_reservation_starts_at_the_stored_ceiling_and_persists_the_new_one() {
    let mut flash = erased();
    put(&mut flash, 0, 0, 40);
    let mut counter = ReservedCounter::new(flash, SEQ_MAGIC);
    assert_eq!(counter.reserve(4), Ok(40..44));
    let flash = counter.into_inner();
    assert_eq!(stored(&flash), Ok(44));
    assert_eq!(decode_record(SEQ_MAGIC, &slot(&flash, 0, 1)), Some(44));
}

#[test]
fn reservations_in_one_boot_follow_each_other() {
    let mut counter = ReservedCounter::new(erased(), SEQ_MAGIC);
    assert_eq!(counter.reserve(3), Ok(0..3));
    assert_eq!(counter.reserve(1), Ok(3..4));
    assert_eq!(counter.reserve(4), Ok(4..8));
}

#[test]
fn reserving_nothing_writes_nothing() {
    let mut counter = ReservedCounter::new(erased(), SEQ_MAGIC);
    assert_eq!(counter.reserve(0), Ok(0..0));
    assert_eq!(counter.into_inner().write_count(), 0);
}

#[test]
fn a_failed_write_hands_out_nothing() {
    let mut flash = erased();
    put(&mut flash, 0, 0, 40);
    flash.fail_with(Some(FlashError::Storage));
    let mut counter = ReservedCounter::new(flash, SEQ_MAGIC);
    assert_eq!(
        counter.reserve(4),
        Err(CounterError::Flash(FlashError::Storage))
    );
}

/// A flash whose writes fail inside `failing`, while reads and erases work.
struct FailingWrites {
    inner: MockFlash,
    failing: Range<u32>,
}

impl Flash for FailingWrites {
    fn capacity(&self) -> usize {
        self.inner.capacity()
    }

    fn read(&mut self, offset: u32, buffer: &mut [u8]) -> Result<(), FlashError> {
        self.inner.read(offset, buffer)
    }

    fn write(&mut self, offset: u32, data: &[u8]) -> Result<(), FlashError> {
        if self.failing.contains(&offset) {
            return Err(FlashError::Storage);
        }
        self.inner.write(offset, data)
    }

    fn erase(&mut self, offset: u32, length: u32) -> Result<(), FlashError> {
        self.inner.erase(offset, length)
    }
}

#[test]
fn a_write_that_does_not_read_back_hands_out_nothing() {
    let mut flash = erased();
    put(&mut flash, 0, 0, 40);
    flash.ignore_writes(true);
    let mut counter = ReservedCounter::new(flash, SEQ_MAGIC);
    assert_eq!(counter.reserve(4), Err(CounterError::NotVerified));
    let mut flash = counter.into_inner();
    flash.ignore_writes(false);
    // The next boot starts at the old ceiling: nothing below 44 was handed out.
    let mut counter = ReservedCounter::new(flash, SEQ_MAGIC);
    assert_eq!(counter.reserve(4), Ok(40..44));
}

#[test]
fn after_a_failed_write_the_next_reservation_uses_a_fresh_slot() {
    let mut flash = erased();
    put(&mut flash, 0, 0, 40);
    let slot_one = SLOT_SIZE as u32;
    let mut counter = ReservedCounter::new(
        FailingWrites {
            inner: flash,
            failing: slot_one..slot_one + 1,
        },
        SEQ_MAGIC,
    );
    assert_eq!(
        counter.reserve(4),
        Err(CounterError::Flash(FlashError::Storage))
    );
    assert_eq!(counter.reserve(4), Ok(40..44));
    let flash = counter.into_inner().inner;
    assert_eq!(decode_record(SEQ_MAGIC, &slot(&flash, 0, 2)), Some(44));
    assert_eq!(stored(&flash), Ok(44));
}

#[test]
fn values_never_repeat_across_resets() {
    let mut flash = erased();
    let mut issued = Vec::new();
    // 300 boots of four Readings each: more than two whole sectors, so both rollovers are crossed.
    for boot in 0..300_u64 {
        let mut counter = ReservedCounter::new(flash, SEQ_MAGIC);
        let n = 1 + boot % 4;
        issued.extend(counter.reserve(n).expect("reserve"));
        flash = counter.into_inner();
    }
    assert!(issued.windows(2).all(|pair| pair[0] < pair[1]));
    assert_eq!(issued.first(), Some(&0));
    assert!(flash.erase_count() >= 2);
}

#[test]
fn values_never_repeat_when_every_boot_loses_power_after_the_reservation() {
    // A boot that reserves and dies before using the values leaves a gap, never a repeat.
    let mut flash = erased();
    let mut last_end = 0;
    for _ in 0..50 {
        let mut counter = ReservedCounter::new(flash, SEQ_MAGIC);
        let range = counter.reserve(4).expect("reserve");
        assert!(range.start >= last_end);
        last_end = range.end;
        flash = counter.into_inner();
    }
}

#[test]
fn a_torn_last_record_is_ignored() {
    let mut flash = erased();
    put(&mut flash, 0, 0, 10);
    put(&mut flash, 0, 1, 20);
    // Slot 2 lost power halfway: the first ten bytes of a record for 30.
    let torn = encode_record(SEQ_MAGIC, 30);
    let offset = 2 * SLOT_SIZE;
    flash.contents_mut()[offset..offset + 10].copy_from_slice(&torn[..10]);
    let mut counter = ReservedCounter::new(flash, SEQ_MAGIC);
    assert_eq!(counter.current(), Ok(20));
    assert_eq!(counter.reserve(4), Ok(20..24));
    let flash = counter.into_inner();
    // The torn slot is left alone; the new record goes to the next erased slot.
    assert_eq!(&slot(&flash, 0, 2)[..10], &torn[..10]);
    assert_eq!(decode_record(SEQ_MAGIC, &slot(&flash, 0, 3)), Some(24));
}

#[test]
fn the_highest_valid_record_of_either_sector_wins() {
    let mut flash = erased();
    for index in 0..SLOTS_PER_SECTOR {
        put(&mut flash, 0, index, u64::from(index) + 1);
    }
    put(&mut flash, 1, 0, 200);
    let mut counter = ReservedCounter::new(flash, SEQ_MAGIC);
    assert_eq!(counter.current(), Ok(200));
    assert_eq!(counter.reserve(1), Ok(200..201));
    let flash = counter.into_inner();
    assert_eq!(decode_record(SEQ_MAGIC, &slot(&flash, 1, 1)), Some(201));
    assert_eq!(flash.erase_count(), 0);
}

#[test]
fn a_full_sector_rolls_over_by_erasing_the_other_one() {
    let mut flash = erased();
    // Sector 1 holds old records; sector 0 is full and newer.
    put(&mut flash, 1, 0, 3);
    put(&mut flash, 1, 1, 5);
    for index in 0..SLOTS_PER_SECTOR {
        put(&mut flash, 0, index, 10 + u64::from(index));
    }
    let before = slot(&flash, 0, SLOTS_PER_SECTOR - 1);
    let mut counter = ReservedCounter::new(flash, SEQ_MAGIC);
    let top = 10 + u64::from(SLOTS_PER_SECTOR) - 1;
    assert_eq!(counter.reserve(4), Ok(top..top + 4));
    let flash = counter.into_inner();
    assert_eq!(flash.erase_count(), 1);
    assert_eq!(decode_record(SEQ_MAGIC, &slot(&flash, 1, 0)), Some(top + 4));
    assert_eq!(slot(&flash, 1, 1), [0xFF; SLOT_SIZE]);
    // The old sector is untouched until the next rollover.
    assert_eq!(slot(&flash, 0, SLOTS_PER_SECTOR - 1), before);
    assert_eq!(stored(&flash), Ok(top + 4));
}

#[test]
fn a_rollover_that_loses_power_after_the_erase_keeps_the_old_ceiling() {
    let mut flash = erased();
    for index in 0..SLOTS_PER_SECTOR {
        put(&mut flash, 0, index, 100 + u64::from(index));
    }
    let top = 100 + u64::from(SLOTS_PER_SECTOR) - 1;
    // The write into the freshly erased sector 1 never lands.
    let mut counter = ReservedCounter::new(
        FailingWrites {
            inner: flash,
            failing: SECTOR_SIZE..2 * SECTOR_SIZE,
        },
        SEQ_MAGIC,
    );
    assert!(counter.reserve(4).is_err());
    let flash = counter.into_inner().inner;
    // Sector 1 is erased and empty, sector 0 still holds the ceiling: never both invalid.
    assert_eq!(stored(&flash), Ok(top));
    let mut counter = ReservedCounter::new(flash, SEQ_MAGIC);
    assert_eq!(counter.reserve(4), Ok(top..top + 4));
}

#[test]
fn an_interrupted_erase_of_the_other_sector_is_harmless() {
    let mut flash = erased();
    for index in 0..SLOTS_PER_SECTOR {
        put(&mut flash, 0, index, 500 + u64::from(index));
    }
    // Sector 1 half-erased: garbage in its first slots.
    let start = SECTOR_SIZE as usize;
    flash.contents_mut()[start..start + 100].fill(0x5A);
    let top = 500 + u64::from(SLOTS_PER_SECTOR) - 1;
    let mut counter = ReservedCounter::new(flash, SEQ_MAGIC);
    assert_eq!(counter.current(), Ok(top));
    assert_eq!(counter.reserve(1), Ok(top..top + 1));
    let flash = counter.into_inner();
    assert_eq!(decode_record(SEQ_MAGIC, &slot(&flash, 1, 0)), Some(top + 1));
}

#[test]
fn written_slots_without_a_valid_record_are_corrupt() {
    let mut flash = erased();
    flash.contents_mut()[0..SLOT_SIZE].fill(0x00);
    flash.contents_mut()[SECTOR_SIZE as usize + 64] = 0x12;
    let mut counter = ReservedCounter::new(flash, SEQ_MAGIC);
    assert_eq!(counter.current(), Err(CounterError::Corrupt));
    assert_eq!(counter.reserve(4), Err(CounterError::Corrupt));
    assert_eq!(counter.into_inner().write_count(), 0);
}

#[test]
fn a_single_torn_record_on_its_own_is_corrupt_not_zero() {
    let mut flash = erased();
    let torn = encode_record(SEQ_MAGIC, 7);
    flash.contents_mut()[..12].copy_from_slice(&torn[..12]);
    let mut counter = ReservedCounter::new(flash, SEQ_MAGIC);
    assert_eq!(counter.reserve(1), Err(CounterError::Corrupt));
}

#[test]
fn records_of_another_counter_do_not_count() {
    let mut flash = erased();
    let other = encode_record(BOOT_MAGIC, 99);
    flash.contents_mut()[..SLOT_SIZE].copy_from_slice(&other);
    let mut counter = ReservedCounter::new(flash, SEQ_MAGIC);
    assert_eq!(counter.current(), Err(CounterError::Corrupt));
}

#[test]
fn an_unreadable_partition_is_an_error_not_zero() {
    let mut flash = erased();
    flash.fail_with(Some(FlashError::Storage));
    let mut counter = ReservedCounter::new(flash, SEQ_MAGIC);
    assert_eq!(
        counter.current(),
        Err(CounterError::Flash(FlashError::Storage))
    );
}

#[test]
fn a_region_smaller_than_two_sectors_is_refused() {
    let mut counter = ReservedCounter::new(MockFlash::new(4096), SEQ_MAGIC);
    assert_eq!(counter.current(), Err(CounterError::TooSmall));
}

#[test]
fn the_counter_refuses_to_wrap() {
    let mut flash = erased();
    put(&mut flash, 0, 0, u64::MAX - 1);
    let mut counter = ReservedCounter::new(flash, SEQ_MAGIC);
    assert_eq!(counter.reserve(2), Err(CounterError::Exhausted));
    assert_eq!(counter.reserve(1), Ok(u64::MAX - 1..u64::MAX));
}
