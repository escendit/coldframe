# Helm charts

Six self-authored charts install the Coldframe stack on Kubernetes (Stories 2.2 and 2.3, AD-15,
AD-17, AD-22).
They have no upstream chart dependencies, never render a Secret, and read every credential from
the fixed Secrets listed in [`../SECRETS.md`](../SECRETS.md).

| Chart | What | Image | Chart `version` / `appVersion` |
| --- | --- | --- | --- |
| [`database`](database) | The CloudNativePG `Cluster` `coldframe-db` (PostgreSQL 18): roles and databases of the Server, Temporal and Keycloak; WAL archiving and scheduled base backups to S3 through the Barman Cloud plugin; a recovery mode | `ghcr.io/cloudnative-pg/postgresql:18.6-standard-trixie` | release / `18.6` |
| [`nats`](nats) | NATS Server with JetStream, one replica, store on an `emptyDir` (disposable, AD-5) | `docker.io/library/nats:2.15.0` | release / `2.15.0` |
| [`temporal`](temporal) | Temporal Server, every service in one Deployment on PostgreSQL; schema and namespace hook Jobs | `docker.io/temporalio/server:1.31.2`, `admin-tools:1.31.2` | release / `1.31.2` |
| [`keycloak`](keycloak) | Keycloak (Phase Two + `keycloak-temporal-extensions`), `start --optimized` | `ghcr.io/escendit/coldframe/keycloak` | release / release |
| [`server`](server) | The Server (Orleans silo, Edge API, SignalR) and its migration hook Job | `ghcr.io/escendit/coldframe/server`, `.../migrations` | release / release |
| [`web`](web) | The web backend-for-frontend | `ghcr.io/escendit/coldframe/web` | release / release |

Temporal 1.31.3, the epic pin, is not published as an image; the chart runs 1.31.2, the version
the Aspire stack runs.

The `database` chart has no Deployment of its own: CloudNativePG runs the instances, and the
chart's `appVersion` is the PostgreSQL version of `imageName`.

The Coldframe charts' image tag defaults to the chart's `appVersion`, which equals the release
version: release tag `vX.Y.Z` = image tag `X.Y.Z` = chart `version` and `appVersion`. The release
workflow refuses a tag that differs from the charts (`check-version.sh`); bump every `Chart.yaml`
before tagging. A pre-release tag `vX.Y.Z-rc.N` needs every chart's `version`, and the `appVersion`
of server, web and keycloak, set to `X.Y.Z-rc.N` first.

## Install

### Prerequisites

Once per cluster, the operators pinned in [`dependencies.env`](dependencies.env): cert-manager
(the Barman Cloud plugin serves gRPC over TLS with cert-manager certificates), CloudNativePG and
the Barman Cloud plugin, in that order. Every manifest is checked against its pinned sha256:

```sh
source deploy/charts/dependencies.env
manifest=$(mktemp)
install_manifest() { # <url> <sha256> [kubectl apply arguments...]
  curl -fsSL -o "$manifest" "$1" && echo "$2  $manifest" | sha256sum --check --strict \
    && kubectl apply -f "$manifest" "${@:3}"
}
install_manifest "$CERT_MANAGER_URL" "$CERT_MANAGER_SHA256"
for d in cert-manager cert-manager-cainjector cert-manager-webhook; do
  kubectl -n cert-manager rollout status deployment/$d
done
install_manifest "$CNPG_URL" "$CNPG_SHA256" --server-side
kubectl -n cnpg-system rollout status deployment/cnpg-controller-manager
# The plugin's Certificates go through the cert-manager webhook, which can lag its rollout: retry.
for attempt in $(seq 1 30); do
  install_manifest "$BARMAN_CLOUD_PLUGIN_URL" "$BARMAN_CLOUD_PLUGIN_SHA256" && break
  sleep 5
done
kubectl -n cnpg-system rollout status deployment/barman-cloud
rm -f "$manifest"
```

In the namespace of the releases (the defaults assume one namespace):

1. Every Secret of [`../SECRETS.md`](../SECRETS.md) consumed by the charts, including the
   database Secrets (each `username` equal to its role name) and `coldframe-backup-s3`.
2. An S3 bucket off the node for the backups, and its endpoint.
3. For Keycloak, a ConfigMap holding the `coldframe` realm file, with the redirect URIs of your
   host names (the realm in `aspire/keycloak/realms` is for localhost and is not packaged).

### Releases

