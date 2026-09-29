//! Lowercase hex, the text form of the Device ID, fingerprints, nonces and signatures.

use crate::Error;

const DIGITS: &[u8; 16] = b"0123456789abcdef";

/// Writes `bytes` as lowercase hex into `out`, which needs `2 * bytes.len()` bytes; returns the
/// text.
pub fn encode<'a>(bytes: &[u8], out: &'a mut [u8]) -> Result<&'a str, Error> {
    let out = out.get_mut(..bytes.len() * 2).ok_or(Error::InvalidLength)?;
    for (pair, byte) in out.chunks_exact_mut(2).zip(bytes) {
        pair[0] = DIGITS[usize::from(byte >> 4)];
        pair[1] = DIGITS[usize::from(byte & 0x0f)];
    }
    core::str::from_utf8(out).map_err(|_| Error::InvalidLength)
}

/// Lowercase hex of a fixed-size value, `M` must be `2 * N`.
pub(crate) fn encode_array<const N: usize, const M: usize>(bytes: &[u8; N]) -> [u8; M] {
    let mut out = [0u8; M];
    for (pair, byte) in out.chunks_exact_mut(2).zip(bytes) {
        pair[0] = DIGITS[usize::from(byte >> 4)];
        pair[1] = DIGITS[usize::from(byte & 0x0f)];
    }
    out
}

#[cfg(test)]
mod tests {
    use super::encode;

    #[test]
    fn encodes_lowercase() {
        let mut out = [0u8; 6];
        assert_eq!(encode(&[0x00, 0xab, 0xff], &mut out), Ok("00abff"));
    }

    #[test]
    fn refuses_a_short_buffer() {
        let mut out = [0u8; 3];
        assert!(encode(&[1, 2], &mut out).is_err());
    }
}
