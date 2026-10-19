---
title: 'Releases with one version and firmware binaries'
type: 'feature'
created: '2026-10-19'
status: 'awaiting-operator'
baseline_revision: 'bb7c4a5eafcee3619348fb90361f17857cfad84f'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-10-context.md'
warnings: [oversized]
deferred:
  - summary: >-
      A v* tag on any commit publishes; the documented "tag only from main" rule is not enforced by the workflow.
    evidence: |-
      Pre-existing since Story 2.1 (deploy/images/README.md already asks for a tag ruleset); the mode job accepts any v* push.
    location: >-
      .github/workflows/release.yml (mode job)
    severity: low
  - summary: >-
      The tag-only branches of release.yml (ghcr login, --push, imagetools summary, verify, gh release create, --prerelease) never run in the dry run.
    evidence: |-
      dry_run is true for every non-push event, so these steps are skipped on PRs; only a real v* tag exercises them. Owed as an operator action (cut the first release and check it).
    location: >-
      .github/workflows/release.yml (publish, release jobs)
    severity: medium
operator_actions:
  - "After this PR merges, cut the first release from main: keep the charts at 0.1.0 (or run deploy/charts/set-version.sh <X.Y.Z> in a chore(release) PR and merge it), then push the tag: git tag -a v0.1.0 -m 'Coldframe 0.1.0' && git push origin v0.1.0."
  - "Watch the Release workflow run for that tag and confirm it is green and that the GitHub Release v0.1.0 has coldframe-hub/node-0.1.0.bin and .elf, every <chart>-0.1.0.tgz, SHA256SUMS and the grouped notes with the docs/build/apps.md link."
  - "Confirm ghcr.io/escendit/coldframe/{server,web,migrations,keycloak}:0.1.0 exist, then download the Release assets and run sha256sum --check --strict SHA256SUMS."
---

<intent-contract>

## Intent

**Problem:** A `vX.Y.Z` tag publishes only the container images today. Nothing attaches Hub and Node firmware or the charts to a release, no release notes are written, nothing stops a wire-major bump the Server cannot take, and the release path has never run outside a real tag. An adopter cannot tell which pieces belong together.

**Approach:** Extend `.github/workflows/release.yml` so that one tag produces everything. That means the images (unchanged), Helm chart packages carrying `X.Y.Z`, release-build Hub and Node firmware, a `SHA256SUMS` file, and release notes grouped by conventional-commit type, all on a GitHub Release. A wire-compatibility gate runs before anything is pushed, and the same workflow runs in a dry-run mode on pull requests: it builds every artifact, verifies the checksums and publishes nothing.

## Boundaries & Constraints

**Always:**
- **One version.** `X.Y.Z` comes from `deploy/images/release-version.sh`, and `deploy/charts/check-version.sh` still gates the run. Every asset name carries the version: `coldframe-hub-X.Y.Z.bin`, `coldframe-hub-X.Y.Z.elf`, and the same for node and for `<chart>-X.Y.Z.tgz`.
- **Firmware is a release build.** Build with `cargo build --release --locked` and no `--features`. Before any firmware is attached, check for both crates that `cargo build --release --features dev-mode` fails with the existing `compile_error!` message. Flash images come from `espflash save-image --chip esp32s3 --merge --partition-table partitions.csv`, so a single file flashes at offset 0.
- **Checksums.** `SHA256SUMS` lists every non-image asset in `sha256sum` format. It is checked with `sha256sum --check --strict` before the Release is created, and in a dry run.
- **Ordering.** No image is pushed and no Release is created unless the version, chart, wire-compat and firmware jobs all succeed. Creating the Release is the last job, and it is the only job with `contents: write`. Pre-release versions (`X.Y.Z-…`) produce a GitHub pre-release.
- **Wire gate.** The Server accepts every major from `oldestAcceptedProtocolMajor` to `protocolMajor` in `packages/crypto-spec/crypto-spec.json`; both are generated constants. If the current `protocolMajor` is greater than the one at the previous `v*` tag, the gate fails unless `oldestAcceptedProtocolMajor` is at most the previous major. When there is no previous tag, the gate passes with a notice.
- **Release notes.** Group the commits from the previous `v*` tag (or from the root commit when there is none) to the release commit by conventional-commit type: Breaking changes (`!` or `BREAKING CHANGE:`), Features, Fixes, Performance, then the remaining types, then Other. Exclude merge commits. The notes name each image `ghcr.io/escendit/coldframe/<component>:X.Y.Z` and the flashing assets. They also state that no mobile store binaries are published, with a link to `docs/build/apps.md` at the tag.
- Pin actions to a commit SHA with the release named in a comment, and verify tool downloads by sha256, as the existing workflows do.

