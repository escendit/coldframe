---
title: 'Database cluster with off-node backups and tested restore'
type: 'feature'
created: '2026-09-28'
status: 'awaiting-operator'
baseline_revision: '23ed8edd22d70d47c70d44fa7cf2364bc6be3e33'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-2-context.md'
warnings: [oversized]
deferred:
  - summary: >-
      Nothing alerts when WAL archiving or base backups fail.
    evidence: |-
      ContinuousArchiving turning False is only visible through a manual kubectl check in restore.md; no PodMonitor or alert rule exists, and Epic 2 has no monitoring stack.
    location: >-
      deploy/charts/database/templates/cluster.yaml
    severity: medium
  - summary: >-
      The backup ObjectStore offers no region or endpointCA setting.
    evidence: |-
      Unverified: settle by archiving to an S3 provider that requires a non-default signing region (AWS outside us-east-1, some Backblaze or Wasabi endpoints) with only endpointURL set. Adding a region key changes the Secret contract.
    location: >-
      deploy/charts/database/templates/objectstore.yaml
    severity: medium (unverified)
  - summary: >-
      Password rotation of coldframe-db-* through CNPG managed roles is documented but never exercised.
    evidence: |-
      SECRETS.md says updating a Secret labelled cnpg.io/reload=true changes the role password; no smoke step rotates a Secret and checks the new password logs in and the old one does not.
    location: >-
      deploy/SECRETS.md
    severity: medium
operator_actions:
  - 'Provide an S3-compatible bucket off the home server, and create the Secret coldframe-backup-s3 (access-key-id, secret-access-key) with read, write, list and delete rights on it, as in deploy/SECRETS.md.'
  - 'On the home server, install cert-manager v1.21.2, CloudNativePG 1.30.1 and the Barman Cloud plugin v0.15.0 with the commands in deploy/charts/README.md (Prerequisites).'
  - 'Install the database chart on the home server with your bucket (backup.endpointURL, backup.destinationPath) and confirm ContinuousArchiving is True and the first Backup is completed.'
  - 'Once Sites, Lots and Memberships exist, follow docs/operations/restore.md once on the home server and confirm the Server is healthy and the Sites, Lots and Memberships appear in the apps.'
  - 'Watch the first pull-request CI run and confirm the k3d chart smoke install, including the backup-and-restore phase, passes (it has only run on kind locally).'
  - 'Confirm the Secret rename coldframe-db-server -> coldframe-db-coldframe before creating the Secrets on the home server.'
---

<intent-contract>

## Intent

**Problem:** The charts of Story 2.2 expect a PostgreSQL at `coldframe-db-rw:5432`, but nothing provides one outside the smoke test's throwaway Postgres: there is no managed cluster, no backup off the node, and no tested way back from a lost disk (Story 2.3, AD-15, AD-17).

**Approach:** Add a self-authored `database` chart that renders a CloudNativePG 1.30.1 `Cluster` on PostgreSQL 18 with the three roles and four databases the charts expect, and ships WAL plus scheduled base backups to an adopter-provided S3 target through the Barman Cloud plugin; its recovery mode bootstraps a fresh cluster from that target. The chart smoke install switches from the throwaway Postgres to this chart, with RustFS as the S3 target, and proves a backup-and-restore round trip. `docs/operations/restore.md` is the runbook.

## Boundaries & Constraints

