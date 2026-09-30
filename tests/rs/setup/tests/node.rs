//! The Node profile of the setup session and the setup window (Story 4.2): every firmware row of
//! the I/O matrix through `run_node_setup`, with the coldframe-hal mocks and the crypto-spec
//! vectors.

mod common;

use std::cell::RefCell;
use std::rc::Rc;

use coldframe_crypto::DeviceKeys;
use coldframe_hal::mock::{MockRadio, MockRtc, MockSetupLink, MockTrng};
use coldframe_hal::{LinkError, Radio, Rtc, RtcError, SetupLink};
use coldframe_protocol::setup_v1::SetupMessage_::Body;
use coldframe_protocol::setup_v1::SiteBinding;
use coldframe_setup::app::{
    AppError, enrolment_request, identity_request, node_binding, site_binding, wifi_config,
    wifi_scan_request,
};
use coldframe_setup::code::SetupCode;
use coldframe_setup::node::{
    NODE_SESSION_IDLE_TIMEOUT_MS, NodeSetupEnd, SETUP_WINDOW_MS, run_node_setup,
};
use common::{AppPeer, Log, PAYLOAD_MTU_23, PAYLOAD_MTU_251, Reply, Step, array, block_on, text};

const FIRMWARE: &str = "0.4.2";
const SITE: &str = "0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b";
const LOT: &str = "0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a6c";
/// A test Server; never a real one.
const SERVER: &str = "https://coldframe.example.org";

// SetupErrorCode values.
const MALFORMED: i32 = 1;
const UNEXPECTED: i32 = 2;
const FINGERPRINT_MISMATCH: i32 = 3;
// DeviceKind values.
const NODE: i32 = 2;

/// A clock shared by the Device and the scripted app, which moves it.
#[derive(Clone, Default)]
struct Clock(Rc<RefCell<MockRtc>>);

impl Clock {
    fn advance(&self, millis: u64) {
        self.0.borrow_mut().advance(millis);
    }
}

impl Rtc for Clock {
    fn uptime_millis(&self) -> u64 {
        self.0.borrow().uptime_millis()
    }

    fn unix_time_millis(&self) -> Option<u64> {
        self.0.borrow().unix_time_millis()
    }

    fn set_unix_time_millis(&mut self, unix_millis: u64) -> Result<(), RtcError> {
        self.0.borrow_mut().set_unix_time_millis(unix_millis)
    }
}

/// A link on which time passes while it waits: a bounded accept or receive that times out moves
/// the clock by its timeout, as the real one would.
struct TimedLink {
    inner: MockSetupLink,
    clock: Clock,
}

impl SetupLink for TimedLink {
    async fn accept(&mut self) -> Result<(), LinkError> {
        self.inner.accept().await
    }

    async fn accept_within(&mut self, timeout_ms: u32) -> Result<(), LinkError> {
        let result = self.inner.accept_within(timeout_ms).await;
        if result == Err(LinkError::Timeout) {
            self.clock.advance(u64::from(timeout_ms));
        }
        result
    }

    async fn receive(&mut self, buffer: &mut [u8], timeout_ms: u32) -> Result<usize, LinkError> {
        let result = self.inner.receive(buffer, timeout_ms).await;
        if result == Err(LinkError::Timeout) {
            self.clock.advance(u64::from(timeout_ms));
        }
        result
    }

    async fn send(&mut self, payload: &[u8]) -> Result<(), LinkError> {
        self.inner.send(payload).await
    }

    fn max_payload(&self) -> usize {
        self.inner.max_payload()
    }

    async fn disconnect(&mut self) {
        self.inner.disconnect().await;
    }
}

/// The fixture world of one test: the vectors, the Node and its mocks.
struct World {
    keys: DeviceKeys,
    code: SetupCode,
    app_private: [u8; 32],
    device_private: [u8; 32],
    device_public: [u8; 32],
    ikm_e: [u8; 32],
    server_key: [u8; 32],
    device_id: Vec<u8>,
    enc: Vec<u8>,
    ciphertext: Vec<u8>,
    pop_code: String,
    wrong_code: String,
    link: MockSetupLink,
    clock: Clock,
    log: Rc<RefCell<Log>>,
}

