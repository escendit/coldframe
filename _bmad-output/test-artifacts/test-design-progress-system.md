---
runScope: 'system-level'
runKey: 'system'
workflowStatus: 'in-progress'
totalSteps: 5
stepsCompleted: ['step-01-detect-mode', 'step-02-load-context', 'step-03-risk-and-testability']
lastStep: 'step-03-risk-and-testability'
nextStep: '{skill-root}/steps-c/step-04-coverage-plan.md'
inputDocuments:
  - _bmad-output/specs/spec-coldframe/SPEC.md
  - _bmad-output/specs/spec-coldframe/acceptance-criteria.md
  - _bmad-output/specs/spec-coldframe/device-hardware.md
  - _bmad-output/planning-artifacts/prds/prd-coldframe-2026-09-25/prd.md
  - _bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md
  - _bmad-output/planning-artifacts/epics.md
  - _bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/EXPERIENCE.md
  - docs/spikes/hub-radio-coexistence.md
  - _bmad/tea/config.yaml
  - knowledge/adr-quality-readiness-checklist.md
  - knowledge/nfr-criteria.md
  - knowledge/test-levels-framework.md
  - knowledge/risk-governance.md
  - knowledge/test-quality.md
  - knowledge/playwright-cli.md
lastSaved: '2026-09-27'
---

# Test Design Progress — System Level

## Step 1: Mode & prerequisites

- **Mode:** System-Level. Both requirements and architecture exist, alongside epics and stories, so system-level is preferred first. No `sprint-status.yaml` exists yet.
- **Prerequisites present:**
  - PRD: `_bmad-output/planning-artifacts/prds/prd-coldframe-2026-09-25/prd.md`, with the spec `_bmad-output/specs/spec-coldframe/SPEC.md` as the primary requirements source
  - Architecture and decision records: `_bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md` (AD-1 to AD-25)
  - Additional context: `epics.md` (10 epics, 53 stories), the UX contract (DESIGN.md, EXPERIENCE.md) and `docs/spikes/hub-radio-coexistence.md`
- **Run identity:** `run_scope=system-level`, `run_key=system`. No prior checkpoint existed.

## Step 2: Context loaded

### Config
- `tea_use_playwright_utils: true` (applies to the web E2E layer once `package.json` exists), `tea_use_pactjs_utils: true`, `tea_pact_mcp: mcp`, `tea_browser_automation: auto`, `test_stack_type: auto`.
- **Detected stack:** today the repository has only `Cargo.toml` (the spike firmware), so it reads as backend. The **planned stack** is fullstack plus mobile plus embedded:
  - .NET 10 Orleans Server with ASP.NET Core and SignalR
  - SvelteKit backend-for-frontend
  - Kotlin Multiplatform core with SwiftUI and Jetpack Compose shells
  - Rust no_std firmware on ESP32-S3
- **Contract testing (Pact):** not relevant. There are no Pact artifacts or dependencies. Contracts are covered by AD-10/AD-24 schema-compatibility checks, golden fixtures and shared test vectors.

### Tech stack and integration points
- **Server:**
  - Orleans 10.3.1 grains, event-sourced into a PostgreSQL 18 JSON journal with an outbox
  - NATS JetStream (stream hints only)
  - Temporal 1.31.3 (Keycloak event pipeline)
  - Keycloak 26.6.7 with Phase Two (OIDC, Organizations)
  - FluentMigrator
  - CloudNativePG
- **External integrations:**
  - APNs and FCM (push)
  - SMTP (Phase Two invitations)
  - Let's Encrypt DNS-01 through a DNS provider API
  - off-node S3 (backups)
- **Device path:** Node → ESP-NOW (sealed Protobuf frames) → Hub → HTTPS JSON `POST /device/ingest` and `/device/heartbeat` (HMAC-authenticated) → Server. Sealed downlinks travel back the same way.
- **Client paths:**
  - REST/OpenAPI for all apps
  - SignalR through the backend-for-frontend proxy (AsyncAPI) for the web app
  - BLE GATT setup protocol (AD-25) between the mobile apps and the Devices
