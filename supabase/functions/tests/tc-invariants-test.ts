// Cross-file invariant (task 02 acceptance criterion): a check-in/collection-files
// transaction's lifetime must be strictly less than S3's noncurrent-version-expiry
// lifecycle floor. If it weren't, a slow/stalled transaction could still be trying to
// reference an object version that MinIO/S3 has already permanently deleted as a
// noncurrent version — silently corrupting an in-progress check-in.
//
// There is no single source-of-truth constant shared by the SQL (Postgres) and the S3
// lifecycle config (docker-compose.yml locally; server/provision-aws.ps1 in production), so
// this test re-parses both source files at test time rather than hardcoding both sides
// — that way it actually fails loudly if someone changes one without the other, per
// BloomDesktop's AGENTS.md testing philosophy ("test failures indicate what went wrong").
import { assert, assertEquals } from "@std/assert";

const repoRoot = new URL("../../../", import.meta.url); // supabase/functions/tests/ -> repo root

const readText = async (relativePath: string): Promise<string> =>
    await Deno.readTextFile(new URL(relativePath, repoRoot));

/** Extracts every `INTERVAL '<n> hours'` used as a checkin/collection-files transaction
 * expiry (in the schema's initial DEFAULT and both transaction functions' resume-path
 * updates) and asserts they all agree — a mismatch would mean a resumed transaction
 * silently gets a different lifetime than a fresh one. */
Deno.test(
    "invariant: transaction expiry intervals are internally consistent (48h everywhere)",
    async () => {
        const schema = await readText("supabase/schemas/tc/03_tables.sql");
        const txFunctions = await readText(
            "supabase/schemas/tc/02_functions.sql",
        );

        const schemaHours = [
            ...schema.matchAll(
                /expires_at\s+timestamp with time zone DEFAULT \(now\(\) \+ '(\d+):00:00'::interval\)/g,
            ),
        ].map((m) => Number(m[1]));
        const resumeHours = [
            ...txFunctions.matchAll(
                /expires_at\s*=\s*now\(\)\s*\+\s*INTERVAL '(\d+) hours'/g,
            ),
        ].map((m) => Number(m[1]));

        assert(
            schemaHours.length >= 2,
            "expected the expires_at DEFAULT on both checkin_transactions and collection_file_transactions",
        );
        assert(
            resumeHours.length >= 2,
            "expected checkin_start_tx AND collection_files_start_tx resume updates",
        );

        const allHours = [...schemaHours, ...resumeHours];
        for (const h of allHours) {
            assertEquals(
                h,
                allHours[0],
                `all transaction-expiry intervals must match; found ${allHours.join(", ")}`,
            );
        }
    },
);

Deno.test(
    "invariant: the stale-upload sweep's grace period is at least the transaction lifetime",
    async () => {
        const schema = await readText("supabase/schemas/tc/03_tables.sql");
        const txHoursMatch = schema.match(
            /expires_at\s+timestamp with time zone DEFAULT \(now\(\) \+ '(\d+):00:00'::interval\)/,
        );
        assert(
            txHoursMatch,
            "could not find the transaction expires_at default",
        );
        const { UPLOAD_GRACE_MS } = await import(
            "../sweep-stale-uploads/index.ts"
        );
        assert(
            UPLOAD_GRACE_MS >= Number(txHoursMatch[1]) * 60 * 60 * 1000,
            `sweep-stale-uploads UPLOAD_GRACE_MS (${UPLOAD_GRACE_MS} ms) must cover the ` +
                `${txHoursMatch[1]}h transaction lifetime, or it could delete an upload a live ` +
                `transaction is about to commit.`,
        );
    },
);

Deno.test(
    "invariant: transaction lifetime (48h) is strictly less than the S3 noncurrent-version-expiry floor (7d, local MinIO)",
    async () => {
        const schema = await readText("supabase/schemas/tc/03_tables.sql");
        const compose = await readText("server/dev/docker-compose.yml");

        const txHoursMatch = schema.match(
            /expires_at\s+timestamp with time zone DEFAULT \(now\(\) \+ '(\d+):00:00'::interval\)/,
        );
        assert(
            txHoursMatch,
            "could not find the checkin_transactions expires_at default in supabase/schemas/tc/03_tables.sql",
        );
        const txHours = Number(txHoursMatch[1]);

        const noncurrentDaysMatch = compose.match(
            /--noncurrent-expire-days (\d+)/,
        );
        assert(
            noncurrentDaysMatch,
            "could not find --noncurrent-expire-days in server/dev/docker-compose.yml",
        );
        const noncurrentDays = Number(noncurrentDaysMatch[1]);

        assert(
            txHours < noncurrentDays * 24,
            `CONTRACTS.md invariant violated: transaction lifetime (${txHours}h) must be strictly ` +
                `less than the noncurrent-version-expiry floor (${noncurrentDays}d = ${noncurrentDays * 24}h) — ` +
                `otherwise a version an in-flight transaction still references could be permanently deleted ` +
                `out from under it.`,
        );
    },
);
