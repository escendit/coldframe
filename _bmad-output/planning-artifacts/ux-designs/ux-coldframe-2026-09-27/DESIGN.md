---
name: Coldframe
status: final
created: 2026-09-27
updated: 2026-09-27
sources:
  - ../../../specs/spec-coldframe/
  - ../../architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md
description: Self-hosted garden watering alerts. Direction B "Plots" (one square tile per Lot, solid orange only when a Lot needs water) rendered on the Escendit Design System (Carbon base, Ubuntu type, 0 radius, borders not shadows). Light and dark themes; theme follows the system.
colors:
  # Escendit DS semantic tokens (escendit/branding css/theme.css, vendored into packages/design-tokens).
  # Unsuffixed = light theme, -dark = dark theme. Tokens without a -dark twin are identical in both.
  background: '#FEFEFE'
  background-dark: '#262626'
  layer-01: '#F4F4F4'
  layer-01-dark: '#393939'
  layer-02: '#FEFEFE'
  layer-02-dark: '#525252'
  field-01: '#F4F4F4'
  field-01-dark: '#393939'
  text-primary: '#161616'
  text-primary-dark: '#F4F4F4'
  text-secondary: '#525252'
  text-secondary-dark: '#C6C6C6'
  text-helper: '#707070'
  text-helper-dark: '#ADADAD'
  text-disabled: '#C6C6C6'
  text-disabled-dark: '#6F6F6F'
  text-on-color: '#FEFEFE'
  border-subtle: '#E0E0E0'
  border-subtle-dark: '#525252'
  border-strong: '#8D8D8D'
  border-strong-dark: '#6F6F6F'
  overlay: '#16161680'
  overlay-dark: '#161616B3'
  # Two-tone focus indicator: ring + gap, visible on every fill including orange
  focus: '#161616'
  focus-dark: '#F4F4F4'
  focus-gap: '#FEFEFE'
  focus-gap-dark: '#262626'
  primary: '#FF770F'
  primary-hover: '#E06A00'
  primary-hover-dark: '#FF8A2E'
  primary-active: '#C25C00'
  # Orange used as text, lines or selection marks (DS orange fails 4.5:1 / 3:1 on light surfaces)
  primary-text: '#B84A00'
  primary-text-dark: '#FF8A2E'
  secondary: '#670FFF'
  button-secondary: '#393939'
  button-secondary-dark: '#6F6F6F'
  header-bg: '#161616'
  header-border: '#393939'
  support-error: '#FF454B'
  support-error-text: '#DA1E28'
  support-error-text-dark: '#FF8389'
  support-success: '#48CF00'
  setup-done: '#2E7D00'
  setup-done-dark: '#48CF00'
  support-warning: '#FFCB0F'
  support-info: '#0F43FF'
  ink-on-bright: '#0A0A0A'
  # Lot status — needsWater (the only solid-orange tile)
  status-water-fill: '#FF770F'
  status-water-ink: '#0A0A0A'
  status-water-level: '#CF5D00'
  # Lot status — ok (neutral tile, soil-level fill)
  status-ok-fill: '#F4F4F4'
  status-ok-fill-dark: '#393939'
  status-ok-level: '#E0E0E0'
  status-ok-level-dark: '#474747'
  status-ok-border: '#8D8D8D'
  status-ok-border-dark: '#8D8D8D'
  status-level-edge: '#525252'
  status-level-edge-dark: '#C6C6C6'
  status-low-marker: '#8D8D8D'
  status-low-marker-dark: '#ADADAD'
  # Lot status — unknown and needsCalibration: 1.5 px hatch lines on a solid ground, text on a solid plate
  status-hatch-ground: '#F4F4F4'
  status-hatch-ground-dark: '#393939'
  status-hatch-line: '#8D8D8D'
  status-hatch-line-dark: '#8D8D8D'
  status-unknown-border: '#8D8D8D'
  status-calibration-border: '#9C8400'
  status-calibration-border-dark: '#FFCB0F'
  status-calibration-ink: '#6B5D00'
  status-calibration-ink-dark: '#FFCB0F'
  # Lot status — paused (purple outline)
  status-paused-fill: '#F2EDFF'
  status-paused-fill-dark: '#262626'
  status-paused-border: '#670FFF'
  status-paused-border-dark: '#8945FF'
  status-paused-ink: '#43009C'
  status-paused-ink-dark: '#C5A6FF'
  # Lot status — noNode (dotted empty outline)
  status-no-node-border: '#8D8D8D'
  status-no-node-border-dark: '#6F6F6F'
  status-no-node-ink: '#525252'
  status-no-node-ink-dark: '#ADADAD'
  # Transport staleness (client-side only, AD-14)
  stale-border: '#8D8D8D'
  stale-ink: '#6B5D00'
  stale-ink-dark: '#FFCB0F'
  # History chart
  chart-bar: '#8D8D8D'
  chart-bar-dark: '#8D8D8D'
  chart-bar-below-low: '#B84A00'
  chart-bar-below-low-dark: '#FF770F'
  chart-band: '#F2FFE3'
  chart-band-dark: '#124000'
  chart-high-line: '#8D8D8D'
