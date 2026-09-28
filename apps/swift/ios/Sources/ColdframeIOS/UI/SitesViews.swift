#if canImport(SwiftUI)
  import ColdframeDesignTokens
  import Foundation
  import SwiftUI

  /// What the Sites views can ask of the core. Built over a `SitesService`; `.none` does
  /// nothing, for previews and render tests.
  @MainActor
  public struct SitesActions {
    public var availableTimeZones: () -> [String]
    public var load: () -> Void
    public var select: (String) -> Void
    public var newSite: () -> Void
    public var cancelNewSite: () -> Void
    public var setName: (String) -> Void
    public var confirmTimeZone: () -> Void
    public var changeTimeZone: () -> Void
    public var pickTimeZone: (String) -> Void
    public var submit: () -> Void

    public init(service: SitesService) {
      availableTimeZones = { service.availableTimeZones() }
      load = { service.load() }
      select = { service.select(siteId: $0) }
      newSite = { service.newSite() }
      cancelNewSite = { service.cancelNewSite() }
      setName = { service.setName($0) }
      confirmTimeZone = { service.confirmTimeZone() }
      changeTimeZone = { service.changeTimeZone() }
      pickTimeZone = { service.pickTimeZone($0) }
      submit = { service.submit() }
    }

    private init() {
      availableTimeZones = { [] }
      load = {}
      select = { _ in }
      newSite = {}
      cancelNewSite = {}
      setName = { _ in }
      confirmTimeZone = {}
      changeTimeZone = {}
      pickTimeZone = { _ in }
      submit = {}
    }

    public static let none = SitesActions()
  }

  /// The Sites could not be read: the notice, with Try again unless it is a certificate failure.
  public struct SitesFailedView: View {
    let notice: SitesNoticeKind
    let onTryAgain: () -> Void
    @Environment(\.palette) private var palette

    public init(notice: SitesNoticeKind, onTryAgain: @escaping () -> Void) {
      self.notice = notice
      self.onTryAgain = onTryAgain
    }

    public var body: some View {
      ScrollView {
        InlineNotice(
          message: notice.loadMessage, announcement: notice.announcement,
          action: notice.action.map { ($0.label, onTryAgain) }
        )
        .padding(Spacing.gutterMobile)
      }
      .background(palette.background)
    }
  }

  /// Create Site (UX-DR61): the Site name, the time-zone confirm panel (UX-DR61) and "Create
  /// Site", whose working label replaces it in place. Cancel shows only from "New Site".
  public struct CreateSiteView: View {
    let presentation: CreateSitePresentation
    let actions: SitesActions
    @Environment(\.palette) private var palette

    public init(presentation: CreateSitePresentation, actions: SitesActions) {
      self.presentation = presentation
      self.actions = actions
    }

    public var body: some View {
      NavigationStack {
        ScrollView {
          VStack(alignment: .leading, spacing: Spacing.step6) {
            L10n.createSiteTitle.text.role(Typography.headline)
              .foregroundStyle(palette.textPrimary)
              .accessibilityAddTraits(.isHeader)
            L10n.createSiteIntro.text.role(Typography.body)
              .foregroundStyle(palette.textSecondary)
              .fixedSize(horizontal: false, vertical: true)
            TextInputField(
              label: L10n.createSiteName.string,
              value: Binding(get: { presentation.name }, set: { actions.setName($0) }),
              helper: presentation.nameError == nil ? L10n.createSiteNameHelper.string : nil,
              error: presentation.nameError?.message.string)
            TimeZonePanel(presentation: presentation.timeZone, actions: actions)
            if let notice = presentation.notice {
              InlineNotice(
                message: notice.createMessage, announcement: notice.announcement,
                action: notice.action.map { ($0.label, actions.submit) })
            }
            PrimaryButton(
              presentation.buttonLabel, isEnabled: presentation.isButtonEnabled,
              action: actions.submit)
          }
          .padding(Spacing.gutterMobile)
        }
        .background(palette.background)
        .toolbar {
          if presentation.cancellable {
            ToolbarItem(placement: .cancellationAction) {
              Button(action: actions.cancelNewSite) { L10n.modalCancel.text }
                .disabled(presentation.working)
            }
          }
        }
      }
      .interactiveDismissDisabled(true)
    }
  }

  /// The time-zone confirm panel: a dashed `support-warning` box with Confirm and Change; Change
  /// opens a searchable list of IANA zone IDs.
  struct TimeZonePanel: View {
    let presentation: TimeZonePanelPresentation
    let actions: SitesActions
    @State private var query = ""
    @Environment(\.palette) private var palette

    var body: some View {
      VStack(alignment: .leading, spacing: Spacing.step4) {
        L10n.timeZoneLegend.text.role(Typography.body).foregroundStyle(palette.textSecondary)
          .accessibilityAddTraits(.isHeader)
        Text(verbatim: presentation.sentence.string(presentation.zone))
          .role(Typography.bodyLg).foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
        L10n.timeZoneHelper.text.role(Typography.helper).foregroundStyle(palette.textHelper)
          .fixedSize(horizontal: false, vertical: true)
        if presentation.changing {
          zoneList
        } else {
          HStack(spacing: Spacing.step3) {
            if presentation.showsConfirm {
              PrimaryButton(.timeZoneConfirm, variant: .secondary, action: actions.confirmTimeZone)
            }
            PrimaryButton(.timeZoneChange, variant: .ghost, action: actions.changeTimeZone)
          }
        }
      }
      .padding(Spacing.step5)
      .overlay {
        Rectangle().strokeBorder(
          palette.color(ColorTokens.supportWarning),
          style: StrokeStyle(lineWidth: 1, dash: [4, 4]))
      }
    }

    private var zoneList: some View {
      let matches = TimeZonePanelPresentation.filter(actions.availableTimeZones(), query: query)
      return VStack(alignment: .leading, spacing: Spacing.step3) {
        TextInputField(
          label: L10n.timeZoneFilter.string, value: $query,
          helper: L10n.timeZoneFilterHelper.string)
        if matches.isEmpty {
          L10n.timeZoneNone.text.role(Typography.body).foregroundStyle(palette.textSecondary)
        }
        LazyVStack(alignment: .leading, spacing: 0) {
          ForEach(matches, id: \.self) { zone in
            Button {
              actions.pickTimeZone(zone)
            } label: {
              Text(verbatim: zone).role(Typography.bodyLg).foregroundStyle(palette.textPrimary)
                .frame(maxWidth: .infinity, minHeight: TouchTarget.minimum, alignment: .leading)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .overlay(alignment: .bottom) {
              Rectangle().fill(palette.borderSubtle).frame(height: 1)
            }
            .accessibilityAddTraits(zone == presentation.zone ? .isSelected : [])
          }
        }
      }
    }
  }

  /// The empty Garden (UX-DR62, UX-DR82): the Site summary header with the switcher and the
  /// Site menu, "No Readings yet", the four first-run tiles (UX-DR54) and the Member notice.
  public struct GardenView: View {
    let presentation: GardenPresentation
    let actions: SitesActions
    let onOpenTab: (AppTab) -> Void
    @State private var switching = false
    @Environment(\.palette) private var palette
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    public init(
      presentation: GardenPresentation, actions: SitesActions,
      onOpenTab: @escaping (AppTab) -> Void
    ) {
      self.presentation = presentation
      self.actions = actions
      self.onOpenTab = onOpenTab
    }

    public var body: some View {
      ScrollView {
        VStack(alignment: .leading, spacing: Spacing.step6) {
          header
          tiles
          if let notice = presentation.memberNotice {
            InlineNotice(message: notice)
          }
        }
        .padding(Spacing.gutterMobile)
      }
      .background(palette.background)
      .sheet(isPresented: $switching) {
        SiteSwitcherSheet(
          rows: presentation.switcherRows,
          onSelect: { siteId in
            switching = false
            actions.select(siteId)
          },
          onNewSite: {
            switching = false
            actions.newSite()
          })
      }
    }

    private var header: some View {
      VStack(alignment: .leading, spacing: Spacing.step3) {
        HStack(alignment: .top, spacing: Spacing.step3) {
          Button {
            switching = true
          } label: {
            HStack(spacing: Spacing.step2) {
              Text(verbatim: presentation.siteName).role(Typography.bodyLg)
                .foregroundStyle(palette.textPrimary)
                .multilineTextAlignment(.leading)
                .fixedSize(horizontal: false, vertical: true)
              CarbonIconShape(.chevronDown).fill(palette.textPrimary).frame(width: 16, height: 16)
                .accessibilityHidden(true)
            }
            .frame(minHeight: TouchTarget.minimum)
            .contentShape(Rectangle())
          }
          .buttonStyle(.plain)
          .accessibilityLabel(Text(verbatim: L10n.sitesOpenSwitcher.string(presentation.siteName)))
          Spacer(minLength: 0)
          SiteMenu(
            siteName: presentation.siteName, items: presentation.menuItems,
            enabled: presentation.menuEnabled, onOpenTab: onOpenTab)
        }
        presentation.headline.text.role(Typography.headline).foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
          .accessibilityAddTraits(.isHeader)
        presentation.subline.text.role(Typography.body).foregroundStyle(palette.textSecondary)
          .fixedSize(horizontal: false, vertical: true)
      }
    }

    private var tiles: some View {
      let columns = dynamicTypeSize.isAccessibilitySize ? 1 : 2
      return VStack(alignment: .leading, spacing: Spacing.step3) {
        L10n.gardenSteps.text.role(Typography.section).foregroundStyle(palette.textPrimary)
          .accessibilityAddTraits(.isHeader)
        LazyVGrid(
          columns: Array(
            repeating: GridItem(.flexible(), spacing: Spacing.tileGap, alignment: .top),
            count: columns),
          spacing: Spacing.tileGap
        ) {
          ForEach(presentation.tiles, id: \.number) { tile in
            FirstRunTile(tile: tile)
          }
        }
      }
    }
  }

  /// One first-run step tile: next is solid `primary` with `ink-on-bright`, later and done are a 1 pt
  /// dashed `border-strong` outline, done carries a checkmark. Read as one element.
  struct FirstRunTile: View {
    let tile: FirstRunTilePresentation
    @Environment(\.palette) private var palette

    var body: some View {
      let ink = tile.isSolid ? palette.inkOnBright : palette.textPrimary
      VStack(alignment: .leading, spacing: Spacing.step3) {
        HStack(spacing: Spacing.step2) {
          if tile.showsCheckmark {
            CarbonIconShape(.checkmark).fill(ink).frame(width: 16, height: 16)
          }
          Text(verbatim: L10n.gardenStep.string(tile.number)).role(Typography.statusLabel)
            .foregroundStyle(ink)
        }
        tile.step.label.text.role(Typography.tileName).foregroundStyle(ink)
          .fixedSize(horizontal: false, vertical: true)
        tile.state.label.text.role(Typography.statusLabel).foregroundStyle(ink)
      }
      .padding(Spacing.tilePadding)
      .frame(maxWidth: .infinity, minHeight: 120, alignment: .topLeading)
      .background(tile.isSolid ? palette.primary : Color.clear)
      .overlay {
        if !tile.isSolid {
          Rectangle().strokeBorder(
            palette.borderStrong,
            style: StrokeStyle(lineWidth: 1, dash: tile.isDashed ? [4, 4] : []))
        }
      }
      .accessibilityElement(children: .combine)
    }
  }

  /// The Site switcher (UX-DR23): a native sheet listing the Sites in the Server's order with
  /// their Role; the current one is marked with a checkmark and selected; "New Site" is last.
  public struct SiteSwitcherSheet: View {
    let rows: [SiteSwitcherRow]
    let onSelect: (String) -> Void
    let onNewSite: () -> Void
    @Environment(\.palette) private var palette

    public init(
      rows: [SiteSwitcherRow], onSelect: @escaping (String) -> Void,
      onNewSite: @escaping () -> Void
    ) {
      self.rows = rows
      self.onSelect = onSelect
      self.onNewSite = onNewSite
    }

    public var body: some View {
      NavigationStack {
        List(rows) { row in
          Button {
            if let siteId = row.siteId { onSelect(siteId) } else { onNewSite() }
          } label: {
            label(for: row)
          }
          .buttonStyle(.plain)
          .accessibilityAddTraits(row.traits.contains(.selected) ? .isSelected : [])
        }
        .listStyle(.plain)
        .scrollContentBackground(.hidden)
        .background(palette.background)
        .navigationTitle(L10n.sitesTitle.string)
      }
    }

    @ViewBuilder
    private func label(for row: SiteSwitcherRow) -> some View {
      HStack(spacing: Spacing.step3) {
        VStack(alignment: .leading, spacing: Spacing.step1) {
          if let name = row.name {
            Text(verbatim: name).role(Typography.bodyLg).foregroundStyle(palette.textPrimary)
          } else {
            L10n.sitesNew.text.role(Typography.bodyLg).foregroundStyle(palette.primaryText)
          }
          if let role = row.role {
            role.label.text.role(Typography.statusLabel).foregroundStyle(palette.textSecondary)
          }
        }
        Spacer(minLength: 0)
        if row.isSelected {
          CarbonIconShape(.checkmark).fill(palette.primaryText).frame(width: 20, height: 20)
            .accessibilityHidden(true)
        }
      }
      .frame(minHeight: TouchTarget.minimum)
      .contentShape(Rectangle())
    }
  }

  /// The Site menu (UX-DR22): a native `Menu` behind the `overflow-menu--vertical` trigger.
  /// Story 1.8 renders only Site settings, which opens the Settings index.
  public struct SiteMenu: View {
    let siteName: String
    let items: [SiteMenuItem]
    let enabled: Bool
    let onOpenTab: (AppTab) -> Void
    @Environment(\.palette) private var palette

    public init(
      siteName: String, items: [SiteMenuItem], enabled: Bool,
      onOpenTab: @escaping (AppTab) -> Void
    ) {
      self.siteName = siteName
      self.items = items
      self.enabled = enabled
      self.onOpenTab = onOpenTab
    }

    public var body: some View {
      Menu {
        ForEach(items, id: \.self) { item in
          Button {
            if let tab = item.opensTab { onOpenTab(tab) }
          } label: {
            if item == .siteSettings {
              item.label.text
            } else {
              Text(verbatim: item.label.string(siteName))
            }
            if !enabled { L10n.siteMenuNeedsServer.text }
          }
          .disabled(!enabled)
        }
      } label: {
        CarbonIconShape(.overflowMenuVertical).fill(palette.textPrimary)
          .frame(width: 20, height: 20)
          .frame(minWidth: TouchTarget.minimum, minHeight: TouchTarget.minimum)
          .contentShape(Rectangle())
      }
      .accessibilityLabel(Text(verbatim: L10n.siteMenuOpen.string(siteName)))
    }
  }
#endif
