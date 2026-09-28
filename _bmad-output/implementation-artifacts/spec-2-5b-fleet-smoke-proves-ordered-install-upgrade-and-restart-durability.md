---
title: 'Fleet smoke proves ordered install, upgrade and restart durability'
type: 'feature'
created: '2026-09-28'
status: 'awaiting-operator'
baseline_revision: 'e2c78273821e2f7e3c492e984bc829c576d4de70'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-2-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-2-5a-gitops-bundles-and-install-guide-for-my-rke2-server.md'
warnings: [oversized]
deferred:
  - summary: >-
      The Fleet smoke has only run on kind; the k3d path that CI uses, and the Images job timeout of 170 minutes, are unproven.
    evidence: |-
      Unverified: .github/ has no SMOKE_CLUSTER setting, so CI runs the k3d path (the real k3s HelmChartConfig CRD, k3d image import, docker restart of the k3d node), and every local run used kind. It is settled by one green Images job on GitHub Actions that includes the "Fleet smoke" step, together with its step duration.
    location: >-
      .github/workflows/images-verify.yml (Fleet smoke); deploy/fleet/smoke.sh
    severity: medium (unverified)
operator_actions:
  - "Push the loop branch chore/bmad-gzp-module-install (PR #10) and confirm that the Images CI job passes, including its new 'Fleet smoke' step on k3d, within the job's 170-minute timeout. If it fails, read the bundle, BundleDeployment and pod diagnostics the step prints."
---

<intent-contract>

## Intent

**Problem:** Story 2.5a describes the install as 11 Fleet bundles and checks them statically, but nothing has ever run them on a cluster. So nothing proves that Fleet installs them in dependency order, that an upgrade runs the migrations before a stop-then-start Server, or that a restart keeps committed data (NFR3, NFR7).

**Approach:** Add `deploy/fleet/smoke.sh`. On a throwaway single-node k3d cluster (kind locally) with Fleet v0.16.2, it runs `fleet apply` on the reference GitRepo's `paths` and waits until every bundle and pod is ready. It checks the dependency order, then upgrades by a version bump. It restarts the Server pod, the database primary and the node, and after each one proves the data is intact and HTTPS is healthy. The Images CI job runs it after the chart smoke. On failure it prints bundle, BundleDeployment and pod diagnostics.

## Boundaries & Constraints

**Always:**
- The bundles come only from `fleet apply --namespace fleet-local <gitrepo name> <gitrepo paths>`, where the name and paths are read from `deploy/fleet/gitrepo.yaml`, as gitjob would do it. There is no git server. `gitrepo.yaml` itself must pass `kubectl apply --dry-run=server` against Fleet's CRD.
- Fleet (`fleet-crd` and `fleet`), rke2-traefik and every other download are pinned in `deploy/charts/dependencies.env`, and each is checked against its sha256. The script uses no new unpinned binaries.
- The adopter's out-of-band objects are created the way `install.md` says: the Secrets of `SECRETS.md`, `coldframe-realm`, and ConfigMap `coldframe-values`. `coldframe-values` has exactly the keys of `values.example.yaml`: CI values, local image repositories with `pullPolicy: Never`, and the smoke CA's external issuer. It never sets `image.tag`.
- Install proof:
  - All 11 labelled bundles are Ready at their current generation, and every BundleDeployment has applied its current deployment.
  - For every `dependsOn` edge, the dependent's first Helm release was created no earlier than its dependency's.
  - Every pod is Ready or belongs to a completed Job. `/.well-known/healthz` reports Healthy, and the realm is served.
  - The three hosts answer over HTTPS through Traefik's 443, trusting only the smoke CA.
  - cert-manager runs with the DNS-01 flags of `split-dns.md`.
- Upgrade proof:
  - A copy of `deploy/` has every chart `version` (and the Coldframe `appVersion`) bumped to `<v>-smoke.1`. The images are retagged to match and the bundles re-applied.
  - The migration Job runs again with the new image and completes before the new Server pod is created.
  - A once-a-second poller never sees two Server pods that can run.
  - Marker rows and the domain-table digest are unchanged.
- Restart proof, after each of: Server pod delete, database primary delete, node container restart:
  - The bundles and pods become Ready again, and every long-running pod actually restarted after the node restart.
  - The committed markers are unchanged.
  - The digest of the Server's domain tables (`identity_memberships identity_sites journal_events journal_outbox lots projection_checkpoints`) is unchanged.
  - The realm is still served.
  - `https://api.<domain>/.well-known/healthz` reports Healthy.
