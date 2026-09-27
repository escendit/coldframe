# Accessibility review: Coldframe UX spines (DESIGN.md + EXPERIENCE.md)

- Reviewed: `DESIGN.md`, `EXPERIENCE.md`, `.memlog.md` (2026-09-27 drafts). Spines were not modified.
- Checked against: the agreed floor in `.memlog.md` ("minimum": Dynamic Type / font scale / browser zoom, full VoiceOver/TalkBack/screen-reader labels, status never by colour alone (text + pattern), DS contrast), plus WCAG 2.2 AA where it matters for this product (1.4.3, 1.4.11, 1.4.1, 1.4.4/1.4.10, 2.4.7/2.4.11, 2.2.1, 4.1.3, 2.5.7/2.5.8).
- Method: WCAG relative-luminance contrast computed from the frontmatter hex values for every load-bearing pair, in both themes. Colour-vision deficiency simulated with Machado et al. 2009 matrices at full severity, with CIE76 ΔE between the simulated colours.

## Verdict

**Does not yet meet the agreed floor. Fixable without structural redesign.**

The behaviour spine is strong. Screen-reader labels are written per status, "silence never looks fine" is a good principle for accessibility, stale data gets a text marker, reduced motion is covered, errors have no auto-dismiss, and there are no hover-only affordances. The problems are concentrated in three areas:

1. **Orange in the light theme.** The DS orange `#FF770F` is only 2.64:1 on white. It is used as the focus ring, the primary-button background under white text, link and ghost text, the selected-state indicator and chart marks. The focus ring is also **1.00:1 on every orange surface**, including the needs-water tile, which is the most important thing on screen.
2. **The "pattern" part of "text + pattern" cannot be seen.** The hatch stripes (1.20:1 light, 1.31:1 dark), the OK tile fill and border (1.09 / 1.31, and 1.00 in dark because the border equals the fill) and the soil level (1.20 / 1.48) are all far below 3:1. Without colour and text, the 7 tile states collapse into 4 visual groups. Text still tells them apart, so the letter of the floor holds, but the claim in DESIGN.md that "tile borders and hatch vs background ≥ 3:1" is false for most pairs.
3. **Dynamic behaviour is under-specified for assistive tech.** Fixed-aspect tiles conflict with Dynamic Type and 200 % zoom. There is no announcement policy for setup progress, calibration waits, stale-mode entry or live scan lists.

## Contrast table (computed)

Requirement: 4.5:1 for normal text, 3:1 for large text (≥ 24 px, or ≥ 18.66 px bold) and for non-text UI and graphics. **Bold** = failure that matters. "exempt" = WCAG exempts it, listed for completeness.

### Text

