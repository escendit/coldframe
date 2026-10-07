import Foundation
import Testing

@testable import ColdframeIOS

/// A snapshot as the core flattens it, defaulted to step 1 on "Home garden" with Node 7C19
/// selected of two candidates, the one pressed last; every argument mirrors a `NodeSetupSnapshot`
/// field. The Lots, once loaded, are Tomatoes (free) and Beans (has a Node).
func nodeSetup(
  open: Bool = true, step: Int = 1, showsCancel: Bool? = nil, radio: String = "ready",
  candidateIds: [String] = ["n-1", "n-2"], candidateNames: [String] = ["7C19", "A0B4"],
  candidateSignals: [String] = ["strong", "weak"], candidatePressed: [Bool] = [true, false],
  noNodeYet: Bool = false, selectedId: String? = "n-1", node: String? = "7C19",
  codeText: String = "", codeError: String? = nil, codeWorking: Bool = false,
  codeAccepted: Bool = false, deviceId: String? = nil, siteName: String = "Home garden",
  lotsLoaded: Bool = false, lotIds: [String] = ["t", "b"],
  lotNames: [String] = ["Tomatoes", "Beans"], lotSelectable: [Bool] = [true, false],
  selectedLotId: String? = nil, lotName: String? = nil, newLotOpen: Bool = false,
  newLotName: String = "", newLotError: String? = nil, creatingLot: Bool = false,
  assigning: Bool = false, lotNotice: String? = nil, lotNoticeLot: String? = nil,
  lotsRetryable: Bool = false, outcome: String? = nil, outcomePrimary: String? = nil,
  outcomeSecondary: String? = nil, stoppedStep: Int = 0, confirmingLeave: Bool = false, announcementId: Int = 0,
  announcementKind: String? = nil, announcementAssertive: Bool = false,
  announcementLot: String? = nil, announcementSignal: String? = nil,
  announcementOutcome: String? = nil
) -> NodeSetupPresentation {
  NodeSetupPresentation(
    open: open, step: step, keepAwake: open, showsCancel: showsCancel ?? (step == 1),
    radio: radio, candidateIds: candidateIds, candidateNames: candidateNames,
    candidateSignals: candidateSignals, candidatePressed: candidatePressed, noNodeYet: noNodeYet,
    selectedId: selectedId, node: node, codeText: codeText, codeError: codeError,
    codeWorking: codeWorking, codeAccepted: codeAccepted, deviceId: deviceId, siteName: siteName,
    lotsLoaded: lotsLoaded, lotIds: lotIds, lotNames: lotNames, lotSelectable: lotSelectable,
    selectedLotId: selectedLotId, lotName: lotName, newLotOpen: newLotOpen,
    newLotName: newLotName, newLotError: newLotError, creatingLot: creatingLot,
    assigning: assigning, lotNotice: lotNotice, lotNoticeLot: lotNoticeLot,
    lotsRetryable: lotsRetryable, outcome: outcome, outcomePrimary: outcomePrimary,
    outcomeSecondary: outcomeSecondary, stoppedStep: stoppedStep, confirmingLeave: confirmingLeave, announcementId: announcementId,
    announcementKind: announcementKind, announcementAssertive: announcementAssertive,
    announcementNode: "7C19", announcementLot: announcementLot,
    announcementSignal: announcementSignal, announcementOutcome: announcementOutcome)
}

/// Fills a catalogue entry the way `String(format:)` does for `%@`, `%lld` and their numbered
/// forms.
private func resolved(_ copy: Copy) throws -> String {
  var value = try #require(try Catalogue.entries()[copy.key.rawValue])
  for (index, argument) in copy.arguments.enumerated() {
    let (placeholder, text): (String, String) =
      switch argument {
      case .text(let text): ("@", text)
      case .number(let number): ("lld", String(number))
      }
    value = value.replacingOccurrences(of: "%\(index + 1)$\(placeholder)", with: text)
    if let plain = value.range(of: "%\(placeholder)") {
      value.replaceSubrange(plain, with: text)
    }
  }
  return value
}

private func resolved(_ key: L10n) throws -> String { try resolved(Copy(key)) }

/// The announcement as the views post it: the presentation's own text, filled from the catalogue.
private func spoken(_ presentation: NodeSetupPresentation) throws -> String? {
  try presentation.spokenAnnouncement(resolve: resolved)
}

/// Step 4 with the Lots read and Tomatoes picked.
private func lotStep(
  selectedLotId: String? = "t", newLotOpen: Bool = false, newLotName: String = "",
  newLotError: String? = nil, creatingLot: Bool = false, assigning: Bool = false,
  lotNotice: String? = nil, lotNoticeLot: String? = nil, lotsRetryable: Bool = false,
  announcementId: Int = 0, announcementKind: String? = nil, announcementAssertive: Bool = false,
  announcementLot: String? = nil
) -> NodeSetupPresentation {
  nodeSetup(
    step: 4, codeAccepted: true, deviceId: "7c19000000000001", lotsLoaded: true,
    selectedLotId: selectedLotId, lotName: selectedLotId == "t" ? "Tomatoes" : nil,
    newLotOpen: newLotOpen, newLotName: newLotName, newLotError: newLotError,
    creatingLot: creatingLot, assigning: assigning, lotNotice: lotNotice,
    lotNoticeLot: lotNoticeLot, lotsRetryable: lotsRetryable, announcementId: announcementId,
    announcementKind: announcementKind, announcementAssertive: announcementAssertive,
    announcementLot: announcementLot)
}

