#!/usr/bin/env python3
# /// script
# requires-python = ">=3.9"
# dependencies = ["tomlkit"]
# ///
"""Enable or disable the gzp-pipeline auto-track hook in a BMad build skill's customize override.

Targets _bmad/custom/bmad-build.toml by default; pass --skill-name bmad-build-auto
(with that skill's --target/--customize-toml paths and --unattended) to hook the
unattended development loop instead. The two skills read separate override files,
so each needs its own hook.

Writes two fields into the [workflow] table of the customize override:
- activation_steps_prepend: an entry telling Build to hand task/time tracking to
  gzp-pipeline before its own steps run.
- on_complete: an instruction telling Build to hand completion (GitHub Flow, Zoho
  status, time-log close) to gzp-pipeline.

Uses tomlkit so any existing file (comments, unrelated overrides, formatting) is
preserved and only round-trips through a parse/dump. Anti-zombie for our own
entries: a prior gzp-pipeline-authored activation_steps_prepend line is replaced,
not duplicated, on repeat runs.

If on_complete already holds content that isn't ours (a real user override),
the write is skipped for that field and reported as a conflict rather than
clobbering it — activation_steps_prepend still gets our entry since it's an
append-only list.

Exit codes: 0=success (including no-op skip/disable), 1=validation error, 2=runtime error
"""

import argparse
import json
import sys
from pathlib import Path

try:
    import tomlkit
except ImportError:
    print("Error: tomlkit is required (PEP 723 dependency)", file=sys.stderr)
    sys.exit(2)

PREPEND_ENTRY = (
    "Invoke the gzp-pipeline skill before any other work: gzp-pipeline is the sole owner of "
    "Zoho task status and time-log sessions. Let it resume in-flight state or start tracking "
    "for this build (setting the relevant Zoho task 'In Progress' and opening a time-log "
    "session) before Build proceeds to its own steps. Do not duplicate task/time tracking here."
)

ON_COMPLETE_TEXT = (
    "Invoke the gzp-pipeline skill to hand off completion of this build's work: it drives "
    "code-touching changes through GitHub Flow (branch, commit, push, PR — self-assigned, "
    "labeled) if that has not already happened, updates the Zoho task's status, and closes "
    "out the time-log session opened at activation. Do not commit, push, open a PR, or touch "
    "Zoho directly from Build's own steps — gzp-pipeline owns that lifecycle end to end.\n"
)

# Unattended variants (bmad-build-auto): same ownership prefixes, plus the rule that
# nothing in the handoff may wait on a human.
UNATTENDED_PREPEND_ENTRY = (
    "Invoke the gzp-pipeline skill before any other work: gzp-pipeline is the sole owner of "
    "Zoho task status and time-log sessions. Let it resume in-flight state or start tracking "
    "for this iteration before this workflow proceeds to its own steps. This run is "
    "unattended: gzp-pipeline must not ask questions or wait for a human — if it cannot "
    "proceed (required config unresolved, or a prior task's PR still awaiting merge), it "
    "reports the blocker and this workflow must HALT with status blocked, naming that blocker. "
    "Do not duplicate task/time tracking here."
)

UNATTENDED_ON_COMPLETE_TEXT = (
    "Invoke the gzp-pipeline skill to hand off completion of this iteration's work: it drives "
    "code-touching changes through GitHub Flow (tracking issue when configured, branch, commit, "
    "push, PR — self-assigned, labeled) if that has not already happened, updates the Zoho "
    "task's status, and closes out the time-log session opened at activation. This run is "
    "unattended: at the merge gate gzp-pipeline accepts its own PR — it merges once checks "
    "are green (loop auto-merge, on by default) — and when the merge is blocked or loop "
    "auto-merge is off it leaves the PR open and returns; it never waits for a human and "
    "never forces a merge. Do not commit, "
    "push, open a PR, or touch Zoho directly from this workflow's own steps — gzp-pipeline "
    "owns that lifecycle end to end.\n"
)

# Marker prefix identifying entries this script owns, so re-runs replace rather
# than duplicate, and disable can find what to remove.
_OWNED_PREPEND_PREFIX = "Invoke the gzp-pipeline skill before any other work:"
_OWNED_ON_COMPLETE_PREFIX = "Invoke the gzp-pipeline skill to hand off completion"


