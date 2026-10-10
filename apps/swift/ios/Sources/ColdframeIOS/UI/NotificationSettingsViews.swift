#if canImport(SwiftUI)
  import ColdframeDesignTokens
  import Foundation
  import SwiftUI

  /// What My notifications can ask of the core. Built over a `NotificationSettingsService`;
  /// `.none` does nothing, for previews and render tests. The views never validate a window,
  /// pick a zone to propose or decide what a failed change leaves behind (AD-14).
  @MainActor
  public struct NotificationSettingsActions {
    /// Every entry of the surface reads the settings again.
    public var load: () -> Void
    /// Try again: after a failed load, or for the change that was not saved.
    public var retry: () -> Void
    /// The hour and the minute a time picker chose.
    public var setWindowFrom: (Int, Int) -> Void
    public var setWindowTo: (Int, Int) -> Void
    public var saveWindow: () -> Void
    public var timeZone: TimeZoneActions
    public var setMuted: (Bool) -> Void
    public var setReminderCadence: (MyReminderCadenceKind) -> Void

    public init(service: NotificationSettingsService) {
      load = { service.load() }
      retry = { service.retry() }
      setWindowFrom = { service.setWindowFrom(hour: $0, minute: $1) }
      setWindowTo = { service.setWindowTo(hour: $0, minute: $1) }
      saveWindow = { service.saveWindow() }
      timeZone = TimeZoneActions(
        confirm: { service.confirmTimeZone() }, change: { service.changeTimeZone() },
        pick: { service.pickTimeZone($0) }, zones: { service.availableTimeZones() })
      setMuted = { service.setMuted($0) }
      setReminderCadence = { service.setReminderCadence($0) }
    }

    private init() {
      load = {}
      retry = {}
      setWindowFrom = { _, _ in }
      setWindowTo = { _, _ in }
      saveWindow = {}
      timeZone = TimeZoneActions()
      setMuted = { _ in }
      setReminderCadence = { _ in }
    }

    public static let none = NotificationSettingsActions()
  }

  /// My notifications (Story 6.3, UX-DR72): the Notification Window control (UX-DR47), the
  /// time-zone confirm panel (UX-DR48), and for the current Site the "Mute ‹Site›" switch
  /// (UX-DR49) and my Reminder cadence (UX-DR50). Without a current Site only the window and the
  /// time zone show. The switch and the segmented choice apply at once; the window has Save. A
  /// change that was not saved shows its notice under its control, which is back at the Server's
  /// value. Every entry reads the settings again. While notifications are off for the app on
  /// this phone, the notice with Open Settings stands first, whatever the settings' own surface
  /// is (Story 6.5, UX-DR88).
  public struct MyNotificationsView: View {
    let presentation: NotificationSettingsPresentation
    let actions: NotificationSettingsActions
    let push: PushPresentation
    let pushActions: PushActions
    @Environment(\.palette) private var palette
    @Environment(\.locale) private var locale

    public init(
      presentation: NotificationSettingsPresentation, actions: NotificationSettingsActions = .none,
      push: PushPresentation = .idle, pushActions: PushActions = .none
    ) {
      self.presentation = presentation
      self.actions = actions
      self.push = push
      self.pushActions = pushActions
    }

    public var body: some View {
      ScrollView {
        VStack(alignment: .leading, spacing: Spacing.step6) {
          if let notice = push.settingsNotice {
            PushNotice(notice: notice, actions: pushActions)
          }
          switch presentation.surface {
          case .idle, .loading:
            EmptyView()
          case .failed(let notice, let tryAgain):
            InlineNotice(
              message: notice.message(control: nil), announcement: notice.announcement,
              action: tryAgain ? (label: L10n.noticeTryAgain, perform: actions.retry) : nil)
          case .ready:
            content
          }
        }
        .padding(Spacing.gutterMobile)
        .frame(maxWidth: .infinity, alignment: .leading)
      }
      .background(palette.background)
      .navigationTitle(L10n.notificationsTitle.string)
      .onAppear { actions.load() }
    }

    @ViewBuilder
    private var content: some View {
      VStack(alignment: .leading, spacing: Spacing.step6) {
        notice(presentation.generalNotice)
        if let window = presentation.window {
          L10n.notificationsWindow.text.role(Typography.section)
            .foregroundStyle(palette.textPrimary)
            .accessibilityAddTraits(.isHeader)
          NotificationWindowControl(window: window, actions: actions)
          notice(presentation.notice(for: .window))
        }
        if let timeZone = presentation.timeZone {
          TimeZonePanel(presentation: timeZone, actions: actions.timeZone)
          notice(presentation.notice(for: .timeZone))
        }
        // Mute and my Reminder cadence belong to the current Site; without one they are absent.
        if let site = presentation.site {
          MuteToggle(site: site, onChange: actions.setMuted)
          notice(presentation.notice(for: .mute))
          cadence(site)
          notice(presentation.notice(for: .reminderCadence))
        }
      }
    }

    /// The notice of a change that was not saved, with Try again where that can help.
    @ViewBuilder
    private func notice(_ notice: NotificationNoticePresentation?) -> some View {
      if let notice {
        InlineNotice(
          message: notice.message, subject: notice.subject, announcement: notice.announcement,
          action: notice.tryAgain ? (label: L10n.noticeTryAgain, perform: actions.retry) : nil)
      }
    }

    /// My Reminder cadence (UX-DR50): "Use Site setting" / "Daily" / "Every 2 days", applied at
    /// once, with the helper naming the Site setting.
    private func cadence(_ site: SiteNotificationsPresentation) -> some View {
      VStack(alignment: .leading, spacing: Spacing.step3) {
        SegmentedChoice(
          label: .notificationsCadence, segments: site.cadenceSegments,
          onSelect: actions.setReminderCadence)
        if let helper = site.cadenceHelper(.catalogue(locale: locale)) {
          Text(verbatim: helper).role(Typography.helper).foregroundStyle(palette.textHelper)
            .fixedSize(horizontal: false, vertical: true)
        }
      }
    }
  }

  /// Notification Window control (UX-DR47): two time pickers, a 24 h bar that previews the
  /// window, the window in big type, the helper naming the window's own start, and Save, which
  /// reads "Saving…" in place and is followed by a polite "Saved.". Every value is the core's
  /// draft; a picker only hands the chosen hour and minute back to it.
  struct NotificationWindowControl: View {
    let window: NotificationWindowPresentation
    let actions: NotificationSettingsActions
    @Environment(\.palette) private var palette

    var body: some View {
      VStack(alignment: .leading, spacing: Spacing.step4) {
        ViewThatFits(in: .horizontal) {
          HStack(alignment: .top, spacing: Spacing.step6) { pickers }
          VStack(alignment: .leading, spacing: Spacing.step4) { pickers }
        }
        NotificationWindowBar(window: window)
        Text(verbatim: window.range.string).role(Typography.headline)
          .foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
        Text(verbatim: window.helper.string).role(Typography.helper)
          .foregroundStyle(palette.textHelper)
          .fixedSize(horizontal: false, vertical: true)
        if let error = window.inlineError {
          HStack(alignment: .top, spacing: Spacing.step2) {
            CarbonIconShape(.errorFilled).fill(palette.supportErrorText)
              .frame(width: 16, height: 16)
              .accessibilityHidden(true)
            error.text.role(Typography.helper).foregroundStyle(palette.supportErrorText)
              .fixedSize(horizontal: false, vertical: true)
          }
        }
        PrimaryButton(window.saveLabel, isEnabled: window.isSaveEnabled, action: actions.saveWindow)
        if let saved = window.savedLine {
          saved.text.role(Typography.body).foregroundStyle(palette.textPrimary)
            .fixedSize(horizontal: false, vertical: true)
        }
      }
      // "Saved." is said once, politely, when the Server has taken the window.
      .onChange(of: window.isSaved) { _, saved in
        if saved {
          AccessibilityNotification.Announcement(
            AttributedString(L10n.notificationsWindowSaved.string)
          ).post()
        }
      }
    }

    @ViewBuilder
    private var pickers: some View {
      picker(
        .notificationsWindowFrom, spoken: .notificationsWindowFromTitle, date: window.fromDate,
        onPick: actions.setWindowFrom)
      picker(
        .notificationsWindowTo, spoken: .notificationsWindowToTitle, date: window.toDate,
        onPick: actions.setWindowTo)
    }

    /// One native time picker under its label; VoiceOver reads "Window starts at" with the time.
    private func picker(
      _ label: L10n, spoken: L10n, date: Date, onPick: @escaping (Int, Int) -> Void
    ) -> some View {
      VStack(alignment: .leading, spacing: Spacing.step2) {
        label.text.role(Typography.body).foregroundStyle(palette.textSecondary)
        DatePicker(
          selection: Binding(
            get: { date },
            set: {
              let time = NotificationWindowPresentation.time(of: $0)
              onPick(time.hour, time.minute)
            }),
          displayedComponents: .hourAndMinute
        ) {
          spoken.text
        }
        .labelsHidden()
        .environment(\.calendar, NotificationWindowPresentation.pickerCalendar)
        .environment(\.timeZone, NotificationWindowPresentation.pickerTimeZone)
        .tint(palette.primaryText)
        .frame(minHeight: TouchTarget.minimum)
      }
    }
  }

  /// The 24 h preview of the window: `primary` inside it, hatched outside, over the hours 00 to
  /// 24. It is a picture of the two times above it, so VoiceOver skips all of it.
  struct NotificationWindowBar: View {
    static let height: Double = 24

    let window: NotificationWindowPresentation
    @Environment(\.palette) private var palette

    var body: some View {
      VStack(alignment: .leading, spacing: Spacing.step1) {
        GeometryReader { geometry in
          let width = Double(geometry.size.width)
          ZStack(alignment: .topLeading) {
            Hatch(palette: palette)
            if let bar = window.bar {
              Rectangle().fill(palette.primary)
                .frame(width: max(0, width * (bar.end - bar.start)), height: Self.height)
                .offset(x: width * bar.start)
            }
          }
        }
        .frame(height: Self.height)
        HStack(spacing: 0) {
          ForEach(NotificationWindowPresentation.axisHours, id: \.self) { hour in
            if hour != NotificationWindowPresentation.axisHours.first {
              Spacer(minLength: 0)
            }
            Text(verbatim: hour).role(Typography.metaMono)
              .foregroundStyle(palette.textSecondary)
          }
        }
      }
      .accessibilityHidden(true)
    }
  }

  /// Mute toggle (UX-DR49): the native switch, labelled "Mute ‹Site›" with what it does under
  /// it. It applies at once and mutes this Site for this User only.
  struct MuteToggle: View {
    let site: SiteNotificationsPresentation
    let onChange: (Bool) -> Void
    @Environment(\.palette) private var palette

    var body: some View {
      Toggle(isOn: Binding(get: { site.muted }, set: { onChange($0) })) {
        VStack(alignment: .leading, spacing: Spacing.step1) {
          Text(verbatim: site.muteLabel.string).role(Typography.bodyLg)
            .foregroundStyle(palette.textPrimary)
            .fixedSize(horizontal: false, vertical: true)
          Text(verbatim: site.muteHelper.string).role(Typography.helper)
            .foregroundStyle(palette.textHelper)
            .fixedSize(horizontal: false, vertical: true)
        }
      }
      .toggleStyle(.switch)
      .tint(palette.primary)
      .frame(minHeight: TouchTarget.control)
    }
  }
#endif