**Always:**
- Chart `deploy/charts/database`: `apiVersion: v2`, `version` = release version (currently `0.1.0`), `appVersion: "18.6"`. Same helpers, labels and test style as the other charts; added to `test.sh`'s chart list (check-version.sh already covers every chart).
- Cluster name `coldframe-db` (value `clusterName`), so CNPG's Service is `coldframe-db-rw`. `instances: 1` default, `imageName` `ghcr.io/cloudnative-pg/postgresql:18.6-standard-trixie` (value), `storage.size` (default `10Gi`) and optional `storage.storageClass`. `enableSuperuserAccess: false`.
- Roles and databases: `bootstrap.initdb` creates database `coldframe` owned by role `coldframe` from Secret `coldframe-db-server`. `spec.managed.roles` manages `coldframe`, `temporal`, `keycloak` (login, `passwordSecret` = `coldframe-db-server`, `-temporal`, `-keycloak`). CNPG `Database` objects create `temporal` and `temporal_visibility` (owner `temporal`) and `keycloak` (owner `keycloak`) with `databaseReclaimPolicy: retain`. Role names are values defaulting to `coldframe`/`temporal`/`keycloak`; SECRETS.md states each Secret's `username` must equal its role name.
- Backups via the Barman Cloud plugin v0.15.0 (`barman-cloud.cloudnative-pg.io`), not the deprecated in-tree `barmanObjectStore`: an `ObjectStore` (`barmancloud.cnpg.io/v1`) with `destinationPath`, `endpointURL`, credentials from Secret `coldframe-backup-s3` (`access-key-id`, `secret-access-key`), gzip WAL and data, `retentionPolicy` (default `30d`); the Cluster lists the plugin with `isWALArchiver: true`; a `ScheduledBackup` (`method: plugin`, 6-field cron `schedule` default `0 0 3 * * *`, `immediate: true`, `backupOwnerReference: self`).
- `backup.serverName` (default: cluster name) is the archive folder. Recovery mode (`recovery.enabled`) bootstraps with `bootstrap.recovery` from an `externalClusters` entry using the plugin with `recovery.sourceServerName` (default `coldframe-db`) and optional `recovery.targetTime`; the chart `fail`s at render time when recovery is on and `backup.serverName` equals the source (the fresh cluster must archive to a new folder).
- Defaults render and lint: `backup.endpointURL` defaults to the placeholder `https://s3.example.invalid`, `backup.destinationPath` to `s3://coldframe-backups/`; `ci-values.yaml` points at RustFS.
- Pinned dependency manifests (version, URL, sha256) live in one sourced file, `deploy/charts/dependencies.env`: CNPG `cnpg-1.30.1.yaml`, plugin `manifest.yaml` v0.15.0, cert-manager v1.21.2 `cert-manager.yaml` (the plugin needs it), plus RustFS `docker.io/rustfs/rustfs:1.0.0` and `docker.io/amazon/aws-cli:2.37.4` for the smoke. Every download is sha256-checked.
- kubeconform validates the CNPG and ObjectStore kinds strictly against JSON schemas generated from those pinned CRD manifests (additionalProperties false where the CRD lists properties and does not preserve unknown fields).
- `check-manifests.py` also treats CNPG `passwordSecret` and the ObjectStore `s3Credentials` entries as Secret references, so the contract check covers the new chart.
- `docs/operations/restore.md` states the recovery point (data written after the last archived WAL segment is lost; with CNPG's default `archive_timeout` of 5 min that is at most about five minutes of writes), the steps (stop apps, uninstall, install in recovery mode with a new `backup.serverName`, verify, start apps), the verification (Server healthy; Sites, Lots, Memberships visible), and a required step to advance every Device's replay window by a safety margin before Devices reconnect (AD-17), noting the command arrives with Device support (Story 4.5) and that there are no Devices before Epic 4.

**Never:**
- No rendered Secret, no generated password, no `lookup`; no in-tree `barmanObjectStore`; no upstream chart dependency; no Ingress or TLS objects (2.4); no Fleet bundle (2.5).
- No change to Server, web or migration runtime code, or to the other charts' templates beyond what the database move needs.
- The smoke test must not keep `testdata/postgres.yaml` as the database.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| First install | CNPG, plugin, Secrets present | `coldframe-db` Ready; roles and 4 databases exist; immediate backup completes; `ContinuousArchiving` True | — |
| Restore | recovery on, new `serverName` | fresh cluster Ready with every row of the backup plus archived WAL; archives to the new folder | — |
| Restore, same folder | recovery on, `serverName` = source | render fails naming both values | `helm install` refuses |
| Unknown S3 key referenced | template uses a key not in SECRETS.md | `test.sh` fails naming it | — |

</intent-contract>

## Code Map

- `deploy/charts/nats/` -- smallest chart: copy `_helpers.tpl` shape, labels, `tests/*_test.yaml` style.
- `deploy/charts/test.sh` -- `charts=(...)` list, `render_and_check` (kubeconform args at `kubeconform_args`), fixture checks: add `database`, CRD schema location, new fixtures.
- `deploy/charts/check-manifests.py` `references()` -- add `passwordSecret` (name) and `s3Credentials.{accessKeyId,secretAccessKey,region,sessionToken}` (name, key).
- `deploy/charts/check-version.sh` -- globs every `*/Chart.yaml`; `database` is not a Coldframe-appVersion chart. No change expected.
- `deploy/charts/smoke.sh` -- replace the `testdata/postgres.yaml` block: install cert-manager, CNPG, plugin (wait for their Deployments), RustFS + bucket Job, `coldframe-backup-s3` Secret, then `helm install database` and `kubectl wait cluster/coldframe-db --for=condition=Ready`. Add the backup/restore phase after the upgrade checks. Keep `SMOKE_CLUSTER=kind` fallback, trap diagnostics (add `clusters,backups,objectstores` and operator logs).
- `deploy/charts/testdata/postgres.yaml` -- delete (replaced by the chart).
- `deploy/SECRETS.md` -- rows for `coldframe-db-*` and `coldframe-backup-s3` consumers, username = role name, rotation now reconciled by CNPG managed roles (verify before documenting).
- `deploy/charts/README.md`, `deploy/README.md` -- chart table, prerequisites (cert-manager, CNPG, plugin install commands from `dependencies.env`), install order `database` first, values table.
- `.github/workflows/images-verify.yml` -- chart smoke step comment; the step already runs `smoke.sh`. `.github/workflows/ci.yml` `charts` job -- `test.sh` now downloads the CRD manifests (network, sha256-checked).
- Local: rootless Podman host; k3s cannot start, use `SMOKE_CLUSTER=kind KIND_EXPERIMENTAL_PROVIDER=podman` and `DOCKER_HOST=unix:///run/user/1000/podman/podman.sock`. helm/kubectl/kind present; fetch kubeconform, helm-unittest, shellcheck, actionlint into the scratchpad. Images from Story 2.2 build with `docker buildx`/podman.

## Tasks & Acceptance

**Execution:**
- `deploy/charts/dependencies.env` -- pins (versions, URLs, sha256, images) -- one source for test, smoke and later Fleet.
- `deploy/charts/crd-schemas.py` -- CRD YAML on stdin → `<kind>_<version>.json` files in a directory (lower-case kind), strict as above.
- `deploy/charts/database/` -- `Chart.yaml`, `values.yaml`, `ci-values.yaml`, `templates/{_helpers.tpl,cluster.yaml,databases.yaml,objectstore.yaml,scheduledbackup.yaml}`, `tests/database_test.yaml` -- per Always; unit tests: no Secret, secret refs and keys, roles and Database owners, plugin wiring, ScheduledBackup, recovery on/off, same-folder `fail`, placeholder defaults.
- `deploy/charts/check-manifests.py` + `testdata/` fixtures -- new reference shapes; fixtures: an undocumented `s3Credentials` key fails, a documented CNPG manifest passes.
- `deploy/charts/test.sh` -- fetch and verify pinned CRD manifests into a cache dir (`KUBECONFORM_CACHE` or temp), generate schemas, add `-schema-location default -schema-location <dir>/{{.ResourceKind}}_{{.ResourceAPIVersion}}.json`; add `database`.
- `deploy/charts/testdata/rustfs.yaml` -- test-only RustFS Deployment + Service `rustfs:9000` (credentials from `coldframe-backup-s3`, restricted security context) and a bucket Job (aws-cli `s3 mb`, idempotent).
- `deploy/charts/smoke.sh` -- per Code Map; backup/restore phase: scale `server`, `web`, `keycloak`, `temporal` to 0; insert marker A into a `smoke_marker` table in `coldframe`; create a `Backup` (plugin) and wait `completed`; insert marker B; record a fingerprint (row count of every table in the four databases); `pg_switch_wal()` and wait until `pg_stat_archiver` shows that segment archived; assert the immediate scheduled backup completed; `helm uninstall database`, wait until the cluster pods and PVCs are gone; `helm install database` in recovery mode with `backup.serverName=coldframe-db-restored`; wait Ready; assert markers A and B and an identical fingerprint; assert `ContinuousArchiving` True; scale apps back, Server `/.well-known/healthz` Healthy and the realm served.
- `docs/operations/restore.md` -- runbook per Always, with the commands the smoke uses.
- `deploy/SECRETS.md`, `deploy/charts/README.md`, `deploy/README.md` -- per Code Map.
- `.github/workflows/ci.yml`, `images-verify.yml` -- comments and any timeout the longer smoke needs.

**Acceptance Criteria:**
- Given CNPG 1.30.1 and the plugin installed, when the `database` chart is applied, then one CNPG cluster runs PostgreSQL 18 with separate roles and databases for the Server, Temporal and Keycloak, and the other charts become ready against it.
- Given `coldframe-backup-s3` and an S3 target, when the cluster runs, then WAL is archived continuously and the ScheduledBackup takes base backups to that target.
- Given `deploy/charts/test.sh`, when it runs, then the `database` chart passes lint, unit tests, strict kubeconform against the pinned CRDs and the Secret contract check.
- Given the k3d smoke with RustFS, when the backup-and-restore phase runs, then a marker written before the backup (and one written after it, through WAL) exist in the restored cluster and the Server is healthy on it.
- Given `docs/operations/restore.md`, when read, then it states the recovery point and requires advancing every Device's replay window by a safety margin.

## Spec Change Log

- 2026-09-28, dev: **Secret `coldframe-db-server` renamed to `coldframe-db-coldframe`.** CloudNativePG creates its own server TLS Secret `<cluster>-server`, i.e. `coldframe-db-server` for the fixed cluster name `coldframe-db`; with the basic-auth Secret of that name present the operator loops on "generating server TLS certificate: missing tls.key secret data" and never creates an instance (seen on the first kind smoke). Cluster name (`coldframe-db-rw`) and the Secret name cannot both stay. The Secret now follows `coldframe-db-<role>` like `-temporal`/`-keycloak`; changed in SECRETS.md, the `server` chart helper and tests, fixtures, smoke and docs (the "beyond what the database move needs" allowance). `test.sh` now fails when SECRETS.md lists any CNPG-generated name (`coldframe-db-{ca,server,replication,app,superuser}`). Operator impact: the home server has not been installed yet (2.2 awaiting operator), so no live Secret to migrate.

## Review Triage Log

### 2026-09-28 — Review pass
- verdicts: 31 findings — high 0, medium 8, low 19, false 3, maybe-false 1
- findings:
  - `[medium]` `[patch]` Blind: recovery values kept in the release set up a stale or blocked next restore; the guard only compares serverName with sourceServerName — restore.md gained "After a restore: the next restore" (current folder as source, a never-used dated folder as target, guard limits stated).
  - `[low]` `[patch]` Blind: no step to archive the last WAL when the old primary still runs — optional restore.md step 2 (pg_switch_wal, wait for pg_stat_archiver/ContinuousArchiving; skipped for PITR).
  - `[low]` `[patch]` Blind: the five-minute recovery point relies on CNPG's default archive_timeout — the chart now sets `archive_timeout` from `backup.archiveTimeout` (default 5min), unit-tested, referenced by restore.md.
  - `[medium]` `[defer]` Blind: nothing alerts when archiving or backups fail — no monitoring stack exists in Epic 2; deferred.
  - `[low]` `[reject]` Blind: backups not encrypted — at-rest encryption is a bucket setting of the adopter's provider; a new value adds surface for an unlikely need.
  - `[maybe-false]` `[defer]` Blind: no region / endpointCA values; some providers need a region — would be settled by restoring against Backblaze/Wasabi/AWS non-us-east-1 with only an endpoint URL; deferred (medium, unverified).
  - `[low]` `[reject]` Blind: a restore can only read from the bucket it writes to — cross-bucket restore is a new feature; key rotation in the same bucket covers compromised credentials.
  - `[low]` `[patch]` Blind: the runbook ignores NATS, Temporal side effects and lost Keycloak sessions — NATS now stopped/started in steps 1 and 6, one sentence on lost accounts/sessions and re-run Temporal work.
  - `[low]` `[patch]` Blind: runbook hard-codes pod coldframe-db-1 — same root as the verification-gap runbook row; primary found by label.
  - `[low]` `[patch]` Blind: the smoke checks the immediate scheduled backup once without waiting — bounded wait for the ScheduledBackup-owned Backup before creating smoke-backup (smoke passed).
  - `[low]` `[reject]` Blind: PITR not tested end to end — the chart only passes targetTime through to CNPG's recoveryTarget, which the unit test pins; a second restore cycle in the smoke adds minutes for CNPG's own behaviour.
  - `[low]` `[patch]` Blind: recovery.sourceServerName defaults to the literal coldframe-db instead of clusterName — now "" = clusterName via a helper, unit-tested.
  - `[medium]` `[patch]` Blind: README install block lacks the plugin-apply retry and the cainjector wait; fixed /tmp file — waits for all three cert-manager deployments, retries like smoke.sh, uses mktemp.
  - `[low]` `[patch]` Blind: the old archive folder is never cleaned up — restore.md says it leaves retention and may be deleted after the restored cluster's first backup.
  - `[low]` `[reject]` Blind: CI charts job downloads CRDs uncached; noisy output on failure — kubeconform already downloads schemas; the download failure is reported first; caching adds workflow steps for a rare failure.
  - `[low]` `[patch]` Edge: empty recovery.sourceServerName renders serverName null — same root as the sourceServerName default row; empty now means clusterName.
  - `[medium]` `[patch]` Edge: a second restore can reuse a non-empty folder as serverName — same root as the first Blind row.
  - `[low]` `[reject]` Edge: check-manifests misses endpointCA, azure/google credentials, superuserSecret — no template renders them; adding branches guards state not shown to occur.
  - `[low]` `[reject]` Edge: crd-schemas rejects null for nullable int-or-string — no rendered manifest sets such a field to null.
  - `[low]` `[patch]` Edge: role names, owners, imageName, storageClass unquoted — now quoted, unit-tested with 123/yes/007/true.
  - `[medium]` `[patch]` Edge: runbook `kubectl wait --for=delete -l` errors when nothing matches — same root as the verification-gap runbook row; replaced by the smoke's polling.
  - `[low]` `[patch]` Edge: runbook reads pg_stat_archiver from coldframe-db-1, possibly a replica — same root as the verification-gap runbook row.
  - `[low]` `[patch]` Edge: a stale manual Backup satisfies the runbook's verification — verification now requires a Backup of the ScheduledBackup created after the restore.
  - `[medium]` `[patch]` Edge: README plugin install fails while the cert-manager webhook lags — same root as the README install row.
  - `[low]` `[reject]` Edge: RustFS and aws-cli images pinned by tag, not digest — test-only images, same tag pinning as every third-party image in the charts; the sha256 claim covers the manifests.
  - `[medium]` `[patch]` Verification gap: the runbook's uninstall-wait and verify commands are not what the smoke runs — runbook now polls like the smoke and finds the primary by label; the "same steps" claim now names the parts the smoke skips.
  - `[medium]` `[defer]` Verification gap: coldframe-db-* rotation via managed roles and cnpg.io/reload is documented but never exercised — deferred; a smoke rotation step is follow-up work.
  - `[false]` `[reject]` Intent: the Sites/Lots/Memberships check is not automated — the runbook's domain check is the operator's; finalization lists it under operator_actions.
  - `[false]` `[reject]` Intent: no real off-node bucket is exercised — an operator action, listed at finalization; RustFS is the epic's CI stand-in.
  - `[false]` `[reject]` Intent: spec left at in-review with no operator_actions — finalization writes both after review.
  - `[low]` `[reject]` Intent: renaming coldframe-db-server to coldframe-db-coldframe changes Story 2.2's contract — forced by the CNPG TLS Secret name clash, logged in the Spec Change Log, no live install to migrate.

## Design Notes

- Why the plugin: CNPG deprecated in-tree Barman Cloud support in 1.26; the plugin (which needs cert-manager for its gRPC TLS) is the supported path, and cert-manager is pinned for the epic anyway.
- The restored cluster must not archive into the source's folder (barman refuses a non-empty archive, and sharing it would corrupt the source's timeline); hence the new `serverName` and the render-time guard.
- Plugin wiring sketch:
  ```yaml
  plugins:
    - name: barman-cloud.cloudnative-pg.io
      isWALArchiver: true
      parameters: { barmanObjectName: coldframe-db-backup, serverName: coldframe-db }
  ```
