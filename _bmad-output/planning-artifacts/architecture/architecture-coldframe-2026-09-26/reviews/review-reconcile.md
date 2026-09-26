---
review: reconcile
target: ARCHITECTURE-SPINE.md (Coldframe V1, draft 2026-09-26)
inputs:
  - prds/prd-coldframe-2026-09-25/prd.md
  - prds/prd-coldframe-2026-09-25/addendum.md
  - architecture-coldframe-2026-09-26/.memlog.md
date: 2026-09-26
---

# Reconcile Review: Spine vs PRD, Addendum, and Decision Log

## Verdict

The spine faithfully carries the major memlog decisions: Orleans lanes, ack after the PostgreSQL commit, the TLS strategy, the Device key model, KMP, and the BFF. But the AD structure dropped several quiet PRD rules where independently built units will diverge. The biggest gaps sit in Device Health, the product's "silence must never be mistaken for all fine" promise:

- A silent Hub must be reported as the Hub, not as every Node behind it.
- Health Alert Reminders are capped at once per day.
- Lot staleness and "unknown" status need one definition.
- Server downtime must not produce false Silence Alerts.

Beyond Device Health:

- One internal contradiction (replay rejection vs duplicate-acked resends) will cause a Node to resend forever.
- AD-3 misstates the memlog. It makes the Server the only writer of *Users*, which contradicts FR-5 and FR-7.

Severity counts: 2 critical, 7 high, 11 medium, 6 low.

---

## Critical

### C1. Replay rejection vs duplicate acknowledgement: a resent frame is never acknowledged
- **What:** AD-12 says "The Server rejects a frame that … replays an old counter." AD-9 says a duplicate Reading "is a no-op that is still acknowledged," and FR-4 says the Node "resends it once the Server is reachable again." Suppose the Node resends the *same sealed frame* after the ack was lost on the way back (Server → Hub → ESP-NOW). The Server then rejects it as a replay, sends no ack, and the Node retries until its buffer overflows. That is silent data loss, and the Node also looks healthy to the Hub. Firmware and Server teams will each pick a different reading of this.
- **Source:** PRD FR-4 (resend until acknowledged), NFR-3; spine AD-9 vs AD-12.
- **Fix (AD-12, Sealing bullet):** "Resent Readings are always re-sealed in a new frame with a fresh counter value; a frame is never retransmitted verbatim. The counter is persisted in flash before each send, so it survives deep sleep and power loss. A frame that fails authentication or carries a stale counter is not acknowledged, and it does not count as a report for the Silence Window (FR-13). Recovering a Device whose counter is lost requires re-enrolment." Also add to AD-9: "The dedupe key, not the counter, decides whether a Reading is new."

### C2. A silent Hub is reported as the Hub, not as every Node behind it: dropped
- **What:** FR-13 says "a silent Hub is reported as the Hub, not as every Node behind it." AD-7 has each Device grain evaluate its own Silence Window independently. If a Hub is down for more than 6 h, every Node on the Site also opens a Silent Device Alert, which is the exact notification storm the PRD forbids (also SM-C1). Nothing assigns this cross-Device suppression to any grain, so it will be implemented inconsistently or not at all.
- **Source:** PRD FR-13, bullet 1; SM-C1.
- **Fix (AD-7, Device grain bullet):** "While the Site's Hub has an open Silent Device Alert, Node Silence Windows on that Site do not open Silent Device Alerts. When the Hub recovers, each Node's Silence Window restarts from the Hub's recovery time. The Device grain of each Node learns Hub silence from the Site grain (or a Site-scoped stream), never by polling other Device grains."

---

## High

### H1. AD-3 makes the Server the only writer of *Users*, which contradicts FR-5 and the memlog
- **What:** The spine says "Users, Sites …, Memberships, and Roles live in Keycloak. The **Server is the only writer** to them." But FR-5 says "The identity provider owns accounts, passwords, and registration," and the memlog assumption (line 36) scopes the rule to "Sites/Memberships/Roles." The spine silently widened it to Users. Self-registration and account edits happen in Keycloak, not through the Server.
- **Source:** PRD FR-5; memlog lines 36 and 67.
- **Fix:** "Keycloak owns Users (accounts, credentials, registration). The Server is the only writer of Sites (Organizations), Memberships, and Roles, through the Phase Two / Admin API."

