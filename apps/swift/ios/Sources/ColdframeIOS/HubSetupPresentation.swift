import ColdframeDesignTokens
import Foundation

/// One argument of a catalogue entry: text for `%@`, a number for `%lld`.
public enum CopyArgument: Equatable, Sendable {
  case text(String)
  case number(Int)
}

/// A catalogue entry with its arguments, resolved by the views (the catalogue needs Apple's
/// resource tooling); tests read the keys and arguments on Linux.
public struct Copy: Equatable, Sendable {
  public let key: L10n
  public let arguments: [CopyArgument]

  public init(_ key: L10n, _ arguments: CopyArgument...) {
    self.key = key
    self.arguments = arguments
  }

  public init(key: L10n, arguments: [CopyArgument]) {
    self.key = key
    self.arguments = arguments
  }
}

/// The five steps of Add a Hub (UX-DR66).
public enum HubSetupStepKind: Int, CaseIterable, Sendable {
  case scan = 1
  case code
  case wifi
  case site
  case progress

  public static let count = 5

  public var title: L10n {
    switch self {
    case .scan: .addHubTitleScan
    case .code: .addHubTitleCode
    case .wifi: .addHubTitleWifi
    case .site: .addHubTitleSite
    case .progress: .addHubTitleProgress
    }
  }
}

/// Whether Bluetooth can scan, as the core reports it.
public enum RadioStateKind: String, CaseIterable, Sendable {
  case ready
  case off
  case unauthorized
  case unsupported

  /// The step 1 notice (UX-DR94), or nil when scanning.
  public var notice: L10n? {
    switch self {
    case .ready: nil
    case .off, .unauthorized: .addHubBluetoothNeeded
    case .unsupported: .addHubBluetoothUnsupported
    }
  }

  /// Off and denied carry Open Settings; scanning resumes when the radio is ready.
  public var opensSettings: Bool { self == .off || self == .unauthorized }
}

public enum SignalKind: String, CaseIterable, Sendable {
  case strong
  case medium
  case weak

  public var label: L10n {
    switch self {
    case .strong: .addHubSignalStrong
    case .medium: .addHubSignalMedium
    case .weak: .addHubSignalWeak
    }
  }
}

/// A Device candidate tile (UX-DR37): "Hub 3F2A" and its signal, one element, selected with a
/// checkmark and the selected trait.
public struct CandidatePresentation: Equatable, Sendable, Identifiable {
  public let id: String
  public let hub: String
  public let signal: SignalKind
  public let isSelected: Bool

  public init(id: String, hub: String, signal: SignalKind, isSelected: Bool) {
    self.id = id
    self.hub = hub
    self.signal = signal
    self.isSelected = isSelected
  }

  public var name: Copy { Copy(.addHubCandidate, .text(hub)) }
  public var traits: Set<ControlTrait> { isSelected ? [.button, .selected] : [.button] }
}

public enum CodeErrorKind: String, CaseIterable, Sendable {
  case blank
  case wrongCode

  public var message: L10n {
    switch self {
    case .blank: .addHubCodeBlank
    case .wrongCode: .addHubCodeWrong
    }
  }
}

public enum NetworkSecurityKind: String, CaseIterable, Sendable {
  case open
  case wpa2
  case wpa3Transition
  case wpa3Only
  case other

  public var label: L10n {
    switch self {
    case .open: .addHubSecurityOpen
    case .wpa2: .addHubSecurityWpa2
    case .wpa3Transition: .addHubSecurityWpa3Transition
    case .wpa3Only: .addHubSecurityWpa3Only
    case .other: .addHubSecurityOther
    }
  }
}

/// A Wi-Fi network row (UX-DR42): SSID left, security right; WPA3-only and other unsupported
/// networks are hatched, not selectable, and say why inline.
public struct NetworkRowPresentation: Equatable, Sendable, Identifiable {
  public let ssid: String
  public let security: NetworkSecurityKind
  public let isSupported: Bool
  public let isSelected: Bool

  public init(ssid: String, security: NetworkSecurityKind, isSupported: Bool, isSelected: Bool) {
    self.ssid = ssid
    self.security = security
    self.isSupported = isSupported
    self.isSelected = isSelected
  }

