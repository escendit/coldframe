// swift-tools-version: 6.0

import PackageDescription

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
      path: "apps/swift/ios/Sources/ColdframeIOS"
    ),
    .testTarget(
      name: "ColdframeIOSTests",
      dependencies: ["ColdframeIOS"],
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
