// Resets the local dev stack to a known-empty state between scenarios (HARD-WON RULE #5 in
// Design/CloudTeamCollections/orchestration/09-e2e.prompt.md): `supabase db reset` replays
// migrations + seed, the MinIO bucket prefix is cleared via a throwaway `mc` container on the
// same Podman/Docker network as `bloom-minio` (NOT via host.containers.internal — see
// server/dev/README.md's gvproxy-hang gotcha), and local scratch collection folders are wiped.
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import * as fs from "node:fs/promises";
import * as path from "node:path";
import { repoRoot } from "./paths";

const execFileAsync = promisify(execFile);

// Root for every scratch collection folder this harness creates. Kept outside the repo so a
// stray leftover can never get committed, and named distinctively so it's obviously
// harness-owned if a human finds it.
export const E2E_SCRATCH_ROOT = "C:\\BloomE2E";

/** Runs `supabase db reset`, which drops+recreates the DB, replays all migrations, and reruns
 * server/dev/seed.sql (wired via supabase/config.toml's `seed_sql_path`). Wipes all tc.* rows. */
export const resetDatabase = async (): Promise<void> => {
    // `supabase` resolves to a Volta .cmd shim on Windows; Node can only spawn .cmd/.bat files
    // with `shell: true`. Safe here since the argument list is fixed (no interpolated/quoted
    // user data that shell-concatenation could mangle) — see harness/db.ts for why the SQL
    // verification path avoids this CLI entirely.
    await execFileAsync("supabase", ["db", "reset"], {
        cwd: repoRoot,
        timeout: 120_000,
        windowsHide: true,
        shell: true,
    });
};

/** Clears every object (and all noncurrent versions) under the dev bucket by running `mc rm`
 * in a throwaway container on the same network as `bloom-minio` — container-to-container
 * traffic on the shared bridge network is instant; routing through the host gateway
 * (host.containers.internal) is known to hang indefinitely on this Podman setup (see
 * server/dev/README.md "Known gotchas" #1). Uses `podman`; falls back to `docker` if present. */
export const resetMinioBucket = async (
    bucket = "bloom-teams-local",
): Promise<void> => {
    const engine = await detectContainerEngine();
    const script = `mc alias set local http://bloom-minio:9000 minioadmin minioadmin >/dev/null && mc rm --recursive --force --versions local/${bucket} || true`;
    await execFileAsync(
        engine,
        [
            "run",
            "--rm",
            "--network",
            "dev_default",
            "--entrypoint",
            "/bin/sh",
            "quay.io/minio/mc:latest",
            "-c",
            script,
        ],
        {
            timeout: 60_000,
            windowsHide: true,
            env: { ...process.env, MSYS_NO_PATHCONV: "1" },
        },
    );
};

let cachedEngine: string | undefined;
const detectContainerEngine = async (): Promise<string> => {
    if (cachedEngine) return cachedEngine;
    for (const candidate of ["podman", "docker"]) {
        try {
            await execFileAsync(
                candidate,
                ["version", "--format", "{{.Server.Os}}"],
                {
                    timeout: 10_000,
                    windowsHide: true,
                },
            );
            cachedEngine = candidate;
            return candidate;
        } catch {
            // try next
        }
    }
    throw new Error(
        "Neither podman nor docker is available to reset the MinIO bucket. See server/dev/README.md prerequisites.",
    );
};

/** Deletes every scratch collection folder created by a previous run, and recreates the empty
 * root. Safe to call even if the root doesn't exist yet. */
export const resetScratchCollections = async (): Promise<void> => {
    await fs.rm(E2E_SCRATCH_ROOT, { recursive: true, force: true });
    await fs.mkdir(E2E_SCRATCH_ROOT, { recursive: true });
};

/** Full per-scenario reset: DB + bucket + local scratch folders. Call this in `test.beforeEach`
 * (or once in `beforeAll` for scenarios that intentionally chain steps within one test). */
export const resetStack = async (): Promise<void> => {
    await Promise.all([
        resetDatabase(),
        resetMinioBucket(),
        resetScratchCollections(),
    ]);
};
