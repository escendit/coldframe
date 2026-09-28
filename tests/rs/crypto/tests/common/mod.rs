//! Reads `packages/crypto-spec/vectors.json`, the single source of the shared vectors.

#![allow(dead_code, reason = "each test file uses a different part")]

use serde_json::Value;

/// The parsed vectors file.
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
    decode(text(value, field))
}

/// A lowercase hex field as a fixed-size array.
pub fn array<const N: usize>(value: &Value, field: &str) -> [u8; N] {
    bytes(value, field)
        .try_into()
        .unwrap_or_else(|_| panic!("{field} has {N} bytes"))
}

/// A decimal-string 64-bit field.
pub fn number(value: &Value, field: &str) -> u64 {
    text(value, field)
        .parse()
        .unwrap_or_else(|_| panic!("{field} is a decimal u64"))
}

/// A list field.
pub fn list<'a>(value: &'a Value, field: &str) -> &'a Vec<Value> {
    value[field]
        .as_array()
        .unwrap_or_else(|| panic!("{field} is a list"))
}

/// Decodes lowercase hex.
pub fn decode(text: &str) -> Vec<u8> {
    assert!(text.len().is_multiple_of(2), "odd hex length");
    (0..text.len())
        .step_by(2)
        .map(|i| u8::from_str_radix(&text[i..i + 2], 16).expect("hex digits"))
        .collect()
}

/// Encodes lowercase hex.
pub fn encode(bytes: &[u8]) -> String {
    bytes.iter().map(|byte| format!("{byte:02x}")).collect()
}
