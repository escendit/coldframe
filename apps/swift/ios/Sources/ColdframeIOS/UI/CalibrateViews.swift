#if canImport(SwiftUI)
  import ColdframeDesignTokens
  import Foundation
  import SwiftUI

  /// What Calibrate can ask of the core. Built over a `CalibrateService`; `.none` does nothing,
  /// for previews and render tests. The views never compute a step, a freshness or a percentage.
  @MainActor
  public struct CalibrateActions {
    /// Opens Calibrate for a Lot: the Lot's id and name.
    public var open: (String, String) -> Void
    public var close: () -> Void
    public var retry: () -> Void
    public var pick: (Int64) -> Void
    public var record: () -> Void

    public init(service: CalibrateService) {
      open = { service.open(lotId: $0, name: $1) }
      close = { service.close() }
      retry = { service.retry() }
      pick = { service.pick(readingSeq: $0) }
      record = { service.record() }
    }

    private init() {
      open = { _, _ in }
      close = {}
      retry = {}
      pick = { _ in }
      record = {}
    }

    public static let none = CalibrateActions()
  }

  /// Calibrate (Story 5.2, UX-DR66): a full-screen, two-step flow, dry then wet, then the
  /// confirmation. Each step waits for a Reading taken after it started: the waiting panel shows
  /// the last raw value and time and the hint to short-press the Node's setup button, and a fresh
  /// Reading enables Record dry / Record wet. "Recent Readings" is the alternative. The waiting
  /// text is never announced; a fresh Reading and the first percentage are, politely. On a paused
  /// Node nothing is waited for. The core decided everything shown; this draws it with catalogue
  /// words. Leaving keeps the dry point on the Server, so returning resumes at wet.
  public struct CalibrateView: View {
    let presentation: CalibratePresentation
    let siteName: String
    let actions: CalibrateActions
    let now: () -> Date
    let timeZone: TimeZone
    @AccessibilityFocusState private var titleFocused: Bool
    @Environment(\.palette) private var palette
    @Environment(\.locale) private var locale

    public init(
      presentation: CalibratePresentation, siteName: String,
      actions: CalibrateActions = .none, now: @escaping () -> Date = { Date() },
      timeZone: TimeZone = .current
    ) {
      self.presentation = presentation
      self.siteName = siteName
      self.actions = actions
      self.now = now
      self.timeZone = timeZone
    }

    public var body: some View {
      let context = CopyContext.catalogue(now: now(), timeZone: timeZone, locale: locale)
      ScrollView {
        VStack(alignment: .leading, spacing: Spacing.step6) {
          CalibrateLinkButton(text: presentation.backLabel.string, action: actions.close)
          content(context)
        }
        .padding(Spacing.gutterMobile)
        .frame(maxWidth: .infinity, alignment: .leading)
      }
      .background(palette.background)
      .onAppear { titleFocused = true }
      .onChange(of: presentation.step) { titleFocused = true }
      .onChange(of: presentation.announcement?.id) { announce(context) }
    }

    // A fresh Reading and the first percentage are posted once, politely; nothing else is.
    private func announce(_ context: CopyContext) {
      guard let text = presentation.spokenAnnouncement(context) else { return }
      AccessibilityNotification.Announcement(AttributedString(text)).post()
    }

    @ViewBuilder
    private func content(_ context: CopyContext) -> some View {
      switch presentation.surface {
      case .idle:
        EmptyView()
      case .loading(let name):
        title(Copy(.calibrateTitle, .text(name)).string)
        Text(verbatim: L10n.gardenLoading.string(name)).role(Typography.body)
          .foregroundStyle(palette.textSecondary)
          .frame(maxWidth: .infinity, minHeight: LotTile.minimumHeight, alignment: .topLeading)
          .padding(Spacing.tilePadding)
          .overlay { Rectangle().strokeBorder(palette.borderSubtle, lineWidth: 1) }
      case .failed(let name, let notice, let tryAgain):
        title(Copy(.calibrateTitle, .text(name)).string)
        InlineNotice(
          message: notice.message, subject: notice.takesSite ? siteName : nil,
          announcement: notice.announcement,
          action: tryAgain ? (label: L10n.noticeTryAgain, perform: actions.retry) : nil)
      case .paused(let name, let bySite, _):
        title(Copy(.calibrateTitle, .text(name)).string)
        // Nothing is waited for: Readings resume after the Pause ends.
        Text(verbatim: presentation.pausedMessage(bySite: bySite).string).role(Typography.bodyLg)
          .foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
      case .ready:
        ready(context)
      }
    }

    private func title(_ text: String) -> some View {
      Text(verbatim: text).role(Typography.headline).foregroundStyle(palette.textPrimary)
        .fixedSize(horizontal: false, vertical: true)
        .accessibilityAddTraits(.isHeader)
        .accessibilityFocused($titleFocused)
    }

    @ViewBuilder
    private func ready(_ context: CopyContext) -> some View {
      if let counter = presentation.step.counter {
        counter.text.role(Typography.statusLabel).foregroundStyle(palette.textHelper)
      }
      title(presentation.step == .confirm ? L10n.calibrateStepConfirm.string : presentation.title.string)
      if let notice = presentation.notice {
        InlineNotice(
          message: notice.message, subject: notice.takesSite ? siteName : nil,
          announcement: notice.announcement)
      }
      if presentation.step == .confirm {
        confirmation
      } else {
        step(context)
      }
    }

    @ViewBuilder
    private func step(_ context: CopyContext) -> some View {
      if let intro = presentation.step.intro {
        intro.text.role(Typography.bodyLg).foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
      }
      if let fresh = presentation.freshText(context) {
        Text(verbatim: fresh).role(Typography.body).foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
      } else if presentation.isWaiting {
        waiting(context)
      }
      if let label = presentation.recordLabel {
        PrimaryButton(label, isEnabled: presentation.isRecordEnabled, action: actions.record)
      }
      recent(context)
    }

    // The waiting panel: plain text, never announced.
    private func waiting(_ context: CopyContext) -> some View {
      VStack(alignment: .leading, spacing: Spacing.step3) {
        presentation.waitingTitle.text.role(Typography.section).foregroundStyle(palette.textPrimary)
        Text(verbatim: presentation.lastReadingText(context)).role(Typography.metaMono)
          .foregroundStyle(palette.textSecondary)
          .fixedSize(horizontal: false, vertical: true)
        presentation.hint.text.role(Typography.body).foregroundStyle(palette.textSecondary)
          .fixedSize(horizontal: false, vertical: true)
      }
      .padding(Spacing.tilePadding)
      .frame(maxWidth: .infinity, alignment: .leading)
      .overlay { Rectangle().strokeBorder(palette.borderSubtle, lineWidth: 1) }
    }

    @ViewBuilder
    private func recent(_ context: CopyContext) -> some View {
      VStack(alignment: .leading, spacing: Spacing.tileGap) {
        SectionHeading(title: .calibrateRecent)
        if presentation.readings.isEmpty {
          L10n.calibrateRecentEmpty.text.role(Typography.body)
            .foregroundStyle(palette.textSecondary)
        } else {
          ForEach(presentation.readings) { reading in
            let use = presentation.step.useLabel?.string ?? ""
            CandidateTileView(
              name: reading.text(context), signal: use, badge: nil,
              description: "\(reading.text(context)). \(use)", isSelected: reading.isCandidate
            ) { actions.pick(reading.readingSeq) }
          }
        }
      }
    }

    // Both raw values and "% appears with the next Reading", which becomes "Tomatoes reads ~40 %"
    // in place when the first calibrated Reading arrives. No percentage is drawn before that.
    private var confirmation: some View {
      VStack(alignment: .leading, spacing: Spacing.step4) {
        Text(verbatim: presentation.pointsText.string).role(Typography.bodyLg)
          .foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
        Text(verbatim: presentation.statusText.string).role(Typography.bodyLg)
          .foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
        PrimaryButton(.addNodeDone, variant: .secondary, action: actions.close)
      }
    }
  }

  /// A text-only ghost button for a label with arguments.
  struct CalibrateLinkButton: View {
    let text: String
    let action: () -> Void
    @Environment(\.palette) private var palette

    var body: some View {
      Button(action: action) {
        Text(verbatim: text)
          .role(Typography.button)
          .multilineTextAlignment(.leading)
          .fixedSize(horizontal: false, vertical: true)
          .padding(.horizontal, Spacing.step5)
          .padding(.vertical, Spacing.step4)
          .frame(minWidth: TouchTarget.control, minHeight: TouchTarget.control)
          .foregroundStyle(palette.primaryText)
          .contentShape(Rectangle())
      }
      .buttonStyle(.plain)
    }
  }
#endif
