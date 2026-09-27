---
stepsCompleted: [1, 2, 3, 4]
inputDocuments:
  - _bmad-output/specs/spec-coldframe/SPEC.md
  - _bmad-output/specs/spec-coldframe/acceptance-criteria.md
  - _bmad-output/specs/spec-coldframe/device-hardware.md
  - _bmad-output/specs/spec-coldframe/user-journeys.md
  - _bmad-output/specs/spec-coldframe/glossary.md
  - _bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md
  - _bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/DESIGN.md
  - _bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/EXPERIENCE.md
  - _bmad-output/planning-artifacts/prds/prd-coldframe-2026-09-25/prd.md
---

# Coldframe - Epic Breakdown

## Overview

This document breaks Coldframe V1 down into epics and stories that can be implemented. It draws on four inputs:

- **The spec** (`SPEC.md` and its companions) is the primary source of requirements. Capability CAP-N is the same as FR-N.
- **The UX contract** (`DESIGN.md` and `EXPERIENCE.md`).
- **The architecture spine**, whose decisions are cited by `AD-n`.
- **The PRD**, kept for traceability only.

## Requirements Inventory

### Functional Requirements

FR1: An Administrator provisions a Hub with Wi-Fi credentials from the iOS or Android app over BLE and binds it to a Site.
- The Hub joins Wi-Fi only through provisioning; the firmware contains no credentials.
- The Hub appears on the Site within 1 minute.
- The session is encrypted and bound to the Device's proof-of-possession setup code.

FR2: An Administrator identifies a nearby unassigned Node over BLE and assigns it to a Site and Lot.
- The Node is visible only after its setup button is pressed, and only for a limited time.
- The app lists only Nodes that are in range.
- A Lot that already holds a Node rejects a second one.
- Reassigning a Node keeps its Reading history.
- An unassigned Node's Readings are stored but not evaluated.

FR3: Each Node declares its Sensors and Sensor Specifications when it first reports.
- Calibration controls are shown only for `calibration: true` Sensors.
- Soil moisture alerts by default; other Sensors are only watched until an Owner or Administrator sets Thresholds.
- Air quality is reported as gas resistance (Ω) and alerts on the low Threshold.
- Redeclaring a Specification never overwrites a Threshold override.

FR4: A Node reports one Reading per Sensor every 15 minutes through the Hub, plus battery % and charging status.
- Readings carry the Node's measurement time. All Readings from one wake share that time.
- Reporting survives a router Wi-Fi channel change.
- The Hub stores no Readings.
- The Node buffers at least 24 h of unacknowledged Readings and resends them. A Reading is acknowledged only after a durable commit.

FR5: Users sign in to the mobile and web apps through the self-hosted Keycloak (OIDC).
- The Server stores no passwords.
- Sites, Memberships and Roles live in Keycloak (Phase Two Organizations), with a projection on the Server.

FR6: Sites and Lots can be created and managed.
- Any User can create a Site and becomes its first Owner immediately.
- An Owner can rename the Site.
- An Administrator can create, rename and remove Lots.
- Removing a Lot that holds a Node is rejected.

FR7: An Owner can invite people by email with a Role, change their Role and remove their Membership.
- Acceptance happens on the home network, via the Phase Two invitation flow.
- Every Site always keeps at least one Owner.
- Changes by a Member to Devices, Thresholds, Calibration, Pause or Memberships are rejected with an authorization error.
- Roles apply per Site.

FR8: Any Member sees the Site overview:
- the latest Reading of every Sensor, grouped by Lot, and a 30-day history chart
- each Device's last-seen time, battery and charging status
- exactly one status per Lot: needs water, needs calibration, OK, unknown (with the time since the last Reading), paused or no Node
  - the status is computed on the Server
  - precedence: noNode > paused > unknown > needsCalibration > needsWater > ok
- Lots sorted as needs water, needs calibration, unknown, OK, paused, no Node
- a stale Reading never shown as current, and the apps say when the Server is unreachable and how old the data is

Readings are retained indefinitely.

FR9: An Administrator calibrates a `calibration: true` Sensor with *dry* and *wet* points.
- Each point is the raw value of a stored Reading, submitted over REST and never over BLE.
- A short press of the Node's setup button triggers an immediate Reading.
- Thresholds and calibrated Readings are shown in 0–100 %.
- Recalibration affects only new Readings and keeps Threshold % values.
- History keeps its original Calibration.

FR10: An Owner or Administrator sets, changes or removes a Sensor's low and high Thresholds.
- The low Threshold is required on an alerting Sensor. The high Threshold is optional, and an empty high never alerts.
- Low must be below high.
- New Sensors start with the Specification defaults.
- Without a default, the proposed low Threshold is Min + 20 % × (Max − Min). There is no fallback for the high Threshold.
- Other Roles are rejected.

FR11: The Server opens and closes Threshold Alerts.
- An Alert opens after 3 consecutive Readings beyond a Threshold, about 30 min after the first crossing.
- It closes after 3 consecutive Readings back within the Thresholds; 1–2 Readings on the other side change nothing.
- The Alert names the Lot and the side crossed. For low soil moisture it reads "‹Lot› needs water".
- A closed Alert sends no Reminders.

FR12: While a Threshold Alert stays open, Reminders repeat at the resolved cadence: the User's setting for the Site, else the Site setting, else once per day. An Owner or Administrator sets the Site cadence.

FR13: The Server opens a Silent Device Alert when a Node or Hub reports nothing for longer than its Silence Window.
- Default Silence Window: 6 h for a Node, 5 min for a Hub. It can be overridden per Device.
- The Hub sends a heartbeat every 30–60 s.
- A silent Hub is reported as the Hub, not as each Node behind it.
- The Alert closes when the Device reports again.
- Server downtime is never counted as Device silence.
- Health Alerts remind at most once per day.

FR14: The Server opens a Low-battery Alert after 3 consecutive reports below 20 % while not charging. It closes after 3 reports that are charging or ≥ 20 %. The Alert shows the level and charging status.

FR15: Alerts and Reminders are delivered as push notifications to every User with a Membership on the Site. Payloads are self-contained and understandable away from home.

FR16: Each User sets a daily Notification Window, evaluated in their IANA time zone. The zone is detected from the phone OS or the browser (IP as a last resort) and confirmed by the User.
- Default window: 07:00–22:00.
- Anything that falls due outside the window is sent as one summary when it opens, with at most one entry per open Alert.
- An Alert that opened and closed while held is not delivered.

FR17: Each User can mute and unmute all notifications for a Site. This affects only that User; Alerts still open and close for everyone.

FR18: An Administrator pauses and resumes a Device or a whole Site, with an optional end date.
- While paused, no Readings are ingested, no Alerts open, and open Alerts close.
- A Pause with an end date resumes automatically.
- On resume, the Silence Window restarts.
- Devices added to a paused Site start paused.
- A Device paused both individually and by its Site resumes only when both are lifted.

FR19: The iOS and Android apps provide:
- BLE provisioning, and Node identification and assignment
- Readings, Thresholds, Calibration and Pause
- Notification Window and Reminder settings, and Membership management
- notifications

FR20: The web app provides everything the mobile apps do except BLE functions, and shows Alerts as browser notifications while it is open. Browser notifications follow the same Notification Window and mute rules. There is no background web push.

FR21: The Server opens an Uncalibrated Sensor Health Alert when an assigned Node has a `calibration: true` Sensor without Calibration.
- The Alert names the Lot and the Sensor.
- It closes once the Sensor is calibrated.
- An uncalibrated Sensor opens no Threshold Alerts.

### NonFunctional Requirements

NFR1: **Local-first.** The Server and Keycloak run on the home network with no inbound internet traffic. Outbound traffic (APNs/FCM, SMTP, ACME DNS-01) is allowed.
NFR2: **Authenticated access.** Every API and web request except Device ingestion carries a Keycloak token and is authorized per Site by Role. Devices authenticate as Devices.
NFR3: **Durability.** Restarting the Server or the Hub loses no stored Readings, settings or open Alerts. Readings are retained indefinitely.
NFR4: **Energy autonomy.** A solar-charged Node runs a full season (about April–October) without manual recharging, and lasts at least 14 days with no sun at the 15-minute interval.
NFR5: **Outdoor survival.** The Node, panel and soil probe survive a season in wet soil without corrosion-driven drift making Alerts unreliable.
NFR6: **Multi-Site correctness.** Membership, Role and multi-Site behaviour are covered by automated tests, because only one Site is field-tested.
NFR7: **Reproducibility.** Another person can build the Node and Hub and run the full stack from the public docs alone. Container images are provided. The reference deployment is single-node RKE2 with Fleet; Compose is a reference example only.
NFR8: **Open source.** Firmware, Server, both apps and the Node/Hub hardware design are public under Apache-2.0. The firmware is Rust and documented as a learning reference. No proprietary vendor libraries are used.
NFR9: **Room for irrigation.** The data and command paths allow later Device commands without reworking the V1 model. Commands can require confirmation, can be stopped manually, and are blocked by Pause.
NFR10: **TLS everywhere.** All IP traffic uses TLS, including on the LAN, with no plain-HTTP endpoints.
NFR11: **Reach.** A Node reports reliably from the author's farthest Lot, where the home Wi-Fi is unusable.
NFR12: **Hardware baseline.** Node and Hub run Rust firmware on ESP32-S3. The air-quality Sensor reports raw gas resistance from a BME680.
NFR13: **Approximate soil moisture.** A separate ADC capacitive probe is used, with two-point linear Calibration and no temperature compensation. Temperature Readings from the same wake are recorded for later analysis.
NFR14: **Accessibility floor.**
- Dynamic Type on iOS, font scale on Android, and browser zoom are supported.
- Full VoiceOver, TalkBack and screen-reader labels are provided.
- Status is never conveyed by colour alone.
- Text meets 4.5:1 contrast, and UI and graphics meet 3:1.

NFR15: **Internationalisation readiness.** English only in V1. All strings are externalised, layouts tolerate 30–40 % longer text, and dates, times and numbers use locale formatting.
NFR16: **Test-first, tested by default.**
- Every story is built test-first: failing tests that express its acceptance criteria are written before the implementation (red → green → refactor).
- Each story ships with automated tests for the behaviour it adds, and CI blocks merges when they fail.
- **What kind of test belongs where:**
  - **Server:** unit tests for domain logic; Orleans TestCluster grain tests; API tests against the OpenAPI contract; integration tests run through **Aspire's testing host** (`Aspire.Hosting.Testing`), which starts the same AppHost used for local development: PostgreSQL, NATS, Temporal and Keycloak.
  - **Firmware:** host-side unit tests only, covering protocol, crypto, buffering, the channel re-scan and the setup protocol state machines, with hardware behind traits or mocks. **No on-device tests in CI.** Hardware behaviour is verified manually against a documented checklist.
  - **Shared Kotlin core:** unit tests for the BLE and setup protocol state machines, the API client and staleness logic, with a mocked BLE layer.
  - **Mobile UI:** snapshot or UI tests for the six Lot statuses in light and dark themes, with accessibility checks at the largest text size.
  - **Web:** component tests plus Playwright end-to-end tests, including an automated accessibility check (axe).
  - **Contracts:** the AD-24 checks.
- Journeys UJ-1 to UJ-6 each have at least one end-to-end or integration test where automation is possible. BLE parts are tested at the protocol level.

### Additional Requirements

These come from the architecture spine. There is **no starter template**. Epic 1 Story 1 is the **monorepo scaffold**, following AD-15 and AD-23:

- `apps/`, `packages/`, `tests/`, `aspire/`, `deploy/`, `hardware/` and `docs/`. All tests live in `tests/<lang>/…`, mirroring `apps/` and `packages/`, never next to the code they test. Exception: Rust unit tests stay inline (`#[cfg(test)]`).
- the Aspire AppHost for the local dev stack, which also serves as the Server integration-test host via `Aspire.Hosting.Testing`
- GitHub Actions CI
- Central Package Management

The rest of the list:

- **Server paradigm (AD-1, AD-2, AD-21):** an Orleans 10.3.1 actor model with one owning grain per entity. The grains are Site, Lot, Device, Sensor, Alert and User, all event-sourced (`JournaledGrain`, CustomStorage). Events go to one PostgreSQL JSON event journal with an outbox, and projectors read that journal by global position. Orleans streams on NATS JetStream are hints only.
- **Infrastructure lanes (AD-5, AD-6):**
  - Orleans Reminders are wake-ups only; every deadline is persisted as a due-at time.
  - Temporal carries only the Keycloak event pipeline, via `keycloak-temporal-extensions` v0.0.1-rc.2.
  - NATS backs only the Orleans streams, through the pinned alpha provider.
- **Identity (AD-3, AD-4):**
  - The Site grain is the only writer to Keycloak/Phase Two.
  - Create Site is an idempotent flow through the User grain.
  - The identity projection gives read-your-writes.
  - Authorization reads the caller's Role from the projection; an authorization-matrix test is generated.
  - Invitations use Phase Two's native flow.
- **Alerting and delivery (AD-7, AD-8):**
  - Alert IDs are deterministic, and only the grain that opened an Alert closes it.
  - Evaluation is monotonic in `measured_at` order.
  - A Node's Silence Alert is suppressed while its relay Hub is silent.
  - The User grain owns the Notification Window, summaries, mute and Reminders.
  - A Notifier seam wraps APNs, FCM and SignalR.
  - Pause is a set of sources, with an evaluation epoch.
- **Ingestion (AD-9, AD-17):**
  - Acknowledgement is per Reading, keyed by (device_id, reading_seq).
  - Each frame gets a status (stored, duplicate or rejected_*).
  - Readings go in an append-only table with monthly partitions, plus a device-reports table.
  - The Sensor grain owns the Calibration in force.
  - Frame counters never repeat, every frame is freshly sealed, and a 64-entry replay window applies.
  - The acknowledgement window W is 300 ms.
- **Relationships (AD-18, AD-19, AD-20):**
  - The Lot grain claims and releases occupancy.
  - The Site grain keeps the Device roster, and any enrolled Hub may relay.
  - `sensorId` is UUIDv5(device:slot:quantity), with a `spec_hash` on frames and Thresholds stored as Default, Override or Cleared.
  - Removal is a tombstone.
- **Contracts (AD-10):**
  - The contracts live in `packages/proto`, `packages/openapi`, `packages/asyncapi` and `packages/crypto-spec`, written contract-first with generated code.
  - Every frame carries a `protocol_version`, and the Server accepts the current and previous major versions.
  - Hub JSON uses golden fixtures.
- **Time (AD-11):** everything is stored in UTC. The Hub bootstraps its clock with SNTP. The Node sets its clock only from sealed downlinks. Unsynced Readings are rebased on the Server.
- **Device trust (AD-12):**
  - An eFuse HMAC root derives K_dev, and purpose keys come from HKDF.
  - Enrolment seals K_dev with HPKE to the Server's enrolment key.
  - ChaCha20-Poly1305 protects both uplink and downlink.
  - Hub requests are authenticated by HMAC.
  - A dev-mode build is available.
- **BLE setup (AD-25):** a custom GATT setup protocol shared by Hub and Node, secured by X25519 plus the proof-of-possession code, HKDF and ChaCha20-Poly1305, with test vectors.
- **TLS (AD-13):** Let's Encrypt DNS-01 certificates on a real domain, with split DNS. The apps and the Hub trust public roots only.
- **Clients (AD-14):**
  - Lot status is computed on the Server, including the `unknownCause` and `pausedBy` fields.
  - The KMP core (Kable, kotlin-multiplatform-oidc, generated API client, push-token registration) sits under SwiftUI and Jetpack Compose shells.
  - The SvelteKit web app is a backend-for-frontend using `@escendit/sveltekit-auth-keycloak`, with SignalR proxied through it.
  - There are two SignalR message families.
  - The mobile apps have the Server URL and Keycloak issuer as build-time configuration.
- **Deployment and operations (AD-15, AD-22, AD-23):**
  - Single-node RKE2 with Fleet and Helm charts, and CloudNativePG 1.30 (one cluster with separate databases).
  - Barman backups to off-node S3, with a tested restore.
  - Secrets created out of band.
  - Upgrades stop and restart the Server, with migrations first.
  - Migrations use FluentMigrator 8.0.1 with `Escendit.Orleans.Migrations.Cluster.PostgreSQL` 10.3.1-rc.0, and partitions are created at least 2 months ahead.
  - Images go to `ghcr.io/escendit/coldframe/<component>`, with one SemVer across the repo.
  - Firmware is flashed over USB in V1.
  - The Escendit service defaults are used.
- **Commands (AD-16):** the sealed downlink reserves an empty `commands` field. V1 has no command types.
- **CI test obligations (AD-24):**
  - the authorization matrix
  - crypto and BLE-session test vectors in Rust, C# and Kotlin
  - contract compatibility checks
  - Hub golden fixtures
  - an event-replay test
  - Orleans grain tests for the AD-7 and AD-8 transitions
- **Firmware requirements (from the spike and device-hardware):**
  - **H-1:** the Hub joins the strongest BSSID, scanning all channels.
  - **H-2:** TLS checks certificate dates, which needs cmake and ninja in CI.
  - **H-3:** WPA3-only networks are not supported; this is documented.
  - **N-1:** only the sealed acknowledgement counts as delivery.
  - **N-2:** the acknowledgement window is 300 ms, followed by a re-scan.
  - **N-3:** a short press of the setup button sends a Reading now, and a long press enters BLE setup mode.
  - Node BLE and ESP-NOW coexistence must be validated in the Node epic.
  - The power budget requires at most about 100 µA average sleep current.
- **Design tokens:** tokens from `escendit/branding` are vendored into `packages/design-tokens` and generated for Swift, Kotlin and CSS. The web app does not depend on the private npm package.

### UX Design Requirements

#### Tokens & theming

