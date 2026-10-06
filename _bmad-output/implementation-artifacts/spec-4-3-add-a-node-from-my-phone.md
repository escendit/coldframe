---
title: 'Story 4.3: Add a Node from my phone'
type: 'feature'
created: '2026-10-06'
baseline_revision: 'bb70a216fc8a43cb337407044ae8a6d1801db97b'
status: 'awaiting-operator'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-3-6-add-a-hub-from-my-phone.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-4-2-node-pairing-setup-mode-enrolment-and-lot-assignment.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/EXPERIENCE.md'
warnings: ['oversized']
deferred:
  - summary: >-
      On iOS the Devices header now has two ghost actions and no handling for the largest text sizes; whether the heading and both buttons fit is unknown.
    evidence: |-
      DevicesViews.swift adds a second .primaryAction toolbar button. Android stacks the two from font scale 1.5; the iOS Devices tests are presentation-only and RenderTests only assert that an image renders. Settle it by rendering the Devices tab for an Owner at .accessibility5 on a Mac or iPhone.
    location: >-
      apps/swift/ios/Sources/ColdframeIOS/UI/DevicesViews.swift (toolbar)
    severity: medium (unverified)
  - summary: >-
      On iOS, Add a Node opened from "Hub is online" may lose keep-awake if the Hub flow's onDisappear runs after the Node flow's onAppear.
    evidence: |-
      SetupFlowEffects sets isIdleTimerDisabled in onAppear and clears it in onDisappear; the root swaps AddHubFlowView for AddNodeFlowView in one update. SwiftUI's ordering of the two callbacks decides it. Settle it on an iPhone: open Add a Node from the Hub outcome and watch whether the screen dims and locks.
    location: >-
      apps/swift/ios/Sources/ColdframeIOS/UI/HubSetupViews.swift (SetupFlowEffects)
    severity: medium (unverified)
  - summary: >-
      No test joins the Hub outcome's Add a Node action to an open Node flow through the platform wiring.
    evidence: |-
      HubSetupEngineTest passes its own onAddNode lambda; the join is onAddNode = { nodeSetup.open(it) } in AndroidSignIn.kt and IosSignIn.kt, forwarded by SitesWiring.hubSetup, and every hop defaults to {}. Dropping the argument compiles and Add a Node closes to the Garden as before this story, with every test green. Same shape as DW-54.
    location: >-
      packages/kt/core/src/androidMain/.../signin/AndroidSignIn.kt; packages/kt/core/src/iosMain/.../signin/IosSignIn.kt; packages/kt/core/src/commonMain/.../sites/SitesWiring.kt
    severity: medium
  - summary: >-
      The iOS entry points of Add a Node (Devices action, no-Node tile) are only checked by render tests that assert an image exists.
    evidence: |-
      LotGrid's Button, the Devices toolbar item and the root's onAddNode: nodeSetupActions.open each default to a no-op closure; no Swift test passes or observes onAddNode. Removing the root's argument compiles and both entry points do nothing. Taps are covered on Android only. Needs a view-interaction harness or the tap decision in Linux-compiled code.
    location: >-
      apps/swift/ios/Sources/ColdframeIOS/UI/{Screens,DevicesViews,SiteSettingsViews}.swift
    severity: medium
  - summary: >-
      The app-target wiring of Add a Node (MainActivity, ColdframeApp, CoreNodeSetupService, canAddNode in CoreLotsService and CoreDevicesService) can be dropped or mis-mapped without a test failing.
    evidence: |-
      ColdframeRoot, ColdframeRootView, ShellModel and the Lots and Devices presentations default the new parameters to closed, none or false. Deleting MainActivity's two nodeSetup lines, or canAddNode: snapshot.canAddNode in CoreLotsService, keeps every check green. CoreNodeSetupService maps 43 snapshot fields by hand and apps/swift/ios/App has no tests. Extends DW-54.
    location: >-
      apps/kt/android/.../MainActivity.kt; apps/swift/ios/App/{ColdframeApp,CoreNodeSetupService,CoreLotsService,CoreDevicesService}.swift
    severity: medium
operator_actions:
  - "On a Mac with Xcode 26.6, run swift build && swift test && swift format lint --strict -r . at the repo root and fix any compile or test failure in the never-compiled SwiftUI files (UI/NodeSetupViews.swift, the refactored UI/HubSetupViews.swift, UI/DevicesViews.swift, UI/SiteSettingsViews.swift, UI/Screens.swift and RenderTests.swift at .accessibility5, light and dark)."
  - "On that Mac, in apps/swift/ios, run xcodegen generate and xcodebuild build -scheme Coldframe -destination 'generic/platform=iOS Simulator' CODE_SIGNING_ALLOWED=NO, and confirm App/CoreNodeSetupService.swift, CoreLotsService.swift and CoreDevicesService.swift compile against ColdframeCore (check the Kotlin/Native names of IosNodeSetup and the canAddNode snapshot fields)."
  - "With a dev-mode Node from the Story 4.2 bench (docs/bench/node-setup-checklist.md) and the Android app signed in as an Owner against the real Server, long-press the Node's setup button, run Add a Node from Devices, pick a free Lot and confirm the outcome reads '<Lot> has a Node' and the Lot's Garden tile is no longer 'no Node'."
  - "Repeat the Add a Node run on an iPhone, once from a 'no Node' tile (the Lot must be preselected on step 4) and once from Add a Node on 'Hub is online'; in the second run confirm the screen stays awake through the flow."
  - "On both phones, walk the error cases with a real Node: Bluetooth off, a wrong setup code, waiting more than 3 minutes before entering the code (expect '<ID> stopped listening'), and a second Node put on a Lot that got its Node meanwhile (expect the Lot-taken notice and a retry with another Lot without pressing the button again)."
  - "Run the Android flow with TalkBack at font scale 2 and the iOS flow with VoiceOver at the largest Dynamic Type size; confirm the candidate, wrong-code, Lot-taken and outcome announcements are spoken once, and that the Devices header shows 'Add a Hub' and 'Add a Node' without clipping the heading."
