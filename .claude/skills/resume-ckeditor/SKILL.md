---
name: resume-ckeditor
description: Resume the long-running "retire CKEditor / unify Undo" project (BL-6681). Use when the user says "/resume-ckeditor", "resume the ckeditor work", "continue retiring ckeditor", or picks that project back up after an interruption (sleep, token exhaustion, days off).
---

# Resume the CKEditor-retirement project (BL-6681)

This is a deliberately long-running project spanning many sessions and many rebases against a
moving `master`. Its state lives in `docs/retire-ckeditor/`, not in any session's memory.

## Do this, in order

1. **Read the state.**
   - `docs/retire-ckeditor/PROGRESS.md` — the live log, current phase, and next actions. Start here.
   - `docs/retire-ckeditor/PLAN.md` — the staged plan (Stages 0–6). Authoritative for *what* to
     do. §11 records what BL-6681 itself asks for, and what on it is already obsolete.
   - `docs/retire-ckeditor/REVIEW-NOTES.md` — findings already verified and decisions already
     made. **Do not re-litigate anything settled there.** If you think a settled point is wrong,
     say so explicitly to the user rather than quietly changing course.
   - `docs/retire-ckeditor/BEHAVIOR-INVENTORY.md` if it exists — the behaviours that must survive.

2. **Orient in git.** `git status`, `git log --oneline -15`, and note the current branch. Because
   the plan deliberately avoids long-lived branches, you may well be on `master` with nothing in
   flight; that is the normal resting state between stages, not a sign something was lost.

3. **Check for drift.** Other work lands on `master` continuously. Before continuing a stage,
   confirm the files it touches still look the way the plan assumes — the plan cites specific
   `file:line` locations, and those move. If a citation has gone stale, fix the plan text as part
   of the work; a plan nobody trusts is worse than no plan.

4. **Continue from the next unchecked item** in PROGRESS.md's "Next actions". Confirm with the
   user which stage to work on if more than one is plausible.

5. **Before ending the session** (or when you sense you are running low on context), update
   `PROGRESS.md`: what you did, what you learned, the branch/PR, and a revised "Next actions".
   Do this even if the work is half-finished — especially then.

## Ground rules for this project

- **New code goes in new files** (`bookEdit/undo/`, `bookEdit/textEditor/`). Edits to existing
  files should be one-line dispatches wherever possible, and as late in the plan as possible.
  This is the project's whole defence against rebase pain.
- **Don't keep a long-lived branch.** Each stage is designed to be its own small, green,
  flag-inert PR onto `master`.
- **Deletion commits (Stage 5) are regenerated, never rebased.** If one conflicts, throw it away
  and redo it mechanically.
- Build and test through the wrappers, never bare `dotnet`/`vite` — `build/agent-dotnet.sh` and
  `build/agent-vite.sh` — because the developer usually has a Bloom running via `./go.sh`. See
  `AGENTS.md`. Never run the full `pnpm build`.
- To see a change in the running Bloom, just edit the source (the Vite dev server pushes it in)
  and observe via the `run-bloom` skill. No build.
