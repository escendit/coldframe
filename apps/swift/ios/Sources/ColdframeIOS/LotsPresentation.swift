import ColdframeDesignTokens
import Foundation

/// A Lot status as the Server computed it (AD-14); the client never computes or re-sorts it.
public enum LotStatusKind: String, CaseIterable, Sendable {
  case needsWater
  case needsCalibration
  case unknown
  case ok
  case paused
  case noNode
}

/// One Lot tile on Garden (UX-DR18, UX-DR20). Story 1.9 draws the *no Node* variant in full:
/// transparent, 1 pt dotted `status-no-node-border`, `add`, a large "+" and "add a Node". Every
/// other status shows only the Lot name until its variant arrives (Epic 5). For Administrators
/// and Owners a *no Node* tile starts Add a Node with its Lot (Story 4.3); no other tile is
/// tappable yet: Lot detail is a later epic.
public struct LotTilePresentation: Equatable, Sendable, Identifiable {
  public let id: String
  public let name: String
  public let status: LotStatusKind
  /// Whether the caller may add a Node to the Site, as the core decided.
  public let canAddNode: Bool

  public init(id: String, name: String, status: LotStatusKind, canAddNode: Bool = false) {
    self.id = id
    self.name = name
    self.status = status
    self.canAddNode = canAddNode
  }

  public var isNoNode: Bool { status == .noNode }

  /// The 1 pt dotted outline of the *no Node* tile.
  public var isDotted: Bool { isNoNode }

  /// The Carbon icon beside the status label, only where the variant is specified.
  public var icon: CarbonIcon? { isNoNode ? .add : nil }

  /// The status label under the name ("no Node", uppercase by style).
  public var statusLabel: L10n? { isNoNode ? .lotTileNoNode : nil }

  /// The big tile value: "+".
  public var value: L10n? { isNoNode ? .lotTileNoNodeValue : nil }

  /// The foot line: "add a Node".
  public var foot: L10n? { isNoNode ? .lotTileAddNode : nil }

  /// The spoken label of the one merged accessibility element: "{lot}, no Node, add a Node",
  /// or the name alone for a status whose variant is not drawn yet.
  public var accessibilityFormat: L10n? { isNoNode ? .lotTileDescriptionNoNode : nil }

  /// A *no Node* tile is a button for Administrators and Owners, with the same spoken label;
  /// for a Member it is not interactive.
  public var isTappable: Bool { isNoNode && canAddNode }

  public var traits: Set<ControlTrait> { isTappable ? [.button] : [] }

  /// Two columns, one at accessibility text sizes (DESIGN.md Layout).
  public static func columns(accessibilitySize: Bool) -> Int { accessibilitySize ? 1 : 2 }
}

/// The result of a Site settings action that did not change anything, named as the core names it.
public enum LotsActionNoticeKind: String, CaseIterable, Sendable {
  case forbidden
  case lotClaimed
  case lotNotFound
  case renameSiteUnavailable
  case keyReused
  case unreachable
  case certificate
  case unexpected

  public var message: L10n {
    switch self {
    case .forbidden: .siteSettingsForbidden
    case .lotClaimed: .siteSettingsLotClaimed
    case .lotNotFound: .siteSettingsLotNotFound
    case .renameSiteUnavailable: .siteSettingsRenameSiteUnavailable
    case .keyReused: .siteSettingsKeyReused
    case .unreachable: .noticeUnreachable
    case .certificate: .noticeCertificate
    case .unexpected: .siteSettingsUnexpected
    }
  }

  /// Forbidden names the Site and Lot claimed names the Lot (`%@`).
  public var takesSubject: Bool { self == .forbidden || self == .lotClaimed }

  public var announcement: Announcement { .assertive }
}

/// A name the client refused before sending it.
extension NameErrorKind {
  /// The reason under the Lot name field; too long shares the Site copy.
  public var lotMessage: L10n {
    switch self {
    case .blank: .siteSettingsLotNameBlank
    case .tooLong: .createSiteNameTooLong
    }
  }
}

/// One Lot row of Site settings.
public struct LotRowPresentation: Equatable, Sendable, Identifiable {
  public let id: String
  public let name: String

  public init(id: String, name: String) {
    self.id = id
    self.name = name
  }
}

/// Rename Lot, shown in an alert with a field while the core has a Lot to rename.
public struct LotRenamePresentation: Equatable, Sendable {
  public let lotId: String
  public let draft: String
  public let error: NameErrorKind?
  public let working: Bool

