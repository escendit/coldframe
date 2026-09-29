#!/usr/bin/env bash
# Installs the seven charts on a disposable k3d cluster, checks that the stack comes up over HTTPS
# through RKE2's Traefik, and proves a backup-and-restore round trip of the database.
#
#   deploy/charts/smoke.sh <server-image> <web-image> <migrations-image> <keycloak-image>
#
# 1. A k3d cluster (k3s pinned below) starts; the four Coldframe images are imported into it.
# 2. cert-manager, CloudNativePG and the Barman Cloud plugin are installed from the manifests
#    pinned in dependencies.env (each checked against its sha256), and RKE2's Traefik
#    (rke2-traefik-crd and rke2-traefik, pinned there too) in kube-system with the values of
#    deploy/rke2/rke2-traefik-config.yaml, as RKE2's helm-controller would install it.
# 3. Namespace "coldframe" gets placeholder Secrets with random values, created with kubectl as an
#    adopter would (deploy/SECRETS.md), and RustFS as the S3 backup target (testdata/rustfs.yaml).
# 4. helm install database (CI values: RustFS); the cluster must be Ready and archiving WAL, with
#    its three login roles and four databases. Then helm install --wait: nats, temporal, keycloak
#    (importing the Aspire realm), server, web.
# 5. Every pod must be Ready, or Succeeded and owned by a Job; the Server's /.well-known/healthz
#    must answer 200 "Healthy" through the API server's Service proxy, and Keycloak must serve
#    the imported coldframe realm.
# 5a. TLS (docs/operations/split-dns.md): a test CA (testdata/smoke-ca.yaml) stands in for Let's
#    Encrypt; helm install ingress (CI values: external issuer smoke-ca, domain
#    coldframe.smoke.test). The Certificate must be Ready with a renewalTime before notAfter. Through
#    a port-forward to Traefik's 443, curl (--resolve, --cacert) must reach the Server's health,
#    Keycloak's realm (issuer https://auth.<domain>:<port>/realms/coldframe) and the web app's
#    readiness. Traefik's Service and DaemonSet must expose no port 80 and no web entrypoint.
# 6. helm upgrade temporal: the schema and namespace hooks re-run on a current schema and keep
#    the existing namespace.
# 7. helm upgrade server with an unreachable database: the migration hook Job fails, the upgrade
#    fails and the Server Deployment is unchanged.
# 8. helm upgrade server: the migration hook Job must run again and complete.
# 9. Backup and restore (docs/operations/restore.md): with the apps stopped, marker A is written,
#    a base backup taken, marker B written and its WAL segment archived; the immediate scheduled
#    backup must have completed. The database release is uninstalled (cluster and volumes gone)
#    and installed again in recovery mode, archiving to a new folder. The restored cluster must
#    hold both markers and the same row count in every table of the four databases, and archive
#    WAL again; with the apps scaled back, the checks of step 5 must pass.
#
# Needs k3d, kubectl, helm, jq, curl, sha256sum, python3 with PyYAML and docker (or Podman through
# DOCKER_HOST). On failure the pods, events, CloudNativePG and cert-manager objects and logs
# (operators and Traefik included) are printed; the cluster is always deleted.
#
# SMOKE_CLUSTER=kind runs the same checks on kind (kindest/node, pinned below) instead: a fallback
# for hosts where k3s cannot start, such as rootless Podman without the cpuset cgroup controller
# delegated. CI uses k3d. SMOKE_TIMEOUT (seconds, default 600) bounds each helm install and wait.
set -euo pipefail

if [[ $# -ne 4 ]]; then
  echo "usage: $0 <server-image> <web-image> <migrations-image> <keycloak-image>" >&2
  exit 2
fi

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo=$(cd "${here}/../.." && pwd)
# shellcheck source=SCRIPTDIR/dependencies.env
source "${here}/dependencies.env"

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
db_cluster=coldframe-db
cluster="cf-smoke-$$-${RANDOM}"
work=$(mktemp -d)
export KUBECONFIG=${work}/kubeconfig
cluster_created=false
cluster_up=false
smoke_tags=()
port_forward_pid=""
domain=coldframe.smoke.test

case ${provider} in
  k3d | kind) ;;
  *) echo "SMOKE_CLUSTER must be k3d or kind, not '${provider}'" >&2; exit 2 ;;
esac

random_secret() {
  od -An -N24 -tx1 /dev/urandom | tr -d ' \n'
}

