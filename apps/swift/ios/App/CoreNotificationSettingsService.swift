import ColdframeCore
import ColdframeIOS
import Foundation

/// Adapts `IosNotificationSettings` of the shared Kotlin core to `NotificationSettingsService`.
/// The snapshot is flat strings, flags and numbers; the Server calls, the hand-over of the time
/// zone and every rule stay in the core (AD-14). Nothing here delivers a notification.
@MainActor
final class CoreNotificationSettingsService: NotificationSettingsService {
  private let core: IosNotificationSettings
  private var watch: Watch?

  init(core: IosNotificationSettings) {
    self.core = core
  }

  func observe(_ onChange: @escaping @MainActor (NotificationSettingsPresentation) -> Void) {
    watch?.close()
    watch = core.watch { snapshot in
      MainActor.assumeIsolated {
        onChange(
          NotificationSettingsPresentation(
            surface: snapshot.surface, notice: snapshot.notice,
            noticeTryAgain: snapshot.noticeTryAgain, noticeControl: snapshot.noticeControl,
            windowFrom: snapshot.windowFrom, windowTo: snapshot.windowTo,
            windowFromMinutes: Int(snapshot.windowFromMinutes),
            windowToMinutes: Int(snapshot.windowToMinutes),
            savedWindowFrom: snapshot.savedWindowFrom, savedWindowTo: snapshot.savedWindowTo,
            windowDirty: snapshot.windowDirty, windowOutOfOrder: snapshot.windowOutOfOrder,
            canSaveWindow: snapshot.canSaveWindow, windowWorking: snapshot.windowWorking,
            windowSaved: snapshot.windowSaved, timeZoneDetected: snapshot.timeZoneDetected,
            timeZoneChosen: snapshot.timeZoneChosen, timeZoneChanging: snapshot.timeZoneChanging,
            timeZoneWorking: snapshot.timeZoneWorking, hasSite: snapshot.hasSite,
            siteId: snapshot.siteId, siteName: snapshot.siteName, muted: snapshot.muted,
            reminderCadence: snapshot.reminderCadence,
            siteReminderCadence: snapshot.siteReminderCadence, siteWorking: snapshot.siteWorking))
      }
    }
  }

  func availableTimeZones() -> [String] { core.availableTimeZones() }

  func load() { core.load() }

  func retry() { core.retry() }

  func setWindowFrom(hour: Int, minute: Int) {
    core.setWindowFromTime(hour: Int32(hour), minute: Int32(minute))
  }

  func setWindowTo(hour: Int, minute: Int) {
    core.setWindowToTime(hour: Int32(hour), minute: Int32(minute))
  }

  func saveWindow() { core.saveWindow() }

  func confirmTimeZone() { core.confirmTimeZone() }

  func changeTimeZone() { core.changeTimeZone() }

  func pickTimeZone(_ zoneId: String) { core.pickTimeZone(zoneId: zoneId) }

  func setMuted(_ muted: Bool) { core.setMuted(muted: muted) }

  /// The core takes the contract value, or empty for "Use Site setting".
  func setReminderCadence(_ cadence: MyReminderCadenceKind) {
    core.setReminderCadence(cadence: cadence.rawValue)
  }
}
