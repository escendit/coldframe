# @coldframe/web

The Coldframe web app: a SvelteKit 2 backend-for-frontend on `adapter-node` (AD-14). It runs OIDC
Authorization Code + PKCE on the server through
[`@escendit/sveltekit-auth-keycloak`](https://github.com/escendit/sveltekit-extensions). The
browser holds only the package's httpOnly session cookie; access, refresh and ID tokens never reach
page data, HTML or browser storage. Layout data carries only the display name and initials and
the caller's Sites (ID, name, Role).

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

The web app is the only caller of the Server; the browser never calls it (AD-14).
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
  below them the Site's Lots as tiles. The Site menu sits after the Site tabs and holds only Site
  settings until the Pause story turns Pause/Resume on (`siteMenuItems` in
  [`src/lib/site-menu.ts`](src/lib/site-menu.ts)). See [Lot status](#lot-status) and
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
- Not here: the "Browser notifications while Coldframe is open" toggle (Story 6.6) and the
  notifications-off notice (Story 6.5).

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
- **Refetch on focus.** Garden loads again when its tab gets focus or becomes visible. There is no
  polling and no SignalR yet.

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
and the below-low days in the text summary; a raw History has none of it. There is no admin strip before Epic 8. A "Hub is silent" Lot names
the first Hub of the Devices list, since the Lot does not carry the Hub (no Server producer yet).

Devices lists "Hubs", then "Nodes" in the Server's order (Lot name, unassigned last, then Device ID) with
Lot name, last seen, battery (`battery--low` below 20 %) and charging from the Server; no row actions
until Story 4.9. The Playwright fake Server (`fixtures/fake-idp.ts`) seeds `node`, `sensors`, `history` and
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

### Alerts (Story 6.2)

`/alerts` (`src/lib/server/alerts.ts`, `src/lib/alerts.ts`, `AlertRows.svelte`) reads
`GET /sites/{siteId}/alerts` on every load and on focus: 200 Alerts per page, following `nextCursor` to
the end or to 10 pages (`alertsPageCap`), and a page that fails fails the whole read. The Server's order is
kept; the page only groups it: "Threshold Alerts" (kind `threshold`), "Health Alerts" (every other kind,
also one this client does not know), then "Closed" (the Alerts the Server still lists, closed in the last
7 days). With no open Alert it says "No open Alerts.", followed by Closed when it has rows.

`alertRow` picks one of four variants (DESIGN.md `alert-row-*`): needs water only for an open, low-side,
soil-moisture Threshold Alert (the only orange row, `rain-drop`); any other open Threshold Alert on
`layer-01` with a 2 px border and `arrow--down` / `arrow--up`; a Health Alert hatched and dashed with its
text on a plate (`help`, `battery--low`, `tools`); a closed Alert as an outline, whatever it was. A row
shows the Lot, the condition and when it started, never a value or a Threshold (the Alert carries none).
Each row is one link with one spoken label ("Tomatoes needs water, since 5:45 AM"): a Threshold or
uncalibrated Alert opens Lot detail, a silent Node or a low battery opens Devices, and a kind this client
does not know opens Devices. Rows have no other action. Health Alerts have no Server producer before
Epic 7; tests and the fake Server seed them.

There is no Open Alerts rail, no count in the side nav and no stale mode: a failed read shows the notice
with Try again and no rows. The fake Server has `POST /control/alerts` (`setAlerts`: the Alerts, or a
status for their list) and serves them in pages.

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
  the only timer in the app, and is never announced.
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
My notifications is never shown without a Site (no Membership opens Create Site), so that state has unit tests only. Run the e2e tests with
`pnpm --filter @coldframe/web-e2e test`; after an intended visual change, refresh the screenshots
with `pnpm --filter @coldframe/web build && pnpm --filter @coldframe/web-e2e exec playwright test --update-snapshots`.