/// An error outcome for Node 7C19, with Tomatoes picked when it stopped on step 4.
private func stopped(_ outcome: String, primary: String, step: Int = 3) -> NodeSetupPresentation {
  nodeSetup(
    step: 5, lotName: step == 4 ? "Tomatoes" : nil, outcome: outcome, outcomePrimary: primary,
    stoppedStep: step)
}

// The happy path

@Test("UX-DR67 UX-DR39 the flow opens on 01 / 05 with Cancel and the instruction to press")
func nodePressStep() throws {
  let press = nodeSetup(selectedId: nil, node: nil)
  #expect(press.isOpen)
  #expect(press.keepAwake)
  #expect(press.step == .press)
  #expect(press.counterCurrent == "01")
  #expect(try resolved(press.counterTotal) == "/ 05")
  #expect(try resolved(press.counterDescription) == "Step 1 of 5")
  #expect(press.backLabel == .modalCancel)
  #expect(try resolved(press.backLabel) == "Cancel")
  #expect(try resolved(press.title) == "Press the setup button on the Node")
  #expect(
    try resolved(.addNodePressBody)
      == "Hold the button for 3 seconds. The Node then listens for 3 minutes.")
  #expect(try resolved(.addNodePressAction) == "Look for the Node")
  #expect(press.outcome == nil)
  #expect(!NodeSetupPresentation.closed.isOpen)
  #expect(!NodeSetupPresentation.closed.keepAwake)
}

@Test("UX-DR67 UX-DR39 the happy path counts 01 to 04 with Back after step 1 and its titles")
func nodeHappyPathSteps() throws {
  let titles = ["Press the setup button on the Node", "Pick the Node", "Enter the setup code"]
  for (index, title) in (titles + ["Pick a Lot"]).enumerated() {
    let step = nodeSetup(step: index + 1)
    #expect(step.counterCurrent == "0\(index + 1)")
    #expect(try resolved(step.counterTotal) == "/ 05")
    #expect(try resolved(step.counterDescription) == "Step \(index + 1) of 5")
    #expect(step.backLabel == (index == 0 ? .modalCancel : .navBack))
    #expect(try resolved(step.title) == title)
  }
  #expect(try resolved(nodeSetup(step: 2).backLabel) == "Back")
  #expect(NodeSetupStepKind.count == 5)
  #expect(NodeSetupStepKind.allCases.map(\.rawValue) == [1, 2, 3, 4, 5])
  // Step 5 is the outcome screen; without an outcome yet the Lot picker stays.
  #expect(nodeSetup(step: 5).step == .outcome)
  #expect(try resolved(nodeSetup(step: 5).title) == "Pick a Lot")
  // A step this shell does not know reads as the first.
  #expect(nodeSetup(step: 9).step == .press)
}

@Test("UX-DR67 UX-DR55 a 201 ends on Tomatoes has a Node, a success screen with Done")
func nodeAssigned() throws {
  let assigned = nodeSetup(
    step: 5, lotName: "Tomatoes", outcome: "assigned", outcomePrimary: "done", stoppedStep: 5)
  let outcome = try #require(assigned.outcome)
  #expect(outcome.kind == .assigned)
  #expect(outcome.isSuccess)
  #expect(outcome.eyebrow == nil)
  #expect(outcome.eyebrowIcon == nil)
  #expect(try resolved(outcome.title) == "Tomatoes has a Node")
  #expect(
    try resolved(outcome.body)
      == "Node 7C19 reports for Tomatoes. Its Sensors appear with its first Readings.")
  #expect(outcome.primary == .done)
  #expect(try resolved(outcome.primary.label) == "Done")
}

// Step 2: scan

