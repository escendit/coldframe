---
title: 'TLS with public certificates on my home network'
type: 'feature'
created: '2026-09-28'
status: 'awaiting-operator'
baseline_revision: '664af814b2b7f149c770dbc7689c1fa43b38753e'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-2-context.md'
warnings: [oversized]
deferred:
  - summary: >-
      The Traefik checks render rke2-traefik without the values RKE2 itself injects into the chart.
    evidence: |-
      Unverified: test.sh and smoke.sh layer only the HelmChartConfig valuesContent over the chart defaults. If RKE2 v1.36.4's own HelmChart values set anything under ports.web, the node could differ. Settle on the home server with `kubectl -n kube-system get helmchart rke2-traefik -o jsonpath='{.spec.valuesContent}'` and `kubectl -n kube-system get svc rke2-traefik` (443 only).
    location: >-
      deploy/rke2/rke2-traefik-config.yaml
    severity: medium (unverified)
operator_actions:
  - 'Host the DNS zone of your domain at Cloudflare and choose a dedicated subdomain (e.g. coldframe.<your-domain>) as the ingress chart value domain.'
  - 'Create a Cloudflare API token with Zone DNS Edit and Zone Read on that zone only, and store it as the Secret coldframe-dns01 (key api-token) in the coldframe namespace, as in deploy/SECRETS.md.'
  - 'On the home RKE2 server, apply deploy/rke2/rke2-traefik-config.yaml and confirm that kubectl -n kube-system get svc rke2-traefik lists 443 only and that curl http://<node-lan-address>/ is refused (deploy/rke2/README.md).'
  - 'Apply the guarded cert-manager recursive-nameserver patch from docs/operations/split-dns.md step 2 and confirm both flags appear once in the cert-manager args.'
  - 'Add per-host split-DNS records for the web, api and auth hosts on your router or local DNS pointing to the server LAN address, and confirm the node and the cluster resolve auth.<domain> to it (docs/operations/split-dns.md step 5).'
  - 'Install the ingress chart with your domain and confirm certificate/coldframe-tls becomes Ready from Let''s Encrypt production with a renewalTime before notAfter.'
  - 'From a phone on the home Wi-Fi, open https://<domain> and https://auth.<domain>/realms/coldframe/.well-known/openid-configuration and confirm no certificate warning and the expected issuer (docs/operations/split-dns.md step 7).'
  - 'Watch the first pull-request CI run and confirm the k3d chart smoke install, including the TLS phase, passes (it has only run on kind locally).'
---

<intent-contract>

## Intent

**Problem:** The Coldframe Services are `ClusterIP` only: nothing exposes the web app, the Server API and Keycloak on a real domain, no certificate is issued, and RKE2's bundled Traefik listens on port 80 by default (Story 2.4, AD-13, NFR1, NFR10).

**Approach:** Add a self-authored `ingress` chart: a namespaced cert-manager `Issuer` for Let's Encrypt with a Cloudflare DNS-01 solver fed by the fixed Secret `coldframe-dns01`, one `Certificate` for the three hosts, and one Traefik `Ingress` on the `websecure` entrypoint only. A `HelmChartConfig` for `rke2-traefik` removes the `web` (port 80) entrypoint. A new check asserts TLS on every Ingress and IngressRoute, DNS-01 on every ACME issuer and no port 80 anywhere, including the pinned rke2-traefik chart rendered with that config. The smoke install routes HTTPS through that Traefik with a test CA. `docs/operations/split-dns.md` is the runbook.

## Boundaries & Constraints

