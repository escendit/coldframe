---
title: "Reconciliation: Brief → PRD (Coldframe)"
created: 2026-09-26
source: briefs/brief-coldframe-2026-09-25/{brief.md, addendum.md}
target: prds/prd-coldframe-2026-09-25/{prd.md, addendum.md}
---

# Reconciliation: Brief → PRD

Scope: brief content that is missing, weakened, or contradicted in the PRD + addendum **without** a `.memlog.md` decision explaining it. Changes the memlog records are treated as intentional and not listed here. Examples: bed → Lot, "several hours early" → Notification Window, outbound internet allowed, Keycloak, solar charging, RKE2/Fleet, pause per Device, all-Sensor Thresholds, no Mark watered/Snooze.

Severity: **high** = brief intent or success criterion lost; **medium** = weakened or only implicit; **low** = wording, candour, or completeness.

---

## R-1 — A failed Sensor is not detected (high)

- **Brief:** brief.md §What Makes This Different (l.42): "…with Device-health alerts so a failed Sensor is noticed." The same intent sits in §The Solution (l.36): "Silence must never be mistaken for 'all fine'."
- **PRD status:** Missing. Health Alerts cover only a silent Device (FR-13) and low battery (FR-14). A Node that keeps reporting while its probe is unplugged, shorted, stuck at one value, corroded, or out of its Specification range raises no Alert. The soil value can then sit above the low Threshold for good, which is exactly the "silence read as fine" failure the brief warns about. NFR-5 names corrosion drift but only as a survival goal; nothing detects it.
- **Suggested fix:** Add an FR under §4.5, e.g. "FR-14a Sensor fault Alert". A Health Alert opens when a Sensor reports a value outside its Specification range, reports an error or no value, or reports the same raw value for N hours [ASSUMPTION on N]. Add the Sensor-fault kind to the Glossary *Health Alert* entry and extend SM-3 to cover it.

## R-2 — Stale Readings can look current in the apps (medium)

- **Brief:** l.36: "Silence must never be mistaken for 'all fine'." Also l.24: competitors "mostly show data rather than tell you what to do."
- **PRD status:** Weakened. FR-8 shows the latest Reading per Lot plus a last-seen time. Nothing requires that a Lot whose Node is silent or paused be *marked* as unknown, stale, or paused rather than showing its last (possibly healthy) value. There is also no requirement for a decision-first view ("which Lots need water now") in the apps. JTBD Functional asks for exactly that view, but no FR delivers it.
- **Suggested fix:** Extend FR-8 or add an FR. Each Lot shows one status: *needs water* / *OK* / *unknown (silent since …)* / *paused*. The Site overview leads with Lots that need water. A Reading older than the Silence Window is never shown as the Lot's current state.

## R-3 — The PRD promises "system itself is broken" coverage but only covers Devices (medium)

- **Brief:** l.36 and Success Criteria l.53: "No silent failures."
- **PRD status:** Partially carried. The PRD strengthens the intent in JTBD *Trust* ("Tell me when the system itself is broken, so no news really means good news"). No FR or NFR covers the largest silent failure: the Server, database, or push path being down (Server crash, home internet outage, expired APNs/FCM credentials). In that case nothing reaches the phone and the gardener reads silence as fine. NFR-3 covers durability, not detection.
- **Suggested fix:** Pick one of two options:
  - (a) Add a lightweight requirement: the mobile app shows "last contact with Server" and warns when it is older than X. The Server could also send a periodic "still alive" digest in the Notification Window.
  - (b) Record Server-down detection as an explicit V1 non-goal and soften JTBD *Trust* to "when a Device is broken".

  Either way, the PRD should not promise more than it specifies.

## R-4 — "Rust on ESP32" is no longer a PRD-level constraint, and its reference value has no success measure (medium)

