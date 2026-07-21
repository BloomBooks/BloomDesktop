/* eslint-env node */
/* global AbortSignal, clearTimeout, console, fetch, process, setTimeout */
import { spawn } from "node:child_process";
import net from "node:net";
import path from "node:path";
import { pipeChildOutput } from "./childOutput.mjs";

// Starts (or waits on) the Vite dev server that both launchers need. This logic
// was extracted verbatim from go.mjs so the watch launcher (go.mjs) and the
// build-once launcher (run.mjs) share ONE well-tuned implementation. The tuning
// comments below are load-bearing — see the git history of go.mjs for why each
// value is what it is.

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
// (an earlier value) spuriously aborted every probe during this window, so
// waitForViteClient never got its 2 consecutive successes and the whole launch
// failed. A slow-but-listening server is healthy, not broken; a genuinely dead
// server still fails fast via ECONNREFUSED, so this longer timeout only affects
// the busy-but-fine case.
const viteHealthRequestTimeoutMs = 3000;
const maxRandomVitePortAttempts = 10;
// Probe both loopback families: Vite may bind only IPv6 (::1) on some machines,
// and Node's fetch resolves "localhost" to IPv4 (127.0.0.1) first, so a
// localhost-only probe can spuriously report Vite as unreachable.
const viteLoopbackHosts = ["127.0.0.1", "[::1]"];
const toViteOrigin = (host, port) => `http://${host}:${port}`;

const delay = (milliseconds) =>
    new Promise((resolve) => setTimeout(resolve, milliseconds));

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

const waitForViteClient = async (port, timeoutMs, isShuttingDown) => {
    const deadline = Date.now() + timeoutMs;
    let consecutiveSuccesses = 0;

    while (!isShuttingDown() && Date.now() < deadline) {
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

const buildStartupError = (message, logTail) => {
    const error = new Error(message);
    error.portConflict = /already in use|EADDRINUSE/i.test(logTail);
    return error;
};

const startDevServerOnPort = (context, port) =>
    new Promise((resolve, reject) => {
        const child = spawn(
            process.execPath,
            [context.devScriptPath, "--port", String(port)],
            {
                cwd: context.browserUIRoot,
                stdio: ["ignore", "pipe", "pipe"],
                shell: false,
                env: {
                    ...process.env,
                    PORT: String(port),
                },
            },
        );

        context.registerChild(child);

        let quietTimer;
        let startupFinished = false;
        let logTail = "";
        let sawReady = false;
        let sawInitialBuild = false;
        let sawWatchersStarted = false;
        let lastOutputAt = Date.now();

        const scheduleQuiescenceCheck = () => {
            if (startupFinished || context.isShuttingDown()) {
                return;
            }

            if (!(sawReady && sawInitialBuild && sawWatchersStarted)) {
                return;
            }

            clearTimeout(quietTimer);
            quietTimer = setTimeout(async () => {
                if (startupFinished || context.isShuttingDown()) {
                    return;
                }

                if (Date.now() - lastOutputAt < startupQuietMs) {
                    scheduleQuiescenceCheck();
                    return;
                }

                const healthy = await waitForViteClient(
                    port,
                    viteHealthTimeoutMs,
                    context.isShuttingDown,
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
            if (startupFinished || context.isShuttingDown()) {
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
                if (!context.isShuttingDown()) {
                    const detail = signal
                        ? `signal ${signal}`
                        : `code ${code ?? 0}`;
                    console.error(
                        `[go] Vite dev exited unexpectedly with ${detail}.`,
                    );
                    context.onUnexpectedExit(code ?? 1);
                }
                return;
            }

            if (context.isShuttingDown()) {
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

/**
 * Ensure a Vite dev server is running and healthy, returning its child process
 * and port. If `requestedPort` is given, Vite must be able to bind exactly that
 * port; otherwise a random available port is chosen (retried on port conflicts).
 *
 * @param {object} params
 * @param {string} params.repoRoot - Absolute path to the repo root.
 * @param {number} [params.requestedPort] - A specific Vite port to require, or undefined for random.
 * @param {(child: import("node:child_process").ChildProcess) => void} params.registerChild -
 *   Called with the spawned Vite child so the caller can shut it down.
 * @param {() => boolean} params.isShuttingDown - Returns true once the caller is tearing down.
 * @param {(exitCode: number) => void} params.onUnexpectedExit - Called if Vite dies after startup.
 * @param {(message: string) => void} params.log - Progress logger.
 * @returns {Promise<{child: import("node:child_process").ChildProcess, port: number}>}
 */
export const startViteDevServer = async (params) => {
    const browserUIRoot = path.join(params.repoRoot, "src", "BloomBrowserUI");
    const context = {
        browserUIRoot,
        devScriptPath: path.join(browserUIRoot, "scripts", "dev.mjs"),
        registerChild: params.registerChild,
        isShuttingDown: params.isShuttingDown,
        onUnexpectedExit: params.onUnexpectedExit,
    };

    if (params.requestedPort) {
        const available = await canListenOnLoopbackPort(params.requestedPort);
        if (!available) {
            throw new Error(
                `Requested Vite port ${params.requestedPort} is already in use.`,
            );
        }

        params.log(
            `Starting Vite on requested port ${params.requestedPort}...`,
        );
        return startDevServerOnPort(context, params.requestedPort);
    }

    let lastError;

    for (let attempt = 1; attempt <= maxRandomVitePortAttempts; attempt++) {
        const port = await pickRandomAvailablePort();
        params.log(
            `Starting Vite on random port ${port} (attempt ${attempt}/${maxRandomVitePortAttempts})...`,
        );

        try {
            return await startDevServerOnPort(context, port);
        } catch (error) {
            lastError = error;
            if (!error?.portConflict) {
                throw error;
            }

            params.log(
                `Vite could not hold port ${port} through startup. Retrying with a new random port...`,
            );
        }
    }

    throw lastError ?? new Error("Unable to start Vite on a random port.");
};
