---
title: "PRD: Coldframe — Garden Intelligence Platform"
status: final
created: 2026-09-25
updated: 2026-09-26
---

# PRD: Coldframe — Garden Intelligence Platform

## 0. Document Purpose

This PRD defines what Coldframe V1 must do. It is written for the author and for anyone building from the public repositories, and it feeds the architecture, UX, and epic/story work. It builds on the final product brief (`planning-artifacts/briefs/brief-coldframe-2026-09-25/`) and does not repeat its market narrative. Vocabulary is fixed in §3 Glossary. Features are grouped in §4 with globally numbered FRs. All assumptions raised during drafting were resolved with the author; decisions are traced in `.memlog.md`. Technology choices (chip, radio stack, crates, protocols) live in `addendum.md`.

## 1. Vision

Coldframe tells you when your garden needs water, while there is still time to act. Battery-powered Nodes in the garden measure soil moisture, temperature, humidity, and air quality and relay Readings through a Hub to a self-hosted Server on the home network. When the soil-moisture Sensor on a Lot drops below its low Threshold, the Server sends a push notification at a time of day the gardener has chosen.

The product's job is one clear decision — *water now* — grounded in measured soil, not weather estimates. It is equally honest about its own failures: a Device that goes silent or runs low on battery raises a Health Alert, because silence must never be mistaken for "all fine".

Coldframe is personal first and open by default. The firmware, Server (API and web app), and iOS and Android apps are public, and the documentation lets another gardener rebuild the whole system. There is no technical moat: the pieces exist separately, and Coldframe's value is combining them into one open, self-hosted system. V1 only notifies; the data and command paths are shaped so that a later version can open a valve instead, with the gardener still in control. More Lots and Nodes, and weather forecasts as a complement to measured soil ("rain tomorrow, skip"), follow later.

## 2. Target User

### 2.1 Jobs To Be Done

- **Functional:** Tell me which Lot needs water today, early enough to water it, without me checking by hand.
- **Trust:** Tell me when a Node, Hub, or Sensor setup is broken, so no news really means good news.
- **Control:** Let me choose when I am bothered, and let me pause the system for maintenance or the off-season without false alarms.
- **Ownership:** Run on my own hardware and network, with no vendor cloud and no lock-in.
- **Builder:** Give me (and other makers) a real, end-to-end Rust-on-ESP32 system to learn from and reproduce.

### 2.2 Non-Users (V1)

- Gardeners who cannot or will not build hardware, flash firmware, and run a single-node Kubernetes cluster with an identity provider — there is no hosted service and no pre-built hardware.
- Commercial growers and farms needing field-scale coverage, compliance, or automated irrigation.

### 2.3 Key User Journeys

- **UJ-1. Simon sets up the garden.** On the home Wi-Fi, Simon signs in for the first time, creates the Site "Home", and becomes its Owner. In the mobile app Simon provisions the Hub over BLE, powers a Node, and assigns it to the Lot "Tomatoes". The Node appears with its Sensors. Simon calibrates its soil-moisture Sensor (dry and wet) and sets a personal Notification Window to "from 07:00".
- **UJ-2. A hot week, a morning nudge.** The "Tomatoes" soil moisture drops below its low Threshold at 02:40. At 07:00 Simon's phone shows "Tomatoes needs water". Simon waters before work; the next Readings recover above the low Threshold, the Alert clears, and no Reminder follows. **Edge case:** if nobody waters, a Reminder arrives in the next morning's summary.
- **UJ-3. A Node dies quietly.** The Node at the far end of the garden stops reporting overnight. When Simon's Notification Window opens, the phone shows "Node on Lot 'Beans' silent for 6 h" instead of a false sense that the beans are fine.
- **UJ-4. Off for the winter.** In October Simon pauses the whole Site. No Readings are ingested and no Health Alerts fire for the Nodes brought indoors.
- **UJ-5. A second pair of eyes.** Before a holiday, Simon invites a neighbor by email as a Member of the Site; the neighbor accepts on Simon's Wi-Fi. The neighbor receives the same Alerts in their own Notification Window but cannot change Thresholds.

