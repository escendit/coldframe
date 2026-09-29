---
title: 'Helm charts with a fixed Secret contract'
type: 'feature'
created: '2026-09-28'
status: 'awaiting-operator'
baseline_revision: 'c9e61b4f9dc43d74db9fb00eb67f332e70a61037'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-2-context.md'
warnings: [oversized]
deferred: []
operator_actions:
  - 'Add the CI job "Charts" as a required status check on main (the chart smoke install runs inside the already-listed "Images / Build, structure-test and smoke" check).'
  - 'After the first release, make the GHCR package ghcr.io/escendit/coldframe/keycloak public (Package settings > Change visibility).'
  - 'Tag the first release as v0.1.0 to match the chart versions, or bump every deploy/charts/*/Chart.yaml version (and the server, web, keycloak appVersion) to the intended version before pushing the tag.'
  - 'Watch the first pull-request CI run and confirm the k3d chart smoke install in the Images job passes (it has only been run on kind locally).'
---

<intent-contract>

## Intent

**Problem:** The images from Story 2.1 cannot be installed on a cluster: there are no Helm charts, no list of the Secrets an adopter must create, and nothing guarantees the migration Job runs before the Server rolls (Story 2.2, AD-15, AD-22). No public image carries Phase Two Keycloak with `keycloak-temporal-extensions`, so a Keycloak chart would have nothing to pull.

**Approach:** Add five self-authored charts under `deploy/charts/` (`server` incl. the migration Job, `web`, `keycloak`, `temporal`, `nats`). They read every credential from fixed-name Secrets documented in `deploy/SECRETS.md` and never render a Secret. Publish a `keycloak` image alongside the other three. Add static chart checks (helm lint, helm-unittest, kubeconform, Secret scan) to CI, and a k3d smoke install to the image verification so every pod becomes ready and the Server reports healthy.

## Boundaries & Constraints

