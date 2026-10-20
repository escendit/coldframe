# BLE setup and the proof-of-possession code

A new Hub or Node knows nothing about your Wi-Fi, your Site or your Server. The app tells it over
Bluetooth Low Energy, through a small custom GATT service. Before any setting crosses the air, the
two sides agree on session keys, and the Device's setup code, the proof-of-possession (PoP) code, is
mixed into those keys. The code itself is never sent. This page follows one setup session from the
first BLE write to the stored record. It is part of the [firmware reference](README.md); the
previous page, [buttons.md](buttons.md), shows how a long press opens the Node's setup window, and
the next, [radio-coexistence.md](radio-coexistence.md), covers BLE, Wi-Fi and ESP-NOW on one radio.

The rules come from AD-25 (the setup protocol) and AD-12 (the key hierarchy and enrolment) in the
[architecture spine](../../_bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md).
To try it on a bench, use the [Hub setup checklist](../bench/hub-setup-checklist.md) and the
[Node setup checklist](../bench/node-setup-checklist.md).

## How it works

### The setup code

On first boot the Device draws 40 bits from its TRNG and writes them as 8 characters of the
Crockford base32 alphabet (digits and uppercase letters without I, L, O and U). See
`packages/rs/setup/src/code.rs` (`SetupCode::generate`). The code goes into the `cf_setup` flash
partition once and is then read back on every boot. It is shown in one place only: a plain serial
line `setup code=XXXXXXXX`, printed with `esp_println`, never as a log record.

- The Hub prints it on every boot while it is unprovisioned, and never once it is provisioned.
- The Node draws it on its first long press and prints it each time it enters setup mode.

`SetupCode` deliberately implements neither `Debug` nor `Display`, so it cannot leak into a log by
accident. A corrupt code record is never regenerated, because the old code may already be on a
sticker: the Hub halts, and the Node logs an error and skips setup mode.

### The GATT service and framing

The service UUID is `c01d0001-5e70-4c0d-8f00-00000000c0de`. The app writes to the `…0002`
characteristic and the Device answers with notifications on `…0003` (`SERVICE_UUID` and friends in
`packages/rs/setup/src/lib.rs`). The Device advertises the service UUID, with the name
`Coldframe Hub XXXX` or `Coldframe Node XXXX` (the first four Device ID hex digits) in the scan
response. The BLE address is a static random address derived from the Device ID.

A protocol frame can be larger than one BLE write, so each write or notification is
`header(1) ‖ fragment`. Header `0x01` marks the last fragment of a frame, `0x00` says more follow.
`packages/rs/setup/src/framing.rs` splits frames (`fragments`) and joins them again
(`Reassembler`). A frame that grows past 1152 bytes is dropped, together with its remaining
fragments. The board adapter sizes payloads from the connection's ATT MTU minus 3, between 20 and 248
bytes (`max_payload` in `apps/rs/hub/src/board/ble.rs`). The header byte sits inside that payload,
so a fragment carries at most ATT MTU − 4 bytes, as the `coldframe-setup` crate docs say.

### Opening the session

The first frame on a connection is a plaintext `SessionHello`: protocol version 1 and the app's
ephemeral X25519 public key. The Device draws its own ephemeral private key from the TRNG, answers
with a plaintext `SessionHelloReply` carrying its public key, and both ends now compute the same
X25519 shared secret. A malformed hello, a wrong version or a key that is not 32 bytes closes the
connection without a reply.

### Mixing in the PoP code

`packages/rs/crypto/src/setup.rs` (`derive_keys`) turns the shared secret into two keys:

```rust
// salt = the setup code, uppercased; info = label ‖ app_pub ‖ device_pub
let mut extract = HkdfExtract::<Sha256>::new(Some(salt));
extract.input_ikm(&shared);
hkdf.expand_multi_info(&[SETUP_LABEL.as_bytes(), app_public, device_public], &mut okm)
```

The 64 output bytes split in two: bytes 0..32 seal app → Device, bytes 32..64 seal Device → app.
The X25519 secret alone would give an eavesdropper-proof channel, but not a proof that the app is
talking to *your* Device. With the code as HKDF salt, only someone who read the code from that
Device's serial line (or sticker) derives the right keys. Both public keys go into `info`, so the
keys belong to this one exchange.

### Sealed messages and counters

Every later frame is a `SealedSetupMessage`: protocol version, a `u64` counter and a
ChaCha20-Poly1305 ciphertext of one `SetupMessage`. `SetupSession` in the same file keeps one
counter per direction, both starting at 0. The nonce is four zero bytes followed by the counter in
big-endian; the associated data is the one-byte protocol major. Because each direction has its
own key, the two counter sequences never share a nonce. `SetupSession::open` refuses a counter
that is not above every counter it opened before, so a replayed message does not open twice.

