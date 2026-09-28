//! HPKE (RFC 9180) base mode with DHKEM(X25519, HKDF-SHA256), HKDF-SHA256 and ChaCha20Poly1305,
//! composed from the standard primitives and checked against RFC 9180 Appendix A.2.1.
//!
//! Enrolment seals `K_dev` to the Server's enrolment key with `info = "coldframe/enrolment/v1"`
//! and `aad = device_id`. The ephemeral key comes from `ikm_e` through `DeriveKeyPair`, so the
//! firmware passes 32 bytes from its TRNG and the vectors pass fixed ones.

use hkdf::{Hkdf, HkdfExtract};
use sha2::{Digest, Sha256};
use x25519_dalek::{X25519_BASEPOINT_BYTES, x25519};

use crate::keys::{DeviceId, DeviceKeys};
use crate::spec::{
    AEAD_KEY_LENGTH, AEAD_NONCE_LENGTH, AEAD_TAG_LENGTH, DEVICE_KEY_LENGTH, ENROLMENT_INFO,
    HPKE_AEAD_ID, HPKE_ENC_LENGTH, HPKE_KDF_ID, HPKE_KEM_ID, HPKE_LABEL_BASE_NONCE,
    HPKE_LABEL_DKP_PRK, HPKE_LABEL_EAE_PRK, HPKE_LABEL_INFO_HASH, HPKE_LABEL_KEY,
    HPKE_LABEL_PSK_ID_HASH, HPKE_LABEL_SECRET, HPKE_LABEL_SHARED_SECRET, HPKE_LABEL_SK, HPKE_MODE,
    HPKE_VERSION_LABEL, X25519_KEY_LENGTH,
};
use crate::{Error, aead, hex};

/// Length of the sealed `K_dev`: the key plus the tag.
pub const ENROLMENT_CIPHERTEXT_LENGTH: usize = DEVICE_KEY_LENGTH + AEAD_TAG_LENGTH;

const HASH_LENGTH: usize = 32;

/// `"KEM" ‖ I2OSP(kem_id, 2)`.
fn kem_suite() -> [u8; 5] {
    let id = HPKE_KEM_ID.to_be_bytes();
    [b'K', b'E', b'M', id[0], id[1]]
}

/// `"HPKE" ‖ I2OSP(kem_id, 2) ‖ I2OSP(kdf_id, 2) ‖ I2OSP(aead_id, 2)`.
fn hpke_suite() -> [u8; 10] {
    let (kem, kdf, aead) = (
        HPKE_KEM_ID.to_be_bytes(),
        HPKE_KDF_ID.to_be_bytes(),
        HPKE_AEAD_ID.to_be_bytes(),
    );
    [
        b'H', b'P', b'K', b'E', kem[0], kem[1], kdf[0], kdf[1], aead[0], aead[1],
    ]
}

/// `LabeledExtract(salt, label, ikm)`.
fn labeled_extract(suite: &[u8], salt: &[u8], label: &str, ikm: &[u8]) -> [u8; HASH_LENGTH] {
    let mut extract = HkdfExtract::<Sha256>::new(Some(salt));
    extract.input_ikm(HPKE_VERSION_LABEL.as_bytes());
    extract.input_ikm(suite);
    extract.input_ikm(label.as_bytes());
    extract.input_ikm(ikm);
    extract.finalize().0.into()
}

/// `LabeledExpand(prk, label, info, N)`.
fn labeled_expand<const N: usize>(
    suite: &[u8],
    prk: &[u8; HASH_LENGTH],
    label: &str,
    info: &[u8],
) -> [u8; N] {
    let length = u16::try_from(N).expect("HPKE output lengths fit in two bytes");
    let mut out = [0u8; N];
    Hkdf::<Sha256>::from_prk(prk)
        .expect("a SHA-256 PRK is 32 bytes")
        .expand_multi_info(
            &[
                &length.to_be_bytes(),
                HPKE_VERSION_LABEL.as_bytes(),
                suite,
                label.as_bytes(),
                info,
            ],
            &mut out,
        )
        .expect("N is far below 255 * 32");
    out
}

/// The X25519 public key of a raw private key.
#[must_use]
pub fn public_key(private_key: &[u8; X25519_KEY_LENGTH]) -> [u8; X25519_KEY_LENGTH] {
    x25519(*private_key, X25519_BASEPOINT_BYTES)
}

/// X25519 that refuses the all-zero output (RFC 7748 §6.1, RFC 9180 §7.1.4).
pub fn diffie_hellman(
    private_key: &[u8; X25519_KEY_LENGTH],
    public_key: &[u8; X25519_KEY_LENGTH],
) -> Result<[u8; X25519_KEY_LENGTH], Error> {
    let shared = x25519(*private_key, *public_key);
    if shared.iter().fold(0u8, |acc, byte| acc | byte) == 0 {
        return Err(Error::InvalidPublicKey);
    }
    Ok(shared)
}

/// `DeriveKeyPair(ikm)` for DHKEM(X25519): the private and public key.
#[must_use]
pub fn derive_key_pair(ikm: &[u8]) -> ([u8; X25519_KEY_LENGTH], [u8; X25519_KEY_LENGTH]) {
    let suite = kem_suite();
    let dkp_prk = labeled_extract(&suite, &[], HPKE_LABEL_DKP_PRK, ikm);
    let private_key = labeled_expand(&suite, &dkp_prk, HPKE_LABEL_SK, &[]);
    let public = public_key(&private_key);
    (private_key, public)
}

