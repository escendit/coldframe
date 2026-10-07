# Epic 5 Context: Calibrate the soil and set Thresholds

<!-- Generated from planning artifacts. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Let Simon turn raw soil-probe values into an approximate 0-100 % moisture reading by calibrating a Sensor (dry and wet points), and set low and high Thresholds per Sensor. Afterwards Lots show approximate % and *OK*, and the History chart shows the Threshold band. *Needs water* is not live yet: it arrives with Threshold Alerts in Epic 6, because it is defined by an open low-side Threshold Alert.

## Stories

- Story 5.1: Calibration on the Server
- Story 5.2: Calibrate from the app
- Story 5.3: Thresholds on the Server
- Story 5.4: Set Thresholds in the app and see them on the chart

## Requirements & Constraints

- Only Administrators (and Owners) calibrate, and only Sensors whose Specification says `calibration: true`; other Sensors show no Calibrate control. Only Owners and Administrators change Thresholds. Members get read-only views with admin controls hidden (not disabled); the API rejects their changes with 403, and every new endpoint is in the authorization matrix.
- Calibration is two-point linear (dry: probe in dry soil; wet: probe in water). Reference points are raw values of already-stored Readings the Administrator picks, submitted over REST, never over BLE.
- A half-finished Calibration (only dry) leaves the Sensor uncalibrated and keeps the dry point. Indistinct dry/wet points are rejected with Problem Details.
- Recalibration affects only later Readings; history keeps the Calibration it was stored with. Threshold % values do not change on recalibration.
- Thresholds on a calibrating Sensor are always expressed in 0-100 % (5 % steps in the UI). Low is required on an alerting Sensor, high is optional (empty never alerts), low must be below high.
- A watched Sensor with no Specification default (e.g. temperature) gets a proposed low of `Min + 20 % x (Max - Min)` when alerts are turned on, with no proposed high.
- Until a Sensor is calibrated it opens no Threshold Alert; saving a Calibration moves the Lot out of *needs calibration* (its % appears with the next stored Reading, never before).
- Test-first (NFR16): named tests written failing before implementation. Server: Orleans TestCluster and integration tests. Apps: snapshot tests in light and dark themes (largest text size for Thresholds) plus a Playwright end-to-end flow (calibrate, set low Threshold, tile shows ~% *OK*).

## Technical Decisions

- The Sensor grain is the only writer of Calibration and the only validator of Thresholds. It persists `SensorCalibrated` (new Calibration ID, both raw values), and synchronously sets the Calibration in force on the Device grain before confirming, re-delivering from persisted state if that call fails. The Device grain treats it as a read-only cache.
- Readings are append-only in the monthly-partitioned table; each row stores the raw value and the ID of the Calibration in force. Normalized % is derived from that Calibration and rounded to the nearest 5 % for display, so compensation can be added later without migration.
- Each Threshold side is stored as `Default | Override(value) | Cleared`; a later Specification redeclaration never replaces an override.
- A Threshold change raises a Thresholds-changed event, treated by Story 6.1 as a new evaluation epoch (streaks reset).
- Lot status is computed once on the Server as a read model exposed via OpenAPI; precedence `noNode` > `paused` > `unknown` > `needsCalibration` > `needsWater` > `ok`.
- Conventions: PRD glossary terms verbatim in code, API and UI; events are past-tense (`SensorCalibrated`); REST resources are plural nouns under `/sites/{siteId}/...`; calibrating Sensors expose 0-100 %.

## UX & Interaction Patterns

- Calibrate is a two-step full-screen modal (dry, then wet) on web, iOS and Android, reachable from the needs-calibration tile, Lot detail and the Node-added outcome (and later the Uncalibrated push).
- Each step waits for a Reading taken after the step started: waiting panel shows "Waiting for the next Reading", last raw value and time, hint to short-press the Node's setup button; a fresh Reading enables "Record dry"/"Record wet". A "Recent Readings" list is an alternative selection.
- The flow can be left and resumed (the dry point is kept; resume at wet). Confirmation shows both raw values and "% appears with the next Reading", updating in place (e.g. "Tomatoes reads ~40 %") when the first calibrated Reading arrives. No % is shown before the Server stores a calibrated Reading.
- On a paused Device, do not start the wait; explain Readings resume after the Pause ends, with Resume offered only to Admin+ when the Pause is the Device's own. Tested with a fixture `pausedBy` until Epic 8.
- Thresholds is a modal (Cancel/Save) with a Threshold column per Sensor: draggable or typed, current-Reading marker, dashed "no high" marker, "Add high" / clear. "Low must stay below high." appears inline and Save is disabled while invalid. Reachable from Lot detail, a Sensor cell, and right after Calibration.
- The 30-day History chart shows the Threshold band; daily lows below low use the below-low chart token. A calibrated in-range Lot tile shows ~% and *OK*.
- Accessibility: polite announcements for a fresh Reading ("New Reading 07:17, raw 612. Record dry is available.") and the first % ("Tomatoes reads about 40 percent."); the waiting text is never announced. Validation errors state what happened, what did not change and the next action. A 403 race shows "You can't change this on <Site>. Ask an Owner or Administrator."

## Cross-Story Dependencies

- 5.2 depends on 5.1 (Calibration endpoint); 5.4 depends on 5.3 (Threshold endpoints) and on the Lot detail chart/status from Epic 4.
- Builds on Epic 4: stored Readings and Sensor grains/Specifications (4.5, 4.6), Lot status and Lot detail (4.7, 4.8), and the Node-added outcome (4.3).
- Feeds Epic 6: Story 6.1 consumes the Thresholds-changed event and Calibration state to open/close Threshold Alerts, which makes *needs water* live. Epic 7 (uncalibrated Sensor Alert) closes with reason `calibrated`.
- Epic 8 makes Pause real; Story 5.2's paused behaviour is fixture-tested until then.
