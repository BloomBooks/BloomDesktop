# Cloud Team Collections — going-live runbook

How to take Cloud Team Collections from the fully-local stack (local Supabase + MinIO +
local auth; see `server/dev/README.md`) to real, testable infrastructure, and what must be true
before the `cloud-collections` branch merges to master. Each step is tagged **[HUMAN]** (needs
credentials, org access, or a judgment call) or **[AGENT]** (a codeable task an agent can be
given, with the human reviewing). Steps are ordered; parallelizable groups are noted.

Design context: `../CloudTeamCollections.md` · Contracts: `CONTRACTS.md` · Progress:
`IMPLEMENTATION.md` (this file expands its "Deferred until real infrastructure" list).

---

## Phase 1 — Decisions (block everything else in Phase 2+)

### 1.1 [DECIDED 8 Jul 2026] Auth option: **A**
**Option A: Supabase third-party Firebase auth** — Supabase is configured to trust
Firebase-issued JWTs directly, so the Bloom user signs in with the same BloomLibrary
(Firebase) account they already have, and that token IS the Supabase credential. Phase 3
below is therefore actionable; the Bloom-side provider work (3.4) started the same day.
- **A (chosen)**: BloomLibrary2's login page forwards the Firebase ID + refresh tokens it
  already holds to Bloom (~5 lines in BloomLibrary2 `src/editor.ts`), and Supabase is set to
  accept Firebase as a third-party auth provider. Requires one NEW small Firebase Admin cloud
  function that adds the static `role: "authenticated"` custom claim to every user (plus a
  one-time backfill over existing users — no custom-claims infrastructure exists today).
- ~~B~~: exchange the legacy Parse session token — rejected; welds us to bloom-parse-server,
  which is being decommissioned.
- ~~C~~: hand-validate Firebase JWTs ourselves per the stale `bloom-parse-server/supabase/`
  docs — rejected; more code we own, no benefit over A.

### 1.2 [DECIDED 9 Jul 2026] Safety-window duration: **7 days**
John confirmed the `provision-aws.ps1` default of 7 days for noncurrent-version expiry
("keeping noncurrent objects for 7 days seems plenty"). Constraint honored: strictly greater
than the 48-hour checkin-transaction lifetime (`tc.checkin_transactions.expires_at`). No
script change needed — step 2.1 runs as written.

---

## Phase 2 — Infrastructure provisioning

### 2.1 [HUMAN] Provision AWS (S3 + IAM)
Run the reviewed-but-never-run script (needs an AWS account + CLI credentials with S3/IAM
admin rights — that's why it's a human step):

```powershell
# dry run first:
server\provision-aws.ps1 -WhatIf
server\provision-aws.ps1        # defaults: bloom-teams-production + bloom-teams-sandbox, us-east-1
```

It idempotently creates, per environment:
- S3 bucket `bloom-teams-<env>` — versioning ON, public access blocked, lifecycle rules
  (abort incomplete multipart 7d; expire noncurrent versions under `tc/` per step 1.2).
- IAM role `bloom-teams-broker` — the role the edge functions AssumeRole into to mint
  short-lived, per-request, per-book-prefix-scoped credentials for clients.
- IAM user `bloom-teams-broker-caller` — assume-only (its sole permission is sts:AssumeRole on
  that role). Its access key becomes the edge functions' `AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY`.
- IAM user `bloom-teams-admin` — direct S3 permissions, used ONLY server-side (checksum/
  version-id verification, manifest backup). Key becomes `BLOOM_S3_ADMIN_ACCESS_KEY`/`_SECRET_KEY`.

Before running: review the embedded IAM policy JSON against current least-privilege guidance,
and note the script's own NOTES section (developed against AWS CLI v2, never executed).
**Record the two access-key pairs somewhere safe; they are needed in 2.3.**

### 2.2 [HUMAN] Create the hosted Supabase projects
Two projects (production + sandbox) in the org's Supabase account. For each:
1. `supabase link --project-ref <ref>` from the repo root.
2. `supabase db push` — applies the checked-in migration in `supabase/migrations/` (a generated,
   lossless concatenation of the declarative `supabase/schemas/*.sql`; see CONTRACTS.md →
   "Database: declarative schema") — exactly what the local stack runs. No schema differences exist.
3. `supabase functions deploy` — deploys the same checked-in edge functions
   (`supabase/functions/*`).
4. Record each project's URL and anon key (Dashboard → Settings → API) for 2.4 and Phase 3.

