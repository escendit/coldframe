import ColdframeDesignTokens
import Foundation
import Testing

@testable import ColdframeIOS

private let context = Overview.context

@Test("UX-DR18 a Lot that needs water is a solid water tile with rain-drop, ~20 and the Reading")
func needsWaterTile() throws {
  let tile = Overview.tile(Overview.needsWater)

  #expect(tile.variant == .needsWater)
  #expect(tile.variant.fill == .water)
  #expect(tile.variant.border == .none)
  #expect(tile.variant.background == ColorTokens.statusWaterFill)
  #expect(tile.variant.ink == ColorTokens.statusWaterInk)
  #expect(tile.icon == .rainDrop)
  #expect(tile.statusLabel == .lotTileLabelNeedsWater)
  #expect(tile.valueText(context) == "~20")
  #expect(tile.footText(context) == "07:02 · low 30%")
  #expect(try Catalogue.entries()["lot_tile_label_needs_water"] == "Needs water")
}

@Test("UX-DR18 an OK Lot is a soil tile with a 1 pt solid border, checkmark--outline and ~35")
func okTile() throws {
  let tile = Overview.tile(Overview.ok)

  #expect(tile.variant == .ok)
  #expect(tile.variant.fill == .soil)
  #expect(
    tile.variant.border
      == LotTileBorder(style: .solid, width: 1, color: ColorTokens.statusOkBorder))
  #expect(tile.variant.background == ColorTokens.statusOkFill)
  #expect(tile.variant.footInk == ColorTokens.textSecondary)
  #expect(tile.icon == .checkmarkOutline)
  #expect(tile.statusLabel == .lotTileLabelOk)
  #expect(tile.valueText(context) == "~35")
  #expect(tile.footText(context) == "07:02 · low 25%")
  #expect(try Catalogue.entries()["lot_tile_label_ok"] == "OK")
}

@Test("UX-DR18 an OK Lot without a percentage shows no big value, only the parts present")
func okTileWithoutPercentage() {
  let tile = Overview.tile(Overview.okBare)

  #expect(tile.variant == .ok)
  #expect(tile.valueText(context) == nil)
  #expect(tile.footText(context) == "07:02")
  #expect(tile.level == nil)
  #expect(tile.lowMarker == nil)
  #expect(tile.spokenText(context) == "Onions, OK")
  // Nothing present at all: no foot either.
  var nothing = Overview.okBare
  nothing.readingAt = ""
  nothing.foot = "none"
  #expect(Overview.tile(nothing).footText(context) == nil)
  // Only the low Threshold.
  var lowOnly = Overview.okBare
  lowOnly.readingAt = ""
  lowOnly.low = "25"
  #expect(Overview.tile(lowOnly).footText(context) == "low 25%")
}

@Test("UX-DR18 an unknown Lot is a hatched tile with a 1 pt dashed border, help and the silence")
func unknownTile() throws {
  let node = Overview.tile(Overview.nodeSilent)
  let hub = Overview.tile(Overview.hubSilent)
  let never = Overview.tile(Overview.neverReported)

  for tile in [node, hub, never] {
    #expect(tile.variant == .unknown)
    #expect(tile.variant.fill == .hatch)
    #expect(
      tile.variant.border
        == LotTileBorder(style: .dashed, width: 1, color: ColorTokens.statusUnknownBorder))
    #expect(tile.icon == .help)
  }
  // The label comes from the Server's `unknownCause`, as the core read it.
  #expect(node.statusLabel == .lotTileLabelUnknownNode)
  #expect(hub.statusLabel == .lotTileLabelUnknownHub)
  #expect(node.valueText(context) == "6 h")
  #expect(node.footText(context) == "was ~40% at 01:05")
  #expect(hub.valueText(context) == "12 min")
  #expect(hub.footText(context) == "last Reading 07:02")
  #expect(never.valueText(context) == "2 d")
  #expect(never.footText(context) == "no Readings yet")
  let entries = try Catalogue.entries()
  #expect(entries["lot_tile_label_unknown_node"] == "Silent · unknown")
  #expect(entries["lot_tile_label_unknown_hub"] == "Hub silent · unknown")
}