- Markers are committed in the `coldframe` and `keycloak` databases. Those two hold the events and the settings.
- The EXIT trap always deletes the cluster and the smoke image tags. On failure it first prints:
  - bundles with their non-True conditions
  - BundleDeployments with ready, modified status and conditions
  - pods and Jobs in all namespaces
  - the `coldframe` events
  - the CNPG and cert-manager objects
  - pod logs
  - the logs of fleet-controller, fleet-agent, the operators and cert-manager
- Every wait is bounded by `SMOKE_TIMEOUT`. The script prints its total run time. The Images job `timeout-minutes` covers the chart smoke plus the Fleet smoke with margin.
- House style is that of `deploy/charts/smoke.sh`: `set -euo pipefail`, `ok:`/`FAIL:` lines, `SMOKE_CLUSTER=k3d|kind`, and Podman through `DOCKER_HOST`. The script is shellcheck-clean, and the workflow is actionlint-clean.

**Never:**
- No change to chart templates, to the bundles' `fleet.yaml`, to `check-fleet.py`, or to `deploy/charts/smoke.sh` behaviour. The one exception: if the smoke proves that a bundle needs a change to converge (for example `diff.comparePatches` for a Modified status, or a keycloak→temporal edge), make the minimal change there. Update `check-fleet.py`, its fixtures and `test.sh` with it, and record it in the Spec Change Log.
- No new file in `deploy/fleet/testdata/`. `test.sh` requires every file there to be a tested failure fixture.
- No Rancher, no git server, no chart publishing, and no merge of `backup/2-5-attempt`.
- The 2.5a deferred items (takeOwnership adoption of manual releases, restore under a paused GitRepo) are not part of this story.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Ordered install | Fresh cluster, prerequisites present, bundles applied | 11 bundles Ready, order holds, stack + HTTPS OK | Diagnostics, then `FAIL:` and exit 1 |
| Upgrade | Bumped tree re-applied | Job reruns first, never 2 Server pods, data intact | `FAIL:` with pod/Job timeline |
| Restarts | Server pod, DB primary, node | Ready again, data intact, HTTPS Healthy | `FAIL:` naming the restart and what was lost |
| Bad usage | Not 4 image arguments, wrong `SMOKE_CLUSTER`, fleet CLI ≠ `FLEET_VERSION` | Exit 2 or `FAIL:` before a cluster is created | No cluster left behind |

</intent-contract>

## Code Map

- `backup/2-5-attempt:deploy/fleet/smoke.sh` (commit 5f84ab2, 727 lines): a complete draft of this script that has never been verified. It has the cleanup/diagnostics trap, `fetch`, `import_images`, the Fleet install and agent wait, `coldframe-values` built from `ci-values.yaml` (inline Python), `bundles_ready`/`wait_bundles`/`wait_bundle`, `check_stack`, `check_https`, `release_order`, seed, upgrade poller + `timeline`, and the three restarts. Take it with `git show`, then adapt it to the Always rules. It has a marker in `coldframe` only, puts its CRD path under `deploy/fleet/testdata` (move it), and has no run-time print.
- `backup/2-5-attempt:deploy/fleet/testdata/helmchartconfig-crd.yaml`: the stand-in HelmChartConfig CRD for kind. Put it at `deploy/charts/testdata/helmchartconfig-crd.yaml`, next to the smoke assets `smoke-ca.yaml` and `rustfs.yaml`.
- `backup/2-5-attempt:.github/workflows/images-verify.yml` diff: adds the fleet CLI install (sha-checked, from `dependencies.env`), a "Fleet smoke" step after "Chart smoke install", timeout 110→170, and a header comment.
- `deploy/charts/smoke.sh`: the reference for style, `kind` image import via `ctr` (:191-194), `sql`/`primary`, and the HTTPS port-forward. Read it only.
- The 2.5a bundles and their facts:
  - The operator bundles are in `deploy/fleet/{cert-manager,cloudnative-pg,barman-cloud}/fleet.yaml`, with releases `cert-manager`, `cnpg` and `barman-cloud`.
  - Each Coldframe bundle is at `deploy/charts/<chart>/fleet.yaml`, namespace `coldframe`. Only database, ingress, keycloak, server and web use `valuesFrom` on `coldframe-values`. The 2.5a review removed their `helm.values`.
  - `deploy/rke2/fleet.yaml` has release `rke2-traefik-config` in `kube-system`.
