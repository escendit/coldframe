# Validation Report — Coldframe

- **DESIGN.md:** `/var/home/simon/work/escendit/coldframe/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/DESIGN.md`
- **EXPERIENCE.md:** `/var/home/simon/work/escendit/coldframe/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/EXPERIENCE.md`
- **Run at:** 2026-09-27T18:37:52+02:00

## Overall verdict

The pair is a usable contract. Every colour token has a hex value and light/dark pairs, every `{path}` reference resolves, all five spec journeys have a Key Flow with a protagonist and a climax, and the Lot-status and stale-data rules are specified well enough to build from without guessing. Most of the gaps are at the seams. Several components are specified in only one spine, or under different names in each. `DESIGN.md` still describes direction B's live-BLE calibration panel, which the memlog dropped in favour of stored-Reading calibration. Sign-in errors, notification-permission denial and calibration failures have no specified states. The rubric walker found no criticals and five highs to fix before epics are cut from these spines.

The two extra reviewers make the picture more serious. The accessibility review finds that the spines *do not yet meet the agreed floor*, though the fixes need no structural redesign. The DS orange fails as focus ring (1.00:1 on every orange surface), as the primary-button background under white text (2.64:1) and as light-theme text or selection. The "pattern" half of "text + pattern" is too low-contrast to see. Dynamic Type, zoom and live announcements are under-specified. The spec-consistency review returns *pass with changes*. It finds one critical gap: Members are never asked for notification permission, so UJ-5 fails. It also finds seven highs, several of which need a spec or AD decision rather than a UX edit: the Node "report now" button, the post-Calibration value versus AD-9, adding a Device to a paused Site, invitations, the unknown-cause and pause-source fields, and orange on non-water Threshold Alerts.

**Resolution status.** The user approved fixing every Critical and High finding. Named resolutions: dark ink `#0A0A0A` on orange buttons, deviating from the Escendit DS until the fix lands upstream; the Node setup-button short press ("report now") becomes a Node firmware requirement; invitations stay purely Phase Two; `LotStatus` gains server-computed `unknownCause` and `pausedBy`; notification permission is requested on the first Site overview. These fixes are being applied to the spines now. Medium and Low findings stay open and do not block epics.

## Category verdicts

- Flow coverage — adequate
- Token completeness — adequate
- Component coverage — thin
- State coverage — adequate
- Visual reference coverage — adequate
- Bloat & overspecification — adequate
- Inheritance discipline — adequate
- Shape fit — strong

## Findings by severity

Totals: Critical 3 · High 19 · Medium 39 · Low 40 (101 findings, per reviewer, not deduplicated). Critical and High: resolution approved, being applied. Medium and Low: open, non-blocking.

### Critical (3)

**[Accessibility]** — C1. The focus ring cannot be seen on orange surfaces and fails in the light theme (§ DESIGN.md focus; EXPERIENCE.md Accessibility Floor, Interaction Primitives)  
The orange ring is 1.00:1 on the needs-water tile, primary buttons, the Threshold Alert row, the selected segment and the first-run "next" tile, and 2.64 / 2.42 on white / layer-01. A keyboard user cannot see focus on the tile the product exists to show (WCAG 2.4.7, 1.4.11).  
Fix: Use a two-tone focus indicator: new `focus-inner` `#161616` / `focus-inner-dark` `#F4F4F4`, a 2 px outline plus a 2 px background gap. Keep orange only as an optional outer accent. Never let the sticky AppHeader or tab bar hide focus (2.4.11, `scroll-padding-top`).  
Status: Resolution approved — being applied

**[Accessibility]** — C2. The primary button label is white on orange (2.64:1; hover 3.34 / 2.33; active 4.32) (§ DESIGN.md button-primary.foreground; Colors [ASSUMPTION] "DS-owned")  
Every primary action fails 1.4.3 (SIGN IN, SEND INVITE, Record dry, Save, PAUSE HOME GARDEN…). The 14 px label does not qualify for the large-text exemption. Calling it "DS-owned" does not make it pass, and segmented choice and first-run tiles already use ink on orange.  
Fix: Set `button-primary.foreground` to `{colors.ink-on-bright}` (7.45 / 5.87 / 4.55). Raise the change upstream in `escendit/branding` and override locally until then. No white text on orange anywhere.  
Status: Resolution approved — being applied. Approved resolution: dark ink `#0A0A0A` on orange buttons, a recorded deviation from the Escendit DS until the fix lands upstream.

**[Spec-consistency]** — C-1 Members are never asked for notification permission, so UJ-5 fails (§ EXPERIENCE › Notifications "Permission"; memlog)  
Permission is requested only after the first Hub setup. A Member or invited Administrator never sets up a Hub, and on iOS and Android 13+ no push can be shown without the permission. The UJ-5 climax cannot happen (CAP-15, CAP-19, UJ-5 step 3).  
Fix: Ask when the User first holds a Membership and has seen the overview. Add a persistent "Notifications are off → Open Settings" row in My notifications.  
Status: Resolution approved — being applied. Approved resolution: notification permission is requested on the first Site overview.

### High (19)

**[Rubric · Token completeness]** — calibration-live-value "steady" text is green at about 1.9:1 (§ DESIGN.md L343–346, L456)  
`steady: {colors.support-success}` (`#48CF00`) is used as **text** ("steady for 5 s, ready to record") on `layer-01` `#F4F4F4`. In the light theme that is well below 4.5:1. The component itself is obsolete (see Component coverage).  
Fix: Remove the token group together with the component. If a "fresh Reading arrived" text colour is still needed, use a text-safe token (for example `text-primary` plus a Carbon checkmark icon).  
Status: Resolution approved — being applied

