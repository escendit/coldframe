# packages/proto

The Protobuf contract between Devices, the app and the Server (AD-10, AD-25), a buf v2 module
([`buf.yaml`](buf.yaml)).

| File | Package | What |
| --- | --- | --- |
| [`coldframe/setup/v1/setup.proto`](coldframe/setup/v1/setup.proto) | `coldframe.setup.v1` | The BLE setup protocol, one message set for Hub and Node: the plaintext key exchange (`SessionHello`, `SessionHelloReply`), `SealedSetupMessage`, and the sealed `SetupMessage` steps (identity, Wi-Fi scan list, Wi-Fi config and result, Site binding, enrolment request and response, errors) |
| [`coldframe/device/v1/envelope.proto`](coldframe/device/v1/envelope.proto) | `coldframe.device.v1` | `SealedEnvelope` for uplink frames and downlinks; `NodeFrame`, the plaintext of an uplink (one wake report: `Reading`s with their `Quantity` and `reading_seq`, the time as synced `measured_at_ms` or `Unsynced` boot ID and uptime, `report_seq`, battery and `ChargeStatus`, the `spec_hash` and, on request, the `SpecificationSet`); and the sealed `Downlink` with `acked_readings` (`ReadingSeqRange`, both ends included), its empty `commands` and `specifications_unknown` (AD-9, AD-11, AD-16, AD-19) |

