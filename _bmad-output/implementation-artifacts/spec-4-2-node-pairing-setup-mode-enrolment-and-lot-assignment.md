---
title: 'Story 4.2: Node pairing: setup mode, enrolment and Lot assignment'
type: 'feature'
created: '2026-09-30'
baseline_revision: '1451642e36521d15ea57b026126e2df71a3f8844'
status: 'awaiting-operator'
review_loop_iteration: 2
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-4-1-node-firmware-foundation-wake-measure-sleep.md'
  - '{project-root}/_bmad-output/specs/spec-coldframe/device-hardware.md'
warnings: ['oversized']
deferred: []
operator_actions:
  - "On two ESP32-S3 Nodes wired to the reference wiring in apps/rs/node/src/board/pins.rs (setup button from GPIO7 to ground), flash dev-mode builds from apps/rs/node. Run Parts A to E and G of docs/bench/node-setup-checklist.md. Expect no advertising before a press; a short press logging wake plan=report-now and sleep ms=900000; a long press advertising until setup end=window-closed about 180 s after setup mode window_ms= is logged; a wrong code then the right one; node-setup plus the POST answering 201 with lotId, and 409 lot-claimed for a second Node on the same Lot; and a stuck button arming only the timer wake (button_armed=false)."
  - "In Part B, confirm that a button wake logs wake cause=button. This settles whether esp-hal reports the ESP32-S3 deep-sleep pad wake as ext0/ext1, which the firmware relies on."
  - "Run Part F of docs/bench/node-setup-checklist.md with both Nodes in setup mode. Confirm each logs coex espnow rx from the other while board 1 completes the BLE setup of Part E. This validates BLE and ESP-NOW coexistence on one Node."
  - "Record the results in the Result table of docs/bench/node-setup-checklist.md. Replace 'pending' in the coexistence open item of _bmad-output/specs/spec-coldframe/device-hardware.md with the observed outcome, and commit both files."
---

<intent-contract>

## Intent

**Problem:** A Node can't be paired yet. It has no setup button, no BLE setup service, and no way to be enrolled onto a Lot. The Server enrols Hubs, but a Lot has no `Claim` and a Device has no `DeviceAssigned`, so the Server cannot enforce "one Node per Lot" (FR2, N-3, AD-18, AD-25).

**Approach:**
- **Setup button:** the Node gets a setup button that wakes it from deep sleep. A host-tested classifier turns the press into a plan: a long press runs a time-bounded BLE setup window; a short press takes a Reading now; a bounce goes back to sleep.
- **Setup session:** `coldframe-setup` gets a Node profile of the AD-25 session. It reports kind Node, requires a Lot in the binding, never touches Wi-Fi, and returns the HPKE-sealed `K_dev` exactly as the Hub does.
- **Server:** the Device grain claims the Lot through a new `ILotGrain.Claim`, then journals `DeviceEnrolled` and `DeviceAssigned` together.
- **Coexistence:** a dev-mode ESP-NOW probe during setup mode lets a bench checklist confirm that BLE and ESP-NOW coexist.

## Boundaries & Constraints

**Always:**
- **BLE only in setup mode.** The Node starts the BLE controller and advertises only after a long press, and only until the window closes. The window closes at `SETUP_WINDOW_MS = 180_000` from its start, or when an enrolled session's connection ends. On every other path, BLE is never initialized.
- **Setup-mode exit.** Setup mode always ends in a normal `run_wake` followed by deep sleep, so the Readings that follow pairing arrive promptly.
- **Button:** GPIO7, active low with a pull-up that stays on in deep sleep. The deep-sleep wake is EXT0 on low level. If the button is still low when the Node goes to sleep, it arms only the timer wake, so a stuck button never makes the Node re-wake in a loop.
- **Press classification** (in `coldframe-sensing`, polling every `PRESS_POLL_MS = 10`):
  - released before `DEBOUNCE_MS = 50` → `Spurious`;
  - released before `LONG_PRESS_MS = 3_000` → `Short`;
  - held for 3 000 ms → `Long`, returned as soon as the threshold is reached, without waiting for release;
  - a pin read error → `Spurious`.
- **Setup session rules.**
  - A wrong setup code fails the session exactly as it does on the Hub: one sealed `SetupError`, then a disconnect. The window keeps advertising for another attempt until it closes.
  - The Node profile's `Identity.kind` is `NODE`.
  - A `SiteBinding` needs a non-empty `site_id` and a non-empty `lot_id`, and must not carry a `server_url`; otherwise the session replies `MALFORMED_MESSAGE`. The binding is validated only: the Server, not the Node, holds the assignment (AD-18).
  - `WifiScanRequest` and `WifiConfig` reply `UNEXPECTED_MESSAGE`.
- **Watchdog.** During setup mode the RWDT is raised to `SETUP_WINDOW_MS + 30 s`, and restored to 30 s afterwards.
- **Server claim rule.** `Lot.Claim(siteId, nodeId)` succeeds only if the Lot exists on that Site, isn't removed, and is either free or already held by that Node (AD-18). The Device grain journals `DeviceEnrolled` (when new) and `DeviceAssigned` in one `ConfirmEvents`, and only after a successful claim.
- **API compatibility.** `lotId` is optional everywhere in the API, so `oasdiff` reports no breaking change. A Hub request carrying `lotId` gets a 400.
- **Pinning and TDD.** Pin new Rust dependencies exactly, with the Hub's versions. Write tests first, red → green.

**Never:**
- **Out of scope for this story:**
  - ESP-NOW Reading transport, sealing, acknowledgements or downlinks (Story 4.4);
  - app UI (Story 4.3);
  - move and unassign (Story 4.9);
  - the Devices read model (Story 3.7).
