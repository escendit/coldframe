#if canImport(SwiftUI)
  import ColdframeDesignTokens
  import Foundation
  import SwiftUI

  /// What the Lots views can ask of the core. Built over a `LotsService`; `.none` does nothing,
  /// for previews and render tests.
  @MainActor
  public struct LotsActions {
    public var load: () -> Void
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
          case .waiting:
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

  /// The Lot grid on Garden (UX-DR18, UX-DR20): the Server's order, two columns, one at
  /// accessibility text sizes. A load failure shows its notice in place of the grid.
  struct LotGrid: View {
    let lots: LotsPresentation
    let onTryAgain: () -> Void
    @Environment(\.palette) private var palette
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    var body: some View {
      if let failure = lots.failure {
        InlineNotice(
          message: failure.loadMessage, announcement: failure.announcement,
          action: failure.action.map { ($0.label, onTryAgain) })
      } else if !lots.tiles.isEmpty {
        let columns = LotTilePresentation.columns(
          accessibilitySize: dynamicTypeSize.isAccessibilitySize)
        VStack(alignment: .leading, spacing: Spacing.step3) {
          L10n.gardenLots.text.role(Typography.section).foregroundStyle(palette.textPrimary)
            .accessibilityAddTraits(.isHeader)
          LazyVGrid(
            columns: Array(
              repeating: GridItem(.flexible(), spacing: Spacing.tileGap, alignment: .top),
              count: columns),
            spacing: Spacing.tileGap
          ) {
            ForEach(lots.tiles) { tile in
              LotTile(tile: tile)
            }
          }
        }
      }
    }
  }

  /// One Lot tile. *No Node*: transparent, 1 pt dotted `status-no-node-border`, ink
  /// `status-no-node-ink`, the name over `add` + "no Node", a large "+" over "add a Node". Other
  /// statuses show the name only in 1.9. One accessibility element; not tappable yet.
  struct LotTile: View {
    let tile: LotTilePresentation
    @Environment(\.palette) private var palette

    var body: some View {
      let ink = tile.isNoNode ? palette.color(ColorTokens.statusNoNodeInk) : palette.textPrimary
      VStack(alignment: .leading, spacing: Spacing.step3) {
        Text(verbatim: tile.name).role(Typography.tileName).foregroundStyle(ink)
          .fixedSize(horizontal: false, vertical: true)
        if let label = tile.statusLabel {
          HStack(spacing: Spacing.step2) {
            if let icon = tile.icon {
              CarbonIconShape(icon).fill(ink).frame(width: 16, height: 16)
            }
            label.text.role(Typography.statusLabel).foregroundStyle(ink)
          }
        }
        Spacer(minLength: 0)
        if let value = tile.value {
          value.text.role(Typography.tileValue).foregroundStyle(ink)
        }
        if let foot = tile.foot {
          foot.text.role(Typography.metaMono).foregroundStyle(ink)
            .fixedSize(horizontal: false, vertical: true)
        }
      }
      .padding(Spacing.tilePadding)
      .frame(maxWidth: .infinity, minHeight: 160, alignment: .topLeading)
      .overlay {
        Rectangle().strokeBorder(
          tile.isNoNode ? palette.color(ColorTokens.statusNoNodeBorder) : palette.borderStrong,
          style: StrokeStyle(lineWidth: 1, lineCap: .round, dash: tile.isDotted ? [1, 3] : []))
      }
      .accessibilityElement(children: .ignore)
      .accessibilityLabel(
        Text(verbatim: tile.accessibilityFormat.map { $0.string(tile.name) } ?? tile.name))
    }
  }
#endif
