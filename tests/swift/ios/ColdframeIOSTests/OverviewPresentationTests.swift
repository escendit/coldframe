import ColdframeDesignTokens
import Foundation
import Testing

@testable import ColdframeIOS

private let context = Overview.context

private let garden = GardenPresentation(
  siteName: "Home garden", role: .owner, tiles: [], tilesActionable: true,
  switcherRows: [.newSite], menuItems: [.siteSettings], menuEnabled: true,
  showsMemberNotice: false)

private func summary(_ lots: LotsPresentation) -> SiteSummaryPresentation? {
  if case .summary(let summary) = garden.header(lots: lots) { return summary }
  return nil
}

private func staleHeader(_ lots: LotsPresentation) -> StaleHeaderPresentation? {
  if case .stale(let stale) = garden.header(lots: lots) { return stale }
  return nil
}

@Test("UX-DR129 one Lot that needs water is named; more are counted")
func headlineNeedsWater() throws {
  let one = try #require(summary(Overview.lots()))
  let more = try #require(
    summary(Overview.lots(headline: "needsWater", headlineCount: 2, headlineLotName: nil)))

  #expect(one.kind == .needsWater)
  #expect(one.headline(context) == "Tomatoes needs water")
  #expect(more.headline(context) == "2 Lots need water")
  #expect(one.headlineInk == ColorTokens.textPrimary)
}

@Test("UX-DR129 unknown and needs-calibration Lots can't be read")
func headlineCantBeRead() throws {
  let one = try #require(
    summary(Overview.lots(headline: "cantBeRead", headlineCount: 1, headlineLotName: nil)))
  let more = try #require(
    summary(Overview.lots(headline: "cantBeRead", headlineCount: 4, headlineLotName: nil)))

  #expect(one.headline(context) == "1 Lot can't be read")
  #expect(more.headline(context) == "4 Lots can't be read")
}

@Test("UX-DR129 a Site paused as a whole says Paused until the shared end, in paused ink")
func headlinePaused() throws {
  let until = try #require(
    summary(
      Overview.lots(
        headline: "paused", headlineCount: 0, headlineLotName: nil,
        headlinePausedUntil: String(Overview.novemberMs), headlinePausedInk: true)))
  let open = try #require(
    summary(
      Overview.lots(
        headline: "paused", headlineCount: 0, headlineLotName: nil, headlinePausedInk: true)))

  #expect(until.headline(context) == "Paused until 1 Nov")
  #expect(until.headlineInk == ColorTokens.statusPausedInk)
  #expect(open.headline(context) == "Paused")
  #expect(open.headlineInk == ColorTokens.statusPausedInk)
}

@Test("UX-DR129 otherwise Nothing needs water; without a Node, No Readings yet")
func headlineNothingAndNoReadings() throws {
  let nothing = try #require(
    summary(Overview.lots(headline: "nothingNeedsWater", headlineCount: 0, headlineLotName: nil)))
  let none = try #require(
    summary(
      Overview.lots(
        [Overview.noNode], headline: "noReadings", headlineCount: 0, headlineLotName: nil,
        counts: [])))

  #expect(nothing.headline(context) == "Nothing needs water")
  #expect(none.headline(context) == "No Readings yet")
  // The first-run detail line stays under "No Readings yet".
  #expect(none.subline(context) == "Nothing is measuring, so there's no status to show.")
  // Lots that are not known yet, or a headline this client does not know, never read as fine.
  #expect(summary(.waiting) == .noReadings)
  #expect(summary(Overview.lots(surface: "failed", notice: "unreachable")) == .noReadings)
  #expect(summary(Overview.lots(headline: "sprouting"))?.kind == .noReadings)
  #expect(garden.headline == .gardenNoReadings)
}

@Test("UX-DR129 the counts subline lists the non-zero counts in the Server's order")
func countsSubline() throws {
  let all = try #require(summary(Overview.lots()))
  let few = try #require(
    summary(
      Overview.lots(
        headline: "nothingNeedsWater", counts: [("needsCalibration", 2), ("ok", 1)])))
  let none = try #require(summary(Overview.lots(headline: "nothingNeedsWater", counts: [])))

  #expect(
    all.subline(context) == "1 needs Calibration · 3 unknown · 2 OK · 3 paused · 1 without Node")
  #expect(few.subline(context) == "2 need Calibration · 1 OK")
  #expect(none.subline(context) == nil)
  // The headline says how many need water; a count for it, or for a status this client does
  // not know, is not written.
  let odd = try #require(
    summary(Overview.lots(counts: [("needsWater", 1), ("sprouting", 2), ("unknown", 1)])))
  #expect(odd.subline(context) == "1 unknown")
}

