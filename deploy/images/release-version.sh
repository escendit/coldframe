#!/usr/bin/env bash
# Validates a release tag and prints the image version it stands for.
#
#   deploy/images/release-version.sh v1.2.3        -> 1.2.3
#   deploy/images/release-version.sh v1.2.3-rc.1   -> 1.2.3-rc.1
#   deploy/images/release-version.sh v1.2          -> error naming the tag, exit 1
#
# One SemVer per release tag vX.Y.Z across the monorepo; the image tag equals it without the "v".
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: $0 <tag>" >&2
  exit 2
fi

tag=$1
# SemVer 2.0.0 pre-release: dot-separated, non-empty identifiers; a numeric one has no leading zero.
identifier='(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
pattern="^v(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(-${identifier}(\\.${identifier})*)?$"

if [[ ! ${tag} =~ ${pattern} ]]; then
  echo "Tag '${tag}' is not a release tag: expected vX.Y.Z or vX.Y.Z-<pre-release>." >&2
  exit 1
fi

echo "${tag#v}"
