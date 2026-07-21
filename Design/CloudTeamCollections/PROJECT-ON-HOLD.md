# Cloud Team Collections — ON HOLD (resume handoff)

**Status: paused 21 Jul 2026.** The feature is implemented and flag-gated; work paused during the
review/hardening phase. This file captures state and gotchas that are **not** in the other docs.
For the feature itself, read those first (see "Where to look" at the bottom).

## Git / PR state — READ THIS FIRST

- Working branch: **`cloud-collections`**, local HEAD **`1b00656d86`** (a merge commit; the branch
  now contains current `origin/master` = `a5c79edee0` as of the pause).
- ⚠️ **`origin/cloud-collections` is NOT up to date.** The ~300-commit local history — including
  every recent review change below and the master merge — was never pushed to
  `origin/cloud-collections`. It lives **only in this local clone** plus (as a squashed tree) in
  the review branch. If this machine's clone could be lost, `git push origin cloud-collections`
  before relying on it.
- Review PR: **#8052**, branch **`cloud-tc-for-review`**, head **`3d9068ac70`**, base `master`,
  9 path-grouped commits / 251 files, **byte-identical** to `cloud-collections`'s tree. It is a
  regenerable packaging artifact, not hand-edited.

### How to update PR #8052 (current mechanics)
Regenerate it whenever `cloud-collections` changes (base is now **current `origin/master`**, not the
old merge-base):
```bash
git checkout -B cloud-tc-for-review origin/master
git diff --name-only origin/master cloud-collections > /tmp/allfiles.txt
Design/CloudTeamCollections/orchestration/regen-bucket.sh  /tmp/allfiles.txt /tmp/groups   # must print "unmatched: 0"
Design/CloudTeamCollections/orchestration/regen-rebuild.sh /tmp/groups                      # must print "IDENTITY OK"
git push origin cloud-tc-for-review --force-with-lease
git checkout cloud-collections
```
(Extend `regen-bucket.sh`'s path patterns if new top-level paths appear, or it reports "unmatched".)
Greptile review is triggered by a PR comment whose first line is `@greptile-apps review` (not done
since the last push). Human-review plan: `orchestration/REVIEW-PLAN.md`.

## What changed in the recent review pass (beyond IMPLEMENTATION.md's merge log)
All committed on `cloud-collections`; each also has a bloom-desktop pre-commit-hook-passing commit:
1. **DB → declarative schema.** 16 migrations collapsed into `supabase/schemas/` (source of truth) +
   one generated init migration. `supabase db diff` is NOT used to generate it (it drops COMMENT ON
   and GRANT EXECUTE) — the init is a lossless concatenation via `build/regen-init-migration.sh`,
   which fail-closes once `supabase/.init-migration-frozen` exists (created at go-live). Full
   workflow: CONTRACTS.md → "Database: declarative schema".
2. **6.5 gating:** the "Cloud Team Collections" opt-in checkbox is hidden unless the
   `cloudCollections` environment variable is set (`CollectionSettingsApi.CloudTeamCollectionOptionVisible`).
3. **Terminology:** "dev" no longer means the local emulation anywhere — **local** = on-machine
   (Supabase+MinIO), **dev/sandbox** = hosted test cloud. Enum `CloudAuthMode.Local`, class
   `LocalCloudAuthProvider`, wire value `"local"`, E2E harness `localStack.ts`/`LocalStackUser`.
4. **Lock semantics:** `CloudCachedBook.LockedBy` is the raw auth user id (not an email);
   `LockedByEmail`/`LockedByDisplayName` are display-only. `SyncLockDisplayFieldsLocked` keeps them
   consistent after checkout/takeover/checkin (fixes a "shows previous owner after takeover" bug).
5. **Doc/comment clarity:** version-tracking trio (`CurrentVersionId` identity vs `CurrentVersionSeq`
   / `LocalVersionSeq` order), `GetOrAddLocked` bare-row invariant, `_groupsByKey` (3 independently
   versioned collection-file groups), and a present-tense rewrite of `../CloudTeamCollections.md`.

## Next steps / open decisions (when resuming)
- **Split the review into two PRs** — `bloom-core-supabase` (schema/functions/edge/`server/dev`) and
  `bloom-desktop` (client). **OPEN DECISION:** where the Supabase-dependent test code goes (C#
  `*LiveTests` + `src/BloomTests/e2e/`). Both detailed in **GOING-LIVE.md § 6.0**.
- Remaining go-live work: GOING-LIVE.md Phases 2–5 (AWS provisioning, hosted Supabase projects,
  real auth wiring, sandbox verification, product-gap items).
- If continuing review: post the Greptile trigger; work `REVIEW-PLAN.md`.

## Operational gotchas (not in other docs)
- **Local stack lifecycle.** Start: `supabase start` + MinIO (`server/dev/docker-compose.yml`, via
  Podman here) + `supabase functions serve --env-file server/dev/functions.env`. Container runtime
  on this machine is **Podman**, not Docker (no `docker` on PATH; the Supabase CLI talks to Podman's
  Docker-compatible API). Stop everything with `supabase stop` + bring the MinIO compose down.
- **`supabase db diff` shadow DB.** It provisions a shadow DB on `shadow_port` (54320). A crashed/
  leaked diff can leave an orphaned shadow container bound to 54320 that blocks the next diff
  ("address already in use"); a leftover `gallant_hellman`-style Podman container was seen this
  session. Remove the orphan (`podman rm -f <name>`) or temporarily point `shadow_port` elsewhere.
- **vitest fork-pool flakiness (Windows):** running several suites in one `vitest run` invocation can
  fail with "Timeout starting forks runner". Run affected suites **individually**:
  `node_modules/.bin/vitest run <path>` from `src/BloomBrowserUI`. (Separate from the documented
  `_SKIP_WEBSOCKET_CREATION_` hang fix in `vitest.setup.ts`.)
- **Build/test C# while a Bloom is running:** use `build/agent-dotnet.sh` (never plain `dotnet`).
  Cloud filter: `--filter "FullyQualifiedName~TeamCollection.Cloud"` (163–164 tests; add
  `~SharingApi` if touching the API). pgTAP: `supabase test db`. Rebuild DB from the init migration:
  `supabase db reset`.

## Where to look (docs, in reading order)
1. `../CloudTeamCollections.md` — architecture overview (now present-tense).
2. `CONTRACTS.md` — API surface + the declarative-schema workflow.
3. `SCHEMA.md` — `tc` ER diagram.
4. `GOING-LIVE.md` — deployment runbook + the two-PR split plan (§6.0).
5. `server/dev/README.md` — local stack setup.
6. `IMPLEMENTATION.md` (build/merge log) and `orchestration/RESUME.md` (+ `DOGFOOD-BATCH-1.md`
   progress log) — how it was built.
