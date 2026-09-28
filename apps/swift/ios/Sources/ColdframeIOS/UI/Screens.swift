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
  /// The Garden tab shows the current Site's empty Garden (Story 1.8).
  public struct AppTabView: View {
    let theme: ThemePreference
    let onSelectTheme: (ThemePreference) -> Void
    let onSignOut: () -> Void
    let garden: GardenPresentation?
    let sitesActions: SitesActions
    @State private var selection: AppTab = .garden
    @Environment(\.palette) private var palette

    public init(
      theme: ThemePreference, onSelectTheme: @escaping (ThemePreference) -> Void,
      onSignOut: @escaping () -> Void, garden: GardenPresentation? = nil,
      sitesActions: SitesActions = .none
    ) {
      self.theme = theme
      self.onSelectTheme = onSelectTheme
      self.onSignOut = onSignOut
      self.garden = garden
      self.sitesActions = sitesActions
    }

    public var body: some View {
      TabView(selection: $selection) {
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
    }

    @ViewBuilder
    private func content(for tab: AppTab) -> some View {
      switch tab {
      case .settings:
        SettingsView(theme: theme, onSelectTheme: onSelectTheme, onSignOut: onSignOut)
      case .garden:
        if let garden {
          GardenView(presentation: garden, actions: sitesActions) { selection = $0 }
        } else {
          palette.background.ignoresSafeArea()
        }
      default:
        // Alerts and Devices carry their heading only until their stories.
        palette.background.ignoresSafeArea()
      }
    }
  }

  /// Settings index (UX-DR71): Appearance, then Account with Sign out, confirmed in a native
  /// dialog that names the result (UX-DR113).
  public struct SettingsView: View {
    let theme: ThemePreference
    let onSelectTheme: (ThemePreference) -> Void
    let onSignOut: () -> Void
    @State private var confirmingSignOut = false
    @Environment(\.palette) private var palette

    public init(
      theme: ThemePreference, onSelectTheme: @escaping (ThemePreference) -> Void,
      onSignOut: @escaping () -> Void
    ) {
      self.theme = theme
      self.onSelectTheme = onSelectTheme
      self.onSignOut = onSignOut
    }

    public var body: some View {
      let confirmation = ConfirmationPresentation.signOut
      List {
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
    let theme: ThemePreference
    let onSignIn: () -> Void
    let onSignOut: () -> Void
    let onSelectTheme: (ThemePreference) -> Void
    @Environment(\.colorScheme) private var systemScheme

    public init(
      presentation: SignInPresentation, sites: SitesPresentation = .waiting,
      sitesActions: SitesActions = .none, theme: ThemePreference,
      onSignIn: @escaping () -> Void, onSignOut: @escaping () -> Void,
      onSelectTheme: @escaping (ThemePreference) -> Void
    ) {
      self.presentation = presentation
      self.sites = sites
      self.sitesActions = sitesActions
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
    @ViewBuilder
    private func signedIn(isDark: Bool) -> some View {
      switch sites.surface {
      case .waiting:
        ColdframePalette(isDark: isDark).background.ignoresSafeArea()
      case .failed(let notice):
        SitesFailedView(notice: notice, onTryAgain: sitesActions.load)
      case .createSite(let form):
        CreateSiteView(presentation: form, actions: sitesActions)
      case .garden(let garden, let creating):
        AppTabView(
          theme: theme, onSelectTheme: onSelectTheme, onSignOut: onSignOut, garden: garden,
          sitesActions: sitesActions
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
    public let signIn: SignInService
    public let appearance: AppearanceService
    public let sitesService: SitesService?

    public init(
      signIn: SignInService, appearance: AppearanceService, sites: SitesService? = nil
    ) {
      self.signIn = signIn
      self.appearance = appearance
      self.sitesService = sites
      signIn.observe { [weak self] in self?.presentation = $0 }
      appearance.observe { [weak self] in self?.theme = $0 }
      sites?.observe { [weak self] in self?.sites = $0 }
    }

    /// The Sites actions for the views; nothing happens without a service.
    public var sitesActions: SitesActions {
      sitesService.map(SitesActions.init(service:)) ?? .none
    }
  }
#endif
