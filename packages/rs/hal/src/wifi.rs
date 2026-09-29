//! Wi-Fi station: scan every channel and join one access point (Hub only).
//!
//! The setup session decides which access point to join (the strongest BSSID for the SSID,
//! never the first match); this trait only scans and joins. A password passed to
//! [`Wifi::join`] is never logged or returned in an error.

use core::fmt;

/// Longest SSID, in bytes.
pub const SSID_MAX_LENGTH: usize = 32;

/// How a network authenticates, as the Hub sees it.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Security {
    /// No authentication.
    Open,
    /// WPA or WPA2 personal (pre-shared key).
    Wpa2Personal,
    /// WPA2/WPA3 transition mode; the Hub joins with WPA2.
    Wpa3Transition,
    /// WPA3-only (SAE): unsupported.
    Wpa3Only,
    /// Anything else (WEP, OWE, enterprise, WAPI, DPP): unsupported.
    Other,
}

impl Security {
    /// Whether the Hub can join a network that authenticates this way.
    #[must_use]
    pub const fn is_supported(self) -> bool {
        matches!(self, Self::Open | Self::Wpa2Personal | Self::Wpa3Transition)
    }
}

/// One access point heard by a scan.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct AccessPoint {
    /// The SSID bytes; only the first `ssid_length` are meaningful.
    pub ssid: [u8; SSID_MAX_LENGTH],
    /// Length of the SSID, at most [`SSID_MAX_LENGTH`].
    pub ssid_length: u8,
    /// The BSSID (MAC address of the access point).
    pub bssid: [u8; 6],
    /// The primary channel.
    pub channel: u8,
    /// Signal strength in dBm.
    pub rssi: i8,
    /// How it authenticates.
    pub security: Security,
}

impl AccessPoint {
    /// An empty entry, to fill a scan buffer with.
    pub const EMPTY: Self = Self {
        ssid: [0; SSID_MAX_LENGTH],
        ssid_length: 0,
        bssid: [0; 6],
        channel: 0,
        rssi: i8::MIN,
        security: Security::Other,
    };

    /// An access point with `ssid` (truncated to [`SSID_MAX_LENGTH`] bytes).
    #[must_use]
    pub fn new(ssid: &[u8], bssid: [u8; 6], channel: u8, rssi: i8, security: Security) -> Self {
        let length = ssid.len().min(SSID_MAX_LENGTH);
        let mut bytes = [0; SSID_MAX_LENGTH];
        bytes[..length].copy_from_slice(&ssid[..length]);
        Self {
            ssid: bytes,
            // `length` is at most 32.
            ssid_length: u8::try_from(length).unwrap_or(u8::MAX),
            bssid,
            channel,
            rssi,
            security,
        }
    }

    /// The SSID bytes.
    #[must_use]
    pub fn ssid(&self) -> &[u8] {
        &self.ssid[..usize::from(self.ssid_length).min(SSID_MAX_LENGTH)]
    }
}

/// Why a scan failed.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum WifiError {
    /// The driver could not scan.
    ScanFailed,
}

impl fmt::Display for WifiError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::ScanFailed => "Wi-Fi scan failed",
        })
    }
}

impl core::error::Error for WifiError {}

/// Why joining failed. Carries neither the SSID nor the password.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum JoinError {
    /// The access point refused the passphrase (or the handshake timed out, as with one).
    WrongPassword,
    /// The access point was not found.
    NotFound,
    /// The access point's security is not supported.
    Unsupported,
    /// Any other failure.
    Failed,
}

impl fmt::Display for JoinError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::WrongPassword => "Wi-Fi passphrase refused",
            Self::NotFound => "Wi-Fi network not found",
            Self::Unsupported => "Wi-Fi security unsupported",
            Self::Failed => "Wi-Fi join failed",
        })
    }
}

impl core::error::Error for JoinError {}

/// A Wi-Fi station.
///
/// `async` for a single-threaded executor, like [`crate::SetupLink`].
#[allow(async_fn_in_trait)]
pub trait Wifi {
    /// Actively scans every channel and writes the access points heard into `out`; returns how
    /// many. When more are heard than `out` holds, the strongest are kept. Must not be called
    /// while a join is in progress.
    ///
    /// # Errors
    ///
    /// [`WifiError::ScanFailed`].
    async fn scan(&mut self, out: &mut [AccessPoint]) -> Result<usize, WifiError>;

    /// Joins the access point `bssid` on `channel` with `ssid` and `password` (empty for an
    /// open network), and waits for association and the key handshake.
    ///
    /// # Errors
    ///
    /// See [`JoinError`].
    async fn join(
        &mut self,
        ssid: &str,
        password: &str,
        bssid: [u8; 6],
        channel: u8,
    ) -> Result<(), JoinError>;
}
