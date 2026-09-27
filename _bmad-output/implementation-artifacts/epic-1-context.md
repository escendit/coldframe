# Epic 1 Context: Sign in and create my garden

<!-- Generated from planning artifacts. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Simon signs in through his self-hosted Keycloak on web, iOS and Android, creates the Site "Home" as its Owner, and manages Lots; the Garden overview shows its empty state. The epic is also the foundation for all later work: monorepo, CI, local dev stack, event journal and projections, design tokens, the identity pipeline and per-Site authorization. Patterns set here are the ones every later grain, endpoint and screen follows.

## Stories

- Story 1.1: Monorepo scaffold, CI and local dev stack
- Story 1.2: Event journal, migrations and projection pipeline
- Story 1.3: Design tokens and themes
- Story 1.4: Sign in on the web
- Story 1.5: Sign in on iOS and Android
- Story 1.6: Create a Site on the Server with per-Site authorization
- Story 1.7: Reconcile identity changes from Keycloak
- Story 1.8: Create Site and the empty Garden in the apps
- Story 1.9: Manage my Site and Lots

## Requirements & Constraints

- Sign-in is OIDC against Keycloak, which owns accounts, passwords and registration. The Server stores no passwords and never writes Users.
- Any User can create a Site and is its Owner immediately. Only an Owner renames the Site. Owners and Administrators create, rename and remove Lots. Removing a Lot that holds a Node is rejected.
- Roles are ordered Owner > Administrator > Member and apply per Site; a Role on one Site grants nothing on another. Every request carries a Keycloak token and is authorized per Site.
- Only one Site is field-tested, so Role and multi-Site behaviour must be proven by automated tests.
- Test-first: acceptance criteria become failing tests before implementation; CI blocks merges on failing tests or lint errors.
- Accessibility floor: text scales without truncation, screen-reader labels carry role and state, nothing is conveyed by colour alone, contrast is at least 4.5:1 for text and 3:1 for UI, enforced in CI from the tokens.
- English only, but every string is externalised from the first commit; layouts tolerate 30-40 % longer text; plurals and formats follow locale rules.
- All traffic uses TLS with publicly trusted certificates; clients never offer to bypass a certificate failure.

## Technical Decisions

