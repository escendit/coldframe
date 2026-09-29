//! Every row of the Story 3.4 I/O matrix, and the Story 3.5 setup rows (the Server check,
//! `NO_SERVER`, `server_url`), through `run_setup`, with the coldframe-hal mocks and the
//! crypto-spec vectors.

mod common;

use std::cell::RefCell;
use std::rc::Rc;

use coldframe_crypto::DeviceKeys;
use coldframe_crypto::hpke::fingerprint;
use coldframe_hal::mock::{
    LinkEvent, MockFlash, MockNet, MockRadio, MockRtc, MockSetupLink, MockTimer, MockTrng, MockWifi,
};
use coldframe_hal::{
    AccessPoint, FlashError, JoinError, LinkError, NetError, Radio, Rtc, Security, Wifi, WifiError,
};
use coldframe_protocol::setup_v1::SetupMessage_::Body;
use coldframe_protocol::setup_v1::{
    EnrolmentRequest, Identity, SealedSetupMessage, SessionHello, SetupMessage, SiteBinding,
    WifiResult,
};
use coldframe_protocol::{PROTOCOL_VERSION, encode};
use coldframe_setup::app::{
    AppError, enrolment_request, identity_request, site_binding, wifi_config, wifi_scan_request,
};
use coldframe_setup::code::SetupCode;
use coldframe_setup::framing::{HEADER_LAST, HEADER_MORE};
use coldframe_setup::store::{
    PARTITION_SIZE, PROVISIONING_OFFSET, ProvisioningState, load_provisioning, write_setup_code,
};
use coldframe_setup::{MAX_FRAME, Provisioned, SESSION_IDLE_TIMEOUT_MS, SetupError, run_setup};
use common::{
    AppPeer, Log, PAYLOAD_MTU_23, PAYLOAD_MTU_251, Reply, Step, array, block_on, text, vectors,
};

const FIRMWARE: &str = "1.2.3";
const SITE: &str = "0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b";
const SSID: &str = "garden";
/// A test fixture, not a real credential.
const PASSWORD: &str = "fixture-passphrase";
/// A test Server; never a real one.
const SERVER: &str = "https://coldframe.example.org";
/// What SNTP answers during the Server check.
const SNTP_TIME: u64 = 1_790_000_000_000;
/// The Server's answer to an accepted heartbeat.
const ACCEPTED: &[u8] = br#"{"serverTime":"2026-09-29T12:34:56.789Z"}"#;
const ACCEPTED_MS: u64 = 1_790_685_296_789;

// SetupErrorCode and WifiStatus values.
const MALFORMED: i32 = 1;
const UNEXPECTED: i32 = 2;
const FINGERPRINT_MISMATCH: i32 = 3;
const INTERNAL: i32 = 4;
const CONNECTED: i32 = 1;
const WRONG_PASSWORD: i32 = 2;
const NETWORK_NOT_FOUND: i32 = 3;
const UNSUPPORTED_SECURITY: i32 = 4;
const NO_SERVER: i32 = 5;
// WifiSecurity values.
const OPEN: i32 = 1;
const WPA2: i32 = 2;
const WPA3_TRANSITION: i32 = 3;
const WPA3_ONLY: i32 = 4;

const STRONG_GARDEN: [u8; 6] = [0x02, 0, 0, 0, 0, 0xB0];

/// The fixture world of one test: the vectors, the Device and its mocks.
struct World {
    keys: DeviceKeys,
    code: SetupCode,
    app_private: [u8; 32],
    hub_private: [u8; 32],
    hub_public: [u8; 32],
    ikm_e: [u8; 32],
    server_key: [u8; 32],
    device_id: Vec<u8>,
    enc: Vec<u8>,
    ciphertext: Vec<u8>,
    pop_code: String,
    wrong_code: String,
    link: MockSetupLink,
    wifi: MockWifi,
    net: MockNet,
    rtc: MockRtc,
    timer: MockTimer,
    flash: MockFlash,
    log: Rc<RefCell<Log>>,
}

fn scan() -> Vec<AccessPoint> {
    vec![
        AccessPoint::new(
            b"garden",
            [0x02, 0, 0, 0, 0, 0xA0],
            1,
            -70,
            Security::Wpa2Personal,
        ),
        AccessPoint::new(b"garden", STRONG_GARDEN, 6, -50, Security::Wpa2Personal),
        AccessPoint::new(
            b"neighbour",
            [0x02, 0, 0, 0, 0, 0xC0],
            11,
            -60,
            Security::Wpa3Only,
        ),
        AccessPoint::new(
            b"",
            [0x02, 0, 0, 0, 0, 0xD0],
            3,
            -40,
            Security::Wpa2Personal,
        ),
        AccessPoint::new(b"cafe", [0x02, 0, 0, 0, 0, 0xE0], 3, -80, Security::Open),
        AccessPoint::new(
            b"mixed",
            [0x02, 0, 0, 0, 0, 0xF0],
            9,
            -65,
            Security::Wpa3Transition,
        ),
        AccessPoint::new(b"corp", [0x02, 0, 0, 0, 1, 0x00], 1, -75, Security::Other),
    ]
}

