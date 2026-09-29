//! The golden Hub JSON fixtures of `packages/openapi` (AD-10, AD-24): the hand-written structs
//! decode every response fixture and encode the request fixtures' values back to the same JSON.
//! `pnpm --filter @coldframe/openapi run check` keeps the fixtures in step with the contract.

mod common;

use std::collections::BTreeSet;

use coldframe_uplink::json::{HeartbeatRequest, HeartbeatResponse, server_time_ms};
use coldframe_uplink::parse_rfc3339_ms;
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
