#!/usr/bin/env bash
# Installs the five charts on a disposable k3d cluster and checks that the stack comes up.
#
#   deploy/charts/smoke.sh <server-image> <web-image> <migrations-image> <keycloak-image>
#
# 1. A k3d cluster (k3s pinned below) starts; the four Coldframe images are imported into it.
# 2. Namespace "coldframe" gets placeholder Secrets with random values, created with kubectl as an
#    adopter would (deploy/SECRETS.md), and a test-only PostgreSQL 18.6 as coldframe-db-rw
#    (testdata/postgres.yaml) with the roles and databases the charts expect.
# 3. helm install --wait: nats, temporal, keycloak (importing the Aspire realm), server, web.
# 4. Every pod must be Ready, or Succeeded and owned by a Job; the Server's /.well-known/healthz
#    must answer 200 "Healthy" through the API server's Service proxy, and Keycloak must serve
#    the imported coldframe realm.
# 5. helm upgrade temporal: the schema and namespace hooks re-run on a current schema and keep
#    the existing namespace.
# 6. helm upgrade server with an unreachable database: the migration hook Job fails, the upgrade
#    fails and the Server Deployment is unchanged.
# 7. helm upgrade server: the migration hook Job must run again and complete.
#
# Needs k3d, kubectl, helm, jq and docker (or Podman through DOCKER_HOST). On failure the pods,
# events and logs are printed; the cluster is always deleted.
#
# SMOKE_CLUSTER=kind runs the same checks on kind (kindest/node, pinned below) instead: a fallback
# for hosts where k3s cannot start, such as rootless Podman without the cpuset cgroup controller
# delegated. CI uses k3d. SMOKE_TIMEOUT (seconds, default 600) bounds each helm install.
set -euo pipefail

if [[ $# -ne 4 ]]; then
  echo "usage: $0 <server-image> <web-image> <migrations-image> <keycloak-image>" >&2
  exit 2
fi

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo=$(cd "${here}/../.." && pwd)

declare -A source_images=(
  [server]=$1
  [web]=$2
  [migrations]=$3
  [keycloak]=$4
)

provider=${SMOKE_CLUSTER:-k3d}
k3s_image=docker.io/rancher/k3s:v1.36.4-k3s1
kind_image=docker.io/kindest/node:v1.36.4
timeout=${SMOKE_TIMEOUT:-600}s
namespace=coldframe
cluster="cf-smoke-$$-${RANDOM}"
work=$(mktemp -d)
export KUBECONFIG=${work}/kubeconfig
cluster_created=false
cluster_up=false
smoke_tags=()

case ${provider} in
  k3d | kind) ;;
  *) echo "SMOKE_CLUSTER must be k3d or kind, not '${provider}'" >&2; exit 2 ;;
esac

random_secret() {
  od -An -N24 -tx1 /dev/urandom | tr -d ' \n'
}