- **ESP-NOW code limits.** No ESP-NOW code runs in a release build. The coexistence probe is `dev-mode` only.
- **Node persistence.** The Node never stores the Site, the Lot or Wi-Fi settings. Its `cf_setup` holds only the CFPC setup-code record.
- **Hub.** The Hub's behaviour does not change. Its only change is implementing the new `SetupLink::accept_within`.
- **Setup-code storage.** The setup code is never regenerated when its record is corrupt. The Node logs an error and skips setup mode.
- **Logging.** No logs of keys, sealed payloads or Reading values in release builds. Values may be logged only in `dev-mode` builds.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Long press | EXT0 wake, low ≥3 s | `WakePlan::Setup`: advertise, serve sessions until enrolled or the window closes, then `run_wake`, sleep | none |
| Short press | low 50 ms–3 s | `WakePlan::ReportNow` → `run_wake` now, sleep a full period; no BLE | none |
| Bounce | low <50 ms or read error | `WakePlan::SleepAgain`: no measurement, sleep a full period | none |
| Timer/cold boot | no button | `WakePlan::Measure` as in Story 4.1; no BLE | none |
| Nobody connects | window elapses | `NodeSetupEnd::WindowClosed`; `accept_within` got the remaining time | none |
| Wrong code | first sealed message fails to open | one sealed `SetupError`, disconnect, advertise again within the window | none |
| Enrolment | right code, identity, binding with lot, enrolment request | `Identity{kind NODE}`, `EnrolmentResponse` matching the enrolment vector; the connection ends, then `Enrolled` | fingerprint mismatch → `FINGERPRINT_MISMATCH`, nothing sealed |
| Hub-shaped binding | `server_url` set or `lot_id` missing | `MALFORMED_MESSAGE`, session stays open | none |
| Session outlives window | deadline passes mid-session | receives time out at the deadline, disconnect, `WindowClosed` | none |
| Stuck button | still low at sleep | timer wake only | none |
| Claim free Lot | Node, Site OK, free Lot | 201 `{id, kind: node, siteId, lotId}`; `device.enrolled` + `device.assigned` on `device/{id}`; `lot.claimed` on `lot/{id}` | none |
| Occupied Lot | Lot held by another Node | 409 `urn:coldframe:problem:lot-claimed` ("This Lot already has a Node."); device stream unchanged | Site registration may remain; a retry with a free Lot enrols |
| Missing Lot | Lot unknown, removed, or on another Site | 404 `urn:coldframe:problem:lot-not-found`; nothing journaled on the Device | none |
| Retry | same Node, same Lot again | 201, no new events | none |
| Other Lot | Node already assigned to Lot A, request Lot B | 409 `urn:coldframe:problem:device-assigned`; nothing changes | move is Story 4.9 |
| Concurrent claims | two Nodes claim one Lot at once | exactly one `Enrolled`, the other `LotOccupied`; one `lot.claimed` | Lot grain is non-reentrant |
| Hub with lot | `kind: hub`, `lotId` set | 400 validation problem | none |

</intent-contract>

## Code Map

- `packages/rs/setup/src/session.rs:147,196,242,293,299,312-350` -- `Session::new`, the hello, wrong-code handling, the hard-coded `DeviceKind::Hub`, the Hub-only `site_binding`, and `enrolment` (reuse as-is).
- `packages/rs/setup/src/service.rs:76-200` -- `boot`, the `run_setup` loop and `serve`. `serve`'s receive loop is the model for the Node loop.
- `packages/rs/setup/src/{lib.rs:67,store.rs,app.rs,code.rs}` -- `SESSION_IDLE_TIMEOUT_MS`, `load_or_create_code` (CFPC at offset 0), and `app::site_binding(site, server)`, which cannot carry a Lot.
- `packages/rs/hal/src/ble.rs:38-80`, `mock.rs:593-770` -- the `SetupLink` trait and `MockSetupLink`. Its `accept` returns `Transport` when no connections are left.
- `packages/proto/coldframe/setup/v1/setup.proto:78,166-176` -- `DeviceKind::NODE`. `SiteBinding.lot_id` is documented "for a Node".
- `packages/rs/crypto/src/hpke.rs:186,209` -- `seal_enrolment` and `fingerprint`. The Server key arrives in `EnrolmentRequest`.
- `tests/rs/setup/tests/{session.rs,common/mod.rs}` -- the scripted-app pattern and vector checks: `a_wrong_code_gets_one_sealed_error_then_a_disconnect` at :423 and the happy path at :262.
- `tests/rs/setup-client/src/main.rs:5-14,324,448-470` -- the bench client. `enrol_device_request` builds the POST body.
- `packages/rs/sensing/src/wake.rs:170,181,308` -- `WakeOutcome`, `WakeCause`, `run_wake`, and `boot_id(counter, cause)`.
- `apps/rs/node/src/main.rs:176-284`, `board/{sleep,pins,radio,flash}.rs`, `partitions.csv:2`, `Cargo.toml:25-29`:
  - the boot flow and the RWDT;
  - `wake_cause()` returns only `Timer`/`ColdBoot`;
  - `deep_sleep` has a timer wake only;
  - the lazy radio;
  - partition labels;
  - the gap at 0x12000 is reserved for `cf_setup`;
  - esp-radio has `wifi` only.
- `apps/rs/hub/src/board/ble.rs:67-366`, `main.rs:169-285` -- the trouble-host GATT server, the `ADVERTISE` signal, `BoardSetupLink`, and the BLE-before-Wi-Fi controller order. Copy this for the Node, adding a `Coldframe Node XXXX` name.
- `apps/rs/spike-hub-radio/src/bin/node.rs`, `Cargo.toml:20` -- ESP-NOW over esp-radio (`esp-now`, `ble`, `coex` features), for reference.
- `packages/cs/contracts/Devices/{DeviceGrains.cs:57-190,DeviceEvents.cs:30,DeviceKind.cs}` -- `IDeviceGrain.Enrol`, `EnrolDevice`, `DeviceEnrolmentOutcome`, `DeviceSummary`, `DeviceEnrolled`.
- `apps/cs/server/Devices/{DeviceGrain.cs:33-75,DeviceState.cs}` -- the enrol flow: other-Site check, then `ISiteGrain.RegisterDevice`, then `RaiseEvent`/`ConfirmEvents`.
- `packages/cs/contracts/Lots/{LotGrains.cs:278,LotEvents.cs:248}`, `apps/cs/server/Lots/{LotGrain.cs:12,79-83,109,LotState.cs}`:
  - `ILotGrain` (keyed by Lot ID; each method takes `siteId`) and `LotOutcome`;
  - `LotClaimed`/`LotReleased` already exist;
  - `ClaimedBy` already exists, and removal already refuses a claimed Lot;
  - `CatchUpLotsAsync` gives read-your-writes.
- `apps/cs/server/Edge/{EdgeApi.cs:85,93,172,560-640,EdgeProblems.cs:33,38,EdgeValidation.cs}` -- the `enrolDevice` handler and DTOs, `ToHttpResult`, and the `LotNotFound`/`LotClaimed` problem URNs. `CanonicalizeLotId` lives in `EdgeApi`.
- `packages/openapi/coldframe.openapi.json:196-230,568,586,600-615` -- `enrolDevice`, `EnrolDeviceRequest`, `Device`, and the problem-type enum. A copy is kept in `tests/cs/server.tests/Fixtures/coldframe.openapi.json`, which `EdgeEndpointDiscoveryTests` reads.
- `tests/cs/server.tests/Journal/FixtureJournalReplayTests.cs:114`, `Fixtures/journal.json` -- every event alias needs a fixture row.
- `tests/cs/server.integration/{Identity/IdentityCluster.cs:23,Identity/SiteDeviceRegistrationTests.cs:86,141,Devices/EnrolmentTests.cs:321,Edge/LotsTests.cs,Edge/EdgeApiFixture.cs:273}` -- the TestCluster fixture, seeding helpers, and the HTTP enrolment helpers.
- `tests/cs/device-simulator/SimulatedDevice.cs:64,80-104` -- `Create(DeviceKind.Node)` and `SealEnrolment`.
- `docs/bench/{hub-setup-checklist.md,node-power-checklist.md}` -- the checklist shapes.

