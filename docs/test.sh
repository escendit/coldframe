#!/usr/bin/env bash
# Plain-bash tests for the doc checks (Story 10.2): check-links.sh and check-commands.sh against
# throwaway fixtures, with a stub espflash. Needs only bash, awk and coreutils. Run from anywhere:
#
#   docs/test.sh
# The fixtures are Markdown in single quotes, backticks and all.
# shellcheck disable=SC2016
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
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

links=${here}/check-links.sh
commands=${here}/check-commands.sh

# A fresh fixture repository: <name> -> its path. It holds docs/build/{flash,power}.md stubs and
# apps/rs/node/src/board/pins.rs, so the good links have something to resolve to.
fixture() {
  local root=${work}/$1
  mkdir -p "${root}/docs/build" "${root}/apps/rs/node/src/board"
  : > "${root}/apps/rs/node/src/board/pins.rs"
  : > "${root}/apps/rs/node/Cargo.toml"
  printf '# Power\n' > "${root}/docs/build/power.md"
  printf '# Flash\n' > "${root}/docs/build/flash.md"
  echo "${root}"
}

# page <root> <relative path> <content>: writes a Markdown page.
page() {
  mkdir -p "$(dirname "$1/$2")"
  printf '%s\n' "$3" > "$1/$2"
}

echo "# check-links.sh"
good=$(fixture good)
page "${good}" docs/build/flash.md '# Flash

See [power](power.md) and [the pins](../../apps/rs/node/src/board/pins.rs).'
expect "good docs pass" 0 "resolve" "" -- env DOCS_REPO_ROOT="${good}" "${links}" docs

broken=$(fixture broken)
page "${broken}" docs/build/flash.md '# Flash

A [broken link](../nope.md).'
expect "a broken link fails naming file, line and target" 1 "" "docs/build/flash.md:3: broken link '../nope.md'" \
  -- env DOCS_REPO_ROOT="${broken}" "${links}" docs

anchors=$(fixture anchors)
page "${anchors}" docs/build/flash.md '# Flash

[budget](power.md#budget), [query](power.md?plain=1), [here](#flash), <https://example.invalid/x>,
[external](https://example.invalid/nope.md), [mail](mailto:someone@example.invalid),
[titled](power.md "Power"), [angled](<power.md>).'
expect "anchors and queries are dropped, external links skipped" 0 "resolve" "" \
  -- env DOCS_REPO_ROOT="${anchors}" "${links}" docs

spaced=$(fixture spaced)
page "${spaced}" docs/build/my\ file.md '# Mine'
page "${spaced}" docs/build/flash.md '[mine](<my file.md>) [titled](<my file.md> "Mine") [gone](<no file.md>)'
expect "an angle-bracket target keeps its spaces" 1 "" "docs/build/flash.md:1: broken link 'no file.md'" \
  -- env DOCS_REPO_ROOT="${spaced}" "${links}" docs
expect "only the missing angle-bracket target fails" 1 "" "check-links: 1 broken" \
  -- env DOCS_REPO_ROOT="${spaced}" "${links}" docs

anchor_missing=$(fixture anchor-missing)
page "${anchor_missing}" docs/build/flash.md '[gone](gone.md#budget)'
expect "the file part of an anchored link is checked" 1 "" "docs/build/flash.md:1: broken link 'gone.md'" \
  -- env DOCS_REPO_ROOT="${anchor_missing}" "${links}" docs

backtick=$(fixture backtick)
page "${backtick}" docs/build/flash.md '# Flash

The pins are in `apps/rs/node/src/board/pins.rs` and `apps/rs/node/src/board/pins.rs:7-17`.'
expect "an existing backtick path passes, with a line suffix" 0 "resolve" "" \
  -- env DOCS_REPO_ROOT="${backtick}" "${links}" docs

backtick_missing=$(fixture backtick-missing)
page "${backtick_missing}" docs/build/flash.md '# Flash

The pins are in `apps/rs/node/src/board/gone.rs`.'
expect "a missing backtick path fails naming file and line" 1 "" \
  "docs/build/flash.md:3: missing path 'apps/rs/node/src/board/gone.rs'" \
  -- env DOCS_REPO_ROOT="${backtick_missing}" "${links}" docs

placeholders=$(fixture placeholders)
page "${placeholders}" docs/build/flash.md '# Flash

`docs/coldframe-node-X.Y.Z.bin`, `apps/<port>`, `apps/rs/*/`, `apps/rs/{hub,node}/`,
`apps/rs/node/src/.../x`, `apps/rs/node and more`, `cargo build`, `src/main.rs`.'
expect "placeholder and non-repository spans are skipped" 0 "resolve" "" \
  -- env DOCS_REPO_ROOT="${placeholders}" "${links}" docs

fenced=$(fixture fenced)
page "${fenced}" docs/build/flash.md '# Flash

```text
[not a link](nope.md) `apps/nope`
```'
expect "fenced blocks are not checked" 0 "resolve" "" -- env DOCS_REPO_ROOT="${fenced}" "${links}" docs

rooted=$(fixture rooted)
page "${rooted}" README.md '[pins](/apps/rs/node/src/board/pins.rs) [gone](/apps/gone.md)'
expect "a root-relative link resolves from the repository root" 1 "" "README.md:1: broken link '/apps/gone.md'" \
  -- env DOCS_REPO_ROOT="${rooted}" "${links}" README.md

several=$(fixture several)
page "${several}" docs/build/flash.md '[a](a.md)
[b](b.md) `docs/c.md`'
expect "every failure is listed" 1 "" "check-links: 3 broken" -- env DOCS_REPO_ROOT="${several}" "${links}" docs

expect "a missing root is a usage error" 2 "" "usage:" -- env DOCS_REPO_ROOT="${good}" "${links}" nope
expect "the repository docs pass" 0 "resolve" "" -- "${links}"

echo "# check-commands.sh"
# A stub espflash: logs its arguments, knows the real subcommand names, fails on any other.
stubs=${work}/bin
mkdir -p "${stubs}"
calls=${work}/espflash-calls
cat > "${stubs}/espflash" <<EOF
#!/usr/bin/env bash
echo "\$*" >> "${calls}"
case \$1 in
  board-info|checksum-md5|completions|erase-flash|erase-parts|erase-region|flash|hold-in-reset|\\
  list-ports|monitor|partition-table|read-flash|reset|save-image|write-bin|help) exit 0 ;;
  *) echo "error: unrecognized subcommand '\$1'" >&2; exit 2 ;;
