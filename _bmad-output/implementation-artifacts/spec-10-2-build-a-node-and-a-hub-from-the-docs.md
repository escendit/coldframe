---
title: 'Build a node and a hub from the docs'
type: 'feature'
created: '2026-10-19'
status: 'awaiting-operator'
baseline_revision: '21ad924cfaeb1caa249d9f8c17b682e7ea3e2c4a'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-10-context.md'
warnings: [oversized]
deferred: []
operator_actions:
  - "Build one Node and one Hub from hardware/ (bom.md, node.md, hub.md, enclosure.md) on real parts, and correct any wiring or part error you find in those pages."
  - "Check every example part number and figure in hardware/bom.md (TPS22917, RT9080-33, TP4056 input limit and R_PROG table, CN3791, the cell size) against its datasheet before anyone orders parts."
  - "Flash a test board in dev mode from source, and a fresh board from the first release's binaries (after Story 10-1's v0.1.0 release), following docs/build/flash.md; confirm the identity line and the setup code appear as documented (Hub at first boot, Node on a 3 s long press), and fix any log line that differs."
  - "Measure the reference Node's average sleep current with docs/bench/node-power-checklist.md, record the result in that checklist's Result table, and replace the 'not measured yet' notes in docs/build/power.md and hardware/node.md with the measured value."
---

<intent-contract>

## Intent

**Problem:** `hardware/` is a placeholder, and `docs/build/` holds only the apps guide. A maker cannot build a Node or a Hub, wire the switched probe and divider, flash release or source firmware, or learn the irreversible eFuse burn without reading the firmware source. No CI job checks that the docs' links or build commands still work.

**Approach:** Add a reference hardware design to `hardware/`: schematics as Markdown connection tables and ASCII block diagrams, a BOM, the wiring, enclosure notes and probe sealing. Add an adopter build guide to `docs/build/` covering flashing, first boot, eFuse and dev mode, power, and placement. Add a new path-filtered `docs.yml` workflow. It resolves every relative link and referenced repo path, and it runs every build command the guide documents against the firmware crates, with build only and no flashing.

## Boundaries & Constraints

**Always:**
- **Facts come from the code.** Every pin, constant and command must match the firmware.
  - Pins: `apps/rs/node/src/board/pins.rs`.
  - Wake and button timing: `packages/rs/sensing/src/{wake,button}.rs`.
  - Partition offsets: `apps/rs/{hub,node}/partitions.csv`.
  - Battery curve: `packages/rs/sensing/src/battery.rs`.
  - Soil calibration: `apps/cs/server/Sensors/CalibrationMath.cs`.
  - Link to the source file rather than restating a whole table where it could drift.
- **Label the design honestly.** `hardware/` is under Apache-2.0 through the root `LICENSE`, and every hardware page says so. Every part is a *reference choice* that has not been bench-verified: say so in `hardware/README.md`, and give the electrical requirement each part must meet. Examples: LDO quiescent current ≤ ~5 µA and ≥ 500 mA output; the charger has an NTC input and an open-drain active-low status pin; the load switches are active-high and need an external pull-down.
- **The eFuse warning is prominent.** It is a blockquote at the top of `docs/build/flash.md`, before any flash command. It says that the first boot of any build without `--features dev-mode`, debug or release, burns an `HMAC_UP` key block and that this cannot be undone. It says how to use dev mode on test boards, and that release binaries always burn.
- **State the setup code accurately.** The Hub prints `setup code=XXXXXXXX` on every boot while unprovisioned, which includes its first boot. The Node prints it when it enters setup mode, on a long press (3 s). Document how to watch the serial output, and the `erase-region` recovery.
- **Mark every command block.** Every fenced `bash`/`sh` block in `docs/build/*.md` is preceded by a marker:
  - `<!-- ci:run <repo-relative dir> -->`: CI runs the block in that directory, after `source ~/export-esp.sh`.
  - `<!-- ci:hardware -->`: the block needs a board. CI checks that every `espflash <subcommand>` in it is a real subcommand, with `espflash <subcommand> --help`.
  - An unmarked block fails the check.
- Copy the firmware pins (espup 0.17.1, esp toolchain 1.95.0.0, espflash 4.5.0), the apt packages, the install steps and the rust-cache key from the `firmware` job in `ci.yml`. Pin actions by SHA with a release comment.

