//! [`Radio`] over esp-radio's Wi-Fi controller.

use coldframe_hal::{Radio, RadioError};
use esp_radio::wifi::WifiController;

/// The radio, on for as long as this value lives.
///
/// Creating the esp-radio [`WifiController`] initialises the radio and registers it as an entropy
/// source of the TRNG. [`BoardRadio::into_controller`] hands the controller on to the Wi-Fi
/// station adapter once the identity is provisioned.
pub struct BoardRadio {
    controller: WifiController<'static>,
}

impl BoardRadio {
    /// Takes over a controller that `WifiController::new` has just started.
    pub fn new(controller: WifiController<'static>) -> Self {
        Self { controller }
    }

    /// The controller, for the Wi-Fi station adapter. The radio stays on while it lives.
    pub fn into_controller(self) -> WifiController<'static> {
        self.controller
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
