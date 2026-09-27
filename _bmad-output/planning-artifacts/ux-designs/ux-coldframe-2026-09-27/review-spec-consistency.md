# Spec-consistency review — Coldframe UX spines (2026-09-27)

Reviewed: `DESIGN.md`, `EXPERIENCE.md`, `.memlog.md`.
Contract: `specs/spec-coldframe/` (SPEC, acceptance-criteria, glossary, user-journeys, device-hardware) and `ARCHITECTURE-SPINE.md`.

**Verdict: PASS WITH CHANGES.** The spines follow the contract well. They keep server-computed LotStatus with the 6 statuses, precedence and sort order. They show stale data honestly, keep "Silence Window" and "outside your window" apart, use stored-Reading Calibration in EXPERIENCE, show no "already set up" Hub, break no non-goals and add no Alert actions. One critical gap and seven high findings must be fixed before epics are cut. Several of them need a spec or AD decision, not just a UX edit.

Counts: **Critical 1 · High 7 · Medium 13 · Low 16**

Fix legend: **UX** = change the spine; **SPEC/AD** = the contract needs a decision or addition; **Both** = both need to change.

---

## Critical

### C-1 Members are never asked for notification permission, so UJ-5 fails
- **UX location:** EXPERIENCE › Notifications ("Permission: mobile asks after the first Hub is set up"); memlog decision "notification permission asked after first Hub setup".
- **Contract:** CAP-15 (delivered to every User with a Membership), UJ-5 step 3 (the neighbour receives the same Alerts), CAP-19 (receiving notifications).
- **Problem:** A Member or an invited Administrator never sets up a Hub. On iOS, and on Android 13+, no push can be shown without the runtime permission. The UJ-5 climax ("the neighbour's phone shows 'Cucumbers needs water'") cannot happen. A User on a second Site that already has Hubs would never be asked either.
- **Fix (UX):** Ask when the User first holds a Membership on any Site and has seen the overview, whichever comes first: after the first Hub setup or after the first Membership arrives. Add a persistent row in My notifications, "Notifications are off for Coldframe → Open Settings", for users who declined.

## High

### H-1 "Report now" short press of the Node setup button is invented firmware behaviour, and UJ-1 depends on it
- **UX location:** EXPERIENCE › Component Patterns › Calibration reference point ("short press… makes it report immediately `[ASSUMPTION]`"); Key Flows UJ-1 step 7 ("he presses the Node's button to report now"); memlog decision on Calibration.
- **Contract:** device-hardware › Node requirements (the button enters BLE setup mode with a timeout, and advertising is limited because of the energy budget); CAP-2 (Node visible over BLE only after the button press); AD-25 (the Node advertises only in button-triggered setup mode); NFR-4.
- **Problem:** The spec gives the button exactly one meaning: enter setup mode. A second gesture that forces an ESP-NOW report is new firmware and protocol behaviour. It also needs a gesture design (short press vs setup press), because the same press may start BLE advertising. The primary UJ-1 flow is written as if the feature exists. The `[ASSUMPTION]` tag hides a spec change.
- **Fix (Both):** Either add it to device-hardware.md as a Node requirement (for example, "N-3: short press = immediate wake-and-report; long press = setup mode") and have the Node epic accept it, or remove it from UJ-1 and make "wait for the next Reading, or pick a recent stored Reading" the only path, as AD-9 describes.

### H-2 DESIGN still specifies B's live-BLE calibration component
- **UX location:** DESIGN › front matter `components.calibration-live-value` (`steady: support-success`); DESIGN › Components › "Calibration live value — 'LIVE RAW VALUE'… 'steady for 5 s, ready to record'"; DESIGN › Typography (`hero-value` "on Lot detail and live calibration"); DESIGN › Colors › Green ("calibration 'steady'").
- **Contract:** AD-9 (reference points are raw values of **stored Readings**, submitted over REST, never over BLE); EXPERIENCE's own Calibration reference point (the spines win, but the two spines disagree with each other).
- **Problem:** A live raw value that updates every few seconds and a "steady for 5 s" readiness check only work over a live BLE stream. The Node reports every 15 min over ESP-NOW. Designers or implementers reading DESIGN will build the dropped flow.
- **Fix (UX):** Replace it with a "stored Reading" panel: the last raw value with its Reading time, the "Waiting for the next Reading · usually within 15 min" state, and a recent-Readings picker. Remove "steady" from the green usage and "live calibration" from the typography notes. Treat B-plots-4/5 as superseded for this screen.

