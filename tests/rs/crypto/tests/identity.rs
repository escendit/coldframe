//! Identity provisioning behind the HAL traits (AD-12, AD-24), driven by the `coldframe-hal`
//! mocks: the shared key-hierarchy vectors through the HMAC peripheral, and every row of the
//! Story 3.2 I/O matrix.

mod common;

use coldframe_crypto::DeviceKeys;
use coldframe_crypto::identity::{
    DEV_RECORD_LENGTH, HalError, IdentityError, IdentitySource, Provisioned, device_keys_from_hmac,
    provision_dev, provision_efuse,
};
use coldframe_hal::mock::{
    BurnBehaviour, MOCK_SECTOR_SIZE, MockEfuse, MockFlash, MockHmac, MockRadio, MockTrng,
};
use coldframe_hal::{
    Efuse, EfuseError, FlashError, HmacError, KeyBlock, KeyPurpose, Radio, RadioError, TrngError,
};
use common::{array, encode, list, text, vectors};
use sha2::{Digest, Sha256};

const ROOT: [u8; 32] = [
    0x5a, 0x11, 0x3c, 0x9e, 0x07, 0xd2, 0x48, 0xb6, 0x21, 0xf0, 0x6d, 0x93, 0xae, 0x14, 0x7b, 0xc5,
    0x38, 0x82, 0xe9, 0x50, 0x0f, 0xa7, 0x64, 0x1d, 0xcb, 0x2e, 0x95, 0x46, 0xf8, 0x03, 0xba, 0x71,
];

fn assert_same_keys(actual: &DeviceKeys, expected: &DeviceKeys) {
    assert_eq!(actual.device_key, expected.device_key);
    assert_eq!(actual.seal_key, expected.seal_key);
    assert_eq!(actual.ack_key, expected.ack_key);
    assert_eq!(actual.hub_auth_key, expected.hub_auth_key);
    assert_eq!(actual.device_id, expected.device_id);
}

fn efuse_boot(efuse: &MockEfuse, trng_bytes: &[u8]) -> Result<Provisioned, IdentityError> {
    let mut radio = MockRadio::new();
    let mut trng = MockTrng::with_bytes(&radio, trng_bytes);
    let mut hmac = MockHmac::new(efuse);
    provision_efuse(&mut radio, &mut trng, &mut efuse.clone(), &mut hmac)
}

fn dev_boot(flash: &mut MockFlash, trng_bytes: &[u8]) -> Result<Provisioned, IdentityError> {
    let mut radio = MockRadio::new();
    let mut trng = MockTrng::with_bytes(&radio, trng_bytes);
    provision_dev(&mut radio, &mut trng, flash)
}

fn fill_every_block(efuse: &MockEfuse) {
    for block in KeyBlock::ALL {
        efuse.set_block(block, [0x11; 32], KeyPurpose::User, false, true);
    }
}

// ---------------------------------------------------------------------------------------------
// Shared vectors

#[test]
fn every_key_hierarchy_vector_through_the_hmac_peripheral() {
    let all = vectors();
    let cases = list(&all, "keyHierarchy");
    assert!(!cases.is_empty());
    for case in cases {
        let name = text(case, "name");
        let root: [u8; 32] = array(case, "rootKey");
        for block in KeyBlock::ALL {
            let efuse = MockEfuse::with_identity(block, root);
            let provisioned =
                efuse_boot(&efuse, &[]).unwrap_or_else(|error| panic!("{name}: {error}"));
            let keys = &provisioned.keys;

            assert_eq!(
                provisioned.source,
                IdentitySource::Existing(block),
                "{name}"
            );
            assert_eq!(encode(&keys.device_key), text(case, "deviceKey"), "{name}");
            assert_eq!(encode(&keys.seal_key), text(case, "sealKey"), "{name}");
            assert_eq!(encode(&keys.ack_key), text(case, "ackKey"), "{name}");
            assert_eq!(
                encode(&keys.hub_auth_key),
                text(case, "hubAuthKey"),
                "{name}"
            );
            assert_eq!(
                std::str::from_utf8(&keys.device_id.to_hex()).unwrap(),
                text(case, "deviceId"),
                "{name}"
            );
            assert_eq!(efuse.burn_count(), 0, "{name}");
        }
    }
}

#[test]
fn device_keys_from_hmac_matches_the_software_derivation() {
    let efuse = MockEfuse::with_identity(KeyBlock::Key3, ROOT);
    let keys = device_keys_from_hmac(&mut MockHmac::new(&efuse), KeyBlock::Key3).unwrap();
    assert_same_keys(&keys, &DeviceKeys::from_root_key(&ROOT));
}