- **Layout:** no starter template. `apps/`, `packages/`, `tests/`, `aspire/`, `deploy/`, `hardware/`, `docs/`. All tests live under `tests/<lang>/`, mirroring the code; only Rust unit tests stay inline.
- **Pins:** .NET 10, Central Package Management, no floating transitive versions. Orleans 10.3.1, `Microsoft.Orleans.Streaming.NATS` 10.3.1-alpha.1, NATS.Net 2.x, FluentMigrator 8.0.1, `Escendit.Orleans.Migrations.Cluster.PostgreSQL` 10.3.1-rc.0, Phase Two Keycloak 26.6.7 with `keycloak-temporal-extensions` v0.0.1-rc.2.
- **Local stack:** the Aspire AppHost runs PostgreSQL, NATS JetStream, Temporal and Keycloak, and doubles as the integration-test host. .NET services use the Escendit service defaults.
- **Domain:** one owning, event-sourced grain per entity, the only writer of its state. API handlers change state only through grains and read from read models; grains never read read models. Cross-entity invariants are enforced by a synchronous grain call.
- **Journal:** one PostgreSQL event table with a global position, System.Text.Json payloads and stable type aliases. Event and outbox row commit in one transaction. Projectors read by global position, keep a checkpoint and can rebuild from zero; consumers are idempotent. Orleans streams are wake-up hints only, so everything must converge with streams disabled. Old event versions stay readable, and CI replays a fixture journal.
- **Migrations:** one forward-only set, run as a job before the silo starts. Application startup never runs DDL.
- **Time:** UTC everywhere. Server code reads time only from an injected `TimeProvider`; tests wait on journal positions with a bounded timeout, never sleep.
- **Identity:** the Site grain is the only writer of Sites, Memberships and Roles to Keycloak (Phase Two Organizations). It checks its own persisted Owner set, calls Keycloak, then persists. Lifecycle is `Uncreated`, `Active`, `Deleted`; outside `Active`, Site-scoped calls return 404. Create Site runs through the User grain, which tags the Organization with the idempotency key so a retry finds it.
- **Read-your-writes:** Site-grain events update the identity projection immediately.
- **Reconciliation:** Keycloak events reach the Site and User grains through Temporal, which carries nothing else, and apply idempotently. An edit that would leave a Site without an Owner keeps the last valid Owner set and raises an operator-visible error; it is never silently repaired.
- **Authorization:** the caller's Role comes from the identity projection, never token claims. Each endpoint declares its minimum Role in metadata; one shared policy enforces it. A generated matrix test covers every endpoint, Role, own Site and other Site, and picks up new endpoints automatically.
- **Lots:** the Lot grain owns occupancy; a claimed Lot refuses removal. Removal is an event plus tombstone; the ID stays resolvable.
- **API conventions:** contract-first OpenAPI with generated clients. Site ID is the Keycloak Organization ID, User ID is the OIDC `sub`, Lots use UUIDv7. Creating POSTs accept an `Idempotency-Key`, kept 24 h. Errors are RFC 9457 Problem Details: 403 for a missing Role, 404 for a non-existent Site. JSON is camelCase with string enums. Resources are plural nouns under `/sites/{siteId}/...`; events are past tense; glossary terms are used verbatim.
- **Clients:** render only. The web app is a SvelteKit backend-for-frontend using `@escendit/sveltekit-auth-keycloak`; the browser holds only a session cookie and never calls the Server directly. The shared Kotlin core owns OIDC (Authorization Code + PKCE), tokens and the API client; SwiftUI and Compose shells hold UI only. Mobile builds bake in the Server URL and Keycloak issuer.
- **Design tokens:** copied from the Escendit branding theme into `packages/design-tokens`, generated for Swift, Kotlin and CSS in light and dark. No dependency on the private branding package.

## UX & Interaction Patterns

- **Sign in:** a card with the Coldframe mark and one SIGN IN button over the signature radial gradient, the only gradient in the product. No Server address field. Failures show as non-dismissable inline notices with one action; cancelling returns silently; an expired session shows a signed-out notice.
- **Navigation:** mobile tabs Garden, Alerts, Devices, Settings. Web shell with Site tabs in the header, side nav Garden, Alerts, Devices, Members, and Settings in the footer.
- **Create Site:** shown on first sign-in with no Membership or from "New Site". Site name plus a panel proposing the detected time zone to confirm or change.
- **Empty Garden:** summary header, "No Readings yet", and four first-run step tiles; web users and Members see them without actions. Never describe an empty state as fine.
- **Site switcher:** lists each Site with the user's Role and always offers "New Site".
- **Role gating:** controls a Role cannot use are hidden, not disabled. Destructive actions confirm in a dialog naming the object.
- **Lot tiles** appear in the Server's order; clients never re-sort or compute status.
- **Theme:** System, Light or Dark, applied instantly, stored per device.
- **Visual rules:** square corners, 1 px borders, no shadows. Orange primary buttons with dark ink labels. Two-tone focus ring, never orange. Carbon icons only.
- **Copy:** calm and literal; no exclamation marks or emoji. Button labels name the result. Errors say what happened, what did not change, and the next step. Working buttons show an in-place progress label, never a spinner.

## Cross-Story Dependencies

- 1.1 precedes everything. 1.2 underpins the grains in 1.6, 1.7 and 1.9. 1.3 supplies tokens and icons for the UI in 1.4, 1.5, 1.8 and 1.9.
- 1.6 provides the Create Site endpoint and authorization policy used by 1.7, 1.8 and 1.9. 1.8 needs sign-in from 1.4 and 1.5.
- The authorization matrix starts in 1.6 and must cover every endpoint added afterwards, including the Lot endpoints in 1.9.
- Node assignment arrives in a later epic, so refusing removal of an occupied Lot is tested with a fixture claim.
