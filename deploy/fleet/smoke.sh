#!/usr/bin/env bash
# Installs the Coldframe stack through Fleet on a disposable k3d cluster, the GitOps path of
# docs/operations/install.md, then upgrades it by a chart-version bump and restarts it three ways
# without losing data.
#
#   deploy/fleet/smoke.sh <server-image> <web-image> <migrations-image> <keycloak-image>
#
# 1. A k3d cluster (k3s pinned below) starts; the four images are imported as
#    localhost/coldframe/<component>:<appVersion>, the tag the charts deploy by default.
# 2. Fleet (fleet-crd and fleet, pinned in deploy/charts/dependencies.env and checked against their
#    sha256) is installed with Helm; deploy/fleet/gitrepo.yaml must pass a server-side dry run
#    against its CRD. RKE2's Traefik (rke2-traefik-crd and rke2-traefik with the values of
#    deploy/rke2/rke2-traefik-config.yaml) is installed as RKE2 would install it; on kind, a
#    stand-in HelmChartConfig CRD (deploy/charts/testdata/helmchartconfig-crd.yaml) takes the
#    place of k3s's.
# 3. Namespace "coldframe" gets what an adopter creates out of band: placeholder Secrets
#    (deploy/SECRETS.md), the realm ConfigMap coldframe-realm, RustFS as the S3 target, and the
#    ConfigMap coldframe-values, built from the charts' ci-values.yaml plus the local images and
#    the smoke CA's Issuer (deploy/fleet/values.example.yaml is its documented shape).
# 4. `fleet apply` creates the Bundles of the GitRepo's paths in fleet-local, as gitjob would from
#    the repository (no git server). Once the cert-manager bundle is Ready, the smoke CA
#    (deploy/charts/testdata/smoke-ca.yaml) stands in for Let's Encrypt. Every bundle must become
#    Ready, and no dependent's first Helm release may have been created before those of its
#    dependencies (the same second passes). Then every pod must be Ready (or a completed Job),
#    the Server healthy, the realm served, and the three hosts must answer over HTTPS through
#    Traefik's 443 with the smoke CA.
# 5. A marker row is committed in the databases coldframe (events) and keycloak (settings), and a
#    digest of the Server's domain tables taken.
# 6. Upgrade: a copy of deploy/ with every chart's version (and the Coldframe appVersion) bumped to
#    <version>-smoke.1, the images retagged to it, and the bundles applied again. The migration Job
#    must run again with the new image and complete before the new Server pod is created; a
#    once-a-second poller must never see two Server pods that can run. The markers, the digest and
#    the checks of step 4 must hold.
# 7. Restarts: the Server pod, the database primary pod, and the node container. After each, the
#    bundles and pods must be Ready again (after the node restart, every long-running pod must
#    have started again), the markers and the digest unchanged, the realm served, and the Server
#    healthy over HTTPS.
#
# Needs k3d, kubectl, helm, fleet (the CLI, FLEET_VERSION), jq, curl, sha256sum, python3 with
# PyYAML and docker (or Podman through DOCKER_HOST). On failure the bundles, bundle deployments,
# pods and Jobs, events, CloudNativePG and cert-manager objects and logs (Fleet and the operators
# included) are printed; the cluster and the smoke image tags are always deleted. The total run
# time is printed at the end.
#
# SMOKE_CLUSTER=kind runs the same checks on kind (kindest/node, pinned below), as
# deploy/charts/smoke.sh does. SMOKE_TIMEOUT (seconds, default 900) bounds every wait on the
# bundles, the stack, the upgrade and the restarts; the first install of all the bundles gets twice
# that. The prerequisites (cluster, Fleet, rke2-traefik, RustFS) get a fixed 300s each.
set -Eeuo pipefail
started=${SECONDS}

if [[ $# -ne 4 ]]; then
  echo "usage: $0 <server-image> <web-image> <migrations-image> <keycloak-image>" >&2
  exit 2
fi

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo=$(cd "${here}/../.." && pwd)
charts=${repo}/deploy/charts
# shellcheck source=SCRIPTDIR/../charts/dependencies.env
source "${charts}/dependencies.env"

declare -A source_images=(
  [server]=$1
  [web]=$2
  [migrations]=$3
  [keycloak]=$4
)

provider=${SMOKE_CLUSTER:-k3d}
k3s_image=docker.io/rancher/k3s:v1.36.4-k3s1
kind_image=docker.io/kindest/node:v1.36.4
timeout_seconds=${SMOKE_TIMEOUT:-900}
timeout=${timeout_seconds}s
# The prerequisites that are not under test (the cluster, Fleet, rke2-traefik, RustFS) get a fixed
# bound, as in deploy/charts/smoke.sh.
setup_seconds=300
setup_timeout=${setup_seconds}s
namespace=coldframe
db_cluster=coldframe-db
domain=coldframe.smoke.test
cluster="cf-fleet-$$-${RANDOM}"
case ${provider} in
  k3d) node_container=k3d-${cluster}-server-0 ;;
  kind) node_container=${cluster}-control-plane ;;
  *) echo "SMOKE_CLUSTER must be k3d or kind, not '${provider}'" >&2; exit 2 ;;
esac
work=$(mktemp -d)
export KUBECONFIG=${work}/kubeconfig
cluster_created=false
cluster_up=false
smoke_tags=()
port_forward_pid=""
poller_pid=""
failure=""
ns=(--namespace "${namespace}")

random_secret() {
  od -An -N24 -tx1 /dev/urandom | tr -d ' \n'
}

stop_port_forward() {
  if [[ -n ${port_forward_pid} ]]; then
    kill "${port_forward_pid}" 2>/dev/null || true
    wait "${port_forward_pid}" 2>/dev/null || true
    port_forward_pid=""
  fi
}

