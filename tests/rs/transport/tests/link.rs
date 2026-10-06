//! The link state: what a Node remembers about its link between wakes.

mod common;

use coldframe_hal::mock::MockFlash;
use coldframe_hal::{Flash, FlashError};
use coldframe_transport::link::{
    LINK_MAGIC, LinkError, SECTOR_SIZE, SLOT_SIZE, SLOTS_PER_SECTOR, decode_record, encode_record,
    load, save,
};
use coldframe_transport::{Burst, ClockSync, LinkState};
use common::{Flashes, LINK_SIZE};

fn full() -> LinkState {
    LinkState {
        channel: Some(11),
        misses: 2,
        scan_holdoff: 3,
        specifications_requested: true,
        burst: Some(Burst {
            first_counter: 0x0102_0304_0506_0708,
            frames: 8,
            acked: 0b1010_0101,
            boot_id: 7,
            uptime_ms: 86_400_123,
        }),
        clock: Some(ClockSync {
            boot_id: 7,
            offset_ms: 1_790_000_000_000,
        }),
    }
}

#[test]
fn a_record_round_trips() {
    assert_eq!(SLOT_SIZE, 64);
    assert_eq!(&LINK_MAGIC, b"CFLK");
    for state in [LinkState::default(), full()] {
        let record = encode_record(&state, 42);
        assert_eq!(decode_record(&record), Some((state, 42)));
        let mut torn = record;
        torn[20] ^= 0x80;
        assert_eq!(decode_record(&torn), None);
    }
    assert_eq!(decode_record(&[0xFF; SLOT_SIZE]), None);
}

#[test]
fn a_burst_knows_its_counters_and_acknowledges_each_once() {
    let mut burst = Burst {
        first_counter: 40,
        frames: 3,
        acked: 0,
        boot_id: 1,
        uptime_ms: 0,
    };
    assert!(!burst.contains(39));
    assert!(burst.contains(40) && burst.contains(42));
    assert!(!burst.contains(43));
    assert!(!burst.acknowledge(43), "not a frame of this burst");
    assert!(burst.acknowledge(41), "the first downlink for it is fresh");
    assert!(!burst.acknowledge(41), "the same one again is not");
    assert!(!burst.is_acknowledged());
    assert!(burst.acknowledge(40) && burst.acknowledge(42));
    assert!(burst.is_acknowledged());
}

#[test]
fn an_erased_region_is_the_default_state_and_a_save_reads_back() {
    let mut flashes = Flashes::new();
    assert_eq!(load(&mut flashes.link()), Ok(LinkState::default()));
    save(&mut flashes.link(), &full()).unwrap();
    assert_eq!(load(&mut flashes.link()), Ok(full()));
    let newer = LinkState {
        channel: Some(1),
        ..full()
    };
    save(&mut flashes.link(), &newer).unwrap();
    assert_eq!(load(&mut flashes.link()), Ok(newer));
    // Two records, appended: nothing was erased.
    assert_eq!(flashes.link.erase_count(), 0);
    assert_eq!(flashes.link.write_count(), 2);
}

#[test]
fn the_state_rolls_over_between_two_sectors() {
    let mut flashes = Flashes::new();
    for wake in 0..1_000u32 {
        let state = LinkState {
            misses: (wake % 200) as u8,
            ..full()
        };
        save(&mut flashes.link(), &state).unwrap();
        assert_eq!(load(&mut flashes.link()), Ok(state), "wake {wake}");
    }
    // 64 records per sector: one erase per 64 saves after the first sector.
    assert_eq!(
        flashes.link.erase_count(),
        1_000 / SLOTS_PER_SECTOR as usize
    );
}

#[test]
fn a_power_cut_at_any_write_keeps_the_previous_state() {
    // Just before a rollover, so the save erases and writes.
    let mut start = MockFlash::new(LINK_SIZE);
    for _ in 0..SLOTS_PER_SECTOR {
        save(&mut start, &full()).unwrap();
    }
    let newer = LinkState {
        channel: Some(3),
        ..full()
    };
    for cut in 0..3 {
        let mut flashes = Flashes::new();
        flashes
            .link
            .contents_mut()
            .copy_from_slice(start.contents());
        flashes.power.left = Some(cut);
        let result = save(&mut flashes.link(), &newer);
        let completed = !flashes.power.off;
        assert_eq!(result.is_ok(), completed, "cut {cut}");
        flashes.restore_power();
        let loaded = load(&mut flashes.link()).unwrap();
        if completed {
            assert_eq!(loaded, newer);
            assert_eq!(cut, 2, "an erase and a write");
        } else {
            assert_eq!(loaded, full(), "cut {cut}");
        }
        // The next save works either way.
        save(&mut flashes.link(), &newer).unwrap();
        assert_eq!(load(&mut flashes.link()), Ok(newer));
    }
}

#[test]
fn garbage_reads_as_the_default_state_and_is_written_over() {
    let mut flashes = Flashes::new();
    flashes.link.contents_mut()[..SLOT_SIZE].fill(0x00);
    flashes.link.contents_mut()[SECTOR_SIZE as usize..][..SLOT_SIZE].fill(0x5A);
    assert_eq!(load(&mut flashes.link()), Ok(LinkState::default()));
    save(&mut flashes.link(), &full()).unwrap();
    assert_eq!(load(&mut flashes.link()), Ok(full()));
}

#[test]
fn a_small_or_failing_region_is_an_error() {
    let mut small = MockFlash::new(SECTOR_SIZE as usize);
    assert_eq!(load(&mut small), Err(LinkError::TooSmall));
    assert_eq!(save(&mut small, &full()), Err(LinkError::TooSmall));
    let mut failing = MockFlash::new(LINK_SIZE);
    failing.fail_with(Some(FlashError::Storage));
    assert_eq!(
        load(&mut failing),
        Err(LinkError::Flash(FlashError::Storage))
    );
    let mut ignoring = MockFlash::new(LINK_SIZE);
    ignoring.ignore_writes(true);
    assert_eq!(save(&mut ignoring, &full()), Err(LinkError::NotVerified));
    assert_eq!(ignoring.capacity(), LINK_SIZE);
}
