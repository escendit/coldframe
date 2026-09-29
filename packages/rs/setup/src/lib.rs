//! The BLE setup service of a Coldframe Device (AD-25), over the `coldframe-hal` traits only.
//!
//! - [`code`]: the setup code (proof-of-possession code): 8 Crockford base32 characters.
//! - [`store`]: the `cf_setup` flash records, `CFPC` (setup code) and `CFWP` (provisioning).
//! - [`framing`]: splitting frames into BLE payloads and reassembling them.
//! - [`session`]: the Device side of one BLE session as a state machine without I/O.
//! - [`service`]: [`service::run_setup`], which drives link, Wi-Fi, TRNG and flash until the
//!   Device is provisioned.
//! - [`app`]: the app side of a session, for tests and the desktop bench client.
//!
//! # Transport contract
//!
//! - GATT service [`SERVICE_UUID`]; the app writes to [`WRITE_CHARACTERISTIC_UUID`] and the
//!   Device notifies on [`NOTIFY_CHARACTERISTIC_UUID`].
//! - Every write or notification is `header(1) ‖ fragment`: header [`framing::HEADER_LAST`]
//!   marks the last fragment of a frame, [`framing::HEADER_MORE`] says more follow. A fragment
//!   is at most ATT MTU − 4 bytes, and a reassembled frame at most [`MAX_FRAME`].
//! - The first frame of a connection is a `SessionHello`; the Device answers `SessionHelloReply`.
//!   Every later frame is a `SealedSetupMessage`.
//!
//! The crate is `no_std` without `alloc`. No type holding the setup code, a key, a Wi-Fi
//! password or a decrypted message implements `Debug`, and no error carries one.

#![cfg_attr(not(test), no_std)]

pub mod app;
pub mod code;
pub mod framing;
pub mod service;
pub mod session;
pub mod store;

use coldframe_protocol::{
    SEALED_SETUP_MESSAGE_MAX_SIZE, SESSION_HELLO_MAX_SIZE, SESSION_HELLO_REPLY_MAX_SIZE,
};

pub use code::SetupCode;
pub use service::{Provisioned, SetupError, run_setup};

/// The setup GATT service.
pub const SERVICE_UUID: u128 = 0xc01d_0001_5e70_4c0d_8f00_0000_0000_c0de;

/// The write characteristic, app → Device.
pub const WRITE_CHARACTERISTIC_UUID: u128 = 0xc01d_0002_5e70_4c0d_8f00_0000_0000_c0de;

/// The notify characteristic, Device → app.
pub const NOTIFY_CHARACTERISTIC_UUID: u128 = 0xc01d_0003_5e70_4c0d_8f00_0000_0000_c0de;

/// [`SERVICE_UUID`] in text form.
pub const SERVICE_UUID_STR: &str = "c01d0001-5e70-4c0d-8f00-00000000c0de";

/// [`WRITE_CHARACTERISTIC_UUID`] in text form.
pub const WRITE_CHARACTERISTIC_UUID_STR: &str = "c01d0002-5e70-4c0d-8f00-00000000c0de";

/// [`NOTIFY_CHARACTERISTIC_UUID`] in text form.
pub const NOTIFY_CHARACTERISTIC_UUID_STR: &str = "c01d0003-5e70-4c0d-8f00-00000000c0de";

/// The largest reassembled frame, in bytes.
pub const MAX_FRAME: usize = 1152;

const _: () = assert!(MAX_FRAME >= SEALED_SETUP_MESSAGE_MAX_SIZE);
const _: () = assert!(MAX_FRAME >= SESSION_HELLO_MAX_SIZE);
const _: () = assert!(MAX_FRAME >= SESSION_HELLO_REPLY_MAX_SIZE);

/// A session with no write for this long is disconnected, and the Device advertises again.
pub const SESSION_IDLE_TIMEOUT_MS: u32 = 300_000;

/// How many access points one scan keeps (the strongest).
pub const SCAN_CAPACITY: usize = 32;

/// How many networks a `WifiScanList` carries at most, one per SSID, strongest first.
pub const MAX_NETWORKS: usize = 16;

/// Longest Site ID, in bytes (a UUID in text form).
pub const SITE_ID_MAX_LENGTH: usize = 36;

/// Longest Wi-Fi passphrase, in bytes.
pub const PASSWORD_MAX_LENGTH: usize = 64;

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn uuid_text_matches_the_numbers() {
        for (number, text) in [
            (SERVICE_UUID, SERVICE_UUID_STR),
            (WRITE_CHARACTERISTIC_UUID, WRITE_CHARACTERISTIC_UUID_STR),
            (NOTIFY_CHARACTERISTIC_UUID, NOTIFY_CHARACTERISTIC_UUID_STR),
        ] {
            let hex: String = text.chars().filter(|c| *c != '-').collect();
            assert_eq!(u128::from_str_radix(&hex, 16).unwrap(), number);
        }
    }
}
