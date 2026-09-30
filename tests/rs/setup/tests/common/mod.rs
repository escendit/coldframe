//! Shared helpers: the vectors, a blocking executor, and a scripted app peer for `MockSetupLink`.

#![allow(dead_code, reason = "each test file uses a different part")]

use std::cell::RefCell;
use std::collections::VecDeque;
use std::future::Future;
use std::pin::pin;
use std::rc::Rc;
use std::task::{Context, Poll, Waker};

use coldframe_hal::mock::MockSetupLink;
use coldframe_protocol::setup_v1::SetupMessage_::Body;
use coldframe_setup::MAX_FRAME;
use coldframe_setup::app::{AppClient, AppError};
use coldframe_setup::framing::{Reassembler, fragments};
use serde_json::Value;

/// The parsed `packages/crypto-spec/vectors.json`.
pub fn vectors() -> Value {
    let path = concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../../packages/crypto-spec/vectors.json"
    );
    let text = std::fs::read_to_string(path).expect("vectors.json is readable");
    serde_json::from_str(&text).expect("vectors.json is JSON")
}

/// A string field.
pub fn text<'a>(value: &'a Value, field: &str) -> &'a str {
    value[field]
        .as_str()
        .unwrap_or_else(|| panic!("{field} is a string"))
}

/// A lowercase hex field as bytes.
pub fn bytes(value: &Value, field: &str) -> Vec<u8> {
    let text = text(value, field);
    (0..text.len())
        .step_by(2)
        .map(|i| u8::from_str_radix(&text[i..i + 2], 16).expect("hex digits"))
        .collect()
}

/// A lowercase hex field as a fixed-size array.
pub fn array<const N: usize>(value: &Value, field: &str) -> [u8; N] {
    bytes(value, field)
        .try_into()
        .unwrap_or_else(|_| panic!("{field} has {N} bytes"))
}

/// Runs a future that never waits (every mock completes at once) with a no-op waker.
pub fn block_on<F: Future>(future: F) -> F::Output {
    let mut future = pin!(future);
    let mut context = Context::from_waker(Waker::noop());
    loop {
        if let Poll::Ready(output) = future.as_mut().poll(&mut context) {
            return output;
        }
    }
}

/// ATT payload sizes: the default MTU of 23 and the 251 a client can ask for, minus 3.
pub const PAYLOAD_MTU_23: usize = 20;
/// See [`PAYLOAD_MTU_23`].
pub const PAYLOAD_MTU_251: usize = 248;

/// Splits a frame into BLE payloads.
pub fn payloads(frame: &[u8], max_payload: usize) -> Vec<Vec<u8>> {
    fragments(frame, max_payload)
        .map(|fragment| {
            let mut buffer = vec![0u8; max_payload];
            let length = fragment.write_to(&mut buffer);
            buffer.truncate(length);
            buffer
        })
        .collect()
}

/// One thing the scripted app does after it hears from the Device.
pub enum Step {
    /// Seal and send a message; `true` waits for a reply before the next step.
    Send(Body, bool),
    /// Seal arbitrary plaintext and send it; waits for a reply.
    SendRaw(Vec<u8>),
    /// Send a frame as given (unsealed); waits for a reply.
    Frame(Vec<u8>),
    /// Send these payloads as given; waits for a reply.
    Payloads(Vec<Vec<u8>>),
    /// Seal a message, corrupt its tag, send it; the Device should say nothing.
    Tampered(Body),
    /// Send the previous sealed frame again; the Device should say nothing.
    Replay,
}

/// A reply as the app saw it, comparable in tests.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Reply {
    /// `Identity`: Device ID, kind, firmware version.
    Identity(Vec<u8>, i32, String),
    /// `WifiScanList`: (ssid, bssid, rssi, security, channel) per network.
    ScanList(Vec<(String, Vec<u8>, i32, i32, u32)>),
    /// `WifiResult` status.
    WifiResult(i32),
    /// `EnrolmentResponse`: Device ID, enc, ciphertext.
    Enrolment(Vec<u8>, Vec<u8>, Vec<u8>),
    /// `SetupError` code.
    Error(i32),
    /// Any other body.
    Other,
    /// The frame did not open.
    Failed(AppError),
}

/// What the scripted app observed.
#[derive(Default)]
pub struct Log {
    /// The Device's key from `SessionHelloReply`, per connection.
    pub device_keys: Vec<[u8; 32]>,
    /// Every sealed reply, in order.
    pub replies: Vec<Reply>,
}

/// A scripted app on one connection.
pub struct AppPeer {
    client: AppClient,
    code: String,
    max_payload: usize,
    reassembler: Reassembler,
    hello_done: bool,
    steps: VecDeque<Step>,
    last_sealed: Vec<u8>,
    log: Rc<RefCell<Log>>,
}

