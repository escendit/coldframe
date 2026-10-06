//! The report buffer: order, acknowledgement by `reading_seq` range, overflow, sector reclaim,
//! and power loss at every write and erase.

mod common;

use coldframe_hal::mock::MockFlash;
use coldframe_hal::{Flash, FlashError};
use coldframe_sensing::{ChargeStatus, MeasuredAt};
use coldframe_transport::buffer::{
    BufferError, CAPACITY, MAX_SECTORS, MIN_SECTORS, SECTOR_SIZE, SLOTS_PER_SECTOR,
};
use coldframe_transport::report::RECORD_SIZE;
use coldframe_transport::{Report, ReportBuffer};
use common::{BUFFER_SIZE, Flashes, buffered, report};

const SYNCED: MeasuredAt = MeasuredAt::Synced {
    unix_ms: 1_790_000_000_000,
};

/// The report of wake `index`: Readings `5 * index..5 * index + 4`, report_seq `5 * index + 4`.
fn wake_report(index: u64) -> Report {
    report(5 * index, SYNCED)
}

fn seq(index: u64) -> u64 {
    5 * index + 4
}

/// A copy of a flash region, as it is at this moment.
fn snapshot(flash: &MockFlash) -> MockFlash {
    let mut copy = MockFlash::new(flash.capacity());
    copy.contents_mut().copy_from_slice(flash.contents());
    copy
}

#[test]
fn the_layout_holds_a_day_of_reports() {
    assert_eq!(CAPACITY, 96);
    assert_eq!(RECORD_SIZE, 128);
    assert_eq!(SLOTS_PER_SECTOR, 32);
    assert_eq!(BUFFER_SIZE, MAX_SECTORS * SECTOR_SIZE as usize);
    // The capacity, the reserve of one sector and a sector to reclaim fit the smallest region.
    const { assert!(CAPACITY + 2 * SLOTS_PER_SECTOR <= MIN_SECTORS * SLOTS_PER_SECTOR) };
}

#[test]
fn a_report_round_trips_through_its_record() {
    for stored in [
        wake_report(7),
        Report {
            report_seq: u64::MAX,
            measured_at: MeasuredAt::Unsynced {
                boot_id: 9,
                uptime_ms: 123_456_789,
            },
            readings: heapless::Vec::new(),
            battery_percent: None,
            charging: ChargeStatus::Unknown,
        },
        Report {
            charging: ChargeStatus::NotCharging,
            battery_percent: Some(0),
            ..wake_report(3)
        },
    ] {
        let record = stored.encode();
        let (decoded, live) = Report::decode(&record).expect("a valid record");
        assert_eq!(decoded, stored);
        assert_eq!(live.count_ones() as usize, stored.readings.len() + 1);
        // One flipped bit of the body fails the check.
        let mut torn = record;
        torn[40] ^= 0x01;
        assert!(Report::decode(&torn).is_none());
    }
    assert!(Report::decode(&[0xFF; RECORD_SIZE]).is_none());
    assert!(Report::decode(&[0x00; RECORD_SIZE]).is_none());
}

#[test]
fn reports_come_back_oldest_first_and_survive_a_reboot() {
    let mut flashes = Flashes::new();
    {
        let mut flash = flashes.buffer();
        let mut buffer = ReportBuffer::open(&mut flash).unwrap();
        assert!(buffer.is_empty());
        // Appended out of order: the order is by report_seq.
        for index in [3, 1, 2] {
            assert_eq!(buffer.append(&mut flash, &wake_report(index)), Ok(0));
        }
        assert_eq!(buffer.len(), 3);
        assert_eq!(buffer.next(&mut flash, None).unwrap(), Some(wake_report(1)));
        assert_eq!(
            buffer.next(&mut flash, Some(seq(1))).unwrap(),
            Some(wake_report(2))
        );
        assert_eq!(buffer.next(&mut flash, Some(seq(3))).unwrap(), None);
        // The same report twice is buffered once.
        assert_eq!(buffer.append(&mut flash, &wake_report(2)), Ok(0));
        assert_eq!(buffer.len(), 3);
    }
    // A new index over the same flash: deep sleep, or a power cut.
    assert_eq!(buffered(&mut flashes), [seq(1), seq(2), seq(3)]);
}

