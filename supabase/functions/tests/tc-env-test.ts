// Unit tests for _shared/tc/env.ts's S3 endpoint rule: required in local mode (MinIO), optional
// in a hosted deployment, where leaving BLOOM_S3_ENDPOINT unset (as GOING-LIVE.md 2.3 says to)
// must select the AWS SDK's default endpoint rather than fail.
import { assertEquals, assertThrows } from "@std/assert";
import { setTestEnv } from "../_shared/tc/test_support.ts";

setTestEnv();
const { s3Env } = await import("../_shared/tc/env.ts");
const { adminS3Client } = await import("../_shared/tc/s3.ts");

/** Runs `fn` with the given env vars set (undefined = unset), restoring the previous values. */
const withEnv = async (
    vars: Record<string, string | undefined>,
    fn: () => void | Promise<void>,
): Promise<void> => {
    const saved = Object.fromEntries(
        Object.keys(vars).map((k) => [k, Deno.env.get(k)]),
    );
    const apply = (values: Record<string, string | undefined>) => {
        for (const [k, v] of Object.entries(values)) {
            if (v === undefined) Deno.env.delete(k);
            else Deno.env.set(k, v);
        }
    };
    apply(vars);
    try {
        await fn();
    } finally {
        apply(saved);
    }
};

Deno.test("s3Env: local mode uses the configured MinIO endpoint", async () => {
    await withEnv(
        {
            BLOOM_CLOUD_LOCAL_MODE: "true",
            BLOOM_S3_ENDPOINT: "http://minio.invalid:9000",
        },
        () => {
            assertEquals(s3Env().endpoint, "http://minio.invalid:9000");
        },
    );
});

Deno.test(
    "s3Env: local mode without BLOOM_S3_ENDPOINT fails fast",
    async () => {
        await withEnv(
            { BLOOM_CLOUD_LOCAL_MODE: "true", BLOOM_S3_ENDPOINT: undefined },
            () => {
                assertThrows(
                    () => s3Env(),
                    Error,
                    "Missing required environment variable: BLOOM_S3_ENDPOINT",
                );
            },
        );
    },
);

Deno.test(
    "s3Env: hosted (non-local) mode without BLOOM_S3_ENDPOINT gives undefined, and the admin client then targets real AWS",
    async () => {
        await withEnv(
            {
                BLOOM_CLOUD_LOCAL_MODE: "false",
                BLOOM_S3_ENDPOINT: undefined,
                BLOOM_S3_REGION: "us-east-1",
                BLOOM_S3_FORCE_PATH_STYLE: "false",
                BLOOM_S3_ADMIN_ACCESS_KEY: "admin-key",
                BLOOM_S3_ADMIN_SECRET_KEY: "admin-secret",
            },
            async () => {
                assertEquals(s3Env().endpoint, undefined);
                // Sanity check that constructing the client works and resolves the SDK's own
                // default endpoint (not a MinIO one) for the region.
                const client = adminS3Client();
                const endpoint = await client.config.endpointProvider(
                    { Region: "us-east-1", Bucket: "bloom-teams-test" },
                    {},
                );
                assertEquals(
                    endpoint.url.hostname.endsWith("amazonaws.com"),
                    true,
                    `expected an AWS endpoint, got ${endpoint.url.href}`,
                );
            },
        );
    },
);

Deno.test(
    "s3Env: hosted mode still honors an explicitly configured endpoint",
    async () => {
        await withEnv(
            {
                BLOOM_CLOUD_LOCAL_MODE: "false",
                BLOOM_S3_ENDPOINT: "https://s3.us-east-1.amazonaws.com",
            },
            () => {
                assertEquals(
                    s3Env().endpoint,
                    "https://s3.us-east-1.amazonaws.com",
                );
            },
        );
    },
);
