---
title: 'Story 1.4: Sign in on the web'
type: 'feature'
created: '2026-09-28'
status: 'done'
baseline_revision: '2ce4dd54f80812a48b060aa9512e817c1ec65a1c'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/EXPERIENCE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/DESIGN.md'
warnings:
  - oversized
deferred:
  - summary: >-
      No test drives a real authorization-code exchange between apps/ts/web and the coldframe realm in Keycloak; e2e tests use a fake OIDC provider and the realm is checked only as configuration.
    evidence: |-
      Unverified (maybe-false). KeycloakTests.cs asserts the discovery document and the coldframe-web client settings; every browser test targets tests/ts/web.e2e/fixtures/fake-idp.ts. To settle it, sign in to the web app against the Aspire stack's Keycloak (docs/quickstart.md "Run the web app") with a registered user, or add an AppHost-hosted e2e run.
    location: >-
      aspire/keycloak/realms/coldframe-realm.json, tests/ts/web.e2e/fixtures/fake-idp.ts
    severity: medium (unverified)
  - summary: >-
      The CI workflow, including the new Playwright install and report-upload steps, has not run on GitHub; it is verified only by running the same commands locally.
    evidence: |-
      Pre-existing and already tracked as DW-6 (spec-1-1). This run does not push.
    location: >-
      .github/workflows/ci.yml
    severity: low
  - summary: >-
      The UX-DR92 unreachable notice says "Check that this phone is on your home Wi-Fi." on the web too; EXPERIENCE.md has no web variant.
    evidence: |-
      EXPERIENCE.md lines 155-164 give one copy for all platforms; the catalogue uses it verbatim. Changing it needs a UX decision in EXPERIENCE.md, which this story may not edit.
    location: >-
      apps/ts/web/src/lib/i18n/en.json
    severity: low
---

<intent-contract>

## Intent

**Problem:** There is no web app. Simon cannot sign in from a browser, and every later web story (1.8, 1.9, Epic 4 onward) needs the SvelteKit backend-for-frontend, its session, the app shell, the theme and the shared UI components this story sets up (FR5, FR20, AD-14, epics.md lines 588–626).

**Approach:** Create `apps/ts/web`, a SvelteKit 2 BFF on `adapter-node` that runs OIDC Authorization Code + PKCE server-side through `@escendit/sveltekit-auth-keycloak` 0.1.0-rc.12, wrapped by a thin app layer that turns reachability, certificate and Keycloak failures into the UX-DR92/93 Inline notices. Build the Sign-in surface, the app shell, Settings → Appearance and the listed DS-style components from `@coldframe/design-tokens`, with every string in one message catalogue. Prove it with vitest unit tests and Playwright e2e tests (axe, keyboard) against a fake OIDC provider, and give the local Aspire stack a `coldframe` realm with a `coldframe-web` client.

## Boundaries & Constraints