## Tasks & Acceptance

**Execution:**
- `packages/rs/hal/src/{ble.rs,mock.rs}` -- add `async fn accept_within(&mut self, timeout_ms: u32) -> Result<(), LinkError>` (`Timeout` when nobody connects in time) as a required method. `MockSetupLink` opens the next queued connection; with none queued, it records the timeout (`accept_timeouts()`) and returns `Timeout`. Also add a scripted `MockButton` (an `InputPin` that replays a level sequence, then holds the last level). -- the window needs a bounded accept.
- `apps/rs/hub/src/board/ble.rs` -- implement `accept_within` with `embassy_time::with_timeout` around the existing accept. No other Hub change.
- `packages/rs/sensing/src/{button.rs,lib.rs,wake.rs}`:
  - `button.rs`:
    - `enum Press {Spurious, Short, Long}` and `async fn classify_press(button: &mut impl InputPin, timer: &mut impl Timer) -> Press`;
    - `enum WakePlan {Measure, ReportNow, Setup, SleepAgain}` with `fn wake_plan(cause, press: Option<Press>) -> WakePlan`;
    - `fn arm_button_wake(button: &mut impl InputPin) -> bool`: true only when the pin reads released; held or a read error gives false;
    - `fn sleep_after(plan: WakePlan, measured_sleep_ms: u32) -> u32`: `Measure` → `measured_sleep_ms`, every other plan → `WAKE_PERIOD_MS`. The firmware uses it for the sleep it actually takes.
  - `wake.rs`: add `WakeCause::Button`. `boot_id` treats it like `Timer`.
- `packages/rs/setup/src/{session.rs,node.rs,app.rs,lib.rs}`:
  - `Session::node(code, keys, fw)` with a private profile carrying the Node rules above, plus `is_enrolled()`. `Session::new` behaves exactly as today.
  - `node.rs`:
    - `SETUP_WINDOW_MS`;
    - `enum NodeSetupEnd {Enrolled, WindowClosed}`;
    - `async fn run_node_setup(link, rtc, trng, keys, code, fw, window_ms) -> Result<NodeSetupEnd, SetupError>`.
  - `NODE_SESSION_IDLE_TIMEOUT_MS = 60_000`: a Node connection with no write for 60 s is dropped, and the window advertises again. The Hub's 300 s idle timeout would exceed the whole window.
  - The deadline is `rtc.uptime_millis()` + the window, taken when setup mode starts advertising. Each accept is bounded by the remaining time, and each receive by `min(NODE_SESSION_IDLE_TIMEOUT_MS, remaining)`. The loop disconnects after every connection and returns `Enrolled` once an enrolled session's connection has ended.
  - `app.rs`: `node_binding(site_id, lot_id)`.
- `tests/rs/setup/tests/node.rs` and `tests/rs/sensing/tests/button.rs` -- red-first tests for every firmware row of the matrix.
  - `node.rs` covers every setup-session row, including an enrolment response that matches `vectors.json` `enrolment` and the kind `NODE`.
  - `button.rs` covers:
    - the thresholds at 49/50 ms and 2 990/3 000 ms;
    - that `Long` is returned without waiting for release;
    - a read error;
    - the `wake_plan` table, which never yields `Setup` without `Long`;
    - `arm_button_wake` (released / held / error);
    - `sleep_after` for all four plans.
  - `node.rs` also covers an idle connection being dropped after `NODE_SESSION_IDLE_TIMEOUT_MS`, with the real `SETUP_WINDOW_MS`, after which the window accepts again.
- `tests/rs/setup-client/src/main.rs` -- add a `node-setup --code --site --lot --enrolment-key [--fingerprint] [--address] [--seconds]` command: identity, `node_binding`, then enrolment. `--lot` must parse as a UUID (it is printed in canonical lowercase), otherwise the command fails with a usage error before any BLE traffic. It prints the `EnrolDeviceRequest` body with `lotId`. `scan` also lists `Coldframe Node` devices. Extend the body test with a Node case.
- `apps/rs/node/{Cargo.toml,Cargo.lock,partitions.csv}`:
  - esp-radio features gain `ble`, `coex` and `esp-now`.
  - Add `trouble-host`, `embassy-sync`, `embassy-futures`, `coldframe-setup` and `coldframe-protocol`, pinned as in the Hub.
  - Add `cf_setup` at 0x12000/0x2000.
- `apps/rs/node/src/{main.rs,board/{ble.rs,button.rs,sleep.rs,pins.rs,radio.rs,flash.rs,coex.rs}}`:
  - GPIO7 goes in `pins.rs`.
  - `wake_cause()` reports `Button` on an EXT0 wakeup.
  - `deep_sleep(…, arm_button: bool)`.
  - Boot order: on a button wake, classify the press first, right after `esp_hal::init` and taking the button pin, before the boot ID and identity. That way only the bootloader delay eats into a short press. Then the boot ID, identity and `wake_plan`.
  - For `Setup`:
    1. Load the setup code from `cf_setup`. When the record is corrupt, log an error, skip setup and continue to `run_wake`.
    2. Log `setup code=`.
    3. Raise the RWDT.
    4. Start the BLE (and, in `dev-mode`, ESP-NOW) controllers.
    5. Call `run_node_setup` and log `setup end=enrolled|window-closed`.
    6. Drop the controllers and restore the RWDT.
    7. Call `run_wake`.
  - `board/coex.rs` (`dev-mode` only): every 2 s, broadcast `b"CFCOEX1" ‖ device_id` on channel 1. Log `coex espnow tx ok=` and every received probe as `coex espnow rx from=<mac>`.
  - Log `wake plan=`.
  - The sleep length comes from `sleep_after`. At sleep entry, log the value actually slept as `sleep ms=<n> button_armed=<bool>`; `button_armed` comes from `arm_button_wake`.
  - `board/ble.rs`: after an attribute-server failure on a new connection, signal `ADVERTISE` again before continuing, so advertising resumes. The Hub copy has the same pre-existing gap and stays unchanged.
