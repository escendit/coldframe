---
title: 'Firmware learning reference'
type: 'feature'
created: '2026-10-20'
status: 'done'
baseline_revision: 'a5bae8573c50692249adf99c9ce494e96ff36dd0'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-10-context.md'
warnings: [oversized]
deferred:
  - summary: >-
      Hub comments still claim the eFuse adapter holds the Hub's only unsafe code, but board/rtc.rs has an unsafe block for the mbedTLS clock hooks.
    evidence: |-
      apps/rs/hub/src/board/efuse.rs module doc line 7 and apps/rs/hub/Cargo.toml:60 say only efuse.rs may use unsafe; apps/rs/hub/src/board/rtc.rs:40-41 has #[allow(unsafe_code)] unsafe { ... }. Pre-existing, not caused by story 10-4; docs/firmware/identity.md already states the code's truth.
    location: >-
      apps/rs/hub/src/board/efuse.rs:7
    severity: low
---

<intent-contract>

## Intent

**Problem:** A maker who wants to learn ESP-NOW, BLE provisioning and deep sleep from the Coldframe firmware has no entry point. The knowledge is spread over crate docs, `apps/rs/*/README.md`, ARCHITECTURE-SPINE AD texts and the spike report, and `docs/firmware/` does not exist. `cargo doc` over `packages/rs/*` is not run in CI. Today it emits 2 rustdoc warnings (a bare URL in the generated `packages/rs/crypto/src/spec.rs`, an ambiguous `[`env`]` link in `packages/rs/sensing/src/lib.rs:10`). The re-exported micropb wire types in `coldframe-protocol` have no docs at all, because `missing_docs` is allowed on the generated module.

**Approach:** Add `docs/firmware/`: an index page plus one page per AC topic. Each page explains the mechanism in prose and points to the code by repo path and symbol. Make every public item of `packages/rs/*` documented, including the generated wire types, by carrying the proto comments into the generated code. Fix the 2 rustdoc warnings at their source. Add a `cargo doc` job with `RUSTDOCFLAGS=-D warnings` to the Docs workflow.

## Boundaries & Constraints

**Always:**
- Take facts from the code, not from the planning docs. Where the two disagree, document the code. Examples: the Hub only rescans after the link drops, and there is no RSSI-degrade re-evaluation; the burned fuse purpose is `HMAC_UP` and the peripheral mode is `ToUser`.
- Point to code as backticked repo-root paths (`check-links.sh` verifies they exist) plus symbol names. Link to READMEs, AD texts and checklists instead of copying their tables.
- State these facts: eFuse burns are irreversible, and any non-`dev-mode` first boot burns, with a `> [!WARNING]`. The ~100 µA average sleep-current budget is not yet measured. WPA3-only Wi-Fi is unsupported. A short press means report now, a long press (≥3 s) means BLE setup. Never erase `cf_frame` on a Node that has sent frames.
- Match the existing page style: one `#` title, an intro that links neighbouring pages, `##`/`###` sections, GitHub alerts, no front matter.
- Every bash/sh block in `docs/firmware/` carries a `ci:` marker.
- New CI actions are pinned by SHA with a release comment, copied from `ci.yml`.

**Never:**
- Edit `.github/workflows/ci.yml`. That forces run-all, including the slow Images job (precedent: specs 10-2 and 10-3).
- Make the Docs workflow a required check, or fetch external links.
- Edit `sprint-status.yaml`.
- Change firmware behaviour, wire bytes, crypto-spec values or proto field numbers. Comment-only proto changes are fine.
- Hand-edit the generated `packages/rs/crypto/src/spec.rs`. Change the generator input and regenerate.
- Add a docs site generator.
- Add `#[allow(missing_docs)]` or `#[allow(rustdoc::…)]` to silence a gap.

</intent-contract>

## Code Map

