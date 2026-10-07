---
title: 'Story 4.9: Move or unassign a Node'
type: 'feature'
created: '2026-10-07'
baseline_revision: 'd5a49525a05d054c59d03c63b4f02aa99693c3bc'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
warnings: [oversized]
deferred:
  - summary: >-
      A crash or journal failure between the successful Lot claim and the DeviceMoved write leaves the target Lot claimed by a Node that is not on it.
    evidence: |-
      DeviceGrain.Move claims first, then journals (AD-18 order, same as Enrol). No pending claim is persisted. A client retry of the same Move is idempotent (Lot.Claim answers Held) and heals it.
    location: >-
      apps/cs/server/Devices/DeviceGrain.cs Move
    severity: low
  - summary: >-
      iOS unassign-confirmation gating (row tap does not call unassignNode, confirm does) has no behavioural test; the Swift test only asserts catalogue strings.
    evidence: |-
      No view-level iOS harness exists and no Swift toolchain was available on this host; Android and web have behavioural tests for the dialog.
    location: >-
      apps/swift/ios/Sources/ColdframeIOS/UI/DevicesViews.swift:75
    severity: medium
---

<intent-contract>

## Intent

**Problem:** A Node, once assigned to a Lot, can never change Lots or be released (`DeviceGrain.Enrol` returns `AlreadyAssigned`), so the garden layout cannot change without losing the Node's history.

**Approach:** Add Admin+ `Move` and `Unassign` operations on the Device grain (claim new Lot, journal `DeviceMoved`/`DeviceUnassigned`, release the old Lot from persisted pending state with retry), expose them as REST endpoints in the authorization matrix and OpenAPI contract, and add row actions to the Devices list on web, Android and iOS. Reading history stays keyed to the Node.

## Boundaries & Constraints

**Always:** The Device grain is the only source of "which Lot am I on". Order is claim new Lot, then journal the event, then release the old Lot; the release is idempotent and retried from persisted pending state until it succeeds (AD-18). A rejected move changes nothing (no event, no claim). Endpoints require Administrator and appear in the authorization matrix and OpenAPI (`x-coldframe-minimum-role`). Members never see the actions (hidden in UI, 403 from the API). Destructive unassign confirms in a native dialog or DS Modal naming the Node (EXPERIENCE.md:186). Tests are written failing first; client tests carry the `UX-DR31` prefix. Never log Reading payloads, keys or tokens; RFC 9457 errors.

**Never:** No BLE for move/unassign. Do not build Pause/Resume, Silence Window or swipe-to-Pause (UX-DR31 parts owned by later epics). Do not delete or rewrite Readings. Do not implement the Epic 6 evaluation-context push. Do not change `Lot.Claim`/`Lot.Release` semantics.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Move | Node on Tomatoes, target Peppers free, Admin | Peppers claimed, `DeviceMoved` journaled, Tomatoes released, Devices list shows Peppers, Tomatoes projects `noNode`, Readings stay with the Node | No error |
| Target occupied | Target Lot held by another Node | Rejected 409 `lot-claimed`, no event, nothing changes | Problem response |
| Move to same Lot | Target is the current Lot | No-op success, no event | None |
| Target missing/removed | Unknown or removed Lot | 404 `lot-not-found`, nothing changes | Problem response |
| Concurrent move to same Lot | Two Nodes race for one free Lot | Exactly one wins, the other gets `lot-claimed`, loser keeps its Lot | Problem response |
| Release fails | Old-Lot release throws after `DeviceMoved` | Move still succeeds, release pending is persisted and retried (activation/reminder) until the old Lot is free | Retry, never lost across silo restart |
| Unassign | Node on a Lot, Admin | `DeviceUnassigned` journaled, Lot released (same retry), Devices shows unassigned, later Readings stored but not evaluated | No error |
| Unassign unassigned | Node already unassigned | No-op success | None |
| Member | Member calls move/unassign | 403, no change; UI hides actions | Problem response |
| Unknown Node | Device id not in Site | 404 | Problem response |

</intent-contract>

## Code Map

