// Cloud Team Collections -- Option A (decided 8 Jul 2026): reference Firebase Admin artifact.
//
// *** THIS FILE IS NOT DEPLOYED FROM THIS REPO. ***
// Deploy lives in BloomLibrary infrastructure -- this is the reviewed reference the actual
// Firebase Functions deployment (in the BloomLibrary/bloom-parse-server infrastructure repo,
// wherever its `functions/` directory lives) should be written from. See README.md in this
// folder for the full deploy + backfill story and why this claim exists at all.
//
// WHAT THIS DOES
// Supabase's third-party Firebase auth (the mechanism Bloom's CloudAuth relies on -- see
// Design/CloudTeamCollections/GOING-LIVE.md Phase 3.1) requires every accepted JWT to carry a
// `role` claim; Supabase Postgres policies key off `auth.jwt() ->> 'role'` the same way they
// would for a native GoTrue-issued token. Firebase ID tokens don't carry a `role` claim by
// default -- BloomLibrary has never needed one before Option A. This trigger stamps the static
// custom claim `role: "authenticated"` onto every user the moment their Firebase Auth account
// is created, so every ID token minted for them from then on carries it automatically (Firebase
// merges custom claims into the ID token on each mint -- no per-sign-in code needed).
//
// This is intentionally the ONLY thing this trigger does. It is a blanket "yes, you are an
// authenticated BloomLibrary user" marker, not a role/permission system -- Bloom's own
// `tc.*` RLS policies are what actually decide what a signed-in user may do with a given
// collection (see supabase/migrations/20260706000001_tc_schema.sql).
//
// DEPLOY (from the BloomLibrary infrastructure repo that owns the real `functions/` project)
//   1. Copy this file's function body into that project's functions source (adjust the
//      import style to whatever Firebase Functions SDK version that project pins).
//   2. `firebase deploy --only functions:setAuthenticatedClaim` (or equivalent per that
//      project's deploy tooling).
//   3. Run backfillAuthenticatedClaim.js (this same folder) ONCE against production to stamp
//      the claim on every account that existed before this trigger went live -- new
//      sign-ups only get it going forward; the trigger never fires retroactively.
//
// Uses the 1st-gen `functions.auth.user().onCreate` trigger (not a 2nd-gen
// `identity.beforeUserCreated` blocking function) deliberately: it needs no Identity Platform
// upgrade, and "eventually consistent within a few seconds of sign-up" is fine here -- unlike a
// blocking function, nothing in this flow needs the claim to be present on the very first ID
// token minted (BloomLibrary's own sign-in completes long before the user ever opens Bloom's
// Cloud Team Collection sign-in dialog).

const functions = require("firebase-functions/v1");
const admin = require("firebase-admin");

if (!admin.apps.length) {
    admin.initializeApp();
}

exports.setAuthenticatedClaim = functions.auth.user().onCreate(async (user) => {
    // Merge with any existing custom claims (none exist today, but this stays correct if a
    // future claim is ever added) rather than overwriting them outright.
    const existingClaims = user.customClaims || {};
    await admin.auth().setCustomUserClaims(user.uid, {
        ...existingClaims,
        role: "authenticated",
    });
});
