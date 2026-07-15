/* eslint-env node */
/* global process */
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

// Regex (matched against a process's full command line) identifying a node
// process that belongs to a Bloom dev stack, in ANY worktree. These are the
// processes `go.mjs` launches, directly or transitively: the dev.mjs launcher
// and the watchBloomExe controller (direct children of go.mjs), plus Vite, the
// LESS watcher, the onchange watchers, and the content-copy scripts they spawn.
// go.mjs itself is deliberately NOT matched, so a live launcher is never a
// target. Kept Bloom-specific (paths/script names) so we never touch an
// unrelated orphaned node server that happens to be on the machine.
const BLOOM_DEV_STACK_SIGNATURE =
    "watchBloomExe\\.mjs" +
    "|BloomBrowserUI[\\\\/](scripts[\\\\/](dev|watchLess)\\.mjs|node_modules[\\\\/](vite|onchange)[\\\\/])" +
    "|(compilePug|copyStaticFile|copyContentFile)\\.mjs";

/**
 * Find the process ids of ORPHANED Bloom dev-stack `node.exe` processes across
 * ALL worktrees, excluding the supplied pids (e.g. our own). Windows only;
 * returns an empty array elsewhere because the orphaning bug this guards against
 * is Windows-specific.
 *
 * "Orphaned" means the process's parent is no longer running. This is the key
 * safety property: a LIVE go.sh session's processes always have a living parent
 * chain up to their `go.mjs` controller, so they never match — we can therefore
 * reap orphans from every worktree without any risk of killing a running session
 * (this worktree's or another's). When a launcher is hard-killed (terminal
 * closed, SIGKILL, timeout) its shutdown handler never runs, leaving exactly
 * these dead-parented orphans behind. We only need the orphaned tree roots; their
 * still-parented descendants are taken down when the root's tree is killed.
 *
 * @param {number[]} [excludePids] - Process ids to exclude from the result.
 * @returns {Promise<number[]>} The matching, orphaned, non-excluded process ids.
 */
export const findOrphanedBloomDevStackRoots = (excludePids = []) =>
    new Promise((resolve) => {
        if (!isWindows) {
            resolve([]);
            return;
        }

        const excluded = new Set(excludePids.filter(Number.isInteger));
        // List node.exe processes whose command line matches a Bloom dev-stack
        // process AND whose parent is no longer alive (true orphans), printing
        // just their pids. We build a lookup of every live pid so the
        // parent-alive test is a cheap hash check.
        const script = [
            `$sig = '${BLOOM_DEV_STACK_SIGNATURE}';`,
            "$all = Get-CimInstance Win32_Process;",
            "$alive = @{};",
            "foreach ($p in $all) { $alive[[int]$p.ProcessId] = $true }",
            "$all",
            "| Where-Object { $_.Name -eq 'node.exe' -and $_.CommandLine -and ($_.CommandLine -match $sig) -and -not $alive.ContainsKey([int]$_.ParentProcessId) }",
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
 * Detect and force-kill orphaned Bloom dev-stack `node.exe` processes left over
 * from hard-killed launchers, in ANY worktree. Running this at `go.sh` startup
 * keeps prior leaks from accumulating across worktrees and sessions and starving
 * the machine (which otherwise ratchets up until launches start failing their
 * Vite health check). Safe because only dead-parented orphans are targeted, never
 * a live session. Windows only; a no-op elsewhere.
 *
 * @param {object} [params]
 * @param {number[]} [params.excludePids] - Process ids to leave alone (e.g. our own).
 * @param {(message: string) => void} [params.log] - Logger for what was reaped.
 * @returns {Promise<number>} The number of orphaned processes that were killed.
 */
export const reapOrphanedBloomDevStacks = async (params = {}) => {
    if (!isWindows) {
        return 0;
    }

    const orphanPids = await findOrphanedBloomDevStackRoots(params.excludePids);

    if (orphanPids.length === 0) {
        return 0;
    }

    params.log?.(
        `Found ${orphanPids.length} orphaned Bloom dev-server node process(es) left by a hard-killed launcher (pids: ${orphanPids.join(", ")}). Cleaning them up before starting.`,
    );

    await Promise.all(orphanPids.map((pid) => killProcessTree(pid)));
    return orphanPids.length;
};
