---
title: 'GitOps bundles and install guide for my RKE2 server'
type: 'feature'
created: '2026-09-28'
status: 'awaiting-operator'
baseline_revision: 'd4c0ea79dc8a6973cd67b2497e23109e0750ba3f'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-2-context.md'
warnings: [oversized]
deferred:
  - summary: >-
      The seven Coldframe chart bundles have no helm.takeOwnership; adopting releases that a manual helm install created is unproven.
    evidence: |-
      Unverified: install.md says Fleet upgrades the existing releases in place. If Fleet v0.16.2's helm deployer refuses or conflicts on an existing same-name release it did not create, the move from a manual install stalls. Settle on a cluster: helm install one chart by hand, then apply its bundle and check the BundleDeployment (a candidate for the Story 2.5b smoke).
    location: >-
      deploy/charts/*/fleet.yaml; docs/operations/install.md (Moving from a manual install)
    severity: medium (unverified)
  - summary: >-
      The Fleet restore procedure pauses the GitRepo, which may not stop the agent from reinstalling the uninstalled database release.
    evidence: |-
      Unverified: spec.paused stops new Git revisions reaching Bundles, but the existing BundleDeployment stays. Whether the agent redeploys a Helm release removed with helm uninstall (drift correction is off by default) decides whether a fresh cluster appears between steps 3 and 4. Settle on a cluster: pause, helm uninstall database, and watch the BundleDeployment for 10 minutes.
    location: >-
      docs/operations/restore.md (After a restore, Under Fleet)
    severity: medium (unverified)
  - summary: >-
      The keycloak bundle does not depend on temporal, although Keycloak's event pipeline targets temporal:7233.
    evidence: |-
      Unverified: the epic order puts nats, temporal and keycloak in parallel after database and ingress. The manual install order ran temporal before keycloak. Whether keycloak-temporal-extensions v0.0.1-rc.2 fails startup or readiness, or only drops or retries events, when Temporal or its namespace is not there yet, decides whether a keycloak -> temporal edge is needed. Settle on a cluster: start Keycloak with Temporal scaled to 0.
    location: >-
      deploy/charts/keycloak/fleet.yaml; deploy/fleet/check-fleet.py ORDER
    severity: medium (unverified)
operator_actions:
  - "Cut the first release tag that contains deploy/fleet (for example v0.1.0) and make sure spec.revision in deploy/fleet/gitrepo.yaml, or in your applied copy, names that existing tag."
  - "On the home RKE2 server, follow docs/operations/install.md steps 1 to 5: install Fleet v0.16.2 from the sha-checked charts, create the Secrets of deploy/SECRETS.md and the coldframe-realm ConfigMap, apply your filled-in coldframe-values ConfigMap, then apply the GitRepo and watch all 11 bundles become Ready."
  - "If the server already runs a manual Helm install, first follow the 'Moving from a manual install' section of docs/operations/install.md (remove rke2-traefik-config.yaml from the RKE2 manifests directory and delete deployment cnpg-controller-manager and barman-cloud in cnpg-system), then delete the listed leftovers once every bundle is Ready."
  - "Run the verification checklist of docs/operations/install.md on the home server and tick every item: bundles and pods ready, cert-manager args and 443-only Traefik, valid HTTPS on all three hosts, sign-in from a phone and a browser, restart durability (Server pod, database primary, node reboot), and a completed backup in S3."
---

<intent-contract>

## Intent

**Problem:** Today the stack can only be installed by hand. The operator runs `kubectl apply` for the operators, applies a cert-manager patch, then runs seven `helm install` calls in order (`deploy/charts/README.md`). There is no GitOps description of the install, no `docs/operations/install.md`, and no check that such a description is well formed.

**Approach:**
- Describe the whole install as Fleet v0.16.2 bundles under `deploy/`: three pinned upstream operator charts, the RKE2 Traefik `HelmChartConfig`, and the seven Coldframe charts. The bundles are chained by label-selector `dependsOn` edges.
- One reference GitRepo `deploy/fleet/gitrepo.yaml` lists the bundle folders. Its `revision` is the release tag.
- Site values come from one out-of-band ConfigMap through `helm.valuesFrom`.
- `deploy/charts/test.sh` renders the bundles with `fleet apply -o -` and checks them. Every checker failure is proven by a fixture.
- `docs/operations/install.md` holds the procedure and the manual checklist.
- The cluster proof (Fleet smoke) is Story 2.5b.

## Boundaries & Constraints

**Always:**
- Each Coldframe chart's `fleet.yaml` sits in its chart folder, `deploy/charts/<chart>/fleet.yaml`. Fleet 0.16.2 rejects chart paths and symlinks that point outside a bundle folder.
- GitRepo `paths` list only folders that contain a `fleet.yaml`. A scanned folder with files of its own would become a catch-all bundle.
- Labels and `dependsOn`:
  - Every bundle carries the label `coldframe.escendit.io/bundle: <name>`.
  - Every `dependsOn` is `selector.matchLabels` on that label. None uses a bundle `name`.
- The `dependsOn` edges are:
  - `cert-manager`, `cloudnative-pg` and `rke2-traefik`: none
  - `barman-cloud`: cert-manager and cloudnative-pg
  - `database`: cloudnative-pg and barman-cloud
  - `ingress`: cert-manager, rke2-traefik and database
  - `nats`, `temporal` and `keycloak`: database and ingress
  - `server`: nats, temporal and keycloak
  - `web`: server and keycloak
- Pins live in `deploy/charts/dependencies.env` and equal the `fleet.yaml` repo and version:
  - cert-manager: chart `v1.21.2` from `https://charts.jetstack.io`
  - cloudnative-pg: chart `0.29.1` (app 1.30.1) from `https://cloudnative-pg.github.io/charts`
  - plugin-barman-cloud: chart `0.8.0` (app v0.15.0) from `https://cloudnative-pg.github.io/charts`
  - The Fleet v0.16.2 CLI is pinned by sha256 per architecture: amd64 `3e15a4ad…9a19a9a`, arm64 `a43b471f…4fe6e9d`.
- The cert-manager bundle sets the two DNS-01 recursive-nameserver flags from `split-dns.md` through `extraArgs`. This replaces the manual patch.
- Release names and the `coldframe` namespace equal those in `charts/README.md`.
- Values:
  - Fixed in-cluster values go in `helm.values`.
  - Site values (domain, S3, issuer and origin URLs, ACME email) come from ConfigMap `coldframe-values` in namespace `coldframe`, through `valuesFrom` `configMapKeyRef`. Its keys are `database`, `keycloak`, `server`, `web` and `ingress`.
  - `deploy/fleet/values.example.yaml` documents the ConfigMap, and CI renders it.
  - Nothing sets `image.tag`.
  - Git holds no secret values, and `SECRETS.md` is unchanged.
- Scripts follow the house style: `set -euo pipefail`, `ok`/`fail` lines, sha-checked pinned downloads. They must pass shellcheck, and workflows must pass actionlint.

**Never:**
- No change to chart templates or behavior.
- No chart publishing.
- No vendored operator manifests.
- No Rancher.
- No Fleet smoke and no `images-verify.yml` change: both belong to Story 2.5b.
- Do not merge `backup/2-5-attempt`. Its `deploy/` and `docs/operations/` files may be taken file by file, because `deploy/` is unchanged on this branch since that backup's base (b515f4c).
- Do not mark this story `blocked` for the home-server steps. Those are operator actions.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Repo bundles | `fleet apply -o -` over the `gitrepo.yaml` paths | Exactly the 10 bundles pass `check-fleet.py` | No error expected |
| Stray bundle | A path without `fleet.yaml`, or an unlabeled bundle | The check fails and names it | Fixture-proven |
| Broken graph | A selector matching 0 or several bundles, a `dependsOn` by name, a cycle, or a missing order edge | The check fails | Fixture-proven |
| Pin/value drift | A version other than `dependencies.env`, the cert-manager flags missing, `image.tag` set, or `valuesFrom` missing | The check fails | Fixture-proven |
| Example values | Each chart rendered with its `fleet.yaml` values plus its key of `values.example.yaml` | helm template, kubeconform, the Secret contract and the TLS checks all pass | No error expected |

</intent-contract>

## Code Map

- `backup/2-5-attempt` (commit 5f84ab2): verified implementation of everything below. It also contains `deploy/fleet/smoke.sh` and `testdata/helmchartconfig-crd.yaml`, which are 2.5b and must not be taken. Its `.github/workflows/ci.yml` diff (a fleet CLI install step in the `charts` job, plus a `fleet --version` line) must be re-applied by hand, because `ci.yml` has changed on this branch since.
- `deploy/charts/test.sh`:
  - `charts=(...)`, then `check`/`expect_failure`/`fetch`, then `render_and_check`.
  - The backup adds a "fleet bundles" section: fleet apply, `check-fleet.py`, one `expect_failure` per fixture, and a `render_and_check` per chart using its Fleet values.
  - The previous session's run of the backup tree: 146 passed, 0 failed.
- `deploy/charts/dependencies.env`: plain `NAME=value` lines sourced by bash. The backup adds the `*_HELM_REPO`, `*_CHART_VERSION` and `FLEET_*` lines.
- `deploy/rke2/`: becomes the `rke2-traefik` bundle (`fleet.yaml` and a `.fleetignore` for its README).
- Docs to update: `deploy/README.md` (Story 2.5 row, install order), `deploy/charts/README.md` (the "arrive with 2.5" line), `docs/operations/split-dns.md` (under Fleet, the flags come from the bundle), and `docs/operations/restore.md` (recovery values go in `coldframe-values`).
  - The backup's wording mentions "the Fleet smoke" and "Story 2.5". Change it to 2.5a, and mark the smoke as 2.5b.
- Tools: fleet v0.16.2 (sha verified), kubeconform, shellcheck and actionlint in the scratchpad `bin/`, and helm-unittest in `plugins`, copied from `/tmp/claude-1000/-var-home-simon-work-escendit-coldframe/804617ce-7e62-4c70-99a5-4f210b4671f7/scratchpad/`.

## Tasks & Acceptance

**Execution:**
- Take the following from `backup/2-5-attempt`: `deploy/charts/*/fleet.yaml` (×7), `deploy/fleet/{cert-manager,cloudnative-pg,barman-cloud}/fleet.yaml`, `deploy/fleet/{gitrepo.yaml,values.example.yaml,check-fleet.py}`, `deploy/fleet/testdata/*` (except `helmchartconfig-crd.yaml`), `deploy/rke2/{fleet.yaml,.fleetignore,README.md}`, `deploy/charts/dependencies.env` and `deploy/charts/test.sh`. Review each one against the Always rules. Fix the comments that point at the smoke so they say Story 2.5b.
- `.github/workflows/ci.yml`: in the `charts` job, add the sha-checked fleet CLI install and `fleet --version`, and update the job comment.
- `docs/operations/install.md`: cover prerequisites (RKE2, Fleet via Helm at the pinned version, Secrets, realm, and the `coldframe-values` ConfigMap), applying the GitRepo, watching bundles, upgrading by changing `revision`, moving from a manual install, and a manual checklist: pods ready, valid HTTPS on all three hosts, sign-in from phone and browser, restart durability, and a backup in S3.
- Update `deploy/README.md`, `deploy/charts/README.md`, `split-dns.md` and `restore.md`.

**Acceptance Criteria:**
- Given the repo, when `deploy/charts/test.sh` runs, then it ends with 0 failed, the Fleet section passes, and every `deploy/fleet/testdata` fixture is rejected by `check-fleet.py`.
- Given `deploy/fleet/gitrepo.yaml`, when it is inspected, then `revision` is a release tag, and its `paths` are exactly the 10 bundle folders.
- Given `docs/operations/install.md`, when it is read, then it covers every topic the story names. The home-server run of its checklist is an operator action.

## Spec Change Log

- 2026-09-28: The I/O matrix and the gitrepo AC say "10 bundles", but the Approach (3 operators + rke2-traefik + 7 charts) and the `dependsOn` list name 11. The implementation has 11 bundles and 11 GitRepo paths, and `check-fleet.py` enforces all 11. Read "10" as "11".

## Review Triage Log

### 2026-09-28 — Review pass
- verdicts: 34 findings — high 1, medium 3, low 18, false 6, maybe-false 6
- findings:
  - `[medium]` `[patch]` Blind: gitrepo.yaml revision v0.1.0 is not an existing tag, and nothing checks that revision is a release tag — install.md intro and step 4 and the gitrepo.yaml comment now say revision must name an existing release tag that contains deploy/fleet; check-fleet.py fails a revision that does not match ^v\d+\.\d+\.\d+$ (fixture gitrepo-revision-branch.yaml). Cutting the tag is an operator action.
  - `[low]` `[patch]` Blind: the dependencies.env header claims every chart download is sha-checked, but the operator charts are fetched by version only — header reworded.
  - `[high]` `[patch]` Blind: operator bundles never rendered; the takeOwnership adoption claims are untested — grouped with verification-gap #1; fixed there.
  - `[maybe-false]` `[defer]` Blind: the chart bundles lack takeOwnership for adopting manually created releases — depends on Fleet's helm deployer behaviour with an existing release; deferred (medium, unverified) with the cluster check that settles it.
  - `[maybe-false]` `[defer]` Blind: a paused GitRepo may not stop the agent from reinstalling the database during a restore — deferred (medium, unverified); settle by pausing, uninstalling and watching the BundleDeployment.
  - `[low]` `[patch]` Blind: the chart fleet.yaml helm.values repeat chart defaults and would override a future default change — removed the helm.values blocks from keycloak, server, temporal and web; comments adjusted.
  - `[maybe-false]` `[defer]` Blind: keycloak does not depend on temporal — whether the event extension fails without Temporal is unknown; deferred (medium, unverified).
  - `[low]` `[patch]` Blind: one stuck DNS-01 Certificate holds back nats, temporal, keycloak, server and web, and install.md does not say so — the edge is intended by the epic's order; added to install.md Common causes, with inspect commands.
  - `[low]` `[reject]` Blind: the committed spec has a local scratchpad path, a local backup branch and "10 bundles" — the fix edits this build's spec; the Spec Change Log already records 10 → 11.
  - `[low]` `[patch]` Blind: install.md prerequisites omit openssl, curl, jq and the aws CLI; the Fleet chart pins are unused; the checklist lacks the cert-manager args and 443-only checks — prerequisites extended; step 1 installs Fleet from the sha-checked FLEET_*_CHART_URL assets; checklist item added.
  - `[low]` `[reject]` Blind: the test.sh Fleet render reads fleet.yaml rather than the rendered bundles, and fleet_work has no trap — fleet apply copies helm.values and valuesFrom unchanged and check-fleet.py enforces exactly one valuesFrom, so no divergence was shown; an interrupted run leaving a temp dir is negligible, and a trap is added complexity.
  - `[low]` `[patch]` Blind: lines past the wrap in restore.md and split-dns.md — re-wrapped (also SECRETS.md, dependencies.env, nats and server fleet.yaml).
  - `[false]` `[reject]` Edge: a site key overlapping fleet.yaml values makes the render's precedence misleading — no key overlaps (the fleet.yaml values are now gone entirely), so the case does not occur.
  - `[low]` `[reject]` Edge: two chart folders could swap label and releaseName undetected — needs a deliberate cross-edit of two files; the fix is an extra guard; unlikely in everyday use.
  - `[low]` `[reject]` Edge: image.repository with ':tag' or a digest, or images in lists, escape the image.tag check — no values set repositories; an extra guard for an unlikely edit.
  - `[false]` `[reject]` Edge: a non-string ConfigMap data value crashes check_values with a traceback — the check still fails loudly (non-zero exit); that is correct behaviour.
  - `[low]` `[reject]` Edge: load_env ignores quotes and the export prefix — dependencies.env is documented as plain NAME=value lines and has none; guard not worth it.
  - `[low]` `[reject]` Edge: the rke2-traefik bundle has no specific takeOwnership check — an extra guard for an unlikely edit; the bundle file is 10 lines.
  - `[medium]` `[patch]` Edge: revision v0.1.0 does not exist — grouped with Blind #1; fixed there.
  - `[low]` `[patch]` Edge: a HelmChartConfig in RKE2's manifests directory would fight the Fleet bundle — install.md migration step added to remove that file.
  - `[maybe-false]` `[defer]` Edge: the paused GitRepo does not stop reconciliation during a restore — grouped with Blind #5; deferred.
  - `[low]` `[reject]` Edge: the fixture-coverage grep matches mentions in comments — only a deliberately misleading comment defeats it; a regex guard for an unlikely case.
  - `[low]` `[reject]` Edge: no trap removes fleet_work — same as Blind #11; negligible.
  - `[low]` `[patch]` Edge: SECRETS.md:116 still says cluster/coldframe-db — changed to clusters.postgresql.cnpg.io/coldframe-db.
  - `[low]` `[reject]` Edge (claim): the spec says 10 bundle folders, the code has 11 — the fix edits this build's spec; the Spec Change Log records it.
  - `[high]` `[patch]` Verification gap: the operator charts' Deployment selectors differ from the kubectl manifests (immutable), so takeOwnership adoption fails and blocks every bundle after — install.md migration now deletes deployment cnpg-controller-manager and barman-cloud first (data untouched); the fleet.yaml comments no longer claim adoption as-is.
  - `[medium]` `[patch]` Verification gap: many check-fleet.py branches have no fixture — 14 fixtures added with expect_failure: releaseName, namespace, takeOwnership, repo, extra edge, crds.enabled, matchExpressions, chart not in its own folder, valuesFrom on nats, path listed twice, revision, and ConfigMap image.tag / missing key / wrong name (check_fleet takes a values-file override); test.sh 146 → 160 passed.
  - `[medium]` `[patch]` Verification gap (other): the barman plugin manifest's leftovers (ServiceAccount plugin-barman-cloud, Issuer selfsigned-issuer, RBAC) were not listed — grouped with verification-gap #1; added to the leftovers list.
  - `[low]` `[reject]` Verification gap (other): unlabeled-bundle.yaml only weakly proves the label check — the fixture's name matches the real nats bundle, so the merge applies; other name-bearing fixtures catch naming drift.
  - `[false]` `[reject]` Intent: the frontmatter lacks the awaiting-operator handoff — the handoff is written at finalization (this pass: status awaiting-operator with operator_actions).
  - `[maybe-false]` `[defer]` Intent: whether "revision is a release tag" holds is an out-of-repo fact — grouped with Blind #1; the tag itself is an operator action.
  - `[false]` `[reject]` Intent: the epic's 10 vs 11 bundle count — the epic AC names no count ("exactly those bundles"); 11 follows the listed bundles.
  - `[false]` `[reject]` Intent: AC3 "when I follow it on the home server" is restated as document coverage — the home-server run is recorded as an operator action, not dropped.
  - `[false]` `[reject]` Intent: the clusters.postgresql.cnpg.io rename and the Fleet restore notes are beyond the listed scope — consistent with the intent; no harm named.

## Design Notes

A GitRepo on `deploy/` itself would create a catch-all bundle from `deploy/`'s own files, including the test fixtures. Listing the bundle folders in `paths` is the supported layout. CI reads those same `paths`, so the documented GitRepo and the tested one can't drift.

`check-version.sh` forces chart `version` and `appVersion` to equal the release tag. Pinning `revision: vX.Y.Z` therefore pins the charts and the images together.

## Verification

**Commands:**
- `PATH=$SCRATCH/bin:$PATH HELM_PLUGINS=$SCRATCH/plugins deploy/charts/test.sh`: expected "0 failed", including the Fleet section.
- `shellcheck deploy/charts/test.sh && actionlint && python3 -m py_compile deploy/fleet/check-fleet.py`: expected clean.

## Auto Run Result

Status: awaiting-operator

**Summary:** Coldframe is now described as 11 Fleet v0.16.2 bundles: cert-manager, CloudNativePG and the Barman Cloud plugin from pinned upstream charts; the RKE2 Traefik HelmChartConfig; and the seven Coldframe charts. The bundles are chained by label-selector dependsOn in the epic's order. `deploy/fleet/gitrepo.yaml` is the reference GitRepo; its revision is a release tag and its paths are the 11 bundle folders. Site values come from the out-of-band ConfigMap `coldframe-values` through helm.valuesFrom, with a rendered example. `deploy/charts/test.sh` (the Charts CI job) renders the bundles with `fleet apply -o -`, checks them with `deploy/fleet/check-fleet.py`, proves every checker failure with a fixture, and renders every chart with its Fleet values. `docs/operations/install.md` is the GitOps runbook with the manual checklist. The decisions and files of the timed-out 2.5 attempt were reused file by file; the smoke stays in Story 2.5b.

**Files changed:**
- `.github/workflows/ci.yml`: the Charts job installs the sha-checked fleet CLI.
- `deploy/charts/dependencies.env`: the operator Helm repos and chart versions, and the Fleet CLI and chart pins.
- `deploy/charts/test.sh`: the Fleet bundles section (render, check-fleet.py, 25 fixtures, Fleet-values renders).
- `deploy/charts/{database,ingress,nats,temporal,keycloak,server,web}/fleet.yaml`: the chart bundles.
- `deploy/fleet/{cert-manager,cloudnative-pg,barman-cloud}/fleet.yaml`: the operator bundles.
- `deploy/fleet/gitrepo.yaml`, `deploy/fleet/values.example.yaml`: the reference GitRepo and the site-values ConfigMap.
- `deploy/fleet/check-fleet.py`, `deploy/fleet/testdata/*.yaml`: the bundle checker and its 25 failure fixtures.
- `deploy/rke2/{fleet.yaml,.fleetignore,README.md}`: the rke2-traefik bundle.
- `docs/operations/install.md`: new GitOps install runbook and checklist.
- `deploy/README.md`, `deploy/charts/README.md`, `deploy/SECRETS.md`, `docs/operations/restore.md`, `docs/operations/split-dns.md`: Fleet notes and the unambiguous `clusters.postgresql.cnpg.io` kind.

**Review:** 34 findings. 10 patch groups were applied, covering 1 high, 3 medium and 8 low findings. 3 were deferred as medium (unverified): chart-bundle adoption of manual releases, restore under a paused GitRepo, and a keycloak → temporal edge. The rest were rejected with the reasons recorded in the triage log.

**Follow-up review:** recommended (a high patch was applied). The unverified risk: the rewritten "Moving from a manual install" procedure (deleting the two operator Deployments, then takeOwnership adopting the rest) rests on rendered-chart comparisons, not on a cluster run.

**Verification:**
- `deploy/charts/test.sh`: 160 passed, 0 failed, including the Fleet section. All 11 paths render, check-fleet.py passes on the repo, every one of the 25 fixtures is rejected, and all seven charts pass template, kubeconform, the Secret contract and the TLS checks with their Fleet values.
- `shellcheck -x deploy/charts/test.sh`, `actionlint` and `python3 -m py_compile deploy/fleet/check-fleet.py` are clean.

**Residual risks:**
- Nothing ran on a cluster; the Fleet smoke is Story 2.5b.
- The three deferred items need a cluster to settle.
- The GitRepo's revision needs a release tag that contains deploy/fleet; none exists yet.
