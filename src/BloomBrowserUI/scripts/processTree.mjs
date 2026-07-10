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

/**
 * Find `node.exe` processes whose command line mentions a linked library's checkout path
 * and that are NOT owned by a *different* live `go.mjs` launcher.
 *
 * The orphan-only test isn't enough here: a linked library's dev server spawns through
 * pnpm/cmd/vite-plus layers, so after a hard-killed launcher an intermediate process often
 * survives and keeps the real server "parented" (not an orphan). But we also must not stomp
 * a concurrent `go.sh --with <same-lib>` in another terminal. So instead of the parent test,
 * we walk each candidate's ancestor chain: if it reaches a live `go.mjs` that is NOT our own
 * launcher (`selfGoPid`), another session owns it -> skip. Otherwise it's our own child
 * (ancestor is us) or a leftover from a dead launcher (no live go.mjs ancestor) -> reap.
 *
 * @param {string} commandLineMarker - The library checkout path identifying its processes.
 * @param {number} selfGoPid - This launcher's pid (go.mjs), whose owned children we DO reap.
 * @returns {Promise<number[]>} The matching process ids that we own or that are leftovers.
 */
const findLinkedLibraryServers = (commandLineMarker, selfGoPid) =>
    new Promise((resolve) => {
        if (!isWindows || !commandLineMarker) {
            resolve([]);
            return;
        }

        const markerLiteral = toPowerShellLiteral(commandLineMarker);
        const script = [
            `$marker = ${markerLiteral};`,
            `$selfGo = ${Number(selfGoPid) || 0};`,
            "$all = Get-CimInstance Win32_Process;",
            "$byId = @{};",
            "foreach ($p in $all) { $byId[[int]$p.ProcessId] = $p }",
            // True if pid's ancestor chain includes a live go.mjs other than $selfGo.
            "function OwnedByOtherGo([int]$startPid) {",
            "  $cur = $startPid; $depth = 0;",
            "  while ($cur -and $byId.ContainsKey($cur) -and $depth -lt 50) {",
            // Reaching our own launcher means the whole chain above is our launch (the
            // Volta shim that started us is also a 'node ... go.mjs'); stop -> owned by us.
            "    if ($cur -eq $selfGo) { return $false }",
            "    $proc = $byId[$cur];",
            "    if ($proc.Name -eq 'node.exe' -and $proc.CommandLine -and $proc.CommandLine.Contains('go.mjs')) { return $true }",
            "    $cur = [int]$proc.ParentProcessId; $depth++;",
            "  }",
            "  return $false",
            "}",
            "$all",
            "| Where-Object { $_.Name -eq 'node.exe' -and $_.CommandLine -and $_.CommandLine.Contains($marker) -and -not (OwnedByOtherGo([int]$_.ProcessId)) }",
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
                .filter((pid) => Number.isInteger(pid) && pid > 0);
            resolve([...new Set(pids)]);
        });
    });

/**
 * Force-kill the dev servers / watch-builds we spawned for a `go.sh --with <lib>` run that
 * live under a library checkout, INCLUDING ones a previous hard-killed launcher left behind
 * (whose intermediate processes survived, so the orphan sweep misses them). A concurrent
 * `go.sh --with <same-lib>` in another terminal is left untouched (see findLinkedLibraryServers).
 * Windows only; a no-op elsewhere.
 *
 * @param {object} params
 * @param {string} params.commandLineMarker - The library checkout path identifying its processes.
 * @param {number} params.selfGoPid - This launcher's pid (go.mjs).
 * @param {(message: string) => void} [params.log] - Logger for what was reaped.
 * @returns {Promise<number>} The number of processes that were killed.
 */
export const killLinkedLibraryServers = async (params) => {
    if (!isWindows) {
        return 0;
    }

    const pids = await findLinkedLibraryServers(
        params.commandLineMarker,
        params.selfGoPid,
    );

    if (pids.length === 0) {
        return 0;
    }

    params.log?.(
        `Reaping ${pids.length} leftover dev-server node process(es) under ${params.commandLineMarker} (pids: ${pids.join(", ")}).`,
    );

    await Promise.all(pids.map((pid) => killProcessTree(pid)));
    return pids.length;
};
