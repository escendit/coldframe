---
title: "Addendum: Coldframe PRD"
created: 2026-09-26
updated: 2026-09-26
---

# Addendum: Coldframe PRD

Depth that belongs to architecture and solution design, carried from the product brief, research, and PRD drafting. Not requirements; the PRD is authoritative on *what*.

> **Superseded where decided:** the architecture spine (`planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md`) is authoritative on *how*. Where this addendum lists options, the spine's decisions (AD-n) win.

## Hardware and firmware (from brief)

- Firmware in **Rust on ESP32-S3** for both Devices: `coldframe-node` (Sensors → ESP-NOW, deep sleep) and `coldframe-hub` (ESP-NOW ⇄ Wi-Fi, BLE provisioning). Two ESP32-S3 boards on hand.
- **ESP-NOW** chosen for garden distance beyond Wi-Fi reach.
- **Risk (a):** esp-radio lists S3 BLE/coexistence with caveats; the Hub needs Wi-Fi + BLE + ESP-NOW together — **validated by the Hub radio spike (2026-09-27): GO, S3 kept** (`docs/spikes/hub-radio-coexistence.md`). The C3 fallback is no longer needed. The spike's Hub requirements: join the strongest BSSID (scan all channels); enable TLS certificate-date checks (needs cmake and ninja); WPA3-only networks are unsupported in esp-radio 1.0.0-beta.1.
- **Risk (b):** ESP-NOW and Wi-Fi STA share one channel; the Node must follow the router's channel (drives the FR-4 channel-change criterion).

## Node

### Power budget (rough estimate, 800 mAh, one ESP32-S3)

Assumptions: ~640 mAh usable (80 % of 800 mAh LiPo), one wake every 15 min (96/day), wake = boot + read Sensors + ESP-NOW send. This table is the no-sun case; see Solar charging below.

| Scenario | Wake | Sleep current | mAh/day | Runtime |
|---|---|---|---|---|
| Optimized custom board | 0.3 s @ 80 mA | ~20 µA | ~1.1 | ~1.5 years (self-discharge limits it to ~1 year) |
| Reasonable custom board | 0.5 s @ 100 mA | ~50 µA | ~2.5 | ~8 months |
| Stock dev board (USB-UART, LED, LDO always on) | 0.5 s @ 100 mA | ~1 mA | ~25 | ~3–4 weeks |
| Any board + always-on air-quality sensor (e.g. MOX, ~1–4 mA) | — | +1–4 mA | 25–100 | 1–4 weeks |

**Budget for a 6-month season without solar:** ≈ 3.5 mAh/day total → average sleep current ≤ ~100 µA with wakes as above. Consequences (no-solar case; with solar per NFR-4 a stock dev board bridges ~25 dark days, so it is usable but not recommended): prefer a low-quiescent custom board for the Node; switch Sensors off during sleep; an always-on air-quality Sensor breaks the budget — the chosen BME680 in forced mode does not (see Sensors); keep the wake short (no Wi-Fi on the Node; channel re-scan only on send failure).

### Solar charging (NFR-4)

The Node's 800 mAh LiPo is charged by a small solar panel. Rough yield for a ~1 W panel: full sun ≈ 3 Wh/day ≈ 500+ mAh into the battery after charger losses; heavy overcast ≈ 5–10 % of that (25–50 mAh/day). Both exceed the ~2.5 mAh/day of a reasonable board, so solar turns the question from *runtime* into *autonomy*: how many dark or shaded days the battery bridges (≈ 250 days at 2.5 mAh/day; ≈ 25 days on a stock dev board; ≈ 6–25 days with an always-on air-quality sensor).

