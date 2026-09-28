#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["tomlkit"]
# ///
"""Unit tests for write-build-hook.py. Run: uv run scripts/tests/test-write-build-hook.py"""

import json
import subprocess
import sys
import tempfile
from pathlib import Path

import tomlkit

SCRIPT = Path(__file__).resolve().parent.parent / "write-build-hook.py"


def run(*args):
    return subprocess.run([sys.executable, str(SCRIPT), *args], capture_output=True, text=True)


def parse_stdout(result):
    return json.loads(result.stdout)


def installed_skill(tmp_path, name):
    customize = tmp_path / "skills" / name / "customize.toml"
    customize.parent.mkdir(parents=True)
    customize.write_text("[workflow]\n")
    return customize


def load(target):
    return tomlkit.parse(target.read_text())["workflow"]


def test_enable_interactive_writes_both_fields(tmp_path):
    customize = installed_skill(tmp_path, "bmad-build")
    target = tmp_path / "custom" / "bmad-build.toml"
    result = run("--target", str(target), "--customize-toml", str(customize))
    assert result.returncode == 0, result.stderr
    payload = parse_stdout(result)
    assert payload["status"] == "success"
    assert payload["mode"] == "interactive"
    workflow = load(target)
    assert len(workflow["activation_steps_prepend"]) == 1
    assert "unattended" not in workflow["activation_steps_prepend"][0]
    assert workflow["on_complete"].startswith("Invoke the gzp-pipeline skill to hand off completion")


def test_legacy_flag_name_still_accepted(tmp_path):
    customize = installed_skill(tmp_path, "bmad-build")
    target = tmp_path / "custom" / "bmad-build.toml"
    result = run("--target", str(target), "--bmad-build-customize-toml", str(customize))
    assert result.returncode == 0, result.stderr
    assert parse_stdout(result)["status"] == "success"


def test_enable_unattended_writes_never_wait_variants(tmp_path):
    customize = installed_skill(tmp_path, "bmad-build-auto")
    target = tmp_path / "custom" / "bmad-build-auto.toml"
    result = run(
        "--target", str(target), "--customize-toml", str(customize),
        "--skill-name", "bmad-build-auto", "--unattended",
    )
    assert result.returncode == 0, result.stderr
    payload = parse_stdout(result)
    assert payload["status"] == "success"
    assert payload["mode"] == "unattended"
    assert payload["skill"] == "bmad-build-auto"
    workflow = load(target)
    assert "must not ask questions or wait for a human" in workflow["activation_steps_prepend"][0]
    assert "never waits for a human" in workflow["on_complete"]
    assert "accepts its own PR" in workflow["on_complete"]
    assert "never forces a merge" in workflow["on_complete"]


def test_rerun_replaces_rather_than_duplicates(tmp_path):
    customize = installed_skill(tmp_path, "bmad-build-auto")
    target = tmp_path / "custom" / "bmad-build-auto.toml"
    args = ("--target", str(target), "--customize-toml", str(customize), "--unattended")
    run(*args)
    result = run(*args)
    assert result.returncode == 0, result.stderr
    assert len(load(target)["activation_steps_prepend"]) == 1


def test_switching_mode_replaces_owned_entries(tmp_path):
    customize = installed_skill(tmp_path, "bmad-build-auto")
    target = tmp_path / "custom" / "bmad-build-auto.toml"
    run("--target", str(target), "--customize-toml", str(customize))
    run("--target", str(target), "--customize-toml", str(customize), "--unattended")
    workflow = load(target)
    assert len(workflow["activation_steps_prepend"]) == 1
    assert "unattended" in workflow["activation_steps_prepend"][0]
    assert "never waits for a human" in workflow["on_complete"]


def test_skipped_when_skill_not_installed(tmp_path):
    target = tmp_path / "custom" / "bmad-build-auto.toml"
    result = run(
        "--target", str(target), "--customize-toml", str(tmp_path / "missing" / "customize.toml"),
        "--skill-name", "bmad-build-auto", "--unattended",
    )
    assert result.returncode == 0, result.stderr
    payload = parse_stdout(result)
    assert payload["status"] == "skipped"
    assert "bmad-build-auto is not installed" in payload["reason"]
    assert not target.exists()


def test_foreign_entries_and_on_complete_are_preserved(tmp_path):
    customize = installed_skill(tmp_path, "bmad-build-auto")
    target = tmp_path / "custom" / "bmad-build-auto.toml"
    target.parent.mkdir(parents=True)
    target.write_text(
        '[workflow]\nactivation_steps_prepend = ["Someone else\'s step."]\non_complete = "My own completion step."\n'
    )
    result = run("--target", str(target), "--customize-toml", str(customize), "--unattended")
    assert result.returncode == 0, result.stderr
    assert parse_stdout(result)["status"] == "conflict"
    workflow = load(target)
    assert list(workflow["activation_steps_prepend"])[0] == "Someone else's step."
    assert len(workflow["activation_steps_prepend"]) == 2
    assert workflow["on_complete"] == "My own completion step."


def test_disable_removes_only_owned_entries(tmp_path):
    customize = installed_skill(tmp_path, "bmad-build-auto")
    target = tmp_path / "custom" / "bmad-build-auto.toml"
    target.parent.mkdir(parents=True)
    target.write_text('[workflow]\nactivation_steps_prepend = ["Someone else\'s step."]\n')
    run("--target", str(target), "--customize-toml", str(customize), "--unattended")
    result = run("--target", str(target), "--customize-toml", str(customize), "--action", "disable")
    assert result.returncode == 0, result.stderr
    assert parse_stdout(result)["changed"] is True
    workflow = load(target)
    assert list(workflow["activation_steps_prepend"]) == ["Someone else's step."]
    assert workflow["on_complete"] == ""


def test_unresolved_project_root_token_is_rejected(tmp_path):
    result = run("--target", "{project-root}/_bmad/custom/x.toml", "--customize-toml", "/nowhere")
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