**Always:**
- Charts are `apiVersion: v2`. Each chart's `version` is `0.1.0`; the Coldframe charts (`server`, `web`, `keycloak`) have `appVersion: "0.1.0"`, and their image tag defaults to `.Chart.AppVersion` (release tag = image tag = appVersion). `temporal` and `nats` have the upstream version as `appVersion`.
- Images: `ghcr.io/escendit/coldframe/{server,migrations,web,keycloak}`, `docker.io/temporalio/server:1.31.2` and `docker.io/temporalio/admin-tools:1.31.2` (1.31.3, the epic pin, is not published; 1.31.2 is what the Aspire stack already runs), `docker.io/library/nats:2.15.0`. Repository, tag and pull policy are overridable per chart.
- Resource names default to the release name. Cross-chart addresses are values whose defaults assume releases `nats`, `temporal`, `keycloak`, `server`, `web` in one namespace, and a database at `coldframe-db-rw:5432` (the CNPG `-rw` Service Story 2.3 creates).
- Credentials reach pods only as `secretKeyRef` env vars or Secret volumes naming a Secret and key from `deploy/SECRETS.md`. Secret names are fixed in templates, not values. Connection strings are composed with Kubernetes `$(VAR)` expansion from those env vars.
- Secret contract (namespace of the release): `coldframe-db-server`, `coldframe-db-temporal`, `coldframe-db-keycloak` (keys `username`, `password`; `kubernetes.io/basic-auth`, the shape CNPG consumes), `coldframe-keycloak-admin` (`username`, `password`), `coldframe-oidc-clients` (`web-client-secret`, `server-client-secret`). Documented but not yet consumed: `coldframe-smtp` (`host`, `port`, `username`, `password`, `from`; Epic 9), `coldframe-push` (`apns-key.p8`, `apns-key-id`, `apns-team-id`, `fcm-service-account.json`; Epic 6), `coldframe-dns01` (`api-token`; Story 2.4), `coldframe-enrolment-key` (`private-key.pem`; Epic 3), `coldframe-backup-s3` (`access-key-id`, `secret-access-key`; Story 2.3).
- Server: `replicas: 1` fixed (not a value), `strategy: Recreate`, `terminationGracePeriodSeconds` (default 60) strictly greater than the host shutdown timeout it passes to .NET (default 45 s), probes on `/.well-known/healthz/live` and `/ready` at 8080, a startup probe for slow silo start. The migration Job is a `pre-install,pre-upgrade` hook (delete policy `before-hook-creation`, `backoffLimit` small, `activeDeadlineSeconds` set) using the `migrations` image with the Server's appVersion, so a failed migration fails the release before the Deployment changes.
- Temporal: one Deployment running all services on PostgreSQL (`postgres12` plugin, databases `temporal` and `temporal_visibility`). A `pre-install,pre-upgrade` hook Job (admin-tools) sets up and updates both schemas and is idempotent. A `post-install,post-upgrade` hook Job creates namespace `coldframe` if absent, retrying until the frontend answers. Service port 7233.
- Keycloak: `start --optimized`, database from `coldframe-db-keycloak`, bootstrap admin from `coldframe-keycloak-admin`, `KC_HTTP_ENABLED=true` in-cluster, `KC_PROXY_HEADERS=xforwarded`, `hostname` value (empty → `KC_HOSTNAME_STRICT=false`), Temporal event-listener env as in `AppHost.cs`, `COLDFRAME_WEB_CLIENT_SECRET`/`COLDFRAME_SERVER_CLIENT_SECRET` from `coldframe-oidc-clients`. Optional `realmImport.configMap`: when set, mounts it at `/opt/keycloak/data/import` and adds `--import-realm`. Probes on management port 9000.
- NATS: one replica, JetStream on an `emptyDir` (disposable, AD-5), monitoring port 8222 for probes, client port 4222.
- Every pod: `runAsNonRoot`, numeric `runAsUser`, `seccompProfile: RuntimeDefault`; every container: `allowPrivilegeEscalation: false`, capabilities drop `ALL`. Services are `ClusterIP` only.
- Keycloak image: `aspire/keycloak/Dockerfile` follows the Story 2.1 image rules (build stages `--platform=$BUILDPLATFORM`, final stage without `RUN`, numeric `USER`, explicit final `FROM`); the Quarkus build runs in a build stage and its output is copied. The AppHost keeps using it.
- Tool pins in CI: helm v4.2.2, helm-unittest v1.1.2, kubeconform v0.8.0 (schemas for Kubernetes 1.36.0, strict), k3d v5.9.0 with `rancher/k3s:v1.36.4-k3s1`. Downloads are sha256-checked; actions pinned to a commit as in `ci.yml`.

**Never:**
- No Secret, ConfigMap or value holding a credential rendered by any chart; no `lookup`, `randAlphaNum` or generated passwords.
- No Ingress, IngressRoute, cert-manager objects, CNPG objects or Fleet bundles (Stories 2.3–2.5). No Service of type `LoadBalancer`/`NodePort`.
- No upstream chart dependencies (keeps the Secret contract and offline tests under our control).
- No DDL at Server startup and no change to Server, web or migration runtime code.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Upgrade, migration ok | `helm upgrade server` with new tag | hook Job runs the new `migrations` image and completes; then the old Server pod stops and the new one starts | — |
| Upgrade, migration fails | migration Job exits 1 | release fails; Server Deployment spec unchanged | Helm reports the failed hook |
| Missing Secret | `coldframe-db-server` absent | pods stay `CreateContainerConfigError` naming the Secret | nothing rendered as a fallback |
| Rendered manifests | any chart, default or CI values | no document of kind `Secret` | chart test fails |
| Unknown Secret ref | a template references a Secret or key not in `SECRETS.md` | `deploy/charts/test.sh` fails naming it | — |
| Release version | tag `v0.2.0` while charts say `0.1.0` | release `version` job fails naming the chart | no image pushed |
| Temporal re-install | schema already current | schema Job exits 0; namespace Job leaves `coldframe` as is | — |