### H2. Invitation acceptance and expiry: who writes the Membership?
- **What:** Under FR-7 the invitee "gets the Membership on acceptance" after signing in with the IdP. With Phase Two invitations, Keycloak creates the Membership on acceptance, which breaks the "Server is the only writer" rule. AD-6 also has the Server persist "invitation expiry," while Phase Two invitations carry their own expiry. So there are two expiry clocks and two Membership writers.
- **Source:** PRD FR-7, bullet 1; addendum "Invitations by email"; memlog line 38; spine AD-3 and AD-6.
- **Fix (AD-3):** Pick one and state it. Either: "Invitations are Server-owned: the Server grain stores the invitation and its expiry, sends the email link to a Server endpoint, and on acceptance writes the Membership through the Admin API. Phase Two's invitation feature is not used." Or: "Invitations are Phase Two-owned (creation, email, expiry, acceptance). Acceptance is the one Membership write Keycloak performs, and it arrives through the event path. Invitation expiry is removed from AD-6."

### H3. Health Alert Reminders are capped at once per day: dropped
- **What:** PRD §4.5 says "Every Health Alert (FR-13, FR-14, FR-21) sends Reminders at most once per day while it stays open." FR-12's cadence resolution applies to Threshold Alerts ("While a Threshold Alert stays open"). AD-7 applies "Reminder cadence (User → Site → default)" to all Alerts, so a User with a 2-hour cadence would get Health Reminders every 2 hours.
- **Source:** PRD §4.5 description; FR-12; SM-C1.
- **Fix (AD-7, User grain bullet):** "Reminder cadence (User → Site → default of once per day) applies to Threshold Alerts. Health Alerts remind at the resolved cadence, but never more than once per 24 h."

### H4. "Three consecutive Readings" has no defined ordering; late buffered Readings will diverge
- **What:** Buffered Readings (up to 24 h) arrive late and in batches after an outage, possibly interleaved with fresh ones. AD-7 does not say whether "consecutive" means in `measured_at` order or arrival order. It also does not say whether a late Reading counts as the Device "reporting again" for FR-13. Two implementations will open and close Alerts differently, and that changes the timing of SM-2 notifications.
- **Source:** PRD FR-4 (buffer and resend), FR-11, FR-13, FR-14.
- **Fix (AD-7):** "Consecutive means consecutive by `measured_at` per Sensor (per Device for battery). A Reading older than the latest one evaluated is stored but does not re-run the debounce. Any authenticated frame received counts as a report for the Silence Window, whatever its `measured_at`. An Alert's opened-at/closed-at is the `measured_at` of the third Reading."

### H5. Lot status and staleness need one definition ("a stale Reading is never shown as the current state")
- **What:** FR-8 defines the Lot statuses *needs water / OK / unknown (with time since last Reading) / paused*, says the Site overview lists needs-water first, and says "a stale Reading is never shown as the current state." The spine only puts "staleness … logic" in the KMP core (AD-14). The web app would need its own copy, and nothing defines what "stale" means. This is the PRD's central honesty tone, and it is left to each client.
- **Source:** PRD §1 ("silence must never be mistaken for 'all fine'"), FR-8 bullets 1, 2, and 4.
- **Fix (new rule in AD-7 or AD-14):** "Lot status is computed on the Server into the Lot read model with one rule: *paused* if the Device is paused; else *unknown* if the Node has an open Silent Device Alert, is unassigned, or has no Reading; else *needs water* if a low soil-moisture Threshold Alert is open; else *OK*. Clients render it and never derive status from raw Readings. Clients (KMP core **and** web) separately show 'Server unreachable' with the age of the displayed data."

### H6. Authorization reads a projection that lags the Server's own writes
- **What:** AD-4 reads Roles from the identity projection, and AD-3 updates that projection asynchronously through Keycloak → Temporal → grain. Right after "Create Site" (FR-6), the creator's Owner Role is not yet projected, so their next call returns 403. The FR-7 "at least one Owner" check against a stale projection also lets two concurrent demotions both pass. This is a distill-introduced assumption (memlog line 69) that was never triaged.
- **Source:** PRD FR-6, FR-7; memlog line 69.
- **Fix (AD-3/AD-4):** "When the Server writes to Keycloak, the owning Site grain also applies the change to its own state immediately (read-your-writes), and the Keycloak event later confirms it idempotently. Membership changes for one Site are serialized through that Site grain, so the at-least-one-Owner check and the write cannot interleave." Mark AD-4 as `[ASSUMPTION — confirm]` until triaged.