- `packages/cs/contracts/Lots/LotGrains.cs`, `apps/cs/server/Lots/LotGrain.cs` -- add `Task<LotResult> Claim(string siteId, string nodeId, CancellationToken ct)` (alias `claim`) and a new appended outcome `LotOutcome.Held`. Its results:
  - `NotFound`: the Lot is uncreated or on another Site;
  - `AlreadyRemoved`: the Lot is removed;
  - `Claimed`: another Node holds the Lot;
  - `Held`: the Lot was free (journal `LotClaimed(nodeId)`, then `CatchUpLotsAsync`) or this Node already holds it (no event).

  Also add `Task<LotResult> Release(string siteId, string nodeId, CancellationToken ct)` (alias `release`) and a new appended outcome `LotOutcome.Released`. When `nodeId` holds the Lot, it journals `LotReleased(nodeId)`, calls `CatchUpLotsAsync`, and returns `Released`. Otherwise it journals nothing and returns `Unchanged` (or `NotFound` for an uncreated Lot or another Site). Only the Device grain calls it. Story 4.9 reuses it.
- `packages/cs/contracts/Devices/{DeviceGrains.cs,DeviceEvents.cs}`, `apps/cs/server/Devices/{DeviceGrain.cs,DeviceState.cs}`:
  - `EnrolDevice` gets an appended `string? LotId = null` with the next `[Id]`.
  - Outcomes gain `LotNotFound`, `LotOccupied` and `AlreadyAssigned`.
  - `DeviceSummary` gets `string? LotId`.
  - New event `[EventType("device.assigned")] DeviceAssigned(string SiteId, string LotId, DateTimeOffset AssignedAt)`; `DeviceState.LotId` and its `Apply`.
  - `Enrol` flow:
    1. The other-Site check.
    2. `RegisterDevice`.
    3. If `LotId` is set:
       - it equals the current Lot → no-op;
       - another Lot is set → `AlreadyAssigned`;
       - otherwise call `Claim` and map `NotFound`/`AlreadyRemoved` → `LotNotFound` and `Claimed` → `LotOccupied`.
    4. Raise `DeviceEnrolled` if it is new and `DeviceAssigned` if it was claimed, then one `ConfirmEvents`.
    5. No compensating release. Orleans' log-consistency adaptor retries a failed or conflicting Device write until it lands, so `ConfirmEvents` does not throw and a granted claim is always recorded while the activation lives. Add no `catch` around `ConfirmEvents`.
  - A Hub request with `LotId` throws `ArgumentException`.
- `apps/cs/server/Edge/{EdgeApi.cs,EdgeProblems.cs}`:
  - `EnrolDeviceRequest.LotId` and `DeviceResponse.LotId`, omitted when null.
  - Validation: canonicalize `lotId`; reject it for a Hub. Use a separate 400 detail for each case: a Hub with a Lot, and a `lotId` that is not a Lot ID.
  - Map `LotNotFound` → 404 `lot-not-found`, `LotOccupied` → 409 `lot-claimed`, and `AlreadyAssigned` → 409 with a new `DeviceAssigned` URN.
- `packages/openapi/coldframe.openapi.json`, its test fixture copy, and `packages/openapi/README.md` -- optional `lotId` (uuid) on the request and on `Device`, the 404/409 responses on `enrolDevice`, and the new problem type in the enum. Also state the idempotency scope in the `enrolDevice` description and the README's idempotency note:
  - the `Idempotency-Key` covers the Device's registration on the Site;
  - `lotId` is checked against the Device on every request;
  - a refused claim persists nothing for the Device, so a retry with the same key may name another Lot.
- `tests/cs/server.tests/Fixtures/journal.json` -- a `device.assigned` row.
- `tests/cs/server.integration/Devices/NodeAssignmentTests.cs` -- red-first TestCluster tests on `IdentityCluster`, for:
  - a free Lot: device aliases `device.enrolled`, `device.assigned`; lot alias `lot.claimed`; the `lots` projection `claimed_by`;
  - an occupied Lot, a removed Lot, and another Site's Lot;
  - a retry, and a different Lot;
  - Hub unchanged;
  - `Task.WhenAll` of two Nodes claiming one Lot: exactly one wins.

  Also add:
  - `Lot.Claim` unit cases: the same Node twice gives one event;
  - `Lot.Release` cases: the holder releases once, another Node or a second release is `Unchanged`, and removal succeeds after a release;
  - a conflict test: activate the Device grain (enrol the Node without a Lot), append an event to `device/{id}` directly through `identity.Store` so the grain's next write conflicts, then enrol with a free Lot. The write is retried and succeeds: the device aliases end `device.assigned`, the Lot's end `lot.claimed`, and the `lots` projection shows it held by that Node. So the Lot is never stranded.
- `tests/cs/server.integration/Devices/EnrolmentTests.cs` -- HTTP cases: a Node with a Lot gets 201 and `lotId`; 409 `lot-claimed`; 404 `lot-not-found`; 409 `device-assigned`; a Hub with a Lot gets 400. `SimulatedDevice` builds Node bodies.
- `docs/bench/node-setup-checklist.md` -- modelled on `hub-setup-checklist.md`. Its parts:
  - A: flash `dev-mode` on two Nodes and record the codes (never commit them);
  - B: a scan shows no Node before a press; a short press logs `report-now` with no advertising, and `sleep ms=900000`;
  - C: a long press advertises; `setup end=window-closed` about 3 min after `setup mode window_ms=` is logged (the window starts when advertising starts, not at the press), then the scan is empty;
  - D: a wrong code, then the right one;
  - E: `node-setup` plus a POST gives 201 with `lotId`; a second Node on the same Lot gets 409. Show the request headers (`Authorization: Bearer …`, `Idempotency-Key`), and say which steps reuse the key (a retry of the same body) and which use a new one (the second Node);
  - F: coexistence: both Nodes in setup mode log `coex espnow rx` from each other while board 1 completes Part E;
  - G: a stuck button;
  - Result table.
- `apps/rs/node/README.md`, `_bmad-output/specs/spec-coldframe/device-hardware.md` -- the button pin, setup mode, log lines and `cf_setup`. The coexistence open item points to the checklist result, marked pending.

**Acceptance Criteria:**
- Given the host workspace, when `cargo test --workspace --locked` runs, then the button, wake-plan and Node-session tests pass, having been seen failing first, and every existing setup test still passes.
- Given `apps/rs/node` and `apps/rs/hub`, when the release and `dev-mode` builds and clippy (`-D warnings`) run on the esp toolchain, then all succeed, and the Node's release build contains no coexistence probe.
- Given the .NET solution, when `dotnet build -warnaserror`, `dotnet format --verify-no-changes` and `dotnet test` run, then the Lot-claim, concurrency and HTTP tests pass. The endpoint-discovery and authorization-matrix tests pass unchanged, and `oasdiff breaking` reports nothing.
- Given a Node on the bench, when the operator follows `docs/bench/node-setup-checklist.md`, then setup mode, the window timeout, a wrong code, enrolment into a Lot, and BLE + ESP-NOW on one Node all behave as specified. This is a manual operator action.

## Spec Change Log

