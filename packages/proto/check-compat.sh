#!/usr/bin/env bash
# Wire-contract compatibility check (AD-10, AD-24): `buf lint` on packages/proto, then `buf breaking`
# (FILE rules) and `oasdiff breaking --fail-on ERR` against a baseline. Exits non-zero on a lint error
# or a breaking change. A missing baseline (the contract did not exist yet) passes with a notice.
# An operation that carries `x-coldframe-planned` in the baseline is in the contract ahead of the
# Server and was never served, so it is left out of the OpenAPI comparison: replacing it is not a break.
#
# Usage:
#   packages/proto/check-compat.sh [--base <git-ref>]   compare the working tree with <git-ref>
#                                                       (default: the merge base with origin/main)
#   packages/proto/check-compat.sh --self-test          prove that breaking changes fail
#
# Explicit inputs (used by the self-test): --proto-dir, --proto-baseline-dir, --openapi,
# --openapi-baseline. Tools: $BUF and $OASDIFF, default `buf` and `oasdiff` on PATH
# (packages/proto/install-tools.sh installs the pinned versions), and python3.
set -euo pipefail

BUF="${BUF:-buf}"
OASDIFF="${OASDIFF:-oasdiff}"
root="$(git -C "$(dirname "${BASH_SOURCE[0]}")" rev-parse --show-toplevel)"
script="${root}/packages/proto/check-compat.sh"

notice() {
  if [[ -n "${GITHUB_ACTIONS:-}" ]]; then echo "::notice::$*"; else echo "notice: $*"; fi
}

self_test() {
  local work failures=0
  work="$(mktemp -d)"
  trap 'rm -rf "${work}"' RETURN

  expect() {
    local want="$1" name="$2"
    shift 2
    local status=0
    "$@" >"${work}/out.txt" 2>&1 || status=$?
    if [[ "${want}" == pass && ${status} -ne 0 ]] || [[ "${want}" == fail && ${status} -eq 0 ]]; then
      echo "self-test FAILED: ${name} (exit ${status}, expected ${want})"
      sed 's/^/  | /' "${work}/out.txt"
      failures=$((failures + 1))
    else
      echo "self-test ok: ${name} (exit ${status})"
    fi
  }

  local proto="${root}/packages/proto" openapi="${root}/packages/openapi/coldframe.openapi.json"

  expect pass "the tree is compatible with itself" \
    "${script}" --proto-dir "${proto}" --proto-baseline-dir "${proto}" --openapi "${openapi}" --openapi-baseline "${openapi}"

  # A removed Protobuf field.
  cp -R "${proto}" "${work}/proto-removed-field"
  sed -i '/bytes ciphertext = 4;/d' "${work}/proto-removed-field/coldframe/device/v1/envelope.proto"
  if cmp -s "${proto}/coldframe/device/v1/envelope.proto" "${work}/proto-removed-field/coldframe/device/v1/envelope.proto"; then
    echo "self-test FAILED: could not remove SealedEnvelope.ciphertext"
    failures=$((failures + 1))
  fi
  expect fail "a removed .proto field fails" \
    "${script}" --proto-dir "${work}/proto-removed-field" --proto-baseline-dir "${proto}" --openapi "${openapi}" --openapi-baseline "${openapi}"

  # A removed OpenAPI operation, and an optional request property made required.
  python3 - "${openapi}" "${work}" <<'PY'
import json, sys
source, work = sys.argv[1], sys.argv[2]
contract = json.load(open(source))
del contract["paths"]["/sites/{siteId}/lots/{lotId}"]["delete"]
json.dump(contract, open(f"{work}/removed-operation.json", "w"))
contract = json.load(open(source))
contract["components"]["schemas"]["HeartbeatRequest"]["required"].append("uptimeMs")
json.dump(contract, open(f"{work}/newly-required.json", "w"))
# The same operation, planned in the baseline: in the contract ahead of the Server.
contract = json.load(open(source))
contract["paths"]["/sites/{siteId}/lots/{lotId}"]["delete"]["x-coldframe-planned"] = "9.9"
json.dump(contract, open(f"{work}/planned-baseline.json", "w"))
del contract["paths"]["/sites/{siteId}/lots/{lotId}"]["delete"]["responses"]["204"]
contract["paths"]["/sites/{siteId}/lots/{lotId}"]["delete"]["responses"]["202"] = {"description": "Replaced."}
json.dump(contract, open(f"{work}/planned-replaced.json", "w"))
PY
  expect fail "a removed OpenAPI operation fails" \
    "${script}" --proto-dir "${proto}" --proto-baseline-dir "${proto}" --openapi "${work}/removed-operation.json" --openapi-baseline "${openapi}"
  expect fail "a request property made required fails" \
    "${script}" --proto-dir "${proto}" --proto-baseline-dir "${proto}" --openapi "${work}/newly-required.json" --openapi-baseline "${openapi}"

  # An operation planned in the baseline may be removed or replaced; the same change to a served one fails.
  expect pass "a removed planned operation passes" \
    "${script}" --proto-dir "${proto}" --proto-baseline-dir "${proto}" --openapi "${work}/removed-operation.json" --openapi-baseline "${work}/planned-baseline.json"
  if ! grep -q "planned in the baseline" "${work}/out.txt"; then
    echo "self-test FAILED: a planned operation printed no notice"
    failures=$((failures + 1))
  fi
  expect pass "a replaced planned operation passes" \
    "${script}" --proto-dir "${proto}" --proto-baseline-dir "${proto}" --openapi "${work}/planned-replaced.json" --openapi-baseline "${work}/planned-baseline.json"
  expect fail "the same replacement of a served operation fails" \
    "${script}" --proto-dir "${proto}" --proto-baseline-dir "${proto}" --openapi "${work}/planned-replaced.json" --openapi-baseline "${openapi}"

  # No baseline at all.
  mkdir -p "${work}/empty"
  expect pass "a missing baseline passes" \
    "${script}" --proto-dir "${proto}" --proto-baseline-dir "${work}/empty" --openapi "${openapi}" --openapi-baseline "${work}/absent.json"
  if ! grep -q "no baseline" "${work}/out.txt"; then
    echo "self-test FAILED: a missing baseline printed no notice"
    failures=$((failures + 1))
  fi

  # --base mode extracts a real baseline from git: HEAD has both contracts.
  expect pass "--base HEAD compares with a real baseline" "${script}" --base HEAD
  if ! grep -q "^Baseline: HEAD" "${work}/out.txt" || grep -q "no baseline" "${work}/out.txt"; then
    echo "self-test FAILED: --base HEAD did not compare with the committed contracts"
    sed 's/^/  | /' "${work}/out.txt"
    failures=$((failures + 1))
  fi

  if [[ ${failures} -gt 0 ]]; then
    echo "${failures} self-test check(s) failed."
    return 1
  fi
  echo "All self-test checks passed."
}

