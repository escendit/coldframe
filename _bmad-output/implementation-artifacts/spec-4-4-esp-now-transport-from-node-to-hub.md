---
title: 'Story 4.4: ESP-NOW transport from Node to Hub'
type: 'feature'
created: '2026-10-06'
baseline_revision: '30ffdf4ba73d1142fa52f71857869c9c38fd22c4'
status: 'awaiting-operator'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
  - '{project-root}/_bmad-output/specs/spec-coldframe/device-hardware.md'
  - '{project-root}/docs/spikes/hub-radio-coexistence.md'
warnings: ['oversized']
operator_actions:
  - "Flash the release Node image from apps/rs/node and the release Hub image from apps/rs/hub (erase 0x14000-0x1FFFF on a Node flashed before this story, as Part A of docs/bench/node-transport-checklist.md says), then run Parts A to F and I of docs/bench/node-transport-checklist.md on the bench: first contact, in-window and late acknowledgements, an hour with the Hub powered off and the backlog returning to 1, a router channel change, power cuts with no repeated counter, clock sync, and Hub heap and stack headroom during ingest."
  - "Run Part G of docs/bench/node-transport-checklist.md in the garden: place the Node at the farthest Lot, confirm it reports reliably there (NFR-11), and record the wake duration with the radio on."
  - "Run Part H of docs/bench/node-transport-checklist.md and Part F of docs/bench/node-setup-checklist.md, then copy the results into the Result tables and into the pending rows of _bmad-output/specs/spec-coldframe/device-hardware.md."
  - "Decide whether the Hub keeping up to 8 sealed downlinks per Node (instead of the single latest one AD-9 names) is acceptable, or whether a kept-alive ingest connection or cumulative Server acknowledgements should replace it; see the Spec Change Log of this spec."
deferred:
  - summary: >-
      A Node that hears a probe reply on a channel next to the Hub's may store the wrong channel and keep choosing it at every re-scan.
    evidence: |-
      find_hub stores the channel it probed on, not the Hub's own channel, and scans ascending. Whether an ESP32-S3 answers a probe heard one channel off is not known; a bench run at close range with a sniffer, or carrying the Hub's channel in ProbeReply, would settle it.
    location: >-
      packages/rs/transport/src/transport.rs (find_hub)
    severity: medium (unverified)
  - summary: >-
      Hub main-task stack headroom fell to about 34 KiB with the relay state and ingest buffers and is not measured on hardware.
    evidence: |-
      The ingest request and response buffers and the batch copy live in the main task; only Part I of the transport checklist watches for a reset during ingest. A stack high-water log on a real board would settle it.
    location: >-
      apps/rs/hub/src/main.rs, packages/rs/uplink/src/uplink.rs (ingest)
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** A Node measures every 15 minutes and then drops its Readings: there is no Reading buffer, no sealed Node frame, no ESP-NOW link, no Hub relay to `POST /device/ingest`, and the Node never learns the time. Nothing reaches the Server that Story 4.5 built.

**Approach:** Give the Node a flash-backed report buffer, a block-reserved frame counter and a transport step that seals each buffered wake report as a `NodeFrame` and sends it over ESP-NOW, deleting a report only on an authentic sealed downlink. Give the Hub an opaque relay that forwards sealed envelopes to `/device/ingest` and returns sealed downlinks, with a volatile per-Node slot for late acknowledgements. Define the Node ⇄ Hub radio messages in `packages/proto`.

## Boundaries & Constraints