#[test]
fn device_keys_from_hmac_refuses_a_block_without_an_identity() {
    let efuse = MockEfuse::new();
    assert!(matches!(
        device_keys_from_hmac(&mut MockHmac::new(&efuse), KeyBlock::Key0),
        Err(IdentityError::Hal(HalError::Hmac(
            HmacError::KeyPurposeMismatch
        )))
    ));
}

// ---------------------------------------------------------------------------------------------
// Release (eFuse) path

#[test]
fn first_boot_burns_the_trng_root_once_and_later_boots_reuse_it() {
    let efuse = MockEfuse::new();

    let first = efuse_boot(&efuse, &ROOT).unwrap();
    assert_eq!(first.source, IdentitySource::Burned(KeyBlock::Key0));
    assert_eq!(efuse.burn_count(), 1);
    assert_eq!(efuse.key_purpose(KeyBlock::Key0), KeyPurpose::HmacUp);
    assert!(efuse.is_read_protected(KeyBlock::Key0));
    assert!(efuse.is_write_protected(KeyBlock::Key0));
    assert_eq!(efuse.read_key(KeyBlock::Key0), None);
    // The burned root is the scripted TRNG output: the software derivation of it agrees.
    assert_same_keys(&first.keys, &DeviceKeys::from_root_key(&ROOT));

    let second = efuse_boot(&efuse, &[0x77; 32]).unwrap();
    assert_eq!(second.source, IdentitySource::Existing(KeyBlock::Key0));
    assert_eq!(efuse.burn_count(), 1);
    assert_same_keys(&second.keys, &first.keys);
}

#[test]
fn first_boot_turns_the_radio_on_before_drawing_the_root() {
    let efuse = MockEfuse::new();
    let mut radio = MockRadio::new();
    let mut trng = MockTrng::with_bytes(&radio, &ROOT);
    assert!(!radio.is_enabled());
    provision_efuse(
        &mut radio,
        &mut trng,
        &mut efuse.clone(),
        &mut MockHmac::new(&efuse),
    )
    .unwrap();
    assert!(radio.is_enabled());
}

#[test]
fn first_boot_skips_used_blocks() {
    let efuse = MockEfuse::new();
    efuse.set_block(
        KeyBlock::Key0,
        [0x11; 32],
        KeyPurpose::XtsAes128Key,
        true,
        true,
    );
    // A half-burned block: data but still purpose User. The chip reports it as used.
    efuse.set_block(KeyBlock::Key1, [0x22; 32], KeyPurpose::User, false, false);

    let provisioned = efuse_boot(&efuse, &ROOT).unwrap();
    assert_eq!(provisioned.source, IdentitySource::Burned(KeyBlock::Key2));
    assert_eq!(efuse.burn_count(), 1);
    assert_same_keys(&provisioned.keys, &DeviceKeys::from_root_key(&ROOT));
}

#[test]
fn two_identity_blocks_are_ambiguous() {
    let efuse = MockEfuse::with_identity(KeyBlock::Key1, ROOT);
    efuse.set_block(KeyBlock::Key4, [0x33; 32], KeyPurpose::HmacUp, true, true);
    let mut hmac = MockHmac::new(&efuse);
    let mut radio = MockRadio::new();
    let mut trng = MockTrng::new(&radio);

    assert!(matches!(
        provision_efuse(&mut radio, &mut trng, &mut efuse.clone(), &mut hmac),
        Err(IdentityError::AmbiguousIdentity)
    ));
    assert_eq!(efuse.burn_count(), 0);
    assert_eq!(hmac.call_count(), 0);
}

#[test]
fn an_unprotected_identity_is_refused() {
    let efuse = MockEfuse::new();
    efuse.set_block(KeyBlock::Key2, ROOT, KeyPurpose::HmacUp, false, true);
    let mut hmac = MockHmac::new(&efuse);
    let mut radio = MockRadio::new();
    let mut trng = MockTrng::new(&radio);

    assert!(matches!(
        provision_efuse(&mut radio, &mut trng, &mut efuse.clone(), &mut hmac),
        Err(IdentityError::IdentityNotProtected)
    ));
    assert_eq!(efuse.burn_count(), 0);
    assert_eq!(hmac.call_count(), 0);
}

#[test]
fn no_free_block_burns_nothing() {
    let efuse = MockEfuse::new();
    fill_every_block(&efuse);
    assert!(matches!(
        efuse_boot(&efuse, &ROOT),
        Err(IdentityError::NoFreeKeyBlock)
    ));
    assert_eq!(efuse.burn_count(), 0);
}

