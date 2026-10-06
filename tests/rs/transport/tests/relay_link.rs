//! The Node's transport against the Hub's real relay state (`coldframe_uplink::Relay`), with
//! late delivery: every downlink reaches the Hub after the Node has gone back to sleep. A backlog
//! sent as one burst is acknowledged in full at the next wake.

mod common;

use std::cell::RefCell;
use std::rc::Rc;

use coldframe_hal::mock::{MockDatagramRadio, MockFlash, MockRtc, SentDatagram};
use coldframe_hal::{DATAGRAM_MAX, MacAddress};
use coldframe_sensing::MeasuredAt;
use coldframe_transport::{Buffered, Hub, LinkState, Outcome, begin, link, run};
use coldframe_uplink::{Heard, Relay};
use common::{
    Flashes, HUB_MAC, LINK_SIZE, PERIOD_MS, World, block_on, buffered, keys, report, trng,
};

const NODE_MAC: MacAddress = [0x02, 0, 0, 0, 0, 0x4E];
const CHANNEL: u8 = 6;

/// A Hub: the real relay state, the Server behind it, and whether it is powered.
struct Garden {
    relay: Relay,
    server: World,
    on: bool,
}

impl Garden {
    /// The Hub's radio task: what goes back to the Node for one datagram it sent.
    fn hear(&mut self, sent: &SentDatagram) -> Vec<(MacAddress, Vec<u8>)> {
        let mut answers = Vec::new();
        if !self.on || sent.channel != CHANNEL {
            return answers;
        }
        if let Heard::Probe { reply, pending } = self.relay.hear(&NODE_MAC, &sent.payload) {
            answers.push((HUB_MAC, reply.to_vec()));
            let mut message = [0u8; DATAGRAM_MAX];
            for _ in 0..pending {
                let length = self.relay.take_kept(&NODE_MAC, &mut message).unwrap();
                answers.push((HUB_MAC, message[..length].to_vec()));
            }
        }
        answers
    }

    /// The Hub's uplink, after the Node has gone back to sleep: one request with the queued
    /// frames, the downlinks kept, each sent once into the void.
    fn post(&mut self) -> usize {
        let (batch, dropped) = self.relay.take_batch();
        assert_eq!(dropped, 0);
        for queued in &batch {
            let sent = SentDatagram {
                channel: CHANNEL,
                to: HUB_MAC,
                payload: Vec::new(),
            };
            let downlink = self.server.ingest(queued.envelope.as_bytes(), &sent);
            // The radio message is the kind byte and the envelope.
            assert!(self.relay.keep(&queued.source, &downlink[1..]));
        }
        let mut message = [0u8; DATAGRAM_MAX];
        while self.relay.next_unsent(&mut message).is_some() {}
        batch.len()
    }
}

struct Node {
    flashes: Flashes,
    garden: Rc<RefCell<Garden>>,
    uptime_ms: u64,
    next_seq: u64,
}

impl Node {
    fn wake(&mut self) -> Outcome {
        let mut rtc = MockRtc::new();
        rtc.advance(self.uptime_ms);
        let (mut link, _) = begin(&mut self.flashes, &mut rtc, 1);
        let report = report(
            self.next_seq,
            MeasuredAt::Unsynced {
                boot_id: 1,
                uptime_ms: self.uptime_ms,
            },
        );
        self.next_seq += 5;
        let mut radio = MockDatagramRadio::new();
        let garden = Rc::clone(&self.garden);
        radio.set_peer(move |sent| garden.borrow_mut().hear(sent));
        let outcome = block_on(run(
            &mut self.flashes,
            &mut radio,
            &mut rtc,
            &mut trng(),
            &keys(),
            1,
            &mut link,
            Some(&report),
        ));
        self.uptime_ms += PERIOD_MS;
        // The request and its answer come after the Node's window.
        self.garden.borrow_mut().post();
        outcome
    }
}

