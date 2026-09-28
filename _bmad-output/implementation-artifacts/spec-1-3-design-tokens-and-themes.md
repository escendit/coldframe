---
title: 'Story 1.3: Design tokens and themes'
type: 'feature'
created: '2026-09-28'
status: 'done'
baseline_revision: 'f40cc8d729fa649cbdc61f5b9aa72a19dc08d760'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/DESIGN.md'
warnings:
  - oversized
deferred:
  - summary: >-
      DESIGN.md sets hero-value, tile-value and tile-value-web to Ubuntu Condensed weight 300, but Ubuntu Condensed exists only in 400, so those roles render at 400.
    evidence: |-
      google/fonts ufl/ubuntucondensed ships only UbuntuCondensed-Regular.ttf; generated fonts.css has no Ubuntu Condensed 300 face. The token copies DESIGN.md faithfully; the design needs a decision (use 400, or Ubuntu Light for values).
    location: >-
      packages/design-tokens/tokens/tokens.json (hero-value, tile-value, tile-value-web)
    severity: low
---

<intent-contract>

## Intent

**Problem:** No client has a colour, type, spacing or icon source. Stories 1.4, 1.5, 1.8 and 1.9 build UI on iOS, Android and the web, and all three must look like one Escendit product and meet the contrast floor (UX-DR1–UX-DR11, UX-DR13, NFR14).

**Approach:** Vendor `escendit/branding` `css/theme.css`, the Carbon icon subset and the Ubuntu fonts into `packages/design-tokens`, hold one JSON token source there, and generate committed Swift, Kotlin and CSS/TS outputs from it with a Node script. CI tests prove the outputs are fresh, match DESIGN.md's frontmatter and the vendored DS values, and meet the load-bearing contrast table.

## Boundaries & Constraints

**Always:**
- One token source: `packages/design-tokens/tokens/tokens.json`. Every colour, typography, spacing and rounded token in DESIGN.md's frontmatter appears in it with the same value; a colour without a `-dark` twin gets `dark` = `light`. `components` are composites, not tokens, and are not generated.
- Generated files are committed and carry a "generated, do not edit" header. A check regenerates in memory and fails when any committed output differs.
- Colours keep alpha (`overlay` `#16161680` / `#161616B3`). Swift uses `0xRRGGBBAA` `UInt32`; Kotlin uses Android `0xAARRGGBB` `Long`; CSS uses `#RRGGBBAA` hex as written in DESIGN.md.
- CSS custom properties use the `--cf-` prefix. Light values sit in `:root, [data-theme="light"]`; dark values in `[data-theme="dark"]` and in `@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) }`, so "System" is the absence of `data-theme`.
- Typography: weights 300 or 400 only. Web sizes are `rem` (px / 16); roles with base size ≥ 36 px are capped at 2× as `min(<rem>, <2×px>px)`. Android sizes are `sp` with `maxFontScale = 2` for roles ≥ 36 px. iOS roles name a Dynamic Type text style and `maximumPointSize = 2 × size` for roles ≥ 36 px. `status-label` and `button` carry an uppercase flag; no string is upper-cased.
- `focus` is never orange, overriding the DS `--color-focus`; `ink-on-bright` overrides DS white `text-on-color` on orange. Both deviations are written down in `packages/design-tokens/README.md` with the pending upstream fix.
- Icons: exactly the UX-DR13 list, taken from `@carbon/icons` `svg/32/`, geometry only, no colour anywhere in the platform outputs. Web SVGs set `fill="currentColor"` on the root and nowhere else.
- Vendored third-party files keep their licence file and a `SOURCE.md` naming origin, version or commit and licence. The Swift and Kotlin libraries are Foundation-only / Kotlin common code; no Compose, UIKit or app shell.
- Tests live under `tests/<lang>/`, mirroring the code, and are written failing first.

