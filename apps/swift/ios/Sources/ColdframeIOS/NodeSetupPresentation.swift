import ColdframeDesignTokens
import Foundation

/// The five steps of Add a Node (UX-DR67).
public enum NodeSetupStepKind: Int, CaseIterable, Sendable {
  case press = 1
  case scan
  case code
  case lot
  case outcome

  public static let count = 5

  /// The step title, a headline. The outcome step has its own screen.
  public var title: L10n {
    switch self {
    case .press: .addNodeTitlePress
    case .scan: .addNodeTitleScan
    case .code: .addNodeTitleCode
    case .lot, .outcome: .addNodeTitleLot
    }
  }
}

extension RadioStateKind {
  /// The step 2 notice of Add a Node (UX-DR94), or nil when scanning.
  public var nodeNotice: L10n? {
    switch self {
    case .ready: nil
    case .off, .unauthorized: .addNodeBluetoothNeeded
    case .unsupported: .addNodeBluetoothUnsupported
    }
  }
}

extension CodeErrorKind {
  /// The reason under the Node's setup code field; the wrong-code one names the Node (`%@`).
  public var nodeMessage: L10n {
    switch self {
    case .blank: .addNodeCodeBlank
    case .wrongCode: .addNodeCodeWrong
    }
  }
}

/// A Node candidate tile (UX-DR37): "Node 7C19" and its signal, one element, selected with a
/// checkmark and the selected trait. "Pressed just now" marks the Node first heard last. Neither
/// the advert nor the Identity carries a battery level or a Sensor count, so the tile has none.
public struct NodeCandidatePresentation: Equatable, Sendable, Identifiable {
  public let id: String
  public let node: String
  public let signal: SignalKind
  public let pressedJustNow: Bool
  public let isSelected: Bool

  public init(id: String, node: String, signal: SignalKind, pressedJustNow: Bool, isSelected: Bool)
  {
    self.id = id
    self.node = node
    self.signal = signal
    self.pressedJustNow = pressedJustNow
    self.isSelected = isSelected
  }

  public var name: Copy { Copy(.addNodeCandidate, .text(node)) }

  /// "Pressed just now", only on the most recent setup-mode advertiser.
  public var badge: L10n? { pressedJustNow ? .addNodePressed : nil }

  /// The spoken label, given the resolved signal words: "Node 7C19, pressed just now, strong
  /// signal", or "Node 7C19, strong signal".
  public func description(signal: String) -> Copy {
    Copy(
      pressedJustNow ? .addNodeCandidateDescriptionPressed : .addNodeCandidateDescription,
      .text(node), .text(signal))
  }

  public var traits: Set<ControlTrait> { isSelected ? [.button, .selected] : [.button] }
}

/// One Lot of the Lot picker (UX-DR38). A Lot that has a Node cannot be picked and says why in
/// words ("Has a Node"), not by colour alone.
public struct LotChoicePresentation: Equatable, Sendable, Identifiable {
  public let id: String
  public let name: String
  public let isSelectable: Bool
  public let isSelected: Bool

  public init(id: String, name: String, isSelectable: Bool, isSelected: Bool) {
    self.id = id
    self.name = name
    self.isSelectable = isSelectable
    self.isSelected = isSelected
  }

  /// "Has a Node" under the name of a Lot that cannot be picked.
  public var reason: L10n? { isSelectable ? nil : .addNodeLotHasNode }

  /// The spoken label when it is not the name alone: "Beans, has a Node".
  public var description: Copy? {
    isSelectable ? nil : Copy(.addNodeLotDescriptionHasNode, .text(name))
  }

  public var traits: Set<ControlTrait> { isSelected ? [.button, .selected] : [.button] }
}

/// The notice on step 4, named as the core names it.
public enum LotPickerNoticeKind: String, CaseIterable, Sendable {
  case unreachable
  case certificate
  case unexpected
  case lotTaken
  case lotGone
  case assignFailed

  public var message: L10n {
    switch self {
    case .unreachable: .noticeUnreachable
    case .certificate: .noticeCertificate
    case .unexpected: .addNodeLotsUnexpected
    case .lotTaken: .addNodeLotTaken
    case .lotGone: .addNodeLotGone
    case .assignFailed: .addNodeAssignFailed
    }
  }

  /// Lot taken names the Lot (`%@`).
  public var takesLot: Bool { self == .lotTaken }
}

/// The inline "+ New Lot" field: the same field, check and copy as Create Lot in Site settings.
public struct NewLotPresentation: Equatable, Sendable {
  public let name: String
  public let error: NameErrorKind?
  public let working: Bool

