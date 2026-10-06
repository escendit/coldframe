//! The Story 4.4 Hub rows through `Relay` and `Uplink::relay_step`, with the coldframe-hal mocks:
//! the opaque relay to `/device/ingest`, downlinks back to their Nodes, a failed request, a full
//! queue, and a probe answered while a request is in flight.

mod common;

use std::cell::RefCell;
use std::rc::Rc;

use coldframe_crypto::DeviceKeys;
use coldframe_crypto::heartbeat::Request;
use coldframe_hal::mock::{MockNet, MockRtc, MockTrng, MockWifi};
use coldframe_hal::{DATAGRAM_MAX, HttpRequest, HttpResponse, MacAddress, Net, NetError, Rtc};
use coldframe_protocol::radio::{ENVELOPE_MAX, Message, PROBE_REPLY_LENGTH};
use coldframe_uplink::base64;
use coldframe_uplink::heartbeat::HeartbeatOutcome;
use coldframe_uplink::json::unescape;
use coldframe_uplink::relay::{DOWNLINK_POOL, DOWNLINKS_PER_NODE, UPLINK_QUEUE};
use coldframe_uplink::{
    Event, Heard, INGEST_BATCH_MAX, INGEST_PATH, INGEST_REQUEST_MAX, INGEST_RESPONSE_MAX,
    IngestOutcome, Relay, RelayPort, ServerUrl, Uplink,
};
use common::{PASSWORD, SERVER, SSID, block_on, json, scan, trng, vector_keys};
use serde_json::Value;

const NOW: u64 = 1_790_000_000_000;
const NODE_A: MacAddress = [0x02, 0, 0, 0, 0, 0xA1];
const NODE_B: MacAddress = [0x02, 0, 0, 0, 0, 0xB2];
const HEARTBEAT_OK: &[u8] = br#"{"serverTime":"2026-09-29T12:34:56.789Z"}"#;

/// The relay as the firmware shares it: never borrowed across an `await`.
struct Port(RefCell<Relay>);

impl Port {
    /// A relay that has been told the Hub can relay.
    fn new() -> Self {
        let mut relay = Relay::new();
        relay.set_relaying(true);
        Self(RefCell::new(relay))
    }

    fn hear(&self, source: &MacAddress, datagram: &[u8]) -> Heard {
        self.0.borrow_mut().hear(source, datagram)
    }

    fn next_unsent(&self) -> Option<(MacAddress, Vec<u8>)> {
        let mut message = [0u8; DATAGRAM_MAX];
        let (mac, length) = self.0.borrow_mut().next_unsent(&mut message)?;
        Some((mac, message[..length].to_vec()))
    }

    /// How many downlinks are kept for `mac`.
    fn kept(&self, mac: &MacAddress) -> u8 {
        self.0.borrow().kept(mac)
    }

    /// Takes the oldest downlink kept for `mac`: offered once.
    fn take_kept(&self, mac: &MacAddress) -> Option<Vec<u8>> {
        let mut message = [0u8; DATAGRAM_MAX];
        let length = self.0.borrow_mut().take_kept(mac, &mut message)?;
        Some(message[..length].to_vec())
    }
}

impl RelayPort for Port {
    fn with<T>(&self, f: impl FnOnce(&mut Relay) -> T) -> T {
        f(&mut self.0.borrow_mut())
    }

    async fn uplink_queued(&self) {}
}

/// The mocks of one Hub that has joined and set its clock.
struct Hub {
    wifi: MockWifi,
    net: MockNet,
    rtc: MockRtc,
    trng: MockTrng,
    port: Port,
}

impl Hub {
    fn new() -> Self {
        let mut wifi = MockWifi::new(scan());
        wifi.set_join_outcome(SSID, PASSWORD, Ok(()));
        wifi.set_connected(true);
        let net = MockNet::with_link(wifi.link());
        let mut rtc = MockRtc::new();
        rtc.set_unix_time_millis(NOW).unwrap();
        Self {
            wifi,
            net,
            rtc,
            trng: trng(&[]),
            port: Port::new(),
        }
    }
}

fn uplink(keys: &DeviceKeys) -> Uplink<'_> {
    Uplink::new(SSID, PASSWORD, ServerUrl::parse(SERVER).unwrap(), keys).after_setup(0)
}

/// Runs `relay_step` on a Hub whose keys the uplink borrows.
macro_rules! relay {
    ($hub:ident, $uplink:ident) => {{
        let mut events = Vec::new();
        let relayed = block_on($uplink.relay_step(
            &$hub.wifi,
            &mut $hub.net,
            &$hub.rtc,
            &mut $hub.trng,
            &$hub.port,
            &mut |event: &Event| events.push(*event),
        ));
        (relayed, events)
    }};
}

/// An uplink radio message around `envelope`.
fn uplink_message(envelope: &[u8]) -> Vec<u8> {
    let mut message = [0u8; DATAGRAM_MAX];
    let length = Message::Uplink(envelope).encode(&mut message).unwrap();
    message[..length].to_vec()
}

fn probe(nonce: [u8; 8]) -> Vec<u8> {
    let mut message = [0u8; 9];
    Message::Probe { nonce }.encode(&mut message).unwrap();
    message.to_vec()
}

