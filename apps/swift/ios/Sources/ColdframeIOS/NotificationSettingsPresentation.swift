import Foundation

/// A Reminder cadence, as the contract names it. There is no "Never".
public enum ReminderCadenceKind: String, CaseIterable, Hashable, Sendable {
  case daily
  case every2Days

  /// "Daily" or "Every 2 days": the same words in My notifications and in Site settings.
  public var label: L10n {
    switch self {
    case .daily: .remindersDaily
    case .every2Days: .remindersEvery2Days
    }
  }
}

/// My Reminder cadence for the current Site (UX-DR50). The raw value is what the core takes:
/// the contract value, or empty for "Use Site setting".
public enum MyReminderCadenceKind: String, CaseIterable, Hashable, Sendable {
  case useSiteSetting = ""
  case daily
  case every2Days

  public var label: L10n {
    switch self {
    case .useSiteSetting: .notificationsCadenceUseSite
    case .daily: ReminderCadenceKind.daily.label
    case .every2Days: ReminderCadenceKind.every2Days.label
    }
  }
}

/// The control of My notifications a change that was not saved belongs to, as the core names it.
public enum NotificationControlKind: String, CaseIterable, Sendable {
  case window
  case timeZone
  case mute
  case reminderCadence
}

/// Why the notification settings could not be read, or why a change was not saved, as the core
/// named it. Each message says what happened and that nothing was changed.
public enum NotificationSettingsNoticeKind: String, CaseIterable, Sendable {
  case invalid
  case siteRefused
  case notSaved
  case unreachable
  case certificate
  case unexpected

  /// The copy for a failed load (`control` is nil) or for the change to `control`.
  public func message(control: NotificationControlKind?) -> L10n {
    switch self {
    case .invalid:
      switch control {
      case .window: .notificationsNoticeInvalidWindow
      case .timeZone: .notificationsNoticeInvalidTimeZone
      default: .notificationsNoticeNotSaved
      }
    case .siteRefused: .notificationsNoticeSiteGone
    case .notSaved: .notificationsNoticeNotSaved
    case .unreachable: .noticeUnreachable
    case .certificate: .noticeCertificate
    case .unexpected: .notificationsUnavailable
    }
  }

  /// The one notice that names the Site (`%@`).
  public var takesSite: Bool { self == .siteRefused }

  /// A change that did not happen is said at once.
  public var announcement: Announcement { .assertive }
}

/// A notice as it is drawn: the catalogue entry, the Site it names (or nil) and whether Try
/// again can help.
public struct NotificationNoticePresentation: Equatable, Sendable {
  public let message: L10n
  public let subject: String?
  public let tryAgain: Bool

  public init(message: L10n, subject: String?, tryAgain: Bool) {
    self.message = message
    self.subject = subject
    self.tryAgain = tryAgain
  }

  public let announcement: Announcement = .assertive
}

/// A time of day, as a time picker hands it over.
public struct NotificationTime: Equatable, Sendable {
  public let hour: Int
  public let minute: Int

  public init(hour: Int, minute: Int) {
    self.hour = hour
    self.minute = minute
  }
}

/// The Notification Window control (UX-DR47): two time pickers, a decorative 24 h bar, the
/// window in big type, the helper naming the window's own start, and Save. `from` and `to` are
/// the core's draft (`"HH:mm"`); the shell never validates a window (AD-14).
public struct NotificationWindowPresentation: Equatable, Sendable {
  public static let minutesPerDay = 1440
  /// The hours under the 24 h bar.
  public static let axisHours = ["00", "06", "12", "18", "24"]
  /// The pickers count in a zone without daylight saving, so no time of day is skipped.
  public static let pickerTimeZone = TimeZone(secondsFromGMT: 0) ?? .gmt
  /// The calendar the pickers count in: Gregorian, in `pickerTimeZone`.
  public static let pickerCalendar: Calendar = {
    var calendar = Calendar(identifier: .gregorian)
    calendar.timeZone = pickerTimeZone
    return calendar
  }()

  public let from: String
  public let to: String
  public let fromMinutes: Int
  public let toMinutes: Int
  public let savedFrom: String
  public let savedTo: String
  public let isDirty: Bool
  public let isOutOfOrder: Bool
  public let canSave: Bool
  public let isWorking: Bool
  public let isSaved: Bool

  public init(
    from: String, to: String, fromMinutes: Int, toMinutes: Int, savedFrom: String = "",
    savedTo: String = "", isDirty: Bool = false, isOutOfOrder: Bool = false, canSave: Bool = false,
    isWorking: Bool = false, isSaved: Bool = false
  ) {
    self.from = from
    self.to = to
    self.fromMinutes = fromMinutes
    self.toMinutes = toMinutes
    self.savedFrom = savedFrom
    self.savedTo = savedTo
    self.isDirty = isDirty
    self.isOutOfOrder = isOutOfOrder
    self.canSave = canSave
    self.isWorking = isWorking
    self.isSaved = isSaved
  }

  /// "07:00 to 22:00".
  public var range: Copy { Copy(.notificationsWindowRange, .text(from), .text(to)) }

