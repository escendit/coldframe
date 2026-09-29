#if canImport(SwiftUI)
  import SwiftUI
  import Testing

  @testable import ColdframeDesignTokens

  private func isClose(_ a: CGRect, _ b: CGRect, tolerance: CGFloat = 0.001) -> Bool {
    abs(a.minX - b.minX) <= tolerance && abs(a.minY - b.minY) <= tolerance
      && abs(a.width - b.width) <= tolerance && abs(a.height - b.height) <= tolerance
  }

  @Test(arguments: CarbonIcon.allCases)
  func scalesTheViewportToASquareFrame(icon: CarbonIcon) {
    let shape = CarbonIconShape(icon)
    let base = shape.path(in: CGRect(x: 0, y: 0, width: 32, height: 32)).boundingRect
    let scaled = shape.path(in: CGRect(x: 0, y: 0, width: 64, height: 64)).boundingRect
    let expected = CGRect(
      x: base.minX * 2, y: base.minY * 2, width: base.width * 2, height: base.height * 2)
    #expect(isClose(scaled, expected))
  }

  @Test(arguments: CarbonIcon.allCases)
  func centresTheViewportInAWideFrame(icon: CarbonIcon) {
    let shape = CarbonIconShape(icon)
    let base = shape.path(in: CGRect(x: 0, y: 0, width: 32, height: 32)).boundingRect
    let wide = shape.path(in: CGRect(x: 0, y: 0, width: 64, height: 32)).boundingRect
    #expect(isClose(wide, base.offsetBy(dx: 16, dy: 0)))
  }
#endif