**Always:**
- Frame: `NodeFrame` (`protocol_version`, `spec_hash`, `boot_id`, `uptime_ms`, measured time, `report_seq`, Readings, battery, charging) in a `SealedEnvelope`, sealed with ChaCha20-Poly1305 under `seal/v1` through `coldframe_crypto::frame`. One wake report per frame. `report_seq` comes from the `reading_seq` counter.
- Frame counter: a `ReservedCounter` on its own flash partition. The ceiling is raised and read back before anything is sealed. Every transmission, including a resend, is sealed fresh under a new counter. The buffer stores report payload, never sealed bytes.
- Delivery (N-1): only a downlink that opens under `ack/v1`, carries this Node's Device ID and protocol version 1 deletes Readings, and only those in its `acked_readings` ranges. Radio send status is never read as delivery.
- Buffer: survives deep sleep and power loss, holds at least 96 wake reports (24 h), plain NOR writes only. When full, the oldest report is dropped, counted and logged.
- Ack window (N-2): 300 ms after the last send of a wake. A wake that receives no *fresh* downlink is a miss. A downlink is fresh when its `acked_counter` lies in the burst sent this wake or in the previous persisted burst.
- Channel following: after 3 consecutive misses the Node probes channels 1 to 13 (120 ms each) and uses the first channel where a Hub answers. With no known channel it scans at once. After a failed full scan the next one is no sooner than 4 wakes later. Only a fresh downlink resets the miss count.
- Late ack: the Hub keeps the latest downlink per Node (keyed by ESP-NOW source MAC) in RAM only and offers it when that Node next probes.
- Hub relay: forwards each received envelope unread as standard padded base64 in `{"frames":[…]}`, HMAC-signed for `/device/ingest`, and maps `results[i].downlink` to the sender of `frames[i]`. It stores no Readings, parses no envelope, builds no acknowledgement and never POSTs the same bytes twice. A failed POST drops the batch; the Node resends.
- Clock (AD-11): the Node's wall clock changes only from a fresh downlink. The first sync of a boot sets it. Later corrections go forward in full and backward by at most 1000 ms per downlink. Without a sync for the current boot ID, reports are `Unsynced {boot_id, uptime_ms}`.
- `spec_hash` is SHA-256 of the Node's serialized `SpecificationSet` (four Specifications, soil moisture `calibration: true` with default Thresholds). The set is attached to the next frame once after a downlink sets `specifications_unknown`.
- Logic lives in host-tested `no_std` crates behind HAL traits; `apps/rs/*/src/board` only adapts esp types. No new `unsafe`.

**Never:**
- No Wi-Fi station on the Node. No Readings, keys or sealed payloads in logs.
- No Server, OpenAPI or `envelope.proto` schema change. No Hub keep-alive connection pool (see Design Notes).
- No transmission on a wake that entered setup mode; the report is buffered.
- No change to `sprint-status.yaml`. No fix for DW-64 or DW-66.
- Never edit a crypto-spec vector to make a test pass.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Vector frame | `vectors.json` "node frame with four readings" `message` | Rust `NodeFrame` encoding equals the vector plaintext; sealed bytes equal its ciphertext | No error expected |
| Vector downlink | "downlink acknowledging the readings of the node frame" | Decodes to `acked_counter` 42, `server_time_ms` 1790000000321, range 100..104 | No error expected |
| In-window ack | One buffered report, Hub answers within 300 ms | Report deleted, miss count 0, clock set from `server_time_ms` | No error expected |
| No ack | Hub answers the probe, no downlink in 300 ms | Report kept, miss count +1, nothing deleted | Radio "sent OK" ignored |
| Resend | Second wake after a miss | Same report content, strictly higher counter, new `boot_id`/`uptime_ms` values of the resend | No error expected |
| Late ack | Hub slot holds the previous wake's downlink | Probe reply says pending; Node deletes the acked report before sending; that report is not resent | No error expected |
| Forged downlink | Wrong key, wrong Device ID, or version ≠ 1 | Nothing deleted, clock untouched, miss count not reset | Dropped silently |
| Replayed old downlink | Authentic downlink whose `acked_counter` is older than the previous burst | Acked Readings deleted (no-op if gone); clock untouched; miss count not reset | No error expected |
| Counter across reboot | Reset between two wakes | Counters of the second wake are all above every counter of the first | Counter fault: nothing is sealed, report stays buffered |
| Buffer overflow | 97th report with 96 unacked | Oldest report dropped, drop count 1, newest 96 kept in order | Logged |
| Channel re-scan | 3 consecutive misses, Hub now on another channel | Node scans, stores the new channel, sends there | Scan finds nothing: report kept, next scan ≥ 4 wakes later |
| Backlog | 20 buffered reports | At most 8 frames sent this wake, oldest first | No error expected |
| Clock back-step | Synced clock 5 s ahead of a fresh downlink | Clock moves back exactly 1000 ms | No error expected |
| Unsynced report | No sync for this boot ID | Frame carries `Unsynced {boot_id, uptime_ms}` of the measurement | No error expected |
| Specification request | Downlink with `specifications_unknown` | Next frame carries the set once; the frame after does not; envelope + kind byte ≤ 250 bytes | No error expected |
| Hub relay | Two Nodes send one envelope each; Server answers `stored` + downlink, `rejected_auth` | One POST with both frames in arrival order, byte-identical after base64 decode; one Downlink sent to the first Node's MAC; nothing to the second | No error expected |
| Hub POST fails | Transport error, 401 or 503 | Batch dropped, no Downlink sent, slot unchanged, heartbeats continue | Logged event |
| Hub queue full | More uplinks than the queue holds before a POST | Newest dropped, earlier ones relayed | Logged event |

