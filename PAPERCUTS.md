# Papercuts

Small dev/agent/tooling friction points and improvement ideas — captured in the moment,
fixed later. This file holds cuts about **this repo** (its docs, scripts, build, tests,
agent experience); cuts about the environment, machine, or team workflow go in
bloom-team-skills' `PAPERCUTS.md`. The full procedure is the `papercut` skill in
https://github.com/BloomBooks/bloom-team-skills.

House rules:

- Add new entries at the **top**, directly under this header block.
- Entry format: `## YYYY-MM-DD — Title`, then `- **Cut:**` / `- **Idea:**` / optional
  `- **Context:**` lines. 2–5 lines total.
- Hit the same cut again? Add a dated `seen again: ...` line to the existing entry instead of
  duplicating it.
- On a merge conflict here, keep both sides' entries.
- Product bugs/features go to YouTrack instead.
- To work through the backlog, run the `papercut` skill in trim mode ("trim the papercuts").
  Fixed, promoted, or stale entries get **deleted** — the log only contains open cuts.

---

## 2026-07-10 — Pre-commit hook fails confusingly when node_modules is empty

- **Cut:** With `src/BloomBrowserUI/node_modules` empty (e.g. a fresh checkout/worktree after
  the pnpm migration, before `pnpm install`), `git commit` fails with
  `pretty-quick: command not found` — and since the hook runs mid-command, the failed commit
  is easy to miss.
- **Idea:** Have `.vite-hooks/pre-commit` check for `node_modules/.bin/pretty-quick` first and
  fail with "front-end dependencies not installed — run `pnpm install` in src/BloomBrowserUI".
- **Context:** Hit while committing this very file.
