import Foundation
import Testing

@testable import ColdframeIOS

/// A `NotificationSettingsPresentation` as `CoreNotificationSettingsService` builds it from the
/// core's flat `NotificationSettingsSnapshot`: a User on "Home garden" with the default window
/// 07:00 to 22:00 and the phone's zone proposed.
enum NotificationsFixture {
  static func settings(
    surface: String = "ready", notice: String? = nil, noticeTryAgain: Bool = false,
    noticeControl: String? = nil, windowFrom: String = "07:00", windowTo: String = "22:00",
    windowFromMinutes: Int = 420, windowToMinutes: Int = 1320, savedWindowFrom: String = "07:00",
    savedWindowTo: String = "22:00", windowDirty: Bool = false, windowOutOfOrder: Bool = false,
    canSaveWindow: Bool = false, windowWorking: Bool = false, windowSaved: Bool = false,
    timeZoneDetected: String = "Europe/Zurich", timeZoneChosen: String? = nil,
    timeZoneChanging: Bool = false, timeZoneWorking: Bool = false, hasSite: Bool = true,
    siteId: String? = "a", siteName: String? = "Home garden", muted: Bool = false,
    reminderCadence: String = "", siteReminderCadence: String = "daily", siteWorking: Bool = false
  ) -> NotificationSettingsPresentation {
    NotificationSettingsPresentation(
      surface: surface, notice: notice, noticeTryAgain: noticeTryAgain,
      noticeControl: noticeControl, windowFrom: windowFrom, windowTo: windowTo,
      windowFromMinutes: windowFromMinutes, windowToMinutes: windowToMinutes,
      savedWindowFrom: savedWindowFrom, savedWindowTo: savedWindowTo, windowDirty: windowDirty,
      windowOutOfOrder: windowOutOfOrder, canSaveWindow: canSaveWindow,
      windowWorking: windowWorking, windowSaved: windowSaved, timeZoneDetected: timeZoneDetected,
      timeZoneChosen: timeZoneChosen, timeZoneChanging: timeZoneChanging,
      timeZoneWorking: timeZoneWorking, hasSite: hasSite, siteId: hasSite ? siteId : nil,
      siteName: hasSite ? siteName : nil, muted: muted, reminderCadence: reminderCadence,
      siteReminderCadence: hasSite ? siteReminderCadence : "", siteWorking: siteWorking)
  }

  /// No current Site: only the window and the time zone show.
  static let withoutSite = settings(hasSite: false)
  static let confirmed = settings(timeZoneChosen: "Europe/Vienna")
}

private let context = Overview.context

@Test("UX-DR47 the Notification Window reads 07:00 to 22:00 and its helper names its own start")
func notificationWindowDefaults() throws {
  let window = try #require(NotificationsFixture.settings().window)

  #expect(window.from == "07:00")
  #expect(window.to == "22:00")
  #expect(window.range == Copy(.notificationsWindowRange, .text("07:00"), .text("22:00")))
  #expect(Catalogue.resolve(window.range) == "07:00 to 22:00")
  #expect(
    Catalogue.resolve(window.helper)
      == "Outside this window, anything waits for one summary at 07:00.")
  #expect(window.inlineError == nil)
  #expect(window.savedLine == nil)
  #expect(window.saveLabel == .notificationsWindowSave)
  // Nothing to save until the window was changed.
  #expect(!window.isSaveEnabled)
}

@Test("UX-DR47 the helper follows the draft, and Save sends it: Saving… in place, then Saved.")
func notificationWindowEdited() throws {
  let edited = try #require(
    NotificationsFixture.settings(
      windowFrom: "06:30", windowFromMinutes: 390, windowDirty: true, canSaveWindow: true
    ).window)
  let working = try #require(
    NotificationsFixture.settings(
      windowFrom: "06:30", windowFromMinutes: 390, windowDirty: true, windowWorking: true
    ).window)
  let saved = try #require(
    NotificationsFixture.settings(
      windowFrom: "06:30", windowFromMinutes: 390, savedWindowFrom: "06:30", windowSaved: true
    ).window)

  #expect(Catalogue.resolve(edited.range) == "06:30 to 22:00")
  #expect(
    Catalogue.resolve(edited.helper)
      == "Outside this window, anything waits for one summary at 06:30.")
  #expect(edited.isSaveEnabled)
  #expect(working.saveLabel == .notificationsWindowSaving)
  #expect(!working.isSaveEnabled)
  #expect(saved.savedLine == .notificationsWindowSaved)
  #expect(try Catalogue.entries()["notifications_window_saved"] == "Saved.")
}

