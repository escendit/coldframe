#!/usr/bin/env bash
# FR-1: the Hub image holds no Wi-Fi credentials. Builds the release image with canary values in
# every environment variable a careless change might read credentials from, converts it with
# `espflash save-image`, and fails if any canary appears in the ELF or the flash image.
#
# Needs the esp toolchain (`source ~/export-esp.sh`) and espflash. Run from anywhere:
#   apps/rs/hub/check-image.sh
# The host test tests/rs/setup/tests/image_guard.rs checks the sources for env reads on every CI
# run; this script checks the built artefact.
set -euo pipefail

cd "$(dirname "$0")"

declare -A canaries=(
  [WIFI_SSID]="cf-canary-wifi-ssid-4b1d"
  [WIFI_PASSWORD]="cf-canary-wifi-password-9e27"
  [SSID]="cf-canary-ssid-c3a8"
  [PASSWORD]="cf-canary-password-5f60"
  [CF_WIFI_SSID]="cf-canary-cf-wifi-ssid-81d2"
)
for name in "${!canaries[@]}"; do
  export "$name=${canaries[$name]}"
done

cargo build --release

elf="target/xtensa-esp32s3-none-elf/release/coldframe-hub"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
image="$work/coldframe-hub.bin"
espflash save-image --chip esp32s3 --partition-table partitions.csv "$elf" "$image" >/dev/null

# The search must be able to find strings at all: the crate name is in both artefacts' app
# descriptor.
for artefact in "$elf" "$image"; do
  if ! grep -a -F -q "coldframe-hub" "$artefact"; then
    echo "check-image: sanity string not found in $artefact; the search is broken" >&2
    exit 2
  fi
done

failed=0
for name in "${!canaries[@]}"; do
  for artefact in "$elf" "$image"; do
    if grep -a -F -q "${canaries[$name]}" "$artefact"; then
      echo "check-image: the value of $name is baked into $(basename "$artefact")" >&2
      failed=1
    fi
  done
done
if [[ $failed -ne 0 ]]; then
  exit 1
fi
echo "check-image: no canary in the ELF or the image (${#canaries[@]} variables checked)"
