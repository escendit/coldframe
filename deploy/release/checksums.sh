#!/usr/bin/env bash
# The SHA256SUMS of the release assets (Story 10.1).
#
#   deploy/release/checksums.sh <dir>           writes <dir>/SHA256SUMS, then verifies it
#   deploy/release/checksums.sh --check <dir>   only verifies an existing <dir>/SHA256SUMS
#
# SHA256SUMS lists every regular file in <dir> except itself, in `sha256sum` format and sorted by
# name, so `sha256sum --check --strict SHA256SUMS` verifies a download. Verifying also fails when
# a file of <dir> is not listed. <dir> must hold only files.
set -euo pipefail

check_only=false
if [[ $# -eq 2 && $1 == "--check" ]]; then
  check_only=true
  shift
fi
if [[ $# -ne 1 ]]; then
  echo "usage: $0 [--check] <dir>" >&2
  exit 2
fi

dir=$1
if [[ ! -d ${dir} ]]; then
  echo "${dir}: no such directory" >&2
  exit 2
fi
cd "${dir}"

shopt -s nullglob dotglob
assets=()
for entry in *; do
  [[ ${entry} == "SHA256SUMS" ]] && continue
  if [[ ! -f ${entry} || -L ${entry} ]]; then
    echo "${dir}/${entry}: not a regular file; the asset directory holds only files" >&2
    exit 1
  fi
  assets+=("${entry}")
done
if [[ ${#assets[@]} -eq 0 ]]; then
  echo "${dir}: no assets to checksum" >&2
  exit 1
fi

if [[ ${check_only} == false ]]; then
  LC_ALL=C sha256sum -- "${assets[@]}" | LC_ALL=C sort -k 2 > SHA256SUMS
elif [[ ! -f SHA256SUMS ]]; then
  echo "${dir}/SHA256SUMS: missing" >&2
  exit 1
fi

# Every asset is listed: a file added after SHA256SUMS was written would not be checked.
listed=$(sed -E 's/^[0-9a-f]{64}  //' SHA256SUMS | LC_ALL=C sort)
present=$(printf '%s\n' "${assets[@]}" | LC_ALL=C sort)
if [[ ${listed} != "${present}" ]]; then
  echo "${dir}/SHA256SUMS does not list exactly the assets:" >&2
  diff <(echo "${listed}") <(echo "${present}") >&2 || true
  exit 1
fi

sha256sum --check --strict SHA256SUMS