</intent-contract>

## Code Map

- `aspire/Coldframe.AppHost/AppHost.cs` -- authoritative env for Server (`ConnectionStrings__coldframe`, `ConnectionStrings__nats`, `Identity__*`, `Keycloak__*`, `KeycloakEvents__*`) and Keycloak (`KC_DB_*`, `KC_BOOTSTRAP_ADMIN_*`, `KC_SPI_EVENTS_LISTENER__TEMPORAL__TARGET_HOST`/`__NAMESPACE`, client-secret env names). Read-only.
- `aspire/keycloak/Dockerfile` -- Phase Two 26.6.7 + extension v0.0.1-rc.2 (commit-checked). Final stage currently runs `kc.sh build` and has no `USER`: restructure (base image user is `keycloak`, uid 1000; ports 8080/8443/9000).
- `aspire/keycloak/realms/coldframe-realm.json` -- localhost redirect URIs, so it is not packaged; adopters supply a realm ConfigMap.
- `apps/cs/server/Hosting/SiloExtensions.cs` -- ADO.NET clustering on the migrated tables, `listenOnAnyHostAddress: true`, silo 11111 / gateway 30000. Read-only.
- `deploy/images/README.md` -- per-component env tables, health paths, UIDs (server/migrations 1654, web 1000): the chart env must match.
- `deploy/images/smoke.sh` -- the Server is ready without Keycloak; Temporal/NATS/Postgres are needed. Pattern for the k3d smoke (trap cleanup, `wait_until`, logs on failure).
- `deploy/images/test.sh` (lines ~88–113) -- Dockerfile rule loop over a hard-coded list: add `aspire/keycloak/Dockerfile`.
- `.github/workflows/images-verify.yml` -- `COMPONENTS` list, loads `coldframe/<c>:ci` images, CST loop: add keycloak and the chart smoke step.
- `.github/workflows/release.yml` -- `version` job and publish matrix (lines ~57–73): add keycloak and the chart version check.
- `.github/workflows/ci.yml` -- job conventions; add a `charts` job.
- `deploy/README.md` -- Epic 2 table.
- Local tooling: helm v4.2.2, kubectl, podman 5.8 (use `DOCKER_HOST=unix:///run/user/1000/podman/podman.sock`), skopeo, python3+PyYAML, jq; k3d/kubeconform/helm-unittest must be fetched into the scratchpad.

## Tasks & Acceptance

