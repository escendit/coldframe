//! The Story 4.4 Node rows through `begin` and `run`, with the coldframe-hal mocks and a scripted
//! Hub: one test per row of the I/O matrix.

mod common;

use coldframe_crypto::frame as crypto_frame;
use coldframe_hal::mock::MockFlash;
use coldframe_hal::{BROADCAST, DatagramError};
use coldframe_protocol::device_v1::NodeFrame_::Measured;
use coldframe_protocol::device_v1::{ChargeStatus, Quantity, SealedEnvelope, Unit};
use coldframe_protocol::radio::{MAX_PAYLOAD, Message};
use coldframe_protocol::{
    NODE_FRAME_MAX_SIZE, SPECIFICATION_SET_MAX_SIZE, decode, encode, encode_node_frame,
};
use coldframe_sensing::counter::{CounterError, encode_record};
use coldframe_sensing::{
    BatteryLevel, MeasuredAt, Reading, ReadingValue, ReservedCounter, WakeReport,
};
use coldframe_transport::frame::{
    node_frame, open_downlink, seal_uplink, spec_hash, specification_set,
};
use coldframe_transport::transport::{
    ACK_WINDOW_MS, BURST_MAX, FRAME_MAGIC, PENDING_WAIT_MS, PROBE_WAIT_MS, RESCAN_AFTER_MISSES,
    SCAN_BACKOFF_WAKES,
};
use coldframe_transport::{Buffered, ClockChange, Hub, Partition, Report, ReportBuffer};
use common::{
    Ack, HUB_MAC, Node, PERIOD_MS, downlink, downlink_message, frame_vector, hex, keys, other_keys,
    report,
};
use sha2::{Digest, Sha256};

const SERVER_TIME: i64 = 1_790_000_000_000;

// --- Vector frame ---------------------------------------------------------------------------

#[test]
fn the_vector_report_builds_and_seals_to_the_vector_frame() {
    let case = frame_vector("node frame with four readings");
    let message = &case["message"];
    // The report of the vector: Readings 100 to 103, report_seq 104.
    let report = report(
        100,
        MeasuredAt::Synced {
            unix_ms: 1_790_000_000_000,
        },
    );
    assert_eq!(message["reportSeq"], "104");
    let frame = node_frame(&report, 3, 912_345, &hex(message, "specHash"), None);
    assert_eq!(frame.charging, ChargeStatus::Charging);
    assert_eq!(frame.battery_percent(), Some(&87));
    assert_eq!(frame.readings[1].quantity, Quantity::AirTemperature);

    let mut plaintext = [0u8; NODE_FRAME_MAX_SIZE];
    let length = encode_node_frame(&frame, &mut plaintext).unwrap();
    assert_eq!(plaintext[..length], hex(&case, "plaintext")[..]);

    let mut radio_message = [0u8; MAX_PAYLOAD];
    let sealed = seal_uplink(&keys(), 42, &frame, &mut radio_message).unwrap();
    let Ok(Message::Uplink(envelope)) = Message::decode(&radio_message[..sealed]) else {
        panic!("an uplink");
    };
    let envelope: SealedEnvelope = decode(envelope).unwrap();
    assert_eq!(envelope.protocol_version, 1);
    assert_eq!(envelope.device_id.as_slice(), &hex(&case, "deviceId")[..]);
    assert_eq!(envelope.counter, 42);
    assert_eq!(
        envelope.ciphertext.as_slice(),
        &hex(&case, "ciphertext")[..]
    );
}

// --- Vector downlink ------------------------------------------------------------------------

#[test]
fn the_vector_downlink_opens_and_acknowledges_the_vector_report() {
    let case = frame_vector("downlink acknowledging the readings of the node frame");
    let envelope = SealedEnvelope {
        protocol_version: 1,
        device_id: heapless::Vec::from_slice(&hex(&case, "deviceId")).unwrap(),
        counter: 8,
        ciphertext: heapless::Vec::from_slice(&hex(&case, "ciphertext")).unwrap(),
    };
    let mut bytes = [0u8; 128];
    let length = encode(&envelope, &mut bytes).unwrap();
    let downlink = open_downlink(&keys(), &bytes[..length]).expect("the vector opens");
    assert_eq!(downlink.acked_counter, 42);
    assert_eq!(downlink.server_time_ms, 1_790_000_000_321);
    let [range] = downlink.acked_readings.as_slice() else {
        panic!("one range");
    };
    assert_eq!((range.first, range.last), (100, 104));

    // Its range deletes exactly the vector report.
    let mut flashes = common::Flashes::new();
    let mut flash = flashes.buffer();
    let mut buffer = ReportBuffer::open(&mut flash).unwrap();
    for first in [100, 105] {
        let stored = report(first, MeasuredAt::Synced { unix_ms: 1 });
        buffer.append(&mut flash, &stored).unwrap();
    }
    assert_eq!(buffer.ack(&mut flash, range.first, range.last), Ok(5));
    assert_eq!(common::buffered(&mut flashes), [109]);
}

// --- In-window ack --------------------------------------------------------------------------