  public var id: String { ssid }
  public var isHatched: Bool { !isSupported }
  public var reason: L10n? { isSupported ? nil : .addHubWifiUnsupported }
  public var traits: Set<ControlTrait> { isSelected ? [.button, .selected] : [.button] }
}

public enum WifiErrorKind: String, CaseIterable, Sendable {
  case ssidBlank
  case ssidTooLong
  case passwordTooLong

  public var message: L10n {
    switch self {
    case .ssidBlank: .addHubWifiSsidBlank
    case .ssidTooLong: .addHubWifiSsidTooLong
    case .passwordTooLong: .addHubWifiPasswordTooLong
    }
  }
}

public enum KeyNoticeKind: String, CaseIterable, Sendable {
  case unreachable
  case certificate
  case unexpected

  public var message: L10n {
    switch self {
    case .unreachable: .noticeUnreachable
    case .certificate: .noticeCertificate
    case .unexpected: .addHubKeyUnexpected
    }
  }
}

/// The four segments of the Setup progress (UX-DR40).
public enum ProgressSegmentKind: Int, CaseIterable, Sendable {
  case bluetooth
  case wifiSent
  case joining
  case server

  public var label: L10n {
    switch self {
    case .bluetooth: .addHubSegmentBluetooth
    case .wifiSent: .addHubSegmentWifiSent
    case .joining: .addHubSegmentJoining
    case .server: .addHubSegmentServer
    }
  }

  /// What the progress element says while this segment is active.
  public var activity: L10n {
    switch self {
    case .bluetooth: .addHubActivityBluetooth
    case .wifiSent: .addHubActivityWifiSent
    case .joining: .addHubActivityJoining
    case .server: .addHubActivityServer
    }
  }
}

public enum SegmentStateKind: Sendable {
  case done
  case active
  case pending

  /// Done is `setup-done` with a checkmark, active orange with `in-progress`, pending an outline.
  public var icon: CarbonIcon? {
    switch self {
    case .done: .checkmark
    case .active: .inProgress
    case .pending: nil
    }
  }
}

public struct ProgressSegmentPresentation: Equatable, Sendable, Identifiable {
  public let segment: ProgressSegmentKind
  public let state: SegmentStateKind

  public var id: Int { segment.rawValue }
}

/// Every way Add a Hub ends on an outcome screen (UX-DR55).
public enum OutcomeKindValue: String, CaseIterable, Sendable {
  case online
  case wrongPassword
  case networkNotFound
  case unsupportedSecurity
  case noServer
  case timeout
  case lostConnection
  case onAnotherSite
  case notAllowed
  case siteGone
  case serverUnreachable
  case fingerprintMismatch
  case hubRefused

  public var isSuccess: Bool { self == .online }
}

public enum OutcomeActionKind: String, CaseIterable, Sendable {
  case addNode
  case reenterPassword
  case otherNetwork
  case chooseNetwork
  case retryWifi
  case help
  case startOver
  case close

  public var label: L10n {
    switch self {
    case .addNode: .addHubAddNode
    case .reenterPassword: .addHubReenterPassword
    case .otherNetwork: .addHubOtherNetwork
    case .chooseNetwork: .addHubChooseNetwork
    case .retryWifi: .addHubRetryWifi
    case .help: .addHubHelp
    case .startOver: .addHubStartOver
    case .close: .addHubClose
    }
  }
}

/// An outcome screen: success is full-bleed `support-success`; an error has the eyebrow
/// "Step 5 stopped" with `error--filled`, a plain headline, and never dismisses itself.
public struct OutcomePresentation: Equatable, Sendable {
  public let kind: OutcomeKindValue
  public let title: Copy
  public let body: Copy
  public let help: Copy?
  public let primary: OutcomeActionKind
  public let secondary: OutcomeActionKind?

  public var isSuccess: Bool { kind.isSuccess }
  public var eyebrow: Copy? {
    isSuccess ? nil : Copy(.addHubStopped, .number(HubSetupStepKind.progress.rawValue))
  }
  public var eyebrowIcon: CarbonIcon? { isSuccess ? nil : .errorFilled }

