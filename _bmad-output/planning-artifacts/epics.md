---
stepsCompleted: []
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

- `apps/`, `packages/`, `aspire/`, `deploy/`, `hardware/` and `docs/`
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

{{requirements_coverage_map}}

## Epic List

{{epics_list}}
