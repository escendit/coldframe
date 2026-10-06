//! `coldframe.device.v1` through micropb, against the shared vectors of
//! `packages/crypto-spec/vectors.json` and the ingest fixture of `packages/openapi`: the Rust
//! `NodeFrame` encodes to the vector plaintext and seals to its ciphertext, the vector `Downlink`
//! decodes, and a `SealedEnvelope` is the bytes the Hub relays.

use coldframe_crypto::{DeviceId, DeviceKeys, frame};
use coldframe_protocol::device_v1::NodeFrame_::{Measured, Unsynced};
use coldframe_protocol::device_v1::{
    ChargeStatus, Downlink, NodeFrame, Quantity, Reading, ReadingSeqRange, SealedEnvelope,
    Specification, SpecificationSet, Unit,
};
use coldframe_protocol::{
    CodecError, DOWNLINK_MAX_SIZE, ENVELOPE_CIPHERTEXT_CAPACITY, NODE_FRAME_MAX_SIZE,
    PROTOCOL_VERSION, SEALED_ENVELOPE_MAX_SIZE, SPECIFICATION_SET_MAX_SIZE, decode, encode,
    encode_node_frame, radio,
};
use serde_json::Value;

fn repo_json(relative: &str) -> Value {
    let path = format!("{}/../../../{relative}", env!("CARGO_MANIFEST_DIR"));
    let text = std::fs::read_to_string(&path).unwrap_or_else(|error| panic!("{path}: {error}"));
    serde_json::from_str(&text).expect("JSON")
}

fn frame_vector(name: &str) -> Value {
    repo_json("packages/crypto-spec/vectors.json")["frames"]
        .as_array()
        .expect("frames")
        .iter()
        .find(|case| case["name"] == name)
        .unwrap_or_else(|| panic!("vector {name}"))
        .clone()
}

fn hex(value: &Value, field: &str) -> Vec<u8> {
    let text = value[field].as_str().expect("hex string");
    (0..text.len())
        .step_by(2)
        .map(|i| u8::from_str_radix(&text[i..i + 2], 16).expect("hex digits"))
        .collect()
}

fn number(value: &Value, field: &str) -> u64 {
    value[field]
        .as_str()
        .unwrap_or_else(|| panic!("{field} is a decimal string"))
        .parse()
        .expect("decimal")
}

fn signed(value: &Value, field: &str) -> i64 {
    value[field]
        .as_str()
        .expect("decimal string")
        .parse()
        .expect("decimal")
}

fn quantity(token: &str) -> Quantity {
    match token {
        "soil_moisture" => Quantity::SoilMoisture,
        "air_temperature" => Quantity::AirTemperature,
        "relative_humidity" => Quantity::RelativeHumidity,
        "gas_resistance" => Quantity::GasResistance,
        other => panic!("unknown quantity {other}"),
    }
}

/// Standard padded base64, for the fixture.
fn base64(text: &str) -> Vec<u8> {
    const ALPHABET: &[u8] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let mut out = Vec::new();
    let mut bits = 0u32;
    let mut count = 0;
    for byte in text.bytes().filter(|byte| *byte != b'=') {
        let value = ALPHABET.iter().position(|c| *c == byte).expect("base64");
        bits = bits << 6 | u32::try_from(value).unwrap();
        count += 6;
        if count >= 8 {
            count -= 8;
            out.push(u8::try_from(bits >> count & 0xFF).unwrap());
        }
    }
    out
}

/// The `NodeFrame` the vector's `message` breakdown describes.
fn vector_node_frame(message: &Value) -> NodeFrame {
    assert_eq!(message["type"], "NodeFrame");
    let mut frame = NodeFrame {
        protocol_version: u32::try_from(message["protocolVersion"].as_u64().unwrap()).unwrap(),
        spec_hash: heapless::Vec::from_slice(&hex(message, "specHash")).unwrap(),
        boot_id: number(message, "bootId"),
        uptime_ms: number(message, "uptimeMs"),
        report_seq: number(message, "reportSeq"),
        measured: Some(Measured::MeasuredAtMs(signed(message, "measuredAtMs"))),
        charging: match message["charging"].as_str().unwrap() {
            "charging" => ChargeStatus::Charging,
            "not_charging" => ChargeStatus::NotCharging,
            other => panic!("unknown charging {other}"),
        },
        ..NodeFrame::default()
    };
    for reading in message["readings"].as_array().unwrap() {
        frame
            .readings
            .push(Reading {
                slot: u32::try_from(reading["slot"].as_u64().unwrap()).unwrap(),
                reading_seq: number(reading, "readingSeq"),
                quantity: quantity(reading["quantity"].as_str().unwrap()),
                value: signed(reading, "value"),
            })
            .expect("four readings fit");
    }
    frame.set_battery_percent(u32::try_from(message["batteryPercent"].as_u64().unwrap()).unwrap());
    frame
}