#[test]
fn an_ack_inside_the_window_deletes_the_report_and_sets_the_clock() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    let wake = node.wake();

    assert_eq!(wake.outcome.buffered, Buffered::Stored { dropped: 0 });
    assert_eq!(
        wake.outcome.hub,
        Hub::Found {
            channel: 6,
            scanned: false
        }
    );
    assert_eq!(wake.outcome.sent, 1);
    assert!(wake.outcome.fresh);
    assert_eq!(wake.outcome.misses, 0);
    assert_eq!(wake.outcome.deleted, 5, "four Readings and the report");
    assert_eq!(wake.outcome.backlog, 0);
    assert!(wake.outcome.faults.is_empty());
    assert!(node.buffered().is_empty());

    // The clock is the Server's, and the next wake starts with it.
    assert_eq!(wake.outcome.clock, ClockChange::Set);
    assert_eq!(wake.unix_ms, Some(SERVER_TIME as u64));
    assert_eq!(wake.link.misses, 0);

    // One probe on the known channel, one frame to the Hub that answered.
    assert_eq!(wake.channels, [6]);
    let [probe, uplink] = wake.sent.as_slice() else {
        panic!("a probe and a frame: {:?}", wake.sent.len());
    };
    assert_eq!((probe.to, probe.payload.len()), (BROADCAST, 9));
    assert_eq!((uplink.to, uplink.channel), (HUB_MAC, 6));
    // The burst was acknowledged in full, so the Node stopped listening at once.
    assert!(wake.timeouts.is_empty(), "{:?}", wake.timeouts);
}

// --- No ack ---------------------------------------------------------------------------------

#[test]
fn without_an_ack_the_report_is_kept_whatever_the_radio_reports() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    node.world.borrow_mut().ack = Ack::Lost;
    // The radio says every send succeeded. That is not delivery (N-1).
    node.send_report = Ok(());
    let wake = node.wake();

    assert_eq!(wake.outcome.sent, 1);
    assert!(!wake.outcome.fresh);
    assert_eq!(wake.outcome.misses, 1);
    assert_eq!(wake.outcome.deleted, 0);
    assert_eq!(wake.outcome.backlog, 1);
    assert_eq!(wake.outcome.clock, ClockChange::Unchanged);
    assert_eq!(wake.unix_ms, None);
    assert_eq!(node.buffered(), [104]);
    // The Node listened for the whole window after its last send.
    assert_eq!(wake.timeouts, [ACK_WINDOW_MS]);
    assert_eq!(ACK_WINDOW_MS, 300);
}

#[test]
fn a_send_the_radio_calls_failed_is_still_acknowledged_by_its_downlink() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    node.send_report = Err(DatagramError::Send);
    let wake = node.wake();
    assert_eq!(wake.outcome.sent, 1);
    assert!(wake.outcome.fresh);
    assert!(node.buffered().is_empty());
}

// --- Resend ---------------------------------------------------------------------------------

#[test]
fn a_resend_is_the_same_report_under_a_higher_counter() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    node.world.borrow_mut().ack = Ack::Lost;
    let first = node.wake();
    assert_eq!(first.uplinks().len(), 1);
    let first_frame = open_uplink(&first.uplinks()[0].payload);

    // A power cycle between the wakes: the resend carries the new boot ID and uptime.
    node.reboot();
    node.world.borrow_mut().ack = Ack::InWindow;
    let second = node.wake();
    assert_eq!(second.uplinks().len(), 2, "the old report and the new one");
    let resent = open_uplink(&second.uplinks()[0].payload);
    let fresh = open_uplink(&second.uplinks()[1].payload);

    assert!(resent.0 > first_frame.0, "a strictly higher counter");
    assert!(fresh.0 > resent.0);
    // Never the same sealed bytes twice.
    assert_ne!(first.uplinks()[0].payload, second.uplinks()[0].payload);

    let (old, new) = (&first_frame.1, &resent.1);
    assert_eq!(new.report_seq, old.report_seq);
    assert_eq!(new.readings, old.readings);
    assert_eq!(new.measured, old.measured);
    assert_eq!(new.battery_percent(), old.battery_percent());
    assert_eq!(new.charging, old.charging);
    assert_eq!(new.spec_hash, old.spec_hash);
    // boot_id and uptime_ms are those of the resend.
    assert_eq!((old.boot_id, old.uptime_ms), (1, 5_000));
    assert_eq!((new.boot_id, new.uptime_ms), (2, 1_000));
    // The measurement keeps its own time: unsynced, from the first boot.
    let Some(Measured::Unsynced(measured)) = &new.measured else {
        panic!("unsynced");
    };
    assert_eq!((measured.boot_id, measured.uptime_ms), (1, 5_000));

    assert!(node.buffered().is_empty());
}

// --- Late ack -------------------------------------------------------------------------------

#[test]
fn a_late_ack_is_collected_at_the_next_probe_before_anything_is_sent() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    node.world.borrow_mut().ack = Ack::Late;

    let first = node.wake();
    assert_eq!(first.outcome.sent, 1);
    assert_eq!(first.outcome.pending, 0);
    assert!(!first.outcome.fresh, "nothing came inside the window");
    assert_eq!(first.outcome.misses, 1);
    assert_eq!(node.buffered(), [104]);
    assert_eq!(node.world.borrow().kept.len(), 1);

    let second = node.wake();
    // The probe reply said pending; the kept downlink deleted report 104 before the burst.
    assert_eq!(second.outcome.pending, 1);
    assert!(second.outcome.fresh);
    assert_eq!(second.outcome.misses, 0);
    assert_eq!(second.outcome.deleted, 5);
    assert_eq!(second.outcome.sent, 1, "only the new report");
    assert_eq!(node.world.borrow().report_seqs(), [104, 109]);
    assert_eq!(node.buffered(), [109]);
    // The clock comes from the late ack: the Server time plus the 15 minutes since the send.
    assert_eq!(second.outcome.clock, ClockChange::Set);
    assert_eq!(second.unix_ms, Some(SERVER_TIME as u64 + PERIOD_MS));
    assert_eq!(PENDING_WAIT_MS, 120);
}

