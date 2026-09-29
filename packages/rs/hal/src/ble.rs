//! The BLE link of the setup service (AD-25).
//!
//! One connection at a time, carrying opaque payloads: every write the app makes to the setup
//! write characteristic arrives through [`SetupLink::receive`], and every [`SetupLink::send`]
//! becomes one notification on the notify characteristic. Framing, the session and the GATT
//! layout above it live in `coldframe-setup`; this trait knows nothing of them.

use core::fmt;

/// Why a link operation failed. Carries no payload.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum LinkError {
    /// The peer disconnected, or no connection is open.
    Disconnected,
    /// Nothing arrived within the timeout.
    Timeout,
    /// The BLE stack failed.
    Transport,
}

impl fmt::Display for LinkError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::Disconnected => "BLE peer disconnected",
            Self::Timeout => "BLE link idle timeout",
            Self::Transport => "BLE transport failure",
        })
    }
}

impl core::error::Error for LinkError {}

/// The BLE setup link: advertise, accept one connection, move payloads, disconnect.
///
/// The methods are `async` for a single-threaded executor (embassy on the Hub, a blocking
/// executor in host tests), so the futures need not be `Send`.
#[allow(async_fn_in_trait)]
pub trait SetupLink {
    /// Advertises the setup service until a central connects, then stops advertising.
    ///
    /// # Errors
    ///
    /// [`LinkError::Transport`] when the stack cannot advertise or accept. The caller treats it as
    /// fatal; an adapter retries transient failures itself.
    async fn accept(&mut self) -> Result<(), LinkError>;

    /// Waits up to `timeout_ms` for the next write payload and copies it into `buffer`; returns
    /// its length. A payload longer than `buffer` is truncated.
    ///
    /// # Errors
    ///
    /// [`LinkError::Timeout`] when nothing arrives in time, [`LinkError::Disconnected`] when the
    /// peer has gone, [`LinkError::Transport`] when the stack fails.
    async fn receive(&mut self, buffer: &mut [u8], timeout_ms: u32) -> Result<usize, LinkError>;

    /// Sends one payload of at most [`SetupLink::max_payload`] bytes as one notification.
    ///
    /// # Errors
    ///
    /// [`LinkError::Disconnected`] or [`LinkError::Transport`].
    async fn send(&mut self, payload: &[u8]) -> Result<(), LinkError>;

    /// The largest payload one write or notification carries on the current connection: the ATT
    /// MTU minus 3.
    fn max_payload(&self) -> usize;

    /// Ends the current connection, if any. Idempotent.
    async fn disconnect(&mut self);
}
