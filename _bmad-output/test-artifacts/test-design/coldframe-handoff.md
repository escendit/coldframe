---
title: 'TEA Test Design → BMAD Handoff Document'
version: '1.0'
workflowType: 'testarch-test-design-handoff'
inputDocuments:
  - _bmad-output/test-artifacts/test-design-progress-system.md
  - _bmad-output/planning-artifacts/epics.md
  - _bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md
sourceWorkflow: 'testarch-test-design'
generatedBy: 'TEA Master Test Architect'
generatedAt: '2026-09-27'
projectName: 'Coldframe'
---

# TEA → BMAD Integration Handoff

## Purpose

This document connects TEA's system-level test design for Coldframe to BMAD implementation planning. **The epics and stories already exist** (`_bmad-output/planning-artifacts/epics.md`: 10 epics, 53 stories). This handoff does not propose new epics or stories. It says which existing epic carries which risk mitigation, and which tests each existing story writes test-first. Risk and test IDs (R-xx, T-xx, TC-x) match the checkpoint exactly.

The testability enablers are already in the plan:

| Enabler | What | Spine | Story |
|---|---|---|---|
| TC-1 | `TimeProvider` everywhere; `FakeTimeProvider` in TestCluster and Aspire test hosts | AD-6, AD-24 | 1.2, 6.4 |
| TC-2 | `DeviceSimulator` test library (C#), cross-checked against Rust vectors | AD-24 | 3.1 |
| TC-3 | HAL-trait crate `packages/rs/hal` with mocks | AD-24, source tree | 3.2 |
| TC-8 | Notifier `dueAt`/`sentAt` log and metric | AD-7 | 6.4 |

## TEA Artifacts Inventory

| Artifact | Path | BMAD Integration Point |
|---|---|---|
| Test Design: Architecture | `_bmad-output/test-artifacts/test-design-architecture.md` | Testability enablers, ASRs, NFR planning for the spine |
| Test Design: QA | `_bmad-output/test-artifacts/test-design-qa.md` | Coverage matrix, execution strategy, story test requirements |
| Progress checkpoint | `_bmad-output/test-artifacts/test-design-progress-system.md` | Source of record for R-01..R-16, T-01..T-45, quality gates |
| Risk Assessment | (embedded in the checkpoint and the architecture document) | Epic risk classification, story priority |
| Coverage Strategy | (embedded in the checkpoint and the QA document) | Story test requirements |
| This handoff | `_bmad-output/test-artifacts/test-design/coldframe-handoff.md` | Epic and story guidance for ATDD and implementation |

## Epic-Level Integration Guidance

### Risk References

The seven risks scoring ≥ 6 (none scores 9). Each must be mitigated, with tests passing, before V1.

| Risk | Score | Carrying epic | Mitigation the epic must deliver |
|---|---|---|---|
| R-01 Nonce reuse or counter reset | 6 | **Epic 4** (restore advance in Epic 2) | Counter property tests across reboots and power loss; Server replay-window tests; window advance after restore (T-01..T-03) |
| R-02 Reading loss (ack before commit, forged ack) | 6 | **Epic 4** | Crash-between-insert-and-response test; delete only on authenticated downlink; 1 h Hub-outage simulator test (T-04..T-08) |
| R-03 Missed, late or duplicate notifications | 6 | **Epic 6** | Fake-clock TestCluster suite: restart across 07:00, daylight saving, summary dedup, ≤ 1 min latency (T-16..T-21, T-25) |
| R-04 Silence reads as "all fine" | 6 | **Epic 7** (also Epics 4 and 8) | Node and Hub windows, suppression lifting, no false silence on restart; UJ-3 E2E (T-22..T-24, T-26, T-28, T-29) |
| R-05 Cross-Site access or stale Roles | 6 | **Epics 1 and 9** | Generated authorization matrix on every endpoint; revocation on the next request (T-13..T-15, T-27) |
| R-06 Custom crypto inconsistent or weak | 6 | **Epic 3** | Shared vectors in three languages; negative tests; recorded crypto-spec review before V1 (T-09..T-12, T-41) |
| R-07 Firmware regressions without on-device tests | 6 | **Epics 3, 4 and 10** | HAL-trait crate (TC-3); host-side coverage; versioned manual checklist per firmware release (T-12, T-42) |

P2-band risks (score 2–4: R-08..R-16) are carried as listed in the Risk-to-Story Mapping below.

### Quality Gates

Global gates, applied to every merge to `main` and to the V1 release:

- P0 pass rate 100 %, P1 ≥ 95 %.
- R-01..R-07 mitigations implemented and passing before V1; R-06 also needs the crypto-spec review recorded.
- Line coverage ≥ 80 % on Server domain code (grains, projections) and firmware logic crates (`packages/rs/*`); UI and generated code excluded.
- T-42 signed off for the firmware release being shipped.
- An evidence source identified for every in-scope NFR category; PASS/CONCERNS/FAIL deferred to `nfr-assess`.

Per epic (an epic is done when these pass):

| Epic | Gate |
|---|---|
| 1 Sign in and create my garden | T-13 (P0) green on every endpoint; T-35, T-37, T-45 in CI |
| 2 Run Coldframe on my home server | T-03 restore advance green; T-39 restore ≤ 1 h (timed, nightly); T-38 chart tests |
| 3 Bring the Hub online | T-09, T-10, T-11 (P0) green; `DeviceSimulator` (TC-2) and HAL crate (TC-3) in place; T-42 Hub items signed off |
| 4 See what my soil is doing | T-01, T-02, T-04, T-05, T-06, T-29 (P0) green; T-42 Node items signed off |
| 5 Calibrate the soil and set Thresholds | T-31 green |
| 6 Get told when to water | T-16, T-18, T-19 (P0) green; T-21 latency ≤ 1 min asserted |
| 7 Know when something breaks | T-22, T-23 (P0) green; T-24 E2E green |
| 8 Pause for maintenance and winter | T-28, T-26 green |
| 9 Share the garden | T-14 (P0) green; T-27 E2E green |
| 10 Let others rebuild Coldframe | T-42 checklist versioned and signed off for the release; T-44 before V1 |

## Story-Level Integration Guidance

### P0/P1 Test Scenarios → Story Acceptance Criteria

Write these tests first (failing) in each story. **Bold** = P0. P2 rows are listed for completeness.

| Story | Tests to write test-first |
|---|---|
| 1.1 Monorepo scaffold, CI and local dev stack | T-45 |
| 1.2 Event journal, migrations and projection pipeline | T-35 (TC-1 `TimeProvider` convention lands here) |
| 1.3 Design tokens and themes | T-30, T-37 |
| 1.4 Sign in on the web | T-37 |
| 1.6 Create a Site with per-Site authorization | **T-13**, T-15 |
| 1.7 Reconcile identity changes from Keycloak | T-15 |
| 1.9 Manage my Site and Lots | T-33 |
| 2.2 Helm charts with a fixed Secret contract | T-38 |
| 2.3 Database cluster with off-node backups and tested restore | **T-03**, T-39 |
| 2.4 TLS with public certificates | T-38 |
| 3.1 Wire and crypto contracts with shared test vectors | **T-09**, T-36 (TC-2 `DeviceSimulator` lands here) |
| 3.2 Hub firmware foundation | T-12 (TC-3 HAL crate lands here) |
| 3.4 Hub BLE setup service | **T-10** |
| 3.5 Hub joins Wi-Fi and heartbeats | **T-11**, T-36, T-40 (P2) |
| 3.6 Add a Hub from my phone | **T-10**, T-41 |
| 3.x Hub firmware stories | T-42 (Hub items) |
| 4.1 Node firmware foundation | T-42 (sleep current, R-10) |
| 4.2 Node pairing | T-33, T-42 (BLE plus ESP-NOW coexistence, R-11) |
| 4.3 Add a Node from my phone | T-41 |
| 4.4 ESP-NOW transport from Node to Hub | **T-01, T-02, T-05, T-06**, T-08 |
| 4.5 Server ingestion and acknowledgements | **T-02, T-03, T-04, T-06**, T-07, T-08 |
| 4.6 Sensor Specifications and Sensor grains | T-32 (P2) |
| 4.7 Lot status and the Site overview | **T-29**, T-30 |
| 4.9 Move or unassign a Node | T-33 |
| 5.1 Calibration on the Server | T-31 |
| 5.3 Thresholds on the Server | T-32 (P2) |
| 6.1 Threshold Alerts open and close | **T-16**, T-17 |
| 6.4 Delivery timing | **T-18, T-19**, T-20, T-21 (TC-8 lands here) |
| 6.5 Push notifications on my phone | T-21, T-43 (P2) |
| 6.6 Browser notifications and live updates | T-25, T-36 |
| 7.1 Silent Device Alert on the Server | **T-22, T-23** |
| 7.2 Silence in the apps | T-24 |
| 7.3 Low-battery Alert / 7.4 Uncalibrated Sensor Alert | T-34 (P2) |
| 8.1 Pause and resume on the Server | T-28 |
| 8.2 Pause and resume in the apps | T-26 |
| 9.2 Change and remove Roles | **T-14**, T-27 |
| 10.5 Someone else reproduces Coldframe | T-44 (P2) |

Test-level rules (duplicate-coverage guard): business rules at Grain or Unit level only; Integration covers wiring, persistence and authorization; E2E covers one happy path per journey plus its key failure; UI tests render fixture rows and never re-test status computation. Test data is per test, synthetic and self-cleaning (TC-7); no seeding endpoint. Async waits use journal-position or checkpoint polling, never sleeps (TC-5).

### Data-TestId Requirements

No `data-testid` list is defined at system level. UI coverage uses fixture-row snapshot tests (T-30) and axe (T-37); web E2E (T-24..T-27) uses Playwright. Define selectors per story during ATDD.

## Risk-to-Story Mapping

| Risk ID | Category | P×I | Recommended Story/Epic | Test Level |
|---|---|---|---|---|
| R-01 | SEC/DATA | 2×3=6 | 4.4, 4.5, 2.3 (Epic 4) | Unit (Rust), Integration (simulator) — T-01..T-03 |
| R-02 | DATA | 2×3=6 | 4.4, 4.5 (Epic 4) | Unit (Rust), Integration (simulator), Manual — T-04..T-08 |
| R-03 | BUS | 2×3=6 | 6.1, 6.4, 6.5, 6.6 (Epic 6) | Grain (fake clock), Integration, E2E — T-16..T-21, T-25, T-43 |
| R-04 | BUS | 2×3=6 | 7.1, 7.2, 7.3, 7.4, 4.7, 8.1, 8.2 (Epic 7) | Grain (fake clock), Unit (projection), E2E — T-22..T-24, T-26, T-28, T-29, T-34 |
| R-05 | SEC | 2×3=6 | 1.6, 1.7, 9.2 (Epics 1, 9) | Integration, Grain, E2E — T-13..T-15, T-27 |
| R-06 | SEC | 2×3=6 | 3.1, 3.2, 3.4, 3.5, 3.6, 4.3 (Epic 3) | Contract, Unit (Rust + Kotlin), Integration — T-09..T-12, T-41 |
| R-07 | TECH | 3×2=6 | 3.2, 3.x, 4.x (Epics 3–4, 10) | Unit (Rust, mock HAL), Manual — T-12, T-42 |
| R-08 | TECH | 2×2=4 | 1.2, 3.1, 3.5, 6.6 (ongoing) | Contract, Integration — T-35, T-36 |
| R-09 | OPS | 2×2=4 | 2.3, 4.5 (Epic 2) | Infra, Integration — T-03, T-39 |
| R-10 | TECH | 2×2=4 | 4.1 (Epic 4) | Manual — T-42 |
| R-11 | TECH | 2×2=4 | 4.2 (Epic 4) | Manual — T-42 |
| R-12 | BUS | 2×2=4 | 1.3, 1.4, 4.7 (all UI epics) | UI component, Contract (CI), E2E — T-30, T-37 |
| R-13 | OPS | 1×3=3 | 2.4, 3.5 (Epics 2–3) | Infra, Unit (Rust, mock clock), Manual — T-38, T-40 |
| R-14 | SEC | 1×3=3 | 1.1, 2.2 (Epic 1) | Contract (CI), Infra — T-38, T-45 |
| R-15 | BUS | 1×2=2 | 6.1 (Epic 6) | Grain — T-16 |
| R-16 | DATA | 1×2=2 | 5.1 (Epic 5) | Grain — T-31 |

## Recommended BMAD → TEA Workflow Sequence

Epics and stories already exist, so step 2 becomes a refinement pass, not a creation pass.

1. **TEA Test Design** (`TD`) → produces this handoff document
2. **BMAD epics/stories refinement** → confirm each story's acceptance criteria carry the T-ids above (enablers already added to 1.2, 3.1, 3.2, 6.4)
3. **TEA ATDD** (`AT`) → generates failing acceptance tests per story, starting with P0 rows
4. **BMAD Implementation** → developers implement test-first
5. **TEA Automate** (`TA`) → expands the suite
6. **TEA Trace** (`TR`) → validates coverage against T-01..T-45 and the quality gates

## Phase Transition Quality Gates

| From Phase | To Phase | Gate Criteria |
|---|---|---|
| Test Design | Epic/Story refinement | All P0 risks (R-01..R-07) have a mitigation and a carrying epic — met |
| Epic/Story refinement | ATDD | Stories carry the T-ids from this handoff in their acceptance criteria |
| ATDD | Implementation | Failing acceptance tests exist for all P0/P1 scenarios of the story |
| Implementation | Test Automation | All acceptance tests pass; P0 100 %, P1 ≥ 95 % |
| Test Automation | Release | Trace shows ≥ 80 % coverage of P0/P1 requirements; R-01..R-07 mitigated; R-06 review recorded; T-42 signed off |
