---
title: 'Deploy Coldframe at home and build my own apps'
type: 'feature'
created: '2026-10-19'
status: 'awaiting-operator'
baseline_revision: '05c2837148ef2a04e8e4b3ad4e6bdd265d5800cf'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-10-context.md'
warnings: [oversized]
deferred: []
operator_actions:
  - "Follow docs/deploy/README.md end to end on a real domain with its zone at Cloudflare and a single-node RKE2 home server, at a published release tag, and fix in the guide every step that differs from what you see (RKE2 install, firewall, namespace, Secrets, realm, split DNS, first sign-in)."
  - "Build the Android app with your own Firebase project and the iOS app with your own Apple Developer team and APNs key, following docs/build/apps.md, install both on your phones, sign in against your deployment and receive a push; correct any console step or command in the guide that differs."
  - "Once a release tag publishes the images, start deploy/compose/ with docker compose up on a LAN Docker host following deploy/compose/README.md, sign in to the web app, and fix compose.yaml or the README where the stack does not come up."
---

<intent-contract>

## Intent

**Problem:** The deploy facts are spread across runbooks written for operators (`docs/operations/install.md`, `split-dns.md`, `deploy/SECRETS.md`, `deploy/charts/README.md`). Nothing installs RKE2 itself, nothing tells a newcomer how to turn the localhost realm into one for their domain, and nothing covers the first sign-in. `docs/build/apps.md` is a stub, and the Compose example that `deploy/README.md:14`, `README.md:25` and `docs/quickstart.md:469` promise does not exist.

**Approach:** Add one ordered adopter guide in `docs/deploy/` that walks domain → RKE2 + Fleet → Secrets → Keycloak realm → split DNS → first sign-in, linking the Epic 2 runbooks for detail rather than copying them. Add a small tested script that writes a realm file for the adopter's hosts. Expand `docs/build/apps.md` into a full build-your-own-apps guide. Add `deploy/compose/` as a reference-only example. Extend the `Docs` workflow so links resolve, the realm script's tests pass, and `docker compose config` validates the example.

## Boundaries & Constraints

**Always:**
- **Order is fixed.** `docs/deploy/README.md` has the six sections in this order, numbered: 1 Domain and DNS API token, 2 RKE2 and Fleet, 3 Secrets, 4 Keycloak realm and Phase Two, 5 Split DNS, 6 First sign-in. It links `docs/operations/install.md`, `split-dns.md` and `restore.md` (the Epic 2 runbooks) and `deploy/SECRETS.md`.
- **Link, don't duplicate.** The runbooks stay the source of truth for commands that already exist there (Fleet install, Secret creation, cert-manager flags, split DNS records, verification). The guide gives the order, the decisions and the missing steps, then links the runbook section by anchor.
- **Missing steps the guide adds:**
  - RKE2 v1.36.4+rke2r1 install on a single node (`curl -sfL https://get.rke2.io | sudo INSTALL_RKE2_VERSION=v1.36.4+rke2r1 sh -`, enable `rke2-server`, kubeconfig at `/etc/rancher/rke2/rke2.yaml`) and pointing it at the LAN resolver of section 5 (forward reference: the node must use split DNS; `split-dns.md#the-node-and-the-cluster-must-use-this-resolver`).
  - Domain: a dedicated subdomain, zone at Cloudflare, the token scopes (Zone→DNS→Edit, Zone→Zone→Read) as in `split-dns.md` step 1, the three hosts `<domain>`, `api.<domain>`, `auth.<domain>`.
  - Realm: run `deploy/realm/prepare-realm.sh`, create the `coldframe-realm` ConfigMap from its output, and what Phase Two adds (organizations, the `coldframe` email theme with the home-network hint, the Invitation required action, Verify email). SMTP comes from `coldframe-smtp` at first import; the realm is imported once only.
  - First sign-in: open `https://<domain>` on the home Wi-Fi, Register, verify the email (needs working SMTP), create a Site; whoever creates it is its Owner. Then the permanent Keycloak admin (`deploy/SECRETS.md` post-start step). Then the install verification checklist.