**Execution:**
- `aspire/keycloak/Dockerfile` -- restructure per Always; verify the AppHost-built image still starts (`start --optimized`) -- a publishable Keycloak image.
- `deploy/images/keycloak.cst.yaml` -- entrypoint `kc.sh`, user `1000`, ports 8080/9000, extension JAR present in `/opt/keycloak/providers`.
- `deploy/images/test.sh`, `deploy/images/README.md` -- include the Keycloak Dockerfile in the rules loop; document the image and its env.
- `deploy/charts/{server,web,keycloak,temporal,nats}/` -- `Chart.yaml`, `values.yaml`, `templates/` (`_helpers.tpl`, workloads, Services, hook Jobs), `tests/*_test.yaml` (helm-unittest), `ci-values.yaml` where CI overrides are needed -- per Always.
- helm-unittest suites must cover: no Secret document in any chart; every `secretKeyRef` name/key; Server `replicas: 1`, `Recreate`, grace > shutdown timeout, probes; migration Job hook annotations and image tag = appVersion; Temporal hook annotations and weights; Keycloak realm import on/off; security contexts.
- `deploy/charts/test.sh` -- for every chart: `helm lint --strict`, `helm unittest`, `helm template` (default and CI values) piped to kubeconform strict; fail on any rendered `kind: Secret`; fail on any Secret name/key reference absent from `deploy/SECRETS.md`. Needs only helm, the plugin, kubeconform and python3.
- `deploy/charts/check-version.sh <version>` -- fail naming each `Chart.yaml` whose `version` (all charts) or `appVersion` (server, web, keycloak) differs; covered in `test.sh` with a matching and a mismatching version.
- `deploy/charts/smoke.sh <server> <web> <migrations> <keycloak>` -- create a k3d cluster (pinned k3s), import the images, create namespace `coldframe`, create placeholder Secrets with random values via `kubectl create secret`, deploy a test-only PostgreSQL 18.6 (`deploy/charts/testdata/postgres.yaml`, roles and databases `coldframe`, `temporal`, `temporal_visibility`, `keycloak`) as `coldframe-db-rw`, then `helm install --wait` nats, temporal, keycloak, server, web with image overrides. Assert every pod Ready or Job-owned `Succeeded`, and `GET /.well-known/healthz` on the Server Service (API-server proxy) returns 200 `Healthy`. Then `helm upgrade server` and assert the migration hook Job completed. Logs and events on failure; cluster always deleted (`trap`).
- `.github/workflows/images-verify.yml` -- add `keycloak:aspire/keycloak/Dockerfile` to `COMPONENTS`; install pinned k3d and helm; add a "Chart smoke install" step running `deploy/charts/smoke.sh` with the `:ci` images.
- `.github/workflows/release.yml` -- add keycloak to the publish matrix; run `check-version.sh` in the `version` job.
- `.github/workflows/ci.yml` -- `charts` job: install pinned helm, helm-unittest, kubeconform; run `deploy/charts/test.sh`.
- `deploy/SECRETS.md` -- one row per Secret: name, type, keys, consumer (chart or story/epic), example `kubectl create secret` command with placeholders; states that charts never create Secrets.
- `deploy/charts/README.md` + `deploy/README.md` -- chart list, install order (nats → temporal → keycloak → server → web, after the database), values that matter, upgrade behaviour, local checks.

**Acceptance Criteria:**
- Given `deploy/charts/`, when inspected, then it contains charts for the Server (with the migration Job), web BFF, Keycloak (Phase Two + extension image), Temporal and NATS JetStream, and `deploy/SECRETS.md` lists every Secret they reference by fixed name and key.
- Given the charts, when `deploy/charts/test.sh` runs, then helm lint, helm-unittest and kubeconform pass, and the run fails if any chart renders a Secret.
- Given a Helm install or upgrade of `server`, when it runs, then the migration hook Job completes before the single-replica Recreate Deployment changes.
- Given a k3d cluster with placeholder Secrets, when `deploy/charts/smoke.sh` runs, then every pod becomes ready and the Server health endpoint returns 200 `Healthy`.
- Given a pull request, when CI runs, then the `charts` job and the chart smoke install in the `images` job both block on failure.

## Spec Change Log

- 2026-09-28 (implementation): the Phase Two base image's `keycloak` user is UID/GID 2000, not 1000 as the Code Map assumed. The Keycloak image uses `USER 2000`, `keycloak.cst.yaml` expects `2000`, and the chart's `runAsUser` defaults to 2000. Intent unchanged (numeric base-image user).
- 2026-09-28 (implementation): k3s cannot start on this host's rootless Podman (`failed to find cpuset cgroup (v2)`: the cpuset controller is not delegated to the user session). `smoke.sh` takes `SMOKE_CLUSTER=kind` (kindest/node v1.36.4, images imported with `ctr` because kind v0.31 cannot read that node's containerd config) as the local fallback; CI keeps k3d.
- 2026-09-28 (implementation): Temporal 1.31.2's embedded config needs a dynamic config file, so the temporal chart renders a credential-free ConfigMap (`dynamicConfig` value, default `{}`).

## Review Triage Log

