#!/usr/bin/env bash
# Runs the commands the build guide (Story 10.2) and the firmware reference (Story 10.4) document.
# Firmware builds only, never flashing.
#
#   docs/check-commands.sh [--list] [file...]
#
# The default files are docs/build/*.md and docs/firmware/*.md (Story 10.4), relative to the
# repository root unless absolute. Every fenced bash/sh block must be preceded by a marker (blank
# lines between them are allowed):
#
#   <!-- ci:run <repo-relative dir> -->  the block runs in that directory, after
#                                        `source ~/export-esp.sh` when it exists (the guide's
#                                        install block creates it), with errexit and pipefail;
#   <!-- ci:hardware -->                 the block needs a board: only `espflash <subcommand> --help`
#                                        runs, for every espflash subcommand in it;
#   <!-- ci:manual -->                   the block needs a phone, Xcode, the Android SDK, a cluster
#                                        (Story 10.3) or a browser (Story 10.4): nothing runs;
#                                        --list shows it as manual.
#
# An unmarked block, a marker that no bash/sh block follows, an unknown ci: marker or a ci:run
# directory that does not exist fails. --list prints every block with its marker and runs nothing.
#
# Exit 0 when every block passes, 1 naming each failing block as file:line, 2 on bad usage.
# DOCS_REPO_ROOT overrides the repository root and ESP_EXPORT the export file (the tests use both).
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo=${DOCS_REPO_ROOT:-$(cd "${here}/.." && pwd)}
esp_export=${ESP_EXPORT:-${HOME}/export-esp.sh}

list=false
files=()
for arg in "$@"; do
  case ${arg} in
    --list) list=true ;;
    -h | --help)
      sed -n '2,24p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    -*)
      echo "usage: check-commands.sh [--list] [file...]: unknown option '${arg}'" >&2
      exit 2
      ;;
    /*) files+=("${arg}") ;;
    *) files+=("${repo}/${arg}") ;;
  esac
done
if [[ ${#files[@]} -eq 0 ]]; then
  for file in "${repo}"/docs/build/*.md "${repo}"/docs/firmware/*.md; do
    [[ -f ${file} ]] && files+=("${file}")
  done
fi
for file in "${files[@]}"; do
  if [[ ! -f ${file} ]]; then
    echo "usage: check-commands.sh [--list] [file...]: '${file}' is no file" >&2
    exit 2
  fi
done

work=$(mktemp -d)
trap 'rm -rf "${work}"' EXIT

# Splits a Markdown file into ${work}/<n>.sh blocks and prints one manifest line per block or
# problem: "<line> <run|hardware|manual|unmarked|orphan|unknown> <argument> <block file>", separated by
# the unit separator 0x1F (a tab would collapse an empty argument).
# Plain POSIX awk: the CI runner's awk is mawk.
extract() {
  awk -v work="${work}" -v prefix="$2" '
    function emit(kind, arg, path) { print FNR "\037" kind "\037" arg "\037" path }
    function trim(s) { sub(/^[ \t]+/, "", s); sub(/[ \t]+$/, "", s); return s }
    infence {
      if ($0 ~ /^[ \t]*(```|~~~)[ \t]*$/) { infence = 0; if (out != "") close(out); out = ""; next }
      if (out != "") print > out
      next
    }
    /^[ \t]*<!--[ \t]*ci:/ {
      if (pending != "") emit("orphan", pending, "")
      m = $0
      sub(/^[ \t]*<!--[ \t]*/, "", m)
      sub(/[ \t]*-->[ \t]*$/, "", m)
      if (m == "ci:hardware") { pending = "hardware"; arg = "" }
      else if (m == "ci:manual") { pending = "manual"; arg = "" }
      else if (m ~ /^ci:run[ \t]+[^ \t]+$/) { pending = "run"; arg = trim(substr(m, 7)) }
      else { emit("unknown", m, ""); pending = "" }
      next
    }
    /^[ \t]*(```|~~~)/ {
      info = $0
      sub(/^[ \t]*(```|~~~)/, "", info)
      info = trim(info)
      lang = info
      sub(/[ \t{].*$/, "", lang)
      infence = 1
      out = ""
      if (lang == "bash" || lang == "sh" || lang == "shell") {
        blocks++
        out = work "/" prefix "-" blocks ".sh"
        printf "" > out
        if (pending == "") emit("unmarked", "", out)
        else emit(pending, arg, out)
      } else if (pending != "") {
        emit("orphan", pending, "")
      }
      pending = ""
      next
    }
    /^[ \t]*$/ { next }
    {
      if (pending != "") emit("orphan", pending, "")
      pending = ""
    }
    END { if (pending != "") emit("orphan", pending, "") }
  ' "$1"
}

