# Sealing frames: counters and the replay window

Every wake report a Node sends, and every acknowledgement the Server sends back, is sealed with
ChaCha20-Poly1305. The Hub in between relays the sealed bytes and can neither read nor forge them.
This page shows how a frame is sealed, where the counter that makes each nonce unique comes from,
how the Server refuses replays, and why a report leaves the Node's flash only on a sealed
acknowledgement. It builds on the purpose keys from [identity.md](identity.md); the radio that
carries the frames is in [espnow.md](espnow.md). The index is [README.md](README.md).

The governing decisions are AD-12 (sealing in both directions) and AD-17 (counters, the replay
window and the report buffer) in the
[architecture spine](../../_bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md),
and requirement N-1 (delivery) in [device-hardware.md](../../_bmad-output/specs/spec-coldframe/device-hardware.md).

## How it works

### Two keys, one per direction

The Node derives two 32-byte purpose keys from its device key: `seal/v1` for uplink frames and
`ack/v1` for downlinks (`DeviceKeys` in `packages/rs/crypto/src/keys.rs`). Because the two
directions use different keys, an uplink and a downlink can never share a (key, nonce) pair, even
when their counters are equal. Each side keeps its own counter: the Node reserves uplink counters
in flash, and the Server reserves downlink counters in its database.

### Nonce and associated data

ChaCha20-Poly1305 needs a 12-byte nonce that never repeats under one key. Coldframe builds it from
the Device ID and the frame counter (`nonce` in `packages/rs/crypto/src/frame.rs`):

```text
nonce = device_id[0..4] ‖ counter (u64, big-endian)              12 bytes
aad   = protocol_major (u8) ‖ device_id (8 bytes) ‖ counter (u64, big-endian)   17 bytes
```

The key is already unique to the Device, so the counter alone keeps the nonce unique; the four
Device ID bytes are only a prefix. The associated data (`aad`) is authenticated but not
encrypted. It binds the ciphertext to the full Device ID, the counter and the wire major, so a
frame cannot be replayed under another Device ID or counter, or read as another protocol version,
without the tag failing. `seal` and `open` in the same file wrap the AEAD in
`packages/rs/crypto/src/aead.rs`: the sealed bytes are the ciphertext followed by the 16-byte tag.

### The envelope

On the wire both directions use one message, `SealedEnvelope` in
`packages/proto/coldframe/device/v1/envelope.proto`. It carries the protocol version, the Device
ID and the counter in the clear, and the ciphertext. The receiver needs those clear fields to
rebuild the nonce and the AAD. The plaintext of an uplink is a `NodeFrame` (one wake report); the
plaintext of a downlink is a `Downlink`.

`seal_uplink` in `packages/rs/transport/src/frame.rs` encodes the `NodeFrame`, seals it with the
`seal/v1` key under the given counter, wraps it in a `SealedEnvelope`, and puts the radio kind
byte in front. If the result would not fit one ESP-NOW payload it returns `SealError::TooLong`.
The plaintext buffer is zeroed after use.

`open_downlink` in the same file does the reverse for the Node. It drops a downlink silently
unless the envelope is protocol version 1, names this Node's Device ID, opens under the `ack/v1`
key, and decodes to a `Downlink` of version 1.

### The frame counter in flash

A counter that repeats after a reset would reuse a nonce, which breaks ChaCha20-Poly1305. So the
Node never keeps the frame counter in RAM alone. `ReservedCounter` in
`packages/rs/sensing/src/counter.rs` stores a *ceiling* in flash: every value below it may already
have been used. To get counters, `reserve(n)` writes the raised ceiling, reads it back, and only
then hands out `ceiling..ceiling + n`. A reset or power cut at any point can waste values (gaps
are allowed) but never repeat one.

The records go into two 4096-byte sectors of 32-byte slots, appended one after another. When the
active sector is full, the other sector is erased and the next record goes there, so one sector
always holds a valid ceiling. On boot the counter is the maximum over the valid records of both
sectors. A region with written slots but no valid record is `CounterError::Corrupt`, and the
counter never falls back to 0.

The frame counter lives in the `cf_frame` partition (`apps/rs/node/partitions.csv`, opened by
`FRAME_PARTITION` in `apps/rs/node/src/board/flash.rs`), with the record magic `CFFC`
(`FRAME_MAGIC` in `packages/rs/transport/src/transport.rs`). Each wake that sends reserves one
counter per frame in a single write (`reserve_counters`), and every frame of the burst is sealed
under its own fresh counter. If the reservation fails, nothing is sealed and the reports stay
buffered.

> [!WARNING]
> Never erase the `cf_frame` partition on a Node that has sent frames. The counter would restart
> at 0. The next frames would then be sealed under nonces already used with the same `seal/v1`
> key, which leaks plaintext to anyone who recorded the old frames. The Server would also refuse
> them as replays, because they are far below its high-water mark, so the Node could not deliver
> anything until its counter climbed past the old one. Only a board that has never sent a frame,
> or a `dev-mode` Node whose flash root in `cf_ident` is erased with it (a new identity, so new
> keys, enrolled again), may have `cf_frame` erased. An eFuse Node can never get new keys. The
> partition table is in the [Node README](../../apps/rs/node/README.md).

### The replay window

An attacker can record a sealed frame and send it again; the tag is still valid. The receiver
refuses it by its counter. `ReplayWindow` in `packages/rs/crypto/src/frame.rs` keeps the highest
accepted counter and a 64-bit mask of the 64 counters ending at it. A counter above the high-water
mark is accepted; a counter within the window is accepted once; anything older, or seen before,
is a replay. `open_checked` refuses a replay *before* decrypting, and moves the window only
*after* the frame authenticates, so a forged frame cannot push the window forward.