@Test(
  "UX-DR18 a Lot that needs Calibration is hatched with a 2 pt dashed calibration border, tools and raw"
)
func needsCalibrationTile() throws {
  let tile = Overview.tile(Overview.needsCalibration)

  #expect(tile.variant == .needsCalibration)
  #expect(tile.variant.fill == .hatch)
  #expect(
    tile.variant.border
      == LotTileBorder(style: .dashed, width: 2, color: ColorTokens.statusCalibrationBorder))
  #expect(tile.variant.labelInk == ColorTokens.statusCalibrationInk)
  #expect(tile.icon == .tools)
  #expect(tile.statusLabel == .lotTileLabelNeedsCalibration)
  #expect(tile.valueText(context) == "raw")
  #expect(tile.footText(context) == "no % until calibrated")
  #expect(try Catalogue.entries()["lot_tile_label_needs_calibration"] == "Needs Calibration")
}

@Test("UX-DR18 a paused Lot is a flat tile with a 2 pt solid border, pause--outline and a dash")
func pausedTile() throws {
  let until = Overview.tile(Overview.pausedUntil)
  let bySite = Overview.tile(Overview.pausedBySite)
  let open = Overview.tile(Overview.paused)

  for tile in [until, bySite, open] {
    #expect(tile.variant == .paused)
    #expect(tile.variant.fill == .paused)
    #expect(
      tile.variant.border
        == LotTileBorder(style: .solid, width: 2, color: ColorTokens.statusPausedBorder))
    #expect(tile.variant.background == ColorTokens.statusPausedFill)
    #expect(tile.variant.ink == ColorTokens.statusPausedInk)
    #expect(tile.icon == .pauseOutline)
    #expect(tile.valueText(context) == "—")
  }
  // "Paused by Site" comes from the Server's `pausedBy`, as the core read it.
  #expect(until.statusLabel == .lotTileLabelPaused)
  #expect(bySite.statusLabel == .lotTileLabelPausedBySite)
  #expect(bySite.pausedBySite)
  #expect(until.footText(context) == "until 1 Nov")
  #expect(bySite.footText(context) == "paused")
  #expect(open.footText(context) == "paused")
  #expect(try Catalogue.entries()["lot_tile_label_paused_by_site"] == "Paused by Site")
}

@Test("UX-DR18 a Lot without a Node is a dotted tile with add, a large + and add a Node")
func noNodeTile() throws {
  let tile = Overview.tile(Overview.noNode)

  #expect(tile.name == "Zucchini")
  #expect(tile.status == .noNode)
  #expect(tile.variant == .noNode)
  #expect(tile.variant.fill == .none)
  #expect(
    tile.variant.border
      == LotTileBorder(style: .dotted, width: 1, color: ColorTokens.statusNoNodeBorder))
  #expect(tile.variant.ink == ColorTokens.statusNoNodeInk)
  #expect(tile.icon == .add)
  #expect(tile.statusLabel == .lotTileNoNode)
  #expect(tile.valueText(context) == "+")
  #expect(tile.footText(context) == "add a Node")
  #expect(tile.spokenText(context) == "Zucchini, no Node, add a Node")
}

@Test("UX-DR18 no secondary condition is appended to a label: each label is one catalogue entry")
func labelsAreSingleEntries() throws {
  let entries = try Catalogue.entries()
  for label in LotTileLabelKind.allCases {
    #expect(entries[label.live.rawValue] != nil, "\(label)")
    #expect(entries[label.was.rawValue] != nil, "\(label)")
  }
  #expect(Set(LotTileLabelKind.allCases.map(\.live)).count == LotTileLabelKind.allCases.count)
}