#[test]
fn all_zero_or_all_ones_entropy_burns_nothing() {
    for bad in [[0x00; 32], [0xFF; 32]] {
        let efuse = MockEfuse::new();
        assert!(matches!(
            efuse_boot(&efuse, &bad),
            Err(IdentityError::BadEntropy)
        ));
        assert_eq!(efuse.burn_count(), 0);
        assert!(
            KeyBlock::ALL
                .into_iter()
                .all(|block| efuse.is_unused(block))
        );
    }
}

#[test]
fn an_unconfirmed_burn_is_reported() {
    // A burn that reached the fuses without read protection counts once; one that never reached
    // them (reported Ok, nothing changed) counts zero. Either way only the verification fails.
    for (behaviour, burns) in [
        (BurnBehaviour::SilentlyIgnored, 0),
        (BurnBehaviour::WithoutReadProtection, 1),
    ] {
        let efuse = MockEfuse::new();
        efuse.set_burn_behaviour(behaviour);
        assert!(
            matches!(
                efuse_boot(&efuse, &ROOT),
                Err(IdentityError::BurnNotVerified)
            ),
            "{behaviour:?}"
        );
        assert_eq!(efuse.burn_count(), burns, "{behaviour:?}");
    }
}

#[test]
fn radio_failure_draws_and_burns_nothing() {
    let efuse = MockEfuse::new();
    let mut radio = MockRadio::new();
    radio.fail_with(Some(RadioError::StartFailed));
    let mut trng = MockTrng::with_bytes(&radio, &ROOT);

    assert!(matches!(
        provision_efuse(
            &mut radio,
            &mut trng,
            &mut efuse.clone(),
            &mut MockHmac::new(&efuse)
        ),
        Err(IdentityError::Hal(HalError::Radio(RadioError::StartFailed)))
    ));
    assert_eq!(efuse.burn_count(), 0);
}

#[test]
fn trng_failure_burns_nothing() {
    let efuse = MockEfuse::new();
    let mut radio = MockRadio::new();
    let mut trng = MockTrng::with_bytes(&radio, &ROOT);
    trng.fail_with(Some(TrngError::Hardware));

    assert!(matches!(
        provision_efuse(
            &mut radio,
            &mut trng,
            &mut efuse.clone(),
            &mut MockHmac::new(&efuse)
        ),
        Err(IdentityError::Hal(HalError::Trng(TrngError::Hardware)))
    ));
    assert_eq!(efuse.burn_count(), 0);
}

#[test]
fn efuse_failure_is_wrapped() {
    let efuse = MockEfuse::new();
    efuse.set_burn_behaviour(BurnBehaviour::Fail(EfuseError::BurnFailed));
    assert!(matches!(
        efuse_boot(&efuse, &ROOT),
        Err(IdentityError::Hal(HalError::Efuse(EfuseError::BurnFailed)))
    ));
    assert_eq!(efuse.burn_count(), 0);
}

#[test]
fn hmac_failure_is_wrapped() {
    let efuse = MockEfuse::with_identity(KeyBlock::Key0, ROOT);
    let mut hmac = MockHmac::new(&efuse);
    hmac.fail_with(Some(HmacError::Hardware));
    let mut radio = MockRadio::new();
    let mut trng = MockTrng::new(&radio);

    assert!(matches!(
        provision_efuse(&mut radio, &mut trng, &mut efuse.clone(), &mut hmac),
        Err(IdentityError::Hal(HalError::Hmac(HmacError::Hardware)))
    ));
    assert_eq!(efuse.burn_count(), 0);
}

#[test]
fn errors_carry_no_secret_material() {
    let errors = [
        IdentityError::AmbiguousIdentity,
        IdentityError::IdentityNotProtected,
        IdentityError::NoFreeKeyBlock,
        IdentityError::BadEntropy,
        IdentityError::BurnNotVerified,
        IdentityError::CorruptDevIdentity,
        IdentityError::DevIdentityNotVerified,
        IdentityError::Hal(HalError::Trng(TrngError::EntropySourceDisabled)),
        IdentityError::Hal(HalError::Flash(FlashError::Storage)),
    ];
    let root_hex = encode(&ROOT);
    for error in errors {
        let shown = format!("{error} {error:?}");
        assert!(!shown.is_empty());
        assert!(!shown.contains(&root_hex));
    }
    // Errors are small Copy values: no room for a key.
    assert!(size_of::<IdentityError>() <= 4);
}

// ---------------------------------------------------------------------------------------------
// Dev-mode (flash) path

fn dev_record(root: &[u8; 32]) -> Vec<u8> {
    let mut record = b"CFID\x01".to_vec();
    record.extend_from_slice(root);
    let check = Sha256::digest(&record);
    record.extend_from_slice(&check[..4]);
    record
}

