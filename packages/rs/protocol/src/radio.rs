//! The Node ⇄ Hub radio messages (AD-9), carried as ESP-NOW v1 payloads of at most
//! [`MAX_PAYLOAD`] bytes. The first byte is the kind:
//!
//! | Kind | Direction | Body |
//! | --- | --- | --- |
//! | [`KIND_PROBE`] | Node → broadcast | an 8-byte nonce |
//! | [`KIND_PROBE_REPLY`] | Hub → Node, unicast | the nonce, then one byte `pending`: how many downlinks the Hub holds for this Node and sends next, 0 to [`PENDING_MAX`] |
//! | [`KIND_UPLINK`] | Node → Hub, unicast | the bytes of a `SealedEnvelope` |
//! | [`KIND_DOWNLINK`] | Hub → Node, unicast | the bytes of a `SealedEnvelope` |
//!
//! Probes and their replies are not authenticated: a forged reply costs a Node one wasted burst
//! and can neither delete a Reading nor reset its miss count. The envelopes are opaque here; the
//! Hub relays them unread.

use crate::CodecError;

/// The largest ESP-NOW v1 payload.
pub const MAX_PAYLOAD: usize = 250;

/// The largest envelope one message carries: the payload less the kind byte.
pub const ENVELOPE_MAX: usize = MAX_PAYLOAD - 1;

/// The most downlinks a Hub keeps for one Node, and so the largest `pending` of a probe reply.
pub const PENDING_MAX: u8 = 8;

/// Length of a probe's nonce.
pub const NONCE_LENGTH: usize = 8;

/// A Node asks "is a Hub on this channel?".
pub const KIND_PROBE: u8 = 0x01;

/// A Hub answers a probe.
pub const KIND_PROBE_REPLY: u8 = 0x02;

/// A Node's sealed frame.
pub const KIND_UPLINK: u8 = 0x03;

/// A sealed downlink for a Node.
pub const KIND_DOWNLINK: u8 = 0x04;

/// Length of an encoded probe.
pub const PROBE_LENGTH: usize = 1 + NONCE_LENGTH;

/// Length of an encoded probe reply.
pub const PROBE_REPLY_LENGTH: usize = 1 + NONCE_LENGTH + 1;

/// One radio message. An envelope borrows the bytes it was decoded from.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Message<'a> {
    /// Node → broadcast.
    Probe {
        /// Echoed by the reply, so a Node matches it to its probe.
        nonce: [u8; NONCE_LENGTH],
    },
    /// Hub → Node.
    ProbeReply {
        /// The probe's nonce.
        nonce: [u8; NONCE_LENGTH],
        /// How many downlinks the Hub holds for this Node and sends next, 0 to [`PENDING_MAX`].
        pending: u8,
    },
    /// Node → Hub: the bytes of a `SealedEnvelope`, 1 to [`ENVELOPE_MAX`].
    Uplink(&'a [u8]),
    /// Hub → Node: the bytes of a `SealedEnvelope`, 1 to [`ENVELOPE_MAX`].
    Downlink(&'a [u8]),
}

impl<'a> Message<'a> {
    /// Encodes the message into the front of `out`; returns its length.
    ///
    /// # Errors
    ///
    /// [`CodecError::BufferTooSmall`] when `out` cannot hold it, [`CodecError::Malformed`] for an
    /// empty envelope, one longer than [`ENVELOPE_MAX`], or a `pending` over [`PENDING_MAX`].
    pub fn encode(&self, out: &mut [u8]) -> Result<usize, CodecError> {
        let (kind, envelope) = match self {
            Self::Probe { nonce } => {
                let target = out
                    .get_mut(..PROBE_LENGTH)
                    .ok_or(CodecError::BufferTooSmall)?;
                target[0] = KIND_PROBE;
                target[1..].copy_from_slice(nonce);
                return Ok(PROBE_LENGTH);
            }
            Self::ProbeReply { nonce, pending } => {
                if *pending > PENDING_MAX {
                    return Err(CodecError::Malformed);
                }
                let target = out
                    .get_mut(..PROBE_REPLY_LENGTH)
                    .ok_or(CodecError::BufferTooSmall)?;
                target[0] = KIND_PROBE_REPLY;
                target[1..=NONCE_LENGTH].copy_from_slice(nonce);
                target[1 + NONCE_LENGTH] = *pending;
                return Ok(PROBE_REPLY_LENGTH);
            }
            Self::Uplink(envelope) => (KIND_UPLINK, *envelope),
            Self::Downlink(envelope) => (KIND_DOWNLINK, *envelope),
        };
        if envelope.is_empty() || envelope.len() > ENVELOPE_MAX {
            return Err(CodecError::Malformed);
        }
        let target = out
            .get_mut(..=envelope.len())
            .ok_or(CodecError::BufferTooSmall)?;
        target[0] = kind;
        target[1..].copy_from_slice(envelope);
        Ok(1 + envelope.len())
    }

    /// Decodes one payload.
    ///
    /// # Errors
    ///
    /// [`CodecError::Malformed`] for an unknown kind, a body of the wrong length, a `pending`
    /// byte over [`PENDING_MAX`], an empty envelope, or a payload over [`MAX_PAYLOAD`].
    pub fn decode(payload: &'a [u8]) -> Result<Self, CodecError> {
        if payload.len() > MAX_PAYLOAD {
            return Err(CodecError::Malformed);
        }
        let (&kind, body) = payload.split_first().ok_or(CodecError::Malformed)?;
        match kind {
            KIND_PROBE => Ok(Self::Probe {
                nonce: body.try_into().map_err(|_| CodecError::Malformed)?,
            }),
            KIND_PROBE_REPLY => {
                let (nonce, pending) = body
                    .split_first_chunk::<NONCE_LENGTH>()
                    .ok_or(CodecError::Malformed)?;
                let pending = match pending {
                    [count] if *count <= PENDING_MAX => *count,
                    _ => return Err(CodecError::Malformed),
                };
                Ok(Self::ProbeReply {
                    nonce: *nonce,
                    pending,
                })
            }
            KIND_UPLINK if !body.is_empty() => Ok(Self::Uplink(body)),
            KIND_DOWNLINK if !body.is_empty() => Ok(Self::Downlink(body)),
            _ => Err(CodecError::Malformed),
        }
    }
}
