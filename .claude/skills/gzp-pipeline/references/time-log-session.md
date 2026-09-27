# Time-log sessions

This whole file applies only when `gzp_zoho_workflow` is enabled — with it off, no time-log sessions exist and nothing here runs.

Zoho's time-log tools have no live start/stop timer — `add_time_log` only accepts a *finished* entry (`hours`, or a `start_time` **and** `end_time` together). A "session" is simulated with two calls bracketing the work.

`open_time_logs` in `session-state` is keyed by task id, not a single slot — the primary task and an in-flight issue-task (`references/issue-report.md`) can each have their own open session at once, so always pass the exact `task_id` you're acting on.

**Start.** The moment work begins on a task: get the wall-clock time (`date +"%I:%M %p"`), call `add_time_log` for the task with `date` = today, `start_time` = that time, `end_time` = that same time (a zero-length placeholder), `bill_status` = `gzp_default_bill_status` unless the task clearly calls for something else, and a note like "Session started." Record the returned time-log `id`:

```
uv run scripts/session_state.py open-time-log --path {project-root}/_bmad/memory/gzp-pipeline/session-state.json --task-id <task-id> --time-log-id <returned-id> --started-at "<time>"
```

**Stop.** When the segment ends: get the wall-clock time again, call `update_single_time_log` on that same `timelog_id` setting `end_time` to the new time, and append a one-line summary of what was actually done to `notes`/`extra_data.notes` — never leave it blank. Then close the session:

```
uv run scripts/session_state.py close-time-log --path {project-root}/_bmad/memory/gzp-pipeline/session-state.json --task-id <task-id>
```

**Never compute or type in a duration yourself** — Zoho derives it from `start_time`/`end_time`.

A task can have several session pairs over its life (planning, implementation, post-review follow-up, a subagent dispatch) — always start a fresh pair when work resumes rather than reusing or extending a closed entry. If `open-time-log` for a task id fails because one is already open, that's a dangling session from an interrupted run — close it first (best-effort end time, noted as "recovered after interruption") before opening the new one.