## 3. Glossary

- **User** — a person with an account on a Server. Has one Notification Window and personal Alert settings.
- **Site** — a garden. Has a name chosen by its Owner. A Server hosts one or more Sites.
- **Lot** — a named area on a Site (a bed). A Site has many Lots; a Lot has at most one Node, and a Node sits on exactly one Lot. A Hub belongs to a Site, not to a Lot.
- **Membership** — the link User × Site × exactly one Role. A User may hold Memberships on several Sites with different Roles.
- **Role** — Owner ⊃ Administrator ⊃ Member (each includes the privileges of the ones below).
  - **Owner** — highest authority on a Site; manages Memberships.
  - **Administrator** — assigns Devices, sets Thresholds, performs Calibration, pauses Devices and the Site.
  - **Member** — reads data and receives Alerts.
- **Device** — a Node or a Hub, bound to one Site (and, for a Node, to one Lot). Only Nodes carry Sensors.
  - **Node** — battery-powered Device in the garden that measures and reports Readings.
  - **Hub** — the Wi-Fi gateway: a mains-powered Device on the home network that relays Readings from Nodes to the Server without storing them. Has no Sensors.
- **Sensor** — one measuring element on a Node, described by a Sensor Specification.
- **Sensor Specification** — declared by the Device: measured quantity, unit, range, default low and high Threshold (optional), and `calibration: true|false` with the Calibration method (V1: two-point dry/wet). Digital Sensors declare `calibration: false`.
- **Calibration** — per-Sensor mapping from raw value to normalized value, for Sensors whose Specification says `calibration: true`.
- **Reading** — one timestamped value from one Sensor.
- **Threshold** — an alerting Sensor has a required **low Threshold** and an optional **high Threshold**; a Reading below the low or above the high one is alert-worthy (e.g. soil moisture too dry / waterlogged; temperature frost / heat). Defaults come from the Sensor Specification; an Owner or Administrator can override them per Sensor. A Sensor without Thresholds is only watched (recorded and shown), never alerted on.
- **Alert** — a condition the Server tells Users about. Two kinds:
  - **Threshold Alert** — a Sensor's Readings went below its low or above its high Threshold (for soil moisture below low: "Lot needs water").
  - **Health Alert** — a Device is silent beyond its Silence Window, its battery is low, or one of its Sensors needs Calibration.
- **Reminder** — repeated notification for a still-open Alert. Cadence resolves in order: the User's own setting for the Site → the Site setting → the default (once per day).
- **Notification Window** — a User's daily time range (from/to) in which notifications are delivered; the simplest form is "from 07:00". Notifications that fall due outside it are gathered and delivered in bulk when it opens.
- **Silence Window** — how long a Device may go without reporting before a Health Alert opens.
- **Pause** — a state of a Device in which Readings are not ingested and no Alerts open. Pausing a Site pauses all its Devices.
- **Server** — the self-hosted backend: ingestion, Alert evaluation, notifications, API, and web app.

## 4. Features

### 4.1 Device Setup and Reporting

**Description:** An Administrator brings a Hub onto the home Wi-Fi from the mobile app over BLE, then adds Nodes. Nodes report Readings on a fixed interval through the Hub. Realizes UJ-1.

#### FR-1: Provision a Hub over BLE
An Administrator can give a Hub its Wi-Fi credentials from the iOS or Android app over BLE and bind it to a Site.
- The Hub joins Wi-Fi only through BLE provisioning; firmware contains no Wi-Fi credentials.
- After provisioning, the Hub appears on the chosen Site within one minute.
- Credentials are exchanged encrypted over BLE.

#### FR-2: Pair a Node and assign it
A new Node is *unassigned*. An Administrator identifies it from the iOS or Android app over BLE, then assigns it to a Site and Lot; from then on, the Node reports through that Site's Hub.
- A Node becomes visible over BLE only after its setup button is pressed, and only for a limited time; otherwise it does not advertise, to save energy.
- The app shows only Nodes within BLE range, so the Administrator knows which physical Node is being assigned.
- An unassigned Node's Readings are not evaluated for Alerts.
- A Lot that already has a Node cannot take a second one; the existing Node must be moved or unassigned first.
- Reassigning a Node to another Lot keeps its Reading history.

