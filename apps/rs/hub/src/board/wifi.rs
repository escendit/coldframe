//! [`Wifi`] over the esp-radio Wi-Fi station.
//!
//! - Scan: an active scan of every channel (`ScanConfig::default()`); the strongest access points
//!   are kept when more are heard than fit.
//! - Join: pinned to the BSSID and channel the caller chose (`coldframe_uplink::select_bssid`),
//!   WPA2-Personal (or open for an empty passphrase). esp-radio has no WPA3-SAE station
//!   configuration, which is why WPA3-only networks are unsupported.
//! - Link: [`Wifi::is_connected`] is the station's association state; the uplink checks it before
//!   each heartbeat and after any network error, and re-joins. [`Wifi::leave`] disconnects.
//!
//! Neither the SSID nor the passphrase is logged.

use coldframe_hal::{AccessPoint, JoinError, Security, Wifi, WifiError};
use embassy_time::{Duration, with_timeout};
use esp_radio::wifi::scan::ScanConfig;
use esp_radio::wifi::sta::StationConfig;
use esp_radio::wifi::{
    AuthenticationMethod, AuthenticationMethodConfig, Config, ConnectionError, DisconnectReason,
    WifiController,
};

/// How long one join may take before it counts as failed.
const JOIN_TIMEOUT: Duration = Duration::from_secs(30);

/// The Wi-Fi station.
pub struct BoardWifi {
    controller: WifiController<'static>,
}

impl BoardWifi {
    /// Takes over the controller the radio was started with.
    pub fn new(controller: WifiController<'static>) -> Self {
        Self { controller }
    }
}

/// How the Hub sees an access point's authentication.
fn security(method: Option<AuthenticationMethod>) -> Security {
    match method {
        Some(AuthenticationMethod::None) => Security::Open,
        Some(
            AuthenticationMethod::Wpa
            | AuthenticationMethod::Wpa2Personal
            | AuthenticationMethod::WpaWpa2Personal,
        ) => Security::Wpa2Personal,
        Some(AuthenticationMethod::Wpa2Wpa3Personal) => Security::Wpa3Transition,
        Some(
            AuthenticationMethod::Wpa3Personal
            | AuthenticationMethod::Wpa3ExtPsk
            | AuthenticationMethod::Wpa3ExtPskMixed,
        ) => Security::Wpa3Only,
        _ => Security::Other,
    }
}

/// Why a join failed, from the station's disconnect reason.
fn join_error(reason: DisconnectReason) -> JoinError {
    match reason {
        // 2, 14, 15, 202, 204: the passphrase was refused or the handshake never finished.
        DisconnectReason::AuthenticationExpired
        | DisconnectReason::MicFailure
        | DisconnectReason::FourWayHandshakeTimeout
        | DisconnectReason::AuthenticationFailed
        | DisconnectReason::HandshakeTimeout => JoinError::WrongPassword,
        // 201.
        DisconnectReason::NoAccessPointFound => JoinError::NotFound,
        // 210, 211.
        DisconnectReason::NoAccessPointFoundWithCompatibleSecurity
        | DisconnectReason::NoAccessPointFoundInAuthmodeThreshold => JoinError::Unsupported,
        _ => JoinError::Failed,
    }
}

impl Wifi for BoardWifi {
    async fn scan(&mut self, out: &mut [AccessPoint]) -> Result<usize, WifiError> {
        let mut heard = self
            .controller
            .scan_async(&ScanConfig::default())
            .await
            .map_err(|_| WifiError::ScanFailed)?;
        heard.sort_unstable_by_key(|ap| core::cmp::Reverse(ap.signal_strength));
        let mut count = 0;
        for ap in &heard {
            if count == out.len() {
                break;
            }
            let ssid = ap.ssid.as_str();
            // `as_str` cuts a non-UTF-8 SSID short; leave such networks out rather than show a
            // name the Hub could not join by.
            if ssid.len() != ap.ssid.len() {
                continue;
            }
            out[count] = AccessPoint::new(
                ssid.as_bytes(),
                ap.bssid,
                ap.channel,
                ap.signal_strength,
                security(ap.auth_method),
            );
            count += 1;
        }
        Ok(count)
    }

    async fn join(
        &mut self,
        ssid: &str,
        password: &str,
        bssid: [u8; 6],
        channel: u8,
    ) -> Result<(), JoinError> {
        if self.controller.is_connected() {
            let _ = self.controller.disconnect_async().await;
        }
        let authentication = if password.is_empty() {
            AuthenticationMethodConfig::Open
        } else {
            // WPA2 passphrases are 8 to 63 characters, or 64 hex digits.
            if password.len() < 8 {
                return Err(JoinError::WrongPassword);
            }
            AuthenticationMethodConfig::Wpa2Personal(
                password.try_into().map_err(|_| JoinError::WrongPassword)?,
            )
        };
        let station = StationConfig::default()
            .with_ssid(ssid.try_into().map_err(|_| JoinError::NotFound)?)
            .with_bssid(bssid)
            .with_channel(channel)
            .with_authentication(authentication);
        self.controller
            .set_config(&Config::Station(station))
            .map_err(|_| JoinError::Failed)?;
        match with_timeout(JOIN_TIMEOUT, self.controller.connect_async()).await {
            Ok(Ok(_)) => Ok(()),
            Ok(Err(ConnectionError::Failed(info))) => Err(join_error(info.reason)),
            Ok(Err(_)) => Err(JoinError::Failed),
            Err(_) => {
                let _ = self.controller.disconnect_async().await;
                Err(JoinError::Failed)
            }
        }
    }

    fn is_connected(&self) -> bool {
        self.controller.is_connected()
    }

    async fn leave(&mut self) {
        if self.controller.is_connected() {
            let _ = self.controller.disconnect_async().await;
        }
    }
}
