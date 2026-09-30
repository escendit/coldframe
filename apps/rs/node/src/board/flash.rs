//! [`Flash`] over the Coldframe data partitions, through esp-storage.
//!
//! One [`FlashStorage`] lives in a `StaticCell`; [`BoardStorage::partition`] opens a partition
//! of it by label. A partition borrows the storage, so `cf_ident` (dev mode), `cf_boot` and
//! `cf_seq` are used one after the other. Copied from the Hub (apps/rs/hub) with the Node's labels.
//!
//! Nothing here touches the ESP-IDF `nvs` partition.
//!
//! Unlike the Hub's adapter, a word-aligned write is a plain NOR program of the erased bytes, not
//! esp-storage's read-erase-rewrite of the whole sector. The counters (`coldframe-sensing`) rely
//! on it: a power loss during a read-erase-rewrite would wipe every record of the active sector
//! and roll the stored ceiling back. Only the dev-mode identity record (41 bytes, once, onto an
//! erased partition) takes the read-erase-rewrite path.

use coldframe_hal::{Flash, FlashError};
use embedded_storage::nor_flash::NorFlash;
use esp_bootloader_esp_idf::partitions::{
    self, FlashRegion, FlashStorage, PARTITION_TABLE_MAX_LEN, RawPartitionType,
};
use esp_hal::peripherals::FLASH;
use static_cell::StaticCell;

/// Label of the dev-mode identity partition in `partitions.csv`.
#[cfg(feature = "dev-mode")]
pub const IDENTITY_PARTITION: &str = "cf_ident";

/// Label of the `reading_seq` counter partition in `partitions.csv`.
pub const SEQ_PARTITION: &str = "cf_seq";

/// Label of the boot counter partition in `partitions.csv`.
pub const BOOT_PARTITION: &str = "cf_boot";

/// Data subtype of the Coldframe partitions in `partitions.csv`.
/// `undefined` (0x06): espflash only parses the named ESP-IDF data subtypes, not custom ones.
const DATA_SUBTYPE: u8 = 0x06;

const SECTOR_SIZE: u32 = FlashStorage::SECTOR_SIZE;

/// Alignment of a plain NOR write, offset and length.
const WORD_SIZE: u32 = 4;

/// Why a partition could not be opened.
#[derive(Clone, Copy, Debug)]
pub enum PartitionError {
    /// The partition table could not be read.
    Table,
    /// No data partition with the label and subtype `undefined`.
    Missing,
}

impl core::fmt::Display for PartitionError {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        f.write_str(match self {
            Self::Table => "partition table unreadable",
            Self::Missing => "Coldframe data partition (subtype undefined) missing",
        })
    }
}

/// The SPI flash, shared by the Coldframe partitions.
pub struct BoardStorage {
    storage: &'static mut FlashStorage<'static>,
}

impl BoardStorage {
    /// Takes the flash. Call once: it holds the flash for the program's lifetime.
    pub fn new(flash: FLASH<'static>) -> Self {
        static STORAGE: StaticCell<FlashStorage<'static>> = StaticCell::new();
        Self {
            storage: STORAGE.init(FlashStorage::new(flash)),
        }
    }

    /// Opens the data partition `label` (subtype `undefined`).
    ///
    /// # Errors
    ///
    /// [`PartitionError`] when the table is unreadable or lacks the partition.
    pub fn partition(&mut self, label: &str) -> Result<BoardFlash<'_>, PartitionError> {
        let mut table = [0u8; PARTITION_TABLE_MAX_LEN];
        let entry = partitions::read_partition_table(self.storage, &mut table)
            .map_err(|_| PartitionError::Table)?
            .iter()
            .find(|entry| {
                entry.label_as_str() == label
                    && entry.raw_type() == RawPartitionType::Data as u8
                    && entry.raw_subtype() == DATA_SUBTYPE
            })
            .ok_or(PartitionError::Missing)?;
        Ok(BoardFlash {
            region: entry.as_flash_region(self.storage),
        })
    }
}

/// One partition, addressed from 0.
pub struct BoardFlash<'a> {
    region: FlashRegion<'a, 'static>,
}

fn storage_error(error: partitions::Error) -> FlashError {
    match error {
        partitions::Error::OutOfBounds => FlashError::OutOfBounds,
        _ => FlashError::Storage,
    }
}

impl Flash for BoardFlash<'_> {
    fn capacity(&self) -> usize {
        self.region.capacity()
    }

    fn read(&mut self, offset: u32, buffer: &mut [u8]) -> Result<(), FlashError> {
        self.region.read(offset, buffer).map_err(storage_error)
    }

    fn write(&mut self, offset: u32, data: &[u8]) -> Result<(), FlashError> {
        let aligned =
            offset.is_multiple_of(WORD_SIZE) && data.len().is_multiple_of(WORD_SIZE as usize);
        if aligned {
            // Programs the erased bytes only; the rest of the sector is never touched.
            let mut nor = self.region.as_nor_flash().map_err(storage_error)?;
            return nor.write(offset, data).map_err(storage_error);
        }
        // esp-storage writes by read-erase-rewrite of each sector touched. Over an erased range,
        // which is the only place the dev-mode identity writes, that equals a NOR write.
        self.region.write(offset, data).map_err(storage_error)
    }

    fn erase(&mut self, offset: u32, length: u32) -> Result<(), FlashError> {
        if !offset.is_multiple_of(SECTOR_SIZE) || !length.is_multiple_of(SECTOR_SIZE) {
            return Err(FlashError::Unaligned);
        }
        let end = offset.checked_add(length).ok_or(FlashError::OutOfBounds)?;
        self.region.erase(offset, end).map_err(storage_error)
    }
}
