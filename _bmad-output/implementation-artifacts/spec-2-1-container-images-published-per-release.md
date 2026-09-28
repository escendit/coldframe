---
title: 'Container images published per release'
type: 'feature'
created: '2026-09-28'
status: 'awaiting-operator'
baseline_revision: '06fde5e32882062054769b894db37b16e3925980'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-2-context.md'
warnings: [oversized]
deferred: []
operator_actions:
  - 'After the first release, make the GHCR packages ghcr.io/escendit/coldframe/server, web and migrations public (Package settings > Change visibility).'
  - 'Add the CI job "Images / Build, structure-test and smoke" as a required status check on main.'
  - 'Protect release tags: add a tag ruleset on the repository restricting creation of v* tags to maintainers, and push release tags only from commits on main.'
  - 'Push the first release tag (git tag vX.Y.Z && git push origin vX.Y.Z) and confirm the Release run pushes amd64 and arm64 manifest lists.'
---

<intent-contract>

## Intent

**Problem:** Nothing server-side ships as an image yet, so an adopter's cluster cannot pull a chosen Coldframe release (Story 2.1, AD-23). The web BFF also has no health endpoint a kubelet could probe.

**Approach:** Add one Dockerfile per component (`server`, `web`, `migrations`), a reusable verify workflow (container-structure tests plus a container smoke run that hits the health endpoints) called by CI on every pull request, and a release workflow that on tag `vX.Y.Z` verifies and pushes multi-arch images to `ghcr.io/escendit/coldframe/<component>:X.Y.Z`. It also reports any architecture a component's base image lacks.

## Boundaries & Constraints

**Always:**
- Build context is the repo root. Every Dockerfile is multi-stage. Build stages use `FROM --platform=$BUILDPLATFORM`. Final stages only `COPY`, set `ENV`/`EXPOSE`/`USER`/`ENTRYPOINT` and never `RUN`, so arm64 builds need no QEMU.
- Final stages run as a numeric non-root UID: `1654` (.NET `app`) or `1000` (node `node`). Kubernetes `runAsNonRoot` must be able to verify it.
- Base images use explicit versions: `mcr.microsoft.com/dotnet/sdk:10.0.103` (the 1xx band that global.json requires), `mcr.microsoft.com/dotnet/aspnet:10.0.12`, `mcr.microsoft.com/dotnet/runtime:10.0.12`, `node:24.21.0-trixie-slim`. Actions are pinned to a commit SHA with a `# vX.Y.Z` comment, as in `ci.yml`. container-structure-test is pinned to v1.22.1, and its download is checked against a sha256.
- .NET: `dotnet restore --locked-mode`, then a portable (no `-r`) framework-dependent `dotnet publish -c Release`, with `ENTRYPOINT ["dotnet","<Assembly>.dll"]`. A RID-specific restore would break the committed lock files.
- web: pnpm `12.6.0`, `pnpm install --frozen-lockfile`, `pnpm --filter @coldframe/web build`, then a production-only deploy of `@coldframe/web`. That deploy's `node_modules` is resolved for the target architecture (pnpm `supportedArchitectures` cpu derived from `TARGETARCH`). `ENTRYPOINT ["node","build"]`.
- Configuration comes only from environment variables. Nothing environment-specific is baked in: no connection strings, URLs or secrets. Defaults: server `ASPNETCORE_HTTP_PORTS=8080` with `EXPOSE 8080 11111 30000`; web `PORT=3000`, `NODE_ENV=production`, `EXPOSE 3000`.
- Health paths match the Server's existing Escendit defaults: `/.well-known/healthz/live` and `/.well-known/healthz/ready`.
- The release pushes only the exact version tag `X.Y.Z` (never `latest`). It adds OCI labels `org.opencontainers.image.{source,version,revision,licenses}`, where revision is the full commit SHA. Workflow permissions stay least-privilege: `packages: write` only on the push job.

