// Agent CLI for the go.sh launcher control surface (see
// scripts/watchBloomExeControl.mjs). Lets an agent discover the launcher
// running for this worktree, query its state, and command it — quit Bloom,
// rebuild+relaunch, or tear the whole dev stack down — without a human
// pressing Enter in the launcher terminal.
//
//   node .github/skills/bloom-automation/launcherControl.mjs --status [--json]
//   node ... --restart [--wait-ready] [--json]      # rebuild + relaunch (any state)
//   node ... --start [--wait-ready] [--json]        # relaunch, only when awaiting-restart
//   node ... --quit-bloom [--json]                  # durably stop Bloom, launcher stays
//   node ... --shutdown [--json]                    # stop Bloom + launcher + Vite
//   node ... --ensure-running [--wait-ready] [--json]  # start the stack if nobody's home
//   options: --repo-root <path> (default: this checkout), --timeout-ms <n> (default 300000)
//
// Exit codes: 0 = success; 2 = no live launcher found (fall back to
// --ensure-running or launching go.sh yourself); 1 = other failure.

import { execFileSync, spawn } from "node:child_process";
import {
    closeSync,
    mkdirSync,
    openSync,
    rmSync,
    statSync,
    writeFileSync,
} from "node:fs";
import path from "node:path";
import {
    getDefaultRepoRoot,
    requireOptionValue,
} from "./bloomProcessCommon.mjs";
import {
    getDiscoveryFilePath,
    readDiscoveryFile,
} from "../../../scripts/watchBloomExeControl.mjs";

const actionNames = [
    "--status",
    "--restart",
    "--start",
    "--quit-bloom",
    "--shutdown",
    "--ensure-running",
];

const parseArgs = () => {
    const args = process.argv.slice(2);
    const options = {
        action: undefined,
        json: false,
        waitReady: false,
        repoRoot: getDefaultRepoRoot(),
        timeoutMs: 300000,
    };

    for (let i = 0; i < args.length; i++) {
        const arg = args[i];

        if (actionNames.includes(arg)) {
            if (options.action) {
                throw new Error(
                    `Only one action may be given (saw ${options.action} and ${arg}).`,
                );
            }
            options.action = arg.slice(2);
            continue;
        }

        if (arg === "--json") {
            options.json = true;
            continue;
        }

        if (arg === "--wait-ready") {
            options.waitReady = true;
            continue;
        }

        if (arg === "--repo-root") {
            options.repoRoot = path.resolve(
                requireOptionValue(args, i, "--repo-root"),
            );
            i++;
            continue;
        }

        if (arg === "--timeout-ms") {
            options.timeoutMs = Number(
                requireOptionValue(args, i, "--timeout-ms"),
            );
            if (
                !Number.isInteger(options.timeoutMs) ||
                options.timeoutMs <= 0
            ) {
                throw new Error("--timeout-ms must be a positive integer.");
            }
            i++;
            continue;
        }

        throw new Error(
            `Unsupported option ${arg}. Actions: ${actionNames.join(", ")}; options: --json, --wait-ready, --repo-root <path>, --timeout-ms <n>.`,
        );
    }

    if (!options.action) {
        throw new Error(`An action is required: ${actionNames.join(", ")}.`);
    }

    return options;
};