**Never:**
- No dependency on `@escendit/branding` or any network fetch at build or test time (no Google Fonts).
- No shadow, elevation, blur or gradient tokens; every `rounded` token is 0.
- No hatch primitive (UX-DR12), theme-switcher UI, SwiftUI/Compose colour adapters, or font registration in mobile apps — they arrive with the app shells.
- Do not edit DESIGN.md or `sprint-status.yaml`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Fresh outputs | committed outputs match `tokens.json` + icons | check passes | No error expected |
| Stale output | a generated file edited or `tokens.json` changed without regenerating | check fails naming the stale file | exit 1 / failing test |
| Contrast breach | a text pair below 4.5:1 or a non-text pair below 3:1 in either theme | contrast check fails naming pair, theme and ratio | exit 1 / failing test |
| Published drift | computed ratio differs from DESIGN.md's published value by > 0.01 | check fails naming the pair | failing test |
| DS drift | a token with a `ds` mapping differs from the vendored `theme.css` value | test fails naming token and theme | failing test |
| DESIGN.md drift | a frontmatter token missing from or different in `tokens.json` | test fails naming the token | failing test |
| Unsupported SVG | an icon uses an element or attribute the normaliser cannot convert | generation fails naming icon and element | exit 1 |

</intent-contract>

## Code Map

- `../branding/css/theme.css` (commit `67c33f39c49e118be6785d11ebdea1f3bc5191f8`, Apache-2.0) -- copy verbatim to `packages/design-tokens/vendor/escendit-branding/theme.css`. Palette in `@theme` (`--color-primary-600` `#cf5d00`, `--color-secondary-50` `#f2edff`, `--color-black` `#0a0a0a` …); semantic light values in `:root, [data-theme="light"]` (`--esc-*`), dark overrides in `.dark, [data-theme="dark"]`; `--esc-overlay` is `rgb(22 22 22 / .5)` / `/ .7`; `--color-focus` is orange. Values are lowercase and some are `var(...)`.
- DESIGN.md (`_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/DESIGN.md`) -- frontmatter lines 10–225: `colors`, `typography` (fontSize `36px`, fontWeight `'400'`, lineHeight `'1.1'`, letterSpacing `0.06em`, `note`), `rounded`, `spacing` (keys `'1'`…`'13'` plus named). Contrast table lines 492–507; all 29 expanded pairs were recomputed and match the published values to two decimals.
- `pnpm-workspace.yaml` -- globs `apps/ts/*`, `packages/ts/*`, `tests/ts/*`; add `packages/design-tokens`.
- `packages/ts/api-client/`, `tests/ts/api-client/` -- package/test pattern: `package.json` with `lint` (`eslint --max-warnings 0 .`), `typecheck` (`tsc --project tsconfig.json`), `test` (`vitest run`); `tsconfig.json` extends `tsconfig.base.json` (`noEmit`, strict, `types: []`). Scripts run by Node 24 need `allowImportingTsExtensions`, `erasableSyntaxOnly`, and `types: ["node"]` (add `@types/node`).
- `Package.swift` -- one target at `apps/swift/ios/Sources/ColdframeIOS`, tests at `tests/swift/ios/...`, Swift Testing (`import Testing`, `#expect`). Add a library target and test target; add `platforms: [.iOS(.v17), .macOS(.v14)]`.
- `settings.gradle.kts`, `packages/kt/core/build.gradle.kts` -- KMP `jvm()` only, `explicitApi()`, `jvmToolchain(25)`, tests redirected to `tests/kt/<module>/commonTest/kotlin`, ktlint via `libs.plugins.ktlint`. Copy for `:design-tokens`; generated Kotlin must pass ktlint.
- `.github/workflows/ci.yml` -- `typescript` job runs `pnpm -r typecheck|lint|test`; `kotlin` runs `./gradlew check`; `swift` runs `swift build`, `swift test`, `swift format lint --strict --recursive .` on macOS. New projects are picked up; add one explicit step `pnpm --filter @coldframe/design-tokens run check` in the `typescript` job.
- `docs/quickstart.md`, `packages/ts/README.md`, `packages/kt/README.md` -- tables of folders; add the new packages.
- Local tooling: node 24.21, pnpm 12.6, JDK 25; no Swift — run Swift in a `swift:6.x` container with podman (`podman run --rm -v "$PWD":/src:Z -w /src swift:6.2 ...`); SwiftUI code cannot be compiled on Linux.
- External: `@carbon/icons` 11.89.0 (Apache-2.0) tarball holds `svg/32/<name>.svg`; Ubuntu fonts from `github.com/google/fonts` `ufl/ubuntu/Ubuntu-Light.ttf`, `Ubuntu-Regular.ttf`, `ufl/ubuntucondensed/UbuntuCondensed-Regular.ttf`, `ufl/ubuntumono/UbuntuMono-Regular.ttf`, licence `UFL.txt`. Download once to vendor; never at build time.

