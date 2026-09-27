---
id: SPEC-coldframe
companions:
  - glossary.md
  - acceptance-criteria.md
  - user-journeys.md
  - device-hardware.md
  - ../../planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md
sources:
  - ../../planning-artifacts/prds/prd-coldframe-2026-09-25/prd.md
  - ../../planning-artifacts/prds/prd-coldframe-2026-09-25/addendum.md
  - ../../../docs/spikes/hub-radio-coexistence.md
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate. Source documents listed in frontmatter are for traceability — consult them only if you need narrative rationale or prose color this contract intentionally omits.

# Coldframe V1: self-hosted garden watering alerts

Terms (Site, Lot, Node, Hub, Sensor, Reading, Threshold, Alert, Reminder, Notification Window, Silence Window, Pause, Roles) are defined in `glossary.md`. The architecture spine is authoritative on *how*; its `AD-n` IDs are stable and cite-able.

## Why

A **vision to realize**, built in the open. A gardener wants to be told which bed needs water **while there is still time to act**, based on measured soil rather than weather estimates, without checking by hand, and without a vendor cloud. Existing pieces (sensors, ESP32 radios, OIDC, push) exist separately; Coldframe's value is combining them into one open, self-hosted, reproducible system. It must be equally honest about its own failures: silence from a broken Device must never read as "all fine". It also serves makers as a real end-to-end Rust-on-ESP32 reference. V1 only notifies. Data and command paths stay open for later irrigation control, with the gardener still in control.

## Capabilities

CAP-N matches PRD FR-N. Line-item acceptance criteria and defaults per capability are in `acceptance-criteria.md`.

- **CAP-1**
  - **intent:** An Administrator gives a Hub its Wi-Fi credentials from the iOS or Android app over BLE and binds it to a Site.
  - **success:** A freshly flashed Hub with no credentials in firmware appears on the chosen Site within one minute of provisioning; credentials travel encrypted.
- **CAP-2**
  - **intent:** An Administrator identifies a nearby unassigned Node over BLE (after its setup button is pressed) and assigns it to a Site and Lot.
  - **success:** The assigned Node reports through that Site's Hub; a Lot holding a Node rejects a second one; reassigning keeps the Node's Reading history.
- **CAP-3**
  - **intent:** Each Node declares its Sensors and their Sensor Specifications to the Server when it first reports.
  - **success:** Calibration controls appear only for Sensors declaring `calibration: true`; soil moisture alerts by default, other Sensors are watched until Thresholds are set.
- **CAP-4**
  - **intent:** A Node reports one Reading per Sensor every 15 minutes, plus battery level and charging status, through the Hub to the Server.
  - **success:** Readings carry the Node's measurement time; after a Server or Hub outage of up to 24 h, every buffered Reading arrives; reporting survives a router Wi-Fi channel change without re-provisioning.
- **CAP-5**
  - **intent:** Users sign in to the mobile and web apps through a self-hosted OpenID Connect identity provider (Keycloak).
  - **success:** The Server stores no passwords; Sites, Memberships, and Roles are held in the identity provider (Phase Two Organizations) with a Server-side projection.
- **CAP-6**
  - **intent:** Any User creates a Site and becomes its first Owner; Owners rename Sites; Administrators create, rename, and remove Lots.
  - **success:** Creating a Site grants the creator Owner immediately; removing a Lot that still holds a Node is rejected.
- **CAP-7**
  - **intent:** An Owner invites a person by email with a Role, changes Roles, and removes Memberships.
  - **success:** The invitee gets the Membership on acceptance (on the home network); every Site always keeps at least one Owner; a Member's attempt to change Devices, Thresholds, Calibration, Pause, or Memberships is rejected; a Role on one Site grants nothing on another.
