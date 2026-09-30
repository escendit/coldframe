//! Why the Node is running, and deep sleep with an RTC timer wakeup.

use coldframe_sensing::WakeCause;
use esp_hal::peripherals::LPWR;
use esp_hal::rtc_cntl::sleep::{LowPower, RtcSleepConfig};
use esp_hal::rtc_cntl::{SocResetReason, WakeupSource, reset_reason, wakeup_cause};
use esp_hal::system::Cpu;
use esp_hal::time::{Duration, Instant};

/// A timer wake from deep sleep keeps the boot ID; anything else is a cold boot.
pub fn wake_cause() -> WakeCause {
    if wakeup_cause().contains(WakeupSource::Timer) {
        WakeCause::Timer
    } else {
        WakeCause::ColdBoot
    }
}

/// The chip's reset reason, for the log.
pub fn reset_reason_name() -> &'static str {
    match reset_reason(Cpu::ProCpu) {
        Some(SocResetReason::ChipPowerOn) => "power_on",
        Some(SocResetReason::CoreDeepSleep) => "deep_sleep",
        Some(SocResetReason::CoreSw | SocResetReason::CpuSw) => "software",
        Some(_) => "other",
        None => "unknown",
    }
}

/// Deep-sleeps for `ms` milliseconds; the wake is a reset into `main`.
pub fn deep_sleep(lpwr: LPWR<'static>, ms: u32) -> ! {
    let mut low_power = LowPower::new(lpwr);
    low_power.set_wakeup_deadline(Instant::now() + Duration::from_millis(u64::from(ms)));
    low_power.sleep_deep(RtcSleepConfig::deep())
}
