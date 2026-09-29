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
- **Garden** shows the Site summary header, "No Readings yet" and the four first-run step tiles
  without actions, with the note that adding a Hub or Node needs the mobile app (Members see the
  read-only note instead). The Site menu sits after the Site tabs and holds only Site settings until
  the Pause story turns Pause/Resume on (`siteMenuItems` in [`src/lib/site-menu.ts`](src/lib/site-menu.ts)).
  Below the first-run tiles, the Site's Lots show as tiles in the Server's order
  ([`LotTiles.svelte`](src/lib/components/LotTiles.svelte)): a *no Node* tile has the dotted border,
  `add`, "+" and "add a Node"; other statuses show the name only until their variants arrive. Tiles
  are not tappable yet.
- **Site settings** (`/settings/site`, first row of the Settings index and the Site menu item).
  [`src/lib/server/site-settings.ts`](src/lib/server/site-settings.ts) loads the current Site's Lots
  ([`src/lib/server/lots.ts`](src/lib/server/lots.ts)) and runs the named actions `renameSite`
  (Owner), `createLot`, `renameLot` and `removeLot` (Owner and Administrator). Controls a Role cannot
  use are hidden; Members see the Lots read-only with one notice. Create Lot keeps one
  `Idempotency-Key` per attempt (kept after a 503 or network failure, replaced after a 422 or
  success). Remove asks in a Modal naming the Lot. A 403 says the change is not allowed on the
  Site; a 409 says to move or unassign the Node first.

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
seed a Lot as holding a Node). `specs/site-settings.spec.ts` renames the Site, creates, renames and
removes Lots, checks the 409 copy and the Member view, with screenshots. `specs/garden.spec.ts` signs in with no Site,
creates "Home" and checks the empty Garden in light and dark at 200 % zoom, with axe and committed
screenshots (Linux Chromium) under `specs/garden.spec.ts-snapshots/`. Run the e2e tests with
`pnpm --filter @coldframe/web-e2e test`; after an intended visual change, refresh the screenshots
with `pnpm --filter @coldframe/web build && pnpm --filter @coldframe/web-e2e exec playwright test --update-snapshots`.
