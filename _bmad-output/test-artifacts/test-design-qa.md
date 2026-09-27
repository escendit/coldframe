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
---

# Test Design for QA: Coldframe (System Level)

**Purpose:** Test execution recipe for QA. Defines what to test, how to test it, and what testing needs from the architecture and the build.

**Date:** 2026-09-27
**Author:** Simon (solo; with BMad TEA)
**Status:** Draft
**Project:** coldframe

**Related:** See Architecture doc (test-design-architecture.md) for testability concerns and architectural blockers. Source checkpoint: `test-design-progress-system.md`.

---

## Executive Summary

**Scope:** Whole system: .NET 10 Orleans Server (ASP.NET Core, SignalR, PostgreSQL journal, NATS, Temporal, Keycloak/Phase Two), SvelteKit backend-for-frontend and web app, Kotlin Multiplatform core with SwiftUI and Compose shells, Rust no_std firmware (Hub and Node, ESP32-S3), Helm/k3d deployment. Epics 1–10, 53 stories.

**Risk Summary:**

- Total Risks: 16 (7 high-priority score ≥6, 7 medium score 3–4, 2 low score 2); no score-9 risk, nothing blocks the gate
- Critical Categories: SEC (R-01, R-05, R-06), DATA (R-01, R-02), BUS (R-03, R-04), TECH (R-07)

**Coverage Summary:**

- P0 tests: ~17 scenarios (device data path, crypto, authorization, alert/notification/silence timing)
- P1 tests: ~23 scenarios (journeys, contracts, infra, UI, manual firmware checklist)
- P2 tests: ~5 scenarios (secondary rules, real push, reproduction)
- P3 tests: 0 (no performance or benchmark work, by decision)
- **Total**: ~45 scenarios, T-01..T-45 (~100–165 h, ~2.5–4 weeks full-time equivalent, spread across Epics 1–10 test-first)

---

## Not in Scope

| Item | Reasoning | Mitigation |
| --- | --- | --- |
| **Performance / load tests (API, overview)** | No latency SLO, by decision (Simon, 2026-09-27) | Only the notification ≤ 1 min threshold is tested (T-21) |
| **Availability SLA tests** | Best effort, single node | Restart durability covered (T-04, T-19, T-22) |
| **Scalability / volume data sets** | No scale envelope; per-test synthetic data only | Accepted assumption; revisit if scale needs appear |
| **On-device firmware CI** | NFR16 / AD-24: firmware tested host-side only | HAL-trait mocks (TC-3) plus manual checklist T-42 per release |
| **DNS-01 against a real provider in CI** | Needs real DNS provider API and public domain | Staging/self-signed issuer in k3d (T-38); real renewal checked manually |
| **Pact consumer-driven contracts** | No Pact artifacts; contracts are schema-first | Shared vectors and schema compatibility (T-09, T-36) |
| **Automated BLE end to end** | Simulators and CI can't reach real Devices over BLE | Mocked transports (T-10, T-41); desktop BLE client on the bench (T-42) |

**Note:** Exclusions accepted by Simon (sole QA, Dev and PM).

---

## Dependencies & Test Blockers

**CRITICAL:** These must exist before the dependent tests can be written.

### Backend/Architecture Dependencies (Pre-Implementation)

**Source:** Architecture doc and checkpoint TC-1..TC-8. TC-1, TC-2, TC-3 and TC-8 are already in the spine and stories.

