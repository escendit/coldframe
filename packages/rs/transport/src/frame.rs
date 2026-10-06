//! Building and sealing a `NodeFrame`, opening a `Downlink`, and the Node's Specification set.
//!
//! An uplink is a `NodeFrame` sealed with the `seal/v1` key under a fresh counter, wrapped in a
//! `SealedEnvelope`, after the radio kind byte. A downlink is a `SealedEnvelope` whose payload
//! opens under the `ack/v1` key. Everything goes through `coldframe_crypto::frame`.

use coldframe_crypto::spec::{AEAD_TAG_LENGTH, DEVICE_ID_LENGTH};
use coldframe_crypto::{DeviceId, DeviceKeys, frame};
use coldframe_protocol::device_v1::NodeFrame_::{Measured, Unsynced};
use coldframe_protocol::device_v1::{
    ChargeStatus, Downlink, NodeFrame, Quantity, Reading, SealedEnvelope, Specification,
    SpecificationSet, Unit,
};
use coldframe_protocol::{
    ENVELOPE_CIPHERTEXT_CAPACITY, NODE_FRAME_MAX_SIZE, PROTOCOL_VERSION, SEALED_ENVELOPE_MAX_SIZE,
    SPECIFICATION_SET_MAX_SIZE, decode, encode, encode_node_frame, radio,
};
use coldframe_sensing::{ChargeStatus as Charging, MeasuredAt};
use heapless::Vec;
use sha2::{Digest, Sha256};

use crate::report::Report;

/// Length of a `spec_hash`: SHA-256.
pub const SPEC_HASH_LENGTH: usize = 32;

/// The default low Threshold of soil moisture, in percent.
pub const SOIL_DEFAULT_LOW_PCT: i64 = 30;

/// The default high Threshold of soil moisture, in percent.
pub const SOIL_DEFAULT_HIGH_PCT: i64 = 80;

/// The Node's Specification set (AD-19): one Specification per Sensor, the index being the slot.
/// Soil moisture is calibrated and has default Thresholds; the others are only watched.
#[must_use]
pub fn specification_set() -> SpecificationSet {
    let mut soil = Specification {
        quantity: Quantity::SoilMoisture,
        unit: Unit::RawCount,
        range_min: 0,
        range_max: 4095,
        calibration: true,
        ..Specification::default()
    };
    soil.set_default_low(SOIL_DEFAULT_LOW_PCT);
    soil.set_default_high(SOIL_DEFAULT_HIGH_PCT);
    let watched = |quantity, unit, range_min, range_max| Specification {
        quantity,
        unit,
        range_min,
        range_max,
        ..Specification::default()
    };
    let mut set = SpecificationSet::default();
    // Four Specifications, which is the capacity.
    for specification in [
        soil,
        watched(
            Quantity::AirTemperature,
            Unit::MilliDegreeCelsius,
            -40_000,
            85_000,
        ),
        watched(Quantity::RelativeHumidity, Unit::MilliPercent, 0, 100_000),
        watched(Quantity::GasResistance, Unit::Ohm, 0, 100_000_000),
    ] {
        let _ = set.specifications.push(specification);
    }
    set
}

/// The `spec_hash` every frame carries: SHA-256 of the serialized [`specification_set`].
#[must_use]
pub fn spec_hash() -> [u8; SPEC_HASH_LENGTH] {
    let mut bytes = [0u8; SPECIFICATION_SET_MAX_SIZE];
    // The buffer is the type's largest encoding.
    let length = encode(&specification_set(), &mut bytes).unwrap_or(0);
    Sha256::digest(&bytes[..length]).into()
}

