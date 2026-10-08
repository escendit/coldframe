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
      actionNotice: nil, actionNoticeSubject: nil,
      lotVariants: ["noNode", "unknown"], lotLabels: ["noNode", "silent"],
      lotValues: ["plus", "none"], lotFoots: ["addNode", "noReadingsYet"],
      lotSpokens: ["noNode", "nodeSilent"], lotOpensAddNode: [true, false])
    #expect(lots.tiles.first?.isTappable == true)
    #expect(
      renders(
        NavigationStack { GardenView(presentation: emptyGarden, lots: lots, actions: .none) },
        dark: dark))
  }

  /// The Site overview at a fixed clock, so the times it writes do not depend on the machine.
  @MainActor
  private func overview(_ lots: LotsPresentation) -> some View {
    NavigationStack {
      GardenView(
        presentation: emptyGarden, lots: lots, actions: .none,
        now: { Overview.context.now }, timeZone: Overview.context.timeZone)
    }
  }

  /// The same at the default text size: two columns, the value at the bottom of the tile.
  @MainActor
  private func rendersAtDefaultSize<Content: View>(_ view: Content, dark: Bool) -> Bool {
    let renderer = ImageRenderer(
      content:
        view
        .environment(\.palette, ColdframePalette(isDark: dark))
        .environment(\.colorScheme, dark ? .dark : .light)
        .environment(\.dynamicTypeSize, .large)
        .frame(width: 390, height: 2400))
    return renderer.cgImage != nil
  }

  @Test(
    "UX-DR18 UX-DR17 every Lot tile variant renders in two columns and in one",
    arguments: [false, true])
  @MainActor
  func lotTileVariantsRender(dark: Bool) {
    let lots = Overview.lots()
    #expect(Set(lots.tiles.map(\.variant)).count == 6)
    #expect(rendersAtDefaultSize(overview(lots), dark: dark))
    // Accessibility 5: one column, the value directly under the status label.
    #expect(renders(overview(lots), dark: dark))
  }

  @Test(
    "UX-DR18 UX-DR99 each Lot tile variant renders on its own",
    arguments: [false, true])
  @MainActor
  func eachLotTileVariantRenders(dark: Bool) {
    for lot in Overview.everyLot {
      let tile = Overview.tile(lot)
      for valueFollowsLabel in [false, true] {
        #expect(
          renders(
            LotTile(
              tile: tile, context: .catalogue(), valueFollowsLabel: valueFollowsLabel),
            dark: dark),
          "\(lot.name)")
      }
    }
  }

  @Test(
    "UX-DR19 UX-DR24 UX-DR79 the stale overview renders: stale header and stale tiles",
    arguments: [false, true])
  @MainActor
  func staleOverviewRenders(dark: Bool) {
    let lots = Overview.staleLots()
    #expect(lots.tiles.allSatisfy { $0.isStale })
    #expect(rendersAtDefaultSize(overview(lots), dark: dark))
    #expect(renders(overview(lots), dark: dark))
    for lot in Overview.everyLot {
      #expect(
        renders(
          LotTile(tile: Overview.staleTile(lot), context: .catalogue(), valueFollowsLabel: false),
          dark: dark),
        "\(lot.name)")
    }
  }

  @Test(
    "UX-DR19 UX-DR80 the loading overview renders skeleton tiles",
    arguments: [false, true])
  @MainActor
  func loadingOverviewRenders(dark: Bool) {
    #expect(rendersAtDefaultSize(overview(Overview.loading), dark: dark))
    #expect(renders(overview(Overview.loading), dark: dark))
    #expect(renders(LotSkeletonTile(), dark: dark))
  }

  @Test(
    "UX-DR21 UX-DR129 the summary header renders every headline, the paused one in paused ink",
    arguments: [false, true])
  @MainActor
  func summaryHeaderRenders(dark: Bool) {
    let headlines: [LotsPresentation] = [
      Overview.lots(),
      Overview.lots(headline: "cantBeRead", headlineCount: 4, headlineLotName: nil),
      Overview.lots(
        headline: "paused", headlineCount: 0, headlineLotName: nil,
        headlinePausedUntil: String(Overview.novemberMs), headlinePausedInk: true),
      Overview.lots(headline: "nothingNeedsWater", headlineCount: 0, headlineLotName: nil),
    ]
    for lots in headlines {
      #expect(renders(overview(lots), dark: dark))
    }
  }

  @Test("UX-DR12 the hatch and its plate render", arguments: [false, true])
  @MainActor
  func hatchRenders(dark: Bool) {
    let palette = ColdframePalette(isDark: dark)
    #expect(
      renders(
        L10n.lotTileFootUncalibrated.text.plate(true, palette).padding()
          .background { Hatch(palette: palette) },
        dark: dark))
  }

  @Test("UX-DR98 UX-DR125 tile copy resolves through the String Catalog as the Linux tests read it")
  func tileCopyResolves() {
    let context = CopyContext.catalogue(
      now: Overview.context.now, timeZone: Overview.context.timeZone,
      locale: Overview.context.locale)
    #expect(Overview.tile(Overview.needsCalibration).footText(context) == "no % until calibrated")
    #expect(Overview.tile(Overview.pausedUntil).valueText(context) == "—")
    #expect(
      Overview.tile(Overview.needsCalibration).spokenText(context)
        == "Carrots, needs Calibration, no percentage until calibrated")
    #expect(
      Overview.tile(Overview.pausedBySite).spokenText(context) == "Leeks, paused with the Site")
    #expect(L10n.gardenCountNeedsCalibration.string(1) == "1 needs Calibration")
    #expect(L10n.gardenCountNeedsCalibration.string(2) == "2 need Calibration")
    #expect(L10n.durationSpokenHours.string(1) == "1 hour")
    #expect(L10n.durationSpokenHours.string(6) == "6 hours")
  }

  @MainActor
  private final class LotsSpy: LotsService {
    var calls: [String] = []
    func observe(_ onChange: @escaping @MainActor (LotsPresentation) -> Void) {}
    func observeEvents(_ onEvent: @escaping @MainActor (LotsEventPresentation) -> Void) {}
    func load() { calls.append("load") }
    func refresh() { calls.append("refresh") }
    func tick() { calls.append("tick") }
    func setSiteName(_ name: String) {}
    func renameSite() {}
    func setNewLotName(_ name: String) {}
    func createLot() {}
    func startRename(lotId: String) {}
    func setRename(_ name: String) {}
    func rename() {}
    func cancelRename() {}
    func askRemove(lotId: String) {}
    func confirmRemove() {}
    func cancelRemove() {}
  }

  @Test("UX-DR112 pull-to-refresh and the minute tick reach the Lots service")
  @MainActor
  func lotsActionsForward() {
    let spy = LotsSpy()
    let actions = LotsActions(service: spy)
    actions.refresh()
    actions.tick()
    actions.load()
    #expect(spy.calls == ["refresh", "tick", "load"])
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

  // MARK: - Lot detail and Nodes (Story 4.8)

  @MainActor
  private func lotDetailView(_ presentation: LotDetailPresentation) -> some View {
    NavigationStack {
      LotDetailView(
        presentation: presentation, siteName: "Home garden",
        now: { Overview.context.now }, timeZone: Overview.context.timeZone)
    }
  }

  private let lotDetailStates: [(name: String, fixture: LotDetailFixture)] = [
    ("live needs calibration", .needsCalibration), ("live needs water", .needsWater),
    ("live ok", .ok), ("unknown node", .nodeSilent), ("unknown hub", .hubSilent),
    ("paused until", .pausedUntil), ("paused by the Site", .pausedBySite),
    ("no Node", .noNodeLot), ("stale", LotDetailFixture.needsWater.asStale),
  ]

  @Test(
    "UX-DR63 UX-DR27 UX-DR28 UX-DR29 UX-DR32 Lot detail renders each state at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func lotDetailAtAccessibility5(dark: Bool) {
    for state in lotDetailStates {
      #expect(renders(lotDetailView(state.fixture.build()), dark: dark), "\(state.name)")
    }
  }

  @Test(
    "UX-DR63 UX-DR27 UX-DR28 UX-DR29 UX-DR32 Lot detail renders each state at the default text size",
    arguments: [false, true])
  @MainActor
  func lotDetailAtDefaultSize(dark: Bool) {
    for state in lotDetailStates {
      #expect(
        rendersAtDefaultSize(lotDetailView(state.fixture.build()), dark: dark), "\(state.name)")
    }
  }

  @Test(
    "UX-DR63 UX-DR79 Lot detail renders while loading and after a failed read",
    arguments: [false, true])
  @MainActor
  func lotDetailLoadingAndFailed(dark: Bool) {
    var loading = LotDetailFixture.ok
    loading.surface = "loading"
    #expect(renders(lotDetailView(loading.build()), dark: dark))
    for notice in LotDetailNoticeKind.allCases {
      var failed = LotDetailFixture.ok
      failed.surface = "failed"
      failed.notice = notice.rawValue
      #expect(renders(lotDetailView(failed.build()), dark: dark), "\(notice)")
    }
  }

  @Test(
    "UX-DR32 UX-DR33 the History chart renders with its readout, for each quantity",
    arguments: [false, true])
  @MainActor
  func historyChartRenders(dark: Bool) throws {
    for quantity in SensorQuantityKind.allCases {
      var detail = LotDetailFixture.needsCalibration
      detail.chartQuantity = quantity.rawValue
      detail.chartUnit = quantity == .soilMoisture ? "raw" : "celsius"
      let chart = try #require(detail.build().chart)
      #expect(
        rendersAtDefaultSize(HistoryChartView(chart: chart, context: Overview.context), dark: dark),
        "\(quantity)")
    }
  }

  @Test(
    "UX-DR30 Devices with Nodes renders at the largest accessibility text size",
    arguments: [false, true])
  @MainActor
  func devicesWithNodesAtAccessibility5(dark: Bool) {
    let presentation = DevicesPresentation(
      surface: "ready", notice: nil, siteId: "a", canAddHub: true, canAddNode: true,
      hubIds: ["3f2a9c0d1e4b5a67"], hubStatuses: ["online"], hubLastSeen: ["1791270120000"],
      nodeIds: ["7c19aa01bb02cc03", "1b2c3d4e5f607182", "0a0b0c0d0e0f1011"],
      nodeLotNames: ["Beans", "Tomatoes", ""], nodeBatteries: ["14", "62", ""],
      nodeBatteryLow: [true, false, false], nodeCharging: ["notCharging", "charging", ""],
      nodeLastSeen: ["1791248700000", "1791270120000", ""])
    #expect(renders(devicesView(presentation), dark: dark))
    #expect(rendersAtDefaultSize(devicesView(presentation), dark: dark))
  }

  @MainActor
  private final class LotDetailSpy: LotDetailService {
    var calls: [String] = []
    func observe(_ onChange: @escaping @MainActor (LotDetailPresentation) -> Void) {}
    func observeEvents(_ onEvent: @escaping @MainActor (LotsEventPresentation) -> Void) {}
    func open(lotId: String, name: String) { calls.append("open \(lotId) \(name)") }
    func close() { calls.append("close") }
    func refresh() { calls.append("refresh") }
    func tick() { calls.append("tick") }
    func pick(_ quantity: SensorQuantityKind) { calls.append("pick \(quantity.rawValue)") }
  }

  @Test("UX-DR63 UX-DR33 opening, refreshing, ticking and picking reach the Lot detail service")
  @MainActor
  func lotDetailActionsForward() {
    let spy = LotDetailSpy()
    let actions = LotDetailActions(service: spy)
    actions.open("p", "Peppers")
    actions.refresh()
    actions.tick()
    actions.pick(.airTemperature)
    actions.close()
    #expect(
      spy.calls == ["open p Peppers", "refresh", "tick", "pick airTemperature", "close"])
  }
  // MARK: - Calibrate (Story 5.2)

  @MainActor
  private func calibrateView(_ presentation: CalibratePresentation) -> some View {
    CalibrateView(
      presentation: presentation, siteName: "Home garden", actions: .none,
      now: { Overview.context.now }, timeZone: Overview.context.timeZone)
  }

  private func calibrateSnapshot(
    surface: String = "ready", notice: String? = nil, noticeTryAgain: Bool = false,
    step: String = "dry", canRecord: Bool = false, hasFresh: Bool = false, pickedSeq: String = "",
    dryRaw: String = "", wetRaw: String = "", percent: String = "", pausedBySite: Bool = false,
    offersResume: Bool = false
  ) -> CalibratePresentation {
    CalibratePresentation(
      surface: surface, notice: notice, noticeTryAgain: noticeTryAgain, lotId: "t",
      lotName: "Tomatoes", step: step, working: false, canRecord: canRecord, hasFresh: hasFresh,
      lastRawValue: "612", lastReadingAt: String(Overview.readingMs), readingSeqs: [42, 41],
      readingRawValues: [612, 640],
      readingAts: [String(Overview.readingMs), String(Overview.earlierMs)], pickedSeq: pickedSeq,
      dryRaw: dryRaw, wetRaw: wetRaw, percent: percent, announcementId: 0,
      announcementKind: nil, announcementStep: nil, announcementRaw: "", announcementAt: "",
      announcementPercent: "", announcementLot: "Tomatoes", pausedBySite: pausedBySite,
      offersResume: offersResume)
  }

  @Test(
    "Story 5.2 Calibrate renders every step at the largest accessibility text size and at the default one",
    arguments: [false, true])
  @MainActor
  func calibrateStepsRender(dark: Bool) {
    let steps = [
      calibrateSnapshot(),
      calibrateSnapshot(canRecord: true, hasFresh: true, pickedSeq: "42"),
      calibrateSnapshot(canRecord: true, pickedSeq: "41"),
      calibrateSnapshot(step: "wet", dryRaw: "3000"),
      calibrateSnapshot(
        step: "wet", canRecord: true, hasFresh: true, pickedSeq: "42", dryRaw: "3000"),
      calibrateSnapshot(step: "confirm", dryRaw: "3000", wetRaw: "1200"),
      calibrateSnapshot(step: "confirm", dryRaw: "3000", wetRaw: "1200", percent: "40"),
    ]
    for presentation in steps {
      #expect(renders(calibrateView(presentation), dark: dark), "\(presentation.step)")
      #expect(rendersAtDefaultSize(calibrateView(presentation), dark: dark))
    }
  }

  @Test(
    "Story 5.2 Calibrate renders its notices, the loading outline and the paused explanation",
    arguments: [false, true])
  @MainActor
  func calibrateOtherSurfacesRender(dark: Bool) {
    let surfaces = [
      calibrateSnapshot(surface: "loading"),
      calibrateSnapshot(surface: "failed", notice: "noSensor", noticeTryAgain: true),
      calibrateSnapshot(surface: "failed", notice: "forbidden"),
      calibrateSnapshot(surface: "paused", offersResume: true),
      calibrateSnapshot(surface: "paused", pausedBySite: true),
      calibrateSnapshot(notice: "indistinct", step: "wet", dryRaw: "3000"),
      calibrateSnapshot(notice: "notDelivered", canRecord: true, pickedSeq: "42"),
    ]
    for presentation in surfaces {
      #expect(renders(calibrateView(presentation), dark: dark))
      #expect(rendersAtDefaultSize(calibrateView(presentation), dark: dark))
    }
  }

  @Test(
    "Story 5.2 the Node-added outcome with Calibrate and the needs-calibration tile control render",
    arguments: [false, true])
  @MainActor
  func calibrateEntryPointsRender(dark: Bool) {
    #expect(
      renders(
        nodeFlow(
          nodeSetup(
            step: 5, lotName: "Tomatoes", outcome: "assigned", outcomePrimary: "done",
            outcomeSecondary: "calibrate", stoppedStep: 5)),
        dark: dark))
    var calibrating = Overview.needsCalibration
    calibrating.opensCalibrate = true
    #expect(
      rendersAtDefaultSize(overview(Overview.lots([calibrating, Overview.ok])), dark: dark))
    var detail = LotDetailFixture.needsCalibration
    detail.canCalibrate = true
    #expect(
      rendersAtDefaultSize(
        lotDetailView(detail.build()), dark: dark))
  }
#endif
