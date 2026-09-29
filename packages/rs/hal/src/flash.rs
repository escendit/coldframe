//! A flash region.

use core::fmt;

/// Why a flash operation failed.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum FlashError {
    /// The range lies outside the region.
    OutOfBounds,
    /// An erase range is not aligned to the sector size.
    Unaligned,
    /// The flash chip failed.
    Storage,
}

impl fmt::Display for FlashError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::OutOfBounds => "flash range out of bounds",
            Self::Unaligned => "flash erase range not sector-aligned",
            Self::Storage => "flash storage failure",
        })
    }
}

impl core::error::Error for FlashError {}

/// A flash region, addressed from 0.
///
/// NOR semantics: [`Flash::erase`] sets bytes to `0xFF` sector by sector, and a write can only
/// clear bits. Writers erase first unless the target is known to be erased.
///
/// Hardware adapters may instead read, erase and rewrite each sector a write touches (the ESP32-S3
/// adapter over esp-storage does). Callers must therefore only write to erased ranges and must not
/// rely on the bit-clearing behaviour.
pub trait Flash {
    /// Size of the region in bytes.
    fn capacity(&self) -> usize;

    /// Reads `buffer.len()` bytes at `offset`.
    ///
    /// # Errors
    ///
    /// [`FlashError::OutOfBounds`] or [`FlashError::Storage`].
    fn read(&mut self, offset: u32, buffer: &mut [u8]) -> Result<(), FlashError>;

    /// Writes `data` at `offset`.
    ///
    /// # Errors
    ///
    /// [`FlashError::OutOfBounds`] or [`FlashError::Storage`].
    fn write(&mut self, offset: u32, data: &[u8]) -> Result<(), FlashError>;

    /// Erases `length` bytes at `offset`; both are multiples of the sector size.
    ///
    /// # Errors
    ///
    /// [`FlashError::Unaligned`], [`FlashError::OutOfBounds`] or [`FlashError::Storage`].
    fn erase(&mut self, offset: u32, length: u32) -> Result<(), FlashError>;
}