const normalizeComparablePath = (value) =>
    path.resolve(value).replace(/\//g, "\\").toLowerCase();

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

const isPidAlive = (pid) => {
    if (!Number.isInteger(pid) || pid <= 0) {
        return false;
    }
    try {
        process.kill(pid, 0);
        return true;
    } catch {
        return false;
    }
};

// Reads the discovery file and verifies over HTTP that the launcher it
// advertises is actually alive and belongs to this worktree. The file alone
// proves nothing: a hard-killed launcher leaves it behind.
const probeLauncher = async (repoRoot) => {
    const discoveryFilePath = getDiscoveryFilePath(repoRoot);
    const discovery = readDiscoveryFile(discoveryFilePath);

    if (!discovery?.controlUrl) {
        // go.mjs writes an early record (state "starting", no controlUrl yet)
        // during init / dev-server startup, which can take minutes. A live
        // goPid means a launch is underway: wait for it, don't start another.
        if (
            discovery?.kind === "bloom-launcher" &&
            discovery.state === "starting" &&
            isPidAlive(discovery.goPid)
        ) {
            return {
                launcherFound: false,
                starting: true,
                phase: discovery.phase,
                goPid: discovery.goPid,
                startedAt: discovery.startedAt,
                discoveryFilePath,
            };
        }

        return {
            launcherFound: false,
            staleFile: !!discovery,
            discoveryFilePath,
        };
    }

    try {
        const response = await fetch(`${discovery.controlUrl}/status`, {
            signal: AbortSignal.timeout(2000),
        });
        if (!response.ok) {
            throw new Error(`status ${response.status}`);
        }

        const status = await response.json();
        if (
            !status.repoRoot ||
            normalizeComparablePath(status.repoRoot) !==
                normalizeComparablePath(repoRoot)
        ) {
            return {
                launcherFound: false,
                staleFile: true,
                mismatchedRepoRoot: status.repoRoot,
                discoveryFilePath,
            };
        }

        return { launcherFound: true, discovery, status, discoveryFilePath };
    } catch {
        return { launcherFound: false, staleFile: true, discoveryFilePath };
    }
};

const postAction = async (controlUrl, route) => {
    const response = await fetch(`${controlUrl}${route}`, {
        method: "POST",
        signal: AbortSignal.timeout(5000),
    });
    return { statusCode: response.status, body: await response.json() };
};

const fetchStatus = async (controlUrl) => {
    const response = await fetch(`${controlUrl}/status`, {
        signal: AbortSignal.timeout(2000),
    });
    if (!response.ok) {
        throw new Error(`GET /status returned ${response.status}`);
    }
    return response.json();
};

// Polls /status until predicate(status) is true. Tolerates transient fetch
// failures (e.g. mid-teardown) by re-probing via the discovery file, which a
// relaunched launcher rewrites.
const waitForStatus = async (repoRoot, controlUrl, predicate, deadline) => {
    let lastStatus;

    while (Date.now() < deadline) {
        try {
            lastStatus = await fetchStatus(controlUrl);
            if (predicate(lastStatus)) {
                return lastStatus;
            }
        } catch {
            const probe = await probeLauncher(repoRoot);
            if (probe.launcherFound) {
                controlUrl = probe.discovery.controlUrl;
            }
        }

        await sleep(1000);
    }

    throw new Error(
        `Timed out waiting for the launcher to reach the requested state. Last observed: ${JSON.stringify(lastStatus)}`,
    );
};

const waitForLauncherGone = async (controlUrl, deadline) => {
    while (Date.now() < deadline) {
        try {
            await fetchStatus(controlUrl);
        } catch {
            return true;
        }
        await sleep(500);
    }
    return false;
};

// --- ensure-running helpers --------------------------------------------------

const isOrcaRuntimeReachable = () => {
    try {
        const output = execFileSync("orca", ["status", "--json"], {
            encoding: "utf8",
            timeout: 15000,
            windowsHide: true,
            stdio: ["ignore", "pipe", "ignore"],
        });
        const parsed = JSON.parse(output);
        return (
            parsed?.ok === true && parsed?.result?.runtime?.reachable === true
        );
    } catch {
        return false;
    }
};

const goMjsPath = (repoRoot) =>
    path.join(repoRoot, "src", "BloomBrowserUI", "scripts", "go.mjs");

// Launches the go.sh flow in its own Orca terminal tab. The tab is owned by
// the Orca app — visible to the human, independent of the agent session that
// requested it, and controllable by any agent via the HTTP API.
const launchViaOrca = (repoRoot) => {
    // "node <go.mjs>" rather than "./go.sh" because the Orca terminal's shell
    // may not be bash; go.sh is a 4-line shim around exactly this command.
    const command = `node "${goMjsPath(repoRoot)}"`;
    const output = execFileSync(
        "orca",
        [
            "terminal",
            "create",
            "--worktree",
            `path:${repoRoot}`,
            "--title",
            "go.sh",
            "--command",
            command,
            "--json",
        ],
        {
            encoding: "utf8",
            timeout: 30000,
            windowsHide: true,
            stdio: ["ignore", "pipe", "pipe"],
        },
    );

    const parsed = JSON.parse(output);
    if (parsed?.ok !== true) {
        throw new Error(`orca terminal create failed: ${output}`);
    }

    return { method: "orca-terminal", orcaResult: parsed.result };
};

// Non-Orca fallback: a fully detached process. It has no terminal window, but
// it survives the agent session that started it; its output goes to a log
// file that /status advertises as logPath.
const launchDetached = (repoRoot) => {
    const logPath = path.join(repoRoot, "output", "bloom-launcher.log");
    mkdirSync(path.dirname(logPath), { recursive: true });
    const logFd = openSync(logPath, "a");

    try {
        const child = spawn(process.execPath, [goMjsPath(repoRoot)], {
            cwd: repoRoot,
            detached: true,
            stdio: ["ignore", logFd, logFd],
            env: { ...process.env, BLOOM_LAUNCHER_LOG: logPath },
            windowsHide: true,
        });
        child.unref();
        return { method: "detached", goPid: child.pid, logPath };
    } finally {
        closeSync(logFd);
    }
};

const startingLockPath = (repoRoot) =>
    path.join(repoRoot, "output", "bloom-launcher.starting.lock");

const startingLockStaleMs = 5 * 60 * 1000;

// Guards against two agents racing --ensure-running into a double launch.
// Best-effort: O_EXCL create wins; a lock older than 5 minutes is presumed
// abandoned and stolen.
const tryAcquireStartingLock = (repoRoot) => {
    const lockPath = startingLockPath(repoRoot);
    mkdirSync(path.dirname(lockPath), { recursive: true });
    const lockContent = JSON.stringify({
        pid: process.pid,
        startedAt: new Date().toISOString(),
    });

    try {
        writeFileSync(lockPath, lockContent, { flag: "wx" });
        return { acquired: true, lockPath };
    } catch {}

    try {
        const ageMs = Date.now() - statSync(lockPath).mtimeMs;
        if (ageMs > startingLockStaleMs) {
            rmSync(lockPath, { force: true });
            writeFileSync(lockPath, lockContent, { flag: "wx" });
            return { acquired: true, lockPath };
        }
    } catch {}

    return { acquired: false, lockPath };
};

const releaseStartingLock = (lockPath) => {
    try {
        rmSync(lockPath, { force: true });
    } catch {}
};

const waitForLauncher = async (repoRoot, deadline, note) => {
    while (Date.now() < deadline) {
        const probe = await probeLauncher(repoRoot);
        if (probe.launcherFound) {
            return probe;
        }
        await sleep(1000);
    }

    throw new Error(
        `Timed out waiting for a launcher to appear for ${repoRoot}${note ? ` (${note})` : ""}.`,
    );
};

const ensureRunning = async (options, deadline) => {
    let probe = await probeLauncher(options.repoRoot);
    let launch;

    if (!probe.launcherFound && probe.starting) {
        // A launcher (possibly human-started) is mid-startup — init.sh or the
        // Vite dev server can take minutes. Wait for it instead of launching.
        probe = await waitForLauncher(
            options.repoRoot,
            deadline,
            `a launcher is already starting (phase: ${probe.phase})`,
        );
        launch = { method: "waited-for-starting-launcher" };
    } else if (!probe.launcherFound) {
        const lock = tryAcquireStartingLock(options.repoRoot);

        if (!lock.acquired) {
            // Another agent is already starting the stack; wait for it.
            probe = await waitForLauncher(
                options.repoRoot,
                deadline,
                "another agent holds the starting lock",
            );
            launch = { method: "waited-for-other-agent" };
        } else {
            try {
                launch = isOrcaRuntimeReachable()
                    ? launchViaOrca(options.repoRoot)
                    : launchDetached(options.repoRoot);
                probe = await waitForLauncher(options.repoRoot, deadline);
            } finally {
                releaseStartingLock(lock.lockPath);
            }
        }
    }

    let status = probe.status;

    // The launcher may be parked with Bloom stopped; nudge it.
    if (status.state === "awaiting-restart") {
        await postAction(probe.discovery.controlUrl, "/start");
        status = await fetchStatus(probe.discovery.controlUrl);
    }

    if (options.waitReady) {
        status = await waitForStatus(
            options.repoRoot,
            probe.discovery.controlUrl,
            (current) => current.state === "bloom-running",
            deadline,
        );
    }

    return { launcherFound: true, launch, status };
};

// --- main --------------------------------------------------------------------

let options;
try {
    options = parseArgs();
} catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
}