/// The AEAD key and base nonce of a base-mode context.
struct Context {
    key: [u8; AEAD_KEY_LENGTH],
    base_nonce: [u8; AEAD_NONCE_LENGTH],
}

fn key_schedule(shared_secret: &[u8; HASH_LENGTH], info: &[u8]) -> Context {
    let suite = hpke_suite();
    let psk_id_hash = labeled_extract(&suite, &[], HPKE_LABEL_PSK_ID_HASH, &[]);
    let info_hash = labeled_extract(&suite, &[], HPKE_LABEL_INFO_HASH, info);
    let mut context = [0u8; 1 + 2 * HASH_LENGTH];
    context[0] = HPKE_MODE;
    context[1..=HASH_LENGTH].copy_from_slice(&psk_id_hash);
    context[1 + HASH_LENGTH..].copy_from_slice(&info_hash);
    let secret = labeled_extract(&suite, shared_secret, HPKE_LABEL_SECRET, &[]);
    Context {
        key: labeled_expand(&suite, &secret, HPKE_LABEL_KEY, &context),
        base_nonce: labeled_expand(&suite, &secret, HPKE_LABEL_BASE_NONCE, &context),
    }
}

/// `ExtractAndExpand(dh, enc ‖ pkR)`.
fn shared_secret(
    dh: &[u8; X25519_KEY_LENGTH],
    enc: &[u8; HPKE_ENC_LENGTH],
    recipient: &[u8; X25519_KEY_LENGTH],
) -> [u8; HASH_LENGTH] {
    let suite = kem_suite();
    let mut kem_context = [0u8; HPKE_ENC_LENGTH + X25519_KEY_LENGTH];
    kem_context[..HPKE_ENC_LENGTH].copy_from_slice(enc);
    kem_context[HPKE_ENC_LENGTH..].copy_from_slice(recipient);
    let eae_prk = labeled_extract(&suite, &[], HPKE_LABEL_EAE_PRK, dh);
    labeled_expand(&suite, &eae_prk, HPKE_LABEL_SHARED_SECRET, &kem_context)
}

/// Single-shot base-mode `Seal` (sequence 0) to `recipient`. Writes the ciphertext
/// (`plaintext.len() + 16` bytes) into `out` and returns `enc` and that length.
pub fn seal_base(
    recipient: &[u8; X25519_KEY_LENGTH],
    ikm_e: &[u8],
    info: &[u8],
    aad: &[u8],
    plaintext: &[u8],
    out: &mut [u8],
) -> Result<([u8; HPKE_ENC_LENGTH], usize), Error> {
    let (ephemeral, enc) = derive_key_pair(ikm_e);
    let dh = diffie_hellman(&ephemeral, recipient)?;
    let context = key_schedule(&shared_secret(&dh, &enc, recipient), info);
    let length = aead::seal(&context.key, &context.base_nonce, aad, plaintext, out)?;
    Ok((enc, length))
}

/// Single-shot base-mode `Open` (sequence 0) with the recipient's private key.
pub fn open_base(
    recipient_private_key: &[u8; X25519_KEY_LENGTH],
    enc: &[u8; HPKE_ENC_LENGTH],
    info: &[u8],
    aad: &[u8],
    ciphertext: &[u8],
    out: &mut [u8],
) -> Result<usize, Error> {
    let dh = diffie_hellman(recipient_private_key, enc)?;
    let recipient = public_key(recipient_private_key);
    let context = key_schedule(&shared_secret(&dh, enc, &recipient), info);
    aead::open(&context.key, &context.base_nonce, aad, ciphertext, out)
}

/// The sealed enrolment the Hub returns over BLE: `enc` and `K_dev` sealed to the Server.
pub struct SealedEnrolment {
    /// The Device ID, also the HPKE associated data.
    pub device_id: DeviceId,
    /// The HPKE encapsulated key.
    pub enc: [u8; HPKE_ENC_LENGTH],
    /// `K_dev` sealed with the enrolment context.
    pub ciphertext: [u8; ENROLMENT_CIPHERTEXT_LENGTH],
}

/// Seals `K_dev` to the Server's enrolment public key. `ikm_e` is 32 fresh random bytes.
pub fn seal_enrolment(
    server_public_key: &[u8; X25519_KEY_LENGTH],
    ikm_e: &[u8; X25519_KEY_LENGTH],
    keys: &DeviceKeys,
) -> Result<SealedEnrolment, Error> {
    let mut ciphertext = [0u8; ENROLMENT_CIPHERTEXT_LENGTH];
    let (enc, _) = seal_base(
        server_public_key,
        ikm_e,
        ENROLMENT_INFO.as_bytes(),
        keys.device_id.as_bytes(),
        &keys.device_key,
        &mut ciphertext,
    )?;
    Ok(SealedEnrolment {
        device_id: keys.device_id,
        enc,
        ciphertext,
    })
}

/// The fingerprint of an enrolment public key: lowercase hex SHA-256 of its 32 raw bytes.
#[must_use]
pub fn fingerprint(public_key: &[u8; X25519_KEY_LENGTH]) -> [u8; 2 * HASH_LENGTH] {
    let digest: [u8; HASH_LENGTH] = Sha256::digest(public_key).into();
    hex::encode_array(&digest)
}
