# Escendit Design System

A design system extracted from the public Escendit Ltd / GmbH codebase, primarily the [`escendit/branding`](https://github.com/escendit/branding), [`escendit/identity-provider-design`](https://github.com/escendit/identity-provider-design), [`escendit/email-design`](https://github.com/escendit/email-design), and [`escendit/tools-branding`](https://github.com/escendit/tools-branding) repositories.

## What is Escendit?

Escendit is a developer-tools / enterprise-software organization (Latin *escendit* = "ascends, climbs"). It publishes:

- **OSS .NET / Microsoft Orleans extension libraries** on GitHub and NuGet (Cassandra, RabbitMQ, OpenSearch, MongoDB clients & Orleans providers).
- **Internal enterprise products** (private repos): Escendit **Portal**, **Manager Console**, **Developers Console**, **Runtime Server** (Orleans cluster), **Runtime Manager**, **API Manager**, plus an **Identity Provider** stack built on Keycloak.
- **Build / branding tooling**: `Escendit.Tools.Branding` NuGet package, an `@escendit/branding` npm package, and a Maven Keycloak theme.

The visual system is built on top of **IBM Carbon Design System** (`@carbon/styles`, `@carbon/themes`, `@carbon/grid`, `@carbon/layout`) — Escendit overrides Carbon's tokens with its own primary/secondary colors and Ubuntu typography. This is **not** a Microsoft-blue conservative brand (despite the enterprise positioning); it is **bold orange + purple on a Carbon foundation**.

## Sources

| Repo | What it gave us |
|---|---|
| `escendit/branding` | Color palettes (`src/presets/colors.js`), CSS/SCSS variable definitions |
| `escendit/identity-provider-design` | Logos, login/welcome HTML, button + form SCSS, theme mixins, Ubuntu type stack |
| `escendit/email-design` | Spacing/radius/shadow/font scale (Tailwind config), Keycloak email templates |
| `escendit/tools-branding` | NuGet package icon (alt logo treatment) |
| `escendit/identity-provider-theme` | Keycloak theme structure (Maven, not imported in detail) |

The original imported files are preserved under `_source/`. Production assets you should reference live in `assets/` and `colors_and_type.css`.

## Caveats / Substitutions

- **Direction conflict.** You requested an "enterprise & trustworthy, blue, Microsoft-adjacent" direction. The actual Escendit codebase is the opposite — **bold orange + purple, Ubuntu type, gradient backgrounds**. I followed the codebase, since fabricating a different direction would defeat the point of using `github.com/escendit` as source of truth. **If you want a divergent blue treatment, tell me and I'll add it as a Tweak alongside the canonical orange.**
- No font files are bundled. **Ubuntu**, **Ubuntu Mono**, **Ubuntu Condensed** and **Quattrocento** are loaded from Google Fonts (which is what the codebase does). If you want offline `.ttf` files, ask and I'll add them.
- The `portal`, `manager`, `console`, `runtime-*`, `api-manager`, `architecture`, and `email-design` private docs were not exhaustively explored — I imported their public design siblings. If you want me to dig into a specific private product, name it.
- I have **not** seen any slide deck templates in the org. The `slides/` directory contains plausible Escendit-styled slides created from the foundations, not exact copies of an existing template.

---

## Index

```
Escendit Design System/
├── README.md                  ← you are here
├── SKILL.md                   ← Agent-Skills compatible entry point
├── colors_and_type.css        ← all tokens, fonts, semantic CSS vars
├── assets/
│   ├── escendit-logo-on-white.svg  full wordmark for white backgrounds
│   ├── escendit-logo-on-orange.svg full wordmark for orange backgrounds
│   ├── escendit-logo-on-black.svg  full wordmark for black backgrounds
│   ├── escendit-mark.svg           orange mark only (the "stacked truck")
│   ├── escendit-mark.png           raster fallback
│   ├── escendit-package-icon.png   alt mark used for NuGet packages
│   └── icons/                      arrows, checkmark, close, social brands
├── ui_kits/
│   ├── identity-provider/          login / register / welcome (Keycloak)
│   ├── docs-site/                  README + library docs treatment
│   └── nuget-package/              package listing card
├── slides/                          16:9 sample slides in Escendit style
├── templates/
│   └── component-reference/         live DC reference of all UI components (light/dark) — synced from the branding-v2 project
├── preview/                         design-system tab cards (700×N)
└── _source/                         original imported files (read-only ref)
    └── component-handoff-2026-09/   Svelte 5 + Tailwind 4 handoff spec, theme.css, Button/TextInput/ThemeSwitcher.svelte
```

---

## Components

Real React components compiled into `_ds_bundle.js` (namespace `EscenditDesignSystem_019dfe`). Each lives in `components/<Name>/` as `<Name>.jsx` + `<Name>.d.ts` + a `@dsCard` preview. Styles are lifted 1:1 from `templates/component-reference/`.

- **Icons** (`Icon`) — Carbon-style 32×32 glyph set, `currentColor`
- **Button** — primary · secondary · tertiary · accept · decline · ghost; sm/md/lg/xl; icon-only
- **TextInput** — label link, helper, invalid, disabled, password toggle
- **Checkbox** — checked / indeterminate / disabled / brand accent
- **Select** — custom listbox, invalid, disabled
- **DatePicker** (+ `CalendarMonth`) — field + 288px month grid
- **DateRangePicker** — presets rail + two months
- **InlineNotification** — error · success · info · warning
- **Toast** (+ `Toaster`) — 288px floating notification
- **Tag** — status chips, sm/md, dismissible, solid version
- **Card** — border-defined, stat, clickable
- **DataTable** — toolbar, sortable, selectable, pagination
- **Modal** — sm/md/lg, danger, inline preview
- **ThemeSwitcher** — segmented · menu · icon; light/dark/system
- **AppHeader** (+ `HeaderAction`) — 48px UI-shell bar: app switcher, tenant, nav or search
- **AppShell** (+ `SideNav`) — header + 256px rail + main

---

## Content Fundamentals

How Escendit writes copy, based on the imported READMEs, login HTML, and email templates.

**Voice & tone.** Direct, technical, and pragmatic. The reader is assumed to be a .NET developer or a platform engineer. There is **no marketing fluff** — the `tools-branding` README opens with "A NuGet package that sets the MSBuild properties such as Authors, PackageIcon..." and gets straight to install commands. No taglines, no superlatives, no "delight."

**Person.** Mostly second-person imperative ("To install this package, use..." / "You can modify the properties..."). First-person plural ("we") only appears in legal/license contexts.

**Casing.** Sentence case for headings ("Administration Console", "Signin to your account", "Forgot password?"). UPPERCASE only for action button labels (LOGIN, HOME, ACCEPT, DECLINE) — this is enforced in CSS (`text-transform: uppercase` + `font-family: Ubuntu Condensed`), not in copy.

**Microcopy patterns.**
- Form labels: short, sentence-case, no colon ("Email", "Password").
- Inline links sit in label rows: `Email · Create account?` / `Password · Forgot password?`.
- Helper text below fields is terse and direct: "Email is required."
- Errors use a plain `Error` title with a one-line subtitle, never an exclamation point.

**Emoji and unicode.** None. The codebase contains no emoji in UI strings. Iconography is exclusively SVG (Carbon-style 32×32 icons + custom brand marks).

**Examples lifted from the codebase:**
- Page title: `Welcome` (literally just that word — `welcome.html` is a 7-line file).
- Login heading: `Administration Console` → `Signin to your account` (note: "Signin" is one word — likely a bug in the original, preserved for fidelity).
- Button labels: `Home`, `Login`, `ACCEPT`, `DECLINE`.
- Email subjects (from Keycloak templates): `Email verification`, `Password reset`, `Update password`.

**Vibe.** Carbon-via-Ubuntu — feels like an internal tool at a serious infrastructure company. Not playful, not bureaucratic. Precise.

---

## Visual Foundations

**Colors.** A 9-step palette per hue, all defined in `_source/escendit-design/presets/colors.js`. Primary is **`#FF770F` orange**; secondary is **`#670FFF` purple**. Both at the 500 step. Neutrals are a custom 11-step gray scale (50 → 900, plus an 850). Semantic hues: red, orange (warning, distinct from primary), yellow, green, blue. Most UI surfaces use Carbon's white/g90 themes underneath, with Escendit colors overriding `focus`, `link-primary`, and `support-error`.

**Typography.** **Ubuntu** for body (300, 400 the only weights loaded from Google Fonts). **Ubuntu Condensed** for headings *and* uppercased action buttons. **Ubuntu Mono** for code. **Quattrocento** (serif) for long-form prose like ToS bodies — a deliberate, narrow use case. Heading hierarchy is anchored at 16pt for h2 and 15pt for h3, which is small by web standards but matches Carbon's productive type scale.

**Spacing.** Carbon's spacing scale (`spacing-01` = 2px through `spacing-13` = 160px). The `_source/escendit-design/login/_index.scss` uses `spacing-13` (160px) as the page-edge gutter for the login card — generous, asymmetric whitespace is part of the look.

**Backgrounds.** The signature treatment is a **radial-gradient from primary orange (top-left) to secondary purple (bottom-right)**, defined exactly once in `_source/escendit-design/scss/themes/_mixins.scss`:

```scss
background: radial-gradient(farthest-corner at 5% 5%,
  $esc-color-primary,
  $esc-color-secondary) no-repeat;
```

This is used on the body of authentication pages (login, welcome). On most app surfaces, plain white (`#FEFEFE`) or Carbon g90 (`#262626`) is used. **No images, no patterns, no textures.** Hand-drawn illustrations: none. Photography: none in the codebase.

**Animation.** None defined in the imported SCSS. Carbon's defaults (150ms ease-out for hover) apply. No bounces, no parallax.

**Hover states.** Buttons darken 5% on light theme, lighten 5% on dark theme — an explicit pattern in `_button.scss`. Links: same darken/lighten rule. No opacity-fade hovers.

**Press / active states.** Same direction as hover, deeper magnitude (10% for decline buttons). No shrink/scale transforms.

**Borders.** 1px solid borders, color from `--esc-border-subtle` (#e0e0e0 light / #525252 dark) or `--esc-border-strong` (#8d8d8d). The login form uses a 1px `<hr>` colored with `border-strong`. Carbon-style — no rounded everything.

**Shadows.** Tailwind's default scale is exposed in the email-design config (sm → 2xl + inner). The Carbon-themed app surfaces lean on borders rather than shadows.

**Capsules vs. protection gradients.** Capsule pills are not used. Buttons are square Carbon-style rectangles with `border-radius: 0` (default) — no pill capsules.

**Layout rules.** Login/welcome use a fixed 22rem-wide column anchored top-left with `margin: spacing-13` on three sides. Header lives in the same column. Footer images are absolute-positioned bottom-right. App shells follow Carbon's `flex-grid`.

**Transparency / blur.** Not used. Solid surfaces only.

**Corner radii.** **Default = 0** in the Carbon-based UI (login forms have square corners). The email-design Tailwind config defines a full radius scale (2 → 24px) — used for marketing emails, not for app chrome.

**Cards.** Borders, not shadows. No rounding. White `--esc-layer-02` on top of `--esc-layer-01` gray background creates the "card" effect (Carbon's standard layer pattern).

**Image treatment.** The codebase doesn't ship product photography. The package icon (`assets/escendit-package-icon.png`) is a flat orange tile with the white mark — that defines the "image vibe": **flat, saturated, geometric.** No grain, no warm-cool color grading. If you bring in photography, it should be high-contrast and slightly desaturated to sit alongside the saturated brand colors without competing.

---

## Iconography

**Approach.** Two distinct icon systems, used in different contexts.

1. **Carbon icons (32×32, currentColor fills).** This is the in-app system. The login page uses inline SVGs from Carbon's icon set (`<svg viewBox="0 0 32 32" fill="currentColor">` with `Transparent_Rectangle` placeholders). Stroke-less, filled glyphs. Examples in the imported source: home, arrow-right, eye, error-fill, close.
2. **Custom brand SVGs (16×16).** Imported under `assets/icons/` — `arrow-left`, `arrow-right`, `checkmark`, `close`. Same fill-only style, smaller default size. These are content/UI accents, not toolbar icons.
3. **Brand-icon SVGs.** Social glyphs from FontAwesome's "brands" set (`facebook-square-brands`, `instagram-square-brands`, `linkedin-brands`, `youtube-square-brands`) live in the email-design pipeline. **Substitution flag:** these are FontAwesome assets shipped inside the Escendit repo. If you redistribute, check the FontAwesome license.

**No icon font.** Everything is SVG, inlined or `<img>`-referenced.
**No emoji.** Confirmed via codebase grep — no emoji in any imported file.
**No unicode-glyph icons.** No `→`, `✓`, `×` in source — those exist as SVG files (`arrow-right.svg`, `checkmark.svg`, `close.svg`).

**For new designs**, the rule is: use Carbon icons (CDN: `@carbon/icons` 32px, fill currentColor) for in-app glyphs, and the imported SVGs in `assets/icons/` for arrows / checkmarks / dismiss. **Do not draw new SVGs from scratch and do not use emoji.**

---

## Logos

| File | Use |
|---|---|
| `assets/escendit-logo-on-white.svg` | Full wordmark, WCAG AA (≈18.1:1) — for white backgrounds |
| `assets/escendit-logo-on-orange.svg` | Full wordmark, WCAG AA (≈6.2:1) — for brand-orange backgrounds |
| `assets/escendit-logo-on-black.svg` | Full wordmark, WCAG AA (≈19.6:1) — for black/dark backgrounds |
| `assets/escendit-mark.svg` | Mark only (orange "stacked truck" / quarry shape) — favicon, app icon |
| `assets/escendit-mark.png` | Raster fallback of the mark |
| `assets/escendit-package-icon.png` | NuGet package icon — solid orange tile with white mark (used by `Escendit.Tools.Branding`) |

The mark itself is two layered parallelograms — a **light-orange `#FFAE60`** rear plane and a **darker `#F28B00`** front plane. These two specific orange values are *not* in the main color scale; they're hardcoded into the SVG path styles. Treat them as logo-only; do not introduce them as additional brand colors.
