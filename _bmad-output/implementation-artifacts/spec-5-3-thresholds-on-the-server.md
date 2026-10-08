---
title: 'Story 5.3: Thresholds on the Server'
type: 'feature'
created: '2026-10-08'
baseline_revision: 'a9749778c7bfef29aa2f30b0860cba854da0ddb4'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-5-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-5-1-calibration-on-the-server.md'
  - '{project-root}/apps/cs/README.md'
warnings: ['oversized']
deferred:
  - summary: >-
      KeycloakReconciliationTests.ARenameInKeycloakRenamesTheSite times out after 60 s on this host, and a full integration run showed Keycloak "Initialized" health DOWN with class-cleanup failures.
    evidence: |-
      The test fails identically on the untouched baseline (a9749778) with this story's changes stashed; the first full run of this story passed all 913 tests, so it depends on Keycloak start-up on the host.
    location: >-
      tests/cs/server.integration/Identity/KeycloakReconciliationTests.cs:137
    severity: low
---

<intent-contract>

## Intent

**Problem:** The Sensor grain stores Thresholds (`Default | Override(value) | Cleared`, `SensorThresholdsChanged`) but nothing sets them: only fixtures write the event, no rule validates a side, no proposal exists for a Sensor without a Specification default, and no endpoint reads or writes Thresholds.

**Approach:** Add `SetThresholds` and a Threshold read (with the proposed low) to the Sensor grain, the only validator and writer (AD-19), and expose them as `GET` (Member, read-only) and `PUT` (Administrator, which includes Owner) on `/sites/{siteId}/sensors/{sensorId}/thresholds`, in the OpenAPI contract and the authorization matrix. A change journals `SensorThresholdsChanged`, which Story 6.1 reads as a new evaluation epoch.

## Boundaries & Constraints

**Always:** The Sensor grain alone validates. A Sensor is *alerting* when its effective low exists. Rules on the effective values after the change: a high without a low is refused (low required on an alerting Sensor), low must be strictly below high, an empty high is allowed and never alerts, clearing both sides makes the Sensor watched only. A side's request is `default`, `override` with a value, or `cleared`; a value on `default`/`cleared` or none on `override` is refused. A calibrating Sensor (`calibration: true`) takes and gives whole percent 0 to 100 whatever its Calibration state (Threshold % never changes on recalibration); any other Sensor takes values within its Specification range. The API speaks display units (`%`, `°C`, `kΩ`; AD-14), the grain stores the Specification unit. A change that leaves both sides as they are journals nothing (no new epoch). A later Specification redeclaration never replaces an override (already true; keep it covered). The proposed low is `Min + 20 % x (Max - Min)` of the Sensor's range when its Specification has no default low (20 % for a calibrating Sensor), never a proposed high. Members read, never write: PUT answers 403 and is in the authorization matrix; GET is open to Members. Events past tense, PRD terms verbatim, `TimeProvider` not wall clock. Test-first (NFR16): named failing tests before implementation. Every endpoint in OpenAPI, README and the authorization matrix.

**Never:** No app or UI work (5.4), no Alert evaluation or Notification (Epic 6), no change to Calibration math or 5.1 behaviour, no change to stored history, no editing of existing migrations, no writing `sprint-status.yaml`. No Threshold work in the Device grain.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Set low | Soil Sensor, `low: override 25`, high unchanged | `SensorThresholdsChanged` journaled, 200 with both sides | — |
| Same again | Request equals the effective sides | 200, nothing journaled | — |
| Add / clear high | `high: override 70`, later `high: cleared` | Journaled each time; cleared high never alerts | — |
| Back to default | `low: default` | Side follows the Specification default again | — |
| Low not below high | low 70, high 60 or equal | Refused, nothing journaled | 400 validation |
| High without low | low `cleared` (or no default), high `override` | Refused | 400 validation |
| Bad side | `override` without value, value on `default`, % outside 0-100, value outside range | Refused | 400 validation |
| No default, watched | Temperature, `GET` | `proposedLow` = Min + 20 % x (Max - Min), no `proposedHigh`, sides `cleared`/default empty | — |
| Redeclaration | Specification changes after an override | Override value and kinds survive | — |
| Member | Member `PUT` / `GET` | PUT 403; GET 200 read-only | 403 |
| Unknown / other Site's Sensor | Not in the caller's Site | Not disclosed | 404 sensor-not-found |