impl World {
    fn new() -> Self {
        let all = vectors();
        let setup = &all["setup"][0];
        let enrolment = &all["enrolment"][0];
        let code = SetupCode::parse(text(setup, "normalizedPopCode").as_bytes()).unwrap();
        let mut flash = MockFlash::new(PARTITION_SIZE);
        write_setup_code(&mut flash, &code).unwrap();
        let mut wifi = MockWifi::new(scan());
        wifi.set_join_outcome(SSID, PASSWORD, Ok(()));
        let net = MockNet::with_link(wifi.link());
        let mut world = Self {
            keys: DeviceKeys::from_root_key(&array(enrolment, "rootKey")),
            code,
            app_private: array(setup, "appPrivateKey"),
            hub_private: array(setup, "hubPrivateKey"),
            hub_public: array(setup, "hubPublicKey"),
            ikm_e: array(enrolment, "ikmE"),
            server_key: array(enrolment, "recipientPublicKey"),
            device_id: common::bytes(enrolment, "deviceId"),
            enc: common::bytes(enrolment, "enc"),
            ciphertext: common::bytes(enrolment, "ciphertext"),
            pop_code: text(setup, "popCode").to_owned(),
            wrong_code: text(setup, "wrongPopCode").to_owned(),
            link: MockSetupLink::new(),
            wifi,
            net,
            rtc: MockRtc::new(),
            timer: MockTimer::new(),
            flash,
            log: Rc::new(RefCell::new(Log::default())),
        };
        world.server_accepts();
        world
    }

    /// Scripts one Server check that passes: SNTP answers, the heartbeat is accepted.
    fn server_accepts(&mut self) {
        self.net.push_sntp(Ok(SNTP_TIME));
        self.net.push_post(Ok((200, ACCEPTED)));
    }

    /// Queues a connection of an app that types `code` and plays `steps`.
    fn app(&mut self, code: &str, max_payload: usize, steps: Vec<Step>) {
        let code = code.to_owned();
        AppPeer::new(
            self.app_private,
            &code,
            max_payload,
            steps,
            Rc::clone(&self.log),
        )
        .connect(&mut self.link);
    }

    /// Runs the service with a TRNG that yields `trng` first.
    fn run(&mut self, trng: &[u8]) -> Result<Provisioned, SetupError> {
        let mut radio = MockRadio::new();
        radio.enable().unwrap();
        let mut trng = MockTrng::with_bytes(&radio, trng);
        block_on(run_setup(
            &mut self.link,
            &mut self.wifi,
            &mut self.net,
            &mut self.rtc,
            &mut self.timer,
            &mut trng,
            &mut self.flash,
            &self.keys,
            &self.code,
            FIRMWARE,
        ))
    }

    /// The TRNG bytes of the happy path: the session key, then the enrolment `ikm_e`.
    fn vector_trng(&self) -> Vec<u8> {
        [self.hub_private.as_slice(), self.ikm_e.as_slice()].concat()
    }

    fn replies(&self) -> Vec<Reply> {
        std::mem::take(&mut self.log.borrow_mut().replies)
    }

    fn provisioning_erased(&mut self) -> bool {
        matches!(
            load_provisioning(&mut self.flash).unwrap(),
            ProvisioningState::Unprovisioned
        )
    }

    fn enrolment(&self) -> Reply {
        Reply::Enrolment(
            self.device_id.clone(),
            self.enc.clone(),
            self.ciphertext.clone(),
        )
    }

    /// An `EnrolmentResponse` reply, whatever its HPKE bytes (the TRNG was not the vectors').
    fn enrolment_ignoring_vectors(&self, reply: &Reply) -> Reply {
        match reply {
            Reply::Enrolment(device_id, ..) if *device_id == self.device_id => reply.clone(),
            other => panic!("expected an enrolment response, got {other:?}"),
        }
    }

    fn enrol(&self) -> Step {
        Step::Send(enrolment_request(&self.server_key, None).unwrap(), true)
    }
}

fn bind() -> Step {
    Step::Send(site_binding(SITE, SERVER).unwrap(), false)
}

fn config(ssid: &str, password: &str) -> Step {
    Step::Send(wifi_config(ssid, password).unwrap(), true)
}

fn identity() -> Step {
    Step::Send(identity_request(), true)
}

fn sealed_message(body: Body) -> Vec<u8> {
    let message = SetupMessage {
        protocol_version: PROTOCOL_VERSION,
        body: Some(body),
    };
    let mut buffer = vec![0u8; MAX_FRAME];
    let length = encode(&message, &mut buffer).unwrap();
    buffer.truncate(length);
    buffer
}