Install the database first and wait until the cluster is ready; it provides `coldframe-db-rw:5432`
with the roles and databases `coldframe`, `temporal`, `temporal_visibility` and `keycloak`. Then
install the rest in dependency order; the release names are the defaults every chart assumes for
the others (`nats:4222`, `temporal:7233`, `keycloak:8080`, `server:8080`):

```sh
NS=coldframe
helm install database deploy/charts/database -n "$NS" \
  --set backup.endpointURL=https://s3.example.org \
  --set backup.destinationPath=s3://my-bucket/coldframe/
kubectl -n "$NS" wait cluster/coldframe-db --for=condition=Ready --timeout=10m
helm install nats     deploy/charts/nats     -n "$NS" --wait
helm install temporal deploy/charts/temporal -n "$NS" --wait
helm install keycloak deploy/charts/keycloak -n "$NS" --wait \
  --set hostname=auth.example.org --set realmImport.configMap=coldframe-realm
helm install server   deploy/charts/server   -n "$NS" --wait \
  --set identity.authority=https://auth.example.org/realms/coldframe \
  --set keycloak.baseUrl=http://keycloak:8080
helm install web      deploy/charts/web      -n "$NS" --wait \
  --set keycloak.issuer=https://auth.example.org/realms/coldframe \
  --set origin=https://coldframe.example.org
```

Restoring the database from its backups: [`docs/operations/restore.md`](../../docs/operations/restore.md).

Every Service is `ClusterIP`. Ingress, TLS and the Fleet bundles arrive with Stories 2.4 and 2.5.

## Values that matter

| Chart | Value | Default | Notes |
| --- | --- | --- | --- |
| all but database | `image.repository`, `image.tag`, `image.pullPolicy` | see the table above | `tag` empty = `appVersion` |
| all but database | `fullnameOverride` | release name | resource names |
| database | `clusterName` | `coldframe-db` | the CNPG Cluster; its Service `<clusterName>-rw` is the other charts' `database.host` |
| database | `instances`, `imageName`, `storage.size`, `storage.storageClass`, `resources` | `1`, PostgreSQL 18.6, `10Gi`, cluster default, none | |
| database | `roles.server`, `roles.temporal`, `roles.keycloak` | `coldframe`, `temporal`, `keycloak` | must equal the `username` of the matching Secret |
| database | `backup.endpointURL`, `backup.destinationPath` | `https://s3.example.invalid`, `s3://coldframe-backups/` | placeholders that only render: set your bucket |
| database | `backup.serverName` | `clusterName` | the archive folder below `destinationPath` |
| database | `backup.retentionPolicy`, `backup.schedule` | `30d`, `0 0 3 * * *` | the schedule is a 6-field cron, seconds first |
| database | `backup.archiveTimeout` | `5min` | PostgreSQL `archive_timeout`: bounds the data a restore loses |
| database | `recovery.enabled`, `recovery.sourceServerName`, `recovery.targetTime` | `false`, empty (= `clusterName`), empty | bootstrap from the backups of `sourceServerName`; the chart fails when it equals `backup.serverName` |
| server, temporal, keycloak | `database.host`, `database.port`, `database.name` | `coldframe-db-rw`, `5432`, per chart | temporal also has `database.visibilityName` |
| server | `identity.authority`, `identity.requireHttpsMetadata` | in-cluster Keycloak, `true` | an `https` authority in production |
| server | `shutdownTimeoutSeconds`, `terminationGracePeriodSeconds` | `45`, `60` | the chart fails unless the grace period is longer |
| server | `migrations.image.*`, `migrations.backoffLimit`, `migrations.activeDeadlineSeconds` | `migrations` image at `appVersion`, `1`, `600` | |
| server | `nats.url`, `keycloak.*`, `keycloakEvents.*` | the other releases | |
| web | `serverUrl`, `keycloak.issuer`, `origin`, `sessionCookieSecure` | the other releases, empty, `true` | |
| keycloak | `hostname` | empty: `KC_HOSTNAME_STRICT=false` | set the public host name in production |
| keycloak | `realmImport.configMap` | empty: no import | mounted at `/opt/keycloak/data/import`, adds `--import-realm` |
| temporal | `namespace.name`, `namespace.retention`, `numHistoryShards` | `coldframe`, `72h`, `4` | never change the shard count after the first install |
| nats | `jetstream.storageSizeLimit` | `1Gi` | size limit of the `emptyDir` |

There is no value for a Secret name: the names are fixed in the templates so that `SECRETS.md`
stays the whole contract.

## Upgrades

