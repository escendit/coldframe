#!/usr/bin/env bash
# Static checks of the Helm charts and their Fleet bundles (Stories 2.2 to 2.5a). No cluster
# needed. Run from anywhere:
#
#   deploy/charts/test.sh
#
# For every chart: helm lint --strict, helm unittest, and helm template (default values and, when
# present, ci-values.yaml) validated by kubeconform in strict mode against the Kubernetes 1.36.0
# schemas and, for the CloudNativePG, Barman Cloud and cert-manager kinds, against strict JSON
# schemas generated (crd-schemas.py) from the CRD manifests pinned in dependencies.env, then checked
# by check-manifests.py (no rendered Secret, and no Secret name or key that deploy/SECRETS.md does
# not list) and check-ingress.py (TLS on every Ingress and IngressRoute, DNS-01 on every ACME
# issuer, nothing on port 80). The pinned rke2-traefik chart is rendered too: with its default
# values it must fail check-ingress.py (port 80), with deploy/rke2/rke2-traefik-config.yaml pass.
# The checks are proven able to fail on fixtures in testdata/, and check-version.sh is tested with a
# matching and a mismatching version.
#
# Fleet bundles: `fleet apply --output -` renders the paths of deploy/fleet/gitrepo.yaml as gitjob
# would, and deploy/fleet/check-fleet.py checks them (labels, the dependsOn order, the pins of
# dependencies.env, the site values); each fixture in deploy/fleet/testdata/ must make it fail.
# Every chart is also rendered and checked with its fleet.yaml values and its key of
# deploy/fleet/values.example.yaml. The Fleet smoke (deploy/fleet/smoke.sh) must refuse bad usage
# (image count, SMOKE_CLUSTER, fleet CLI version) before it creates a cluster.
#
# Needs helm (v4), the helm-unittest plugin, kubeconform, the fleet CLI (FLEET_VERSION of
# dependencies.env), curl, sha256sum and python3 with PyYAML. kubeconform downloads the Kubernetes
# schemas, this script the pinned CRD manifests and the rke2-traefik chart (each checked against
# its sha256), and fleet the operator charts; KUBECONFORM_CACHE names a directory to cache the
# first two in.
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo=$(cd "${here}/../.." && pwd)
contract=${repo}/deploy/SECRETS.md
kubernetes_version=1.36.0
charts=(database nats temporal keycloak server web ingress)
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

# shellcheck source=SCRIPTDIR/dependencies.env
source "${here}/dependencies.env"

kubeconform_args=(-strict -summary -kubernetes-version "${kubernetes_version}")
if [[ -n ${KUBECONFORM_CACHE:-} ]]; then
  mkdir -p "${KUBECONFORM_CACHE}"
  kubeconform_args+=(-cache "${KUBECONFORM_CACHE}")
  crd_cache=${KUBECONFORM_CACHE}/crds
else
  crd_cache=$(mktemp -d)
  trap 'rm -rf "${crd_cache}"' EXIT
fi
crd_schemas=${crd_cache}/schemas-${CNPG_VERSION}-${BARMAN_CLOUD_PLUGIN_VERSION}-${CERT_MANAGER_VERSION}

# fetch <file> <url> <sha256>: downloads a pinned manifest into the CRD cache (once) and checks it.
fetch() {
  local file=${crd_cache}/$1 url=$2 sha256=$3
  if [[ ! -f ${file} ]]; then
    curl --fail --silent --show-error --location --output "${file}.part" "${url}" || return 1
    mv "${file}.part" "${file}"
  fi
  if ! echo "${sha256}  ${file}" | sha256sum --check --strict --quiet; then
    rm -f "${file}"
    echo "${url}: sha256 mismatch" >&2
    return 1
  fi
}