</intent-contract>

## Code Map

- `packages/cs/contracts/Sensors/SensorGrains.cs` -- `ISensorGrain` (add `SetThresholds`, `ThresholdProposal`/read), `ThresholdSetting`/`ThresholdKind` (exist), `SensorSnapshot` already carries effective sides; add request/result/outcome DTOs and limits beside the Calibration ones.
- `packages/cs/contracts/Sensors/SensorEvents.cs` -- `SensorThresholdsChanged` exists (`[EventType("sensor.thresholds-changed")]`); update its remark ("only fixtures write it").
- `apps/cs/server/Sensors/SensorGrain.cs`, `SensorState.cs` -- `Calibrate` is the template (`RaiseEvent` + `ConfirmEvents`, `Clock`); `Effective`/`EffectiveLow`/`EffectiveHigh`, `Apply(SensorThresholdsChanged)` exist. Add `ThresholdRules` (pure, unit-testable: validate, proposal) in `apps/cs/server/Sensors/`.
- `apps/cs/server/Edge/EdgeApi.cs` (routes ~l.388-394, `GetSensorCalibrationAsync`/`FindSiteSensorAsync` ~l.1440-1500, `ToHttpResult` pattern), `EdgeProblems.cs`, `EdgeValidation.cs` -- add `getSensorThresholds` (Member) and `setSensorThresholds` (Administrator), request/response records, display-unit conversion (`apps/cs/server/Lots/SensorConversion.cs` units: milli-°C, milli-%, Ω to °C, %, kΩ).
- `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md` -- both operations with `x-coldframe-minimum-role`, `SensorThresholds`/request schemas; `pnpm --filter @coldframe/api-client generate` rewrites `packages/ts/api-client/src/schema.ts`, aliases in `src/index.ts`. `EdgeEndpointDiscoveryTests` fails on any mapped/contract mismatch.
- `tests/cs/server.integration/Edge/AuthorizationMatrixTests.cs` (`_samples` ~l.109-121) -- add both samples; the endpoint catalog discovers routes by itself. Endpoint tests beside `tests/cs/server.integration/Devices/CalibrationEndpointTests.cs`; TestCluster tests beside `CalibrationGrainTests.cs`/`SpecificationGrainTests.cs` (`SeedNodeAsync`, `IngestAsync`).
- `tests/cs/server.tests/Sensors/` (`SensorStateTests.cs`, `SensorStateCalibrationTests.cs`) -- unit tests for rules and state; `tests/cs/server.tests/Fixtures/journal.json` already has a thresholds-changed row.
- `apps/cs/README.md` -- document Thresholds (sections "Sensor Specifications", "Add an endpoint").

## Tasks & Acceptance

**Execution:**
- `tests/cs/server.tests/Sensors/**`, `tests/cs/server.integration/**` -- write failing tests first, named per matrix row (every validation rule, the three states, the 20 % proposal, an override surviving a redeclaration, no-op journals nothing, Thresholds-changed event raised, Member 403/GET 200, other Site 404, calibrating Sensor in 0-100 %) -- NFR16
- `packages/cs/contracts/Sensors/*` -- grain methods, DTOs, outcomes; refresh the event remark
- `apps/cs/server/Sensors/*` -- `ThresholdRules`, `SetThresholds`, proposal on read; journal only on a real change
- `apps/cs/server/Edge/*` -- two endpoints, unit conversion, Problem Details (400 validation)
- `packages/openapi/*`, `packages/ts/api-client/*` -- contract, README, regenerated schema, aliases
- `tests/cs/server.integration/Edge/AuthorizationMatrixTests.cs`, `apps/cs/README.md` -- matrix samples and docs