### A wrong code

If the user types the wrong code, the two sides derive different keys and the Device cannot open
the app's first sealed message. `SetupSession::open` reports that first failure as
`WrongSetupCode`. The Device then still sends one `SetupError` (code `MALFORMED_MESSAGE`), sealed
under its own keys, and disconnects. The app cannot open that reply either, and that is how it
knows the code was wrong (`AppError::WrongSetupCode` in `packages/rs/setup/src/app.rs`). Neither
side learns the other's code. A failure on any *later* message means tampering or replay: the
Device closes the connection without a word.

> [!NOTE]
> There is no retry counter or lock-out in the firmware. A wrong code costs one connection; the
> app reconnects and starts a fresh exchange with fresh keys.

### The message flow

`packages/rs/setup/src/session.rs` (`Session`) is the Device side as a state machine without I/O.
It takes whole frames and answers with an `Action`: send, close, scan, join, check the Server,
leave, or persist. `packages/rs/setup/src/service.rs` (`run_setup`) carries those actions out on
real hardware. The message set lives in `packages/proto/coldframe/setup/v1/setup.proto`.

For a **Hub**, a typical session runs:

1. `IdentityRequest` → `Identity`: the Device ID, kind `HUB` and the firmware version.
2. `WifiScanRequest` → `WifiScanList`: one entry per SSID (its strongest access point), strongest
   first, at most 16 networks, each with its security.
3. `SiteBinding`: a Site ID and the Server URL (`https://` plus a host). No reply on success.
4. `EnrolmentRequest` → `EnrolmentResponse` (see below).
5. `WifiConfig` (SSID and passphrase) → `WifiResult`. Accepted only after a binding and an
   enrolment in the same session. The Hub scans again and joins the strongest *supported* access
   point for that SSID (`select_bssid` in `packages/rs/uplink/src/bssid.rs`).

After a successful join the Hub checks the Server before it stores anything: DHCP, SNTP and one
signed heartbeat, all within 40 s (`check_server`). Only an accepted heartbeat stores the
provisioning record and answers `CONNECTED`. Anything else makes the Hub leave the network, store
nothing and answer `NO_SERVER`; the session stays open so the user can retry. Once the record is
stored, a second binding, enrolment or Wi-Fi config is `UNEXPECTED_MESSAGE`.

> [!WARNING]
> WPA3-only Wi-Fi networks are unsupported. They appear in the scan list as
> `WIFI_SECURITY_WPA3_ONLY`, but they are not selectable: joining one answers
> `UNSUPPORTED_SECURITY`. Open, WPA2-Personal and
> WPA2/WPA3 transition networks work; a transition network is joined with WPA2.

A **Node** (`Session::node`) never touches Wi-Fi: `WifiScanRequest` and `WifiConfig` are
`UNEXPECTED_MESSAGE`. Its `SiteBinding` must carry a Site ID and a Lot ID and no Server URL, and
it is only validated: the Server, not the Node, holds the Lot assignment (AD-18). What matters for
a Node is the enrolment.

### Enrolment: K_dev to the Server

The app fetches the Server's X25519 enrolment key over TLS and sends it in `EnrolmentRequest`,
with its SHA-256 fingerprint in hex. The Device refuses a mismatch (`FINGERPRINT_MISMATCH`). This
is an integrity check only, since the fingerprint travels with the key; authenticity comes from
the app's TLS fetch.

The Device then seals its device key `K_dev` to that key with HPKE (RFC 9180 base mode,
DHKEM(X25519, HKDF-SHA256), ChaCha20-Poly1305), `info = "coldframe/enrolment/v1"` and the Device
ID as associated data. See `packages/rs/crypto/src/hpke.rs` (`seal_enrolment`). The ephemeral
HPKE key comes from 32 fresh TRNG bytes. The app relays the `EnrolmentResponse` to the Server
unread: it cannot open it, and neither can anyone listening on BLE.

### What is stored

The `cf_setup` partition (`packages/rs/setup/src/store.rs`) has two records, each in its own
4 KiB sector, each with a magic, a version and a 4-byte SHA-256 check:

- `CFPC` at `0x0000`: the setup code. Written once, on first boot.
- `CFWP` at `0x1000`: the Hub's provisioning record, version 2: SSID, passphrase, Site ID and
  Server URL. Written only after a join and a passed Server check. An erased or corrupt sector
  means unprovisioned. An older version-1 record (no Server address) also counts as corrupt, so
  such a Hub is set up again.

The Node stores nothing during setup except its setup code: no Site, no Lot and no Wi-Fi settings.

### The Node's setup window

