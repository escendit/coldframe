---
title: 'Story 3.5: Hub joins Wi-Fi and heartbeats to the Server'
type: 'feature'
created: '2026-09-29'
baseline_revision: '1aca64fd64235a1cd5a14347ff6c3d2ec5174198'
status: 'awaiting-operator'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md'
  - '{project-root}/apps/cs/README.md'
  - '{project-root}/docs/spikes/hub-radio-coexistence.md'
warnings: ['oversized']
operator_actions:
  - "Flash a bench ESP32-S3 with the release build of apps/rs/hub (espflash erase-region 0xC000 0x2000 first on a board that ran Story 3.4, whose v1 provisioning record now reads as corrupt), then follow docs/bench/hub-uplink-checklist.md with coldframe-setup-client --server <your Server URL> against the real Server and confirm setup ends CONNECTED."
  - "During that run confirm on serial that a heartbeat goes out every 30–60 s, and on the Server that the Hub's device.seen events keep arriving (last-seen advances)."
  - "Power-cycle the access point and reboot the Hub, and confirm on serial that the Hub re-joins with backoff and resumes heartbeats both times."
  - "Run setup once with --server pointing at an unreachable host and once with --server https://expired.badssl.com, and confirm both answer NO_SERVER, the second one with a TLS verification failure logged."
  - "Record the minimum free heap logged after the setup Server check (pass at 32768 bytes or more) and fill in the Result table of docs/bench/hub-uplink-checklist.md, then commit it."
  - "After this change is on GitHub, confirm the new firmware job in .github/workflows/ci.yml passes (cmake, ninja, espup toolchain, release and dev-mode builds, check-image.sh)."
deferred:
  - summary: >-
      A Device grain replays its whole journal stream on activation, with no snapshot, and each Hub now adds a device.seen event every 30-60 s (about 700k a year).
    evidence: |-
      JournaledStreamGrain.ReadStateFromStorage reads Store.ReadStreamAsync(StreamId) in full; there is no snapshot anywhere in apps/cs/server/Journal. Story 3.5 journals every accepted heartbeat by design (spec Design Notes). A silo restart after months of heartbeats reactivates each Device by replaying hundreds of thousands of rows. Fix with journal snapshots or a retention/compaction rule for Device streams.
    location: >-
      apps/cs/server/Journal/JournaledStreamGrain.cs (ReadStateFromStorage); apps/cs/server/Devices/DeviceGrain.cs (Heartbeat)
    severity: medium
  - summary: >-
      A provisioned Hub has no way back into BLE setup, so a changed Wi-Fi password, a re-enrolled or removed Device, or a new Server host leaves it retrying forever until cf_setup is erased by hand.
    evidence: |-
      Since Story 3.4 the Hub advertises only while unprovisioned (apps/rs/hub/src/main.rs, Boot::advertises). Story 3.5's uplink retries joins and heartbeats forever with backoff. The only recovery is espflash erase-region 0xC000 0x2000. It needs a Device lifecycle decision: a reset button, a factory-reset gesture, or re-provisioning after N failures.
    location: >-
      apps/rs/hub/src/main.rs (provisioned path); packages/rs/setup/src/service.rs (Boot)
    severity: medium
---

<intent-contract>

## Intent

**Problem:**
- A provisioned Hub never joins Wi-Fi after setup, and it has no network stack: no DHCP, DNS, SNTP, TLS or HTTP.
- The Server does not serve `POST /device/heartbeat`. That operation is still `x-coldframe-planned: "3.5"`, and nothing records a Device's last-seen time.
- The Hub also has no way to learn the Server's address. The provisioning record holds only the SSID, the password and the Site.
- `setup.proto` already promises that `CONNECTED` means "joined and the Server answered", with `NO_SERVER` otherwise, but the Hub produces neither.

**Approach:**
- Add an optional `server_url` to `SiteBinding`, which the app sends and the Hub stores in its provisioning record.
- Put the uplink logic in a new host-tested crate, `packages/rs/uplink`: BSSID choice, reconnect backoff, heartbeat scheduling, signing, JSON and time handling. It runs over new `coldframe-hal` traits.
- Implement those traits in `apps/rs/hub` on embassy-net, SNTP, mbedtls-rs with `hook-wall-clock`, and reqwless.
- On the Server, verify heartbeats inside the Device grain and journal last-seen.
- Add golden JSON fixtures generated from OpenAPI, plus a firmware CI job with cmake and ninja.

## Boundaries & Constraints

**Always:**
- **Server address.**
  - `SiteBinding` gains `optional string server_url = 3` (additive, AD-10). The value is `https://` + a lowercase DNS host + an optional `:port` (1–65535), with no path, query, fragment, userinfo or IP literal, and at most 100 bytes.
  - A Hub `SiteBinding` must carry a valid `server_url`: absent or invalid is `MALFORMED_MESSAGE`. Node rules are unchanged.
  - `coldframe-uplink` exposes `ServerUrl::parse -> {host, port (default 443)}`.
