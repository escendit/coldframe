---
title: "Polish review: PRD Coldframe"
target: prd.md
lenses: structure, prose
date: 2026-09-26
---

# Polish Review — PRD Coldframe

**Read:** This document exists to help the author and downstream builders (architecture, UX, epics/stories) know exactly what Coldframe V1 must do, in fixed vocabulary.
**Structure model:** Requirements/specification document (reference: scannable, ID-stable, rules before consequences).
**Reader:** humans (default). **Style guide:** Microsoft Writing Style Guide. **Baseline:** 4,176 words.

Content was treated as sacrosanct: no requirement, number, decision, or ID changed. "V1" casing was already consistent (no lowercase "v1" found).

## Findings

| Pass | Original Text | Revised Text | Changes | Status |
| --- | --- | --- | --- | --- |
| structure | FR-13 bullets: scope bullet ("Applies to both Nodes and Hubs…") last | MOVE to first bullet | Scope before defaults and mechanism; 0 words | Applied |
| structure | FR-10 bullets: constraints (low required, low < high) after defaults and clearing | MOVE constraints to top; "clear/set high" last | Rules first, then lifecycle; "clear a high Threshold" now follows the rule that high may be empty; 0 words | Applied |
| structure | FR-11 bullets: message wording before hysteresis/timing | MOVE hysteresis and timing up; message after | Consequences of the three-Reading rule follow it directly; 0 words | Applied |
| structure | FR-18 bullets: "On resume…" before "optional end date" | MOVE end-date bullet up | Pause lifecycle in order (start, end, resume); 0 words | Applied |
| structure | FR-14 bullet "Health Alerts remind at most once per day" — a rule for all Health Alerts housed under FR-14 | PRESERVE; FR-21 now cites "in FR-14" | Moving it would re-home a requirement; flag for PM if FR-13 should cite it too | Partly applied |
| structure | §0 "All assumptions … resolved" vs §10 Assumptions Index | PRESERVE | Mild overlap, but §10 is the index slot; reinforcement, not redundancy | No change |
| structure | §8 MVP Scope "Out" vs §6 Non-Goals | PRESERVE | Scope summary is a useful recap | No change |
| structure | §4.8 Apps has no **Description:** line, unlike §4.1–4.7 | QUESTION | Missing scaffolding; not added (no new content allowed) | Flag for PM |
| prose | Honestly, there is no technical moat | There is no technical moat | Removed filler | Applied |
| prose | calibrates its soil-moisture Sensor dry and wet | calibrates its soil-moisture Sensor (dry and wet) | Clarity | Applied |
| prose | neighbour (×3, UJ-5); behaviour (NFR-6); towards (OQ-5) | neighbor; behavior; toward | Consistent US spelling (doc uses "normalized") | Applied |
| prose | Owner or Administrator can override them per Sensor | an Owner or Administrator can override them… | Missing article | Applied |
| prose | Cadence resolves the User's own setting for the Site → Site setting → default | Cadence resolves in order: the User's own setting for the Site → the Site setting → the default | Clarify precedence | Applied |
| prose | …assigns it to a Site and Lot; the Node then reports | …; from then on, the Node reports | Removed doubled "then" | Applied |
| prose | calibration controls (FR-3); its alert uses (FR-3) | Calibration controls; its Alert uses | Glossary capitalization | Applied |
| prose | …turning alerts on for watched Sensors…; others cannot. | …, which turns alerts on for watched Sensors…. Other Users cannot. | Split run-on; clear antecedent | Applied |
| prose | A single Reading or two … neither opens nor closes | One or two Readings … neither open nor close | Agreement and clarity | Applied |
| prose | On resume, the Silence Window starts again | On resume, the Silence Window restarts | Concision | Applied |
| prose | It follows the Health Alert Reminder rule (at most once per day). | …rule in FR-14 (at most once per day). | Explicit cross-reference | Applied |
| prose | Alerts of both kinds and Reminders | Threshold Alerts, Health Alerts, and Reminders | Glossary terms verbatim | Applied |
| prose | FR-19 list "viewing Readings, Thresholds, Calibration, Pause, …" | Semicolon-separated list | "viewing" no longer reads as governing every item | Applied |
| prose | except anything that needs Bluetooth | except features that need Bluetooth | Precision | Applied |
| prose | NFR-4 "…without manual recharging at the FR-4 interval." | "…and, at the FR-4 interval, runs a full season … without manual recharging." | Misplaced modifier | Applied |
| prose | No remote access to the app or web app | No remote access to the mobile apps or web app | Consistent naming | Applied |
| prose | No air-quality index (IAQ, eCO2) in V1: | No air-quality index (IAQ, eCO2): | "V1" already in section heading | Applied |
| prose | (delivered at the next window opening) | (delivered when the Notification Window next opens) | Glossary term | Applied |
| prose | "e.g." used throughout | Consider: "for example"? | Microsoft style prefers "for example"; left as consistent house usage | Not applied |

## Flagged for PM (not changed — would alter meaning)

1. **Sensor vs Device (Glossary, FR-3):** Glossary says a Sensor is "on a Device" and FR-3 says "Each Device declares its Sensors", but only Nodes carry Sensors. Should these say "Node"?
2. **§6 "Sensor alerts are based only on Thresholds"** conflicts with FR-21 (Health Alert for an uncalibrated Sensor). Consider "Threshold Alerts are based only on Thresholds" or a carve-out for FR-21.
3. **Vision "When a Lot crosses its Threshold"**: Thresholds belong to Sensors, not Lots. Consider "When a Lot's soil moisture crosses its Threshold".
4. **Health Alert Reminder rule** sits in FR-14 but applies to FR-13 and FR-21 as well; consider whether FR-13 should reference it.
5. **"alerts" (lowercase) as verb/generic** in FR-3 and FR-10 ("turns on alerts") left as is; confirm this is not meant as the Glossary noun Alert.
6. **§4.8** lacks the Description line the other feature groups have.

## Summary

25 recommendations applied (4 structural moves, 21 prose fixes); net word change about +10 words (under 1 %). No length target given. No comprehension trade-offs; no sections added or removed; all IDs (FR-1..FR-21, NFR-1..NFR-11, UJ-1..UJ-5, SM-*, Open Questions 1–5) unchanged.
