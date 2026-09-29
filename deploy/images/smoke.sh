#!/usr/bin/env bash
# Starts the three Coldframe images against their real dependencies and checks they come up.
#
#   deploy/images/smoke.sh <server-image> <web-image> <migrations-image>
#
# 1. PostgreSQL, NATS (JetStream) and the Temporal dev server start on a private network, with
#    the same images the Aspire AppHost uses.
# 2. The migration job must exit 0 with a connection string, and 1 without one.
# 3. The Server must answer 200 on /.well-known/healthz/live and /ready within SMOKE_TIMEOUT.
# 4. The web app must answer 200 on the same two paths. Its issuer points at a host that does not
#    exist: the health endpoints must not need Keycloak.
#
# Keycloak is not started: the Server's readiness does not depend on it (the service account and
# the token metadata are fetched on first use).
#
# Every credential is generated per run. Containers and the network are always removed; on
# failure their logs are printed first.
set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "usage: $0 <server-image> <web-image> <migrations-image>" >&2
  exit 2
fi

server_image=$1
web_image=$2
migrations_image=$3

timeout_seconds=${SMOKE_TIMEOUT:-180}
postgres_image=docker.io/library/postgres:18.6
nats_image=docker.io/library/nats:2.15.0
temporal_image=docker.io/temporalio/temporal:1.8.3

run_id="cf-smoke-$$-${RANDOM}"
network=${run_id}
containers=()

random_secret() {
  od -An -N24 -tx1 /dev/urandom | tr -d ' \n'
}

postgres_password=$(random_secret)
client_secret=$(random_secret)
# Throwaway Device enrolment keys: an X25519 PKCS#8 PEM (its newlines are kept) and a KEK of 48 characters.
enrolment_key_pem=$(openssl genpkey -algorithm X25519)
device_kek=$(random_secret)
connection_string="Host=postgres;Port=5432;Database=coldframe;Username=postgres;Password=${postgres_password}"

cleanup() {
  local status=$?
  if [[ ${status} -ne 0 ]]; then
    for container in "${containers[@]}"; do
      echo "::group::logs of ${container}"
      docker logs "${container}" 2>&1 | tail -n 200 || true
      echo "::endgroup::"
    done
    echo "Smoke run FAILED." >&2
  fi
  if [[ ${#containers[@]} -gt 0 ]]; then
    docker rm --force "${containers[@]}" >/dev/null 2>&1 || true
  fi
  docker network rm "${network}" >/dev/null 2>&1 || true
  exit "${status}"
}
trap cleanup EXIT

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

# start <alias> <docker run arguments...>: a detached container reachable as <alias> on the network.
start() {
  local alias=$1
  shift
  local name="${run_id}-${alias}"
  containers+=("${name}")
  docker run --detach --name "${name}" --network "${network}" --network-alias "${alias}" "$@" >/dev/null
}

# wait_until <description> <command...>: retries the command every 2 s until the deadline.
wait_until() {
  local description=$1
  shift
  local deadline=$((SECONDS + timeout_seconds))
  until "$@" >/dev/null 2>&1; do
    if ((SECONDS >= deadline)); then
      fail "${description} not reached within ${timeout_seconds} s"
    fi
    sleep 2
  done
  echo "ok: ${description}"
}

# http_ok <url>: true when the URL answers 200.
http_ok() {
  [[ "$(curl --silent --output /dev/null --max-time 5 --write-out '%{http_code}' "$1")" == "200" ]]
}

# host_url <container> <port>: the loopback URL of a published container port.
host_url() {
  local mapping
  mapping=$(docker port "${run_id}-$1" "$2/tcp" | head -n 1)
  echo "http://127.0.0.1:${mapping##*:}"
}

docker network create "${network}" >/dev/null

echo "--- dependencies"
start postgres --env POSTGRES_PASSWORD="${postgres_password}" --env POSTGRES_DB=coldframe "${postgres_image}"
start nats "${nats_image}" -js
start temporal "${temporal_image}" server start-dev --ip 0.0.0.0 --namespace coldframe

wait_until "PostgreSQL accepts connections" \
  docker exec "${run_id}-postgres" pg_isready --username postgres --dbname coldframe --host 127.0.0.1
wait_until "Temporal serves the coldframe namespace" \
  docker exec "${run_id}-temporal" temporal operator namespace describe --namespace coldframe --address 127.0.0.1:7233

echo "--- migrations"
docker run --rm --network "${network}" --env ConnectionStrings__coldframe="${connection_string}" "${migrations_image}" \
  || fail "the migration job exited non-zero with a valid connection string"
echo "ok: migrations exit 0"

set +e
docker run --rm "${migrations_image}"
misconfigured=$?
set -e
[[ ${misconfigured} -eq 1 ]] || fail "the migration job without a connection string exited ${misconfigured}, expected 1"
echo "ok: migrations without a connection string exit 1"

echo "--- server"
start server --publish 127.0.0.1::8080 \
  --env ConnectionStrings__coldframe="${connection_string}" \
  --env ConnectionStrings__nats=nats://nats:4222 \
  --env Identity__Authority=http://keycloak.invalid/realms/coldframe \
  --env Identity__Audience=coldframe-server \
  --env Identity__RequireHttpsMetadata=false \
  --env Keycloak__BaseUrl=http://keycloak.invalid \
  --env Keycloak__Realm=coldframe \
  --env Keycloak__ClientId=coldframe-server \
  --env Keycloak__ClientSecret="${client_secret}" \
  --env KeycloakEvents__TargetHost=temporal:7233 \
  --env KeycloakEvents__Namespace=coldframe \
  --env KeycloakEvents__RealmId=coldframe \
  --env Enrolment__PrivateKeyPem="${enrolment_key_pem}" \
  --env Enrolment__DeviceKeyEncryptionKey="${device_kek}" \
  "${server_image}"

server_url=$(host_url server 8080)
wait_until "server /.well-known/healthz/live is 200" http_ok "${server_url}/.well-known/healthz/live"
wait_until "server /.well-known/healthz/ready is 200" http_ok "${server_url}/.well-known/healthz/ready"

echo "--- web"
start web --publish 127.0.0.1::3000 \
  --env COLDFRAME_SERVER_URL=http://server:8080 \
  --env KEYCLOAK_ISSUER=http://keycloak.invalid/realms/coldframe \
  --env KEYCLOAK_CLIENT_ID=coldframe-web \
  --env KEYCLOAK_CLIENT_SECRET="${client_secret}" \
  --env KEYCLOAK_ALLOW_INSECURE_HTTP=true \
  "${web_image}"

web_url=$(host_url web 3000)
wait_until "web /.well-known/healthz/live is 200" http_ok "${web_url}/.well-known/healthz/live"
wait_until "web /.well-known/healthz/ready is 200" http_ok "${web_url}/.well-known/healthz/ready"

echo "Smoke run passed."
