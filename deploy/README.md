# deploy

Everything needed to run Coldframe outside a developer machine.

| Arrives in | What |
| --- | --- |
| Epic 2, Story 2.1 | Container images per release, built from one Dockerfile per component: see [`images/`](images/README.md) |
| Epic 2, Stories 2.2–2.5 | Helm charts, Fleet bundles and the list of Kubernetes Secrets an adopter creates out of band |
| Epic 10 | The Compose file, as a reference example only |

No secret value is ever committed here. Charts reference Secrets by fixed names and keys (AD-15).

For local development use the Aspire AppHost in [`aspire/`](../aspire); see the [quickstart](../docs/quickstart.md).
