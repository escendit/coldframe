#if canImport(SwiftUI)
  import ColdframeDesignTokens
  import Foundation
  import SwiftUI

  /// What the Alerts tab can ask of the core. Built over an `AlertsService`; `.none` does
  /// nothing, for previews and render tests.
  @MainActor
  public struct AlertsActions {
    public var load: () -> Void
    public var refresh: () -> Void

    public init(service: AlertsService) {
      load = { service.load() }
      refresh = { service.refresh() }
    }

    private init() {
      load = {}
      refresh = {}
    }

    public static let none = AlertsActions()
  }

  /// The Alerts tab (UX-DR64): open Threshold Alerts, then open Health Alerts, then the Alerts
  /// closed in the last 7 days under "Closed", each in the Server's order. Without an open Alert
  /// it reads "No open Alerts." (UX-DR82), followed by Closed when it has rows. A failed load
  /// shows the notice and no rows. A Threshold or uncalibrated row pushes Lot detail on this
  /// tab's own stack; a silent or battery row opens Devices. `now` is the clock the times are
  /// told against.
  public struct AlertsView: View {
    let presentation: AlertsPresentation
    let actions: AlertsActions
    let lotDetail: LotDetailPresentation
    let lotDetailActions: LotDetailActions
    let onAddNode: (String) -> Void
    let onCalibrate: (String, String) -> Void
    let onSetThresholds: (String, String, String) -> Void
    let onOpenDevices: () -> Void
    let now: () -> Date
    let timeZone: TimeZone
    @State private var openedLot: OpenedLot?
    @Environment(\.palette) private var palette
    @Environment(\.locale) private var locale

    public init(
      presentation: AlertsPresentation, actions: AlertsActions = .none,
      lotDetail: LotDetailPresentation = .idle, lotDetailActions: LotDetailActions = .none,
      onAddNode: @escaping (String) -> Void = { _ in },
      onCalibrate: @escaping (String, String) -> Void = { _, _ in },
      onSetThresholds: @escaping (String, String, String) -> Void = { _, _, _ in },
      onOpenDevices: @escaping () -> Void = {}, now: @escaping () -> Date = { Date() },
      timeZone: TimeZone = .current
    ) {
      self.presentation = presentation
      self.actions = actions
      self.lotDetail = lotDetail
      self.lotDetailActions = lotDetailActions
      self.onAddNode = onAddNode
      self.onCalibrate = onCalibrate
      self.onSetThresholds = onSetThresholds
      self.onOpenDevices = onOpenDevices
      self.now = now
      self.timeZone = timeZone
    }

    public var body: some View {
      let context = CopyContext.catalogue(now: now(), timeZone: timeZone, locale: locale)
      ScrollView {
        VStack(alignment: .leading, spacing: Spacing.step6) {
          content(context)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, Spacing.gutterMobile)
        .padding(.vertical, Spacing.step5)
      }
      .background(palette.background)
      .refreshable { [actions] in
        await MainActor.run { actions.refresh() }
      }
      .navigationDestination(item: $openedLot) { lot in
        // The core reads the Lot while the destination is on screen and forgets it on leaving.
        LotDetailView(
          presentation: lotDetail, siteName: presentation.siteName ?? "",
          actions: lotDetailActions, onAddNode: onAddNode, onCalibrate: onCalibrate,
          onSetThresholds: onSetThresholds, onOpenDevices: onOpenDevices, now: now,
          timeZone: timeZone
        )
        .navigationTitle(lot.name)
        .onAppear { lotDetailActions.open(lot.id, lot.name) }
        .onDisappear { lotDetailActions.close() }
      }
    }

    @ViewBuilder
    private func content(_ context: CopyContext) -> some View {
      switch presentation.surface {
      case .waiting:
        // Nothing is shown while the Alerts are read for the first time.
        EmptyView()
      case .failed(let notice):
        InlineNotice(
          message: notice.message,
          action: notice.offersTryAgain ? (label: L10n.noticeTryAgain, perform: actions.load) : nil)
      case .ready:
        if presentation.showsEmpty {
          L10n.alertsEmpty.text.role(Typography.body).foregroundStyle(palette.textSecondary)
            .fixedSize(horizontal: false, vertical: true)
        }
        ForEach(presentation.groups) { group in
          section(group, context)
        }
      }
    }

    private func section(_ group: AlertGroupPresentation, _ context: CopyContext) -> some View {
      VStack(alignment: .leading, spacing: Spacing.step4) {
        group.kind.title.text.role(Typography.section).foregroundStyle(palette.textPrimary)
          .accessibilityAddTraits(.isHeader)
        VStack(spacing: Spacing.tileGap) {
          ForEach(group.rows) { row in
            Button {
              open(row)
            } label: {
              // A closed row is transparent: the whole of it takes the tap.
              AlertRowView(row: row, context: context).contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityAddTraits(.isButton)
          }
        }
      }
    }

    private func open(_ row: AlertRowPresentation) {
      switch row.target {
      case .lot(let id, let name): openedLot = OpenedLot(id: id, name: name)
      case .devices: onOpenDevices()
      }
    }
  }

  /// One Alert row (UX-DR25, UX-DR26): its icon, then the eyebrow in `status-label` over the
  /// title in `section` type, on the fill and inside the border of its variant. On a hatched row
  /// the icon and the text sit on a solid plate. One accessibility element with the complete
  /// spoken label (UX-DR98); the row has no other action.
  struct AlertRowView: View {
    let row: AlertRowPresentation
    let context: CopyContext
    @Environment(\.palette) private var palette

    var body: some View {
      let variant = row.variant
      let ink = palette.color(variant.ink)
      HStack(alignment: .top, spacing: Spacing.step4) {
        CarbonIconShape(row.icon).fill(ink).frame(width: 20, height: 20)
          .plate(variant.isHatched, palette)
        VStack(alignment: .leading, spacing: Spacing.step2) {
          // Sentence case in the catalogue; the status-label style makes it uppercase.
          Text(verbatim: row.eyebrowText(context)).role(Typography.statusLabel)
            .foregroundStyle(ink)
            .multilineTextAlignment(.leading)
            .fixedSize(horizontal: false, vertical: true)
            .plate(variant.isHatched, palette)
          Text(verbatim: row.titleText(context)).role(Typography.section)
            .foregroundStyle(ink)
            .multilineTextAlignment(.leading)
            .fixedSize(horizontal: false, vertical: true)
            .plate(variant.isHatched, palette)
        }
        Spacer(minLength: 0)
      }
      .padding(Spacing.tilePadding)
      .frame(maxWidth: .infinity, minHeight: TouchTarget.control, alignment: .topLeading)
      .background {
        if variant.isHatched {
          Hatch(palette: palette)
        } else if let background = variant.background {
          palette.color(background)
        }
      }
      .overlay { LotTileOutline(border: variant.border) }
      .accessibilityElement(children: .ignore)
      .accessibilityLabel(Text(verbatim: row.spokenText(context)))
    }
  }
#endif