def parse_args():
    parser = argparse.ArgumentParser(
        description="Enable/disable the gzp-pipeline hook in a BMad build skill's customize override."
    )
    parser.add_argument(
        "--target",
        required=True,
        help="Path to the override file, e.g. _bmad/custom/bmad-build.toml or _bmad/custom/bmad-build-auto.toml",
    )
    parser.add_argument(
        "--customize-toml",
        "--bmad-build-customize-toml",
        dest="customize_toml",
        required=True,
        help="Path to the installed target skill's customize.toml — existence gates the write.",
    )
    parser.add_argument(
        "--skill-name",
        default="bmad-build",
        help="Name of the skill being hooked, used in messages (default: bmad-build).",
    )
    parser.add_argument(
        "--unattended",
        action="store_true",
        help="Write the unattended hook variants (for bmad-build-auto): never ask, never wait on a human.",
    )
    parser.add_argument(
        "--action",
        choices=["enable", "disable"],
        default="enable",
        help="enable: write/refresh our hook entries. disable: remove them, leaving everything else intact.",
    )
    return parser.parse_args()


def reject_unresolved_paths(named_paths: list[tuple[str, str]]) -> None:
    for name, value in named_paths:
        if value and "{project-root}" in value:
            print(
                json.dumps(
                    {
                        "status": "error",
                        "error": (
                            f"Unresolved '{{project-root}}' token in {name} path: {value!r}. "
                            "Resolve '{project-root}' to the actual project root before running "
                            "this script — it is a filesystem path, not a config value."
                        ),
                    },
                    indent=2,
                ),
                file=sys.stderr,
            )
            sys.exit(1)


def main():
    args = parse_args()
    reject_unresolved_paths(
        [("--target", args.target), ("--customize-toml", args.customize_toml)]
    )

    target = Path(args.target)
    skill_present = Path(args.customize_toml).exists()
    prepend_entry = UNATTENDED_PREPEND_ENTRY if args.unattended else PREPEND_ENTRY
    on_complete_text = UNATTENDED_ON_COMPLETE_TEXT if args.unattended else ON_COMPLETE_TEXT

    if not skill_present:
        print(
            json.dumps(
                {
                    "status": "skipped",
                    "reason": f"{args.skill_name} is not installed in this project — nothing to hook.",
                    "skill": args.skill_name,
                    "target": str(target),
                },
                indent=2,
            )
        )
        return 0

    doc = tomlkit.parse(target.read_text(encoding="utf-8")) if target.exists() else tomlkit.document()

    if "workflow" not in doc:
        doc["workflow"] = tomlkit.table()
    workflow = doc["workflow"]

    on_complete_conflict = False

    if args.action == "enable":
        prepend = workflow.get("activation_steps_prepend")
        if prepend is None:
            prepend = tomlkit.array()
            prepend.multiline(True)
        else:
            # Anti-zombie: drop any prior run of ours, keep everyone else's entries.
            prepend = tomlkit.array(
                [str(v) for v in prepend if not str(v).startswith(_OWNED_PREPEND_PREFIX)]
            )
            prepend.multiline(True)
        prepend.append(prepend_entry)
        workflow["activation_steps_prepend"] = prepend

        existing_on_complete = str(workflow.get("on_complete", "")).strip()
        if not existing_on_complete or existing_on_complete.startswith(_OWNED_ON_COMPLETE_PREFIX):
            workflow["on_complete"] = on_complete_text
        else:
            on_complete_conflict = True

        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(tomlkit.dumps(doc), encoding="utf-8")

        print(
            json.dumps(
                {
                    "status": "conflict" if on_complete_conflict else "success",
                    "skill": args.skill_name,
                    "mode": "unattended" if args.unattended else "interactive",
                    "target": str(target.resolve()),
                    "activation_steps_prepend": "written",
                    "on_complete": "skipped (existing custom content preserved)"
                    if on_complete_conflict
                    else "written",
                },
                indent=2,
            )
        )
        return 0

    # action == disable
    if not target.exists():
        print(json.dumps({"status": "skipped", "reason": "no override file exists.", "target": str(target)}, indent=2))
        return 0

    changed = False
    prepend = workflow.get("activation_steps_prepend")
    if prepend is not None:
        filtered = [str(v) for v in prepend if not str(v).startswith(_OWNED_PREPEND_PREFIX)]
        if len(filtered) != len(prepend):
            changed = True
        new_arr = tomlkit.array(filtered)
        new_arr.multiline(True)
        workflow["activation_steps_prepend"] = new_arr

    existing_on_complete = str(workflow.get("on_complete", "")).strip()
    if existing_on_complete.startswith(_OWNED_ON_COMPLETE_PREFIX):
        workflow["on_complete"] = ""
        changed = True

    if changed:
        target.write_text(tomlkit.dumps(doc), encoding="utf-8")

    print(
        json.dumps(
            {"status": "success" if changed else "skipped", "target": str(target.resolve()), "changed": changed},
            indent=2,
        )
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
