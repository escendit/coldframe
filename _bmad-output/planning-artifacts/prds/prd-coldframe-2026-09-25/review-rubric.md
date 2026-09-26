# PRD Quality Review — Coldframe (Garden Intelligence Platform)

Calibrated for stakes: hobby / solo, built in the open, chain-top (feeds architecture, UX, epics). Enterprise sections (compliance, rollout, stakeholder sign-off) are deliberately not expected.

## Overall verdict
A sharp, honest hobby PRD. The thesis ("*water now*", and silence is never "all fine") is clear, and the FRs are mostly concrete and testable, with specific numbers (3 consecutive Readings, 6 h / 5 min Silence Windows, 20 % battery, 14 days without sun). Non-Goals and counter-metrics do real work. The risk is in the seams of the core alert path. The PRD never says how Reminders interact with the Notification Window, what the window's "to" defaults to, or what happens to a raw-unit Threshold when a Sensor is calibrated. There is also stale text from the many edits: broken Open Question references in FR-5 and the addendum, and a §0 promise of `[ASSUMPTION]` tags that no longer exist. Fix those and it is ready for architecture.

## Decision-readiness — strong
Decisions are stated as decisions, and the PRD usually says what was given up. FR-7 accepts that "Accepting the invitation requires being on the home network in V1". §6 rejects "Mark watered"/"Snooze" and gives the reason ("the Sensor sees the watering and closes the Alert itself"). §6 also drops the IAQ index to keep "every repository fully open". NFR-7 names Compose as "not the primary supported path". A reader pushing back on any of these finds the objection already answered.

The Open Questions (§9) are really open. Four of the five (Hub radio coexistence, BLE provisioning protocol, native vs cross-platform, probe sealing) are architecture/hardware questions rather than product questions. That is fine for a hobby PRD, but it means the PRD has no open *product* question except OQ 5. That one leaks into FR-5 as a hedge ("may be held in the identity provider"). The hedge is at least bounded: "Either way, the rules in FR-6 and FR-7 apply unchanged."

### Findings
- **low** Open Questions are mostly implementation questions (§9 OQ 1–4) — They are legitimate risks but belong to architecture. Keeping them here is harmless as long as architecture picks them up. *Fix:* optionally label them "architecture risks carried forward" so the PRD's own product decisions read as closed.

## Substance over theater — strong
There is no persona theater: one protagonist (Simon) and one supporting role (the neighbour in UJ-5), and each drives FRs (Membership/Role rules, per-User Notification Window). Every JTBD in §2.1 maps to features or NFRs; even "Builder" maps to NFR-7/NFR-8. The Vision could not be swapped into another PRD. NFRs mostly carry product-specific bounds: NFR-4 "about 6 months, April–October … at least 14 days" with no sun, and NFR-10 "including on the home network". Two NFRs are softer.

### Findings
- **medium** NFR-5 has no bound (§5 NFR-5) — "without corrosion-driven drift making Alerts unreliable" is an adjective, not a threshold. *Fix:* state a measurable bound, e.g. "a calibrated soil-moisture Sensor drifts by no more than N percentage points over the season against a re-check in dry soil and in water".
- **low** NFR-6 is a test strategy, not a quality attribute (§5 NFR-6) — "covered by automated tests" describes how to verify, not what must hold. That is acceptable for a hobby project. *Fix:* phrase it as the property ("Role and Membership rules hold across ≥2 Sites with overlapping Users") and keep automated tests as the means.

## Strategic coherence — adequate
The thesis is explicit, and prioritization follows it: the soil-moisture Threshold Alert (FR-10–FR-12) and self-monitoring (FR-13–FR-14) are the heart of the PRD. SM-1/SM-2 test the thesis rather than activity. The counter-metrics are well chosen: SM-C1 explicitly guards against "training the gardener to ignore Coldframe". The MVP is problem-solving scope, and the scope logic matches.

The weak spot is the non-soil Sensors. The Vision lists "temperature, humidity, and air quality", and FR-3/FR-10 make every Sensor "alert-ready with the default Thresholds … without further setup". Yet no JTBD, UJ or SM gives a reason for a temperature, humidity or gas-resistance push notification. Per-Sensor default alerting on sensors that do not serve "water now" is exactly the noise SM-C1 warns about.

