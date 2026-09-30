//! The setup button (Story 4.2): press classification at its thresholds, and the wake plan.

mod common;

use coldframe_hal::GpioError;
use coldframe_hal::mock::{MockButton, MockTimer};
use coldframe_sensing::button::{
    DEBOUNCE_MS, LONG_PRESS_MS, PRESS_POLL_MS, Press, WakePlan, arm_button_wake, classify_press,
    sleep_after, wake_plan,
};
use coldframe_sensing::wake::{MIN_SLEEP_MS, WAKE_PERIOD_MS};
use coldframe_sensing::{WakeCause, boot_id};
use common::{block_on, erased};

use coldframe_sensing::ReservedCounter;
use coldframe_sensing::counter::BOOT_MAGIC;

/// A button held low for `held_ms` from the first read, then released: the classifier reads it
/// every [`PRESS_POLL_MS`], so every read at a time up to and including `held_ms` sees it low.
fn pressed_for(held_ms: u32) -> MockButton {
    let lows = held_ms / PRESS_POLL_MS + 1;
    let levels = (0..lows).map(|_| false).chain([true]);
    MockButton::new(levels)
}

fn classify(button: &mut MockButton) -> (Press, MockTimer) {
    let mut timer = MockTimer::new();
    let press = block_on(classify_press(button, &mut timer));
    (press, timer)
}

#[test]
fn the_thresholds_are_the_spec_values() {
    assert_eq!(PRESS_POLL_MS, 10);
    assert_eq!(DEBOUNCE_MS, 50);
    assert_eq!(LONG_PRESS_MS, 3_000);
}

#[test]
fn a_press_under_the_debounce_is_spurious() {
    assert_eq!(classify(&mut pressed_for(49)).0, Press::Spurious);
    assert_eq!(classify(&mut pressed_for(0)).0, Press::Spurious);
}

#[test]
fn a_press_of_the_debounce_is_short() {
    assert_eq!(classify(&mut pressed_for(50)).0, Press::Short);
}

#[test]
fn a_press_just_under_the_long_threshold_is_short() {
    let (press, timer) = classify(&mut pressed_for(2_990));
    assert_eq!(press, Press::Short);
    // It polled until it saw the release.
    assert_eq!(timer.sleeps().iter().sum::<u32>(), 3_000);
}

#[test]
fn a_press_of_the_long_threshold_is_long() {
    assert_eq!(classify(&mut pressed_for(3_000)).0, Press::Long);
}

#[test]
fn long_is_returned_without_waiting_for_release() {
    // Held for ever: the last scripted level (low) repeats.
    let mut button = MockButton::new([false]);
    let (press, timer) = classify(&mut button);
    assert_eq!(press, Press::Long);
    // Reads at 0, 10, …, 3 000 ms: the threshold read returns at once.
    assert_eq!(button.reads(), 301);
    assert!(timer.sleeps().iter().all(|&ms| ms == PRESS_POLL_MS));
    assert_eq!(timer.sleeps().iter().sum::<u32>(), LONG_PRESS_MS);
}

#[test]
fn a_button_already_released_is_spurious() {
    let mut button = MockButton::new([true]);
    let (press, timer) = classify(&mut button);
    assert_eq!(press, Press::Spurious);
    assert_eq!(button.reads(), 1);
    assert!(timer.sleeps().is_empty());
}

#[test]
fn a_read_error_is_spurious() {
    let mut button = MockButton::with_reads([Err(GpioError::Hardware)]);
    assert_eq!(classify(&mut button).0, Press::Spurious);

    // Also mid-press, after the debounce, and at what would have been the long threshold.
    let mut reads: Vec<Result<bool, GpioError>> = (0..10).map(|_| Ok(false)).collect();
    reads.push(Err(GpioError::Hardware));
    assert_eq!(
        classify(&mut MockButton::with_reads(reads)).0,
        Press::Spurious
    );
    let mut reads: Vec<Result<bool, GpioError>> = (0..300).map(|_| Ok(false)).collect();
    reads.push(Err(GpioError::Hardware));
    assert_eq!(
        classify(&mut MockButton::with_reads(reads)).0,
        Press::Spurious
    );
}