- **Server**: one replica, `strategy: Recreate`. The migration Job is a `pre-install,pre-upgrade`
  hook running the `migrations` image of the same version. Helm waits for it before it touches the
  Deployment: when the migration fails, the release fails and the running Server stays as it is.
  Then the old pod stops (the host gets `shutdownTimeoutSeconds` to drain the silo, Kubernetes
  waits `terminationGracePeriodSeconds`) and the new one starts. The previous Job is deleted only
  when the next one is created, so its logs stay available.
- **Temporal**: a `pre-install,pre-upgrade` hook Job sets up and updates both schemas (idempotent);
  a `post-install,post-upgrade` hook Job creates the namespace when it is absent.
- **Keycloak**: one replica, `Recreate`; an existing realm is not overwritten by the import.
- **Database**: CloudNativePG applies a new `imageName` (PostgreSQL minor version) with a restart
  of the instance. Changing `clusterName` or the bootstrap values of a running release does not
  move the data: `bootstrap` only runs for a new cluster. **Uninstalling the release deletes the
  cluster and its volumes**; only the backups in the object store remain, and
  [`restore.md`](../../docs/operations/restore.md) brings them back.
- A missing Secret or key leaves the pod in `CreateContainerConfigError`, naming it; nothing falls
  back to a generated value.

## Checks

```sh
deploy/charts/test.sh           # helm lint, helm-unittest, kubeconform, Secret contract
deploy/charts/check-version.sh 0.1.0
deploy/charts/smoke.sh coldframe/server:dev coldframe/web:dev coldframe/migrations:dev coldframe/keycloak:dev
```

| Script | Purpose | Needs |
| --- | --- | --- |
| `test.sh` | per chart: `helm lint --strict`, `helm unittest` (`<chart>/tests`), `helm template` with default and `ci-values.yaml` values (and the database in recovery mode) validated by kubeconform (strict, Kubernetes 1.36.0, and the CloudNativePG and Barman Cloud kinds against schemas generated from the CRDs pinned in `dependencies.env`), and `check-manifests.py`; also proves the checks fail on the fixtures in `testdata/` | helm v4.2.2, helm-unittest v1.1.2, kubeconform v0.8.0, curl, python3 with PyYAML; network for the CRD manifests (`KUBECONFORM_CACHE` caches them) |
| `check-manifests.py <SECRETS.md> <label>` | reads rendered manifests on stdin; fails on a rendered Secret or a Secret name or key missing from `SECRETS.md` (including CNPG `passwordSecret` and ObjectStore `s3Credentials`) | python3, PyYAML |
| `crd-schemas.py <dir>` | reads CRD manifests on stdin and writes strict kubeconform schemas `<kind>_<version>.json` (unknown fields fail) | python3, PyYAML |
| `dependencies.env` | the pinned cert-manager, CloudNativePG and Barman Cloud plugin manifests (version, URL, sha256) and the smoke's RustFS and aws-cli images; sourced by `test.sh`, `smoke.sh` and the install commands above | |
| `check-version.sh <version>` | fails naming each `Chart.yaml` whose `version` (all charts) or `appVersion` (server, web, keycloak) differs | bash |
| `smoke.sh <server> <web> <migrations> <keycloak>` | a disposable k3d cluster (`rancher/k3s:v1.36.4-k3s1`) with cert-manager, CloudNativePG and the Barman Cloud plugin, placeholder Secrets and RustFS as the S3 target (`testdata/rustfs.yaml`): installs the database and every chart, asserts every pod Ready (or a completed Job), the Server's `/.well-known/healthz` Healthy and the imported realm served, checks that a failing migration fails `helm upgrade server` without touching the Deployment and that a good upgrade reruns the migration Job, then runs a backup-and-restore round trip (a marker before and one after a base backup, uninstall, recovery into a new folder, both markers and every table's row count back, the Server healthy on the restored cluster) | k3d v5.9.0, helm, kubectl, jq, curl, docker |

`ci-values.yaml` holds the CI overrides: the plain-HTTP in-cluster Keycloak, and RustFS
(`http://rustfs:9000`) as the database's S3 target. `SMOKE_CLUSTER=kind`
runs the smoke on kind (`kindest/node:v1.36.4`) instead, for hosts where k3s cannot start, such as
rootless Podman without the `cpuset` cgroup controller delegated (`KIND_EXPERIMENTAL_PROVIDER=podman`).

CI runs `test.sh` as the `Charts` job and `smoke.sh` in the `Images` job.