#[test]
fn the_kept_downlink_offered_again_is_not_fresh_a_second_time() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    // Acknowledged inside the window; the Hub also keeps the downlink in its slot.
    assert!(node.wake().outcome.fresh);
    // The Server goes away. The Hub still offers the downlink of the last wake at the probe.
    node.world.borrow_mut().ack = Ack::Lost;
    let wake = node.wake();
    assert_eq!(wake.outcome.pending, 1);
    assert_eq!(wake.outcome.downlinks, 1, "authentic, so applied");
    assert_eq!(wake.outcome.deleted, 0);
    assert!(!wake.outcome.fresh, "it was fresh once, a wake ago");
    assert_eq!(wake.outcome.misses, 1);
    assert_eq!(wake.outcome.clock, ClockChange::Unchanged);
}

#[test]
fn a_late_ack_from_another_boot_deletes_but_does_not_set_the_clock() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    node.world.borrow_mut().ack = Ack::Late;
    node.wake();
    node.reboot();
    let wake = node.wake();
    assert!(wake.outcome.fresh);
    assert_eq!(wake.outcome.deleted, 5);
    // The burst's uptime belongs to the last power-on: it says nothing about this one.
    assert_eq!(wake.outcome.clock, ClockChange::Unchanged);
    assert_eq!(wake.unix_ms, None);
}

// --- Forged downlink ------------------------------------------------------------------------

#[test]
fn a_forged_downlink_changes_nothing() {
    let node_keys = keys();
    let foreign = other_keys();
    let ack = downlink(1, SERVER_TIME, 0, u64::MAX);
    let mut wrong_version = ack.clone();
    wrong_version.protocol_version = 2;
    let forgeries = [
        // Sealed under another Device's ack key, naming this Node.
        downlink_message(&foreign, node_keys.device_id, 1, 1, &ack),
        // Authentic for another Device.
        downlink_message(&foreign, foreign.device_id, 1, 1, &ack),
        // This Node's key, but the envelope names another Device ID.
        downlink_message(&node_keys, foreign.device_id, 1, 1, &ack),
        // Envelope version 2.
        downlink_message(&node_keys, node_keys.device_id, 2, 1, &ack),
        // Downlink version 2 inside an authentic envelope.
        downlink_message(&node_keys, node_keys.device_id, 1, 1, &wrong_version),
        // Not an envelope at all.
        vec![0x04, 0xFF, 0xFF, 0xFF],
    ];
    for (index, forgery) in forgeries.into_iter().enumerate() {
        let mut node = Node::new(6);
        node.flashes.link = known_channel(6);
        node.world.borrow_mut().ack = Ack::Lost;
        // Two misses so far; the counter of the first frame will be 0, then 1.
        node.wake();
        // The forgery arrives in the window of the second wake, acknowledging its counter.
        node.world
            .borrow_mut()
            .inject
            .push((HUB_MAC, forgery.clone()));
        let wake = node.wake();
        assert_eq!(wake.outcome.downlinks, 0, "forgery {index}");
        assert_eq!(wake.outcome.deleted, 0, "forgery {index}");
        assert!(!wake.outcome.fresh, "forgery {index}");
        assert_eq!(wake.outcome.misses, 2, "forgery {index}");
        assert_eq!(
            wake.outcome.clock,
            ClockChange::Unchanged,
            "forgery {index}"
        );
        assert_eq!(wake.unix_ms, None, "forgery {index}");
        assert_eq!(node.buffered(), [104, 109], "forgery {index}");
        assert!(wake.outcome.faults.is_empty(), "dropped silently");
    }
}

// --- Replayed old downlink ------------------------------------------------------------------

#[test]
fn a_replayed_old_downlink_deletes_what_it_acknowledges_and_nothing_more() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    // Wake 1 is acknowledged in the window; keep its downlink for the replay.
    let first = node.wake();
    assert!(first.outcome.fresh);
    let replay = node.world.borrow().kept[0].clone();
    let synced = first.unix_ms.expect("synced");

    // Wakes 2 and 3 reach nobody: the Hub is off.
    node.world.borrow_mut().channel = None;
    node.world.borrow_mut().kept.clear();
    assert_eq!(node.wake().outcome.misses, 1);
    assert_eq!(node.wake().outcome.misses, 2);

    // Wake 4: the Hub is back but the Server is not, so a burst goes out unanswered. Wake 5
    // hears a replay of the downlink of wake 1, whose acked_counter is older than that burst.
    node.world.borrow_mut().channel = Some(6);
    node.world.borrow_mut().ack = Ack::Lost;
    let fourth = node.wake();
    assert_eq!(fourth.outcome.sent, 3);
    assert_eq!(fourth.outcome.misses, 3);
    node.world.borrow_mut().inject.push((HUB_MAC, replay));
    let fifth = node.wake();

    // Authentic, so it is applied: but everything it acknowledges is already gone.
    assert_eq!(fifth.outcome.downlinks, 1);
    assert_eq!(fifth.outcome.deleted, 0);
    assert!(!fifth.outcome.fresh);
    assert_eq!(fifth.outcome.misses, 4, "the miss count is not reset");
    // The clock only ran on: the replay did not touch it.
    assert_eq!(fifth.outcome.clock, ClockChange::Unchanged);
    assert_eq!(fifth.unix_ms, Some(synced + 4 * PERIOD_MS));
    assert_eq!(node.buffered(), [109, 114, 119, 124]);
}

