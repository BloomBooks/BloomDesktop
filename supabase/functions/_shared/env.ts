// Environment / configuration helpers shared by every Cloud Team Collections edge
// function. Centralised here so the dev/prod credential-provider seam (see
// server/dev/DEV-CREDENTIALS.md) lives in exactly one place.

/** Reads a required env var; throws (fails fast) if missing — see AGENTS.md testing
 * philosophy: don't silently work around a missing dependency. */
export const requireEnv = (name: string): string => {
    const value = Deno.env.get(name);
    if (!value) {
        throw new Error(`Missing required environment variable: ${name}`);
    }
    return value;
};

/** Optional env var with a default. */
export const optionalEnv = (name: string, fallback: string): string =>
    Deno.env.get(name) ?? fallback;

/** True when running against the local dev stack (MinIO via AssumeRole) rather than
 * real AWS. Mirrors server/dev/DEV-CREDENTIALS.md's `BLOOM_DEV_MODE` secret. */
export const isDevMode = (): boolean => optionalEnv("BLOOM_DEV_MODE", "false") === "true";

/** Supabase project URL + anon key, auto-injected by the Supabase CLI/platform into
 * every edge function's environment — used to call PostgREST RPCs with the caller's
 * OWN forwarded JWT (never the service-role key; see the migration's header comment
 * for why that is both sufficient and correct here). */
export const supabaseUrl = (): string => requireEnv("SUPABASE_URL");
export const supabaseAnonKey = (): string => requireEnv("SUPABASE_ANON_KEY");

/** S3 / MinIO connection details. */
export interface S3Env {
    endpoint: string;
    bucket: string;
    region: string;
    forcePathStyle: boolean;
}

export const s3Env = (): S3Env => ({
    endpoint: requireEnv("BLOOM_S3_ENDPOINT"),
    bucket: requireEnv("BLOOM_S3_BUCKET"),
    region: optionalEnv("BLOOM_S3_REGION", "us-east-1"),
    // MinIO requires path-style; real AWS uses virtual-hosted style. Dev mode always
    // forces path-style; production can opt out via BLOOM_S3_FORCE_PATH_STYLE=false.
    forcePathStyle: isDevMode() || optionalEnv("BLOOM_S3_FORCE_PATH_STYLE", "true") === "true",
});

/** Root/admin credentials used ONLY server-side (never sent to a client) to call
 * MinIO's AssumeRole STS endpoint in dev mode, and for admin S3 operations
 * (HeadObject / GetObjectAttributes verification, .manifest.json writes). */
export const minioRootCredentials = () => ({
    accessKeyId: optionalEnv("BLOOM_S3_ROOT_ACCESS_KEY", "minioadmin"),
    secretAccessKey: optionalEnv("BLOOM_S3_ROOT_SECRET_KEY", "minioadmin"),
});

/** Production broker configuration: the "assume-only" IAM user's credentials and the
 * bloom-teams-broker role ARN it is allowed to assume (see server/provision-aws.ts). */
export const prodBrokerConfig = () => ({
    roleArn: requireEnv("BLOOM_TEAMS_BROKER_ROLE_ARN"),
    accessKeyId: requireEnv("AWS_ACCESS_KEY_ID"),
    secretAccessKey: requireEnv("AWS_SECRET_ACCESS_KEY"),
    region: optionalEnv("AWS_REGION", "us-east-1"),
});

/** Admin S3 credentials for server-side verification/writes (HeadObject,
 * GetObjectAttributes, .manifest.json PUT). In dev this is the MinIO root user; in
 * production a dedicated admin/broker identity with full bucket access (distinct
 * from the narrowly-scoped session credentials handed to clients). */
export const adminS3Credentials = () => {
    if (isDevMode()) {
        return minioRootCredentials();
    }
    return {
        accessKeyId: requireEnv("BLOOM_S3_ADMIN_ACCESS_KEY"),
        secretAccessKey: requireEnv("BLOOM_S3_ADMIN_SECRET_KEY"),
    };
};
