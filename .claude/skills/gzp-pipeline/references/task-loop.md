# Work a task

Set `session-state.current_task` to the task being worked (id, title) before doing anything else, so an interruption mid-task is always recoverable. `STATE` below stands for `{project-root}/_bmad/memory/gzp-pipeline/session-state.json`; `<task-id>` is `current_task.id`. If a `TodoWrite` list from `references/planning-phase.md` is active for this phase, mark this task's item `in_progress` now too — and `completed` when the task closes at the end of either path below.

Every GitHub-side call below (create the tracking issue, open/assign/label the PR, read merge state and comments, merge) goes through the tool resolved at activation — `session-state.resolved.github_tool`: the MCP server named by `gzp_github_mcp_name` (preferred), or the `gh` CLI as fallback. The MCP tool names quoted here (`get_me`, `pull_request_read`, `merge_pull_request`) are that server's; with `gh`, use the equivalent subcommand.

**Config gates** (see Invariants in `SKILL.md` for the full statements):
- `gzp_zoho_workflow` off → skip the *Zoho half* of every step below (status changes, time-log sessions, task comments) — but always keep the `session_state.py` writes (`current_task.step`, `github_issue`, `branch`, `pr_number`): the resume flow depends on them regardless of the toggle. `current_task` then holds a short slug for the work item instead of a Zoho id, the tracking issue (when issue-first is on) is the work item of record, and progress lives in `session-state` + `TodoWrite`. With issue-first *also* off, the PR is the only durable record — recommend enabling issue-first alongside Zoho-off.
- `gzp_gpg_signing` off → skip the signature spot-check in step 6; commit with the repo's git config as-is.
- `gzp_auto_merge_pr` on → step 9 merges the PR itself instead of waiting.

## Decide the branch first

Does this task produce a code change? If no — pure brainstorming, planning, or testing-only work — skip straight to **Non-code path** below. Otherwise use **Code path**. This decision is made once per task, up front; don't re-litigate it mid-task.

## Code path

1. **Set status "In Progress"** on the Zoho task (using `session-state.resolved.status_option_ids`), and `uv run scripts/session_state.py set --path STATE current_task.step '"in_progress"'`.
2. **Start a time-log session** on `<task-id>` (`references/time-log-session.md`) covering the implementation segment.
3. **Open the tracking issue first — only when `gzp_github_issue_first` is true.** Before any branch exists, create a GitHub issue mirroring the Zoho task: title from the task's title, body linking the Zoho task (prefix + name) and summarizing the scope, self-assigned, with labels that fit the repo's existing label set. Record it with `uv run scripts/session_state.py set --path STATE current_task.github_issue <number>` so a resumed session knows the issue already exists (never create a second one). On resume with `github_issue` null, search open issues for the task title before creating — the prior session may have died between creating the issue and recording it. This issue is a mirror of the Zoho task, not a work item of its own — no separate time-log, no separate planning (see Invariants in `SKILL.md`). When the flag is false or unset, skip this step entirely.
4. **Implement the task's scope.** Create the branch, fetching and updating from the base branch first. The name is fully deterministic — `{type}/{zoho-id}-gh{issue}-{slug}` — so a resumed session can reconstruct it without guessing:
   - `{type}`: the conventional-commit type the task's commit will carry, decided once here and reused for the commit and PR title — `feat`, `fix`, `chore`, `docs`, `refactor`, `test`, `perf`, or `ci` (this is what sets the semver bump: `feat` → minor, `fix` → patch, a breaking change → major).
   - `{zoho-id}`: the Zoho task's prefix, lowercased (e.g. `ecm-t101`); omitted when `gzp_zoho_workflow` is off.
   - `gh{issue}`: the tracking issue number from step 3; omitted when no tracking issue exists.
   - `{slug}`: the task title in kebab-case, ASCII only, at most 40 characters, cut at a word boundary.
   - Segments are joined with `-`; there is always at least the slug. Examples: `feat/ecm-t101-gh5-generic-toggles`, `fix/ecm-t88-setup-installs-build-hook` (issue-first off), `feat/gh5-generic-toggles` (Zoho off), `chore/bump-deps` (both off). Record it with `set current_task.branch '"<name>"'`.

   Don't stash or discard unrelated uncommitted changes already in the tree — scope your own staging carefully.
   - **Always work from a local git clone**, even for a repo other than the current working directory (resolve via `session-state.resolved.repo_owner`/`repo_name`, or an explicit override when the user names a different repo for this task). Clone it locally rather than editing via GitHub API file-write tools (`create_or_update_file`, `push_files`) — those bypass local git entirely and the resulting commit won't carry the user's own GPG signature.
5. **Verify before staging.** Build/test the affected code. Stage explicitly by path (never a blind `git add -A`) and diff what's staged before committing.
6. **Commit, push, open the PR.** Conventional commit message. PR body: summary bullets and a Test plan checklist — only check `[x]` what actually ran. If a tracking issue was opened in step 3, the PR body must include `Closes #<issue>` so the merge closes it automatically.
   - When `gzp_gpg_signing` is enabled (the default): `git config commit.gpgsign` is expected already `true` locally — never override it, never pass `--no-gpg-sign`. After committing, spot-check `git log --show-signature -1` shows a good signature from the user's key, not just GitHub's own web-verification. If it doesn't, stop and ask rather than guessing or skipping the check. When disabled: commit as the repo is configured and skip the signature check.
   - Assign the PR to the user (resolve their GitHub identity via `get_me` once per session if not already known).
   - Attach labels that actually fit the repo's existing label set — don't invent new ones speculatively.