### 2026-09-30 — Review pass 1 loopback
- **Triggering findings:**
  - A failed Device write after a granted `Lot.Claim` leaves the Lot held by a Node that no journal records. A retry with another Lot strands it, and removal is then refused (medium).
  - The press was classified only after the boot ID and identity work, so ordinary short presses read as bounces and "report now" was lost (medium).
  - The "full period after a press" sleep lived only in firmware `main`. Nothing tested it, and the checklist read `wake done sleep_ms=`, which is not the value slept (medium).
- **Amended:**
  - `ILotGrain.Release`, `LotOutcome.Released`, and the Device grain's compensating release, with a conflict-driven test.
  - Press classification first in the boot order.
  - `sleep_after` plus a `sleep ms=` log line.
  - Folded in alongside:
    - `NODE_SESSION_IDLE_TIMEOUT_MS = 60 s`;
    - the window measured from advertising start (docs and checklist);
    - the idempotency scope documented in OpenAPI and the README;
    - setup-client `--lot` UUID validation;
    - re-advertising after a Node attribute-server failure;
    - `arm_button_wake` listed in Tasks.
- **Known-bad state avoided:**
  - Permanently stranded Lots.
  - A short press that does nothing.
  - A sleep-length regression no check can catch.
  - A stalled connection holding the whole window.
  - An operator checklist that cannot pass.
- **KEEP:** Attempt 1 is saved at `/tmp/claude-1000/-var-home-simon-work-escendit-coldframe/466cf20f-9c13-47f6-963a-ee4fc087105d/scratchpad/spec-4-2-attempt-1.patch`. Apply it with `git apply` from the repository root, then make the amendments. Everything else in it was verified green (Rust 252+ tests, both firmware builds, .NET, TS client) and must survive:
  - `accept_within` on the trait, the mock, the Hub and the Node;
  - `MockButton`;
  - `classify_press`, `wake_plan` and `arm_button_wake` with their tests;
  - `Session::node`, `node_binding` and `run_node_setup` with the `tests/rs/setup/tests/node.rs` suite;
  - the setup-client `node-setup` command;
  - the Node firmware: `board/{ble,button,coex}.rs`, `RadioTrng`, `cf_setup`, the 128 KiB heap, the setup-mode steps and the watchdog handling;
  - `Lot.Claim` and `LotOutcome.Held`;
  - the `DeviceGrain` flow and outcomes;
  - the Edge mapping, the OpenAPI changes, the regenerated `packages/ts/api-client/src/schema.ts` and the `journal.json` row;
  - `NodeAssignmentTests` and the HTTP tests;
  - the checklist and the docs.

### 2026-09-30 — Review pass 2 loopback
- **Triggering findings:**
  - The compensating release added in loopback 1 never runs. Orleans' log-consistency adaptor retries the Device's write rather than throwing from `ConfirmEvents`, and the `when` filter read the tentative `State`, which already holds the Lot. So the spec's "call fails, `lot.released`" test could not exist, and the delivered test contradicted it (medium).
- **Amended:**
  - Removed the compensation (Enrol step 5) and replaced its test with the conflict-is-retried test.
  - Rewrote the Design Note.
  - Folded in: separate 400 details for a Hub with a Lot and for a malformed `lotId`, and checklist Part E headers and Idempotency-Key guidance.
- **Known-bad state avoided:**
  - Dead code presented as a guarantee.
  - A release that could free a Lot the pending Device write later records.
  - A spec and test suite that contradict each other.
- **KEEP:** Attempt 2 is saved at `/tmp/claude-1000/-var-home-simon-work-escendit-coldframe/466cf20f-9c13-47f6-963a-ee4fc087105d/scratchpad/spec-4-2-attempt-2.patch`. Apply it with `git apply` from the repository root, then only:
  - delete the `try`/`catch` around `ConfirmEvents`, `ReleaseClaimAsync` and `LogReleaseFailed` in `DeviceGrain`, along with any doc text promising a compensating release;
  - keep `ILotGrain.Release`, `LotOutcome.Released` and their tests;
  - keep `AConflictingDeviceWriteAfterAGrantedClaimIsRetriedSoTheLotIsNeverStranded`;
  - split the two 400 details;
  - edit checklist Part E.

  Everything else in attempt 2 was verified green (Rust 256, .NET 490, both firmware builds, TS client, release ELF free of probe strings) and must survive unchanged.

## Review Triage Log

