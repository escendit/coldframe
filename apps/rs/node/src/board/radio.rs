//! [`Radio`] over esp-radio's Wi-Fi controller, started lazily.
//!
//! The radio is the TRNG's entropy source, and the identity only needs it when it draws a new
//! root (first boot). A normal wake never calls [`Radio::enable`], so it never powers the radio.

use coldframe_hal::{Radio, RadioError};
use esp_hal::peripherals::WIFI;
use esp_radio::wifi::{ControllerConfig, WifiController};

/// The radio: off until [`Radio::enable`], then on for as long as this value lives.
pub struct BoardRadio {
    wifi: Option<WIFI<'static>>,
    controller: Option<WifiController<'static>>,
}

impl BoardRadio {
    /// Holds the Wi-Fi peripheral without starting anything.
    pub fn new(wifi: WIFI<'static>) -> Self {
        Self {
            wifi: Some(wifi),
            controller: None,
        }
    }
}

impl Radio for BoardRadio {
    fn enable(&mut self) -> Result<(), RadioError> {
        if self.controller.is_some() {
            return Ok(());
        }
        let wifi = self.wifi.take().ok_or(RadioError::StartFailed)?;
        // Creating the controller initialises the radio and registers it as a TRNG entropy source.
        let controller = WifiController::new(wifi, ControllerConfig::default())
            .map_err(|_| RadioError::StartFailed)?;
        self.controller = Some(controller);
        Ok(())
    }

    fn is_enabled(&self) -> bool {
        self.controller.is_some()
    }
}
