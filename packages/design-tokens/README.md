# packages/design-tokens

The single source of Coldframe's colours, typography, spacing, corner radii, icons and fonts, and
the generator that turns it into CSS/TypeScript, Swift and Kotlin. Every client builds its UI from
these outputs, so the web app, iOS and Android look like one Escendit product and meet the
contrast floor (UX-DR1–UX-DR11, UX-DR13, NFR14).

| Path | What |
| --- | --- |
| `tokens/tokens.json` | The single token source: colours (light, dark, DS provenance), typography, spacing, radii |
| `tokens/contrast.json` | The load-bearing contrast table of DESIGN.md › Colors, with the published ratios |
| `vendor/escendit-branding/` | Verbatim copy of `escendit/branding` `css/theme.css`, with `LICENSE` and `SOURCE.md` |
| `vendor/carbon-icons/` | The UX-DR13 subset of `@carbon/icons` `svg/32/`, with `LICENSE` and `SOURCE.md` |
| `fonts/` | Ubuntu Light, Ubuntu, Ubuntu Condensed and Ubuntu Mono, with `UFL.txt` and `SOURCE.md` |
| `scripts/` | `generate.ts`, `contrast.ts` and the modules they share, run by Node 24 directly |
| `generated/` | Web outputs: `css/tokens.css`, `css/fonts.css`, `icons/<name>.svg`, `ts/index.ts` |

Generated platform outputs live next to the code of each platform:

| Platform | Generated | Hand-written |
| --- | --- | --- |
| Swift | [`packages/swift/design-tokens/Sources/ColdframeDesignTokens/Generated`](../swift/design-tokens) | `ThemedColor`, `TypeRole`, `IOSTextStyle`, `IconPath`, `CarbonIconShape` |
| Kotlin | [`packages/kt/design-tokens/generated`](../kt/design-tokens) | `ThemedColor`, `TypeRole`, `IconPath` |

Tests: [`tests/ts/design-tokens`](../../tests/ts/design-tokens) (DESIGN.md sync, DS provenance,
contrast, freshness, icons, typography, fonts), [`tests/swift/design-tokens`](../../tests/swift/design-tokens)
and [`tests/kt/design-tokens`](../../tests/kt/design-tokens).

## Change a token

1. Change DESIGN.md first; it is the spec
   (`_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/DESIGN.md`). Then change `tokens/tokens.json` to match. The sync test
   fails until both agree, in both directions.
2. If the change touches a pair in DESIGN.md's contrast table, update `tokens/contrast.json` too.
3. Regenerate and commit every changed file:

   ```sh
   pnpm --filter @coldframe/design-tokens run generate
   ```

4. Update the counts and lists the tests hard-code: the colour and role counts in the Swift and
   Kotlin tests (`ColorTokensTests`, `TypographyTests`, `ColorTokensTest`, `TypographyTest`), the
   pair counts in `tests/ts/design-tokens/contrast.test.ts`, and the copies of the icon list
   (`ICON_NAMES`, `icons.test.ts`, `CarbonIconTests.swift`, `CarbonIconTest.kt`).
5. Check, as CI does:

   ```sh
   pnpm --filter @coldframe/design-tokens run check
   pnpm --filter @coldframe/design-tokens-tests test
   ```

`check` fails when a committed output differs from a regeneration (it names each stale file), and
prints the recomputed contrast table for both themes, failing when a text pair is below 4.5:1 or a
non-text pair below 3:1. The test suite additionally fails when a ratio drifts more than 0.01 from
the value DESIGN.md publishes, or when a token with a `ds` mapping no longer equals the vendored
theme. Never edit a generated file by hand; each one says so in its header.

## `tokens.json`

- **Colours**: `{ "light", "dark", "ds"? }`. Values are `#RRGGBB` or `#RRGGBBAA` exactly as DESIGN.md
  writes them; a token without a `-dark` twin has `dark` = `light`. `ds` names the custom property of
  the vendored `theme.css` the value comes from, either one name for both themes or
  `{ "light"?, "dark"? }`. A theme without a name holds a local value (see Deviations).
- **Typography**: `{ fontFamily, fontSize (px), fontWeight (300 or 400), lineHeight (unitless),
  letterSpacingEm, uppercase, iosTextStyle }`.
- **Spacing** and **rounded**: px. Every radius is 0.

DESIGN.md's `components` are composites of these tokens, not tokens, and are not generated. There
are no shadow, elevation, blur or gradient tokens.

## Naming map

| DESIGN.md | CSS | TypeScript | Swift | Kotlin |
| --- | --- | --- | --- | --- |
| `colors.text-primary` | `--cf-color-text-primary` | `colors['text-primary']` | `ColorTokens.textPrimary` | `ColorTokens.textPrimary` |
| `colors.layer-01` | `--cf-color-layer-01` | `colors['layer-01']` | `ColorTokens.layer01` | `ColorTokens.layer01` |
| `typography.hero-value` | `--cf-type-hero-value-font-size` (and `-font-family`, `-font-weight`, `-line-height`, `-letter-spacing`, `-text-transform`) | `typography['hero-value']` | `Typography.heroValue` | `Typography.heroValue` |
| `spacing.'5'` | `--cf-spacing-5` | `spacing['5']` | `Spacing.step5` | `Spacing.STEP_5` |
| `spacing.gutter-mobile` | `--cf-spacing-gutter-mobile` | `spacing['gutter-mobile']` | `Spacing.gutterMobile` | `Spacing.GUTTER_MOBILE` |
| `rounded.DEFAULT` | `--cf-radius-default` | `rounded.DEFAULT` | ``Radius.`default` `` | `Radius.DEFAULT` |
| icon `checkmark--outline` | `icons/checkmark--outline.svg` | `icons['checkmark--outline']` | `CarbonIcon.checkmarkOutline` | `CarbonIcon.CHECKMARK_OUTLINE` |

