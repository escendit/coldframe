//! The Story 3.5 uplink rows through `Uplink::step` and `check_server`, with the coldframe-hal
//! mocks: provisioned boot, link lost, heartbeat OK and bad replies, and the setup check.

mod common;

use coldframe_crypto::DeviceKeys;
use coldframe_crypto::heartbeat::Request;
use coldframe_hal::mock::{MockNet, MockRtc, MockTimer, MockTrng, MockWifi};
use coldframe_hal::{JoinError, NetError, Rtc, Security, WifiError};
use coldframe_uplink::heartbeat::{CheckError, HeartbeatOutcome, ServerCheck, check_server};
use coldframe_uplink::uplink::ClockError;
use coldframe_uplink::{
    Event, HEARTBEAT_MAX_INTERVAL_MS, HEARTBEAT_MIN_INTERVAL_MS, IP_TIMEOUT_MS, JoinFailure,
    SERVER_CHECK_TIMEOUT_MS, SNTP_TIMEOUT_MS, ServerUrl, Uplink,
};
use common::{
    PASSWORD, SERVER, SSID, STRONG, array, block_on, bytes, heartbeat_vector, scan, text, trng,
    vector_keys,
};

const SNTP_TIME: u64 = 1_790_000_000_000;
const OK: &[u8] = br#"{"serverTime":"2026-09-29T12:34:56.789Z"}"#;
const OK_MS: u64 = 1_790_685_296_789;

/// The mocks of one Hub.
struct Hub {
    keys: DeviceKeys,
    wifi: MockWifi,
    net: MockNet,
    rtc: MockRtc,
    trng: MockTrng,
}

impl Hub {
    fn new() -> Self {
        let mut wifi = MockWifi::new(scan());
        wifi.set_join_outcome(SSID, PASSWORD, Ok(()));
        let net = MockNet::with_link(wifi.link());
        Self {
            keys: vector_keys(),
            wifi,
            net,
            rtc: MockRtc::new(),
            trng: trng(&[]),
        }
    }
}

/// Runs `step` on a Hub whose keys the uplink borrows.
macro_rules! step {
    ($hub:ident, $uplink:ident) => {{
        let mut events = Vec::new();
        let wait = block_on($uplink.step(
            &mut $hub.wifi,
            &mut $hub.net,
            &mut $hub.rtc,
            &mut $hub.trng,
            &mut |event: &Event| events.push(*event),
        ));
        (wait, events)
    }};
}

fn in_schedule(wait: u32) -> bool {
    (HEARTBEAT_MIN_INTERVAL_MS..=HEARTBEAT_MAX_INTERVAL_MS).contains(&wait)
}

