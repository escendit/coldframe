#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# ///
"""Unit tests for session_state.py. Run: uv run scripts/tests/test-session_state.py"""

import json
import subprocess
import sys
import tempfile
from pathlib import Path

SCRIPT = Path(__file__).resolve().parent.parent / "session_state.py"


def run(*args):
    result = subprocess.run(
        [sys.executable, str(SCRIPT), *args], capture_output=True, text=True
    )
    return result


def parse_stdout(result):
    return json.loads(result.stdout.strip().splitlines()[-1])


def test_init_creates_default_schema(tmp_path):
    state_file = tmp_path / "session-state.json"
    result = run("init", "--path", str(state_file))
    assert result.returncode == 0, result.stderr
    payload = parse_stdout(result)
    assert payload["ok"] is True
    assert payload["created"] is True
    data = json.loads(state_file.read_text())
    assert data["current_task"] == {"id": None, "title": None, "step": None, "github_issue": None, "branch": None, "pr_number": None}
    assert data["issue_tasks"] == []


def test_init_is_idempotent(tmp_path):
    state_file = tmp_path / "session-state.json"
    run("init", "--path", str(state_file))
    result = run("init", "--path", str(state_file))
    payload = parse_stdout(result)
    assert payload["created"] is False


def test_set_and_get_dotted_path(tmp_path):
    state_file = tmp_path / "session-state.json"
    run("init", "--path", str(state_file))
    result = run("set", "--path", str(state_file), "current_task.id", '"T-1"')
    assert result.returncode == 0, result.stderr
    result = run("get", "--path", str(state_file), "current_task.id")
    payload = parse_stdout(result)
    assert payload["value"] == "T-1"


def test_set_unknown_top_level_field_fails(tmp_path):
    state_file = tmp_path / "session-state.json"
    run("init", "--path", str(state_file))
    result = run("set", "--path", str(state_file), "bogus.field", '"x"')
    assert result.returncode == 1
    payload = json.loads(result.stderr.strip())
    assert payload["ok"] is False
    assert "bogus" in payload["error"]


def test_add_and_close_issue_task(tmp_path):
    state_file = tmp_path / "session-state.json"
    run("init", "--path", str(state_file))
    result = run(
        "add-issue-task",
        "--path", str(state_file),
        "--issue-number", "7",
        "--task-id", "T-99",
        "--time-log-id", "TL-1",
    )
    assert result.returncode == 0, result.stderr
    result = run("add-issue-task", "--path", str(state_file), "--issue-number", "7", "--task-id", "T-99")
    assert result.returncode == 1

    result = run("close-issue-task", "--path", str(state_file), "--issue-number", "7")
    payload = parse_stdout(result)
    assert payload["entry"]["status"] == "closed"

    result = run("close-issue-task", "--path", str(state_file), "--issue-number", "999")
    assert result.returncode == 1


def test_clear_current_task_resets_current_task_only(tmp_path):
    state_file = tmp_path / "session-state.json"
    run("init", "--path", str(state_file))
    run("set", "--path", str(state_file), "current_task.id", '"T-1"')
    run("open-time-log", "--path", str(state_file), "--task-id", "T-99", "--time-log-id", "TL-1", "--started-at", "9:00 AM")
    result = run("clear-current-task", "--path", str(state_file))
    payload = parse_stdout(result)
    assert payload["current_task"]["id"] is None
    data = json.loads(state_file.read_text())
    assert "T-99" in data["open_time_logs"]


def test_open_and_close_time_log_keyed_by_task(tmp_path):
    state_file = tmp_path / "session-state.json"
    run("init", "--path", str(state_file))
    result = run("open-time-log", "--path", str(state_file), "--task-id", "T-1", "--time-log-id", "TL-1", "--started-at", "9:00 AM")
    assert result.returncode == 0, result.stderr

    # A second, unrelated task can have its own concurrent open session.
    result = run("open-time-log", "--path", str(state_file), "--task-id", "T-2", "--time-log-id", "TL-2", "--started-at", "9:05 AM")
    assert result.returncode == 0, result.stderr

    # Opening a second session on the same task fails rather than silently overwriting.
    result = run("open-time-log", "--path", str(state_file), "--task-id", "T-1", "--time-log-id", "TL-3", "--started-at", "9:10 AM")
    assert result.returncode == 1

    result = run("close-time-log", "--path", str(state_file), "--task-id", "T-1")
    assert result.returncode == 0, result.stderr
    data = json.loads(state_file.read_text())
    assert "T-1" not in data["open_time_logs"]
    assert "T-2" in data["open_time_logs"]  # closing T-1's session must not touch T-2's

    result = run("close-time-log", "--path", str(state_file), "--task-id", "T-1")
    assert result.returncode == 1


def main():
    tests = [obj for name, obj in list(globals().items()) if name.startswith("test_")]
    failures = 0
    for test in tests:
        with tempfile.TemporaryDirectory() as tmp:
            try:
                test(Path(tmp))
                print(f"PASS {test.__name__}")
            except AssertionError as exc:
                failures += 1
                print(f"FAIL {test.__name__}: {exc}")
    if failures:
        print(f"{failures} failure(s)")
        return 1
    print("All tests passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
