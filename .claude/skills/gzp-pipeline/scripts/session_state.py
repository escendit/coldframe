#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# ///
"""Read/write the gzp-pipeline session-state file.

The session-state file is the structured working artifact that lets
gzp-pipeline recover its exact pipeline position (current task list,
current task, open time-log session, in-flight issue-tasks) after a
context reset or a fresh session, instead of re-deriving it from
conversation history.

Schema (all keys always present; unset scalars are null):

{
  "resolved": {
    "zoho_mcp_name": str|null,
    "github_tool": "gh"|"mcp"|null,
    "portal_id": str|null,
    "zoho_project_id": str|null,
    "zoho_project_name": str|null,
    "status_option_ids": {<status label>: <option id>, ...},
    "repo_owner": str|null,
    "repo_name": str|null,
    "zoho_user": {"zuid": int, "zpuid": str, "name": str}|null
  },
  "current_task_list": {"id": str|null, "name": str|null},
  "current_task": {"id": str|null, "title": str|null, "step": str|null, "github_issue": int|null, "branch": str|null, "pr_number": int|null},
  "open_time_logs": {<task_id>: {"time_log_id": str, "started_at": str}, ...},
  "issue_tasks": [
    {"issue_number": int, "task_id": str, "time_log_id": str|null, "status": "open"|"closed"}
  ]
}

open_time_logs is keyed by task_id, not a single slot — the primary task and
any in-flight issue-task can each have their own open session at once (see
references/issue-report.md), and closing one must never touch another's.

Commands:
  init                 Create the file with the default schema if it does not
                        exist (idempotent — a no-op if it already exists).
  show                 Print the full current state.
  get <path>           Print the value at a dotted path (e.g. "current_task.step").
  set <path> <value>   Set the value at a dotted path. <value> is parsed as
                        JSON when possible (numbers, booleans, null, objects,
                        arrays), otherwise stored as a raw string.
  open-time-log         Record a newly-started time-log session for a task_id.
  close-time-log         Remove a task_id's open time-log session (it has been stopped).
  add-issue-task        Append a new entry to issue_tasks.
  close-issue-task      Set an issue_tasks entry's status to "closed".
  clear-current-task    Reset current_task to its empty defaults (does not
                         touch open_time_logs — stop the task's own session
                         first, per references/time-log-session.md).

Every command takes --path (required) pointing at the session-state JSON
file. Writes are atomic (temp file + rename) so an interrupted run never
leaves a half-written file.
"""

import argparse
import json
import os
import sys
from pathlib import Path

DEFAULT_STATE = {
    "resolved": {
        "zoho_mcp_name": None,
        "github_tool": None,
        "portal_id": None,
        "zoho_project_id": None,
        "zoho_project_name": None,
        "status_option_ids": {},
        "repo_owner": None,
        "repo_name": None,
        "zoho_user": None,
    },
    "current_task_list": {"id": None, "name": None},
    "current_task": {"id": None, "title": None, "step": None, "github_issue": None, "branch": None, "pr_number": None},
    "open_time_logs": {},
    "issue_tasks": [],
}

TOP_LEVEL_KEYS = list(DEFAULT_STATE.keys())


class StateError(Exception):
    """A validation or lookup failure with a JSON-serializable payload."""

    def __init__(self, payload: dict):
        super().__init__(payload.get("error", ""))
        self.payload = payload


def load_state(path: Path) -> dict:
    if not path.exists():
        raise StateError({"ok": False, "error": f"session-state file not found at {path}; run 'init' first"})
    return json.loads(path.read_text())