fn reply_of(message: &coldframe_protocol::setup_v1::SetupMessage) -> Reply {
    match &message.body {
        Some(Body::Identity(identity)) => Reply::Identity(
            identity.device_id.to_vec(),
            identity.kind.0,
            identity.firmware_version.to_string(),
        ),
        Some(Body::WifiScanList(list)) => Reply::ScanList(
            list.networks
                .iter()
                .map(|n| {
                    (
                        n.ssid.to_string(),
                        n.bssid.to_vec(),
                        n.rssi,
                        n.security.0,
                        n.channel,
                    )
                })
                .collect(),
        ),
        Some(Body::WifiResult(result)) => Reply::WifiResult(result.status.0),
        Some(Body::EnrolmentResponse(response)) => Reply::Enrolment(
            response.device_id.to_vec(),
            response.enc.to_vec(),
            response.ciphertext.to_vec(),
        ),
        Some(Body::Error(error)) => Reply::Error(error.code.0),
        _ => Reply::Other,
    }
}

impl AppPeer {
    /// An app with `private_key` that types `code` and plays `steps` after the key exchange.
    pub fn new(
        private_key: [u8; 32],
        code: &str,
        max_payload: usize,
        steps: Vec<Step>,
        log: Rc<RefCell<Log>>,
    ) -> Self {
        Self {
            client: AppClient::new(private_key),
            code: code.to_owned(),
            max_payload,
            reassembler: Reassembler::new(),
            hello_done: false,
            steps: steps.into(),
            last_sealed: Vec::new(),
            log,
        }
    }

    /// The payloads of its `SessionHello`.
    pub fn hello(&self) -> Vec<Vec<u8>> {
        let mut frame = [0u8; MAX_FRAME];
        let length = self.client.hello(&mut frame).unwrap();
        payloads(&frame[..length], self.max_payload)
    }

    /// Queues this app as the next connection of `link`; it answers the Device on that
    /// connection.
    pub fn connect(mut self, link: &mut MockSetupLink) {
        let hello = self.hello();
        link.connect_with_peer(self.max_payload, hello, move |payload| {
            self.on_payload(payload)
        });
    }

    /// Like [`AppPeer::connect`], but runs `hook` before the app handles each payload the Device
    /// sends: a test moves its clock there.
    pub fn connect_with_hook(mut self, link: &mut MockSetupLink, mut hook: impl FnMut() + 'static) {
        let hello = self.hello();
        link.connect_with_peer(self.max_payload, hello, move |payload| {
            hook();
            self.on_payload(payload)
        });
    }

    fn on_payload(&mut self, payload: &[u8]) -> Vec<Vec<u8>> {
        let frame = match self.reassembler.push(payload) {
            Ok(Some(frame)) => frame.to_vec(),
            Ok(None) => return Vec::new(),
            Err(error) => panic!("the Device sent a bad payload: {error}"),
        };
        if !self.hello_done {
            self.hello_done = true;
            let key = self
                .client
                .on_hello_reply(&frame, &self.code)
                .expect("a valid SessionHelloReply");
            self.log.borrow_mut().device_keys.push(key);
        } else {
            let reply = match self.client.open(&frame) {
                Ok(message) => reply_of(&message),
                Err(error) => Reply::Failed(error),
            };
            self.log.borrow_mut().replies.push(reply);
        }
        self.next_steps()
    }

    fn next_steps(&mut self) -> Vec<Vec<u8>> {
        let mut out = Vec::new();
        while let Some(step) = self.steps.pop_front() {
            let mut frame = vec![0u8; MAX_FRAME];
            let waits = match step {
                Step::Send(body, waits) => {
                    let length = self.client.seal(body, &mut frame).unwrap();
                    frame.truncate(length);
                    self.last_sealed.clone_from(&frame);
                    out.extend(payloads(&frame, self.max_payload));
                    waits
                }
                Step::SendRaw(plain) => {
                    let length = self.client.seal_raw(&plain, &mut frame).unwrap();
                    frame.truncate(length);
                    self.last_sealed.clone_from(&frame);
                    out.extend(payloads(&frame, self.max_payload));
                    true
                }
                Step::Frame(raw) => {
                    out.extend(payloads(&raw, self.max_payload));
                    true
                }
                Step::Payloads(raw) => {
                    out.extend(raw);
                    true
                }
                Step::Tampered(body) => {
                    let length = self.client.seal(body, &mut frame).unwrap();
                    frame.truncate(length);
                    // The ciphertext (with its tag) is the last field of the frame.
                    *frame.last_mut().unwrap() ^= 0x01;
                    out.extend(payloads(&frame, self.max_payload));
                    true
                }
                Step::Replay => {
                    out.extend(payloads(&self.last_sealed, self.max_payload));
                    true
                }
            };
            if waits {
                break;
            }
        }
        out
    }
}