#[test]
fn a_provisioned_boot_joins_sets_the_clock_then_heartbeats() {
    let mut hub = Hub::new();
    let keys = DeviceKeys::from_root_key(&array(&heartbeat_vector(), "rootKey"));
    assert_eq!(
        keys.device_id.as_bytes(),
        vector_keys().device_id.as_bytes()
    );
    let mut uplink = Uplink::new(SSID, PASSWORD, ServerUrl::parse(SERVER).unwrap(), &keys);
    hub.rtc.advance(61_000);
    hub.net.push_sntp(Ok(SNTP_TIME));
    hub.net.push_post(Ok((200, OK)));
    let vector = heartbeat_vector();
    hub.trng = trng(&bytes(&vector, "nonce"));

    // 1. Join the strongest BSSID.
    let (wait, events) = step!(hub, uplink);
    assert_eq!(wait, 0);
    assert_eq!(
        events,
        [Event::Joined {
            channel: 6,
            rssi: -50
        }]
    );
    assert_eq!(hub.wifi.join_count(), 1);
    let join = &hub.wifi.joins()[0];
    assert_eq!(
        (join.ssid.as_str(), join.bssid, join.channel),
        (SSID, STRONG, 6)
    );
    assert!(hub.net.requests().is_empty());

    // 2. DHCP, then SNTP, before any TLS.
    let (wait, events) = step!(hub, uplink);
    assert_eq!(wait, 0);
    assert_eq!(events, [Event::ClockSet { unix_ms: SNTP_TIME }]);
    assert_eq!(hub.rtc.unix_time_millis(), Some(SNTP_TIME));
    assert_eq!(hub.net.ip_timeouts(), [IP_TIMEOUT_MS]);
    assert_eq!(hub.net.sntp_timeouts(), [SNTP_TIMEOUT_MS]);
    assert!(uplink.clock_set());
    assert!(hub.net.requests().is_empty(), "no TLS before the clock");

    // 3. The heartbeat, byte for byte the crypto-spec vector.
    let (wait, events) = step!(hub, uplink);
    assert!(in_schedule(wait), "{wait}");
    assert_eq!(
        events,
        [Event::Heartbeat {
            outcome: HeartbeatOutcome::Accepted {
                server_time_ms: OK_MS
            },
            next_ms: wait
        }]
    );
    let [request] = hub.net.requests() else {
        panic!("one request");
    };
    assert_eq!(
        (request.host.as_str(), request.port, request.path.as_str()),
        ("coldframe.example.org", 443, "/device/heartbeat")
    );
    assert_eq!(request.body, bytes(&vector, "body"));
    let expected = &vector["headers"];
    assert_eq!(request.headers.len(), 4);
    for (name, value) in &request.headers {
        assert_eq!(value, text(expected, name), "{name}");
    }
    // The Hub now follows the Server's clock.
    assert_eq!(hub.rtc.unix_time_millis(), Some(OK_MS));
    // No advertising and no setup: nothing but the uplink ran.
    assert_eq!(hub.wifi.leave_count(), 0);
}

