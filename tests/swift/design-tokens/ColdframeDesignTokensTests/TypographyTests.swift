import Testing

@testable import ColdframeDesignTokens

private let expectedTextStyles: [String: IOSTextStyle] = [
  "headline": .largeTitle,
  "title": .title,
  "section": .title3,
  "hero-value": .largeTitle,
  "tile-value": .largeTitle,
  "tile-value-web": .largeTitle,
  "tile-name": .body,
  "status-label": .footnote,
  "body": .subheadline,
  "body-lg": .callout,
  "helper": .caption1,
  "meta-mono": .caption1,
  "button": .subheadline,
  "step-counter": .largeTitle,
]

@Test func holdsEveryRole() {
  #expect(Typography.all.count == 14)
  #expect(Set(Typography.all.keys) == Set(expectedTextStyles.keys))
}

@Test(arguments: Typography.all.keys.sorted())
func mapsEachRoleToItsDynamicTypeStyle(name: String) throws {
  let role = try #require(Typography.all[name])
  #expect(role.textStyle == expectedTextStyles[name])
  #expect(role.weight == 300 || role.weight == 400)
}

@Test(arguments: Typography.all.keys.sorted())
func capsRolesOf36PointsAndMoreAt2x(name: String) throws {
  let role = try #require(Typography.all[name])
  let huge = role.textStyle.defaultPointSize * 4
  if role.size >= 36 {
    #expect(role.maximumPointSize == role.size * 2)
    #expect(role.scaledSize(forStylePointSize: huge) == role.size * 2)
  } else {
    #expect(role.maximumPointSize == nil)
    #expect(role.scaledSize(forStylePointSize: huge) == role.size * 4)
  }
  #expect(role.scaledSize(forStylePointSize: role.textStyle.defaultPointSize) == role.size)
}

@Test func holdsTheDesignValues() {
  #expect(Typography.headline.size == 36)
  #expect(Typography.headline.fontFamily == "Ubuntu Condensed")
  #expect(Typography.heroValue.size == 72)
  #expect(Typography.heroValue.weight == 300)
  #expect(Typography.body.lineHeight == 1.43)
  #expect(Typography.statusLabel.letterSpacingEm == 0.06)
}

@Test func uppercasesOnlyStatusLabelAndButton() {
  let uppercased = Set(Typography.all.filter { $0.value.uppercase }.keys)
  #expect(uppercased == ["status-label", "button"])
}