@Test("UX-DR47 a window that does not start before it ends says so inline and cannot be saved")
func notificationWindowOutOfOrder() throws {
  let window = try #require(
    NotificationsFixture.settings(
      windowFrom: "22:00", windowFromMinutes: 1320, windowDirty: true, windowOutOfOrder: true
    ).window)

  #expect(window.inlineError == .notificationsWindowNotBefore)
  #expect(!window.isSaveEnabled)
  #expect(window.bar == nil)
  #expect(
    try Catalogue.entries()["notifications_window_not_before"]
      == "The start must be before the end.")
}

@Test("UX-DR47 the 24 h bar previews the window, is decorative and is hidden from VoiceOver")
func notificationWindowBar() throws {
  let window = try #require(NotificationsFixture.settings().window)
  let bar = try #require(window.bar)

  #expect(abs(bar.start - 420.0 / 1440.0) < 0.0001)
  #expect(abs(bar.end - 1320.0 / 1440.0) < 0.0001)
  #expect(NotificationWindowPresentation.axisHours == ["00", "06", "12", "18", "24"])
  let views = try Repo.text(
    "apps/swift/ios/Sources/ColdframeIOS/UI/NotificationSettingsViews.swift")
  let drawn = try #require(
    views.components(separatedBy: "struct NotificationWindowBar").last?
      .components(separatedBy: "\n  }\n").first)
  #expect(drawn.contains(".accessibilityHidden(true)"))
  #expect(drawn.contains("Hatch(palette: palette)"))
  #expect(!drawn.contains("Button"))
  #expect(!drawn.contains("accessibilityLabel"))
}

@Test("UX-DR47 the two time pickers hand the core an hour and a minute, and show its draft")
func notificationWindowPickers() throws {
  let window = try #require(NotificationsFixture.settings().window)

  #expect(
    NotificationWindowPresentation.time(of: window.fromDate) == NotificationTime(hour: 7, minute: 0)
  )
  #expect(
    NotificationWindowPresentation.time(of: window.toDate) == NotificationTime(hour: 22, minute: 0))
  let halfPast = NotificationWindowPresentation.date(minutes: 23 * 60 + 59)
  #expect(
    NotificationWindowPresentation.time(of: halfPast) == NotificationTime(hour: 23, minute: 59))
  // The pickers count in a zone without daylight saving, so no time of day is ever skipped.
  #expect(NotificationWindowPresentation.pickerTimeZone.secondsFromGMT() == 0)
  #expect(
    NotificationWindowPresentation.time(of: Date(timeIntervalSince1970: -60))
      == NotificationTime(hour: 23, minute: 59))
}

@Test("UX-DR48 the time-zone confirm panel proposes the detected zone with Confirm and Change")
func notificationsTimeZoneProposed() throws {
  let panel = try #require(NotificationsFixture.settings().timeZone)

  #expect(panel.zone == "Europe/Zurich")
  #expect(!panel.isConfirmed)
  #expect(panel.showsConfirm)
  #expect(!panel.showsList)
  #expect(Catalogue.resolve(panel.sentenceCopy) == "Is your time zone Europe/Zurich?")
  #expect(try Catalogue.entries()["time_zone_helper"] == "Used for your Notification Window.")
}

@Test("UX-DR48 a zone the User chose is named and is never replaced by the detected one")
func notificationsTimeZoneChosen() throws {
  let panel = try #require(NotificationsFixture.confirmed.timeZone)
  let changing = try #require(
    NotificationsFixture.settings(timeZoneChosen: "Europe/Vienna", timeZoneChanging: true).timeZone)
  let working = try #require(
    NotificationsFixture.settings(timeZoneChosen: "Europe/Vienna", timeZoneWorking: true).timeZone)

  #expect(panel.zone == "Europe/Vienna")
  #expect(panel.isConfirmed)
  #expect(!panel.showsConfirm)
  #expect(Catalogue.resolve(panel.sentenceCopy) == "Your time zone is Europe/Vienna.")
  #expect(changing.showsList)
  #expect(!changing.showsConfirm)
  #expect(!panel.working)
  #expect(working.working)
}

@Test("UX-DR48 without a zone to propose the panel says so and shows the list at once")
func notificationsTimeZoneUnknown() throws {
  let panel = try #require(NotificationsFixture.settings(timeZoneDetected: "").timeZone)

  #expect(panel.zone.isEmpty)
  #expect(!panel.showsConfirm)
  #expect(panel.showsList)
  #expect(panel.sentenceCopy == Copy(.timeZoneUnknown))
  #expect(
    Catalogue.resolve(panel.sentenceCopy)
      == "Your time zone could not be detected. Choose it from the list.")
}