**Never:**
- Don't publish mobile store binaries. Don't add OTA. Don't push or release anything in dry-run mode.
- Don't edit `.github/workflows/ci.yml`: editing it reruns every job. The dry run uses release.yml's own `pull_request` and `workflow_dispatch` triggers.
- Don't touch the heartbeat major (`HeartbeatProtocolVersion`). It is out of scope.
- Don't edit `sprint-status.yaml`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Release | tag `v0.2.0`, charts at 0.2.0 | Images pushed. Release `v0.2.0` with 2×.bin, 2×.elf, chart .tgz files, SHA256SUMS and grouped notes | No error expected |
| Pre-release | tag `v0.2.0-rc.1` | As above, marked pre-release | No error expected |
| Chart mismatch | tag `v0.3.0`, charts at 0.2.0 | Nothing pushed or released | `check-version.sh` error names the charts |
| Major bump, still accepted | previous tag major 1 → now 2, oldest 1 | Gate passes | No error expected |
| Major bump, previous dropped | previous tag major 1 → now 2, oldest 2 | Run fails before any push | `::error::` names both majors |
| First release | no earlier `v*` tag | Gate passes; notes cover the full history | Notice only |
| Dry run | PR touching release paths | Every asset built, checksums verified, uploaded as a workflow artifact; no push and no Release | Fails on any missing asset or bad checksum |

</intent-contract>

## Code Map

- `.github/workflows/release.yml` -- the current jobs are version, verify (calls `images-verify.yml`) and a 4-component publish matrix. Extend this file. `on: push: tags: v*` stays; add `pull_request` (with paths) and `workflow_dispatch`.
- `.github/workflows/ci.yml:208-314` -- the `firmware` job. Copy its pinned versions (ESPUP 0.17.1, esp toolchain 1.95.0.0, espflash 4.5.0), its apt packages, its `espup install … --export-file ~/export-esp.sh` step and its rust-cache setup. The Node dev-mode refusal check is at `:286-299` and is the pattern to reuse. Read-only.
- `.github/workflows/images-verify.yml` -- the helm v4.2.2 download and its sha256 pin are the pattern for installing helm. Read-only.
- `apps/rs/{hub,node}/` -- the firmware crates, each with its own toolchain (`channel = "esp"`), `.cargo/config.toml` (target `xtensa-esp32s3-none-elf`), `partitions.csv` and `Cargo.lock`. The ELF is at `target/xtensa-esp32s3-none-elf/release/coldframe-{hub,node}`. The dev-mode guard is at `hub/src/main.rs:37-40` and `node/src/main.rs:62-65`.
- `apps/rs/hub/check-image.sh` -- the existing `save-image` call and the credential canary check. Run it in the release firmware job.
- `deploy/images/release-version.sh` and `deploy/images/test.sh` -- tag parsing and the bash `expect` test style to follow.
- `deploy/charts/check-version.sh` and `deploy/charts/test.sh:236-243` -- the version gate. Coldframe charts: server, web, keycloak. All charts are at 0.1.0.
- `packages/crypto-spec/crypto-spec.json:3` -- `protocolMajor: 1`. `scripts/lib/spec.ts:7,123` holds the interface and the `Protocol` constant group; add `oldestAcceptedProtocolMajor` (kind `u8`) there and regenerate the outputs: `packages/rs/crypto/src/spec.rs`, `packages/cs/crypto/Generated/CryptoSpec.g.cs` and `packages/kt/core/generated/.../CryptoSpec.kt`.
- `apps/cs/server/Devices/NodeFrameReader.cs:101` and `apps/cs/server/Devices/DeviceGrain.cs:641` -- the strict `!= CryptoSpec.ProtocolMajor` checks that become a range check.
- `tests/cs/server.tests/Edge/DeviceIngestRequestTests.cs` and `tests/cs/server.integration/Devices/IngestTests.cs` -- the existing tests for major rejection.
- `docs/operations/install.md:4-8` -- says Fleet installs at a release tag. Link from it to the new release doc.