7. **Set status "In Review"** on the Zoho task, `set current_task.step '"pr_open_awaiting_merge"'`, and `set current_task.pr_number <number>` — the auto-merge gate in step 9 may only act on a PR recorded here.
8. **Stop the time-log session** from step 2, with a note summarizing what was implemented, the PR number, and the tracking issue number if one exists.
9. **Merge gate.**
   - **Default (`gzp_auto_merge_pr` off): wait. Do not proceed until the PR is confirmed merged.** When the user's message implies merge status ("continue", "next", "merged") or is otherwise ambiguous, verify yourself via `pull_request_read` rather than asking them to restate it. Only ask when the check itself is inconclusive (closed-not-merged, or the call fails).
   - **`gzp_auto_merge_pr` on:** merge the PR yourself now — only the PR recorded in `current_task.pr_number` (opened by this pipeline for this task). Through the resolved GitHub tool (`session-state.resolved.github_tool`): the GitHub MCP's `merge_pull_request` with an explicit merge method (pick the repo's default/allowed one; then delete the branch), or — on the `gh` fallback — `gh pr merge <n> --delete-branch` with an explicit method flag (`--squash`, `--merge`, or `--rebase`; a bare `gh pr merge` prompts interactively and must never be issued). Verify it reports merged, then continue to step 10 without waiting. Keep the step-6 self-assignment either way — it records authorship, not a review request. If the merge is blocked — required checks pending or failing, required reviews, conflicts — report the exact blocker and fall back to the waiting behavior above; never use `--admin` or any force-style override.
10. **Check for open feedback before treating the merge as done.** Read the PR's description-level comments and review comments. If there's actionable feedback not already addressed pre-merge:
    - Keep the task's status at "In Review"/"In Progress" — don't close it yet. Set `current_task.step` to `"addressing_review_feedback"`.
    - Start a new time-log session on `<task-id>` (a fresh pair — never reopen or extend the one from step 2/8).
    - Address the feedback, then stop that session with a note on what was addressed. Set `current_task.step` back to `"pr_open_awaiting_merge"`.
    - Repeat until there's no more open feedback.
    - Bot reviews (Sourcery, CodeRabbit, etc.) don't need a reply comment — verify the finding against the actual diff (bots produce false positives), record what was found/done in the time-log note, and only reply on the PR if the user explicitly asks.
11. **Close out.** If a tracking issue exists, verify the merge actually closed it (`Closes #` only fires on merges to the default branch) — close it manually with a link to the merged PR if it's still open. Add a final note (task comment and/or the last session's note) summarizing the work across all sessions and linking the merged PR(s) and tracking issue. **Set status "Closed."**

    A tracking issue must never outlive its task: if the PR is closed without merging, or the task is parked or abandoned, close the issue too (with a comment saying why) — or, when the task is parked with real intent to resume, leave it open but note the parked state on it.

## Non-code path

1. **Set status "In Progress"** and `set current_task.step '"in_progress"'`.
2. **Start a time-log session** on `<task-id>` covering the work (brainstorming, planning, testing, research — whatever the task actually is).
3. Do the work.
4. **Stop the session** with a note on what was produced/decided.
5. **Set status "Closed."** No branch, no issue, no PR — nothing GitHub-side happens for this task.

With `gzp_zoho_workflow` off, this path reduces to: do the work, mark the `TodoWrite` item completed, and summarize what was produced in conversation — there is nowhere else to record it, so the summary is not optional.

## After either path

Clear the current task: `uv run scripts/session_state.py clear-current-task --path STATE`. Then wait for new instructions — if more tasks are already broken out in the phase's list, either the user names the next one or you take the next open one and confirm. Only re-enter `references/planning-phase.md` when the user starts a genuinely new phase.

## Ad Hoc Requests

Not everything arrives as "start the next task from the list." A mid-session ask that doesn't fit an open task list — "merge these PRs", "fix Y real quick" — still needs a task and a time-log session *before* work starts, not skipped because it feels too small and not batched up as a retroactive log afterward. This is the exact failure mode the pipeline exists to prevent.

1. If an existing open task list is genuinely the right home for it, add the task there. Otherwise create a small task list for it (e.g. "Repo maintenance: <short description>") rather than skipping list creation because it's "just one thing." (With `gzp_zoho_workflow` off, skip the Zoho list/task and instead set `current_task` to a short slug and add a `TodoWrite` item before starting — the "track it before working it" rule survives the toggle.)
2. Create the task (self-assigned, same as any other — see Invariants in `SKILL.md`), start a time-log session on it, *then* start the work. Add it to the active `TodoWrite` mirror if one exists for this phase; otherwise a single-item list is fine.
3. Run it through the normal lifecycle above (code or non-code path, whichever fits). `gzp_github_issue_first` applies to ad hoc code tasks exactly as to planned ones — a one-line fix still gets its tracking issue when the flag is on.

If a session catches itself having skipped this, don't just apologize and move on — create the missed task(s) now, reconstruct the time as accurately as possible (commit timestamps, prior task's close time as an anchor), and note in the log that it's a retroactive entry.