**[ONE-TIME, immediately after the first successful `db push` to ANY hosted project] Freeze the
initial migration.** Until now the database schema has been maintained by *regenerating* the single
initial migration from `supabase/schemas/*.sql` (`build/regen-init-migration.sh`). That is only safe
while every database is disposable. Once a hosted database holds real data, regenerating would
rewrite already-applied history and destroy it. Disarm the regen script by committing its freeze
marker:

```bash
touch supabase/.init-migration-frozen
git add supabase/.init-migration-frozen && git commit -m "Freeze TC init migration (live DB exists)"
```

From this point, `regen-init-migration.sh` refuses to run (it can still be force-overridden with
`ALLOW_INIT_REGEN=1` for a deliberate wipe-and-start-over during early testing), and all schema
changes are **forward-only delta migrations** — edit the `schemas/*.sql` source, then hand-write (or
`supabase db diff` + re-add dropped `COMMENT`/`GRANT` lines) a new migration. See CONTRACTS.md →
"Database: declarative schema".

This is how "Supabase gets access to the S3 buckets": the edge functions run INSIDE Supabase
and read the AWS credentials from Supabase **secrets** (next step). Nothing else links them.

### 2.3 [HUMAN] Set the edge-function secrets (this is the Supabase↔S3 handshake)
For each project:

```bash
supabase secrets set BLOOM_CLOUD_LOCAL_MODE=false
supabase secrets set AWS_ACCESS_KEY_ID=<bloom-teams-broker-caller key>
supabase secrets set AWS_SECRET_ACCESS_KEY=<bloom-teams-broker-caller secret>
supabase secrets set BLOOM_S3_ADMIN_ACCESS_KEY=<bloom-teams-admin key>
supabase secrets set BLOOM_S3_ADMIN_SECRET_KEY=<bloom-teams-admin secret>
supabase secrets set BLOOM_S3_BUCKET=bloom-teams-<env>
supabase secrets set BLOOM_S3_REGION=us-east-1
# do NOT set BLOOM_S3_ENDPOINT in production — its absence selects real AWS endpoints
```

`BLOOM_CLOUD_LOCAL_MODE=false` flips `_shared/env.ts`/`s3.ts` from MinIO-AssumeRole local
credentials to real AWS STS (false is also the default when unset — hosted deployments,
including any future "dev"-named project, never set it). The names mirror the local
`server/dev/functions.env` (which is the committed, local-only-constants version of this
same set).

### 2.4 [AGENT] Security hardening: lock down the `tc.*_tx` RPCs  ← REQUIRED before production
Currently the internal transaction RPCs are EXECUTE-granted to `authenticated` because edge
functions forward the caller's JWT. A member could call e.g. `checkin_finish_tx` directly and
bypass the edge function's S3 checksum verification (blast radius limited to their own
collection, but still). Task: (a) switch the edge functions to use the service-role key with an
explicit verified-user-id parameter, and (b) change the `*_tx` grants in
`supabase/schemas/04_security.sql` to REVOKE `authenticated`. Apply the change per the stage
(pre-launch: edit the schema file, then `build/regen-init-migration.sh`; once a project database
exists: a forward-only delta migration that never edits the already-applied initial migration) —
see CONTRACTS.md → "Database: declarative schema". Verify with a pgTAP test that `authenticated`
can no longer execute them. Human review before deploy.

---

## Phase 3 — Auth implementation (after 1.1; assumes Option A)

### 3.1 [HUMAN] Enable Firebase as third-party auth on both Supabase projects
Dashboard → Authentication → Third-party Auth → add Firebase, pointing at the BloomLibrary
Firebase project. (This is configuration, not code, and needs org access to both consoles.)

### 3.2 [AGENT, other repo] BloomLibrary2 token forwarding
The ~5-line change in BloomLibrary2 `src/editor.ts`: after login, forward the Firebase ID +
refresh tokens to Bloom (the login flow Bloom already hosts in a browser for registration).
Lives in the BloomLibrary2 repo; needs that repo's normal review/deploy cycle.

### 3.3 [AGENT, Firebase] `role: "authenticated"` custom-claim function + backfill
A small Firebase Admin cloud function that stamps the static claim on user creation, plus a
one-time backfill script over existing users. Supabase requires this claim on third-party JWTs.
[HUMAN]: deploy it (Firebase console/CLI credentials) and run the backfill.

