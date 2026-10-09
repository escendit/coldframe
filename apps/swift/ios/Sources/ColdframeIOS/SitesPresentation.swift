import Foundation

/// A Role on one Site, as the Server reported it (never computed here, AD-14).
public enum SiteRoleKind: String, CaseIterable, Sendable {
  case owner
  case administrator
  case member

  public var label: L10n {
    switch self {
    case .owner: .roleOwner
    case .administrator: .roleAdministrator
    case .member: .roleMember
    }
  }
}

/// A notice on the Sites surfaces, named as the core names it.
public enum SitesNoticeKind: String, CaseIterable, Sendable {
  case unreachable
  case certificate
  case identityProviderUnavailable
  case keyReused
  case unexpected

  /// The copy when the Sites could not be listed. Unreachable and Certificate reuse the
  /// sign-in notices.
  public var loadMessage: L10n {
    switch self {
    case .unreachable: .noticeUnreachable
    case .certificate: .noticeCertificate
    default: .sitesUnexpected
    }
  }

  /// The copy on Create Site: it says the Site was not created and to try again.
  public var createMessage: L10n {
    switch self {
    case .unreachable: .noticeUnreachable
    case .certificate: .noticeCertificate
    case .identityProviderUnavailable: .createSiteUnavailable
    case .keyReused: .createSiteKeyReused
    case .unexpected: .createSiteUnexpected
    }
  }

  /// A certificate failure is never retried insecurely, so it has no action (AD-13).
  public var action: NoticeActionKind? {
    self == .certificate ? nil : .tryAgain
  }

  public var announcement: Announcement { .assertive }
}

/// The one-line reason under the Site name field.
public enum NameErrorKind: String, CaseIterable, Sendable {
  case blank
  case tooLong

  public var message: L10n {
    switch self {
    case .blank: .createSiteNameBlank
    case .tooLong: .createSiteNameTooLong
    }
  }
}

/// The time-zone confirm panel (UX-DR48, UX-DR61), on Create Site and on My notifications: the
/// detected zone proposed with Confirm and Change; a zone the user confirmed or picked is named
/// instead and never replaced by detection. Without a zone to propose it says so and shows the
/// list at once.
public struct TimeZonePanelPresentation: Equatable, Sendable {
  /// The proposal; empty when there is none.
  public let detected: String
  public let chosen: String?
  public let changing: Bool
  /// A choice is on its way to the Server: the buttons wait.
  public let working: Bool

  public init(detected: String, chosen: String?, changing: Bool, working: Bool = false) {
    self.detected = detected
    self.chosen = chosen
    self.changing = changing
    self.working = working
  }

  /// The zone the panel names; empty when there is none to propose.
  public var zone: String { chosen ?? detected }

  public var isConfirmed: Bool { chosen != nil }

  /// "Is your time zone …?" until confirmed, then "Your time zone is ….".
  public var sentence: L10n { isConfirmed ? .timeZoneChosen : .timeZoneQuestion }

  /// The sentence with its zone, or "Your time zone could not be detected. …" without one.
  public var sentenceCopy: Copy {
    zone.isEmpty ? Copy(.timeZoneUnknown) : Copy(sentence, .text(zone))
  }

  /// Confirm shows only while a proposal waits for the user.
  public var showsConfirm: Bool { !isConfirmed && !changing && !zone.isEmpty }

  /// The searchable list shows after Change, and at once when there is nothing to propose.
  public var showsList: Bool { changing || zone.isEmpty }

  /// Zones whose ID contains every word of `query`, ignoring case and treating `_` and `/` as
  /// spaces; the OS order is kept.
  public static func filter(_ zones: [String], query: String) -> [String] {
    let words = query.lowercased().split(whereSeparator: { $0 == " " || $0 == "/" || $0 == "_" })
    guard !words.isEmpty else { return zones }
    return zones.filter { zone in
      let name = zone.lowercased().replacingOccurrences(of: "_", with: " ")
      return words.allSatisfy { name.contains($0) }
    }
  }
}