**Acceptance Criteria:**
- Given a Sensor, when Thresholds are set, then the Sensor grain accepts only a low below the high, a low when a high is set, and well-formed sides, and stores each side as Default, Override(value) or Cleared; anything else is refused with nothing journaled.
- Given a Specification redeclaration after an override, then the override and kinds are unchanged.
- Given a watched Sensor with no Specification default, when Thresholds are read, then the proposed low is `Min + 20 % x (Max - Min)` with no proposed high.
- Given a calibrating Sensor, when Thresholds are read or written, then they are whole percent 0 to 100.
- Given a saved change, then `SensorThresholdsChanged` is journaled (the evaluation epoch Story 6.1 uses); an unchanged request journals nothing.
- Given a Member, when they change Thresholds, then 403; they can read them; both endpoints are in the OpenAPI contract and the authorization matrix.

## Spec Change Log

## Review Triage Log

### 2026-10-08 — Review pass
- verdicts: 34 findings (blind 14, edge 12, verification-gap 3, intent-alignment 5 divergence notes) — high 0, medium 5, low 23, false 6, maybe-false 0; rows below group findings that share a cause
- findings:
  - `[medium]` `[patch]` Edge rounds a display value finer than the stored unit although the Design Notes say it refuses one that does not convert exactly (blind, edge x2, intent-alignment) — `TryStoredSide` now refuses; test `AValueFinerThanTheStoredUnitIsA400AndNothingIsJournaled` and README wording added.
  - `[medium]` `[patch]` No test declares a humidity (milli-%) Sensor, so that display arm is unpinned (verification-gap) — added `AHumidityValueIsConvertedFromPercentToMilliPercentAndBack`.
  - `[low]` `[patch]` `ACalibratingSensorTakesWholePercentWhateverItsCalibrationState` never calibrates (verification-gap) — renamed to what it checks.
  - `[low]` `[reject]` Stale Specification between the snapshot read and `SetThresholds` mis-scales a value (blind, edge x2) — needs a unit change for an existing Sensor ID within one request; the fix adds a public request field.
  - `[low]` `[reject]` Kept sides not range-checked or left inconsistent after a Specification redeclaration, so an unrelated PUT is refused (blind, edge x2, verification-gap) — reachable only by a redeclaration that narrows a range or removes a default under an override; refusing an invalid end state is the stated rule.
  - `[false]` `[reject]` Edge duplicates grain validation and a direct grain caller can store a fractional percent (blind) — the grain takes `long`, so a fraction cannot reach it; the Edge pre-check only maps display units.
  - `[low]` `[reject]` `Changed = 0` is a success value and `ToHttpResult` throws on an impossible result (blind, edge) — a default-initialised result is not producible; failing loudly is the codebase pattern.
  - `[low]` `[reject]` `ThresholdSetting` means kind-only on a request and effective value in a response (blind) — pre-existing contract from `Describe`; WellFormed refuses only what a request sends.
  - `[low]` `[reject]` Unit tables duplicated in `EdgeApi` and `SensorConversion`; obscure `Shown` normalisation (blind x2) — different directions and types; the endpoint tests pin the output.
  - `[low]` `[reject]` Proposed low ignores the effective high (blind) — with no default low a high needs a low, so the proposal only matters before any side is set.
  - `[low]` `[reject]` `SensorThresholdsChanged` carries no actor (blind) — no acceptance criterion asks for one and adding a field changes a journaled event contract.
  - `[low]` `[reject]` PUT has PATCH semantics and no concurrency guard (blind) — the spec and OpenAPI say an absent side stays; the grain validates against current state on every write.
  - `[low]` `[reject]` Shared-cluster silo restart and a bundled refusal test (blind) — the full suite ran green; splitting adds no coverage.
  - `[low]` `[reject]` Redundant grain calls on GET and PUT (blind) — one cheap local call each; same shape as the calibration endpoints.
  - `[low]` `[reject]` OpenAPI gives no per-quantity range and a generic 400 (blind) — documentation polish, no behaviour at stake.
  - `[low]` `[reject]` PUT validates the body before the Sensor lookup so a bad body on a foreign Sensor is 400 not 404 (edge x2) — same order as `CalibrateSensorAsync`; a 400 for a malformed body discloses nothing about the Sensor.
  - `[false]` `[reject]` `ProposedLow` overflows or leaves the range for an inverted range (edge) — Specifications declare Min below Max and the product cannot overflow a long for any real range.
  - `[false]` `[reject]` `ConfirmEvents` failure leaves unpersisted state (edge) — the same `JournaledStreamGrain` pattern every Sensor write uses.
  - `[false]` `[reject]` `SensorState.Effective` delegating adds a null-guard on replay (edge) — a stored side is never null.
  - `[false]` `[reject]` Intent-alignment: the awaiting-operator clause cannot be confirmed from the diff — it applies only to human-only acceptance criteria and this story has none; status goes to `done`.
  - `[false]` `[reject]` Intent-alignment: the grain refuses a `default` side carrying a value after the Edge builds it — that is the designed path, covered by the 400 refusal test.
  - `[low]` `[reject]` Intent-alignment: no new `SensorState` unit tests and no TS tests — the delegation is covered by existing `SensorStateTests`, and the TS changes are generated and checked by `pnpm -r test`.

