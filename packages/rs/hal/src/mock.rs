//! Mock implementations of every trait, for host-side tests (feature `mock`, std only).
//!
//! The mocks model the hardware contracts the firmware relies on, not just return values:
//!
//! - [`MockEfuse`] burns once per block and hides a burned key's data.
//! - [`MockHmac`] shares the eFuse state and computes a real HMAC-SHA256 over the block's key.
//! - [`MockTrng`] refuses to produce bytes while its [`MockRadio`] is off.
//! - [`MockFlash`] has NOR semantics: erase to `0xFF`, writes only clear bits.
//! - [`MockSetupLink`] plays scripted connections and can answer each send through a peer
//!   callback, so a test drives a whole BLE session.
//! - [`MockWifi`] returns a scripted scan and a join outcome per SSID and password.
//!
//! Each mock can inject a failure so tests reach every error path.

use std::cell::{Cell, RefCell};
use std::collections::VecDeque;
use std::rc::Rc;
use std::string::String;
use std::vec::Vec;

use hmac::{Hmac, KeyInit, Mac};
use sha2::Sha256;

use crate::adc::{Adc, AdcError};
use crate::ble::{LinkError, SetupLink};
use crate::efuse::{Efuse, EfuseError, KEY_LENGTH, KeyBlock, KeyPurpose};
use crate::flash::{Flash, FlashError};
use crate::gpio::{GpioError, InputPin, OutputPin};
use crate::hmac::{HMAC_LENGTH, HmacError, HmacPeripheral};
use crate::radio::{Radio, RadioError};
use crate::rng::{Trng, TrngError};
use crate::rtc::{Rtc, RtcError};
use crate::wifi::{AccessPoint, JoinError, Wifi, WifiError};

// ---------------------------------------------------------------------------------------------
// eFuse

/// How [`MockEfuse::burn_key`] behaves.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum BurnBehaviour {
    /// Burns like the chip: data, purpose, write protection and, for a protected purpose, read
    /// protection.
    Normal,
    /// Fails with the given error and changes nothing.
    Fail(EfuseError),
    /// Reports success but changes nothing, as if the programming never reached the fuses.
    SilentlyIgnored,
    /// Burns data and purpose but leaves the block readable.
    WithoutReadProtection,
}

#[derive(Clone, Copy)]
struct KeyBlockState {
    data: [u8; KEY_LENGTH],
    purpose: KeyPurpose,
    read_protected: bool,
    write_protected: bool,
}

impl KeyBlockState {
    const UNUSED: Self = Self {
        data: [0; KEY_LENGTH],
        purpose: KeyPurpose::User,
        read_protected: false,
        write_protected: false,
    };

    fn is_unused(&self) -> bool {
        self.purpose == KeyPurpose::User
            && self.data == [0; KEY_LENGTH]
            && !self.read_protected
            && !self.write_protected
    }
}

struct EfuseState {
    blocks: [KeyBlockState; 6],
    burns: usize,
    behaviour: BurnBehaviour,
}

/// Six eFuse key blocks with data, purpose, read and write protection, plus a burn counter.
///
/// Clones share the same fuses, as do the [`MockHmac`]s made from it.
#[derive(Clone)]
pub struct MockEfuse {
    state: Rc<RefCell<EfuseState>>,
}

impl Default for MockEfuse {
    fn default() -> Self {
        Self::new()
    }
}

impl MockEfuse {
    /// A factory-fresh chip: every key block unused.
    #[must_use]
    pub fn new() -> Self {
        Self {
            state: Rc::new(RefCell::new(EfuseState {
                blocks: [KeyBlockState::UNUSED; 6],
                burns: 0,
                behaviour: BurnBehaviour::Normal,
            })),
        }
    }

    /// A chip whose `block` already holds `root` as a read-protected HMAC upstream key: a Device
    /// that has booted before. Does not count as a burn.
    #[must_use]
    pub fn with_identity(block: KeyBlock, root: [u8; KEY_LENGTH]) -> Self {
        let efuse = Self::new();
        efuse.set_block(block, root, KeyPurpose::HmacUp, true, true);
        efuse
    }

    /// Sets `block` directly, as a test fixture. Does not count as a burn.
    pub fn set_block(
        &self,
        block: KeyBlock,
        data: [u8; KEY_LENGTH],
        purpose: KeyPurpose,
        read_protected: bool,
        write_protected: bool,
    ) {
        self.state.borrow_mut().blocks[usize::from(block.index())] = KeyBlockState {
            data,
            purpose,
            read_protected,
            write_protected,
        };
    }