@Test(
  "UX-DR19 a stale tile has no fill, a 1 pt solid stale border, cloud--offline, Was ‹status› and as of"
)
func staleTile() throws {
  let expected: [(Overview.Lot, L10n, String)] = [
    (Overview.needsWater, .lotTileWasNeedsWater, "Was needs water"),
    (Overview.ok, .lotTileWasOk, "Was OK"),
    (Overview.nodeSilent, .lotTileWasUnknownNode, "Was silent · unknown"),
    (Overview.hubSilent, .lotTileWasUnknownHub, "Was Hub silent · unknown"),
    (Overview.needsCalibration, .lotTileWasNeedsCalibration, "Was needs Calibration"),
    (Overview.pausedUntil, .lotTileWasPaused, "Was paused"),
    (Overview.pausedBySite, .lotTileWasPausedBySite, "Was paused by Site"),
    (Overview.noNode, .lotTileWasNoNode, "Was no Node"),
  ]
  let entries = try Catalogue.entries()
  for (lot, label, english) in expected {
    let tile = Overview.staleTile(lot)
    #expect(tile.isStale, "\(lot.name)")
    #expect(tile.variant.fill == .none)
    #expect(tile.variant.background == nil)
    #expect(!tile.variant.isHatched)
    #expect(
      tile.variant.border
        == LotTileBorder(style: .solid, width: 1, color: ColorTokens.staleBorder))
    #expect(tile.variant.ink == ColorTokens.textSecondary)
    #expect(tile.variant.footInk == ColorTokens.staleInk)
    #expect(tile.icon == .cloudOffline)
    #expect(tile.statusLabel == label, "\(lot.name)")
    #expect(entries[label.rawValue] == english)
    // No value and no level: nothing on a stale tile is live. It still opens Lot detail, which
    // is stale too.
    #expect(tile.valueText(context) == nil)
    #expect(tile.level == nil)
    #expect(tile.lowMarker == nil)
    #expect(tile.opensLotDetail && !tile.opensAddNode)
    #expect(tile.footText(context) == "as of 07:02")
  }
}

@Test("UX-DR19 UX-DR80 skeleton tiles carry nothing: no Lot, no label, no value")
func skeletonTiles() {
  let loading = Overview.loading

  #expect(loading.surface == .loading(siteName: "Home garden"))
  #expect(loading.loadingSiteName == "Home garden")
  #expect(loading.tiles.isEmpty)
  #expect(loading.overview == nil)
  #expect(loading.siteSettings == nil)
  #expect(LotTilePresentation.skeletonCount > 0)
}

@Test("UX-DR17 the level and the low tick stand at the Reading and the low Threshold")
func tileAnatomy() {
  let water = Overview.tile(Overview.needsWater)
  let ok = Overview.tile(Overview.ok)
  let unknown = Overview.tile(Overview.nodeSilent)

  #expect(water.level == 20)
  #expect(water.lowMarker == 30)
  #expect(water.variant.level == ColorTokens.statusWaterLevel)
  #expect(water.variant.levelEdge == ColorTokens.statusWaterInk)
  #expect(water.variant.lowMarker == ColorTokens.statusWaterInk)
  #expect(ok.level == 35)
  #expect(ok.lowMarker == 25)
  #expect(ok.variant.level == ColorTokens.statusOkLevel)
  #expect(ok.variant.levelEdge == ColorTokens.statusLevelEdge)
  #expect(ok.variant.lowMarker == ColorTokens.statusLowMarker)
  // A last percentage on an unknown tile is text only: no level is drawn from it.
  #expect(unknown.soilPercent == 40)
  #expect(unknown.level == nil)
  for variant in LotTileVariantKind.allCases {
    #expect(variant.showsLevel == (variant == .needsWater || variant == .ok))
  }
}

