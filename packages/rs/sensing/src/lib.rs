//! The Node's wake cycle (Story 4.1), over the `coldframe-hal` traits only (AD-24).
//!
//! A Node wakes every 15 minutes, takes one Reading per Sensor and deep-sleeps again. Every wake
//! is a reboot, so all state that must outlive it lives in flash.
//!
//! - [`button`]: [`classify_press`] times a setup-button press (Story 4.2), and [`wake_plan`]
//!   decides what the wake does.
//! - [`battery`]: the switched-divider scaling and the LiPo discharge curve, giving an
//!   approximate [`BatteryLevel`].
//! - [`env`]: [`env_sample_from`], the one conversion of the BME680's floats to integer units.
//! - [`counter`]: [`ReservedCounter`], a strictly increasing `u64` reserved in flash before any
//!   value is handed out (AD-17). It backs both `reading_seq` and the boot counter.
//! - [`wake`]: [`run_wake`], one measurement with the probe and divider switched on only while
//!   read, one shared `measured_at` (AD-11), and the sleep time to the next wake; [`boot_id`];
//!   [`issue_report_seq`], the `report_seq` every wake report carries.
//!
//! The crate is `no_std` without `alloc`, and it never logs: the firmware logs the
//! [`WakeOutcome`], and only a `dev-mode` build logs Reading values.

#![cfg_attr(not(test), no_std)]

pub mod battery;
pub mod button;
pub mod counter;
pub mod env;
pub mod wake;

pub use battery::{BatteryLevel, Divider};
pub use button::{Press, WakePlan, arm_button_wake, classify_press, sleep_after, wake_plan};
pub use counter::{CounterError, ReservedCounter};
pub use env::env_sample_from;
pub use wake::{
    ChargeStatus, Faults, MeasuredAt, Reading, ReadingValue, Sensors, WakeCause, WakeOutcome,
    WakeReport, boot_id, issue_report_seq, run_wake, sleep_ms,
};

/// Name of this crate.
pub const NAME: &str = env!("CARGO_PKG_NAME");
