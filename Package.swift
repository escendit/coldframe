// swift-tools-version: 6.0

import PackageDescription

// The String Catalog needs Apple's resource tooling, which Linux toolchains lack; on Linux the
// catalogue is left out and the code that reads it is compiled only where SwiftUI exists.
#if os(Linux)
  let iosResources: [Resource] = []
  let iosExcludes = ["Resources"]
#else
  let iosResources: [Resource] = [.process("Resources")]
  let iosExcludes: [String] = []
#endif

// Swift code keeps the monorepo layout: sources under apps/swift or packages/swift, tests under
// tests/swift.
let package = Package(
  name: "Coldframe",
  platforms: [.iOS(.v17), .macOS(.v14)],
  products: [
    .library(name: "ColdframeIOS", targets: ["ColdframeIOS"]),
    .library(name: "ColdframeDesignTokens", targets: ["ColdframeDesignTokens"]),
  ],
  targets: [
    .target(
      name: "ColdframeIOS",
      dependencies: ["ColdframeDesignTokens"],
      path: "apps/swift/ios/Sources/ColdframeIOS",
      exclude: iosExcludes,
      resources: iosResources
    ),
    .testTarget(
      name: "ColdframeIOSTests",
      dependencies: ["ColdframeIOS", "ColdframeDesignTokens"],
      path: "tests/swift/ios/ColdframeIOSTests"
    ),
    // Colours, typography, spacing, radii and icons; Sources/.../Generated is written by
    // packages/design-tokens (`pnpm --filter @coldframe/design-tokens run generate`).
    .target(
      name: "ColdframeDesignTokens",
      path: "packages/swift/design-tokens/Sources/ColdframeDesignTokens"
    ),
    .testTarget(
      name: "ColdframeDesignTokensTests",
      dependencies: ["ColdframeDesignTokens"],
      path: "tests/swift/design-tokens/ColdframeDesignTokensTests"
    ),
  ]
)
