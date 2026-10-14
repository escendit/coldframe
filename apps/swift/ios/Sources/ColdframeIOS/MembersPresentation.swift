import Foundation

/// Why the one line under the email field shows, as the core names it (UX-DR52).
public enum InviteEmailErrorKind: String, CaseIterable, Sendable {
  case blank
  case invalid

  public var message: L10n {
    switch self {
    case .blank: .membersEmailBlank
    case .invalid: .membersEmailInvalid
    }
  }
}

/// The Inline notice on Members, as the core names it.
public enum MembersNoticeKind: String, CaseIterable, Sendable {
  case sent
  case alreadyInvited
  case notSent
  case membersUnavailable
  case forbidden
  case siteGone
  case unreachable
  case certificate
  case unexpected

  public var message: L10n {
    switch self {
    case .sent: .membersSent
    case .alreadyInvited: .membersAlreadyInvited
    case .notSent: .membersNotSent
    case .membersUnavailable: .membersUnavailable
    case .forbidden: .membersForbidden
    case .siteGone: .membersSiteGone
    case .unreachable: .membersUnreachable
    case .certificate: .noticeCertificate
    case .unexpected: .membersUnexpected
    }
  }

  /// "Invitation sent to ‹email›…" names the email; the 403 and 404 notices name the Site.
  var argument: Argument? {
    switch self {
    case .sent: .email
    case .forbidden, .siteGone: .site
    default: nil
    }
  }

  enum Argument { case email, site }

  /// The sent notice is said politely; anything that did not happen at once.
  public var announcement: Announcement { self == .sent ? .polite : .assertive }
}

extension SiteRoleKind {
  /// The one line under the Role choice of the Invite form (UX-DR52).
  public var inviteDescription: L10n {
    switch self {
    case .member: .membersRoleMemberDescription
    case .administrator: .membersRoleAdministratorDescription
    case .owner: .membersRoleOwnerDescription
    }
  }
}

/// One Member tile (UX-DR51): the initials square, the name and the Role line, upper case by
/// style. The signed-in User's own square is `primary` with `ink-on-bright`; the others are
/// `layer-01` with `text-primary`. One accessibility element. Display-only in Story 9.1.
public struct MemberTilePresentation: Equatable, Sendable, Identifiable {
  public let id: String
  public let name: String
  public let email: String
  public let role: SiteRoleKind
  public let initials: String
  public let isYou: Bool

  public init(
    id: String, name: String, email: String, role: SiteRoleKind, initials: String, isYou: Bool
  ) {
    self.id = id
    self.name = name
    self.email = email
    self.role = role
    self.initials = initials
    self.isYou = isYou
  }

  public var roleLabel: L10n { role.label }

  /// "Simon Novak, Owner, you" on the User's own tile, "Ana Costa, Member" on the others.
  public func spoken(_ context: CopyContext) -> String {
    context.text(
      isYou ? .membersTileYou : .membersTile, .text(name), .text(context.text(role.label)))
  }
}

/// What Members shows.
public enum MembersSurface: Equatable, Sendable {
  /// No current Site, or a Role below Owner: the surface and its Settings row do not exist.
  case hidden
  case loading
  case failed
  case ready
}

/// Members as the SwiftUI shell sees it (Story 9.1, UX-DR73), built from the core's flat
/// `MembersSnapshot`: one tile per Membership in the Server's order and the Invite form (an email
/// field, the Member / Administrator / Owner choice with its one-line description, "Send invite").
/// There is no pending-invitation list: an invitee appears once they accepted. The shell never
/// checks an email or decides what a refused invitation leaves (AD-14).
public struct MembersPresentation: Equatable, Sendable {
  public let surface: MembersSurface
  public let siteName: String
  public let members: [MemberTilePresentation]
  public let email: String
  /// The selected segment; Member at first.
  public let role: SiteRoleKind
  /// The segments in order.
  public let roles: [SiteRoleKind]
  public let emailError: InviteEmailErrorKind?
  public let working: Bool
  public let notice: MembersNoticeKind?
  public let noticeTryAgain: Bool
  /// The email the sent notice names.
  public let sentTo: String

  public static let hidden = MembersPresentation(surface: .hidden)

