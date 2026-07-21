/* eslint-env node */
/* global clearTimeout, console, process, setTimeout */
import { spawn } from "node:child_process";
import {
    existsSync,
    mkdirSync,
    openSync,
    closeSync,
    readFileSync,
    writeSync,
    statSync,
    readdirSync,
    unlinkSync,
} from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
    automationReadyPrefix,
    makeAutomationReadyScanner,
} from "./automationReady.mjs";
import { pipeChildOutput } from "./childOutput.mjs";
import { startViteDevServer } from "./viteDevServer.mjs";
import { killProcessTree, reapOrphanedBloomDevStacks } from "./processTree.mjs";
import { findRunningStandardBloomInstances } from "../../../.github/skills/bloom-automation/bloomProcessCommon.mjs";
import { getHelpfulStartupLabel } from "../../../scripts/watchBloomExeLabel.mjs";

// `pnpm run` / `./run.sh` — the BUILD-ONCE launcher (contrast with `go.sh`, which
// runs Bloom under `dotnet watch`). It builds BloomExe once (Debug), then launches
// the built Bloom.exe DIRECTLY. Rationale (dogfood, 14 Jul 2026): our C# changes
// are structural, so .NET Hot Reload can't apply them and `dotnet watch` forces a
// full rebuild+relaunch anyway — while the watcher holds the output tree's apphost
// locked so nobody else can rebuild. Dropping the watcher removes the lock fight;
// the front-end still hot-reloads because Vite runs exactly as it does under go.
//
// Two-window story (Alice + Bob against the same source): both windows run this
// same command. A build LOCK serializes the build so they don't race into the
// shared output tree, and a freshness check lets the second window skip the build
// entirely (the first already produced an up-to-date exe), so it never has to
// touch the apphost the first instance now holds open.

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const browserUIRoot = path.resolve(__dirname, "..");
const repoRoot = path.resolve(browserUIRoot, "..", "..");
process.env.feedback = "off";

const buildConfiguration = "Debug";
const bloomExeCsproj = path.join(
    repoRoot,
    "src",
    "BloomExe",
    "BloomExe.csproj",
);
// BloomExe.csproj: OutputPath=..\..\output\$(Configuration)\$(Platform)\,
// AppendTargetFrameworkToOutputPath=false, AssemblyName=Bloom, Platform=AnyCPU.
const builtExePath = path.join(
    repoRoot,
    "output",
    buildConfiguration,
    "AnyCPU",
    "Bloom.exe",
);
const outputRoot = path.join(repoRoot, "output");
const buildLockPath = path.join(outputRoot, "run-build.lock");

const launchTimeoutMs = 120000;
const buildLockPollMs = 500;
// A build lock older than this is treated as abandoned (builder crashed without
// releasing it). A full clean build is well under this; a normal incremental build
// is seconds. Ten minutes is generous headroom over the slowest real build.
const staleBuildLockMs = 10 * 60 * 1000;
// Directories we never descend into when scanning C# sources for freshness — they
// hold build output, dependencies, or VCS metadata, none of which are edited.
const freshnessSkipDirs = new Set([
    "output",
    "bin",
    "obj",
    "node_modules",
    ".git",
    "DistFiles",
]);

const parseArgs = () => {
    const args = process.argv.slice(2);
    const options = { vitePort: undefined };

    for (let index = 0; index < args.length; index++) {
        const arg = args[index];

        if (arg === "--vite-port" || arg.startsWith("--vite-port=")) {
            const raw =
                arg === "--vite-port" ? args[++index] : arg.split("=")[1];
            const parsed = Number.parseInt(raw, 10);
            if (!Number.isInteger(parsed) || parsed <= 0 || parsed > 65535) {
                throw new Error(
                    `--vite-port must be an integer from 1 to 65535. Received: ${raw}`,
                );
            }
            options.vitePort = parsed;
            continue;
        }

        throw new Error(
            `Unsupported option "${arg}". The only supported option is --vite-port.`,
        );
    }

    return options;
};