</intent-contract>

## Code Map

- `packages/proto/coldframe/device/v1/envelope.proto` -- `SealedEnvelope`, `NodeFrame`, `Reading`, `Downlink`, `SpecificationSet`; read-only contract from Story 4.5. `:139-141` states the resend rule.
- `packages/proto/README.md` -- says "The ESP-NOW messages arrive in Epic 4 (Story 4.4)"; the radio message table goes here.
- `packages/rs/protocol/build.rs`, `src/lib.rs` -- micropb generation; compiles only `setup.proto` today. Pattern for capacities and for `tests/rs/protocol/tests/setup_v1.rs`.
- `packages/rs/crypto/src/frame.rs` -- `nonce`, `aad`, `seal`, `open`, `open_checked`, `ReplayWindow`; `keys.rs` `DeviceKeys {seal_key, ack_key, hub_auth_key, device_id}`; `heartbeat.rs` `Request::write_canonical`.
- `packages/crypto-spec/vectors.json` -- `frames` cases with `message` breakdowns; `tests/rs/crypto/tests/vectors.rs:47` is the existing AEAD check.
- `packages/rs/sensing/src/wake.rs` -- `run_wake` (`:302`), `WakeOutcome`, `WakeReport {measured_at, readings, battery, charging}`, `Reading {slot, seq, value}`, `MeasuredAt`; reserves only `values.len()` seqs (`:369`).
- `packages/rs/sensing/src/counter.rs` -- `ReservedCounter::{new, current, reserve}`, `SEQ_MAGIC`, `BOOT_MAGIC`; reuse for the frame counter with a new magic.
- `packages/rs/hal/src/{radio,rtc,flash,net,mock}.rs` -- `Radio` is only a power switch; no ESP-NOW trait or mock exists. `Rtc::set_unix_time_millis`, `MockRtc`, `MockFlash`, `MockNet`.
- `packages/rs/uplink/src/{uplink,sign,json,time}.rs` -- `Uplink::step`/`run` (`uplink.rs:172`, `:254`), `sign_heartbeat` hard-codes method and path (`sign.rs:71`), `RESPONSE_MAX = 512`, `MonotonicStamp`.
- `apps/rs/hub/src/main.rs:313-317`, `board/{radio,wifi,net}.rs` -- `uplink.run` owns `wifi` and `net`; `BoardRadio::into_controller()` moves the controller into `BoardWifi`; `net.rs:48` `RESPONSE_BUFFER = 1_536`, tx 2048, `Connection: close`. `Cargo.toml` lacks the esp-radio `esp-now` feature.
- `apps/rs/node/src/main.rs:425-458` -- `run_wake`, `log_outcome`, `sleep`; the transport goes between `:455` and `:458`. `board/rtc.rs` returns `None`/`Err` "Not before Story 4.4". `board/coex.rs` shows the esp-radio ESP-NOW calls. `partitions.csv` leaves `0x14000-0x1FFFF` free. `Cargo.toml` comment says no ESP-NOW in release until this story.
- `apps/rs/spike-hub-radio/src/{lib.rs,bin/node.rs,bin/hub.rs}` -- throwaway probe/reply/data/ack precedent (`PROBE_WAIT_MS = 120`, `RESCAN_AFTER = 3`, peers with `channel: None`).
- `packages/openapi/fixtures/hub/ingest-*.json`, `packages/openapi/coldframe.openapi.json` (`deviceIngest`) -- 32 frames, 1024 base64 chars per frame, 16384-byte body; results by index, `downlink` omitted unless `stored`/`duplicate`.
- `tests/cs/device-simulator/SimulatedDevice.cs` -- behavioural reference: `SpecHash` (`:113`), `DefaultSpecifications`, `ReportSeq` (`:297`), `SpecificationsRequested` (`:500`).
- `tests/rs/{uplink,sensing,protocol}/tests` -- host test layout, `common/mod.rs` rigs. `tests/rs/setup/tests/image_guard.rs` forbids env reads in image sources.
- `.github/workflows/ci.yml:176-191` -- host job; the `cargo build -p …` no-mock list must name any new shared crate. `apps/rs/{hub,node}/Cargo.lock` are separate and `--locked`.
- `docs/bench/node-setup-checklist.md` Part F, `docs/bench/hub-uplink-checklist.md` -- checklist conventions; Part F uses the `dev-mode` coexistence probe.

