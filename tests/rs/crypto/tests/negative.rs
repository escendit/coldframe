//! What must fail: a wrong PoP code, tampering, a replayed counter, the wrong enrolment key.

mod common;

use coldframe_crypto::frame::{self, ReplayWindow};
use coldframe_crypto::heartbeat::Request;
use coldframe_crypto::setup::{Role, SetupSession};
use coldframe_crypto::spec::ENROLMENT_INFO;
use coldframe_crypto::{DeviceId, DeviceKeys, Error, hpke};
use common::{array, bytes, list, number, text, vectors};
use serde_json::Value;

fn sessions(case: &Value, app_code: &str, hub_code: &str) -> (SetupSession, SetupSession) {
    let app_private: [u8; 32] = array(case, "appPrivateKey");
    let hub_private: [u8; 32] = array(case, "hubPrivateKey");
    let app = SetupSession::new(
        Role::App,
        &app_private,
        &hpke::public_key(&hub_private),
        app_code,
    )
    .unwrap();
    let hub = SetupSession::new(
        Role::Device,
        &hub_private,
        &hpke::public_key(&app_private),
        hub_code,
    )
    .unwrap();
    (app, hub)
}

#[test]
fn a_wrong_setup_code_fails_the_first_message_with_a_distinct_error() {
    let all = vectors();
    let case = &list(&all, "setup")[0];
    let (mut app, mut hub) = sessions(case, text(case, "popCode"), text(case, "wrongPopCode"));
    let mut sealed = [0u8; 64];
    let (counter, length) = app.seal(b"identity please", &mut sealed).unwrap();
    let mut opened = [0u8; 64];
    assert_eq!(
        hub.open(counter, &sealed[..length], &mut opened),
        Err(Error::WrongSetupCode)
    );
    assert!(opened.iter().all(|&byte| byte == 0), "no plaintext leaks");
}

#[test]
fn a_tampered_setup_message_after_the_first_is_an_authentication_failure() {
    let all = vectors();
    let case = &list(&all, "setup")[0];
    let (mut app, mut hub) = sessions(case, text(case, "popCode"), text(case, "normalizedPopCode"));
    let mut sealed = [0u8; 64];
    let mut opened = [0u8; 64];
    let (counter, length) = app.seal(b"one", &mut sealed).unwrap();
    hub.open(counter, &sealed[..length], &mut opened).unwrap();
    let (counter, length) = app.seal(b"two", &mut sealed).unwrap();
    sealed[0] ^= 1;
    assert_eq!(
        hub.open(counter, &sealed[..length], &mut opened),
        Err(Error::AuthenticationFailed)
    );
}

#[test]
fn a_replayed_setup_counter_is_refused() {
    let all = vectors();
    let case = &list(&all, "setup")[0];
    let (mut app, mut hub) = sessions(case, text(case, "popCode"), text(case, "popCode"));
    let mut first = [0u8; 64];
    let mut opened = [0u8; 64];
    let (counter, length) = app.seal(b"one", &mut first).unwrap();
    hub.open(counter, &first[..length], &mut opened).unwrap();
    assert_eq!(
        hub.open(counter, &first[..length], &mut opened),
        Err(Error::Replay)
    );
}

#[test]
fn a_tampered_frame_or_changed_aad_fails() {
    let all = vectors();
    let case = &list(&all, "frames")[0];
    let key: [u8; 32] = array(case, "key");
    let device_id = DeviceId(array(case, "deviceId"));
    let counter = number(case, "counter");
    let sealed = bytes(case, "ciphertext");
    let mut out = vec![0u8; sealed.len()];

    let mut flipped = sealed.clone();
    flipped[0] ^= 0x80;
    assert_eq!(
        frame::open(&key, &device_id, counter, &flipped, &mut out),
        Err(Error::AuthenticationFailed)
    );
    let mut flipped_tag = sealed.clone();
    *flipped_tag.last_mut().unwrap() ^= 1;
    assert_eq!(
        frame::open(&key, &device_id, counter, &flipped_tag, &mut out),
        Err(Error::AuthenticationFailed)
    );
    assert_eq!(
        frame::open(&key, &device_id, counter + 1, &sealed, &mut out),
        Err(Error::AuthenticationFailed)
    );
    let mut other_device = device_id;
    other_device.0[7] ^= 1;
    assert_eq!(
        frame::open(&key, &other_device, counter, &sealed, &mut out),
        Err(Error::AuthenticationFailed)
    );
    assert_eq!(
        frame::open(&key, &device_id, counter, &sealed[..15], &mut out),
        Err(Error::AuthenticationFailed)
    );
}

