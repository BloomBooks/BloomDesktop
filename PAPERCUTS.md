# Papercuts

Small dev/agent/tooling friction points and improvement ideas — captured in the moment,
fixed later. This file holds cuts about **this repo** (its docs, scripts, build, tests, and
skills); cuts about the environment, machine setup, or team workflow go in bloom-team-skills'
`PAPERCUTS.md`. The full procedure is the `papercut` skill.

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

## 2026-07-15 — Orphaned `node.exe` dev stacks pile up until `go.sh` can't launch
- **Cut:** Each `go.sh` spawns a dev stack (`dev.mjs` → vite + ~7 `onchange`/`watchLess`
  watchers + `watchBloomExe`). On Windows, closing a terminal or Ctrl-C doesn't reliably tear
  down that child tree, so stacks orphan across worktrees/sessions and `node.exe` accumulates
  (saw ~40 live: 3 worktrees + a dead-parent chrome-devtools-mcp daemon). Under that load
  `go.mjs`'s Vite health gate times out — it needs **2 consecutive sub-3s** `/@vite/client`
  responses, and Vite binds **IPv6-only (`[::1]`, 127.0.0.1 refused)** on this machine, so
  there's zero margin — and every launch fails with "Vite … never became reachable," i.e. it
  "piles up until none work." `go.mjs`'s startup sweep only reaps *this* worktree's stale
  procs, and the chrome-devtools-mcp daemon has a **watchdog that respawns it after kill**
  (self-healing zombie).
- **Idea:** Add a zombie-reaper keyed on *liveness of the controller*, not worktree path: a
  vite/watcher subtree with no living `go.mjs`/`watchBloomExe` ancestor (or a proc whose
  parent is dead) is a zombie → auto-kill. Run it on `go.sh` startup across **all** Bloom
  worktree stacks (safe because it never touches subtrees with a live controller), and/or ship
  a standalone `pnpm reap`. Separately harden the health probe so a slow-but-listening Vite
  passes: bind Vite on 127.0.0.1 too, accept a single success, and scale the timeout with
  detected load. For chrome-devtools-mcp, kill the watchdog first (else it resurrects the
  daemon) — memory note already says prefer the CLI over the MCP.
- **Context:** BL-16549AiSourceBubbles; repeated `./go.sh` failures. See `go.mjs`
  `waitForViteClient` / `startDevServerOnPort` and `processTree.mjs`
  `sweepStaleWorktreeNodeProcesses`.

- Running `dotnet test` (or any BloomExe build) while a `./go.sh` / `dotnet watch`
  Bloom is live fails at the copy-to-output step: the running process locks both
  `output/Debug/AnyCPU/Bloom.exe` (native apphost) and, once hot-reload deltas have
  been applied, `Bloom.dll` too (MSB3026/MSB3027 "being used by another process").
  Compilation itself succeeds; only the copy fails, so the tests never run.
  Workaround that neither kills the running instance nor touches the locked output:
  redirect the whole build to a scratch dir and skip the apphost, e.g.
  `dotnet test src/BloomTests/BloomTests.csproj --filter ... -p:UseAppHost=false -p:OutDir=<abs-scratch-path-with-trailing-slash>`.

## 2026-07-15 — config-r draws a divider between every direct group child; label is string-typed
- **Cut:** `@sillsdev/config-r`'s `ConfigrGroup` (in a focused page) inserts a horizontal
  divider between *every* direct child of the group. So an engine block written as a
  `<ConfigrBoolean/>` followed by a separate `{enabled && <>...fields...</>}` gets an unwanted
  line between the checkbox and its own settings. Also, `IConfigrProps.label` is typed `string`,
  so you can't cleanly put a logo/node before a label.
- **Workaround:** Wrap each engine's checkbox + conditional fields in a single fragment so the
  group sees one child per engine (dividers land only *between* engines). For a logo-in-label,
  pass a ReactNode cast `as unknown as string` — config-r renders `label` straight into MUI
  `ListItemText` `primary`, which accepts a node, so it works at runtime.
- **Idea:** Ask config-r for a `label?: React.ReactNode` type and/or a per-row `hideDivider`
  (or a "subgroup" that suppresses internal dividers). See `AiTranslationSettingsGroup.tsx`.
- **Context:** BL-16549AiSourceBubbles AI Source Bubbles settings.

## 2026-07-13 — pnpm-lock.yaml reformats wholesale on any install (format drift)
- **Cut:** The committed `src/BloomBrowserUI/pnpm-lock.yaml` (on master too) is in an
  older pnpm serialization style (double-quoted `lockfileVersion`, 4-space indent, and it
  resolves some deps with a `(supports-color@5.5.0)` peer suffix). But the pinned + active
  pnpm (11.5.2, per `packageManager`) writes a *different* style (single-quoted, 2-space,
  no supports-color suffix). So **any** `pnpm install` rewrites the entire lockfile,
  producing a spurious ~30k-line diff that has nothing to do with your actual change. To
  bump a single `github:` dependency's commit hash I had to hand-patch the lock (swap the
  4 hash occurrences + the integrity line) to keep the diff minimal and mergeable.
- **Idea:** Regenerate/commit the lockfile once with the pinned pnpm so committed state
  matches `packageManager` output, or document the exact pnpm invocation the team uses so
  installs are format-stable. Until then, hash bumps need a manual lock edit.
- **Context:** BL image-chooser integration PR (BloomDesktop #8059); local pnpm 11.5.2.

## 2026-07-11 — Can't screenshot Bloom's WinForms modal dialogs via CDP
- **Cut:** Verifying a `WireUpForWinforms` modal (e.g. `CollectionChooserDialog`) is painful. The modal opens in a separate WebView2 that is NOT exposed on the main CDP endpoint (`/json/list` shows only the main workspace page), so `screenshotBloom.mjs` can't capture it. Vite React Fast Refresh also closes any open modal on HMR. I fell back to OS-level screen capture, which needed the `AttachThreadInput` foregrounding trick because the ORCA host window covers the screen and `SetForegroundWindow` from a background process is blocked.
- **Idea:** Have the `run-bloom` / `bloom-automation` skill document a supported way to screenshot modal dialogs — e.g. expose the dialog's WebView2 on a discoverable CDP port, or ship a helper that does the force-foreground + region capture.
- **Context:** ImproveVisuals branch, restyling the Open/Create Collections dialog to the 2A design.