impl World {
    fn new() -> Self {
        let all = common::vectors();
        let setup = &all["setup"][0];
        let enrolment = &all["enrolment"][0];
        Self {
            keys: DeviceKeys::from_root_key(&array(enrolment, "rootKey")),
            code: SetupCode::parse(text(setup, "normalizedPopCode").as_bytes()).unwrap(),
            app_private: array(setup, "appPrivateKey"),
            device_private: array(setup, "hubPrivateKey"),
            device_public: array(setup, "hubPublicKey"),
            ikm_e: array(enrolment, "ikmE"),
            server_key: array(enrolment, "recipientPublicKey"),
            device_id: common::bytes(enrolment, "deviceId"),
            enc: common::bytes(enrolment, "enc"),
            ciphertext: common::bytes(enrolment, "ciphertext"),
            pop_code: text(setup, "popCode").to_owned(),
            wrong_code: text(setup, "wrongPopCode").to_owned(),
            link: MockSetupLink::new(),
            clock: Clock::default(),
            log: Rc::new(RefCell::new(Log::default())),
        }
    }

    fn peer(&self, code: &str, max_payload: usize, steps: Vec<Step>) -> AppPeer {
        AppPeer::new(
            self.app_private,
            code,
            max_payload,
            steps,
            Rc::clone(&self.log),
        )
    }

    /// Queues a connection of an app that types `code` and plays `steps`.
    fn app(&mut self, code: &str, max_payload: usize, steps: Vec<Step>) {
        self.peer(code, max_payload, steps).connect(&mut self.link);
    }

    /// Queues an app whose every Device send first moves the clock by `step_ms`.
    fn slow_app(&mut self, code: &str, steps: Vec<Step>, step_ms: u64) {
        let clock = self.clock.clone();
        self.peer(code, PAYLOAD_MTU_251, steps)
            .connect_with_hook(&mut self.link, move || clock.advance(step_ms));
    }

    /// Runs the setup window on a link where waiting takes no time.
    fn run(
        &mut self,
        trng: &[u8],
        window_ms: u32,
    ) -> Result<NodeSetupEnd, coldframe_setup::SetupError> {
        let mut radio = MockRadio::new();
        radio.enable().unwrap();
        let mut trng = MockTrng::with_bytes(&radio, trng);
        let clock = self.clock.clone();
        block_on(run_node_setup(
            &mut self.link,
            &clock,
            &mut trng,
            &self.keys,
            &self.code,
            FIRMWARE,
            window_ms,
        ))
    }

    /// Runs the setup window on a link where a wait that times out takes its whole timeout.
    fn run_timed(&mut self, window_ms: u32) -> Result<NodeSetupEnd, coldframe_setup::SetupError> {
        let mut radio = MockRadio::new();
        radio.enable().unwrap();
        let mut trng = MockTrng::with_bytes(&radio, &[]);
        let clock = self.clock.clone();
        let mut link = TimedLink {
            inner: std::mem::take(&mut self.link),
            clock: clock.clone(),
        };
        let result = block_on(run_node_setup(
            &mut link, &clock, &mut trng, &self.keys, &self.code, FIRMWARE, window_ms,
        ));
        self.link = link.inner;
        result
    }

    /// The TRNG bytes of the vectors: the session key, then the enrolment `ikm_e`.
    fn vector_trng(&self) -> Vec<u8> {
        [self.device_private.as_slice(), self.ikm_e.as_slice()].concat()
    }

    fn replies(&self) -> Vec<Reply> {
        std::mem::take(&mut self.log.borrow_mut().replies)
    }

    fn enrolment(&self) -> Reply {
        Reply::Enrolment(
            self.device_id.clone(),
            self.enc.clone(),
            self.ciphertext.clone(),
        )
    }

