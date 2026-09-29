//! [`Radio`] over esp-radio's Wi-Fi controller.

use coldframe_hal::{Radio, RadioError};
use esp_radio::wifi::WifiController;

/// The radio, on for as long as this value lives.
///
/// Creating the esp-radio [`WifiController`] initialises the radio and registers it as an entropy
/// source of the TRNG. Stories 3.4 and 3.5 add BLE and Wi-Fi join on top of it.
pub struct BoardRadio {
    _controller: WifiController<'static>,
}

impl BoardRadio {
    /// Takes over a controller that `WifiController::new` has just started.
    pub fn new(controller: WifiController<'static>) -> Self {
        Self {
            _controller: controller,
        }
    }
}

impl Radio for BoardRadio {
    fn enable(&mut self) -> Result<(), RadioError> {
        // The controller exists, so the radio is already on.
        Ok(())
    }

    fn is_enabled(&self) -> bool {
        true
    }
}
