import Foundation
import Testing

@testable import ColdframeIOS

/// A snapshot as the core flattens it, defaulted to step 1 with Hub 3F2A selected on "Home
/// garden"; every argument mirrors a `HubSetupSnapshot` field.
func hubSetup(
  open: Bool = true, step: Int = 1, showsCancel: Bool? = nil, radio: String = "ready",
  candidateIds: [String] = ["p-1", "p-2"], candidateNames: [String] = ["3F2A", "11C0"],
  candidateSignals: [String] = ["strong", "medium"], noHubYet: Bool = false,
  selectedId: String? = "p-1", hub: String? = "3F2A", codeText: String = "",
  codeError: String? = nil, codeWorking: Bool = false, codeAccepted: Bool = false,
  deviceId: String? = nil, networksLoaded: Bool = false,
  networkSsids: [String] = ["Novak-Home", "Neighbour", "Office"],
  networkSecurities: [String] = ["wpa2", "wpa3Only", "other"],
  networkSupported: [Bool] = [true, false, false], ssid: String = "",
  otherNetwork: Bool = false, password: String = "", wifiError: String? = nil,
  fingerprint: String? = nil, keyNotice: String? = nil, progressReached: Int = 0,
  elapsedSeconds: Int = 0, outcome: String? = nil, outcomePrimary: String? = nil,
  outcomeSecondary: String? = nil, helpShown: Bool = false, confirmingLeave: Bool = false,
  announcementId: Int = 0, announcementKind: String? = nil, announcementAssertive: Bool = false,
  announcementSsid: String? = nil, announcementSignal: String? = nil,
  announcementOutcome: String? = nil
) -> HubSetupPresentation {
  HubSetupPresentation(
    open: open, step: step, keepAwake: open, showsCancel: showsCancel ?? (step == 1),
    radio: radio, candidateIds: candidateIds, candidateNames: candidateNames,
    candidateSignals: candidateSignals, noHubYet: noHubYet, selectedId: selectedId, hub: hub,
    codeText: codeText, codeError: codeError, codeWorking: codeWorking,
    codeAccepted: codeAccepted, deviceId: deviceId, networksLoaded: networksLoaded,
    networkSsids: networkSsids, networkSecurities: networkSecurities,
    networkSupported: networkSupported, ssid: ssid, otherNetwork: otherNetwork,
    password: password, wifiError: wifiError, siteIds: ["s-1"], siteNames: ["Home garden"],
    selectedSiteId: "s-1", fingerprint: fingerprint, loadingKey: false, keyNotice: keyNotice,
    progressReached: progressReached, elapsedSeconds: elapsedSeconds, outcome: outcome,
    outcomePrimary: outcomePrimary, outcomeSecondary: outcomeSecondary, helpShown: helpShown,
    confirmingLeave: confirmingLeave, announcementId: announcementId,
    announcementKind: announcementKind, announcementAssertive: announcementAssertive,
    announcementHub: "3F2A", announcementSsid: announcementSsid,
    announcementSignal: announcementSignal, announcementOutcome: announcementOutcome,
    serverHost: "coldframe.example.org")
}

/// Fills a catalogue entry the way `String(format:)` does for `%n$@` and `%n$lld`.
private func resolve(_ copy: Copy) throws -> String {
  var value = try #require(try Catalogue.entries()[copy.key.rawValue])
  for (index, argument) in copy.arguments.enumerated() {
    switch argument {
    case .text(let text): value = value.replacingOccurrences(of: "%\(index + 1)$@", with: text)
    case .number(let number):
      value = value.replacingOccurrences(of: "%\(index + 1)$lld", with: String(number))
    }
  }
  return value
}

@Test("UX-DR66 UX-DR39 the flow opens on 01 / 05 with Cancel; later steps show Back")
func counterAndCancel() throws {
  let scan = hubSetup()
  #expect(scan.isOpen)
  #expect(scan.keepAwake)
  #expect(scan.step == .scan)
  #expect(scan.counterCurrent == "01")
  #expect(try resolve(scan.counterTotal) == "/ 05")
  #expect(try resolve(scan.counterDescription) == "Step 1 of 5")
  #expect(scan.backLabel == .modalCancel)
  #expect(try resolve(scan.title) == "Find the Hub")

  let code = hubSetup(step: 2)
  #expect(code.counterCurrent == "02")
  #expect(code.backLabel == .navBack)
  #expect(try resolve(hubSetup(step: 5).title) == "Bringing Hub 3F2A online")
  #expect(!HubSetupPresentation.closed.isOpen)
  #expect(!HubSetupPresentation.closed.keepAwake)
}

