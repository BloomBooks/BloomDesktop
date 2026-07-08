# Cloud Team Collections — implementation master checklist

Design: [../CloudTeamCollections.md](../CloudTeamCollections.md) · Contracts: [CONTRACTS.md](CONTRACTS.md)
Rules: agents tick checkboxes **only in their own task file**; this master file is updated
**only by the orchestrator**. Every task PR must build, pass its acceptance tests, and pass
the entire existing folder-TC test suite.

## Local-first development strategy

All development and testing through Wave 4 runs against a **fully local stack**: no real S3
bucket, no hosted Supabase project, and no Firebase/BloomLibrary auth changes are needed to
start. Setup and details live in [tasks/11-local-dev-stack.md](tasks/11-local-dev-stack.md).

- **Database + API**: local Supabase (`supabase start`, Docker) — the identical Postgres
  schema, RLS, RPCs, edge functions (`supabase functions serve`), and realtime that production
  will use. Migrations written now ARE the production migrations. (SQLite rejected: it has no
  PostgREST/RLS/edge-function equivalents, so we would build a parallel backend and then throw
  it away; local Supabase is the product stack itself.)
- **S3 substitute**: MinIO in Docker — an S3-compatible server that stores objects on the
  local file system, with object versioning and checksum support. `BloomS3Client` /
  TransferUtility talk to it through a service-URL override; nothing else in the client
  changes. In dev mode the edge functions return static MinIO credentials **in the same JSON
  shape as the production STS response**, so CONTRACTS.md is unchanged (per-book credential
  scoping is a production security measure, not a functional dependency).
