//! Shared helpers: a blocking executor, flash partitions that insist on plain NOR writes and can
//! lose power at a chosen write, a scripted Hub with its Server, and a Node that wakes.

#![allow(dead_code, reason = "each test file uses a different part")]

use std::cell::RefCell;
use std::future::Future;
use std::pin::pin;
use std::rc::Rc;
use std::task::{Context, Poll, Waker};

use coldframe_crypto::{DeviceId, DeviceKeys, frame};
use coldframe_hal::mock::{
    MockDatagramRadio, MockFlash, MockRadio, MockRtc, MockTrng, SentDatagram,
};
use coldframe_hal::{DatagramError, Flash, FlashError, MacAddress, Radio, Rtc};
use coldframe_protocol::device_v1::{Downlink, NodeFrame, ReadingSeqRange, SealedEnvelope};
use coldframe_protocol::radio::Message;
use coldframe_protocol::{decode, encode};
use coldframe_sensing::{ChargeStatus, MeasuredAt};
use coldframe_transport::{
    BufferedReading, LinkState, Outcome, Partition, Partitions, Report, begin, buffer_only, run,
};
use serde_json::Value;

/// Runs a future that never waits on anything external to completion.
pub fn block_on<F: Future>(future: F) -> F::Output {
    let mut future = pin!(future);
    let mut context = Context::from_waker(Waker::noop());
    loop {
        if let Poll::Ready(output) = future.as_mut().poll(&mut context) {
            return output;
        }
    }
}

/// The root key of the crypto-spec frame vectors.
pub const ROOT: [u8; 32] = [
    0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25,
    26, 27, 28, 29, 30, 31,
];

/// The keys of the vector Node.
pub fn keys() -> DeviceKeys {
    DeviceKeys::from_root_key(&ROOT)
}

/// The keys of some other Device.
pub fn other_keys() -> DeviceKeys {
    DeviceKeys::from_root_key(&[0x77; 32])
}

/// A vector of `packages/crypto-spec/vectors.json` `frames`, by name.
pub fn frame_vector(name: &str) -> Value {
    let path = concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../../packages/crypto-spec/vectors.json"
    );
    let text = std::fs::read_to_string(path).expect("vectors.json is readable");
    let all: Value = serde_json::from_str(&text).expect("vectors.json is JSON");
    all["frames"]
        .as_array()
        .expect("frames")
        .iter()
        .find(|case| case["name"] == name)
        .unwrap_or_else(|| panic!("vector {name}"))
        .clone()
}

/// A lowercase hex field as bytes.
pub fn hex(value: &Value, field: &str) -> Vec<u8> {
    let text = value[field].as_str().expect("hex string");
    (0..text.len())
        .step_by(2)
        .map(|i| u8::from_str_radix(&text[i..i + 2], 16).expect("hex digits"))
        .collect()
}

// ---------------------------------------------------------------------------------------------
// Flash

/// Sizes of the Node's transport partitions, as in `apps/rs/node/partitions.csv`.
pub const FRAME_SIZE: usize = 0x2000;
pub const LINK_SIZE: usize = 0x2000;
pub const BUFFER_SIZE: usize = 0x8000;

/// When the power fails: after this many more writes and erases, counted over every partition.
#[derive(Clone, Copy, Debug, Default)]
pub struct Power {
    /// Writes and erases left before the cut; `None` never cuts.
    pub left: Option<usize>,
    /// The power is off: every operation fails.
    pub off: bool,
    /// Writes and erases so far.
    pub operations: usize,
}

impl Power {
    /// Counts one write or erase; returns whether it is the one the power fails in.
    fn spend(&mut self) -> bool {
        self.operations += 1;
        match self.left.as_mut() {
            Some(0) => {
                self.off = true;
                true
            }
            Some(left) => {
                *left -= 1;
                false
            }
            None => false,
        }
    }
}

/// One partition, as the Node's flash adapter gives it: a write must be word-aligned (so it is a
/// plain NOR program, never a read-erase-rewrite of the sector) and may only clear bits.
pub struct Nor<'a> {
    flash: &'a mut MockFlash,
    power: &'a mut Power,
}