**Never:**
- Don't edit `.github/workflows/ci.yml`, because that reruns the slow Images job. Don't make the docs workflow a required check.
- Don't change firmware behaviour or the Server.
- Don't check external `http(s)` links: they need the network and they flake.
- Don't add a docs site generator.
- Don't edit `sprint-status.yaml`.
- Don't claim a measured sleep current. The measurement is still pending.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Good docs | every relative link and referenced path exists | `check-links.sh` exits 0 | No error expected |
| Broken link | `[x](../nope.md)` in `docs/build/flash.md` | exits 1 | names the file, line and target |
| Anchor and query | `[x](power.md#budget)`, `<https://…>`, `mailto:` | only the file part is checked; external links are skipped | No error expected |
| Backtick path | `` `apps/rs/node/src/board/pins.rs` `` | checked to exist | missing → names file:line |
| Placeholder path | `` `coldframe-node-X.Y.Z.bin` ``, `` `<port>` ``, `` `apps/rs/*/` `` | not checked | No error expected |
| Run block | `ci:run apps/rs/node` + `cargo build --release --locked` | `check-commands.sh` runs it there | non-zero status → names the block's file:line |
| Hardware block | `ci:hardware` + `espflash write-bin 0x0 …` | only `espflash write-bin --help` runs | unknown subcommand → fails |
| Unmarked block | a ```` ```bash ```` block with no marker | fails | names file:line |

</intent-contract>

## Code Map

- `apps/rs/node/src/board/pins.rs:7-17,22-31,45-54,78-91`: the pin table.
  - GPIO1 soil (ADC1); GPIO2 divider midpoint; GPIO4 probe switch; GPIO5 divider switch; GPIO6 CHRG (active low, pull-up); GPIO7 button to GND; GPIO8/9 I²C SDA/SCL; BME680 at 0x77.
  - The divider is 100 kΩ/100 kΩ with a midpoint capacitor of at most 10 nF, and it is high-side switched; a low-side NMOS is ruled out. The pads float in deep sleep, so add external pull-downs.
- `apps/rs/node/README.md:140-172,204-238,282-329` and `apps/rs/hub/README.md:62-114,133-221`: existing prose to link to rather than duplicate. It covers buttons, build commands, the eFuse WARNING wording, `erase-region` recipes (Hub cf_setup 0xC000/0x2000, cf_ident 0xE000/0x1000; Node cf_setup 0x12000/0x2000, cf_ident 0xD000/0x1000) and WPA3-only being unsupported. The Hub also needs cmake and ninja.
- `packages/rs/sensing/src/button.rs:21-27`: debounce 50 ms, long press 3 s.
- `packages/rs/sensing/src/wake.rs:35`: wakes every 15 min.
- `packages/rs/setup/src/node.rs:30`: the setup window is 180 s.
- `packages/rs/sensing/src/battery.rs:1-5,32-54`: battery % comes from a 21-point curve and is always approximate.
- `apps/cs/server/Sensors/CalibrationMath.cs`: soil % is two-point linear, rounded to 5 %, with no temperature compensation.
- `docs/bench/node-power-checklist.md`: the measurement method (a meter in series with the battery, ≥ 3 periods, release build, pass at ≤ ~100 µA). Link to it from the power page. Read-only.
- `_bmad-output/planning-artifacts/prds/prd-coldframe-2026-09-25/addendum.md:35-44,119-120` and `_bmad-output/specs/spec-coldframe/device-hardware.md:52-75`: the solar requirements (~1 W panel, no charging below 0 °C or above ~45 °C), the capacitive-probe caveats (NE555 "v1.2" boards, unsealed edges), and the board sleep-current table.
- `docs/operations/release.md:43,140` and `.github/workflows/release.yml:266-273`: the release assets `coldframe-{hub,node}-X.Y.Z.bin`, a merged image written with `espflash write-bin 0x0`, and `SHA256SUMS`. Local equivalent: `espflash save-image --chip esp32s3 --merge --partition-table partitions.csv <elf> <bin>`.
- `.github/workflows/ci.yml:203-314`: the `firmware` job to copy setup from. Read-only.
- `deploy/release/test.sh:1-40`: the `expect`/`pass`/`failed` test style.
- `README.md:13-14`, `docs/quickstart.md:318-338,467`, `docs/build/apps.md`: add links to the new guide.
- `.gitignore:463-466`: `docs/build/` is already un-ignored, and `hardware/` is not ignored. Check new paths with `git check-ignore`.

## Tasks & Acceptance

**Execution:**
- `hardware/README.md`: rewrite it as an index:
  - status ("reference design, not yet bench-verified");
  - the Apache-2.0 notice;
  - links to every page below and to `docs/build/`.
- `hardware/bom.md`: the Node and Hub BOM, each line with a part, an example part number and the requirement it must meet. Reference choices:
  - ESP32-S3-WROOM-1 (N8) module. The Hub uses an ESP32-S3 DevKitC-1 (N8) on USB 5 V.
  - A capacitive soil probe, with the NE555 caveat.
  - A BME680 breakout with SDO high.
  - A 1S LiPo, 800 mAh, with a 10 kΩ NTC.
  - A ~1 W 5–6 V solar panel.
  - A TP4056 charger with TEMP/NTC and an open-drain `CHRG`; CN3791 as a solar alternative.
  - A low-Iq 3.3 V LDO.
  - Two high-side load switches.
  - The 100 kΩ/100 kΩ divider with a ≤ 10 nF capacitor.
  - Pull-downs, the button, and the enclosure parts.
- `hardware/node.md`: the Node schematic, as an ASCII block diagram and per-block connection tables for:
  - the power path (panel → charger with NTC → LiPo → LDO → module);
  - the switched probe power on GPIO4;
  - the switched battery divider on GPIO5 → GPIO2;
  - CHRG → GPIO6;
  - the button GPIO7 → GND;
  - I²C to the BME680;
  - the USB/UART flashing header with EN/BOOT.

  Add a wiring table that is a full pin-to-pin list, and link to `pins.rs` as the source of truth.
- `hardware/hub.md`: the Hub schematic and wiring. A dev board on USB 5 V uses no GPIO, so cover power, antenna clearance and placement on the home network.
- `hardware/enclosure.md`: enclosure notes (IP rating, venting for the BME680, cable glands, keeping the panel and antenna clear), probe sealing (conformal coat or epoxy on the electronics and cut edges, heat-shrink, the insertion line), and the NFR-5 outdoor requirement.
- `docs/build/README.md`: the adopter build-guide index. It sets the order (parts → assemble → toolchain → flash Hub → flash Node → add them from the app) and links to `apps.md`.
- `docs/build/flash.md`:
  - the eFuse and dev-mode blockquote at the top;
  - installing espup and espflash at the pinned versions;
  - flashing from release binaries: download, `sha256sum --check --ignore-missing SHA256SUMS`, `espflash write-bin 0x0 …`;
  - flashing from source: `cargo build --release --locked`, then `save-image --merge` or `cargo run --release`;
  - the first-boot serial output, with the identity line and the setup code (Hub at first boot, Node on a long press);
  - recovery with `erase-region`;
  - the WPA2 / mixed WPA2/WPA3 requirement;
  - short press versus long press.

  Mark every block.
- `docs/build/power.md`:
  - panel placement above the canopy, angled to the sun, not shaded by the plant;
  - the ~1 W panel and 14 days without sun;
  - the sleep-current budget of ≤ ~100 µA, why a stock dev board draws ~1 mA, and how to measure it (link the bench checklist);
  - why soil % (two-point calibration, temperature and salinity, settling for 2–3 days) and battery % (curve, worse while charging) are approximate.
- `docs/check-links.sh`: usage `[root…]`; default `docs hardware README.md`.
  - For every `.md` file, resolve inline `[text](target)` links that are not external, relative to the file.
  - Resolve backtick paths that start with `apps/ packages/ deploy/ docs/ hardware/ tests/ .github/` from the repo root, skipping any that contain `<`, `*`, `{`, `X.Y.Z` or a space.
  - Exit 1 and list every failure.
- `docs/check-commands.sh`: usage `[file…]`; default `docs/build/*.md`. It extracts the marked blocks and runs them as described in Always. `--list` prints the blocks without running them.
- `docs/test.sh`: tests the I/O matrix for both scripts against fixtures in `mktemp -d`, in the `deploy/release/test.sh` style. `check-commands.sh` is tested with a stub `espflash` and with blocks that run `true`/`false`.
- `.github/workflows/docs.yml`:
  - Triggers: `pull_request` and `push` on main, with paths `docs/**`, `hardware/**`, `README.md`, `apps/rs/{hub,node}/**`, `packages/rs/**` and the workflow itself, plus `workflow_dispatch`.
  - `links` job: `docs/test.sh`, then `docs/check-links.sh`.
  - `commands` job: firmware setup copied from ci.yml, then `docs/check-commands.sh`.
  - Use `permissions: contents: read`.
- `README.md`, `docs/quickstart.md`, `docs/build/apps.md`: link to `docs/build/README.md` and `hardware/README.md`.

**Acceptance Criteria:**
- Given `hardware/`, when a maker opens it, then they find:
  - the Node and Hub schematics;
  - a BOM with every listed part (ESP32-S3, capacitive probe, BME680, 800 mAh LiPo, solar panel, a charger with NTC and a status pin);
  - the wiring, including the switched probe power and the switched battery divider;
  - enclosure notes and probe sealing;
  - the Apache-2.0 notice.
- Given `docs/build/`, when a maker follows it, then it shows how to flash both Devices over USB with `espflash`, from release binaries and from source, and where the setup code appears. A blockquote before the first flash command warns that the eFuse burn is irreversible and explains dev mode.
- Given the power guidance, when read, then it covers panel placement above the canopy, the ≤ ~100 µA budget and how to measure it, and why soil % and battery % are approximate.
- Given a pull request touching `docs/build/`, when the `Docs` workflow runs, then it fails on any broken relative link or referenced path. It runs every `ci:run` block against the firmware crates, and only `--help` for `ci:hardware` blocks.

## Spec Change Log

## Review Triage Log

### 2026-10-19 — Review pass
- verdicts: 33 findings — high 0, medium 8, low 22, false 2, maybe-false 1
- findings:
  - `[low]` `[patch]` (blind) `check-commands.sh --help` printed `set -uo pipefail` — the help range now ends at the last header line.
  - `[low]` `[reject]` (blind) Anchors and reference-style links are not checked — no doc uses reference-style links, and a broken anchor still lands on the right page; checking anchors needs heading-slug generation, which is more than a direct fix.
  - `[medium]` `[patch]` (blind) The docs.yml path filter misses files the docs link to (apps/cs, deploy, tests, …) — both path lists now cover apps/**, packages/**, deploy/**, tests/**, aspire/**, .github/workflows/**, LICENSE and .gitignore.
  - `[low]` `[patch]` (blind) power.md listed the 20 µA row as ~1.1 mAh a day (inconsistent with the wake overhead), and the 100 µA budget had no row — corrected to ~1.6 mAh, and a Budget row was added (~3.5 mAh, about 180 days).
  - `[medium]` `[patch]` (blind) BOM N3's example, Adafruit 3660, has a regulator and level shifter, which breaks its own requirement — now requires a bare breakout and says such boards do not qualify unless modified.
  - `[medium]` `[patch]` (blind) No documented way to connect USB to the Node's header — added BOM N16a and a "Connecting USB" section in node.md (GND/D−/D+, no VBUS), linked from flash.md.
  - `[medium]` `[patch]` (blind) No download-mode recovery for a sleeping or crash-looping Node — flash.md now has the BOOT+RESET steps and notes that the enclosure must be opened.
  - `[maybe-false]` `[reject]` (blind) The CHRG internal pull-up may draw ~70 µA in deep sleep while charging — whether the pad's pull-up survives deep sleep is unverified. If it does, it only draws while the panel is charging, so it would be low; it is firmware behaviour from Story 4.1 either way. Settled by a bench measurement while charging.
  - `[low]` `[reject]` (blind) Deep-discharge and brownout behaviour is not covered — rarely met (the low-battery Alert from Story 7.3 fires first); it needs new content, not a direct fix.
  - `[low]` `[reject]` (blind) ci:hardware checks only subcommand names, not flags or other tools — every documented flag is valid today (`espflash write-bin --help` lists `--monitor`); checking flags means parsing per tool, which is more than a direct fix.
  - `[low]` `[patch]` (blind) The default roots skipped apps/rs/hub and apps/rs/node READMEs — added; both pass.
  - `[low]` `[patch]` (edge) On a fresh machine the install block failed before it could create the export file — the export file is now sourced only if it exists; the test was updated.
  - `[low]` `[reject]` (edge) An unclosed fence or a 4-backtick fence swallows the rest of the file — no doc has either; the fix adds fence-length tracking.
  - `[low]` `[reject]` (edge) A global option before the espflash subcommand goes unchecked — no doc writes it that way; the fix adds option parsing.
  - `[low]` `[reject]` (edge) A missing espflash gives a misleading message — the run still fails; CI always installs espflash first.
  - `[low]` `[reject]` (edge) An empty docs/build passes with 0 files — unlikely (the guide is the subject of the check, and docs/test.sh asserts the repository guide's blocks).
  - `[low]` `[reject]` (edge) Reference-style links, HTML href and targets with `)` are not checked — none are used in the checked docs; same root cause as the blind anchor row.
  - `[low]` `[reject]` (edge) A heading-anchor rename passes silently — same as the blind anchor row.
  - `[low]` `[reject]` (edge) Outside a git checkout, the gitignored-path skip fails — CI and contributors use git; adding a work-tree probe is a guard for an unshown case.
  - `[low]` `[patch]` (edge) An angle-bracket target containing a space was cut at the space — bracketed targets now keep spaces; two tests were added.
  - `[medium]` `[patch]` (edge) Path filter misses referenced paths — same root cause and fix as the blind path-filter row.
  - `[low]` `[patch]` (edge) check-commands `--help` extra line — same fix as the first blind row.
  - `[false]` `[reject]` (edge) check-links `--help` omits the DOCS_REPO_ROOT line — the header ends at line 18 and `2,18p` prints it; the `set` line is 19.
  - `[false]` `[reject]` (edge) The guide's install block re-downloads the toolchain on every run — measured locally: with the toolchain in place, the whole `check-commands.sh` run took 2.4 s, so espup reuses the existing install.
  - `[low]` `[patch]` (edge) Inconsistent per-day figures in power.md — same fix as the blind power row.
  - `[low]` `[reject]` (edge) The AC claims the workflow fails on any broken link — inline links, code-span paths and bracketed targets are checked; anchors and reference-style links are covered by the blind anchor row.
  - `[medium]` `[patch]` (verification-gap) docs.yml's path filter lets link-breaking changes through — same fix as the blind path-filter row.
  - `[medium]` `[patch]` (verification-gap) Nothing checks that the documented toolchain pins match ci.yml — docs/test.sh now reads the pins from ci.yml and fails when release.yml, docs.yml or flash.md (prose and install block) differ; ci.yml and release.yml are in the trigger paths. Drift was confirmed to fail the test.
  - `[medium]` `[reject]` (intent) Human-owed work (bench build, real flash, sleep-current measurement) is not in operator_actions — the fix is a spec edit; finalization records it under operator_actions with status awaiting-operator.
  - `[low]` `[reject]` (intent) No CI check compares the documented pins and constants with the firmware — the current values match (checked by hand against pins.rs, wake.rs, button.rs and partitions.csv); an automated fact comparison is new tooling, not a direct fix.
  - `[low]` `[reject]` (intent) The release-binary path is only checked by subcommand name — it needs a real Release and a board; owed as an operator action.
  - `[low]` `[reject]` (intent) The workflow has not run on GitHub yet — the PR run is that verification; it is listed under residual risks.
  - `[low]` `[reject]` (intent) The spec restates the AC at document level — the fix is a spec edit.

## Design Notes

**Why Markdown schematics.** A KiCad project written without an EDA tool or a bench could not be verified in this repo. Connection tables can be reviewed against `pins.rs` and checked by CI. A KiCad capture and PCB can follow once the bench verification is done (an operator action).

**Why markers.** The AC asks that *documented* commands run. With markers, every block is either executed or explicitly marked as needing hardware, so an unmarked block fails the check instead of going untested.

## Verification

**Commands:**
- `docs/test.sh`: expected all pass.
- `docs/check-links.sh`: expected exit 0.
- `docs/check-commands.sh --list`: lists every block with its marker.
- `docs/check-commands.sh`: expected exit 0, with the esp toolchain installed locally.
- `actionlint .github/workflows/docs.yml`, if available: expected no findings.

**Manual checks (if no CLI):**
- `hardware/node.md` against `pins.rs`: every GPIO and divider value matches.

## Auto Run Result

Status: awaiting-operator

**Summary.** `hardware/` now holds a reference Node and Hub design under Apache-2.0: schematics as ASCII block diagrams and connection tables, a BOM with the requirement each part must meet, a full pin-to-pin wiring list tied to `pins.rs` (including the switched probe power and the high-side switched battery divider), enclosure notes and probe sealing. It is labelled "not yet bench-verified". `docs/build/` gains an adopter build guide:
- `flash.md` opens with the eFuse and dev-mode warning. It covers flashing over USB with `espflash` from release binaries (with a checksum check) or from source, the first-boot output and the setup code (Hub at first boot, Node on a 3 s press), download mode, the buttons, WPA2, and `erase-region` recovery.
- `power.md` covers panel placement, the ≤ ~100 µA budget, how to measure it, and why soil % and battery % are approximate.

A new `Docs` workflow checks every relative link and referenced repo path. It runs every `ci:run` command block against the firmware crates (build only), checks `ci:hardware` blocks by espflash subcommand, and keeps the documented toolchain pins in step with ci.yml.

**Files changed**
- `hardware/README.md`, `bom.md`, `node.md`, `hub.md`, `enclosure.md`: the reference design.
- `docs/build/README.md`, `flash.md`, `power.md`: the adopter build guide. `docs/build/apps.md`, `README.md` and `docs/quickstart.md` link to it.
- `docs/check-links.sh`: the link and path resolver.
- `docs/check-commands.sh`: the runner for marked command blocks.
- `docs/test.sh`: 44 fixture tests, including the pin-drift tests.
- `.github/workflows/docs.yml`: the Links and Commands jobs. They are path-filtered and not required; `ci.yml` is untouched.

**Review findings.** There were 33 findings: high 0, medium 8, low 22, false 2, maybe-false 1.
- **Patched (10 root causes):** 5 medium (the path filter, the BOM N3 example, Node USB connection, download mode, the pin-drift check) and 5 low (`--help` range, power table, default roots, the fresh-machine export, angle-bracket targets).
- **Deferred:** none.
- **Rejected:** each with its reason in the triage log:
  - anchors and reference-style links;
  - the CHRG pull-up in sleep (maybe-false, firmware);
  - deep discharge;
  - flag checks in `ci:hardware` blocks;
  - unclosed fences;
  - a global espflash option before the subcommand;
  - a missing espflash;
  - an empty `docs/build/`;
  - a non-git checkout;
  - the AC "any broken link" claim;
  - the operator-action bookkeeping (handled here);
  - an automated fact comparison with the firmware;
  - the release path checked by name only;
  - the not-yet-run workflow;
  - the spec restatement;
  - two false findings (`check-links --help`, toolchain re-download).

**Follow-up review: recommended (true).** Five medium entries were patched. The risk not yet verified is the widened `docs.yml` triggers: the Commands job now runs on most PRs, and neither job has run on GitHub yet.

**Verification.**
- `docs/test.sh`: 44 passed.
- `docs/check-links.sh`: 419 links and paths in 25 files resolve.
- `docs/check-commands.sh --list`: 14 blocks, all marked.
- `docs/check-commands.sh`: exit 0 locally with the esp toolchain. It ran the Hub and Node release and dev-mode builds and `save-image --merge`, and checked every espflash subcommand.
- The implementer ran actionlint and shellcheck: clean. Gitleaks on `docs/`, `hardware/` and `.github/`: no leaks.
- `hardware/node.md` was checked by hand against `pins.rs`.

**Residual risks.**
- The design is a reference design that nobody has built yet. Part numbers and some figures (TPS22917, RT9080-33, the TP4056 input limit and R_PROG table, CN3791) come from memory and have not been checked against datasheets.
- The Node's sleep current has not been measured.
- The documented log lines come from the firmware source, not from a captured boot.
- The `Docs` workflow has not run on GitHub yet.
