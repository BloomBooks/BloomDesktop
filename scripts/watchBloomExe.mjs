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
import {
    automationReadyPrefix,
    makeAutomationReadyScanner,
} from "../src/BloomBrowserUI/scripts/automationReady.mjs";
import { pipeChildOutput } from "../src/BloomBrowserUI/scripts/childOutput.mjs";
import { terminateChildProcess } from "../src/BloomBrowserUI/scripts/processTree.mjs";

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

    // A fresh scanner per launch, with both callbacks gated on launchToken so
    // trailing output from a terminated previous launch (whose streams may
    // still drain after a restart) can never trigger ready handling — or parse
    // errors — for the current one.
    const scanner = makeAutomationReadyScanner(
        (info) => {
            if (launchToken !== activeLaunchToken) {
                return;
            }

            reportAutomationReady(info);
        },
        (error) => {
            if (launchToken !== activeLaunchToken) {
                return;
            }

            console.error(
                `Could not parse ${automationReadyPrefix.trim()} payload: ${error instanceof Error ? error.message : String(error)}`,
            );
        },
    );

    pipeChildOutput(child, "", scanner);

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
    // signalFirst: give dotnet watch a chance to shut down cleanly on SIGINT
    // before its tree is force-killed (see processTree.mjs).
    await terminateChildProcess(child, { signalFirst: true });
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
