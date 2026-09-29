---
title: 'Story 1.1: Monorepo scaffold, CI and local dev stack'
type: 'feature'
created: '2026-09-28'
status: 'awaiting-operator'
baseline_revision: '77130b78c31b270030021cbd8df011ec90efc21c'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md'
warnings:
  - oversized
deferred:
  - summary: 'The architecture Stack pins Temporal Server 1.31.3, but no public container image exists for that version; the local stack uses the Temporal CLI development server instead.'
    evidence: 'Docker Hub temporalio/server lists 1.31.0, 1.31.1, 1.31.2 and 1.32.0 only; temporalio/auto-setup stops at 1.29.7 (checked 2026-09-28).'
    location: '_bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md (Stack table)'
    severity: 'low'
  - summary: >-
      The Escendit hosting packages extend the concrete HostApplicationBuilder only, so an ASP.NET Core host cannot call AddServiceDefaults() or any Orleans extension of Escendit.Extensions.Hosting.Orleans; the server applies the defaults through a local shim and the Orleans package contributes only transitive dependencies, including Redis providers.
    evidence: |-
      The XML documentation of Escendit.Extensions.Hosting.ServiceDefaults 0.1.0-rc.4 lists AddServiceDefaults(HostApplicationBuilder, ...) and no overload for IHostApplicationBuilder or WebApplicationBuilder. Story 1.2 meets the same limit when it adds AdoNet clustering and NATS streams. Needs an upstream change (target IHostApplicationBuilder) or a decision to keep the shim.
    location: >-
      apps/cs/server/Hosting/ServiceDefaultsExtensions.cs
    severity: medium
  - summary: >-
      No test asserts that the server exports telemetry when OTEL_EXPORTER_OTLP_ENDPOINT is set.
    evidence: |-
      The integration run executes the branch but asserts nothing about telemetry. An assertion needs an OTLP collector in the test host.
    location: >-
      apps/cs/server/Hosting/ServiceDefaultsExtensions.cs
    severity: low
  - summary: >-
      The health test may fail on a slow runner when the server process is running but does not listen within the retry budget of the HTTP resilience handler.
    evidence: |-
      Not observed in any local run. To settle it, measure the time from the Running state to the first accepted connection on a GitHub runner and compare it with the retry budget of the standard resilience handler.
    location: >-
      tests/cs/server.integration/ServerHealthTests.cs
    severity: medium (unverified)
  - summary: >-
      The CI jobs are not required status checks on main, so a failing job does not block a merge.
    evidence: |-
      The GitHub API reports no branch protection on main and a ruleset with deletion and non_fast_forward only. This is a repository setting; it is listed under operator_actions.
    location: >-
      .github/workflows/ci.yml
    severity: medium
  - summary: >-
      The CI workflow has never run on GitHub; the macOS Swift job and the Docker-based .NET and secrets jobs are verified only by running their commands locally.
    evidence: |-
      This run may not push or open a pull request. It is listed under operator_actions.
    location: >-
      .github/workflows/ci.yml
    severity: medium
operator_actions:
  - 'Create a new branch from main that carries this story''s commit, because the pull request of chore/bmad-gzp-module-install is already merged; push it and open a pull request.'
  - 'Confirm on that pull request that the six CI jobs (.NET, Rust, Kotlin, TypeScript, Swift, Secrets) pass, including the Swift job on the macos-26 runner.'
  - 'In the GitHub repository settings, make .NET, Rust, Kotlin, TypeScript, Swift and Secrets required status checks on main.'
---

<intent-contract>

## Intent

**Problem:** The repository holds only planning documents and a throwaway Rust spike. There is no monorepo layout, no pinned .NET dependency set, no local stack, and no CI, so no later story has a place to land or a way to be built and tested.

**Approach:** Lay down the architecture spine's source tree, a .NET solution under Central Package Management, an Aspire AppHost that starts PostgreSQL, NATS JetStream, Temporal, Keycloak and the Orleans silo, one integration test that proves the silo is healthy, a walking-skeleton workspace per language, a GitHub Actions workflow that builds, tests and lints all five languages, and a developer quickstart.

