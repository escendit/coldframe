//! The Node's transport to the Server through a Hub (Story 4.4), over the `coldframe-hal` traits
//! only (AD-24).
//!
//! A Node measures every 15 minutes and must not lose a Reading while no Hub hears it. Every wake
//! is a reboot, so everything that outlives a wake is in flash.
//!
//! - [`report`]: [`Report`], one wake report as the buffer keeps it and its flash record.
//! - [`buffer`]: [`ReportBuffer`], 96 wake reports (24 h) in flash with plain NOR writes; the
//!   oldest is dropped when it is full, and a report is deleted only by an acknowledgement.
//! - [`link`]: [`LinkState`], what the Node remembers about its link between wakes.
//! - [`frame`]: the `NodeFrame` of a report, its seal, the Node's Specification set and
//!   `spec_hash`, and opening a `Downlink`.
//! - [`transport`]: [`begin`] and [`run`], the transport step of one wake: probe, late
//!   acknowledgement, burst, acknowledgement window, miss count, channel scan and the clock rule.
//!
//! The crate is `no_std` without `alloc`, and it never logs: the firmware logs the [`Outcome`],
//! which carries no Reading, key or sealed payload.

#![cfg_attr(not(test), no_std)]

pub mod buffer;
pub mod frame;
pub mod link;
pub mod report;
pub mod transport;

pub use buffer::{BufferError, ReportBuffer};
pub use link::{Burst, ClockSync, LinkError, LinkState};
pub use report::{BufferedReading, Report};
pub use transport::{
    Buffered, ClockChange, Faults, Hub, Outcome, Partition, Partitions, begin, buffer_only, run,
};

/// Name of this crate.
pub const NAME: &str = env!("CARGO_PKG_NAME");
