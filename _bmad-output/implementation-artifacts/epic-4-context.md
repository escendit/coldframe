# Epic 4 Context: See what my soil is doing

<!-- Generated from planning artifacts. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Simon presses a Node's button, assigns it to "Tomatoes", and sees its Readings: Lot tiles, Lot detail with a 30-day chart, battery and charging status, and last seen. The epic delivers the whole Reading path end to end: Node firmware (wake, measure, deep sleep, 24 h buffer, sealing, report now, channel following), ESP-NOW relay through the Hub, exactly-once ingestion with acknowledgement-after-commit and a replay window, the partitioned Readings table, Lot and Sensor grains, and the Server-computed LotStatus projection behind an honest overview that marks stale data and an unreachable Server. It reuses Epic 3's contracts, key hierarchy, BLE setup session, enrolment path and Device simulator. Firmware is tested host-side only; power, reach and coexistence are checked manually against a bench checklist.

## Stories

- Story 4.1: Node firmware foundation: wake, measure, sleep
- Story 4.2: Node pairing: setup mode, enrolment and Lot assignment
- Story 4.3: Add a Node from my phone
- Story 4.4: ESP-NOW transport from Node to Hub
- Story 4.5: Server ingestion and acknowledgements
- Story 4.6: Sensor Specifications and Sensor grains
- Story 4.7: Lot status and the Site overview
- Story 4.8: Lot detail with history and Device status
- Story 4.9: Move or unassign a Node

## Requirements & Constraints

- Node reports one Reading per Sensor every 15 min through the Hub, plus battery % and charging status. All Readings from one wake share `measured_at`. The Hub stores no Readings. Reporting must survive a router Wi-Fi channel change and work from the farthest Lot where Wi-Fi is unusable.
- Node buffers ≥24 h of unacknowledged Readings and resends them; a Reading is deleted only on a sealed acknowledgement, which the Server sends only after a durable commit. Radio-level send status never counts as delivery.
- Hardware: ESP32-S3, separate ADC capacitive soil probe (raw, two-point Calibration later, no temperature compensation), BME680 in forced mode (temperature, humidity, raw gas resistance in Ω). Probe and battery divider are power-switched. Battery % from a LiPo discharge-curve table, marked approximate. Power budget: average sleep current ≤ ~100 µA, short wakes; a season on solar and ≥14 days with no sun.
- Pairing: Node advertises only after a long press of its setup button, for a limited time; a short press means "report now". App lists only in-range Nodes. A Lot holding a Node rejects a second one. Reassigning keeps Reading history. Unassigned Node Readings are stored but not evaluated.
- Each Node declares its Sensors/Specifications on first report. Soil moisture is `calibration: true` with default Thresholds; temperature, humidity and air quality are watched only. Redeclaring never overwrites a Threshold override.
- Overview: exactly one Server-computed status per Lot, precedence noNode > paused > unknown > needsCalibration > needsWater > ok, sorted needsWater, needsCalibration, unknown, ok, paused, noNode. A stale Reading is never shown as current; apps say when the Server is unreachable and how old the data is. Readings retained indefinitely.
- Only Admin+ adds, moves or unassigns Nodes; Members get 403 and every new endpoint joins the authorization matrix.
- Test-first (red → green): firmware host-side tests with hardware behind traits; Orleans TestCluster grain tests; Server integration on the Aspire AppHost using crypto-spec vectors and the Device simulator; KMP core with mocked BLE; mobile/web snapshot tests for every tile variant in light and dark, with accessibility checks at the largest text size. BLE/ESP-NOW coexistence on the Node is a manual checklist item.

## Technical Decisions

