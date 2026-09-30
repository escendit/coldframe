//! [`Rtc`] over the ESP32-S3 RTC timer.
//!
//! Uptime is the RTC timer's time since power-on: it keeps counting through deep sleep, so the
//! `Unsynced` `uptime_ms` of consecutive wakes of one boot ID is monotonic (AD-11).
//!
//! The wall clock is never set in this story: Story 4.4 sets it from authenticated downlinks
//! only. Until then [`Rtc::unix_time_millis`] is `None` and every Reading is `Unsynced`.

use coldframe_hal::{Rtc, RtcError};
use esp_hal::peripherals::RTC_TIMER;
use esp_hal::rtc_cntl::{Rtc as EspRtc, RwdtStage};
use esp_hal::time::Duration;

/// The RTC.
pub struct BoardRtc {
    rtc: EspRtc<'static>,
}

impl BoardRtc {
    /// Takes the RTC timer.
    pub fn new(rtc_timer: RTC_TIMER<'static>) -> Self {
        Self {
            rtc: EspRtc::new(rtc_timer),
        }
    }

    /// Arms the RTC watchdog: if the wake has not deep-slept within `timeout`, stage 0 resets the
    /// whole chip. It pauses in sleep and is never fed, so it bounds each wake, including a
    /// panic that halts instead of resetting.
    pub fn arm_watchdog(&mut self, timeout: Duration) {
        self.rtc.rwdt.set_timeout(RwdtStage::Stage0, timeout);
        self.rtc.rwdt.enable();
        // Start the count from zero, not from whatever the previous boot left in the RTC domain.
        self.rtc.rwdt.feed();
    }
}

impl Rtc for BoardRtc {
    fn uptime_millis(&self) -> u64 {
        // `as_millis` is u64 here (esp-hal's Duration).
        self.rtc.time_since_power_up().as_millis()
    }

    fn unix_time_millis(&self) -> Option<u64> {
        None
    }

    fn set_unix_time_millis(&mut self, _unix_millis: u64) -> Result<(), RtcError> {
        // Not before Story 4.4: only an authenticated downlink may set the Node's clock.
        Err(RtcError::Hardware)
    }
}
