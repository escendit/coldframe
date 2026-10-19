#!/usr/bin/env bash
# Writes the Markdown notes of a GitHub Release to stdout (Story 10.1).
#
#   deploy/release/release-notes.sh <version> <from-ref|-> <to-ref>
#
# The commits from <from-ref> (the previous v* tag, excluded) to <to-ref> (the release commit) are
# grouped by Conventional Commit type: Breaking changes (`type!:` or a `BREAKING CHANGE:` footer),
# Features, Fixes, Performance, then the other types, then Other (subjects that are not
# Conventional Commits). "-" for <from-ref> means no previous release: the notes cover the whole
# history up to <to-ref>. Merge commits are left out. The notes then name the container images,
# the firmware and how to flash it, the charts, and say that no mobile store binaries are published.
#
# RELEASE_REPOSITORY (default escendit/coldframe) names the GitHub repository and the image path.
# Run inside the git repository.
set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "usage: $0 <version> <from-ref|-> <to-ref>" >&2
  exit 2
fi

version=$1
from=$2
to=$3
repository=${RELEASE_REPOSITORY:-escendit/coldframe}
repository=${repository,,}
tag="v${version}"

if [[ ${from} == "-" ]]; then
  range=${to}
else
  range="${from}..${to}"
fi

# The sections in order: the Conventional Commit types with a heading of their own, then any other
# type (by name), then Other.
known_types=(feat fix perf refactor docs test build ci style chore revert)
declare -A headings=(
  [feat]="Features"
  [fix]="Fixes"
  [perf]="Performance"
  [refactor]="Refactoring"
  [docs]="Documentation"
  [test]="Tests"
  [build]="Build"
  [ci]="CI"
  [style]="Style"
  [chore]="Chores"
  [revert]="Reverts"
)
declare -A entries=()
breaking=""
other=""
extra_types=()

pattern='^([A-Za-z]+)(\(([^)]*)\))?(!)?: (.+)$'

# One record per commit, oldest first: hash, subject, body, separated by \x1f and ended by \x1e.
log=$(git log --no-merges --reverse --format='%h%x1f%s%x1f%b%x1e' "${range}" --)

while IFS= read -r -d $'\x1e' record; do
  record=${record#$'\n'}
  [[ -z ${record} ]] && continue
  hash=${record%%$'\x1f'*}
  rest=${record#*$'\x1f'}
  subject=${rest%%$'\x1f'*}
  body=${rest#*$'\x1f'}

  if [[ ${subject} =~ ${pattern} ]]; then
    type=${BASH_REMATCH[1],,}
    scope=${BASH_REMATCH[3]}
    bang=${BASH_REMATCH[4]}
    description=${BASH_REMATCH[5]}
    if [[ -n ${scope} ]]; then
      line="- **${scope}:** ${description} (${hash})"
    else
      line="- ${description} (${hash})"
    fi
    if [[ -n ${bang} ]] || grep -qE '^BREAKING[ -]CHANGE: ' <<<"${body}"; then
      breaking+="${line}"$'\n'
    else
      if [[ -z ${entries[${type}]+set} && -z ${headings[${type}]+set} ]]; then
        extra_types+=("${type}")
      fi
      entries[${type}]+="${line}"$'\n'
    fi
  else
    other+="- ${subject} (${hash})"$'\n'
  fi
done <<<"${log}"

# section <heading> <lines>
section() {
  [[ -z $2 ]] && return 0
  printf '### %s\n\n%s\n' "$1" "$2"
}

echo "## Coldframe ${version}"
echo
echo "One version for the whole monorepo: the images, the firmware and the charts below all belong to \`${tag}\`."
echo

echo "## Changes"
echo
section "Breaking changes" "${breaking}"
for type in "${known_types[@]}"; do
  section "${headings[${type}]}" "${entries[${type}]:-}"
done
if [[ ${#extra_types[@]} -gt 0 ]]; then
  mapfile -t sorted < <(printf '%s\n' "${extra_types[@]}" | sort -u)
  for type in "${sorted[@]}"; do
    section "${type}" "${entries[${type}]:-}"
  done
fi
section "Other" "${other}"
if [[ -z ${breaking}${other} && ${#entries[@]} -eq 0 ]]; then
  echo "No commits since the previous release."
  echo
fi

cat <<NOTES
## Container images

Multi-arch (linux/amd64 and linux/arm64 where the base image has both), tagged with this version only:

- \`ghcr.io/${repository}/server:${version}\`
- \`ghcr.io/${repository}/web:${version}\`
- \`ghcr.io/${repository}/migrations:${version}\`
- \`ghcr.io/${repository}/keycloak:${version}\`

## Firmware

Release builds of the Hub and the Node for the ESP32-S3 (dev mode is refused in a release build):

- \`coldframe-hub-${version}.bin\` and \`coldframe-node-${version}.bin\`: merged flash images
  (bootloader, partition table and app) that flash at offset 0, for example
  \`espflash write-bin 0x0 coldframe-node-${version}.bin\`.
- \`coldframe-hub-${version}.elf\` and \`coldframe-node-${version}.elf\`: the same firmware as ELF,
  for debugging and for \`espflash flash --partition-table partitions.csv\` with the crate's
  partition table at this tag: [Hub](https://github.com/${repository}/blob/${tag}/apps/rs/hub/partitions.csv),
  [Node](https://github.com/${repository}/blob/${tag}/apps/rs/node/partitions.csv).

Check the downloads before flashing: \`sha256sum --check --strict SHA256SUMS\`.

## Helm charts

Every chart in \`deploy/charts\` is attached as \`<chart>-${version}.tgz\`. Fleet installs the
same charts from the repository at \`${tag}\`
([install guide](https://github.com/${repository}/blob/${tag}/docs/operations/install.md)).

## Mobile apps

No mobile store binaries are published. Build the iOS and Android apps from source with your own
APNs key and FCM project:
[docs/build/apps.md](https://github.com/${repository}/blob/${tag}/docs/build/apps.md).
NOTES