#[test]
fn a_replayed_old_downlink_still_deletes_a_reading_the_node_holds() {
    // The ack of an old frame arrives long after: its Readings are deleted, as the Server has
    // them, but it is not fresh.
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    node.world.borrow_mut().ack = Ack::Late;
    node.wake();
    let late = node.world.borrow_mut().kept.remove(0);
    node.world.borrow_mut().ack = Ack::Lost;
    // Two more bursts go out unanswered, so the first is older than the previous burst.
    node.wake();
    node.wake();
    assert_eq!(node.buffered(), [104, 109, 114]);
    node.world.borrow_mut().inject.push((HUB_MAC, late));
    let wake = node.wake();
    assert_eq!(wake.outcome.downlinks, 1);
    assert_eq!(wake.outcome.deleted, 5);
    assert!(!wake.outcome.fresh);
    assert_eq!(wake.outcome.misses, 4);
    assert_eq!(wake.unix_ms, None, "the clock is untouched");
    assert_eq!(node.buffered(), [109, 114, 119]);
}

// --- Counter across reboot ------------------------------------------------------------------

#[test]
fn counters_after_a_reset_are_above_every_counter_before_it() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    node.world.borrow_mut().ack = Ack::Lost;
    node.wake();
    node.wake();
    let before: Vec<u64> = node
        .world
        .borrow()
        .uplinks
        .iter()
        .map(|uplink| uplink.counter)
        .collect();
    assert!(before.is_empty(), "the frames were lost on the way");
    let mut counters = Vec::new();
    node.world.borrow_mut().ack = Ack::InWindow;
    // Count what the Node seals: open every uplink it sends.
    for _ in 0..2 {
        let wake = node.wake();
        counters.push(
            wake.uplinks()
                .iter()
                .map(|sent| open_uplink(&sent.payload).0)
                .collect::<Vec<_>>(),
        );
        node.reboot();
    }
    let first_max = counters[0].iter().max().unwrap();
    assert!(counters[1].iter().all(|counter| counter > first_max));
    // And strictly increasing within a wake.
    for wake in &counters {
        assert!(wake.windows(2).all(|pair| pair[0] < pair[1]));
    }
    // The stored ceiling is above every counter used.
    let ceiling = ReservedCounter::new(&mut node.flashes.frame, FRAME_MAGIC)
        .current()
        .unwrap();
    assert!(counters.iter().flatten().all(|counter| *counter < ceiling));
}

#[test]
fn the_ceiling_is_in_flash_before_a_frame_is_sealed() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    // The stored ceiling is 1000: the first frame is sealed under 1000, never under 0.
    node.flashes.frame.contents_mut()[..32].copy_from_slice(&encode_record(FRAME_MAGIC, 1_000));
    let wake = node.wake();
    assert_eq!(open_uplink(&wake.uplinks()[0].payload).0, 1_000);
    assert_eq!(
        ReservedCounter::new(&mut node.flashes.frame, FRAME_MAGIC).current(),
        Ok(1_001)
    );
}

#[test]
fn a_counter_fault_seals_nothing_and_keeps_the_report() {
    for fault in ["corrupt", "ignored", "missing"] {
        let mut node = Node::new(6);
        node.flashes.link = known_channel(6);
        match fault {
            // Written slots and no valid record: the counter never falls back to 0.
            "corrupt" => node.flashes.frame.contents_mut()[..32].fill(0x00),
            // The write does not reach the chip, so the ceiling does not read back.
            "ignored" => node.flashes.frame.ignore_writes(true),
            _ => node.flashes.missing = Some(Partition::Frame),
        }
        let wake = node.wake();
        assert!(wake.uplinks().is_empty(), "{fault}: nothing is sealed");
        assert_eq!(wake.outcome.sent, 0, "{fault}");
        assert_eq!(wake.probes().len(), 1, "{fault}: the Hub was found");
        match fault {
            "corrupt" => assert_eq!(wake.outcome.faults.counter, Some(CounterError::Corrupt)),
            "ignored" => assert_eq!(wake.outcome.faults.counter, Some(CounterError::NotVerified)),
            _ => assert_eq!(wake.outcome.faults.partition, Some(Partition::Frame)),
        }
        node.flashes.missing = None;
        assert_eq!(node.buffered(), [104], "{fault}: the report stays buffered");
        assert_eq!(wake.outcome.misses, 1, "{fault}");
    }
}

// --- Buffer overflow ------------------------------------------------------------------------

#[test]
fn the_97th_unacknowledged_report_drops_the_oldest() {
    let mut node = Node::new(6);
    // No Hub at all: every wake buffers and nothing is acknowledged.
    node.world.borrow_mut().channel = None;
    for _ in 0..96 {
        let wake = node.wake();
        assert_eq!(wake.outcome.buffered, Buffered::Stored { dropped: 0 });
    }
    assert_eq!(node.buffered().len(), 96);
    let wake = node.wake();
    assert_eq!(wake.outcome.buffered, Buffered::Stored { dropped: 1 });
    assert_eq!(wake.outcome.backlog, 96);
    // The newest 96, in order: the first report (104) is gone.
    let expected: Vec<u64> = (1..97).map(|wake| 104 + 5 * wake).collect();
    assert_eq!(node.buffered(), expected);
}

// --- Channel re-scan ------------------------------------------------------------------------

