/* eslint-env node */
/* global console, process */
import { spawn } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { pipeChildOutput } from "./childOutput.mjs";
import { startViteDevServer } from "./viteDevServer.mjs";
import {
    sweepStaleWorktreeNodeProcesses,
    terminateChildProcess,
} from "./processTree.mjs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const browserUIRoot = path.resolve(__dirname, "..");
const repoRoot = path.resolve(browserUIRoot, "..", "..");
const exeScriptPath = path.join(repoRoot, "scripts", "watchBloomExe.mjs");
process.env.feedback = "off";

const parsePositiveInteger = (value) => {
    const parsed = Number.parseInt(value, 10);
    if (Number.isInteger(parsed) && parsed > 0 && parsed <= 65535) {
        return parsed;
    }

    return undefined;
};

const requireOptionValue = (args, index, optionName) => {
    const value = args[index + 1];
    if (!value || value.startsWith("--")) {
        throw new Error(`${optionName} requires a value.`);
    }

    return value;
};

const parseRequiredPortValue = (optionName, value) => {
    const parsed = parsePositiveInteger(value);
    if (!parsed) {
        throw new Error(
            `${optionName} must be an integer from 1 to 65535. Received: ${value}`,
        );
    }

    return parsed;
};

const parseArgs = () => {
    const args = process.argv.slice(2);
    const options = {
        vitePort: undefined,
    };

    for (let index = 0; index < args.length; index++) {
        const arg = args[index];

        if (arg === "--vite-port") {
            options.vitePort = parseRequiredPortValue(
                "--vite-port",
                requireOptionValue(args, index, "--vite-port"),
            );
            index++;
            continue;
        }

        if (arg.startsWith("--vite-port=")) {
            options.vitePort = parseRequiredPortValue(
                "--vite-port",
                arg.slice("--vite-port=".length),
            );
            continue;
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

const children = [];
let isShuttingDown = false;

const shutdown = async (exitCode = 0) => {
    if (isShuttingDown) {
        return;
    }

    isShuttingDown = true;
    const normalizedExitCode = Number.isInteger(exitCode) ? exitCode : 1;
    console.log(`[go] Shutting down (exit ${normalizedExitCode})...`);

    // terminateChildProcess kills each child's WHOLE subtree; see its doc
    // comment in processTree.mjs for why signal propagation can't be trusted
    // (the dev.mjs subtree — Vite + ~7 watchers and their own command children —
    // would orphan on Windows otherwise).
    await Promise.all(children.map((child) => terminateChildProcess(child)));
    process.exit(normalizedExitCode);
};

process.on("SIGINT", () => {
    void shutdown(0);
});

process.on("SIGTERM", () => {
    void shutdown(0);
});

const startBloomExe = (vitePort) => {
    const args = [
        exeScriptPath,
        "--repo-root",
        repoRoot,
        "--vite-port",
        String(vitePort),
    ];

    const child = spawn(process.execPath, args, {
        cwd: browserUIRoot,
        stdio: ["inherit", "pipe", "pipe"],
        shell: false,
    });

    children.push(child);
    pipeChildOutput(child, "[exe] ");

    child.on("error", (error) => {
        if (isShuttingDown) {
            return;
        }

        console.error(`[go] Failed to start Bloom exe flow: ${error.message}`);
        void shutdown(1);
    });

    child.on("exit", (code, signal) => {
        if (isShuttingDown) {
            return;
        }

        const detail = signal ? `signal ${signal}` : `code ${code ?? 0}`;
        console.error(`[go] Bloom exe flow exited with ${detail}.`);
        void shutdown(code ?? 1);
    });
};

const main = async () => {
    // Defensive sweep: a prior launcher that was hard-killed (terminal closed,
    // SIGKILL, timeout) cannot run its shutdown handlers, so its Vite + watcher
    // node processes orphan. Reap any such leftovers from THIS worktree before we
    // start, so a previous leak can't starve the machine and wreck this run. We
    // match on this worktree's repo-root path (which appears in the command lines
    // of dev.mjs, watchBloomExe.mjs, and the Vite/onchange bins) and exclude our
    // own pid. Killing a tree root takes its relative-path descendants
    // (watchLess, onchange's command children) with it.
    await sweepStaleWorktreeNodeProcesses({
        commandLineMarker: repoRoot,
        excludePids: [process.pid],
        log: (message) => console.log(`[go] ${message}`),
    });

    console.log(
        "[go] Launching Vite first and waiting for it to go quiet before starting Bloom.",
    );
    const dev = await startViteDevServer({
        repoRoot,
        requestedPort: options.vitePort,
        registerChild: (child) => children.push(child),
        isShuttingDown: () => isShuttingDown,
        onUnexpectedExit: (exitCode) => void shutdown(exitCode),
        log: (message) => console.log(`[go] ${message}`),
    });
    console.log(
        `[go] Vite is reachable and quiet on port ${dev.port}. Starting Bloom...`,
    );
    startBloomExe(dev.port);
};

main().catch((error) => {
    console.error(`[go] ${error.message}`);
    void shutdown(1);
});