echo "# pinned CRD schemas"
mkdir -p "${crd_cache}"
if fetch "cnpg-${CNPG_VERSION}.yaml" "${CNPG_URL}" "${CNPG_SHA256}" \
  && fetch "barman-cloud-${BARMAN_CLOUD_PLUGIN_VERSION}.yaml" "${BARMAN_CLOUD_PLUGIN_URL}" "${BARMAN_CLOUD_PLUGIN_SHA256}" \
  && fetch "cert-manager-${CERT_MANAGER_VERSION}.yaml" "${CERT_MANAGER_URL}" "${CERT_MANAGER_SHA256}"; then
  pass "CNPG ${CNPG_VERSION}, Barman Cloud plugin ${BARMAN_CLOUD_PLUGIN_VERSION} and cert-manager ${CERT_MANAGER_VERSION} manifests match their sha256"
else
  failed "download the pinned CRD manifests"
fi
rm -rf "${crd_schemas}"
check "generate strict schemas from the CRDs" python3 "${here}/crd-schemas.py" "${crd_schemas}" \
  < <(cat "${crd_cache}/cnpg-${CNPG_VERSION}.yaml" "${crd_cache}/barman-cloud-${BARMAN_CLOUD_PLUGIN_VERSION}.yaml" \
    "${crd_cache}/cert-manager-${CERT_MANAGER_VERSION}.yaml" 2>/dev/null)
kubeconform_args+=(-schema-location default -schema-location "${crd_schemas}/{{.ResourceKind}}_{{.ResourceAPIVersion}}.json")

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
  check "${label}: TLS, DNS-01, no port 80" python3 "${here}/check-ingress.py" "${label}" <<<"${rendered}"
}

check_fixture() {
  python3 "${here}/check-manifests.py" "${contract}" "$1" <"${here}/testdata/$1"
}

echo "# the Secret check can fail"
expect_failure "a rendered Secret fails"            "charts must not render a Secret" check_fixture renders-secret.yaml
expect_failure "an undocumented Secret fails"       "Secret 'coldframe-undocumented'" check_fixture unknown-secret.yaml
expect_failure "an undocumented key fails"          "key 'connection-string' of Secret 'coldframe-db-coldframe'" check_fixture unknown-key.yaml
expect_failure "an optional Secret reference fails"  "is optional" check_fixture optional-secret.yaml
check          "documented Secrets and keys pass"   check_fixture documented-secrets.yaml
expect_failure "an undocumented S3 credential key fails" "key 'region' of Secret 'coldframe-backup-s3'" check_fixture unknown-s3-key.yaml
expect_failure "an undocumented role passwordSecret fails" "Secret 'coldframe-db-reporting'" check_fixture unknown-role-secret.yaml
check          "documented CNPG references pass"    check_fixture documented-cnpg.yaml
expect_failure "an undocumented DNS-01 token key fails" "key 'api-key' of Secret 'coldframe-dns01'" check_fixture unknown-dns01-key.yaml

# CloudNativePG creates these Secrets for the cluster coldframe-db, and cert-manager those of the
# ingress chart (the certificate and the ACME account key); a contract Secret of the same name
# would stop the cluster ("missing tls.key secret data") or be overwritten.
check "no contract Secret takes a name CloudNativePG or cert-manager generates" python3 - "${contract}" <<'PYTHON'
import re, sys
names = set(re.findall(r"^\| `([a-z0-9-]+)` \|", open(sys.argv[1], encoding="utf-8").read(), re.M))
generated = {f"coldframe-db-{suffix}" for suffix in ("ca", "server", "replication", "app", "superuser")}
generated |= {"coldframe-tls", "coldframe-letsencrypt-account"}
clashes = sorted(names & generated)
if clashes:
    sys.exit(f"SECRETS.md lists Secrets that CloudNativePG or cert-manager generates: {', '.join(clashes)}")
PYTHON

check_ingress_fixture() {
  python3 "${here}/check-ingress.py" "$1" <"${here}/testdata/$1"
}

