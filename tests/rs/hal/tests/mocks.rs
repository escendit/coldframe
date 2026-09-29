//! The mocks keep the hardware contracts that firmware logic relies on.

use coldframe_hal::mock::{
    BurnBehaviour, MOCK_SECTOR_SIZE, MockAdc, MockEfuse, MockFlash, MockHmac, MockPin, MockRadio,
    MockRtc, MockTrng,
};
use coldframe_hal::{
    Adc, Efuse, EfuseError, Flash, FlashError, HmacError, HmacPeripheral, InputPin, KeyBlock,
    KeyPurpose, OutputPin, Radio, Rtc, Trng, TrngError,
};

const KEY: [u8; 32] = [0x42; 32];

#[test]
fn key_purpose_values_match_the_esp32_s3() {
    assert_eq!(KeyPurpose::HmacUp.raw(), 8);
    assert_eq!(KeyPurpose::from_raw(8), KeyPurpose::HmacUp);
    assert_eq!(KeyPurpose::from_raw(0), KeyPurpose::User);
    assert_eq!(KeyPurpose::from_raw(14), KeyPurpose::Unknown(14));
    for raw in 0..16 {
        assert_eq!(KeyPurpose::from_raw(raw).raw(), raw);
    }
}

#[test]
fn key_blocks_round_trip_their_index() {
    for (index, block) in KeyBlock::ALL.into_iter().enumerate() {
        let index = u8::try_from(index).unwrap();
        assert_eq!(block.index(), index);
        assert_eq!(KeyBlock::from_index(index), Some(block));
    }
    assert_eq!(KeyBlock::from_index(6), None);
    assert_eq!(KeyBlock::Key3.to_string(), "KEY3");
}

#[test]
fn a_fresh_efuse_has_six_unused_blocks() {
    let efuse = MockEfuse::new();
    for block in KeyBlock::ALL {
        assert!(efuse.is_unused(block));
        assert_eq!(efuse.key_purpose(block), KeyPurpose::User);
        assert!(!efuse.is_read_protected(block));
    }
    assert_eq!(efuse.burn_count(), 0);
}

#[test]
fn a_burn_sets_purpose_and_protection_and_hides_the_key() {
    let mut efuse = MockEfuse::new();
    efuse
        .burn_key(KeyBlock::Key2, &KEY, KeyPurpose::HmacUp)
        .unwrap();

    assert_eq!(efuse.burn_count(), 1);
    assert_eq!(efuse.key_purpose(KeyBlock::Key2), KeyPurpose::HmacUp);
    assert!(efuse.is_read_protected(KeyBlock::Key2));
    assert!(efuse.is_write_protected(KeyBlock::Key2));
    assert!(!efuse.is_unused(KeyBlock::Key2));
    assert_eq!(efuse.read_key(KeyBlock::Key2), None);
}

#[test]
fn a_user_purpose_burn_stays_readable() {
    let mut efuse = MockEfuse::new();
    efuse
        .burn_key(KeyBlock::Key0, &KEY, KeyPurpose::User)
        .unwrap();
    assert_eq!(efuse.read_key(KeyBlock::Key0), Some(KEY));
    assert!(!efuse.is_unused(KeyBlock::Key0));
}

#[test]
fn a_block_burns_only_once() {
    let mut efuse = MockEfuse::new();
    efuse
        .burn_key(KeyBlock::Key0, &KEY, KeyPurpose::HmacUp)
        .unwrap();
    assert_eq!(
        efuse.burn_key(KeyBlock::Key0, &[0x24; 32], KeyPurpose::HmacUp),
        Err(EfuseError::BlockInUse)
    );
    assert_eq!(efuse.burn_count(), 1);
}

#[test]
fn a_failing_or_ignored_burn_changes_nothing() {
    let mut efuse = MockEfuse::new();
    efuse.set_burn_behaviour(BurnBehaviour::Fail(EfuseError::BurnFailed));
    assert_eq!(
        efuse.burn_key(KeyBlock::Key0, &KEY, KeyPurpose::HmacUp),
        Err(EfuseError::BurnFailed)
    );
    efuse.set_burn_behaviour(BurnBehaviour::SilentlyIgnored);
    assert_eq!(
        efuse.burn_key(KeyBlock::Key0, &KEY, KeyPurpose::HmacUp),
        Ok(())
    );
    assert!(efuse.is_unused(KeyBlock::Key0));
    assert_eq!(efuse.burn_count(), 0);
}

#[test]
fn with_identity_is_a_protected_hmac_up_block() {
    let efuse = MockEfuse::with_identity(KeyBlock::Key4, KEY);
    assert_eq!(efuse.key_purpose(KeyBlock::Key4), KeyPurpose::HmacUp);
    assert!(efuse.is_read_protected(KeyBlock::Key4));
    assert_eq!(efuse.read_key(KeyBlock::Key4), None);
    assert_eq!(efuse.burn_count(), 0);
}