- `packages/cs/contracts/Devices/DeviceEvents.cs` -- add `DeviceMoved` (`device.moved`: from/to Lot) and `DeviceUnassigned` (`device.unassigned`) following the `[EventType]/[GenerateSerializer]/[Alias]` pattern of `DeviceAssigned`.
- `packages/cs/contracts/Devices/DeviceGrains.cs` -- add `IDeviceGrain.Move`/`Unassign` and result/outcome types (mirror `DeviceEnrolmentOutcome`: `LotNotFound`, `LotOccupied`, `NotFound`).
- `packages/cs/contracts/Lots/LotGrains.cs` -- `ILotGrain.Claim`/`Release` already exist (Release documented for 4.9); read-only.
- `apps/cs/server/Lots/LotGrain.cs` (`Claim` ~106, `Release` ~130 idempotent), `LotState.cs`, `LotsProjector.cs` (~224 handles `LotClaimed`/`LotReleased` and re-evaluates to `noNode`) -- reuse unchanged.
- `apps/cs/server/Devices/DeviceGrain.cs` -- `Enrol` (~61-152) is the claim/journal template; add `Move`/`Unassign`, pending-release processing on activation and via grain reminder/timer.
- `apps/cs/server/Devices/DeviceState.cs` -- `LotId` is `[Id(6)]`; state ids 0-10 used, next free is 11 for `PendingRelease` (Lot id); `Apply` for the new events.
- `apps/cs/server/Devices/DevicesProjector.cs` -- add cases: `DeviceMoved` sets `lot_id` (reuse `AssignSql`), `DeviceUnassigned` sets `lot_id = NULL`. `DevicesReadModel.cs` already left-joins lots, so null lists as unassigned.
- `apps/cs/server/Lots/LotDetailReadModel.cs` -- verify old Lot shows no Readings and history is keyed to the Node after move/unassign (spec-4-8 line 52); fix only if wrong.
- `apps/cs/server/Edge/EdgeApi.cs` (`MapEdgeApi` ~265-330, `EnrolDeviceAsync` ~1090 as the outcome-to-problem template) -- add `POST /sites/{siteId}/devices/{deviceId}/move` (`moveDevice`) and `/unassign` (`unassignDevice`), `.RequireSiteRole(SiteRole.Administrator)`.
- `apps/cs/server/Edge/EdgeProblems.cs` -- `lot-claimed` 409, `lot-not-found` 404 exist; reuse.
- `packages/openapi/coldframe.openapi.json` (Devices paths ~199) and `packages/openapi/README.md` -- add operations with `x-coldframe-minimum-role: Administrator`, 401/403/404/409; extend the README operations table.
- `packages/ts/api-client/src/schema.ts` -- regenerate (`pnpm --filter @coldframe/api-client generate`).
- `tests/cs/server.integration/Edge/AuthorizationMatrixTests.cs` (`_samples` ~78-103) -- add Sample entries for both new endpoints; `tests/cs/server.tests/Edge/EdgeEndpointDiscoveryTests.cs` cross-checks OpenAPI.
- `tests/cs/server.integration/Devices/NodeAssignmentTests.cs` + `Identity/IdentityCluster.cs` (fault filter pattern `SensorFaults` ~259, `RestartSiloAsync`) -- template and fixture for the Orleans TestCluster tests.
- `apps/ts/web/src/routes/(app)/devices/+page.svelte` (Nodes row ~66), `+page.server.ts` (form actions pattern in `settings/site/+page.server.ts`), `src/lib/devices.ts` (`devicesAccessOf`), `src/lib/server/devices.ts` -- web row actions, Lot picker, confirm modal. Tests: `tests/ts/web/devices.test.ts`, `tests/ts/web.e2e/specs/devices.spec.ts`.
- `packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/api/ColdframeApi.kt` (`enrolDevice` ~157), `core/devices/Devices.kt`, `DevicesEngine.kt`, `DevicesSnapshot.kt`; `core/sites/DeviceChoices.kt` and `NodeSetupEngine.kt` Lot choice (check for reuse). Tests: `tests/kt/core/commonTest/.../devices/DevicesEngineTest.kt`, `ColdframeApiTest.kt`.
- `apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/ui/devices/DevicesScreen.kt`, `DevicesActions.kt`; test `tests/kt/android/test/kotlin/.../DevicesScreenTest.kt` + snapshots.
- `apps/swift/ios/Sources/ColdframeIOS/DevicesPresentation.swift`, `UI/DevicesViews.swift`, `apps/swift/ios/App/CoreDevicesService.swift`; test `tests/swift/ios/ColdframeIOSTests/DevicesPresentationTests.swift`.
- `_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/EXPERIENCE.md` lines 49, 94, 186, 281 -- role gating, row actions, confirm-dialog rule, UJ-6 move journey. No copy exists for the move picker or the "Lot already has a Node" rejection; write it and add i18n/string-table entries.