#[test]
fn after_three_misses_the_node_scans_and_follows_the_hub() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    assert!(node.wake().outcome.fresh);

    // The router changes channel: the Hub is on 11 now.
    node.world.borrow_mut().channel = Some(11);
    for miss in 1..=RESCAN_AFTER_MISSES {
        let wake = node.wake();
        assert_eq!(wake.outcome.hub, Hub::NotFound { scanned: false });
        assert_eq!(wake.channels, [6], "only the known channel is probed");
        assert_eq!(wake.outcome.misses, miss);
        assert!(
            wake.uplinks().is_empty(),
            "nothing is sealed without a reply"
        );
        assert_eq!(wake.timeouts, [PROBE_WAIT_MS]);
    }

    // The fourth wake scans: the known channel first, then 1 to 13, and stops at the Hub.
    let wake = node.wake();
    assert_eq!(wake.channels, [6, 1, 2, 3, 4, 5, 7, 8, 9, 10, 11]);
    assert_eq!(
        wake.outcome.hub,
        Hub::Found {
            channel: 11,
            scanned: true
        }
    );
    assert_eq!(wake.link.channel, Some(11));
    assert_eq!(
        wake.outcome.sent, 4,
        "the three kept reports and the new one"
    );
    assert!(wake.uplinks().iter().all(|sent| sent.channel == 11));
    assert!(wake.outcome.fresh);
    assert_eq!(wake.outcome.misses, 0);
    assert!(node.buffered().is_empty());
    assert_eq!(PROBE_WAIT_MS, 120);

    // From then on only channel 11 is probed.
    assert_eq!(node.wake().channels, [11]);
}

#[test]
fn a_node_without_a_known_channel_scans_at_once() {
    let mut node = Node::new(3);
    let wake = node.wake();
    assert_eq!(wake.channels, [1, 2, 3]);
    assert_eq!(
        wake.outcome.hub,
        Hub::Found {
            channel: 3,
            scanned: true
        }
    );
    assert_eq!(wake.link.channel, Some(3));
    assert!(wake.outcome.fresh);
}

#[test]
fn a_scan_that_finds_nothing_is_not_repeated_for_four_wakes() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    node.world.borrow_mut().channel = None;
    let full_scan: Vec<u8> = [6]
        .into_iter()
        .chain((1..=13).filter(|c| *c != 6))
        .collect();

    let mut scans = Vec::new();
    for wake_index in 0..12u8 {
        let wake = node.wake();
        assert!(wake.uplinks().is_empty());
        assert_eq!(wake.outcome.misses, wake_index + 1);
        if wake.channels == full_scan {
            assert_eq!(wake.outcome.hub, Hub::NotFound { scanned: true });
            scans.push(wake_index);
        } else {
            assert_eq!(wake.channels, [6], "wake {wake_index}");
            assert_eq!(wake.outcome.hub, Hub::NotFound { scanned: false });
        }
    }
    // Misses 1 to 3 probe the known channel; the 4th wake scans; then every 4th wake.
    assert_eq!(scans, [3, 7, 11]);
    assert_eq!(SCAN_BACKOFF_WAKES, 4);
    // Every report is kept.
    assert_eq!(node.buffered().len(), 12);

    // The Hub comes back on another channel between two scans: the next scan finds it.
    node.world.borrow_mut().channel = Some(1);
    let mut found_after = 0;
    loop {
        found_after += 1;
        if node.wake().outcome.fresh {
            break;
        }
        assert!(found_after < 5);
    }
    assert_eq!(found_after, 4, "the scan after the hold-off");
}

// --- Backlog --------------------------------------------------------------------------------

#[test]
fn a_backlog_goes_out_eight_frames_a_wake_oldest_first() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    for _ in 0..19 {
        assert_eq!(node.setup_wake().buffered, Buffered::Stored { dropped: 0 });
    }
    // 19 buffered; this wake adds the 20th.
    let wake = node.wake();
    assert_eq!(wake.outcome.sent, 8);
    assert_eq!(BURST_MAX, 8);
    let expected: Vec<u64> = (0..8).map(|index| 104 + 5 * index).collect();
    assert_eq!(node.world.borrow().report_seqs(), expected, "oldest first");
    assert_eq!(wake.outcome.backlog, 12);
    assert_eq!(node.buffered().len(), 12);
    // One fresh counter per frame, in order.
    let counters: Vec<u64> = node
        .world
        .borrow()
        .uplinks
        .iter()
        .map(|uplink| uplink.counter)
        .collect();
    assert_eq!(counters, (0..8).collect::<Vec<u64>>());
}

// --- Clock ----------------------------------------------------------------------------------

#[test]
fn a_clock_five_seconds_ahead_moves_back_exactly_one_second() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    let first = node.wake();
    assert_eq!(first.unix_ms, Some(SERVER_TIME as u64));

    // 15 minutes on, the Node's clock says SERVER_TIME + 900 s; the Server says 5 s less.
    let expected = SERVER_TIME as u64 + PERIOD_MS;
    node.world.borrow_mut().server_time_ms = (expected - 5_000) as i64;
    let second = node.wake();
    assert_eq!(second.outcome.clock, ClockChange::Back);
    assert_eq!(second.unix_ms, Some(expected - 1_000));
    // The report of that wake was stamped before the correction, with the synced clock.
    assert_eq!(
        second.report.measured_at,
        MeasuredAt::Synced { unix_ms: expected }
    );

    // The next downlink takes one more second off, and no more.
    node.world.borrow_mut().server_time_ms = (expected + PERIOD_MS - 5_000) as i64;
    let third = node.wake();
    assert_eq!(third.unix_ms, Some(expected + PERIOD_MS - 2_000));

    // A clock behind the Server is corrected forward in full.
    node.world.borrow_mut().server_time_ms = (expected + 2 * PERIOD_MS + 90_000) as i64;
    let fourth = node.wake();
    assert_eq!(fourth.outcome.clock, ClockChange::Forward);
    assert_eq!(fourth.unix_ms, Some(expected + 2 * PERIOD_MS + 90_000));
}