- **Local dev and integration host:** the Aspire AppHost via `Aspire.Hosting.Testing`.

### NFRs with thresholds
| NFR / source | Threshold |
|---|---|
| FR1 Hub provisioning | Hub on its Site ≤ 1 min after credentials are sent |
| FR4, N-2, AD-17 | Readings every 15 min; Node buffer ≥ 24 h; acknowledgement window W = 300 ms |
| FR11 | An Alert opens after exactly 3 consecutive Readings (about 30 min) |
| FR13 | Hub heartbeat every 30–60 s; Silence Window 6 h (Node), 5 min (Hub) |
| FR14 | Low battery: 3 reports below 20 % while not charging |
| FR16 | Default Notification Window 07:00–22:00; one summary per window opening |
| NFR3 Durability | Zero loss of stored Readings, settings or open Alerts on restart |
| NFR4 Energy | Full season without recharging; ≥ 14 days with no sun; sleep current ≤ ~100 µA |
| NFR10 TLS | TLS on every IP hop; public certificates; no plain HTTP |
| NFR14 Accessibility | Text 4.5:1, UI and graphics 3:1; Dynamic Type, font scale, browser zoom; screen readers |
| Spike (radio) | ESP-NOW loss < 1 %; minimum free heap ≥ 32 KiB; channel recovery < 60 s |
| AD-11 | Reject `measured_at` more than 5 min in the future; Hub request timestamps within ±5 min |

### Missing thresholds (questions for step 3)
1. **API latency SLO:** no p95 target for REST or Lot overview load time on the home network.
2. **Availability target** for the home server (single node, stop-then-start upgrades).
3. **RTO:** the RPO is the WAL-archive interval, but no recovery-time objective is set.
4. **Notification latency:** how soon after the window opens, or after an in-window Alert opens, the push must arrive.
5. **Scale envelope:** the maximum Lots, Nodes and Sites per Server to design test data for (spec: "more Lots and Nodes later").

### Missing thresholds: answers (Simon, 2026-09-27)
1. API or overview latency SLO: **none**. No performance or load tests; performance is P3 at most.
2. Availability: **best effort**, single node.
3. RTO: **≤ 1 h**, described as generous. The restore runbook must be completable within 1 h.
4. Notification latency: **≤ 1 min**, from the window opening (summary) or from an in-window Alert opening, to the push being sent.
5. Scale envelope: **no volume or seed data sets**. Test data is created per test case only, synthetically and self-cleaning.

## Step 3: Testability and risk

