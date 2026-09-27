---
title: "Extract: Coldframe product brief"
source:
  - _bmad-output/planning-artifacts/briefs/brief-coldframe-2026-09-25/brief.md (status: final)
  - _bmad-output/planning-artifacts/briefs/brief-coldframe-2026-09-25/addendum.md
  - _bmad-output/planning-artifacts/briefs/brief-coldframe-2026-09-25/.memlog.md
created: 2026-09-25
purpose: Faithful structured input for the Coldframe PRD. Nothing here is invented; items marked **[AMBIGUOUS]** or **[NOT STATED]** need resolution in the PRD.
---

# Extract: Coldframe — Garden Intelligence Platform

## Vision & problem

**Product one-liner (brief):** "Coldframe tells you when your garden needs water — before it is too late."

**Problem:**
- In summer, soil dries faster than it looks. Checking by hand is easy to forget; by the time plants show stress, the damage is done.
- Today the author checks and waters "by eye and habit".
- Existing options do not close the gap:
  - DIY sensor nodes (ESPHome + Home Assistant) give raw readings and leave the "should I water?" logic to the user.
  - Weather-based irrigation tools estimate water loss from forecasts but never measure the actual soil — "not enough in a hot, dry summer".
  - Commercial sensors (Gardena, Ecowitt, Flower Care) lock you into their gateway/cloud/valve ecosystem and mostly show data rather than tell you what to do.

**Differentiation (stated honestly in brief):** "no technical moat". Value = putting existing pieces together as one open, self-hosted system whose job is a single, clear decision — *water now* — grounded in measured soil moisture (not weather estimates), with Device-health alerts so a failed Sensor is noticed. Also a real-world, end-to-end Rust-on-ESP32 reference (ESP-NOW, BLE provisioning, deep sleep) for others to learn from.

**Long-term vision:** "the open, self-hosted brain for a home garden": more nodes and beds, soil data combined with weather, and — the natural next step — automatic irrigation, where the "water now" notification becomes a valve opening, "with the gardener still in control".

## Target users/audience

- **Primary: the author (project creator)** — one garden, a node at a distance from the house, an iOS phone. Success = a summer without plants lost to missed watering.
  - Note: the persona was renamed from "the owner" to "the author" to avoid clashing with the Owner Role (memlog).
- **Secondary: open-source gardeners and makers** — comfortable flashing an ESP32 and running a small server; looking for a self-hosted alternative to closed ecosystems.
- **In-product roles** (per Site, via Membership): Owner, Administrator, Member — see Capabilities.

## Stakes (hobby/internal/launch signals)