@Test("UX-DR37 two Nodes keep the core's order and only the one heard last says Pressed just now")
func nodeCandidates() throws {
  let scan = nodeSetup(step: 2)
  #expect(scan.candidates.map(\.node) == ["7C19", "A0B4"])
  #expect(scan.candidates.map(\.signal) == [.strong, .weak])
  #expect(scan.candidates.map(\.pressedJustNow) == [true, false])
  #expect(scan.candidates[0].traits == [.button, .selected])
  #expect(scan.candidates[1].traits == [.button])
  #expect(try resolved(scan.candidates[0].name) == "Node 7C19")
  #expect(try resolved(try #require(scan.candidates[0].badge)) == "Pressed just now")
  #expect(scan.candidates[1].badge == nil)
  let strong = try resolved(scan.candidates[0].signal.label)
  let weak = try resolved(scan.candidates[1].signal.label)
  #expect(
    try resolved(scan.candidates[0].description(signal: strong))
      == "Node 7C19, pressed just now, strong signal")
  #expect(try resolved(scan.candidates[1].description(signal: weak)) == "Node A0B4, weak signal")
  #expect(
    try resolved(.addNodeScanIntro)
      == "Nodes that listen in Bluetooth range appear here, strongest first.")
  #expect(scan.showsStillScanning)
  #expect(try resolved(.addNodeStillScanning) == "Still scanning…")
  #expect(try resolved(try #require(scan.scanAction)) == "Set up Node 7C19")
  #expect(nodeSetup(step: 2, selectedId: nil, node: nil).scanAction == nil)
}

@Test("UX-DR37 the Node tile carries no battery level or Sensor count, and ragged lists drop rows")
func nodeCandidateFields() {
  // Only what the advert carries: the four digits, the signal and the order it was heard in.
  let fields = Mirror(reflecting: nodeSetup(step: 2).candidates[0]).children.compactMap(\.label)
  #expect(fields == ["id", "node", "signal", "pressedJustNow", "isSelected"])
  let ragged = nodeSetup(step: 2, candidatePressed: [true])
  #expect(ragged.candidates.map(\.node) == ["7C19"])
  // A signal this shell does not know reads as the weakest.
  #expect(nodeSetup(step: 2, candidateSignals: ["later", "weak"]).candidates[0].signal == .weak)
}

@Test("UX-DR94 Bluetooth off or denied shows the Node notice with Open Settings; unsupported none")
func nodeBluetoothNotices() throws {
  for radio in ["off", "unauthorized"] {
    let off = nodeSetup(step: 2, radio: radio)
    #expect(off.radio.nodeNotice == .addNodeBluetoothNeeded)
    #expect(off.radio.opensSettings)
    #expect(!off.showsStillScanning)
  }
  #expect(try resolved(.addNodeBluetoothNeeded) == "Coldframe needs Bluetooth to find the Node.")
  #expect(try resolved(.addNodeOpenSettings) == "Open Settings")
  let unsupported = nodeSetup(step: 2, radio: "unsupported")
  #expect(unsupported.radio.nodeNotice == .addNodeBluetoothUnsupported)
  #expect(!unsupported.radio.opensSettings)
  #expect(!unsupported.showsStillScanning)
  #expect(
    try resolved(.addNodeBluetoothUnsupported)
      == "This phone has no Bluetooth that can find the Node.")
  #expect(nodeSetup(step: 2).radio.nodeNotice == nil)
  // A radio state this shell does not know never claims to scan.
  #expect(nodeSetup(step: 2, radio: "later").radio == .off)
}

@Test("UX-DR94 no Node in 30 s says how to wake it while the list keeps scanning")
func nodeNoneYet() throws {
  let waiting = nodeSetup(
    step: 2, candidateIds: [], candidateNames: [], candidateSignals: [], candidatePressed: [],
    noNodeYet: true, selectedId: nil, node: nil)
  #expect(waiting.noNodeYet)
  #expect(waiting.candidates.isEmpty)
  #expect(waiting.showsStillScanning)
  #expect(waiting.scanAction == nil)
  #expect(
    try resolved(.addNodeNoNode)
      == "No Node in range yet. Hold its setup button for 3 seconds; it listens for 3 minutes.")
  #expect(!nodeSetup(step: 2).noNodeYet)
}

// Step 3: setup code

@Test("UX-DR41 the code step checks the code, then shows the chip, the Device ID and Pick a Lot")
func nodeAcceptedCode() throws {
  let typing = nodeSetup(step: 3, codeText: "K7M2Q9XP")
  #expect(try resolved(.addNodeCodeIntro) == "The setup code is on the Node's serial console.")
  #expect(try resolved(.addNodeCodeLabel) == "Setup code")
  #expect(
    try resolved(.addNodeCodeHelper) == "Letters and digits; spaces and dashes don't matter.")
  #expect(try resolved(typing.codeAction) == "Check setup code")
  #expect(typing.isCodeActionEnabled)
  #expect(typing.deviceId == nil)

  let working = nodeSetup(step: 3, codeText: "K7M2Q9XP", codeWorking: true)
  #expect(try resolved(working.codeAction) == "Checking setup code…")
  #expect(!working.isCodeActionEnabled)

  let accepted = nodeSetup(
    step: 3, codeText: "K7M2Q9XP", codeAccepted: true, deviceId: "7c19000000000001")
  #expect(accepted.codeAccepted)
  #expect(try resolved(.addNodeCodeAccepted) == "Accepted")
  #expect(try resolved(.addNodeDeviceId) == "Device ID")
  #expect(accepted.deviceId == "7c19000000000001")
  #expect(try resolved(accepted.codeAction) == "Pick a Lot")
  #expect(accepted.isCodeActionEnabled)
}