@Test("UX-DR48 Create Site and My notifications draw the same panel")
func timeZonePanelShared() throws {
  let sites = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/SitesViews.swift")
  let notifications = try Repo.text(
    "apps/swift/ios/Sources/ColdframeIOS/UI/NotificationSettingsViews.swift")

  #expect(sites.contains("struct TimeZonePanel: View"))
  #expect(sites.contains("TimeZonePanel(presentation: presentation.timeZone"))
  #expect(notifications.contains("TimeZonePanel(presentation: timeZone"))
  #expect(!notifications.contains("struct TimeZonePanel"))
  // Create Site still proposes and confirms as before.
  let proposed = TimeZonePanelPresentation(detected: "Europe/Zurich", chosen: nil, changing: false)
  #expect(proposed.showsConfirm)
  #expect(proposed.sentence == .timeZoneQuestion)
  #expect(!proposed.working)
}

@Test("UX-DR49 Mute is a native switch labelled Mute ‹Site›, for the current Site only")
func muteToggle() throws {
  let site = try #require(NotificationsFixture.settings().site)
  let muted = try #require(NotificationsFixture.settings(muted: true).site)

  #expect(!site.muted)
  #expect(muted.muted)
  #expect(Catalogue.resolve(site.muteLabel) == "Mute Home garden")
  #expect(
    Catalogue.resolve(site.muteHelper) == "Only you stop getting notifications from Home garden.")
  let views = try Repo.text(
    "apps/swift/ios/Sources/ColdframeIOS/UI/NotificationSettingsViews.swift")
  #expect(views.contains("Toggle(isOn:"))
  // Without a current Site there is nothing to mute.
  #expect(NotificationsFixture.withoutSite.site == nil)
  #expect(NotificationsFixture.settings(siteName: nil).site == nil)
}

@Test("UX-DR50 my Reminder cadence offers Use Site setting, Daily and Every 2 days, never Never")
func myReminderCadence() throws {
  let site = try #require(NotificationsFixture.settings().site)
  let own = try #require(
    NotificationsFixture.settings(reminderCadence: "every2Days", siteReminderCadence: "daily").site)

  #expect(site.cadenceSegments.map(\.value) == [.useSiteSetting, .daily, .every2Days])
  #expect(
    site.cadenceSegments.map(\.label) == [
      .notificationsCadenceUseSite, .remindersDaily, .remindersEvery2Days,
    ])
  #expect(site.cadenceSegments.filter(\.isSelected).map(\.value) == [.useSiteSetting])
  #expect(own.cadenceSegments.filter(\.isSelected).map(\.value) == [.every2Days])
  // What the core takes: the contract values, and empty for "Use Site setting".
  #expect(MyReminderCadenceKind.allCases.map(\.rawValue) == ["", "daily", "every2Days"])
  #expect(ReminderCadenceKind.allCases.map(\.rawValue) == ["daily", "every2Days"])
  let entries = try Catalogue.entries()
  #expect(entries["notifications_cadence_use_site"] == "Use Site setting")
  #expect(entries["reminders_daily"] == "Daily")
  #expect(entries["reminders_every_2_days"] == "Every 2 days")
  #expect(!entries.values.contains("Never"))
}

@Test("UX-DR50 the helper under my Reminder cadence names the Site setting")
func myReminderCadenceHelper() throws {
  let daily = try #require(NotificationsFixture.settings().site)
  let every2Days = try #require(
    NotificationsFixture.settings(siteReminderCadence: "every2Days").site)
  let unknown = try #require(NotificationsFixture.settings(siteReminderCadence: "weekly").site)
  let unknownOwn = try #require(NotificationsFixture.settings(reminderCadence: "weekly").site)

  #expect(daily.cadenceHelper(context) == "Site setting: Daily")
  #expect(every2Days.cadenceHelper(context) == "Site setting: Every 2 days")
  // A cadence this client does not know is never worded as another one.
  #expect(unknown.cadenceHelper(context) == nil)
  #expect(unknownOwn.cadenceSegments.allSatisfy { !$0.isSelected })
}