#[test]
fn an_acknowledgement_deletes_exactly_its_ranges() {
    let mut flashes = Flashes::new();
    let mut flash = flashes.buffer();
    let mut buffer = ReportBuffer::open(&mut flash).unwrap();
    for index in 0..3 {
        buffer.append(&mut flash, &wake_report(index)).unwrap();
    }
    // Readings 5..=9 are wake 1 with its report_seq.
    assert_eq!(buffer.ack(&mut flash, 5, 9), Ok(5));
    assert_eq!(buffer.len(), 2);
    // Again: already gone.
    assert_eq!(buffer.ack(&mut flash, 5, 9), Ok(0));
    // Nothing held lies in this range.
    assert_eq!(buffer.ack(&mut flash, 1_000, 2_000), Ok(0));

    // A partial acknowledgement: two Readings of wake 0, not its report_seq.
    assert_eq!(buffer.ack(&mut flash, 1, 2), Ok(2));
    let partial = buffer.next(&mut flash, None).unwrap().unwrap();
    assert_eq!(partial.report_seq, seq(0));
    let left: Vec<u64> = partial.readings.iter().map(|reading| reading.seq).collect();
    assert_eq!(left, [0, 3]);
    assert_eq!(buffer.len(), 2);
    // The rest of it, and the report: now the whole record is gone.
    assert_eq!(buffer.ack(&mut flash, 0, 4), Ok(3));
    assert_eq!(buffer.len(), 1);

    // Only the report_seq of wake 2: its Readings stay, and the record with them.
    assert_eq!(buffer.ack(&mut flash, seq(2), seq(2)), Ok(1));
    let kept = buffer.next(&mut flash, None).unwrap().unwrap();
    assert_eq!(kept.readings.len(), 4);
    // What is deleted stays deleted after a reboot.
    assert_eq!(buffered(&mut flashes), [seq(2)]);
    let mut flash = flashes.buffer();
    let buffer = ReportBuffer::open(&mut flash).unwrap();
    assert_eq!(buffer.next(&mut flash, None).unwrap(), Some(kept));
}

#[test]
fn the_97th_report_drops_the_oldest_and_keeps_the_newest_96_in_order() {
    let mut flashes = Flashes::new();
    let mut flash = flashes.buffer();
    let mut buffer = ReportBuffer::open(&mut flash).unwrap();
    for index in 0..96 {
        assert_eq!(buffer.append(&mut flash, &wake_report(index)), Ok(0));
    }
    assert_eq!(buffer.len(), 96);
    assert_eq!(buffer.append(&mut flash, &wake_report(96)), Ok(1));
    assert_eq!(buffer.len(), 96);
    let expected: Vec<u64> = (1..97).map(seq).collect();
    assert_eq!(buffered(&mut flashes), expected);
}

#[test]
fn a_season_of_reports_reclaims_sectors_without_losing_one() {
    let mut flashes = Flashes::new();
    let mut held: Vec<u64> = Vec::new();
    // 3000 wakes (a month): each buffers one report; most are acknowledged one wake later, every
    // 50th only after a day, and the index is rebuilt on every wake.
    for index in 0..3_000u64 {
        let mut flash = flashes.buffer();
        let mut buffer = ReportBuffer::open(&mut flash).unwrap();
        assert_eq!(buffer.append(&mut flash, &wake_report(index)), Ok(0));
        held.push(index);
        held.retain(|&old| {
            let age = index - old;
            let due = if old % 50 == 0 { age >= 90 } else { age >= 1 };
            if due {
                assert_eq!(buffer.ack(&mut flash, 5 * old, seq(old)), Ok(5));
            }
            !due
        });
        assert_eq!(buffer.len(), held.len());
    }
    let expected: Vec<u64> = held.iter().copied().map(seq).collect();
    assert_eq!(buffered(&mut flashes), expected);
    // 3000 records went through 256 slots: sectors were erased, and none more than its share.
    let erases = flashes.buffer.erase_count();
    assert!((80..=110).contains(&erases), "{erases}");
}

#[test]
fn a_report_that_is_never_acknowledged_outlives_every_reclaim() {
    let mut flashes = Flashes::new();
    let mut flash = flashes.buffer();
    let mut buffer = ReportBuffer::open(&mut flash).unwrap();
    buffer.append(&mut flash, &wake_report(0)).unwrap();
    for index in 1..2_000u64 {
        buffer.append(&mut flash, &wake_report(index)).unwrap();
        assert_eq!(buffer.ack(&mut flash, 5 * index, seq(index)), Ok(5));
    }
    assert_eq!(buffer.len(), 1);
    assert_eq!(buffer.next(&mut flash, None).unwrap(), Some(wake_report(0)));
    assert!(flashes.buffer.erase_count() > 50);
    assert_eq!(buffered(&mut flashes), [seq(0)]);
}

