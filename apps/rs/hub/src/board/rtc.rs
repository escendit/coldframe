//! [`Rtc`] over the ESP32-S3 RTC, which is also the wall clock mbedTLS checks certificate
//! validity dates against (AD-11, AD-13, H-2).
//!
//! Uptime comes from embassy-time. Wall-clock time is `None` until it is set on this boot (by SNTP,
//! then by each heartbeat's `serverTime`): until then the RTC reads 1970, so mbedTLS fails closed
//! (every certificate looks not yet valid) and no TLS connection can succeed without a clock.

use core::sync::atomic::{AtomicBool, Ordering};

use coldframe_hal::{Rtc, RtcError};
use esp_hal::peripherals::RTC_TIMER;
use esp_hal::rtc_cntl::Rtc as EspRtc;
use mbedtls_rs::sys::hook::backend::embassy::timer::EmbassyTimer;
use mbedtls_rs::sys::hook::backend::esp::wall_clock::EspRtcWallClock;
use static_cell::StaticCell;

/// Whether the wall clock has been set on this boot.
static SET: AtomicBool = AtomicBool::new(false);

/// The RTC, shared with the mbedTLS wall-clock hook.
#[derive(Clone, Copy)]
pub struct BoardRtc {
    rtc: &'static EspRtc<'static>,
}

impl BoardRtc {
    /// Takes the RTC and installs it (and the embassy-time timer) as mbedTLS's clocks. Call once,
    /// before any TLS.
    pub fn install(rtc_timer: RTC_TIMER<'static>) -> Self {
        static RTC: StaticCell<EspRtc<'static>> = StaticCell::new();
        static WALL_CLOCK: StaticCell<EspRtcWallClock<'static, &'static EspRtc<'static>>> =
            StaticCell::new();
        static TIMER: EmbassyTimer = EmbassyTimer;

        let rtc: &'static EspRtc<'static> = RTC.init(EspRtc::new(rtc_timer));
        let wall_clock: &'static EspRtcWallClock<'static, &'static EspRtc<'static>> =
            WALL_CLOCK.init(EspRtcWallClock::new(rtc));
        // SAFETY: installed once, at boot, before any mbedTLS call; both hooks are 'static and
        // never removed.
        #[allow(unsafe_code, reason = "the mbedTLS clock hooks are global C state")]
        unsafe {
            mbedtls_rs::sys::hook::timer::hook_timer(Some(&TIMER));
            mbedtls_rs::sys::hook::wall_clock::hook_wall_clock(Some(wall_clock));
        }
        Self { rtc }
    }
}

impl Rtc for BoardRtc {
    fn uptime_millis(&self) -> u64 {
        embassy_time::Instant::now().as_millis()
    }

    fn unix_time_millis(&self) -> Option<u64> {
        SET.load(Ordering::Acquire)
            .then(|| self.rtc.current_time_us() / 1_000)
    }

    fn set_unix_time_millis(&mut self, unix_millis: u64) -> Result<(), RtcError> {
        let micros = unix_millis.checked_mul(1_000).ok_or(RtcError::Hardware)?;
        self.rtc.set_current_time_us(micros);
        SET.store(true, Ordering::Release);
        Ok(())
    }
}