**[Rubric · Component coverage]** — DESIGN still specifies the dropped live-BLE calibration (§ DESIGN.md L343–346, L456; EXPERIENCE.md L97)  
DESIGN.md "Calibration live value" ("LIVE RAW VALUE", "steady for 5 s, ready to record", citing B-plots-4/5) specifies the **live-BLE** calibration that the memlog dropped. EXPERIENCE.md specifies "Calibration reference point" on stored Readings. The two spines describe incompatible screens. There is no visual spec for the waiting state, the recent-Readings list or the "~% ±5 %" confirmation.  
Fix: Replace the DESIGN component with "Calibration reference point": a waiting panel (last raw value and time, "Waiting for the next Reading"), selectable recent-Reading rows, and the confirmation block. Drop the "steady" semantics.  
Status: Resolution approved — being applied

**[Rubric · Component coverage]** — Devices list, Site menu and Site switcher have no visual spec (§ EXPERIENCE.md IA L35, L42; §UJ-4 step 1; §Component Patterns L90)  
These are central to navigation. The **Devices list / Device row** is a main tab with row actions and a swipe reveal. The **Site menu** is the entry point for Site Pause and Resume in UJ-4. The **Site switcher** is an IA surface. Neither Site menu nor Site switcher has a behavioural row in EXPERIENCE.md either.  
Fix: Add DESIGN entries for Device row (Hub vs Node, silent and paused treatments). Add a Site menu / Site switcher row to both spines, covering the mobile sheet and the web AppHeader Site tabs.  
Status: Resolution approved — being applied

**[Rubric · State coverage]** — Sign in has no error states (§ EXPERIENCE.md §IA "Sign in"; §State Patterns)  
Not covered: Server address unreachable or mistyped, TLS or certificate failure (NFR-10, AD-13 real domain), not on the home network (NFR-1), Keycloak cancelled or failed, token expired after sign-in. Every user sees this surface first, and it is the most likely place to fail when away from home.  
Fix: Add a "Sign-in and session" block to State Patterns with copy for each case, in the style of the BLE table.  
Status: Resolution approved — being applied

**[Rubric · State coverage]** — Notification permission denied or revoked is not handled (§ EXPERIENCE.md §Notifications "Permission"; §State Patterns)  
The notification is the whole value of Coldframe. With permission denied, the product fails without any visible sign, which contradicts "silence never looks fine".  
Fix: Add a state for My notifications and a persistent inline notice on the overview ("Notifications are off for Coldframe on this phone — you won't get Alerts." with Open Settings).  
Status: Resolution approved — being applied. Approved together with Spec-consistency C-1: permission is requested on the first Site overview.

**[Accessibility]** — H1. Light-theme orange fails as text or as a state indicator (§ button-ghost, links, step-counter, navigation selected, segmented-choice, selectable-tile, wifi-network-row)  
Orange text is 2.64 on white and 2.42 on layer-01. The step counter fails even as large text. Selected tab, segment, tile and row show selection only by a low-contrast colour change (1.4.1, 1.4.11).  
Fix: Add `primary-text` `#B84A00` (light) / `#FF770F` dark on background. Use it for ghost buttons, links, step counter, selected nav and low line. Add a non-colour selection cue (checkmark, M3 indicator pill, filled icon) and expose the selected state to screen readers.  
Status: Resolution approved — being applied

**[Accessibility]** — H2. The "pattern" half of "text + pattern" cannot be seen; states collapse into 4 shapes (§ status-hatch-a/b, status-ok-*, DESIGN.md Shapes and Colors claim)  
Hatch 1.20 / 1.31, OK fill 1.09, OK border 1.31 (1.00 in dark, because the border equals the fill). unknown and needsCalibration have identical shapes, noNode matches them once the hatch disappears, and ok and stale are both 1 px solid. The claim "tile borders and hatch vs background ≥ 3:1" is false.  
Fix: Draw the hatch as 1.5 px `#8D8D8D` lines over layer-01, with a solid plate behind text. OK border `#8D8D8D`. Give each status a distinct shape and add one Carbon status icon per status. Correct the Colors claim.  
Status: Resolution approved — being applied

**[Accessibility]** — H3. Tile foot line and helper text over the soil fill or hatch fail contrast (§ DESIGN.md tile anatomy)  
text-helper on `#E0E0E0` is 3.75, and on dark `#525252` 3.48. The 12 px foot line carries load-bearing data (Reading time, low Threshold, "as of 07:02").  
Fix: Specify tile text per status: `text-primary` for name and value, `text-secondary` for the foot line. Lower the dark level to `#474747`. Add a CI rule in `packages/design-tokens` checking every tile text token against every surface it can overlap.  
Status: Resolution approved — being applied

**[Accessibility]** — H4. Setup progress is shown by colour alone, and the colours merge under deuteranopia (§ DESIGN.md setup-progress; EXPERIENCE.md Setup progress)  
Done vs active is 1.29:1 (ΔE 7 under deutan). Pending layer-01 on white is 1.09. Done green on white is 2.04.  
Fix: Put an icon and text on every segment, give pending segments a `border-strong` outline, add `setup-done` `#2E7D00` for light, and expose the progress as one element with value text.  
Status: Resolution approved — being applied

**[Accessibility]** — H5. The history chart cannot be read in the light theme (§ chart-bar, chart-bar-below-low, chart-band, history-chart.low-line)  
Bars vs background are 1.69, below-low vs normal bar 1.56, band vs background 1.03, low line on band 2.56. Only the text summary survives.  
Fix: Use `chart-bar` `#8D8D8D` and `chart-bar-below-low` `#B84A00` with a non-colour cue, draw the low line 2 px in `primary-text`, and treat the band as decorative. Use `#8D8D8D` for the dark bars.  
Status: Resolution approved — being applied