- `_bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md:175-390` -- AD-9/10/11/12/17/24/25 texts; Deferred section at line 577. Link target; read-only.
- `_bmad-output/specs/spec-coldframe/device-hardware.md:12-21,57-64` -- H-1..3, N-1..3, power budget. Read-only.
- **ESP-NOW and channel following:**
  - `packages/rs/transport/src/transport.rs`: module doc 1-42; constants `ACK_WINDOW_MS`/`PROBE_WAIT_MS`/`RESCAN_AFTER_MISSES`/`SCAN_BACKOFF_WAKES`; `probe`, `find_hub`, `listen`, `run`
  - `packages/rs/transport/src/link.rs` (`LinkState`, `CFLK`)
  - `packages/rs/protocol/src/radio.rs`
  - `packages/rs/hal/src/datagram.rs`
  - `apps/rs/node/src/board/espnow.rs`; `apps/rs/hub/src/board/espnow.rs` (`channel: None`)
  - `packages/rs/uplink/src/relay.rs`
- **BLE setup and PoP:**
  - `packages/rs/crypto/src/setup.rs` (`derive_keys`, `SetupSession`)
  - `packages/rs/setup/src/{lib,code,framing,session,store,service,node,app}.rs`
  - `packages/rs/crypto/src/hpke.rs`
  - `apps/rs/node/src/main.rs::setup_mode`; `apps/rs/{hub,node}/src/board/ble.rs`
  - `packages/proto/coldframe/setup/v1/setup.proto`
- **Identity:**
  - `packages/rs/crypto/src/{keys,identity}.rs` (`provision_efuse`, `provision_dev`, `CFID`)
  - `packages/rs/hal/src/{efuse,hmac}.rs`
  - `apps/rs/{hub,node}/src/board/{efuse,hmac}.rs`, which are the only `unsafe` code
  - `compile_error!` dev-mode guard in `apps/rs/node/src/main.rs:62-65`
- **Sealing:**
  - `packages/rs/crypto/src/frame.rs` (`nonce`, `aad`, `seal`, `open_checked`, `ReplayWindow`)
  - `packages/rs/transport/src/frame.rs` (`seal_uplink`, `open_downlink`)
  - `packages/rs/sensing/src/counter.rs` (`ReservedCounter`)
  - `packages/rs/transport/src/buffer.rs`
  - `packages/proto/coldframe/device/v1/envelope.proto`
- **Deep sleep:**
  - `apps/rs/node/src/main.rs` (`WATCHDOG_MS`, `sleep`, `fail`, 80 MHz)
  - `apps/rs/node/src/board/{sleep,rtc,button}.rs`
  - `packages/rs/sensing/src/wake.rs` (`WAKE_PERIOD_MS`, `sleep_ms`, `run_wake`)
  - `docs/build/power.md` (link)
- **Buttons:** `packages/rs/sensing/src/button.rs` (`DEBOUNCE_MS` 50, `LONG_PRESS_MS` 3000, `classify_press`, `wake_plan`, `sleep_after`); `apps/rs/node/src/board/pins.rs` (GPIO7).
- **Coexistence:** `docs/spikes/hub-radio-coexistence.md`, with the verdict at about line 400 and findings F-3..F-7. Code: `packages/rs/uplink/src/bssid.rs::select_bssid`, `apps/rs/hub/src/board/wifi.rs::security`, `apps/rs/hub/src/main.rs` (heap, BLE before Wi-Fi). Read-only.
- `apps/rs/node/README.md`, `apps/rs/hub/README.md`, `apps/rs/README.md`, `packages/rs/README.md` -- existing crate and board docs; link to them rather than duplicating.
- **Rustdoc fixes:**
  - `packages/crypto-spec/scripts/lib/spec.ts:209` -- `sensorIdNamespace` doc string embeds the bare URL. Regenerate with `pnpm --filter @coldframe/crypto-spec run generate`, which rewrites the Rust, C# and Kotlin outputs. `run check` is the CI drift gate.
  - `packages/rs/sensing/src/lib.rs:10` -- the `[`env`]` link is ambiguous.
  - `packages/rs/protocol/build.rs` -- has `compiler.include_source_info(false)`. Setting it to `true` makes micropb-gen 0.6.0 emit proto comments as docs (verified).
  - `packages/rs/protocol/src/lib.rs:26-45` -- `missing_docs` is allowed on `mod generated`. `pub use …::v1_ as setup_v1`/`device_v1` makes the undocumented `v1_` package modules public.
  - With source info on and the allow removed, exactly 10 gaps remain: the 2 `v1_` modules, plus 8 enum values without a comment (`DEVICE_KIND_*`, `WIFI_SECURITY_*` in setup.proto:79-104, `WIFI_STATUS_UNSPECIFIED` 143, `SETUP_ERROR_CODE_UNSPECIFIED` 199).