- **Brief:** l.14 "firmware (Rust on ESP32-S3)"; l.42 "a real-world, end-to-end Rust-on-ESP32 reference (ESP-NOW, BLE provisioning, deep sleep) for others to learn from"; Scope l.61 "Firmware in Rust on ESP32-S3".
- **PRD status:** Weakened. JTBD *Builder* keeps the intent, and §8 keeps ESP32-S3. "Rust" appears only in addendum.md, which states "Not requirements", and §0 sends technology choices there. Under the PRD's own rules, a C/ESP-IDF or ESPHome firmware would satisfy the requirements, but it would defeat a stated product value. No SM or NFR measures teaching or reference value: readable, documented firmware covering ESP-NOW, BLE provisioning, and deep sleep.
- **Suggested fix:** Add to NFR-8 or a new NFR: "Node and Hub firmware are written in Rust and documented as a reference for ESP-NOW, BLE provisioning, and deep sleep." Optionally extend SM-5 to cover it. Leave crate choices in the addendum.

## R-5 — Every Sensor alerts by default, which dilutes "one clear decision: water now" (medium)

- **Brief:** l.42: "whose job is a single, clear decision — *water now*"; l.90 Vision: "the 'water now' notification".
- **PRD status:** Tension, not covered by the memlog. The memlog decides that *any* Sensor's Threshold may be adjusted and that alerts are not limited to soil moisture (A.2). It does not decide that every Sensor alerts **out of the box**. FR-3 makes every Sensor "alert-ready with default Thresholds… without further setup". FR-10 then applies a generic fallback (low = Min + 20 % of range) to temperature, humidity, pressure, and BME680 gas resistance. In a garden, low humidity or low gas resistance is not a decision the gardener can act on. Frequent non-actionable Alerts undermine the single decision and SM-C1.
- **Suggested fix:** Pick one of two options:
  - Soil-moisture Threshold Alerts are on by default and other Sensors' Alerts are off until an Owner or Administrator enables them.
  - Keep them on, but require the Sensor Specification (not the generic fallback) to supply meaningful defaults, and rank "needs water" first in notifications and the summary (FR-16).

  Confirm with the author.

## R-6 — Radio reach "beyond Wi-Fi" is not a requirement (medium)

- **Brief:** l.28: "It reports over ESP-NOW, so it works at the far end of the garden, beyond Wi-Fi reach"; l.46 primary user has "a node at a distance from the house"; brief addendum: "ESP-NOW was chosen for the garden distance."
- **PRD status:** Missing as a requirement. UJ-3 mentions "the far end of the garden", and the addendum records the ESP-NOW rationale. No FR or NFR states a range or placement condition that the Node must meet. Validation could therefore pass with the Node next to the house.
- **Suggested fix:** Add an NFR: "A Node reports reliably from the author's farthest Lot, where the home Wi-Fi is not usable [state distance, e.g. ≥ N m through garden/wall]." Tie it to SM-1.

## R-7 — The dryness-measurement risk is narrowed to probe choice (medium)

- **Brief:** Open Questions l.82: "Cheap capacitive probes are non-linear, soil-dependent, and drift with temperature, so probe choice and Calibration method matter." Brief addendum: "temperature and salinity shift readings; settling time after insertion."
- **PRD status:** Weakened. FR-9 fixes a two-point linear Calibration, and PRD Open Question 4 keeps only "Probe choice and sealing". Non-linearity and temperature drift are not tracked as a risk, although SM-2 depends on the calibrated 0 % being the real dry point. The addendum notes the 2–3 days of settling after insertion, but no FR handles it: Alerts or Calibration during settling may be misleading.
- **Suggested fix:** Restore an Open Question or risk: "Is two-point linear Calibration accurate enough across the season's temperature range for SM-2? Is temperature compensation needed (the BME680 temperature is available)?" Optionally have FR-9 recommend or enforce Calibration only after the settling period.

## R-8 — "No technical moat" candour and competitive framing were dropped (low)