- **Node frame and sealing:** Protobuf Node frame carries `protocol_version`, `spec_hash` and Readings; sealed with ChaCha20-Poly1305 under `seal/v1`, nonce = Device ID + 64-bit counter. Counter never repeats for the key's life: reserved in flash in blocks (on boot jump to stored ceiling, raise it before sealing). Every transmission, including resends, is freshly sealed; the buffer keeps payload (`reading_seq`, `measured_at`, value), never sealed bytes. `reading_seq` is a per-Device counter persisted the same way.
- **Hub relay:** forwards sealed frames base64 in the JSON envelope of `POST /device/ingest` (HMAC Hub auth) without reading them; returns opaque sealed downlinks over ESP-NOW; may keep the latest missed downlink per Node in volatile RAM only; never synthesizes acks. Any enrolled Hub may relay any Node.
- **Ack window:** Node waits 300 ms for its sealed downlink; on a miss it keeps the Readings, collects the late ack on its next wake, and re-scans channels after repeated misses.
- **Ingestion:** Server verifies the seal and replay window (above high-water mark or an unseen slot in a 64-entry window), inserts Readings unique on `(device_id, sensor_id, reading_seq)` into an append-only monthly-partitioned table (raw value, `measured_at`, Calibration in force), battery/charging into an append-only device-reports table. HTTP 200 whenever the envelope parses, with per-frame status `stored | duplicate | rejected_auth | rejected_replay | rejected_time | unknown_device | retry`; only `stored`/`duplicate` carry an `ack/v1` downlink. 4xx only for unparseable envelope or failed Hub auth; 5xx only when nothing was processed. Duplicates caught by the Reading key, not the counter. Partitions exist ≥2 months ahead plus a default partition. After a DB restore, replay high-water marks and downlink counters advance by a documented margin.
- **Downlink:** carries `serverTime`, acknowledged `reading_seq` ranges and an empty reserved `commands` field. Node sets its RTC only from authenticated downlinks, slews, never steps back >1 s. Unsynced Readings are flagged `time_unsynced` with boot ID + uptime and rebased by the Server; `measured_at` >5 min in the future is `rejected_time`.
- **Device grain gate:** paused Device Readings are acked and discarded; unassigned Node Readings stored but not evaluated. Device grain sends Sensor grains an evaluation context (assigned, Lot, paused, epoch) on changes. It records the Node's last relay Hub (used later for Hub-silence suppression).
- **Lot occupancy:** Lot grain owns it. Assign = Device grain calls `Lot.Claim(nodeId)` (succeeds only if the Lot exists, isn't removed, and is free or already held by this Node), then persists `DeviceEnrolled` + `DeviceAssigned`. Move = claim new, persist `DeviceMoved`, release old (idempotent, retried from persisted pending state). A claimed Lot refuses removal. The Device grain is the only source of "which Lot am I on"; history follows the Node.
- **Sensor identity:** `sensorId = UUIDv5(deviceId:slot:quantity)`, slot = index in the Specification set. Unknown `spec_hash` → downlink asks for the full set, sent once. Same hash is a no-op; a changed Specification updates defaults only; a new quantity at a slot creates a new Sensor. Thresholds stored per side as `Default | Override | Cleared`. Undeclared-slot Readings are stored and acked, not evaluated.
- **LotStatus projection:** computed once on the Server, exposed via OpenAPI with `status`, `statusSince`, `lastReadingAt`, `unknownCause ∈ {node, hub}`, `pausedBy ⊆ {device, site}` and sort order. `needsCalibration` = the Lot's Node has an uncalibrated `calibration: true` soil Sensor (live in this epic); `needsWater`, `unknown`, `paused` render from fixtures now and go live in Epics 6, 7, 8. Clients only render; their own logic is limited to data age and Server reachability. SignalR `readmodel.changed` is an invalidation hint only; clients refetch over REST.
- **APIs:** history queries take `from`/`to` and are cursor-paginated; RFC 9457 errors; camelCase JSON, enums as strings, absent optionals omitted; UTC everywhere; never log Reading payloads, keys or tokens.

## UX & Interaction Patterns

- **Add a Node (mobile only, Admin+):** five-step full-screen modal reached from Devices, a *no Node* tile, or "Hub is online": press the setup button → in-range Nodes as candidate tiles, strongest first, "PRESSED JUST NOW" badge, scanning continues → setup code → Lot picker (Lots with a Node disabled with "HAS A NODE", "+ New Lot" inline, primary button "Put ‹ID› in ‹Lot›") → outcome "‹Lot› has a Node" with its Sensors and CALIBRATE SOIL MOISTURE when needed. Errors (Bluetooth off, setup window timeout "‹ID› stopped listening. Press its setup button again.", lost connection, wrong code, Lot taken meanwhile) never leave anything half-assigned. Progress announced politely, errors assertively.
- **Lot tiles:** six status variants plus stale and skeleton, each distinct by shape + Carbon icon + text, never colour alone; hatch primitive for unknown/needsCalibration; rendered in Server order with no client re-sort. Uncalibrated shows `raw` and the raw value, never %. Formatting: soil `~` rounded to 5 %, whole °C and %RH, gas in kΩ (3 significant digits).
- **Site header:** headline sentence ("Nothing needs water", "2 Lots can't be read", "No Readings yet"…) exposed as a heading, counts subline.
- **Stale mode:** after the first failed refresh plus one retry, the Stale header replaces the summary ("‹Site› · can't reach your Server", ticking age never announced), every tile goes stale ("WAS ‹STATUS›", "as of 07:02"), admin actions disabled with "Needs your Server"; leaves on the first successful refresh ("Live again."). Cold start shows cached data as stale, or outline-only skeletons with "Loading ‹Site›".
- **Layout:** 2-column phone grid; web 1–4 columns by width; one-column fallback at large text sizes (tiles never shrink). Pull-to-refresh on mobile, refetch on focus on web.
- **Lot detail:** hero (status, "since ‹statusSince›"), 3-up Sensor cells, 30-day History chart (daily-low bars, gaps for missing days, Sensor picker, text summary as accessibility label, Threshold band once Thresholds exist), 2-up Device cells (battery, charging, last seen), admin strip for Admin+. Status-specific hero content for unknown, paused and needsCalibration.
- **Devices:** "Hubs" then "Nodes" (by Lot name) with Device ID, battery, charging, last seen; Admin+ row actions include move to another Lot and unassign (no BLE needed, web or mobile); Members don't see them.

## Cross-Story Dependencies

- 4.1 precedes 4.2 and 4.4. 4.2 (setup mode, enrolment, `Lot.Claim`) precedes 4.3 and 4.9. 4.4 and 4.5 are a pair: 4.5 replaces Epic 3's `/device/ingest` placeholder and must agree with 4.4 on frame, ack and downlink contracts. 4.6 builds on 4.5's ingestion path. 4.5 and 4.6 feed 4.7 and 4.8; 4.3's outcome shows Sensors once 4.4–4.6 land.
- Relies on Epic 3: shared proto/OpenAPI/crypto-spec contracts and vectors, eFuse key hierarchy and dev mode, AD-25 BLE session, HPKE enrolment, Hub heartbeat/HMAC auth, Device grain, Device simulator, Devices list. Relies on Epic 1 (Site/Lot grains, per-Site authorization and matrix, event journal and projectors, design tokens) and Epic 2 (partitions via the migration job, restore runbook).
- Later epics light up statuses rendered here from fixtures: Epic 5 (Calibration, Thresholds, chart band), Epic 6 (needsWater via Threshold Alerts), Epic 7 (unknown via Silent Alerts using last relay Hub; Uncalibrated Alert shares the needsCalibration condition), Epic 8 (paused).
