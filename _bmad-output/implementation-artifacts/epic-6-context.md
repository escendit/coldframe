# Epic 6 Context: Get told when to water

<!-- Generated from planning artifacts. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Turn Thresholds into action: the Server opens a Threshold Alert when a Lot stays beyond a Threshold and closes it when the Lot recovers, and every User with a Membership on the Site is told at a time of day they chose. A push such as "Tomatoes needs water" arrives inside the User's Notification Window, Reminders repeat while the Lot stays dry, anything that fell due overnight arrives as one morning summary, and a User can mute a Site. The web app shows the same Alerts as browser notifications while it is open and refreshes live. This epic also makes the *needs water* Lot status live, and builds the delivery machinery that Epic 7's Health Alerts reuse.

## Stories

- Story 6.1: Threshold Alerts open and close
- Story 6.2: See Alerts in the apps
- Story 6.3: My notification settings and the Site Reminder cadence
- Story 6.4: Delivery timing: windows, summaries and Reminders
- Story 6.5: Push notifications on my phone
- Story 6.6: Browser notifications and live updates on the web

## Requirements & Constraints

- An Alert opens after 3 consecutive Readings beyond a Threshold (about 30 min at the 15-minute interval) and closes after 3 consecutive Readings back within the Thresholds; 1-2 Readings on the other side change nothing. It names the Lot and the side crossed; low soil moisture reads "<Lot> needs water".
- No Threshold Alert opens for an uncalibrated calibrating Sensor or a Sensor without Thresholds. An empty high never alerts.
- Reminders repeat only while a Threshold Alert is open, at the resolved cadence: the User's setting for the Site, else the Site setting, else once per day. Only Owners and Administrators set the Site cadence; Members see it read-only. A closed Alert sends no Reminder, and closing sends no notification.
- Alerts and Reminders go to every User with a Membership on the Site. Payloads are self-contained and understandable away from home.
- Each User has one daily Notification Window (default 07:00-22:00; "from 07:00" alone is valid) evaluated in their own IANA time zone, correct across daylight-saving changes. Anything due outside it is held and sent as one summary when the window opens, with at most one entry per open Alert however many Reminders fell due; an Alert that opened and closed while held is not delivered.
- Mute is per User per Site: it affects only that User, and Alerts still open and close for everyone.
- Browser notifications follow the same window and mute rules, applied on the Server. Nothing is delivered when no tab is open; there is no background web push.
- Restarting the Server loses no open Alerts, settings or pending deliveries (a summary due at 07:00 is still sent after a restart across 07:00).
- Counter-metric: about one notification per Lot per User per day under defaults. The UI never originates notifications (no recovery pings, no nudges), and Alerts have no actions (no mark-watered, no snooze).
- The Alerts read model is paginated, filtered by Site and in the authorization matrix, like every new endpoint.
- Test-first: named tests written failing before implementation. Server: Orleans TestCluster with a fake clock and a Notifier test double. Apps: snapshot tests in light and dark themes, adapter tests with APNs/FCM mocked, and a Playwright end-to-end run of the morning-nudge journey against the Aspire AppHost. A real push on an iPhone and an Android phone is a manual checklist item.

## Technical Decisions

- **Evaluation** belongs to the Sensor grain. It runs in `measured_at` order and is monotonic: a Reading older than the grain's `lastEvaluatedAt` is stored but not evaluated. A Threshold change or a new evaluation epoch resets streaks.
- **Alert identity** is `UUIDv5(subjectKind:subjectId:alertKind:episode)`. The evaluating grain persists the episode counter before calling the idempotent `Alert.Open`, so a retry never duplicates. At most one open Alert exists per subject and kind; a Threshold Alert records its side. Only the grain that opened an Alert closes it, with a reason from `recovered | paused | unassigned | calibrated | removed`.
- **Fan-out:** Alert events are published per Site, and the Site grain keeps the set of open Alerts (rebuildable from the journal). The User grain keeps its own Site set from Membership events: it pulls a Site's open Alerts on join, drops them on removal, and reconciles against `Site.OpenAlerts()` on activation.
- **Only the User grain decides when to notify.** It owns window holding and summaries, mute, push tokens, time zone, per-Site cadence and one Reminder deadline per Alert. The Site cadence lives on the Site grain and is cached in each member's User grain. Health Alerts use `max(resolved, 24 h)`. Every settings change is an event on the User grain.
- **Deadlines are persisted state.** Each window opening and Reminder is a UTC `due-at` on the User grain; an Orleans Reminder is only a wake-up (period at least 1 minute), and every wake and every activation processes everything overdue. Time is read only from `TimeProvider`.
- **Notifier seam:** one seam wraps APNs, FCM and SignalR and contains no timing or filtering logic. Each delivery is sent within 1 minute of falling due, and `dueAt` and `sentAt` are recorded as a structured log field and an OpenTelemetry metric. A token the provider reports invalid is removed from the User grain.
- **Time zone:** detection (mobile OS; browser zone sent by the BFF; IP geolocation as a last resort) only proposes a value. The User confirms or picks another, and a zone the User chose is never overwritten.
- **Status:** `needsWater` means an open low-side Threshold Alert on a soil-moisture Sensor of the Lot's Node. It is computed by the LotStatus projection on the Server and sorts first; clients only render.
- **Web transport:** the browser reaches SignalR only through the SvelteKit BFF, which attaches the access token. SignalR carries exactly two families: `notification.delivered` (only from the Notifier, already windowed and muted) and `readmodel.changed { resource, id, version }`, a hint with no domain data after which the client refetches over REST.
- **Contracts:** SignalR messages and push payloads are authored in `packages/asyncapi`, REST in `packages/openapi`; client types are generated and checked in CI. Push-token registration lives in the shared Kotlin core; the native shells hold UI only.
- Events are past-tense (`AlertOpened`); lists are cursor-paginated; errors are Problem Details.

