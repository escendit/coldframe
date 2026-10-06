//! A connectionless datagram radio: ESP-NOW between a Node and a Hub.
//!
//! The radio sends one payload to one MAC address or to everyone on the channel, and hands over
//! what it hears. The result of [`DatagramRadio::send`] is the radio-level status only. It is not
//! delivery: under coexistence a send reported as failed has often arrived (requirement N-1), so
//! callers decide on what they receive back, never on this result.

use core::fmt;

/// Length of a MAC address.
pub const MAC_LENGTH: usize = 6;

/// A MAC address.
pub type MacAddress = [u8; MAC_LENGTH];

/// The broadcast address: every radio on the channel hears the datagram.
pub const BROADCAST: MacAddress = [0xFF; MAC_LENGTH];

/// The largest payload of one datagram (ESP-NOW v1).
pub const DATAGRAM_MAX: usize = 250;

/// Why a radio operation failed. Carries no payload.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum DatagramError {
    /// The channel could not be set.
    Channel,
    /// The radio reported the send as failed. The datagram may have arrived all the same.
    Send,
    /// The payload is longer than [`DATAGRAM_MAX`].
    TooLong,
}

impl fmt::Display for DatagramError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::Channel => "radio channel not set",
            Self::Send => "radio reported a failed send",
            Self::TooLong => "datagram too long",
        })
    }
}

impl core::error::Error for DatagramError {}

/// One received datagram.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Datagram {
    /// The sender's MAC address.
    pub source: MacAddress,
    /// How many payload bytes were written into the caller's buffer.
    pub len: usize,
}

/// A datagram radio.
///
/// `async` for a single-threaded executor, like [`crate::Wifi`].
#[allow(async_fn_in_trait)]
pub trait DatagramRadio {
    /// Tunes the radio to `channel` (1 to 13).
    ///
    /// # Errors
    ///
    /// [`DatagramError::Channel`] when the radio refuses the channel.
    fn set_channel(&mut self, channel: u8) -> Result<(), DatagramError>;

    /// Sends `payload` to `to`, or to everyone on the channel for [`BROADCAST`].
    ///
    /// # Errors
    ///
    /// [`DatagramError::TooLong`], or [`DatagramError::Send`] for the radio-level status, which
    /// is not delivery: see the module documentation.
    async fn send(&mut self, to: &MacAddress, payload: &[u8]) -> Result<(), DatagramError>;

    /// Waits up to `timeout_ms` for a datagram and copies its payload into `buffer`; `None` when
    /// none arrived in time. A payload longer than `buffer` is cut to fit.
    async fn receive(&mut self, buffer: &mut [u8], timeout_ms: u32) -> Option<Datagram>;
}