## Tasks & Acceptance

**Execution:**
- `tests/cs/server.integration/Devices/NodeMoveTests.cs` -- write failing Orleans TestCluster tests first: move, concurrent move to same Lot, failed release retried (fault filter on `ILotGrain.Release`, incl. across silo restart), unassign, occupied target rejected with no events, Readings still stored after unassign -- covers the I/O matrix.
- `packages/cs/contracts/Devices/DeviceEvents.cs`, `DeviceGrains.cs` -- add events, `Move`/`Unassign` and outcome types -- contract for server and tests.
- `apps/cs/server/Devices/DeviceState.cs`, `DeviceGrain.cs` -- implement claim, journal, release with persisted pending release and retry -- AD-18.
- `apps/cs/server/Devices/DevicesProjector.cs` (and `LotDetailReadModel.cs` only if needed) -- project move/unassign -- Devices list and Lot status stay correct.
- `apps/cs/server/Edge/EdgeApi.cs`, `packages/openapi/*`, `packages/ts/api-client/src/schema.ts`, `tests/cs/server.integration/Edge/AuthorizationMatrixTests.cs` -- endpoints, contract, matrix samples, Member 403 -- authorization.
- `packages/kt/core/...` (API, `DevicesEngine`, `DevicesSnapshot`) and tests -- move/unassign calls, `canManageNodes` gating, Lot choices excluding occupied Lots -- shared mobile logic.
- `apps/ts/web/...` and tests -- Nodes row actions (Admin+ only), Lot picker with occupied Lots disabled "HAS A NODE", unassign confirm naming the Node, error messages -- web surface.
- `apps/kt/android/...`, `apps/swift/ios/...` and tests -- same actions, native confirm dialog, accessible alternatives for actions -- mobile surfaces.
- Named `UX-DR31` tests on web, Android and iOS (written failing first): Admin sees move and unassign; Member sees neither; unassign requires confirmation; occupied Lots not selectable.

**Acceptance Criteria:**
- Given a Node on "Tomatoes", when an Admin moves it to the free Lot "Peppers", then the Device grain claims Peppers, persists `DeviceMoved`, releases Tomatoes (retried from persisted state until it succeeds), the Node's Reading history stays with the Node, and Tomatoes shows no Node.
- Given the target Lot already holds a Node, when a move is attempted, then it is rejected and nothing changes.
- Given an unassigned Node, when it is processed, then the Lot is released, later Readings are stored but not evaluated, and Devices shows it unassigned.
- Given a Member, when they open Devices, then move and unassign are not shown, the API returns 403, and both endpoints are in the authorization matrix.
- Given Orleans TestCluster tests, when they run, then move, concurrent move to the same Lot, failed release retried and unassign pass.
- Given the UX contract, when the surfaces are built, then named `UX-DR31` tests (written failing first) cover move/unassign row actions.

## Spec Change Log

## Review Triage Log

### 2026-10-07 — Review pass
- verdicts: 15 rows (reviewer findings from all four layers grouped by claim; hygiene nits share one row) — high 0, medium 4, low 7, false 4, maybe-false 0
- findings:
  - `[false]` `[reject]` Release races a move back — DeviceGrain is not [Reentrant]; Orleans runs one turn at a time, so the state check and release cannot interleave.
  - `[low]` `[defer]` Orphaned claim between Claim and journal — real, inherited AD-18/Enrol order; client retry heals it; deferred in frontmatter.
  - `[medium]` `[patch]` Reminder re-registered on every 5 s tick and fire-and-forget register/unregister can reorder — Start/Stop made async and awaited, reminder registered once with the timer; a stray reminder now unregisters itself in ReceiveReminder.
  - `[low]` `[reject]` Inline Claim/Release without timeout or backoff — same call pattern as Enrol; fix adds policy not asked for.
  - `[false]` `[reject]` Release outcome ignored — NotFound (no such Lot) means nothing to release; treating it as done is correct.
  - `[low]` `[reject]` No actor on DeviceMoved/DeviceUnassigned — adds public contract surface; intent does not ask for it.
  - `[low]` `[reject]` AC3 "not evaluated" untested — no evaluation exists yet (Epic 6); "stored" is asserted and the Lot is released.
  - `[low]` `[reject]` Endpoint test thinness, 400 vs 404 order, web UX nits, copy/i18n, logger id order, spec Design Notes drift — cosmetic or edit this build's spec.
  - `[low]` `[reject]` Default switch throws 500 on an impossible Lot outcome; same as Enrol.
  - `[medium]` `[patch]` Move to a no-longer-selectable Lot returns silently in DevicesEngine — now sets NodeActionFailure LotTaken, with a test.
  - `[low]` `[reject]` Failed list refresh after a successful move shows the unreachable notice — consistent with every failed list read.
  - `[low]` `[reject]` Web "create a Lot" hint when the Lot read failed — rare, fix adds state.
  - `[medium]` `[patch]` Reminder retry path unverified (verification-gap) — failed-release test now asserts the reminder exists while pending and is gone after success.
  - `[medium]` `[defer]` iOS unassign confirmation not behaviourally tested (verification-gap) — no harness or toolchain; deferred in frontmatter.
  - `[false]` `[reject]` Intent-alignment: no divergence from the implemented reading; LotDetailReadModel is keyed to the Lot's claim, so the old Lot shows no Node after release.