- **Brief:** l.42: "Honestly: no technical moat. The pieces exist separately. Coldframe's value is putting them together…"; l.20–24: why DIY, weather-based, and commercial options fall short.
- **PRD status:** Missing by design. §0 says the PRD "does not repeat its market narrative". The honest positioning (integration value, not invention) helps readers and adopters scope expectations, and the tone of the brief is part of its identity. The PRD Vision keeps "honest about its own failures" but not honesty about differentiation.
- **Suggested fix:** Add one sentence to §1 Vision: "Coldframe has no technical moat; its value is combining existing pieces into one open, self-hosted system whose job is one decision."

## R-9 — "With the gardener still in control" of irrigation has no design hook (low)

- **Brief:** l.90 Vision: "the 'water now' notification becomes a valve opening, with the gardener still in control."
- **PRD status:** Phrase kept in §1. NFR-9 and the addendum's irrigation section cover only command paths and a capability model. Nothing preserves control: human confirmation, manual override or stop, or Pause applying to actuators.
- **Suggested fix:** Extend NFR-9 or the addendum *Future: irrigation* with: "the command model allows a human-confirmed mode and a manual stop, and Pause blocks commands."

## R-10 — Vision elements dropped: more nodes and beds, soil + weather (low)

- **Brief:** l.90: "more nodes and beds, soil data combined with weather"; l.77: "Forecasts may later complement it ('rain tomorrow, skip')."
- **PRD status:** The Non-Goals keep weather out of V1, but the PRD Vision drops the long-term "open, self-hosted brain for a home garden" framing and the weather-as-complement direction.
- **Suggested fix:** Add one line to §1 or §8 *Out (later)*: "weather as a complement to soil (e.g. 'rain tomorrow, skip')", so it is clearly deferred rather than rejected.

## R-11 — Sensor Specification loses "by which method" (low)

- **Brief:** brief addendum *Sensor Specification*: "whether it is calibratable (and by which method)."
- **PRD status:** The Glossary has only `calibration: true|false`. FR-9 hard-codes two-point dry/wet, so a future Sensor with a different method has no place to declare it.
- **Suggested fix:** Add an optional `calibration_method` (V1: `two-point`) to the Sensor Specification in the Glossary and FR-3.

## R-12 — Reproducibility: hardware designs are not in the open-source scope (low)

- **Brief:** l.14: "all public, so other gardeners can build the same setup"; l.55: "someone other than the author can build a node and hub… from the public docs."
- **PRD status:** NFR-8 lists firmware, Server, and apps. The addendum power budget concludes "no stock dev board for the Node" and adds a solar panel and charger IC. The Node hardware (schematic, BOM, enclosure, sealing) therefore becomes necessary to reproduce the setup, but it is not listed as a public artifact.
- **Suggested fix:** Extend NFR-7 or NFR-8: the Node and Hub hardware (BOM, wiring or schematic, enclosure and probe sealing notes) is published with the docs.

## R-13 — The secondary persona and non-user bar were not updated for the Kubernetes path (low, consistency)

- **Brief:** l.47: makers "comfortable flashing an ESP32 and running a small server."
- **PRD status:** The memlog decides RKE2 + Fleet + Keycloak as the supported path, which is a deliberate choice. §2.2 Non-Users still sets the bar at "run a small server". The adopter bar has risen (Kubernetes, GitOps, identity provider, private CA for NFR-10), but neither the Target User section nor the persona says so.
- **Suggested fix:** Update §2.2 or add a secondary-user line: adopters are expected to run a single-node Kubernetes cluster from the docs. Alternatively, state the minimum skills in NFR-7.

---

## Carried correctly (no action)

Push away from home and app on the LAN only; remote access as the next step; no hosted service; ESP-NOW channel following (FR-4); BLE-only Hub provisioning with no hard-coded credentials (FR-1); Roles and Memberships per Site; multi-Site automated tests (NFR-6); Pause without failure alerts (FR-18); re-notification policy (FR-11/12); iOS and Android both mandatory (FR-19); hub radio and BLE-provisioning risks (Open Questions 1–2, addendum); SM-1, SM-3, SM-5 match the brief's success criteria. Early warning, battery, and deployment changes are memlog decisions.
