# Epic 8 Context: Pause for maintenance and winter

<!-- Generated from planning artifacts. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Let an Administrator or Owner switch off ingestion and alerting on purpose, for one Device during maintenance or for the whole Site over winter, so that planned downtime never produces false Alerts. A Pause may carry an end date, at which it lifts by itself; without one it lasts until someone resumes. While paused, the apps say so plainly: the silence is shown as intentional, never as a fault and never as "OK".

## Stories

- Story 8.1: Pause and resume on the Server
- Story 8.2: Pause and resume in the apps

## Requirements & Constraints

- **Who:** Administrators and Owners pause and resume a Device or a whole Site. A Member is refused with 403 and sees the paused state but no controls.
- **While paused:** the Device's Readings are not ingested, no Alert of any kind opens for it, and its open Alerts close. No Health Alerts fire for paused Devices, however long they stay silent.
- **Site Pause** pauses every Device of the Site. A Device added to a paused Site starts paused.
- **Two independent sources:** a Device can be paused individually and by its Site at once; it resumes only when both are lifted.
- **End date is optional.** With one, resume is automatic and must survive a Server restart. Without one, the Pause lasts until someone resumes.
- **On resume** the Silence Window restarts from the moment of resume, so a Device that was quiet during the Pause is not reported silent the instant it ends.
- Future Device commands (not in V1) must be blocked by Pause; nothing here may make that harder.
- Test-first. Server: Orleans TestCluster with a fake clock covering Device pause and resume, Site pause reaching all Devices, redelivery after a failure, both sources held, an end date across a restart, Alerts closing on pause, no false silence after resume, and a Device added to a paused Site starting paused. Apps: snapshot tests in light and dark themes and a Playwright end-to-end run of the winter journey with a fake clock (pause the Site until a date; no Health Alerts while Nodes are silent; automatic resume on the date).

## Technical Decisions

- **One gate, at the Device grain.** Pause state lives only there, as a set of sources `pausedBy ⊆ {device, site}`, each with its own optional end date. A Device is paused if and only if the set is non-empty. No other component keeps or re-derives "is this paused?".
- **Site Pause is owned by the Site grain** (state and end date). It propagates idempotently to every Device in the Site's roster, with re-delivery driven from persisted pending state until each Device acknowledges. A Device joining a paused Site receives the Site's Pause in the join reply.
- **Evaluation context:** on every Pause change the Device grain sends each of its Sensor grains a new context (assigned, Lot, paused, epoch). A paused context makes the Sensor grain close its own open Alerts with reason `paused`; the new epoch resets streaks. The Device grain closes only its own `silent` and `battery` Alerts. Only the grain that opened an Alert closes it.
- **Readings from a paused Device are acknowledged and discarded**, not stored and not evaluated.
- **Resume** happens when the last source clears. The silence clock already counts from the latest of last accepted report, last Server start and last resume.
- **Pause end is a persisted deadline:** a UTC `due-at` in the owning grain's state, resolved as 00:00 on the chosen date in the IANA time zone of the User who set the Pause. An Orleans Reminder is only a wake-up (period at least 1 minute); every wake and every activation processes everything overdue. Time is read only from `TimeProvider`.
- **Lot status is computed on the Server; clients only render.** `paused` carries `pausedBy`. Precedence is `noNode` > `paused` > `unknown` > `needsCalibration` > `needsWater` > `ok`; `paused` sorts after `ok` and before `noNode`.
- Cross-entity state changes go through synchronous grain calls, never read-model checks; API handlers change state only by calling a grain.
- New pause and resume endpoints follow the standing conventions: listed in the authorization matrix, Problem Details errors, contracts authored in `packages/openapi` with generated client types.

## UX & Interaction Patterns

- **Entry points** (web, iOS, Android; Administrator and Owner only): Lot detail action strip (Device), a Devices row action, and the Site menu on the overview (Site). Resume is one tap from the paused Lot's detail, the Devices row or the Site menu.
- **Pause sheet:** native sheet on iOS, modal bottom sheet on Android, DS Modal on web, with Cancel/Save. Scope is a segmented choice (This Device / Whole Site), preselected to Whole Site when opened from the Site menu. Optional "Until" date with helper "Leave empty to pause until someone resumes." The primary button names the result ("Pause Home garden").
- **Site menu:** "Pause <Site>" / "Resume <Site>" and "Site settings"; the pause items are hidden for Members. Disabled with "Needs your Server" in stale mode.
- **Paused tile:** flat paused fill, 2 px solid purple border, the pause outline icon, value "—", foot "until 1 Nov" or "paused", label "PAUSED" or "PAUSED BY SITE" from `pausedBy`. Screen-reader label: "Strawberries, paused until 1 November" / "Strawberries, paused with the Site".
- **Site paused:** headline "Paused until 1 Mar" or "Paused" in paused ink, a line stating that no Alerts are sent while the Site is paused, and every tile paused. After resume, manual or automatic, tiles return to their live statuses.
- **Lot detail hero:** "PAUSED UNTIL 1 NOV" with Resume. Paused by the Site: "Paused with the Site" and, for Administrators and Owners, "Resume the Site to resume this Node".
- **Devices rows:** paused rows add the pause icon and "PAUSED" / "PAUSED BY SITE" in paused ink. Resume on a Device paused by the Site shows "Resume the Site to resume this Node" instead of resuming.
- **Device added to a paused Site:** the Hub-online and Node-added outcome screens add an Inline notice, "Home garden is paused, so 7C19 starts paused. It won't report or alert until the Site is resumed.", with Resume Site and "Calibrate later".
- **Calibrate on a paused Device** does not start waiting: "7C19 is paused, so it sends no Readings. Calibrate after the Pause ends.", with Resume only when the Pause is the Device's own.
- **Member read-only:** controls are hidden, not disabled; one Inline notice, "Only Owners and Administrators can resume it." A 403 race shows "You can't change this on Home garden. Ask an Owner or Administrator."
- **Colour and voice:** purple means paused and nothing else on data surfaces. Never "OK", "fine" or "all good" for a paused Lot. No exclamation marks or "successfully".

## Cross-Story Dependencies

- 8.1 is the base for 8.2, which renders its `paused` status and `pausedBy` and calls its pause and resume endpoints.
- Builds on Epic 7 (silence clock that already honours resume, `silent` and `battery` Alerts on the Device grain, the evaluation context sent to Sensor grains), Epic 6 (Alert grain, close reasons, delivery), Epic 5 (Calibrate flow, which gains the paused branch), Epic 4 (Reading ingestion, Sensor grains, the LotStatus projection, Lot detail, Add a Node outcome screen), Epic 3 (Device enrollment into a Site, Devices surface, Add a Hub outcome screen) and Epic 1 (Site grain and roster, per-Site authorization matrix, overview and Site menu).
- Membership roles from Epic 9 change who sees the controls; the Member read-only behaviour here must hold once Members exist.
