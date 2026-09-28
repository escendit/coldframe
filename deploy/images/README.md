# Container images

Every server-side component ships as a container image, one per release (Stories 2.1 and 2.2,
AD-23). The Helm charts in [`../charts`](../charts/README.md) deploy them.

| Component | Image | Dockerfile | Final base image | User (UID) | Ports |
| --- | --- | --- | --- | --- | --- |
| Server: Orleans silo, Edge API and SignalR | `ghcr.io/escendit/coldframe/server:X.Y.Z` | [`apps/cs/server/Dockerfile`](../../apps/cs/server/Dockerfile) | `mcr.microsoft.com/dotnet/aspnet:10.0.12` | `app` (1654) | 8080 HTTP, 11111 silo, 30000 gateway |
| Web backend-for-frontend | `ghcr.io/escendit/coldframe/web:X.Y.Z` | [`apps/ts/web/Dockerfile`](../../apps/ts/web/Dockerfile) | `node:24.21.0-trixie-slim` | `node` (1000) | 3000 HTTP |
| Migration job | `ghcr.io/escendit/coldframe/migrations:X.Y.Z` | [`apps/cs/migrations/Dockerfile`](../../apps/cs/migrations/Dockerfile) | `mcr.microsoft.com/dotnet/aspnet:10.0.12` | `app` (1654) | none |
| Keycloak: Phase Two 26.6.7 with `keycloak-temporal-extensions` v0.0.1-rc.2 | `ghcr.io/escendit/coldframe/keycloak:X.Y.Z` | [`aspire/keycloak/Dockerfile`](../../aspire/keycloak/Dockerfile) | `quay.io/phasetwo/phasetwo-keycloak:26.6.7` | `keycloak` (2000) | 8080 HTTP, 8443 HTTPS, 9000 management |

The migration job uses the ASP.NET Core runtime image, not the plain .NET one: the Escendit service
defaults it uses reference the `Microsoft.AspNetCore.App` shared framework.

No public image carries Phase Two Keycloak with `keycloak-temporal-extensions`, so Coldframe
publishes its own. The extension is built from its public source at a verified commit, and the
Quarkus build (`kc.sh build`, with `KC_DB=postgres`, health and metrics on) runs in a build stage.
The image must be started with `start --optimized`. The local Aspire stack builds the same
Dockerfile.

## Tags and platforms

- A release tag `vX.Y.Z` (or `vX.Y.Z-<pre-release>`) publishes every image as `:X.Y.Z` (or
  `:X.Y.Z-<pre-release>`). No `latest` or other floating tag is ever pushed; pin the exact version.
- Images are manifest lists for `linux/amd64` and `linux/arm64`. If a final base image lacks one of
  them, the component is pushed for the platforms it has, and the release run warns and names the
  missing architecture in its step summary.
- Every image carries the OCI labels `org.opencontainers.image.source`, `.version`, `.revision` (the
  full commit SHA) and `.licenses`.

## Health

| Component | Liveness | Readiness |
| --- | --- | --- |
| Server | `GET /.well-known/healthz/live` | `GET /.well-known/healthz/ready` (the silo is up) |
| Web | `GET /.well-known/healthz/live` | `GET /.well-known/healthz/ready` (the configuration is loaded; 503 before) |
| Migrations | none: a Kubernetes Job; exit code 0 on success, 1 on failure or missing configuration | |
| Keycloak | `GET /health/live` on port 9000 | `GET /health/ready` on port 9000 (`/health/started` for a startup probe) |

The health endpoints need no authentication. The web endpoints answer before any session or OIDC
handling, so a probe never triggers OIDC discovery.

## Configuration

The images are configured only through environment variables. Nothing environment-specific is baked
in: no connection string, URL or secret.

**Server**

| Variable | Required | Notes |
| --- | --- | --- |
| `ConnectionStrings__coldframe` | yes | PostgreSQL (Npgsql format) |
| `ConnectionStrings__nats` | yes, unless `Journal__HintStream__Enabled=false` | e.g. `nats://nats:4222` |
| `Identity__Authority`, `Identity__Audience` | yes | Keycloak realm issuer; audience `coldframe-server` |
| `Identity__RequireHttpsMetadata` | no | default `true` |
| `Keycloak__BaseUrl`, `Keycloak__Realm`, `Keycloak__ClientId`, `Keycloak__ClientSecret` | yes | the Server's service account |
| `KeycloakEvents__TargetHost`, `KeycloakEvents__Namespace`, `KeycloakEvents__RealmId` | yes | Temporal frontend (`host:7233`) of the Keycloak event pipeline |
| `ASPNETCORE_HTTP_PORTS` | no | default `8080` |
| `Orleans__Endpoints__SiloPort`, `Orleans__Endpoints__GatewayPort` | no | defaults `11111` and `30000` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | no | turns on OTLP export |

**Web**