**Never:**
- No Helm charts, Fleet bundles, k3d or `deploy/SECRETS.md` (Stories 2.2–2.5).
- No change to Server or migration runtime behaviour to make a smoke run pass. No DDL at Server startup.
- No secret, token or credential in any committed file. Registry auth uses only `GITHUB_TOKEN`.
- No `latest` or floating tags, and no `RUN` in a final stage.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Release | tag `v1.2.3` pushed | verify passes. Then `server`, `web` and `migrations` are pushed as `:1.2.3` manifest lists for linux/amd64 and linux/arm64, and the step summary lists the platforms per component | — |
| Pre-release | tag `v1.2.3-rc.1` | pushed as `:1.2.3-rc.1` | — |
| Bad tag | tag `v1.2` or `vfoo` | no image pushed | version job fails, naming the tag |
| Missing arch | final base image lacks arm64 | component pushed for the platforms it has | `::warning::` plus a summary row naming the component and the missing arch; the run still succeeds |
| web live | GET `/.well-known/healthz/live` | 200 `{"status":"Healthy"}`, `cache-control: no-store`, no auth, no OIDC discovery | — |
| web ready | GET `/.well-known/healthz/ready` | 200 once `init` has loaded the config; 503 `{"status":"Unhealthy"}` before that | — |
| migrations smoke | container with a valid `ConnectionStrings__coldframe` | exit 0 | smoke fails on a non-zero exit |
| migrations misconfig | no connection string | exit 1 (existing Program.cs behaviour) | smoke asserts exit 1 |

</intent-contract>

## Code Map

- `apps/cs/server/Program.cs` -- `UseHealthCheckDefaults()` (Escendit.AspNetCore.Diagnostics.HealthChecks) serves `/.well-known/healthz`, `/live`, `/ready` and `/startup`. `SiloHealthCheck` is tagged `ready`. Read-only.
- `apps/cs/server/Hosting/SiloExtensions.cs` -- the silo port and gateway port come from `Orleans__Endpoints__SiloPort` and `Orleans__Endpoints__GatewayPort` (defaults 11111 and 30000). NATS is read from `ConnectionStrings__nats`, or disabled with `Journal__HintStream__Enabled=false`.
- `aspire/Coldframe.AppHost/AppHost.cs` -- the authoritative list of the Server's environment variables (`ConnectionStrings__coldframe`, `Identity__*`, `Keycloak__*`, `KeycloakEvents__*`) and of the dependency images: `postgres:18.6`, `nats:2.15.0 -js`, `docker.io/temporalio/temporal:1.8.3 server start-dev --namespace coldframe`, and `aspire/keycloak/Dockerfile`. The smoke script mirrors these.
- `apps/cs/migrations/Program.cs` -- reads `ConnectionStrings__coldframe`, exits 0 or 1, has no HTTP.
- `apps/ts/web/src/hooks.server.ts` -- `init` loads the config. `handle = sequence(auth, themeHandle)`: the health handle must come first.
- `apps/ts/web/src/lib/server/config.ts` / `runtime.ts` -- the env variables the web app requires: `COLDFRAME_SERVER_URL`, `KEYCLOAK_ISSUER`, `KEYCLOAK_CLIENT_ID`, `KEYCLOAK_CLIENT_SECRET`, and `setConfig`.
- `tests/ts/web/*.test.ts` -- vitest style to follow for the new health test.
- `.github/workflows/ci.yml` -- job conventions: pinned actions, `persist-credentials: false`, `timeout-minutes`, and the header comment style.
- `deploy/README.md` -- the table that says what arrives in Epic 2.

## Tasks & Acceptance