/// Create Site (UX-DR61): the Site name field, the time-zone panel and "Create Site".
public struct CreateSitePresentation: Equatable, Sendable {
  public let name: String
  public let nameError: NameErrorKind?
  public let working: Bool
  public let notice: SitesNoticeKind?
  /// Only from "New Site"; with no Membership, Create Site is the only surface.
  public let cancellable: Bool
  public let timeZone: TimeZonePanelPresentation

  public init(
    name: String, nameError: NameErrorKind?, working: Bool, notice: SitesNoticeKind?,
    cancellable: Bool, timeZone: TimeZonePanelPresentation
  ) {
    self.name = name
    self.nameError = nameError
    self.working = working
    self.notice = notice
    self.cancellable = cancellable
    self.timeZone = timeZone
  }

  /// The label names the result and shows the working label in place, never a spinner.
  public var buttonLabel: L10n { working ? .createSiteWorking : .createSiteAction }

  public var isButtonEnabled: Bool { !working }

  /// The helper shows until a reason replaces it.
  public var fieldHelper: L10n { nameError?.message ?? .createSiteNameHelper }
}

/// One row of the Site switcher (UX-DR23): a Site with its Role, or "New Site", which is last.
public struct SiteSwitcherRow: Equatable, Sendable, Identifiable {
  /// The Site ID; `nil` for "New Site".
  public let siteId: String?
  public let name: String?
  public let role: SiteRoleKind?
  public let isSelected: Bool

  public var id: String { siteId ?? "" }

  public static let newSite = SiteSwitcherRow(siteId: nil, name: nil, role: nil, isSelected: false)

  public init(siteId: String?, name: String?, role: SiteRoleKind?, isSelected: Bool) {
    self.siteId = siteId
    self.name = name
    self.role = role
    self.isSelected = isSelected
  }

  public var isNewSite: Bool { siteId == nil }

  /// The current Site is exposed as selected and drawn with a checkmark.
  public var traits: Set<ControlTrait> { isSelected ? [.button, .selected] : [.button] }
}

/// One Site menu item (UX-DR22).
public enum SiteMenuItem: String, CaseIterable, Sendable {
  case pause
  case resume
  case siteSettings

  public var label: L10n {
    switch self {
    case .pause: .siteMenuPause
    case .resume: .siteMenuResume
    case .siteSettings: .siteMenuSettings
    }
  }

  /// Site settings opens its own surface (Story 1.9, closes DW-25); Pause and Resume open the
  /// Pause sheet in a later story.
  public var opensSiteSettings: Bool { self == .siteSettings }
}

/// One first-run step (UX-DR54).
public enum FirstRunStepKind: String, CaseIterable, Sendable {
  case addHub
  case addNode
  case calibrate
  case setLowThreshold

  public var label: L10n {
    switch self {
    case .addHub: .gardenStepAddHub
    case .addNode: .gardenStepAddNode
    case .calibrate: .gardenStepCalibrate
    case .setLowThreshold: .gardenStepSetThreshold
    }
  }
}

/// How a step tile is drawn: next is solid primary, later is dashed, done has a checkmark.
public enum StepStateKind: String, CaseIterable, Sendable {
  case next
  case later
  case done

  public var label: L10n {
    switch self {
    case .next: .gardenStepNext
    case .later: .gardenStepLater
    case .done: .gardenStepDone
    }
  }
}

public struct FirstRunTilePresentation: Equatable, Sendable {
  public let step: FirstRunStepKind
  /// STEP 1 to 4.
  public let number: Int
  public let state: StepStateKind

  public init(step: FirstRunStepKind, number: Int, state: StepStateKind) {
    self.step = step
    self.number = number
    self.state = state
  }

  public var isSolid: Bool { state == .next }
  /// Later and done tiles share the 1 px dashed `border-strong` outline; done adds a checkmark.
  public var isDashed: Bool { state != .next }
  public var showsCheckmark: Bool { state == .done }
}

/// The Garden of the current Site (UX-DR62, UX-DR82): the Site name with the switcher and the
/// Site menu, the four step tiles and the Member notice. The header under the Site name and the
/// Lot tiles come from the Lots (`header(lots:)`); `headline` and `subline` are what shows
/// while there are none.
public struct GardenPresentation: Equatable, Sendable {
  public let siteName: String
  public let role: SiteRoleKind
  public let tiles: [FirstRunTilePresentation]
  /// Administrators and Owners: the next step's tile (Add a Hub) starts its flow (Story 3.6).
  public let tilesActionable: Bool
  public let switcherRows: [SiteSwitcherRow]
  public let menuItems: [SiteMenuItem]
  /// False in stale mode: every item then shows "Needs your Server".
  public let menuEnabled: Bool
  public let showsMemberNotice: Bool

