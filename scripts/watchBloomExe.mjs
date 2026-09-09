import { spawn } from "node:child_process";
import { existsSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
    findRunningStandardBloomInstances,
    requireOptionValue,
    requireTcpPortOption,
} from "../.github/skills/bloom-automation/bloomProcessCommon.mjs";
import {
    discoveryFileSchemaVersion,
    getDiscoveryFilePath,
    isDotnetWatchRestartSignal,
    launcherReadyPrefix,
    readDiscoveryFile,
    removeDiscoveryFile,
    startControlServer,
    writeDiscoveryFile,
} from "./watchBloomExeControl.mjs";
import { isManualRestartCommand } from "./watchBloomExeInput.mjs";
import { getHelpfulStartupLabel } from "./watchBloomExeLabel.mjs";

const automationReadyPrefix = "BLOOM_AUTOMATION_READY ";

const parseArgs = () => {
    const args = process.argv.slice(2);
    const options = {
        repoRoot: path.resolve(
            path.dirname(fileURLToPath(import.meta.url)),
            "..",
        ),
        vitePort: undefined,
        noWatch: false,
    };

    for (let i = 0; i < args.length; i++) {
        const arg = args[i];

        if (arg === "--no-watch") {
            options.noWatch = true;
            continue;
        }

        if (arg === "--repo-root") {
            options.repoRoot = requireOptionValue(args, i, "--repo-root");
            i++;
            continue;
        }

        if (arg === "--vite-port") {
            options.vitePort = requireTcpPortOption(
                "--vite-port",
                requireOptionValue(args, i, "--vite-port"),
            );
            i++;
            continue;
        }

        if (arg.startsWith("--vite-port=")) {
            options.vitePort = requireTcpPortOption(
                "--vite-port",
                arg.slice("--vite-port=".length),
            );
            continue;
        }

        if (arg.startsWith("--")) {
            throw new Error(
                "Unsupported option. Supported options are --repo-root, --vite-port and --no-watch.",
            );
        }
    }

    return options;
};

let options;

try {
    options = parseArgs();
} catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
}

// How long we give a launch to reach BLOOM_AUTOMATION_READY. Note this clock starts when we
// spawn `dotnet watch run` (see startLaunchTimeout), so it covers dotnet watch's own startup, a
// NuGet restore if one is needed, the MSBuild build, AND Bloom's initialization -- not just
// "Bloom starting". On a cold tree, or a machine short of memory and paging, the build alone can
// eat most of it. Set BLOOM_LAUNCH_TIMEOUT_MS to override (0 disables the timeout entirely).
// The phase timings we log (see stampLaunchPhase) tell you where the time actually went.
const launchTimeoutMs = (() => {
    const fromEnv = process.env.BLOOM_LAUNCH_TIMEOUT_MS;
    if (fromEnv === undefined || fromEnv.trim() === "") return 120000;
    const parsed = Number.parseInt(fromEnv, 10);
    if (!Number.isFinite(parsed) || parsed < 0) {
        console.error(
            `Ignoring BLOOM_LAUNCH_TIMEOUT_MS="${fromEnv}": expected a non-negative whole number of milliseconds.`,
        );
        return 120000;
    }
    return parsed;
})();
const bloomMonitorPollMs = 500;
const shortLivedBloomMs = 5000;
// Under `dotnet watch` (the default), the watch child outlives each Bloom and rebuilds/relaunches
// on a source change. Under --no-watch we use plain `dotnet run`: one build, one Bloom, and a
// source change is picked up only when you restart. That costs you hot reload, but it also skips
// the most expensive part of starting up -- dotnet watch spends longer working out which files to
// watch (a full project evaluation) than MSBuild spends compiling. See the launch phase timings.
const launchesUnderWatch = !options.noWatch;
const projectPath = path.join(
    options.repoRoot,
    "src",
    "BloomExe",
    "BloomExe.csproj",
);

if (!existsSync(projectPath)) {
    console.error(
        `Bloom project not found at ${projectPath}. Verify --repo-root or run the command from this worktree.`,
    );
    process.exit(1);
}