@Test("UX-DR94 UX-DR105 a wrong setup code keeps the text, names the Node and is assertive")
func nodeWrongCode() throws {
  let wrong = nodeSetup(
    step: 3, codeText: "K7M2Q9XQ", codeError: "wrongCode", announcementId: 3,
    announcementKind: "wrongCode", announcementAssertive: true)
  #expect(wrong.codeText == "K7M2Q9XQ")
  #expect(wrong.step == .code)
  #expect(
    try resolved(try #require(wrong.codeErrorMessage))
      == "That setup code doesn't match Node 7C19. Check the serial console.")
  #expect(wrong.codeAction == .addNodeCodeAction)
  #expect(wrong.announcement?.announcement == .assertive)
  #expect(
    try spoken(wrong) == "That setup code doesn't match Node 7C19. Check the serial console.")

  let blank = nodeSetup(step: 3, codeError: "blank")
  #expect(try resolved(try #require(blank.codeErrorMessage)) == "Enter the setup code.")
  #expect(nodeSetup(step: 3).codeErrorMessage == nil)
}

// Step 4: the Lot picker

@Test("UX-DR38 a free Lot can be picked, a Lot with a Node is disabled and says Has a Node")
func nodeLotRows() throws {
  let picker = lotStep(selectedLotId: nil)
  #expect(
    try resolved(picker.lotIntro) == "Node 7C19 reports for the Lot you pick on Home garden.")
  let rows = try #require(picker.lots)
  #expect(rows.map(\.name) == ["Tomatoes", "Beans"])
  #expect(rows.map(\.isSelectable) == [true, false])
  #expect(rows.allSatisfy { !$0.isSelected })
  #expect(rows[0].reason == nil)
  #expect(rows[0].description == nil)
  #expect(try resolved(try #require(rows[1].reason)) == "Has a Node")
  #expect(try resolved(try #require(rows[1].description)) == "Beans, has a Node")
  #expect(picker.lotsLoading == nil)
  #expect(picker.lotNotice == nil)
  // No Lot picked yet: no primary button.
  #expect(picker.selectedLot == nil)
  #expect(picker.assignAction == nil)
  // Lists of different lengths from the bridge show only whole rows, in the Server's order.
  #expect(nodeSetup(step: 4, lotsLoaded: true, lotSelectable: [true]).lots?.map(\.id) == ["t"])
}

@Test("UX-DR38 UX-DR67 the picked Lot is selected and the button reads Put 7C19 in Tomatoes")
func nodeAssignAction() throws {
  let picked = lotStep()
  let rows = try #require(picked.lots)
  #expect(rows[0].isSelected)
  #expect(rows[0].traits == [.button, .selected])
  #expect(rows[1].traits == [.button])
  #expect(picked.selectedLot?.id == "t")
  #expect(try resolved(try #require(picked.assignAction)) == "Put 7C19 in Tomatoes")
  #expect(picked.isAssignEnabled)

  let assigning = lotStep(assigning: true)
  #expect(try resolved(try #require(assigning.assignAction)) == "Putting 7C19 in Tomatoes…")
  #expect(!assigning.isAssignEnabled)

  // A Lot that has a Node is never the picked one, whatever the bridge says.
  let taken = lotStep(selectedLotId: "b")
  #expect(taken.selectedLot == nil)
  #expect(taken.assignAction == nil)
}

@Test("UX-DR38 New Lot opens an inline name field with the Create Lot copy and its check")
func nodeNewLot() throws {
  let closed = lotStep(selectedLotId: nil)
  #expect(closed.showsNewLotTile)
  #expect(closed.newLot == nil)
  #expect(try resolved(.addNodeNewLot) == "+ New Lot")

  let open = lotStep(selectedLotId: nil, newLotOpen: true, newLotName: "Peppers")
  #expect(!open.showsNewLotTile)
  let field = try #require(open.newLot)
  #expect(field.name == "Peppers")
  #expect(try resolved(field.label) == "Lot name")
  #expect(try resolved(field.helper) == "For example Tomatoes. Up to 100 characters.")
  #expect(field.errorMessage == nil)
  #expect(try resolved(field.buttonLabel) == "Create Lot")
  #expect(field.isButtonEnabled)

  let blank = try #require(lotStep(newLotOpen: true, newLotError: "blank").newLot)
  #expect(try resolved(try #require(blank.errorMessage)) == "Enter a name for the Lot.")
  let long = try #require(lotStep(newLotOpen: true, newLotError: "tooLong").newLot)
  #expect(try resolved(try #require(long.errorMessage)) == "Use at most 100 characters.")
  let creating = try #require(
    lotStep(newLotOpen: true, newLotName: "Peppers", creatingLot: true).newLot)
  #expect(try resolved(creating.buttonLabel) == "Creating Lot…")
  #expect(!creating.isButtonEnabled)
  // The field belongs to the list: nothing to add to before the Lots are read.
  #expect(nodeSetup(step: 4, newLotOpen: true).newLot == nil)
  #expect(!nodeSetup(step: 4, newLotOpen: true).showsNewLotTile)
}

