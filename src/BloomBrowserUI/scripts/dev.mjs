/* eslint-env node */
/* global clearTimeout, console, process, setTimeout */
import { spawn } from "child_process";
import path from "node:path";
import * as fs from "node:fs";
import * as net from "node:net";
import { fileURLToPath } from "node:url";
import { glob } from "glob";
import { compilePugFiles } from "./compilePug.mjs";
import { copyStaticFile } from "./copyStaticFile.mjs";
import { copyContentFile } from "../../content/scripts/copyContentFile.mjs";
import { killProcessTree } from "./processTree.mjs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const browserUIRoot = path.resolve(__dirname, "..");
const contentRoot = path.resolve(browserUIRoot, "../content");
const outputBrowserRoot = path.resolve(browserUIRoot, "../../output/browser");

const isWindows = process.platform === "win32";

// Stage a file out of BloomBrowserUI/node_modules into output/browser. The production build
// copies a few node_modules assets into output/browser via viteStaticCopy (vite.config.mts),
// but that copy is build-only AND the dev static-file watcher deliberately skips node_modules
// (see copyStaticFile.mjs). So any node_modules asset that Bloom loads at runtime over its own
// server (not through Vite) must be staged explicitly here, or the page 404s on it in dev.
// Returns true if it actually copied. statSync throws if the source is missing, which is the
// fail-fast we want: a missing asset means dependencies were not installed.
const stageNodeModuleAsset = (relativeSource, relativeDest) => {
    const source = path.resolve(browserUIRoot, "node_modules", relativeSource);
    const dest = path.resolve(outputBrowserRoot, relativeDest);
    if (
        fs.existsSync(dest) &&
        fs.statSync(dest).mtimeMs >= fs.statSync(source).mtimeMs
    ) {
        return false;
    }
    fs.mkdirSync(path.dirname(dest), { recursive: true });
    fs.copyFileSync(source, dest);
    return true;
};

const readJson = (filePath) => JSON.parse(fs.readFileSync(filePath, "utf8"));

const resolvePackageBin = (packageRoot, packageName, binName) => {
    const packageJsonPath = path.resolve(
        packageRoot,
        "node_modules",
        packageName,
        "package.json",
    );

    const packageJson = readJson(packageJsonPath);
    const binField = packageJson.bin;
    let binRelativePath;
    if (typeof binField === "string") {
        binRelativePath = binField;
    } else {
        binRelativePath = binField?.[binName];
    }

    if (!binRelativePath) {
        throw new Error(
            `Unable to resolve bin \"${binName}\" from ${packageJsonPath}`,
        );
    }

    return path.resolve(
        packageRoot,
        "node_modules",
        packageName,
        binRelativePath,
    );
};

const processes = [];
let isShuttingDown = false;
const isVerbose = process.argv.includes("--verbose");

const defaultVitePort = 5173;

const parsePortValue = (value) => {
    const parsed = Number.parseInt(value, 10);
    if (Number.isInteger(parsed) && parsed > 0 && parsed <= 65535) {
        return parsed;
    }
    return undefined;
};

const parsePort = () => {
    const equalsArg = process.argv.find((value) =>
        value?.startsWith("--port="),
    );
    if (equalsArg) {
        const parsed = parsePortValue(equalsArg.split("=")[1]);
        if (parsed !== undefined) {
            return parsed;
        }
    }

    const flagIndex = process.argv.findIndex(
        (value) => value === "--port" || value === "-p",
    );
    if (flagIndex >= 0 && flagIndex + 1 < process.argv.length) {
        const parsed = parsePortValue(process.argv[flagIndex + 1]);
        if (parsed !== undefined) {
            return parsed;
        }
    }

    if (process.env.PORT) {
        const parsed = parsePortValue(process.env.PORT);
        if (parsed !== undefined) {
            return parsed;
        }
    }

    return defaultVitePort;
};

const isPortAvailable = (port) =>
    new Promise((resolve) => {
        const server = net
            .createServer()
            .once("error", () => {
                resolve(false);
            })
            .once("listening", () => {
                server.close(() => resolve(true));
            })
            .listen(port, "127.0.0.1");
    });

function spawnProcess(command, args, options = {}) {
    const proc = spawn(command, args, {
        stdio: ["ignore", "pipe", "pipe"],
        shell: false,
        ...options,
    });

    proc.stdout.on("data", (data) => process.stdout.write(data));
    proc.stderr.on("data", (data) => process.stderr.write(data));

    proc.on("error", (err) => {
        if (isShuttingDown) {
            return;
        }
        console.error(`Process failed to start: ${command}`);
        console.error(err);
        void cleanup(1);
    });

    proc.on("close", (code, signal) => {
        if (isShuttingDown) {
            return;
        }

        if (signal) {
            console.error(`Process exited due to signal ${signal}: ${command}`);
            void cleanup(1);
            return;
        }

        if (code !== 0) {
            console.error(`Process exited with code ${code}: ${command}`);
            void cleanup(code ?? 1);
        }
    });

    processes.push(proc);
    return proc;
}