### Findings
- **medium** Non-soil Sensors alert by default without a thesis-level reason (§1, FR-3, FR-10, SM-C1) — FR-3's "alert-ready … without further setup", combined with the FR-10 fallback "Min + 20 % × (Max − Min)", means humidity, temperature and gas resistance all open Threshold Alerts and Reminders out of the box. For the BME680, raw gas resistance varies strongly per unit and with burn-in (addendum §Air-quality). A spec-default Threshold is therefore close to meaningless, and FR-3's promise is doubtful for that Sensor. *Fix:* decide explicitly. Either (a) non-soil Sensors are recorded but do not alert unless a Threshold is set by the User, or (b) name the gardener decision each one serves (e.g. frost warning) and add it to a JTBD/UJ. Also mark air quality as "recorded, not alerting by default" in V1.
- **low** SM-1 is hard to attribute (§7 SM-1) — "no plant lost to missed watering" is fine as a hobby north star, but SM-2 is the real measurable test. *Fix:* none required. Optionally note that SM-2 is the operational proxy for SM-1.

## Done-ness clarity — adequate
Most FRs have crisp, testable consequences. Examples: FR-1 "appears on the chosen Site within one minute"; FR-11 "three consecutive Readings" with an explicit worked delay; FR-14's boundary cases; FR-16 "An Alert that opened and closed while held is not delivered". This is well above typical. The gaps are clustered on the core notification path, which is the part story creation will lean on hardest.

### Findings
- **high** Reminders vs Notification Window is undefined (FR-12, FR-16, Glossary "Reminder", UJ-2) — FR-16 holds "Alerts of both kinds that open outside the window", but says nothing about Reminders. Open questions: does a once-per-day Reminder fire at window opening, 24 h after the Alert opened, or 24 h after the last notification? Does a held Alert plus a due Reminder produce one notification or two? UJ-2 ("the reminder repeats once the next day") implies window-aligned delivery, but no FR says so. SM-C1 ("at or below the Reminder cadence") cannot be checked without this. *Fix:* add a consequence to FR-12, e.g. "Reminders are delivered only inside the User's Notification Window; the first Reminder is due at the first window opening at least 24 h after the Alert was last notified; a due Reminder and held Alerts are combined into the FR-16 summary."
- **high** Threshold units across Calibration are undefined (FR-9, FR-10, Glossary "Sensor Specification") — Default Thresholds come from the Sensor Specification in the Sensor's raw range. FR-9 says that after Calibration "Readings and Threshold are shown in normalized units (0–100 %)", but does not say what happens to an existing raw Threshold. Is it converted through the Calibration? Reset to a %-default? What happens on recalibration? This decides whether the core "Tomatoes needs water" alert fires at the right moment (SM-2). *Fix:* add to FR-9, e.g. "On Calibration, the Sensor's Thresholds are re-expressed in normalized units; if still at the Specification default they reset to N % low; a User-set Threshold is converted through the new Calibration and shown for confirmation."
- **medium** Notification Window "to" is undefined (Glossary "Notification Window", FR-16, UJ-1) — "simplest form 'from 07:00'" leaves the end implicit (midnight? 24 h?). An Alert opening at 23:30 is either delivered immediately or held until 07:00, depending on the answer. *Fix:* state the default end (e.g. "without 'to', the window ends at 22:00" or "at midnight") and whether windows may cross midnight.
- **medium** Low-battery Alert can flap (FR-14) — It opens below 20 % and closes at "20 % or more". The addendum notes that % is "approximate, especially while charging", so a battery oscillating at 19–20 % will open and close repeatedly. This contradicts the anti-flapping principle of FR-11. *Fix:* apply the same three-consecutive-Readings rule, or close at ≥ 25 %.
- **medium** Readings in flight during outages are unspecified (NFR-3, FR-4) — NFR-3 covers *stored* Readings on restart. It does not say whether a Node or Hub buffers Readings while the Server or Hub is down, which matters for FR-11 counting and FR-8 history. FR-4's "Readings carry the Node's measurement time" hints at buffering, but no requirement states it. *Fix:* state the intent, e.g. "Readings produced while the Server is unreachable for up to N hours are delivered late with their measurement time", or explicitly "may be lost; gaps are shown in history".
- **low** Channel change has no time bound (FR-4) — "Readings keep reaching the Server after the router changes its Wi-Fi channel" does not say within what time or how many missed Readings. *Fix:* "…within two reporting intervals".
- **low** Health Alert reminder rule sits under FR-14 but governs FR-13 too (FR-14 last bullet) — "Health Alerts remind at most once per day" is placed under the Low-battery FR. It also conflicts with the Glossary "Reminder" cadence resolution, which would let a User choose more often. *Fix:* move it to FR-12 or FR-13, and state that the User/Site cadence applies to Threshold Alerts only.
- **low** Counting across gaps in FR-11 — It is unclear whether a missing Reading breaks "three consecutive". *Fix:* one clause: "a missing Reading neither counts nor resets".