### 3.4 [AGENT] Real `CloudAuth` provider in Bloom
Implement the production provider behind the existing `CloudAuth`/`CloudAuthProvider` seam
(`src/BloomExe/TeamCollection/Cloud/`): accept the forwarded Firebase tokens, refresh as
needed, expose the same `GetLoginState`/`SignIn`/`SignOut` surface the dev provider has.
Includes the deferred **persistent token store** (survive Bloom restarts; the dev provider
skips this). `CloudAuthMode.Cloud` already exists as the selector. The server-side
`tc.jwt_email_verified()` helper already isolates the Firebase-vs-GoTrue `email_verified`
claim-shape difference — verify it against a real Firebase JWT and adjust in a new migration
if needed. Claiming an approval requires `email_verified` (all BloomLibrary accounts qualify).

### 3.5 [AGENT] Client production defaults
`CloudEnvironment.cs` compiled defaults currently point at the local stack. Change to: real
production Supabase URL + anon key, empty `DefaultS3Endpoint` (its absence selects real AWS
virtual-hosted style — `S3ForcePathStyle` already keys off this), production bucket,
`CloudAuthMode.Cloud`. Sandbox/dev keep working via the `BLOOM_CLOUDTC_*` env-var overrides
(document a "sandbox profile" env block in server/dev/README.md). Anon keys are public by
design; committing them is fine.

---

## Phase 4 — Verification against real infrastructure (sandbox)

### 4.1 [AGENT] Parity re-verification
Re-run `server/dev/parity-check` against the sandbox bucket + real STS: sha256 checksum
headers, S3 version-id capture on PUT, lifecycle behavior, AssumeRole session-policy scoping.
These were verified against MinIO on the explicit assumption they'd be re-checked on AWS.
[HUMAN]: supply temporary credentials for the run.

### 4.2 [AGENT] E2E matrix against sandbox
Point the E2E harness at sandbox via env vars (the harness's `devStack.ts` values become a
config block) and run the full matrix. Expect to keep per-scenario reset working — that needs
a sandbox-reset path (`supabase db reset --linked` is destructive and slow; an agent should
add a `tc`-schema truncate + bucket-prefix-clear script instead). Also re-run the two
`[Explicit]` C# live tests (`CloudTeamCollectionLiveTests`) with sandbox env vars.

