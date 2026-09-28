# Module Setup

Standalone module self-registration. This file is loaded when:
- The user passes `setup`, `configure`, or `install` as an argument
- The module is not yet registered in `{project-root}/_bmad/config.yaml`
- The skill's first-run init flow detects this is a fresh installation (e.g., agent memory doesn't exist yet)

## Overview

Registers this standalone module into a project. Module identity (name, code, version) comes from `./assets/module.yaml` (sibling to this file). Collects user preferences and writes them to three files:

- **`{project-root}/_bmad/config.yaml`** — shared project config: core settings at root (e.g. `output_folder`, `document_output_language`) plus a section per module with metadata and module-specific values. User-only keys (`user_name`, `communication_language`) are **never** written here.
- **`{project-root}/_bmad/config.user.yaml`** — personal settings intended to be gitignored: `user_name`, `communication_language`, and any module variable marked `user_setting: true` in `./assets/module.yaml`. These values live exclusively here.
- **`{project-root}/_bmad/module-help.csv`** — registers module capabilities for the help system.

Both config scripts use an anti-zombie pattern — existing entries for this module are removed before writing fresh ones, so stale values never persist.

`{project-root}` is a **literal token** in config _values_ (the data written into the files above) — never substitute it there. It signals to the consuming LLM that the value is relative to the project root, not the skill root. **This does not apply to the filesystem path _arguments_ passed to the scripts below** (the `--*-path`, `--*-dir`, and `--target` arguments): those are real paths, so you **must** resolve `{project-root}` to the actual project root before running, or the scripts will write to a literal `{project-root}/` directory under the skill folder. The scripts reject an unresolved token with an error.

## Check Existing Config

1. Read `./assets/module.yaml` for module metadata and variable definitions (the `code` field is the module identifier)
2. Check if `{project-root}/_bmad/config.yaml` exists — if a section matching the module's code is already present, inform the user this is an update (reconfiguration)

If the user provides arguments (e.g. `accept all defaults`, `--headless`, or inline values like `user name is BMad, I speak Swahili`), map any provided values to config keys, use defaults for the rest, and skip interactive prompting. Still display the full confirmation summary at the end.

## Collect Configuration

Ask the user for values. Show defaults in brackets. Present all values together so the user can respond once with only the values they want to change (e.g. "change language to Swahili, rest are fine"). Never tell the user to "press enter" or "leave blank" — in a chat interface they must type something to respond.

**Default priority** (highest wins): existing config values > per-module installer config, if present (`{project-root}/_bmad/{module-code}/config.yaml`, written by an external BMAD installer — read matching keys as defaults instead of re-asking) > `./assets/module.yaml` defaults.

### Core Config

Only collect if no core keys exist yet in `config.yaml` or `config.user.yaml`:

- `user_name` (default: BMad) — written exclusively to `config.user.yaml`
- `communication_language` and `document_output_language` (default: English — ask as a single language question, both keys get the same answer) — `communication_language` written exclusively to `config.user.yaml`
- `output_folder` (default: `{project-root}/_bmad-output`) — written to `config.yaml` at root, shared across all modules

### Module Config

Read each variable in `./assets/module.yaml` that has a `prompt` field. The module.yaml supports several question types:

- **Text input**: Has `prompt`, `default`, and optionally `result` (template), `required`, `regex`, `example` fields
- **Single-select**: Has a `single-select` array of `value`/`label` options — present as a choice list
- **Multi-select**: Has a `multi-select` array — present as checkboxes, default is an array
- **Confirm**: `default` is a boolean — present as Yes/No

Ask using the prompt with its default value. Apply `result` templates when storing (e.g. `{project-root}/{value}`). Fields with `user_setting: true` go exclusively to `config.user.yaml`.

**Conditional questions**: a variable with `asked_when: <other_field>` is asked only when the collected value of that field is truthy; when it isn't, don't prompt (even if `required: true`) — record the variable's default (or its existing/legacy value) silently. Ask questions in the order the variables appear in `module.yaml`, so gating fields come before the questions they gate.

## Write Files

Write a temp JSON file with the collected answers structured as `{"core": {...}, "module": {...}}` (omit `core` if it already exists). Values inside this JSON keep the literal `{project-root}` token. Then run both scripts — they can run in parallel since they write to different files.