  /// "Outside this window, anything waits for one summary at 07:00.": the window's own start.
  public var helper: Copy { Copy(.notificationsWindowHelper, .text(from)) }

  /// "The start must be before the end." while the draft is out of order.
  public var inlineError: L10n? { isOutOfOrder ? .notificationsWindowNotBefore : nil }

  /// Save, or "Saving…" in place while it is sent.
  public var saveLabel: L10n { isWorking ? .notificationsWindowSaving : .notificationsWindowSave }

  public var isSaveEnabled: Bool { canSave && !isWorking }

  /// "Saved." after a save, until the next edit.
  public var savedLine: L10n? { isSaved ? .notificationsWindowSaved : nil }

  /// Where the window lies on the 24 h bar, 0...1 of its width; nil while it is out of order.
  public var bar: (start: Double, end: Double)? {
    guard toMinutes > fromMinutes else { return nil }
    let day = Double(Self.minutesPerDay)
    return (
      min(1, max(0, Double(fromMinutes) / day)), min(1, max(0, Double(toMinutes) / day))
    )
  }

  /// The draft's start for a time picker, in `pickerTimeZone`.
  public var fromDate: Date { Self.date(minutes: fromMinutes) }

  /// The draft's end for a time picker, in `pickerTimeZone`.
  public var toDate: Date { Self.date(minutes: toMinutes) }

  /// The date a time picker shows for `minutes` since midnight, in `pickerTimeZone`.
  public static func date(minutes: Int) -> Date {
    Date(timeIntervalSince1970: Double(minutes) * 60)
  }

  /// The hour and minute a time picker chose, in `pickerTimeZone`.
  public static func time(of date: Date) -> NotificationTime {
    let minutes = Int((date.timeIntervalSince1970 / 60).rounded(.down))
    let ofDay = ((minutes % minutesPerDay) + minutesPerDay) % minutesPerDay
    return NotificationTime(hour: ofDay / 60, minute: ofDay % 60)
  }
}

/// The current Site's part of My notifications: the "Mute ‹Site›" switch (UX-DR49) and my
/// Reminder cadence (UX-DR50). Both apply at once.
public struct SiteNotificationsPresentation: Equatable, Sendable {
  public let siteId: String
  public let siteName: String
  public let muted: Bool
  /// Nil for a cadence this client does not know: then no segment is selected.
  public let cadence: MyReminderCadenceKind?
  /// The Site setting; nil for one this client does not know.
  public let siteCadence: ReminderCadenceKind?
  public let working: Bool

  public init(
    siteId: String, siteName: String, muted: Bool, cadence: MyReminderCadenceKind?,
    siteCadence: ReminderCadenceKind?, working: Bool = false
  ) {
    self.siteId = siteId
    self.siteName = siteName
    self.muted = muted
    self.cadence = cadence
    self.siteCadence = siteCadence
    self.working = working
  }

  /// "Mute Home garden".
  public var muteLabel: Copy { Copy(.notificationsMute, .text(siteName)) }

  /// "Only you stop getting notifications from Home garden."
  public var muteHelper: Copy { Copy(.notificationsMuteHelper, .text(siteName)) }

  /// "Use Site setting" / "Daily" / "Every 2 days".
  public var cadenceSegments: [SegmentPresentation<MyReminderCadenceKind>] {
    MyReminderCadenceKind.allCases.map {
      SegmentPresentation(value: $0, label: $0.label, isSelected: $0 == cadence)
    }
  }

  /// "Site setting: Daily"; nil while the Site setting is not one this client knows.
  public func cadenceHelper(_ context: CopyContext) -> String? {
    siteCadence.map {
      context.text(.notificationsCadenceHelper, .text(context.text($0.label)))
    }
  }
}

/// What My notifications shows.
public enum NotificationSettingsSurface: Equatable, Sendable {
  /// Signed out, or the settings were not asked for yet.
  case idle
  case loading
  case failed(notice: NotificationSettingsNoticeKind, tryAgain: Bool)
  case ready
}

/// My notifications as the SwiftUI shell sees it (Story 6.3, UX-DR72), built from the core's
/// flat `NotificationSettingsSnapshot`: the Notification Window control (UX-DR47), the time-zone
/// confirm panel (UX-DR48), and for the current Site the "Mute ‹Site›" switch (UX-DR49) and my
/// Reminder cadence (UX-DR50). Without a current Site only the window and the time zone show.
/// The shell never computes a rule (AD-14) and never delivers anything.
public struct NotificationSettingsPresentation: Equatable, Sendable {
  public let surface: NotificationSettingsSurface
  public let window: NotificationWindowPresentation?
  public let timeZone: TimeZonePanelPresentation?
  public let site: SiteNotificationsPresentation?
  /// Why the last change was not saved; its control is back at the Server's value.
  public let notice: NotificationSettingsNoticeKind?
  public let noticeControl: NotificationControlKind?
  public let noticeTryAgain: Bool

  public static let idle = NotificationSettingsPresentation(surface: .idle)

