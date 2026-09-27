---
name: Coldframe
status: final
created: 2026-09-27
updated: 2026-09-27
sources:
  - ../../../specs/spec-coldframe/
  - ../../architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md
---

# Coldframe — Experience Spine

## Foundation

Multi-surface: iOS app (SwiftUI), Android app (Jetpack Compose, Material 3 structure), and a SvelteKit web app (BFF, AD-14). iOS and Android share one KMP core (`packages/kt/core`) for BLE setup, OIDC, API client and push-token registration; the native shells hold UI only. Each platform uses its native navigation, gestures, controls and system integrations; the Escendit look is a brand layer over them. `DESIGN.md` is the visual reference; this spine is the behaviour.

Clients only render. Lot status, its sort order and `statusSince` come from the Server (AD-14). Client-side logic is limited to transport staleness: data age and Server unreachable. The web app offers everything the mobile apps do except BLE (Hub provisioning, Node identification). All three clients work only on the home network in V1; push notifications arrive anywhere.

Visual references: [claude-design-B-plots.html](imports/claude-design-B-plots.html) and captures [1](imports/claude-design-B-plots-1.png)–[7](imports/claude-design-B-plots-7.png); A and C directions are rejected and reference only (linked in `DESIGN.md`). The Claude Design brief is [.working/claude-design-prompt.md](.working/claude-design-prompt.md). **Spines win on conflict with any mock.**

## Information Architecture

| Surface | Platforms | Reached from | Purpose | Journey |
|---|---|---|---|---|
| Sign in | all | Cold start, signed out | Server address field (mobile), hand-off to Keycloak; the address is a real domain with a public certificate (AD-13), not B's `coldframe.home.arpa` | UJ-1 |
| Create Site | all | First sign-in with no Membership; Site switcher "New Site" | Name the Site, confirm time zone; creator becomes Owner | UJ-1 |
| Site overview (Garden) | all | Tab 1 / nav "Garden"; app open | Summary sentence + Lot tiles in Server sort order | UJ-2, UJ-3, UJ-4 |
| Lot detail | all | Lot tile; Threshold push | Latest Reading per Sensor, 30-day chart, Node battery/charging/last seen, admin actions | UJ-2 |
| Alerts | all | Tab 2 / nav "Alerts"; Health push; web Open Alerts rail | Open Alerts (Threshold, Health) and recently closed | UJ-3 |
| Devices | all | Tab 3 / nav "Devices" | Hub(s) and Nodes: last seen, battery, charging, Silence Window, Pause, move/unassign Node; entry to Add a Hub / Add a Node | UJ-1, UJ-3, UJ-6 |
| Add a Hub | iOS, Android | First-run step 1; Devices "Add a Hub" | 5-step BLE provisioning (AD-25) | UJ-1 |
| Add a Node | iOS, Android | First-run step 2; no-Node tile; Devices "Add a Node"; "Hub is online" | 5-step BLE identification + Lot assignment | UJ-1 |
| Calibrate | all (uses stored Readings, AD-9; no BLE) | Needs-calibration tile; Lot detail; Node-added screen; Uncalibrated push | Two-point dry/wet Calibration | UJ-1 |
| Thresholds | all | Lot detail; after Calibration | Low (required) / high (optional) per Sensor | UJ-1 |
| Pause | all | Lot detail "Pause" (Device); Devices row; overview Site menu (Site) | Pause/resume a Device or the Site, optional end date | UJ-4 |
| Settings | all | Tab 4 / nav footer | Index: My notifications, Members, Site settings, Appearance, Account | UJ-1, UJ-5 |
| My notifications | all | Settings | Notification Window, time zone, mute this Site, my Reminder cadence, (web) browser notifications | UJ-1, UJ-5 |
| Members | all | Settings; web nav "Members" | Invite by email with Role, change Role, remove Membership | UJ-5, UJ-6 |
| Site settings | all | Settings | Rename Site (Owner), Site Reminder cadence (Owner/Admin), Lots: create/rename/remove (Admin) `[ASSUMPTION]` one surface for these | UJ-6 |
| Appearance | all | Settings | Theme: System / Light / Dark | UJ-6 |
| Site switcher | all | Site name in header (mobile) / Site tabs in AppHeader (web) | Switch Site; create a new Site | UJ-5, UJ-6 |
| Lock-screen push | iOS, Android | OS | Single Alert, Reminder, morning summary | UJ-2, UJ-3, UJ-5 |
| Browser notification | web | OS, while a tab is open | Same content as push | UJ-6 |

Navigation: mobile bottom tabs **Garden · Alerts · Devices · Settings** (Alerts tab label carries the open count, "Alerts · 5"). Web: DS AppShell — header with Coldframe mark, Site tabs, user initials; side nav **Garden · Alerts · Devices · Members**, Settings in the nav footer. Setup flows (Add a Hub, Add a Node, Calibrate) are full-screen modal flows with Cancel/Back top-left; Thresholds and Pause are modal screens with Cancel/Save. One modal level at a time. The Site menu (overview header) holds Site Pause/Resume and Site settings.

