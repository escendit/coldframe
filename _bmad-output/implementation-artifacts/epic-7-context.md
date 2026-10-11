# Epic 7 Context: Know when something breaks

<!-- Generated from planning artifacts. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Make Coldframe honest about its own failures, so that silence is never read as "all fine". A Node or Hub that stops reporting, a Node whose battery is running out while it isn't charging, and a newly assigned soil Sensor that hasn't been calibrated each raise a Health Alert, delivered through the same windows, summaries and mute rules as Threshold Alerts but reminding at most once per day. A silent Hub is reported once, as the Hub, never as one Alert per Node behind it. A Lot whose Node or relay Hub is silent shows *unknown* with how long it has been quiet, instead of presenting an old Reading as current.

## Stories

- Story 7.1: Silent Device Alert on the Server
- Story 7.2: Silence in the apps and Silence Window settings
- Story 7.3: Low-battery Alert
- Story 7.4: Uncalibrated Sensor Alert

## Requirements & Constraints

- **Silent Device:** a Health Alert opens when nothing is accepted from an enrolled Device for longer than its Silence Window, and closes when the Device reports again. Defaults are 6 h for a Node and 5 min for a Hub, overridable per Device. A Hub heartbeats every 30-60 s even with no Node traffic, so Hub silence is detected independently of its Nodes.
- **Server downtime is never Device silence.** Nor is a Pause: the Silence Window restarts at the moment of resume, and no Health Alerts fire for paused Devices.
- **Hub silence is reported as the Hub only.** Lots behind it turn *unknown* with the Hub as cause; no per-Node Alerts open.
- **Low battery:** opens after exactly 3 consecutive device reports below 20 % while *not charging*; closes after 3 consecutive reports that are *charging* or at 20 % or above. Below 20 % while charging never alerts; not charging above 20 % never alerts. The Alert shows level and charging status. Its practical purpose is catching a shaded solar panel before the Node dies.
- **Uncalibrated Sensor:** opens when a Node is assigned to a Lot and one of its `calibration: true` Sensors has no Calibration; names the Lot and the Sensor; closes once calibrated. An uncalibrated Sensor opens no Threshold Alerts.
- **Reminders** for every Health Alert come at most once per day, whatever cadence a User or Site has chosen.
- Every Device that stops reporting must produce a Health Alert within its Silence Window (delivered when the User's Notification Window next opens), unless paused.
- Only Owners and Administrators change a Device's Silence Window; a Member is refused with 403 and sees no control.
- There is no Sensor-fault detection beyond these three kinds; a failed probe surfaces as a Threshold Alert or a Silent Device Alert.
- Test-first. Server: Orleans TestCluster with a fake clock covering default windows, per-Device override, recovery, a restart that causes no false silence, Hub suppression with several Nodes (one Alert) and its lifting; exactly-three battery open/close, charging suppression and flapping around 20 %; uncalibrated open on assignment and close on calibration or unassignment. Apps: snapshot tests in light and dark themes and a Playwright end-to-end run of the Node-dies-quietly journey with a fake clock.

## Technical Decisions

- **Who evaluates:** the Device grain evaluates silence and battery; the Sensor grain evaluates the uncalibrated condition. Only the grain that opened an Alert closes it. The Device grain closes only its `silent` and `battery` Alerts.
- **Alert identity:** `UUIDv5(subjectKind:subjectId:alertKind:episode)` with `alertKind` in `threshold | uncalibrated | silent | battery`. The evaluating grain persists the episode counter before calling the idempotent `Alert.Open`. At most one open Alert per subject and kind. Close reasons come from `recovered | paused | unassigned | calibrated | removed`.
- **Silence clock:** counted from the latest of last accepted report, last Server start and last resume. A Node's liveness is the latest `measured_at` it has had accepted, never arrival time.
- **Deadlines are persisted state:** each Silence Window expiry is a UTC `due-at` on the Device grain. An Orleans Reminder is only a wake-up (period at least 1 minute); every wake and every activation processes everything overdue. Time is read only from `TimeProvider`.
- **Hub suppression:** a Node's silence evaluation is suppressed while its last relay Hub has an open `silent` Alert, and the Node's window restarts when that Hub recovers. Any enrolled Hub may relay any Node; the Hub relation is used only for display and this suppression, never for frame acceptance.
- **Evaluation context:** on every assignment or Pause change, the Device grain sends each of its Sensor grains a context (assigned, Lot, paused, epoch). The Sensor grain opens the `uncalibrated` Alert from that context, and closes its own Alerts when the context becomes paused or unassigned. A new epoch resets streaks.
- **Battery input:** battery % and charging status arrive as device reports, stored append-only and separate from Readings. Evaluation runs in `measured_at` order and is monotonic per subject.
- **Delivery is reused, not rebuilt:** Health Alerts go through the existing per-Site fan-out, the User grain's window holding, summaries and mute, and the Notifier seam. The User grain applies `max(resolved cadence, 24 h)` for Health Alerts.
- **Lot status is computed on the Server; clients only render.** `unknown` means an open `silent` Alert on the Lot's Node or on its relay Hub, with `unknownCause` of `node` or `hub`. Precedence is `noNode` > `paused` > `unknown` > `needsCalibration` > `needsWater` > `ok`; sort order is `needsWater`, `needsCalibration`, `unknown`, `ok`, `paused`, `noNode`.
- New endpoints (Silence Window setting) follow the standing conventions: in the authorization matrix, Problem Details errors, contracts authored in `packages/openapi` with generated client types.

## UX & Interaction Patterns

- **Unknown tile:** hatched with a dashed border and the help icon, a large silence duration ("6 h"), foot "was ~40 % at 01:05", label "SILENT · UNKNOWN" or "HUB SILENT · UNKNOWN" taken from `unknownCause`. It sorts above every *OK* Lot. Screen-reader label states the Lot, unknown, what is silent and for how long, and the last value.
- **Lot detail hero when unknown:** duration and last Reading, plus "Check power or range." (Node) or "Hub 3F2A is silent; Lots behind it can't be read." (Hub).
- **Hub silent:** a non-dismissable Inline notice above the tiles, "Hub 3F2A silent for 12 min — Lots behind it can't be read.", announced politely when it appears.
- **Devices:** a silent row adds the help icon and "SILENT 6 H" alongside last seen; Nodes also show battery % and charging, with the low-battery icon below 20 %. Silence Window is a row action for Administrators and Owners only.
- **Alerts surface:** Health rows sit in the Health group after Threshold Alerts, hatched and dashed with eyebrow "HEALTH ALERT" and an icon by cause (help, low battery, tools). No actions on Alerts.
- **Payload copy** (title / body): "Node on Lot 'Beans' silent for 6 h" / "No Reading since 01:05. Check power or range."; "Hub 3F2A silent for 5 min" / "Lots behind it can't be read."; "Node battery 14 %, not charging" / "Lot 'Lettuce'."; "Soil sensor on Lot 'Peppers' needs calibration" / "An Owner or Administrator can calibrate it.". The morning summary lists a silent Node as "Beans: Node silent for 6 h".
- **Tap routing:** Silent and Battery go to that Device's row in Devices; Uncalibrated goes to Lot detail, where Calibrate is offered to Administrators and Owners.
- **Voice:** never "OK", "fine" or "all good" for a Lot that is unknown or needs calibration. Battery and Health conditions are not appended to Lot tile labels; they show on Lot detail and Devices. "Silence Window" is used only for Device silence. Uncalibrated soil moisture shows the raw value, never a %.

## Cross-Story Dependencies

- 7.1 is the base for 7.2, which renders its `unknown` status, `unknownCause` and Silence Window setting. 7.3 and 7.4 are independent of each other and of 7.2, but share the Health Alert plumbing and payload templates.
- Builds on Epic 6 (Alert grain and identity, per-Site fan-out, User-grain delivery timing with the 24 h Health floor, Notifier seam, the Alerts surface's Health group, push and browser delivery), Epic 5 (Calibration saved, which closes the uncalibrated Alert), Epic 4 (ingestion of Readings and device reports, Sensor grains, the LotStatus projection, Lot detail, Node assignment and move/unassign) and Epic 3 (Hub heartbeat, Devices surface).
- Feeds Epic 8: Pause closes open Health Alerts with reason `paused` and suppresses new ones; resume restarts the Silence Window, which 7.1's silence clock already accounts for.