## Tasks & Acceptance

**Execution:**
- `packages/rs/protocol/build.rs`, `src/lib.rs` -- generate `coldframe.device.v1` types with bounded capacities; add `radio` module with the ESP-NOW message kinds (Design Notes) and their encode/decode -- Rust has no codec for the contract.
- `packages/proto/README.md` -- document the Node ⇄ Hub radio messages, addressing and channel rule -- AD-9 puts them in this contract.
- `tests/rs/protocol/tests/` -- vector tests for `NodeFrame`, `Downlink`, `SealedEnvelope` and radio messages -- matrix rows 1, 2.
- `packages/rs/hal/src/` (+ `mock.rs`) -- add a datagram radio trait (send to MAC or broadcast, receive with timeout, set channel) and a scriptable mock -- hardware stays behind traits.
- `packages/rs/transport/` (new crate `coldframe-transport`, workspace member) -- report buffer, frame counter use, frame building, `spec_hash` and Specification set, wake transport step (probe, late ack, burst, window, miss count, scan), link state, clock rule -- Node logic.
- `tests/rs/transport/` (new) -- one test per Node matrix row, plus power-loss cases for the buffer -- AC coverage.
- `packages/rs/sensing/src/wake.rs` -- issue `report_seq` for every wake report, including one with no Readings -- `NodeFrame.report_seq`.
- `packages/rs/uplink/src/` -- path-parameterised request signing (heartbeat wrapper kept), ingest JSON encode/decode, relay state (uplink queue, per-MAC downlink slots), ingest step in `Uplink`, new events -- Hub logic.
- `tests/rs/uplink/tests/` -- relay tests for the Hub matrix rows; ingest fixtures in `fixtures.rs` -- AC coverage.
- `apps/rs/hub/` -- esp-radio `esp-now` feature, `board/espnow.rs`, radio task that answers probes while a POST is in flight, wiring in `main.rs`, buffers in `board/net.rs` sized for the batch cap, `Cargo.lock` -- Hub firmware.
- `apps/rs/node/` -- partitions for counter, buffer and link state in `partitions.csv`, `board/flash.rs` labels, `board/espnow.rs`, working `BoardRtc` wall clock, transport call in `main.rs`, `Cargo.toml` comment, `Cargo.lock` -- Node firmware.
- `.github/workflows/ci.yml` -- add `-p coldframe-transport` to the no-mock build -- proves `no_std`.
- `docs/bench/node-transport-checklist.md` (new), `docs/bench/node-setup-checklist.md`, `apps/rs/{node,hub}/README.md`, `packages/openapi/README.md` -- bench and garden checklist; keep Part F and the READMEs true -- manual AC.

