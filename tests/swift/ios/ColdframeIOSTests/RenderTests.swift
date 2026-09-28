#if canImport(SwiftUI)
  import SwiftUI
  import Testing

  @testable import ColdframeIOS

  @MainActor
  private func renders<Content: View>(_ view: Content) -> Bool {
    let renderer = ImageRenderer(
      content:
        view
        .environment(\.dynamicTypeSize, .accessibility5)
        .frame(width: 390, height: 1400))
    return renderer.cgImage != nil
  }

  @Test("UX-DR126 the Sign-in surface renders at the largest accessibility text size")
  @MainActor
  func signInAtAccessibility5() {
    let presentation = SignInPresentation(
      restoring: false, working: false, signedIn: false, notice: "certificate", action: nil)
    #expect(renders(SignInView(presentation: presentation, onSignIn: {})))
  }

  @Test("UX-DR126 UX-DR34 the working label renders at the largest accessibility text size")
  @MainActor
  func workingAtAccessibility5() {
    let presentation = SignInPresentation(
      restoring: false, working: true, signedIn: false, notice: "unreachable", action: "tryAgain")
    #expect(renders(SignInView(presentation: presentation, onSignIn: {})))
  }

  @Test("UX-DR109 the tab shell renders at the largest accessibility text size")
  @MainActor
  func shellAtAccessibility5() {
    #expect(renders(AppTabView(theme: .system, onSelectTheme: { _ in }, onSignOut: {})))
  }

  @Test("UX-DR75 UX-DR36 Appearance renders at the largest accessibility text size")
  @MainActor
  func appearanceAtAccessibility5() {
    #expect(renders(NavigationStack { AppearanceView(theme: .dark, onSelectTheme: { _ in }) }))
  }

  @Test("UX-DR35 the text input renders its label, helper and error")
  @MainActor
  func textInputRenders() {
    let field = TextInputField(
      label: "Site name", value: .constant("Home"), helper: nil, error: "A Site needs a name.")
    #expect(renders(field))
  }

  @Test("UX-DR125 counted strings resolve through the catalogue's plural rules")
  func pluralsResolve() {
    #expect(L10n.countLotsNeedWater.string(1) == "1 Lot needs water")
    #expect(L10n.countLotsNeedWater.string(5) == "5 Lots need water")
  }

  @Test("UX-DR104 a notice posts its announcement when it appears")
  @MainActor
  func noticeRenders() {
    #expect(renders(InlineNotice(message: .noticeKeycloak, announcement: .assertive)))
  }
  @MainActor
  private func renders<Content: View>(_ view: Content, dark: Bool) -> Bool {
    renders(
      view.environment(\.palette, ColdframePalette(isDark: dark))
        .environment(\.colorScheme, dark ? .dark : .light))
  }

  private let createSite = CreateSitePresentation(
    name: "Home", nameError: .tooLong, working: false, notice: .identityProviderUnavailable,
    cancellable: true,
    timeZone: TimeZonePanelPresentation(detected: "Europe/Zurich", chosen: nil, changing: false))

  private let emptyGarden = GardenPresentation(
    siteName: "Home garden", role: .member,
    tiles: FirstRunStepKind.allCases.enumerated().map {
      FirstRunTilePresentation(step: $1, number: $0 + 1, state: $0 == 0 ? .next : .later)
    },
    tilesActionable: false,
    switcherRows: [
      SiteSwitcherRow(siteId: "a", name: "Home garden", role: .member, isSelected: true),
      .newSite,
    ],
    menuItems: [.siteSettings], menuEnabled: true, showsMemberNotice: true)

  @Test(
    "UX-DR61 Create Site renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func createSiteAtAccessibility5(dark: Bool) {
    #expect(renders(CreateSiteView(presentation: createSite, actions: .none), dark: dark))
  }

  @Test(
    "UX-DR21 UX-DR54 UX-DR62 UX-DR82 the empty Garden renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func gardenAtAccessibility5(dark: Bool) {
    #expect(
      renders(
        NavigationStack { GardenView(presentation: emptyGarden, actions: .none) { _ in } },
        dark: dark))
  }

  @Test("UX-DR23 the Site switcher renders at the largest accessibility text size")
  @MainActor
  func switcherAtAccessibility5() {
    #expect(
      renders(SiteSwitcherSheet(rows: emptyGarden.switcherRows, onSelect: { _ in }, onNewSite: {})))
  }
#endif
