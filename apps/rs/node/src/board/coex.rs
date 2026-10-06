//! The `dev-mode` BLE + ESP-NOW coexistence probe (Story 4.2; the device-hardware open item).
//!
//! While a Node is in setup mode, a `dev-mode` build also runs ESP-NOW next to BLE: every
//! [`PROBE_EVERY_MS`] it broadcasts `b"CFCOEX1" ‖ device_id` on channel [`PROBE_CHANNEL`] and logs
//! `coex espnow tx ok=`, and it logs every probe it hears as `coex espnow rx from=<mac>`. Two Nodes
//! in setup mode side by side show that both stacks work in one image while a BLE session runs
//! (`docs/bench/node-setup-checklist.md`, Part F).
//!
//! The real ESP-NOW transport is `super::espnow` under `coldframe-transport` (Story 4.4), and it
//! never runs on a wake that entered setup mode. This module is compiled only with `dev-mode`: a
//! release build runs no ESP-NOW next to BLE. The radio-level send status is logged for the bench
//! only; it never counts as delivery (N-1).

use embassy_futures::join::join;
use embassy_time::{Duration, Timer};
use esp_hal::peripherals::WIFI;
use esp_radio::esp_now::{BROADCAST_ADDRESS, EspNow};
use esp_radio::wifi::{ControllerConfig, WifiController};
use log::{info, warn};

/// The probe's magic prefix.
pub const PROBE_MAGIC: &[u8; 7] = b"CFCOEX1";

/// How often a probe is broadcast.
pub const PROBE_EVERY_MS: u64 = 2_000;

/// The channel the probes use.
pub const PROBE_CHANNEL: u8 = 1;

/// A MAC address for the log.
struct Mac<'a>(&'a [u8; 6]);

impl core::fmt::Display for Mac<'_> {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        let [a, b, c, d, e, g] = self.0;
        write!(f, "{a:02x}:{b:02x}:{c:02x}:{d:02x}:{e:02x}:{g:02x}")
    }
}

/// Starts the Wi-Fi controller and ESP-NOW (call after the BLE controller exists), then probes
/// until dropped. Never completes: a failure is logged and the probe goes quiet, so setup mode
/// itself carries on.
pub async fn start(wifi: WIFI<'_>, device_id: &[u8; 8]) {
    match WifiController::new(wifi, ControllerConfig::default()) {
        Ok(controller) => {
            // ESP-NOW keeps Wi-Fi running until it is dropped; so does the controller.
            run(controller.esp_now(), *device_id).await;
            drop(controller);
        }
        Err(error) => warn!("coex wifi controller failed error={error:?}"),
    }
    core::future::pending::<()>().await;
}

/// Broadcasts and listens for probes until dropped.
async fn run(esp_now: EspNow, device_id: [u8; 8]) {
    if let Err(error) = esp_now.set_channel(PROBE_CHANNEL) {
        warn!("coex espnow set_channel failed error={error:?}");
        return;
    }
    let mut probe = [0u8; PROBE_MAGIC.len() + 8];
    probe[..PROBE_MAGIC.len()].copy_from_slice(PROBE_MAGIC);
    probe[PROBE_MAGIC.len()..].copy_from_slice(&device_id);
    info!("coex espnow probing channel={PROBE_CHANNEL} every_ms={PROBE_EVERY_MS}");

    let (_manager, mut sender, mut receiver) = esp_now.split();
    let transmit = async {
        loop {
            let ok = sender.send_async(&BROADCAST_ADDRESS, &probe).await.is_ok();
            info!("coex espnow tx ok={ok}");
            Timer::after(Duration::from_millis(PROBE_EVERY_MS)).await;
        }
    };
    let listen = async {
        loop {
            let received = receiver.receive_async().await;
            if received.data().starts_with(PROBE_MAGIC) {
                info!("coex espnow rx from={}", Mac(&received.info.src_address));
            }
        }
    };
    join(transmit, listen).await;
}