@Test("UX-DR66 only the Add a Hub tile starts a flow, and only for Administrators and Owners")
func addHubTile() {
  let tiles = FirstRunStepKind.allCases.enumerated().map {
    FirstRunTilePresentation(step: $1, number: $0 + 1, state: $0 == 0 ? .next : .later)
  }
  func garden(_ actionable: Bool) -> GardenPresentation {
    GardenPresentation(
      siteName: "Home garden", role: actionable ? .owner : .member, tiles: tiles,
      tilesActionable: actionable, switcherRows: [], menuItems: [], menuEnabled: true,
      showsMemberNotice: !actionable)
  }
  #expect(garden(true).startsFlow(tiles[0]))
  #expect(!garden(true).startsFlow(tiles[1]))
  #expect(!garden(false).startsFlow(tiles[0]))
}

@Test("UX-DR39 leaving after a Hub is selected asks and says nothing is saved")
func leaveConfirmation() throws {
  let asking = hubSetup(step: 3, confirmingLeave: true)
  #expect(asking.confirmingLeave)
  #expect(try resolve(asking.leaveQuestion) == "Stop setting up Hub 3F2A?")
  #expect(try Catalogue.entries()["setup_leave_detail"] == "Nothing is saved on the Hub.")
}

@Test("UX-DR37 candidates keep the core's order, carry their signal and the selected trait")
func candidates() throws {
  let scan = hubSetup()
  #expect(scan.candidates.map(\.hub) == ["3F2A", "11C0"])
  #expect(scan.candidates.map(\.signal) == [.strong, .medium])
  #expect(scan.candidates[0].traits == [.button, .selected])
  #expect(scan.candidates[1].traits == [.button])
  #expect(try resolve(scan.candidates[0].name) == "Hub 3F2A")
  #expect(scan.showsStillScanning)
  #expect(try resolve(try #require(scan.scanAction)) == "Set up Hub 3F2A")
  #expect(hubSetup(selectedId: nil).scanAction == nil)
}

@Test("UX-DR94 Bluetooth off or denied shows the notice with Open Settings; unsupported has none")
func bluetoothNotices() throws {
  for radio in ["off", "unauthorized"] {
    let off = hubSetup(radio: radio)
    #expect(off.radio.notice == .addHubBluetoothNeeded)
    #expect(off.radio.opensSettings)
    #expect(!off.showsStillScanning)
  }
  #expect(hubSetup(radio: "unsupported").radio.opensSettings == false)
  #expect(hubSetup().radio.notice == nil)
  #expect(
    try Catalogue.entries()["add_hub_bluetooth_needed"]
      == "Coldframe needs Bluetooth to find the Hub.")
}

@Test("UX-DR94 no Hub in 30 s says how to wake it while the list keeps scanning")
func noHubYet() throws {
  let waiting = hubSetup(candidateIds: [], candidateNames: [], candidateSignals: [], noHubYet: true)
  #expect(waiting.noHubYet)
  #expect(waiting.showsStillScanning)
  #expect(
    try Catalogue.entries()["add_hub_no_hub"]
      == "No Hub in range yet. Power it on within a few metres; its LED blinks orange while it waits."
  )
}

@Test("UX-DR41 the accepted code shows the chip, the full Device ID and continues to Wi-Fi")
func acceptedCode() throws {
  let accepted = hubSetup(
    step: 2, codeText: "K7M2Q9XP", codeAccepted: true, deviceId: "3f2a9c01b2d4e6f8")
  #expect(accepted.codeAccepted)
  #expect(accepted.deviceId == "3f2a9c01b2d4e6f8")
  #expect(accepted.codeAction == .addHubCodeContinue)
  #expect(try Catalogue.entries()["add_hub_code_accepted"] == "Accepted")
  #expect(hubSetup(step: 2, codeWorking: true).codeAction == .addHubCodeWorking)
  #expect(!hubSetup(step: 2, codeWorking: true).isCodeActionEnabled)
}