  public init(name: String, error: NameErrorKind?, working: Bool) {
    self.name = name
    self.error = error
    self.working = working
  }

  public let label: L10n = .siteSettingsLotName
  public let helper: L10n = .siteSettingsLotNameHelper
  public var errorMessage: L10n? { error?.lotMessage }
  /// "Create Lot", "Creating Lot…" in place while working.
  public var buttonLabel: L10n { working ? .siteSettingsCreatingLot : .siteSettingsCreateLot }
  public var isButtonEnabled: Bool { !working }
}

/// Every way Add a Node ends on an outcome screen (UX-DR55).
public enum NodeOutcomeKindValue: String, CaseIterable, Sendable {
  case assigned
  case stoppedListening
  case lostConnection
  case nodeRefused
  case serverUnreachable
  case fingerprintMismatch
  case alreadyAssigned
  case onAnotherSite
  case notAllowed

  public var isSuccess: Bool { self == .assigned }
}

/// The one action of an Add a Node outcome screen.
public enum NodeOutcomeActionKind: String, CaseIterable, Sendable {
  case done
  case startOver
  case close
  /// Opens Calibrate for the Lot that got the Node; the success outcome's second action, for
  /// Administrators and Owners (Story 5.2).
  case calibrate

  public var label: L10n {
    switch self {
    case .done: .addNodeDone
    case .startOver: .addNodeStartOver
    case .close: .addNodeClose
    case .calibrate: .calibrateAction
    }
  }
}

/// An Add a Node outcome screen: success is full-bleed `support-success`; an error has the
/// eyebrow "Step 3 stopped" with `error--filled`, naming the step it stopped on, a plain
/// headline, and never dismisses itself.
public struct NodeOutcomePresentation: Equatable, Sendable {
  public let kind: NodeOutcomeKindValue
  public let title: Copy
  public let body: Copy
  public let primary: NodeOutcomeActionKind
  /// Calibrate, beside Done, on a success the core says may calibrate; nil otherwise.
  public let secondary: NodeOutcomeActionKind?
  public let stoppedStep: Int

  public var isSuccess: Bool { kind.isSuccess }
  public var eyebrow: Copy? { isSuccess ? nil : Copy(.addNodeStopped, .number(stoppedStep)) }
  public var eyebrowIcon: CarbonIcon? { isSuccess ? nil : .errorFilled }

  /// The copy of [kind] for Node [node] in [lot] on [siteName].
  public static func copy(
    _ kind: NodeOutcomeKindValue, node: String, lot: String, siteName: String
  ) -> (title: Copy, body: Copy) {
    switch kind {
    case .assigned:
      (Copy(.addNodeAssignedTitle, .text(lot)), Copy(.addNodeAssignedBody, .text(node), .text(lot)))
    case .stoppedListening:
      (Copy(.addNodeStoppedListeningTitle, .text(node)), Copy(.addNodeStoppedListeningBody))
    case .lostConnection:
      (Copy(.addNodeLostTitle, .text(node)), Copy(.addNodeLostBody))
    case .nodeRefused:
      (Copy(.addNodeRefusedTitle, .text(node)), Copy(.addNodeRefusedBody))
    case .serverUnreachable:
      (Copy(.addNodeUnreachableTitle), Copy(.addNodeUnreachableBody, .text(node)))
    case .fingerprintMismatch:
      (Copy(.addNodeFingerprintTitle), Copy(.addNodeFingerprintBody))
    case .alreadyAssigned:
      (Copy(.addNodeAlreadyAssignedTitle, .text(node)), Copy(.addNodeAlreadyAssignedBody))
    case .onAnotherSite:
      (Copy(.addNodeOnAnotherSiteTitle, .text(node)), Copy(.addNodeOnAnotherSiteBody))
    case .notAllowed:
      (Copy(.addNodeNotAllowedTitle, .text(siteName)), Copy(.addNodeNotAllowedBody))
    }
  }
}

public enum NodeAnnouncementKind: String, CaseIterable, Sendable {
  case candidateFound
  case wrongCode
  case lotTaken
  case error
  case assigned
}

/// One announcement of Add a Node (UX-DR105), made once per `id`: polite for a new candidate and
/// the assignment, assertive for a wrong code, a taken Lot and errors. Errors read the headline
/// and the next action.
public struct NodeAnnouncementPresentation: Equatable, Sendable {
  public let id: Int
  public let kind: NodeAnnouncementKind
  public let isAssertive: Bool
  public let node: String
  public let lot: String
  public let signal: SignalKind?
  public let outcome: NodeOutcomeKindValue?