  public init(
    surface: NotificationSettingsSurface, window: NotificationWindowPresentation? = nil,
    timeZone: TimeZonePanelPresentation? = nil, site: SiteNotificationsPresentation? = nil,
    notice: NotificationSettingsNoticeKind? = nil, noticeControl: NotificationControlKind? = nil,
    noticeTryAgain: Bool = false
  ) {
    self.surface = surface
    self.window = window
    self.timeZone = timeZone
    self.site = site
    self.notice = notice
    self.noticeControl = noticeControl
    self.noticeTryAgain = noticeTryAgain
  }

  /// Mirrors the flat snapshot field for field. An unknown notice is drawn as unexpected; a
  /// cadence this client does not know is left out, never worded as another one.
  public init(
    surface: String, notice: String?, noticeTryAgain: Bool, noticeControl: String?,
    windowFrom: String, windowTo: String, windowFromMinutes: Int, windowToMinutes: Int,
    savedWindowFrom: String, savedWindowTo: String, windowDirty: Bool, windowOutOfOrder: Bool,
    canSaveWindow: Bool, windowWorking: Bool, windowSaved: Bool, timeZoneDetected: String,
    timeZoneChosen: String?, timeZoneChanging: Bool, timeZoneWorking: Bool, hasSite: Bool,
    siteId: String?, siteName: String?, muted: Bool, reminderCadence: String,
    siteReminderCadence: String, siteWorking: Bool
  ) {
    let noticeKind = notice.map { NotificationSettingsNoticeKind(rawValue: $0) ?? .unexpected }
    switch surface {
    case "loading":
      self.init(surface: .loading)
      return
    case "failed":
      self.init(surface: .failed(notice: noticeKind ?? .unexpected, tryAgain: noticeTryAgain))
      return
    case "ready": break
    default:
      self.init(surface: .idle)
      return
    }
    var site: SiteNotificationsPresentation?
    if hasSite, let siteId, let siteName {
      site = SiteNotificationsPresentation(
        siteId: siteId, siteName: siteName, muted: muted,
        cadence: MyReminderCadenceKind(rawValue: reminderCadence),
        siteCadence: ReminderCadenceKind(rawValue: siteReminderCadence), working: siteWorking)
    }
    self.init(
      surface: .ready,
      window: NotificationWindowPresentation(
        from: windowFrom, to: windowTo, fromMinutes: windowFromMinutes,
        toMinutes: windowToMinutes, savedFrom: savedWindowFrom, savedTo: savedWindowTo,
        isDirty: windowDirty, isOutOfOrder: windowOutOfOrder, canSave: canSaveWindow,
        isWorking: windowWorking, isSaved: windowSaved),
      timeZone: TimeZonePanelPresentation(
        detected: timeZoneDetected, chosen: timeZoneChosen, changing: timeZoneChanging,
        working: timeZoneWorking),
      site: site, notice: noticeKind,
      noticeControl: noticeControl.flatMap(NotificationControlKind.init(rawValue:)),
      noticeTryAgain: noticeTryAgain)
  }

  private func drawn(_ notice: NotificationSettingsNoticeKind, _ control: NotificationControlKind?)
    -> NotificationNoticePresentation
  {
    NotificationNoticePresentation(
      message: notice.message(control: control),
      subject: notice.takesSite ? site?.siteName ?? "" : nil, tryAgain: noticeTryAgain)
  }

  /// The notice of the change to `control` that was not saved, shown under that control.
  public func notice(for control: NotificationControlKind) -> NotificationNoticePresentation? {
    guard surface == .ready, let notice, noticeControl == control else { return nil }
    return drawn(notice, control)
  }

  /// A notice that belongs to no control this client knows: shown above the controls, so a
  /// change that was not saved is never silent.
  public var generalNotice: NotificationNoticePresentation? {
    guard surface == .ready, let notice, noticeControl == nil else { return nil }
    return drawn(notice, nil)
  }
}

/// My notifications of the shared Kotlin core, as the SwiftUI shell sees it. The app target
/// adapts `IosNotificationSettings` of `ColdframeCore`; the Server calls, the hand-over of the
/// time zone and every rule never leave the core.
@MainActor
public protocol NotificationSettingsService: AnyObject {
  /// Calls `onChange` with the current presentation and every change.
  func observe(_ onChange: @escaping @MainActor (NotificationSettingsPresentation) -> Void)
  /// Every IANA zone ID the core knows, for Change on the time-zone panel.
  func availableTimeZones() -> [String]
  /// Reads the settings again: every entry of My notifications.
  func load()
  /// Try again: after a failed load, or for the change that was not saved.
  func retry()
  /// The start of the Notification Window, from its time picker.
  func setWindowFrom(hour: Int, minute: Int)
  /// The end of the Notification Window, from its time picker.
  func setWindowTo(hour: Int, minute: Int)
  func saveWindow()
  func confirmTimeZone()
  func changeTimeZone()
  func pickTimeZone(_ zoneId: String)
  /// The "Mute ‹Site›" switch of the current Site; applies at once.
  func setMuted(_ muted: Bool)
  /// My Reminder cadence for the current Site; applies at once.
  func setReminderCadence(_ cadence: MyReminderCadenceKind)
}