## Boundaries & Constraints

**Always:**
- Test-first: the silo health integration test is written and seen failing before the silo exists.
- Top-level tree is exactly `apps/`, `packages/`, `tests/`, `aspire/`, `deploy/`, `hardware/`, `docs/`; `tests/` is split `cs/ rs/ kt/ swift/ ts/`. Tests live under `tests/<lang>/`; only Rust unit tests are inline.
- .NET 10, Central Package Management, transitive pinning on, committed lock files, CI restores in locked mode. Pins: Orleans 10.3.1, `Microsoft.Orleans.Streaming.NATS` 10.3.1-alpha.1, NATS.Net 2.8.2, Aspire 13.5.4, Escendit service defaults (`Escendit.Extensions.Hosting.*` 0.1.0-rc.4, `Escendit.AspNetCore.Builder.*` 0.1.0-rc.0).
- Keycloak is `quay.io/phasetwo/phasetwo-keycloak:26.6.7` with `keycloak-temporal-extensions` built from the public source at tag `v0.0.1-rc.2`. Every dependency comes from a public registry or repository without credentials.
- Every container image and CI action is pinned to an explicit version.
- Conventional Commits. No secret is committed; dev-only credentials are generated by Aspire or clearly marked local-only.

**Never:**
- No domain code: no grains beyond what a silo needs to start, no journal, no migrations, no DDL at startup (Story 1.2).
- No design tokens, sign-in, SvelteKit app, Android or iOS app (Stories 1.3–1.5). Language skeletons hold only a minimal module and one test.
- No Helm charts, Compose file, image publishing or release workflow (Epic 2, Epic 10).
- Do not move, rewrite or delete `apps/rs/spike-hub-radio` or anything under `_bmad-output/planning-artifacts`.
- Never write `sprint-status.yaml`. Never push or open a pull request.

</intent-contract>

## Code Map

- `apps/rs/spike-hub-radio/` -- existing throwaway spike; `xtensa-esp32s3-none-elf`, `esp` toolchain, no tests. Stays outside the host Cargo workspace (`exclude`).
- `.gitignore` -- VisualStudio template; ignores `**/[Bb]in/*`, `[Ll]og/`, `[Oo]ut/`. Extend for Rust `target/`, Node, Gradle, SwiftPM. Watch the `bin/` rule for any source folder named `bin`.
- `README.md` -- two lines; replace with a short project intro that links to the quickstart.
- `docs/spikes/hub-radio-coexistence.md` -- existing doc; leave as is.
- Local toolchain: dotnet 10.0.112, aspire CLI 13.5.4, podman 5.8.7 (socket up), cargo 1.97.1, node 24, java 25. Missing: gradle, swift, npm/pnpm (corepack available).
- Registry facts checked during planning:
  - `Escendit.Extensions.Hosting.{ServiceDefaults,Orleans}` 0.1.0-rc.4 and `Escendit.AspNetCore.Builder.{Core,Orleans}` 0.1.0-rc.0 are on nuget.org; the Orleans ones floor Orleans at 10.1.0 and pull Redis providers, so explicit pins are required.
  - `Microsoft.Orleans.Streaming.NATS` 10.3.1-alpha.1 floors `NATS.Net` at 2.7.2; NATS.Net 3.x exists and must not be resolved.
  - `Aspire.Hosting.Keycloak` exists only as preview; no first-party Aspire Temporal package.
  - `keycloak-temporal-extensions` releases carry no assets; the JAR is published only to GitHub Packages (needs a token), so it must be built from source. The JAR is not shaded: `temporal-sdk` runtime dependencies must be copied to `providers/` with it.
  - Docker Hub has no `temporalio/server:1.31.3`; `temporalio/auto-setup` stops at 1.29.7.

## Tasks & Acceptance