**[Accessibility]** — H6. Fixed-aspect tiles conflict with Dynamic Type, font scale and 200 % zoom / reflow (§ DESIGN.md Layout; EXPERIENCE.md Accessibility Floor, Responsive, i18n)  
At AX5 or 200 % font scale, the tile content cannot fit a 2-up or even a 1-up square. On a 320 CSS px web view, tiles are about 140 px. A fixed aspect ratio clips text-only zoom and text spacing (1.4.4, 1.4.10, 1.4.12).  
Fix: Make square a minimum, not a fixed size, or switch to 1 column at a defined text-size threshold. Decide this as a rule and drop the `[ASSUMPTION]`. Use rem-based web tiles and 1 column below 400 CSS px.  
Status: Resolution approved — being applied

**[Accessibility]** — H7. No announcement or live-region policy for things that change while the user waits (§ EXPERIENCE.md Setup progress, Calibration reference point, Device candidate tile, Stale header)  
A blind user hears nothing as setup advances, when Record dry becomes available, when stale mode starts or ends, or when a candidate appears. An unscoped live region on the ticking age would spam them (4.1.3).  
Fix: Add an Announcements table (polite, assertive, never announce), move focus to the headline on screen change, and name the platform APIs.  
Status: Resolution approved — being applied

**[Spec-consistency]** — H-1 The Node setup-button short press ("report now") is invented firmware behaviour, and UJ-1 depends on it (§ EXPERIENCE › Calibration reference point; UJ-1 step 7)  
The spec gives the button one meaning: enter BLE setup mode (device-hardware, CAP-2, AD-25, NFR-4). A second gesture that forces an ESP-NOW report is new firmware and protocol behaviour. The `[ASSUMPTION]` tag hides a spec change.  
Fix: Add it to device-hardware.md as a Node requirement (short press = report now, long press = setup mode), or remove it from UJ-1.  
Status: Resolution approved — being applied. Approved resolution: short press "report now" becomes a Node firmware requirement.

**[Spec-consistency]** — H-2 DESIGN still specifies B's live-BLE calibration component (§ DESIGN › components.calibration-live-value; Components; Typography; Colors › Green)  
AD-9 says reference points are raw values of **stored Readings** submitted over REST. A live value with a "steady for 5 s" check needs a live BLE stream. Implementers reading DESIGN will build the dropped flow.  
Fix: Replace it with a stored-Reading panel. Remove "steady" from green usage and "live calibration" from the typography notes. Mark B-plots-4/5 as superseded.  
Status: Resolution approved — being applied

**[Spec-consistency]** — H-3 The value shown right after Calibration contradicts AD-9 (§ EXPERIENCE › UJ-1 step 8; Calibration reference point)  
Readings stored before Calibration have no %. The latest Reading stays raw until the next wake, so the tile cannot show "about 40 %" without client-side computation (against AD-14).  
Fix: Word the confirmation as a preview, add a "Calibrated · first % Reading due by 07:15" state, and move the UJ-1 climax to the next Reading. Alternatively, amend AD-9 so the Server re-derives the latest Reading.  
Status: Resolution approved — being applied

**[Spec-consistency]** — H-4 Adding a Device to a paused Site is not designed, and calibrating in that state is impossible (§ EXPERIENCE › UJ-1 steps 5–7, Node-added screen, Calibrate)  
CAP-18 and AD-8: Devices added to a paused Site start paused, and their Readings are discarded. Calibrate would wait forever, and the acceptance criterion has no UI surface.  
Fix: On the Node-added and Hub-online screens, explain the paused start and offer Resume Site or "Calibrate later". Calibrate refuses to start on a paused Device and says why.  
Status: Resolution approved — being applied

**[Spec-consistency]** — H-5 The invitation UI needs data and surfaces the architecture does not provide (§ EXPERIENCE › IA "Accept invitation"; Member tile; UJ-5)  
AD-3: Phase Two's native invitation flow, no invitation state on the Server. Pending-invite tiles need a projection that does not exist. Acceptance happens on Phase Two pages, so no Coldframe copy can run there. Expiry is not handled, and the Membership arrives asynchronously.  
Fix: Decide in SPEC/AD whether to record sent invitations, or remove the invited tile. Treat acceptance as a themed Phase Two page, put the away-from-home copy in the email, and add a "Joining…" state.  
Status: Resolution approved — being applied. Approved resolution: invitations stay purely Phase Two, with no Coldframe invitation state.

**[Spec-consistency]** — H-6 Unknown cause and OK secondary conditions need read-model fields that do not exist (§ EXPERIENCE › State Patterns (SILENT vs HUB SILENT; ok + Health condition); DESIGN › Lot tile)  
AD-14 `LotStatus` has only `statusSince` and `lastReadingAt`. The client would have to join Alerts to Lots, which is status logic in the client.  
Fix: Extend LotStatus with server-computed `unknownCause` and an optional `secondaryCondition`, or drop the refinements.  
Status: Resolution approved — being applied. Approved resolution: LotStatus gains server-computed `unknownCause` and `pausedBy`.

**[Spec-consistency]** — H-7 Threshold Alerts that are not "needs water" are painted orange (§ DESIGN › alert-row-threshold; Colors; EXPERIENCE › Notifications)  
"Herbs too wet" or "temperature below 5 °C" would use the act-now orange while the Lot tile stays neutral `ok` (CAP-11, AD-14). Orange loses its single meaning.  
Fix: Use orange only for low-side soil-moisture Threshold Alerts. Give other Threshold Alerts a distinct non-orange treatment.  
Status: Resolution approved — being applied

### Medium (39)