**Acceptance Criteria:**
- Given the host workspace, when `cargo test --workspace --locked` runs, then every matrix row has a passing test and no existing test changed its expectation.
- Given a release Node image, when a timer or report-now wake ends, then it has probed, sent and listened with ESP-NOW only, the radio is off before deep sleep, and the wake stays inside the 30 s watchdog in the worst case (full scan plus 8 frames).
- Given a Hub in the middle of an ingest POST, when a Node probes, then the Hub's probe reply is sent without waiting for the POST.
- Given a Hub that is not linked or has no clock, when uplinks arrive, then they are dropped and no POST is attempted.
- Given a power cut at any write during buffering, acking or counter reservation, when the Node boots, then no acked report reappears as unacked with a different `report_seq`, no unacked report is lost except by the overflow rule, and no counter repeats.
- Given a Node and a Hub on the bench and in the garden, when the operator follows `docs/bench/node-transport-checklist.md`, then the Node reports from the farthest Lot (NFR-11), buffered Readings arrive after the Hub was off for an hour, and a router channel change is followed.

## Spec Change Log

### 2026-10-06 — Review pass 1 (no loopback; recorded deviation)

- Trigger: the review showed that one kept downlink per Node cannot drain a backlog. The Server acknowledges per frame and a fresh TLS session outlasts the 300 ms window, so each wake deleted one report and added one.
- Change: the Hub keeps up to 8 downlinks per Node MAC (pool of 24, RAM only), offers them once at that Node's next probe and drops them. `ProbeReply.pending` is a count 0 to 8. The Design Notes radio table's "1 byte `pending` (0/1)" now means that count.
- Deviation: the intent contract's "Late ack" sentence says "the latest downlink per Node", after AD-9. The contract text is left as written; the code departs from it in this one point. This was handled as a patch, not as an intent-gap halt, because the fix is additive and bounded and does not rule out a kept-alive connection or cumulative Server acknowledgements later. It needs the owner's confirmation (see `operator_actions`).
- Known-bad state avoided: a backlog that never shrinks after a Hub outage, with new Readings reaching the Server hours late.
- KEEP: one wake report per frame, fresh counter per send, deletion only by authentic `acked_readings`, freshness bound to the burst's counter range, probe-first wake sequence, flash buffer with power-cut tests.

## Review Triage Log

