#if canImport(SwiftUI)
  import ColdframeDesignTokens
  import Foundation
  import SwiftUI

  /// The Sign-in surface (UX-DR59, UX-DR60): the signature radial gradient behind a card with
  /// the Coldframe mark and SIGN IN. No Server field; the view holds no input (AD-23).
  public struct SignInView: View {
    let presentation: SignInPresentation
    let onSignIn: () -> Void
    @Environment(\.palette) private var palette

    public init(presentation: SignInPresentation, onSignIn: @escaping () -> Void) {
      self.presentation = presentation
      self.onSignIn = onSignIn
    }

    public var body: some View {
      GeometryReader { geometry in
        ScrollView {
          card
            .frame(maxWidth: 416)
            .padding(Spacing.gutterMobile)
            .frame(minWidth: geometry.size.width, minHeight: geometry.size.height)
        }
        .background {
          RadialGradient(
            colors: [palette.primary, palette.secondary],
            center: UnitPoint(x: 0.05, y: 0.05), startRadius: 0,
            endRadius: hypot(geometry.size.width, geometry.size.height)
          )
          .ignoresSafeArea()
        }
      }
    }

    private var card: some View {
      VStack(alignment: .leading, spacing: Spacing.step6) {
        L10n.appName.text.role(Typography.headline).foregroundStyle(palette.textPrimary)
          .accessibilityAddTraits(.isHeader)
        if let notice = presentation.notice {
          InlineNotice(
            message: notice.message, announcement: notice.announcement,
            action: presentation.noticeAction.map { ($0.label, onSignIn) })
        }
        PrimaryButton(
          presentation.buttonLabel, isEnabled: presentation.isButtonEnabled, action: onSignIn)
      }
      .padding(Spacing.step7)
      .background(palette.background)
      .overlay { Rectangle().strokeBorder(palette.borderSubtle, lineWidth: 1) }
    }
  }

  /// The signed-in shell (UX-DR57, UX-DR109): native `TabView` with a `NavigationStack` per tab
  /// and Carbon icons; the selected tab uses the native selected state and `primary-text` tint.
  /// The Garden tab shows the current Site's Garden with its Lots (Stories 1.8 and 1.9); the
  /// Devices tab lists the Hubs (Story 3.7) and reads them again every time it is entered.
  /// `selection` is the selected tab: the root hoists it, so closing a flow that replaced the
  /// shell (Add a Hub, Add a Node) returns to the tab it was opened from. `onAddNode` takes the
  /// Lot of a *no Node* tile, or nil from Devices.
  public struct AppTabView: View {
    let theme: ThemePreference
    let onSelectTheme: (ThemePreference) -> Void
    let onSignOut: () -> Void
    let garden: GardenPresentation?
    let sitesActions: SitesActions
    let lots: LotsPresentation
    let lotsActions: LotsActions
    let onAddHub: () -> Void
    let onAddNode: (String?) -> Void
    let devices: DevicesPresentation
    let devicesActions: DevicesActions
    let hoistedSelection: Binding<AppTab>?
    @State private var ownSelection: AppTab = .garden
    @Environment(\.palette) private var palette

    private var selection: Binding<AppTab> { hoistedSelection ?? $ownSelection }

    public init(
      theme: ThemePreference, onSelectTheme: @escaping (ThemePreference) -> Void,
      onSignOut: @escaping () -> Void, garden: GardenPresentation? = nil,
      sitesActions: SitesActions = .none, lots: LotsPresentation = .waiting,
      lotsActions: LotsActions = .none, onAddHub: @escaping () -> Void = {},
      onAddNode: @escaping (String?) -> Void = { _ in },
      devices: DevicesPresentation = .waiting, devicesActions: DevicesActions = .none,
      selection: Binding<AppTab>? = nil
    ) {
      self.devices = devices
      self.devicesActions = devicesActions
      self.hoistedSelection = selection
      self.theme = theme
      self.onSelectTheme = onSelectTheme
      self.onSignOut = onSignOut
      self.garden = garden
      self.sitesActions = sitesActions
      self.lots = lots
      self.lotsActions = lotsActions
      self.onAddHub = onAddHub
      self.onAddNode = onAddNode
    }

    public var body: some View {
      TabView(selection: selection) {
        ForEach(AppTab.allCases, id: \.self) { tab in
          NavigationStack {
            content(for: tab)
              .navigationTitle(tab.title.string)
          }
          .tabItem {
            Label {
              tab.label.text
            } icon: {
              CarbonImages.image(tab.icon)
            }
          }
          .tag(tab)
        }
      }
      .tint(palette.primaryText)
      // Every entry of the Devices tab reads the list again, also when a closed flow returns to it.
      .onChange(of: selection.wrappedValue, initial: true) { _, tab in
        if tab == .devices { devicesActions.load() }
      }
    }

    @ViewBuilder
    private func content(for tab: AppTab) -> some View {
      switch tab {
      case .settings:
        SettingsView(
          theme: theme, onSelectTheme: onSelectTheme, onSignOut: onSignOut,
          siteName: garden?.siteName, lots: lots, lotsActions: lotsActions)
      case .garden:
        if let garden {
          GardenView(
            presentation: garden, lots: lots, actions: sitesActions, lotsActions: lotsActions,
            onAddHub: onAddHub, onAddNode: { onAddNode($0) })
        } else {
          palette.background.ignoresSafeArea()
        }
      case .devices:
        DevicesView(
          presentation: devices, actions: devicesActions, onAddHub: onAddHub,
          onAddNode: { onAddNode(nil) })
      case .alerts:
        // Alerts carries its heading only until its story.
        palette.background.ignoresSafeArea()
      }
    }
  }

  /// Settings index (UX-DR71): Site settings of the current Site, Appearance, then Account with
  /// Sign out, confirmed in a native dialog that names the result (UX-DR113).
  public struct SettingsView: View {
    let theme: ThemePreference
    let onSelectTheme: (ThemePreference) -> Void
    let onSignOut: () -> Void
    let siteName: String?
    let lots: LotsPresentation
    let lotsActions: LotsActions
    @State private var confirmingSignOut = false
    @Environment(\.palette) private var palette

    public init(
      theme: ThemePreference, onSelectTheme: @escaping (ThemePreference) -> Void,
      onSignOut: @escaping () -> Void, siteName: String? = nil,
      lots: LotsPresentation = .waiting, lotsActions: LotsActions = .none
    ) {
      self.theme = theme
      self.onSelectTheme = onSelectTheme
      self.onSignOut = onSignOut
      self.siteName = siteName
      self.lots = lots
      self.lotsActions = lotsActions
    }

    public var body: some View {
      let confirmation = ConfirmationPresentation.signOut
      List {
        if let siteName {
          NavigationLink {
            SiteSettingsView(presentation: lots, actions: lotsActions)
          } label: {
            VStack(alignment: .leading, spacing: Spacing.step1) {
              SettingsRow.siteSettings.title.text.role(Typography.bodyLg)
                .foregroundStyle(palette.textPrimary)
              Text(verbatim: L10n.settingsSiteSettingsHelper.string(siteName))
                .role(Typography.helper)
                .foregroundStyle(palette.textHelper)
            }
            .frame(minHeight: TouchTarget.minimum)
          }
        }
        NavigationLink {
          AppearanceView(theme: theme, onSelectTheme: onSelectTheme)
        } label: {
          VStack(alignment: .leading, spacing: Spacing.step1) {
            L10n.settingsAppearance.text.role(Typography.bodyLg)
              .foregroundStyle(palette.textPrimary)
            L10n.settingsAppearanceHelper.text.role(Typography.helper)
              .foregroundStyle(palette.textHelper)
          }
          .frame(minHeight: TouchTarget.minimum)
        }
        Section {
          PrimaryButton(.settingsSignOut, variant: .secondary) { confirmingSignOut = true }
            .listRowInsets(EdgeInsets())
        } header: {
          L10n.settingsAccount.text.role(Typography.section).foregroundStyle(palette.textPrimary)
            .accessibilityAddTraits(.isHeader)
        }
      }
      .listStyle(.plain)
      .scrollContentBackground(.hidden)
      .background(palette.background)
      .confirmationDialog(
        confirmation.title.text, isPresented: $confirmingSignOut, titleVisibility: .visible
      ) {
        Button(role: .destructive, action: onSignOut) { confirmation.confirm.text }
        Button(role: .cancel) {
        } label: {
          confirmation.cancel.text
        }
      } message: {
        confirmation.message.text
      }
    }
  }

  /// Appearance (UX-DR75): the Theme switcher only.
  public struct AppearanceView: View {
    let theme: ThemePreference
    let onSelectTheme: (ThemePreference) -> Void
    @Environment(\.palette) private var palette

    public init(theme: ThemePreference, onSelectTheme: @escaping (ThemePreference) -> Void) {
      self.theme = theme
      self.onSelectTheme = onSelectTheme
    }

    public var body: some View {
      ScrollView {
        ThemeSwitcher(selected: theme, onSelect: onSelectTheme)
          .padding(Spacing.gutterMobile)
      }
      .background(palette.background)
      .navigationTitle(L10n.appearanceTitle.string)
    }
  }

  /// Maps the core's presentation to a surface; the only state the views know.
  public struct ColdframeRootView: View {
    let presentation: SignInPresentation
    let sites: SitesPresentation
    let sitesActions: SitesActions
    let lots: LotsPresentation
    let lotsActions: LotsActions
    let hubSetup: HubSetupPresentation
    let hubSetupActions: HubSetupActions
    let nodeSetup: NodeSetupPresentation
    let nodeSetupActions: NodeSetupActions
    let theme: ThemePreference
    let onSignIn: () -> Void
    let onSignOut: () -> Void
    let onSelectTheme: (ThemePreference) -> Void
    let devices: DevicesPresentation
    let devicesActions: DevicesActions
    /// The selected tab outlives the tab shell, which Add a Hub and Add a Node replace while
    /// their flow is open.
    @State private var tab: AppTab = .garden
    @Environment(\.colorScheme) private var systemScheme

    public init(
      presentation: SignInPresentation, sites: SitesPresentation = .waiting,
      sitesActions: SitesActions = .none, lots: LotsPresentation = .waiting,
      lotsActions: LotsActions = .none, hubSetup: HubSetupPresentation = .closed,
      hubSetupActions: HubSetupActions = .none, theme: ThemePreference,
      onSignIn: @escaping () -> Void, onSignOut: @escaping () -> Void,
      onSelectTheme: @escaping (ThemePreference) -> Void,
      devices: DevicesPresentation = .waiting, devicesActions: DevicesActions = .none,
      nodeSetup: NodeSetupPresentation = .closed, nodeSetupActions: NodeSetupActions = .none
    ) {
      self.devices = devices
      self.devicesActions = devicesActions
      self.nodeSetup = nodeSetup
      self.nodeSetupActions = nodeSetupActions
      self.presentation = presentation
      self.sites = sites
      self.sitesActions = sitesActions
      self.lots = lots
      self.lotsActions = lotsActions
      self.hubSetup = hubSetup
      self.hubSetupActions = hubSetupActions
      self.theme = theme
      self.onSignIn = onSignIn
      self.onSignOut = onSignOut
      self.onSelectTheme = onSelectTheme
    }

    public var body: some View {
      let isDark = theme.isDark(systemIsDark: systemScheme == .dark)
      Group {
        switch presentation.surface {
        case .restoring:
          ColdframePalette(isDark: isDark).background.ignoresSafeArea()
        case .signIn:
          SignInView(presentation: presentation, onSignIn: onSignIn)
        case .signedIn:
          signedIn(isDark: isDark)
        }
      }
      .environment(\.palette, ColdframePalette(isDark: isDark))
      .preferredColorScheme(theme.forcedDark.map { $0 ? .dark : .light })
      .transaction { $0.animation = nil }
    }

    /// No Membership: Create Site replaces the tab shell; "New Site" puts it over the shell.
    /// Add a Hub and Add a Node replace the tab shell while their flow is open (one modal level,
    /// UX-DR76); closing one returns to the tab it was opened from. "Add a Node" on "Hub is
    /// online" is the Hub flow's own action: the core closes that flow and opens this one.
    @ViewBuilder
    private func signedIn(isDark: Bool) -> some View {
      switch sites.surface {
      case .waiting:
        ColdframePalette(isDark: isDark).background.ignoresSafeArea()
      case .failed(let notice):
        SitesFailedView(notice: notice, onTryAgain: sitesActions.load)
      case .createSite(let form):
        CreateSiteView(presentation: form, actions: sitesActions)
      case .garden(_, _) where hubSetup.isOpen:
        AddHubFlowView(presentation: hubSetup, actions: hubSetupActions)
      case .garden(_, _) where nodeSetup.isOpen:
        AddNodeFlowView(presentation: nodeSetup, actions: nodeSetupActions)
      case .garden(let garden, let creating):
        AppTabView(
          theme: theme, onSelectTheme: onSelectTheme, onSignOut: onSignOut, garden: garden,
          sitesActions: sitesActions, lots: lots, lotsActions: lotsActions,
          onAddHub: hubSetupActions.open, onAddNode: nodeSetupActions.open, devices: devices,
          devicesActions: devicesActions, selection: $tab
        )
        .sheet(
          isPresented: Binding(
            get: { creating != nil }, set: { if !$0 { sitesActions.cancelNewSite() } })
        ) {
          if let creating {
            CreateSiteView(presentation: creating, actions: sitesActions)
              .environment(\.palette, ColdframePalette(isDark: isDark))
          }
        }
      }
    }
  }

  /// Observes the services and feeds the root view.
  @MainActor
  public final class ShellModel: ObservableObject {
    @Published public private(set) var presentation = SignInPresentation.restoring
    @Published public private(set) var theme = ThemePreference.system
    @Published public private(set) var sites = SitesPresentation.waiting
    @Published public private(set) var lots = LotsPresentation.waiting
    @Published public private(set) var hubSetup = HubSetupPresentation.closed
    @Published public private(set) var devices = DevicesPresentation.waiting
    @Published public private(set) var nodeSetup = NodeSetupPresentation.closed
    public let signIn: SignInService
    public let appearance: AppearanceService
    public let sitesService: SitesService?
    public let lotsService: LotsService?
    public let hubSetupService: HubSetupService?
    public let devicesService: DevicesService?
    public let nodeSetupService: NodeSetupService?

    public init(
      signIn: SignInService, appearance: AppearanceService, sites: SitesService? = nil,
      lots: LotsService? = nil, hubSetup: HubSetupService? = nil,
      devices: DevicesService? = nil, nodeSetup: NodeSetupService? = nil
    ) {
      self.devicesService = devices
      self.nodeSetupService = nodeSetup
      self.signIn = signIn
      self.appearance = appearance
      self.sitesService = sites
      self.lotsService = lots
      self.hubSetupService = hubSetup
      signIn.observe { [weak self] in self?.presentation = $0 }
      appearance.observe { [weak self] in self?.theme = $0 }
      sites?.observe { [weak self] in self?.sites = $0 }
      lots?.observe { [weak self] in self?.lots = $0 }
      // Entering and leaving stale mode are said once, politely (UX-DR106); the minute tick
      // and an unchanged refresh send no event.
      lots?.observeEvents { event in
        if let text = event.text(.catalogue()) {
          AccessibilityNotification.Announcement(AttributedString(text)).post()
        }
      }
      hubSetup?.observe { [weak self] in self?.hubSetup = $0 }
      devices?.observe { [weak self] in self?.devices = $0 }
      nodeSetup?.observe { [weak self] in self?.nodeSetup = $0 }
    }

    /// The Devices actions for the views; nothing happens without a service.
    public var devicesActions: DevicesActions {
      devicesService.map(DevicesActions.init(service:)) ?? .none
    }

    /// The Add a Hub actions for the views; nothing happens without a service.
    public var hubSetupActions: HubSetupActions {
      hubSetupService.map(HubSetupActions.init(service:)) ?? .none
    }

    /// The Add a Node actions for the views; nothing happens without a service.
    public var nodeSetupActions: NodeSetupActions {
      nodeSetupService.map(NodeSetupActions.init(service:)) ?? .none
    }

    /// The Sites actions for the views; nothing happens without a service.
    public var sitesActions: SitesActions {
      sitesService.map(SitesActions.init(service:)) ?? .none
    }

    /// The Lots actions for the views; nothing happens without a service.
    public var lotsActions: LotsActions {
      lotsService.map(LotsActions.init(service:)) ?? .none
    }
  }
#endif