  /// The copy of [kind] for Hub [hub] on [ssid], added to [siteName].
  public static func copy(
    _ kind: OutcomeKindValue, hub: String, ssid: String, siteName: String
  ) -> (title: Copy, body: Copy) {
    switch kind {
    case .online:
      (
        Copy(.addHubOnlineTitle),
        Copy(.addHubOnlineBody, .text(hub), .text(ssid), .text(siteName))
      )
    case .wrongPassword:
      (Copy(.addHubWrongPasswordTitle), Copy(.addHubWrongPasswordBody, .text(hub), .text(ssid)))
    case .networkNotFound:
      (Copy(.addHubNotFoundTitle, .text(hub), .text(ssid)), Copy(.addHubNotFoundBody))
    case .unsupportedSecurity:
      (Copy(.addHubUnsupportedTitle, .text(ssid)), Copy(.addHubUnsupportedBody))
    case .noServer:
      (Copy(.addHubNoServerTitle, .text(hub), .text(ssid)), Copy(.addHubNoServerBody))
    case .timeout:
      (Copy(.addHubTimeoutTitle, .text(hub)), Copy(.addHubTimeoutBody))
    case .lostConnection:
      (Copy(.addHubLostTitle, .text(hub)), Copy(.addHubLostBody))
    case .onAnotherSite:
      (Copy(.addHubOnAnotherSiteTitle, .text(hub)), Copy(.addHubOnAnotherSiteBody))
    case .notAllowed:
      (Copy(.addHubNotAllowedTitle, .text(siteName)), Copy(.addHubNotAllowedBody))
    case .siteGone:
      (Copy(.addHubSiteGoneTitle, .text(siteName)), Copy(.addHubSiteGoneBody))
    case .serverUnreachable:
      (Copy(.addHubUnreachableTitle), Copy(.addHubUnreachableBody, .text(hub)))
    case .fingerprintMismatch:
      (Copy(.addHubFingerprintTitle), Copy(.addHubFingerprintBody))
    case .hubRefused:
      (Copy(.addHubRefusedTitle, .text(hub)), Copy(.addHubRefusedBody))
    }
  }
}

public enum SetupAnnouncementKind: String, CaseIterable, Sendable {
  case candidateFound
  case wifiSent
  case serverSees
  case wrongCode
  case error
}

/// One announcement (UX-DR105), made once per `id`: polite for candidates and progress,
/// assertive for errors. Errors read the headline and the next action.
public struct SetupAnnouncementPresentation: Equatable, Sendable {
  public let id: Int
  public let kind: SetupAnnouncementKind
  public let isAssertive: Bool
  public let hub: String
  public let ssid: String
  public let signal: SignalKind?
  public let outcome: OutcomeKindValue?

  public var announcement: Announcement { isAssertive ? .assertive : .polite }

  /// The message, as catalogue entries: an error is "%1$@. %2$@." of its headline and action.
  public func message(siteName: String) -> (copy: Copy, parts: [Copy])? {
    switch kind {
    case .candidateFound:
      guard let signal else { return nil }
      return (Copy(.addHubFound, .text(hub)), [Copy(signal.label)])
    case .wifiSent:
      return (Copy(.addHubAnnounceWifiSent, .text(ssid)), [])
    case .serverSees:
      return (Copy(.addHubAnnounceServer, .text(hub)), [])
    case .wrongCode:
      return (Copy(.addHubCodeWrong, .text(hub)), [])
    case .error:
      guard let outcome else { return nil }
      let title = OutcomePresentation.copy(outcome, hub: hub, ssid: ssid, siteName: siteName).title
      let action = HubSetupPresentation.primary(of: outcome, helpShown: false)
      return (Copy(.addHubAnnounceError), [title, Copy(action.label)])
    }
  }
}

/// One Site Add a Hub can enrol on.
public struct SiteChoicePresentation: Equatable, Sendable, Identifiable {
  public let id: String
  public let name: String
  public let isSelected: Bool
}