**Execution:**
- [x] `tests/cs/server.integration/` -- add xUnit project using `Aspire.Hosting.Testing` that starts the AppHost and asserts the silo health endpoint is healthy and that Keycloak lists the `temporal` event listener -- written first, seen failing
- [x] `global.json`, `nuget.config`, `Directory.Build.props`, `Directory.Packages.props`, `Coldframe.slnx` -- pin SDK, single public source, CPM with transitive pinning and lock files, warnings as errors -- AD-15 no-float rule
- [x] `apps/cs/server/` -- ASP.NET Core host with an Orleans silo and the Escendit service defaults, exposing health endpoints -- first runtime
- [x] `aspire/Coldframe.AppHost/` -- AppHost wiring PostgreSQL 18, NATS 2.15.0 with JetStream, Temporal, Keycloak and the server, with wait-for ordering -- local stack and test host
- [x] `aspire/keycloak/Dockerfile` -- multi-stage build: compile the extension at `v0.0.1-rc.2`, copy it and its runtime dependencies into the Phase Two image, run `kc.sh build` -- extension loaded
- [x] `Cargo.toml`, `packages/rs/hal/`, `tests/rs/hal/` -- host Cargo workspace with a minimal crate, one inline unit test and one integration test -- Rust skeleton
- [x] `package.json`, `pnpm-workspace.yaml`, `packages/ts/api-client/`, `tests/ts/api-client/` -- pnpm workspace, TypeScript, ESLint, Vitest -- TypeScript skeleton
- [x] `settings.gradle.kts`, `build.gradle.kts`, `gradle/`, `gradlew*`, `packages/kt/core/`, `tests/kt/core/` -- Gradle wrapper, Kotlin 2.4.20 module with its test source set under `tests/kt/core`, ktlint -- Kotlin skeleton
- [x] `Package.swift`, `apps/swift/ios/`, `tests/swift/ios/` -- root SwiftPM manifest with one UI-free library target under `apps/swift/ios`, its test target under `tests/swift/ios`, and `swift format lint --strict` -- Swift skeleton
- [x] `deploy/README.md`, `hardware/README.md`, `apps/*/`, `packages/*/` READMEs -- short placeholder explaining what lands there and in which epic -- tree is complete on a fresh clone
- [x] `.github/workflows/ci.yml` -- on pull request and push to `main`: jobs `dotnet`, `rust`, `kotlin`, `typescript`, `swift` (macOS runner), `secrets` (gitleaks) -- CI gate
- [x] `.gitignore`, `.editorconfig` -- extend for the new toolchains -- clean tree after builds
- [x] `docs/quickstart.md`, `README.md` -- prerequisites, how to run the AppHost with Docker or Podman, how to run each language's tests and lints -- developer quickstart

**Acceptance Criteria:**
- Given a fresh clone, when I list the top level, then `apps/`, `packages/`, `tests/`, `aspire/`, `deploy/`, `hardware/` and `docs/` exist and `tests/` contains `cs`, `rs`, `kt`, `swift` and `ts`.
- Given the .NET solution, when I run `dotnet restore --locked-mode`, then it succeeds, and the lock files resolve Orleans packages to 10.3.1, `Microsoft.Orleans.Streaming.NATS` to 10.3.1-alpha.1 and `NATS.Net` to 2.8.2, with no `PackageReference` carrying a version.
- Given Docker or Podman, when I run the AppHost, then PostgreSQL, NATS with JetStream enabled, Temporal, Keycloak and the server reach the running state, and the server's health endpoint returns healthy.
- Given the running stack, when I ask Keycloak for its server info, then the image is Phase Two 26.6.7 and the event listener provider `temporal` is listed.
- Given the integration test project, when `dotnet test` runs, then it starts the same AppHost and the silo health test passes; with the silo's health endpoint removed, the same test fails.
- Given a pull request, when the workflow runs, then each of the five language jobs builds, tests and lints its workspace, the Swift job runs on a macOS runner, and any failing test or lint error fails the job.
- Given a committed secret pattern, when the `secrets` job runs, then it fails.
- Given `docs/quickstart.md`, when a developer follows it on a machine with the listed prerequisites, then they can start the AppHost and run every test suite.