- **Apps guide facts come from the build files.** Android: `coldframe.serverUrl`, `coldframe.keycloakIssuer`, `coldframe.keycloakClientId` and the four `coldframe.firebase*` Gradle properties (`apps/kt/android/build.gradle.kts:13,40-53`); no `google-services.json`, no plugin. iOS: `Config/Coldframe.local.xcconfig` with the keys of `Config/Coldframe.xcconfig:9-34`, including the `https:/$()/host` escape, `COLDFRAME_PUSH`, `DEVELOPMENT_TEAM`, and XcodeGen. Server side: the `coldframe-push` Secret and `push.apns.topic` = the iOS bundle id.
- **Say what the adopter must change together.** Changing the Android `applicationId` or iOS bundle id is allowed alone; the OIDC redirect scheme `com.escendit.coldframe` is a constant in `CoreConfig.kt:35`, `build.gradle.kts:56` and the realm, so the guide says to keep it (or change all three together). The Firebase Android app must use the applicationId the adopter builds.
- **Android release signing without code changes:** document `./gradlew :android:assembleRelease` then `zipalign` and `apksigner sign` from the SDK build-tools with the adopter's own keystore, and `adb install`. A debug build via `installDebug` is the quick path.
- **Compose is labelled.** `deploy/compose/README.md` and the top comment of `compose.yaml` say "reference example only, not the supported deployment (NFR7); the supported path is `docs/deploy/`". No TLS, no backups, web app only (release mobile builds need HTTPS with a public certificate).
- **Compose topology** matches the charts: postgres (`postgres:18.6`, databases `coldframe`, `temporal`, `temporal_visibility`, `keycloak` via an init script), nats 2.15.0 with JetStream, a one-shot temporal-schema (admin-tools 1.31.2, the four `temporal-sql-tool` commands of `deploy/charts/temporal/templates/schema-job.yaml:42-45`) and namespace creation, temporal server 1.31.2, migrations (one-shot, before server), keycloak (`start --optimized --import-realm`, realm mounted from a prepared file), server, web. Images `ghcr.io/escendit/coldframe/<component>:${COLDFRAME_VERSION}` with no default and no `latest`. Every secret and host comes from `.env` (template `.env.example`; `.env` is gitignored by `*.env`). Env var names come from the chart templates / `deploy/images/README.md`.
- **Compose host name:** one `COLDFRAME_HOST` (a LAN name or IP of the Docker host) used for the browser-facing URLs and by the containers, so the Keycloak issuer is the same string for the browser and the Server (`Identity__RequireHttpsMetadata=false`, `KEYCLOAK_ALLOW_INSECURE_HTTP=true`, `SESSION_COOKIE_SECURE=false`). Ports: web 3000, server 8080, keycloak 8180.
- **`prepare-realm.sh`** (bash + jq): reads `aspire/keycloak/realms/coldframe-realm.json` (or `--in`), sets the `coldframe-web` client's `redirectUris` to `[<origin>/.oidc/signin/callback]` and its `post.logout.redirect.uris` to `<origin>/.oidc/signout/callback` (paths from `apps/ts/web/src/lib/server/auth-handle.ts:10-12`), leaves every `${…}` placeholder and the mobile client untouched, writes to `--out` or stdout. Requires `https://` unless `--allow-http`.
- **New marker `<!-- ci:manual -->`** in `docs/check-commands.sh`: the block needs a phone, Xcode, the Android SDK or a cluster; CI does not run it. `--list` shows it. Every bash/sh block in `docs/build/apps.md` is marked.
- CI: in `docs.yml`'s `links` job add `deploy/realm/test.sh` and `docker compose -f deploy/compose/compose.yaml --env-file deploy/compose/.env.example config --quiet`. Add `deploy/compose/README.md` to `check-links.sh`'s default roots (`docs/` is already covered). Pin nothing new unless an action is added (then by SHA with a release comment).
- Keep `docs/build/apps.md` at its path: `docs/operations/release.md:56` and `deploy/release/release-notes.sh:155` link it. Update `apps.md:4` ("Story 10.3 expands it") and `deploy/README.md:14` accordingly.

