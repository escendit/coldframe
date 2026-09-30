//! Why the Node is running, and deep sleep with an RTC timer wakeup and, when armed, the setup
//! button.

use coldframe_sensing::WakeCause;
use esp_hal::peripherals::LPWR;
use esp_hal::rtc_cntl::sleep::{LowPower, RtcSleepConfig};
use esp_hal::rtc_cntl::{SocResetReason, WakeupSource, reset_reason, wakeup_cause};
use esp_hal::system::Cpu;
use esp_hal::time::{Duration, Instant};
use log::warn;

use super::button::BoardButton;

/// A deep-sleep wake by the setup button (`ext0`/`ext1`, whichever path esp-hal gave the pad) or by
/// the timer keeps the boot ID; anything else is a cold boot.
pub fn wake_cause() -> WakeCause {
    let cause = wakeup_cause();
    if cause.contains(WakeupSource::Ext0) || cause.contains(WakeupSource::Ext1) {
        WakeCause::Button
    } else if cause.contains(WakeupSource::Timer) {
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

/// Deep-sleeps for `ms` milliseconds; the wake is a reset into `main`. With `arm_button`, a low
/// level on the setup button also wakes the Node; the caller passes `false` while the button is
/// still held, so a stuck button leaves only the timer wake.
pub fn deep_sleep(lpwr: LPWR<'static>, ms: u32, button: &mut BoardButton, arm_button: bool) -> ! {
    if arm_button && let Err(error) = button.arm_wake() {
        warn!("setup button wake not armed error={error}");
    }
    let mut low_power = LowPower::new(lpwr);
    low_power.set_wakeup_deadline(Instant::now() + Duration::from_millis(u64::from(ms)));
    low_power.sleep_deep(RtcSleepConfig::deep())
}
