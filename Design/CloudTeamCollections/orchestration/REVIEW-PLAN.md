# Cloud Team Collections — human review plan (PR #8052)

A recommended reading order and prioritization for reviewing the Cloud Team Collections PR
(`cloud-tc-for-review`, 9 path-grouped commits; ~192 added + ~61 modified files). It balances
reading order that builds understanding against the strategic value of scarce human attention.

The 9 commits are already **dependency-ordered and thematically grouped**, so the commit list *is*
the review scaffold. **Review commit-by-commit in order**; within each, the notes below flag what
deserves eyes vs. what to skim. (Only the final tree is test-verified per the squash plan; the
commit ordering is for readability, not per-commit CI.)

## Two facts that shape the strategy

- **The whole feature sits behind the `cloud-team-collections` experimental flag.** Risk to
  *shipping* Bloom is therefore bounded. Human attention is best spent on (1) the **server trust
  boundary** (matters for anyone who enables it, and for the backend itself) and (2) the
  **shared-file blast radius** (the few modified base files that execute even for folder-TC /
  non-TC users). It is *safe to lean on tests* for much of the rest.
- **192 added vs 61 modified.** Added files are usually easier (read the new thing; no diff to
  reconcile). The **61 modified files are the strategic risk surface** — that is where the feature
  can regress existing Bloom.

---

## Reading sequence

### Phase 0 — Orient (read before any code)
- `Design/CloudTeamCollections/CONTRACTS.md` — **the single most important file.** The C#↔Supabase
  wire contract; every RPC / edge-function / client method maps to an entry here. Downstream code
  is opaque without it.