**[Rubric · Flow coverage]** — CAP-10 (set/change/remove Thresholds) has no Key Flow (§ EXPERIENCE.md §Key Flows UJ-1)  
UJ-2 relies on "your low is 30 %", but no flow shows the Thresholds screen being reached, the default proposal (Min + 20 % × range) or saving. The first-run step tile 4 "Set a low Threshold" has no flow. UJ-1 ends at the Notification Window (step 9).  
Fix: Add a step to UJ-1 after the Calibration climax (Thresholds opened from the confirmation, proposed low accepted, Save), or add a short flow.  
Status: Open

**[Rubric · Flow coverage]** — UJ-4 has no failure or edge path (§ EXPERIENCE.md §UJ-4)  
Not covered: a Member trying to pause, a Device added while the Site is paused ("start paused", CAP-18), an automatic resume at the end date. Step 1 allows "Until left empty (or 1 Mar)", but the step 3 climax only shows "Paused until 1 Mar".  
Fix: Pick one example path and add an edge line for the automatic resume on the end date.  
Status: Open

**[Rubric · Flow coverage]** — UJ-6 has no failure path (§ EXPERIENCE.md §UJ-6)  
UJ-6 covers the admin actions most likely to be rejected: removing a Lot that holds a Node, demoting the last Owner, moving a Node onto an occupied Lot.  
Fix: Add a "Failure:" line that points to the existing validation copy in State Patterns.  
Status: Open

**[Rubric · Token completeness]** — confirm-panel border is yellow on white (about 1.4:1) (§ DESIGN.md L357, L402)  
`{colors.support-warning}` `#FFCB0F` on `#FEFEFE` is below 3:1 for non-text. DESIGN.md Colors itself says yellow-500 cannot be read on white.  
Fix: Point it to `{colors.status-calibration-border}` / `-dark`, or add a `confirm-border` light/dark pair.  
Status: Open

**[Rubric · Token completeness]** — outcome-screen-error eyebrow red fails as text (about 3.3:1) (§ DESIGN.md L372, L462)  
`{colors.support-error}` `#FF454B` is used as small uppercase text on `#FEFEFE`.  
Fix: Add a light-theme `support-error-text` token (DS red-600/700) with a dark twin.  
Status: Open

**[Rubric · Token completeness]** — button-primary is white on orange (about 2.7:1) while other orange surfaces use ink (§ DESIGN.md L296, L322, L365, L399)  
The button is declared DS-owned `[ASSUMPTION]`, but `segmented-choice` and `first-run-step-tile` put `ink-on-bright` on the same orange. So the most frequent CTA is the one orange surface that fails 4.5:1, and the two treatments conflict.  
Fix: Record the DS button contrast as an explicit exception, or use `ink-on-bright` on the button label.  
Status: Open. The approved Accessibility C2 resolution (dark ink `#0A0A0A` on orange buttons) covers this.

**[Rubric · Component coverage]** — Components specified only for behaviour, with no DESIGN visual spec (§ DESIGN.md Components; EXPERIENCE.md State Patterns L129)  
Setup code field (including the "accepted" chip), Pause sheet, Invite form, Mute toggle (native, acceptable), Theme switcher (covered implicitly by Segmented choice), skeleton tile (cold start).  
Fix: Add one-line DESIGN entries for the setup-code chip, Pause sheet chrome and skeleton tile. The rest can point to DS or native components.  
Status: Open

**[Rubric · Component coverage]** — Components specified only visually, with no EXPERIENCE behavioural row (§ EXPERIENCE.md Component Patterns)  
First-run step tiles (what a Member sees on an empty Site, whether completed steps disappear), Outcome screens (covered only indirectly by the BLE error table), Sign-in surface (field validation, Keycloak hand-off, return), Buttons, Text input, Navigation (the Alerts count badge rule appears only in IA prose).  
Fix: Add rows at least for First-run step tiles, Sign-in surface and Outcome screens.  
Status: Open

**[Rubric · Component coverage]** — The spines name the same components differently (§ DESIGN.md Components vs EXPERIENCE.md Component Patterns)  
"Alert rows" / "Alert row"; "Confirm panel" / "Time-zone confirm panel"; "Selectable tile" / "Device candidate tile" + "Lot picker"; "Step counter + setup progress" / "Setup flow shell" + "Setup progress"; "Notification Window bar" / "Notification Window control"; "Calibration live value" / "Calibration reference point".  
Fix: Use one canonical name per component in both files and in the frontmatter keys. Where one visual serves several behaviours, say so in the DESIGN entry.  
Status: Open

**[Rubric · State coverage]** — Calibrate failure states are missing (§ EXPERIENCE.md L97)  
Not covered: no Reading arrives (the Node goes silent during calibration), the wet raw value is too close to the dry value or on the wrong side of it, the flow is resumed after the Node was moved.  
Fix: Add rows: timeout copy after, say, 20 min pointing to the Silent Node state, and a validation message for "dry and wet are too close".  
Status: Open

**[Rubric · State coverage]** — Accept invitation covers only the off-network case (§ EXPERIENCE.md §UJ-5 Failure)  
Not specified: invitation expired or revoked, already a Member, signed in with a different account than the invited email.  
Fix: Add rows.  
Status: Open

**[Rubric · Visual reference coverage]** — Citations point to superseded screens without a warning (§ DESIGN.md L454, L456–457)  
B-plots-4 and B-plots-5 are cited as the model for "Calibration live value" but show the dropped live-BLE flow. B-plots-3 shows an "ALREADY SET UP" Hub tile that the spine forbids (AD-25).  
Fix: Annotate at the citation, for example "(B16–B17 show live BLE — superseded by stored-Reading calibration)" and "(ignore 'Already set up' tile, AD-25)".  
Status: Open

**[Rubric · Bloat & overspecification]** — The IA "Closure notes" are stale (§ EXPERIENCE.md L50 vs §UJ-6)  
The notes say Devices, Site settings, Site switcher, Appearance and browser notifications have no journey and are "not invented further". UJ-6 now covers exactly those surfaces.  
Fix: Delete the paragraph or replace it with "Covered by UJ-6 (UX-added; see §7)".  
Status: Open

