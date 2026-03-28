import { spawn } from "node:child_process";
import { existsSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
    requireOptionValue,
    requireTcpPortOption,
} from "../.github/skills/bloom-automation/bloomProcessCommon.mjs";
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

const dotnetArgs = [
    "watch",
    "run",
    "--project",
    projectPath,
    "--",
    "--automation",
];

const startupLabel = getHelpfulStartupLabel(options.repoRoot);

if (startupLabel) {
    dotnetArgs.push("--label", startupLabel);
}

if (options.vitePort) {
    dotnetArgs.push("--vite-port", String(options.vitePort));
}

if (options.vitePort) {
    console.log(`Bloom Vite dev port: ${options.vitePort}`);
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

const child = spawn("dotnet", dotnetArgs, {
    stdio: ["ignore", "pipe", "pipe"],
    shell: false,
});

console.log(`dotnet PID: ${child.pid}`);
console.log(
    "watchBloomExe.mjs tracks the launched Bloom instance until it exits. Treat the latest 'Bloom ready.' line as the launch success signal and keep this terminal open while you target the reported HTTP port.",
);

let childExited = false;
let bloomProcessId;
let bloomReadyAt;
let bloomMonitor;
let childExitCode = 0;
let childExitSignal;
let launchCompleted = false;
let launchFailed = false;

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
    clearTimeout(launchTimeout);

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
    clearTimeout(launchTimeout);
    bloomProcessId = automationInfo.processId;
    bloomReadyAt = Date.now();
    stopBloomMonitor();

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

const handleOutputLine = (line) => {
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

const stdoutWriter = createForwardingLineWriter(
    process.stdout,
    handleOutputLine,
);
const stderrWriter = createForwardingLineWriter(
    process.stderr,
    handleOutputLine,
);

child.stdout.on("data", stdoutWriter.write);
child.stderr.on("data", stderrWriter.write);
child.stdout.on("end", stdoutWriter.flush);
child.stderr.on("end", stderrWriter.flush);

const launchTimeout = setTimeout(() => {
    if (launchCompleted || launchFailed) {
        return;
    }

    launchFailed = true;
    console.error(
        `Bloom did not emit ${automationReadyPrefix.trim()} within ${launchTimeoutMs} ms.`,
    );
    exitForFinishedLaunch(childExitCode || 1);
}, launchTimeoutMs);

child.on("error", (error) => {
    console.error(`Failed to start dotnet: ${error.message}`);
    launchFailed = true;
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