### H7. Server downtime is mistaken for Device silence (false Silence Alerts)
- **What:** AD-6 processes "all overdue deadlines" on every wake and activation. After a Server outage longer than 5 min (Hub) or 6 h (Node), every Device's Silence deadline is overdue, and Silent Device Alerts open, and are pushed, even though the Devices were fine and are about to deliver their buffers. This is the inverse failure of "silence mistaken for all fine" and inflates SM-C1.
- **Source:** PRD FR-13, NFR-3, SM-3, SM-C1; spine AD-6.
- **Fix (AD-6):** "Silence Window deadlines are measured from the later of the Device's last report and the silo's start time. Server downtime never counts as Device silence."

---

## Medium

### M1. Defaults that two units must agree on are missing
- **What:** These are absent from the spine: the Notification Window default **07:00–22:00** (FR-16); the Reminder default **once per day** (named in AD-7 only as "default"); the Node Silence Window default **6 h** (only the Hub's 5 min appears, in AD-6); and the rule that the Silence Window is **per-Device configurable by an Administrator** (FR-13). Clients, User grain, and Device grain can each hard-code different values.
- **Source:** PRD FR-12, FR-13, FR-16.
- **Fix:** Add a "Domain defaults" row to Consistency Conventions: "Notification Window 07:00–22:00 in the User's zone; Reminder cadence 24 h; Silence Window Node 6 h / Hub 5 min, overridable per Device by Administrator+; battery Alert threshold 20 % not charging; debounce 3 consecutive; reporting interval 15 min. Defined once in `packages/cs` and exposed through OpenAPI; clients never hard-code them."

### M2. Hub heartbeat is not an invariant
- **What:** FR-13 requires a Hub heartbeat every 30–60 s, independent of Node traffic, and FR-1's "appears on the Site within one minute" depends on it. The spine mentions a heartbeat only in passing (AD-16 "next heartbeat"). Memlog line 50 records "Hub heartbeat = periodic POST" as an accepted assumption, but it never landed as a rule, and the interval and endpoint shape matter to both the Hub firmware and the Server.
- **Source:** PRD FR-1, FR-13; memlog lines 50 and 67.
- **Fix (AD-9):** "The Hub POSTs a heartbeat every 30–60 s even with no Node frames. Its response carries Server time and the reserved `commands` field. Ingestion POSTs also count as Hub reports. A Hub appears on its Site at its first successful heartbeat after provisioning."

### M3. Push payloads must be self-contained away from home
- **What:** FR-15 says notifications reach the phone away from home, but opening the app for details requires the home network. So the push text itself must carry the decision ("Tomatoes needs water", "Node on Lot 'Beans' silent for 6 h"). The spine is silent. A Notifier adapter could send a content-free "open app" ping, which is useless off-LAN.
- **Source:** PRD FR-15, UJ-2, UJ-3, FR-21 (names Lot and Sensor).
- **Fix (AD-7, Notifier bullet):** "Every notification payload is self-contained: Site, Lot (or Hub), Alert kind, side crossed or health detail (battery %/charging, time silent, Sensor to calibrate). Summaries list one line per open Alert. The payload is rendered once in the User grain, and adapters only transport it."

### M4. Uncalibrated Sensors must suppress Threshold Alerts
- **What:** Under FR-9, until calibrated, a `calibration: true` Sensor "opens no Threshold Alerts." AD-7 has the Sensor grain evaluate "the uncalibrated condition (FR-21)" but never says it gates Threshold evaluation. It also does not say whether Thresholds are compared in normalized % against the Calibration in force (FR-9 bullet 1).
- **Source:** PRD FR-9, FR-21.
- **Fix (AD-7, Sensor grain bullet):** "A `calibration: true` Sensor without a Calibration opens no Threshold Alert. Once calibrated, it compares the normalized value (under the Calibration in force for that Reading) with its % Thresholds. The FR-21 Alert opens only while the Node is assigned to a Lot."

### M5. Threshold edits while an Alert is open
- **What:** The spine does not say what happens to an open Threshold Alert, or to a debounce run in progress, when an Owner or Administrator changes or removes Thresholds (FR-10), or when recalibration happens. Without a rule, one implementation keeps the Alert open until three Readings pass, and another closes it at once.
- **Source:** PRD FR-10, FR-11, FR-9.
- **Fix (AD-7):** "Changing Thresholds or Calibration resets the debounce counter. An open Alert stays open until three consecutive Readings satisfy the new Thresholds. Removing all Thresholds (the Sensor becomes watched-only) closes the Sensor's open Threshold Alerts immediately."

### M6. Threshold validation and the proposed-default formula have no single owner
- **What:** FR-10 has several rules: low is required on an alerting Sensor; low < high, rejected otherwise; the proposed low is Min + 20 % × (Max − Min); there is no high fallback; a Specification-default high can be cleared. Clients (proposal UI) and the Sensor grain (validation) could compute these differently.
- **Source:** PRD FR-10.
- **Fix (AD-7 or a convention):** "The Sensor grain is the only validator of Thresholds, and a rejection is a 400 Problem Details. The proposed low Threshold is computed on the Server and returned by the API. Clients never compute it."

### M7. Lot "at most one Node" needs a single enforcer
- **What:** FR-2 says a Lot takes at most one Node, and FR-6 says a Lot can be removed only after its Node is moved or unassigned. Assignment involves both the Device grain and the Lot grain (see the capability map), and AD-1 names no single enforcer, so concurrent assignments to the same Lot can race.
- **Source:** PRD FR-2 bullet 4, FR-6.
- **Fix (AD-1 or AD-8):** "The Lot grain owns the Lot ↔ Node slot. Assignment is first a reservation on the Lot grain, then a Device grain event. Lot removal is refused while the slot is occupied."

### M8. Create Site atomicity and the memlog's "Create Site steps" were lost
- **What:** The addendum requires "create the Organization and grant the creator Owner atomically." Memlog line 38 puts "Create Site steps" in the Orleans Reminder lane. The spine's AD-6 deadline list omits it, and AD-3 says nothing about what happens if the Organization is created but the Owner grant fails.
- **Source:** addendum "Create Site (FR-6)"; memlog line 38.
- **Fix (AD-3):** "Create Site is a Site-grain process with persisted steps (create Organization → grant creator Owner → mark ready), resumed on wake per AD-6. A Site is not visible until it is ready, and a half-created Organization is completed or deleted, never left without an Owner." Add "Create Site steps" to the AD-6 deadline list.

### M9. Break-glass handling contradicts "every Site keeps at least one Owner"
- **What:** The memlog says admin-console edits are "reconciled via events." The spine instead raises "an operator-visible error, not silently repaired," which allows a Site with zero Owners to persist. That breaks FR-7, and the addendum explicitly says the rule "must be enforced even when members are changed through Keycloak's own admin UI." The spine also never defines where "operator-visible" shows up, and PRD §6 excludes Server monitoring.
- **Source:** PRD FR-7 bullet 3; addendum identity checklist; memlog line 36.
- **Fix (AD-3):** "If an event leaves a Site without an Owner, the Site grain marks the Site *ownerless*: writes are refused except restoring an Owner, the Site shows a banner in all clients, and a structured error is logged. The write is not auto-reverted." (Alternatively, restore memlog wording and state the reconcile action.)

### M10. Event-sourcing scope is incomplete for entities the spine itself names
- **What:** AD-2 lists Device events (paired, assigned, moved, enrolled, paused, resumed) but omits *unassigned* (FR-2) and *Silence Window changed* (FR-13). Lot is a grain (AD-1), but AD-2 does not say whether it is event-sourced. Site event-sourcing is a distill addition that is not in memlog line 43 and not recorded as an assumption.
- **Source:** PRD FR-2, FR-6, FR-13; memlog line 43.
- **Fix (AD-2):** Add "unassigned, Silence Window changed" to Device, and state "Lot: event-sourced (created, renamed, removed, Node slot changes)" or "Lot: mutable state." Log the Site addition in the memlog as a distill-introduced assumption.

### M11. Battery and charging reports have no data home
- **What:** FR-4 requires battery % and charging status on every report, and FR-8 and FR-14 consume them. The spine does not say whether they are Readings (in the Readings table, deduped by Sensor) or Device telemetry in the frame header. Firmware (`packages/proto`) and the Server could model them differently.
- **Source:** PRD FR-4, FR-8, FR-14.
- **Fix (AD-9):** "Battery % and charging status are fields of the sealed Node frame header, stored per frame as Device telemetry (not Sensor Readings) and evaluated by the Device grain."

---

## Low

### L1. Distill-introduced assumptions are presented as settled rules
- **What:** Memlog lines 69–74 say these are to be confirmed at triage: AD-4 (Role from projection), AD-7 (the grain split), AD-8 (paused ack-and-discard), AD-9 (raw + Calibration ID), AD-11 (Server time authority), and the conventions. The spine gives no marker. Only AD-3 is tagged `[ADOPTED]`, while the memlog also marks NATS-only (AD-5), the Streaming.NATS pin (AD-5), service defaults (AD-15), and the BFF (AD-14) as ADOPTED.
- **Fix:** Tag ADs `[ADOPTED]`/`[ASSUMPTION]` in line with the memlog, or resolve the assumptions and log the outcome.

### L2. The Phase Two licence rationale is not recorded
- **What:** Memlog line 20 records Phase Two keycloak-orgs as Elastic License 2.0 (not OSS). Line 25 adopts it on condition that it is "run as deployed software, nothing redistributed in repos." The spine drops that condition, so a future epic could vendor the extension into `deploy/`.
- **Source:** memlog lines 20 and 25; PRD NFR-8.
- **Fix (AD-3 or AD-15):** "Phase Two is consumed only as its published image. No Phase Two code or artifact is committed to or redistributed from Coldframe repositories (ELv2)."

### L3. The NFR-9 command safeguards are only partly carried
- **What:** NFR-9 says commands "can require confirmation, can be stopped manually, and are blocked by Pause." AD-16 and AD-8 cover only Pause.
- **Fix (AD-16):** "The future command model reserves a confirmation state and a stop/cancel event on the Device grain."

### L4. Lot history across reassignment is ambiguous
- **What:** Reading rows carry `device_id` and `sensor_id`, not a Lot. FR-2 keeps history on reassignment, and FR-8 charts per Lot. The spine does not say whether a Lot's chart shows the Readings of its current Node or of whichever Node occupied the Lot at the time.
- **Fix (AD-9):** "Lot history is reconstructed from assignment events: a Lot's chart shows Readings of whichever Node occupied it at each `measured_at`."

### L5. Binds list NFRs that no AD governs
- **What:** The frontmatter binds NFR-4, NFR-5, NFR-6, and NFR-11, but no AD or map row references them. NFR-6 (automated multi-Site tests) is an explicit cross-team obligation.
- **Fix:** Add a map row: "NFR-6: authorization policy (AD-4) covered by automated multi-Site tests". Either drop NFR-4, NFR-5, and NFR-11 from `binds` or map them to hardware/firmware with "no spine AD; see addendum."

### L6. Stack is missing security-pinned versions from the memlog
- **What:** Memlog line 56 records Fleet ≥ 0.14.5 (CVE-2026-41050). Fleet is named in AD-15 but has no row in Stack.
- **Fix:** Add "Fleet | ≥ 0.14.5" to Stack.

---

## Memlog decisions checked and correctly carried
Ack after PostgreSQL commit (lines 57, superseding 35); dedupe key (34); Orleans Reminders wake-only with persisted due-at (38–39); NATS as the Orleans streams backing store only, pinned alpha, one seam (31, 33); Temporal limited to the Keycloak pipeline (26, 38); DNS-01 with split DNS (40); eFuse HMAC key, AEAD, and no mTLS (41); KMP plus native UI (42); monthly-partitioned Readings, no TimescaleDB (45); SvelteKit plus SignalR and the BFF (46, 65); REST/OpenAPI with Protobuf frames in base64 (49, 52); UTC (53); monorepo layout (55); a single CNPG cluster with three databases (58); secrets out of band (60); Escendit service defaults (61); the public-dependency rule (64); Aspire dev-only (37).
