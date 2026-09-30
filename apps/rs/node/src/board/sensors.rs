//! The switches, the charger pin, the two ADC1 inputs and the BME680.
//!
//! Both analogue inputs share ADC1, so they share one converter through a `RefCell` (the wake is
//! single-threaded). The soil input reads raw counts; the battery input uses esp-hal's curve
//! calibration and reads millivolts.

use core::cell::RefCell;

use bosch_bme680::{Bme680, BmeError, Configuration, DeviceAddress};
use coldframe_hal::{
    Adc, AdcError, EnvError, EnvSample, EnvSensor, GpioError, InputPin, OutputPin, RawAdc,
};
use coldframe_sensing::env_sample_from;
use esp_hal::Blocking;
use esp_hal::analog::adc::{Adc as EspAdc, AdcCalCurve, AdcConfig, AdcPin};
use esp_hal::delay::Delay;
use esp_hal::gpio::{AnyPin, Input, InputConfig, Level, Output, OutputConfig, Pull};
use esp_hal::i2c::master::{Config as I2cConfig, I2c};
use esp_hal::peripherals::{ADC1, I2C0};
use esp_hal::time::Rate;

use super::pins::{ATTENUATION, BME680_SECONDARY_ADDRESS, BatteryPin, SoilPin};

/// Ambient temperature the driver assumes for its first heater setting, °C.
const ASSUMED_AMBIENT_C: i32 = 20;

/// A power switch: a push-pull output, low (off) from the start.
pub struct BoardSwitch(Output<'static>);

impl BoardSwitch {
    /// The switch on `pin`, off.
    pub fn new(pin: AnyPin<'static>) -> Self {
        Self(Output::new(pin, Level::Low, OutputConfig::default()))
    }
}

impl OutputPin for BoardSwitch {
    fn set_high(&mut self) -> Result<(), GpioError> {
        self.0.set_high();
        Ok(())
    }

    fn set_low(&mut self) -> Result<(), GpioError> {
        self.0.set_low();
        Ok(())
    }
}

/// The charger status pin, with the internal pull-up (the charger's output is open drain).
pub struct BoardCharger(Input<'static>);

impl BoardCharger {
    /// The input on `pin`.
    pub fn new(pin: AnyPin<'static>) -> Self {
        Self(Input::new(pin, InputConfig::default().with_pull(Pull::Up)))
    }
}

impl InputPin for BoardCharger {
    fn is_high(&mut self) -> Result<bool, GpioError> {
        Ok(self.0.is_high())
    }
}

type Converter = EspAdc<'static, ADC1<'static>, Blocking>;

/// ADC1 and its two pins.
pub struct BoardAdc {
    converter: RefCell<Converter>,
    soil: RefCell<AdcPin<SoilPin, ADC1<'static>>>,
    battery: RefCell<AdcPin<BatteryPin, ADC1<'static>, AdcCalCurve<ADC1<'static>>>>,
}

impl BoardAdc {
    /// Configures ADC1 with the soil pin (raw) and the battery pin (curve-calibrated).
    pub fn new(adc1: ADC1<'static>, soil: SoilPin, battery: BatteryPin) -> Self {
        let mut config = AdcConfig::new();
        let soil = config.enable_pin(soil, ATTENUATION);
        let battery =
            config.enable_pin_with_cal::<_, AdcCalCurve<ADC1<'static>>>(battery, ATTENUATION);
        Self {
            converter: RefCell::new(EspAdc::new(adc1, config)),
            soil: RefCell::new(soil),
            battery: RefCell::new(battery),
        }
    }

    /// The soil input.
    pub fn soil(&self) -> BoardSoil<'_> {
        BoardSoil(self)
    }

    /// The battery input.
    pub fn battery(&self) -> BoardBattery<'_> {
        BoardBattery(self)
    }
}

/// The soil probe's raw input.
pub struct BoardSoil<'a>(&'a BoardAdc);

impl RawAdc for BoardSoil<'_> {
    fn read_raw(&mut self) -> Result<u16, AdcError> {
        let mut converter = self.0.converter.borrow_mut();
        let mut pin = self.0.soil.borrow_mut();
        nb::block!(converter.read_oneshot(&mut pin)).map_err(|()| AdcError::Hardware)
    }
}

/// The battery divider's calibrated input.
pub struct BoardBattery<'a>(&'a BoardAdc);

impl Adc for BoardBattery<'_> {
    fn read_millivolts(&mut self) -> Result<u16, AdcError> {
        let mut converter = self.0.converter.borrow_mut();
        let mut pin = self.0.battery.borrow_mut();
        nb::block!(converter.read_oneshot(&mut pin)).map_err(|()| AdcError::Hardware)
    }
}

type Bus = I2c<'static, Blocking>;

/// The BME680 on I²C, or nothing if it did not answer at boot.
pub struct BoardEnv {
    sensor: Option<Bme680<Bus, Delay>>,
}

impl BoardEnv {
    /// Opens the bus and the sensor. A missing sensor is not fatal: every measurement then fails
    /// with [`EnvError::Bus`], and the wake still reports the soil and the battery.
    pub fn new(
        i2c: I2C0<'static>,
        sda: AnyPin<'static>,
        scl: AnyPin<'static>,
    ) -> (Self, Option<EnvError>) {
        let bus = match I2c::new(
            i2c,
            I2cConfig::default().with_frequency(Rate::from_khz(100)),
        ) {
            Ok(bus) => bus.with_sda(sda).with_scl(scl),
            Err(_) => return (Self { sensor: None }, Some(EnvError::Bus)),
        };
        let address = if BME680_SECONDARY_ADDRESS {
            DeviceAddress::Secondary
        } else {
            DeviceAddress::Primary
        };
        // Forced mode with the driver defaults: gas heater 300 °C for 150 ms.
        match Bme680::new(
            bus,
            address,
            Delay::new(),
            &Configuration::default(),
            ASSUMED_AMBIENT_C,
        ) {
            Ok(sensor) => (
                Self {
                    sensor: Some(sensor),
                },
                None,
            ),
            Err(error) => (Self { sensor: None }, Some(env_error(&error))),
        }
    }
}

fn env_error(error: &BmeError<Bus>) -> EnvError {
    match error {
        BmeError::MeasuringTimeOut => EnvError::Timeout,
        _ => EnvError::Bus,
    }
}

impl EnvSensor for BoardEnv {
    /// Maps the driver's result only: the unit conversion is `env_sample_from`, tested on the
    /// host. An invalid gas value costs only the gas Reading.
    fn measure_forced(&mut self) -> Result<EnvSample, EnvError> {
        let sensor = self.sensor.as_mut().ok_or(EnvError::Bus)?;
        let data = sensor.measure().map_err(|error| env_error(&error))?;
        env_sample_from(data.temperature, data.humidity, data.gas_resistance)
    }
}
