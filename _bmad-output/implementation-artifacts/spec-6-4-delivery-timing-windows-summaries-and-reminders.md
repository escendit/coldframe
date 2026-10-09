---
title: 'Story 6.4: Delivery timing: windows, summaries and Reminders'
type: 'feature'
created: '2026-10-09'
baseline_revision: 1ddd7f22e8c727f6077f58f4c90b61d6e9dc0147
status: 'done'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
  - '{project-root}/apps/cs/README.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Alerts open and close (6.1) and Users have a Notification Window, time zone, mute and Reminder cadence (6.3), but nothing tells a User anything: no User grain learns of an Alert, nothing decides when a delivery is due, and there is no Notifier seam for Stories 6.5 and 6.6 to plug into.

**Approach:** The User grain learns of its Sites' Alerts (told on open and close, pulled on join, reconciled on activation) and alone decides when to notify: at once inside the window, held for one summary per Site at the window opening, and as Reminders at the resolved cadence. Every deadline is a persisted UTC due-at; an Orleans Reminder only wakes the grain. Due deliveries go to a new Notifier seam that records `dueAt` and `sentAt` and holds no timing or filtering logic.

## Boundaries & Constraints

**Always:**
- Server only (`apps/cs/server`, `packages/cs/contracts`, `tests/cs`). The User grain is the only place that decides when to notify; the Notifier and its channels never filter, delay or schedule.
- Reaching the User grain: the Site grain never calls User grains. `ISiteGrain.AlertOpened`/`AlertClosed` answer with the Site's current members, and the Alert grain (the caller) tells each member's User grain inside its existing retried, journaled report loop: the report counts as acknowledged only when the Site and every member answered. The User grain's `AlertOpened`/`AlertClosed` are idempotent and ignore a Site that is not in its own Site set.
- Site set: on a Membership for a Site it did not hold, the User grain pulls `Site.OpenAlerts()`; when the Membership ends it drops that Site's Alerts, held deliveries and deadlines; on activation it reconciles every Site in its set against `Site.OpenAlerts()` (adds missing, drops gone). A pull that fails never fails activation or the Membership sync; it is repeated on the next wake until it succeeds.
- An Alert the User is told about is due at once. An Alert learned by pull or reconciliation gets no opening notification: its first Reminder is due at the first `openedAt + n × interval` after now.
- Due now and inside the window: one notification per Alert (`alert`, later `reminder`) handed to the Notifier. Due outside the window: held, and released as one `summary` per Site when the window opens, with exactly one entry per Alert still open however many deliveries of it were held. An Alert closed while held is dropped; a Site with nothing left sends no summary. Closing never notifies.
- Reminders: one due-at per open Alert, `previous due-at + interval`; overdue Reminders collapse into one and the due-at moves past now. Interval: Threshold Alerts use the resolved cadence (own, else cached Site, else daily; `daily` = 24 h, `every2Days` = 48 h); Health Alerts use `max(resolved, 24 h)` (a rule with unit tests; no Health kind exists yet). A cadence change re-schedules from the previous due-at.
- Mute: a delivery due for a muted Site is discarded (not held, not sent later) and its Reminder still advances; other Users are unaffected. Unmuting starts no catch-up.
- Window: evaluated in the User's time zone (chosen, else detected, else UTC) with `TimeZoneInfo`, correct across daylight-saving changes; a start time that does not exist on a spring-forward day opens at the first valid instant after it, an ambiguous one at its first occurrence. A window or zone change re-evaluates held deliveries at the next wake.
- Deadlines (AD-6): every Reminder due-at and the window-opening due-at are persisted through events on `user/{sub}`. One Orleans Reminder (period 1 minute) plus a short grain timer exist only while the User has open Alerts or pending pulls; every wake and every activation processes everything overdue. Time only from `TimeProvider`.
- Delivery is at-least-once: the grain journals a delivery as sent only after the Notifier returned; a Notifier failure leaves it due for the next wake. Sent ≤ 1 minute after `dueAt`.
- Notifier seam: `INotifier.SendAsync(Notification)`; the notification carries the User, the Site, kind (`alert | reminder | summary`), `dueAt`, the held-from instant for a summary, and per entry the Alert's id, kind, side, Lot, Sensor, Device, quantity and `openedAt`. The one implementation stamps `sentAt` from `TimeProvider`, calls every registered `INotificationChannel` (none ships; 6.5 and 6.6 add them), logs both instants as structured fields and records them on a Coldframe meter registered for export.
- New events get an alias, a `journal.json` row and an `Apply` overload, and survive a silo restart. Test-first (NFR16): the named tests fail before the implementation exists.