/// Add a Hub as the SwiftUI shell sees it (UX-DR66), built from the core's flat
/// `HubSetupSnapshot`. The shell holds UI only: BLE, crypto and every rule stay in the core.
public struct HubSetupPresentation: Equatable, Sendable {
  public let isOpen: Bool
  public let step: HubSetupStepKind
  public let keepAwake: Bool
  public let showsCancel: Bool
  public let radio: RadioStateKind
  public let candidates: [CandidatePresentation]
  public let noHubYet: Bool
  public let hub: String
  public let codeText: String
  public let codeError: CodeErrorKind?
  public let codeWorking: Bool
  public let codeAccepted: Bool
  public let deviceId: String?
  /// Nil until the Hub's scan list arrives.
  public let networks: [NetworkRowPresentation]?
  public let ssid: String
  public let otherNetwork: Bool
  public let password: String
  public let wifiError: WifiErrorKind?
  public let sites: [SiteChoicePresentation]
  public let fingerprint: String?
  public let loadingKey: Bool
  public let keyNotice: KeyNoticeKind?
  public let progressReached: Int
  public let elapsedSeconds: Int
  public let outcome: OutcomePresentation?
  public let confirmingLeave: Bool
  public let announcement: SetupAnnouncementPresentation?

  public static let closed = HubSetupPresentation(
    open: false, step: 1, keepAwake: false, showsCancel: true, radio: "ready", candidateIds: [],
    candidateNames: [], candidateSignals: [], noHubYet: false, selectedId: nil, hub: nil,
    codeText: "", codeError: nil, codeWorking: false, codeAccepted: false, deviceId: nil,
    networksLoaded: false, networkSsids: [], networkSecurities: [], networkSupported: [], ssid: "",
    otherNetwork: false, password: "", wifiError: nil, siteIds: [], siteNames: [],
    selectedSiteId: nil, fingerprint: nil, loadingKey: false, keyNotice: nil, progressReached: 0,
    elapsedSeconds: 0, outcome: nil, outcomePrimary: nil, outcomeSecondary: nil, helpShown: false,
    confirmingLeave: false, announcementId: 0, announcementKind: nil, announcementAssertive: false,
    announcementHub: nil, announcementSsid: nil, announcementSignal: nil, announcementOutcome: nil,
    serverHost: "")