echo "# the TLS, DNS-01 and port-80 check can fail"
expect_failure "an Ingress without tls fails"            "Ingress/no-tls: has no tls" check_ingress_fixture ingress-no-tls.yaml
expect_failure "an Ingress with a host outside tls fails" "is not covered by tls" check_ingress_fixture ingress-partial-tls.yaml
expect_failure "an Ingress without router.tls fails"     "router.tls is '', expected 'true'" check_ingress_fixture ingress-no-router-tls.yaml
expect_failure "an Ingress on the web entrypoint fails"  "router.entrypoints is 'web,websecure', expected 'websecure'" \
  check_ingress_fixture ingress-web-entrypoint.yaml
expect_failure "an IngressRoute on web without tls fails" "IngressRoute/web-route: has no spec.tls" check_ingress_fixture ingressroute-web.yaml
expect_failure "an IngressRoute on web fails"            "entryPoints are ['web'], expected ['websecure']" check_ingress_fixture ingressroute-web.yaml
expect_failure "an HTTP-01 solver fails"                 "Issuer/http01: solver 0 uses http01" check_ingress_fixture issuer-http01.yaml
expect_failure "a Service on port 80 fails"              "Service/port-80: port 'http' has port 80" check_ingress_fixture service-port-80.yaml
check          "HTTPS-only routes and a DNS-01 issuer pass" check_ingress_fixture documented-ingress.yaml
check          "a documented Issuer passes the Secret check" check_fixture documented-ingress.yaml

echo "# RKE2's Traefik (rke2-traefik ${RKE2_TRAEFIK_VERSION})"
traefik_chart=${crd_cache}/rke2-traefik-${RKE2_TRAEFIK_VERSION}.tgz
traefik_values=${crd_cache}/rke2-traefik-values.yaml
if fetch "rke2-traefik-${RKE2_TRAEFIK_VERSION}.tgz" "${RKE2_TRAEFIK_URL}" "${RKE2_TRAEFIK_SHA256}"; then
  pass "rke2-traefik ${RKE2_TRAEFIK_VERSION} matches its sha256"
else
  failed "download the pinned rke2-traefik chart"
fi
# render_traefik [helm template arguments...]: the chart as helm-controller installs it in kube-system.
render_traefik() {
  helm template rke2-traefik "${traefik_chart}" --namespace kube-system "$@"
}
check_traefik() {
  local label=$1 rendered
  shift
  rendered=$(render_traefik "$@") || return 1
  python3 "${here}/check-ingress.py" "${label}" <<<"${rendered}"
}
expect_failure "the RKE2 default values fail (hostPort 80)" "port 'web' has hostPort 80" \
  check_traefik "rke2-traefik (default values)"
expect_failure "the RKE2 default values fail (web entrypoint)" "configures the web entrypoint" \
  check_traefik "rke2-traefik (default values)"
check "the valuesContent of deploy/rke2/rke2-traefik-config.yaml is a HelmChartConfig for rke2-traefik" \
  python3 - "${repo}/deploy/rke2/rke2-traefik-config.yaml" "${traefik_values}" <<'PYTHON'
import sys, yaml
docs = [doc for doc in yaml.safe_load_all(open(sys.argv[1], encoding="utf-8")) if doc]
if len(docs) != 1:
    sys.exit(f"expected one document, found {len(docs)}")
doc = docs[0]
if (doc.get("apiVersion"), doc.get("kind")) != ("helm.cattle.io/v1", "HelmChartConfig"):
    sys.exit(f"expected a helm.cattle.io/v1 HelmChartConfig, found {doc.get('apiVersion')} {doc.get('kind')}")
if (doc["metadata"].get("name"), doc["metadata"].get("namespace")) != ("rke2-traefik", "kube-system"):
    sys.exit("expected metadata.name rke2-traefik in namespace kube-system")
values = yaml.safe_load(doc["spec"]["valuesContent"])
if not isinstance(values, dict):
    sys.exit("valuesContent is not a YAML mapping")
open(sys.argv[2], "w", encoding="utf-8").write(doc["spec"]["valuesContent"])
PYTHON
check "rke2-traefik with deploy/rke2/rke2-traefik-config.yaml passes" \
  check_traefik "rke2-traefik (rke2-traefik-config.yaml)" --values "${traefik_values}"