In the commands below, replace `{project-root}` in every path argument with the actual project root (e.g. `/home/me/myapp`) before running — these are filesystem paths, not config values.

```bash
uv run ./scripts/merge-config.py --config-path "{project-root}/_bmad/config.yaml" --user-config-path "{project-root}/_bmad/config.user.yaml" --module-yaml ./assets/module.yaml --answers {temp-file}
uv run ./scripts/merge-help-csv.py --target "{project-root}/_bmad/module-help.csv" --source ./assets/module-help.csv --module-code {module-code}
```

Both scripts output JSON to stdout with results. If either exits non-zero, surface the error and stop.

Run `./scripts/merge-config.py --help` or `./scripts/merge-help-csv.py --help` for full usage.

## Auto-Track bmad-build (Optional)

If the collected `gzp_autotrack_bmad_build` answer is true, run:

```bash
uv run ./scripts/write-build-hook.py --target "{project-root}/_bmad/custom/bmad-build.toml" --bmad-build-customize-toml "{project-root}/.claude/skills/bmad-build/customize.toml" --action enable
```

(Resolve `{project-root}` to the actual project root, same as every other path argument above.)

The script itself checks whether bmad-build is installed and no-ops (`status: "skipped"`) if it isn't — safe to run unconditionally on `true`, including on a project that adds bmad-build later and re-runs `configure`. Report the script's `status` to the user:
- `"success"` — hook written, mention it in the confirmation summary.
- `"skipped"` — bmad-build isn't installed; say auto-tracking will apply automatically once it is (re-run `configure` after installing it, or just answer this question again).
- `"conflict"` — `activation_steps_prepend` got the gzp-pipeline entry, but `on_complete` in `_bmad/custom/bmad-build.toml` already holds a different custom instruction that wasn't overwritten. Show the user that existing `on_complete` value and ask whether to replace it manually or leave it (gzp-pipeline's completion handoff won't run automatically until it's resolved).

If the answer is false and a prior run of this script already wrote the hook (reconfiguration flipping the choice from yes to no), instead run the same command with `--action disable` to cleanly remove it.

### Auto-Track the unattended loop (bmad-build-auto)

`bmad-build-auto` — one iteration of the unattended development loop — reads its **own** override file, so the bmad-build hook above never reaches it. If the collected `gzp_autotrack_bmad_loop` answer is true, run the same script against the loop skill, in unattended mode:

```bash
uv run ./scripts/write-build-hook.py --target "{project-root}/_bmad/custom/bmad-build-auto.toml" --customize-toml "{project-root}/.claude/skills/bmad-build-auto/customize.toml" --skill-name bmad-build-auto --unattended --action enable
```

`--unattended` writes hook text that forbids asking questions or waiting on a human (see the unattended invariant in `SKILL.md`). Statuses, the `skipped`-when-not-installed behavior, the `conflict` handling, and `--action disable` on a yes→no flip all work exactly as for bmad-build. The two answers are independent: either hook can be on without the other.

`gzp_loop_auto_merge_pr` (asked only when the loop hook is on) needs no script run — it's read at the merge gate. Mention in the confirmation summary which way it was answered, since it decides whether loop iterations merge their own PRs or end with the PR open.

## Create Output Directories

After writing config, create any output directories that were configured. For filesystem operations only (such as creating directories), resolve the `{project-root}` token to the actual project root and create each path-type value from `config.yaml` that does not yet exist — this includes `output_folder` and any module variable whose value starts with `{project-root}/`. The paths stored in the config files must continue to use the literal `{project-root}` token; only the directories on disk should use the resolved paths. Use `mkdir -p` or equivalent to create the full path.

If `./assets/module.yaml` contains a `directories` array, also create each listed directory (resolving any `{field_name}` variables from the collected config values).

## Confirm

Use the script JSON output to display what was written — config values set (written to `config.yaml` at root for core, module section for module values), user settings written to `config.user.yaml` (`user_keys` in result), help entries added, fresh install vs update.

If `./assets/module.yaml` contains `post-install-notes`, display them (if conditional, show only the notes matching the user's selected config values).

Then display the `module_greeting` from `./assets/module.yaml` to the user.

## Return to Skill

Setup is complete. Resume the main skill's normal activation flow — load config from the freshly written files and proceed with whatever the user originally intended.