**[Rubric · Inheritance discipline]** — UJ-6 exists only in EXPERIENCE.md (§ EXPERIENCE.md §UJ-6; spec user-journeys.md)  
`user-journeys.md` still lists only UJ-1…UJ-5, although the other UX-driven upstream changes were propagated. Epics built from the spec will not see UJ-6.  
Fix: Add UJ-6 to `user-journeys.md` with CAP refs (CAP-2, 6, 7, 12, 13, 20), or mark it in EXPERIENCE.md as "UX-only, pending spec update".  
Status: Open

**[Accessibility]** — M1. Error eyebrow red fails as text (§ support-error)  
`#FF454B` text is 3.36 light and 4.47 dark.  
Fix: Add `support-error-text` `#DA1E28` / `#FF8389`. Keep `#FF454B` for borders. Show an error icon beside the reason and connect the reason with `aria-describedby`.  
Status: Open

**[Accessibility]** — M2. Assorted non-text borders below 3:1 (§ status-calibration-border, border-strong-dark, confirm panel)  
Calibration dash on hatch-a 2.78, dark input border 2.30, confirm panel yellow 1.51.  
Fix: Use `#7A6800`, `#8D8D8D` and `#9C8400` respectively.  
Status: Open

**[Accessibility]** — M3. The soil level and low-Threshold tick do not show "how dry" (§ status-ok-level, water-level, low tick)  
Levels are 1.20–1.51 and the tick over the level is 2.51. The Brand claim does not hold for low vision.  
Fix: Draw a 2 px top edge (`status-level-edge`) and make the tick 2 × 12 px in text-secondary.  
Status: Open

**[Accessibility]** — M4. Disabled states hide the reason, and disabled controls are hard to discover (§ selectable-tile.disabled-foreground; stale-mode admin actions)  
Reason text ("HAS A NODE", WPA3 message) is drawn at 1.69. Disabled web buttons are not focusable, so the reason is never reached.  
Fix: Dim only the name and draw the reason in text-secondary. Use `aria-disabled` or an accessibility hint, or a single inline notice.  
Status: Open

**[Accessibility]** — M5. Gaps in the screen-reader labels (§ EXPERIENCE.md State Patterns)  
Missing: the OK-tile Health condition, the HUB SILENT variant, the full stale label, the button role, merging each tile into one element, the headline as a heading, and "Alerts, 5 open".  
Fix: Add these labels, built from externalized, pluralised strings.  
Status: Open

**[Accessibility]** — M6. The chart needs more than a summary sentence (§ EXPERIENCE.md History chart)  
The "tap/drag a bar" tooltip has no keyboard or screen-reader equivalent.  
Fix: Add a data-table or list alternative, Audio Graphs, focusable or adjustable bars, arrow-key navigation, spoken gaps, and a labelled Sensor picker.  
Status: Open

**[Accessibility]** — M7. The time-limit policy cannot be implemented as written (§ EXPERIENCE.md Accessibility Floor)  
The app cannot detect whether a screen reader is reading. The real timeouts come from the hardware.  
Fix: Separate device-imposed from app-imposed timeouts. App-imposed waits never end the flow. A retry keeps what the user entered. State a firmware minimum for the Node setup window (for example ≥ 3 min).  
Status: Open

**[Accessibility]** — M8. The live candidate list reorders under the finger and the screen-reader cursor (§ EXPERIENCE.md Device candidate tile)  
"Strongest first" while the list keeps scanning makes items jump.  
Fix: Sort only on first display, then keep insertion order. Use a text badge for "PRESSED JUST NOW" and announce politely.  
Status: Open

**[Accessibility]** — M9. Truncation contradicts the floor (§ EXPERIENCE.md i18n vs Accessibility Floor)  
"Wrap to two lines, then truncate" conflicts with "no truncated controls or clipped values".  
Fix: Allow truncation below accessibility sizes only, and never truncate status labels.  
Status: Open

**[Accessibility]** — M10. Small condensed type carries critical data (§ DESIGN.md status-label, meta-mono)  
The status word is 13 px condensed uppercase, and the foot line is 12 px.  
Fix: Use at least 14 px for the status label and 13 px meta-mono on tiles. No Ubuntu Condensed below 14 px.  
Status: Open

**[Accessibility]** — M11. No plan for forced colours or high contrast (§ DESIGN.md / EXPERIENCE.md)  
Windows forced-colors removes the hatch and fills, so the needs-water tile looks like every other tile. iOS and Android contrast settings are not addressed.  
Fix: Add a `forced-colors` block, honour Differentiate Without Color and Increase Contrast, and mark fills and charts to be ignored by colour inversion.  
Status: Open

**[Accessibility]** — M12. Web structure basics are missing (§ EXPERIENCE.md Responsive / Web)  
No skip link, landmarks, per-route title, lang attribute, modal focus trap, focus on route change, tablist semantics or list semantics for the grid.  
Fix: Add them to EXPERIENCE.md.  
Status: Open

**[Spec-consistency]** — M-1 A 7-day closed-Alert history is not in the architecture (§ EXPERIENCE › IA Alerts; Alert row; UJ-2 climax)  
AD-7 keeps only open Alerts, and AD-14 names only LotStatus as a read model.  
Fix: Add an Alerts read model with a closed-Alert query and a close reason to AD-14/openapi.  
Status: Open

**[Spec-consistency]** — M-2 The time zone is confirmed during Create Site, which reads as a Site property (§ EXPERIENCE › IA Create Site; UJ-1 step 2)  
Glossary: a User has one time zone, and Sites have none (AD-11).  
Fix: Confirm "your time zone" on first sign-in or in My notifications. Add the IP-geolocation fallback.  
Status: Open