#[test]
fn happy_session_matches_the_vectors_and_provisions() {
    for max_payload in [PAYLOAD_MTU_23, PAYLOAD_MTU_251] {
        let mut world = World::new();
        let pop_code = world.pop_code.clone();
        let steps = vec![
            identity(),
            Step::Send(wifi_scan_request(), true),
            bind(),
            world.enrol(),
            config(SSID, PASSWORD),
        ];
        world.app(&pop_code, max_payload, steps);
        let trng = world.vector_trng();
        let provisioned = world.run(&trng).expect("provisioned");

        assert_eq!(world.log.borrow().device_keys, vec![world.hub_public]);
        let replies = world.replies();
        assert_eq!(
            replies,
            vec![
                Reply::Identity(world.device_id.clone(), 1, FIRMWARE.to_owned()),
                Reply::ScanList(vec![
                    ("garden".into(), STRONG_GARDEN.to_vec(), -50, WPA2, 6),
                    (
                        "neighbour".into(),
                        vec![2, 0, 0, 0, 0, 0xC0],
                        -60,
                        WPA3_ONLY,
                        11
                    ),
                    (
                        "mixed".into(),
                        vec![2, 0, 0, 0, 0, 0xF0],
                        -65,
                        WPA3_TRANSITION,
                        9
                    ),
                    ("corp".into(), vec![2, 0, 0, 0, 1, 0], -75, 5, 1),
                    ("cafe".into(), vec![2, 0, 0, 0, 0, 0xE0], -80, OPEN, 3),
                ]),
                world.enrolment(),
                Reply::WifiResult(CONNECTED),
            ]
        );

        // The strongest BSSID for the SSID, on its channel.
        assert_eq!(world.wifi.join_count(), 1);
        let join = &world.wifi.joins()[0];
        assert_eq!((join.bssid, join.channel), (STRONG_GARDEN, 6));

        assert_eq!(provisioned.record.ssid(), SSID);
        assert_eq!(provisioned.record.password(), PASSWORD);
        assert_eq!(provisioned.record.site_id(), SITE);
        assert_eq!(provisioned.record.server_url(), SERVER);
        let ProvisioningState::Provisioned(stored) = load_provisioning(&mut world.flash).unwrap()
        else {
            panic!("CFWP written");
        };
        assert_eq!(
            (
                stored.ssid(),
                stored.password(),
                stored.site_id(),
                stored.server_url()
            ),
            (SSID, PASSWORD, SITE, SERVER)
        );
        let at = usize::try_from(PROVISIONING_OFFSET).unwrap();
        assert_eq!(&world.flash.contents()[at..at + 5], b"CFWP\x02");

        // The Server check: DHCP, SNTP, then one signed heartbeat to the bound Server; the Hub
        // then follows the Server's clock and stays joined.
        assert_eq!(world.net.sntp_timeouts().len(), 1);
        let [request] = world.net.requests() else {
            panic!("one heartbeat");
        };
        assert_eq!(
            (request.host.as_str(), request.port, request.path.as_str()),
            ("coldframe.example.org", 443, "/device/heartbeat")
        );
        assert_eq!(
            request.header("X-Coldframe-Device"),
            Some(std::str::from_utf8(&world.keys.device_id.to_hex()).unwrap())
        );
        assert_eq!(
            request.header("X-Coldframe-Timestamp"),
            Some("1790000000000")
        );
        assert_eq!(world.rtc.unix_time_millis(), Some(ACCEPTED_MS));
        assert_eq!(provisioned.check.server_time_ms, ACCEPTED_MS);
        assert_eq!(provisioned.check.stamp_ms, SNTP_TIME);
        assert!(world.wifi.is_connected());
        assert_eq!(world.wifi.leave_count(), 0);

        // The session then ended at the idle timeout and the service returned.
        assert_eq!(world.link.accept_count(), 1);
        assert_eq!(world.link.timeouts(), &[SESSION_IDLE_TIMEOUT_MS]);
        assert!(!world.link.is_connected());
        // Every notification fits the connection's payload size.
        assert!(world.link.sent()[0].iter().all(|p| p.len() <= max_payload));
    }
}

#[test]
fn scan_list_is_deduped_strongest_first_and_capped_at_16() {
    let mut world = World::new();
    let mut heard = Vec::new();
    for index in 0..20i8 {
        let ssid = format!("net-{index:02}");
        heard.push(AccessPoint::new(
            ssid.as_bytes(),
            [1; 6],
            1,
            -90 + index,
            Security::Open,
        ));
        heard.push(AccessPoint::new(
            ssid.as_bytes(),
            [2; 6],
            2,
            -95 + index,
            Security::Open,
        ));
    }
    world.wifi = MockWifi::new(heard);
    let code = world.pop_code.clone();
    world.app(
        &code,
        PAYLOAD_MTU_23,
        vec![Step::Send(wifi_scan_request(), true)],
    );
    assert_eq!(
        world.run(&[]).err(),
        Some(SetupError::Link(LinkError::Transport))
    );
    let replies = world.replies();
    let [Reply::ScanList(networks)] = replies.as_slice() else {
        panic!("one scan list, got {replies:?}");
    };
    assert_eq!(networks.len(), 16);
    assert_eq!(networks[0].0, "net-19");
    assert_eq!(networks[0].2, -71);
    assert!(networks.windows(2).all(|pair| pair[0].2 >= pair[1].2));
    assert!(networks.iter().all(|n| n.1 == vec![1; 6]));
}

