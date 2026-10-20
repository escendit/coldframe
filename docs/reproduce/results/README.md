# Reproduction results

This folder holds the result records of runs of the
[reproduction checklist](../README.md), one file per run. They are the evidence for SM-5:
someone other than the author (or the author on a clean machine with fresh hardware and no prior
state) reproduced Coldframe from the public docs alone.

## Adding a record

1. Copy [`TEMPLATE.md`](TEMPLATE.md) to `YYYY-MM-DD-<tester>.md` in this folder: the date the run
   ended and your GitHub handle, or `author-clean` for an author run. A second run on the same day
   adds `-2`.
2. Fill it in: the attestation, every part A to H, every stuck-point issue and the verdict
   (Passed, Pending doc fixes, or Failed at a part). Record failures as they happened.
3. Open a pull request with the file. After the run, only the "Fixed by PR" column and the Verdict
   line (Pending doc fixes → Passed, once every listed fix is merged) may be edited, in the fix's
   pull request or a follow-up one.

Records never hold secrets: no setup code, key, token, password or credential of any account.

## When SM-5 holds

SM-5 holds, and Story 10.5 can close, only when a record here shows a passing run with the
attestation ticked and **every** stuck-point issue it lists fixed in the docs by a merged pull
request. A failed run stays in the folder as history; the next run gets its own record.