### 🚨 Testability concerns (actionable first)
| ID | Concern | Evidence | Mitigation |
|---|---|---|---|
| TC-1 | **Time is everywhere and not injectable by rule.** Notification Windows, Reminders, 6 h / 5 min Silence Windows, Pause end dates and daylight-saving handling all depend on wall-clock time. Stories say "fake clock", but no AD mandates a clock abstraction. | AD-6, AD-7, FR13, FR16, FR18; Stories 6.4, 7.1 and 8.1 require fake-clock tests | Every grain, projector and the Notifier take time from .NET `TimeProvider`, and Orleans Reminder wake-ups are driven through it. TestCluster and Aspire test hosts use `FakeTimeProvider`. Make this a convention (add to AD-6 or AD-24). |
| TC-2 | **No Device simulator.** Ingestion, heartbeat, silence and every journey end-to-end test (UJ-2, UJ-3, UJ-4) need sealed Node frames and HMAC-signed Hub requests without hardware. | AD-9, AD-12, AD-17; Stories 4.5, 6.6, 7.2 and 8.2 need end-to-end tests without hardware | A `DeviceSimulator` test library (C#) built on the crypto-spec and Protobuf contracts: enrol, seal frames (counters, `reading_seq`, `spec_hash`), sign heartbeats, verify downlinks. Cross-checked against Rust vectors. |
| TC-3 | **Firmware is tested host-side only.** esp-radio, eFuse, HMAC, flash, ADC and RTC have to sit behind traits, or firmware logic can't be unit-tested. The real radio stack is verified only manually. | NFR16, AD-24 (no on-device CI) | One HAL-trait crate (`packages/rs/hal`) that all firmware logic depends on, with mock implementations in tests. Manual hardware checklists are versioned in `docs/` and their results recorded per release. |
| TC-4 | **External providers in tests.** APNs, FCM, SMTP (Phase Two invitations), the DNS-01 provider and S3 all need stand-ins. | AD-7 Notifier seam, AD-13, AD-15, Stories 2.3, 6.5 and 9.1 | Notifier test double (Server); Mailpit in the Aspire AppHost for invitation email; RustFS for S3 (in 2.3); a self-signed or staging issuer in k3d. DNS-01 against a real provider is checked manually only. |
| TC-5 | **Asynchronous identity pipeline.** Keycloak → Temporal → Orleans is eventually consistent, so integration tests risk hard waits. | AD-3, Stories 1.7, 9.1 and 9.2 | Test helpers that wait for a journal position or projector checkpoint (bounded polling with a timeout). No sleeps (test-quality rule). |
| TC-6 | **BLE can't be automated end to end.** Simulators and CI can't reach a real Device over BLE. | AD-25, Stories 3.4, 3.6, 4.2 and 4.3 | The protocol and state machines are covered by Kotlin and Rust unit tests with mocked transports, plus a shared session test vector. The desktop BLE client (3.4) is used for the manual bench checklist. |
| TC-7 | **No test-data seeding path.** There is no seeding API, and the user wants none beyond per-test data. | Step 2 answer 5; ADR checklist 1.3 | Per-test builders in `tests/cs` that act through grain APIs (or journal fixtures), creating synthetic Sites, Lots, Devices and Readings per test and cleaning up by using unique Site IDs. No production seeding endpoint. |
| TC-8 | **The ≤ 1 min notification latency isn't observable.** Nothing records due-at against sent-at. | Step 2 answer 4 (new threshold) | The Notifier emits a structured log and OpenTelemetry metric with `dueAt` and `sentAt` for each delivery. Tests assert `sentAt − dueAt ≤ 60 s` with the fake clock, plus a real-time check in the Aspire test host. |

### ✅ Testability strengths
- **Event-sourced journal with global position:** state can be replayed deterministically, and the event-replay CI test exists (AD-21, AD-24).
- **Deterministic IDs** (UUIDv5 for Sensors and Alerts) plus **idempotency keys:** retries can be asserted exactly (AD-7, AD-19, conventions).
- **Contract-first packages** (Protobuf, OpenAPI, AsyncAPI, crypto-spec) with compatibility checks and **shared test vectors** in Rust, C# and Kotlin (AD-10, AD-12, AD-24).
- **Clients only render server-computed `LotStatus`:** UI tests reduce to snapshot tests of fixed read-model rows (AD-14).
- **Generated per-Site authorization matrix** (AD-4, AD-24, NFR6).
- **Aspire AppHost as the integration-test host:** real PostgreSQL, NATS, Temporal and Keycloak per run (AD-15, AD-24).
- **Persisted due-at deadlines:** restart behaviour can be tested (kill the silo, then assert catch-up) (AD-6).
- **Headless:** all business logic is behind REST and grain APIs (ADR 1.2 met).

### Architecturally Significant Requirements (ASRs)
| ID | ASR | Status |
|---|---|---|
| ASR-1 | Acknowledge a Reading only after the PostgreSQL commit; per-frame status; deduplicate by `(device, sensor, reading_seq)` (AD-9) | ACTIONABLE |
| ASR-2 | Frame counters never repeat, every frame is sealed fresh, 64-entry replay window, window advanced after a restore (AD-17) | ACTIONABLE |
| ASR-3 | Deadlines are persisted state; Reminders only wake grains; Server downtime isn't Device silence (AD-6) | ACTIONABLE |
| ASR-4 | Roles are read per Site from the projection, with read-your-writes; at least one Owner is guaranteed (AD-3, AD-4) | ACTIONABLE |
| ASR-5 | One key hierarchy with identical derivation in three languages; AD-25 session bound to the setup code (AD-12, AD-25) | ACTIONABLE |
| ASR-6 | A silent Hub suppresses Node silence; `unknownCause` (AD-7, AD-14) | ACTIONABLE |
| ASR-7 | Notification sent ≤ 1 min after due (new) | ACTIONABLE |
| ASR-8 | Restore completes in ≤ 1 h (RTO), and the recovery point is the WAL-archive interval (AD-15, new) | ACTIONABLE |
| ASR-9 | TLS with public certificates on every IP hop, including Hub certificate-date checks (AD-13, H-2) | FYI (config-verified) |
| ASR-10 | Test-first development; firmware tested host-side only (AD-24, NFR16) | FYI (process) |

### Risk register (P × I, 1–9; ≥ 6 needs mitigation; owner: Simon, solo)
| ID | Cat | Risk (source statement) | P | I | Score | Mitigation | When |
|---|---|---|---|---|---|---|---|
| R-01 | SEC/DATA | Nonce reuse or counter reset under the fixed eFuse-derived key ("counter never repeats for the life of the key", AD-17) could allow forgery or permanent rejection | 2 | 3 | **6** | Host-side property tests of counter reservation across simulated reboots and power loss; Server replay-window tests; restore-advance test (4.5) | Epic 4 |
| R-02 | DATA | Reading loss if acknowledged before commit or if a forged ack is accepted ("acknowledged only after durable commit", AD-9; N-1) | 2 | 3 | **6** | Integration test that crashes between insert and response; firmware test that deletes only on an authenticated downlink; simulator end-to-end test of an hour-long Hub outage | Epic 4 |
| R-03 | BUS | Missed, late or duplicate notifications around window opening, restarts, daylight-saving changes and held-then-closed Alerts (FR16; SM-2 "delivered within the Notification Window before 0 %") | 2 | 3 | **6** | Fake-clock TestCluster suite (6.4): restart across 07:00, daylight-saving transitions, summary dedup; ≤ 1 min latency assertion (TC-8) | Epic 6 |
| R-04 | BUS | Silence reads as "all fine": a missed Silent Alert, or Hub suppression hiding a dead Node ("silence must never be mistaken for all fine", SPEC Why; FR13) | 2 | 3 | **6** | Fake-clock tests of Node and Hub windows, suppression lifting, restart without false silence; UJ-3 end-to-end test | Epic 7 |
| R-05 | SEC | Cross-Site access or stale Roles after a change (NFR6; "Role on Site A grants nothing on Site B", FR7) | 2 | 3 | **6** | Generated authorization matrix on every endpoint; multi-Site tests; revocation takes effect on the next request (9.2) | Epics 1, 9 |
| R-06 | SEC | Custom crypto (AD-12 key hierarchy, AD-25 setup session) implemented inconsistently or weakly | 2 | 3 | **6** | Shared vectors in three languages; negative tests (wrong setup code, tampered frame, replay); a review of crypto-spec by a second person or an external review before V1 | Epic 3 |
| R-07 | TECH | Firmware regressions reach hardware undetected, because there are no on-device tests (NFR16 decision) | 3 | 2 | **6** | HAL-trait crate (TC-3); high host-side coverage of protocol and state machines; versioned manual checklist run before each firmware release | Epics 3–4, 10 |
| R-08 | TECH | Pre-release dependencies change under us (Orleans NATS alpha, esp-radio beta, Escendit RCs; spine Stack) | 2 | 2 | 4 | Pinned versions plus CI; stream use behind a seam (AD-5); Renovate PRs gated by the full suite | Ongoing |
| R-09 | OPS | Restore takes longer than 1 h, or reuses nonces (AD-15; RTO ≤ 1 h) | 2 | 2 | 4 | CI restore test with RustFS (2.3), timed; runbook drill on the home server; replay-window advance (4.5) | Epic 2 |
| R-10 | TECH | Node energy budget missed: sleep current or wake time too high (NFR4) | 2 | 2 | 4 | Manual current measurement checklist (4.1); wake-time counters logged in dev builds | Epic 4 |
| R-11 | TECH | Node BLE plus ESP-NOW coexistence is unvalidated (device-hardware open item) | 2 | 2 | 4 | Manual bench check in 4.2 | Epic 4 |
| R-12 | BUS | Accessibility regressions (NFR14) | 2 | 2 | 4 | Contrast check in the tokens CI (1.3); axe in Playwright; snapshot tests at the largest text size | All UI epics |
| R-13 | OPS | Certificate renewal fails, so the Hub (which checks dates) can't connect (AD-13, H-2) | 1 | 3 | 3 | cert-manager renewal alert in docs; k3d chart test; Hub test with an expired certificate | Epics 2–3 |
| R-14 | SEC | Secrets committed to Git (AD-15) | 1 | 3 | 3 | Chart test that no Secret has data (2.2); secret scanning in CI | Epic 1 |
| R-15 | BUS | Alert flapping or false alarms (FR11; counter-metric SM-C1) | 1 | 2 | 2 | Exactly-3 and 2-of-3 TestCluster cases (6.1) | Epic 6 |
| R-16 | DATA | Calibration applied to the wrong Readings (FR9: "history keeps its Calibration") | 1 | 2 | 2 | Calibration-in-force and recalibration tests (5.1) | Epic 5 |

No risk scores 9, so nothing blocks the gate. Seven risks score ≥ 6 and have mitigations planned above.

### NFR planning
| Category | Threshold | Status | Planned evidence |
|---|---|---|---|
| Security | OIDC; per-Site authorization; HMAC Hub auth; sealed frames; TLS with public certificates; no secrets in Git | Defined | Authorization-matrix tests, crypto vectors, negative tests, chart tests, secret scan |
| Reliability and durability | Zero loss on restart; acknowledge after commit; 24 h buffer; RTO ≤ 1 h | Defined | Crash and restart integration tests, simulator outage end-to-end, timed restore test |
| Performance | Acknowledgement window 300 ms (firmware); notification ≤ 1 min after due; no API latency SLO | Partly defined; the API SLO is **none by decision** | Fake-clock latency assertions, Notifier `dueAt`/`sentAt` metric; no load tests |
| Availability | Best effort | Defined (no SLA) | None |
| Energy and hardware | Season on solar, ≥ 14 days with no sun, sleep ≤ ~100 µA, reach to the farthest Lot | Defined | Manual measurement checklists |
| Accessibility | WCAG AA contrast, scaling, screen readers | Defined | Contrast CI, axe, snapshot tests at the largest text size |
| Maintainability | Test-first; contract compatibility; event replay | Defined | CI gates (AD-24) |
| Scalability | No envelope (per-test data only) | **UNKNOWN by decision**, accepted | None |

### Summary
The highest risks sit on **the device data path** (R-01, R-02), **timing correctness of notifications and silence** (R-03, R-04), **security of authorization and custom crypto** (R-05, R-06) and **firmware without on-device CI** (R-07). All are mitigated by the four enablers above: `TimeProvider` everywhere (TC-1), a Device simulator (TC-2), a HAL-trait crate (TC-3), and `dueAt`/`sentAt` observability (TC-8).

### Change (Simon, 2026-09-27)
- The S3 stand-in for tests is **RustFS** (Apache-2.0, S3-compatible; latest stable 1.0.0, previews 1.0.1-preview.x), replacing MinIO. It is used in the k3d backup and restore CI test (Story 2.3), and optionally as a local S3 in the Aspire AppHost.
- The enablers TC-1, TC-2, TC-3 and TC-8 are now in the spine (AD-6, AD-7, AD-24, source tree) and in Stories 1.2, 3.1, 3.2 and 6.4 (Simon approved).