**Always:**
- The browser holds only the package's httpOnly session cookie. No load function, page data, `__data.json`, HTML, `localStorage` or `sessionStorage` ever contains an access, refresh or ID token; layout data carries only display fields (initials, display name).
- All OIDC runs through `OidcMiddleware` from `@escendit/sveltekit-auth-keycloak` (it already includes the session middleware). App code wraps it; it never re-implements the code exchange, PKCE or refresh.
- SIGN IN is a GET (the package answers a non-GET without a session with 405). Pressing it first checks the Server (`GET {COLDFRAME_SERVER_URL}/.well-known/healthz`, any HTTP response = reachable) and then the issuer's `/.well-known/openid-configuration`, each with a bounded timeout, then redirects to `/.oidc/signin?redirect_uri=<same-origin path>`.
- Failure mapping (copy verbatim from EXPERIENCE.md lines 155–164, via the catalogue): TLS verification error on either probe or during the OIDC flow → certificate notice, no action and no bypass; Server probe gets no response → unreachable notice with Try again; issuer probe fails, Keycloak returns `error` other than `access_denied`, code exchange fails, or discovery throws → Keycloak notice with Try again; `error=access_denied` → back to Sign in with no notice. A failed discovery is retried on the next SIGN IN, never cached until restart.
- Session end: when a request arrives with the app's "had a session" marker cookie but no identity (refresh rejected, session expired or store lost), clear the marker and show "You're signed out. Sign in again to see live data." with a Sign in action. A deliberate sign-out clears the marker first, so it shows no notice. The check runs on every navigation inside the app shell.
- Theme: System (default), Light, Dark in Settings → Appearance applies immediately with no Save, persists in a first-party cookie on this browser, and is rendered server-side as `data-theme` on `<html>` so there is no flash; System means no `data-theme` attribute, which `tokens.css` maps to `prefers-color-scheme`.
- Every user-visible string — including `<title>`, `aria-label`, button labels and notices — comes from `apps/ts/web/src/lib/i18n/en.json`; plurals use `Intl.PluralRules`; uppercase is CSS only. Glossary terms (UX-DR131) are capitalised; no exclamation marks, emoji, "successfully", "OK", "fine" or "all good" (UX-DR130).
- Visuals only from `@coldframe/design-tokens` (`tokens.css`, `fonts.css`, `icons/*`): square corners, 1 px borders, no shadows, no transitions or animations, the signature radial gradient only on the Sign-in background, two-tone focus ring (`focus` 2 px + `focus-gap` 2 px, never orange), 44 px minimum targets, buttons and segments grow with text and never clip.
- Every test name for a UX requirement starts with its id (e.g. `UX-DR56 …`); tests are written failing first; unit tests in `tests/ts/web`, e2e in `tests/ts/web.e2e`; exact version pins.
- Configuration comes from private env vars read only in `hooks.server.ts`/server modules: `COLDFRAME_SERVER_URL`, `KEYCLOAK_ISSUER`, `KEYCLOAK_CLIENT_ID`, `KEYCLOAK_CLIENT_SECRET`, optional `KEYCLOAK_ALLOW_INSECURE_HTTP` (default false), optional `SESSION_COOKIE_SECURE` (default true). Missing required values fail at startup with a message naming the variable.

**Never:**
- No Server address field, no "continue anyway", no disabling of TLS verification, no tokens or claims in client code, no Role taken from token claims.
- No Create Site, Site tabs, Site menu, "New Site", Garden empty state or Alerts count data — they arrive in 1.6/1.8/Epic 6; the shell shows the four nav items and page headings only.
- No SignalR proxy, API client calls, browser notifications, `SessionMonitor`/check-session iframe, back-channel logout wiring or Redis session store.
- No dependency on `@escendit/branding`, no network fetch of fonts or icons, no toasts, spinners, auto-dismissing notices or hover-only affordances.
- Do not edit DESIGN.md, EXPERIENCE.md, epics.md or `sprint-status.yaml`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Signed out, open app | no session, GET `/` or `/garden` | redirect to `/signin`; card with mark and SIGN IN only | No error expected |
| Sign in | SIGN IN, Server and issuer reachable, IdP authenticates | lands on `/garden` in the shell; only the httpOnly session cookie present | No error expected |
| Server unreachable | Server probe refused/DNS/timeout | `/signin` with unreachable notice + Try again | no redirect to IdP |
| Untrusted certificate | either probe or OIDC call fails TLS verification | certificate notice, no action | never retried insecurely |
| Keycloak error | issuer probe fails, `?error=server_error`, token exchange fails, discovery throws | Keycloak notice + Try again | discovery retried next time |
| Cancelled | callback `?error=access_denied` | `/signin`, no notice | none |
| Session ended | marker cookie, identity null, next navigation | `/signin` with signed-out notice + Sign in | marker cleared |
| Signed out on purpose | Settings → Sign out | `/signin`, no notice | marker cleared before redirect |
| Unsafe return path | `redirect_uri` or `returnTo` off-origin | falls back to `/garden` | none |
| Theme | choose Dark, reload | `data-theme="dark"` immediately and after reload; System removes attribute and cookie | invalid cookie value → System |

</intent-contract>

## Code Map