Invitations use Phase Two's native flow (AD-3): the invitee accepts on the Keycloak/Phase Two page opened from the email link. Coldframe has no accept surface and shows no pending invitations; the new Membership appears in Members, and the invitee's app opens on that Site.

Role gating: Member sees every read surface; admin actions (Thresholds, Calibrate, Pause, Add/move Devices, Silence Window, Lots) show for Administrator and Owner; Members management and Site rename show for Owner only.

Closure: every surface is reached by a journey. UJ-6 covers Devices, Site settings, Site switcher, Appearance and browser notifications (CAP-2/6/8/12/13/20 and the theme decision).

## Voice and Tone

Microcopy. Brand voice lives in `DESIGN.md` Brand & Style. Glossary terms verbatim and capitalised (Site, Lot, Node, Hub, Sensor, Reading, Threshold, Alert, Reminder, Notification Window, Silence Window, Pause, Calibration).

| Do | Don't |
|---|---|
| "Tomatoes needs water" | "⚠ Warning: low moisture detected!" |
| "~20 % in the soil, your low is 30 %." | "Soil moisture: 21.7 %" |
| "Node on Lot 'Beans' silent for 6 h" | "Beans: no data" / showing Beans as OK |
| "2 h 12 min old. Last data 07:02. You may be away from home, or the Server is down. Nothing below is live." | "Network error" |
| "Wrong Wi-Fi password. Hub 3F2A reached Novak-Home but was refused. Nothing else changed." | "Provisioning failed (code 7)" |
| "No Readings yet. Nothing is measuring, so there's no status to show." | "All good!" on an empty Site |
| "Member sees everything and gets the same Alerts, but can't change settings." | "Access denied" |

Rules:

- Calm and literal; state the condition, the Lot/Device, and the time. No exclamation marks, no emoji, no encouragement, no "successfully".
- Silence never looks fine: never write "OK", "fine" or "all good" for a Lot that is unknown, needs calibration, paused, has no Node, or is stale.
- Approximate soil moisture: `~` prefix and rounded to the nearest 5 % everywhere (tiles, detail, Alerts, push). Other Sensors: temperature whole °C, humidity whole %RH, air (gas) in kΩ with 3 significant digits `[ASSUMPTION]`. Uncalibrated soil moisture shows `raw` and the raw value, never a %.
- Durations: "min" under 1 h, "h" under 24 h ("h min" in the stale header), "d" from 24 h. Clock times in the user's locale format; earlier than today → weekday; older than 7 days → date.
- Headline sentence by case: any needs water → "2 Lots need water" / "Tomatoes needs water"; none but some unknown or needs calibration → "2 Lots can't be read" `[ASSUMPTION]`; all OK → "Nothing needs water"; Site paused → "Paused until 1 Mar" / "Paused"; no Nodes → "No Readings yet".
- Button labels are verbs naming the result ("Put 7C19 in Potatoes", "Pause Home garden", "Record dry").
- Errors say what happened, what did not change, and the next action.
- "Silence Window" means Device silence only; the time outside a Notification Window is "outside your window".

## Component Patterns

Behavioural. Visual specs live in `DESIGN.md` Components.