### 2026-09-28 — Review pass
- verdicts: 27 findings — high 0, medium 4, low 18, false 4, maybe-false 1
- findings:
  - `[medium]` `[patch]` Blind: rotating a client secret or DB password per SECRETS.md breaks sign-in/DB access (the realm import never overwrites; role passwords live in PostgreSQL) — SECRETS.md now says to change the value at its source first, then the Secret, then restart.
  - `[low]` `[patch]` Blind: shutdown timeout never verified — the implementer checked it once in a scratch app, but nothing guards it. Added `tests/cs/server.tests/Hosting/ShutdownTimeoutTests.cs` (same root as the verification-gap row).
  - `[low]` `[patch]` Blind: the failed-migration smoke step accepts any helm failure — smoke.sh now also asserts `job/server-migrations` has `status.failed >= 1`.
  - `[low]` `[reject]` Blind: the "Missing Secret" matrix row is never tested at runtime — Kubernetes guarantees `CreateContainerConfigError` for non-optional refs. `check-manifests.py` now fails any optional Secret ref (fixture `optional-secret.yaml`), and no chart renders a Secret. A runtime step would add smoke complexity for platform behaviour.
  - `[low]` `[reject]` Blind: helm pins duplicated in two workflows; kubeconform schemas fetched from an unpinned remote — two explicit copies with sha256 checks fail loudly on mismatch, and a composite action adds structure. Schemas for a fixed Kubernetes version do not change.
  - `[low]` `[patch]` / `[reject]` Blind: charts never packaged or published; rc tags need a chart bump — the README now says pre-release tags need the charts at `X.Y.Z-rc.N` first. Publishing is rejected: Fleet (2.5) reads charts from Git, and the intent does not ask for a chart registry.
  - `[low]` `[patch]` Blind: the Keycloak version is hard-coded in two FROM lines; CST hard-codes JAR names — deploy/images/test.sh now asserts both `phasetwo-keycloak` tags match (same root as the verification-gap row). The CST JAR names are rejected: a failing CST names the missing path, which points straight at the bump.
  - `[low]` `[patch]` Blind: the Keycloak "no cache cluster is configured" comment is inaccurate — reworded: one replica is what is supported and tested, and the distributed cache for more is not configured.
  - `[low]` `[reject]` Blind: no DB TLS values — Npgsql and pgjdbc default to `sslmode=prefer`, so TLS is used whenever CNPG offers it; traffic is in-cluster. Adding TLS values is new surface.
  - `[low]` `[reject]` Blind: the `;` password rule is not enforced; PGPASSWORD alternative — documented in SECRETS.md; CNPG-generated passwords are alphanumeric. Switching to PGPASSWORD changes an unverified runtime path.
  - `[low]` `[reject]` Blind: `extraEnv` can bypass the Secret contract — adopter-controlled, and the values comments say non-secret. Guarding it adds template logic.
  - `[low]` `[reject]` Blind: readOnlyRootFilesystem is inconsistent and resources default to `{}` — not required by the spec. Keycloak and Temporal write to their filesystems. Resource sizing on a single home node is an adopter choice.
  - `[low]` `[reject]` Blind: the NATS JetStream store is not capped below the emptyDir limit — the store is disposable (AD-5) and the hint stream is small; a cap needs a config file.
  - `[low]` `[reject]` Blind: check-version.sh does not require the Coldframe charts to exist; single quotes not stripped — a renamed chart or quote style is unlikely, and fixing it adds a guard.
  - `[medium]` `[patch]` Blind: SECRETS.md says the bootstrap admin Secret is "first start only", but it is referenced on every start — the row now says it must stay, and adopters are told to replace the temporary admin with a permanent one.
  - `[low]` `[reject]` Edge: the migrations Job name exceeds 63 characters for release names over 52 characters — unrealistic release names, and the API server rejects them loudly.
  - `[low]` `[reject]` Edge: Temporal hook Job names exceed 63 characters with a long fullnameOverride — same reason.
  - `[medium]` `[patch]` Edge: test.sh hard-codes 0.1.0, so the Charts job breaks on every version bump — the tests now read the current version from `server/Chart.yaml` and test `<current>-mismatch`.
  - `[low]` `[patch]` Edge: helm stderr is mixed into the rendered YAML — `render_and_check` captures stderr separately and prints it only on failure.
  - `[medium]` `[patch]` Edge: default values pair an http authority/issuer with the HTTPS-only settings and fail only at runtime — the server and web templates now `fail` at render time on that combination. The defaults are an `https://auth.example.invalid` placeholder, `ci-values.yaml` sets the in-cluster http issuer, and unit tests cover both.
  - `[low]` `[patch]` Edge: the chart smoke can push the verify job past its 60-minute timeout and lose the trap diagnostics — timeout-minutes raised to 90.
  - `[false]` `[reject]` Edge (claim): the spec says Keycloak UID 1000 but the image uses 2000 — the Spec Change Log records the correction; the image, CST and chart agree on 2000.
  - `[low]` `[patch]` Verification gap: DOTNET_SHUTDOWNTIMEOUTSECONDS is never checked against the running host — `ShutdownTimeoutTests` builds the host with the Server's service defaults and asserts 45 s (passes).
  - `[low]` `[patch]` Verification gap: the two Keycloak FROM tags are not checked for equality — rule added to deploy/images/test.sh, confirmed to fail on a mismatch.
  - `[false]` `[reject]` Intent: the spec is left at in-progress with no `operator_actions` — finalization happens after review, and this pass writes both.
  - `[maybe-false]` `[reject]` Intent: the k3d path has never run (local verification used kind) — k3s cannot start on this host's rootless Podman. The first CI run settles it; if the k3d path failed it would fail loudly (low). Recorded as a residual risk.
  - `[false]` `[reject]` Intent: making Charts a required check is a repository setting — correct, and it is an operator action, not a code defect. Listed under `operator_actions`.

