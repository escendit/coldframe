//! Frames over BLE payloads: `header(1) ‖ fragment`.
//!
//! A frame (an encoded `SessionHello`, `SessionHelloReply` or `SealedSetupMessage`) is split
//! into fragments of at most `max_payload − 1` bytes (ATT MTU − 4). Each carries a header byte:
//! [`HEADER_LAST`] on the frame's last fragment, [`HEADER_MORE`] on the others.

use core::fmt;

use crate::MAX_FRAME;

/// Header of a fragment that more fragments of the same frame follow.
pub const HEADER_MORE: u8 = 0x00;

/// Header of the last fragment of a frame.
pub const HEADER_LAST: u8 = 0x01;

/// Why a payload could not be reassembled. The partial frame is dropped.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum FrameError {
    /// A payload without a header byte.
    Empty,
    /// A header byte other than [`HEADER_MORE`] or [`HEADER_LAST`].
    BadHeader,
    /// The frame grew past [`MAX_FRAME`]. Its remaining fragments are dropped silently up to
    /// and including its last one.
    Oversize,
}

impl fmt::Display for FrameError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::Empty => "empty BLE payload",
            Self::BadHeader => "bad fragment header",
            Self::Oversize => "frame too large",
        })
    }
}

impl core::error::Error for FrameError {}

/// Collects fragments into frames. Deliberately not `Debug`: it holds message bytes.
pub struct Reassembler {
    buffer: [u8; MAX_FRAME],
    length: usize,
    discarding: bool,
}

impl Default for Reassembler {
    fn default() -> Self {
        Self::new()
    }
}

impl Reassembler {
    /// An empty reassembler.
    #[must_use]
    pub const fn new() -> Self {
        Self {
            buffer: [0; MAX_FRAME],
            length: 0,
            discarding: false,
        }
    }

    /// Drops any partial frame.
    pub fn reset(&mut self) {
        self.buffer[..self.length].fill(0);
        self.length = 0;
        self.discarding = false;
    }

    /// Adds one payload; returns the frame it completes, if any.
    ///
    /// # Errors
    ///
    /// See [`FrameError`]; the partial frame is dropped on every error.
    pub fn push(&mut self, payload: &[u8]) -> Result<Option<&[u8]>, FrameError> {
        let Some((&header, fragment)) = payload.split_first() else {
            self.reset();
            return Err(FrameError::Empty);
        };
        let last = match header {
            HEADER_LAST => true,
            HEADER_MORE => false,
            _ => {
                self.reset();
                return Err(FrameError::BadHeader);
            }
        };
        if self.discarding {
            if last {
                self.discarding = false;
            }
            return Ok(None);
        }
        let end = self.length + fragment.len();
        if end > MAX_FRAME {
            self.reset();
            self.discarding = !last;
            return Err(FrameError::Oversize);
        }
        self.buffer[self.length..end].copy_from_slice(fragment);
        self.length = end;
        if !last {
            return Ok(None);
        }
        let length = self.length;
        self.length = 0;
        Ok(Some(&self.buffer[..length]))
    }
}

/// One fragment of a frame.
#[derive(Clone, Copy)]
pub struct Fragment<'a> {
    /// [`HEADER_LAST`] or [`HEADER_MORE`].
    pub header: u8,
    /// The frame bytes it carries.
    pub data: &'a [u8],
}

impl Fragment<'_> {
    /// Writes `header ‖ data` into `out`; returns its length (`data.len() + 1`).
    ///
    /// # Panics
    ///
    /// When `out` is shorter than that; [`fragments`] never makes a fragment longer than the
    /// payload size it was given.
    pub fn write_to(&self, out: &mut [u8]) -> usize {
        out[0] = self.header;
        out[1..=self.data.len()].copy_from_slice(self.data);
        self.data.len() + 1
    }
}

/// Splits `frame` into fragments for payloads of at most `max_payload` bytes (at least 2). An
/// empty frame is one empty last fragment.
pub fn fragments(frame: &[u8], max_payload: usize) -> impl Iterator<Item = Fragment<'_>> {
    let chunk = max_payload.saturating_sub(1).max(1);
    let count = frame.len().div_ceil(chunk).max(1);
    (0..count).map(move |index| {
        let start = index * chunk;
        let end = (start + chunk).min(frame.len());
        Fragment {
            header: if index + 1 == count {
                HEADER_LAST
            } else {
                HEADER_MORE
            },
            data: &frame[start..end],
        }
    })
}