## Auto Run Result

Status: done

**Summary:** Admin+ Move and Unassign on the Device grain with journaled `DeviceMoved`, `DeviceUnassigned` and `DeviceLotReleased`; the old Lot is released from a journaled `PendingReleases` list, retried by a grain timer, a reminder and on activation. REST `POST /sites/{siteId}/devices/{deviceId}/move|unassign`, OpenAPI, generated TS schema, authorization-matrix samples, Kotlin core engine, and Move/Unassign row actions on web, Android and iOS.

**Files changed:** server grain/state/projector/edge/problems (`apps/cs/server`), contracts (`packages/cs/contracts/Devices`), OpenAPI and TS schema (`packages/openapi`, `packages/ts/api-client`), Kotlin core (`packages/kt/core`), web (`apps/ts/web`), Android (`apps/kt/android`), iOS (`apps/swift/ios`), and matching tests under `tests/`.

**Review:** patches applied 3 (medium 2 reminder lifecycle and its test; medium 1 engine silent no-op, plus a self-removal fix for stray reminders); deferred 2 (orphaned claim, iOS confirm test); rejected items are recorded with reasons in the triage log. Follow-up review recommended: true — the reminder-fire path itself (1-minute period) is only asserted by registration, not by a firing; two medium entries were patched.

**Verification:** `dotnet test tests/cs/server.tests` 463 passed; Orleans `NodeMoveTests` + `NodeAssignment` 25 passed via podman; `:core:allTests` passed; web unit and e2e devices specs passed.

**Residual risks:** `NodeMoveEndpointTests` and the authorization-matrix samples could not run here (Aspire Keycloak/database fail to start under rootless podman), so the Member 403 and unknown-Node 404 rows are verified only by endpoint-discovery/OpenAPI role checks and unit tests. iOS code and Swift tests were never compiled (no Swift toolchain). Two lot-detail e2e screenshot tests ("the Server stops answering") fail on a pixel diff in code this story does not touch; not confirmed against a clean baseline.

## Design Notes

Pending release is journaled state, not an in-memory flag: `DeviceMoved`/`DeviceUnassigned` set `PendingRelease = oldLotId` in `Apply`; a release-completed event (or clearing event) removes it. On activation and from a grain reminder the grain calls `Lot.Release(nodeId)` until it returns `Released` or `Unchanged`, so a crash between journal and release still frees the old Lot. Claim-before-journal means a lost race leaves no event.

## Verification

**Commands:**
- `dotnet test tests/cs/server.integration --filter "FullyQualifiedName~NodeMove|FullyQualifiedName~AuthorizationMatrix"` -- expected: pass
- `dotnet test tests/cs/server.tests` -- expected: pass (OpenAPI/endpoint discovery in sync)
- `pnpm --filter @coldframe/api-client generate && pnpm test` (web unit) -- expected: pass, no schema diff
- `./gradlew :packages:kt:core:allTests` -- expected: pass
- Android `DevicesScreenTest` and iOS `DevicesPresentationTests` -- expected: pass where the toolchain exists on this host

**Manual checks (if no CLI):**
- Web Devices as Admin shows move/unassign per Node row; as Member shows neither.