echo "# the CRD schemas are strict"
expect_failure "an unknown Cluster field fails"    "additional properties 'backupz' not allowed" \
  kubeconform "${kubeconform_args[@]}" "${here}/testdata/unknown-cnpg-field.yaml"
check          "a valid Cluster passes"            kubeconform "${kubeconform_args[@]}" "${here}/testdata/documented-cnpg.yaml"
expect_failure "an unknown Issuer field fails"     "additional properties 'apiTokn' not allowed" \
  kubeconform "${kubeconform_args[@]}" "${here}/testdata/unknown-issuer-field.yaml"

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

echo "# database recovery mode"
render_and_check database "database (recovery mode)" --values "${here}/database/ci-values.yaml" \
  --set recovery.enabled=true --set backup.serverName=coldframe-db-restored
expect_failure "recovery into the source's archive folder fails" \
  "backup.serverName (coldframe-db) must differ from recovery.sourceServerName (coldframe-db)" \
  helm template database "${here}/database" --set recovery.enabled=true

echo "# server with push notifications"
# Push is optional (Story 6.5): the default render references no coldframe-push key, and an enabled
# provider references only its own keys of the contract, never as an optional reference.
render_and_check server "server (push enabled)" --set push.apns.enabled=true --set push.fcm.enabled=true
check "server: the default render reads nothing of coldframe-push" \
  bash -c "! helm template server '${here}/server' | grep -q coldframe-push"
expect_failure "APNs without a topic fails naming the value" \
  "push.apns.topic is required" \
  helm template server "${here}/server" --set push.apns.enabled=true --set push.apns.topic=

echo "# ingress without ACME"
render_and_check ingress "ingress (external issuer)" --set acme.enabled=false --set externalIssuer.name=my-ca
expect_failure "no ACME and no external issuer fails naming both values" \
  "acme.enabled is false and externalIssuer.name is empty" \
  helm template ingress "${here}/ingress" --set acme.enabled=false

