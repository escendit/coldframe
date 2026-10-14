# Epic 9 Context: Share the garden

<!-- Generated from planning artifacts. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Let an Owner share a Site with other people: invite someone (for example a neighbour before a holiday) by email with a Role, later change or remove that Role, and never leave the Site without an Owner. A Member gets the same Alerts in their own Notification Window and sees every read surface, but none of the admin controls. This is what turns Coldframe from one gardener's tool into a garden that several people can look after.

## Stories

- Story 9.1: Invite someone to my Site
- Story 9.2: Change and remove Roles, always keeping an Owner

## Requirements & Constraints

- **Only Owners** invite, change Roles and remove Memberships. For Administrators and Members the Invite form and Role controls are hidden, and the API rejects the call with 403.
- **Roles:** `Owner > Administrator > Member`, applied per Site. A Role on Site A grants nothing on Site B.
- **Every Site always keeps at least one Owner.** Any change that would leave zero Owners is rejected, including two Owners demoting each other at the same moment.
- **Acceptance happens on the home network** through the Phase Two invitation flow. The invitee signs in or registers on the Keycloak/Phase Two page opened from the email link. The email (a Keycloak/Phase Two template shipped with the deployment) and the in-app confirmation both say to accept on the garden's Wi-Fi.
- **On acceptance**, the new person appears in Members with their Role. Their User grain gains the Site and pulls its open Alerts, and their app opens on that Site's overview and asks for notification permission there.
- **Lowered or removed Roles take effect on the next request**, without waiting for the token to expire. A removed person's User grain drops the Site and the notifications it holds for that Site.
- **Members:** see every read surface and receive Alerts in their own Notification Window. Changes by a Member to Devices, Thresholds, Calibration, Pause or Memberships are rejected with an authorization error.
- **Multi-Site correctness must be covered by automated tests**, because only one Site is field-tested. This means the generated authorization matrix (every endpoint × Role × own/other Site) and a person who is Owner on Site A and Member on Site B.
- **Tests:** integration tests on the Aspire AppHost with Keycloak and Phase Two (invite → accept → Membership in the projection → new Member authorized per Role). Also a Playwright end-to-end run of the neighbour journey: invite, accept, the neighbour gets "Cucumbers needs water" in their own window, and sees no admin controls. Each UX surface gets at least one named test, written failing before implementation.

## Technical Decisions

- **Keycloak/Phase Two is the identity authority.** Sites are Phase Two Organizations, and Memberships and Roles live there. The Server never writes Users.
- **The Site grain is the only writer** of Memberships and Roles to Keycloak/Phase Two. It checks the Owner invariant against its own persisted Owner set, calls the Phase Two API, then persists the outcome. Calls on one Site are serialized, which is what makes concurrent Owner demotions safe.
- **Invitations use Phase Two's native flow** (email, link, expiry). The Server keeps no invitation state and no invitation deadline. An accepted invitation arrives as a Membership event.
- **Keycloak events** reach the Site and User grains through `keycloak-temporal-extensions` → Temporal → Orleans as idempotent reconciliations. Membership events therefore arrive asynchronously.
- **Break-glass edits** in the Keycloak admin console arrive through the same pipeline. If one would leave a Site with no Owner, the Site grain keeps its last valid Owner set for authorization and raises an operator-visible error. It never silently repairs the violation.
- **Read-your-writes:** Site-grain events update the identity projection straight away.
- **Authorization** reads the caller's Role on the target Site from the identity projection, never from token claims. Every endpoint declares a minimum Role in metadata, and one shared policy enforces it.
- **The User grain** owns its Site set, fed by Membership events. On joining a Site it pulls that Site's open Alerts. On activation it reconciles against `Site.OpenAlerts()`.
- The identity projection is a read model and is not event-sourced. Site and User grains are event-sourced.

## UX & Interaction Patterns

- **Members surface** (Settings → Members on mobile; side nav "Members" on web) holds the Member tiles and the Invite form. Members management is visible to Owners only. It has no pending-invitation list and no in-app accept surface.
- **Invite form:** an email field and a Role Segmented choice (Member / Administrator / Owner) with a one-line description that updates on selection. For Member it reads "Member sees everything and gets the same Alerts, but can't change settings." The primary action is "Send invite". On success the form clears and an Inline notice reads "Invitation sent to <email>. They accept from the email, on the garden's Wi-Fi." The Members list stays unchanged until acceptance.
- **Member tile:** one per Membership. It shows an initials square (current user: `primary` fill with `ink-on-bright`; others: `layer-01` with `text-primary`), the name, and a Role line such as "OWNER · LAST ONE" or "MEMBER · CHANGE". Tapping opens Role change or remove (Owner only). The last Owner is locked with "A Site always keeps at least one Owner."
- **Removing someone** asks for confirmation in a native dialog or DS Modal that names the person. Validation errors show one line under the field, e.g. "A Site always keeps at least one Owner."
- **Invitation accepted, Membership not arrived yet:** Create Site shows "Accepted an invitation? It can take a minute to appear.", refreshes Memberships, and opens the Site as soon as it arrives.
- **Member read-only state:** admin controls (Thresholds, Calibrate, Pause, Add/move Devices, Silence Window, Lots, Members) are hidden, not disabled. A 403 race shows "You can't change this on <Site>. Ask an Owner or Administrator."
- **Notification permission** is requested when a newly accepted Member first lands on the Site overview, after one line explaining why.
- **Site switcher** lists every Site with a Membership and the Role on each. Switching to a Site where the user is a Member shows only read surfaces.

## Cross-Story Dependencies

- 9.2 builds on 9.1's Members surface and Membership flow. The UJ-5 end-to-end test in 9.2 depends on 9.1's invite/accept path.
- Depends on Epic 1: Phase Two Organizations, the Site grain with its Owner set, the identity projection, per-Site authorization and the authorization matrix (1.6), the Keycloak → Temporal → Orleans reconciliation pipeline (1.7), and the Create Site screen (1.8).
- Depends on Epic 6: User-grain Alert delivery, the Notification Window, and push permission timing (6.5). Admin surfaces from Epics 3–8 must honour the Member read-only rules.