## UX & Interaction Patterns

- **Alerts surface** (tab/nav on all three clients): open Alerts with the Threshold group before the Health group, newest first, then a "Closed" section for the last 7 days. Empty state is "No open Alerts.", never "All good". The mobile tab label carries the count ("Alerts · 5"). Tapping a Threshold Alert row opens Lot detail. Row labels for screen readers state the condition and when it started.
- **Colour:** solid orange is reserved for low-side soil-moisture Alerts ("needs water"). "Too wet" and other Sensors use the neutral Threshold treatment with an above-high/below-low eyebrow; Health rows are hatched and dashed; Closed rows are outline only.
- **My notifications:** Notification Window control (from/to pickers, decorative 24 h bar hidden from screen readers, helper explaining the single summary), time-zone confirm panel with a searchable IANA list, "Mute <Site>" native switch, and personal Reminder cadence "Use Site setting" / "Daily" / "Every 2 days". Site settings offers "Daily" / "Every 2 days" (default Daily). There is no "Never".
- **Payload copy:** title is the condition, body is value and context ("Tomatoes needs water" / "~20 % in the soil, your low is 30 %."; "Herbs too wet" / "~65 % in the soil, your high is 60 %."). Reminders reuse the Alert text with "Still" in the body. The summary title is "<Site>: 2 need water, 3 to check", one line per open held Alert with needs-water first and a footer naming the held hours. Soil moisture keeps the `~` prefix rounded to 5 %; counts use plural rules; all strings are externalised.
- **Presentation:** grouped per Site (iOS thread identifier, Android group), standard interruption level, no app-icon badge. Tap routing: Threshold to Lot detail, summary to the Site overview, switching to the notification's Site.
- **Permission:** mobile asks on the first landing on a Site overview (creator or newly accepted Member) after one line of why. If denied or revoked, My notifications shows a persistent notice with Open Settings and the overview shows the same one-line notice until granted; permission is rechecked on every foreground. Web asks only when "Browser notifications while Coldframe is open" is turned on; if the browser blocks it, the toggle returns to off with an explanation. One Alert appears once per device.
- "Silence Window" is used only for Device silence; time outside a Notification Window is "outside your window".

## Cross-Story Dependencies

- 6.1 is the base: 6.2 reads its Alerts, and 6.4 consumes its per-Site Alert events. 6.4 needs the settings from 6.3. 6.5 and 6.6 are adapters behind the Notifier seam built in 6.4; 6.6's end-to-end test exercises 6.1-6.4 together.
- Builds on Epic 5 (Calibration state and the Thresholds-changed event that resets streaks), Epic 4 (ingestion, Sensor grains, the LotStatus projection, Lot detail) and Epic 1 (User grain, Membership events, authorization matrix, Create Site time-zone confirmation).
- The time zone confirmed on Create Site is currently kept only per device and never reaches the Server; 6.3 adds the User time-zone preference and must send that stored choice once, then read it from the Server.
- Feeds Epic 7: Health Alerts (silent, battery, uncalibrated) reuse the Alert grain, fan-out, the 24 h Reminder floor, the Alerts surface's Health group and their payload templates. Tap routing to a Devices row applies to those Alerts.
- Feeds Epic 8 (Pause closes open Alerts with reason `paused` through the evaluation epoch) and Epic 9 (join or leave while Alerts are open).