## Spec Change Log

## Review Triage Log

### 2026-09-28 — Review pass
- verdicts: 52 findings — high 0, medium 15, low 29, false 7, maybe-false 1
- findings:
  - `[low]` `[patch]` Blind: the local service defaults drop the HTTP resilience handler — added `AddStandardResilienceHandler()` to the HTTP client defaults.
  - `[medium]` `[defer]` Blind: `Escendit.Extensions.Hosting.Orleans` is referenced but its API cannot be called from this host — upstream package limit, confirmed in the package's XML documentation; deferred.
  - `[medium]` `[patch]` Blind: no test proves JetStream is enabled — added `NatsTests.JetStreamIsEnabled`; it fails with `.WithJetStream()` removed.
  - `[medium]` `[patch]` Blind: the Keycloak test proves registration, not function — added `AdminEventStartsAWorkflowInTheTemporalNamespace`.
  - `[medium]` `[patch]` Blind: unregistered Rust integration tests are skipped silently — `tests/rs/hal` is now a workspace member whose `tests/` folder Cargo discovers.
  - `[low]` `[patch]` Blind: the version test fails on a pre-release version — the test accepts a pre-release or build suffix; the comment on `VERSION` was reworded.
  - `[low]` `[reject]` Blind: Kotlin has no dependency locking — the no-float rule binds .NET; Gradle locking for a Multiplatform module is more than a direct correction and the skeleton has one dependency.
  - `[low]` `[patch]` Blind: the JDK is not pinned where the quickstart says — added `jvmToolchain(25)` and corrected the table.
  - `[low]` `[patch]` Blind: the quickstart says CI runs the same commands — CI runs `./gradlew check`; the sentence now names the added flags.
  - `[low]` `[reject]` Blind: images are pinned by tag, not digest — the constraint is an explicit version, which every image has; digests add upkeep without a named harm here.
  - `[low]` `[patch]` Blind: nothing keeps pins current, and a new advisory breaks the build — NU1901 to NU1904 stay warnings; an update bot is a new configuration surface and was not added.
  - `[medium]` `[defer]` Blind: the merge gate is claimed but not configured — repository setting; the workflow header was reworded and the setting is an operator action.
  - `[low]` `[reject]` Blind: the Keycloak image is rebuilt from the network on every CI run — a layer cache adds complexity; the wait order is the stack the story asks for.
  - `[low]` `[patch]` Blind: no `.gitattributes` — added with LF default, CRLF for `*.bat` and `*.cmd`, binary for `*.jar`.
  - `[low]` `[patch]` Blind: `aspire.config.json` has no final newline — added.
  - `[low]` `[patch]` Blind: the Cargo `exclude` names only the spike — now `apps/rs`.
  - `[low]` `[reject]` Blind: the gitleaks allowlist exempts whole files and scanning happens after push — both files are vendored tooling; narrowing needs per-rule fingerprints, and a pre-commit hook is a new surface.
  - `[low]` `[patch]` Blind: the local gitleaks command lacks the SELinux note — added.
  - `[low]` `[reject]` Blind: the Swift version is a floor and the Xcode path is hard-coded — a removed Xcode fails the job with a clear `xcode-select` error; a format configuration file is more than a direct correction.
  - `[false]` `[reject]` Blind: spec bookkeeping does not match its status — the diff was taken before finalization; its fix is an edit of this build's spec.
  - `[low]` `[reject]` Blind: the work sits on a branch whose pull request is merged — the orchestrator works in place on the branch it was started on; moving the commit is an operator action.
  - `[low]` `[patch]` Edge: `ServerHealthTests` blocks 15 minutes when the server fails to start — `WaitForRunningAsync` stops on terminal states.
  - `[low]` `[patch]` Edge: the Keycloak wait blocks 15 minutes on a failed start — `WaitForHealthyAsync` uses `StopOnResourceUnavailable`.
  - `[maybe-false]` `[defer]` Edge: the server may run but not listen within the retry budget — not observed locally; settled by timing the start on a GitHub runner. If true: medium.
  - `[low]` `[patch]` Edge: NuGet audit warnings become errors — same fix as the advisory finding above.
  - `[low]` `[reject]` Edge: silo and gateway ports are not validated — the AppHost allocates them; Orleans reports an invalid port itself, and a guard adds branches for a state that was not shown.
  - `[low]` `[reject]` Edge: the Xcode path may disappear from the runner — same reason as the Swift finding above.
  - `[low]` `[reject]` Edge: `pnpm -r` skips a package without a script — a check script is more than a direct correction; new packages copy the scripts of the skeleton.
  - `[low]` `[patch]` Edge: only `**/*.js` disables type-checked rules — now `**/*.{js,cjs,mjs}`, with CommonJS globals for `*.cjs`.
  - `[low]` `[patch]` Edge: the Cargo `exclude` names only the spike — same fix as above.
  - `[low]` `[reject]` Edge: the `build/` ignore rule hides a source folder named `build` — conventional rule for Gradle and SvelteKit output; no such source folder is planned.
  - `[low]` `[patch]` Edge: no `.gitattributes` — same fix as above.
  - `[low]` `[patch]` Edge: the gitleaks command fails with Podman and SELinux — same fix as above.
  - `[medium]` `[defer]` Gap: CI jobs are not required for merging into `main` — filed evidence from the GitHub API; operator action.
  - `[medium]` `[patch]` Gap: the `secrets` job is never shown to fail, and gitleaks passes when it scans nothing — added a self-test step and a commit-count check.
  - `[medium]` `[patch]` Gap: the Keycloak-to-Temporal listener settings are not observed — same test as the Keycloak finding above; it fails with the namespace variable renamed.
  - `[medium]` `[patch]` Gap: JetStream is not asserted — same test as above.
  - `[medium]` `[patch]` Gap: silo and gateway port wiring is not observed — added `SiloListensOnThePortTheAppHostAllocated`; it fails with the port variable renamed.
  - `[low]` `[defer]` Gap: the OTLP exporter branch has no assertion — needs a collector in the test host; deferred as filed.
  - `[low]` `[patch]` Gap (other): the quickstart gitleaks command fails with Podman and SELinux — same fix as above.
  - `[medium]` `[patch]` Gap (other): `autotests = false` hides unregistered test files — same fix as the Rust finding above.
  - `[medium]` `[defer]` Intent: the CI criterion lives on GitHub, the diff holds a static workflow file — this run may not push; operator action.
  - `[medium]` `[defer]` Intent: "fails the check and blocks merge" needs branch protection — same as the merge gate finding; operator action.
  - `[false]` `[reject]` Intent: the negative criteria have no standing test — they are one-time demonstrations; the implementer and the verification layer both saw the health test fail with the endpoint or the silo check removed.
  - `[medium]` `[defer]` Intent: the silo uses a local shim, not the package's `AddServiceDefaults()` — same as the Escendit package finding; the package API allows one reading only.
  - `[false]` `[reject]` Intent: the server receives PostgreSQL and NATS references but connects to neither — the cluster schema and streams belong to Story 1.2; the criterion asks that the resources start and the silo is healthy.
  - `[medium]` `[patch]` Intent: the Keycloak test checks registration, not delivery — same test as above.
  - `[false]` `[reject]` Intent: Temporal runs a development server, not Server 1.31.3 — no public image exists for 1.31.3; already recorded under `deferred` at planning.
  - `[low]` `[patch]` Intent: the quickstart and CI commands differ — same fix as above.
  - `[false]` `[reject]` Intent: commit, final status and Auto Run Result are absent from the diff — the diff was taken before finalization, which this section records.
  - `[false]` `[reject]` Intent: a top-level `gradle/` folder sits beside the seven named folders — the Gradle wrapper requires it at the root; the story's criterion says the tree contains the seven folders. Its fix would edit this build's spec.
  - `[false]` `[reject]` Intent: versions are verified by command, not by a test — the locked restore in CI is the standing check.