#[test]
fn the_wake_plan_table() {
    use Press::{Long, Short, Spurious};
    use WakeCause::{Button, ColdBoot, Timer};
    let table = [
        (Button, Some(Long), WakePlan::Setup),
        (Button, Some(Short), WakePlan::ReportNow),
        (Button, Some(Spurious), WakePlan::SleepAgain),
        (Button, None, WakePlan::SleepAgain),
        (Timer, None, WakePlan::Measure),
        (ColdBoot, None, WakePlan::Measure),
        // Only a button wake is classified; a press on any other wake is ignored.
        (Timer, Some(Long), WakePlan::Measure),
        (ColdBoot, Some(Long), WakePlan::Measure),
        (Timer, Some(Short), WakePlan::Measure),
        (ColdBoot, Some(Spurious), WakePlan::Measure),
    ];
    for (cause, press, plan) in table {
        assert_eq!(wake_plan(cause, press), plan, "{cause:?} {press:?}");
    }
}

#[test]
fn setup_needs_a_long_press() {
    for cause in [WakeCause::Button, WakeCause::Timer, WakeCause::ColdBoot] {
        for press in [None, Some(Press::Spurious), Some(Press::Short)] {
            assert_ne!(wake_plan(cause, press), WakePlan::Setup);
        }
    }
}

#[test]
fn a_button_wake_keeps_the_boot_id_like_a_timer_wake() {
    let mut boots = ReservedCounter::new(erased(), BOOT_MAGIC);
    let cold = boot_id(&mut boots, WakeCause::ColdBoot).unwrap();
    assert_eq!(boot_id(&mut boots, WakeCause::Button).unwrap(), cold);
    assert_eq!(boot_id(&mut boots, WakeCause::Timer).unwrap(), cold);

    // A button wake with an erased boot counter reserves, like a timer wake: never boot ID 0.
    let mut fresh = ReservedCounter::new(erased(), BOOT_MAGIC);
    assert_ne!(boot_id(&mut fresh, WakeCause::Button).unwrap(), 0);
}

#[test]
fn a_stuck_or_unreadable_button_is_never_armed_as_a_wakeup() {
    // Released: the button may wake the Node.
    assert!(arm_button_wake(&mut MockButton::new([true])));
    // Still held at sleep: timer wake only, so a stuck button never re-wakes the Node in a loop.
    assert!(!arm_button_wake(&mut MockButton::new([false])));
    // Unreadable: timer wake only, the safe choice.
    assert!(!arm_button_wake(&mut MockButton::with_reads([Err(
        GpioError::Hardware
    )])));
}

#[test]
fn only_a_scheduled_measurement_sleeps_the_rest_of_its_period() {
    // A scheduled wake sleeps what `run_wake` measured: the rest of its period.
    assert_eq!(sleep_after(WakePlan::Measure, 897_500), 897_500);
    assert_eq!(sleep_after(WakePlan::Measure, MIN_SLEEP_MS), MIN_SLEEP_MS);
    // A press restarts the schedule: a full period after report-now, setup mode or a bounce,
    // whatever the wake measured (a bounce measures nothing and passes 0).
    for plan in [WakePlan::ReportNow, WakePlan::Setup, WakePlan::SleepAgain] {
        assert_eq!(sleep_after(plan, 897_500), WAKE_PERIOD_MS, "{plan:?}");
        assert_eq!(sleep_after(plan, MIN_SLEEP_MS), WAKE_PERIOD_MS, "{plan:?}");
        assert_eq!(sleep_after(plan, 0), WAKE_PERIOD_MS, "{plan:?}");
    }
}
