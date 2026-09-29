//! The setup code and the provisioning record in `cf_setup`: first, later and provisioned boot,
//! and corrupt records.

use coldframe_hal::mock::{MockFlash, MockRadio, MockTrng};
use coldframe_hal::{FlashError, Radio, TrngError};
use coldframe_setup::code::{ALPHABET, SetupCode};
use coldframe_setup::service::boot;
use coldframe_setup::store::{
    CodeSource, PARTITION_SIZE, PROVISIONING_OFFSET, ProvisioningRecord, ProvisioningState,
    SetupStoreError, load_or_create_code, load_provisioning, store_provisioning,
};

fn trng(bytes: &[u8]) -> MockTrng {
    let mut radio = MockRadio::new();
    radio.enable().unwrap();
    MockTrng::with_bytes(&radio, bytes)
}

#[test]
fn first_boot_generates_stores_and_shows_the_code() {
    let mut flash = MockFlash::new(PARTITION_SIZE);
    let boot = boot(&mut trng(&[0xA5, 0x5A, 0x0F, 0xF0, 0x3C]), &mut flash).unwrap();
    assert_eq!(boot.code_source, CodeSource::Generated);
    assert_eq!(
        boot.code.as_str(),
        SetupCode::from_entropy([0xA5, 0x5A, 0x0F, 0xF0, 0x3C]).as_str()
    );
    assert!(boot.code.as_str().bytes().all(|b| ALPHABET.contains(&b)));
    assert_eq!(boot.code.as_str().len(), 8);
    assert!(boot.shows_code() && boot.advertises() && boot.needs_setup());
    assert!(matches!(
        boot.provisioning,
        ProvisioningState::Unprovisioned
    ));
    // The record: magic, version, the code.
    assert_eq!(&flash.contents()[..5], b"CFPC\x01");
    assert_eq!(&flash.contents()[5..13], boot.code.as_bytes());
    assert_eq!(flash.write_count(), 1);
}

#[test]
fn a_later_unprovisioned_boot_shows_the_same_code_without_writing() {
    let mut flash = MockFlash::new(PARTITION_SIZE);
    let first = boot(&mut trng(&[1, 2, 3, 4, 5]), &mut flash).unwrap();
    let writes = flash.write_count();
    let later = boot(&mut trng(&[9, 9, 9, 9, 9]), &mut flash).unwrap();
    assert_eq!(later.code_source, CodeSource::Stored);
    assert_eq!(later.code.as_str(), first.code.as_str());
    assert_eq!(flash.write_count(), writes);
    assert!(later.shows_code() && later.advertises());
}

#[test]
fn a_provisioned_boot_neither_shows_the_code_nor_advertises() {
    let mut flash = MockFlash::new(PARTITION_SIZE);
    let _ = boot(&mut trng(&[1, 2, 3, 4, 5]), &mut flash).unwrap();
    let record = ProvisioningRecord::new("garden", "not-a-real-pass", "site-1").unwrap();
    store_provisioning(&mut flash, &record).unwrap();

    let boot = boot(&mut trng(&[]), &mut flash).unwrap();
    assert!(!boot.shows_code() && !boot.advertises() && !boot.needs_setup());
    let ProvisioningState::Provisioned(stored) = boot.provisioning else {
        panic!("provisioned");
    };
    assert_eq!(stored.ssid(), "garden");
    assert_eq!(stored.password(), "not-a-real-pass");
    assert_eq!(stored.site_id(), "site-1");
}

#[test]
fn a_corrupt_code_record_halts_and_is_never_regenerated() {
    let mut flash = MockFlash::new(PARTITION_SIZE);
    let _ = boot(&mut trng(&[1, 2, 3, 4, 5]), &mut flash).unwrap();
    flash.contents_mut()[7] ^= 0x01;
    let writes = flash.write_count();
    let erases = flash.erase_count();
    let error = load_or_create_code(&mut trng(&[]), &mut flash).err();
    assert_eq!(error, Some(SetupStoreError::CorruptSetupCode));
    assert_eq!(flash.write_count(), writes);
    assert_eq!(flash.erase_count(), erases);

    // A record with a valid checksum but a character outside the alphabet is corrupt too.
    let mut flash = MockFlash::new(PARTITION_SIZE);
    flash.contents_mut()[..4].copy_from_slice(b"CFPX");
    assert!(matches!(
        boot(&mut trng(&[]), &mut flash),
        Err(SetupStoreError::CorruptSetupCode)
    ));
}

