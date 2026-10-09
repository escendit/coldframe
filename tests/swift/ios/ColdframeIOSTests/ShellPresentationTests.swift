import ColdframeDesignTokens
import Foundation
import Testing

@testable import ColdframeIOS

@Test("UX-DR109 the tab view has Garden, Alerts, Devices and Settings with Carbon icons")
func tabsInOrder() {
  #expect(AppTab.allCases == [.garden, .alerts, .devices, .settings])
  #expect(AppTab.allCases.map(\.label) == [.navGarden, .navAlerts, .navDevices, .navSettings])
  #expect(AppTab.allCases.map(\.icon) == [.grid, .notification, .box, .settings])
  #expect(
    AppTab.allCases.map(\.title) == [.gardenTitle, .alertsTitle, .devicesTitle, .settingsTitle])
}

@Test("UX-DR109 the selected tab is exposed as selected")
func selectedTabTraits() {
  #expect(AppTab.garden.traits(selected: .garden) == [.button, .selected])
  #expect(AppTab.alerts.traits(selected: .garden) == [.button])
}

@Test("UX-DR64 UX-DR82 the Alerts tab reads Alerts · N while N are open, spoken Alerts, N open")
func alertsTabLabel() {
  #expect(AppTab.alerts.labelCopy(openAlerts: 5) == Copy(.navAlertsCount, .number(5)))
  #expect(AppTab.alerts.spokenCopy(openAlerts: 5) == Copy(.countOpenAlerts, .number(5)))
  #expect(Catalogue.resolve(AppTab.alerts.labelCopy(openAlerts: 5)) == "Alerts · 5")
  #expect(Catalogue.resolve(AppTab.alerts.spokenCopy(openAlerts: 5)) == "Alerts, 5 open")
  #expect(Catalogue.resolve(AppTab.alerts.labelCopy(openAlerts: 1)) == "Alerts · 1")
  #expect(Catalogue.resolve(AppTab.alerts.spokenCopy(openAlerts: 1)) == "Alerts, 1 open")
}

@Test("UX-DR64 without open Alerts, and on every other tab, the label carries no count")
func tabLabelsWithoutCount() {
  #expect(AppTab.alerts.labelCopy(openAlerts: 0) == Copy(.navAlerts))
  #expect(AppTab.alerts.spokenCopy(openAlerts: 0) == Copy(.navAlerts))
  #expect(AppTab.alerts.labelCopy(openAlerts: -3) == Copy(.navAlerts))
  for tab in AppTab.allCases where tab != .alerts {
    #expect(tab.labelCopy(openAlerts: 5) == Copy(tab.label))
    #expect(tab.spokenCopy(openAlerts: 5) == Copy(tab.label))
  }
}

@Test("UX-DR64 the tab count is the open count that was read: none after a failed load")
func alertsTabCountFollowsThePresentation() {
  let ready = AlertsFixture.alerts([AlertsFixture.needsWater], openCount: 5)
  let failed = AlertsFixture.alerts(surface: "failed", notice: "unreachable", openCount: 5)

  #expect(
    AppTab.alerts.labelCopy(openAlerts: ready.openCount) == Copy(.navAlertsCount, .number(5)))
  #expect(AppTab.alerts.labelCopy(openAlerts: failed.openCount) == Copy(.navAlerts))
  let waiting = AlertsPresentation.waiting
  #expect(AppTab.alerts.labelCopy(openAlerts: waiting.openCount) == Copy(.navAlerts))
}

@Test("UX-DR36 the selected segment carries the selected trait and a checkmark")
func segmentTraits() {
  let segments = themeSegments(selected: .dark)

  #expect(segments.map(\.value) == [.system, .light, .dark])
  #expect(segments.filter(\.isSelected).map(\.value) == [.dark])
  #expect(segments[2].traits == [.button, .selected])
  #expect(segments[2].showsCheckmark)
  #expect(segments[0].traits == [.button])
  #expect(!segments[0].showsCheckmark)
}

