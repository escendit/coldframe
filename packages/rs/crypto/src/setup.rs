//! The BLE setup session (AD-25): X25519 between the app and the Device, the PoP code mixed in as
//! the HKDF salt, then ChaCha20-Poly1305 per message with one counter per direction.
//!
//! `okm = HKDF-SHA256(salt = uppercase PoP code, ikm = X25519, info = "coldframe/setup/v1" ‖
//! app_pub ‖ hub_pub, L = 64)`; bytes 0..32 seal app → Hub, 32..64 seal Hub → app. Nonce =
//! `0x00000000 ‖ counter_u64_be`; AAD = `u8 protocol_major`. When the first message does not open,
//! the codes differ and [`Error::WrongSetupCode`] says so.

use hkdf::HkdfExtract;
use sha2::Sha256;

use crate::hpke::{diffie_hellman, public_key};
use crate::spec::{
    AEAD_NONCE_LENGTH, PROTOCOL_MAJOR, SETUP_APP_TO_HUB_KEY_OFFSET, SETUP_FIRST_COUNTER,
    SETUP_HUB_TO_APP_KEY_OFFSET, SETUP_KEY_LENGTH, SETUP_LABEL, SETUP_MAX_CODE_LENGTH,
    SETUP_NONCE_PREFIX_LENGTH, SETUP_OKM_LENGTH, X25519_KEY_LENGTH,
};
use crate::{Error, aead};

/// Which end of the session this is.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Role {
    /// The phone, which sends with the app → Hub key.
    App,
    /// The Device, which sends with the Hub → app key.
    Device,
}

/// The two direction keys of a session. Deliberately not `Debug`.
pub struct SetupKeys {
    /// Seals app → Device messages.
    pub app_to_device: [u8; SETUP_KEY_LENGTH],
    /// Seals Device → app messages.
    pub device_to_app: [u8; SETUP_KEY_LENGTH],
}

/// Derives the direction keys. `app_public` and `device_public` fix the order in `info`.
pub fn derive_keys(
    own_private: &[u8; X25519_KEY_LENGTH],
    peer_public: &[u8; X25519_KEY_LENGTH],
    app_public: &[u8; X25519_KEY_LENGTH],
    device_public: &[u8; X25519_KEY_LENGTH],
    pop_code: &str,
) -> Result<SetupKeys, Error> {
    let mut salt = [0u8; SETUP_MAX_CODE_LENGTH];
    let code = pop_code.as_bytes();
    if code.is_empty() || code.len() > SETUP_MAX_CODE_LENGTH || !pop_code.is_ascii() {
        return Err(Error::InvalidSetupCode);
    }
    let salt = &mut salt[..code.len()];
    salt.copy_from_slice(code);
    salt.make_ascii_uppercase();

    let shared = diffie_hellman(own_private, peer_public)?;
    let mut extract = HkdfExtract::<Sha256>::new(Some(salt));
    extract.input_ikm(&shared);
    let (_, hkdf) = extract.finalize();
    let mut okm = [0u8; SETUP_OKM_LENGTH];
    hkdf.expand_multi_info(
        &[SETUP_LABEL.as_bytes(), app_public, device_public],
        &mut okm,
    )
    .map_err(|_| Error::InvalidLength)?;

    let mut keys = SetupKeys {
        app_to_device: [0u8; SETUP_KEY_LENGTH],
        device_to_app: [0u8; SETUP_KEY_LENGTH],
    };
    keys.app_to_device.copy_from_slice(
        &okm[SETUP_APP_TO_HUB_KEY_OFFSET..SETUP_APP_TO_HUB_KEY_OFFSET + SETUP_KEY_LENGTH],
    );
    keys.device_to_app.copy_from_slice(
        &okm[SETUP_HUB_TO_APP_KEY_OFFSET..SETUP_HUB_TO_APP_KEY_OFFSET + SETUP_KEY_LENGTH],
    );
    Ok(keys)
}

/// The nonce of the message with `counter`.
#[must_use]
pub fn nonce(counter: u64) -> [u8; AEAD_NONCE_LENGTH] {
    let mut nonce = [0u8; AEAD_NONCE_LENGTH];
    nonce[SETUP_NONCE_PREFIX_LENGTH..].copy_from_slice(&counter.to_be_bytes());
    nonce
}

/// One end of an established setup session. Deliberately not `Debug`.
pub struct SetupSession {
    send_key: [u8; SETUP_KEY_LENGTH],
    receive_key: [u8; SETUP_KEY_LENGTH],
    next_send: u64,
    next_receive: u64,
    opened_any: bool,
}

impl SetupSession {
    /// Starts a session from this end's private key, the peer's public key and the PoP code.
    pub fn new(
        role: Role,
        own_private: &[u8; X25519_KEY_LENGTH],
        peer_public: &[u8; X25519_KEY_LENGTH],
        pop_code: &str,
    ) -> Result<Self, Error> {
        let own_public = public_key(own_private);
        let (app_public, device_public) = match role {
            Role::App => (&own_public, peer_public),
            Role::Device => (peer_public, &own_public),
        };
        let keys = derive_keys(
            own_private,
            peer_public,
            app_public,
            device_public,
            pop_code,
        )?;
        let (send_key, receive_key) = match role {
            Role::App => (keys.app_to_device, keys.device_to_app),
            Role::Device => (keys.device_to_app, keys.app_to_device),
        };
        Ok(Self {
            send_key,
            receive_key,
            next_send: SETUP_FIRST_COUNTER,
            next_receive: SETUP_FIRST_COUNTER,
            opened_any: false,
        })
    }

    /// Seals the next outgoing message into `out` (`plaintext.len() + 16` bytes); returns its
    /// counter and length.
    pub fn seal(&mut self, plaintext: &[u8], out: &mut [u8]) -> Result<(u64, usize), Error> {
        let counter = self.next_send;
        let next = counter.checked_add(1).ok_or(Error::Replay)?;
        let length = aead::seal(
            &self.send_key,
            &nonce(counter),
            &[PROTOCOL_MAJOR],
            plaintext,
            out,
        )?;
        self.next_send = next;
        Ok((counter, length))
    }

    /// Opens an incoming message. Its counter must be above every counter opened before. The
    /// first failure to open is [`Error::WrongSetupCode`].
    pub fn open(&mut self, counter: u64, sealed: &[u8], out: &mut [u8]) -> Result<usize, Error> {
        if counter < self.next_receive {
            return Err(Error::Replay);
        }
        let next = counter.checked_add(1).ok_or(Error::Replay)?;
        match aead::open(
            &self.receive_key,
            &nonce(counter),
            &[PROTOCOL_MAJOR],
            sealed,
            out,
        ) {
            Ok(length) => {
                self.opened_any = true;
                self.next_receive = next;
                Ok(length)
            }
            Err(Error::AuthenticationFailed) if !self.opened_any => Err(Error::WrongSetupCode),
            Err(error) => Err(error),
        }
    }
}