| Component | Use | Behavioral rules |
|---|---|---|
| Focus indicator | Every focusable element (web; iPadOS and Android with a hardware keyboard) | Two-tone ring per `DESIGN.md`; never the only change on focus-visible; never hidden under sticky header or tab bar. |
| Lot tile | Site overview | Order is the Server's sort (needsWater, needsCalibration, unknown, ok, paused, noNode); client never re-sorts or recomputes status. Label variants come from Server fields only: `unknownCause` (node → "SILENT", hub → "HUB SILENT"), `pausedBy` (includes site → "PAUSED BY SITE"). Tap → Lot detail; tap on no-Node tile → Add a Node (mobile, Admin+) or Lot detail with "Add a Node from the mobile app" (web / Member). One accessibility element per tile (children merged) with role button/link. Updates on `readmodel.changed` refetch without reflow animation. Skeleton tiles are not focusable. |
| Site summary header | Site overview | Headline per Voice rules, exposed as a heading; counts subline; Site clock = time of the newest Reading. Tap Site name (mobile) → Site switcher. |
| Site menu | Site overview | Items: Pause / Resume Home garden (Admin+; hidden for Members), Site settings. Pause opens the Pause sheet with scope Whole Site preselected. Disabled with "Needs your Server" in stale mode. |
| Site switcher | Site overview header (mobile), AppHeader Site tabs (web) | Lists every Site the user holds a Membership on, with Role; picking one switches the whole app to that Site; "New Site" → Create Site. With a single Site it still shows "New Site". Current Site exposed as selected. |
| Stale header | Site overview, Lot detail, Alerts | Replaces the summary header whenever the client is in stale mode (State Patterns). Age ticks every minute (never announced). |
| Alert row | Alerts, web rail | Open Alerts newest first within Threshold then Health groups; closed Alerts (last 7 days) in a "Closed" section. Needs-water treatment only for an open low-side soil-moisture Threshold Alert; other Threshold Alerts use the neutral Threshold treatment. Tap → Lot detail (Threshold, Uncalibrated) or Devices list row (Silent, Battery). No actions on Alerts (no mark-watered, no snooze). |
| Lot detail hero | Lot detail | Shows status + "since" (`statusSince`), value, low Threshold, "±5 % · <Reading time>". Paused by Site: "Paused with the Site" and, for Admin+, "Resume the Site to resume this Node". |
| Sensor cell | Lot detail | Latest Reading + unit; watched Sensors (no Thresholds) show value only; tap → Thresholds for that Sensor (Admin+). |
| Device cell | Lot detail, Devices | Node ID, battery %, charging / not charging, last seen, "every 15 min". Tap → Devices list row. |
| Devices list | Devices | Hubs first, then Nodes by Lot name. Row: ID, Lot, last seen, battery/charging (Nodes), status (silent, paused, paused by Site). Row actions (Admin+): Pause/Resume, Silence Window, move to another Lot, unassign; Resume on a Device paused by the Site says "Resume the Site to resume this Node". Header actions: Add a Hub, Add a Node (mobile only). Empty: "No Devices yet." plus the Add actions (Admin+). |
| History chart | Lot detail | 30 days, one bar per day = daily low of soil moisture; low/high Threshold lines; tap/drag a bar shows that day's low and date `[ASSUMPTION]`. A Sensor picker switches to temperature, humidity or air (gas) history `[ASSUMPTION]` (daily min/max). Days without Readings are gaps, never zero. Text summary as its accessibility label. |
| Button | Everywhere | Label names the result; while working shows a progress label ("Saving…") in place; never a spinner over content. |
| Text input | Forms | Label above, helper below; invalid reason replaces the helper and is linked to the field for screen readers. |
| Segmented choice | Reminder cadence, Theme switcher, Invite form, Pause sheet | Single select; immediate for settings, committed by the primary button inside modal flows. Selected segment exposed as selected. |
| Setup flow shell | Add a Hub, Add a Node, Calibrate | Step counter per flow: Add a Hub "NN / 05", Add a Node "NN / 05", Calibrate "NN / 02". Cancel on step 1, Back afterwards; leaving mid-flow asks "Stop setting up Hub 3F2A? Nothing is saved on the Hub." On step change, accessibility focus moves to the step title. Keeps the screen awake during BLE steps `[ASSUMPTION]`. |
| Device candidate tile | Add a Hub / Add a Node steps 1–2 | Only Devices in BLE range, strongest first. Provisioned Hubs stop advertising (AD-25), so there is no "already set up" tile. Hub: ID from its label + signal. Node: ID, battery %, Sensor count, "PRESSED JUST NOW" for the most recent setup-mode advertiser. Tap selects; list keeps scanning ("Still scanning…"). |
| Setup code field | Add a Hub / Add a Node steps 2–3 | Format-agnostic text entry, auto-uppercase, no autocorrect; validated by opening the BLE session (AD-25); "accepted" chip on success. |
| Wi-Fi network row | Add a Hub step 3 | Networks as seen by the Hub; WPA3-only rows are not selectable and say why; "Other network" for hidden SSIDs `[ASSUMPTION]`. Password field with reveal. |
| Lot picker | Add a Node step 4 | Lots without a Node selectable; Lots with a Node disabled "HAS A NODE"; "+ New Lot" creates inline. Primary button names the result. |
| Setup progress | Add a Hub step 5 | Four segments advance on real events (BLE, Wi-Fi sent, joined, Server sees Hub); each segment shows its state by icon and text, and the whole is one progress element ("Step 3 of 4, joining Wi-Fi"). Elapsed time shown, "Usually under a minute." Times out at 90 s `[ASSUMPTION]` into an error screen. |
| Calibration reference point | Calibrate | Uses **stored Readings** (AD-9); works on web and mobile, no BLE. Each step (dry, then wet) waits for the next Reading taken after the step started: "Waiting for the next Reading" with the last raw value and its time, and the hint to short-press the Node's setup button, which makes it report within seconds (Node firmware requirement N-3, see device-hardware.md); otherwise the next scheduled Reading arrives within 15 min. "Record dry" / "Record wet" enables when a fresh Reading arrives; the Administrator may instead pick a stored Reading from "Recent Readings". The flow can be left and resumed; the chosen points are kept until both exist. Confirmation shows dry/wet raw values and "% appears with the next Reading" (press the setup button again to report now); it updates in place to the first % ("Tomatoes reads ~40 %") when that Reading arrives. No % is shown before the Server has a Reading stored with the new Calibration. Paused Device (by itself or the Site): Calibrate does not start the wait and says "7C19 is paused, so it sends no Readings. Calibrate after the Pause ends." with Resume (Admin+) where the Pause is the Device's own. |
| Threshold column | Thresholds | Drag or type; 5 % steps for calibrated soil moisture `[ASSUMPTION]`; low required, high optional ("Add high" / clear). Inline validation "Low must stay below high." Save disabled while invalid. Proposes low = Min + 20 % × range when enabling alerts on a Sensor without a default (CAP-10). |
| Pause sheet | Pause | Scope: This Device / Whole Site; optional "Until" date (DS DatePicker / native picker); "Leave empty to pause until someone resumes." Resume is one tap from the paused tile's detail, Devices list row or Site menu. |
| Notification Window control | My notifications | From/to time pickers; "from 07:00" alone is valid (default to 22:00); 24 h bar previews it (decorative, hidden from screen readers); helper "Outside this window, anything waits for one summary at 07:00." |
| Time-zone confirm panel | Create Site, My notifications | Detected zone proposed (OS / browser); Confirm or Change (searchable IANA list). Never overwrites a zone the user picked. |
| Mute toggle | My notifications | Native switch "Mute Home garden"; affects only this user. |
| Reminder cadence | My notifications (mine), Site settings (Site) | Mine: "Use Site setting" / "Daily" / "Every 2 days"; Site: "Daily" / "Every 2 days" (default daily). No "Never" — closing the Alert or muting the Site stops Reminders. |
| Member tile | Members | One tile per Membership (no pending invitations; AD-3). Tap → Role change / remove (Owner). Last Owner locked with explanation. |
| Invite form | Members | Email + Role segmented; one-line Role description updates with selection; "Send invite" → Inline notice "Invitation sent to ana@example.com. They accept from the email, on the garden's Wi-Fi." The form clears; nothing is added to the Members list until the invitation is accepted. |
| Theme switcher | Appearance | System (default) / Light / Dark; applies instantly; persisted per device. |
| First-run step tiles | Site overview, empty Site | Shown while the Site has no Node with Readings. Next step tap starts that flow (mobile, Admin+); done steps show a checkmark and stay until the grid replaces the tiles. Web and Members see the tiles without actions plus the BLE or read-only Inline notice. |
| Outcome screens | End of Add a Hub, Add a Node, Calibrate; setup errors | Full screen; accessibility focus moves to the headline; one primary next action. Errors never auto-dismiss. Paused-Site note on success screens (State Patterns). |
| Inline notice | Read-only, Hub silent, web BLE, notifications off, paused-Site Device, sign-in errors | Non-dismissable explanation where a state can't be changed here; may carry one action (Open Settings, Resume Site). |
| Navigation | App shell | Mobile tabs Garden · Alerts · Devices · Settings; web side nav Garden · Alerts · Devices · Members + Settings. Alerts carries the open count, announced as "Alerts, 5 open". Current item exposed as selected / `aria-current`. |
| Sign-in surface | Sign in | Mobile: Server address field (remembered after first success), SIGN IN checks the address, then hands off to Keycloak in the system browser session and returns. Web: SIGN IN only (served by the Server). Errors per State Patterns › Sign-in and session; the address is kept on every error. |
| Push notification | Lock screen, notification shade, browser | Content and tap targets per Notifications. |

