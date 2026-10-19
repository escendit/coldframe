# Cut a release

One SemVer per release tag `vX.Y.Z` (or `vX.Y.Z-<pre-release>`) covers the whole monorepo: the
container images, the Hub and Node firmware and the Helm charts all carry `X.Y.Z` (Story 10.1,
AD-23). The tag triggers [`.github/workflows/release.yml`](../../.github/workflows/release.yml),
which builds and checks everything first and publishes only when every check passed.

## Steps

1. **Set the charts to the version.** On a branch from `main`:

   ```sh
   deploy/charts/set-version.sh 0.2.0
   deploy/charts/check-version.sh 0.2.0
   ```

   `set-version.sh` sets `version` of every chart in `deploy/charts` and `appVersion` of the
   Coldframe charts (server, web, keycloak); temporal and nats keep the upstream `appVersion`.
2. **Open a pull request** with the change, for example `chore(release): 0.2.0`. Changing a
   `Chart.yaml` runs the release workflow as a [dry run](#dry-run) at the new version; check that
   it is green and look at its artifact.
3. **Merge, then tag the merge commit** on `main` and push the tag:

   ```sh
   git switch main && git pull
   git tag -a v0.2.0 -m "Coldframe 0.2.0"
   git push origin v0.2.0
   ```

   A tag with a pre-release part (`v0.2.0-rc.1`) produces a GitHub pre-release. The charts then
   have to be at `0.2.0-rc.1`.
4. **Watch the `Release` run.** If a gate fails, nothing is pushed or released: fix the cause,
   delete the tag (`git push --delete origin v0.2.0`, `git tag -d v0.2.0`) and tag again. The
   exception is the final **Release** job (`gh release create`): it runs after the images were
   pushed at `X.Y.Z`. If it fails, re-run the failed job from the run page (**Re-run failed jobs**);
   do not delete or move the tag, because the pushed images already carry the version.

## What gets published

| What | Where | Name |
| --- | --- | --- |
| Container images (linux/amd64 and linux/arm64 where the base image has both) | ghcr.io | `ghcr.io/escendit/coldframe/{server,web,migrations,keycloak}:X.Y.Z` |
| Hub and Node flash images: bootloader, partition table and app merged, flashed at offset 0 | GitHub Release | `coldframe-hub-X.Y.Z.bin`, `coldframe-node-X.Y.Z.bin` |
| Hub and Node firmware ELF | GitHub Release | `coldframe-hub-X.Y.Z.elf`, `coldframe-node-X.Y.Z.elf` |
| Every Helm chart of `deploy/charts` | GitHub Release | `<chart>-X.Y.Z.tgz` |
| Checksums of every Release asset | GitHub Release | `SHA256SUMS` |
| Release notes, grouped by Conventional Commit type | GitHub Release | the Release body |

Only the exact version is tagged, never `latest`. The firmware is a release build
(`cargo build --release --locked`, no features). Before it is attached, the workflow checks that
a release build with `--features dev-mode` fails with the `compile_error!` message, and that the
Hub image holds no credential canary ([`apps/rs/hub/check-image.sh`](../../apps/rs/hub/check-image.sh)).
There is no OTA: the firmware is flashed over USB with `espflash`.

No mobile store binaries are published. Adopters build the apps themselves:
[`docs/build/apps.md`](../build/apps.md).

The release notes ([`deploy/release/release-notes.sh`](../../deploy/release/release-notes.sh)) cover
the commits since the previous release tag (`v[0-9]*`; for a final release, pre-release tags are
skipped), or the whole history for the first release, without merge
commits. They are grouped as Breaking changes (`type!:` or a `BREAKING CHANGE:` footer), Features,
Fixes, Performance, the other Conventional Commit types, and Other.

## Order and gates

The jobs run in this order, and each gate stops everything after it:

1. **Mode**: a tag push is a release; the tag must be `vX.Y.Z` or `vX.Y.Z-<pre-release>`.
2. **Checks**: the tests of the release scripts
   ([`deploy/release/test.sh`](../../deploy/release/test.sh)), the chart versions
   ([`deploy/charts/check-version.sh`](../../deploy/charts/check-version.sh)) and the
   [wire gate](#wire-gate).
3. **Firmware** (Hub and Node), **Charts** and **Notes**, side by side.
4. **Assets**: the assets must be exactly the four firmware files and one package per chart;
   `SHA256SUMS` is written and verified with `sha256sum --check --strict`.
5. **Verify**: on a tag, the image verification of CI runs again
   ([`.github/workflows/images-verify.yml`](../../.github/workflows/images-verify.yml)); a dry run
   skips it, because CI runs it on every pull request.
6. **Publish**: the images are pushed.
7. **Release**: the verified assets are checked again and the GitHub Release is created with them.
   It is the last job and the only one that may write to the repository. It runs after the images
   were pushed; see step 4 of [Steps](#steps) for a failure here.

## Wire gate

The Server accepts every wire major from `oldestAcceptedProtocolMajor` to `protocolMajor` of
[`packages/crypto-spec/crypto-spec.json`](../../packages/crypto-spec/crypto-spec.json). Both are
generated into Rust, C# and Kotlin (`pnpm --filter @coldframe/crypto-spec run generate`).
[`deploy/release/check-wire-compat.sh`](../../deploy/release/check-wire-compat.sh) compares the spec
at the release with the spec at the previous release tag:

- When `protocolMajor` is greater than at the previous tag, `oldestAcceptedProtocolMajor` must still
  be at most the previous major, so the Server keeps decoding what Devices in the field send.
  Otherwise the run fails before anything is pushed and names both majors.
- `oldestAcceptedProtocolMajor` greater than `protocolMajor`, a `protocolMajor` lower than at the
  previous tag, and a major below 1 always fail.
- With no previous tag (the first release), the gate passes with a notice.

To bump the major, raise `protocolMajor`, leave `oldestAcceptedProtocolMajor` at the old major and
make the Server decode both. Accepting the old major means more than the range check: the frame AAD
(`Frames.Aad` in `packages/cs/crypto`) and the downlink sealing in `DeviceGrain` must use the major
the Node speaks. The test `AFrameAtTheOldestAcceptedMajorIsDecodedAndItsDownlinkOpens`
(`tests/cs/server.tests/Devices/NodeFrameReaderTests.cs`) guards this: it fails as soon as
`protocolMajor` is bumped while those paths still handle only the current major. Raise `oldestAcceptedProtocolMajor` in a later release, once no Device
speaks the old major any more.

## Dry run

A pull request that changes the workflow, `deploy/release/`, a `Chart.yaml`, the crypto spec or the
firmware's `Cargo.toml`, `Cargo.lock` or `partitions.csv` runs the same workflow as a dry run. A
manual run (**Actions → Release → Run workflow**) does the same on any branch. The version comes
from `deploy/charts/server/Chart.yaml`. A dry run builds every asset, writes and verifies
`SHA256SUMS`, builds the images without pushing them and skips the image verification job (CI runs
it on every pull request). It uploads the assets, `SHA256SUMS` and the notes as the workflow
artifact `release-X.Y.Z-dry-run`. It pushes nothing and creates no Release.

The release scripts can also run locally:

```sh
deploy/release/test.sh
deploy/release/release-notes.sh 0.2.0 v0.1.0 HEAD
deploy/release/check-wire-compat.sh <(git show v0.1.0:packages/crypto-spec/crypto-spec.json) packages/crypto-spec/crypto-spec.json
```

## Verify a download

Download the assets and `SHA256SUMS` of the Release into one directory, then:

```sh
sha256sum --check --strict SHA256SUMS
```

Every line must say `OK`. With the GitHub CLI:

```sh
gh release download v0.2.0 --repo escendit/coldframe --dir coldframe-0.2.0
cd coldframe-0.2.0 && sha256sum --check --strict SHA256SUMS
```

Flash a merged image at offset 0, for example `espflash write-bin 0x0 coldframe-node-0.2.0.bin`.