### 4.3 [DECIDED 9 Jul 2026] AWSSDK.S3 version bump: **on this branch, before go-live**
John: "It's fine for this branch to bump the S3 sdk version." [AGENT] executes the bump and
runs the full suites (cloud filter + FULL BloomTests once + the E2E matrix — AWSSDK is also
used by the BloomLibrary web-upload code, so the blast radius is wider than cloud TCs).
**[HUMAN] added to the test plan at John's request: after the bump, manually verify that web
book UPLOAD (publish to bloomlibrary.org) and DOWNLOAD (get a book from bloomlibrary.org
into Bloom) are unaffected.** Queued as item 10 in `orchestration/DOGFOOD-BATCH-1.md`.
EXECUTED 9 Jul 2026 (merged): AWSSDK.S3/Core 3.5.x → 4.0.100.3; MinIO-facing clients pin
checksum behavior to WHEN_REQUIRED (v4's WHEN_SUPPORTED default breaks S3-compatibles);
real-AWS BloomS3Client keeps v4 defaults; full BloomTests baseline-identical (3036/0).
**[HUMAN/cross-repo] BloomHarvester extends BloomS3Client in its own repo — it must take a
matching AWSSDK v4 bump before consuming a Bloom release containing this change.**
Also worth a quick [HUMAN] smoke when convenient: problem-report book upload (YouTrack /
bloom-problem-books bucket) rides the same SDK.

---

## Phase 5 — Product gaps to close before REAL-USER testing

Found during Wave-4 E2E work (see `tasks/09-e2e.md` progress log for full detail). The
experimental-feature flag gates all of this UI, so merging to master does NOT require these —
but giving the feature to real testers does:

- **[DECIDED 9 Jul 2026 → AGENT] Account-switch behavior (E2E-10).** John's full spec is
  recorded as item 9 in `orchestration/DOGFOOD-BATCH-1.md`: local access is unrestricted and
  only shared-data operations are gated by the CURRENT logon; opening a collection joined
  under another account REFUSES (with admin emails + last local editor named) when the new
  logon is not a member, and opens CONNECTED (with atomic checkout takeover on first edit)
  when it is. Supersedes the earlier "block logout with unsent changes" sketch. E2E-10
  becomes the acceptance test once implemented.
- **[DECIDED + IMPLEMENTED 9 Jul 2026] Cloud recovery (E2E-4's blocked half).** John's
  decision (batch item 8): a sync that must overwrite locally-changed content goes ahead but
  first preserves the previous local version as a `.bloomSource` in Lost and Found (+ server
  incident). Implemented as a pure checksum guard in the auto-apply worker and the Sync
  loop (commit e0526fa30); deliberately NOT the persist-checkout-state-to-local-file
  alternative, which was only needed to reproduce folder-TC blocking semantics. Remaining
  [AGENT] follow-up: extend E2E-4's spec to cover the now-reachable preserve path.
- **[DECIDED + IMPLEMENTED 17 Jul 2026] Admin recovery (only admin unavailable).** The
  `members_last_admin_guard` trigger prevents *removing/demoting* a collection's last admin
  (now race-safe — it locks the collection row before counting; `tc.members_last_admin_guard`
  in `supabase/schemas/02_functions.sql`), so a collection cannot be left with zero admin rows
  through normal use.
  It does NOT, and cannot, prevent the sole admin from simply becoming *unavailable* (leaving
  the org, losing their login). John's decision: no forced second-admin (small teams may have
  only one qualified admin); instead the Bloom team recovers such a collection out-of-band with
  the service-role key. Tooling: `tc.support_set_admin(collection_id, email)` (in
  `supabase/schemas/02_functions.sql`) — service-role-only (not callable by `authenticated`), bypasses `is_admin`,
  idempotent (promotes an existing member or inserts a new admin approval).

  **Runbook — restore an admin to a collection that has lost its only reachable one:**
  1. Find the collection id and, if promoting an existing member, confirm the target email:
     ```sql
     select id, name from tc.collections where name ilike '%<name fragment>%';
     select email, role, (user_id is not null) as claimed
       from tc.members where collection_id = '<collection-uuid>';
     ```
  2. Grant admin (run with the **service-role** connection — Supabase Dashboard → SQL editor,
     or a service-role RPC call; a normal signed-in user cannot do this):
     ```sql
     select tc.support_set_admin('<collection-uuid>', '<email-to-make-admin>');
     ```
  3. Tell that person to sign in to Bloom with that email; `claim_memberships` links their
     account to the new admin row, and they can then manage members normally.

  Deliberately left for later (not needed now): a self-service break-glass (e.g. the original
  `collections.created_by` creator reclaiming admin) — revisit only if recovery volume warrants.
- **[DECIDED + IMPLEMENTED 17 Jul 2026 → OPS to schedule] Orphaned-upload sweep.** A check-in
  uploads changed files to S3 (creating new object versions) *before* it commits. If the upload
  succeeds but the commit fails, the garbage upload becomes S3's *current* version while the
  still-referenced committed version is demoted to *noncurrent* — so the `NoncurrentVersionExpiration`
  lifecycle rule (7 d) would eventually delete the version we still need and never touch the
  garbage. John's decision: keep the 7 d lifecycle rule (it correctly GCs genuinely-superseded
  versions) and add a small sweep that fixes only the failed-commit case, accepting the small
  compound risk (failed commit **and** a completed S3 upload **and** the sweep not running for ~7 d).
  Implemented as:
  - `tc.list_stale_upload_garbage()` (in `supabase/schemas/02_functions.sql`, service-role-only) — the
    reference-aware worklist: per-file S3 keys touched by DEAD (aborted/expired) transactions, each
    with the currently-referenced `s3_version_id` as a "delete newer than this" watermark, and
    **excluding** any path a still-live transaction is uploading.
  - `sweep-stale-uploads` edge function — for each worklist key, deletes every S3 version newer
    than the referenced one (all of them if nothing references the key), restoring the committed
    version to *current*. Idempotent; service-role-only.

  **[OPS] Schedule it ~daily.** Any of: (a) `pg_cron` + `pg_net` job that `net.http_post`s the
  function URL with `Authorization: Bearer <service-role key>`; (b) an external cron (e.g. GitHub
  Actions) POSTing the same. Daily is ample — the staleness threshold is the 48 h transaction
  expiry and the lifecycle floor is 7 d, so there is a ~5-day margin. **Monitor the response**: a
  non-zero `referencedMissing` means a referenced version was already gone when the sweep ran
  (i.e. it ran too late) — page on it.

  **Known residual (garbage leak, NOT data loss):** a *new book* whose very first commit fails is
  reaped by `_checkin_reap_book`, which deletes the phantom book row (cascading its transaction),
  so its uploads are no longer reachable via this sweep. Those objects are unreferenced *current*
  versions the 7 d rule never touches — harmless orphans. Close later if wanted by sweeping before
  the reaper deletes the book, or with a periodic full reference-aware GC.
- **[POLICY DECIDED 9 Jul 2026 → AGENT] Subscription-tier check timing.** John: cloud TCs
  require the SAME subscription tier as folder Team Collections — no new policy, reuse the
  existing FeatureName.TeamCollection gate. Remaining [AGENT] work is purely the timing bug:
  `CheckDisablingTeamCollections` can intermittently disconnect a cloud TC when the
  subscription check races cloud sign-in (harness works around it with a test subscription
  code); make the check deterministic for cloud TCs.