@Test("UX-DR38 UX-DR94 unreadable Lots show the notice with Try again; reading them says so")
func nodeLotsUnreadable() throws {
  let loading = nodeSetup(step: 4, codeAccepted: true)
  #expect(loading.lots == nil)
  #expect(try resolved(try #require(loading.lotsLoading)) == "Fetching the Lots of Home garden…")
  #expect(loading.lotNotice == nil)
  #expect(!loading.offersLotsRetry)
  #expect(loading.assignAction == nil)

  let unreachable = nodeSetup(
    step: 4, codeAccepted: true, lotNotice: "unreachable", lotsRetryable: true)
  #expect(unreachable.step == .lot)
  #expect(unreachable.lotNotice == .unreachable)
  #expect(unreachable.lotNotice?.message == .noticeUnreachable)
  #expect(unreachable.lotNoticeLot == nil)
  #expect(unreachable.offersLotsRetry)
  #expect(try resolved(.noticeTryAgain) == "Try again")
  #expect(unreachable.lotsLoading == nil)
  // The sealed key is kept: the code stays accepted behind the notice.
  #expect(unreachable.codeAccepted)

  // The certificate notice is never retried insecurely; the core says so.
  let certificate = nodeSetup(step: 4, lotNotice: "certificate", lotsRetryable: false)
  #expect(certificate.lotNotice?.message == .noticeCertificate)
  #expect(!certificate.offersLotsRetry)
  let unexpected = nodeSetup(step: 4, lotNotice: "unexpected", lotsRetryable: true)
  #expect(
    try resolved(try #require(unexpected.lotNotice).message)
      == "Your Server returned an error. Nothing was changed.")
  // A notice this shell does not know still says that nothing changed.
  #expect(nodeSetup(step: 4, lotNotice: "later").lotNotice == .unexpected)
}

@Test("UX-DR94 UX-DR105 a Lot taken meanwhile stays on step 4, names the Lot and is assertive")
func nodeLotTaken() throws {
  let taken = lotStep(
    selectedLotId: nil, lotNotice: "lotTaken", lotNoticeLot: "Tomatoes", announcementId: 5,
    announcementKind: "lotTaken", announcementAssertive: true, announcementLot: "Tomatoes")
  #expect(taken.step == .lot)
  #expect(taken.outcome == nil)
  let notice = try #require(taken.lotNotice)
  #expect(notice == .lotTaken)
  #expect(notice.takesLot)
  #expect(taken.lotNoticeLot == "Tomatoes")
  #expect(
    try resolved(Copy(notice.message, .text(try #require(taken.lotNoticeLot))))
      == "Tomatoes got a Node meanwhile. Pick another Lot.")
  // The list was read again and the selection cleared: another Lot is picked, no new session.
  #expect(taken.lots != nil)
  #expect(taken.selectedLot == nil)
  #expect(taken.assignAction == nil)
  #expect(!taken.offersLotsRetry)
  #expect(taken.announcement?.announcement == .assertive)
  // The notice says it, once: the flow posts nothing more for it.
  #expect(taken.announcement?.kind == .lotTaken)
  #expect(try spoken(taken) == nil)
}

@Test("UX-DR94 a Lot that is gone says so on step 4 and another Lot can be picked")
func nodeLotGone() throws {
  let gone = lotStep(selectedLotId: nil, lotNotice: "lotGone", lotNoticeLot: "Tomatoes")
  #expect(gone.step == .lot)
  let notice = try #require(gone.lotNotice)
  #expect(!notice.takesLot)
  #expect(gone.lotNoticeLot == nil)
  #expect(try resolved(notice.message) == "That Lot is gone. Pick another Lot.")
  #expect(gone.lots != nil)
  #expect(gone.assignAction == nil)
}

@Test("UX-DR94 an unreachable Server on assign stays on step 4 and the button retries")
func nodeAssignFailed() throws {
  let failed = lotStep(lotNotice: "assignFailed")
  #expect(failed.step == .lot)
  #expect(failed.outcome == nil)
  #expect(
    try resolved(try #require(failed.lotNotice).message)
      == "Can't reach your Server. Nothing was assigned.")
  // The same button sends the same request again; there is no separate Try again.
  #expect(!failed.offersLotsRetry)
  #expect(try resolved(try #require(failed.assignAction)) == "Put 7C19 in Tomatoes")
  #expect(failed.isAssignEnabled)
}

// Error outcomes

@Test("UX-DR94 UX-DR55 a Node that stopped listening stops step 3 and offers Try again")
func nodeStoppedListening() throws {
  let presentation = stopped("stoppedListening", primary: "startOver")
  let outcome = try #require(presentation.outcome)
  #expect(!outcome.isSuccess)
  #expect(try resolved(try #require(outcome.eyebrow)) == "Step 3 stopped")
  #expect(outcome.eyebrowIcon == .errorFilled)
  #expect(try resolved(outcome.title) == "7C19 stopped listening")
  #expect(try resolved(outcome.body) == "Press its setup button again.")
  #expect(outcome.primary == .startOver)
  #expect(try resolved(outcome.primary.label) == "Try again")
}

@Test("UX-DR94 UX-DR55 a lost connection says nothing was saved and starts over")
func nodeLostConnection() throws {
  let outcome = try #require(stopped("lostConnection", primary: "startOver").outcome)
  #expect(
    try resolved(outcome.title) + ". " + resolved(outcome.body)
      == "Lost the connection to Node 7C19. Nothing was saved.")
  #expect(try resolved(try #require(outcome.eyebrow)) == "Step 3 stopped")
  #expect(outcome.primary == .startOver)
}

@Test("UX-DR94 UX-DR55 a Device that is not a Node, or refuses, ends on refused the setup")
func nodeRefused() throws {
  let outcome = try #require(stopped("nodeRefused", primary: "close").outcome)
  #expect(try resolved(outcome.title) + "." == "Node 7C19 refused the setup.")
  #expect(
    try resolved(outcome.body) == "Nothing was assigned. Press its setup button and try again.")
  #expect(outcome.primary == .close)
  #expect(try resolved(outcome.primary.label) == "Close")
}