## Tasks & Acceptance

**Execution:**
- `tests/ts/design-tokens/` -- write the failing vitest suites first: DESIGN.md sync (parse frontmatter with pinned `yaml`), DS provenance, contrast, freshness, icons, typography-in-CSS, fonts -- test-first (NFR16).
- `packages/design-tokens/vendor/` -- `escendit-branding/theme.css` + `SOURCE.md`; `carbon-icons/svg/<name>.svg` for the 21 UX-DR13 icons + `LICENSE` + `SOURCE.md`; `packages/design-tokens/fonts/` 4 TTFs + `UFL.txt` + `SOURCE.md` -- vendored inputs; mark `*.ttf` binary in `.gitattributes`.
- `packages/design-tokens/tokens/tokens.json` -- colours `{light, dark, ds?}` where `ds` is a CSS custom property name (or `{light?, dark?}` of names) resolved in the vendored theme; typography `{fontFamily, fontSize, fontWeight, lineHeight, letterSpacingEm, uppercase, iosTextStyle}` in px/unitless; spacing and rounded in px -- the single source. Every UX-DR2 token maps to a DS name except `primary-hover` dark, which is local.
- `packages/design-tokens/tokens/contrast.json` -- the 29 pairs `{label, foreground, background, kind: text|non-text, published: {light, dark}}` -- load-bearing table.
- `packages/design-tokens/scripts/` (`generate.ts`, `contrast.ts`, shared modules) + `package.json` (`@coldframe/design-tokens`, scripts `generate`, `check`, `lint`, `typecheck`; exports `.`, `./tokens.css`, `./fonts.css`, `./icons/*`) + `tsconfig.json` -- generator: normalise each icon to one absolute path of `M/L/C/Z` only (arcs → cubics, `H/V` → `L`, `S/Q/T` → `C`, `circle/rect/ellipse/polygon` → path, `transform` applied; skip `data-icon-path="inner-path"` and transparent `fill="none"` rectangles; fail on anything else), then write every output; `check` = freshness + contrast, printing the recomputed table. Pinned dev dependencies allowed (e.g. `svgpath`).
- `packages/design-tokens/generated/` -- `css/tokens.css`, `css/fonts.css` (`@font-face` with relative URLs to `../../fonts/`), `icons/<name>.svg`, `ts/index.ts` (token values and icon path data) -- web outputs.
- `packages/swift/design-tokens/Sources/ColdframeDesignTokens/` -- `Generated/*.swift` (`ColorTokens`, `Typography`, `Spacing`, `Radius`, `CarbonIcon: String, CaseIterable` with `pathData`) + hand-written `ThemedColor`, `TypeRole`, `IOSTextStyle`, `IconPath` parser (Foundation only) and `CarbonIconShape: Shape` under `#if canImport(SwiftUI)` -- iOS output. `Package.swift` -- library `ColdframeDesignTokens` and its test target.
- `packages/kt/design-tokens/` -- `build.gradle.kts`, `generated/` Kotlin (`ColorTokens`, `Typography`, `Spacing`, `Radius`, `CarbonIcon` enum with `pathData`) + hand-written `ThemedColor`, `TypeRole`, `IconPath` parser; register `:design-tokens` in `settings.gradle.kts` -- Android/KMP output.
- `tests/swift/design-tokens/ColdframeDesignTokensTests/`, `tests/kt/design-tokens/commonTest/kotlin/...` -- platform tests: colour count and sample values in both themes, every role's text style / `sp` / cap, all icons parse to non-empty commands inside the 32×32 viewport with no colour.
- `.github/workflows/ci.yml`, `packages/design-tokens/README.md`, `packages/swift/README.md`, `packages/ts/README.md`, `packages/kt/README.md`, `docs/quickstart.md` -- CI step; how to regenerate, naming map, deviations, icon/font sources.