| Variable | Required | Notes |
| --- | --- | --- |
| `COLDFRAME_SERVER_URL` | yes | base URL of the Server |
| `KEYCLOAK_ISSUER`, `KEYCLOAK_CLIENT_ID`, `KEYCLOAK_CLIENT_SECRET` | yes | the `coldframe-web` client |
| `KEYCLOAK_ALLOW_INSECURE_HTTP` | no | default `false`; local Keycloak only |
| `SESSION_COOKIE_SECURE` | no | default `true` |
| `ORIGIN` | behind a proxy | public origin, see the adapter-node docs |
| `PORT` | no | default `3000` |

**Migrations**

| Variable | Required | Notes |
| --- | --- | --- |
| `ConnectionStrings__coldframe` | yes | without it the job exits 1 |

**Keycloak** (the standard Keycloak options; the ones the chart sets)

| Variable | Required | Notes |
| --- | --- | --- |
| `KC_DB_URL_HOST`, `KC_DB_URL_PORT`, `KC_DB_URL_DATABASE`, `KC_DB_USERNAME`, `KC_DB_PASSWORD` | yes | PostgreSQL; `KC_DB=postgres` is built in |
| `KC_BOOTSTRAP_ADMIN_USERNAME`, `KC_BOOTSTRAP_ADMIN_PASSWORD` | first start | temporary admin of the master realm |
| `KC_HTTP_ENABLED`, `KC_PROXY_HEADERS`, `KC_HOSTNAME` or `KC_HOSTNAME_STRICT=false` | yes | plain HTTP behind a TLS-terminating proxy |
| `KC_SPI_EVENTS_LISTENER__TEMPORAL__TARGET_HOST`, `KC_SPI_EVENTS_LISTENER__TEMPORAL__NAMESPACE` | yes | Temporal frontend (`host:7233`) and namespace of the event pipeline |
| `COLDFRAME_WEB_CLIENT_SECRET`, `COLDFRAME_SERVER_CLIENT_SECRET` | with a realm import | substituted into the imported realm file |

## Building and checking locally

Every Dockerfile builds from the repository root. The build stages run on the build platform and
the final stages only copy files, so an arm64 image builds on an amd64 host without QEMU.

```sh
docker buildx build -f apps/cs/server/Dockerfile     -t coldframe/server:dev     --load .
docker buildx build -f apps/ts/web/Dockerfile        -t coldframe/web:dev        --load .
docker buildx build -f apps/cs/migrations/Dockerfile -t coldframe/migrations:dev --load .
docker buildx build -f aspire/keycloak/Dockerfile    -t coldframe/keycloak:dev   --load .

# arm64, kept in the build cache
docker buildx build --platform linux/arm64 -f apps/cs/server/Dockerfile .
```

Structure tests ([container-structure-test](https://github.com/GoogleContainerTools/container-structure-test)
v1.22.1) check the entrypoint, the numeric user, the ports and the files:

```sh
for c in server web migrations keycloak; do
  container-structure-test test --image coldframe/$c:dev --config deploy/images/$c.cst.yaml
done
```

The smoke run starts PostgreSQL, NATS and the Temporal dev server, runs the migration job (exit 0,
then exit 1 without a connection string), and waits for the Server and the web app to answer 200 on
both health paths. It needs Docker and curl, and always removes what it started:

```sh
deploy/images/smoke.sh coldframe/server:dev coldframe/web:dev coldframe/migrations:dev
```

`SMOKE_TIMEOUT` (seconds, default 180) bounds each wait.

The release scripts have plain-bash tests that need no Docker:

```sh
deploy/images/test.sh
```

| Script | Purpose |
| --- | --- |
| `release-version.sh <tag>` | validates a release tag and prints the image version |
| `platforms.sh <dockerfile> [manifest.json]` | prints the release platforms the final base image provides; `missing:<platform>` on stderr |
| `smoke.sh <server> <web> <migrations>` | the smoke run above |
| `test.sh` | tests of the two scripts above and of the Dockerfile rules (all four Dockerfiles) |

## CI and release

- [`images-verify.yml`](../../.github/workflows/images-verify.yml) builds every Dockerfile for every
  release platform, loads the linux/amd64 images, runs the structure tests, the smoke run and the
  chart smoke install on k3d ([`../charts/smoke.sh`](../charts/README.md)). CI runs it on every pull
  request as the `Images` job.
- [`release.yml`](../../.github/workflows/release.yml) runs on a `v*` tag: it validates the tag, runs
  the same verification, then pushes each image with the version tag and the OCI labels. The tag must
  equal the version of every Helm chart (`deploy/charts/check-version.sh`), or nothing is pushed. Only the
  push job has `packages: write`, and it authenticates with `GITHUB_TOKEN` alone.
- Any `v*` tag publishes, whatever commit it points to: push release tags only from `main`, and
  protect `v*` tags with a repository tag ruleset so that only maintainers can create them.
