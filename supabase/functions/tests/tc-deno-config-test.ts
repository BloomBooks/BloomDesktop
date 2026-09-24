// Each Team Collections edge function carries its own deno.json with an import map for the
// AWS SDK, because `supabase functions deploy` bundles a function using the deno.json in
// its own folder. Tests and `deno check` resolve the same bare specifiers through the root
// deno.json instead. This test fails if the two ever pin different versions, so what we
// test is what we deploy.
import { assert, assertEquals } from "@std/assert";

const repoRoot = new URL("../../../", import.meta.url); // supabase/functions/tests/ -> repo root

const tcFunctions = [
    "checkin-abort",
    "checkin-finish",
    "checkin-start",
    "collection-files-finish",
    "collection-files-start",
    "download-start",
    "sweep-stale-uploads",
];

/** Reads and parses a deno.json file, relative to the repo root. */
const readImports = async (
    relativePath: string,
): Promise<Record<string, string>> => {
    const text = await Deno.readTextFile(new URL(relativePath, repoRoot));
    return JSON.parse(text).imports ?? {};
};

Deno.test(
    "tc functions: per-function deno.json imports match the root deno.json pins",
    async () => {
        const rootImports = await readImports("deno.json");
        assert(
            rootImports["@aws-sdk/client-s3"],
            "root deno.json should map @aws-sdk/client-s3",
        );
        for (const fn of tcFunctions) {
            const fnImports = await readImports(
                `supabase/functions/${fn}/deno.json`,
            );
            assert(
                Object.keys(fnImports).length > 0,
                `supabase/functions/${fn}/deno.json should have an imports map`,
            );
            for (const [name, specifier] of Object.entries(fnImports)) {
                assertEquals(
                    specifier,
                    rootImports[name],
                    `${fn}/deno.json pins ${name} differently from the root deno.json`,
                );
            }
        }
    },
);