#[test]
fn hmac_computes_hmac_sha256_over_the_block_key() {
    // HMAC-SHA256(key = 32 × 0x0b, msg = "Hi There"), computed independently with Python's
    // `hmac.new(b"\x0b" * 32, b"Hi There", hashlib.sha256)`.
    let efuse = MockEfuse::with_identity(KeyBlock::Key1, [0x0b; 32]);
    let mut hmac = MockHmac::new(&efuse);
    let out = hmac.hmac_sha256(KeyBlock::Key1, b"Hi There").unwrap();
    let expected = "198a607eb44bfbc69903a0f1cf2bbdc5ba0aa3f3d9ae3c1c7a3b1696a0b68cf7";
    let hex: String = out.iter().map(|byte| format!("{byte:02x}")).collect();
    assert_eq!(hex, expected);
    assert_eq!(hmac.call_count(), 1);
}

#[test]
fn hmac_refuses_a_block_without_hmac_up_purpose() {
    let mut efuse = MockEfuse::new();
    let mut hmac = MockHmac::new(&efuse);
    assert_eq!(
        hmac.hmac_sha256(KeyBlock::Key0, b"msg"),
        Err(HmacError::KeyPurposeMismatch)
    );
    efuse
        .burn_key(KeyBlock::Key0, &KEY, KeyPurpose::User)
        .unwrap();
    assert_eq!(
        hmac.hmac_sha256(KeyBlock::Key0, b"msg"),
        Err(HmacError::KeyPurposeMismatch)
    );
}

#[test]
fn hmac_sees_a_burn_made_after_it_was_created() {
    let mut efuse = MockEfuse::new();
    let mut hmac = MockHmac::new(&efuse);
    efuse
        .burn_key(KeyBlock::Key5, &KEY, KeyPurpose::HmacUp)
        .unwrap();
    assert!(hmac.hmac_sha256(KeyBlock::Key5, b"msg").is_ok());
}

#[test]
fn trng_refuses_while_the_radio_is_off() {
    let mut radio = MockRadio::new();
    let mut trng = MockTrng::new(&radio);
    let mut buffer = [0u8; 32];

    assert!(!radio.is_enabled());
    assert_eq!(
        trng.fill(&mut buffer),
        Err(TrngError::EntropySourceDisabled)
    );

    radio.enable().unwrap();
    assert!(radio.is_enabled());
    trng.fill(&mut buffer).unwrap();
    assert_ne!(buffer, [0u8; 32]);
}

#[test]
fn trng_hands_out_scripted_bytes_first() {
    let mut radio = MockRadio::new();
    let mut trng = MockTrng::with_bytes(&radio, &[1, 2, 3]);
    radio.enable().unwrap();
    let mut buffer = [0u8; 2];
    trng.fill(&mut buffer).unwrap();
    assert_eq!(buffer, [1, 2]);
    trng.fill(&mut buffer).unwrap();
    assert_eq!(buffer[0], 3);
}

#[test]
fn flash_starts_erased_and_writes_only_clear_bits() {
    let mut flash = MockFlash::new(MOCK_SECTOR_SIZE as usize);
    assert_eq!(flash.capacity(), 4096);
    assert!(flash.contents().iter().all(|&byte| byte == 0xFF));

    flash.write(0, &[0b1010_1010]).unwrap();
    flash.write(0, &[0b1111_0000]).unwrap();
    let mut byte = [0u8; 1];
    flash.read(0, &mut byte).unwrap();
    assert_eq!(byte, [0b1010_0000]);
    assert_eq!(flash.write_count(), 2);

    flash.erase(0, MOCK_SECTOR_SIZE).unwrap();
    flash.read(0, &mut byte).unwrap();
    assert_eq!(byte, [0xFF]);
    assert_eq!(flash.erase_count(), 1);
}

#[test]
fn flash_rejects_out_of_bounds_and_unaligned_access() {
    let mut flash = MockFlash::new(MOCK_SECTOR_SIZE as usize);
    let mut buffer = [0u8; 8];
    assert_eq!(flash.read(4092, &mut buffer), Err(FlashError::OutOfBounds));
    assert_eq!(flash.write(4095, &[0, 0]), Err(FlashError::OutOfBounds));
    assert_eq!(flash.erase(1, MOCK_SECTOR_SIZE), Err(FlashError::Unaligned));
    assert_eq!(flash.erase(0, 100), Err(FlashError::Unaligned));
    assert_eq!(
        flash.erase(0, 2 * MOCK_SECTOR_SIZE),
        Err(FlashError::OutOfBounds)
    );
    assert_eq!(flash.write_count(), 0);
}

#[test]
fn adc_rtc_and_gpio_mocks_behave() {
    let mut adc = MockAdc::new(3300);
    assert_eq!(adc.read_millivolts(), Ok(3300));

    let mut rtc = MockRtc::new();
    assert_eq!(rtc.unix_time_millis(), None);
    rtc.advance(1_000);
    rtc.set_unix_time_millis(1_700_000_000_000).unwrap();
    rtc.advance(500);
    assert_eq!(rtc.uptime_millis(), 1_500);
    assert_eq!(rtc.unix_time_millis(), Some(1_700_000_000_500));

    let mut pin = MockPin::new();
    assert_eq!(pin.is_high(), Ok(false));
    pin.set_high().unwrap();
    assert_eq!(pin.is_high(), Ok(true));
    pin.set_low().unwrap();
    assert_eq!(pin.is_high(), Ok(false));
}