#### FR-3: Declare Sensor Specifications
Each Node declares its Sensors and their Sensor Specifications to the Server when it first reports.
- The Server and apps show Calibration controls only for Sensors with `calibration: true`.
- A Sensor whose Specification defines default Thresholds alerts without further setup; in V1 that is soil moisture only. Every other Sensor (temperature, humidity, air quality) is only watched until an Owner or Administrator sets Thresholds for it.
- The air-quality Sensor reports gas resistance (Ω); lower resistance means more volatile compounds, so its Alert uses the low Threshold like any other Sensor.

#### FR-4: Report Readings
A Node reports one Reading per Sensor every 15 minutes through a Hub to the Server. Every battery-powered Device also reports its battery level (%) and charging status (*charging* / *not charging*).
- Readings keep reaching the Server after the router changes its Wi-Fi channel, without re-provisioning.
- Readings carry the Node's measurement time, not the Server's arrival time.
- The Hub is a pass-through proxy and stores nothing. If the Server does not acknowledge a Reading (Server or Hub unreachable), the Node keeps it and resends it once the Server is reachable again; a Node buffers at least 24 hours of Readings.

### 4.2 Sites, Lots, and People

**Description:** One Server hosts many Users and Sites. Access is per Site through Memberships. Realizes UJ-1, UJ-5.

#### FR-5: Sign-in via OpenID Connect
Users sign in to the mobile apps and web app through a self-hosted OpenID Connect identity provider (reference choice: Keycloak, see addendum). The identity provider owns accounts, passwords, and registration; the Server stores no passwords.
- Sites, Memberships, and Roles are held in the identity provider (Keycloak Organizations via the Phase Two extension). The Server keeps a local projection of them for fast reads and enforces the rules in FR-6 and FR-7 (see architecture spine AD-3).

#### FR-6: Manage Sites and Lots
Any User can create a Site and becomes its first Owner. An Owner can rename the Site. An Administrator can create, rename, and remove Lots on a Site.
- Removing a Lot requires first moving or unassigning its Node.

#### FR-7: Manage Memberships
An Owner can invite a person to a Site with a Role by email, change the Role, and remove the Membership.
- The invitee receives an email with a link, signs in or registers with the identity provider, and gets the Membership on acceptance.
- Accepting the invitation requires being on the home network in V1, because the Server and identity provider are not reachable from the internet.
- Every Site keeps at least one Owner.
- A Member sees data and Alerts but cannot change Devices, Thresholds, Calibration, Pause, or Memberships; the API rejects such changes with an authorization error.
- Roles are enforced per Site: Administrator on Site A grants nothing on Site B.

### 4.3 Readings and Calibration

**Description:** Users see what their garden is doing now and over time; Administrators tune soil Sensors to their soil. Realizes UJ-1.

#### FR-8: View Readings
Any Member can see the latest Reading of every Sensor on a Site, grouped by Lot, and a history chart of the last 30 days.
- Each Lot shows one status: *needs water*, *OK*, *unknown* (Node silent, with the time since its last Reading), or *paused*. A stale Reading is never shown as the current state.
- The Site overview lists Lots that need water first.
- Each Device shows its last-seen time; battery-powered Devices also show battery level (%) and charging status.
- When the apps cannot reach the Server, they say so and show how old the displayed data is.
- Readings are retained indefinitely.

#### FR-9: Calibrate a Sensor
An Administrator can calibrate a Sensor whose Specification says `calibration: true` by recording two reference points: *dry* (probe in dry soil) and *wet* (probe in water).
- Thresholds of a Sensor with `calibration: true` are always expressed in normalized units (0–100 %), and its Readings are shown in the same units once calibrated.
- Until it is calibrated, such a Sensor opens no Threshold Alerts (see FR-21).
- Recalibrating applies to new Readings and leaves the Thresholds' % values unchanged; history keeps the Calibration it was recorded with.