    /// Changes how later burns behave.
    pub fn set_burn_behaviour(&self, behaviour: BurnBehaviour) {
        self.state.borrow_mut().behaviour = behaviour;
    }

    /// How many times [`Efuse::burn_key`] changed the fuses.
    #[must_use]
    pub fn burn_count(&self) -> usize {
        self.state.borrow().burns
    }

    /// The data of `block` as software could read it: `None` once it is read-protected.
    #[must_use]
    pub fn read_key(&self, block: KeyBlock) -> Option<[u8; KEY_LENGTH]> {
        let state = self.state.borrow();
        let slot = &state.blocks[usize::from(block.index())];
        (!slot.read_protected).then_some(slot.data)
    }

    /// Whether `block` is write-protected.
    #[must_use]
    pub fn is_write_protected(&self, block: KeyBlock) -> bool {
        self.state.borrow().blocks[usize::from(block.index())].write_protected
    }
}

impl Efuse for MockEfuse {
    fn key_purpose(&self, block: KeyBlock) -> KeyPurpose {
        self.state.borrow().blocks[usize::from(block.index())].purpose
    }

    fn is_read_protected(&self, block: KeyBlock) -> bool {
        self.state.borrow().blocks[usize::from(block.index())].read_protected
    }

    fn is_unused(&self, block: KeyBlock) -> bool {
        self.state.borrow().blocks[usize::from(block.index())].is_unused()
    }

    fn burn_key(
        &mut self,
        block: KeyBlock,
        key: &[u8; KEY_LENGTH],
        purpose: KeyPurpose,
    ) -> Result<(), EfuseError> {
        let mut state = self.state.borrow_mut();
        if !state.blocks[usize::from(block.index())].is_unused() {
            return Err(EfuseError::BlockInUse);
        }
        let read_protected = match state.behaviour {
            BurnBehaviour::Fail(error) => return Err(error),
            BurnBehaviour::SilentlyIgnored => return Ok(()),
            BurnBehaviour::Normal => purpose.is_read_protected_on_burn(),
            BurnBehaviour::WithoutReadProtection => false,
        };
        state.blocks[usize::from(block.index())] = KeyBlockState {
            data: *key,
            purpose,
            read_protected,
            write_protected: true,
        };
        state.burns += 1;
        Ok(())
    }
}

/// The HMAC peripheral over the fuses of a [`MockEfuse`].
pub struct MockHmac {
    efuse: Rc<RefCell<EfuseState>>,
    failure: Option<HmacError>,
    calls: usize,
}

impl MockHmac {
    /// An HMAC peripheral keyed by the blocks of `efuse`.
    #[must_use]
    pub fn new(efuse: &MockEfuse) -> Self {
        Self {
            efuse: Rc::clone(&efuse.state),
            failure: None,
            calls: 0,
        }
    }

    /// Makes every later call fail with `error`, or succeed again with `None`.
    pub fn fail_with(&mut self, error: Option<HmacError>) {
        self.failure = error;
    }

    /// How many times [`HmacPeripheral::hmac_sha256`] was called.
    #[must_use]
    pub fn call_count(&self) -> usize {
        self.calls
    }
}

impl HmacPeripheral for MockHmac {
    fn hmac_sha256(&mut self, block: KeyBlock, msg: &[u8]) -> Result<[u8; HMAC_LENGTH], HmacError> {
        self.calls += 1;
        if let Some(error) = self.failure {
            return Err(error);
        }
        let state = self.efuse.borrow();
        let slot = &state.blocks[usize::from(block.index())];
        if slot.purpose != KeyPurpose::HmacUp {
            return Err(HmacError::KeyPurposeMismatch);
        }
        let mut mac = <Hmac<Sha256> as KeyInit>::new_from_slice(&slot.data)
            .expect("HMAC accepts a key of any length");
        mac.update(msg);
        Ok(mac.finalize().into_bytes().into())
    }
}

// ---------------------------------------------------------------------------------------------
// Radio and TRNG

/// A radio that is off until [`Radio::enable`]. Clones share the same state.
#[derive(Clone, Default)]
pub struct MockRadio {
    enabled: Rc<Cell<bool>>,
    failure: Option<RadioError>,
}

impl MockRadio {
    /// A radio that is off.
    #[must_use]
    pub fn new() -> Self {
        Self::default()
    }

