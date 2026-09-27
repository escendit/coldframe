---
workflowStatus: 'complete'
totalSteps: 5
stepsCompleted: ['step-01-detect-mode', 'step-02-load-context', 'step-03-risk-and-testability', 'step-04-coverage-plan', 'step-05-generate-output']
lastStep: 'step-05-generate-output'
nextStep: ''
lastSaved: '2026-09-27'
workflowType: 'testarch-test-design'
inputDocuments:
  - _bmad-output/test-artifacts/test-design-progress-system.md
  - _bmad-output/specs/spec-coldframe/SPEC.md
  - _bmad-output/planning-artifacts/prds/prd-coldframe-2026-09-25/prd.md
  - _bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md
  - _bmad-output/planning-artifacts/epics.md
  - _bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/EXPERIENCE.md
  - docs/spikes/hub-radio-coexistence.md
---

# Test Design for Architecture: Coldframe V1 (System Level)

**Purpose:** Architectural concerns, testability gaps, and NFR requirements for review by Architecture/Dev teams. Serves as a contract between QA and Engineering on what must be addressed before test development begins.

**Date:** 2026-09-27
**Author:** Simon (solo project: architect, developer and QA)
**Status:** Architecture Review Pending
**Project:** coldframe
**PRD Reference:** `_bmad-output/planning-artifacts/prds/prd-coldframe-2026-09-25/prd.md` (primary requirements source: `_bmad-output/specs/spec-coldframe/SPEC.md`)
**ADR Reference:** `_bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md` (AD-1 to AD-25)

---

## Executive Summary

**Scope:** Whole Coldframe V1 system: Node and Hub firmware (Rust no_std, ESP32-S3), Server (.NET 10 Orleans, ingestion, Alert evaluation, notifications, REST, SignalR), SvelteKit BFF, KMP core with SwiftUI/Compose shells, Keycloak + Phase Two identity, single-node RKE2 reference deployment. 10 epics, 53 stories.

**Business Context** (from PRD):

- **Revenue/Impact:** N/A (personal, open-source, self-hosted). Success metrics: SM-1 (a summer with no plant lost), SM-2 (Alert delivered in the window before 0 %), SM-3 (silent Device reported within its Silence Window), SM-4 (Node lasts the season), SM-5 (another person reproduces the setup).
- **Problem:** Tell the gardener which Lot needs water while there is still time, and never let silence read as "all fine".
- **GA Launch:** V1 release; no date set. High-risk mitigations (R-01 to R-07) must pass before it.

**Architecture** (from ARCHITECTURE-SPINE):

- **Key Decision 1:** Orleans grains as sole writers, event-sourced into one PostgreSQL journal with outbox and CQRS projections (AD-1, AD-2, AD-21).
- **Key Decision 2:** Devices seal frames under a hardware-rooted key hierarchy; the Hub is an untrusted relay; acknowledge only after commit (AD-9, AD-12, AD-17, AD-25).
- **Key Decision 3:** Deadlines are persisted state, time injected via `TimeProvider`; per-Site authorization from the identity projection (AD-3, AD-4, AD-6, AD-7).

**Expected Scale** (from ADR): No scale envelope by decision (one household, "more Lots and Nodes later"). Readings every 15 min per Node; Hub heartbeat every 30–60 s. No volume data sets; test data is per test case only.

**Risk Summary:**

- **Total risks**: 16
- **High-priority (≥6)**: 7 risks requiring immediate mitigation (none scores 9)
- **Test effort**: 45 scenarios (17 P0, 23 P1, 5 P2); ~100–165 h solo, built test-first inside each story (~2.5–4 weeks full-time equivalent, spread across Epics 1–10)

---

## Quick Guide

### 🚨 BLOCKERS - Team Must Decide (Can't Proceed Without)

**Pre-Implementation Critical Path** - All four enablers are decided and now in the spine (Simon approved, 2026-09-27); they must be *built* before the dependent tests can be written:

