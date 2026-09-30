//! [`Trng`] over the esp-hal TRNG.

use coldframe_hal::{Radio, Trng, TrngError};

use super::radio::BoardRadio;

/// The hardware TRNG. It refuses to run unless an entropy source (the Wi-Fi or BLE radio) is on.
pub struct BoardTrng;

impl Trng for BoardTrng {
    fn fill(&mut self, buffer: &mut [u8]) -> Result<(), TrngError> {
        // `try_new` fails unless an entropy source is registered; esp-radio registers itself when
        // the Wi-Fi or the BLE controller starts. A plain `Rng` would hand out pseudo-random bytes instead.
        let trng = esp_hal::rng::Trng::try_new().map_err(|_| TrngError::EntropySourceDisabled)?;
        trng.read(buffer);
        Ok(())
    }
}

/// The hardware TRNG, starting the Wi-Fi radio as its entropy source on the first fill only: a
/// setup mode that loads a stored setup code never powers the radio for it.
pub struct RadioTrng<'a, 'd> {
    radio: &'a mut BoardRadio<'d>,
}

impl<'a, 'd> RadioTrng<'a, 'd> {
    /// The TRNG over `radio`, which stays off until a byte is needed.
    pub fn new(radio: &'a mut BoardRadio<'d>) -> Self {
        Self { radio }
    }
}

impl Trng for RadioTrng<'_, '_> {
    fn fill(&mut self, buffer: &mut [u8]) -> Result<(), TrngError> {
        self.radio
            .enable()
            .map_err(|_| TrngError::EntropySourceDisabled)?;
        BoardTrng.fill(buffer)
    }
}
