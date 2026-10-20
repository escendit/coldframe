#!/usr/bin/env bash
# Writes the `coldframe` realm file for your hosts (Story 10.3).
#
#   deploy/realm/prepare-realm.sh --web-origin https://<domain> [--in <realm.json>] [--out <file>] [--allow-http]
#
# Reads the realm of the local stack (aspire/keycloak/realms/coldframe-realm.json, or --in) and
# sets the two redirect fields of the `coldframe-web` client to the web app's public origin:
#
#   redirectUris                          [<origin>/.oidc/signin/callback]
#   attributes."post.logout.redirect.uris" <origin>/.oidc/signout/callback
#
# Everything else stays as it is: the ${COLDFRAME_*} placeholders (Keycloak substitutes them from
# the Secrets at the first import), the `coldframe-mobile` client and its com.escendit.coldframe
# scheme, and the realm settings. The result goes to --out, or to stdout.
#
# The origin is a scheme and a host (and an optional port), without a path; a trailing slash is
# dropped. It must be https://, unless --allow-http is given, which is for the Compose reference
# example only (deploy/compose/README.md).
#
# Exit 0 on success, 1 when jq fails or the input holds no coldframe-web client, 2 on bad usage.
set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo=$(cd "${here}/../.." && pwd)

usage() {
  echo "usage: prepare-realm.sh --web-origin https://<domain> [--in <realm.json>] [--out <file>] [--allow-http]" >&2
  [[ $# -gt 0 ]] && echo "prepare-realm.sh: $1" >&2
  exit 2
}

input=${repo}/aspire/keycloak/realms/coldframe-realm.json
output=""
origin=""
allow_http=false

while [[ $# -gt 0 ]]; do
  case $1 in
    -h | --help)
      sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    --web-origin | --in | --out)
      [[ $# -ge 2 && -n $2 ]] || usage "$1 needs a value"
      case $1 in
        --web-origin) origin=$2 ;;
        --in) input=$2 ;;
        --out) output=$2 ;;
      esac
      shift 2
      ;;
    --web-origin=* | --in=* | --out=*)
      value=${1#*=}
      [[ -n ${value} ]] || usage "${1%%=*} needs a value"
      case $1 in
        --web-origin=*) origin=${value} ;;
        --in=*) input=${value} ;;
        --out=*) output=${value} ;;
      esac
      shift
      ;;
    --allow-http)
      allow_http=true
      shift
      ;;
    *) usage "unknown argument '$1'" ;;
  esac
done

[[ -n ${origin} ]] || usage "--web-origin is required"
origin=${origin%/}
if [[ ! ${origin} =~ ^(https?)://[A-Za-z0-9.-]+(:[0-9]{1,5})?$ ]]; then
  usage "'${origin}' is not an origin: a scheme and a host (and port), without a path, such as https://coldframe.example.org"
fi
if [[ ${BASH_REMATCH[1]} == http && ${allow_http} != true ]]; then
  usage "'${origin}' is plain http: the deployment needs https://. --allow-http is for the Compose reference example only"
fi
[[ -f ${input} ]] || usage "the input realm file '${input}' does not exist"

command -v jq > /dev/null || {
  echo "prepare-realm.sh: jq is required" >&2
  exit 1
}

if ! jq -e '[.clients[]? | select(.clientId == "coldframe-web")] | length == 1' "${input}" > /dev/null 2>&1; then
  echo "prepare-realm.sh: '${input}' is not a realm file with exactly one coldframe-web client" >&2
  exit 1
fi

realm=$(jq --indent 2 --arg origin "${origin}" '
  .clients |= map(
    if .clientId == "coldframe-web" then
      .redirectUris = [$origin + "/.oidc/signin/callback"]
      | .attributes["post.logout.redirect.uris"] = ($origin + "/.oidc/signout/callback")
    else . end)
' "${input}")

if [[ -n ${output} ]]; then
  printf '%s\n' "${realm}" > "${output}"
else
  printf '%s\n' "${realm}"
fi