1. **TC-1: Injectable time** - Every grain, projector and the Notifier read time only from `TimeProvider`; test hosts use `FakeTimeProvider`; build fails on `DateTime.Now/UtcNow` (AD-6; Story 1.2). Blocks R-03, R-04 suites. (owner: Simon)
2. **TC-2: Device simulator** - C# test library in `tests/cs` on `packages/proto` + `packages/crypto-spec`: enrol, seal frames, sign heartbeats, verify downlinks; cross-checked against Rust vectors (AD-24; Story 3.1). Blocks R-01, R-02 integration and all Device-path E2E. (owner: Simon)
3. **TC-3: HAL-trait crate** - `packages/rs/hal` (radio, eFuse, HMAC, flash, ADC, RTC, GPIO) with mocks; all firmware logic depends only on it (AD-24; Story 3.2). Blocks host-side firmware tests (R-01, R-02, R-07). (owner: Simon)
4. **TC-8: Notification latency observable** - Notifier records `dueAt` and `sentAt` as a structured log field and OpenTelemetry metric (AD-7; Story 6.4). Blocks the ≤ 1 min assertion (ASR-7). (owner: Simon)

**What we need from team:** Build these 4 items in their stories before the dependent tests; nothing further to decide.

---

### ⚠️ HIGH PRIORITY - Team Should Validate (We Provide Recommendation, You Approve)

1. **R-06: Custom crypto review** - Have `packages/crypto-spec` (AD-12 hierarchy, AD-25 session) reviewed by a second person or externally before V1, and record it; Simon to choose the reviewer (before V1 release).
2. **R-07: Firmware without on-device CI** - Treat the versioned manual checklist (T-42) as a release gate for every firmware release; Simon approves (Epics 3–4, 10).
3. **R-09 / ASR-8: Restore within RTO ≤ 1 h** - Time the CI restore with RustFS (Story 2.3) and drill the runbook on the home server; restore must advance replay windows (AD-15, AD-17); Simon approves (Epic 2).
4. **TC-7: No seeding endpoint** - Per-test builders acting through grain APIs with unique Site IDs replace any seeding API; no production seeding endpoint; Simon approves (all epics).

**What we need from team:** Review recommendations and approve (or suggest changes).

---

### 📋 INFO ONLY - Solutions Provided (Review, No Decisions Needed)

1. **Test strategy**: Unit / Contract / Grain / Integration / UI component / E2E / Infra / Manual. Business rules at Grain or Unit only; Integration for wiring, persistence, authorization; one E2E happy path plus key failure per journey (duplicate-coverage guard).
2. **Tooling**: xUnit, Orleans TestCluster, `Aspire.Hosting.Testing`, `cargo test` (host), `kotlin.test`, SwiftUI/Compose snapshots, Playwright + axe, helm-unittest, kubeconform, k3d, Mailpit, RustFS (S3 stand-in; Apache-2.0, 1.0.0). No Pact (contracts via AD-10/AD-24 compatibility checks and shared vectors).
3. **Tiered CI/CD**: PR runs everything functional (target < 15 min; move E2E to nightly if exceeded); nightly k3d smoke + backup/restore; per firmware/V1 release manual checklist, real push, crypto review; once before V1 reproduction by another person.
4. **Coverage**: 45 scenarios (T-01 to T-45) prioritized P0–P2, risk-linked; every risk has at least one suitable-level scenario. See `test-design-qa.md`.
5. **Quality gates**: P0 100 %, P1 ≥ 95 %; R-01 to R-07 mitigated; ≥ 80 % line coverage on Server domain and `packages/rs/*`; T-42 signed off. Details in `test-design-qa.md`.

**What we need from team:** Just review and acknowledge.

---

## For Architects and Devs - Open Topics 👷

### Risk Assessment

**Total risks identified**: 16 (7 high-priority score ≥6, 7 medium, 2 low)

#### High-Priority Risks (Score ≥6) - IMMEDIATE ATTENTION

