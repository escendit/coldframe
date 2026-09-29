//! `coldframe.setup.v1` through micropb: every `SetupMessage` body round-trips, the sizes fit
//! the BLE frame, and over-capacity fields fail to decode.

use core::marker::PhantomData;

use coldframe_protocol::setup_v1::SetupMessage_::Body;
use coldframe_protocol::setup_v1::{
    DeviceKind, EnrolmentRequest, EnrolmentResponse, Identity, IdentityRequest, SealedSetupMessage,
    SessionHello, SessionHelloReply, SetupError, SetupErrorCode, SetupMessage, SiteBinding,
    WifiConfig, WifiNetwork, WifiResult, WifiScanList, WifiScanRequest, WifiSecurity, WifiStatus,
};
use coldframe_protocol::{
    CodecError, PROTOCOL_VERSION, SEALED_CIPHERTEXT_CAPACITY, SEALED_SETUP_MESSAGE_MAX_SIZE,
    SESSION_HELLO_MAX_SIZE, SETUP_MESSAGE_MAX_SIZE, decode, encode,
};
use coldframe_setup::MAX_FRAME;

fn vec<const N: usize>(bytes: &[u8]) -> heapless::Vec<u8, N> {
    heapless::Vec::from_slice(bytes).expect("fits")
}

fn string<const N: usize>(text: &str) -> heapless::String<N> {
    text.try_into().expect("fits")
}

fn full_scan_list() -> WifiScanList {
    let mut list = WifiScanList::default();
    for index in 0..16u8 {
        let ssid = format!("{:0>32}", index);
        list.networks
            .push(WifiNetwork {
                ssid: string(&ssid),
                bssid: vec(&[index; 6]),
                rssi: -100 + i32::from(index),
                security: WifiSecurity::Wpa3Only,
                channel: 13,
            })
            .expect("16 fit");
    }
    list
}

fn bodies() -> Vec<Body> {
    let mut binding = SiteBinding {
        site_id: string("0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b"),
        ..SiteBinding::default()
    };
    let hub_binding = Body::SiteBinding(binding.clone());
    binding.set_lot_id(string("0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5c"));
    vec![
        Body::IdentityRequest(IdentityRequest {}),
        Body::Identity(Identity {
            device_id: vec(&[1, 2, 3, 4, 5, 6, 7, 8]),
            kind: DeviceKind::Hub,
            firmware_version: string("0.0.0"),
        }),
        Body::WifiScanRequest(WifiScanRequest {}),
        Body::WifiScanList(full_scan_list()),
        Body::WifiConfig(WifiConfig {
            ssid: string("garden"),
            password: string("correct horse battery staple"),
        }),
        Body::WifiResult(WifiResult {
            status: WifiStatus::WrongPassword,
        }),
        hub_binding,
        Body::SiteBinding(binding),
        Body::EnrolmentRequest(EnrolmentRequest {
            server_public_key: vec(&[7; 32]),
            fingerprint: string(&"a".repeat(64)),
        }),
        Body::EnrolmentResponse(EnrolmentResponse {
            device_id: vec(&[9; 8]),
            enc: vec(&[3; 32]),
            ciphertext: vec(&[4; 48]),
        }),
        Body::Error(SetupError {
            code: SetupErrorCode::FingerprintMismatch,
        }),
    ]
}

#[test]
fn every_setup_message_body_round_trips() {
    let bodies = bodies();
    assert_eq!(bodies.len(), 11, "every oneof member, plus a Node binding");
    for body in bodies {
        let message = SetupMessage {
            protocol_version: PROTOCOL_VERSION,
            body: Some(body),
        };
        let mut buffer = [0u8; SETUP_MESSAGE_MAX_SIZE];
        let length = encode(&message, &mut buffer).expect("fits MAX_SIZE");
        let decoded: SetupMessage = decode(&buffer[..length]).expect("decodes");
        assert!(decoded == message, "round trip differs");
    }
}