### 4.4 Thresholds and Threshold Alerts

**Description:** The core decision. When a Sensor crosses its Threshold, the Server opens a Threshold Alert naming the Lot; it repeats as a Reminder until the Readings recover. Realizes UJ-2.

#### FR-10: Set Thresholds
An Owner or Administrator can set, change, or remove the Thresholds of any Sensor, which turns alerts on for watched Sensors such as temperature or air quality. Other Users cannot.
- On an alerting Sensor, the low Threshold is required. The high Threshold may be left empty where it does not apply; an empty high Threshold never opens an Alert.
- When both are set, the low Threshold must be below the high Threshold; the Server rejects anything else.
- A new Sensor starts with the default low and high Threshold from its Sensor Specification, or with none if the Specification defines none.
- When an Owner or Administrator turns on alerts for a Sensor whose Specification gives no default low Threshold, the proposed value is Min + 20 % × (Max − Min) of the Sensor's range. There is no fallback for the high Threshold: without a Specification default, it stays empty.
- An Owner or Administrator can clear a high Threshold that came from the Specification default, or set one where none exists.

#### FR-11: Open and close Threshold Alerts
The Server opens a Threshold Alert when **three consecutive Readings** of a Sensor are below its low Threshold or above its high Threshold, and closes it when **three consecutive Readings** are back within the Thresholds.
- One or two Readings on the other side of a Threshold neither open nor close an Alert, so a Reading hovering around the Threshold does not flap.
- At the FR-4 interval, an Alert opens about 30 minutes after the first Reading crosses.
- The Alert says which side was crossed (for soil moisture, low reads "‹Lot› needs water").
- A closed Alert sends no further Reminders.

#### FR-12: Remind while open
While a Threshold Alert stays open, the Server sends Reminders at the resolved cadence: the User's own setting for the Site, else the Site setting, else once per day.
- An Owner or Administrator sets the Site cadence; every User may set their own for each Site.

### 4.5 Device Health

**Description:** Coldframe watches itself so that silence is never read as "all fine". Every Health Alert (FR-13, FR-14, FR-21) sends Reminders at most once per day while it stays open. Realizes UJ-3.

#### FR-13: Silent Device Alert
The Server opens a Health Alert when a Device sends nothing for longer than its Silence Window, and closes it when the Device reports again.
- Applies to both Nodes and Hubs; a silent Hub is reported as the Hub, not as every Node behind it.
- Default Silence Window: **6 hours for a Node**, **5 minutes for a Hub**. An Administrator can change it per Device.
- A Hub sends a heartbeat to the Server every 30–60 seconds, even when no Node has reported, so a silent Hub is detected independently of its Nodes.

#### FR-14: Low-battery Alert
The Server opens a Health Alert when **three consecutive reports** of a Device show a battery level below 20 % while *not charging*, and closes it when **three consecutive reports** show *charging* or a level of 20 % or more.
- A Device below 20 % that reports *charging* raises no Alert; *not charging* above 20 % is never an Alert on its own.
- The Alert shows the battery level and charging status.

#### FR-21: Uncalibrated Sensor Alert
The Server opens a Health Alert when a Node is assigned to a Lot and one of its Sensors with `calibration: true` has no Calibration, and closes it once that Sensor is calibrated.
- The notification names the Lot and the Sensor and asks an Owner or Administrator to calibrate it.

### 4.6 Notification Delivery

**Description:** Alerts reach every User with a Membership on the Site, on their phone, only in their chosen hours. Realizes UJ-2, UJ-3, UJ-5.

#### FR-15: Push notifications
The Server delivers Alerts and Reminders as push notifications to the iOS and Android apps of every User with a Membership on the Site.
- Notifications reach the phone away from home; opening the app for details requires the home network in V1.

#### FR-16: Notification Window
Each User can set a daily Notification Window (from/to; simplest form "from 07:00"). The default is 07:00–22:00.
- Threshold Alerts, Health Alerts, and Reminders that fall due outside the window are gathered and pushed in bulk as one summary notification when the window opens.
- An Alert that opened and closed while held is not delivered.
- A summary contains at most one entry per open Alert, however many Reminders fell due for it.