@Test("UX-DR95 a wrong setup code keeps the field and names the Hub")
func wrongCode() throws {
  let wrong = hubSetup(step: 2, codeText: "K7M2Q9XQ", codeError: "wrongCode")
  #expect(wrong.codeText == "K7M2Q9XQ")
  #expect(
    try resolve(try #require(wrong.codeErrorMessage))
      == "That setup code doesn't match Hub 3F2A. Check its label or the serial console.")
  #expect(wrong.codeAction == .addHubCodeAction)
}

@Test("UX-DR42 WPA3-only and other rows are hatched, not selectable, and say why")
func wifiRows() throws {
  let wifi = hubSetup(step: 3, networksLoaded: true, ssid: "Novak-Home")
  let rows = try #require(wifi.networks)
  #expect(rows.map(\.security) == [.wpa2, .wpa3Only, .other])
  #expect(rows.map(\.isSupported) == [true, false, false])
  #expect(rows.map(\.isHatched) == [false, true, true])
  #expect(rows[0].isSelected)
  #expect(rows[1].reason == .addHubWifiUnsupported)
  #expect(
    try Catalogue.entries()["add_hub_wifi_unsupported"]
      == "Not supported: the Hub needs WPA2 or mixed WPA2/WPA3.")
  #expect(try resolve(try #require(wifi.wifiAction)) == "Use Novak-Home")
  #expect(hubSetup(step: 3, networksLoaded: false).networks == nil)
  // A security this shell does not know is never selectable.
  let unknown = hubSetup(
    step: 3, networksLoaded: true, networkSsids: ["X"], networkSecurities: ["wpa4"],
    networkSupported: [true])
  #expect(unknown.networks?.first?.isSupported == false)
}

@Test("UX-DR42 Other network types the SSID and keeps the password field")
func otherNetwork() throws {
  let other = hubSetup(step: 3, networksLoaded: true, ssid: "", otherNetwork: true)
  #expect(other.showsPassword)
  #expect(try resolve(try #require(other.wifiAction)) == "Use this network")
  #expect(other.networks?.allSatisfy { !$0.isSelected } == true)
  let open = hubSetup(
    step: 3, networksLoaded: true, networkSsids: ["Cafe"], networkSecurities: ["open"],
    networkSupported: [true], ssid: "Cafe")
  #expect(!open.showsPassword)
}

@Test("UX-DR66 step 4 shows the fingerprint in groups and names the result")
func siteStep() throws {
  let site = hubSetup(step: 4, fingerprint: String(repeating: "7b12", count: 16))
  #expect(site.groupedFingerprint?.hasPrefix("7b12 7b12") == true)
  #expect(try resolve(try #require(site.siteAction)) == "Add Hub 3F2A to Home garden")
  #expect(hubSetup(step: 4).siteAction == nil)
  #expect(hubSetup(step: 4, keyNotice: "unreachable").keyNotice?.message == .noticeUnreachable)
}

