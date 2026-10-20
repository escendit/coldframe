---
title: 'Someone else reproduces Coldframe'
type: 'feature'
created: '2026-10-20'
status: 'awaiting-operator'
baseline_revision: 'c8c4a2c3c8e770d5b4c32e78273508c8dc0e222c'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-10-context.md'
warnings: [oversized]
deferred:
  - summary: >-
      docs/check-links.sh ignores #anchor fragments, so the ~30 section links of the reproduction checklist can break silently when a guide heading is renamed.
    evidence: |-
      check-links.sh header says it checks "the file part of the target (no #anchor, no ?query)"; docs/test.sh:93-100 fixture expects exit 0 for power.md#budget against a file with only "# Power". All anchors resolve today (checked by script). Pre-existing, deliberate Story 10.2 behaviour; fix is anchor-slug resolution in check-links.sh plus a failing fixture in docs/test.sh.
    location: >-
      docs/check-links.sh
    severity: low
operator_actions:
  - "Publish the first release tag vX.Y.Z (story 10-1's operator actions) from a main that contains docs/reproduce/, and confirm its GitHub Release carries the firmware binaries and SHA256SUMS."
  - "Find a person other than the author, or prepare a clean machine, fresh Node and Hub boards and no prior Coldframe state for an author run, and have them run docs/reproduce/README.md parts A to H against that release tag with only the public repository and docs."
  - "File every stuck point of that run with the Reproduction stuck point issue form, and merge a fix for each one into the docs (or into a new release tag when the fix is in firmware, Server, charts or apps)."
  - "Commit the run's record as docs/reproduce/results/YYYY-MM-DD-<tester>.md copied from TEMPLATE.md, and update its Fixed by PR column and verdict until it reads Passed before closing story 10-5."
  - "After this PR merges, open the repository's New issue page on GitHub and confirm the Reproduction stuck point form is offered with its part, guide, environment and release fields."
---

<intent-contract>

## Intent

**Problem:** SM-5 ("reproducible from the docs") has no test instrument. Nothing in `docs/` tells a second person what to do end to end, from parts to a Threshold Alert, what counts as passing, how to file a stuck point, or where the run's results go. The story's real proof, a person other than the author (or the author on a clean machine with fresh hardware) completing the run, can only be done by a human.

**Approach:** Add a manual reproduction checklist under `docs/reproduce/` that walks Stories 10.2 and 10.3 end to end through the existing guides. It links them and does not copy their commands. Add a result template, a results folder, and a GitHub issue form for stuck points. Link the checklist from the README and from the build and deploy guides. The run itself, the issues it raises, the doc fixes and the committed result are operator actions.

## Boundaries & Constraints

**Always:**
- The checklist follows the guides in order: release tag → parts and assembly → flash Hub and Node → deploy (RKE2 + Fleet) → build own apps → add Hub, add Node → calibrate → Threshold Alert inside the Notification Window. Each step links the guide section that does the work.
- Each check is a `- [ ]` box with an observable expected result, in the style of `docs/bench/*-checklist.md` (one `#` title, intro, "What you need", lettered `##` parts, a closing "Result" section).
- State the AC's entry conditions: public repo and docs only; a person other than the author, or the author on a clean machine and fresh hardware with no prior state. No help from the author beyond what is in the repository.
- Every stuck point becomes a GitHub issue (through the new form), and the doc fix lands before the story closes. The result record lists every issue.
- Facts must match the existing docs: setup code (Hub on first boot, Node on a 3 s long press), the 180 s setup window, 2.4 GHz WPA2 or mixed WPA2/WPA3 Wi-Fi (no WPA3-only), irreversible eFuse burn outside dev mode, calibration after 2–3 days settling, low Threshold default 30 %, Notification Window default 07:00–22:00, and an Alert after three consecutive Readings below the low Threshold.
- An author run counts only with an attested clean machine, fresh hardware and no prior state (no cached toolchain, cluster, realm or enrolled boards). The story stays open until every listed stuck-point issue is fixed in the docs.
- Never commit secrets: the result template warns against setup codes, keys, tokens and domains' credentials.

**Never:**
- Edit `.github/workflows/ci.yml` or `sprint-status.yaml`.
- Copy guide commands into the checklist. Add no fenced `bash`/`sh` blocks (they would need `ci:` markers and duplicate the guides).
- Fake a reproduction result or pre-fill a run record.
- Create new GitHub labels.

</intent-contract>

## Code Map