#[test]
fn two_fresh_downlinks_in_one_wake_correct_the_clock_once() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    assert_eq!(node.wake().unix_ms, Some(SERVER_TIME as u64));

    // A setup wake buffers a report, so the next burst has two frames; both are acknowledged
    // late, by a Server whose clock is 5 s behind the Node's.
    node.world.borrow_mut().ack = Ack::Late;
    node.setup_wake();
    let at_send = SERVER_TIME as u64 + 2 * PERIOD_MS;
    node.world.borrow_mut().server_time_ms = (at_send - 5_000) as i64;
    let burst = node.wake();
    assert_eq!(burst.outcome.sent, 2);
    assert_eq!(burst.unix_ms, Some(at_send));
    assert_eq!(node.world.borrow().kept.len(), 2);

    // The next wake collects both. Each is fresh; the clock goes back one step, not two.
    let wake = node.wake();
    assert_eq!(wake.outcome.pending, 2);
    assert_eq!(wake.outcome.downlinks, 2);
    assert_eq!(wake.outcome.deleted, 10);
    assert!(wake.outcome.fresh);
    assert_eq!(wake.outcome.clock, ClockChange::Back);
    assert_eq!(wake.unix_ms, Some(at_send + PERIOD_MS - 1_000));
}

#[test]
fn the_first_sync_of_a_boot_sets_the_clock_even_far_back() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    assert_eq!(node.wake().unix_ms, Some(SERVER_TIME as u64));
    // A new power-on is unsynced until its own first downlink, which may say anything.
    node.reboot();
    node.world.borrow_mut().server_time_ms = SERVER_TIME - 3_600_000;
    let wake = node.wake();
    assert!(matches!(
        wake.report.measured_at,
        MeasuredAt::Unsynced { boot_id: 2, .. }
    ));
    assert_eq!(wake.outcome.clock, ClockChange::Set);
    assert_eq!(wake.unix_ms, Some((SERVER_TIME - 3_600_000) as u64));
}

// --- Unsynced report ------------------------------------------------------------------------

#[test]
fn a_report_without_a_sync_for_its_boot_is_unsynced() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    let first = node.wake();
    let frame = &node.world.borrow().uplinks[0].frame.clone();
    // No downlink had set the clock when the Readings were taken.
    let Some(Measured::Unsynced(unsynced)) = &frame.measured else {
        panic!("unsynced");
    };
    assert_eq!((unsynced.boot_id, unsynced.uptime_ms), (1, 5_000));
    assert_eq!((frame.boot_id, frame.uptime_ms), (1, 5_000));
    assert_eq!(first.unix_ms, Some(SERVER_TIME as u64));

    // The next wake of the same boot is synced: its frame carries the time.
    node.wake();
    let frame = node.world.borrow().uplinks[1].frame.clone();
    assert_eq!(
        frame.measured,
        Some(Measured::MeasuredAtMs(SERVER_TIME + PERIOD_MS as i64))
    );

    // After a power cycle the sync is gone with the boot ID.
    node.reboot();
    node.wake();
    let frame = node.world.borrow().uplinks[2].frame.clone();
    let Some(Measured::Unsynced(unsynced)) = &frame.measured else {
        panic!("unsynced after the reboot");
    };
    assert_eq!((unsynced.boot_id, unsynced.uptime_ms), (2, 1_000));
}

// --- Specification request ------------------------------------------------------------------

#[test]
fn a_specification_request_is_answered_once_in_the_next_frame() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);

    // The Server does not know the hash: its downlink says so.
    let first = node.wake();
    assert!(!first.outcome.specifications_sent);
    assert!(first.link.specifications_requested);
    {
        let world = node.world.borrow();
        let frame = &world.uplinks[0].frame;
        assert_eq!(frame.spec_hash.as_slice(), spec_hash());
        assert_eq!(frame.specifications(), None);
    }

    // The next frame carries the set, once.
    let second = node.wake();
    assert!(second.outcome.specifications_sent);
    assert!(!second.link.specifications_requested);
    {
        let world = node.world.borrow();
        let uplink = &world.uplinks[1];
        assert_eq!(uplink.frame.specifications(), Some(&specification_set()));
        assert_eq!(uplink.frame.spec_hash.as_slice(), spec_hash());
        // The envelope and its kind byte fit one ESP-NOW payload.
        assert!(uplink.message_len <= 250, "{}", uplink.message_len);
        assert_eq!(world.specification_sets, 1);
    }

    // The frame after does not.
    let third = node.wake();
    assert!(!third.outcome.specifications_sent);
    assert_eq!(node.world.borrow().uplinks[2].frame.specifications(), None);
    assert_eq!(node.world.borrow().specification_sets, 1);
}