typography:
  # Base sizes at default text size. iOS scales with Dynamic Type (relative to the noted text style),
  # Android with font scale (sp), web with browser zoom (rem). DS loads Ubuntu 300 and 400 only.
  headline:
    fontFamily: Ubuntu Condensed
    fontSize: 36px
    fontWeight: '400'
    lineHeight: '1.1'
    note: 'Scales like iOS Large Title · Android Headline Large'
  title:
    fontFamily: Ubuntu Condensed
    fontSize: 28px
    fontWeight: '400'
    lineHeight: '1.2'
    note: 'Scales like iOS Title 1 · Android Headline Medium'
  section:
    fontFamily: Ubuntu Condensed
    fontSize: 20px
    fontWeight: '400'
    lineHeight: '1.2'
    note: 'DS h3 · iOS Title 3 · Android Title Large'
  hero-value:
    fontFamily: Ubuntu Condensed
    fontSize: 72px
    fontWeight: '300'
    lineHeight: '1'
    note: 'Lot detail current value and the Calibration reference raw value; scales like iOS Large Title, capped at 2x'
  tile-value:
    fontFamily: Ubuntu Condensed
    fontSize: 36px
    fontWeight: '300'
    lineHeight: '1'
  tile-value-web:
    fontFamily: Ubuntu Condensed
    fontSize: 52px
    fontWeight: '300'
    lineHeight: '1'
  tile-name:
    fontFamily: Ubuntu
    fontSize: 17px
    fontWeight: '400'
    lineHeight: '1.25'
    note: 'iOS Body · Android Body Large'
  status-label:
    fontFamily: Ubuntu Condensed
    fontSize: 13px
    fontWeight: '400'
    lineHeight: '1.2'
    letterSpacing: 0.06em
    note: 'Uppercase via style, never in the string'
  body:
    fontFamily: Ubuntu
    fontSize: 14px
    fontWeight: '400'
    lineHeight: '1.43'
    note: 'DS body · iOS Subheadline · Android Body Medium'
  body-lg:
    fontFamily: Ubuntu
    fontSize: 16px
    fontWeight: '400'
    lineHeight: '1.5'
  helper:
    fontFamily: Ubuntu
    fontSize: 12px
    fontWeight: '400'
    lineHeight: '1.34'
    note: 'iOS Caption 1 · Android Body Small'
  meta-mono:
    fontFamily: Ubuntu Mono
    fontSize: 12px
    fontWeight: '400'
    lineHeight: '1.3'
    note: 'Timestamps, Device IDs, raw values, tile foot line'
  button:
    fontFamily: Ubuntu Condensed
    fontSize: 14px
    fontWeight: '400'
    lineHeight: '1'
    letterSpacing: 0.02em
    note: 'DS text-cta: uppercase via style'
  step-counter:
    fontFamily: Ubuntu Condensed
    fontSize: 56px
    fontWeight: '400'
    lineHeight: '1'
rounded:
  none: 0px
  DEFAULT: 0px
spacing:
  # DS/Carbon spacing scale (spacing-01 … spacing-13)
  '1': 2px
  '2': 4px
  '3': 8px
  '4': 12px
  '5': 16px
  '6': 24px
  '7': 32px
  '8': 40px
  '9': 48px
  '10': 64px
  '11': 80px
  '12': 96px
  '13': 160px
  gutter-mobile: 16px
  gutter-web: 32px
  tile-gap: 8px
  tile-padding: 12px
  tile-padding-web: 16px
  alerts-rail-web: 340px
  field-height: 40px
  button-height: 48px
  header-height: 48px
  focus-ring: 2px
  focus-offset: 2px
  one-column-web: 400px
