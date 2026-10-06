//! The wait windows against a clock that moves: while datagrams that are no downlink for this
//! Node keep arriving, a probe still gives up after `PROBE_WAIT_MS` and the Node still stops
//! listening `ACK_WINDOW_MS` after its last send.

mod common;

use std::cell::Cell;
use std::collections::VecDeque;
use std::rc::Rc;

use coldframe_hal::{Datagram, DatagramError, DatagramRadio, MacAddress, Rtc, RtcError};
use coldframe_protocol::radio::Message;
use coldframe_sensing::MeasuredAt;
use coldframe_transport::transport::{ACK_WINDOW_MS, PROBE_WAIT_MS};
use coldframe_transport::{Hub, LinkState, run};
use common::{Flashes, HUB_MAC, block_on, keys, report, trng};

const NEIGHBOUR: MacAddress = [0x02, 0, 0, 0, 0, 0x99];

/// A clock the radio moves.
#[derive(Clone)]
struct Clock(Rc<Cell<u64>>);

impl Rtc for Clock {
    fn uptime_millis(&self) -> u64 {
        self.0.get()
    }

    fn unix_time_millis(&self) -> Option<u64> {
        None
    }

    fn set_unix_time_millis(&mut self, _unix_millis: u64) -> Result<(), RtcError> {
        Ok(())
    }
}

/// A busy channel: every receive that has nothing from the Hub hears a neighbour's probe after
/// `step_ms`, forever. The Hub answers probes only when `hub` is set.
struct Busy {
    clock: Clock,
    step_ms: u64,
    hub: bool,
    from_hub: VecDeque<Vec<u8>>,
    /// The timeout of every receive, with the uptime it was asked at.
    receives: Vec<(u64, u32)>,
    uplinks: usize,
    last_send_ms: u64,
}

impl Busy {
    fn new(clock: &Clock, step_ms: u64, hub: bool) -> Self {
        Self {
            clock: clock.clone(),
            step_ms,
            hub,
            from_hub: VecDeque::new(),
            receives: Vec::new(),
            uplinks: 0,
            last_send_ms: 0,
        }
    }
}

impl DatagramRadio for Busy {
    fn set_channel(&mut self, _channel: u8) -> Result<(), DatagramError> {
        Ok(())
    }

    async fn send(&mut self, _to: &MacAddress, payload: &[u8]) -> Result<(), DatagramError> {
        self.last_send_ms = self.clock.uptime_millis();
        match Message::decode(payload) {
            Ok(Message::Probe { nonce }) if self.hub => {
                let mut reply = [0u8; 16];
                let length = Message::ProbeReply { nonce, pending: 0 }
                    .encode(&mut reply)
                    .unwrap();
                self.from_hub.push_back(reply[..length].to_vec());
            }
            Ok(Message::Uplink(_)) => self.uplinks += 1,
            _ => {}
        }
        Ok(())
    }

    async fn receive(&mut self, buffer: &mut [u8], timeout_ms: u32) -> Option<Datagram> {
        self.receives.push((self.clock.uptime_millis(), timeout_ms));
        let (source, payload) = match self.from_hub.pop_front() {
            Some(reply) => (HUB_MAC, reply),
            None => {
                // Someone else's probe, a while later: not for this Node.
                assert!(self.receives.len() < 50, "the wait never ends");
                self.clock.0.set(self.clock.uptime_millis() + self.step_ms);
                let mut probe = [0u8; 9];
                Message::Probe { nonce: [0x5A; 8] }
                    .encode(&mut probe)
                    .unwrap();
                (NEIGHBOUR, probe.to_vec())
            }
        };
        buffer[..payload.len()].copy_from_slice(&payload);
        Some(Datagram {
            source,
            len: payload.len(),
        })
    }
}

fn wake(radio: &mut Busy, clock: &Clock, link: &mut LinkState) -> coldframe_transport::Outcome {
    let mut flashes = Flashes::new();
    let mut rtc = clock.clone();
    let report = report(
        100,
        MeasuredAt::Unsynced {
            boot_id: 1,
            uptime_ms: 0,
        },
    );
    block_on(run(
        &mut flashes,
        radio,
        &mut rtc,
        &mut trng(),
        &keys(),
        1,
        link,
        Some(&report),
    ))
}

#[test]
fn a_probe_gives_up_after_its_wait_on_a_busy_channel() {
    let clock = Clock(Rc::new(Cell::new(10_000)));
    // A neighbour's probe every 50 ms, and no Hub.
    let mut radio = Busy::new(&clock, 50, false);
    let mut link = LinkState {
        channel: Some(6),
        ..LinkState::default()
    };
    let outcome = wake(&mut radio, &clock, &mut link);
    assert_eq!(outcome.hub, Hub::NotFound { scanned: false });
    assert_eq!(outcome.sent, 0);
    // The wait shrinks with the clock and ends at 120 ms: three receives, not an endless loop
    // and not a fresh 120 ms after every datagram.
    assert_eq!(PROBE_WAIT_MS, 120);
    assert_eq!(radio.receives, [(10_000, 120), (10_050, 70), (10_100, 20)]);
    assert_eq!(clock.uptime_millis(), 10_150);
}

#[test]
fn the_node_stops_listening_at_the_window_on_a_busy_channel() {
    let clock = Clock(Rc::new(Cell::new(10_000)));
    // The Hub answers the probe; then a neighbour's probe every 100 ms and no downlink.
    let mut radio = Busy::new(&clock, 100, true);
    let mut link = LinkState {
        channel: Some(6),
        ..LinkState::default()
    };
    let outcome = wake(&mut radio, &clock, &mut link);
    assert_eq!(
        outcome.hub,
        Hub::Found {
            channel: 6,
            scanned: false
        }
    );
    assert_eq!((outcome.sent, radio.uplinks), (1, 1));
    assert!(!outcome.fresh);
    assert_eq!(outcome.downlinks, 0);
    // One receive for the probe reply, then the window: 300 ms from the last send, counted down.
    assert_eq!(ACK_WINDOW_MS, 300);
    let sent_at = radio.last_send_ms;
    assert_eq!(
        radio.receives,
        [
            (10_000, 120),
            (sent_at, 300),
            (sent_at + 100, 200),
            (sent_at + 200, 100)
        ]
    );
    assert_eq!(clock.uptime_millis(), sent_at + 300);
}