  public var announcement: Announcement { isAssertive ? .assertive : .polite }

  /// The message, as catalogue entries: an error is "%1$@. %2$@." of its headline and action.
  public func message(siteName: String) -> (copy: Copy, parts: [Copy])? {
    switch kind {
    case .candidateFound:
      guard let signal else { return nil }
      return (Copy(.addNodeFound, .text(node)), [Copy(signal.label)])
    case .wrongCode:
      return (Copy(.addNodeCodeWrong, .text(node)), [])
    case .lotTaken:
      // The text of the notice on step 4.
      return (Copy(.addNodeLotTaken, .text(lot)), [])
    case .assigned:
      return (Copy(.addNodeAnnounceAssigned, .text(lot)), [])
    case .error:
      guard let outcome else { return nil }
      let title = NodeOutcomePresentation.copy(
        outcome, node: node, lot: lot, siteName: siteName
      ).title
      let action = NodeSetupPresentation.primary(of: outcome)
      return (Copy(.addNodeAnnounceError), [title, Copy(action.label)])
    }
  }
}

/// Add a Node as the SwiftUI shell sees it (UX-DR67), built from the core's flat
/// `NodeSetupSnapshot`. The shell holds UI only: BLE, crypto and every rule stay in the core.
public struct NodeSetupPresentation: Equatable, Sendable {
  public let isOpen: Bool
  public let step: NodeSetupStepKind
  public let keepAwake: Bool
  public let showsCancel: Bool
  public let radio: RadioStateKind
  public let candidates: [NodeCandidatePresentation]
  public let noNodeYet: Bool
  public let node: String
  public let codeText: String
  public let codeError: CodeErrorKind?
  public let codeWorking: Bool
  public let codeAccepted: Bool
  public let deviceId: String?
  public let siteName: String
  /// Nil until the Lots of the Site arrive.
  public let lots: [LotChoicePresentation]?
  /// The picked Lot's name, for the primary button and the outcome.
  public let lotName: String
  /// The inline name field; nil while "+ New Lot" is a tile, and until the Lots arrive.
  public let newLot: NewLotPresentation?
  public let assigning: Bool
  public let lotNotice: LotPickerNoticeKind?
  public let lotNoticeLot: String?
  public let lotsRetryable: Bool
  public let outcome: NodeOutcomePresentation?
  public let confirmingLeave: Bool
  public let announcement: NodeAnnouncementPresentation?

  public static let closed = NodeSetupPresentation(
    open: false, step: 1, keepAwake: false, showsCancel: true, radio: "ready", candidateIds: [],
    candidateNames: [], candidateSignals: [], candidatePressed: [], noNodeYet: false,
    selectedId: nil, node: nil, codeText: "", codeError: nil, codeWorking: false,
    codeAccepted: false, deviceId: nil, siteName: "", lotsLoaded: false, lotIds: [], lotNames: [],
    lotSelectable: [], selectedLotId: nil, lotName: nil, newLotOpen: false, newLotName: "",
    newLotError: nil, creatingLot: false, assigning: false, lotNotice: nil, lotNoticeLot: nil,
    lotsRetryable: false, outcome: nil, outcomePrimary: nil, outcomeSecondary: nil, stoppedStep: 0,
    confirmingLeave: false, announcementId: 0, announcementKind: nil,
    announcementAssertive: false, announcementNode: nil, announcementLot: nil,
    announcementSignal: nil, announcementOutcome: nil)