esac
EOF
chmod +x "${stubs}/espflash"
esp_export=${work}/export-esp.sh
echo 'export STUB_ESP=sourced' > "${esp_export}"

check() { # <fixture> [args...]
  local root=$1
  shift
  env PATH="${stubs}:${PATH}" DOCS_REPO_ROOT="${root}" ESP_EXPORT="${esp_export}" "${commands}" "$@"
}

run_ok=$(fixture run-ok)
page "${run_ok}" docs/build/flash.md '# Flash

<!-- ci:run apps/rs/node -->

```bash
test -f Cargo.toml
test "${STUB_ESP}" = sourced
true
```'
expect "a ci:run block runs in its directory after the export file" 0 "1 blocks in 2 files pass" - \
  -- check "${run_ok}"

run_fail=$(fixture run-fail)
page "${run_fail}" docs/build/flash.md '# Flash

<!-- ci:run apps/rs/node -->
```sh
false
true
```'
expect "a failing ci:run block names its file and line" 1 - "docs/build/flash.md:4: the ci:run apps/rs/node block failed" \
  -- check "${run_fail}"

: > "${calls}"
hardware=$(fixture hardware)
page "${hardware}" docs/build/flash.md '# Flash

<!-- ci:hardware -->
```bash
# espflash bogus in a comment is not checked
espflash write-bin 0x0 coldframe-node-X.Y.Z.bin
cargo run --release
```'
expect "a ci:hardware block only checks its espflash subcommands" 0 "espflash write-bin is a subcommand" "" \
  -- check "${hardware}"
if [[ $(cat "${calls}") == "write-bin --help" ]]; then
  pass "a ci:hardware block runs only espflash <subcommand> --help"
else
  failed "a ci:hardware block runs only espflash <subcommand> --help" "calls: $(cat "${calls}")"
fi

hardware_bad=$(fixture hardware-bad)
page "${hardware_bad}" docs/build/flash.md '<!-- ci:hardware -->
```bash
espflash write-binary 0x0 x.bin
```'
expect "an unknown espflash subcommand fails" 1 - "docs/build/flash.md:2: 'espflash write-binary' is not an espflash subcommand" \
  -- check "${hardware_bad}"

unmarked=$(fixture unmarked)
page "${unmarked}" docs/build/flash.md '# Flash

```bash
cargo build --release --locked
```'
expect "an unmarked block fails naming its file and line" 1 - "docs/build/flash.md:3: unmarked bash/sh block" \
  -- check "${unmarked}"
expect "--list reports an unmarked block too" 1 - "docs/build/flash.md:3: unmarked bash/sh block" \
  -- check "${unmarked}" --list

orphan=$(fixture orphan)
page "${orphan}" docs/build/flash.md '<!-- ci:run apps/rs/node -->
Some prose between the marker and the block.

```bash
true
```'
expect "a marker followed by prose is an orphan" 1 - "docs/build/flash.md:2: a ci:run marker is not followed" \
  -- check "${orphan}"

unknown=$(fixture unknown)
page "${unknown}" docs/build/flash.md '<!-- ci:skip -->
```bash
true
```'
expect "an unknown marker fails" 1 - "docs/build/flash.md:1: unknown marker 'ci:skip'" -- check "${unknown}"