@Test("UX-DR17 UX-DR12 hatched tiles put their text on a solid plate")
func hatchedTilesUseThePlate() {
  for variant in LotTileVariantKind.allCases {
    #expect(variant.isHatched == (variant == .unknown || variant == .needsCalibration))
    #expect(variant.isHatched == (variant.fill == .hatch))
  }
}

@Test("UX-DR12 the hatch is 135°, 1.5 pt lines every 8 pt on the hatch ground, with a plate")
func hatchPrimitive() throws {
  let hatch = HatchPresentation.standard

  #expect(hatch.angleDegrees == 135)
  #expect(hatch.lineWidth == 1.5)
  #expect(hatch.spacing == 8)
  #expect(hatch.line == ColorTokens.statusHatchLine)
  #expect(hatch.ground == ColorTokens.statusHatchGround)
  #expect(hatch.plate == ColorTokens.statusHatchGround)
  // Lines start left of the area so that the top-left corner is covered too.
  #expect(hatch.lineStarts(width: 16, height: 16) == [-16, -8, 0, 8])
  #expect(hatch.lineStarts(width: 0, height: 16).isEmpty)
  // One primitive: the Wi-Fi row draws with it and defines none of its own.
  let setup = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/HubSetupViews.swift")
  #expect(setup.contains("Hatch(palette: palette)"))
  #expect(setup.contains(".plate(row.isHatched, palette)"))
  #expect(!setup.contains("struct Hatch"))
  let tiles = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/SiteSettingsViews.swift")
  #expect(tiles.contains("Hatch(palette: palette)"))
  #expect(tiles.contains(".plate(variant.isHatched, palette)"))
}

@Test("UX-DR20 tiles keep the Server's order and are never re-sorted")
func serverOrder() {
  // An order no sort would produce.
  let shuffled = [Overview.noNode, Overview.ok, Overview.needsWater, Overview.paused]
  let presentation = Overview.lots(shuffled)

  #expect(presentation.tiles.map(\.id) == ["z", "p", "t", "k"])
  #expect(presentation.tiles.map(\.variant) == [.noNode, .ok, .needsWater, .paused])
  #expect(
    presentation.siteSettings?.lots.map(\.name) == ["Zucchini", "Peppers", "Tomatoes", "Kale"])
}

@Test(
  "UX-DR20 UX-DR63 UX-DR67 UX-DR84 every tile is a button; only a no-Node tile the core opens starts Add a Node"
)
func everyTileOpensDetailExceptNoNode() {
  for tile in Overview.lots().tiles {
    #expect(tile.isTappable, "\(tile.name)")
    #expect(tile.traits == [.button], "\(tile.name)")
    #expect(tile.opensAddNode == (tile.id == "z"), "\(tile.name)")
    #expect(tile.opensLotDetail == (tile.id != "z"), "\(tile.name)")
  }
  // A Member, or stale mode: the core does not open Add a Node, so the tile opens Lot detail.
  var member = Overview.noNode
  member.opensAddNode = false
  #expect(Overview.tile(member).opensLotDetail)
  // The core's word alone decides: no flag, no Add a Node.
  #expect(Overview.lots().tiles.filter(\.opensAddNode).map(\.spoken) == [.noNode])
}

@Test("UX-DR77 a status, variant or label this client does not know is drawn as unknown")
func unknownNames() {
  var odd = Overview.ok
  odd.status = "sprouting"
  odd.variant = "sprouting"
  odd.label = "sprouting"
  odd.value = "sprouting"
  odd.foot = "sprouting"
  odd.spoken = "sprouting"
  let tile = Overview.tile(odd)

  #expect(tile.status == .unknown)
  #expect(tile.variant == .unknown)
  #expect(tile.statusLabel == .lotTileLabelUnknownNode)
  #expect(tile.valueText(context) == nil)
  #expect(tile.footText(context) == nil)
  #expect(tile.spokenText(context) == "Peppers, unknown")
  // Lists shorter than the Lots: the missing tile is unknown, never fine and never tappable.
  let short = LotsPresentation(
    surface: "ready", notice: nil, siteId: "a", siteName: "Home", role: "owner",
    canRenameSite: true, canEditLots: true, readOnlyNotice: false,
    siteNameDraft: "Home", siteNameError: nil, siteRenameWorking: false,
    lotIds: ["t"], lotNames: ["Tomatoes"], lotStatuses: ["ok"],
    newLotName: "", newLotNameError: nil, createWorking: false,
    renamingLotId: nil, renameDraft: "", renameError: nil, renameWorking: false,
    removingLotId: nil, removingLotName: nil, removeWorking: false,
    actionNotice: nil, actionNoticeSubject: nil)
  #expect(short.tiles.map(\.variant) == [.unknown])
  #expect(short.tiles.allSatisfy { $0.opensLotDetail })
  #expect(short.overview?.summary.kind == .noReadings)
}

