# 12 — Real auth provider, Option A client/server seams (post-decision)

**Goal**: Everything Option A (Supabase third-party Firebase auth — DECIDED 8 Jul 2026)
unblocks that lives in THIS repo and needs no live infrastructure. The live wiring (hosted
Supabase third-party-auth config, BloomLibrary2 `editor.ts` forwarding, Firebase deploy +
backfill) stays in GOING-LIVE.md Phases 3.1–3.3.

**Dependencies**: Wave 4 merged (CloudAuth seam + dev provider exist). Owns:
`src/BloomExe/TeamCollection/Cloud/` auth files, `server/firebase/` (new),
one new migration + pgTAP additions if the email_verified check needs it.

## Steps
- [ ] **Firebase token provider** behind the existing `CloudAuth`/provider seam
      (`CloudAuthMode.Cloud`): holds a Firebase ID token + refresh token; refreshes via the
      Google securetoken API (`https://securetoken.googleapis.com/v1/token?key=<apiKey>`)
      when the ID token is near/at expiry; exposes the same login-state surface the dev
      provider has (email, signedIn, emailVerified — read from the ID token's claims,
      NOT by trusting the caller). API key + Firebase project id become CloudEnvironment
      values (env-var override + compiled default placeholder). All HTTP mockable; unit
      tests for refresh, expiry parsing, claim extraction, and failure modes.
- [ ] **Persistent token store**: refresh token survives Bloom restarts. Windows DPAPI
      (`ProtectedData`, CurrentUser scope) in a file under the Bloom app-data folder —
      NOT plain text, NOT in user.config. Store/load/clear unit-tested (DPAPI itself may
      need `[Platform(Include = "Win")]`-style guards consistent with existing tests).
- [ ] **Token receipt endpoint**: the Bloom-side half of BloomLibrary2's ~5-line
      `editor.ts` forwarding. STUDY FIRST how Bloom already hosts the BloomLibrary login
      page and receives its results (WebLibraryIntegration / the existing sign-in flows) and
      reuse that channel's conventions; the endpoint accepts `{idToken, refreshToken}`,
      hands them to the provider, persists, and raises the existing loginState change
      notifications. Document the exact request shape in CONTRACTS.md (new "Auth (Option A)"
      section) so the BloomLibrary2 change can be written against it verbatim.
- [ ] **`tc.jwt_email_verified()` vs the Firebase claim shape**: Firebase ID tokens carry a
      top-level boolean `email_verified` claim; GoTrue's shape differs. Read the current
      helper; if it does not already handle both, add a NEW migration (never edit merged
      ones) + pgTAP tests feeding both claim shapes via `request.jwt.claims`.
- [ ] **Reference Firebase Admin artifacts** under `server/firebase/` (clearly labeled
      "deploy lives in BloomLibrary infrastructure — this is the reviewed reference"):
      (a) an auth-trigger cloud function adding the static `role: "authenticated"` custom
      claim on user creation; (b) a one-time backfill script over existing users;
      (c) README covering deploy + backfill steps and the claim's purpose (Supabase requires
      it on third-party JWTs).
- [ ] **Sign-in dialog behavior in cloud mode**: the existing sharing sign-in UI is
      dev-mode email/password. In `CloudAuthMode.Cloud` it must instead route to the
      browser-hosted BloomLibrary login (the same flow the token receipt endpoint completes).
      Wire what is wireable now behind the mode switch; if the full browser flow cannot be
      completed without live pieces, leave a clearly-marked seam + component test of the
      mode switch itself.

## Acceptance
- All new C# unit-tested (mocked HTTP; no live services); full widened-filter suite green;
  pgTAP green if migrations added. NO Bloom.exe launches and NO E2E runs in this task —
  the machine is in interactive use.

**Agent notes**: Sonnet, MAIN tree (C# build/test), branch `task/12-real-auth`.

## Progress log