#### FR-17: Mute a Site
Each User can mute and unmute all notifications for a Site without leaving it.
- Muting affects only that User; other Users on the Site keep receiving Alerts, and Alerts still open and close as normal.

### 4.7 Pause

**Description:** Planned downtime must not cry wolf. Realizes UJ-4.

#### FR-18: Pause a Device or a Site
An Administrator can pause and resume a Device. Pausing a Site pauses all its Devices.
- While paused, a Device's Readings are not ingested and no Alerts open for it; open Alerts for it close.
- A Pause may carry an optional end date, at which the Device resumes automatically; without one, the Pause lasts until an Administrator resumes it.
- On resume, the Silence Window restarts from the moment of resume.
- Devices added to a paused Site start paused.

### 4.8 Apps

**Description:** The same capabilities reach Users on iOS, Android, and the web; only BLE-dependent setup is mobile-only. Realizes UJ-1 to UJ-5.

#### FR-19: Mobile apps
The iOS and Android apps both support: BLE provisioning, BLE identification and assignment of Nodes, viewing Readings; Thresholds; Calibration; Pause; Notification Window and Reminder settings; Membership management; and receiving notifications.

#### FR-20: Web app
The Server's web app offers everything the mobile apps do except features that need Bluetooth (Hub provisioning, Node identification). While the web app is open in a browser, it shows Alerts as browser notifications.
- Browser notifications follow the same Notification Window and mute settings as push notifications.
- When no browser has the web app open, nothing is delivered there; there is no background web push in V1.

## 5. Cross-Cutting Requirements

- **NFR-1 Local-first:** The Server and the identity provider run on the home network and accept no inbound traffic from the internet. Outbound internet access is allowed (e.g. push-notification services APNs/FCM, email for invitations).
- **NFR-2 Authenticated access:** Every API and web-app request except Device ingestion carries a valid token from the identity provider and is authorized by Role per Site. Devices authenticate to the Server as Devices.
- **NFR-3 Durability:** Restarting the Server or the Hub loses no stored Readings, settings, or open Alerts; Readings taken while the Server is unreachable arrive later from the Node's buffer (FR-4).
- **NFR-4 Energy autonomy:** A Node is solar-charged and, at the FR-4 interval, runs a full season (about 6 months, April–October) without manual recharging. On a full battery with no solar input it keeps reporting for at least 14 days.
- **NFR-5 Outdoor survival:** A Node, its solar panel, and its soil probe survive a season outdoors in wet soil without corrosion-driven drift making Alerts unreliable.
- **NFR-6 Multi-Site correctness:** Only one Site is field-tested, so Membership, Role, and multi-Site behavior are covered by automated tests.
- **NFR-7 Reproducibility:** Someone other than the author can build a Node and Hub and run the full stack from the public docs alone. The Server and its dependencies (including the identity provider) ship as container images. The reference deployment is a single-node Kubernetes cluster on a home server, delivered by GitOps (see addendum); the public docs cover that path end to end. A Compose file is provided as a reference example for those who prefer not to run Kubernetes; it is not the primary supported path.
- **NFR-8 Open source and reference value:** Firmware, Server, both mobile apps, and the Node and Hub hardware design are public under Apache-2.0, as set in the repository's `LICENSE`. Node and Hub firmware are written in Rust and documented as a learning reference for ESP-NOW, BLE provisioning, and deep sleep.
- **NFR-9 Room for irrigation:** The data and command paths allow a later version to send commands to Devices (e.g. open a valve) without reworking the V1 model, keeping the gardener in control: commands can require confirmation, can be stopped manually, and are blocked by Pause.
- **NFR-10 Encryption in transit:** All network traffic between the mobile apps, web app, Hub, Server, and identity provider uses TLS, including on the home network. No plain-HTTP endpoints are exposed.
- **NFR-11 Reach:** A Node reports reliably from the author's farthest Lot, where the home Wi-Fi is not usable.

## 6. Non-Goals (V1)