- The architecture overview + `GOING-LIVE.md` (deploy + security model). Skim the dogfood-batch
  log only for the known-bug context (#0 takeover, #15 cross-machine lock, #18 rename-clobber) —
  those name the sharp edges.

### Phase 1 — Server contract & security boundary (commits 2 → 3) — highest strategic value
Review the server *before* the client: once you know what the server enforces, you can judge
whether the client trusts it correctly.
- **Commit 2** in file order: `20260706000001_tc_schema` (data model) →
  **`_000002_tc_rls`** (the authorization boundary — read the RLS policies most carefully:
  *can a non-member read or mutate a collection's rows?*) → `_000003_tc_rpcs` and
  `_000004_tc_checkin_txn_functions` (membership gates inside each RPC) → the
  takeover / seat / member-display-name migrations. Then read `supabase/tests/*.sql` (pgTAP) as the
  executable spec of intended behavior.
- **Commit 3** — edge functions: **`download-start`** (does it scope the vended S3 credentials to
  *this* collection's prefix, or could a member pull another collection's objects?),
  `checkin-start` / `checkin-finish` / `checkin-abort` (the two-phase upload transaction — can a
  crash between phases corrupt state?), `collection-files-start` / `collection-files-finish`,
  `_shared`.

Why tests can't replace this: a passing test proves the happy path; only a human proves the
*absence* of an authz hole.

### Phase 2 — Client-core primitives (commit 5)
The building blocks Phase 3 orchestrates. Order: `CloudEnvironment` → `CloudAuth` +
`DevCloudAuthProvider` / `FirebaseCloudAuthProvider` + **`CloudTokenStore`** (credential handling +
DPAPI persistence) → `CloudCollectionClient` (RPC wrappers, ~1:1 with CONTRACTS) →
`BookVersionManifest` (hash / identity) → **`CloudBookTransfer`** (pinned/verified download — data
integrity) → **`CloudRepoCache`** (local mirror + `ApplyDelta` / cursor — correctness core).
Note `BloomS3Client.cs` / `S3Extensions.cs` here are **modified** and shared with the existing
BloomLibrary upload path — a blast-radius item.

### Phase 3 — The orchestration (commit 6) — highest correctness / data-loss risk
The most complex, stateful, concurrency-heavy code; where the live bugs lived. Start with the
seams, then the driver:
- `TeamCollection.cs` (**modified** — the new virtual seams) and `TeamCollection.AutoApply.cs`
  (background-download queue + self-healing + rename detection).
- `CloudTeamCollection.cs` (+ `.CollectionFiles.cs`) — identity-first resolution,
  **rename detection (#18)**, **account-switch / takeover (#0/#15)**, the
  "updates-available includes checked-out-elsewhere" decision, the collection-file
  **delete-extras** path (the one that once stripped `TeamCollectionLink.txt`).
- `CloudCollectionMonitor.cs` (polling / concurrency), `CloudJoinFlow.cs`,
  `RemoteBookAutoApplyQueue.cs`, `TeamCollectionManager.cs` (**modified**), `Program.cs`
  (**modified** — refusal path), `WorkspaceModel.cs` (**modified** — tier-timing).

### Phase 4 — Integration surface (commits 7 → 8)
- **Commit 7** (C#↔TS API): `SharingApi.cs`, `TeamCollectionApi.cs` (modified),
  `CollectionChooserApi` / `CollectionApi` (modified).
- **Commit 8** (front-end): prioritize the *flows* — sign-in dialog, join-collection dialog,
  `SharingPanel`, `TeamCollectionSettingsPanel`, join cards on `CollectionCard`, status panel,
  `bloomApi.ts` hooks — over cosmetic. Worth a glance: *why did the registration dialog change?*
  (`react_components/registration/*` modified).

### Phase 5 — Skim / lean on automation (commits 4, 9, localization, generated)
- **Commit 4** (`server/**` dev stack): **dev-only, never shipped to end users.** Read the README
  to understand the local stack; deep review is low-value.
- **Commit 9** (E2E harness): self-verifying — it runs the real two-instance scenarios. Read the
  *scenario list* as a coverage map; don't line-review the harness.
- Localization XLF (en only), `.stories.tsx`, generated files: skip.
- Unit tests everywhere: don't line-review — read test **names** as the behavioral spec and note
  coverage gaps.

---

## If resources don't permit reviewing everything

| Tier | What | Why |
|---|---|---|
| **1 — Must** | RLS (`_000002`) + RPC/txn membership gates + `download-start` credential scoping; `CloudAuth` / `CloudTokenStore`; `CloudRepoCache` (delta/cursor); `CloudTeamCollection` (identity/rename/takeover + delete-extras); the seams in `TeamCollection.cs` / `.AutoApply.cs`; **the ~20 modified shared C# files** (blast radius) | Authz holes, data-loss / clobber, concurrency, and regressions to existing Bloom — the things tests + Greptile are structurally weakest at |
| **2 — Should** | Rest of client core (`CloudBookTransfer`, `CloudCollectionClient`), monitor / join / queue, edge-function detail, HTTP API layer | Correctness of the machinery, but more test-covered |
| **3 — Lean on CI** | Dev stack, E2E harness, front-end cosmetic, localization, generated, unit-test bodies | Self-verifying or low blast radius; the byte-identical regen + green suites cover these |

### The blast-radius shortlist
Do this even if you do nothing else in C#. The modified, non-test files in existing Bloom areas —
`TeamCollection.cs`, `FolderTeamCollection.cs`, `TeamCollection.ErrorReporting.cs`,
`TeamCollectionManager.cs`, `TeamCollectionMessage.cs`, `DisconnectedTeamCollection.cs`,
`WorkspaceModel.cs`, `Program.cs`, `NonFatalProblem.cs`, `ApplicationContainer.cs`,
`HtmlErrorReporter.cs`, `BrowserProgressDialog.cs`, `ExperimentalFeatures.cs`,
`FeatureRegistry.cs`, `HistoryEvent.cs`, `CollectionSettingsDialog.cs`, `BloomS3Client.cs`,
`S3Extensions.cs`, and the `web/controllers/*Api.cs`. One question each: *does this change behavior
for folder TCs or non-TC Bloom?* Most cloud changes are additive (new virtual seams with no-op
defaults), so the answer *should* mostly be "no" — confirming that is exactly where a human adds
value the flag-gating can't.

### Let the machines go first
Use Greptile / Devin + the pgTAP / unit / e2e suites as pass 1, so human time goes to what they
can't do: reasoning about authz *absence*, data-loss paths, and cross-feature regressions.
