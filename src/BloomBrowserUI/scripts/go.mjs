/* eslint-env node */
/* global AbortSignal, clearTimeout, console, fetch, process, setTimeout */
import { spawn } from "node:child_process";
import net from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const browserUIRoot = path.resolve(__dirname, "..");
const repoRoot = path.resolve(browserUIRoot, "..", "..");
const devScriptPath = path.join(browserUIRoot, "scripts", "dev.mjs");
const exeScriptPath = path.join(repoRoot, "scripts", "watchBloomExe.mjs");
process.env.feedback = "off";
const startupQuietMs = 1500;
const viteHealthTimeoutMs = 15000;
const viteHealthPollMs = 250;
const maxRandomVitePortAttempts = 10;
const gracefulShutdownMs = 1500;
const toViteOrigin = (port) => `http://localhost:${port}`;

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
        httpPort: undefined,
        cdpPort: undefined,
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

        if (arg === "--http-port") {
            options.httpPort = parseRequiredPortValue(
                "--http-port",
                requireOptionValue(args, index, "--http-port"),
            );
            index++;
            continue;
        }

        if (arg.startsWith("--http-port=")) {
            options.httpPort = parseRequiredPortValue(
                "--http-port",
                arg.slice("--http-port=".length),
            );
            continue;
        }

        if (arg === "--cdp-port") {
            options.cdpPort = parseRequiredPortValue(
                "--cdp-port",
                requireOptionValue(args, index, "--cdp-port"),
            );
            index++;
            continue;
        }

        if (arg.startsWith("--cdp-port=")) {
            options.cdpPort = parseRequiredPortValue(
                "--cdp-port",
                arg.slice("--cdp-port=".length),
            );
        }
    }

    return options;
};

const options = parseArgs();

const children = [];
let isShuttingDown = false;

const delay = (milliseconds) =>
    new Promise((resolve) => setTimeout(resolve, milliseconds));

const createPrefixedWriter = (prefix, target, onText) => {
    let buffered = "";

    const flushLines = (text) => {
        buffered += text.replace(/\r/g, "\n");
        const lines = buffered.split("\n");
        buffered = lines.pop() ?? "";

        for (const line of lines) {
            target.write(`${prefix}${line}\n`);
        }
    };

    return {
        write: (chunk) => {
            const text = chunk.toString();
            onText?.(text);
            flushLines(text);
        },
        flush: () => {
            if (!buffered) {
                return;
            }

            target.write(`${prefix}${buffered}\n`);
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
    try {
        const response = await fetch(`${toViteOrigin(port)}/@vite/client`, {
            signal: AbortSignal.timeout(500),
        });
        return response.ok;
    } catch {
        return false;
    }
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

            if (process.platform === "win32") {
                const killer = spawn(
                    "taskkill",
                    ["/pid", String(child.pid), "/t", "/f"],
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
                child.kill("SIGTERM");
            } catch (error) {
                void error;
            }

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

            if (logTail.includes("Watching appearance migration files...")) {
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

    if (options.httpPort) {
        args.push("--http-port", String(options.httpPort));
    }

    if (options.cdpPort) {
        args.push("--cdp-port", String(options.cdpPort));
    }

    const child = spawn(process.execPath, args, {
        cwd: browserUIRoot,
        stdio: ["ignore", "pipe", "pipe"],
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