## Design Notes

- Helm hooks give "migration first, must succeed" without an init container racing the old pod; `Recreate` gives stop-then-start. Fleet (Story 2.5) runs Helm hooks.
- Secret names are fixed in templates so `SECRETS.md` is the whole contract; making them values would let the docs and the install drift.
- Credentials for the Server connection string, e.g.:
  ```yaml
  - name: DB_USERNAME
    valueFrom: { secretKeyRef: { name: coldframe-db-server, key: username } }
  - name: ConnectionStrings__coldframe
    value: "Host={{ .Values.database.host }};Port=5432;Database=coldframe;Username=$(DB_USERNAME);Password=$(DB_PASSWORD)"
  ```
- The .NET host shutdown timeout is set through host configuration (`DOTNET_SHUTDOWNTIMEOUTSECONDS`); verify it takes effect.
- Operator actions to record at finalization: add the `Charts` check as required; make `ghcr.io/escendit/coldframe/keycloak` public after the first release; the first release tag must match the chart versions (`v0.1.0`, or bump the charts first).

## Verification

**Commands:**
- `deploy/charts/test.sh` -- expected: all pass (tools fetched into the scratchpad)
- `deploy/images/test.sh` -- expected: all pass
- `docker buildx build -f aspire/keycloak/Dockerfile -t coldframe/keycloak:dev --load .` and `--platform linux/arm64` -- expected: success; CST passes with `deploy/images/keycloak.cst.yaml`
- `deploy/charts/smoke.sh coldframe/server:dev coldframe/web:dev coldframe/migrations:dev coldframe/keycloak:dev` -- expected: exit 0 (k3d on rootless Podman; if the host cannot run k3d, record why and run the charts on kind instead)
- `bash -n` on every script; shellcheck and actionlint if obtainable -- expected: clean
- `dotnet build aspire/Coldframe.AppHost` -- expected: success

## Auto Run Result

Status: awaiting-operator

**Summary:** Coldframe now has five self-authored Helm charts under `deploy/charts/`:
- `server`: one replica, `Recreate`, a 60 s grace period over the host's 45 s shutdown timeout (the chart fails to render otherwise), and the migration Job as a `pre-install,pre-upgrade` hook.
- `web`
- `keycloak`: `start --optimized`, with an optional realm-import ConfigMap.
- `temporal` 1.31.2: one Deployment on PostgreSQL, an idempotent schema hook and a namespace hook.
- `nats`: JetStream on an emptyDir.

