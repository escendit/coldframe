#!/usr/bin/env bash
# Plain-bash tests for the realm writer (Story 10.3): prepare-realm.sh against the realm of the
# local stack, with jq assertions on its output. Needs jq. Run from anywhere:
#
#   deploy/realm/test.sh
# The jq filters and placeholders are literal.
# shellcheck disable=SC2016
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo=$(cd "${here}/../.." && pwd)
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

# expect <name> <expected status> <expected stdout> <expected stderr> -- <command...>
# The expected stdout and stderr are substrings, "" for none at all, or - for anything.
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
  elif [[ -z ${want_out} && -n ${out} ]]; then
    failed "${name}" "stdout '${out}', expected none"
  elif [[ -n ${want_out} && ${want_out} != "-" && ${out} != *"${want_out}"* ]]; then
    failed "${name}" "stdout '${out}' does not contain '${want_out}'"
  elif [[ -z ${want_err} && -n ${err_text} ]]; then
    failed "${name}" "stderr '${err_text}', expected none"
  elif [[ -n ${want_err} && ${want_err} != "-" && ${err_text} != *"${want_err}"* ]]; then
    failed "${name}" "stderr '${err_text}' does not contain '${want_err}'"
  else
    pass "${name}"
  fi
}

# check <name> <file> <jq filter>: the filter is true on the file.
check() {
  if jq -e "$3" "$2" > /dev/null 2>&1; then
    pass "$1"
  else
    failed "$1" "jq '$3' is not true on $2"
  fi
}

prepare=${here}/prepare-realm.sh
source_realm=${repo}/aspire/keycloak/realms/coldframe-realm.json
web='.clients[] | select(.clientId == "coldframe-web")'

echo "# prepare-realm.sh"
domain=${work}/domain.json
expect "a realm for a domain" 0 "" "" \
  -- "${prepare}" --web-origin https://coldframe.example.org --out "${domain}"
check "the output is a realm" "${domain}" '.realm == "coldframe"'
check "the web sign-in redirect is the origin's callback" "${domain}" \
  "${web} | .redirectUris == [\"https://coldframe.example.org/.oidc/signin/callback\"]"
check "the web post-logout redirect is the origin's signout callback" "${domain}" \
  "${web} | .attributes[\"post.logout.redirect.uris\"] == \"https://coldframe.example.org/.oidc/signout/callback\""
check "the web client keeps its secret placeholder" "${domain}" \
  "${web} | .secret == \"\${COLDFRAME_WEB_CLIENT_SECRET}\""
check "the SMTP placeholders stay" "${domain}" \
  '.smtpServer.host == "${COLDFRAME_SMTP_HOST}" and .smtpServer.password == "${COLDFRAME_SMTP_PASSWORD:}"'
# Everything but the two web fields equals the source.
if diff <(jq -S "del(.clients[] | select(.clientId == \"coldframe-web\") | .redirectUris, .attributes[\"post.logout.redirect.uris\"])" "${source_realm}") \
  <(jq -S "del(.clients[] | select(.clientId == \"coldframe-web\") | .redirectUris, .attributes[\"post.logout.redirect.uris\"])" "${domain}") > /dev/null; then
  pass "nothing else changes: the mobile and server clients, placeholders and realm settings"
else
  failed "nothing else changes: the mobile and server clients, placeholders and realm settings" \
    "$(diff <(jq -S . "${source_realm}") <(jq -S . "${domain}") | head -20)"
fi
check "the mobile client keeps its scheme" "${domain}" \
  '.clients[] | select(.clientId == "coldframe-mobile") | .redirectUris == ["com.escendit.coldframe:/signin/callback"]'

if ! grep -q localhost "${domain}"; then
  pass "no localhost is left in the output"
else
  failed "no localhost is left in the output" "$(grep -n localhost "${domain}")"
fi

equals=${work}/equals.json
expect "the --web-origin=… and --out=… form" 0 "" "" \
  -- "${prepare}" --web-origin=https://eq.example.org --out="${equals}"
check "the =-form sets the redirect URIs" "${equals}" \
  "${web} | .redirectUris == [\"https://eq.example.org/.oidc/signin/callback\"]"
expect "an empty --web-origin= is refused" 2 "" "--web-origin needs a value" -- "${prepare}" --web-origin=

slash=${work}/slash.json
expect "a trailing slash" 0 "" "" -- "${prepare}" --web-origin https://x.example.org/ --out "${slash}"
check "a trailing slash is dropped" "${slash}" \
  "${web} | .redirectUris == [\"https://x.example.org/.oidc/signin/callback\"] and .attributes[\"post.logout.redirect.uris\"] == \"https://x.example.org/.oidc/signout/callback\""

expect "plain http is refused" 2 "" "--allow-http is for the Compose reference example only" \
  -- "${prepare}" --web-origin http://192.168.1.10:3000
http=${work}/http.json
expect "plain http with --allow-http" 0 "" "" \
  -- "${prepare}" --allow-http --web-origin http://192.168.1.10:3000 --out "${http}"
check "--allow-http uses that origin" "${http}" \
  "${web} | .redirectUris == [\"http://192.168.1.10:3000/.oidc/signin/callback\"] and .attributes[\"post.logout.redirect.uris\"] == \"http://192.168.1.10:3000/.oidc/signout/callback\""

expect "a missing origin is refused" 2 "" "usage:" -- "${prepare}"
expect "an origin with a path is refused" 2 "" "usage:" -- "${prepare}" --web-origin https://x.org/app
expect "an origin without a scheme is refused" 2 "" "usage:" -- "${prepare}" --web-origin x.org
expect "an unknown argument is refused" 2 "" "usage:" -- "${prepare}" --web-origin https://x.org --nope
expect "a missing input names the file" 2 "" "nope.json" \
  -- "${prepare}" --web-origin https://x.org --in "${work}/nope.json"

printf '{"realm": "coldframe", "clients": []}\n' > "${work}/no-web.json"
expect "an input without the web client fails" 1 "" "exactly one coldframe-web client" \
  -- "${prepare}" --web-origin https://x.org --in "${work}/no-web.json"

expect "without --out the realm goes to stdout" 0 '"https://coldframe.example.org/.oidc/signin/callback"' "" \
  -- "${prepare}" --web-origin https://coldframe.example.org
stdout_realm=${work}/stdout.json
"${prepare}" --web-origin https://coldframe.example.org > "${stdout_realm}"
check "stdout is valid JSON" "${stdout_realm}" '.realm == "coldframe"'

copy=${work}/copy.json
cp "${source_realm}" "${copy}"
expect "--in reads another file" 0 "" "" -- "${prepare}" --web-origin https://x.org --in "${copy}" --out "${work}/in.json"
check "--in output has the origin" "${work}/in.json" "${web} | .redirectUris == [\"https://x.org/.oidc/signin/callback\"]"

expect "--help prints the usage" 0 "--web-origin" "" -- "${prepare}" --help

echo
echo "${passes} passed, ${failures} failed"
[[ ${failures} -eq 0 ]]
