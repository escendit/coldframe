# @coldframe/web

The Coldframe web app: a SvelteKit 2 backend-for-frontend on `adapter-node` (AD-14). It runs OIDC
Authorization Code + PKCE on the server through
[`@escendit/sveltekit-auth-keycloak`](https://github.com/escendit/sveltekit-extensions). The
browser holds only the package's httpOnly session cookie; access, refresh and ID tokens never reach
page data, HTML or browser storage. Layout data carries only the display name and initials and
the caller's Sites (ID, name, Role, and whether the Site is paused and until when).

How to run it against the local stack is in [`docs/quickstart.md`](../../../docs/quickstart.md#run-the-web-app).

## Configuration

Private environment variables, read only by server modules. A missing required variable stops the
app at startup with a message naming it.

| Variable | Required | Meaning |
| --- | --- | --- |
| `COLDFRAME_SERVER_URL` | yes | The Coldframe Server. SIGN IN first checks `/.well-known/healthz` there; any HTTP answer counts as reachable |
| `KEYCLOAK_ISSUER` | yes | The realm issuer, e.g. `https://id.example.org/realms/coldframe` |
| `KEYCLOAK_CLIENT_ID` | yes | `coldframe-web` |
| `KEYCLOAK_CLIENT_SECRET` | yes | The confidential client's secret |
| `KEYCLOAK_ALLOW_INSECURE_HTTP` | no, default `false` | Allows a plain-HTTP issuer. Only for a local Keycloak |
| `SESSION_COOKIE_SECURE` | no, default `true` | Marks the session cookie `Secure`. Chrome and Firefox accept that on `http://localhost`; set `false` for a browser that does not |

A production build (`pnpm --filter @coldframe/web build`, then `node build` in this folder) also
needs adapter-node's `ORIGIN` (or `PROTOCOL_HEADER`/`HOST_HEADER`) so redirect URIs use the public
address, and `PORT`.

## Sessions live in memory

Sessions are kept in the process (`InMemorySessionStore`); spine open item "Web session store".
Run one replica only. A restart signs everyone out, and on their next request they see "You're
signed out. Sign in again to see live data."

## How sign-in works

- **SIGN IN is a GET** to `/signin/start`. It checks the Server, then the issuer's discovery
  document, each with a 5 s timeout, and only then redirects to the package's
  `/.oidc/signin?redirect_uri=<same-origin path>`. The package answers a non-GET without a session
  with 405.
- **Failures become Inline notices** on `/signin` (UX-DR92). A TLS verification error anywhere →
  the certificate notice, with no action and no bypass. No answer from the Server → the unreachable
  notice with Try again. The issuer failing, Keycloak returning an `error` other than
  `access_denied`, a failed code exchange or a failed discovery → the Keycloak notice with Try again.
  `access_denied` (cancelled) → back to Sign in, no notice.
- **The wrapper, not a fork.** [`src/lib/server/auth-handle.ts`](src/lib/server/auth-handle.ts)
  wraps `OidcMiddleware` and closes its gaps by looking at the request path, the callback query and
  the inner response: bare 400s, one-shot discovery (a failed discovery is retried on the next
  SIGN IN; the session store is shared, so no session is lost) and no expiry signal. Upgrading the
  package stays a version bump.
- **Session end (UX-DR93).** While signed in, the app keeps an httpOnly marker cookie `cf_session`.
  A request that carries it but has no identity (refresh rejected, session expired, store lost)
  clears it and shows "You're signed out. Sign in again to see live data." The shell's guard reads
  the URL, so it runs on every navigation. Signing out from Settings clears the marker first, so it
  shows no notice.
- **Theme.** System (default), Light or Dark in Settings → Appearance applies at once and is kept in
  the first-party cookie `cf_theme` on this browser. The server renders it as `data-theme` on
  `<html>`, so the first paint has no flash; System means no attribute, and `tokens.css` follows
  `prefers-color-scheme`.

## Sites

The web app is the only caller of the Server; the browser never calls it (AD-14), not even for live
updates: see [Live updates](#live-updates-story-66).
[`src/lib/server/sites.ts`](src/lib/server/sites.ts) calls `GET /sites` and `POST /sites` through
[`@coldframe/api-client`](../../../packages/ts/api-client) with the session's access token and turns
every answer into a value (`validation`, `unavailable`, `keyReused`, `unreachable`, `certificate`,
`unauthorized`).

- **Shell load.** Every app page loads the caller's Sites (`GET /sites`, in the Server's order, each
  with the caller's Role from the Server). No Membership → 303 to `/sites/new`. The current Site is
  the first-party httpOnly cookie `cf_site` on this browser; an unknown or stale ID falls back to the
  first Site. The Site tabs in the AppHeader link to `?site=<id>`, which sets the cookie and redirects
  to the same page without the query. A 401 from the Server signs the session out and lands on Sign
  in with the signed-out notice.
- **Create Site** (`/sites/new`). The form carries an `Idempotency-Key` made when it opens; a 503 or a
  network failure keeps it for the retry, a 422 `idempotency-key-reused` replaces it. The name is
  checked (1 to 100 characters after trimming) before any request. The time zone is proposed from
  the browser. It is the User's, not the Site's (AD-11), so the body of `POST /sites` stays `{name}`;
  once the Site exists, a confirmed or picked zone goes to the Server as the User's own choice
  (`PATCH /me/notification-settings` with `timeZone`) in the same action. See
  [Time zone](#time-zone).
- **Garden** shows the Site summary header, the four first-run step tiles without actions with the
  note that adding a Hub or Node needs the mobile app (Members see the read-only note instead), and
  below them the Site's Lots as tiles. The Site menu sits after the Site tabs on every page and holds
  "Pause ‹Site›", or "Resume ‹Site›" while the Server calls the Site paused, for Owners and
  Administrators, then Site settings (`siteMenuItems` in [`src/lib/site-menu.ts`](src/lib/site-menu.ts)).
  See [Lot status](#lot-status), [Pause and Resume](#pause-and-resume-story-82) and
  [Stale mode](#stale-mode).
- **Site settings** (`/settings/site`, the row after My notifications in the Settings index, and the
  Site menu item). [`src/lib/server/site-settings.ts`](src/lib/server/site-settings.ts) loads the
  current Site's Lots ([`src/lib/server/lots.ts`](src/lib/server/lots.ts)) and its Reminder cadence,
  and runs the named actions `renameSite` (Owner), `createLot`, `renameLot`, `removeLot` and
  `setReminderCadence` (Owner and Administrator). Controls a Role cannot use are hidden; Members see
  the Lots and the cadence read-only with one notice. The **Reminders** section is a Segmented choice
  "Daily" / "Every 2 days" that applies at once (each segment submits the form). A save that fails
  shows the Site's value again and a notice with Try again, which sends the same cadence once more.
  After a 503 `reminder-cadence-not-delivered` the Site's value is the pick itself (the Site saved it,
  a member was not reached), so the control shows the pick beside the notice. Create Lot keeps one
  `Idempotency-Key` per attempt (kept after a 503 or network failure, replaced after a 422 or
  success). Remove asks in a Modal naming the Lot. A 403 says the change is not allowed on the
  Site; a 409 says to move or unassign the Node first.

## My notifications (Story 6.3)

`/settings/notifications` (`src/lib/server/notifications.ts`, `src/lib/notifications.ts`), the first
row of the Settings index. It reads `GET /me/notification-settings` and, for the current Site,
`GET /sites/{siteId}/notification-settings` on every load; there is no last-good copy.

| Control | Component | Action | Request |
| --- | --- | --- | --- |
| Notification Window | `NotificationWindow.svelte`: two native time fields, a decorative 24 h bar (`aria-hidden`), the range in large type, and Save | `saveWindow` | `PATCH /me/notification-settings` `{window: {from, to?}}`; an empty To is left out, so the Server ends the window at 22:00 |
| Time zone | `TimeZonePanel.svelte`, the Create Site panel | `chooseTimeZone`, on Confirm or a pick | `PATCH` `{timeZone}` |
| Mute ‹Site› | `Toggle.svelte`: a native checkbox with `role="switch"` | `setMute`, on change | `PUT /sites/{siteId}/notification-settings` |
| My Reminder cadence | `SegmentedChoice.svelte` with a `name`: "Use Site setting" / "Daily" / "Every 2 days"; the helper names the Site setting | `setCadence`, on a segment | the same `PUT` |

- The `PUT` replaces both values (`muted` is required, a missing `reminderCadence` means "use the
  Site setting"), so each of the two forms carries the other value as it is now.
- Mute and cadence need a current Site. The window and the time zone are the User's own.
- The window is checked before it is sent (`HH:mm`, start before end); the reason sits under the
  field and what was typed stays. The Server stays the validator.
- A save that fails leaves the data as the Server last sent it, and the controls are built again
  from it. The notice (`RetryNotice.svelte`) has Try again where repeating can help: it is a form
  that sends the same fields again. A certificate failure, a refused value and a Site that is no
  longer the caller's have no Try again. A 401 signs out.
- Below the Site's controls sits "Browser notifications while Coldframe is open", which is this browser's
  own and no form of the page: see [Browser notifications](#browser-notifications). It shows only with a
  current Site.
- Not here: the notifications-off notice (Story 6.5), which is the phones'.

### Time zone

The time zone lives on the Server. The Time-zone confirm panel shows until the User chose one
(`timeZoneConfirmed`); the proposal is the browser's zone, else the zone the Server detected, else
none and the list is all there is. A chosen zone is never proposed over.

The browser never calls the Server, so the web app's server hands the zone over
(`handOverTimeZone` in [`src/lib/server/shell.ts`](src/lib/server/shell.ts)), on the first shell
load of a browser session that listed the Sites:

| Cookie | Written by | Holds |
| --- | --- | --- |
| `cf_time_zone` | the server, httpOnly, 1 year | the zone the User chose, as the Server holds it; the pages format times with it |
| `cf_browser_zone` | the page (root layout), session | the browser's own zone, which no request header carries |
| `cf_zone_sync` | the server, httpOnly, session | the User ID the hand-over is done for |

1. Read `GET /me/notification-settings`.
2. While the User has chosen no zone there: a valid `cf_time_zone` (confirmed on Create Site before
   this story) is sent as `timeZone`; otherwise `cf_browser_zone` is sent as `detectedTimeZone`,
   unless the Server already holds that zone. With neither, nothing is marked and the next load asks again.
3. Once the Server answered (a 400 included) `cf_zone_sync` is set, and `cf_time_zone` follows the
   Server: the chosen zone, or no cookie while none is chosen. No answer, a 5xx or an answer outside
   the contract keeps the copy, and the next load tries again.

A `cf_zone_sync` of another User means `cf_time_zone` was theirs: it is not sent as this User's
choice. Signing out from Settings clears `cf_time_zone` and `cf_zone_sync`. Create Site and the
Time-zone panel on My notifications also set `cf_time_zone` from the Server's answer; when the
Create Site send gets no answer, the zone is kept in the cookie and `cf_zone_sync` is cleared, so
the next load hands it over.

## Lot status

The Server computes each Lot's status, its order and `statusSince` (AD-14). The web app only
renders them: nothing here computes or re-sorts a status. It counts statuses for the headline and
tells durations from the Server's timestamps and the browser's clock.

- **Tiles.** [`src/lib/lot-tiles.ts`](src/lib/lot-tiles.ts) turns a `Lot` into a tile model (icon,
  label, value, foot line, spoken label, soil level and low tick), and
  [`LotTiles.svelte`](src/lib/components/LotTiles.svelte) draws it. A status string the client does
  not know renders as `unknown`.

  | Status | Shape and icon | Value | Foot |
  | --- | --- | --- | --- |
  | `needsWater` | solid orange, soil level, low tick, `rain-drop` | `~20` | Reading time · low Threshold |
  | `ok` | neutral fill, soil level, 1 px solid, `checkmark--outline` | `~35` | the parts the Server sent |
  | `unknown` | hatch, 1 px dashed, `help`; "Silent" or "Hub silent" from `unknownCause` | silence since `lastReadingAt` (Node) or `statusSince` (Hub, or no Reading) | what it last read, or "no Readings yet" |
  | `needsCalibration` | hatch, 2 px dashed yellow, `tools` | `raw` | "no % until calibrated" |
  | `paused` | flat purple fill, 2 px solid, `pause--outline`; "Paused by Site" when `pausedBy` has `site` | `—` | "until ‹date›" or "paused" |
  | `noNode` | empty, 1 px dotted, `add` | `+` | "add a Node" |

  Soil moisture is `~` and the nearest 5; an uncalibrated Lot never shows a percentage. Each tile is
  one accessibility element with the whole label ("Tomatoes, needs water, about 20 percent, low 30
  percent, Reading 7:02 AM"). Tiles are not tappable yet. The grid has 1 column below 400 px of its
  own width, then 2, 3 (from 672 px) and 4 (from 1056 px); in one column the value sits directly
  under the status label.
- **Hatch.** [`Hatch.svelte`](src/lib/components/Hatch.svelte) is the reusable hatch fill: an inline
  SVG pattern coloured by the hatch tokens (CSS gradients are kept for the Sign-in surface). With
  `plate` its children sit on a solid plate of the hatch ground.
- **Headline.** [`src/lib/garden.ts`](src/lib/garden.ts) picks the first that applies: no Lot has a
  Node → "No Readings yet"; any needs water → "Tomatoes needs water" or "2 Lots need water"; any
  unknown or needing calibration → "2 Lots can't be read"; every Lot with a Node paused by the Site →
  "Paused until ‹date›" (when they share one end) or "Paused", in the paused ink; otherwise "Nothing
  needs water". The subline counts the other statuses in the Server's order.
- **Hub silent.** `GET /sites/{siteId}/lots` also lists the Site's silent Hubs (`silentHubs`). Each is one
  Inline notice above the tiles, "Hub 3F2A silent for 12 min — Lots behind it can't be read.", also when no
  Lot is behind that Hub (`hubNotices` in [`src/lib/garden.ts`](src/lib/garden.ts)). It has no action and
  cannot be dismissed, and it is announced politely once each time it appears, not while it stays over a
  refetch. It goes when the Server stops listing the Hub. Stale mode shows none.
- **Silence.** Every silence duration (tile, hero, notice, Devices row, Alert title) is the browser's or the
  load's clock minus the Server's `silentSince`, rounded down by `formatDuration`: minutes under 1 h, hours
  under 24 h, then days. A Hub, and a Node on no Lot in an Alert title, is named by the first four hex
  digits of its Device ID in upper case (`shortDeviceId`); Device rows show the full ID.
- **Refetch on focus.** Garden loads again when its tab gets focus or becomes visible, and when the
  Server hints that something it shows changed ([Live updates](#live-updates-story-66)). There is no
  polling.

## Lot detail, History and Nodes

Every Lot tile is one link to `/garden/{lotId}` (UX-DR63), the no-Node tile included: on the web it opens
a detail that says "Add a Node from the mobile app." (the BLE flow is mobile only). The page's server load
(`src/lib/server/lot-detail.ts`) reads `GET /sites/{siteId}/lots/{lotId}` (the Lot with `node` and
`sensors`) and one `GET .../history?quantity=...` per quantity the Node reports (soil moisture alone
without Readings), all in the default 30-day window. Like the overview, a refresh and one retry that fail
serve the last good detail of that user, Site and Lot as stale (header, "Was OK", no value in the hero,
Sensor cells or Device cells), and a 404 is a notice. The shell treats `/garden/...` like the overview
for the Sites it falls back to.

The page only renders. Values arrive converted by the Server (soil `raw N`, `°C`, `%`, `kΩ`); the web
formats them (`src/lib/lot-detail.ts`: whole numbers, kΩ to 3 significant digits, durations and times
from Server timestamps) and computes nothing else. The hero shows the soil Reading as `raw N` while the
Lot needs calibration, otherwise the Server's `moisturePercent` when it sends one. The History chart
(`HistoryChart.svelte`) is inline SVG: 30 UTC days ending on the day of the page's clock, one outlined bar
per day with Readings (the daily low), gaps for the others, the picked bar solid, a tap, drag or arrow
key picks a day, the text summary is the label of the image, and nothing animates. With a low Threshold on a
soil-moisture History the Server sent in `%` (calibrated Readings), the chart also draws the band
(`chart-band`), a 2 px low line, a dashed 1 px high line (`chart-high-line`) and a solid
`chart-bar-below-low` bar for each day whose low is under the low, with the legend "solid bar = below N %"
and the below-low days in the text summary; a raw History has none of it. Under the hero, Owners and Administrators have the action strip with Pause or Resume of the Lot's Node ([Pause and Resume](#pause-and-resume-story-82)). A Lot behind a silent Hub
names it from the Lot's `silentHubId` ("Hub 3F2A is silent; Lots behind it can't be read."); without one the copy has no ID. The Devices list is not
read for it.

Devices lists "Hubs", then "Nodes" in the Server's order (Lot name, unassigned last, then Device ID) with
Lot name, last seen, battery (`battery--low` below 20 %) and charging from the Server. Owners and Administrators
move and unassign Nodes (Story 4.9) and set the Silence Window of any Device ([below](#silence-on-devices-story-72)). The Playwright fake Server (`fixtures/fake-idp.ts`) seeds `node`, `sensors`, `history` and
the Node fields; its Lot reads and the history fail with the rest of the Lots reads.

### Thresholds (Story 5.4)

`/garden/{lotId}/thresholds` (`src/lib/server/thresholds.ts`, `src/lib/thresholds.ts`,
`ThresholdColumn.svelte`) reads `GET /sites/{siteId}/sensors/{sensorId}/thresholds` for each Sensor of the Lot
(a Member may) and saves with `PUT` through the `save` form action (only the changed sides are sent; the
Server stays the only validator, the client check of "Low must stay below high." only gates Save). It is
reached from Lot detail ("Set Thresholds", "View Thresholds" for a Member), a Sensor cell and the Calibration
confirmation. Each Sensor is a Threshold column: a vertical track, the low line, the current Reading marker, a
dashed "no high" marker, values to the right; a percentage track (calibrated soil moves in 5 % steps) can be
dragged or typed, other Sensors are typed or moved with the arrow keys. A Member sees the values read-only with
no control. Lot detail also reads the calibratable soil Sensor's Thresholds for the chart band and the summary
line; a failed read leaves them out. The fake Server has `thresholds` seeds, `PUT` recording and
`/control/lot-fields` (`patchLot`).

### Silence on Devices (Story 7.2)

A Device with an open silent Alert carries `silentSince`: its row shows the `help` icon and "Silent 6 h"
(upper case by style) beside last seen. Every row has the id `device-{deviceId}`, which the Health Alert rows
link to; the row a link opened has a stronger edge.

**Silence Window** is a row action of every Hub and Node for Owners and Administrators; a Member has no such
control (hidden, never disabled) and still sees the silence and last seen. The closed row says the window in
force ("Silence Window · 6 h", from `silenceWindowSeconds`). Opened, it is a number with a unit of min, h or
d, Save, and "Use default" while the Device has its own window (`silenceWindowOverridden`). The form action
`setSilenceWindow` (`silenceWindowAction` in [`src/lib/server/devices.ts`](src/lib/server/devices.ts)) sends
`PUT /sites/{siteId}/devices/{deviceId}/silence-window` with `{windowSeconds}`, or `{}` for the default.

| Case | What the row shows |
| --- | --- |
| Not a whole number, or outside 1 min to 30 days | "Enter a whole number from 1 min to 30 d." under the field, what was typed stays, and nothing is sent (`silenceWindowSeconds` in [`src/lib/devices.ts`](src/lib/devices.ts), in the browser and again in the form action) |
| 400 from the Server | the same message |
| 403: the caller lost the Role after the page loaded | "You can't change this on ‹Site›. Ask an Owner or Administrator." |
| 404 | "That Device is gone. Reload the page." |
| No answer, an untrusted certificate, any other status | the notices of move and unassign |

The page holds what is typed and which forms are open, so a refetch or a refusal never replaces it.

### Pause and Resume (Story 8.2)

Whether a Site or a Device is paused is the Server's: `Site.paused` / `pausedUntil`, `DeviceListItem.pausedBy` /
`pausedUntil`, and `Lot.status` / `pausedBy` / `pausedUntil`. The web app renders those members and neither computes
nor remembers a Pause ([`src/lib/pause.ts`](src/lib/pause.ts)). Pause and Resume are for Owners and Administrators;
a Member has no such control anywhere (hidden, never disabled).

| Surface | Paused state | Controls (Owner, Administrator) |
| --- | --- | --- |
| Site menu, every page | "Resume ‹Site›" in place of "Pause ‹Site›" while `Site.paused` | Pause opens the Pause modal with Whole Site; Resume is sent at once. Disabled with "Needs your Server" in stale mode |
| Overview | The headline keeps its Lot rule ("Paused until 1 Mar" or "Paused" in paused ink when every Lot with a Node is paused by the Site); the line "No Alerts are sent while the Site is paused." follows `Site.paused`, so a Site without Nodes says it too; tiles read "Paused by Site" | the Site menu |
| Lot detail | the hero ("Paused until ‹date›", "Paused with the Site") | the action strip under the hero: "Resume 7C19" while `pausedBy` has `device`, else "Pause". Paused by the Site only: the hero says "Resume the Site to resume this Node", and Pause stays |
| Devices rows | the pause icon with "Paused" or "Paused by Site" in paused ink, and "until ‹date›"; `data-paused` lists the sources | the same pair per row; paused by the Site only, the row says "Resume the Site to resume this Node" ("… this Hub") |

- **The Pause modal** ([`PauseModal.svelte`](src/lib/components/PauseModal.svelte)) is a native `<dialog>` named "Pause"
  that takes the one modal level: the scope as a Segmented choice (This Device, selected, and Whole Site from a Device;
  Whole Site alone from the Site menu), the optional "Until" date (the browser's date field) with "Leave empty to pause
  until someone resumes.", Cancel, and a primary button that names the result and follows the scope ("Pause 7C19",
  "Pause Home garden"). Whole Site chosen from a Device pauses the Site and leaves the Device's own Pause alone.
- **Form actions.** The browser never calls the Server: the modal and the Resume form
  ([`ResumeControl.svelte`](src/lib/components/ResumeControl.svelte)) post `siteId`, `scope`, `deviceId` and `until` to
  `?/pause` and `?/resume`, which the overview, Lot detail and Devices all carry (`pauseActions` in
  [`src/lib/server/pause.ts`](src/lib/server/pause.ts)); the Site menu posts to the overview's from any page. They call
  `PUT` / `DELETE /sites/{siteId}/pause` and `…/devices/{deviceId}/pause`. `until` goes out as the calendar date that was
  picked (`YYYY-MM-DD`); the Server resolves it in the User's time zone, and the pages format `pausedUntil` with the day
  formatter in the zone they show times in, so where the two zones differ the shown day can be one off.
- **After a save** the page reads its data again and shows what the Server says; a Site Pause reaches the Lots and
  Devices through delivery, and the live hints refetch them.

| Answer | What shows |
| --- | --- |
| 400 for the date (or a date that is no calendar date, refused before it is sent) | the modal stays open, the date field is marked: "Choose a date after today." |
| 403: the caller lost the Role after the page loaded | "You can't change this on ‹Site›. Ask an Owner or Administrator." |
| 404 | "That Device is gone. Reload the page." (a Site that is gone: "‹Site› is no longer one of your Sites. Nothing was changed.") |
| No answer, any other status | the notice of the Devices actions with Try again, which sends the same again; an untrusted certificate has no Try again |

A refused Pause keeps the modal and what was entered, with the notice inside it. A Resume that did not happen leaves its
notice beside its button, or above the page when it came from the Site menu. A Member on a paused Site and on a paused
Lot gets one Inline notice, "Only Owners and Administrators can resume it." Stale mode shows no Pause control on Lot
detail and neither the line nor that notice on the overview. Who paused the Site is not shown: the Server stores no
attribution. The Add a Hub and Add a Node outcome screens are mobile only, and Calibrate has no Resume.

### Alerts (Story 6.2)

`/alerts` (`src/lib/server/alerts.ts`, `src/lib/alerts.ts`, `AlertRows.svelte`) reads
`GET /sites/{siteId}/alerts` on every load and on focus: 200 Threshold Alerts per page (`alerts`), following
`nextCursor` to the end or to 10 pages (`alertsPageCap`), and a page that fails fails the whole read. The
Health Alerts come beside them in `healthAlerts` (Story 7.2): not paged, taken from the first page only. The
Server's order is kept in both lists and nothing is re-sorted; the page only groups: "Threshold Alerts",
"Health Alerts", then "Closed" (what the Server still lists, closed in the last 7 days: the closed Threshold
Alerts, then the closed Health Alerts). With no open Alert it says "No open Alerts.", followed by Closed when
it has rows. A Server that sends no `healthAlerts` gives the page it gave before.

`alertRow` picks one of four variants (DESIGN.md `alert-row-*`): needs water only for an open, low-side,
soil-moisture Threshold Alert (the only orange row, `rain-drop`); any other open Threshold Alert on
`layer-01` with a 2 px border and `arrow--down` / `arrow--up`; a Health Alert hatched and dashed with its
text on a plate (`help`, `battery--low`, `tools`); a closed Alert as an outline, whatever it was. A row
shows the Lot, the condition and when it started, never a value or a Threshold (the Alert carries none).
Each row is one link with one spoken label ("Tomatoes needs water, since 5:45 AM"): a Threshold or
uncalibrated Alert opens Lot detail; a silent Device, a low battery and a kind this client does not know
open Devices at the row of the Device (`/devices#device-{deviceId}`). Rows have no other action.

Health rows (`healthAlertRow`) are titled from the Alert's Device, Lot, `silentSince` and `batteryPercent`:

| Alert | Title |
| --- | --- |
| `silent`, a Node on a Lot | "Node on Lot 'Beans' silent for 6 h" |
| `silent`, a Hub | "Hub 3F2A silent for 5 min" |
| `silent`, a Node on no Lot | "Node 9C0D silent for 6 h" |
| closed, or open without `silentSince` | the same without "for …" ("Hub 3F2A silent") |
| `battery`, a Node on a Lot (Story 7.3) | "Node battery 14 %, not charging — Lot 'Lettuce'" |
| `battery`, a Node on no Lot | "Node 9C0D battery 14 %, not charging" |
| `battery`, closed, or without a `batteryPercent` from 0 to 100 | "Node battery low on Lot 'Lettuce'" / "Node 9C0D battery low" |
| `uncalibrated` (Story 7.4) | "Soil Sensor on Lot 'Peppers' needs Calibration", open and closed; the tools icon; it opens Lot detail of that Lot |
| a kind this client does not know | the neutral row: "Health Alert on Lot 'Basil'", or "Health Alert" without a Lot |

The Server opens `silent` (Story 7.1), `battery` (Story 7.3) and `uncalibrated` Alerts (Story 7.4). The level in
a low-battery title is the one the Alert opened with, so a closed row names none; Devices and Lot detail show the
Node's newest level and charger status, as before. An `uncalibrated` Alert is about a soil Sensor of a Node on a
Lot: its row leads to Lot detail (`/garden/{lotId}`), where Calibrate is offered to an Owner and an Administrator
and not to a Member, and the row is a Closed row once the Calibration is saved or the Node left the Lot. Its
notification is worded by the Server ("Soil sensor on Lot 'Peppers' needs calibration"); the row keeps the
glossary's capitals. A non-Threshold entry in `alerts`, which the Server no longer sends, still renders as
the Health row it was (it opens Devices without a row).

There is no Open Alerts rail, no count in the side nav and no stale mode: a failed read shows the notice
with Try again and no rows. The fake Server has `POST /control/alerts` (`setAlerts`: the Alerts, the Health
Alerts, or a status for their list) and serves the Alerts in pages.

## Live updates (Story 6.6)

While a tab is open, the Server tells it two things over one SignalR connection: `readmodel.changed
{ resource, id, version }`, a hint without domain data after which the page reads again over REST, and
`notification.delivered`, a notification that is due for this User. The message shapes are the contract
in [`packages/asyncapi`](../../../packages/asyncapi); their types and names come generated from
`@coldframe/api-client` (`liveHubPath`, `liveMessages`, `ReadModelChanged`, `NotificationDelivered`).

- **The BFF route.** The browser talks only to the web origin (AD-14):
  [`src/routes/(app)/live/[...path]/+server.ts`](<src/routes/(app)/live/[...path]/+server.ts>) hands every
  request to `proxyLive` in [`src/lib/server/live.ts`](src/lib/server/live.ts), which forwards the hub's own
  requests to `${COLDFRAME_SERVER_URL}/live` and nothing else:

  | Request | What it is |
  | --- | --- |
  | `POST /live/negotiate` | SignalR's negotiation; the answer carries the connection token |
  | `GET /live?id=…` | the Server-Sent Events stream (or one long poll) |
  | `POST /live?id=…` | what the client sends: the handshake and keep-alive pings |
  | `DELETE /live?id=…` | the end of a long-polling connection |

  It adds `Authorization: Bearer <the session's access token>` and passes on only `accept`,
  `content-type`, and the query parameters `id` and `negotiateVersion`: no cookie, no other header, never a
  token in a URL. The Server's status and body are passed through. Without a session the answer is 401 and
  the Server is not asked (the auth handle makes that a 401 also where the OIDC package would answer 405 or
  redirect); any other path is 404, any other method 405, a body over 64 KiB 413, and a Server that does
  not answer 502. The stream has no time limit (the other requests keep the 10 s bound of every Server
  call), and the upstream request is aborted when the browser leaves.
- **No WebSockets.** SvelteKit on `adapter-node` cannot upgrade a connection, so the hub is used over
  Server-Sent Events, with long polling as the fallback; both are plain HTTP.
- **A reverse proxy must not buffer the stream.** The route answers `text/event-stream` with
  `Cache-Control: no-store` and `X-Accel-Buffering: no`. An ingress in front of the web app has to hand
  events on as they come (nginx: `proxy_buffering off` for `/live`, and a read timeout above the hub's
  15 s keep-alive); behind a buffering proxy the connection times out and is opened again every 30 s, and
  nothing arrives.
- **One connection per tab.** The `(app)` layout mounts it once (`mountLive` in
  [`src/lib/live`](src/lib/live)); `hub.ts` holds the `@microsoft/signalr` client, loaded in the browser
  only, and `live.ts` decides when to connect again. The hub has no method a client can call.
- **Which page reads again** (`showsResource` in [`src/lib/live/rule.ts`](src/lib/live/rule.ts)); the
  page's data is loaded again with `invalidateAll()`, without a reload and without animation:

  | `resource` | `id` | Pages |
  | --- | --- | --- |
  | `sites` | Site | every app page |
  | `lots` | Lot | the overview; Lot detail and its sub-pages of that Lot |
  | `readings` | Lot | the overview; Lot detail of that Lot |
  | `alerts` | Alert | Alerts; the overview |
  | `devices` | Device | Devices; the overview (a Device that turns silent or recovers changes the Hub notice and the tiles) |

  A hint for anything else is dropped. Hints that arrive while a refetch runs cause one more refetch, not
  one each. A refetch that fails is quiet: the page keeps its data and its own stale handling.
- **Losing the connection** shows nothing: no notice, no indicator. It is opened again at once, then
  after 2 s, 5 s, 10 s and every 30 s, and the open page refetches once when it is back, because hints may
  have been missed. The Server closes a connection when its access token expires; the next negotiation
  goes through the web app again and carries the refreshed token. A 401 means signed out: nothing is
  tried again until the next navigation, where the shell signs out if the session is gone.

### Browser notifications

"Browser notifications while Coldframe is open" on My notifications
([`src/lib/browser-notifications.ts`](src/lib/browser-notifications.ts)) belongs to this browser, like
the permission it follows. It is `localStorage` key `cf_browser_notifications` (`on`, or absent), off by
default, and never reaches the Server: the Server sends every delivery to every open tab of the User, and
each browser decides whether to show it. Window, mute and timing were applied on the Server before.

| Turning it on | Result |
| --- | --- |
| The browser asks and the User allows, or it was allowed before | on, and kept for this browser |
| The browser blocks Coldframe, the User refuses, or it has no notifications | back to off, with "Your browser blocks notifications for Coldframe. Allow them in this site's browser settings, then turn this on again." (UX-DR89) |
| The prompt is closed without an answer | back to off, no notice |

The permission prompt appears only from that change of the toggle. When the permission is taken away
later, the toggle renders off and the choice is dropped, so allowing it again needs the toggle too. A
delivery is shown only while the toggle is on and the permission granted, as `new Notification(title,
{ body, tag })` with the Server's title and body (the push text) and the delivery's `collapseId` as
`tag`: two tabs or a repeated delivery replace the notification instead of stacking. A click focuses the
tab and opens `/garden/{lotId}?site={siteId}` for an Alert or a Reminder and `/garden?site={siteId}` for
a summary or a kind this client does not know. The page never writes a notification of its own, and
with no tab open nothing is delivered: there is no service worker and no web push.

## Stale mode

When the Server cannot be reached, the Site overview shows the last good data and says how old it
is. The browser never calls the Server, so that data is kept in the web app's process
([`src/lib/server/last-good.ts`](src/lib/server/last-good.ts)): the Sites per user and the Lots per
user and Site, at most 500 entries (least recently used first out), in memory only. Like the
sessions, it is lost on a restart and not shared between replicas; without it the existing
unreachable notice shows.

- **Entering.** A Sites or Lots read that fails for transport reasons (no answer, a 5xx, or an
  answer outside the contract) is retried once. When the retry fails too, the last good value is
  served with the time it was read. When the Sites already came from the last good answer, the
  Lots are not asked for again. A 401 signs out, and a 403 or 404 drops what was kept. An untrusted
  certificate keeps its own notice and is never retried.
- **What it shows.** The stale header replaces the summary header ("Home garden · can't reach your
  Server", the age, "Last data 7:02 AM. …"); every tile is the stale variant ("Was needs water",
  "as of 7:02 AM", outline only, `cloud--offline`); the Site menu items are disabled with "Needs
  your Server". Both times are the last successful read. The age ticks once a minute in the browser,
  the only timer that changes what a page shows (the hub connection waits before it is opened again), and is never announced.
- **Leaving.** The next load that succeeds is live again. Loads happen on navigation and on focus;
  nothing retries in the background.
- **The web app itself out of reach.** A laptop away from home cannot reach the web app at all, and
  loading the page again would replace the overview with an error page. So on focus the page first
  asks for `/_app/version.json` (a static file; nothing is read from the Server), once more after a
  network error. When both fail it does not load again: the shown Lots stay as stale, "as of" the
  last successful load (or the time the data was already stale from), announced like any entry,
  with the Site menu disabled. The next focus that reaches the web app loads again and leaves stale
  mode. The decision is `onFocusDecision` in [`src/lib/garden.ts`](src/lib/garden.ts).
- **Announcements.** Entering ("Can't reach your Server. Showing data from 7:02 AM.") and leaving
  ("Live again.") are announced politely, once per change.
- **Only the overview.** Devices, Site settings and the other pages have no stale mode: they read
  once and show their notice.

## Copy

Every user-visible string, including `<title>` and `aria-label`, comes from
[`src/lib/i18n/en.json`](src/lib/i18n/en.json) through `t(key, params)`. Plural entries are
`{ one, other }` and are picked with `Intl.PluralRules`. Uppercase is CSS only. Glossary terms
([`glossary.json`](src/lib/i18n/glossary.json)) are capitalised.

The unreachable notice says "this phone" on the web too: EXPERIENCE.md has no web variant of the
UX-DR92 copy, so the catalogue uses it verbatim until the design decides one.

## Tests

Unit tests are in [`tests/ts/web`](../../../tests/ts/web), end-to-end tests in
[`tests/ts/web.e2e`](../../../tests/ts/web.e2e). See the quickstart.

The e2e fake IdP doubles as an in-memory Server (`GET /sites`, `POST /sites`, `PATCH /sites/{id}`
and the Lot routes; `POST /control/sites` resets or seeds the Sites, one Site by default, and can
seed a Lot as holding a Node or with a whole status; `POST /control/reads` makes the Sites and Lots
reads drop the connection after they succeeded). `specs/lot-status.spec.ts` seeds one Lot per tile
variant and compares screenshots of the live and the stale overview in light and dark at 1, 2, 3 and
4 columns, with axe; the stale ones have the time of the run replaced before they are compared. `specs/site-settings.spec.ts` renames the Site, creates, renames and
removes Lots, checks the 409 copy and the Member view, with screenshots. `specs/garden.spec.ts` signs in with no Site,
creates "Home" and checks the empty Garden in light and dark at 200 % zoom, with axe and committed
screenshots (Linux Chromium) under `specs/garden.spec.ts-snapshots/`. `specs/alerts.spec.ts` seeds one Alert per row
variant and cause and compares screenshots of the list, the empty state and the only-closed state in light
and dark, with axe. `specs/notifications.spec.ts` sets the window, the zone, the mute switch and the cadence, checks
the failed-save and time-zone hand-over cases, and compares screenshots of My notifications (zone unconfirmed, then
everything set) in light and dark, with axe; the fake Server has the three notification-settings resources and
`POST /control/notifications` (`setNotifications`: seeds them, and the status the next write answers). On the web
My notifications is never shown without a Site (no Membership opens Create Site), so that state has unit tests only. `specs/silence.spec.ts` (Story 7.2) seeds
silent Devices (`silentSince` on a fake Device; a silent Hub is also in the Lots list's `silentHubs`), Lots with `silentHubId` and `silentSince`, and Health Alerts, and
compares screenshots in light and dark of the overview with the Hub notice, of Devices with a silent Hub and Node and the open Silence Window form, and of Alerts with the
Health rows; it saves a Silence Window, returns it to the default, checks the refused range, the 403 notice and that a Member has no control. The fake Server has
`PUT /sites/{id}/devices/{id}/silence-window` (403 for a Member, 400 outside the range or for a malformed Device ID, 404 for an unknown Device) and lists what it accepted as
`silenceWindowPuts`. Devices and Alerts tell a silence against the web app's clock, so those tests seed times relative to the run and fix the last-seen text before comparing. `specs/live.spec.ts` runs the live updates
against a fake SignalR hub in the fake Server (negotiate, a Server-Sent Events stream, the handshake by POST; `POST /control/live` sends a hub message to every open
connection, drops them, or makes the hub answer 401, and `GET /control/live` lists what the hub was asked): the hub is reached on the web origin only and with the
session's token, a hint refetches the page that shows it without a reload, a lost connection comes back and refetches once, a 401 ends the retries until a navigation,
and the browser toggle, the blocked notice and a delivered notification are checked with a stand-in for the browser's Notification API, with screenshots of
My notifications with the toggle on and with the blocked notice in light and dark. `specs/pause.spec.ts` (Story 8.2) pauses and resumes the Site from the Site menu and a
Device from its Devices row and from Lot detail, checks the refused date, the 403, the 404 and a Server that does not answer, and what a Member sees, and compares
screenshots in light and dark of the Pause modal (Whole Site, a Device, a refused date), the paused overview, the paused Devices rows, Lot detail with Resume and with
"Resume the Site to resume this Node", and the Member's overview and Lot detail. The fake Server has the four pause endpoints (403 for a Member, 400 for an end that
is no date or has passed, 404 for an unknown Device), sends `paused` on a Site and `pausedBy` on a Device, reads a Lot whose Node is paused as `paused`, lists every
pause write as `GET /control/pause`, and `POST /control/pause` (`failNextPause`) makes the next one answer a status or drop the connection. Run the e2e tests with
`pnpm --filter @coldframe/web-e2e test`; after an intended visual change, refresh the screenshots
with `pnpm --filter @coldframe/web build && pnpm --filter @coldframe/web-e2e exec playwright test --update-snapshots`.