Every credential comes from fixed-name Secrets listed in `deploy/SECRETS.md`, and no chart renders a Secret. The Keycloak image (Phase Two with keycloak-temporal-extensions) is now built without emulation and published by the release as `ghcr.io/escendit/coldframe/keycloak`. The release refuses a tag that differs from the chart versions. CI gains a `Charts` job (helm lint, helm-unittest, kubeconform, the Secret-contract check), and the `Images` job gains a k3d chart smoke install.

**Files changed:**
- `deploy/charts/{server,web,keycloak,temporal,nats}/`: charts, values, templates, helm-unittest suites and CI values.
- `deploy/charts/test.sh`, `check-manifests.py`, `check-version.sh`, `smoke.sh`, `testdata/*`: static checks, the Secret-contract check with failing fixtures, the version gate and the cluster smoke run.
- `deploy/charts/README.md`, `deploy/SECRETS.md`, `deploy/README.md`: install order, values, upgrades and the Secret contract.
- `aspire/keycloak/Dockerfile`: Quarkus build moved to a build stage; numeric `USER 2000`; publishable.
- `deploy/images/keycloak.cst.yaml`, `deploy/images/test.sh`, `deploy/images/README.md`: Keycloak structure test, Dockerfile rules (including matching FROM tags), docs.
- `.github/workflows/ci.yml`, `images-verify.yml`, `release.yml`: Charts job, Keycloak build and chart smoke, Keycloak publish and chart version gate.
- `tests/cs/server.tests/Hosting/ShutdownTimeoutTests.cs`: proves `DOTNET_SHUTDOWNTIMEOUTSECONDS` sets the host's shutdown timeout.

**Review findings:** 27 findings. 13 patched (4 medium, 9 low; a split row counts as patched), 0 deferred, 14 rejected. The patches:
- the rotation and bootstrap-admin docs
- the version-agnostic check-version tests
- the render-time HTTPS guards
- separate helm stderr
- a stricter failed-migration assertion
- the shutdown-timeout test
- the Keycloak FROM-tag rule
- the cache comment
- the rc-tag doc
- the job timeout

Rejections, each with its reason in the triage log: long release names, DB TLS values, `extraEnv` guarding, resource defaults, the NATS store cap, duplicated pins, chart publishing, the `;` rule, the check-version guard, the runtime missing-Secret test, and 3 false or maybe-false intent items.

**Follow-up review recommended:** false. Patched 0 high and 4 medium, but every medium patch is a doc fix, a test fix or a render-time guard, each covered by the passing checks below. No specific unverified risk remains beyond the k3d residual risk, which only CI can settle.

**Verification performed (local, rootless Podman):**
- `deploy/charts/test.sh`: 41/41 pass.
- `deploy/images/test.sh`: 33/33 pass.
- shellcheck and actionlint: clean.
- `dotnet test --project tests/cs/server.tests`: 195/195 pass. `dotnet format --verify-no-changes` passes. The AppHost builds.
- The Keycloak image builds for amd64 and arm64, and CST passes on amd64.
- `SMOKE_CLUSTER=kind deploy/charts/smoke.sh` exits 0 after the patches. It checked that:
  - every pod is Ready or a completed Job
  - the Server's `/.well-known/healthz` answers 200 Healthy
  - Keycloak serves the imported realm
  - the Temporal hooks re-run cleanly
  - a failing migration fails the upgrade with the Deployment unchanged
  - a good upgrade re-runs the migration Job

**Residual risks:**
- The k3d path of `smoke.sh` and the new CI steps have not run yet: k3s cannot start on this host, so the smoke ran on kind. The first CI run is their real test.
- Temporal runs 1.31.2 because 1.31.3 is not published.
- Adopters must keep database passwords free of `;`.

**Operator actions owed:** see `operator_actions` in the frontmatter.