### 2026-09-30 — Review pass
- verdicts: 26 findings — high 0, medium 6, low 12, false 7, maybe-false 1
- findings:
  - `[low]` `[patch]` (blind) The Idempotency-Key does not cover `lotId`, so the same key with another Lot is not 422. -- Real, but benign: a refused claim persists nothing for the Device. Folded into the loopback amendment: the scope is documented in OpenAPI and the README.
  - `[medium]` `[bad_spec]` (blind) A failed Device write after a granted claim strands the Lot. -- Verified: `LotClaimed` is journaled on `lot/{id}` before the Device's `ConfirmEvents`, and removal refuses a claimed Lot. Amended with `Lot.Release` and a compensating release.
  - `[low]` `[reject]` (blind) An enrolled Hub re-sent as `kind: node` with a `lotId` answers 500. -- Only a hand-crafted request reaches it (the app sends the kind the Device reports). The fix needs a new outcome and problem type.
  - `[false]` `[reject]` (blind) The Node hands out the sealed `K_dev` without a prior `SiteBinding`. -- No bad outcome: the seal opens only at the Server, which enforces the Lot. The binding carries nothing the Node uses (Design Notes), and the Hub session doesn't order it before enrolment either.
  - `[medium]` `[bad_spec]` (blind) Short taps are lost because the press is timed after the boot ID and identity work. -- Verified in `main.rs`: classification ran after both. Amended: classify first.
  - `[low]` `[patch]` (blind) The 300 s idle bound never applies within the 180 s window, so a stalled connection holds the whole window. -- A direct constant fix, folded into the amendment as `NODE_SESSION_IDLE_TIMEOUT_MS = 60 s`.
  - `[low]` `[patch]` (blind) The docs say the window runs from the press, but it runs from advertising start. -- Folded into the amendment: the checklist and docs reworded.
  - `[false]` `[reject]` (blind) The release build compiles ESP-NOW code and the probe. -- `strings` on the release ELF finds 0 matches for `CFCOEX1`, `coex espnow`, `esp_now_send` and `esp_now_init`. The heap size is recorded in the `main.rs` comment.
  - `[low]` `[patch]` (blind) The bench client misreports a rejected binding and does not validate or escape `--lot`. -- Folded into the amendment: `--lot` must parse as a UUID, which also rules out the malformed binding.
  - `[false]` `[reject]` (blind) The spec never says whether a Node may enrol without a Lot. -- Unassigned Nodes are a first-class state in Epic 4 ("Unassigned Node Readings are stored but not evaluated"; Story 4.9 unassign). Optional `lotId` is intended.
  - `[false]` `[reject]` (blind) A second `EnrolmentRequest` in one Node session re-seals `K_dev`. -- No harm: each seal opens only at the Server, and the Hub behaves the same before provisioning.
  - `[low]` `[reject]` (edge) A failed send of the `EnrolmentResponse` still ends the window as `Enrolled`. -- It needs a BLE notify failure right at that frame, and the only cost is a second long press. The fix adds session state.
  - `[false]` `[reject]` (edge) Enrolment without a binding. -- Same refutation as the blind row above.
  - `[medium]` `[bad_spec]` (edge) The Lot stays claimed after the Device's `ConfirmEvents` fails. -- Same root cause as the stranded-Lot row; amended.
  - `[low]` `[reject]` (edge) A Hub re-sent as a Node answers 500. -- Same as the blind row.
  - `[low]` `[patch]` (edge) Idempotency with another `lotId`. -- Same as the blind row; documented.
  - `[low]` `[patch]` (edge) After an attribute-server failure the Node never re-advertises until the window ends. -- Verified at `apps/rs/node/src/board/ble.rs:183-188` (`continue` waits for a consumed `ADVERTISE` signal). Folded into the amendment. The Hub has the same pre-existing code and stays unchanged.
  - `[medium]` `[bad_spec]` (edge) A short press is released before classification starts. -- Same root cause as the blind row; amended.
  - `[low]` `[reject]` (edge) A GATT write with a non-zero offset is taken as a new payload. -- The app fragments to the ATT payload size and never issues prepared or long writes. The code is identical to the Hub's.
  - `[maybe-false]` `[reject]` (edge) The setup-client now needs the `Coldframe Hub` name prefix, so a scan without a local name fails. -- To settle: a BlueZ passive scan against a Hub. If true it is only `low` (a bench tool), because the firmware sends the name in its scan response.
  - `[false]` `[reject]` (edge, claim) "requires a Lot in the binding" is not enforced. -- `node_binding` rejects a binding without a non-empty `lot_id` as `MALFORMED_MESSAGE`. The claim concerns the binding's content, and that is enforced.
  - `[low]` `[reject]` (edge, claim) `is_enrolled` is true even if sending the response failed. -- Same as the failed-send row.
  - `[medium]` `[bad_spec]` (verification-gap) The full-period sleep after a press is untested, and the checklist reads the wrong log value. -- Pre-verified. Amended with `sleep_after` and its tests plus a `sleep ms=` log line, and checklist parts B and C point at it.
  - `[medium]` `[bad_spec]` (verification-gap, other) The `wake done sleep_ms=` log misstates the sleep after a press. -- Same root cause; amended.
  - `[low]` `[reject]` (verification-gap, other) A Hub re-sent as a Node answers 500. -- Same as the blind row.
  - `[false]` `[reject]` (intent) The spec is not at `awaiting-operator` and has no `operator_actions`, and nothing is committed. -- Finalization sets these after review. It is not a code defect.

### 2026-09-30 — Review pass
- verdicts: 22 findings — high 0, medium 6, low 11, false 4, maybe-false 1
- findings:
  - `[medium]` `[bad_spec]` (blind) The compensating release has no test, and the spec asks for one. -- Verified: no test enters the `catch`. Amended: compensation removed.
  - `[false]` `[reject]` (blind) The compensating release could free a Lot the pending write later records. -- It never runs: the `when` filter reads the tentative `State`, which already holds the Lot. It is removed anyway.
  - `[low]` `[bad_spec]` (blind) The release-failed warning promises a 4.9 recovery path that does not exist. -- Removed with the compensation.
  - `[low]` `[reject]` (blind) A bounce costs a full 15-minute period. -- The intent's matrix row "Bounce … sleep a full period" mandates it, and frequent spurious wakes are not demonstrated.
  - `[low]` `[reject]` (blind) There is no release debounce mid-press, so a glitch during a hold gives `Short`. -- It needs a contact glitch during a held press, and the user simply presses again. The fix adds debounce state beyond the contract's definition of a release.
  - `[maybe-false]` `[defer]` (blind) The button wake is detected only as `Ext0`/`Ext1`. -- esp-hal 1.2.2's GPIO sleep hook selects the ext0, ext1 or per-pin path (`rtc_cntl/sleep/wakeup.rs:234`). The per-pin path is light-sleep only on ESP32-S3, so deep sleep should report ext0/ext1. To settle: bench Part B. Medium if true (unverified).
  - `[low]` `[reject]` (blind) The release ELF probe check is not automated. -- The probe is behind `#[cfg(feature = "dev-mode")]`, and CI already fails any release build with `dev-mode`. A CI grep step adds complexity for an unlikely regression.
  - `[low]` `[patch]` (blind) The checklist has gaps: Part E's Idempotency-Key reuse and headers are unclear. -- Folded into the amendment. The corrupt-`cf_setup` bench step is rejected, because it needs a hand-corrupted flash and the path only logs and continues.
  - `[low]` `[patch]` (blind) The same 400 detail is used for a Hub with a Lot and for a malformed `lotId`. -- Folded into the amendment: separate details.
  - `[low]` `[reject]` (blind) The spec has stale references (the /tmp KEEP path, a fixture "copy"). -- The fix edits this build's spec.
  - `[low]` `[reject]` (blind) The setup client does not validate `--site`. -- Bench-only, and the Server's 404 names the problem.
  - `[medium]` `[bad_spec]` (edge) The `when` filter reads the tentative `State`, so the release never runs. -- Verified: `JournaledGrain.State` is the tentative view. Same root cause; amended.
  - `[low]` `[reject]` (edge) carried: an enrolled Hub re-sent as a Node with a Lot answers 500. -- Only a hand-crafted request reaches it; the fix needs a new outcome and problem type.
  - `[low]` `[reject]` (edge) carried: a failed `EnrolmentResponse` send still ends the window as `Enrolled`. -- Rare, and the only cost is a second long press; the fix adds session state.
  - `[low]` `[reject]` (edge) A peer that stops taking notifications blocks `send` past the window. -- BLE supervision ends a stalled link within seconds, and the RWDT (window + 30 s) bounds the worst case with a reset.
  - `[medium]` `[bad_spec]` (edge, claim) The compensation path is dead code. -- Same root cause; amended.
  - `[medium]` `[bad_spec]` (edge, claim) The compensation test contradicts the spec. -- Same root cause; amended.
  - `[low]` `[reject]` (edge, claim) carried: `enrolled` is set before the send succeeds. -- Same as the failed-send row.
  - `[medium]` `[bad_spec]` (verification-gap) The compensating release never runs in any test. -- Pre-verified. Same root cause; amended.
  - `[medium]` `[bad_spec]` (verification-gap, other) The compensation path may be unreachable. -- Confirmed by the conflict test and by `State` being tentative. Same root cause.
  - `[false]` `[reject]` (verification-gap, other) The spec and the test disagree about a conflicting write. -- The disagreement is real, but it is the same root cause as the rows above, and the amendment resolves it. It is not a separate defect.
  - `[false]` `[reject]` (intent) carried: no commit, no `awaiting-operator`, no `operator_actions`. -- Finalization sets these after review.

