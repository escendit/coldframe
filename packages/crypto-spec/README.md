# packages/crypto-spec

The Device crypto contract (AD-12, AD-17, AD-25), defined once:

- [`crypto-spec.json`](crypto-spec.json): the normative labels, algorithms, sizes, nonce and AAD
  layouts, the setup-session derivation and the heartbeat format.
- [`vectors.json`](vectors.json): the shared test vectors, which Rust, C# and Kotlin must all reproduce.

[`scripts/generate.ts`](scripts/generate.ts) writes the constants for every language, so no language
types a label, size or header name by hand:

| Output | Used by |
| --- | --- |
| [`packages/rs/crypto/src/spec.rs`](../rs/crypto/src/spec.rs) | `coldframe-crypto` (firmware, `no_std`) |
| [`packages/cs/crypto/Generated/CryptoSpec.g.cs`](../cs/crypto/Generated/CryptoSpec.g.cs) | `Coldframe.Crypto` (Server, Device simulator) |
| [`packages/kt/core/generated/kotlin/.../crypto/CryptoSpec.kt`](../kt/core/generated/kotlin/com/escendit/coldframe/core/crypto/CryptoSpec.kt) | the KMP core (`commonMain`) |
| [`vectors.json`](vectors.json) | the vector tests in `tests/rs/crypto`, `tests/cs/crypto.tests`, `tests/kt/core` |

```sh
pnpm --filter @coldframe/crypto-spec run generate   # after changing crypto-spec.json; commit every output
pnpm --filter @coldframe/crypto-spec run check      # fails when an output is stale
```

## The contract

| What | Construction |
| --- | --- |
| Device key | `K_dev = HMAC-SHA256(key = root, msg = "coldframe/device/v1")`; on hardware the eFuse HMAC (ToUser) computes it |
| Purpose keys | `HKDF-SHA256(salt = ∅, ikm = K_dev, info = label, L = 32)`, labels `seal/v1` (uplink), `ack/v1` (downlink), `hub-auth/v1` |
| Device ID | `HKDF-SHA256(K_dev, info = "device-id/v1", L = 8)`; text form 16 lowercase hex digits |
| Frames and downlinks | ChaCha20-Poly1305; nonce = `device_id[0..4] ‖ counter_u64_be`; AAD = `u8 protocol_major ‖ device_id ‖ counter_u64_be` (17 bytes); 64-entry replay window |
| Enrolment | HPKE (RFC 9180) base mode, DHKEM(X25519, HKDF-SHA256) / HKDF-SHA256 / ChaCha20Poly1305; `info = "coldframe/enrolment/v1"`, `aad = device_id`, `pt = K_dev`; fingerprint = lowercase hex SHA-256 of the raw public key |
| Setup session | `okm = HKDF-SHA256(salt = UTF-8(uppercase PoP code), ikm = X25519, info = "coldframe/setup/v1" ‖ app_pub ‖ device_pub, L = 64)`; bytes 0..32 app → Device, 32..64 Device → app; nonce = `0x00000000 ‖ counter_u64_be` per direction from 0; AAD = `u8 protocol_major`; the first message that does not open means a wrong code |
| Heartbeat | lowercase hex `HMAC-SHA256(hub-auth key, METHOD\nPATH\nhex(SHA-256(body))\nTIMESTAMP_MS\nNONCE_HEX)` in `X-Coldframe-Signature`, with `X-Coldframe-Device`, `X-Coldframe-Timestamp` (Unix ms, ±300000 ms) and `X-Coldframe-Nonce` (16 bytes as 32 hex digits) |

HPKE is composed from X25519, HKDF-SHA256 and ChaCha20-Poly1305 exactly as RFC 9180 specifies it,
and every implementation must pass the RFC's own vector (below) before our vectors mean anything.

## The vectors

Byte strings are lowercase hex; 64-bit counters and timestamps are decimal strings. Categories:
`keyHierarchy`, `frames` (uplink with `seal/v1`, downlink with `ack/v1`), `replay` (a counter sequence
with the expected accept/reject), `enrolment` (fixed recipient key and `ikmE`, so the ephemeral key is
deterministic), `setup` (keys, a wrong code, and sealed messages in both directions), `heartbeat`, and
`anchors`: RFC 5869 A.1, RFC 7748 §6.1, RFC 8439 §2.8.2 and RFC 9180 A.2.1 (first encryption), copied
verbatim from the RFC texts.

The generator computes the vectors with a reference implementation on Node's crypto module
([`scripts/lib/reference.ts`](scripts/lib/reference.ts)) and refuses to write them unless that
implementation reproduces every RFC anchor. Rust, C# and Kotlin then reproduce every vector
independently, and each also proves the negative cases: a wrong PoP code fails with a distinct
wrong-code error, a tampered ciphertext or changed AAD fails, and a replayed counter is refused.

The vector keys are public test values. `.gitleaks.toml` allowlists `vectors.json`; never use them for
a real Device.
