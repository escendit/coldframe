# deploy

Everything needed to run Coldframe outside a developer machine. Nothing is here yet.

| Arrives in | What |
| --- | --- |
| Epic 2 | Helm charts, Fleet bundles and the list of Kubernetes Secrets an adopter creates out of band |
| Epic 10 | The Compose file, as a reference example only |

No secret value is ever committed here. Charts reference Secrets by fixed names and keys (AD-15).

For local development use the Aspire AppHost in [`aspire/`](../aspire); see the [quickstart](../docs/quickstart.md).