    fn enrol(&self) -> Step {
        Step::Send(enrolment_request(&self.server_key, None).unwrap(), true)
    }
}

fn identity() -> Step {
    Step::Send(identity_request(), true)
}

fn bind_lot() -> Step {
    Step::Send(node_binding(SITE, LOT).unwrap(), false)
}

fn binding(site_id: &str, lot_id: Option<&str>, server_url: Option<&str>) -> Step {
    let mut binding = SiteBinding {
        site_id: site_id.try_into().unwrap(),
        ..SiteBinding::default()
    };
    if let Some(lot) = lot_id {
        binding.set_lot_id(lot.try_into().unwrap());
    }
    if let Some(url) = server_url {
        binding.set_server_url(url.try_into().unwrap());
    }
    Step::Send(Body::SiteBinding(binding), false)
}

#[test]
fn the_window_is_three_minutes() {
    assert_eq!(SETUP_WINDOW_MS, 180_000);
}

#[test]
fn nobody_connects_and_the_window_closes() {
    let mut world = World::new();
    // The wake started a while ago: the window runs from now.
    world.clock.advance(4_321);
    assert_eq!(
        world.run(&[], SETUP_WINDOW_MS),
        Ok(NodeSetupEnd::WindowClosed)
    );
    assert_eq!(world.link.accept_timeouts(), &[SETUP_WINDOW_MS]);
    assert_eq!(world.link.accept_count(), 0);
}

#[test]
fn a_transport_failure_while_advertising_ends_setup_without_accepting_again() {
    let mut world = World::new();
    world.link.fail_accept_within(LinkError::Transport);
    // A connection is waiting: a loop that went on would accept it.
    world.app(&world.pop_code.clone(), PAYLOAD_MTU_251, vec![identity()]);
    assert_eq!(
        world.run(&[], SETUP_WINDOW_MS),
        Err(coldframe_setup::SetupError::Link(LinkError::Transport))
    );
    assert_eq!(world.link.accept_count(), 0);
    assert!(world.link.accept_timeouts().is_empty());
}

#[test]
fn a_node_connection_idles_out_well_inside_the_window() {
    assert_eq!(NODE_SESSION_IDLE_TIMEOUT_MS, 60_000);
    // The Hub's idle bound would outlast the whole window; the Node's leaves room to retry.
    const { assert!(NODE_SESSION_IDLE_TIMEOUT_MS < SETUP_WINDOW_MS) };
    const { assert!(coldframe_setup::SESSION_IDLE_TIMEOUT_MS > SETUP_WINDOW_MS) };
}

#[test]
fn advertising_waits_for_the_whole_rest_of_the_window() {
    let mut world = World::new();
    // Not cut into idle-timeout steps: the accept is bounded by the deadline alone.
    assert_eq!(
        world.run_timed(SETUP_WINDOW_MS),
        Ok(NodeSetupEnd::WindowClosed)
    );
    assert_eq!(world.link.accept_timeouts(), &[SETUP_WINDOW_MS]);
}

#[test]
fn enrolment_matches_the_vectors_and_reports_a_node() {
    for max_payload in [PAYLOAD_MTU_23, PAYLOAD_MTU_251] {
        let mut world = World::new();
        let code = world.pop_code.clone();
        let steps = vec![identity(), bind_lot(), world.enrol()];
        world.app(&code, max_payload, steps);
        let trng = world.vector_trng();
        let end = world.run(&trng, SETUP_WINDOW_MS);

        assert_eq!(end, Ok(NodeSetupEnd::Enrolled));
        assert_eq!(world.log.borrow().device_keys, vec![world.device_public]);
        assert_eq!(
            world.replies(),
            vec![
                Reply::Identity(world.device_id.clone(), NODE, FIRMWARE.to_owned()),
                world.enrolment(),
            ]
        );
        // The enrolled session's connection ended (the app went quiet), then the window ended.
        assert_eq!(world.link.accept_count(), 1);
        assert_eq!(world.link.disconnect_count(), 1);
        assert!(!world.link.is_connected());
        assert!(world.link.accept_timeouts().is_empty());
        assert!(world.link.sent()[0].iter().all(|p| p.len() <= max_payload));
    }
}

