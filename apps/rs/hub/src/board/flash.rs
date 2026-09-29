//! [`Flash`] over the `cf_ident` partition, through esp-storage (dev mode only).

use coldframe_hal::{Flash, FlashError};
use esp_bootloader_esp_idf::partitions::{
    self, FlashRegion, FlashStorage, PARTITION_TABLE_MAX_LEN, RawPartitionType,
};
use esp_hal::peripherals::FLASH;
use static_cell::StaticCell;

/// Label of the dev-mode identity partition in `partitions.csv`.
pub const IDENTITY_PARTITION: &str = "cf_ident";

/// Data subtype of the identity partition in `partitions.csv`.
/// `undefined` (0x06): espflash only parses the named ESP-IDF data subtypes, not custom ones.
const IDENTITY_SUBTYPE: u8 = 0x06;

const SECTOR_SIZE: u32 = FlashStorage::SECTOR_SIZE;

/// Why the identity partition could not be opened.
#[derive(Clone, Copy, Debug)]
pub enum PartitionError {
    /// The partition table could not be read.
    Table,
    /// No data partition labelled `cf_ident` with subtype `undefined`.
    Missing,
}

impl core::fmt::Display for PartitionError {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        f.write_str(match self {
            Self::Table => "partition table unreadable",
            Self::Missing => "no cf_ident data partition (subtype undefined)",
        })
    }
}

/// The `cf_ident` flash partition, addressed from 0.
pub struct BoardFlash {
    region: FlashRegion<'static, 'static>,
}

impl BoardFlash {
    /// Opens the `cf_ident` partition. Call once: it takes the flash for the program's lifetime.
    ///
    /// # Errors
    ///
    /// [`PartitionError`] when the table is unreadable or lacks the partition.
    pub fn open(flash: FLASH<'static>) -> Result<Self, PartitionError> {
        static STORAGE: StaticCell<FlashStorage<'static>> = StaticCell::new();
        let storage = STORAGE.init(FlashStorage::new(flash));

        let mut table = [0u8; PARTITION_TABLE_MAX_LEN];
        let entry = partitions::read_partition_table(storage, &mut table)
            .map_err(|_| PartitionError::Table)?
            .iter()
            .find(|entry| {
                entry.label_as_str() == IDENTITY_PARTITION
                    && entry.raw_type() == RawPartitionType::Data as u8
                    && entry.raw_subtype() == IDENTITY_SUBTYPE
            })
            .ok_or(PartitionError::Missing)?;
        Ok(Self {
            region: entry.as_flash_region(storage),
        })
    }
}

fn storage_error(error: partitions::Error) -> FlashError {
    match error {
        partitions::Error::OutOfBounds => FlashError::OutOfBounds,
        _ => FlashError::Storage,
    }
}

impl Flash for BoardFlash {
    fn capacity(&self) -> usize {
        self.region.capacity()
    }

    fn read(&mut self, offset: u32, buffer: &mut [u8]) -> Result<(), FlashError> {
        self.region.read(offset, buffer).map_err(storage_error)
    }

    fn write(&mut self, offset: u32, data: &[u8]) -> Result<(), FlashError> {
        // esp-storage writes by read-erase-rewrite of each sector touched. Over an erased range,
        // which is the only place the identity code writes, that equals a NOR write.
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