const deadline = Date.now() + options.timeoutMs;

const report = (result, exitCode = 0) => {
    if (options.json) {
        console.log(JSON.stringify(result, null, 2));
    } else {
        if (result.launcherFound === false) {
            if (result.starting) {
                console.log(
                    `A launcher is starting for ${options.repoRoot} (phase: ${result.phase}, go PID ${result.goPid}, since ${result.startedAt}).`,
                );
                console.log(
                    "Wait for it (e.g. --ensure-running --wait-ready) instead of launching another.",
                );
            } else {
                console.log(
                    `No live launcher found for ${options.repoRoot}${result.staleFile ? " (stale discovery file: nobody home at its controlUrl)" : ""}.`,
                );
                console.log(
                    "Use --ensure-running to start one, or ./go.sh for an interactive session.",
                );
            }
        }
        if (result.launch?.method) {
            console.log(`Launched via: ${result.launch.method}`);
            if (result.launch.logPath) {
                console.log(`Launcher log: ${result.launch.logPath}`);
            }
        }
        if (result.response) {
            console.log(
                `${result.action}: HTTP ${result.response.statusCode} ${JSON.stringify(result.response.body)}`,
            );
        }
        if (result.status) {
            const s = result.status;
            console.log(`Launcher state: ${s.state}`);
            console.log(
                `Bloom PID: ${s.bloomProcessId ?? "none"}, HTTP ${s.httpPort ?? "-"}, CDP ${s.cdpPort ?? "-"}, Vite ${s.vitePort ?? "-"}`,
            );
            console.log(
                `Launch ${s.launchNumber}, control ${result.controlUrl ?? ""}`,
            );
        }
        if (result.shutdownComplete !== undefined) {
            console.log(
                result.shutdownComplete
                    ? "Launcher stack shut down."
                    : "Shutdown requested, but the launcher was still responding at timeout.",
            );
        }
    }

    process.exit(exitCode);
};