## Design Notes

- **Skeletons for all five languages.** The CI criterion names five languages while only C# and Rust exist. A job that skips when nothing is present would never have run before Stories 1.4 and 1.5 need it. Each language therefore gets the smallest real workspace at a path the source tree already names, so every job is proven now and later stories only add modules.
- **Temporal image.** The Stack row pins Temporal Server 1.31.3, which has no public image. The local stack uses the Temporal CLI development server image (`temporalio/temporal`, pinned tag), one container with no schema setup. The deployed Temporal version is Epic 2's decision. The gap is recorded under `deferred`.
- **Keycloak image is built, not pulled.** A Dockerfile build keeps the extension reproducible from public sources and makes the AppHost the single definition for both development and tests.
- **Silo clustering.** Localhost clustering until Story 1.2 adds the cluster schema; the server still receives the PostgreSQL and NATS references so the wiring is in place.
- **Branch.** The run works in place on `chore/bmad-gzp-module-install`, the branch the orchestrator was started on (`scm.isolation = "none"`). Its pull request is already merged, so the commits need a new branch or pull request before they can reach `main`.

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror` -- expected: 0 warnings, 0 errors
- `dotnet format --verify-no-changes` -- expected: exit 0
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test` -- expected: integration tests pass
- `cargo fmt --check && cargo clippy --workspace --all-targets -- -D warnings && cargo test --workspace` -- expected: exit 0
- `pnpm install --frozen-lockfile && pnpm -r lint && pnpm -r typecheck && pnpm -r test` -- expected: exit 0
- `./gradlew check` -- expected: BUILD SUCCESSFUL, ktlint included
- `swift build && swift test && swift format lint --strict -r .` (in a `swift` container on Linux) -- expected: exit 0

