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

// `collections/pullDown` (the "join a cloud collection" API) has no configurable destination:
// CloudJoinFlow.DetermineLocalCollectionFolder always resolves to
// `NewCollectionWizard.DefaultParentDirectoryForCollections` (= `%MyDocuments%\Bloom`) + the
// collection's display name — the SAME real folder a human's Bloom would use. There is no env
// var or API param to redirect it. To keep the harness from ever touching a developer's real
// collections, every collection this harness creates (and therefore ever pulls down) MUST use
// a display name starting with this prefix, and `resetJoinedCollections` only ever deletes
// folders matching it.
export const JOINED_COLLECTION_NAME_PREFIX = "BloomE2E-";

// Resolved via the same Windows shell-folder API .NET's Environment.SpecialFolder.MyDocuments
// uses (NOT `%USERPROFILE%\Documents` — Documents is commonly redirected to OneDrive, as it is
// on the machine this harness was built on: `[Environment]::GetFolderPath('MyDocuments')` is
// the only reliable way to match what Bloom itself will resolve).
let cachedDocumentsFolder: string | undefined;
/** The real `%MyDocuments%\Bloom` folder (see the doc comment above `JOINED_COLLECTION_NAME_PREFIX`
 * for why this must be resolved via PowerShell rather than `%USERPROFILE%\Documents`). Exported
 * for collectionFixture.ts's `pulledDownCollectionFilePath`, which needs the same resolution. */
export const documentsBloomFolder = async (): Promise<string> => {
    if (!cachedDocumentsFolder) {
        const { stdout } = await execFileAsync(
            "powershell",
            [
                "-NoProfile",
                "-Command",
                "[Environment]::GetFolderPath('MyDocuments')",
            ],
            { timeout: 10_000, windowsHide: true },
        );
        cachedDocumentsFolder = stdout.trim();
    }
    return path.join(cachedDocumentsFolder, "Bloom");
};

/** Deletes any `%MyDocuments%\Bloom\<name>` folder this harness could have created via
 * `collections/pullDown` — restricted to names starting with `JOINED_COLLECTION_NAME_PREFIX`
 * so this can never touch a developer's real collections. */
export const resetJoinedCollections = async (): Promise<void> => {
    const bloomDocsFolder = await documentsBloomFolder();
    let entries: string[];
    try {
        entries = await fs.readdir(bloomDocsFolder);
    } catch {
        return; // folder doesn't exist yet — nothing to clean
    }
    await Promise.all(
        entries
            .filter((name) => name.startsWith(JOINED_COLLECTION_NAME_PREFIX))
            .map((name) =>
                fs.rm(path.join(bloomDocsFolder, name), {
                    recursive: true,
                    force: true,
                }),
            ),
    );
};

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
        resetJoinedCollections(),
    ]);
};