@Test("UX-DR40 the four segments advance on real events and read as one progress element")
func progress() throws {
  let joining = hubSetup(step: 5, progressReached: 2, elapsedSeconds: 23)
  #expect(joining.segments.map(\.state) == [.done, .done, .active, .pending])
  #expect(joining.segments[0].state.icon == .checkmark)
  #expect(joining.segments[2].state.icon == .inProgress)
  #expect(joining.segments[3].state.icon == nil)
  let description = joining.progressDescription
  #expect(description.position == 3)
  #expect(description.total == 4)
  #expect(description.activity == .addHubActivityJoining)
  #expect(
    try resolve(
      Copy(
        .addHubProgressDescription, .number(3), .number(4),
        .text(try #require(Catalogue.entries()["add_hub_activity_joining"]))))
      == "Step 3 of 4, joining Wi-Fi")
  #expect(try resolve(joining.elapsed) == "23 s elapsed")
  #expect(try resolve(hubSetup(step: 5, elapsedSeconds: 75).elapsed) == "1 min 15 s elapsed")
  #expect(try Catalogue.entries()["add_hub_usually"] == "Usually under a minute.")
}

@Test("UX-DR55 Hub is online is a success screen with Add a Node")
func online() throws {
  let online = hubSetup(
    step: 5, ssid: "Novak-Home", progressReached: 4, outcome: "online", outcomePrimary: "addNode")
  let outcome = try #require(online.outcome)
  #expect(outcome.isSuccess)
  #expect(outcome.eyebrow == nil)
  #expect(outcome.primary == .addNode)
  #expect(try resolve(outcome.title) == "Hub is online")
  #expect(try resolve(outcome.body) == "Hub 3F2A joined Novak-Home and reports to Home garden.")
}

@Test("UX-DR95 UX-DR55 a wrong Wi-Fi password stops step 5 with Re-enter password / Other network")
func wrongPassword() throws {
  let wrong = hubSetup(
    step: 5, ssid: "Novak-Home", outcome: "wrongPassword", outcomePrimary: "reenterPassword",
    outcomeSecondary: "otherNetwork")
  let outcome = try #require(wrong.outcome)
  #expect(!outcome.isSuccess)
  #expect(try resolve(try #require(outcome.eyebrow)) == "Step 5 stopped")
  #expect(outcome.eyebrowIcon == .errorFilled)
  #expect(try resolve(outcome.title) == "Wrong Wi-Fi password")
  #expect(outcome.primary == .reenterPassword)
  #expect(outcome.secondary == .otherNetwork)
}

@Test("UX-DR95 NETWORK_NOT_FOUND and UNSUPPORTED_SECURITY go back to step 3 with their headline")
func networkOutcomes() throws {
  let notFound = try #require(
    hubSetup(step: 5, ssid: "Novak-Home", outcome: "networkNotFound").outcome)
  #expect(try resolve(notFound.title) == "Hub 3F2A can't hear Novak-Home")
  #expect(notFound.primary == .chooseNetwork)
  let unsupported = try #require(
    hubSetup(step: 5, ssid: "Novak-Home", outcome: "unsupportedSecurity").outcome)
  #expect(try resolve(unsupported.title) == "Novak-Home isn't supported")
}

@Test("UX-DR95 the Server not reached offers Try again and Help reveals the host")
func noServer() throws {
  let noServer = hubSetup(
    step: 5, ssid: "Novak-Home", outcome: "noServer", outcomePrimary: "retryWifi",
    outcomeSecondary: "help")
  let outcome = try #require(noServer.outcome)
  #expect(try resolve(outcome.title) == "Hub 3F2A is on Novak-Home but can't reach your Server.")
  #expect(outcome.primary == .retryWifi)
  #expect(outcome.secondary == .help)
  #expect(outcome.help == nil)
  let help = try #require(
    hubSetup(step: 5, ssid: "Novak-Home", outcome: "noServer", helpShown: true).outcome?.help)
  #expect(
    try resolve(help)
      == "The Hub reached Novak-Home but not coldframe.example.org. Check that this name resolves on your home network."
  )
}

@Test("UX-DR40 UX-DR94 the timeout and a lost connection say nothing was saved and start over")
func timeoutAndLost() throws {
  let timeout = try #require(hubSetup(step: 5, outcome: "timeout").outcome)
  #expect(try resolve(timeout.title) == "Hub 3F2A didn't come online")
  #expect(
    try resolve(timeout.body)
      == "Nothing was saved on the Hub. Keep it near your router and try again.")
  #expect(timeout.primary == .startOver)
  let lost = try #require(hubSetup(step: 3, outcome: "lostConnection").outcome)
  #expect(
    try resolve(lost.title) + ". " + resolve(lost.body)
      == "Lost the connection to Hub 3F2A. Nothing was saved.")
  #expect(lost.primary == .startOver)
}