| Risk ID  | Category     | Description | Probability | Impact | Score | Mitigation | Owner | Timeline |
| -------- | ------------ | ----------- | ----------- | ------ | ----- | ---------- | ----- | -------- |
| **R-01** | **SEC/DATA** | Nonce reuse or counter reset under the fixed eFuse-derived key could allow forgery or permanent rejection (AD-17) | 2 | 3 | **6** | Host-side property tests of counter reservation across reboots/power loss; Server replay-window tests; restore-advance test | Simon | Epic 4 |
| **R-02** | **DATA** | Reading loss if acknowledged before commit or if a forged ack is accepted (AD-9; N-1) | 2 | 3 | **6** | Crash between insert and response; delete only on authenticated downlink; simulator 1 h Hub-outage test | Simon | Epic 4 |
| **R-03** | **BUS** | Missed, late or duplicate notifications around window opening, restarts, DST and held-then-closed Alerts (FR16; SM-2) | 2 | 3 | **6** | Fake-clock TestCluster suite (restart across 07:00, DST, summary dedup); ≤ 1 min latency assertion (TC-8) | Simon | Epic 6 |
| **R-04** | **BUS** | Silence reads as "all fine": missed Silent Alert, or Hub suppression hiding a dead Node (FR13) | 2 | 3 | **6** | Fake-clock tests of Node/Hub windows, suppression lifting, restart without false silence; UJ-3 E2E | Simon | Epic 7 |
| **R-05** | **SEC** | Cross-Site access or stale Roles after a change (NFR6; FR7) | 2 | 3 | **6** | Generated authorization matrix on every endpoint; multi-Site tests; revocation on next request | Simon | Epics 1, 9 |
| **R-06** | **SEC** | Custom crypto (AD-12 hierarchy, AD-25 setup session) implemented inconsistently or weakly | 2 | 3 | **6** | Shared vectors in 3 languages; negative tests (wrong setup code, tamper, replay); second-person/external crypto-spec review before V1 | Simon | Epic 3 |
| **R-07** | **TECH** | Firmware regressions reach hardware undetected; no on-device tests (NFR16) | 3 | 2 | **6** | HAL-trait crate (TC-3); high host-side coverage; versioned manual checklist before each firmware release | Simon | Epics 3–4, 10 |

#### Medium-Priority Risks (Score 3-5)

| Risk ID | Category | Description | Probability | Impact | Score | Mitigation | Owner |
| ------- | -------- | ----------- | ----------- | ------ | ----- | ---------- | ----- |
| R-08 | TECH | Pre-release dependencies change (Orleans NATS alpha, esp-radio beta, Escendit RCs) | 2 | 2 | 4 | Pinned versions + CI; streams behind a seam (AD-5); Renovate PRs gated by full suite | Simon |
| R-09 | OPS | Restore exceeds 1 h, or reuses nonces (AD-15) | 2 | 2 | 4 | Timed CI restore with RustFS (2.3); runbook drill; replay-window advance (4.5) | Simon |
| R-10 | TECH | Node energy budget missed: sleep current or wake time too high (NFR4) | 2 | 2 | 4 | Manual current measurement (4.1); wake-time counters in dev builds | Simon |
| R-11 | TECH | Node BLE + ESP-NOW coexistence unvalidated | 2 | 2 | 4 | Manual bench check (4.2) | Simon |
| R-12 | BUS | Accessibility regressions (NFR14) | 2 | 2 | 4 | Contrast check in tokens CI (1.3); axe in Playwright; largest-text snapshots | Simon |
| R-13 | OPS | Certificate renewal fails; date-checking Hub can't connect (AD-13, H-2) | 1 | 3 | 3 | cert-manager renewal alert in docs; k3d chart test; expired-cert Hub test | Simon |
| R-14 | SEC | Secrets committed to Git (AD-15) | 1 | 3 | 3 | Chart test that no Secret has data (2.2); secret scanning in CI | Simon |

#### Low-Priority Risks (Score 1-2)

| Risk ID | Category | Description | Probability | Impact | Score | Action |
| ------- | -------- | ----------- | ----------- | ------ | ----- | ------ |
| R-15 | BUS | Alert flapping or false alarms (FR11; SM-C1) | 1 | 2 | 2 | Document; exactly-3 and 2-of-3 TestCluster cases (6.1) |
| R-16 | DATA | Calibration applied to the wrong Readings (FR9) | 1 | 2 | 2 | Document; calibration-in-force and recalibration tests (5.1) |

#### Risk Category Legend

- **TECH**: Technical/Architecture (flaws, integration, scalability)
- **SEC**: Security (access controls, auth, data exposure)
- **PERF**: Performance (SLA violations, degradation, resource limits)
- **DATA**: Data Integrity (loss, corruption, inconsistency)
- **BUS**: Business Impact (UX harm, logic errors, revenue)
- **OPS**: Operations (deployment, config, monitoring)

---

### NFR Testability Requirements

**Purpose:** Capture what architecture must provide so NFR validation can be automated later. This is planning guidance, not final evidence assessment.