#[test]
fn an_enrolled_session_is_served_until_its_connection_ends() {
    let mut world = World::new();
    let code = world.pop_code.clone();
    let steps = vec![bind_lot(), world.enrol(), identity()];
    world.app(&code, PAYLOAD_MTU_251, steps);
    let trng = world.vector_trng();
    assert_eq!(
        world.run(&trng, SETUP_WINDOW_MS),
        Ok(NodeSetupEnd::Enrolled)
    );
    let replies = world.replies();
    assert_eq!(replies.len(), 2);
    assert_eq!(replies[0], world.enrolment());
    assert!(matches!(replies[1], Reply::Identity(_, NODE, _)));
    // The idle receive that saw the app leave was bounded by the Node's idle timeout, well
    // inside the window.
    assert_eq!(world.link.timeouts(), &[NODE_SESSION_IDLE_TIMEOUT_MS]);
}

#[test]
fn a_wrong_code_gets_one_sealed_error_then_a_disconnect_and_the_window_continues() {
    let mut world = World::new();
    let wrong = world.wrong_code.clone();
    world.slow_app(&wrong, vec![identity(), identity()], 30_000);
    assert_eq!(
        world.run(&[], SETUP_WINDOW_MS),
        Ok(NodeSetupEnd::WindowClosed)
    );

    assert_eq!(
        world.replies(),
        vec![Reply::Failed(AppError::WrongSetupCode)]
    );
    assert_eq!(world.link.disconnect_count(), 1);
    assert!(
        world.link.timeouts().is_empty(),
        "closed at once, not idled out"
    );
    // Advertised again for the rest of the window: two Device sends moved the clock 60 s.
    assert_eq!(world.link.accept_timeouts(), &[SETUP_WINDOW_MS - 60_000]);
}

#[test]
fn a_wrong_code_then_the_right_one_enrols() {
    let mut world = World::new();
    let (wrong, right) = (world.wrong_code.clone(), world.pop_code.clone());
    world.app(&wrong, PAYLOAD_MTU_23, vec![identity()]);
    let steps = vec![bind_lot(), world.enrol()];
    world.app(&right, PAYLOAD_MTU_251, steps);
    assert_eq!(world.run(&[], SETUP_WINDOW_MS), Ok(NodeSetupEnd::Enrolled));
    let replies = world.replies();
    assert_eq!(replies[0], Reply::Failed(AppError::WrongSetupCode));
    assert!(matches!(replies[1], Reply::Enrolment(ref id, ..) if *id == world.device_id));
    assert_eq!(world.link.accept_count(), 2);
    assert_eq!(world.link.disconnect_count(), 2);
}

#[test]
fn a_fingerprint_mismatch_seals_nothing_and_does_not_enrol() {
    let mut world = World::new();
    let code = world.pop_code.clone();
    let wrong_fingerprint = "00".repeat(32);
    let steps = vec![
        bind_lot(),
        Step::Send(
            enrolment_request(&world.server_key, Some(&wrong_fingerprint)).unwrap(),
            true,
        ),
        identity(),
    ];
    world.app(&code, PAYLOAD_MTU_251, steps);
    let trng = world.vector_trng();
    assert_eq!(
        world.run(&trng, SETUP_WINDOW_MS),
        Ok(NodeSetupEnd::WindowClosed)
    );
    let replies = world.replies();
    assert_eq!(replies[0], Reply::Error(FINGERPRINT_MISMATCH));
    // The session stayed open.
    assert!(matches!(replies[1], Reply::Identity(_, NODE, _)));
    assert!(!replies.iter().any(|r| matches!(r, Reply::Enrolment(..))));
}

