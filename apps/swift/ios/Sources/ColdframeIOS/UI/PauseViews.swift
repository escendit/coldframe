#if canImport(SwiftUI)
  import ColdframeDesignTokens
  import Foundation
  import SwiftUI

  /// What a surface can ask of the core about Pause (Story 8.2): open the sheet, fill it, save
  /// it, and the one-tap Resume. Built over a `PauseService`; `.none` does nothing, for previews
  /// and render tests. The views never decide whether something is paused or who may pause.
  @MainActor
  public struct PauseActions {
    /// Pause in the Site menu: the sheet with Whole Site only.
    public var openForSite: () -> Void
    /// Pause on Lot detail or a Devices row: the sheet with This Device selected.
    public var openForDevice: (String) -> Void
    public var setScope: (PauseScopeKind) -> Void
    /// The picked calendar date, or nil for no end.
    public var setUntil: (PauseDay?) -> Void
    public var cancel: () -> Void
    /// The primary button, and Try again inside the sheet.
    public var save: () -> Void
    public var resumeSite: () -> Void
    public var resumeDevice: (String) -> Void
    /// Try again after a Resume that did not happen.
    public var retry: () -> Void

    public init(service: PauseService) {
      openForSite = { service.openForSite() }
      openForDevice = { service.openForDevice($0) }
      setScope = { service.setScope($0) }
      setUntil = { service.setUntil($0) }
      cancel = { service.cancel() }
      save = { service.save() }
      resumeSite = { service.resumeSite() }
      resumeDevice = { service.resumeDevice($0) }
      retry = { service.retry() }
    }

    private init() {
      openForSite = {}
      openForDevice = { _ in }
      setScope = { _ in }
      setUntil = { _ in }
      cancel = {}
      save = {}
      resumeSite = {}
      resumeDevice = { _ in }
      retry = {}
    }

    public static let none = PauseActions()
  }

  extension View {
    /// The Pause sheet of a surface (UX-DR46): a native sheet that is open while the core has
    /// one open. It closes when the core closes it: Cancel, a swipe down, or a Pause the Server
    /// accepted. `blocked` keeps it away while the surface shows a sheet of its own: one modal
    /// level at a time. `unreachable` is the "can't reach" copy of the surface.
    @MainActor
    func pauseSheet(
      _ pause: PausePresentation, blocked: Bool = false, actions: PauseActions,
      unreachable: L10n = .noticeUnreachable, now: @escaping () -> Date, timeZone: TimeZone
    ) -> some View {
      sheet(
        item: Binding(
          get: { blocked ? nil : pause.sheet },
          // A swipe down is Cancel. The core ignores it for a sheet it already closed.
          set: { if $0 == nil { actions.cancel() } })
      ) { opened in
        PauseSheet(
          form: opened, actions: actions, unreachable: unreachable, now: now, timeZone: timeZone)
      }
    }
  }

  /// The Pause sheet's content (UX-DR46), also rendered on its own in tests: the scope as a
  /// Segmented choice (Whole Site alone from the Site menu; This Device and Whole Site from a
  /// Device), the optional "Until" date, and Cancel beside a primary button that names the
  /// result ("Pause Home garden", "Pause 7C19"). Everything in it is the core's.
  ///
  /// The date is picked in the native calendar, which opens inside the sheet and never on top of
  /// it. What is sent is the calendar date that was picked. Which dates are allowed is the
  /// Server's rule, so none is greyed out here. A date the Server refused marks the field
  /// ("Choose a date after today."); any other refusal is an Inline notice above the buttons,
  /// and the sheet keeps what was entered. While the request runs nothing can be changed and the
  /// sheet cannot be swiped away, so an answer that keeps it open finds it open.
  struct PauseSheet: View {
    let form: PauseSheetPresentation
    let actions: PauseActions
    let unreachable: L10n
    let now: () -> Date
    let timeZone: TimeZone
    @State private var calendarOpen: Bool
    @Environment(\.palette) private var palette
    @Environment(\.locale) private var locale

    /// `calendarOpen` starts with the calendar showing.
    init(
      form: PauseSheetPresentation, actions: PauseActions = .none,
      unreachable: L10n = .noticeUnreachable, now: @escaping () -> Date = { Date() },
      timeZone: TimeZone = .current, calendarOpen: Bool = false
    ) {
      self.form = form
      self.actions = actions
      self.unreachable = unreachable
      self.now = now
      self.timeZone = timeZone
      _calendarOpen = State(initialValue: calendarOpen)
    }

    var body: some View {
      let context = CopyContext.catalogue(now: now(), timeZone: timeZone, locale: locale)
      ScrollView {
        VStack(alignment: .leading, spacing: Spacing.step6) {
          L10n.pauseTitle.text.role(Typography.section).foregroundStyle(palette.textPrimary)
            .accessibilityAddTraits(.isHeader)
          SegmentedChoice(
            label: .pauseScope, segments: form.segments, onSelect: actions.setScope
          )
          .disabled(!form.isEnabled)
          untilField(context)
          if let notice = form.notice {
            InlineNotice(
              message: notice.message(unreachable: unreachable),
              subject: notice.takesSiteName ? form.siteName : nil, announcement: .assertive,
              action: form.noticeTryAgain
                ? (label: L10n.noticeTryAgain, perform: actions.save) : nil)
          }
          HStack(spacing: Spacing.step4) {
            PrimaryButton(
              .modalCancel, variant: .ghost, isEnabled: form.isEnabled, action: actions.cancel)
            primary
          }
        }
        .padding(Spacing.gutterMobile)
        .frame(maxWidth: .infinity, alignment: .leading)
      }
      .background(palette.background)
      .interactiveDismissDisabled(form.working)
    }

    /// The DS primary Button with a label that names what is paused; "Pausing…" in place while
    /// the request runs.
    private var primary: some View {
      Button(action: actions.save) {
        Text(verbatim: form.primaryLabel.string)
          .role(Typography.button)
          .multilineTextAlignment(.leading)
          .fixedSize(horizontal: false, vertical: true)
          .frame(maxWidth: .infinity, alignment: .leading)
          .padding(.horizontal, Spacing.step5)
          .padding(.vertical, Spacing.step4)
          .frame(minWidth: TouchTarget.control, minHeight: TouchTarget.control)
          .foregroundStyle(palette.inkOnBright)
          .background(palette.primary)
          .contentShape(Rectangle())
      }
      .buttonStyle(.plain)
      .disabled(!form.isEnabled)
    }

    /// "Until": a field that names the picked date, or "No end date", drawn as a DS TextInput
    /// is, with the `support-error` border and the reason when the Server refused the date. A
    /// tap opens the calendar below it, and picks tomorrow when the field was empty; "Clear
    /// date" empties the field. One element for VoiceOver: "Until, 1 Mar 2027", with the helper
    /// or the reason as its hint.
    @ViewBuilder
    private func untilField(_ context: CopyContext) -> some View {
      VStack(alignment: .leading, spacing: Spacing.step3) {
        Button {
          // The calendar always marks a day: opened on an empty field it picks tomorrow, so the
          // field and the calendar name the same day.
          if !calendarOpen, let day = form.dayOnOpening(now: now(), timeZone: timeZone) {
            actions.setUntil(day)
          }
          calendarOpen.toggle()
        } label: {
          VStack(alignment: .leading, spacing: Spacing.step3) {
            L10n.pauseUntil.text.role(Typography.body).foregroundStyle(palette.textSecondary)
            HStack(spacing: Spacing.step3) {
              Text(verbatim: form.untilText(context)).role(Typography.bodyLg)
                .foregroundStyle(form.until == nil ? palette.textHelper : palette.textPrimary)
                .multilineTextAlignment(.leading)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)
              if form.dateRefused {
                CarbonIconShape(.errorFilled).fill(palette.supportErrorText)
                  .frame(width: 16, height: 16)
              }
            }
            .padding(.horizontal, Spacing.step5)
            .frame(minHeight: TouchTarget.control)
            .background(palette.field01)
            .overlay(alignment: .bottom) {
              if !form.dateRefused { Rectangle().fill(palette.borderStrong).frame(height: 1) }
            }
            .overlay {
              if form.dateRefused {
                Rectangle().strokeBorder(palette.supportError, lineWidth: 2)
              }
            }
            form.untilHelper.text.role(Typography.helper)
              .foregroundStyle(form.dateRefused ? palette.supportErrorText : palette.textHelper)
              .multilineTextAlignment(.leading)
              .fixedSize(horizontal: false, vertical: true)
          }
          .frame(maxWidth: .infinity, alignment: .leading)
          .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(!form.isEnabled)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(Text(verbatim: form.untilSpoken(context)))
        .accessibilityHint(form.untilHelper.text)
        .accessibilityAddTraits(.isButton)
        if form.showsClearDate {
          PrimaryButton(.pauseUntilClear, variant: .ghost, isEnabled: form.isEnabled) {
            calendarOpen = false
            actions.setUntil(nil)
          }
        }
        if calendarOpen && form.isEnabled {
          calendar
        }
      }
    }

    /// The native calendar. It shows and reads the day in one calendar and zone, so the picked
    /// day is the day that is sent: no zone of the phone or of the User moves it.
    private var calendar: some View {
      DatePicker(
        selection: Binding(
          get: { form.pickerDate(now: now(), timeZone: timeZone) },
          set: { actions.setUntil(PauseDay($0, timeZone: timeZone)) }),
        displayedComponents: .date
      ) {
        L10n.pauseUntil.text
      }
      .datePickerStyle(.graphical)
      .labelsHidden()
      .environment(\.calendar, PauseDay.calendar(timeZone: timeZone))
      .environment(\.timeZone, timeZone)
      .tint(palette.primaryText)
    }
  }

  /// A Resume that did not happen, as an Inline notice on the surface it was asked on: the
  /// surface's "can't reach" copy with Try again, or "You can't change this on ‹Site›…" after a
  /// 403.
  struct PauseFailureNotice: View {
    let failure: PauseFailurePresentation
    let actions: PauseActions
    var unreachable: L10n = .noticeUnreachable

    var body: some View {
      InlineNotice(
        message: failure.message(unreachable: unreachable), subject: failure.subject,
        announcement: .assertive,
        action: failure.tryAgain ? (label: L10n.noticeTryAgain, perform: actions.retry) : nil)
    }
  }

  /// A paused Device or Site (UX-DR70, UX-DR83): the `pause--outline` icon beside its words.
  /// Purple and the pause icon mean paused, and nothing else.
  struct PausedMark<Label: View>: View {
    var iconSize: Double = 16
    var alignment: VerticalAlignment = .center
    @ViewBuilder let label: () -> Label
    @Environment(\.palette) private var palette

    var body: some View {
      HStack(alignment: alignment, spacing: Spacing.step2) {
        CarbonIconShape(.pauseOutline).fill(palette.color(ColorTokens.statusPausedInk))
          .frame(width: iconSize, height: iconSize)
          .accessibilityHidden(true)
        label()
      }
    }
  }
#endif