@Test("UX-DR77 the tile shows the Server's fields as the core passed them, and computes none")
func serverFieldsOnly() {
  let tiles = Overview.lots().tiles

  #expect(
    tiles.map(\.status) == [
      .needsWater, .needsCalibration, .unknown, .unknown, .unknown, .ok, .ok, .paused, .paused,
      .paused, .noNode,
    ])
  // A needs-calibration tile never carries a percentage, whatever else is known.
  var calibrating = Overview.needsCalibration
  calibrating.soil = "40"
  #expect(Overview.tile(calibrating).valueText(context) == "raw")
  #expect(Overview.tile(calibrating).level == nil)
  // The duration is the core's: the tile writes "6 h" and never measures it.
  let silent = Overview.tile(Overview.nodeSilent)
  #expect(silent.duration == LotDurationPresentation(value: 6, unit: .hours))
  var noDuration = Overview.nodeSilent
  noDuration.durationValue = ""
  noDuration.durationUnit = ""
  noDuration.value = "none"
  #expect(Overview.tile(noDuration).valueText(context) == nil)
}

@Test("UX-DR98 every tile has one complete spoken label from the State Patterns table")
func spokenLabels() {
  let expected: [(Overview.Lot, String)] = [
    (
      Overview.needsWater,
      "Tomatoes, needs water, about 20 percent, low 30 percent, Reading 07:02"
    ),
    (Overview.ok, "Peppers, OK, about 35 percent, low 25 percent"),
    (
      Overview.nodeSilent,
      "Beans, unknown, Node silent for 6 hours, last about 40 percent at 01:05"
    ),
    (Overview.hubSilent, "Herbs, unknown, Hub silent for 12 minutes, last Reading 07:02"),
    (Overview.neverReported, "Nasturtiums, unknown, Node silent for 2 days, no Readings yet"),
    (Overview.needsCalibration, "Carrots, needs Calibration, no percentage until calibrated"),
    (Overview.pausedUntil, "Squash, paused until 1 November"),
    (Overview.pausedBySite, "Leeks, paused with the Site"),
    (Overview.paused, "Kale, paused"),
    (Overview.noNode, "Zucchini, no Node, add a Node"),
  ]
  for (lot, spoken) in expected {
    #expect(Overview.tile(lot).spokenText(context) == spoken)
  }
  // One hour, one minute, one day: the catalogue's plural rules.
  var oneHour = Overview.nodeSilent
  oneHour.durationValue = "1"
  #expect(
    Overview.tile(oneHour).spokenText(context)
      == "Beans, unknown, Node silent for 1 hour, last about 40 percent at 01:05")
}

@Test("UX-DR98 a stale tile says what the status was, not live, and as of when")
func staleSpokenLabels() {
  let expected: [(Overview.Lot, String)] = [
    (Overview.needsWater, "Tomatoes, was needs water, not live, as of 07:02"),
    (Overview.ok, "Peppers, was OK, not live, as of 07:02"),
    (Overview.nodeSilent, "Beans, was unknown, not live, as of 07:02"),
    (Overview.needsCalibration, "Carrots, was needs Calibration, not live, as of 07:02"),
    (Overview.pausedBySite, "Leeks, was paused, not live, as of 07:02"),
    (Overview.noNode, "Zucchini, was no Node, not live, as of 07:02"),
  ]
  for (lot, spoken) in expected {
    #expect(Overview.staleTile(lot).spokenText(context) == spoken)
  }
}

