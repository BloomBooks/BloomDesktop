# Squash plan: review-grained history for the Cloud TC feature

Goal (John, 10 Jul 2026): a new branch from current `origin/master` whose commits are
meaningful, human-reviewable steps — replacing `cloud-collections`' ~204 orchestration-grained
commits (126 first-parent) for review/merge purposes. The working branch `cloud-collections`
stays untouched; the squashed branch is a **regenerable packaging artifact**.

## Method: path-staged rebuild (recommended)

Do NOT interactive-rebase 126+ commits (it conflicts immediately — master already contains
cherry-picked batch commits, and the branch has merge-style history). Instead, rebuild the
final tree in dependency-ordered file groups:

```bash
git fetch origin
git checkout -b cloud-tc-for-review origin/master
# For each group below, in order:
#   git checkout cloud-collections -- <paths...>       (adds/modifies)
#   git rm -q <deleted paths in this group, if any>    (see Deletions note)
#   git commit  (message per group, below)
# Then VERIFY (all three must hold):
git diff cloud-tc-for-review cloud-collections --stat   # MUST be empty
dotnet test src/BloomTests/BloomTests.csproj -c Release --filter "(FullyQualifiedName~Cloud|FullyQualifiedName~TeamCollection|FullyQualifiedName~SharingApi)&FullyQualifiedName!~LiveTests"
cd src/BloomBrowserUI && pnpm lint && pnpm vitest run    # (or the targeted cloud files)
```

Properties: byte-identical end state (verified by the empty diff), zero conflict resolution,
zero history surgery, re-runnable any time `cloud-collections` advances (delete + regenerate +
force-push the packaging branch — it carries no one's work).

Caveat for reviewers (put in the PR description): each commit is a coherent reviewable unit
and the ORDER makes most of them compile, but only the FINAL tree is test-verified. That is
the accepted trade-off; per-commit CI-green is not a goal.

Deletions note: files the feature DELETED relative to master must be `git rm`'d in their
group. Enumerate with `git diff --name-status origin/master...cloud-collections | grep '^D'`
(currently expected: none or near-none; MyCloudCollectionsSection.tsx etc. were added AND
deleted within the branch so they never existed on master).

## The groups (dependency-ordered; ~9 commits)

1. **Design docs & plans** — `Design/CloudTeamCollections/**` (34 files),
   `.github/skills/xlf-strings` tweak. "Read this first" context: architecture,
   CONTRACTS.md, GOING-LIVE.md, orchestration records incl. the dogfood batch log.
   Msg: `Cloud Team Collections: design docs, wire contracts, and project records`
2. **Server: schema, RLS, RPCs, pgTAP** — `supabase/migrations/**` (7), `supabase/tests/**`,
   `supabase/config.toml`, `supabase/snippets/**`, `supabase/.gitignore`.
   Msg: `Cloud TC server: tc schema, RLS policies, RPCs, and pgTAP tests`
3. **Server: edge functions** — `supabase/functions/**` (21).
   Msg: `Cloud TC server: edge functions for checkin/download/collection-file transactions`
4. **Local dev stack** — `server/**` (16: MinIO compose, seed users, functions env,
   parity-check console, README).
   Msg: `Cloud TC dev stack: local Supabase + MinIO, seed users, S3 parity checks`
5. **Client core** — `src/BloomExe/TeamCollection/Cloud/{CloudEnvironment,CloudAuth,
   CloudCollectionClient,CloudRepoCache,CloudBookTransfer,...}.cs`, `S3Extensions`,
   `BloomExe.csproj` (AWSSDK v4 bump) + matching `src/BloomTests/TeamCollection/Cloud/`
   unit-test files for these classes.
   Msg: `Cloud TC client core: auth, API client, repo cache, S3 transfer (AWSSDK v4)`
6. **Cloud TeamCollection backend** — `CloudTeamCollection.cs`, `CloudCollectionMonitor.cs`,
   `CloudJoinFlow.cs`, `RemoteBookAutoApplyQueue.cs`, `TeamCollection*.cs` seams,
   `TeamCollectionManager.cs`, `TeamCollectionLink/LastKnownUser`, `Program.cs` (refusal
   path), `NonFatalProblem.cs` (automation guard), `DisconnectedTeamCollection.cs` + their
   tests (TeamCollectionAutoApplyTests, CloudSyncAtStartupTests, CloudAccountSwitch*, …).
   Msg: `Cloud TC backend: cache-backed TeamCollection, polling monitor, join flow,
   background downloads, account-switch handling`
7. **HTTP API layer** — `SharingApi.cs`, `TeamCollectionApi.cs`, `CollectionChooserApi.cs`,
   `CollectionApi.cs`, `ExternalApi.cs`, `WorkspaceApi/Model` bits + their tests.
   Msg: `Cloud TC API: sharing/membership endpoints, capabilities, join cards, book-list merge`
8. **Front-end UI + strings** — `src/BloomBrowserUI/**` (44), `DistFiles/localization/en/**`.
   Msg: `Cloud TC UI: sign-in, sharing dialog, status panel, join cards, download placeholders`
9. **E2E harness + specs** — `src/BloomTests/e2e/**`.
   Msg: `Cloud TC E2E: Playwright-over-CDP harness and 10 two-instance scenarios`

Bucketing rule: run `git diff --name-only origin/master...cloud-collections`, assign every
file to exactly one group (a file with mixed concerns goes to its PRIMARY group — e.g.
TeamCollection.cs → group 6 even though item-8 recovery touched it); after group 9, stage
**everything still unassigned** into the best-fitting group before its commit — the empty
final diff is the proof nothing was dropped.

## Alternative: single squash (fallback)

`git checkout -b cloud-tc-for-review origin/master && git merge --squash cloud-collections
&& git commit` — one giant commit, 235 files. Only if reviewers prefer one unit; the grouped
version costs ~30 min more and is far more reviewable.

## Coordination

- **When:** after bug #0 (takeover semantics) is decided+fixed and the full matrix verdict is
  in — packaging before that just means regenerating. Regeneration is cheap by design.
- **PRs:** open a NEW draft PR from `cloud-tc-for-review`; close #8048 with a comment pointing
  at it (bots then review the meaningful commits). `cloud-collections` remains the working
  branch until merge; regenerate the packaging branch (delete, rebuild, force-push) whenever
  the working branch advances.
- **Merge:** master ultimately merges `cloud-tc-for-review`; verify once more that its tree
  equals `cloud-collections`' before merging, then archive the working branch.