**Never:** No APNs, FCM, SignalR, push tokens, `packages/asyncapi` or payload text (6.5, 6.6). No Health Alert kinds (Epic 7). No new REST endpoint, OpenAPI change or client change. No call from the Site grain to a User grain, no Orleans stream for correctness, no read-model read from a grain. No wall-clock read. No recovery or "closed" notification. No write to `sprint-status.yaml`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| In window | Alert opens 12:00 local, not muted | one `alert` notification, `sentAt - dueAt` ≤ 1 min | Notifier throws: sent on the next wake |
| Overnight | Alert opens 23:30, Reminder of another Alert due 02:00 | nothing until 07:00, then one `summary` with two entries | — |
| Many held | Alert held with two Reminders due before the window opens | summary holds one entry for it | — |
| Closed while held | opens 23:30, closes 05:00 | no summary, no notification | — |
| Reminder | daily cadence, Alert open at 12:00 | `reminder` at 12:00 the next day; none after it closes | — |
| Cadence precedence | own `every2Days`, Site `daily`; then own unset; then neither | 48 h; 24 h; 24 h | — |
| Health floor | Health Alert, resolved interval below 24 h | 24 h | — |
| Muted | Site muted for A, not for B | A receives nothing, B is notified | — |
| Daylight saving | Europe/Zurich, held over the March and the October change | summary at 07:00 local on both days (05:00 and 06:00 UTC) | — |
| No time zone | none chosen or detected | window evaluated in UTC | — |
| Restart across 07:00 | summary pending, silo down from 06:50 to 07:05 | summary sent after the restart, once | — |
| Join | Membership granted while two Alerts are open | both tracked, no opening notification, Reminders follow | pull fails: repeated on the next wake |
| Leave | Membership ends with Alerts open or held | Alerts, held deliveries and deadlines for that Site are gone | — |
| Unknown Site | `AlertOpened` for a Site not in the User's set | acknowledged, nothing stored | — |
| Member unreachable | one member's User grain fails during fan-out | report stays pending and is repeated; others are not notified twice | — |

</intent-contract>

## Code Map

- `packages/cs/contracts/Sites/SiteGrains.cs` -- `IUserGrain` l.10-85 (add `alert-opened`, `alert-closed`), `ISiteGrain.AlertOpened`/`AlertClosed`/`OpenAlerts` l.171-192 (return members), `SiteAlert` l.691-701 (no SiteId: pass it alongside), `ReminderCadence` l.216-229, `NotificationWindow` l.237-259 (wall-clock minutes, never across midnight).
- `packages/cs/contracts/Sites/UserEvents.cs` -- nine `user.*` events; pattern `[EventType] [GenerateSerializer] [Alias("coldframe.user-…")]`, `[property: Id(n)]`. `packages/cs/contracts/Alerts/{AlertGrains,AlertEvents}.cs` -- `AlertKind { Threshold }` only, `AlertSiteNotified`.
- `apps/cs/server/Identity/UserGrain.cs` -- no `OnActivateAsync`, not `IRemindable`; `SyncSiteMembership` l.95-110 (early return when unchanged: a role change is not a join), `SyncSiteReminderCadence` l.217-234, settings writes l.117-214; next log EventId 2. `UserState.cs` -- next `Id(6)`; `Sites` l.111, `TimeZone` l.90, `NotificationWindow`, `SiteNotificationsOf(siteId).{Muted,ResolvedReminderCadence}` l.26-43, `Apply(SiteMembershipChanged)` l.141-156.
- `apps/cs/server/Journal/JournaledStreamGrain.cs` -- `RaiseEvent` + `ConfirmEvents`, `protected TimeProvider Clock` l.31; state exists only as journal events (no grain storage, no snapshots: keep events per delivery few).
- `apps/cs/server/Alerts/AlertGrain.cs` -- `ReportAsync` l.181-224 (calls Site, journals `AlertSiteNotified` after the acknowledgement), timer 1 s/5 s + `report-alert` reminder l.164-176, l.227-277: the wake-up pattern to copy. `AlertsHostingExtensions.cs` l.8 names this story's seam.
- `apps/cs/server/Identity/SiteGrain.cs` -- `AlertOpened` l.253-266, `AlertClosed` l.269-279, `OpenAlerts` l.282-286, rule "never calls User grains" l.14-16; `SiteState.Members` l.74.
- `apps/cs/server/Notifications/` -- `SiteReminderCadenceFanOut.cs` (member fan-out model), `TimeZoneProposal.cs` l.32 (`TimeZoneInfo` use). New seam, rules and hosting extension live here; register in `Program.cs` l.11-21 and `Hosting/ServiceDefaultsExtensions.cs` l.19-40 (`AddMeter`).
- `apps/cs/server/Identity/Reconciliation/IdentityReconciliationActivities.cs` l.82-100 -- the Membership calls; unchanged.
- Tests: `tests/cs/server.integration/Identity/IdentityCluster.cs` (`FakeTimeProvider` l.196, Orleans clocks stay real l.179-202 so the fake clock fires no timer or reminder: advance it, then wait with `JournalWait.UntilAsync`; `UseInMemoryReminderService` l.205; services registered by hand; `UserFaults` l.493-520; `RestartSiloAsync` l.111-115 resets the clock, restore it as `Devices/ThresholdAlertGrainTests.cs` l.918-923 does), `Identity/NotificationSettingsGrainTests.cs` l.270-291, `Identity/CapturingLoggerProvider.cs` (formatted message only), `Devices/ThresholdAlertGrainTests.cs` (`[Collection(IngestSuites.Name)]`, callers of `AlertOpened`). Unit: `tests/cs/server.tests/{Identity/UserStateTests.cs,Notifications/TimeZoneProposalTests.cs,Fixtures/journal.json l.547-589,Journal/FixtureJournalReplayTests.cs}`. No metric test exists: use `MeterListener`, no new package (restore is locked).
- Docs: `apps/cs/README.md` (l.74-81 add an event type, fan-out l.189-192), `packages/cs/README.md` l.33-37.

