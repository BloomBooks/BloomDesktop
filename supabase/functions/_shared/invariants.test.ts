// Cross-file invariant (task 02 acceptance criterion): a check-in/collection-files
// transaction's lifetime must be strictly less than S3's noncurrent-version-expiry
// lifecycle floor. If it weren't, a slow/stalled transaction could still be trying to
// reference an object version that MinIO/S3 has already permanently deleted as a
// noncurrent version — silently corrupting an in-progress check-in.
//
// There is no single source-of-truth constant shared by the SQL (Postgres) and the S3
// lifecycle config (docker-compose.yml locally; server/provision-aws in production), so
// this test re-parses both source files at test time rather than hardcoding both sides
// — that way it actually fails loudly if someone changes one without the other, per
// AGENTS.md's testing philosophy ("test failures indicate what went wrong").
import { assert, assertEquals } from "jsr:@std/assert@1";

const repoRoot = new URL("../../../", import.meta.url); // supabase/functions/_shared/ -> repo root

const readText = async (relativePath: string): Promise<string> =>
    await Deno.readTextFile(new URL(relativePath, repoRoot));

/** Extracts every `INTERVAL '<n> hours'` used as a checkin/collection-files transaction
 * expiry (in the schema's initial DEFAULT and both transaction functions' resume-path
 * updates) and asserts they all agree — a mismatch would mean a resumed transaction
 * silently gets a different lifetime than a fresh one. */
Deno.test("invariant: transaction expiry intervals are internally consistent (48h everywhere)", async () => {
    const schema = await readText("supabase/migrations/20260706000001_tc_schema.sql");
    const txFunctions = await readText("supabase/migrations/20260706000004_tc_checkin_txn_functions.sql");

    const schemaHours = [...schema.matchAll(/expires_at\s+timestamptz[^,]*INTERVAL '(\d+) hours'/g)]
        .map((m) => Number(m[1]));
    const resumeHours = [...txFunctions.matchAll(/expires_at\s*=\s*now\(\)\s*\+\s*INTERVAL '(\d+) hours'/g)]
        .map((m) => Number(m[1]));

    assert(schemaHours.length >= 1, "expected to find at least one expires_at DEFAULT INTERVAL in the schema");
    assert(resumeHours.length >= 2, "expected checkin_start_tx AND collection_files_start_tx resume updates");

    const allHours = [...schemaHours, ...resumeHours];
    for (const h of allHours) {
        assertEquals(h, allHours[0], `all transaction-expiry intervals must match; found ${allHours.join(", ")}`);
    }
});

Deno.test("invariant: transaction lifetime (48h) is strictly less than the S3 noncurrent-version-expiry floor (7d, dev MinIO)", async () => {
    const schema = await readText("supabase/migrations/20260706000001_tc_schema.sql");
    const compose = await readText("server/dev/docker-compose.yml");

    const txHoursMatch = schema.match(/expires_at\s+timestamptz[^,]*INTERVAL '(\d+) hours'/);
    assert(txHoursMatch, "could not find the checkin_transactions expires_at default in the schema migration");
    const txHours = Number(txHoursMatch[1]);

    const noncurrentDaysMatch = compose.match(/--noncurrent-expire-days (\d+)/);
    assert(noncurrentDaysMatch, "could not find --noncurrent-expire-days in server/dev/docker-compose.yml");
    const noncurrentDays = Number(noncurrentDaysMatch[1]);

    assert(
        txHours < noncurrentDays * 24,
        `CONTRACTS.md invariant violated: transaction lifetime (${txHours}h) must be strictly ` +
            `less than the noncurrent-version-expiry floor (${noncurrentDays}d = ${noncurrentDays * 24}h) — ` +
            `otherwise a version an in-flight transaction still references could be permanently deleted ` +
            `out from under it.`,
    );
});
