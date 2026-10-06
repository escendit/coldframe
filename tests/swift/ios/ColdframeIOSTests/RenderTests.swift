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
        NavigationStack { GardenView(presentation: emptyGarden, actions: .none) },
        dark: dark))
  }

  private let lotsReady = LotsPresentation(
    surface: "ready", notice: nil, siteId: "a", siteName: "Home garden", role: "owner",
    canRenameSite: true, canEditLots: true, readOnlyNotice: false,
    siteNameDraft: "Home garden", siteNameError: nil, siteRenameWorking: false,
    lotIds: ["t", "b"], lotNames: ["Tomatoes", "Beans"], lotStatuses: ["noNode", "noNode"],
    newLotName: "Peppers", newLotNameError: "blank", createWorking: true,
    renamingLotId: nil, renameDraft: "", renameError: nil, renameWorking: false,
    removingLotId: nil, removingLotName: nil, removeWorking: false,
    actionNotice: "lotClaimed", actionNoticeSubject: "Tomatoes")

  private let lotsMember = LotsPresentation(
    surface: "ready", notice: nil, siteId: "a", siteName: "Home garden", role: "member",
    canRenameSite: false, canEditLots: false, readOnlyNotice: true,
    siteNameDraft: "Home garden", siteNameError: nil, siteRenameWorking: false,
    lotIds: ["t", "b"], lotNames: ["Tomatoes", "Beans"], lotStatuses: ["noNode", "unknown"],
    newLotName: "", newLotNameError: nil, createWorking: false,
    renamingLotId: nil, renameDraft: "", renameError: nil, renameWorking: false,
    removingLotId: nil, removingLotName: nil, removeWorking: false,
    actionNotice: nil, actionNoticeSubject: nil)

  @Test(
    "UX-DR18 UX-DR20 the Garden with Lots renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func gardenWithLotsAtAccessibility5(dark: Bool) {
    #expect(
      renders(
        NavigationStack {
          GardenView(presentation: emptyGarden, lots: lotsReady, actions: .none)
        },
        dark: dark))
  }

  @Test(
    "UX-DR74 Site settings renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func siteSettingsAtAccessibility5(dark: Bool) {
    #expect(
      renders(
        NavigationStack { SiteSettingsView(presentation: lotsReady, actions: .none) },
        dark: dark))
  }

  @Test("UX-DR84 Site settings for a Member renders at the largest accessibility text size")
  @MainActor
  func siteSettingsMemberAtAccessibility5() {
    #expect(
      renders(
        NavigationStack { SiteSettingsView(presentation: lotsMember, actions: .none) },
        dark: false))
  }

  @Test("UX-DR23 the Site switcher renders at the largest accessibility text size")
  @MainActor
  func switcherAtAccessibility5() {
    #expect(
      renders(SiteSwitcherSheet(rows: emptyGarden.switcherRows, onSelect: { _ in }, onNewSite: {})))
  }

  @MainActor
  private func flow(_ presentation: HubSetupPresentation) -> some View {
    AddHubFlowView(presentation: presentation, actions: .none)
  }

  @Test(
    "UX-DR39 UX-DR37 UX-DR94 Add a Hub step 1 renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func addHubScanAtAccessibility5(dark: Bool) {
    #expect(renders(flow(hubSetup(noHubYet: true)), dark: dark))
    #expect(renders(flow(hubSetup(radio: "off")), dark: dark))
  }

  @Test(
    "UX-DR41 UX-DR95 Add a Hub step 2 renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func addHubCodeAtAccessibility5(dark: Bool) {
    #expect(
      renders(flow(hubSetup(step: 2, codeText: "K7M2Q9XQ", codeError: "wrongCode")), dark: dark))
    #expect(
      renders(
        flow(
          hubSetup(step: 2, codeText: "K7M2Q9XP", codeAccepted: true, deviceId: "3f2a9c01b2d4e6f8")),
        dark: dark))
  }

  @Test(
    "UX-DR42 Add a Hub step 3 renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func addHubWifiAtAccessibility5(dark: Bool) {
    #expect(renders(flow(hubSetup(step: 3, networksLoaded: true, ssid: "Novak-Home")), dark: dark))
  }

  @Test(
    "UX-DR66 Add a Hub step 4 renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func addHubSiteAtAccessibility5(dark: Bool) {
    #expect(
      renders(
        flow(hubSetup(step: 4, fingerprint: String(repeating: "7b12", count: 16))), dark: dark))
  }

  @Test(
    "UX-DR40 Add a Hub step 5 renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func addHubProgressAtAccessibility5(dark: Bool) {
    #expect(renders(flow(hubSetup(step: 5, progressReached: 2, elapsedSeconds: 23)), dark: dark))
  }

  @Test(
    "UX-DR55 UX-DR95 the Add a Hub outcomes render at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func addHubOutcomesAtAccessibility5(dark: Bool) {
    #expect(
      renders(
        flow(hubSetup(step: 5, ssid: "Novak-Home", progressReached: 4, outcome: "online")),
        dark: dark))
    #expect(
      renders(
        flow(
          hubSetup(
            step: 5, ssid: "Novak-Home", outcome: "wrongPassword",
            outcomePrimary: "reenterPassword", outcomeSecondary: "otherNetwork")),
        dark: dark))
  }

  private func devicesReady(canAddHub: Bool, hubs: Bool = true) -> DevicesPresentation {
    DevicesPresentation(
      surface: "ready", notice: nil, siteId: "a", canAddHub: canAddHub,
      hubIds: hubs ? ["1b00aa11bb22cc33", "3f2a9c0d1e4b5a67", "7c19000000000001"] : [],
      hubStatuses: hubs ? ["offline", "online", "offline"] : [],
      hubLastSeen: hubs ? ["1791268800000", "1791270120000", ""] : [])
  }

  @MainActor
  private func devicesView(_ presentation: DevicesPresentation) -> some View {
    NavigationStack {
      DevicesView(
        presentation: presentation, now: { Date(timeIntervalSince1970: 1_791_270_240) },
        timeZone: TimeZone(identifier: "UTC") ?? .gmt)
    }
  }

  @Test(
    "UX-DR30 UX-DR65 Devices with online and offline Hubs renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func devicesAtAccessibility5(dark: Bool) {
    #expect(renders(devicesView(devicesReady(canAddHub: true)), dark: dark))
  }

  @Test(
    "UX-DR30 Devices without Devices renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func devicesEmptyAtAccessibility5(dark: Bool) {
    #expect(renders(devicesView(devicesReady(canAddHub: true, hubs: false)), dark: dark))
  }

  @Test(
    "UX-DR84 Devices for a Member renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func devicesMemberAtAccessibility5(dark: Bool) {
    #expect(renders(devicesView(devicesReady(canAddHub: false)), dark: dark))
  }

  @Test(
    "UX-DR65 Devices after a failed load renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func devicesFailedAtAccessibility5(dark: Bool) {
    let failed = DevicesPresentation(
      surface: "failed", notice: "unreachable", siteId: "a", canAddHub: true,
      hubIds: [], hubStatuses: [], hubLastSeen: [])
    #expect(renders(devicesView(failed), dark: dark))
  }

  @Test("UX-DR65 the tab shell with Devices renders at the largest accessibility text size")
  @MainActor
  func shellWithDevicesAtAccessibility5() {
    #expect(
      renders(
        AppTabView(
          theme: .system, onSelectTheme: { _ in }, onSignOut: {},
          devices: devicesReady(canAddHub: true), selection: .constant(.devices))))
  }
#endif