components:
  focus-indicator:
    ring: '{spacing.focus-ring} solid {colors.focus}'
    gap: '{spacing.focus-offset} {colors.focus-gap}'
    placement: 'outside the component (offset); inset where neighbours abut, gap between ring and fill'
  lot-tile-needs-water:
    background: '{colors.status-water-fill}'
    foreground: '{colors.status-water-ink}'
    level: '{colors.status-water-level}'
    level-edge: '2px solid {colors.status-water-ink}'
    low-marker: '{colors.status-water-ink}'
    border: none
    icon: rain-drop
    radius: '{rounded.none}'
  lot-tile-ok:
    background: '{colors.status-ok-fill}'
    foreground: '{colors.text-primary}'
    foot: '{colors.text-secondary}'
    level: '{colors.status-ok-level}'
    level-edge: '2px solid {colors.status-level-edge}'
    low-marker: '{colors.status-low-marker}'
    border: '1px solid {colors.status-ok-border}'
    icon: checkmark--outline
    radius: '{rounded.none}'
  lot-tile-unknown:
    background: 'hatch 135°, 1.5px {colors.status-hatch-line} lines every 8px on {colors.status-hatch-ground}'
    plate: '{colors.status-hatch-ground}'
    foreground: '{colors.text-primary}'
    foot: '{colors.text-secondary}'
    border: '1px dashed {colors.status-unknown-border}'
    icon: help
    radius: '{rounded.none}'
  lot-tile-needs-calibration:
    background: 'hatch 135°, 1.5px {colors.status-hatch-line} lines every 8px on {colors.status-hatch-ground}'
    plate: '{colors.status-hatch-ground}'
    foreground: '{colors.text-primary}'
    foot: '{colors.text-secondary}'
    label: '{colors.status-calibration-ink}'
    border: '2px dashed {colors.status-calibration-border}'
    icon: tools
    radius: '{rounded.none}'
  lot-tile-paused:
    background: '{colors.status-paused-fill}'
    foreground: '{colors.status-paused-ink}'
    border: '2px solid {colors.status-paused-border}'
    icon: pause--outline
    radius: '{rounded.none}'
  lot-tile-no-node:
    background: transparent
    foreground: '{colors.status-no-node-ink}'
    border: '1px dotted {colors.status-no-node-border}'
    icon: add
    radius: '{rounded.none}'
  lot-tile-stale:
    background: transparent
    foreground: '{colors.text-secondary}'
    timestamp: '{colors.stale-ink}'
    border: '1px solid {colors.stale-border}'
    icon: cloud--offline
    radius: '{rounded.none}'
  lot-tile-skeleton:
    background: transparent
    border: '1px solid {colors.border-subtle}'
  site-summary-header:
    headline: '{typography.headline}'
    subline: '{typography.body}'
    foreground: '{colors.text-primary}'
  site-menu:
    trigger-icon: overflow-menu--vertical
    item: '{typography.body-lg}'
  site-switcher:
    trigger-icon: chevron--down
    row: '{typography.body-lg}'
    role: '{typography.status-label}'
    current-mark: '{colors.primary-text}'
  stale-header:
    age: '{typography.headline}'
    age-color: '{colors.stale-ink}'
    body: '{typography.body}'
  alert-row-needs-water:
    background: '{colors.status-water-fill}'
    foreground: '{colors.status-water-ink}'
    title: '{typography.section}'
    icon: rain-drop
    radius: '{rounded.none}'
  alert-row-threshold:
    background: '{colors.layer-01}'
    foreground: '{colors.text-primary}'
    border: '2px solid {colors.border-strong}'
    title: '{typography.section}'
    icon: 'arrow--up (above high) / arrow--down (below low)'
    radius: '{rounded.none}'
  alert-row-health:
    background: 'hatch 135°, 1.5px {colors.status-hatch-line} lines every 8px on {colors.status-hatch-ground}'
    plate: '{colors.status-hatch-ground}'
    foreground: '{colors.text-primary}'
    border: '1px dashed {colors.status-unknown-border}'
    title: '{typography.section}'
    icon: 'help (silent) / battery--low (battery) / tools (uncalibrated)'
  alert-row-closed:
    background: transparent
    foreground: '{colors.text-secondary}'
    border: '1px solid {colors.border-subtle}'
  lot-detail-hero:
    value: '{typography.hero-value}'
    background-needs-water: '{colors.status-water-fill}'
    foreground-needs-water: '{colors.status-water-ink}'
    background-other: '{colors.background}'
  sensor-cell:
    background: '{colors.layer-01}'
    label: '{typography.helper}'
    value: '{typography.title}'
    border: '1px solid {colors.border-subtle}'
  device-cell:
    background: '{colors.layer-01}'
    value: '{typography.title}'
    meta: '{typography.helper}'
  devices-list:
    row-background: '{colors.background}'
    row-border: '1px solid {colors.border-subtle}'
    id: '{typography.meta-mono}'
    name: '{typography.body-lg}'
    meta: '{typography.helper}'
    silent-icon: help
    paused-icon: pause--outline
    paused-label: '{colors.status-paused-ink}'
  history-chart:
    bar: '1px solid {colors.chart-bar}, no fill'
    bar-below-low: 'solid {colors.chart-bar-below-low}'
    band: '{colors.chart-band}'
    low-line: '2px solid {colors.primary-text}'
    high-line: '1px dashed {colors.chart-high-line}'
    axis: '{typography.meta-mono}'
  button-primary:
    background: '{colors.primary}'
    foreground: '{colors.ink-on-bright}'
    hover: '{colors.primary-hover}'
    active: '{colors.primary-active}'
    height: '{spacing.button-height}'
    radius: '{rounded.none}'
    label: '{typography.button}'
  button-secondary:
    background: '{colors.button-secondary}'
    foreground: '{colors.text-on-color}'
    radius: '{rounded.none}'
    label: '{typography.button}'
  button-ghost:
    background: transparent
    foreground: '{colors.primary-text}'
    label: '{typography.button}'
  text-input:
    background: '{colors.field-01}'
    foreground: '{colors.text-primary}'
    border-bottom: '1px solid {colors.border-strong}'
    invalid: '2px solid {colors.support-error}'
    invalid-text: '{colors.support-error-text}'
    invalid-icon: error--filled
    height: '{spacing.field-height}'
    radius: '{rounded.none}'
  segmented-choice:
    background: '{colors.layer-01}'
    selected-background: '{colors.primary}'
    selected-foreground: '{colors.ink-on-bright}'
    selected-icon: checkmark
    radius: '{rounded.none}'
  selectable-tile:
    background: '{colors.layer-01}'
    selected-border: '2px solid {colors.primary-text}'
    selected-icon: checkmark
    disabled-foreground: '{colors.text-disabled}'
    radius: '{rounded.none}'
  setup-flow-shell:
    current: '{typography.step-counter}'
    current-color: '{colors.primary-text}'
    total-color: '{colors.text-helper}'
  setup-progress:
    done: '{colors.setup-done}'
    done-icon: checkmark
    active: '{colors.primary}'
    active-icon: in-progress
    pending: '1px solid {colors.border-strong}, no fill'
    label: '{typography.status-label}'
  setup-code-field:
    field: '{typography.meta-mono}'
    accepted-chip: '{colors.layer-01}'
    accepted-icon: checkmark
  wifi-network-row:
    background: '{colors.background}'
    border: '1px solid {colors.border-subtle}'
    selected-border: '2px solid {colors.primary-text}'
    unsupported: 'hatch 135°, 1.5px {colors.status-hatch-line} lines on {colors.status-hatch-ground}, 1px dashed {colors.status-unknown-border}'
  calibration-reference-point:
    background: '{colors.layer-01}'
    raw-value: '{typography.hero-value}'
    reading-time: '{typography.meta-mono}'
    fresh: '{colors.text-primary}'
    fresh-icon: checkmark
    waiting-icon: time
    reading-row-border: '1px solid {colors.border-subtle}'
  threshold-column:
    track: '{colors.layer-01}'
    soil: '{colors.status-water-level}'
    low-line: '2px solid {colors.primary-text}'
    empty-high: '1px dashed {colors.border-strong}'
  pause-sheet:
    background: '{colors.background}'
    scrim: '{colors.overlay}'
    note: '{typography.body}'
  notification-window-control:
    inside: '{colors.primary}'
    outside: 'hatch 135°, 1.5px {colors.status-hatch-line} lines on {colors.status-hatch-ground}'
    axis: '{typography.meta-mono}'
  time-zone-confirm-panel:
    border: '1px dashed {colors.support-warning}'
    background: transparent
  member-tile:
    background: '{colors.layer-01}'
    initials-you-background: '{colors.primary}'
    initials-you-foreground: '{colors.ink-on-bright}'
  first-run-step-tile:
    next-background: '{colors.primary}'
    next-foreground: '{colors.ink-on-bright}'
    later-border: '1px dashed {colors.border-strong}'
  outcome-screen-success:
    background: '{colors.support-success}'
    foreground: '{colors.ink-on-bright}'
  outcome-screen-error:
    background: '{colors.background}'
    eyebrow: '{colors.support-error-text}'
    icon: error--filled
  inline-notice:
    background: '{colors.layer-01}'
    border-left: '3px solid {colors.border-strong}'
    body: '{typography.body}'
  app-header-web:
    background: '{colors.header-bg}'
    border: '1px solid {colors.header-border}'
    height: '{spacing.header-height}'
  sign-in-surface:
    background: 'DS signature radial: {colors.primary} at 5% 5% → {colors.secondary}'
    card: '{colors.background}'