UX-DR1: Create `packages/design-tokens` by vendoring the Escendit DS semantic tokens from `escendit/branding` `css/theme.css` (not the private `@escendit/branding` package) and generating Swift (iOS), Kotlin (Android/KMP) and CSS (web) outputs from a single source; all three clients consume colours, type, spacing and radius only from these generated outputs. (Source: DESIGN.md › Colors, front matter `colors`; EXPERIENCE.md › Responsive & Platform (Web); Platforms: iOS/Android/web)
UX-DR2: Define the neutral and brand semantic token set with light (unsuffixed) and dark (`-dark`) values exactly as specified: `background`, `layer-01`, `layer-02`, `field-01`, `text-primary/secondary/helper/disabled/on-color`, `border-subtle/strong`, `overlay`, `primary`, `primary-hover`, `primary-active`, `secondary`, `button-secondary`, `header-bg`, `header-border`, `support-error/success/warning/info`, `setup-done`; tokens without a `-dark` twin are identical in both themes. (Source: DESIGN.md › front matter `colors`, Colors › Neutrals; Platforms: iOS/Android/web)
UX-DR3: Add the Coldframe-specific accessibility tokens and local override: `primary-text` (#B84A00 / #FF8A2E) for orange text, lines and selection marks; `support-error-text`; `ink-on-bright` (#0A0A0A) as the label colour on every orange/green/selected-orange surface including the primary Button, overriding the DS white `text-on-color` locally in `packages/design-tokens` until the upstream `escendit/branding` fix lands (document the deviation). (Source: DESIGN.md › Colors (Orange as text, Ink on bright), Do's and Don'ts; Platforms: iOS/Android/web)
UX-DR4: Add the Lot-status token set for both themes: `status-water-fill/ink/level`, `status-ok-fill/level/border`, `status-level-edge`, `status-low-marker`, `status-hatch-ground/line`, `status-unknown-border`, `status-calibration-border/ink`, `status-paused-fill/border/ink`, `status-no-node-border/ink`; replace Direction B hard-coded values (#0A0A0A page, #1A1A1A hatch, #161616 paused fill, #1A2A12 chart band) with DS tokens, shifting the dark Lot-status set one Carbon layer up. (Source: DESIGN.md › front matter `colors` (Lot status), Colors intro; Platforms: iOS/Android/web)
UX-DR5: Add stale-data tokens (`stale-border`, `stale-ink`) and History chart tokens (`chart-bar`, `chart-bar-below-low`, `chart-band`, `chart-high-line`) in light and dark. (Source: DESIGN.md › front matter `colors` (Transport staleness, History chart), Colors › Chart / Yellow; Platforms: iOS/Android/web)
UX-DR6: Add two-tone focus tokens `focus` (#161616 / #F4F4F4) and `focus-gap` (#FEFEFE / #262626) plus `spacing.focus-ring` (2px) and `spacing.focus-offset` (2px); focus must never be orange. (Source: DESIGN.md › Colors (Focus), front matter `spacing`, Components › Focus indicator; Platforms: iOS/Android/web)
UX-DR7: Implement contrast enforcement in `packages/design-tokens` CI: regenerate the load-bearing WCAG 2.2 contrast table from the tokens (all pairs in DESIGN.md › Colors table, both themes) and fail the build if any tile text token is below 4.5:1 against any surface it can overlap, or any listed non-text pair drops below 3:1. (Source: DESIGN.md › Colors (Load-bearing contrast); Platforms: iOS/Android/web)
UX-DR8: Define typography tokens for roles `headline`, `title`, `section`, `hero-value`, `tile-value`, `tile-value-web`, `tile-name`, `status-label`, `body`, `body-lg`, `helper`, `meta-mono`, `button`, `step-counter` (family, size, weight 300/400 only, line height, letter spacing); bundle Ubuntu, Ubuntu Condensed, Ubuntu Mono in the apps and self-host them on web (no Google Fonts fetch); no serif. (Source: DESIGN.md › front matter `typography`, Typography; Platforms: iOS/Android/web)
UX-DR9: Map each type role to platform scaling: iOS Dynamic Type relative to the noted text style, Android `sp` font scale, web `rem`; cap sizes >= 36px at 2x; uppercase for `status-label` and `button` applied via style, never in strings. (Source: DESIGN.md › Typography; EXPERIENCE.md › Accessibility Floor; Platforms: iOS/Android/web)
UX-DR10: Define spacing and layout tokens: DS/Carbon scale `spacing.1`-`spacing.13`, `gutter-mobile` 16px, `gutter-web` 32px, `tile-gap` 8px, `tile-padding` 12px / `tile-padding-web` 16px, `alerts-rail-web` 340px, `field-height` 40px, `button-height` 48px, `header-height` 48px, `one-column-web` 400px. (Source: DESIGN.md › front matter `spacing`, Layout & Spacing; Platforms: iOS/Android/web)
UX-DR11: Enforce shape and elevation rules via tokens and shared styles: `rounded.none` (0px) for tiles, cards, fields, buttons, sheets, dialogs and web shell on all platforms (Android 12px radius superseded; OS-drawn chrome keeps platform shape); no shadows, blur, transparency or gradients; modal scrims use `overlay`; the DS signature radial gradient only on the Sign-in surface. (Source: DESIGN.md › Shapes, Elevation & Depth; Platforms: iOS/Android/web)
UX-DR12: Implement a reusable hatch fill primitive (135°, 1.5px `status-hatch-line` lines every 8px on `status-hatch-ground`) with an optional solid `status-hatch-ground` text plate, used by unknown/needs-calibration tiles, Health Alert rows, unsupported Wi-Fi rows and the Notification Window control outside-window area. (Source: DESIGN.md › front matter components `lot-tile-unknown`, `alert-row-health`, `wifi-network-row`, `notification-window-control`, Layout & Spacing (Tile anatomy); Platforms: iOS/Android/web)
UX-DR13: Vendor a Carbon icon subset (`@carbon/icons`, fill `currentColor`) with the tokens, covering at least: rain-drop, checkmark--outline, checkmark, help, tools, pause--outline, add, cloud--offline, overflow-menu--vertical, chevron--down, arrow--up, arrow--down, battery--low, error--filled, view, in-progress, time, grid, notification, box, settings; no emoji, unicode glyph icons or hand-drawn SVGs. (Source: DESIGN.md › Components (Icons), Shapes table; Platforms: iOS/Android/web)
UX-DR14: Implement colour-semantics guardrails as review/test criteria: solid orange only for needs water (needs-water tile, open low-side soil-moisture Threshold Alert row, needs-water Lot detail hero, below-low chart bars) plus interactive fills (primary button, selected segment, first-run next step, Notification Window bar); purple only for paused; yellow only for needs-calibration border and stale age/timestamps; green only for finished setup steps and "Hub is online"; red only for setup/validation failures with `error--filled`; OK is neutral (no green). (Source: DESIGN.md › Colors, Do's and Don'ts; EXPERIENCE.md › Inspiration & Anti-patterns; Platforms: iOS/Android/web)
UX-DR15: Implement theme switching: default System follows OS appearance (iOS/Android system appearance; web `prefers-color-scheme` mapped to the DS `data-theme` attribute because `theme.css` keys dark mode off `data-theme`); Settings › Appearance override System / Light / Dark applies instantly without restart and is persisted per device (not synced across devices). (Source: EXPERIENCE.md › Responsive & Platform (Theme), Component Patterns › Theme switcher; DESIGN.md › description; Platforms: iOS/Android/web)

#### Components

UX-DR16: Build the Focus indicator: 2px `focus` ring with 2px `focus-gap` gap drawn outside the component, inset (ring outside, gap inside over the fill) where neighbours abut (segments, list rows); applied to every focusable element on web and on iPadOS/Android with hardware keyboard or switch control; never the only change on focus-visible; never hidden under sticky AppHeader or tab bar (web `scroll-padding-top` = `header-height`). (Source: DESIGN.md › Components › Focus indicator; EXPERIENCE.md › Component Patterns › Focus indicator; Platforms: iOS/Android/web)
UX-DR17: Build the Lot tile base anatomy: at least square (web at least 1:0.82), grows in height to fit content; inset `tile-padding` / `tile-padding-web`; top-left Lot name (`tile-name`) over status icon + `status-label`; bottom-left big value (`tile-value` / `tile-value-web`) over `meta-mono` foot line; soil-level fill with 2px level edge rising to the Reading %; 12px tick on the right edge at the low Threshold height; hatched tiles place text on a solid plate; in one-column layout the value sits directly under the status label. (Source: DESIGN.md › Layout & Spacing (Tile anatomy), Components › Lot tile; Platforms: iOS/Android/web)
UX-DR18: Implement the six Lot tile status variants with exact tokens, shape, icon, value and foot: needsWater (solid `status-water-fill`, no border, `rain-drop`, `~20`, "07:02 · low 30 %"); ok (soil fill, 1px solid border, `checkmark--outline`, `~35`); unknown (hatch, 1px dashed, `help`, silence duration `6 h`, foot "was ~40 % at 01:05", label "SILENT · UNKNOWN" / "HUB SILENT · UNKNOWN" from `unknownCause`); needsCalibration (hatch, 2px dashed `status-calibration-border`, `tools`, `raw`, "no % until calibrated"); paused (flat `status-paused-fill`, 2px solid purple, `pause--outline`, `—`, "until 1 Nov"/"paused", label "PAUSED" / "PAUSED BY SITE" from `pausedBy`); noNode (transparent, 1px dotted, `add`, `+`, "add a Node"); no secondary conditions appended to labels. (Source: DESIGN.md › Components › Lot tile, Shapes table, front matter `lot-tile-*`; EXPERIENCE.md › State Patterns › Lot status; Platforms: iOS/Android/web)
UX-DR19: Implement the Lot tile stale variant and skeleton: stale removes all fills, hatches and colour, 1px solid `stale-border`, `cloud--offline` icon, label "WAS <STATUS>" (e.g. "WAS NEEDS WATER"), foot "as of 07:02" in `stale-ink`; skeleton (cold start without cache) is `border-subtle` outline only with no icon, label or value and is not focusable. (Source: DESIGN.md › Components › Lot tile, front matter `lot-tile-stale`, `lot-tile-skeleton`; EXPERIENCE.md › Component Patterns › Lot tile; Platforms: iOS/Android/web)
UX-DR20: Implement Lot tile behaviour: render in Server sort order (needsWater, needsCalibration, unknown, ok, paused, noNode) without client re-sort or status recomputation; tap → Lot detail; tap on noNode tile → Add a Node (mobile, Admin+) or Lot detail with "Add a Node from the mobile app" (web / Member); one merged accessibility element per tile with role button/link; refetch on `readmodel.changed` without reflow animation. (Source: EXPERIENCE.md › Component Patterns › Lot tile; Platforms: iOS/Android/web)
UX-DR21: Build the Site summary header: Site name (tappable on mobile with `chevron--down` → Site switcher), Site clock in `meta-mono` (time of newest Reading), Site menu trigger, `headline` sentence exposed as a heading, and counts subline ("2 unknown · 2 OK · 1 paused · 1 without Node"); paused Site headline in `status-paused-ink` ("Paused until 1 Mar"). (Source: DESIGN.md › Components › Site summary header; EXPERIENCE.md › Component Patterns › Site summary header, Voice and Tone (Headline sentence); Platforms: iOS/Android/web)
UX-DR22: Build the Site menu: `overflow-menu--vertical` trigger at the right of the Site summary header (mobile) or on the current Site tab (web); native menu (iOS) / Material dropdown (Android) / DS overflow menu (web); items in `body-lg`: "Pause <Site>" / "Resume <Site>" (Admin+, hidden for Members) and "Site settings"; Pause opens the Pause sheet with scope Whole Site preselected; disabled with "Needs your Server" in stale mode. (Source: DESIGN.md › Components › Site menu; EXPERIENCE.md › Component Patterns › Site menu; Platforms: iOS/Android/web)
UX-DR23: Build the Site switcher: mobile native sheet listing every Site with a Membership (name + Role in `status-label`), current Site marked with `checkmark` in `primary-text` and exposed as selected, "New Site" last (shown even with one Site) → Create Site; web Site tabs in the AppHeader with current tab underlined in `primary` on `header-bg` and "New Site" as last tab; picking a Site switches the whole app. (Source: DESIGN.md › Components › Site switcher; EXPERIENCE.md › Component Patterns › Site switcher; Platforms: iOS/Android/web)
UX-DR24: Build the Stale header replacing the summary header on Site overview, Lot detail and Alerts in stale mode: `cloud--offline` icon, "<Site> · can't reach your Server", age in `headline` + `stale-ink` ("2 h 12 min old") ticking every minute (never announced), and one explanatory line ("Last data 07:02. You may be away from home, or the Server is down. Nothing below is live."). (Source: DESIGN.md › Components › Stale header; EXPERIENCE.md › Component Patterns › Stale header, Voice and Tone; Platforms: iOS/Android/web)
UX-DR25: Build the Alert row in four variants: needs water (open low-side soil-moisture Threshold Alert only; solid orange, `rain-drop`, eyebrow "NEEDS WATER" + time); other Threshold (`layer-01`, 2px solid `border-strong`, `arrow--up`/`arrow--down`, eyebrow "THRESHOLD ALERT · ABOVE HIGH"/"· BELOW LOW"); Health (hatch + 1px dashed on text plate, icon by cause `help`/`battery--low`/`tools`, eyebrow "HEALTH ALERT"); Closed (outline only, `text-secondary`, eyebrow "… · CLOSED 06:40"); title in `section`. (Source: DESIGN.md › Components › Alert row, front matter `alert-row-*`; Platforms: iOS/Android/web)
UX-DR26: Implement Alert row behaviour: open Alerts newest first within Threshold then Health groups; closed Alerts from the last 7 days in a "Closed" section; tap → Lot detail (Threshold, Uncalibrated) or the Devices list row (Silent, Battery); no actions on Alerts (no mark-watered, no snooze). (Source: EXPERIENCE.md › Component Patterns › Alert row; Platforms: iOS/Android/web)
UX-DR27: Build the Lot detail hero: Lot name (`title`), status icon + status label with "since <statusSince>", `hero-value` with unit, low Threshold and "±5 % · <Reading time>" right-aligned; orange background only for needsWater, other statuses use the matching tile treatment; paused by Site shows "Paused with the Site" and, for Admin+, "Resume the Site to resume this Node". (Source: DESIGN.md › Components › Lot detail hero; EXPERIENCE.md › Component Patterns › Lot detail hero; Platforms: iOS/Android/web)
UX-DR28: Build the Sensor cell: `layer-01` block with 1px `border-subtle`, `helper` label over `title` value with unit (Temperature `14 °C`, Humidity `78 %`, Air (gas) `142 kΩ`); watched Sensors without Thresholds show value only; tap → Thresholds for that Sensor (Admin+). (Source: DESIGN.md › Components › Sensor cell; EXPERIENCE.md › Component Patterns › Sensor cell; Platforms: iOS/Android/web)
UX-DR29: Build the Device cell: `layer-01` block, `helper` label over `title` value with `helper` meta (Node battery `62 %` "charging · solar"; Last seen `07:02` "every 15 min"); battery below 20 % shows `battery--low`; tap → the Devices list row. (Source: DESIGN.md › Components › Device cell; EXPERIENCE.md › Component Patterns › Device cell; Platforms: iOS/Android/web)
UX-DR30: Build the Devices list: sections "Hubs" then "Nodes" (Nodes by Lot name); full-width rows on `background` with 1px `border-subtle` dividers; left Device ID (`meta-mono`) + Lot name (`body-lg`), right last seen, battery % and charging (Nodes) in `helper`; silent rows add `help` + "SILENT 6 H"; paused rows add `pause--outline` + "PAUSED"/"PAUSED BY SITE" in `status-paused-ink`; header ghost buttons Add a Hub / Add a Node (mobile only); empty state "No Devices yet." plus Add actions (Admin+). (Source: DESIGN.md › Components › Devices list; EXPERIENCE.md › Component Patterns › Devices list; Platforms: iOS/Android/web)
UX-DR31: Implement Devices list row actions (Admin+): Pause/Resume, Silence Window, move to another Lot, unassign; Resume on a Device paused by the Site shows "Resume the Site to resume this Node" instead of resuming; swipe on a row reveals Pause (mobile). (Source: EXPERIENCE.md › Component Patterns › Devices list, Interaction Primitives; Platforms: iOS/Android/web)
UX-DR32: Build the History chart: 30 bars, one per day of the soil-moisture daily low; normal days 1px outlined `chart-bar` bars, below-low days solid `chart-bar-below-low`; low line 2px `primary-text`, high line 1px dashed `chart-high-line`, decorative `chart-band` between; axis dates in `meta-mono`; legend "solid bar = below 30 %"; days without Readings are gaps, never zero. (Source: DESIGN.md › Components › History chart; EXPERIENCE.md › Component Patterns › History chart; Platforms: iOS/Android/web)
UX-DR33: Implement History chart interactions: tap/drag a bar shows that day's low and date; Sensor picker switches to temperature, humidity or air (gas) history (daily min/max); text summary as the chart's accessibility label ("30 days, lowest about 20 percent today, below the low Threshold on 2 days"); no animation under Reduce Motion. (Source: EXPERIENCE.md › Component Patterns › History chart, Accessibility Floor; Platforms: iOS/Android/web)
UX-DR34: Build the Button (DS Button): primary (`primary` fill, `ink-on-bright` label, hover `primary-hover`, active `primary-active`), secondary (`button-secondary`, `text-on-color`), ghost (`primary-text` label); square, `button-height`, uppercase `button` label; while working shows an in-place progress label ("Saving…"), never a spinner over content; label is a verb naming the result; grows in height with text, never clips. Admin action strip on Lot detail uses secondary buttons. (Source: DESIGN.md › Components › Button; EXPERIENCE.md › Component Patterns › Button, State Patterns › Loading (action); Platforms: iOS/Android/web)
UX-DR35: Build the Text input (DS TextInput): `field-01` fill, 1px `border-strong` bottom border, `field-height`, label above, helper below, password reveal with Carbon `view` icon; invalid state = 2px `support-error` border + `error--filled` icon + reason in `support-error-text` replacing the helper and linked to the field for screen readers. (Source: DESIGN.md › Components › Text input; EXPERIENCE.md › Component Patterns › Text input; Platforms: iOS/Android/web)
UX-DR36: Build the Segmented choice: equal-width square segments on `layer-01`; selected = `primary` fill, `ink-on-bright` label, leading `checkmark`, exposed as selected; single select; applies immediately in settings, committed by the primary button inside modal flows; wraps to two rows at large text. Used by Reminder cadence, Theme switcher, Invite form Role and Pause scope. (Source: DESIGN.md › Components › Segmented choice; EXPERIENCE.md › Component Patterns › Segmented choice; Platforms: iOS/Android/web)
UX-DR37: Build the Device candidate tile (`selectable-tile` tokens): Device ID (`meta-mono`) with signal (Hub) or battery %, Sensor count and signal (Node); "PRESSED JUST NOW" text badge on the most recent setup-mode Node; selected = 2px `primary-text` border + `checkmark`; only in-range Devices, strongest first; list keeps scanning ("Still scanning…"); no "already set up" tile (AD-25). (Source: DESIGN.md › Components › Device candidate tile; EXPERIENCE.md › Component Patterns › Device candidate tile; Platforms: iOS/Android)
UX-DR38: Build the Lot picker (`selectable-tile` tokens): one tile per Lot; Lots without a Node selectable with the Device candidate selected state; Lots with a Node disabled with dimmed name and reason "HAS A NODE"; "+ New Lot" dotted tile creates a Lot inline; primary button names the result ("Put 7C19 in Potatoes"). (Source: DESIGN.md › Components › Lot picker; EXPERIENCE.md › Component Patterns › Lot picker; Platforms: iOS/Android)
UX-DR39: Build the Setup flow shell: `step-counter` "NN / TT" top-left (current in `primary-text`, total in `text-helper`) — Add a Hub "NN / 05", Add a Node "NN / 05", Calibrate "NN / 02"; step title in `headline`; Cancel on step 1, Back afterwards; leaving mid-flow confirms ("Stop setting up Hub 3F2A? Nothing is saved on the Hub."); accessibility focus moves to the step title on step change; keeps the screen awake during BLE steps; primary action directly below content, not pinned. (Source: DESIGN.md › Components › Setup flow shell, Layout & Spacing (Setup flows); EXPERIENCE.md › Component Patterns › Setup flow shell; Platforms: iOS/Android/web (Calibrate only on web))
UX-DR40: Build the Setup progress: four labelled segments BLUETOOTH · WI-FI SENT · JOINING… · SERVER advancing only on real events; done = `setup-done` fill + `checkmark`; active = `primary` fill + `in-progress` icon (static under Reduce Motion); pending = 1px `border-strong` outline; exposed as one progress element ("Step 3 of 4, joining Wi-Fi"); shows elapsed time and "Usually under a minute."; times out at 90 s into an error screen. (Source: DESIGN.md › Components › Setup progress; EXPERIENCE.md › Component Patterns › Setup progress; Platforms: iOS/Android)
UX-DR41: Build the Setup code field: Text input in `meta-mono`, format-agnostic, auto-uppercase, no autocorrect; validated by opening the BLE session (AD-25); on success an "ACCEPTED" chip (`layer-01`, `checkmark`) at the field's right. (Source: DESIGN.md › Components › Setup code field; EXPERIENCE.md › Component Patterns › Setup code field; Platforms: iOS/Android)
UX-DR42: Build the Wi-Fi network row: networks as seen by the Hub; SSID left, security right (`meta-mono`); selected = 2px `primary-text` border + `checkmark`; WPA3-only rows hatched, not selectable, with inline reason "Not supported: the Hub needs WPA2 or mixed WPA2/WPA3."; "Other network" entry for hidden SSIDs; password field with reveal. (Source: DESIGN.md › Components › Wi-Fi network row; EXPERIENCE.md › Component Patterns › Wi-Fi network row, State Patterns › BLE setup errors; Platforms: iOS/Android)
UX-DR43: Build the Calibration reference point (stored Readings, AD-9, no live BLE value): waiting state = `layer-01` panel with `time` icon, "Waiting for the next Reading", last raw value in `hero-value` with Reading time in `meta-mono`, hint "Short-press the Node's setup button to report now"; fresh state = `checkmark` + "New Reading 07:17", raw value, and enabled "Record dry"/"Record wet" primary button; "Recent Readings" list (time + raw value) selectable with `checkmark` as an alternative. (Source: DESIGN.md › Components › Calibration reference point; EXPERIENCE.md › Component Patterns › Calibration reference point; Platforms: iOS/Android/web)
UX-DR44: Implement Calibration reference point behaviour: each step waits for a Reading taken after the step started; flow can be left and resumed with chosen points kept until both exist; confirmation shows dry and wet raw values side by side and "% appears with the next Reading", updating in place to "Tomatoes reads ~40 %" when the first calibrated Reading is stored; no % shown before the Server stores a calibrated Reading. (Source: EXPERIENCE.md › Component Patterns › Calibration reference point; DESIGN.md › Components › Calibration reference point; Platforms: iOS/Android/web)
UX-DR45: Build the Threshold column: vertical soil column on `layer-01` track with `status-water-level` soil, 2px `primary-text` low line, current Reading marker, dashed "no high" marker, values listed to the right; drag or type; 5 % steps for calibrated soil moisture; low required, high optional ("Add high" / clear); inline validation "Low must stay below high." with Save disabled while invalid; proposes low = Min + 20 % × range when enabling alerts on a Sensor without a default (CAP-10). (Source: DESIGN.md › Components › Threshold column; EXPERIENCE.md › Component Patterns › Threshold column; Platforms: iOS/Android/web)
UX-DR46: Build the Pause sheet: native sheet (iOS) / ModalBottomSheet (Android) / DS Modal (web) on `overlay` scrim; Pause scope Segmented choice (This Device / Whole Site); optional "Until" date via native picker / DS DatePicker with helper "Leave empty to pause until someone resumes."; primary button naming the result ("Pause Home garden"). (Source: DESIGN.md › Components › Pause sheet; EXPERIENCE.md › Component Patterns › Pause sheet; Platforms: iOS/Android/web)
UX-DR47: Build the Notification Window control: from/to time pickers ("from 07:00" alone is valid, defaulting the end to 22:00); decorative 24 h bar (`primary` inside the window, hatched outside, hidden from screen readers) and big "07:00 to 22:00"; helper "Outside this window, anything waits for one summary at 07:00." (Source: DESIGN.md › Components › Notification Window control; EXPERIENCE.md › Component Patterns › Notification Window control; Platforms: iOS/Android/web)
UX-DR48: Build the Time-zone confirm panel: 1px dashed `support-warning` box "Is your time zone Europe/Zurich?" proposing the OS/browser-detected zone with Confirm / Change (searchable IANA list); never overwrites a zone the user picked; used on Create Site and My notifications. (Source: DESIGN.md › Components › Time-zone confirm panel; EXPERIENCE.md › Component Patterns › Time-zone confirm panel; Platforms: iOS/Android/web)
UX-DR49: Build the Mute toggle: native switch (iOS Toggle / Material Switch / DS Toggle) labelled "Mute <Site>" affecting only the current user. (Source: DESIGN.md › Components › Mute toggle; EXPERIENCE.md › Component Patterns › Mute toggle; Platforms: iOS/Android/web)
UX-DR50: Build the Reminder cadence control: Segmented choice with helper line; personal (My notifications) options "Use Site setting" / "Daily" / "Every 2 days"; Site (Site settings) options "Daily" / "Every 2 days" (default Daily); no "Never" option. (Source: DESIGN.md › Components › Reminder cadence; EXPERIENCE.md › Component Patterns › Reminder cadence; Platforms: iOS/Android/web)
UX-DR51: Build the Member tile: initials square (current user: `primary` fill with `ink-on-bright`; others `layer-01` with `text-primary`), name, Role line ("OWNER · LAST ONE" / "MEMBER · CHANGE"); one tile per Membership, no pending-invitation tiles (AD-3); tap → Role change / remove (Owner only); last Owner locked with explanation "A Site always keeps at least one Owner." (Source: DESIGN.md › Components › Member tile; EXPERIENCE.md › Component Patterns › Member tile; Platforms: iOS/Android/web)
UX-DR52: Build the Invite form: email Text input, Role Segmented choice with one-line Role description updating on selection (Member: "Member sees everything and gets the same Alerts, but can't change settings."), primary "Send invite"; on success the form clears and an Inline notice reads "Invitation sent to <email>. They accept from the email, on the garden's Wi-Fi."; Members list unchanged until acceptance. (Source: DESIGN.md › Components › Invite form; EXPERIENCE.md › Component Patterns › Invite form; Platforms: iOS/Android/web)
UX-DR53: Build the Theme switcher: Segmented choice System (default) / Light / Dark on mobile, DS ThemeSwitcher on web, wired to UX-DR15. (Source: DESIGN.md › Components › Theme switcher; EXPERIENCE.md › Component Patterns › Theme switcher; Platforms: iOS/Android/web)
UX-DR54: Build the First-run step tiles: 2×2 tiles STEP 1-4 (Add a Hub · Add a Node · Calibrate · Set a low Threshold); next step solid `primary` with `ink-on-bright`, later steps 1px dashed `border-strong`, done steps with `checkmark` persisting until the Lot grid replaces the tiles; shown while the Site has no Node with Readings; next-step tap starts the flow (mobile, Admin+); web and Members see tiles without actions plus the BLE or read-only Inline notice. (Source: DESIGN.md › Components › First-run step tiles; EXPERIENCE.md › Component Patterns › First-run step tiles; Platforms: iOS/Android/web)
UX-DR55: Build the Outcome screens: full screen, accessibility focus moves to the headline, one primary next action; success = full-bleed `support-success` with `ink-on-bright` ("Hub is online"); error = `background`, `error--filled` + eyebrow in `support-error-text` ("STEP 5 STOPPED"), plain headline ("Wrong Wi-Fi password"); errors never auto-dismiss; paused-Site Inline notice on success screens when applicable. (Source: DESIGN.md › Components › Outcome screens; EXPERIENCE.md › Component Patterns › Outcome screens; Platforms: iOS/Android/web)
UX-DR56: Build the Inline notice (DS InlineNotification info/warning style): `layer-01` background, 3px `border-strong` left border, `body` text; non-dismissable; may carry one action (Open Settings, Resume Site, Try again, Sign in); used for read-only explanations, Hub silent, web BLE, notifications off, paused-Site Device notes and sign-in errors. (Source: DESIGN.md › Components › Inline notice; EXPERIENCE.md › Component Patterns › Inline notice; Platforms: iOS/Android/web)
UX-DR57: Build mobile Navigation: native tab bar (iOS) / Material 3 NavigationBar (Android) with tabs Garden · Alerts · Devices · Settings and Carbon icons `grid`, `notification`, `box`, `settings`; selected tab = `primary-text` label and icon plus a non-colour cue (filled icon on iOS, M3 indicator pill on Android) exposed as selected; Alerts label carries the open count ("Alerts · 5") announced as "Alerts, 5 open". (Source: DESIGN.md › Components › Navigation; EXPERIENCE.md › Component Patterns › Navigation, Information Architecture (Navigation); Platforms: iOS/Android)
UX-DR58: Build web Navigation: DS AppShell + AppHeader (`header-bg`, 1px `header-border`, `header-height`) with Coldframe mark, Site tabs and user initials; side nav Garden · Alerts · Devices · Members with Settings in the nav footer; current item with 3px left bar and `aria-current`; Alerts carries the open count. (Source: DESIGN.md › Components › Navigation, front matter `app-header-web`; EXPERIENCE.md › Component Patterns › Navigation; Platforms: web)
UX-DR59: Build the Sign-in surface: DS signature radial background (`primary` at 5% 5% → `secondary`), `background` card with Coldframe mark and SIGN IN button only (no Server field; Server URL and Keycloak issuer are build-time configuration in mobile apps, web is served by the Server); no text directly on the gradient; errors as an Inline notice inside the card. (Source: DESIGN.md › Components › Sign-in surface; EXPERIENCE.md › Component Patterns › Sign-in surface; Platforms: iOS/Android/web)

#### Surfaces

UX-DR60: Implement the Sign in surface flow: SIGN IN checks the Server is reachable, then hands off to Keycloak in the system browser session and returns; cold start while signed out lands here. (Source: EXPERIENCE.md › Information Architecture (Sign in), Component Patterns › Sign-in surface, Key Flows › UJ-1; Platforms: iOS/Android/web)
UX-DR61: Implement Create Site: reached on first sign-in with no Membership or via Site switcher "New Site"; Site name field + Time-zone confirm panel; creator becomes Owner; lands on the new Site overview. (Source: EXPERIENCE.md › Information Architecture (Create Site), Key Flows › UJ-1; Platforms: iOS/Android/web)
UX-DR62: Implement Site overview (Garden): Site summary header (or Stale header), optional Inline notices (Hub silent, notifications off) above the tiles, and the Lot grid in Server sort order; empty Site shows "No Readings yet" + First-run step tiles; pull-to-refresh (mobile) / refetch on focus (web). (Source: EXPERIENCE.md › Information Architecture (Site overview), State Patterns; DESIGN.md › Layout & Spacing (Lot grid); Platforms: iOS/Android/web)
UX-DR63: Implement Lot detail: full-width Lot detail hero, 3-up Sensor cell row, 30-day History chart, 2-up Device cell row, then admin action strip (secondary buttons: Thresholds, Calibrate, Pause/Resume; Admin+); rows wrap to 1-up in one-column layout; noNode Lot shows empty detail with Add a Node (mobile, Admin+); reached from Lot tile and Threshold/Uncalibrated push. (Source: DESIGN.md › Layout & Spacing (Lot detail); EXPERIENCE.md › Information Architecture (Lot detail), State Patterns › Lot status; Platforms: iOS/Android/web)
UX-DR64: Implement Alerts surface: open Alerts (Threshold group, then Health group, newest first) and a "Closed" section (last 7 days); empty "No open Alerts." then Closed if any; pull-to-refresh; reached from tab/nav, Health push and the web Open Alerts rail. (Source: EXPERIENCE.md › Information Architecture (Alerts), State Patterns › No open Alerts; Platforms: iOS/Android/web)
UX-DR65: Implement Devices surface: Devices list with Hubs/Nodes, per-row last seen, battery, charging, Silence Window, Pause, move/unassign Node; entry to Add a Hub / Add a Node (mobile); pull-to-refresh; reachable as deep-link target for a specific Device row (Silent/Battery Alerts, push, Device cell). (Source: EXPERIENCE.md › Information Architecture (Devices), Component Patterns › Devices list; Platforms: iOS/Android/web)
UX-DR66: Implement the Add a Hub 5-step full-screen modal BLE flow: 1 scan & select Hub (Device candidate tiles), 2 setup code, 3 Wi-Fi network + password, 4 assign Site, 5 Setup progress → "Hub is online" outcome with ADD A NODE primary action; reached from First-run step 1 and Devices "Add a Hub". (Source: EXPERIENCE.md › Information Architecture (Add a Hub), Key Flows › UJ-1 steps 3-4; Platforms: iOS/Android)
UX-DR67: Implement the Add a Node 5-step full-screen modal BLE flow: select Node (PRESSED JUST NOW), setup code, Lot picker (with "+ New Lot"), confirm "Put <ID> in <Lot>", "<Lot> has a Node" outcome showing Sensors and a CALIBRATE SOIL MOISTURE action when needs calibration; reached from First-run step 2, no-Node tile, Devices "Add a Node" and "Hub is online". (Source: EXPERIENCE.md › Information Architecture (Add a Node), Key Flows › UJ-1 steps 5-6; Platforms: iOS/Android)
UX-DR68: Implement Calibrate as a 2-step full-screen modal flow (dry, wet) using stored Readings on all platforms, ending in the confirmation/outcome; reached from needs-calibration tile, Lot detail, Node-added screen and Uncalibrated push. (Source: EXPERIENCE.md › Information Architecture (Calibrate), Key Flows › UJ-1 steps 6-9; Platforms: iOS/Android/web)
UX-DR69: Implement Thresholds as a modal screen with Cancel/Save hosting the Threshold column per Sensor (low required, high optional); reached from Lot detail, Sensor cell and after Calibration. (Source: EXPERIENCE.md › Information Architecture (Thresholds, Navigation); Platforms: iOS/Android/web)
UX-DR70: Implement Pause/Resume for a Device or the Site via the Pause sheet, reachable from Lot detail "Pause" (Device), Devices row and overview Site menu (Site); Resume is one tap from the paused tile's detail, Devices row or Site menu; resuming restarts Silence Windows from that moment. (Source: EXPERIENCE.md › Information Architecture (Pause), Component Patterns › Pause sheet, Key Flows › UJ-4; Platforms: iOS/Android/web)
UX-DR71: Implement Settings index (tab 4 / web nav footer) listing My notifications, Members, Site settings, Appearance, Account. (Source: EXPERIENCE.md › Information Architecture (Settings); Platforms: iOS/Android/web)
UX-DR72: Implement My notifications: Notification Window control, Time-zone confirm panel, Mute toggle, personal Reminder cadence, notifications-off Inline notice (mobile) and, on web, the "Browser notifications while Coldframe is open" toggle. (Source: EXPERIENCE.md › Information Architecture (My notifications), Notifications; Platforms: iOS/Android/web)
UX-DR73: Implement Members: Member tiles and Invite form; Role change and removal (Owner only) with destructive confirmation naming the Membership; no pending-invitation list and no in-app accept surface (acceptance happens on the Keycloak/Phase Two page, AD-3). (Source: EXPERIENCE.md › Information Architecture (Members, Invitations); Platforms: iOS/Android/web)
UX-DR74: Implement Site settings as one surface: rename Site (Owner), Site Reminder cadence (Owner/Admin), Lots create/rename/remove (Admin+) with remove confirmation and validation (e.g. "Move or unassign the Node on Potatoes first."). (Source: EXPERIENCE.md › Information Architecture (Site settings), Key Flows › UJ-6; Platforms: iOS/Android/web)
UX-DR75: Implement Appearance surface hosting the Theme switcher. (Source: EXPERIENCE.md › Information Architecture (Appearance); Platforms: iOS/Android/web)
UX-DR76: Implement app-shell modal and role-gating rules: setup flows are full-screen modals with Cancel/Back; Thresholds and Pause are modal with Cancel/Save; only one modal level at a time; admin actions (Thresholds, Calibrate, Pause, Add/move Devices, Silence Window, Lots) visible only to Administrator and Owner; Members management and Site rename Owner only; Members see all read surfaces. (Source: EXPERIENCE.md › Information Architecture (Navigation, Role gating); Platforms: iOS/Android/web)

#### States

UX-DR77: Implement the Lot status contract for all six Server-computed statuses (needsWater, needsCalibration, unknown, ok, paused, noNode) across tile, Lot detail hero and screen-reader label exactly per the State Patterns table, using only Server fields (`status`, sort order, `statusSince`, `unknownCause`, `pausedBy`); client never computes status. (Source: EXPERIENCE.md › State Patterns › Lot status, Foundation; Platforms: iOS/Android/web)
UX-DR78: Implement Lot detail status-specific content: needsCalibration hero with Calibrate primary (Admin+); unknown hero with duration + last Reading and "Check power or range." (node) or "Hub 3F2A is silent; Lots behind it can't be read." (hub); paused hero "PAUSED UNTIL 1 NOV" with Resume (Admin+), paused by Site "Paused with the Site" pointing to the Site menu. (Source: EXPERIENCE.md › State Patterns › Lot status; Platforms: iOS/Android/web)
UX-DR79: Implement client-side stale mode: enter after the first failed refresh plus one retry; show Stale header, render every tile as `lot-tile-stale`, disable admin actions with "Needs your Server"; retry in background; leave stale mode on the first successful refresh; applies to Site overview, Lot detail, Alerts, Devices, and when opened from push away from home. (Source: EXPERIENCE.md › State Patterns › Transport and data states (Server unreachable, Away from home); DESIGN.md › Components › Stale header; Platforms: iOS/Android/web)
UX-DR80: Implement cold start: show last cached data in stale mode until the first refresh lands; with no cache show outline-only skeleton tiles with no status or value and "Loading <Site>". (Source: EXPERIENCE.md › State Patterns › Cold start; Platforms: iOS/Android/web)
UX-DR81: Implement the Hub silent state: Inline notice above tiles "Hub 3F2A silent for 12 min — Lots behind it can't be read."; affected Lots render as unknown (HUB SILENT) from the Server. (Source: EXPERIENCE.md › State Patterns › Hub silent; Platforms: iOS/Android/web)
UX-DR82: Implement empty states: empty Site "No Readings yet" + First-run step tiles; no open Alerts "No open Alerts." (never "All good"); Devices "No Devices yet." with Add actions (Admin+). (Source: EXPERIENCE.md › State Patterns › Empty Site, No open Alerts; Component Patterns › Devices list; Platforms: iOS/Android/web)
UX-DR83: Implement the All paused (Site paused) state: headline "Paused until 1 Mar" / "Paused" in paused ink, line "No Alerts are sent while the Site is paused. Paused by <name> (<Role>) on <date>.", all tiles paused ("PAUSED BY SITE"). (Source: EXPERIENCE.md › State Patterns › All paused; Platforms: iOS/Android/web)
UX-DR84: Implement Member read-only state: admin controls hidden (not disabled); where a state can't be changed, one Inline notice "Only Owners and Administrators can resume it."; a 403 race shows "You can't change this on <Site>. Ask an Owner or Administrator." (Source: EXPERIENCE.md › State Patterns › Member read-only; Platforms: iOS/Android/web)
UX-DR85: Implement web BLE-action state: on no-Node tile detail and Devices, Inline notice "Adding a Hub or Node needs the Coldframe mobile app."; no BLE surfaces on web. (Source: EXPERIENCE.md › State Patterns › Web, BLE action; Platforms: web)
UX-DR86: Implement Device added to a paused Site: new Device starts paused (`pausedBy` site); Hub-online and Node-added outcome screens add Inline notice "Home garden is paused, so 7C19 starts paused. It won't report or alert until the Site is resumed." with Resume Site (Admin+) and "Calibrate later"; tile and Devices row read "PAUSED BY SITE". (Source: EXPERIENCE.md › State Patterns › Device added to a paused Site; Platforms: iOS/Android)
UX-DR87: Implement Calibrate on a paused Device: do not start the wait; show "<ID> is paused, so it sends no Readings. Calibrate after the Pause ends." with Resume (Admin+) only when the Pause is the Device's own. (Source: EXPERIENCE.md › Component Patterns › Calibration reference point; Platforms: iOS/Android/web)
UX-DR88: Implement mobile notifications-off state (OS permission denied or revoked): persistent Inline notice in My notifications "Notifications are off for Coldframe on this phone. You won't get Alerts." with Open Settings deep-linking to the app's OS notification settings; same non-dismissable one-line notice above the tiles on every overview visit until granted; permission rechecked on every app foreground. (Source: EXPERIENCE.md › State Patterns › Notifications off; Platforms: iOS/Android)
UX-DR89: Implement web notifications-blocked state: if the browser blocks notifications when the toggle is turned on, return the toggle to off and show "Your browser blocks notifications for Coldframe. Allow them in this site's browser settings, then turn this on again." (Source: EXPERIENCE.md › State Patterns › Notifications blocked (web); Platforms: web)
UX-DR90: Implement the invitation-accepted-awaiting-Membership state on Create Site: show "Accepted an invitation? It can take a minute to appear.", refresh Memberships, and open the Site as soon as it arrives. (Source: EXPERIENCE.md › State Patterns › Invitation accepted, Membership not arrived yet; Key Flows › UJ-5; Platforms: iOS/Android/web)
UX-DR91: Implement form validation errors: DS invalid field + one-line reason under it, including "Low must stay below high.", "Move or unassign the Node on Potatoes first.", "A Site always keeps at least one Owner."; errors state what happened, what did not change and the next action. (Source: EXPERIENCE.md › State Patterns › Validation error, Voice and Tone; Platforms: iOS/Android/web)
UX-DR92: Implement sign-in errors as Inline notices in the sign-in card: Server unreachable "Can't reach your Coldframe Server. Check that this phone is on your home Wi-Fi." + Try again; TLS failure "Your Server's certificate isn't trusted, so Coldframe won't connect. The Server needs a valid certificate for its domain." with no continue-anyway (AD-13); Keycloak cancelled returns silently to Sign in; Keycloak error "Sign-in didn't finish: your Server's sign-in page returned an error. Nothing was changed." + Try again. (Source: EXPERIENCE.md › State Patterns › Sign-in and session; Platforms: iOS/Android/web)
UX-DR93: Implement session expired/revoked: keep cached data in stale mode and show Inline notice "You're signed out. Sign in again to see live data." with a Sign in action. (Source: EXPERIENCE.md › State Patterns › Sign-in and session; Platforms: iOS/Android/web)
UX-DR94: Implement BLE scan and connection errors: Bluetooth off/permission denied (step 1) "Coldframe needs Bluetooth to find the Hub." + Open Settings; no Device in 30 s "No Hub in range yet. Power it on within a few metres; its LED blinks orange while it waits." while scanning continues; Node setup mode timeout (step 2) "7C19 stopped listening. Press its setup button again."; BLE connection lost (any step) "Lost the connection to Hub 3F2A. Nothing was saved." + Try again. (Source: EXPERIENCE.md › State Patterns › BLE setup errors; Platforms: iOS/Android)
UX-DR95: Implement BLE credential and join errors: wrong setup code (step 2/3) "That setup code doesn't match Hub 3F2A. Check its label or the serial console." with field kept for re-entry; WPA3-only row disabled with reason; wrong Wi-Fi password (step 5) → error outcome "Wrong Wi-Fi password" with Re-enter password / Other network; Hub joined but Server not reached → error outcome "Hub 3F2A is on Novak-Home but can't reach your Server." with Try again / Help; Lot already has a Node → Lot picker tile disabled "HAS A NODE". (Source: EXPERIENCE.md › State Patterns › BLE setup errors; Platforms: iOS/Android)

#### Accessibility

UX-DR96: Support text scaling with no truncated controls or clipped values: iOS Dynamic Type (all sizes), Android font scale, web browser zoom to 200 % and 320 px reflow with `rem`-based sizes; tiles keep minimum square height and grow with content. (Source: EXPERIENCE.md › Accessibility Floor; DESIGN.md › Typography; Platforms: iOS/Android/web)
UX-DR97: Implement the one-column Lot grid fallback: switch to 1 column when iOS text size >= Accessibility 1, Android font scale >= 1.5, or the web grid container is narrower than 400 CSS px; tiles never shrink, compact or switch to a list; Lot detail rows wrap to 1-up; value moves directly under the status label. (Source: DESIGN.md › Layout & Spacing (Lot grid); EXPERIENCE.md › Accessibility Floor, Responsive & Platform; Platforms: iOS/Android/web)
UX-DR98: Provide screen-reader labels with role and state for every tile, Alert row, chart and control (VoiceOver / TalkBack / web SR) using the State Patterns label strings (e.g. "Tomatoes, needs water, about 20 percent, low 30 percent, Reading 07:02"); stale tiles append "not live, as of 07:02"; Site headline exposed as heading; decorative elements (Notification Window bar) hidden. (Source: EXPERIENCE.md › Accessibility Floor, State Patterns › Lot status; Platforms: iOS/Android/web)
UX-DR99: Verify status and selection are never conveyed by colour alone: all six statuses and stale remain distinct with colour and text removed (shape + icon per Shapes table); selected segments, tiles and rows carry a `checkmark` and expose selected state (`.isSelected` / `selected` semantics / `aria-pressed` or `aria-current`). (Source: EXPERIENCE.md › Accessibility Floor; DESIGN.md › Shapes; Platforms: iOS/Android/web)
UX-DR100: Enforce minimum tap targets of 44 pt (iOS), 48 dp (Android), 44 px (web) on all interactive elements. (Source: EXPERIENCE.md › Accessibility Floor; Platforms: iOS/Android/web)
UX-DR101: Honour Reduce Motion: no progress-bar or chart animation, `in-progress` icon static, state changes swap instantly; no reflow animation on refetch in any mode. (Source: EXPERIENCE.md › Accessibility Floor; DESIGN.md › Components › Setup progress; Platforms: iOS/Android/web)
UX-DR102: Ensure focus order follows reading order and full web keyboard support (tab order, Enter/Space activates tiles, Esc closes modals); focus indicator visible on web and with hardware keyboard or switch control on orange and neutral surfaces alike. (Source: EXPERIENCE.md › Accessibility Floor, Interaction Primitives; DESIGN.md › Components › Focus indicator; Platforms: iOS/Android/web)
UX-DR103: Setup flows never time out while a screen reader is reading; BLE timeouts announce their message assertively. (Source: EXPERIENCE.md › Accessibility Floor; Platforms: iOS/Android)
UX-DR104: Build announcement infrastructure: iOS `AccessibilityNotification.Announcement` / `.screenChanged`, Android polite/assertive `liveRegion` semantics, web `role="status"` (polite) and `role="alert"` (assertive) regions that exist in the DOM before they are filled. (Source: EXPERIENCE.md › Accessibility Floor › Announcements; Platforms: iOS/Android/web)
UX-DR105: Implement setup and calibration announcements: setup progress segment advance (polite: "Wi-Fi sent." / "Joining Novak-Home." / "Server sees Hub 3F2A."); setup success or step change moves focus to headline/step title; setup error or timeout (assertive: headline + next action); calibration fresh Reading (polite: "New Reading 07:17, raw 612. Record dry is available."); first % after Calibration (polite: "Tomatoes reads about 40 percent."); calibration waiting text never announced; BLE scan new candidate polite once per Device ("Hub 3F2A found, strong signal."), signal/order changes never. (Source: EXPERIENCE.md › Accessibility Floor › Announcements; Platforms: iOS/Android/web)
UX-DR106: Implement app-state announcements: entering stale mode (polite "Can't reach your Server. Showing data from 07:02."), leaving stale mode (polite "Live again."), Alert opens while app open (polite "New Alert: Tomatoes needs water."), Alert closes (never), Hub-silent or notifications-off notice appears (polite, notice text), save result (polite "Saved.") / validation on submit (assertive, field reason); never announce stale age ticks, elapsed-seconds counters or unchanged refetches. (Source: EXPERIENCE.md › Accessibility Floor › Announcements; Platforms: iOS/Android/web)

#### Responsive & platform

UX-DR107: Implement phone layout: 2-column Lot grid (1 column at accessibility text sizes), `gutter-mobile` gutters, vertical scroll, tab bar, full-screen setup flows. (Source: EXPERIENCE.md › Responsive & Platform; DESIGN.md › Layout & Spacing; Platforms: iOS/Android)
UX-DR108: Implement web responsive breakpoints (Carbon): < 400 px grid width → 1 column, side nav behind header menu; < 672 px → 2 columns, side nav collapsed behind header menu; 672-1055 px → 3 columns, side nav rail; >= 1056 px → 4 columns, side nav; >= 1312 px → 4 columns + 340 px Open Alerts rail on the right; `gutter-web` padding; tiles keep at least their square size and more Lots means scrolling, never compaction. (Source: EXPERIENCE.md › Responsive & Platform; DESIGN.md › Layout & Spacing (Lot grid); Platforms: web)
UX-DR109: Implement iOS platform structure: SwiftUI NavigationStack, TabView, sheets for Pause/Thresholds, native pickers and menus, edge-swipe back, Dynamic Type, with the Escendit brand layer. (Source: EXPERIENCE.md › Responsive & Platform (iOS), Foundation; Platforms: iOS)
UX-DR110: Implement Android platform structure: Jetpack Compose Material 3 (NavigationBar, TopAppBar, ModalBottomSheet, predictive back, system date/time pickers, Material Switch) themed with Escendit colour, type and 0-radius shapes. (Source: EXPERIENCE.md › Responsive & Platform (Android); DESIGN.md › Shapes; Platforms: Android)
UX-DR111: Implement web platform structure: SvelteKit BFF using DS Svelte components (AppShell, AppHeader, Button, TextInput, DatePicker, Modal, InlineNotification, ThemeSwitcher) consumed from the vendored tokens; all mobile features except BLE (Hub provisioning, Node identification). (Source: EXPERIENCE.md › Responsive & Platform (Web), Foundation; Platforms: web)
UX-DR112: Implement live-update and refresh primitives: pull-to-refresh on Site overview, Alerts, Devices (mobile); refetch on focus (web); SignalR invalidation hints (e.g. `readmodel.changed`) trigger refetch, never pushed domain data. (Source: EXPERIENCE.md › Interaction Primitives; Platforms: iOS/Android/web)
UX-DR113: Implement navigation and destructive-action primitives: native back (iOS edge swipe, Android system/predictive back, browser back) with modal flows asking before discarding; destructive actions (remove Lot, remove Membership, unassign Node) confirm in a native dialog / DS Modal naming the object; tap to act only (no long-press actions, no hover-only affordances). (Source: EXPERIENCE.md › Interaction Primitives; Platforms: iOS/Android/web)
UX-DR114: Enforce banned patterns as acceptance criteria: no gamification, streaks, celebratory animation, Alert snooze or "mark watered", auto-dismissing error toasts, carousels, or infinite spinners over stale data. (Source: EXPERIENCE.md › Interaction Primitives (Banned), Inspiration & Anti-patterns; Platforms: iOS/Android/web)

#### Notifications

UX-DR115: Implement notification channels: APNs (iOS) and FCM (Android) push with push-token registration via the shared KMP core; browser notifications on web via SignalR `notification.delivered` while a tab is open (no background web push); clients only display what the Server sends (timing, Notification Window holding, mute and Reminder logic are Server-side, AD-7); app icon uses the Coldframe mark. (Source: EXPERIENCE.md › Notifications, Foundation; DESIGN.md › Components › Push notification; Platforms: iOS/Android/web)
UX-DR116: Implement self-contained payload templates (title = condition, body = value and context), readable without the Server: Threshold low soil ("Tomatoes needs water" / "~20 % in the soil, your low is 30 %."), Threshold high ("Herbs too wet" / "~65 % in the soil, your high is 60 %."), Threshold other Sensor ("Tomatoes temperature below 5 °C" / "Reading 4 °C at 05:15."). (Source: EXPERIENCE.md › Notifications (payload table); Platforms: iOS/Android/web)
UX-DR117: Implement Health payload templates: Silent Node ("Node on Lot 'Beans' silent for 6 h" / "No Reading since 01:05. Check power or range."), Silent Hub ("Hub 3F2A silent for 5 min" / "Lots behind it can't be read."), Low battery ("Node battery 14 %, not charging" / "Lot 'Lettuce'."), Uncalibrated ("Soil sensor on Lot 'Peppers' needs calibration" / "An Owner or Administrator can calibrate it."). (Source: EXPERIENCE.md › Notifications (payload table); Platforms: iOS/Android/web)
UX-DR118: Implement the morning summary: title "<Site>: 2 need water, 3 to check", one line per open held Alert with needs-water first, footer "Held overnight, 22:00–07:00"; an unresolved Alert appears once per summary, not once per Reminder. (Source: EXPERIENCE.md › Notifications, Key Flows › UJ-2 (Edge); Platforms: iOS/Android/web)
UX-DR119: Implement Reminder copy reusing the Alert text with "still" in the body ("Still ~20 % in the soil…"); closing an Alert sends nothing; the UI never originates notifications of its own (no recovery pings, no nudges), keeping ~1 notification per Lot per user per day under defaults. (Source: EXPERIENCE.md › Notifications; Platforms: iOS/Android/web)
UX-DR120: Implement notification tap routing: Threshold/Uncalibrated → Lot detail; Silent/Battery → Devices row for that Device; morning summary → Site overview; opening switches to the notification's Site and falls into stale mode if the Server is unreachable. (Source: EXPERIENCE.md › Notifications, State Patterns › Away from home; Platforms: iOS/Android/web)
UX-DR121: Configure notification presentation: grouped per Site (iOS thread identifier, Android notification group/channel), standard interruption level (never critical or time-sensitive), no app-icon badge count. (Source: EXPERIENCE.md › Notifications; Platforms: iOS/Android)
UX-DR122: Implement mobile permission timing: request notification permission when the user first lands on a Site overview (creator right after Create Site, or a newly accepted Member), preceded by one line of why ("Coldframe tells you when a Lot needs water."); denial routes to the notifications-off state. (Source: EXPERIENCE.md › Notifications (Permission), Key Flows › UJ-1 step 2, UJ-5 step 3; Platforms: iOS/Android)
UX-DR123: Implement the web "Browser notifications while Coldframe is open" toggle in My notifications that requests browser permission only when turned on and shows the same content as push; a single Alert appears once per device, not duplicated. (Source: EXPERIENCE.md › Notifications (Permission), Information Architecture (Browser notification), Key Flows › UJ-6; Platforms: web)

#### i18n

UX-DR124: Externalize every user-facing string from the first commit (iOS String Catalog, Android/KMP string resources, web message catalog); English only in V1; no copy in code or images; uppercase applied via style only. (Source: EXPERIENCE.md › Internationalization; DESIGN.md › Typography; Platforms: iOS/Android/web)
UX-DR125: Use CLDR plural rules for all counted strings (e.g. "1 Lot needs water" / "2 Lots need water", counts subline, Alerts count, morning summary). (Source: EXPERIENCE.md › Internationalization, Voice and Tone; Platforms: iOS/Android/web)
UX-DR126: Make layouts tolerate 30-40 % longer text: below accessibility sizes tile names wrap to two lines then truncate (full name on Lot detail and in the accessibility label); at accessibility sizes nothing truncates and tiles grow; buttons grow in height, never clip; segmented choices wrap. (Source: EXPERIENCE.md › Internationalization; Platforms: iOS/Android/web)
UX-DR127: Format dates, times (12/24 h per locale), numbers, percent spacing and units by locale; durations via locale-aware formatters with rules "min" under 1 h, "h" under 24 h ("h min" in the stale header), "d" from 24 h; earlier than today → weekday; older than 7 days → date. (Source: EXPERIENCE.md › Internationalization, Voice and Tone (Durations); Platforms: iOS/Android/web)
UX-DR128: Implement shared value-formatting rules: soil moisture `~` prefix rounded to nearest 5 % everywhere (tiles, detail, Alerts, push); uncalibrated shows `raw` and the raw value, never %; temperature whole °C, humidity whole %RH, air (gas) kΩ with 3 significant digits. (Source: EXPERIENCE.md › Voice and Tone (Rules); DESIGN.md › Do's and Don'ts; Platforms: iOS/Android/web)
UX-DR129: Implement the Site headline sentence rules: any needs water → "2 Lots need water" / "Tomatoes needs water"; none but some unknown or needs calibration → "2 Lots can't be read"; all OK → "Nothing needs water"; Site paused → "Paused until 1 Mar" / "Paused"; no Nodes → "No Readings yet". (Source: EXPERIENCE.md › Voice and Tone (Headline sentence); Platforms: iOS/Android/web)
UX-DR130: Apply voice rules to all copy: calm and literal, no exclamation marks, emoji, encouragement or "successfully"; never "OK", "fine" or "all good" for unknown/needs-calibration/paused/noNode/stale Lots; button labels are result verbs; errors state what happened, what did not change and the next action; "Silence Window" only for Device silence, "outside your window" for Notification Window. (Source: EXPERIENCE.md › Voice and Tone; Platforms: iOS/Android/web)
UX-DR131: Keep glossary terms (Site, Lot, Node, Hub, Sensor, Reading, Threshold, Alert, Reminder, Notification Window, Silence Window, Pause, Calibration) capitalised as proper nouns in source strings with a translator glossary; never translate or reformat Device IDs, setup codes, SSIDs or Server URLs. (Source: EXPERIENCE.md › Internationalization, Voice and Tone; Platforms: iOS/Android/web)

### FR Coverage Map

FR1: Epic 3 - Provision a Hub over BLE with its setup code
FR2: Epic 4 - Pair a Node and assign it to a Lot
FR3: Epic 4 - Nodes declare Sensor Specifications
FR4: Epic 4 - Readings every 15 min through the Hub, buffered and acknowledged
FR5: Epic 1 - Sign in via Keycloak (OIDC)
FR6: Epic 1 - Create and manage Sites and Lots
FR7: Epic 9 - Invite Members and manage Roles
FR8: Epic 4 - Site overview, Lot statuses, history, stale and unreachable indicators
FR9: Epic 5 - Two-point Calibration from stored Readings
FR10: Epic 5 - Set Thresholds
FR11: Epic 6 - Open and close Threshold Alerts
FR12: Epic 6 - Reminders while an Alert is open
FR13: Epic 7 - Silent Device Alert
FR14: Epic 7 - Low-battery Alert
FR15: Epic 6 - Push notifications
FR16: Epic 6 - Notification Window and summary
FR17: Epic 6 - Mute a Site
FR18: Epic 8 - Pause a Device or a Site
FR19: Epics 1, 3–9 - Mobile parity, delivered in each epic's mobile stories (BLE setup in Epics 3–4)
FR20: Epics 1, 4–9 - Web parity, delivered in each epic's web stories; browser notifications in Epic 6
FR21: Epic 7 - Uncalibrated Sensor Alert

Cross-cutting: NFR1–NFR16 and UX-DR1–UX-DR131 apply to every story they touch. NFR16 (test-first) applies to all stories. Deployment NFRs (NFR1, NFR3, NFR7, NFR10) are delivered in Epic 2; reproducibility and open-source NFRs (NFR7, NFR8) are completed in Epic 10.

## Epic List

### Epic 1: Sign in and create my garden
Simon signs in through Keycloak on web, iOS, and Android, creates the Site "Home" as its Owner, and adds and renames Lots. The Site overview shows its empty state. Starts with the monorepo scaffold: layout, `tests/`, the Aspire AppHost and test host, CI, and Central Package Management. Includes the vendored design tokens and theme (System/Light/Dark), the identity grains and the Keycloak → Temporal → Orleans pipeline, per-Site authorization with a generated matrix test, the SvelteKit BFF, and the KMP core with SwiftUI and Compose shells.
**FRs covered:** FR5, FR6 (and FR19, FR20 for these surfaces)

### Epic 2: Run Coldframe on my home server
The whole stack runs 24/7 on Simon's home server. Phones and the Hub reach it over TLS with public certificates, nothing is lost on restart, and backups can be restored. Covers RKE2 with Fleet and Helm charts, CloudNativePG with off-node S3 backups, Let's Encrypt DNS-01 with split DNS, the migration job, images on ghcr.io, and out-of-band Secrets.
**FRs covered:** none (NFR1, NFR3, NFR7, NFR10)

### Epic 3: Bring the Hub online
Simon adds a Hub from his phone over BLE with its setup code. It appears on the Site within a minute, and Devices shows it online with its last-seen time. Covers Hub firmware (strongest-BSSID Wi-Fi, TLS with date checks, heartbeat, HMAC request authentication), the AD-25 BLE setup protocol, AD-12 enrolment with the eFuse root and dev mode, the crypto spec with test vectors, and the Device grain.
**FRs covered:** FR1 (and FR19 for BLE setup)

### Epic 4: See what my soil is doing
Simon presses a Node's button, assigns it to "Tomatoes", and sees its Readings: Lot tiles, Lot detail with the 30-day chart, battery and charging status, and last seen. All six Lot statuses are shown, stale data is marked, and the app says when the Server is unreachable. Covers Node firmware (Sensors, ESP-NOW, deep sleep, 24 h buffer, sealing, report now, channel following), ingestion with acknowledgements and the replay window, the partitioned Readings table, the Lot and Sensor grains, and the LotStatus projection.
**FRs covered:** FR2, FR3, FR4, FR8

### Epic 5: Calibrate the soil and set Thresholds
Simon calibrates the soil probe (dry and wet, using report now) and sets low and high Thresholds. Lots then show approximate % and *OK*, with the Threshold band on the chart. *Needs water* goes live with Threshold Alerts in Epic 6, because it is defined by an open low-side Alert (AD-14).
**FRs covered:** FR9, FR10

### Epic 6: Get told when to water
A morning push says "Tomatoes needs water" inside Simon's Notification Window. Reminders repeat while the Lot stays dry, anything held overnight arrives as one summary, and a Site can be muted. The web app shows browser notifications while open. Covers the Alert and User grains, the Notifier seam (APNs, FCM, SignalR), time zones, and notification permission.
**FRs covered:** FR11, FR12, FR15, FR16, FR17 (and browser notifications from FR20)

### Epic 7: Know when something breaks
A silent Node or Hub, a low battery, or an uncalibrated Sensor raises a Health Alert, capped at one per day. A silent Hub is never reported once per Node.
**FRs covered:** FR13, FR14, FR21

### Epic 8: Pause for maintenance and winter
Simon pauses a Device or the whole Site, optionally until a date, without false alarms. It resumes automatically.
**FRs covered:** FR18

### Epic 9: Share the garden
Simon invites the neighbour by email as a Member and changes or removes Roles; a Site always keeps an Owner. Members get Alerts but see no admin controls.
**FRs covered:** FR7

### Epic 10: Let others rebuild Coldframe
Another maker builds a Node and Hub and deploys the stack from the public docs alone. Covers the adopter guide, hardware designs in `hardware/`, the Compose example, the release pipeline (one SemVer, firmware binaries), and the firmware learning-reference docs.
**FRs covered:** none (NFR7, NFR8)

Dependency flow: 1 → 2 → 3 → 4 → 5 → 6 → 7. Epic 8 needs Epics 6–7 (it closes Health Alerts and suppresses notifications). Epic 9 needs Epic 6 (new Members receive Alerts). Epic 10 comes last.

## Epic 1: Sign in and create my garden

Simon signs in through Keycloak on web, iOS, and Android, creates the Site "Home" as its Owner, and adds and renames Lots. The Site overview shows its empty state. Every story is test-first (NFR16): its acceptance criteria are written as failing tests before implementation.

### Story 1.1: Monorepo scaffold, CI and local dev stack

As a maker building Coldframe,
I want the monorepo, CI and a one-command local stack in place,
So that every later story lands in a consistent layout, is built and tested automatically, and runs locally against real dependencies.

**Acceptance Criteria:**

**Given** a fresh clone of the repository
**When** I inspect the tree
**Then** it contains `apps/`, `packages/`, `tests/` (split by language), `aspire/`, `deploy/`, `hardware/` and `docs/`, as in the architecture spine's source tree
**And** .NET uses Central Package Management with Orleans 10.3.1, `Microsoft.Orleans.Streaming.NATS` 10.3.1-alpha.1 and NATS.Net 2.x pinned, and no transitive version floats (AD-15)

**Given** Docker or Podman is available
**When** I run the Aspire AppHost
**Then** PostgreSQL, NATS JetStream, Temporal, and Keycloak start
**And** Keycloak runs the Phase Two image 26.6.7 with `keycloak-temporal-extensions` v0.0.1-rc.2 loaded
**And** an Orleans silo in `apps/cs/server` starts with the Escendit service defaults and reports healthy on its health endpoint

**Given** a test project in `tests/cs/` that uses `Aspire.Hosting.Testing`
**When** the integration test suite runs
**Then** it starts the same AppHost and asserts that the silo's health endpoint returns healthy (the first failing-then-passing test)

**Given** a pull request
**When** GitHub Actions runs
**Then** it builds and tests every language present (C#, Rust, Kotlin, TypeScript, and Swift on a macOS runner) and fails the check on any failing test or lint error
**And** Rust unit tests may live inline (`#[cfg(test)]`); every other test lives under `tests/`

**Given** the repository
**When** I read `docs/`
**Then** a developer quickstart explains how to run the AppHost and the tests

### Story 1.2: Event journal, migrations and projection pipeline

As a maker building Coldframe,
I want one durable event journal with migrations and a projector framework,
So that every grain added in later stories persists and projects state the same way and survives restarts (NFR3).

**Acceptance Criteria:**

**Given** an empty PostgreSQL database
**When** the migration job runs (FluentMigrator 8.0.1 with `Escendit.Orleans.Migrations.Cluster.PostgreSQL` 10.3.1-rc.0)
**Then** the Orleans cluster schema (clustering, persistence, reminders) and the Coldframe event journal and outbox tables exist
**And** the migrations are forward-only, and application startup never runs DDL (AD-22)

**Given** a sample JournaledGrain that uses CustomStorage (AD-2, AD-21)
**When** it raises an event
**Then** the event is appended to the journal as System.Text.Json, with stream ID, version, type, schema version and global position
**And** the event and its outbox row are written in one transaction

**Given** a projector with a checkpoint table
**When** events are appended
**Then** the projector applies them in global-position order and records its checkpoint
**And** after its read model and checkpoint are deleted, it rebuilds the read model from position 0 with the same result

**Given** Orleans streams are disabled
**When** events are appended
**Then** projectors still converge by polling the journal; streams are only wake-up hints (AD-5, AD-21)

**Given** a fixture journal in `tests/cs/`
**When** the CI event-replay test runs
**Then** every stored event type deserializes (directly or through a registered upcaster) and replays without error (AD-24)

### Story 1.3: Design tokens and themes

As a user of any Coldframe app,
I want one consistent Escendit look in light and dark themes,
So that iOS, Android, and the web look like one product and meet the contrast floor.

**Acceptance Criteria:**

**Given** `escendit/branding` `css/theme.css`
**When** the tokens are vendored into `packages/design-tokens`
**Then** the repository holds a copied token source, with no dependency on the private `@escendit/branding` package
**And** generation produces Swift, Kotlin and CSS outputs for every token in DESIGN.md's frontmatter, with light and dark values (UX-DR1 to UX-DR6, UX-DR8, UX-DR10, UX-DR11)

**Given** the generated tokens
**When** the CI contrast check runs
**Then** it recomputes the load-bearing contrast table from DESIGN.md and fails on any pair below 4.5:1 for text or 3:1 for UI and graphics (UX-DR7, NFR14)

**Given** the Carbon icon subset listed in UX-DR13
**When** the package is built
**Then** every listed icon is available on all three platforms and inherits the current colour

**Given** each platform
**When** a test reads the typography tokens
**Then** each type role maps to Dynamic Type (iOS), `sp` (Android) and `rem` (web) as defined in UX-DR9

### Story 1.4: Sign in on the web

As Simon on my laptop,
I want to sign in to the web app through my Keycloak and choose my theme,
So that I can use Coldframe from a browser on my home network.

**Acceptance Criteria:**

**Given** I am signed out
**When** I open the web app
**Then** I see the Sign-in surface with a single SIGN IN button (UX-DR59, UX-DR60)
**And** SIGN IN starts the OIDC Authorization Code + PKCE flow through the SvelteKit backend-for-frontend using `@escendit/sveltekit-auth-keycloak` (AD-14, FR5, FR20)

**Given** Keycloak authenticates me
**When** I return to the web app
**Then** the browser holds only a session cookie, with no access or refresh token exposed to browser JavaScript
**And** I land on the app shell with the Garden · Alerts · Devices · Members navigation (UX-DR58)

**Given** the Server is unreachable, the certificate is not trusted, or Keycloak returns an error
**When** I press SIGN IN
**Then** the matching Inline notice from UX-DR92 appears, with no "continue anyway" option

**Given** my session expires or is revoked
**When** I next interact with the app
**Then** the notice "You're signed out. Sign in again to see live data." appears, with a Sign in action (UX-DR93)

**Given** Settings → Appearance
**When** I choose System, Light or Dark (UX-DR53, UX-DR15)
**Then** the theme applies immediately and persists on this browser (per device, not synced; UX-DR15)
**And** System follows `prefers-color-scheme`, mapped onto the design system's `data-theme`

**Given** any web page in this story
**When** the automated axe check and keyboard tests run in Playwright
**Then** there are no serious violations, the focus ring is visible, and tab order follows reading order (UX-DR16, UX-DR102)
**And** every string comes from the message catalogue, with no hard-coded copy (UX-DR124)

**Given** the UX contract (DESIGN.md, EXPERIENCE.md)
**When** this story's surfaces and components are built
**Then** each of these has at least one named test, written failing before implementation, and they implement UX-DR34, UX-DR35, UX-DR36, UX-DR56, UX-DR71, UX-DR75, UX-DR76, UX-DR100, UX-DR101, UX-DR104, UX-DR111, UX-DR113, UX-DR114, UX-DR125, UX-DR126, UX-DR127, UX-DR130, UX-DR131 as specified

### Story 1.5: Sign in on iOS and Android

As Simon on my phone,
I want to sign in to the iOS or Android app and choose my theme,
So that I can use Coldframe natively on my phone.

**Acceptance Criteria:**

**Given** an app build configured with the Server URL and Keycloak issuer at build time (AD-23)
**When** I open the app signed out
**Then** I see the Sign-in surface with only SIGN IN, and no field to enter a Server address

**Given** I press SIGN IN
**When** the shared Kotlin core runs OIDC Authorization Code + PKCE (kotlin-multiplatform-oidc) through `ASWebAuthenticationSession` on iOS or Custom Tabs on Android
**Then** after authenticating I return to the app, signed in, with tokens held by the shared core (FR5, FR19)
**And** I see native tab navigation Garden · Alerts · Devices · Settings (UX-DR57, UX-DR109, UX-DR110)

**Given** the Server is unreachable, the certificate is not trusted, Keycloak fails, or I cancel
**When** sign-in runs
**Then** the matching UX-DR92 notice appears; cancelling returns to Sign in with no error

**Given** Settings → Appearance
**When** I choose System, Light or Dark
**Then** the SwiftUI or Compose theme updates from the generated tokens and persists on the device

**Given** the shared Kotlin core module in `tests/kt/`
**When** its unit tests run
**Then** the OIDC flow, the token refresh and the session-expired transitions are covered, with the network mocked

**Given** the largest system text size
**When** the sign-in and shell screens render
**Then** nothing is truncated or clipped (UX-DR96), and VoiceOver or TalkBack reads each control with its role (UX-DR98)

**Given** the UX contract (DESIGN.md, EXPERIENCE.md)
**When** this story's surfaces and components are built
**Then** each of these has at least one named test, written failing before implementation, and they implement UX-DR34, UX-DR35, UX-DR36, UX-DR56, UX-DR71, UX-DR75, UX-DR76, UX-DR100, UX-DR101, UX-DR104, UX-DR113, UX-DR114, UX-DR125, UX-DR126, UX-DR127, UX-DR130, UX-DR131 as specified

### Story 1.6: Create a Site on the Server with per-Site authorization

As Simon, newly signed in,
I want the Server to create my garden as a Site and make me its Owner immediately,
So that everything I do next is authorized on that Site.

**Acceptance Criteria:**

**Given** an authenticated User with no Membership
**When** the Edge API calls `User.CreateSite(idempotencyKey)` with the name "Home"
**Then** a Phase Two Organization is created and tagged with the key, the Site grain is initialized with me as Owner, and `SiteCreated` and `MembershipGranted` are journaled (FR6, AD-3)
**And** my very next request on that Site is authorized, because the Site grain's events update the identity projection immediately (read-your-writes)

**Given** the same Create Site request is retried with the same `Idempotency-Key`
**When** it is processed
**Then** no second Organization or Site is created, and the original result is returned

**Given** a Site ID that does not exist
**When** any Site-scoped call is made
**Then** the API returns 404 as RFC 9457 Problem Details, because the Site grain is `Uncreated`

**Given** the per-Site authorization policy (AD-4)
**When** the generated authorization-matrix test runs over every endpoint so far, for every Role, on my Site and on another Site
**Then** every allowed and denied case matches the endpoint's declared minimum Role, and the matrix generator picks up new endpoints automatically (NFR6, AD-24)

**Given** Orleans TestCluster and Aspire integration tests with Keycloak and Phase Two
**When** they run
**Then** creation, retry idempotency, read-your-writes and the 404 path are covered

### Story 1.7: Reconcile identity changes from Keycloak

As Simon,
I want changes made in Keycloak to reach Coldframe reliably,
So that Coldframe's view of who belongs where never drifts from Keycloak.

**Acceptance Criteria:**

**Given** `keycloak-temporal-extensions` publishing Keycloak admin and user events to Temporal
**When** a Temporal workflow in the Server consumes an organization or membership event
**Then** it is delivered to the Site and User grains and applied as an idempotent reconciliation (AD-3, AD-5)
**And** an event that matches what the Site grain already wrote causes no change and no error

**Given** a break-glass edit in Keycloak's admin console that would leave a Site with no Owner
**When** the event arrives
**Then** the Site grain keeps its last valid Owner set for authorization and raises an operator-visible error, and never silently repairs it

**Given** a Site deletion arriving from Keycloak
**When** it is applied
**Then** the Site grain moves to `Deleted`, its roster is suspended and Site-scoped calls return 404 (AD-20)

**Given** integration tests on the Aspire AppHost that change Keycloak through its admin API
**When** they run
**Then** the reconciliation, duplicate events, the no-Owner break-glass case and Site deletion are covered

### Story 1.8: Create Site and the empty Garden in the apps

As Simon, newly signed in,
I want to name my Site in the app and see my empty garden,
So that I can start setting it up from my phone or laptop.

**Acceptance Criteria:**

**Given** I am signed in and have no Membership
**When** I reach the app on web, iOS or Android
**Then** I see Create Site with a Site name field and the detected time zone to confirm (UX-DR61), and submitting it calls the Story 1.6 endpoint with an `Idempotency-Key`

**Given** a Site with no Hub and no Node
**When** I open Garden on web, iOS or Android
**Then** I see the Site summary header and the empty state "No Readings yet", with the first-run step tiles (UX-DR21, UX-DR54, UX-DR62, UX-DR82)
**And** the Site switcher lists my Sites with my Role and offers "New Site" (UX-DR23)

**Given** snapshot tests and a Playwright end-to-end test (sign in, then create the Site, then the empty Garden)
**When** they run
**Then** they pass in light and dark themes, at the largest text size

**Given** the UX contract (DESIGN.md, EXPERIENCE.md)
**When** this story's surfaces and components are built
**Then** each of these has at least one named test, written failing before implementation, and they implement UX-DR22 as specified

### Story 1.9: Manage my Site and Lots

As an Owner or Administrator,
I want to rename my Site and create, rename and remove Lots,
So that the garden in Coldframe matches my real beds.

**Acceptance Criteria:**

**Given** I am the Owner of "Home"
**When** I rename the Site in Site settings (UX-DR74)
**Then** the new name appears everywhere, including the Site switcher
**And** an Administrator or Member does not see the rename control (UX-DR84), and the API rejects their attempt with 403

**Given** I am an Administrator or Owner
**When** I create the Lots "Tomatoes" and "Beans"
**Then** each Lot grain is created (event-sourced), and each appears on Garden as a tile with status *no Node* and the "+ add a Node" affordance (UX-DR18)
**And** tiles appear in the Server's sort order, and clients never re-sort (UX-DR20)

**Given** a Lot
**When** I rename it
**Then** the new name appears on every surface after the projection updates

**Given** a Lot with no Node
**When** I remove it and confirm the destructive action
**Then** a `LotRemoved` event and tombstone are recorded, and the Lot disappears from Garden (AD-20)
**And** its ID stays resolvable for history

**Given** a Lot that holds a Node (a claim held by the Lot grain; tested with a fixture claim, because Node assignment arrives in Epic 4)
**When** I try to remove it
**Then** the Lot grain refuses with a clear error, and nothing changes (FR6, AD-18)

**Given** a create request retried with the same `Idempotency-Key`
**When** it is processed
**Then** only one Lot exists

**Given** a Member
**When** they open Site settings
**Then** they see Lots read-only, with no create, rename or remove controls, and the API rejects these calls with 403
**And** these endpoints are included in the generated authorization matrix

## Epic 2: Run Coldframe on my home server

The whole stack runs 24/7 on Simon's home server. Phones and the Hub reach it over TLS with publicly trusted certificates. Nothing is lost on restart, and backups can be restored. Infrastructure stories are test-first through chart unit tests, schema validation and a CI smoke install on a disposable k3d cluster; the home-server run is verified manually against a documented checklist.

### Story 2.1: Container images published per release

As Simon deploying Coldframe,
I want versioned container images for every server-side component,
So that my cluster pulls exactly the release I choose.

**Acceptance Criteria:**

**Given** a release tag `vX.Y.Z` on the monorepo
**When** the release workflow runs
**Then** images for the Server (Orleans silo, Edge API and SignalR), the web BFF and the migration job are pushed to `ghcr.io/escendit/coldframe/<component>:X.Y.Z` (AD-23)
**And** each image is multi-arch (amd64 and arm64) where the base image supports it, and the workflow reports any component where it doesn't

**Given** a container built from any image
**When** it starts
**Then** it takes configuration only from environment variables and exposes liveness and readiness endpoints (Escendit service defaults)
**And** it runs as a non-root user

**Given** a pull request that changes a Dockerfile
**When** CI runs
**Then** the image builds, and a container-structure test checks the entrypoint, the non-root user and the health endpoint

### Story 2.2: Helm charts with a fixed Secret contract

As Simon deploying Coldframe,
I want Helm charts for the whole stack that read secrets I create myself,
So that I can install and upgrade the stack without putting secrets in Git.

**Acceptance Criteria:**

**Given** `deploy/charts/`
**When** I inspect it
**Then** there are charts (or subcharts) for the Server, web BFF, Keycloak (the Phase Two image plus `keycloak-temporal-extensions`), Temporal and NATS JetStream
**And** `deploy/SECRETS.md` lists every Kubernetes Secret by fixed name and key (SMTP, APNs/FCM credentials, DNS-01 token, Server enrolment private key, database credentials), and no chart templates a secret value (AD-15)

**Given** a Helm upgrade to a new version
**When** it runs
**Then** the migration Job runs first and must succeed before the Server Deployment rolls
**And** the Server runs as a single replica with a stop-then-start strategy, and a graceful-shutdown timeout lets the silo drain (AD-15, AD-22)

**Given** the charts
**When** CI runs helm lint, helm-unittest and kubeconform schema validation
**Then** all pass, and a test asserts that no Secret manifest contains data

**Given** a disposable k3d cluster in CI with placeholder Secrets
**When** the charts are installed
**Then** every pod becomes ready, and the Server health endpoint returns healthy

### Story 2.3: Database cluster with off-node backups and tested restore

As Simon,
I want all Coldframe data in one managed PostgreSQL cluster with backups off the server,
So that a disk or server failure doesn't lose my garden's history.

**Acceptance Criteria:**

**Given** CloudNativePG 1.30.1 installed in the cluster
**When** the Coldframe database chart is applied
**Then** one CNPG cluster runs on PostgreSQL 18, with separate databases and roles for the Server/Orleans, Temporal and Keycloak (AD-15)

**Given** an adopter-provided S3-compatible target, configured through a fixed-name Secret
**When** the cluster runs
**Then** CNPG (Barman Cloud) ships WAL continuously and takes scheduled base backups to that target

**Given** a backup exists
**When** I follow `docs/operations/restore.md` to restore into a fresh cluster
**Then** the Server starts on the restored data, with Sites, Lots, Memberships and events intact
**And** the runbook states the recovery point: data written after the last archived WAL segment is lost
**And** the runbook requires advancing every Device's replay window by a safety margin after a restore (AD-17)

**Given** the k3d smoke environment in CI with RustFS (S3-compatible, Apache-2.0) standing in for S3
**When** the backup-and-restore test runs
**Then** a marker row written before the backup exists after the restore

### Story 2.4: TLS with public certificates on my home network

As Simon,
I want the web app, the API and Keycloak on a real domain with publicly trusted certificates,
So that phones, browsers and the Hub connect securely without anyone installing a certificate.

**Acceptance Criteria:**

**Given** RKE2's bundled Traefik ingress and cert-manager 1.21.2
**When** the ingress chart is applied with my domain and a DNS-01 API token in a fixed-name Secret
**Then** cert-manager obtains Let's Encrypt certificates via the DNS-01 challenge, with no inbound internet traffic (AD-13, NFR1)
**And** certificates renew automatically before they expire

**Given** the ingress
**When** it is deployed
**Then** only HTTPS (443) is exposed for the Server, web and Keycloak hosts, with no plain-HTTP listener serving application traffic (NFR10)

**Given** my router or local DNS configured per `docs/operations/split-dns.md`
**When** a phone on the home Wi-Fi resolves the Coldframe domain
**Then** it gets the LAN ingress address, and the TLS handshake succeeds using public roots only

**Given** the charts
**When** CI runs chart unit tests
**Then** they assert that TLS is set on every Ingress and IngressRoute, the Issuer uses DNS-01, and no route serves port 80

### Story 2.5: GitOps deployment to my RKE2 server

As Simon,
I want my home server to deploy Coldframe from Git with Fleet,
So that upgrading means changing a version in Git, and a restart never loses data.

**Acceptance Criteria:**

**Given** single-node RKE2 (the stable v1.36 line) with Fleet v0.16.2
**When** I point a Fleet GitRepo at `deploy/` per `docs/operations/install.md`
**Then** Fleet installs CloudNativePG, the database, ingress and TLS, and all Coldframe charts in dependency order, and every pod becomes ready (NFR7)

**Given** a running installation
**When** I change the release version in my Fleet configuration
**Then** the upgrade runs migrations first and then restarts the Server with stop-then-start, and Sites and Lots are intact afterwards

**Given** a running installation with Sites and Lots
**When** the Server pod, the database pod or the whole node restarts
**Then** no stored data, setting or event is lost, and the apps reconnect when the Server is back (NFR3)

**Given** `docs/operations/install.md`
**When** I follow it on the home server
**Then** a manual verification checklist confirms: pods ready; HTTPS valid on all three hosts; sign-in works from phone and browser; restart durability; a backup exists in S3

## Epic 3: Bring the Hub online

Simon adds a Hub from his phone over BLE with its setup code. It appears on the Site within a minute, and Devices shows it online with its last-seen time. Firmware is tested host-side only, with hardware behind traits; on-device behaviour is verified manually against a checklist (NFR16).

### Story 3.1: Wire and crypto contracts with shared test vectors

As a maker building Coldframe,
I want the BLE setup, Device, and crypto contracts defined once and generated into Rust, C# and Kotlin,
So that firmware, Server and app can't disagree on message shape or key derivation.

**Acceptance Criteria:**

**Given** `packages/proto`
**When** I inspect it
**Then** it defines, with `protocol_version`, the BLE setup messages (identity, Wi-Fi scan list, Wi-Fi config and result, Site binding, enrolment request and response) and the sealed-downlink envelope, including an empty `commands` field (AD-10, AD-16, AD-25)
**And** `packages/openapi` defines `POST /device/heartbeat`, `POST /device/ingest` (placeholder), `GET /enrolment-key` and the enrolment endpoint

**Given** `packages/crypto-spec`
**When** code generation runs
**Then** labels (`coldframe/device/v1`, `seal/v1`, `ack/v1`, `hub-auth/v1`), algorithms (HKDF-SHA256, HPKE X25519-HKDF-SHA256-ChaCha20Poly1305, ChaCha20-Poly1305), nonce layout and the setup-session derivation (X25519 plus proof-of-possession code via HKDF) are emitted as constants for Rust, C# and Kotlin (AD-12, AD-25)

**Given** the shared test vectors in `packages/crypto-spec`
**When** CI runs the vector tests in Rust (`tests/rs`), C# (`tests/cs`) and Kotlin (`tests/kt`)
**Then** all three produce identical outputs for key derivation, HPKE sealing, the AEAD with nonce layout, and the setup-session keys (AD-24)
**And** a breaking change to any `.proto` or OpenAPI file fails the contract-compatibility check

### Story 3.2: Hub firmware foundation with a hardware-bound identity

As a maker building Coldframe,
I want the Hub to create its own secret identity on first boot,
So that no human ever sees its key and it can prove who it is.

**Acceptance Criteria:**

**Given** `apps/rs/hub` on esp-hal 1.2.2 and esp-radio 1.0.0-beta.1 for ESP32-S3
**When** a release build boots for the first time
**Then** it generates a key from the TRNG with the radio enabled, burns it into a read-protected eFuse key block with HMAC purpose ToUser, and derives `K_dev = HMAC-SHA256_eFuse(root, "coldframe/device/v1")` (AD-12)
**And** on later boots it detects the burned key and never burns again

**Given** a dev-mode build (a documented Cargo feature)
**When** it boots
**Then** it uses a software key and never touches eFuses, and release builds refuse to compile with dev mode enabled

**Given** key derivation and purpose-key logic in `packages/rs/crypto`, behind a hardware trait
**When** host-side tests run with a mock HMAC peripheral
**Then** the derived purpose keys match the crypto-spec test vectors

**Given** the Hub on the bench
**When** I follow the manual checklist
**Then** the first boot burns the key once (verified with `espflash board-info` key purposes) and a dev-mode board shows no eFuse change

### Story 3.3: Server-side Device enrolment

As Simon,
I want the Server to accept a new Device's sealed identity during setup,
So that only Devices I set up can talk to my Server.

**Acceptance Criteria:**

**Given** the Server enrolment keypair in a fixed-name Secret
**When** an authenticated Administrator calls `GET /enrolment-key`
**Then** it returns the X25519 public key and its fingerprint

**Given** an enrolment request with an HPKE-sealed `K_dev`, the Device identity and a Site ID, sent by an Administrator of that Site
**When** the Server processes it
**Then** the Device grain calls `Site.RegisterDevice(deviceId, kind)`, persists `DeviceEnrolled`, and stores `K_dev` encrypted at rest (AD-12, AD-18)
**And** the Site's roster includes the Device, and the `RegisterDevice` reply carries the Site's Pause state (always empty until Epic 8)

**Given** a malformed or wrongly sealed enrolment, a Member caller, or a Device already enrolled on another Site
**When** it is submitted
**Then** it is rejected with RFC 9457 Problem Details and nothing is persisted
**And** the endpoints are included in the generated authorization matrix

**Given** Server integration tests on the Aspire AppHost
**When** enrolment runs end to end with the crypto-spec vectors
**Then** the stored key decrypts to the expected `K_dev`

### Story 3.4: Hub BLE setup service

As Simon standing next to a new Hub,
I want the Hub to offer a secure setup channel that only works with its setup code,
So that nobody nearby can take it over or read my Wi-Fi password.

**Acceptance Criteria:**

**Given** an unprovisioned Hub
**When** it boots
**Then** it advertises the Coldframe setup GATT service (trouble-host 0.7.0) and prints its per-Device proof-of-possession code to the serial console at first boot; the code is persisted and never sent over BLE (AD-25)

**Given** a client that knows the setup code
**When** it runs the setup session
**Then** X25519 plus the setup code via HKDF produces the session key, every message is encrypted with ChaCha20-Poly1305, and a wrong code fails the session with a distinct error

**Given** an established session
**When** the client asks for networks, sends Wi-Fi config and a Site ID, and sends the Server enrolment public key
**Then** the Hub returns a Wi-Fi scan list (WPA3-only networks flagged as unsupported, H-3), stores the credentials, returns `K_dev` sealed with HPKE to that key, and reports its join result

**Given** the setup state machine behind BLE and storage traits
**When** host-side tests run
**Then** the success path, wrong code, timeout and malformed messages are covered, using the crypto-spec vectors

**Given** a desktop test client in `tests/rs` (for example, btleplug)
**When** I run it against a bench Hub per the manual checklist
**Then** a full setup completes, and a wrong setup code is refused

**Given** the Hub firmware image
**When** it is inspected in a test
**Then** it contains no Wi-Fi credentials; credentials exist only after BLE provisioning (FR1)

### Story 3.5: Hub joins Wi-Fi and heartbeats to the Server

As Simon,
I want the provisioned Hub to join my Wi-Fi reliably and check in with the Server,
So that Coldframe knows the Hub is alive.

**Acceptance Criteria:**

**Given** a provisioned Hub
**When** it joins Wi-Fi
**Then** it scans all channels and joins the strongest BSSID for the SSID (`ScanMethod::AllChannels`), and re-joins after a disconnect (H-1)

**Given** Wi-Fi is up
**When** the Hub connects to the Server
**Then** it bootstraps its clock over SNTP, validates the Server's public certificate including validity dates with mbedtls-rs `hook-wall-clock`, and CI builds with cmake and ninja available (H-2, AD-11, AD-13)

**Given** a connected Hub
**When** every 30–60 s elapse
**Then** it POSTs `/device/heartbeat`, authenticated by an HMAC over method, path, body hash, timestamp and nonce with its `hub-auth/v1` key, and adopts the `serverTime` from the response for Hub-local timing only (AD-12, FR13)

**Given** the Server receives a heartbeat
**When** it validates it
**Then** a bad HMAC, a timestamp more than ±5 min off or a replayed nonce is rejected, and a valid heartbeat updates the Hub's Device-grain last-seen time
**And** the Hub's JSON structs pass the golden fixtures generated from OpenAPI (AD-10)

**Given** the Hub firmware logic behind traits
**When** host-side tests run
**Then** BSSID selection, heartbeat scheduling, HMAC signing against vectors and reconnect backoff are covered

### Story 3.6: Add a Hub from my phone

As Simon,
I want to add a Hub from the iOS or Android app in a few guided steps,
So that it's online on my Site within a minute without touching a terminal.

**Acceptance Criteria:**

**Given** I am an Administrator or Owner on the mobile app
**When** I start Add a Hub
**Then** the five-step full-screen flow runs in the Setup flow shell (UX-DR39, UX-DR66):
1. Scan with Device candidate tiles (UX-DR37).
2. Setup code (UX-DR41).
3. Wi-Fi network rows, with WPA3-only rows not selectable and saying why (UX-DR42).
4. Site.
5. Progress, then outcome (UX-DR55).

**Given** the shared Kotlin core (Kable 0.45)
**When** it runs the setup client
**Then** it fetches `GET /enrolment-key` and shows the key's fingerprint, runs the AD-25 session with the entered code, sends Wi-Fi config and the Site, relays the sealed `K_dev` to the enrolment endpoint unread, and waits for the first heartbeat

**Given** a correct setup
**When** the Hub's first heartbeat arrives
**Then** the "Hub is online" outcome appears within 1 minute of sending the Wi-Fi credentials (FR1)

**Given** Bluetooth is off or permission denied, the code is wrong, the Wi-Fi password is wrong, the network is WPA3-only, or the Hub never reaches the Server
**When** the flow hits that case
**Then** the matching UX-DR94 error with its recovery step appears, and nothing on the Hub or Server is left half-configured

**Given** setup progress changes
**When** a screen reader is on
**Then** the UX-DR105 announcements are made, and the flow never times out while one is being read

**Given** the shared-core setup client in `tests/kt` with a mocked BLE layer
**When** unit tests run
**Then** every step transition and error above is covered, and snapshot tests cover each step in light and dark themes

**Given** the UX contract (DESIGN.md, EXPERIENCE.md)
**When** this story's surfaces and components are built
**Then** each of these has at least one named test, written failing before implementation, and they implement UX-DR40, UX-DR95, UX-DR103 as specified

### Story 3.7: See my Hub in Devices

As a Member of a Site,
I want to see the Site's Hubs with their status,
So that I know whether the garden's gateway is alive.

**Acceptance Criteria:**

**Given** a Site with an enrolled Hub
**When** I open Devices on web, iOS or Android
**Then** the Devices list shows a "Hubs" section with the Hub's ID, online state and last-seen time (UX-DR30), read from a projection of Device-grain events

**Given** the Hub's heartbeat stops
**When** I refresh Devices
**Then** the last-seen time is shown honestly, and the Hub is never shown as online past its last heartbeat (the Silent Alert itself arrives in Epic 7)

**Given** I use the web app
**When** I open Devices
**Then** the Inline notice "Adding a Hub or Node needs the Coldframe mobile app." replaces Add actions (UX-DR85)

**Given** I am a Member
**When** I open Devices
**Then** I see the list without admin actions (UX-DR84), and the list endpoints are in the authorization matrix

**Given** the UX contract (DESIGN.md, EXPERIENCE.md)
**When** this story's surfaces and components are built
**Then** each of these has at least one named test, written failing before implementation, and they implement UX-DR65 as specified

## Epic 4: See what my soil is doing

Simon presses a Node's button, assigns it to "Tomatoes", and sees its Readings: Lot tiles, Lot detail with the 30-day chart, battery and charging status, and last seen. Stale data is marked, and the app says when the Server is unreachable. Firmware is tested host-side only; power, radio reach and coexistence are verified manually against a checklist.

### Story 4.1: Node firmware foundation: wake, measure, sleep

As a maker building Coldframe,
I want the Node to wake every 15 minutes, measure its Sensors and go back to deep sleep,
So that it produces trustworthy Readings on a season's battery budget.

**Acceptance Criteria:**

**Given** `apps/rs/node` on ESP32-S3, reusing `packages/rs/crypto` for its eFuse identity (as in Story 3.2, including dev mode)
**When** it wakes on its 15-minute timer
**Then** it powers the soil probe through a switch, reads the ADC capacitive probe (raw value) and the BME680 in forced mode (temperature, humidity, gas resistance), reads battery voltage through a switched divider and charging status from the charger status pin, then powers everything off and deep-sleeps (NFR12, NFR13)
**And** all Readings from one wake carry the same `measured_at` and a monotonically increasing `reading_seq`, persisted across resets (AD-17, FR4)

**Given** battery voltage
**When** it is converted
**Then** battery % comes from a LiPo discharge-curve table and is marked approximate

**Given** the measurement, scheduling and battery-mapping logic behind hardware traits
**When** host-side tests run
**Then** the wake cycle, `reading_seq` persistence and the battery mapping are covered

**Given** a Node on the bench
**When** I follow the manual checklist
**Then** the average sleep current is at most about 100 µA on the chosen board (or the measured value is recorded against the budget in device-hardware.md), and the wake stays short (NFR4)

### Story 4.2: Node pairing: setup mode, enrolment and Lot assignment

As an Administrator,
I want a new Node to enter setup mode only when I press its button, and to be enrolled and assigned to a Lot,
So that I know exactly which physical Node I'm assigning and it doesn't waste battery advertising.

**Acceptance Criteria:**

**Given** an unassigned Node
**When** I long-press its setup button
**Then** it advertises the Coldframe setup service for a limited time, then stops; it never advertises otherwise (FR2, N-3, AD-25)
**And** a short press instead triggers an immediate Reading and report ("report now")

**Given** a setup session with the Node's setup code
**When** the AD-25 session runs
**Then** the Node returns its identity and `K_dev` sealed with HPKE to the Server's enrolment key, and a wrong code fails the session

**Given** an enrolment request with a Lot
**When** the Server processes it
**Then** the Device grain calls `Lot.Claim(nodeId)`, which succeeds only if the Lot exists, isn't removed and is free, then persists `DeviceEnrolled` and `DeviceAssigned` (AD-18)
**And** a Lot that already holds a Node rejects the claim with a clear error

**Given** the Node setup state machine and the Lot-claim logic
**When** host-side and Orleans TestCluster tests run
**Then** setup timeout, wrong code, occupied Lot and concurrent claims of one Lot (only one wins) are covered

**Given** a Node on the bench
**When** I follow the manual checklist
**Then** BLE setup and ESP-NOW work on the same Node, which validates Node coexistence (device-hardware open item)

### Story 4.3: Add a Node from my phone

As Simon in the garden,
I want to add a Node from the app by pressing its button and picking a Lot,
So that the right Lot starts reporting.

**Acceptance Criteria:**

**Given** I am an Administrator or Owner on iOS or Android
**When** I start Add a Node (from Devices, a *no Node* tile, or "Hub is online")
**Then** the five-step flow runs (UX-DR67):
1. "Press the setup button on the Node".
2. Nodes in BLE range, with "PRESSED JUST NOW" (UX-DR37).
3. Setup code.
4. Lot picker, including "+ New Lot"; Lots that already have a Node are not selectable (UX-DR38).
5. Outcome.

**Given** the setup succeeds
**When** `DeviceAssigned` is persisted
**Then** the outcome shows "‹Lot› has a Node", and the Lot tile changes from *no Node*
**And** its Sensors appear once the first Readings arrive (Stories 4.4 to 4.6)

**Given** BLE errors, a wrong code, the setup window timing out, or a Lot taken meanwhile
**When** the flow hits that case
**Then** the matching UX-DR94 error and recovery appear, and nothing is left half-assigned

**Given** the shared Kotlin core client with a mocked BLE layer
**When** unit and snapshot tests run
**Then** every step, error and theme variant is covered, with UX-DR105 announcements

### Story 4.4: ESP-NOW transport from Node to Hub

As Simon,
I want Nodes to reach the Hub over ESP-NOW from the far end of the garden, and to keep Readings until they're safely stored,
So that no Reading is lost when Wi-Fi can't reach a Lot.

**Acceptance Criteria:**

**Given** a Node with Readings to send
**When** it transmits
**Then** each frame is a Protobuf Node frame carrying `protocol_version`, `spec_hash` and its Readings, sealed with ChaCha20-Poly1305 under `seal/v1`, using a nonce built from the Device ID and a 64-bit counter
**And** the counter never repeats for the life of the key (flash reservation in blocks), and every resend is freshly sealed with a new counter (AD-12, AD-17)

**Given** the Hub receives a Node frame
**When** it relays it
**Then** it forwards the sealed frame, base64-encoded, in the JSON envelope of `POST /device/ingest` without reading it, and returns each opaque sealed downlink to the right Node over ESP-NOW; it stores no Readings (FR4, AD-9)

**Given** no valid sealed downlink acknowledgement within the 300 ms window
**When** the window expires
**Then** the Node keeps the Readings in its buffer (at least 24 h) and resends them on later wakes; it never treats radio-level send status as delivery (N-1, N-2)
**And** it collects a late acknowledgement on its next wake from the Hub's volatile downlink slot

**Given** repeated missed acknowledgements
**When** the threshold is reached
**Then** the Node re-scans channels and finds the Hub on its current channel, so reporting survives a router channel change (FR4, N-2)

**Given** framing, sealing, counter reservation, buffering and re-scan logic behind traits
**When** host-side tests run against the crypto-spec vectors
**Then** resend-with-new-counter, buffer overflow policy, counter continuity across reboots and channel re-scan are covered

**Given** a Node and a Hub on the bench and in the garden
**When** I follow the manual checklist
**Then** the Node reports reliably from the farthest Lot (NFR11), and buffered Readings arrive after the Hub was powered off for an hour

**Given** a sealed downlink carrying `serverTime`
**When** the Node applies it
**Then** the Node sets its RTC only from authenticated downlinks, slews gradually and never steps back more than 1 s at a time; host-side tests cover this, including forged or unauthenticated time being ignored (AD-11)

### Story 4.5: Server ingestion and acknowledgements

As Simon,
I want the Server to store every Reading exactly once and tell the Node only after it's safe,
So that my history is complete and never duplicated.

**Acceptance Criteria:**

**Given** a `POST /device/ingest` envelope from an authenticated Hub
**When** the Server processes each frame
**Then** it verifies the seal and the replay window (above the high-water mark, or in an unseen slot of the 64-entry window), then inserts the Readings keyed by `(device_id, sensor_id, reading_seq)` into the monthly-partitioned Readings table, and battery and charging status into device reports (AD-9, AD-17)
**And** it acknowledges only after the PostgreSQL commit, returning a per-frame status (`stored`, `duplicate`, `rejected_auth`, `rejected_replay`, `rejected_time`, `unknown_device`, `retry`) with a downlink sealed under `ack/v1` for `stored` and `duplicate` only

**Given** a resent frame with a new counter but Readings already stored
**When** it arrives
**Then** the status is `duplicate` and it is acknowledged, with no second row

**Given** each sealed downlink
**When** it is built
**Then** it carries `serverTime` and the acknowledged `reading_seq` ranges, and an empty `commands` field (AD-11, AD-16)
**And** Readings flagged `time_unsynced` are rebased from boot ID and uptime, and a `measured_at` more than 5 minutes in the future is `rejected_time`

**Given** partitions
**When** the migration job or scheduled maintenance runs
**Then** Readings and device-report partitions exist at least two months ahead, with a default partition as a safety net (AD-22)

**Given** Readings from a paused Device (fixture) or an unassigned Node
**When** they arrive
**Then** paused Readings are acknowledged and discarded, and unassigned Node Readings are stored but not evaluated (AD-8)

**Given** Server integration tests on the Aspire AppHost using crypto-spec vectors
**When** they run
**Then** every status path, the replay window, dedupe and acknowledgement-after-commit are covered, including a crash between insert and response (the resend is then `duplicate`)

**Given** a database restore (Story 2.3 runbook)
**When** the operator runs the documented restore command
**Then** every Device's replay-window high-water mark and downlink counter advance by a documented safety margin, so no nonce is reused (AD-15, AD-17)

**Given** frames relayed by a Hub
**When** the relaying Hub for a Node changes
**Then** the Node's Device grain persists its last relay Hub (used for Hub-silence suppression in Story 7.1)

### Story 4.6: Sensor Specifications and Sensor grains

As Simon,
I want each Node to tell the Server what it measures,
So that Readings are labelled correctly and soil moisture is ready for calibration.

**Acceptance Criteria:**

**Given** a frame with a `spec_hash` the Server doesn't know
**When** the Server replies
**Then** the downlink asks for the full Specification set, and the Node sends it once (AD-19)

**Given** a Specification set
**When** it is declared
**Then** each Sensor gets `sensorId = UUIDv5(deviceId:slot:quantity)`, and the Sensor grain stores quantity, unit, range, `calibration` flag and default Thresholds as `Default` (FR3)
**And** soil moisture declares `calibration: true` with default Thresholds; temperature, humidity and air quality (gas resistance in Ω) have none, so they are watched only

**Given** the same `spec_hash` again
**When** it arrives
**Then** nothing changes; a changed Specification updates defaults only and never an override, and a new quantity at a slot creates a new Sensor

**Given** Readings for a slot that hasn't been declared yet
**When** they arrive
**Then** they are stored and acknowledged, but not evaluated until the declaration arrives

**Given** Orleans TestCluster tests
**When** they run
**Then** declaration, redeclaration, a changed Specification and undeclared-slot handling are covered

### Story 4.7: Lot status and the Site overview

As Simon over morning coffee,
I want the overview to show each Lot's status at a glance, honestly,
So that I see what needs attention first and never mistake old data for current.

**Acceptance Criteria:**

**Given** the LotStatus projection (AD-14)
**When** it computes each Lot
**Then** it returns exactly one of `needsWater`, `needsCalibration`, `ok`, `unknown`, `paused`, `noNode`, using precedence noNode > paused > unknown > needsCalibration > needsWater > ok, plus `statusSince`, `lastReadingAt`, `unknownCause` and `pausedBy`
**And** it orders Lots needsWater, needsCalibration, unknown, ok, paused, noNode

**Given** the overview on web, iOS and Android
**When** it renders
**Then** every tile variant matches UX-DR17, UX-DR18 and UX-DR20 (shape, Carbon icon, text; never colour alone) in the Server's order
**And** `unknown`, `paused` and `needsWater` render correctly from fixture rows; they become live in Epics 7, 8 and 6

**Given** calibrating soil Sensors are uncalibrated at this point
**When** a Node reports
**Then** its Lot shows *needs calibration*, with no % value, derived from the Sensor's Calibration state (the same condition that opens the Epic 7 Uncalibrated Alert)

**Given** the Server can't be reached, or the data is older than the stale threshold
**When** the overview renders
**Then** the stale header and tile variant appear with "as of ‹time›", and no Reading is shown as current (UX-DR19, FR8)

**Given** the largest text sizes
**When** the grid renders
**Then** it falls back to one column (UX-DR97), and each tile has a complete screen-reader label (UX-DR98)

**Given** projection tests and UI snapshot tests
**When** they run
**Then** every precedence combination and every tile variant, in light and dark themes, is covered

**Given** the UX contract (DESIGN.md, EXPERIENCE.md)
**When** this story's surfaces and components are built
**Then** each of these has at least one named test, written failing before implementation, and they implement UX-DR12, UX-DR24, UX-DR77, UX-DR79, UX-DR80, UX-DR99, UX-DR106, UX-DR107, UX-DR108, UX-DR112, UX-DR128, UX-DR129 as specified

### Story 4.8: Lot detail with history and Device status

As a Member,
I want to open a Lot and see its latest Readings, a 30-day history and its Node's health,
So that I understand what the Lot has been doing.

**Acceptance Criteria:**

**Given** a Lot with a Node
**When** I open Lot detail on any platform (UX-DR63)
**Then** I see the hero with status, the latest Reading per Sensor (soil moisture raw and uncalibrated until Epic 5; °C, %RH, kΩ, each with its time), and the Node's battery %, charging status and last seen

**Given** the 30-day History chart
**When** it renders
**Then** it shows daily lows from the Readings table, with a text alternative for screen readers (UX-DR98)
**And** the Threshold band appears once Thresholds exist (Epic 5)

**Given** Devices
**When** I open it
**Then** the "Nodes" section lists each Node by Lot, with battery, charging status and last seen (UX-DR30)

**Given** history queries
**When** the API serves them
**Then** they take `from` and `to` and are cursor-paginated, and Readings are retained indefinitely (FR8)

**Given** API contract tests and UI snapshot tests
**When** they run
**Then** Lot detail and Devices are covered for each state, in light and dark themes

**Given** the UX contract (DESIGN.md, EXPERIENCE.md)
**When** this story's surfaces and components are built
**Then** each of these has at least one named test, written failing before implementation, and they implement UX-DR27, UX-DR28, UX-DR29, UX-DR32, UX-DR33, UX-DR78 as specified

### Story 4.9: Move or unassign a Node

As an Administrator,
I want to move a Node to another Lot or unassign it,
So that my garden layout can change without losing history.

**Acceptance Criteria:**

**Given** a Node on "Tomatoes"
**When** I move it to "Peppers" from Devices (web or mobile, no BLE needed)
**Then** the Device grain claims "Peppers", persists `DeviceMoved` and releases "Tomatoes" (the release is retried from persisted state until it succeeds) (AD-18)
**And** the Node's Reading history stays with the Node, and "Tomatoes" shows *no Node* (FR2)

**Given** the target Lot already holds a Node
**When** I try to move a Node there
**Then** the move is rejected, and nothing changes

**Given** I unassign a Node
**When** it is processed
**Then** the Lot is released, the Node's later Readings are stored but not evaluated, and the Node appears as unassigned in Devices

**Given** a Member
**When** they open Devices
**Then** move and unassign are not shown, the API rejects them with 403, and the endpoints are in the authorization matrix

**Given** Orleans TestCluster tests
**When** they run
**Then** move, a concurrent move to the same Lot, a failed release retried, and unassign are covered

**Given** the UX contract (DESIGN.md, EXPERIENCE.md)
**When** this story's surfaces and components are built
**Then** each of these has at least one named test, written failing before implementation, and they implement UX-DR31 as specified

## Epic 5: Calibrate the soil and set Thresholds

Simon calibrates the soil probe and sets Thresholds. Lots show approximate % and *OK*, and the chart shows the Threshold band. *Needs water* goes live in Epic 6, because it is defined by an open low-side Threshold Alert (AD-14).

### Story 5.1: Calibration on the Server

As an Administrator,
I want to record dry and wet reference points from stored Readings,
So that raw probe values become an approximate 0–100 % soil moisture.

**Acceptance Criteria:**

**Given** a `calibration: true` Sensor with stored Readings
**When** an Administrator submits a dry point and a wet point over REST, each naming the stored Reading's `reading_seq` (FR9, AD-9)
**Then** the Sensor grain persists `SensorCalibrated` with a new Calibration ID and both raw values
**And** it synchronously sets the Calibration in force on the Device grain before confirming, and re-delivers from persisted state if that call fails

**Given** a Calibration in force
**When** new Readings arrive
**Then** each is stored with that Calibration ID, and its normalized % is derived from that Calibration and rounded to the nearest 5 % for display

**Given** a recalibration
**When** it is saved
**Then** only Readings after it use the new Calibration; history keeps the Calibration it was recorded with, and Threshold % values are unchanged

**Given** a half-finished Calibration (only the dry point)
**When** it is saved
**Then** the Sensor stays uncalibrated, and the dry point is kept until the wet point arrives

**Given** a dry point that isn't meaningfully distinct from the wet point (for example, identical raw values)
**When** it is submitted
**Then** it is rejected with Problem Details

**Given** a Member caller
**When** they submit a Calibration
**Then** it is rejected with 403, and the endpoint is in the authorization matrix

**Given** Orleans TestCluster and integration tests
**When** they run
**Then** the flows above, the redelivery of Calibration in force, and the Lot leaving *needs calibration* when the Calibration is saved (its % appears with the next Reading) are covered

### Story 5.2: Calibrate from the app

As Simon with the probe in my hand,
I want a guided two-step calibration that works on my phone or laptop,
So that I can calibrate in seconds using the Node's button.

**Acceptance Criteria:**

**Given** a *needs calibration* Lot and an Administrator or Owner
**When** I start Calibrate from the tile, Lot detail or the Node-added outcome
**Then** the two-step flow (dry, then wet) runs on web, iOS and Android (UX-DR68, UX-DR43), with no BLE involved

**Given** the dry step
**When** I put the probe in dry soil and short-press the Node's button (N-3), or wait
**Then** the screen shows "Waiting for the next Reading", with the last raw value and its time, and enables "Record dry" when a Reading taken after the step started arrives
**And** I can instead pick a recent stored Reading from the list

**Given** both points are recorded
**When** I confirm
**Then** the confirmation shows the dry and wet raw values and says the % appears with the next Reading, updating in place when that Reading arrives

**Given** I leave mid-flow
**When** I come back later
**Then** the recorded dry point is kept, and I resume at the wet step

**Given** a paused Device
**When** I open Calibrate
**Then** it explains that Readings resume after the Pause ends, instead of waiting indefinitely (UX-DR86); tested with a fixture `pausedBy` until Epic 8 makes it live

**Given** a screen reader is on
**When** a fresh Reading arrives
**Then** the waiting announcement from UX-DR105 is made

**Given** a Member
**When** they open the Lot
**Then** Calibrate isn't shown (UX-DR84)

**Given** snapshot and client tests
**When** they run
**Then** each step, the resume path and the paused explanation are covered, in light and dark themes

**Given** the UX contract (DESIGN.md, EXPERIENCE.md)
**When** this story's surfaces and components are built
**Then** each of these has at least one named test, written failing before implementation, and they implement UX-DR44, UX-DR87 as specified

**Given** a Sensor with `calibration: false`
**When** its Lot detail is shown
**Then** no Calibrate control appears (FR3)

### Story 5.3: Thresholds on the Server

As an Owner or Administrator,
I want to set, change and clear a Sensor's low and high Thresholds,
So that Coldframe knows when a Lot is too dry or too wet, and can alert on other Sensors if I choose.

**Acceptance Criteria:**

**Given** a Sensor
**When** Thresholds are set
**Then** the Sensor grain validates them: low is required on an alerting Sensor, high is optional (empty never alerts), and low must be below high; anything else is rejected (FR10, AD-19)
**And** each side is stored as `Default`, `Override(value)` or `Cleared`, and a later Specification redeclaration never replaces an override

**Given** a watched Sensor with no Specification default (for example, temperature)
**When** an Owner or Administrator turns on alerts
**Then** the proposed low is `Min + 20 % × (Max − Min)` of the Sensor's range, with no proposed high

**Given** a calibrating Sensor
**When** Thresholds are read or written
**Then** they are in 0–100 %

**Given** a Threshold change
**When** it is saved
**Then** the Sensor grain raises a Thresholds-changed event, which Story 6.1 treats as a new evaluation epoch

**Given** a Member caller
**When** they try to change Thresholds
**Then** it is rejected with 403, and the endpoints are in the authorization matrix

**Given** TestCluster tests
**When** they run
**Then** every validation rule, the three states, the 20 % proposal and the override surviving a redeclaration are covered

### Story 5.4: Set Thresholds in the app and see them on the chart

As Simon,
I want to set Thresholds visually and see them on the history chart,
So that I understand where "too dry" starts for each Lot.

**Acceptance Criteria:**

**Given** an Owner or Administrator on any platform
**When** I open Thresholds from Lot detail or right after Calibration
**Then** the Thresholds modal shows a Threshold column per Sensor, with low required and high optional ("Add high" / clear), in 5 % steps for calibrated soil moisture (UX-DR69, UX-DR45)
**And** "Low must stay below high." appears inline, and Save is disabled while it's invalid

**Given** saved Thresholds
**When** I view Lot detail
**Then** the 30-day History chart shows the Threshold band, and daily lows below the low Threshold use the below-low token (UX-DR5)
**And** a calibrated, in-range Lot shows *OK* with ~% on its tile

**Given** a Member
**When** they open Lot detail
**Then** Thresholds are visible read-only, with no edit control (UX-DR84)

**Given** snapshot tests and a Playwright end-to-end test (calibrate, then set a low Threshold, then the tile shows ~% *OK*)
**When** they run
**Then** they pass in light and dark themes, at the largest text size

**Given** the UX contract (DESIGN.md, EXPERIENCE.md)
**When** this story's surfaces and components are built
**Then** each of these has at least one named test, written failing before implementation, and they implement UX-DR91 as specified

## Epic 6: Get told when to water

A morning push says "Tomatoes needs water" inside Simon's Notification Window. Reminders repeat while the Lot stays dry, anything held overnight arrives as one summary, and a Site can be muted. The web app shows browser notifications while open.

### Story 6.1: Threshold Alerts open and close

As Simon,
I want Coldframe to open an Alert when a Lot stays beyond a Threshold and close it when it recovers,
So that one hovering Reading never cries wolf and a watered Lot clears itself.

**Acceptance Criteria:**

**Given** a calibrated Sensor with Thresholds
**When** three consecutive Readings are below its low Threshold (or above its high one)
**Then** the Sensor grain persists a new episode and calls the idempotent `Alert.Open` with `alertId = UUIDv5(subjectKind:subjectId:threshold:episode)`, recording the side crossed (FR11, AD-7)
**And** one or two Readings on the other side change nothing, and at the 15-minute interval an Alert opens about 30 minutes after the first crossing

**Given** an open Threshold Alert
**When** three consecutive Readings are back within the Thresholds
**Then** only the Sensor grain that opened it closes it, with reason `recovered`

**Given** Readings arrive out of order or as a backlog
**When** they are evaluated
**Then** evaluation runs in `measured_at` order, and a Reading older than the grain's `lastEvaluatedAt` is stored but not evaluated

**Given** an uncalibrated calibrating Sensor, or a Sensor without Thresholds
**When** Readings arrive
**Then** no Threshold Alert opens (FR9, FR21)

**Given** a Threshold change or a new evaluation epoch
**When** it is applied
**Then** streaks reset

**Given** an open low-side Alert on a soil-moisture Sensor
**When** the LotStatus projection updates
**Then** the Lot shows *needs water* and sorts first

**Given** Alert events
**When** they are published
**Then** they go out per Site, and the Site grain keeps the set of open Alerts, which is rebuildable from the journal (AD-7, AD-21)

**Given** TestCluster tests
**When** they run
**Then** they cover exactly-three opening and closing, flapping around the Threshold, a retried evaluation producing no duplicate Alert, the low-to-high switch, out-of-order backlog, and the 2-of-3 no-change cases

### Story 6.2: See Alerts in the apps

As a Member,
I want to see open and recently closed Alerts,
So that I know what's wrong now and what resolved itself.

**Acceptance Criteria:**

**Given** open Alerts on a Site
**When** I open Alerts on web, iOS or Android
**Then** Threshold Alerts are grouped before Health Alerts, newest first, and closed Alerts from the last 7 days sit in "Closed" (UX-DR64)
**And** only low-side soil-moisture Alerts ("‹Lot› needs water") use orange; "too wet" and other Sensors use the non-orange treatment (UX-DR14)

**Given** an Alert row
**When** I tap it
**Then** it opens Lot detail, and the row's screen-reader label states the condition and when it started (UX-DR98)

**Given** no open Alerts
**When** Alerts renders
**Then** it shows "No open Alerts." and never "All good" (UX-DR82)

**Given** the mobile Alerts tab
**When** Alerts are open
**Then** the tab label carries the count ("Alerts · 5")

**Given** an Alerts read model with close reasons
**When** the API serves it
**Then** it is paginated and filtered by Site, and it is in the authorization matrix

**Given** snapshot and API contract tests
**When** they run
**Then** each row variant, both groups and the empty state are covered, in light and dark themes

**Given** the UX contract (DESIGN.md, EXPERIENCE.md)
**When** this story's surfaces and components are built
**Then** each of these has at least one named test, written failing before implementation, and they implement UX-DR25, UX-DR26 as specified

### Story 6.3: My notification settings and the Site Reminder cadence

As Simon,
I want to set when I'm bothered, in my own time zone, and mute a Site,
So that Coldframe fits my day and doesn't wake me at night.

**Acceptance Criteria:**

**Given** a new User
**When** they first reach a Site
**Then** their Notification Window defaults to 07:00–22:00, and their IANA time zone is proposed from the phone OS or the browser (IP only as a last resort), for them to confirm or change (FR16, AD-11)
**And** a zone the User chose themselves is never overwritten by detection

**Given** My notifications (UX-DR72) on web, iOS or Android
**When** I edit it
**Then** I can set the window ("from 07:00" keeps the 22:00 end), confirm or change the time zone, mute or unmute this Site (FR17), and pick my Reminder cadence ("Use Site setting" / "Daily" / "Every 2 days") (UX-DR50)
**And** every change is persisted as an event on my User grain

**Given** Site settings
**When** an Owner or Administrator sets the Site Reminder cadence ("Daily" / "Every 2 days")
**Then** it is stored on the Site grain and cached in each member's User grain (FR12)

**Given** a Member
**When** they open Site settings
**Then** the Site cadence is read-only, and my own settings affect only me

**Given** tests
**When** they run
**Then** they cover time-zone detection precedence, never overwriting a chosen zone, the defaults, and mute scope (only me)

**Given** the UX contract (DESIGN.md, EXPERIENCE.md)
**When** this story's surfaces and components are built
**Then** each of these has at least one named test, written failing before implementation, and they implement UX-DR47, UX-DR48, UX-DR49 as specified

### Story 6.4: Delivery timing: windows, summaries and Reminders

As Simon,
I want Alerts that fall due overnight gathered into one morning summary, and Reminders while a Lot stays dry,
So that I'm told once, at the right time, and not every hour.

**Acceptance Criteria:**

**Given** an Alert opens on a Site I belong to
**When** it is inside my Notification Window and I haven't muted the Site
**Then** my User grain hands it to the Notifier seam at once (AD-7)

**Given** Alerts and Reminders fall due outside my window
**When** my window opens (in my time zone, correct across daylight-saving changes)
**Then** I get one summary with at most one entry per open Alert, and an Alert that opened and closed while held is dropped (FR16)

**Given** an open Threshold Alert
**When** my resolved cadence elapses (User setting → Site setting → once per day)
**Then** a Reminder falls due (delivered now, or held for the window), and a closed Alert sends none (FR12)

**Given** Health Alerts (used by Epic 7)
**When** Reminders are scheduled
**Then** the interval is `max(resolved, 24 h)`

**Given** deadlines
**When** they are stored
**Then** every window opening and Reminder is a persisted UTC `due-at` on the User grain, the Orleans Reminder is only a wake-up, and each wake or activation processes everything overdue (AD-6)

**Given** Membership events
**When** I'm granted or removed from a Site
**Then** my User grain updates its Site set, pulls that Site's open Alerts on join, drops them on removal, and reconciles against `Site.OpenAlerts()` on activation

**Given** TestCluster tests with a fake clock and a Notifier test double
**When** they run
**Then** they cover in-window delivery, the overnight summary (one entry per Alert, closed-while-held dropped), cadence precedence, mute, a daylight-saving change, a silo restart across 07:00 (the summary is still sent), and a join or leave while Alerts are open

### Story 6.5: Push notifications on my phone

As Simon,
I want Alerts as push notifications that make sense on the lock screen,
So that I can act away from home without opening the app.

**Acceptance Criteria:**

**Given** the mobile app signed in
**When** I first land on a Site's overview (as creator or new Member)
**Then** the app asks for notification permission with one line of why, and registers its push token through the shared Kotlin core; the User grain owns the tokens (UX-DR115)

**Given** the Notifier seam
**When** it delivers to my devices
**Then** the APNs and FCM adapters send self-contained payloads (Lot, Sensor or Device, and the condition in plain words, for example "Tomatoes needs water — ~20 % in the soil, your low is 30 %"), grouped per Site, with no app-icon badge (FR15, UX-DR116 to UX-DR121)
**And** the adapters contain no timing or filtering logic, and a token the provider reports as invalid is removed from my User grain

**Given** a morning summary
**When** it arrives
**Then** it is one notification with one line per open Alert

**Given** I tap a notification
**When** the app opens
**Then** it deep-links to Lot detail (Threshold) or to the overview (summary)

**Given** notification permission is denied or revoked
**When** I open the app
**Then** My notifications shows a persistent notice with a link to OS settings, and the overview shows a hint (UX-DR88)

**Given** adapter tests (APNs/FCM mocked) and an integration test on the Aspire AppHost
**When** they run
**Then** they cover payload content and grouping, invalid-token cleanup, and the permission-denied state
**And** a manual checklist confirms a real push on an iPhone and an Android phone

**Given** the UX contract (DESIGN.md, EXPERIENCE.md)
**When** this story's surfaces and components are built
**Then** each of these has at least one named test, written failing before implementation, and they implement UX-DR122 as specified

### Story 6.6: Browser notifications and live updates on the web

As Simon with the web app open,
I want Alerts to pop up and screens to refresh live,
So that I see changes without reloading.

**Acceptance Criteria:**

**Given** the web app open
**When** it connects
**Then** the browser reaches the Server's SignalR hub only through the SvelteKit backend-for-frontend proxy, which attaches my access token (AD-14)

**Given** I turn on "Browser notifications while Coldframe is open" in My notifications
**When** the browser asks for permission and I allow it
**Then** `notification.delivered` messages from the Notifier appear as browser notifications, following the same window and mute rules (applied on the Server) (FR20)
**And** nothing is delivered when no tab is open, because there is no background web push

**Given** a read model changes
**When** the Server emits `readmodel.changed { resource, id, version }`
**Then** the open page refetches that resource over REST; hints carry no domain data

**Given** the message shapes
**When** CI runs
**Then** SignalR messages match `packages/asyncapi`, and the TypeScript client types are generated from it (AD-10)

**Given** a Playwright end-to-end test of UJ-2 (a Reading series crosses the low Threshold, the Alert opens, a browser notification arrives inside the window, recovery closes the Alert, and no Reminder follows)
**When** it runs against the Aspire AppHost with a fake clock
**Then** it passes

**Given** the UX contract (DESIGN.md, EXPERIENCE.md)
**When** this story's surfaces and components are built
**Then** each of these has at least one named test, written failing before implementation, and they implement UX-DR89, UX-DR123 as specified

## Epic 7: Know when something breaks

A silent Node or Hub, a low battery, or an uncalibrated Sensor raises a Health Alert, capped at one Reminder per day. A silent Hub is never reported once per Node. Silence must never look like "all fine".

### Story 7.1: Silent Device Alert on the Server

As Simon,
I want Coldframe to notice when a Node or Hub goes quiet,
So that a dead Device is never mistaken for a healthy Lot.

**Acceptance Criteria:**

**Given** an enrolled Device
**When** nothing is accepted from it for longer than its Silence Window (defaults: Node 6 h, Hub 5 min, overridable per Device)
**Then** the Device grain opens a Health Alert of kind `silent` with a deterministic ID, and closes it with reason `recovered` when the Device reports again (FR13, AD-7)

**Given** silence measurement
**When** it runs
**Then** a Node's liveness is the latest `measured_at` it has had accepted, never arrival time, and silence counts from the latest of last accepted report, last Server start and last resume, so Server downtime never counts as Device silence (AD-6)
**And** every Silence Window expiry is a persisted `due-at`, processed on each wake and activation

**Given** a Node whose last relay Hub has an open `silent` Alert
**When** the Node's own window expires
**Then** no Alert opens for the Node, and its window restarts when the Hub recovers; a silent Hub is reported as the Hub only (FR13)

**Given** an open `silent` Alert
**When** the LotStatus projection updates
**Then** the Lot shows *unknown*, with `unknownCause` `node` or `hub`

**Given** Health Alerts
**When** Reminders are scheduled (Story 6.4)
**Then** they come at most once per day

**Given** TestCluster tests with a fake clock
**When** they run
**Then** they cover Node and Hub default windows, a per-Device override, recovery, a Server restart that doesn't cause false silence, Hub-silence suppression with 5 Nodes (one Alert), and suppression lifting on Hub recovery

### Story 7.2: Silence in the apps and Silence Window settings

As a Member,
I want silent Devices shown unmistakably,
So that I never read an old value as current.

**Acceptance Criteria:**

**Given** a Lot with status *unknown*
**When** the overview renders
**Then** the tile is hatched, with a large duration ("6 h") and "was ~40 % at 01:05", sorted above every *OK* Lot (UX-DR18)

**Given** a silent Hub
**When** the overview renders
**Then** an Inline notice above the tiles reads "Hub 3F2A silent for 12 min — Lots behind it can't be read.", and the affected Lots show *unknown* with the Hub as cause (UX-DR81)

**Given** Devices on web, iOS or Android
**When** a Device is silent
**Then** its row shows silence and last seen, and an Administrator or Owner can change its Silence Window; a Member can't (UX-DR84, 403 on the API)

**Given** push and summary copy
**When** a silent Alert is delivered
**Then** it reads "Node on Lot 'Beans' silent for 6 h" or "Hub 3F2A silent for 5 min" (UX-DR117)

**Given** a Playwright end-to-end test of UJ-3 (a Node stops, and after 6 h the summary and the *unknown* tile appear) with a fake clock, plus snapshot tests
**When** they run
**Then** they pass in light and dark themes

### Story 7.3: Low-battery Alert

As Simon,
I want to know when a Node's battery is running out and it isn't charging,
So that I can clear the shade from the panel or recharge it before it dies.

**Acceptance Criteria:**

**Given** device reports from a Node
**When** three consecutive reports show battery below 20 % while *not charging*
**Then** the Device grain opens a Health Alert of kind `battery`, showing level and charging status (FR14)
**And** three consecutive reports that are *charging* or at 20 % or above close it

**Given** below 20 % while *charging*, or *not charging* above 20 %
**When** reports arrive
**Then** no Alert opens

**Given** the Alert
**When** it is shown or pushed
**Then** it reads, for example, "Node battery 14 %, not charging" on Alerts, Devices and Lot detail (UX-DR117)

**Given** TestCluster tests
**When** they run
**Then** they cover exactly-three open and close, charging suppression, and flapping around 20 %

### Story 7.4: Uncalibrated Sensor Alert

As an Owner or Administrator,
I want to be told when a newly assigned Node's soil Sensor hasn't been calibrated,
So that I don't forget the step that makes its Readings meaningful.

**Acceptance Criteria:**

**Given** a Node is assigned to a Lot, and one of its `calibration: true` Sensors has no Calibration
**When** the Sensor grain receives the evaluation context from the Device grain
**Then** it opens a Health Alert of kind `uncalibrated` naming the Lot and the Sensor (FR21, AD-8)

**Given** the Sensor is calibrated
**When** the Calibration is saved
**Then** the Alert closes with reason `calibrated`

**Given** the Node is unassigned while the Alert is open
**When** the new evaluation context arrives
**Then** the Sensor grain closes its own Alert with reason `unassigned`

**Given** the Alert
**When** it is delivered
**Then** it reads "Soil sensor on Lot 'Peppers' needs calibration", with "An Owner or Administrator can calibrate it.", and deep-links to Lot detail, where Calibrate is offered to Administrators

**Given** TestCluster tests
**When** they run
**Then** they cover open on assignment, close on calibration, close on unassignment, and no Threshold Alerts while uncalibrated

## Epic 8: Pause for maintenance and winter

Simon pauses a Device or the whole Site, optionally until a date, without false alarms. It resumes automatically.

### Story 8.1: Pause and resume on the Server

As an Administrator,
I want to pause a Device or the whole Site, optionally until a date,
So that maintenance and winter storage never raise false Alerts.

**Acceptance Criteria:**

**Given** a Device
**When** an Administrator pauses it
**Then** the Device grain adds `device` to its `pausedBy` set, with an optional end date (FR18, AD-8)

**Given** a Site
**When** an Administrator pauses it
**Then** the Site grain owns the Site Pause and its end date, and propagates it idempotently to every Device in its roster, re-delivering from persisted state until acknowledged
**And** a Device enrolled into a paused Site starts paused, with `pausedBy` = site

**Given** a Device becomes paused
**When** the new evaluation epoch reaches its Sensor grains
**Then** each Sensor grain closes its own open Alerts with reason `paused` and resets streaks, and the Device grain closes its `silent` and `battery` Alerts
**And** Readings from the paused Device are acknowledged and discarded (AD-8, AD-9), and no Alert opens

**Given** a Device paused both individually and by its Site
**When** only one source is lifted
**Then** it stays paused; it resumes only when both are lifted

**Given** a Pause end date
**When** it is set
**Then** it resolves to 00:00 on that date in the time zone of the User who set the Pause, stored as a UTC `due-at`, and resumes automatically even across a silo restart (AD-6)

**Given** a resume
**When** it happens
**Then** the Silence Window restarts from the moment of resume (FR18), and the LotStatus projection shows *paused* with `pausedBy` while paused

**Given** a Member caller
**When** they try to pause or resume
**Then** it is rejected with 403, and the endpoints are in the authorization matrix

**Given** TestCluster tests with a fake clock
**When** they run
**Then** they cover Device pause and resume, Site pause propagating to all Devices, redelivery after a failure, both sources held, an end date across a restart, Alerts closing on pause, no false silence after resume, and a Device added to a paused Site starting paused

### Story 8.2: Pause and resume in the apps

As Simon in October,
I want to pause the whole Site from the overview and see clearly that it's paused,
So that I know the silence over winter is intentional.

**Acceptance Criteria:**

**Given** an Administrator or Owner
**When** I choose Pause, on web, iOS or Android, from Lot detail (Device), a Devices row, or the Site menu (Site)
**Then** the Pause sheet opens, with the scope (this Device / whole Site) and an optional "Until" date (UX-DR46, UX-DR70)

**Given** the Site is paused
**When** the overview renders
**Then** every tile shows the purple-outline *paused* variant, and the headline reads "Paused until 1 Mar" (or "Paused"), with "No Alerts are sent while paused." (UX-DR83)
**And** a Device paused only by the Site shows "Paused by Site" (from `pausedBy`)

**Given** the Site is paused
**When** I choose Resume from the Site menu (or the Pause ends automatically)
**Then** the tiles return to their live statuses

**Given** a Member
**When** they view a paused Site
**Then** they see the paused state but no Pause or Resume controls (UX-DR84)

**Given** a Playwright end-to-end test of UJ-4 (pause the Site until a date; no Health Alerts arrive while Nodes are silent; it resumes automatically on the date), with a fake clock, plus snapshot tests
**When** they run
**Then** they pass in light and dark themes

## Epic 9: Share the garden

Simon invites the neighbour by email as a Member and changes or removes Roles; a Site always keeps an Owner. Members get Alerts but see no admin controls.

### Story 9.1: Invite someone to my Site

As Simon before a holiday,
I want to invite my neighbour by email with a Role,
So that they get the same Alerts while I'm away.

**Acceptance Criteria:**

**Given** I am the Owner of a Site
**When** I submit the Invite form, on web, iOS or Android, with an email address and a Role (Member, Administrator or Owner), whose description updates as I choose (UX-DR52)
**Then** the Site grain sends a Phase Two native invitation for that Organization and Role; the Server keeps no invitation state (FR7, AD-3)
**And** the form confirms "Invitation sent", with the hint to open the link on the garden's Wi-Fi

**Given** the invitation email (a Keycloak/Phase Two template in the deployment)
**When** the invitee opens it
**Then** the email says acceptance needs the home network, and the link leads to the Keycloak/Phase Two page, where they sign in or register and accept

**Given** the invitee accepts
**When** the membership event reaches the Site and User grains through the Keycloak → Temporal → Orleans pipeline
**Then** they appear in Members with their Role, their User grain gains the Site and pulls its open Alerts, and their app opens on that Site's overview, asking for notification permission there (Story 6.5)

**Given** an Administrator or Member
**When** they try to invite
**Then** the Invite form isn't shown, and the API rejects the call with 403

**Given** integration tests on the Aspire AppHost with Keycloak and Phase Two
**When** they run
**Then** invite, then accept, then Membership in the projection, then the new Member is authorized per Role are covered, plus the authorization-matrix entries

**Given** the UX contract (DESIGN.md, EXPERIENCE.md)
**When** this story's surfaces and components are built
**Then** each of these has at least one named test, written failing before implementation, and they implement UX-DR90 as specified

### Story 9.2: Change and remove Roles, always keeping an Owner

As an Owner,
I want to change or remove someone's Role,
So that access matches who helps in the garden, and the Site is never left without an Owner.

**Acceptance Criteria:**

**Given** Members (UX-DR73) on web, iOS or Android
**When** I, as an Owner, change a person's Role or remove them and confirm the destructive dialog, which names the person
**Then** the Site grain checks FR7 against its persisted Owner set, calls Phase Two, persists the result and updates the identity projection immediately

**Given** a change would leave the Site with no Owner
**When** it is submitted, including two Owners demoting each other at the same time
**Then** it is rejected, because Site-grain calls are serialized; at least one Owner always remains (FR7, AD-3)

**Given** a Role is lowered or removed
**When** the person makes their next request
**Then** it is authorized with the new Role or rejected, without waiting for token expiry (AD-4)
**And** a removed person's User grain drops the Site and its held notifications

**Given** a break-glass edit in Keycloak's admin console that would leave a Site with no Owner
**When** the event arrives through the pipeline
**Then** the Site grain keeps its last valid Owner set for authorization and raises an operator-visible error, and never silently repairs it

**Given** a Member
**When** they open the Site
**Then** they see every read surface, receive Alerts in their own Notification Window, and see no Thresholds, Calibrate, Pause, Devices or Members admin controls (UX-DR84)

**Given** NFR6
**When** the generated authorization matrix and multi-Site tests run (a person who is Owner on Site A and Member on Site B)
**Then** every endpoint enforces Roles per Site, and nothing on Site A grants anything on Site B

**Given** a Playwright end-to-end test of UJ-5 (invite, accept, the neighbour gets "Cucumbers needs water" in their own window, and sees no admin controls)
**When** it runs against the Aspire AppHost
**Then** it passes

**Given** the UX contract (DESIGN.md, EXPERIENCE.md)
**When** this story's surfaces and components are built
**Then** each of these has at least one named test, written failing before implementation, and they implement UX-DR51 as specified

## Epic 10: Let others rebuild Coldframe

Another maker builds a Node and Hub and deploys the stack from the public docs alone (NFR7, NFR8). Documentation stories are test-first where it applies: link checks, doc build and command snippets exercised in CI; the full reproduction is verified manually.

### Story 10.1: Releases with one version and firmware binaries

As a maker adopting Coldframe,
I want each release to bundle matching images, charts and firmware under one version,
So that I know which pieces belong together.

**Acceptance Criteria:**

**Given** a release tag `vX.Y.Z`
**When** the release workflow runs
**Then** it publishes the images (Story 2.1), sets each Helm chart's `appVersion` to `X.Y.Z`, attaches Hub and Node firmware binaries (release builds, never dev mode) with checksums, and publishes release notes grouped by conventional-commit type (AD-23)

**Given** a release that changes a wire protocol major version
**When** the release workflow runs
**Then** it fails unless the Server still accepts the previous major version (AD-10)

**Given** the mobile apps
**When** a release is cut
**Then** no store binaries are published; the release notes point to the build-your-own-apps guide (Story 10.3)

**Given** CI
**When** the release workflow is tested on a dry-run tag
**Then** every artifact is produced and the checksums verify

### Story 10.2: Build a Node and a Hub from the docs

As a maker,
I want complete hardware and flashing instructions,
So that I can build working Devices without asking anyone.

**Acceptance Criteria:**

**Given** `hardware/`
**When** I open it
**Then** it contains, under Apache-2.0 (NFR8):
- the Node and Hub schematics
- the bill of materials (ESP32-S3, capacitive soil probe, BME680, LiPo 800 mAh, solar panel, a charger IC with NTC input and a status pin)
- the wiring, including the switched probe power and the battery divider
- enclosure notes, and probe sealing guidance

**Given** `docs/build/`
**When** I follow it
**Then** I can flash the Hub and Node over USB with `espflash` from release binaries or from source, see the setup code printed at first boot, and understand the eFuse key burn
**And** a prominent warning explains that the eFuse burn is irreversible and how to use dev mode on test boards (AD-12)

**Given** the power and placement guidance
**When** I read it
**Then** it covers panel placement above the canopy, the sleep-current budget (about 100 µA or less) and how to measure it, and the approximate nature of soil moisture and battery %

**Given** CI
**When** the docs build
**Then** links and referenced files resolve, and the documented `espflash` and build commands run in a CI job against the firmware crates (build only, no flashing)

### Story 10.3: Deploy Coldframe at home and build my own apps

As a maker,
I want one end-to-end guide from domain to phone,
So that I can run the whole stack and my own apps at home.

**Acceptance Criteria:**

**Given** `docs/deploy/`
**When** I follow it
**Then** it walks through, in order:
1. Domain and DNS API token.
2. RKE2 and Fleet install.
3. Creating every Secret from `deploy/SECRETS.md` (SMTP, APNs/FCM, DNS-01, enrolment key, S3 backups).
4. Keycloak realm and Phase Two setup, including the invitation email template with the home-network hint.
5. Split DNS.
6. The first sign-in.

It links to the operations runbooks from Epic 2.

**Given** the mobile apps
**When** I follow the build-your-own-apps section
**Then** I can set my Server URL and Keycloak issuer as build-time configuration (AD-23), use my own APNs key and FCM project for push, and install the apps on my phones

**Given** `deploy/compose/`
**When** I read it
**Then** it provides a Compose example clearly labelled as a reference example only, not the supported path (NFR7)

**Given** CI
**When** the docs build
**Then** links resolve, and the Compose file validates (`docker compose config`)

### Story 10.4: Firmware learning reference

As a maker learning embedded Rust,
I want the firmware documented as a readable reference,
So that I can learn ESP-NOW, BLE provisioning and deep sleep from a real system (NFR8).

**Acceptance Criteria:**

**Given** `docs/firmware/`
**When** I read it
**Then** it explains, with pointers to the code:
- ESP-NOW transport and channel following
- the BLE setup protocol with its proof-of-possession code (AD-25)
- the eFuse identity and key hierarchy (AD-12)
- frame sealing, counters and replay (AD-17)
- deep sleep and the power budget
- the report-now and setup button presses (N-3)
- the radio coexistence findings from the spike

**Given** the firmware crates
**When** `cargo doc` runs in CI
**Then** public items in `packages/rs/*` are documented, and the doc build has no warnings

### Story 10.5: Someone else reproduces Coldframe

As Simon,
I want a person other than me to rebuild Coldframe from the public docs,
So that I know the "reproducible from the docs" promise holds (SM-5).

**Acceptance Criteria:**

**Given** the public repository and docs only
**When** a person other than the author, or the author on a clean machine and fresh hardware with no prior state, follows Stories 10.2 and 10.3
**Then** they build a Node and a Hub, deploy the stack, install their own apps, add the Hub and Node, calibrate, and receive a Threshold Alert inside their Notification Window
**And** every point where they got stuck is recorded as an issue and fixed in the docs before this story is closed

**Given** the manual reproduction checklist in `docs/`
**When** it is completed
**Then** its results are recorded in the repository