/// The `NodeFrame` of one transmission of `report`: `boot_id` and `uptime_ms` are those of this
/// transmission, everything else is the report. `specifications` attaches the set.
#[must_use]
pub fn node_frame(
    report: &Report,
    boot_id: u64,
    uptime_ms: u64,
    spec_hash: &[u8],
    specifications: Option<SpecificationSet>,
) -> NodeFrame {
    let mut frame = NodeFrame {
        protocol_version: PROTOCOL_VERSION,
        spec_hash: Vec::from_slice(spec_hash).unwrap_or_default(),
        boot_id,
        uptime_ms,
        report_seq: report.report_seq,
        measured: Some(match report.measured_at {
            MeasuredAt::Synced { unix_ms } => {
                Measured::MeasuredAtMs(i64::try_from(unix_ms).unwrap_or(i64::MAX))
            }
            MeasuredAt::Unsynced { boot_id, uptime_ms } => {
                Measured::Unsynced(Unsynced { boot_id, uptime_ms })
            }
        }),
        charging: match report.charging {
            Charging::Charging => ChargeStatus::Charging,
            Charging::NotCharging => ChargeStatus::NotCharging,
            Charging::Unknown => ChargeStatus::Unspecified,
        },
        ..NodeFrame::default()
    };
    for reading in &report.readings {
        // A report has at most four Readings, which is the capacity.
        let _ = frame.readings.push(Reading {
            slot: u32::from(reading.slot),
            reading_seq: reading.seq,
            quantity: Quantity(i32::from(reading.quantity)),
            value: reading.value,
        });
    }
    if let Some(percent) = report.battery_percent {
        frame.set_battery_percent(u32::from(percent));
    }
    if let Some(set) = specifications {
        frame.set_specifications(set);
    }
    frame
}

/// Why a frame was not sealed.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SealError {
    /// The sealed frame does not fit one radio message.
    TooLong,
    /// Encoding or sealing failed.
    Internal,
}

/// Encodes `frame` in field-number order, seals it under `counter` with the `seal/v1` key, and
/// writes the radio message (kind byte, then the `SealedEnvelope`) into `out`; returns its length.
///
/// # Errors
///
/// [`SealError::TooLong`] when the message would exceed one ESP-NOW payload.
pub fn seal_uplink(
    keys: &DeviceKeys,
    counter: u64,
    frame: &NodeFrame,
    out: &mut [u8; radio::MAX_PAYLOAD],
) -> Result<usize, SealError> {
    let mut plaintext = [0u8; NODE_FRAME_MAX_SIZE];
    let length = encode_node_frame(frame, &mut plaintext).map_err(|_| SealError::Internal)?;
    if length + AEAD_TAG_LENGTH > ENVELOPE_CIPHERTEXT_CAPACITY {
        plaintext.fill(0);
        return Err(SealError::TooLong);
    }
    let mut sealed = [0u8; ENVELOPE_CIPHERTEXT_CAPACITY];
    let sealed_length = frame::seal(
        &keys.seal_key,
        &keys.device_id,
        counter,
        &plaintext[..length],
        &mut sealed,
    );
    plaintext.fill(0);
    let sealed_length = sealed_length.map_err(|_| SealError::Internal)?;
    let envelope = SealedEnvelope {
        protocol_version: PROTOCOL_VERSION,
        device_id: Vec::from_slice(keys.device_id.as_bytes()).map_err(|_| SealError::Internal)?,
        counter,
        ciphertext: Vec::from_slice(&sealed[..sealed_length]).map_err(|_| SealError::Internal)?,
    };
    let mut bytes = [0u8; SEALED_ENVELOPE_MAX_SIZE];
    let envelope_length = encode(&envelope, &mut bytes).map_err(|_| SealError::Internal)?;
    if envelope_length > radio::ENVELOPE_MAX {
        return Err(SealError::TooLong);
    }
    radio::Message::Uplink(&bytes[..envelope_length])
        .encode(out)
        .map_err(|_| SealError::Internal)
}

/// Opens a downlink addressed to this Node: the bytes are a `SealedEnvelope` of protocol version
/// 1 with this Node's Device ID whose payload opens under the `ack/v1` key and is a `Downlink` of
/// protocol version 1. Anything else is `None`: a forged or foreign downlink is dropped silently.
#[must_use]
pub fn open_downlink(keys: &DeviceKeys, envelope: &[u8]) -> Option<Downlink> {
    let envelope: SealedEnvelope = decode(envelope).ok()?;
    if envelope.protocol_version != PROTOCOL_VERSION {
        return None;
    }
    let device_id: [u8; DEVICE_ID_LENGTH] = envelope.device_id.as_slice().try_into().ok()?;
    if DeviceId(device_id) != keys.device_id {
        return None;
    }
    let mut plaintext = [0u8; ENVELOPE_CIPHERTEXT_CAPACITY];
    let length = frame::open(
        &keys.ack_key,
        &keys.device_id,
        envelope.counter,
        &envelope.ciphertext,
        &mut plaintext,
    )
    .ok()?;
    let downlink: Downlink = decode(&plaintext[..length]).ok()?;
    (downlink.protocol_version == PROTOCOL_VERSION).then_some(downlink)
}
