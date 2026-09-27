#!/usr/bin/env python3
# /// script
# requires-python = ">=3.9"
# dependencies = ["tomlkit"]
# ///
"""Enable or disable the gzp-pipeline auto-track hook in _bmad/custom/bmad-build.toml.

Writes two fields into the [workflow] table of a bmad-build customize override:
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

# Marker prefix identifying entries this script owns, so re-runs replace rather
# than duplicate, and disable can find what to remove.
_OWNED_PREPEND_PREFIX = "Invoke the gzp-pipeline skill before any other work:"
_OWNED_ON_COMPLETE_PREFIX = "Invoke the gzp-pipeline skill to hand off completion"


def parse_args():
    parser = argparse.ArgumentParser(
        description="Enable/disable the gzp-pipeline hook in a bmad-build customize override."
    )
    parser.add_argument("--target", required=True, help="Path to _bmad/custom/bmad-build.toml")
    parser.add_argument(
        "--bmad-build-customize-toml",
        required=True,
        help="Path to the installed bmad-build skill's customize.toml — existence gates the write.",
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
        [("--target", args.target), ("--bmad-build-customize-toml", args.bmad_build_customize_toml)]
    )

    target = Path(args.target)
    bmad_build_present = Path(args.bmad_build_customize_toml).exists()

    if not bmad_build_present:
        print(
            json.dumps(
                {
                    "status": "skipped",
                    "reason": "bmad-build is not installed in this project — nothing to hook.",
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
        prepend.append(PREPEND_ENTRY)
        workflow["activation_steps_prepend"] = prepend

        existing_on_complete = str(workflow.get("on_complete", "")).strip()
        if not existing_on_complete or existing_on_complete.startswith(_OWNED_ON_COMPLETE_PREFIX):
            workflow["on_complete"] = ON_COMPLETE_TEXT
        else:
            on_complete_conflict = True

        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(tomlkit.dumps(doc), encoding="utf-8")

        print(
            json.dumps(
                {
                    "status": "conflict" if on_complete_conflict else "success",
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
