//! The Hub's side of the Node radio link (Story 4.4, AD-9): ESP-NOW on the Wi-Fi station's radio,
//! and the [`Relay`] the radio task shares with the uplink.
//!
//! - ESP-NOW comes from the running Wi-Fi controller and stays on the station's channel: a peer
//!   is added with `channel: None`, so it follows the access point (radio spike F-7). A Node finds
//!   the Hub by probing; it follows a router channel change by scanning.
//! - [`relay_task`] is its own task. It answers a probe from the relay state alone, so a reply
//!   never waits for the HTTPS request the uplink has in flight. It queues uplinks for that
//!   uplink, sends each downlink once when the Server's answer arrives, and sends a Node's kept
//!   downlinks once more, oldest first, right after the reply to its probe; then they are gone.
//!   A probe is not answered while the Hub cannot relay (not associated, or no clock).
//! - A peer entry lives only for one send, so the 20-peer limit of ESP-NOW never fills up.
//! - The radio-level send status is logged at debug level and otherwise unused: it is not
//!   delivery (N-1). No envelope, key or Reading is logged; MAC addresses are not secrets.

use core::cell::RefCell;

use coldframe_hal::{DATAGRAM_MAX, MacAddress};
use coldframe_uplink::relay::{Heard, Relay, RelayPort};
use embassy_futures::select::{Either, select};
use embassy_sync::blocking_mutex::Mutex;
use embassy_sync::blocking_mutex::raw::CriticalSectionRawMutex;
use embassy_sync::signal::Signal;
use embassy_time::{Duration, with_timeout};
use esp_radio::esp_now::{EspNow, EspNowManager, EspNowSender, EspNowWifiInterface, PeerInfo};
use log::{debug, info, warn};

/// How long one send may take before the radio task moves on. Clearly shorter than the 120 ms
/// a Node waits for a probe reply: a send to a Node that has gone back to sleep must not hold up
/// the answer to the next probe.
const SEND_TIMEOUT: Duration = Duration::from_millis(60);

/// The relay state, shared by the radio task and the uplink. Every access is one short critical
/// section; nothing holds the state across an `await`.
pub struct SharedRelay {
    relay: Mutex<CriticalSectionRawMutex, RefCell<Relay>>,
    /// An uplink was queued: the uplink should post it.
    uplinks: Signal<CriticalSectionRawMutex, ()>,
    /// The uplink touched the relay: a downlink may be waiting to go out.
    downlinks: Signal<CriticalSectionRawMutex, ()>,
}

impl SharedRelay {
    /// An empty relay.
    pub const fn new() -> Self {
        Self {
            relay: Mutex::new(RefCell::new(Relay::new())),
            uplinks: Signal::new(),
            downlinks: Signal::new(),
        }
    }

    /// The radio task's access: it wakes nobody.
    fn lock<T>(&self, f: impl FnOnce(&mut Relay) -> T) -> T {
        self.relay.lock(|relay| f(&mut relay.borrow_mut()))
    }
}

impl RelayPort for SharedRelay {
    fn with<T>(&self, f: impl FnOnce(&mut Relay) -> T) -> T {
        let result = self.lock(f);
        self.downlinks.signal(());
        result
    }

    async fn uplink_queued(&self) {
        self.uplinks.wait().await;
    }
}

/// The Hub's one relay.
pub static RELAY: SharedRelay = SharedRelay::new();

/// A MAC address for the log.
struct Mac<'a>(&'a MacAddress);

impl core::fmt::Display for Mac<'_> {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        let [a, b, c, d, e, g] = self.0;
        write!(f, "{a:02x}:{b:02x}:{c:02x}:{d:02x}:{e:02x}:{g:02x}")
    }
}

/// Sends one radio message to `to`: a peer entry on the station's channel for the send only.
async fn send(manager: &EspNowManager, sender: &mut EspNowSender, to: &MacAddress, message: &[u8]) {
    let added = !manager.peer_exists(to)
        && manager
            .add_peer(PeerInfo {
                interface: EspNowWifiInterface::Station,
                peer_address: *to,
                lmk: None,
                // Follow the station's channel.
                channel: None,
                encrypt: false,
            })
            .is_ok();
    let status = with_timeout(SEND_TIMEOUT, sender.send_async(to, message)).await;
    // Radio-level only, and not delivery (N-1).
    debug!(
        "relay send to={} ok={}",
        Mac(to),
        matches!(status, Ok(Ok(())))
    );
    if added {
        let _ = manager.remove_peer(to);
    }
}

/// Serves the radio forever: probes, uplinks and downlinks, over `shared`.
#[embassy_executor::task]
pub async fn relay_task(esp_now: EspNow, shared: &'static SharedRelay) -> ! {
    let (manager, mut sender, mut receiver) = esp_now.split();
    let mut message = [0u8; DATAGRAM_MAX];
    info!("relay listening");
    loop {
        if let Either::First(received) =
            select(receiver.receive_async(), shared.downlinks.wait()).await
        {
            let source = received.info.src_address;
            match shared.lock(|relay| relay.hear(&source, received.data())) {
                Heard::Probe { reply, pending } => {
                    send(&manager, &mut sender, &source, &reply).await;
                    info!("relay probe from={} pending={pending}", Mac(&source));
                    // Exactly the downlinks the reply announced, each offered this once.
                    for _ in 0..pending {
                        let Some(length) =
                            shared.lock(|relay| relay.take_kept(&source, &mut message))
                        else {
                            break;
                        };
                        send(&manager, &mut sender, &source, &message[..length]).await;
                    }
                }
                Heard::NotRelaying => {
                    info!("relay probe from={} unanswered: not relaying", Mac(&source));
                }
                Heard::Queued => {
                    info!(
                        "relay uplink from={} bytes={}",
                        Mac(&source),
                        received.data().len()
                    );
                    shared.uplinks.signal(());
                }
                Heard::QueueFull => warn!("relay queue full; uplink from={} dropped", Mac(&source)),
                Heard::Duplicate | Heard::Ignored => {}
            }
        }
        // Whatever woke the task: send the downlinks that arrived, each once.
        while let Some((to, length)) = shared.lock(|relay| relay.next_unsent(&mut message)) {
            send(&manager, &mut sender, &to, &message[..length]).await;
            info!("relay downlink to={} bytes={length}", Mac(&to));
        }
    }
}