**Acceptance Criteria:**
- Given the repository, when I inspect `packages/design-tokens` and every `package.json`, then a copied `theme.css` with its source commit is present and nothing depends on `@escendit/branding`.
- Given DESIGN.md's frontmatter, when the sync test runs, then every `colors`, `typography`, `spacing` and `rounded` token exists in `tokens.json` with the same light and dark values, and the Swift, Kotlin and CSS outputs each contain every one of them.
- Given the generated tokens, when `pnpm --filter @coldframe/design-tokens run check` runs in CI, then it prints the recomputed contrast table for both themes and exits non-zero if any text pair is below 4.5:1 or any non-text pair below 3:1.
- Given the 21 UX-DR13 icons, when the three platform test suites run, then each icon is available on web (SVG, `currentColor`), Swift and Kotlin (colourless path data) and every one parses into drawable commands.
- Given each platform, when a test reads the typography tokens, then every role maps to an iOS Dynamic Type text style, an Android `sp` size and a web `rem` size, with the 2× cap on roles ≥ 36 px.
- Given the workspace, when CI runs `pnpm -r lint|typecheck|test`, `./gradlew check` and `swift build && swift test && swift format lint --strict --recursive .`, then all pass.

## Spec Change Log

## Review Triage Log

### 2026-09-28 — Review pass
- verdicts: 32 findings — high 0, medium 0, low 26, false 6, maybe-false 0
- findings:
  - `[low]` `[reject]` Blind: contrast check covers only the 19 table pairs; untabled combinations (text-helper on dark layer-02, text-on-color on primary…) fail — the AC scopes the gate to DESIGN.md's load-bearing table; those combinations are not specified usages, and an allowed-pairs matrix is new design.
  - `[low]` `[patch]` Blind: nothing ties contrast.json to DESIGN.md's table — added a test comparing the table's published ratios per theme with contrast.json.
  - `[low]` `[reject]` Blind: `contrastRatio` ignores alpha — no pair uses a translucent token; a guard covers a state not shown.
  - `[low]` `[reject]` Blind: tokens.json/contrast.json are cast, not validated — a missing value is caught by the DESIGN.md sync test and by compilation; a schema validator is added complexity.
  - `[low]` `[patch]` Blind: README claims every value equal to a DS entry has a `ds` mapping — reworded: `ds` records the property a value is taken from.
  - `[low]` `[reject]` Blind: Swift `commands` swallows parse errors and re-parses per layout — every icon's data is parsed in the Swift and TS tests; 21 short strings make re-parsing negligible.
  - `[low]` `[patch]` Blind: README implies a Compose icon adapter exists — reworded: the Android shell (Story 1.5) builds an ImageVector; no Compose code ships here.
  - `[low]` `[reject]` Blind: TypeRole has no PostScript name — derivable from family + weight when the shells register fonts (1.5); adding it now is new public surface.
  - `[false]` `[reject]` Blind: CRLF checkouts make generated files stale — `.gitattributes` line 1 is `* text=auto eol=lf`, so text files check out as LF everywhere.
  - `[low]` `[reject]` Blind: web fonts are TTF only, no WOFF2 — size only; the spec chose one format for all platforms.
  - `[low]` `[reject]` Blind: SVGs carry no aria-hidden/focusable — labelling is decided per usage by web components (1.4 on).
  - `[low]` `[patch]` Blind: README "Change a token" lacks DESIGN.md path and hard-coded test counts — both added.
  - `[false]` `[reject]` Blind: Package.swift platforms constrain ColdframeIOS — the spec asks for them and ColdframeIOS is an iOS 17 app target with no older consumer.
  - `[low]` `[defer]` Edge: Ubuntu Condensed weight 300 has no vendored face — the font does not exist in 300; pre-existing in DESIGN.md; deferred for a design decision.
  - `[low]` `[reject]` Edge: JSON inputs are not validated — same as the Blind finding.
  - `[low]` `[reject]` Edge: member-name collisions or keywords in generated Swift/Kotlin — current names compile; hypothetical, a guard adds complexity.
  - `[false]` `[reject]` Edge: merging elements into one nonzero path makes overlaps holes — each of the 13 multi-element icons was inspected; every inner element sits inside the outer shape's hole, so no region overlaps.
  - `[low]` `[reject]` Edge: Swift `try?` hides parse errors — same as the Blind finding.
  - `[low]` `[reject]` Edge: contrast of translucent tokens — same as the Blind alpha finding.
  - `[low]` `[reject]` Edge: rgb() channels above 255 in theme.css — the vendored file is fixed and valid; hypothetical.
  - `[low]` `[reject]` Edge: TS and platform parsers differ on tabs or NaN — generated data is single-spaced finite numbers, checked by tests.
  - `[false]` `[reject]` Edge: spec says 29 pairs, so ten are unchecked — DESIGN.md's table expands to 19 pairs; all are checked. The spec's count is a wording slip; editing the spec is not a fix.
  - `[low]` `[reject]` Edge: setup-done light is a second undeclared local value — true (no DS green equals #2E7D00) and documented in the README deviations; the fix is a spec edit.
  - `[low]` `[patch]` Gap: `findStale`'s orphan branch is untested — added a test with a stray generated file in a temporary root.
  - `[low]` `[patch]` Gap: `CarbonIconShape.path(in:)` is untested — added `CarbonIconShapeTests.swift` under `canImport(SwiftUI)` (scale and centring for all icons); runs on macOS CI only.
  - `[low]` `[patch]` Gap: `ThemedColor.components` test cannot see swapped channels — added a four-channel assertion on `primary`.
  - `[low]` `[reject]` Intent: on mobile, "inherits the current colour" is shown as "stores no colour" — SwiftUI's Shape fills with the foreground style; no Android/Compose target exists until 1.5, and adding one is new surface.
  - `[low]` `[reject]` Intent: typography maps to data, not UIFontMetrics/TextUnit — those APIs belong to the app shells (1.4/1.5); the mapping is fully defined in the tokens.
  - `[low]` `[patch]` Intent: drift is checked against contrast.json, not DESIGN.md — grouped with the Blind contrast-table finding; same test added.
  - `[low]` `[reject]` Intent: the CLI's exit 1 is not exercised by a test — verified by running it against a lowered primary-text (exit 1, both pairs named); a subprocess test adds little.
  - `[false]` `[reject]` Intent: spec says 29 pairs while diff has 19 — same as the Edge finding; the diff matches DESIGN.md.
  - `[false]` `[reject]` Intent: Swift/Kotlin tests only spot-check against DESIGN.md — `design-md-sync.test.ts` checks every DESIGN.md token in the Swift and Kotlin outputs.

## Design Notes

- **iOS text styles.** A role's DESIGN.md `note` names its style (`headline` → `largeTitle`, `title` → `title`, `section` → `title3`, `hero-value` → `largeTitle`, `tile-name` → `body`, `body` → `subheadline`, `helper` → `caption1`). Roles without one take the iOS style whose default size is nearest (ties go larger): `tile-value`, `step-counter` → `largeTitle`; `tile-value-web` → `largeTitle`; `status-label` → `footnote`; `body-lg` → `callout`; `meta-mono` → `caption1`; `button` → `subheadline`.
- **Path data as strings.** Icon geometry is a normalised string per icon, parsed at runtime by a ~40-line parser on each platform. Large literal lists would hit the JVM method-size limit and bloat Swift type-checking; the string keeps generated code small and identical across platforms.
- **Colour is inherited, not stored.** Platform outputs hold geometry only; SwiftUI fills `CarbonIconShape` with the environment foreground style, Compose's `Icon` tints with `LocalContentColor`, and web SVGs use `currentColor`.
- **Why a JSON source beside DESIGN.md.** DESIGN.md is the spec; `tokens.json` is the repository's copy with DS provenance. Two tests keep it honest in both directions.

## Verification

**Commands:**
- `pnpm install --frozen-lockfile && pnpm -r typecheck && pnpm -r lint && pnpm -r test` -- expected: exit 0
- `pnpm --filter @coldframe/design-tokens run check` -- expected: prints the table, exit 0; exits 1 after lowering `primary-text` light to `#D06020` in a scratch copy
- `./gradlew check` -- expected: BUILD SUCCESSFUL
- `podman run --rm -v "$PWD":/src:Z -w /src swift:6.2 sh -c 'swift build && swift test && swift format lint --strict --recursive .'` -- expected: exit 0

**Manual checks (if no CLI):**
- `git grep -n "@escendit/branding"` returns only documentation lines.

## Auto Run Result

Status: done

**Summary.** `packages/design-tokens` now holds a verbatim copy of `escendit/branding` `css/theme.css` (commit `67c33f3`), 21 Carbon icons (`@carbon/icons` 11.89.0) and four Ubuntu TTFs, each with licence and `SOURCE.md`, plus one token source (`tokens/tokens.json`) and the contrast table (`tokens/contrast.json`). A Node generator writes committed outputs for the web (`--cf-` CSS with light, dark and System via `prefers-color-scheme`, `@font-face`, `currentColor` SVGs, a TS module), Swift (`ColdframeDesignTokens` target) and Kotlin (`:design-tokens` module). `check` fails on stale outputs and prints the recomputed contrast table, failing below 4.5:1 text / 3:1 non-text; CI runs it in the TypeScript job. Nothing depends on `@escendit/branding`. No acceptance criterion needs a human outside the repository.

**Files changed.**
- `packages/design-tokens/` -- vendored inputs, token and contrast sources, generator scripts, generated web outputs, README (naming map, deviations, how to regenerate)
- `packages/swift/design-tokens/`, `Package.swift` -- Swift library with generated tokens, icon parser and `CarbonIconShape`; platforms iOS 17 / macOS 14
- `packages/kt/design-tokens/`, `settings.gradle.kts` -- Kotlin module with generated tokens and icon parser
- `tests/ts/design-tokens/`, `tests/swift/design-tokens/`, `tests/kt/design-tokens/` -- DESIGN.md sync, DS provenance, contrast, freshness, icons, typography, fonts; platform tests
- `.github/workflows/ci.yml` -- design-token check step; `pnpm-workspace.yaml`, `pnpm-lock.yaml`, `.gitattributes`, READMEs, `docs/quickstart.md`

**Review.** 32 findings: 8 patched (all low: contrast table tied to DESIGN.md, orphan-output test, `CarbonIconShape` test, colour-channel test, three README corrections), 1 deferred (Ubuntu Condensed has no weight 300), 23 rejected with reasons in the triage log.

**Follow-up review recommended: false.** Patched: high 0, medium 0, low 8.

**Verification** (all on this machine after the patches):
- `pnpm install --frozen-lockfile`, `pnpm -r typecheck`, `pnpm -r lint` -- exit 0; `pnpm -r test` -- 596 + 1 passed
- `pnpm --filter @coldframe/design-tokens run check` -- 34 outputs fresh, 38 rows pass; with `primary-text` light set to `#D06020` it exits 1 naming both pairs
- `./gradlew check` -- success
- `swift build`, `swift test` (17 passed), `swift format lint --strict --recursive .` in a `swift:6.3.3` podman container -- exit 0

**Residual risks.**
- `CarbonIconShape` and `CarbonIconShapeTests` compile only with SwiftUI and have not been built; the macOS CI job is their first run.
- Mobile font registration, SwiftUI/Compose colour adapters and an Android icon renderer arrive with the app shells (1.4/1.5).
- Hero and tile values render at 400 until the Ubuntu Condensed 300 question (deferred) is decided.