#[test]
fn the_vector_node_frame_encodes_and_seals_to_the_vector_bytes() {
    let case = frame_vector("node frame with four readings");
    let frame = vector_node_frame(&case["message"]);
    assert_eq!(frame.readings.len(), 4);

    let mut plaintext = [0u8; NODE_FRAME_MAX_SIZE];
    let length = encode_node_frame(&frame, &mut plaintext).unwrap();
    assert_eq!(plaintext[..length], hex(&case, "plaintext")[..]);
    assert_eq!(decode::<NodeFrame>(&plaintext[..length]).unwrap(), frame);
    // micropb's own order (the oneof last) is the same message to any decoder.
    let mut reordered = [0u8; NODE_FRAME_MAX_SIZE];
    let reordered_length = encode(&frame, &mut reordered).unwrap();
    assert_eq!(reordered_length, length);
    assert_eq!(
        decode::<NodeFrame>(&reordered[..reordered_length]).unwrap(),
        frame
    );

    let keys = DeviceKeys::from_root_key(&hex(&case, "rootKey").try_into().unwrap());
    let device_id = DeviceId(hex(&case, "deviceId").try_into().unwrap());
    assert_eq!(device_id, keys.device_id);
    let counter = number(&case, "counter");
    let mut sealed = [0u8; NODE_FRAME_MAX_SIZE + 16];
    let sealed_length = frame::seal(
        &keys.seal_key,
        &device_id,
        counter,
        &plaintext[..length],
        &mut sealed,
    )
    .unwrap();
    assert_eq!(sealed[..sealed_length], hex(&case, "ciphertext")[..]);

    // The envelope around it is, byte for byte, the frame of the ingest request fixture.
    let envelope = SealedEnvelope {
        protocol_version: PROTOCOL_VERSION,
        device_id: heapless::Vec::from_slice(device_id.as_bytes()).unwrap(),
        counter,
        ciphertext: heapless::Vec::from_slice(&sealed[..sealed_length]).unwrap(),
    };
    let mut bytes = [0u8; SEALED_ENVELOPE_MAX_SIZE];
    let envelope_length = encode(&envelope, &mut bytes).unwrap();
    let fixture = repo_json("packages/openapi/fixtures/hub/ingest-request-frames.json");
    let relayed = base64(fixture["frames"][0].as_str().unwrap());
    assert_eq!(bytes[..envelope_length], relayed[..]);
    assert_eq!(decode::<SealedEnvelope>(&relayed).unwrap(), envelope);

    // It fits one radio message with its kind byte.
    let mut payload = [0u8; radio::MAX_PAYLOAD];
    let payload_length = radio::Message::Uplink(&bytes[..envelope_length])
        .encode(&mut payload)
        .unwrap();
    assert_eq!(payload_length, envelope_length + 1);
    assert_eq!(payload[0], radio::KIND_UPLINK);
}