fn decode_base64(text: &str) -> Vec<u8> {
    let mut bytes = [0u8; 512];
    let length = base64::decode(text.as_bytes(), &mut bytes).expect("canonical base64");
    bytes[..length].to_vec()
}

fn fixture(name: &str) -> Value {
    json(&format!("packages/openapi/fixtures/hub/{name}"))
}

/// The sealed envelope of the ingest request fixture: the crypto-spec Node frame.
fn vector_envelope() -> Vec<u8> {
    decode_base64(
        fixture("ingest-request-frames.json")["frames"][0]
            .as_str()
            .unwrap(),
    )
}

/// The sealed downlink of the ingest response fixtures.
fn vector_downlink() -> Vec<u8> {
    decode_base64(
        fixture("ingest-response-mixed.json")["results"][0]["downlink"]
            .as_str()
            .unwrap(),
    )
}

fn mixed_response() -> Vec<u8> {
    serde_json::to_vec(&fixture("ingest-response-mixed.json")).unwrap()
}

// --- Hub relay ------------------------------------------------------------------------------

#[test]
fn two_nodes_are_relayed_in_one_post_and_the_downlink_goes_to_its_node() {
    let mut hub = Hub::new();
    let keys = vector_keys();
    let mut uplink = uplink(&keys);
    let first = vector_envelope();
    // The second Node's bytes are no envelope at all: the Hub relays them unread all the same.
    let second: Vec<u8> = (0..ENVELOPE_MAX).map(|index| (index % 251) as u8).collect();
    assert_eq!(
        hub.port.hear(&NODE_A, &uplink_message(&first)),
        Heard::Queued
    );
    assert_eq!(
        hub.port.hear(&NODE_B, &uplink_message(&second)),
        Heard::Queued
    );

    // The Server: stored with a downlink, then rejected_auth.
    hub.net.push_post(Ok((200, &mixed_response())));
    let (relayed, events) = relay!(hub, uplink);
    assert!(relayed);
    assert_eq!(
        events,
        [Event::Ingest {
            frames: 2,
            outcome: IngestOutcome::Relayed { downlinks: 1 }
        }]
    );

    // One POST with both frames in arrival order, byte-identical after base64.
    let [request] = hub.net.requests() else {
        panic!("one request");
    };
    assert_eq!(
        (request.host.as_str(), request.port, request.path.as_str()),
        ("coldframe.example.org", 443, "/device/ingest")
    );
    assert_eq!(INGEST_PATH, "/device/ingest");
    let body: Value = serde_json::from_slice(&request.body).unwrap();
    let frames = body["frames"].as_array().unwrap();
    assert_eq!(body.as_object().unwrap().len(), 1);
    assert_eq!(frames.len(), 2);
    assert_eq!(decode_base64(frames[0].as_str().unwrap()), first);
    assert_eq!(decode_base64(frames[1].as_str().unwrap()), second);
    // Exactly the fixture's text for the vector frame.
    assert_eq!(
        frames[0],
        fixture("ingest-request-frames.json")["frames"][0]
    );

    // Signed for /device/ingest with the Hub's hub-auth key.
    assert_eq!(request.headers.len(), 4);
    let device_id = keys.device_id.to_hex();
    assert_eq!(
        request.header("X-Coldframe-Device"),
        Some(std::str::from_utf8(&device_id).unwrap())
    );
    let timestamp: u64 = request
        .header("X-Coldframe-Timestamp")
        .unwrap()
        .parse()
        .unwrap();
    assert_eq!(timestamp, NOW);
    let nonce = hex(request.header("X-Coldframe-Nonce").unwrap());
    let signature = hex(request.header("X-Coldframe-Signature").unwrap());
    let signed = Request {
        method: "POST",
        path: "/device/ingest",
        body: &request.body,
        timestamp_ms: timestamp,
        nonce: nonce.as_slice().try_into().unwrap(),
    };
    assert!(signed.verify(&keys.hub_auth_key, &signature).is_ok());

    // One Downlink, to the first Node's MAC, the fixture's bytes unchanged; nothing to the second.
    let (mac, message) = hub.port.next_unsent().expect("one downlink");
    assert_eq!(mac, NODE_A);
    assert_eq!(message[0], 0x04);
    assert_eq!(message[1..], vector_downlink()[..]);
    assert_eq!(hub.port.next_unsent(), None);

    // It stays kept: the first Node's next probe is told and gets it once more, then it is
    // gone. The second Node's probe is told there is nothing.
    let Heard::Probe { reply, pending } = hub.port.hear(&NODE_A, &probe([7; 8])) else {
        panic!("a probe");
    };
    assert_eq!(pending, 1);
    assert_eq!(
        Message::decode(&reply),
        Ok(Message::ProbeReply {
            nonce: [7; 8],
            pending: 1
        })
    );
    assert_eq!(hub.port.take_kept(&NODE_A), Some(message));
    assert_eq!(hub.port.take_kept(&NODE_A), None, "offered once");
    assert!(matches!(
        hub.port.hear(&NODE_A, &probe([7; 8])),
        Heard::Probe { pending: 0, .. }
    ));
    let Heard::Probe { pending, .. } = hub.port.hear(&NODE_B, &probe([8; 8])) else {
        panic!("a probe");
    };
    assert_eq!(pending, 0);
    assert_eq!(hub.port.kept(&NODE_B), 0);

    // The batch is gone: nothing is posted a second time.
    let (relayed, events) = relay!(hub, uplink);
    assert!(!relayed);
    assert!(events.is_empty());
    assert_eq!(hub.net.requests().len(), 1);
}

