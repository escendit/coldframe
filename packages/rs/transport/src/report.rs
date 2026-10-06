//! One buffered wake report and its flash record.
//!
//! A record is one [`RECORD_SIZE`]-byte slot:
//!
//! | Bytes | Content |
//! | --- | --- |
//! | 0 | [`RECORD_MAGIC`] |
//! | 1 | [`RECORD_VERSION`] |
//! | 2 | number of Readings, 0 to 4 |
//! | 3 | flags: bit 0 the time is synced, bit 1 the battery is present |
//! | 4..8 | the *live word*, little endian: written as all ones; an acknowledgement clears bits |
//! | 8..16 | `report_seq` |
//! | 16..24 | Unix milliseconds when synced, otherwise the boot ID |
//! | 24..32 | 0 when synced, otherwise the uptime in milliseconds |
//! | 32 | battery percent |
//! | 33 | charging: 0 unknown, 1 charging, 2 not charging |
//! | 36..108 | four Readings of 18 bytes: slot, quantity, `reading_seq`, value |
//! | 112..116 | the first bytes of SHA-256 over bytes 0..4 and 8..112 |
//!
//! Integers are big endian unless noted; unused bytes are 0, and bytes 116..128 stay erased.
//!
//! The live word is outside the check, so clearing a bit is one plain NOR write of an aligned
//! word that can only clear bits: bit `i` (0 to 3) is Reading `i`, bit 4 the report itself
//! (`report_seq`). A record is acknowledged in full when no bit of its Readings and report is
//! left. A torn first write fails the check and the slot counts as dead; a torn acknowledgement
//! leaves some bits set, and those Readings are sent again under the same `reading_seq`.

use coldframe_protocol::device_v1::Quantity;
use coldframe_sensing::{ChargeStatus, MeasuredAt, ReadingValue, WakeReport};
use heapless::Vec;
use sha2::{Digest, Sha256};

/// Size of one record slot.
pub const RECORD_SIZE: usize = 128;

/// First byte of a record.
pub const RECORD_MAGIC: u8 = 0xCF;

/// Version byte of a record.
pub const RECORD_VERSION: u8 = 1;

/// Readings of one wake report: one per Sensor.
pub const READINGS_MAX: usize = 4;

/// Offset of the live word in a record.
pub(crate) const LIVE_OFFSET: usize = 4;

/// The live bit of the report itself (`report_seq`).
pub(crate) const REPORT_BIT: u8 = 1 << 4;

const FLAG_SYNCED: u8 = 1 << 0;
const FLAG_BATTERY: u8 = 1 << 1;
const READINGS_OFFSET: usize = 36;
const READING_SIZE: usize = 18;
const CHECK_OFFSET: usize = 112;
const CHECK_LENGTH: usize = 4;

/// One Sensor value as the buffer keeps it and a `NodeFrame` carries it.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct BufferedReading {
    /// The Sensor slot.
    pub slot: u8,
    /// The `Quantity` value of `packages/proto`.
    pub quantity: u8,
    /// The `reading_seq`.
    pub seq: u64,
    /// The firmware's raw integer for the quantity.
    pub value: i64,
}

/// One wake report: the payload of one `NodeFrame`. The buffer keeps this, never sealed bytes.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Report {
    /// Identifies the report; above every `reading_seq` of its Readings.
    pub report_seq: u64,
    /// When the Readings were taken.
    pub measured_at: MeasuredAt,
    /// The Readings not yet acknowledged, in slot order.
    pub readings: Vec<BufferedReading, READINGS_MAX>,
    /// The battery charge in percent, when it could be read.
    pub battery_percent: Option<u8>,
    /// The charger status.
    pub charging: ChargeStatus,
}

impl Report {
    /// The report of a wake, or `None` when the wake has no `report_seq` (the `reading_seq`
    /// counter failed): such a report cannot be identified, so it is not buffered.
    #[must_use]
    pub fn from_wake(report: &WakeReport) -> Option<Self> {
        let report_seq = report.report_seq?;
        let mut readings = Vec::new();
        for reading in &report.readings {
            let (quantity, value) = match reading.value {
                ReadingValue::SoilRaw(raw) => (Quantity::SoilMoisture, i64::from(raw)),
                ReadingValue::TemperatureMilliC(value) => {
                    (Quantity::AirTemperature, i64::from(value))
                }
                ReadingValue::HumidityMilliPct(value) => {
                    (Quantity::RelativeHumidity, i64::from(value))
                }
                ReadingValue::GasOhms(value) => (Quantity::GasResistance, i64::from(value)),
            };
            // A wake has at most four Readings, which is the capacity.
            let _ = readings.push(BufferedReading {
                slot: reading.slot,
                quantity: u8::try_from(quantity.0).unwrap_or(0),
                seq: reading.seq,
                value,
            });
        }
        Some(Self {
            report_seq,
            measured_at: report.measured_at,
            readings,
            battery_percent: report.battery.map(|battery| battery.percent),
            charging: report.charging,
        })
    }

    /// The live bits a freshly written record of this report has: one per Reading, and the
    /// report's own.
    pub(crate) fn present_bits(&self) -> u8 {
        present_bits(self.readings.len())
    }

