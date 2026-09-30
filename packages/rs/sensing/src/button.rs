//! The setup button (Story 4.2, N-3): what a press asks for, and what this wake does.
//!
//! The button wakes the Node from deep sleep. [`classify_press`] then polls it every
//! [`PRESS_POLL_MS`] and times the press by the last poll that saw it held:
//!
//! - released before [`DEBOUNCE_MS`] (a bounce, or already released): [`Press::Spurious`];
//! - released before [`LONG_PRESS_MS`]: [`Press::Short`], "report now";
//! - still held at [`LONG_PRESS_MS`]: [`Press::Long`], returned at once without waiting for the
//!   release, so the Node enters setup mode while the finger is still on the button;
//! - a pin read error: [`Press::Spurious`].
//!
//! [`wake_plan`] turns the wake cause and the press into a [`WakePlan`]. Only a long press on a
//! button wake yields [`WakePlan::Setup`]: BLE is never started on any other path.
//! [`sleep_after`] then gives the sleep the wake takes: a press restarts the schedule.

use coldframe_hal::{InputPin, Timer};

use crate::wake::{WAKE_PERIOD_MS, WakeCause};

/// How often the button is read while it is held.
pub const PRESS_POLL_MS: u32 = 10;

/// A press released before this is a bounce.
pub const DEBOUNCE_MS: u32 = 50;

/// A press held this long enters setup mode.
pub const LONG_PRESS_MS: u32 = 3_000;

/// The button is active low: a low level means pressed.
const PRESSED_LEVEL_HIGH: bool = false;

/// What a press asks for.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Press {
    /// A bounce, a press already over, or a pin that could not be read: nothing.
    Spurious,
    /// Report now: take and send one Reading at once.
    Short,
    /// Enter BLE setup mode.
    Long,
}

/// What this wake does.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum WakePlan {
    /// The scheduled measurement (a timer wake or a cold boot).
    Measure,
    /// A short press: measure now, then sleep a full period.
    ReportNow,
    /// A long press: run the BLE setup window, then measure, then sleep a full period.
    Setup,
    /// A bounce: no measurement, sleep a full period.
    SleepAgain,
}

/// Times a press of the button, which woke the Node while held; see the module documentation.
pub async fn classify_press(button: &mut impl InputPin, timer: &mut impl Timer) -> Press {
    let mut now_ms: u32 = 0;
    // The time of the last poll that saw the button held.
    let mut held_ms: Option<u32> = None;
    loop {
        match button.is_high() {
            Err(_) => return Press::Spurious,
            Ok(high) if high == PRESSED_LEVEL_HIGH => {
                held_ms = Some(now_ms);
                if now_ms >= LONG_PRESS_MS {
                    return Press::Long;
                }
            }
            Ok(_) => {
                return match held_ms {
                    Some(held) if held >= DEBOUNCE_MS => Press::Short,
                    _ => Press::Spurious,
                };
            }
        }
        timer.sleep_ms(PRESS_POLL_MS).await;
        now_ms += PRESS_POLL_MS;
    }
}

/// Whether the button may be armed as a deep-sleep wakeup: only while it reads released. A button
/// still held (stuck) or unreadable leaves only the timer wake, so it never re-wakes the Node in a
/// loop.
pub fn arm_button_wake(button: &mut impl InputPin) -> bool {
    matches!(button.is_high(), Ok(high) if high != PRESSED_LEVEL_HIGH)
}

/// The plan for a wake: only a button wake is classified, and only a long press enters setup.
#[must_use]
pub fn wake_plan(cause: WakeCause, press: Option<Press>) -> WakePlan {
    match (cause, press) {
        (WakeCause::Button, Some(Press::Long)) => WakePlan::Setup,
        (WakeCause::Button, Some(Press::Short)) => WakePlan::ReportNow,
        (WakeCause::Button, Some(Press::Spurious) | None) => WakePlan::SleepAgain,
        (WakeCause::Timer | WakeCause::ColdBoot, _) => WakePlan::Measure,
    }
}

/// The sleep this wake takes: a scheduled [`WakePlan::Measure`] sleeps `measured_sleep_ms`, the
/// rest of its period as `run_wake` measured it; every press ([`WakePlan::ReportNow`],
/// [`WakePlan::Setup`], [`WakePlan::SleepAgain`]) restarts the schedule with a full
/// [`WAKE_PERIOD_MS`].
#[must_use]
pub fn sleep_after(plan: WakePlan, measured_sleep_ms: u32) -> u32 {
    match plan {
        WakePlan::Measure => measured_sleep_ms,
        WakePlan::ReportNow | WakePlan::Setup | WakePlan::SleepAgain => WAKE_PERIOD_MS,
    }
}
