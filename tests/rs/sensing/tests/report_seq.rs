//! `issue_report_seq`: every wake report gets a `report_seq` from the `reading_seq` counter,
//! above the Readings of its wake, also when the wake has no Reading (Story 4.4).

mod common;

use coldframe_hal::FlashError;
use coldframe_hal::mock::MockFlash;
use coldframe_sensing::counter::{CounterError, SEQ_MAGIC, SLOT_SIZE, encode_record};
use coldframe_sensing::{
    ChargeStatus, Faults, MeasuredAt, Reading, ReadingValue, ReservedCounter, WakeOutcome,
    WakeReport, issue_report_seq,
};
use common::erased;

/// A seq partition whose stored ceiling is `ceiling`.
fn ceiling(ceiling: u64) -> MockFlash {
    let mut flash = erased();
    flash.contents_mut()[..SLOT_SIZE].copy_from_slice(&encode_record(SEQ_MAGIC, ceiling));
    flash
}

/// The outcome of a wake that issued `readings` Readings from `first` on.
fn outcome(first: u64, readings: u64) -> WakeOutcome {
    let mut report = WakeReport {
        measured_at: MeasuredAt::Unsynced {
            boot_id: 1,
            uptime_ms: 5,
        },
        readings: Default::default(),
        battery: None,
        charging: ChargeStatus::Unknown,
        report_seq: None,
    };
    for seq in first..first + readings {
        report
            .readings
            .push(Reading {
                slot: 0,
                seq,
                value: ReadingValue::SoilRaw(1873),
            })
            .unwrap();
    }
    WakeOutcome {
        report,
        faults: Faults::default(),
        sleep_ms: 900_000,
    }
}

#[test]
fn the_report_seq_follows_the_readings_of_the_wake() {
    // The wake issued Readings 40 to 43, so the stored ceiling is 44.
    let mut seq = ReservedCounter::new(ceiling(44), SEQ_MAGIC);
    let mut outcome = outcome(40, 4);
    assert_eq!(issue_report_seq(&mut outcome, &mut seq), Ok(44));
    assert_eq!(outcome.report.report_seq, Some(44));
    assert!(outcome.faults.is_empty());
    // The Readings are untouched, and the ceiling is in flash before the value is used.
    assert_eq!(outcome.report.seq_range(), Some(40..44));
    assert_eq!(
        ReservedCounter::new(seq.into_inner(), SEQ_MAGIC).current(),
        Ok(45)
    );
}

#[test]
fn a_wake_without_readings_still_gets_a_report_seq() {
    let mut seq = ReservedCounter::new(ceiling(44), SEQ_MAGIC);
    let mut outcome = outcome(0, 0);
    assert_eq!(outcome.report.seq_range(), None);
    assert_eq!(issue_report_seq(&mut outcome, &mut seq), Ok(44));
    assert_eq!(outcome.report.report_seq, Some(44));

    // A fresh Device: the first report_seq may be 0.
    let mut fresh = ReservedCounter::new(erased(), SEQ_MAGIC);
    let mut first = self::outcome(0, 0);
    assert_eq!(issue_report_seq(&mut first, &mut fresh), Ok(0));
    assert_eq!(first.report.report_seq, Some(0));
}

#[test]
fn issuing_twice_keeps_the_value_and_reserves_nothing_more() {
    let mut seq = ReservedCounter::new(ceiling(10), SEQ_MAGIC);
    let mut outcome = outcome(6, 4);
    assert_eq!(issue_report_seq(&mut outcome, &mut seq), Ok(10));
    assert_eq!(issue_report_seq(&mut outcome, &mut seq), Ok(10));
    assert_eq!(seq.current(), Ok(11));
}

#[test]
fn consecutive_wakes_never_share_a_seq() {
    let mut seq = ReservedCounter::new(erased(), SEQ_MAGIC);
    let mut used = Vec::new();
    for _ in 0..3 {
        // A wake: four Readings, then the report.
        let range = seq.reserve(4).unwrap();
        let mut outcome = outcome(range.start, 4);
        let report_seq = issue_report_seq(&mut outcome, &mut seq).unwrap();
        used.extend(range);
        used.push(report_seq);
        // A deep-sleep wake is a reboot: a new counter over the same flash.
        seq = ReservedCounter::new(seq.into_inner(), SEQ_MAGIC);
    }
    assert_eq!(used, (0..15).collect::<Vec<u64>>());
}

#[test]
fn a_counter_fault_leaves_the_report_without_a_report_seq() {
    let mut flash = ceiling(44);
    flash.ignore_writes(true);
    let mut seq = ReservedCounter::new(flash, SEQ_MAGIC);
    let mut outcome = outcome(40, 4);
    assert_eq!(
        issue_report_seq(&mut outcome, &mut seq),
        Err(CounterError::NotVerified)
    );
    assert_eq!(outcome.report.report_seq, None);
    assert_eq!(outcome.faults.counter, Some(CounterError::NotVerified));

    // A fault the wake already recorded is kept.
    let mut flash = ceiling(44);
    flash.fail_with(Some(FlashError::Storage));
    let mut seq = ReservedCounter::new(flash, SEQ_MAGIC);
    let mut outcome = self::outcome(0, 0);
    outcome.faults.counter = Some(CounterError::Corrupt);
    assert!(issue_report_seq(&mut outcome, &mut seq).is_err());
    assert_eq!(outcome.faults.counter, Some(CounterError::Corrupt));
    assert_eq!(outcome.report.report_seq, None);
}
