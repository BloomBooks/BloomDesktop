/* eslint-env node */
/* global AbortSignal, clearTimeout, console, fetch, process, setTimeout */
import { spawn } from "node:child_process";
import net from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
    killProcessTree,
    sweepStaleWorktreeNodeProcesses,
} from "./processTree.mjs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const browserUIRoot = path.resolve(__dirname, "..");
const repoRoot = path.resolve(browserUIRoot, "..", "..");
const devScriptPath = path.join(browserUIRoot, "scripts", "dev.mjs");
const exeScriptPath = path.join(repoRoot, "scripts", "watchBloomExe.mjs");
process.env.feedback = "off";
const startupQuietMs = 1500;
const viteHealthTimeoutMs = 30000;
const viteHealthPollMs = 250;
// Per-request timeout for a single /@vite/client probe. This must comfortably
// exceed Vite's real cold-start response latency, NOT just its steady-state
// latency (a few ms). We probe at the most CPU-contended moment of startup:
// Vite is still pre-bundling deps (optimizeDeps for jquery/comicaljs), the 7
// file watchers are doing their initial scans, and LESS is compiling ~180
// stylesheets, so Vite's event loop stalls in bursts. Measured latency under
// that load reaches ~2.9s (p99), while steady state is <10ms. A 500ms timeout
// (the previous value) spuriously aborted every probe during this window, so
// waitForViteClient never got its 2 consecutive successes and the whole launch
// failed. A slow-but-listening server is healthy, not broken; a genuinely dead
// server still fails fast via ECONNREFUSED, so this longer timeout only affects
// the busy-but-fine case.
const viteHealthRequestTimeoutMs = 3000;
const maxRandomVitePortAttempts = 10;
const gracefulShutdownMs = 1500;
// Probe both loopback families: Vite may bind only IPv6 (::1) on some machines,
// and Node's fetch resolves "localhost" to IPv4 (127.0.0.1) first, so a
// localhost-only probe can spuriously report Vite as unreachable.
const viteLoopbackHosts = ["127.0.0.1", "[::1]"];
const toViteOrigin = (host, port) => `http://${host}:${port}`;

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

const delay = (milliseconds) =>
    new Promise((resolve) => setTimeout(resolve, milliseconds));

const createPrefixedWriter = (prefix, target, onText) => {
    let buffered = "";

    const flushLines = (text) => {
        buffered += text;
        let lineStart = 0;

        for (let index = 0; index < buffered.length; index++) {
            const current = buffered[index];
            if (current === "\n") {
                target.write(`${prefix}${buffered.slice(lineStart, index)}\n`);
                lineStart = index + 1;
                continue;
            }

            if (current !== "\r") {
                continue;
            }

            if (index === buffered.length - 1) {
                break;
            }

            target.write(`${prefix}${buffered.slice(lineStart, index)}\n`);
            if (buffered[index + 1] === "\n") {
                index++;
            }

            lineStart = index + 1;
        }

        buffered = buffered.slice(lineStart);
    };

    return {
        write: (chunk) => {
            const text = chunk.toString();
            onText?.(text);
            flushLines(text);
        },
        flush: () => {
            const remainingLine = buffered.endsWith("\r")
                ? buffered.slice(0, -1)
                : buffered;
            if (!remainingLine) {
                buffered = "";
                return;
            }

            target.write(`${prefix}${remainingLine}\n`);
            buffered = "";
        },
    };
};

const pipeChildOutput = (child, prefix, onText) => {
    const stdoutWriter = createPrefixedWriter(prefix, process.stdout, onText);
    const stderrWriter = createPrefixedWriter(prefix, process.stderr, onText);

    child.stdout.on("data", stdoutWriter.write);
    child.stderr.on("data", stderrWriter.write);
    child.stdout.on("end", stdoutWriter.flush);
    child.stderr.on("end", stderrWriter.flush);
};

const canListenOnLoopbackPort = (port) =>
    new Promise((resolve) => {
        const server = net.createServer();
        let settled = false;

        const finish = (result) => {
            if (settled) {
                return;
            }

            settled = true;
            resolve(result);
        };

        server.once("error", () => {
            server.close(() => finish(false));
        });

        server.once("listening", () => {
            server.close(() => finish(true));
        });

        server.listen({
            host: "127.0.0.1",
            port,
            exclusive: true,
        });
    });

const pickRandomAvailablePort = () =>
    new Promise((resolve, reject) => {
        const server = net.createServer();

        server.once("error", reject);
        server.listen({ host: "127.0.0.1", port: 0, exclusive: true }, () => {
            const address = server.address();
            const port =
                typeof address === "object" && address
                    ? address.port
                    : undefined;

            server.close((error) => {
                if (error) {
                    reject(error);
                    return;
                }

                if (!port) {
                    reject(new Error("Unable to choose a Vite dev port."));
                    return;
                }

                resolve(port);
            });
        });
    });

const isViteClientReachable = async (port) => {
    for (const host of viteLoopbackHosts) {
        try {
            const response = await fetch(
                `${toViteOrigin(host, port)}/@vite/client`,
                {
                    signal: AbortSignal.timeout(viteHealthRequestTimeoutMs),
                },
            );
            if (response.ok) {
                return true;
            }
        } catch {
            // Try the next loopback host before giving up.
        }
    }

    return false;
};

const waitForViteClient = async (port, timeoutMs) => {
    const deadline = Date.now() + timeoutMs;
    let consecutiveSuccesses = 0;

    while (!isShuttingDown && Date.now() < deadline) {
        if (await isViteClientReachable(port)) {
            consecutiveSuccesses++;
            if (consecutiveSuccesses >= 2) {
                return true;
            }
        } else {
            consecutiveSuccesses = 0;
        }

        await delay(viteHealthPollMs);
    }

    return false;
};