- `pnpm-workspace.yaml` -- already globs `apps/ts/*` and `tests/ts/*`. Add `packageExtensions: '@escendit/sveltekit-session@0.1.0-rc.12': { dependencies: { base58-js: 3.0.3 } }` -- rc.12 imports `base58-js` (`dist/DefaultSessionHasher.js:1`) but declares it only as a devDependency; fixed upstream on an unreleased branch.
- `.npmrc` (new, repo root) -- `@escendit:registry=https://registry.npmjs.org/` so a user-level GitHub Packages override cannot apply (quickstart promises no tokens). rc.12 on npmjs has the same integrity as GitHub Packages.
- `@escendit/sveltekit-auth-keycloak@0.1.0-rc.12` API (read `node_modules/@escendit/sveltekit-auth-keycloak/dist/{config,middleware}.js` before wiring) -- `OidcMiddleware(config)` returns a `Handle`; config `issuer`, `clientId`, `clientSecret`, `allowInsecureRequests`, `cookie {name, secure}`, `expireIn`; routes `/.oidc/signin?redirect_uri=`, `/.oidc/signin/callback`, `/.oidc/signout?redirect_uri=`, `/.oidc/signout/callback`; `locals.session.identity` null or `{authenticated, idToken, accessTokenExpiresAt, …}`; refresh is transparent; rejected refresh deletes the session and sets identity null; transient refresh failure throws. Scopes fixed `openid profile`. Cookie `sameSite: 'strict'`, `httpOnly`. Error gaps: unknown state → JSON 400 `invalid_challenge`; `?error=` or failed exchange → bare 400; discovery runs once at construction and a rejection is never retried. No `App.Locals` typing shipped -- declare in `src/app.d.ts`.
- `packages/design-tokens/package.json` -- exports `./tokens.css` (`:root,[data-theme="light"]`, `[data-theme="dark"]`, System via `@media (prefers-color-scheme: dark) :root:not([data-theme="light"])`), `./fonts.css` (relative `../../fonts/*.ttf`; allow the package dir in Vite `server.fs.allow`), `./icons/*` (21 Carbon SVGs incl. `checkmark`, `error--filled`, `settings`, `view`, `grid`, `notification`, `tools`), `.` (TS token values). Custom properties use `--cf-` prefix.
- `eslint.config.js` -- flat config, typescript-eslint `strictTypeChecked` + `projectService`; add `eslint-plugin-svelte` for `**/*.svelte` (parser `svelte-eslint-parser` with the TS parser) and ignore `**/.svelte-kit/`, `**/build/`, `**/test-results/`, `**/playwright-report/`.
- `tsconfig.base.json` -- no DOM lib; the web app's `tsconfig.json` extends `./.svelte-kit/tsconfig.json` instead (strict flags copied from base). Test packages follow `tests/ts/design-tokens` (extends base, `types: ["node"]`, `allowImportingTsExtensions`, `erasableSyntaxOnly`) plus `lib: ["ES2024","DOM"]` where needed.
- `tests/ts/design-tokens/package.json` -- test package pattern: private, `lint`, `typecheck`, `test: vitest run`, `workspace:*` devDeps.
- `.github/workflows/ci.yml` lines 105–140 (`typescript` job) -- add, before `Test`, `pnpm --filter @coldframe/web-e2e exec playwright install --with-deps chromium`; `pnpm -r typecheck|lint|test` then covers the new packages.
- `aspire/Coldframe.AppHost/AppHost.cs` lines 45–63 -- Keycloak runs `start --optimized` on dynamic host ports with HTTP enabled; add a realm import (bind-mount `../keycloak/realms` to `/opt/keycloak/data/import`, arg `--import-realm`) and pass a generated secret parameter `coldframe-web-client-secret` as `COLDFRAME_WEB_CLIENT_SECRET`. The Server's health path is `/.well-known/healthz`.
- `tests/cs/server.integration/KeycloakTests.cs` + `AppHostFixture.cs` -- pattern for Keycloak integration tests against the AppHost (admin token via `admin-cli` on `master`).
- `docs/quickstart.md` (TypeScript L116–130, test layout L178–185, pins L187–195), `apps/ts/README.md` L7, `packages/ts/README.md` -- update tables and add "Run the web app".
- Local tooling: node 24.21, pnpm 12.6 (`npm` is a broken symlink; use pnpm), Playwright 1.63.0 chromium revision 1243 already cached, podman 5.8.7. Pins: `@sveltejs/kit` 2.70.3, `svelte` 5.57.1 (spine L422), `@sveltejs/adapter-node` 5.5.7, `@sveltejs/vite-plugin-svelte` 7.3.1, `svelte-check` 4.7.6, `eslint-plugin-svelte` 3.23.0, `@playwright/test` 1.63.0, `@axe-core/playwright` 4.13.0; root `vite` 8.3.1, `vitest` 5.0.2, `typescript` 6.0.3.