**Execution:**
- `.dockerignore` -- exclude `.git`, `**/bin`, `**/obj`, `**/node_modules`, `**/.svelte-kit`, `apps/ts/web/build`, `target`, `build`, `.gradle`, `.kotlin`, `_bmad*`, `.claude`, `.idea` and `*.env`. This keeps the context small and keeps host artifacts out.
- `apps/cs/server/Dockerfile`, `apps/cs/migrations/Dockerfile`, `apps/ts/web/Dockerfile` -- one per component, following the rules in Always.
- `apps/ts/web/src/lib/server/health.ts` + `hooks.server.ts` -- add `healthHandle`, which answers the two paths per the matrix and passes everything else through. Make it first in `sequence`.
- `tests/ts/web/health.test.ts` -- cover live 200, ready 200/503, the headers, and pass-through for other paths.
- `deploy/images/<component>.cst.yaml` (×3) -- a container-structure-test `metadataTest` for the entrypoint, the numeric user, the exposed ports and the port env. Add `fileExistenceTests` for the entry dll or `build/index.js`.
- `deploy/images/platforms.sh <dockerfile> [manifest.json]` -- read the final stage's `FROM` image and inspect its manifest list (`docker buildx imagetools inspect --raw`), or use the given manifest file (the test seam). Print the intersection with `linux/amd64,linux/arm64`. Print one `missing:<platform>` line per absent platform on stderr, and exit non-zero if the intersection is empty.
- `deploy/images/release-version.sh <tag>` -- validate the tag against the SemVer regex below. Print `X.Y.Z[-pre]` or exit non-zero, naming the tag.
- `deploy/images/test.sh` -- plain-bash tests with fixtures under `deploy/images/testdata/`. Cover the valid, pre-release and bad tags (`v1.2`, `vfoo`, `1.2.3`). Cover `platforms.sh` on a multi-arch manifest, an amd64-only manifest (missing arm64 is reported) and a manifest with no matching platform (fails). The verify workflow runs it.
- `deploy/images/smoke.sh` -- takes the three local image refs. Create a Docker network and start PostgreSQL, NATS and the Temporal dev server. Run migrations and expect exit 0, then run it without a connection string and expect exit 1. Start the server and poll live/ready until 200, within 180 s. Start web (issuer pointing at an unreachable host) and poll live/ready until 200. Start the `aspire/keycloak` image only if the Server cannot become ready without it. Print container logs on failure. Always clean up (`trap`).
- `.github/workflows/images-verify.yml` -- a reusable workflow (`workflow_call`) with a matrix-free job. It installs CST (pinned and sha256-checked), sets up a buildx `docker-container` builder, and builds each component for the platforms `platforms.sh` returns (cache-only). It also builds linux/amd64 with `--load`, runs `container-structure-test test` for each component, then runs `smoke.sh`.
- `.github/workflows/ci.yml` -- add an `images` job: `uses: ./.github/workflows/images-verify.yml`. It runs on every pull request, so any Dockerfile change is covered.
- `.github/workflows/release.yml` -- on push of tags `v*`. The `version` job runs `release-version.sh`, which validates the tag against the SemVer regex `^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z.-]+)?$` and outputs `X.Y.Z[-pre]`. The `verify` job calls `images-verify.yml`. The `publish` matrix job (server/web/migrations; `packages: write`) logs in to ghcr, resolves platforms, runs `buildx build --push` with the version tag and the OCI labels, and writes the platform and missing-architecture summary.
- `deploy/images/README.md` + `deploy/README.md` -- document the components, image names and tags, ports, health paths, UIDs, the required env per component, and how to run the CST and smoke checks locally. Update the Epic 2 table row.

**Acceptance Criteria:**
- Given a release tag `vX.Y.Z`, when `release.yml` runs, then its publish step pushes `ghcr.io/escendit/coldframe/{server,web,migrations}:X.Y.Z` as amd64 and arm64 manifest lists, only after verify passed.
- Given any of the three images built locally, when `container-structure-test` runs with its `deploy/images/*.cst.yaml`, then the entrypoint, the non-root numeric user and the ports pass.
- Given the three images built locally, when `deploy/images/smoke.sh` runs, then server and web answer 200 on `/.well-known/healthz/live` and `/ready`, and migrations exits 0.
- Given a pull request, when CI runs, then the `images` job builds every Dockerfile and fails if a structure test or the smoke run fails.

## Spec Change Log