Every colour and typography role is also reachable by its DESIGN.md name through `ColorTokens.all`
and `Typography.all` on Swift and Kotlin.

Value formats: CSS writes colours as in DESIGN.md (`#RRGGBB`, `#RRGGBBAA`); Swift uses `UInt32`
`0xRRGGBBAA`; Kotlin uses the Android colour int `0xAARRGGBB` as a `Long`. Alpha is kept everywhere
(`overlay` is `#16161680` light, `#161616B3` dark).

## Themes on the web

`tokens.css` puts light values in `:root, [data-theme="light"]` and dark values in
`[data-theme="dark"]` and in `@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) }`.
"System" is the absence of `data-theme` on the root element; "Light" and "Dark" set it. Import
`@coldframe/design-tokens/tokens.css` and `@coldframe/design-tokens/fonts.css`.

## Typography across platforms

| | Size | Scaling | Cap for roles ≥ 36 px |
| --- | --- | --- | --- |
| Web | `rem` (px / 16) | browser zoom and font size | `min(<rem>, <2 × px>px)` |
| Android | `sp` | font scale | `maxFontScale = 2` |
| iOS | points | Dynamic Type, relative to `textStyle` | `maximumPointSize = 2 × size` |

iOS text styles come from DESIGN.md's notes (`headline` → Large Title, `title` → Title 1, `section`
→ Title 3, `hero-value` → Large Title, `tile-name` → Body, `body` → Subheadline, `helper` →
Caption 1). Roles without a note take the style whose default size is nearest, ties going larger:
`tile-value`, `tile-value-web`, `step-counter` → Large Title; `status-label` → Footnote; `body-lg`
→ Callout; `meta-mono` → Caption 1; `button` → Subheadline.

`status-label` and `button` carry `uppercase`; apply it with a text transform. No string is ever
upper-cased.

## Icons

The 21 UX-DR13 icons, from `@carbon/icons` 11.89.0 `svg/32/`. The generator normalises each SVG
into one absolute path of `M`, `L`, `C` and `Z` commands: arcs become cubics, `H`/`V` become `L`,
`S`/`Q`/`T` become `C`, `circle`, `rect`, `ellipse` and `polygon` become paths and `transform` is
applied. Inner paths (`data-icon-path="inner-path"`) and transparent `fill="none"` rectangles are
skipped. Anything else — another element, a colour attribute, a rounded rectangle — fails generation,
naming the icon and the element.

The outputs carry geometry only; colour is inherited. Web SVGs set `fill="currentColor"` on the root
and nowhere else. Swift's `CarbonIconShape` (SwiftUI only) is filled with the environment's
foreground style. On Android, the shell (Story 1.5) builds an `ImageVector` from the path data and
Compose's `Icon` then tints it with `LocalContentColor`; no Compose code ships here. Swift and Kotlin hold the path
data as a string and parse it with `IconPath.parse`, which keeps the generated code small and
identical across platforms.

To add an icon, see [`vendor/carbon-icons/SOURCE.md`](vendor/carbon-icons/SOURCE.md).

## Fonts

Ubuntu 300 and 400, Ubuntu Condensed 400 and Ubuntu Mono 400 (Ubuntu Font Licence 1.0). The web app
serves them itself through `fonts.css`; nothing is fetched from Google Fonts. The mobile apps
register them when their shells arrive.

## Deviations from the Escendit Design System

Each of these is a local override until `escendit/branding` is fixed upstream; the tests pin them.

| Token | Coldframe | DS | Why | Upstream |
| --- | --- | --- | --- | --- |
| `focus` | `#161616` light, `#F4F4F4` dark, two-tone with `focus-gap` | `--color-focus` is orange (`var(--color-primary)`) | An orange ring disappears on orange fills and is reserved for *needs water* | Pending fix in `escendit/branding`: a neutral two-tone focus ring |
| `ink-on-bright` | `#0A0A0A` on every orange, green and selected-orange surface, the primary button label included | The DS Button uses white `text-on-color` on orange | White on `#FF770F` is 2.64:1, below 4.5:1 | Pending fix in `escendit/branding`: dark ink on primary buttons |
| `primary-hover` dark | `#FF8A2E` | `--esc-button-primary-hover` has no dark override (`#E06A00`) | DESIGN.md lightens the hover in the dark theme; the value equals the DS dark `--esc-link-primary-hover` | Pending: a dark button-hover token |
| `setup-done` light | `#2E7D00` | No such value in the palette (`--color-green-700` is `#2F9C00`) | DS `support-success` (`#48CF00`, used in dark) is 1.87:1 on light `layer-01`, below 3:1; DESIGN.md sets a darker green that is not in the palette | Pending: the value added to the palette |

Tokens specific to Coldframe (Lot status, chart, `primary-text`, `support-error-text` and others)
have no DS counterpart. `ds` records the DS property a value is taken from, not every DS entry
that happens to hold the same value.
