import { spawn } from "node:child_process";
import { existsSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
    acquireBloomPortLease,
    formatBloomPortPlan,
    releaseBloomPortLease,
    waitForBloomInstanceInfo,
} from "../.github/skills/bloom-automation/bloomProcessCommon.mjs";

const parseArgs = () => {
    const args = process.argv.slice(2);
    const options = {
        repoRoot: path.resolve(
            path.dirname(fileURLToPath(import.meta.url)),
            "..",
        ),
        httpPort: undefined,
        cdpPort: undefined,
    };

    for (let i = 0; i < args.length; i++) {
        const arg = args[i];

        if (arg === "--repo-root") {
            options.repoRoot = args[i + 1] || options.repoRoot;
            i++;
            continue;
        }

        if (arg === "--http-port") {
            options.httpPort = args[i + 1];
            i++;
            continue;
        }

        if (arg.startsWith("--http-port=")) {
            options.httpPort = arg.slice("--http-port=".length);
            continue;
        }

        if (arg === "--cdp-port") {
            options.cdpPort = args[i + 1];
            i++;
            continue;
        }

        if (arg.startsWith("--cdp-port=")) {
            options.cdpPort = arg.slice("--cdp-port=".length);
        }
    }

    return options;
};

const options = parseArgs();
const launchTimeoutMs = 120000;
const bloomMonitorPollMs = 500;
const shortLivedBloomMs = 5000;
const launchesUnderWatch = true;
const projectPath = path.join(
    options.repoRoot,
    "src",
    "BloomExe",
    "BloomExe.csproj",
);
const worktreeLabel = "/" + path.basename(path.resolve(options.repoRoot)) + "/";

if (!existsSync(projectPath)) {
    console.error(
        `Bloom project not found at ${projectPath}. Verify --repo-root or run the command from this worktree.`,
    );
    process.exit(1);
}

const lease = await acquireBloomPortLease({
    httpPort: options.httpPort,
    cdpPort: options.cdpPort,
});
const portPlan = lease.portPlan;
const dotnetArgs = [
    "watch",
    "run",
    "--project",
    projectPath,
    "--",
    "--http-port",
    String(portPlan.httpPort),
    "--cdp-port",
    String(portPlan.cdpPort),
    "--label",
    worktreeLabel,
];

console.log(`Bloom launch ports: ${formatBloomPortPlan(portPlan)}`);

const child = spawn("dotnet", dotnetArgs, {
    stdio: "inherit",
    shell: false,
});

console.log(`dotnet PID: ${child.pid}`);
console.log(
    "watchBloomExe.mjs tracks the launched Bloom instance until it exits. Treat the 'Bloom ready.' line as the launch success signal and keep this terminal open while you target the reported HTTP port.",
);

let childExited = false;
let leaseReleased = false;
let bloomProcessId;
let bloomReadyAt;
let bloomMonitor;
let childExitCode = 0;
let childExitSignal;

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

const exitForFinishedLaunch = (exitCode = 0) => {
    stopBloomMonitor();
    releaseLease();

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
            if (
                launchesUnderWatch &&
                bloomReadyAt &&
                Date.now() - bloomReadyAt >= shortLivedBloomMs
            ) {
                stopBloomMonitor();
            }
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

const releaseLease = () => {
    if (leaseReleased) {
        return;
    }

    leaseReleased = true;
    releaseBloomPortLease(lease);
};

process.on("exit", releaseLease);

waitForBloomInstanceInfo(portPlan.httpPort, launchTimeoutMs)
    .then((instanceInfo) => {
        if (childExited && !isProcessRunning(instanceInfo.processId)) {
            console.error(
                `Bloom reported ready on HTTP ${instanceInfo.httpPort}, but Bloom PID ${instanceInfo.processId} was already gone by the time the launcher checked it.`,
            );
            exitForFinishedLaunch(childExitCode || 1);
            return;
        }

        bloomProcessId = instanceInfo.processId;
        bloomReadyAt = Date.now();

        console.log(
            `Bloom ready. HTTP ${instanceInfo.httpPort}, websocket ${instanceInfo.webSocketPort || portPlan.webSocketPort}, CDP ${instanceInfo.cdpPort || portPlan.cdpPort}, Bloom PID ${instanceInfo.processId}.`,
        );

        if (childExited && !launchesUnderWatch) {
            console.log(
                `dotnet exited, but Bloom PID ${instanceInfo.processId} is still running. Continuing to monitor that Bloom process and hold the port lease.`,
            );
        }

        startBloomMonitor();
    })
    .catch((error) => {
        console.error(error.message);

        if (childExited) {
            exitForFinishedLaunch(childExitCode || 1);
        }
    });

child.on("error", (error) => {
    console.error(`Failed to start dotnet: ${error.message}`);
    exitForFinishedLaunch(1);
});

child.on("exit", (code, signal) => {
    childExited = true;
    childExitCode = code ?? 0;
    childExitSignal = signal ?? undefined;

    if (
        bloomProcessId &&
        isProcessRunning(bloomProcessId) &&
        !launchesUnderWatch
    ) {
        console.log(
            `dotnet exited${code !== null ? ` with code ${code}` : ""}, but Bloom PID ${bloomProcessId} is still running. Waiting for Bloom to exit before releasing the port lease.`,
        );
        startBloomMonitor();
        return;
    }

    if (!bloomProcessId) {
        console.log(
            `dotnet exited before Bloom reported ready on HTTP ${portPlan.httpPort}. Waiting briefly for Bloom to appear.`,
        );
        return;
    }

    exitForFinishedLaunch(childExitCode);
});
