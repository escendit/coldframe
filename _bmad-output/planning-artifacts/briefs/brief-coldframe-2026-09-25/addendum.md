# Addendum: Coldframe — technical context for downstream docs

Settled decisions come first (architecture sketch, hardware, ubiquitous language), then open questions, then the unverified research digest.

## Architecture sketch (from brain dump)

- **coldframe-node** — Rust firmware on an ESP device (ESP32-S3; see Hardware decision). One Sensor set per node: temperature, humidity, air quality, soil moisture. Talks ESP-NOW.
- **coldframe-hub** — ESP device acting as an ESP-NOW ⇄ Wi-Fi proxy to the server. Joins Wi-Fi **only** via BLE provisioning.
- **Battery management** — optional on both node and hub.
- **Mobile app** — performs BLE provisioning of the hub; connects to the server-side API.
- **Server-side API** — receives measurements from hubs; backs the mobile app.

## Hardware decision (user, 2026-09-25)

- 2× ESP32-S3 on hand: one is the gateway (ESP-NOW ⇄ Wi-Fi + BLE provisioning), the other an ESP-NOW-only sensor node. ESP-NOW was chosen for the garden distance.
- Flags for architecture:
  - (a) esp-radio lists S3 BLE/coex with caveats — the gateway needs Wi-Fi, BLE, and ESP-NOW together; validate early.
  - (b) ESP-NOW and Wi-Fi STA on one radio share a channel — the node must follow the router's channel.
- Future: irrigation actuation (valves/pumps) — design data and command paths so it can be added without rework; not in V1.

## Ubiquitous language (user, 2026-09-25)

- **User** — `id`, `name`.
- **Site** — the domain term for a garden (chosen by the user over "AllotedLand" and "Allotment"; "Plot" rejected because it collides with charting); has a *Friendly Name*.
- **Membership** — a User's attachment to a Site; each Membership carries exactly one Role. A User can hold Memberships on several Sites, with a different Role on each.
- **Role** — held through a Membership, so it is scoped per Site. Roles are hierarchical: Owner inherits Administrator, and Administrator inherits Member.
  - **Owner** — assigns Members and Devices; inherits Administrator.
  - **Administrator** — inherits Member; assigns Devices; calibrates Sensors; sets thresholds; pauses ingestion.
  - **Member** — read-only; receives notifications.
- **Device** — a node or hub bound to a Site; carries one or more Sensors.
- **Sensor** — one measuring element on a Device (for example, soil moisture, temperature, humidity, air quality); described by a Sensor Specification.
- **Sensor Specification** — what a Sensor measures and how: measured quantity, unit, range, and whether it is calibratable (and by which method). Tells the system and the UI which Sensors can be calibrated.
- **Calibration** — per-Sensor mapping from a raw reading (for example, a moisture ADC value) to a normalized value; performed by an Administrator (or Owner). Soil-moisture ADC is the V1 case.

## Open questions carried to PRD / architecture

- **Dryness measurement** — how to measure and normalize dryness (raw capacitance → % → calibrated per-soil value); Calibration UX.
- **Re-notification policy** — once a threshold is crossed: once per crossing? Reminder cadence? Clear on recovery?
- **Network model** — *Settled:* the V1 server takes no inbound internet traffic; its only internet traffic is outbound to APNs/FCM and other notification endpoints. *Open:* how the app reaches the server once the public endpoint arrives (auth, TLS, tunnel vs hosted).
- **Mobile tech** — native per platform vs cross-platform (Flutter/React Native/KMP), given that iOS and Android are both mandatory and BLE provisioning is needed on both.
- **Sampling interval** — sampling/reporting interval vs the battery-life target for the node.
- **Multi-User / multi-Site** — *Settled:* in V1. *Open:* define the account, Site, and Device ownership model, the Roles (Owner, Administrator, Member), and how a hub or node is bound to a Site during provisioning. V1 is field-tested on one garden only, so the multi-Site paths need automated test coverage.
- **Roles across Sites** — Settled: a User holds a Role per Site through a Membership, so the same User can have different Roles on different Sites.

## Research digest (2026-09-25, web research — verify before relying on it)

### Landscape

- **ESPHome + Home Assistant DIY soil nodes** — collect data; watering logic left to user automations. SOILSENS-V5W already uses ESP-NOW + a "CapiBridge" gateway (github.com/PricelessToolkit/SOILSENS-V5W).
- **HA smart-irrigation integrations** (Smart Irrigation, NeverDry, IrriSynk) — FAO-56 water-balance models on weather data, not soil sensors. Closest to the decision logic.
- **Mycodo** — Pi-based control/PID; heavy, not battery-node oriented.
- **MiFlora / Flower Care** — cheap BLE, short range, cloud app, HA friction.
- **Ecowitt WH51** — good calibratable probe, locked to the Ecowitt gateway, no watering recommendation.
- **Gardena smart** — proprietary ecosystem tied to their valves.
- **LoRa nodes** (Dragino LSE01, Makerfabs) — long range, need LoRaWAN infrastructure, raw data only.
- **FarmBot** — robotic gantry, overkill for monitoring.
- **Apparent gap:** self-hosted, open, sensor + ET (evapotranspiration)/forecast model → plain "water now / skip" recommendation.

### Rust on ESP32

- esp-hal 1.0.0 stable (Oct 2025). esp-radio (Wi-Fi/BLE/ESP-NOW) at 1.0.0-beta.2 (Sep 2026) — beta, requires esp-hal `unstable`. BLE host: TrouBLE.
- C2/C3/C6: Wi-Fi + BLE + coex + ESP-NOW supported. S2/S3 have caveats on BLE/coex. H2: no Wi-Fi/ESP-NOW — unsuitable.
- **Risk:** C6 Wi-Fi-station + BLE coexistence currently broken (esp-rs/esp-hal#6397, open). Directly affects hub BLE provisioning. Consider C3 for the hub, or validate C6 first.
- esp-idf-hal/-svc (std) now community-maintained, not Espressif-backed.

### BLE Wi-Fi provisioning

- Espressif unified provisioning (protocomm/protobuf over BLE GATT; Sec1 X25519+AES-CTR, Sec2 SRP6a), now the `network_provisioning` component. Official open-source ESP BLE Provisioning apps and mobile libraries exist.
- No native Rust crate; esp-idf-svc lacks a wrapper (issue #87). An Espressif blog post (Apr 2026) hand-rolled Sec1 in no_std Rust. Expect to implement it, or FFI to the C component. Improv Wi-Fi is an alternative with a Rust implementation.

### Soil sensing

- Use capacitive, not resistive (corrosion). Cheap "v1.2" capacitive boards often have an NE555 (bad at 3.3 V), unsealed edges, and non-linear output.
- Per-soil calibration needed (dry/wet endpoints minimum); temperature and salinity shift readings; settling time after insertion.
- Standard decision model: FAO-56 ET0 × crop coefficient → daily soil-water deficit, minus rain/forecast rain; trigger at allowed depletion. Best practice: combine sensor trend with ET model.

### Battery

- ESP-NOW vs Wi-Fi on the same node: ~3.7 yr vs ~6.9 mo (ThingPulse; figures from snippets). ESP-NOW wake ≈60 ms at 70–150 mA.
- Bare-module deep sleep 5–15 µA; dev boards 25 µA–mA — board design dominates. No Rust-specific sleep figures found.