#[test]
fn every_status_is_passed_on_only_as_its_downlink() {
    let mut hub = Hub::new();
    let keys = vector_keys();
    let mut uplink = uplink(&keys);
    let response = fixture("ingest-response-every-status.json");
    let results = response["results"].as_array().unwrap();
    assert_eq!(results.len(), 7);
    for index in 0..7u8 {
        let mac = [0x02, 0, 0, 0, 0, index];
        assert_eq!(
            hub.port.hear(&mac, &uplink_message(&[index + 1; 40])),
            Heard::Queued
        );
    }
    hub.net
        .push_post(Ok((200, &serde_json::to_vec(&response).unwrap())));
    let (_, events) = relay!(hub, uplink);
    assert_eq!(
        events,
        [Event::Ingest {
            frames: 7,
            outcome: IngestOutcome::Relayed { downlinks: 2 }
        }]
    );
    // stored and duplicate carry a downlink; no other status makes the Hub send anything.
    let mut sent = Vec::new();
    while let Some((mac, _)) = hub.port.next_unsent() {
        sent.push(mac[5]);
    }
    assert_eq!(sent, [0, 1]);
    for index in 2..7u8 {
        assert_eq!(hub.port.kept(&[0x02, 0, 0, 0, 0, index]), 0);
    }
}

// --- Hub POST fails -------------------------------------------------------------------------