- No irrigation control — no valves, pumps, or commands to Devices.
- No remote access to the mobile apps or web app from outside the home network.
- No weather-forecast integration; measured soil stays the primary signal.
- No hosted public service and no pre-built hardware.
- No frost/heat logic beyond generic Thresholds.
- No air-quality index (IAQ, eCO2): the air-quality Sensor reports raw gas resistance; proprietary vendor libraries are not used, keeping every repository fully open.
- No monitoring of the Server itself, the home internet connection, or the push services from outside; the apps only show when the Server is unreachable (FR-8).
- No Sensor-fault detection: apart from the Uncalibrated Sensor Alert (FR-21), a Sensor raises Alerts only through its Thresholds. A failed probe shows up as a Threshold Alert (its Readings leave the Thresholds) or as a Silent Device Alert.
- No "Mark watered" or "Snooze" actions on Alerts: the Sensor sees the watering and closes the Alert itself (FR-11).

## 7. Success Metrics

**Primary**
- **SM-1:** One full summer with the author's Site monitored and no plant lost to missed watering. Validates FR-11, FR-12, FR-15, FR-16.
- **SM-2:** Every soil-moisture Threshold Alert is delivered within the author's Notification Window before the Lot reaches its calibrated dry point (0 %). Validates FR-9, FR-11, FR-16.

**Secondary**
- **SM-3:** Every Device that stops reporting produces a Health Alert within its Silence Window (delivered when the Notification Window next opens), unless paused. Validates FR-13, FR-18.
- **SM-4:** A Node completes the season without manual recharging. Validates NFR-4.
- **SM-5:** At least one person other than the author reproduces the setup from the public docs. Validates NFR-7.

**Counter-metrics (do not optimize)**
- **SM-C1:** Notifications per Lot per User per day — keep at or below the Reminder cadence. Counterbalances SM-1/SM-2: alerting earlier or more often would "win" SM-2 by training the gardener to ignore Coldframe.
- **SM-C2:** Reporting interval — do not lengthen it to win SM-4 if it delays Threshold Alerts past the Notification Window. Counterbalances SM-4.

## 8. MVP Scope

**In:** FR-1 to FR-21; one Hub and at least one Node; iOS and Android apps; web app; single self-hosted Server.

**Out (later):** remote access (next step after V1), irrigation control, weather integration.

## 9. Open Questions

Question 1 was resolved by the Hub radio spike. Questions 2, 3, and 5 were resolved in the architecture spine (`planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md`). Numbering is kept stable because the spine cites these questions by number.

1. ~~**Hub radio feasibility:**~~ *Resolved by the Hub radio spike (2026-09-27): **GO**.* One ESP32-S3 ran Wi-Fi, BLE, ESP-NOW, and TLS together for about 3 h 45 min across four runs. There were no crashes, ESP-NOW loss was 0.04 % in the 2-hour run, and the Node found the Hub again after a channel change in under a second. See `docs/spikes/hub-radio-coexistence.md`.
2. ~~**BLE provisioning protocol:**~~ *Resolved in architecture (AD-25):* a custom Coldframe BLE setup protocol for Hub and Node, secured with X25519 plus a per-Device proof-of-possession code. It is used instead of Espressif unified provisioning (no Rust crate; its app libraries don't fit the shared Kotlin core) or Improv Wi-Fi (credentials not encrypted, no enrolment).
3. ~~**Mobile tech:**~~ *Resolved in architecture (AD-14):* a Kotlin Multiplatform shared core (BLE, provisioning, OIDC, API client) with native SwiftUI and Jetpack Compose UI.
4. **Probe choice, sealing, and drift:** Which probe meets NFR-5, and is two-point linear Calibration accurate enough across the season's temperature range for SM-2, or is temperature compensation needed?
5. ~~**Where Sites, Memberships, and Roles live:**~~ *Resolved in architecture (AD-3):* in Keycloak as Phase Two Organizations. The Server's Site grain is the only writer, and a local projection serves reads.

## 10. Assumptions Index

All assumptions from the draft have been confirmed or resolved with the author (see `.memlog.md`). None open.