### H-3 The value shown right after Calibration contradicts AD-9
- **UX location:** EXPERIENCE › Key Flows UJ-1 step 8 ("Tomatoes now reads about 40 %… tile is neutral with a soil fill at ~40"); Calibration reference point ("Confirmation shows… the resulting ~%").
- **Contract:** AD-9 (normalized values come from the Calibration each Reading was **stored with**); CAP-9 ("Recalibrating applies to new Readings"; history keeps the Calibration it was recorded with).
- **Problem:** Every Reading stored before Calibration has no Calibration in force. The latest Reading stays raw until the next wake (up to 15 min). Either the tile shows a % the Server does not have, which means client-side computation (against AD-14), or it shows `raw` next to an `ok` status. The confirmation "~%" can be shown as a preview built from the reference points. The tile and hero cannot show it.
- **Fix (UX):** Word the confirmation as a preview ("Readings from now on will show in %; the latest raw value maps to about 40 %"). Add a Lot state "Calibrated · first % Reading due by 07:15" for the gap. Tile value: `raw` with foot "% from next Reading". Move the UJ-1 climax to the next Reading. **Alternatively (SPEC/AD):** let the Server re-derive the latest Reading under the new Calibration for display. That changes AD-9's "History keeps the Calibration it was recorded with".

### H-4 Adding a Device to a paused Site is not designed, and calibrating in that state is impossible
- **UX location:** EXPERIENCE › Key Flows UJ-1 steps 5–7, Add a Node flow, Node-added screen, Calibrate; State Patterns (no state for this case).
- **Contract:** CAP-18 ("Devices added to a paused Site start paused"); AD-8 (a Device joining a paused Site receives the Pause in the join reply; Readings from a paused Device are acknowledged and **discarded**).
- **Problem:** On a paused Site, a newly added Node goes straight to "Tomatoes has a Node… CALIBRATE". No Readings are ingested while it is paused, so the Calibrate flow waits forever ("Waiting for the next Reading"). The acceptance criterion has no UI surface at all.
- **Fix (UX):** On the Node-added and Hub-online screens, when the Site is paused, show "Home garden is paused, so 7C19 starts paused. It won't report or alert until you resume the Site." Offer Resume Site (Admin+) and "Calibrate later". Calibrate must refuse to start on a paused Device and give the reason.

