---
title: "Polish review: addendum.md"
created: 2026-09-26
reviewed: addendum.md
lenses: structure, prose (bmad-review)
---

# Polish review: addendum.md

**Read:** this addendum exists to give the architect and solution designer the technical depth (hardware, power, identity, TLS, deployment, research) behind the Coldframe PRD, without restating requirements. Reader type: humans. Style guide: Microsoft Writing Style Guide. Structure model: reference / design-notes document, grouped by system area, decisions before research, future last.

Original: 2,100 words, 13 H2 sections appended in discovery order. All findings below were **applied** to `addendum.md`.

| Pass | Original Text | Revised Text | Changes |
|---|---|---|---|
| structure | Section order: Hardware → Research notes → Future → Power budget → Solar → BLE → Keycloak → Deployment → TLS → BME680 → Buffering | MOVE into: Hardware and firmware → Node (Power budget, Solar charging, Buffering, BLE identification) → Sensors (pointer to soil notes, BME680) → Identity: Keycloak (Organizations candidate) → TLS → Deployment → Research notes (unverified) → Future: irrigation | Document grew by appending; regroups by system area, decisions before unverified research, future last. No content removed (~0 words) |
| structure | §Node power budget, §Solar charging, §Node BLE identification, §Node buffering (four separate H2s) | MERGE under `## Node` as H3s; drop the redundant "Node" prefix from each heading | One place for all Node design notes |
| structure | §Hardware — "Push via APNs and FCM, outbound only (NFR-1)." | MOVE into §Deployment "No inbound (NFR-1)" bullet | Push is a Server/network concern, not hardware; that bullet already named APNs/FCM |
| structure | §Research notes — "The S3 decision stands; C3 is the fallback if validation fails." | MOVE to §Hardware Risk (a) | A decision, not an unverified research note; sits next to the risk it resolves. Unverified caveat on the remaining notes unchanged |
| structure | §BME680 — "Decision: (a) for V1" placed before "Options considered: (a)…(c)" | MOVE Decision after Options | (a) was referenced before it was defined |
| structure | (none) — soil-sensing depth only in Research notes | ADD one-line `## Sensors` pointer to Research notes and PRD Open Question 4 | Sensors section otherwise covered only air quality; soil note kept in the unverified section so its caveat holds |
| structure | §Power budget consequences — "an air-quality Sensor must be duty-cycled or dropped from the battery Node" | CONDENSE/REFRAME: "an always-on air-quality Sensor breaks the budget — the chosen BME680 in forced mode does not (see Sensors)"; budget heading labelled "without solar"; table intro notes it is the no-sun case | Stale: read as an open problem, superseded by the BME680 analysis and PRD FR-3/NFR-4. Numbers unchanged |
| structure | Cross-references | ADD: Solar → NFR-4; BME680 → FR-3, PRD §6; Organizations candidate → Open Question 5; Invitations → FR-7; Research BLE provisioning → OQ2; soil sensing → OQ4; Irrigation heading → NFR-9 | Links each area to the PRD item it serves. All existing and new refs verified against prd.md (see below) |
| prose | "carried from the product brief and research." | "carried from the product brief, research, and PRD drafting." | Intro no longer understated the source of later sections |
| prose | "(drives FR-4 consequence)" | "(drives the FR-4 channel-change criterion)" | Vague referent |
| prose | "The Node also uses BLE, only for setup:" | "The Node uses BLE only for setup:" | Comma splice / misplaced "also" |
| prose | "coexistence caveat does not apply" | "coexistence caveat (Risk (a)) does not apply" | Clear antecedent after reorder |
| prose | "Shows up as falling battery % …" | "Shading shows up as falling battery % …" | Missing subject |
| prose | "(they are flat per-organization roles — hierarchy via composite roles or …)" | ", which are flat per organization — build the hierarchy via composite roles or …" | Parenthetical carried the instruction; made it a clause |
| prose | "The earlier worry about always-on MOX sensors does not apply" | "The always-on MOX row in the power budget does not apply" | "earlier" depended on the old section order |
| prose | "— so it can serve"; "exists but users"; "infer from battery-voltage trend"; "map to %" | ", so it can serve"; "exists, but users"; "infer from the battery-voltage trend"; "map it to %" | Minor punctuation and article fixes |

## Cross-reference verification (against prd.md)

All correct: OQ1 (Hub radio), OQ2 (BLE provisioning), OQ4 (probe/drift), OQ5 (where Sites/Roles live); FR-2 (setup button, BLE identify), FR-3 (gas resistance Ω, low Threshold), FR-4 (channel change, battery %, charging status, 24 h buffer), FR-5 (OIDC/Keycloak), FR-6 (create Site), FR-7 (at least one Owner, email invite, LAN-only), FR-11 (three consecutive Readings), FR-14 (low battery, not charging); NFR-1, NFR-2, NFR-3, NFR-4, NFR-7, NFR-9, NFR-10; §6 Non-Goals (no IAQ/BSEC; no Mark watered/Snooze).

## Removed or reframed as stale

- Power budget: "an air-quality Sensor must be duty-cycled or dropped from the battery Node" → reframed as a table-derived consequence resolved by the BME680 choice.
- BME680: "The earlier worry about …" → reworded to point at the table row.
- "Hub is mains-powered": not present in the addendum; nothing to remove (matches PRD glossary anyway).

## Flagged for the PM (not changed)

1. **No-sun power budget vs NFR-4.** The 6-month no-solar budget (≈ 3.5 mAh/day, "no stock dev board") predates solar. NFR-4 now asks for solar plus ≥ 14 days with no sun; per the Solar section a stock dev board bridges ≈ 25 days. Decide whether "no stock dev board for the Node" is still a hard consequence or now a recommendation for autonomy margin.
2. **BME680 "three of the Node's Sensors"** lists four quantities (temperature, humidity, pressure, gas); PRD names temperature, humidity, and air quality. Pressure is not a PRD Sensor — confirm it is intentionally unused/watched.
3. **"Hardware and firmware (from brief)"** — Risk (a) now carries the C3 fallback decision that previously sat under unverified research notes; confirm it is a decision, not a research suggestion.

## Summary

16 recommendations, all applied. Word count essentially unchanged (~2,100 → ~2,150; a few cross-references and one pointer line added, no content cut). No length target. No comprehension trade-offs.