@Test(
  "UX-DR24 the stale header names the Site, shows the age in stale ink and when the data is from"
)
func staleHeaderContent() throws {
  let header = try #require(staleHeader(Overview.staleLots()))

  #expect(header.icon == .cloudOffline)
  #expect(header.title(context) == "Home garden · can't reach your Server")
  #expect(header.ageText(context) == "2 h 58 min old")
  #expect(header.ageInk == ColorTokens.staleInk)
  #expect(
    header.detail(context)
      == "Last data 07:02. You may be away from home, or the Server is down. Nothing below is live."
  )
}

@Test("UX-DR24 the age is min under an hour, h min under a day and d from a day")
func staleAge() {
  func age(_ days: Int, _ hours: Int, _ minutes: Int) -> String {
    Catalogue.resolve(StaleAgePresentation(days: days, hours: hours, minutes: minutes).copy)
  }

  #expect(age(0, 0, 0) == "0 min")
  #expect(age(0, 0, 12) == "12 min")
  #expect(age(0, 2, 12) == "2 h 12 min")
  #expect(age(0, 2, 0) == "2 h")
  #expect(age(3, 5, 40) == "3 d")
}

@Test("UX-DR24 the age moves with the minute tick and is never announced")
func staleAgeTicksSilently() throws {
  let before = try #require(
    staleHeader(Overview.lots(stale: true, fetchedAtMs: Overview.readingMs, staleAge: (0, 2, 58))))
  let after = try #require(
    staleHeader(Overview.lots(stale: true, fetchedAtMs: Overview.readingMs, staleAge: (0, 2, 59))))

  #expect(before.ageText(context) == "2 h 58 min old")
  #expect(after.ageText(context) == "2 h 59 min old")
  // "Last data" does not move while reads fail.
  #expect(before.detail(context) == after.detail(context))
  #expect(!after.announcesAge)
  // The header is plain text: it is not a notice and posts nothing.
  let views = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/SitesViews.swift")
  let header = try #require(
    views.components(separatedBy: "struct StaleHeader: View").last?
      .components(separatedBy: "\n  }\n").first)
  #expect(header.contains("stale.ageText(context)"))
  #expect(!header.contains("InlineNotice"))
  #expect(!header.contains("AccessibilityNotification"))
  // The tick only reads the core's snapshot again.
  let service = try Repo.text("apps/swift/ios/App/CoreLotsService.swift")
  #expect(service.contains("onChange?(CoreLotsService.presentation(of: core.current()))"))
}

@Test("UX-DR79 in stale mode the stale header replaces the summary and every tile is stale")
func staleMode() throws {
  let stale = Overview.staleLots()

  #expect(stale.overview?.stale == true)
  #expect(staleHeader(stale) != nil)
  #expect(summary(stale) == nil)
  #expect(stale.tiles.count == Overview.everyLot.count)
  #expect(stale.tiles.allSatisfy { $0.isStale && $0.valueText(context) == nil })
  #expect(stale.tiles.allSatisfy { $0.footText(context) == "as of 07:02" })
  #expect(stale.tiles.allSatisfy { $0.opensLotDetail })
  // Live again: the summary and the live tiles are back.
  let live = Overview.lots()
  #expect(staleHeader(live) == nil)
  #expect(summary(live) != nil)
  #expect(live.tiles.allSatisfy { !$0.isStale })
}

@Test("UX-DR79 in stale mode every Site menu item is disabled with Needs your Server")
func staleSiteMenu() throws {
  let stale = garden.menu(lots: Overview.staleLots())
  let live = garden.menu(lots: Overview.lots())

  #expect(stale.items == [.siteSettings])
  #expect(!stale.enabled)
  #expect(stale.disabledReason == .siteMenuNeedsServer)
  #expect(try Catalogue.entries()["site_menu_needs_server"] == "Needs your Server")
  #expect(live.enabled)
  #expect(live.disabledReason == nil)
  // Before the Lots are known the Sites menu shows.
  #expect(
    garden.menu(lots: .waiting) == SiteMenuPresentation(items: [.siteSettings], enabled: true))
}

@Test("UX-DR79 stale mode is the overview's only: Site settings and Devices have none")
func staleModeOnlyOnTheOverview() throws {
  let settings = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/SiteSettingsViews.swift")
  let siteSettings = try #require(
    settings.components(separatedBy: "struct LotGrid").first?
      .components(separatedBy: "public struct SiteSettingsView").last)
  #expect(!siteSettings.contains("stale"))
  #expect(!siteSettings.contains("StaleHeader"))
  let devices = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/DevicesViews.swift")
  #expect(!devices.contains("StaleHeader"))
  // Site settings keeps working on the Lots it has.
  #expect(Overview.staleLots().siteSettings?.lots.count == Overview.everyLot.count)
}

@Test("UX-DR80 a cold start with kept Lots shows them in stale mode until the refresh lands")
func coldStartWithCache() throws {
  let cached = Overview.lots(
    Overview.everyLot.map(\.stale), stale: true, refreshing: true,
    fetchedAtMs: Overview.earlierMs, staleAge: (0, 8, 55), menuEnabled: false)

  #expect(cached.overview?.refreshing == true)
  let header = try #require(staleHeader(cached))
  #expect(header.ageText(context) == "8 h 55 min old")
  #expect(header.detail(context).hasPrefix("Last data 01:05."))
  #expect(cached.tiles.allSatisfy { $0.isStale })
  #expect(cached.tiles.first?.footText(context) == "as of 01:05")
}