### 2026-09-30 — Review pass
- verdicts: 18 findings — high 0, medium 0, low 11, false 5, maybe-false 2
- findings:
  - `[low]` `[reject]` (blind) A refused claim leaves the Site's `site.device-registered`, so a future Devices list could show a Node that never enrolled. -- The Design Notes accept this: the registration is idempotency bookkeeping, and nothing reads it as a roster today. Story 3.7 should build its list from `device.enrolled`. Fixing it now means reordering the Site and Lot calls, which risks an orphan claim when the Site refuses the key.
  - `[low]` `[patch]` (blind) No test proves a claim left by a crash heals on a retry with the same Lot. -- Added `AClaimLeftBehindByACrashBeforeTheDeviceWriteHealsOnARetryWithTheSameLot`. The retry-with-another-Lot residual stays as documented (Story 4.9).
  - `[low]` `[patch]` (blind) Re-enrolling an assigned Node without `lotId` is untested and undocumented. -- Added `ReEnrollingAnAssignedNodeWithoutALotKeepsItsLotAndJournalsNothing` and "Leaving out lotId never unassigns a Node." to `enrolDevice`; `schema.ts` regenerated.
  - `[low]` `[patch]` (blind) The OpenAPI response `DeviceOnAnotherSite` is no longer referenced. -- Deleted it; `oasdiff breaking` reports nothing.
  - `[maybe-false]` `[reject]` (blind) carried: the Hub bench `setup` now picks by the `Coldframe Hub` name prefix. -- To settle: a BlueZ passive scan against a Hub. If true it is only `low` (a bench tool), because the firmware sends the name in its scan response.
  - `[false]` `[reject]` (blind) The setup code is printed on every long press, in release builds too. -- The code proves physical possession, and printing it needs UART access to a Node that someone is already long-pressing, which is physical possession. The Hub prints it the same way.
  - `[low]` `[patch]` (blind) Checklist Part E asks for event-stream checks the operator can't make. -- Replaced them with the POST responses and `DELETE` of the claimed Lot answering 409 `lot-claimed`; Part F reworded the same way.
  - `[low]` `[reject]` (blind) `classify_press` counts sleeps, so the thresholds drift on hardware. -- The per-poll overhead is microseconds against a 10 ms poll, a sub-percent drift on UX tolerances.
  - `[false]` `[reject]` (blind) A timed-out `accept_within` can lose a later Connected event. -- On the Node, `accept_within` only times out at the deadline, which ends the window with no later `accept`. The Hub never calls `accept_within`.
  - `[low]` `[reject]` (blind) The Node's dependency graph includes `coldframe-uplink` through `coldframe-setup`. -- It has no runtime effect (dead code is dropped at link), and a feature gate adds build complexity.
  - `[low]` `[reject]` (blind) `wake done sleep_ms=` is misleading after a press. -- The README and checklist point operators at `sleep ms=`, and Story 4.1's power checklist relies on the existing field.
  - `[low]` `[reject]` (edge) carried: an enrolled Hub re-sent as a Node with a Lot answers 500. -- Only a hand-crafted request reaches it; the fix needs a new outcome and problem type.
  - `[low]` `[reject]` (edge) carried: a failed `EnrolmentResponse` send still ends the window as `Enrolled`. -- Rare, and the only cost is a second long press; the fix adds session state.
  - `[maybe-false]` `[reject]` (edge) carried: the setup-client prefix pick fails without a scan-response name. -- Same as the blind row.
  - `[low]` `[patch]` (verification-gap) `run_node_setup`'s exit on a transport failure is never exercised. -- Added `MockSetupLink::fail_accept_within` and `a_transport_failure_while_advertising_ends_setup_without_accepting_again` (red, then green).
  - `[false]` `[reject]` (intent) carried: no commit, no `awaiting-operator`, no `operator_actions`. -- Done at this finalization.
  - `[false]` `[reject]` (intent) `Lot.Claim` also succeeds for the Node that already holds the Lot, where the epic says "free". -- AD-18 (ARCHITECTURE-SPINE.md:300) says "free or already held by n". That is the idempotent retry.
  - `[false]` `[reject]` (intent) An assigned Node still advertises on a long press, though the epic's precondition is "an unassigned Node". -- The AC's rule is "it never advertises otherwise", meaning without a long press, and that is enforced. The Node stores no Lot, and the Server refuses a different Lot with 409 `device-assigned`.

## Design Notes

- **Why the Node validates the binding but ignores it:** the Lot is chosen in the app and enforced by the Server (`Lot.Claim`). The sealed `K_dev` is valid for any Lot, so a 409 from the Server needs no second BLE session. The app can POST again with another Lot.
- **Why `accept_within` is a trait method:** a window that is only enforced by an embassy `select` in the board could not be host-tested. A deadline taken from `Rtc` plus bounded link calls keeps the logic crate authoritative. `MockTimer` never advances `MockRtc`, so tests move time with `MockRtc::advance`, driven from the peer callback.
- **The order of Site registration and the claim** follows the existing flow: Site first, then the claim. A failed claim leaves only the Site's idempotent registration, which journals no Device event, and a retry with any Lot reuses it. That keeps the claim the only occupancy check (AD-18: a synchronous grain call, never a read model).
- **Why there is no compensating release:** AD-18 orders the calls Lot first, then the Device journal, and the two streams share no transaction. Orleans' log-consistency adaptor keeps retrying the Device's write (the conflict test shows it), so a live activation always records a granted claim. A `catch` around `ConfirmEvents` would be dead code, and a release there could even free a Lot that the pending write later records. The residual is a silo crash between the two writes; Story 4.9's persisted pending state and its use of `Lot.Release` cover it.
- **Why classify before identity:** the press is only observable while the finger is on the button. Every millisecond of boot work before the first poll shortens the shortest press that counts.
- **Why coexistence is a dev-mode probe:** the real ESP-NOW transport arrives in Story 4.4. The probe exercises `esp-radio` `coex` with both stacks live in one image, which is what the device-hardware open item asks for, without shipping radio code in release.

## Verification

