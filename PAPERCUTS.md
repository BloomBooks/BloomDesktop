# Papercuts

Small dev/agent/tooling friction points captured mid-task (see the `papercut` skill).

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