- `docs/build/README.md` -- the 6-step build guide (parts, assemble, toolchain, flash Hub, flash Node, add from the app). Anchors: `#1-get-the-parts` … `#6-add-them-from-the-app`. Its "Afterwards"-style pointers are where a link to the checklist fits (end of step 6).
- `docs/build/flash.md` -- `#toolchain`, `#flash-from-the-release-binaries`, `#first-boot-and-the-setup-code`, `#the-buttons`, `#wi-fi`. It holds the eFuse warning.
- `docs/build/power.md#soil-moisture` -- calibration timing.
- `docs/build/apps.md` -- build your own apps (`#android`, `#ios`, `#server-push`).
- `docs/deploy/README.md` -- six sections (`#1-domain-and-dns-api-token` … `#6-first-sign-in`) and `#afterwards`. It links `docs/operations/install.md#verification-checklist-on-the-home-server`.
- `docs/bench/push-checklist.md:102-125` -- the Alert part (Notification Window default, three Readings below the low Threshold, notification text). Style model for checklists. `docs/bench/node-power-checklist.md:87-91` -- a Result table model.
- `docs/operations/release.md` -- release tags. No `vX.Y.Z` tag exists yet (`gh release list` is empty), so the run depends on story 10-1's operator action.
- `hardware/README.md`, `hardware/bom.md` -- parts.
- `README.md:10-20` -- "Start here" list.
- `docs/check-links.sh` -- default roots include `docs` (recursive) and `README.md`. It checks relative links and backticked `apps/ packages/ deploy/ docs/ hardware/ tests/ .github/` paths. `docs/check-commands.sh` default glob is `docs/build/*.md docs/firmware/*.md` only.
- `.github/` has no `ISSUE_TEMPLATE/` yet. Repo labels are the GitHub defaults (`bug`, `documentation` absent). `ci.yml:87,112` path filters do not match `.github/ISSUE_TEMPLATE/**`, so an issue form does not force run-all. `docs.yml` does not trigger on it either.

## Tasks & Acceptance

**Execution:**
- `docs/reproduce/README.md` -- the reproduction checklist. Intro (SM-5, who may run it, entry conditions, link to the issue form and to `results/`). "What you need" (release tag, parts from the BOM, home server, domain at Cloudflare, SMTP, S3, phone(s) with Apple/Firebase accounts, a clean machine). Parts A–H:
  - A: Start clean — attestation
  - B: Release
  - C: Build the Node and Hub
  - D: Flash
  - E: Deploy
  - F: Build the apps
  - G: Add Hub and Node, calibrate
  - H: Threshold Alert inside the Notification Window

  Each part links the guide section and lists observable checks. Then "When you get stuck" (file the issue form, note it, the story stays open until fixed) and "Record the result" (copy the template).
- `docs/reproduce/results/TEMPLATE.md` -- the copy-me result record: tester (handle or "author, clean machine"), date, release tag, hardware, server OS, phones; attestation box; one row per part A–H (pass/fail/notes); a stuck-points table (issue number, part, fixed by PR); a final verdict line; a no-secrets warning.
- `docs/reproduce/results/README.md` -- what the folder holds, the file naming `YYYY-MM-DD-<tester>.md`, and the rule that SM-5 holds only when a run passes with every listed issue fixed.
- `.github/ISSUE_TEMPLATE/reproduction-stuck-point.yml` -- a GitHub issue form: title prefix `[reproduce] `, label `bug`. Fields: checklist part (dropdown A–H), guide page and section, what you did, what you expected, what happened, release tag, and a confirmation checkbox that no secret is included.
- `README.md` -- add a "Reproduce Coldframe" bullet under "Start here".
- `docs/build/README.md`, `docs/deploy/README.md` -- one sentence each, pointing a second-person tester to `docs/reproduce/README.md`.

**Acceptance Criteria:**
- Given the repository, when a tester opens `docs/reproduce/README.md`, then it walks Stories 10.2 and 10.3 in order. It covers building a Node and a Hub, deploying, installing their own apps, adding the Hub and the Node, calibrating, and receiving a Threshold Alert inside their Notification Window. Every step links the guide that does it, and every link and path passes `docs/check-links.sh`.
- Given a stuck point, when the tester opens a new issue on GitHub, then the "Reproduction stuck point" form is offered with the fields above.
- Given a finished run, when the tester copies `docs/reproduce/results/TEMPLATE.md`, then the record captures the attestation, a per-part result, every stuck-point issue with its fixing PR, and a verdict, and it says where the record is committed.
- Given the change, when `docs/test.sh`, `docs/check-links.sh` and `docs/check-commands.sh --list` run, then all pass.