- `.github/workflows/docs.yml` -- add the job here. The `Swatinem/rust-cache@6323deb102c322ba6fcbdcafc7e3dddab59af2b6 # v2.9.2` pin is at `ci.yml:183`. The host toolchain comes from `rustup toolchain install` reading `rust-toolchain.toml`.
- `docs/check-links.sh` -- already scans `docs/**`, so new pages are covered. `docs/check-commands.sh:45-49` -- default glob is `docs/build/*.md` only.
- `README.md:10-17` ("Start here") -- the docs index.

## Tasks & Acceptance

**Execution:**
- `packages/proto/coldframe/setup/v1/setup.proto` -- add a one-line comment to each undocumented enum value. Comment-only change; buf lint and breaking must stay green.
- `packages/rs/protocol/build.rs` -- `include_source_info(true)`, and update the module doc to say proto comments become rustdoc.
- `packages/rs/protocol/src/lib.rs` -- drop `missing_docs` from the generated module's allow list. Expose `setup_v1`/`device_v1` as documented modules that glob re-export the generated package modules (`pub mod setup_v1 { pub use super::generated::…::v1_::*; }`), keeping every existing path such as `setup_v1::SetupMessage_::Body` valid. Update the crate doc lines 6-10 to match.
- `packages/crypto-spec/scripts/lib/spec.ts` -- put the namespace name in a code span so no bare URL reaches the docs. Then regenerate the Rust, C# and Kotlin outputs.
- `packages/rs/sensing/src/lib.rs` -- link the module unambiguously (`mod@env`).
- `docs/firmware/README.md` -- the index. It gives:
  - a reading order
  - a crate map (the 7 `packages/rs` crates and the 2 firmware crates, one line each, linking the existing READMEs)
  - the two toolchains: host 1.97 versus esp, with `rust-version` 1.95
  - how to open the API docs (`cargo doc --no-deps -p coldframe-… --open`, in a `<!-- ci:manual -->` block)
  - links to the 7 topic pages
- `docs/firmware/espnow.md`, `ble-setup.md`, `identity.md`, `sealing.md`, `deep-sleep.md`, `buttons.md`, `radio-coexistence.md` -- one page per AC topic. Each page has "How it works", the key constants and timings, and a "Where to read the code" list (path plus symbol), and links the governing AD/N/H items.
- `docs/check-commands.sh` -- add `docs/firmware/*.md` to the default files, and update the header comment. Then any shell block there must carry a marker; check `docs/test.sh` still passes.
- `.github/workflows/docs.yml` -- add a `rustdoc` job ("Rust docs"). It checks out, runs `rustup toolchain install` and rust-cache, then runs `cargo doc --no-deps --locked -p coldframe-hal -p coldframe-crypto -p coldframe-protocol -p coldframe-setup -p coldframe-uplink -p coldframe-sensing -p coldframe-transport` with `RUSTDOCFLAGS: -D warnings`. Give it a reason comment citing Story 10.4, and update the header comment.
- `README.md` -- add a "Firmware reference" bullet under "Start here". Also add a link line in `apps/rs/README.md` and `packages/rs/README.md`.