**Never:**
- Don't edit `.github/workflows/ci.yml` (reruns the slow Images job), app code, charts, firmware, or the Server.
- Don't edit `sprint-status.yaml`.
- Don't commit keys, team ids, `google-services.json`, keystores or a filled `.env`.
- Don't check external `http(s)` links or run `docker compose up` in CI.
- Don't present Compose as supported or claim the guide was followed end to end on real hardware (that is Story 10.5).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Realm for a domain | `--web-origin https://coldframe.example.org` | web redirect `https://coldframe.example.org/.oidc/signin/callback`, post-logout `…/signout/callback`; `${COLDFRAME_*}` placeholders and mobile client unchanged; valid JSON | No error expected |
| Trailing slash | `--web-origin https://x.example.org/` | same as without the slash | No error expected |
| HTTP origin | `--web-origin http://192.168.1.10:3000` | refused | exit 2, says `--allow-http` is for the Compose example only |
| HTTP allowed | `--allow-http --web-origin http://192.168.1.10:3000` | redirects use that origin | No error expected |
| Bad origin | missing, or with a path (`https://x.org/app`), or no scheme | refused | exit 2 with usage |
| Missing input | `--in nope.json` | refused | exit 2 names the file |
| `--out` | `--out realm.json` | file written, nothing on stdout | No error expected |
| Manual block | `<!-- ci:manual -->` + a bash block | not run; `--list` shows `manual` | No error expected |
| Compose config | `.env.example` values | `docker compose config --quiet` exits 0 | a missing required variable fails |

</intent-contract>

## Code Map