### H-5 The invitation UI needs data and surfaces the architecture does not provide
- **UX location:** EXPERIENCE › IA "Accept invitation | web `[ASSUMPTION]`"; Component Patterns › Member tile ("Invited = pending until accepted"); UJ-5 step 1 ("MEMBER · INVITED" tile) and Failure copy ("Accepting an invitation needs the home network…").
- **Contract:** AD-3 (invitations use **Phase Two's native invitation flow**; the Server holds no invitation state; an accepted invitation arrives as a Membership event; the identity projection holds Memberships); architecture Invariants (clients never talk to the Keycloak Admin API); NFR-1 (away from home neither Keycloak nor the web app is reachable).
- **Problems:**
  1. Showing pending invitations requires the Server to project Phase Two invitations. That projection is not defined, and clients may not fetch them from Keycloak themselves.
  2. Acceptance happens on Phase Two/Keycloak pages, not on a Coldframe web surface. Away from home, the email link simply fails to load. No Coldframe code runs that could show the tailored failure copy.
  3. Phase Two invitations expire. The UX has no expired, resend or revoke state.
  4. After acceptance, the Membership arrives asynchronously (Keycloak → Temporal → Site grain). The "app opens on Home's overview" step can hit a 403/404 or land on Create Site.
- **Fix (Both):** SPEC/AD: decide whether the Site grain records "invitation sent" (the invitee's email, the Role and the Phase Two expiry) in a read model so Owners see pending invites, with revoke and resend through the Site grain. If not, remove the invited tile. UX: redefine "Accept invitation" as a themed Keycloak/Phase Two page, not a Coldframe surface. Move the away-from-home copy into the invitation email text ("open this link on the garden's Wi-Fi"). Add a "Joining Home garden…" state that polls until the Membership appears. Add a "Waiting for an invitation?" hint on Create Site for first sign-ins.

### H-6 Unknown cause and OK secondary conditions need read-model fields that do not exist
- **UX location:** EXPERIENCE › State Patterns › Lot status: unknown label "SILENT · UNKNOWN" vs "HUB SILENT · UNKNOWN", ok "plus one open Health condition in label". DESIGN › Components › Lot tile ("OK · BATTERY 14 %").
- **Contract:** AD-14 (`LotStatus` with `statusSince` and `lastReadingAt` only; clients only render and must not combine Alerts into status); CAP-8 ("exactly one status").
- **Problem:** The client cannot tell a Node-silent unknown from a Hub-silent unknown, or pick "one open Health condition", without joining open Alerts to Lots and choosing between them. That is status logic in the client. Every client would implement it separately.
- **Fix (SPEC/AD):** Extend the AD-14 LotStatus read model with a server-computed `unknownCause ∈ {nodeSilent, hubSilent}` and an optional `secondaryCondition` (kind and value, chosen by server precedence). Otherwise (UX), drop both refinements and show the Health Alert only on Lot detail and in Alerts.

### H-7 Threshold Alerts that are not "needs water" are painted orange
- **UX location:** DESIGN › components `alert-row-threshold` (orange fill) and Components › Alert rows ("Threshold Alert: solid orange"); DESIGN › Colors (orange "fills… the Threshold Alert row"); EXPERIENCE › Notifications ("Herbs too wet", "temperature below 5 °C").
- **Contract:** CAP-11 (a Threshold Alert names the side crossed and exists for every alerting Sensor, including high-side soil, temperature, humidity and gas); AD-14 (`needsWater` = an open **low-side soil-moisture** Threshold Alert only); DESIGN's own rule that orange means only *needs water*.
- **Problem:** "Herbs too wet" or "Tomatoes temperature below 5 °C" would be drawn in the same act-now orange as *needs water*. The Lot's tile meanwhile stays neutral `ok`, because LotStatus has no status for these Alerts. The Alerts list and the overview then disagree, and orange loses its single meaning.
- **Fix (UX):** Use orange only for low-side soil-moisture Threshold Alerts. Give other Threshold Alerts a distinct non-orange treatment (for example a solid neutral border with an "above high" / "below low" eyebrow). Also consider (SPEC/AD) whether an `ok` Lot with an open non-water Threshold Alert should show a server-provided secondary condition (see H-6).

## Medium

### M-1 Closed-Alert history for 7 days is not in the architecture
- **UX:** EXPERIENCE › IA Alerts ("recently closed"), Alert row ("closed Alerts (last 7 days)"), UJ-2 climax ("the Alert sits under 'Closed'"); memlog "closed Alerts visible 7 days".
- **Contract:** AD-7 (the Site grain keeps the set of **open** Alerts); AD-14 (only LotStatus is named as a read model); the spec has no Alert history capability.
- **Fix (SPEC/AD):** Add an Alerts read model with a closed-Alert query and a close reason (`recovered`, `paused`, `unassigned`, `calibrated`, `removed`) to AD-14/openapi. The 7-day window itself is a UX default. Show the close reason in the row ("closed · paused").

### M-2 The time zone is confirmed during Create Site, which reads as a Site property
- **UX:** EXPERIENCE › IA Create Site ("Name the Site, confirm time zone"); UJ-1 step 2; Time-zone confirm panel "Create Site, My notifications".
- **Contract:** Glossary (a **User** has one IANA time zone; Sites have none); AD-11.
- **Fix (UX):** Confirm "your time zone" on first sign-in, or in My notifications, as a User setting that applies to every Site. Do not put it inside Create Site. Also add the IP-geolocation last-resort proposal (AD-11) to the confirm panel's detection order.

### M-3 The Pause end-date time zone and the auto-resume moment are undefined
- **UX:** Pause sheet (optional "Until" **date**); tiles "until 1 Nov"; headline "Paused until 1 Mar".
- **Contract:** CAP-18 (an optional end date resumes automatically); AD-6/AD-11 (the deadline is stored as a UTC `due-at`). The spec does not say which zone or time of day a date resolves to.
- **Fix (SPEC/AD):** Define it, for example "resumes at 00:00 on the end date in the pausing User's time zone, stored as UTC". UX: add the helper "Resumes automatically on 1 Mar at 00:00". Neither spine says in the UI that the Pause resumes by itself.

### M-4 Pause sources are hidden, so Resume on a Device whose Site is paused misleads
- **UX:** Pause sheet ("Resume is one tap from the paused tile's detail or Devices row"); Lot status paused hero with Resume.
- **Contract:** AD-8 (`pausedBy ⊆ {device, site}`; a Device resumes only when **both** sources clear); CAP-18.
- **Fix (UX):** Show the source(s) ("Paused with the Site", "Paused on its own until 1 Nov", or both). Where the Site Pause is the blocker, the Device's Resume says "Resume the Site to resume this Node". Requires the read model to expose `pausedBy` (a small AD-14 addition).

### M-5 Morning summary per Site vs "one summary"
- **UX:** EXPERIENCE › Notifications ("Morning summary | Home garden: 2 need water, 3 to check"; "Grouped per Site").
- **Contract:** CAP-16 / glossary Notification Window ("delivered in bulk as **one** summary when it opens"); AD-7 (the User grain owns the summary; per-Site scope is not specified).
- **Fix (SPEC/AD or UX):** Decide it. Either one summary per User covering every Site (UX title "2 need water, 3 to check" with Site-prefixed lines), or amend CAP-16/AD-7 to "one summary per Site". In both cases, state that an Alert that opened and closed while held is dropped, as AD-7 says. The UX never mentions this acceptance criterion.

### M-6 There is no way to remove Thresholds (turn alerts off) or return to the default
- **UX:** Threshold column (low required, high optional, "Add high"/clear only).
- **Contract:** CAP-10 ("sets, changes, or **removes**"; "turns alerts on for watched Sensors", which implies turning them off); AD-19 (`Default | Override | Cleared` per side).
- **Fix (UX):** Add "Stop alerting on this Sensor (keep watching)", which clears low and high, and "Reset to Specification default" where one exists. Clarify whether soil moisture (which alerts by default) may be set to watched-only. If not, that is a SPEC decision.

### M-7 The "±5 %" accuracy claim is not supported by the spec
- **UX:** EXPERIENCE › Lot detail hero ("±5 % · <Reading time>"), Calibration confirmation ("good to roughly ±5 %"); DESIGN › Lot detail hero.
- **Contract:** SPEC Constraints › Approximate soil moisture (no temperature compensation; temperature and salinity shift Readings); device-hardware (non-linear cheap probes). No accuracy figure is specified.
- **Fix (UX):** Replace it with "approximate" and keep the `~` prefix and 5 % rounding. Only if the Node epic measures an accuracy figure (SPEC) should one be shown.

### M-8 Mobile live updates over SignalR are not covered by AD-14
- **UX:** EXPERIENCE › Interaction Primitives ("Live updates arrive through SignalR invalidation hints") and Lot tile ("Updates on `readmodel.changed`"), applied to every platform.
- **Contract:** AD-14 (SignalR is described through the BFF for the browser; the KMP core is listed as REST only; the diagram shows `CORE -->|REST|`).
- **Fix (SPEC/AD):** Either state in AD-14 that the KMP core also subscribes to SignalR directly with the user token, or (UX) define mobile refresh as refetch on foreground, pull-to-refresh and polling.

### M-9 Where partial Calibration points are kept is not defined
- **UX:** Calibration reference point ("The flow can be left and resumed; the chosen points are kept until both exist").
- **Contract:** AD-9 (`Sensor.Calibrate` receives reference points over REST; there is no draft state).
- **Fix (UX, or SPEC/AD):** Say that the chosen dry point is kept locally on that client (lost when the user switches device). Otherwise, add a Sensor-grain "calibration in progress" draft to AD-9.

### M-10 History chart vs Node moves and pre-Calibration Readings
- **UX:** History chart (daily low %, "Days without Readings are gaps"); UJ-6 ("history stays with the Node").
- **Contract:** CAP-2 / AD-18 (history follows the Node); AD-9 (Readings stored without a Calibration have no %).
- **Fix (UX):** Say whether Lot detail charts the **Lot's** history (Readings while each Node sat there) or the current **Node's** history, including time on another Lot. Say how raw-only (pre-Calibration) days are shown: as gaps with a "not calibrated yet" band, never as 0 %. If Lot-scoped history is wanted, AD-14/AD-18 need a Lot-keyed history query.

### M-11 Node advertisement content and the Hub Wi-Fi scan list extend the BLE protocol
- **UX:** Device candidate tile (Node "battery %, Sensor count" before the session); Wi-Fi network row ("Networks as seen by the Hub"; "Other network" for hidden SSIDs `[ASSUMPTION]`).
- **Contract:** AD-25 (the message set covers identity, Wi-Fi config and result, Site/Lot binding and enrolment; everything after advertising is encrypted and PoP-bound; advertising payload is unspecified); device-hardware H-1 (join the strongest BSSID after an all-channel scan).
- **Fix (SPEC/AD):** Add a Wi-Fi scan request/result message, with security type so WPA3-only rows can be flagged, and the advertising payload fields to `packages/proto`/AD-25. Otherwise (UX), show Node battery and Sensor count only after the PoP session opens. Confirm that a hidden SSID works with H-1 scanning.

### M-12 The Settings IA mixes per-User and per-Site scopes without labelling them
- **UX:** My notifications = Notification Window, time zone (per **User**) + mute, my Reminder cadence (per **Site**).
- **Contract:** Glossary (a User has **one** Notification Window and time zone); CAP-12 / CAP-17 (per User per Site).
- **Fix (UX):** Split the surface into "All Sites" (window, zone, browser notifications) and "Home garden" (mute, my Reminder cadence). Otherwise, multi-Site Users will think each Site has its own window.

### M-13 Reminder cadence values are not in the contract
- **UX:** Reminder cadence ("Use Site setting / Daily / Every 2 days").
- **Contract:** CAP-12 / AD-7 (cadence resolves User → Site → daily; Health Alerts use `max(resolved, 24 h)`); no value set.
- **Fix (SPEC/AD):** Record the allowed values in openapi/AD-7 (UX default is acceptable). "Use Site setting" correctly models the absent User value. Leaving out "Never" is correct.

## Low

- **L-1 Headline cases are incomplete or inaccurate.** In EXPERIENCE › Voice rules, a mix of ok with paused Devices or no-Node Lots has no case. "can't be read" is wrong for needsCalibration: the Node reports raw values. Fix (UX): "2 Lots need calibration" / "1 Lot silent", and define the mixed cases.
- **L-2 "Paused by Simon (Owner) on 2 Nov"** (EXPERIENCE › All paused) needs the actor and time in a read model. AD-8 stores only state and end date. Fix: add it to the Site Pause read model (AD) or drop the line (UX).
- **L-3 Hub "LED blinks orange while it waits"** (BLE errors, "No Device found") is invented hardware behaviour. device-hardware has no LED. Fix: SPEC (Hub epic) or generic copy.
- **L-4 Device cell "charging · solar"** (DESIGN › Sensor cell / device cell). The spec only has *charging* / *not charging*. Fix (UX): drop "solar".
- **L-5 Glossary wording.** The Uncalibrated push "Soil sensor on Lot 'Peppers'…" should read "Soil-moisture Sensor on Lot 'Peppers' needs calibration". "bed" appears in prose (DESIGN Colors "a dry bed"; EXPERIENCE UJ-2 climax). Keep "bed" out of UI strings (glossary: Lot, not Bed).
- **L-6 "Garden" tab label** for the Site overview. The glossary term is Site, and the rest of the UI says "Site" ("Whole Site", "Site settings"). Acceptable as a nav label, but make the header read the Site name and keep "Site" in copy. Consider "Overview".
- **L-7 "from 07:00" interpreted as 07:00–22:00.** CAP-16 says only "simplest form is 'from 07:00'". Confirm in the SPEC that the default end is kept, and not open-ended to 24:00.
- **L-8 Hub provisioning timeout of 90 s vs CAP-1's "within one minute".** That is fine as an error margin. Add an "Taking longer than usual…" state at 60 s.
- **L-9 Recalibration messaging is missing.** CAP-9: a recalibration changes only new Readings, and Threshold % values stay unchanged. Add this to the Calibrate confirmation when a Calibration already exists.
- **L-10 Calibration gating.** State explicitly that Calibrate appears only for `calibration: true` Sensors (CAP-3), not only through the needs-calibration entry points.
- **L-11 The Pause close effect is not shown.** CAP-18: open Alerts close on Pause. Show those Alerts moving to Closed with reason "paused" (ties to M-1).
- **L-12 Time-zone detection order** is missing the IP fallback (AD-11). Covered in M-2.
- **L-13 Web side nav shows "Members" to Members.** The spec does not say whether Members may see the Membership list. Decide (UX): read-only list, or hide it.
- **L-14 Air (gas) shown in kΩ** while the contract unit is Ω (Consistency Conventions). Fine for display. Make sure Threshold entry shows the same unit it stores, or converts clearly.
- **L-15 UJ-6 exists only in the UX.** Add it to `user-journeys.md`, or mark the Devices, Site settings, Site switcher and Appearance surfaces as traced to CAP-2/6/12/13/20 directly (the closure note already flags this).
- **L-16 The browser-notification opt-in toggle** is per browser. State that it is stored in that browser (not in User settings on the Server), and that Server-side window and mute still apply (CAP-20).

---

## `[ASSUMPTION]` tags that actually need a spec/AD change

| Tag location | Why it is not a pure UX default | Owner |
|---|---|---|
| Calibration: Node setup-button short press reports immediately | New firmware gesture and radio behaviour; conflicts with the button's single "setup mode" meaning (H-1) | device-hardware.md / Node epic |
| Accept invitation as a web surface; away-from-home copy | Acceptance is Phase Two native (AD-3); no Coldframe surface can render there (H-5) | AD-3 |
| Wi-Fi network row "Other network" for hidden SSIDs, plus the implicit Hub scan list | Needs BLE proto messages and firmware scan behaviour (M-11) | AD-25 / Hub epic |
| Hub-silent Site notice / "HUB SILENT · UNKNOWN" (untagged but assumes data) | Needs `unknownCause` in LotStatus (H-6) | AD-14 |
| Closed Alerts 7 days (memlog "accepted", untagged in spine) | Needs an Alerts read model with closed-Alert query (M-1) | AD-7 / AD-14 |
| Morning summary grouped per Site (untagged) | Conflicts with "one summary" (M-5) | CAP-16 / AD-7 |
| Pause "Until" date resolution (untagged) | Deadline semantics are Server-side (M-3) | CAP-18 / AD-6 / AD-11 |

Tags that are legitimate UX defaults: 5 % Threshold steps, chart tap detail, Sensor picker for history, Carbon breakpoints, self-hosted fonts, keep screen awake, 1-column grid at large text, stale after one retry, cold-start cache, i18n tooling, Reminder "still" wording, swipe to reveal Pause, notification copy variants, one Site-settings surface, air (gas) formatting, "2 Lots can't be read" (reword per L-1), web notification opt-in, 90 s timeout, tile contrast verification.

## Checked and consistent (no action)

Server-computed LotStatus with 6 statuses, precedence and sort order; the client never re-sorts. Server-unreachable indicator with data age, and stale tiles "WAS …". Lots needing water listed first. Member 403 copy and hidden admin controls. At least one Owner (last Owner locked, validation copy). Low required / high optional and empty. 20 % fallback proposal. Mute per Site affecting only this User. Summary has one entry per open Alert. Health Reminders never more often than daily. Web: browser notifications only while open, no background push. Glossary "Silence Window" vs "outside your window". No "already set up" Hub (AD-25). Node only after its button press, in range, strongest first. PoP code validated by the session. WPA3-only rows blocked (H-3). A Lot with a Node rejects a second one, and a Lot with a Node refuses removal. Real domain on sign-in (AD-13). No commands, mark watered, snooze, weather or irrigation controls. Away from home handled as stale, not as remote access. Private branding package vendored (NFR-7).