impl Flash for Nor<'_> {
    fn capacity(&self) -> usize {
        self.flash.capacity()
    }

    fn read(&mut self, offset: u32, buffer: &mut [u8]) -> Result<(), FlashError> {
        if self.power.off {
            return Err(FlashError::Storage);
        }
        self.flash.read(offset, buffer)
    }

    fn write(&mut self, offset: u32, data: &[u8]) -> Result<(), FlashError> {
        if self.power.off {
            return Err(FlashError::Storage);
        }
        assert!(
            offset.is_multiple_of(4) && data.len().is_multiple_of(4),
            "a write at {offset} of {} bytes is not a plain NOR program",
            data.len()
        );
        let start = offset as usize;
        let before = &self.flash.contents()[start..start + data.len()];
        assert!(
            before.iter().zip(data).all(|(old, new)| old & new == *new),
            "a write at {offset} would set bits: it needs an erase"
        );
        if self.power.spend() {
            // The cut tears the write: the first half of it reached the chip.
            let torn = data.len() / 2;
            self.flash.write(offset, &data[..torn])?;
            return Err(FlashError::Storage);
        }
        self.flash.write(offset, data)
    }

    fn erase(&mut self, offset: u32, length: u32) -> Result<(), FlashError> {
        if self.power.off {
            return Err(FlashError::Storage);
        }
        if self.power.spend() {
            // The cut tears the erase: the first half of the range is erased.
            let start = offset as usize;
            let half = length as usize / 2;
            self.flash.contents_mut()[start..start + half].fill(0xFF);
            return Err(FlashError::Storage);
        }
        self.flash.erase(offset, length)
    }
}

/// The three transport partitions of one Node.
pub struct Flashes {
    pub frame: MockFlash,
    pub buffer: MockFlash,
    pub link: MockFlash,
    pub power: Power,
    /// A partition that cannot be opened.
    pub missing: Option<Partition>,
}

impl Flashes {
    pub fn new() -> Self {
        Self {
            frame: MockFlash::new(FRAME_SIZE),
            buffer: MockFlash::new(BUFFER_SIZE),
            link: MockFlash::new(LINK_SIZE),
            power: Power::default(),
            missing: None,
        }
    }

    /// The power comes back: the flash keeps what reached it.
    pub fn restore_power(&mut self) {
        self.power = Power::default();
    }

    /// The buffer partition alone.
    pub fn buffer(&mut self) -> Nor<'_> {
        Nor {
            flash: &mut self.buffer,
            power: &mut self.power,
        }
    }

    /// The link partition alone.
    pub fn link(&mut self) -> Nor<'_> {
        Nor {
            flash: &mut self.link,
            power: &mut self.power,
        }
    }
}

impl Partitions for Flashes {
    type Flash<'a> = Nor<'a>;

    fn open(&mut self, partition: Partition) -> Option<Nor<'_>> {
        if self.missing == Some(partition) {
            return None;
        }
        let flash = match partition {
            Partition::Frame => &mut self.frame,
            Partition::Buffer => &mut self.buffer,
            Partition::Link => &mut self.link,
        };
        Some(Nor {
            flash,
            power: &mut self.power,
        })
    }
}

// ---------------------------------------------------------------------------------------------
// The Hub and its Server

/// The Hub's station MAC.
pub const HUB_MAC: MacAddress = [0x02, 0xC0, 0x1D, 0, 0, 0x01];

/// When the Hub hands a downlink to the Node.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Ack {
    /// Inside the acknowledgement window, right after the frame.
    InWindow,
    /// Too late for the window: kept by the Hub and offered once at the next probe.
    Late,
    /// Never: the frame is lost on its way to the Server.
    Lost,
}

/// One frame the Server opened.
#[derive(Clone, Debug)]
pub struct Uplink {
    /// The envelope's counter.
    pub counter: u64,
    /// The channel it came in on.
    pub channel: u8,
    /// The opened frame.
    pub frame: NodeFrame,
    /// The length of the radio message that carried it.
    pub message_len: usize,
}

