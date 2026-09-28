import Foundation
import Testing

@testable import ColdframeIOS

private func offenders(_ pattern: String, in path: String) throws -> [String] {
  let expression = try NSRegularExpression(pattern: pattern)
  var found: [String] = []
  for file in Repo.swiftSources(path) {
    let text = try String(contentsOf: file, encoding: .utf8)
    for (index, line) in text.components(separatedBy: "\n").enumerated() {
      let range = NSRange(line.startIndex..., in: line)
      if expression.firstMatch(in: line, range: range) != nil {
        found.append("\(file.lastPathComponent):\(index + 1)")
      }
    }
  }
  return found
}

@Test("UX-DR101 state changes swap instantly: no animations or transitions in the iOS shell")
func noAnimations() throws {
  #expect(!Motion.animatesStateChanges)
  #expect(!Repo.swiftSources("apps/swift/ios").isEmpty)
  let pattern = #"withAnimation|\.animation\(\.|\.transition\(|matchedGeometryEffect|repeatForever"#
  #expect(try offenders(pattern, in: "apps/swift/ios") == [])
}

@Test("UX-DR114 no spinners, toasts, carousels, snooze or streaks in the iOS shell")
func noBannedPatterns() throws {
  let pattern = #"ProgressView|[Tt]oast|\.page\b|PageTabViewStyle|[Cc]arousel|[Ss]nooze|[Ss]treak"#
  #expect(try offenders(pattern, in: "apps/swift/ios") == [])
}

@Test("UX-DR113 tap to act only: no long-press actions")
func noLongPress() throws {
  #expect(try offenders(#"onLongPressGesture|LongPressGesture"#, in: "apps/swift/ios") == [])
}

@Test("UX-DR124 no literal copy in the SwiftUI views")
func noLiteralCopy() throws {
  let pattern = #"Text\("|Button\("|accessibilityLabel\(""#
  #expect(try offenders(pattern, in: "apps/swift/ios") == [])
}

@Test func theShellNeverNamesATokenAndHasNoAddressField() throws {
  let tokens = #"accessToken|refreshToken|idToken|access_token|refresh_token|id_token"#
  #expect(try offenders(tokens, in: "apps/swift/ios") == [])
  let screens = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/Screens.swift")
  let signIn = screens.components(separatedBy: "struct AppTabView").first ?? ""
  #expect(!signIn.contains("TextField"))
  #expect(!signIn.contains("SecureField"))
}