#[test]
fn the_vector_downlink_decodes() {
    let case = frame_vector("downlink acknowledging the readings of the node frame");
    let downlink: Downlink = decode(&hex(&case, "plaintext")).unwrap();
    assert_eq!(downlink.protocol_version, 1);
    assert_eq!(downlink.acked_counter, 42);
    assert_eq!(downlink.server_time_ms, 1_790_000_000_321);
    assert!(downlink.commands.is_empty());
    assert!(!downlink.specifications_unknown);
    assert_eq!(
        downlink.acked_readings.as_slice(),
        &[ReadingSeqRange {
            first: 100,
            last: 104
        }]
    );
    let message = &case["message"];
    assert_eq!(downlink.acked_counter, number(message, "ackedCounter"));
    assert_eq!(downlink.server_time_ms, signed(message, "serverTimeMs"));

    // It re-encodes to the vector plaintext, and opens from the vector ciphertext.
    let mut plaintext = [0u8; DOWNLINK_MAX_SIZE];
    let length = encode(&downlink, &mut plaintext).unwrap();
    assert_eq!(plaintext[..length], hex(&case, "plaintext")[..]);
    let keys = DeviceKeys::from_root_key(&hex(&case, "rootKey").try_into().unwrap());
    let mut opened = [0u8; DOWNLINK_MAX_SIZE];
    let opened_length = frame::open(
        &keys.ack_key,
        &keys.device_id,
        number(&case, "counter"),
        &hex(&case, "ciphertext"),
        &mut opened,
    )
    .unwrap();
    assert_eq!(
        decode::<Downlink>(&opened[..opened_length]).unwrap(),
        downlink
    );

    // The downlink of the ingest response fixture is this one in its envelope.
    let fixture = repo_json("packages/openapi/fixtures/hub/ingest-response-mixed.json");
    let relayed = base64(fixture["results"][0]["downlink"].as_str().unwrap());
    let envelope: SealedEnvelope = decode(&relayed).unwrap();
    assert_eq!(envelope.protocol_version, 1);
    assert_eq!(envelope.device_id.as_slice(), keys.device_id.as_bytes());
    assert_eq!(envelope.counter, number(&case, "counter"));
    assert_eq!(
        envelope.ciphertext.as_slice(),
        &hex(&case, "ciphertext")[..]
    );
    assert!(relayed.len() <= radio::ENVELOPE_MAX);
}

#[test]
fn the_earlier_downlink_vector_decodes_without_ranges() {
    let case = frame_vector("downlink acknowledging counter 1");
    let downlink: Downlink = decode(&hex(&case, "plaintext")).unwrap();
    assert_eq!(downlink.protocol_version, 1);
    assert_eq!(downlink.acked_counter, 1);
    assert!(downlink.acked_readings.is_empty());
}

#[test]
fn an_unsynced_frame_with_its_specification_set_round_trips() {
    let mut soil = Specification {
        quantity: Quantity::SoilMoisture,
        unit: Unit::RawCount,
        range_min: 0,
        range_max: 4095,
        calibration: true,
        ..Specification::default()
    };
    soil.set_default_low(30);
    soil.set_default_high(80);
    let mut set = SpecificationSet::default();
    set.specifications.push(soil).unwrap();
    set.specifications
        .push(Specification {
            quantity: Quantity::AirTemperature,
            unit: Unit::MilliDegreeCelsius,
            range_min: -40_000,
            range_max: 85_000,
            ..Specification::default()
        })
        .unwrap();
    let mut encoded_set = [0u8; SPECIFICATION_SET_MAX_SIZE];
    let set_length = encode(&set, &mut encoded_set).unwrap();
    assert_eq!(
        decode::<SpecificationSet>(&encoded_set[..set_length]).unwrap(),
        set
    );

    let mut frame = NodeFrame {
        protocol_version: PROTOCOL_VERSION,
        spec_hash: heapless::Vec::from_slice(&[0xAB; 32]).unwrap(),
        boot_id: 7,
        uptime_ms: 5_400_123,
        report_seq: 9,
        measured: Some(Measured::Unsynced(Unsynced {
            boot_id: 6,
            uptime_ms: 900_000,
        })),
        ..NodeFrame::default()
    };
    frame.set_specifications(set.clone());
    let mut bytes = [0u8; NODE_FRAME_MAX_SIZE];
    let length = encode_node_frame(&frame, &mut bytes).unwrap();
    let decoded: NodeFrame = decode(&bytes[..length]).unwrap();
    assert_eq!(decoded, frame);
    assert_eq!(decoded.specifications(), Some(&set));
    assert_eq!(decoded.battery_percent(), None);
    let Some(Measured::Unsynced(unsynced)) = decoded.measured else {
        panic!("unsynced");
    };
    assert_eq!((unsynced.boot_id, unsynced.uptime_ms), (6, 900_000));
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
    // SealedEnvelope.device_id holds 8 bytes, its ciphertext the configured capacity.
    assert!(decode::<SealedEnvelope>(&field(2, 8, 1)).is_ok());
    assert_eq!(
        decode::<SealedEnvelope>(&field(2, 9, 1)).err(),
        Some(CodecError::Malformed)
    );
    assert!(decode::<SealedEnvelope>(&field(4, ENVELOPE_CIPHERTEXT_CAPACITY, 0)).is_ok());
    assert!(decode::<SealedEnvelope>(&field(4, ENVELOPE_CIPHERTEXT_CAPACITY + 1, 0)).is_err());
    // NodeFrame.spec_hash holds a SHA-256.
    assert!(decode::<NodeFrame>(&field(2, 32, 7)).is_ok());
    assert!(decode::<NodeFrame>(&field(2, 33, 7)).is_err());
    // Five Readings do not fit a Node with four Sensors.
    let readings: Vec<u8> = (0..5).flat_map(|_| field(8, 0, 0)).collect();
    assert!(decode::<NodeFrame>(&readings[..8]).is_ok());
    assert!(decode::<NodeFrame>(&readings).is_err());
    // Nine acknowledged ranges do not fit a Downlink.
    let ranges: Vec<u8> = (0..9).flat_map(|_| field(5, 0, 0)).collect();
    assert!(decode::<Downlink>(&ranges[..16]).is_ok());
    assert!(decode::<Downlink>(&ranges).is_err());
    // Truncated.
    assert!(decode::<SealedEnvelope>(&field(4, 20, 0)[..10]).is_err());
}

