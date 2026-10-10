import ColdframeIOS
import UIKit
@preconcurrency import UserNotifications

/// What the app delegate receives from the OS, kept until `CorePushService` takes it: a tapped
/// notification can start the app before the SwiftUI shell and its adapters exist.
@MainActor
final class PushInbox {
  static let shared = PushInbox()

  private var onOpened: (([String: String]) -> Void)?
  private var onDeviceToken: ((String) -> Void)?
  private var waitingOpened: [[String: String]] = []
  private var waitingDeviceToken: String?

  private init() {}

  /// Hands over everything that waited, then every later event as it arrives.
  func attach(
    onOpened: @escaping ([String: String]) -> Void, onDeviceToken: @escaping (String) -> Void
  ) {
    self.onOpened = onOpened
    self.onDeviceToken = onDeviceToken
    waitingOpened.forEach(onOpened)
    waitingOpened = []
    if let waitingDeviceToken { onDeviceToken(waitingDeviceToken) }
    waitingDeviceToken = nil
  }

  /// The routing keys of a tapped notification.
  func opened(_ routing: [String: String]) {
    if let onOpened { onOpened(routing) } else { waitingOpened.append(routing) }
  }

  /// The APNs device token as lowercase hex.
  func deviceToken(_ hex: String) {
    if let onDeviceToken { onDeviceToken(hex) } else { waitingDeviceToken = hex }
  }
}

/// A closure of the OS that is called back on the main actor.
private struct MainActorCallback<Call>: @unchecked Sendable {
  let call: Call
}

/// The UIKit side of push (Story 6.5): the APNs device token and the notification centre. The
/// Server writes every notification; the app adds no category and no action, counts nothing on
/// its icon and never posts a notification of its own (UX-DR119, UX-DR121).
@MainActor
final class AppDelegate: NSObject, UIApplicationDelegate, UNUserNotificationCenterDelegate {
  func application(
    _ application: UIApplication,
    didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil
  ) -> Bool {
    // Set before launch ends, so the tap that started the app is delivered too.
    UNUserNotificationCenter.current().delegate = self
    return true
  }

  func application(
    _ application: UIApplication,
    didRegisterForRemoteNotificationsWithDeviceToken deviceToken: Data
  ) {
    PushInbox.shared.deviceToken(PushPayload.hex(deviceToken))
  }

  /// An app without the push entitlement, or without a network, has no device token: it works
  /// and registers nothing. The next start asks APNs again.
  func application(
    _ application: UIApplication, didFailToRegisterForRemoteNotificationsWithError error: Error
  ) {}

  /// A notification that arrives while the app is in front shows like any other.
  nonisolated func userNotificationCenter(
    _ center: UNUserNotificationCenter, willPresent notification: UNNotification,
    withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void
  ) {
    var options: UNNotificationPresentationOptions = []
    for option in PushDisplay.foreground {
      switch option {
      case .banner: options.insert(.banner)
      case .list: options.insert(.list)
      case .sound: options.insert(.sound)
      }
    }
    let shown = options
    let done = MainActorCallback(call: completionHandler)
    Task { @MainActor in done.call(shown) }
  }

  /// A tapped notification: its routing keys go to the core, which switches the Site and names
  /// the route once the Sites are loaded (UX-DR120).
  nonisolated func userNotificationCenter(
    _ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse,
    withCompletionHandler completionHandler: @escaping () -> Void
  ) {
    let tapped = response.actionIdentifier == UNNotificationDefaultActionIdentifier
    let routing = PushPayload.routing(userInfo: response.notification.request.content.userInfo)
    let done = MainActorCallback(call: completionHandler)
    Task { @MainActor in
      if tapped, let routing { PushInbox.shared.opened(routing) }
      done.call()
    }
  }
}