## Tasks & Acceptance

**Execution:**
- `tests/ts/web/` (`@coldframe/web-tests`, vitest; `vitest.config.ts` with `@sveltejs/vite-plugin-svelte` and a `$lib` alias to `apps/ts/web/src/lib`) -- write failing unit tests first: failure classifier (TLS codes walked through `error.cause`: `DEPTH_ZERO_SELF_SIGNED_CERT`, `SELF_SIGNED_CERT_IN_CHAIN`, `UNABLE_TO_VERIFY_LEAF_SIGNATURE`, `UNABLE_TO_GET_ISSUER_CERT(_LOCALLY)`, `CERT_HAS_EXPIRED`, `CERT_NOT_YET_VALID`, `CERT_UNTRUSTED`, `ERR_TLS_CERT_ALTNAME_INVALID`, …), probes with injected `fetch`, auth-handle wrapper with a fake inner handle (every matrix row), safe return path, theme cookie parse/serialise, catalogue lookup and plurals (UX-DR125), locale formatting (UX-DR127 duration and day rules), voice and glossary rules over `en.json` (UX-DR130, UX-DR131), a no-hard-coded-copy test that parses every `.svelte` file with `svelte/compiler` and fails on any letter-bearing text node or literal `aria-label`/`title`/`alt`/`placeholder`/`label` attribute (UX-DR124), SSR renders of each component via `svelte/server` (UX-DR34, 35, 36, 53, 56, 76), and a coverage test asserting every UX-DR id in this story's AC appears in a test name in `tests/ts/web` or `tests/ts/web.e2e`.
- `apps/ts/web/` (`@coldframe/web`) -- `package.json` (scripts `dev`, `build`, `preview`, `typecheck: svelte-kit sync && svelte-check --fail-on-warnings`, `lint`), `svelte.config.js` (adapter-node), `vite.config.ts`, `tsconfig.json`, `src/app.html` (`<html lang="en" %cf.theme%>`), `src/app.d.ts` (`App.Locals.session`), `src/hooks.server.ts` (env validation, `sequence(authHandle, themeHandle)`).
- `apps/ts/web/src/lib/server/` -- `config.ts` (env → typed config), `failures.ts` (classifier → `unreachable | certificate | keycloak`), `probe.ts` (Server and issuer probes, timeouts), `auth-handle.ts` (lazy `OidcMiddleware`, dropped after a thrown discovery/sign-in error; maps thrown errors and ≥400 callback responses per the matrix; clears/sets the session marker cookie `cf_session`, httpOnly, SameSite=Lax), `return-path.ts`, `theme.ts` (cookie `cf_theme` → `transformPageChunk`).
- `apps/ts/web/src/routes/` -- `signin/+page.svelte` + `+page.server.ts` (notice from `?notice=`; redirect to `/garden` when signed in), `signin/start/+server.ts` (probe then redirect), `(app)/+layout.server.ts` (guard; reads `url.pathname` so it reruns on every navigation; returns initials and display name only), `(app)/+layout.svelte` (shell), `(app)/+page.server.ts` (`/` → `/garden`), `(app)/{garden,alerts,devices,members}/+page.svelte` (heading only), `(app)/settings/+page.svelte` (index: Appearance link, Sign out → `/.oidc/signout?redirect_uri=/signin`), `(app)/settings/appearance/+page.svelte`.
- `apps/ts/web/src/lib/components/` -- `Button` (primary/secondary/ghost, `working` shows the in-place progress label, e.g. "Signing in…"), `TextInput` (label, helper, invalid reason linked by `aria-describedby`, `aria-invalid`, `error--filled` icon, reveal via `view`), `SegmentedChoice` (equal-width, `aria-pressed`, leading `checkmark` on selected, wraps), `ThemeSwitcher` (SegmentedChoice wired to theme), `InlineNotice` (layer-01, 3 px left border, not dismissable, optional one action), `Modal` (native `<dialog>`, Esc closes, Cancel + named result action, one level), `RoleGate` (renders children only when Role ≥ minimum; hidden, not disabled), `AppShell`/`AppHeader`/`SideNav` (mark, initials; Garden · Alerts · Devices · Members with 3 px bar and `aria-current`; Settings in the footer; below 672 px the nav sits behind a header menu button), `Icon` (inline Carbon SVG, `aria-hidden`), `LiveRegions` (`role="status"` and `role="alert"` present empty in the root layout; Sign-in notices are announced through them), `SignInCard`.
- `apps/ts/web/src/lib/i18n/` -- `en.json` (all copy; plural entries as `{one, other}`), `glossary.json` (UX-DR131 terms), `index.ts` (`t(key, params)` typed from the JSON keys, `Intl.PluralRules('en')`), `format.ts` (dates, times, numbers, durations per UX-DR127).
- `tests/ts/web.e2e/` (`@coldframe/web-e2e`, Playwright, chromium only; `test: playwright test`) -- `fixtures/fake-idp.ts` (Node `http` + `jose`: discovery, JWKS, authorize with an auto-approve form, token with PKCE S256 check and refresh, end-session, `/.well-known/healthz` for the fake Server, and a control endpoint to switch modes: `error`, `access_denied`, token failure, short-lived tokens with rejected refresh, and to list issued tokens), a self-signed HTTPS stub (pinned `selfsigned`), `fixtures/stack.ts` launching the fake IdP and three `node build` instances of the web app (normal; Server URL on a closed port; Server URL on the TLS stub); specs: sign-in happy path and token isolation, each notice, cancel, session end, deliberate sign-out, theme persistence and System, axe (no serious/critical, both themes, every page), keyboard (tab order equals DOM reading order, focus ring visible and not orange, Esc closes Modal), 44 px targets, no transitions, 40 % longer text does not clip buttons or segments.
- `aspire/keycloak/realms/coldframe-realm.json`, `aspire/Coldframe.AppHost/AppHost.cs`, `tests/cs/server.integration/KeycloakTests.cs` -- realm `coldframe` (registration enabled, Organizations on), confidential client `coldframe-web` (standard flow only, PKCE S256 required, redirect `http://localhost:5173/.oidc/signin/callback`, post-logout `http://localhost:5173/.oidc/signout/callback`, secret `${COLDFRAME_WEB_CLIENT_SECRET}`); import on start; an integration test asserting the realm's discovery document and the client settings.
- `pnpm-workspace.yaml`, `.npmrc`, `eslint.config.js`, `.gitignore`, `pnpm-lock.yaml`, `.github/workflows/ci.yml`, `apps/ts/README.md`, `docs/quickstart.md`, `apps/ts/web/README.md` -- wiring, CI Playwright install, how to run the web app against the local Keycloak (env vars, where to find the issuer and secret in the Aspire dashboard), the UX-DR92 copy note below.

