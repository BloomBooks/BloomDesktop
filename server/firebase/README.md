# Cloud Team Collections — Firebase Admin reference artifacts (Option A)

**Deploy lives in BloomLibrary infrastructure — the two `.js` files in this folder are the
reviewed reference, not code this repo builds, tests, or deploys.** Bloom (this repo) only
*consumes* the resulting JWTs (`src/BloomExe/TeamCollection/Cloud/CloudAuth.cs`'s
`FirebaseCloudAuthProvider`); it has no Firebase Admin SDK credentials and should never need
any. See `Design/CloudTeamCollections/GOING-LIVE.md` Phase 3.3 for where this sits in the
overall go-live sequence, and `Design/CloudTeamCollections.md`'s "Auth" bullet for why Option A
was chosen over exchanging the legacy Parse session (Option B) or hand-validating Firebase JWTs
ourselves (Option C).

## Why this claim exists

Supabase is configured to accept Firebase-issued JWTs directly as valid Supabase credentials
("third-party auth" — see GOING-LIVE.md Phase 3.1). Supabase's own policies/RLS machinery
expect every accepted JWT to carry a `role` claim (the same claim a native GoTrue-issued token
always has), and Firebase ID tokens do not carry one by default — BloomLibrary has never needed
a `role` claim on its own tokens before this. Without it, every Cloud Team Collection RPC call
from a real BloomLibrary account would be rejected before Bloom's own `tc.*` RLS policies (which
key off `email_verified`/`sub`, not `role`) even get a chance to run.

The fix is a single static custom claim, `role: "authenticated"`, stamped on every Firebase user
— it is a blanket "yes, this is an authenticated BloomLibrary user" marker for Supabase's
benefit, **not** a role/permission system of its own. Bloom's own authorization (who is an
admin vs. member of a given collection, who can claim an approval, etc.) is entirely decided by
`tc.*` RLS policies and the `tc.jwt_email_verified()`/`tc.current_user_id()` helpers in
`supabase/migrations/20260706000001_tc_schema.sql` — this claim only gets a token in the door.

## Files

- **`setAuthenticatedClaim.js`** — a Firebase Auth `onCreate` trigger that stamps the claim on
  every NEW user from the moment it is deployed. 1st-gen (`functions.auth.user().onCreate`),
  deliberately: it needs no Identity Platform upgrade, and "eventually consistent within a few
  seconds of sign-up" is fine (nothing needs the claim present on the very first ID token ever
  minted — a user always finishes BloomLibrary sign-up long before they open Bloom's Cloud Team
  Collection sign-in dialog).
- **`backfillAuthenticatedClaim.js`** — a one-time script that walks every EXISTING user (via
  `listUsers()`, paginated) and stamps the same claim on any that don't already have it. The
  trigger above only fires for accounts created after it is deployed; every account that
  predates it needs this run once. Idempotent and safe to re-run (skips users that already carry
  the claim); defaults to a dry run, pass `--apply` to actually write.

## Deploy + backfill steps [HUMAN — needs Firebase console/CLI credentials for the BloomLibrary project]

1. Copy `setAuthenticatedClaim.js`'s function body into the BloomLibrary infrastructure repo's
   real Firebase Functions source (adjust the `firebase-functions`/`firebase-admin` import style
   to whatever SDK version that project pins — this reference uses the current v1 trigger API).
2. Deploy it: `firebase deploy --only functions:setAuthenticatedClaim` (or that project's
   equivalent deploy command).
3. Run the backfill ONCE against production, from an environment with Admin SDK credentials for
   the BloomLibrary Firebase project:
   ```bash
   GOOGLE_APPLICATION_CREDENTIALS=/path/to/service-account.json node backfillAuthenticatedClaim.js
   # review the dry-run counts printed above, then:
   GOOGLE_APPLICATION_CREDENTIALS=/path/to/service-account.json node backfillAuthenticatedClaim.js --apply
   ```
4. Sanity-check: sign in to Bloom's Cloud Team Collection sign-in dialog with a pre-existing
   BloomLibrary account and confirm `sharing/loginState` reports `signedIn: true` with the
   correct email (see `CONTRACTS.md`'s "Auth (Option A)" section for the endpoint that
   completes this).

## Local dev stack has no equivalent

The local Supabase dev stack (`server/dev/`) uses local GoTrue, not Firebase third-party auth,
so nothing in this folder is exercised by local development or the mandatory C# test filter —
`tc.jwt_email_verified()` already branches on `role = 'authenticated'` for the local-GoTrue
shape independently of this claim (see that function's own comment in
`20260706000001_tc_schema.sql`).