#[test]
fn the_session_frames_round_trip() {
    let hello = SessionHello {
        protocol_version: 1,
        app_public_key: vec(&[5; 32]),
    };
    let mut buffer = [0u8; MAX_FRAME];
    let length = encode(&hello, &mut buffer).unwrap();
    assert_eq!(decode::<SessionHello>(&buffer[..length]).unwrap(), hello);

    let reply = SessionHelloReply {
        protocol_version: 1,
        device_public_key: vec(&[6; 32]),
    };
    let length = encode(&reply, &mut buffer).unwrap();
    assert_eq!(
        decode::<SessionHelloReply>(&buffer[..length]).unwrap(),
        reply
    );

    // The largest encoding: every varint at its widest, the ciphertext at capacity.
    let sealed = SealedSetupMessage {
        protocol_version: u32::MAX,
        counter: u64::MAX,
        ciphertext: vec(&[0xAB; SEALED_CIPHERTEXT_CAPACITY]),
    };
    let length = encode(&sealed, &mut buffer).unwrap();
    assert_eq!(length, SEALED_SETUP_MESSAGE_MAX_SIZE);
    assert_eq!(
        decode::<SealedSetupMessage>(&buffer[..length]).unwrap(),
        sealed
    );
}

#[test]
fn the_largest_messages_fit_a_frame() {
    const { assert!(SEALED_SETUP_MESSAGE_MAX_SIZE <= MAX_FRAME) };
    const { assert!(SESSION_HELLO_MAX_SIZE <= MAX_FRAME) };
    assert_eq!(SEALED_CIPHERTEXT_CAPACITY, SETUP_MESSAGE_MAX_SIZE + 16);

    // A full scan list is the largest SetupMessage and still seals into one frame.
    let message = SetupMessage {
        protocol_version: PROTOCOL_VERSION,
        body: Some(Body::WifiScanList(full_scan_list())),
    };
    let mut buffer = [0u8; SETUP_MESSAGE_MAX_SIZE];
    let length = encode(&message, &mut buffer).unwrap();
    assert!(length <= SETUP_MESSAGE_MAX_SIZE);
}

/// A length-delimited field `number` holding `length` bytes of `fill`.
fn field(number: u8, length: usize, fill: u8) -> Vec<u8> {
    let mut bytes = vec![number << 3 | 2];
    let mut rest = length;
    loop {
        let byte = u8::try_from(rest & 0x7F).unwrap();
        rest >>= 7;
        if rest == 0 {
            bytes.push(byte);
            break;
        }
        bytes.push(byte | 0x80);
    }
    bytes.extend(std::iter::repeat_n(fill, length));
    bytes
}

#[test]
fn over_capacity_fields_fail_to_decode() {
    // WifiConfig.ssid is 32 bytes at most, password 64, SiteBinding.site_id 36.
    assert!(decode::<WifiConfig>(&field(1, 32, b'a')).is_ok());
    assert_eq!(
        decode::<WifiConfig>(&field(1, 33, b'a')).err(),
        Some(CodecError::Malformed)
    );
    assert!(decode::<WifiConfig>(&field(2, 64, b'p')).is_ok());
    assert!(decode::<WifiConfig>(&field(2, 65, b'p')).is_err());
    assert!(decode::<SiteBinding>(&field(1, 36, b's')).is_ok());
    assert!(decode::<SiteBinding>(&field(1, 37, b's')).is_err());
    assert!(decode::<EnrolmentRequest>(&field(1, 33, 1)).is_err());
    assert!(decode::<EnrolmentRequest>(&field(2, 65, b'f')).is_err());
    assert!(decode::<SealedSetupMessage>(&field(3, SEALED_CIPHERTEXT_CAPACITY + 1, 0)).is_err());
    // Invalid UTF-8 in a string field.
    assert!(decode::<WifiConfig>(&field(1, 4, 0xFF)).is_err());
    // Truncated.
    assert!(decode::<WifiConfig>(&field(1, 8, b'a')[..5]).is_err());
}

#[test]
fn encode_reports_a_small_buffer() {
    let hello = SessionHello {
        protocol_version: 1,
        app_public_key: vec(&[5; 32]),
    };
    let mut small = [0u8; 10];
    assert_eq!(encode(&hello, &mut small), Err(CodecError::BufferTooSmall));
}

/// Whether `T` implements `Debug`, by inherent-const-over-trait-const resolution.
trait NotDebug {
    const IS_DEBUG: bool = false;
}
impl<T> NotDebug for Probe<T> {}
struct Probe<T>(PhantomData<T>);
impl<T: core::fmt::Debug> Probe<T> {
    const IS_DEBUG: bool = true;
}

#[test]
fn password_holders_have_no_debug() {
    const { assert!(!Probe::<WifiConfig>::IS_DEBUG) };
    const { assert!(!Probe::<SetupMessage>::IS_DEBUG) };
    const { assert!(!Probe::<Body>::IS_DEBUG) };
    // Messages without secrets keep their Debug.
    const { assert!(Probe::<SessionHello>::IS_DEBUG) };
    const { assert!(Probe::<WifiScanList>::IS_DEBUG) };
}