#[test]
fn a_corrupt_provisioning_record_reads_as_unprovisioned() {
    let mut flash = MockFlash::new(PARTITION_SIZE);
    let _ = boot(&mut trng(&[1, 2, 3, 4, 5]), &mut flash).unwrap();
    let record = ProvisioningRecord::new("garden", "not-a-real-pass", "site-1").unwrap();
    store_provisioning(&mut flash, &record).unwrap();
    let at = usize::try_from(PROVISIONING_OFFSET).unwrap() + 8;
    flash.contents_mut()[at] ^= 0x40;

    let boot = boot(&mut trng(&[]), &mut flash).unwrap();
    assert!(matches!(boot.provisioning, ProvisioningState::Corrupt));
    assert!(boot.shows_code() && boot.advertises());
}

#[test]
fn provisioning_records_round_trip_at_their_limits() {
    let mut flash = MockFlash::new(PARTITION_SIZE);
    let ssid = "s".repeat(32);
    let password = "p".repeat(64);
    let site = "0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b";
    store_provisioning(
        &mut flash,
        &ProvisioningRecord::new(&ssid, &password, site).unwrap(),
    )
    .unwrap();
    let ProvisioningState::Provisioned(record) = load_provisioning(&mut flash).unwrap() else {
        panic!("provisioned");
    };
    assert_eq!(
        (record.ssid(), record.password(), record.site_id()),
        (&*ssid, &*password, site)
    );

    // An open network has an empty password.
    store_provisioning(
        &mut flash,
        &ProvisioningRecord::new("cafe", "", "s").unwrap(),
    )
    .unwrap();
    let ProvisioningState::Provisioned(record) = load_provisioning(&mut flash).unwrap() else {
        panic!("provisioned");
    };
    assert_eq!(record.password(), "");

    for (ssid, password, site) in [
        ("", "p", "s"),
        ("s", "p", ""),
        (&*"s".repeat(33), "p", "s"),
        ("s", &*"p".repeat(65), "s"),
        ("s", "p", &*"x".repeat(37)),
    ] {
        assert!(matches!(
            ProvisioningRecord::new(ssid, password, site),
            Err(SetupStoreError::InvalidField)
        ));
    }
}

#[test]
fn the_code_record_leaves_the_provisioning_sector_alone() {
    let mut flash = MockFlash::new(PARTITION_SIZE);
    let _ = boot(&mut trng(&[1, 2, 3, 4, 5]), &mut flash).unwrap();
    let start = usize::try_from(PROVISIONING_OFFSET).unwrap();
    assert!(flash.contents()[start..].iter().all(|&b| b == 0xFF));
}

#[test]
fn hardware_failures_are_reported_without_writing() {
    let mut flash = MockFlash::new(PARTITION_SIZE);
    let mut failing = trng(&[]);
    failing.fail_with(Some(TrngError::Hardware));
    assert!(matches!(
        boot(&mut failing, &mut flash),
        Err(SetupStoreError::Trng(TrngError::Hardware))
    ));
    assert_eq!(flash.write_count(), 0);

    let mut flash = MockFlash::new(PARTITION_SIZE);
    flash.ignore_writes(true);
    assert!(matches!(
        boot(&mut trng(&[1, 2, 3, 4, 5]), &mut flash),
        Err(SetupStoreError::NotVerified)
    ));

    let mut flash = MockFlash::new(PARTITION_SIZE);
    flash.fail_with(Some(FlashError::Storage));
    assert!(matches!(
        boot(&mut trng(&[1, 2, 3, 4, 5]), &mut flash),
        Err(SetupStoreError::Flash(FlashError::Storage))
    ));
}

#[test]
fn store_errors_say_nothing_secret() {
    for error in [
        SetupStoreError::CorruptSetupCode,
        SetupStoreError::NotVerified,
        SetupStoreError::InvalidField,
    ] {
        let text = error.to_string();
        assert!(!text.contains("CFPC") && !text.contains("garden"));
    }
}