- `docs/operations/install.md:44-59,78-120,123-136,272-320` -- Fleet install prerequisites, Secrets + realm step, SMTP/Phase Two note (what to enable in the realm), site values, verification checklist ("Sign-in from a phone and a browser"). Link targets; read-only.
- `docs/operations/split-dns.md:22,37,58,132,151,169,199,236` -- headings to link by anchor (token, records, node resolver, phones, verify, private-CA fallback). Cloudflare only (:18-20). Read-only.
- `docs/operations/restore.md` -- Epic 2 restore runbook; link.
- `deploy/SECRETS.md:18-28,30-48,71-127,139-141` -- the Secret table, push section, creation commands, permanent admin. Link; do not restate keys beyond naming each Secret.
- `deploy/charts/README.md:73-84` -- prerequisites 1-4 (realm "with the redirect URIs of your host names"); point item 3 at `prepare-realm.sh`.
- `deploy/fleet/values.example.yaml:22-48` -- site values the adopter edits (hosts, issuer, invitation redirect, ACME email).
- `deploy/rke2/README.md` -- Traefik HelmChartConfig (applied by the `rke2-traefik` bundle; don't apply by hand).
- `aspire/keycloak/realms/coldframe-realm.json:6-11,26,29-38,45-63,81-97` -- realm flags, SMTP placeholders, web client (localhost redirects at :59,:63), mobile client. Input of the script; read-only.
- `aspire/keycloak/themes/coldframe/email/messages/messages_en.properties:5-7` -- the home-network hint text to quote.
- `apps/ts/web/src/lib/server/auth-handle.ts:10-12` -- web callback paths.
- `apps/kt/android/build.gradle.kts:13,21,28,40-56` and `packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/signin/CoreConfig.kt:26-48` -- Android properties, applicationId, redirect scheme, `.invalid` defaults.
- `apps/swift/ios/Config/Coldframe.xcconfig:3-34`, `apps/swift/ios/project.yml:6,36-45`, `apps/swift/ios/Config/.gitignore:1` -- iOS keys, URL escape, XcodeGen, Kotlin framework pre-build (needs JDK 25 + Android SDK), bundle id.
- `apps/cs/server/Notifications/Push/PushOptions.cs:60-103`, `deploy/charts/server/values.yaml:77-87` -- server push options; topic = bundle id.
- `docs/quickstart.md:152-231` and `docs/bench/push-checklist.md:57-101` -- existing runnable app/push commands to reuse and link.
- `deploy/images/README.md:6-90` -- images, ports, env vars for Compose. `deploy/charts/{server,web,keycloak,temporal,nats}/templates/*.yaml` -- exact env var wiring (server `deployment.yaml:51-127`, web `:34-52`, keycloak `:44-129`, temporal `:39-58`, nats args `:28-31`).
- `docs/check-commands.sh:1-20,59-80,160-195` -- marker parsing and dispatch; add `ci:manual`. `docs/test.sh` -- fixture tests in `expect/pass/failed` style; add the manual-marker tests.
- `docs/check-links.sh:33` -- default roots.
- `.github/workflows/docs.yml:51-65` -- `links` job to extend; triggers already include `deploy/**`.
- `.gitignore:12,463` -- `*.env` ignored (so `.env.example` is not), any `build/` ignored; check new paths with `git check-ignore`.
- `deploy/release/test.sh` -- test style to copy for `deploy/realm/test.sh`.

## Tasks & Acceptance

**Execution:**
- `deploy/realm/prepare-realm.sh` -- the realm writer described in Always, with `--help`.
- `deploy/realm/test.sh` -- the matrix rows for the script, in `deploy/release/test.sh` style, with `jq` assertions on the output.
- `docs/deploy/README.md` -- the six-section guide (plus a short "Before you start" list: hardware for the server, a release tag, what costs money, and "Afterwards": upgrades, restore, verification).
- `docs/build/apps.md` -- rewrite as the build-your-own-apps guide: prerequisites (Apple Developer Program, Firebase project, JDK 25, Android SDK, Xcode + XcodeGen), choosing ids, Android (config, debug install, signed release install, FCM), iOS (local xcconfig, team, push entitlement, install on a device), server push (`coldframe-push`, topic), troubleshooting (Unreachable notice = unconfigured URLs; sign-in redirect errors = scheme/realm mismatch), which version. Every bash block marked `ci:manual`.
- `docs/check-commands.sh`, `docs/test.sh` -- the `ci:manual` marker and its tests (not run, listed, wrong-argument form fails like other unknown markers).
- `deploy/compose/compose.yaml`, `deploy/compose/.env.example`, `deploy/compose/postgres-init.sql` (or `.sh`), `deploy/compose/README.md` -- the reference example and how to start it (`prepare-realm.sh --allow-http …`, `cp .env.example .env`, `docker compose up -d`).
- `.github/workflows/docs.yml` -- the two new `links` steps. `docs/check-links.sh` -- add `deploy/compose/README.md` to default roots.
- `deploy/charts/README.md` (prerequisite 3), `docs/operations/install.md` (realm sentence in step 2), `deploy/README.md:14`, `README.md`, `docs/quickstart.md:469` -- point to `docs/deploy/`, `prepare-realm.sh` and `deploy/compose/`.

**Acceptance Criteria:**
- Given `docs/deploy/README.md`, when read top to bottom, then the six steps appear in the AC order, each with the commands or the runbook link it needs, and it links `install.md`, `split-dns.md`, `restore.md` and `deploy/SECRETS.md`.
- Given the build-your-own-apps section, when followed, then it states where the Server URL and Keycloak issuer are set for each platform at build time, how to use one's own APNs key and FCM project (app and Server side), and how to install on a phone.
- Given `deploy/compose/`, when read, then both the README and `compose.yaml` say it is a reference example only, not the supported path.
- Given a pull request, when the `Docs` workflow runs, then links resolve, `deploy/realm/test.sh` passes and `docker compose config` validates the example.

## Spec Change Log

## Review Triage Log

### 2026-10-19 — Review pass
- verdicts: 27 findings — high 0, medium 6, low 18, false 3, maybe-false 0
- findings:
  - `[low]` `[reject]` (blind) The deploy guide's command blocks are never checked by check-commands.sh — they need a cluster and a node, so CI could only list them; widening the checker's scope to docs/deploy is new scope, not a direct fix, and nothing there runs unattended.
  - `[medium]` `[patch]` (blind) Nothing creates the `coldframe` namespace before the Secrets: deploy/SECRETS.md only sets NS=coldframe — section 3 now runs `kubectl create namespace coldframe` first.
  - `[medium]` `[patch]` (blind) Section 2 runs bare `kubectl` on the node, but RKE2's bin dir is not on PATH — the block now exports `/var/lib/rancher/rke2/bin` (commented as for the node).
  - `[low]` `[patch]` (blind) The stack install (GitRepo revision, bundles Ready) was buried in section 5 — it has its own `### Install the stack` heading; the six sections are unchanged.
  - `[medium]` `[patch]` (blind) Nothing says to open inbound 443 on the node; firewalld blocks it on Fedora/RHEL and sign-in times out — section 2 has the firewall note with the firewall-cmd commands and RKE2's guidance link.
  - `[low]` `[patch]` (blind) Compose step 1 hardcoded the realm origin, unrelated to COLDFRAME_HOST — it now uses `http://<COLDFRAME_HOST>:3000` and says it must match .env exactly.
  - `[low]` `[patch]` (blind) Compose start-up: temporal-namespace waits forever; Keycloak has no healthcheck — the wait is bounded (200 × 3 s, then exit 1). The Keycloak part is not a defect: the Server's JWT bearer and the web app fetch OIDC metadata lazily, and every long-running service has `restart: unless-stopped`.
  - `[medium]` `[patch]` (blind) The empty-env CI check passes even if one variable loses its `:?` guard — replaced by deploy/compose/test.sh, which drops each guarded variable from .env.example in turn and asserts config fails naming it (17 tests), run in the Docs step.
  - `[low]` `[patch]` (blind) realm test gaps — added the `--web-origin=`/`--out=` form, the empty `--web-origin=` error and a no-`localhost` assertion (28 tests). Port bounds, a missing jq and same in/out file were not added: same in/out works (jq reads before the write), and the others are unlikely and need new guards.
  - `[low]` `[patch]` (blind) apps.md cited CoreConfig.kt and build.gradle.kts by line number — it now names `REDIRECT_SCHEME` and the `oidcRedirectScheme` placeholder.
  - `[false]` `[reject]` (blind) apps.md contradicts CoreConfig.kt's "reversed domain of the application id" comment — the comment describes how the constant was named; the scheme is a constant independent of applicationId (build.gradle.kts sets the placeholder literally), so "does not have to match your ids" is true; the guide also names both realm fields.
  - `[low]` `[patch]` (blind) The signed-release block assumes `$ANDROID_HOME` though the page offers `sdk.dir` — it now says ANDROID_HOME must be exported for that block.
  - `[low]` `[patch]` (blind) The apps.md intro said every block needs a phone/Xcode/SDK/cluster, but `git checkout` does not — reworded to "most need".
  - `[low]` `[patch]` (blind) Re-running `kubectl create configmap coldframe-realm` fails with AlreadyExists — the guide and install.md show the `--dry-run=client -o yaml | kubectl apply -f -` form and note it only matters before Keycloak's first start.
  - `[low]` `[reject]` (blind) `--out coldframe-realm.json` lands in the repository root — it holds only placeholders and redirect URIs (no secret), shows up in `git status`, and install.md writes coldframe-values.yaml the same way; a root ignore rule is more than a direct fix.
  - `[low]` `[patch]` (edge) temporal-namespace loops forever on a broken Temporal — same root cause and fix as the blind start-up row.
  - `[low]` `[patch]` (edge) The realm origin can differ from COLDFRAME_HOST — same root cause and fix as the blind Compose origin row.
  - `[low]` `[reject]` (edge) prepare-realm.sh accepts a port above 65535 or a default port — a typo here is unlikely and fails visibly at the first sign-in; rejecting it needs new range and normalisation guards.
  - `[low]` `[reject]` (edge) A host of only dots or hyphens passes the origin regex — no one types that; tightening the regex for it is a guard for an unshown case.
  - `[low]` `[patch]` (edge) Compose interpolates `$` in .env values — .env.example now says to use the openssl commands and single-quote any value containing `$`.
  - `[medium]` `[patch]` (verification-gap) The Compose guard check notices only when every guard is lost — same root cause and fix as the blind CI-guard row.
  - `[low]` `[patch]` (verification-gap) The `--opt=value` form of prepare-realm.sh is untested — same fix as the blind realm-test row.
  - `[medium]` `[reject]` (intent) Under the outcome reading the story needs a human (domain, Cloudflare token, Apple/Firebase consoles, phones), but the diff records no awaiting-operator — the fix is spec bookkeeping; finalization sets status awaiting-operator with operator_actions.
  - `[false]` `[reject]` (intent) The guide walks through Secrets and the realm more narrowly than "creating every Secret" and "Phase Two setup" — section 3 creates the namespace, names every Secret of deploy/SECRETS.md and links its creation commands; section 4 writes the realm and creates the ConfigMap, and states what Phase Two features the realm turns on, which the image and realm ship, so there is nothing further to configure.
  - `[low]` `[reject]` (intent) The deploy guide's own blocks are not exercised — same as the first blind row.
  - `[false]` `[reject]` (intent) The Compose env var names are only a manual check — the verification-gap layer compared them with the server, web and keycloak chart templates and the web config.ts: they match.
  - `[low]` `[reject]` (intent) No Docs workflow run is shown — the PR run is that verification; listed under residual risks.

## Design Notes

**Why a realm script.** "Edit the realm for your hosts" is the step most likely to go wrong silently: a wrong redirect URI only shows up as a Keycloak error at the first sign-in. A jq rewrite of exactly two fields is small, testable in CI, and serves both the Fleet path and Compose.

**Why `ci:manual`.** `docs/build/apps.md` sits under the command checker's default glob, so its blocks must carry a marker; reusing `ci:hardware` would wrongly suggest an espflash check.

## Verification

**Commands:**
- `deploy/realm/test.sh` -- all pass.
- `docs/test.sh` -- all pass (including the new marker tests).
- `docs/check-links.sh` -- exit 0.
- `docs/check-commands.sh --list` -- every block in `docs/build/*.md` marked; apps.md blocks show `manual`.
- `docker compose -f deploy/compose/compose.yaml --env-file deploy/compose/.env.example config --quiet` -- exit 0.
- `git check-ignore deploy/compose/.env.example docs/deploy/README.md deploy/realm/prepare-realm.sh` -- nothing ignored.

**Manual checks (if no CLI):**
- Every env var name in `compose.yaml` matches the chart templates / `deploy/images/README.md`.
- Every Gradle property / xcconfig key in `apps.md` exists in the build files.

## Auto Run Result

Status: awaiting-operator

**Summary.** An ordered adopter guide, `docs/deploy/README.md`, takes a maker from a domain to a first sign-in in six numbered sections: domain and DNS token; RKE2 and Fleet; Secrets; Keycloak realm and Phase Two; split DNS (with the stack install); first sign-in. It links the Epic 2 runbooks (`install.md`, `split-dns.md`, `restore.md`) and `deploy/SECRETS.md` by section instead of copying them. It adds what no runbook had: installing RKE2, kubectl on the node, the firewall, the namespace, writing the realm, and the first sign-in with the Owner and the permanent Keycloak admin. A new tested script, `deploy/realm/prepare-realm.sh`, writes the realm for the adopter's web origin. `docs/build/apps.md` is now the full build-your-own-apps guide: build-time Server URL and issuer for each platform, choosing ids and the fixed redirect scheme, Firebase and APNs setup on the app and Server side, debug and signed-release installs, and troubleshooting. `deploy/compose/` is a Compose example labelled reference-only in its README and in `compose.yaml`. The `Docs` workflow now tests the realm writer and validates the Compose file, including every required variable.

**Files changed**
- `docs/deploy/README.md`: the new six-section deploy guide.
- `docs/build/apps.md`: the build-your-own-apps guide; every command block marked `ci:manual`.
- `deploy/realm/prepare-realm.sh`, `deploy/realm/test.sh`: the realm writer and its 28 tests.
- `deploy/compose/compose.yaml`, `.env.example`, `postgres-init.sh`, `README.md`, `.gitignore`, `test.sh`: the reference example, its settings template, the database init, the guide and the guard tests (17).
- `docs/check-commands.sh`, `docs/test.sh`: the `ci:manual` marker and its tests.
- `docs/check-links.sh`: `deploy/compose/README.md` added to the default roots.
- `.github/workflows/docs.yml`: the realm-writer and Compose steps in the `links` job.
- `README.md`, `deploy/README.md`, `deploy/charts/README.md`, `docs/operations/install.md`, `docs/quickstart.md`, `docs/build/README.md`: links to the guide, the script and the Compose example.

**Review findings.** 27 findings: high 0, medium 6, low 18, false 3, maybe-false 0.
- **Patched (13 root causes):** 4 medium (namespace, kubectl PATH, firewall 443, the per-variable Compose guard test) and 9 low (stack-install heading, Compose realm origin, bounded Temporal wait, realm test gaps, line-number citations, ANDROID_HOME, apps.md intro wording, ConfigMap replace, `$` in .env).
- **Deferred:** none.
- **Rejected,** each with its reason in the triage log: unchecked deploy-guide blocks (twice), the realm file in the repo root, port bounds and default ports, a dots-only host, the awaiting-operator bookkeeping (handled here), the unobserved CI run, and three false findings (CoreConfig comment, narrow Secrets/realm walk-through, Compose env names).

**Follow-up review: recommended (true).** Four medium entries were patched. The unverified risk is the new guide steps nobody has run on a real host (the RKE2 install, kubectl PATH and firewalld commands, the namespace order) and the two new Docs steps, which have not run on GitHub yet.

**Verification.**
- `deploy/realm/test.sh`: 28 passed. `deploy/compose/test.sh`: 17 passed. `docs/test.sh`: 49 passed.
- `docs/check-links.sh`: 497 links and paths in 27 files resolve.
- `docs/check-commands.sh`: 19 blocks in 4 files pass (firmware builds and espflash subcommands); `--list` shows the five apps.md blocks as `ci:manual`.
- `docker compose -f deploy/compose/compose.yaml --env-file deploy/compose/.env.example config --quiet`: exit 0; with an empty env file it fails naming each missing variable.
- `git check-ignore`: none of the new files is ignored.
- The implementer started postgres, the Temporal schema and namespace one-shots, Temporal and NATS from the Compose file locally with Podman: the four databases and the `coldframe` namespace were created.

**Residual risks.**
- No published release exists yet, so the Keycloak, migrations, Server and web services of the Compose example have never started.
- The deploy guide and the apps guide have not been followed on real hardware, a real domain or real phones; that is Story 10.5 and the operator actions above.
- The SELinux `chcon` hint in the Compose README is untested.
- `shellcheck` and `actionlint` were not available locally; the new Docs steps have not run on GitHub yet.