#[test]
fn a_torn_record_and_a_small_region() {
    let mut flashes = Flashes::new();
    // Half a record reached the flash before the power failed.
    let record = wake_report(1).encode();
    flashes.buffer.contents_mut()[..64].copy_from_slice(&record[..64]);
    let mut flash = flashes.buffer();
    let mut buffer = ReportBuffer::open(&mut flash).unwrap();
    assert!(buffer.is_empty(), "a torn record is no report");
    // The slot is not reused before its sector is erased: the next report goes elsewhere.
    buffer.append(&mut flash, &wake_report(2)).unwrap();
    assert_eq!(flashes.buffer.contents()[..64], record[..64]);
    assert_eq!(buffered(&mut flashes), [seq(2)]);

    let mut small = MockFlash::new((MIN_SECTORS - 1) * SECTOR_SIZE as usize);
    assert_eq!(
        ReportBuffer::open(&mut small).err(),
        Some(BufferError::TooSmall)
    );
    let mut smallest = MockFlash::new(MIN_SECTORS * SECTOR_SIZE as usize);
    let mut buffer = ReportBuffer::open(&mut smallest).unwrap();
    for index in 0..300 {
        buffer.append(&mut smallest, &wake_report(index)).unwrap();
    }
    assert_eq!(buffer.len(), CAPACITY);

    let mut failing = MockFlash::new(BUFFER_SIZE);
    failing.fail_with(Some(FlashError::Storage));
    assert_eq!(
        ReportBuffer::open(&mut failing).err(),
        Some(BufferError::Flash(FlashError::Storage))
    );
}

#[test]
fn a_write_that_does_not_reach_the_chip_is_an_error_and_loses_nothing() {
    let mut flashes = Flashes::new();
    {
        let mut flash = flashes.buffer();
        let mut buffer = ReportBuffer::open(&mut flash).unwrap();
        buffer.append(&mut flash, &wake_report(0)).unwrap();
    }
    flashes.buffer.ignore_writes(true);
    {
        let mut flash = flashes.buffer();
        let mut buffer = ReportBuffer::open(&mut flash).unwrap();
        assert_eq!(
            buffer.append(&mut flash, &wake_report(1)),
            Err(BufferError::NotVerified)
        );
        assert_eq!(buffer.len(), 1);
    }
    flashes.buffer.ignore_writes(false);
    assert_eq!(buffered(&mut flashes), [seq(0)]);
}

/// A buffer about to reclaim a sector: 60 reports held (some of them in the sector that will be
/// erased), the rest of the slots dead, and one sector's worth free.
fn before_a_reclaim() -> (MockFlash, Vec<u64>) {
    let mut flash = MockFlash::new(BUFFER_SIZE);
    let mut buffer = ReportBuffer::open(&mut flash).unwrap();
    let mut held = Vec::new();
    let slots = MAX_SECTORS * SLOTS_PER_SECTOR;
    for index in 0..(slots - SLOTS_PER_SECTOR) as u64 {
        buffer.append(&mut flash, &wake_report(index)).unwrap();
        // Every fourth report stays unacknowledged, spread over every sector.
        if index % 4 == 0 && held.len() < 60 {
            held.push(seq(index));
        } else {
            buffer.ack(&mut flash, 5 * index, seq(index)).unwrap();
        }
    }
    assert_eq!(flash.erase_count(), 0, "nothing reclaimed yet");
    (flash, held)
}

#[test]
fn a_power_cut_at_any_write_of_an_append_loses_no_buffered_report() {
    let (start, held) = before_a_reclaim();
    let new = wake_report(1_000);
    let mut cut = 0;
    let mut completed = false;
    while !completed {
        let mut flashes = Flashes::new();
        flashes.buffer = snapshot(&start);
        flashes.power.left = Some(cut);
        let result = {
            let mut flash = flashes.buffer();
            let mut buffer = ReportBuffer::open(&mut flash).unwrap();
            buffer.append(&mut flash, &new)
        };
        completed = !flashes.power.off;
        assert_eq!(result.is_ok(), completed, "cut {cut}");
        if completed {
            // The append took a reclaim: copies, an erase, the record.
            assert!(cut > 3, "{cut}");
            assert_eq!(flashes.buffer.erase_count(), 1);
        }

        // The Node boots again.
        flashes.restore_power();
        let after = buffered(&mut flashes);
        let old: Vec<u64> = after
            .iter()
            .copied()
            .filter(|seq| *seq != new.report_seq)
            .collect();
        assert_eq!(old, held, "cut {cut}: every buffered report is still there");
        assert!(after.len() <= held.len() + 1, "cut {cut}");
        if completed {
            assert_eq!(after.last(), Some(&new.report_seq));
        }

        // And the buffer still works: the next wakes buffer and are acknowledged.
        let mut flash = flashes.buffer();
        let mut buffer = ReportBuffer::open(&mut flash).unwrap();
        for index in 2_000..2_040 {
            buffer.append(&mut flash, &wake_report(index)).unwrap();
            assert_eq!(buffer.ack(&mut flash, 5 * index, seq(index)), Ok(5));
        }
        for held_seq in &held {
            assert_eq!(
                buffer.ack(&mut flash, held_seq - 4, *held_seq),
                Ok(5),
                "cut {cut}"
            );
        }
        buffer
            .ack(&mut flash, new.report_seq - 4, new.report_seq)
            .unwrap();
        assert!(buffer.is_empty(), "cut {cut}");
        cut += 1;
    }
    assert!(cut > 10, "the scenario has many writes: {cut}");
}

