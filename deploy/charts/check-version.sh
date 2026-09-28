#!/usr/bin/env bash
# Checks that the charts carry the release version.
#
#   deploy/charts/check-version.sh <version> [charts-dir]
#
# Every chart's `version`, and the `appVersion` of the Coldframe charts (server, web, keycloak),
# must equal <version> (the release tag without its "v", as printed by
# deploy/images/release-version.sh). temporal and nats carry the upstream version as appVersion.
# Fails naming each Chart.yaml that differs. charts-dir defaults to the directory of this script.
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "usage: $0 <version> [charts-dir]" >&2
  exit 2
fi

version=$1
charts=${2:-$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)}
coldframe_charts=(server web keycloak)
failures=0

# field <Chart.yaml> <name>: the top-level scalar, without quotes.
field() {
  sed -n -E "s/^$2:[[:space:]]*\"?([^\"[:space:]]*)\"?[[:space:]]*$/\1/p" "$1" | head -n 1
}

shopt -s nullglob
found=0
for chart_file in "${charts}"/*/Chart.yaml; do
  found=$((found + 1))
  chart=$(basename "$(dirname "${chart_file}")")
  chart_version=$(field "${chart_file}" version)
  if [[ ${chart_version} != "${version}" ]]; then
    echo "${chart_file}: version is '${chart_version}', expected '${version}'" >&2
    failures=$((failures + 1))
  fi
  for coldframe in "${coldframe_charts[@]}"; do
    if [[ ${chart} == "${coldframe}" ]]; then
      app_version=$(field "${chart_file}" appVersion)
      if [[ ${app_version} != "${version}" ]]; then
        echo "${chart_file}: appVersion is '${app_version}', expected '${version}'" >&2
        failures=$((failures + 1))
      fi
    fi
  done
done

if [[ ${found} -eq 0 ]]; then
  echo "No Chart.yaml under ${charts}" >&2
  exit 2
fi
if [[ ${failures} -gt 0 ]]; then
  echo "Bump the charts to ${version} before tagging the release." >&2
  exit 1
fi
echo "All ${found} charts are at ${version}."