---

<intent-contract>

## Intent

**Problem:** A Node can only be paired with the Rust bench client. The mobile apps have no Add a Node flow, so Simon cannot put a Node on a Lot from his phone (FR2), and the Garden's *no Node* tiles, the Devices tab and "Hub is online" lead nowhere.

**Approach:** Add a `NodeSetupEngine` to the KMP core that reuses the Add a Hub setup client (radio port, session crypto, framing, enrolment API) and drives the five steps of Story 4.3: press the button, pick the Node, setup code, Lot picker, outcome. Render it in Jetpack Compose and SwiftUI, and open it from Devices, a *no Node* tile and the Hub outcome's Add a Node action, for Administrators and Owners only.

## Boundaries & Constraints

**Always:**
- **Steps** (Story 4.3 order, counter "01 / 05" to "05 / 05"): 1 Press (instruction), 2 Scan, 3 Code, 4 Lot, 5 Outcome. Cancel on step 1, Back on 2 and 3 (one step back; leaving step 3 disconnects and rescans). Back on step 4 and system back after a Node is selected ask "Stop setting up Node 7C19? Nothing is saved on the Node."
- **Engine pattern** as `HubSetupEngine`: one `StateFlow<NodeSetupState>`, actions as methods, a flat `NodeSetupSnapshot` and `IosNodeSetup` facade for Swift, enum values crossing as lowercase-first keys. The shells hold UI only.
- **Site and Role:** the flow runs on one Site: the one passed to `open`, else the current Site. `open` does nothing unless the caller is Administrator or Owner there. It closes when Sites stop being Ready, and on any 401.
- **Scan:** starts on entering step 2 while the radio is Ready. It lists only adverts named `Coldframe Node XXXX`, strongest first. "Pressed just now" marks the one candidate that was first heard most recently. After 30 s with none: the no-Node notice, scanning continues. A new candidate is announced politely once.
- **BLE session** (one per code attempt, on Continue of step 3): connect → hello → `IdentityRequest`. A reply that does not open is the wrong-code case. `Identity.kind` must be `NODE`. Then `GET /enrolment-key` (recompute and check the fingerprint) → `EnrolmentRequest` → `EnrolmentResponse` whose `device_id` equals the Identity's. The engine keeps `enc`/`ciphertext` in memory, disconnects, and only then shows the accepted chip. No `SiteBinding`, `WifiScanRequest` or `WifiConfig` is ever sent to a Node.
- **Lot step** needs no BLE. It reads `GET /sites/{siteId}/lots`; a Lot is selectable only when its status is `noNode`; any other shows "Has a Node" and cannot be picked. A Lot passed to `open` is preselected if selectable. "+ New Lot" opens an inline name field (same validation and copy as Create Lot); creating it reloads the list and selects it. The primary button reads "Put 7C19 in Tomatoes".
- **Assign:** `POST /sites/{siteId}/devices` with `kind: "node"`, the kept `enc`/`ciphertext`, `lotId`, and an `Idempotency-Key` created once per Device and Site and reused on every retry. 201 → outcome "Tomatoes has a Node", then the Lots and Devices engines reload.
- **Security:** the setup code is never sent, logged or printed (`toString` redacts). Keys and sealed payloads are never logged.
- **Copy:** every string in `strings.xml` and `Localizable.xcstrings` under matching `add_node_*` keys (plus `devices_add_node`), passing the existing copy rules. No animations, spinners, toasts or long-press.
- **Tests first** (AD-24): each matrix row and UX-DR 37, 38, 39, 67, 94, 105 gets a failing test named after its id before the code; new ids join `CoverageTest.storyIds` and `iosIds`.