---

## Brand & Style

Coldframe answers one question, *water now or not*, and is equally blunt about its own blind spots. Direction B, "Plots", is the base: the garden as a grid of square tiles, one per Lot. A Lot that needs water is the only thing painted solid orange; everything else stays quiet. Measured tiles fill from the bottom like soil, so "how dry" reads before any number does. Anything the system cannot vouch for — a silent Node, an uncalibrated probe, data from an unreachable Server — is drawn as visibly *not normal*: hatched, dashed, dotted, or stripped to an outline, each with its own icon. Silence never looks fine.

The look is the Escendit Design System laid over native platform behaviour: Carbon foundations, Ubuntu and Ubuntu Condensed, square corners, 1 px borders, flat solid surfaces, Carbon icons. Precise, calm, a little industrial — a maker's instrument, not a lifestyle app. No gamification, no celebration, no decoration that isn't carrying state.

Visual references (spines win on conflict with every mock): chosen direction [imports/claude-design-B-plots.html](imports/claude-design-B-plots.html) and its captures [B-plots-1](imports/claude-design-B-plots-1.png) (status legend, overview dark/light, Lot detail, Alerts) through [B-plots-7](imports/claude-design-B-plots-7.png) (Android, web overview). Rejected directions, reference only: [A Ledger](imports/claude-design-A-ledger.html) ([1](imports/claude-design-A-ledger-1.png), [2](imports/claude-design-A-ledger-2.png), [3](imports/claude-design-A-ledger-3.png), [4](imports/claude-design-A-ledger-4.png)) and [C Brief](imports/claude-design-C-brief.html) ([1](imports/claude-design-C-brief-1.png), [2](imports/claude-design-C-brief-2.png), [3](imports/claude-design-C-brief-3.png), [4](imports/claude-design-C-brief-4.png), [5](imports/claude-design-C-brief-5.png), [6](imports/claude-design-C-brief-6.png), [7](imports/claude-design-C-brief-7.png)). Design-system source: [imports/escendit-ds/README.md](imports/escendit-ds/README.md), [colors_and_type.css](imports/escendit-ds/colors_and_type.css).

## Colors

All neutrals and brand colours are Escendit DS tokens, vendored from `escendit/branding` `css/theme.css` into `packages/design-tokens` and generated for Swift and Kotlin. Direction B's hard-coded off-DS values are superseded: `#0A0A0A` page → `{colors.background-dark}` (`#262626`); `#1A1A1A` hatch → `{colors.status-hatch-ground-dark}`; `#161616` paused fill → `{colors.status-paused-fill-dark}`; `#1A2A12` chart band → `{colors.chart-band-dark}`. Everything in the Lot-status set shifts one Carbon layer up so tiles still separate from the lighter DS dark background.