- Operator actions to record at finalization: provide an off-node S3 bucket and create `coldframe-backup-s3`; install cert-manager, CNPG and the plugin on the home server; run the restore runbook once on the home server and confirm Sites, Lots and Memberships; watch the first CI run of the k3d path.

## Verification

**Commands:**
- `deploy/charts/test.sh` -- expected: all pass
- `SMOKE_CLUSTER=kind deploy/charts/smoke.sh coldframe/server:dev coldframe/web:dev coldframe/migrations:dev coldframe/keycloak:dev` -- expected: exit 0 including the restore phase
- `bash -n`, shellcheck, actionlint -- expected: clean

## Auto Run Result

Status: awaiting-operator

**Summary:** A new `database` chart runs the CloudNativePG 1.30.1 Cluster `coldframe-db` on PostgreSQL 18.6, with managed login roles `coldframe`, `temporal` and `keycloak` and the databases `coldframe`, `temporal`, `temporal_visibility` and `keycloak`. It archives WAL continuously (`archive_timeout` 5 min) and takes a daily base backup (plus one immediately) to an S3 target through the Barman Cloud plugin v0.15.0, with credentials from `coldframe-backup-s3`. Its recovery mode bootstraps a fresh cluster from those backups into a new archive folder and refuses to render into the source folder. The chart smoke install now runs on this chart with RustFS as S3 and proves a backup-and-restore round trip. `docs/operations/restore.md` is the runbook. The Server's database Secret is renamed `coldframe-db-coldframe` (CNPG owns `coldframe-db-server`; see Spec Change Log).

