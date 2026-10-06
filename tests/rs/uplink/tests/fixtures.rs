//! The golden Hub JSON fixtures of `packages/openapi` (AD-10, AD-24): the hand-written structs
//! decode every response fixture and encode the request fixtures' values back to the same JSON.
//! `pnpm --filter @coldframe/openapi run check` keeps the fixtures in step with the contract.

mod common;

use std::collections::BTreeSet;

use coldframe_uplink::json::{
    HeartbeatRequest, HeartbeatResponse, IngestResponse, IngestStatus, JsonError,
    encode_ingest_request, server_time_ms,
};
use coldframe_uplink::{
    FRAME_BASE64_MAX, INGEST_BATCH_MAX, INGEST_FRAMES_MAX, INGEST_REQUEST_MAX, base64,
    parse_rfc3339_ms,
};
use common::{json, repo};
use serde_json::Value;

const DIR: &str = "packages/openapi/fixtures/hub";

fn fixtures(prefix: &str) -> Vec<(String, Value)> {
    let mut found: Vec<(String, Value)> = std::fs::read_dir(repo().join(DIR))
        .expect("the fixture directory exists")
        .map(|entry| entry.unwrap().file_name().into_string().unwrap())
        .filter(|name| name.starts_with(prefix) && name.ends_with(".json"))
        .map(|name| {
            let value = json(&format!("{DIR}/{name}"));
            (name, value)
        })
        .collect();
    found.sort_by(|a, b| a.0.cmp(&b.0));
    found
}

fn schema(name: &str) -> (BTreeSet<String>, BTreeSet<String>) {
    let schemas = json(&format!("{DIR}/schemas.json"));
    let list = |field: &str| -> BTreeSet<String> {
        schemas[name][field]
            .as_array()
            .unwrap_or_else(|| panic!("{name}.{field} is a list"))
            .iter()
            .map(|key| key.as_str().unwrap().to_owned())
            .collect()
    };
    (list("properties"), list("required"))
}

fn keys(value: &Value) -> BTreeSet<String> {
    value
        .as_object()
        .expect("an object")
        .keys()
        .cloned()
        .collect()
}

#[test]
fn every_response_fixture_decodes() {
    let found = fixtures("heartbeat-response-");
    let names: Vec<&str> = found.iter().map(|(name, _)| name.as_str()).collect();
    assert_eq!(
        names,
        [
            "heartbeat-response-extra-field.json",
            "heartbeat-response-fraction.json",
            "heartbeat-response-no-fraction.json",
        ]
    );
    let (properties, required) = schema("HeartbeatResponse");
    for (name, value) in &found {
        let text = serde_json::to_vec(value).unwrap();
        let response = HeartbeatResponse::decode(&text)
            .unwrap_or_else(|error| panic!("{name} decodes: {error}"));
        let expected = value["serverTime"].as_str().unwrap();
        assert_eq!(response.server_time, expected, "{name}");
        assert_eq!(
            server_time_ms(&text),
            Ok(parse_rfc3339_ms(expected).unwrap()),
            "{name}"
        );
        // The struct reads every required key, and nothing it reads is outside the schema.
        assert!(required.contains("serverTime") && properties.contains("serverTime"));
        // Pretty-printed as committed, too.
        let committed = std::fs::read(repo().join(DIR).join(name)).unwrap();
        assert_eq!(
            HeartbeatResponse::decode(&committed).unwrap().server_time,
            expected
        );
    }
}

#[test]
fn the_request_fixtures_encode_back_to_the_same_json() {
    let found = fixtures("heartbeat-request-");
    let names: Vec<&str> = found.iter().map(|(name, _)| name.as_str()).collect();
    assert_eq!(
        names,
        [
            "heartbeat-request-full.json",
            "heartbeat-request-minimal.json"
        ]
    );
    let (properties, required) = schema("HeartbeatRequest");
    for (name, value) in &found {
        let request = HeartbeatRequest {
            protocol_version: u32::try_from(value["protocolVersion"].as_u64().unwrap()).unwrap(),
            uptime_ms: value.get("uptimeMs").map(|uptime| uptime.as_u64().unwrap()),
        };
        let mut out = [0u8; 128];
        let length = request.encode(&mut out).unwrap();
        let encoded: Value = serde_json::from_slice(&out[..length]).unwrap();
        assert_eq!(&encoded, value, "{name}");
        let encoded_keys = keys(&encoded);
        assert!(
            encoded_keys.is_subset(&properties),
            "{name}: {encoded_keys:?}"
        );
        assert!(
            required.is_subset(&encoded_keys),
            "{name}: {encoded_keys:?}"
        );
    }
}