fn node() -> Node {
    let mut flashes = Flashes::new();
    let mut known = MockFlash::new(LINK_SIZE);
    let state = LinkState {
        channel: Some(CHANNEL),
        ..LinkState::default()
    };
    link::save(&mut known, &state).unwrap();
    flashes.link = known;
    Node {
        flashes,
        garden: Rc::new(RefCell::new(Garden {
            relay: Relay::new(),
            server: World::new(CHANNEL),
            on: true,
        })),
        uptime_ms: 5_000,
        next_seq: 100,
    }
}

#[test]
fn a_hub_that_cannot_relay_leaves_the_probe_unanswered_and_nothing_is_sealed() {
    let mut node = node();
    // Powered, but not associated or without a clock: the relay has not been told it can relay.
    let outcome = node.wake();
    assert_eq!(outcome.hub, Hub::NotFound { scanned: false });
    assert_eq!(outcome.sent, 0);
    assert_eq!(outcome.misses, 1);
    assert_eq!(buffered(&mut node.flashes), [104]);
    assert!(node.garden.borrow().server.uplinks.is_empty());
    // No frame counter was spent.
    assert!(
        node.flashes
            .frame
            .contents()
            .iter()
            .all(|byte| *byte == 0xFF)
    );
}

#[test]
fn a_backlog_is_acknowledged_in_full_at_the_wake_after_its_burst() {
    let mut node = node();
    node.garden.borrow_mut().relay.set_relaying(true);

    // The Hub is off for five wakes: the backlog grows to five.
    node.garden.borrow_mut().on = false;
    for expected in 1..=5 {
        let outcome = node.wake();
        assert_eq!(outcome.buffered, Buffered::Stored { dropped: 0 });
        assert_eq!((outcome.sent, outcome.backlog), (0, expected));
    }

    // It comes back. The burst carries all six reports; their downlinks arrive too late.
    node.garden.borrow_mut().on = true;
    let burst = node.wake();
    assert_eq!((burst.pending, burst.sent), (0, 6));
    assert_eq!((burst.downlinks, burst.fresh, burst.backlog), (0, false, 6));

    // The next wake collects every one of them before it sends: only its own report is left.
    let after = node.wake();
    assert_eq!(after.pending, 6);
    assert_eq!((after.downlinks, after.deleted), (6, 30));
    assert!(after.fresh);
    assert_eq!(after.misses, 0);
    assert_eq!(after.sent, 1, "nothing of the backlog is sent again");
    assert_eq!(after.backlog, 1);

    // And it stays at one: each wake collects the last wake's downlink.
    for _ in 0..3 {
        let outcome = node.wake();
        assert_eq!((outcome.pending, outcome.sent, outcome.backlog), (1, 1, 1));
        assert!(outcome.fresh);
    }

    // The Server got every report exactly once, in order.
    let stored = node.garden.borrow().server.report_seqs();
    let expected: Vec<u64> = (0..10).map(|wake| 104 + 5 * wake).collect();
    assert_eq!(stored, expected);
    assert_eq!(buffered(&mut node.flashes), [149]);
}

#[test]
fn kept_downlinks_are_offered_once() {
    let mut node = node();
    node.garden.borrow_mut().relay.set_relaying(true);
    assert_eq!(node.wake().sent, 1);
    assert_eq!(node.garden.borrow().relay.kept(&NODE_MAC), 1);
    // The Server goes away: the next wake collects the kept downlink, and nothing new is kept.
    let collected = node.wake();
    assert_eq!((collected.pending, collected.deleted), (1, 5));
    let batch = node.garden.borrow_mut().relay.take_batch();
    assert_eq!(
        batch.0.len(),
        0,
        "the wake's own post already took the frame"
    );
    // That post kept a new downlink; drop it, as a failed request would never have had it.
    let mut message = [0u8; DATAGRAM_MAX];
    while node
        .garden
        .borrow_mut()
        .relay
        .take_kept(&NODE_MAC, &mut message)
        .is_some()
    {}
    // Now nothing is kept: the probe says so, and no stale downlink comes again.
    let quiet = node.wake();
    assert_eq!((quiet.pending, quiet.downlinks), (0, 0));
    assert!(!quiet.fresh);
    assert_eq!(quiet.misses, 1);
}
