// Cloud Team Collections -- Option A (decided 8 Jul 2026): reference Firebase Admin artifact.
//
// *** THIS FILE IS NOT RUN FROM THIS REPO. ***
// It is the reviewed reference for a ONE-TIME script [HUMAN] runs from the BloomLibrary
// infrastructure side (wherever a Firebase Admin SDK service-account key for the BloomLibrary
// Firebase project is available) after deploying setAuthenticatedClaim.js (this same folder).
// See README.md in this folder for the full story.
//
// WHAT THIS DOES
// setAuthenticatedClaim.js only fires for NEW sign-ups from the moment it is deployed. Every
// BloomLibrary account created before that moment needs the same `role: "authenticated"`
// custom claim stamped on it once, by hand, or those users would sign in to Bloom's Cloud Team
// Collections feature and get rejected by Supabase (no `role` claim on their JWT). This script
// walks every existing user via the Admin SDK's paginated listUsers() and stamps the claim on
// any that don't already have it.
//
// Idempotent and safe to re-run: it skips users that already carry the claim (e.g. everyone
// created after the trigger went live, or a user this script already touched), and it never
// removes or overwrites any OTHER custom claim a user might already have (there are none
// today, per setAuthenticatedClaim.js's own comment, but this stays correct if that changes).
//
// USAGE (from the BloomLibrary infrastructure repo/environment with Admin SDK credentials):
//   GOOGLE_APPLICATION_CREDENTIALS=/path/to/service-account.json node backfillAuthenticatedClaim.js
//
// This is a dry-run-first tool: pass --apply to actually write claims; without it, the script
// only reports how many users would be changed, matching the "review before you act" spirit of
// server/provision-aws.ps1's -WhatIf switch elsewhere in this repo.

const admin = require("firebase-admin");

if (!admin.apps.length) {
    admin.initializeApp();
}

async function backfillAuthenticatedClaim(apply) {
    let pageToken = undefined;
    let scanned = 0;
    let alreadyHadClaim = 0;
    let updated = 0;
    const failures = [];

    do {
        const page = await admin.auth().listUsers(1000, pageToken);
        for (const user of page.users) {
            scanned++;
            const existingClaims = user.customClaims || {};
            if (existingClaims.role === "authenticated") {
                alreadyHadClaim++;
                continue;
            }

            if (apply) {
                try {
                    await admin.auth().setCustomUserClaims(user.uid, {
                        ...existingClaims,
                        role: "authenticated",
                    });
                    updated++;
                } catch (e) {
                    failures.push({
                        uid: user.uid,
                        email: user.email,
                        error: String(e),
                    });
                }
            } else {
                updated++; // "would update" count in dry-run mode
            }
        }
        pageToken = page.pageToken;
    } while (pageToken);

    console.log(
        `Scanned ${scanned} user(s): ${alreadyHadClaim} already had the claim, ` +
            `${updated} ${apply ? "updated" : "would be updated"}.`,
    );
    if (failures.length > 0) {
        console.error(`${failures.length} user(s) FAILED to update:`, failures);
        process.exitCode = 1;
    }
}

const apply = process.argv.includes("--apply");
if (!apply) {
    console.log(
        "Dry run (no claims will be written). Pass --apply to actually update users.\n",
    );
}
backfillAuthenticatedClaim(apply).catch((e) => {
    console.error("backfillAuthenticatedClaim failed:", e);
    process.exitCode = 1;
});