#[test]
fn a_hub_shaped_or_incomplete_binding_is_malformed_and_the_session_stays_open() {
    let cases = [
        // A Hub binding: a Server, no Lot.
        Step::Send(site_binding(SITE, SERVER).unwrap(), true),
        // No Lot.
        binding(SITE, None, None),
        // An empty Lot.
        binding(SITE, Some(""), None),
        // A Lot, but also a Server.
        binding(SITE, Some(LOT), Some(SERVER)),
        // An empty Site.
        binding("", Some(LOT), None),
    ];
    for case in cases {
        let case = match case {
            Step::Send(body, _) => Step::Send(body, true),
            other => other,
        };
        let mut world = World::new();
        let code = world.pop_code.clone();
        let steps = vec![case, identity(), bind_lot(), world.enrol()];
        world.app(&code, PAYLOAD_MTU_251, steps);
        let trng = world.vector_trng();
        assert_eq!(
            world.run(&trng, SETUP_WINDOW_MS),
            Ok(NodeSetupEnd::Enrolled)
        );
        let replies = world.replies();
        assert_eq!(replies[0], Reply::Error(MALFORMED));
        assert!(matches!(replies[1], Reply::Identity(_, NODE, _)));
        assert_eq!(replies[2], world.enrolment());
        assert_eq!(replies.len(), 3, "a valid Node binding has no reply");
    }
}

#[test]
fn wifi_messages_are_unexpected_on_a_node() {
    let mut world = World::new();
    let code = world.pop_code.clone();
    let steps = vec![
        Step::Send(wifi_scan_request(), true),
        bind_lot(),
        world.enrol(),
        Step::Send(wifi_config("garden", "fixture-passphrase").unwrap(), true),
    ];
    world.app(&code, PAYLOAD_MTU_251, steps);
    let trng = world.vector_trng();
    assert_eq!(
        world.run(&trng, SETUP_WINDOW_MS),
        Ok(NodeSetupEnd::Enrolled)
    );
    let replies = world.replies();
    assert_eq!(replies[0], Reply::Error(UNEXPECTED));
    assert_eq!(replies[1], world.enrolment());
    assert_eq!(replies[2], Reply::Error(UNEXPECTED));
}

#[test]
fn a_session_that_outlives_the_window_times_out_at_the_deadline() {
    let mut world = World::new();
    let code = world.pop_code.clone();
    // Each Device send takes 60 s: the hello reply and the Identity leave 60 s of the window.
    world.slow_app(&code, vec![identity()], 60_000);
    assert_eq!(
        world.run(&[], SETUP_WINDOW_MS),
        Ok(NodeSetupEnd::WindowClosed)
    );
    assert!(matches!(world.replies()[..], [Reply::Identity(_, NODE, _)]));
    assert_eq!(world.link.timeouts(), &[SETUP_WINDOW_MS - 120_000]);
    assert_eq!(world.link.disconnect_count(), 1);
    // The window was over: no further advertising.
    assert!(world.link.accept_timeouts().is_empty());
    assert_eq!(world.link.accept_count(), 1);
}

#[test]
fn an_idle_session_is_dropped_and_the_window_continues() {
    let mut world = World::new();
    let code = world.pop_code.clone();
    // The app says hello, asks for the Identity, then goes silent.
    world.app(&code, PAYLOAD_MTU_251, vec![identity()]);
    assert_eq!(
        world.run_timed(SETUP_WINDOW_MS),
        Ok(NodeSetupEnd::WindowClosed)
    );
    assert!(matches!(world.replies()[..], [Reply::Identity(_, NODE, _)]));
    // Dropped after the Node's idle timeout, not the Hub's 300 s …
    assert_eq!(world.link.timeouts(), &[NODE_SESSION_IDLE_TIMEOUT_MS]);
    assert_eq!(world.link.disconnect_count(), 1);
    // … and the window advertised again for the rest of its three minutes.
    assert_eq!(
        world.link.accept_timeouts(),
        &[SETUP_WINDOW_MS - NODE_SESSION_IDLE_TIMEOUT_MS]
    );
}