def write_state(path: Path, state: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(json.dumps(state, indent=2) + "\n")
    os.replace(tmp, path)


def resolve_path(state: dict, dotted: str, create: bool = False):
    """Walk a dotted path, returning (parent_container, final_key)."""
    parts = dotted.split(".")
    top = parts[0]
    if top not in TOP_LEVEL_KEYS:
        raise StateError(
            {"ok": False, "error": f"field '{top}' not found — available top-level fields: {TOP_LEVEL_KEYS}"}
        )
    node = state
    for part in parts[:-1]:
        if not isinstance(node, dict) or part not in node:
            if create:
                node[part] = {}
            else:
                raise StateError({"ok": False, "error": f"path '{dotted}' does not exist under '{part}'"})
        node = node[part]
    return node, parts[-1]


def cmd_init(args) -> int:
    path = Path(args.path)
    if path.exists():
        print(json.dumps({"ok": True, "created": False, "path": str(path)}))
        return 0
    write_state(path, json.loads(json.dumps(DEFAULT_STATE)))
    print(json.dumps({"ok": True, "created": True, "path": str(path)}))
    return 0


def cmd_show(args) -> int:
    state = load_state(Path(args.path))
    print(json.dumps({"ok": True, "state": state}))
    return 0


def cmd_get(args) -> int:
    state = load_state(Path(args.path))
    parent, key = resolve_path(state, args.field_path)
    if not isinstance(parent, dict) or key not in parent:
        raise StateError({"ok": False, "error": f"path '{args.field_path}' does not exist under '{key}'"})
    print(json.dumps({"ok": True, "path": args.field_path, "value": parent[key]}))
    return 0


def cmd_set(args) -> int:
    path = Path(args.path)
    state = load_state(path)
    parent, key = resolve_path(state, args.field_path, create=True)
    try:
        value = json.loads(args.value)
    except json.JSONDecodeError:
        value = args.value
    parent[key] = value
    write_state(path, state)
    print(json.dumps({"ok": True, "path": args.field_path, "value": value}))
    return 0


def cmd_open_time_log(args) -> int:
    path = Path(args.path)
    state = load_state(path)
    if args.task_id in state["open_time_logs"]:
        raise StateError(
            {"ok": False, "error": f"task_id '{args.task_id}' already has an open time-log session"}
        )
    entry = {"time_log_id": args.time_log_id, "started_at": args.started_at}
    state["open_time_logs"][args.task_id] = entry
    write_state(path, state)
    print(json.dumps({"ok": True, "task_id": args.task_id, "entry": entry}))
    return 0


def cmd_close_time_log(args) -> int:
    path = Path(args.path)
    state = load_state(path)
    if args.task_id not in state["open_time_logs"]:
        raise StateError({"ok": False, "error": f"task_id '{args.task_id}' has no open time-log session"})
    entry = state["open_time_logs"].pop(args.task_id)
    write_state(path, state)
    print(json.dumps({"ok": True, "task_id": args.task_id, "closed_entry": entry}))
    return 0


def cmd_add_issue_task(args) -> int:
    path = Path(args.path)
    state = load_state(path)
    for entry in state["issue_tasks"]:
        if entry["issue_number"] == args.issue_number:
            raise StateError({"ok": False, "error": f"issue_number {args.issue_number} already tracked"})
    entry = {
        "issue_number": args.issue_number,
        "task_id": args.task_id,
        "time_log_id": args.time_log_id,
        "status": "open",
    }
    state["issue_tasks"].append(entry)
    write_state(path, state)
    print(json.dumps({"ok": True, "entry": entry}))
    return 0


def cmd_close_issue_task(args) -> int:
    path = Path(args.path)
    state = load_state(path)
    for entry in state["issue_tasks"]:
        if entry["issue_number"] == args.issue_number:
            entry["status"] = "closed"
            write_state(path, state)
            print(json.dumps({"ok": True, "entry": entry}))
            return 0
    raise StateError({"ok": False, "error": f"issue_number {args.issue_number} not tracked"})


def cmd_clear_current_task(args) -> int:
    path = Path(args.path)
    state = load_state(path)
    state["current_task"] = json.loads(json.dumps(DEFAULT_STATE["current_task"]))
    write_state(path, state)
    print(json.dumps({"ok": True, "current_task": state["current_task"]}))
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Read/write the gzp-pipeline session-state file.")
    sub = parser.add_subparsers(dest="command", required=True)

    p_init = sub.add_parser("init", help="Create the file with default schema if missing (idempotent)")
    p_init.add_argument("--path", required=True)
    p_init.set_defaults(func=cmd_init)

    p_show = sub.add_parser("show", help="Print the full current state")
    p_show.add_argument("--path", required=True)
    p_show.set_defaults(func=cmd_show)

    p_get = sub.add_parser("get", help="Print the value at a dotted path")
    p_get.add_argument("--path", required=True)
    p_get.add_argument("field_path")
    p_get.set_defaults(func=cmd_get)

    p_set = sub.add_parser("set", help="Set the value at a dotted path (JSON-parsed when possible)")
    p_set.add_argument("--path", required=True)
    p_set.add_argument("field_path")
    p_set.add_argument("value")
    p_set.set_defaults(func=cmd_set)

    p_open = sub.add_parser("open-time-log", help="Record a newly-started time-log session for a task_id")
    p_open.add_argument("--path", required=True)
    p_open.add_argument("--task-id", required=True)
    p_open.add_argument("--time-log-id", required=True)
    p_open.add_argument("--started-at", required=True)
    p_open.set_defaults(func=cmd_open_time_log)

    p_closelog = sub.add_parser("close-time-log", help="Remove a task_id's open time-log session")
    p_closelog.add_argument("--path", required=True)
    p_closelog.add_argument("--task-id", required=True)
    p_closelog.set_defaults(func=cmd_close_time_log)

    p_add = sub.add_parser("add-issue-task", help="Append a new entry to issue_tasks")
    p_add.add_argument("--path", required=True)
    p_add.add_argument("--issue-number", type=int, required=True)
    p_add.add_argument("--task-id", required=True)
    p_add.add_argument("--time-log-id")
    p_add.set_defaults(func=cmd_add_issue_task)

    p_close = sub.add_parser("close-issue-task", help="Set an issue_tasks entry's status to closed")
    p_close.add_argument("--path", required=True)
    p_close.add_argument("--issue-number", type=int, required=True)
    p_close.set_defaults(func=cmd_close_issue_task)

    p_clear = sub.add_parser("clear-current-task", help="Reset current_task to defaults")
    p_clear.add_argument("--path", required=True)
    p_clear.set_defaults(func=cmd_clear_current_task)

    args = parser.parse_args()
    try:
        return args.func(args)
    except StateError as exc:
        print(json.dumps(exc.payload), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
