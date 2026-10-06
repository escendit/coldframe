//! Heartbeat signing against `packages/crypto-spec/vectors.json` `heartbeat[0]`.

mod common;

use coldframe_crypto::spec::{
    HEARTBEAT_DEVICE_HEADER, HEARTBEAT_NONCE_HEADER, HEARTBEAT_SIGNATURE_HEADER,
    HEARTBEAT_TIMESTAMP_HEADER,
};
use coldframe_uplink::json::HeartbeatRequest;
use coldframe_uplink::{
    HEARTBEAT_METHOD, HEARTBEAT_PATH, INGEST_METHOD, INGEST_PATH, sign_heartbeat, sign_request,
};
use common::{array, bytes, heartbeat_vector, text, vector_keys};

#[test]
fn the_vector_request_is_what_the_hub_sends() {
    let vector = heartbeat_vector();
    assert_eq!(text(&vector, "method"), HEARTBEAT_METHOD);
    assert_eq!(text(&vector, "path"), HEARTBEAT_PATH);
    // The vector's body is exactly the Hub's JSON for uptime 61 000 ms.
    let mut body = [0u8; 64];
    let length = HeartbeatRequest::new(61_000).encode(&mut body).unwrap();
    assert_eq!(&body[..length], bytes(&vector, "body").as_slice());
    assert_eq!(
        std::str::from_utf8(&body[..length]).unwrap(),
        r#"{"protocolVersion":1,"uptimeMs":61000}"#
    );
}

#[test]
fn signing_matches_the_vector_headers() {
    let vector = heartbeat_vector();
    let keys = vector_keys();
    assert_eq!(keys.hub_auth_key, array(&vector, "hubAuthKey"));
    let timestamp: u64 = text(&vector, "timestampMs").parse().unwrap();
    let headers = sign_heartbeat(
        &keys,
        &bytes(&vector, "body"),
        timestamp,
        &array(&vector, "nonce"),
    );
    let expected = &vector["headers"];
    assert_eq!(headers.device(), text(expected, HEARTBEAT_DEVICE_HEADER));
    assert_eq!(
        headers.timestamp(),
        text(expected, HEARTBEAT_TIMESTAMP_HEADER)
    );
    assert_eq!(headers.nonce(), text(expected, HEARTBEAT_NONCE_HEADER));
    assert_eq!(
        headers.signature(),
        text(expected, HEARTBEAT_SIGNATURE_HEADER)
    );
    assert_eq!(headers.signature(), text(&vector, "signature"));
    let pairs = headers.pairs();
    let names: Vec<&str> = pairs.iter().map(|(name, _)| *name).collect();
    assert_eq!(
        names,
        [
            "X-Coldframe-Device",
            "X-Coldframe-Timestamp",
            "X-Coldframe-Nonce",
            "X-Coldframe-Signature"
        ]
    );
    for (name, value) in pairs {
        assert_eq!(value, text(expected, name));
    }
}

#[test]
fn any_change_changes_the_signature() {
    let vector = heartbeat_vector();
    let keys = vector_keys();
    let body = bytes(&vector, "body");
    let timestamp: u64 = text(&vector, "timestampMs").parse().unwrap();
    let nonce: [u8; 16] = array(&vector, "nonce");
    let reference = sign_heartbeat(&keys, &body, timestamp, &nonce)
        .signature()
        .to_owned();
    let mut other_body = body.clone();
    other_body[5] ^= 1;
    let mut other_nonce = nonce;
    other_nonce[0] ^= 1;
    for signature in [
        sign_heartbeat(&keys, &other_body, timestamp, &nonce)
            .signature()
            .to_owned(),
        sign_heartbeat(&keys, &body, timestamp + 1, &nonce)
            .signature()
            .to_owned(),
        sign_heartbeat(&keys, &body, timestamp, &other_nonce)
            .signature()
            .to_owned(),
        sign_heartbeat(
            &coldframe_crypto::DeviceKeys::from_root_key(&[7; 32]),
            &body,
            timestamp,
            &nonce,
        )
        .signature()
        .to_owned(),
    ] {
        assert_ne!(signature, reference);
    }
}

#[test]
fn a_request_is_signed_for_its_own_path() {
    let vector = heartbeat_vector();
    let keys = vector_keys();
    let body = bytes(&vector, "body");
    let timestamp: u64 = text(&vector, "timestampMs").parse().unwrap();
    let nonce = array(&vector, "nonce");
    // The heartbeat wrapper is the generic signer with the heartbeat's method and path.
    let heartbeat = sign_heartbeat(&keys, &body, timestamp, &nonce);
    let generic = sign_request(
        &keys,
        HEARTBEAT_METHOD,
        HEARTBEAT_PATH,
        &body,
        timestamp,
        &nonce,
    );
    assert_eq!(generic.signature(), heartbeat.signature());
    assert_eq!(generic.signature(), text(&vector, "signature"));

    // The same body signed for /device/ingest has another signature, which verifies for that
    // path and for no other.
    assert_eq!((INGEST_METHOD, INGEST_PATH), ("POST", "/device/ingest"));
    let ingest = sign_request(&keys, INGEST_METHOD, INGEST_PATH, &body, timestamp, &nonce);
    assert_ne!(ingest.signature(), heartbeat.signature());
    assert_eq!(ingest.device(), heartbeat.device());
    assert_eq!(ingest.timestamp(), heartbeat.timestamp());
    assert_eq!(ingest.nonce(), heartbeat.nonce());
    let signature: Vec<u8> = (0..64)
        .step_by(2)
        .map(|i| u8::from_str_radix(&ingest.signature()[i..i + 2], 16).unwrap())
        .collect();
    let request = |path| coldframe_crypto::heartbeat::Request {
        method: "POST",
        path,
        body: &body,
        timestamp_ms: timestamp,
        nonce: &nonce,
    };
    assert!(
        request("/device/ingest")
            .verify(&keys.hub_auth_key, &signature)
            .is_ok()
    );
    assert!(
        request("/device/heartbeat")
            .verify(&keys.hub_auth_key, &signature)
            .is_err()
    );
}