#[test]
fn a_failed_scan_is_internal() {
    let mut world = World::new();
    world.wifi.fail_scan(Some(WifiError::ScanFailed));
    let code = world.pop_code.clone();
    world.app(
        &code,
        PAYLOAD_MTU_23,
        vec![Step::Send(wifi_scan_request(), true), identity()],
    );
    let _ = world.run(&[]);
    assert_eq!(world.replies()[0], Reply::Error(INTERNAL));
}

#[test]
fn a_wrong_code_gets_one_sealed_error_then_a_disconnect() {
    let mut world = World::new();
    let wrong = world.wrong_code.clone();
    world.app(&wrong, PAYLOAD_MTU_23, vec![identity(), identity()]);
    let result = world.run(&world.vector_trng());

    assert_eq!(result.err(), Some(SetupError::Link(LinkError::Transport)));
    assert_eq!(
        world.replies(),
        vec![Reply::Failed(AppError::WrongSetupCode)]
    );
    assert_eq!(world.link.disconnect_count(), 1);
    assert!(
        world.link.timeouts().is_empty(),
        "closed at once, not idled out"
    );
    assert!(world.provisioning_erased());
    assert_eq!(world.wifi.join_count(), 0);
}

#[test]
fn a_wrong_code_then_the_right_one_provisions() {
    let mut world = World::new();
    let (wrong, right) = (world.wrong_code.clone(), world.pop_code.clone());
    world.app(&wrong, PAYLOAD_MTU_23, vec![identity()]);
    let steps = vec![bind(), world.enrol(), config(SSID, PASSWORD)];
    world.app(&right, PAYLOAD_MTU_251, steps);
    assert!(world.run(&[]).is_ok());
    let replies = world.replies();
    assert_eq!(replies[0], Reply::Failed(AppError::WrongSetupCode));
    assert_eq!(replies.last(), Some(&Reply::WifiResult(CONNECTED)));
    assert_eq!(world.link.accept_count(), 2);
}

#[test]
fn a_wrong_password_stores_nothing_and_allows_a_retry() {
    let mut world = World::new();
    let code = world.pop_code.clone();
    world.app(
        &code,
        PAYLOAD_MTU_23,
        vec![bind(), world.enrol(), config(SSID, "wrong-passphrase")],
    );
    assert!(world.run(&[]).is_err());
    assert_eq!(
        world.replies().last(),
        Some(&Reply::WifiResult(WRONG_PASSWORD))
    );
    assert!(world.provisioning_erased());

    let mut world = World::new();
    world.app(
        &code,
        PAYLOAD_MTU_23,
        vec![
            bind(),
            world.enrol(),
            config(SSID, "wrong-passphrase"),
            config(SSID, PASSWORD),
        ],
    );
    let provisioned = world.run(&[]).expect("the retry provisions");
    assert_eq!(
        world.replies()[1..],
        [
            Reply::WifiResult(WRONG_PASSWORD),
            Reply::WifiResult(CONNECTED)
        ]
    );
    assert_eq!(provisioned.record.password(), PASSWORD);
    assert_eq!(world.link.accept_count(), 1, "one session");
}

#[test]
fn unheard_and_unsupported_networks_are_refused_without_joining() {
    let mut world = World::new();
    let code = world.pop_code.clone();
    world.app(
        &code,
        PAYLOAD_MTU_251,
        vec![
            bind(),
            world.enrol(),
            config("absent", PASSWORD),
            config("neighbour", PASSWORD),
            config("corp", PASSWORD),
        ],
    );
    assert!(world.run(&[]).is_err());
    assert_eq!(
        world.replies()[1..],
        [
            Reply::WifiResult(NETWORK_NOT_FOUND),
            Reply::WifiResult(UNSUPPORTED_SECURITY),
            Reply::WifiResult(UNSUPPORTED_SECURITY),
        ]
    );
    assert_eq!(world.wifi.join_count(), 0);
    assert!(world.provisioning_erased());
}

#[test]
fn join_outcomes_map_to_results() {
    for (outcome, expected) in [
        (JoinError::NotFound, Reply::WifiResult(NETWORK_NOT_FOUND)),
        (
            JoinError::Unsupported,
            Reply::WifiResult(UNSUPPORTED_SECURITY),
        ),
        (JoinError::Failed, Reply::Error(INTERNAL)),
    ] {
        let mut world = World::new();
        world.wifi.set_join_outcome("cafe", "", Err(outcome));
        let code = world.pop_code.clone();
        world.app(
            &code,
            PAYLOAD_MTU_23,
            vec![bind(), world.enrol(), config("cafe", ""), identity()],
        );
        assert!(world.run(&[]).is_err());
        let replies = world.replies();
        assert_eq!(replies[1], expected);
        // The session continues.
        assert!(matches!(replies[2], Reply::Identity(..)));
        assert!(world.provisioning_erased());
    }
}