- **[DONE 9 Jul 2026] Preview pane refresh on Receive** — fixed by batch item 4+5's
  auto-apply (the worker refreshes the preview when the applied book is selected). Still
  open, nice-to-have: join-conflict states show generic errors (dedicated resolution dialog
  was deferred from task 07).

---

## Phase 6 — Merging `cloud-collections` to master

### 6.0 [PLAN] Split the review/merge into two PRs

Rather than one monorepo PR, the work lands as two, in two repos:

1. **`bloom-core-supabase` repo** — everything that runs *in the cloud* and is not shipped inside
   Bloom desktop: the `tc` database (the declarative `supabase/schemas/`, its generated migration,
   and the `supabase/tests/` pgTAP), the edge functions (`supabase/functions/`),
   `supabase/config.toml`, and the local-stack setup under `server/dev/` (MinIO compose, seed
   users, DEV-CREDENTIALS, `functions.env`). This becomes the source of truth for the backend and
   is deployed via the Supabase CLI independently of Bloom releases.
2. **`bloom-desktop` repo (this one)** — only the desktop client: the `CloudTeamCollection` C#
   classes and `SharingApi`, the React/TS UI, and their unit/component tests.

This supersedes the single review packaging in PR #8052, which stays the interim review vehicle on
`cloud-collections` until the split is executed. (Development stayed in one repo for velocity; the
split happens for release — see `../CloudTeamCollections.md` "Key decisions".)

**[OPEN DECISION] Where the Supabase-dependent test code goes.** Some tests straddle the split:

- *Pure server tests* — pgTAP (`supabase/tests/`) and the Deno edge-function tests — clearly move
  with the backend, into `bloom-core-supabase`.
- *Desktop tests that require a running stack* have no obvious home: the `[Explicit]` C#
  live/integration tests (`src/BloomTests/TeamCollection/Cloud/*LiveTests`, which talk to a local
  Supabase + MinIO) and the E2E harness (`src/BloomTests/e2e/`, which drives a real `Bloom.exe`
  against the local stack). They exercise *desktop* code but cannot run without the backend repo's
  stack.

  Options to choose between:
  - **(a)** Keep them in `bloom-desktop`; its CI pulls and starts the stack from
    `bloom-core-supabase` (Supabase CLI + the checked-in `server/dev/` compose) to run them.
  - **(b)** Move them to `bloom-core-supabase`, alongside the code they lean on — but they depend
    on building `Bloom.exe` / the desktop test assemblies, which live in the other repo.
  - **(c)** Split by dependency: server-only assertions to `bloom-core-supabase`; anything that
    drives desktop code stays in `bloom-desktop`, gated to skip cleanly when no stack is configured.

  Leaning: (a) or (c), since the live/E2E tests are about desktop behavior and belong with the
  desktop code; the only real question is how CI obtains a stack to run them against.

### 6.1 Merge prerequisites

Prerequisites (mostly already true; verify at merge time):

1. [AGENT] Final rebase onto master; full folder-TC regression suite green (the widened
   filter `~Cloud|~TeamCollection|~SharingApi` plus the FULL BloomTests run once); vitest
   suite green; E2E matrix green locally.
2. [HUMAN] Confirm the experimental flag ("Cloud Team Collections (experimental)" in
   Settings → Advanced) is the ONLY way the new UI appears — merged code must be inert for
   everyone else. (Verified in Wave 4; re-verify after rebase.)
3. [AGENT] XLF check: all new strings `translate="no"`, en-only, and **no `--` inside any
   `<note>`** (crashes every launch; rule + history in `.github/skills/xlf-strings/SKILL.md`).
4. [HUMAN] Normal PR review + team heads-up that `server/`, `supabase/`, and
   `src/BloomTests/e2e/` are new top-level areas.
5. [DECIDED 9 Jul 2026] Dogfood plan: NO existing team collections are touched. Create
   fresh test collections, turn them into cloud TCs, have various testers join and try
   things out (against the sandbox infra from Phases 2–4). (Task 10's
   `docs/user-walkthrough.md` is the tester-facing doc; feedback channel: whatever is
   convenient — nothing formal was mandated.)

Merging BEFORE Phases 2–5 are done is fine and useful (code is flag-gated and local-stack
self-sufficient); real-user testing needs Phases 2–4 plus at least the account-switch and
recovery items from Phase 5 triaged.