**Acceptance Criteria:**
- Given I am signed out, when I open the web app, then I see the Sign-in surface: the gradient background, a card with the Coldframe mark and a single SIGN IN button, no other control and no text on the gradient (UX-DR59, UX-DR60).
- Given I press SIGN IN and the fake IdP authenticates me, when I return, then I land on `/garden` in the shell with Garden · Alerts · Devices · Members and Settings in the nav footer, the session cookie is httpOnly, and no issued token string appears in any response body, `document.cookie`, `localStorage` or `sessionStorage` (AD-14, UX-DR58).
- Given the Server is unreachable, its certificate is untrusted, or the IdP returns an error, when I press SIGN IN, then the matching UX-DR92 Inline notice appears in the card, and the certificate notice has no action.
- Given my refresh is rejected after the access token expires, when I next navigate in the shell, then I see "You're signed out. Sign in again to see live data." with a Sign in action (UX-DR93).
- Given Settings → Appearance, when I choose System, Light or Dark, then `data-theme` changes immediately and survives a reload, and System follows `prefers-color-scheme` via Playwright `colorScheme` emulation (UX-DR15, UX-DR53, UX-DR75).
- Given every page in this story in both themes, when the Playwright axe and keyboard tests run, then there are no serious or critical violations, the focus ring is visible, and tab order follows reading order (UX-DR16, UX-DR102); and the no-hard-coded-copy test passes (UX-DR124).
- Given the UX-DR list in the story's last AC, when the coverage test runs, then each id is named by at least one test.
- Given the workspace, when CI runs `pnpm install --frozen-lockfile`, the Playwright install step, `pnpm -r typecheck`, `pnpm -r lint` and `pnpm -r test`, then all pass; and `dotnet test` of the integration project passes with the new realm test.

