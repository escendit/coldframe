//! [`Timer`] over embassy-time.

use coldframe_hal::Timer;

/// The embassy-time timer.
pub struct BoardTimer;

impl Timer for BoardTimer {
    async fn sleep_ms(&mut self, ms: u32) {
        embassy_time::Timer::after_millis(u64::from(ms)).await;
    }
}
