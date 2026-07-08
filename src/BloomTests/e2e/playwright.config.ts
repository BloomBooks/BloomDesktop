import type { PlaywrightTestConfig } from "@playwright/test";

// Real Bloom.exe instances + a live local Supabase/MinIO stack are slow to bring up (multi-
// second RPC round trips, S3 uploads, `supabase db reset`). No watch modes, no retries that
// could mask a flaky reset, and workers=1: scenarios launch/kill real OS processes and reset
// shared local infra (DB + MinIO bucket), so parallel workers would stomp on each other.
const config: PlaywrightTestConfig = {
    testDir: "tests",
    globalSetup: require.resolve("./harness/globalSetup.ts"),
    timeout: 180_000,
    workers: 1,
    retries: 0,
    fullyParallel: false,
    reporter: [["list"]],
    expect: {
        timeout: 15_000,
    },
    use: {
        trace: "retain-on-failure",
    },
};

export default config;
