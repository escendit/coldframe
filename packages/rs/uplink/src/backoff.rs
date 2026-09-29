//! Reconnect backoff: 1 s, doubling per failure, capped at 60 s, reset on success.

use crate::{BACKOFF_INITIAL_MS, BACKOFF_MAX_MS};

/// The wait before the next attempt after a failure.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Backoff {
    next_ms: u32,
}

impl Default for Backoff {
    fn default() -> Self {
        Self::new()
    }
}

impl Backoff {
    /// A backoff whose first wait is [`BACKOFF_INITIAL_MS`].
    #[must_use]
    pub const fn new() -> Self {
        Self {
            next_ms: BACKOFF_INITIAL_MS,
        }
    }

    /// Records a failure; returns how long to wait before the next attempt: 1 000, 2 000,
    /// 4 000 … ms, never more than [`BACKOFF_MAX_MS`].
    pub fn fail(&mut self) -> u32 {
        let wait = self.next_ms;
        self.next_ms = self.next_ms.saturating_mul(2).min(BACKOFF_MAX_MS);
        wait
    }

    /// Records a success: the next failure waits [`BACKOFF_INITIAL_MS`] again.
    pub fn reset(&mut self) {
        self.next_ms = BACKOFF_INITIAL_MS;
    }
}