## Tasks & Acceptance

**Execution:**
- `packages/crypto-spec/crypto-spec.json`, `scripts/lib/spec.ts` and the generated outputs -- add `oldestAcceptedProtocolMajor: 1` with the doc "The oldest wire major the Server still accepts.", then run `pnpm --filter @coldframe/crypto-spec run generate` -- this gives the gate one machine-readable source of truth.
- `apps/cs/server/Devices/NodeFrameReader.cs` and `DeviceGrain.cs` -- accept `OldestAcceptedProtocolMajor <= v <= ProtocolMajor` -- the Server now does what the gate checks. Extend the existing rejection tests to cover a major below and a major above the range.
- `deploy/release/check-wire-compat.sh` -- usage `<previous-spec.json|-> <current-spec.json>`. Use `jq` and exit 1 with a message naming the majors. Also fail when `oldestAcceptedProtocolMajor > protocolMajor`.
- `deploy/release/release-notes.sh` -- usage `<version> <from-ref|-> <to-ref>`, writing Markdown to stdout. It covers the grouping, the image list, the firmware and flashing note, and the mobile and apps-guide note.
- `deploy/release/checksums.sh` -- usage `<dir>`. It writes `SHA256SUMS` over every file except itself, then verifies it with `--check --strict`.
- `deploy/release/test.sh` -- tests in the style of `deploy/images/test.sh`. Cover every I/O row of the wire gate and notes grouping, using a throwaway git repo in `mktemp -d` that has feat, fix, `feat!` and merge commits. Also cover checksum creation and detection of a tampered file.
- `.github/workflows/release.yml` -- update the header comment, then add or change these jobs:
  - **mode**: `dry_run` is true unless the event is `push`. A dry run takes its version from `deploy/charts/server/Chart.yaml` (`v<version>`).
  - **checks**: run `deploy/release/test.sh`, check-version, and the wire gate. Use fetch-depth 0, and find the previous tag with `git describe --tags --abbrev=0 --match 'v*' <sha>^`.
  - **firmware matrix**: hub and node. Run the dev-mode refusal, the release build, `check-image.sh` (hub only) and save-image `--merge`, then upload the `.bin` and `.elf`.
  - **charts**: `helm package` each chart in `deploy/charts/*`.
  - **notes**.
  - **verify**: real tag runs only.
  - **publish**: on a dry run, build without `--push`.
  - **release**: download all assets and run `checksums.sh`. On a dry run, upload them as one workflow artifact. Otherwise run `gh release create "v$VERSION" --verify-tag --notes-file … [--prerelease]` with all assets.
  - The `pull_request` paths are `.github/workflows/release.yml`, `deploy/release/**`, `deploy/charts/*/Chart.yaml`, `packages/crypto-spec/crypto-spec.json` and `apps/rs/{hub,node}/{Cargo.toml,Cargo.lock,partitions.csv}`.
- `deploy/charts/set-version.sh` and `deploy/charts/test.sh` -- usage `<version>`. It sets `version` on every chart and `appVersion` on server, web and keycloak, and is tested by round-tripping it through `check-version.sh`. This is how a maintainer sets the charts to `X.Y.Z` before tagging.
- `docs/operations/release.md` -- how to cut a release: set-version, PR, tag, what gets published, the dry run, the wire-gate rule and how to verify `SHA256SUMS`. Link it from `docs/operations/install.md` and `deploy/README.md`.
- `docs/build/apps.md` -- a short guide. It explains that the apps are built from source with your own APNs key and FCM project, that the Server URL and issuer are build-time configuration, and points to the existing app READMEs. Story 10.3 expands it.