## Tasks & Acceptance

**Execution:**
- `tests/cs/server.tests/Notifications/{NotificationWindowRuleTests,ReminderIntervalRuleTests,NotifierTests}.cs`, `tests/cs/server.tests/Identity/UserStateDeliveryTests.cs`, `tests/cs/server.tests/Fixtures/journal.json` -- failing tests first: window containment and next opening (zones, both daylight-saving changes, no zone), interval precedence and Health floor, Notifier log and metric with `dueAt`/`sentAt`, state transitions, fixture rows -- NFR16
- `tests/cs/server.integration/Identity/{DeliveryTimingGrainTests,RecordingNotificationChannel}.cs`, `Identity/IdentityCluster.cs`, `Devices/ThresholdAlertGrainTests.cs` -- failing tests first: every matrix row from in-window to member-unreachable, including restart across 07:00 and join/leave; recording double and `UserFaults` for the new methods
- `packages/cs/contracts/Sites/{SiteGrains,UserEvents}.cs`, `packages/cs/contracts/Notifications/Notifications.cs`, `packages/cs/README.md` -- User grain methods, member-returning Site answers, `Notification` model, events `user.alert-tracked`, `user.alert-dropped`, `user.delivery-held`, `user.notification-sent`, `user.site-alerts-pulled`
- `apps/cs/server/Notifications/{INotifier,Notifier,INotificationChannel,NotificationWindowRule,ReminderIntervalRule,NotificationsHostingExtensions}.cs`, `apps/cs/server/Program.cs`, `apps/cs/server/Hosting/ServiceDefaultsExtensions.cs` -- seam, pure rules, meter, registration
- `apps/cs/server/Identity/{UserGrain,UserState}.cs` -- Alert tracking, pull and reconcile, due processing, wake-up Reminder and timer, re-evaluation after settings and Membership changes
- `apps/cs/server/Identity/SiteGrain.cs`, `apps/cs/server/Alerts/AlertGrain.cs` -- members in the answer, member fan-out inside the report loop
- `apps/cs/README.md` -- delivery timing, the seam and how 6.5/6.6 add a channel

**Acceptance Criteria:**
- Given a real Sensor crossing its low Threshold in the integration cluster, when the Alert opens inside a member's window, then that User's recording channel holds one `alert` notification for it without any test calling the User grain directly.
- Given deliveries of two Users on one Site with different windows and zones, when an Alert opens, then each is notified or held by their own settings.
- Given the User stream after held, sent and Reminder deliveries, when the silo restarts, then no delivery is lost or sent twice and the next Reminder due-at is unchanged.
- Given a User with no open Alerts and no pending pull, when the grain is inspected, then no wake-up Reminder is registered.
- Given the Server's telemetry setup, when a notification is sent, then one log entry carries `dueAt` and `sentAt` and the Coldframe notifications meter is among the exported meters.

