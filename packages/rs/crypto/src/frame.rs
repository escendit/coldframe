//! Frame and downlink sealing (AD-12) and the replay window (AD-17).
//!
//! Nonce = `device_id[0..4] ‖ counter_u64_be`; AAD = `u8 protocol_major ‖ device_id ‖
//! counter_u64_be`. Uplink frames use the `seal/v1` key, downlinks the `ack/v1` key, so the two
//! directions never share a (key, nonce) pair.

use crate::keys::DeviceId;
use crate::spec::{
    AEAD_NONCE_LENGTH, DEVICE_ID_LENGTH, FRAME_AAD_LENGTH, FRAME_NONCE_DEVICE_ID_PREFIX_LENGTH,
    PROTOCOL_MAJOR, PURPOSE_KEY_LENGTH, REPLAY_WINDOW,
};
use crate::{Error, aead};

/// The 12-byte nonce of the frame with `counter`.
#[must_use]
pub fn nonce(device_id: &DeviceId, counter: u64) -> [u8; AEAD_NONCE_LENGTH] {
    let mut nonce = [0u8; AEAD_NONCE_LENGTH];
    let (prefix, tail) = nonce.split_at_mut(FRAME_NONCE_DEVICE_ID_PREFIX_LENGTH);
    prefix.copy_from_slice(&device_id.0[..FRAME_NONCE_DEVICE_ID_PREFIX_LENGTH]);
    tail.copy_from_slice(&counter.to_be_bytes());
    nonce
}

/// The 17-byte associated data of the frame with `counter`.
#[must_use]
pub fn aad(device_id: &DeviceId, counter: u64) -> [u8; FRAME_AAD_LENGTH] {
    let mut aad = [0u8; FRAME_AAD_LENGTH];
    aad[0] = PROTOCOL_MAJOR;
    aad[1..=DEVICE_ID_LENGTH].copy_from_slice(&device_id.0);
    aad[1 + DEVICE_ID_LENGTH..].copy_from_slice(&counter.to_be_bytes());
    aad
}

/// Seals `plaintext` with a purpose key into `out` (`plaintext.len() + 16` bytes).
pub fn seal(
    key: &[u8; PURPOSE_KEY_LENGTH],
    device_id: &DeviceId,
    counter: u64,
    plaintext: &[u8],
    out: &mut [u8],
) -> Result<usize, Error> {
    aead::seal(
        key,
        &nonce(device_id, counter),
        &aad(device_id, counter),
        plaintext,
        out,
    )
}

/// Opens a sealed frame into `out` (`sealed.len() - 16` bytes). Checks authenticity only; use
/// [`open_checked`] to enforce the replay window too.
pub fn open(
    key: &[u8; PURPOSE_KEY_LENGTH],
    device_id: &DeviceId,
    counter: u64,
    sealed: &[u8],
    out: &mut [u8],
) -> Result<usize, Error> {
    aead::open(
        key,
        &nonce(device_id, counter),
        &aad(device_id, counter),
        sealed,
        out,
    )
}

/// Opens a sealed frame and records its counter: a replay is refused before decrypting, and the
/// window moves only after the frame authenticates.
pub fn open_checked(
    key: &[u8; PURPOSE_KEY_LENGTH],
    device_id: &DeviceId,
    counter: u64,
    sealed: &[u8],
    out: &mut [u8],
    window: &mut ReplayWindow,
) -> Result<usize, Error> {
    window.check(counter)?;
    let length = open(key, device_id, counter, sealed, out)?;
    window.accept(counter)?;
    Ok(length)
}

/// Accepts a counter above the high-water mark, or an unseen one among the 64 counters ending at
/// it (AD-17). Anything else is a replay.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct ReplayWindow {
    highest: Option<u64>,
    /// Bit `i` is set when counter `highest - i` was accepted.
    seen: u64,
}

const _: () = assert!(REPLAY_WINDOW == u64::BITS as usize);

impl ReplayWindow {
    /// A window that has seen nothing.
    #[must_use]
    pub const fn new() -> Self {
        Self {
            highest: None,
            seen: 0,
        }
    }

    /// The highest accepted counter, if any.
    #[must_use]
    pub const fn highest(&self) -> Option<u64> {
        self.highest
    }

    /// Whether `counter` would be accepted, without recording it.
    pub fn check(&self, counter: u64) -> Result<(), Error> {
        let Some(highest) = self.highest else {
            return Ok(());
        };
        if counter > highest {
            return Ok(());
        }
        let age = highest - counter;
        if age >= REPLAY_WINDOW as u64 || self.seen & (1 << age) != 0 {
            return Err(Error::Replay);
        }
        Ok(())
    }

    /// Records `counter`, or refuses it as a replay.
    pub fn accept(&mut self, counter: u64) -> Result<(), Error> {
        self.check(counter)?;
        match self.highest {
            Some(highest) if counter <= highest => {
                self.seen |= 1 << (highest - counter);
            }
            Some(highest) => {
                let shift = counter - highest;
                self.seen = if shift >= u64::from(u64::BITS) {
                    0
                } else {
                    self.seen << shift
                } | 1;
                self.highest = Some(counter);
            }
            None => {
                self.seen = 1;
                self.highest = Some(counter);
            }
        }
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::ReplayWindow;

    #[test]
    fn the_first_counter_may_be_zero() {
        let mut window = ReplayWindow::new();
        assert!(window.accept(0).is_ok());
        assert!(window.accept(0).is_err());
        assert!(window.accept(1).is_ok());
    }

    #[test]
    fn a_large_jump_forgets_the_old_window() {
        let mut window = ReplayWindow::new();
        assert!(window.accept(5).is_ok());
        assert!(window.accept(u64::MAX).is_ok());
        assert!(window.accept(5).is_err());
        assert!(window.accept(u64::MAX - 63).is_ok());
        assert!(window.accept(u64::MAX - 64).is_err());
    }
}