- Server chart: Deployment `server` has strategy `Recreate`, and the pre-install/pre-upgrade hook Job `server-migrations` runs the migrations. Server pods carry the label `app.kubernetes.io/instance=server`. The CNPG Cluster is `coldframe-db`.
- Stale references to update: `deploy/README.md:11`, `deploy/charts/dependencies.env:3,40`, `deploy/charts/README.md:213-214`, `deploy/fleet/gitrepo.yaml:18`, `docs/operations/install.md:37-38`, `docs/operations/restore.md:200`. Each says "Story 2.5b" or "will". Point them at `deploy/fleet/smoke.sh`.
- Local tools are in the scratchpad `bin/`: fleet v0.16.2 (sha-verified), k3d, kubeconform, shellcheck and actionlint. `kind`, `helm`, `kubectl` and `podman` are on PATH. The Podman images `docker.io/coldframe/{server,web,migrations,keycloak}:dev` exist and can be rebuilt with the `COMPONENTS` Dockerfiles in `images-verify.yml`. Previous sessions ran the local smoke with `SMOKE_CLUSTER=kind KIND_EXPERIMENTAL_PROVIDER=podman DOCKER_HOST=unix:///run/user/1000/podman/podman.sock`.

## Tasks & Acceptance

**Execution:**
- `deploy/fleet/smoke.sh`: new and executable. Adapt the backup draft to the Always rules:
  - Add the keycloak marker to seed and to `check_data`.
  - Take the CRD from its new path.
  - Print `ok: Fleet smoke passed in <m>m<s>s`.
  - Keep the header comment accurate.
- `deploy/charts/testdata/helmchartconfig-crd.yaml`: the stand-in CRD, with a comment saying it is used only on kind.
- `.github/workflows/images-verify.yml`:
  - Install the fleet CLI in the helm/k3d step.
  - Add the "Fleet smoke" step after the chart smoke.
  - Raise `timeout-minutes` enough for both smokes.
  - Update the header comment.
- `deploy/README.md`, `deploy/charts/README.md`, `deploy/charts/dependencies.env`, `deploy/fleet/gitrepo.yaml`, `docs/operations/install.md`, `docs/operations/restore.md`: change the Story 2.5b wording to point at the smoke.
- If the local run needs a bundle fix, apply the Never exception.

**Acceptance Criteria:**
- Given the four images, when `SMOKE_CLUSTER=kind deploy/fleet/smoke.sh …` runs locally, then it ends with `ok: Fleet smoke passed`. The output includes the order listing, the upgrade timeline assertions and all three restart checks.
- Given a forced failure (for example `SMOKE_TIMEOUT=1`), when the smoke runs, then it prints the bundle, BundleDeployment and pod diagnostics, exits non-zero and deletes the cluster.
- Given the repo, when `deploy/charts/test.sh`, `shellcheck -x deploy/fleet/smoke.sh` and `actionlint` run, then all pass with 0 failed.

## Spec Change Log

## Review Triage Log