## Spec Change Log

## Review Triage Log

### 2026-09-28 — Review pass
- verdicts: 32 findings — high 0, medium 1, low 24, false 5, maybe-false 2
- findings:
  - `[false]` `[reject]` Blind: `/.oidc/signin?redirect_uri=https://evil` skips `safeReturnPath` — the package's `SanitizeRedirectUri` (dist/middleware.js:12-29) accepts only same-origin values for sign-in and sign-out, falling back to the origin.
  - `[low]` `[reject]` Blind: sign-out is a GET, so another site can sign the user out — a nuisance only (no data changes); a guard needs Sec-Fetch-Site checks or a POST flow the package does not offer.
  - `[low]` `[reject]` Blind: `serverHealthUrl` drops a path in `COLDFRAME_SERVER_URL` — `.well-known` is root-relative by RFC 8615 and the Server maps `/.well-known/healthz` at its root.
  - `[low]` `[patch]` Blind: in-memory sessions are not documented — added "Sessions live in memory" to `apps/ts/web/README.md` (one replica, a restart signs everyone out with the signed-out notice).
  - `[low]` `[patch]` Blind: a failed sign-in loses the page the user was going to — grouped with Edge "return path dropped"; a thrown error on `/.oidc/signin` now keeps the safe `redirect_uri` as `returnTo`. Callback failures cannot know it (the package keeps it in its challenge).
  - `[low]` `[reject]` Blind: the fake IdP accepts any redirect URI — both the package and the realm use the package default callback paths; the realm test asserts the client's redirect URIs.
  - `[low]` `[patch]` Blind: the certificate notice for an issuer is untested — grouped with Verification gap 2; see there.
  - `[low]` `[reject]` Blind: `/signin/start` can be used to make unlimited outbound probes — a single-user app on the home network; each probe is bounded to 5 s, and a cache adds state for a threat not shown.
  - `[maybe-false]` `[reject]` Blind: raw OIDC errors may log secrets — openid-client errors carry the OP's error response, not the client secret or verifier; settling it needs a captured token-endpoint error; would be low at most.
  - `[low]` `[patch]` Blind: CI discards the Playwright report and traces — added an `if: failure()` upload step with `actions/upload-artifact` pinned by SHA (v7.0.1).
  - `[low]` `[reject]` Blind: the signed-out e2e test waits 1.5 s — the wait is for a 1 s token to expire, which is inherent to the scenario; it passed in every run.
  - `[low]` `[reject]` Blind: the token-leak test registers two listeners and skips 3xx responses — the duplication is cosmetic, and redirect `Location` values are app paths or the IdP authorize URL, which carries no token.
  - `[low]` `[reject]` Blind: `/` hard-codes `/garden` with a 307 beside the guard's 303 — either order ends on the same page; no user-visible effect.
  - `[low]` `[patch]` Blind: a failed discovery is never logged — `watch()` now calls `options.log` with the error.
  - `[medium]` `[patch]` Edge: after a failed discovery, only `/.oidc/signin` rebuilds the middleware, so token refreshes on page loads fail until someone presses SIGN IN — the failed instance is now dropped on any path; unit test on `/alerts` added.
  - `[false]` `[reject]` Edge: the session marker is used up by routes that never show the notice — static assets bypass hooks, no link preloading is configured in `app.html`, and the guard puts `notice=signed-out` in the redirect URL, so a later request without the marker cannot lose it.
  - `[false]` `[reject]` Edge: a throw on `/.oidc/signout` shows the Keycloak notice — `handleSignOutEndpoint` catches a failed discovery and redirects to `redirect_uri` (dist/middleware.js:506-530).
  - `[low]` `[patch]` Edge: a thrown sign-in error drops the return path — grouped with the Blind finding above; fixed there.
  - `[low]` `[reject]` Edge: an `http://` issuer without `KEYCLOAK_ALLOW_INSECURE_HTTP` starts and then fails every sign-in — the quickstart sets the flag for the local stack; a startup check is an extra guard for a misconfiguration.
  - `[low]` `[patch]` Edge: a future date renders as a weekday — `format.ts` now uses the weekday only 1–7 days back; test added.
  - `[false]` `[reject]` Edge: exactly 7 days back shows a weekday — the rule is "older than 7 days shows the date" (EXPERIENCE.md), so 7 days is still a weekday.
  - `[low]` `[reject]` Edge: a NaN duration renders "NaN d" — no caller formats durations yet; a guard for an unshown state.
  - `[low]` `[patch]` Edge: email-only claims give initials from the TLD — initials now come from the part before `@`; test added.
  - `[low]` `[reject]` Edge: no name claims give an empty avatar label — Keycloak always returns `preferred_username` with the fixed `profile` scope.
  - `[low]` `[patch]` Edge: an unmounted open Modal blocks every later modal — `Modal.svelte` releases the modal level in an `$effect` cleanup.
  - `[low]` `[reject]` Edge: `min-width: fit-content` can make segments unequal — only when one label outgrows its equal share, which is the UX-DR36/126 wrapping behaviour; System/Light/Dark render equal.
  - `[low]` `[patch]` Verification gap: `/signin/start` keeping `returnTo` on a failed probe is untested — e2e test on the unreachable instance from `/devices` added.
  - `[low]` `[patch]` Verification gap: the certificate re-check after a failed code exchange is wired in hooks but untested — moved to `diagnoseCallback` in `probe.ts`, unit-tested for certificate and Keycloak outcomes.
  - `[maybe-false]` `[defer]` Intent: no test drives a real code exchange against the `coldframe` realm — medium if true; settled by a local sign-in against the Aspire Keycloak or an AppHost-hosted e2e run.
  - `[low]` `[defer]` Intent: the CI workflow has not run on GitHub — pre-existing, tracked as DW-6.
  - `[low]` `[defer]` Intent: the unreachable copy says "this phone" on the web — needs a UX decision in EXPERIENCE.md.
  - `[false]` `[reject]` Intent: the story may owe human-only actions (awaiting-operator) — no acceptance criterion needs an action outside the repository; the CI and realm checks run locally with the committed code.

