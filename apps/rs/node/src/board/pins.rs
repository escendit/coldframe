//! The reference wiring: the one place to change pins.
//!
//! There is no Node board yet (hardware/ is empty until Epic 10), so this is a reference wiring
//! for an ESP32-S3 dev board. `_bmad-output/specs/spec-coldframe/device-hardware.md` lists the
//! same table.
//!
//! | Signal | Pin | Notes |
//! | --- | --- | --- |
//! | Soil probe analogue out | GPIO1 (ADC1_CH0) | raw count, 11 dB |
//! | Battery divider midpoint | GPIO2 (ADC1_CH1) | calibrated mV, 11 dB, 100 kΩ / 100 kΩ |
//! | Probe power switch | GPIO4 | high-side switch, high = on; control pulled down externally |
//! | Divider switch | GPIO5 | high-side switch, high = on; control pulled down externally |
//! | Charger status (`CHRG`) | GPIO6 | active low, internal pull-up |
//! | Setup button | GPIO7 | to ground, active low, internal pull-up kept in deep sleep; wakes the Node |
//! | I²C SDA | GPIO8 | BME680 |
//! | I²C SCL | GPIO9 | BME680 |
//! | BME680 address | 0x77 | SDO to VCC |
//!
//! The setup button is a momentary switch from GPIO7 (a low-power pad, so it can wake the chip from
//! deep sleep) to ground. The internal pull-up holds it high while released, also in deep sleep.
//!
//! Only ADC1 is used: ADC2 conflicts with the radio. The digital pads float in deep sleep, so the
//! switch controls need external pull-downs to stay off.
//!
//! The probe feed and the battery divider are switched on the **high side** (a P-MOSFET with a
//! level-shifting driver, or a load switch, enabled from the GPIO), off while the GPIO is low or
//! pulled down. A low-side N-MOSFET would leave the divider midpoint, and GPIO2, at up to 4.2 V through
//! the top resistor while off, beyond the pad's rating, and would leak through the pad. With the
//! divider switched high-side, the ADC pins see 0 V while off. The divider midpoint may carry at most
//! 10 nF: with 100 kΩ / 100 kΩ that is RC = 50 kΩ × 10 nF = 0.5 ms, so the 10 ms `DIVIDER_SETTLE_MS`
//! is well over 5·RC. A larger capacitor needs `DIVIDER_SETTLE_MS` raised to at least 5·RC.

use coldframe_sensing::Divider;
use esp_hal::analog::adc::Attenuation;
use esp_hal::gpio::AnyPin;
use esp_hal::peripherals::{GPIO1, GPIO2};

/// The soil probe's ADC1 pin.
pub type SoilPin = GPIO1<'static>;

/// The battery divider's ADC1 pin.
pub type BatteryPin = GPIO2<'static>;

/// Attenuation of both analogue inputs: about 0–3.1 V.
pub const ATTENUATION: Attenuation = Attenuation::_11dB;

/// The battery divider: 100 kΩ from the cell, 100 kΩ to ground (4.2 V reads 2.1 V).
pub const DIVIDER: Divider = Divider {
    top_ohms: 100_000,
    bottom_ohms: 100_000,
};

/// I²C address of the BME680 (SDO high).
pub const BME680_SECONDARY_ADDRESS: bool = true;

/// The Node's pins, taken from the peripherals by [`take_pins!`](crate::take_pins).
pub struct Pins {
    /// Soil probe analogue output.
    pub soil: SoilPin,
    /// Battery divider midpoint.
    pub battery: BatteryPin,
    /// Probe power switch.
    pub probe_switch: AnyPin<'static>,
    /// Battery divider switch.
    pub divider_switch: AnyPin<'static>,
    /// Charger status input.
    pub charger: AnyPin<'static>,
    /// Setup button input.
    pub button: AnyPin<'static>,
    /// I²C data.
    pub sda: AnyPin<'static>,
    /// I²C clock.
    pub scl: AnyPin<'static>,
}

/// Moves the Node's pins out of esp-hal's `Peripherals`.
#[macro_export]
macro_rules! take_pins {
    ($peripherals:ident) => {
        $crate::board::pins::Pins {
            soil: $peripherals.GPIO1,
            battery: $peripherals.GPIO2,
            probe_switch: $peripherals.GPIO4.into(),
            divider_switch: $peripherals.GPIO5.into(),
            charger: $peripherals.GPIO6.into(),
            button: $peripherals.GPIO7.into(),
            sda: $peripherals.GPIO8.into(),
            scl: $peripherals.GPIO9.into(),
        }
    };
}