    /// Makes every later [`Radio::enable`] fail with `error`, or succeed again with `None`.
    pub fn fail_with(&mut self, error: Option<RadioError>) {
        self.failure = error;
    }
}

impl Radio for MockRadio {
    fn enable(&mut self) -> Result<(), RadioError> {
        if let Some(error) = self.failure {
            return Err(error);
        }
        self.enabled.set(true);
        Ok(())
    }

    fn is_enabled(&self) -> bool {
        self.enabled.get()
    }
}

/// A TRNG whose entropy source is a [`MockRadio`]: it fails while the radio is off.
///
/// Its bytes come first from a script, then from a deterministic generator.
pub struct MockTrng {
    radio: Rc<Cell<bool>>,
    script: VecDeque<u8>,
    state: u64,
    failure: Option<TrngError>,
}

impl MockTrng {
    /// A TRNG fed by `radio`.
    #[must_use]
    pub fn new(radio: &MockRadio) -> Self {
        Self {
            radio: Rc::clone(&radio.enabled),
            script: VecDeque::new(),
            state: 0x9E37_79B9_7F4A_7C15,
            failure: None,
        }
    }

    /// A TRNG fed by `radio` that hands out `bytes` first.
    #[must_use]
    pub fn with_bytes(radio: &MockRadio, bytes: &[u8]) -> Self {
        let mut trng = Self::new(radio);
        trng.script.extend(bytes);
        trng
    }

    /// Makes every later call fail with `error`, or succeed again with `None`.
    pub fn fail_with(&mut self, error: Option<TrngError>) {
        self.failure = error;
    }

    fn next_generated(&mut self) -> u8 {
        // xorshift64*: deterministic filler once the script is spent. Not random, and never used
        // outside tests.
        self.state ^= self.state >> 12;
        self.state ^= self.state << 25;
        self.state ^= self.state >> 27;
        self.state.wrapping_mul(0x2545_F491_4F6C_DD1D).to_be_bytes()[0]
    }
}

impl Trng for MockTrng {
    fn fill(&mut self, buffer: &mut [u8]) -> Result<(), TrngError> {
        if !self.radio.get() {
            return Err(TrngError::EntropySourceDisabled);
        }
        if let Some(error) = self.failure {
            return Err(error);
        }
        for byte in buffer {
            *byte = match self.script.pop_front() {
                Some(scripted) => scripted,
                None => self.next_generated(),
            };
        }
        Ok(())
    }
}

// ---------------------------------------------------------------------------------------------
// Flash

/// Sector size of [`MockFlash`], as on the ESP32-S3.
pub const MOCK_SECTOR_SIZE: u32 = 4096;

/// A flash region with NOR semantics, and write and erase counters.
pub struct MockFlash {
    bytes: Vec<u8>,
    writes: usize,
    erases: usize,
    failure: Option<FlashError>,
    writes_ignored: bool,
}

impl MockFlash {
    /// An erased region of `capacity` bytes, a multiple of [`MOCK_SECTOR_SIZE`].
    #[must_use]
    pub fn new(capacity: usize) -> Self {
        assert!(
            capacity.is_multiple_of(MOCK_SECTOR_SIZE as usize),
            "capacity is whole sectors"
        );
        Self {
            bytes: vec![0xFF; capacity],
            writes: 0,
            erases: 0,
            failure: None,
            writes_ignored: false,
        }
    }

    /// The raw contents, as a test fixture. Changing them counts as no write.
    pub fn contents_mut(&mut self) -> &mut [u8] {
        &mut self.bytes
    }

    /// The raw contents.
    #[must_use]
    pub fn contents(&self) -> &[u8] {
        &self.bytes
    }

    /// How many times [`Flash::write`] returned success.
    #[must_use]
    pub fn write_count(&self) -> usize {
        self.writes
    }

    /// How many times [`Flash::erase`] succeeded.
    #[must_use]
    pub fn erase_count(&self) -> usize {
        self.erases
    }

    /// Makes every later operation fail with `error`, or succeed again with `None`.
    pub fn fail_with(&mut self, error: Option<FlashError>) {
        self.failure = error;
    }

    /// Makes later writes report success but change nothing, as if they never reached the chip.
    pub fn ignore_writes(&mut self, ignored: bool) {
        self.writes_ignored = ignored;
    }

    fn range(&self, offset: u32, length: usize) -> Result<core::ops::Range<usize>, FlashError> {
        if let Some(error) = self.failure {
            return Err(error);
        }
        let start = usize::try_from(offset).map_err(|_| FlashError::OutOfBounds)?;
        let end = start.checked_add(length).ok_or(FlashError::OutOfBounds)?;
        if end > self.bytes.len() {
            return Err(FlashError::OutOfBounds);
        }
        Ok(start..end)
    }
}