#[test]
fn a_failed_post_drops_the_batch_and_heartbeats_continue() {
    type Answer = Result<(u16, &'static [u8]), NetError>;
    let failures: [(Answer, IngestOutcome); 5] = [
        (Err(NetError::Tls), IngestOutcome::Failed(NetError::Tls)),
        (
            Err(NetError::Timeout),
            IngestOutcome::Failed(NetError::Timeout),
        ),
        (Ok((401, b"")), IngestOutcome::Rejected { status: 401 }),
        (Ok((503, b"")), IngestOutcome::Rejected { status: 503 }),
        // 200, but not one result per frame.
        (Ok((200, br#"{"results":[]}"#)), IngestOutcome::BadReply),
    ];
    for (answer, expected) in failures {
        let mut hub = Hub::new();
        let keys = vector_keys();
        let mut uplink = uplink(&keys);
        // The slot of Node A holds an earlier downlink, already sent.
        assert!(hub.port.0.borrow_mut().keep(&NODE_A, &[0xD0; 33]));
        let earlier = hub.port.next_unsent().expect("the earlier downlink");
        assert_eq!(
            hub.port.hear(&NODE_A, &uplink_message(&[1; 60])),
            Heard::Queued
        );
        assert_eq!(
            hub.port.hear(&NODE_B, &uplink_message(&[2; 60])),
            Heard::Queued
        );

        hub.net.push_post(answer);
        let (relayed, events) = relay!(hub, uplink);
        assert!(relayed);
        assert_eq!(
            events,
            [Event::Ingest {
                frames: 2,
                outcome: expected
            }],
            "{expected:?}"
        );
        // No Downlink is sent, and what is kept is unchanged.
        assert_eq!(hub.port.next_unsent(), None, "{expected:?}");
        assert_eq!((hub.port.kept(&NODE_A), hub.port.kept(&NODE_B)), (1, 0));

        // The batch is dropped: the same bytes are never posted again.
        hub.net.push_post(Ok((200, &mixed_response())));
        let (relayed, _) = relay!(hub, uplink);
        assert!(!relayed, "{expected:?}");
        assert_eq!(hub.net.requests().len(), 1);

        // Heartbeats continue (the queued 200 is the ingest fixture: a heartbeat calls it a bad
        // reply, and the next one is accepted).
        let mut events = Vec::new();
        block_on(uplink.step(
            &mut hub.wifi,
            &mut hub.net,
            &mut hub.rtc,
            &mut hub.trng,
            &mut |event: &Event| events.push(*event),
        ));
        hub.net.push_post(Ok((200, HEARTBEAT_OK)));
        block_on(uplink.step(
            &mut hub.wifi,
            &mut hub.net,
            &mut hub.rtc,
            &mut hub.trng,
            &mut |event: &Event| events.push(*event),
        ));
        assert!(matches!(
            events.as_slice(),
            [
                Event::Heartbeat {
                    outcome: HeartbeatOutcome::BadReply,
                    ..
                },
                Event::Heartbeat {
                    outcome: HeartbeatOutcome::Accepted { .. },
                    ..
                }
            ]
        ));
        let paths: Vec<&str> = hub
            .net
            .requests()
            .iter()
            .map(|request| request.path.as_str())
            .collect();
        assert_eq!(
            paths,
            ["/device/ingest", "/device/heartbeat", "/device/heartbeat"]
        );
        // Request timestamps strictly increase over both operations.
        let stamps: Vec<u64> = hub
            .net
            .requests()
            .iter()
            .map(|request| {
                request
                    .header("X-Coldframe-Timestamp")
                    .unwrap()
                    .parse()
                    .unwrap()
            })
            .collect();
        assert!(
            stamps.windows(2).all(|pair| pair[0] < pair[1]),
            "{stamps:?}"
        );
        // The earlier downlink is still the one kept for Node A.
        assert_eq!(hub.port.take_kept(&NODE_A), Some(earlier.1));
    }
}

#[test]
fn a_downlink_that_is_not_base64_or_too_long_is_not_passed_on() {
    let mut hub = Hub::new();
    let keys = vector_keys();
    let mut uplink = uplink(&keys);
    for index in 0..3u8 {
        hub.port.hear(
            &[0x02, 0, 0, 0, 0, index],
            &uplink_message(&[index + 1; 20]),
        );
    }
    let too_long = "A".repeat(4 * 84);
    let body = format!(
        r#"{{"results":[{{"status":"stored","downlink":"not base64!"}},{{"status":"stored","downlink":"{too_long}"}},{{"status":"stored","downlink":"AQID"}}]}}"#
    );
    hub.net.push_post(Ok((200, body.as_bytes())));
    let (_, events) = relay!(hub, uplink);
    assert_eq!(
        events,
        [Event::Ingest {
            frames: 3,
            outcome: IngestOutcome::Relayed { downlinks: 1 }
        }]
    );
    let (mac, message) = hub.port.next_unsent().unwrap();
    assert_eq!((mac[5], message), (2, vec![0x04, 1, 2, 3]));
    assert_eq!(hub.port.next_unsent(), None);
}

// --- Not linked, no clock -------------------------------------------------------------------

#[test]
fn without_a_link_or_a_clock_uplinks_are_dropped_and_nothing_is_posted() {
    // Not associated.
    let mut hub = Hub::new();
    let keys = vector_keys();
    let mut uplink = uplink(&keys);
    hub.wifi.set_connected(false);
    hub.port.hear(&NODE_A, &uplink_message(&[1; 60]));
    let (relayed, events) = relay!(hub, uplink);
    assert!(relayed);
    assert_eq!(
        events,
        [Event::Ingest {
            frames: 1,
            outcome: IngestOutcome::NotLinked
        }]
    );
    assert!(hub.net.requests().is_empty());
    assert!(!hub.port.0.borrow().has_uplinks(), "dropped, not kept");

    // Associated, but the clock has not been set on this boot.
    let mut hub = Hub::new();
    let mut unset = Uplink::new(SSID, PASSWORD, ServerUrl::parse(SERVER).unwrap(), &keys);
    assert!(!unset.clock_set());
    hub.port.hear(&NODE_A, &uplink_message(&[1; 60]));
    let (_, events) = relay!(hub, unset);
    assert_eq!(
        events,
        [Event::Ingest {
            frames: 1,
            outcome: IngestOutcome::NoClock
        }]
    );
    assert!(hub.net.requests().is_empty());
    assert!(!hub.port.0.borrow().has_uplinks());

    // No IP address yet: the request is not attempted either.
    let mut hub = Hub::new();
    let mut uplink = self::uplink(&keys);
    hub.net.push_ip(Err(NetError::NoIp));
    hub.port.hear(&NODE_A, &uplink_message(&[1; 60]));
    let (_, events) = relay!(hub, uplink);
    assert_eq!(
        events,
        [Event::Ingest {
            frames: 1,
            outcome: IngestOutcome::Failed(NetError::NoIp)
        }]
    );
    assert!(hub.net.requests().is_empty());
}

// --- Hub queue full -------------------------------------------------------------------------

#[test]
fn a_full_queue_drops_the_newest_and_relays_the_earlier_ones() {
    let mut hub = Hub::new();
    let keys = vector_keys();
    let mut uplink = uplink(&keys);
    assert_eq!((UPLINK_QUEUE, INGEST_BATCH_MAX), (8, 8));
    for index in 0..8u8 {
        let heard = hub.port.hear(
            &[0x02, 0, 0, 0, 0, index],
            &uplink_message(&[index + 1; 30]),
        );
        assert_eq!(heard, Heard::Queued);
    }
    for index in 8..10u8 {
        let heard = hub.port.hear(
            &[0x02, 0, 0, 0, 0, index],
            &uplink_message(&[index + 1; 30]),
        );
        assert_eq!(heard, Heard::QueueFull);
    }
    hub.net.push_post(Ok((503, b"")));
    let (_, events) = relay!(hub, uplink);
    assert_eq!(
        events,
        [
            Event::UplinksLost { dropped: 2 },
            Event::Ingest {
                frames: 8,
                outcome: IngestOutcome::Rejected { status: 503 }
            }
        ]
    );
    let body: Value = serde_json::from_slice(&hub.net.requests()[0].body).unwrap();
    let frames: Vec<Vec<u8>> = body["frames"]
        .as_array()
        .unwrap()
        .iter()
        .map(|frame| decode_base64(frame.as_str().unwrap()))
        .collect();
    let expected: Vec<Vec<u8>> = (0..8u8).map(|index| vec![index + 1; 30]).collect();
    assert_eq!(frames, expected, "the earlier ones, in arrival order");
    // The count starts again with the next batch.
    hub.port.hear(&NODE_A, &uplink_message(&[0x55; 30]));
    hub.net.push_post(Ok((503, b"")));
    let (_, events) = relay!(hub, uplink);
    assert_eq!(events.len(), 1);
}

#[test]
fn the_largest_batch_fits_the_request_and_response_buffers() {
    let mut hub = Hub::new();
    let keys = vector_keys();
    let mut uplink = uplink(&keys);
    let mut results = Vec::new();
    for index in 0..8u8 {
        let envelope = [index + 1; ENVELOPE_MAX];
        let mac = [0x02, 0, 0, 0, 0, index];
        assert_eq!(
            hub.port.hear(&mac, &uplink_message(&envelope)),
            Heard::Queued
        );
        let mut text = [0u8; 400];
        let length = base64::encode(&envelope, &mut text).unwrap();
        results.push(format!(
            r#"{{"status":"rejected_replay","downlink":"{}"}}"#,
            std::str::from_utf8(&text[..length]).unwrap()
        ));
    }
    let response = format!(r#"{{"results":[{}]}}"#, results.join(","));
    assert!(response.len() <= INGEST_RESPONSE_MAX, "{}", response.len());
    hub.net.push_post(Ok((200, response.as_bytes())));
    let (_, events) = relay!(hub, uplink);
    assert_eq!(
        events,
        [Event::Ingest {
            frames: 8,
            outcome: IngestOutcome::Relayed { downlinks: 8 }
        }]
    );
    let request = &hub.net.requests()[0];
    assert!(request.body.len() <= INGEST_REQUEST_MAX);
    assert!(request.body.len() <= 16_384, "the contract's body limit");
    // A downlink as long as a radio message carries goes out whole.
    let (_, message) = hub.port.next_unsent().unwrap();
    assert_eq!(message.len(), DATAGRAM_MAX);
}

// --- A probe during a POST ------------------------------------------------------------------

/// A network whose `post` hears a probe and an uplink while the request is in flight.
struct Busy<'a> {
    inner: MockNet,
    port: &'a Port,
    heard: Rc<RefCell<Vec<Heard>>>,
}

impl Net for Busy<'_> {
    async fn wait_ip(&mut self, timeout_ms: u32) -> Result<(), NetError> {
        self.inner.wait_ip(timeout_ms).await
    }

    async fn sntp(&mut self, timeout_ms: u32) -> Result<u64, NetError> {
        self.inner.sntp(timeout_ms).await
    }

    async fn post(
        &mut self,
        request: &HttpRequest<'_>,
        response_body: &mut [u8],
    ) -> Result<HttpResponse, NetError> {
        // The radio task runs while the TLS exchange waits: the relay must be free.
        self.heard.borrow_mut().extend([
            self.port.hear(&NODE_B, &probe([3; 8])),
            self.port.hear(&NODE_A, &probe([4; 8])),
            self.port.hear(&NODE_B, &uplink_message(&[9; 50])),
        ]);
        self.inner.post(request, response_body).await
    }
}

#[test]
fn a_probe_is_answered_while_a_post_is_in_flight() {
    let keys = vector_keys();
    let mut uplink = uplink(&keys);
    let hub = Hub::new();
    // Node A has a kept downlink from before, and sends a frame.
    assert!(hub.port.0.borrow_mut().keep(&NODE_A, &[0xD0; 33]));
    hub.port.hear(&NODE_A, &uplink_message(&vector_envelope()));

    let heard = Rc::new(RefCell::new(Vec::new()));
    let mut inner = MockNet::new();
    inner.push_post(Ok((200, br#"{"results":[{"status":"rejected_auth"}]}"#)));
    let mut net = Busy {
        inner,
        port: &hub.port,
        heard: Rc::clone(&heard),
    };
    let mut trng = trng(&[]);
    let mut events = Vec::new();
    block_on(uplink.relay_step(
        &hub.wifi,
        &mut net,
        &hub.rtc,
        &mut trng,
        &hub.port,
        &mut |event: &Event| events.push(*event),
    ));
    assert_eq!(
        events,
        [Event::Ingest {
            frames: 1,
            outcome: IngestOutcome::Relayed { downlinks: 0 }
        }]
    );

    // Both probes got their reply during the request, and the uplink was queued for the next.
    let heard = heard.borrow();
    assert!(matches!(heard[0], Heard::Probe { pending: 0, .. }));
    assert!(matches!(heard[1], Heard::Probe { pending: 1, .. }));
    assert_eq!(heard[2], Heard::Queued);
    assert!(hub.port.0.borrow().has_uplinks());
    assert_eq!(net.inner.requests().len(), 1);
}

// --- The relay state ------------------------------------------------------------------------

#[test]
fn the_same_bytes_are_queued_once() {
    let port = Port::new();
    let message = uplink_message(&[0x42; 70]);
    assert_eq!(port.hear(&NODE_A, &message), Heard::Queued);
    assert_eq!(port.hear(&NODE_A, &message), Heard::Duplicate);
    assert_eq!(port.hear(&NODE_B, &message), Heard::Duplicate);
    let (batch, dropped) = port.0.borrow_mut().take_batch();
    assert_eq!((batch.len(), dropped), (1, 0));
    assert_eq!(batch[0].source, NODE_A);
    assert_eq!(batch[0].envelope.as_bytes(), [0x42; 70]);
}

#[test]
fn what_is_not_for_a_hub_is_ignored() {
    let port = Port::new();
    let mut reply = [0u8; PROBE_REPLY_LENGTH];
    Message::ProbeReply {
        nonce: [1; 8],
        pending: 0,
    }
    .encode(&mut reply)
    .unwrap();
    for datagram in [
        &reply[..],
        // A downlink is the Hub's to send, never to take in.
        &[0x04, 1, 2, 3],
        &[],
        &[0x03],
        &[0xFF, 0xFF],
        &[0x01, 1, 2, 3],
        &[0x03; 251],
    ] {
        assert_eq!(port.hear(&NODE_A, datagram), Heard::Ignored, "{datagram:?}");
    }
    assert!(!port.0.borrow().has_uplinks());
    assert_eq!(port.next_unsent(), None);
}

#[test]
fn two_frames_of_one_node_in_one_batch_keep_both_downlinks() {
    let mut hub = Hub::new();
    let keys = vector_keys();
    let mut uplink = uplink(&keys);
    assert_eq!(
        hub.port.hear(&NODE_A, &uplink_message(&[1; 40])),
        Heard::Queued
    );
    assert_eq!(
        hub.port.hear(&NODE_B, &uplink_message(&[2; 40])),
        Heard::Queued
    );
    assert_eq!(
        hub.port.hear(&NODE_A, &uplink_message(&[3; 40])),
        Heard::Queued
    );
    // AQID, BAUG and BwgJ are the bytes 1 2 3, 4 5 6 and 7 8 9.
    hub.net.push_post(Ok((
        200,
        br#"{"results":[{"status":"stored","downlink":"AQID"},{"status":"stored","downlink":"BAUG"},{"status":"duplicate","downlink":"BwgJ"}]}"#,
    )));
    let (_, events) = relay!(hub, uplink);
    assert_eq!(
        events,
        [Event::Ingest {
            frames: 3,
            outcome: IngestOutcome::Relayed { downlinks: 3 }
        }]
    );
    // Each goes out once on arrival, in the order of the results.
    let sent: Vec<(MacAddress, Vec<u8>)> = std::iter::from_fn(|| hub.port.next_unsent()).collect();
    assert_eq!(
        sent,
        [
            (NODE_A, vec![0x04, 1, 2, 3]),
            (NODE_B, vec![0x04, 4, 5, 6]),
            (NODE_A, vec![0x04, 7, 8, 9]),
        ]
    );
    // Node A's next probe is told of both, and gets both, oldest first, once.
    assert!(matches!(
        hub.port.hear(&NODE_A, &probe([1; 8])),
        Heard::Probe { pending: 2, .. }
    ));
    assert_eq!(hub.port.take_kept(&NODE_A), Some(vec![0x04, 1, 2, 3]));
    assert_eq!(hub.port.take_kept(&NODE_A), Some(vec![0x04, 7, 8, 9]));
    assert_eq!(hub.port.take_kept(&NODE_A), None);
    assert_eq!(hub.port.kept(&NODE_B), 1);
}

#[test]
fn an_escaped_downlink_is_unescaped_before_base64() {
    let mut hub = Hub::new();
    let keys = vector_keys();
    let mut uplink = uplink(&keys);
    for index in 0..3u8 {
        hub.port.hear(
            &[0x02, 0, 0, 0, 0, index],
            &uplink_message(&[index + 1; 20]),
        );
    }
    // The bytes FB FF FE are "+//+" in base64. System.Text.Json writes `+` as \u002B; another
    // serializer may write `/` as \/.
    let body = br#"{"results":[{"status":"stored","downlink":"\u002B//\u002b"},{"status":"stored","downlink":"+\/\/+"},{"status":"stored","downlink":"\u00e9w=="}]}"#;
    hub.net.push_post(Ok((200, body)));
    let (_, events) = relay!(hub, uplink);
    assert_eq!(
        events,
        [Event::Ingest {
            frames: 3,
            outcome: IngestOutcome::Relayed { downlinks: 2 }
        }]
    );
    let sent: Vec<(u8, Vec<u8>)> = std::iter::from_fn(|| hub.port.next_unsent())
        .map(|(mac, message)| (mac[5], message))
        .collect();
    assert_eq!(
        sent,
        [
            (0, vec![0x04, 0xFB, 0xFF, 0xFE]),
            (1, vec![0x04, 0xFB, 0xFF, 0xFE])
        ]
    );

    let mut out = [0u8; 16];
    let unescaped =
        |text: &str, out: &mut [u8]| unescape(text, out).map(|length| out[..length].to_vec());
    assert_eq!(unescaped("AQID", &mut out), Some(b"AQID".to_vec()));
    assert_eq!(
        unescaped(r"a\u002Bb\/c\\d\u0022", &mut out),
        Some(b"a+b/c\\d\"".to_vec())
    );
    assert_eq!(unescaped(r#"\""#, &mut out), Some(b"\"".to_vec()));
    for bad in [r"\", r"\u00", r"\u00zz", r"\x41", r"\u00e9", r"\u2603"] {
        assert_eq!(unescaped(bad, &mut out), None, "{bad}");
    }
    assert_eq!(unescape("toolong", &mut out[..3]), None);
}

#[test]
fn a_probe_is_answered_only_while_the_hub_can_relay() {
    let keys = vector_keys();
    // A relay nobody has told anything: the Hub has just booted.
    let fresh = Relay::new();
    let mut hub = Hub::new();
    hub.port = Port(RefCell::new(fresh));
    assert_eq!(hub.port.hear(&NODE_A, &probe([1; 8])), Heard::NotRelaying);

    // Associated with a clock: the first relay step says so, and probes are answered.
    let mut uplink = uplink(&keys);
    let _ = relay!(hub, uplink);
    assert!(matches!(
        hub.port.hear(&NODE_A, &probe([1; 8])),
        Heard::Probe { pending: 0, .. }
    ));

    // The link drops: the next relay step stops the answers.
    hub.wifi.set_connected(false);
    let _ = relay!(hub, uplink);
    assert_eq!(hub.port.hear(&NODE_A, &probe([1; 8])), Heard::NotRelaying);
    hub.wifi.set_connected(true);
    let _ = relay!(hub, uplink);
    assert!(matches!(
        hub.port.hear(&NODE_A, &probe([1; 8])),
        Heard::Probe { .. }
    ));

    // Associated, but the clock was never set on this boot.
    let mut unset = Uplink::new(SSID, PASSWORD, ServerUrl::parse(SERVER).unwrap(), &keys);
    let _ = relay!(hub, unset);
    assert_eq!(hub.port.hear(&NODE_A, &probe([1; 8])), Heard::NotRelaying);
}

#[test]
fn a_node_keeps_eight_downlinks_and_the_node_heard_longest_ago_is_evicted() {
    let port = Port::new();
    assert_eq!((DOWNLINKS_PER_NODE, DOWNLINK_POOL), (8, 24));
    let mac = |index: u8| [0x02, 0, 0, 0, 1, index];
    let keep = |index: u8, fill: u8| assert!(port.0.borrow_mut().keep(&mac(index), &[fill; 20]));

    // Nine for one Node: the oldest gives way, the rest stay in arrival order.
    for fill in 1..=9 {
        keep(0, fill);
    }
    assert_eq!(port.kept(&mac(0)), 8);
    assert_eq!(port.take_kept(&mac(0)).unwrap()[1..], [2; 20]);
    assert_eq!(port.kept(&mac(0)), 7);

    // Fill the pool: 7 for Node 0, 8 for Node 1, 8 for Node 2, 1 for Node 3.
    for fill in 1..=8 {
        keep(1, fill);
    }
    for fill in 1..=8 {
        keep(2, fill);
    }
    keep(3, 1);
    assert_eq!((0..4).map(|index| port.kept(&mac(index))).sum::<u8>(), 24);
    // A fifth Node: the Node heard from longest ago (Node 0) gives up everything kept for it.
    keep(4, 1);
    assert_eq!(
        [0, 1, 2, 3, 4].map(|index| port.kept(&mac(index))),
        [0, 8, 8, 1, 1]
    );

    // Unsent downlinks go out oldest first, each once, and stay kept for the probe.
    let mut order = Vec::new();
    while let Some((to, _)) = port.next_unsent() {
        order.push(to[5]);
    }
    let mut expected = vec![1; 8];
    expected.extend([2; 8]);
    expected.extend([3, 4]);
    assert_eq!(order, expected);
    assert_eq!(port.kept(&mac(1)), 8);

    // An empty or oversize envelope is not kept.
    assert!(!port.0.borrow_mut().keep(&mac(3), &[]));
    assert!(!port.0.borrow_mut().keep(&mac(3), &[0; ENVELOPE_MAX + 1]));
    assert_eq!(port.kept(&mac(3)), 1);
}

// --- The relay loop between two steps -------------------------------------------------------

/// One clock for the RTC and the timer: a sleep moves it.
#[derive(Clone)]
struct Clock(Rc<std::cell::Cell<u64>>);

impl Rtc for Clock {
    fn uptime_millis(&self) -> u64 {
        self.0.get()
    }

    fn unix_time_millis(&self) -> Option<u64> {
        Some(NOW + self.0.get())
    }

    fn set_unix_time_millis(&mut self, _unix_millis: u64) -> Result<(), coldframe_hal::RtcError> {
        Ok(())
    }
}

impl coldframe_hal::Timer for Clock {
    async fn sleep_ms(&mut self, ms: u32) {
        self.0.set(self.0.get() + u64::from(ms));
    }
}

/// A port whose next uplink arrives `after_ms` into the wait, once; then nothing more comes.
struct Arriving<'a> {
    port: &'a Port,
    clock: Clock,
    after_ms: std::cell::Cell<Option<u64>>,
}

impl RelayPort for Arriving<'_> {
    fn with<T>(&self, f: impl FnOnce(&mut Relay) -> T) -> T {
        self.port.with(f)
    }

    async fn uplink_queued(&self) {
        match self.after_ms.take() {
            Some(after) => {
                self.clock.0.set(self.clock.0.get() + after);
                self.port.hear(&NODE_B, &uplink_message(&[0xB0; 30]));
            }
            None => std::future::pending().await,
        }
    }
}

#[test]
fn the_relay_loop_posts_what_is_queued_and_what_arrives_and_returns_at_the_wait() {
    let keys = vector_keys();
    let mut uplink = uplink(&keys);
    let mut hub = Hub::new();
    let clock = Clock(Rc::new(std::cell::Cell::new(1_000)));
    let mut timer = clock.clone();
    let rejected = br#"{"results":[{"status":"rejected_auth"}]}"#;

    // An uplink is already queued, and another arrives 10 s into a 30 s wait.
    hub.port.hear(&NODE_A, &uplink_message(&[0xA0; 30]));
    hub.net.push_post(Ok((200, rejected)));
    hub.net.push_post(Ok((200, rejected)));
    let port = Arriving {
        port: &hub.port,
        clock: clock.clone(),
        after_ms: std::cell::Cell::new(Some(10_000)),
    };
    let mut events = Vec::new();
    block_on(uplink.relay_until(
        &hub.wifi,
        &mut hub.net,
        &clock,
        &mut timer,
        &mut hub.trng,
        &port,
        30_000,
        &mut |event: &Event| events.push(*event),
    ));
    // Both were posted, each in its own request, the first at once.
    assert_eq!(events.len(), 2);
    assert!(events.iter().all(|event| matches!(
        event,
        Event::Ingest {
            frames: 1,
            outcome: IngestOutcome::Relayed { downlinks: 0 }
        }
    )));
    let stamps: Vec<&str> = hub
        .net
        .requests()
        .iter()
        .map(|request| request.header("X-Coldframe-Timestamp").unwrap())
        .collect();
    assert_eq!(
        stamps,
        [(NOW + 1_000).to_string(), (NOW + 11_000).to_string()]
    );
    // It returned when 30 s had passed since the call: not at the arrival, and not 30 s after it.
    assert_eq!(clock.uptime_millis(), 31_000);

    // With nothing queued and nothing arriving it waits the whole time and posts nothing.
    let quiet = Arriving {
        port: &hub.port,
        clock: clock.clone(),
        after_ms: std::cell::Cell::new(None),
    };
    block_on(uplink.relay_until(
        &hub.wifi,
        &mut hub.net,
        &clock,
        &mut timer,
        &mut hub.trng,
        &quiet,
        45_000,
        &mut |event: &Event| events.push(*event),
    ));
    assert_eq!(clock.uptime_millis(), 76_000);
    assert_eq!((events.len(), hub.net.requests().len()), (2, 2));

    // A wait of zero still relays what is queued, and returns at once.
    hub.port.hear(&NODE_A, &uplink_message(&[0xA1; 30]));
    hub.net.push_post(Ok((200, rejected)));
    block_on(uplink.relay_until(
        &hub.wifi,
        &mut hub.net,
        &clock,
        &mut timer,
        &mut hub.trng,
        &quiet,
        0,
        &mut |event: &Event| events.push(*event),
    ));
    assert_eq!(clock.uptime_millis(), 76_000);
    assert_eq!(hub.net.requests().len(), 3);
}

#[test]
fn events_carry_no_payload() {
    let event = Event::Ingest {
        frames: 2,
        outcome: IngestOutcome::Relayed { downlinks: 1 },
    };
    assert_eq!(
        format!("{event:?}"),
        "Ingest { frames: 2, outcome: Relayed { downlinks: 1 } }"
    );
    for (outcome, kind) in [
        (IngestOutcome::Relayed { downlinks: 0 }, "ok"),
        (IngestOutcome::Rejected { status: 401 }, "rejected"),
        (IngestOutcome::BadReply, "bad-reply"),
        (IngestOutcome::Failed(NetError::Tls), "tls"),
        (IngestOutcome::NotLinked, "not-linked"),
        (IngestOutcome::NoClock, "no-clock"),
        (IngestOutcome::NoEntropy, "no-entropy"),
    ] {
        assert_eq!(outcome.kind(), kind);
    }
}

// --- base64 ---------------------------------------------------------------------------------

#[test]
fn base64_is_standard_and_padded() {
    // RFC 4648 section 10.
    for (plain, encoded) in [
        ("", ""),
        ("f", "Zg=="),
        ("fo", "Zm8="),
        ("foo", "Zm9v"),
        ("foob", "Zm9vYg=="),
        ("fooba", "Zm9vYmE="),
        ("foobar", "Zm9vYmFy"),
    ] {
        let mut out = [0u8; 16];
        let length = base64::encode(plain.as_bytes(), &mut out).unwrap();
        assert_eq!(std::str::from_utf8(&out[..length]).unwrap(), encoded);
        assert_eq!(base64::encoded_len(plain.len()), encoded.len());
        let mut back = [0u8; 16];
        let length = base64::decode(encoded.as_bytes(), &mut back).unwrap();
        assert_eq!(&back[..length], plain.as_bytes());
    }
    let mut out = [0u8; 4];
    assert_eq!(base64::encode(&[0xFB, 0xFF, 0xFE], &mut out), Some(4));
    assert_eq!(&out, b"+//+", "the standard alphabet, not the URL one");
    assert_eq!(base64::encode(b"foob", &mut out), None, "too small");

    let mut back = [0u8; 16];
    for bad in [
        "Zg", "Zg=", "Z===", "====", "Zm9v=", "Zg==Zm9v", "Zm=v", "Zm9-", "Zm9_", "Zm 9", "Zh==",
        "Zm9=",
    ] {
        assert_eq!(base64::decode(bad.as_bytes(), &mut back), None, "{bad}");
    }
    assert_eq!(
        base64::decode(b"Zm9vYmFy", &mut back[..5]),
        None,
        "too small"
    );
}

fn hex(text: &str) -> Vec<u8> {
    (0..text.len())
        .step_by(2)
        .map(|i| u8::from_str_radix(&text[i..i + 2], 16).expect("hex digits"))
        .collect()
}