- **CAP-8**
  - **intent:** Any Member sees the latest Reading of every Sensor grouped by Lot, a 30-day history chart, and each Device's last-seen, battery, and charging state.
  - **success:** Each Lot shows exactly one status (*needs water*, *needs calibration*, *OK*, *unknown* with time since last Reading, *paused*, *no Node*); Lots needing water list first; a stale Reading is never shown as current; the apps say when the Server is unreachable and how old the data is.
- **CAP-9**
  - **intent:** An Administrator calibrates a `calibration: true` Sensor with a *dry* and a *wet* reference point.
  - **success:** Thresholds and calibrated Readings show in 0–100 %; recalibration changes only new Readings and leaves Threshold % values unchanged.
- **CAP-10**
  - **intent:** An Owner or Administrator sets, changes, or removes a Sensor's low and high Thresholds.
  - **success:** Low below high is enforced; turning alerts on without a Specification default proposes low = Min + 20 % × (Max − Min); an empty high Threshold never alerts; other Roles are rejected.
- **CAP-11**
  - **intent:** The Server opens a Threshold Alert naming the Lot and the side crossed, and closes it when Readings recover.
  - **success:** Three consecutive Readings beyond a Threshold open the Alert (≈30 min after the first crossing), three consecutive within close it; one or two Readings on the other side change nothing.
- **CAP-12**
  - **intent:** While a Threshold Alert stays open, the Server repeats it as a Reminder at the resolved cadence.
  - **success:** Cadence resolves User setting for the Site → Site setting → once per day; a closed Alert sends no further Reminders.
- **CAP-13**
  - **intent:** The Server raises a Health Alert when a Node or Hub reports nothing for longer than its Silence Window.
  - **success:** Defaults 6 h (Node) and 5 min (Hub, heartbeat every 30–60 s), overridable per Device; a silent Hub is reported as the Hub, not as every Node behind it; the Alert closes when the Device reports again.
- **CAP-14**
  - **intent:** The Server raises a Health Alert when a Device's battery is low while not charging.
  - **success:** Three consecutive reports below 20 % and *not charging* open it; three reports *charging* or ≥ 20 % close it; the Alert shows level and charging status.
- **CAP-15**
  - **intent:** The Server delivers Alerts and Reminders as push notifications to every User with a Membership on the Site.
  - **success:** Notifications reach the phone away from home and are understandable without opening the app.
- **CAP-16**
  - **intent:** Each User sets a daily Notification Window, evaluated in their own time zone; anything falling due outside it is delivered as one summary when it opens.
  - **success:** Default 07:00–22:00; a summary holds at most one entry per open Alert; an Alert that opened and closed while held is not delivered.
- **CAP-17**
  - **intent:** Each User mutes and unmutes all notifications for one Site without leaving it.
  - **success:** Muting affects only that User; Alerts still open and close for everyone.
- **CAP-18**
  - **intent:** An Administrator pauses and resumes a Device or a whole Site, optionally until an end date.
  - **success:** While paused, no Readings are ingested and no Alerts open, and open Alerts close; a Pause with an end date resumes automatically; on resume the Silence Window restarts; Devices added to a paused Site start paused.
- **CAP-19**
  - **intent:** The iOS and Android apps offer BLE provisioning and Node assignment, Readings, Thresholds, Calibration, Pause, Notification Window and Reminder settings, Membership management, and notifications.
  - **success:** Every listed function works on both platforms against the same Server.
- **CAP-20**
  - **intent:** The web app offers everything the mobile apps do except BLE functions, and shows Alerts as browser notifications while open.
  - **success:** Browser notifications follow the same Notification Window and mute rules; nothing is delivered when no browser has the app open.
- **CAP-21**
  - **intent:** The Server raises a Health Alert when an assigned Node has a `calibration: true` Sensor without Calibration.
  - **success:** The notification names Lot and Sensor and asks an Owner or Administrator to calibrate; it closes once the Sensor is calibrated; the uncalibrated Sensor opens no Threshold Alerts meanwhile.

## Constraints

