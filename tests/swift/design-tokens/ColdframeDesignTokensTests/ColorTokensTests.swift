import Testing

@testable import ColdframeDesignTokens

@Test func holdsEveryDesignColour() {
  #expect(ColorTokens.all.count == 53)
}

@Test func holdsLightAndDarkValuesAsRrggbbaa() {
  #expect(ColorTokens.background == ThemedColor(light: 0xFEFE_FEFF, dark: 0x2626_26FF))
  #expect(ColorTokens.primaryText == ThemedColor(light: 0xB84A_00FF, dark: 0xFF8A_2EFF))
  #expect(ColorTokens.focus.resolve(isDark: false) == 0x1616_16FF)
  #expect(ColorTokens.focus.resolve(isDark: true) == 0xF4F4_F4FF)
}

@Test func keepsAlpha() {
  #expect(ColorTokens.overlay.light == 0x1616_1680)
  #expect(ColorTokens.overlay.dark == 0x1616_16B3)
  let components = ThemedColor.components(of: ColorTokens.overlay.light)
  #expect(components.red == Double(0x16) / 255)
  #expect(components.alpha == Double(0x80) / 255)
}

@Test func splitsRrggbbaaIntoItsChannels() {
  #expect(ColorTokens.primary.light == 0xFF77_0FFF)
  let components = ThemedColor.components(of: ColorTokens.primary.light)
  #expect(components.red == Double(0xFF) / 255)
  #expect(components.green == Double(0x77) / 255)
  #expect(components.blue == Double(0x0F) / 255)
  #expect(components.alpha == Double(0xFF) / 255)
}

@Test func usesTheLightValueInBothThemesWithoutADarkTwin() {
  #expect(ColorTokens.primary.light == ColorTokens.primary.dark)
  #expect(ColorTokens.inkOnBright.dark == 0x0A0A_0AFF)
}

@Test func keysEveryTokenByItsDesignName() {
  #expect(ColorTokens.all["status-water-fill"] == ColorTokens.statusWaterFill)
  #expect(ColorTokens.all["layer-01"] == ColorTokens.layer01)
}

@Test func holdsSpacingAndSquareRadii() {
  #expect(Spacing.step1 == 2)
  #expect(Spacing.step13 == 160)
  #expect(Spacing.buttonHeight == 48)
  #expect(Radius.none == 0)
  #expect(Radius.default == 0)
}