**Always:**
- Chart `deploy/charts/ingress`: `apiVersion: v2`, `version` = release version (`0.1.0`), `appVersion: "1.21.2"` (cert-manager); same helpers, labels, test style as the other charts; added to `test.sh`'s chart list.
- Values: `domain` (placeholder `coldframe.example.invalid`, renders only); `hosts.web|api|auth` (empty = `<domain>`, `api.<domain>`, `auth.<domain>`); `backends.web|api|auth` (`service`, `port`: `web:3000`, `server:8080`, `keycloak:8080`); `ingressClassName: traefik`; `acme.enabled: true`, `acme.server` (Let's Encrypt production URL; staging documented), `acme.email` (optional); `externalIssuer.name|kind` (`""`, `Issuer`), used only when `acme.enabled` is false, the documented private-CA fallback and the smoke's path. The chart `fail`s when `acme.enabled` is false and `externalIssuer.name` is empty.
- `Issuer` `coldframe-letsencrypt` (ACME, `privateKeySecretRef` `coldframe-letsencrypt-account`), exactly one solver: `dns01.cloudflare.apiTokenSecretRef` = `coldframe-dns01` / `api-token`. No `http01` anywhere.
- `Certificate` `coldframe-tls` (secret `coldframe-tls`), `dnsNames` = the three hosts, cert-manager's default renewal (2/3 of the lifetime).
- `Ingress` `coldframe`: `ingressClassName`, annotations `traefik.ingress.kubernetes.io/router.entrypoints: websecure` and `traefik.ingress.kubernetes.io/router.tls: "true"`, one rule per host to its backend, `tls` listing every host with `coldframe-tls`.
- `deploy/rke2/rke2-traefik-config.yaml`: `HelmChartConfig` `rke2-traefik` in `kube-system` whose `valuesContent` sets `ports.web: null` (verified: rke2-traefik 40.1.010 then renders no hostPort 80, no Service port 80, no `web` entrypoint).
- `deploy/charts/check-ingress.py` (stdin manifests, label; exit 1 naming each offender): every `Ingress` has `tls` covering each rule host and the two Traefik annotations with entrypoints exactly `websecure`; every `IngressRoute` (`traefik.io`) has `spec.tls` and `entryPoints` exactly `[websecure]`; every `Issuer`/`ClusterIssuer` with `acme` has ≥1 solver, each `dns01`, none `http01`; no `Service` port/nodePort 80, no container `containerPort`/`hostPort` 80, no container arg starting `--entryPoints.web.` or `--entrypoints.web.`. Run on every chart render in `render_and_check`, proven on fixtures, and on the pinned rke2-traefik chart: default values must fail (hostPort 80), our `valuesContent` must pass.
- `dependencies.env` pins rke2-traefik and rke2-traefik-crd `40.1.010` (URLs `https://rke2-charts.rancher.io/assets/rke2-traefik-1.34-1.36/<name>-40.1.010.tgz`, sha256 `f15c97da5d655baf7e1e658eb118eaee9598f2a9bdb3d038436aadcb4825f21e` / `77b34bbda2930bcfd314dc8d5463f038f7ce4e00b9b11c9c57802e5676f7b05b`), the chart RKE2 v1.36.4+rke2r1 ships.
- kubeconform validates `Issuer` and `Certificate` strictly against schemas generated from the pinned `cert-manager.yaml` CRDs (added to `crd-schemas.py`'s input and the schema cache key).
- `check-manifests.py` treats `apiTokenSecretRef` (name, key) as a Secret reference. SECRETS.md: `coldframe-dns01` consumer = `ingress` chart (Cloudflare API token, Zone DNS Edit + Zone Read on the zone); cert-manager generates `coldframe-tls` and `coldframe-letsencrypt-account`, which no contract Secret may take (test.sh clash check extended).
- `docs/operations/split-dns.md`: LAN ingress address; per-host (not whole-zone) local records on the router or local DNS for the three hosts; no public A record needed; node and cluster DNS must use that resolver so the Server and web reach `auth.<domain>`; cert-manager's `--dns01-recursive-nameservers-only --dns01-recursive-nameservers=1.1.1.1:53,9.9.9.9:53` (patch command) so split DNS cannot hide the challenge TXT record; phones must not bypass LAN DNS (Android Private DNS, browser secure DNS); verification from a phone on Wi-Fi (resolves to LAN address, handshake with public roots, no warning); `openssl s_client`/`curl` checks; renewal check (`kubectl get certificate`, `status.renewalTime`); private CA only as an unsupported fallback via `externalIssuer`.

**Never:**
- No HTTP-01, no redirect listener on 80, no inbound internet traffic, no LoadBalancer/NodePort Service in the charts; no rendered Secret, no Secret name as a value; no ClusterIssuer (the token Secret lives in the release namespace).
- No Fleet bundle (2.5); no change to Server, web or Keycloak runtime code or templates.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Production install | domain set, `coldframe-dns01` present | Issuer Ready, Certificate Ready for three hosts, HTTPS on 443 only | — |
| Fallback CA | `acme.enabled=false`, `externalIssuer.name=my-ca` | no Issuer rendered; Certificate references `my-ca` | — |
| No issuer | `acme.enabled=false`, no external name | render fails naming both values | `helm install` refuses |
| Route on port 80 | a manifest with entrypoint `web`, no tls, http01 or port 80 | `test.sh` fails naming it | — |

</intent-contract>

## Code Map

- `deploy/charts/nats/` -- smallest chart to copy (`_helpers.tpl`, labels, `tests/` style); `database/` for a non-image chart (`appVersion` not the release).
- `deploy/charts/test.sh` -- `charts=(...)`, `fetch` + CRD cache (`crd_schemas` path keyed by versions), `render_and_check` (add `check-ingress.py`), fixture sections, CNPG clash heredoc (extend with cert-manager names), final recovery section pattern.
- `deploy/charts/check-manifests.py` `references()` -- add `apiTokenSecretRef`.
- `deploy/charts/crd-schemas.py` -- unchanged; feed it `cert-manager.yaml` too (verify it copes with cert-manager CRDs).
- `deploy/charts/smoke.sh` -- operator install block (add rke2-traefik-crd + rke2-traefik via `helm install` from pinned tgz, values from the HelmChartConfig via python3/PyYAML, `kube-system`); `check_stack`; cleanup trap (kill port-forward, add certificates/issuers/ingress to diagnostics). k3d already disables its own Traefik.
- `deploy/charts/testdata/` -- new fixtures; `smoke-ca.yaml` (self-signed Issuer → CA Certificate `smoke-ca` → CA Issuer `smoke-ca`).
- `deploy/charts/keycloak/values.yaml` `hostname`, `server` `identity.authority`, `web` `keycloak.issuer`/`origin` -- only README install examples change to `auth.<domain>` etc.
- `.github/workflows/images-verify.yml` -- add the "Check PyYAML" step as in `ci.yml` before the chart smoke; comments. `ci.yml` `charts` job -- comment only (test.sh downloads more pins).
- Scratchpad (not repo), `$SCRATCH` = `/tmp/claude-1000/-var-home-simon-work-escendit-coldframe/33fad80e-e7e4-4bb9-8102-24d657514857/scratchpad` (also holds the downloaded and extracted rke2-traefik chart in `traefik/`): helm-unittest plugin at `$SCRATCH/plugins` (`HELM_PLUGINS`), kubeconform/shellcheck/actionlint/k3d in `$SCRATCH/bin`; smoke locally with `SMOKE_CLUSTER=kind KIND_EXPERIMENTAL_PROVIDER=podman DOCKER_HOST=unix:///run/user/1000/podman/podman.sock`.

## Tasks & Acceptance

**Execution:**
- `deploy/charts/dependencies.env` -- rke2-traefik pins.
- `deploy/rke2/rke2-traefik-config.yaml` + `deploy/rke2/README.md` -- HelmChartConfig and how to apply it (`/var/lib/rancher/rke2/server/manifests/` or `kubectl apply`), verification (`kubectl -n kube-system get svc rke2-traefik`: no 80).
- `deploy/charts/ingress/` -- `Chart.yaml`, `values.yaml`, `ci-values.yaml` (domain `coldframe.smoke.test`, `acme.enabled: false`, `externalIssuer.name: smoke-ca`), `templates/{_helpers.tpl,issuer.yaml,certificate.yaml,ingress.yaml}`, `tests/ingress_test.yaml` -- unit tests: TLS and hosts, annotations, backends, host overrides, Issuer DNS-01 Cloudflare refs and no http01, server/email, Certificate dnsNames/issuerRef, external issuer, the `fail`, no Secret.
- `deploy/charts/check-ingress.py` + fixtures (`ingress-no-tls.yaml`, `ingress-web-entrypoint.yaml`, `ingressroute-web.yaml`, `issuer-http01.yaml`, `service-port-80.yaml`, `documented-ingress.yaml`) -- rules per Always.
- `deploy/charts/test.sh` -- cert-manager CRD schemas, check-ingress in `render_and_check`, fixture section, rke2-traefik render section (default fails, config passes), clash check names, `ingress` chart, external-issuer render and `fail` check.
- `deploy/charts/check-manifests.py` -- `apiTokenSecretRef`.
- `deploy/charts/smoke.sh` -- Traefik install; after the first `check_stack`: apply `smoke-ca.yaml`, `install ingress`, wait `certificate/coldframe-tls` Ready, assert `renewalTime` < `notAfter`; port-forward `svc/rke2-traefik` 443; curl with `--resolve` and `--cacert` (CA from `coldframe-tls` `ca.crt`): `https://api.<d>/.well-known/healthz` Healthy, `https://auth.<d>/realms/coldframe/.well-known/openid-configuration` issuer `https://auth.<d>:<port>/realms/coldframe`, `https://<d>/.well-known/healthz/ready` 200; assert the Traefik Service and DaemonSet expose no port 80 and no `web` entrypoint.
- `docs/operations/split-dns.md` -- per Always.
- `deploy/SECRETS.md`, `deploy/charts/README.md` (chart table, prerequisites incl. cert-manager DNS patch and HelmChartConfig, install `ingress`, values, checks), `deploy/README.md`, workflows.

**Acceptance Criteria:**
- Given RKE2's Traefik with `deploy/rke2/rke2-traefik-config.yaml` and cert-manager 1.21.2, when the `ingress` chart is installed with a domain and `coldframe-dns01`, then an ACME Issuer with only a Cloudflare DNS-01 solver and a Certificate for the three hosts exist, and cert-manager renews it before expiry (renewalTime set).
- Given the Traefik and ingress config, when deployed, then only 443 serves the web, Server and Keycloak hosts and nothing listens on 80 (smoke asserts it; test.sh proves the RKE2 default would fail).
- Given `deploy/charts/test.sh`, when it runs, then every chart and the Traefik render pass the TLS/DNS-01/port-80 check, the fixtures fail it, and the `ingress` chart passes lint, unit tests, strict kubeconform and the Secret contract.
- Given `docs/operations/split-dns.md`, when followed, then a phone on the home Wi-Fi resolves the hosts to the LAN ingress address and the handshake succeeds with public roots (operator verification).

## Spec Change Log

## Review Triage Log

### 2026-09-28 — Review pass
- verdicts: 29 findings — high 0, medium 3, low 20, false 5, maybe-false 1
- findings:
  - `[low]` `[patch]` Blind: acme.email described as bringing expiry notices, which Let's Encrypt stopped sending in 2025 — values.yaml and split-dns.md step 4 now call it an optional account contact; renewal failures show only through the step-6 certificate check.
  - `[low]` `[patch]` Blind: the cert-manager args patch is not idempotent, and "re-applying the manifest removes the flags" is wrong for an unchanged-args kubectl apply — README and split-dns.md now use a guarded loop that adds only missing flags; the removal claim became "check the args after every upgrade".
  - `[medium]` `[patch]` Blind: the smoke never applies the documented cert-manager flags, so CI never proves cert-manager 1.21.2 starts with them — smoke.sh runs the same guarded patch after the cert-manager rollout; the kind smoke showed "deployment.apps/cert-manager patched" and a clean rollout.
  - `[maybe-false]` `[defer]` Blind: the checks do not see the values RKE2 injects into rke2-traefik — cannot be settled off the home server; deferred (medium, unverified) with the kubectl commands that settle it, and an operator action.
  - `[medium]` `[patch]` Blind: the examples use the apex as domain, so the web host is the apex and a LAN override hides any public site there — examples now use coldframe.example.org, and one sentence recommends a dedicated subdomain.
  - `[low]` `[patch]` Blind: dnsmasq/Pi-hole address=/domain/ matches every subdomain, including _acme-challenge — split-dns.md step 5 names the safe host-record= / Local DNS records form and warns against address=.
  - `[low]` `[patch]` Blind: a bare host without https:// gets connection refused (nothing on 80, no redirect) — split-dns.md tells users to open and bookmark the https:// URL; the HSTS suggestion is rejected (no HTTP listener exists to protect).
  - `[false]` `[reject]` Blind: a stale rke2-traefik-values.yaml in KUBECONFORM_CACHE can make the "config passes" check pass — the values are rewritten only after the HelmChartConfig check succeeds; when that check fails, test.sh fails as a whole, so no false pass of the run happens.
  - `[low]` `[reject]` Blind: check-ingress.py misses tls entries without secretName, targetPort 80, env-var or file entrypoints, Gateway API and IngressRouteTCP — no chart renders any of them; extra branches guard state not shown to occur (targetPort 80 is pod-internal and exposes nothing).
  - `[low]` `[reject]` Blind: unit tests miss fullnameOverride, duplicate hosts, string ports, invalid externalIssuer.kind; only issuer.yaml tests the empty-domain guard — helm template renders every template, so the guard in issuer.yaml always runs (that part is false); the rest are unlikely misconfigurations that fail loudly at install.
  - `[low]` `[patch]` Blind: the empty-domain message mentions the DNS-01 token even with acme off — message is now "domain is empty: set the domain of the three hosts"; the unit test still passes.
  - `[low]` `[patch]` Blind: DNS-01 token rotation revokes the old token without checking the new one — SECRETS.md and split-dns.md now force a renewal (cmctl renew or delete coldframe-tls) and wait for Ready before revoking.
  - `[low]` `[patch]` Blind: a charts/README.md sentence is not re-wrapped — re-wrapped.
  - `[false]` `[reject]` Edge: the Ingress is named after the release, not coldframe as the spec says, so runbooks expecting ingress/coldframe miss it — no runbook, check or doc references the Ingress by name, and the name follows the documented fullnameOverride convention of every chart but database.
  - `[low]` `[reject]` Edge: a named backend port string renders number 0 — backends.*.port is documented as a port number; the API server rejects the result loudly.
  - `[low]` `[reject]` Edge: backends.<c> set to null fails with an unclear template error — the render fails loudly on a deliberate misconfiguration.
  - `[low]` `[reject]` Edge: hosts set to null fails in sprig get — same as the previous row: a loud failure on deliberate misconfiguration.
  - `[low]` `[reject]` Edge: equal host overrides produce duplicate dnsNames and rules — unlikely configuration; cert-manager rejects it visibly; a guard adds a branch for undemonstrated state.
  - `[low]` `[reject]` Edge: a host override outside the token's zone never completes DNS-01 — the challenge shows the failure (split-dns.md step 4 diagnostics); a suffix guard would also block valid multi-zone setups.
  - `[low]` `[reject]` Edge: the placeholder domain installs and the ACME order fails — by design, like the database chart's placeholders: defaults must lint, and the pending Certificate is visible.
  - `[low]` `[reject]` Edge: empty acme.server renders an Issuer that never gets Ready — deliberate misconfiguration, visible on the Issuer.
  - `[low]` `[reject]` Edge: invalid externalIssuer.kind — the Certificate stays pending visibly; the fallback is unsupported by design.
  - `[low]` `[reject]` Edge: IngressRouteTCP/UDP and Gateway API listeners are not checked — no chart renders them.
  - `[low]` `[reject]` Edge: an entrypoint with another name bound to :80 escapes the argument check — without a hostPort 80 (which is checked) nothing on the node listens on 80; no chart or config renders one.
  - `[false]` `[reject]` Edge: extensions/v1beta1 Ingress spec.backend is not checked — that API was removed in Kubernetes 1.22; kubeconform against 1.36.0 rejects such a manifest first.
  - `[low]` `[reject]` Edge: other DNS-01 Secret selectors (apiKeySecretRef, route53, tsig) bypass the contract check — no template renders them; the chart supports Cloudflare only.
  - `[false]` `[reject]` Edge: stale Traefik values in the persistent cache — same refutation as the Blind row: a failing HelmChartConfig check fails test.sh.
  - `[medium]` `[patch]` Verification gap: the per-host TLS branch, the router.tls branch and the web-entrypoint argument branch of check-ingress.py are never shown to fail alone — fixtures ingress-partial-tls.yaml and ingress-no-router-tls.yaml with their expect_failure lines, and a second expect_failure on the default rke2-traefik render matching "configures the web entrypoint"; test.sh 105 passed.
  - `[false]` `[reject]` Intent: the operator hand-off (awaiting-operator, operator_actions, commit) is missing — finalization writes it after review, as the workflow orders.

## Design Notes

- Cloudflare is the reference DNS-01 provider: the contract's single `api-token` key maps onto `apiTokenSecretRef`; another provider is a chart change, stated in split-dns.md.
- Namespaced `Issuer`, not `ClusterIssuer`: a ClusterIssuer reads Secrets from the cert-manager namespace, breaking "Secrets in the releases' namespace".
- `ports.web: null` removes the entrypoint, its hostPort and Service port together; Helm treats null in a later values file as deletion, which is how helm-controller layers HelmChartConfig. RKE2's own injected values are not verified here: the operator checks the Service on the home server.
- Recursive nameservers: with split DNS the cluster's resolver may answer for the domain locally, hiding `_acme-challenge` TXT records from cert-manager's propagation check.

## Verification

**Commands:**
- `HELM_PLUGINS=$SCRATCH/plugins PATH=$SCRATCH/bin:$PATH deploy/charts/test.sh` -- expected: all pass
- `SMOKE_CLUSTER=kind deploy/charts/smoke.sh coldframe/server:dev coldframe/web:dev coldframe/migrations:dev coldframe/keycloak:dev` -- expected: exit 0 including the TLS phase
- `bash -n`, shellcheck, actionlint -- expected: clean

## Auto Run Result

Status: awaiting-operator

**Summary:** A new `ingress` chart serves the web app (`<domain>`), the Server API (`api.<domain>`) and Keycloak (`auth.<domain>`) over HTTPS. It renders a namespaced cert-manager `Issuer` `coldframe-letsencrypt` for Let's Encrypt. Its only solver is Cloudflare DNS-01, reading the token from `coldframe-dns01`/`api-token`. The chart also renders a `Certificate` `coldframe-tls` for the three hosts, renewed by cert-manager's default at 2/3 of its lifetime, and one Traefik `Ingress` on the `websecure` entrypoint only. `deploy/rke2/rke2-traefik-config.yaml` removes Traefik's `web` entrypoint, so nothing serves port 80. The new `check-ingress.py` asserts TLS on every Ingress and IngressRoute, DNS-01 on every ACME issuer and no port 80 anywhere. It runs on every chart render and on the pinned rke2-traefik 40.1.010 chart: the defaults fail and the config passes. The smoke install routes HTTPS through that Traefik with a test CA. `docs/operations/split-dns.md` is the runbook. A private CA (`externalIssuer`) is an unsupported fallback.

**Files changed:**
- `deploy/charts/ingress/`: chart, values, CI values, templates, 13 helm-unittest tests.
- `deploy/rke2/rke2-traefik-config.yaml`, `deploy/rke2/README.md`: the Traefik HelmChartConfig, how to apply and verify it.
- `deploy/charts/check-ingress.py` and `testdata/*`: the TLS/DNS-01/port-80 check with fixtures that prove it fails; `smoke-ca.yaml` is the smoke's test CA.
- `deploy/charts/test.sh`: cert-manager CRD schemas, the check on every render, the rke2-traefik render, the extended clash check, the `ingress` chart and its external-issuer mode.
- `deploy/charts/check-manifests.py`: `apiTokenSecretRef` counts as a Secret reference.
- `deploy/charts/dependencies.env`: rke2-traefik and rke2-traefik-crd 40.1.010 pins.
- `deploy/charts/smoke.sh`: the Traefik install, the guarded cert-manager DNS-01 resolver patch, and the TLS phase.
- `docs/operations/split-dns.md`, `deploy/SECRETS.md`, `deploy/charts/README.md`, `deploy/README.md`, `.github/workflows/ci.yml`, `images-verify.yml` (PyYAML step): docs and CI.

**Review findings:** 29 findings. 10 were patched: 3 medium (the smoke applies the cert-manager flags, subdomain examples, fixtures for three unproven check branches) and 7 low (acme.email wording, idempotent patch command, dnsmasq form, https:// note, mode-neutral error message, token rotation check, re-wrap). 1 was deferred: the values RKE2 injects into rke2-traefik (medium, unverified). 18 were rejected: 5 false (stale cached values twice, the Ingress name, the removed v1beta1 API, the operator hand-off handled at finalization) and 13 low (unrendered kinds and selectors, and loud failures on deliberate misconfigurations). Reasons are in the triage log.

**Follow-up review recommended:** true. This pass patched 0 high and 3 medium entries. The specific unverified risk: the rewritten operator commands in `docs/operations/split-dns.md` and `deploy/charts/README.md` have been shell-reviewed but never run verbatim against a cluster. These are the guarded cert-manager patch loop, the subdomain examples and the rotation steps. The smoke runs its own copy of the patch. The Let's Encrypt/Cloudflare path has never been exercised.

**Verification performed (local, rootless Podman):**
- `deploy/charts/test.sh`: 105 passed, 0 failed, after the patches.
- `shellcheck -x` and actionlint clean; `bash -n` on every script.
- `SMOKE_CLUSTER=kind deploy/charts/smoke.sh ...:dev`: exit 0 after the patches.
  - cert-manager patched with the resolver flags and rolled out.
  - Certificate `coldframe-tls` Ready for the three hosts with renewalTime before notAfter.
  - Over Traefik's 443 with the test CA: the Server `Healthy`, the Keycloak issuer `https://auth.coldframe.smoke.test:<port>/realms/coldframe`, and web readiness 200.
  - The Traefik Service exposes `websecure:443` only, with no web entrypoint.
  - The upgrade and backup/restore phases passed as before.

**Residual risks:**
- No Let's Encrypt certificate was issued. The Cloudflare DNS-01 path and the phone check are operator actions.
- RKE2's own injected Traefik values are not covered (deferred).
- The cert-manager flag patch lives outside any chart. The Fleet bundles of Story 2.5 must carry it.
- The k3d path of the smoke has not run yet. The first CI run settles it.

**Operator actions owed:** see `operator_actions` in the frontmatter.
