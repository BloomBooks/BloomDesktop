# 12 — Real auth provider, Option A client/server seams (post-decision)

**Goal**: Everything Option A (Supabase third-party Firebase auth — DECIDED 8 Jul 2026)
unblocks that lives in THIS repo and needs no live infrastructure. The live wiring (hosted
Supabase third-party-auth config, BloomLibrary2 `editor.ts` forwarding, Firebase deploy +
backfill) stays in GOING-LIVE.md Phases 3.1–3.3.

**Dependencies**: Wave 4 merged (CloudAuth seam + dev provider exist). Owns:
`src/BloomExe/TeamCollection/Cloud/` auth files, `server/firebase/` (new),
one new migration + pgTAP additions if the email_verified check needs it.

## Steps
- [x] **Firebase token provider** behind the existing `CloudAuth`/provider seam
      (`CloudAuthMode.Cloud`): holds a Firebase ID token + refresh token; refreshes via the
      Google securetoken API (`https://securetoken.googleapis.com/v1/token?key=<apiKey>`)
      when the ID token is near/at expiry; exposes the same login-state surface the dev
      provider has (email, signedIn, emailVerified — read from the ID token's claims,
      NOT by trusting the caller). API key + Firebase project id become CloudEnvironment
      values (env-var override + compiled default placeholder). All HTTP mockable; unit
      tests for refresh, expiry parsing, claim extraction, and failure modes.
      DONE 8 Jul 2026: `FirebaseCloudAuthProvider` in CloudAuth.cs (renamed from the
      `RealCloudAuthProvider` stub); `SessionFromIdToken` decodes the JWT payload locally
      (no signature verification -- Supabase verifies server-side on every actual use, see
      the class doc comment) for email/sub/email_verified/exp. Found + fixed in passing: the
      enum was `CloudAuthMode.Real`/"real" but BloomBrowserUI's `sharingApi.ts`
      `SharingLoginMode` type (and this very task file/GOING-LIVE.md) all say "cloud" --
      renamed the enum, wire string, and env var value to `Cloud`/"cloud" throughout so the
      two ends of the API actually agree. `CloudLoginState`/`ISharingLoginState` gained the
      `emailVerified` field the TS side had already declared but C# never populated.
      Tests: FirebaseCloudAuthProviderTests.cs (14 tests, all HTTP mocked via the existing
      FakeRestExecutor) + CloudAuthTests.cs additions for SignInWithExternalTokens/
      EmailVerified/cloud-mode wiring.
- [x] **Persistent token store**: refresh token survives Bloom restarts. Windows DPAPI
      (`ProtectedData`, CurrentUser scope) in a file under the Bloom app-data folder —
      NOT plain text, NOT in user.config. Store/load/clear unit-tested (DPAPI itself may
      need `[Platform(Include = "Win")]`-style guards consistent with existing tests).
      DONE 8 Jul 2026: `DpapiCloudTokenStore` in new file CloudTokenStore.cs, file at
      `ProjectContext.GetBloomAppDataFolder()/CloudTeamCollectionSession.dat`. No
      `[Platform(Include="Win")]` guard needed -- BloomExe and BloomTests both target
      `net8.0-windows` already, so every test run is on Windows; added
      `System.Security.Cryptography.ProtectedData` 6.0.0 (already in the local NuGet
      cache) to BloomExe.csproj. Tests: CloudTokenStoreTests.cs (round-trip, corrupted-file
      recovery, on-disk-encrypted sanity check, clear/no-file-yet).
