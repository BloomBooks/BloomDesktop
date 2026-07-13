import { spawn } from "node:child_process";
import { existsSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
    findRunningStandardBloomInstances,
    requireOptionValue,
    requireTcpPortOption,
} from "../.github/skills/bloom-automation/bloomProcessCommon.mjs";
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
    };

    for (let i = 0; i < args.length; i++) {
        const arg = args[i];

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
                "Unsupported option. Supported options are --repo-root and --vite-port.",
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

const launchTimeoutMs = 120000;
const bloomMonitorPollMs = 500;
const shortLivedBloomMs = 5000;
const launchesUnderWatch = true;
const restartPrompt =
    "Bloom is closed. Press Enter to relaunch, or press Ctrl+C to stop watching.";
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

const dotnetArgs = [
    "watch",
    "run",
    "--project",
    projectPath,
    "--",
    "--automation",
];

// `pnpm go` is the HUMAN launcher: --attended keeps --automation's ready-handshake and
// single-instance bypass but turns its unattended-UI policies back off (dialog auto-close,
// problem-report suppression). Without it, any warning in the Team Collection startup sync
// auto-closed the sync dialog and silently killed the launch (dogfood bug #16, 13 Jul 2026).
// Set BLOOM_GO_UNATTENDED=1 for a fully unattended launch (agent-driven CDP runs that must
// never block on a dialog); the E2E harness has its own launcher and is unaffected either way.
if (process.env.BLOOM_GO_UNATTENDED !== "1") {
    dotnetArgs.push("--attended");
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
            console.log(
                `Bloom PID ${bloomProcessId} exited after ${runtimeMs} ms while dotnet watch remains active.`,
            );
            stopBloomMonitor();
            awaitingManualRestart = true;
            if (process.stdin.isTTY) {
                console.log(restartPrompt);
            }
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

    launchCompleted = true;
    clearLaunchTimeout();
    bloomProcessId = automationInfo.processId;
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

const handleOutputLine = (launchToken, line) => {
    if (launchToken !== activeLaunchToken) {
        return;
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
    launchTimeout = setTimeout(() => {
        if (launchCompleted || launchFailed) {
            return;
        }

        launchFailed = true;
        console.error(
            `Bloom did not emit ${automationReadyPrefix.trim()} within ${launchTimeoutMs} ms.`,
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

const restartWatchChild = async () => {
    if (restartInProgress) {
        return;
    }

    restartInProgress = true;
    restartRequested = true;
    awaitingManualRestart = false;
    console.log("Restarting Bloom...");
    await terminateChild(child);
};

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

spawnWatchChild();