**Acceptance Criteria:**
- Given `docs/firmware/`, when a maker reads it, then each of the 7 AC topics has a page that explains the mechanism with backticked repo paths and symbols, and every one of those paths passes `docs/check-links.sh`.
- Given the 7 firmware crates, when `RUSTDOCFLAGS="-D warnings" cargo doc --no-deps --locked -p …` runs, then it succeeds, and the generated `setup_v1`/`device_v1` items carry docs (no `missing_docs` allow remains in `packages/rs`).
- Given a PR touching `packages/**` or `docs/**`, when the Docs workflow runs, then the new job runs that `cargo doc` command and fails on any rustdoc warning.
- Given the crypto-spec change, when `pnpm --filter @coldframe/crypto-spec run check` runs, then nothing is stale.

## Spec Change Log

## Review Triage Log

### 2026-10-20 — Review pass
- verdicts: 18 findings — high 0, medium 4, low 13, false 1, maybe-false 0
- findings:
  - `[low]` `[patch]` identity.md calls the Hub `board/efuse.rs` "the same file" as the Node's, but the module comments differ — fixed: it now says same code, only the module comment differs. The Hub file's "only `unsafe` code of the Hub" comment and the `apps/rs/hub/Cargo.toml:60` lint comment are stale since `board/rtc.rs:41` has an `unsafe` block; that is pre-existing, so it is deferred.
  - `[medium]` `[patch]` ble-setup.md constants table: `SERVICE_UUID` shown as the text UUID, but it is a `u128` (`SERVICE_UUID_STR` is the text, `packages/rs/setup/src/lib.rs:45,54`), and `MAX_PAYLOAD` lists only the Hub copy (`apps/rs/node/src/board/ble.rs:35` also has one) — fixed: the row now names `SERVICE_UUID_STR` and the u128 form, and lists both ble.rs files.
  - `[low]` `[reject]` the rustdoc job lists the 7 crates by hand, so an 8th crate could miss the gate — `ci.yml`'s required `rust` job lists the same crates the same way. A new crate is rare, and the fix (`--workspace` plus excludes, or cargo metadata) is just another list or adds complexity.
  - `[low]` `[reject]` the firmware crates `apps/rs/{node,hub}` have no `cargo doc` in CI — the story AC scopes the gate to "public items in `packages/rs/*`". Building esp-toolchain docs in CI is new CI surface beyond the AC.
  - `[low]` `[reject]` the README's `cargo doc --open` block is `ci:manual` and widens `ci:manual` to "a browser" — this is a documentation choice. A `ci:run` would run the host toolchain inside the esp-only `commands` job, which adds risk for a cosmetic gain.
  - `[low]` `[patch]` the `docs.yml` header and the `check-commands.sh` line 2 header only described the build guide — fixed: both headers were reworded to cover `docs/build` and `docs/firmware`, and the `--help` line range was adjusted.
  - `[low]` `[reject]` the cited symbols and constant values are not machine-checked — a new checker adds complexity, and the 10-2 review already rejected this ("no automated check of documented facts").
  - `[low]` `[reject]` there is no page on the Hub→Server uplink (heartbeats, `hub-auth/v1` signing) — the story AC enumerates 7 topics and this is not one of them. A reader meets the gap only beyond the story's promise.
  - `[low]` `[reject]` there is no page on the Node measurement path (probe, BME680, battery) — this is outside the 7 AC topics, for the same reason as the row above.
  - `[low]` `[patch]` constants tables use different column layouts — fixed: `buttons.md` was reordered to `Constant | Value | Where`. The descriptive table in `radio-coexistence.md` was left as is, because its rows are findings, not symbols.
  - `[low]` `[patch]` the `packages/rs/protocol/build.rs` doc says an uncommented proto item "fails `cargo doc`", but `missing_docs` is only `warn` — fixed: it now says the item warns, and fails under CI's `-D warnings` (clippy, rustdoc).
  - `[low]` `[reject]` every page links the dated `_bmad-output` spine, and there is no ID glossary — the links resolve and `check-links` guards them. Adding a glossary is new content with little everyday harm.
  - `[medium]` `[patch]` the `docs.yml` path filters omit the root `Cargo.toml`, `Cargo.lock` and `rust-toolchain.toml`, so the Rust docs job skips lint, dependency or toolchain-only PRs — fixed: the three paths were added to both the `pull_request` and the `push` lists.
  - `[low]` `[patch]` the `coldframe-hal` `mock` feature items are not covered by the rustdoc gate — fixed: `--features coldframe-hal/mock` was added to the job. It was verified locally with `-D warnings`.
  - `[medium]` `[patch]` the new default `docs/firmware/*.md` glob in `check-commands.sh` had no test — fixed: a fixture case was added to `docs/test.sh` (50/50 pass). A mutation run that reverted the glob made the test fail (49/1).
  - `[medium]` `[patch]` (same root cause as the path-filter row) the rustdoc job does not run when only root Cargo files change — fixed by the path-filter patch above.
  - `[low]` `[reject]` intent audit: the spec reads "the firmware crates" as the 7 `packages/rs` crates, which is narrower than `apps/rs` — this is the same as the "no `cargo doc` for `apps/rs/{node,hub}`" row above, and the AC text itself says `packages/rs/*`.
  - `[false]` `[reject]` intent audit: the work is uncommitted — this is the review stage. The commit happens at finalization through gzp-pipeline (the On Complete hook owns commit, push and PR).

