#if canImport(SwiftUI)
  import Foundation
  import SwiftUI

  /// What the push notices and the tap route can ask of the app's OS side. Built over a
  /// `PushService`; `.none` does nothing, for previews and render tests. The core decides what
  /// shows (`PushPresentation`); the app target holds the permission prompt and the system
  /// settings.
  @MainActor
  public struct PushActions {
    /// Continue on the why-line: the OS prompt.
    public var ask: () -> Void
    /// Open Settings on the notice: this app's notification settings.
    public var openSettings: () -> Void
    /// The shell has shown the route of a tapped notification.
    public var routeHandled: () -> Void
    /// Reads the OS permission again: every time the app comes to the front.
    public var refresh: () -> Void

    public init(service: PushService) {
      ask = { service.ask() }
      openSettings = { service.openSettings() }
      routeHandled = { service.routeHandled() }
      refresh = { service.refresh() }
    }

    private init() {
      ask = {}
      openSettings = {}
      routeHandled = {}
      refresh = {}
    }

    public static let none = PushActions()

    /// What the one action of `notice` does.
    public func perform(_ action: PushNoticeActionKind) -> () -> Void {
      switch action {
      case .ask: ask
      case .openSettings: openSettings
      }
    }
  }

  /// One of the two notices about notifications, as an Inline notice with its one action: the
  /// line of why with Continue, which leads to the OS prompt (UX-DR122), or the
  /// notifications-off notice with Open Settings (UX-DR88). It stays while the core says so and
  /// cannot be closed.
  public struct PushNotice: View {
    let notice: PushNoticeKind
    let actions: PushActions

    public init(notice: PushNoticeKind, actions: PushActions = .none) {
      self.notice = notice
      self.actions = actions
    }

    public var body: some View {
      InlineNotice(
        message: notice.message,
        action: (label: notice.actionLabel, perform: actions.perform(notice.action)))
    }
  }
#endif
