//! coldframe-crypto reproduces every shared vector of `packages/crypto-spec/vectors.json`.

mod common;

use coldframe_crypto::frame::{self, ReplayWindow};
use coldframe_crypto::heartbeat::Request;
use coldframe_crypto::setup::{self, Role, SetupSession};
use coldframe_crypto::spec::{
    ENROLMENT_INFO, HEARTBEAT_DEVICE_HEADER, HEARTBEAT_NONCE_HEADER, HEARTBEAT_SIGNATURE_HEADER,
    HEARTBEAT_TIMESTAMP_HEADER, PROTOCOL_MAJOR, REPLAY_WINDOW,
};
use coldframe_crypto::{DeviceId, DeviceKeys, hpke, keys};
use common::{array, bytes, decode, encode, list, number, text, vectors};

#[test]
fn the_vectors_use_the_generated_protocol_major() {
    assert_eq!(vectors()["protocolMajor"], u64::from(PROTOCOL_MAJOR));
}

#[test]
fn key_hierarchy() {
    let all = vectors();
    let cases = list(&all, "keyHierarchy");
    assert!(!cases.is_empty());
    for case in cases {
        let root: [u8; 32] = array(case, "rootKey");
        let device_key = keys::derive_device_key(&root);
        assert_eq!(
            encode(&device_key),
            text(case, "deviceKey"),
            "{}",
            text(case, "name")
        );
        let derived = DeviceKeys::from_device_key(device_key);
        assert_eq!(encode(&derived.seal_key), text(case, "sealKey"));
        assert_eq!(encode(&derived.ack_key), text(case, "ackKey"));
        assert_eq!(encode(&derived.hub_auth_key), text(case, "hubAuthKey"));
        assert_eq!(encode(derived.device_id.as_bytes()), text(case, "deviceId"));
        assert_eq!(
            std::str::from_utf8(&derived.device_id.to_hex()).unwrap(),
            text(case, "deviceId")
        );
    }
}

#[test]
fn frames() {
    let all = vectors();
    let cases = list(&all, "frames");
    assert!(cases.iter().any(|case| text(case, "purpose") == "seal"));
    assert!(cases.iter().any(|case| text(case, "purpose") == "ack"));
    for case in cases {
        let keys = DeviceKeys::from_root_key(&array(case, "rootKey"));
        let key = match text(case, "purpose") {
            "seal" => keys.seal_key,
            "ack" => keys.ack_key,
            other => panic!("unknown purpose {other}"),
        };
        assert_eq!(encode(&key), text(case, "key"));
        let device_id = DeviceId(array(case, "deviceId"));
        assert_eq!(device_id, keys.device_id);
        let counter = number(case, "counter");
        let plaintext = bytes(case, "plaintext");
        assert_eq!(
            encode(&frame::nonce(&device_id, counter)),
            text(case, "nonce")
        );
        assert_eq!(encode(&frame::aad(&device_id, counter)), text(case, "aad"));

        let mut sealed = vec![0u8; plaintext.len() + 16];
        let length = frame::seal(&key, &device_id, counter, &plaintext, &mut sealed).unwrap();
        assert_eq!(
            encode(&sealed[..length]),
            text(case, "ciphertext"),
            "{}",
            text(case, "name")
        );

        let mut opened = vec![0u8; plaintext.len()];
        let length = frame::open(
            &key,
            &device_id,
            counter,
            &bytes(case, "ciphertext"),
            &mut opened,
        )
        .unwrap();
        assert_eq!(opened[..length], plaintext[..]);
    }
}

#[test]
fn replay_window() {
    let all = vectors();
    let case = &all["replay"];
    assert_eq!(case["window"], REPLAY_WINDOW as u64);
    let steps = list(case, "steps");
    assert!(steps.iter().any(|step| step["accepted"] == false));
    let mut window = ReplayWindow::new();
    for step in steps {
        let counter = number(step, "counter");
        let accepted = step["accepted"].as_bool().unwrap();
        assert_eq!(
            window.accept(counter).is_ok(),
            accepted,
            "counter {counter}"
        );
    }
}

#[test]
fn enrolment() {
    let all = vectors();
    let cases = list(&all, "enrolment");
    assert!(!cases.is_empty());
    for case in cases {
        let keys = DeviceKeys::from_root_key(&array(case, "rootKey"));
        let recipient_private: [u8; 32] = array(case, "recipientPrivateKey");
        let recipient_public = hpke::public_key(&recipient_private);
        assert_eq!(encode(&recipient_public), text(case, "recipientPublicKey"));
        assert_eq!(
            std::str::from_utf8(&hpke::fingerprint(&recipient_public)).unwrap(),
            text(case, "fingerprint")
        );
        assert_eq!(decode(text(case, "info")), ENROLMENT_INFO.as_bytes());
        let ikm_e: [u8; 32] = array(case, "ikmE");
        let (ephemeral, enc) = hpke::derive_key_pair(&ikm_e);
        assert_eq!(encode(&ephemeral), text(case, "ephemeralPrivateKey"));
        assert_eq!(encode(&enc), text(case, "enc"));

        let sealed = hpke::seal_enrolment(&recipient_public, &ikm_e, &keys).unwrap();
        assert_eq!(encode(sealed.device_id.as_bytes()), text(case, "deviceId"));
        assert_eq!(encode(&sealed.enc), text(case, "enc"));
        assert_eq!(
            encode(&sealed.ciphertext),
            text(case, "ciphertext"),
            "{}",
            text(case, "name")
        );

        let mut opened = [0u8; 32];
        hpke::open_base(
            &recipient_private,
            &sealed.enc,
            ENROLMENT_INFO.as_bytes(),
            sealed.device_id.as_bytes(),
            &sealed.ciphertext,
            &mut opened,
        )
        .unwrap();
        assert_eq!(encode(&opened), text(case, "deviceKey"));
    }
}