## Design Notes

**Why the gate lives in `docs.yml`:** the `rust` job in `ci.yml` is the required check. But any `ci.yml` edit forces run-all, which includes the Images job that is known to be slow and flaky, and specs 10-2 and 10-3 both forbid editing it. `docs.yml` is already path-filtered on `packages/**`. The AC only asks that `cargo doc` runs in CI. Clippy `-D warnings` in the required `rust` job already enforces `missing_docs` on hand-written items.

**Why glob re-export modules:** a `#[doc]` on `pub use … as setup_v1` cannot document the original `v1_` module. A wrapper module is documented, makes `v1_` unreachable publicly (so the lint no longer fires on it), and keeps every item path.

**Line numbers drift:** docs name symbols, and use `path:line` only where a symbol does not exist. `check-links.sh` strips `:N` anyway.

## Verification

**Commands:**
- `RUSTDOCFLAGS="-D warnings" cargo doc --no-deps --locked -p coldframe-hal -p coldframe-crypto -p coldframe-protocol -p coldframe-setup -p coldframe-uplink -p coldframe-sensing -p coldframe-transport` -- expected: success, no warnings
- `cargo clippy --workspace --all-targets --locked -- -D warnings && cargo test --workspace --locked` -- expected: green
- `pnpm --filter @coldframe/crypto-spec run check` -- expected: nothing stale
- `docs/test.sh && docs/check-links.sh && docs/check-commands.sh --list` -- expected: pass, with the new pages listed
- `buf lint` / `packages/proto/check-compat.sh` if `buf` is installed -- expected: pass
- `actionlint .github/workflows/docs.yml` if available -- expected: clean
- If the esp toolchain is available: `cargo build --release` in `apps/rs/node` and `apps/rs/hub` -- expected: still builds after the protocol module change

## Auto Run Result

Status: done

**Summary.** Added the firmware learning reference `docs/firmware/`: an index plus 7 topic pages (identity, sealing, ESP-NOW, deep sleep, buttons, BLE setup, radio coexistence). Each page explains its mechanism, lists the key constants and points to the code by path and symbol. It states the required facts (irreversible eFuse burn, unmeasured ~100 µA budget, no WPA3-only, short vs long press, never erase `cf_frame`) and flags where the code differs from the planning docs. Made every public item of `packages/rs/*` documented, including the generated wire types: proto comments now become rustdoc, and the `missing_docs` allow is gone. Fixed the 2 existing rustdoc warnings at their source. Added a `Rust docs` job (`cargo doc` with `RUSTDOCFLAGS=-D warnings`) to the Docs workflow. No human-only acceptance criteria, so no operator actions.