@Test("UX-DR95 enrolment refusals and a bad fingerprint each have their copy")
func refusals() throws {
  for kind in ["onAnotherSite", "notAllowed", "siteGone", "fingerprintMismatch", "hubRefused"] {
    let outcome = try #require(hubSetup(step: 5, outcome: kind).outcome, "\(kind)")
    #expect(outcome.primary == .close, "\(kind)")
    #expect(!(try resolve(outcome.title)).isEmpty)
  }
  let server = try #require(hubSetup(step: 5, outcome: "serverUnreachable").outcome)
  #expect(server.primary == .startOver)
  let fingerprint = try #require(hubSetup(step: 5, outcome: "fingerprintMismatch").outcome)
  #expect(try resolve(fingerprint.title) == "The Server's enrolment key doesn't check out")
}

@Test("UX-DR105 new candidates and progress are polite; errors are assertive with the next action")
func announcements() throws {
  let found = try #require(
    hubSetup(announcementId: 1, announcementKind: "candidateFound", announcementSignal: "strong")
      .announcement)
  #expect(found.announcement == .polite)
  let foundMessage = try #require(found.message(siteName: "Home garden"))
  let signal = try resolve(foundMessage.parts[0])
  #expect(
    try resolve(
      Copy(key: foundMessage.copy.key, arguments: foundMessage.copy.arguments + [.text(signal)]))
      == "Hub 3F2A found, strong signal.")

  let sent = try #require(
    hubSetup(announcementId: 2, announcementKind: "wifiSent", announcementSsid: "Novak-Home")
      .announcement)
  #expect(sent.announcement == .polite)
  #expect(
    try resolve(try #require(sent.message(siteName: "")).copy) == "Wi-Fi sent. Joining Novak-Home.")

  let server = try #require(
    hubSetup(announcementId: 3, announcementKind: "serverSees").announcement)
  #expect(try resolve(try #require(server.message(siteName: "")).copy) == "Server sees Hub 3F2A.")

  let error = try #require(
    hubSetup(
      announcementId: 4, announcementKind: "error", announcementAssertive: true,
      announcementSsid: "Novak-Home", announcementOutcome: "wrongPassword"
    ).announcement)
  #expect(error.announcement == .assertive)
  let errorMessage = try #require(error.message(siteName: ""))
  let text = try resolve(
    Copy(
      errorMessage.copy.key, .text(try resolve(errorMessage.parts[0])),
      .text(try resolve(errorMessage.parts[1]))))
  #expect(text == "Wrong Wi-Fi password. Re-enter password.")
  #expect(hubSetup(announcementId: 0, announcementKind: "error").announcement == nil)
}

@Test("UX-DR103 the reading-time hold is at least a second plus 60 ms per character")
func readingTimeHold() {
  #expect(readingTime(of: "") >= 1.0)
  #expect(abs(readingTime(of: "0123456789") - 1.6) < 0.0001)
}

@Test("UX-DR66 every snapshot key the core sends has a Swift value")
func snapshotKeys() {
  #expect(
    OutcomeKindValue.allCases.map(\.rawValue) == [
      "online", "wrongPassword", "networkNotFound", "unsupportedSecurity", "noServer", "timeout",
      "lostConnection", "onAnotherSite", "notAllowed", "siteGone", "serverUnreachable",
      "fingerprintMismatch", "hubRefused",
    ])
  #expect(
    OutcomeActionKind.allCases.map(\.rawValue) == [
      "addNode", "reenterPassword", "otherNetwork", "chooseNetwork", "retryWifi", "help",
      "startOver", "close",
    ])
  #expect(
    SetupAnnouncementKind.allCases.map(\.rawValue) == [
      "candidateFound", "wifiSent", "serverSees", "wrongCode", "error",
    ])
  #expect(
    NetworkSecurityKind.allCases.map(\.rawValue) == [
      "open", "wpa2", "wpa3Transition", "wpa3Only", "other",
    ])
  #expect(
    RadioStateKind.allCases.map(\.rawValue) == ["ready", "off", "unauthorized", "unsupported"])
}

@Test("UX-DR66 the Bluetooth usage text is in both Info.plists")
func bluetoothUsage() throws {
  for plist in ["apps/swift/ios/Config/Info.plist", "apps/swift/ios/Config/Info-Debug.plist"] {
    #expect(try Repo.text(plist).contains("NSBluetoothAlwaysUsageDescription"), "\(plist)")
  }
}