- **Provisioning record v2.** CFWP version byte `0x02` adds `u8 len ‖ server_url` after `site_id`. Any other version, including 3.4's `0x01`, loads as `Corrupt`: the Hub logs `corrupt provisioning record`, treats itself as unprovisioned, and advertises again.
- **Setup server check.**
  - After a successful join in the setup session, the Hub brings up IP, bootstraps the clock and sends one signed heartbeat, all within `SERVER_CHECK_TIMEOUT_MS` = 40 000.
  - HTTP 200 with a valid `serverTime` → store the v2 record, adopt `serverTime`, answer `CONNECTED`.
  - Anything else → answer `NO_SERVER`, leave the Wi-Fi network, store nothing, and keep the session open for a retry.
  - The proto README documents that the app must post the enrolment to the Server before it sends `WifiConfig`.
- **Wi-Fi (H-1).**
  - Scan all channels and choose the strongest BSSID for the SSID with `coldframe_uplink::select_bssid`. The 3.4 setup session reuses this function instead of its private `pick_target` logic.
  - Link loss is detected before each heartbeat and after any network error, then the Hub re-joins with backoff: 1 s, doubling per failure, capped at 60 s, reset on a successful join. Every join failure kind retries.
- **Clock (AD-11).**
  - SNTP against `pool.ntp.org` runs once per boot, before the first TLS connection. It retries with the same backoff, and TLS is never attempted without a wall clock.
  - After that the Rtc follows each heartbeat's `serverTime`. The Hub never stamps data with it.
  - Heartbeat timestamps are strictly increasing: `max(rtc_unix_ms, last_sent + 1)`.
- **Heartbeat (AD-12, FR13).**
  - Every 30 000–60 000 ms (45 s ± up to 15 s of jitter from the TRNG), `POST /device/heartbeat` with the body `{"protocolVersion":1,"uptimeMs":<n>}`.
  - Headers per `packages/crypto-spec`, signed with the existing `coldframe_crypto::heartbeat::Request` using `keys.hub_auth_key` and a 16-byte TRNG nonce.
  - One TLS connection at a time, closed after each exchange (`Connection: close`).
  - A non-200 answer or a network error is logged by kind and the schedule continues.
- **TLS (H-2, AD-13).**
  - mbedtls-rs 0.3.0 with `hook-wall-clock`, so certificate validity dates are checked, and SNI set to the host.
  - Public roots only: ISRG Root X1/X2, SSL.com TLS ECC Root CA 2022 and DigiCert Global Root G2, from the spike's `certs/roots.pem`. They are inlined as a string-literal const, because `include_str!` is banned by `image_guard.rs`.
- **Server verification.**
  - The Edge endpoint reads the raw body, capped at 4 KiB.
  - A missing or malformed `X-Coldframe-*` header gets 401 `device-unauthorized`. Malformed means a pattern mismatch, or a timestamp that overflows `long`.
  - A body that is not JSON, or has a `protocolVersion` other than 1, gets 400 validation.
  - Otherwise the endpoint calls `IDeviceGrain.Heartbeat`. The grain verifies inside itself, because the plaintext `K_dev` never crosses a grain boundary. It unwraps `K_dev` with `DeviceKeyVault`, derives the hub-auth key, verifies with `Coldframe.Crypto.Heartbeat.Verify` and zeroes both keys.
  - The grain rejects with 401:
    - an unenrolled Device;
    - a bad signature;
    - skew beyond ±`HeartbeatMaxSkewMs`, measured against `Clock`;
    - a nonce already seen, from an in-memory cache pruned beyond the skew window;
    - a timestamp ≤ the persisted `LastHeartbeatTimestampMs`, which covers replay after the grain reactivates.
  - An accepted heartbeat raises `[EventType("device.seen")] DeviceSeen(SeenAt, DeviceTimestampMs, UptimeMs?)` and confirms it. The endpoint answers 200 with `{"serverTime":"yyyy-MM-ddTHH:mm:ss.fffZ"}`, taken from `TimeProvider`.
- The new Edge access rule is `Device`: the endpoint is anonymous to JWT, and its rule string matches `x-coldframe-minimum-role: "Device"`. Remove the operation's `x-coldframe-planned` mark.
- Server Device-path tests use `SimulatedDevice`, never hand-built headers.
- Firmware logic lives in `packages/rs/*` and names no esp type (AD-24). New crates are `no_std`, have no `alloc`, set `rust-version = "1.95"`, and forbid `unsafe_code`.
- Never log keys, the password, nonces, signatures or bodies.
- Pin exact versions (AD-15), taking them from the spike's `Cargo.lock` where it has them:
  - `embassy-net` 0.9.1 with `dhcpv4`, `medium-ethernet`, `tcp`, `udp` and `dns`;
  - `mbedtls-rs` 0.3.0 with `esp32s3`, `hook-wall-clock`, `embassy-time` and `use-gcc`;
  - `reqwless` 0.14.0 with `default-features = false`;
  - `serde` and `serde-json-core` 0.6.0, used for the hand-written Hub JSON structs.
- Raise the Hub heap to the spike's 144 KiB + 64 KiB. Log the minimum free heap after the setup server check and in the uptime line.