#[test]
fn a_replayed_frame_is_refused_and_a_forged_one_does_not_move_the_window() {
    let all = vectors();
    let case = &list(&all, "frames")[0];
    let key: [u8; 32] = array(case, "key");
    let device_id = DeviceId(array(case, "deviceId"));
    let counter = number(case, "counter");
    let sealed = bytes(case, "ciphertext");
    let mut out = vec![0u8; sealed.len()];
    let mut window = ReplayWindow::new();

    let mut forged = sealed.clone();
    forged[1] ^= 1;
    assert_eq!(
        frame::open_checked(&key, &device_id, counter, &forged, &mut out, &mut window),
        Err(Error::AuthenticationFailed)
    );
    assert_eq!(window.highest(), None);
    assert!(frame::open_checked(&key, &device_id, counter, &sealed, &mut out, &mut window).is_ok());
    assert_eq!(
        frame::open_checked(&key, &device_id, counter, &sealed, &mut out, &mut window),
        Err(Error::Replay)
    );
}

#[test]
fn enrolment_opens_only_with_the_right_key_and_device_id() {
    let all = vectors();
    let case = &list(&all, "enrolment")[0];
    let keys = DeviceKeys::from_root_key(&array(case, "rootKey"));
    let recipient: [u8; 32] = array(case, "recipientPrivateKey");
    let sealed =
        hpke::seal_enrolment(&hpke::public_key(&recipient), &array(case, "ikmE"), &keys).unwrap();
    let mut out = [0u8; 32];

    let mut wrong = recipient;
    wrong[0] ^= 0x40;
    let info = ENROLMENT_INFO.as_bytes();
    assert_eq!(
        hpke::open_base(
            &wrong,
            &sealed.enc,
            info,
            sealed.device_id.as_bytes(),
            &sealed.ciphertext,
            &mut out
        ),
        Err(Error::AuthenticationFailed)
    );
    let mut other_id = *sealed.device_id.as_bytes();
    other_id[0] ^= 1;
    assert_eq!(
        hpke::open_base(
            &recipient,
            &sealed.enc,
            info,
            &other_id,
            &sealed.ciphertext,
            &mut out
        ),
        Err(Error::AuthenticationFailed)
    );
}

#[test]
fn a_small_order_public_key_is_refused() {
    let all = vectors();
    let keys = DeviceKeys::from_root_key(&array(&list(&all, "enrolment")[0], "rootKey"));
    assert_eq!(
        hpke::seal_enrolment(&[0u8; 32], &[7u8; 32], &keys).err(),
        Some(Error::InvalidPublicKey)
    );
}

#[test]
fn a_changed_body_or_path_breaks_the_heartbeat_signature() {
    let all = vectors();
    let case = &list(&all, "heartbeat")[0];
    let key: [u8; 32] = array(case, "hubAuthKey");
    let body = bytes(case, "body");
    let nonce: [u8; 16] = array(case, "nonce");
    let signature = bytes(case, "signature");
    let request = Request {
        method: text(case, "method"),
        path: text(case, "path"),
        body: &body,
        timestamp_ms: number(case, "timestampMs"),
        nonce: &nonce,
    };
    assert!(request.verify(&key, &signature).is_ok());
    let mut changed_body = body.clone();
    changed_body[0] ^= 1;
    assert!(
        Request {
            body: &changed_body,
            ..request
        }
        .verify(&key, &signature)
        .is_err()
    );
    assert!(
        Request {
            path: "/device/ingest",
            ..request
        }
        .verify(&key, &signature)
        .is_err()
    );
    assert!(
        Request {
            timestamp_ms: request.timestamp_ms + 1,
            ..request
        }
        .verify(&key, &signature)
        .is_err()
    );
}

#[test]
fn an_invalid_setup_code_is_refused() {
    let all = vectors();
    let case = &list(&all, "setup")[0];
    let app_private: [u8; 32] = array(case, "appPrivateKey");
    let hub_public = hpke::public_key(&array(case, "hubPrivateKey"));
    let too_long = "A".repeat(coldframe_crypto::spec::SETUP_MAX_CODE_LENGTH + 1);
    for code in ["", too_long.as_str(), "Ä1B2C3"] {
        assert_eq!(
            SetupSession::new(Role::App, &app_private, &hub_public, code).err(),
            Some(Error::InvalidSetupCode),
            "{code:?}"
        );
    }
}

#[test]
fn a_counter_that_cannot_advance_is_a_replay_before_decrypting() {
    let all = vectors();
    let case = &list(&all, "setup")[0];
    let (_, mut hub) = sessions(case, text(case, "popCode"), text(case, "popCode"));
    let mut out = [0xaau8; 32];
    assert_eq!(hub.open(u64::MAX, &[0u8; 32], &mut out), Err(Error::Replay));
    assert!(out.iter().all(|&byte| byte == 0xaa), "nothing written");
}
