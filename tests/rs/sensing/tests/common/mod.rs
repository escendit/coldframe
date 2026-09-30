//! Shared helpers: a blocking executor, and pins and sensors that record into one event log.

#![allow(dead_code, reason = "each test file uses a different part")]

use std::cell::RefCell;
use std::collections::VecDeque;
use std::future::Future;
use std::pin::pin;
use std::rc::Rc;
use std::task::{Context, Poll, Waker};

use coldframe_hal::mock::{MockAdc, MockEnvSensor, MockFlash, MockRawAdc, MockTimer};
use coldframe_hal::{
    Adc, AdcError, EnvError, EnvSample, EnvSensor, GpioError, OutputPin, RawAdc, Timer,
};

/// Size of a counter partition: two sectors.
pub const COUNTER_SIZE: usize = 0x2000;

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

/// An erased counter partition.
pub fn erased() -> MockFlash {
    MockFlash::new(COUNTER_SIZE)
}

/// One step of a wake, in the order it happened.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Event {
    /// A switch was driven high.
    High(&'static str),
    /// A switch was driven low.
    Low(&'static str),
    /// The soil probe was sampled.
    Soil,
    /// The BME680 measured.
    Env,
    /// The battery was read.
    Battery,
    /// The wake waited this many milliseconds.
    Wait(u32),
}

/// The shared event log.
pub type Log = Rc<RefCell<Vec<Event>>>;

/// A new empty log.
pub fn log() -> Log {
    Rc::new(RefCell::new(Vec::new()))
}

/// A switch that logs every level change, and may refuse to go high or low.
pub struct Switch {
    pub name: &'static str,
    pub log: Log,
    pub high: bool,
    pub fail_high: bool,
    pub fail_low: bool,
}

impl Switch {
    pub fn new(name: &'static str, log: &Log) -> Self {
        Self {
            name,
            log: log.clone(),
            high: false,
            fail_high: false,
            fail_low: false,
        }
    }
}

impl OutputPin for Switch {
    fn set_high(&mut self) -> Result<(), GpioError> {
        if self.fail_high {
            return Err(GpioError::Hardware);
        }
        self.high = true;
        self.log.borrow_mut().push(Event::High(self.name));
        Ok(())
    }

    fn set_low(&mut self) -> Result<(), GpioError> {
        if self.fail_low {
            return Err(GpioError::Hardware);
        }
        self.high = false;
        self.log.borrow_mut().push(Event::Low(self.name));
        Ok(())
    }
}

/// A soil ADC that logs each sample.
pub struct Soil {
    pub adc: MockRawAdc,
    pub log: Log,
}

impl RawAdc for Soil {
    fn read_raw(&mut self) -> Result<u16, AdcError> {
        self.log.borrow_mut().push(Event::Soil);
        self.adc.read_raw()
    }
}

/// A BME680 that logs each measurement.
pub struct Env {
    pub sensor: MockEnvSensor,
    pub log: Log,
}

impl EnvSensor for Env {
    fn measure_forced(&mut self) -> Result<EnvSample, EnvError> {
        self.log.borrow_mut().push(Event::Env);
        self.sensor.measure_forced()
    }
}

/// A battery ADC that logs each read: queued conversions first, then the mock's reading.
pub struct Battery {
    pub adc: MockAdc,
    pub queued: VecDeque<Result<u16, AdcError>>,
    pub log: Log,
}

impl Adc for Battery {
    fn read_millivolts(&mut self) -> Result<u16, AdcError> {
        self.log.borrow_mut().push(Event::Battery);
        match self.queued.pop_front() {
            Some(reading) => reading,
            None => self.adc.read_millivolts(),
        }
    }
}

/// A timer that logs each wait into the event log, so waits are ordered against the switches.
pub struct Wait {
    pub timer: MockTimer,
    pub log: Log,
}

impl Timer for Wait {
    async fn sleep_ms(&mut self, ms: u32) {
        self.log.borrow_mut().push(Event::Wait(ms));
        self.timer.sleep_ms(ms).await;
    }
}

/// The sample every healthy BME680 in these tests measures.
pub const SAMPLE: EnvSample = EnvSample {
    temperature_milli_c: 21_500,
    humidity_milli_pct: 55_250,
    gas_ohms: Some(120_000),
};