| NFR Category | Threshold / Requirement | Current Design Support | Gap / Decision Needed | Planned Evidence |
| --- | --- | --- | --- | --- |
| Security | OIDC; per-Site authorization; HMAC Hub auth (±5 min); sealed frames; TLS with public certs on every IP hop; no secrets in Git | Supported (AD-3, AD-4, AD-12, AD-13, AD-15, AD-25) | R-06 crypto-spec review pending | Authorization-matrix and vector reports, negative tests, chart tests, secret-scan report, review record |
| Performance | Notification ≤ 1 min after due; ack window 300 ms (firmware); **no API latency SLO (decision)** | Supported (AD-7 `dueAt`/`sentAt`, AD-17) | None; no load tests by decision | Fake-clock grain report, Notifier metric, manual checklist record |
| Reliability | Zero loss on restart (NFR3); ack after commit; Node buffer ≥ 24 h; RTO ≤ 1 h, RPO = WAL-archive interval; **availability best effort** | Supported (AD-6, AD-9, AD-15, AD-17, AD-21) | None | Crash/restart integration tests, simulator outage E2E, timed restore log |
| Maintainability | Test-first; contract compatibility; event replay; ≥ 80 % domain/firmware-logic coverage | Supported (AD-10, AD-21, AD-24) | None | CI coverage report, compatibility and replay logs |
| Energy / hardware | Full season on solar; ≥ 14 days no sun; sleep ≤ ~100 µA; reach to farthest Lot | Partial (manual only, NFR16) | Accepted: no on-device CI | Signed-off manual checklist per firmware release |
| Accessibility | Text 4.5:1, UI 3:1; Dynamic Type / font scale / zoom; screen readers | Supported (AD-14 fixture rendering) | None | Contrast CI, axe report, snapshot artifacts |

**ADR Quality Readiness Checklist** (8 categories, 29 criteria) against the spine. Totals: 18 ✅ Covered, 6 ⚠️ Gap, 5 N/A.

| # | Criterion | Status | Evidence / Gap |
| --- | --- | --- | --- |
| 1.1 | Isolation | ✅ | Notifier seam (AD-7), stream seam (AD-5), `TimeProvider` (AD-6), HAL traits and Device simulator (AD-24); Mailpit and RustFS stand-ins |
| 1.2 | Headless interaction | ✅ | All logic behind REST and grain APIs; clients only render (AD-1, AD-14) |
| 1.3 | State control | ⚠️ | No seeding API by decision; per-test builders through grain APIs (TC-7) |
| 1.4 | Sample requests | ✅ | Contract-first `packages/openapi`, golden Hub JSON fixtures, crypto test vectors (AD-10, AD-12) |
| 2.1 | Segregation | ✅ | Per-Site tenancy (AD-4); unique Site IDs per test; no staging, no shared prod metrics (AD-15) |
| 2.2 | Generation | ✅ | Synthetic per-test data only; no production data |
| 2.3 | Teardown | ✅ | Isolation by unique Site ID and ephemeral Aspire host per run; tombstones, no hard delete (AD-20) |
| 3.1 | Statelessness | ✅ | Server pods stateless, state in CNPG (AD-15); restart catch-up from persisted deadlines (AD-6) |
| 3.2 | Bottlenecks | N/A | No scale envelope and no load tests by decision |
| 3.3 | SLA definitions | N/A | Best-effort availability, single node (decision) |
| 3.4 | Circuit breakers | ⚠️ | Device path degrades safely (per-frame `retry`, 24 h buffer, streams optional: AD-9, AD-17, AD-21); no fail-fast/timeout rule stated for Keycloak Admin or APNs/FCM calls. Clarification only; no new risk |
| 4.1 | RTO/RPO | ✅ | RTO ≤ 1 h (decision); RPO = WAL-archive interval (AD-15) |
| 4.2 | Failover | N/A | Single node, accepted; recovery path is restore (AD-15) |
| 4.3 | Backups | ✅ | Barman Cloud + WAL to off-node S3; restore documented and tested; replay-window advance (AD-15, AD-17). Immutability left to the adopter's S3 target |
| 5.1 | AuthN/AuthZ | ✅ | Keycloak OIDC; Roles per Site from projection; generated matrix; Hub HMAC (AD-3, AD-4, AD-12, AD-24) |
| 5.2 | Encryption | ✅ | TLS on every IP hop (AD-13); sealed ESP-NOW and BLE (AD-12, AD-25); `K_dev` encrypted at rest (AD-12). Full-DB at-rest encryption not specified |
| 5.3 | Secrets | ✅ | Out-of-band Kubernetes Secrets; charts never template values (AD-15); secret scan (T-45) |
| 5.4 | Input validation | ⚠️ | Device inputs authenticated and validated (AD-9, AD-11, AD-19); RFC 9457 errors; no explicit injection/sanitisation convention for REST |
| 6.1 | Tracing | ✅ | OpenTelemetry via Escendit service defaults (AD-15, conventions) |
| 6.2 | Logs | ⚠️ | Structured OTel logs, no secrets or payloads; dynamic log-level toggle not specified (env-var config) |
| 6.3 | Metrics | ✅ | OTel emitted, incl. `dueAt`/`sentAt` (AD-7); production backend deferred (spine Deferred) |
| 6.4 | Config | ✅ | Environment variables only; mobile Server URL build-time by design (AD-23) |
| 7.1 | Latency | N/A | No API latency SLO by decision; the timing thresholds that exist (≤ 1 min notification, 300 ms ack) are covered (AD-7, AD-17) |
| 7.2 | Throttling | ⚠️ | No rate limiting stated; exposure limited to LAN, no inbound internet (AD-13); Hub nonce replay rejected (AD-12) |
| 7.3 | Perceived performance | ✅ | Skeleton tiles and stale mode (EXPERIENCE.md; AD-14 transport staleness) |
| 7.4 | Degradation | ✅ | RFC 9457 Problem Details; "Can't reach your Server" stale mode (AD-14, EXPERIENCE.md) |
| 8.1 | Zero downtime | N/A | Single silo, stop-then-start upgrades, accepted (AD-15) |
| 8.2 | Backward compatibility | ✅ | Forward-only migrations as a Job before start (AD-22); upcasters (AD-21); previous wire major accepted (AD-10, AD-23) |
| 8.3 | Rollback | ⚠️ | No automated rollback; forward-only migrations; recovery via restore within RTO (AD-15, AD-22) |