impl Flash for MockFlash {
    fn capacity(&self) -> usize {
        self.bytes.len()
    }

    fn read(&mut self, offset: u32, buffer: &mut [u8]) -> Result<(), FlashError> {
        let range = self.range(offset, buffer.len())?;
        buffer.copy_from_slice(&self.bytes[range]);
        Ok(())
    }

    fn write(&mut self, offset: u32, data: &[u8]) -> Result<(), FlashError> {
        let range = self.range(offset, data.len())?;
        self.writes += 1;
        if self.writes_ignored {
            return Ok(());
        }
        for (cell, byte) in self.bytes[range].iter_mut().zip(data) {
            // NOR: programming can only clear bits.
            *cell &= *byte;
        }
        Ok(())
    }

    fn erase(&mut self, offset: u32, length: u32) -> Result<(), FlashError> {
        if !offset.is_multiple_of(MOCK_SECTOR_SIZE) || !length.is_multiple_of(MOCK_SECTOR_SIZE) {
            return Err(FlashError::Unaligned);
        }
        let length = usize::try_from(length).map_err(|_| FlashError::OutOfBounds)?;
        let range = self.range(offset, length)?;
        self.bytes[range].fill(0xFF);
        self.erases += 1;
        Ok(())
    }
}

// ---------------------------------------------------------------------------------------------
// ADC, RTC, GPIO

/// An ADC input that reads a set value.
#[derive(Clone, Copy, Debug)]
pub struct MockAdc {
    /// The next reading, or the error to return.
    pub reading: Result<u16, AdcError>,
}

impl MockAdc {
    /// An input that reads `millivolts`.
    #[must_use]
    pub const fn new(millivolts: u16) -> Self {
        Self {
            reading: Ok(millivolts),
        }
    }
}

impl Adc for MockAdc {
    fn read_millivolts(&mut self) -> Result<u16, AdcError> {
        self.reading
    }
}

/// A clock whose uptime the test advances by hand.
#[derive(Clone, Copy, Debug, Default)]
pub struct MockRtc {
    uptime: u64,
    unix_offset: Option<u64>,
    failure: Option<RtcError>,
}

impl MockRtc {
    /// A clock at uptime 0 with no wall-clock time.
    #[must_use]
    pub const fn new() -> Self {
        Self {
            uptime: 0,
            unix_offset: None,
            failure: None,
        }
    }

    /// Moves uptime (and wall-clock time) forward by `millis`.
    pub fn advance(&mut self, millis: u64) {
        self.uptime = self.uptime.saturating_add(millis);
    }

    /// Makes every later set fail with `error`, or succeed again with `None`.
    pub fn fail_with(&mut self, error: Option<RtcError>) {
        self.failure = error;
    }
}

impl Rtc for MockRtc {
    fn uptime_millis(&self) -> u64 {
        self.uptime
    }

    fn unix_time_millis(&self) -> Option<u64> {
        self.unix_offset
            .map(|offset| offset.saturating_add(self.uptime))
    }

    fn set_unix_time_millis(&mut self, unix_millis: u64) -> Result<(), RtcError> {
        if let Some(error) = self.failure {
            return Err(error);
        }
        self.unix_offset = Some(unix_millis.saturating_sub(self.uptime));
        Ok(())
    }
}

/// A pin that is both an output and an input: it reads back what was driven.
#[derive(Clone, Copy, Debug, Default)]
pub struct MockPin {
    /// The pin level: `true` is high.
    pub high: bool,
    /// The error every operation returns, if any.
    pub failure: Option<GpioError>,
}

impl MockPin {
    /// A low pin.
    #[must_use]
    pub const fn new() -> Self {
        Self {
            high: false,
            failure: None,
        }
    }

    fn check(&self) -> Result<(), GpioError> {
        self.failure.map_or(Ok(()), Err)
    }
}

impl OutputPin for MockPin {
    fn set_high(&mut self) -> Result<(), GpioError> {
        self.check()?;
        self.high = true;
        Ok(())
    }

    fn set_low(&mut self) -> Result<(), GpioError> {
        self.check()?;
        self.high = false;
        Ok(())
    }
}

impl InputPin for MockPin {
    fn is_high(&mut self) -> Result<bool, GpioError> {
        self.check()?;
        Ok(self.high)
    }
}

