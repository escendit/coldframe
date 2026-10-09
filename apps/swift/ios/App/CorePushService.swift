import ColdframeCore
import ColdframeIOS
import UIKit
@preconcurrency import UserNotifications

/// Adapts `IosPush` of the shared Kotlin core to `PushService` and holds the OS calls of push
/// (Story 6.5): the notification permission and its one prompt, the registration with APNs and
/// the system settings. The snapshot is flat strings and flags. The core sends the device token
/// to the Server after sign-in and removes the registration at sign-out; nothing here calls a
/// Server. An app built without the push entitlement gets no device token and registers nothing.
@MainActor
final class CorePushService: PushService {
  private let core: IosPush
  private var watch: Watch?
  /// The APNs environment comes from Info.plist, which repeats the `aps-environment` of
  /// `Config/Coldframe.entitlements` (`COLDFRAME_APS_ENVIRONMENT` in `Config/Coldframe.xcconfig`).
  init(core: IosPush, bundle: Bundle = .main) {
    self.core = core
    let environment = PushEnvironment(
      entitlement: bundle.object(forInfoDictionaryKey: "ColdframeApsEnvironment") as? String)
    // What the app delegate received before this adapter existed (a cold start from a tapped
    // notification) is handed over now.
    PushInbox.shared.attach(
      onOpened: { core.opened(coldframe: $0) },
      onDeviceToken: { core.deviceToken(tokenHex: $0, environment: environment.rawValue) })
  }

  func observe(_ onChange: @escaping @MainActor (PushPresentation) -> Void) {
    watch?.close()
    watch = core.watch { snapshot in
      MainActor.assumeIsolated {
        onChange(
          PushPresentation(
            permission: snapshot.permission, noticeVisible: snapshot.noticeVisible,
            promptDue: snapshot.promptDue, routeTarget: snapshot.routeTarget,
            routeSiteId: snapshot.routeSiteId, routeLotId: snapshot.routeLotId,
            routeLotName: snapshot.routeLotName))
      }
    }
  }

  func refresh() {
    Task { await report() }
  }

  /// The OS prompt, for alerts and sound. The core marks this phone as asked, so the why-line
  /// and the prompt do not show again.
  func ask() {
    Task {
      let granted = await Self.requestAuthorization()
      core.promptAnswered(status: OsNotificationPermission(promptGranted: granted).rawValue)
      await report()
    }
  }

  /// This app's notification settings in the system Settings.
  func openSettings() {
    guard let url = URL(string: UIApplication.openNotificationSettingsURLString) else { return }
    UIApplication.shared.open(url)
  }

  func routeHandled() { core.routeHandled() }

  /// Tells the core what the OS says, and once the permission is granted asks APNs for the
  /// device token, which arrives in the app delegate.
  private func report() async {
    guard let permission = await Self.permission() else { return }
    core.reportPermission(status: permission.rawValue)
    // Every time, not once per run: a registration that failed (no network) is tried again at the
    // next foreground, and the core ignores a token it already has.
    if permission == .granted {
      UIApplication.shared.registerForRemoteNotifications()
    }
  }

  private nonisolated static func permission() async -> OsNotificationPermission? {
    let settings = await UNUserNotificationCenter.current().notificationSettings()
    switch settings.authorizationStatus {
    case .notDetermined: return .notDetermined
    case .denied: return .denied
    case .authorized, .provisional, .ephemeral: return .granted
    @unknown default: return nil
    }
  }

  private nonisolated static func requestAuthorization() async -> Bool {
    var options: UNAuthorizationOptions = []
    for option in PushDisplay.authorization {
      switch option {
      case .alert: options.insert(.alert)
      case .sound: options.insert(.sound)
      }
    }
    return (try? await UNUserNotificationCenter.current().requestAuthorization(options: options))
      ?? false
  }
}
