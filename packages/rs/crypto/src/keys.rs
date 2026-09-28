//! The key hierarchy (AD-12): root → `K_dev` → purpose keys and the Device ID.
//!
//! On hardware the root never leaves the eFuse block and `K_dev` comes from the HMAC peripheral.
//! [`derive_device_key`] is the software stand-in for dev mode and host tests; both compute
//! `HMAC-SHA256(root, "coldframe/device/v1")`.

use hkdf::Hkdf;
use hmac::{Hmac, KeyInit, Mac};
use sha2::Sha256;

use crate::hex;
use crate::spec::{
    ACK_LABEL, DEVICE_ID_HEX_LENGTH, DEVICE_ID_LABEL, DEVICE_ID_LENGTH, DEVICE_KEY_LABEL,
    DEVICE_KEY_LENGTH, HUB_AUTH_LABEL, PURPOSE_KEY_LENGTH, ROOT_KEY_LENGTH, SEAL_LABEL,
};

/// `K_dev = HMAC-SHA256(root, "coldframe/device/v1")`, in software.
#[must_use]
pub fn derive_device_key(root_key: &[u8; ROOT_KEY_LENGTH]) -> [u8; DEVICE_KEY_LENGTH] {
    let mut mac = <Hmac<Sha256> as KeyInit>::new_from_slice(root_key)
        .expect("HMAC accepts a key of any length");
    mac.update(DEVICE_KEY_LABEL.as_bytes());
    mac.finalize().into_bytes().into()
}

fn expand<const N: usize>(device_key: &[u8; DEVICE_KEY_LENGTH], label: &str) -> [u8; N] {
    let mut out = [0u8; N];
    Hkdf::<Sha256>::new(None, device_key)
        .expand(label.as_bytes(), &mut out)
        .expect("N is far below 255 * 32");
    out
}

/// `HKDF-SHA256(salt = empty, ikm = K_dev, info = label, L = 32)`.
#[must_use]
pub fn derive_purpose_key(
    device_key: &[u8; DEVICE_KEY_LENGTH],
    label: &str,
) -> [u8; PURPOSE_KEY_LENGTH] {
    expand(device_key, label)
}

/// The Device ID, `HKDF-SHA256(K_dev, "device-id/v1", L = 8)`.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub struct DeviceId(pub [u8; DEVICE_ID_LENGTH]);

impl DeviceId {
    /// Derives the Device ID from `K_dev`.
    #[must_use]
    pub fn derive(device_key: &[u8; DEVICE_KEY_LENGTH]) -> Self {
        Self(expand(device_key, DEVICE_ID_LABEL))
    }

    /// The raw 8 bytes.
    #[must_use]
    pub const fn as_bytes(&self) -> &[u8; DEVICE_ID_LENGTH] {
        &self.0
    }

    /// The text form: 16 lowercase hex digits.
    #[must_use]
    pub fn to_hex(&self) -> [u8; DEVICE_ID_HEX_LENGTH] {
        hex::encode_array(&self.0)
    }
}

/// Every key a Device holds, derived from `K_dev`. Deliberately not `Debug`.
pub struct DeviceKeys {
    /// `K_dev`, which enrolment seals to the Server.
    pub device_key: [u8; DEVICE_KEY_LENGTH],
    /// `seal/v1`: seals uplink frames.
    pub seal_key: [u8; PURPOSE_KEY_LENGTH],
    /// `ack/v1`: seals downlinks.
    pub ack_key: [u8; PURPOSE_KEY_LENGTH],
    /// `hub-auth/v1`: signs Hub requests.
    pub hub_auth_key: [u8; PURPOSE_KEY_LENGTH],
    /// The Device ID.
    pub device_id: DeviceId,
}

impl DeviceKeys {
    /// Derives every purpose key and the Device ID from `K_dev`.
    #[must_use]
    pub fn from_device_key(device_key: [u8; DEVICE_KEY_LENGTH]) -> Self {
        Self {
            seal_key: derive_purpose_key(&device_key, SEAL_LABEL),
            ack_key: derive_purpose_key(&device_key, ACK_LABEL),
            hub_auth_key: derive_purpose_key(&device_key, HUB_AUTH_LABEL),
            device_id: DeviceId::derive(&device_key),
            device_key,
        }
    }

    /// Derives everything from a software root key (dev mode and tests).
    #[must_use]
    pub fn from_root_key(root_key: &[u8; ROOT_KEY_LENGTH]) -> Self {
        Self::from_device_key(derive_device_key(root_key))
    }
}
