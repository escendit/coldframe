import Testing

@testable import ColdframeIOS

/// A snapshot as the core flattens it, with every field defaulted to an empty Site "Home".
private func snapshot(
  surface: String = "ready", notice: String? = nil,
  siteIds: [String] = ["a"], siteNames: [String] = ["Home"], siteRoles: [String] = ["owner"],
  currentId: String? = "a", currentName: String? = "Home", currentRole: String? = "owner",
  formShown: Bool = false, formName: String = "", formNameError: String? = nil,
  formWorking: Bool = false, formNotice: String? = nil, formCancellable: Bool = false,
  timeZoneDetected: String = "Europe/Zurich", timeZoneChosen: String? = nil,
  timeZoneChanging: Bool = false,
  steps: [String] = ["addHub", "addNode", "calibrate", "setLowThreshold"],
  stepStates: [String] = ["next", "later", "later", "later"],
  stepsActionable: Bool = false, memberNotice: Bool = false,
  menuItems: [String] = ["siteSettings"], menuEnabled: Bool = true
) -> SitesPresentation {
  SitesPresentation(
    surface: surface, notice: notice, siteIds: siteIds, siteNames: siteNames,
    siteRoles: siteRoles, currentId: currentId, currentName: currentName,
    currentRole: currentRole, formShown: formShown, formName: formName,
    formNameError: formNameError, formWorking: formWorking, formNotice: formNotice,
    formCancellable: formCancellable, timeZoneDetected: timeZoneDetected,
    timeZoneChosen: timeZoneChosen, timeZoneChanging: timeZoneChanging, steps: steps,
    stepStates: stepStates, stepsActionable: stepsActionable, memberNotice: memberNotice,
    menuItems: menuItems, menuEnabled: menuEnabled)
}

private func needsSite(
  name: String = "", nameError: String? = nil, working: Bool = false, notice: String? = nil,
  chosen: String? = nil, changing: Bool = false
) -> SitesPresentation {
  snapshot(
    surface: "needsSite", siteIds: [], siteNames: [], siteRoles: [], currentId: nil,
    currentName: nil, currentRole: nil, formShown: true, formName: name,
    formNameError: nameError, formWorking: working, formNotice: notice,
    timeZoneChosen: chosen, timeZoneChanging: changing, steps: [], stepStates: [],
    menuItems: [])
}

private func garden(_ presentation: SitesPresentation) -> GardenPresentation? {
  if case .garden(let garden, _) = presentation.surface { return garden }
  return nil
}

@Test("UX-DR61 with no Membership Create Site replaces the tab shell and cannot be cancelled")
func noMembershipShowsCreateSite() {
  let presentation = needsSite()

  #expect(!presentation.showsTabs)
  guard case .createSite(let form) = presentation.surface else {
    Issue.record("expected Create Site")
    return
  }
  #expect(!form.cancellable)
  #expect(form.name.isEmpty)
}

@Test("UX-DR61 UX-DR23 New Site opens a cancellable Create Site over the Garden")
func newSiteOpensOverTheShell() {
  let presentation = snapshot(formShown: true, formCancellable: true)

  #expect(presentation.showsTabs)
  #expect(presentation.createSite?.cancellable == true)
  #expect(snapshot().createSite == nil)
}

@Test("UX-DR61 Create Site names the result and shows its working label in place")
func createSiteButton() {
  let idle = needsSite(name: "Home").createSite
  let working = needsSite(name: "Home", working: true).createSite

  #expect(idle?.buttonLabel == .createSiteAction)
  #expect(idle?.isButtonEnabled == true)
  #expect(working?.buttonLabel == .createSiteWorking)
  #expect(working?.isButtonEnabled == false)
}

@Test(
  "UX-DR21 an invalid name shows its one-line reason in place of the helper",
  arguments: [
    (nil, L10n.createSiteNameHelper),
    ("blank", L10n.createSiteNameBlank),
    ("tooLong", L10n.createSiteNameTooLong),
  ] as [(String?, L10n)])
func nameErrors(error: String?, expected: L10n) {
  #expect(needsSite(nameError: error).createSite?.fieldHelper == expected)
}

@Test(
  "UX-DR21 Create Site failures say the Site was not created; certificate has no Try again",
  arguments: [
    ("identityProviderUnavailable", L10n.createSiteUnavailable, NoticeActionKind.tryAgain),
    ("keyReused", L10n.createSiteKeyReused, NoticeActionKind.tryAgain),
    ("unexpected", L10n.createSiteUnexpected, NoticeActionKind.tryAgain),
    ("unreachable", L10n.noticeUnreachable, NoticeActionKind.tryAgain),
    ("certificate", L10n.noticeCertificate, nil),
  ] as [(String, L10n, NoticeActionKind?)])
func createSiteNotices(notice: String, message: L10n, action: NoticeActionKind?) {
  let form = needsSite(notice: notice).createSite

  #expect(form?.notice?.createMessage == message)
  #expect(form?.notice?.action == action)
}

@Test(
  "UX-DR23 a failed Sites load shows the notice; only certificate has no Try again",
  arguments: [
    ("unreachable", L10n.noticeUnreachable, NoticeActionKind.tryAgain),
    ("certificate", L10n.noticeCertificate, nil),
    ("unexpected", L10n.sitesUnexpected, NoticeActionKind.tryAgain),
  ] as [(String, L10n, NoticeActionKind?)])
func failedLoad(notice: String, message: L10n, action: NoticeActionKind?) {
  let presentation = snapshot(surface: "failed", notice: notice)

  #expect(presentation.surface == .failed(SitesNoticeKind(rawValue: notice)!))
  #expect(SitesNoticeKind(rawValue: notice)?.loadMessage == message)
  #expect(SitesNoticeKind(rawValue: notice)?.action == action)
  #expect(!presentation.showsTabs)
}

