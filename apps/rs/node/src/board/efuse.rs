//! [`Efuse`] over esp-hal's eFuse reads and the ESP32-S3 ROM's key programming.
//!
//! esp-hal 1.2.2 can only read eFuses. Burning uses the ROM routine `ets_efuse_write_key`, which
//! applies the Reed-Solomon coding of key blocks and programs data, purpose and protection bits
//! in one call. `linkall.x` links the ROM symbols (esp-rom-sys `esp32s3.rom.ld`).
//!
//! This module holds the only `unsafe` code of the Node. Copied from the Hub (apps/rs/hub); a shared
//! ESP32-S3 board crate is a deferred item.

#![allow(unsafe_code, reason = "calls into the ESP32-S3 ROM eFuse routines")]

use core::ffi::c_void;
use core::sync::atomic::{AtomicBool, Ordering};

use coldframe_hal::efuse::KEY_LENGTH;
use coldframe_hal::{Efuse, EfuseError, KeyBlock, KeyPurpose};
use esp_hal::efuse::{
    KEY_PURPOSE_0, KEY_PURPOSE_1, KEY_PURPOSE_2, KEY_PURPOSE_3, KEY_PURPOSE_4, KEY_PURPOSE_5,
    RD_DIS, read_field_le,
};

/// eFuse block number of `KEY0`; `KEYn` is block `4 + n` (ROM `ets_efuse_block_t`).
const FIRST_KEY_BLOCK: u32 = 4;

// ESP32-S3 ROM, `esp32s3/rom/efuse.h`. The block and purpose enums are C `int`s.
unsafe extern "C" {
    /// Programs `data` into `key_block` with `purpose`, sets its write protection and, for a
    /// protected purpose, its read protection. Returns 0 on success.
    fn ets_efuse_write_key(
        key_block: u32,
        purpose: u32,
        data: *const c_void,
        data_len: usize,
    ) -> i32;
    /// Whether `block` has purpose USER, all-zero data and no protection bits.
    fn ets_efuse_key_block_unused(block: u32) -> bool;
    /// Reloads the eFuse read registers from the fuses. Returns 0 on success.
    fn ets_efuse_read() -> i32;
}

static TAKEN: AtomicBool = AtomicBool::new(false);

/// The chip's six key blocks. At most one exists, so only one owner programs fuses.
pub struct BoardEfuse {
    _private: (),
}

impl BoardEfuse {
    /// The eFuse adapter, or `None` if it was already taken. (esp-hal keeps its `EFUSE`
    /// singleton internal, so this adapter guards itself.)
    pub fn take() -> Option<Self> {
        (!TAKEN.swap(true, Ordering::AcqRel)).then_some(Self { _private: () })
    }
}

fn rom_block(block: KeyBlock) -> u32 {
    FIRST_KEY_BLOCK + u32::from(block.index())
}

impl Efuse for BoardEfuse {
    fn key_purpose(&self, block: KeyBlock) -> KeyPurpose {
        let field = match block {
            KeyBlock::Key0 => KEY_PURPOSE_0,
            KeyBlock::Key1 => KEY_PURPOSE_1,
            KeyBlock::Key2 => KEY_PURPOSE_2,
            KeyBlock::Key3 => KEY_PURPOSE_3,
            KeyBlock::Key4 => KEY_PURPOSE_4,
            KeyBlock::Key5 => KEY_PURPOSE_5,
        };
        KeyPurpose::from_raw(read_field_le::<u8>(field))
    }

    fn is_read_protected(&self, block: KeyBlock) -> bool {
        // RD_DIS is 7 bits: bit n disables reads of KEYn (eFuse block 4 + n), bit 6 SYS_DATA2.
        let rd_dis = read_field_le::<u8>(RD_DIS);
        rd_dis & (1 << block.index()) != 0
    }

    fn is_unused(&self, block: KeyBlock) -> bool {
        // SAFETY: a pure read of the eFuse read registers; `rom_block` is 4..=9, a valid
        // `ets_efuse_block_t` key block.
        unsafe { ets_efuse_key_block_unused(rom_block(block)) }
    }

    fn burn_key(
        &mut self,
        block: KeyBlock,
        key: &[u8; KEY_LENGTH],
        purpose: KeyPurpose,
    ) -> Result<(), EfuseError> {
        if !self.is_unused(block) {
            return Err(EfuseError::BlockInUse);
        }
        // SAFETY: `rom_block` is a key block (4..=9); `purpose` is a 4-bit ROM purpose value;
        // `key` is a live 32-byte buffer that the ROM only reads, and `data_len` is its length.
        // `self` is the only `BoardEfuse`, so no other code programs fuses concurrently.
        let written = unsafe {
            ets_efuse_write_key(
                rom_block(block),
                u32::from(purpose.raw()),
                key.as_ptr().cast::<c_void>(),
                key.len(),
            )
        };
        // SAFETY: reloads the read registers from the fuses; no arguments, no memory access by
        // the caller. Done on failure too, so later reads show whatever the fuses now hold.
        let reloaded = unsafe { ets_efuse_read() };
        if written != 0 || reloaded != 0 {
            return Err(EfuseError::BurnFailed);
        }
        Ok(())
    }
}