@Test("UX-DR72 My notifications shows the window, the time zone, Mute and my Reminder cadence")
func myNotificationsWithSite() {
  let settings = NotificationsFixture.settings()

  #expect(settings.surface == .ready)
  #expect(settings.window != nil)
  #expect(settings.timeZone != nil)
  #expect(settings.site?.siteName == "Home garden")
  #expect(settings.generalNotice == nil)
  for control in NotificationControlKind.allCases {
    #expect(settings.notice(for: control) == nil)
  }
}

@Test("UX-DR72 without a current Site only the window and the time zone show")
func myNotificationsWithoutSite() {
  let settings = NotificationsFixture.withoutSite

  #expect(settings.surface == .ready)
  #expect(settings.window != nil)
  #expect(settings.timeZone != nil)
  #expect(settings.site == nil)
}

@Test("UX-DR72 a change that was not saved shows its notice under its control, with Try again")
func myNotificationsNotSaved() {
  let mute = NotificationsFixture.settings(
    notice: "notSaved", noticeTryAgain: true, noticeControl: "mute")
  let window = NotificationsFixture.settings(notice: "invalid", noticeControl: "window")
  let zone = NotificationsFixture.settings(notice: "invalid", noticeControl: "timeZone")
  let gone = NotificationsFixture.settings(notice: "siteRefused", noticeControl: "reminderCadence")

  #expect(
    mute.notice(for: .mute)
      == NotificationNoticePresentation(
        message: .notificationsNoticeNotSaved, subject: nil, tryAgain: true))
  #expect(mute.notice(for: .window) == nil)
  #expect(mute.notice(for: .reminderCadence) == nil)
  #expect(window.notice(for: .window)?.message == .notificationsNoticeInvalidWindow)
  #expect(window.notice(for: .window)?.tryAgain == false)
  #expect(zone.notice(for: .timeZone)?.message == .notificationsNoticeInvalidTimeZone)
  #expect(
    gone.notice(for: .reminderCadence)
      == NotificationNoticePresentation(
        message: .notificationsNoticeSiteGone, subject: "Home garden", tryAgain: false))
  // A notice for a control this client does not know is still shown, above the controls.
  let other = NotificationsFixture.settings(
    notice: "notSaved", noticeTryAgain: true, noticeControl: "summary")
  #expect(other.generalNotice?.message == .notificationsNoticeNotSaved)
  #expect(NotificationControlKind.allCases.allSatisfy { other.notice(for: $0) == nil })
}

@Test("UX-DR72 a failed load shows the notice instead of the controls, with Try again")
func myNotificationsFailed() {
  let unreachable = NotificationsFixture.settings(
    surface: "failed", notice: "unreachable", noticeTryAgain: true)
  let certificate = NotificationsFixture.settings(surface: "failed", notice: "certificate")
  let unknown = NotificationsFixture.settings(surface: "failed", notice: "whatever")

  #expect(unreachable.surface == .failed(notice: .unreachable, tryAgain: true))
  #expect(unreachable.window == nil)
  #expect(unreachable.site == nil)
  #expect(certificate.surface == .failed(notice: .certificate, tryAgain: false))
  #expect(unknown.surface == .failed(notice: .unexpected, tryAgain: false))
  #expect(NotificationSettingsNoticeKind.unreachable.message(control: nil) == .noticeUnreachable)
  #expect(NotificationSettingsNoticeKind.certificate.message(control: nil) == .noticeCertificate)
  #expect(
    NotificationSettingsNoticeKind.unexpected.message(control: nil) == .notificationsUnavailable)
  #expect(NotificationSettingsNoticeKind.allCases.allSatisfy { $0.announcement == .assertive })
  #expect(NotificationsFixture.settings(surface: "loading").surface == .loading)
  #expect(NotificationsFixture.settings(surface: "idle").surface == .idle)
  #expect(NotificationsFixture.settings(surface: "later").surface == .idle)
  #expect(NotificationSettingsPresentation.idle.surface == .idle)
}

@Test("UX-DR72 My notifications never asks for permission, shows no off notice, sends no token")
func myNotificationsStaysInScope() throws {
  let views = try Repo.text(
    "apps/swift/ios/Sources/ColdframeIOS/UI/NotificationSettingsViews.swift")
  let service = try Repo.text("apps/swift/ios/App/CoreNotificationSettingsService.swift")

  for text in [views, service] {
    #expect(!text.contains("UNUserNotificationCenter"))
    #expect(!text.contains("requestAuthorization"))
    #expect(!text.contains("registerForRemoteNotifications"))
    #expect(!text.contains("openSettingsURLString"))
  }
  // Every entry of the surface reads the settings again.
  #expect(views.contains(".onAppear { actions.load() }"))
  #expect(service.contains("core.load()"))
}
