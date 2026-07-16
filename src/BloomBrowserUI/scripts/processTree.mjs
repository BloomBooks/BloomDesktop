/* eslint-env node */
/* global clearTimeout, process, setTimeout */
import { spawn } from "node:child_process";

const isWindows = process.platform === "win32";

/**
 * Force-kill a process and ALL of its descendants.
 *
 * This exists because the dev launcher spawns a deep tree (dev.mjs -> Vite plus
 * ~7 watcher processes -> onchange's own command children). On Windows a plain
 * SIGTERM/SIGINT to a Node child does NOT reach that child's descendants, so the
 * watcher grandchildren orphan and accumulate across runs. `taskkill /t` walks
 * and kills the whole tree, which is the only reliable way to avoid the leak.
 *
 * On POSIX we keep the historical best-effort behavior (signal the process
 * directly); terminals already propagate Ctrl-C to the foreground process group,
 * and we avoid the larger behavioral change of detached process groups on a
 * platform the bug does not affect.
 *
 * @param {number} pid - Process id to kill (together with its descendants on Windows).
 * @param {string} [signal] - POSIX signal to send; ignored on Windows. Defaults to SIGTERM.
 * @returns {Promise<void>} Resolves once the kill attempt has completed.
 */
export const killProcessTree = (pid, signal = "SIGTERM") =>
    new Promise((resolve) => {
        if (!Number.isInteger(pid) || pid <= 0) {
            resolve();
            return;
        }

        if (isWindows) {
            const killer = spawn(
                "taskkill",
                ["/pid", String(pid), "/t", "/f"],
                {
                    stdio: "ignore",
                    shell: false,
                },
            );
            killer.on("exit", () => resolve());
            killer.on("error", () => resolve());
            return;
        }

        try {
            process.kill(pid, signal);
        } catch {
            // The process may already be gone; nothing more to do.
        }
        resolve();
    });

/**
 * Terminate a spawned child AND all of its descendants, resolving once the
 * child has exited (or a bounded force-kill fallback has fired).
 *
 * This is the shared teardown for the dev launchers (go.mjs, run.mjs, and
 * repo-root scripts/watchBloomExe.mjs). It builds on killProcessTree because on
 * Windows a plain signal to a Node child does NOT reach that child's
 * descendants, so the spawned subtree (Vite + watchers, or dotnet watch +
 * Bloom) would orphan if we merely signaled the child and trusted it to reap
 * them before exiting.
 *
 * Two kill strategies, selected by `signalFirst`:
 * - false (default; go.mjs/run.mjs): on Windows, force-kill the whole subtree
 *   immediately — the only reliable way to leave zero orphans there. On POSIX,
 *   send SIGINT first so dotnet/Bloom can shut down cleanly (terminals already
 *   propagate Ctrl-C to the process group), then force-kill the tree if the
 *   child is still alive after `gracefulShutdownMs`.
 * - true (watchBloomExe.mjs, which restarts `dotnet watch` in place): send
 *   SIGINT first on EVERY platform, giving the child `gracefulShutdownMs` to
 *   shut down gracefully, then force-kill whatever remains of the tree.
 *
 * @param {import("node:child_process").ChildProcess} child - The child to terminate.
 * @param {object} [options]
 * @param {number} [options.gracefulShutdownMs] - How long to wait after SIGINT
 *   before force-killing the tree. Defaults to 1500.
 * @param {boolean} [options.signalFirst] - Send SIGINT before force-killing even
 *   on Windows. Defaults to false (immediate tree kill on Windows).
 * @returns {Promise<void>} Resolves once the child has exited or the bounded
 *   force-kill fallback has run.
 */
export const terminateChildProcess = (
    child,
    { gracefulShutdownMs = 1500, signalFirst = false } = {},
) =>
    new Promise((resolve) => {
        if (!child || child.exitCode !== null || child.signalCode) {
            resolve();
            return;
        }

        let settled = false;
        let forceTimer;

        const finish = () => {
            if (settled) {
                return;
            }

            settled = true;
            if (forceTimer) {
                clearTimeout(forceTimer);
            }
            resolve();
        };

        child.once("exit", finish);

        if (isWindows && !signalFirst) {
            // Kill the entire subtree by pid; the "exit" event resolves us, and
            // the watchdog covers the case where it never arrives.
            void killProcessTree(child.pid);
            forceTimer = setTimeout(finish, gracefulShutdownMs);
            return;
        }

        try {
            child.kill("SIGINT");
        } catch {
            finish();
            return;
        }

        forceTimer = setTimeout(() => {
            if (settled) {
                return;
            }

            void killProcessTree(child.pid, "SIGTERM");
            setTimeout(finish, 250);
        }, gracefulShutdownMs);
    });