/// A Hub on one channel with the Server behind it.
pub struct World {
    /// The Hub's channel; `None` while the Hub is off.
    pub channel: Option<u8>,
    /// When downlinks reach the Node.
    pub ack: Ack,
    /// The downlinks the Hub keeps for the Node, oldest first, as radio messages: at most 8,
    /// offered once at the next probe and then dropped.
    pub kept: Vec<Vec<u8>>,
    /// Every frame the Server opened, in order.
    pub uplinks: Vec<Uplink>,
    /// The Server clock put into the next downlink.
    pub server_time_ms: i64,
    /// The `spec_hash` of the last Specification set the Server accepted.
    pub known_spec_hash: Option<Vec<u8>>,
    /// The Specification sets the Server received.
    pub specification_sets: usize,
    /// The counter of the next downlink.
    pub next_downlink: u64,
    /// Probes heard on the Hub's channel.
    pub probes: usize,
    /// Datagrams queued for the Node after its next send, whatever that send is.
    pub inject: Vec<(MacAddress, Vec<u8>)>,
}

impl World {
    pub fn new(channel: u8) -> Self {
        Self {
            channel: Some(channel),
            ack: Ack::InWindow,
            kept: Vec::new(),
            uplinks: Vec::new(),
            server_time_ms: 1_790_000_000_000,
            known_spec_hash: None,
            specification_sets: 0,
            next_downlink: 1,
            probes: 0,
            inject: Vec::new(),
        }
    }

    /// The reading_seq values of every frame the Server opened, in order.
    pub fn report_seqs(&self) -> Vec<u64> {
        self.uplinks
            .iter()
            .map(|uplink| uplink.frame.report_seq)
            .collect()
    }

    /// What the Hub and the Server do with one datagram of the Node.
    fn hear(&mut self, sent: &SentDatagram) -> Vec<(MacAddress, Vec<u8>)> {
        let mut answers = std::mem::take(&mut self.inject);
        if self.channel != Some(sent.channel) {
            return answers;
        }
        match Message::decode(&sent.payload) {
            Ok(Message::Probe { nonce }) => {
                self.probes += 1;
                let kept = std::mem::take(&mut self.kept);
                let pending = u8::try_from(kept.len()).unwrap();
                let mut reply = [0u8; 16];
                let length = Message::ProbeReply { nonce, pending }
                    .encode(&mut reply)
                    .unwrap();
                answers.push((HUB_MAC, reply[..length].to_vec()));
                answers.extend(kept.into_iter().map(|downlink| (HUB_MAC, downlink)));
            }
            Ok(Message::Uplink(envelope)) if sent.to == HUB_MAC && self.ack != Ack::Lost => {
                let downlink = self.ingest(envelope, sent);
                // The Hub sends it when it arrives and keeps it for the next probe.
                if self.kept.len() == 8 {
                    self.kept.remove(0);
                }
                self.kept.push(downlink.clone());
                if self.ack == Ack::InWindow {
                    answers.push((HUB_MAC, downlink));
                }
            }
            _ => {}
        }
        answers
    }

    /// The Server: opens the frame, stores it, and seals the downlink that acknowledges it
    /// (returned as the radio message that carries it).
    pub fn ingest(&mut self, envelope: &[u8], sent: &SentDatagram) -> Vec<u8> {
        let keys = keys();
        let envelope: SealedEnvelope = decode(envelope).expect("an envelope");
        assert_eq!(envelope.protocol_version, 1);
        assert_eq!(envelope.device_id.as_slice(), keys.device_id.as_bytes());
        let mut plaintext = vec![0u8; envelope.ciphertext.len()];
        let length = frame::open(
            &keys.seal_key,
            &keys.device_id,
            envelope.counter,
            &envelope.ciphertext,
            &mut plaintext,
        )
        .expect("the frame opens under seal/v1");
        let node_frame: NodeFrame = decode(&plaintext[..length]).expect("a NodeFrame");
        if node_frame.specifications().is_some() {
            self.specification_sets += 1;
            self.known_spec_hash = Some(node_frame.spec_hash.to_vec());
        }
        let mut seqs: Vec<u64> = node_frame
            .readings
            .iter()
            .map(|reading| reading.reading_seq)
            .collect();
        seqs.push(node_frame.report_seq);
        seqs.sort_unstable();
        let mut downlink = Downlink {
            protocol_version: 1,
            acked_counter: envelope.counter,
            server_time_ms: self.server_time_ms,
            specifications_unknown: self.known_spec_hash.as_deref()
                != Some(node_frame.spec_hash.as_slice()),
            ..Downlink::default()
        };
        for seq in seqs {
            match downlink.acked_readings.last_mut() {
                Some(range) if range.last + 1 == seq => range.last = seq,
                _ => downlink
                    .acked_readings
                    .push(ReadingSeqRange {
                        first: seq,
                        last: seq,
                    })
                    .expect("the ranges fit"),
            }
        }
        self.uplinks.push(Uplink {
            counter: envelope.counter,
            channel: sent.channel,
            frame: node_frame,
            message_len: sent.payload.len(),
        });
        let counter = self.next_downlink;
        self.next_downlink += 1;
        downlink_message(&keys, keys.device_id, 1, counter, &downlink)
    }
}