@Test("UX-DR94 UX-DR55 an unreadable enrolment key ends on Can't reach your Server and starts over")
func nodeServerUnreachable() throws {
  let outcome = try #require(stopped("serverUnreachable", primary: "startOver").outcome)
  #expect(try resolved(outcome.title) == "Can't reach your Server")
  #expect(
    try resolved(outcome.body)
      == "Node 7C19 was not assigned. Check that this phone is on your home Wi-Fi.")
  #expect(outcome.primary == .startOver)
}

@Test("UX-DR94 UX-DR55 a fingerprint mismatch enrols nothing and only closes")
func nodeFingerprintMismatch() throws {
  let outcome = try #require(stopped("fingerprintMismatch", primary: "close").outcome)
  #expect(
    try resolved(outcome.title) + "." == "The Server's enrolment key doesn't check out.")
  #expect(try resolved(outcome.body) == "Nothing was enrolled and the Node was not assigned.")
  #expect(outcome.primary == .close)
}

@Test("UX-DR94 UX-DR55 a Node already in another Lot stops step 4 and only closes")
func nodeAlreadyAssigned() throws {
  let outcome = try #require(stopped("alreadyAssigned", primary: "close", step: 4).outcome)
  #expect(try resolved(try #require(outcome.eyebrow)) == "Step 4 stopped")
  #expect(try resolved(outcome.title) + "." == "Node 7C19 is already in another Lot.")
  #expect(try resolved(outcome.body) == "It stays in that Lot. Nothing was changed.")
  #expect(outcome.primary == .close)
}

@Test("UX-DR94 UX-DR55 a Node on another Site and a caller who may not add Nodes have their copy")
func nodeOtherSiteAndNotAllowed() throws {
  let other = try #require(stopped("onAnotherSite", primary: "close", step: 4).outcome)
  #expect(try resolved(other.title) == "Node 7C19 is on another Site")
  #expect(
    try resolved(other.body) == "It stays on the Site it was added to. Nothing was changed.")
  #expect(other.primary == .close)

  let denied = try #require(stopped("notAllowed", primary: "close", step: 4).outcome)
  #expect(try resolved(denied.title) == "You can't add Nodes to Home garden")
  #expect(try resolved(denied.body) == "Ask an Owner or Administrator. Nothing was changed.")
  #expect(denied.primary == .close)
}

@Test("UX-DR55 every outcome has one primary action, the core's, also when it names none")
func nodeOutcomePrimaries() throws {
  let expected: [NodeOutcomeKindValue: NodeOutcomeActionKind] = [
    .assigned: .done, .stoppedListening: .startOver, .lostConnection: .startOver,
    .nodeRefused: .close, .serverUnreachable: .startOver, .fingerprintMismatch: .close,
    .alreadyAssigned: .close, .onAnotherSite: .close, .notAllowed: .close,
  ]
  for kind in NodeOutcomeKindValue.allCases {
    #expect(NodeSetupPresentation.primary(of: kind) == expected[kind], "\(kind)")
    let outcome = try #require(nodeSetup(step: 5, outcome: kind.rawValue).outcome, "\(kind)")
    #expect(outcome.primary == expected[kind], "\(kind)")
    #expect(!(try resolved(outcome.title)).isEmpty)
    #expect(!(try resolved(outcome.body)).isEmpty)
    #expect(outcome.isSuccess == (kind == .assigned))
  }
  // The core's word wins over the shell's table.
  let named = try #require(
    nodeSetup(step: 5, outcome: "lostConnection", outcomePrimary: "close").outcome)
  #expect(named.primary == .close)
  // An outcome this shell does not know is not drawn as one.
  #expect(nodeSetup(step: 5, outcome: "later").outcome == nil)
}