| # | Enabler | Where | Unblocks |
| --- | --- | --- | --- |
| TC-1 | Server time only from `TimeProvider`; `FakeTimeProvider` in TestCluster and Aspire hosts; build fails on `DateTime.Now/UtcNow` | AD-6, Story 1.2 | T-16..T-24, T-28, all fake-clock E2E |
| TC-2 | `DeviceSimulator` (C#): enrol, seal frames, sign heartbeats, verify downlinks; cross-checked against Rust vectors | AD-24, Story 3.1 | T-02..T-08, T-11, T-24..T-26 |
| TC-3 | HAL-trait crate `packages/rs/hal` with mocks; versioned manual checklists in `docs/checklists/` | AD-24, Story 3.2 | T-01, T-05, T-08, T-12, T-40, T-42 |
| TC-4 | Stand-ins: Notifier test double, Mailpit (Aspire), RustFS (S3), self-signed/staging issuer in k3d | AD-7, AD-13, AD-15; Stories 2.3, 6.5, 9.1 | T-21, T-27, T-38, T-39 |
| TC-5 | Wait helpers on journal position / projector checkpoint (bounded polling, no sleeps) | AD-3; Stories 1.7, 9.1, 9.2 | T-14, T-15, T-27 |
| TC-6 | BLE protocol and state machines testable with mocked transport; shared session vector | AD-25; Stories 3.4, 3.6, 4.2, 4.3 | T-10, T-41 |
| TC-7 | Per-test builders in `tests/cs` via grain APIs or journal fixtures; unique Site IDs; no seeding endpoint | Story 1.2 onward | All Grain/Integration/E2E |
| TC-8 | Notifier emits `dueAt`/`sentAt` as structured log and OpenTelemetry metric | AD-7, Story 6.4 | T-21 |

### QA Infrastructure Setup (Pre-Implementation)

1. **Test Data Factories** - Simon
   - Builders for Site, Lot, Device (Hub/Node), Reading, Role, Calibration, Threshold; synthetic, randomized IDs
   - Cleanup by unique Site ID per test; safe for parallel runs; no shared seed data (TC-7)

2. **Test Environments** - Simon
   - Local: Aspire AppHost (`Aspire.Hosting.Testing`) with real PostgreSQL, NATS, Temporal, Keycloak/Phase Two, Mailpit, optional RustFS
   - CI/CD: same Aspire host per run for Integration and E2E; k3d with RustFS for infra (nightly); macOS runner for Swift snapshots
   - Staging: N/A — no staging environment; the home RKE2 server is production. Restore drill runs there manually (R-09)

**Example factory pattern (web E2E layer, playwright-utils; paths illustrative):**

```typescript
import { test } from '@seontechnologies/playwright-utils/api-request/fixtures';
import { expect } from '@playwright/test';
import { faker } from '@faker-js/faker';

test('@P1 @E2E T-26 Site paused until a date reports paused', async ({ apiRequest }) => {
  const site = { name: `site-${faker.string.uuid()}` };

  const { status: created, body } = await apiRequest({
    method: 'POST',
    path: '/api/sites',
    body: site,
  });
  expect(created).toBe(201);

  const { status: paused } = await apiRequest({
    method: 'POST',
    path: `/api/sites/${body.id}/pause`,
    body: { until: '2026-10-15' },
  });
  expect(paused).toBe(200);

  const { body: after } = await apiRequest({
    method: 'GET',
    path: `/api/sites/${body.id}`,
  });
  expect(after.pausedBy).toBe('site');
});
```

---

## Risk Assessment

**Note:** Full risk details (P, I, mitigation, timing) in the Architecture doc. Owner of all risks: Simon.

### High-Priority Risks (Score ≥6)

| Risk ID | Category | Description | Score | QA Test Coverage |
| --- | --- | --- | --- | --- |
| **R-01** | SEC/DATA | Nonce reuse or counter reset under fixed eFuse key | **6** | T-01, T-02, T-03 |
| **R-02** | DATA | Reading loss (ack before commit, forged ack accepted) | **6** | T-04..T-08 |
| **R-03** | BUS | Missed, late or duplicate notifications (window, restart, DST) | **6** | T-16..T-21, T-25, T-43 |
| **R-04** | BUS | Silence read as "all fine"; Hub suppression hides dead Node | **6** | T-22..T-24, T-26, T-28, T-29, T-34 |
| **R-05** | SEC | Cross-Site access or stale Roles | **6** | T-13, T-14, T-15, T-27 |
| **R-06** | SEC | Custom crypto (AD-12, AD-25) inconsistent or weak | **6** | T-09..T-12, T-41; crypto-spec review before V1 |
| **R-07** | TECH | Firmware regressions undetected (no on-device CI) | **6** | T-12, T-42 |

### Medium/Low-Priority Risks

| Risk ID | Category | Description | Score | QA Test Coverage |
| --- | --- | --- | --- | --- |
| R-08 | TECH | Pre-release dependencies change (Orleans NATS alpha, esp-radio beta, RCs) | 4 | T-35, T-36; Renovate PRs gated by full suite |
| R-09 | OPS | Restore > 1 h or reuses nonces | 4 | T-03, T-39; home-server runbook drill |
| R-10 | TECH | Node energy budget missed | 4 | T-42 (sleep current) |
| R-11 | TECH | Node BLE + ESP-NOW coexistence unvalidated | 4 | T-42 (coexistence) |
| R-12 | BUS | Accessibility regressions | 4 | T-30, T-37 |
| R-13 | OPS | Certificate renewal fails; Hub can't connect | 3 | T-38, T-40 |
| R-14 | SEC | Secrets committed to Git | 3 | T-38, T-45 |
| R-15 | BUS | Alert flapping or false alarms | 2 | T-16 |
| R-16 | DATA | Calibration applied to wrong Readings | 2 | T-31 |

---

## NFR Test Coverage Plan

| NFR Category | Requirement / Threshold | Planned Validation | Tool / Level | Evidence Artifact | Priority |
| --- | --- | --- | --- | --- | --- |
| Security | OIDC; per-Site authz; HMAC Hub auth (±5 min); sealed frames; TLS everywhere; no secrets in Git | T-09..T-15, T-38, T-45 | Contract, Grain, Integration, Infra, CI | Vector and authz-matrix reports, secret-scan report, crypto-spec review record | P0 |
| Performance | Notification ≤ 1 min after due; ack window W = 300 ms; no API SLO (by decision) | T-21; ack window in T-05, T-42 | Grain (fake clock), Integration (real time), Manual | Grain report, Notifier `dueAt`/`sentAt` metric, checklist record | P1 |
| Reliability | Zero loss on restart (NFR3); ack after commit; 24 h Node buffer; RTO ≤ 1 h | T-03..T-06, T-19, T-22, T-35, T-39 | Integration, Grain, Infra | Integration/grain reports, timed restore log | P0 |
| Maintainability | Test-first; contract compatibility; event replay; ≥ 80 % line coverage on domain and firmware logic | T-35, T-36, coverage | CI | Coverage report, compatibility logs | P1 |
| Energy / hardware | Full season on solar; ≥ 14 days no sun; sleep ≤ ~100 µA; reach to farthest Lot; ESP-NOW loss < 1 %; heap ≥ 32 KiB; channel recovery < 60 s | T-42 | Manual | Signed-off checklist per firmware release | P1 |
| Accessibility | Text 4.5:1, UI 3:1; Dynamic Type, font scale, zoom; screen readers | T-30, T-37 | UI component, Contract (CI), E2E (axe) | Contrast CI report, axe report, snapshots | P1 |
| Availability | Best effort | None | — | None | — |
| Scalability | No envelope | None | — | None | — |

**Missing thresholds or evidence sources:** API latency SLO (none, by decision); availability (best effort, no evidence); scalability (UNKNOWN by decision, accepted). Final PASS/CONCERNS/FAIL deferred to `nfr-assess`.

---

## Entry Criteria

- [ ] Requirements and assumptions agreed (Simon; step-2 answers recorded 2026-09-27)
- [ ] Aspire AppHost test host runs locally and in CI
- [ ] Per-test builders available (TC-7); no seed data required
- [ ] Enablers for the target story in place (see Dependencies: TC-1 in 1.2, TC-2 in 3.1, TC-3 in 3.2, TC-8 in 6.4)
- [ ] Story implemented test-first on its branch (no separate test phase)
- [ ] For firmware releases: bench hardware (Hub, Node, current meter, desktop BLE client) available

## Exit Criteria

- [ ] All P0 tests passing (100 %)
- [ ] P1 pass rate ≥ 95 % on every merge to `main`; failures triaged
- [ ] No open bugs linked to R-01..R-07
- [ ] Mitigations for R-01..R-07 implemented and passing before V1; R-06 crypto-spec review recorded
- [ ] Line coverage ≥ 80 % on Server domain code and `packages/rs/*` (UI and generated code excluded)
- [ ] T-42 signed off for the firmware release being shipped
- [ ] Evidence source present for every in-scope NFR category (decision deferred to `nfr-assess`)

---

## Project Team (Optional)

| Name | Role | Testing Responsibilities |
| --- | --- | --- |
| Simon | QA Lead, Dev Lead, PM, Architect (solo) | All test design and implementation, enablers, manual checklists, sign-off |
| Second person / external reviewer | Crypto reviewer | Review crypto-spec before V1 (R-06); run T-44 |

---

## Test Coverage Plan

**IMPORTANT:** P0/P1/P2/P3 = **priority and risk level**, NOT execution timing. See "Execution Strategy" for when tests run. Stories listed per row (epics.md). Duplicate-coverage guard: business rules at Grain/Unit only; Integration covers wiring, persistence, authz; E2E one happy path plus key failure per journey; UI renders fixture rows only.

### P0 (Critical)

**Criteria:** Critical business, security, data-integrity, or compliance impact with no safe workaround. Risk score is supporting evidence and is not a required condition.

| Test ID | Requirement | Test Level | Risk Link | Notes |
| --- | --- | --- | --- | --- |
| **T-01** | Frame counter never repeats across simulated reboots and power loss | Unit (Rust) | R-01 | 4.4; property test, mock flash |
| **T-02** | Resend sealed fresh with new counter; replayed/tampered frame rejected | Unit (Rust) + Integration (simulator) | R-01 | 4.4, 4.5 |
| **T-03** | Replay window accepts/rejects correctly; advances after restore | Integration | R-01, R-09 | 4.5, 2.3 |
| **T-04** | Crash between insert and response: resend is `duplicate`, no loss, no second row | Integration | R-02 | 4.5 |
| **T-05** | Node deletes buffer only on authenticated downlink | Unit (Rust) | R-02 | 4.4; includes ack window W |
| **T-06** | 1 h Hub outage: buffered Readings stored exactly once | Integration (simulator) + Manual | R-02 | 4.4, 4.5 |
| **T-09** | Key hierarchy, HPKE, AEAD nonce layout, setup session identical in Rust, C#, Kotlin | Contract | R-06 | 3.1; shared vectors |
| **T-10** | AD-25: wrong setup code fails; encrypted; code never sent over BLE | Unit (Rust + Kotlin) | R-06 | 3.4, 3.6 |
| **T-11** | Hub HMAC: bad signature, skew > ±5 min, nonce replay rejected | Integration | R-06 | 3.5 |
| **T-13** | Generated authz matrix: every endpoint × Role × own/other Site | Integration | R-05 | 1.6 → all |
| **T-14** | Role change applies next request; ≥ 1 Owner under concurrent demotion | Grain + Integration | R-05 | 9.2 |
| **T-16** | Threshold Alert opens/closes on exactly 3; deterministic IDs, no retry duplicate | Grain | R-15, R-03 | 6.1 |
| **T-18** | Window delivery: immediate, held, one summary per window, closed-while-held dropped | Grain (fake clock) | R-03 | 6.4 |
| **T-19** | Silo restart across 07:00 still sends summary; DST correct | Grain (fake clock) | R-03 | 6.4 |
| **T-22** | Silent Device: 6 h / 5 min defaults, override, recovery closes, downtime never counts | Grain (fake clock) | R-04 | 7.1 |
| **T-23** | Silent Hub: one Alert for 5 Nodes; windows restart; Lots *unknown* (Hub cause) | Grain (fake clock) | R-04 | 7.1 |
| **T-29** | `LotStatus` precedence and sort for all combinations (`unknownCause`, `pausedBy`) | Unit (projection) | R-04 | 4.7 |

**Total P0:** ~17 tests

---

### P1 (High)

**Criteria:** Core, frequent, or complex behavior with material user reach and a limited workaround. Risk score is supporting evidence and is not a required condition.

| Test ID | Requirement | Test Level | Risk Link | Notes |
| --- | --- | --- | --- | --- |
| **T-07** | Per-frame status vocabulary and HTTP semantics | Integration | R-02 | 4.5 |
| **T-08** | Unsynced rebased; `measured_at` > 5 min future rejected; clock slews, never back > 1 s | Unit (Rust) + Integration | R-02 | 4.4, 4.5 |
| **T-12** | eFuse burn once; dev mode never burns; release + dev mode fails to compile | Unit (Rust, mock HAL) + Manual | R-06, R-07 | 3.2 |
| **T-15** | Create Site idempotent, read-your-writes; Keycloak reconcile; no-Owner edit flagged | Grain + Integration | R-05 | 1.6, 1.7; TC-5 waits |
| **T-17** | Out-of-order/backlog Readings evaluated in `measured_at` order | Grain | R-03 | 6.1 |
| **T-20** | Reminder cadence User → Site → daily; Health ≤ 1/day; none after close | Grain (fake clock) | R-03 | 6.4 |
| **T-21** | Delivery ≤ 1 min after due; `dueAt`/`sentAt` recorded | Grain (fake clock) + Integration (real time) | R-03 | 6.4, 6.5; TC-8 |
| **T-24** | UJ-3: Node stops → after 6 h summary and *unknown* tile | E2E (Playwright, fake clock) | R-04 | 7.2 |
| **T-25** | UJ-2: crossing → Alert → browser notification → recovery closes, no Reminder | E2E | R-03 | 6.6 |
| **T-26** | UJ-4: Site paused to a date; no Health Alerts; auto resume | E2E | R-04 | 8.2 |
| **T-27** | UJ-5: invite, accept, Member alerted in own window, no admin controls | E2E | R-05 | 9.2; Mailpit |
| **T-28** | Pause sources; Site pause propagates; joining Device starts paused; pause closes Alerts | Grain (fake clock) | R-04 | 8.1 |
| **T-30** | UI renders all statuses and stale variants; light/dark; largest text; never colour alone | UI component | R-12 | 4.7, 1.3 |
| **T-31** | Calibration in force; history keeps its Calibration; recalibration affects new only | Grain | R-16 | 5.1 |
| **T-33** | Lot occupancy: concurrent claims, move with retried release, claimed Lot not removable | Grain | — | 4.2, 4.9, 1.9 |
| **T-35** | Event replay over fixture journal; projector rebuild from 0 | Contract + Integration | R-08 | 1.2 |
| **T-36** | Protobuf/OpenAPI/AsyncAPI compatibility; Hub golden JSON | Contract | R-08 | 3.1, 3.5, 6.6 |
| **T-37** | Contrast table in tokens CI; axe no serious violations per web page | Contract (CI) + E2E | R-12 | 1.3, 1.4 |
| **T-38** | Charts: no Secret data, TLS on every route, no port 80; k3d smoke ready | Infra | R-14, R-13 | 2.2, 2.4 |
| **T-39** | Backup to RustFS; restore ≤ 1 h (timed); marker row survives | Infra | R-09 | 2.3 |
| **T-41** | Hub and Node setup flows with every BLE error path (mocked BLE) | Unit (Kotlin) + UI component | R-06 | 3.6, 4.3 |
| **T-42** | Firmware manual checklist per release (items below) | Manual | R-07, R-10, R-11 | 3.x, 4.x |
| **T-45** | Secret scanning in CI | Contract (CI) | R-14 | 1.1 |

**Total P1:** ~23 tests

---

### P2 (Medium)

**Criteria:** Secondary behavior with narrower user reach and an acceptable workaround. Risk score is supporting evidence and is not a required condition.

| Test ID | Requirement | Test Level | Risk Link | Notes |
| --- | --- | --- | --- | --- |
| **T-32** | Thresholds: validation, three states, 20 % proposal, override survives redeclaration | Grain | — | 5.3, 4.6 |
| **T-34** | Low-battery and Uncalibrated Alert open/close rules | Grain | R-04 | 7.3, 7.4 |
| **T-40** | Hub rejects expired/untrusted certificate | Unit (Rust, mock clock) + Manual | R-13 | 3.5 |
| **T-43** | Real push on iPhone and Android; deep link opens Lot detail | Manual | R-03 | 6.5 |
| **T-44** | Reproduction by another person from the docs (SM-5) | Manual | — | 10.5 |

**Total P2:** ~5 tests

---

### P3 (Low)

**Criteria:** Rare, cosmetic, or experimental behavior with minimal impact and an easy workaround. Risk score is supporting evidence and is not a required condition.

| Test ID | Requirement | Test Level | Risk Link | Notes |
| --- | --- | --- | --- | --- |
| — | None planned | — | — | No performance/benchmark work (no latency SLO) |

**Total P3:** 0 tests

---

## Execution Strategy

**Philosophy:** Run everything in PRs unless there's significant infrastructure overhead. Target < 15 min per PR; Playwright parallelizes well and the fake clock keeps journeys short.

### Every PR: all functional tests (~10-15 min)

- Unit (xUnit, `cargo test` host, `kotlin.test`), Contract (vectors, schema diff, secret scan), Grain (TestCluster), Integration (Aspire), UI component, chart unit tests (helm-unittest, kubeconform)
- Playwright E2E journeys (T-24..T-27, T-37 axe) with fake clock, sharded as needed
- If E2E pushes the PR past 15 min, move E2E to nightly

### Nightly: infrastructure tests (~30-60 min)

- k3d smoke install and backup/restore with RustFS (T-38 smoke part, T-39)
- Swift snapshot job on macOS if runner cost matters
- No k6/performance tests (no latency SLO)

### Weekly: Chaos & Long-Running

N/A — no chaos, failover or endurance tests (single-node, best-effort availability). Restore timing is covered nightly (T-39).

**Manual tests** (per firmware or V1 release; results recorded in `docs/checklists/`):

| T-id | Checklist item | Threshold / pass condition |
| --- | --- | --- |
| T-42 | Hub eFuse burn and BLE setup via desktop client | Burns once; setup completes |
| T-42 | Hub Wi-Fi picks strongest BSSID | Strongest AP joined |
| T-42 | Hub heartbeat reaches Server | Every 30–60 s |
| T-42 | Node sleep current | ≤ ~100 µA |
| T-42 | Node reach to the farthest Lot | Readings arrive; ESP-NOW loss < 1 % |
| T-42 | Node BLE + ESP-NOW coexistence | Both work; free heap ≥ 32 KiB |
| T-42 | Channel re-scan after Hub channel change | Recovery < 60 s |
| T-42 | Acknowledgement window | W = 300 ms honoured |
| T-06 | Hub unplugged 1 h, then restored | All buffered Readings stored once |
| T-12 | Release build with dev mode enabled | Fails to compile; dev board never burns |
| T-40 | Hub with expired certificate on bench | Connection refused |
| T-43 | Real push on iPhone and Android | Delivered; deep link opens Lot detail |
| T-44 | Another person rebuilds from docs (once before V1) | Working system without help |
| R-06 | Crypto-spec review (once before V1) | Review recorded |

---

## QA Effort Estimate

**Test development effort only** (Simon, solo; built test-first per story, not a separate phase):

| Priority | Count | Effort Range | Notes |
| --- | --- | --- | --- |
| P0 | ~17 | ~50–80 h | Includes harness: Device simulator, fake-clock setup, authz-matrix generator |
| P1 | ~23 | ~40–70 h | Journeys, contracts, infra, UI, checklist authoring |
| P2 | ~5 | ~8–16 h | Secondary rules, manual runs |
| P3 | 0 | — | None planned |
| **Total** | ~45 | **~100–165 h (~2.5–4 weeks FTE)** | **1 person, spread across Epics 1–10** |

**Assumptions:**

- Includes test design, implementation, debugging, CI integration
- Excludes ongoing maintenance (~10 % effort) and per-release manual runs
- Enablers TC-1..TC-8 land with their stories (1.2, 3.1, 3.2, 6.4 and others)

**Dependencies:** See "Dependencies & Test Blockers".

---

## Implementation Planning Handoff (Optional)

| Work Item | Owner | Target Milestone (Optional) | Dependencies/Notes |
| --- | --- | --- | --- |
| `TimeProvider` rule + `FakeTimeProvider` hosts (TC-1) | Simon | Story 1.2 | Blocks all fake-clock tests |
| Per-test builders, unique-Site cleanup (TC-7) | Simon | Story 1.2 | No seeding endpoint |
| Authz-matrix generator (T-13) | Simon | Story 1.6 | Runs for every new endpoint |
| Journal-position wait helpers (TC-5) | Simon | Story 1.7 | Replaces sleeps |
| RustFS in k3d CI (TC-4) | Simon | Story 2.3 | T-39, T-03 restore part |
| `DeviceSimulator` + shared vectors (TC-2) | Simon | Story 3.1 | Cross-check with Rust |
| HAL-trait crate + mocks; `docs/checklists/` (TC-3) | Simon | Story 3.2 | T-42 template |
| Notifier test double, Mailpit, `dueAt`/`sentAt` metric (TC-4, TC-8) | Simon | Stories 6.4, 6.5, 9.1 | T-21, T-27 |
| Crypto-spec review | External reviewer | Before V1 | R-06 exit criterion |

---

## Tooling & Access

| Tool or Service | Purpose | Access Required | Status |
| --- | --- | --- | --- |
| RustFS 1.0.0 | S3 stand-in for backup/restore | Container image in k3d / Aspire | Pending (Story 2.3) |
| Mailpit | Invitation email capture | Container in Aspire AppHost | Pending (Story 9.1) |
| k3d | Chart smoke install, restore test | CI runner with Docker | Pending (Story 2.2) |
| macOS CI runner | SwiftUI snapshot tests | Runner minutes | Pending |
| iPhone + Android phone, APNs/FCM credentials | T-43 real push | Apple/Google developer accounts | Pending |
| ESP32-S3 Hub/Node bench, current meter, desktop BLE client | T-42 and firmware manual items | Physical hardware | Pending |
| DNS provider API token | Manual DNS-01 renewal check | Provider account | Pending |

**Access requests needed (if any):**

- [ ] APNs key and FCM project for push testing
- [ ] macOS runner minutes for Swift snapshots

---

## Interworking & Regression

| Service/Component | Impact | Regression Scope | Validation Steps |
| --- | --- | --- | --- |
| **Orleans Server (grains, journal, projections)** | Core of all rules | Grain + Integration suites, event replay (T-35) | Full PR suite green |
| **Edge API / device ingest** | Device data path | T-02..T-08, T-11, golden JSON (T-36) | Simulator integration tests |
| **Keycloak → Temporal → Orleans** | Identity and Roles | T-13..T-15, T-27 | Aspire integration with wait helpers |
| **BFF + web app** | Journeys, live updates | T-24..T-27, T-37 | Playwright E2E |
| **KMP core + iOS/Android shells** | Setup flows, rendering | T-30, T-41 | Kotlin unit, snapshot/screenshot tests |
| **Firmware (Hub, Node)** | Radio, crypto, buffer | T-01, T-05, T-08, T-12, T-40, T-42 | Host `cargo test` + manual checklist |
| **Helm charts / deployment** | Secrets, TLS, backups | T-38, T-39 | Chart unit tests, nightly k3d |
| **Shared contracts (Protobuf, OpenAPI, AsyncAPI, crypto-spec)** | All languages | T-09, T-36 | Vector runners in Rust, C#, Kotlin |

**Regression test strategy:**

- Every merge to `main` runs the full PR suite; Renovate dependency PRs are gated by it (R-08)
- Nightly infra run must be green before a release tag; T-42 signed off before any firmware release
- No cross-team coordination needed (solo project); external reviewer only for R-06 and T-44

---

## Appendix A: Code Examples & Tagging

**Playwright Tags for Selective Execution (web E2E; paths illustrative):**

```typescript
import { test } from '@seontechnologies/playwright-utils/api-request/fixtures';
import { expect } from '@playwright/test';

// P0 security check at the BFF edge
test('@P0 @API @Security unauthenticated request returns 401', async ({ apiRequest }) => {
  const { status } = await apiRequest({
    method: 'GET',
    path: '/api/sites',
    skipAuth: true,
  });

  expect(status).toBe(401);
});

// P1 journey data check
test('@P1 @E2E T-24 silent Node shows unknown Lot status', async ({ apiRequest }) => {
  const { status, body } = await apiRequest({
    method: 'GET',
    path: '/api/sites/test-site/lots',
  });

  expect(status).toBe(200);
  expect(body.lots[0].status).toBe('unknown');
});
```

**Run specific tags / priorities per stack:**

```bash
# Web E2E
npx playwright test --grep @P0
npx playwright test --grep "@P0|@P1"

# .NET (xUnit trait Priority=P0)
dotnet test --filter "Priority=P0"

# Rust firmware logic (host)
cargo test --workspace

# Kotlin shared core
./gradlew allTests
```

---

## Appendix B: Knowledge Base References

- **Risk Governance**: `risk-governance.md` - Risk scoring methodology
- **Test Priorities Matrix**: `test-priorities-matrix.md` - P0-P3 criteria
- **Test Levels Framework**: `test-levels-framework.md` - E2E vs API vs Unit selection
- **Test Quality**: `test-quality.md` - Definition of Done (no hard waits, ≤1000 lines, <1.5 min)
- **NFR Criteria**: `nfr-criteria.md` - NFR categories and evidence
- **ADR Quality Readiness**: `adr-quality-readiness-checklist.md` - testability of ADRs

---

**Generated by:** BMad TEA Agent
**Workflow:** `bmad-testarch-test-design`
**Version:** 4.0 (BMad v6)
