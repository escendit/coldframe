# deploy

Everything needed to run Coldframe outside a developer machine.

| Arrives in | What |
| --- | --- |
| Epic 2, Story 2.1 | Container images per release, built from one Dockerfile per component: see [`images/`](images/README.md) |
| Epic 2, Story 2.2 | Helm charts for NATS, Temporal, Keycloak, the Server (with its migration Job) and the web app: see [`charts/`](charts/README.md). The Secrets an adopter creates out of band: see [`SECRETS.md`](SECRETS.md) |
| Epic 2, Story 2.3 | The `database` chart: a CloudNativePG cluster with WAL archiving and base backups to S3, and its restore runbook [`docs/operations/restore.md`](../docs/operations/restore.md). The pinned cluster dependencies (cert-manager, CloudNativePG, Barman Cloud plugin): [`charts/dependencies.env`](charts/dependencies.env) |
| Epic 2, Story 2.4 | The `ingress` chart: Let's Encrypt certificates through DNS-01 (cert-manager, Cloudflare) and a Traefik Ingress on 443 only; RKE2's Traefik without port 80: see [`rke2/`](rke2/README.md); split DNS runbook [`docs/operations/split-dns.md`](../docs/operations/split-dns.md) |
| Epic 2, Story 2.5 | The Fleet bundles |
| Epic 10 | The Compose file, as a reference example only |

No secret value is ever committed here. Charts reference Secrets by fixed names and keys, listed in
[`SECRETS.md`](SECRETS.md), and never create one (AD-15).

Install order: cert-manager (with the DNS-01 resolver patch) → CloudNativePG → Barman Cloud
plugin, and on RKE2 the Traefik `HelmChartConfig`; then the releases database → nats → temporal →
keycloak → server → web → ingress ([`charts/README.md`](charts/README.md#install)).

For local development use the Aspire AppHost in [`aspire/`](../aspire); see the [quickstart](../docs/quickstart.md).