### 2026-10-06 — Review pass
- verdicts: 41 findings — high 2, medium 9, low 22, false 5, maybe-false 3
- findings:
  - `[high]` `[patch]` Blind: a backlog never clears on the late-ack path; checklist Parts C and G disagree — confirmed in `Relay::keep` and `Uplink::ingest`; patched: up to 8 kept downlinks per Node, offered once; docs aligned. Larger than a normal patch and it departs from one contract sentence; see Spec Change Log.
  - `[high]` `[patch]` Blind: downlinks with `+` are dropped because the Server writes `\u002B` — confirmed: `EdgeAuthentication.cs` sets no relaxed encoder and `json.rs` borrowed the raw text; patched: `json::unescape` before base64, test added.
  - `[low]` `[reject]` Blind: a Node can be pinned to a Hub that never delivers — real only with a second Hub or a forger in range; choosing between Hubs is not in the story and needs stored Hub identity and new state.
  - `[medium]` `[patch]` Blind: the Hub answers probes when it cannot relay — confirmed; patched: no reply unless linked with a clock, tests on both sides.
  - `[low]` `[reject]` Blind: no fairness or rate limit on the uplink queue — Nodes wake 15 minutes apart and a burst takes milliseconds; a collision costs one resend. A per-MAC cap adds state for an unlikely case.
  - `[medium]` `[patch]` Blind: a kept downlink is offered forever — confirmed; patched with the first row (offered once, then dropped).
  - `[false]` `[reject]` Blind: `specifications_requested` cleared at seal, not at ack — the Server sets `specifications_unknown` again on the next downlink while the hash is unknown, so a lost frame heals itself.
  - `[low]` `[reject]` Blind: dropped downlinks are invisible and an encode failure is logged as `BadReply` — log detail only; counters add surface.
  - `[medium]` `[patch]` Blind: one wake can step the clock back several seconds — confirmed in `sync_clock`; patched: one correction per wake, test added. The forward bias by relay latency (under 1 s) is accepted.
  - `[low]` `[patch]` Blind: the Node worst-case comment ignores the send timeout — patched: comment now gives about 2.1 s normal, 6.2 s bound.
  - `[low]` `[patch]` Blind: "a heartbeat is never late because of the relay" is untrue — comment corrected.
  - `[false]` `[reject]` Blind: spec status and empty logs — the review ran before finalization; its fix is a spec edit.
  - `[medium]` `[patch]` Blind: no host test joins the Node transport to the real `Relay` — added `tests/rs/transport/tests/relay_link.rs`.
  - `[maybe-false]` `[defer]` Blind: Hub stack headroom left to the bench — a stack high-water log on hardware would settle it; deferred, medium (unverified).
  - `[low]` `[reject]` Blind: `reclaim` does not retry, `spec_hash` falls back silently, a `cf_seq` open failure skips the transport, a failed `issue_report_seq` discards Readings — all need a flash fault; each fix adds branches.
  - `[high]` `[patch]` Edge: several frames of one MAC keep one downlink — same defect as the first row; same patch.
  - `[high]` `[patch]` Edge: `\u002B` in the downlink — same defect as the second row; same patch.
  - `[false]` `[reject]` Edge: `server_time_ms` of 0 sets the clock to 1970 — only an authentic downlink is applied and the Server always sets the time (`DeviceGrain.cs:369-376`).
  - `[low]` `[patch]` Edge: Hub send timeout longer than the probe wait — patched: 60 ms.
  - `[low]` `[reject]` Edge: a `set_channel` failure is reported as no Hub — needs a radio fault; adds a fault field.
  - `[maybe-false]` `[defer]` Edge: a reply heard on an adjacent channel stores the wrong channel — radio behaviour unknown; deferred, medium (unverified).
  - `[low]` `[reject]` Edge: `reclaim` failure with free slots loses the new report — needs an erase fault; adds a branch.
  - `[low]` `[reject]` Edge: append gives up after one failed write — needs a write fault.
  - `[low]` `[reject]` Edge: two copies with different live bits bring acked Readings back — they are resent and answered `duplicate`; no data harm.
  - `[low]` `[reject]` Edge: `deleted` double-counts across two copies — a log figure after a power cut.
  - `[low]` `[reject]` Edge: an empty buffer with a Hub present counts as a miss — only when the report could not be stored.
  - `[low]` `[patch]` Edge: an ingest started just before the deadline delays the heartbeat — bounded by one request; the comment was corrected, no timeout added.
  - `[low]` `[reject]` Edge: encode failure labelled `BadReply` — same as the Blind row on labels.
  - `[false]` `[reject]` Edge: wall clock set below uptime — the time comes from an authentic `server_time_ms`, always far above uptime.
  - `[false]` `[reject]` Edge: link generation reaching `u32::MAX` — four thousand million writes at about 100 a day.
  - `[medium]` `[patch]` Gap: backlog tested only against a scripted Hub — added the `Relay` cross test and a two-frames-one-MAC relay test.
  - `[medium]` `[patch]` Gap: `Report::from_wake` never executed by a test — test added.
  - `[medium]` `[patch]` Gap: `Uplink::run`'s relay loop untested — extracted `relay_until`, test added.
  - `[medium]` `[patch]` Gap: wait windows tested with a frozen clock — added `tests/rs/transport/tests/windows.rs`.
  - `[medium]` `[patch]` Gap: `spec_hash()` asserted against itself — independent SHA-256, units and ranges asserted against the simulator.
  - `[high]` `[patch]` Gap (other): a backlog from an outage does not drain — same defect as the first row.
  - `[low]` `[patch]` Gap (other): stale `wake done … sleep_ms=` line — now logged after the transport.
  - `[low]` `[reject]` Intent: tests exercise host mocks, not radios, flash and garden — inherent; the hardware run is owed under `operator_actions`.
  - `[low]` `[reject]` Intent: the clock moves forward in full, where the epic says "slews gradually" — the 1 s backward bound holds; a forward step cannot reorder Readings. Recorded as a residual risk with DW-64.
  - `[maybe-false]` `[reject]` Intent: the late path is the normal one and the extra `reading_seq` per wake shifts Story 4.1's ranges — both stated in the spec and checklists; whether in-window acks are needed is the keep-alive decision under `operator_actions`.
  - `[low]` `[reject]` Intent: no `operator_actions` or awaiting-operator status in the diff — set at finalization.

