//! Waiting: the one clock operation that needs an executor.

/// A timer.
///
/// `async` for a single-threaded executor, like [`crate::Wifi`].
#[allow(async_fn_in_trait)]
pub trait Timer {
    /// Waits `ms` milliseconds.
    async fn sleep_ms(&mut self, ms: u32);
}