// ---------------------------------------------------------------------------------------------
// BLE setup link

/// One scripted event on a [`MockSetupLink`] connection.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum LinkEvent {
    /// The app writes this payload.
    Payload(Vec<u8>),
    /// The app disconnects.
    Disconnect,
}

/// Answers each payload the Device sends with the payloads the app writes back.
pub type Peer = Box<dyn FnMut(&[u8]) -> Vec<Vec<u8>>>;

struct MockConnection {
    max_payload: usize,
    incoming: VecDeque<LinkEvent>,
    peer: Option<Peer>,
}

/// A scripted BLE setup link.
///
/// - [`MockSetupLink::connect`] queues a connection; [`SetupLink::accept`] opens the next one,
///   and fails with [`LinkError::Transport`] once none is left, which ends a test run.
/// - [`SetupLink::receive`] returns the connection's scripted events in order. With nothing left
///   it returns [`LinkError::Timeout`], as if the app went quiet, and records the timeout asked
///   for.
/// - Every [`SetupLink::send`] is recorded, and the connection's peer callback (or the link's
///   default one), if set, answers it: its payloads are queued as incoming writes on the same
///   connection.
pub struct MockSetupLink {
    pending: VecDeque<MockConnection>,
    current: Option<MockConnection>,
    peer: Option<Peer>,
    sent: Vec<Vec<Vec<u8>>>,
    accepts: usize,
    disconnects: usize,
    timeouts: Vec<u32>,
}

impl Default for MockSetupLink {
    fn default() -> Self {
        Self::new()
    }
}

impl MockSetupLink {
    /// A link with no connection scripted.
    #[must_use]
    pub fn new() -> Self {
        Self {
            pending: VecDeque::new(),
            current: None,
            peer: None,
            sent: Vec::new(),
            accepts: 0,
            disconnects: 0,
            timeouts: Vec::new(),
        }
    }

    /// Queues a connection with ATT payload size `max_payload` (the MTU minus 3) whose app first
    /// writes `payloads`.
    pub fn connect(&mut self, max_payload: usize, payloads: Vec<Vec<u8>>) {
        self.connect_with(
            max_payload,
            payloads.into_iter().map(LinkEvent::Payload).collect(),
        );
    }

    /// Queues a connection that plays `events`.
    pub fn connect_with(&mut self, max_payload: usize, events: Vec<LinkEvent>) {
        self.pending.push_back(MockConnection {
            max_payload,
            incoming: events.into(),
            peer: None,
        });
    }

    /// Queues a connection whose app first writes `payloads` and then answers every send with
    /// its own `peer`.
    pub fn connect_with_peer(
        &mut self,
        max_payload: usize,
        payloads: Vec<Vec<u8>>,
        peer: impl FnMut(&[u8]) -> Vec<Vec<u8>> + 'static,
    ) {
        self.pending.push_back(MockConnection {
            max_payload,
            incoming: payloads.into_iter().map(LinkEvent::Payload).collect(),
            peer: Some(Box::new(peer)),
        });
    }

    /// Answers every later send on a connection without its own peer with `peer`.
    pub fn set_peer(&mut self, peer: impl FnMut(&[u8]) -> Vec<Vec<u8>> + 'static) {
        self.peer = Some(Box::new(peer));
    }

    /// The payloads sent, one list per accepted connection.
    #[must_use]
    pub fn sent(&self) -> &[Vec<Vec<u8>>] {
        &self.sent
    }

    /// How many connections were accepted.
    #[must_use]
    pub fn accept_count(&self) -> usize {
        self.accepts
    }

    /// How many times the Device disconnected an open connection.
    #[must_use]
    pub fn disconnect_count(&self) -> usize {
        self.disconnects
    }

    /// The timeouts of the receives that timed out, in milliseconds.
    #[must_use]
    pub fn timeouts(&self) -> &[u32] {
        &self.timeouts
    }

    /// Whether a connection is open.
    #[must_use]
    pub fn is_connected(&self) -> bool {
        self.current.is_some()
    }
}

impl SetupLink for MockSetupLink {
    async fn accept(&mut self) -> Result<(), LinkError> {
        let next = self.pending.pop_front().ok_or(LinkError::Transport)?;
        self.current = Some(next);
        self.sent.push(Vec::new());
        self.accepts += 1;
        Ok(())
    }

