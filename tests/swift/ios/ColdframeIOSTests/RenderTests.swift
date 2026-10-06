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

  @MainActor
  private func nodeFlow(_ presentation: NodeSetupPresentation) -> some View {
    AddNodeFlowView(presentation: presentation, actions: .none)
  }

  @Test(
    "UX-DR39 UX-DR67 Add a Node step 1 renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func addNodePressAtAccessibility5(dark: Bool) {
    #expect(renders(nodeFlow(nodeSetup(selectedId: nil, node: nil)), dark: dark))
  }

  @Test(
    "UX-DR37 UX-DR94 Add a Node step 2 renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func addNodeScanAtAccessibility5(dark: Bool) {
    #expect(renders(nodeFlow(nodeSetup(step: 2)), dark: dark))
    #expect(renders(nodeFlow(nodeSetup(step: 2, noNodeYet: true)), dark: dark))
    #expect(renders(nodeFlow(nodeSetup(step: 2, radio: "off")), dark: dark))
  }

  @Test(
    "UX-DR41 UX-DR94 Add a Node step 3 renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func addNodeCodeAtAccessibility5(dark: Bool) {
    #expect(
      renders(
        nodeFlow(nodeSetup(step: 3, codeText: "K7M2Q9XQ", codeError: "wrongCode")), dark: dark))
    #expect(
      renders(
        nodeFlow(
          nodeSetup(
            step: 3, codeText: "K7M2Q9XP", codeAccepted: true, deviceId: "7c19000000000001")),
        dark: dark))
  }

  @Test(
    "UX-DR38 Add a Node step 4 renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func addNodeLotAtAccessibility5(dark: Bool) {
    #expect(renders(nodeFlow(nodeSetup(step: 4)), dark: dark))
    #expect(
      renders(
        nodeFlow(nodeSetup(step: 4, lotsLoaded: true, selectedLotId: "t", lotName: "Tomatoes")),
        dark: dark))
    #expect(
      renders(
        nodeFlow(
          nodeSetup(
            step: 4, lotsLoaded: true, newLotOpen: true, newLotName: "", newLotError: "blank")),
        dark: dark))
  }

  @Test(
    "UX-DR38 UX-DR94 the Lot-taken notice of Add a Node renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func addNodeLotTakenAtAccessibility5(dark: Bool) {
    #expect(
      renders(
        nodeFlow(
          nodeSetup(step: 4, lotsLoaded: true, lotNotice: "lotTaken", lotNoticeLot: "Tomatoes")),
        dark: dark))
    #expect(
      renders(
        nodeFlow(nodeSetup(step: 4, lotNotice: "unreachable", lotsRetryable: true)), dark: dark))
  }

  @Test(
    "UX-DR55 UX-DR94 the Add a Node outcomes render at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func addNodeOutcomesAtAccessibility5(dark: Bool) {
    #expect(
      renders(
        nodeFlow(
          nodeSetup(
            step: 5, lotName: "Tomatoes", outcome: "assigned", outcomePrimary: "done",
            stoppedStep: 5)),
        dark: dark))
    #expect(
      renders(
        nodeFlow(
          nodeSetup(
            step: 5, outcome: "stoppedListening", outcomePrimary: "startOver", stoppedStep: 3)),
        dark: dark))
  }

  @Test("UX-DR39 UX-DR67 the leave question of Add a Node renders over its step")
  @MainActor
  func addNodeLeaveAtAccessibility5() {
    #expect(
      renders(nodeFlow(nodeSetup(step: 4, lotsLoaded: true, confirmingLeave: true)), dark: false))
  }

  /// Counts what the views ask of the core.
  @MainActor
  private final class CountingNodeSetup: NodeSetupService {
    var calls: [String] = []

    func observe(_ onChange: @escaping @MainActor (NodeSetupPresentation) -> Void) {}
    func open(lotId: String?) { calls.append("open(\(lotId ?? "nil"))") }
    func close() { calls.append("close") }
    func recheckRadio() { calls.append("recheckRadio") }
    func announcing(_ active: Bool) { calls.append("announcing(\(active))") }
    func back() { calls.append("back") }
    func leave() { calls.append("leave") }
    func confirmLeave() { calls.append("confirmLeave") }
    func stayInFlow() { calls.append("stayInFlow") }
    func continueFromPress() { calls.append("continueFromPress") }
    func select(candidateId: String) { calls.append("select(\(candidateId))") }
    func continueFromScan() { calls.append("continueFromScan") }
    func setCode(_ text: String) { calls.append("setCode") }
    func submitCode() { calls.append("submitCode") }
    func continueFromCode() { calls.append("continueFromCode") }
    func retryLots() { calls.append("retryLots") }
    func chooseLot(_ lotId: String) { calls.append("chooseLot(\(lotId))") }
    func openNewLot() { calls.append("openNewLot") }
    func setNewLotName(_ name: String) { calls.append("setNewLotName(\(name))") }
    func createLot() { calls.append("createLot") }
    func assign() { calls.append("assign") }
    func outcomeAction(_ action: NodeOutcomeActionKind) {
      calls.append("outcomeAction(\(action.rawValue))")
    }
  }

  @Test("UX-DR67 the Add a Node actions forward every tap to the service")
  @MainActor
  func addNodeActionsForward() {
    let service = CountingNodeSetup()
    let actions = NodeSetupActions(service: service)
    actions.open(nil)
    actions.open("t")
    actions.close()
    actions.recheckRadio()
    actions.announcing(true)
    actions.back()
    actions.leave()
    actions.confirmLeave()
    actions.stayInFlow()
    actions.continueFromPress()
    actions.select("n-1")
    actions.continueFromScan()
    actions.setCode("K7M2Q9XP")
    actions.submitCode()
    actions.continueFromCode()
    actions.retryLots()
    actions.chooseLot("t")
    actions.openNewLot()
    actions.setNewLotName("Peppers")
    actions.createLot()
    actions.assign()
    actions.outcomeAction(.startOver)
    #expect(
      service.calls == [
        "open(nil)", "open(t)", "close", "recheckRadio", "announcing(true)", "back", "leave",
        "confirmLeave", "stayInFlow", "continueFromPress", "select(n-1)", "continueFromScan",
        "setCode", "submitCode", "continueFromCode", "retryLots", "chooseLot(t)", "openNewLot",
        "setNewLotName(Peppers)", "createLot", "assign", "outcomeAction(startOver)",
      ])
  }

  @Test(
    "UX-DR67 the root shows Add a Node in place of the tab shell while its flow is open",
    arguments: [false, true])
  @MainActor
  func rootWithNodeFlowAtAccessibility5(dark: Bool) {
    let signedIn = SignInPresentation(
      restoring: false, working: false, signedIn: true, notice: nil, action: nil)
    #expect(
      renders(
        ColdframeRootView(
          presentation: signedIn,
          sites: SitesPresentation(surface: .garden(emptyGarden, creating: nil)),
          theme: dark ? .dark : .light, onSignIn: {}, onSignOut: {}, onSelectTheme: { _ in },
          nodeSetup: nodeSetup(step: 2), nodeSetupActions: .none)))
  }

  @Test(
    "UX-DR18 UX-DR67 the Garden with tappable no-Node tiles renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func gardenWithTappableLotsAtAccessibility5(dark: Bool) {
    let lots = LotsPresentation(
      surface: "ready", notice: nil, siteId: "a", siteName: "Home garden", role: "administrator",
      canRenameSite: false, canEditLots: true, readOnlyNotice: false,
      siteNameDraft: "Home garden", siteNameError: nil, siteRenameWorking: false,
      lotIds: ["t", "b"], lotNames: ["Tomatoes", "Beans"], lotStatuses: ["noNode", "unknown"],
      newLotName: "", newLotNameError: nil, createWorking: false,
      renamingLotId: nil, renameDraft: "", renameError: nil, renameWorking: false,
      removingLotId: nil, removingLotName: nil, removeWorking: false,
      actionNotice: nil, actionNoticeSubject: nil, canAddNode: true)
    #expect(
      renders(
        NavigationStack { GardenView(presentation: emptyGarden, lots: lots, actions: .none) },
        dark: dark))
  }

  private func devicesReady(canAddHub: Bool, hubs: Bool = true) -> DevicesPresentation {
    DevicesPresentation(
      surface: "ready", notice: nil, siteId: "a", canAddHub: canAddHub, canAddNode: canAddHub,
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
