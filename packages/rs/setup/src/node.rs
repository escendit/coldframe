//! The Node's setup window (Story 4.2): after a long press of the setup button, serve BLE setup
//! sessions for at most [`SETUP_WINDOW_MS`].
//!
//! [`run_node_setup`] takes its deadline from the [`Rtc`] (uptime now + the window), so the window
//! is decided here and host-tested, not in a board-level `select`. The deadline is taken when the
//! window starts advertising, not at the press:
//!
//! - it advertises with [`SetupLink::accept_within`], bounded by the rest of the window; nobody
//!   connecting before the deadline ends the window ([`NodeSetupEnd::WindowClosed`]);
//! - it serves one [`Session::node`] per connection, with the Hub's framing and wrong-code rules,
//!   and disconnects after every connection: when the app disconnects, when the session closes
//!   it (a wrong code), after [`NODE_SESSION_IDLE_TIMEOUT_MS`] without a write, or at the
//!   deadline: each receive is bounded by `min(NODE_SESSION_IDLE_TIMEOUT_MS, remaining)`;
//! - once a session has sent its `EnrolmentResponse` and its connection has ended, it returns
//!   [`NodeSetupEnd::Enrolled`]; otherwise it advertises again for the rest of the window.
//!
//! The Node stores nothing here: neither the Site nor the Lot (the Server holds the assignment,
//! AD-18), and no Wi-Fi settings.

use coldframe_crypto::DeviceKeys;
use coldframe_hal::{LinkError, Rtc, SetupLink, Trng};

use crate::MAX_FRAME;
use crate::code::SetupCode;
use crate::framing::Reassembler;
use crate::service::{MAX_PAYLOAD, SetupError, send_frame};
use crate::session::{Action, Session};

/// How long a long press keeps the Node in setup mode: three minutes from the start of the window.
pub const SETUP_WINDOW_MS: u32 = 180_000;

/// A Node connection with no write for this long is dropped, and the window advertises again.
/// The Hub's [`crate::SESSION_IDLE_TIMEOUT_MS`] (300 s) would outlast the whole window.
pub const NODE_SESSION_IDLE_TIMEOUT_MS: u32 = 60_000;

/// How the setup window ended.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum NodeSetupEnd {
    /// A session handed out the sealed `K_dev`, and its connection has ended.
    Enrolled,
    /// The window closed without an enrolment.
    WindowClosed,
}

/// How long the next link call may wait, and whether that wait runs to the deadline.
struct Wait {
    ms: u32,
    to_deadline: bool,
}

/// The rest of the window, bounded by `bound_ms`, or `None` once the deadline has passed.
fn wait_until<R: Rtc>(rtc: &R, deadline: u64, bound_ms: u32) -> Option<Wait> {
    let remaining = deadline.saturating_sub(rtc.uptime_millis());
    if remaining == 0 {
        return None;
    }
    let ms = remaining.min(u64::from(bound_ms));
    Some(Wait {
        // At most `bound_ms`, so it fits.
        ms: u32::try_from(ms).unwrap_or(bound_ms),
        to_deadline: ms == remaining,
    })
}

/// Runs the setup window: see the module documentation.
///
/// `link` is the BLE setup link, `rtc` gives the deadline, `trng` the session and enrolment
/// randomness; `keys` are the Node's keys, `code` its setup code and `firmware_version` goes into
/// `Identity`. `window_ms` is normally [`SETUP_WINDOW_MS`].
///
/// # Errors
///
/// [`SetupError::Link`] when the link cannot advertise or accept.
pub async fn run_node_setup<L, R, T>(
    link: &mut L,
    rtc: &R,
    trng: &mut T,
    keys: &DeviceKeys,
    code: &SetupCode,
    firmware_version: &str,
    window_ms: u32,
) -> Result<NodeSetupEnd, SetupError>
where
    L: SetupLink,
    R: Rtc,
    T: Trng,
{
    let deadline = rtc.uptime_millis().saturating_add(u64::from(window_ms));
    loop {
        // The deadline lies at most `window_ms` ahead, so the whole rest of it fits the bound.
        let Some(wait) = wait_until(rtc, deadline, window_ms) else {
            return Ok(NodeSetupEnd::WindowClosed);
        };
        match link.accept_within(wait.ms).await {
            Ok(()) => {}
            // Waited out the whole window: over, whatever the clock says.
            Err(LinkError::Timeout) if wait.to_deadline => return Ok(NodeSetupEnd::WindowClosed),
            Err(LinkError::Timeout) => continue,
            Err(error) => return Err(SetupError::Link(error)),
        }
        let mut session = Session::node(code, keys, firmware_version);
        let window_over = serve(link, rtc, trng, &mut session, deadline).await;
        link.disconnect().await;
        if session.is_enrolled() {
            return Ok(NodeSetupEnd::Enrolled);
        }
        if window_over {
            return Ok(NodeSetupEnd::WindowClosed);
        }
    }
}

/// Serves one connection until it ends; returns whether it ended because the window did.
async fn serve<L: SetupLink, R: Rtc, T: Trng>(
    link: &mut L,
    rtc: &R,
    trng: &mut T,
    session: &mut Session<'_>,
    deadline: u64,
) -> bool {
    let mut reassembler = Reassembler::new();
    let mut payload = [0u8; MAX_PAYLOAD];
    let mut out = [0u8; MAX_FRAME];
    loop {
        let Some(wait) = wait_until(rtc, deadline, NODE_SESSION_IDLE_TIMEOUT_MS) else {
            return true;
        };
        let length = match link.receive(&mut payload, wait.ms).await {
            Ok(length) => length,
            Err(LinkError::Timeout) => return wait.to_deadline,
            // Disconnected, or the stack failed: the connection is over.
            Err(_) => return false,
        };
        let action = match reassembler.push(&payload[..length]) {
            Ok(Some(frame)) => session.on_frame(trng, frame, &mut out),
            Ok(None) => continue,
            Err(_) => session.on_frame_error(&mut out),
        };
        payload.fill(0);
        let done = match action {
            Action::Nothing => false,
            Action::Send(length) => send_frame(link, &out[..length]).await.is_err(),
            Action::SendThenClose(length) => {
                let _ = send_frame(link, &out[..length]).await;
                true
            }
            // A Node session never asks for Wi-Fi, a Server check or a store.
            Action::Close
            | Action::Scan
            | Action::Join
            | Action::CheckServer
            | Action::Leave
            | Action::Persist => true,
        };
        out.fill(0);
        if done {
            return false;
        }
    }
}