#[test]
fn out_of_order_messages_are_unexpected_and_the_session_continues() {
    let mut world = World::new();
    let code = world.pop_code.clone();
    let mut lot = SiteBinding {
        site_id: SITE.try_into().unwrap(),
        ..SiteBinding::default()
    };
    lot.set_lot_id(SITE.try_into().unwrap());
    let app_identity = Body::Identity(Identity {
        device_id: heapless::Vec::from_slice(&[1; 8]).unwrap(),
        ..Identity::default()
    });
    let steps = vec![
        config(SSID, PASSWORD),
        bind(),
        config(SSID, PASSWORD),
        Step::Send(app_identity, true),
        Step::Send(Body::WifiResult(WifiResult::default()), true),
        Step::Send(Body::SiteBinding(lot), true),
        Step::Send(Body::SiteBinding(SiteBinding::default()), true),
        identity(),
    ];
    world.app(&code, PAYLOAD_MTU_23, steps);
    assert!(world.run(&[]).is_err());
    assert_eq!(
        world.replies(),
        vec![
            Reply::Error(UNEXPECTED),
            Reply::Error(UNEXPECTED),
            Reply::Error(UNEXPECTED),
            Reply::Error(UNEXPECTED),
            Reply::Error(MALFORMED),
            Reply::Error(MALFORMED),
            Reply::Identity(world.device_id.clone(), 1, FIRMWARE.to_owned()),
        ]
    );
    assert_eq!(world.wifi.scan_count(), 0);
}

#[test]
fn malformed_sealed_messages_are_answered_and_the_session_continues() {
    let mut world = World::new();
    let code = world.pop_code.clone();
    let wrong_version = {
        let mut message = sealed_message(identity_request());
        // Field 1 (protocol_version) is the first varint.
        assert_eq!(message[..2], [0x08, 0x01]);
        message[1] = 0x02;
        message
    };
    let outer_version = {
        let sealed = SealedSetupMessage {
            protocol_version: 2,
            counter: 7,
            ciphertext: heapless::Vec::from_slice(&[0; 40]).unwrap(),
        };
        let mut buffer = vec![0u8; MAX_FRAME];
        let length = encode(&sealed, &mut buffer).unwrap();
        buffer.truncate(length);
        buffer
    };
    let short_key = Body::EnrolmentRequest(EnrolmentRequest {
        server_public_key: heapless::Vec::from_slice(&[9; 31]).unwrap(),
        fingerprint: std::str::from_utf8(&fingerprint(&[9; 32]))
            .unwrap()
            .try_into()
            .unwrap(),
    });
    let mut oversize = Vec::new();
    for _ in 0..70 {
        let mut payload = vec![HEADER_MORE];
        payload.extend([0u8; 19]);
        oversize.push(payload);
    }
    oversize.push(vec![HEADER_LAST, 0]);
    let steps = vec![
        identity(),
        Step::SendRaw(vec![0xFF, 0xFF, 0xFF]),
        Step::SendRaw(wrong_version),
        Step::SendRaw(sealed_message(Body::IdentityRequest(Default::default()))[..0].to_vec()),
        Step::Frame(outer_version),
        Step::Frame(vec![0x1A, 0x05, 0x01]),
        Step::Send(short_key, true),
        Step::Payloads(oversize),
        Step::Payloads(vec![vec![0x07, 1, 2, 3]]),
        identity(),
    ];
    world.app(&code, PAYLOAD_MTU_23, steps);
    assert!(world.run(&[]).is_err());
    let replies = world.replies();
    let identity = Reply::Identity(world.device_id.clone(), 1, FIRMWARE.to_owned());
    assert_eq!(
        replies,
        vec![
            identity.clone(),
            Reply::Error(MALFORMED),
            Reply::Error(MALFORMED),
            Reply::Error(MALFORMED),
            Reply::Error(MALFORMED),
            Reply::Error(MALFORMED),
            Reply::Error(MALFORMED),
            Reply::Error(MALFORMED),
            Reply::Error(MALFORMED),
            identity,
        ]
    );
    assert_eq!(
        world.link.disconnect_count(),
        1,
        "only the final idle timeout"
    );
}