**Never:**
- No mTLS, and no private CA.
- No KMP or app code (3.6). No Devices projection, read model or UI (3.7). No Silent Alert (Epic 7). No `/device/ingest` behaviour.
- No change to existing proto field numbers, and no other proto fields.
- No keep-alive connection pool.
- No `env!`, `option_env!` or `include_*!` for configuration.
- Never commit real credentials or server hosts; use `example.org` in tests.
- Don't edit `_bmad-output/implementation-artifacts/sprint-status.yaml`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Setup happy | Enrolled Device, join OK, Server answers 200 | Record v2 stored, Rtc = `serverTime`, `CONNECTED` | none |
| Setup no server | Join OK, but no IP, DNS, SNTP or TLS, a non-200, or 40 s pass | `NO_SERVER`, Wi-Fi left, nothing stored, session open | retry allowed |
| Bad server_url | Hub `SiteBinding` without `server_url`, with `http://`, with a path, or with an IP | `SetupError MALFORMED_MESSAGE` | session continues |
| Provisioned boot | Valid v2 record | Join strongest BSSID → SNTP → heartbeat loop; no advertising | backoff on failures |
| Old record | CFWP v1 | Treated as corrupt → unprovisioned, advertises | logged |
| Link lost | `is_connected` false at a tick, or a network error with the link down | Re-join: delays 1, 2, 4 … 60 s; reset on success | loops forever |
| Heartbeat OK | Server 200 `{"serverTime":"…Z"}`, fraction optional, unknown fields ignored | Rtc set, next heartbeat in 30–60 s | none |
| Heartbeat bad reply | Non-200, bad JSON, missing `serverTime` | Logged, Rtc unchanged, schedule continues | none |
| Server valid | Simulator-signed heartbeat from an enrolled Hub | 200 + `serverTime`; journal gains `device.seen` | none |
| Server rejects | Bad HMAC, skew beyond ±300 000 ms, repeated nonce, timestamp ≤ last, unknown Device, missing or malformed header | 401 `urn:coldframe:problem:device-unauthorized`, nothing journaled | none |
| Server bad body | Authenticated headers, but the body is not JSON, has `protocolVersion` 0 or 2, or is over 4 KiB | 400 validation, nothing journaled | none |

</intent-contract>

## Code Map

- **Contracts.**
  - `packages/proto/coldframe/setup/v1/setup.proto:220-226` -- `SiteBinding`. Add `server_url`, and update the comments for `CONNECTED`/`NO_SERVER` ("reached and accepted the Hub").
  - `packages/proto/README.md:80-120` -- the Hub message order and the "Story 3.5 adds…" sentence to replace.
  - `packages/rs/protocol/build.rs:46-67` -- capacities. Add `SiteBinding.server_url` = 100.
- **OpenAPI.** `packages/openapi/coldframe.openapi.json`:
  - `:10-44` is heartbeat. Its `x-coldframe-planned` is at `:16`.
  - `:376-381` is `deviceHmac`.
  - `:408-429` are the header params.
  - `:536-549` are `HeartbeatRequest`/`HeartbeatResponse`. Add `examples` there to feed the fixtures.
  - `:652` is `DeviceUnauthorized`.
  - `README.md` explains how to drop a planned mark.
- **Crypto.**
  - `packages/crypto-spec/crypto-spec.json:83-99` and `vectors.json:211-250` -- heartbeat canonical string and vectors.
  - Generator precedent for the fixtures: `packages/crypto-spec/{package.json,scripts/generate.ts,scripts/lib/paths.ts}`.
  - `pnpm-workspace.yaml` lists packages explicitly; add `packages/openapi`.
  - `packages/rs/crypto/src/heartbeat.rs` -- `Request{method,path,body,timestamp_ms,nonce}`, `sign`, `signature_hex`.
  - `packages/rs/crypto/src/spec.rs:150-173` -- `HEARTBEAT_*` constants.
  - `packages/rs/crypto/src/keys.rs:68-84` -- `DeviceKeys.hub_auth_key`, `device_id.to_hex()`.
- **HAL.**
  - `packages/rs/hal/src/wifi.rs:134` -- the `Wifi` trait (scan/join). Extend it with `fn is_connected(&self) -> bool` and `async fn leave(&mut self)`.
  - `packages/rs/hal/src/rtc.rs:23` -- `Rtc` (uptime, unix, set).
  - `packages/rs/hal/src/mock.rs` -- `MockWifi` (`:792`, one outcome per SSID/password; extend it to a per-attempt queue and a scriptable link state) and `MockRtc` (`:490`).
- **Setup crate.**
  - `packages/rs/setup/src/session.rs:352-377` -- `pick_target`. Replace it with `select_bssid`. `:64` is `JoinTarget`.
  - `packages/rs/setup/src/store.rs`:
    - `:178-230` is `ProvisioningRecord::new`/accessors; add `server_url`.
    - `:269-316` are `ProvisioningState`, `load_provisioning` and `store_provisioning`, which erases the sector at `:316`.
  - `packages/rs/setup/src/service.rs:29,71,106` -- `Provisioned`, `boot` and `run_setup`. `run_setup` gains the net, rtc and timer parameters and runs the server check after the join.
  - `packages/rs/setup/src/app.rs` and `tests/rs/setup-client/src/main.rs` -- the client gains `--server <url>` in the `SiteBinding`.