## Design Notes

**Radio messages** (ESP-NOW v1 payload ≤ 250 bytes; first byte is the kind):

| Kind | Direction | Body |
|------|-----------|------|
| `0x01` Probe | Node → broadcast | 8-byte nonce |
| `0x02` ProbeReply | Hub → Node unicast | nonce echo, 1 byte `pending` (0/1) |
| `0x03` Uplink | Node → Hub unicast | `SealedEnvelope` bytes |
| `0x04` Downlink | Hub → Node unicast | `SealedEnvelope` bytes |

Probes are unauthenticated on purpose: a forged reply costs one wasted burst and cannot reset the miss count or delete anything. The first Hub to answer a probe is the Hub for that wake ("any enrolled Hub may relay any Node"). Peers follow the station channel (`channel: None`, spike F-7).

**Wake sequence:** buffer the report → probe the known channel (or scan) → if `pending`, wait for the slot Downlink and apply it → send up to 8 unacked reports oldest first → listen 300 ms after the last send → persist link state if it changed → radio off. No probe reply means nothing is sealed or sent and the wake is a miss.

**Why the late-ack path is the normal one today:** `BoardNet` opens a fresh TLS session per POST (627–756 ms in the spike), longer than the window. The Hub therefore sends each Downlink when it arrives and also keeps it in the slot; the Node usually collects it on its next wake. A kept-alive connection would move acks inside the window and is left for later work.

**Clock from a late ack:** persist, with the burst's counter range, the uptime and boot ID at send. For a fresh downlink, wall-clock offset = `server_time_ms − uptime_at_send`, valid only for the same boot ID. This needs no Node-side downlink replay state, so DW-66 is not made worse: a replayed downlink can only delete Readings the Server already stored.

**Hub memory:** queue and slot tables are fixed-size (suggested: 8 frames per POST, 16 slots, oldest slot evicted). A fast clock that needs more than 1 s back per wake keeps drifting; that is DW-64's open decision.

## Verification

