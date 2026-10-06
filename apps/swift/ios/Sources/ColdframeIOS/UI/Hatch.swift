import ColdframeDesignTokens

/// The hatch fill (UX-DR12, DESIGN.md Shapes): 135° lines of `status-hatch-line`, 1.5 pt wide,
/// every 8 pt on `status-hatch-ground`, with a solid `status-hatch-ground` plate under text. Used
/// by the unknown and needs-calibration Lot tiles and by unsupported Wi-Fi rows.
public struct HatchPresentation: Equatable, Sendable {
  public let angleDegrees: Double
  public let lineWidth: Double
  public let spacing: Double
  public let line: ThemedColor
  public let ground: ThemedColor
  /// The solid plate text sits on.
  public let plate: ThemedColor

  public static let standard = HatchPresentation(
    angleDegrees: 135, lineWidth: 1.5, spacing: 8, line: ColorTokens.statusHatchLine,
    ground: ColorTokens.statusHatchGround, plate: ColorTokens.statusHatchGround)

  /// Where each line starts on the bottom edge; a line rises to the top edge as far to the
  /// right as the area is high, so the first ones start left of it.
  public func lineStarts(width: Double, height: Double) -> [Double] {
    guard spacing > 0, width > 0, height > 0 else { return [] }
    return Array(stride(from: -height, to: width, by: spacing))
  }
}

#if canImport(SwiftUI)
  import SwiftUI

  /// The hatch fill, drawn behind a tile or row.
  struct Hatch: View {
    let palette: ColdframePalette
    var hatch = HatchPresentation.standard

    var body: some View {
      Canvas { context, size in
        let width = Double(size.width)
        let height = Double(size.height)
        context.fill(
          Path(CGRect(origin: .zero, size: size)), with: .color(palette.color(hatch.ground)))
        for x in hatch.lineStarts(width: width, height: height) {
          var line = Path()
          line.move(to: CGPoint(x: x, y: height))
          line.addLine(to: CGPoint(x: x + height, y: 0))
          context.stroke(
            line, with: .color(palette.color(hatch.line)), lineWidth: hatch.lineWidth)
        }
      }
      .accessibilityHidden(true)
    }
  }

  extension View {
    /// Text on a hatched tile or row sits on a solid plate.
    func plate(_ on: Bool, _ palette: ColdframePalette) -> some View {
      background(on ? palette.color(HatchPresentation.standard.plate) : Color.clear)
    }
  }
#endif