## State Patterns

### Lot status (Server-computed, AD-14)

| Status | Tile | Lot detail | Screen-reader label |
|---|---|---|---|
| needsWater | Solid orange, `~20`, "07:02 · low 30 %" | Orange hero "NEEDS WATER · SINCE 05:45" | "Tomatoes, needs water, about 20 percent, low 30 percent, Reading 07:02" |
| needsCalibration | Hatch + yellow dash, `raw`, "no % until calibrated" | Hero "NEEDS CALIBRATION", Calibrate primary (Admin+) | "Peppers, needs calibration, no percentage until calibrated" |
| unknown | Hatch + dash, silence duration `6 h`, "was ~40 % at 01:05"; label "SILENT · UNKNOWN" (`unknownCause` node) or "HUB SILENT · UNKNOWN" (`unknownCause` hub) | Hero shows duration and last Reading; node: "Check power or range."; hub: "Hub 3F2A is silent; Lots behind it can't be read." | "Beans, unknown, Node silent for 6 hours, last about 40 percent at 01:05" / "Beans, unknown, Hub silent for 12 minutes, last about 40 percent at 07:02" |
| ok | Neutral with soil fill, `~35`, "07:03 · low 25 %"; no secondary condition in the label (battery and Health conditions show on Lot detail and Devices) | Neutral hero; Device cell shows battery | "Herbs, OK, about 35 percent, low 25 percent" |
| paused | Purple outline, `—`, "until 1 Nov"; label "PAUSED", or "PAUSED BY SITE" when `pausedBy` includes site | Hero "PAUSED UNTIL 1 NOV", Resume (Admin+); paused by Site: "Paused with the Site", Resume the Site from the Site menu | "Strawberries, paused until 1 November" / "Strawberries, paused with the Site" |
| noNode | Dotted empty, `+`, "add a Node" | Empty detail with Add a Node (mobile, Admin+) | "Potatoes, no Node, add a Node" |

### Transport and data states

