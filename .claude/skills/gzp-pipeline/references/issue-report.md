# Report an issue

Something went wrong mid-task that's worth tracking as its own reported problem. This is distinct from a task's *tracking issue* (the one `gzp_github_issue_first` opens as a mirror of the Zoho task, auto-closed by the PR merge) — a reported issue is a standalone artifact with its own linked Zoho task, and it exists regardless of that setting.

With `gzp_zoho_workflow` off, only step 1 runs: the GitHub issue *is* the complete record — skip the linked Zoho task, its time-log session, and the `add-issue-task` session-state entry entirely.

1. **Open the GitHub issue** describing what went wrong, with enough context (repo, branch/PR if relevant, what was expected vs. observed) that it's understandable without this conversation.
2. **Auto-create a linked, standalone Zoho task** for it — every issue gets one, no case-by-case judgment call:
   ```
   uv run scripts/session_state.py add-issue-task --path {project-root}/_bmad/memory/gzp-pipeline/session-state.json --issue-number <n> --task-id <zoho-task-id>
   ```
   Create the Zoho task first (`create_a_task`, in the current task list or a small maintenance list if none fits, self-assigned via `owners_and_work.owners` = `resolved.zoho_user` same as every other task this skill creates), reference the issue number in its description, then record it with the command above.
3. **Start a time-log session** on this new task's id (`references/time-log-session.md`) — `open_time_logs` is keyed by task id, so this session and whatever session is open on the originating task coexist without either overwriting the other. The originating task's `session-state.current_task` is untouched.
4. **Resolve the issue on its own schedule.** It may get fixed immediately (small ad hoc fix — still go through `references/task-loop.md`'s code path for the actual change) or sit open for later. Either way, the Zoho task tracks it, not the originating task.
5. **On resolution**, close the GitHub issue, stop the task's time-log session with a closing note, set the Zoho task status "Closed", and mark it resolved:
   ```
   uv run scripts/session_state.py close-issue-task --path {project-root}/_bmad/memory/gzp-pipeline/session-state.json --issue-number <n>
   ```

Resume behavior: on activation, any `issue_tasks` entry still `"status": "open"` in `session-state` is in-flight work the user may want surfaced alongside the primary task's status — mention it, don't silently drop it.
