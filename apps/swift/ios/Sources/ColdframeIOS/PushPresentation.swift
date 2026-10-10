import Foundation

/// The notification permission as the core holds it: `unknown` until the OS was asked or read.
public enum PushPermissionKind: String, CaseIterable, Sendable {
  case unknown
  case granted
  case denied
}

/// What the OS says about the notification permission, in the words the core takes. The app
/// target maps `UNAuthorizationStatus` to it: authorized, provisional and ephemeral are granted.
public enum OsNotificationPermission: String, CaseIterable, Sendable {
  case notDetermined
  case granted
  case denied

  /// The answer of the OS prompt.
  public init(promptGranted: Bool) {
    self = promptGranted ? .granted : .denied
  }
}

/// The APNs environment of this build's device tokens, as the contract names it.
public enum PushEnvironment: String, CaseIterable, Sendable {
  case production
  case sandbox

  /// From the value of the `aps-environment` entitlement (`development` or `production`), which
  /// the Info.plist repeats: a development build gets sandbox tokens.
  public init(entitlement: String?) {
    self = entitlement == "development" ? .sandbox : .production
  }
}

/// What the OS prompt asks for (UX-DR121, UX-DR122): alerts and sound. There is no third option:
/// Coldframe never counts on the app icon.
public enum PushAuthorizationOption: String, CaseIterable, Sendable {
  case alert
  case sound
}

/// How a notification that arrives while the app is in front is shown (UX-DR121).
public enum PushDisplayOption: String, CaseIterable, Sendable {
  case banner
  case list
  case sound
}

/// The options the app target hands to `UNUserNotificationCenter`.
public enum PushDisplay {
  public static let authorization: Set<PushAuthorizationOption> = [.alert, .sound]
  public static let foreground: Set<PushDisplayOption> = [.banner, .list, .sound]
}

/// What a tapped notification's `userInfo` and the device token give the core.
public enum PushPayload {
  /// The object next to `aps` that carries the routing keys (`packages/asyncapi`).
  public static let routingKey = "coldframe"

  /// The routing keys of a Coldframe notification (`kind`, `siteId`, `lotId`, `alertId`,
  /// `collapseId`), every value a string; nil for a notification that carries none. The core
  /// reads them and decides where the tap leads.
  public static func routing(userInfo: [AnyHashable: Any]) -> [String: String]? {
    guard let object = userInfo[routingKey] as? [AnyHashable: Any] else { return nil }
    var routing: [String: String] = [:]
    for (key, value) in object {
      if let key = key as? String, let value = value as? String { routing[key] = value }
    }
    return routing.isEmpty ? nil : routing
  }

  /// The APNs device token as lowercase hex, two digits a byte.
  public static func hex(_ deviceToken: Data) -> String {
    deviceToken.map { byte in
      let digits = String(byte, radix: 16)
      return digits.count == 1 ? "0" + digits : digits
    }.joined()
  }
}

/// What the one action of a push notice does.
public enum PushNoticeActionKind: Sendable {
  /// The OS prompt.
  case ask
  /// This app's notification settings in the system Settings.
  case openSettings
}

/// The two Inline notices about notifications. Neither is dismissable.
public enum PushNoticeKind: CaseIterable, Sendable {
  /// The one line of why before the OS prompt (UX-DR122).
  case why
  /// Notifications are off for the app on this phone (UX-DR88).
  case off

  public var message: L10n {
    switch self {
    case .why: .pushWhy
    case .off: .pushOff
    }
  }

  public var actionLabel: L10n {
    switch self {
    case .why: .pushWhyContinue
    case .off: .pushOpenSettings
    }
  }

  public var action: PushNoticeActionKind {
    switch self {
    case .why: .ask
    case .off: .openSettings
    }
  }
}

/// Where a tapped notification leads (UX-DR120). The core has already switched to `siteId`.
public enum PushRoutePresentation: Equatable, Sendable {
  /// An Alert or a Reminder: Lot detail of that Lot.
  case lotDetail(siteId: String, lotId: String, lotName: String)
  /// A summary, or a Lot the core does not know: the Site overview.
  case overview(siteId: String)

  /// Both are surfaces of the Garden tab.
  public var tab: AppTab { .garden }

  /// What the Garden tab's stack shows for this route: the Lot over the overview, or the
  /// overview alone, with whatever was open on it closed.
  public var gardenPath: GardenPath {
    switch self {
    case .lotDetail(_, let lotId, let lotName):
      GardenPath(lot: OpenedLot(id: lotId, name: lotName))
    case .overview: .root
    }
  }
}

/// Push on this phone, built from the core's flat `PushSnapshot`. The core decides what shows
/// and where a tap leads; the shell holds the OS prompt, the system settings and the stack.
public struct PushPresentation: Equatable, Sendable {
  public let permission: PushPermissionKind
  /// The notice of UX-DR88 shows in My notifications and on the overview.
  public let noticeVisible: Bool
  /// The overview shows the one line of why, whose action leads to the OS prompt.
  public let promptDue: Bool
  /// The tap waiting to be shown; the shell shows it and says so with `routeHandled`.
  public let route: PushRoutePresentation?

  /// Before the core has said anything: no notice, no prompt, no route.
  public static let idle = PushPresentation(permission: .unknown)

  public init(
    permission: PushPermissionKind, noticeVisible: Bool = false, promptDue: Bool = false,
    route: PushRoutePresentation? = nil
  ) {
    self.permission = permission
    self.noticeVisible = noticeVisible
    self.promptDue = promptDue
    self.route = route
  }

  /// From the snapshot's fields. A target this client does not know, and a Lot without an ID,
  /// end on the overview of the Site.
  public init(
    permission: String, noticeVisible: Bool, promptDue: Bool, routeTarget: String?,
    routeSiteId: String?, routeLotId: String?, routeLotName: String?
  ) {
    var route: PushRoutePresentation?
    if let routeTarget {
      let siteId = routeSiteId ?? ""
      if routeTarget == "lotDetail", let routeLotId, !routeLotId.isEmpty {
        route = .lotDetail(siteId: siteId, lotId: routeLotId, lotName: routeLotName ?? "")
      } else {
        route = .overview(siteId: siteId)
      }
    }
    self.init(
      permission: PushPermissionKind(rawValue: permission) ?? .unknown,
      noticeVisible: noticeVisible, promptDue: promptDue, route: route)
  }

  /// Above the tiles of the overview: on the first landing the one line of why (UX-DR122),
  /// afterwards the notifications-off notice while the permission is denied (UX-DR88).
  public var overviewNotice: PushNoticeKind? {
    if promptDue { return .why }
    return noticeVisible ? .off : nil
  }

  /// In My notifications: the notifications-off notice, persistent while the permission is
  /// denied. My notifications never asks.
  public var settingsNotice: PushNoticeKind? {
    noticeVisible ? .off : nil
  }
}

/// Push of the shared Kotlin core, as the SwiftUI shell sees it. The app target adapts `IosPush`
/// of `ColdframeCore` and holds the OS calls; the core registers the device with the Server and
/// removes it at sign-out. Nothing here calls a Server.
@MainActor
public protocol PushService: AnyObject {
  /// Calls `onChange` with the current presentation and every change.
  func observe(_ onChange: @escaping @MainActor (PushPresentation) -> Void)
  /// Reads the OS permission again and tells the core: at start and on every foreground.
  func refresh()
  /// Continue on the why-line: the OS prompt, once per phone.
  func ask()
  /// Open Settings on the notice: this app's notification settings.
  func openSettings()
  /// The shell has shown the route of a tapped notification.
  func routeHandled()
}