## Design Notes

- **UX-DR92 copy on the web.** The unreachable copy says "this phone"; EXPERIENCE.md has no web variant. The catalogue uses the specified copy verbatim and the question is deferred, not rewritten here.
- **Coldframe mark.** No artwork is specified. The card shows the product name "Coldframe" from the catalogue in the `headline` role as the mark, as an `<h1>`; replacing it with artwork later changes one component.
- **Before any Site exists.** The shell has no Site tabs until 1.8; `/garden` shows only its heading. Create Site routing for users without a Membership is Story 1.8.
- **Wrapper, not fork.** The package's gaps (bare 400s, one-shot discovery, no expiry signal) are closed in `auth-handle.ts` by inspecting the request path, the callback query and the inner response, so upgrading the package stays a version bump.
- **Why a fake IdP in CI.** The `typescript` job has no containers; a small OIDC provider lets e2e tests drive every failure mode deterministically. Real Keycloak is covered by the realm integration test and the documented local run.

## Verification

**Commands:**
- `pnpm install --frozen-lockfile && pnpm -r typecheck && pnpm -r lint && pnpm -r test` -- expected: exit 0, Playwright suite included
- `pnpm --filter @coldframe/web build` -- expected: exit 0, `apps/ts/web/build/` produced
- `pnpm --filter @coldframe/design-tokens run check` -- expected: exit 0 (unchanged)
- `dotnet test tests/cs/server.integration` (podman: `ASPIRE_CONTAINER_RUNTIME=podman`) -- expected: all pass including the realm test

**Manual checks (if no CLI):**
- `git grep -n "accessTokenRaw\|refreshTokenRaw\|idTokenRaw" apps/ts/web/src` shows use only under `src/lib/server/` or `hooks.server.ts`.

## Auto Run Result

Status: done