#[test]
fn heartbeats_verify_and_their_stamps_strictly_increase() {
    let mut hub = Hub::new();
    let keys = vector_keys();
    let mut uplink = Uplink::new(SSID, PASSWORD, ServerUrl::parse(SERVER).unwrap(), &keys);
    hub.net.push_sntp(Ok(SNTP_TIME));
    let _ = step!(hub, uplink);
    let _ = step!(hub, uplink);
    // The Server answers a time behind the Hub's clock every time.
    for _ in 0..5 {
        hub.net
            .push_post(Ok((200, br#"{"serverTime":"2026-09-01T00:00:00Z"}"#)));
    }
    for _ in 0..5 {
        let (wait, _) = step!(hub, uplink);
        assert!(in_schedule(wait));
    }
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
    assert_eq!(stamps.len(), 5);
    assert!(
        stamps.windows(2).all(|pair| pair[0] < pair[1]),
        "{stamps:?}"
    );
    let mut nonces: Vec<&str> = hub
        .net
        .requests()
        .iter()
        .map(|request| request.header("X-Coldframe-Nonce").unwrap())
        .collect();
    nonces.dedup();
    assert_eq!(nonces.len(), 5, "a fresh nonce every time");
    for request in hub.net.requests() {
        let nonce: Vec<u8> = (0..32)
            .step_by(2)
            .map(|i| {
                u8::from_str_radix(&request.header("X-Coldframe-Nonce").unwrap()[i..i + 2], 16)
                    .unwrap()
            })
            .collect();
        let signature: Vec<u8> = (0..64)
            .step_by(2)
            .map(|i| {
                u8::from_str_radix(
                    &request.header("X-Coldframe-Signature").unwrap()[i..i + 2],
                    16,
                )
                .unwrap()
            })
            .collect();
        let signed = Request {
            method: "POST",
            path: "/device/heartbeat",
            body: &request.body,
            timestamp_ms: request
                .header("X-Coldframe-Timestamp")
                .unwrap()
                .parse()
                .unwrap(),
            nonce: &nonce.try_into().unwrap(),
        };
        assert!(signed.verify(&keys.hub_auth_key, &signature).is_ok());
        let body: serde_json::Value = serde_json::from_slice(&request.body).unwrap();
        assert_eq!(body["protocolVersion"], 1);
        assert!(body["uptimeMs"].is_u64());
    }
}

#[test]
fn no_heartbeat_goes_out_before_the_clock_is_set() {
    let mut hub = Hub::new();
    let keys = vector_keys();
    let mut uplink = Uplink::new(SSID, PASSWORD, ServerUrl::parse(SERVER).unwrap(), &keys);
    let _ = step!(hub, uplink);
    hub.net.push_ip(Err(NetError::NoIp));
    hub.net.push_sntp(Err(NetError::Dns));
    hub.net.push_sntp(Err(NetError::Sntp));
    hub.net.push_sntp(Err(NetError::Timeout));
    let mut waits = Vec::new();
    for _ in 0..4 {
        let (wait, events) = step!(hub, uplink);
        assert!(
            matches!(events[..], [Event::ClockFailed { .. }]),
            "{events:?}"
        );
        waits.push(wait);
    }
    assert_eq!(waits, [1_000, 2_000, 4_000, 8_000]);
    assert!(hub.net.requests().is_empty(), "no TLS without a wall clock");
    assert_eq!(hub.rtc.unix_time_millis(), None);

    // The next SNTP answer sets the clock and resets its backoff.
    hub.net.push_sntp(Ok(SNTP_TIME));
    let (wait, events) = step!(hub, uplink);
    assert_eq!(
        (wait, events),
        (0, vec![Event::ClockSet { unix_ms: SNTP_TIME }])
    );
}

#[test]
fn a_clock_failure_with_the_link_down_rejoins_first() {
    let mut hub = Hub::new();
    let keys = vector_keys();
    let mut uplink = Uplink::new(SSID, PASSWORD, ServerUrl::parse(SERVER).unwrap(), &keys);
    let _ = step!(hub, uplink);
    hub.net.push_sntp_dropping_link(Err(NetError::Timeout));
    let (wait, events) = step!(hub, uplink);
    assert_eq!(wait, 0, "no clock backoff while the link is down");
    assert_eq!(
        events,
        [Event::ClockFailed {
            error: ClockError::Net(NetError::Timeout),
            retry_ms: 0
        }]
    );
    let (wait, events) = step!(hub, uplink);
    assert_eq!(wait, 0);
    assert_eq!(events[0], Event::LinkLost);
    assert!(matches!(events[1], Event::Joined { .. }));
    assert!(!uplink.clock_set());
}

#[test]
fn a_lost_link_rejoins_with_backoff_and_resets_on_success() {
    let mut hub = Hub::new();
    let keys = vector_keys();
    let mut uplink = Uplink::new(SSID, PASSWORD, ServerUrl::parse(SERVER).unwrap(), &keys)
        .after_setup(SNTP_TIME);
    hub.wifi.set_connected(true);
    hub.rtc.set_unix_time_millis(SNTP_TIME + 1_000).unwrap();
    hub.net.push_post(Ok((200, OK)));
    let (wait, _) = step!(hub, uplink);
    assert!(in_schedule(wait));

    // The access point goes away (a power cycle): every join fails until it is back.
    hub.wifi.drop_link();
    for _ in 0..9 {
        hub.wifi.push_join_outcome(Err(JoinError::NotFound));
    }
    let mut waits = Vec::new();
    for index in 0..9 {
        let (wait, events) = step!(hub, uplink);
        if index == 0 {
            assert_eq!(events[0], Event::LinkLost);
        } else {
            assert!(!events.contains(&Event::LinkLost), "reported once");
        }
        assert!(matches!(
            events.last(),
            Some(Event::JoinFailed {
                failure: JoinFailure::Join(JoinError::NotFound),
                ..
            })
        ));
        waits.push(wait);
    }
    assert_eq!(
        waits,
        [
            1_000, 2_000, 4_000, 8_000, 16_000, 32_000, 60_000, 60_000, 60_000
        ]
    );
    assert!(hub.net.requests().len() == 1, "no heartbeat while down");

    // The access point is back: the join succeeds and the heartbeats resume at once.
    let (wait, events) = step!(hub, uplink);
    assert_eq!(wait, 0);
    assert!(matches!(events[..], [Event::Joined { channel: 6, .. }]));
    hub.net.push_post(Ok((200, OK)));
    let (wait, events) = step!(hub, uplink);
    assert!(in_schedule(wait));
    assert!(matches!(
        events[..],
        [Event::Heartbeat {
            outcome: HeartbeatOutcome::Accepted { .. },
            ..
        }]
    ));
    assert!(uplink.clock_set(), "SNTP runs once per boot, not per join");
    assert_eq!(hub.net.sntp_timeouts().len(), 0);

    // The backoff restarted at 1 s.
    hub.wifi.drop_link();
    hub.wifi.push_join_outcome(Err(JoinError::Failed));
    let (wait, _) = step!(hub, uplink);
    assert_eq!(wait, 1_000);
}

#[test]
fn every_join_failure_kind_retries() {
    let mut hub = Hub::new();
    let keys = vector_keys();
    let mut uplink = Uplink::new(SSID, PASSWORD, ServerUrl::parse(SERVER).unwrap(), &keys);

    hub.wifi.fail_scan(Some(WifiError::ScanFailed));
    let (wait, events) = step!(hub, uplink);
    assert_eq!(wait, 1_000);
    assert_eq!(
        events,
        [Event::JoinFailed {
            failure: JoinFailure::Scan(WifiError::ScanFailed),
            retry_ms: 1_000
        }]
    );
    hub.wifi.fail_scan(None);

    hub.wifi.set_scan(Vec::new());
    let (wait, events) = step!(hub, uplink);
    assert_eq!(wait, 2_000);
    assert!(matches!(
        events[..],
        [Event::JoinFailed {
            failure: JoinFailure::NotHeard,
            ..
        }]
    ));

    hub.wifi.set_scan(vec![coldframe_hal::AccessPoint::new(
        SSID.as_bytes(),
        [9; 6],
        1,
        -30,
        Security::Wpa3Only,
    )]);
    let (wait, events) = step!(hub, uplink);
    assert_eq!(wait, 4_000);
    assert!(matches!(
        events[..],
        [Event::JoinFailed {
            failure: JoinFailure::Unsupported,
            ..
        }]
    ));

    hub.wifi.set_scan(scan());
    for (error, expected) in [
        (JoinError::WrongPassword, 8_000),
        (JoinError::NotFound, 16_000),
        (JoinError::Unsupported, 32_000),
        (JoinError::Failed, 60_000),
    ] {
        hub.wifi.push_join_outcome(Err(error));
        let (wait, events) = step!(hub, uplink);
        assert_eq!(wait, expected);
        assert_eq!(
            events,
            [Event::JoinFailed {
                failure: JoinFailure::Join(error),
                retry_ms: wait
            }]
        );
    }
    assert_eq!(hub.wifi.join_count(), 4);
    let (wait, _) = step!(hub, uplink);
    assert_eq!(wait, 0, "and joins in the end");
}

#[test]
fn a_network_error_with_the_link_down_rejoins_at_once() {
    let mut hub = Hub::new();
    let keys = vector_keys();
    let mut uplink =
        Uplink::new(SSID, PASSWORD, ServerUrl::parse(SERVER).unwrap(), &keys).after_setup(0);
    hub.wifi.set_connected(true);
    hub.rtc.set_unix_time_millis(SNTP_TIME).unwrap();
    hub.net.push_post_dropping_link(Err(NetError::Tls));
    let (wait, events) = step!(hub, uplink);
    assert_eq!(wait, 0);
    assert_eq!(
        events,
        [Event::Heartbeat {
            outcome: HeartbeatOutcome::Failed(NetError::Tls),
            next_ms: 0
        }]
    );
    let (wait, events) = step!(hub, uplink);
    assert_eq!(wait, 0);
    assert_eq!(events[0], Event::LinkLost);
    assert!(matches!(events[1], Event::Joined { .. }));
    assert_eq!(hub.wifi.join_count(), 1);
}

#[test]
fn a_network_error_with_the_link_up_keeps_the_schedule() {
    for error in [
        NetError::Dns,
        NetError::Tls,
        NetError::Http,
        NetError::Timeout,
    ] {
        let mut hub = Hub::new();
        let keys = vector_keys();
        let mut uplink =
            Uplink::new(SSID, PASSWORD, ServerUrl::parse(SERVER).unwrap(), &keys).after_setup(0);
        hub.wifi.set_connected(true);
        hub.rtc.set_unix_time_millis(SNTP_TIME).unwrap();
        hub.net.push_post(Err(error));
        let (wait, events) = step!(hub, uplink);
        assert!(in_schedule(wait));
        assert_eq!(
            events,
            [Event::Heartbeat {
                outcome: HeartbeatOutcome::Failed(error),
                next_ms: wait
            }]
        );
        assert_eq!(hub.wifi.join_count(), 0, "no re-join while the link is up");
        assert_eq!(hub.rtc.unix_time_millis(), Some(SNTP_TIME));
    }
    // No IP at the tick (DHCP lost) is a network error too.
    let mut hub = Hub::new();
    let keys = vector_keys();
    let mut uplink =
        Uplink::new(SSID, PASSWORD, ServerUrl::parse(SERVER).unwrap(), &keys).after_setup(0);
    hub.wifi.set_connected(true);
    hub.rtc.set_unix_time_millis(SNTP_TIME).unwrap();
    hub.net.push_ip(Err(NetError::NoIp));
    let (wait, events) = step!(hub, uplink);
    assert!(in_schedule(wait));
    assert!(matches!(
        events[..],
        [Event::Heartbeat {
            outcome: HeartbeatOutcome::Failed(NetError::NoIp),
            ..
        }]
    ));
    assert!(hub.net.requests().is_empty());
}

#[test]
fn bad_replies_leave_the_clock_and_keep_the_schedule() {
    for (answer, outcome) in [
        (
            (
                401,
                &br#"{"type":"urn:coldframe:problem:device-unauthorized"}"#[..],
            ),
            HeartbeatOutcome::Rejected { status: 401 },
        ),
        ((500, &b""[..]), HeartbeatOutcome::Rejected { status: 500 }),
        ((204, &b""[..]), HeartbeatOutcome::Rejected { status: 204 }),
        ((200, &b"not json"[..]), HeartbeatOutcome::BadReply),
        ((200, &b"{}"[..]), HeartbeatOutcome::BadReply),
        (
            (200, &br#"{"time":"2026-09-29T12:34:56Z"}"#[..]),
            HeartbeatOutcome::BadReply,
        ),
        (
            (200, &br#"{"serverTime":"2026-09-29T12:34:56+02:00"}"#[..]),
            HeartbeatOutcome::BadReply,
        ),
        (
            (200, &[b' '; 600][..]),
            HeartbeatOutcome::Failed(NetError::Http),
        ),
    ] {
        let mut hub = Hub::new();
        let keys = vector_keys();
        let mut uplink =
            Uplink::new(SSID, PASSWORD, ServerUrl::parse(SERVER).unwrap(), &keys).after_setup(0);
        hub.wifi.set_connected(true);
        hub.rtc.set_unix_time_millis(SNTP_TIME).unwrap();
        hub.net.push_post(Ok(answer));
        let (wait, events) = step!(hub, uplink);
        assert!(in_schedule(wait));
        assert_eq!(
            events,
            [Event::Heartbeat {
                outcome,
                next_ms: wait
            }]
        );
        assert_eq!(hub.rtc.unix_time_millis(), Some(SNTP_TIME), "{outcome:?}");
    }
}

#[test]
fn an_accepted_heartbeat_with_extra_fields_sets_the_clock() {
    let mut hub = Hub::new();
    let keys = vector_keys();
    let mut uplink =
        Uplink::new(SSID, PASSWORD, ServerUrl::parse(SERVER).unwrap(), &keys).after_setup(0);
    hub.wifi.set_connected(true);
    hub.rtc.set_unix_time_millis(SNTP_TIME).unwrap();
    hub.net.push_post(Ok((
        200,
        br#"{"later":{"a":[1,2]},"serverTime":"2026-09-29T12:34:56Z","more":true}"#,
    )));
    let (_, events) = step!(hub, uplink);
    assert!(matches!(
        events[..],
        [Event::Heartbeat {
            outcome: HeartbeatOutcome::Accepted {
                server_time_ms: 1_790_685_296_000
            },
            ..
        }]
    ));
    assert_eq!(hub.rtc.unix_time_millis(), Some(1_790_685_296_000));
}

#[test]
fn after_setup_the_first_stamp_goes_above_the_checks() {
    let mut hub = Hub::new();
    let keys = vector_keys();
    let check_stamp = SNTP_TIME + 10_000;
    let mut uplink = Uplink::new(SSID, PASSWORD, ServerUrl::parse(SERVER).unwrap(), &keys)
        .after_setup(check_stamp);
    assert!(uplink.clock_set());
    hub.wifi.set_connected(true);
    // The Server's time was behind the Hub's SNTP time.
    hub.rtc.set_unix_time_millis(SNTP_TIME).unwrap();
    hub.net.push_post(Ok((200, OK)));
    let _ = step!(hub, uplink);
    let stamp: u64 = hub.net.requests()[0]
        .header("X-Coldframe-Timestamp")
        .unwrap()
        .parse()
        .unwrap();
    assert_eq!(stamp, check_stamp + 1);
    assert!(hub.net.sntp_timeouts().is_empty(), "no second SNTP");
    assert_eq!(hub.wifi.join_count(), 0, "already joined by setup");
}

#[test]
fn a_custom_port_reaches_the_request() {
    let mut hub = Hub::new();
    let keys = vector_keys();
    let mut uplink = Uplink::new(
        SSID,
        PASSWORD,
        ServerUrl::parse("https://coldframe.example.org:8443").unwrap(),
        &keys,
    )
    .after_setup(0);
    hub.wifi.set_connected(true);
    hub.rtc.set_unix_time_millis(SNTP_TIME).unwrap();
    hub.net.push_post(Ok((200, OK)));
    let _ = step!(hub, uplink);
    assert_eq!(hub.net.requests()[0].port, 8443);
}

#[test]
fn events_carry_nothing_secret() {
    let mut hub = Hub::new();
    let keys = vector_keys();
    let mut uplink = Uplink::new(SSID, PASSWORD, ServerUrl::parse(SERVER).unwrap(), &keys);
    hub.net.push_sntp(Ok(SNTP_TIME));
    hub.net.push_post(Ok((200, OK)));
    let mut log = String::new();
    for _ in 0..3 {
        let (_, events) = step!(hub, uplink);
        log.push_str(&format!("{events:?}"));
    }
    let request = &hub.net.requests()[0];
    for secret in [
        PASSWORD,
        request.header("X-Coldframe-Nonce").unwrap(),
        request.header("X-Coldframe-Signature").unwrap(),
        std::str::from_utf8(&request.body).unwrap(),
    ] {
        assert!(!log.contains(secret), "{secret} in {log}");
    }
}

// ---------------------------------------------------------------------------------------------
// The setup session's Server check

fn check(hub: &mut Hub, timer: &mut MockTimer) -> Result<ServerCheck, CheckError> {
    let server = ServerUrl::parse(SERVER).unwrap();
    block_on(check_server(
        &mut hub.net,
        &mut hub.rtc,
        timer,
        &mut hub.trng,
        &hub.keys,
        &server,
    ))
}

#[test]
fn the_server_check_sets_the_clock_and_adopts_server_time() {
    let mut hub = Hub::new();
    hub.wifi.set_connected(true);
    hub.net.push_sntp(Ok(SNTP_TIME));
    hub.net.push_post(Ok((200, OK)));
    let mut timer = MockTimer::new();
    let result = check(&mut hub, &mut timer);
    assert_eq!(
        result,
        Ok(ServerCheck {
            server_time_ms: OK_MS,
            stamp_ms: SNTP_TIME
        })
    );
    assert_eq!(hub.rtc.unix_time_millis(), Some(OK_MS));
    assert_eq!(hub.net.ip_timeouts(), [SERVER_CHECK_TIMEOUT_MS]);
    assert_eq!(hub.net.sntp_timeouts(), [SNTP_TIMEOUT_MS]);
    assert_eq!(hub.net.requests().len(), 1);
    assert_eq!(hub.net.requests()[0].path, "/device/heartbeat");
    // The check finished before its 40 s deadline was ever polled.
    assert!(timer.sleeps().is_empty());
}

#[test]
fn every_server_check_failure_is_reported() {
    type Script = fn(&mut MockNet);
    let cases: Vec<(Script, CheckError)> = vec![
        (
            |net| net.push_ip(Err(NetError::NoIp)),
            CheckError::Net(NetError::NoIp),
        ),
        (
            |net| net.push_sntp(Err(NetError::Dns)),
            CheckError::Net(NetError::Dns),
        ),
        (
            |net| net.push_sntp(Err(NetError::Sntp)),
            CheckError::Net(NetError::Sntp),
        ),
        (
            |net| {
                net.push_sntp(Ok(SNTP_TIME));
                net.push_post(Err(NetError::Tls));
            },
            CheckError::Net(NetError::Tls),
        ),
        (
            |net| {
                net.push_sntp(Ok(SNTP_TIME));
                net.push_post(Err(NetError::Timeout));
            },
            CheckError::Net(NetError::Timeout),
        ),
        (
            |net| {
                net.push_sntp(Ok(SNTP_TIME));
                net.push_post(Ok((401, b"")));
            },
            CheckError::Status(401),
        ),
        (
            |net| {
                net.push_sntp(Ok(SNTP_TIME));
                net.push_post(Ok((200, b"{}")));
            },
            CheckError::BadReply,
        ),
    ];
    for (script, expected) in cases {
        let mut hub = Hub::new();
        hub.wifi.set_connected(true);
        script(&mut hub.net);
        let mut timer = MockTimer::new();
        assert_eq!(check(&mut hub, &mut timer), Err(expected));
        assert_ne!(
            hub.rtc.unix_time_millis(),
            Some(OK_MS),
            "no server time adopted"
        );
    }
}

#[test]
fn a_server_check_that_would_overrun_its_budget_times_out() {
    // The whole budget is spent before the heartbeat: nothing is sent.
    struct SlowNet {
        inner: MockNet,
        rtc: std::rc::Rc<std::cell::Cell<u64>>,
    }
    impl coldframe_hal::Net for SlowNet {
        async fn wait_ip(&mut self, timeout_ms: u32) -> Result<(), NetError> {
            self.rtc.set(self.rtc.get() + u64::from(timeout_ms));
            self.inner.wait_ip(timeout_ms).await
        }
        async fn sntp(&mut self, timeout_ms: u32) -> Result<u64, NetError> {
            self.inner.sntp(timeout_ms).await
        }
        async fn post(
            &mut self,
            request: &coldframe_hal::HttpRequest<'_>,
            body: &mut [u8],
        ) -> Result<coldframe_hal::HttpResponse, NetError> {
            self.inner.post(request, body).await
        }
    }
    struct SharedRtc(std::rc::Rc<std::cell::Cell<u64>>, MockRtc);
    impl Rtc for SharedRtc {
        fn uptime_millis(&self) -> u64 {
            self.0.get()
        }
        fn unix_time_millis(&self) -> Option<u64> {
            self.1.unix_time_millis()
        }
        fn set_unix_time_millis(&mut self, unix: u64) -> Result<(), coldframe_hal::RtcError> {
            self.1.set_unix_time_millis(unix)
        }
    }
    let uptime = std::rc::Rc::new(std::cell::Cell::new(0));
    let mut net = SlowNet {
        inner: MockNet::new(),
        rtc: uptime.clone(),
    };
    net.inner.push_sntp(Ok(SNTP_TIME));
    net.inner.push_post(Ok((200, OK)));
    let mut rtc = SharedRtc(uptime, MockRtc::new());
    let mut timer = MockTimer::new();
    let mut trng = trng(&[]);
    let keys = vector_keys();
    let result = block_on(check_server(
        &mut net,
        &mut rtc,
        &mut timer,
        &mut trng,
        &keys,
        &ServerUrl::parse(SERVER).unwrap(),
    ));
    assert_eq!(result, Err(CheckError::Timeout));
    assert!(net.inner.requests().is_empty());
    assert!(net.inner.sntp_timeouts().is_empty());
}