#[test]
fn what_the_hub_sends_fits_the_schema() {
    let (properties, required) = schema("HeartbeatRequest");
    let mut out = [0u8; 128];
    let length = HeartbeatRequest::new(u64::MAX).encode(&mut out).unwrap();
    let sent: Value = serde_json::from_slice(&out[..length]).unwrap();
    let sent_keys = keys(&sent);
    assert!(sent_keys.is_subset(&properties));
    assert!(required.is_subset(&sent_keys));
    assert_eq!(sent["protocolVersion"], 1);
    assert_eq!(sent["uptimeMs"], u64::MAX);
}

#[test]
fn bad_replies_do_not_decode() {
    for body in [
        &b""[..],
        b"not json",
        b"{}",
        b"[]",
        br#"{"serverTime":12}"#,
        br#"{"servertime":"2026-09-29T12:34:56Z"}"#,
        br#"{"serverTime":null}"#,
    ] {
        assert!(
            HeartbeatResponse::decode(body).is_err(),
            "{:?}",
            String::from_utf8_lossy(body)
        );
    }
    assert!(server_time_ms(br#"{"serverTime":"yesterday"}"#).is_err());
    assert!(server_time_ms(br#"{"serverTime":"2026-09-29T12:34:56+01:00"}"#).is_err());
}

// ---------------------------------------------------------------------------------------------
// Ingest (Story 4.4)

fn status(token: &str) -> IngestStatus {
    match token {
        "stored" => IngestStatus::Stored,
        "duplicate" => IngestStatus::Duplicate,
        "rejected_auth" => IngestStatus::RejectedAuth,
        "rejected_replay" => IngestStatus::RejectedReplay,
        "rejected_time" => IngestStatus::RejectedTime,
        "unknown_device" => IngestStatus::UnknownDevice,
        "retry" => IngestStatus::Retry,
        other => panic!("the fixtures name no status {other}"),
    }
}

#[test]
fn the_ingest_request_fixtures_encode_back_to_the_same_json() {
    let found = fixtures("ingest-request-");
    let names: Vec<&str> = found.iter().map(|(name, _)| name.as_str()).collect();
    assert_eq!(
        names,
        ["ingest-request-empty.json", "ingest-request-frames.json"]
    );
    let (properties, required) = schema("IngestRequest");
    assert_eq!(
        json(&format!("{DIR}/schemas.json"))["IngestRequest"]["maxFrames"],
        INGEST_FRAMES_MAX as u64
    );
    const { assert!(INGEST_BATCH_MAX <= INGEST_FRAMES_MAX) };
    for (name, value) in &found {
        // The frames as the radio delivered them: the bytes under the fixture's base64.
        let frames: Vec<Vec<u8>> = value["frames"]
            .as_array()
            .unwrap()
            .iter()
            .map(|frame| {
                let text = frame.as_str().unwrap();
                assert!(text.len() <= FRAME_BASE64_MAX);
                let mut bytes = [0u8; 256];
                let length = base64::decode(text.as_bytes(), &mut bytes).unwrap();
                bytes[..length].to_vec()
            })
            .collect();
        let mut out = [0u8; INGEST_REQUEST_MAX];
        let length = encode_ingest_request(frames.iter().map(Vec::as_slice), &mut out).unwrap();
        let encoded: Value = serde_json::from_slice(&out[..length]).unwrap();
        assert_eq!(&encoded, value, "{name}");
        let encoded_keys = keys(&encoded);
        assert!(encoded_keys.is_subset(&properties), "{name}");
        assert!(required.is_subset(&encoded_keys), "{name}");
    }
    // The exact bytes: no whitespace, frames in order.
    let mut out = [0u8; 64];
    let length = encode_ingest_request([&b"foo"[..], b"fo"], &mut out).unwrap();
    assert_eq!(&out[..length], br#"{"frames":["Zm9v","Zm8="]}"#);
    let length = encode_ingest_request(std::iter::empty(), &mut out).unwrap();
    assert_eq!(&out[..length], br#"{"frames":[]}"#);
    assert_eq!(
        encode_ingest_request([&[0u8; 60][..]], &mut out),
        Err(JsonError::BufferTooSmall)
    );
}

#[test]
fn every_ingest_response_fixture_decodes() {
    let found = fixtures("ingest-response-");
    let names: Vec<&str> = found.iter().map(|(name, _)| name.as_str()).collect();
    assert_eq!(
        names,
        [
            "ingest-response-empty.json",
            "ingest-response-every-status.json",
            "ingest-response-extra-field.json",
            "ingest-response-mixed.json",
        ]
    );
    let (properties, required) = schema("IngestResponse");
    assert!(required.contains("results") && properties.contains("results"));
    let (result_properties, result_required) = schema("IngestFrameResult");
    assert!(result_required.contains("status"));
    assert!(result_properties.contains("downlink") && !result_required.contains("downlink"));
    let schemas = json(&format!("{DIR}/schemas.json"));
    let statuses = schemas["IngestFrameResult"]["statuses"].as_array().unwrap();
    let mut seen = BTreeSet::new();
    for (name, value) in &found {
        // Compact and pretty-printed as committed.
        let compact = serde_json::to_vec(value).unwrap();
        let committed = std::fs::read(repo().join(DIR).join(name)).unwrap();
        for text in [&compact, &committed] {
            let response =
                IngestResponse::decode(text).unwrap_or_else(|error| panic!("{name}: {error}"));
            let expected = value["results"].as_array().unwrap();
            assert_eq!(response.results.len(), expected.len(), "{name}");
            for (result, expected) in response.results.iter().zip(expected) {
                let token = expected["status"].as_str().unwrap();
                assert_eq!(result.status, status(token), "{name}");
                assert_eq!(result.downlink, expected["downlink"].as_str(), "{name}");
                // A downlink comes exactly with stored and duplicate.
                assert_eq!(
                    result.downlink.is_some(),
                    matches!(
                        result.status,
                        IngestStatus::Stored | IngestStatus::Duplicate
                    )
                );
                seen.insert(token.to_owned());
            }
        }
    }
    // The fixtures show every status of the contract, and the Hub knows each.
    assert_eq!(seen.len(), statuses.len());
    for token in statuses {
        assert!(seen.contains(token.as_str().unwrap()));
    }
}

#[test]
fn bad_ingest_replies_do_not_decode_and_unknown_statuses_do() {
    for body in [
        &b""[..],
        b"not json",
        b"{}",
        b"[]",
        br#"{"results":{}}"#,
        br#"{"results":[{}]}"#,
        br#"{"results":[{"status":7}]}"#,
        br#"{"results":[{"downlink":"AQID"}]}"#,
        br#"{"Results":[]}"#,
    ] {
        assert_eq!(
            IngestResponse::decode(body).err(),
            Some(JsonError::Malformed),
            "{:?}",
            String::from_utf8_lossy(body)
        );
    }
    // A status a later Server adds is not an error: the Hub only passes downlinks on.
    let later = IngestResponse::decode(br#"{"results":[{"status":"quarantined"}]}"#).unwrap();
    assert_eq!(later.results[0].status, IngestStatus::Other);
    assert_eq!(later.results[0].downlink, None);
    // More results than the contract allows.
    let many = format!(
        r#"{{"results":[{}]}}"#,
        vec![r#"{"status":"retry"}"#; INGEST_FRAMES_MAX + 1].join(",")
    );
    assert!(IngestResponse::decode(many.as_bytes()).is_err());
    let most = format!(
        r#"{{"results":[{}]}}"#,
        vec![r#"{"status":"retry"}"#; INGEST_FRAMES_MAX].join(",")
    );
    assert_eq!(
        IngestResponse::decode(most.as_bytes())
            .unwrap()
            .results
            .len(),
        INGEST_FRAMES_MAX
    );
}