  /// Mirrors the flat snapshot field for field; unknown names fall back to the safest reading.
  public init(
    open: Bool, step: Int, keepAwake: Bool, showsCancel: Bool, radio: String,
    candidateIds: [String], candidateNames: [String], candidateSignals: [String], noHubYet: Bool,
    selectedId: String?, hub: String?, codeText: String, codeError: String?, codeWorking: Bool,
    codeAccepted: Bool, deviceId: String?, networksLoaded: Bool, networkSsids: [String],
    networkSecurities: [String], networkSupported: [Bool], ssid: String, otherNetwork: Bool,
    password: String, wifiError: String?, siteIds: [String], siteNames: [String],
    selectedSiteId: String?, fingerprint: String?, loadingKey: Bool, keyNotice: String?,
    progressReached: Int, elapsedSeconds: Int, outcome: String?, outcomePrimary: String?,
    outcomeSecondary: String?, helpShown: Bool, confirmingLeave: Bool, announcementId: Int,
    announcementKind: String?, announcementAssertive: Bool, announcementHub: String?,
    announcementSsid: String?, announcementSignal: String?, announcementOutcome: String?,
    serverHost: String
  ) {
    let hub = hub ?? ""
    self.isOpen = open
    self.step = HubSetupStepKind(rawValue: step) ?? .scan
    self.keepAwake = keepAwake
    self.showsCancel = showsCancel
    self.radio = RadioStateKind(rawValue: radio) ?? .off
    let candidateCount = min(candidateIds.count, candidateNames.count, candidateSignals.count)
    // Strongest first, as the core sorted them; never re-sorted here.
    self.candidates = (0..<candidateCount).map {
      CandidatePresentation(
        id: candidateIds[$0], hub: candidateNames[$0],
        signal: SignalKind(rawValue: candidateSignals[$0]) ?? .weak,
        isSelected: candidateIds[$0] == selectedId)
    }
    self.noHubYet = noHubYet
    self.hub = hub
    self.codeText = codeText
    self.codeError = codeError.flatMap(CodeErrorKind.init(rawValue:))
    self.codeWorking = codeWorking
    self.codeAccepted = codeAccepted
    self.deviceId = deviceId
    if networksLoaded {
      let count = min(networkSsids.count, networkSecurities.count, networkSupported.count)
      self.networks = (0..<count).map {
        // An unknown security is never selectable.
        let security = NetworkSecurityKind(rawValue: networkSecurities[$0]) ?? .other
        let supported = networkSupported[$0] && security != .wpa3Only && security != .other
        return NetworkRowPresentation(
          ssid: networkSsids[$0], security: security, isSupported: supported,
          isSelected: supported && !otherNetwork && networkSsids[$0] == ssid)
      }
    } else {
      self.networks = nil
    }
    self.ssid = ssid
    self.otherNetwork = otherNetwork
    self.password = password
    self.wifiError = wifiError.flatMap(WifiErrorKind.init(rawValue:))
    let siteCount = min(siteIds.count, siteNames.count)
    self.sites = (0..<siteCount).map {
      SiteChoicePresentation(
        id: siteIds[$0], name: siteNames[$0], isSelected: siteIds[$0] == selectedSiteId)
    }
    self.fingerprint = fingerprint
    self.loadingKey = loadingKey
    self.keyNotice = keyNotice.flatMap(KeyNoticeKind.init(rawValue:))
    self.progressReached = max(0, min(progressReached, ProgressSegmentKind.allCases.count))
    self.elapsedSeconds = elapsedSeconds
    let siteName = zip(siteIds, siteNames).first { $0.0 == selectedSiteId }?.1 ?? ""
    if let kind = outcome.flatMap(OutcomeKindValue.init(rawValue:)) {
      let copy = OutcomePresentation.copy(kind, hub: hub, ssid: ssid, siteName: siteName)
      let help =
        kind == .noServer && helpShown
        ? Copy(.addHubNoServerHelp, .text(ssid), .text(serverHost)) : nil
      self.outcome = OutcomePresentation(
        kind: kind, title: copy.title, body: copy.body, help: help,
        primary: outcomePrimary.flatMap(OutcomeActionKind.init(rawValue:))
          ?? Self.primary(of: kind, helpShown: helpShown),
        secondary: outcomeSecondary.flatMap(OutcomeActionKind.init(rawValue:)))
    } else {
      self.outcome = nil
    }
    self.confirmingLeave = confirmingLeave
    if announcementId > 0,
      let kind = announcementKind.flatMap(SetupAnnouncementKind.init(rawValue:))
    {
      self.announcement = SetupAnnouncementPresentation(
        id: announcementId, kind: kind, isAssertive: announcementAssertive,
        hub: announcementHub ?? hub, ssid: announcementSsid ?? "",
        signal: announcementSignal.flatMap(SignalKind.init(rawValue:)),
        outcome: announcementOutcome.flatMap(OutcomeKindValue.init(rawValue:)))
    } else {
      self.announcement = nil
    }
  }

  /// The primary action of an outcome, as the core defines it.
  public static func primary(of kind: OutcomeKindValue, helpShown: Bool) -> OutcomeActionKind {
    switch kind {
    case .online: .addNode
    case .wrongPassword: .reenterPassword
    case .networkNotFound, .unsupportedSecurity: .chooseNetwork
    case .noServer: .retryWifi
    case .timeout, .lostConnection, .serverUnreachable: .startOver
    case .onAnotherSite, .notAllowed, .siteGone, .fingerprintMismatch, .hubRefused: .close
    }
  }

  // Setup flow shell (UX-DR39)

  /// "01" in `primary-text`, then "/ 05" in `text-helper`.
  public var counterCurrent: String { Self.twoDigits(step.rawValue) }
  public var counterTotal: Copy {
    Copy(.setupStepOf, .text(Self.twoDigits(HubSetupStepKind.count)))
  }
  /// Read as one element: "Step 1 of 5".
  public var counterDescription: Copy {
    Copy(.setupStepDescription, .number(step.rawValue), .number(HubSetupStepKind.count))
  }

  /// Cancel on step 1, Back afterwards.
  public var backLabel: L10n { showsCancel ? .modalCancel : .navBack }

  public var title: Copy {
    step == .progress ? Copy(step.title, .text(hub)) : Copy(step.title)
  }

  public var leaveQuestion: Copy { Copy(.setupLeaveQuestion, .text(hub)) }

  // Step 1

  public var selectedCandidate: CandidatePresentation? { candidates.first(where: \.isSelected) }

