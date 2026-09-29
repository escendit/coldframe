//! A deadline over any future, with nothing but the [`Timer`] trait.

use core::future::{Future, poll_fn};
use core::pin::pin;
use core::task::Poll;

use coldframe_hal::Timer;

/// Runs `future` for at most `ms` milliseconds: `Some(output)` when it finished first, `None`
/// when the timer did. The future is polled first, so one that is ready at once always wins.
pub(crate) async fn with_timeout<T: Timer, F: Future>(
    timer: &mut T,
    ms: u32,
    future: F,
) -> Option<F::Output> {
    let mut future = pin!(future);
    let mut deadline = pin!(timer.sleep_ms(ms));
    poll_fn(|context| {
        if let Poll::Ready(output) = future.as_mut().poll(context) {
            return Poll::Ready(Some(output));
        }
        if deadline.as_mut().poll(context).is_ready() {
            return Poll::Ready(None);
        }
        Poll::Pending
    })
    .await
}