**Files changed:**
- `deploy/charts/database/`: chart, values, CI values, templates and 23 helm-unittest tests.
- `deploy/charts/dependencies.env`: pinned cert-manager, CNPG and plugin manifests (sha256) and the smoke's RustFS and aws-cli images (`.gitignore` exception).
- `deploy/charts/crd-schemas.py`: strict kubeconform schemas from the pinned CRDs.
- `deploy/charts/test.sh`, `check-manifests.py`, `testdata/*`: CRD schema validation, recovery-mode render, new Secret-reference shapes, fixtures proving the checks fail, CNPG name-clash check; `testdata/postgres.yaml` removed; `testdata/rustfs.yaml` added.
- `deploy/charts/smoke.sh`: operators, RustFS, database chart, backup/restore phase.
- `deploy/charts/server/`: Secret rename in helper, values and tests.
- `docs/operations/restore.md`: the restore runbook.
- `deploy/SECRETS.md`, `deploy/charts/README.md`, `deploy/README.md`, `.github/workflows/ci.yml`, `images-verify.yml` (timeout 110 min): docs and comments.

**Review findings:** 31 findings — 17 patched (6 medium, 11 low; grouped rows counted individually), 3 deferred (monitoring of archiving, S3 region/endpointCA, password rotation test), 11 rejected: encryption value, cross-bucket restore, end-to-end PITR, CRD download caching, extra check-manifests shapes, nullable int-or-string, image digests, the Secret rename, and three false intent items (operator work handled at finalization). Reasons are in the triage log.