- 2026-09-28 (implementation): the migrations image uses `mcr.microsoft.com/dotnet/aspnet:10.0.12`, not `runtime:10.0.12`. The job's runtimeconfig requires the `Microsoft.AspNetCore.App` shared framework (pulled in by Escendit.Extensions.Hosting.ServiceDefaults); on the runtime image the container exits 150 with "No frameworks were found", so the smoke run's exit-0 and exit-1 assertions cannot hold.

## Review Triage Log

### 2026-09-28 — Review pass
- verdicts: 33 findings — high 0, medium 0, low 24, false 9, maybe-false 0
- findings:
  - `[low]` `[reject]` Blind: publish rebuilds instead of pushing the verified bits — same commit, same pinned bases, built minutes apart; only a base re-tag in that window differs. Fixing it means restructuring the release around staging and promoting by digest, which is not worth it for that risk.
  - `[low]` `[reject]` Blind: a matrix failure can half-publish a release — a push failing after verify passed is rare (a registry hiccup), and "re-run failed jobs" re-pushes the exact tag. Staged promotion adds significant complexity.
  - `[low]` `[patch]` Blind: any `v*` tag on any commit publishes — deploy/images/README.md now says to push release tags only from main and to protect `v*` with a tag ruleset; also added to operator_actions.
  - `[low]` `[reject]` Blind: base images pinned by tag, not digest; no SBOM or signing — the repo convention (ci.yml) is explicit version tags, and the story does not ask for SBOM or signing. The fix is new infrastructure.
  - `[false]` `[reject]` Blind: arm64 images are never run, so a wrong-arch native module would slip through — I built all three arm64 images locally and ran smoke.sh against them under emulation. Migrations exit 0/1, and server and web both answer 200 on live and ready. The server image has runtimes/linux-arm64 (Temporal bridge). The web runtime loads no native module.
  - `[low]` `[reject]` Blind: web `COPY . .` busts the layer cache, and the printf append to pnpm-workspace.yaml could duplicate a key — the cache cost is performance only. A duplicate key fails loudly with a YAML error.
  - `[false]` `[reject]` Blind: web readiness is effectively always true — the matrix defines ready as "config loaded". init runs before listening, and a bad config crashes the process, which is the intended loud failure. No wrong 200 is served.
  - `[low]` `[patch]` Blind: release-version.sh accepts invalid SemVer pre-releases (`-01`, `rc..1`, `-.`) — the regex now enforces SemVer 2.0.0 identifiers, with 5 new test.sh cases.
  - `[low]` `[reject]` Blind: CST does not assert that no env files are baked in — no appsettings.Development.json or .env exists in any publish output, .dockerignore excludes them, and final stages copy only publish outputs.
  - `[low]` `[patch]` Blind: .dockerignore misses `.env.*`, and build/target/.gradle match only at the root — added `**/.env*`, `**/build`, `**/target`, `**/.gradle`. No tracked file is excluded.
  - `[low]` `[patch]` Blind: the component list is duplicated, and the structure-test loop is hardcoded — the loop now iterates `${COMPONENTS}`. The release matrix stays a separate, explicit list.
  - `[low]` `[reject]` Blind: the images job runs on every PR with no paths filter or cache — the spec chose this so any Dockerfile change is covered and the check can be required. The cost is CI time only.
  - `[false]` `[reject]` Blind: health.test leaks setConfig, and nothing tests that the health handle comes first in `sequence` — vitest isolates module state per file, and the leak is the file's last test. The ordering is enforced by smoke.sh: with auth first, `/live` would redirect (303) and the smoke run fails.
  - `[false]` `[reject]` Blind: platforms.sh mis-parses `FROM <stage>` and backslash-continued FROM lines — no Dockerfile uses either, and both fail loudly (exit 2, or an inspect error), never silently.
  - `[low]` `[reject]` Edge: publish rebuilds instead of pushing the verified artifact — duplicate of the first Blind row, same reason.
  - `[low]` `[reject]` Edge: fail-fast false can half-publish — duplicate of the second Blind row, same reason.
  - `[low]` `[reject]` Edge: re-running a release overwrites an existing X.Y.Z — re-running is the recovery path for a half-published release. Tag protection (operator action) governs who can trigger it.
  - `[low]` `[patch]` Edge: platforms.sh stderr is hidden in missing.txt on failure — release.yml now prints the captured stderr and an `::error::` before exiting 1.
  - `[low]` `[patch]` Edge: SemVer pre-release is too loose; a tag over 128 characters fails only at push — the pre-release part is fixed with the SemVer row above. The length case is unrealistic for a release tag, so it is rejected.
  - `[false]` `[reject]` Edge: final FROM naming a stage alias or scratch — same as the Blind platforms.sh row: not present, and it fails loudly.
  - `[false]` `[reject]` Edge: a backslash-continued FROM — same: not present, and it fails loudly with exit 2.
  - `[low]` `[reject]` Edge: smoke host_url yields an empty port when the container exited early — the run still fails, with every container's logs, after SMOKE_TIMEOUT. It is slower, not wrong.
  - `[low]` `[reject]` Edge: a crash during wait_until is reported only after the timeout — the same slow-but-correct failure, with the logs printed.
  - `[low]` `[reject]` Edge: a hung migration job bypasses SMOKE_TIMEOUT — it is bounded by the job's timeout-minutes, and a hang is not a realistic path for an exit-on-completion job.
  - `[low]` `[reject]` Edge: exit 1 for any reason passes the misconfiguration check — without a connection string, Program.cs returns 1 before any I/O. An unhandled .NET crash exits 134, not 1.
  - `[low]` `[patch]` Edge: test.sh's `""` stderr expectation matches anything — `""` now requires empty stderr.
  - `[false]` `[reject]` Edge: the numeric-USER check matches any stage — the CST metadataTest asserts the final image's user (1654/1000), so a root final stage fails CI.
  - `[false]` `[reject]` Edge: the health path with a trailing slash falls through to auth — probes use the exact configured path (the charts in 2.2), and no caller sends a trailing slash.
  - `[low]` `[reject]` Edge (claim): "pushed only after verify passed" does not mean the pushed bits are the verified ones — same as the first Blind row. The ordering claim holds through `needs:`.
  - `[low]` `[patch]` Verification gap: platforms.sh's choice of the final-stage image is never asserted — the no-match case now expects `example.org/final:2.0 provides none` on stderr.
  - `[low]` `[patch]` Verification other: release.yml hides platforms.sh stderr — same fix as the Edge stderr row.
  - `[low]` `[reject]` Verification other: the publish job's pushed images and labels are never inspected — same as the first Blind row. The step summary reads back the pushed manifest's platforms.
  - `[false]` `[reject]` Intent audit: nothing is committed or finalized, and pushing a tag could be agent work — finalization (commit, awaiting-operator, Auto Run Result) happens after review. The first release tag is a release decision, so it stays an operator action.

