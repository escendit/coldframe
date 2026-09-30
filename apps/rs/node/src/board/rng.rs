//! [`Trng`] over the esp-hal TRNG.

use coldframe_hal::{Trng, TrngError};

/// The hardware TRNG. It refuses to run unless an entropy source (the radio) is on.
pub struct BoardTrng;

impl Trng for BoardTrng {
    fn fill(&mut self, buffer: &mut [u8]) -> Result<(), TrngError> {
        // `try_new` fails unless an entropy source is registered; esp-radio registers itself when
        // the Wi-Fi controller starts. A plain `Rng` would hand out pseudo-random bytes instead.
        let trng = esp_hal::rng::Trng::try_new().map_err(|_| TrngError::EntropySourceDisabled)?;
        trng.read(buffer);
        Ok(())
    }
}
