#if canImport(SwiftUI)
  import ColdframeDesignTokens
  import Foundation
  import SwiftUI

  /// What Thresholds can ask of the core. Built over a `ThresholdsService`; `.none` does nothing,
  /// for previews and render tests. The views never validate, snap or save by themselves.
  @MainActor
  public struct ThresholdsActions {
    /// Opens Thresholds for a Lot: the Lot's id and name, and the Sensor cell it came from (or empty).
    public var open: (String, String, String) -> Void
    public var close: () -> Void
    public var retry: () -> Void
    public var setLowText: (String, String) -> Void
    public var setHighText: (String, String) -> Void
    public var setLow: (String, Double) -> Void
    public var setHigh: (String, Double) -> Void
    public var addHigh: (String) -> Void
    public var clearHigh: (String) -> Void
    public var turnOnAlerts: (String) -> Void
    public var turnOffAlerts: (String) -> Void
    public var save: () -> Void

    public init(service: ThresholdsService) {
      open = { service.open(lotId: $0, name: $1, sensorId: $2) }
      close = { service.close() }
      retry = { service.retry() }
      setLowText = { service.setLowText(sensorId: $0, text: $1) }
      setHighText = { service.setHighText(sensorId: $0, text: $1) }
      setLow = { service.setLow(sensorId: $0, value: $1) }
      setHigh = { service.setHigh(sensorId: $0, value: $1) }
      addHigh = { service.addHigh(sensorId: $0) }
      clearHigh = { service.clearHigh(sensorId: $0) }
      turnOnAlerts = { service.turnOnAlerts(sensorId: $0) }
      turnOffAlerts = { service.turnOffAlerts(sensorId: $0) }
      save = { service.save() }
    }

    private init() {
      open = { _, _, _ in }
      close = {}
      retry = {}
      setLowText = { _, _ in }
      setHighText = { _, _ in }
      setLow = { _, _ in }
      setHigh = { _, _ in }
      addHigh = { _ in }
      clearHigh = { _ in }
      turnOnAlerts = { _ in }
      turnOffAlerts = { _ in }
      save = {}
    }

    public static let none = ThresholdsActions()
  }

  /// Thresholds (Story 5.4, UX-DR45, UX-DR69, UX-DR84, UX-DR91): a modal with Cancel and Save and a
  /// Threshold column per Sensor. A Member sees the same columns read-only, with no edit control and
  /// no Save (hidden, not disabled). "Low must stay below high." appears inline and Save is disabled
  /// while the core says a column is invalid. The core decided everything shown; this draws it with
  /// catalogue words. Once saved the screen closes itself and Lot detail reads again.
  public struct ThresholdsView: View {
    let presentation: ThresholdsPresentation
    let siteName: String
    let actions: ThresholdsActions
    let now: () -> Date
    let timeZone: TimeZone
    @AccessibilityFocusState private var titleFocused: Bool
    @Environment(\.palette) private var palette
    @Environment(\.locale) private var locale

    public init(
      presentation: ThresholdsPresentation, siteName: String,
      actions: ThresholdsActions = .none, now: @escaping () -> Date = { Date() },
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
          bar
          content(context)
        }
        .padding(Spacing.gutterMobile)
        .frame(maxWidth: .infinity, alignment: .leading)
      }
      .background(palette.background)
      .onAppear { titleFocused = true }
      .onChange(of: presentation.isSaved) { _, saved in if saved { actions.close() } }
    }

    // Cancel and Save (UX-DR69). Save is absent for a Role that cannot save.
    private var bar: some View {
      ViewThatFits(in: .horizontal) {
        HStack(spacing: Spacing.step4) { barItems }
        VStack(alignment: .leading, spacing: Spacing.step3) { barItems }
      }
    }

    @ViewBuilder
    private var barItems: some View {
      CalibrateLinkButton(text: presentation.closeLabel.string, action: actions.close)
      Spacer(minLength: 0)
      if let save = presentation.saveLabel {
        PrimaryButton(save, isEnabled: presentation.isSaveEnabled, action: actions.save)
          .fixedSize(horizontal: true, vertical: false)
      }
    }

    @ViewBuilder
    private func content(_ context: CopyContext) -> some View {
      switch presentation.surface {
      case .idle:
        EmptyView()
      case .loading(let name):
        title(Copy(.thresholdsTitle, .text(name)).string)
        Text(verbatim: L10n.gardenLoading.string(name)).role(Typography.body)
          .foregroundStyle(palette.textSecondary)
          .frame(maxWidth: .infinity, minHeight: LotTile.minimumHeight, alignment: .topLeading)
          .padding(Spacing.tilePadding)
          .overlay { Rectangle().strokeBorder(palette.borderSubtle, lineWidth: 1) }
      case .failed(let name, let notice, let tryAgain):
        title(Copy(.thresholdsTitle, .text(name)).string)
        InlineNotice(
          message: notice.message, subject: notice.takesSite ? siteName : nil,
          announcement: notice.announcement,
          action: tryAgain ? (label: L10n.noticeTryAgain, perform: actions.retry) : nil)
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
      title(presentation.title.string)
      if let readOnly = presentation.readOnlyNotice {
        readOnly.text.role(Typography.body).foregroundStyle(palette.textSecondary)
          .fixedSize(horizontal: false, vertical: true)
      }
      // A save that did not happen says so and keeps every edit.
      if let notice = presentation.saveNotice {
        InlineNotice(
          message: notice.message, subject: notice.takesSite ? siteName : nil,
          announcement: notice.announcement)
      }
      ForEach(presentation.columns) { column in
        ThresholdColumnView(
          column: column, presentation: presentation, actions: actions, context: context)
      }
    }
  }

  /// One Threshold column (UX-DR45): a vertical track on `layer-01`, the 2 pt `primary-text` low
  /// line, the current Reading marker and the dashed "no high" marker, with the values to the
  /// right; drag the lines or type the numbers. Alerts are turned on and off here, and "Add high"
  /// and "Clear high" move the high.
  struct ThresholdColumnView: View {
    static let trackHeight: Double = 168
    static let trackWidth: Double = 56

    let column: ThresholdColumnPresentation
    let presentation: ThresholdsPresentation
    let actions: ThresholdsActions
    let context: CopyContext
    @Environment(\.palette) private var palette
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    var body: some View {
      VStack(alignment: .leading, spacing: Spacing.step4) {
        Text(verbatim: context.text(column.title)).role(Typography.section)
          .foregroundStyle(palette.textPrimary)
          .accessibilityAddTraits(.isHeader)
        layout
        if presentation.canEdit { controls }
      }
      .padding(Spacing.tilePadding)
      .frame(maxWidth: .infinity, alignment: .leading)
      .background(palette.layer01)
      .overlay { Rectangle().strokeBorder(palette.borderSubtle, lineWidth: 1) }
    }

    @ViewBuilder
    private var layout: some View {
      if dynamicTypeSize.isAccessibilitySize {
        VStack(alignment: .leading, spacing: Spacing.step4) {
          track
          values
        }
      } else {
        HStack(alignment: .top, spacing: Spacing.step5) {
          track
          values
        }
      }
    }

    // The track: low line, high line or the dashed "no high" marker, and the Reading marker.
    private var track: some View {
      let height = Self.trackHeight
      return ZStack(alignment: .topLeading) {
        Rectangle().fill(palette.background)
        Rectangle().strokeBorder(palette.borderSubtle, lineWidth: 1)
        if let low = column.lowFraction {
          Rectangle().fill(palette.primaryText).frame(height: 2)
            .offset(y: y(low, height) - 1)
        }
        if let high = column.highFraction {
          Rectangle().fill(palette.borderStrong).frame(height: 2)
            .offset(y: y(high, height) - 1)
        } else {
          // Dashed "no high" marker at the top of the track.
          Path { path in
            path.move(to: CGPoint(x: 0, y: 1))
            path.addLine(to: CGPoint(x: Self.trackWidth, y: 1))
          }
          .stroke(palette.borderStrong, style: StrokeStyle(lineWidth: 1, dash: [4, 3]))
        }
        if let current = column.currentFraction {
          // The current Reading: a filled marker, not colour alone.
          Path { path in
            let at = y(current, height)
            path.move(to: CGPoint(x: Self.trackWidth - 12, y: at))
            path.addLine(to: CGPoint(x: Self.trackWidth, y: at - 6))
            path.addLine(to: CGPoint(x: Self.trackWidth, y: at + 6))
            path.closeSubpath()
          }
          .fill(palette.textPrimary)
        }
      }
      .frame(width: Self.trackWidth, height: height)
      .contentShape(Rectangle())
      .gesture(
        DragGesture(minimumDistance: 0)
          .onChanged { value in drag(to: Double(value.location.y), height: height) }
      )
      .accessibilityElement(children: .ignore)
      .accessibilityLabel(Text(verbatim: column.spokenText(context)))
    }

    // 0 is the bottom of the track.
    private func y(_ fraction: Double, _ height: Double) -> Double { height * (1 - fraction) }

    // Dragging moves the nearer line; only an editor can, and only a percentage has a fixed range.
    private func drag(to y: Double, height: Double) {
      guard presentation.canEdit, column.draggable, height > 0 else { return }
      let fraction = min(1, max(0, 1 - y / height))
      let value = column.trackMin + fraction * (column.trackMax - column.trackMin)
      if let high = column.highFraction, let low = column.lowFraction {
        if abs(fraction - high) < abs(fraction - low) {
          actions.setHigh(column.sensorId, value)
        } else {
          actions.setLow(column.sensorId, value)
        }
      } else if column.alerting {
        actions.setLow(column.sensorId, value)
      }
    }

    // Values to the right of the track, and the typed fields for an editor.
    private var values: some View {
      VStack(alignment: .leading, spacing: Spacing.step3) {
        readout(.thresholdsLow, column.lowText(context))
        readout(.thresholdsHigh, column.highText(context))
        Text(verbatim: column.currentText(context)).role(Typography.metaMono)
          .foregroundStyle(palette.textSecondary)
          .fixedSize(horizontal: false, vertical: true)
        if presentation.canEdit {
          if !column.alerting, let error = column.inlineError {
            Text(verbatim: error.string).role(Typography.helper)
              .foregroundStyle(palette.supportErrorText)
              .fixedSize(horizontal: false, vertical: true)
          }
          if column.alerting {
            ThresholdField(
              label: L10n.thresholdsLowFor.string(context.text(column.title)), value: column.low,
              error: column.inlineError.map { $0.string },
              onCommit: { actions.setLowText(column.sensorId, $0) })
          }
          if column.hasHigh {
            ThresholdField(
              label: L10n.thresholdsHighFor.string(context.text(column.title)), value: column.high,
              error: nil, onCommit: { actions.setHighText(column.sensorId, $0) })
          }
        }
      }
      .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func readout(_ label: L10n, _ value: String) -> some View {
      HStack(alignment: .firstTextBaseline, spacing: Spacing.step3) {
        label.text.role(Typography.helper).foregroundStyle(palette.textSecondary)
        Text(verbatim: value).role(Typography.title).foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
      }
      .accessibilityElement(children: .combine)
    }

    // Turn on Alerts offers the Server's proposed low for a Sensor without a default; Add high / Clear high.
    @ViewBuilder
    private var controls: some View {
      VStack(alignment: .leading, spacing: Spacing.step2) {
        if let toggle = presentation.alertsToggle(column) {
          PrimaryButton(toggle, variant: .ghost) {
            if column.alerting {
              actions.turnOffAlerts(column.sensorId)
            } else {
              actions.turnOnAlerts(column.sensorId)
            }
          }
        }
        if presentation.showsAddHigh(column) {
          PrimaryButton(.thresholdsAddHigh, variant: .ghost) { actions.addHigh(column.sensorId) }
        }
        if presentation.showsClearHigh(column) {
          PrimaryButton(.thresholdsClearHigh, variant: .ghost) {
            actions.clearHigh(column.sensorId)
          }
        }
      }
    }
  }

  /// A typed number. The text is the person's until they finish (return or leaving the field), then
  /// the core snaps it and the field shows what the core kept. An error replaces nothing: it sits
  /// under the field with the 2 pt `support-error` border and is read with it (UX-DR91).
  struct ThresholdField: View {
    let label: String
    let value: String
    let error: String?
    let onCommit: (String) -> Void
    @State private var text = ""
    @FocusState private var focused: Bool
    @Environment(\.palette) private var palette

    var body: some View {
      VStack(alignment: .leading, spacing: Spacing.step2) {
        Text(verbatim: label).role(Typography.helper).foregroundStyle(palette.textSecondary)
          .fixedSize(horizontal: false, vertical: true)
        TextField(label, text: $text)
          .role(Typography.bodyLg)
          .foregroundStyle(palette.textPrimary)
          #if os(iOS)
            .keyboardType(.numbersAndPunctuation)
          #endif
          .focused($focused)
          .onSubmit(commit)
          .padding(.horizontal, Spacing.step5)
          .frame(minHeight: TouchTarget.control)
          .background(palette.field01)
          .overlay(alignment: .bottom) {
            if error == nil { Rectangle().fill(palette.borderStrong).frame(height: 1) }
          }
          .overlay {
            if error != nil { Rectangle().strokeBorder(palette.supportError, lineWidth: 2) }
          }
          .accessibilityHint(error ?? "")
        if let error {
          HStack(alignment: .top, spacing: Spacing.step2) {
            CarbonIconShape(.errorFilled).fill(palette.supportErrorText).frame(
              width: 16, height: 16
            )
            .accessibilityHidden(true)
            Text(verbatim: error).role(Typography.helper).foregroundStyle(palette.supportErrorText)
              .fixedSize(horizontal: false, vertical: true)
          }
        }
      }
      .onAppear { text = value }
      .onChange(of: value) { _, new in if !focused { text = new } }
      .onChange(of: focused) { _, now in
        if !now {
          commit()
          text = value
        }
      }
    }

    private func commit() { onCommit(text) }
  }
#endif