**Unknown thresholds:** None open. API latency SLO = none, availability = best effort, scalability = UNKNOWN by decision (accepted assumption, no seed/volume data). RTO ≤ 1 h and notification ≤ 1 min were supplied by Simon (2026-09-27).

**Assessment boundary:** Final PASS/CONCERNS/FAIL status belongs in `nfr-assess` after implementation evidence exists.

---

### Testability Concerns and Architectural Gaps

**🚨 ACTIONABLE CONCERNS - Architecture Team Must Address.**

#### 1. Blockers to Fast Feedback (WHAT WE NEED FROM ARCHITECTURE)

| Concern | Impact | What Architecture Must Provide | Owner | Timeline |
| --- | --- | --- | --- | --- |
| **TC-1 Time not injectable** | Windows, Reminders, Silence, Pause, DST untestable without waiting | `TimeProvider` everywhere; `FakeTimeProvider` in TestCluster/Aspire (now AD-6) | Simon | Story 1.2 |
| **TC-2 No Device simulator** | No ingestion, heartbeat, silence or journey E2E without hardware | Device simulator library in `tests/cs` (now AD-24) | Simon | Story 3.1 |
| **TC-3 Firmware host-side only** | Firmware logic not unit-testable | HAL-trait crate `packages/rs/hal` with mocks (now AD-24, source tree) | Simon | Story 3.2 |
| **TC-8 Latency not observable** | ≤ 1 min cannot be asserted | Notifier `dueAt`/`sentAt` log field + OTel metric (now AD-7) | Simon | Story 6.4 |

#### 2. Architectural Improvements Needed (WHAT SHOULD BE CHANGED)

1. **TC-4 External provider stand-ins**
   - **Current problem**: APNs, FCM, SMTP, DNS-01 and S3 have no test doubles.
   - **Required change**: Notifier test double; Mailpit in the AppHost; RustFS for S3; self-signed/staging issuer in k3d. DNS-01 checked manually only.
   - **Impact if not fixed**: Delivery, invitation, backup and TLS paths untested in CI.
   - **Owner**: Simon
   - **Timeline**: Stories 2.3, 6.5, 9.1