### 2026-09-29 — Review pass
- verdicts: 29 findings — high 0, medium 5, low 17, false 3, maybe-false 4
- findings:
  - `[low]` `[patch]` Blind: the ERR trap is not inherited by functions, so a failure inside one gets a generic FAIL line. Fixed with `set -Eeuo pipefail`.
  - `[low]` `[reject]` Blind: the domain-table digest compares empty tables. It is true but accepted by the Design Notes. The committed markers carry the proof, and seeding domain rows through SQL would bypass the Server and risk the projections.
  - `[medium]` `[patch]` Blind: the markers predate the restarts by minutes, so the loss of recent commits would go unseen. Fixed: `commit_marker` commits a `before-<restart>` marker in coldframe and keycloak before each restart, and the check after that restart requires it.
  - `[low]` `[reject]` Blind: release_order passes a dependent released in the same second. If Fleet ignored dependsOn, the 18 edges would almost surely show a strict inversion, and the same-second allowance is deliberate (Design Notes).
  - `[low]` `[patch]` Blind: the upgrade did not require the migration Job to have no failed attempt. Fixed: `.status.failed // 0 == 0` is now asserted, and a failure prints the timeline.
  - `[low]` `[patch]` Blind: nothing checked the HelmChartConfig that the rke2-traefik bundle delivers. Fixed: `valuesContent` must equal the repo file's values, and the check passed in the local run.
  - `[low]` `[reject]` Blind: SMOKE_TIMEOUT is not validated. This is a misuse only a developer can hit, and the fix is an extra guard.
  - `[maybe-false]` `[reject]` Blind: the Fleet smoke step has no step timeout, so cumulative waits could exceed the job budget before diagnostics print. Each stuck wait fails on its own bound (900 to 1800 s) and prints diagnostics. Only a chain of near-timeouts that each succeed could add up, and even then it would be low.
  - `[low]` `[patch]` Blind: the diagnostics omit Traefik's logs. Fixed: kube-system/daemonset/rke2-traefik was added to the log loop. The curl and port-forward errors are already in the FAIL messages.
  - `[medium]` `[patch]` Blind: the Server-restart loop never fails when there is no new pod, and a missing pod after the upgrade gives a cryptic error. Fixed: an explicit FAIL for each case.
  - `[low]` `[patch]` Blind: the comma-joined `server_pod_before` makes the "not replaced" check vacuous when there are two pods. Fixed: the check now tests list membership.
  - `[low]` `[reject]` Blind: there are no local-run docs for the Fleet smoke. The script header documents usage, tools, SMOKE_CLUSTER and SMOKE_TIMEOUT, and `deploy/charts/README.md` points at it.
  - `[low]` `[reject]` Blind: the negative tests call the real smoke.sh, and the no-fleet branch is untested. A regressed guard fails test.sh loudly, because the expected text is missing and the Charts job has no k3d.
  - `[low]` `[patch]` Edge: an invalid SMOKE_CLUSTER exits before the EXIT trap and leaks the temp dir. Fixed: the validation was moved above mktemp.
  - `[low]` `[reject]` Edge: SMOKE_TIMEOUT is non-numeric. This duplicates the Blind finding; same reason.
  - `[medium]` `[patch]` Edge: the Server-restart loop has no failure branch. Grouped with Blind #10; fixed there.
  - `[low]` `[reject]` Edge: a transient kubectl error inside the check_stack probe ends the wait early. The final real check_stack still fails loudly. It needs an API blip at that exact call, and the fix is extra guards.
  - `[false]` `[reject]` Edge: the mutable tables (projection_checkpoints, journal_outbox) cause false digest failures. In four full runs the digest was unchanged across the upgrade and all restarts.
  - `[low]` `[reject]` Edge: cleanup removes pre-existing `localhost/coldframe/*` tags. This is the same pattern as the chart smoke, and nobody keeps such tags.
  - `[low]` `[patch]` Edge: `server_pod_before` is a joined list. Grouped with Blind #11; fixed there.
  - `[medium]` `[patch]` Verification gap: the stop-then-start check passes when every poll was unreadable. Fixed: only numeric samples count, it fails when there are none, and the count is reported.
  - `[medium]` `[patch]` Verification gap: the Server restart is not proven to replace the pod. Grouped with Blind #10; fixed there.
  - `[maybe-false]` `[defer]` Verification gap (other): the k3d path is not proven, because CI runs k3d but local runs used kind. Deferred as medium (unverified). A green Images run settles it, and it is an operator action.
  - `[maybe-false]` `[reject]` Intent: the CI run on k3d is not proven and the bookkeeping shows in-review. The fix is this spec's frontmatter: finalized as awaiting-operator, with the CI run as an operator action.
  - `[maybe-false]` `[defer]` Intent: kind and k3d cover different rke2-traefik paths. Grouped with verification gap (other); deferred.
  - `[false]` `[reject]` Intent: the Git pull path is not exercised. The epic AC itself says the bundles are applied with `fleet apply` from the reference GitRepo's paths.
  - `[low]` `[reject]` Intent: durability is proven at the database, not at the app level. This is the same as Blind #2. The CI stack has no users to create Sites, and the manual checklist covers them.
  - `[low]` `[reject]` Intent: the order is "no earlier than". This is the same as Blind #4.
  - `[false]` `[reject]` Intent: epic-2-context.md is modified outside the patch. It is the context recompiled in step 1 because epics.md was newer, and it is committed with this story.

## Design Notes

**Why the release order comes from Helm release Secrets:** Fleet exposes no install timestamps. Each first-revision Helm Secret (`owner=helm,version=1`) is created when that release is installed. A dependent released in the same whole second as its dependency passes: a bundle can count as Ready right after its install.

**Why the durability proof is at the database:** the CI stack has no users, so no Site can be created through the API. Committed marker rows, plus a digest of every domain table, prove that nothing committed is lost. The manual checklist in `install.md` covers real Sites.

**Why a CI green run is still owed:** locally the smoke runs on kind. The Images job runs it on k3d, and only a GitHub run proves it fits the job timeout there.

## Verification