**Commands:**
- `cargo build --workspace --all-targets --locked` -- expected: success
- `cargo build -p coldframe-hal -p coldframe-crypto -p coldframe-protocol -p coldframe-setup -p coldframe-uplink -p coldframe-sensing -p coldframe-transport --locked` -- expected: success without mocks
- `cargo fmt --all --check` -- expected: no diff
- `cargo clippy --workspace --all-targets --locked -- -D warnings` -- expected: no warnings
- `cargo test --workspace --locked` -- expected: all pass
- In `apps/rs/hub` and `apps/rs/node`, after `source ~/export-esp.sh` (when the esp toolchain is installed): `cargo build --release --locked`, `cargo build --features dev-mode --locked`, `cargo fmt --check`, `cargo clippy --release --locked -- -D warnings`, `cargo clippy --features dev-mode --locked -- -D warnings`; Hub also `./check-image.sh` -- expected: success

**Manual checks (if no CLI):**
- `docs/bench/node-transport-checklist.md` exists, follows the lettered-part format, and covers reach from the farthest Lot, one hour with the Hub off, a router channel change, counter continuity after power cuts and clock sync.

## Auto Run Result

Status: awaiting-operator

**Summary:** The Node buffers each wake report in flash, seals it as a `NodeFrame` under a block-reserved counter and sends it over ESP-NOW; it deletes a report only on an authentic sealed downlink, follows the Hub's channel after three misses and sets its clock only from fresh downlinks. The Hub relays sealed envelopes unread to `POST /device/ingest` and returns sealed downlinks, keeping missed ones in RAM for the Node's next wake. Nothing has run on hardware.

**Files changed:**
- `packages/rs/protocol` -- `coldframe.device.v1` types, `encode_node_frame`, `radio` messages.
- `packages/rs/hal` -- `DatagramRadio` trait and mock, `Flash` for `&mut F`.
- `packages/rs/transport` (new) -- report buffer, link state, frame building, wake transport step, clock rule.
- `packages/rs/sensing` -- `report_seq` per wake report.
- `packages/rs/uplink` -- request signing by path, base64, ingest JSON with unescaping, `Relay`, `relay_step`, `relay_until`.
- `apps/rs/hub` -- `esp-now` feature, `board/espnow.rs` radio task, wiring, buffers.
- `apps/rs/node` -- partitions `cf_frame`, `cf_link`, `cf_buf`, `board/espnow.rs`, wall clock, transport call.
- `tests/rs/{protocol,hal,sensing,transport,uplink}` -- vector, matrix-row, power-cut, relay and cross tests.
- `.github/workflows/ci.yml`, `Cargo.toml`, lock files -- new crate in the workspace and the no-mock build.
- `docs/bench/node-transport-checklist.md` (new), other checklists and READMEs, `packages/proto/README.md`.

**Review:** 41 findings. Patched 20 rows (13 entries): high 2 entries (backlog never draining; escaped `+` in downlinks), medium 8, low 3. Deferred 2 (adjacent-channel probe reply; Hub stack headroom). Rejected 19; each reason is in the Review Triage Log.

**Follow-up review recommended:** true. Two high entries were patched and the patched code was not reviewed again: the multi-downlink `Relay` pool with its eviction, and `json::unescape`.

**Verification:**
- `cargo build --workspace --all-targets --locked`, the no-mock build with `coldframe-transport`, `cargo fmt --all --check`, `cargo clippy --workspace --all-targets --locked -- -D warnings`: pass.
- `cargo test --workspace --locked`: 350 passed, 0 failed, 0 ignored. Every matrix row has a passing test.
- Hub and Node: release and `dev-mode` builds, `cargo fmt --check`, both clippy runs: pass. Hub `./check-image.sh`: pass.

**Residual risks:**
- The Hub keeps up to 8 downlinks per Node, which departs from "the latest downlink" in the contract and AD-9; see Spec Change Log.
- Acknowledgements normally arrive one wake late, because the Hub opens a fresh TLS session per POST.
- A Node clock that runs fast by more than 1 s per wake keeps drifting (DW-64); forward corrections are applied in full, not slewed.
- The flash buffer clears bits in programmed words; only the mock has exercised this.
- Boards flashed before this story may need `0x14000`-`0x1FFFF` erased once.
- Wake duration and power with the radio on every wake are unmeasured.