    async fn receive(&mut self, buffer: &mut [u8], timeout_ms: u32) -> Result<usize, LinkError> {
        let connection = self.current.as_mut().ok_or(LinkError::Disconnected)?;
        match connection.incoming.pop_front() {
            Some(LinkEvent::Payload(payload)) => {
                let length = payload.len().min(buffer.len());
                buffer[..length].copy_from_slice(&payload[..length]);
                Ok(length)
            }
            Some(LinkEvent::Disconnect) => {
                self.current = None;
                Err(LinkError::Disconnected)
            }
            None => {
                self.timeouts.push(timeout_ms);
                Err(LinkError::Timeout)
            }
        }
    }

    async fn send(&mut self, payload: &[u8]) -> Result<(), LinkError> {
        let connection = self.current.as_mut().ok_or(LinkError::Disconnected)?;
        assert!(
            payload.len() <= connection.max_payload,
            "a send exceeds the ATT payload size"
        );
        if let Some(sent) = self.sent.last_mut() {
            sent.push(payload.to_vec());
        }
        if let Some(peer) = connection.peer.as_mut().or(self.peer.as_mut()) {
            for answer in peer(payload) {
                connection.incoming.push_back(LinkEvent::Payload(answer));
            }
        }
        Ok(())
    }

    fn max_payload(&self) -> usize {
        self.current
            .as_ref()
            .map_or(20, |connection| connection.max_payload)
    }

    async fn disconnect(&mut self) {
        if self.current.take().is_some() {
            self.disconnects += 1;
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Wi-Fi

/// One join the [`MockWifi`] saw.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct JoinAttempt {
    /// The SSID.
    pub ssid: String,
    /// The passphrase (a test fixture, never a real one).
    pub password: String,
    /// The BSSID asked for.
    pub bssid: [u8; 6],
    /// The channel asked for.
    pub channel: u8,
}

/// A Wi-Fi station with a scripted scan and scripted join outcomes.
///
/// A join with an SSID and password pair that has no scripted outcome fails with
/// [`JoinError::WrongPassword`].
pub struct MockWifi {
    scan: Vec<AccessPoint>,
    scan_failure: Option<WifiError>,
    outcomes: Vec<(String, String, Result<(), JoinError>)>,
    joins: Vec<JoinAttempt>,
    scans: usize,
}

impl MockWifi {
    /// A station whose every scan hears `access_points`.
    #[must_use]
    pub fn new(access_points: Vec<AccessPoint>) -> Self {
        Self {
            scan: access_points,
            scan_failure: None,
            outcomes: Vec::new(),
            joins: Vec::new(),
            scans: 0,
        }
    }

    /// Makes a join with `ssid` and `password` end with `outcome`.
    pub fn set_join_outcome(&mut self, ssid: &str, password: &str, outcome: Result<(), JoinError>) {
        self.outcomes
            .push((ssid.to_owned(), password.to_owned(), outcome));
    }

    /// Makes every later scan fail with `error`, or succeed again with `None`.
    pub fn fail_scan(&mut self, error: Option<WifiError>) {
        self.scan_failure = error;
    }

    /// Every join attempted, in order.
    #[must_use]
    pub fn joins(&self) -> &[JoinAttempt] {
        &self.joins
    }

    /// How many joins were attempted.
    #[must_use]
    pub fn join_count(&self) -> usize {
        self.joins.len()
    }

    /// How many scans ran.
    #[must_use]
    pub fn scan_count(&self) -> usize {
        self.scans
    }
}

impl Wifi for MockWifi {
    async fn scan(&mut self, out: &mut [AccessPoint]) -> Result<usize, WifiError> {
        self.scans += 1;
        if let Some(error) = self.scan_failure {
            return Err(error);
        }
        let mut heard = self.scan.clone();
        // Keep the strongest when the buffer is too small, as the trait requires.
        heard.sort_by_key(|access_point| std::cmp::Reverse(access_point.rssi));
        let count = heard.len().min(out.len());
        out[..count].copy_from_slice(&heard[..count]);
        Ok(count)
    }

    async fn join(
        &mut self,
        ssid: &str,
        password: &str,
        bssid: [u8; 6],
        channel: u8,
    ) -> Result<(), JoinError> {
        self.joins.push(JoinAttempt {
            ssid: ssid.to_owned(),
            password: password.to_owned(),
            bssid,
            channel,
        });
        self.outcomes
            .iter()
            .find(|(s, p, _)| s == ssid && p == password)
            .map_or(Err(JoinError::WrongPassword), |(_, _, outcome)| *outcome)
    }
}
