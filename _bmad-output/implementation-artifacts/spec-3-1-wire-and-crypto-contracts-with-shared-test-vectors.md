---
title: 'Story 3.1: Wire and crypto contracts with shared test vectors'
type: 'feature'
created: '2026-09-28'
status: 'done'
baseline_revision: '88b95f1f7f887f7af2e26c00446a3fe7a53a3469'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md'
warnings: ['oversized']
deferred:
  - summary: >-
      No normative rule says who owns and persists the Server's downlink (ack/v1) counter across restarts and failover.
    evidence: |-
      SealedEnvelope.counter is "never repeats" but crypto-spec.json and the READMEs name no owner or persistence rule; a reused counter reuses a ChaCha20-Poly1305 nonce. Belongs to the Server downlink work (Stories 3.5 / 4.5).
    location: >-
      packages/crypto-spec/crypto-spec.json (frame), packages/proto/coldframe/device/v1/envelope.proto
    severity: medium
  - summary: >-
      ReplayWindow (Rust and C#) cannot be saved and restored, so the Device grain cannot keep it across activations.
    evidence: |-
      Both keep highest/seen private with only an empty constructor; the Server must persist the window (AD-17) when it verifies uplinks in Story 3.5 / 4.5.
    location: >-
      packages/cs/crypto/Frames.cs, packages/rs/crypto/src/frame.rs
    severity: medium
  - summary: >-
      The simulator's envelope Device-ID and protocol-version checks are not pinned by any test.
    evidence: |-
      SimulatorTests only exercise a different Device's keys (AEAD fails first) and never a protocol_version other than 1; removing either check in Envelopes.Open keeps all tests green. Pin when the real Device grain lands (Story 3.5).
    location: >-
      tests/cs/device-simulator/SimulatedDevice.cs (Envelopes.Open)
    severity: medium
  - summary: >-
      The PoP code only salts HKDF over an unauthenticated X25519 exchange, so an active man-in-the-middle can brute-force a short code offline from one captured message; no minimum code entropy is specified.
    evidence: |-
      Inherent to the AD-25 design (not a PAKE); R-06 already calls for an external crypto review before V1. Code length/alphabet/entropy should be fixed with the firmware PoP generation (Story 3.2 / 3.4) and the review.
    location: >-
      packages/crypto-spec/crypto-spec.json (setup)
    severity: medium
---

<intent-contract>

## Intent

**Problem:** Firmware (Rust), Server (C#) and app (Kotlin) must agree byte-for-byte on BLE setup messages, Device REST endpoints, key derivation, HPKE enrolment sealing, AEAD nonce layout, heartbeat signing and setup-session keys. Today `packages/proto` and `packages/crypto-spec` are README-only, `packages/openapi` has no Device operations, and no crypto code or vectors exist in any language (risk R-06, tests T-09/T-36).

**Approach:** Define the wire contracts once (`packages/proto`, `packages/openapi`) and the crypto contract once (`packages/crypto-spec/crypto-spec.json` + `vectors.json`), generate constants into Rust, C# and Kotlin with a checked-in generator (mirroring `packages/design-tokens`), implement the primitives per language, prove all three reproduce the same shared vectors (anchored to RFC vectors), add a `tests/cs` Device simulator, and gate breaking `.proto`/OpenAPI changes in CI.

## Boundaries & Constraints

**Always:**
- Every top-level Protobuf frame/envelope carries `uint32 protocol_version = 1`; current major is `1`. Field numbers never reused; reserve removed ones.
- Byte-level decisions in Design Notes are normative; they live in `crypto-spec.json` and flow to generated constants — no language hand-types a label, size or header name.
- Generated files are committed, carry a "generated — do not edit" header, and a `--check` mode plus freshness test fail when stale.
- Vector tests read the single `packages/crypto-spec/vectors.json` at test time; every language asserts every vector category.
- Include verbatim external anchors in `vectors.json`: RFC 5869 A.1 (HKDF-SHA256), RFC 7748 §6.1 (X25519), RFC 8439 §2.8.2 (ChaCha20-Poly1305), RFC 9180 A.2.1 base-mode first encryption (DHKEM(X25519)/HKDF-SHA256/ChaCha20Poly1305). Copy values from the RFC texts, never from our own output.
- Negative tests in all three languages: wrong PoP code fails setup-session decryption with a distinct error, tampered ciphertext/AAD fails, replayed/non-increasing counter is rejected.
- Pin every new dependency / CI tool to an exact version (SHA-pinned actions, checksum-verified binaries); update lock files (`Cargo.lock`, `packages.lock.json`, Gradle catalog). Keys/secrets never logged.
- Tests live under `tests/<lang>/` (only Rust unit tests inline); new projects registered in root `Cargo.toml`, `Coldframe.slnx`, `settings.gradle.kts`, `pnpm-workspace.yaml` as applicable. Existing CI jobs keep passing (`dotnet build -warnaserror`, `cargo clippy -D warnings`, `./gradlew check`, pnpm test/lint/typecheck).

**Never:**
- No Server endpoint implementation, HMAC verification middleware, enrolment persistence, firmware eFuse/HAL code, BLE GATT service, or KMP production crypto — those are Stories 3.2–3.6.
- No Espressif provisioning/Improv, no mTLS, no custom primitives beyond composing standard ones (HPKE may be composed from X25519+HKDF+ChaCha20-Poly1305 per RFC 9180 only if it passes the RFC anchor).
- Don't edit `_bmad-output/implementation-artifacts/sprint-status.yaml`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Key hierarchy | vector root key | identical `K_dev`, `seal/v1`, `ack/v1`, `hub-auth/v1` keys, Device ID in Rust/C#/Kotlin | none |
| HPKE enrolment | recipient keypair, ikmE, `K_dev`, Device ID | identical `enc`+ciphertext; C# and Kotlin open it back to `K_dev` | wrong recipient key / altered AAD → open fails |
| Frame AEAD | purpose key, Device ID, counter, plaintext | identical nonce, AAD, ciphertext | tamper → auth failure; counter ≤ last seen or outside 64-window → replay rejection |
| Setup session | both X25519 keys, PoP code | identical app→hub / hub→app keys and first sealed message | wrong PoP → distinct `WrongSetupCode`-style error, never a generic crash |
| Heartbeat HMAC | method, path, body, timestamp, nonce | identical canonical string and signature hex | altered body/path → signature mismatch |
| Contract compat | `.proto` field removed / OpenAPI op or required-ness broken vs baseline | compat check exits non-zero | missing baseline file → check passes with explicit "no baseline" notice |

</intent-contract>

## Code Map

- `packages/proto/README.md`, `packages/crypto-spec/README.md`, `packages/asyncapi/README.md` -- README-only placeholders to replace/extend.
- `packages/openapi/coldframe.openapi.json` -- OpenAPI 3.1, `bearer` scheme, every op has `x-coldframe-minimum-role` (SiteRole | `Authenticated`); `packages/openapi/README.md` op table (L24 says Device endpoints arrive in Epic 3).
- `tests/cs/server.tests/Edge/EdgeEndpointDiscoveryTests.cs:47-55` -- asserts mapped endpoints == contract ops; `apps/cs/server/Edge/EdgeAccessRule.cs` knows only SiteRole/`Authenticated`; integration `AuthorizationMatrixTests` needs a sample per mapped endpoint; `apps/cs/README.md:181-195` "Add an endpoint".
- `tests/ts/api-client/index.test.ts:29` -- fails until `pnpm --filter @coldframe/api-client generate` re-run after contract edits.
- `packages/design-tokens/scripts/generate.ts`, `scripts/lib/paths.ts`, `tests/ts/design-tokens/freshness.test.ts`, `.github/workflows/ci.yml:161-164` -- codegen + `--check` + freshness precedent to mirror; Kotlin srcDir pattern `packages/kt/design-tokens/build.gradle.kts:41-43`.
- `packages/rs/hal/`, `tests/rs/hal/` + root `Cargo.toml:5-6` -- crate/test-crate layout (edition 2024, `unsafe_code=forbid`, `missing_docs=warn`); `Cargo.lock` currently has no third-party crates.
- `Coldframe.slnx:11-18`, `Directory.Packages.props` (CPM, transitive pinning), `Directory.Build.props:14-15` (lock files, warnings as errors), `tests/cs/server.tests/*.csproj` (xunit.v3, `OutputType=Exe`) -- .NET registration pattern. .NET 10 has HKDF/HMACSHA256/ChaCha20Poly1305 but no X25519/HPKE.
- `packages/kt/core/build.gradle.kts:17,108-122` (namespace `com.escendit.coldframe.core`, jvmToolchain 25, tests via srcDirs in `tests/kt/core`), `settings.gradle.kts:20-27`, `gradle/libs.versions.toml` -- JDK 25 has X25519, ChaCha20-Poly1305, `KDF` HKDF-SHA256, HmacSHA256; no HPKE.
- `.github/workflows/ci.yml` -- jobs `dotnet` L23-52, `rust` L54-80, `kotlin` L82-111, `typescript` L113-164; new vector tests ride existing jobs; add a `contracts` job after L164. No git tags exist.
- `.gitleaks.toml` -- only BMAD allowlist; vector private keys will need a path allowlist.
- `docs/quickstart.md:190-278` -- test command docs to update.

## Tasks & Acceptance

**Execution:**
- `packages/proto/coldframe/setup/v1/setup.proto` -- BLE setup messages: `SessionHello`/`SessionHelloReply` (plaintext X25519 keys), `SealedSetupMessage{protocol_version,counter,ciphertext}`, inner `SetupMessage` oneof: `Identity`, `WifiScanRequest`, `WifiScanList`(ssid, bssid, rssi, security enum incl. `WPA3_ONLY`), `WifiConfig`, `WifiResult`(status enum incl. wrong password / no server), `SiteBinding`(site_id, optional lot_id), `EnrolmentRequest`(server public key, fingerprint), `EnrolmentResponse`(device_id, enc, ciphertext), `SetupError` -- AD-10/AD-25 single message set.
- `packages/proto/coldframe/device/v1/envelope.proto` -- `SealedEnvelope{protocol_version,device_id,counter,ciphertext}` (uplink frames and downlinks), `Downlink{protocol_version,acked_counter,server_time_ms,repeated Command commands}` with empty `Command` -- AD-12/16/17.
- `packages/proto/buf.yaml` + `README.md` -- buf v2 module, lint + `breaking` (FILE) config; README: versioning rules, how to run checks.
- `packages/openapi/coldframe.openapi.json` + `README.md` -- add `deviceHmac` security scheme (headers per Design Notes); `POST /device/heartbeat` (min role `Device`, response `serverTime`), `POST /device/ingest` (min role `Device`, `application/octet-stream` placeholder, 202), `GET /enrolment-key` (`Authenticated`; `publicKey` base64url, `fingerprint`), `POST /sites/{siteId}/devices` (`Administrator`, `Idempotency-Key`, body `deviceId`,`kind`,`enc`,`ciphertext`; 201 / RFC 9457 problems). Mark each with `x-coldframe-planned: "<story>"` (3.5, 4.x, 3.3, 3.3).
- `tests/cs/server.tests/Edge/EdgeEndpointDiscoveryTests.cs` (+ any matrix test helper) -- treat `x-coldframe-planned` ops as "must NOT be mapped yet"; all others must be mapped exactly as today. Regenerate TS api-client (`pnpm --filter @coldframe/api-client generate`) and commit output.
- `packages/crypto-spec/crypto-spec.json`, `vectors.json`, `package.json`, `scripts/generate.ts` (+ `--check`), `README.md` -- normative spec + vectors; generator emits constants to `packages/rs/crypto/src/spec.rs`, `packages/cs/crypto/Generated/CryptoSpec.g.cs`, `packages/kt/core/generated/kotlin/com/escendit/coldframe/core/crypto/CryptoSpec.kt` (commonMain srcDir); register in `pnpm-workspace.yaml`; freshness test `tests/ts/crypto-spec/freshness.test.ts`; CI `check` step next to design-tokens.
- `packages/rs/crypto` (no_std, workspace member) -- `derive_device_key` (software HMAC stand-in for eFuse), purpose keys, Device ID, frame nonce/AAD + seal/open with 64-entry replay window, HPKE base seal (deterministic-ephemeral variant for vectors), setup-session keys + seal/open, heartbeat canonical string + signature. Deps: `hkdf`,`hmac`,`sha2`,`chacha20poly1305 =0.11.0`,`x25519-dalek` (no default features where possible).
- `tests/rs/crypto` -- vector runner over all categories + RFC anchors + negative tests.
- `packages/cs/crypto` (Coldframe.Crypto, net10) -- same API surface; BouncyCastle.Cryptography for X25519 (and HPKE if its API supports a supplied ephemeral, else RFC 9180 composition); `packages/cs/protocol` (Coldframe.Protocol) compiling `packages/proto/**/*.proto` via Grpc.Tools + Google.Protobuf (spine pin 3.36.2).
- `tests/cs/device-simulator` (Coldframe.DeviceSimulator library) -- `SimulatedDevice.Create(rootKey|random)`, `SealEnrolment(serverPublicKey, siteId)`, `SignHeartbeat(method,path,body,time,nonce)` → headers, `SealFrame`/`OpenDownlink`, `SealDownlink`/`VerifyFrame` server-side helpers, all built on Coldframe.Crypto + Coldframe.Protocol.
- `tests/cs/crypto.tests` -- xunit.v3 vector runner, negative tests, simulator tests asserting simulator outputs equal `vectors.json`; add all new projects to `Coldframe.slnx`, lock files committed.
- `tests/kt/core/jvmTest/.../crypto/` (or `tests/kt/crypto-spec`) -- JVM vector runner (JDK providers; BouncyCastle only if needed, via version catalog) using generated `CryptoSpec` constants; includes negative tests.
- `.github/workflows/ci.yml` -- `contracts` job: pinned `buf` (`buf lint`, `buf breaking` vs `origin/<base>` merge-base, or previous commit on push) and pinned `oasdiff breaking --fail-on ERR` vs baseline `coldframe.openapi.json`; baseline file absent → pass with notice. Script it as `packages/proto/check-compat.sh` (callable locally) plus a test (`tests/ts/contracts/compat.test.ts` or shell self-test) proving a removed field / removed op makes it exit non-zero.
- `.gitleaks.toml` -- allowlist `packages/crypto-spec/vectors.json` only. `docs/quickstart.md`, package READMEs -- document generate/check/vector/compat commands.

**Acceptance Criteria:**
- Given `packages/proto`, when inspected, then it defines with `protocol_version` the identity, Wi-Fi scan list, Wi-Fi config/result, Site binding, enrolment request/response messages and the sealed-downlink envelope with an empty `commands` field, and `buf lint` passes.
- Given `packages/openapi`, when inspected, then it defines `POST /device/heartbeat`, `POST /device/ingest` (placeholder), `GET /enrolment-key` and `POST /sites/{siteId}/devices`, and the existing .NET, TS and Kotlin contract tests pass.
- Given `packages/crypto-spec`, when `pnpm --filter @coldframe/crypto-spec run generate` runs, then labels, algorithms, nonce layout, setup-session derivation and heartbeat format constants are emitted for Rust, C# and Kotlin, and `run check` fails on stale output.
- Given `vectors.json`, when `cargo test --workspace`, `dotnet test` and `./gradlew check` run, then all three reproduce every vector (key derivation, HPKE sealing, AEAD with nonce layout, setup-session keys, heartbeat HMAC, RFC anchors) and the negative tests pass.
- Given the Device simulator, when a test uses it, then it generates an identity, HPKE-seals enrolment, signs heartbeats and seals/verifies frames and downlinks from the generated proto and crypto-spec types, and its outputs equal the shared vectors.
- Given a `.proto` field removal or an OpenAPI breaking change against the baseline, when the compat check runs, then it exits non-zero.

## Spec Change Log

## Review Triage Log

### 2026-09-28 — Review pass
- verdicts: 45 findings — high 0, medium 10, low 25, false 10, maybe-false 0
- findings:
  - `[low]` `[patch]` (blind) Wrong setup code has no defined way to reach the app — documented in setup.proto that the Device replies with a SetupError sealed under its own keys and the app's failed first open means wrong code.
  - `[low]` `[patch]` (blind) EnrolmentRequest fingerprint check implies authenticity it cannot give — reworded in setup.proto and OpenAPI as an integrity check; authenticity comes from the TLS fetch and the user-visible fingerprint.
  - `[low]` `[reject]` (blind) HPKE enrolment does not bind siteId/kind — anyone able to obtain the blob already has physical access and the PoP code and could enrol the Device anywhere; 409 covers double enrolment; the fix changes the vector contract in three languages for no demonstrated harm.
  - `[medium]` `[patch]` (blind) PoP code limits hand-typed in each language (spec's "no hand-typed sizes") — added setup.maxCodeLength to crypto-spec.json, regenerated, all three use the generated constant; offline-brute-force/entropy part deferred (R-06).
  - `[medium]` `[defer]` (blind) No rule for who owns/persists the downlink counter — deferred to Stories 3.5/4.5.
  - `[medium]` `[defer]` (blind) ReplayWindow cannot be persisted/restored — deferred to Story 3.5 Device grain.
  - `[low]` `[reject]` (blind) Key material not zeroized — defence in depth only (no memory-disclosure path shown); fix adds a dependency and Drop/IDisposable surface in two languages.
  - `[low]` `[patch]` (blind) Heartbeat canonical string under-specified (query string, method case, timestamp sign) — crypto-spec.json/OpenAPI now state uppercase METHOD and PATH without query; C# rejects negative timestamps.
  - `[false]` `[reject]` (blind) serverTime ISO vs server_time_ms — the project convention is ISO-8601 in JSON and Unix ms in Protobuf (epic context Conventions); this follows it.
  - `[low]` `[reject]` (blind) Enrolment failures have no dedicated problem type — the operation is planned for 3.3; failures map to the existing 400 validation problem and a slug is an additive x-extensible-enum change there.
  - `[medium]` `[patch]` (blind) CI contracts job compares a push only with HEAD^ — now uses github.event.before, falling back to HEAD^ when all-zero or unknown.
  - `[low]` `[reject]` (blind) constants.test.ts checks presence more than values — a wrong emitted value fails every language's vector suite, which uses the generated constants.
  - `[low]` `[reject]` (blind) Anchor test compares generator with itself; TS reference never opens HPKE — the TS reference only generates vectors and must reproduce the RFC anchors to run; HPKE Open is anchored in C# and Kotlin.
  - `[medium]` `[patch]` (blind) Kotlin lacks small-order and invalid-setup-code negative tests — added (grouped with the verification-gap setup-code finding); the test exposed doPhase outside the try in JdkCrypto.kt, fixed.
  - `[low]` `[reject]` (blind) Vectors lack replay/setup/heartbeat edge cases — each language covers the window edges in its own negative tests; the window is checked by one side per direction, so a divergence is not a wire incompatibility.
  - `[false]` `[reject]` (blind) Simulator cannot run the BLE setup session — the AC and AD-24 scope the simulator to identity, enrolment, heartbeat, frames and downlinks.
  - `[low]` `[patch]` (blind) DeviceId.Parse accepts uppercase — now lowercase-only, malformed input throws CryptoFailureException.
  - `[low]` `[patch]` (blind) C# SetupSession overflow throws OverflowException — counter checked before seal/open, fails with Replay.
  - `[low]` `[reject]` (blind) hub_pub vs device_pub naming — cosmetic; the order and bytes are identical and vector-checked.
  - `[low]` `[reject]` (blind) Rust hex::encode_array does not enforce M == 2N at compile time — internal helper, every call site is test-covered.
  - `[low]` `[reject]` (blind) HpkeSealed/SealedEnrolment record equality compares byte[] references — no caller compares them.
  - `[false]` `[reject]` (edge) C# heartbeat canonical ambiguous if method/path contain \n — HTTP methods and routed paths cannot carry a raw LF, and the device signer passes constants; no reachable collision.
  - `[false]` `[reject]` (edge) Rust heartbeat canonical ambiguous if method/path contain \n — same refutation as the C# row.
  - `[low]` `[reject]` (edge) OpenAPI timestamp pattern admits values above i64::MAX — the planned verifier (3.5) parses to long and rejects overflow as 401; grouped with the timestamp-sign patch, which is what mattered.
  - `[low]` `[patch]` (edge) C# accepts negative timestampMs — ThrowIfNegative added (grouped with the heartbeat row).
  - `[low]` `[reject]` (edge) C# replay window would alias if replayWindow were raised above 64 — needs a spec change nobody makes; the fix is a new guard.
  - `[low]` `[patch]` (edge) C# SetupSession Open at counter ulong.MaxValue — grouped with the overflow patch.
  - `[low]` `[patch]` (edge) DeviceId.Parse FormatException / uppercase — grouped with the DeviceId patch.
  - `[low]` `[reject]` (edge) HPKE accepts short ikmE — callers pass 32 random bytes; RFC 9180 sets no minimum length; adding a guard is new surface.
  - `[low]` `[reject]` (edge) Generator has no cross-field consistency asserts — an inconsistent spec fails the vector suites at once.
  - `[low]` `[reject]` (edge) check-compat explicit mode with one baseline skips the other — explicit mode is only used by the self-test, which always passes both.
  - `[medium]` `[patch]` (edge) CI push multi-commit base — grouped with the CI base patch.
  - `[low]` `[reject]` (edge) x-coldframe-planned false/null treated as planned — the extension's value is always a story id by contract; the story that serves it removes the key.
  - `[medium]` `[patch]` (edge) check-compat aborts when the base has no packages/proto — existence now checked with git cat-file; only true absence means "no baseline".
  - `[medium]` `[patch]` (verification-gap) --base mode never self-tested and a failed extraction passes silently — grouped with the row above; extraction failures now fail, and a --base HEAD self-test case was added.
  - `[medium]` `[patch]` (verification-gap) InvalidSetupCode untested in Rust, over-length untested in C# — added Rust an_invalid_setup_code_is_refused and the 65-character C# case.
  - `[medium]` `[defer]` (verification-gap) Simulator Device-ID/version checks unverified — deferred to Story 3.5 per the layer's disposition.
  - `[low]` `[patch]` (verification-gap) Rust SetupSession::open writes plaintext before failing at u64::MAX — grouped with the overflow patch; counter checked before decrypting.
  - `[low]` `[patch]` (verification-gap) DeviceId.Parse accepts uppercase — grouped with the DeviceId patch.
  - `[false]` `[reject]` (intent) Protobuf types generated only for C# — the ACs require generated crypto constants in three languages and proto definitions; no Rust or Kotlin code consumes the messages yet, so shapes cannot disagree; micropb/KMP generation lands with 3.4/3.6 as the proto README records.
  - `[false]` `[reject]` (intent) Kotlin crypto is test-only — no shipped Kotlin crypto exists to diverge; the AC asks for Kotlin vector tests in tests/kt, which run on the generated constants; production KMP crypto is Story 3.6.
  - `[false]` `[reject]` (intent) Agreement is transitive through the TS reference — every language must equal the same committed vectors, so identical outputs follow; the reference itself must reproduce the RFC anchors.
  - `[false]` `[reject]` (intent) Rust HPKE seal only — the Device only seals; opening is Server/app side and tested there.
  - `[false]` `[reject]` (intent) OpenAPI operations defined, not served — the AC says "defines"; serving is 3.3/3.5.
  - `[false]` `[reject]` (intent) Compat proof is a shell self-test with FILE rules — FILE is buf's strictest breaking category and the self-test runs in CI before the real check.

## Design Notes

Normative byte decisions (spine leaves them open; recorded in `crypto-spec.json`):
- `K_dev = HMAC-SHA256(key=root32, msg="coldframe/device/v1")`. Purpose key = `HKDF-SHA256(salt=∅, ikm=K_dev, info=label, L=32)` for `seal/v1` (uplink), `ack/v1` (downlink), `hub-auth/v1`. Device ID = `HKDF(K_dev, info="device-id/v1", L=8)`; text form 16 lowercase hex.
- Frame AEAD (ChaCha20-Poly1305): nonce(12) = `device_id[0..4] ‖ counter_u64_be`; AAD = `u8 protocol_major ‖ device_id(8) ‖ counter_u64_be` (17 B). Replay window 64 (AD-17).
- HPKE (RFC 9180 base, KEM 0x0020 / KDF 0x0001 / AEAD 0x0003): `info="coldframe/enrolment/v1"`, `aad=device_id`, `pt=K_dev`. Fingerprint = lowercase hex SHA-256 of the raw 32-byte public key.
- Setup session: `okm = HKDF-SHA256(salt=UTF-8(uppercase PoP code), ikm=X25519(shared), info="coldframe/setup/v1" ‖ app_pub ‖ hub_pub, L=64)`; bytes 0..32 app→hub key, 32..64 hub→app key; nonce = `0x00000000 ‖ counter_u64_be` per direction starting at 0; AAD = `u8 protocol_major`. First decrypt failure ⇒ wrong-code error.
- Heartbeat: canonical = `METHOD\nPATH\nhex(SHA-256(body))\nTIMESTAMP_MS\nNONCE_HEX`; signature = lowercase hex HMAC-SHA256(hub-auth key). Headers `X-Coldframe-Device`, `X-Coldframe-Timestamp` (Unix ms), `X-Coldframe-Nonce` (32 hex = 16 B), `X-Coldframe-Signature`; skew ±300000 ms.
- Kotlin: vector runner implements primitives on JDK providers in test scope; KMP production crypto (commonMain/iOS) is Story 3.6's choice — generated constants are commonMain so it reuses them.

## Verification

**Commands:**
- `pnpm --filter @coldframe/crypto-spec run check && pnpm test && pnpm lint && pnpm typecheck` -- expected: pass
- `cargo clippy --workspace --all-targets --locked -- -D warnings && cargo test --workspace --locked` -- expected: pass incl. `tests/rs/crypto`
- `dotnet build Coldframe.slnx -warnaserror && dotnet test --solution Coldframe.slnx` (or repo's documented form) -- expected: pass incl. crypto.tests
- `./gradlew check` -- expected: pass incl. Kotlin vectors
- `packages/proto/check-compat.sh` (with locally downloaded pinned buf/oasdiff) -- expected: pass on tree; self-test proves failure on breaking change

## Auto Run Result

Status: done

**Summary.** Story 3.1 defines the Device wire and crypto contracts once and proves Rust, C# and Kotlin agree on them. `packages/proto` holds the BLE setup messages and the sealed envelope/downlink (every top-level message carries `protocol_version`, empty `commands`); `packages/openapi` adds `POST /device/heartbeat`, `POST /device/ingest` (placeholder), `GET /enrolment-key` and `POST /sites/{siteId}/devices`, each marked `x-coldframe-planned` so the Server maps them in their own stories. `packages/crypto-spec/crypto-spec.json` is the normative byte contract; a TS generator emits constants for Rust, C# and Kotlin plus `vectors.json` (computed by a reference implementation that must first reproduce RFC 5869/7748/8439/9180 anchors). Crypto libraries: `packages/rs/crypto` (no_std), `packages/cs/crypto`, and a JDK-based Kotlin vector runner; C# Protobuf types in `packages/cs/protocol`; Device simulator in `tests/cs/device-simulator`. A `contracts` CI job runs `buf lint`, `buf breaking` and `oasdiff breaking` against the base via `packages/proto/check-compat.sh`.

**Files changed (main groups).**
- `packages/proto/**` — setup.proto, envelope.proto, buf.yaml, check-compat.sh (+ self-test), install-tools.sh (pinned, sha256-checked buf/oasdiff), README.
- `packages/openapi/coldframe.openapi.json`, README; `packages/ts/api-client/src/schema.ts` regenerated; ProblemDetails.type moved to `x-extensible-enum` (oasdiff treats enum additions as breaking); Kotlin `OpenApiContractTest` reads the new key.
- `packages/crypto-spec/**` — spec, vectors, generator/reference/render scripts; `tests/ts/crypto-spec/**` freshness, constants and vector tests.
- `packages/rs/crypto/**`, `tests/rs/crypto/**`; root `Cargo.toml`/`Cargo.lock`.
- `packages/cs/crypto/**`, `packages/cs/protocol/**`, `tests/cs/crypto.tests/**`, `tests/cs/device-simulator/**`; `Coldframe.slnx`, `Directory.Packages.props` (BouncyCastle 2.7.0, Google.Protobuf 3.36.2, Grpc.Tools 2.84.0), lock files (Server's transitive Google.Protobuf moves 3.26.1 → 3.36.2).
- `packages/kt/core/build.gradle.kts` + `generated/kotlin/**/CryptoSpec.kt`; `tests/kt/core/jvmTest/**/crypto/**`.
- `tests/cs/server.tests/Edge/EdgeEndpointDiscoveryTests.cs` — planned operations must not be mapped yet.
- `.github/workflows/ci.yml` (crypto-spec check step, `contracts` job), `.gitleaks.toml` (vectors.json allowlist), `docs/quickstart.md`, package READMEs, `pnpm-workspace.yaml`, `pnpm-lock.yaml`.

**Review findings.** 45 findings (high 0, medium 10, low 25, false 10). Patches applied: 9 entries (4 medium: generated PoP-code limit, CI push base, compat baseline extraction + `--base` self-test, missing negative tests incl. a Kotlin `doPhase` bug; 5 low: wrong-code signalling doc, fingerprint wording, heartbeat canonical/negative timestamp, setup counter overflow ordering, DeviceId.Parse strictness). Deferred: 4 (downlink counter ownership, ReplayWindow persistence, simulator ID/version checks, PoP entropy — see frontmatter). Rejected: every other finding, with its reason in the Review Triage Log.

**Follow-up review recommended: true** — 4 medium entries patched (0 high). Named unverified risk: the rewritten `check-compat.sh` baseline logic and the CI `github.event.before` base selection have only run locally (the `--base HEAD` self-test passes only once the proto files are committed) and never on GitHub Actions.

**Verification.** `pnpm --filter @coldframe/crypto-spec run check`, `pnpm -r typecheck`, `pnpm -r lint`, TS tests (crypto-spec 64, design-tokens 601, api-client 5, web 313; web e2e 35 passed before the review patches) — pass. `cargo fmt --check`, `cargo clippy --workspace --all-targets --locked -D warnings`, `cargo test --workspace --locked` (27) — pass. `dotnet restore --locked-mode`, `build -warnaserror`, `format --verify-no-changes`, `dotnet test` (345 incl. Aspire integration) — pass. `./gradlew check` — pass (Kotlin crypto vectors, anchors, negatives). `check-compat.sh --self-test` (pinned buf 1.73.0 / oasdiff 1.32.1) and `--base 88b95f1` — pass before patches; after commit the self-test (6 cases incl. `--base HEAD`) and `--base 88b95f1` / `--base d1343f1` (main, no `packages/`) — pass.

**Residual risks.** No `no_std` target build (only x86_64 installed); Kotlin iOS compile not run (generated file is plain constants); `contracts` job not yet run on GitHub; Rust (micropb) and Kotlin Protobuf generation and production KMP crypto are left to Stories 3.4/3.6; custom crypto composition still needs the external review R-06 calls for.