  public init(
    siteName: String, role: SiteRoleKind, tiles: [FirstRunTilePresentation],
    tilesActionable: Bool, switcherRows: [SiteSwitcherRow], menuItems: [SiteMenuItem],
    menuEnabled: Bool, showsMemberNotice: Bool
  ) {
    self.siteName = siteName
    self.role = role
    self.tiles = tiles
    self.tilesActionable = tilesActionable
    self.switcherRows = switcherRows
    self.menuItems = menuItems
    self.menuEnabled = menuEnabled
    self.showsMemberNotice = showsMemberNotice
  }

  /// The headline is exposed as a heading; an empty Site never reads as fine.
  public let headline: L10n = .gardenNoReadings
  public let subline: L10n = .gardenNoReadingsDetail

  /// "Only Owners and Administrators can add Devices." for Members only.
  public var memberNotice: L10n? { showsMemberNotice ? .gardenMemberNotice : nil }

  /// Whether [tile] starts a flow: only Add a Hub as the next step, for Administrators and
  /// Owners (UX-DR66); the other steps' flows arrive in Epic 4 and later.
  public func startsFlow(_ tile: FirstRunTilePresentation) -> Bool {
    tilesActionable && tile.step == .addHub && tile.state == .next
  }
}

/// What stands at the top of the Site overview.
public enum GardenHeaderPresentation: Equatable, Sendable {
  /// The Site summary: the headline sentence and the counts.
  case summary(SiteSummaryPresentation)
  /// Stale mode: the stale header replaces the summary (UX-DR24).
  case stale(StaleHeaderPresentation)
  /// The Lots are read for the first time: "Loading ‹Site›" over skeleton tiles (UX-DR80).
  case loading(siteName: String)
}

/// The Site menu as it is drawn: in stale mode every item is disabled with "Needs your Server"
/// (UX-DR22, UX-DR79).
public struct SiteMenuPresentation: Equatable, Sendable {
  public let items: [SiteMenuItem]
  public let enabled: Bool

  public init(items: [SiteMenuItem], enabled: Bool) {
    self.items = items
    self.enabled = enabled
  }

  /// The line under every item while the menu is disabled.
  public var disabledReason: L10n? { enabled ? nil : .siteMenuNeedsServer }
}

extension GardenPresentation {
  /// The header for the Lots as the core has them: the stale header in stale mode, "Loading
  /// ‹Site›" on a cold start without kept Lots, else the summary. Until the Lots are known the
  /// summary is "No Readings yet": an unread Site never reads as fine.
  public func header(lots: LotsPresentation) -> GardenHeaderPresentation {
    if lots.loadingSiteName != nil { return .loading(siteName: siteName) }
    guard let overview = lots.overview else { return .summary(.noReadings) }
    if overview.stale, let fetchedAt = overview.fetchedAt {
      return .stale(
        StaleHeaderPresentation(siteName: siteName, fetchedAt: fetchedAt, age: overview.staleAge))
    }
    return .summary(overview.summary)
  }

  /// The Site menu: the one the core built for the Lots' Site while they are shown (disabled in
  /// stale mode), else the Sites one.
  public func menu(lots: LotsPresentation) -> SiteMenuPresentation {
    if let overview = lots.overview {
      return SiteMenuPresentation(items: overview.menuItems, enabled: overview.menuEnabled)
    }
    return SiteMenuPresentation(items: menuItems, enabled: menuEnabled)
  }
}

/// Which Sites surface shows while signed in.
public enum SitesSurface: Equatable, Sendable {
  /// Signed out, or the Sites are being read: only the background.
  case waiting
  /// The Sites could not be read.
  case failed(SitesNoticeKind)
  /// No Membership: Create Site replaces the tab shell.
  case createSite(CreateSitePresentation)
  /// The tab shell on the current Site, with Create Site over it while `creating`.
  case garden(GardenPresentation, creating: CreateSitePresentation?)
}