@Test("UX-DR99 the six statuses and stale differ in shape and icon, with colour and text removed")
func neverColourAlone() {
  let variants = LotTileVariantKind.allCases
  #expect(variants.count == 7)
  // The outline and fill of a tile without its colours.
  let shapes = Set(
    variants.map { "\($0.fill.rawValue)/\($0.border.style.rawValue)/\($0.border.width)" })
  #expect(shapes.count == variants.count)
  #expect(Set(variants.map(\.icon)).count == variants.count)
  #expect(
    variants.map(\.icon) == [
      .rainDrop, .checkmarkOutline, .help, .tools, .pauseOutline, .add, .cloudOffline,
    ])
  // Every tile also says its status in words.
  for tile in Overview.lots().tiles + Overview.staleLots().tiles {
    #expect(!Catalogue.resolve(Copy(tile.statusLabel)).isEmpty)
  }
}

@Test("UX-DR97 one column from Accessibility 1, with the value directly under the status label")
func oneColumnAtAccessibilitySizes() throws {
  #expect(LotTilePresentation.columns(accessibilitySize: true) == 1)
  #expect(LotTilePresentation.valueFollowsLabel(columns: 1))
  #expect(!LotTilePresentation.valueFollowsLabel(columns: 2))
  // The grid asks Dynamic Type, whose accessibility sizes start at Accessibility 1, and the
  // tile keeps its minimum height in one column: it grows, it never compacts.
  let views = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/SiteSettingsViews.swift")
  #expect(
    views.contains(
      "LotTilePresentation.columns(accessibilitySize: dynamicTypeSize.isAccessibilitySize)"))
  #expect(views.contains("LotTilePresentation.valueFollowsLabel(columns: columns)"))
  #expect(views.contains("if !valueFollowsLabel {\n          Spacer(minLength: 0)"))
  #expect(!views.contains("lineLimit"))
}

@Test("UX-DR107 the phone grid has two columns in mobile gutters and scrolls vertically")
func phoneLayout() throws {
  #expect(LotTilePresentation.columns(accessibilitySize: false) == 2)
  #expect(LotTilePresentation.columns(accessibilitySize: true) == 1)
  let garden = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/SitesViews.swift")
  let overview = garden.components(separatedBy: "public struct GardenView").last ?? ""
  #expect(overview.contains("ScrollView {"))
  #expect(overview.contains(".padding(Spacing.gutterMobile)"))
  #expect(!overview.contains("ScrollView(.horizontal"))
}

@Test(
  "UX-DR128 soil moisture is written ~ and the core's nearest 5; uncalibrated shows raw, never %"
)
func valueFormatting() throws {
  #expect(Overview.tile(Overview.needsWater).valueText(context) == "~20")
  #expect(Overview.tile(Overview.nodeSilent).footText(context) == "was ~40% at 01:05")
  #expect(try Catalogue.entries()["soil_approx"] == "~%@")
  let raw = Overview.tile(Overview.needsCalibration)
  #expect(raw.valueText(context) == "raw")
  #expect(raw.footText(context)?.contains("~") == false)
  #expect(!raw.spokenText(context).contains("about"))
  // The percent sign and its spacing are the locale's.
  let german = CopyContext(
    now: context.now, timeZone: context.timeZone, locale: Locale(identifier: "de_DE"),
    resolve: Catalogue.resolve)
  let foot = try #require(Overview.tile(Overview.needsWater).footText(german))
  #expect(foot.hasPrefix("07:02 · low 30"))
  #expect(foot != "07:02 · low 30%")
}
