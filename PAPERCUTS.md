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


## 2026-07-15 — agent-dotnet full suite can never be fully green (9 environmental failures)

- **Cut:** Running the full C# suite through `build/agent-dotnet.sh` always fails 9 tests for
  environment reasons: the per-terminal agent output tree lacks `BloomPdfMaker.exe` (4
  `PdfMakerTests` failures) and `XMatterHelper` can't locate the checked-in
  `src/BloomTests/xMatter/Test-XMatter` packs from that tree (5 failures in
  `XMatterHelperTests` and `InsertPageAfter_FromDifferentBook_MergesStyles`). Agents can't get
  a green full-suite baseline, so every preflight has to re-establish that these 9 are noise.
- **Idea:** Make the wrapper copy/link `BloomPdfMaker.exe` into its private output tree and fix
  the xmatter file-locator path (or document the 9 known failures in AGENTS.md as expected under
  the wrapper).
- **Context:** BloomDesktop, found during `/preflight` of PR #8067 (speedUpCSharpTests).


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
