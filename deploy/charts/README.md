# Helm charts

Five self-authored charts install the Coldframe stack on Kubernetes (Story 2.2, AD-15, AD-22).
They have no upstream chart dependencies, never render a Secret, and read every credential from
the fixed Secrets listed in [`../SECRETS.md`](../SECRETS.md).

| Chart | What | Image | Chart `version` / `appVersion` |
| --- | --- | --- | --- |
| [`nats`](nats) | NATS Server with JetStream, one replica, store on an `emptyDir` (disposable, AD-5) | `docker.io/library/nats:2.15.0` | release / `2.15.0` |
| [`temporal`](temporal) | Temporal Server, every service in one Deployment on PostgreSQL; schema and namespace hook Jobs | `docker.io/temporalio/server:1.31.2`, `admin-tools:1.31.2` | release / `1.31.2` |
| [`keycloak`](keycloak) | Keycloak (Phase Two + `keycloak-temporal-extensions`), `start --optimized` | `ghcr.io/escendit/coldframe/keycloak` | release / release |
| [`server`](server) | The Server (Orleans silo, Edge API, SignalR) and its migration hook Job | `ghcr.io/escendit/coldframe/server`, `.../migrations` | release / release |
| [`web`](web) | The web backend-for-frontend | `ghcr.io/escendit/coldframe/web` | release / release |

Temporal 1.31.3, the epic pin, is not published as an image; the chart runs 1.31.2, the version
the Aspire stack runs.

The Coldframe charts' image tag defaults to the chart's `appVersion`, which equals the release
version: release tag `vX.Y.Z` = image tag `X.Y.Z` = chart `version` and `appVersion`. The release
workflow refuses a tag that differs from the charts (`check-version.sh`); bump every `Chart.yaml`
before tagging. A pre-release tag `vX.Y.Z-rc.N` needs every chart's `version`, and the `appVersion`
of server, web and keycloak, set to `X.Y.Z-rc.N` first.

## Install

Prerequisites, in the namespace of the releases (the defaults assume one namespace):

1. The PostgreSQL cluster with the roles and databases `coldframe`, `temporal`,
   `temporal_visibility` and `keycloak`, reachable as `coldframe-db-rw:5432` (the CloudNativePG
   `-rw` Service of Story 2.3).
2. Every Secret of [`../SECRETS.md`](../SECRETS.md) consumed by the charts.
3. For Keycloak, a ConfigMap holding the `coldframe` realm file, with the redirect URIs of your
   host names (the realm in `aspire/keycloak/realms` is for localhost and is not packaged).

Then install in dependency order; the release names are the defaults every chart assumes for the
others (`nats:4222`, `temporal:7233`, `keycloak:8080`, `server:8080`):

```sh
NS=coldframe
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

Every Service is `ClusterIP`. Ingress, TLS and the Fleet bundles arrive with Stories 2.4 and 2.5.

## Values that matter

| Chart | Value | Default | Notes |
| --- | --- | --- | --- |
| all | `image.repository`, `image.tag`, `image.pullPolicy` | see the table above | `tag` empty = `appVersion` |
| all | `fullnameOverride` | release name | resource names |
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
| `test.sh` | per chart: `helm lint --strict`, `helm unittest` (`<chart>/tests`), `helm template` with default and `ci-values.yaml` values validated by kubeconform (strict, Kubernetes 1.36.0), and `check-manifests.py`; also proves the checks fail on the fixtures in `testdata/` | helm v4.2.2, helm-unittest v1.1.2, kubeconform v0.8.0, python3 with PyYAML |
| `check-manifests.py <SECRETS.md> <label>` | reads rendered manifests on stdin; fails on a rendered Secret or a Secret name or key missing from `SECRETS.md` | python3, PyYAML |
| `check-version.sh <version>` | fails naming each `Chart.yaml` whose `version` (all charts) or `appVersion` (server, web, keycloak) differs | bash |
| `smoke.sh <server> <web> <migrations> <keycloak>` | a disposable k3d cluster (`rancher/k3s:v1.36.4-k3s1`) with placeholder Secrets and a test-only PostgreSQL (`testdata/postgres.yaml`): installs every chart with `--wait`, asserts every pod Ready (or a completed Job), the Server's `/.well-known/healthz` Healthy and the imported realm served, then checks that a failing migration fails `helm upgrade server` without touching the Deployment and that a good upgrade reruns the migration Job | k3d v5.9.0, helm, kubectl, jq, docker |

`ci-values.yaml` holds the CI overrides for the plain-HTTP in-cluster Keycloak. `SMOKE_CLUSTER=kind`
runs the smoke on kind (`kindest/node:v1.36.4`) instead, for hosts where k3s cannot start, such as
rootless Podman without the `cpuset` cgroup controller delegated (`KIND_EXPERIMENTAL_PROVIDER=podman`).

CI runs `test.sh` as the `Charts` job and `smoke.sh` in the `Images` job.
