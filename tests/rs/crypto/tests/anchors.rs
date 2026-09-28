//! The primitives coldframe-crypto builds on reproduce the RFC vectors in `vectors.json`
//! (copied verbatim from RFC 5869, RFC 7748, RFC 8439 and RFC 9180).

mod common;

use coldframe_crypto::{aead, hpke};
use common::{array, bytes, encode, text, vectors};
use hkdf::Hkdf;
use sha2::Sha256;

#[test]
fn rfc5869_hkdf_sha256_test_case_1() {
    let all = vectors();
    let case = &all["anchors"]["rfc5869TestCase1"];
    let (prk, hkdf) = Hkdf::<Sha256>::extract(Some(&bytes(case, "salt")), &bytes(case, "ikm"));
    assert_eq!(encode(&prk), text(case, "prk"));
    let mut okm = vec![0u8; usize::try_from(case["length"].as_u64().unwrap()).unwrap()];
    hkdf.expand(&bytes(case, "info"), &mut okm).unwrap();
    assert_eq!(encode(&okm), text(case, "okm"));
}

#[test]
fn rfc7748_x25519() {
    let all = vectors();
    let case = &all["anchors"]["rfc7748Section61"];
    let alice: [u8; 32] = array(case, "alicePrivate");
    let bob: [u8; 32] = array(case, "bobPrivate");
    assert_eq!(encode(&hpke::public_key(&alice)), text(case, "alicePublic"));
    assert_eq!(encode(&hpke::public_key(&bob)), text(case, "bobPublic"));
    let shared = hpke::diffie_hellman(&alice, &array(case, "bobPublic")).unwrap();
    assert_eq!(encode(&shared), text(case, "sharedSecret"));
    let shared = hpke::diffie_hellman(&bob, &array(case, "alicePublic")).unwrap();
    assert_eq!(encode(&shared), text(case, "sharedSecret"));
}

#[test]
fn rfc8439_chacha20_poly1305() {
    let all = vectors();
    let case = &all["anchors"]["rfc8439Section282"];
    let plaintext = bytes(case, "plaintext");
    let mut sealed = vec![0u8; plaintext.len() + 16];
    let length = aead::seal(
        &array(case, "key"),
        &array(case, "nonce"),
        &bytes(case, "aad"),
        &plaintext,
        &mut sealed,
    )
    .unwrap();
    assert_eq!(
        encode(&sealed[..length]),
        format!("{}{}", text(case, "ciphertext"), text(case, "tag"))
    );
}

#[test]
fn rfc9180_a21_base_mode_first_encryption() {
    let all = vectors();
    let case = &all["anchors"]["rfc9180A21"];
    assert_eq!(case["kemId"], 32);
    assert_eq!(case["kdfId"], 1);
    assert_eq!(case["aeadId"], 3);
    let (sk_e, pk_e) = hpke::derive_key_pair(&bytes(case, "ikmE"));
    assert_eq!(encode(&sk_e), text(case, "skEm"));
    assert_eq!(encode(&pk_e), text(case, "pkEm"));

    let plaintext = bytes(case, "pt");
    let mut ciphertext = vec![0u8; plaintext.len() + 16];
    let (enc, length) = hpke::seal_base(
        &array(case, "pkRm"),
        &bytes(case, "ikmE"),
        &bytes(case, "info"),
        &bytes(case, "aad"),
        &plaintext,
        &mut ciphertext,
    )
    .unwrap();
    assert_eq!(encode(&enc), text(case, "enc"));
    assert_eq!(encode(&ciphertext[..length]), text(case, "ct"));

    let mut opened = vec![0u8; plaintext.len()];
    hpke::open_base(
        &array(case, "skRm"),
        &enc,
        &bytes(case, "info"),
        &bytes(case, "aad"),
        &ciphertext[..length],
        &mut opened,
    )
    .unwrap();
    assert_eq!(opened, plaintext);
}