cleanup() {
  local status=$?
  if [[ ${status} -ne 0 && ${cluster_up} == true ]]; then
    echo "::group::pods"
    kubectl -n "${namespace}" get pods,jobs -o wide 2>&1 || true
    echo "::endgroup::"
    echo "::group::events"
    kubectl -n "${namespace}" get events --sort-by=.lastTimestamp 2>&1 | tail -n 100 || true
    echo "::endgroup::"
    local pod
    for pod in $(kubectl -n "${namespace}" get pods -o name 2>/dev/null); do
      echo "::group::logs of ${pod}"
      kubectl -n "${namespace}" logs "${pod}" --all-containers --tail=200 2>&1 || true
      echo "::endgroup::"
    done
    echo "Chart smoke install FAILED." >&2
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

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

echo "--- cluster (${provider})"
cluster_created=true
if [[ ${provider} == kind ]]; then
  kind create cluster --name "${cluster}" --image "${kind_image}" --kubeconfig "${KUBECONFIG}" --wait 300s
else
  k3d cluster create "${cluster}" \
    --image "${k3s_image}" \
    --no-lb \
    --k3s-arg '--disable=traefik@server:0' \
    --k3s-arg '--disable=metrics-server@server:0' \
    --kubeconfig-update-default=false \
    --wait --timeout 300s
  k3d kubeconfig get "${cluster}" >"${KUBECONFIG}"
fi
chmod 600 "${KUBECONFIG}"
cluster_up=true
kubectl wait --for=condition=Ready nodes --all --timeout=300s

# The images get a fixed, registry-qualified name, the same with Docker and Podman.
for component in server web migrations keycloak; do
  tag="localhost/coldframe/${component}:smoke"
  docker tag "${source_images[${component}]}" "${tag}"
  smoke_tags+=("${tag}")
done
if [[ ${provider} == kind ]]; then
  # Straight into the node's containerd: "kind load" cannot read the containerd configuration of
  # every node image.
  for tag in "${smoke_tags[@]}"; do
    docker save "${tag}" | docker exec -i "${cluster}-control-plane" ctr --namespace=k8s.io images import -
  done
else
  k3d image import --cluster "${cluster}" "${smoke_tags[@]}"
fi

echo "--- placeholder Secrets and the test database"
kubectl create namespace "${namespace}"
ns=(--namespace "${namespace}")
kubectl "${ns[@]}" create secret generic smoke-postgres-superuser --from-literal=password="$(random_secret)"
kubectl "${ns[@]}" create secret generic coldframe-db-server --type=kubernetes.io/basic-auth \
  --from-literal=username=coldframe --from-literal=password="$(random_secret)"
kubectl "${ns[@]}" create secret generic coldframe-db-temporal --type=kubernetes.io/basic-auth \
  --from-literal=username=temporal --from-literal=password="$(random_secret)"
kubectl "${ns[@]}" create secret generic coldframe-db-keycloak --type=kubernetes.io/basic-auth \
  --from-literal=username=keycloak --from-literal=password="$(random_secret)"
kubectl "${ns[@]}" create secret generic coldframe-keycloak-admin \
  --from-literal=username=admin --from-literal=password="$(random_secret)"
kubectl "${ns[@]}" create secret generic coldframe-oidc-clients \
  --from-literal=web-client-secret="$(random_secret)" --from-literal=server-client-secret="$(random_secret)"
kubectl "${ns[@]}" create configmap coldframe-realm \
  --from-file=coldframe-realm.json="${repo}/aspire/keycloak/realms/coldframe-realm.json"

kubectl "${ns[@]}" apply -f "${here}/testdata/postgres.yaml"
kubectl "${ns[@]}" rollout status deployment/smoke-postgres --timeout=300s

# install <release> [helm arguments...]: installs deploy/charts/<release> and waits for it.
install() {
  local release=$1
  shift
  local values=()
  [[ -f ${here}/${release}/ci-values.yaml ]] && values=(--values "${here}/${release}/ci-values.yaml")
  echo "--- helm install ${release}"
  helm install "${release}" "${here}/${release}" "${ns[@]}" "${values[@]}" \
    --wait --timeout "${timeout}" "$@"
}

image_args() {
  echo --set "$1.repository=localhost/coldframe/$2" --set "$1.tag=smoke" --set "$1.pullPolicy=Never"
}

# shellcheck disable=SC2046 # image_args prints separate arguments on purpose.
{
  install nats
  install temporal
  install keycloak $(image_args image keycloak) --set realmImport.configMap=coldframe-realm
  install server $(image_args image server) $(image_args migrations.image migrations)
  install web $(image_args image web)
}

echo "--- checks"
not_ready=$(kubectl "${ns[@]}" get pods -o json | jq -r '
  .items[]
  | select(
      if .status.phase == "Succeeded" then
        ([.metadata.ownerReferences[]? | select(.kind == "Job")] | length) == 0
      else
        ([.status.conditions[]? | select(.type == "Ready" and .status == "True")] | length) == 0
      end)
  | "\(.metadata.name) (\(.status.phase))"')
[[ -z ${not_ready} ]] || fail "pods neither Ready nor completed Jobs: ${not_ready}"
echo "ok: every pod is Ready or a completed Job"

health=$(kubectl get --raw "/api/v1/namespaces/${namespace}/services/http:server:http/proxy/.well-known/healthz") \
  || fail "GET /.well-known/healthz on the Server Service did not answer 200"
# The Escendit service defaults answer a JSON report; its overall status must be Healthy.
status=$(jq -r 'if type == "object" then .status else . end' <<<"${health}" 2>/dev/null || echo "${health}")
[[ ${status} == "Healthy" ]] || fail "the Server's /.well-known/healthz answered '${health}', expected status Healthy"
echo "ok: the Server's /.well-known/healthz answers 200 Healthy"

issuer=$(kubectl get --raw "/api/v1/namespaces/${namespace}/services/http:keycloak:http/proxy/realms/coldframe/.well-known/openid-configuration" \
  | jq -r '.issuer // empty') || true
[[ -n ${issuer} ]] || fail "Keycloak does not serve the imported coldframe realm"
echo "ok: Keycloak serves the imported coldframe realm (issuer ${issuer})"

echo "--- helm upgrade temporal"
# The schema hook runs again on an up-to-date database and the namespace hook finds "coldframe":
# both must succeed and leave the namespace as it is.
schema_before=$(kubectl "${ns[@]}" get job temporal-schema -o jsonpath='{.metadata.uid}')
helm upgrade temporal "${here}/temporal" "${ns[@]}" --wait --timeout "${timeout}"
schema_after=$(kubectl "${ns[@]}" get job temporal-schema -o jsonpath='{.metadata.uid}')
[[ ${schema_after} != "${schema_before}" ]] || fail "helm upgrade temporal did not run the schema hook Job again"
for job in temporal-schema temporal-namespace; do
  succeeded=$(kubectl "${ns[@]}" get job "${job}" -o jsonpath='{.status.succeeded}')
  [[ ${succeeded} == "1" ]] || fail "the ${job} hook Job did not complete on upgrade (succeeded=${succeeded:-0})"
done
kubectl "${ns[@]}" logs job/temporal-namespace | grep -q "namespace coldframe exists" \
  || fail "the namespace hook Job did not find the existing coldframe namespace"
echo "ok: re-running the Temporal hooks on a current schema succeeds and keeps the namespace"

echo "--- helm upgrade server, failing migration"
generation_before=$(kubectl "${ns[@]}" get deployment server -o jsonpath='{.metadata.generation}')
# An unreachable database host makes the migration Job exit 1; the Deployment must stay as it is.
# shellcheck disable=SC2046 # image_args prints separate arguments on purpose.
if helm upgrade server "${here}/server" "${ns[@]}" --values "${here}/server/ci-values.yaml" \
  $(image_args image server) $(image_args migrations.image migrations) \
  --set database.host=coldframe-db-absent.invalid --wait --timeout 300s; then
  fail "helm upgrade succeeded although the migration Job failed"
fi
# The upgrade must have failed because of the migration, not a timeout, pull or render error.
migration_failures=$(kubectl "${ns[@]}" get job server-migrations -o jsonpath='{.status.failed}' || true)
[[ ${migration_failures:-0} -ge 1 ]] \
  || fail "helm upgrade failed, but not because of the migration Job (status.failed=${migration_failures:-0})"
generation_after=$(kubectl "${ns[@]}" get deployment server -o jsonpath='{.metadata.generation}')
[[ ${generation_after} == "${generation_before}" ]] \
  || fail "the Server Deployment changed (generation ${generation_before} -> ${generation_after}) although the migration failed"
echo "ok: a failed migration fails the upgrade and leaves the Server Deployment unchanged"

echo "--- helm upgrade server"
job_before=$(kubectl "${ns[@]}" get job server-migrations -o jsonpath='{.metadata.uid}')
# shellcheck disable=SC2046 # image_args prints separate arguments on purpose.
helm upgrade server "${here}/server" "${ns[@]}" --values "${here}/server/ci-values.yaml" \
  $(image_args image server) $(image_args migrations.image migrations) \
  --wait --timeout "${timeout}"
job_after=$(kubectl "${ns[@]}" get job server-migrations -o jsonpath='{.metadata.uid}')
[[ ${job_after} != "${job_before}" ]] || fail "helm upgrade did not run the migration hook Job again"
succeeded=$(kubectl "${ns[@]}" get job server-migrations -o jsonpath='{.status.succeeded}')
[[ ${succeeded} == "1" ]] || fail "the migration hook Job did not complete on upgrade (succeeded=${succeeded:-0})"
echo "ok: the migration hook Job ran again and completed on upgrade"
kubectl "${ns[@]}" rollout status deployment/server --timeout=300s

echo "Chart smoke install passed."
