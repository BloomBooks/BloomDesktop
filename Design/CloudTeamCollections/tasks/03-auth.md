# 03 — Auth (Wave 1)

**Goal**: `CloudAuth` + `CloudCollectionClient` skeleton: a Supabase session from the existing
BloomLibrary browser login, with refresh, behind one interface.

**Dependencies**: CONTRACTS.md; **the Option A/B/C decision** (colleague review — see design
doc; the interface is option-agnostic, so skeleton work can start immediately).
Owns new files `src/BloomExe/TeamCollection/Cloud/CloudAuth.cs`, `CloudCollectionClient.cs`
(+ if Option A: the BloomLibrary2 `src/editor.ts` change and the Firebase Admin claim
function live in their own repos — coordinate, do not fork here).

## Steps
- [ ] `CloudAuth`: obtain/store tokens from the `external/login` payload (hook
      `ExternalApi.LoginSuccessful`); proactive refresh (timer at ~80% TTL + on-401); sign-out;
      "who am I" (email/user id); option-specific internals isolated.
- [ ] `CloudCollectionClient`: RestSharp client for RPCs + edge functions per CONTRACTS.md
      (model on `BloomLibraryBookApiClient`), bearer injection, ClientOutOfDate surfacing,
      typed errors (LockHeldByOther etc.).
- [ ] Settings storage for the refresh token (new user-setting alongside LastLoginSessionToken).
- [ ] `sharing/loginState` endpoint groundwork (used by UI tasks).

## Acceptance
- `CloudAuthTests`: refresh on timer/401; refresh failure mid-operation aborts cleanly and
  surfaces "please sign in"; account-switch detection hook.
- Client tests: bearer attached; typed error mapping; out-of-date handling.
- Manual: sign in via bloomlibrary.org, hold a valid Supabase session > 2h (soak).

**Agent notes**: Sonnet. Editing a checked-out book must NEVER block on auth.
