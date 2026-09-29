//! Which access point to join (H-1): the strongest BSSID for the SSID, never the first match.

use core::fmt;

use coldframe_hal::AccessPoint;

/// Why no access point was chosen.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SelectError {
    /// No access point with this SSID was heard.
    NotHeard,
    /// Access points with this SSID were heard, but none the Hub can join (WPA3-only or another
    /// unsupported security).
    Unsupported,
}

impl fmt::Display for SelectError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::NotHeard => "network not heard",
            Self::Unsupported => "network security unsupported",
        })
    }
}

impl core::error::Error for SelectError {}

/// The access point to join for `ssid`: the strongest one the Hub supports. Among equally strong
/// ones, the first heard wins.
///
/// # Errors
///
/// [`SelectError::NotHeard`] when no access point has the SSID, [`SelectError::Unsupported`]
/// when every one that has it is unsupported.
pub fn select_bssid<'a>(
    heard: &'a [AccessPoint],
    ssid: &[u8],
) -> Result<&'a AccessPoint, SelectError> {
    let mut seen = false;
    let mut best: Option<&AccessPoint> = None;
    for access_point in heard.iter().filter(|ap| ap.ssid() == ssid) {
        seen = true;
        if !access_point.security.is_supported() {
            continue;
        }
        if best.is_none_or(|current| access_point.rssi > current.rssi) {
            best = Some(access_point);
        }
    }
    match best {
        Some(access_point) => Ok(access_point),
        None if seen => Err(SelectError::Unsupported),
        None => Err(SelectError::NotHeard),
    }
}