// Leaving

@Test("UX-DR39 leaving mid-flow asks Stop setting up Node 7C19 and says nothing is saved")
func nodeLeaveConfirmation() throws {
  #expect(!lotStep().confirmingLeave)
  let confirming = nodeSetup(step: 4, lotsLoaded: true, confirmingLeave: true)
  #expect(confirming.confirmingLeave)
  // The question is over the step it was asked on, which stays.
  #expect(confirming.step == .lot)
  #expect(try resolved(confirming.leaveQuestion) == "Stop setting up Node 7C19?")
  #expect(try resolved(confirming.leaveDetail) == "Nothing is saved on the Node.")
  #expect(try resolved(.setupLeaveConfirm) == "Stop setting up")
  #expect(try resolved(.setupLeaveStay) == "Keep setting up")
  // Back is still Back on step 4: the core decides that it asks.
  #expect(confirming.backLabel == .navBack)
}

// The Hub outcome

@Test("UX-DR67 UX-DR55 Add a Node on Hub is online goes to the core, which opens step 1")
func nodeFromHubOutcome() throws {
  let online = try #require(
    hubSetup(
      step: 5, ssid: "Novak-Home", progressReached: 4, outcome: "online",
      outcomePrimary: "addNode"
    ).outcome)
  // The Hub flow forwards its own action; the core closes that flow and opens this one.
  #expect(online.primary == .addNode)
  #expect(online.primary.rawValue == "addNode")
  #expect(try resolved(online.primary.label) == "Add a Node")
  let opened = nodeSetup(selectedId: nil, node: nil)
  #expect(opened.isOpen)
  #expect(opened.step == .press)
  #expect(opened.counterCurrent == "01")
  #expect(opened.backLabel == .modalCancel)
}

// Announcements

@Test(
  "UX-DR105 a new Node and the assignment are polite; a wrong code, a taken Lot and errors assertive"
)
func nodeAnnouncements() throws {
  let found = nodeSetup(
    step: 2, announcementId: 1, announcementKind: "candidateFound", announcementSignal: "strong")
  #expect(found.announcement?.announcement == .polite)
  #expect(try spoken(found) == "Node 7C19 found, strong signal.")

  let assigned = nodeSetup(
    step: 5, lotName: "Tomatoes", outcome: "assigned", announcementId: 2,
    announcementKind: "assigned", announcementLot: "Tomatoes")
  #expect(assigned.announcement?.announcement == .polite)
  #expect(try spoken(assigned) == "Tomatoes has a Node.")

  let error = nodeSetup(
    step: 5, outcome: "stoppedListening", stoppedStep: 3, announcementId: 3,
    announcementKind: "error", announcementAssertive: true,
    announcementOutcome: "stoppedListening")
  #expect(error.announcement?.announcement == .assertive)
  #expect(try spoken(error) == "7C19 stopped listening. Try again.")

  // The headline, then the one action left.
  let denied = nodeSetup(
    step: 5, outcome: "notAllowed", stoppedStep: 4, announcementId: 4, announcementKind: "error",
    announcementAssertive: true, announcementOutcome: "notAllowed")
  #expect(try spoken(denied) == "You can't add Nodes to Home garden. Close.")

  // Made once per id: none without an id, a kind or what it needs to say.
  #expect(nodeSetup(announcementId: 0, announcementKind: "error").announcement == nil)
  #expect(nodeSetup(announcementId: 6, announcementKind: "later").announcement == nil)
  let silent = try #require(
    nodeSetup(announcementId: 7, announcementKind: "candidateFound").announcement)
  #expect(silent.message(siteName: "Home garden") == nil)
  let unnamed = try #require(nodeSetup(announcementId: 8, announcementKind: "error").announcement)
  #expect(unnamed.message(siteName: "Home garden") == nil)
  // The Lot of an announcement falls back to the picked one.
  let fallback = nodeSetup(
    step: 4, lotName: "Tomatoes", announcementId: 9, announcementKind: "assigned")
  #expect(try spoken(fallback) == "Tomatoes has a Node.")
}

// The bridge

@Test("UX-DR67 every snapshot key the core sends for Add a Node has a Swift value")
func nodeSnapshotKeys() {
  #expect(
    NodeOutcomeKindValue.allCases.map(\.rawValue) == [
      "assigned", "stoppedListening", "lostConnection", "nodeRefused", "serverUnreachable",
      "fingerprintMismatch", "alreadyAssigned", "onAnotherSite", "notAllowed",
    ])
  #expect(NodeOutcomeActionKind.allCases.map(\.rawValue) == ["done", "startOver", "close", "calibrate"])
  #expect(
    LotPickerNoticeKind.allCases.map(\.rawValue) == [
      "unreachable", "certificate", "unexpected", "lotTaken", "lotGone", "assignFailed",
    ])
  #expect(
    NodeAnnouncementKind.allCases.map(\.rawValue) == [
      "candidateFound", "wrongCode", "lotTaken", "error", "assigned",
    ])
  #expect(NameErrorKind.allCases.map(\.rawValue) == ["blank", "tooLong"])
  #expect(CodeErrorKind.allCases.map(\.rawValue) == ["blank", "wrongCode"])
  #expect(SignalKind.allCases.map(\.rawValue) == ["strong", "medium", "weak"])
  #expect(
    RadioStateKind.allCases.map(\.rawValue) == ["ready", "off", "unauthorized", "unsupported"])
}