#[test]
fn the_specification_set_is_the_four_sensors_of_a_node() {
    let set = specification_set();
    let soil = &set.specifications[0];
    assert_eq!(soil.quantity, Quantity::SoilMoisture);
    assert!(soil.calibration);
    assert_eq!(
        (soil.default_low(), soil.default_high()),
        (Some(&30), Some(&80))
    );
    assert_eq!(set.specifications.len(), 4);
    for watched in &set.specifications[1..] {
        assert!(!watched.calibration);
        assert_eq!(
            (watched.default_low(), watched.default_high()),
            (None, None)
        );
        assert!(watched.range_min < watched.range_max);
    }
    let quantities: Vec<Quantity> = set.specifications.iter().map(|s| s.quantity).collect();
    assert_eq!(
        quantities,
        [
            Quantity::SoilMoisture,
            Quantity::AirTemperature,
            Quantity::RelativeHumidity,
            Quantity::GasResistance
        ]
    );
    // The hash is SHA-256 of the serialized set, computed here independently.
    let mut encoded = [0u8; SPECIFICATION_SET_MAX_SIZE];
    let length = encode(&set, &mut encoded).unwrap();
    let expected: [u8; 32] = Sha256::digest(&encoded[..length]).into();
    assert_eq!(spec_hash(), expected);
    assert_ne!(spec_hash(), <[u8; 32]>::from(Sha256::digest([])));
}

#[test]
fn the_specification_set_matches_the_device_simulator() {
    // Units and ranges, slot by slot, as `DefaultSpecifications` of the C# simulator declares
    // them: the Server's tests and the firmware describe the same Node.
    let expected = [
        (Unit::RawCount, 0, 4_095),
        (Unit::MilliDegreeCelsius, -40_000, 85_000),
        (Unit::MilliPercent, 0, 100_000),
        (Unit::Ohm, 0, 100_000_000),
    ];
    let set = specification_set();
    for (specification, (unit, range_min, range_max)) in set.specifications.iter().zip(expected) {
        assert_eq!(specification.unit, unit);
        assert_eq!(
            (specification.range_min, specification.range_max),
            (range_min, range_max)
        );
    }
    let path = concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../../tests/cs/device-simulator/SimulatedDevice.cs"
    );
    let simulator = std::fs::read_to_string(path).expect("the simulator source is readable");
    let start = simulator
        .find("SpecificationSet DefaultSpecifications()")
        .expect("DefaultSpecifications");
    let declared = &simulator[start..start + simulator[start..].find("};").unwrap()];
    for fragment in [
        "Quantity = Quantity.SoilMoisture",
        "Unit = Unit.RawCount",
        "RangeMin = 0,",
        "RangeMax = 4095",
        "Calibration = true",
        "DefaultLow = 30",
        "DefaultHigh = 80",
        "Quantity = Quantity.AirTemperature, Unit = Unit.MilliDegreeCelsius, RangeMin = -40_000, RangeMax = 85_000",
        "Quantity = Quantity.RelativeHumidity, Unit = Unit.MilliPercent, RangeMin = 0, RangeMax = 100_000 ",
        "Quantity = Quantity.GasResistance, Unit = Unit.Ohm, RangeMin = 0, RangeMax = 100_000_000",
    ] {
        assert!(
            declared.contains(fragment),
            "the simulator declares {fragment}"
        );
    }
    assert_eq!(declared.matches("new Specification").count(), 4);
}

#[test]
fn a_wake_report_becomes_the_buffered_report() {
    let reading = |slot, seq, value| Reading { slot, seq, value };
    let mut wake = WakeReport {
        measured_at: MeasuredAt::Synced { unix_ms: 1_234 },
        readings: Default::default(),
        battery: Some(BatteryLevel {
            cell_millivolts: 3_870,
            percent: 60,
            approximate: true,
        }),
        charging: coldframe_sensing::ChargeStatus::NotCharging,
        report_seq: None,
    };
    for value in [
        reading(0, 40, ReadingValue::SoilRaw(1_873)),
        reading(1, 41, ReadingValue::TemperatureMilliC(-2_500)),
        reading(2, 42, ReadingValue::HumidityMilliPct(64_250)),
        reading(3, 43, ReadingValue::GasOhms(48_211)),
    ] {
        wake.readings.push(value).unwrap();
    }
    // Without a report_seq the report cannot be identified: it is not buffered.
    assert_eq!(Report::from_wake(&wake), None);

    wake.report_seq = Some(44);
    let report = Report::from_wake(&wake).expect("a report");
    assert_eq!(report.report_seq, 44);
    assert_eq!(report.measured_at, MeasuredAt::Synced { unix_ms: 1_234 });
    assert_eq!(report.battery_percent, Some(60));
    assert_eq!(
        report.charging,
        coldframe_sensing::ChargeStatus::NotCharging
    );
    let readings: Vec<(u8, i32, u64, i64)> = report
        .readings
        .iter()
        .map(|r| (r.slot, i32::from(r.quantity), r.seq, r.value))
        .collect();
    assert_eq!(
        readings,
        [
            (0, Quantity::SoilMoisture.0, 40, 1_873),
            (1, Quantity::AirTemperature.0, 41, -2_500),
            (2, Quantity::RelativeHumidity.0, 42, 64_250),
            (3, Quantity::GasResistance.0, 43, 48_211),
        ]
    );

    // No battery, and a wake without Readings.
    wake.battery = None;
    wake.readings.clear();
    let empty = Report::from_wake(&wake).expect("a report");
    assert_eq!(empty.battery_percent, None);
    assert!(empty.readings.is_empty());
}