## Spec Change Log

## Review Triage Log

### 2026-10-20 — Review pass
- verdicts: 23 findings — high 0, medium 4, low 17, false 2, maybe-false 0
- findings:
  - `[medium]` `[patch]` TEMPLATE.md offers "Passed with open issues" while README Result calls anything else a failed run — fixed: verdicts are now Passed / Pending doc fixes / Failed at part X, and Result says a pending run becomes a pass once every listed fix merges.
  - `[low]` `[patch]` results/README.md forbids editing a record yet asks to fill "Fixed by PR" later, with no rule for the verdict (same root cause as the row above) — fixed: only the Fixed by PR column and the Verdict line (Pending → Passed) may change after the run.
  - `[medium]` `[patch]` a rerun cannot meet "on one release tag" or Part A's fresh boards — fixed: "When you get stuck" says to rerun the affected parts from the fixed docs on `main` with the same artifacts and already-flashed boards, noted in the same record.
  - `[low]` `[reject]` tags cut before this PR lack the checklist — no release tag exists yet (`gh release list` is empty), so the first tag will contain it; the operator action asks for a tag from a main that has docs/reproduce/.
  - `[low]` `[patch]` Part D checks the Hub's burned/existing identity but not the Node's — fixed: added the Node's `source=burned KEYn` once / `source=existing KEYn` same Device ID check (flash.md:211,222).
  - `[low]` `[patch]` Part E has no check for deploy section 4 (realm) or for replacing the bootstrap admin — fixed: one check each, matching docs/deploy/README.md:147-167 and section 6.
  - `[low]` `[patch]` Part H's recovery check passes even if the Lot never recovers — fixed: the Lot must show within its Thresholds again (the Alert closes).
  - `[low]` `[patch]` issue form uses only `bug` and has no environment fields — the label half is refuted: the `[reproduce] ` title prefix lets a maintainer list every stuck point, and the spec forbids new labels; fixed the other half with an optional Environment textarea.
  - `[medium]` `[patch]` stuck points needing a code/chart fix have no path to "fixed in the docs" (same root cause as the rerun row) — fixed: such a fix ships in a new release tag and the run continues from the first affected part on it.
  - `[false]` `[reject]` "Story 10.5" and "SM-5" are internal IDs an outsider cannot read — the intro explains SM-5 in plain words ("can be reproduced from the public repository alone"), and the "(Story N.M)" H1 matches every docs/bench checklist.
  - `[low]` `[patch]` author tester id spelled `author-clean` and "author, clean machine" — fixed: `author-clean` in TEMPLATE.md too.
  - `[low]` `[defer]` ~25 `#anchor` links are never checked by CI, and `.github/ISSUE_TEMPLATE/**` is outside docs.yml's path filters — anchors all resolve today; ignoring fragments is the deliberate, tested Story 10.2 behaviour of check-links.sh, so it is deferred; the form is validated by GitHub when it renders.
  - `[low]` `[patch]` Part A does not rule out reused accounts and credentials from an earlier install — fixed: a Part A box and an Attestation box for Firebase project, APNs key, Cloudflare token and S3 bucket (author runs).
  - `[medium]` `[patch]` (edge) the "Passed with open issues" verdict lets a record claim a pass the checklist counts as failed — same group as the first row; fixed there.
  - `[low]` `[patch]` (edge) a non-author cannot tell which Part A boxes apply while the pass rule needs all ticked — fixed: the remaining Part A items are required for author runs; a non-author ticks them where true and writes n/a otherwise.
  - `[low]` `[patch]` (edge) the checksum check only lists the two .bin files, but `--strict` needs every asset — fixed: the check runs with every Release asset next to `SHA256SUMS` (release.md "Verify a download").
  - `[low]` `[patch]` (edge) Part E asks for every Secret of section 3 although `coldframe-push` is optional and needs Part F's keys (docs/deploy/README.md section 3, "optional") — fixed: "every required Secret", push may follow in Part F.
  - `[low]` `[patch]` (edge) Part A's "every eFuse key block unused" conflicts with Part D's "at least one unused USER block" — fixed: Part A says "never flashed with Coldframe (Part D checks the key blocks)".
  - `[low]` `[patch]` (edge) the issue form requires a release tag even when stuck before one is known — fixed: not required, with "Write none if you have no tag yet".
  - `[low]` `[patch]` (edge) template and results README disagree on the author id — same group as the author-id row; fixed there.
  - `[low]` `[defer]` (verification-gap) section anchors in the checklist are never checked — same group as the anchors row; deferred (DW entry in frontmatter).
  - `[low]` `[reject]` (verification-gap other) the issue form and its A–H dropdown are not checked by any CI — GitHub validates the form when it renders the chooser; a CI check would be new surface for a list that changes only with the checklist's part headings.
  - `[false]` `[reject]` (intent) the work is uncommitted, the spec still `in-review` and lacks `operator_actions:` — the audit ran before finalization; this step sets `awaiting-operator`, the list and the commit.