| Pair | Light | Dark | Result |
|---|---|---|---|
| text-primary on background / layer-01 | 17.94 / 16.45 | 13.76 / 10.50 | pass |
| text-primary on hatch-a / ok-level | 13.71 | 10.50 (on #393939) | pass |
| text-secondary on background / layer-01 | 7.75 / 7.10 | 8.86 / 6.76 | pass |
| text-secondary on ok-level (soil fill) | 5.92 | 4.57 | pass (dark marginal) |
| text-helper on background / layer-01 | 4.91 / 4.50 | 6.74 / 5.15 | pass (light layer-01 exactly at limit) |
| **text-helper on ok-level / hatch-a** (tile foot line over soil fill or hatch) | **3.75** | **3.48** (on #525252) | **FAIL** |
| **text-on-color #FEFEFE on primary #FF770F** (DS primary button) | **2.64** | **2.64** | **FAIL** |
| **…on primary-hover** | **3.34** (#E06A00) | **2.33** (#FF8A2E) | **FAIL** |
| **…on primary-active #C25C00** | **4.32** | **4.32** | **FAIL** |
| ink-on-bright #0A0A0A on primary / hover / active | 7.45 / 5.87 / 4.55 | 7.45 / ~8.3 / 4.55 | pass (DESIGN.md claims 8.2:1; the actual value is 7.45) |
| ink-on-bright on status-water-level #CF5D00 (text over the soil fill in the orange tile) | 4.92 | 4.92 | pass |
| ink-on-bright on support-success | 9.62 | 9.62 | pass |
| white on button-secondary | 11.45 | 4.98 | pass |
| **Orange text: ghost button, links** | **2.64** bg / **2.42** layer-01 | 5.69 bg / **4.34** layer-01 | **FAIL** light; FAIL dark on layer-01 |
| **Step counter "03" orange 56 px (large text)** | **2.64** | 5.69 | **FAIL** light |
| **support-success text** ("steady for 5 s…", calibration) | **1.87** | 5.61 | **FAIL** light |
| **support-error eyebrow** ("STEP 5 STOPPED") | **3.36** | **4.47** | **FAIL** both |
| paused ink on paused fill / background | 10.67 / 12.11 | 7.40 | pass |
| calibration ink on hatch-a / hatch-b | 4.99 / 5.98 | 7.59 / 9.95 | pass |
| stale-ink on background | 6.53 | 9.95 | pass |
| no-node ink on background | 7.75 | 6.74 | pass |
| header text on header-bg; orange selected on header-bg | 16.45; 6.81 | same | pass |
| text-disabled on background | 1.69 | 3.01 | exempt. See M4: informational reasons use this colour |

### Non-text (3:1)

| Pair | Light | Dark | Result |
|---|---|---|---|
| **Focus ring on background / layer-01** | **2.64 / 2.42** | 5.69 / 4.34 | **FAIL** light |
| **Focus ring on orange** (primary button, needs-water tile, Threshold Alert row, selected segment, first-run "next" tile) | **1.00** | **1.00** | **FAIL** (invisible) |
| Needs-water tile on background | 2.64 | 5.69 | light below 3:1, but a large solid area that is also labelled. Low |
| **status-water-level on water-fill** (soil level inside the orange tile) | **1.51** | **1.51** | **FAIL** |
| **OK tile fill on background** | **1.09** | **1.31** | **FAIL** |
| **OK tile border on background** | **1.31** | **1.31** (border #393939 = fill #393939, **1.00**) | **FAIL** |
| **OK soil level on OK fill** | **1.20** | **1.48** | **FAIL** |
| Low-marker tick on OK fill / on soil level | 3.02 / **2.51** | 5.15 / 3.48 | light FAIL on soil level |
| **Hatch stripe a vs b** (unknown, needsCalibration, Health Alert row, WPA3 row, window bar) | **1.20** | **1.31** (hatch-b = page bg) | **FAIL**, pattern effectively invisible |
| Unknown dashed border on background | 3.29 | 4.56 | pass |
| Calibration dashed border on background / **on hatch-a** | 3.64 / **2.78** | 9.95 | light FAIL against the hatch it borders |
| Paused border on background / fill | 6.74 / 5.93 | 3.14 | pass (dark marginal) |
| No-node dashed border on background | 3.29 | 3.01 | pass (dark is at the limit and will fail after any rounding) |
| Stale border on background | 3.29 | 4.56 | pass |
| **Chart bar on background / band** | **1.69 / 1.64** | 3.01 / **2.38** | **FAIL** |
| **Chart below-low bar vs neutral bar** | **1.56** | **1.89** | **FAIL**. Colour is the only thing that sets these bars apart |
| **Chart below-low bar / low line on band** | **2.56** | 4.49 | light FAIL |
| **Chart band on background** | **1.03** | **1.27** | **FAIL** (band invisible) |
| Chart high line on band / background | 3.19 | 4.56 | pass |
| Input bottom border on field-01 | 3.02 | **2.30** | dark FAIL |
| Invalid border red on background | 3.36 | ~4.47 | pass |
| **Confirm panel yellow dash** | **1.51** | 9.95 | light FAIL |
| **Selected border orange on layer-01**; selected segment fill vs layer-01 | **2.42** | 4.34 | light FAIL |
| **Setup progress: done green on background; pending layer-01 on background; done vs active** | **2.04; 1.09; 1.29** | — ; 1.31; 1.29 | **FAIL** |
| Notification Window bar orange vs hatch | 2.01 | — | FAIL, but backed by text. Low |

### Colour-vision simulation (ΔE, CIE76; below ~20 reads as "same colour")

| Pair | Normal | Protan | Deutan | Tritan |
|---|---|---|---|---|
| orange vs green (setup progress active vs done) | 113 | **24** | **7** | 112 |
| orange vs yellow #FFCB0F | 49 | 30 | **19** | 47 |
| green vs yellow | 72 | **7** | 21 | 70 |
| calibration yellow #9C8400 vs unknown grey #8D8D8D | 61 | 59 | 58 | **22** |
| purple vs grey | 127 | 93 | 100 | 37 |
| orange vs red | 39 | 40 | 27 | **20** |

Consequences: under protan or deutan, the orange needs-water tile and a yellow needs-calibration border are both yellowish. They still differ by shape (solid fill vs hatch + dash), so this is acceptable. Setup progress done (green) vs active (orange) cannot be told apart under deutan (ΔE 7). Purple "paused" survives all three types. Under tritan, calibration yellow vs unknown grey weakens to 22, and because those two statuses share the same hatch and dash, only text separates them.

### Status differentiation with colour removed

| Status | Visual encoding without colour | Perceivable at WCAG levels? |
|---|---|---|
| needsWater | solid fill | yes (large solid area) |
| ok | fill + 1 px border + soil level | **no**: fill 1.09, border 1.31/1.00, level 1.20. Reads as borderless text |
| unknown | hatch + 1 px dashed | only the dashed border (hatch 1.20) |
| needsCalibration | hatch + 1 px dashed | **identical shape to unknown**. Only border hue differs |
| paused | 2 px solid outline | yes (weight + purple) |
| noNode | 1 px dashed, empty, "+" | dashed border, **same shape as unknown** once the hatch is invisible |
| stale | 1 px solid, empty | **same shape as OK** (1 px solid) except for border lightness |

With colour and text removed there are 4 groups: {needsWater}, {paused}, {unknown, needsCalibration, noNode}, {ok, stale}. Text resolves all of them, because the status label, value and foot line are always present. So "never by colour alone" holds, but "text + pattern" does not.

---

## Critical

### C1. The focus ring cannot be seen on orange surfaces and fails in the light theme
- **Where:** DESIGN.md `focus: '#FF770F'`, `text-input.focus`; EXPERIENCE.md Accessibility Floor ("visible DS focus ring `{colors.focus}` on web"), Interaction Primitives (web keyboard, Enter/Space on tiles).
- **Problem:** The orange ring on orange is 1.00:1. That covers the needs-water tile, primary buttons, the Threshold Alert row, the selected segment and the first-run "next" tile. On white or layer-01 it is 2.64 / 2.42. A keyboard user cannot see focus on the tile the product exists to show, or on every primary CTA in the setup and settings flows. This fails WCAG 2.4.7 and fails 1.4.11 for the indicator.
- **Fix:** Use a two-tone focus indicator on every platform with a keyboard or switch focus (web; also iPadOS and Android with a hardware keyboard):
  - Add `focus-inner: '#161616'` and `focus-inner-dark: '#F4F4F4'`.
  - Draw a 2 px `focus-inner` outline plus a 2 px `background` gap (`outline-offset: 2px`) so it separates from any fill. #161616 on orange is 6.81:1 and on white is 17.9:1.
  - Keep orange only as an optional outer accent. Never use it as the sole indicator.
  - Also require that focus is never hidden under the sticky AppHeader or mobile tab bar (WCAG 2.4.11): use `scroll-padding-top: header-height`.

### C2. The primary button label is white on orange (2.64:1; hover 3.34 / 2.33; active 4.32)
- **Where:** DESIGN.md `button-primary.foreground: '{colors.text-on-color}'`; Colors bullet "`[ASSUMPTION]` DS `text-on-color` … kept only inside the DS Button component, whose contrast is DS-owned".
- **Problem:** Every primary action fails 1.4.3: SIGN IN, SEND INVITE, "Put 7C19 in Tomatoes", Record dry, Save, PAUSE HOME GARDEN. The label is `button` 14 px condensed uppercase, so the large-text exemption does not apply. Calling it "DS-owned" does not make it pass, and the memlog floor includes "DS contrast". The spine is also inconsistent: segmented choice and first-run tiles already use ink on orange.
- **Fix:** Set `button-primary.foreground: '{colors.ink-on-bright}'`. The results are 7.45 on `primary`, 5.87 on `primary-hover` #E06A00, 4.55 on `primary-active` #C25C00, and about 8.3 on `primary-hover-dark` #FF8A2E. Raise it upstream in `escendit/branding` too (Button `color: var(--text-on-color)` → an ink token for primary). Until then, override locally in `packages/design-tokens`. Do not keep white text on orange anywhere, including member initials and the sign-in gradient. See L7.

---

## High

### H1. Light-theme orange used as text or as a state indicator fails
- **Where:** `button-ghost.foreground: primary`, links, `step-counter.current-color: primary`, navigation "selected in `{colors.primary}`", `segmented-choice.selected-background`, `selectable-tile.selected-border`, `wifi-network-row.selected-border`.
- **Problem:** Orange text is 2.64 on white and 2.42 on layer-01, and 4.34 on dark layer-01. The step counter fails even as large text. The selected tab (iOS tint only), the selected segment (orange fill vs layer-01 is 2.42) and the selected tile or row (2 px orange at 2.42) show selection by a low-contrast colour change alone (1.4.1 and 1.4.11).
- **Fix:**
  - Add `primary-text: '#B84A00'` for light text and marks (5.18 on white, 4.75 on layer-01, 5.03 on chart band), and `primary-text-dark: '#FF770F'` on background only (5.69). On dark layer-01 use `#FF8A2E` or text-primary.
  - Use `primary-text` for ghost buttons, links, the step counter, the selected nav label and icon, and the Threshold column low line.
  - Give selection a non-colour cue on top of the colour: a Carbon `checkmark` icon in the selected segment or tile; M3 NavigationBar's indicator pill on Android; a filled vs outline icon on iOS tabs.
  - Expose selection to screen readers: `.isSelected` / `selected` semantics / `aria-pressed` / `aria-current`.

### H2. The "pattern" half of "text + pattern" cannot be perceived; states collapse into 4 shapes
- **Where:** `status-hatch-a/b` (1.20 light, 1.31 dark; `status-hatch-b-dark` = `background-dark`), `status-ok-*` (fill 1.09, border 1.31; `status-ok-border-dark` = `status-ok-fill-dark` → 1.00), DESIGN.md Shapes ("hatch + dashed (unknown), hatch + yellow dashed (needs calibration)…") and the Colors claim "tile borders and hatch vs background ≥ 3:1".
- **Problem:** See the table above. unknown and needsCalibration are identical in shape, and noNode matches them once the hatch disappears. ok and stale are both 1 px solid outlines. Low-vision users, and anyone reading in sunlight in a garden (the product's main context), lose the "visibly not normal" signal the Brand section promises.
- **Fix:**
  - Hatch: draw it as thin lines, not 50/50 stripes, so text keeps a light ground. Use 1.5 px lines every 8 px at 135° in `status-hatch-line: '#8D8D8D'` (3.02 on #F4F4F4) and `status-hatch-line-dark: '#8D8D8D'` (3.48 on #393939), over `hatch-b` = layer-01 in both themes (not page bg in dark). Put tile text on a solid layer-01 backing plate so text contrast does not depend on the stripes.
  - OK tile: `status-ok-border: '#8D8D8D'` (3.29), `status-ok-border-dark: '#8D8D8D'` (4.56 on #262626).
  - Make each status unique by shape, not hue:
    - needsCalibration: 2 px dashed, or keep the hatch and add a Carbon icon, while unknown stays 1 px dashed.
    - noNode: dotted or "+" glyph only, no hatch.
    - stale: drop the border to 1 px dotted, or add a Carbon `time` / `cloud-offline` icon beside the foot line.
  - Add one Carbon status icon per status next to the status label (e.g. `drop`/`humidity` needsWater, `warning-alt` or `tools` needsCalibration, `help` unknown, `pause` paused, `add` noNode, `cloud-offline` stale). This is the cheapest robust fix and also helps with CVD, forced colours and low literacy.
  - Correct the Colors claim once the values are verified.

### H3. Tile foot line and helper text over the soil fill or hatch fail contrast
- **Where:** DESIGN.md tile anatomy ("bottom-left big value over mono foot line; soil-level fill rises from the bottom edge"). The OK-tile foot colour is unspecified; helper-coloured text is implied.
- **Problem:** The foot line always sits inside the fill once the Reading is above ~10 %. text-helper on #E0E0E0 is 3.75, and on dark #525252 it is 3.48. text-secondary on dark #525252 is only 4.57. The foot line carries load-bearing data (Reading time, low Threshold, "was ~40 % at 01:05", "as of 07:02") at 12 px mono.
- **Fix:** Specify tile foreground text explicitly per status. Use `text-primary` for the name and value and `text-secondary` for the foot line (light 5.92 on #E0E0E0). For dark, lower the level to `status-ok-level-dark: '#474747'` or keep the foot text `text-primary-dark` (7.10 on #525252). Add a CI rule to `packages/design-tokens`: every tile text token must be ≥ 4.5 against **every** surface it can overlap (fill, level, hatch-a, hatch-b).

### H4. Setup progress is shown by colour alone, and the colours merge under deuteranopia
- **Where:** DESIGN.md `setup-progress` (done `support-success`, active `primary`, pending `layer-01`); EXPERIENCE.md Setup progress.
- **Problem:** Done vs active is 1.29:1 and ΔE 7 under deutan, so it looks like the same colour. Pending layer-01 on white is 1.09, which is invisible. Done green on white is 2.04. The segment labels do not say which state each segment is in.
- **Fix:**
  - Show state on every segment with an icon plus text: `checkmark` for done, an animated-free `in-progress` icon for active ("JOINING…"), empty for pending. Give pending segments a 1 px `border-strong` outline.
  - Darken done to `setup-done: '#2E7D00'` light (4.71 on layer-01). Keep #48CF00 in dark.
  - Expose it as one progress element with value text ("Step 3 of 4, joining Wi-Fi").

### H5. The history chart cannot be read in the light theme
- **Where:** `chart-bar` #C6C6C6, `chart-bar-below-low` #FF770F, `chart-band` #F2FFE3, `history-chart.low-line: primary`.
- **Problem:** Bars vs background are 1.69. The below-low bar vs a normal bar is 1.56 (1.89 dark), and the "orange = below 30 %" legend relies on hue. The band vs background is 1.03 light and 1.27 dark. The low line on the band is 2.56. Only the text summary survives.
- **Fix:**
  - Light: `chart-bar: '#8D8D8D'` (3.29 on bg). `chart-bar-below-low: '#B84A00'` (5.03 on band). Mark below-low days with a non-colour cue as well: a solid bar vs an outlined or hatched bar, or a small marker under the bar. The bar ending below the low line helps only if the line itself passes.
  - `low-line` in `primary-text` light, drawn 2 px solid. Keep the high line dashed.
  - Treat the band as decorative (no 3:1 needed), or give it 1 px edges.
  - Dark: `chart-bar-dark` passes on bg (3.01, marginal) but is 2.38 on the band. Use `#8D8D8D` (3.60 on #124000).

### H6. Fixed-aspect tiles conflict with Dynamic Type, font scale and 200 % zoom / reflow
- **Where:** DESIGN.md Layout ("square tiles (web: 1 : 0.82) in a fixed-size grid… Tiles never shrink"); EXPERIENCE.md Accessibility Floor ("no truncated controls or clipped values… grid drops to 1 column `[ASSUMPTION]`"), Responsive ("Tiles keep their size at every width"), i18n ("wrap to two lines, then truncate").
- **Problem:** At iOS AX5 or Android 200 % font scale, the tile holds a 17 → ~53 pt name on 2 lines, a 13 → ~40 pt status label on 2 lines, a 36 → 72 pt value (capped) and a 12 → ~36 pt foot line. That cannot fit a 2-up square on a 375 pt phone, and does not fit a 1-up square either once the name wraps. On the web at 320 CSS px (WCAG 1.4.10), two columns give roughly 140 px tiles with a 52 px value. A fixed aspect ratio clips text-only zoom and text-spacing overrides (1.4.4, 1.4.12).
- **Fix:**
  - Make square a **minimum**, not fixed: `aspect-ratio` as `min-height`, with height growing to content. Or switch the grid to 1 column at a text-size threshold (iOS ≥ `.accessibility1`, Android fontScale ≥ 1.5, web container width < 400 CSS px). Decide this as a rule; drop the `[ASSUMPTION]`.
  - At accessibility sizes the value may sit below the name instead of pinned bottom-left, and the soil fill becomes a thin side bar.
  - Say explicitly that web tiles use `rem`-based sizes and a single column below 400 CSS px.
  - Resolve the conflict with the truncation rule; see M9.

### H7. No announcement or live-region policy for things that change while the user waits
- **Where:** EXPERIENCE.md Setup progress, Calibration reference point, Device candidate tile ("list keeps scanning"), Stale header ("Age ticks every minute"), Transport states, Accessibility Floor ("BLE timeouts announce their message" only).
- **Problem:** A blind user doing the Hub setup hears nothing as segments advance. Nobody tells them when "Record dry" becomes enabled after a wait of up to 15 minutes, when entering or leaving stale mode, when "Hub is online" lands, or when a new candidate appears. Conversely, an unscoped live region on the ticking age, or on the elapsed-seconds counter, would spam them. WCAG 4.1.3 Status Messages applies.
- **Fix:** Add an "Announcements" table to EXPERIENCE.md:
  - **Polite:**
    - each setup-progress segment change ("Wi-Fi sent", "Joining Novak-Home", "Server sees Hub 3F2A")
    - fresh Reading arrived ("New Reading 07:17, raw 612. Record dry is available.")
    - entering stale mode ("Can't reach your Server. Showing data from 07:02.") and leaving it ("Live again.")
    - Hub-silent notice appearing
    - new candidate found ("Hub 3F2A found, strong signal")
    - Save / "Saving…" result
  - **Assertive:** setup errors and timeouts.
  - **Never announce:** the per-minute age tick, the elapsed-seconds counter, background refetches that do not change status.
  - On screen change (outcome screens, error screens, step advance), move accessibility focus to the step title or headline.
  - Platforms: iOS `AccessibilityNotification.Announcement` / `.screenChanged`; Android `liveRegion` semantics and `announceForAccessibility` sparingly; web `role="status"` / `aria-live="polite"` regions that exist before they are populated.

---

## Medium

### M1. Error eyebrow red fails as text
`support-error` #FF454B text is 3.36 light and 4.47 dark ("STEP 5 STOPPED", validation reasons if drawn red). Add `support-error-text: '#DA1E28'` light (4.96) and `'#FF8389'` dark (6.38). Keep #FF454B for the 2 px invalid border (passes 3:1). Always put an error icon (`error-filled`) beside the invalid field reason, and connect the reason via `aria-describedby` / `accessibilityHint` / `error()` semantics.

### M2. Assorted non-text borders below 3:1
- Calibration dashed border on hatch-a is 2.78 light. Use `status-calibration-border: '#7A6800'` (5.47 bg / 4.18 hatch).
- Dark input bottom border `border-strong-dark` #6F6F6F on field-01-dark is 2.30. Use `#8D8D8D` for the field border in dark (3.48).
- Confirm panel `support-warning` dash on white is 1.51. Use `#9C8400` in light.

### M3. The soil level and low-Threshold tick do not show "how dry"
The OK soil level is 1.20 / 1.48, the needs-water level inside orange is 1.51, and the low tick over the soil level is 2.51 light. The value text carries the data, so this is supplementary, but the Brand claim ("how dry reads before any number does") does not hold for low vision. Draw a 2 px top edge on the fill (`status-level-edge: '#525252'` light on #F4F4F4 is 7.1; `#0A0A0A` inside orange; `#C6C6C6` dark). Make the tick 2 × 12 px in text-secondary.

### M4. Disabled states hide the reason, and disabled controls are hard to discover
`selectable-tile.disabled-foreground: text-disabled` (1.69 light) is used for tiles whose **reason** text ("HAS A NODE", WPA3 "Not supported…") the user needs. In stale mode, admin actions are "disabled with 'Needs your Server'". Disabled web buttons are not focusable, so a screen-reader or keyboard user never reaches that reason. Fix:
- Dim only the name. Draw the reason in text-secondary (7.75).
- Use `aria-disabled="true"` (focusable, announces "dimmed") instead of `disabled` for actions with an explanation. On iOS/Android, put the reason in the accessibility hint.
- Or replace the disabled actions with a single inline notice.

### M5. Gaps in the screen-reader labels
The State Patterns labels are good. Missing:
- the OK-tile Health condition ("OK · BATTERY 14 %" must reach the label: "Herbs, OK, about 35 percent, low 25 percent, Node battery 14 percent")
- the HUB SILENT variant of unknown
- the full stale label ("Tomatoes, was needs water, not live, as of 07:02")
- the button trait / role
- combining child elements into one element per tile (`accessibilityElement(children: .ignore)` / `mergeDescendants` / a single `<a>` with `aria-label`) so "~", "—", "+" and "6 h" are never read raw
- the overview headline exposed as a heading
- the tab label "Alerts · 5" exposed as "Alerts, 5 open" (accessibilityValue / `badge` semantics), not "Alerts dot 5"

Label templates must be externalized, pluralised strings, not concatenations (i18n).

### M6. The chart needs more than a summary sentence
Add:
- a data-table / list alternative: web `<table>` behind "Show as table" or visually hidden; mobile a "Daily values" list screen
- iOS Audio Graphs (`AXChartDescriptor`)
- per-bar focusable elements or an adjustable element with the ◀▶ swipe on mobile
- arrow-key navigation between bars on web, because the "tap/drag a bar" tooltip currently has no keyboard equivalent
- spoken gaps ("3 October, no Readings"), never zero
- a Sensor picker that is a labelled segmented control

### M7. The time-limit policy cannot be implemented as written
"Setup flows never time out while a screen reader is reading" is not detectable. The real timeouts are hardware-driven: the Node setup mode (firmware), the 90 s Hub join, the 30 s scan hint. These fall under the WCAG 2.2.1 real-time exception, but the spine should say:
- which timeouts are device-imposed and which are app-imposed
- app-imposed waits (scan hint, calibration wait) never end the flow and only change the message
- retrying after a timeout keeps everything already entered (SSID, Wi-Fi password, setup code); today "Re-enter password" implies the user must retype it
- the Node setup-mode window is long enough for a user of switch control or screen reader to type the setup code; state the firmware minimum, e.g. ≥ 3 min, as an input to the Node epic

### M8. The live candidate list reorders under the finger and the screen-reader cursor
"Strongest first" while "list keeps scanning" means candidates jump position. That causes mis-taps for motor-impaired users and lost place for screen-reader users. Keep insertion order after the first appearance, sort only on first display, and mark "PRESSED JUST NOW" with a text badge rather than moving the item to the top. Announce new items politely (H7).

### M9. Truncation contradicts the floor
i18n says "tile names and status labels wrap to two lines, then truncate with full text in the accessibility label". The floor says "no truncated controls or clipped values". The accessibility label does not help sighted low-vision users at large text. Rule: below accessibility sizes, truncating after 2 lines is allowed and the full name is always shown on Lot detail. At accessibility sizes, never truncate; the tile grows (H6). Status labels never truncate; use shorter i18n strings instead.

### M10. Small condensed type carries critical data
`status-label` is 13 px Ubuntu Condensed uppercase and `meta-mono` is 12 px for the foot line. Condensed uppercase at 13 px is the hardest style in the set to read, and it carries the status word. Use at least 14 px for the status label with `letterSpacing 0.06em` kept, `meta-mono` at 13 px on tiles, and no Ubuntu Condensed below 14 px. Weight 300 is fine at ≥ 36 px but should not be used below that (the spine already complies).

### M11. No plan for forced colours or high contrast
Windows forced-colors (web) removes background images (hatch) and background fills (orange tile, OK fill), so only borders and text remain. That makes the needs-water tile look like every other tile. iOS Increase Contrast / Differentiate Without Color, Android high-contrast text and Smart Invert are unaddressed. Fix:
- a `@media (forced-colors: active)` block: needs-water tile `border: 4px solid CanvasText` plus the status icon from H2; hatch → `border-style: dashed`; `forced-color-adjust: none` only on the chart
- honour iOS `accessibilityDifferentiateWithoutColor` / Increase Contrast by switching to the higher-contrast token variants
- mark tile fills and charts `accessibilityIgnoresInvertColors` where inversion breaks meaning

### M12. Web structure basics are missing
Add to EXPERIENCE.md Responsive / Web:
- a skip link to main content
- landmarks (`header`, `nav`, `main`, `aside` for the Open Alerts rail)
- a unique `<title>` per route ("Tomatoes · Home garden · Coldframe")
- `lang` attribute from the locale
- a focus trap in DS Modal, with focus returned to the invoking control on close
- focus moved to the page `<h1>` on client-side route change (SvelteKit does not do this reliably by default)
- the Site tabs in AppHeader as a real tablist or links with `aria-current`
- the tile grid as a `list` of links (`role="list"`) so users hear "list, 6 items"

---

## Low

- **L1. "~", "—", "+", "h"/"min" in visible text and push bodies.** In-app labels override these (good), but push bodies are read raw: "tilde twenty percent", or "twenty percent" with the approximation lost. Consider "about 20 %" in notification bodies, or accept the loss knowingly. Use unit-style formatters with a spelled-out style for any duration or percentage text that has no explicit label.
- **L2. Target sizes.** The floor says 44 px web targets, but `field-height: 40px` for web text inputs does not meet it. It passes WCAG 2.5.8 (24 px), so align the floor or use 48 px fields. Specify ≥ 44/48 hit areas for the password-reveal icon, the "accepted" chip if interactive, Threshold column drag handles, and 4-way segmented choices at large text (allow wrapping to 2 rows).
- **L3. Gesture alternatives.** Swipe-to-Pause on Device rows (`[ASSUMPTION]`) and pull-to-refresh need visible or custom-action alternatives: accessibility custom actions / `customActions` semantics, and a "Refresh" action in the overflow menu. The Threshold column drag is already covered by typing (WCAG 2.5.7). Give it adjustable / Slider semantics in 5 % steps.
- **L4. Marginal passes that will fail after rounding.** In dark theme, the no-node border and border-strong are #6F6F6F on #262626 (3.01), chart bar #6F6F6F (3.01), paused border #8945FF (3.14), and text-secondary on #525252 (4.57). Use #8D8D8D for dark dashed and neutral borders and #9A66FF for the dark paused border.
- **L5. Contrast claims in DESIGN.md are inaccurate.** It says needs-water ink is "8.2:1" / "≈ 8:1"; the actual is 7.45. It says "tile borders and hatch vs background ≥ 3:1"; that is false for the hatch, the OK border and the soil level. Replace the prose with a generated table from the `packages/design-tokens` CI check, so the numbers cannot drift.
- **L6. Stale component.** `calibration-live-value` ("LIVE RAW VALUE", "steady for 5 s" in green at 1.87:1) describes the live-BLE calibration that the memlog dropped (AD-9 stored Readings). Remove it or rewrite it as "latest Reading" with text-primary plus a `checkmark` icon for "fresh".
- **L7. Orange surfaces with unspecified text.** The Member tile's "initials orange for you" has no foreground token; use `ink-on-bright`, not white. Sign-in: no text may sit directly on the orange→purple radial (white on #FF770F is 2.64). The Notification Window bar (orange vs hatch 2.01) is backed by the "07:00 to 22:00" text, which is fine; expose the bar as decorative.
- **L8. Needs-water tile vs white page is 2.64.** It is large, solid and labelled, so it is perceivable. If H2 icons are adopted, nothing more is needed. Otherwise consider a 1 px `#C25C00` edge in light (4.32 on bg, 3.96 on layer-01).

---

## Suggested token changes (summary)

| Token | Current | Suggested | Result |
|---|---|---|---|
| `button-primary.foreground` | text-on-color #FEFEFE | ink-on-bright #0A0A0A | 7.45 / 5.87 / 4.55 |
| new `focus-inner` / `-dark` | — | #161616 / #F4F4F4, 2 px + 2 px offset | 6.81 on orange, 17.9 on white |
| new `primary-text` (light) | — | #B84A00 | 5.18 bg, 4.75 layer-01 |
| `status-ok-border` / `-dark` | #E0E0E0 / #393939 | #8D8D8D / #8D8D8D | 3.29 / 4.56 |
| new `status-hatch-line` / `-dark` | stripes #E0E0E0/#F4F4F4, #393939/#262626 | 1.5 px lines #8D8D8D on layer-01 | 3.02 / 3.48 |
| `status-calibration-border` | #9C8400 | #7A6800 | 5.47 bg, 4.18 hatch |
| new `status-level-edge` | — | #525252 light / #C6C6C6 dark / #0A0A0A in orange | ≥ 3:1 |
| `chart-bar` | #C6C6C6 | #8D8D8D | 3.29 |
| `chart-bar-below-low` (light) | #FF770F | #B84A00 + outlined/marker cue | 5.03 on band |
| `chart-bar-dark` | #6F6F6F | #8D8D8D | 3.60 on band |
| new `support-error-text` / `-dark` | #FF454B | #DA1E28 / #FF8389 | 4.96 / 6.38 |
| new `setup-done` (light) | #48CF00 | #2E7D00 + checkmark | 4.71 |
| field border dark | #6F6F6F | #8D8D8D | 3.48 |
| confirm-panel border (light) | #FFCB0F | #9C8400 | 3.64 |
| dark dashed/neutral borders | #6F6F6F | #8D8D8D | 4.56 |