function spawnNodeScript(scriptPath, args, options = {}) {
    return spawnProcess(process.execPath, [scriptPath, ...args], {
        shell: false,
        ...options,
    });
}

function startVite(port) {
    return new Promise((resolve) => {
        console.log("Starting Vite dev server...");

        const viteBin = resolvePackageBin(browserUIRoot, "vite", "vite");
        let ready = false;
        const vite = spawn(
            process.execPath,
            [viteBin, "--port", String(port), "--strictPort"],
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

        processes.push(vite);

        vite.stdout.on("data", (data) => {
            process.stdout.write(data);
            if (data.toString().includes("ready in")) {
                ready = true;
                resolve();
            }
        });

        vite.stderr.on("data", (data) => process.stderr.write(data));

        vite.on("error", (err) => {
            if (isShuttingDown) {
                return;
            }
            console.error("Vite failed to start:", err);
            void cleanup(1);
        });

        vite.on("close", (code) => {
            if (isShuttingDown) {
                return;
            }
            if (!ready) {
                console.error(
                    `Vite exited before becoming ready (code ${code}).`,
                );
                void cleanup(1);
                return;
            }

            console.error(`Vite exited unexpectedly (code ${code}).`);
            void cleanup(code ?? 1);
        });
    });
}

async function runInitialBuilds() {
    let copiedCount = 0;
    const pugResult = await compilePugFiles({
        logSummary: isVerbose,
        logWhenNoChanges: isVerbose,
        logFiles: isVerbose,
    });

    const staticFiles = glob.sync("**/*.*", {
        cwd: browserUIRoot,
        nodir: true,
        absolute: true,
        ignore: ["**/node_modules/**"],
    });
    for (const file of staticFiles) {
        if (copyStaticFile(file, { quiet: !isVerbose })) {
            copiedCount++;
        }
    }

    // jQuery is consumed two ways: bundled (imported by various bundles) AND as a browser
    // global, loaded by a plain <script src="/bloom/jquery.min.js"> that Book.cs injects so
    // legacy code (qtip, etc.) finds jQuery on window. The build stages jquery.min.js into
    // output/browser via viteStaticCopy; dev must do the same or the edit page 404s on it.
    if (stageNodeModuleAsset("jquery/dist/jquery.min.js", "jquery.min.js")) {
        copiedCount++;
    }

    const contentCopyJobs = [
        {
            label: "template files",
            pattern:
                "templates/**/!(tsconfig).{png,jpg,svg,css,json,htm,html,txt,js,gif}",
            sourceBase: "templates",
            destinationBase: "templates",
        },
        {
            label: "branding files",
            pattern:
                "branding/**/!(source)/*.{png,jpg,svg,css,json,htm,html,txt,js}",
            sourceBase: "branding",
            destinationBase: "branding",
        },
        {
            label: "appearance theme files",
            pattern: "appearanceThemes/**/*.css",
            sourceBase: "appearanceThemes",
            destinationBase: "appearanceThemes",
        },
        {
            label: "appearance migration files",
            pattern: "appearanceMigrations/**",
            sourceBase: "appearanceMigrations",
            destinationBase: "appearanceMigrations",
        },
    ];

    for (const job of contentCopyJobs) {
        const files = glob.sync(job.pattern, {
            cwd: contentRoot,
            nodir: true,
            absolute: true,
        });
        for (const file of files) {
            if (
                copyContentFile(file, job.sourceBase, job.destinationBase, {
                    quiet: !isVerbose,
                })
            ) {
                copiedCount++;
            }
        }
    }

    const compiledCount = pugResult?.compiled ?? 0;
    const totalChanges = compiledCount + copiedCount;
    if (totalChanges === 0) {
        console.log("Initial build done (no changes).");
        return;
    }

    if (isVerbose) {
        const summaryParts = [];
        if (compiledCount > 0) {
            summaryParts.push(`Pug: ${compiledCount} compiled`);
        }
        if (copiedCount > 0) {
            summaryParts.push(`${copiedCount} files copied`);
        }

        const summaryText = summaryParts.length
            ? ` (${summaryParts.join(", ")})`
            : "";
        console.log(`Initial build done${summaryText}.`);
    } else {
        console.log("Initial build done.");
    }
}

async function startWatchers() {
    await runInitialBuilds();

    const onchangeBin = resolvePackageBin(
        browserUIRoot,
        "onchange",
        "onchange",
    );
    const nodeForOnchange = isWindows ? "node" : process.execPath;

    // Pug watcher - compile all pug files initially, then watch for changes
    const verboseFlag = isVerbose ? ["--verbose"] : [];

    spawnNodeScript(
        onchangeBin,
        [
            "-k",
            "-i",
            "**/*.pug",
            "../content/**/*.pug",
            "--",
            nodeForOnchange,
            "./scripts/compilePug.mjs",
            ...verboseFlag,
        ],
        { cwd: browserUIRoot },
    );

    // Less watcher - consolidate BloomBrowserUI and content LESS processing
    spawnProcess(
        process.execPath,
        ["./scripts/watchLess.mjs", "--scope=all", ...verboseFlag],
        {
            cwd: browserUIRoot,
            shell: false,
        },
    );

    // Static file watcher - only triggers on actual changes (no -i since copyStaticFile needs a specific file)
    spawnNodeScript(
        onchangeBin,
        [
            "-k",
            "**/*.*",
            "--",
            nodeForOnchange,
            "./scripts/copyStaticFile.mjs",
            "{{file}}",
            ...verboseFlag,
        ],
        { cwd: browserUIRoot },
    );

    // Content watchers (spawn directly to avoid printing full commands)
    spawnNodeScript(
        onchangeBin,
        [
            "-k",
            "-i",
            "-a",
            "templates/**/!(tsconfig).{png,jpg,svg,css,json,htm,html,txt,js,gif}",
            "--",
            nodeForOnchange,
            "./scripts/copyContentFile.mjs",
            "{{file}}",
            "templates",
            "templates",
            ...verboseFlag,
        ],
        { cwd: contentRoot },
    );

    spawnNodeScript(
        onchangeBin,
        [
            "-k",
            "-i",
            "-a",
            "branding/**/!(source)/*.{png,jpg,svg,css,json,htm,html,txt,js}",
            "--",
            nodeForOnchange,
            "./scripts/copyContentFile.mjs",
            "{{file}}",
            "branding",
            "branding",
            ...verboseFlag,
        ],
        { cwd: contentRoot },
    );

    spawnNodeScript(
        onchangeBin,
        [
            "-k",
            "-i",
            "-a",
            "appearanceThemes/**/*.css",
            "--",
            nodeForOnchange,
            "./scripts/copyContentFile.mjs",
            "{{file}}",
            "appearanceThemes",
            "appearanceThemes",
            ...verboseFlag,
        ],
        { cwd: contentRoot },
    );

    spawnNodeScript(
        onchangeBin,
        [
            "-k",
            "-i",
            "-a",
            "appearanceMigrations/**",
            "--",
            nodeForOnchange,
            "./scripts/copyContentFile.mjs",
            "{{file}}",
            "appearanceMigrations",
            "appearanceMigrations",
            ...verboseFlag,
        ],
        { cwd: contentRoot },
    );

    console.log(
        "Watching for changes: pug, LESS, static files, templates, branding, appearance themes & migrations.",
    );
}

// Force-kill every watcher subtree, then exit. We must kill whole trees (not
// just the direct children) because on Windows a plain kill leaves each
// watcher's own command children (notably `onchange -k`) running, and those
// orphans accumulate across runs. We await the kills so they actually complete
// before we exit; a watchdog guarantees we still exit even if a kill hangs.
async function cleanup(exitCode = 0) {
    if (isShuttingDown) {
        return;
    }
    isShuttingDown = true;
    console.log("\nShutting down...");
    const normalizedExitCode =
        typeof exitCode === "number" && Number.isFinite(exitCode)
            ? exitCode
            : 0;

    // Never let a stuck kill keep us alive indefinitely.
    const watchdog = setTimeout(() => process.exit(normalizedExitCode), 5000);
    watchdog.unref();

    await Promise.all(
        processes.map((proc) =>
            proc.pid ? killProcessTree(proc.pid) : Promise.resolve(),
        ),
    );

    clearTimeout(watchdog);
    process.exit(normalizedExitCode);
}

process.on("SIGINT", () => void cleanup(0));
process.on("SIGTERM", () => void cleanup(0));

async function main() {
    if (!fs.existsSync(process.execPath)) {
        throw new Error(`Node executable not found at ${process.execPath}`);
    }

    const port = parsePort();
    const available = await isPortAvailable(port);
    if (!available) {
        console.error(`Port ${port} is already in use.`);
        // Suggest a neighboring port, staying within the valid range parsePortValue accepts.
        const suggestedPort = port < 65535 ? port + 1 : port - 1;
        console.error(
            `Stop the other dev server, or run: pnpm dev --port=${suggestedPort}`,
        );
        process.exit(1);
    }

    await startVite(port);
    await startWatchers();
}

main().catch((err) => {
    console.error("Dev script failed:", err);
    void cleanup(1);
});