/// A `Downlink` sealed with the `ack/v1` key of `keys` into an envelope that names `device_id`
/// and `envelope_version`, as the radio message that carries it.
pub fn downlink_message(
    keys: &DeviceKeys,
    device_id: DeviceId,
    envelope_version: u32,
    counter: u64,
    downlink: &Downlink,
) -> Vec<u8> {
    let mut plaintext = [0u8; 256];
    let length = encode(downlink, &mut plaintext).unwrap();
    let mut sealed = [0u8; 256];
    let sealed_length = frame::seal(
        &keys.ack_key,
        &device_id,
        counter,
        &plaintext[..length],
        &mut sealed,
    )
    .unwrap();
    let envelope = SealedEnvelope {
        protocol_version: envelope_version,
        device_id: heapless::Vec::from_slice(device_id.as_bytes()).unwrap(),
        counter,
        ciphertext: heapless::Vec::from_slice(&sealed[..sealed_length]).unwrap(),
    };
    let mut bytes = [0u8; 256];
    let envelope_length = encode(&envelope, &mut bytes).unwrap();
    let mut message = [0u8; 250];
    let message_length = Message::Downlink(&bytes[..envelope_length])
        .encode(&mut message)
        .unwrap();
    message[..message_length].to_vec()
}

/// A downlink that acknowledges `acked_counter` and the reading_seq values `first..=last`.
pub fn downlink(acked_counter: u64, server_time_ms: i64, first: u64, last: u64) -> Downlink {
    let mut downlink = Downlink {
        protocol_version: 1,
        acked_counter,
        server_time_ms,
        ..Downlink::default()
    };
    downlink
        .acked_readings
        .push(ReadingSeqRange { first, last })
        .unwrap();
    downlink
}

// ---------------------------------------------------------------------------------------------
// The Node

/// The time between two wakes.
pub const PERIOD_MS: u64 = 900_000;

/// What one wake did.
pub struct Wake {
    /// The transport's outcome.
    pub outcome: Outcome,
    /// Every datagram the Node sent.
    pub sent: Vec<SentDatagram>,
    /// Every channel the Node tuned to, in order.
    pub channels: Vec<u8>,
    /// The timeouts of the receives that heard nothing.
    pub timeouts: Vec<u32>,
    /// The link state after the wake.
    pub link: LinkState,
    /// The wall clock at the end of the wake.
    pub unix_ms: Option<u64>,
    /// The report the wake measured.
    pub report: Report,
}

impl Wake {
    /// The uplinks among the datagrams sent.
    pub fn uplinks(&self) -> Vec<&SentDatagram> {
        self.sent
            .iter()
            .filter(|sent| sent.payload.first() == Some(&0x03))
            .collect()
    }

    /// The probes among the datagrams sent.
    pub fn probes(&self) -> Vec<&SentDatagram> {
        self.sent
            .iter()
            .filter(|sent| sent.payload.first() == Some(&0x01))
            .collect()
    }
}

/// A Node with its flash, and the Hub it talks to.
pub struct Node {
    pub flashes: Flashes,
    pub world: Rc<RefCell<World>>,
    pub keys: DeviceKeys,
    pub boot_id: u64,
    /// The RTC uptime at the start of the next wake.
    pub uptime_ms: u64,
    /// The next `reading_seq`.
    pub next_seq: u64,
    /// What the radio reports for every send, whatever arrives.
    pub send_report: Result<(), DatagramError>,
    /// How long the wake's clock runs ahead between the measurement and the transport.
    pub drift_ms: u64,
}

impl Node {
    /// A fresh Node next to a Hub on `channel`.
    pub fn new(channel: u8) -> Self {
        Self {
            flashes: Flashes::new(),
            world: Rc::new(RefCell::new(World::new(channel))),
            keys: keys(),
            boot_id: 1,
            uptime_ms: 5_000,
            next_seq: 100,
            send_report: Ok(()),
            drift_ms: 0,
        }
    }

