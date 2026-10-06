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
  the browser and, once confirmed or picked, stored only in the httpOnly cookie `cf_time_zone`; it is
  never sent to the Server (AD-11), so the request body stays `{name}`.
- **Garden** shows the Site summary header, the four first-run step tiles without actions with the
  note that adding a Hub or Node needs the mobile app (Members see the read-only note instead), and
  below them the Site's Lots as tiles. The Site menu sits after the Site tabs and holds only Site
  settings until the Pause story turns Pause/Resume on (`siteMenuItems` in
  [`src/lib/site-menu.ts`](src/lib/site-menu.ts)). See [Lot status](#lot-status) and
  [Stale mode](#stale-mode).
- **Site settings** (`/settings/site`, first row of the Settings index and the Site menu item).
  [`src/lib/server/site-settings.ts`](src/lib/server/site-settings.ts) loads the current Site's Lots
  ([`src/lib/server/lots.ts`](src/lib/server/lots.ts)) and runs the named actions `renameSite`
  (Owner), `createLot`, `renameLot` and `removeLot` (Owner and Administrator). Controls a Role cannot
  use are hidden; Members see the Lots read-only with one notice. Create Lot keeps one
  `Idempotency-Key` per attempt (kept after a 503 or network failure, replaced after a 422 or
  success). Remove asks in a Modal naming the Lot. A 403 says the change is not allowed on the
  Site; a 409 says to move or unassign the Node first.

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
screenshots (Linux Chromium) under `specs/garden.spec.ts-snapshots/`. Run the e2e tests with
`pnpm --filter @coldframe/web-e2e test`; after an intended visual change, refresh the screenshots
with `pnpm --filter @coldframe/web build && pnpm --filter @coldframe/web-e2e exec playwright test --update-snapshots`.