  public init(lotId: String, draft: String, error: NameErrorKind?, working: Bool) {
    self.lotId = lotId
    self.draft = draft
    self.error = error
    self.working = working
  }

  public var buttonLabel: L10n { working ? .siteSettingsRenamingLot : .siteSettingsRenameLot }
  public var isButtonEnabled: Bool { !working }
}

/// The destructive confirmation naming the Lot (UX-DR74): "Remove Lot {lot}?".
public struct LotRemovalPresentation: Equatable, Sendable {
  public let lotId: String
  public let lotName: String
  public let working: Bool

  public init(lotId: String, lotName: String, working: Bool) {
    self.lotId = lotId
    self.lotName = lotName
    self.working = working
  }

  public let title: L10n = .siteSettingsRemoveLotQuestion
  public let message: L10n = .siteSettingsRemoveLotDetail
  public var confirm: L10n { working ? .siteSettingsRemovingLot : .siteSettingsRemoveLot }
  public let cancel: L10n = .siteSettingsCancel
}

/// Site settings as one surface (UX-DR74): the Site name, renamed only by an Owner, and the
/// Lots, created, renamed and removed by Owners and Administrators. A Member sees both read-only
/// with one notice; controls a Role cannot use are hidden, not disabled (UX-DR84).
public struct SiteSettingsPresentation: Equatable, Sendable {
  public let siteName: String
  public let role: SiteRoleKind
  public let canRenameSite: Bool
  public let canEditLots: Bool
  public let showsReadOnlyNotice: Bool
  public let siteNameDraft: String
  public let siteNameError: NameErrorKind?
  public let siteRenameWorking: Bool
  public let lots: [LotRowPresentation]
  public let newLotName: String
  public let newLotNameError: NameErrorKind?
  public let createWorking: Bool
  public let renaming: LotRenamePresentation?
  public let removing: LotRemovalPresentation?
  public let notice: LotsActionNoticeKind?
  public let noticeSubject: String?

  public init(
    siteName: String, role: SiteRoleKind, canRenameSite: Bool, canEditLots: Bool,
    showsReadOnlyNotice: Bool, siteNameDraft: String, siteNameError: NameErrorKind?,
    siteRenameWorking: Bool, lots: [LotRowPresentation], newLotName: String,
    newLotNameError: NameErrorKind?, createWorking: Bool, renaming: LotRenamePresentation?,
    removing: LotRemovalPresentation?, notice: LotsActionNoticeKind?, noticeSubject: String?
  ) {
    self.siteName = siteName
    self.role = role
    self.canRenameSite = canRenameSite
    self.canEditLots = canEditLots
    self.showsReadOnlyNotice = showsReadOnlyNotice
    self.siteNameDraft = siteNameDraft
    self.siteNameError = siteNameError
    self.siteRenameWorking = siteRenameWorking
    self.lots = lots
    self.newLotName = newLotName
    self.newLotNameError = newLotNameError
    self.createWorking = createWorking
    self.renaming = renaming
    self.removing = removing
    self.notice = notice
    self.noticeSubject = noticeSubject
  }

  /// "Rename Site", "Renaming Site…" in place while working.
  public var renameSiteLabel: L10n {
    siteRenameWorking ? .siteSettingsRenamingSite : .siteSettingsRenameSite
  }

  public var siteNameHelper: L10n { siteNameError?.message ?? .siteSettingsSiteNameHelper }

  /// "Create Lot", "Creating Lot…" in place while working.
  public var createLotLabel: L10n {
    createWorking ? .siteSettingsCreatingLot : .siteSettingsCreateLot
  }

  public var lotNameHelper: L10n { newLotNameError?.lotMessage ?? .siteSettingsLotNameHelper }

  /// "Only Owners and Administrators can change Lots." for Members only.
  public var readOnlyNotice: L10n? { showsReadOnlyNotice ? .siteSettingsReadOnly : nil }

  public var emptyLots: L10n? { lots.isEmpty ? .siteSettingsNoLots : nil }
}

/// Which Lots surface shows.
public enum LotsSurface: Equatable, Sendable {
  /// Signed out, no current Site, or the Lots are being read.
  case waiting
  /// The Lots could not be read: the Sites load notices (Unreachable, Certificate, …).
  case failed(SitesNoticeKind)
  case ready(SiteSettingsPresentation, tiles: [LotTilePresentation])
}

/// The Swift mirror of the core's `LotsState`, built from the flattened `LotsSnapshot`.
public struct LotsPresentation: Equatable, Sendable {
  public let surface: LotsSurface