## Spec Change Log

## Review Triage Log

### 2026-10-09 — Review pass
- verdicts: 44 findings — high 0, medium 4, low 32, false 8, maybe-false 0
- findings:
  - `[low]` `[reject]` Blind: one member whose User grain cannot be told keeps the report pending, so the Sensor does not deliver the close and the others keep being reminded — real, and it follows from "acknowledged only when the Site and every member answered"; the Sensor grain delivers a close only after its open was reported (`SensorGrain.DeliverAlertsAsync`), so a fix inside the Alert grain is unreachable. A User grain fails `AlertOpened` only when its journal cannot be written, which is an outage of the whole Server. Named under residual risks.
  - `[low]` `[reject]` Blind: no test for a close while the open report is stuck — the Sensor never sends that close (row above), so the state cannot be produced.
  - `[low]` `[reject]` Blind: a reconciliation that runs between the Site's acknowledgement and the tell tracks the Alert without an opening notification — needs an activation timer to fire inside that gap, or a failed tell followed by a reconcile; upgrading a pulled Alert needs a new event and state.
  - `[low]` `[patch]` Blind: the docs said a slow channel never holds up the Alert grain, but a wake waits for the Notifier and other calls queue behind it — README, `INotificationChannel` and the grain comment now say that `AlertOpened` sends nothing itself and that a channel bounds its own time.
  - `[low]` `[reject]` Blind: the member fan-out is sequential — a hung User grain is the outage case of the first row; parallel calls change failure handling for no everyday gain.
  - `[low]` `[reject]` Blind: reconciliation runs only on activation, so a stale tracked Alert is reminded for good — no path to a missed close was shown (closes are repeated until every member answered; a deleted Site ends every Membership); a periodic reconcile adds a deadline of its own.
  - `[low]` `[reject]` Blind: a muted Site keeps the timer and the reminder alive — an idle tick every 5 s per such User; excluding muted Sites needs re-scheduling on every mute change.
  - `[low]` `[reject]` Blind: no back-off and no failure metric on retries — a failing send or pull is logged on every wake; back-off adds state.
  - `[low]` `[patch]` Blind: the delay histogram shows lateness that is by design — README now names the three cases.
  - `[low]` `[reject]` Blind: no margin on "≤ 1 minute" when only the reminder wakes the grain — AD-6 sets the reminder period to at least 1 minute, which is also the Orleans minimum.
  - `[low]` `[reject]` Blind: a 24 h interval drifts by an hour against the local window across daylight saving — the Reminder is then held for the morning summary; still about one a day.
  - `[low]` `[reject]` Blind: `Contains` reads closed between the two occurrences of an ambiguous start — needs a window starting inside the repeated hour of one night a year.
  - `[low]` `[reject]` Blind: an unknown time zone ID becomes UTC silently — Story 6.3 stores only zones the Server knows; needs a tz database that dropped one.
  - `[low]` `[reject]` Blind: joining a Site adds an inline Site call to the Membership sync — a slow Site grain holds it as it already holds Create Site.
  - `[low]` `[reject]` Blind: every activation calls `OpenAlerts()` per Site — the intent asks for it; one grain call per Site.
  - `[false]` `[reject]` Blind: `IUserGrain.AlertOpened` trusts its caller — grain interfaces are not reachable from outside the silo; no endpoint calls it.
  - `[low]` `[reject]` Blind: fixture rows 14 and 17 show a sequence the grain cannot produce without a window change in between — the fixture pins shapes, `Apply` does not validate, and the replay test asserts the due-ats of exactly these rows.
  - `[low]` `[reject]` Blind: other tests leave Alerts open on the shared cluster — idle timers of a few grains; the suite ran 1200 of 1200.
  - `[low]` `[patch]` Blind: over-long comment line in `AlertGrain.cs` — rewrapped.
  - `[low]` `[reject]` Edge: a member who keeps failing `AlertOpened` after the Alert closed blocks the close — as the first Blind row.
  - `[false]` `[reject]` Edge: members of a Site that stopped being Active are never told a close — a Site leaves Active only by deletion, and the reconciliation then ends every Membership (`SyncSiteMembership(null)`), which drops the Site's Alerts.
  - `[low]` `[reject]` Edge: a pull or reconcile before the tell loses the opening notification — as the third Blind row.
  - `[low]` `[reject]` Edge: an Alert told before the Membership reached the User grain is acknowledged and later pulled without an opening notification — a window of one reconciliation run; the intent states both rules.
  - `[low]` `[reject]` Edge: a due-at inside yesterday's window first seen after the window reopened is sent as a late alert or reminder — needs the silo down overnight; the User is still told once, inside the window.
  - `[medium]` `[patch]` Edge: while one channel fails, every retry hands the channels that delivered the same notification again, every 5 s — `Notifier` now tries every channel, logs a failing one (EventId 2) and fails the send only when no channel delivered; tests and docs updated.
  - `[medium]` `[patch]` Edge: a failing channel skipped the channels after it — same fix as the row above.
  - `[low]` `[reject]` Edge: a Site whose `OpenAlerts` keeps failing is asked every 5 s with a warning each time — as the back-off row.
  - `[low]` `[reject]` Edge: a window that lies wholly inside the hour the clocks skip opens at an instant `Contains` refuses — one night a year for a window under an hour long at 02:00; the summary follows a day later.
  - `[medium]` `[patch]` Gap: no test reached a night-time due-at first seen while the window is open — `ADeliveryThatFellDueAtNightAndIsFirstSeenInsideTheWindowIsASummaryNotAReminder` added.
  - `[low]` `[patch]` Gap: held deliveries following a window or zone that now opens earlier were not verified — `HeldDeliveriesFollowAWindowOrAZoneThatNowOpensEarlier` added.
  - `[medium]` `[patch]` Gap: reconciliation on activation was verified only for a User who already tracks an Alert — `AUserWhoTracksNothingStillReconcilesOnActivation` added.
  - `[low]` `[patch]` Gap (other): a later held delivery journaled the window opening computed at the first hold — it now journals the next opening of the window as it is; `ALaterHeldDeliveryJournalsTheWindowOpeningOfTheWindowAsItIsNow` added.
  - `[false]` `[reject]` Intent: nothing reaches a recipient because no channel is registered — the story ends at the Notifier seam; the channels are Stories 6.5 and 6.6.
  - `[false]` `[reject]` Intent: "one summary" is one per Site — the epic's summary title names one Site and a tap opens that Site's overview, so no other grouping fits.
  - `[low]` `[reject]` Intent: the ≤ 1 minute assertions read the fake clock — AD-6 has tests drive deadlines with the fake clock; real latency is the 5 s timer and the 1 minute reminder.
  - `[low]` `[reject]` Intent: no test lets the reminder service fire the wake-up reminder — the repo's convention (registration is asserted, the tick is called directly); the reminder runs on the real clock.
  - `[low]` `[reject]` Intent: the restart test activates the grain itself — the in-memory reminder table does not survive the test restart; the persisted state and the processing on activation are what the test pins. Named under residual risks.
  - `[false]` `[reject]` Intent: the next Reminder due-at is derived, not stored — it is the journaled previous due-at plus the journaled cadence; nothing is lost in a restart.
  - `[false]` `[reject]` Intent: reconciliation runs on the first wake after activation, not inside it — the intent requires that a failing pull never fails the activation.
  - `[false]` `[reject]` Intent: the Health floor is a rule only — no Health kind exists before Epic 7, which the intent says.
  - `[low]` `[reject]` Intent: the metric is a delay histogram and a counter, not the two instants — a metric cannot carry timestamps; both instants are in the log entry.
  - `[low]` `[reject]` Intent: the Alert grain calls User grains and `alert.site-notified` now needs every member — as the first Blind row.
  - `[low]` `[reject]` Intent: mute discards, a pulled Alert gets no opening notification, no zone means UTC — decisions the story is silent on, stated in the intent contract and named under residual risks.
  - `[false]` `[reject]` Intent: test coverage of the test criterion — every listed item has a named test; no defect claimed.

