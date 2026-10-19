#!/usr/bin/env bash
# Sets the charts to a release version before the release is tagged (Story 10.1).
#
#   deploy/charts/set-version.sh <version> [charts-dir]
#
# Sets `version` of every chart, and `appVersion` of the Coldframe charts (server, web, keycloak),
# to <version> (the release tag without its "v", for example 0.2.0 or 0.2.0-rc.1). temporal and
# nats keep the upstream appVersion. deploy/charts/check-version.sh <version> passes afterwards.
# charts-dir defaults to the directory of this script.
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "usage: $0 <version> [charts-dir]" >&2
  exit 2
fi

version=$1
here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
charts=${2:-${here}}
coldframe_charts=(server web keycloak)

if ! "${here}/../images/release-version.sh" "v${version}" >/dev/null; then
  echo "'${version}' is not a release version: expected X.Y.Z or X.Y.Z-<pre-release>." >&2
  exit 1
fi

shopt -s nullglob
found=0
for chart_file in "${charts}"/*/Chart.yaml; do
  found=$((found + 1))
  chart=$(basename "$(dirname "${chart_file}")")
  if ! grep -qE '^version:' "${chart_file}"; then
    echo "${chart_file}: no top-level version" >&2
    exit 1
  fi
  sed -i -E "s/^version:[[:space:]]*.*$/version: ${version}/" "${chart_file}"
  for coldframe in "${coldframe_charts[@]}"; do
    if [[ ${chart} == "${coldframe}" ]]; then
      if ! grep -qE '^appVersion:' "${chart_file}"; then
        echo "${chart_file}: no top-level appVersion" >&2
        exit 1
      fi
      sed -i -E "s/^appVersion:[[:space:]]*.*$/appVersion: \"${version}\"/" "${chart_file}"
    fi
  done
done

if [[ ${found} -eq 0 ]]; then
  echo "No Chart.yaml under ${charts}" >&2
  exit 2
fi
echo "Set ${found} charts to ${version}."
