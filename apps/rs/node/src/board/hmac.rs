//! [`HmacPeripheral`] over the esp-hal HMAC accelerator in upstream mode.

use coldframe_hal::hmac::HMAC_LENGTH;
use coldframe_hal::{HmacError, HmacPeripheral, KeyBlock};
use esp_hal::hmac::{Hmac, HmacPurpose, KeyId};
use esp_hal::peripherals::HMAC;

/// The HMAC peripheral: `HMAC-SHA256` keyed by an eFuse block that software cannot read.
pub struct BoardHmac {
    hmac: Hmac<'static>,
}

impl BoardHmac {
    /// Takes the HMAC peripheral.
    pub fn new(hmac: HMAC<'static>) -> Self {
        Self {
            hmac: Hmac::new(hmac),
        }
    }
}

fn key_id(block: KeyBlock) -> KeyId {
    match block {
        KeyBlock::Key0 => KeyId::Key0,
        KeyBlock::Key1 => KeyId::Key1,
        KeyBlock::Key2 => KeyId::Key2,
        KeyBlock::Key3 => KeyId::Key3,
        KeyBlock::Key4 => KeyId::Key4,
        KeyBlock::Key5 => KeyId::Key5,
    }
}

impl HmacPeripheral for BoardHmac {
    fn hmac_sha256(&mut self, block: KeyBlock, msg: &[u8]) -> Result<[u8; HMAC_LENGTH], HmacError> {
        self.hmac.init();
        nb::block!(self.hmac.configure(HmacPurpose::ToUser, key_id(block)))
            .map_err(|_| HmacError::KeyPurposeMismatch)?;
        let mut remaining = msg;
        while !remaining.is_empty() {
            remaining = match nb::block!(self.hmac.update(remaining)) {
                Ok(rest) => rest,
                Err(never) => match never {},
            };
        }
        let mut output = [0u8; HMAC_LENGTH];
        match nb::block!(self.hmac.finalize(&mut output)) {
            Ok(()) => Ok(output),
            Err(never) => match never {},
        }
    }
}