**[Spec-consistency]** — M-3 The Pause end-date time zone and the auto-resume moment are undefined (§ Pause sheet; tiles; headline)  
CAP-18 and AD-6/AD-11 store a UTC due-at, but no rule says how a date resolves to it.  
Fix: Define it (for example 00:00 in the pausing User's zone) and add "Resumes automatically on…" helper text.  
Status: Open

**[Spec-consistency]** — M-4 Pause sources are hidden, so Resume on a Device whose Site is paused misleads (§ Pause sheet; paused hero)  
AD-8 `pausedBy ⊆ {device, site}`. A Device resumes only when both sources clear.  
Fix: Show the source(s) and point to Resume Site when the Site Pause blocks.  
Status: Open. Note: `pausedBy` is now part of the approved H-6 resolution; the UX copy remains open.

**[Spec-consistency]** — M-5 Morning summary per Site vs "one summary" (§ EXPERIENCE › Notifications)  
CAP-16 says one summary when the window opens. AD-7 does not specify per-Site scope.  
Fix: Decide per User or per Site. State that Alerts that opened and closed while held are dropped.  
Status: Open

**[Spec-consistency]** — M-6 There is no way to remove Thresholds or return to the default (§ Threshold column)  
CAP-10 includes "removes". AD-19 has `Default | Override | Cleared`.  
Fix: Add "Stop alerting on this Sensor" and "Reset to Specification default".  
Status: Open

**[Spec-consistency]** — M-7 The spec does not support the "±5 %" accuracy claim (§ EXPERIENCE › Lot detail hero, Calibration confirmation; DESIGN)  
SPEC Constraints: moisture values are approximate, with no temperature compensation.  
Fix: Replace it with "approximate" and keep the `~` prefix and 5 % rounding.  
Status: Open

**[Spec-consistency]** — M-8 AD-14 does not cover mobile live updates over SignalR (§ EXPERIENCE › Interaction Primitives; Lot tile)  
AD-14 describes SignalR only for the browser, through the BFF. The KMP core is REST only.  
Fix: Amend AD-14, or define mobile refresh as refetch on foreground, pull-to-refresh and polling.  
Status: Open

**[Spec-consistency]** — M-9 Nothing defines where partial Calibration points are kept (§ Calibration reference point)  
AD-9 has no draft state.  
Fix: Keep the dry point locally on the client, or add a Sensor-grain draft to AD-9.  
Status: Open

**[Spec-consistency]** — M-10 History chart vs Node moves and Readings taken before Calibration (§ History chart; UJ-6)  
History follows the Node (AD-18), and Readings stored without a Calibration have no %.  
Fix: Say whether the chart shows the Lot's or the Node's history, and show raw-only days as gaps, never 0 %.  
Status: Open

**[Spec-consistency]** — M-11 Node advertisement content and the Hub Wi-Fi scan list extend the BLE protocol (§ Device candidate tile; Wi-Fi network row)  
AD-25 does not specify the advertising payload or a Wi-Fi scan message.  
Fix: Add the messages to `packages/proto`/AD-25, or show Node details only after the session opens.  
Status: Open

**[Spec-consistency]** — M-12 The Settings IA mixes per-User and per-Site scopes without labelling them (§ My notifications)  
The window and time zone are per User. Mute and Reminder cadence are per Site.  
Fix: Split the surface into "All Sites" and "<Site name>".  
Status: Open

**[Spec-consistency]** — M-13 Reminder cadence values are not in the contract (§ Reminder cadence)  
CAP-12 and AD-7 define the resolution order but no set of values.  
Fix: Record the allowed values in openapi/AD-7.  
Status: Open

### Low (40)

**[Rubric · Flow coverage]** — CAP-14, CAP-17 and CAP-9 recalibration have no flow (§ EXPERIENCE.md §Component Patterns "Calibration reference point")  
Low battery, mute and recalibration ("changes only new Readings") appear only in component rows and notification payloads. That is acceptable for V1, but recalibration from Lot detail is a path no flow covers.  
Fix: Optional one-line variant under UJ-1.  
Status: Open

**[Rubric · Flow coverage]** — UJ-1 step 7 relies on the Node "report now" button (§ EXPERIENCE.md §UJ-1 step 7)  
The button is tagged `[ASSUMPTION]` pending the Node epic. The main path should read correctly without it.  
Fix: Write the step as "waits for the next Reading (≤ 15 min)" and treat the button as an optional shortcut.  
Status: Open. Related: the approved Spec-consistency H-1 resolution makes the button a Node firmware requirement.

**[Rubric · Flow coverage]** — UJ-5 step 2 contradicts the web-only Accept invitation (§ EXPERIENCE.md §IA "Accept invitation" vs §UJ-5)  
UJ-5 step 2 says "the app opens on Home's overview", but IA marks Accept invitation as web-only `[ASSUMPTION]`.  
Fix: Say "the web app opens…" or resolve the assumption.  
Status: Open. Related: invitations stay purely Phase Two (approved Spec-consistency H-5 resolution).

**[Rubric · Token completeness]** — Stated contrast figures are optimistic (§ DESIGN.md L399, L407)  
`#0A0A0A` on `#FF770F` computes to about 7.4:1, not "8.2:1" or "≈ 8:1". Pass/fail is unchanged.  
Fix: Correct the figures.  
Status: Open

**[Rubric · Token completeness]** — primary-active has no -dark twin (§ DESIGN.md L38–40)  
`primary-hover` has a `-dark` twin and `primary-active` does not, so hover and active diverge in the dark theme.  
Fix: Add `primary-active-dark` or say that it is intentionally shared.  
Status: Open

**[Rubric · Token completeness]** — Unreferenced tokens (§ DESIGN.md frontmatter)  
Nothing references `layer-02`/`-dark`, `support-info`, `typography.tile-name` (named in prose without braces), `body-lg`, `spacing.tile-padding-web` or `rounded.DEFAULT`.  
Fix: Reference `tile-name` and `tile-padding-web` as `{…}` in Layout & Spacing / Components; keep or drop the rest.  
Status: Open

**[Rubric · Component coverage]** — Setup flow shell step count is wrong for Calibrate (§ EXPERIENCE.md L91)  
"Setup flow shell" applies "NN / 05" to Calibrate, but Calibrate has two steps (dry and wet, B16 "STEP 1 OF 2").  
Fix: State the step count for each flow.  
Status: Open

**[Rubric · State coverage]** — Missing empty or cold-load states (§ EXPERIENCE.md §State Patterns)  
Devices with no Devices (Member on a new Site), Lot detail and Alerts cold load, Site switcher with a single Site, Members with only yourself, invite email send failure (SMTP).  
Fix: One row each, or a general cold-load rule that applies to every surface.  
Status: Open

**[Rubric · State coverage]** — Lot detail chart for a new Node is only implied (§ EXPERIENCE.md L86)  
For a Node added less than 30 days ago, the chart's behaviour is only implied by "Days without Readings are gaps".  
Fix: State that the chart shows the days so far and gives no "no data" warning for days before the Node existed.  
Status: Open

**[Rubric · Visual reference coverage]** — EXPERIENCE.md links captures only once (§ EXPERIENCE.md L19)  
Captures are linked only as a block in Foundation ("[1]–[7]"). Key Flows and Component Patterns never point to the frame that illustrates them.  
Fix: Add inline "→ B-plots-n (B09–B12)" pointers in the Key Flows.  
Status: Open

**[Rubric · Visual reference coverage]** — A and C captures are bare numbers (§ DESIGN.md L392)  
They have no description of what each shows. This is acceptable because those directions were rejected.  
Fix: Optional.  
Status: Open

**[Rubric · Bloat & overspecification]** — Foundation and Notifications restate architecture (§ EXPERIENCE.md L15–17, L237, L256)  
They repeat content from the architecture: the KMP path `packages/kt/core`, SignalR invalidation, AD-7 Server-side timing, the counter-metric.  
Fix: Reduce to one-line AD citations.  
Status: Open

**[Rubric · Bloat & overspecification]** — A flow is written into a table cell (§ EXPERIENCE.md L97)  
The "Calibration reference point" cell is a whole flow written as one paragraph.  
Fix: Move the step sequence to a Key Flow variant and keep only the rules in the row.  
Status: Open

**[Rubric · Inheritance discipline]** — Terms not in the glossary (§ EXPERIENCE.md UJ-3 step 1 and others)  
"Silent Alert", "Uncalibrated" push/Alert, "Admin+", "Garden" (tab label for the Site overview), "Site menu". The glossary defines only "Health Alert", with three causes.  
Fix: Use "Health Alert (silent)" or add the sub-kind names to the glossary. Define "Admin+" once as "Administrator or Owner".  
Status: Open

**[Rubric · Inheritance discipline]** — Green and orange usage rules have undeclared exceptions (§ DESIGN.md L403 vs L94–95, L405; L362, L398)  
Green "only confirms a finished setup step", but `chart-band` is a green tint on data surfaces. Orange is used decoratively for the member-tile "you" initials, despite "never for decoration".  
Fix: Add the chart band to the green rule and make the member initials neutral, or list both as exceptions.  
Status: Open

**[Rubric · Shape fit]** — Frontmatter status and description (§ EXPERIENCE.md frontmatter)  
EXPERIENCE.md frontmatter has no `description`. Neither spine's `status: draft` reflects the user acceptance recorded in the memlog.  
Fix: Set the status when finalizing.  
Status: Open

**[Accessibility]** — L1. "~", "—", "+", "h"/"min" in push bodies (§ EXPERIENCE.md Notifications)  
Push bodies are read raw ("tilde twenty percent").  
Fix: Use "about 20 %" in notification bodies, or accept the loss knowingly.  
Status: Open

**[Accessibility]** — L2. Target sizes (§ DESIGN.md field-height)  
40 px web fields do not meet the stated 44 px floor, although they pass WCAG 2.5.8.  
Fix: Align the floor or use 48 px. Specify hit areas for icons, chips, drag handles and segments.  
Status: Open

**[Accessibility]** — L3. Gesture alternatives (§ EXPERIENCE.md Device row swipe, pull-to-refresh)  
Swipe-to-Pause and pull-to-refresh need alternatives.  
Fix: Add custom actions and a Refresh menu item. Give the Threshold drag Slider semantics.  
Status: Open

**[Accessibility]** — L4. Marginal passes that will fail after rounding (§ Dark no-node border, border-strong, chart bar, paused border)  
Values are 3.01–3.14, and 4.57 for text-secondary on `#525252`.  
Fix: Use `#8D8D8D` for dark dashed and neutral borders and `#9A66FF` for the dark paused border.  
Status: Open

**[Accessibility]** — L5. Contrast claims in DESIGN.md are inaccurate (§ DESIGN.md Colors)  
"8.2:1" is really 7.45, and "hatch vs background ≥ 3:1" is false.  
Fix: Replace the prose with a table generated by the token CI check.  
Status: Open

**[Accessibility]** — L6. Stale component (§ DESIGN.md calibration-live-value)  
Describes the dropped live-BLE calibration, with green text at 1.87:1.  
Fix: Remove it or rewrite it as "latest Reading".  
Status: Open

**[Accessibility]** — L7. Orange surfaces with unspecified text (§ Member tile, sign-in gradient, Notification Window bar)  
The "you" initials have no foreground token, and white on the sign-in gradient is 2.64.  
Fix: Use `ink-on-bright`, put no text on the gradient, and mark the bar as decorative.  
Status: Open

**[Accessibility]** — L8. Needs-water tile vs white page is 2.64 (§ DESIGN.md lot-tile needs-water)  
The tile is large, solid and labelled, so it is perceivable.  
Fix: Nothing more is needed if the H2 icons are adopted. Otherwise add a 1 px `#C25C00` edge.  
Status: Open

**[Spec-consistency]** — L-1 Headline cases are incomplete or inaccurate (§ EXPERIENCE › Voice rules)  
Mixed ok + paused / no-Node cases are missing, and "can't be read" is wrong for needsCalibration.  
Fix: Use "2 Lots need calibration" / "1 Lot silent" and define the mixed cases.  
Status: Open

**[Spec-consistency]** — L-2 "Paused by Simon (Owner) on 2 Nov" needs actor and time (§ EXPERIENCE › All paused)  
AD-8 stores only the state and the end date.  
Fix: Add them to the read model, or drop the line.  
Status: Open

**[Spec-consistency]** — L-3 The Hub's "LED blinks orange" is invented (§ BLE errors)  
device-hardware has no LED.  
Fix: Add it to the Hub epic, or use generic copy.  
Status: Open

**[Spec-consistency]** — L-4 Device cell "charging · solar" (§ DESIGN › device cell)  
The spec has only charging and not charging.  
Fix: Drop "solar".  
Status: Open

**[Spec-consistency]** — L-5 Glossary wording (§ Uncalibrated push; DESIGN Colors; UJ-2)  
"Soil sensor" should be "Soil-moisture Sensor". "bed" appears in prose.  
Fix: Keep "bed" out of UI strings.  
Status: Open

**[Spec-consistency]** — L-6 "Garden" tab label (§ Navigation)  
The glossary term is Site.  
Fix: Show the Site name in the header, and consider "Overview".  
Status: Open

**[Spec-consistency]** — L-7 "from 07:00" interpreted as 07:00–22:00 (§ Notification Window)  
CAP-16 says only "from 07:00".  
Fix: Confirm the default end in the SPEC.  
Status: Open

**[Spec-consistency]** — L-8 Hub timeout of 90 s vs CAP-1's "within one minute" (§ BLE errors)  
Acceptable as an error margin.  
Fix: Add a "Taking longer than usual…" state at 60 s.  
Status: Open

**[Spec-consistency]** — L-9 Recalibration messaging is missing (§ Calibrate confirmation)  
CAP-9: a recalibration changes only new Readings, and Threshold % values stay unchanged.  
Fix: Say so when a Calibration already exists.  
Status: Open

**[Spec-consistency]** — L-10 Calibration gating (§ Calibrate entry points)  
CAP-3: only `calibration: true` Sensors can be calibrated.  
Fix: State the gate explicitly.  
Status: Open

**[Spec-consistency]** — L-11 The Pause close effect is not shown (§ Alerts)  
CAP-18: open Alerts close on Pause.  
Fix: Show those Alerts moving to Closed with reason "paused".  
Status: Open

**[Spec-consistency]** — L-12 The time-zone detection order is missing the IP fallback (§ Time-zone confirm panel)  
AD-11. Covered in M-2.  
Fix: See M-2.  
Status: Open

**[Spec-consistency]** — L-13 The web side nav shows "Members" to Members (§ Web side nav)  
The spec does not say whether Members may see the Membership list.  
Fix: Show a read-only list, or hide it.  
Status: Open

**[Spec-consistency]** — L-14 Air (gas) shown in kΩ while the contract unit is Ω (§ Sensor cell; Threshold entry)  
Fine for display.  
Fix: Make Threshold entry show or convert the stored unit clearly.  
Status: Open

**[Spec-consistency]** — L-15 UJ-6 exists only in the UX (§ EXPERIENCE › UJ-6)  
Same as the Inheritance-discipline finding.  
Fix: Add it to `user-journeys.md`, or trace the surfaces to CAPs directly.  
Status: Open

**[Spec-consistency]** — L-16 The browser-notification opt-in is per browser (§ My notifications (web))  
Where it is stored is not stated.  
Fix: State that it is stored in the browser and that Server-side window and mute still apply (CAP-20).  
Status: Open

## Mechanical notes

- **Token references:** 0 unresolved across both spines (script-verified). Unreferenced tokens are listed under Token completeness.
- **Name inconsistencies:** see the component name pairs under Component coverage. Also, the State Patterns `ok` example shows the tile as `~45`, but its screen-reader label says "about 35 percent" (EXPERIENCE.md L119).
- **Stale cross-reference:** the IA closure notes contradict UJ-6 (see Bloat & overspecification).
- **Frontmatter:** both spines have `name`, `status`, `created`, `updated` and `sources`; only DESIGN.md has `description`. The sources resolve.
- **Open assumptions:** 6 `[ASSUMPTION]` tags in DESIGN.md and 23 in EXPERIENCE.md. These block stories: the stale-mode trigger, the 90 s setup timeout, the web-only Accept invitation, the Node "report now" button, and the breakpoints. The Node button and Accept invitation assumptions are now covered by approved resolutions (Spec H-1, H-5). The breakpoints are tied to Accessibility H6 (approved) and the 90 s timeout to Spec L-8 (open).
- **Overlaps across reviewers:** the stale `calibration-live-value` component is raised four times (Token completeness, Component coverage, Spec H-2, Accessibility L6). Orange-button contrast is raised twice (Token completeness medium, Accessibility C2), as are notification permission (State coverage high, Spec C-1) and UJ-6 only in UX (Inheritance medium, Spec L-15). Severity counts below are per reviewer, not deduplicated.
- **Mermaid:** none present.

## Reviewer files

- `review-rubric.md`
- `review-accessibility.md`
- `review-spec-consistency.md`
