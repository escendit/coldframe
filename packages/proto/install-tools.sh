#!/usr/bin/env bash
# Downloads the pinned buf and oasdiff for check-compat.sh into a folder and verifies their sha256.
# Usage: packages/proto/install-tools.sh <bin-dir>    (Linux x86_64; CI and local runs share the pins)
set -euo pipefail

BUF_VERSION="1.73.0"
# sha256 of buf-Linux-x86_64 (the release's sha256.txt).
BUF_SHA256="8f2986298ad08f0cc1bf999b9797b7c383adf32d7edf0f73d6f1e1a701baeac1"
OASDIFF_VERSION="1.32.1"
# sha256 of oasdiff_1.32.1_linux_amd64.tar.gz (the release's checksums.txt).
OASDIFF_SHA256="7c8939fc49b75ee11fec66a5b83b37a2fca6aee109fed85013b1ba2ac2a1ee7f"

bin="${1:?usage: install-tools.sh <bin-dir>}"

if [[ "$(uname -s)-$(uname -m)" != "Linux-x86_64" ]]; then
  echo "install-tools.sh pins Linux x86_64 builds; install buf ${BUF_VERSION} and oasdiff ${OASDIFF_VERSION} yourself." >&2
  exit 1
fi

downloads="$(mktemp -d)"
trap 'rm -rf "${downloads}"' EXIT
mkdir -p "${bin}"

curl --fail --silent --show-error --location --output "${downloads}/buf" \
  "https://github.com/bufbuild/buf/releases/download/v${BUF_VERSION}/buf-Linux-x86_64"
echo "${BUF_SHA256}  ${downloads}/buf" | sha256sum --check --strict
install -m 0755 "${downloads}/buf" "${bin}/buf"

curl --fail --silent --show-error --location --output "${downloads}/oasdiff.tar.gz" \
  "https://github.com/oasdiff/oasdiff/releases/download/v${OASDIFF_VERSION}/oasdiff_${OASDIFF_VERSION}_linux_amd64.tar.gz"
echo "${OASDIFF_SHA256}  ${downloads}/oasdiff.tar.gz" | sha256sum --check --strict
tar --extract --gzip --file "${downloads}/oasdiff.tar.gz" --directory "${downloads}" oasdiff
install -m 0755 "${downloads}/oasdiff" "${bin}/oasdiff"

"${bin}/buf" --version
"${bin}/oasdiff" --version
