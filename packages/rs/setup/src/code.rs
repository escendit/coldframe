//! The setup code (proof-of-possession code, AD-25): 8 characters of the Crockford base32
//! alphabet, 40 bits drawn once from the TRNG.
//!
//! It is mixed into the session key and never sent over BLE. [`SetupCode`] is deliberately not
//! `Debug` or `Display`; [`SetupCode::as_str`] is the one way to read it, for the serial line.

use coldframe_hal::{Trng, TrngError};

/// Characters in a setup code.
pub const CODE_LENGTH: usize = 8;

/// Bytes of TRNG output one code consumes (8 × 5 bits).
pub const CODE_ENTROPY_LENGTH: usize = 5;

/// The Crockford base32 alphabet: digits and uppercase letters without I, L, O and U.
pub const ALPHABET: &[u8; 32] = b"0123456789ABCDEFGHJKMNPQRSTVWXYZ";

/// A setup code. Deliberately not `Debug`, `Display` or `Copy`.
#[derive(Clone, PartialEq, Eq)]
pub struct SetupCode([u8; CODE_LENGTH]);

impl SetupCode {
    /// Draws a fresh code from the TRNG.
    ///
    /// # Errors
    ///
    /// The TRNG's error; no code exists then.
    pub fn generate<T: Trng>(trng: &mut T) -> Result<Self, TrngError> {
        let mut entropy = [0u8; CODE_ENTROPY_LENGTH];
        trng.fill(&mut entropy)?;
        let code = Self::from_entropy(entropy);
        entropy.fill(0);
        Ok(code)
    }

    /// The code for 40 bits of entropy, most significant bits first.
    #[must_use]
    pub fn from_entropy(entropy: [u8; CODE_ENTROPY_LENGTH]) -> Self {
        let mut bits = entropy
            .iter()
            .fold(0u64, |acc, &byte| (acc << 8) | u64::from(byte));
        let mut code = [0u8; CODE_LENGTH];
        for slot in code.iter_mut().rev() {
            // The mask keeps 5 bits, so the index is below 32.
            *slot = ALPHABET[usize::try_from(bits & 0x1F).unwrap_or(0)];
            bits >>= 5;
        }
        Self(code)
    }

    /// A code from its stored or typed form: exactly 8 characters of [`ALPHABET`] (uppercase).
    #[must_use]
    pub fn parse(text: &[u8]) -> Option<Self> {
        let code: [u8; CODE_LENGTH] = text.try_into().ok()?;
        code.iter()
            .all(|byte| ALPHABET.contains(byte))
            .then_some(Self(code))
    }

    /// The code as text, for the one serial line that shows it and for the session key.
    #[must_use]
    pub fn as_str(&self) -> &str {
        // Every byte is from the ASCII alphabet.
        core::str::from_utf8(&self.0).unwrap_or("")
    }

    /// The code's bytes.
    #[must_use]
    pub const fn as_bytes(&self) -> &[u8; CODE_LENGTH] {
        &self.0
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn entropy_maps_to_the_alphabet_big_endian() {
        assert_eq!(SetupCode::from_entropy([0; 5]).as_str(), "00000000");
        assert_eq!(SetupCode::from_entropy([0xFF; 5]).as_str(), "ZZZZZZZZ");
        assert_eq!(
            SetupCode::from_entropy([0, 0, 0, 0, 0x21]).as_str(),
            "00000011"
        );
    }

    #[test]
    fn parse_accepts_only_the_alphabet() {
        assert!(SetupCode::parse(b"K7Q492MX").is_some());
        assert!(SetupCode::parse(b"k7q492mx").is_none());
        assert!(SetupCode::parse(b"K7Q492MI").is_none());
        assert!(SetupCode::parse(b"K7Q492M").is_none());
    }
}