@Test("UX-DR80 a cold start without kept Lots shows skeleton tiles and Loading ‹Site›")
func coldStartWithoutCache() throws {
  #expect(garden.header(lots: Overview.loading) == .loading(siteName: "Home garden"))
  #expect(Catalogue.resolve(Copy(.gardenLoading, .text("Home garden"))) == "Loading Home garden")
  #expect(Overview.loading.tiles.isEmpty)
  // The skeletons are hidden from VoiceOver as one group; they are not buttons.
  let views = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/SiteSettingsViews.swift")
  let skeletons = try #require(
    views.components(separatedBy: "} else if lots.loadingSiteName != nil {").last?
      .components(separatedBy: "} else if !lots.tiles.isEmpty {").first)
  #expect(skeletons.contains("LotSkeletonTile()"))
  #expect(skeletons.contains(".accessibilityHidden(true)"))
  #expect(!skeletons.contains("Button"))
  // No kept Lots and no Server: the existing notice.
  let failed = Overview.lots(surface: "failed", notice: "unreachable")
  #expect(failed.failure == .unreachable)
  #expect(failed.loadingSiteName == nil)
}

@Test("UX-DR106 entering and leaving stale mode are announced politely, once each by the core")
func staleAnnouncements() throws {
  let entered = try #require(
    LotsEventPresentation(
      kind: "enteredStale", siteId: "a", fetchedAtEpochMs: Overview.readingMs))
  let left = try #require(
    LotsEventPresentation(kind: "leftStale", siteId: "a", fetchedAtEpochMs: 0))

  #expect(entered.text(context) == "Can't reach your Server. Showing data from 07:02.")
  #expect(left.text(context) == "Live again.")
  #expect(entered.announcement == .polite)
  #expect(left.announcement == .polite)
  // An event this client does not know says nothing.
  #expect(LotsEventPresentation(kind: "sprouted", siteId: "a", fetchedAtEpochMs: 0) == nil)
  #expect(
    LotsEventPresentation(kind: "enteredStale", siteId: "a", fetchedAtEpochMs: 0)?
      .text(context) == nil)
}

@Test("UX-DR106 only the core's events are announced: never a tick or an unchanged refresh")
func onlyEventsAreAnnounced() throws {
  let screens = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/Screens.swift")
  let model = try #require(screens.components(separatedBy: "final class ShellModel").last)
  #expect(model.contains("lots?.observeEvents"))
  // One post, for the events; a polite announcement has no raised priority.
  #expect(model.components(separatedBy: "AccessibilityNotification.Announcement").count == 2)
  #expect(!model.contains("accessibilitySpeechAnnouncementPriority"))
  let service = try Repo.text("apps/swift/ios/App/CoreLotsService.swift")
  #expect(service.contains("core.watchEvents"))
  let garden = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/SitesViews.swift")
  let overview = try #require(
    garden.components(separatedBy: "public struct GardenView").last?
      .components(separatedBy: "struct FirstRunTile").first)
  #expect(!overview.contains("AccessibilityNotification"))
}

@Test("UX-DR112 the overview refreshes on pull and when the app becomes active, and never polls")
func refreshPrimitives() throws {
  let garden = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/SitesViews.swift")
  #expect(garden.contains(".refreshable { [lotsActions] in"))
  #expect(garden.contains("lotsActions.refresh()"))
  let app = try Repo.text("apps/swift/ios/App/ColdframeApp.swift")
  let active = try #require(app.components(separatedBy: "if phase == .active {").last)
  #expect(active.contains("model.lotsService?.refresh()"))
  let service = try Repo.text("apps/swift/ios/App/CoreLotsService.swift")
  #expect(service.contains("func refresh() { core.refresh() }"))
  // The one timer is the minute tick, which reads the core's snapshot and fetches nothing.
  let sleeps = Repo.swiftSources("apps/swift/ios").filter {
    ((try? String(contentsOf: $0, encoding: .utf8)) ?? "").contains("Task.sleep")
  }
  // The overview and Lot detail (Story 4.8) each have their minute tick, and no other timer.
  #expect(Set(sleeps.map(\.lastPathComponent)) == ["SitesViews.swift", "LotDetailViews.swift"])
  #expect(garden.contains("try? await Task.sleep(for: .seconds(60))"))
  #expect(garden.contains("lotsActions.tick()"))
  #expect(garden.components(separatedBy: "Task.sleep").count == 2)
  for file in Repo.swiftSources("apps/swift/ios") {
    let text = try String(contentsOf: file, encoding: .utf8)
    #expect(!text.contains("Timer."), "\(file.lastPathComponent)")
    #expect(!text.contains("SignalR"), "\(file.lastPathComponent)")
  }
}