/**
 * Quote a string as a single PowerShell single-quoted literal (doubling any
 * embedded single quotes), so an arbitrary worktree path can be embedded safely.
 *
 * @param {string} value - The raw string to quote.
 * @returns {string} A PowerShell-safe single-quoted literal.
 */
const toPowerShellLiteral = (value) => `'${value.replace(/'/g, "''")}'`;

/**
 * Find the process ids of stale, ORPHANED `node.exe` processes whose command line
 * contains the given marker (typically this worktree's repo-root path), excluding
 * the supplied pids (e.g. our own process). Windows only; returns an empty array on
 * other platforms because the orphaning bug this guards against is Windows-specific.
 *
 * "Orphaned" means the process's parent is no longer running -- exactly the state a
 * dev-server tree is left in after the launcher is hard-killed without running its
 * shutdown handlers. Requiring a dead parent is what keeps the sweep from killing a
 * legitimate concurrent run from the same worktree (e.g. another terminal's
 * `pnpm dev` or tests), whose processes still have a living parent. We only need to
 * find the orphaned tree roots; their still-parented descendants (Vite, onchange's
 * command children) are taken down when the root's tree is killed.
 *
 * @param {string} commandLineMarker - Substring that identifies this worktree's processes.
 * @param {number[]} [excludePids] - Process ids to exclude from the result.
 * @returns {Promise<number[]>} The matching, orphaned, non-excluded process ids.
 */
export const findStaleWorktreeNodeProcesses = (
    commandLineMarker,
    excludePids = [],
) =>
    new Promise((resolve) => {
        if (!isWindows || !commandLineMarker) {
            resolve([]);
            return;
        }

        const excluded = new Set(excludePids.filter(Number.isInteger));
        const markerLiteral = toPowerShellLiteral(commandLineMarker);
        // List node.exe processes whose command line mentions this worktree AND
        // whose parent process is no longer alive (true orphans), printing just
        // their pids, one per line. We build a lookup of every live pid so the
        // parent-alive test is a cheap hash check.
        const script = [
            `$marker = ${markerLiteral};`,
            "$all = Get-CimInstance Win32_Process;",
            "$alive = @{};",
            "foreach ($p in $all) { $alive[[int]$p.ProcessId] = $true }",
            "$all",
            "| Where-Object { $_.Name -eq 'node.exe' -and $_.CommandLine -and $_.CommandLine.Contains($marker) -and -not $alive.ContainsKey([int]$_.ParentProcessId) }",
            "| ForEach-Object { $_.ProcessId }",
        ].join(" ");

        const probe = spawn(
            "powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command", script],
            { stdio: ["ignore", "pipe", "ignore"], shell: false },
        );

        let output = "";
        probe.stdout.on("data", (chunk) => {
            output += chunk.toString();
        });

        probe.on("error", () => resolve([]));
        probe.on("exit", () => {
            const pids = output
                .split(/\r?\n/)
                .map((line) => Number.parseInt(line.trim(), 10))
                .filter((pid) => Number.isInteger(pid) && pid > 0)
                .filter((pid) => !excluded.has(pid));
            resolve([...new Set(pids)]);
        });
    });

/**
 * Detect and force-kill stale dev-server `node.exe` processes left over from a
 * previous run of THIS worktree (e.g. after the launcher was hard-killed and its
 * signal handlers never ran). This keeps a prior leak from silently starving the
 * machine and wrecking the next `go.sh`. Windows only; a no-op elsewhere.
 *
 * @param {object} params
 * @param {string} params.commandLineMarker - Substring identifying this worktree's processes.
 * @param {number[]} [params.excludePids] - Process ids to leave alone (e.g. our own).
 * @param {(message: string) => void} [params.log] - Logger for what was swept.
 * @returns {Promise<number>} The number of stale processes that were killed.
 */
export const sweepStaleWorktreeNodeProcesses = async (params) => {
    if (!isWindows) {
        return 0;
    }

    const stalePids = await findStaleWorktreeNodeProcesses(
        params.commandLineMarker,
        params.excludePids,
    );

    if (stalePids.length === 0) {
        return 0;
    }

    params.log?.(
        `Found ${stalePids.length} orphaned dev-server node process(es) from a hard-killed previous run of this worktree (pids: ${stalePids.join(", ")}). Cleaning them up before starting.`,
    );

    await Promise.all(stalePids.map((pid) => killProcessTree(pid)));
    return stalePids.length;
};