**Acceptance Criteria:**
- Given a `vX.Y.Z` tag with matching charts, when the workflow runs, then the GitHub Release `vX.Y.Z` has these assets: Hub and Node `.bin` and `.elf`, every chart `.tgz` (Coldframe charts with `appVersion` X.Y.Z), and `SHA256SUMS`. Its notes are grouped by conventional-commit type and link to `docs/build/apps.md`, and the four images are pushed at `X.Y.Z`.
- Given a pull request that changes `release.yml`, when CI runs, then the `Release` dry run produces every asset, `sha256sum --check --strict` passes, and nothing is pushed or released.

## Spec Change Log

## Review Triage Log

### 2026-10-19 — Review pass
- verdicts: 38 findings — high 0, medium 15, low 20, false 3, maybe-false 0
- findings:
  - `[medium]` `[patch]` (blind) Range check declares older majors accepted but Frames.Aad and downlink sealing use the current major — added tripwire test AFrameAtTheOldestAcceptedMajorIsDecodedAndItsDownlinkOpens in NodeFrameReaderTests and documented the requirement in release.md and packages/proto/README.md.
  - `[medium]` `[patch]` (blind) Wire gate passes a protocolMajor decrease — check-wire-compat.sh now fails on a decrease; test row added.
  - `[medium]` `[patch]` (blind) Previous-tag lookup matches pre-release and non-version v* tags — `--match 'v[0-9]*'`, plus `--exclude 'v*-*'` for a final release.
  - `[medium]` `[patch]` (blind) Images pushed before the asset check and checksums, and the runbook claims nothing is pushed — new `assets` job (exact-asset check + SHA256SUMS) that publish needs; the runbook covers a failed final Release job (re-run it, do not re-tag).
  - `[low]` `[patch]` (blind) firmware/charts do not wait for checks, contradicting the runbook — they now need [mode, checks].
  - `[low]` `[patch]` (blind) check-image.sh rebuilds after the --locked release build — reordered so the release build runs last.
  - `[low]` `[patch]` (blind) Dry-run paths miss scripts that can break a tag run — added check-version.sh, set-version.sh, release-version.sh, check-image.sh and crypto-spec/scripts/**.
  - `[low]` `[reject]` (blind) Firmware crates and app versions stay 0.0.0 — Release asset names carry X.Y.Z; changing crate versions needs Cargo.lock edits and new checks, and users rarely read the app descriptor; listed as a residual risk.
  - `[low]` `[patch]` (blind) ELF flashing needs partitions.csv, which is not shipped — notes link both partitions.csv at the tag; test asserts it.
  - `[low]` `[patch]` (blind) Header claims every tool download is sha256-checked — corrected: only helm is; espup/espflash are pinned crates installed with --locked.
  - `[low]` `[patch]` (blind) Tag runs restore rust-cache into shipped builds — the cache step runs only on a dry run.
  - `[low]` `[defer]` (blind) Tag not checked against main — pre-existing since Story 2.1; deferred.
  - `[low]` `[reject]` (blind) No image digests or provenance on the Release — not required by the story; needs matrix outputs passed into the notes, which is more than a direct fix.
  - `[low]` `[reject]` (blind) A backport release takes the Latest badge — no backport branches exist; a conditional --latest needs a version comparison.
  - `[low]` `[patch]` (blind) Below-range test repeats the 0 case and would not compile if oldest were 0 — the gate now requires both majors >= 1.
  - `[false]` `[reject]` (blind) Coldframe chart list duplicated in set-version/check-version — the round trip in deploy/charts/test.sh fails if check-version gains a chart set-version lacks; the other direction checks nothing extra, so no appVersion drift goes unseen.
  - `[low]` `[patch]` (blind) Runbook says Verify runs on every PR — reworded: it runs on a tag; a dry run skips it.
  - `[low]` `[reject]` (blind) Dry-run version can equal an existing tag — cosmetic; the artifact is named `-dry-run` and nothing is published.
  - `[medium]` `[patch]` (edge) Older-major frames fail AEAD — same root cause and fix as the first row (tripwire test + docs).
  - `[medium]` `[patch]` (edge) Major decrease passes — same fix as the decrease row.
  - `[medium]` `[patch]` (edge) Pre-release previous tag shrinks the final notes — same fix as the tag-match row.
  - `[low]` `[patch]` (edge) Non-release v* tag picked as previous — same fix as the tag-match row.
  - `[medium]` `[patch]` (edge) Notes/publish/release failure after images pushed — same fix as the assets-job row.
  - `[medium]` `[patch]` (edge) Runbook claims nothing pushed on any failure — same fix as the assets-job row.
  - `[low]` `[patch]` (edge) Shipped Hub ELF may come from the check-image build — same fix as the reorder row.
  - `[low]` `[patch]` (edge) Docs say gates stop everything — same fix as the needs: checks row.
  - `[low]` `[reject]` (edge) Below-range test duplicates ProtocolVersion = 0 — harmless redundancy; the gate's >= 1 rule keeps it compiling.
  - `[low]` `[patch]` (edge) oldestAcceptedProtocolMajor 0 breaks the test build — the gate rejects majors below 1.
  - `[low]` `[reject]` (edge) set-version.sh can leave charts half-updated — needs a Chart.yaml without appVersion, which none has; the charts are in git and easy to restore.
  - `[medium]` `[patch]` (verification-gap) Acceptance of an older in-range major is never tested — tripwire test added, as in the first row.
  - `[medium]` `[defer]` (verification-gap) Tag-only branches of release.yml never run in the dry run — they can only run on a real tag; deferred and owed as operator_actions.
  - `[medium]` `[patch]` (verification-gap) Old-major frames fail authentication (AAD) — grouped with the first row.
  - `[medium]` `[patch]` (verification-gap) Old-major Nodes drop downlinks — grouped with the first row; the tripwire test also opens a downlink under the old AAD.
  - `[false]` `[reject]` (intent) The workflow does not itself write appVersion — the charts at the tag must carry X.Y.Z because Fleet installs from git (Story 2.2 contract), check-version.sh enforces it, and the packaged .tgz files carry appVersion X.Y.Z; set-version.sh is the setter.
  - `[medium]` `[patch]` (intent) Wire gate trusts a declared constant — grouped with the first row; the tripwire test ties the constant to the decode path.
  - `[false]` `[reject]` (intent) "Dry-run tag" reading as a pushed throwaway tag — a pushed v* tag would publish, which contradicts a dry run; the PR/dispatch dry run runs the same jobs at the chart version and publishes nothing.
  - `[medium]` `[patch]` (intent) Proving AC1 needs a real tag push, which only a maintainer can do — spec finalized at awaiting-operator with operator_actions.
  - `[low]` `[reject]` (intent) The heartbeat major is not gated — no heartbeat bump is planned; gating it means moving HeartbeatProtocolVersion into crypto-spec (more than a direct fix).

## Design Notes

A range (`oldest..current`) needs no new generator kind, unlike a list of majors. Bumping `protocolMajor` while keeping `oldestAcceptedProtocolMajor` at the old value is the explicit promise that the Server still decodes the previous major. The gate holds the author to that promise across tags.

## Verification

**Commands:**
- `deploy/release/test.sh && deploy/charts/test.sh && deploy/images/test.sh` -- expected: all pass
- `pnpm --filter @coldframe/crypto-spec run check` -- expected: the outputs are fresh
- `dotnet test tests/cs/server.tests` -- expected: green, including the new range cases
- `cargo test --workspace --locked` -- expected: green (the generated Rust constant compiles)
- `actionlint .github/workflows/release.yml`, if available -- expected: no findings

**Manual checks (if no CLI):**
- On the PR, the `Release` workflow's dry run is green and its artifact contains the 4 firmware files, the chart .tgz files and `SHA256SUMS`.

## Auto Run Result

Status: awaiting-operator

**Summary.** A `vX.Y.Z` tag now produces one release. The four images are pushed (unchanged). A GitHub Release gets release-build Hub and Node firmware (a merged `.bin` that flashes at offset 0, plus the `.elf`), every chart as `<chart>-X.Y.Z.tgz`, `SHA256SUMS`, and notes grouped by conventional-commit type that point to `docs/build/apps.md` (no store binaries). Gates run before anything is pushed: the chart versions, the wire gate (`oldestAcceptedProtocolMajor..protocolMajor`, which the Server now enforces as a range), a check that dev-mode is refused, and the asset and checksum checks. Pull requests on the release paths, and manual runs, run the whole workflow as a dry run that pushes and releases nothing.

**Files changed**
- `.github/workflows/release.yml`: these jobs: mode, checks, firmware matrix, charts, notes, assets, verify (tag only), publish (no push on a dry run), release (`gh release create` or the dry-run artifact).
- `deploy/release/{check-wire-compat.sh,release-notes.sh,checksums.sh,test.sh}`: the wire gate, the notes, SHA256SUMS, and 62 tests.
- `deploy/charts/set-version.sh`, `deploy/charts/test.sh`: set the charts to a version before tagging, tested by a round trip through check-version.sh.
- `packages/crypto-spec/crypto-spec.json`, `scripts/lib/spec.ts` and the generated Rust/C#/Kotlin: the `oldestAcceptedProtocolMajor` constant.
- `apps/cs/server/Devices/{NodeFrameReader,DeviceGrain}.cs`: accept a range of majors.
- `tests/cs/server.tests/Devices/NodeFrameReaderTests.cs`, `tests/cs/server.integration/Devices/IngestTests.cs`: cases above and below the range, and the tripwire test for the oldest accepted major.
- `docs/operations/release.md` (new runbook), `docs/build/apps.md` (new, short apps guide), plus links in `docs/operations/install.md`, `deploy/README.md`, `deploy/images/README.md` and `packages/proto/README.md`.
- `.gitignore`: un-ignore `deploy/release/` and `docs/build/`.

**Review findings.** 38 findings: medium 15, low 20, false 3. Patched: 12 root causes (6 medium and 6 low entries; rows grouped as in the triage log). Deferred: 2 (tag-on-main enforcement, which predates this story; the tag-only workflow branches, owed as operator actions). Rejected, with the reasons recorded in the triage log: the firmware/app embedded version (low), image digests/provenance (low), the Latest badge on backports (low), the dry-run version colliding with a tag (low), the duplicate below-range test (low), half-updated charts in set-version (low), the heartbeat major (low), and three false findings (the chart-list duplication, the workflow not writing appVersion itself, the literal reading of "dry-run tag").

**Follow-up review: recommended (true).** Six medium entries were patched. The risk not yet verified: the restructured `release.yml` job graph (the new `assets` job, the `needs`/`if` conditions, the bundle hand-off to `release`) has passed actionlint and a YAML parse, but has not run on GitHub yet. The PR's dry run is its first run.

**Verification.**
- `deploy/release/test.sh`: 62 passed.
- `deploy/images/test.sh`: 33 passed.
- `deploy/charts/test.sh`: the set-version and check-version cases pass. The Fleet/kubeconform cases fail locally only because those tools are missing.
- `pnpm --filter @coldframe/crypto-spec run check`: fresh.
- `dotnet test --project tests/cs/server.tests`: 1139 passed.
- `tests/cs/server.integration`: builds. It was not run, because Keycloak would not start under podman.
- `cargo test --workspace --locked`: 350 passed.
- actionlint 1.7.12 with shellcheck, run by the implementer: clean. `release.yml` parses as YAML.
- Firmware steps run locally by the implementer: the dev-mode refusal for both crates, the release builds, `check-image.sh`, and `save-image --merge` (the image starts with 0xE9).

**Residual risks.**
- The real-tag path (ghcr push, `gh release create`, pre-release flag) runs only on a tag; see operator_actions.
- The firmware crates still report version 0.0.0 inside the binary.
- A dry run rebuilds the images and both firmware crates, so pull requests on the release paths take longer.
- The extended ingest integration test is unverified locally and needs CI.