**Never:**
- No Server, OpenAPI, proto or firmware change. No web change.
- No Nodes section in Devices, no Lot detail, no move/unassign (Stories 4.8, 4.9). No Sensors, CALIBRATE action or paused-Site note on the outcome (Stories 4.4–4.6, Epics 5 and 8).
- No battery % or Sensor count on the candidate tile: neither the advert nor `Identity` carries them.
- No change to what a Member sees: no Add a Node action, *no Node* tiles stay non-interactive.
- The first-run "Add a Node" tile stays non-actionable (Story 4.3 names three entry points; the tile's state needs Hub/Node knowledge the Garden does not have).
- `HubSetupEngine` behaviour changes only in what Add a Node does after closing.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Happy path | Admin opens, Continue, picks "Node 7C19", code, picks free Lot "Tomatoes", confirms | Steps 01→04, accepted chip after the sealed key is held; 201 → "Tomatoes has a Node", action Done closes; Lots and Devices reload | — |
| Bluetooth off / denied | radio not Ready on step 2 | Notice "Coldframe needs Bluetooth to find the Node." + Open Settings | Scan resumes when Ready |
| No Node in 30 s | no matching advert | Notice "No Node in range yet. Hold its setup button for 3 seconds; it listens for 3 minutes." | Keeps scanning |
| Two Nodes | A heard, then B | Both listed strongest first; only B shows "Pressed just now" | Order changes not announced |
| Wrong code | first sealed reply fails to open | "That setup code doesn't match Node 7C19. Check the serial console." under the field, text kept, assertive | Disconnect; next Continue reconnects |
| Node stopped listening | `connect` fails | Error outcome "7C19 stopped listening" / "Press its setup button again." → Try again (step 1) | Assertive |
| Lost connection | link drops or no reply mid-session | "Lost the connection to Node 7C19. Nothing was saved." → Try again (step 1) | Assertive |
| Not a Node / `SetupError` | `Identity.kind` ≠ NODE, error reply, malformed | Error outcome "Node 7C19 refused the setup." → Close | Disconnect |
| Enrolment key unreadable | `GET /enrolment-key` fails | Error outcome "Can't reach your Server" → Try again (step 1) | Disconnect |
| Fingerprint mismatch | hash ≠ fingerprint, or Node says `FINGERPRINT_MISMATCH` | Error outcome "The Server's enrolment key doesn't check out." → Close | Nothing enrolled |
| Lots unreadable | list fails on step 4 | Notice + Try again on step 4; sealed key kept | — |
| Lot taken meanwhile | POST → 409 `lot-claimed` | Stay on step 4: "Tomatoes got a Node meanwhile. Pick another Lot."; list reloads; selection cleared; assertive | Retry with another Lot, no new BLE session |
| Lot gone | POST → 404 | Stay on step 4: "That Lot is gone. Pick another Lot."; list reloads | — |
| Server unreachable on assign | POST fails without an answer, 5xx, 422 or 400 | Stay on step 4: "Can't reach your Server. Nothing was assigned."; the button retries with the same key | — |
| Node already assigned | 409 `device-assigned` | Error outcome "Node 7C19 is already in another Lot." → Close | — |
| On another Site / not allowed | 409 `device-on-another-site` / 403 | Error outcome with the matching copy → Close | — |
| Leave mid-flow | Back on step 4, or Cancel/system back after selection | Confirm dialog; confirming disconnects and closes; nothing was sent to the Server | — |
| Hub outcome | "Add a Node" on "Hub is online" | Hub flow closes, Node flow opens on step 1 for the Hub's Site | No-op if not Admin+ there |

</intent-contract>

## Code Map

- `packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/setup/HubSetupEngine.kt` -- the model. Reuse the shapes of `open/close` (:112-149), `back/leave` (:165-197), scan (`startScan`, `onAdvert` :216-277), `submitCode` (:305-353, wrong-code catch), `loadKey` fingerprint check (:454-488), enrolment exchange and POST (:516-567), `work` (:732), `ensureLink`/`onLinkClosed`/`disconnect`/`send`/`receive`/`exchange` (:748-830), `keyFor` (:857), `announcement` (:879), `outcomeAction` (:661; `AddNode` only closes today), companion constants.
- `.../setup/HubSetupState.kt` -- `SignalStrength`, `CodeForm` (redacting `toString`), `CodeError`, `SetupAnnouncement` shape, `SetupOutcome` (`primary`/`secondary`/`stoppedStep`). Reuse `SignalStrength`, `CodeForm`, `CodeError`, `RadioState`; give the Node its own step, outcome, action and announcement enums.
- `.../setup/{SetupRadio.kt,SetupSession.kt,Framing.kt,SetupCode.kt,KableSetupRadio.kt}` -- reuse unchanged except `SetupGatt` gains `NODE_NAME_PREFIX = "Coldframe Node "`. `connect` throws `SetupLinkException` when it cannot connect.
- `.../setup/{HubSetupSnapshot.kt,../../iosMain/.../setup/IosHubSetup.kt}` -- flat snapshot, `Enum.key()`, `outcomeActionOf`, facade pattern.
- `.../api/{ApiResult.kt,ApiDtos.kt:69,ColdframeApi.kt:180-216,240}` -- `EnrolDeviceRequestDto` has no `lotId`; `failureOf` maps every 409 other than `device-on-another-site` to `LotClaimed`, so `device-assigned` is misread today. `DeviceDto` has no `lotId`.
- `.../lots/{LotsEngine.kt:22-41,216-262,Lots.kt}` -- `LotsApi.listLots/createLot`, `CreateSiteForm.validate` for names, `LotStatus.NoNode`, `LotStatus.fromServer`; `LotsEngine.load()` and `DevicesEngine.load()` for the reload.
- `.../sites/SitesWiring.kt:57-64`, `androidMain/.../signin/AndroidSignIn.kt:56-63`, `iosMain/.../signin/IosSignIn.kt:52-59` -- wiring.
- `.../devices/Devices.kt:54,69` -- `canAddHub` (Admin+); Add a Node uses the same rule.
- `tests/kt/core/commonTest/.../setup/{SetupFakes.kt,HubSetupEngineTest.kt,HubSetupSnapshotTest.kt}` -- `FakeHub` (:43, real session crypto), `FakeLink`, `FakeSetupRadio`; `tests/kt/core/commonTest/.../api/ColdframeApiTest.kt`, `jvmTest/.../api/OpenApiContractTest.kt`.
- `packages/openapi/coldframe.openapi.json:196-230,667-723` -- `enrolDevice` with optional `lotId`, problem types.
- `packages/rs/setup/src/session.rs:306-330` -- the Node profile: Wi-Fi messages refused; enrolment needs no prior binding.
- `apps/kt/android/.../ColdframeRoot.kt:48-64,146-171`, `MainActivity.kt:37-72` -- flow shown when `hubSetup.open`; add the Node flow the same way.
- `apps/kt/android/.../ui/setup/` -- reusable as-is: `SetupFlowShell` (:43), `SetupCodeField` (:42), `OutcomeCopy`, `readingTimeMillis`. Hub-typed: `CandidateTile(candidate: HubCandidate…)` (:53), `SetupOutcomeScreen` (:137), private `AnnouncementRegion` (`AddHubFlow.kt:423-458`), inline leave dialog (:134-161), permission/keep-awake/back effects (:77-94), `openBluetoothSettings` (:499).
- `apps/kt/android/.../ui/shell/AppShell.kt:168-176` (Devices header action), `ui/sites/{GardenScreen.kt:60-67,249,LotTile.kt:49,87}` (tiles not clickable, no role passed), `res/values/strings.xml:128-131,140-143,161-273`.
- `tests/kt/android/test/.../{AddHubFlowTest.kt:62-156,SnapshotTest.kt:94-133,231-246,CoverageTest.kt:9-111,StringsTest.kt,SourceScanTest.kt,DevicesScreenTest.kt,SitesScreensTest.kt}`, baselines `tests/kt/android/snapshots/`.
- `apps/swift/ios/Sources/ColdframeIOS/HubSetupPresentation.swift` (:84 candidate, :274 outcome, :336 announcement, :377 presentation, :525-541 shell helpers, :629 `readingTime`, :635 service protocol), `UI/HubSetupViews.swift` (:11 actions, :92 flow view with private shell/leave/announce logic, :287 `CandidateTileView`, :355 `SetupCodeFieldView`, :667 `OutcomeView`), `UI/Screens.swift:253-394` (root switch :318, `ShellModel`), `UI/DevicesViews.swift:27,58-64`, `UI/SiteSettingsViews.swift:219,254` (`LotGrid`, `LotTile`), `LotsPresentation.swift:18-55` (`isTappable = false`), `DevicesPresentation.swift:62-81`, `L10n.swift`, `Resources/Localizable.xcstrings`, `App/{ColdframeApp,CoreHubSetupService,CoreSignInService}.swift`.
- `tests/swift/ios/ColdframeIOSTests/{HubSetupPresentationTests.swift:8-56,355-378,RenderTests.swift:168,LotsPresentationTests.swift:86,DevicesPresentationTests.swift,CatalogueTests.swift}`.
- `apps/kt/README.md`, `apps/swift/README.md`, `packages/kt/README.md` -- flow documentation; `_bmad-output/implementation-artifacts/deferred-work.md` -- DW entries (next id DW-55).

## Tasks & Acceptance

**Execution:**
- `packages/kt/core/src/commonMain/.../api/{ApiResult.kt,ApiDtos.kt,ColdframeApi.kt}` -- add `EnrolDeviceRequestDto.lotId: String? = null` (omitted when null), `DeviceDto.lotId: String? = null`, `ApiFailure.DeviceAssigned` and `PROBLEM_DEVICE_ASSIGNED`; other 409s stay `LotClaimed`. Extend `ColdframeApiTest` and `OpenApiContractTest` first -- the Node body and the misread 409.
- `packages/kt/core/src/commonMain/.../setup/{NodeSetupState.kt,NodeSetupEngine.kt,NodeSetupSnapshot.kt,SetupRadio.kt}` -- `NodeSetupStep {Press, Scan, Code, Lot, Outcome}`, `NodeCandidate(peripheralId, shortId, rssi, pressedJustNow)`, `LotChoice(id, name, selectable)`, `LotForm` (choices or null while loading, selected id, new-Lot field with `NameError`, working flags, notice `{Unreachable, Certificate, Unexpected, LotTaken, LotGone, AssignFailed}` with the Lot name), `NodeOutcomeKind {Assigned, StoppedListening, LostConnection, NodeRefused, ServerUnreachable, FingerprintMismatch, AlreadyAssigned, OnAnotherSite, NotAllowed}`, `NodeOutcomeAction {Done, StartOver, Close}`, `stoppedStep` = the step it stopped on, announcements `{CandidateFound, WrongCode, LotTaken, Error, Assigned}`; the engine `NodeSetupEngine(radio, api: EnrolmentApi, lots: LotsApi, sites, scope, onAssigned, newKey, newSession)` with `open(siteId: String? = null, lotId: String? = null)`, `close`, `recheckRadio`, `announcing`, `back`, `leave`, `confirmLeave`, `stayInFlow`, `continueFromPress`, `select`, `continueFromScan`, `setCode`, `submitCode`, `continueFromCode`, `retryLots`, `chooseLot`, `openNewLot`, `setNewLotName`, `createLot`, `assign`, `outcomeAction` -- implements every matrix row.
- `packages/kt/core/src/commonMain/.../setup/HubSetupEngine.kt`, `.../sites/SitesWiring.kt`, `androidMain/.../signin/AndroidSignIn.kt`, `iosMain/.../signin/IosSignIn.kt`, `iosMain/.../setup/IosNodeSetup.kt` -- `HubSetupEngine` takes `onAddNode: (siteId: String) -> Unit = {}`, called after `close()` for `OutcomeAction.AddNode` with the Hub's Site; `SitesWiring.nodeSetup(...)` wires `onAssigned` to `lots.load()` and `devices.load()` and the Hub's `onAddNode` to `nodeSetup.open(siteId)`; expose `nodeSetup` on both platforms; the iOS facade mirrors `IosHubSetup`.
- `tests/kt/core/commonTest/.../setup/{SetupFakes.kt,NodeSetupEngineTest.kt,NodeSetupSnapshotTest.kt}` -- a `FakeNode` (kind NODE, answers Wi-Fi messages with `UNEXPECTED_MESSAGE`, records every message it received) beside `FakeHub`; one engine test per matrix row and step transition, asserting that the Node never receives a binding or Wi-Fi message, that the POST carries the Node's `enc`/`ciphertext` byte-identical with `lotId` and the same key on a retry, and that no POST happens before `assign`; snapshot key tests pinning the Swift raw values; `toString` redaction.
- `apps/kt/android/.../ui/setup/{AddNodeFlow.kt,NodeSetupActions.kt,LotPickerRow.kt,CandidateTile.kt,SetupOutcome.kt,AddHubFlow.kt}` -- the Compose flow. Extract the announcement region, leave dialog and outcome layout into shared composables that take resolved strings, and make `CandidateTile` take name, signal label, badge and description, so both flows use them; Hub rendering stays pixel-identical (its baselines must not change).
- `apps/kt/android/.../{MainActivity.kt,ColdframeRoot.kt}`, `ui/shell/AppShell.kt`, `ui/sites/{GardenScreen.kt,LotTile.kt}`, `res/values/strings.xml` -- show `AddNodeFlow` when `nodeSetup.open`; Devices header gains "Add a Node" beside "Add a Hub" (Admin+); a *no Node* tile is a button for Admin+ that opens the flow with its Lot (role `Button`, same spoken label), and stays as today for a Member.
- `tests/kt/android/test/.../{AddNodeFlowTest.kt,SnapshotTest.kt,CoverageTest.kt,DevicesScreenTest.kt,SitesScreensTest.kt}` + baselines -- UX-DR-named tests for every step, notice and outcome, the three entry points, the Member case, the leave dialog and announcements; Roborazzi light + dark at font scale 2 for steps 1–4, the success outcome, one error outcome and the Lot-taken notice; re-record the Devices and Garden baselines that change.
- `apps/swift/ios/Sources/ColdframeIOS/{NodeSetupPresentation.swift,L10n.swift,LotsPresentation.swift,DevicesPresentation.swift}`, `Resources/Localizable.xcstrings`, `UI/{NodeSetupViews.swift,HubSetupViews.swift,Screens.swift,DevicesViews.swift,SiteSettingsViews.swift,SitesViews.swift}`, `App/{CoreNodeSetupService.swift,ColdframeApp.swift,CoreSignInService.swift}` -- the same flow in SwiftUI: `NodeSetupPresentation` + `NodeSetupService`, the flow view reusing `CandidateTileView`, the code field and `OutcomeView` (generalised where Hub-typed), root switch, Devices action, tappable *no Node* tile for Admin+.
- `tests/swift/ios/ColdframeIOSTests/{NodeSetupPresentationTests.swift,RenderTests.swift,LotsPresentationTests.swift,DevicesPresentationTests.swift,HubSetupPresentationTests.swift}` -- one presentation test per matrix row with resolved copy, raw-value pins, tile tappability by role; render tests light/dark at `.accessibility5` (macOS only).
- `apps/kt/README.md`, `apps/swift/README.md`, `packages/kt/README.md`, `_bmad-output/implementation-artifacts/deferred-work.md` -- document the flow and its session order; record as deferred: battery % and Sensor count on the candidate tile, candidates never expiring, the first-run Add a Node tile, the outcome's Sensors/CALIBRATE/paused-Site note.

**Acceptance Criteria:**
- Given an Administrator or Owner on Android or iOS, when they tap "Add a Node" in Devices, a *no Node* tile, or Add a Node on "Hub is online", then the flow opens on "01 / 05" with "Press the setup button on the Node"; from a tile, that Lot is preselected on step 4. A Member has none of these actions.
- Given the core with a fake radio and `FakeNode`, when the happy path runs, then the Server receives one `enrolDevice` with `kind: node`, the chosen `lotId` and the Node's sealed key unread, only after the user confirmed, and the outcome reads "Tomatoes has a Node".
- Given a 201, when the flow closes, then the Lots engine has reloaded, so the Lot's tile is no longer *no Node*.
- Given any error row of the matrix, when it occurs, then its copy and recovery appear, an assertive announcement is made, and the Server holds no assignment the user did not see confirmed.
- Given `./gradlew check`, when it runs, then core, Android UI and Roborazzi tests pass with unchanged Add a Hub baselines; given the Swift container, `swift build && swift test && swift format lint --strict -r .` pass.

## Spec Change Log

## Review Triage Log

### 2026-10-06 — Review pass
- verdicts: 45 findings — high 0, medium 7, low 32, false 4, maybe-false 2
- findings:
  - `[low]` `[reject]` (blind) Back on step 3 after the accepted chip discards the sealed key without asking. -- Real: `back()` runs `backToScan()`, and the Node has left setup mode. The user chose Back over Continue and the code text is kept; asking instead adds a branch that departs from the contract's "Back on 3: one step back".
  - `[low]` `[reject]` (blind) A 404 on assign always reads "That Lot is gone", also when the Site is gone, and the failed reload is silent. -- Real, but a Site cannot be removed in V1; telling the two apart needs problem-type parsing and a new outcome.
  - `[low]` `[reject]` (blind) 403 or 404 from the Lot list or Create Lot shows a retryable "Your Server returned an error". -- Needs a Role change in the middle of a flow; the fix adds outcome routing on step 4.
  - `[false]` `[reject]` (blind) The Idempotency-Key outlives the flow, so a second session could leave Node and Server with different `K_dev`. -- `K_dev` is the Device's own key, re-sealed each session; the key covers only the Site registration (OpenAPI `enrolDevice`), and `lotId` is checked on every request.
  - `[low]` `[reject]` (blind) A 400 or 422 on assign shows "Can't reach your Server" and retries the same request. -- 422 cannot occur (one key per Device and Site) and 400 needs a Node that seals garbage; the matrix names this row.
  - `[low]` `[reject]` (blind) With Bluetooth off after a Node was picked, the flow ends on "stopped listening". -- Real, needs Bluetooth turned off between step 2 and Continue on step 3; a correct notice needs a radio gate and a notice on step 3.
  - `[low]` `[reject]` (blind) A scan that throws is not restarted while the radio stays Ready. -- Same code as Add a Hub; the fix adds a retry loop for a failure no test or report has shown.
  - `[low]` `[reject]` (blind) Top-left Back does nothing while the code is checked. -- Same rule as Add a Hub (rejected there); system back still asks to leave.
  - `[low]` `[patch]` (blind) "+ New Lot" kept the picked Lot, so Create Lot and "Put 7C19 in …" showed together. -- Verified in `AddNodeFlow.kt` LotStep. `openNewLot` now clears the selection; asserted in the engine test.
  - `[low]` `[reject]` (blind) The Bluetooth permission prompt appears on step 1. -- True, and harmless: the permission is needed one tap later; gating adds a parameter to the shared effects.
  - `[low]` `[reject]` (blind) The core emits a `LotTaken` announcement that both shells drop. -- No user effect: the step-4 notice announces itself assertively, once.
  - `[low]` `[reject]` (blind) `announcing()` is written and never read in the Node engine. -- Documented; the shared shell effects call it for both flows.
  - `[low]` `[reject]` (blind) The Administrator-or-Owner rule is written in several places. -- `canAddNode` delegates to `canAddHub`; no caller diverges today.
  - `[maybe-false]` `[defer]` (blind) iOS has no large-type handling for the two Devices header actions. -- To settle: render Devices for an Owner at `.accessibility5` on a Mac. Medium if true (unverified); also in `operator_actions`.
  - `[low]` `[reject]` (blind) Spec bookkeeping (empty `deferred`, `in-review`), and keep-awake on step 1 and outcomes. -- The fix edits this build's spec; keep-awake while open is the Hub flow's rule.
  - `[medium]` `[patch]` (edge) A Lots reload landing during or after an assignment cleared the selection, so the outcome read " has a Node". -- Verified by trace (create → reload in flight → assign → 201 → reload). A read that lands while assigning or after an outcome now changes nothing; a gated engine test covers it.
  - `[low]` `[reject]` (edge) Back on step 3 after acceptance. -- Same as the blind row.
  - `[low]` `[reject]` (edge) A leave question opened during the code check stays over the outcome. -- The dialog stays answerable, and either answer leads somewhere sensible; Add a Hub behaves the same.
  - `[low]` `[reject]` (edge) A scan that throws is not restarted. -- Same as the blind row.
  - `[low]` `[reject]` (edge) Bluetooth off at submit reads as "stopped listening". -- Same as the blind row.
  - `[low]` `[reject]` (edge) A certificate, 403 or 404 on `GET /enrolment-key` shows "Can't reach your Server". -- The same host answered the Sites and Lots calls moments before; separate outcomes add copy on both platforms.
  - `[low]` `[reject]` (edge) An `EnrolmentResponse` with an empty or mis-sized seal is accepted. -- Needs a misbehaving Node; the Server refuses it. Add a Hub has the same check.
  - `[low]` `[reject]` (edge) 400/422 on assign retried forever. -- Same as the blind row.
  - `[low]` `[reject]` (edge) 404 when the Site is gone. -- Same as the blind row.
  - `[low]` `[patch]` (edge) After a 422 on Create Lot the picker did not show a Lot an earlier attempt may have created. -- `createFailed` now reads the Lots again on `KeyReused`, keeping the notice; covered by the new create-key test.
  - `[low]` `[reject]` (edge) `onAssigned` calls `LotsEngine.load()`, a no-op while Loading or Idle. -- The flow covers the shell, so the Lots engine sits in Ready for the whole flow.
  - `[maybe-false]` `[defer]` (edge) On iOS, swapping the Hub flow for the Node flow may clear keep-awake. -- To settle: an iPhone run from "Hub is online". Medium if true (unverified); also in `operator_actions`.
  - `[low]` `[reject]` (edge) `eyebrowIcon` is now read only by tests. -- No behaviour change: the icon was `.errorFilled` for every error before and after.
  - `[low]` `[reject]` (edge, claim) A lost answer after the Server committed shows "Nothing was assigned." -- Real and rare on a home network; the same button converges (201), and the Garden shows the truth. Recorded as a residual risk; a truthful notice needs a reconciling read.
  - `[low]` `[reject]` (edge, claim) "The Lots engine has reloaded" does not hold while it is Loading. -- Same as the `onAssigned` row.
  - `[medium]` `[patch]` (verification-gap) No test reached a second Create Lot or a 422 on create. -- Added `uxDr38…` cases: a second create sends a fresh key; after a 422 the list is read again and the next create sends a new key. Removing either `createKey = null` now fails.
  - `[medium]` `[patch]` (verification-gap) `NodeSetupActions.of` was never run, so the Lot could be passed as the Site unnoticed. -- Added `NodeSetupActionsTest` on a real engine: `open("lot-t")` opens the flow on the current Site.
  - `[medium]` `[defer]` (verification-gap) Nothing joins the Hub outcome's Add a Node to an open Node flow through the platform wiring. -- Composition roots have no harness (DW-54 shape); deferred.
  - `[medium]` `[patch]` (verification-gap) iOS announcement text was asserted through a test-side copy. -- `setupAnnouncementText` and the Lot-taken rule moved into the Linux-compiled presentation sources (`spokenAnnouncement`); the views and the tests call the same function.
  - `[medium]` `[defer]` (verification-gap) The iOS entry points are checked only by "it renders". -- No view-interaction harness on Swift; deferred.
  - `[medium]` `[defer]` (verification-gap) App-target wiring of Add a Node repeats DW-54. -- Deferred with the Node lines named.
  - `[low]` `[reject]` (intent) Every test runs against fakes; no real Node, BLE stack or Server. -- The story's fourth criterion names the mocked BLE layer; the real-system run is owed in `operator_actions`.
  - `[false]` `[reject]` (intent) The single reload after a 201 may read a Lot still projected as *no Node*. -- `LotGrain.Claim` awaits `CatchUpLotsAsync()` before it returns, so the read model is caught up before the 201.
  - `[low]` `[reject]` (intent) Enrolment without a `SiteBinding` is exercised only by `FakeNode`. -- `session.rs` routes `EnrolmentRequest` without a prior binding (spec 4-2 triage); the bench run in `operator_actions` confirms it on hardware.
  - `[false]` `[reject]` (intent) Leaving at step 4 leaves the Node "enrolled" while the Server holds nothing. -- The Node stores no Site or Lot (spec 4-2 "Node persistence"); it only ends its window, and a new long press starts over.
  - `[low]` `[reject]` (intent) The window timeout is inferred from a failed connect, and an expired Node stays listed. -- Recorded as DW-56; the window is not signalled over BLE.
  - `[low]` `[reject]` (intent) The iOS app target and render tests are not executed on Linux. -- Owed in `operator_actions`, as for Stories 3.6 and 3.7.
  - `[low]` `[patch]` (intent) Step 3 was snapshotted as wrong-code in light only and accepted in dark only. -- Both states are now recorded in both themes (`add-node-code-wrong`, `add-node-code-accepted`).
  - `[low]` `[reject]` (intent) The first-run entry point, battery/Sensor count and the outcome's Sensors are not built. -- The story names three entry points and puts Sensors in Stories 4.4–4.6; the tile data has no source. Recorded as DW-55, DW-57, DW-58.
  - `[false]` `[reject]` (intent) The spec is not at `awaiting-operator` and has no `operator_actions`. -- Set at this finalization.

## Design Notes

- **Why enrolment runs on the code step and no `SiteBinding` is sent:** a Node drops a connection with no write for 60 s and listens for 180 s in total, so a session held open through the Lot picker would often die while the user types a new Lot's name. The sealed `K_dev` is valid for any Lot (spec 4-2: "a 409 from the Server needs no second BLE session"), and the Node only validates a binding and stores nothing from it, so the app takes the sealed key first and treats the Lot as a Server matter. Ending the connection after enrolment also closes the Node's setup window, so it measures and sleeps at once.
- **"Stopped listening" versus "lost connection":** the 180 s window is not signalled. A failed `connect` means the Node no longer advertises (window over); a drop mid-exchange is a lost connection. Both restart at step 1.
- **"Pressed just now":** the docs define it only as "the most recent setup-mode advertiser". Every listed Node is in setup mode, so the badge goes to the candidate first heard last.
- **Copy not in the UX docs** (Node variants of the Hub strings): step titles "Press the setup button on the Node", "Pick the Node", "Enter the setup code", "Pick a Lot"; step 1 body "Hold the button for 3 seconds. The Node then listens for 3 minutes."; step 1 action "Look for the Node"; outcome body "Node 7C19 reports for Tomatoes. Its Sensors appear with its first Readings."; announcements "Node 7C19 found, strong signal." and "Tomatoes has a Node."
- **A Lot created with "+ New Lot" stays** if the assignment then fails: a Lot without a Node is an ordinary state.

## Verification

**Commands:**
- `./gradlew check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64` -- expected: green, including Roborazzi verify (record new baselines with `./gradlew :android:recordRoborazziDebug` first).
- `podman run --rm --security-opt label=disable -v "$PWD":/work -w /work swift:6.3.3 sh -c 'swift build && swift test && swift format lint --strict -r .'` -- expected: green.
- `pnpm install --frozen-lockfile && pnpm -r lint && pnpm -r typecheck && pnpm -r test` -- expected: green (glossary and coverage checks).

**Manual checks (if no CLI):**
- Operator: Mac build of the iOS app and SwiftUI render tests; a bench run with a real Node on Android and iPhone (long press, flow to "‹Lot› has a Node", wrong code, window timeout, Lot taken), with TalkBack and VoiceOver.

## Auto Run Result

Status: awaiting-operator

**Summary:** Story 4.3 adds the Add a Node flow to the mobile apps.
- **Core:** `NodeSetupEngine` runs the five steps on one Site. On step 3 it opens the BLE session, checks the setup code and that the Device is a Node, reads and checks the Server's enrolment key, takes the Node's sealed key and disconnects. Step 4 is a Server matter only: it lists the Site's Lots (only *no Node* Lots can be picked), creates a Lot inline, and posts the assignment with one Idempotency-Key per Device and Site. A 201 reloads the Lots and Devices engines.
- **API client:** `lotId` on the enrolment request and on `Device`; `device-assigned` is no longer read as `lot-claimed`.
- **Entry points (Administrators and Owners):** "Add a Node" in the Devices header, a *no Node* tile (its Lot preselected), and Add a Node on "Hub is online" (opens for the Hub's Site).
- **Android and iOS:** the flow in Compose and SwiftUI. The announcement region, leave dialog, flow effects, candidate tile and outcome layout are now shared with Add a Hub.

**Files changed:**
- `packages/kt/core/src/commonMain/.../setup/{NodeSetupEngine,NodeSetupState,NodeSetupSnapshot}.kt`, `iosMain/.../setup/IosNodeSetup.kt` -- the engine, its state and the Swift facade.
- `packages/kt/core/src/commonMain/.../api/{ApiDtos,ApiResult,ColdframeApi}.kt` -- `lotId`, `DeviceAssigned`.
- `packages/kt/core/src/commonMain/.../{setup/HubSetupEngine.kt,setup/SetupRadio.kt,sites/SitesWiring.kt,devices/*,lots/LotsSnapshot.kt}`, `androidMain/.../AndroidSignIn.kt`, `iosMain/.../IosSignIn.kt` -- `onAddNode`, the Node name prefix, wiring, `canAddNode`.
- `apps/kt/android/.../ui/setup/{AddNodeFlow,NodeSetupActions,LotPickerRow}.kt` and the shared pieces in `{AddHubFlow,CandidateTile,SetupFlowShell,SetupOutcome}.kt`; `ColdframeRoot.kt`, `MainActivity.kt`, `ui/shell/AppShell.kt`, `ui/sites/{GardenScreen,LotTile}.kt`, `strings.xml`.
- `apps/swift/ios/Sources/ColdframeIOS/{NodeSetupPresentation,HubSetupPresentation,LotsPresentation,DevicesPresentation,L10n}.swift`, `UI/{NodeSetupViews,HubSetupViews,Screens,DevicesViews,SiteSettingsViews,SitesViews}.swift`, `Resources/Localizable.xcstrings`, `App/{CoreNodeSetupService,ColdframeApp,CoreSignInService,CoreLotsService,CoreDevicesService}.swift`.
- `tests/kt/core/**` (engine, snapshot, API, fakes), `tests/kt/android/**` (`AddNodeFlowTest`, `NodeSetupActionsTest`, snapshots, coverage), 16 new `add-node-*` baselines and 6 re-recorded `devices-*` baselines, `tests/swift/ios/**`.
- `apps/kt/README.md`, `apps/swift/README.md`, `packages/kt/README.md`, `deferred-work.md` (DW-55 to DW-58).

**Review findings:** 45 findings: 0 high, 7 medium, 32 low, 4 false, 2 maybe-false.
- **Patched (7 rows):** 4 medium (a late Lots read blanking the outcome's Lot name; the untested create-key reset; the untested `NodeSetupActions.of`; iOS announcement text tested through a copy) and 3 low ("+ New Lot" showing two primary buttons; no re-read after a 422 on create; step-3 snapshots in one theme each).
- **Deferred (5 items):** two unverified iOS risks (Devices header at the largest text size; keep-awake when the Hub flow hands over) and three wiring test gaps (Hub outcome to Node flow, iOS entry-point taps, app-target wiring).
- **Rejected (33 rows):** 29 low and 4 false, each with its reason in the triage log.

**Follow-up review recommended:** true. Patched this pass: 0 high, 4 medium, 3 low. The named risk: the announcement text for both setup flows moved between Swift files that are only partly compiled on Linux, and the Add a Hub SwiftUI views were refactored without a compile.

**Verification:**
- `./gradlew check :core:compileKotlinIosArm64 :core:compileKotlinIosSimulatorArm64`: green; 353 core tests and 217 Android tests, 0 failed; Roborazzi verify, ktlint and lint pass. Add a Hub and Garden baselines are unchanged.
- `swift:6.3.3` container, `swift build && swift test && swift format lint --strict -r .`: green, 148 tests.
- `pnpm -r lint`, `typecheck`, `test`: green (335 unit, 42 e2e).
- Matrix audit: every I/O row has a core engine test that ran and passed.
- Tests were written together with the code, not before it; the implementer checked nine engine mutations and the four patched behaviours against the tests instead.

**Residual risks:**
- The SwiftUI views (new and refactored), the iOS app adapters and `RenderTests.swift` have never been compiled.
- Nothing ran against a real Node; Kable and the radio are exercised through fakes only.
- If the Server assigned the Node but its answer was lost, step 4 says "Nothing was assigned."; the same button then succeeds, and another Lot ends on "already in another Lot".
- A Node whose 180 s window closed stays in the list and fails only after the code is typed (DW-56).

**Operator actions owed:** see the frontmatter `operator_actions` (Mac build of the Swift sources, and bench runs with a real Node on Android and iPhone, including the error cases and screen readers).
