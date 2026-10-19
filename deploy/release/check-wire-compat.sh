#!/usr/bin/env bash
# The wire-compatibility gate of a release (Story 10.1).
#
#   deploy/release/check-wire-compat.sh <previous-spec.json|-> <current-spec.json>
#
# The Server accepts every wire major from `oldestAcceptedProtocolMajor` to `protocolMajor` of
# packages/crypto-spec/crypto-spec.json. A release whose `protocolMajor` is greater than the one of
# the previous release (<previous-spec.json>, the file at the previous v* tag) fails unless
# `oldestAcceptedProtocolMajor` is still at most the previous major: the Server must keep decoding
# what Devices in the field speak. "-" stands for no previous release; the gate then passes with a
# notice. `oldestAcceptedProtocolMajor` greater than `protocolMajor`, a `protocolMajor` lower than
# at the previous release, and a major below 1 always fail.
#
# Exit 0 on pass, 1 on a gate failure (message on stderr), 2 on bad usage or an unreadable spec.
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: $0 <previous-spec.json|-> <current-spec.json>" >&2
  exit 2
fi

previous=$1
current=$2

# major <file> <field>: the integer field, or exit 2 naming the file. Wire majors start at 1.
major() {
  local value
  if ! value=$(jq -er --arg field "$2" '.[$field] | select(type == "number" and . == floor)' "$1" 2>/dev/null); then
    echo "$1: '$2' is missing or not an integer" >&2
    exit 2
  fi
  if (( value < 1 )); then
    echo "$1: '$2' is ${value}; wire majors start at 1" >&2
    exit 1
  fi
  echo "${value}"
}

current_major=$(major "${current}" protocolMajor)
oldest=$(major "${current}" oldestAcceptedProtocolMajor)

if (( oldest > current_major )); then
  echo "oldestAcceptedProtocolMajor ${oldest} is greater than protocolMajor ${current_major} in ${current}" >&2
  exit 1
fi

if [[ ${previous} == "-" ]]; then
  echo "No previous release: the wire gate passes (protocolMajor ${current_major}, oldest accepted ${oldest})."
  exit 0
fi

previous_major=$(major "${previous}" protocolMajor)

if (( current_major < previous_major )); then
  echo "protocolMajor goes down from ${previous_major} (previous release) to ${current_major}; a wire major never decreases." >&2
  exit 1
fi

if (( current_major > previous_major && oldest > previous_major )); then
  echo "protocolMajor goes from ${previous_major} (previous release) to ${current_major}, but the Server no longer accepts major ${previous_major}: oldestAcceptedProtocolMajor is ${oldest}. Keep oldestAcceptedProtocolMajor at ${previous_major} or lower until no Device speaks major ${previous_major}." >&2
  exit 1
fi

echo "Wire gate passes: protocolMajor ${previous_major} -> ${current_major}, the Server accepts ${oldest} to ${current_major}."