## Design Notes

- The migration image is a Kubernetes Job, so it has no liveness or readiness endpoint. Its health is its exit code, which the smoke run asserts. The structure test still checks its entrypoint and user.
- CST cannot probe HTTP, so the "health endpoint" check in CI is the smoke run. It lives in the same verify workflow and blocks the same way.
- Some things only an operator can do: make the GHCR packages public, add `images` as a required status check, and push the first release tag. List them as `operator_actions`.

## Verification

**Commands:**
- `pnpm --filter @coldframe/web-tests test && pnpm -r lint && pnpm -r typecheck` -- expected: pass
- `docker buildx build -f apps/cs/server/Dockerfile -t coldframe/server:dev --load .` (likewise for web and migrations) -- expected: success
- `docker buildx build --platform linux/arm64 -f <each Dockerfile> .` -- expected: success, without QEMU
- `container-structure-test test --image coldframe/<c>:dev --config deploy/images/<c>.cst.yaml` -- expected: PASS (download CST v1.22.1 into the scratchpad)
- `deploy/images/smoke.sh coldframe/server:dev coldframe/web:dev coldframe/migrations:dev` -- expected: exit 0
- `deploy/images/test.sh` -- expected: all pass
- `bash -n deploy/images/*.sh && shellcheck deploy/images/*.sh` (if available); `actionlint` on the workflows (if available) -- expected: clean