@Test("UX-DR23 idle and loading show only the background")
func waiting() {
  #expect(snapshot(surface: "idle").surface == .waiting)
  #expect(snapshot(surface: "loading").surface == .waiting)
}

@Test("UX-DR61 the detected zone is proposed with Confirm and Change")
func timeZoneProposed() {
  let zone = needsSite().createSite?.timeZone

  #expect(zone?.sentence == .timeZoneQuestion)
  #expect(zone?.zone == "Europe/Zurich")
  #expect(zone?.showsConfirm == true)
}

@Test("UX-DR61 a chosen zone is named instead of the detected one and needs no Confirm")
func timeZoneChosen() {
  let zone = needsSite(chosen: "Pacific/Auckland").createSite?.timeZone

  #expect(zone?.sentence == .timeZoneChosen)
  #expect(zone?.zone == "Pacific/Auckland")
  #expect(zone?.showsConfirm == false)
}

@Test("UX-DR61 Change searches the IANA IDs by any word, keeping their order")
func timeZoneSearch() {
  let zones = ["America/New_York", "Europe/Zurich", "Pacific/Auckland"]

  #expect(needsSite(changing: true).createSite?.timeZone.changing == true)
  #expect(TimeZonePanelPresentation.filter(zones, query: "") == zones)
  #expect(TimeZonePanelPresentation.filter(zones, query: "zur") == ["Europe/Zurich"])
  #expect(TimeZonePanelPresentation.filter(zones, query: "new york") == ["America/New_York"])
  #expect(TimeZonePanelPresentation.filter(zones, query: "mars").isEmpty)
}

@Test("UX-DR23 the switcher lists the Sites in the Server's order with their Role, New Site last")
func switcherRows() {
  let presentation = snapshot(
    siteIds: ["b", "a"], siteNames: ["Allotment", "Home"], siteRoles: ["member", "owner"],
    currentId: "a", currentName: "Home", currentRole: "owner")
  let rows = garden(presentation)?.switcherRows ?? []

  #expect(rows.map(\.name) == ["Allotment", "Home", nil])
  #expect(rows.map(\.role) == [.member, .owner, nil])
  #expect(rows.map(\.isSelected) == [false, true, false])
  #expect(rows[1].traits == [.button, .selected])
  #expect(rows.last?.isNewSite == true)
  #expect(SiteRoleKind.administrator.label == .roleAdministrator)
}

@Test("UX-DR23 with a single Site the switcher still offers New Site")
func switcherSingleSite() {
  let rows = garden(snapshot())?.switcherRows ?? []

  #expect(rows.count == 2)
  #expect(rows.last == .newSite)
}

@Test("UX-DR21 UX-DR62 UX-DR82 the empty Garden names the Site and says No Readings yet")
func emptyGardenHeader() {
  let garden = garden(snapshot())

  #expect(garden?.siteName == "Home")
  #expect(garden?.headline == .gardenNoReadings)
  #expect(garden?.subline == .gardenNoReadingsDetail)
}

@Test("UX-DR54 four step tiles: Add a Hub next, the others later, none actionable yet")
func firstRunTiles() {
  let tiles = garden(snapshot())?.tiles ?? []

  #expect(tiles.map(\.step) == [.addHub, .addNode, .calibrate, .setLowThreshold])
  #expect(tiles.map(\.number) == [1, 2, 3, 4])
  #expect(tiles.map(\.isSolid) == [true, false, false, false])
  #expect(tiles.map(\.isDashed) == [false, true, true, true])
  #expect(
    tiles.map(\.step.label) == [
      .gardenStepAddHub, .gardenStepAddNode, .gardenStepCalibrate, .gardenStepSetThreshold,
    ])
  #expect(garden(snapshot())?.tilesActionable == false)
}

@Test("UX-DR54 a done step carries a checkmark")
func doneStep() {
  let tile = FirstRunTilePresentation(step: .addHub, number: 1, state: .done)

  #expect(tile.showsCheckmark)
  #expect(!tile.isSolid)
  #expect(tile.isDashed)
  #expect(StepStateKind.done.label == .gardenStepDone)
}

@Test("UX-DR54 only a Member sees the read-only notice")
func memberNotice() {
  let member = garden(snapshot(currentRole: "member", memberNotice: true))
  let owner = garden(snapshot())

  #expect(member?.memberNotice == .gardenMemberNotice)
  #expect(member?.role == .member)
  #expect(owner?.memberNotice == nil)
}

@Test("UX-DR22 UX-DR74 the Site menu holds Site settings, which opens Site settings")
func siteMenu() {
  let garden = garden(snapshot())

  #expect(garden?.menuItems == [.siteSettings])
  #expect(garden?.menuEnabled == true)
  #expect(SiteMenuItem.siteSettings.label == .siteMenuSettings)
  #expect(SiteMenuItem.siteSettings.opensSiteSettings)
}

@Test("UX-DR22 Pause and Resume keep their labels and are hidden unless the core lists them")
func siteMenuPause() {
  let paused = garden(snapshot(menuItems: ["resume", "siteSettings"], menuEnabled: false))

  #expect(paused?.menuItems == [.resume, .siteSettings])
  #expect(paused?.menuEnabled == false)
  #expect(SiteMenuItem.pause.label == .siteMenuPause)
  #expect(!SiteMenuItem.pause.opensSiteSettings)
  #expect(!SiteMenuItem.resume.opensSiteSettings)
}
