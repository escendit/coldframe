import ColdframeDesignTokens
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

@Test("UX-DR71 Settings lists Appearance, then Account")
func settingsIndex() {
  #expect(SettingsRow.allCases == [.appearance, .account])
  #expect(SettingsRow.allCases.map(\.title) == [.settingsAppearance, .settingsAccount])
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
