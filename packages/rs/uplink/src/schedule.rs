//! When the next heartbeat goes out: 45 s ± up to 15 s, the jitter drawn from the TRNG, so
//! Hubs that boot together do not heartbeat together.

use coldframe_hal::Trng;

use crate::{HEARTBEAT_MAX_INTERVAL_MS, HEARTBEAT_MIN_INTERVAL_MS};

/// The heartbeat interval.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct HeartbeatSchedule;

impl HeartbeatSchedule {
    /// The middle of the range, used when the TRNG fails.
    pub const MIDPOINT_MS: u32 =
        u32::midpoint(HEARTBEAT_MIN_INTERVAL_MS, HEARTBEAT_MAX_INTERVAL_MS);

    /// The wait before the next heartbeat: uniform over
    /// [`HEARTBEAT_MIN_INTERVAL_MS`]..=[`HEARTBEAT_MAX_INTERVAL_MS`], or [`Self::MIDPOINT_MS`]
    /// when the TRNG gives nothing.
    pub fn next_delay_ms<T: Trng>(self, trng: &mut T) -> u32 {
        let mut draw = [0u8; 4];
        if trng.fill(&mut draw).is_err() {
            return Self::MIDPOINT_MS;
        }
        let span = HEARTBEAT_MAX_INTERVAL_MS - HEARTBEAT_MIN_INTERVAL_MS + 1;
        HEARTBEAT_MIN_INTERVAL_MS + u32::from_le_bytes(draw) % span
    }
}