**Files changed.**
- `docs/firmware/{README,identity,sealing,espnow,deep-sleep,buttons,ble-setup,radio-coexistence}.md` -- the new reference.
- `packages/rs/protocol/build.rs` -- keeps proto source info, so comments become rustdoc.
- `packages/rs/protocol/src/lib.rs` -- `setup_v1`/`device_v1` are now documented glob re-export modules, and no `missing_docs` allow remains.
- `packages/proto/coldframe/setup/v1/setup.proto` -- comments on 8 enum values (comment-only).
- `packages/crypto-spec/scripts/lib/spec.ts` -- the namespace URL is now in a code span. The generated outputs are regenerated: `packages/rs/crypto/src/spec.rs`, `packages/cs/crypto/Generated/CryptoSpec.g.cs` and the Kotlin `CryptoSpec.kt`.
- `packages/rs/sensing/src/lib.rs` -- unambiguous `mod@env` link.
- `.github/workflows/docs.yml` -- new `rustdoc` job, root Cargo files added to the path filters, header updated.
- `docs/check-commands.sh` -- `docs/firmware/*.md` added to the default files, header updated.
- `docs/test.sh` -- fixture test for the firmware glob.
- `README.md`, `apps/rs/README.md`, `packages/rs/README.md` -- links to the reference.

**Review findings.** 18 findings: high 0, medium 4, low 13, false 1.
- **Patches applied (8 entries):** medium ×3 (ble-setup table facts; Docs path filters for root Cargo files, grouped with the verification-gap row; test for the `docs/firmware` glob) and low ×5 (identity "same file" wording, headers, buttons table layout, `build.rs` comment, `mock` feature in the rustdoc gate).
- **Deferred (1):** stale Hub "only unsafe code" comments, which are pre-existing.
- **Rejected:**
  - Hand-listed crates: they match `ci.yml`, and the alternative is the same list or more complexity.
  - No CI `cargo doc` for `apps/rs` (raised twice, once by the intent audit): the AC scopes the gate to `packages/rs/*`.
  - The `ci:manual` browser block: a cosmetic choice, and `ci:run` would put the host toolchain in the esp job.
  - Symbol and constant checker: adds complexity, and the 10-2 review rejected the same idea.
  - Uplink page and measurement page: outside the 7 AC topics.
  - ID glossary and dated links: the links are guarded and the harm is low.
  - "Uncommitted": false, because the commit happens at finalization.

**Follow-up review recommended: true.** Patched: high 0, medium 3, low 5. The named unverified risk: the 7 topic pages were drafted from the code and only spot-checked. The review itself found two wrong constants in `ble-setup.md`, so other cited constants, timings or symbol names may still be inaccurate. A second pass should check every "Key constants" table and "Where to read the code" symbol against the source.

**Verification.** All of the following passed:
- `RUSTDOCFLAGS="-D warnings" cargo doc --no-deps --locked --features coldframe-hal/mock -p` over the 7 crates: 0 warnings.
- `cargo fmt --check`, `cargo clippy --workspace --all-targets --locked -D warnings` and `cargo test --workspace --locked`: no failures.
- `pnpm --filter @coldframe/crypto-spec run check`: all outputs fresh.
- `docs/test.sh`: 50/50 pass. A mutation run that reverted the glob makes the new test fail.
- `docs/check-links.sh`: 819 links and paths resolve.
- `docs/check-commands.sh --list`: OK.
- `cargo build --release` of `apps/rs/node` and `apps/rs/hub` on the esp toolchain: builds.

`buf` and `actionlint` are not installed locally, so buf lint/breaking and actionlint did not run. The proto change is comment-only.

**Residual risks.**
- The `Rust docs` job is not a required check.
- Any future proto item without a comment now fails clippy in the required `rust` job too, not only the Docs job. This is intended, but stricter than before.
- `cargo doc` inside `apps/rs/node` still prints one warning of its own. That is outside the AC scope.