  /// Mirrors the flat snapshot field for field; unknown names fall back to the safest reading.
  public init(
    open: Bool, step: Int, keepAwake: Bool, showsCancel: Bool, radio: String,
    candidateIds: [String], candidateNames: [String], candidateSignals: [String],
    candidatePressed: [Bool], noNodeYet: Bool, selectedId: String?, node: String?,
    codeText: String, codeError: String?, codeWorking: Bool, codeAccepted: Bool,
    deviceId: String?, siteName: String, lotsLoaded: Bool, lotIds: [String], lotNames: [String],
    lotSelectable: [Bool], selectedLotId: String?, lotName: String?, newLotOpen: Bool,
    newLotName: String, newLotError: String?, creatingLot: Bool, assigning: Bool,
    lotNotice: String?, lotNoticeLot: String?, lotsRetryable: Bool, outcome: String?,
    outcomePrimary: String?, outcomeSecondary: String? = nil, stoppedStep: Int,
    confirmingLeave: Bool, announcementId: Int,
    announcementKind: String?, announcementAssertive: Bool, announcementNode: String?,
    announcementLot: String?, announcementSignal: String?, announcementOutcome: String?
  ) {
    let node = node ?? ""
    let lotName = lotName ?? ""
    self.isOpen = open
    self.step = NodeSetupStepKind(rawValue: step) ?? .press
    self.keepAwake = keepAwake
    self.showsCancel = showsCancel
    self.radio = RadioStateKind(rawValue: radio) ?? .off
    let candidateCount = min(
      candidateIds.count, candidateNames.count, candidateSignals.count, candidatePressed.count)
    // Strongest first, as the core sorted them; never re-sorted here.
    self.candidates = (0..<candidateCount).map {
      NodeCandidatePresentation(
        id: candidateIds[$0], node: candidateNames[$0],
        signal: SignalKind(rawValue: candidateSignals[$0]) ?? .weak,
        pressedJustNow: candidatePressed[$0], isSelected: candidateIds[$0] == selectedId)
    }
    self.noNodeYet = noNodeYet
    self.node = node
    self.codeText = codeText
    self.codeError = codeError.flatMap(CodeErrorKind.init(rawValue:))
    self.codeWorking = codeWorking
    self.codeAccepted = codeAccepted
    self.deviceId = deviceId
    self.siteName = siteName
    if lotsLoaded {
      let count = min(lotIds.count, lotNames.count, lotSelectable.count)
      // The Server's order, never re-sorted. A Lot that has a Node is never selected.
      self.lots = (0..<count).map {
        LotChoicePresentation(
          id: lotIds[$0], name: lotNames[$0], isSelectable: lotSelectable[$0],
          isSelected: lotSelectable[$0] && lotIds[$0] == selectedLotId)
      }
      self.newLot =
        newLotOpen
        ? NewLotPresentation(
          name: newLotName, error: newLotError.flatMap(NameErrorKind.init(rawValue:)),
          working: creatingLot)
        : nil
    } else {
      self.lots = nil
      self.newLot = nil
    }
    self.lotName = lotName
    self.assigning = assigning
    // An unknown notice still says that nothing changed.
    let notice = lotNotice.map { LotPickerNoticeKind(rawValue: $0) ?? .unexpected }
    self.lotNotice = notice
    self.lotNoticeLot = notice?.takesLot == true ? (lotNoticeLot ?? "") : nil
    self.lotsRetryable = lotsRetryable
    if let kind = outcome.flatMap(NodeOutcomeKindValue.init(rawValue:)) {
      let copy = NodeOutcomePresentation.copy(kind, node: node, lot: lotName, siteName: siteName)
      self.outcome = NodeOutcomePresentation(
        kind: kind, title: copy.title, body: copy.body,
        primary: outcomePrimary.flatMap(NodeOutcomeActionKind.init(rawValue:))
          ?? Self.primary(of: kind),
        secondary: kind.isSuccess
          ? outcomeSecondary.flatMap(NodeOutcomeActionKind.init(rawValue:)).flatMap {
            $0 == .calibrate ? $0 : nil
          } : nil,
        stoppedStep: stoppedStep)
    } else {
      self.outcome = nil
    }
    self.confirmingLeave = confirmingLeave
    if announcementId > 0,
      let kind = announcementKind.flatMap(NodeAnnouncementKind.init(rawValue:))
    {
      self.announcement = NodeAnnouncementPresentation(
        id: announcementId, kind: kind, isAssertive: announcementAssertive,
        node: announcementNode ?? node, lot: announcementLot ?? lotName,
        signal: announcementSignal.flatMap(SignalKind.init(rawValue:)),
        outcome: announcementOutcome.flatMap(NodeOutcomeKindValue.init(rawValue:)))
    } else {
      self.announcement = nil
    }
  }

  /// The primary action of an outcome, as the core defines it.
  public static func primary(of kind: NodeOutcomeKindValue) -> NodeOutcomeActionKind {
    switch kind {
    case .assigned: .done
    case .stoppedListening, .lostConnection, .serverUnreachable: .startOver
    case .nodeRefused, .fingerprintMismatch, .alreadyAssigned, .onAnotherSite, .notAllowed: .close
    }
  }

  // Setup flow shell (UX-DR39)

  /// "01" in `primary-text`, then "/ 05" in `text-helper`.
  public var counterCurrent: String { HubSetupPresentation.twoDigits(step.rawValue) }
  public var counterTotal: Copy {
    Copy(.setupStepOf, .text(HubSetupPresentation.twoDigits(NodeSetupStepKind.count)))
  }
  /// Read as one element: "Step 1 of 5".
  public var counterDescription: Copy {
    Copy(.setupStepDescription, .number(step.rawValue), .number(NodeSetupStepKind.count))
  }

