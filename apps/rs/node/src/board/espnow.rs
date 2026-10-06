//! [`DatagramRadio`] over esp-radio's ESP-NOW (Story 4.4).
//!
//! The Node never associates: it tunes ESP-NOW to a channel, broadcasts a probe and talks to the
//! Hub that answers. The radio is on only while this value and the Wi-Fi controller it came from
//! live; `main` drops both before deep sleep.
//!
//! The result of a send is the radio-level status. It is not delivery (N-1): under coexistence a
//! send reported as failed has often arrived, so the transport never reads it.

use coldframe_hal::{BROADCAST, DATAGRAM_MAX, Datagram, DatagramError, DatagramRadio, MacAddress};
use embassy_time::{Duration, with_timeout};
use esp_radio::esp_now::{EspNow, EspNowWifiInterface, PeerInfo};

/// How long one send may take before the Node moves on. The send callback normally fires within
/// a few milliseconds; the bound keeps a wake short whatever the driver does.
const SEND_TIMEOUT: Duration = Duration::from_millis(200);

/// ESP-NOW, without a Wi-Fi association.
pub struct BoardEspNow {
    esp_now: EspNow,
}

impl BoardEspNow {
    /// Takes over ESP-NOW from a running Wi-Fi controller (`WifiController::esp_now`).
    pub fn new(esp_now: EspNow) -> Self {
        Self { esp_now }
    }
}

impl DatagramRadio for BoardEspNow {
    fn set_channel(&mut self, channel: u8) -> Result<(), DatagramError> {
        self.esp_now
            .set_channel(channel)
            .map_err(|_| DatagramError::Channel)
    }

    async fn send(&mut self, to: &MacAddress, payload: &[u8]) -> Result<(), DatagramError> {
        if payload.len() > DATAGRAM_MAX {
            return Err(DatagramError::TooLong);
        }
        // esp-radio adds the broadcast peer itself; a Hub becomes a peer on the current channel
        // (`channel: None`) the first time the Node writes to it.
        if *to != BROADCAST && !self.esp_now.peer_exists(to) {
            self.esp_now
                .add_peer(PeerInfo {
                    interface: EspNowWifiInterface::Station,
                    peer_address: *to,
                    lmk: None,
                    channel: None,
                    encrypt: false,
                })
                .map_err(|_| DatagramError::Send)?;
        }
        match with_timeout(SEND_TIMEOUT, self.esp_now.send_async(to, payload)).await {
            Ok(Ok(())) => Ok(()),
            _ => Err(DatagramError::Send),
        }
    }

    async fn receive(&mut self, buffer: &mut [u8], timeout_ms: u32) -> Option<Datagram> {
        let timeout = Duration::from_millis(u64::from(timeout_ms));
        let received = with_timeout(timeout, self.esp_now.receive_async())
            .await
            .ok()?;
        let payload = received.data();
        let len = payload.len().min(buffer.len());
        buffer[..len].copy_from_slice(&payload[..len]);
        Some(Datagram {
            source: received.info.src_address,
            len,
        })
    }
}