**Commands:**
- `cargo test --workspace --locked && cargo clippy --workspace --all-targets --locked -- -D warnings && cargo fmt --all --check` -- all green
- `cd apps/rs/node && source ~/export-esp.sh && cargo build --release --locked && cargo build --features dev-mode --locked && cargo clippy --release --locked -- -D warnings && cargo clippy --features dev-mode --locked -- -D warnings && cargo fmt --check` -- all succeed; repeat the builds and clippy in `apps/rs/hub`
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror && dotnet format --verify-no-changes --no-restore && dotnet test --no-build` -- green (integration tests need Docker for the AppHost)
- `cargo test -p coldframe-setup-client --locked` -- the Node body test passes (also part of the workspace run)

**Manual checks (if no CLI):**
- On hardware: setup mode, window, wrong code, enrolment, and coexistence (operator, via the checklist).

## Auto Run Result

Status: awaiting-operator

**Summary:** Story 4.2 pairs a Node with a Lot.
- **Setup button** (`coldframe-sensing::button`):
  - The Node's setup button (GPIO7, EXT deep-sleep wake) is classified first thing on a button wake.
  - A long press (3 s) enters setup mode, a short press reports now, and a bounce sleeps again.
  - `wake_plan`, `arm_button_wake` (a stuck button arms only the timer) and `sleep_after` (a full period after any press) are pure functions with host tests.
- **Setup window** (`coldframe-setup::node`):
  - `Session::node` reports kind `NODE`, requires a Lot and no Server in the binding, refuses Wi-Fi messages, and hands out `K_dev` HPKE-sealed exactly as the Hub does. A wrong code fails the session.
  - `run_node_setup` serves sessions for at most 180 s from advertising start, with a 60 s idle drop per connection.
  - `SetupLink::accept_within` makes the window host-testable.
- **Node firmware:**
  - BLE runs only in setup mode, with a raised watchdog, and the `cf_setup` partition holds the setup code.
  - A `dev-mode`-only ESP-NOW coexistence probe runs during setup; the release ELF contains no probe strings.
- **Server:**
  - `ILotGrain.Claim` (the only occupancy check, AD-18) and `ILotGrain.Release` (for Story 4.9).
  - A Node enrolment with `lotId` claims the Lot, then journals `DeviceEnrolled` and `DeviceAssigned` in one write.
  - Errors: 409 `lot-claimed` for an occupied Lot, 404 `lot-not-found`, 409 `device-assigned` for a Node already on another Lot, and 400 for a Hub with a Lot.
  - `lotId` is optional in the OpenAPI (no breaking change).
- **Bench:** `coldframe-setup-client node-setup` and `docs/bench/node-setup-checklist.md`.

**Files changed:**
- `packages/rs/hal/src/{ble.rs,mock.rs}` -- `accept_within`, `MockButton`, `MockSetupLink` bounded accepts and `fail_accept_within`.
- `packages/rs/sensing/src/{button.rs,lib.rs,wake.rs}` -- the press classifier, `wake_plan`, `arm_button_wake`, `sleep_after`, and `WakeCause::Button`.
- `packages/rs/setup/src/{session.rs,node.rs,app.rs,lib.rs,service.rs}` -- the Node session profile, the setup window, and `node_binding`.
- `apps/rs/hub/src/board/ble.rs` -- `accept_within` only.
- `apps/rs/node/**` -- the button, setup mode, the BLE adapter, the `dev-mode` coexistence probe, `cf_setup`, dependencies, and the README.
- `tests/rs/{sensing/tests/button.rs,setup/tests/node.rs,setup/tests/common/mod.rs,setup-client/**}` -- the host tests and the bench client's `node-setup` command.
- `packages/cs/contracts/{Devices,Lots}/*`, `apps/cs/server/{Devices,Lots,Edge}/*` -- `Lot.Claim`/`Release`, `DeviceAssigned`, the enrolment flow, and the HTTP mapping.
- `packages/openapi/{coldframe.openapi.json,README.md}`, `packages/ts/api-client/src/schema.ts` -- the contract.
- `tests/cs/**` -- `NodeAssignmentTests` (TestCluster, including concurrent claims), the HTTP enrolment tests, a `journal.json` row, the simulator's Node bodies.
- `docs/bench/node-setup-checklist.md`, `_bmad-output/specs/spec-coldframe/device-hardware.md`, `apps/rs/README.md`, `packages/rs/README.md` -- docs.

**Review findings:**
- **Pass 1:** 26 findings, triaged as bad_spec (the stranded Lot on a failed Device write, lost short presses, and the untested full-period sleep).
  - The spec was amended, the code reverted and re-derived, and the patches were folded in: the 60 s idle drop, the window from advertising start, the idempotency scope documented, `--lot` UUID validation, and re-advertising after an attribute-server failure.
- **Pass 2:** 22 findings, triaged as bad_spec: the pass-1 compensating release was dead code, because Orleans retries the Device's write and the filter read the tentative `State`.
  - The compensation was removed, and a test shows the conflicting write is retried.
  - Folded in: separate 400 details, and checklist Part E headers and key guidance.
- **Pass 3:** 18 findings.
  - Patched 5 (all low): the crash-residual heal test, the lot-less re-enrolment test and docs, the unused OpenAPI response, observable checklist checks, and the transport-failure test.
  - Rejected 13, for the reasons in the triage log: 6 low, 5 false and 2 maybe-false.
  - Deferred: none.

**Follow-up review recommended:** false. The last pass patched 0 high and 0 medium.

**Verification:**
- `cargo test --workspace --locked`: 257 passed, 0 failed. Clippy `-D warnings` and `fmt --check` are clean.
- `apps/rs/node` and `apps/rs/hub`: the release and `dev-mode` builds, both clippy runs and `fmt --check` pass. The release + `dev-mode` build is refused. The Node release ELF has 0 probe strings.
- .NET: `dotnet build -warnaserror` and `dotnet format --verify-no-changes` pass; `dotnet test`: 492/492.
- `oasdiff breaking`: none. TypeScript: typecheck, lint and the api-client test (5) pass.
- The matrix audit found every I/O row covered by a passing test. Red → green was observed for the new Rust tests and the server tests of attempt 1.

**Residual risks:**
- Nothing has run on hardware. These are unverified until the bench run:
  - the EXT0/EXT1 wake cause on ESP32-S3 deep sleep;
  - whether the pull-up holds through sleep;
  - the Wi-Fi stop, then BLE start;
  - BLE + ESP-NOW coexistence.
- A silo crash between `LotClaimed` and `DeviceAssigned`, followed by a retry with another Lot, leaves the first Lot held until Story 4.9.
- A refused claim leaves the Site's idempotent registration, so Story 3.7 should list Devices from `device.enrolled`.
- A press shorter than the bootloader delay reads as a bounce.

**Operator actions owed:** see the frontmatter `operator_actions` (the bench checklist Parts A–G, including the coexistence check, and recording the results).
