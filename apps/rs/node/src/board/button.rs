//! The setup button on GPIO7: a switch to ground, active low, with the internal pull-up.
//!
//! It wakes the Node from deep sleep on a low level through a low-power path: esp-hal assigns the
//! pad to `ext0`/`ext1` at sleep entry, holds the pad and keeps its pull-up through the sleep. The
//! Node arms that wake only while the button reads released, so a stuck button never re-wakes it
//! in a loop ([`super::sleep::deep_sleep`]).

use coldframe_hal::{GpioError, InputPin};
use esp_hal::gpio::{AnyPin, Event, Input, InputConfig, Pull, WakeConfigError, WakeupConfig};

/// The setup button.
pub struct BoardButton(Input<'static>);

impl BoardButton {
    /// The button on `pin`, with the pull-up on.
    pub fn new(pin: AnyPin<'static>) -> Self {
        Self(Input::new(pin, InputConfig::default().with_pull(Pull::Up)))
    }

    /// Makes a low level wake the chip from deep sleep.
    ///
    /// # Errors
    ///
    /// [`WakeConfigError::NoLowPowerPath`] if the pad cannot wake the chip from deep sleep.
    pub fn arm_wake(&mut self) -> Result<(), WakeConfigError> {
        self.0
            .apply_wakeup_config(&WakeupConfig::default().with_low_power_path(true))?;
        self.0.listen(Event::LowLevel);
        Ok(())
    }
}

impl InputPin for BoardButton {
    fn is_high(&mut self) -> Result<bool, GpioError> {
        Ok(self.0.is_high())
    }
}
