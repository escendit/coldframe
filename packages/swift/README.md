# packages/swift

Swift libraries. Each is a SwiftPM target of the `Package.swift` in the repository root, so it
builds and tests without Xcode.

| Folder | What | Arrives in |
| --- | --- | --- |
| `design-tokens/` | `ColdframeDesignTokens`: colours, typography, spacing, radii and Carbon icons; Foundation-free, plus `CarbonIconShape` where SwiftUI exists | Story 1.3; `Generated/` is written by [`packages/design-tokens`](../design-tokens) |

Tests live in [`tests/swift`](../../tests/swift).