## Design Notes

- **Why the Alert grain fans out.** "Published per Site" exists as the Alert grain's journaled, retried call to the Site grain. The Site grain may not call User grains (a User grain calls its Site grains, so the reverse would allow a call cycle), and Story 6.3 set the pattern: the grain answers with its members and the caller fans out. Extending the report loop reuses its retry and restart behaviour; a member added between answer and fan-out is covered by the pull on join.
- **One summary per Site.** The summary title names the Site and a tap opens that Site's overview, so held deliveries group per Site.
- **No zone means UTC.** Clients send a detected zone at session start (6.3), so a User without one is rare; UTC is deterministic and keeps the window in force.
- **Text stays out.** The notification carries identifiers and the Alert facts the grains hold. Lot and Site names, values and wording belong to the channels (6.5, 6.6).
- **Reminders anchor to the due-at, not the send time**, so a held Reminder does not drift to the window opening for good.

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror && dotnet format --verify-no-changes --no-restore` -- expected: success, no changes
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- expected: pass (known host flakes: `KeycloakReconciliationTests.ARenameInKeycloakRenamesTheSite`, three `NodeMoveTests` reminder timeouts)

## Auto Run Result

Status: done

**Summary:** The User grain now decides when each User is notified. It is told of every Alert that opens or closes on its Sites (the Site grain answers with its members and the Alert grain tells them), pulls a Site's open Alerts when it joins, and reconciles after every activation. A delivery due inside the Notification Window goes to the new Notifier seam at once; one due outside it is held for one summary per Site when the window opens, in the User's time zone and correct across daylight-saving changes. Reminders follow the resolved cadence, a muted Site is silent, and every deadline is journaled, with an Orleans reminder and a grain timer only waking the grain. The Notifier records `dueAt` and `sentAt` in a log entry and on a Coldframe meter. No channel ships: until Stories 6.5 and 6.6 nothing reaches a phone or a browser.

**Files changed:**
- `packages/cs/contracts/Sites/{SiteGrains,UserEvents}.cs`, `packages/cs/contracts/Notifications/Notifications.cs` -- `IUserGrain.AlertOpened`/`AlertClosed`, member-returning Site answers, the notification model, five `user.*` events.
- `apps/cs/server/Identity/{UserGrain,UserState}.cs` -- Alert tracking, pull and reconcile, due processing, summaries, Reminders, mute, the wake-up reminder and timer.
- `apps/cs/server/Identity/SiteGrain.cs`, `apps/cs/server/Alerts/AlertGrain.cs` -- members in the answer, member fan-out inside the report loop.
- `apps/cs/server/Notifications/{INotifier,Notifier,INotificationChannel,NotificationWindowRule,ReminderIntervalRule,NotificationsHostingExtensions}.cs`, `apps/cs/server/Program.cs`, `apps/cs/server/Hosting/ServiceDefaultsExtensions.cs` -- the seam, the pure rules, the meter and its export.
- `tests/cs/server.tests/**` -- rule, Notifier, telemetry and state tests, five fixture rows.
- `tests/cs/server.integration/**` -- `DeliveryTimingGrainTests` (23 tests), a real-Sensor test in `ThresholdAlertGrainTests`, the recording channel, fault filters, three stream assertions that gained `user.site-alerts-pulled`.
- `apps/cs/README.md`, `packages/cs/README.md` -- delivery timing, the seam, how to add a channel.

**Review findings:** 44 findings from four layers. Patched 9 rows (8 entries: 3 medium, 5 low). Deferred 0. Rejected 35: 27 low and 8 false; each reason is in the Review Triage Log. The rejected low findings are mostly outage and race cases (a member whose User grain cannot be told, a reconcile inside the tell gap, retries without back-off) and daylight-saving corner cases of one night a year.

**Follow-up review recommendation:** true. Patched by verdict: high 0, medium 3, low 5. The unverified risk: the Notifier's failure rule changed after the review (a send now counts as sent when at least one channel delivered), and no review layer has read that rule or the four tests added with it.

**Verification:**
- `dotnet restore --locked-mode`, `dotnet build --no-restore -warnaserror`, `dotnet format --verify-no-changes --no-restore` -- pass, after the review patches.
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --no-build` -- 1200 passed, 0 failed, 0 skipped (1195 before the review patches).
- Every row of the I/O matrix has a covering test that ran and passed.

**Residual risks:**
- A member whose User grain cannot journal keeps an Alert's report pending: the Sensor then does not deliver the close, the other members keep being reminded, and the Node's frames are answered `retry` until that grain answers. This is the same coupling the Site grain already had, extended to every member.
- A channel that fails while another delivers is not handed the notification again; Stories 6.5 and 6.6 must retry inside their own channel if they need to.
- Test-first (NFR16) was not followed literally: implementation and tests were written in the same pass.
- The path where the persisted Orleans reminder alone re-activates the grain after a restart is not exercised; tests call the tick or activate the grain.
- Decisions the story is silent on: a muted delivery is discarded, an Alert learned by pull gets no opening notification, and a User without a time zone has a window in UTC.
- The notification carries no Lot or Site name and no value; the channels must look them up (Story 6.5).
