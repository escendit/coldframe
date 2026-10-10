# Helm charts

Seven self-authored charts install the Coldframe stack on Kubernetes (Stories 2.2 to 2.4, AD-13,
AD-15, AD-17, AD-22).
They have no upstream chart dependencies, never render a Secret, and read every credential from
the fixed Secrets listed in [`../SECRETS.md`](../SECRETS.md).

| Chart | What | Image | Chart `version` / `appVersion` |
| --- | --- | --- | --- |
| [`database`](database) | The CloudNativePG `Cluster` `coldframe-db` (PostgreSQL 18): roles and databases of the Server, Temporal and Keycloak; WAL archiving and scheduled base backups to S3 through the Barman Cloud plugin; a recovery mode | `ghcr.io/cloudnative-pg/postgresql:18.6-standard-trixie` | release / `18.6` |
| [`nats`](nats) | NATS Server with JetStream, one replica, store on an `emptyDir` (disposable, AD-5) | `docker.io/library/nats:2.15.0` | release / `2.15.0` |
| [`temporal`](temporal) | Temporal Server, every service in one Deployment on PostgreSQL; schema and namespace hook Jobs | `docker.io/temporalio/server:1.31.2`, `admin-tools:1.31.2` | release / `1.31.2` |
| [`keycloak`](keycloak) | Keycloak (Phase Two + `keycloak-temporal-extensions`), `start --optimized` | `ghcr.io/escendit/coldframe/keycloak` | release / release |
| [`server`](server) | The Server (Orleans silo, Edge API, SignalR), its migration hook Job and the daily partition CronJob | `ghcr.io/escendit/coldframe/server`, `.../migrations` | release / release |
| [`web`](web) | The web backend-for-frontend | `ghcr.io/escendit/coldframe/web` | release / release |
| [`ingress`](ingress) | HTTPS for the three hosts: a cert-manager `Issuer` for Let's Encrypt with a Cloudflare DNS-01 solver, the `Certificate` `coldframe-tls`, and a Traefik `Ingress` on `websecure` (443) only | none | release / `1.21.2` (cert-manager) |

Temporal 1.31.3, the epic pin, is not published as an image; the chart runs 1.31.2, the version
the Aspire stack runs.

The `database` chart has no Deployment of its own: CloudNativePG runs the instances, and the
chart's `appVersion` is the PostgreSQL version of `imageName`. The `ingress` chart has none either:
cert-manager and RKE2's Traefik do the work, and its `appVersion` is the cert-manager version it
targets.

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
# DNS-01 behind split DNS: check the challenge on public resolvers (docs/operations/split-dns.md).
# Adds each flag only when it is missing: safe to run again, e.g. after a cert-manager upgrade.
for flag in --dns01-recursive-nameservers-only --dns01-recursive-nameservers=1.1.1.1:53,9.9.9.9:53; do
  kubectl -n cert-manager get deployment cert-manager \
    -o jsonpath='{.spec.template.spec.containers[0].args}' | grep -F -- "\"${flag}\"" >/dev/null \
    || kubectl -n cert-manager patch deployment cert-manager --type=json \
      -p "[{\"op\": \"add\", \"path\": \"/spec/template/spec/containers/0/args/-\", \"value\": \"${flag}\"}]"
done
kubectl -n cert-manager rollout status deployment/cert-manager
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

On RKE2, Traefik without port 80: apply the `HelmChartConfig`
[`../rke2/rke2-traefik-config.yaml`](../rke2/rke2-traefik-config.yaml) and check that the
`rke2-traefik` Service lists 443 only ([`../rke2/README.md`](../rke2/README.md)).

In the namespace of the releases (the defaults assume one namespace):

1. Every Secret of [`../SECRETS.md`](../SECRETS.md) consumed by the charts, including the
   database Secrets (each `username` equal to its role name) and `coldframe-backup-s3`.
2. An S3 bucket off the node for the backups, and its endpoint.
3. For Keycloak, a ConfigMap holding the `coldframe` realm file, with the redirect URIs of your
   host names (the realm in `aspire/keycloak/realms` is for localhost and is not packaged).
