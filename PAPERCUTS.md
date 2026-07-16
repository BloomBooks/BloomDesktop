# Papercuts

Small dev/agent/tooling friction points and improvement ideas — captured in the moment,
fixed later. This file holds cuts about **this repo** (its docs, scripts, build, tests);
cuts about the environment, machine setup, team workflow, or agents in general go in
bloom-team-skills' `PAPERCUTS.md`. The full procedure is the `papercut` skill in
bloom-team-skills.

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