failures=0
blocks=0
fail() {
  failures=$((failures + 1))
  echo "FAIL $1" >&2
}

indent() { sed 's/^/    /' "$1"; }

# The espflash subcommands a block calls, one per line, comment lines left out.
espflash_subcommands() {
  grep -v '^[[:space:]]*#' "$1" |
    grep -oE '(^|[^[:alnum:]_./-])espflash[[:space:]]+[a-z][a-z0-9-]*' |
    awk '{ print $NF }' | sort -u
}

run_block() { # <where> <dir> <block>
  local where=$1 dir=$2 block=$3
  echo "==> ${where}: ci:run ${dir}"
  indent "${block}"
  {
    if [[ -f ${esp_export} ]]; then
      printf 'source %q\n' "${esp_export}"
    fi
    printf 'set -euo pipefail\n'
    cat "${block}"
  } > "${block}.run"
  if ! (cd "${repo}/${dir}" && bash "${block}.run" < /dev/null); then
    fail "${where}: the ci:run ${dir} block failed"
  fi
}

check_hardware() { # <where> <block>
  local where=$1 block=$2 sub any=false
  echo "==> ${where}: ci:hardware"
  indent "${block}"
  while IFS= read -r sub; do
    any=true
    if espflash "${sub}" --help > /dev/null 2>&1; then
      echo "    ok: espflash ${sub} is a subcommand"
    else
      fail "${where}: 'espflash ${sub}' is not an espflash subcommand"
    fi
  done < <(espflash_subcommands "${block}")
  if [[ ${any} == false ]]; then
    echo "    (no espflash command to check)"
  fi
}

index=0
for file in "${files[@]}"; do
  index=$((index + 1))
  rel=${file#"${repo}"/}
  while IFS=$'\x1f' read -r line kind arg block; do
    where="${rel}:${line}"
    case ${kind} in
      unmarked)
        blocks=$((blocks + 1))
        fail "${where}: unmarked bash/sh block; precede it with <!-- ci:run <dir> -->, <!-- ci:hardware --> or <!-- ci:manual -->"
        ${list} && indent "${block}"
        ;;
      orphan) fail "${where}: a ci:${arg} marker is not followed by a bash/sh block" ;;
      unknown) fail "${where}: unknown marker '${arg}'" ;;
      run)
        blocks=$((blocks + 1))
        if [[ ! -d ${repo}/${arg} ]]; then
          fail "${where}: ci:run directory '${arg}' does not exist"
        elif ${list}; then
          echo "${where}: ci:run ${arg}"
          indent "${block}"
        else
          run_block "${where}" "${arg}" "${block}"
        fi
        ;;
      hardware)
        blocks=$((blocks + 1))
        if ${list}; then
          echo "${where}: ci:hardware"
          indent "${block}"
        else
          check_hardware "${where}" "${block}"
        fi
        ;;
      manual)
        blocks=$((blocks + 1))
        if ${list}; then
          echo "${where}: ci:manual"
          indent "${block}"
        else
          echo "==> ${where}: ci:manual (not run: needs a phone, Xcode, the Android SDK, a cluster or a browser)"
        fi
        ;;
    esac
  done < <(extract "${file}" "${index}")
done

if [[ ${failures} -gt 0 ]]; then
  echo "check-commands: ${failures} failed (${blocks} blocks in ${#files[@]} files)" >&2
  exit 1
fi
echo "check-commands: ${blocks} blocks in ${#files[@]} files pass"