- **Orange (`{colors.primary}` `#FF770F`)** has one job on data surfaces: *needs water*. It fills the needs-water tile, the needs-water Alert row (open low-side soil-moisture Threshold Alert only), the Lot detail hero of a Lot needing water, and daily-low bars below the low Threshold. Other Threshold Alerts (too wet, temperature, humidity, air) are never orange. Elsewhere orange is only the interactive fill (primary button, selected segment, first-run next step, Notification Window control bar). Never for Health Alerts, the focus ring or decoration.
- **Orange as text or line (`{colors.primary-text}` `#B84A00` light / `#FF8A2E` dark)** for ghost buttons, links, the step counter, selected nav label and icon, selection borders, the low-Threshold line and light-theme below-low chart bars.
- **Ink on bright (`{colors.ink-on-bright}` `#0A0A0A`)** is the text colour on every orange, green and selected-orange surface, including the primary button label (7.45:1 on `primary`, 5.87:1 on hover, 4.55:1 on active). The DS white `text-on-color` is never used on orange; this deviates from the Escendit DS Button pending an upstream fix in `escendit/branding`, and is overridden locally in `packages/design-tokens` until then.
- **Focus (`{colors.focus}` `#161616` light / `#F4F4F4` dark)** is a two-tone indicator: ring plus a `{colors.focus-gap}` gap between ring and component (Components › Focus indicator), so it separates from any fill, orange included.
- **Neutrals** (`background`, `layer-01/02`, `field-01`, `text-*`, `border-*`) follow DS light (white/`#F4F4F4`) and dark (Carbon g90 `#262626`/`#393939`/`#525252`) exactly.
- **Purple (`{colors.status-paused-border}`)** means *paused*, and nothing else on data surfaces. Brand secondary `#670FFF` in light, `#8945FF` in dark for contrast.
- **Yellow** has two uses, both "don't trust this yet": the needs-calibration dashed border (`{colors.status-calibration-border}`) and the stale-data age and timestamps (`{colors.stale-ink}`). Light theme uses DS yellow-700/800 because yellow-500 is illegible on white.
- **Green** only confirms a finished setup step: done setup-progress segments (`{colors.setup-done}`, always with a checkmark) and the "Hub is online" outcome (`{colors.support-success}` with `ink-on-bright`). It never marks a Lot as OK — OK is deliberately neutral.
- **Red** only for setup/validation failures: `{colors.support-error}` for the 2 px invalid border, `{colors.support-error-text}` for error text and eyebrows, always with the `error--filled` icon. Never on Lot tiles or Alerts: a dry Lot is not an error.
- **Chart**: outlined neutral bars (`{colors.chart-bar}`), solid orange bars where the daily low fell below the low Threshold (`{colors.chart-bar-below-low}`), a decorative green band between low and high (`{colors.chart-band}`).

Load-bearing contrast (WCAG 2.2, computed from the tokens; `packages/design-tokens` CI regenerates and enforces these, and fails any tile text token below 4.5:1 against every surface it can overlap):

| Pair | Light | Dark |
|---|---|---|
| Needs-water ink on orange / on soil level | 7.45 / 4.92 | 7.45 / 4.92 |
| Primary button label (ink) on primary / hover / active | 7.45 / 5.87 / 4.55 | 7.45 / 8.42 / 4.55 |
| Tile foot line `text-secondary` on OK soil level / hatch plate | 5.92 / 7.10 | 5.44 / 6.76 |
| Calibration label on hatch plate | 5.98 | 7.59 |
| Paused ink on paused fill | 10.67 | 7.40 |
| `primary-text` on background / layer-01 | 5.18 / 4.75 | 6.43 / 4.91 |
| `support-error-text` on background | 4.96 | 6.38 |
| Focus ring on its gap (the ring always sits on the gap) | 17.94 | 13.76 |
| Hatch line on hatch ground (non-text) | 3.02 | 3.48 |
| OK border / stale border on background (non-text) | 3.29 | 4.56 |
| Setup done on layer-01 (non-text) | 4.71 | 5.61 |
| Chart bar on background; below-low bar on band (non-text) | 3.29; 5.03 | 4.56; 4.49 |

The OK soil level and the needs-water level are not contrast-bearing; each carries a 2 px level edge (`{colors.status-level-edge}` / `{colors.status-water-ink}`) that is.

## Typography