Design notes:
- **Shading is the real risk:** plants grow over the panel during summer; place it above the canopy. Shading shows up as falling battery % with *not charging* until the FR-14 Low-battery Alert fires.
- **LiPo temperature limits:** the charger must not charge below 0 °C or above ~45 °C (spring frost, panel-heated enclosure in full sun). Use a charger IC with thermistor (NTC) input.
- **Sleep-current budget relaxes** with solar, but a low sleep current still buys autonomy; the power table above remains the no-sun case.
- **Charging status (FR-4, FR-14):** most solar/LiPo charger ICs expose a status pin (e.g. TP4056 `CHRG`/`STDBY`, BQ24074 `CHG`/`PGOOD`, CN3791 `CHRG`/`DONE`) that the Node reads as a GPIO on each wake → *charging* / *not charging*. Fallback without a status pin: infer from the battery-voltage trend across daylight hours.
- **Battery % (FR-4):** measure battery voltage via ADC through a switched divider (so it draws nothing in sleep) and map it to % with a LiPo discharge curve; % is approximate, especially while charging.

### Buffering (FR-4, NFR-3)

The Hub is a pass-through proxy (ESP-NOW ⇄ Wi-Fi/TLS) and stores nothing. Reliability is end to end: the Server acknowledges each Reading (or batch), the Hub relays the acknowledgement back over ESP-NOW, and the Node keeps unacknowledged Readings and resends them on later wakes. 24 h at 15-min intervals is 96 Readings per Sensor — a few KB, which fits RTC RAM (survives deep sleep) or flash (survives power loss; mind write wear). Resends add wake time; batch them.

### BLE identification (FR-2)

The Node uses BLE only for setup: the mobile app finds it over BLE and reads its identity (e.g. MAC / device ID) so the Administrator can assign that physical Node to a Site and Lot. BLE advertising is limited to a setup mode entered by a **physical setup button** on the Node, with a timeout (duration for architecture, e.g. a few minutes) — continuous advertising would break the energy budget. The Node has no Wi-Fi, so the S3 BLE/Wi-Fi coexistence caveat (Risk (a)) does not apply to it; BLE and ESP-NOW coexistence on the Node still needs validation.

## Sensors

Soil-moisture probe notes are in Research notes below (unverified); probe choice is PRD Open Question 4.

### Air quality: Bosch BME680 (FR-3)

One BME680 covers temperature, humidity, pressure, and gas (VOC), so it serves the Node's temperature, humidity, and air-quality Sensors (pressure is measured but is not a V1 Sensor).

Power (Bosch datasheet): sleep 0.15 µA; 0.09–12 mA depending on mode; the gas heater draws up to ~12 mA **only during the gas sub-measurement** (~150 ms); ULP mode (300 s rhythm) averages < 0.1 mA. In forced mode at one measurement per 15-minute wake: 12 mA × ~0.15 s ≈ 1.8 mAs per wake ≈ **0.05 mAh/day** — negligible against the ~2.5 mAh/day Node budget. The always-on MOX row in the power budget does not apply to the BME680.

Catch — the IAQ index:
- The BME680 itself outputs **gas resistance (Ω)**, not an air-quality index. Bosch's **IAQ index** (0–500), eCO2, and bVOC come from **BSEC**, a **closed-source binary** under a Bosch licence agreement. It cannot be redistributed in the Apache-2.0 repositories; a Rust wrapper (`bsec` crate) exists, but users must obtain the library themselves.
- BSEC expects a fixed sampling rhythm (ULP = every 300 s, LP = every 3 s); 15-minute sampling is outside its supported modes. BSEC state must be saved across deep sleep (RTC RAM or flash) and needs days of burn-in to reach full accuracy.

Options considered: (a) report raw gas resistance and let Coldframe apply an open, simple baseline / relative-change threshold; (b) optional BSEC build that adopters enable themselves, waking every 5 min for the gas measurement (≈ 3× the wakes; affordable with solar); (c) BME688 (same family, same BSEC dependency for indices).

**Decision: (a) for V1** — raw gas resistance, standard low Threshold, no BSEC (PRD §6); (b) stays a possible later opt-in.