- [x] **Token receipt endpoint**: the Bloom-side half of BloomLibrary2's ~5-line
      `editor.ts` forwarding. STUDY FIRST how Bloom already hosts the BloomLibrary login
      page and receives its results (WebLibraryIntegration / the existing sign-in flows) and
      reuse that channel's conventions; the endpoint accepts `{idToken, refreshToken}`,
      hands them to the provider, persists, and raises the existing loginState change
      notifications. Document the exact request shape in CONTRACTS.md (new "Auth (Option A)"
      section) so the BloomLibrary2 change can be written against it verbatim.
      DONE 8 Jul 2026: studied `ExternalApi.cs`'s existing `external/login` (the Parse-session
      channel BloomLibraryAuthentication.LogIn's `login-for-editor?port=` page already posts
      back to) and reused its exact conventions -- new sibling endpoint
      `POST /bloom/api/external/cloudLogin` (separate from external/login since the two
      payloads are independent), same OPTIONS/CORS handling and `Shell.ComeToFront()`.
      Delegates the actual sign-in to a new `SharingApi.HandleCloudLoginTokens` (public
      static) so it reuses `CurrentAuth()`/`NotifyClients()` rather than duplicating that
      identity plumbing; fires the same `sharing`/`loginState` websocket event `HandleLogin`
      already does. CONTRACTS.md v1.3: new "Auth (Option A)" section with the exact route/
      body/reply (see below). Not directly unit-tested (SharingApi's other handlers that
      touch the global `CurrentAuth()`/`CurrentClient()` statics aren't either, per
      SharingApiTests.cs's own scope note) -- the logic underneath it
      (`CloudAuth.SignInWithExternalTokens`/`FirebaseCloudAuthProvider.AcceptExternalSession`)
      is fully covered by step 1's tests.
- [x] **`tc.jwt_email_verified()` vs the Firebase claim shape**: Firebase ID tokens carry a
      top-level boolean `email_verified` claim; GoTrue's shape differs. Read the current
      helper; if it does not already handle both, add a NEW migration (never edit merged
      ones) + pgTAP tests feeding both claim shapes via `request.jwt.claims`.
      DONE 8 Jul 2026 -- NO migration needed: `20260706000001_tc_schema.sql`'s
      `tc.jwt_email_verified()` already special-cases exactly this. It checks, in order: (1)
      a top-level `email_verified` claim present (Firebase's real shape -- a real Firebase ID
      token always carries this as a top-level boolean, both before and after the Option A
      role-claim backfill, since that only adds `role`) -- casts it to boolean, which handles
      both a JSON boolean and a JSON string `"true"`/`"false"` since `->>'` always yields the
      claim's text form; (2) else, `role = 'authenticated'` (local GoTrue's auto-confirm
      shape, no `email_verified` claim at all). `supabase/tests/01_tc_schema_test.sql`
      already pgTAP-covers both shapes (cases 1a Firebase-true, 1b Firebase-false, 1c
      local-GoTrue) plus their effect on `claim_memberships()` (cases 4a/4b). Re-ran
      `supabase test db` against the local stack: `Files=1, Tests=42 ... All tests
      successful.` No code change; this bullet's only output is this verification note.
- [x] **Reference Firebase Admin artifacts** under `server/firebase/` (clearly labeled
      "deploy lives in BloomLibrary infrastructure — this is the reviewed reference"):
      (a) an auth-trigger cloud function adding the static `role: "authenticated"` custom
      claim on user creation; (b) a one-time backfill script over existing users;
      (c) README covering deploy + backfill steps and the claim's purpose (Supabase requires
      it on third-party JWTs).
      DONE 8 Jul 2026: `server/firebase/setAuthenticatedClaim.js` (1st-gen
      `functions.auth.user().onCreate` trigger -- no Identity Platform upgrade needed, and
      eventual consistency is fine here), `server/firebase/backfillAuthenticatedClaim.js`
      (paginated `listUsers()` walk, idempotent, dry-run by default / `--apply` to write),
      `server/firebase/README.md` (why the claim exists, deploy+backfill steps, sanity
      check). Not built/tested/linted by this repo (no root JS toolchain covers
      `server/firebase/`; correctly out of scope per the file headers) -- reviewed reference
      only, exactly as the task specifies.
