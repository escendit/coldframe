use core::fmt;

/// Why a cryptographic operation failed. Carries no key material or payload.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Error {
    /// A ciphertext, tag or associated data did not authenticate.
    AuthenticationFailed,
    /// The first message of a setup session did not open: the PoP code differs.
    WrongSetupCode,
    /// A counter was already seen, is not above the last one, or fell out of the window.
    Replay,
    /// An X25519 exchange produced the all-zero value (a small-order public key).
    InvalidPublicKey,
    /// A PoP code is empty, too long or not ASCII.
    InvalidSetupCode,
    /// An output buffer is too small, or an input has the wrong length.
    InvalidLength,
}

impl fmt::Display for Error {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::AuthenticationFailed => "authentication failed",
            Self::WrongSetupCode => "wrong setup code",
            Self::Replay => "replayed or out-of-window counter",
            Self::InvalidPublicKey => "invalid X25519 public key",
            Self::InvalidSetupCode => "invalid setup code",
            Self::InvalidLength => "invalid length",
        })
    }
}

impl core::error::Error for Error {}