- Memlog decision: "public open-source project, driven by a personal need (know when to water own garden) — right-size rigor to passion/OSS, not investor-grade".
- Built in the open: firmware, server (API + web app), iOS and Android apps are all public.
- No hosted public service — each adopter runs their own server.
- Field test scope: V1 is field-tested on one garden only (the author's), over one full summer.
- Working mode recorded: "fast path by default" for the brief.
- **[NOT STATED]** No timeline, release date, or launch/marketing plan stated.

## Form factors & surfaces (hardware, server, web, mobile, API)

- **Hardware / firmware**
  - **coldframe-node** — ESP32-S3, Rust firmware. Sensors: soil moisture, temperature, humidity, air quality. Reports over ESP-NOW (works at the far end of the garden beyond Wi-Fi reach). Battery management optional.
  - **coldframe-hub** — ESP32-S3 gateway bridging ESP-NOW ⇄ home Wi-Fi to the server. Joins Wi-Fi **only** through BLE provisioning from the mobile app — no hard-coded credentials. Battery management optional.
  - Author has 2× ESP32-S3 on hand: one hub, one ESP-NOW-only node.
- **Server** — self-hosted, on the home network. Ingests readings, evaluates thresholds and Device health, sends push notifications, serves an API and a web app.
- **Web app** — served by the server. **[AMBIGUOUS]** Its feature set is not described anywhere (which of provisioning/thresholds/readings/admin it covers vs the mobile apps).
- **Mobile apps** — iOS and Android, both "first-class, mandatory platforms". Functions: BLE provisioning of hubs, set thresholds, view readings, receive notifications. Connect to the server-side API.
- **API** — server-side; receives measurements from hubs; backs the mobile app (and web app).

## Capabilities in v1 scope (bulleted, precise)

**Firmware**
- Node: read Sensors → transmit over ESP-NOW.
- Hub: receive ESP-NOW → forward over Wi-Fi to server; BLE provisioning for Wi-Fi credentials.
- ESP-NOW must follow the router's Wi-Fi channel.
- Deep sleep referenced as part of the Rust-on-ESP32 reference value (brief "What Makes This Different").

**Server**
- Ingestion of readings from hubs.
- Threshold evaluation.
- Device-health monitoring.
- Pause control.
- Push notifications (APNs/FCM and "any other notification endpoints").
- API and web app.

**Alerts / notifications**
- **Dryness alert** — sent early, when a bed crosses its threshold and before the soil is fully dry.
- **Health alerts**:
  - low battery;
  - a Device (node or hub) silent longer than a configured window (e.g. 1 hour or 1 day). "Silence must never be mistaken for 'all fine'."
- **Pause** — Administrator can pause ingestion and alerting (maintenance, off-season) so planned downtime does not trigger failure alerts.
- **[AMBIGUOUS]** Re-notification policy is open (see Open questions).
- **[AMBIGUOUS]** Pause granularity (per Site, per Device, whole server) not stated.
- **[AMBIGUOUS]** Low-battery alert vs "battery management is optional" — unclear how/if low battery is detected on Devices without battery management.
- **[AMBIGUOUS]** Thresholds are described for dryness/soil moisture only; whether thresholds/alerts apply to temperature, humidity, or air quality is not stated.
- **[AMBIGUOUS]** "When a bed crosses its threshold" — "bed" is not in the ubiquitous language; mapping bed ↔ Device/Sensor/Site is undefined.

**Calibration & thresholds**
- Administrators (and Owner, by inheritance) calibrate Sensors that support Calibration — in V1, the soil-moisture ADC — "to your soil".
- Administrators (and Owner) set thresholds.
- Calibration = per-Sensor mapping from raw reading (e.g. moisture ADC) to a normalized value.

**Mobile apps (iOS + Android)**
- BLE provisioning of hubs; thresholds; readings; notifications.
- App management works on the home network only in V1; notifications reach the phone anywhere.

**Multi-User / multi-Site / roles**
- Multiple Users and multiple gardens (Sites) per server.
- Membership = User × Site × exactly one Role. A User can hold Memberships on several Sites with different Roles.
- Roles are hierarchical: Owner ⊃ Administrator ⊃ Member.
  - **Owner** — assigns Members and Devices; inherits Administrator. Highest authority.
  - **Administrator** — assigns Devices; calibrates Sensors; sets thresholds; pauses ingestion; inherits Member.
  - **Member** — read-only; receives notifications.
- Multi-Site paths need automated test coverage (because only one garden is field-tested).

**Domain model (ubiquitous language, addendum)**
- **User** — `id`, `name`.
- **Site** — domain term for a garden; has a *Friendly Name*. ("AllotedLand", "Allotment" superseded; "Plot" rejected — collides with charting.)
- **Membership**, **Role** — as above.
- **Device** — a node or hub bound to a Site; carries one or more Sensors.
- **Sensor** — one measuring element on a Device; described by a Sensor Specification.
- **Sensor Specification** — measured quantity, unit, range, whether calibratable (and by which method). Tells system and UI which Sensors can be calibrated.
- **Calibration** — per-Sensor raw → normalized mapping; done by Administrator (or Owner).

## Explicitly out of scope / later versions

- **Remote app access** — exposing the server on a public or cloud endpoint; "the next step after V1".
- **Irrigation control** (valves, pumps) — V1 must not block it (design data and command paths so it can be added without rework), but does not build it.
- **Weather-forecast integration** — soil measurement is primary; forecasts may later complement it ("rain tomorrow, skip").
- **A hosted service run for the public** — each adopter runs their own server.
- Implied: multi-garden field testing beyond one garden (supported in software, not field-tested).

## Success metrics & counter-metrics stated

- **One full summer** with the author's garden monitored and no plant loss from missed watering.
- **Early warning:** dryness alert arrives at least **several hours** before the soil reaches "fully dry", at the author's Calibration. **[AMBIGUOUS]** "several hours" not quantified; "fully dry" not defined.
- **No silent failures:** every node or hub that stops reporting triggers an alert within its configured window, unless paused.
- **Battery:** a node runs a full season on one charge. **[AMBIGUOUS]** "season" length not defined; tension with "battery management is optional"; sampling interval vs battery target is an open question.
- **Reproducible:** someone other than the author can build a node and hub and run the stack from the public docs.
- **Counter-metrics:** none stated explicitly. Closest guardrails: pause must prevent planned downtime from triggering failure alerts (i.e. avoid false alerts); silence must not read as "all fine" (avoid false reassurance). **[NOT STATED]** no alert-fatigue / false-positive metric.

## Constraints (hardware, power, radio, hosting, licensing, open-source)

- **Hardware:** ESP32-S3 for both node and hub (decided; 2 units on hand). Soil probe type not decided (research: capacitive, not resistive).
- **Power:** battery management optional on node and hub; target a full season per charge for the node.
- **Radio:**
  - Node ↔ hub via ESP-NOW (chosen for garden distance).
  - Hub must run Wi-Fi + BLE + ESP-NOW on one ESP32-S3; esp-radio lists S3 BLE/coex with caveats — validate early.
  - ESP-NOW and Wi-Fi STA share one channel — node must follow the router's channel.
  - Hub Wi-Fi credentials only via BLE provisioning; no hard-coded credentials.
- **Hosting / network:** self-hosted on the home network; no inbound traffic from the internet; only outbound internet traffic to Apple/Google push services and other notification endpoints. App management only from the home network in V1.
- **Open source:** firmware, server (API + web app), iOS and Android apps all public; docs must let a third party reproduce the build.
- **Licensing:** **[NOT STATED]** no license chosen or mentioned.
- **Platforms:** iOS and Android both mandatory.
- **Language:** firmware in Rust.

## Tech/implementation decisions stated (these belong in a PRD addendum, keep verbatim-ish)

- Firmware in **Rust on ESP32-S3**: node (Sensors → ESP-NOW) and hub (ESP-NOW → Wi-Fi, BLE provisioning).
- "2× ESP32-S3 on hand: one is the gateway (ESP-NOW ⇄ Wi-Fi + BLE provisioning), the other an ESP-NOW-only sensor node. ESP-NOW was chosen for the garden distance."
- "(a) esp-radio lists S3 BLE/coex with caveats — the gateway needs Wi-Fi, BLE, and ESP-NOW together; validate early."
- "(b) ESP-NOW and Wi-Fi STA on one radio share a channel — the node must follow the router's channel."
- "Future: irrigation actuation (valves/pumps) — design data and command paths so it can be added without rework; not in V1."
- Push via APNs/FCM (outbound only).
- Architecture sketch component names: `coldframe-node`, `coldframe-hub`, mobile app, server-side API.
- **Research digest (addendum explicitly says "unverified — verify before relying on it"):**
  - esp-hal 1.0.0 stable (Oct 2025); esp-radio 1.0.0-beta.2 (Sep 2026), beta, requires esp-hal `unstable`; BLE host: TrouBLE.
  - C2/C3/C6 support Wi-Fi + BLE + coex + ESP-NOW; S2/S3 have BLE/coex caveats; H2 unsuitable (no Wi-Fi/ESP-NOW).
  - C6 Wi-Fi STA + BLE coex currently broken (esp-rs/esp-hal#6397); digest suggests "Consider C3 for the hub, or validate C6 first". **[AMBIGUOUS]** This suggestion predates/sits beside the S3 hardware decision; the decision (S3) stands.
  - esp-idf-hal/-svc (std) now community-maintained.
  - BLE provisioning: Espressif unified provisioning (protocomm/protobuf over BLE GATT; Sec1 X25519+AES-CTR, Sec2 SRP6a), now `network_provisioning` component; official open-source ESP BLE Provisioning apps/mobile libs exist. No native Rust crate (esp-idf-svc issue #87); an Espressif blog (Apr 2026) hand-rolled Sec1 in no_std Rust. Expect to implement or FFI to C. Improv Wi-Fi is an alternative with a Rust implementation.
  - Soil sensing: capacitive not resistive; cheap "v1.2" boards often have NE555 (bad at 3.3 V), unsealed edges, non-linear output; per-soil calibration (dry/wet endpoints minimum); temperature and salinity shift readings; settling time after insertion. Standard model FAO-56 ET0 × crop coefficient; best practice combine sensor trend with ET model.
  - Battery: ESP-NOW vs Wi-Fi same node ~3.7 yr vs ~6.9 mo (ThingPulse, from snippets); ESP-NOW wake ≈60 ms at 70–150 mA; bare-module deep sleep 5–15 µA, dev boards 25 µA–mA — board design dominates; no Rust-specific sleep figures.
  - Landscape: ESPHome+HA, SOILSENS-V5W (ESP-NOW + CapiBridge), HA Smart Irrigation/NeverDry/IrriSynk (FAO-56 on weather), Mycodo, MiFlora/Flower Care, Ecowitt WH51, Gardena smart, LoRa nodes (Dragino LSE01, Makerfabs), FarmBot. Apparent gap: self-hosted, open, sensor + ET/forecast → plain "water now / skip".

## Decisions recorded in memlog (one line each)

- Stakes: public OSS project driven by personal need; right-size rigor to passion/OSS, not investor-grade.
- Hardware: 2× ESP32-S3 — one ESP-NOW⇄Wi-Fi hub, one ESP-NOW-only node; ESP-NOW chosen for garden distance.
- Watering signal: measured soil dryness is primary; weather may complement, not replace.
- V1 output: mobile push notification only; architecture ready for later irrigation actuation (out of V1).
- V1 scope: node+hub firmware, server ingestion, push notifications, mobile app for management/provisioning.
- Calibration done by owner; users set thresholds; dryness measurement method open. (Superseded: Administrators calibrate and set thresholds.)
- Dryness notification fires on threshold crossing, before soil is fully dry.
- Health notifications: low battery; Device silent for configurable window (e.g. 1h / 1 day).
- Pause control: owner can pause ingestion/alerting for maintenance/offline periods. (Later: Administrator, and Owner by inheritance.)
- Owner self-hosts; local-only first, later public/cloud endpoint; server provides API + web app.
- Mobile: iOS and Android both mandatory for V1.
- Working mode: fast path by default.
- Change: iOS + Android both mandatory as a product requirement (removed assumption that Android was driven by OSS adopters).
- V1 network model: server local, no inbound internet; outbound only to APNs/FCM/notification endpoints; app management on home network only.
- V1 supports multiple Users and multiple gardens; field-tested on one garden only.
- Ubiquitous language v1: User(id,name); Garden = AllotedLand; Roles Owner/Administrator/Member. (Superseded naming.)
- Renamed AllotedLand → Allotment; "Plot" rejected (collides with charting). (Superseded.)
- Administrators calibrate Sensors (V1: soil-moisture ADC).
- Device has multiple Sensors; each has a Sensor Specification incl. calibratable flag.
- Garden domain term = Site (supersedes Allotment).
- Administrator (and Owner) sets thresholds and pauses ingestion, alongside calibration.
- Change: persona "the owner" renamed "the author" to avoid clash with Owner Role.
- User confirmed remaining brief assumptions (success criteria, water by eye today, forecasts later, no public hosted service); brief final.
- Role scoped per Site via Membership (User × Site × Role); "Membership" added to avoid clash with Member role.
- Membership confirmed as term; Owner is highest authority, includes Administrator and Member privileges.
- Role hierarchy confirmed: Owner ⊃ Administrator ⊃ Member.

## Open questions / assumptions stated

- **Dryness measurement** — raw capacitance → % → calibrated per-soil value; probe choice; Calibration method and UX. Cheap capacitive probes are non-linear, soil-dependent, drift with temperature.
- **Hub radio** — Wi-Fi + BLE + ESP-NOW on one ESP32-S3; esp-radio beta with S3 BLE/coex caveats; validate early. ESP-NOW must follow router's channel.
- **BLE provisioning in Rust** — no ready-made crate for Espressif's protocol; implement or bind C component (Improv Wi-Fi noted as alternative in research).
- **Remote access** — V1 off-home viewing/settings waits for public endpoint; open: auth, TLS, tunnel vs hosted.
- **Re-notification policy** — once per crossing? reminder cadence? until watered? clear on recovery?
- **Mobile tech** — native per platform vs cross-platform (Flutter / React Native / KMP), given both platforms mandatory and BLE provisioning needed on both.
- **Sampling interval** — sampling/reporting interval vs node battery-life target.
- **Ownership/binding model** — account, Site, and Device ownership; how a hub or node is bound to a Site during provisioning.
- Additional gaps observed in this extract (not stated as questions in the sources): web app scope; license; pause granularity; notification recipients' preferences (can Members opt out / per-Site?); how nodes are paired to hubs; whether non-soil Sensors have thresholds; definition of "bed"; data retention/history; server deployment packaging.
- **Assumptions:** all brief [ASSUMPTION] tags were confirmed by the user and removed; brief status is final. Research digest remains explicitly unverified.

## Qualitative intent (tone, feel, values) that could get lost

- **One clear decision:** the product's job is "water now" — tell the gardener what to do, not just show data. Differentiates from raw-data DIY and data-display commercial products.
- **Early, not late:** alerts must arrive while there is still time to act.
- **Trust through honesty about failure:** "Silence must never be mistaken for 'all fine'." Device health is a first-class concern, and planned downtime must not cry wolf (pause).
- **Measured over estimated:** real soil data is primary; weather only complements later.
- **Open and self-hosted, no lock-in:** reaction against closed gateway/cloud/valve ecosystems; each adopter owns their server; all code public.
- **Reproducibility and teaching value:** meant as a real-world end-to-end Rust-on-ESP32 reference others can learn from and rebuild from public docs.
- **Candid positioning:** "Honestly: no technical moat." Value is integration, not invention.
- **Gardener stays in control:** even in the irrigation future, the valve replaces the notification "with the gardener still in control".
- **Right-sized rigor:** passion/OSS project, not investor-grade — PRD should avoid enterprise overreach, yet keep multi-User/multi-Site correctness covered by automated tests.
- **Privacy/locality by default:** no inbound internet exposure in V1; outbound only for push.
- **Language discipline:** the ubiquitous language (Site, Membership, Role, Device, Sensor, Sensor Specification, Calibration) was deliberately chosen; "Plot" and "the owner" (persona) avoided to prevent collisions.
