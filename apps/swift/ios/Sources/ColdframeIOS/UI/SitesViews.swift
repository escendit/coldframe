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

  /// The Site overview (UX-DR62, UX-DR82): the Site name with the switcher and the Site menu,
  /// then the summary (UX-DR21, UX-DR129), or the stale header in stale mode (UX-DR24), or
  /// "Loading ‹Site›" on a first load (UX-DR80); the four first-run tiles (UX-DR54) and the
  /// Member notice; then the Site's Lot tiles in the Server's order (UX-DR18 to UX-DR20). Pull
  /// to refresh reads the Lots again (UX-DR112). A one-minute tick moves the stale age and the
  /// tiles' durations; it fetches nothing and announces nothing. The Site menu's Site settings
  /// opens Site settings (UX-DR74). A *no Node* Lot tile starts Add a Node with its Lot for
  /// Administrators and Owners; the first-run "Add a Node" step tile starts nothing.
  public struct GardenView: View {
    let presentation: GardenPresentation
    let lots: LotsPresentation
    let actions: SitesActions
    let lotsActions: LotsActions
    let onAddHub: () -> Void
    let onAddNode: (String) -> Void
    let lotDetail: LotDetailPresentation
    let lotDetailActions: LotDetailActions
    let onOpenDevices: () -> Void
    let now: () -> Date
    let timeZone: TimeZone
    @State private var switching = false
    @State private var openingSiteSettings = false
    @State private var openedLot: OpenedLot?
    @Environment(\.palette) private var palette
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize
    @Environment(\.locale) private var locale

    public init(
      presentation: GardenPresentation, lots: LotsPresentation = .waiting,
      actions: SitesActions, lotsActions: LotsActions = .none, onAddHub: @escaping () -> Void = {},
      onAddNode: @escaping (String) -> Void = { _ in },
      lotDetail: LotDetailPresentation = .idle, lotDetailActions: LotDetailActions = .none,
      onOpenDevices: @escaping () -> Void = {},
      now: @escaping () -> Date = { Date() }, timeZone: TimeZone = .current
    ) {
      self.presentation = presentation
      self.lots = lots
      self.actions = actions
      self.lotsActions = lotsActions
      self.onAddHub = onAddHub
      self.onAddNode = onAddNode
      self.lotDetail = lotDetail
      self.lotDetailActions = lotDetailActions
      self.onOpenDevices = onOpenDevices
      self.now = now
      self.timeZone = timeZone
    }

    public var body: some View {
      let context = CopyContext.catalogue(now: now(), timeZone: timeZone, locale: locale)
      ScrollView {
        VStack(alignment: .leading, spacing: Spacing.step6) {
          header(context)
          tiles
          if let notice = presentation.memberNotice {
            InlineNotice(message: notice)
          }
          LotGrid(
            lots: lots, context: context, onTryAgain: lotsActions.load, onAddNode: onAddNode,
            onOpenLot: { openedLot = OpenedLot(id: $0, name: $1) })
        }
        .padding(Spacing.gutterMobile)
      }
      .background(palette.background)
      .refreshable { [lotsActions] in
        await MainActor.run { lotsActions.refresh() }
      }
      .task { [lotsActions] in
        // The only timer of the overview: once a minute the core's snapshot is read again.
        while !Task.isCancelled {
          try? await Task.sleep(for: .seconds(60))
          if !Task.isCancelled {
            await MainActor.run { lotsActions.tick() }
          }
        }
      }
      .navigationDestination(item: $openedLot) { lot in
        // The core reads the Lot while the destination is on screen and forgets it on leaving.
        LotDetailView(
          presentation: lotDetail, siteName: presentation.siteName, actions: lotDetailActions,
          onAddNode: onAddNode, onOpenDevices: onOpenDevices, now: now, timeZone: timeZone
        )
        .navigationTitle(lot.name)
        .navigationBarTitleDisplayMode(.inline)
        .onAppear { lotDetailActions.open(lot.id, lot.name) }
        .onDisappear { lotDetailActions.close() }
      }
      .navigationDestination(isPresented: $openingSiteSettings) {
        SiteSettingsView(presentation: lots, actions: lotsActions)
      }
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

    private func header(_ context: CopyContext) -> some View {
      let menu = presentation.menu(lots: lots)
      return VStack(alignment: .leading, spacing: Spacing.step3) {
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
            siteName: presentation.siteName, items: menu.items, enabled: menu.enabled,
            onOpenSiteSettings: { openingSiteSettings = true })
        }
        switch presentation.header(lots: lots) {
        case .summary(let summary):
          SiteSummaryHeader(summary: summary, context: context)
        case .stale(let stale):
          StaleHeader(stale: stale, context: context)
        case .loading(let siteName):
          Text(verbatim: L10n.gardenLoading.string(siteName)).role(Typography.headline)
            .foregroundStyle(palette.textPrimary)
            .fixedSize(horizontal: false, vertical: true)
            .accessibilityAddTraits(.isHeader)
        }
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
            if presentation.startsFlow(tile) {
              Button(action: onAddHub) { FirstRunTile(tile: tile) }
                .buttonStyle(.plain)
                .accessibilityAddTraits(.isButton)
            } else {
              FirstRunTile(tile: tile)
            }
          }
        }
      }
    }
  }

  /// The Site summary (UX-DR21, UX-DR129): the headline sentence, exposed as a heading and in
  /// paused ink for a paused Site, over the counts subline.
  struct SiteSummaryHeader: View {
    let summary: SiteSummaryPresentation
    let context: CopyContext
    @Environment(\.palette) private var palette

    var body: some View {
      VStack(alignment: .leading, spacing: Spacing.step3) {
        Text(verbatim: summary.headline(context)).role(Typography.headline)
          .foregroundStyle(palette.color(summary.headlineInk))
          .fixedSize(horizontal: false, vertical: true)
          .accessibilityAddTraits(.isHeader)
        if let subline = summary.subline(context) {
          Text(verbatim: subline).role(Typography.body).foregroundStyle(palette.textSecondary)
            .fixedSize(horizontal: false, vertical: true)
        }
      }
    }
  }

  /// The stale header (UX-DR24): `cloud--offline` beside "‹Site› · can't reach your Server",
  /// the age in `headline` and `stale-ink`, and the line that says when the data is from. The
  /// age changes with the minute tick as plain text: nothing here posts an announcement.
  struct StaleHeader: View {
    let stale: StaleHeaderPresentation
    let context: CopyContext
    @Environment(\.palette) private var palette

    var body: some View {
      VStack(alignment: .leading, spacing: Spacing.step3) {
        HStack(alignment: .top, spacing: Spacing.step2) {
          CarbonIconShape(stale.icon).fill(palette.textPrimary).frame(width: 20, height: 20)
            .accessibilityHidden(true)
          Text(verbatim: stale.title(context)).role(Typography.bodyLg)
            .foregroundStyle(palette.textPrimary)
            .fixedSize(horizontal: false, vertical: true)
        }
        Text(verbatim: stale.ageText(context)).role(Typography.headline)
          .foregroundStyle(palette.color(stale.ageInk))
          .fixedSize(horizontal: false, vertical: true)
          .accessibilityAddTraits(.isHeader)
        Text(verbatim: stale.detail(context)).role(Typography.body)
          .foregroundStyle(palette.textSecondary)
          .fixedSize(horizontal: false, vertical: true)
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
  /// It renders Site settings, which opens Site settings (Story 1.9).
  public struct SiteMenu: View {
    let siteName: String
    let items: [SiteMenuItem]
    let enabled: Bool
    let onOpenSiteSettings: () -> Void
    @Environment(\.palette) private var palette

    public init(
      siteName: String, items: [SiteMenuItem], enabled: Bool,
      onOpenSiteSettings: @escaping () -> Void
    ) {
      self.siteName = siteName
      self.items = items
      self.enabled = enabled
      self.onOpenSiteSettings = onOpenSiteSettings
    }

    public var body: some View {
      Menu {
        ForEach(items, id: \.self) { item in
          Button {
            if item.opensSiteSettings { onOpenSiteSettings() }
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