The Node ⇄ Hub radio messages are not Protobuf: they are a kind byte around these envelopes, see
[ESP-NOW transport](#esp-now-transport).

## Specifications

A Node declares its Sensors with a `SpecificationSet` (AD-19): one `Specification` per Sensor, 1 to 32 of
them, the index being the Sensor's slot. A `Specification` holds the `Quantity`, the `Unit` of the raw
value, the range (`range_min` < `range_max`), whether two-point Calibration applies (`calibration`), and
optional default Thresholds (`default_low` < `default_high`): in percent, 0 to 100, for a calibrating
Sensor, otherwise in `unit` and within the range.

- Every `NodeFrame` carries `spec_hash`: SHA-256 of the Node's serialized set (1 to 32 bytes). The Server
  never recomputes it; it only compares it with the hash of the last set it accepted from that Device.
- When they differ, or the frame has no hash, the `Downlink` says `specifications_unknown`. The Node then
  attaches the set (`NodeFrame.specifications`) to its next frame, once per request. A lost frame is asked
  for again by the next `Downlink`.
- A set that breaks the rules above is ignored: the frame's Readings are stored and acknowledged all the
  same, and the `Downlink` keeps asking.

## Versioning

- Every top-level frame and envelope carries `uint32 protocol_version = 1`; the current major is `1`
  (`PROTOCOL_MAJOR` in [`packages/crypto-spec`](../crypto-spec)). The Server accepts the current and the
  previous major.
- Within a major, changes are additive only. A field number is never reused: when a field goes, reserve
  its number and name (`reserved 4; reserved "old_name";`).
- A wire break is a new major in a new package (`coldframe.device.v2`), shipped in a release whose
  Server still accepts the old one (AD-23).

## Checks

[`check-compat.sh`](check-compat.sh) runs `buf lint` (STANDARD), then `buf breaking` (FILE rules) on
this folder and `oasdiff breaking --fail-on ERR` on
[`packages/openapi/coldframe.openapi.json`](../openapi/coldframe.openapi.json), both against a baseline
commit. An operation that carries `x-coldframe-planned` in the baseline was never served, so it is left
out of the OpenAPI comparison: the story that serves it may replace it. A baseline without `.proto` files or without the OpenAPI file passes with a notice. CI's
`contracts` job compares a pull request with its merge base and a push with the previous commit, after
`--self-test` proves that a removed field, a removed operation and a newly required property fail, and
that removing or replacing a planned operation passes while the same change to a served one fails.

```sh
packages/proto/install-tools.sh "$HOME/.local/bin"   # pinned buf and oasdiff, sha256-checked (Linux x86_64)
packages/proto/check-compat.sh --self-test
packages/proto/check-compat.sh                        # against the merge base with origin/main
packages/proto/check-compat.sh --base <git-ref>
```

## Generated code

- **C#**: [`packages/cs/protocol`](../cs/protocol) (`Coldframe.Protocol`) compiles these files at build
  time with Grpc.Tools into Google.Protobuf types (`Coldframe.Protocol.Setup.V1`,
  `Coldframe.Protocol.Device.V1`).
- **Rust**: [`packages/rs/protocol`](../rs/protocol) (`coldframe-protocol`) generates
  `coldframe.setup.v1` and `coldframe.device.v1` at build time with protox and micropb-gen 0.6.0
  (no `protoc`): `no_std` types with heapless containers of fixed capacity, each with a
  compile-time `MAX_SIZE`.
  - Setup: SSID 32, password 64, Site and Lot ID 36, keys and `enc` 32, enrolment ciphertext 48,
    fingerprint 64, 16 networks. `WifiConfig` and `SetupMessage` (and its `Body`) implement no
    `Debug`.
  - Device: sized for a Node with four Sensors. Device ID 8, envelope ciphertext 240 (more than one
    radio message carries), `spec_hash` 32, 4 Readings, 4 Specifications, 8 acknowledged ranges,
    4 commands. A frame or downlink over these fails to decode.
  - micropb writes a `oneof` after every other field. `encode_node_frame` writes a `NodeFrame` in
    field-number order instead, the bytes the shared vector of
    [`packages/crypto-spec`](../crypto-spec) pins; any decoder reads both.
  - The `radio` module is the codec of the [ESP-NOW messages](#esp-now-transport).
- **Kotlin**: [`packages/kt/core`](../kt/core) generates `coldframe.setup.v1` at build time with the
  Wire 7.1.0 Gradle plugin (`generateCommonMainProtos`, into `build/generated/source/wire`) for
  every target of the core (JVM, Android, iOS). The app side of the session is
  `com.escendit.coldframe.core.setup.SetupSession` over the pure-Kotlin crypto in
  `com.escendit.coldframe.core.crypto` (X25519, HKDF-SHA256, ChaCha20-Poly1305), checked against
  [`vectors.json`](../crypto-spec/vectors.json) on every target and against the JDK on the JVM.
  `HubSetupEngine` runs the Hub message order below over Kable 0.45 (`KableSetupRadio`).

## ESP-NOW transport

A Node talks to a Hub over ESP-NOW (AD-9), because it reaches Lots beyond usable Wi-Fi. A message is
one ESP-NOW v1 payload of at most 250 bytes: one kind byte, then the body. The Rust codec is
`coldframe_protocol::radio`; the Node's side is [`coldframe-transport`](../rs/transport), the Hub's
`coldframe_uplink::relay`.

| Kind | Name | Direction | Body |
| --- | --- | --- | --- |
| `0x01` | Probe | Node → broadcast (`ff:ff:ff:ff:ff:ff`) | an 8-byte nonce |
| `0x02` | ProbeReply | Hub → Node, unicast | the probe's nonce, then one byte `pending`: how many downlinks the Hub holds for this Node and sends next, `0x00` to `0x08` |
| `0x03` | Uplink | Node → Hub, unicast | the bytes of one `SealedEnvelope` whose payload is a `NodeFrame` |
| `0x04` | Downlink | Hub → Node, unicast | the bytes of one `SealedEnvelope` whose payload is a `Downlink` |

A message of another kind, of the wrong length, with a `pending` over 8 or with an empty
envelope is dropped. An envelope is at most 249 bytes. A frame that would not fit with its
`SpecificationSet` goes without it, and the Server asks again.

- **Addressing.** A Node has no stored Hub address. It broadcasts a Probe and talks to the Hub whose
  ProbeReply echoes its nonce first: that Hub's station MAC address is the unicast address for the
  wake. Any enrolled Hub may relay any Node. A Hub addresses a Node by the source MAC address of
  its frames and keeps the Node's downlinks under that address, in RAM only. A Hub answers a Probe
  only while it can relay: associated to its access point, with its clock set.
- **Channel.** ESP-NOW and the Hub's Wi-Fi station share one radio, so the Hub is on its access
  point's channel and follows it. A Node probes the channel a Hub last answered on. Without one,
  or after 3 consecutive wakes without a fresh downlink, it probes that channel and then channels
  1 to 13, 120 ms each, and uses the first where a Hub answers. A scan that finds no Hub is not
  repeated before 4 wakes have passed.
- **Trust.** Probes and their replies are not authenticated: a forged reply costs a Node one wasted
  burst and can neither delete a Reading nor reset its miss count. Everything else is sealed end to
  end. The Hub relays both envelopes unread: to the Server as standard padded base64 in
  `POST /device/ingest` ([`packages/openapi`](../openapi)), and `results[i].downlink` back to the
  sender of `frames[i]`. It builds no acknowledgement.
- **Delivery.** A Node listens 300 ms after the last frame of a wake (AD-17). It deletes a buffered
  Reading only when a Downlink opens under its `ack/v1` key, names its Device ID and protocol
  version 1, and holds the `reading_seq` in `acked_readings`. What the radio reports for a send is
  never delivery. Every transmission, a resend included, is a freshly sealed envelope under a new
  counter.
- **Late acknowledgement.** The Hub opens a TLS connection per request, which takes longer than
  the window, so a Downlink usually arrives after the Node has gone back to sleep. The Hub sends it
  once when it arrives and keeps it: up to 8 per Node, in arrival order, so every frame of a burst
  keeps its own. At the Node's next Probe it answers with their number in `pending`, sends them
  once more, oldest first, and drops them. The Node applies them before it sends anything, and
  stops listening for them once it has applied `pending` Downlinks or 120 ms have passed.

## BLE transport

The setup protocol runs over one GATT service (AD-25). The Rust constants are in
[`coldframe-setup`](../rs/setup/src/lib.rs).

| What | UUID | Direction |
| --- | --- | --- |
| Service | `c01d0001-5e70-4c0d-8f00-00000000c0de` | advertised in the advertising data while the Device is unprovisioned |
| Write characteristic | `c01d0002-5e70-4c0d-8f00-00000000c0de` | app → Device, write with response |
| Notify characteristic | `c01d0003-5e70-4c0d-8f00-00000000c0de` | Device → app, notifications (subscribe before the first write) |

- **Fragments.** Every write and every notification is `header(1) ‖ fragment`. Header `0x01` marks
  the last fragment of a frame, `0x00` says more follow; any other header is malformed. A fragment is
  at most ATT MTU − 4 bytes (MTU 23 → 19, MTU 251 → 247). A reassembled frame is at most 1152 bytes
  (`MAX_FRAME`, at least the largest `SealedSetupMessage`, 1117 bytes). A bad header or an oversize
  frame drops the partial frame.
- **Frame order.** The first frame of a connection is a `SessionHello`; the Device answers
  `SessionHelloReply` with a fresh X25519 key. Every later frame in either direction is a
  `SealedSetupMessage`. A malformed `SessionHello` ends the connection without a reply.
- **Errors.** Once the session is open, a frame that does not parse, carries another
  `protocol_version`, or has a field of the wrong length is answered with a sealed `SetupError`
  `MALFORMED_MESSAGE`, and the session continues. The first sealed message that does not open means a
  wrong setup code: the Device answers one `SetupError` (`MALFORMED_MESSAGE`) sealed under its own
  keys and disconnects, so the app's failure to open it says "wrong setup code". A later message that
  does not open, or whose counter does not increase, ends the connection without a reply.
- **Idle timeout.** A connection with no write for 300 s is dropped and the Device advertises again.

### Hub message order

1. `IdentityRequest` → `Identity` (Device ID, `DEVICE_KIND_HUB`, firmware version). Allowed at any time.
2. `WifiScanRequest` → `WifiScanList`: one entry per SSID (its strongest access point), strongest first,
   at most 16; hidden SSIDs are left out; WPA3-only networks appear as `WIFI_SECURITY_WPA3_ONLY`.
   Allowed at any time.
3. `SiteBinding`: a non-empty `site_id`, no `lot_id` (a Hub has no Lot; `lot_id` is
   `MALFORMED_MESSAGE`), and a `server_url`: `https://` + a lowercase DNS host + an optional `:port`
   (1–65535), with no path, query, fragment, userinfo or IP literal, at most 100 bytes (for example
   `https://coldframe.example.org` or `https://coldframe.example.org:8443`). A missing or invalid
   `server_url` is `MALFORMED_MESSAGE`. A Node omits `server_url`. **No reply on success**; a failure
   is a `SetupError`.
4. `EnrolmentRequest` → `EnrolmentResponse` (`K_dev` sealed with HPKE to the key). A key that is not 32
   bytes is `MALFORMED_MESSAGE`; a fingerprint that does not match is `FINGERPRINT_MISMATCH` and nothing
   is sealed.
5. `WifiConfig` → `WifiResult`. Only after steps 3 and 4 in the same session, otherwise
   `UNEXPECTED_MESSAGE`. The Hub scans, picks the strongest BSSID of the SSID and joins it:
   `NETWORK_NOT_FOUND` when the SSID was not heard, `UNSUPPORTED_SECURITY` for WPA3-only and other
   unsupported networks (no join attempted), `WRONG_PASSWORD` (the session stays open for a retry).
   After a successful join the Hub checks the Server within 40 s: it brings up IP (DHCP), sets its
   clock (SNTP) and sends one signed `POST /device/heartbeat` to `server_url`. An HTTP 200 with a valid
   `serverTime` stores the provisioning record and answers `CONNECTED`. Anything else (no IP, DNS, SNTP
   or TLS, a non-200 such as a 401 for a Hub the Server does not know, or the 40 s budget running out)
   answers `NO_SERVER`: the Hub leaves the network, stores nothing, and the session stays open for a
   retry. Any other join failure is `SetupError` `INTERNAL`. Nothing is stored on the Hub unless the
   join and the Server check succeed.

Messages only the Device sends (`Identity`, `WifiScanList`, `WifiResult`, `EnrolmentResponse`) are
`UNEXPECTED_MESSAGE` when the app sends them. A `SetupError` from the app is ignored. After
`CONNECTED` the Hub answers `SiteBinding`, `EnrolmentRequest` and `WifiConfig` with
`UNEXPECTED_MESSAGE`, finishes the session (the app disconnects, or the idle timeout) and stops
advertising for good.

**WPA3-only networks are not supported.** The Hub joins with WPA2 (including WPA2/WPA3 transition
networks); a WPA3-only or enterprise network is listed as `WIFI_SECURITY_WPA3_ONLY` or
`WIFI_SECURITY_OTHER`, and the apps show it hatched and unselectable with "Not supported: the Hub
needs WPA2 or mixed WPA2/WPA3."

**The app enrols before it sends `WifiConfig`.** The Server accepts the Hub's heartbeat only once it
knows the Device, so the app posts the `EnrolmentResponse` to `POST /sites/{siteId}/devices` and waits
for its 201 before it sends `WifiConfig`. Otherwise the Server check answers `NO_SERVER`; the app can
then enrol and send the same `WifiConfig` again in the same session.