- [x] **Sign-in dialog behavior in cloud mode**: the existing sharing sign-in UI is
      dev-mode email/password. In `CloudAuthMode.Cloud` it must instead route to the
      browser-hosted BloomLibrary login (the same flow the token receipt endpoint completes).
      Wire what is wireable now behind the mode switch; if the full browser flow cannot be
      completed without live pieces, leave a clearly-marked seam + component test of the
      mode switch itself.
      DONE 8 Jul 2026: `SignInDialogBody`'s "cloud" branch now shows a real "Sign In" button
      (was: a static "not available yet" message) that posts to a new
      `sharing/openBrowserSignIn` endpoint, which just calls the existing
      `BloomLibraryAuthentication.LogIn()` (the same browser flow already used for
      BloomLibrary/Parse sign-in -- fully wireable today with no live Firebase/Supabase
      pieces; the dialog closes itself once `external/cloudLogin` completes the sign-in, via
      the same `useSharingLoginState()` mechanism the dev form already relies on). Old
      "SignInNotYetAvailable" string marked obsolete-as-of-6.5 in BloomMediumPriority.xlf;
      new `TeamCollection.Sharing.SignInViaBrowser` string added there (recommended priority:
      medium, since this is feature-specific instructional text for an experimental,
      flag-gated feature -- flagging for confirmation since the xlf-strings skill calls for
      asking, but this session runs autonomously). Vitest: SignInDialog.test.tsx (2 new
      tests: cloud-mode button renders, click calls onOpenBrowserSignIn) -- 5/5 green,
      `--pool=threads` single run. `yarn lint`: 0 errors (778 pre-existing warnings,
      unrelated to these files).

## Acceptance
- All new C# unit-tested (mocked HTTP; no live services); full widened-filter suite green;
  pgTAP green if migrations added. NO Bloom.exe launches and NO E2E runs in this task —
  the machine is in interactive use.

**Agent notes**: Sonnet, MAIN tree (C# build/test), branch `task/12-real-auth`.

## Progress log
- 8 Jul 2026 · done: steps 1-2 (Firebase token provider + DPAPI persistent token store), plus
  the CloudAuthMode.Real->Cloud rename found along the way (see step 1's note) · next: token
  receipt endpoint (step 3) -- study ExternalApi.cs's existing `external/login` handler first,
  then add the new endpoint + CONTRACTS.md "Auth (Option A)" section.
- 8 Jul 2026 · done: steps 3 (token receipt endpoint `external/cloudLogin` + CONTRACTS.md v1.3)
  and 6 (SignInDialog cloud-mode browser button + `sharing/openBrowserSignIn`) · next: step 4
  (`tc.jwt_email_verified()` vs Firebase claim shape -- read the migration, likely already
  correct per its own 1a/1b pgTAP cases, confirm by re-running `supabase test db`) then step 5
  (reference Firebase Admin artifacts under `server/firebase/`).
- 8 Jul 2026 · done: step 4 (verified only -- no migration needed; `supabase test db` green,
  42/42) · next: step 5, reference Firebase Admin artifacts under `server/firebase/` (auth
  trigger claim function + backfill script + README), then the final full-suite verification
  pass and report.
- 8 Jul 2026 · done: step 5 (server/firebase/ reference artifacts) -- ALL SIX STEPS COMPLETE.
  Final verification: mandatory C# filter 359/359 green, pgTAP 42/42 green,
  SignInDialog.test.tsx 5/5 green (`--pool=threads` single run), `yarn lint` 0 errors. NOTE:
  a repo-wide `yarn vitest run teamCollection` (broader than what this task touched) hung
  twice and was killed -- consistent with the known pre-existing Windows vitest-hang issue
  (memory note: parallel vitest hangs after ~15 files; WebSocketManager handles), not a
  regression from this task's changes; not investigated further as out of scope · next: none
  -- task complete; see the final report for what's still deferred to GOING-LIVE.md Phase 3
  (live wiring: hosted Supabase third-party-auth config,
  BloomLibrary2 editor.ts change, actual Firebase deploy+backfill).
