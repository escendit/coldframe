# Epic 2 Context: Run Coldframe on my home server

<!-- Generated from planning artifacts. Regenerate with compile-epic-context if planning docs change. -->

## Goal

The whole Coldframe stack runs 24/7 on Simon's single-node home server. Phones, browsers and the Hub reach it over TLS with publicly trusted certificates, nothing is lost on restart, and backups go off the node and can be restored. The epic turns the Epic 1 codebase into a reproducible, GitOps-deployed installation: versioned images, Helm charts with a fixed Secret contract, a managed PostgreSQL cluster with tested restore, Let's Encrypt DNS-01 TLS with split DNS, and Fleet-driven installs and upgrades. It delivers no user-facing features. It is the deployment base that every later epic runs on and that adopters reproduce.

## Stories

- Story 2.1: Container images published per release
- Story 2.2: Helm charts with a fixed Secret contract
- Story 2.3: Database cluster with off-node backups and tested restore
- Story 2.4: TLS with public certificates on my home network
- Story 2.5: GitOps deployment to my RKE2 server

## Requirements & Constraints

- **Local-first:** the Server and Keycloak run on the home network with no inbound internet traffic. Outbound traffic is allowed for APNs/FCM, SMTP and the ACME DNS-01 challenge.
- **Durability:** restarting the Server pod, the database pod or the whole node loses no Readings, settings, open Alerts or events. Readings are kept indefinitely. The apps reconnect once the Server is back.
- **TLS everywhere:** every IP endpoint (Server, web, Keycloak) serves HTTPS only. There is no plain-HTTP listener for application traffic, and nothing serves port 80. The Hub and the apps trust public roots only. A private CA is only a documented fallback, not a supported path.
- **Reproducibility and open source:** another person can run the full stack from the public docs alone. Every dependency comes from a public registry or repository. Container images are provided. The reference deployment is single-node RKE2 with Fleet. A Compose file is a reference example only.
- **No secrets in Git:** the adopter creates all Secrets out of band. Charts never template secret values.
- **Test-first:** infrastructure stories are tested through chart unit tests (helm-unittest), helm lint, kubeconform schema validation and a CI smoke install on a disposable k3d cluster. The home-server run is verified by hand against a documented checklist. CI blocks merges on failures.

## Technical Decisions

- **Stack pins:** RKE2 v1.36.4+rke2r1 (stable), with its bundled Traefik ingress. Fleet v0.16.2, cert-manager 1.21.2, CloudNativePG 1.30.1 on PostgreSQL 18, NATS Server 2.15.0 (JetStream), Temporal Server 1.31.3, Phase Two Keycloak 26.6.7 with keycloak-orgs 0.182 and `keycloak-temporal-extensions` v0.0.1-rc.2. Recheck extension-JAR compatibility whenever either Keycloak or the extension is upgraded.
- **Images:** publish to `ghcr.io/escendit/coldframe/<component>` for the Server (Orleans silo, Edge API and SignalR in one), the web BFF and the migration job. Images are multi-arch (amd64/arm64) where the base image allows it. The workflow reports any component where it does not. Containers are configured only through environment variables, run as non-root, and expose liveness and readiness through the Escendit service defaults.
- **Versioning:** one SemVer per release tag `vX.Y.Z` across the monorepo. The image tag and the Helm chart appVersion both equal that tag.
- **Layout:** charts live in `deploy/charts/`, with Fleet bundles under `deploy/`. The Secret contract is `deploy/SECRETS.md`, which lists every Secret by fixed name and key: SMTP, APNs/FCM, DNS-01 token, Server enrolment private key, database credentials and the S3 backup target. Operator runbooks live in `docs/operations/` (`install.md`, `restore.md`, `split-dns.md`).
- **State:** Server pods are stateless. All durable state is in one CNPG cluster with separate databases and roles for Server/Orleans, Temporal and Keycloak. JetStream is disposable.
- **Upgrades:** there is a single Server replica with a stop-then-start strategy, and a graceful-shutdown timeout lets the silo drain. The migration Job (FluentMigrator, forward-only) runs first and must succeed before the Server rolls. Application startup never runs DDL.
- **Backups:** CNPG uses Barman Cloud to ship WAL continuously and take scheduled base backups to an adopter-provided S3-compatible target. In CI, RustFS stands in for S3. After a restore, every Device's replay window is advanced by a safety margin. The runbook must state the recovery point: data written after the last archived WAL segment is lost.
- **TLS and DNS:** cert-manager Issuers use Let's Encrypt DNS-01 for a real domain, and certificates renew automatically. Split DNS resolves that domain to the LAN ingress address. Chart tests assert that TLS is set on every Ingress and IngressRoute, that the Issuer uses DNS-01 and that no route serves port 80.
- **Fleet:** a GitRepo pointed at `deploy/` installs components in dependency order: CNPG, then the database, then ingress and TLS, then the Coldframe charts. An upgrade is a release-version change in Git.
- **Isolation lanes:** no Device, app or external system connects to NATS, Temporal or PostgreSQL. Only the ingress is exposed.
- **Environments:** local development uses the Aspire AppHost, which generates no deployment artifacts. Production is the reference deployment. There is no staging environment.

## Cross-Story Dependencies

- 2.1 comes first: its images are what the 2.2 charts deploy. 2.2 defines the Secret contract that 2.3 (database credentials, S3) and 2.4 (DNS-01 token) plug into. 2.5 composes 2.2 through 2.4 into one Fleet install and verifies restart durability end to end.
- Builds on Epic 1: the Server, web BFF, migration set, Aspire stack and Escendit service defaults already exist. The restore test checks that Sites, Lots, Memberships and events survive.
- Later epics depend on this one. The Hub (Epic 3) and the mobile apps need public-root TLS on the real domain. The Server enrolment private key Secret is consumed by Device enrolment in Epic 3. Push credentials (Epic 6) and SMTP for invitations (Epic 9) are wired as Secrets here. Epic 10 publishes the adopter docs built on these runbooks.