  public init(
    surface: MembersSurface, siteName: String = "", members: [MemberTilePresentation] = [],
    email: String = "", role: SiteRoleKind = .member,
    roles: [SiteRoleKind] = [.member, .administrator, .owner],
    emailError: InviteEmailErrorKind? = nil, working: Bool = false,
    notice: MembersNoticeKind? = nil, noticeTryAgain: Bool = false, sentTo: String = ""
  ) {
    self.surface = surface
    self.siteName = siteName
    self.members = members
    self.email = email
    self.role = role
    self.roles = roles
    self.emailError = emailError
    self.working = working
    self.notice = notice
    self.noticeTryAgain = noticeTryAgain
    self.sentTo = sentTo
  }

  /// Mirrors the flat snapshot field for field. An unknown surface reads as hidden, an unknown
  /// notice as unexpected, an unknown Role as Member (it grants the least).
  public init(
    surface: String, siteName: String, memberIds: [String], memberNames: [String],
    memberEmails: [String], memberRoles: [String], memberInitials: [String],
    memberIsYou: [Bool], email: String, role: String, roles: [String], emailError: String?,
    working: Bool, notice: String?, noticeTryAgain: Bool, sentTo: String
  ) {
    let kind: MembersSurface =
      switch surface {
      case "loading": .loading
      case "failed": .failed
      case "ready": .ready
      default: .hidden
      }
    func roleOf(_ value: String) -> SiteRoleKind { SiteRoleKind(rawValue: value) ?? .member }
    let tiles = memberIds.indices.map { index in
      MemberTilePresentation(
        id: memberIds[index], name: memberNames[safe: index] ?? "",
        email: memberEmails[safe: index] ?? "",
        role: roleOf(memberRoles[safe: index] ?? ""),
        initials: memberInitials[safe: index] ?? "",
        isYou: memberIsYou[safe: index] ?? false)
    }
    self.init(
      surface: kind, siteName: siteName, members: kind == .ready ? tiles : [], email: email,
      role: roleOf(role), roles: roles.compactMap(SiteRoleKind.init(rawValue:)),
      emailError: emailError.map { InviteEmailErrorKind(rawValue: $0) ?? .invalid },
      working: working, notice: notice.map { MembersNoticeKind(rawValue: $0) ?? .unexpected },
      noticeTryAgain: noticeTryAgain, sentTo: sentTo)
  }

  /// Members exists for an Owner only; for anyone else the Settings row is not drawn.
  public var canManageMembers: Bool { surface != .hidden }

  /// The Role choice: one segment per Role, the selected one with the selected trait and a check.
  public var roleSegments: [SegmentPresentation<SiteRoleKind>] {
    roles.map { SegmentPresentation(value: $0, label: $0.label, isSelected: $0 == role) }
  }

  /// "Member sees everything and gets the same Alerts, but can't change settings." and the others.
  public var roleDescription: L10n { role.inviteDescription }

  /// "Send invite", or "Sending invite…" in place while it is sent.
  public var buttonLabel: L10n { working ? .membersSendingInvite : .membersSendInvite }

  public var isButtonEnabled: Bool { !working }

  /// The line under the email field: its reason, else the helper.
  public var emailLine: L10n { emailError?.message ?? .membersEmailHelper }

  /// The notice as it is drawn, with the email or the Site it names; nil without one.
  public var noticeCopy: Copy? {
    guard let notice else { return nil }
    switch notice.argument {
    case .email: return Copy(notice.message, .text(sentTo))
    case .site: return Copy(notice.message, .text(siteName))
    case nil: return Copy(notice.message)
    }
  }
}

extension Array {
  fileprivate subscript(safe index: Int) -> Element? {
    indices.contains(index) ? self[index] : nil
  }
}

/// Members of the shared Kotlin core, as the SwiftUI shell sees it. The app target adapts
/// `IosMembers` of `ColdframeCore`; the Server calls and every rule stay in the core.
@MainActor
public protocol MembersService: AnyObject {
  /// Calls `onChange` with the current presentation and every change.
  func observe(_ onChange: @escaping @MainActor (MembersPresentation) -> Void)
  /// Reads the Memberships: every entry of Members.
  func load()
  func setEmail(_ email: String)
  func setRole(_ role: SiteRoleKind)
  /// "Send invite".
  func sendInvite()
  /// Try again, where the notice offers it.
  func retry()
}