2. **TC-5 Asynchronous identity pipeline**
   - **Current problem**: Keycloak → Temporal → Orleans is eventually consistent.
   - **Required change**: Helpers that wait on a journal position or projector checkpoint (bounded polling, timeout); no sleeps.
   - **Impact if not fixed**: Hard waits, flaky integration tests.
   - **Owner**: Simon
   - **Timeline**: Stories 1.7, 9.1, 9.2
3. **TC-6 BLE not automatable end to end**
   - **Current problem**: CI cannot reach a real Device over BLE.
   - **Required change**: Kotlin and Rust unit tests with mocked transports plus a shared session vector (AD-25); desktop BLE client (3.4) for the bench checklist.
   - **Impact if not fixed**: Setup regressions found only on hardware.
   - **Owner**: Simon
   - **Timeline**: Stories 3.4, 3.6, 4.2, 4.3
4. **TC-7 No test-data seeding path**
   - **Current problem**: No seeding API, and none wanted beyond per-test data.
   - **Required change**: Per-test builders in `tests/cs` via grain APIs or journal fixtures; unique Site IDs; no production seeding endpoint.
   - **Impact if not fixed**: Slow, coupled test setup.
   - **Owner**: Simon
   - **Timeline**: From Story 1.2 onward

---

### Testability Assessment Summary

**📊 CURRENT STATE - FYI.**

#### What Works Well

- ✅ Event-sourced journal with global position: deterministic replay; event-replay CI test (AD-21, AD-24)
- ✅ Deterministic IDs (UUIDv5 Sensors/Alerts) and idempotency keys: retries asserted exactly (AD-7, AD-19)
- ✅ Contract-first packages with compatibility checks and shared vectors in Rust, C#, Kotlin (AD-10, AD-12, AD-24)
- ✅ Server-computed `LotStatus`: UI tests are fixture-row snapshots (AD-14)
- ✅ Generated per-Site authorization matrix (AD-4, AD-24)
- ✅ Aspire AppHost as integration host with real PostgreSQL, NATS, Temporal, Keycloak (AD-15, AD-24)
- ✅ Persisted `due-at` deadlines: kill-silo-then-catch-up is testable (AD-6)

#### Accepted Trade-offs (No Action Required)

For Coldframe V1, the following trade-offs are acceptable:

- **No on-device CI (NFR16)** - Hardware verified by versioned manual checklist (T-42); R-07 mitigated host-side.
- **Single node, best-effort availability** - No failover, blue/green or SLA; recovery is restore within 1 h.
- **No API latency SLO, no load or scale tests** - One household on a LAN; performance P3 at most.
- **DNS-01 against a real provider checked manually** - CI uses a self-signed/staging issuer.

These are maintained as-is for V1 and revisited only if Coldframe scales beyond one household.

---

### Risk Mitigation Plans (High-Priority Risks ≥6)

**Purpose**: Mitigation strategies for all 7 high-priority risks. These must be addressed before the V1 release. Owner for all: Simon. Status for all: Planned.

#### R-01: Nonce reuse / counter reset (Score: 6) - HIGH

**Mitigation Strategy:**

1. Counter reservation in flash blocks: jump to stored ceiling on boot, raise before sealing (AD-17).
2. Fresh seal per transmission, including resends; 64-entry Server replay window.
3. Advance every replay window by a safety margin after restore (AD-15).

**Owner:** Simon
**Timeline:** Epic 4 (Stories 4.4, 4.5), restore in 2.3
**Status:** Planned
**Verification:** T-01, T-02, T-03 pass (P0).

#### R-02: Reading loss (Score: 6) - HIGH

**Mitigation Strategy:**

1. Acknowledge only after PostgreSQL commit; per-frame status; dedupe by `(device, sensor, reading_seq)` (AD-9).
2. Node deletes only on an authenticated downlink; Hub never synthesizes acks; ≥ 24 h buffer.

**Owner:** Simon
**Timeline:** Epic 4
**Status:** Planned
**Verification:** T-04 to T-08 pass; T-06 simulator outage stores every Reading exactly once.

#### R-03: Missed, late or duplicate notifications (Score: 6) - HIGH

**Mitigation Strategy:**

1. User grain owns windowing, summaries and Reminders from persisted deadlines (AD-6, AD-7).
2. Time via `TimeProvider` (TC-1); Notifier records `dueAt`/`sentAt` (TC-8).

**Owner:** Simon
**Timeline:** Epic 6
**Status:** Planned
**Verification:** T-16 to T-21 and T-25 pass; `sentAt − dueAt ≤ 60 s`.