nodir=$(fixture nodir)
page "${nodir}" docs/build/flash.md '<!-- ci:run apps/rs/gone -->
```bash
true
```'
expect "a ci:run directory that does not exist fails" 1 - "ci:run directory 'apps/rs/gone' does not exist" \
  -- check "${nodir}"

listed=$(fixture listed)
page "${listed}" docs/build/flash.md '<!-- ci:run apps/rs/node -->
```bash
touch ran
```

```text
an unmarked text block is not a command block
```

<!-- ci:hardware -->
```bash
espflash monitor
```'
: > "${calls}"
expect "--list prints every block with its marker" 0 "docs/build/flash.md:2: ci:run apps/rs/node" "" \
  -- check "${listed}" --list
expect "--list names the hardware block" 0 "docs/build/flash.md:11: ci:hardware" "" -- check "${listed}" --list
if [[ ! -e ${listed}/apps/rs/node/ran && ! -s ${calls} ]]; then
  pass "--list runs nothing"
else
  failed "--list runs nothing" "ran: $(ls "${listed}/apps/rs/node")" "calls: $(cat "${calls}")"
fi

no_export=$(fixture no-export)
page "${no_export}" docs/build/flash.md '<!-- ci:run apps/rs/node -->
```bash
test -z "${STUB_ESP:-}"
```'
expect "without the export file a ci:run block still runs, unsourced" 0 "1 blocks in 2 files pass" - \
  -- env -u STUB_ESP PATH="${stubs}:${PATH}" DOCS_REPO_ROOT="${no_export}" ESP_EXPORT="${work}/none.sh" "${commands}"

expect "a file argument that is no file is a usage error" 2 "" "usage:" -- check "${good}" docs/build/nope.md
expect "the repository guide marks every block" 0 "pass" "" -- "${commands}" --list

echo "# toolchain pins"
# The pins of ci.yml's firmware job are the reference; release.yml, docs.yml and the guide follow.
pin() { # <file> <variable>: the quoted value of `<variable>: "<value>"`
  sed -n "s/^[[:space:]]*$2:[[:space:]]*\"\([^\"]*\)\".*/\1/p" "$1" | head -n 1
}
workflows=${here}/../.github/workflows
flash=${here}/build/flash.md
espup_pin=$(pin "${workflows}/ci.yml" ESPUP_VERSION)
toolchain_pin=$(pin "${workflows}/ci.yml" ESP_TOOLCHAIN_VERSION)
espflash_pin=$(pin "${workflows}/ci.yml" ESPFLASH_VERSION)
if [[ -n ${espup_pin} && -n ${toolchain_pin} && -n ${espflash_pin} ]]; then
  pass "ci.yml names the three pins"
else
  failed "ci.yml names the three pins" "espup '${espup_pin}' toolchain '${toolchain_pin}' espflash '${espflash_pin}'"
fi
for workflow in release.yml docs.yml; do
  for variable in ESPUP_VERSION ESP_TOOLCHAIN_VERSION ESPFLASH_VERSION; do
    want=$(pin "${workflows}/ci.yml" "${variable}")
    got=$(pin "${workflows}/${workflow}" "${variable}")
    if [[ ${got} == "${want}" ]]; then
      pass "${workflow} pins ${variable} as ci.yml does"
    else
      failed "${workflow} pins ${variable} as ci.yml does" "${workflow}: '${got}', ci.yml: '${want}'"
    fi
  done
done
# guide <name> <substring>: flash.md contains it.
guide() {
  if grep -qF -- "$2" "${flash}"; then pass "$1"; else failed "$1" "flash.md does not contain '$2'"; fi
}
guide "the guide's prose names the espup pin" "espup ${espup_pin}"
guide "the guide's prose names the toolchain pin" "esp toolchain ${toolchain_pin}"
guide "the guide's prose names the espflash pin" "espflash ${espflash_pin}"
guide "the install block installs the espup pin" "cargo install espup --version ${espup_pin} --locked"
guide "the install block installs the toolchain pin" "espup install --toolchain-version ${toolchain_pin} "
guide "the install block installs the espflash pin" "cargo install espflash --version ${espflash_pin} --locked"
# No other version of the three tools appears in the guide.
stray=$(grep -noE "(espup|espflash) [0-9]+\.[0-9]+\.[0-9]+|--version [0-9.]+|--toolchain-version [0-9.]+|toolchain [0-9]+\.[0-9]+\.[0-9]+\.[0-9]+" "${flash}" |
  grep -vE "(espup|--version) ${espup_pin//./\\.}\$|(espflash|--version) ${espflash_pin//./\\.}\$|toolchain(-version)? ${toolchain_pin//./\\.}\$")
if [[ -z ${stray} ]]; then
  pass "the guide names no other tool version"
else
  failed "the guide names no other tool version" "${stray}"
fi

echo
echo "${passes} passed, ${failures} failed"
[[ ${failures} -eq 0 ]]