#[test]
fn a_malformed_hello_disconnects() {
    let mut world = World::new();
    let bad_version = {
        let hello = SessionHello {
            protocol_version: 2,
            app_public_key: heapless::Vec::from_slice(&[9; 32]).unwrap(),
        };
        let mut buffer = vec![0u8; 64];
        let length = encode(&hello, &mut buffer).unwrap();
        buffer.truncate(length);
        buffer
    };
    let short_key = {
        let hello = SessionHello {
            protocol_version: 1,
            app_public_key: heapless::Vec::from_slice(&[9; 16]).unwrap(),
        };
        let mut buffer = vec![0u8; 64];
        let length = encode(&hello, &mut buffer).unwrap();
        buffer.truncate(length);
        buffer
    };
    for payloads in [
        vec![vec![HEADER_LAST, 0xFF, 0xFF]],
        common::payloads(&bad_version, PAYLOAD_MTU_23),
        common::payloads(&short_key, PAYLOAD_MTU_23),
        vec![vec![0x07, 0x00]],
    ] {
        world.link.connect(PAYLOAD_MTU_23, payloads);
    }
    let mut radio = MockRadio::new();
    radio.enable().unwrap();
    let mut trng = MockTrng::new(&radio);
    let result = block_on(run_setup(
        &mut world.link,
        &mut world.wifi,
        &mut world.net,
        &mut world.rtc,
        &mut world.timer,
        &mut trng,
        &mut world.flash,
        &world.keys,
        &world.code,
        FIRMWARE,
    ));
    assert_eq!(result.err(), Some(SetupError::Link(LinkError::Transport)));
    assert_eq!(world.link.accept_count(), 4);
    assert_eq!(world.link.disconnect_count(), 4);
    assert!(
        world.link.sent().iter().all(Vec::is_empty),
        "no reply to a bad hello"
    );
    assert!(world.link.timeouts().is_empty());
}

#[test]
fn a_fingerprint_mismatch_seals_nothing() {
    let mut world = World::new();
    let code = world.pop_code.clone();
    let other = fingerprint(&[0x42; 32]);
    let mismatch = enrolment_request(
        &world.server_key,
        Some(std::str::from_utf8(&other).unwrap()),
    );
    world.app(
        &code,
        PAYLOAD_MTU_23,
        vec![
            bind(),
            Step::Send(mismatch.unwrap(), true),
            config(SSID, PASSWORD),
            world.enrol(),
        ],
    );
    // The TRNG yields the session key, then the vector ikm_e: the mismatch must not consume it.
    let trng = world.vector_trng();
    assert!(world.run(&trng).is_err());
    assert_eq!(
        world.replies(),
        vec![
            Reply::Error(FINGERPRINT_MISMATCH),
            Reply::Error(UNEXPECTED),
            world.enrolment()
        ]
    );
}

#[test]
fn tampered_and_replayed_messages_end_the_session_silently() {
    for bad in [Step::Tampered(identity_request()), Step::Replay] {
        let mut world = World::new();
        let code = world.pop_code.clone();
        world.app(&code, PAYLOAD_MTU_23, vec![identity(), bad, identity()]);
        assert!(world.run(&[]).is_err());
        assert_eq!(world.replies().len(), 1, "only the first identity");
        assert_eq!(world.link.disconnect_count(), 1);
        assert!(world.link.timeouts().is_empty(), "closed at once");
    }
}

#[test]
fn an_idle_session_times_out_and_the_hub_advertises_again() {
    let mut world = World::new();
    let code = world.pop_code.clone();
    world.app(&code, PAYLOAD_MTU_23, vec![]);
    world.app(
        &code,
        PAYLOAD_MTU_23,
        vec![bind(), world.enrol(), config(SSID, PASSWORD)],
    );
    assert!(world.run(&[]).is_ok());
    assert_eq!(world.link.accept_count(), 2);
    assert_eq!(
        world.link.timeouts(),
        &[SESSION_IDLE_TIMEOUT_MS, SESSION_IDLE_TIMEOUT_MS]
    );
}

#[test]
fn a_peer_disconnect_ends_the_session() {
    let mut world = World::new();
    world.link.connect_with(
        PAYLOAD_MTU_23,
        vec![
            LinkEvent::Payload(vec![HEADER_MORE, 1]),
            LinkEvent::Disconnect,
        ],
    );
    assert_eq!(
        world.run(&[]).err(),
        Some(SetupError::Link(LinkError::Transport))
    );
    assert_eq!(world.link.accept_count(), 1);
    assert!(world.link.timeouts().is_empty());
}

#[test]
fn an_app_setup_error_is_ignored() {
    let mut world = World::new();
    let code = world.pop_code.clone();
    world.app(
        &code,
        PAYLOAD_MTU_23,
        vec![
            Step::Send(Body::Error(Default::default()), false),
            identity(),
        ],
    );
    assert!(world.run(&[]).is_err());
    assert!(matches!(world.replies().as_slice(), [Reply::Identity(..)]));
}

#[test]
fn a_failed_persist_is_internal_and_stores_nothing() {
    for break_flash in [
        (|flash: &mut MockFlash| flash.ignore_writes(true)) as fn(&mut MockFlash),
        |flash: &mut MockFlash| flash.fail_with(Some(FlashError::Storage)),
    ] {
        let mut world = World::new();
        break_flash(&mut world.flash);
        let code = world.pop_code.clone();
        world.app(
            &code,
            PAYLOAD_MTU_23,
            vec![bind(), world.enrol(), config(SSID, PASSWORD)],
        );
        let result = world.run(&[]);
        assert_eq!(result.err(), Some(SetupError::Link(LinkError::Transport)));
        assert_eq!(world.replies()[1..], [Reply::Error(INTERNAL)]);
        assert_eq!(world.wifi.join_count(), 1);
        world.flash.ignore_writes(false);
        world.flash.fail_with(None);
        assert!(world.provisioning_erased());
    }
}