    /// A power cycle: a new boot ID, and the uptime starts again.
    pub fn reboot(&mut self) {
        self.boot_id += 1;
        self.uptime_ms = 1_000;
    }

    /// A wake report with four Readings, stamped by `rtc`.
    pub fn measure(&mut self, rtc: &MockRtc) -> Report {
        let first = self.next_seq;
        self.next_seq += 5;
        let measured_at = match rtc.unix_time_millis() {
            Some(unix_ms) => MeasuredAt::Synced { unix_ms },
            None => MeasuredAt::Unsynced {
                boot_id: self.boot_id,
                uptime_ms: rtc.uptime_millis(),
            },
        };
        report(first, measured_at)
    }

    fn radio(&self) -> MockDatagramRadio {
        let mut radio = MockDatagramRadio::new();
        let world = Rc::clone(&self.world);
        radio.set_peer(move |sent| world.borrow_mut().hear(sent));
        radio.report_sends_as(self.send_report);
        radio
    }

    /// One wake: a reboot out of deep sleep, the measurement, the transport, deep sleep again.
    pub fn wake(&mut self) -> Wake {
        self.wake_with(|_| {})
    }

    /// One wake whose clock a test may touch between the measurement and the transport.
    pub fn wake_with(&mut self, between: impl FnOnce(&mut MockRtc)) -> Wake {
        // The wall clock does not outlive a wake: `begin` gives it back.
        let mut rtc = MockRtc::new();
        rtc.advance(self.uptime_ms);
        let (mut link, fault) = begin(&mut self.flashes, &mut rtc, self.boot_id);
        assert_eq!(fault, None);
        let report = self.measure(&rtc);
        between(&mut rtc);
        let mut radio = self.radio();
        let mut trng = trng();
        let outcome = block_on(run(
            &mut self.flashes,
            &mut radio,
            &mut rtc,
            &mut trng,
            &self.keys,
            self.boot_id,
            &mut link,
            Some(&report),
        ));
        self.uptime_ms += PERIOD_MS;
        Wake {
            outcome,
            sent: radio.sent().to_vec(),
            channels: radio.channels().to_vec(),
            timeouts: radio.timeouts().to_vec(),
            link,
            unix_ms: rtc.unix_time_millis(),
            report,
        }
    }

    /// A wake that entered setup mode: the report is buffered, the radio stays off.
    pub fn setup_wake(&mut self) -> Outcome {
        let mut rtc = MockRtc::new();
        rtc.advance(self.uptime_ms);
        let report = self.measure(&rtc);
        self.uptime_ms += PERIOD_MS;
        buffer_only(&mut self.flashes, Some(&report))
    }

    /// The `report_seq` of every buffered report, oldest first.
    pub fn buffered(&mut self) -> Vec<u64> {
        buffered(&mut self.flashes)
    }
}

/// The `report_seq` of every buffered report, oldest first.
pub fn buffered(flashes: &mut Flashes) -> Vec<u64> {
    let mut flash = flashes.buffer();
    let buffer = coldframe_transport::ReportBuffer::open(&mut flash).expect("the buffer opens");
    let mut seqs = Vec::new();
    let mut after = None;
    while let Some(report) = buffer.next(&mut flash, after).expect("the buffer reads") {
        after = Some(report.report_seq);
        seqs.push(report.report_seq);
    }
    assert_eq!(seqs.len(), buffer.len());
    seqs
}

/// A TRNG whose radio is on.
pub fn trng() -> MockTrng {
    let mut radio = MockRadio::new();
    radio.enable().unwrap();
    MockTrng::new(&radio)
}

/// A report whose Readings take `first..first + 4` and whose `report_seq` is `first + 4`.
pub fn report(first: u64, measured_at: MeasuredAt) -> Report {
    let values = [(1u8, 1873i64), (2, -2500), (3, 64_250), (4, 48_211)];
    let mut readings = heapless::Vec::new();
    for (slot, (quantity, value)) in (0u8..).zip(values) {
        readings
            .push(BufferedReading {
                slot,
                quantity,
                seq: first + u64::from(slot),
                value,
            })
            .unwrap();
    }
    Report {
        report_seq: first + 4,
        measured_at,
        readings,
        battery_percent: Some(87),
        charging: ChargeStatus::Charging,
    }
}