**Follow-up review recommended:** true. Patched 0 high and 6 medium (in 3 entries). The specific unverified risk: the rewritten runbook commands (polling uninstall wait, primary lookup by label, optional WAL switch, backup verification by label) are shell-checked but have not been run verbatim against a cluster; the smoke runs its own equivalents.

**Verification performed (local, rootless Podman):**
- `deploy/charts/test.sh`: 62 passed, 0 failed (after patches).
- shellcheck and actionlint clean; `bash -n` on every script.
- `SMOKE_CLUSTER=kind deploy/charts/smoke.sh ...:dev`: exit 0 after the patches — cluster Ready and archiving with the roles and databases, every chart ready, Server Healthy, realm served, hooks and migration gating as before, immediate scheduled backup completed, markers A (base backup) and B (WAL) restored, identical row counts in all four databases, restored cluster archiving to `coldframe-db-restored`, apps healthy on it.

**Residual risks:**
- The k3d path and the new CI steps have not run yet (k3s cannot start on this host); the first CI run settles it.
- `coldframe-db-*` rotation through managed roles is documented from CNPG docs but untested (deferred).
- `test.sh` now needs network access for the CRD manifests.
- Region-dependent S3 providers may need a region setting the contract does not offer yet (deferred).

**Operator actions owed:** see `operator_actions` in the frontmatter.
