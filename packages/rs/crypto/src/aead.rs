//! ChaCha20-Poly1305 (RFC 8439) over caller-provided buffers: a sealed message is the ciphertext followed by
//! the tag.

use chacha20poly1305::{AeadInOut, ChaCha20Poly1305, Key, KeyInit, Nonce, Tag};

use crate::Error;
use crate::spec::{AEAD_KEY_LENGTH, AEAD_NONCE_LENGTH, AEAD_TAG_LENGTH};

/// Seals `plaintext` into `out`, which needs `plaintext.len() + 16` bytes; returns that length.
pub fn seal(
    key: &[u8; AEAD_KEY_LENGTH],
    nonce: &[u8; AEAD_NONCE_LENGTH],
    aad: &[u8],
    plaintext: &[u8],
    out: &mut [u8],
) -> Result<usize, Error> {
    let length = plaintext.len() + AEAD_TAG_LENGTH;
    let out = out.get_mut(..length).ok_or(Error::InvalidLength)?;
    let (body, tag_out) = out.split_at_mut(plaintext.len());
    body.copy_from_slice(plaintext);
    let cipher = ChaCha20Poly1305::new(&Key::from(*key));
    let tag = cipher
        .encrypt_inout_detached(&Nonce::from(*nonce), aad, body.into())
        .map_err(|_| Error::InvalidLength)?;
    tag_out.copy_from_slice(tag.as_slice());
    Ok(length)
}

/// Opens `sealed` into `out`, which needs `sealed.len() - 16` bytes; returns that length. On
/// failure `out` holds no plaintext.
pub fn open(
    key: &[u8; AEAD_KEY_LENGTH],
    nonce: &[u8; AEAD_NONCE_LENGTH],
    aad: &[u8],
    sealed: &[u8],
    out: &mut [u8],
) -> Result<usize, Error> {
    let length = sealed
        .len()
        .checked_sub(AEAD_TAG_LENGTH)
        .ok_or(Error::AuthenticationFailed)?;
    let out = out.get_mut(..length).ok_or(Error::InvalidLength)?;
    let (body, tag) = sealed.split_at(length);
    out.copy_from_slice(body);
    let tag = Tag::try_from(tag).map_err(|_| Error::AuthenticationFailed)?;
    let cipher = ChaCha20Poly1305::new(&Key::from(*key));
    if cipher
        .decrypt_inout_detached(&Nonce::from(*nonce), aad, (&mut *out).into(), &tag)
        .is_err()
    {
        out.fill(0);
        return Err(Error::AuthenticationFailed);
    }
    Ok(length)
}