  public var showsStillScanning: Bool { radio == .ready }

  public var scanAction: Copy? {
    selectedCandidate.map { Copy(.addHubSelectAction, .text($0.hub)) }
  }

  // Step 2

  public var codeErrorMessage: Copy? { codeError.map { Copy($0.message, .text(hub)) } }

  public var codeAction: L10n {
    codeAccepted ? .addHubCodeContinue : (codeWorking ? .addHubCodeWorking : .addHubCodeAction)
  }

  public var isCodeActionEnabled: Bool { !codeWorking }

  // Step 3

  public var showsPassword: Bool {
    guard !otherNetwork else { return true }
    return networks?.first { $0.ssid == ssid }?.security != .open
  }

  public var wifiAction: Copy? {
    if !ssid.isEmpty { return Copy(.addHubWifiAction, .text(ssid)) }
    return otherNetwork ? Copy(.addHubWifiActionOther) : nil
  }

  // Step 4

  public var selectedSite: SiteChoicePresentation? { sites.first(where: \.isSelected) }

  /// The 64 hex digits in groups of four, for reading aloud and comparing.
  public var groupedFingerprint: String? {
    fingerprint.map { value in
      stride(from: 0, to: value.count, by: 4).map { start -> String in
        let from = value.index(value.startIndex, offsetBy: start)
        let to = value.index(from, offsetBy: min(4, value.count - start))
        return String(value[from..<to])
      }.joined(separator: " ")
    }
  }

  public var siteAction: Copy? {
    guard fingerprint != nil, let site = selectedSite else { return nil }
    return Copy(.addHubSiteAction, .text(hub), .text(site.name))
  }

  // Step 5 (UX-DR40)

  public var segments: [ProgressSegmentPresentation] {
    ProgressSegmentKind.allCases.map { segment in
      let state: SegmentStateKind =
        segment.rawValue < progressReached
        ? .done : (segment.rawValue == progressReached ? .active : .pending)
      return ProgressSegmentPresentation(segment: segment, state: state)
    }
  }

  /// One progress element: "Step 3 of 4, joining Wi-Fi".
  public var progressDescription: (position: Int, total: Int, activity: L10n) {
    let total = ProgressSegmentKind.allCases.count
    let active = ProgressSegmentKind(rawValue: progressReached)
    return (min(progressReached + 1, total), total, active?.activity ?? .addHubActivityDone)
  }

  /// Never announced.
  public var elapsed: Copy {
    elapsedSeconds < 60
      ? Copy(.addHubElapsedSeconds, .number(elapsedSeconds))
      : Copy(.addHubElapsedMinutes, .number(elapsedSeconds / 60), .number(elapsedSeconds % 60))
  }

  public var selectedSiteName: String { selectedSite?.name ?? "" }

  static func twoDigits(_ value: Int) -> String {
    value < 10 ? "0\(value)" : "\(value)"
  }
}

/// A screen reader's pace, generously: at least 1 s plus 60 ms per character. iOS reports when
/// an announcement finishes; this bounds the hold if it never does (UX-DR103).
public func readingTime(of message: String) -> Double {
  1.0 + 0.06 * Double(message.count)
}

/// Add a Hub of the shared Kotlin core, as the SwiftUI shell sees it. The app target adapts
/// `IosHubSetup` of `ColdframeCore`; BLE and the crypto never leave the core.
@MainActor
public protocol HubSetupService: AnyObject {
  func observe(_ onChange: @escaping @MainActor (HubSetupPresentation) -> Void)
  func open()
  func close()
  func recheckRadio()
  /// Whether VoiceOver is reading an announcement; the progress timeout waits (UX-DR103).
  func announcing(_ active: Bool)
  func back()
  func leave()
  func confirmLeave()
  func stayInFlow()
  func select(candidateId: String)
  func continueFromScan()
  func setCode(_ text: String)
  func submitCode()
  func continueFromCode()
  func chooseNetwork(_ ssid: String)
  func chooseOtherNetwork()
  func setOtherSsid(_ ssid: String)
  func setPassword(_ password: String)
  func continueFromWifi()
  func chooseSite(_ siteId: String)
  func retryKey()
  func start()
  func outcomeAction(_ action: OutcomeActionKind)
}