4. A domain with its DNS zone at Cloudflare, the Secret `coldframe-dns01` holding an API token for
   that zone, and split DNS for the three hosts
   ([`docs/operations/split-dns.md`](../../docs/operations/split-dns.md)).

### Releases

Install the database first and wait until the cluster is ready; it provides `coldframe-db-rw:5432`
with the roles and databases `coldframe`, `temporal`, `temporal_visibility` and `keycloak`. Then
install the rest in dependency order; the release names are the defaults every chart assumes for
the others (`nats:4222`, `temporal:7233`, `keycloak:8080`, `server:8080`, `web:3000`). With the
domain `coldframe.example.org`, the hosts are `coldframe.example.org` (web),
`api.coldframe.example.org` (Server) and `auth.coldframe.example.org` (Keycloak). Prefer a
dedicated subdomain over the apex of your zone: the LAN record for the web host would hide any
public site at the apex:

```sh
NS=coldframe
helm install database deploy/charts/database -n "$NS" \
  --set backup.endpointURL=https://s3.example.org \
  --set backup.destinationPath=s3://my-bucket/coldframe/
kubectl -n "$NS" wait clusters.postgresql.cnpg.io/coldframe-db --for=condition=Ready --timeout=10m
helm install nats     deploy/charts/nats     -n "$NS" --wait
helm install temporal deploy/charts/temporal -n "$NS" --wait
helm install keycloak deploy/charts/keycloak -n "$NS" --wait \
  --set hostname=auth.coldframe.example.org --set realmImport.configMap=coldframe-realm
helm install server   deploy/charts/server   -n "$NS" --wait \
  --set identity.authority=https://auth.coldframe.example.org/realms/coldframe \
  --set keycloak.baseUrl=http://keycloak:8080
helm install web      deploy/charts/web      -n "$NS" --wait \
  --set keycloak.issuer=https://auth.coldframe.example.org/realms/coldframe \
  --set origin=https://coldframe.example.org
helm install ingress  deploy/charts/ingress  -n "$NS" --wait \
  --set domain=coldframe.example.org --set acme.email=you@example.org
kubectl -n "$NS" wait certificate/coldframe-tls --for=condition=Ready --timeout=10m
```

The Server and the web app reach Keycloak at `https://auth.coldframe.example.org`, so the node and the
cluster must resolve it to the LAN ingress address (split DNS); until the certificate is Ready,
their token validation fails. Verification, renewals and the phone check:
[`docs/operations/split-dns.md`](../../docs/operations/split-dns.md).

Restoring the database from its backups: [`docs/operations/restore.md`](../../docs/operations/restore.md).

Every Service is `ClusterIP`; only Traefik's websecure entrypoint (hostPort 443) is exposed, and
nothing serves port 80.