/// The Swift mirror of the core's `SitesState`, built from the flattened `SitesSnapshot`.
public struct SitesPresentation: Equatable, Sendable {
  public let surface: SitesSurface

  public init(surface: SitesSurface) {
    self.surface = surface
  }

  public static let waiting = SitesPresentation(surface: .waiting)

  /// Mirrors the flat snapshot field for field; unknown names fall back to the safest reading.
  public init(
    surface: String, notice: String?,
    siteIds: [String], siteNames: [String], siteRoles: [String],
    currentId: String?, currentName: String?, currentRole: String?,
    formShown: Bool, formName: String, formNameError: String?, formWorking: Bool,
    formNotice: String?, formCancellable: Bool,
    timeZoneDetected: String, timeZoneChosen: String?, timeZoneChanging: Bool,
    steps: [String], stepStates: [String], stepsActionable: Bool, memberNotice: Bool,
    menuItems: [String], menuEnabled: Bool
  ) {
    let form =
      formShown
      ? CreateSitePresentation(
        name: formName, nameError: formNameError.flatMap(NameErrorKind.init(rawValue:)),
        working: formWorking, notice: formNotice.flatMap(SitesNoticeKind.init(rawValue:)),
        cancellable: formCancellable,
        timeZone: TimeZonePanelPresentation(
          detected: timeZoneDetected, chosen: timeZoneChosen, changing: timeZoneChanging))
      : nil
    switch surface {
    case "failed":
      self.surface = .failed(notice.flatMap(SitesNoticeKind.init(rawValue:)) ?? .unexpected)
    case "needsSite":
      self.surface = form.map { .createSite($0) } ?? .waiting
    case "ready":
      guard let currentId, let currentName else {
        self.surface = .waiting
        return
      }
      let count = min(siteIds.count, siteNames.count, siteRoles.count)
      // The Server's order, never re-sorted (AD-14); "New Site" is always last.
      let rows =
        (0..<count).map {
          SiteSwitcherRow(
            siteId: siteIds[$0], name: siteNames[$0],
            role: SiteRoleKind(rawValue: siteRoles[$0]) ?? .member,
            isSelected: siteIds[$0] == currentId)
        } + [.newSite]
      let tiles = zip(steps, stepStates).enumerated().compactMap { index, pair in
        FirstRunStepKind(rawValue: pair.0).map { step in
          FirstRunTilePresentation(
            step: step, number: index + 1, state: StepStateKind(rawValue: pair.1) ?? .later)
        }
      }
      let garden = GardenPresentation(
        siteName: currentName,
        role: currentRole.flatMap(SiteRoleKind.init(rawValue:)) ?? .member,
        tiles: tiles, tilesActionable: stepsActionable, switcherRows: rows,
        menuItems: menuItems.compactMap(SiteMenuItem.init(rawValue:)), menuEnabled: menuEnabled,
        showsMemberNotice: memberNotice)
      self.surface = .garden(garden, creating: form)
    default:
      self.surface = .waiting
    }
  }

  /// The tab shell shows only once a Site exists.
  public var showsTabs: Bool {
    if case .garden = surface { return true }
    return false
  }

  /// Create Site, full screen or over the shell.
  public var createSite: CreateSitePresentation? {
    switch surface {
    case .createSite(let form): form
    case .garden(_, let creating): creating
    default: nil
    }
  }
}

/// The Sites half of the shared Kotlin core, as the SwiftUI shell sees it. The app target
/// adapts `IosSites` of `ColdframeCore`; no token or URL crosses this boundary.
@MainActor
public protocol SitesService: AnyObject {
  /// Calls `onChange` with the current presentation and every change.
  func observe(_ onChange: @escaping @MainActor (SitesPresentation) -> Void)
  /// Every IANA zone ID the OS knows, for Change.
  func availableTimeZones() -> [String]
  /// Try again after a failed load.
  func load()
  func select(siteId: String)
  func newSite()
  func cancelNewSite()
  func setName(_ name: String)
  func confirmTimeZone()
  func changeTimeZone()
  func pickTimeZone(_ zoneId: String)
  func submit()
}