The window tolerates the gaps and reordering that a reserved counter and a lossy radio produce.
A large jump, as after many unsent wakes, simply forgets the old window.

Who runs it: the Server, not the firmware. The Server's Device grain loads the window from the
database, checks each uplink against it, and commits the moved window in the same transaction as
the Readings (`apps/cs/server/Devices/DeviceGrain.cs`, using the C# port in
`packages/cs/crypto/Frames.cs`). The Rust `ReplayWindow` and `open_checked` are the reference
implementation, tested against the shared vectors; no firmware crate calls them today.

### Downlinks on the Node

The Node does not keep a replay window for downlinks. It does not need one, because an authentic
downlink can only delete what the Server has already stored. Instead the transport decides
whether a downlink is *fresh*: its `acked_counter` must be a frame of this wake's burst or the
previous one, not acknowledged before (`Burst::acknowledge` in
`packages/rs/transport/src/link.rs`). Only a fresh downlink resets the miss count and sets the
clock. A replayed or late copy is harmless.

### The report buffer and deletion on acknowledgement

A wake report is written to the flash buffer `cf_buf` before the radio starts (`ReportBuffer` in
`packages/rs/transport/src/buffer.rs`). It stays there, across deep sleep and power loss, until a
sealed downlink acknowledges it. The buffer holds 96 reports (24 h at one wake every 15 minutes);
when it is full, the oldest report is dropped.

The `Downlink` lists the `reading_seq` values the Server has durably stored, as ranges
(`acked_readings`). `ReportBuffer::ack` deletes exactly the Readings and reports whose sequence
numbers fall in those ranges, by clearing bits of the record's live word. A record with no live
bit left is dead, and its sector is erased later. What the radio reports for a send is never read:
only a downlink that passed `open_downlink` deletes anything. A lost frame or a lost downlink
therefore costs only a resend. A resend is the same report sealed again under a new counter, with
the boot ID and uptime of the resend, and the Server takes it as a duplicate.

## Key constants and timings

| Constant | Value | Where |
| --- | --- | --- |
| `SEAL_LABEL`, `ACK_LABEL` | `seal/v1`, `ack/v1` | `packages/rs/crypto/src/spec.rs` |
| `AEAD_NONCE_LENGTH` | 12 bytes | `packages/rs/crypto/src/spec.rs` |
| `AEAD_TAG_LENGTH` | 16 bytes | `packages/rs/crypto/src/spec.rs` |
| `FRAME_NONCE_DEVICE_ID_PREFIX_LENGTH` | 4 bytes | `packages/rs/crypto/src/spec.rs` |
| `FRAME_AAD_LENGTH` | 17 bytes | `packages/rs/crypto/src/spec.rs` |
| `DEVICE_ID_LENGTH` | 8 bytes | `packages/rs/crypto/src/spec.rs` |
| `PROTOCOL_MAJOR` | 1 | `packages/rs/crypto/src/spec.rs` |
| `REPLAY_WINDOW` | 64 counters | `packages/rs/crypto/src/spec.rs` |
| `FRAME_MAGIC` | `CFFC` | `packages/rs/transport/src/transport.rs` |
| `SECTORS`, `SLOT_SIZE` | 2 sectors, 32-byte slots | `packages/rs/sensing/src/counter.rs` |
| `CAPACITY` | 96 reports | `packages/rs/transport/src/buffer.rs` |
| `MIN_SECTORS`, `MAX_SECTORS` | 5 to 8 sectors | `packages/rs/transport/src/buffer.rs` |
| `BURST_MAX` | 8 frames per wake | `packages/rs/transport/src/transport.rs` |
| `ACK_WINDOW_MS` | 300 ms | `packages/rs/transport/src/transport.rs` |
| `MAX_PAYLOAD` | 250 bytes per radio message | `packages/rs/protocol/src/radio.rs` |

The constants in `packages/rs/crypto/src/spec.rs` are generated from `packages/crypto-spec`, so
the Rust, C# and Kotlin sides share them.

## Where to read the code

- `packages/rs/crypto/src/frame.rs` (`nonce`, `aad`, `seal`, `open`): the whole sealing rule in
  a few lines.
- `packages/rs/crypto/src/frame.rs` (`ReplayWindow`, `open_checked`): the sliding window and the
  check-before-decrypt, accept-after-authenticate order.
- `packages/rs/transport/src/frame.rs` (`seal_uplink`, `open_downlink`): how a `NodeFrame`
  becomes a radio message, and what a Node accepts back.
- `packages/rs/sensing/src/counter.rs` (`ReservedCounter`): a power-loss-safe counter on NOR
  flash; the module doc explains the record layout.
- `packages/rs/transport/src/transport.rs` (`reserve_counters`, `send_burst`): one reservation
  per wake, one fresh counter per frame.
- `packages/rs/transport/src/buffer.rs` (`ReportBuffer`, `ack`): the report buffer and deletion
  on a sealed acknowledgement.
- `packages/rs/transport/src/link.rs` (`Burst`): which downlinks count as fresh.
- `packages/proto/coldframe/device/v1/envelope.proto` (`SealedEnvelope`, `NodeFrame`,
  `Downlink`): the wire messages, with comments.
- `apps/rs/node/partitions.csv`: where `cf_frame` and `cf_buf` sit in flash.
- `apps/cs/server/Devices/DeviceGrain.cs`: the Server side, where the replay window is enforced
  and the downlink counter is reserved.
