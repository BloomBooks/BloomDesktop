/* eslint-env node */
/* global console, process */
import { spawn } from "child_process";
import path from "node:path";
import * as fs from "node:fs";
import * as net from "node:net";
import { fileURLToPath } from "node:url";
import { glob } from "glob";
import { compilePugFiles } from "./compilePug.mjs";
import { copyStaticFile } from "./copyStaticFile.mjs";
import { copyContentFile } from "../../content/scripts/copyContentFile.mjs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const browserUIRoot = path.resolve(__dirname, "..");
const contentRoot = path.resolve(browserUIRoot, "../content");

const isWindows = process.platform === "win32";

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

const parsePort = () => {
    const arg = process.argv.find((value) => value?.startsWith("--port="));
    if (arg) {
        const parsed = Number.parseInt(arg.split("=")[1], 10);
        if (Number.isFinite(parsed)) {
            return parsed;
        }
    }

    if (process.env.PORT) {
        const parsed = Number.parseInt(process.env.PORT, 10);
        if (Number.isFinite(parsed)) {
            return parsed;
        }
    }

    return defaultVitePort;
};

const isPortAvailable = (port) =>
    new Promise((resolve) => {
        const server = net
            .createServer()
            .once("error", (err) => {
                resolve(err.code !== "EADDRINUSE");
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
        cleanup(1);
    });

    proc.on("close", (code, signal) => {
        if (isShuttingDown) {
            return;
        }

        if (signal) {
            console.error(`Process exited due to signal ${signal}: ${command}`);
            cleanup(1);
            return;
        }

        if (code !== 0) {
            console.error(`Process exited with code ${code}: ${command}`);
            cleanup(code ?? 1);
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
        console.log("Starting Vite dev server...\n");

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
            console.error("Vite failed to start:", err);
            process.exit(1);
        });

        vite.on("close", (code) => {
            if (!ready) {
                console.error(
                    `Vite exited before becoming ready (code ${code}).`,
                );
                process.exit(1);
            }
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
        console.log("\nInitial build done (no changes).\n");
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
        console.log(`\nInitial build done${summaryText}.\n`);
    } else {
        console.log("\nInitial build done.\n");
    }
}

async function startWatchers() {
    await runInitialBuilds();
    console.log("\nStarting file watchers...\n");

    const onchangeBin = resolvePackageBin(
        browserUIRoot,
        "onchange",
        "onchange",
    );
    const nodeForOnchange = isWindows ? "node" : process.execPath;

    // Pug watcher - compile all pug files initially, then watch for changes
    console.log("Watching pug files...");
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
    console.log("Watching LESS files...");
    spawnProcess(process.execPath, ["./scripts/watchLess.mjs", "--scope=all"], {
        cwd: browserUIRoot,
        shell: false,
    });

    // Static file watcher - only triggers on actual changes (no -i since copyStaticFile needs a specific file)
    console.log("Watching browser UI static files...");
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
    console.log("Watching template files...");
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

    console.log("Watching branding files...");
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

    console.log("Watching appearance theme files...");
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

    console.log("Watching appearance migration files...");
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
}

function cleanup(exitCode = 0) {
    if (isShuttingDown) {
        return;
    }
    isShuttingDown = true;
    console.log("\nShutting down...");
    for (const proc of processes) {
        proc.kill();
    }
    const normalizedExitCode =
        typeof exitCode === "number" && Number.isFinite(exitCode)
            ? exitCode
            : 0;
    process.exit(normalizedExitCode);
}

process.on("SIGINT", () => cleanup(0));
process.on("SIGTERM", () => cleanup(0));

async function main() {
    if (!fs.existsSync(process.execPath)) {
        throw new Error(`Node executable not found at ${process.execPath}`);
    }

    const port = parsePort();
    const available = await isPortAvailable(port);
    if (!available) {
        console.error(`Port ${port} is already in use.`);
        console.error(
            `Stop the other dev server, or run: yarn dev --port=${port + 1}`,
        );
        process.exit(1);
    }

    await startVite(port);
    await startWatchers();
}

main().catch((err) => {
    console.error("Dev script failed:", err);
    cleanup(1);
});