**Commands:**
- `PATH=$SCRATCH/bin:$PATH HELM_PLUGINS=$SCRATCH/plugins deploy/charts/test.sh` -- expected: `0 failed`
- `shellcheck -x deploy/fleet/smoke.sh && actionlint` -- expected: clean
- `PATH=$SCRATCH/bin:$PATH SMOKE_CLUSTER=kind KIND_EXPERIMENTAL_PROVIDER=podman DOCKER_HOST=unix:///run/user/1000/podman/podman.sock deploy/fleet/smoke.sh coldframe/server:dev coldframe/web:dev coldframe/migrations:dev coldframe/keycloak:dev` -- expected: `ok: Fleet smoke passed`

## Auto Run Result

Status: awaiting-operator

**Summary:** `deploy/fleet/smoke.sh` installs Coldframe through Fleet v0.16.2 on a throwaway single-node cluster (k3d in CI, kind locally).
- **Install:** it runs `fleet apply` on the reference GitRepo's paths. All 11 bundles become Ready, and all 18 dependsOn edges hold, judged by the creation order of the Helm releases. Every pod becomes Ready, HTTPS answers on the three hosts, cert-manager runs with its DNS-01 flags, and the rke2-traefik bundle delivers the repo's HelmChartConfig.
- **Upgrade:** a chart and image bump to `<v>-smoke.1` runs the migration Job first, with no failed attempt, before a new Server pod exists. A poller never sees two Server pods.
- **Restarts:** the Server pod, the database primary and the node are restarted in turn. After each one, the committed markers survive: the seed markers plus one committed right before that restart, in the coldframe and keycloak databases. The domain-table digest is unchanged, and the Server answers Healthy over HTTPS.
- **Failure output:** on failure it prints the bundles, BundleDeployments, pods and Jobs, events, CNPG and cert-manager objects, and pod, Fleet, operator and Traefik logs. It always deletes the cluster.
- **CI:** the Images CI job runs it after the chart smoke. The job timeout is raised from 110 to 170 minutes.

**Files changed:**
- `deploy/fleet/smoke.sh`: the new Fleet smoke.
- `deploy/charts/testdata/helmchartconfig-crd.yaml`: the stand-in HelmChartConfig CRD for kind.
- `.github/workflows/images-verify.yml`: sha-checked fleet CLI install, the Fleet smoke step, and the timeout of 170.
- `deploy/charts/test.sh`: three bad-usage checks for the Fleet smoke (image count, SMOKE_CLUSTER, fleet CLI version).
- `deploy/README.md`, `deploy/charts/README.md`, `deploy/charts/dependencies.env`, `deploy/fleet/gitrepo.yaml`, `docs/operations/install.md`, `docs/operations/restore.md`: the "Story 2.5b" references now point at the smoke.
- `_bmad-output/implementation-artifacts/epic-2-context.md`: recompiled, because epics.md was newer.

**Review:** 29 findings: medium 5, low 17, false 3, maybe-false 4.
- **Patched:** 9 entries: 3 medium (a marker before each restart, a Server restart or upgrade that yields no new pod now fails, the poller needs numeric samples) and 6 low (ERR trap under `-E`, migration Job `failed == 0`, the HelmChartConfig content check, Traefik logs, the membership test for the "not replaced" check, SMOKE_CLUSTER validated before mktemp).
- **Deferred:** 1, the unproven k3d/CI path (medium, unverified).
- **Rejected:** the rest, with their reasons in the triage log.

**Follow-up review:** recommended, because 3 medium patches were applied. The specific unverified risk: the patched checks (`before-<restart>` markers, `failed == 0`, the HelmChartConfig comparison, the numeric-sample poller) have passed only one local kind run and have never run on k3d.

**Verification:**
- `deploy/charts/test.sh`: 163 passed, 0 failed.
- `shellcheck -x deploy/fleet/smoke.sh deploy/charts/test.sh` and `actionlint`: clean.
- `SMOKE_CLUSTER=kind` (Podman) full run after the patches: `ok: Fleet smoke passed in 10m24s`. There were also two earlier passing runs by the implementer and one by me before the review.
- The implementer forced a failure with `SMOKE_TIMEOUT=1`: the diagnostics printed, the exit was 1, and the cluster was deleted.

**Residual risks:**
- It has never run on k3d or in GitHub Actions, so the 170-minute budget is not measured there. This is the operator action.
- The domain tables are empty in CI, so their digest is a weak signal; the markers carry the durability proof.
- The deferred items from 2.5a are untouched: takeOwnership adoption, restore under a paused GitRepo, and the keycloak→temporal edge.

