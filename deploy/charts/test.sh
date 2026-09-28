#!/usr/bin/env bash
# Static checks of the Helm charts (Story 2.2). No cluster needed. Run from anywhere:
#
#   deploy/charts/test.sh
#
# For every chart: helm lint --strict, helm unittest, and helm template (default values and, when
# present, ci-values.yaml) validated by kubeconform in strict mode against the Kubernetes 1.36.0
# schemas, then checked by check-manifests.py: no rendered Secret, and no Secret name or key that
# deploy/SECRETS.md does not list. The checks are proven able to fail on fixtures in testdata/, and
# check-version.sh is tested with a matching and a mismatching version.
#
# Needs helm (v4), the helm-unittest plugin, kubeconform and python3 with PyYAML. kubeconform
# downloads the schemas; KUBECONFORM_CACHE names a directory to cache them in.
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo=$(cd "${here}/../.." && pwd)
contract=${repo}/deploy/SECRETS.md
kubernetes_version=1.36.0
charts=(nats temporal keycloak server web)
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

# check <name> <command...>: passes when the command exits 0; prints its output otherwise.
check() {
  local name=$1
  shift
  local output
  if output=$("$@" 2>&1); then
    pass "${name}"
  else
    failed "${name}" "${output}"
  fi
}

# expect_failure <name> <expected stderr substring> <command...>: passes when the command exits
# non-zero and its output contains the substring.
expect_failure() {
  local name=$1 want=$2
  shift 2
  local output
  if output=$("$@" 2>&1); then
    failed "${name}" "exit 0, expected a failure" "${output}"
  elif [[ ${output} != *"${want}"* ]]; then
    failed "${name}" "output does not contain '${want}'" "${output}"
  else
    pass "${name}"
  fi
}

kubeconform_args=(-strict -summary -kubernetes-version "${kubernetes_version}")
if [[ -n ${KUBECONFORM_CACHE:-} ]]; then
  mkdir -p "${KUBECONFORM_CACHE}"
  kubeconform_args+=(-cache "${KUBECONFORM_CACHE}")
fi

# render_and_check <chart> <label> [helm template arguments...]
render_and_check() {
  local chart=$1 label=$2
  shift 2
  local rendered errors
  errors=$(mktemp)
  if ! rendered=$(helm template "${chart}" "${here}/${chart}" "$@" 2>"${errors}"); then
    failed "${label}: helm template" "$(cat "${errors}")"
    rm -f "${errors}"
    return
  fi
  rm -f "${errors}"
  pass "${label}: helm template"
  check "${label}: kubeconform" kubeconform "${kubeconform_args[@]}" - <<<"${rendered}"
  check "${label}: Secret contract" python3 "${here}/check-manifests.py" "${contract}" "${label}" <<<"${rendered}"
}

check_fixture() {
  python3 "${here}/check-manifests.py" "${contract}" "$1" <"${here}/testdata/$1"
}

echo "# the Secret check can fail"
expect_failure "a rendered Secret fails"            "charts must not render a Secret" check_fixture renders-secret.yaml
expect_failure "an undocumented Secret fails"       "Secret 'coldframe-undocumented'" check_fixture unknown-secret.yaml
expect_failure "an undocumented key fails"          "key 'connection-string' of Secret 'coldframe-db-server'" check_fixture unknown-key.yaml
expect_failure "an optional Secret reference fails"  "is optional" check_fixture optional-secret.yaml
check          "documented Secrets and keys pass"   check_fixture documented-secrets.yaml

echo "# check-version.sh"
current=$(sed -n -E 's/^version:[[:space:]]*"?([^"[:space:]]*)"?[[:space:]]*$/\1/p' "${here}/server/Chart.yaml")
other="${current}-mismatch"
check          "matching version passes"            "${here}/check-version.sh" "${current}"
expect_failure "mismatching version names the chart" "server/Chart.yaml: version is '${current}', expected '${other}'" \
  "${here}/check-version.sh" "${other}"
expect_failure "mismatching appVersion names the chart" "keycloak/Chart.yaml: appVersion is '${current}', expected '${other}'" \
  "${here}/check-version.sh" "${other}"

for chart in "${charts[@]}"; do
  echo "# ${chart}"
  dir=${here}/${chart}
  check "${chart}: helm lint --strict" helm lint --strict "${dir}"
  render_and_check "${chart}" "${chart} (default values)"
  if [[ -f ${dir}/ci-values.yaml ]]; then
    check "${chart}: helm lint --strict (CI values)" helm lint --strict "${dir}" --values "${dir}/ci-values.yaml"
    render_and_check "${chart}" "${chart} (CI values)" --values "${dir}/ci-values.yaml"
  fi
  check "${chart}: helm unittest" helm unittest --strict "${dir}"
done

echo "# ${passes} passed, ${failures} failed"
[[ ${failures} -eq 0 ]]