Sources: [BME680 datasheet](https://www.bosch-sensortec.com/media/boschsensortec/downloads/datasheets/bst-bme680-ds001.pdf); [Adafruit BSEC guide](https://learn.adafruit.com/adafruit-bme680-humidity-temperature-barometic-pressure-voc-gas/bsec-air-quality-library); [bsec Rust crate](https://docs.rs/bsec); [BSEC2 ESP32-S3 deep-sleep issue](https://github.com/boschsensortec/Bosch-BSEC2-Library/issues/31).

## Identity: Keycloak (FR-5, NFR-2)

User management is delegated to a self-hosted **Keycloak** via **OpenID Connect**. Keycloak runs alongside the Server on the home network (part of the same deployment). Mobile apps use Authorization Code + PKCE; the web app uses Authorization Code; the Server API validates access tokens. Accounts, passwords, registration policy, and (optionally) MFA are Keycloak's concern. Devices do not authenticate through Keycloak (separate Device credentials, see NFR-2).

Open for architecture: whether self-registration is enabled in the Keycloak realm or Users are created by an admin; how the mobile apps reach Keycloak's issuer URL on the home network (and later remotely); Keycloak's resource footprint on small home servers (e.g. Raspberry Pi).

### Decided: Sites as Keycloak Organizations (Phase Two, PRD Open Question 5, resolved by AD-3)

**Decided in architecture (AD-3).** Phase Two Organizations hold Sites, Memberships, and Roles. The Server's Site grain is the only writer, and it serializes FR-6/FR-7 checks. Changes flow back through `keycloak-temporal-extensions` → Temporal → Orleans into a local projection. Invitations use Phase Two's native flow. The original option analysis follows: model each **Site** as an Organization using the Phase Two Keycloak organizations extension, with **Memberships** and **Roles** (Owner, Administrator, Member) held in Keycloak. The Server's data model keeps only reference points — organization ID for a Site, subject ID for a User — and hangs Lots, Devices, Sensors, Readings, and Alerts off them.

What it buys: one source of truth for who belongs where; Site membership and roles can arrive as token claims; invitations and member management come from the extension instead of being built.

What to check in architecture:
- **Role hierarchy:** Owner ⊃ Administrator ⊃ Member must be expressed with Phase Two organization roles, which are flat per organization — build the hierarchy via composite roles or by granting all implied roles.
- **"Every Site keeps at least one Owner"** (FR-7) must be enforced even when members are changed through Keycloak's own admin UI or API.
- **Create Site** (FR-6): the Server (or the app via Phase Two's API) must create the Organization and grant the creator Owner atomically.
- **Revocation latency:** roles read from tokens stay valid until the token expires; keep access-token lifetimes short or check live for sensitive actions.
- **Invitations by email** (decided, FR-7): need an SMTP relay configured in Keycloak/Server; outbound traffic is allowed by NFR-1. The link points to the home-network hostname, so it only works on the LAN in V1.
- **Native Keycloak Organizations** (Keycloak 26+) were compared. As of Keycloak 26.7 they do support invitations and per-organization roles through org groups. Phase Two was kept because it is already in use (see the architecture memlog).
- **Lock-in:** the extension ties upgrades to Phase Two's release cadence for each Keycloak version.

## TLS on the home network (NFR-10)

TLS is mandatory for all IP traffic, even inside the LAN. Considerations for architecture:
- **Certificates (decided, AD-13):** use a real domain with Let's Encrypt DNS-01 certificates, resolved to the LAN by split DNS. It needs no inbound traffic and no trust setup on phones or the Hub. The private-CA option was rejected: the OIDC login runs in the phone's system browser, which ignores app-bundled trust anchors, and Android 11+ apps cannot install CAs. That would mean installing the CA by hand on every phone. A private CA remains only a documented fallback.
- **Hub:** ESP32-S3 can do TLS client connections (mbedTLS; in Rust e.g. esp-mbedtls or embedded-tls) — check RAM/flash footprint alongside Wi-Fi + BLE + ESP-NOW.
- **Node ↔ Hub (ESP-NOW):** not IP, so TLS does not apply. *Decided (AD-12, AD-17):* application-layer end-to-end sealing (ChaCha20-Poly1305) from Node to Server and back, with keys derived from a read-protected eFuse root. The Hub is an untrusted relay, and ESP-NOW's per-hop CCMP is not relied on.

## Deployment: cloud-native (NFR-7)

The author's direction is "cloud native all the way": Server, Keycloak, and database ship as OCI container images; configuration via environment variables; health/readiness endpoints; stateless Server process with state in the database.

**Reference deployment:** a **single-node Kubernetes cluster — Rancher RKE2** — on a home server, with **Fleet** as GitOps continuous delivery (cluster state from a Git repository).

Implications for architecture:
- **Footprint:** RKE2 plus Keycloak plus database plus Server needs a small x86 or ARM64 box with a few GB of RAM; state minimum hardware in the docs. Build multi-arch images if ARM64 home servers (e.g. Raspberry Pi 5) are supported.
- **Storage and durability (NFR-3):** single node means local persistent volumes; define backup/restore for the database and Keycloak.
- **Ingress on the LAN:** apps and the Hub reach the Server and Keycloak through the cluster ingress by a stable local hostname — ingress plus certificate management provides TLS (NFR-10).
- **No inbound (NFR-1):** no public ingress; outbound allowed. Push goes via APNs and FCM, outbound only; SMTP for invitations.
- **Reproducibility (NFR-7):** Fleet bundles / Helm charts in a public repo so an adopter points their own RKE2 + Fleet at it. RKE2 is considered easy enough to install for adopters; a Compose file ships as a reference example only (not the primary supported path).

## Research notes (unverified — verify before relying on them)

- **Firmware stack:** esp-hal 1.0 stable; esp-radio 1.0.0-beta (requires esp-hal `unstable`); BLE host TrouBLE. C3/C6 have fuller coex support; C6 STA+BLE coex had an open bug.
- **BLE provisioning (PRD Open Question 2):** Espressif unified provisioning (protocomm/protobuf over GATT, Sec1/Sec2) has official mobile libraries but no Rust crate; an Espressif blog hand-rolled Sec1 in no_std Rust. Improv Wi-Fi is a simpler alternative with a Rust implementation.
- **Soil sensing (PRD Open Question 4):** use capacitive probes, not resistive; cheap "v1.2" boards often use an NE555 (poor at 3.3 V), have unsealed edges and non-linear output — seal or coat. Per-soil dry/wet Calibration is the minimum; temperature and salinity shift readings; probes need 2–3 days to settle after insertion.
- **Battery:** ESP-NOW vs Wi-Fi on the same node ≈ 3.7 years vs ≈ 7 months (ThingPulse). ESP-NOW wake ≈ 60 ms at 70–150 mA. Bare-module deep sleep 5–15 µA; dev boards 25 µA–mA — board design dominates. Commercial claims ~12 months, real-world often weeks (see `research-landscape.md`).
- **Alert noise:** comparables fix flapping with a hysteresis band (~5 %) and offer "Mark watered"/"Snooze 24 h" actions and cooldowns (Mark watered/Snooze rejected for V1, PRD §6). Coldframe instead debounces: three consecutive Readings beyond a Threshold (FR-11). Rejected alternative: value-based recovery band — needs a per-unit margin per Sensor; consecutive Readings works the same for every Sensor type.
- **Differentiator:** local-first; competitor cloud integrations drop status changes after internet blips.

## Future: irrigation (NFR-9)

Design Device command paths (Server → Hub → Node) and a Device capability model so a valve or pump Device can be added without reworking Sites, Lots, Sensors, or Alerts.