#[test]
fn radio_messages_round_trip() {
    let nonce = [1, 2, 3, 4, 5, 6, 7, 8];
    let envelope = [0x5A; radio::ENVELOPE_MAX];
    let mut buffer = [0u8; radio::MAX_PAYLOAD];
    for (message, kind, length) in [
        (radio::Message::Probe { nonce }, 0x01, 9),
        (radio::Message::ProbeReply { nonce, pending: 0 }, 0x02, 10),
        (radio::Message::ProbeReply { nonce, pending: 8 }, 0x02, 10),
        (radio::Message::Uplink(&envelope[..80]), 0x03, 81),
        (radio::Message::Downlink(&envelope[..33]), 0x04, 34),
        (radio::Message::Uplink(&envelope), 0x03, 250),
        (radio::Message::Downlink(&envelope), 0x04, 250),
    ] {
        let written = message.encode(&mut buffer).unwrap();
        assert_eq!(written, length, "{message:?}");
        assert_eq!(buffer[0], kind);
        assert_eq!(radio::Message::decode(&buffer[..written]), Ok(message));
    }
    // The wire layout: kind, nonce, pending.
    let written = radio::Message::ProbeReply { nonce, pending: 3 }
        .encode(&mut buffer)
        .unwrap();
    assert_eq!(buffer[..written], [0x02, 1, 2, 3, 4, 5, 6, 7, 8, 3]);
    assert_eq!(radio::PENDING_MAX, 8);
    assert_eq!(
        radio::Message::ProbeReply { nonce, pending: 9 }.encode(&mut buffer),
        Err(CodecError::Malformed)
    );
    assert_eq!(
        (
            radio::KIND_PROBE,
            radio::KIND_PROBE_REPLY,
            radio::KIND_UPLINK,
            radio::KIND_DOWNLINK
        ),
        (0x01, 0x02, 0x03, 0x04)
    );
    assert_eq!(radio::MAX_PAYLOAD, 250);
}

#[test]
fn malformed_radio_messages_are_refused() {
    let malformed = |bytes: &[u8]| {
        assert_eq!(
            radio::Message::decode(bytes),
            Err(CodecError::Malformed),
            "{bytes:?}"
        );
    };
    malformed(&[]);
    malformed(&[0x00, 1, 2, 3]);
    malformed(&[0x05]);
    // A probe with a short or long nonce.
    malformed(&[0x01, 1, 2, 3, 4, 5, 6, 7]);
    malformed(&[0x01, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
    // A reply without `pending`, or with more than a Hub keeps.
    malformed(&[0x02, 1, 2, 3, 4, 5, 6, 7, 8]);
    malformed(&[0x02, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
    malformed(&[0x02, 1, 2, 3, 4, 5, 6, 7, 8, 1, 0]);
    // An empty envelope, or a payload over the ESP-NOW limit.
    malformed(&[0x03]);
    malformed(&[0x04]);
    malformed(&[0x03; 251]);

    let mut small = [0u8; 8];
    let nonce = [0; 8];
    assert_eq!(
        radio::Message::Probe { nonce }.encode(&mut small),
        Err(CodecError::BufferTooSmall)
    );
    assert_eq!(
        radio::Message::Uplink(&[1; 8]).encode(&mut small),
        Err(CodecError::BufferTooSmall)
    );
    let mut buffer = [0u8; 300];
    assert_eq!(
        radio::Message::Uplink(&[]).encode(&mut buffer),
        Err(CodecError::Malformed)
    );
    assert_eq!(
        radio::Message::Downlink(&[0; 250]).encode(&mut buffer),
        Err(CodecError::Malformed)
    );
}