The reference install is Fleet (GitOps): each chart folder holds its `fleet.yaml` (release name,
namespace `coldframe`, `dependsOn`, and the site values from the ConfigMap
`coldframe-values`), and the operators are Fleet bundles too
([`docs/operations/install.md`](../../docs/operations/install.md)). The commands above are the
manual equivalent.

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
| server | `partitions.schedule`, `partitions.monthsAhead`, `partitions.backoffLimit`, `partitions.activeDeadlineSeconds` | `17 3 * * *` (UTC), `3`, `1`, `600` | the CronJob runs the `migrations` image with `partitions`; the chart fails below 2 months ahead (AD-22) |
| server | `stopped` | `false` | `true` renders 0 replicas: only while a restore under Fleet advances the replay windows ([restore.md](../../docs/operations/restore.md), step 5) |
| server | `nats.url`, `keycloak.*`, `keycloakEvents.*` | the other releases | |
| server | `push.apns.enabled`, `push.apns.topic`, `push.fcm.enabled` | `false`, `com.escendit.coldframe`, `false` | push notifications are optional: an enabled provider reads its keys of the Secret `coldframe-push`, which are then required ([SECRETS.md](../SECRETS.md#push-notifications)) |
| web | `serverUrl`, `keycloak.issuer`, `origin`, `sessionCookieSecure` | the other releases, empty, `true` | |
| keycloak | `hostname` | empty: `KC_HOSTNAME_STRICT=false` | set the public host name in production |
| keycloak | `realmImport.configMap` | empty: no import | mounted at `/opt/keycloak/data/import`, adds `--import-realm` |
| temporal | `namespace.name`, `namespace.retention`, `numHistoryShards` | `coldframe`, `72h`, `4` | never change the shard count after the first install |
| nats | `jetstream.storageSizeLimit` | `1Gi` | size limit of the `emptyDir` |
| ingress | `domain` | `coldframe.example.invalid` | placeholder that only renders: set your domain |
| ingress | `hosts.web`, `hosts.api`, `hosts.auth` | empty: `<domain>`, `api.<domain>`, `auth.<domain>` | the Certificate's `dnsNames` and the Ingress rules |
| ingress | `backends.web`, `backends.api`, `backends.auth` (`service`, `port`) | `web:3000`, `server:8080`, `keycloak:8080` | |
| ingress | `ingressClassName` | `traefik` | RKE2's Traefik |
| ingress | `acme.enabled`, `acme.server`, `acme.email` | `true`, Let's Encrypt production, empty | staging: `https://acme-staging-v02.api.letsencrypt.org/directory` |
| ingress | `externalIssuer.name`, `externalIssuer.kind` | empty, `Issuer` | used only when `acme.enabled` is false (unsupported private-CA fallback); the chart fails when both are off |

There is no value for a Secret name (the `ingress` chart's `coldframe-dns01`, and the
cert-manager-generated `coldframe-tls` and `coldframe-letsencrypt-account`, included): the names
are fixed in the templates so that `SECRETS.md` stays the whole contract.

## Upgrades

- **Server**: one replica, `strategy: Recreate`. The migration Job is a `pre-install,pre-upgrade`
  hook running the `migrations` image of the same version. Helm waits for it before it touches the
  Deployment: when the migration fails, the release fails and the running Server stays as it is.
  Then the old pod stops (the host gets `shutdownTimeoutSeconds` to drain the silo, Kubernetes
  waits `terminationGracePeriodSeconds`) and the new one starts. The previous Job is deleted only
  when the next one is created, so its logs stay available. The Job also creates the monthly
  partitions of the Readings and device-report tables; the CronJob `server-partitions` runs the
  same image with `partitions` once a day, so they stay at least two months ahead between
  upgrades (AD-22). A failed run changes nothing; the next one catches up.
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
deploy/charts/test.sh           # helm lint, helm-unittest, kubeconform, Secret contract, TLS/port 80
deploy/charts/check-version.sh 0.1.0
deploy/charts/smoke.sh coldframe/server:dev coldframe/web:dev coldframe/migrations:dev coldframe/keycloak:dev
```

| Script | Purpose | Needs |
| --- | --- | --- |
| `test.sh` | renders the Fleet bundles of `deploy/fleet/gitrepo.yaml` with the fleet CLI and checks them with `deploy/fleet/check-fleet.py` (which must fail on each fixture in `deploy/fleet/testdata/`), and renders each chart with its `fleet.yaml` values and its key of `deploy/fleet/values.example.yaml`; per chart: `helm lint --strict`, `helm unittest` (`<chart>/tests`), `helm template` with default and `ci-values.yaml` values (and the database in recovery mode) validated by kubeconform (strict, Kubernetes 1.36.0, and the CloudNativePG, Barman Cloud and cert-manager kinds against schemas generated from the CRDs pinned in `dependencies.env`), `check-manifests.py` and `check-ingress.py`; renders the pinned rke2-traefik chart, which must fail `check-ingress.py` with its default values (hostPort 80) and pass with `deploy/rke2/rke2-traefik-config.yaml`; also proves the checks fail on the fixtures in `testdata/` | helm v4.2.2, helm-unittest v1.1.2, kubeconform v0.8.0, the fleet CLI v0.16.2 (`dependencies.env`), curl, python3 with PyYAML; network for the CRD manifests, the rke2-traefik chart (`KUBECONFORM_CACHE` caches them) and the operator charts |
| `check-manifests.py <SECRETS.md> <label>` | reads rendered manifests on stdin; fails on a rendered Secret or a Secret name or key missing from `SECRETS.md` (including CNPG `passwordSecret` and ObjectStore `s3Credentials`) | python3, PyYAML |
| `check-ingress.py <label>` | reads rendered manifests on stdin; fails on an Ingress without `tls` for each host or without the Traefik annotations for `websecure` only, a Traefik `IngressRoute` without `tls` or on another entrypoint, an ACME `Issuer`/`ClusterIssuer` with a solver that is not DNS-01 (or is HTTP-01), a Service port or nodePort 80, a container port or hostPort 80, or a `--entryPoints.web.*` argument | python3, PyYAML |
| `crd-schemas.py <dir>` | reads CRD manifests on stdin and writes strict kubeconform schemas `<kind>_<version>.json` (unknown fields fail) | python3, PyYAML |
| `dependencies.env` | the pinned cert-manager, CloudNativePG and Barman Cloud plugin manifests and their Helm charts (the Fleet bundles' repo and version), the rke2-traefik and rke2-traefik-crd charts of RKE2 v1.36.4+rke2r1, Fleet (the CLI per architecture, the fleet-crd and fleet charts) (version, URL, sha256), and the smoke's RustFS and aws-cli images; sourced by `test.sh`, `smoke.sh`, CI and the install commands above | |
| `check-version.sh <version>` | fails naming each `Chart.yaml` whose `version` (all charts) or `appVersion` (server, web, keycloak) differs | bash |
| `smoke.sh <server> <web> <migrations> <keycloak>` | a disposable k3d cluster (`rancher/k3s:v1.36.4-k3s1`) with cert-manager, CloudNativePG and the Barman Cloud plugin, placeholder Secrets and RustFS as the S3 target (`testdata/rustfs.yaml`): installs RKE2's Traefik (pinned rke2-traefik with the `HelmChartConfig` values), the database and every chart, asserts every pod Ready (or a completed Job), the Server's `/.well-known/healthz` Healthy and the imported realm served; installs the `ingress` chart with a test CA (`testdata/smoke-ca.yaml`) in place of Let's Encrypt and checks the Certificate Ready with a `renewalTime`, the Server, Keycloak (issuer `https://auth.<domain>:<port>/…`) and the web app over HTTPS through Traefik's 443 with that CA, and no port 80 or `web` entrypoint on Traefik; checks that a failing migration fails `helm upgrade server` without touching the Deployment and that a good upgrade reruns the migration Job, then runs a backup-and-restore round trip (a marker before and one after a base backup, uninstall, recovery into a new folder, both markers and every table's row count back, `advance-replay` as a one-off Job from the partition CronJob before the apps start, the Server healthy on the restored cluster) | k3d v5.9.0, helm, kubectl, jq, curl, docker |

`ci-values.yaml` holds the CI overrides: the plain-HTTP in-cluster Keycloak, RustFS
(`http://rustfs:9000`) as the database's S3 target, and for `ingress` the domain
`coldframe.smoke.test` with the smoke's CA Issuer `smoke-ca` instead of ACME. `SMOKE_CLUSTER=kind`
runs the smoke on kind (`kindest/node:v1.36.4`) instead, for hosts where k3s cannot start, such as
rootless Podman without the `cpuset` cgroup controller delegated (`KIND_EXPERIMENTAL_PROVIDER=podman`).

CI runs `test.sh` as the `Charts` job and `smoke.sh` in the `Images` job. The GitOps counterpart
of `smoke.sh` is [`deploy/fleet/smoke.sh`](../fleet/smoke.sh), also in the `Images` job: Fleet
installs the bundles in dependency order on a disposable cluster, then upgrades and restarts them.
