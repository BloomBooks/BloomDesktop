// S3 (MinIO) verification helpers. Uses the same throwaway-`mc`-container-on-the-shared-network
// approach as harness/reset.ts's bucket clear (see that file's comment for why
// `host.containers.internal` is never used here).
import { execFile } from "node:child_process";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);

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
    throw new Error("Neither podman nor docker is available to query MinIO.");
};

/** Lists every object key under `prefix` (recursive) in the local bucket. Returns bare keys
 * (relative to the bucket root), e.g. `tc/<collectionId>/books/<bookId>/meta.json`. */
export const listS3Objects = async (
    prefix: string,
    bucket = "bloom-teams-local",
): Promise<string[]> => {
    const engine = await detectContainerEngine();
    const script = `mc alias set local http://bloom-minio:9000 minioadmin minioadmin >/dev/null && mc ls --recursive local/${bucket}/${prefix} 2>/dev/null || true`;
    const { stdout } = await execFileAsync(
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
            timeout: 30_000,
            windowsHide: true,
            env: { ...process.env, MSYS_NO_PATHCONV: "1" },
        },
    );
    // Each line looks like: "[2026-07-08 05:39:57 UTC]  56KiB STANDARD books/<id>/A5 Portrait.htm"
    // The key is everything after the 4th whitespace-separated field (keys may contain spaces).
    return stdout
        .split(/\r?\n/)
        .map((line) => line.trim())
        .filter(Boolean)
        .map((line) => {
            const match = line.match(/^\[.+?\]\s+\S+\s+\S+\s+(.+)$/);
            return match ? `${prefix.replace(/\/$/, "")}/${match[1]}` : null;
        })
        .filter((key): key is string => key !== null);
};
