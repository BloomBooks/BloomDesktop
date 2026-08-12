---
name: resume-ckeditor
description: Resume the long-running "retire CKEditor / unify Undo" project (BL-6681). Use when the user says "/resume-ckeditor", "resume the ckeditor work", "continue retiring ckeditor", or picks that project back up after an interruption (sleep, token exhaustion, days off).
---

# Resume the CKEditor-retirement project (BL-6681)

This is a deliberately long-running project spanning many sessions and months of a moving `master`.
Its state lives in `docs/retire-ckeditor/`, not in any session's memory.

## If you are on a fresh clone or a different computer

**None of this project exists on `master`** — nothing may merge there until a `Version6.5` branch is
cut (see the ground rules), so the plan, the progress log, the code, and *this skill file* all live
only on the project's own branches. A checkout of `master` has no `/resume-ckeditor` at all.

So the first move is always:

```sh
git fetch origin
git branch -r | grep BL-6681             # see what exists
git checkout BL-6681-stage1-undostack    # or whatever PROGRESS.md's branch table calls the tip
```

Then read `docs/retire-ckeditor/PROGRESS.md`, whose **branch table** is authoritative for which
branch is which and which one is the current working tip. That table changes as stages advance;
trust it over this file, and over any branch name you remember.

## Do this, in order

1. **Read the state.**
   - `docs/retire-ckeditor/PROGRESS.md` — the live log, the branch table, the master-sync log, and
     the next actions. **Start here.**
   - `docs/retire-ckeditor/PLAN.md` — the staged plan (Stages 0–6). Authoritative for *what* to do.
     **§5 is the branch strategy and it is not optional reading** — the no-merging constraint makes
     several natural instincts (rebase onto master, land the stage on master) actively wrong. §11
     records what BL-6681 itself asks for, and what on it is already obsolete.
   - `docs/retire-ckeditor/REVIEW-NOTES.md` — findings already verified and decisions already made.
     **Do not re-litigate anything settled there.** If you think a settled point is wrong, say so
     explicitly to the user rather than quietly changing course.
   - `docs/retire-ckeditor/DEFERRED-EDITS.md` — edits to *existing* files that finished new code is
     waiting on, each with why it is safe and what proves it worked. Check whether any are now due.
   - `docs/retire-ckeditor/BEHAVIOR-INVENTORY.md` — the behaviours that must survive.

2. **Orient in git.** `git status`, `git log --oneline -15`, `git branch -vv | grep BL-6681`. You
   should be on one of the project's branches, not `master`. Several live branches at once is the
   normal state here, not a sign something went wrong — read PROGRESS.md's branch table.

3. **Check drift, and sync if due.** `master` moves ~17 commits a day, of which ~2 touch this
   project's files. PLAN.md §5.3 gives the procedure and §5.1 the measured numbers. In short:

   ```sh
   git log <last-sync-sha>..origin/master --oneline -- \
       src/BloomBrowserUI/bookEdit/js/bloomEditing.ts \
       src/BloomBrowserUI/bookEdit/toolbox/toolbox.ts \
       src/BloomBrowserUI/bookEdit/bloomField/BloomField.ts \
       src/BloomBrowserUI/lib/ckeditor
   ```

   where `<last-sync-sha>` is the newest row of PROGRESS.md's master-sync table. Sync weekly, and
   always before starting a new stage. **Merge into the integration branch; never rebase it.**

   Separately, the plan cites specific `file:line` locations and those drift. If a citation has gone
   stale, fix the plan text as part of the work — a plan nobody trusts is worse than no plan.

4. **Continue from the next unchecked item** in PROGRESS.md's "Next actions". Confirm with the user
   which stage to work on if more than one is plausible.

5. **Before ending the session** (or when you sense you are running low on context), update
   `PROGRESS.md`: what you did, what you learned, the branch state, and a revised "Next actions".
   Do this even if the work is half-finished — especially then. Then **push**, so the work survives a
   move to another machine.

## Ground rules for this project

- **Nothing merges to `master` until a `Version6.5` branch is cut** (manager's decision,
  2026-08-06), which happens once 6.5 is mostly finished. Everything below follows from that;
  PLAN.md §5 is the full treatment.
- **One long-lived integration branch, `BL-6681-ckeditor`,** is the project's trunk and the only
  branch that merges `master` in. Each stage is a short-lived branch off it, PR'd *into* it and
  **squash-merged**, so integration carries roughly one commit per stage. Delete a stage branch once
  merged and cut the next fresh, or sibling branches re-apply changes already in.
  - Note this **reverses** the project's original "never keep a long-lived branch, land small PRs
    promptly" rule. If you find that rule quoted anywhere, it is historical.
- **Merge, never rebase, the integration branch or any pushed/reviewed branch.** Rebasing rewrites
  reviewed commits, discards their review threads, needs a force-push, and re-resolves the same
  conflict on every replay. An unreviewed, unpushed stage branch may still be rebased onto
  integration freely.
- **After every master sync, run the nightly by hand:**
  `gh workflow run nightly.yml --ref BL-6681-ckeditor`. It is schedule-only and master-only, and it
  is the only thing that runs the full C# suite and the visual-regression suite — a branch that never
  merges otherwise goes months with neither.
- **New code goes in new files** (`bookEdit/undo/`, `bookEdit/textEditor/`). Edits to existing files
  should be one-line dispatches wherever possible, and as late in the plan as possible. This is the
  main reason a months-long branch is survivable; record any that must wait in DEFERRED-EDITS.md.
- **Deletion commits (Stage 5) are regenerated, never reconciled.** If one conflicts with an incoming
  master change, throw it away and redo it mechanically against the new state.
- **Every stage boundary must be a state that could ship as-is** — green, flag-inert, no half-applied
  dispatch. The merge date is set by someone else and may move.
- Build and test through the wrappers, never bare `dotnet`/`vite` — `build/agent-dotnet.sh` and
  `build/agent-vite.sh` — because the developer usually has a Bloom running via `./go.sh`. See
  `AGENTS.md`. Never run the full `pnpm build`.
- To see a change in the running Bloom, just edit the source (the Vite dev server pushes it in) and
  observe via the `run-bloom` skill. No build.
