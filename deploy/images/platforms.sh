#!/usr/bin/env bash
# Prints the release platforms that a Dockerfile's final base image provides.
#
#   deploy/images/platforms.sh <dockerfile> [manifest.json]
#
# Reads the image of the last FROM (the final stage) and inspects its manifest list with
# `docker buildx imagetools inspect --raw`, or reads the given manifest file instead (the test
# seam). Prints the intersection with linux/amd64,linux/arm64 as a comma-separated list on stdout,
# and one "missing:<platform>" line per absent platform on stderr. Exits 1 when the intersection
# is empty.
set -euo pipefail

wanted=(linux/amd64 linux/arm64)

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "usage: $0 <dockerfile> [manifest.json]" >&2
  exit 2
fi

dockerfile=$1
manifest_file=${2:-}

[[ -f ${dockerfile} ]] || { echo "No such Dockerfile: ${dockerfile}" >&2; exit 2; }

# The final stage's FROM line, without flags such as --platform=, and without "AS <name>".
image=$(awk 'toupper($1) == "FROM" { line = $0 } END { print line }' "${dockerfile}" \
  | awk '{ for (i = 2; i <= NF; i++) if ($i !~ /^--/) { print $i; exit } }')

if [[ -z ${image} ]]; then
  echo "No FROM line in ${dockerfile}." >&2
  exit 2
fi
if [[ ${image} == *'$'* ]]; then
  echo "The final FROM of ${dockerfile} uses a build argument (${image}); it must name an explicit image." >&2
  exit 2
fi

if [[ -n ${manifest_file} ]]; then
  manifest=$(cat "${manifest_file}")
else
  manifest=$(docker buildx imagetools inspect --raw "${image}")
  # A single-platform image has no manifest list: read the platform from its configuration.
  if [[ $(jq -r 'has("manifests")' <<<"${manifest}") != "true" ]]; then
    manifest=$(docker buildx imagetools inspect --format '{{json .Image}}' "${image}")
  fi
fi

# A manifest list carries one platform per entry (attestations are "unknown/unknown"); a single
# image's configuration carries its own os and architecture.
available=$(jq -r '
  if has("manifests") then .manifests[] | select(.platform != null) | .platform
  else . end
  | select(.os != null and .architecture != null)
  | "\(.os)/\(.architecture)"' <<<"${manifest}" | sort -u)

found=()
for platform in "${wanted[@]}"; do
  if grep -qxF "${platform}" <<<"${available}"; then
    found+=("${platform}")
  else
    echo "missing:${platform}" >&2
  fi
done

if [[ ${#found[@]} -eq 0 ]]; then
  echo "${image} provides none of ${wanted[*]}." >&2
  exit 1
fi

(IFS=,; echo "${found[*]}")