- **Firmware.**
  - `apps/rs/hub/src/main.rs:94-212` -- boot. The heap is at `:98-99`, radio init at `:107-116`, setup at `:166-197`, and the provisioned path at `:198-202`, which currently does not join.
  - `apps/rs/hub/src/board/wifi.rs:55,73,103` -- the join-error map, scan and join.
  - `apps/rs/hub/Cargo.toml` -- pins; `unsafe_code = deny`.
- **Spike templates** in `apps/rs/spike-hub-radio/src/bin/hub.rs`:
  - CA bundle `:169-175`;
  - TLS setup `:303-324`, with `EspAccel` optional and `unsafe` allowed locally in `board` only;
  - wall-clock hook `:329-346`;
  - `embassy_net::new` `:387-395`;
  - `wifi_task` `:411-481`;
  - HTTPS worker `:880-1032`;
  - raw HTTP `exchange` `:1073-1213`;
  - `sntp` `:1220-1272`.
  - Build notes on cmake and ninja: spike `README.md:86-103`.
- **Guard.** `tests/rs/setup/tests/image_guard.rs:57-72` bans `include_*!` and non-`CARGO_PKG_*` `env!`. Add `packages/rs/uplink` to its scanned crates.
- **Server.**
  - `apps/cs/server/Edge/EdgeApi.cs:117-143` -- `MapEdgeApi`. The enrol handler at `:419-487` is the pattern; `ReadJsonAsync` is at `:579`.
  - `Edge/EdgeAccessRule.cs` -- add `Device`.
  - `Edge/EdgeAuthentication.cs:124-128` -- fallback policy (JWT).
  - `Edge/EdgeProblems.cs` -- add `DeviceUnauthorized`.
  - `Edge/EdgeValidation.cs:98` -- `NormalizeDeviceId`.
- **Device grain.**
  - `packages/cs/contracts/Devices/DeviceGrains.cs:8-19` -- `IDeviceGrain`; add `Heartbeat` and its DTOs.
  - `packages/cs/contracts/Devices/DeviceEvents.cs:27-34` -- `DeviceEnrolled`, the event pattern.
  - `apps/cs/server/Devices/{DeviceGrain.cs:18-54,DeviceState.cs,DeviceKeyVault.cs:85}` -- `Unwrap` (the caller zeroes the result).
  - `packages/cs/crypto/{Heartbeat.cs:17-51,DeviceKeys.cs:142}`.
- **C# tests.**
  - `tests/cs/device-simulator/SimulatedDevice.cs:104-110` -- `SignHeartbeat`. Add a helper that builds an `HttpRequestMessage`.
  - `tests/cs/server.tests/{Edge/EdgeEndpointDiscoveryTests.cs,Fixtures/journal.json:211,Edge/EdgeTestHost.cs}`.
  - `tests/cs/server.integration/{Edge/AuthorizationMatrixTests.cs:32-86,186,Edge/EdgeApiFixture.cs,Devices/EnrolmentTests.cs:58,321,Devices/Vectors.cs}`.
- **CI.** `.github/workflows/ci.yml`:
  - `rust` job `:156-188`, which builds no firmware and installs no cmake;
  - `typescript` job crypto-spec check `:278-281`;
  - path filters `:66-85`.
  - DW-43 in `deferred-work.md:328` is the missing firmware CI.
- **Docs.** `apps/rs/hub/README.md:144,163-176`, `docs/quickstart.md:6-22,248-261`, `docs/bench/hub-setup-checklist.md` (style template), `apps/cs/README.md:212-225`.

## Tasks & Acceptance

