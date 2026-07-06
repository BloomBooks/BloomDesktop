# 03 — Auth (Wave 1)

**Goal**: `CloudAuth` + `CloudCollectionClient` skeleton behind one interface, with a **dev
auth provider** (local GoTrue email/password against task 11's stack) as the first concrete
implementation. Real BloomLibrary/Firebase sign-in (Option A/B/C) is a later drop-in provider —
the decision is **not blocking** for this task or anything downstream.

**Dependencies**: CONTRACTS.md; task 11's env-var contract. The Option A/B/C decision
(colleague review — see design doc) is deferred to the real-infrastructure cutover; the
interface is option-agnostic. Owns new files
`src/BloomExe/TeamCollection/Cloud/CloudEnvironment.cs`, `CloudAuth.cs`,
`CloudCollectionClient.cs`. (When the real option lands: if Option A, the BloomLibrary2
`src/editor.ts` change and the Firebase Admin claim function live in their own repos —
coordinate, do not fork here.)

## Steps
- [x] `CloudEnvironment`: one place resolving Supabase URL, anon key, S3 endpoint/bucket/
      path-style, and auth mode from the `BLOOM_CLOUDTC_*` env vars (names per task 11's
      README) over compiled defaults. Everything cloud-related reads config from here;
      switching local ↔ sandbox ↔ production is config only.
- [x] `CloudAuth` interface + session core (provider-agnostic): token store, proactive refresh
      (timer at ~80% TTL + on-401), sign-out, "who am I" (email/user id), account-switch
      detection hook.
- [x] **Dev provider** (`AUTH_MODE=dev`): sign in = GoTrue password grant against the local
      stack; unknown email ⇒ sign-up (auto-confirmed) then sign in — i.e. any login is
      accepted. Honors `BLOOM_CLOUDTC_USER`/`BLOOM_CLOUDTC_PASSWORD` for silent auto-sign-in,
      **bypassing shared stored tokens** — this is what lets two Bloom instances on one
      machine run as two different users.
- [x] Real-provider seam (`AUTH_MODE=real`): stub that surfaces "not yet available"; the
      `external/login` payload hook (`ExternalApi.LoginSuccessful`) and refresh-token
      user-setting (alongside LastLoginSessionToken) are wired but inert until the Option
      A/B/C provider is implemented (deferred-infrastructure list).
- [x] `CloudCollectionClient`: RestSharp client for RPCs + edge functions per CONTRACTS.md
      (model on `BloomLibraryBookApiClient`), bearer injection, ClientOutOfDate surfacing,
      typed errors (LockHeldByOther etc.).
- [x] `sharing/loginState` endpoint groundwork (used by UI tasks; reports mode + identity so
      dev-mode sign-in can be a plain email/password form instead of the browser flow).

## Acceptance
- `CloudAuthTests`: refresh on timer/401; refresh failure mid-operation aborts cleanly and
  surfaces "please sign in"; account-switch detection hook; env-override identity wins over
  stored tokens.
- Client tests: bearer attached; typed error mapping; out-of-date handling.
- Manual (local stack): two Bloom instances with different `BLOOM_CLOUDTC_USER` values hold
  two distinct valid sessions simultaneously; a session survives > 2h (refresh soak).

**Agent notes**: Sonnet. Editing a checked-out book must NEVER block on auth. Keep the dev
provider tiny — it must be deletable without touching the session core.

## Progress log

- 6 Jul 2026 · done: CloudEnvironment/CloudAuth/CloudCollectionClient skeleton implemented and
  building clean (BloomExe.csproj auto-globs new .cs files, no csproj edit needed); dev-provider
  GoTrue calls live-verified against the local stack (sign-in, RPC with Content-Profile: tc,
  401/error shapes). All "Steps" checkboxes ticked. · next: write CloudAuthTests +
  CloudCollectionClientTests under src/BloomTests/TeamCollection/Cloud/ covering the Acceptance
  section, then run `dotnet test --filter FullyQualifiedName~Cloud` and the TeamCollection
  regression filter.
- 6 Jul 2026 (later) · done: full Acceptance test suite written and green —
  CloudAuthTests/CloudCollectionClientTests/CloudEnvironmentTests (36 tests: mocked
  ICloudAuthProvider/IRestExecutor fakes cover refresh-on-timer, refresh-on-401 success/failure,
  account-switch, env-override-wins-over-stored-session, bearer injection, and every
  CloudErrorCode mapping incl. ClientOutOfDate); plus one `[Explicit]` test
  (LiveDevProvider_TwoUsersSignInConcurrently_HoldDistinctSessions) that live-verified alice/bob
  get independent sessions + independent refreshes against the running local Supabase stack.
  Full `FullyQualifiedName~Cloud` filter: 46/46 green. Full `FullyQualifiedName~TeamCollection`
  regression filter: 244/244 green (no folder-TC regression). Task complete except the
  multi-hour manual two-window/>2h-soak items in Acceptance, which need a human at a keyboard —
  see the final report for what was substituted. · next: none for this task; ready for
  orchestrator review/merge. Downstream: task 04 (client-core) builds the actual RPC/edge-function
  method wrappers on top of CloudCollectionClient.CallRpc/CallEdgeFunction.