base=""
proto_dir="${root}/packages/proto"
openapi="${root}/packages/openapi/coldframe.openapi.json"
proto_baseline=""
openapi_baseline=""
explicit=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --self-test) self_test; exit $? ;;
    --base) base="$2"; shift 2 ;;
    --proto-dir) proto_dir="$2"; shift 2 ;;
    --openapi) openapi="$2"; shift 2 ;;
    --proto-baseline-dir) proto_baseline="$2"; explicit=true; shift 2 ;;
    --openapi-baseline) openapi_baseline="$2"; explicit=true; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT

if [[ "${explicit}" == false ]]; then
  if [[ -z "${base}" ]]; then
    base="$(git -C "${root}" merge-base HEAD origin/main 2>/dev/null || true)"
  fi
  if [[ -z "${base}" ]] || ! git -C "${root}" rev-parse --verify --quiet "${base}^{commit}" >/dev/null; then
    echo "No baseline commit (pass --base <git-ref>, or fetch origin/main)." >&2
    exit 2
  fi
  echo "Baseline: ${base}"
  # Only true absence at the baseline means "no baseline"; any other extraction failure fails the check.
  proto_baseline="${work}/baseline/packages/proto"
  if git -C "${root}" cat-file -e "${base}:packages/proto" 2>/dev/null; then
    mkdir -p "${work}/baseline"
    git -C "${root}" archive --format=tar --output="${work}/baseline.tar" "${base}" -- packages/proto
    tar -x -f "${work}/baseline.tar" -C "${work}/baseline"
  fi
  openapi_baseline="${work}/baseline.openapi.json"
  if git -C "${root}" cat-file -e "${base}:packages/openapi/coldframe.openapi.json" 2>/dev/null; then
    git -C "${root}" show "${base}:packages/openapi/coldframe.openapi.json" >"${openapi_baseline}"
  fi
fi

echo "== buf lint"
(cd "${proto_dir}" && "${BUF}" lint)

echo "== buf breaking (FILE)"
if [[ -d "${proto_baseline}" ]] && find "${proto_baseline}" -name '*.proto' -print -quit | grep -q .; then
  (cd "${proto_dir}" && "${BUF}" breaking --against "${proto_baseline}")
  echo "No breaking Protobuf change."
else
  notice "packages/proto has no baseline .proto files; nothing to compare, the check passes."
fi

echo "== oasdiff breaking"
if [[ -f "${openapi_baseline}" ]]; then
  # Operations planned in the baseline were never served: they leave both sides of the comparison.
  planned="$(python3 - "${openapi_baseline}" "${openapi}" "${work}/served-baseline.json" "${work}/served.json" <<'PY'
import json, sys

baseline_path, current_path, baseline_out, current_out = sys.argv[1:5]
methods = ("get", "put", "post", "delete", "patch", "head", "options", "trace")
baseline = json.load(open(baseline_path))
current = json.load(open(current_path))
planned = [
    (path, method)
    for path, item in baseline.get("paths", {}).items()
    for method in methods
    if isinstance(item.get(method), dict) and "x-coldframe-planned" in item[method]
]
for contract in (baseline, current):
    for path, method in planned:
        item = contract.get("paths", {}).get(path)
        if item is not None:
            item.pop(method, None)
            if not any(key in item for key in methods):
                del contract["paths"][path]
json.dump(baseline, open(baseline_out, "w"))
json.dump(current, open(current_out, "w"))
print(", ".join(f"{method.upper()} {path}" for path, method in planned))
PY
)"
  if [[ -n "${planned}" ]]; then
    notice "planned in the baseline, so not compared: ${planned}"
  fi
  "${OASDIFF}" breaking "${work}/served-baseline.json" "${work}/served.json" --fail-on ERR
  echo "No breaking OpenAPI change."
else
  notice "packages/openapi has no baseline contract; nothing to compare, the check passes."
fi
