#if canImport(SwiftUI)
  import ColdframeDesignTokens
  import Foundation
  import SwiftUI

  /// What Lot detail can ask of the core. Built over a `LotDetailService`; `.none` does nothing,
  /// for previews and render tests. The views never compute a status, a unit or an aggregate.
  @MainActor
  public struct LotDetailActions {
    public var open: (String, String) -> Void
    public var close: () -> Void
    public var refresh: () -> Void
    public var tick: () -> Void
    public var pick: (SensorQuantityKind) -> Void

    public init(service: LotDetailService) {
      open = { service.open(lotId: $0, name: $1) }
      close = { service.close() }
      refresh = { service.refresh() }
      tick = { service.tick() }
      pick = { service.pick($0) }
    }

    private init() {
      open = { _, _ in }
      close = {}
      refresh = {}
      tick = {}
      pick = { _ in }
    }

    public static let none = LotDetailActions()
  }

  /// A Lot opened from a tile: what the destination needs before the Server answers.
  struct OpenedLot: Hashable {
    let id: String
    let name: String
  }

  /// Lot detail (UX-DR63): the full-width hero, a 3-up row of Sensor cells, the 30-day History
  /// chart with its Sensor picker, then a 2-up row of Device cells; the rows go 1-up from
  /// Accessibility 1. The core decided everything shown; this draws it with catalogue words.
  /// Calibrate and Set Thresholds show for Administrators and Owners where the core says so; a
  /// Member sees View Thresholds, read-only (UX-DR84). Pause has no destination before Epic 8. Stale mode shows the stale header, the hero without a value, and no Sensor or Device
  /// cell. A *no Node* Lot is the empty detail. Pull to refresh reads the Lot again; a one-minute
  /// tick moves the stale age and announces nothing.
  public struct LotDetailView: View {
    let presentation: LotDetailPresentation
    let siteName: String
    let actions: LotDetailActions
    let onAddNode: (String) -> Void
    let onCalibrate: (String, String) -> Void
    /// Opens Thresholds: the Lot's id and name, and the Sensor cell it came from (or empty).
    let onSetThresholds: (String, String, String) -> Void
    let onOpenDevices: () -> Void
    let now: () -> Date
    let timeZone: TimeZone
    @Environment(\.palette) private var palette
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize
    @Environment(\.locale) private var locale

    public init(
      presentation: LotDetailPresentation, siteName: String,
      actions: LotDetailActions = .none, onAddNode: @escaping (String) -> Void = { _ in },
      onCalibrate: @escaping (String, String) -> Void = { _, _ in },
      onSetThresholds: @escaping (String, String, String) -> Void = { _, _, _ in },
      onOpenDevices: @escaping () -> Void = {}, now: @escaping () -> Date = { Date() },
      timeZone: TimeZone = .current
    ) {
      self.presentation = presentation
      self.siteName = siteName
      self.actions = actions
      self.onAddNode = onAddNode
      self.onCalibrate = onCalibrate
      self.onSetThresholds = onSetThresholds
      self.onOpenDevices = onOpenDevices
      self.now = now
      self.timeZone = timeZone
    }

    private var accessibilitySize: Bool { dynamicTypeSize.isAccessibilitySize }

    public var body: some View {
      let context = CopyContext.catalogue(now: now(), timeZone: timeZone, locale: locale)
      ScrollView {
        VStack(alignment: .leading, spacing: Spacing.step6) {
          content(context)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(Spacing.gutterMobile)
      }
      .background(palette.background)
      .refreshable { [actions] in
        await MainActor.run { actions.refresh() }
      }
      .task { [actions] in
        // The only timer of Lot detail: once a minute the core's snapshot is read again.
        while !Task.isCancelled {
          try? await Task.sleep(for: .seconds(60))
          if !Task.isCancelled {
            await MainActor.run { actions.tick() }
          }
        }
      }
    }

    @ViewBuilder
    private func content(_ context: CopyContext) -> some View {
      switch presentation.surface {
      case .idle:
        EmptyView()
      case .loading(let name):
        LotDetailTitle(name: name)
        // Nothing to read yet: the name and an outline, no value.
        Text(verbatim: L10n.gardenLoading.string(name)).role(Typography.body)
          .foregroundStyle(palette.textSecondary)
          .frame(maxWidth: .infinity, minHeight: LotTile.minimumHeight, alignment: .topLeading)
          .padding(Spacing.tilePadding)
          .overlay { Rectangle().strokeBorder(palette.borderSubtle, lineWidth: 1) }
      case .failed(let name, let notice):
        LotDetailTitle(name: name)
        InlineNotice(
          message: notice.message, subject: notice.takesSite ? siteName : nil,
          action: notice.offersTryAgain
            ? (label: L10n.noticeTryAgain, perform: actions.refresh) : nil
        )
      case .ready:
        ready(context)
      }
    }

    @ViewBuilder
    private func ready(_ context: CopyContext) -> some View {
      if let header = presentation.staleHeader(siteName: siteName) {
        StaleHeader(stale: header, context: context)
      }
      if let hero = presentation.hero {
        LotDetailHero(hero: hero, context: context)
      }
      if presentation.noNode {
        noNode
      } else {
        // Calibrate is hidden, not disabled, unless the core says this Role and Lot may.
        if presentation.canCalibrate, let lotId = presentation.lotId {
          PrimaryButton(.calibrateAction, variant: .secondary) {
            onCalibrate(lotId, presentation.hero?.name ?? "")
          }
        }
        // Thresholds: Set for an editor, View (read-only) for a Member; hidden, never disabled.
        if let action = presentation.thresholdsAction, let lotId = presentation.lotId {
          PrimaryButton(action, variant: .secondary) {
            onSetThresholds(lotId, presentation.hero?.name ?? "", "")
          }
        }
        if let sensors = presentation.sensors {
          sensorCells(sensors, context)
        }
        history(context)
        if let device = presentation.device {
          deviceCells(device, context)
        }
      }
    }

    private var noNode: some View {
      VStack(alignment: .leading, spacing: Spacing.step4) {
        L10n.lotDetailNoNode.text.role(Typography.bodyLg).foregroundStyle(palette.textSecondary)
        if presentation.canAddNode, let lotId = presentation.lotId {
          PrimaryButton(.devicesAddNode, variant: .secondary) { onAddNode(lotId) }
        }
      }
    }

    private func columns(_ count: Int) -> [GridItem] {
      Array(
        repeating: GridItem(.flexible(), spacing: Spacing.tileGap, alignment: .top), count: count)
    }

    private func sensorCells(_ cells: [SensorCellPresentation], _ context: CopyContext)
      -> some View
    {
      VStack(alignment: .leading, spacing: Spacing.tileGap) {
        SectionHeading(title: .lotDetailSensors)
        if cells.isEmpty {
          L10n.lotDetailNoReadings.text.role(Typography.body)
            .foregroundStyle(palette.textSecondary)
        } else {
          LazyVGrid(
            columns: columns(
              LotDetailPresentation.sensorColumns(accessibilitySize: accessibilitySize)),
            spacing: Spacing.tileGap
          ) {
            ForEach(cells) { cell in
              DetailCell(
                label: cell.label.string, value: cell.valueText(context),
                meta: cell.timeText(context), spoken: cell.spokenText(context),
                onTap: thresholdsTap(for: cell))
            }
          }
        }
      }
    }

    // A Sensor cell is a button only where the core offers Thresholds (UX-DR84).
    private func thresholdsTap(for cell: SensorCellPresentation) -> (() -> Void)? {
      guard let sensorId = presentation.thresholdsSensorId(for: cell),
        let lotId = presentation.lotId
      else { return nil }
      let name = presentation.hero?.name ?? ""
      return { onSetThresholds(lotId, name, sensorId) }
    }

    private func deviceCells(_ device: DeviceCellsPresentation, _ context: CopyContext)
      -> some View
    {
      VStack(alignment: .leading, spacing: Spacing.tileGap) {
        Text(verbatim: L10n.lotDetailNode.string(device.nodeId)).role(Typography.metaMono)
          .foregroundStyle(palette.textSecondary)
          .accessibilityAddTraits(.isHeader)
        LazyVGrid(
          columns: columns(
            LotDetailPresentation.deviceColumns(accessibilitySize: accessibilitySize)),
          spacing: Spacing.tileGap
        ) {
          DetailCell(
            label: L10n.lotDetailBattery.string, value: device.batteryText(context),
            meta: device.chargingText(context),
            spoken: [
              L10n.lotDetailBattery.string, device.batteryText(context),
              device.chargingText(context),
            ].compactMap { $0 }.joined(separator: ", "), icon: device.batteryIcon,
            onTap: onOpenDevices)
          DetailCell(
            label: L10n.lotDetailLastSeen.string, value: device.lastSeenText(context),
            meta: device.cadence.string,
            spoken: [
              L10n.lotDetailLastSeen.string, device.lastSeenText(context), device.cadence.string,
            ].joined(separator: ", "), onTap: onOpenDevices)
        }
      }
    }

    private func history(_ context: CopyContext) -> some View {
      VStack(alignment: .leading, spacing: Spacing.step4) {
        SectionHeading(title: .lotDetailHistory)
        if presentation.showsPicker {
          SegmentedChoice(
            label: .lotDetailPicker, segments: presentation.pickerSegments, onSelect: actions.pick)
        }
        if let chart = presentation.chart {
          HistoryChartView(chart: chart, context: context).id(chart.quantity)
        } else if presentation.historyUnavailable {
          L10n.lotDetailChartUnavailable.text.role(Typography.body)
            .foregroundStyle(palette.textSecondary)
        } else {
          L10n.lotDetailChartNoReadings.text.role(Typography.body)
            .foregroundStyle(palette.textSecondary)
        }
      }
    }
  }

  /// The Lot's name as the heading of a detail that has nothing else to show yet.
  struct LotDetailTitle: View {
    let name: String
    @Environment(\.palette) private var palette

    var body: some View {
      Text(verbatim: name).role(Typography.title).foregroundStyle(palette.textPrimary)
        .fixedSize(horizontal: false, vertical: true)
        .accessibilityAddTraits(.isHeader)
    }
  }

  struct SectionHeading: View {
    let title: L10n
    @Environment(\.palette) private var palette

    var body: some View {
      title.text.role(Typography.section).foregroundStyle(palette.textPrimary)
        .accessibilityAddTraits(.isHeader)
    }
  }

  /// The hero (UX-DR27, UX-DR78): the tile's treatment of the status on a full-width block.
  /// One accessibility element with the complete spoken label.
  struct LotDetailHero: View {
    let hero: LotDetailHeroPresentation
    let context: CopyContext
    @Environment(\.palette) private var palette

    var body: some View {
      let variant = hero.variant
      let ink = palette.color(variant.ink)
      let labelInk = palette.color(variant.labelInk)
      let foot = palette.color(variant.footInk)
      VStack(alignment: .leading, spacing: Spacing.step4) {
        Text(verbatim: hero.name).role(Typography.title).foregroundStyle(ink)
          .fixedSize(horizontal: false, vertical: true)
          .plate(variant.isHatched, palette)
        HStack(spacing: Spacing.step2) {
          CarbonIconShape(hero.icon).fill(labelInk).frame(width: 16, height: 16)
          hero.statusLabel.text.role(Typography.statusLabel).foregroundStyle(labelInk)
            .fixedSize(horizontal: false, vertical: true)
          if let since = hero.since(context) {
            Text(verbatim: since).role(Typography.metaMono).foregroundStyle(foot)
              .fixedSize(horizontal: false, vertical: true)
          }
        }
        .plate(variant.isHatched, palette)
        if let value = hero.valueText(context) {
          Text(verbatim: value).role(Typography.heroValue).foregroundStyle(ink)
            .fixedSize(horizontal: false, vertical: true)
            .plate(variant.isHatched, palette)
        }
        ForEach([hero.readingText(context), hero.lowText(context)].compactMap { $0 }, id: \.self) {
          Text(verbatim: $0).role(Typography.metaMono).foregroundStyle(foot)
            .fixedSize(horizontal: false, vertical: true)
            .plate(variant.isHatched, palette)
        }
        ForEach(
          [hero.noteText(context), hero.resumeSiteText(context)].compactMap { $0 }, id: \.self
        ) {
          Text(verbatim: $0).role(Typography.body).foregroundStyle(ink)
            .fixedSize(horizontal: false, vertical: true)
            .plate(variant.isHatched, palette)
        }
      }
      .padding(Spacing.tilePadding)
      .frame(maxWidth: .infinity, alignment: .topLeading)
      .background {
        if variant.isHatched {
          Hatch(palette: palette)
        } else if let background = variant.background {
          palette.color(background)
        }
      }
      .overlay { LotTileOutline(border: variant.border) }
      .accessibilityElement(children: .ignore)
      .accessibilityLabel(Text(verbatim: hero.spokenText(context)))
    }
  }

  /// A Sensor or Device cell (UX-DR28, UX-DR29): `layer-01` with a 1 pt `border-subtle` outline,
  /// a `helper` label over a `title` value over `helper` meta. One element; a button when it has
  /// a destination.
  struct DetailCell: View {
    let label: String
    let value: String
    let meta: String?
    let spoken: String
    var icon: CarbonIcon?
    var onTap: (() -> Void)?
    @Environment(\.palette) private var palette

    var body: some View {
      if let onTap {
        Button(action: onTap) { cell.contentShape(Rectangle()) }
          .buttonStyle(.plain)
          .accessibilityAddTraits(.isButton)
      } else {
        cell
      }
    }

    private var cell: some View {
      VStack(alignment: .leading, spacing: Spacing.step2) {
        Text(verbatim: label).role(Typography.helper).foregroundStyle(palette.textSecondary)
          .fixedSize(horizontal: false, vertical: true)
        HStack(spacing: Spacing.step2) {
          if let icon {
            CarbonIconShape(icon).fill(palette.textPrimary).frame(width: 24, height: 24)
          }
          Text(verbatim: value).role(Typography.title).foregroundStyle(palette.textPrimary)
            .fixedSize(horizontal: false, vertical: true)
        }
        if let meta {
          Text(verbatim: meta).role(Typography.helper).foregroundStyle(palette.textSecondary)
            .fixedSize(horizontal: false, vertical: true)
        }
      }
      .padding(Spacing.tilePadding)
      .frame(maxWidth: .infinity, alignment: .topLeading)
      .background(palette.layer01)
      .overlay { Rectangle().strokeBorder(palette.borderSubtle, lineWidth: 1) }
      .accessibilityElement(children: .ignore)
      .accessibilityLabel(Text(verbatim: spoken))
    }
  }

  /// The History chart (UX-DR5, UX-DR32, UX-DR33): one 1 pt outlined bar per day for 30 days, the
  /// day's low scaled by the core; a day without Readings is a gap. With a Threshold band
  /// (soil moisture in %) it draws the `chart-band` zone, the 2 pt low line and the 1 pt dashed
  /// `chart-high-line`, and a day whose low is under the low line is a solid `chart-bar-below-low`
  /// bar, which is not colour alone. A tap or drag selects a day and shows its readout as text; the
  /// whole chart is one element whose label is the text summary (UX-DR98). Nothing animates.
  struct HistoryChartView: View {
    static let plotHeight: Double = 120
    static let barGap = 0.2

    let chart: HistoryChartPresentation
    let context: CopyContext
    @State private var selected: Int?
    @Environment(\.palette) private var palette

    var body: some View {
      VStack(alignment: .leading, spacing: Spacing.step3) {
        GeometryReader { geometry in
          let width = Double(max(1, geometry.size.width))
          Canvas { drawing, size in
            draw(&drawing, size: size)
          }
          .contentShape(Rectangle())
          .gesture(
            DragGesture(minimumDistance: 0)
              .onChanged { value in
                selected = chart.index(atFraction: Double(value.location.x) / width)
              })
        }
        .frame(height: Self.plotHeight)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(Text(verbatim: chart.summary(context)))
        .accessibilityAddTraits(.isImage)
        if let legend = chart.legend(context) {
          // The legend explains the solid bars.
          Text(verbatim: legend).role(Typography.metaMono).foregroundStyle(palette.textSecondary)
            .fixedSize(horizontal: false, vertical: true)
        }
        if let first = chart.bars.first, let last = chart.bars.last {
          HStack {
            Text(verbatim: chart.dayText(first.dayStart, context)).role(Typography.metaMono)
              .foregroundStyle(palette.textSecondary)
            Spacer(minLength: 0)
            Text(verbatim: chart.dayText(last.dayStart, context)).role(Typography.metaMono)
              .foregroundStyle(palette.textSecondary)
          }
        }
        if let selected, let readout = chart.readout(at: selected, context) {
          Text(verbatim: readout).role(Typography.metaMono).foregroundStyle(palette.textPrimary)
            .fixedSize(horizontal: false, vertical: true)
        }
      }
    }

    /// The baseline and one outlined bar per present day; the selected one is 2 pt.
    private func draw(_ drawing: inout GraphicsContext, size: CGSize) {
      guard !chart.bars.isEmpty else { return }
      let slot = Double(size.width) / Double(chart.bars.count)
      let gap = slot * Self.barGap
      let height = Double(size.height)
      let plot = height - 2
      // The band sits behind the bars: from the low line up to the high line, or to the top.
      if let band = chart.band {
        let top = band.highFraction ?? 1
        let rect = CGRect(
          x: 0, y: height - plot * top, width: Double(size.width),
          height: plot * (top - band.lowFraction))
        drawing.fill(Path(rect), with: .color(palette.color(ColorTokens.chartBand)))
      }
      var baseline = Path()
      baseline.move(to: CGPoint(x: 0, y: height))
      baseline.addLine(to: CGPoint(x: Double(size.width), y: height))
      drawing.stroke(baseline, with: .color(palette.borderSubtle), lineWidth: 1)
      for (index, bar) in chart.bars.enumerated() where bar.present {
        let stroke = index == selected ? 2.0 : 1.0
        let barHeight = (height - 2) * bar.fraction
        let rect = CGRect(
          x: Double(index) * slot + gap / 2 + stroke / 2, y: height - barHeight + stroke / 2,
          width: max(1, slot - gap - stroke), height: max(1, barHeight - stroke))
        if bar.belowLow {
          // Under the low Threshold: solid, in its own token.
          drawing.fill(Path(rect), with: .color(palette.color(ColorTokens.chartBarBelowLow)))
        }
        drawing.stroke(
          Path(rect),
          with: .color(palette.color(bar.belowLow ? ColorTokens.chartBarBelowLow : ColorTokens.chartBar)),
          lineWidth: stroke)
      }
      if let band = chart.band {
        var low = Path()
        let lowY = height - plot * band.lowFraction
        low.move(to: CGPoint(x: 0, y: lowY))
        low.addLine(to: CGPoint(x: Double(size.width), y: lowY))
        drawing.stroke(low, with: .color(palette.primaryText), lineWidth: 2)
        if let high = band.highFraction {
          var line = Path()
          let highY = height - plot * high
          line.move(to: CGPoint(x: 0, y: highY))
          line.addLine(to: CGPoint(x: Double(size.width), y: highY))
          drawing.stroke(
            line, with: .color(palette.color(ColorTokens.chartHighLine)),
            style: StrokeStyle(lineWidth: 1, dash: [4, 3]))
        }
      }
    }
  }
#endif