#[test]
fn nothing_changes_after_connected() {
    let mut world = World::new();
    let code = world.pop_code.clone();
    let steps = vec![
        bind(),
        world.enrol(),
        config(SSID, PASSWORD),
        config("cafe", ""),
        Step::Send(site_binding("other-site", SERVER).unwrap(), true),
        world.enrol(),
        identity(),
    ];
    world.app(&code, PAYLOAD_MTU_23, steps);
    let writes_before = world.flash.write_count();
    let provisioned = world.run(&[]).expect("provisioned");
    assert_eq!(
        world.replies()[1..],
        [
            Reply::WifiResult(CONNECTED),
            Reply::Error(UNEXPECTED),
            Reply::Error(UNEXPECTED),
            Reply::Error(UNEXPECTED),
            Reply::Identity(world.device_id.clone(), 1, FIRMWARE.to_owned()),
        ]
    );
    assert_eq!(world.wifi.join_count(), 1);
    assert_eq!(
        world.flash.write_count(),
        writes_before + 1,
        "one provisioning write"
    );
    assert_eq!(
        (provisioned.record.ssid(), provisioned.record.site_id()),
        (SSID, SITE)
    );
    let ProvisioningState::Provisioned(stored) = load_provisioning(&mut world.flash).unwrap()
    else {
        panic!("CFWP written");
    };
    assert_eq!((stored.ssid(), stored.site_id()), (SSID, SITE));
}

#[test]
fn a_transition_network_is_joined_with_wpa2() {
    let mut world = World::new();
    world.wifi.set_join_outcome("mixed", PASSWORD, Ok(()));
    let code = world.pop_code.clone();
    world.app(
        &code,
        PAYLOAD_MTU_251,
        vec![bind(), world.enrol(), config("mixed", PASSWORD)],
    );
    let provisioned = world.run(&[]).expect("provisioned");
    assert_eq!(world.replies()[1..], [Reply::WifiResult(CONNECTED)]);
    assert_eq!(world.wifi.join_count(), 1);
    let join = &world.wifi.joins()[0];
    assert_eq!((join.bssid, join.channel), ([0x02, 0, 0, 0, 0, 0xF0], 9));
    assert_eq!(provisioned.record.ssid(), "mixed");
}

// ---------------------------------------------------------------------------------------------
// Story 3.5: the Server check after the join

#[test]
fn no_server_leaves_the_network_stores_nothing_and_allows_a_retry() {
    type Script = fn(&mut MockNet);
    let cases: Vec<(&str, Script)> = vec![
        ("no IP", |net| net.push_ip(Err(NetError::NoIp))),
        ("no DNS for SNTP", |net| net.push_sntp(Err(NetError::Dns))),
        ("no SNTP", |net| net.push_sntp(Err(NetError::Sntp))),
        ("no TLS", |net| {
            net.push_sntp(Ok(SNTP_TIME));
            net.push_post(Err(NetError::Tls));
        }),
        ("no DNS for the Server", |net| {
            net.push_sntp(Ok(SNTP_TIME));
            net.push_post(Err(NetError::Dns));
        }),
        ("the 40 s passed", |net| {
            net.push_sntp(Ok(SNTP_TIME));
            net.push_post(Err(NetError::Timeout));
        }),
        ("an unenrolled Hub", |net| {
            net.push_sntp(Ok(SNTP_TIME));
            net.push_post(Ok((
                401,
                br#"{"type":"urn:coldframe:problem:device-unauthorized"}"#,
            )));
        }),
        ("a Server error", |net| {
            net.push_sntp(Ok(SNTP_TIME));
            net.push_post(Ok((503, b"")));
        }),
        ("a 200 without serverTime", |net| {
            net.push_sntp(Ok(SNTP_TIME));
            net.push_post(Ok((200, b"{}")));
        }),
    ];
    for (name, script) in cases {
        let mut world = World::new();
        // Replace the default passing check with this failure, then allow the retry to pass.
        world.net = MockNet::with_link(world.wifi.link());
        script(&mut world.net);
        world.server_accepts();
        let code = world.pop_code.clone();
        world.app(
            &code,
            PAYLOAD_MTU_23,
            vec![
                bind(),
                world.enrol(),
                config(SSID, PASSWORD),
                identity(),
                config(SSID, PASSWORD),
            ],
        );
        let provisioned = world
            .run(&[])
            .unwrap_or_else(|_| panic!("{name}: retry provisions"));
        let replies = world.replies();
        assert_eq!(replies[1], Reply::WifiResult(NO_SERVER), "{name}");
        assert!(
            matches!(replies[2], Reply::Identity(..)),
            "{name}: session open"
        );
        assert_eq!(replies[3], Reply::WifiResult(CONNECTED), "{name}");
        assert_eq!(world.wifi.join_count(), 2, "{name}");
        assert_eq!(world.wifi.leave_count(), 1, "{name}: left after NO_SERVER");
        assert_eq!(world.link.accept_count(), 1, "{name}: one session");
        assert_eq!(provisioned.record.server_url(), SERVER);
    }
}