#[test]
fn setup_session() {
    let all = vectors();
    let cases = list(&all, "setup");
    assert!(!cases.is_empty());
    for case in cases {
        let app_private: [u8; 32] = array(case, "appPrivateKey");
        let hub_private: [u8; 32] = array(case, "hubPrivateKey");
        let app_public = hpke::public_key(&app_private);
        let hub_public = hpke::public_key(&hub_private);
        assert_eq!(encode(&app_public), text(case, "appPublicKey"));
        assert_eq!(encode(&hub_public), text(case, "hubPublicKey"));
        assert_eq!(
            encode(&hpke::diffie_hellman(&app_private, &hub_public).unwrap()),
            text(case, "sharedSecret")
        );
        let code = text(case, "popCode");
        for (own, peer) in [(&app_private, &hub_public), (&hub_private, &app_public)] {
            let keys = setup::derive_keys(own, peer, &app_public, &hub_public, code).unwrap();
            assert_eq!(encode(&keys.app_to_device), text(case, "appToHubKey"));
            assert_eq!(encode(&keys.device_to_app), text(case, "hubToAppKey"));
        }
        assert_eq!(bytes(case, "aad"), [PROTOCOL_MAJOR]);

        let mut app = SetupSession::new(Role::App, &app_private, &hub_public, code).unwrap();
        let mut hub = SetupSession::new(
            Role::Device,
            &hub_private,
            &app_public,
            text(case, "normalizedPopCode"),
        )
        .unwrap();
        for message in list(case, "messages") {
            let counter = number(message, "counter");
            let plaintext = bytes(message, "plaintext");
            assert_eq!(encode(&setup::nonce(counter)), text(message, "nonce"));
            let (sender, receiver) = match text(message, "direction") {
                "appToHub" => (&mut app, &mut hub),
                "hubToApp" => (&mut hub, &mut app),
                other => panic!("unknown direction {other}"),
            };
            let mut sealed = vec![0u8; plaintext.len() + 16];
            let (sent_counter, length) = sender.seal(&plaintext, &mut sealed).unwrap();
            assert_eq!(sent_counter, counter);
            assert_eq!(encode(&sealed[..length]), text(message, "ciphertext"));
            let mut opened = vec![0u8; plaintext.len()];
            let length = receiver
                .open(counter, &sealed[..length], &mut opened)
                .unwrap();
            assert_eq!(opened[..length], plaintext[..]);
        }
    }
}

#[test]
fn heartbeat() {
    let all = vectors();
    let cases = list(&all, "heartbeat");
    assert!(!cases.is_empty());
    for case in cases {
        let keys = DeviceKeys::from_root_key(&array(case, "rootKey"));
        assert_eq!(encode(&keys.hub_auth_key), text(case, "hubAuthKey"));
        let body = bytes(case, "body");
        let nonce: [u8; 16] = array(case, "nonce");
        let request = Request {
            method: text(case, "method"),
            path: text(case, "path"),
            body: &body,
            timestamp_ms: number(case, "timestampMs"),
            nonce: &nonce,
        };
        let mut canonical = String::new();
        request.write_canonical(&mut canonical).unwrap();
        assert_eq!(canonical, text(case, "canonical"));
        assert!(canonical.contains(text(case, "bodyHash")));
        let signature = request.signature_hex(&keys.hub_auth_key);
        assert_eq!(
            std::str::from_utf8(&signature).unwrap(),
            text(case, "signature")
        );
        assert!(
            request
                .verify(&keys.hub_auth_key, &bytes(case, "signature"))
                .is_ok()
        );

        let headers = &case["headers"];
        assert_eq!(headers[HEARTBEAT_DEVICE_HEADER], text(case, "deviceId"));
        assert_eq!(
            std::str::from_utf8(&keys.device_id.to_hex()).unwrap(),
            text(case, "deviceId")
        );
        assert_eq!(
            headers[HEARTBEAT_TIMESTAMP_HEADER],
            text(case, "timestampMs")
        );
        assert_eq!(headers[HEARTBEAT_NONCE_HEADER], text(case, "nonce"));
        assert_eq!(headers[HEARTBEAT_SIGNATURE_HEADER], text(case, "signature"));
    }
}