- **Local-first (NFR-1):** Server and identity provider run on the home network and accept no inbound internet traffic; outbound (APNs/FCM push, SMTP, ACME DNS-01) is allowed.
- **Authenticated access (NFR-2):** every API and web request except Device ingestion carries an identity-provider token and is authorized per Site by Role; Devices authenticate as Devices.
- **Durability (NFR-3):** restarting Server or Hub loses no stored Readings, settings, or open Alerts; Readings are retained indefinitely.
- **Energy autonomy (NFR-4):** a solar-charged Node runs a full season (≈ April–October) without manual recharging and ≥ 14 days on a full battery with no sun, at the 15-minute interval.
- **Outdoor survival (NFR-5):** Node, panel, and soil probe survive a season in wet soil without corrosion-driven drift making Alerts unreliable.
- **Multi-Site correctness (NFR-6):** Membership, Role, and multi-Site behavior are covered by automated tests (only one Site is field-tested).
- **Reproducibility (NFR-7):** someone other than the author builds Node and Hub and runs the full stack from the public docs alone; Server and dependencies ship as container images; reference deployment is single-node Kubernetes via GitOps; a Compose file is a reference example only.
- **Open source (NFR-8):** firmware, Server, both apps, and Node/Hub hardware design are public under Apache-2.0; firmware is Rust and documented as a learning reference (ESP-NOW, BLE provisioning, deep sleep); no proprietary vendor libraries in the repositories.
- **Room for irrigation (NFR-9):** data and command paths allow later Device commands (e.g. open a valve) without reworking the V1 model; commands can require confirmation, can be stopped manually, and are blocked by Pause.
- **TLS everywhere (NFR-10):** all IP traffic between apps, web, Hub, Server, and identity provider uses TLS, including on the LAN; no plain-HTTP endpoints.
- **Reach (NFR-11):** a Node reports reliably from the author's farthest Lot, where home Wi-Fi is unusable.
- **Test-first:** every capability is built test-first, with acceptance criteria written as failing automated tests before implementation. Firmware is tested host-side only; there are no on-device tests in CI.
- **Hardware baseline:** Node and Hub run Rust firmware on ESP32-S3 (coexistence validated, see `device-hardware.md`); the air-quality Sensor reports raw gas resistance (Ω) from a BME680.
- **Approximate soil moisture:** V1 soil moisture is approximate. A separate analog (ADC) capacitive probe uses two-point linear Calibration with no temperature compensation. Temperature Readings from the same Node wake are recorded alongside, so the temperature effect can be analysed later and compensation added without migrating history (spine AD-9 keeps raw values). Probe model and sealing are chosen in the Node/hardware epic against NFR-5.

## Non-goals

- Irrigation control: no valves, pumps, or commands to Devices in V1.
- Remote access to the apps or web app from outside the home network (planned next after V1).
- Weather-forecast integration; measured soil stays the primary signal.
- Hosted public service or pre-built hardware.
- Frost/heat logic beyond generic Thresholds.
- Air-quality index (IAQ, eCO2) or Bosch BSEC.
- External monitoring of the Server, home internet, or push services (apps only show Server unreachable).
- Sensor-fault detection beyond the Uncalibrated Sensor Alert (a failed probe surfaces as a Threshold or Silent Device Alert).
- "Mark watered" or "Snooze" actions on Alerts.
- Background web push when no browser has the app open.

## Success signal

- **Primary:** one full summer on the author's Site with no plant lost to missed watering, and every soil-moisture Threshold Alert delivered inside the author's Notification Window before the Lot reaches its calibrated dry point (0 %).
- **Secondary:** every Device that stops reporting produces a Health Alert within its Silence Window (delivered when the window next opens) unless paused; a Node completes the season without manual recharging; at least one other person reproduces the setup from the public docs.
- **Counter-metrics (do not optimize):** notifications per Lot per User per day stay at or below the Reminder cadence; the reporting interval is not lengthened to save battery if that delays Threshold Alerts past the Notification Window.