## Auto Run Result

Status: awaiting-operator

**Summary:** Coldframe now has release-published container images for `server` (Orleans silo, Edge API and SignalR), `web` (the SvelteKit BFF) and `migrations` (the FluentMigrator job). Each is built from its own multi-stage Dockerfile. Build stages run on the build platform and final stages only copy, so arm64 needs no emulation. Every image runs as a numeric non-root user and takes its configuration only from environment variables. The web app gained `/.well-known/healthz/live` and `/ready`, answered before any auth handling. A reusable `images-verify.yml` builds every image for amd64 and arm64, runs container-structure-test and a smoke run against real PostgreSQL, NATS and Temporal. CI runs it as the `images` job on every pull request. `release.yml` validates a `vX.Y.Z[-pre]` tag, verifies, then pushes `ghcr.io/escendit/coldframe/<component>:X.Y.Z` with OCI labels and a platform or missing-architecture summary.

**Files changed:**
- `.dockerignore` -- keeps host outputs, env files and tooling dirs out of the build context.
- `apps/cs/server/Dockerfile`, `apps/cs/migrations/Dockerfile`, `apps/ts/web/Dockerfile` -- one image per component.
- `apps/ts/web/src/lib/server/health.ts`, `hooks.server.ts`, `runtime.ts` -- web liveness and readiness handle, placed first in the handle chain.
- `tests/ts/web/health.test.ts` -- unit tests for the health handle.
- `deploy/images/{server,web,migrations}.cst.yaml` -- structure tests: entrypoint, user, ports, env, files.
- `deploy/images/platforms.sh`, `release-version.sh`, `smoke.sh`, `test.sh`, `testdata/*` -- platform resolution, tag validation, the smoke run, and bash tests.
- `.github/workflows/images-verify.yml`, `release.yml`, `ci.yml` -- verification (PR plus release) and publishing.
- `deploy/images/README.md`, `deploy/README.md` -- the image contract and a local how-to.

**Review findings:** 33 findings. 9 patched (all low):
- SemVer pre-release strictness
- the empty-stderr expectation in test.sh
- the assertion that platforms.sh reads the final-stage image
- surfacing platforms.sh stderr in release.yml
- the structure-test loop reading COMPONENTS
- .dockerignore patterns
- the tag-protection note

0 deferred. 24 rejected, each with its reason in the Review Triage Log: 15 low not worth the added complexity, and 9 false.

**Deviation:** the migrations image uses `aspnet:10.0.12`, not `runtime:10.0.12`, because the job needs the Microsoft.AspNetCore.App framework (see Spec Change Log).

**Follow-up review recommended:** false. Patched: 0 high, 0 medium, 9 low.

**Verification performed (local; Docker CLI against rootless Podman):**
- `deploy/images/test.sh`: 29/29 pass.
- shellcheck 0.11.0 and actionlint 1.7.12: clean.
- Web unit tests: 313 pass. `pnpm -r lint` and `pnpm -r typecheck` pass.
- All three images build for linux/amd64 (`--load`) and linux/arm64.
- container-structure-test v1.22.1 passes for all three images.
- `smoke.sh` passes on the amd64 images. It also passed, under emulation, on the arm64 images.

**Residual risks:**
- `images-verify.yml` and `release.yml` have not yet run on GitHub. The first PR run and the first tag are their real test.
- Publishing rebuilds the images after verify rather than promoting the exact verified bits.
- A failed component push can leave a partial release; recover by re-running the failed jobs.

**Operator actions owed:** see the frontmatter `operator_actions`:
- make the GHCR packages public
- require the Images check
- add a `v*` tag ruleset
- push the first release tag