cleanup() {
  local status=$?
  if [[ -n ${port_forward_pid} ]]; then
    kill "${port_forward_pid}" 2>/dev/null || true
    wait "${port_forward_pid}" 2>/dev/null || true
  fi
  if [[ ${status} -ne 0 && ${cluster_up} == true ]]; then
    echo "::group::pods"
    kubectl -n "${namespace}" get pods,jobs -o wide 2>&1 || true
    echo "::endgroup::"
    echo "::group::events"
    kubectl -n "${namespace}" get events --sort-by=.lastTimestamp 2>&1 | tail -n 100 || true
    echo "::endgroup::"
    echo "::group::database objects"
    kubectl -n "${namespace}" get clusters,databases,backups,scheduledbackups,objectstores,pvc -o wide 2>&1 || true
    kubectl -n "${namespace}" get clusters -o jsonpath='{range .items[*]}{.metadata.name}{": "}{.status.conditions}{"\n"}{end}' 2>&1 || true
    echo "::endgroup::"
    echo "::group::TLS objects"
    kubectl -n "${namespace}" get issuers,certificates,certificaterequests,ingresses -o wide 2>&1 || true
    kubectl -n "${namespace}" get certificates -o jsonpath='{range .items[*]}{.metadata.name}{": "}{.status}{"\n"}{end}' 2>&1 || true
    kubectl -n kube-system get svc,daemonset -l app.kubernetes.io/name=rke2-traefik -o wide 2>&1 || true
    echo "::endgroup::"
    local pod deployment
    for pod in $(kubectl -n "${namespace}" get pods -o name 2>/dev/null); do
      echo "::group::logs of ${pod}"
      kubectl -n "${namespace}" logs "${pod}" --all-containers --tail=200 2>&1 || true
      echo "::endgroup::"
    done
    for deployment in cnpg-system/cnpg-controller-manager cnpg-system/barman-cloud cert-manager/cert-manager; do
      echo "::group::logs of ${deployment}"
      kubectl -n "${deployment%%/*}" logs "deployment/${deployment#*/}" --tail=200 2>&1 || true
      echo "::endgroup::"
    done
    echo "::group::logs of kube-system/rke2-traefik"
    kubectl -n kube-system logs daemonset/rke2-traefik --tail=200 2>&1 || true
    echo "::endgroup::"
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

# fetch <file> <url> <sha256>: downloads a pinned manifest into the work directory and checks it.
fetch() {
  curl --fail --silent --show-error --location --output "${work}/$1" "$2"
  echo "$3  ${work}/$1" | sha256sum --check --strict --quiet \
    || fail "$2 does not match its pinned sha256"
}

echo "--- pinned manifests"
fetch cert-manager.yaml "${CERT_MANAGER_URL}" "${CERT_MANAGER_SHA256}"
fetch cnpg.yaml "${CNPG_URL}" "${CNPG_SHA256}"
fetch barman-cloud.yaml "${BARMAN_CLOUD_PLUGIN_URL}" "${BARMAN_CLOUD_PLUGIN_SHA256}"
fetch rke2-traefik-crd.tgz "${RKE2_TRAEFIK_CRD_URL}" "${RKE2_TRAEFIK_CRD_SHA256}"
fetch rke2-traefik.tgz "${RKE2_TRAEFIK_URL}" "${RKE2_TRAEFIK_SHA256}"
# The values RKE2's helm-controller layers over the chart: the HelmChartConfig's valuesContent.
python3 -c '
import sys, yaml
config = next(doc for doc in yaml.safe_load_all(open(sys.argv[1], encoding="utf-8")) if doc)
sys.stdout.write(config["spec"]["valuesContent"])
' "${repo}/deploy/rke2/rke2-traefik-config.yaml" >"${work}/rke2-traefik-values.yaml"

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
# Straight into the node's containerd: "kind load" cannot read the containerd configuration of
# every node image, and "k3d image import" goes through a tools node and a shared volume and can
# report success after the node failed to read the tarball.
if [[ ${provider} == kind ]]; then
  node_container=${cluster}-control-plane
else
  node_container=k3d-${cluster}-server-0
fi
for tag in "${smoke_tags[@]}"; do
  docker save "${tag}" | docker exec -i "${node_container}" ctr --namespace=k8s.io images import -
  docker exec "${node_container}" ctr --namespace=k8s.io images ls --quiet | grep --quiet --fixed-strings --line-regexp "${tag}" \
    || fail "image ${tag} is not in the containerd of ${node_container} after the import"
done

echo "--- cert-manager ${CERT_MANAGER_VERSION}, CloudNativePG ${CNPG_VERSION}, Barman Cloud plugin ${BARMAN_CLOUD_PLUGIN_VERSION}"
kubectl apply -f "${work}/cert-manager.yaml" >/dev/null
# Server-side: the CNPG CRDs are too large for the last-applied annotation.
kubectl apply --server-side -f "${work}/cnpg.yaml" >/dev/null
for deployment in cert-manager cert-manager-cainjector cert-manager-webhook; do
  kubectl -n cert-manager rollout status "deployment/${deployment}" --timeout=300s
done
# The DNS-01 resolver flags of docs/operations/split-dns.md, added as the README does (only when
# missing): cert-manager must start with them.
for flag in --dns01-recursive-nameservers-only --dns01-recursive-nameservers=1.1.1.1:53,9.9.9.9:53; do
  kubectl -n cert-manager get deployment cert-manager \
    -o jsonpath='{.spec.template.spec.containers[0].args}' | grep -F -- "\"${flag}\"" >/dev/null \
    || kubectl -n cert-manager patch deployment cert-manager --type=json \
      -p "[{\"op\": \"add\", \"path\": \"/spec/template/spec/containers/0/args/-\", \"value\": \"${flag}\"}]"
done
kubectl -n cert-manager rollout status deployment/cert-manager --timeout=300s
kubectl -n cnpg-system rollout status deployment/cnpg-controller-manager --timeout=300s
# The plugin's Certificates go through the cert-manager webhook, which can lag its rollout.
for attempt in $(seq 1 30); do
  if kubectl apply -f "${work}/barman-cloud.yaml" >/dev/null 2>"${work}/apply.err"; then
    break
  fi
  [[ ${attempt} -lt 30 ]] || fail "the Barman Cloud plugin manifest does not apply: $(cat "${work}/apply.err")"
  sleep 5
done
kubectl -n cnpg-system rollout status deployment/barman-cloud --timeout=300s

echo "--- rke2-traefik ${RKE2_TRAEFIK_VERSION} (deploy/rke2/rke2-traefik-config.yaml)"
helm install rke2-traefik-crd "${work}/rke2-traefik-crd.tgz" --namespace kube-system --wait --timeout 300s
helm install rke2-traefik "${work}/rke2-traefik.tgz" --namespace kube-system \
  --values "${work}/rke2-traefik-values.yaml" --wait --timeout "${timeout}"

echo "--- placeholder Secrets and the S3 target"
kubectl create namespace "${namespace}"
ns=(--namespace "${namespace}")
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
openssl genpkey -algorithm X25519 -out "${work}/enrolment-private-key.pem"
kubectl "${ns[@]}" create secret generic coldframe-enrolment-key \
  --from-file=private-key.pem="${work}/enrolment-private-key.pem"
rm -f "${work}/enrolment-private-key.pem"
kubectl "${ns[@]}" create secret generic coldframe-device-kek --from-literal=kek="$(random_secret)"
kubectl "${ns[@]}" create configmap coldframe-realm \
  --from-file=coldframe-realm.json="${repo}/aspire/keycloak/realms/coldframe-realm.json"

sed -e "s|@RUSTFS_IMAGE@|${RUSTFS_IMAGE}|" -e "s|@AWS_CLI_IMAGE@|${AWS_CLI_IMAGE}|" "${here}/testdata/rustfs.yaml" \
  | kubectl "${ns[@]}" apply -f -
kubectl "${ns[@]}" rollout status deployment/rustfs --timeout=300s
kubectl "${ns[@]}" wait job/rustfs-bucket --for=condition=Complete --timeout=300s

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

# install_database [helm arguments...]: installs the database chart (CI values: RustFS) and waits
# until the cluster is Ready, archives WAL and has applied its Database objects.
install_database() {
  echo "--- helm install database $*"
  helm install database "${here}/database" "${ns[@]}" --values "${here}/database/ci-values.yaml" \
    --timeout "${timeout}" "$@"
  kubectl "${ns[@]}" wait "cluster/${db_cluster}" --for=condition=Ready --timeout="${timeout}"
  kubectl "${ns[@]}" wait "cluster/${db_cluster}" --for=condition=ContinuousArchiving --timeout=300s
  kubectl "${ns[@]}" wait databases --all --for=jsonpath='{.status.applied}'=true --timeout=300s
}

# primary: the name of the cluster's primary pod.
primary() {
  kubectl "${ns[@]}" get pods -l "cnpg.io/cluster=${db_cluster},cnpg.io/instanceRole=primary" \
    -o jsonpath='{.items[0].metadata.name}'
}

# sql <database> <statement>: runs one statement as the postgres superuser over the primary's
# local socket (as `kubectl cnpg psql` does) and prints the result unaligned, without headers.
sql() {
  kubectl "${ns[@]}" exec "$(primary)" -c postgres -- \
    psql --no-psqlrc --quiet --tuples-only --no-align -v ON_ERROR_STOP=1 --dbname "$1" --command "$2"
}

# fingerprint: the exact row count of every table in the four databases, one line per database.
fingerprint() {
  local database
  for database in coldframe temporal temporal_visibility keycloak; do
    printf '%s: ' "${database}"
    sql "${database}" "
      SELECT coalesce(string_agg(format('%I.%I=%s', table_schema, table_name,
               (xpath('/row/c/text()', query_to_xml(format('SELECT count(*) AS c FROM %I.%I',
                 table_schema, table_name), false, true, '')))[1]::text), ','
               ORDER BY table_schema, table_name), '')
        FROM information_schema.tables
       WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ('pg_catalog', 'information_schema')"
  done
}

# wait_archived <label>: switches to a new WAL segment and waits until the closed one is archived.
wait_archived() {
  local segment
  segment=$(sql postgres "SELECT pg_walfile_name(pg_switch_wal())")
  for _ in $(seq 1 150); do
    if [[ $(sql postgres "SELECT coalesce(last_archived_wal >= '${segment}', false) FROM pg_stat_archiver") == "t" ]]; then
      echo "ok: $1: WAL segment ${segment} archived"
      return
    fi
    sleep 2
  done
  fail "$1: WAL segment ${segment} was not archived within 5 minutes"
}

# wait_stopped <deployment>: waits until no pod of the Deployment is left (its hook Jobs' pods,
# which carry the same labels, stay).
wait_stopped() {
  local left
  for _ in $(seq 1 150); do
    left=$(kubectl "${ns[@]}" get pods -l "app.kubernetes.io/instance=$1" -o json \
      | jq '[.items[] | select(any(.metadata.ownerReferences[]?; .kind == "ReplicaSet"))] | length')
    [[ ${left} == 0 ]] && return
    sleep 2
  done
  fail "the pods of deployment/$1 did not stop within 5 minutes"
}

# check_stack: every pod Ready (or a completed Job), the Server healthy, the realm served.
check_stack() {
  local not_ready health status issuer
  not_ready=$(kubectl "${ns[@]}" get pods -o json | jq -r '
    .items[]
    | select(.metadata.deletionTimestamp == null)
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
}

install_database
roles=$(sql postgres "SELECT string_agg(rolname, ',' ORDER BY rolname) FROM pg_roles WHERE rolname IN ('coldframe', 'temporal', 'keycloak') AND rolcanlogin")
[[ ${roles} == "coldframe,keycloak,temporal" ]] || fail "the login roles are '${roles}', expected coldframe, keycloak and temporal"
owners=$(sql postgres "SELECT string_agg(datname || '=' || pg_get_userbyid(datdba), ',' ORDER BY datname) FROM pg_database WHERE datname IN ('coldframe', 'temporal', 'temporal_visibility', 'keycloak')")
[[ ${owners} == "coldframe=coldframe,keycloak=keycloak,temporal=temporal,temporal_visibility=temporal" ]] \
  || fail "the databases and their owners are '${owners}'"
echo "ok: the database cluster is Ready and archiving WAL, with its roles and databases (${owners})"

# shellcheck disable=SC2046 # image_args prints separate arguments on purpose.
{
  install nats
  install temporal
  install keycloak $(image_args image keycloak) --set realmImport.configMap=coldframe-realm
  install server $(image_args image server) $(image_args migrations.image migrations)
  install web $(image_args image web)
}

echo "--- checks"
check_stack

# https_get <host> <path>: GET https://<host>:<port><path> through Traefik's websecure port, the
# host resolved to the port-forward, trusting the smoke CA only.
https_get() {
  curl --fail --silent --show-error --max-time 30 \
    --resolve "$1:${https_port}:127.0.0.1" --cacert "${work}/smoke-ca.crt" \
    "https://$1:${https_port}$2"
}

echo "--- TLS: the ingress chart, a test CA and Traefik on 443 only"
kubectl "${ns[@]}" apply -f "${here}/testdata/smoke-ca.yaml"
kubectl "${ns[@]}" wait certificate/smoke-ca --for=condition=Ready --timeout=300s
install ingress
kubectl "${ns[@]}" wait certificate/coldframe-tls --for=condition=Ready --timeout=300s
dns_names=$(kubectl "${ns[@]}" get certificate coldframe-tls -o jsonpath='{.spec.dnsNames}' | jq -r 'join(",")')
[[ ${dns_names} == "${domain},api.${domain},auth.${domain}" ]] \
  || fail "the Certificate coldframe-tls names '${dns_names}', expected the three hosts of ${domain}"
renewal=$(kubectl "${ns[@]}" get certificate coldframe-tls -o jsonpath='{.status.renewalTime}')
not_after=$(kubectl "${ns[@]}" get certificate coldframe-tls -o jsonpath='{.status.notAfter}')
[[ -n ${renewal} && -n ${not_after} ]] || fail "the Certificate has no renewalTime (${renewal}) or notAfter (${not_after})"
[[ $(date -u -d "${renewal}" +%s) -lt $(date -u -d "${not_after}" +%s) ]] \
  || fail "the Certificate renews at ${renewal}, not before it expires at ${not_after}"
echo "ok: the Certificate coldframe-tls is Ready for ${dns_names}, renewing at ${renewal} (expires ${not_after})"

kubectl "${ns[@]}" get secret coldframe-tls -o jsonpath='{.data.ca\.crt}' | base64 --decode >"${work}/smoke-ca.crt"
[[ -s ${work}/smoke-ca.crt ]] || fail "the Secret coldframe-tls has no ca.crt"

kubectl -n kube-system port-forward svc/rke2-traefik :443 >"${work}/port-forward.log" 2>&1 &
port_forward_pid=$!
https_port=
for _ in $(seq 1 30); do
  https_port=$(sed -n -E 's/^Forwarding from 127\.0\.0\.1:([0-9]+) -> .*/\1/p' "${work}/port-forward.log" | head -n 1)
  [[ -n ${https_port} ]] && break
  kill -0 "${port_forward_pid}" 2>/dev/null || fail "kubectl port-forward to Traefik exited: $(cat "${work}/port-forward.log")"
  sleep 1
done
[[ -n ${https_port} ]] || fail "kubectl port-forward to Traefik did not start: $(cat "${work}/port-forward.log")"

# Traefik can take a moment to pick up the Ingress and its certificate.
health=
for _ in $(seq 1 30); do
  health=$(https_get "api.${domain}" /.well-known/healthz 2>"${work}/curl.err") && break
  sleep 2
done
[[ -n ${health} ]] || fail "GET https://api.${domain}/.well-known/healthz through Traefik failed: $(cat "${work}/curl.err")"
status=$(jq -r 'if type == "object" then .status else . end' <<<"${health}" 2>/dev/null || echo "${health}")
[[ ${status} == "Healthy" ]] || fail "https://api.${domain}/.well-known/healthz answered '${health}', expected status Healthy"
echo "ok: https://api.${domain}/.well-known/healthz answers Healthy over TLS from the test CA"

issuer=$(https_get "auth.${domain}" /realms/coldframe/.well-known/openid-configuration | jq -r '.issuer // empty') \
  || fail "GET https://auth.${domain}/realms/coldframe/.well-known/openid-configuration through Traefik failed"
[[ ${issuer} == "https://auth.${domain}:${https_port}/realms/coldframe" ]] \
  || fail "Keycloak's issuer through Traefik is '${issuer}', expected https://auth.${domain}:${https_port}/realms/coldframe"
echo "ok: Keycloak serves the coldframe realm over TLS (issuer ${issuer})"

https_get "${domain}" /.well-known/healthz/ready >/dev/null \
  || fail "GET https://${domain}/.well-known/healthz/ready through Traefik did not answer 200"
echo "ok: https://${domain}/.well-known/healthz/ready answers 200 over TLS"

traefik_service_ports=$(kubectl -n kube-system get svc rke2-traefik -o json \
  | jq -r '[.spec.ports[] | "\(.name):\(.port)" + (if .nodePort then "/\(.nodePort)" else "" end)] | join(",")')
[[ ${traefik_service_ports} == "websecure:443" ]] \
  || fail "Traefik's Service exposes '${traefik_service_ports}', expected websecure:443 only"
traefik_offenders=$(kubectl -n kube-system get daemonset rke2-traefik -o json | jq -r '
  .spec.template.spec.containers[]
  | ((.ports // [])[] | select(.name == "web" or .containerPort == 80 or .hostPort == 80) | "port \(.name)"),
    ((.args // [])[] | select(test("^--entry[pP]oints\\.web\\.")))')
[[ -z ${traefik_offenders} ]] || fail "Traefik's DaemonSet still has port 80 or the web entrypoint: ${traefik_offenders}"
echo "ok: Traefik exposes 443 only (Service ${traefik_service_ports}); no port 80, no web entrypoint"
kill "${port_forward_pid}" 2>/dev/null || true
wait "${port_forward_pid}" 2>/dev/null || true
port_forward_pid=""

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

echo "--- backup and restore"
# The apps stop first (docs/operations/restore.md): nothing writes while the data is compared.
apps=(server web keycloak temporal)
for app in "${apps[@]}"; do
  kubectl "${ns[@]}" scale "deployment/${app}" --replicas=0
done
for app in "${apps[@]}"; do
  wait_stopped "${app}"
done

# The immediate scheduled backup must be done before smoke-backup starts, so the two never overlap.
scheduled=
for _ in $(seq 1 150); do
  scheduled=$(kubectl "${ns[@]}" get backups -o json | jq -r '
    [.items[]
     | select(any(.metadata.ownerReferences[]?; .kind == "ScheduledBackup") and .status.phase == "completed")
     | .metadata.name] | join(",")')
  [[ -n ${scheduled} ]] && break
  sleep 2
done
[[ -n ${scheduled} ]] || fail "the immediate scheduled backup did not complete within 5 minutes"
echo "ok: the immediate scheduled backup completed (${scheduled})"

sql coldframe "CREATE TABLE smoke_marker (name text PRIMARY KEY, written_at timestamptz NOT NULL DEFAULT now())"
sql coldframe "INSERT INTO smoke_marker (name) VALUES ('A')"
# The base backup starts with a spread checkpoint, which takes minutes after the apps' writes; a
# checkpoint now leaves it nothing to flush.
sql postgres "CHECKPOINT"
kubectl "${ns[@]}" apply -f - <<EOF
apiVersion: postgresql.cnpg.io/v1
kind: Backup
metadata:
  name: smoke-backup
spec:
  cluster:
    name: ${db_cluster}
  method: plugin
  pluginConfiguration:
    name: barman-cloud.cloudnative-pg.io
EOF
kubectl "${ns[@]}" wait backup/smoke-backup --for=jsonpath='{.status.phase}'=completed --timeout="${timeout}"
echo "ok: marker A written, then the base backup smoke-backup completed"

# Marker B reaches the restored cluster only through the archived WAL.
sql coldframe "INSERT INTO smoke_marker (name) VALUES ('B')"
before=$(fingerprint)
wait_archived "marker B"

helm uninstall database "${ns[@]}" --wait --timeout 300s
for _ in $(seq 1 150); do
  left=$(kubectl "${ns[@]}" get "cluster/${db_cluster}" -o name --ignore-not-found
    kubectl "${ns[@]}" get pods,pvc -l "cnpg.io/cluster=${db_cluster}" -o name)
  [[ -z ${left} ]] && break
  sleep 2
done
[[ -z ${left} ]] || fail "the uninstall left the database objects ${left//$'\n'/, }"
echo "ok: the database release, its pods and its volumes are gone"

install_database --set recovery.enabled=true --set backup.serverName=coldframe-db-restored
markers=$(sql coldframe "SELECT string_agg(name, ',' ORDER BY name) FROM smoke_marker")
[[ ${markers} == "A,B" ]] || fail "the restored cluster holds the markers '${markers}', expected A,B"
echo "ok: the restored cluster holds marker A (base backup) and marker B (archived WAL)"
after=$(fingerprint)
[[ ${after} == "${before}" ]] || fail "the row counts differ after the restore:
before: ${before}
after:  ${after}"
echo "ok: every table of the four databases has the same row count after the restore"
wait_archived "the restored cluster archives to coldframe-db-restored"

for app in temporal keycloak server web; do
  kubectl "${ns[@]}" scale "deployment/${app}" --replicas=1
  kubectl "${ns[@]}" rollout status "deployment/${app}" --timeout="${timeout}"
done
check_stack

echo "Chart smoke install passed."