#### R-04: Silence read as "all fine" (Score: 6) - HIGH

**Mitigation Strategy:**

1. Silence measured from `max(last report, last Server start, last resume)` (AD-6).
2. Hub-silence suppression with `unknownCause`; restart of Node windows on Hub recovery (AD-7, AD-14).

**Owner:** Simon
**Timeline:** Epic 7
**Status:** Planned
**Verification:** T-22, T-23, T-24, T-26, T-28, T-29 pass.

#### R-05: Cross-Site access / stale Roles (Score: 6) - HIGH

**Mitigation Strategy:**

1. Roles read per request from the identity projection, never token claims; one shared policy (AD-4).
2. Site grain serializes Role changes and keeps at least one Owner (AD-3).

**Owner:** Simon
**Timeline:** Epics 1, 9
**Status:** Planned
**Verification:** T-13 (generated matrix) gates every merge; T-14, T-15, T-27 pass.

#### R-06: Weak or inconsistent custom crypto (Score: 6) - HIGH

**Mitigation Strategy:**

1. Single source in `packages/crypto-spec`; shared vectors in Rust, C#, Kotlin (AD-12, AD-25).
2. Negative tests: wrong setup code, tampered frame, replay.
3. Second-person or external review of crypto-spec, recorded before V1.

**Owner:** Simon
**Timeline:** Epic 3; review before V1
**Status:** Planned
**Verification:** T-09 to T-12, T-41 pass; review record exists. Residual: custom-crypto risk remains until the review is done.

#### R-07: Firmware regressions undetected (Score: 6) - HIGH

**Mitigation Strategy:**

1. All firmware logic behind `packages/rs/hal` with mocks (TC-3).
2. High host-side coverage of protocol and state machines (≥ 80 % on `packages/rs/*`).
3. Versioned manual checklist run before each firmware release.

**Owner:** Simon
**Timeline:** Epics 3–4, 10
**Status:** Planned
**Verification:** T-12 passes; T-42 signed off per release. Residual: radio-stack faults found only on the bench.

---

### Assumptions and Dependencies

#### Assumptions

1. Single-node RKE2, single silo, stop-then-start upgrades; availability is best effort (AD-15).
2. No scale envelope: one household; test data is synthetic and per test case only.
3. The 300 ms acknowledgement window from the radio spike holds and is re-measured against the real Server (AD-17).
4. RustFS is S3-compatible enough to stand in for the adopter's off-node S3 in backup/restore tests.
5. `keycloak-temporal-extensions` stays compatible with the pinned Phase Two image (AD-15).

#### Dependencies

1. `TimeProvider` convention and fake-clock hosts (TC-1) - Required by Story 1.2
2. RustFS in k3d backup/restore CI (TC-4) - Required by Story 2.3
3. Device simulator (TC-2) - Required by Story 3.1, before Epic 4 integration tests
4. HAL-trait crate (TC-3) - Required by Story 3.2
5. Notifier `dueAt`/`sentAt` (TC-8) - Required by Story 6.4
6. Mailpit in the Aspire AppHost (TC-4) - Required by Story 9.1
7. Crypto-spec reviewer (R-06) - Required before V1 release

#### Risks to Plan

- **Risk**: E2E journeys push the PR run past 15 min.
  - **Impact**: Slower feedback on every merge.
  - **Contingency**: Move E2E to nightly (execution strategy).
- **Risk**: No second person available for the crypto-spec review.
  - **Impact**: R-06 cannot be closed; V1 gate not met.
  - **Contingency**: External review before V1 (as already allowed by R-06).
- **Risk**: Pre-release dependencies change (R-08).
  - **Impact**: Harness or tests break on upgrade.
  - **Contingency**: Pinned versions; Renovate PRs gated by the full suite.

---

**End of Architecture Document.**

**Next Steps for Architecture Team:**

1. Review Quick Guide (🚨/⚠️/📋) and prioritize blockers
2. Assign owners and timelines for high-priority risks (≥6)
3. Validate assumptions and dependencies
4. Provide feedback to QA on testability gaps

**Next Steps for QA Team:**

1. Wait for pre-implementation blockers to be resolved
2. Refer to companion QA doc (test-design-qa.md) for test scenarios
3. Begin test infrastructure setup (factories, fixtures, environments)
