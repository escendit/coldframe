# deploy

Everything needed to run Coldframe outside a developer machine.

| Arrives in | What |
| --- | --- |
| Epic 2, Story 2.1 | Container images per release, built from one Dockerfile per component: see [`images/`](images/README.md) |
| Epic 2, Story 2.2 | Helm charts for NATS, Temporal, Keycloak, the Server (with its migration Job) and the web app: see [`charts/`](charts/README.md). The Secrets an adopter creates out of band: see [`SECRETS.md`](SECRETS.md) |
| Epic 2, Story 2.3 | The `database` chart: a CloudNativePG cluster with WAL archiving and base backups to S3, and its restore runbook [`docs/operations/restore.md`](../docs/operations/restore.md). The pinned cluster dependencies (cert-manager, CloudNativePG, Barman Cloud plugin): [`charts/dependencies.env`](charts/dependencies.env) |
| Epic 2, Story 2.4 | The `ingress` chart: Let's Encrypt certificates through DNS-01 (cert-manager, Cloudflare) and a Traefik Ingress on 443 only; RKE2's Traefik without port 80: see [`rke2/`](rke2/README.md); split DNS runbook [`docs/operations/split-dns.md`](../docs/operations/split-dns.md) |
| Epic 2, Story 2.5a | The Fleet bundles: a `fleet.yaml` in every chart folder, the operator bundles and the reference GitRepo in [`fleet/`](fleet), and the `rke2-traefik` bundle in [`rke2/`](rke2/README.md); the GitOps install runbook [`docs/operations/install.md`](../docs/operations/install.md). The Fleet smoke on a cluster is Story 2.5b |
| Epic 10 | The Compose file, as a reference example only |

No secret value is ever committed here. Charts reference Secrets by fixed names and keys, listed in
[`SECRETS.md`](SECRETS.md), and never create one (AD-15).

The reference install is GitOps: Fleet applies the GitRepo [`fleet/gitrepo.yaml`](fleet/gitrepo.yaml)
at a release tag, and the bundles install themselves in dependency order: cert-manager,
CloudNativePG and RKE2's Traefik configuration; the Barman Cloud plugin; the database; the
ingress; NATS, Temporal and Keycloak; the Server; the web app
([`docs/operations/install.md`](../docs/operations/install.md)). An upgrade is a new tag. The
same charts can still be installed by hand with Helm ([`charts/README.md`](charts/README.md#install)).

For local development use the Aspire AppHost in [`aspire/`](../aspire); see the [quickstart](../docs/quickstart.md).