**Execution:**
- **Contracts:**
  - `setup.proto`, `packages/proto/README.md` and `packages/rs/protocol/build.rs` -- add `server_url` and its capacity, update the comments, and document the app's enrol-before-`WifiConfig` order.
  - Also regenerate any generated setup code (C#, via the build) and check that `check-compat.sh` passes.
- **HAL** (`packages/rs/hal/src/{net.rs,timer.rs,wifi.rs,lib.rs,mock.rs}`):
  - `Net`: `wait_ip(timeout_ms)`, `sntp(timeout_ms) -> unix ms`, and `post(&HttpRequest{host, port, path, headers: &[(&str,&str)], body}, resp_body: &mut [u8]) -> Result<HttpResponse{status, body_len}, NetError>`. `NetError` is `NoIp`, `Dns`, `Sntp`, `Tls`, `Http` or `Timeout`.
  - `Timer`: `sleep_ms`.
  - The `Wifi` additions from the Code Map.
  - `MockNet` (scripted results that record requests), `MockTimer` (records sleeps), and the `MockWifi` extensions.
- **`packages/rs/uplink/**`** (new `coldframe-uplink`, a workspace member):
  - `select_bssid` and `Backoff`;
  - `HeartbeatSchedule`;
  - `ServerUrl`;
  - an RFC 3339 `…Z` parser to unix ms;
  - `MonotonicStamp`;
  - JSON `HeartbeatRequest`/`HeartbeatResponse` on serde-json-core;
  - `sign_heartbeat` → the header values;
  - `check_server(...)`, the one-shot check used by setup;
  - `Uplink::step(...) -> next_delay_ms`, plus a `run` loop forever. Tests drive `step`.
- **`packages/rs/setup/src/{session,store,service,app}.rs`** -- the v2 record, the `server_url` rule, `select_bssid` reuse, the server check, and `NO_SERVER`.
- **`tests/rs/uplink/**`**:
  - BSSID choice (strongest, ties, absent SSID);
  - the backoff sequence and reset;
  - schedule bounds over many TRNG draws;
  - `ServerUrl` accept and reject tables;
  - RFC 3339 cases;
  - monotonic stamps;
  - signing that equals `vectors.json` `heartbeat[0]`;
  - `step` scenarios for the link-lost re-join and the heartbeat-OK and bad-reply rows;
  - golden fixtures: decode every response fixture, and encode the request fixture values so they equal the fixture JSON semantically, with keys ⊆ schema properties and required keys present.
- **`tests/rs/setup/**`** -- the setup happy, no-server and bad-`server_url` rows, the v1-record-as-corrupt row, and the v2 round trip. Update the existing session tests for `server_url`.
- **Golden fixtures:**
  - `packages/openapi/{package.json,scripts/generate-fixtures.ts,fixtures/hub/*.json}` plus `pnpm-workspace.yaml`. `@coldframe/openapi` gets `generate` and `check` scripts. They emit the request fixtures (minimal and full), the response fixtures (with a fraction, without one, with an extra field) and `schemas.json` (the two schemas' properties and required keys), all taken from the OpenAPI `examples` and schemas.
  - `.github/workflows/ci.yml` -- add the fixture check next to the crypto-spec check.
- **`.github/workflows/ci.yml` `firmware` job** (runs on rust path changes):
  - apt `cmake` and `ninja-build`;
  - `espup` pinned, installing the esp toolchain version that `apps/rs/hub/rust-toolchain.toml` resolves to locally;
  - `espflash` pinned;
  - `cargo build --release`, `cargo build --features dev-mode`, clippy `-D warnings`, fmt, `./check-image.sh`;
  - cached, with a timeout of 60 minutes.
- **Firmware** (`apps/rs/hub/{Cargo.toml,Cargo.lock,src/main.rs,src/board/{net,timer,rtc,roots,wifi,mod}.rs}`):
  - Board `Net` (embassy-net stack with DHCP/DNS, SNTP over UDP, mbedtls-rs session + reqwless request/response), `Timer` (embassy-time), `Rtc` (esp RTC, which also feeds the wall-clock hook) and the roots const.
  - The `Wifi` additions.
  - Boot: always create the stack and spawn `net_task`; the setup path passes net, rtc and timer to `run_setup`; both paths then run `Uplink::run`.
  - Fallback: if reqwless cannot sit over the mbedtls-rs session because the embedded-io versions clash, use the spike's raw HTTP/1.1 `exchange`, extended to return the body, and record it as a deviation.
- **Server:**
  - `apps/cs/server/Edge/{EdgeApi,EdgeAccessRule,EdgeProblems}.cs`, `apps/cs/server/Devices/{DeviceGrain,DeviceState}.cs`, and `packages/cs/contracts/Devices/{DeviceGrains,DeviceEvents}.cs` -- the endpoint, the `Device` rule, `DeviceUnauthorized`, `Heartbeat`, `DeviceSeen` and the state fields `LastSeenAt`/`LastHeartbeatTimestampMs`. Remove the planned mark in `coldframe.openapi.json`.
  - Regenerate `packages/ts/api-client`.
- **C# tests:**
  - `tests/cs/device-simulator/SimulatedDevice.cs` -- the helper.
  - `tests/cs/server.tests/**` -- the discovery rule, a `journal.json` `device.seen` row, and unit tests on `EdgeTestHost` for header and body parsing.
  - `tests/cs/server.integration/**` -- one test per Server row of the matrix, using the vector Hub. The authorization-matrix tests get a Device branch: without Device headers, even with a user token, the answer is 401 `device-unauthorized`.
- **Docs:**
  - `docs/bench/hub-uplink-checklist.md` (new): setup with `--server`, `CONNECTED`, heartbeats every 30–60 s on serial, last-seen advancing on the Server, rejoin after an AP power cycle, rejoin after a Hub reboot, `NO_SERVER` against an unreachable host, heap ≥ 32 768 B during the setup check, and a Result table.
  - Update `apps/rs/hub/README.md`, `packages/rs/README.md`, `docs/quickstart.md` (cmake and ninja prerequisites), `apps/cs/README.md` and `hub-setup-checklist.md` (`--server`, re-setup after v1).

**Acceptance Criteria:**
- Given the host workspace, when `cargo test --workspace --locked` runs, then BSSID selection, heartbeat scheduling, HMAC signing against vectors, reconnect backoff, the golden fixtures and every Rust matrix row pass.
- Given the Server, when the unit and integration suites run, then every Server matrix row passes, the discovery test maps `deviceHeartbeat` with rule `Device`, and journal replay covers `device.seen`.
- Given `apps/rs/hub` with the esp toolchain, cmake and ninja, when release and dev-mode builds, clippy, fmt and `check-image.sh` run, then all pass. `ci.yml` has a `firmware` job that does the same.
- Given `pnpm --filter @coldframe/openapi run check`, `pnpm --filter @coldframe/crypto-spec run check` and `packages/proto/check-compat.sh --self-test`, when they run, then they pass.
- Given a bench Hub and a reachable Server, when the operator follows `docs/bench/hub-uplink-checklist.md`, then setup ends `CONNECTED`, heartbeats keep last-seen current, and the Hub rejoins after an AP outage. This is a manual operator action.

## Spec Change Log

## Review Triage Log

### 2026-09-29 — Review pass
- verdicts: 26 findings — high 0, medium 4, low 14, false 8, maybe-false 0
- findings:
  - `[low]` `[reject]` (blind) A Hub refused with 401 for clock skew never re-runs SNTP, so it stays refused until a reboot. The frozen intent contract says SNTP runs once per boot (AD-11), so the fix edits the spec. It needs the Server clock more than 5 min off NTP, or a forged SNTP reply, which the SNTP patch below now blocks.
  - `[medium]` `[patch]` (blind) The SNTP client accepts any reply: the source is not checked, and LI=3 and stratum 0 are accepted. Patched: the reply must come from the queried server on port 123, and LI=3 and stratum 0 are rejected.
  - `[medium]` `[defer]` (blind) One `device.seen` per heartbeat, with no journal snapshot. Confirmed that `JournaledStreamGrain` replays the full stream on activation. The heartbeat volume is the spec's design choice, and the missing snapshot is pre-existing journal infrastructure, so it is deferred.
  - `[low]` `[reject]` (blind) Anonymous heartbeats with random Device IDs activate grains, and there is no rate limit. Each costs one empty-stream read. The Server sits behind LAN split DNS (AD-13), no endpoint has rate limiting, and a limiter adds new infrastructure.
  - `[low]` `[reject]` (blind) Refusal reasons are logged neither by the Server grain nor for the Hub's setup `CheckError`. The outcome itself is still reported (401 or `NO_SERVER`), and after setup the Hub's uplink loop logs every heartbeat kind. Neither crate nor grain has a logger, so adding one is new surface.
  - `[low]` `[patch]` (blind) A large non-200 body is reported as an HTTP failure. Grouped with the edge row below; same patch: a non-200 status returns `{status, body_len 0}` without reading the body. A header section over 1536 B still fails, which a normal ingress does not reach.
  - `[low]` `[reject]` (blind) A TCP connect failure is logged as a TLS failure. Logging only, and the fix adds a `NetError` variant to the hal's public surface.
  - `[low]` `[reject]` (blind) An encode failure is reported as `NoEntropy`. It is unreachable: the 64-byte buffer holds the longest body, and `REQUEST_MAX` is sized for it.
  - `[medium]` `[defer]` (blind) A provisioned Hub has no way back into setup. This has been pre-existing since 3.4 (it advertises only while unprovisioned) and needs a Device lifecycle decision, so it is deferred.
  - `[low]` `[reject]` (blind) No test covers replay after the grain reactivates. The persisted rule is `State.LastHeartbeatTimestampMs`, rebuilt by `Apply(DeviceSeen)` (`Math.Max`), and `FixtureJournalReplayTests` covers that replay. The reboot sub-case needs `serverTime` to run seconds ahead of NTP, which is not realistic.
  - `[false]` `[reject]` (blind) The nonce cache is redundant with the strict-increase check. The intent contract requires both, and the cache causes no named harm.
  - `[low]` `[reject]` (edge) A persist failure after a passed check leaves the Hub joined. It needs a flash write failure, and the Hub is not half-configured because nothing was stored. A retry re-joins, since the join disconnects first. The fix adds a branch.
  - `[low]` `[reject]` (edge) The app disconnects during the check and the check then passes. The Hub ends enrolled and provisioned, so it is fully configured, and the app sees it online on the Server. The fix adds link-state guards.
  - `[low]` `[reject]` (edge) Persistent 401 from clock skew. Duplicate of the blind row; same reason.
  - `[medium]` `[patch]` (edge) SNTP era rollover, LI=3 and a spoofed source. Grouped with the blind SNTP row. Patched: in addition to the source and LI/stratum checks, seconds below the Unix offset are read as NTP era 1.
  - `[low]` `[patch]` (edge) The Host header omits a non-default port. Patched: `host:port` when the port ≠ 443.
  - `[low]` `[patch]` (edge) A non-200 answer is reported as an HTTP failure. Same patch as the blind row.
  - `[low]` `[reject]` (edge) Encode failure mapped to `NoEntropy`. Duplicate of the blind row: unreachable.
  - `[low]` `[reject]` (edge) A second Server check in the same boot restarts `MonotonicStamp`. It needs an accepted check followed by a persist failure, then a retry within the same second-scale window. That is rare, and the fix threads state through the session.
  - `[false]` `[reject]` (edge) A `PathBase`, trailing slash or case change makes the signed path differ. `Request.Path` excludes `PathBase`, and the Hub always sends the exact path it signs, so a valid Hub heartbeat is never refused.
  - `[low]` `[patch]` (edge) The nonce is recorded before `ConfirmEvents`. Patched: it is recorded only after `ConfirmEvents` succeeds.
  - `[medium]` `[patch]` (verification-gap) The 401 for a key that does not unwrap had no test. Patched: `ADeviceWhoseKeyDoesNotUnwrapIsRefused` journals a key wrapped under another KEK and asserts 401 with nothing journaled.
  - `[false]` `[reject]` (intent) The procedural finish (awaiting-operator, `operator_actions`, commit) was absent from the diff. That happens at finalization, after review; it is not a defect in the diff.
  - `[false]` `[reject]` (intent) The literal `ScanMethod::AllChannels` does not appear. The board scan uses `ScanConfig::default()`, an active scan of every channel, and `select_bssid` picks the strongest BSSID. Together they give the behaviour the acceptance criterion names.
  - `[low]` `[patch]` (intent) Nothing guards certificate-date checking (H-2). Patched: `hub_tls_checks_certificate_dates` fails unless the mbedtls-rs dependency enables `hook-wall-clock`, and the bench checklist gains an expired-certificate step (`--server https://expired.badssl.com` → TLS failure + `NO_SERVER`).
  - `[false]` `[reject]` (intent) Scope goes beyond the epic's criteria (`server_url`, record v2, `NO_SERVER`). `setup.proto` (3.1/3.4) already defines `CONNECTED` as "the Server answered", and the Hub had no source for the Server address, so these are required, not extra.

## Design Notes

- **Why `server_url` travels in `SiteBinding`.** The app already knows its Server; the mobile build is configured with it and sign-in has no address field. The Server is also where the Site lives. A value compiled into the firmware would make the image specific to one deployment, and FR-1 plus the image guard already keep configuration out of the build. The field is optional, so Nodes simply omit it.
- **Replay without a shared cache.** Orleans guarantees one activation per Device, so an in-memory nonce set is authoritative while the grain is active. The persisted strictly-increasing timestamp closes the gap after reactivation. The Hub guarantees increasing stamps, so neither rule rejects a legitimate Hub.
- **Journal every heartbeat.** It costs about 2 000 small rows per Hub per day. In return, 3.7's projection and Epic 7's silence alerts read real events. Throttling can come later without a contract change.
- **CONNECTED requires 200.** A 401 during setup means the app has not enrolled the Hub. Storing credentials anyway would leave the Hub half-configured, so the result is `NO_SERVER` and the app retries after enrolling.

## Verification

**Commands:**
- `cargo fmt --all --check && cargo clippy --workspace --all-targets --locked -- -D warnings && cargo test --workspace --locked` -- expected: clean, all pass
- `cargo build -p coldframe-hal -p coldframe-crypto -p coldframe-protocol -p coldframe-setup -p coldframe-uplink --locked` -- expected: pass
- `cd apps/rs/hub && source ~/export-esp.sh && cargo build --release && cargo build --features dev-mode && cargo clippy --release -- -D warnings && cargo fmt --check && ./check-image.sh` (cmake and ninja on PATH, e.g. `uv tool install cmake ninja`) -- expected: all pass
- `dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test --project tests/cs/server.tests && dotnet test --project tests/cs/crypto.tests && dotnet test --project tests/cs/server.integration` -- expected: all pass
- `pnpm --filter @coldframe/openapi run check && pnpm --filter @coldframe/api-client test && packages/proto/check-compat.sh --self-test && packages/proto/check-compat.sh --base 1aca64fd64235a1cd5a14347ff6c3d2ec5174198` -- expected: pass

**Manual checks (if no CLI):**
- The operator runs `docs/bench/hub-uplink-checklist.md` on an ESP32-S3 against the real Server.

## Auto Run Result

Status: awaiting-operator

**Summary.** A provisioned Hub now joins Wi-Fi and heartbeats to the Server.
- **Contract.** `SiteBinding` gains an optional `server_url`. The Hub stores it in a v2 provisioning record; a 3.4 v1 record reads as corrupt, so that Hub asks for setup again.
- **Setup.** A setup join is followed by a Server check (DHCP, SNTP, one signed heartbeat, within 40 s). The session answers `CONNECTED` only on a 200, and otherwise leaves the network and answers `NO_SERVER`.
- **`coldframe-uplink`** (new, host-tested):
  - strongest-BSSID choice and 1–60 s reconnect backoff;
  - a 30–60 s heartbeat schedule;
  - `ServerUrl` parsing, RFC 3339 parsing and increasing timestamps;
  - serde-json-core bodies and hub-auth signing;
  - `check_server`, `Uplink::step` and `run`.
- **Firmware.** It implements the new `Net`/`Timer`/`Rtc` traits on:
  - embassy-net (DHCP, DNS);
  - SNTP;
  - mbedtls-rs with `hook-wall-clock` and public roots;
  - reqwless.
- **Server.** `POST /device/heartbeat` uses a `Device` access rule. The Device grain verifies inside itself: it unwraps `K_dev`, derives the hub-auth key and checks the HMAC, the ±5 min skew, the nonce and the persisted timestamp, and journals `device.seen`.
- **Also added:** golden Hub JSON fixtures generated from OpenAPI, a firmware CI job with cmake and ninja, and a bench checklist.

**Files changed.**
- `packages/proto/coldframe/setup/v1/setup.proto`, `packages/proto/README.md` and `packages/rs/protocol/build.rs`: `server_url`, the `CONNECTED`/`NO_SERVER` meaning, and enrol before `WifiConfig`.
- `packages/rs/hal/src/{net,timer,wifi,lib,mock}.rs`: the `Net` and `Timer` traits, `is_connected`/`leave`, `MockNet`, `MockTimer`, and the `MockWifi` join queue and link state.
- `packages/rs/uplink/**` (new): the uplink logic. `packages/rs/setup/src/{session,service,store,app}.rs`: the server check, record v2 and `select_bssid` reuse.
- `apps/rs/hub/src/{main.rs,board/{net,rtc,timer,roots,wifi,mod}.rs}`, `Cargo.toml` and `Cargo.lock`: the network stack, TLS, SNTP, the uplink loop and a 208 KiB heap.
- `apps/cs/server/Edge/{EdgeApi,EdgeAccessRule,EdgeProblems,EdgeValidation}.cs`, `apps/cs/server/Devices/{DeviceGrain,DeviceState}.cs` and `packages/cs/contracts/Devices/{DeviceGrains,DeviceEvents}.cs`: the heartbeat endpoint and the grain verification.
- `packages/openapi/{coldframe.openapi.json,package.json,scripts/generate-fixtures.ts,fixtures/hub/*}`, `pnpm-workspace.yaml` and `packages/ts/api-client/src/schema.ts`: the planned mark removed, examples, and the fixture generator.
- `tests/rs/{uplink/**,hal/tests/network.rs,setup/tests/*,protocol/tests/setup_v1.rs,setup-client}`, `tests/cs/{device-simulator,server.tests,server.integration}/**`: the tests, the simulator helper and the `--server` client flag.
- `.github/workflows/ci.yml`: the firmware job and the fixture check.
- `docs/bench/hub-uplink-checklist.md` (new), plus the READMEs, `docs/quickstart.md` and `hub-setup-checklist.md`.

**Deviations.**
- The api-client tests live in `@coldframe/api-client-tests`, not in `@coldframe/api-client`.
- The Server integration tests use the `heartbeat[1]` vector Hub, because `heartbeat[0]` shares the enrolment vector's root key with the enrolment tests.
- The bench client waits for Enter so the operator can post the enrolment before `WifiConfig`.

**Review findings.** 26 findings: medium 4, low 14, false 8.
- **Patched: 6 entries.** At entry verdict: 2 medium, 4 low.
  - medium: SNTP reply validation (source, LI, stratum, era).
  - medium: a test for a key that does not unwrap.
  - low: the Host port.
  - low: the non-200 status is kept when the body is large.
  - low: the nonce is recorded after `ConfirmEvents`.
  - low: the `hook-wall-clock` guard and the expired-certificate bench step.
- **Deferred: 2, both medium.**
  - The journal has no snapshot while heartbeats are journaled.
  - A provisioned Hub has no way back into setup.
- **Rejected: every other row, each with its reason in the Review Triage Log.**
  - No SNTP re-run on persistent 401: the fix edits the intent contract.
  - No rate limit, and no refusal-reason logging.
  - The TCP/TLS error kind, and encode failure mapped to `NoEntropy` (×2, unreachable).
  - Replay after reactivation, and the second check in one boot.
  - Persist failure leaving the Hub joined, and an app disconnect during the check.
  - False: the nonce cache is redundant, a `PathBase` changes the signed path, the procedural finish is missing from the diff, `ScanMethod::AllChannels` is missing, and scope beyond the epic.

**Follow-up review recommended: true.** Two medium entries were patched. The named unverified risk is the new SNTP reply validation and the non-200 response path in `apps/rs/hub/src/board/net.rs`. They compile and pass clippy, but no host test reaches `board/*`, and they have never run against a real network.

**Verification** (after the patches):
- `cargo fmt --all --check` and `cargo clippy --workspace --all-targets --locked -- -D warnings` are clean. `cargo test --workspace --locked`: 160 passed, 0 failed. The no_std build of hal, crypto, protocol, setup and uplink passes.
- `apps/rs/hub` with the esp toolchain, cmake and ninja: release and dev-mode builds, clippy `-D warnings` for both, fmt and `./check-image.sh` all pass (no canary).
- `dotnet build -warnaserror` and `dotnet format --verify-no-changes` are clean. server.tests 282/282, crypto.tests 35/35, and server.integration 153/153 on the Aspire AppHost.
- The `@coldframe/openapi` and `@coldframe/crypto-spec` checks pass, the api-client tests pass, `check-compat.sh --self-test` passes, and `--base 1aca64f…` finds no breaking Protobuf or OpenAPI change.
- Matrix audit: every I/O-matrix row has a named test that ran and passed (`tests/rs/uplink/tests/steps.rs`, `tests/rs/setup/tests/{session,boot}.rs`, `tests/cs/server.integration/Devices/HeartbeatTests.cs`, `tests/cs/server.tests/Edge/DeviceHeartbeatRequestTests.cs`).

**Residual risks.**
- **Nothing has run on hardware:** the radio re-join, SNTP, TLS with date checks, reqwless over mbedtls-rs, the heap during BLE + TLS, and the 40 s setup budget. The operator bench run covers these.
- The new `firmware` CI job has never run on GitHub. espup downloads or cache paths may fail on the first run.
- There is no SNTP re-run when the Server refuses the clock. A Server clock more than 5 min off NTP locks Hubs out until they reboot.
- The journal grows by about 2 000 `device.seen` rows per Hub per day, with no snapshot (deferred).
- The Hub trusts four public roots. A Server certificate from another CA would fail TLS.

