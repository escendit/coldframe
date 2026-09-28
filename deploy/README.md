# deploy

Everything needed to run Coldframe outside a developer machine.

| Arrives in | What |
| --- | --- |
| Epic 2, Story 2.1 | Container images per release, built from one Dockerfile per component: see [`images/`](images/README.md) |
| Epic 2, Story 2.2 | Helm charts for NATS, Temporal, Keycloak, the Server (with its migration Job) and the web app: see [`charts/`](charts/README.md). The Secrets an adopter creates out of band: see [`SECRETS.md`](SECRETS.md) |
| Epic 2, Stories 2.3–2.5 | The database cluster with backups, TLS and split DNS, and the Fleet bundles |
| Epic 10 | The Compose file, as a reference example only |

No secret value is ever committed here. Charts reference Secrets by fixed names and keys, listed in
[`SECRETS.md`](SECRETS.md), and never create one (AD-15).

Install order, after the database: nats → temporal → keycloak → server → web
([`charts/README.md`](charts/README.md)).

For local development use the Aspire AppHost in [`aspire/`](../aspire); see the [quickstart](../docs/quickstart.md).
