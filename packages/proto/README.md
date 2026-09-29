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
- **Rust** (micropb, `packages/rs/protocol`) and the **Kotlin** core arrive with the firmware and app
  stories that use them (3.2, 3.4, 3.6).