@Test("UX-DR75 Appearance offers System, Light and Dark, System first")
func appearanceOptions() {
  #expect(themeSegments(selected: .system).map(\.label) == [.themeSystem, .themeLight, .themeDark])
  #expect(themeSegments(selected: .system)[0].isSelected)
}

@Test("UX-DR71 UX-DR74 Settings lists My notifications, Site settings, Appearance, then Account")
func settingsIndex() {
  #expect(SettingsRow.allCases == [.notifications, .siteSettings, .appearance, .account])
  #expect(
    SettingsRow.allCases.map(\.title) == [
      .settingsNotifications, .settingsSiteSettings, .settingsAppearance, .settingsAccount,
    ])
  #expect(SettingsRow.siteSettings.helper == .settingsSiteSettingsHelper)
  #expect(SettingsRow.account.helper == nil)
}

@Test("UX-DR72 My notifications sits in Settings above Site settings and opens its own surface")
func settingsNotificationsRow() throws {
  #expect(SettingsRow.allCases.first == .notifications)
  #expect(SettingsRow.notifications.title == .settingsNotifications)
  #expect(SettingsRow.notifications.helper == .settingsNotificationsHelper)
  let entries = try Catalogue.entries()
  #expect(entries["settings_notifications"] == "My notifications")
  #expect(
    entries["settings_notifications_helper"]
      == "Notification Window, time zone, mute and Reminders")
  #expect(entries["notifications_title"] == "My notifications")
  // The row is drawn before the Site settings row, and needs no current Site.
  let screens = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/Screens.swift")
  let index = try #require(
    screens.components(separatedBy: "public struct SettingsView").last?
      .components(separatedBy: "public struct AppearanceView").first)
  let notifications = try #require(index.range(of: "MyNotificationsView("))
  let siteSettings = try #require(index.range(of: "SiteSettingsView("))
  let needsSite = try #require(index.range(of: "if let siteName {"))
  #expect(notifications.lowerBound < needsSite.lowerBound)
  #expect(needsSite.lowerBound < siteSettings.lowerBound)
}

@Test("UX-DR113 UX-DR76 sign out confirms in one native dialog that names the result")
func signOutConfirmation() {
  let confirmation = ConfirmationPresentation.signOut

  #expect(confirmation.title == .settingsSignOutQuestion)
  #expect(confirmation.message == .settingsSignOutDetail)
  #expect(confirmation.confirm == .settingsSignOut)
  #expect(confirmation.cancel == .modalCancel)
  #expect(confirmation.isModal)
}

@Test("UX-DR100 every control is at least 44 pt")
func touchTargets() {
  #expect(TouchTarget.minimum == 44)
  #expect(TouchTarget.control >= 44)
  #expect(TouchTarget.control == Spacing.buttonHeight)
}

@Test("UX-DR15 unknown stored themes are System, which follows the OS")
func themePreference() {
  #expect(ThemePreference(stored: nil) == .system)
  #expect(ThemePreference(stored: "sepia") == .system)
  #expect(ThemePreference(stored: "dark") == .dark)
  #expect(ThemePreference.system.isDark(systemIsDark: true))
  #expect(!ThemePreference.light.isDark(systemIsDark: true))
  #expect(ThemePreference.dark.isDark(systemIsDark: false))
  #expect(ThemePreference.system.forcedDark == nil)
}

@Test func fontFacesUseTheBundledUbuntuFiles() {
  #expect(FontFaces.postScriptName(for: Typography.headline) == "UbuntuCondensed-Regular")
  #expect(FontFaces.postScriptName(for: Typography.body) == "Ubuntu-Regular")
  #expect(FontFaces.postScriptName(for: Typography.metaMono) == "UbuntuMono-Regular")
  #expect(FontFaces.files.count == 4)
}
