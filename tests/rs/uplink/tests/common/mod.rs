//! Shared helpers: the vectors, the fixtures, a blocking executor and the fixture Hub.

#![allow(dead_code, reason = "each test file uses a different part")]

use std::future::Future;
use std::path::PathBuf;
use std::pin::pin;
use std::task::{Context, Poll, Waker};

use coldframe_crypto::DeviceKeys;
use coldframe_hal::mock::{MockRadio, MockTrng};
use coldframe_hal::{AccessPoint, Radio, Security};
use serde_json::Value;

/// The repository root.
pub fn repo() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../..")
}

/// A JSON file of the repository.
pub fn json(relative: &str) -> Value {
    let path = repo().join(relative);
    let text = std::fs::read_to_string(&path)
        .unwrap_or_else(|error| panic!("{} is readable: {error}", path.display()));
    serde_json::from_str(&text).unwrap_or_else(|error| panic!("{relative} is JSON: {error}"))
}

/// The parsed `packages/crypto-spec/vectors.json`.
pub fn vectors() -> Value {
    json("packages/crypto-spec/vectors.json")
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

/// The heartbeat vector `heartbeat[0]`.
pub fn heartbeat_vector() -> Value {
    vectors()["heartbeat"][0].clone()
}

/// The keys of the heartbeat vector's Hub.
pub fn vector_keys() -> DeviceKeys {
    DeviceKeys::from_root_key(&array(&heartbeat_vector(), "rootKey"))
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

/// A TRNG whose radio is on and that hands out `bytes` first.
pub fn trng(bytes: &[u8]) -> MockTrng {
    let mut radio = MockRadio::new();
    radio.enable().unwrap();
    MockTrng::with_bytes(&radio, bytes)
}

/// The fixture SSID.
pub const SSID: &str = "garden";
/// A test fixture, not a real credential.
pub const PASSWORD: &str = "fixture-passphrase";
/// A test Server; never a real one.
pub const SERVER: &str = "https://coldframe.example.org";

/// The strongest `garden` access point of [`scan`].
pub const STRONG: [u8; 6] = [0x02, 0, 0, 0, 0, 0xB0];

/// What the fixture Hub hears: `garden` on two BSSIDs, and others.
pub fn scan() -> Vec<AccessPoint> {
    vec![
        AccessPoint::new(
            b"garden",
            [0x02, 0, 0, 0, 0, 0xA0],
            1,
            -70,
            Security::Wpa2Personal,
        ),
        AccessPoint::new(b"garden", STRONG, 6, -50, Security::Wpa2Personal),
        AccessPoint::new(
            b"neighbour",
            [0x02, 0, 0, 0, 0, 0xC0],
            11,
            -40,
            Security::Wpa2Personal,
        ),
    ]
}