## Scope honesty — strong
§6 Non-Goals does real work: each item closes a door a reader might assume is open (valve control, remote access, IAQ, Snooze), often with the reason. FR-20 explicitly says "no background web push in V1". FR-15 says opening the app "requires the home network in V1". The open-items density is low (5 OQs, 0 assumptions), which is appropriate for a hobby green-light. The only tension is wording. §6 says "no … commands to Devices", while NFR-9 requires the paths to allow them later. The two are compatible (shape now, don't build), but only NFR-9 makes that clear.

### Findings
- **medium** Stale §0 promise of `[ASSUMPTION]` tags (§0, §10) — §0 says "Inferred items carry inline `[ASSUMPTION]` tags and are indexed in §10", but there are none. §10 says "None open" and points to `.memlog.md`, which is outside the public contract. *Fix:* change §0 to "All inferences were confirmed with the author; §10 records that none remain open", or drop the sentence.
- **low** Non-Goal vs NFR-9 wording (§6 first bullet, NFR-9) — *Fix:* reword the Non-Goal to "No irrigation control in V1 — no valves or pumps; command paths are shaped (NFR-9) but not built".

## Downstream usability — adequate
IDs are contiguous and unique: FR-1–FR-20, NFR-1–NFR-10, UJ-1–UJ-5, SM-1–SM-5 plus SM-C1/C2. Each feature names the UJs it realizes, and each SM names the FRs/NFRs it validates. The Glossary is strong, with precise cardinalities ("a Lot has at most one Node, and a Node sits on exactly one Lot") and a clear Role hierarchy, and capitalized terms are used almost everywhere. What hurts extraction are the broken cross-references left by Open Question renumbering, and a few Glossary drifts in load-bearing places.

### Findings
- **medium** Broken cross-reference: "Open Question 10" (FR-5) — The PRD has only five Open Questions. The Keycloak-Organizations question is OQ 5 (the memlog shows it was logged as OQ 10 before renumbering). *Fix:* "see Open Question 5".
- **medium** Addendum points at renumbered or removed Open Questions (addendum §Hardware "PRD Open Question 3"; §Research "Alert noise … (PRD Open Question 1)"; §Deployment "relevant to Open Question on LAN TLS") — Hub radio feasibility is now OQ 1, not OQ 3; OQ 3 is mobile tech. The Alert-noise question was closed into §6/FR-11. The LAN-TLS question was closed into NFR-10. An architect following these links lands on the wrong question. *Fix:* "Open Question 1"; replace "(PRD Open Question 1)" with "(closed: FR-11, §6)"; replace "Open Question on LAN TLS" with "NFR-10".
- **medium** "Member" used where "User" is meant (Glossary "Reminder": "Member setting → Site setting"; FR-12 "the Member's own setting") — Member is a Role. Read literally, Owners and Administrators have no personal cadence, which contradicts FR-12's own bullet "every User may set their own". *Fix:* "User's own setting (per Site)" in both places.

## Shape fit — strong
The shape is right for a hobby, single-operator, chain-top PRD. There are five short UJs with a named protagonist, no enterprise furniture, and deep technical material pushed into `addendum.md`. The main shape concern is implementation leaking into the PRD body, despite §0's rule that "Technology choices … live in addendum.md". Some of this is justified for a builder-oriented open-source project (the "Builder" JTBD explicitly names Rust-on-ESP32). But Keycloak and Kubernetes/GitOps are solution choices that constrain architecture from inside the requirements.

### Findings
- **medium** Implementation leakage in FR-5 (FR-5, OQ 5) — "(Keycloak, see addendum)" and "Sites, Memberships, and Roles may be held in the identity provider, with the Server keeping only references" is an architecture decision placed inside an FR. The requirement is "sign in via a self-hosted OpenID Connect provider; the Server stores no passwords". *Fix:* keep that, move Keycloak and the storage-location hedge to the addendum/OQ 5, and keep the sentence "Either way, the rules in FR-6 and FR-7 apply unchanged".
- **low** Deployment stack in NFR-7 (NFR-7) — "single-node Kubernetes cluster … delivered by GitOps" is the author's real, confirmed direction, and it shapes the reproducibility docs. Keep it if it is a deliberate constraint, but label it as such. *Fix:* "Constraint (author choice): …", with RKE2/Fleet staying in the addendum as now.
- **low** Hardware in MVP Scope (§8 "on ESP32-S3") — This is consistent with the Builder JTBD, but it contradicts §0's "chip … live in addendum.md". *Fix:* either soften §0 ("except the ESP32 platform, which is a product commitment") or drop the chip from §8.

## Mechanical notes
- **Glossary drift:**
  - **UJ-3** names a Node ("Node 'Beans' silent for 6 h"), but Nodes have no name in the Glossary or FRs; Lots do. Use "Node on Lot 'Beans'", or add a Device name to the Glossary/FR-2.
  - "garden" is used as a synonym for Site in NFR-6 ("Only one garden is field-tested") and SM-1. That is fine in narrative (§1, UJs) but should read "Site" in NFR/SM text.
  - "gardener" (§1, SM-C1) is used for User. It is acceptable in narrative.
  - "bed" and "gateway" appear only inside Glossary definitions (Lot, Hub), which is fine.
  - Lowercase "alert" in FR-3 ("its alert uses the low Threshold") should be "Threshold Alert".
  - Lowercase "node" does not appear. Device/Node/Hub casing is consistent.
- **Undefined terms:**
  - "Friendly Name" (Glossary "Site") is capitalized as a term but never defined or used again.
  - "registers" (FR-3 "when it registers") has no defined moment. Is it on assignment (FR-2), or on first contact?
  - "Alert settings" (Glossary "User") is not defined.
  - "Site setting" / "Site cadence" is not in the Glossary.
- **Hub and battery fields (FR-8):** "Each Device shows its … battery level (%), and charging status", but a Hub is mains-powered (Glossary). Use "Each Node" for battery and charging status, and "Each Device" for last-seen.
- **Single vs multiple Hubs:** FR-2 says "that Site's Hub" (singular) and §8 says "one Hub", but the Glossary does not bound Hubs per Site. State "a Site has exactly one Hub in V1" if that is the intent.
- **Site-level Pause state:** FR-18's "Devices added to a paused Site start paused" implies the Site itself has a Pause state, but the Glossary defines Pause only as "a state of a Device". Add Site Pause to the Glossary, and say what resuming one Device inside a paused Site means.
- **Sensor Specification defaults:** The Glossary says the Specification declares a "default low and high Threshold", while FR-10 handles Specifications that give neither. Change the Glossary to "optional default low and high Threshold".
- **Addendum numeric drift:** The Solar and BME680 sections use "~2.5 mAh/day Node budget", while the power-budget section states "≈ 3.5 mAh/day total" as the budget (2.5 is the "reasonable board" consumption). Say "consumption" vs "budget" consistently.
- **Addendum air-quality options:** Option (a) in "Options considered" describes "an open, simple baseline / relative-change threshold", but the recorded decision is "standard low Threshold". Align the option text with the decision.
- **Assumptions Index roundtrip:** There are no inline tags and §10 says none are open, so the roundtrip is consistent. Only the §0 sentence is stale (see Scope honesty).
- **UJ protagonists:** All five UJs carry Simon inline. The neighbour in UJ-5 is unnamed but carries their Role, which is sufficient.
- **Required sections for stakes:** All present (Vision, users/JTBD, UJs, Glossary, FRs, NFRs, Non-Goals, SMs with counter-metrics, MVP scope, OQs). Nothing is missing for a hobby PRD.
