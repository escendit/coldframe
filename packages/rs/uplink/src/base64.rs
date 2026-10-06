//! Standard padded base64 (RFC 4648 section 4), as `packages/openapi` carries sealed frames.

const ALPHABET: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
const PAD: u8 = b'=';

/// The encoded length of `bytes` bytes.
#[must_use]
pub const fn encoded_len(bytes: usize) -> usize {
    bytes.div_ceil(3) * 4
}

/// Encodes `bytes` into the front of `out`; returns the encoded length, or `None` when `out` is
/// too small.
pub fn encode(bytes: &[u8], out: &mut [u8]) -> Option<usize> {
    let length = encoded_len(bytes.len());
    let target = out.get_mut(..length)?;
    for (chunk, quad) in bytes.chunks(3).zip(target.chunks_exact_mut(4)) {
        let group = u32::from(chunk[0]) << 16
            | u32::from(chunk.get(1).copied().unwrap_or(0)) << 8
            | u32::from(chunk.get(2).copied().unwrap_or(0));
        let sextet = |shift: u32| ALPHABET[(group >> shift & 0x3F) as usize];
        quad[0] = sextet(18);
        quad[1] = sextet(12);
        quad[2] = if chunk.len() > 1 { sextet(6) } else { PAD };
        quad[3] = if chunk.len() > 2 { sextet(0) } else { PAD };
    }
    Some(length)
}

fn value(character: u8) -> Option<u32> {
    match character {
        b'A'..=b'Z' => Some(u32::from(character - b'A')),
        b'a'..=b'z' => Some(u32::from(character - b'a') + 26),
        b'0'..=b'9' => Some(u32::from(character - b'0') + 52),
        b'+' => Some(62),
        b'/' => Some(63),
        _ => None,
    }
}

/// Decodes canonical padded base64 into the front of `out`; returns the decoded length. `None`
/// for a length that is not a multiple of four, a character outside the alphabet, padding
/// anywhere but the end, non-zero bits under the padding, or an `out` that is too small.
pub fn decode(text: &[u8], out: &mut [u8]) -> Option<usize> {
    if !text.len().is_multiple_of(4) {
        return None;
    }
    let quads = text.len() / 4;
    let mut length = 0;
    for (index, quad) in text.chunks_exact(4).enumerate() {
        let padding = quad.iter().rev().take_while(|byte| **byte == PAD).count();
        if padding > 2 || (padding > 0 && index + 1 != quads) {
            return None;
        }
        let mut group = 0u32;
        for &character in &quad[..4 - padding] {
            group = group << 6 | value(character)?;
        }
        group <<= 6 * padding as u32;
        let bytes = 3 - padding;
        // The bits the padding leaves unused must be zero.
        if group & ((1u32 << (8 * padding)) - 1) != 0 {
            return None;
        }
        let target = out.get_mut(length..length + bytes)?;
        target.copy_from_slice(&group.to_be_bytes()[1..=bytes]);
        length += bytes;
    }
    Some(length)
}