#[test]
fn no_server_without_a_retry_stores_nothing() {
    let mut world = World::new();
    world.net = MockNet::with_link(world.wifi.link());
    world.net.push_sntp(Ok(SNTP_TIME));
    world.net.push_post(Ok((401, b"")));
    let code = world.pop_code.clone();
    world.app(
        &code,
        PAYLOAD_MTU_251,
        vec![bind(), world.enrol(), config(SSID, PASSWORD)],
    );
    assert_eq!(
        world.run(&[]).err(),
        Some(SetupError::Link(LinkError::Transport))
    );
    assert_eq!(world.replies()[1..], [Reply::WifiResult(NO_SERVER)]);
    assert!(world.provisioning_erased());
    assert!(!world.wifi.is_connected(), "the Hub left the network");
    assert_eq!(world.wifi.leave_count(), 1);
    assert_eq!(
        world.flash.write_count(),
        1,
        "only the setup code, written before"
    );
}

#[test]
fn the_server_check_is_bounded_and_ordered() {
    let mut world = World::new();
    let code = world.pop_code.clone();
    world.app(
        &code,
        PAYLOAD_MTU_23,
        vec![bind(), world.enrol(), config(SSID, PASSWORD)],
    );
    world.run(&[]).expect("provisioned");
    // DHCP within the whole 40 s, SNTP within its own 10 s, nothing sent before the clock.
    assert_eq!(world.net.ip_timeouts(), [40_000]);
    assert_eq!(world.net.sntp_timeouts(), [10_000]);
    assert_eq!(world.net.requests().len(), 1);
    let body: serde_json::Value = serde_json::from_slice(&world.net.requests()[0].body).unwrap();
    assert_eq!(body["protocolVersion"], 1);
    assert!(body["uptimeMs"].is_u64());
}

#[test]
fn a_hub_binding_needs_a_valid_server_url() {
    let without = Body::SiteBinding(SiteBinding {
        site_id: SITE.try_into().unwrap(),
        ..SiteBinding::default()
    });
    let mut steps = vec![Step::Send(without, true)];
    for url in [
        "",
        "http://coldframe.example.org",
        "https://coldframe.example.org/api",
        "https://coldframe.example.org/",
        "https://192.168.1.10",
        "https://[::1]",
        "https://Coldframe.example.org",
        "https://user@coldframe.example.org",
        "https://coldframe.example.org:0",
    ] {
        steps.push(Step::Send(site_binding(SITE, url).unwrap(), true));
    }
    // With no valid binding, a WifiConfig is out of order; the session goes on.
    let mut world = World::new();
    steps.push(world.enrol());
    steps.push(config(SSID, PASSWORD));
    steps.push(bind());
    steps.push(config(SSID, PASSWORD));
    let code = world.pop_code.clone();
    world.app(&code, PAYLOAD_MTU_251, steps);
    let provisioned = world.run(&[]).expect("the valid binding provisions");
    let replies = world.replies();
    assert!(
        replies[..10]
            .iter()
            .all(|reply| *reply == Reply::Error(MALFORMED))
    );
    assert_eq!(
        replies[10..],
        [
            world.enrolment_ignoring_vectors(&replies[10]),
            Reply::Error(UNEXPECTED),
            Reply::WifiResult(CONNECTED),
        ]
    );
    assert_eq!(provisioned.record.server_url(), SERVER);
    // A valid custom port is kept.
    let mut world = World::new();
    let code = world.pop_code.clone();
    let custom = "https://coldframe.example.org:8443";
    world.app(
        &code,
        PAYLOAD_MTU_251,
        vec![
            Step::Send(site_binding(SITE, custom).unwrap(), false),
            world.enrol(),
            config(SSID, PASSWORD),
        ],
    );
    let provisioned = world.run(&[]).expect("provisioned");
    assert_eq!(provisioned.record.server_url(), custom);
    assert_eq!(world.net.requests()[0].port, 8443);
}

#[test]
fn an_oversize_server_url_does_not_decode() {
    // 101 bytes exceed SiteBinding.server_url's capacity of 100: the message is malformed.
    let long = format!("https://{}.example.org", "a".repeat(81));
    assert_eq!(long.len(), 101);
    assert!(site_binding(SITE, &long).is_err());
    let exact = format!("https://{}.{}.example.org", "a".repeat(40), "b".repeat(39));
    assert_eq!(exact.len(), 100);
    assert!(site_binding(SITE, &exact).is_ok());
}
