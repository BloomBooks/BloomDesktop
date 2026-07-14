// Constants describing the local dev stack (server/dev/README.md) that this harness drives
// against. These match the seeded values from server/dev/seed.sql and `supabase start`'s
// fixed local demo keys, which are stable across `supabase db reset` (they are baked into
// supabase/config.toml, not randomly generated per-reset).

export const SUPABASE_URL = "http://localhost:54321";

// Local Supabase's fixed demo anon key (see supabase status / supabase/config.toml). Stable
// across `supabase db reset`.
export const ANON_KEY =
    "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6ImFub24iLCJleHAiOjE5ODM4MTI5OTZ9.CRXP1A7WOeoJeXxjNni43kdQwgnWNReilDMblYTn_I0";

export const S3_ENDPOINT = "http://localhost:9000";
export const S3_BUCKET = "bloom-teams-local";

// Dev users seeded by server/dev/seed.sql. All share this password.
export const DEV_PASSWORD = "BloomDev123!";

export interface DevUser {
    email: string;
    password: string;
}

export const ALICE: DevUser = {
    email: "alice@dev.local",
    password: DEV_PASSWORD,
};
export const BOB: DevUser = { email: "bob@dev.local", password: DEV_PASSWORD };
export const ADMIN: DevUser = {
    email: "admin@dev.local",
    password: DEV_PASSWORD,
};

// Builds the BLOOM_CLOUDTC_* environment block for a Bloom.exe instance signed in as `user`.
export const cloudTcEnv = (user?: DevUser): NodeJS.ProcessEnv => ({
    BLOOM_CLOUDTC_SUPABASE_URL: SUPABASE_URL,
    BLOOM_CLOUDTC_ANON_KEY: ANON_KEY,
    BLOOM_CLOUDTC_S3_ENDPOINT: S3_ENDPOINT,
    BLOOM_CLOUDTC_S3_BUCKET: S3_BUCKET,
    BLOOM_CLOUDTC_AUTH_MODE: "dev",
    // Fast change-propagation for tests: real users poll every 60s, but scenarios that wait
    // for a second instance to notice a remote change converge in seconds instead of relying
    // on explicit pollNow calls racing the server commit (see server/dev/README.md's env
    // table). The 90s expect.poll budgets stay as generous ceilings.
    BLOOM_CLOUDTC_POLL_SECONDS: "5",
    ...(user
        ? {
              BLOOM_CLOUDTC_USER: user.email,
              BLOOM_CLOUDTC_PASSWORD: user.password,
          }
        : {}),
});