  /// Cancel on step 1, Back afterwards. Both call `back`: the core closes on step 1, goes one
  /// step back on 2 and 3, and asks before leaving on step 4.
  public var backLabel: L10n { showsCancel ? .modalCancel : .navBack }

  public var title: Copy { Copy(step.title) }

  /// "Stop setting up Node 7C19?"
  public var leaveQuestion: Copy { Copy(.addNodeLeaveQuestion, .text(node)) }
  /// "Nothing is saved on the Node."
  public let leaveDetail: L10n = .addNodeLeaveDetail

  // Step 2

  public var selectedCandidate: NodeCandidatePresentation? {
    candidates.first(where: \.isSelected)
  }

  public var showsStillScanning: Bool { radio == .ready }

  public var scanAction: Copy? {
    selectedCandidate.map { Copy(.addNodeSelectAction, .text($0.node)) }
  }

  // Step 3

  public var codeErrorMessage: Copy? {
    codeError.map { $0 == .wrongCode ? Copy($0.nodeMessage, .text(node)) : Copy($0.nodeMessage) }
  }

  /// "Check setup code", "Checking setup code…" in place while working, then "Pick a Lot".
  public var codeAction: L10n {
    codeAccepted ? .addNodeCodeContinue : (codeWorking ? .addNodeCodeWorking : .addNodeCodeAction)
  }

  public var isCodeActionEnabled: Bool { !codeWorking }

  // Step 4 (UX-DR38)

  public var lotIntro: Copy { Copy(.addNodeLotIntro, .text(node), .text(siteName)) }

  /// "Fetching the Lots of Home garden…" until the Lots or a notice arrive.
  public var lotsLoading: Copy? {
    lots == nil && lotNotice == nil ? Copy(.addNodeLotsLoading, .text(siteName)) : nil
  }

  /// Try again shows only where reading the Lots again can help.
  public var offersLotsRetry: Bool { lotNotice != nil && lotsRetryable }

  /// "+ New Lot" is a tile until it opens the inline name field.
  public var showsNewLotTile: Bool { lots != nil && newLot == nil }

  public var selectedLot: LotChoicePresentation? { lots?.first(where: \.isSelected) }

  /// "Put 7C19 in Tomatoes", "Putting 7C19 in Tomatoes…" in place while working; nil until a
  /// Lot is picked.
  public var assignAction: Copy? {
    selectedLot.map {
      Copy(assigning ? .addNodeAssignWorking : .addNodeAssignAction, .text(node), .text($0.name))
    }
  }

  public var isAssignEnabled: Bool { !assigning }
}

/// Add a Node of the shared Kotlin core, as the SwiftUI shell sees it. The app target adapts
/// `IosNodeSetup` of `ColdframeCore`; BLE and the crypto never leave the core.
@MainActor
public protocol NodeSetupService: AnyObject {
  func observe(_ onChange: @escaping @MainActor (NodeSetupPresentation) -> Void)
  /// Opens on the current Site; `lotId` is the Lot of a *no Node* tile, or nil from Devices.
  func open(lotId: String?)
  func close()
  func recheckRadio()
  /// Whether VoiceOver is reading an announcement (UX-DR103).
  func announcing(_ active: Bool)
  func back()
  func leave()
  func confirmLeave()
  func stayInFlow()
  func continueFromPress()
  func select(candidateId: String)
  func continueFromScan()
  func setCode(_ text: String)
  func submitCode()
  func continueFromCode()
  func retryLots()
  func chooseLot(_ lotId: String)
  func openNewLot()
  func setNewLotName(_ name: String)
  func createLot()
  func assign()
  /// Forwarded to the core as the action's raw value, the snapshot's `outcomePrimary`.
  func outcomeAction(_ action: NodeOutcomeActionKind)
}

extension NodeSetupPresentation {
  /// What the flow posts for the current announcement (UX-DR105), or nil when there is none.
  /// Nothing is posted for a Lot taken meanwhile: the notice on step 4 announces itself,
  /// assertively, so it is said once, there.
  public func spokenAnnouncement(resolve: (Copy) throws -> String) rethrows -> String? {
    guard let announcement, announcement.kind != .lotTaken,
      let message = announcement.message(siteName: siteName)
    else { return nil }
    return try setupAnnouncementText(
      message, isError: announcement.kind == .error, resolve: resolve)
  }
}