#[test]
fn dev_first_boot_stores_the_trng_root_and_later_boots_reuse_it() {
    let mut flash = MockFlash::new(MOCK_SECTOR_SIZE as usize);

    let first = dev_boot(&mut flash, &ROOT).unwrap();
    assert_eq!(first.source, IdentitySource::DevGenerated);
    assert_eq!(flash.write_count(), 1);
    assert_eq!(DEV_RECORD_LENGTH, 41);
    assert_eq!(
        &flash.contents()[..DEV_RECORD_LENGTH],
        dev_record(&ROOT).as_slice()
    );
    assert_same_keys(&first.keys, &DeviceKeys::from_root_key(&ROOT));

    let second = dev_boot(&mut flash, &[0x77; 32]).unwrap();
    assert_eq!(second.source, IdentitySource::DevStored);
    assert_eq!(flash.write_count(), 1);
    assert_same_keys(&second.keys, &first.keys);
}

#[test]
fn dev_first_boot_turns_the_radio_on() {
    let mut flash = MockFlash::new(MOCK_SECTOR_SIZE as usize);
    let mut radio = MockRadio::new();
    let mut trng = MockTrng::with_bytes(&radio, &ROOT);
    provision_dev(&mut radio, &mut trng, &mut flash).unwrap();
    assert!(radio.is_enabled());
}

#[test]
fn dev_corrupt_record_is_refused_and_not_rewritten() {
    let mut record = dev_record(&ROOT);
    let corruptions: [fn(&mut Vec<u8>); 4] = [
        |record| record[0] = b'X',                      // magic
        |record| record[4] = 2,                         // version
        |record| record[10] ^= 0x01,                    // root, so the check fails
        |record| record[DEV_RECORD_LENGTH - 1] ^= 0x80, // check
    ];
    for corrupt in corruptions {
        let mut flash = MockFlash::new(MOCK_SECTOR_SIZE as usize);
        let mut bad = record.clone();
        corrupt(&mut bad);
        flash.contents_mut()[..DEV_RECORD_LENGTH].copy_from_slice(&bad);

        assert!(matches!(
            dev_boot(&mut flash, &ROOT),
            Err(IdentityError::CorruptDevIdentity)
        ));
        assert_eq!(flash.write_count(), 0);
        assert_eq!(flash.erase_count(), 0);
        assert_eq!(&flash.contents()[..DEV_RECORD_LENGTH], bad.as_slice());
    }
    // A single stray cleared byte in an otherwise erased region is corrupt too.
    record.fill(0xFF);
    record[20] = 0xFE;
    let mut flash = MockFlash::new(MOCK_SECTOR_SIZE as usize);
    flash.contents_mut()[..DEV_RECORD_LENGTH].copy_from_slice(&record);
    assert!(matches!(
        dev_boot(&mut flash, &ROOT),
        Err(IdentityError::CorruptDevIdentity)
    ));
    assert_eq!(flash.write_count(), 0);
}

#[test]
fn dev_bad_entropy_writes_nothing() {
    for bad in [[0x00; 32], [0xFF; 32]] {
        let mut flash = MockFlash::new(MOCK_SECTOR_SIZE as usize);
        assert!(matches!(
            dev_boot(&mut flash, &bad),
            Err(IdentityError::BadEntropy)
        ));
        assert_eq!(flash.write_count(), 0);
    }
}

#[test]
fn dev_flash_failure_is_wrapped() {
    let mut flash = MockFlash::new(MOCK_SECTOR_SIZE as usize);
    flash.fail_with(Some(FlashError::Storage));
    assert!(matches!(
        dev_boot(&mut flash, &ROOT),
        Err(IdentityError::Hal(HalError::Flash(FlashError::Storage)))
    ));
    assert_eq!(flash.write_count(), 0);
}

#[test]
fn dev_write_that_does_not_read_back_is_reported() {
    let mut flash = MockFlash::new(MOCK_SECTOR_SIZE as usize);
    flash.ignore_writes(true);
    assert!(matches!(
        dev_boot(&mut flash, &ROOT),
        Err(IdentityError::DevIdentityNotVerified)
    ));
    assert!(flash.contents().iter().all(|&byte| byte == 0xFF));
}

#[test]
fn dev_radio_failure_writes_nothing() {
    let mut flash = MockFlash::new(MOCK_SECTOR_SIZE as usize);
    let mut radio = MockRadio::new();
    radio.fail_with(Some(RadioError::StartFailed));
    let mut trng = MockTrng::with_bytes(&radio, &ROOT);
    assert!(matches!(
        provision_dev(&mut radio, &mut trng, &mut flash),
        Err(IdentityError::Hal(HalError::Radio(RadioError::StartFailed)))
    ));
    assert_eq!(flash.write_count(), 0);
}
