# ⚠️ TEMPORARY (as of 2026-08-27): new work targets Version6.5, not master

We are in a transition phase. Unless the user says otherwise:

- Branch new work off **`Version6.5`**, not `master`, even if you are sitting on `master` now.
- Open PRs with **`Version6.5`** as the base branch.
- Assume `Version6.5` is the right target rather than asking just to confirm it. If something
  about the task genuinely makes the target unclear, it is fine to ask — but say that you are
  assuming `Version6.5` when you do.

Delete this whole section (it exists only on master) once master is the normal target again.

---

This project has a web front-end at src/BloomBrowserUI.
The front-end uses pnpm 11.5.2. Never ever use npm or yarn.

# Architecture

- C# backend
- web front-end in React/Typescript
- WebView2 for hosting the web front-end in the desktop app
- We strictly control both ends of the API.
    - Don't worry about legacy API support. If you need to change the API, just change it on both sides.
    - Don't be overly defensive about error handling. If the API is used incorrectly, it's fine for it to throw an error. We want to know about it so we can fix it.

# Code Style

- React components are arrow function components (see the example below); `src/BloomBrowserUI/AGENTS.md` covers other functions
- do not destructure props
- do not define a props data type unless it is huge
- example: export const SomeComponent: React.FunctionComponent<{initiallySelectedGroupIndex: number;}> = (props) => {...}

- Avoid removing existing comments unless your changes make them inaccurate/obsolete
- Avoid adding a comment like "// add this line".

- Style elements using the css macro from @emotion/react directly on the element being styled, using the css prop. E.g. `<div css={css`color:red`}>`

- Where possible style things using @emotion/react rather than using sx objects.

- Avoid stacking/nesting ternary (`? :`) operators (e.g. `a ? x : b ? y : z`). They're too hard for humans to read. Use an if/else-if chain (or a switch) instead. A single, non-nested ternary is fine.

- For Typescript coding style, see ./src/BloomBrowserUI/AGENTS.md

# Testing

- Fail Fast. Don't write code that silently works around failed dependencies. If a dependency is missing we should fail. Javascript itself will fail if we try to use a missing dependency, and that's fine. E.g. if you expect a foo to be defined, don't write "if(foo){}". Just use foo and if it's null, fine, we'll get an error, which is good.
- Try to make it so that test failures indicate what went wrong. For example, `fail("An error occurred in setup; we should not have gotten here")` would be better than `expect(false).toBeTruthy();` and `expect(foo).toBe(3);` would be better than `expect(foo === 3).toBe(true);`.
- Add sanity checks to guard against falsely passing tests. For example, when unit testing a method, sanity check that the test data values are as expected before you call the method, and then after you call the method you can verify that those values have changed as expected.

## Building and testing while a Bloom is running

- **C#:** build and test through `build/agent-dotnet.sh` (PowerShell: `.ps1`), never bare
  `dotnet`, because the developer's running Bloom locks the shared output. The wrapper, the
  per-run temp isolation, and the opt-in Reading App Builder real-build test are described in
  `src/BloomTests/AGENTS.md`.
- **Front-end:** editing `.ts`/`.tsx`/`.less` needs no build; the dev server pushes it into the
  running Bloom. `pnpm test`, `pnpm lint`, `pnpm typecheck` are always safe. **Never run the full
  `pnpm build` yourself**; to confirm the production bundle compiles use `build/agent-vite.sh`.
  Details, including what to do if the vitest suite seems to hang, are in
  `src/BloomBrowserUI/AGENTS.md`.

# Terminal
The vscode terminal often loses the first character sent from copilot agents. So if you send "cd" it might just say "bash: d: command not found". Try prefixing commands with a space.

# Running Bloom
- Do not run an already-built `Bloom.exe` directly, because it may be stale and miss local code changes.
- Use a source-aware launcher that picks up the current repo state. Right now the default launcher is `./go.sh` at the repo root. If a build fails with errors like missing `PodcastUtilities`, `IDevice`, or other types/namespaces
  that "could not be found" (CS0246) in files such as `src/BloomExe/Publish/BloomPub/usb/AndroidDeviceUsbConnection.cs`, the problem is probably that this worktree has not got its dependencies yet. Fix that with `./init.sh`.

- Do not launch Bloom with `dotnet run` or `node scripts/watchBloomExe.mjs` unless you are specifically working on the launcher scripts themselves or a better repo-supported source-aware launcher has been documented.

If you create new files for temporary purposes (e.g. output or artifact or log files), be sure to clean them up when you're done and be careful not to accidentally commit them.

# Localization
Whenever you add, modify, or review localizable strings (XLF entries), follow `.claude/skills/xlf-strings/SKILL.md`. For how Crowdin works and why those rules exist — including why a no-longer-used string is marked obsolete rather than deleted — see `DistFiles/localization/README.md`.

Two rules apply at all times, even outside that skill:
- **Only ever edit files under `DistFiles/localization/en/`** — never touch the other language subdirectories.
- **Never delete a `<trans-unit>` on your own initiative**, even an obsolete one, and even when you are confident nothing uses it. Mark it obsolete and leave it.

# Commenting
All public methods should have a comment. So should most private ones!

# Git Committing
Always include a good description when creating a git commit.

# Issue tracker
This project tracks work in **YouTrack**, at https://issues.bloomlibrary.org/youtrack (Kanban
boards). Ticket ids look like **`BL-16572`** (`BL-` plus a number). The skill that talks to it is
**`youtrack-api`** — use it for any tracker operation (read an issue, find the id for the current
work, list/post comments, set an issue's State); the higher-level `youtrack-*` skills build on it.

To find the ticket id for the branch you are on, look for a `BL-XXXXX` token in the branch name,
then the PR title, then recent commit messages. Not every branch has a card — some work (small
cleanups, branding tweaks, tooling) is done without one, so finding no id is a normal outcome, not
a reason to go hunting.

**A `[6.X]` prefix on a card's summary names the target branch.** If a card's summary starts
with something like `[6.4]` or `[6.5]`, the fix belongs on that `VersionX.Y` branch. Before
starting, check the branch you are about to branch from and the PR base you plan to use; if
they don't match the prefix, stop and confirm the target with the user rather than guessing.
A card with no prefix has no branch requirement from this rule.

# Plans for multi-step or future work
In-progress plans, refactoring proposals, and other short-lived repo-level guidance live under
`docs/<slug>/`, one folder per effort.

# Nested AGENTS.md files
Guidance that only matters in one part of the tree lives in an `AGENTS.md` in that folder
(`src/BloomBrowserUI`, `src/BloomTests`, `src/content/branding`, …).

# Skills
Reusable, task-specific procedures for this repo live in `.claude/skills/<name>/SKILL.md`, a
folder both Claude Code and GitHub Copilot discover on their own. When a request matches one,
follow its `SKILL.md` as the authoritative procedure (it may have more files alongside it); an
agent that does not discover skills automatically should open the file itself.

Team-wide workflow skills that are not specific to this repo (the preflight → self-review →
peer-review pipeline, Devin and Reviewable review handling, YouTrack operations) live in
https://github.com/BloomBooks/bloom-team-skills — install per its README (clone + symlink
into `~/.claude/skills`).