/// Records what the views ask of the core, as `CoreNodeSetupService` would pass it on.
@MainActor
private final class RecordingNodeSetup: NodeSetupService {
  var calls: [String] = []

  func observe(_ onChange: @escaping @MainActor (NodeSetupPresentation) -> Void) {
    onChange(nodeSetup())
  }
  func open(lotId: String?) { calls.append("open(\(lotId ?? "nil"))") }
  func close() { calls.append("close") }
  func recheckRadio() { calls.append("recheckRadio") }
  func announcing(_ active: Bool) { calls.append("announcing(\(active))") }
  func back() { calls.append("back") }
  func leave() { calls.append("leave") }
  func confirmLeave() { calls.append("confirmLeave") }
  func stayInFlow() { calls.append("stayInFlow") }
  func continueFromPress() { calls.append("continueFromPress") }
  func select(candidateId: String) { calls.append("select(\(candidateId))") }
  func continueFromScan() { calls.append("continueFromScan") }
  func setCode(_ text: String) { calls.append("setCode(\(text.count))") }
  func submitCode() { calls.append("submitCode") }
  func continueFromCode() { calls.append("continueFromCode") }
  func retryLots() { calls.append("retryLots") }
  func chooseLot(_ lotId: String) { calls.append("chooseLot(\(lotId))") }
  func openNewLot() { calls.append("openNewLot") }
  func setNewLotName(_ name: String) { calls.append("setNewLotName(\(name))") }
  func createLot() { calls.append("createLot") }
  func assign() { calls.append("assign") }
  func outcomeAction(_ action: NodeOutcomeActionKind) {
    calls.append("outcomeAction(\(action.rawValue))")
  }
}

@Test("UX-DR67 every action reaches the service with what the presentation names")
@MainActor
func nodeServiceActions() throws {
  let recording = RecordingNodeSetup()
  let service: any NodeSetupService = recording
  var observed: NodeSetupPresentation?
  service.observe { observed = $0 }
  #expect(observed?.isOpen == true)

  // From Devices without a Lot, from a no-Node tile with its Lot.
  service.open(lotId: nil)
  service.open(lotId: "t")
  service.recheckRadio()
  service.announcing(true)
  service.announcing(false)
  service.continueFromPress()
  let scan = nodeSetup(step: 2)
  service.select(candidateId: scan.candidates[1].id)
  service.continueFromScan()
  service.setCode("K7M2Q9XP")
  service.submitCode()
  service.continueFromCode()
  service.retryLots()
  service.chooseLot(try #require(lotStep(selectedLotId: nil).lots?.first).id)
  service.openNewLot()
  service.setNewLotName("Peppers")
  service.createLot()
  service.assign()
  // Cancel and Back are the same request; the core closes, steps back or asks.
  service.back()
  service.leave()
  service.stayInFlow()
  service.confirmLeave()
  for kind in ["assigned", "stoppedListening", "nodeRefused"] {
    let outcome = try #require(nodeSetup(step: 5, outcome: kind).outcome)
    service.outcomeAction(outcome.primary)
  }
  service.close()

  #expect(
    recording.calls == [
      "open(nil)", "open(t)", "recheckRadio", "announcing(true)", "announcing(false)",
      "continueFromPress", "select(n-2)", "continueFromScan", "setCode(8)", "submitCode",
      "continueFromCode", "retryLots", "chooseLot(t)", "openNewLot", "setNewLotName(Peppers)",
      "createLot", "assign", "back", "leave", "stayInFlow", "confirmLeave",
      "outcomeAction(done)", "outcomeAction(startOver)", "outcomeAction(close)", "close",
    ])
}

@Test("UX-DR67 no Node flow string is a literal: the views read the catalogue's add_node keys")
func nodeCatalogueKeys() throws {
  let entries = try Catalogue.entries()
  let keys = L10n.allCases.map(\.rawValue).filter { $0.hasPrefix("add_node_") }
  #expect(!keys.isEmpty)
  for key in keys {
    #expect(entries[key]?.isEmpty == false, "\(key)")
  }
  #expect(entries["devices_add_node"] == "Add a Node")
  // The Node flow sends no Wi-Fi and has no progress step: none of its keys names them.
  #expect(keys.allSatisfy { !$0.contains("wifi") && !$0.contains("segment") })
}