- **Auth substitute**: local GoTrue (bundled with local Supabase) email/password with
  auto-confirm — any email + password signs up/in as a valid, verified user ("a backend that
  accepts any login"), yielding real JWTs so RLS and every RPC run unchanged. Real
  BloomLibrary/Firebase sign-in (Option A/B/C) plugs in later behind the existing `CloudAuth`
  seam; the server side isolates the token-shape difference (Firebase `email_verified` claim
  vs GoTrue confirmation) in one SQL helper, `tc.jwt_email_verified()`.
- **Two instances on one machine**: each Bloom instance gets its own collection folder plus
  `BLOOM_CLOUDTC_USER` / `BLOOM_CLOUDTC_PASSWORD` env-var overrides so it runs as a distinct
  dev identity (bypassing the shared stored-token settings). This is the Wave 3 manual smoke
  and the mechanism the Wave 4 E2E harness scales up.
- **Environment switching**: every external endpoint (Supabase URL, anon key, S3
  endpoint/bucket/path-style, auth mode) resolves through one `CloudEnvironment` config
  (env vars over compiled defaults; owned by task 03). Cutover to the real bucket, hosted
  Supabase, and real sign-in is configuration plus the deferred-infrastructure list below —
  zero protocol or schema change.

## Branching

- Integration branch: `cloud-collections` (base branch: **confirm with John** — master vs the
  active Version6.x branch). Base merged into integration weekly.
- One branch + one git worktree per task; PRs into the integration branch, merged one at a
  time by the orchestrator after code review.

## Waves

| Wave | Tasks | Parallel? | Gate |
|------|-------|-----------|------|
| 0 | [00-enablers](tasks/00-enablers.md) | No — orchestrator-led (shared hot files) | Existing TC suite green, zero behavior change |
| 1 | [11-local-dev-stack](tasks/11-local-dev-stack.md) · [01-server-schema](tasks/01-server-schema.md) · [02-edge-functions](tasks/02-edge-functions.md) · [03-auth](tasks/03-auth.md) · [07-ui-setup](tasks/07-ui-setup.md) | Yes — zero file overlap (contracts frozen first) | Each task's acceptance tests; 11's stack-smoke script green |
| 2 | [04-client-core](tasks/04-client-core.md) · [08-ui-collection-tab](tasks/08-ui-collection-tab.md) | Yes | Unit suites green |
| 3 | [05-cloud-backend](tasks/05-cloud-backend.md) → [06-api-endpoints](tasks/06-api-endpoints.md) → UI wiring | **Sequenced** (shared files) | Two-instance smoke on ONE machine against the local stack |
| 4 | [09-e2e](tasks/09-e2e.md) · [10-adoption](tasks/10-adoption.md) | Yes | Full E2E matrix green against the local stack; dogfood |

## Shared-file schedule (no two concurrent tasks may touch the same one)

| File | Owner |
|------|-------|
| TeamCollection.cs, TeamCollectionManager.cs | Wave 0 only (orchestrator) |
| TeamCollectionApi.cs | 06 only |
| CollectionChooserDialog | 07 only |
| FeatureRegistry.cs, BloomExe.csproj | Orchestrator at merge time |
| supabase/** | 01/02 (01 owns migrations; 02 owns functions/); 11 owns config.toml auth/dev settings + seed |
| server/dev/** (docker-compose, seeds, smoke script, docs) | 11 only |
| Cloud/CloudEnvironment.cs | 03 only |

## Deferred until real infrastructure is available (tracked, NOT blocking)

**The detailed go-live runbook — ordered steps, each tagged [HUMAN] or [AGENT], covering AWS
provisioning, hosted Supabase, the Supabase↔S3 secret handshake, Firebase auth (Option A),
sandbox verification, remaining product gaps, and the merge-to-master checklist — is
[GOING-LIVE.md](GOING-LIVE.md).** The list below remains as the summary index.

Each of these is a config/provisioning swap, not a code change, thanks to the seams above.

- [ ] Auth Option A/B/C decision (colleague review) and, for Option A: the BloomLibrary2
      `src/editor.ts` token-forwarding change + the Firebase Admin claim function (other repos).
      Then: implement the real `CloudAuth` provider behind the existing interface.
- [ ] Run `server/provision-aws` (script is written and reviewed in task 02) against real AWS:
      buckets, versioning, lifecycle, `bloom-teams-broker` role, assume-only IAM user.
- [ ] Create hosted Supabase projects (production + sandbox); `supabase db push` the same
      migrations; deploy the same edge functions; `supabase secrets set` the AWS credentials.
- [ ] Flip edge functions from static-MinIO-credential dev mode to real STS (env switch).
- [ ] Re-verify MinIO/AWS parity assumptions against the real bucket (sha256 checksum headers,
      s3 version-id capture, lifecycle behavior) and re-run the E2E matrix against sandbox.
- [ ] **Security hardening (REQUIRED before production):** the internal `tc.*_tx` RPCs are
      currently EXECUTE-granted to `authenticated` because edge functions forward the
      caller's JWT (identity stamping via auth.jwt()). A member could therefore call e.g.
      `checkin_finish_tx` directly, bypassing the edge function's S3 checksum verification
      (blast radius: corrupting manifests in their own collection only — membership/lock
      still enforced). Before cutover: switch edge functions to the service-role key +
      explicit verified-user-id parameter, then revoke `authenticated` from all `*_tx`
      functions.

## Status

- [x] Wave 0 complete (folder backend provably unchanged — 208/208 TC tests green)
- [x] Wave 1 complete 7 Jul 2026 (local dev stack live; schema+RPCs pgTAP-green; edge
      functions live-verified; auth skeleton w/ dev provider; UI shells tested)
  - [x] 01-server-schema DONE — pgTAP 42/42 green on the live local stack (6 Jul 2026)
  - [x] 11-local-dev-stack DONE — full stack verified on **Podman 5.8.3** (rootful, WSL2,
        Docker-compat pipe) instead of Docker Desktop: MinIO up w/ versioning+lifecycle,
        dev sign-in works, parity spike 4/4, smoke green. README documents the recipe.
  - [x] 02-edge-functions DONE — six functions live-verified (26/26 integration checks on
        the local stack incl. MinIO AssumeRole dev creds); Deno 32/32; provision-aws
        authored-not-run. Found+fixed: get_collection_state new-book leak (01's file);
        edge_runtime per_worker; MinIO joined to the supabase network (gvproxy hang).
  - [x] 03-auth DONE — CloudEnvironment/CloudAuth(dev provider)/CloudCollectionClient;
        46/46 cloud + 244/244 folder-TC tests; live-verified vs local stack. Deferred:
        persistent token store; real provider awaits Option A/B/C; human GUI smoke + 2h soak.
  - [x] 07-ui-setup (shells) DONE — SharingPanel, cloud create dialog, chooser
        "Get my Team Collections", registration email lock; 29/29 component tests;
        XLF en-only. Wiring to real endpoints deferred to Wave 3 (after 06) as planned.
- [x] Wave 2 complete 7 Jul 2026 — 04 client core (83/83 cloud, 281/281 folder-TC);
      08 collection-tab shells (60/60 component tests; folder path zero-extra-requests)
- [x] Wave 3 complete 7 Jul 2026 — GATE PASSED: two-instance manual smoke on one machine
      (checkout contention visible immediately; Send/Receive round trip with real content;
      automatic status propagation via polling). Twelve real bugs found+fixed+pinned during
      the smoke; see merge log.
- [ ] Wave 4 complete
  - [x] 09 harness + E2E-1/E2E-2 DONE 8 Jul 2026 — Playwright-over-CDP harness at
        `src/BloomTests/e2e/` (build-once/launch-many, per-scenario DB+MinIO+scratch reset,
        DB/S3 verification, experimental-flag automation); E2E-1 (1.4 min) and E2E-2
        (2.5 min, the automated two-instance smoke) green on orchestrator re-runs.
        E2E-3..10 still to come (continues on `task/09-e2e` patterns).
  - [x] 10-adoption DONE 8 Jul 2026 — all 7 polish items; see merge log.
- [ ] Real-infrastructure cutover complete (deferred list above)
- [ ] Auth option decided (colleague review — see design doc Open items; **not blocking** —
      dev auth provider ships first)
- [ ] Safety-window duration confirmed (7 days vs 1 day)

## Merge log

(orchestrator appends: date · task · PR · notes)

- 8 Jul 2026 · post-merge E2E gate · direct commits · The gate caught ONE real merge bug:
  task 10's new XLF `<note>` texts contained double hyphens, which L10NSharp parses as
  illegal XML-comment content — EVERY Bloom launch crashed at startup in SetUpLocalization
  (no unit/component test can catch this; only a real launch reads the installed XLF).
  Fixed + rule pinned in .github/skills/xlf-strings/SKILL.md. Remaining gate failures were
  environmental, proven by a pre-merge control run failing identically: (a) a locked Windows
  session stalls WebView2 at about:blank indefinitely (E2E needs an unlocked desktop);
  (b) createCloudTeamCollection deadlocks if posted while the workspace WebView2 is still
  initializing (UI-thread handler + modal progress dialog vs nested message pump — E2E-1
  now uses E2E-2's connect-before-trigger pattern as a guard). Full diagnosis in task 09's
  progress log, findings 7–8.

- 8 Jul 2026 · 09-e2e (harness + E2E-1/2) · merged locally · Harness encodes every smoke-test
  environment rule (Release build MANDATORY — Debug shows a blocking attach-debugger dialog on
  any positional arg; build-once/launch-many; foreign-Bloom fail-loud; per-scenario
  `supabase db reset` + `mc` bucket clear + scratch wipe; user.config flag automation).
  E2E-1 and E2E-2 green on orchestrator re-runs after the agent's runs were starved by
  concurrent sessions (~6GB free RAM — diagnosis confirmed by clean re-run). Two product
  findings REPORTED for follow-up, not fixed: (1) every ReactDialog-hosted WebView2 requests
  the same fixed remote-debugging port, so secondary dialogs are never CDP-reachable —
  harness drives their backend endpoints directly instead; (2) checkout/check-in buttons
  ignore CDP-synthesized clicks (root cause undiagnosed; direct API used). Also found:
  WebView2 temp-profile folders leak per launch (harness cleans them in globalSetup);
  ~1s endpoint-registration race after BLOOM_AUTOMATION_READY (harness retries 404s).
  E2E-3..10 remain; E2E-4 must reproduce the recovery-path NRE.

- 8 Jul 2026 · 10-adoption (+ Wave-3 polish list) · merged locally · All 7 items: proper
  "Cloud Team Collections (experimental)" checkbox in Settings→Advanced (ends the
  user.config hack); pull-down auto-opens the joined collection; un-team cleanup
  (CleanStaleTeamCollectionArtifacts) + TeamCollectionLinkConflictException guard with
  fix-instructions message; first-Receive reconcile verified-by-reading (no checksum
  reconcile happens — matches folder-TC behavior, documented as known limitation);
  user walkthrough doc (Design/CloudTeamCollections/docs/user-walkthrough.md); XLF sweep
  (11 en entries; fixed a TeamCollection.ConflictingCollection id collision); analytics
  audit (cloud join + Receive Updates events added, Backend=Cloud verified elsewhere).
  Agent also found+fixed: Team Collection settings tab was invisible when ONLY the cloud
  flag was on. Orchestrator review fixes: pullDown replied with the collection FOLDER but
  workspace/openCollection needs the .bloomCollection FILE path (renamed field to
  collectionPath); doc-comment placement. Verified: C# widened filter 332/332; vitest
  29/29 on touched files.

- 7 Jul 2026 · two-instance smoke (Wave-3 gate) · direct commits · PASSED after fixing 12
  live-found bugs: members_add scalar-response crash; missing claim_memberships in join;
  identity-model registration-vs-account comparisons (4 sites incl. OkToCheckIn, whose
  false conflict ALSO exposed the unified-recovery clobber + .bloomSource save working);
  'null null' display; BookButton anonymous avatar; missing experimental-feature checkbox
  (flag set via user.config for now — proper Advanced-settings checkbox still owed);
  collection-file mirror-delete stripping TeamCollectionLink.txt; update-Send committing
  changed-files-only manifests (data-loss class — server was right, client wrong; Receive
  now also refuses empty manifests); cloud change events discarded by the .bloom-suffix
  contract; Receive Updates now refreshes UI immediately. Deferred niceties logged:
  selected-book PREVIEW pane doesn't refresh on Receive until reselect (old base-code
  ENHANCE); pull-down doesn't auto-open; recovery-path NRE reproduction (E2E-4).

- 7 Jul 2026 · ui-wiring · merged locally · Dispatcher fixes a live folder-TC create-dialog
  breakage (WireUpForWinforms last-caller-wins; three instances of the bug class fixed,
  regression-tested). Sign-in dialog; SharingPanel + pull-down wiring done. Orchestrator
  fix on cloud-collections: SharingApi claimed-detection treated JSON-null user_id as
  claimed (JTokenType.Null gotcha) — found because SharingApiTests match NO task's filter;
  the widened mandatory filter is now recorded in orchestration/RESUME.md. Post-rebase
  full suite 318/318. Known smoke-test limitations: pull-down doesn't auto-open the new
  collection; join-conflict states show generic errors (matching-flags endpoint TBD);
  real-auth mode intentionally still a placeholder.

- 7 Jul 2026 · 05-cloud-backend · merged locally · Live Send→Receive→lock round trip green.
  Agent's live test found+fixed 2 integration bugs (RestSharp serializer mangling JTokens;
  S3 keys built at wrong prefix level). Orchestrator base fixes at review: AttemptLock now
  honors TryLockInRepo refusal; BookHistoryEventType.WorkPreservedLocally=100. Findings
  routed forward: (a) server stamps locked_by/created_by with auth UUID — 06 adds a
  migration surfacing lockedByEmail/Name for display; (b) collection-file groups have no
  pinned per-file manifest RPC (client reads latest from S3) — acceptable dev-mode gap,
  revisit before production; (c) checkin-start/finish omit the new book's server id —
  client refreshes state post-commit (works; consider contract addition later);
  (d) SyncAtStartup matrix only partially ported — remainder folded into task 09's scope.

- 7 Jul 2026 · 08-ui-collection-tab · merged locally · Survived one session-limit
  interruption (WIP preserved). Orchestrator review fix: the capability/experimental-flag
  hooks fetched per component mount — BookButton would have issued hundreds of identical
  requests per Collection-tab visit; now cached once per page load with test-reset seams
  in vitest.setup. 60/60 re-verified. Wiring of the ~9 mocked endpoints lands with task 06.
  WAVE 2 COMPLETE.

- 7 Jul 2026 · 04-client-core · merged locally · 83/83 cloud (re-verified) + 281/281
  folder-TC. Three findings for later tasks: (1) CONTRACT GAP — no RPC returns a book's
  per-file manifest for Receive; decide at 05 launch (likely additive get_book_manifest
  RPC, CONTRACTS bump) vs reading S3 .manifest.json. (2) AWSSDK.S3 3.5.3.10 predates
  native checksum properties; manual x-amz-checksum-sha256 header live-verified — SDK bump
  is a deliberate SEPARATE follow-up (publish path shares the package). (3) Task 05 must
  point CloudBookTransfer.DownloadFiles at a temp book folder and do the final whole-
  directory swap itself (the class's per-file move loop after full verification is not a
  single atomic dir swap).

- 7 Jul 2026 · 07-ui-setup · merged locally · Orchestrator review fix: the chooser's cloud
  section rendered UNGATED for all users — now behind the experimental feature (re-verified
  29/29 after fix). Registration/settings changes are additive and folder-safe. Deferred to
  Wave 3 wiring (documented in task file): SharingPanel into settings' isTeamCollection
  branch; JoinCloudCollectionDialog's matching logic into the chooser's onPullDown.
  WAVE 1 COMPLETE.

- 7 Jul 2026 · 02-edge-functions · merged locally · Two agent interruptions survived via
  per-step commits (the gvproxy hang the agent later diagnosed was likely the stall cause).
  Independently re-verified Deno 32/32 + pgTAP 42/42. In-place edit of applied migration
  20260706000003 accepted THIS TIME (nothing deployed anywhere yet); convention from now
  on: schema changes to merged migrations arrive as NEW migration files. Deferred-infra
  list gains the *_tx grant hardening item.

- 6 Jul 2026 · 03-auth · merged locally · Reviewed all three classes; one cosmetic fix
  (stranded doc comment). Evidence: 46/46 cloud, 244/244 folder-TC, live GoTrue sign-in +
  RPC error-shape verification, [Explicit] two-concurrent-sessions test green. Note for 04:
  build RPC/edge wrappers on CallRpc/CallEdgeFunction; RPC errors carry Postgres codes
  (typed CONTRACTS codes are edge-function-shaped). Dev default AnonKey is empty — devs set
  BLOOM_CLOUDTC_ANON_KEY from `supabase status` (consider compiling in the stable local
  demo key later).

- 6 Jul 2026 · 00-enablers · merged locally · Reviewed diff line-by-line; seams preserve
  folder behavior; 208/208 TeamCollection tests (24 new) verified against fresh binaries.
  Note: TryLockInRepo gained a BookStatus param vs the task file (avoids redundant GetStatus).
- 6 Jul 2026 · 01-server-schema · merged locally · Orchestrator fixes: checkout_book
  ROW_COUNT type bug; pgTAP errcode; realtime pg_notify→realtime.send TODO (wave 4);
  seed wiring in config.toml. CONTRACTS.md bumped to v1.1 (p_ arg prefix; Content-Profile
  header). pgTAP suite authored but UNRUN (no Docker yet).
- 6 Jul 2026 · 11-local-dev-stack · merged locally · Orchestrator fix: seed bcrypt hash was
  invalid (verified with bcryptjs); replaced with a self-verified hash. Smoke script and
  parity spike authored but UNRUN (no Docker yet); parity-check compiles clean.
- 6 Jul 2026 (later) · runtime verification of 01+11 · direct commits · Full local stack
  verified on Podman (not Docker Desktop). pgTAP 42/42; parity 4/4; smoke green; live RPC
  round-trip OK. Fixes found by running: pgTAP plan count + RLS superuser-bypass (tests);
  parity-check fabricated session token (MinIO validates tokens → DEV-CREDENTIALS spec
  corrected to MinIO AssumeRole); smoke.ps1 PS-5.1 syntax/encoding bugs + JSON `supabase
  status` parsing; compose lifecycle via `mc ilm rule add`; committed .gitkeep in all
  bind-mounted dirs (Podman does not auto-create them).