  public init(surface: LotsSurface) {
    self.surface = surface
  }

  public static let waiting = LotsPresentation(surface: .waiting)

  /// Mirrors the flat snapshot field for field; unknown names fall back to the safest reading.
  public init(
    surface: String, notice: String?, siteId: String?, siteName: String?, role: String?,
    canRenameSite: Bool, canEditLots: Bool, readOnlyNotice: Bool,
    siteNameDraft: String, siteNameError: String?, siteRenameWorking: Bool,
    lotIds: [String], lotNames: [String], lotStatuses: [String],
    newLotName: String, newLotNameError: String?, createWorking: Bool,
    renamingLotId: String?, renameDraft: String, renameError: String?, renameWorking: Bool,
    removingLotId: String?, removingLotName: String?, removeWorking: Bool,
    actionNotice: String?, actionNoticeSubject: String?, canAddNode: Bool = false
  ) {
    switch surface {
    case "failed":
      self.surface = .failed(notice.flatMap(SitesNoticeKind.init(rawValue:)) ?? .unexpected)
    case "ready":
      guard siteId != nil, let siteName else {
        self.surface = .waiting
        return
      }
      let count = min(lotIds.count, lotNames.count, lotStatuses.count)
      // The Server's order, never re-sorted (UX-DR20). An unknown status is drawn as a name
      // only, never as *no Node*.
      let tiles = (0..<count).map {
        LotTilePresentation(
          id: lotIds[$0], name: lotNames[$0],
          status: LotStatusKind(rawValue: lotStatuses[$0]) ?? .unknown, canAddNode: canAddNode)
      }
      let role = role.flatMap(SiteRoleKind.init(rawValue:)) ?? .member
      let notice = actionNotice.map { LotsActionNoticeKind(rawValue: $0) ?? .unexpected }
      let renaming = renamingLotId.map { lotId in
        LotRenamePresentation(
          lotId: lotId, draft: renameDraft,
          error: renameError.flatMap(NameErrorKind.init(rawValue:)), working: renameWorking)
      }
      let removing = removingLotId.map { lotId in
        LotRemovalPresentation(
          lotId: lotId,
          lotName: removingLotName ?? tiles.first { $0.id == lotId }?.name ?? "",
          working: removeWorking)
      }
      let settings = SiteSettingsPresentation(
        siteName: siteName, role: role,
        canRenameSite: canRenameSite, canEditLots: canEditLots,
        showsReadOnlyNotice: readOnlyNotice,
        siteNameDraft: siteNameDraft,
        siteNameError: siteNameError.flatMap(NameErrorKind.init(rawValue:)),
        siteRenameWorking: siteRenameWorking,
        lots: tiles.map { LotRowPresentation(id: $0.id, name: $0.name) },
        newLotName: newLotName,
        newLotNameError: newLotNameError.flatMap(NameErrorKind.init(rawValue:)),
        createWorking: createWorking,
        renaming: canEditLots ? renaming : nil,
        removing: canEditLots ? removing : nil,
        notice: notice,
        noticeSubject: notice?.takesSubject == true ? actionNoticeSubject : nil)
      self.surface = .ready(settings, tiles: tiles)
    default:
      self.surface = .waiting
    }
  }

  /// The Lot tiles for Garden, in the Server's order; empty unless ready.
  public var tiles: [LotTilePresentation] {
    if case .ready(_, let tiles) = surface { return tiles }
    return []
  }

  /// Site settings; nil unless ready.
  public var siteSettings: SiteSettingsPresentation? {
    if case .ready(let settings, _) = surface { return settings }
    return nil
  }

  /// The load failure that replaces the Lot grid on Garden.
  public var failure: SitesNoticeKind? {
    if case .failed(let notice) = surface { return notice }
    return nil
  }
}

/// The Lots half of the shared Kotlin core, as the SwiftUI shell sees it. The app target adapts
/// `IosLots` of `ColdframeCore`; no token or URL crosses this boundary. The core follows the
/// current Site of the Sites half and reloads after every change.
@MainActor
public protocol LotsService: AnyObject {
  /// Calls `onChange` with the current presentation and every change.
  func observe(_ onChange: @escaping @MainActor (LotsPresentation) -> Void)
  /// Try again after a failed load.
  func load()
  func setSiteName(_ name: String)
  func renameSite()
  func setNewLotName(_ name: String)
  func createLot()
  func startRename(lotId: String)
  func setRename(_ name: String)
  func rename()
  func cancelRename()
  func askRemove(lotId: String)
  func confirmRemove()
  func cancelRemove()
}
