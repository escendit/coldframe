#if canImport(SwiftUI)
  import SwiftUI

  /// A Carbon icon as a SwiftUI shape, scaled to fit and centred in its frame.
  ///
  /// It holds geometry only: fill it with the environment's foreground style, for example
  /// `CarbonIconShape(.rainDrop).fill(.foreground)`.
  public struct CarbonIconShape: Shape {
    public let icon: CarbonIcon

    public init(_ icon: CarbonIcon) {
      self.icon = icon
    }

    public func path(in rect: CGRect) -> Path {
      let scale = min(rect.width, rect.height) / IconPath.viewportSize
      let originX = rect.minX + (rect.width - IconPath.viewportSize * scale) / 2
      let originY = rect.minY + (rect.height - IconPath.viewportSize * scale) / 2

      func point(_ x: Double, _ y: Double) -> CGPoint {
        CGPoint(x: originX + x * scale, y: originY + y * scale)
      }

      var path = Path()
      for command in icon.commands {
        switch command {
        case .move(let x, let y):
          path.move(to: point(x, y))
        case .line(let x, let y):
          path.addLine(to: point(x, y))
        case .cubic(let x1, let y1, let x2, let y2, let x, let y):
          path.addCurve(to: point(x, y), control1: point(x1, y1), control2: point(x2, y2))
        case .close:
          path.closeSubpath()
        }
      }
      return path
    }
  }
#endif