let options;
try {
    options = parseArgs();
} catch (error) {
    console.error(`[run] ${error instanceof Error ? error.message : error}`);
    process.exit(1);
}

const children = [];
let bloomChild;
let bloomProcessId;
let isShuttingDown = false;

const delay = (milliseconds) =>
    new Promise((resolve) => setTimeout(resolve, milliseconds));

const log = (message) => console.log(`[run] ${message}`);

const shutdown = async (exitCode = 0) => {
    if (isShuttingDown) {
        return;
    }
    isShuttingDown = true;
    const normalizedExitCode = Number.isInteger(exitCode) ? exitCode : 1;
    log(`Shutting down (exit ${normalizedExitCode})...`);
    // killProcessTree takes down each child's WHOLE subtree (see its doc comment
    // in processTree.mjs for why signal propagation can't be trusted on Windows).
    await Promise.all(children.map((child) => killProcessTree(child.pid)));
    process.exit(normalizedExitCode);
};

process.on("SIGINT", () => void shutdown(0));
process.on("SIGTERM", () => void shutdown(0));

const isProcessAlive = (pid) => {
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

// --- Build coordination ---------------------------------------------------

// Newest modification time (ms) among C# sources and project files under src/,
// plus the repo-root MSBuild files (Directory.Build.props and friends), which
// also change build output. Used only to decide whether the existing build is
// already up to date; the build lock plus dotnet's own incrementality are the
// real correctness guarantees, so a slightly conservative answer (rebuild when
// unsure) is fine.
const newestSourceMtimeMs = () => {
    let newest = 0;
    for (const rootBuildFile of [
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
    ]) {
        try {
            const mtime = statSync(path.join(repoRoot, rootBuildFile)).mtimeMs;
            if (mtime > newest) {
                newest = mtime;
            }
        } catch {
            // Not every repo-root build file exists; that's fine.
        }
    }
    const stack = [path.join(repoRoot, "src")];

    while (stack.length > 0) {
        const dir = stack.pop();
        let entries;
        try {
            entries = readdirSync(dir, { withFileTypes: true });
        } catch {
            continue;
        }

        for (const entry of entries) {
            if (entry.isDirectory()) {
                if (!freshnessSkipDirs.has(entry.name)) {
                    stack.push(path.join(dir, entry.name));
                }
                continue;
            }

            if (!/\.(cs|csproj)$/i.test(entry.name)) {
                continue;
            }

            try {
                const mtime = statSync(path.join(dir, entry.name)).mtimeMs;
                if (mtime > newest) {
                    newest = mtime;
                }
            } catch {
                // Ignore files that vanish mid-scan.
            }
        }
    }

    return newest;
};

const isBuiltExeFresh = () => {
    if (!existsSync(builtExePath)) {
        return false;
    }
    try {
        // Compare against the newest BUILD OUTPUT, not just the apphost exe: an
        // incremental `dotnet build` rewrites Bloom.dll but usually leaves the
        // apphost Bloom.exe untouched (it only embeds the dll name), so the exe's
        // mtime goes permanently stale after the first source edit and an
        // exe-only check would make every launch rebuild forever.
        const builtDllPath = path.join(path.dirname(builtExePath), "Bloom.dll");
        let newestOutput = statSync(builtExePath).mtimeMs;
        try {
            const dllMtime = statSync(builtDllPath).mtimeMs;
            if (dllMtime > newestOutput) {
                newestOutput = dllMtime;
            }
        } catch {
            // No dll (very unusual); fall back to the exe mtime alone.
        }
        return newestOutput >= newestSourceMtimeMs();
    } catch {
        return false;
    }
};

// Acquire an exclusive, cross-process build lock. If another `run` is mid-build,
// wait until it releases (or its lock goes stale). Returns a release function.
const acquireBuildLock = async () => {
    if (!existsSync(outputRoot)) {
        mkdirSync(outputRoot, { recursive: true });
    }

    for (;;) {
        try {
            const fd = openSync(buildLockPath, "wx");
            writeSync(fd, JSON.stringify({ pid: process.pid, at: Date.now() }));
            closeSync(fd);
            let released = false;
            return () => {
                if (released) {
                    return;
                }
                released = true;
                try {
                    unlinkSync(buildLockPath);
                } catch {
                    // Already gone; nothing to do.
                }
            };
        } catch (error) {
            if (error.code !== "EEXIST") {
                throw error;
            }
        }

        // Lock is held by someone else. Steal it if it is stale (crashed builder
        // or dead pid); otherwise wait and retry.
        let holder;
        try {
            holder = JSON.parse(readFileSync(buildLockPath, "utf8"));
        } catch {
            holder = undefined;
        }

        const ageMs = holder?.at ? Date.now() - holder.at : Infinity;
        const holderDead = holder?.pid && !isProcessAlive(holder.pid);
        if (ageMs > staleBuildLockMs || holderDead) {
            log(
                `Removing a stale build lock (${holderDead ? `dead pid ${holder.pid}` : `age ${Math.round(ageMs / 1000)}s`}).`,
            );
            try {
                unlinkSync(buildLockPath);
            } catch {
                // Someone else cleaned it up; loop and retry.
            }
            continue;
        }

        log(
            `Another build is in progress (pid ${holder?.pid ?? "?"}). Waiting for it to finish...`,
        );
        await delay(buildLockPollMs);
    }
};

const runDotnetBuild = () =>
    new Promise((resolve, reject) => {
        log(`Building BloomExe (${buildConfiguration})...`);
        const child = spawn(
            "dotnet",
            ["build", bloomExeCsproj, "-c", buildConfiguration],
            { cwd: repoRoot, stdio: ["ignore", "pipe", "pipe"], shell: false },
        );
        pipeChildOutput(child, "[build] ");
        child.on("error", reject);
        child.on("exit", (code) => {
            if (code === 0) {
                resolve();
                return;
            }
            reject(new Error(`dotnet build failed with exit code ${code}.`));
        });
    });

// Build the exe unless it is already up to date, serialized against any other
// `run` via the build lock so two windows never build the same tree at once.
const ensureBuilt = async () => {
    if (isBuiltExeFresh()) {
        log("Built Bloom.exe is already up to date; skipping build.");
        return;
    }

    const release = await acquireBuildLock();
    try {
        // Re-check after acquiring: another window may have just built for us.
        if (isBuiltExeFresh()) {
            log("Another window already produced an up-to-date build.");
            return;
        }
        await runDotnetBuild();
        if (!existsSync(builtExePath)) {
            throw new Error(
                `Build reported success but ${builtExePath} does not exist.`,
            );
        }
    } finally {
        release();
    }
};

// --- Vite -----------------------------------------------------------------

// Reuse a Vite server already serving THIS worktree (from another `run`/`go`
// instance) so two windows don't run two Vites; otherwise start our own.
const ensureVite = async () => {
    if (options.vitePort) {
        log(`Using caller-provided Vite port ${options.vitePort}.`);
        return options.vitePort;
    }

    const normalizedRepoRoot = repoRoot.replace(/\//g, "\\").toLowerCase();
    const instances = await findRunningStandardBloomInstances();
    const inheritedPort = instances
        .filter(
            (instance) =>
                instance.detectedRepoRoot &&
                instance.detectedRepoRoot.toLowerCase() ===
                    normalizedRepoRoot &&
                instance.vitePort,
        )
        .map((instance) => instance.vitePort)[0];

    if (inheritedPort) {
        log(
            `Reusing Vite port ${inheritedPort} from a running instance of this worktree.`,
        );
        return inheritedPort;
    }

    const dev = await startViteDevServer({
        repoRoot,
        requestedPort: undefined,
        registerChild: (child) => children.push(child),
        isShuttingDown: () => isShuttingDown,
        onUnexpectedExit: (exitCode) => void shutdown(exitCode),
        log,
    });
    log(`Vite is reachable and quiet on port ${dev.port}.`);
    return dev.port;
};

// --- Bloom launch ---------------------------------------------------------

const buildBloomArgs = (vitePort) => {
    const args = ["--automation"];
    // Mirror `pnpm go`: a human is watching, so keep --automation's ready
    // handshake but turn its unattended-UI policies (dialog auto-close, problem
    // report suppression) back off. Set BLOOM_GO_UNATTENDED=1 to opt out.
    if (process.env.BLOOM_GO_UNATTENDED !== "1") {
        args.push("--attended");
    }
    const label = getHelpfulStartupLabel(repoRoot);
    if (label) {
        args.push("--label", label);
    }
    args.push("--vite-port", String(vitePort));
    return args;
};

const launchBloom = (vitePort) =>
    new Promise((resolve) => {
        const args = buildBloomArgs(vitePort);
        log(`Launching ${builtExePath} ${args.join(" ")}`);
        bloomChild = spawn(builtExePath, args, {
            cwd: repoRoot,
            stdio: ["ignore", "pipe", "pipe"],
            shell: false,
        });
        children.push(bloomChild);

        let ready = false;
        const launchTimer = setTimeout(() => {
            if (!ready) {
                console.error(
                    `[run] Bloom did not emit ${automationReadyPrefix.trim()} within ${launchTimeoutMs} ms.`,
                );
            }
        }, launchTimeoutMs);

        const scanner = makeAutomationReadyScanner(
            (info) => {
                ready = true;
                clearTimeout(launchTimer);
                bloomProcessId = Number(info?.processId) || bloomChild.pid;
                log(
                    `Bloom ready. HTTP ${info?.httpPort}, CDP ${info?.cdpPort}, Bloom PID ${bloomProcessId}.`,
                );
            },
            (error) =>
                console.error(
                    `[run] Could not parse ${automationReadyPrefix.trim()} payload: ${error.message}`,
                ),
        );

        pipeChildOutput(bloomChild, "[bloom] ", scanner);

        bloomChild.on("error", (error) => {
            clearTimeout(launchTimer);
            console.error(`[run] Failed to start Bloom: ${error.message}`);
            resolve(1);
        });

        bloomChild.on("exit", (code, signal) => {
            clearTimeout(launchTimer);
            const detail = signal ? `signal ${signal}` : `code ${code ?? 0}`;
            log(`Bloom exited (${detail}).`);
            bloomChild = undefined;
            bloomProcessId = undefined;
            resolve(code ?? 0);
        });
    });

// After Bloom closes, wait for Enter to rebuild + relaunch (picking up any C#
// changes) while keeping Vite warm, or Ctrl+C to stop.
const relaunchPrompt =
    "Bloom closed. Press Enter to rebuild & relaunch, or Ctrl+C to stop.";

const runLaunchLoop = async (vitePort) => {
    for (;;) {
        try {
            await ensureBuilt();
        } catch (error) {
            console.error(`[run] ${error.message}`);
            if (!process.stdin.isTTY) {
                await shutdown(1);
                return;
            }
            log("Fix the build error, then press Enter to try again.");
            await waitForEnter();
            continue;
        }

        await launchBloom(vitePort);
        if (isShuttingDown) {
            return;
        }

        if (!process.stdin.isTTY) {
            // No human to prompt (e.g. spawned by another tool): stop cleanly.
            await shutdown(0);
            return;
        }

        log(relaunchPrompt);
        await waitForEnter();
    }
};

let pendingEnterResolvers = [];
const waitForEnter = () =>
    new Promise((resolve) => pendingEnterResolvers.push(resolve));

if (process.stdin.readable) {
    process.stdin.setEncoding("utf8");
    process.stdin.on("data", () => {
        const resolvers = pendingEnterResolvers;
        pendingEnterResolvers = [];
        resolvers.forEach((resolve) => resolve());
    });
    process.stdin.resume();
}

const main = async () => {
    // Reap dev-stack node processes orphaned by a hard-killed launcher before we
    // start (same cleanup go.mjs does at startup; reuses master's shared helper).
    await reapOrphanedBloomDevStacks({
        excludePids: [process.pid],
        log,
    });

    const vitePort = await ensureVite();
    await runLaunchLoop(vitePort);
};

main().catch((error) => {
    console.error(`[run] ${error.message}`);
    void shutdown(1);
});
