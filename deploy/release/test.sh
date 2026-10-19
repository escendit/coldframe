#!/usr/bin/env bash
# Plain-bash tests for the release scripts (Story 10.1): the wire gate, the release notes on a
# throwaway git repository, and the checksums. Needs git, jq and sha256sum. Run from anywhere:
#
#   deploy/release/test.sh
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

wire=${here}/check-wire-compat.sh
notes=${here}/release-notes.sh
checksums=${here}/checksums.sh

# spec <name> <protocolMajor> [oldestAcceptedProtocolMajor]: a spec file holding only the majors.
spec() {
  local file=${work}/$1.json
  if [[ $# -eq 3 ]]; then
    printf '{"protocolMajor": %s, "oldestAcceptedProtocolMajor": %s}\n' "$2" "$3" > "${file}"
  else
    printf '{"protocolMajor": %s}\n' "$2" > "${file}"
  fi
  echo "${file}"
}

echo "# check-wire-compat.sh"
expect "same major passes" 0 "Wire gate passes" "" \
  -- "${wire}" "$(spec prev-1 1 1)" "$(spec cur-1-1 1 1)"
expect "major bump, previous still accepted, passes" 0 "1 -> 2" "" \
  -- "${wire}" "$(spec prev-1 1 1)" "$(spec cur-2-1 2 1)"
expect "major bump from a spec without oldestAcceptedProtocolMajor passes" 0 "1 -> 2" "" \
  -- "${wire}" "$(spec prev-legacy 1)" "$(spec cur-2-1 2 1)"
expect "major bump, previous dropped, fails naming both majors" 1 "" \
  "protocolMajor goes from 1 (previous release) to 2, but the Server no longer accepts major 1" \
  -- "${wire}" "$(spec prev-1 1 1)" "$(spec cur-2-2 2 2)"
expect "first release passes with a notice" 0 "No previous release" "" \
  -- "${wire}" - "$(spec cur-1-1 1 1)"
expect "oldest above current fails" 1 "" "oldestAcceptedProtocolMajor 3 is greater than protocolMajor 2" \
  -- "${wire}" - "$(spec cur-2-3 2 3)"
expect "missing oldestAcceptedProtocolMajor fails" 2 "" "'oldestAcceptedProtocolMajor' is missing" \
  -- "${wire}" - "$(spec cur-legacy 1)"
expect "major decrease fails" 1 "" "protocolMajor goes down from 2 (previous release) to 1" \
  -- "${wire}" "$(spec prev-2 2 2)" "$(spec cur-1-1 1 1)"
expect "oldestAcceptedProtocolMajor 0 fails" 1 "" "'oldestAcceptedProtocolMajor' is 0; wire majors start at 1" \
  -- "${wire}" - "$(spec cur-1-0 1 0)"
expect "protocolMajor 0 fails" 1 "" "'protocolMajor' is 0; wire majors start at 1" \
  -- "${wire}" - "$(spec cur-0-0 0 0)"
expect "bad usage" 2 "" "usage:" -- "${wire}" -
expect "the repository spec passes as a first release" 0 "No previous release" "" \
  -- "${wire}" - "${repo}/packages/crypto-spec/crypto-spec.json"
expect "the repository spec passes against itself" 0 "Wire gate passes" "" \
  -- "${wire}" "${repo}/packages/crypto-spec/crypto-spec.json" "${repo}/packages/crypto-spec/crypto-spec.json"

echo "# release-notes.sh"
history=${work}/history
# The throwaway repository ignores the user's git configuration (signing, hooks, default branch).
export GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_NOSYSTEM=1
git init --quiet --initial-branch=main "${history}"
commit() { # <subject> [body]: an empty commit
  git -C "${history}" -c user.name=test -c user.email=test@example.org commit --quiet --allow-empty \
    --message "$1" ${2:+--message "$2"}
}
commit "chore(repo): initial commit"
commit "feat(server): first feature before the tag"
git -C "${history}" tag v0.1.0
commit "feat(hub): add the release workflow"
commit "fix(node): stop dropping frames"
commit "feat(api)!: rename the ingest route"
commit "refactor(server): change the ingest store" "BREAKING CHANGE: the store schema changes"
commit "perf(web): cache the sites list"
commit "docs: explain the release"
commit "wip: something unusual"
commit "Update the README"
git -C "${history}" switch --quiet --create topic
commit "fix(web): a fix on a branch"
git -C "${history}" switch --quiet main
git -C "${history}" -c user.name=test -c user.email=test@example.org merge --quiet --no-ff \
  --message "Merge pull request #1 from topic" topic

in_history() { (cd "${history}" && "$@"); }
release_notes=$(in_history "${notes}" 0.2.0 v0.1.0 HEAD)
notes_status=$?

# has <name> <substring>; lacks <name> <substring>
has() {
  if [[ ${release_notes} == *"$2"* ]]; then pass "$1"; else failed "$1" "notes do not contain '$2'" "${release_notes}"; fi
}
lacks() {
  if [[ ${release_notes} != *"$2"* ]]; then pass "$1"; else failed "$1" "notes contain '$2'" "${release_notes}"; fi
}
# in_section <name> <heading> <line>: <line> is in the section under "### <heading>"
in_section() {
  local section=${release_notes#*"### $2"$'\n'}
  section=${section%%$'\n'"#"*}
  if [[ ${release_notes} == *"### $2"$'\n'* && ${section} == *"$3"* ]]; then
    pass "$1"
  else
    failed "$1" "'$3' is not under '### $2'" "${release_notes}"
  fi
}
# before <name> <first> <second>: <first> appears before <second>
before() {
  local head=${release_notes%%"$3"*}
  if [[ ${release_notes} == *"$2"* && ${release_notes} == *"$3"* && ${head} == *"$2"* ]]; then
    pass "$1"
  else
    failed "$1" "'$2' does not come before '$3'" "${release_notes}"
  fi
}

if [[ ${notes_status} -eq 0 ]]; then pass "notes from a tag exit 0"; else failed "notes from a tag exit 0" "exit ${notes_status}"; fi
has    "a feature is listed with its scope" "- **hub:** add the release workflow"
in_section "a feature is under Features" "Features" "- **hub:** add the release workflow"
in_section "a fix is under Fixes" "Fixes" "- **node:** stop dropping frames"
in_section "a bang commit is breaking" "Breaking changes" "- **api:** rename the ingest route"
in_section "a BREAKING CHANGE footer is breaking" "Breaking changes" "- **server:** change the ingest store"
in_section "a perf commit is under Performance" "Performance" "- **web:** cache the sites list"
lacks  "a breaking commit is not repeated under Refactoring" "### Refactoring"
has    "a scopeless commit is listed without a scope" "- explain the release"
in_section "an unknown type has its own section" "wip" "- something unusual"
in_section "a non-conventional subject is under Other" "Other" "- Update the README"
lacks  "a merge commit is left out" "Merge pull request"
in_section "a commit merged from a branch is kept" "Fixes" "- **web:** a fix on a branch"
lacks  "a commit before the previous tag is left out" "first feature before the tag"
before "Breaking changes come first" "### Breaking changes" "### Features"
before "Features come before Fixes" "### Features" "### Fixes"
before "Fixes come before Performance" "### Fixes" "### Performance"
before "Performance comes before the other types" "### Performance" "### Documentation"
before "the other types come before unknown ones" "### Documentation" "### wip"
before "Other comes last" "### wip" "### Other"
has    "the server image is named" "ghcr.io/escendit/coldframe/server:0.2.0"
has    "the web image is named" "ghcr.io/escendit/coldframe/web:0.2.0"
has    "the migrations image is named" "ghcr.io/escendit/coldframe/migrations:0.2.0"
has    "the keycloak image is named" "ghcr.io/escendit/coldframe/keycloak:0.2.0"
has    "the Hub flash image is named" "coldframe-hub-0.2.0.bin"
has    "the Node ELF is named" "coldframe-node-0.2.0.elf"
has    "flashing at offset 0 is explained" "espflash write-bin 0x0 coldframe-node-0.2.0.bin"
has    "the Hub partition table is linked at the tag" "https://github.com/escendit/coldframe/blob/v0.2.0/apps/rs/hub/partitions.csv"
has    "the Node partition table is linked at the tag" "https://github.com/escendit/coldframe/blob/v0.2.0/apps/rs/node/partitions.csv"
has    "the checksums are explained" "sha256sum --check --strict SHA256SUMS"
has    "no mobile store binaries are published" "No mobile store binaries are published"
has    "the apps guide is linked at the tag" "https://github.com/escendit/coldframe/blob/v0.2.0/docs/build/apps.md"

release_notes=$(in_history "${notes}" 0.1.0 - v0.1.0)
has    "a first release covers the root commit" "- **repo:** initial commit"
has    "a first release covers the whole history" "- **server:** first feature before the tag"
lacks  "a first release stops at its commit" "add the release workflow"

release_notes=$(cd "${history}" && RELEASE_REPOSITORY=Example/Fork "${notes}" 0.2.0 v0.1.0 HEAD)
has    "RELEASE_REPOSITORY names the images in lower case" "ghcr.io/example/fork/server:0.2.0"

git -C "${history}" tag v0.2.0
release_notes=$(in_history "${notes}" 0.2.1 v0.2.0 HEAD)
has    "a release without commits says so" "No commits since the previous release."

expect "bad usage" 2 "" "usage:" -- "${notes}" 0.2.0 v0.1.0
expect "an unknown ref fails" 128 "" - -- in_history "${notes}" 0.2.0 v9.9.9 HEAD

echo "# checksums.sh"
assets=${work}/assets
mkdir -p "${assets}"
printf 'hub firmware\n' > "${assets}/coldframe-hub-0.2.0.bin"
printf 'node firmware\n' > "${assets}/coldframe-node-0.2.0.bin"
printf 'chart\n' > "${assets}/server-0.2.0.tgz"
expect "SHA256SUMS is written and verified" 0 "coldframe-hub-0.2.0.bin: OK" "" -- "${checksums}" "${assets}"
listed=$(awk '{print $2}' "${assets}/SHA256SUMS" | paste -sd, -)
if [[ ${listed} == "coldframe-hub-0.2.0.bin,coldframe-node-0.2.0.bin,server-0.2.0.tgz" ]]; then
  pass "SHA256SUMS lists every asset but itself, sorted"
else
  failed "SHA256SUMS lists every asset but itself, sorted" "listed: ${listed}"
fi
if (cd "${assets}" && sha256sum --check --strict --quiet SHA256SUMS); then
  pass "SHA256SUMS is in sha256sum format"
else
  failed "SHA256SUMS is in sha256sum format" "$(cat "${assets}/SHA256SUMS")"
fi
expect "an untouched directory verifies" 0 "server-0.2.0.tgz: OK" "" -- "${checksums}" --check "${assets}"
printf 'tampered\n' >> "${assets}/coldframe-node-0.2.0.bin"
expect "a tampered file is detected" 1 "coldframe-node-0.2.0.bin: FAILED" - -- "${checksums}" --check "${assets}"
printf 'node firmware\n' > "${assets}/coldframe-node-0.2.0.bin"
printf 'unlisted\n' > "${assets}/extra.tgz"
expect "an unlisted file is detected" 1 "" "does not list exactly the assets" -- "${checksums}" --check "${assets}"
rm -f "${assets}/extra.tgz" "${assets}/server-0.2.0.tgz"
expect "a missing file is detected" 1 "" "does not list exactly the assets" -- "${checksums}" --check "${assets}"
mkdir -p "${work}/empty"
expect "an empty directory fails" 1 "" "no assets" -- "${checksums}" "${work}/empty"
mkdir -p "${assets}/nested"
expect "a subdirectory fails" 1 "" "not a regular file" -- "${checksums}" "${assets}"
expect "bad usage" 2 "" "usage:" -- "${checksums}"

echo "# ${passes} passed, ${failures} failed"
[[ ${failures} -eq 0 ]]
