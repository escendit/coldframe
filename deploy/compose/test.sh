#!/usr/bin/env bash
# Plain-bash tests for the Compose reference example (Story 10.3): `docker compose config` passes
# with .env.example, and fails naming the variable when any one `${NAME:?…}` guard of compose.yaml
# is left out of the settings. Needs Docker Compose; starts nothing. Run from anywhere:
#
#   deploy/compose/test.sh
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
work=$(mktemp -d)
trap 'rm -rf "${work}"' EXIT
failures=0
passes=0

pass() {
  passes=$((passes + 1))
  echo "ok   - $1"
}

failed() {
  failures=$((failures + 1))
  echo "FAIL - $1" >&2
  shift
  for line in "$@"; do
    echo "       ${line}" >&2
  done
}

compose=${here}/compose.yaml
example=${here}/.env.example

# config <env file> [-u NAME]: docker compose config, without NAME in the process environment.
config() {
  local env_file=$1
  shift
  env "$@" docker compose -f "${compose}" --env-file "${env_file}" config --quiet
}

if err=$(config "${example}" 2>&1); then
  pass ".env.example passes docker compose config"
else
  failed ".env.example passes docker compose config" "${err}"
fi

mapfile -t guarded < <(grep -oE '\$\{[A-Z_][A-Z0-9_]*:\?' "${compose}" | sed -E 's/^\$\{//; s/:\?$//' | sort -u)
if [[ ${#guarded[@]} -gt 0 ]]; then
  pass "compose.yaml has ${#guarded[@]} required variables"
else
  failed "compose.yaml has required variables" "no \${NAME:? guard found"
fi

for name in "${guarded[@]}"; do
  grep -v "^${name}=" "${example}" > "${work}/without.env"
  if ! grep -q "^${name}=" "${example}"; then
    failed "${name} is in .env.example" "compose.yaml requires it, .env.example does not set it"
    continue
  fi
  if err=$(config "${work}/without.env" -u "${name}" 2>&1); then
    failed "a missing ${name} fails config" "config passed without ${name}"
  elif [[ ${err} == *"${name}"* ]]; then
    pass "a missing ${name} fails config naming it"
  else
    failed "a missing ${name} fails config naming it" "${err}"
  fi
done

echo
echo "${passes} passed, ${failures} failed"
[[ ${failures} -eq 0 ]]