echo "# fleet bundles (fleet ${FLEET_VERSION}, deploy/fleet)"
fleet_dir=${repo}/deploy/fleet
gitrepo=${fleet_dir}/gitrepo.yaml
values_example=${fleet_dir}/values.example.yaml
fleet_work=$(mktemp -d)
bundles=${fleet_work}/bundles.yaml
# The GitRepo's name and paths: fleet apply names each bundle <name>-<path>, as gitjob does.
fleet_name=$(python3 -c 'import sys, yaml; print(yaml.safe_load(open(sys.argv[1]))["metadata"]["name"])' "${gitrepo}")
mapfile -t fleet_paths < <(python3 -c '
import sys, yaml
print("\n".join(yaml.safe_load(open(sys.argv[1]))["spec"]["paths"]))' "${gitrepo}")
fleet_cli_version=$(fleet --version 2>/dev/null | awk '{print $3}')
if [[ ${fleet_cli_version} != "${FLEET_VERSION}" ]]; then
  failed "the fleet CLI is ${FLEET_VERSION}" "found '${fleet_cli_version:-no fleet on PATH}'"
elif (cd "${repo}" && fleet apply --output - "${fleet_name}" "${fleet_paths[@]}") >"${bundles}" 2>"${fleet_work}/apply.err"; then
  pass "fleet apply renders the ${#fleet_paths[@]} paths of deploy/fleet/gitrepo.yaml"
else
  failed "fleet apply of the paths of deploy/fleet/gitrepo.yaml" "$(cat "${fleet_work}/apply.err")"
fi

check_fleet() { # [gitrepo.yaml] [values.yaml]: check-fleet.py over the rendered bundles (stdin)
  python3 "${fleet_dir}/check-fleet.py" "${repo}" "${1:-${gitrepo}}" "${2:-${values_example}}"
}
check "check-fleet.py passes on the repository" check_fleet <"${bundles}"

# fleet_fixture <fixture>: check-fleet.py over the repository's bundles with the fixture's Bundle
# documents merged in by name (maps merge, lists are replaced, null deletes; a new name is added),
# or with the fixture's GitRepo in place of gitrepo.yaml, or its ConfigMap in place of
# values.example.yaml.
fleet_fixture() {
  local fixture=${fleet_dir}/testdata/$1
  if grep -q '^kind: GitRepo' "${fixture}"; then
    check_fleet "${fixture}" <"${bundles}"
    return
  fi
  if grep -q '^kind: ConfigMap' "${fixture}"; then
    check_fleet "${gitrepo}" "${fixture}" <"${bundles}"
    return
  fi
  python3 - "${bundles}" "${fixture}" <<'PYTHON' | check_fleet
import re, sys, yaml

def merge(base, patch):
    for key, value in patch.items():
        if value is None:
            base.pop(key, None)
        elif isinstance(value, dict) and isinstance(base.get(key), dict):
            merge(base[key], value)
        else:
            base[key] = value

bundles = [doc for chunk in re.split(r"(?m)^(?=apiVersion: )", open(sys.argv[1]).read())
           for doc in yaml.safe_load_all(chunk) if doc]
by_name = {bundle["metadata"]["name"]: bundle for bundle in bundles}
for patch in (doc for doc in yaml.safe_load_all(open(sys.argv[2])) if doc):
    name = patch["metadata"]["name"]
    if name in by_name:
        merge(by_name[name], patch)
    else:
        bundles.append(patch)
yaml.safe_dump_all(bundles, sys.stdout)
PYTHON
}
expect_failure "a GitRepo path without fleet.yaml fails"    "GitRepo path 'deploy' has no fleet.yaml" fleet_fixture gitrepo-stray-path.yaml
expect_failure "an unlabeled bundle fails"                  "bundle 'coldframe-deploy-charts-nats' has no label" fleet_fixture unlabeled-bundle.yaml
expect_failure "a dependsOn by bundle name fails"           "names bundle 'coldframe-deploy-charts-server'" fleet_fixture dependson-by-name.yaml
expect_failure "a selector matching no bundle fails"        "matches 0 bundles" fleet_fixture dependson-no-match.yaml
expect_failure "a selector matching two bundles fails"      "matches 2 bundles" fleet_fixture dependson-ambiguous.yaml
expect_failure "a dependsOn cycle fails"                    "dependsOn has a cycle" fleet_fixture dependson-cycle.yaml
expect_failure "a missing order edge fails"                 "bundle server must depend on keycloak" fleet_fixture order-edge-missing.yaml
expect_failure "a chart version other than the pin fails"   "(CERT_MANAGER_CHART_VERSION in dependencies.env)" fleet_fixture pin-drift.yaml
expect_failure "cert-manager without the DNS-01 flags fails" "--dns01-recursive-nameservers=1.1.1.1:53,9.9.9.9:53' once" \
  fleet_fixture cert-manager-flags-missing.yaml
expect_failure "an image.tag in the bundle values fails"    "sets image.tag" fleet_fixture sets-image-tag.yaml
expect_failure "a site-specific bundle without valuesFrom fails" "bundle ingress: helm.valuesFrom must be" \
  fleet_fixture valuesfrom-missing.yaml
expect_failure "a GitRepo path listed twice fails"          "GitRepo path 'deploy/charts/web' is listed twice" fleet_fixture gitrepo-path-twice.yaml
expect_failure "a GitRepo revision other than a tag fails"  "revision 'main' is not a release tag" fleet_fixture gitrepo-revision-branch.yaml
expect_failure "a release name drift fails"                 "bundle server: helm.releaseName is 'coldframe-server'" fleet_fixture release-name-drift.yaml
expect_failure "a namespace drift fails"                    "bundle web: defaultNamespace is 'default'" fleet_fixture namespace-drift.yaml
expect_failure "an operator without takeOwnership fails"    "bundle cloudnative-pg: helm.takeOwnership must be true" fleet_fixture takeownership-missing.yaml
expect_failure "an operator Helm repo other than the pin fails" "(BARMAN_CLOUD_PLUGIN_HELM_REPO in dependencies.env)" \
  fleet_fixture repo-drift.yaml
expect_failure "a dependsOn edge the order does not list fails" "bundle nats depends on keycloak, which the order does not list" \
  fleet_fixture dependson-extra-edge.yaml
expect_failure "cert-manager without its CRDs fails"        "bundle cert-manager: helm.values.crds.enabled must be true" fleet_fixture crds-disabled.yaml
expect_failure "a matchExpressions selector fails"          "the selector must be matchLabels on" fleet_fixture dependson-match-expressions.yaml
expect_failure "a Coldframe chart from a Helm repo fails"   "bundle nats: expected the chart in its own folder" fleet_fixture chart-not-own-folder.yaml
expect_failure "valuesFrom on a bundle without site values fails" "bundle nats: has no site values and must not use helm.valuesFrom" \
  fleet_fixture valuesfrom-non-site.yaml
expect_failure "an image.tag in the values ConfigMap fails" "values ConfigMap key server: sets image.tag" fleet_fixture values-image-tag.yaml
expect_failure "a values ConfigMap without a key fails"     "values ConfigMap: keys are" fleet_fixture values-key-missing.yaml
expect_failure "a values ConfigMap of another name fails"   "expected the ConfigMap coldframe-values in namespace coldframe" \
  fleet_fixture values-wrong-name.yaml
shopt -s nullglob
for fixture in "${fleet_dir}"/testdata/*.yaml; do
  grep -q -F "fleet_fixture $(basename "${fixture}")" "${BASH_SOURCE[0]}" \
    || failed "deploy/fleet/testdata/$(basename "${fixture}") is not tested"
done
shopt -u nullglob

# Every Coldframe chart as Fleet deploys it: the fleet.yaml helm.values, then the chart's key of
# the example values ConfigMap (the keys are disjoint, so the merge order does not matter).
for chart in "${charts[@]}"; do
  if python3 - "${here}/${chart}/fleet.yaml" "${values_example}" "${chart}" "${fleet_work}/${chart}" <<'PYTHON'; then
import sys, yaml
fleet, example, chart, out = sys.argv[1:]
helm = yaml.safe_load(open(fleet)).get("helm") or {}
open(f"{out}-fleet.yaml", "w").write(yaml.safe_dump(helm.get("values") or {}))
site = ""
if helm.get("valuesFrom"):
    site = yaml.safe_load(open(example))["data"][helm["valuesFrom"][0]["configMapKeyRef"]["key"]]
open(f"{out}-site.yaml", "w").write(site)
PYTHON
    render_and_check "${chart}" "${chart} (Fleet values)" --namespace coldframe \
      --values "${fleet_work}/${chart}-fleet.yaml" --values "${fleet_work}/${chart}-site.yaml"
  else
    failed "${chart}: read deploy/charts/${chart}/fleet.yaml and its values key"
  fi
done

# The Fleet smoke (deploy/fleet/smoke.sh) refuses bad usage before it creates a cluster.
echo "# fleet smoke usage"
smoke=${fleet_dir}/smoke.sh
mkdir -p "${fleet_work}/fake-bin"
printf '#!/bin/sh\necho "fleet version v0.0.0 (fake)"\n' >"${fleet_work}/fake-bin/fleet"
chmod +x "${fleet_work}/fake-bin/fleet"
expect_failure "the Fleet smoke needs four images"         "usage: " "${smoke}" a b c
expect_failure "the Fleet smoke rejects an unknown cluster" "SMOKE_CLUSTER must be k3d or kind, not 'minikube'" \
  env SMOKE_CLUSTER=minikube "${smoke}" a b c d
expect_failure "the Fleet smoke needs the pinned fleet CLI" "the fleet CLI must be ${FLEET_VERSION}, found 'v0.0.0'" \
  env PATH="${fleet_work}/fake-bin:${PATH}" "${smoke}" a b c d
rm -rf "${fleet_work}"

echo "# ${passes} passed, ${failures} failed"
[[ ${failures} -eq 0 ]]
