# Start a new phase

Every phase gets its own Zoho task list — never reuse a prior phase's list or drop tasks into the project's default/general list.

With `gzp_zoho_workflow` off, the Zoho half of this file is skipped — no task list, no Planning task, no time-log session (steps 2–4, 8–9, and step 5's `create_a_task` calls). What remains: agree the scope (step 1), apply step 5's decomposition thinking (one logical, independently reviewable unit per item, code-touching decided up front), write the breakdown straight into the `TodoWrite` list (step 6 — which is then the source of truth, not a mirror), and confirm (step 7). The re-evaluation paragraph below likewise loses its Planning-session mechanics: just refresh the `TodoWrite` list when scope changes.

1. Agree the phase's name and rough scope with the user — a short natural-language check is fine unless there's a genuine fork in how to split it, in which case use `AskUserQuestion`.
2. **Create the task list** (`create_task_list`, named after the phase, e.g. "Phase 3: Webhook delivery retries"). Write its id/name to `session-state.current_task_list`.
3. **Create a standing Planning task** inside the new list — not one of the worked items — that the breakdown and any cross-task planning gets logged against. Assign it to yourself (`owners_and_work.owners` = `resolved.zoho_user`, resolved in `SKILL.md`'s activation step).
4. **Start a time-log session** on the Planning task (`references/time-log-session.md`).
5. **Decide, per planned item, whether it touches code.** Break the phase into individual tasks (`create_a_task`, each self-assigned the same way as the Planning task) — one task = one logical, independently reviewable unit. Prefer more, smaller tasks. A task that's pure brainstorming/planning/testing-only is still a task; it just won't enter the code branch of `references/task-loop.md` later.
6. **Mirror the breakdown into a local todo list** (`TodoWrite`) — one item per task, worded from the Zoho task title, all starting `pending`. This is a same-session visibility aid alongside Zoho, not a replacement for it: `session-state`/Zoho stay the source of truth across resets, the todo list just makes in-flight progress visible for this conversation. Re-run this step (fresh full list) whenever the breakdown changes.
7. Surface the resulting list as a short numbered summary and confirm which task to start on (or take the first one if the user said "just go").
8. **Stop the Planning session** with a note summarizing the breakdown (e.g. "Split into N tasks, starting with X").
9. **Close the Planning task.** It's a standing task for the evaluation work, and that work is finished the moment the tasks below it exist — don't leave it open indefinitely.

If the phase's scope needs re-evaluation later (a task grows, a new task surfaces), start a fresh Planning session (a new start/stop pair) for that re-evaluation rather than leaving it untimed, close the Planning task again afterward, and refresh the `TodoWrite` mirror to match.

Once the breakdown is confirmed, hand off to `references/task-loop.md` for the first task.