Ubuntu for body and names, Ubuntu Condensed for headlines, values, status labels and button labels, Ubuntu Mono for timestamps, Device IDs and raw values. Only weights 300 and 400 are used. No serif (the DS's Quattrocento has no role here). Fonts are bundled in the apps and self-hosted for the web `[ASSUMPTION]` (no Google Fonts fetch from a local-first product).

- `{typography.headline}` is the one-sentence Site state at the top of the overview ("2 Lots need water"), Alerts count, stale age, and setup step titles.
- `{typography.tile-name}` is the Lot name on tiles.
- `{typography.tile-value}` / `{typography.tile-value-web}` carry the tile's big value: `~20`, `6 h`, `raw`, `—`, `+`. `{typography.hero-value}` does the same on Lot detail and for the raw value of the Calibration reference Reading.
- `{typography.status-label}` is the uppercase status word under each Lot name (NEEDS WATER, SILENT · UNKNOWN), always preceded by its status icon. Uppercase is applied by style, never baked into strings.
- `{typography.meta-mono}` carries the tile foot line ("07:02 · low 30 %", "as of 07:02") and chart axes.

Every size is a base size that scales: iOS via Dynamic Type relative to the noted text style, Android via font scale in `sp`, web in `rem`. Values ≥ 36 px cap at 2× `[ASSUMPTION]`.

## Layout & Spacing

DS/Carbon 2 px-based scale (`{spacing.1}` … `{spacing.13}`). Mobile gutters `{spacing.gutter-mobile}`; web content padding `{spacing.gutter-web}`.

- **Lot grid**: tiles are at least square (web: at least 1 : 0.82) and grow in height to fit their content; `{spacing.tile-gap}` gaps. Phone: 2 columns. Web: 4 columns at ≥ 1056 px, plus a `{spacing.alerts-rail-web}` Open Alerts rail at ≥ 1312 px (Carbon lg/xlg breakpoints `[ASSUMPTION]`). **One column** when iOS text size ≥ Accessibility 1, Android font scale ≥ 1.5, or the web grid container is narrower than `{spacing.one-column-web}` (covers 320 px reflow and 200 % zoom). Tiles never shrink their content, compact, or switch to a list; with many Lots the overview scrolls.
- **Tile anatomy** (`{spacing.tile-padding}` inset, `{spacing.tile-padding-web}` on web): top-left Lot name (`{typography.tile-name}`) over status icon + status label; bottom-left big value over mono foot line; soil-level fill with a 2 px level edge rises from the bottom edge to the Reading's %; a 12 px tick on the right edge marks the low Threshold height. On hatched tiles the name, label, value and foot sit on a solid `{colors.status-hatch-ground}` plate. In the one-column layout the value sits directly under the status label and the tile grows downward.
- **Lot detail**: full-width hero, a 3-up row of sensor cells, the 30-day chart, a 2-up row of device cells, then admin actions. Rows wrap to 1-up in the one-column layout.
- **Setup flows**: big `step-counter` ("01 / 05") top-left, one question per screen, primary action directly below the content (not pinned to the bottom), as in Direction B.

## Elevation & Depth

None. Hierarchy is carried by DS layers (`background` → `layer-01` → `layer-02`) and 1 px borders. No shadows on tiles, cards, rails or sheets; modal scrims use `{colors.overlay}`. No blur, no transparency, no gradients — except the DS signature radial on the sign-in screen only.

## Shapes

Everything the app draws is square: `{rounded.none}` for tiles, cards, fields, buttons, sheets, dialogs and the web shell. Direction B's 12 px Android tile radius is superseded by the DS 0-radius rule. `[ASSUMPTION]` OS-drawn chrome (permission prompts, system share sheets, notification shade, native switches) keeps its platform shape.

Status is carried by label text, a unique edge/fill shape and a unique Carbon icon, then colour. With colour and text removed, every state stays distinct:

| State | Shape | Carbon icon |
|---|---|---|
| needsWater | solid fill, no border, level edge | `rain-drop` |
| ok | soil fill with level edge, 1 px solid border | `checkmark--outline` |
| unknown | hatch lines, 1 px dashed border | `help` |
| needsCalibration | hatch lines, 2 px dashed border | `tools` |
| paused | flat fill, 2 px solid border | `pause--outline` |
| noNode | empty, 1 px dotted border, big `+` | `add` |
| stale (any status) | empty, 1 px solid border, no level | `cloud--offline` |

## Components

Icons: Carbon icons only (`@carbon/icons`, fill `currentColor`), subset vendored with the tokens. No emoji, no unicode glyph icons. Every focusable element uses the Focus indicator.

- **Focus indicator** — `{spacing.focus-ring}` `{colors.focus}` ring with a `{spacing.focus-offset}` `{colors.focus-gap}` gap, drawn outside the component; inset (ring outside, gap inside, over the fill) where neighbours abut (segments, list rows). Never hidden under the sticky AppHeader or tab bar (web `scroll-padding-top` = `{spacing.header-height}`).
- **Lot tile** — 6 statuses + stale; anatomy in Layout, shape and icon per Shapes. Big value per status: needs water / OK → `~20` (moisture rounded to 5 %, `~` prefix); unknown → silence duration (`6 h`) with foot "was ~40 % at 01:05", label "SILENT · UNKNOWN" (`unknownCause` node) or "HUB SILENT · UNKNOWN" (`unknownCause` hub); needs calibration → `raw` with foot "no % until calibrated"; paused → `—` with foot "until 1 Nov" (or "paused" without end date), label "PAUSED" or "PAUSED BY SITE" when `pausedBy` includes site; no Node → `+` with foot "add a Node". No secondary conditions are appended to labels; battery and other Health conditions appear on Lot detail and Devices. Stale → all fills, hatches and colour removed, `cloud--offline` icon; label reads "WAS NEEDS WATER"; foot reads "as of 07:02" in `{colors.stale-ink}`. Skeleton (cold start without cache): `lot-tile-skeleton` outline, no icon, label or value. See [B-plots-1](imports/claude-design-B-plots-1.png), [B-plots-2](imports/claude-design-B-plots-2.png).
- **Site summary header** — Site name (tappable on mobile, with Site switcher chevron) + clock (`meta-mono`) and Site menu trigger, above the `headline` sentence and a counts subline ("2 unknown · 2 OK · 1 paused · 1 without Node"). Paused Site: headline in `{colors.status-paused-ink}` ("Paused until 1 Mar").
- **Site menu** — `overflow-menu--vertical` trigger at the right of the Site summary header (mobile) or on the current Site tab (web); native menu (iOS) / Material dropdown menu (Android) / DS overflow menu (web); items in `{typography.body-lg}`: Pause Home garden / Resume Home garden (Admin+), Site settings.
- **Site switcher** — mobile: native sheet listing Sites (name + Role in `status-label`), current Site marked with `checkmark` in `{colors.primary-text}`, "New Site" last. Web: Site tabs in the AppHeader, current tab underlined in `{colors.primary}` on `{colors.header-bg}`, "New Site" as the last tab.
- **Stale header** — replaces the summary header: `cloud--offline` icon, "Home garden · can't reach your Server", age in `headline` + `{colors.stale-ink}` ("2 h 12 min old"), one explanatory line.
- **Alert row** — needs water (open low-side soil-moisture Threshold Alert): solid orange, `rain-drop`, eyebrow "NEEDS WATER" + time. Other Threshold Alert (too wet, temperature, humidity, air): `layer-01` with 2 px solid `{colors.border-strong}`, `arrow--up` / `arrow--down`, eyebrow "THRESHOLD ALERT · ABOVE HIGH" / "· BELOW LOW". Health Alert: hatch + dashed border on a solid text plate, icon by cause (`help`, `battery--low`, `tools`), eyebrow "HEALTH ALERT". Closed: outline only, `text-secondary` text, eyebrow "… · CLOSED 06:40". See [B-plots-2](imports/claude-design-B-plots-2.png).
- **Lot detail hero** — Lot name (`title`), status icon + status label with "since 05:45", `hero-value` with unit, low Threshold and "±5 % · 07:02" right-aligned. Orange only for needs water; other statuses use the matching tile treatment. See [B-plots-1](imports/claude-design-B-plots-1.png) (B03).
- **Sensor cell** — `layer-01` block, helper label over `title` value (Temperature `14 °C`, Humidity `78 %`, Air (gas) `142 kΩ`).
- **Device cell** — `layer-01` block, helper label over `title` value (Node `62 %` "charging · solar", Last seen `07:02` "every 15 min"); battery below 20 % shows `battery--low`.
- **Devices list** — full-width rows on `background` with a 1 px `border-subtle` divider; section headers "Hubs" / "Nodes". Row: Device ID (`meta-mono`) and Lot name (`body-lg`) left; last seen, battery % and charging (Nodes) in `helper` right; silent rows add `help` + "SILENT 6 H"; paused rows add `pause--outline` + "PAUSED" / "PAUSED BY SITE" in `{colors.status-paused-ink}`. Header actions as ghost buttons (Add a Hub, Add a Node — mobile only).
- **History chart** — 30 bars of daily low for soil moisture: normal days as 1 px outlined bars, below-low days as solid `{colors.chart-bar-below-low}` bars; low line 2 px solid `{colors.primary-text}`, high line dashed neutral, decorative band between; axis dates in `meta-mono`; legend "solid bar = below 30 %".
- **Button** — DS Button: primary (orange fill, `ink-on-bright` label), secondary, ghost (`{colors.primary-text}` label). Square, `{spacing.button-height}`, uppercase condensed label. Admin action strip on Lot detail uses secondary buttons.
- **Text input** — DS TextInput: `field-01` fill, bottom border, password reveal (Carbon `view` icon), helper and invalid states; invalid = 2 px `{colors.support-error}` border plus `error--filled` icon and reason in `{colors.support-error-text}`.
- **Segmented choice** — equal-width square segments; selected = orange fill, `ink-on-bright` label and a leading `checkmark`. Used by Reminder cadence, Theme switcher, Invite form Role, Pause scope. Wraps to two rows at large text.
- **Device candidate tile** — `selectable-tile` tokens: square tile with Device ID (`meta-mono`) and signal / battery; selected = 2 px `{colors.primary-text}` border + `checkmark`; "PRESSED JUST NOW" as a text badge. See [B-plots-3](imports/claude-design-B-plots-3.png) (ignore its "Already set up" tile, AD-25).
- **Lot picker** — `selectable-tile` tokens: one tile per Lot; selected state as on the Device candidate tile; Lots with a Node show dimmed name and the reason "HAS A NODE"; "+ New Lot" as a dotted tile.
- **Setup flow shell** — "03 / 05" in `step-counter`, current number in `{colors.primary-text}`, total in `{colors.text-helper}`; Cancel/Back top-left; step title in `headline`.
- **Setup progress** — four labelled segments (BLUETOOTH · WI-FI SENT · JOINING… · SERVER): done = `{colors.setup-done}` fill + `checkmark`; active = orange fill + `in-progress` icon (static under Reduce Motion); pending = 1 px `border-strong` outline, no fill. See [B-plots-3](imports/claude-design-B-plots-3.png).
- **Setup code field** — Text input in `meta-mono`, uppercase; on success an "ACCEPTED" chip (`layer-01`, `checkmark`) at the field's right.
- **Wi-Fi network row** — SSID left, security right (`meta-mono`); selected = 2 px `{colors.primary-text}` border + `checkmark`; WPA3-only rows hatched with the reason inline.
- **Calibration reference point** — stored-Reading panel (AD-9), no live value. Waiting state: `layer-01` panel with `time` icon, "Waiting for the next Reading", the last raw value in `hero-value` with its Reading time in `meta-mono`, and the hint "Short-press the Node's setup button to report now". Fresh state: `checkmark` + "New Reading 07:17" in `{colors.text-primary}`, the raw value, and the Record dry / Record wet primary button. Below: "Recent Readings" list, one row per stored Reading (time + raw value), selectable with `checkmark`. Confirmation: dry and wet raw values side by side and the line "% appears with the next Reading", updating in place to the first % once it arrives. (B-plots-4/5 show the superseded live-BLE calibration; do not build from them.)
- **Threshold column** — vertical soil column: low line 2 px `{colors.primary-text}`, current Reading marker, dashed "no high" marker; values listed to the right. See [B-plots-5](imports/claude-design-B-plots-5.png) (B18).
- **Pause sheet** — native sheet (iOS) / ModalBottomSheet (Android) / DS Modal (web) on `{colors.overlay}`: Pause scope segmented choice, "Until" date field, helper line, primary button naming the result ("Pause Home garden").
- **Notification Window control** — two time fields, then a decorative 24 h bar (orange inside the window, hatched outside) and big `07:00 to 22:00`.
- **Time-zone confirm panel** — dashed yellow box for "Is your time zone Europe/Zurich?" with Confirm / Change.
- **Mute toggle** — native switch (iOS Toggle / Material Switch / DS Toggle) with label "Mute Home garden".
- **Reminder cadence** — Segmented choice with helper line under it.
- **Member tile** — initials square (you: orange fill with `ink-on-bright` initials; others: `layer-01` with `text-primary`), name, "OWNER · LAST ONE" / "MEMBER · CHANGE". See [B-plots-6](imports/claude-design-B-plots-6.png) (ignore its invited tile).
- **Invite form** — Text input (email), Role Segmented choice with the one-line Role description under it, primary "Send invite"; after sending, an Inline notice "Invitation sent to …".
- **Theme switcher** — Segmented choice System / Light / Dark (DS ThemeSwitcher on web).
- **First-run step tiles** — 2×2 tiles STEP 1–4 (Add a Hub · Add a Node · Calibrate · Set a low Threshold); the next step solid orange with `ink-on-bright`, later steps dashed, done steps with `checkmark`.
- **Outcome screens** — success: full-bleed `{colors.support-success}` with `ink-on-bright` ("Hub is online"); error: neutral background, `error--filled` + eyebrow in `{colors.support-error-text}` ("STEP 5 STOPPED"), plain headline ("Wrong Wi-Fi password").
- **Inline notice** — DS InlineNotification style (info/warning), used for read-only explanations, Site-level Hub silence, notification permission off, and paused-Site Device notes.
- **Navigation** — mobile: native tab bar (iOS) / Material 3 NavigationBar (Android) with Carbon icons `grid`, `notification`, `box`, `settings`; selected tab = `{colors.primary-text}` label and icon plus a non-colour cue (filled icon on iOS, M3 indicator pill on Android). Web: DS AppShell + AppHeader (`{colors.header-bg}`, Site tabs in header, side nav Garden · Alerts · Devices · Members, Settings in the nav footer), current item with a 3 px left bar. See [B-plots-7](imports/claude-design-B-plots-7.png).
- **Sign-in surface** — DS signature radial background, white card with Server field and SIGN IN button; errors as an Inline notice inside the card. No text sits directly on the gradient. See [B-plots-6](imports/claude-design-B-plots-6.png) (B22).
- **Push notification** — OS-rendered; only content is specified (EXPERIENCE.md → Notifications). App icon uses the Coldframe mark.

## Do's and Don'ts

| Do | Don't |
|---|---|
| Paint solid orange only for *needs water*, including Alert rows | Use orange for too-wet, temperature, humidity or air Alerts, Health Alerts, OK, or decoration |
| Put `ink-on-bright` on every orange surface, primary buttons included — a deliberate deviation from the Escendit DS pending an upstream fix in `escendit/branding` | White `text-on-color` on orange (2.64:1) |
| Use `primary-text` for orange text, lines and selection marks | DS `#FF770F` as text or thin lines on light surfaces |
| Two-tone focus indicator on every focusable element | An orange focus ring, or focus shown by colour change alone |
| Carry every status by label text + unique shape + Carbon icon, then colour | Rely on colour alone, reuse one shape for two states, or use a green "OK" |
| Strip all fills when data is stale and print its timestamp | Leave a stale tile looking live |
| Prefix approximate moisture with `~` and round to 5 % | Show decimals or unrounded % for soil moisture |
| Use DS tokens for every colour, both themes | Hard-code B's `#0A0A0A` / `#1A1A1A` / `#1A2A12` |
| Square corners, 1 px borders, flat layers | Rounded tiles (incl. Android), shadows, blur, gradients outside sign-in |
| Carbon icons, `currentColor` | Emoji, unicode glyph icons, hand-drawn SVGs |
| Keep tiles full size, let them grow in height, drop to one column at large text | Fixed-height tiles that clip, shrinking tiles, or a list mode for many Lots |
| Red only for setup/validation failures | Red for a dry Lot or a silent Node |
