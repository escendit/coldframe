#!/usr/bin/env bash
# Checks the Markdown docs (Story 10.2): every relative link and every referenced repository path
# must exist. External links (http, https, mailto, any scheme) are never fetched.
#
#   docs/check-links.sh [root...]
#
# Each root is a directory (every *.md below it) or a Markdown file, relative to the repository
# root unless absolute. The default is `docs hardware README.md apps/rs/hub/README.md
# apps/rs/node/README.md deploy/compose/README.md` (the build guide sends readers to the firmware
# READMEs, the deploy guide to the Compose example). Outside fenced code blocks, two things are
# checked:
#
# - inline links `[text](target)`: the file part of the target (no #anchor, no ?query), relative
#   to the Markdown file, or to the repository root when it starts with `/`;
# - code spans that start with apps/ packages/ deploy/ docs/ hardware/ tests/ .github/, from the
#   repository root. A span holding `<`, `*`, `{`, `X.Y.Z`, `...` or a space is a placeholder and
#   is skipped; a trailing `:<line>` or `:<line>-<line>` is dropped. A missing path that git
#   ignores is a local file the reader creates (an `.env`, a `*.local.xcconfig`) and passes.
#
# Exit 0 when everything resolves, 1 listing every failure as file:line, 2 on bad usage.
# DOCS_REPO_ROOT overrides the repository root (the tests use it).
set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo=${DOCS_REPO_ROOT:-$(cd "${here}/.." && pwd)}

if [[ ${1:-} == -h || ${1:-} == --help ]]; then
  sed -n '2,21p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
  exit 0
fi

roots=("$@")
if [[ ${#roots[@]} -eq 0 ]]; then
  roots=(docs hardware README.md apps/rs/hub/README.md apps/rs/node/README.md deploy/compose/README.md)
fi

files=()
for root in "${roots[@]}"; do
  if [[ ${root} == /* ]]; then path=${root}; else path=${repo}/${root}; fi
  if [[ -d ${path} ]]; then
    while IFS= read -r -d '' file; do
      files+=("${file}")
    done < <(find "${path}" -type f -name '*.md' -print0 | sort -z)
  elif [[ -f ${path} ]]; then
    files+=("${path}")
  else
    echo "usage: check-links.sh [root...]: '${root}' is no file or directory under ${repo}" >&2
    exit 2
  fi
done

# Prints "<line>\t<link|code>\t<target>" for every inline link and code span outside fences.
extract() {
  awk '
    /^[[:space:]]*(```|~~~)/ { fenced = !fenced; next }
    fenced { next }
    {
      line = $0
      rest = line
      while (match(rest, /`[^`]+`/)) {
        print FNR "\tcode\t" substr(rest, RSTART + 1, RLENGTH - 2)
        rest = substr(rest, RSTART + RLENGTH)
      }
      gsub(/`[^`]+`/, "", line)
      rest = line
      while (match(rest, /\]\([^)]*\)/)) {
        print FNR "\tlink\t" substr(rest, RSTART + 2, RLENGTH - 3)
        rest = substr(rest, RSTART + RLENGTH)
      }
    }
  ' "$1"
}

failures=0
checked=0
fail() {
  failures=$((failures + 1))
  echo "$1" >&2
}

for file in "${files[@]}"; do
  rel=${file#"${repo}"/}
  dir=$(dirname "${file}")
  while IFS=$'\t' read -r line kind target; do
    case ${kind} in
      link)
        # `<target>` keeps its spaces; otherwise drop an optional "title".
        if [[ ${target} == '<'* ]]; then
          target=${target#<}
          target=${target%%>*}
        else
          target=${target%% *}
        fi
        # External: any URI scheme (https:, mailto:, ...).
        if [[ ${target} =~ ^[A-Za-z][A-Za-z0-9+.-]*: ]]; then
          continue
        fi
        target=${target%%#*}
        target=${target%%\?*}
        [[ -z ${target} ]] && continue
        if [[ ${target} == /* ]]; then
          resolved=${repo}${target}
        else
          resolved=${dir}/${target}
        fi
        checked=$((checked + 1))
        if [[ ! -e ${resolved} ]]; then
          fail "${rel}:${line}: broken link '${target}'"
        fi
        ;;
      code)
        case ${target} in
          apps/* | packages/* | deploy/* | docs/* | hardware/* | tests/* | .github/*) ;;
          *) continue ;;
        esac
        case ${target} in
          *'<'* | *'*'* | *'{'* | *X.Y.Z* | *...* | *' '*) continue ;;
        esac
        target=${target%#*}
        if [[ ${target} =~ ^(.*):[0-9]+(-[0-9]+)?(,[0-9]+(-[0-9]+)?)*$ ]]; then
          target=${BASH_REMATCH[1]}
        fi
        checked=$((checked + 1))
        if [[ ! -e ${repo}/${target} ]] && ! git -C "${repo}" check-ignore --quiet --no-index -- "${target}" 2>/dev/null; then
          fail "${rel}:${line}: missing path '${target}'"
        fi
        ;;
    esac
  done < <(extract "${file}")
done

if [[ ${failures} -gt 0 ]]; then
  echo "check-links: ${failures} broken of ${checked} in ${#files[@]} files" >&2
  exit 1
fi
echo "check-links: ${checked} links and paths in ${#files[@]} files resolve"