A long press starts setup mode on the Node (`setup_mode` in `apps/rs/node/src/main.rs`). It
starts the BLE controller, raises the RTC watchdog to the window plus 30 s, and runs
`run_node_setup` (`packages/rs/setup/src/node.rs`). The window lasts 180 s from the moment it
starts advertising, measured on the RTC uptime. Each connection idles out after 60 s without a
write, and every wait is bounded by what is left of the window. After any connection that handed
out the sealed `K_dev`, the window ends as `enrolled`; otherwise the Node advertises again until
the deadline. Then the BLE controller is dropped and the Node finishes its normal wake.

The Hub has no window: it advertises for as long as it is unprovisioned, and a connection idles
out after 300 s.

## Key constants and timings

| Constant | Value | Where |
| --- | --- | --- |
| `CODE_LENGTH` | 8 Crockford base32 characters (40 bits) | `packages/rs/setup/src/code.rs` |
| `SERVICE_UUID_STR` | `c01d0001-5e70-4c0d-8f00-00000000c0de` (`SERVICE_UUID` is the `u128` form) | `packages/rs/setup/src/lib.rs` |
| `MAX_FRAME` | 1152 bytes per reassembled frame | `packages/rs/setup/src/lib.rs` |
| `HEADER_LAST` / `HEADER_MORE` | `0x01` / `0x00` | `packages/rs/setup/src/framing.rs` |
| `MAX_PAYLOAD` | 248 bytes, the largest BLE payload | `apps/rs/hub/src/board/ble.rs`, `apps/rs/node/src/board/ble.rs` |
| `SETUP_LABEL` | `coldframe/setup/v1` (HKDF info prefix) | `packages/rs/crypto/src/spec.rs` |
| `SETUP_OKM_LENGTH` | 64 bytes, two 32-byte direction keys | `packages/rs/crypto/src/spec.rs` |
| `SETUP_FIRST_COUNTER` | 0, per direction | `packages/rs/crypto/src/spec.rs` |
| `ENROLMENT_INFO` | `coldframe/enrolment/v1` (HPKE info) | `packages/rs/crypto/src/spec.rs` |
| `SESSION_IDLE_TIMEOUT_MS` | 300 000 ms (Hub) | `packages/rs/setup/src/lib.rs` |
| `SETUP_WINDOW_MS` | 180 000 ms (Node) | `packages/rs/setup/src/node.rs` |
| `NODE_SESSION_IDLE_TIMEOUT_MS` | 60 000 ms (Node) | `packages/rs/setup/src/node.rs` |
| `SERVER_CHECK_TIMEOUT_MS` | 40 000 ms (Hub Server check) | `packages/rs/uplink/src/lib.rs` |
| `MAX_NETWORKS` | 16 networks per scan list | `packages/rs/setup/src/lib.rs` |
| `PROVISIONING_VERSION` | 2 (`CFWP` record) | `packages/rs/setup/src/store.rs` |

## Where to read the code

- `packages/rs/crypto/src/setup.rs` (`derive_keys`, `SetupSession`): the key derivation with the
  PoP code as salt, the per-direction counters and the wrong-code signal.
- `packages/rs/crypto/src/hpke.rs` (`seal_enrolment`, `fingerprint`): HPKE sealing of `K_dev` to
  the Server's enrolment key.
- `packages/rs/setup/src/lib.rs`: the crate overview and the transport contract.
- `packages/rs/setup/src/code.rs` (`SetupCode`): drawing, parsing and hiding the setup code.
- `packages/rs/setup/src/framing.rs` (`fragments`, `Reassembler`): frames over BLE payloads.
- `packages/rs/setup/src/session.rs` (`Session`): the Device-side state machine; its module docs
  list every Hub and Node rule.
- `packages/rs/setup/src/service.rs` (`run_setup`): the Hub's loop that drives link, Wi-Fi, Server
  check and flash.
- `packages/rs/setup/src/node.rs` (`run_node_setup`): the Node's time-bounded window.
- `packages/rs/setup/src/store.rs` (`load_or_create_code`, `store_provisioning`): the `cf_setup`
  records.
- `packages/rs/setup/src/app.rs` (`AppClient`): the app side in Rust, used by host tests and the
  desktop bench client in `tests/rs/setup-client`.
- `apps/rs/node/src/main.rs` (`setup_mode`): how a long press brings up BLE and the window.
- `apps/rs/hub/src/board/ble.rs` and `apps/rs/node/src/board/ble.rs` (`BoardSetupLink`): the
  TrouBLE GATT server and advertising behind the `SetupLink` trait.
- `packages/proto/coldframe/setup/v1/setup.proto`: the message set, with the wrong-code rule in
  its header comment.
- Hub and Node overviews: [apps/rs/hub/README.md](../../apps/rs/hub/README.md),
  [apps/rs/node/README.md](../../apps/rs/node/README.md),
  [packages/rs/README.md](../../packages/rs/README.md).