| State | Surface | Treatment |
|---|---|---|
| Server unreachable | Site overview, Lot detail, Alerts, Devices | Stale mode after the first failed refresh plus one retry `[ASSUMPTION]`: stale header with data age, every tile `lot-tile-stale` ("WAS NEEDS WATER", "as of 07:02"), admin actions disabled with "Needs your Server". Retries in background; leaves stale mode on the first successful refresh. |
| Away from home (opened from push) | Any | Same as unreachable; the push itself was self-contained. |
| Cold start | Site overview | Last cached data shown in stale mode until the first refresh lands `[ASSUMPTION]`; no cache → outline-only skeleton tiles with no status or value, "Loading Home garden". |
| Hub silent | Site overview | Inline notice above tiles "Hub 3F2A silent for 12 min — Lots behind it can't be read." Affected Lots are unknown via the Server. |
| Empty Site (no Hub/Node) | Site overview | "No Readings yet" + first-run step tiles (Add a Hub → Add a Node → Calibrate → Set a low Threshold); Site name field shown before this only on Create Site. |
| No open Alerts | Alerts | "No open Alerts." then the Closed section if any. Never "All good". |
| All paused | Site overview | Headline "Paused until 1 Mar" in paused ink; line "No Alerts are sent while the Site is paused. Paused by Simon (Owner) on 2 Nov."; all tiles paused. |
| Member read-only | All admin surfaces | Admin controls hidden (not disabled); where a state can't be changed, one inline notice: "Only Owners and Administrators can resume it." A 403 race shows "You can't change this on Home garden. Ask an Owner or Administrator." |
| Web, BLE action | No-Node tile, Devices | Inline notice "Adding a Hub or Node needs the Coldframe mobile app." |
| Loading (action) | Buttons in flows | Button shows progress label ("Saving…"), stays in place; no spinners over content. |
| Device added to a paused Site | Hub-online and Node-added outcome screens, Devices, Lot | The new Device starts paused (`pausedBy` site, AD-8). Outcome screen adds an Inline notice: "Home garden is paused, so 7C19 starts paused. It won't report or alert until the Site is resumed." with Resume Site (Admin+) and "Calibrate later"; its tile and row read "PAUSED BY SITE". |
| Notifications off (mobile OS permission denied or revoked) | My notifications, Site overview | My notifications: persistent Inline notice "Notifications are off for Coldframe on this phone. You won't get Alerts." → Open Settings (deep link to the app's OS notification settings). Overview: the same one-line notice above the tiles on every visit until permission is granted; not dismissable. Rechecked on every app foreground. |
| Notifications blocked (web) | My notifications | Browser notifications toggle on but the browser blocks the site: toggle returns to off with "Your browser blocks notifications for Coldframe. Allow them in this site's browser settings, then turn this on again." (browsers offer no deep link). |
| Invitation accepted, Membership not arrived yet | Create Site (first sign-in) | Membership events arrive asynchronously (AD-3). Create Site shows "Accepted an invitation? It can take a minute to appear." and refreshes Memberships; the Site opens as soon as it arrives. |
| Validation error | Forms | DS invalid field + one-line reason under it; e.g. "Low must stay below high.", "Move or unassign the Node on Potatoes first.", "A Site always keeps at least one Owner." |

### Sign-in and session

| Case | Surface | Copy / recovery |
|---|---|---|
| Address malformed (mobile) | Sign in, under the field | "Enter your Server's address, like coldframe.example.com." |
| Address unreachable (DNS failure, timeout, refused) — includes being off the home network, which the app cannot tell apart | Sign in, Inline notice | "Can't reach coldframe.novak.ch. Check the address, and that this phone is on your home Wi-Fi." → Try again / Edit address |
| Address answers but is not a Coldframe Server | Sign in, Inline notice | "coldframe.novak.ch answered, but it isn't a Coldframe Server. Check the address." → Edit address |
| Certificate / TLS failure | Sign in, Inline notice | "coldframe.novak.ch's certificate isn't trusted, so Coldframe won't connect. The Server needs a valid certificate for its domain." No "continue anyway" (AD-13). |
| Keycloak cancelled by the user | Sign in | Returns to Sign in with the address kept; no error. |
| Keycloak error or unreachable during sign-in | Sign in, Inline notice | "Sign-in didn't finish: your Server's sign-in page returned an error. Nothing was changed." → Try again |
| Session expired or revoked later | Any | Cached data stays in stale mode; Inline notice "You're signed out. Sign in again to see live data." → Sign in (address prefilled). |
| Web off the home network | Browser | The page does not load (browser's own error); no Coldframe copy is possible. |

### BLE setup errors (mobile)

| Error | Screen | Copy / recovery |
|---|---|---|
| Bluetooth off / permission denied | Step 1 | "Coldframe needs Bluetooth to find the Hub." → Open Settings |
| No Device found (30 s) | Step 1 | "No Hub in range yet. Power it on within a few metres; its LED blinks orange while it waits." → keeps scanning |
| Node setup mode timed out | Step 2 | "7C19 stopped listening. Press its setup button again." |
| Wrong setup code | Step 2/3 | "That setup code doesn't match Hub 3F2A. Check its label or the serial console." → field stays for re-entry |
| WPA3-only network | Step 3 | Row disabled: "Not supported: the Hub needs WPA2 or mixed WPA2/WPA3." |
| Wrong Wi-Fi password | Step 5 → error screen | "Wrong Wi-Fi password" → Re-enter password / Other network |
| Hub joined Wi-Fi, Server not reached | Step 5 → error screen | "Hub 3F2A is on Novak-Home but can't reach your Server." → Try again / Help `[ASSUMPTION]` copy |
| BLE connection lost | Any step | "Lost the connection to Hub 3F2A. Nothing was saved." → Try again |
| Lot already has a Node | Node step 4 | Lot tile disabled "HAS A NODE" |

## Interaction Primitives

- Tap to act. No long-press actions; no hover-only affordances.
- Pull-to-refresh on Site overview, Alerts, Devices (mobile); refetch on focus (web). Live updates arrive through SignalR invalidation hints; data is refetched, never pushed as domain data.
- Native back: iOS edge swipe, Android system/predictive back, browser back. Modal flows ask before discarding.
- Swipe on Device rows reveals Pause (Admin+) `[ASSUMPTION]`.
- Destructive actions (remove Lot, remove Membership, unassign Node) confirm in a native dialog / DS Modal naming the object.
- Web keyboard: full tab order, Enter/Space activates tiles, Esc closes modals.
- **Banned:** gamification, streaks, celebratory animation, Alert snooze or "mark watered", auto-dismissing error toasts, carousels, infinite spinners over stale data.

## Accessibility Floor

Behavioural. Contrast lives in `DESIGN.md` Colors.

- Text scales with Dynamic Type (iOS), font scale (Android), browser zoom to 200 % and 320 px reflow (web) with no truncated controls or clipped values. Tiles have a minimum square height and grow with their content; the Lot grid drops to 1 column at iOS Accessibility 1 and larger, Android font scale ≥ 1.5, or a web grid narrower than 400 CSS px (`DESIGN.md` Layout). Web sizes are `rem`-based.
- VoiceOver / TalkBack / web screen readers: every tile, Alert row, chart and control labelled with role and state (labels in State Patterns). Stale tiles append "not live, as of 07:02". Chart has a text summary ("30 days, lowest about 20 percent today, below the low Threshold on 2 days").
- Status never by colour alone: status word + unique shape + Carbon icon + value always present (`DESIGN.md` Shapes); all six statuses and stale stay distinct with colour and text removed.
- Selection never by colour alone: selected segments, tiles and rows carry a checkmark and expose selected state (`.isSelected` / `selected` semantics / `aria-pressed` or `aria-current`).
- Tap targets ≥ 44 pt (iOS) / 48 dp (Android) / 44 px (web).
- Reduce Motion: no progress-bar or chart animation; state changes swap instantly.
- Focus order follows reading order. The two-tone focus indicator (`{colors.focus}`, `DESIGN.md` Components) shows on web and wherever a hardware keyboard or switch control moves focus, and stays visible on orange and neutral surfaces alike.
- Setup flows never time out while a screen reader is reading; BLE timeouts announce their message.

### Announcements

iOS `AccessibilityNotification.Announcement` (polite) / `.screenChanged`; Android polite / assertive `liveRegion` semantics; web `role="status"` (polite) and `role="alert"` (assertive) regions that exist before they are filled.

| Event | Priority | Announcement |
|---|---|---|
| Setup progress segment advances | polite | "Wi-Fi sent." / "Joining Novak-Home." / "Server sees Hub 3F2A." |
| Setup success or step change | focus move | Accessibility focus moves to the headline or step title ("Hub is online"). |
| Setup error or timeout | assertive | Error headline + next action ("Wrong Wi-Fi password. Re-enter password."). |
| Calibration: fresh Reading arrives | polite | "New Reading 07:17, raw 612. Record dry is available." |
| Calibration: first % Reading after Calibration | polite | "Tomatoes reads about 40 percent." |
| Calibration waiting text | never | Static text; read on demand. |
| Entering stale mode | polite | "Can't reach your Server. Showing data from 07:02." |
| Leaving stale mode | polite | "Live again." |
| BLE scan list: new candidate | polite, once per Device | "Hub 3F2A found, strong signal." |
| BLE scan list: signal or order changes | never | — |
| Alert opens while the app is open | polite | "New Alert: Tomatoes needs water." |
| Alert closes while the app is open | never | List updates silently. |
| Hub-silent or notifications-off notice appears | polite | The notice text. |
| Save result / validation error on submit | polite / assertive | "Saved." / the field's reason. |
| Stale age tick, elapsed-seconds counter, refetch without change | never | — |

## Key Flows

### UJ-1. Simon sets up the garden (Saturday afternoon, phone in the shed)

1. On the home Wi-Fi Simon opens the app; Sign in shows the Server field; SIGN IN hands off to Keycloak and back.
2. No Membership yet → Create Site: he types "Home", confirms the detected time zone Europe/Zurich; he is Owner. Overview shows "No Readings yet" with first-run step tiles; on this first landing the app asks for notification permission with one line of why, and he allows it.
3. STEP 1 Add a Hub: scan finds 3F2A → setup code accepted → picks Novak-Home, enters password → Site "Home" → progress runs through BLUETOOTH · WI-FI SENT · JOINING · SERVER.
4. Green "Hub is online" screen within a minute; he taps ADD A NODE.
5. He long-presses the Node's setup button; 7C19 shows "PRESSED JUST NOW" → setup code → picks Lot "Tomatoes" (creating it via "+ New Lot") → "Put 7C19 in Tomatoes".
6. "Tomatoes has a Node": Sensors appear, soil moisture hatched "NEEDS CALIBRATION"; he taps CALIBRATE SOIL MOISTURE.
7. Dry step: probe in dry soil; he short-presses the Node's setup button to report now (N-3); the new Reading arrives within seconds; Record dry. Wet step: probe in a glass of water, same again, Record wet.
8. Confirmation: dry and wet raw values, "% appears with the next Reading". He puts the probe back in the bed and short-presses once more.
9. **Climax:** seconds later the confirmation updates to "Tomatoes reads ~40 %". Back on the overview the Tomatoes tile is neutral with a soil fill at ~40 — the first honest status the garden has ever had.
10. Settings → My notifications: he sets the Notification Window "from 07:00".

Failure: wrong setup code or Wi-Fi password → error screens per State Patterns; nothing on the Hub changes and Simon retries the one step. Declined notification permission → the "Notifications are off" notice on the overview and in My notifications.

### UJ-2. A hot week, a morning nudge (Simon, Tuesday, before work)

1. 02:40 Tomatoes drops below its low Threshold; the Alert opens ~30 min later and is held (outside the window).
2. 07:00 the lock screen shows "Tomatoes needs water — ~20 % in the soil, your low is 30 %." (or the morning summary if more is held).
3. He taps it; the app opens on Tomatoes Lot detail: orange hero "NEEDS WATER · SINCE 03:10", chart with today's bar in orange.
4. He waters before work.
5. **Climax:** by mid-morning the Tomatoes tile is neutral again, the Alert sits under "Closed", and no Reminder arrives — the app went quiet because the bed is fine, not because it stopped looking.

Edge: nobody waters → next morning's summary carries one entry "Tomatoes needs water (~15 %)", not one per Reminder.

### UJ-3. A Node dies quietly (Simon, Wednesday morning coffee)

1. Overnight the Node on Beans stops reporting; after 6 h the Silent Alert opens and is held.
2. 07:00 the summary includes "Beans: Node silent for 6 h".
3. **Climax:** on the overview the Beans tile is hatched with a big `6 h` and "was ~40 % at 01:05" — unmistakably not fine, and sorted above every OK Lot.
4. He taps through to Alerts → the Health Alert → Devices row for the Node: last seen 01:05, battery 31 %.

Variant: the Hub goes silent → one Alert "Hub 3F2A silent for 5 min" and a Site notice; Lots behind it turn unknown without one Alert per Node.

### UJ-4. Off for the winter (Simon, October)

1. Overview → Site menu → Pause; scope Whole Site; "Until" left empty (or 1 Mar).
2. PAUSE HOME GARDEN.
3. **Climax:** every tile turns to a purple outline reading "PAUSED"; headline "Paused until 1 Mar"; no Health Alerts arrive while Nodes sit indoors.
4. In spring, Resume from the same menu; Silence Windows restart from that moment.

### UJ-5. A second pair of eyes (Simon and the neighbour, before a holiday)

1. Settings → Members: Simon enters the neighbour's email, picks Member (description: "Member sees everything and gets the same Alerts, but can't change settings."), SEND INVITE. "Invitation sent to …" appears; the Members list is unchanged.
2. The neighbour opens the email link on Simon's Wi-Fi and accepts on the Keycloak/Phase Two page (signing in or registering there, AD-3). The neighbour now appears in Simon's Members list; the neighbour's app signs in to the same Server and opens on Home's overview.
3. On first landing on the overview, the neighbour's phone asks for notification permission; they allow it.
4. The neighbour sets their own Notification Window in My notifications.
5. **Climax:** a week later the neighbour's phone shows "Cucumbers needs water" in their own window; opening Cucumbers shows the same tiles and chart as Simon's, with no Thresholds, Calibrate or Pause controls anywhere.

Failure: the neighbour opens the link away from home → the Keycloak page does not load (no Coldframe copy is possible); the Invite form's confirmation already says to accept on the garden's Wi-Fi. If the app opens before the Membership arrives → Create Site shows the "Accepted an invitation?" state (State Patterns).

### UJ-6. Tidying up after the first month (Simon, Sunday evening at the laptop)

1. On the web app Simon opens Settings → Site settings: renames "Home" to "Home garden", adds Lots "Beans" and "Peppers", removes the unused Lot "Herbs" (it has no Node), and sets the Site Reminder cadence.
2. Devices: he moves the Node from "Tomatoes" to "Peppers" (Tomatoes now shows "no Node"; history stays with the Node) and lengthens the Beans Node's Silence Window to 8 h because it sits at the garden's edge.
3. Members: he changes the neighbour's Role from Member to Administrator for the summer, then back after the holiday.
4. Appearance: he switches the web app to Dark while the phone keeps following the system.
5. He switches Site in the header to his parents' allotment, where he is a Member, and sees only read surfaces there.
6. **Climax:** with the tab left open, a browser notification "Peppers needs water" arrives inside his Notification Window — the same Alert his phone just showed, not a second one per device.

## Notifications

- Channels: APNs/FCM push (mobile), browser notifications via SignalR `notification.delivered` while a web tab is open (no background web push). All timing, Notification Window holding, mute and Reminder logic is Server-side (AD-7); clients display what arrives.
- Payloads are self-contained and readable without the Server. Title = condition, body = value and context:

| Kind | Title | Body |
|---|---|---|
| Threshold low (soil) | Tomatoes needs water | ~20 % in the soil, your low is 30 %. |
| Threshold high | Herbs too wet | ~65 % in the soil, your high is 60 %. |
| Threshold (other Sensor) | Tomatoes temperature below 5 °C `[ASSUMPTION]` | Reading 4 °C at 05:15. |
| Silent Node | Node on Lot 'Beans' silent for 6 h | No Reading since 01:05. Check power or range. |
| Silent Hub | Hub 3F2A silent for 5 min `[ASSUMPTION]` | Lots behind it can't be read. |
| Low battery | Node battery 14 %, not charging | Lot 'Lettuce'. |
| Uncalibrated | Soil sensor on Lot 'Peppers' needs calibration | An Owner or Administrator can calibrate it. |
| Morning summary | Home garden: 2 need water, 3 to check | One line per open Alert, needs-water first; footer "Held overnight, 22:00–07:00". |

- Reminders reuse the Alert's text with "still" in the body ("Still ~20 % in the soil…") `[ASSUMPTION]`.
- Closing an Alert sends nothing.
- Tap: Threshold/Uncalibrated → Lot detail; Silent/Battery → Devices row; summary → Site overview.
- Grouped per Site (iOS thread identifier, Android group); standard interruption level, never critical/time-sensitive; no app-icon badge count.
- Permission: mobile asks when the user first lands on a Site overview (the creator right after Create Site, or a newly accepted Member), with one line of why ("Coldframe tells you when a Lot needs water."). Denied or revoked → State Patterns › Notifications off. Web asks only when the user turns on "Browser notifications while Coldframe is open" in My notifications `[ASSUMPTION]`.
- Counter-metric: at most ~1 notification per Lot per user per day under default settings; the UI never adds notifications of its own (no recovery pings, no nudges).

## Internationalization

- English only in V1; i18n-ready from the first commit.
- Every user-facing string externalized (iOS String Catalog, Android/KMP string resources, web message catalog) `[ASSUMPTION]` tooling; no copy in code or images; uppercase via style only.
- Plurals via CLDR rules ("1 Lot needs water" / "2 Lots need water").
- Layouts tolerate 30–40 % longer text: below accessibility text sizes, tile names wrap to two lines, then truncate (full name on Lot detail and in the accessibility label); at accessibility sizes nothing truncates and the tile grows; buttons grow in height, never clip.
- Dates, times (12/24 h), numbers, percent spacing and units formatted by locale; durations via locale-aware formatters.
- Glossary terms remain proper nouns in every locale's source strings; translators get the glossary.
- Device IDs, setup codes, SSIDs and Server URLs are never translated or reformatted.

## Responsive & Platform

| Surface | Behaviour |
|---|---|
| Phone (iOS, Android) | 2-column Lot grid, 1 column at accessibility text sizes; vertical scroll; tab bar; full-screen setup flows. |
| Web < 400 px grid width (incl. 320 px reflow, 200 % zoom) | 1 column; side nav behind a header menu. |
| Web < 672 px | 2 columns; side nav collapses behind a header menu (DS AppShell). |
| Web 672–1055 px | 3 columns; side nav rail. |
| Web ≥ 1056 px | 4 columns; side nav. |
| Web ≥ 1312 px | 4 columns + Open Alerts rail on the right. |

Breakpoints are Carbon's `[ASSUMPTION]`; the 1-column rule is decided. Tiles keep at least their square size at every width and grow in height with content; more Lots means more scrolling, never compaction.

- **iOS:** SwiftUI NavigationStack, TabView, sheets for Pause/Thresholds, native pickers, edge-swipe back, Dynamic Type.
- **Android:** Material 3 structure (NavigationBar, TopAppBar, ModalBottomSheet, predictive back, system date/time pickers) with Escendit colour, type and 0-radius shapes; B's 12 px radius superseded.
- **Web:** SvelteKit BFF; DS Svelte components (AppShell, AppHeader, Button, TextInput, DatePicker, Modal, InlineNotification, ThemeSwitcher) consumed from vendored tokens, not the private `@escendit/branding` package. No BLE surfaces.
- **Theme:** follows the OS setting by default (iOS/Android system appearance; web `prefers-color-scheme` mapped to the DS `data-theme` attribute, since `theme.css` keys dark mode off `data-theme`). Override in Settings → Appearance: System / Light / Dark.

## Inspiration & Anti-patterns

- **Lifted from direction B (Plots):** square tile per Lot, solid orange as the only "act now" signal, soil-level fill, hatched unknown, stale-strips-fills, daily-low bar chart, big step counters in setup.
- **Lifted from the Escendit DS:** tokens, type, square corners, borders over shadows, Carbon icons, signature gradient on sign-in only.
- **Rejected — direction A (Ledger) list rows and C (Brief) serif sentences:** not chosen; nothing lifted.
- **Rejected — B's off-DS hex values and Android 12 px radius:** DS has priority.
- **Rejected — green "OK" badges:** OK stays neutral so only water and trouble draw the eye.
- **Rejected — weather, watering controls, snooze, mark watered:** V1 non-goals.
- **Rejected — dashboards that look calm when data is missing:** silence never looks fine.