#[test]
fn a_power_cut_at_any_write_of_an_overflow_drops_only_the_oldest() {
    // 96 unacknowledged reports; the 97th must drop the oldest and nothing else.
    let mut start = MockFlash::new(BUFFER_SIZE);
    {
        let mut buffer = ReportBuffer::open(&mut start).unwrap();
        for index in 0..96 {
            buffer.append(&mut start, &wake_report(index)).unwrap();
        }
    }
    let all: Vec<u64> = (0..96).map(seq).collect();
    let mut cut = 0;
    loop {
        let mut flashes = Flashes::new();
        flashes.buffer = snapshot(&start);
        flashes.power.left = Some(cut);
        {
            let mut flash = flashes.buffer();
            let mut buffer = ReportBuffer::open(&mut flash).unwrap();
            let _ = buffer.append(&mut flash, &wake_report(96));
        }
        let completed = !flashes.power.off;
        flashes.restore_power();
        let after = buffered(&mut flashes);
        // Only the oldest may be missing, and only the new one may be added.
        let old: Vec<u64> = after.iter().copied().filter(|s| *s != seq(96)).collect();
        assert!(old == all || old == all[1..], "cut {cut}: {old:?}");
        if completed {
            assert_eq!(old, all[1..]);
            assert_eq!(after.last(), Some(&seq(96)));
            break;
        }
        cut += 1;
    }
}

#[test]
fn a_power_cut_at_any_write_of_an_acknowledgement_never_changes_a_report_seq() {
    let mut start = MockFlash::new(BUFFER_SIZE);
    {
        let mut buffer = ReportBuffer::open(&mut start).unwrap();
        for index in 0..4 {
            buffer.append(&mut start, &wake_report(index)).unwrap();
        }
    }
    let mut cut = 0;
    loop {
        let mut flashes = Flashes::new();
        flashes.buffer = snapshot(&start);
        flashes.power.left = Some(cut);
        {
            // One downlink range over wakes 1 and 2.
            let mut flash = flashes.buffer();
            let mut buffer = ReportBuffer::open(&mut flash).unwrap();
            let _ = buffer.ack(&mut flash, 5, 14);
        }
        let completed = !flashes.power.off;
        flashes.restore_power();

        let mut flash = flashes.buffer();
        let mut buffer = ReportBuffer::open(&mut flash).unwrap();
        let mut after = None;
        let mut seen = Vec::new();
        while let Some(found) = buffer.next(&mut flash, after).unwrap() {
            after = Some(found.report_seq);
            // Whatever is left of a report is the report as it was buffered, or a part of it.
            let original = wake_report(found.report_seq / 5);
            assert_eq!(found.report_seq, original.report_seq, "cut {cut}");
            assert_eq!(found.measured_at, original.measured_at);
            assert!(
                found
                    .readings
                    .iter()
                    .all(|reading| original.readings.contains(reading)),
                "cut {cut}"
            );
            seen.push(found.report_seq);
        }
        // The reports outside the range are untouched, whole.
        assert!(
            seen.contains(&seq(0)) && seen.contains(&seq(3)),
            "cut {cut}"
        );
        assert_eq!(buffer.next(&mut flash, None).unwrap(), Some(wake_report(0)));
        if completed {
            assert_eq!(seen, [seq(0), seq(3)]);
            break;
        }
        // The downlink arrives again (the Node resent): now the range is gone for good.
        buffer.ack(&mut flash, 5, 14).unwrap();
        assert_eq!(buffer.len(), 2, "cut {cut}");
        cut += 1;
    }
    assert_eq!(cut, 2, "one live-word write per report in the range");
}
