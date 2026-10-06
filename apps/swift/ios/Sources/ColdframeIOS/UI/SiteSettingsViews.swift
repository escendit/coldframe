#if canImport(SwiftUI)
  import ColdframeDesignTokens
  import Foundation
  import SwiftUI

  /// What the Lots views can ask of the core. Built over a `LotsService`; `.none` does nothing,
  /// for previews and render tests.
  @MainActor
  public struct LotsActions {
    public var load: () -> Void
    public var refresh: () -> Void
    public var tick: () -> Void
    public var setSiteName: (String) -> Void
    public var renameSite: () -> Void
    public var setNewLotName: (String) -> Void
    public var createLot: () -> Void
    public var startRename: (String) -> Void
    public var setRename: (String) -> Void
    public var rename: () -> Void
    public var cancelRename: () -> Void
    public var askRemove: (String) -> Void
    public var confirmRemove: () -> Void
    public var cancelRemove: () -> Void

    public init(service: LotsService) {
      load = { service.load() }
      refresh = { service.refresh() }
      tick = { service.tick() }
      setSiteName = { service.setSiteName($0) }
      renameSite = { service.renameSite() }
      setNewLotName = { service.setNewLotName($0) }
      createLot = { service.createLot() }
      startRename = { service.startRename(lotId: $0) }
      setRename = { service.setRename($0) }
      rename = { service.rename() }
      cancelRename = { service.cancelRename() }
      askRemove = { service.askRemove(lotId: $0) }
      confirmRemove = { service.confirmRemove() }
      cancelRemove = { service.cancelRemove() }
    }

    private init() {
      load = {}
      refresh = {}
      tick = {}
      setSiteName = { _ in }
      renameSite = {}
      setNewLotName = { _ in }
      createLot = {}
      startRename = { _ in }
      setRename = { _ in }
      rename = {}
      cancelRename = {}
      askRemove = { _ in }
      confirmRemove = {}
      cancelRemove = {}
    }

    public static let none = LotsActions()
  }

  /// Site settings (UX-DR74, UX-DR84): the Site name with "Rename Site" for an Owner, then the
  /// Lots with "Create Lot", and per Lot "Rename Lot" (an alert with a field) and "Remove Lot"
  /// (a destructive dialog naming the Lot) for Owners and Administrators. A Member sees the name
  /// and the Lots as text with one notice. Buttons show their working label in place.
  public struct SiteSettingsView: View {
    let presentation: LotsPresentation
    let actions: LotsActions
    @Environment(\.palette) private var palette

    public init(presentation: LotsPresentation, actions: LotsActions) {
      self.presentation = presentation
      self.actions = actions
    }

    public var body: some View {
      ScrollView {
        Group {
          switch presentation.surface {
          case .waiting, .loading:
            EmptyView()
          case .failed(let notice):
            InlineNotice(
              message: notice.loadMessage, announcement: notice.announcement,
              action: notice.action.map { ($0.label, actions.load) })
          case .ready(let settings, _):
            content(settings)
          }
        }
        .padding(Spacing.gutterMobile)
        .frame(maxWidth: .infinity, alignment: .leading)
      }
      .background(palette.background)
      .navigationTitle(L10n.siteSettingsTitle.string)
      .alert(
        L10n.siteSettingsRenameLot.text,
        isPresented: Binding(
          get: { presentation.siteSettings?.renaming != nil },
          set: { if !$0 { actions.cancelRename() } }),
        presenting: presentation.siteSettings?.renaming
      ) { renaming in
        TextField(
          L10n.siteSettingsLotName.string,
          text: Binding(get: { renaming.draft }, set: { actions.setRename($0) }))
        Button(action: actions.rename) { renaming.buttonLabel.text }
          .disabled(!renaming.isButtonEnabled)
        Button(role: .cancel, action: actions.cancelRename) { L10n.siteSettingsCancel.text }
      } message: { renaming in
        (renaming.error?.lotMessage ?? .siteSettingsLotNameHelper).text
      }
      .confirmationDialog(
        Text(
          verbatim: presentation.siteSettings?.removing.map {
            $0.title.string($0.lotName)
          } ?? ""),
        isPresented: Binding(
          get: { presentation.siteSettings?.removing != nil },
          set: { if !$0 { actions.cancelRemove() } }),
        titleVisibility: .visible,
        presenting: presentation.siteSettings?.removing
      ) { removing in
        Button(role: .destructive, action: actions.confirmRemove) { removing.confirm.text }
          .disabled(removing.working)
        Button(role: .cancel, action: actions.cancelRemove) { removing.cancel.text }
      } message: { removing in
        removing.message.text
      }
    }

    @ViewBuilder
    private func content(_ settings: SiteSettingsPresentation) -> some View {
      VStack(alignment: .leading, spacing: Spacing.step6) {
        siteName(settings)
        if let notice = settings.notice {
          InlineNotice(
            message: notice.message, subject: notice.takesSubject ? settings.noticeSubject : nil,
            announcement: notice.announcement)
        }
        lots(settings)
      }
    }

    @ViewBuilder
    private func siteName(_ settings: SiteSettingsPresentation) -> some View {
      if settings.canRenameSite {
        VStack(alignment: .leading, spacing: Spacing.step4) {
          TextInputField(
            label: L10n.siteSettingsSiteName.string,
            value: Binding(get: { settings.siteNameDraft }, set: { actions.setSiteName($0) }),
            helper: settings.siteNameError == nil ? L10n.siteSettingsSiteNameHelper.string : nil,
            error: settings.siteNameError?.message.string)
          PrimaryButton(
            settings.renameSiteLabel, isEnabled: !settings.siteRenameWorking,
            action: actions.renameSite)
        }
      } else {
        VStack(alignment: .leading, spacing: Spacing.step2) {
          L10n.siteSettingsSiteName.text.role(Typography.helper)
            .foregroundStyle(palette.textSecondary)
          Text(verbatim: settings.siteName).role(Typography.bodyLg)
            .foregroundStyle(palette.textPrimary)
            .fixedSize(horizontal: false, vertical: true)
        }
        .accessibilityElement(children: .combine)
      }
    }

    @ViewBuilder
    private func lots(_ settings: SiteSettingsPresentation) -> some View {
      VStack(alignment: .leading, spacing: Spacing.step4) {
        L10n.siteSettingsLots.text.role(Typography.section).foregroundStyle(palette.textPrimary)
          .accessibilityAddTraits(.isHeader)
        if let notice = settings.readOnlyNotice {
          InlineNotice(message: notice)
        }
        if let empty = settings.emptyLots {
          empty.text.role(Typography.body).foregroundStyle(palette.textSecondary)
        }
        VStack(alignment: .leading, spacing: 0) {
          // The Server's order, never re-sorted (UX-DR20).
          ForEach(settings.lots) { lot in
            row(lot, editable: settings.canEditLots)
          }
        }
        if settings.canEditLots {
          TextInputField(
            label: L10n.siteSettingsLotName.string,
            value: Binding(get: { settings.newLotName }, set: { actions.setNewLotName($0) }),
            helper: settings.newLotNameError == nil
              ? L10n.siteSettingsLotNameHelper.string : nil,
            error: settings.newLotNameError?.lotMessage.string)
          PrimaryButton(
            settings.createLotLabel, isEnabled: !settings.createWorking, action: actions.createLot)
        }
      }
    }

    private func row(_ lot: LotRowPresentation, editable: Bool) -> some View {
      VStack(alignment: .leading, spacing: Spacing.step2) {
        Text(verbatim: lot.name).role(Typography.bodyLg).foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
          .frame(maxWidth: .infinity, minHeight: TouchTarget.minimum, alignment: .leading)
        if editable {
          // Row actions are text buttons; no new icons (spec).
          ViewThatFits(in: .horizontal) {
            HStack(spacing: Spacing.step3) { rowActions(lot) }
            VStack(alignment: .leading, spacing: 0) { rowActions(lot) }
          }
        }
      }
      .padding(.vertical, Spacing.step3)
      .overlay(alignment: .bottom) { Rectangle().fill(palette.borderSubtle).frame(height: 1) }
      .accessibilityElement(children: .contain)
    }

    @ViewBuilder
    private func rowActions(_ lot: LotRowPresentation) -> some View {
      PrimaryButton(.siteSettingsRenameLot, variant: .ghost) { actions.startRename(lot.id) }
      PrimaryButton(.siteSettingsRemoveLot, variant: .ghost) { actions.askRemove(lot.id) }
    }
  }

  /// The Lot grid on the Site overview (UX-DR18 to UX-DR20, UX-DR97, UX-DR107): the Server's
  /// order, two columns, one from Accessibility 1. A load failure shows its notice in place of
  /// the grid; a first load shows skeleton tiles. Every tile opens Lot detail, except a *no
  /// Node* tile that starts Add a Node with its Lot where the core says so.
  struct LotGrid: View {
    let lots: LotsPresentation
    let context: CopyContext
    let onTryAgain: () -> Void
    let onAddNode: (String) -> Void
    let onOpenLot: (String, String) -> Void
    @Environment(\.palette) private var palette
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    private var columns: Int {
      LotTilePresentation.columns(accessibilitySize: dynamicTypeSize.isAccessibilitySize)
    }

    var body: some View {
      if let failure = lots.failure {
        InlineNotice(
          message: failure.loadMessage, announcement: failure.announcement,
          action: failure.action.map { ($0.label, onTryAgain) })
      } else if lots.loadingSiteName != nil {
        grid {
          ForEach(0..<LotTilePresentation.skeletonCount, id: \.self) { _ in
            LotSkeletonTile()
          }
        }
        // Nothing to read or focus: the header says "Loading ‹Site›".
        .accessibilityHidden(true)
      } else if !lots.tiles.isEmpty {
        VStack(alignment: .leading, spacing: Spacing.step3) {
          L10n.gardenLots.text.role(Typography.section).foregroundStyle(palette.textPrimary)
            .accessibilityAddTraits(.isHeader)
          grid {
            ForEach(lots.tiles) { tile in
              let tileView = LotTile(
                tile: tile, context: context,
                valueFollowsLabel: LotTilePresentation.valueFollowsLabel(columns: columns))
              Button {
                if tile.opensAddNode {
                  onAddNode(tile.id)
                } else {
                  onOpenLot(tile.id, tile.name)
                }
              } label: {
                // The tile is transparent: the whole of it takes the tap.
                tileView.contentShape(Rectangle())
              }
              .buttonStyle(.plain)
              .accessibilityAddTraits(.isButton)
            }
          }
        }
      }
    }

    private func grid<Content: View>(@ViewBuilder content: () -> Content) -> some View {
      LazyVGrid(
        columns: Array(
          repeating: GridItem(.flexible(), spacing: Spacing.tileGap, alignment: .top),
          count: columns),
        spacing: Spacing.tileGap,
        content: content)
    }
  }

  /// A skeleton tile (UX-DR19): a 1 pt `border-subtle` outline with no icon, label or value.
  struct LotSkeletonTile: View {
    @Environment(\.palette) private var palette

    var body: some View {
      Rectangle().strokeBorder(palette.borderSubtle, lineWidth: 1)
        .frame(maxWidth: .infinity, minHeight: LotTile.minimumHeight)
    }
  }

  /// One Lot tile (UX-DR17 to UX-DR19): the name over the icon and the status label, then the
  /// big value over the foot line, on the fill and inside the border of its variant. Everything
  /// it shows comes from `LotTilePresentation`. One accessibility element with the complete
  /// spoken label, whether or not `LotGrid` makes it a button.
  struct LotTile: View {
    static let minimumHeight: Double = 160

    let tile: LotTilePresentation
    let context: CopyContext
    /// One column: the value sits directly under the status label.
    let valueFollowsLabel: Bool
    @Environment(\.palette) private var palette

    var body: some View {
      let variant = tile.variant
      let ink = palette.color(variant.ink)
      let labelInk = palette.color(variant.labelInk)
      VStack(alignment: .leading, spacing: Spacing.step3) {
        Text(verbatim: tile.name).role(Typography.tileName).foregroundStyle(ink)
          .fixedSize(horizontal: false, vertical: true)
          .plate(variant.isHatched, palette)
        HStack(spacing: Spacing.step2) {
          CarbonIconShape(tile.icon).fill(labelInk).frame(width: 16, height: 16)
          tile.statusLabel.text.role(Typography.statusLabel).foregroundStyle(labelInk)
            .fixedSize(horizontal: false, vertical: true)
        }
        .plate(variant.isHatched, palette)
        if !valueFollowsLabel {
          Spacer(minLength: 0)
        }
        if let value = tile.valueText(context) {
          Text(verbatim: value).role(Typography.tileValue).foregroundStyle(ink)
            .fixedSize(horizontal: false, vertical: true)
            .plate(variant.isHatched, palette)
        }
        if let foot = tile.footText(context) {
          Text(verbatim: foot).role(Typography.metaMono)
            .foregroundStyle(palette.color(variant.footInk))
            .fixedSize(horizontal: false, vertical: true)
            .plate(variant.isHatched, palette)
        }
      }
      .padding(Spacing.tilePadding)
      .frame(maxWidth: .infinity, minHeight: Self.minimumHeight, alignment: .topLeading)
      .background { LotTileFill(tile: tile) }
      .overlay { LotTileOutline(border: variant.border) }
      .accessibilityElement(children: .ignore)
      .accessibilityLabel(Text(verbatim: tile.spokenText(context)))
    }
  }

  /// A tile's fill: the flat fill of its variant with the soil level, its 2 pt edge and the
  /// 12 pt low Threshold tick; the hatch; or nothing.
  struct LotTileFill: View {
    let tile: LotTilePresentation
    @Environment(\.palette) private var palette

    var body: some View {
      let variant = tile.variant
      ZStack {
        if variant.isHatched {
          Hatch(palette: palette)
        } else if let background = variant.background {
          palette.color(background)
        }
        if variant.showsLevel {
          GeometryReader { geometry in
            let width = Double(geometry.size.width)
            let height = Double(geometry.size.height)
            ZStack(alignment: .topLeading) {
              if let level = tile.level, let fill = variant.level, let edge = variant.levelEdge {
                let top = height * (1 - Double(level) / 100)
                Rectangle().fill(palette.color(fill))
                  .frame(width: width, height: max(0, height - top))
                  .offset(y: top)
                Rectangle().fill(palette.color(edge))
                  .frame(width: width, height: 2)
                  .offset(y: top)
              }
              if let low = tile.lowMarker, let marker = variant.lowMarker {
                Rectangle().fill(palette.color(marker))
                  .frame(width: 12, height: 2)
                  .offset(x: max(0, width - 12), y: height * (1 - Double(low) / 100))
              }
            }
          }
          .clipped()
        }
      }
      .accessibilityHidden(true)
    }
  }

  /// A tile's outline: none, solid, dashed or dotted, in the width and colour of its variant.
  struct LotTileOutline: View {
    let border: LotTileBorder
    @Environment(\.palette) private var palette

    var body: some View {
      if let color = border.color, border.style != .none {
        Rectangle().strokeBorder(
          palette.color(color),
          style: StrokeStyle(
            lineWidth: border.width, lineCap: border.style == .dotted ? .round : .butt,
            dash: dash))
      }
    }

    private var dash: [CGFloat] {
      switch border.style {
      case .dashed: [4, 4]
      case .dotted: [1, 3]
      case .none, .solid: []
      }
    }
  }
#endif
