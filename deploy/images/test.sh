#!/usr/bin/env bash
# Plain-bash tests for the image release scripts. No Docker needed: platforms.sh reads fixture
# manifests from testdata/. Run from anywhere:
#
#   deploy/images/test.sh
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo=$(cd "${here}/../.." && pwd)
data=${here}/testdata
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

# expect <name> <expected status> <expected stdout> <expected stderr> -- <command...>
# The expected stderr is a substring, "" for no stderr at all, or - for anything.
expect() {
  local name=$1 want_status=$2 want_out=$3 want_err=$4
  shift 5
  local out err status
  err=$(mktemp)
  out=$("$@" 2>"${err}")
  status=$?
  local err_text
  err_text=$(cat "${err}")
  rm -f "${err}"

  if [[ ${status} -ne ${want_status} ]]; then
    failed "${name}" "exit ${status}, expected ${want_status}" "stdout: ${out}" "stderr: ${err_text}"
  elif [[ ${out} != "${want_out}" ]]; then
    failed "${name}" "stdout '${out}', expected '${want_out}'"
  elif [[ -z ${want_err} && -n ${err_text} ]]; then
    failed "${name}" "stderr '${err_text}', expected none"
  elif [[ -n ${want_err} && ${want_err} != "-" && ${err_text} != *"${want_err}"* ]]; then
    failed "${name}" "stderr '${err_text}' does not contain '${want_err}'"
  else
    pass "${name}"
  fi
}

release_version=${here}/release-version.sh
platforms=${here}/platforms.sh

echo "# release-version.sh"
expect "release tag"            0 "1.2.3"       -          -- "${release_version}" v1.2.3
expect "zero components"        0 "0.0.0"       -          -- "${release_version}" v0.0.0
expect "pre-release tag"        0 "1.2.3-rc.1"  -          -- "${release_version}" v1.2.3-rc.1
expect "two components fail"    1 ""            "'v1.2'"   -- "${release_version}" v1.2
expect "not a version fails"    1 ""            "'vfoo'"   -- "${release_version}" vfoo
expect "missing v fails"        1 ""            "'1.2.3'"  -- "${release_version}" 1.2.3
expect "leading zero fails"     1 ""            "'v01.2.3'" -- "${release_version}" v01.2.3
expect "build metadata fails"   1 ""            "'v1.2.3+b1'" -- "${release_version}" v1.2.3+b1
expect "empty pre-release fails" 1 ""           "'v1.2.3-'" -- "${release_version}" v1.2.3-
expect "pre-release leading zero fails" 1 ""    "'v1.2.3-01'" -- "${release_version}" v1.2.3-01
expect "empty pre-release identifier fails" 1 "" "'v1.2.3-rc..1'" -- "${release_version}" v1.2.3-rc..1
expect "pre-release of dots fails" 1 ""         "'v1.2.3-.'" -- "${release_version}" v1.2.3-.
expect "numeric zero identifier"  0 "1.2.3-rc.0" -        -- "${release_version}" v1.2.3-rc.0
expect "alphanumeric with leading digit" 0 "1.2.3-0a" -   -- "${release_version}" v1.2.3-0a

echo "# platforms.sh"
expect "multi-arch manifest list" 0 "linux/amd64,linux/arm64" "" \
  -- "${platforms}" "${data}/Dockerfile" "${data}/multi-arch.json"
expect "amd64-only reports missing arm64" 0 "linux/amd64" "missing:linux/arm64" \
  -- "${platforms}" "${data}/Dockerfile" "${data}/amd64-only.json"
expect "no matching platform fails naming the final image" 1 "" "example.org/final:2.0 provides none" \
  -- "${platforms}" "${data}/Dockerfile" "${data}/no-match.json"
expect "single image configuration" 0 "linux/arm64" "missing:linux/amd64" \
  -- "${platforms}" "${data}/Dockerfile" "${data}/single-image-config.json"
expect "final FROM from a build argument fails" 2 "" "explicit image" \
  -- "${platforms}" "${data}/Dockerfile.argument" "${data}/multi-arch.json"
expect "missing Dockerfile fails" 2 "" "No such Dockerfile" \
  -- "${platforms}" "${data}/Dockerfile.absent" "${data}/multi-arch.json"

# The rules every component Dockerfile follows (deploy/images/README.md): build stages run on the
# build platform, and the final stage never runs a command, so arm64 builds need no emulation.
echo "# component Dockerfiles"
for dockerfile in apps/cs/server/Dockerfile apps/cs/migrations/Dockerfile apps/ts/web/Dockerfile aspire/keycloak/Dockerfile; do
  path=${repo}/${dockerfile}
  final_runs=$(awk 'toupper($1) == "FROM" { n = 0; final = NR } toupper($1) == "RUN" && final { n++ } END { print n + 0 }' "${path}")
  if [[ ${final_runs} -eq 0 ]]; then
    pass "${dockerfile}: no RUN in the final stage"
  else
    failed "${dockerfile}: no RUN in the final stage" "found ${final_runs}"
  fi

  stray=$(awk 'toupper($1) == "FROM" { froms[++n] = $0 } END { for (i = 1; i < n; i++) if (froms[i] !~ /--platform=\$BUILDPLATFORM/) print froms[i] }' "${path}")
  if [[ -z ${stray} ]]; then
    pass "${dockerfile}: build stages use --platform=\$BUILDPLATFORM"
  else
    failed "${dockerfile}: build stages use --platform=\$BUILDPLATFORM" "${stray}"
  fi

  if grep -qE '^USER [0-9]+$' "${path}"; then
    pass "${dockerfile}: numeric USER"
  else
    failed "${dockerfile}: numeric USER"
  fi
done

# The Keycloak Dockerfile names the Phase Two image in the build stage and the final stage; the
# Quarkus build output is only valid for the same version.
keycloak_dockerfile=aspire/keycloak/Dockerfile
keycloak_tags=$(grep -oE 'phasetwo-keycloak:[^[:space:]]+' "${repo}/${keycloak_dockerfile}" | sort -u)
if [[ -z ${keycloak_tags} ]]; then
  failed "${keycloak_dockerfile}: every phasetwo-keycloak FROM has the same tag" "no phasetwo-keycloak image found"
elif [[ $(wc -l <<<"${keycloak_tags}") -eq 1 ]]; then
  pass "${keycloak_dockerfile}: every phasetwo-keycloak FROM has the same tag"
else
  failed "${keycloak_dockerfile}: every phasetwo-keycloak FROM has the same tag" "${keycloak_tags}"
fi

echo "# ${passes} passed, ${failures} failed"
[[ ${failures} -eq 0 ]]