## Design Notes

**Alerting** is not stored: a Sensor alerts exactly when its effective low exists, so clearing the low turns alerts off and a high alone is refused. **Units:** the grain compares stored values (Specification unit; percent for a calibrating Sensor); the Edge converts to and from display units, rounding a written display value to the stored unit and refusing one that does not convert exactly into range. **Proposal:** computed with integer arithmetic on the Specification range, a read-time convenience, never journaled until an Administrator saves it as an override.

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror` -- expected: success
- `dotnet format --verify-no-changes --no-restore` -- expected: no changes
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- expected: all pass
- `pnpm -r test` and `pnpm lint && pnpm typecheck` -- expected: success (generated client not stale)

## Auto Run Result

Status: done

**Summary:** An Owner or Administrator can set, change and clear a Sensor's low and high Thresholds over `PUT /sites/{siteId}/sensors/{sensorId}/thresholds`, and any Member can read them, with the proposed low, over `GET` on the same path. The Sensor grain (`SetThresholds`, `GetThresholds`, pure `ThresholdRules`) is the only validator: it checks the effective values (a high needs a low, low strictly below high, whole percent 0 to 100 for a calibrating Sensor, the Specification range otherwise), stores each side as `Default`, `Override(value)` or `Cleared`, and journals `SensorThresholdsChanged` only for a real change. The Edge converts display units (`%`, `°C`, `kΩ`) and refuses a value finer than the stored unit. Proposed low is `Min + 20 % x (Max - Min)` when the Specification has no default low. An override survives a Specification redeclaration.

**Files changed:** `packages/cs/contracts/Sensors/SensorGrains.cs`, `SensorEvents.cs`; `apps/cs/server/Sensors/ThresholdRules.cs` (new), `SensorGrain.cs`, `SensorState.cs`; `apps/cs/server/Edge/EdgeApi.cs`; `packages/openapi/coldframe.openapi.json` and `README.md`; `packages/ts/api-client/src/schema.ts` and `index.ts`; `apps/cs/README.md`; tests `ThresholdRulesTests`, `ThresholdGrainTests`, `ThresholdEndpointTests`, `AuthorizationMatrixTests`.

**Review:** 34 findings. Patched: 3 (refuse inexact display values and document it; humidity conversion test; test rename). Deferred: 1 (pre-existing Keycloak test failure, not caused by this story). Rejected: all others, with reasons in the Review Triage Log.

**Follow-up review recommended:** false (patched: 0 high, 2 medium, 1 low).

**Verification:** `dotnet build -warnaserror` and `dotnet format --verify-no-changes` clean; `dotnet test` 913 passed on the first full run; after the review patches the unit project (544) and the Threshold and authorization matrix integration tests (19) pass. A later full integration run had Keycloak start-up failures on this host; `KeycloakReconciliationTests.ARenameInKeycloakRenamesTheSite` fails the same way on the untouched baseline. `pnpm -r test`, `pnpm lint` and `pnpm typecheck` passed (no TypeScript change after the review). Every matrix row is covered by a passing test.

**Residual risks:** the Kotlin client is not regenerated (Story 5.4 is the app work); a Specification redeclaration that narrows a range leaves a stored override outside it until the next write; the full integration suite was not rerun green after the review patches because of the Keycloak start-up problem on this host.