try {
    if (options.action === "ensure-running") {
        const result = await ensureRunning(options, deadline);
        report({
            ...result,
            controlUrl: (await probeLauncher(options.repoRoot)).discovery
                ?.controlUrl,
        });
    }

    const probe = await probeLauncher(options.repoRoot);

    if (!probe.launcherFound) {
        report(
            {
                launcherFound: false,
                starting: probe.starting,
                phase: probe.phase,
                goPid: probe.goPid,
                startedAt: probe.startedAt,
                staleFile: probe.staleFile,
                discoveryFilePath: probe.discoveryFilePath,
                repoRoot: options.repoRoot,
            },
            2,
        );
    }

    const controlUrl = probe.discovery.controlUrl;

    if (options.action === "status") {
        report({
            launcherFound: true,
            controlUrl,
            discovery: probe.discovery,
            status: probe.status,
        });
    }

    if (options.action === "shutdown") {
        const response = await postAction(controlUrl, "/shutdown");
        const shutdownComplete = await waitForLauncherGone(
            controlUrl,
            Math.min(deadline, Date.now() + 60000),
        );
        report(
            {
                launcherFound: true,
                action: "shutdown",
                response,
                shutdownComplete,
            },
            shutdownComplete ? 0 : 1,
        );
    }

    if (options.action === "quit-bloom") {
        const response = await postAction(controlUrl, "/quit-bloom");
        let status = probe.status;
        if (response.statusCode === 202) {
            status = await waitForStatus(
                options.repoRoot,
                controlUrl,
                (current) => current.state === "awaiting-restart",
                deadline,
            );
        }
        report({
            launcherFound: true,
            action: "quit-bloom",
            response,
            controlUrl,
            status,
        });
    }

    // restart / start
    const baselineLaunchNumber = probe.status.launchNumber ?? 0;
    const route = options.action === "start" ? "/start" : "/restart";
    const response = await postAction(controlUrl, route);

    if (response.statusCode >= 400) {
        report(
            {
                launcherFound: true,
                action: options.action,
                response,
                controlUrl,
                status: probe.status,
            },
            1,
        );
    }

    let status = probe.status;
    if (options.waitReady) {
        status = await waitForStatus(
            options.repoRoot,
            controlUrl,
            (current) =>
                current.state === "bloom-running" &&
                (current.launchNumber ?? 0) > baselineLaunchNumber,
            deadline,
        );
    }

    report({
        launcherFound: true,
        action: options.action,
        response,
        controlUrl,
        status,
        waitedForReady: options.waitReady,
    });
} catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
}