    /// The record of this report, with every live bit set.
    #[must_use]
    pub fn encode(&self) -> [u8; RECORD_SIZE] {
        let mut record = [0u8; RECORD_SIZE];
        record[0] = RECORD_MAGIC;
        record[1] = RECORD_VERSION;
        // At most four Readings.
        record[2] = self.readings.len() as u8;
        let (time_a, time_b) = match self.measured_at {
            MeasuredAt::Synced { unix_ms } => {
                record[3] |= FLAG_SYNCED;
                (unix_ms, 0)
            }
            MeasuredAt::Unsynced { boot_id, uptime_ms } => (boot_id, uptime_ms),
        };
        record[LIVE_OFFSET..LIVE_OFFSET + 4].fill(0xFF);
        record[8..16].copy_from_slice(&self.report_seq.to_be_bytes());
        record[16..24].copy_from_slice(&time_a.to_be_bytes());
        record[24..32].copy_from_slice(&time_b.to_be_bytes());
        if let Some(percent) = self.battery_percent {
            record[3] |= FLAG_BATTERY;
            record[32] = percent;
        }
        record[33] = match self.charging {
            ChargeStatus::Unknown => 0,
            ChargeStatus::Charging => 1,
            ChargeStatus::NotCharging => 2,
        };
        for (reading, bytes) in self
            .readings
            .iter()
            .zip(record[READINGS_OFFSET..].chunks_exact_mut(READING_SIZE))
        {
            bytes[0] = reading.slot;
            bytes[1] = reading.quantity;
            bytes[2..10].copy_from_slice(&reading.seq.to_be_bytes());
            bytes[10..18].copy_from_slice(&reading.value.to_be_bytes());
        }
        let check = check(&record);
        record[CHECK_OFFSET..CHECK_OFFSET + CHECK_LENGTH].copy_from_slice(&check);
        record[CHECK_OFFSET + CHECK_LENGTH..].fill(0xFF);
        record
    }

    /// The report of a valid record with every Reading it was written with, and its live bits;
    /// `None` for anything else (an erased slot, a torn write).
    #[must_use]
    pub fn decode(record: &[u8; RECORD_SIZE]) -> Option<(Self, u8)> {
        if record[0] != RECORD_MAGIC
            || record[1] != RECORD_VERSION
            || usize::from(record[2]) > READINGS_MAX
            || record[CHECK_OFFSET..CHECK_OFFSET + CHECK_LENGTH] != check(record)
        {
            return None;
        }
        let count = usize::from(record[2]);
        let time_a = be_u64(&record[16..24]);
        let time_b = be_u64(&record[24..32]);
        let measured_at = if record[3] & FLAG_SYNCED != 0 {
            MeasuredAt::Synced { unix_ms: time_a }
        } else {
            MeasuredAt::Unsynced {
                boot_id: time_a,
                uptime_ms: time_b,
            }
        };
        let mut readings = Vec::new();
        for bytes in record[READINGS_OFFSET..]
            .chunks_exact(READING_SIZE)
            .take(count)
        {
            let _ = readings.push(BufferedReading {
                slot: bytes[0],
                quantity: bytes[1],
                seq: be_u64(&bytes[2..10]),
                value: be_u64(&bytes[10..18]).cast_signed(),
            });
        }
        let report = Self {
            report_seq: be_u64(&record[8..16]),
            measured_at,
            readings,
            battery_percent: (record[3] & FLAG_BATTERY != 0).then_some(record[32]),
            charging: match record[33] {
                1 => ChargeStatus::Charging,
                2 => ChargeStatus::NotCharging,
                _ => ChargeStatus::Unknown,
            },
        };
        let live = live_bits(record) & present_bits(count);
        Some((report, live))
    }

    /// Keeps only the Readings whose live bit is set in `live`.
    pub(crate) fn retain_live(&mut self, live: u8) {
        let mut index = 0;
        self.readings.retain(|_| {
            let keep = live & (1 << index) != 0;
            index += 1;
            keep
        });
    }
}

/// The live bits of a record with `count` Readings.
pub(crate) fn present_bits(count: usize) -> u8 {
    ((1u8 << count.min(READINGS_MAX)) - 1) | REPORT_BIT
}

/// The low byte of the live word.
pub(crate) fn live_bits(record: &[u8; RECORD_SIZE]) -> u8 {
    record[LIVE_OFFSET]
}

fn be_u64(bytes: &[u8]) -> u64 {
    let mut value = [0u8; 8];
    value.copy_from_slice(bytes);
    u64::from_be_bytes(value)
}

/// SHA-256 over the record without its live word, cut to the check length.
fn check(record: &[u8; RECORD_SIZE]) -> [u8; CHECK_LENGTH] {
    let mut hasher = Sha256::new();
    hasher.update(&record[..LIVE_OFFSET]);
    hasher.update(&record[LIVE_OFFSET + 4..CHECK_OFFSET]);
    let digest = hasher.finalize();
    let mut check = [0u8; CHECK_LENGTH];
    check.copy_from_slice(&digest[..CHECK_LENGTH]);
    check
}