#[test]
fn a_frame_at_its_longest_still_fits_one_payload_without_the_set() {
    // Every varint at its widest: the frame with the set is too long, and goes without it.
    let mut widest = report(
        u64::MAX - 10,
        MeasuredAt::Unsynced {
            boot_id: u64::MAX,
            uptime_ms: u64::MAX,
        },
    );
    for reading in &mut widest.readings {
        reading.value = i64::MIN;
    }
    let hash = spec_hash();
    let mut message = [0u8; MAX_PAYLOAD];
    let with_set = node_frame(
        &widest,
        u64::MAX,
        u64::MAX,
        &hash,
        Some(specification_set()),
    );
    assert!(seal_uplink(&keys(), u64::MAX, &with_set, &mut message).is_err());
    let without = node_frame(&widest, u64::MAX, u64::MAX, &hash, None);
    let length = seal_uplink(&keys(), u64::MAX, &without, &mut message).unwrap();
    assert!(length <= MAX_PAYLOAD, "{length}");
}

// --- Setup wake -----------------------------------------------------------------------------

#[test]
fn a_setup_wake_buffers_without_the_radio() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    let outcome = node.setup_wake();
    assert_eq!(outcome.buffered, Buffered::Stored { dropped: 0 });
    assert_eq!(outcome.hub, Hub::NotTried);
    assert_eq!(outcome.sent, 0);
    assert_eq!(node.world.borrow().probes, 0);
    assert_eq!(node.buffered(), [104]);
    // The next wake sends it.
    let wake = node.wake();
    assert_eq!(wake.outcome.sent, 2);
    assert!(node.buffered().is_empty());
}

#[test]
fn a_missing_buffer_partition_loses_the_report_and_sends_nothing() {
    let mut node = Node::new(6);
    node.flashes.link = known_channel(6);
    node.flashes.missing = Some(Partition::Buffer);
    let wake = node.wake();
    assert_eq!(wake.outcome.buffered, Buffered::Failed);
    assert_eq!(wake.outcome.faults.partition, Some(Partition::Buffer));
    assert!(wake.sent.is_empty());
}

// --- Power loss -----------------------------------------------------------------------------

#[test]
fn a_power_cut_anywhere_in_a_wake_repeats_no_counter_and_loses_no_report() {
    let mut cut = 0;
    loop {
        let mut node = Node::new(6);
        node.flashes.link = known_channel(6);
        // Late acknowledgements: every wake deletes (the kept downlink) and sends.
        node.world.borrow_mut().ack = Ack::Late;
        let mut counters = Vec::new();
        let mut measured = Vec::new();
        let record = |wake: &common::Wake, counters: &mut Vec<u64>| {
            counters.extend(
                wake.uplinks()
                    .iter()
                    .map(|sent| open_uplink(&sent.payload).0),
            );
        };
        for _ in 0..2 {
            let wake = node.wake();
            record(&wake, &mut counters);
            measured.push(wake.report.report_seq);
        }

        // The power fails at the `cut`-th write or erase of this wake, whichever partition.
        node.flashes.power.left = Some(cut);
        let interrupted = node.wake();
        let completed = !node.flashes.power.off;
        record(&interrupted, &mut counters);
        let interrupted_seq = interrupted.report.report_seq;
        if completed {
            measured.push(interrupted_seq);
        }

        // Power is back: a new boot, and three more wakes.
        node.flashes.restore_power();
        node.reboot();
        for _ in 0..3 {
            let wake = node.wake();
            assert!(
                wake.outcome.faults.is_empty(),
                "cut {cut}: {:?}",
                wake.outcome.faults
            );
            record(&wake, &mut counters);
            measured.push(wake.report.report_seq);
        }

        // No counter repeats: every frame ever sealed has a higher counter than the one before.
        assert!(
            counters.windows(2).all(|pair| pair[0] < pair[1]),
            "cut {cut}: {counters:?}"
        );
        // No report is lost: each one is on the Server or still buffered.
        let stored = node.world.borrow().report_seqs();
        let held = node.buffered();
        for seq in &measured {
            assert!(
                stored.contains(seq) || held.contains(seq),
                "cut {cut}: report {seq} is lost"
            );
        }
        // Nothing is buffered that was not measured, and nothing changed its report_seq.
        for seq in &held {
            assert!(
                measured.contains(seq) || *seq == interrupted_seq,
                "cut {cut}: report {seq} was never measured"
            );
        }
        if completed {
            // A wake writes the report, an acknowledgement, the counter ceiling and the link.
            assert!(cut >= 4, "{cut}");
            break;
        }
        cut += 1;
    }
}

// --- Helpers --------------------------------------------------------------------------------

/// A link partition that already knows the Hub's channel.
fn known_channel(channel: u8) -> MockFlash {
    let mut flash = MockFlash::new(common::LINK_SIZE);
    let state = coldframe_transport::LinkState {
        channel: Some(channel),
        ..Default::default()
    };
    coldframe_transport::link::save(&mut flash, &state).unwrap();
    flash
}

/// The counter and the opened `NodeFrame` of an uplink radio message.
fn open_uplink(message: &[u8]) -> (u64, coldframe_protocol::device_v1::NodeFrame) {
    let Ok(Message::Uplink(envelope)) = Message::decode(message) else {
        panic!("an uplink");
    };
    let envelope: SealedEnvelope = decode(envelope).unwrap();
    let keys = keys();
    let mut plaintext = vec![0u8; envelope.ciphertext.len()];
    let length = crypto_frame::open(
        &keys.seal_key,
        &keys.device_id,
        envelope.counter,
        &envelope.ciphertext,
        &mut plaintext,
    )
    .unwrap();
    (envelope.counter, decode(&plaintext[..length]).unwrap())
}