## Design Notes

**Why the checklist does not copy commands:** the guides are the thing under test. A checklist that repeats their commands would let a stale guide pass. Each part says "follow X" and then lists what you must observe.

**Why the run is an operator action:** the AC requires a physical build, real accounts (domain, Apple, Firebase, SMTP, S3), a person other than the author and the first release tag. An agent can only prepare the instrument.

## Verification

**Commands:**
- `docs/check-links.sh` -- expected: every link and path resolves, including `docs/reproduce/**`
- `docs/test.sh` -- expected: all cases pass
- `docs/check-commands.sh --list` -- expected: OK (no new unmarked blocks in the default files)
- `python3 -c 'import yaml,sys; yaml.safe_load(open(".github/ISSUE_TEMPLATE/reproduction-stuck-point.yml"))'` -- expected: parses
- `actionlint` is not relevant (no workflow change)

**Manual checks (if no CLI):**
- Every fact listed under Always matches the cited guide lines.

## Auto Run Result

Status: awaiting-operator

**Summary.** The agent-doable part of story 10-5 is done. A manual reproduction checklist (`docs/reproduce/README.md`, parts A–H) walks the build guide and the deploy guide end to end, from the parts to a Threshold Alert inside the Notification Window. It links each guide section instead of copying commands, and states entry conditions, a clean-machine attestation, the stuck-point procedure, rerun rules and the pass rule. A result template and a results folder hold the committed record. A GitHub issue form files stuck points. README, the build guide and the deploy guide link the checklist. The reproduction run itself, the issues it raises, their fixes and the committed record need a human, real hardware, real accounts and a release tag, so they are listed under `operator_actions`.

**Files changed.**
- `docs/reproduce/README.md` -- the reproduction checklist (new).
- `docs/reproduce/results/TEMPLATE.md` -- the copy-me result record (new).
- `docs/reproduce/results/README.md` -- naming, edit rules and when SM-5 holds (new).
- `.github/ISSUE_TEMPLATE/reproduction-stuck-point.yml` -- the "Reproduction stuck point" issue form (new).
- `README.md` -- "Reproduce Coldframe" bullet under Start here.
- `docs/build/README.md`, `docs/deploy/README.md` -- one pointer each to the checklist.

**Review findings.** 23 findings: high 0, medium 4, low 17, false 2.
- **Patches applied (17 rows in 13 entries):** verdict options and edit rules, rerun and code-fix path, Node identity check, realm and bootstrap-admin checks, optional push Secret, Part H recovery, Part A applicability, board and reused-account wording, full-asset checksum check, author id, issue form release/environment fields.
- **Deferred (1 entry):** `#anchor` links are not checked by `docs/check-links.sh` (pre-existing Story 10.2 behaviour).
- **Rejected:** pre-existing tags lacking the checklist (no tag exists yet); internal IDs (false: SM-5 is explained, H1 matches the bench checklists); no CI for the issue form (GitHub validates it; new surface); uncommitted bookkeeping (false: finalization does it).

**Follow-up review recommended: true.** Two medium entries were patched on this first pass (patched: high 0, medium 2 entries, low 11 entries). The rewritten rules for pending verdicts, reruns from `main` and code fixes that need a new tag have not been read by an independent reviewer.

**Verification.**
- `docs/check-links.sh`: 876 links and paths in 38 files resolve.
- `docs/test.sh`: 50 passed, 0 failed.
- `docs/check-commands.sh --list`: 20 blocks in 12 files pass (no new fenced shell blocks).
- The issue form YAML parses.
- By hand: the cited log lines (`flash.md` 195-226), release assets and the verify step (`release.md`), the optional `coldframe-push` Secret, `prepare-realm.sh`, and the Alert facts (`push-checklist.md` Part D) match the checklist. The reviewer layers resolved every anchor by script.

**Residual risks.**
- No release tag exists yet, so parts B and D cannot pass until story 10-1's release is cut.
- Part G's app wording ("the Hub added", "no longer asks for Calibration") is read from app behaviour, not a guide, and may need tightening after the first run.
- The issue form is seen live only once it is on the default branch.