**Manual checks (if no CLI):**
- `.github/workflows/ci.yml` parses (`actionlint` if available) and every `uses:` is version-pinned.
- The workflow itself can only be observed on GitHub after a push; the macOS Swift job cannot be run locally.

## Auto Run Result

Status: awaiting-operator

**Summary.** The repository now has the monorepo layout, a .NET solution under Central Package Management with lock files, an Aspire AppHost that starts PostgreSQL, NATS with JetStream, Temporal, Keycloak and the Server, six integration tests on that AppHost, a walking skeleton for Rust, TypeScript, Kotlin and Swift, a GitHub Actions workflow with six jobs, and a developer quickstart. Everything an agent can do is done and committed. The workflow has never run on GitHub, and the merge gate is a repository setting; both are listed under `operator_actions`.

**Files changed.**
- `global.json`, `nuget.config`, `Directory.Build.props`, `Directory.Packages.props`, `Coldframe.slnx` -- .NET SDK pin, single public source, central versions, lock files, warnings as errors
- `apps/cs/server/` -- ASP.NET Core host with an Orleans silo, the Escendit defaults and a `silo` health check at `/.well-known/healthz`
- `aspire/Coldframe.AppHost/` -- the local stack and integration-test host
- `aspire/keycloak/Dockerfile` -- Phase Two Keycloak 26.6.7 with `keycloak-temporal-extensions` built from tag `v0.0.1-rc.2`
- `tests/cs/server.integration/` -- fixture and tests for server health, silo ports, Keycloak server info, Keycloak-to-Temporal delivery and JetStream
- `Cargo.toml`, `Cargo.lock`, `rust-toolchain.toml`, `packages/rs/hal/`, `tests/rs/hal/` -- Rust workspace, crate and test crate
- `package.json`, `pnpm-workspace.yaml`, `pnpm-lock.yaml`, `tsconfig.base.json`, `eslint.config.js`, `.node-version`, `packages/ts/api-client/`, `tests/ts/api-client/` -- TypeScript workspace
- `settings.gradle.kts`, `build.gradle.kts`, `gradle.properties`, `gradle/`, `gradlew`, `gradlew.bat`, `packages/kt/core/`, `tests/kt/core/` -- Kotlin workspace
- `Package.swift`, `apps/swift/ios/`, `tests/swift/ios/` -- Swift package
- `.github/workflows/ci.yml`, `.gitleaks.toml` -- CI jobs and secret scanning
- `.gitignore`, `.gitattributes`, `.editorconfig` -- ignore, line-ending and editor rules
- `docs/quickstart.md`, `README.md`, READMEs under `apps/`, `packages/`, `deploy/`, `hardware/` -- quickstart and folder descriptions
- `_bmad-output/implementation-artifacts/epic-1-context.md` -- compiled epic context