**Summary.** `apps/ts/web` is a new SvelteKit 2 BFF on `adapter-node`. OIDC Authorization Code + PKCE runs server-side through `@escendit/sveltekit-auth-keycloak` 0.1.0-rc.12. `auth-handle.ts` wraps the package and adds the failure notices: SIGN IN first checks the Server and the issuer, and failures map to the UX-DR92 notices (unreachable, certificate, Keycloak; cancel is silent). A failed discovery is retried. The `cf_session` marker turns a session that ended on its own into the UX-DR93 signed-out notice. The browser holds only httpOnly cookies. The app has the Sign-in surface, the app shell (Garden · Alerts · Devices · Members, Settings in the footer), Settings → Appearance (System/Light/Dark, rendered server-side as `data-theme`, stored per browser) and the DS-style components. Every string is in `src/lib/i18n/en.json`, with plurals and locale formatting. The local Aspire Keycloak imports a `coldframe` realm with a confidential `coldframe-web` client. No acceptance criterion needs a human outside the repository.

**Files changed.**
- `apps/ts/web/` -- the web app: config, probes, failure classifier, auth wrapper, guard, theme, components, routes, i18n, README
- `tests/ts/web/` -- vitest unit tests (matrix rows, catalogue, voice/glossary, no-hard-coded-copy, SSR renders, UX-DR coverage)
- `tests/ts/web.e2e/` -- Playwright suite with a fake OIDC provider, a self-signed TLS stub and three app instances; axe, keyboard, theme, notices
- `aspire/keycloak/realms/coldframe-realm.json`, `aspire/Coldframe.AppHost/AppHost.cs`, `tests/cs/server.integration/KeycloakTests.cs` -- realm import with a generated client secret, and two realm tests
- `.github/workflows/ci.yml` -- Playwright Chromium install, and report upload on failure
- `pnpm-workspace.yaml` (`base58-js` package extension), `.npmrc`, `eslint.config.js` (Svelte), `.gitignore`, `package.json`, `pnpm-lock.yaml`, `apps/ts/README.md`, `docs/quickstart.md` -- wiring and docs

**Deviations from the spec (from the implementer).**
- The realm file is copied into the Keycloak image, not bind-mounted, because SELinux blocked the mount.
- `dotnet test` needs `--project`.
- Sign out confirms in a Modal.
- `bun` is marked external in `vite.config.ts` for the package's unused Redis store.

**Review.** 32 findings:
- **Patched (12 rows, 10 fixes):**
  - a failed discovery is now retried on any path (medium)
  - the discovery failure is logged
  - the return path is kept on a thrown sign-in error
  - `diagnoseCallback` is extracted and tested
  - an e2e test covers the return path on a failed probe
  - Modal releases on unmount
  - future dates show the date
  - email-only initials
  - an in-memory session note in the README
  - the CI report upload
- **Deferred (3):** the real-Keycloak code exchange is untested (medium, unverified); CI has not run on GitHub (DW-6); the "this phone" copy on the web.
- **Rejected (17):** reasons are in the triage log.

**Follow-up review recommended: false.** Patched entries: high 0, medium 1, low 9.

**Verification** (on this machine, after the patches):
- `pnpm install --frozen-lockfile`, `pnpm -r typecheck` (svelte-check with warnings as failures) and `pnpm -r lint`: exit 0.
- `pnpm -r test`: exit 0. That covers web unit 198 passed, web e2e 27 passed, design-tokens 599 passed, and api-client 1 passed.
- `pnpm --filter @coldframe/web build` and `pnpm --filter @coldframe/design-tokens run check`: exit 0.
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test --project tests/cs/server.integration`: 27/27 passed. This ran before the review patches; the patches touched no C# or AppHost files.
- `grep accessTokenRaw|refreshTokenRaw|idTokenRaw apps/ts/web/src`: no matches.

**Residual risks.**
- The session cookie is `SameSite=Strict`, set by the package. A Keycloak on a different site than the web app would lose the cookie on the callback. This was not tested.
- A certificate failure during the code exchange is inferred by checking the issuer again, because the package hides the cause.
- The in-memory session store means one replica, and a restart signs everyone out.
- No browser test has run against the real Keycloak (deferred).