cleanup() {
  local status=$?
  stop_port_forward
  if [[ -n ${poller_pid} ]]; then
    kill "${poller_pid}" 2>/dev/null || true
    wait "${poller_pid}" 2>/dev/null || true
  fi
  if [[ ${status} -ne 0 && ${cluster_up} == true ]]; then
    echo "::group::bundles"
    kubectl -n fleet-local get bundles -o wide 2>&1 || true
    kubectl -n fleet-local get bundles -o json 2>/dev/null \
      | jq -r '.items[] | "\(.metadata.name): \(.status.summary // {}) \([.status.conditions[]? | select(.status != "True") | "\(.type): \(.message // "")"])"' || true
    echo "::endgroup::"
    echo "::group::bundle deployments"
    kubectl get bundledeployments -A -o wide 2>&1 || true
    kubectl get bundledeployments -A -o json 2>/dev/null \
      | jq -r '.items[] | "\(.metadata.name): ready=\(.status.ready) nonModified=\(.status.nonModified) \(.status.display // {}) \([.status.conditions[]? | select(.status != "True") | "\(.type): \(.message // "")"]) modified=\(.status.modifiedStatus // [])"' || true
    echo "::endgroup::"
    echo "::group::pods"
    kubectl get pods,jobs -A -o wide 2>&1 || true
    echo "::endgroup::"
    echo "::group::events"
    kubectl -n "${namespace}" get events --sort-by=.lastTimestamp 2>&1 | tail -n 100 || true
    echo "::endgroup::"
    echo "::group::database and TLS objects"
    kubectl -n "${namespace}" get clusters.postgresql.cnpg.io,databases.postgresql.cnpg.io,backups,objectstores,pvc -o wide 2>&1 || true
    kubectl get issuers,clusterissuers,certificates,certificaterequests -A -o wide 2>&1 || true
    kubectl -n "${namespace}" get clusters.postgresql.cnpg.io -o jsonpath='{range .items[*]}{.metadata.name}{": "}{.status.conditions}{"\n"}{end}' 2>&1 || true
    echo "::endgroup::"
    local pod workload
    for pod in $(kubectl -n "${namespace}" get pods -o name 2>/dev/null); do
      echo "::group::logs of ${pod}"
      kubectl -n "${namespace}" logs "${pod}" --all-containers --tail=200 2>&1 || true
      echo "::endgroup::"
    done
    for workload in cattle-fleet-system/deployment/fleet-controller cattle-fleet-system/deployment/fleet-agent \
      cnpg-system/deployment/cnpg-controller-manager cnpg-system/deployment/barman-cloud \
      cert-manager/deployment/cert-manager kube-system/daemonset/rke2-traefik; do
      echo "::group::logs of ${workload}"
      kubectl -n "${workload%%/*}" logs "${workload#*/}" --all-containers --tail=200 2>&1 || true
      echo "::endgroup::"
    done
  fi
  if [[ ${status} -ne 0 ]]; then
    local elapsed=$((SECONDS - started))
    echo "FAIL: Fleet smoke failed after $((elapsed / 60))m$((elapsed % 60))s: ${failure:-exit status ${status}}" >&2
  fi
  if [[ ${cluster_created} == true ]]; then
    if [[ ${provider} == kind ]]; then
      kind delete cluster --name "${cluster}" >/dev/null 2>&1 || true
    else
      k3d cluster delete "${cluster}" >/dev/null 2>&1 || true
    fi
  fi
  if [[ ${#smoke_tags[@]} -gt 0 ]]; then
    docker rmi "${smoke_tags[@]}" >/dev/null 2>&1 || true
  fi
  rm -rf "${work}"
  exit "${status}"
}
trap cleanup EXIT
# A command that fails under set -e, outside fail(), is named in the final FAIL line.
trap 'failure=${failure:-"line ${LINENO}: ${BASH_COMMAND}"}' ERR

fail() {
  failure=$*
  echo "FAIL: $*" >&2
  exit 1
}

# fetch <file> <url> <sha256>: downloads a pinned file into the work directory and checks it.
fetch() {
  curl --fail --silent --show-error --location --output "${work}/$1" "$2"
  echo "$3  ${work}/$1" | sha256sum --check --strict --quiet \
    || fail "$2 does not match its pinned sha256"
}

# chart_field <Chart.yaml> <name>: a top-level scalar, without quotes.
chart_field() {
  sed -n -E "s/^$2:[[:space:]]*\"?([^\"[:space:]]*)\"?[[:space:]]*$/\1/p" "$1" | head -n 1
}

fleet_cli_version=$(fleet --version 2>/dev/null | awk '{print $3}' || true)
[[ ${fleet_cli_version} == "${FLEET_VERSION}" ]] \
  || fail "the fleet CLI must be ${FLEET_VERSION}, found '${fleet_cli_version:-no fleet on PATH}'"

# The GitRepo's name and paths: `fleet apply <name> <paths>` creates the bundles gitjob would.
fleet_name=$(python3 -c 'import sys, yaml; print(yaml.safe_load(open(sys.argv[1]))["metadata"]["name"])' \
  "${here}/gitrepo.yaml")
mapfile -t fleet_paths < <(python3 -c '
import sys, yaml
print("\n".join(yaml.safe_load(open(sys.argv[1]))["spec"]["paths"]))' "${here}/gitrepo.yaml")
bundle_count=${#fleet_paths[@]}

version=$(chart_field "${charts}/server/Chart.yaml" appVersion)
upgrade_version=${version}-smoke.1
"${charts}/check-version.sh" "${version}" >/dev/null \
  || fail "the charts do not all carry version ${version} (deploy/charts/check-version.sh)"

echo "--- pinned charts"
fetch fleet-crd.tgz "${FLEET_CRD_CHART_URL}" "${FLEET_CRD_CHART_SHA256}"
fetch fleet.tgz "${FLEET_CHART_URL}" "${FLEET_CHART_SHA256}"
fetch rke2-traefik-crd.tgz "${RKE2_TRAEFIK_CRD_URL}" "${RKE2_TRAEFIK_CRD_SHA256}"
fetch rke2-traefik.tgz "${RKE2_TRAEFIK_URL}" "${RKE2_TRAEFIK_SHA256}"
python3 -c '
import sys, yaml
config = next(doc for doc in yaml.safe_load_all(open(sys.argv[1], encoding="utf-8")) if doc)
sys.stdout.write(config["spec"]["valuesContent"])
' "${repo}/deploy/rke2/rke2-traefik-config.yaml" >"${work}/rke2-traefik-values.yaml"

echo "--- cluster (${provider})"
cluster_created=true
if [[ ${provider} == kind ]]; then
  kind create cluster --name "${cluster}" --image "${kind_image}" --kubeconfig "${KUBECONFIG}" --wait "${setup_timeout}"
else
  k3d cluster create "${cluster}" \
    --image "${k3s_image}" \
    --no-lb \
    --k3s-arg '--disable=traefik@server:0' \
    --k3s-arg '--disable=metrics-server@server:0' \
    --kubeconfig-update-default=false \
    --wait --timeout "${setup_timeout}"
  k3d kubeconfig get "${cluster}" >"${KUBECONFIG}"
fi
chmod 600 "${KUBECONFIG}"
cluster_up=true
kubectl wait --for=condition=Ready nodes --all --timeout="${setup_timeout}"

# import_images <tag>: tags the four images localhost/coldframe/<component>:<tag> and imports them.
import_images() {
  local component tag tags=()
  for component in server web migrations keycloak; do
    tag="localhost/coldframe/${component}:$1"
    docker tag "${source_images[${component}]}" "${tag}"
    smoke_tags+=("${tag}")
    tags+=("${tag}")
  done
  if [[ ${provider} == kind ]]; then
    for tag in "${tags[@]}"; do
      docker save "${tag}" | docker exec -i "${node_container}" ctr --namespace=k8s.io images import -
    done
  else
    k3d image import --cluster "${cluster}" "${tags[@]}"
  fi
}
import_images "${version}"

echo "--- Fleet ${FLEET_VERSION}"
helm install fleet-crd "${work}/fleet-crd.tgz" --namespace cattle-fleet-system --create-namespace \
  --wait --timeout "${setup_timeout}"
helm install fleet "${work}/fleet.tgz" --namespace cattle-fleet-system --wait --timeout "${setup_timeout}"
# The controller registers the cluster itself as "local" in fleet-local, in the ClusterGroup
# "default" that the bundles target; its agent (in cattle-fleet-system) must have reported in.
agent_seen=
deadline=$((SECONDS + setup_seconds))
while [[ ${SECONDS} -lt ${deadline} ]]; do
  agent_seen=$(kubectl -n fleet-local get clusters.fleet.cattle.io local -o jsonpath='{.status.agent.lastSeen}' 2>/dev/null || true)
  if [[ -n ${agent_seen} ]] && kubectl -n fleet-local get clustergroups.fleet.cattle.io default >/dev/null 2>&1 \
    && kubectl -n cattle-fleet-system get deployment fleet-agent >/dev/null 2>&1; then
    break
  fi
  sleep 2
done
[[ -n ${agent_seen} ]] || fail "Fleet registered no local cluster with a running agent"
kubectl -n cattle-fleet-system rollout status deployment/fleet-agent --timeout="${setup_timeout}"
kubectl apply --dry-run=server -f "${here}/gitrepo.yaml" >/dev/null \
  || fail "deploy/fleet/gitrepo.yaml does not pass a server-side dry run against Fleet's GitRepo CRD"
echo "ok: Fleet runs with its local cluster; deploy/fleet/gitrepo.yaml is a valid GitRepo"

echo "--- rke2-traefik ${RKE2_TRAEFIK_VERSION}, as RKE2 installs it"
if [[ ${provider} == kind ]]; then
  kubectl apply -f "${charts}/testdata/helmchartconfig-crd.yaml"
  kubectl wait crd/helmchartconfigs.helm.cattle.io --for=condition=Established --timeout="${setup_timeout}"
fi
helm install rke2-traefik-crd "${work}/rke2-traefik-crd.tgz" --namespace kube-system --wait --timeout "${setup_timeout}"
helm install rke2-traefik "${work}/rke2-traefik.tgz" --namespace kube-system \
  --values "${work}/rke2-traefik-values.yaml" --wait --timeout "${setup_timeout}"

echo "--- what an adopter creates out of band"
kubectl create namespace "${namespace}"
kubectl "${ns[@]}" create secret generic coldframe-db-coldframe --type=kubernetes.io/basic-auth \
  --from-literal=username=coldframe --from-literal=password="$(random_secret)"
kubectl "${ns[@]}" create secret generic coldframe-db-temporal --type=kubernetes.io/basic-auth \
  --from-literal=username=temporal --from-literal=password="$(random_secret)"
kubectl "${ns[@]}" create secret generic coldframe-db-keycloak --type=kubernetes.io/basic-auth \
  --from-literal=username=keycloak --from-literal=password="$(random_secret)"
kubectl "${ns[@]}" label secret coldframe-db-coldframe coldframe-db-temporal coldframe-db-keycloak cnpg.io/reload=true
kubectl "${ns[@]}" create secret generic coldframe-keycloak-admin \
  --from-literal=username=admin --from-literal=password="$(random_secret)"
kubectl "${ns[@]}" create secret generic coldframe-oidc-clients \
  --from-literal=web-client-secret="$(random_secret)" --from-literal=server-client-secret="$(random_secret)"
kubectl "${ns[@]}" create secret generic coldframe-backup-s3 \
  --from-literal=access-key-id="smoke$(random_secret | cut -c1-12)" --from-literal=secret-access-key="$(random_secret)"
kubectl "${ns[@]}" create configmap coldframe-realm \
  --from-file=coldframe-realm.json="${repo}/aspire/keycloak/realms/coldframe-realm.json"

sed -e "s|@RUSTFS_IMAGE@|${RUSTFS_IMAGE}|" -e "s|@AWS_CLI_IMAGE@|${AWS_CLI_IMAGE}|" "${charts}/testdata/rustfs.yaml" \
  | kubectl "${ns[@]}" apply -f -
kubectl "${ns[@]}" rollout status deployment/rustfs --timeout="${setup_timeout}"
kubectl "${ns[@]}" wait job/rustfs-bucket --for=condition=Complete --timeout="${setup_timeout}"

# coldframe-values: the shape of deploy/fleet/values.example.yaml, with the CI values of each chart
# (ci-values.yaml), the local images (the tag stays the chart's appVersion) and the smoke CA.
python3 - "${charts}" "${here}/values.example.yaml" >"${work}/coldframe-values.yaml" <<'PYTHON'
import os, sys, yaml

charts, example = sys.argv[1:]

def merge(base, extra):
    for key, value in extra.items():
        if isinstance(value, dict) and isinstance(base.get(key), dict):
            merge(base[key], value)
        else:
            base[key] = value
    return base

def image(component):
    return {"repository": f"localhost/coldframe/{component}", "pullPolicy": "Never"}

extras = {
    "database": {},
    "keycloak": {"image": image("keycloak"), "realmImport": {"configMap": "coldframe-realm"}},
    "server": {"image": image("server"), "migrations": {"image": image("migrations")}},
    "web": {"image": image("web")},
    "ingress": {"acme": {"enabled": False}, "externalIssuer": {"name": "smoke-ca"}},
}
configmap = yaml.safe_load(open(example, encoding="utf-8"))
if set(configmap["data"]) != set(extras):
    sys.exit(f"values.example.yaml has the keys {sorted(configmap['data'])}, expected {sorted(extras)}")
data = {}
for key, extra in extras.items():
    path = os.path.join(charts, key, "ci-values.yaml")
    values = yaml.safe_load(open(path, encoding="utf-8")) if os.path.exists(path) else {}
    data[key] = yaml.safe_dump(merge(values or {}, extra), sort_keys=False)
configmap["data"] = data
yaml.safe_dump(configmap, sys.stdout, sort_keys=False)
PYTHON
kubectl apply -f "${work}/coldframe-values.yaml"

# bundles_ready: every bundle of the GitRepo's paths is Ready at its current generation, and every
# bundle deployment has applied its current deployment and is ready.
bundles_ready() {
  local bundles deployments
  bundles=$(kubectl -n fleet-local get bundles -o json) || return 1
  deployments=$(kubectl get bundledeployments -A -l fleet.cattle.io/bundle-namespace=fleet-local -o json) || return 1
  jq -e --argjson count "${bundle_count}" '
    [.items[] | select(.metadata.labels["coldframe.escendit.io/bundle"] != null)]
    | length == $count and all(.[];
        (.status.observedGeneration // 0) == .metadata.generation
        and (.status.summary.desiredReady // 0) > 0
        and .status.summary.ready == .status.summary.desiredReady)' <<<"${bundles}" >/dev/null || return 1
  jq -e --argjson count "${bundle_count}" '
    .items | length >= $count and all(.[];
      .status.appliedDeploymentID == .spec.deploymentID and .status.ready == true)' <<<"${deployments}" >/dev/null
}

# wait_bundles <seconds>: waits until bundles_ready, printing the bundles every minute.
wait_bundles() {
  local deadline=$((SECONDS + $1)) next_report=$((SECONDS + 60))
  until bundles_ready; do
    [[ ${SECONDS} -lt ${deadline} ]] || fail "the bundles are not all Ready after $1s"
    if [[ ${SECONDS} -ge ${next_report} ]]; then
      kubectl -n fleet-local get bundles 2>&1 || true
      next_report=$((SECONDS + 60))
    fi
    sleep 5
  done
  echo "ok: all ${bundle_count} bundles are Ready"
}

# wait_bundle <label>: waits until the bundle labelled <label> is Ready.
wait_bundle() {
  for _ in $(seq 1 "$((timeout_seconds / 5))"); do
    if kubectl -n fleet-local get bundles -l "coldframe.escendit.io/bundle=$1" -o json | jq -e '
      .items | length == 1 and ((.[0].status.summary.desiredReady // 0) > 0)
      and .[0].status.summary.ready == .[0].status.summary.desiredReady' >/dev/null; then
      return
    fi
    sleep 5
  done
  fail "the bundle $1 is not Ready after ${timeout_seconds}s"
}

# primary: the name of the cluster's primary pod.
primary() {
  kubectl "${ns[@]}" get pods -l "cnpg.io/cluster=${db_cluster},cnpg.io/instanceRole=primary" \
    -o jsonpath='{.items[0].metadata.name}'
}

# sql <database> <statement>: runs one statement as the postgres superuser on the primary.
sql() {
  kubectl "${ns[@]}" exec "$(primary)" -c postgres -- \
    psql --no-psqlrc --quiet --tuples-only --no-align -v ON_ERROR_STOP=1 --dbname "$1" --command "$2"
}

# domain_digest: row count and md5 of every domain table of the Server's database (the tables of
# apps/cs/migrations); fails when one is missing.
domain_tables=(identity_memberships identity_sites journal_events journal_outbox lots projection_checkpoints)
domain_digest() {
  local table digest
  for table in "${domain_tables[@]}"; do
    digest=$(sql coldframe "SELECT count(*) || ':' || md5(coalesce(string_agg(row_to_json(t)::text, E'\\n' ORDER BY row_to_json(t)::text), '')) FROM ${table} t") \
      || fail "the domain table ${table} cannot be read"
    printf '%s=%s ' "${table}" "${digest}"
  done
}

# The databases that get a committed marker: the Server's events and Keycloak's settings.
marker_databases=(coldframe keycloak)

# markers: the marker rows of every marker database, "<database>: <name>@<written_at>" each.
markers() {
  local database rows
  for database in "${marker_databases[@]}"; do
    rows=$(sql "${database}" "SELECT string_agg(name || '@' || written_at, ',' ORDER BY name) FROM smoke_marker") \
      || return 1
    printf '%s: %s; ' "${database}" "${rows}"
  done
}

# commit_marker <name>: commits one more marker row in every marker database; the markers that
# check_data requires are read back afterwards.
commit_marker() {
  local database
  for database in "${marker_databases[@]}"; do
    sql "${database}" "INSERT INTO smoke_marker (name) VALUES ('$1-${database}')" >/dev/null
  done
  seed_markers=$(markers) || fail "the markers cannot be read back after committing $1"
  echo "ok: committed the marker $1 in ${marker_databases[*]}"
}

# check_data <label>: the markers and the domain digest are those of the seed.
check_data() {
  local found digest
  found=$(markers) || fail "$1: the marker tables cannot be read"
  [[ ${found} == "${seed_markers}" ]] || fail "$1: lost committed data: the markers are '${found}', expected '${seed_markers}'"
  digest=$(domain_digest)
  [[ ${digest} == "${seed_digest}" ]] || fail "$1: lost committed data: the domain tables changed:
before: ${seed_digest}
after:  ${digest}"
  echo "ok: $1: the committed markers (${marker_databases[*]}) and the domain tables are unchanged"
}

# check_stack: every pod Ready (or a completed Job), the Server healthy, the realm served. Unlike
# the chart smoke, Fleet does not wait for CloudNativePG's Database objects before the next
# bundle: a hook Job's first pod can fail on a database still being created, and its retry
# succeed. A failed pod of a Job that completed passes.
check_stack() {
  local not_ready health status issuer complete_jobs
  complete_jobs=$(kubectl "${ns[@]}" get jobs -o json \
    | jq -c '[.items[] | select(any(.status.conditions[]?; .type == "Complete" and .status == "True")) | .metadata.name]')
  not_ready=$(kubectl "${ns[@]}" get pods -o json | jq -r --argjson complete "${complete_jobs}" '
    .items[]
    | select(.metadata.deletionTimestamp == null)
    | ([.metadata.ownerReferences[]? | select(.kind == "Job") | .name]) as $jobs
    | select(
        if .status.phase == "Succeeded" then
          ($jobs | length) == 0
        elif .status.phase == "Failed" then
          ($jobs | length) == 0 or (any($jobs[]; . as $job | $complete | index($job)) | not)
        else
          ([.status.conditions[]? | select(.type == "Ready" and .status == "True")] | length) == 0
        end)
    | "\(.metadata.name) (\(.status.phase))"')
  [[ -z ${not_ready} ]] || fail "pods neither Ready nor completed Jobs: ${not_ready}"
  echo "ok: every pod is Ready or a completed Job"

  health=$(kubectl get --raw "/api/v1/namespaces/${namespace}/services/http:server:http/proxy/.well-known/healthz") \
    || fail "GET /.well-known/healthz on the Server Service did not answer 200"
  status=$(jq -r 'if type == "object" then .status else . end' <<<"${health}" 2>/dev/null || echo "${health}")
  [[ ${status} == "Healthy" ]] || fail "the Server's /.well-known/healthz answered '${health}', expected status Healthy"
  echo "ok: the Server's /.well-known/healthz answers 200 Healthy"

  issuer=$(kubectl get --raw "/api/v1/namespaces/${namespace}/services/http:keycloak:http/proxy/realms/coldframe/.well-known/openid-configuration" \
    | jq -r '.issuer // empty') || true
  [[ -n ${issuer} ]] || fail "Keycloak does not serve the imported coldframe realm"
  echo "ok: Keycloak serves the imported coldframe realm (issuer ${issuer})"
}

# wait_stack <label>: retries check_stack (in a subshell, so that its failure does not exit) for
# up to SMOKE_TIMEOUT, then runs it once more for real.
wait_stack() {
  local deadline=$((SECONDS + timeout_seconds))
  until (check_stack) >/dev/null 2>&1; do
    [[ ${SECONDS} -lt ${deadline} ]] || break
    sleep 5
  done
  echo "--- $1: the stack"
  check_stack
}

# https_get <host> <path>: GET https://<host>:<port><path> through Traefik's websecure port.
https_get() {
  curl --fail --silent --show-error --max-time 30 \
    --resolve "$1:${https_port}:127.0.0.1" --cacert "${work}/smoke-ca.crt" \
    "https://$1:${https_port}$2"
}

# check_https <label>: the three hosts over HTTPS through a port-forward to Traefik's 443, trusting
# the smoke CA only.
check_https() {
  local health status issuer
  kubectl "${ns[@]}" get secret coldframe-tls -o jsonpath='{.data.ca\.crt}' | base64 --decode >"${work}/smoke-ca.crt"
  [[ -s ${work}/smoke-ca.crt ]] || fail "$1: the Secret coldframe-tls has no ca.crt"
  kubectl -n kube-system rollout status daemonset/rke2-traefik --timeout="${timeout}" >/dev/null
  kubectl -n kube-system port-forward svc/rke2-traefik :443 >"${work}/port-forward.log" 2>&1 &
  port_forward_pid=$!
  https_port=
  for _ in $(seq 1 30); do
    https_port=$(sed -n -E 's/^Forwarding from 127\.0\.0\.1:([0-9]+) -> .*/\1/p' "${work}/port-forward.log" | head -n 1)
    [[ -n ${https_port} ]] && break
    kill -0 "${port_forward_pid}" 2>/dev/null || fail "$1: kubectl port-forward to Traefik exited: $(cat "${work}/port-forward.log")"
    sleep 1
  done
  [[ -n ${https_port} ]] || fail "$1: kubectl port-forward to Traefik did not start: $(cat "${work}/port-forward.log")"

  health=
  for _ in $(seq 1 60); do
    health=$(https_get "api.${domain}" /.well-known/healthz 2>"${work}/curl.err") && break
    sleep 2
  done
  [[ -n ${health} ]] || fail "$1: GET https://api.${domain}/.well-known/healthz through Traefik failed: $(cat "${work}/curl.err")"
  status=$(jq -r 'if type == "object" then .status else . end' <<<"${health}" 2>/dev/null || echo "${health}")
  [[ ${status} == "Healthy" ]] || fail "$1: https://api.${domain}/.well-known/healthz answered '${health}', expected status Healthy"
  echo "ok: $1: https://api.${domain}/.well-known/healthz answers Healthy"

  issuer=$(https_get "auth.${domain}" /realms/coldframe/.well-known/openid-configuration | jq -r '.issuer // empty') \
    || fail "$1: GET https://auth.${domain}/realms/coldframe/.well-known/openid-configuration through Traefik failed"
  [[ ${issuer} == "https://auth.${domain}:${https_port}/realms/coldframe" ]] \
    || fail "$1: Keycloak's issuer through Traefik is '${issuer}', expected https://auth.${domain}:${https_port}/realms/coldframe"
  echo "ok: $1: https://auth.${domain} serves the coldframe realm"

  https_get "${domain}" /.well-known/healthz/ready >/dev/null \
    || fail "$1: GET https://${domain}/.well-known/healthz/ready through Traefik did not answer 200"
  echo "ok: $1: https://${domain}/.well-known/healthz/ready answers 200"
  stop_port_forward
}

# release_order: no dependent's Helm release (its first revision) was created before the release
# of one of its dependencies, read from the Bundles' dependsOn. The timestamps have whole seconds,
# and a bundle can count as Ready in the moment after its install, before the objects it created
# report their first status (a new CloudNativePG Cluster has no conditions yet): the same second
# passes.
release_order() {
  local bundles releases
  bundles=$(kubectl -n fleet-local get bundles -o json)
  releases=$(kubectl get secrets -A -l owner=helm,version=1 -o json)
  python3 - <(echo "${bundles}") <(echo "${releases}") <<'PYTHON'
import json, sys
from datetime import datetime

LABEL = "coldframe.escendit.io/bundle"
bundles = json.load(open(sys.argv[1]))["items"]
releases = json.load(open(sys.argv[2]))["items"]
created = {}
for secret in releases:
    meta = secret["metadata"]
    created[(meta["namespace"], meta["labels"]["name"])] = datetime.fromisoformat(
        meta["creationTimestamp"].replace("Z", "+00:00"))
when, edges = {}, []
for bundle in bundles:
    label = bundle["metadata"].get("labels", {}).get(LABEL)
    if not label:
        continue
    spec = bundle["spec"]
    key = (spec.get("namespace") or spec.get("defaultNamespace"), spec["helm"]["releaseName"])
    if key not in created:
        sys.exit(f"bundle {label}: no Helm release {key[1]} in namespace {key[0]}")
    when[label] = created[key]
    for entry in spec.get("dependsOn") or []:
        edges.append((label, entry["selector"]["matchLabels"][LABEL]))
errors = [f"{dependent} ({when[dependent]:%H:%M:%S}) was installed before its dependency "
          f"{dependency} ({when[dependency]:%H:%M:%S})"
          for dependent, dependency in edges if when[dependent] < when[dependency]]
if errors:
    sys.exit("\n".join(errors))
for label in sorted(when, key=when.get):
    print(f"   {when[label]:%H:%M:%S} {label}")
print(f"ok: all {len(edges)} dependsOn edges hold: no release was installed before its dependencies")
PYTHON
}

echo "--- fleet apply: the ${bundle_count} paths of deploy/fleet/gitrepo.yaml"
(cd "${repo}" && fleet apply --kubeconfig "${KUBECONFIG}" --namespace fleet-local "${fleet_name}" "${fleet_paths[@]}")
# The ingress chart's Certificate needs the smoke CA, whose objects need cert-manager's CRDs and
# webhook: they come from the cert-manager bundle.
wait_bundle cert-manager
deadline=$((SECONDS + timeout_seconds))
until kubectl "${ns[@]}" apply -f "${charts}/testdata/smoke-ca.yaml" >/dev/null 2>"${work}/apply.err"; do
  [[ ${SECONDS} -lt ${deadline} ]] || fail "the smoke CA does not apply: $(cat "${work}/apply.err")"
  sleep 5
done
kubectl "${ns[@]}" wait certificate/smoke-ca --for=condition=Ready --timeout="${timeout}"
wait_bundles "$((2 * timeout_seconds))"
release_order
wait_stack "install"
check_https "install"
dns_names=$(kubectl "${ns[@]}" get certificate coldframe-tls -o jsonpath='{.spec.dnsNames}' | jq -r 'join(",")')
[[ ${dns_names} == "${domain},api.${domain},auth.${domain}" ]] \
  || fail "the Certificate coldframe-tls names '${dns_names}', expected the three hosts of ${domain}"
args=$(kubectl -n cert-manager get deployment cert-manager -o jsonpath='{.spec.template.spec.containers[0].args}')
for flag in --dns01-recursive-nameservers-only --dns01-recursive-nameservers=1.1.1.1:53,9.9.9.9:53; do
  grep -F -- "\"${flag}\"" <<<"${args}" >/dev/null || fail "cert-manager runs without ${flag}: ${args}"
done
echo "ok: cert-manager runs with the DNS-01 recursive-nameserver flags of docs/operations/split-dns.md"
# Compared without trailing newlines (command substitution strips them on both sides).
delivered=$(kubectl -n kube-system get helmchartconfig rke2-traefik -o jsonpath='{.spec.valuesContent}') \
  || fail "the rke2-traefik bundle delivered no HelmChartConfig rke2-traefik in kube-system"
printf '%s\n' "${delivered}" >"${work}/delivered-values.yaml"
[[ ${delivered} == "$(cat "${work}/rke2-traefik-values.yaml")" ]] \
  || fail "the HelmChartConfig rke2-traefik differs from deploy/rke2/rke2-traefik-config.yaml:
$(diff "${work}/rke2-traefik-values.yaml" "${work}/delivered-values.yaml" || true)"
echo "ok: the rke2-traefik bundle delivers the valuesContent of deploy/rke2/rke2-traefik-config.yaml"

echo "--- seed"
for database in "${marker_databases[@]}"; do
  sql "${database}" "CREATE TABLE smoke_marker (name text PRIMARY KEY, written_at timestamptz NOT NULL DEFAULT now())"
  sql "${database}" "INSERT INTO smoke_marker (name) VALUES ('committed-${database}')"
done
seed_markers=$(markers) || fail "the markers cannot be read back"
seed_digest=$(domain_digest)
echo "ok: markers ${seed_markers}domain tables ${seed_digest}"

echo "--- upgrade to ${upgrade_version}"
tree=${work}/tree
mkdir -p "${tree}"
cp -R "${repo}/deploy" "${tree}/deploy"
for chart_file in "${tree}"/deploy/charts/*/Chart.yaml; do
  sed -i -E "s/^version:.*/version: ${upgrade_version}/" "${chart_file}"
  case $(basename "$(dirname "${chart_file}")") in
    server | web | keycloak) sed -i -E "s/^appVersion:.*/appVersion: \"${upgrade_version}\"/" "${chart_file}" ;;
  esac
done
"${charts}/check-version.sh" "${upgrade_version}" "${tree}/deploy/charts" >/dev/null \
  || fail "the bumped copy does not carry ${upgrade_version}"
import_images "${upgrade_version}"

job_before=$(kubectl "${ns[@]}" get job server-migrations -o jsonpath='{.metadata.uid}')
server_pod_before=$(kubectl "${ns[@]}" get pods -l app.kubernetes.io/instance=server -o json \
  | jq -r '[.items[] | select(any(.metadata.ownerReferences[]?; .kind == "ReplicaSet")) | .metadata.name] | join(",")')
# The poller records every Server pod once a second, terminating ones included. It counts those
# whose containers may run (phase Pending, Running or Unknown): an old pod whose container has
# exited (Succeeded or Failed) but whose object is still being deleted is stopped, which is what
# the Recreate strategy waits for.
(
  while true; do
    printf '%s %s\n' "$(date -u +%H:%M:%S)" "$(kubectl "${ns[@]}" get pods -l app.kubernetes.io/instance=server -o json 2>/dev/null \
      | jq -r '[.items[] | select(any(.metadata.ownerReferences[]?; .kind == "ReplicaSet"))]
               | "\([.[] | select(.status.phase != "Succeeded" and .status.phase != "Failed")] | length) \(
                  [.[] | "\(.metadata.name):\(.status.phase)\(if .metadata.deletionTimestamp then ":terminating" else "" end)"]
                  | join(","))"' 2>/dev/null || echo "? unreadable")"
    sleep 1
  done
) >"${work}/server-pods.log" 2>&1 &
poller_pid=$!

(cd "${tree}" && fleet apply --kubeconfig "${KUBECONFIG}" --namespace fleet-local "${fleet_name}" "${fleet_paths[@]}")
upgraded=false
for _ in $(seq 1 "$((timeout_seconds / 5))"); do
  image=$(kubectl "${ns[@]}" get deployment server -o jsonpath='{.spec.template.spec.containers[0].image}')
  if [[ ${image} == "localhost/coldframe/server:${upgrade_version}" ]] && bundles_ready; then
    upgraded=true
    break
  fi
  sleep 5
done
kill "${poller_pid}" 2>/dev/null || true
wait "${poller_pid}" 2>/dev/null || true
poller_pid=""

timeline() {
  echo "Server pods (UTC, count of pods that may run, all pods):"
  uniq -f 1 "${work}/server-pods.log" | tail -n 60
  kubectl "${ns[@]}" get job server-migrations -o json \
    | jq -r '"migration Job \(.metadata.uid): created \(.metadata.creationTimestamp), started \(.status.startTime), completed \(.status.completionTime // "never"), succeeded \(.status.succeeded // 0), failed \(.status.failed // 0)"'
  kubectl "${ns[@]}" get pods -l app.kubernetes.io/instance=server -o json \
    | jq -r '.items[] | "pod \(.metadata.name): created \(.metadata.creationTimestamp), \(.status.phase), image \(.spec.containers[0].image)"'
}
[[ ${upgraded} == true ]] || fail "the upgrade to ${upgrade_version} did not complete within ${timeout_seconds}s
$(timeline)"
job_after=$(kubectl "${ns[@]}" get job server-migrations -o json)
[[ $(jq -r '.metadata.uid' <<<"${job_after}") != "${job_before}" ]] \
  || fail "the upgrade did not run the migration Job again
$(timeline)"
[[ $(jq -r '.status.succeeded // 0' <<<"${job_after}") == 1 ]] \
  || fail "the migration Job did not complete on upgrade
$(timeline)"
[[ $(jq -r '.status.failed // 0' <<<"${job_after}") == 0 ]] \
  || fail "the migration Job had failed attempts on upgrade
$(timeline)"
[[ $(jq -r '.spec.template.spec.containers[0].image' <<<"${job_after}") == "localhost/coldframe/migrations:${upgrade_version}" ]] \
  || fail "the migration Job did not run the ${upgrade_version} image
$(timeline)"
completed=$(jq -r '.status.completionTime' <<<"${job_after}")
new_pod=$(kubectl "${ns[@]}" get pods -l app.kubernetes.io/instance=server -o json \
  | jq -c '[.items[] | select(any(.metadata.ownerReferences[]?; .kind == "ReplicaSet")) | select(.metadata.deletionTimestamp == null)] | .[0] // empty')
[[ -n ${new_pod} ]] || fail "no running Server pod after the upgrade
$(timeline)"
[[ ",${server_pod_before}," != *",$(jq -r '.metadata.name' <<<"${new_pod}"),"* ]] || fail "the Server pod was not replaced
$(timeline)"
[[ $(date -u -d "$(jq -r '.metadata.creationTimestamp' <<<"${new_pod}")" +%s) -ge $(date -u -d "${completed}" +%s) ]] \
  || fail "the new Server pod was created before the migration Job completed (${completed})
$(timeline)"
echo "ok: the migration Job ran ${upgrade_version} and completed (${completed}) before the new Server pod was created ($(jq -r '.metadata.creationTimestamp' <<<"${new_pod}"))"
max_pods=$(awk '$2 ~ /^[0-9]+$/ && $2 > max { max = $2 } END { print max + 0 }' "${work}/server-pods.log")
samples=$(awk '$2 ~ /^[0-9]+$/ { n++ } END { print n + 0 }' "${work}/server-pods.log")
[[ ${max_pods} -le 1 ]] || fail "the poller saw ${max_pods} Server pods at once: the upgrade was not stop-then-start
$(timeline)"
[[ ${samples} -gt 0 ]] || fail "the Server pod poller read no Server pods
$(timeline)"
echo "ok: the poller never saw two Server pods (${samples} samples)"
wait_stack "upgrade"
check_data "upgrade"
check_https "upgrade"

echo "--- restart: the Server pod"
commit_marker before-server-pod-restart
old_uid=$(jq -r '.metadata.uid' <<<"${new_pod}")
kubectl "${ns[@]}" delete pod "$(jq -r '.metadata.name' <<<"${new_pod}")" --wait=false
for _ in $(seq 1 "$((timeout_seconds / 5))"); do
  new_uid=$(kubectl "${ns[@]}" get pods -l app.kubernetes.io/instance=server -o json \
    | jq -r '[.items[] | select(any(.metadata.ownerReferences[]?; .kind == "ReplicaSet")) | select(.metadata.deletionTimestamp == null) | .metadata.uid] | first // empty')
  [[ -n ${new_uid} && ${new_uid} != "${old_uid}" ]] && break
  sleep 5
done
[[ -n ${new_uid} && ${new_uid} != "${old_uid}" ]] \
  || fail "no new Server pod after deleting $(jq -r '.metadata.name' <<<"${new_pod}")"
kubectl "${ns[@]}" rollout status deployment/server --timeout="${timeout}"
wait_bundles "${timeout_seconds}"
wait_stack "Server pod restart"
check_data "Server pod restart"
check_https "Server pod restart"

echo "--- restart: the database primary pod"
commit_marker before-database-restart
old_primary=$(primary)
old_uid=$(kubectl "${ns[@]}" get pod "${old_primary}" -o jsonpath='{.metadata.uid}')
kubectl "${ns[@]}" delete pod "${old_primary}"
for _ in $(seq 1 "$((timeout_seconds / 5))"); do
  new_uid=$(kubectl "${ns[@]}" get pods -l "cnpg.io/cluster=${db_cluster},cnpg.io/instanceRole=primary" \
    -o jsonpath='{.items[0].metadata.uid}' 2>/dev/null || true)
  [[ -n ${new_uid} && ${new_uid} != "${old_uid}" ]] && break
  sleep 5
done
[[ -n ${new_uid} && ${new_uid} != "${old_uid}" ]] || fail "no new primary pod after deleting ${old_primary}"
kubectl "${ns[@]}" wait "pod/$(primary)" --for=condition=Ready --timeout="${timeout}"
kubectl "${ns[@]}" wait "clusters.postgresql.cnpg.io/${db_cluster}" --for=condition=Ready --timeout="${timeout}"
wait_bundles "${timeout_seconds}"
wait_stack "database pod restart"
check_data "database pod restart"
check_https "database pod restart"

echo "--- restart: the node (${node_container})"
commit_marker before-node-restart
restarted_at=$(date -u +%s)
docker restart "${node_container}" >/dev/null
deadline=$((SECONDS + timeout_seconds))
until kubectl get --raw /readyz >/dev/null 2>&1; do
  [[ ${SECONDS} -lt ${deadline} ]] || break
  sleep 2
done
kubectl get --raw /readyz >/dev/null || fail "the API server did not come back after the node restart"
kubectl wait --for=condition=Ready nodes --all --timeout="${timeout}"
# Until the kubelet has synced, the pods still show their state from before the restart: wait
# until every container of the long-running pods has started again.
stale=
for _ in $(seq 1 "$((timeout_seconds / 5))"); do
  stale=$(kubectl get pods --all-namespaces -o json | jq -r --argjson since "${restarted_at}" '
    .items[]
    | select(.metadata.deletionTimestamp == null)
    | select(all(.metadata.ownerReferences[]?; .kind != "Job"))
    | select(any(.status.containerStatuses[]?;
        ((.state.running.startedAt // "1970-01-01T00:00:00Z") | fromdateiso8601) < $since))
    | "\(.metadata.namespace)/\(.metadata.name)"')
  [[ -z ${stale} ]] && break
  sleep 5
done
[[ -z ${stale} ]] || fail "pods not restarted with the node: ${stale//$'\n'/, }"
echo "ok: every long-running pod restarted with the node"
kubectl "${ns[@]}" wait "clusters.postgresql.cnpg.io/${db_cluster}" --for=condition=Ready --timeout="${timeout}"
wait_bundles "${timeout_seconds}"
wait_stack "node restart"
check_data "node restart"
check_https "node restart"

elapsed=$((SECONDS - started))
echo "ok: Fleet smoke passed in $((elapsed / 60))m$((elapsed % 60))s"