**Review findings.** 52 findings from four layers: high 0, medium 15, low 29, false 7, maybe-false 1.
- Patches applied: 17 changes covering 27 findings. Patched entries by verdict: medium 5 (JetStream test, Keycloak-to-Temporal test, silo port test, secrets self-test, Rust test discovery), low 12.
- Deferred: 8 findings, recorded as 5 items in `deferred` (Escendit package API, OTLP assertion, possible start-up race in the health test, merge gate, workflow never run on GitHub). One item was deferred at planning (Temporal version).
- Rejected: 17 findings. Each reason is in the Review Triage Log above.

**Follow-up review recommended: true.** Five medium entries were patched. The unverified risk is the patched CI workflow: the secrets self-test step, the commit-count check and the `macos-26` Swift job have run only as local commands, never on a GitHub runner. The fail-fast waits in `AppHostFixture` were not exercised against a resource that fails to start.

**Verification.** All run on this machine after the patches, from the repository root:
- `dotnet restore --locked-mode`, `dotnet build --no-restore -warnaserror` -- 0 warnings, 0 errors
- `dotnet format --verify-no-changes` -- exit 0
- `ASPIRE_CONTAINER_RUNTIME=podman dotnet test` -- 6 of 6 passed
- `cargo fmt --all --check`, `cargo clippy --workspace --all-targets --locked -- -D warnings`, `cargo test --workspace --locked` -- exit 0, 2 tests passed
- `pnpm install --frozen-lockfile`, `pnpm -r lint`, `pnpm -r typecheck`, `pnpm -r test` -- exit 0, 1 test passed
- `./gradlew check --rerun-tasks` -- BUILD SUCCESSFUL, `jvmTest` and ktlint ran
- `swift build`, `swift test`, `swift format lint --strict` in the `swift:6.3.3` container -- exit 0, 1 test passed
- Reported by the implementation subagent, not repeated by me: the tests failed before the server existed; removing `.WithJetStream()`, renaming the Temporal namespace variable and renaming the silo port variable each failed the matching test; `actionlint` found nothing; gitleaks found no leaks in 47 commits and exited 1 on a fake key.

**Residual risks.**
- The workflow is unproven on GitHub, see above.
- The commit is on `chore/bmad-gzp-module-install`, whose pull request is already merged. It needs a new branch before it can reach `main`.
- `keycloak-temporal-extensions` v0.0.1-rc.2 compiles against Keycloak 26.7.0 and runs in 26.6.7. It loads and delivers events in the tests, but the pairing must be checked again when either version changes.
- The local stack runs the Temporal CLI development server 1.8.3 (Server 1.31.2), not the Server 1.31.3 of the architecture.
- The AppHost build downloads `aspire.cli` 13.5.4 from nuget.org outside the lock files.
- One vendored file, `.claude/skills/bmad-brainstorming/assets/brain-methods.csv`, stays CRLF in the working copy so that its BMAD manifest hash still matches.
