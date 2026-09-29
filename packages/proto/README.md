# packages/proto

The Protobuf contract between Devices, the app and the Server (AD-10, AD-25), a buf v2 module
([`buf.yaml`](buf.yaml)).

| File | Package | What |
| --- | --- | --- |
| [`coldframe/setup/v1/setup.proto`](coldframe/setup/v1/setup.proto) | `coldframe.setup.v1` | The BLE setup protocol, one message set for Hub and Node: the plaintext key exchange (`SessionHello`, `SessionHelloReply`), `SealedSetupMessage`, and the sealed `SetupMessage` steps (identity, Wi-Fi scan list, Wi-Fi config and result, Site binding, enrolment request and response, errors) |
| [`coldframe/device/v1/envelope.proto`](coldframe/device/v1/envelope.proto) | `coldframe.device.v1` | `SealedEnvelope` for uplink frames and downlinks, and the sealed `Downlink` with its empty `commands` (AD-16) |

The ESP-NOW messages, the Node frame payload and the Specification set arrive in Epic 4.

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
commit. A baseline without `.proto` files or without the OpenAPI file passes with a notice. CI's
`contracts` job compares a pull request with its merge base and a push with the previous commit, after
`--self-test` proves that a removed field, a removed operation and a newly required property fail.

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
  `coldframe.setup.v1` at build time with protox and micropb-gen 0.6.0 (no `protoc`): `no_std`
  types with heapless containers of fixed capacity (SSID 32, password 64, Site and Lot ID 36, keys
  and `enc` 32, enrolment ciphertext 48, fingerprint 64, 16 networks), each with a compile-time
  `MAX_SIZE`. `WifiConfig` and `SetupMessage` (and its `Body`) implement no `Debug`.
- The **Kotlin** core arrives with Story 3.6.

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

**The app enrols before it sends `WifiConfig`.** The Server accepts the Hub's heartbeat only once it
knows the Device, so the app posts the `EnrolmentResponse` to `POST /sites/{siteId}/devices` and waits
for its 201 before it sends `WifiConfig`. Otherwise the Server check answers `NO_SERVER`; the app can
then enrol and send the same `WifiConfig` again in the same session.