// Terminate a spawned child AND all of its descendants.
//
// On Windows we do NOT rely on SIGINT propagation: terminating a Node child does
// not terminate its descendants, so the dev.mjs subtree (Vite + ~7 watchers and
// their own command children) would orphan if we merely signaled dev.mjs and
// then trusted it to reap them before exiting. Instead we `taskkill /t /f` the
// whole subtree directly, which is the only reliable way to leave zero orphans.
//
// On POSIX we keep the graceful SIGINT-then-force path so dotnet/Bloom can shut
// down cleanly; terminals already propagate Ctrl-C to the process group there.
const terminateChild = (child) =>
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

        if (process.platform === "win32") {
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

const shutdown = async (exitCode = 0) => {
    if (isShuttingDown) {
        return;
    }

    isShuttingDown = true;
    const normalizedExitCode = Number.isInteger(exitCode) ? exitCode : 1;
    console.log(`[go] Shutting down (exit ${normalizedExitCode})...`);

    await Promise.all(children.map((child) => terminateChild(child)));
    process.exit(normalizedExitCode);
};

process.on("SIGINT", () => {
    void shutdown(0);
});

process.on("SIGTERM", () => {
    void shutdown(0);
});

const buildStartupError = (message, logTail) => {
    const error = new Error(message);
    error.portConflict = /already in use|EADDRINUSE/i.test(logTail);
    return error;
};

const startDevServerOnPort = (port) =>
    new Promise((resolve, reject) => {
        const child = spawn(
            process.execPath,
            [devScriptPath, "--port", String(port)],
            {
                cwd: browserUIRoot,
                stdio: ["ignore", "pipe", "pipe"],
                shell: false,
                env: {
                    ...process.env,
                    PORT: String(port),
                },
            },
        );

        children.push(child);

        let quietTimer;
        let startupFinished = false;
        let logTail = "";
        let sawReady = false;
        let sawInitialBuild = false;
        let sawWatchersStarted = false;
        let lastOutputAt = Date.now();

        const scheduleQuiescenceCheck = () => {
            if (startupFinished || isShuttingDown) {
                return;
            }

            if (!(sawReady && sawInitialBuild && sawWatchersStarted)) {
                return;
            }

            clearTimeout(quietTimer);
            quietTimer = setTimeout(async () => {
                if (startupFinished || isShuttingDown) {
                    return;
                }

                if (Date.now() - lastOutputAt < startupQuietMs) {
                    scheduleQuiescenceCheck();
                    return;
                }

                const healthy = await waitForViteClient(
                    port,
                    viteHealthTimeoutMs,
                );
                if (!healthy) {
                    reject(
                        buildStartupError(
                            `Vite on port ${port} never became reachable at /@vite/client.`,
                            logTail,
                        ),
                    );
                    return;
                }

                if (Date.now() - lastOutputAt < startupQuietMs) {
                    scheduleQuiescenceCheck();
                    return;
                }

                startupFinished = true;
                resolve({ child, port });
            }, startupQuietMs);
        };

        const observeText = (text) => {
            lastOutputAt = Date.now();
            logTail = (logTail + text).slice(-12000);

            if (logTail.includes("ready in")) {
                sawReady = true;
            }

            if (logTail.includes("Initial build done")) {
                sawInitialBuild = true;
            }

            if (logTail.includes("Watching for changes:")) {
                sawWatchersStarted = true;
            }

            scheduleQuiescenceCheck();
        };

        pipeChildOutput(child, "[dev] ", observeText);

        child.on("error", (error) => {
            if (startupFinished || isShuttingDown) {
                return;
            }

            clearTimeout(quietTimer);
            reject(
                buildStartupError(
                    `Failed to start Vite dev: ${error.message}`,
                    logTail,
                ),
            );
        });

        child.on("exit", (code, signal) => {
            clearTimeout(quietTimer);

            if (startupFinished) {
                if (!isShuttingDown) {
                    const detail = signal
                        ? `signal ${signal}`
                        : `code ${code ?? 0}`;
                    console.error(
                        `[go] Vite dev exited unexpectedly with ${detail}.`,
                    );
                    void shutdown(code ?? 1);
                }
                return;
            }

            if (isShuttingDown) {
                return;
            }

            reject(
                buildStartupError(
                    `Vite exited before becoming quiescent (code ${code ?? 0}${signal ? `, signal ${signal}` : ""}).`,
                    logTail,
                ),
            );
        });
    });

const startDevServer = async () => {
    if (options.vitePort) {
        const available = await canListenOnLoopbackPort(options.vitePort);
        if (!available) {
            throw new Error(
                `Requested Vite port ${options.vitePort} is already in use.`,
            );
        }

        console.log(
            `[go] Starting Vite on requested port ${options.vitePort}...`,
        );
        return startDevServerOnPort(options.vitePort);
    }

    let lastError;

    for (let attempt = 1; attempt <= maxRandomVitePortAttempts; attempt++) {
        const port = await pickRandomAvailablePort();
        console.log(
            `[go] Starting Vite on random port ${port} (attempt ${attempt}/${maxRandomVitePortAttempts})...`,
        );

        try {
            return await startDevServerOnPort(port);
        } catch (error) {
            lastError = error;
            if (!error?.portConflict) {
                throw error;
            }

            console.error(
                `[go] Vite could not hold port ${port} through startup. Retrying with a new random port...`,
            );
        }
    }

    throw lastError ?? new Error("Unable to start Vite on a random port.");
};

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
    const dev = await startDevServer();
    console.log(
        `[go] Vite is reachable and quiet on port ${dev.port}. Starting Bloom...`,
    );
    startBloomExe(dev.port);
};

main().catch((error) => {
    console.error(`[go] ${error.message}`);
    void shutdown(1);
});