const normalizeComparablePath = (value) =>
    path.resolve(value).replace(/\//g, "\\").toLowerCase();

const tryInferVitePortFromRunningBloom = async () => {
    const runningInstances = await findRunningStandardBloomInstances();
    const expectedRepoRoot = normalizeComparablePath(options.repoRoot);
    const vitePorts = [
        ...new Set(
            runningInstances
                .filter(
                    (instance) =>
                        instance.detectedRepoRoot &&
                        normalizeComparablePath(instance.detectedRepoRoot) ===
                            expectedRepoRoot &&
                        instance.vitePort,
                )
                .map((instance) => instance.vitePort),
        ),
    ];

    if (vitePorts.length === 1) {
        return vitePorts[0];
    }

    if (vitePorts.length > 1) {
        console.warn(
            `Multiple running Bloom instances from this worktree reported different Vite ports (${vitePorts.join(", ")}). Launching without an inherited Vite port.`,
        );
    }

    return undefined;
};

const effectiveVitePort =
    options.vitePort ?? (await tryInferVitePortFromRunningBloom());

const dotnetArgs = launchesUnderWatch
    ? ["watch", "run", "--project", projectPath, "--", "--automation"]
    : ["run", "--project", projectPath, "--", "--automation"];

if (!launchesUnderWatch) {
    console.log(
        "Starting Bloom WITHOUT dotnet watch. C# changes will not be picked up until you " +
            "restart (POST /restart, or the launcherControl --restart helper), and Bloom's " +
            "restart toast will not appear because nothing is watching for changes.",
    );
}

const startupLabel = getHelpfulStartupLabel(options.repoRoot);

if (startupLabel) {
    dotnetArgs.push("--label", startupLabel);
}

if (effectiveVitePort) {
    dotnetArgs.push("--vite-port", String(effectiveVitePort));
}

if (effectiveVitePort) {
    if (options.vitePort) {
        console.log(`Bloom Vite dev port: ${effectiveVitePort}`);
    } else {
        console.log(
            `Inherited Bloom Vite dev port from running worktree instance: ${effectiveVitePort}`,
        );
    }
}

const createForwardingLineWriter = (target, onLine) => {
    let buffered = "";

    const emitBufferedLines = (text) => {
        buffered += text;

        while (buffered.length > 0) {
            const crlfIndex = buffered.indexOf("\r\n");
            const lfIndex = buffered.indexOf("\n");
            const crIndex = buffered.indexOf("\r");
            const newlineIndexes = [crlfIndex, lfIndex, crIndex].filter(
                (index) => index >= 0,
            );

            if (newlineIndexes.length === 0) {
                return;
            }

            const lineEnd = Math.min(...newlineIndexes);
            const line = buffered.slice(0, lineEnd);
            const separatorLength = buffered.startsWith("\r\n", lineEnd)
                ? 2
                : 1;

            onLine?.(line);
            buffered = buffered.slice(lineEnd + separatorLength);
        }
    };

    return {
        write: (chunk) => {
            const text = chunk.toString();
            target.write(text);
            emitBufferedLines(text);
        },
        flush: () => {
            if (!buffered) {
                return;
            }

            onLine?.(buffered);
            buffered = "";
        },
    };
};

let child;
let childExited = false;
let bloomProcessId;
let bloomReadyAt;
let bloomMonitor;
let childExitCode = 0;
let childExitSignal;
let launchCompleted = false;
let launchFailed = false;
let launchTimeout;
let launchNumber = 0;
let activeLaunchToken = 0;
let restartRequested = false;
let awaitingManualRestart = false;
let restartInProgress = false;
// Full BLOOM_AUTOMATION_READY payload (processId/httpPort/cdpPort) for the
// current Bloom, kept so the control API can report the ports.
let lastAutomationInfo;
// Set when the control API deliberately stops Bloom: tells the watch child's
// exit handler to park in awaiting-restart instead of exiting the launcher.
let stopRequested = false;
// Set once we are exiting the launcher on purpose (teardownStack), so the exit
// handler stays quiet instead of inviting the developer to relaunch.
let tearingDownStack = false;
// When dotnet watch last reported a source-file change (it kills + rebuilds
// the app right after). Lets the Bloom-exit monitor tell "watch is rebuilding
// Bloom" from "the developer closed Bloom".
let lastWatchRestartSignalAt;
// Whether dotnet watch has reported ANY source change since the current Bloom
// reported ready — i.e. whether a restart would incorporate .NET changes.
// Conservative: an edit dotnet watch hot-reloaded still counts, since for
// Bloom a full restart is the only trusted way to take a .NET change.
let sourceChangedSinceReady = false;
// How fresh that signal must be to explain a Bloom exit. dotnet watch usually
// kills the app within a beat of printing the file-changed line, but on a busy
// machine the gap can stretch. Erring generous is deliberate: mistaking a rebuild
// for "the developer closed Bloom" destroys the whole dev stack, whereas the
// opposite mistake only delays freeing memory.
// This window is only trusted while Bloom actually died in it — see
// disarmSignalIfBloomSurvives, which throws the signal away once Bloom is seen
// surviving the change (i.e. the watcher hot-reloaded and will NOT relaunch).
const watchRestartSignalWindowMs = 30000;
// How long to let dotnet watch act on a file change before concluding it chose a
// hot reload over killing Bloom. Comfortably longer than the "prints the line,
// then kills the app" gap, and short enough that a close right after an edit is
// still read as a close.
const watchHotReloadGraceMs = 15000;
let hotReloadCheckTimer;

const isProcessRunning = (pid) => {
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

const stopBloomMonitor = () => {
    if (!bloomMonitor) {
        return;
    }

    clearInterval(bloomMonitor);
    bloomMonitor = undefined;
};

const clearLaunchTimeout = () => {
    if (!launchTimeout) {
        return;
    }

    clearTimeout(launchTimeout);
    launchTimeout = undefined;
};

const resetLaunchState = () => {
    childExited = false;
    bloomProcessId = undefined;
    bloomReadyAt = undefined;
    lastAutomationInfo = undefined;
    lastWatchRestartSignalAt = undefined;
    clearTimeout(hotReloadCheckTimer);
    hotReloadCheckTimer = undefined;
    sourceChangedSinceReady = false;
    // A restart won over any in-flight quit; drop the stale stop request so it
    // cannot mis-park a later, unrelated child exit.
    stopRequested = false;
    childExitCode = 0;
    childExitSignal = undefined;
    launchCompleted = false;
    launchFailed = false;
    awaitingManualRestart = false;
    stopBloomMonitor();
    clearLaunchTimeout();
};

const exitForFinishedLaunch = (exitCode = 0) => {
    stopBloomMonitor();
    clearLaunchTimeout();

    if (childExitSignal) {
        process.kill(process.pid, childExitSignal);
        return;
    }

    process.exit(exitCode);
};

const hasFreshWatchRestartSignal = () =>
    lastWatchRestartSignalAt !== undefined &&
    Date.now() - lastWatchRestartSignalAt < watchRestartSignalWindowMs;

// A file-changed line means a rebuild is imminent ONLY if dotnet watch is going to
// kill Bloom. When it hot-reloads instead, Bloom keeps running and no rebuilt
// instance ever announces itself — so nothing would clear the signal, and a close
// minutes later (well, up to watchRestartSignalWindowMs later) would be misread as
// "a rebuild is coming", leaving the whole stack waiting forever for a Bloom that
// is never rebuilt. So: if Bloom is still alive a beat after the change line, the
// watcher chose hot reload, and this signal no longer explains any later exit.
const disarmSignalIfBloomSurvives = () => {
    clearTimeout(hotReloadCheckTimer);
    hotReloadCheckTimer = setTimeout(() => {
        hotReloadCheckTimer = undefined;
        if (bloomProcessId && isProcessRunning(bloomProcessId)) {
            lastWatchRestartSignalAt = undefined;
        }
    }, watchHotReloadGraceMs);
};

// Bloom exited while dotnet watch is still alive. Two possibilities:
// - dotnet watch is rebuilding Bloom because a source file changed: keep the
//   stack alive and wait for the rebuilt Bloom to announce itself.
// - the developer closed Bloom (window X, File > Quit, crash): tear the whole
//   dev stack down so it stops holding memory once the developer has moved
//   on. Agents can start a fresh stack with launcherControl --ensure-running.
const handleBloomExitUnderWatch = async (exitedPid, runtimeMs) => {
    const launchToken = activeLaunchToken;
    console.log(
        `Bloom PID ${exitedPid} exited after ${runtimeMs} ms while dotnet watch remains active.`,
    );

    // dotnet watch prints its file-changed line right before killing the app,
    // but that output can land a beat after we notice the process die — give
    // it a moment before concluding the developer closed Bloom.
    if (!hasFreshWatchRestartSignal()) {
        await new Promise((resolve) => setTimeout(resolve, 2000));
    }

    // While we waited, a control-API restart/quit/shutdown may have taken
    // over, or the rebuilt Bloom may already have announced itself. Defer to it.
    if (
        launchToken !== activeLaunchToken ||
        restartInProgress ||
        stopRequested ||
        awaitingManualRestart ||
        // A rebuilt Bloom that announced itself during the wait replaced
        // bloomProcessId with its own live pid (a watch rebuild does not bump
        // activeLaunchToken, so this is what "someone got there first" looks
        // like). Note we cannot test launchCompleted here: it is still true
        // from the launch whose Bloom just exited.
        (bloomProcessId && isProcessRunning(bloomProcessId)) ||
        !isWatchChildAlive()
    ) {
        return;
    }

    if (hasFreshWatchRestartSignal()) {
        lastWatchRestartSignalAt = undefined;
        console.log(
            "dotnet watch is rebuilding Bloom after a file change. Waiting for the new instance...",
        );
        launchCompleted = false;
        bloomProcessId = undefined;
        bloomReadyAt = undefined;
        // Deliberately NO launch timeout while waiting for a watch rebuild. The
        // usual reason the rebuilt Bloom doesn't appear is a C# compile error:
        // dotnet watch has already killed Bloom, the build failed, and the
        // watcher now waits for the developer to fix the code — a human-paced
        // wait that easily outlasts launchTimeoutMs. Arming the timeout here
        // would make that ordinary typo exit the launcher, and go.mjs would
        // then tear down Vite too. The watch child is still alive, so a
        // successful rebuild announces itself through reportAutomationReady;
        // POST /restart (or Ctrl+C) is always available meanwhile.
        clearLaunchTimeout();
        return;
    }

    await teardownStack(
        "Bloom was closed. Shutting down the whole dev stack (dotnet watch, Vite) to free its memory. Start it again with ./go.sh; agents use launcherControl.mjs --ensure-running.",
    );
};

const startBloomMonitor = () => {
    if (bloomMonitor || !bloomProcessId) {
        return;
    }

    bloomMonitor = setInterval(() => {
        if (isProcessRunning(bloomProcessId)) {
            return;
        }

        const runtimeMs = bloomReadyAt ? Date.now() - bloomReadyAt : undefined;
        const exitedTooSoon =
            runtimeMs !== undefined && runtimeMs < shortLivedBloomMs;

        if (launchesUnderWatch && !exitedTooSoon) {
            stopBloomMonitor();
            void handleBloomExitUnderWatch(bloomProcessId, runtimeMs);
            return;
        }

        if (exitedTooSoon) {
            console.error(
                `Bloom PID ${bloomProcessId} exited ${runtimeMs} ms after reporting ready. Treating this as a failed launch.`,
            );
        } else {
            console.log(`Bloom PID ${bloomProcessId} exited.`);
        }

        exitForFinishedLaunch(exitedTooSoon ? 1 : childExitCode);
    }, bloomMonitorPollMs);
};

const normalizeAutomationInfo = (automationInfo) => {
    const processId = Number(automationInfo?.processId);
    const httpPort = Number(automationInfo?.httpPort);
    const cdpPort = Number(automationInfo?.cdpPort);

    if (!Number.isInteger(processId) || processId <= 0) {
        throw new Error(
            "automation startup info did not include a valid processId.",
        );
    }

    if (!Number.isInteger(httpPort) || httpPort <= 0) {
        throw new Error(
            "automation startup info did not include a valid httpPort.",
        );
    }

    if (!Number.isInteger(cdpPort) || cdpPort <= 0) {
        throw new Error(
            "automation startup info did not include a valid cdpPort.",
        );
    }

    return {
        processId,
        httpPort,
        cdpPort,
    };
};

const reportAutomationReady = (rawAutomationInfo) => {
    const automationInfo = normalizeAutomationInfo(rawAutomationInfo);

    if (childExited && !isProcessRunning(automationInfo.processId)) {
        console.error(
            `Bloom reported ready on HTTP ${automationInfo.httpPort}, but Bloom PID ${automationInfo.processId} was already gone by the time the launcher checked it.`,
        );
        launchFailed = true;
        exitForFinishedLaunch(childExitCode || 1);
        return;
    }

    stampLaunchPhase("Bloom reported automation-ready");
    launchCompleted = true;
    clearLaunchTimeout();
    bloomProcessId = automationInfo.processId;
    lastAutomationInfo = automationInfo;
    lastWatchRestartSignalAt = undefined;
    sourceChangedSinceReady = false;
    bloomReadyAt = Date.now();
    stopBloomMonitor();
    awaitingManualRestart = false;

    console.log(
        `Bloom ready. HTTP ${automationInfo.httpPort}, CDP ${automationInfo.cdpPort}, Bloom PID ${automationInfo.processId}.`,
    );

    if (childExited && !launchesUnderWatch) {
        console.log(
            `dotnet exited, but Bloom PID ${automationInfo.processId} is still running. Continuing to monitor that Bloom process.`,
        );
    }

    startBloomMonitor();
};

// Phase timings for a launch, so "it timed out" can be answered with "doing what?".
// Elapsed is measured from the moment we spawn dotnet, which is also when the launch timeout
// starts, so these numbers add up to the budget that timeout is policing.
let launchStartedAt;
let lastPhaseAt;
const stampLaunchPhase = (label) => {
    const now = Date.now();
    if (launchStartedAt === undefined) {
        launchStartedAt = now;
        lastPhaseAt = now;
    }
    const sinceStart = ((now - launchStartedAt) / 1000).toFixed(1);
    const sinceLast = ((now - lastPhaseAt) / 1000).toFixed(1);
    lastPhaseAt = now;
    console.log(
        `launch phase: ${label} (+${sinceLast}s, ${sinceStart}s total)`,
    );
};

// The lines dotnet watch prints as it works through a build. We only want the timings, so match
// loosely on the distinctive words rather than the emoji, which vary by console encoding.
const buildPhaseOfWatchLine = (line) => {
    if (/dotnet watch.*Building\b/.test(line)) return "msbuild started";
    if (/Build succeeded/.test(line)) return "msbuild succeeded";
    if (/Determining projects to restore|Restored .*\.csproj/.test(line))
        return "nuget restore";
    if (/Hot reload enabled/.test(line)) return "dotnet watch ready";
    return undefined;
};

const handleOutputLine = (launchToken, line) => {
    if (launchToken !== activeLaunchToken) {
        return;
    }

    const buildPhase = buildPhaseOfWatchLine(line);
    if (buildPhase) {
        stampLaunchPhase(buildPhase);
    }

    if (isDotnetWatchRestartSignal(line)) {
        lastWatchRestartSignalAt = Date.now();
        sourceChangedSinceReady = true;
        disarmSignalIfBloomSurvives();
    }

    if (!line.startsWith(automationReadyPrefix)) {
        return;
    }

    try {
        reportAutomationReady(
            JSON.parse(line.slice(automationReadyPrefix.length)),
        );
    } catch (error) {
        console.error(
            `Could not parse ${automationReadyPrefix.trim()} payload: ${error instanceof Error ? error.message : String(error)}`,
        );
    }
};

const terminateChild = (targetChild) =>
    new Promise((resolve) => {
        if (
            !targetChild ||
            targetChild.exitCode !== null ||
            targetChild.signalCode
        ) {
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

        targetChild.once("exit", finish);

        try {
            targetChild.kill("SIGINT");
        } catch {
            finish();
            return;
        }

        forceTimer = setTimeout(() => {
            if (settled) {
                return;
            }

            if (process.platform === "win32") {
                const killer = spawn(
                    "taskkill",
                    ["/pid", String(targetChild.pid), "/t", "/f"],
                    {
                        stdio: "ignore",
                        shell: false,
                    },
                );

                killer.on("exit", finish);
                killer.on("error", finish);
                return;
            }

            try {
                targetChild.kill("SIGTERM");
            } catch {
                finish();
                return;
            }

            setTimeout(finish, 250);
        }, 1500);
    });

const startLaunchTimeout = () => {
    if (launchTimeoutMs === 0) {
        // Explicitly disabled via BLOOM_LAUNCH_TIMEOUT_MS=0: wait indefinitely. Useful on a
        // machine where the build time is wildly variable, and when measuring what a launch
        // actually costs without the launcher pulling the stack down mid-build.
        return;
    }
    launchTimeout = setTimeout(() => {
        if (launchCompleted || launchFailed) {
            return;
        }

        launchFailed = true;
        stampLaunchPhase("GAVE UP");
        console.error(
            `Bloom did not emit ${automationReadyPrefix.trim()} within ${launchTimeoutMs} ms. ` +
                `The phase timings above show how far it got; that clock covers dotnet watch's ` +
                `startup, any restore, the build, and Bloom's own initialization. If the build ` +
                `simply needs longer on this machine, set BLOOM_LAUNCH_TIMEOUT_MS.`,
        );
        exitForFinishedLaunch(childExitCode || 1);
    }, launchTimeoutMs);
};

const spawnWatchChild = () => {
    resetLaunchState();
    launchNumber++;
    activeLaunchToken++;
    const launchToken = activeLaunchToken;

    child = spawn("dotnet", dotnetArgs, {
        stdio: ["ignore", "pipe", "pipe"],
        shell: false,
    });

    console.log(`dotnet PID: ${child.pid} (launch ${launchNumber})`);

    const stdoutWriter = createForwardingLineWriter(process.stdout, (line) =>
        handleOutputLine(launchToken, line),
    );
    const stderrWriter = createForwardingLineWriter(process.stderr, (line) =>
        handleOutputLine(launchToken, line),
    );

    child.stdout.on("data", stdoutWriter.write);
    child.stderr.on("data", stderrWriter.write);
    child.stdout.on("end", stdoutWriter.flush);
    child.stderr.on("end", stderrWriter.flush);

    launchStartedAt = undefined; // restart the phase clock with the launch timeout
    stampLaunchPhase("dotnet spawned");
    startLaunchTimeout();

    child.on("error", (error) => {
        if (launchToken !== activeLaunchToken) {
            return;
        }

        console.error(`Failed to start dotnet: ${error.message}`);
        launchFailed = true;
        exitForFinishedLaunch(1);
    });

    child.on("exit", (code, signal) => {
        if (launchToken !== activeLaunchToken) {
            return;
        }

        childExited = true;
        childExitCode = code ?? 0;
        childExitSignal = signal ?? undefined;

        if (restartRequested) {
            restartRequested = false;
            restartInProgress = false;
            if (!launchFailed) {
                console.log("Starting a fresh Bloom watch cycle...");
            }
            spawnWatchChild();
            return;
        }

        // A control-API quit deliberately stopped Bloom and its watch child.
        // Park in awaiting-restart (like the human closing Bloom's window)
        // instead of exiting the launcher. Checked after restartRequested so
        // a restart requested mid-quit wins.
        if (tearingDownStack) {
            // teardownStack is about to process.exit; say nothing.
            return;
        }

        if (stopRequested) {
            stopRequested = false;
            stopBloomMonitor();
            clearLaunchTimeout();
            if (!awaitingManualRestart) {
                awaitingManualRestart = true;
                console.log(
                    "Bloom stopped by control request. Press Enter here (or POST /restart to the control API) to rebuild and relaunch.",
                );
            }
            return;
        }

        if (
            bloomProcessId &&
            isProcessRunning(bloomProcessId) &&
            !launchesUnderWatch
        ) {
            console.log(
                `dotnet exited${code !== null ? ` with code ${code}` : ""}, but Bloom PID ${bloomProcessId} is still running. Waiting for Bloom to exit before this launcher exits.`,
            );
            startBloomMonitor();
            return;
        }

        if (!bloomProcessId) {
            console.log(
                "dotnet exited before Bloom reported automation-ready startup info. Waiting briefly for Bloom to appear.",
            );
            return;
        }

        exitForFinishedLaunch(childExitCode);
    });
};

const isChildAlive = (candidate) =>
    !!candidate && candidate.exitCode === null && !candidate.signalCode;

const isWatchChildAlive = () => isChildAlive(child);

const restartWatchChild = async () => {
    if (restartInProgress) {
        return;
    }

    if (!isWatchChildAlive()) {
        // The watch child is already gone (e.g. after a control-API quit), so
        // there is no exit event coming to respawn for us. Spawn directly.
        awaitingManualRestart = false;
        console.log("Restarting Bloom...");
        spawnWatchChild();
        return;
    }

    // Remember which child we set out to recycle: closing Bloom below takes a
    // while, and the global `child` can be replaced meanwhile.
    const childToRecycle = child;
    const launchToken = activeLaunchToken;

    restartInProgress = true;
    restartRequested = true;
    awaitingManualRestart = false;
    console.log("Restarting Bloom...");
    // Stop watching Bloom's pid before we take it down on purpose, and close
    // Bloom via WM_CLOSE first so it saves its state; only then recycle the
    // dotnet watch child (whose tree-kill would hard-kill Bloom otherwise).
    stopBloomMonitor();
    clearLaunchTimeout();
    await closeAnyRunningBloom();

    // If the watch child exited on its own while we were closing Bloom (losing
    // Bloom often takes dotnet watch down with it), its exit handler already
    // honored the restartRequested flag by spawning a replacement — that fresh
    // cycle IS our restart. Terminating it here, through a `child` that now
    // points at the replacement, would undo the restart and leave the launcher
    // with nothing able to start Bloom.
    if (launchToken !== activeLaunchToken) {
        console.log(
            "The watch child recycled itself while Bloom was closing; that fresh cycle is the restart.",
        );
        return;
    }

    await terminateChild(childToRecycle);
};

const runTaskkill = (args) =>
    new Promise((resolve) => {
        const killer = spawn("taskkill", args, {
            stdio: "ignore",
            shell: false,
        });
        killer.on("exit", resolve);
        killer.on("error", () => resolve(undefined));
    });

const waitForProcessExit = async (pid, timeoutMs) => {
    const deadline = Date.now() + timeoutMs;

    while (Date.now() < deadline) {
        if (!isProcessRunning(pid)) {
            return true;
        }

        await new Promise((resolve) => setTimeout(resolve, 250));
    }

    return !isProcessRunning(pid);
};

const quitBloomProcessGracefully = async (pid) => {
    if (!isProcessRunning(pid)) {
        return;
    }

    if (process.platform === "win32") {
        // taskkill without /f sends WM_CLOSE — the same as the user clicking
        // the window's X, so Bloom saves and exits cleanly.
        await runTaskkill(["/pid", String(pid)]);
        if (await waitForProcessExit(pid, 10000)) {
            return;
        }

        console.log(
            `Bloom PID ${pid} did not close gracefully within 10 s (a modal dialog may be blocking it); force-killing.`,
        );
        await runTaskkill(["/pid", String(pid), "/f"]);
        await waitForProcessExit(pid, 5000);
        return;
    }

    try {
        process.kill(pid, "SIGTERM");
    } catch {}
    if (await waitForProcessExit(pid, 10000)) {
        return;
    }

    try {
        process.kill(pid, "SIGKILL");
    } catch {}
};

// Closes whatever Bloom is running right now, and keeps going if another one turns
// up while we are working: a dotnet-watch rebuild can announce a replacement Bloom
// while we are still closing the previous one, and because the watch child is the
// same process that does NOT bump activeLaunchToken. Any Bloom left alive when we
// then stop the watch child is orphaned, not killed with it — terminateChild kills
// the watcher by pid, so Windows leaves its descendants running, and a stray Bloom
// means port conflicts and a duplicate instance next time.
// Bounded on purpose: each pass closes at most one Bloom, and a watcher that keeps
// producing new instances faster than we close them is a runaway we should not spin
// on forever.
const closeAnyRunningBloom = async () => {
    for (let attempt = 0; attempt < 3; attempt++) {
        const pid = bloomProcessId;
        if (!pid || !isProcessRunning(pid)) {
            return;
        }

        await quitBloomProcessGracefully(pid);

        if (bloomProcessId === pid) {
            return; // nothing newer announced itself while we closed that one
        }

        console.log(
            `A rebuilt Bloom (PID ${bloomProcessId}) appeared while PID ${pid} was closing; closing it too.`,
        );
    }
};

// Control-API action: durably stop Bloom. Unlike the human closing Bloom's
// window (which leaves dotnet watch alive, so the next C# edit silently
// respawns Bloom), this also stops the watch child; the launcher parks in
// awaiting-restart until Enter or POST /restart.
const quitBloom = async () => {
    clearLaunchTimeout();
    stopBloomMonitor();

    // Remember which watch child we set out to stop, and which launch we are
    // acting on: closing Bloom below can take up to 15 s, and a /restart in
    // that window recycles both.
    const childToStop = child;
    const launchToken = activeLaunchToken;

    // Set BEFORE taking Bloom down: if the watch child happens to exit while we
    // wait, its exit handler must see that this was a deliberate stop.
    // Otherwise it would fall through to exitForFinishedLaunch and take the
    // whole stack (including Vite) down, which is the opposite of what
    // /quit-bloom promises.
    stopRequested = true;

    await closeAnyRunningBloom();

    // A /restart that arrived while we were closing Bloom has already replaced
    // the watch child (and cleared stopRequested). Terminating that replacement
    // would kill the only process able to launch Bloom, so let the restart win.
    if (launchToken !== activeLaunchToken) {
        console.log(
            "A restart took over while Bloom was closing; leaving the new watch cycle alone.",
        );
        return;
    }

    if (isChildAlive(childToStop)) {
        await terminateChild(childToStop);
        return;
    }

    stopRequested = false;

    if (!awaitingManualRestart) {
        awaitingManualRestart = true;
        console.log(
            "Bloom stopped by control request. Press Enter here (or POST /restart to the control API) to rebuild and relaunch.",
        );
    }
};

// Tears down the whole dev stack: gracefully quits Bloom, stops the dotnet
// watch child, then exits this process — which makes go.mjs's exe-exit
// handler shut down the Vite dev server and the rest.
const teardownStack = async (reason) => {
    console.log(reason);
    // Both flags: stopRequested keeps the child's exit handler from treating
    // this as a finished launch, and tearingDownStack keeps it from printing
    // the "press Enter to relaunch" invitation into a terminal that is exiting.
    tearingDownStack = true;
    stopRequested = true;
    clearLaunchTimeout();
    stopBloomMonitor();

    await closeAnyRunningBloom();

    await terminateChild(child);
    process.exit(0);
};

// Control-API action: tear down the whole dev stack on request.
const shutdownStack = () =>
    teardownStack(
        "Shutting down the Bloom launcher stack by control request...",
    );

if (process.stdin.readable) {
    process.stdin.setEncoding("utf8");
    process.stdin.on("data", (chunk) => {
        if (!awaitingManualRestart || !isManualRestartCommand(chunk)) {
            return;
        }

        void restartWatchChild();
    });
    process.stdin.resume();
}

// --- Launcher control surface -----------------------------------------------
// A loopback-only HTTP server through which agents can query and drive this
// launcher (see watchBloomExeControl.mjs and the bloom-automation skill).
// Discovered via output/bloom-launcher.json and the BLOOM_LAUNCHER_READY line.
// Started BEFORE the first dotnet spawn so the control port can be passed to
// Bloom (--launcher-port), which uses it for its in-app restart toast.

const launcherStartedAt = new Date().toISOString();
// In the go.sh flow our parent is go.mjs, which owns the Vite dev server.
const goPid = process.ppid;
const discoveryFilePath = getDiscoveryFilePath(options.repoRoot);

const getControlSnapshot = () => ({
    restartInProgress,
    launchCompleted,
    launchFailed,
    awaitingManualRestart,
    bloomRunning: !!bloomProcessId && isProcessRunning(bloomProcessId),
    watchChildAlive: isWatchChildAlive(),
    launchNumber,
    dotnetWatchPid: child?.pid,
    bloomProcessId,
    httpPort: lastAutomationInfo?.httpPort,
    cdpPort: lastAutomationInfo?.cdpPort,
    vitePort: effectiveVitePort,
    sourceChangedSinceReady,
    label: startupLabel,
    repoRoot: options.repoRoot,
    launcherPid: process.pid,
    goPid,
    logPath: process.env.BLOOM_LAUNCHER_LOG || undefined,
    startedAt: launcherStartedAt,
    bloomReadyAt,
});

const warnIfAnotherLauncherIsLive = async () => {
    const existing = readDiscoveryFile(discoveryFilePath);
    if (!existing?.controlUrl || existing.launcherPid === process.pid) {
        return;
    }

    try {
        const response = await fetch(`${existing.controlUrl}/status`, {
            signal: AbortSignal.timeout(1000),
        });
        if (response.ok) {
            console.warn(
                `Another Bloom launcher (PID ${existing.launcherPid}) appears to be running for this worktree at ${existing.controlUrl}. Overwriting its discovery file; the last launcher started wins.`,
            );
        }
    } catch {
        // Nobody home: the file is stale, which is normal after a hard kill.
    }
};

const startControlSurface = async () => {
    try {
        await warnIfAnotherLauncherIsLive();

        const { port } = await startControlServer({
            actions: { restart: restartWatchChild, quitBloom, shutdownStack },
            getSnapshot: getControlSnapshot,
            log: console.log,
        });

        const controlUrl = `http://127.0.0.1:${port}`;
        writeDiscoveryFile(discoveryFilePath, {
            schemaVersion: discoveryFileSchemaVersion,
            kind: "bloom-launcher",
            controlPort: port,
            controlUrl,
            launcherPid: process.pid,
            goPid,
            repoRoot: options.repoRoot,
            label: startupLabel,
            vitePort: effectiveVitePort,
            logPath: process.env.BLOOM_LAUNCHER_LOG || undefined,
            startedAt: launcherStartedAt,
        });

        process.on("exit", () => {
            // Only remove the file if it is still ours; a newer launcher may
            // have overwritten it while we were shutting down.
            if (
                readDiscoveryFile(discoveryFilePath)?.launcherPid ===
                process.pid
            ) {
                removeDiscoveryFile(discoveryFilePath);
            }
        });

        console.log(
            `Launcher control: ${controlUrl} (state file: ${discoveryFilePath})`,
        );
        console.log(
            `${launcherReadyPrefix}${JSON.stringify({
                controlPort: port,
                controlUrl,
                launcherPid: process.pid,
                repoRoot: options.repoRoot,
            })}`,
        );

        return port;
    } catch (error) {
        // The launcher still works for the human without the control surface.
        console.error(
            `Could not start the launcher control server: ${error instanceof Error ? error.message : String(error)}`,
        );
        return undefined;
    }
};

const controlPort = await startControlSurface();
if (controlPort) {
    // Bloom's dev-only restart toast polls /status here and posts /restart back
    // to this port (see DevLauncher.cs).
    dotnetArgs.push("--launcher-port", String(controlPort));
}

spawnWatchChild();
