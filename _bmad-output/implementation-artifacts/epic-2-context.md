# Epic 2 Context: Run Coldframe on my home server

<!-- Generated from planning artifacts. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Run the whole Coldframe stack 24/7 on Simon's single home server, delivered by GitOps. Phones, browsers and the Hub reach it over TLS with publicly trusted certificates and no inbound internet traffic. Restarts lose nothing, and off-node backups can be restored. The epic adds no user-facing features. It turns the Epic 1 services into a reproducible, durable deployment that every later epic ships onto, and that adopters can rebuild from the public docs.

## Stories

- Story 2.1: Container images published per release
- Story 2.2: Helm charts with a fixed Secret contract
- Story 2.3: Database cluster with off-node backups and tested restore
- Story 2.4: TLS with public certificates on my home network
- Story 2.5a: GitOps bundles and install guide for my RKE2 server
- Story 2.5b: Fleet smoke proves ordered install, upgrade and restart durability

## Requirements & Constraints

- **Local-first:** the Server and Keycloak run on the home network with no inbound internet traffic. Only outbound traffic is allowed: APNs/FCM, SMTP and ACME DNS-01.
- **TLS everywhere:** every IP endpoint (Server API, web app, Keycloak) serves HTTPS with a publicly trusted certificate. Nothing serves application traffic over plain HTTP, including on the LAN.
- **Durability:** restarting the Server pod, the database pod or the whole node loses no stored Readings, settings, events or open Alerts. Readings are kept indefinitely.
- **Reproducibility:** someone else can run the full stack from the public docs alone. Container images are published. The reference deployment is single-node RKE2 with Fleet. Compose is only a reference example and is not part of this epic.
- **Test-first infrastructure:** infrastructure is tested with chart unit tests (helm-unittest), helm lint, kubeconform schema validation and a CI smoke install on a disposable k3d cluster (kind is acceptable locally). CI blocks merges on failure. The real home-server run is checked by hand against a documented checklist.
- The smoke run must fit the Images CI job timeout. On failure it prints bundle, BundleDeployment and pod diagnostics.

## Technical Decisions

- **Platform:** single-node RKE2 (stable v1.36.4+rke2r1), with Fleet v0.16.2 pulling the Helm charts in `deploy/`. Ingress is RKE2's bundled Traefik (configured via `HelmChartConfig`). There is no staging environment. The Aspire AppHost is for local dev and tests only and generates no deployment artifacts.
- **Charts:** `deploy/charts/` covers the Server (Orleans silo, Edge API, SignalR), the web BFF, Keycloak (Phase Two image 26.6.7 plus `keycloak-temporal-extensions` v0.0.1-rc.2, whose compatibility must be re-checked on any upgrade), Temporal and NATS JetStream. It also has the database and ingress charts, seven Coldframe charts in total. Upstream pins live in `deploy/charts/dependencies.env`, and every bundle pin must equal them.
- **State:** Server pods are stateless. All durable state is in one CloudNativePG 1.30.1 cluster on PostgreSQL 18, with separate databases and roles for Server/Orleans, Temporal and Keycloak. JetStream is disposable.
- **Backups:** CNPG uses the Barman Cloud plugin for continuous WAL archiving and scheduled base backups to an adopter-provided S3-compatible target. In CI, RustFS stands in for S3. The restore runbook states the recovery point. It also requires advancing every Device's replay window by a safety margin after a restore, so a restore cannot accept replayed frames.
- **Secrets:** Kubernetes Secrets have fixed names and keys and are listed in `deploy/SECRETS.md`. They cover SMTP, APNs/FCM, the DNS-01 token, the Server enrolment private key, database credentials and S3. The adopter creates them out of band. Charts never template secret values, and a test asserts that no Secret manifest contains data.
- **Site-specific values** (domain, S3, issuer and origin URLs, ACME email) come from one out-of-band ConfigMap through Fleet `helm.valuesFrom`. A documented example is rendered in CI. Git holds no secret values.
- **TLS:** cert-manager 1.21.2 issues Let's Encrypt certificates via DNS-01 on a real domain and renews them automatically. Split DNS resolves the domain to the LAN ingress. The apps and the Hub trust public roots only. A private CA is a documented fallback, not a supported path. Chart tests assert that TLS is set on every Ingress and IngressRoute, that the Issuer uses DNS-01, and that no route serves port 80.
- **Upgrades:** the Server runs as a single replica with a stop-then-start strategy and a graceful-shutdown timeout so the silo can drain. The migration Job (FluentMigrator, one forward-only set) runs first and must succeed before the Server rolls. Application startup never runs DDL.
- **Images and release:** images are published to `ghcr.io/escendit/coldframe/<component>:X.Y.Z` on tag `vX.Y.Z` for the Server, the web BFF and the migration job. They are multi-arch (amd64/arm64) where the base image allows, and the workflow reports any component where it does not. One SemVer applies across the repo, and the chart appVersion equals the tag. Containers take configuration from environment variables only, run as non-root and expose liveness and readiness endpoints through the Escendit service defaults. A container-structure test checks each image.
- **GitOps layout:** one reference GitRepo, `deploy/fleet/gitrepo.yaml`, whose `revision` is the release tag. Bundles are labelled and ordered with label-selector `dependsOn`: operators (cert-manager, CNPG, the Barman plugin), then the database, ingress and TLS, then the Coldframe charts. A checker run by `deploy/charts/test.sh` rejects cycles and dangling selectors, and each checker failure has a fixture that proves it. Upgrading means changing the tag in Git. Earlier verified fleet-CLI decisions (folder layout, pins and sha256s, cert-manager `extraArgs`) are recorded on branch `backup/2-5-attempt`. Reuse them, but do not merge that branch.

## Cross-Story Dependencies

- Epic 2 depends on Epic 1's services (Server, BFF, Keycloak/Temporal identity pipeline, migrations and the Aspire stack). Epic 3 onwards assumes this deployment: the Hub needs public-root TLS to reach the Server.
- 2.1 comes first: the charts and smoke tests consume its images.
- 2.2 comes next: 2.3 and 2.4 add the database and ingress/TLS charts to it.
- 2.5a bundles everything from 2.1–2.4 and needs no cluster.
- 2.5b proves 2.5a on a k3d cluster: ordered install, upgrade and restart durability.
- Adopter-facing docs written here (`docs/operations/install.md`, `restore.md`, `split-dns.md`) feed Epic 10's public adopter guide.
