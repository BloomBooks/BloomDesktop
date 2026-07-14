#!/usr/bin/env node
const { spawn } = require("child_process");
const fs = require("fs");
const path = require("path");

const args = new Set(process.argv.slice(2));
const verbose = args.has("--verbose");

const browserUIRoot = path.resolve(__dirname, "..");
const contentRoot = path.resolve(browserUIRoot, "..", "content");

const env = { ...process.env };

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
    const binRelativePath =
        typeof binField === "string" ? binField : binField?.[binName];

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

const viteBin = resolvePackageBin(browserUIRoot, "vite", "vite");

const runCommand = (command, commandArgs, options = {}) =>
    new Promise((resolve, reject) => {
        const showOutput = options.showOutput ?? verbose;
        const child = spawn(command, commandArgs, {
            cwd: options.cwd ?? browserUIRoot,
            env,
            shell: false,
            stdio: showOutput ? "inherit" : ["ignore", "pipe", "pipe"],
        });

        if (showOutput) {
            child.on("close", (code) => {
                if (code === 0) {
                    resolve();
                } else {
                    reject(
                        new Error(
                            `Command failed (${code}): ${command} ${commandArgs.join(
                                " ",
                            )}`,
                        ),
                    );
                }
            });
            child.on("error", reject);
            return;
        }

        let stdout = "";
        let stderr = "";
        child.stdout.on("data", (data) => {
            stdout += data;
        });
        child.stderr.on("data", (data) => {
            stderr += data;
        });
        child.on("close", (code) => {
            if (code === 0) {
                resolve();
                return;
            }
            if (stdout) {
                process.stdout.write(stdout);
            }
            if (stderr) {
                process.stderr.write(stderr);
            }
            reject(
                new Error(
                    `Command failed (${code}): ${command} ${commandArgs.join(
                        " ",
                    )}`,
                ),
            );
        });
        child.on("error", reject);
    });

const run = async () => {
    console.log("Cleaning output/browser...");
    await runCommand("node", ["scripts/clean.js", "--quiet"], {
        cwd: browserUIRoot,
    });

    console.log("Building content assets...");
    await runCommand("node", ["checkForNodeModules.js"], { cwd: contentRoot });

    const cpxBin = resolvePackageBin(contentRoot, "cpx", "cpx");
    const rimrafBin = resolvePackageBin(contentRoot, "rimraf", "rimraf");

    // Run pageSizes.ts through Node's native TypeScript type stripping rather
    // than ts-node: the pinned TypeScript 6.0 + ts-node combo fails on Node 22
    // (ERR_UNKNOWN_FILE_EXTENSION and a TS5107 deprecation error).
    // --experimental-strip-types is required on the Volta-pinned Node 22.14
    // (stripping is on by default only from 22.18); the disable-warning flags
    // silence the harmless experimental and typeless-package.json notices.
    await runCommand(
        "node",
        [
            "--experimental-strip-types",
            "--disable-warning=ExperimentalWarning",
            "--disable-warning=MODULE_TYPELESS_PACKAGE_JSON",
            "pageSizes.ts",
        ],
        {
            cwd: contentRoot,
        },
    );

    // Type-check before bundling. Vite/esbuild strips types without checking them,
    // so this is our only build-time guard against type errors (it fails on the
    // blunder-class codes — grammar, missing names/modules, wrong arg counts —
    // see scripts/typecheck.js). Run it before the Vite build so a type error
    // fails fast rather than after a full bundle.
    console.log("Type checking...");
    await runCommand("node", ["scripts/typecheck.js"], {
        cwd: browserUIRoot,
        showOutput: true,
    });

    console.log("Vite build...");
    await runCommand("node", [viteBin, "build", "--logLevel", "warn"], {
        cwd: browserUIRoot,
        showOutput: true,
    });

    console.log("Copying branding assets...");
    await runCommand(
        "node",
        [
            cpxBin,
            "branding/**/!(source)/*.{png,jpg,svg,css,json,htm}",
            "../../output/browser/branding",
        ],
        { cwd: contentRoot },
    );
    console.log("Copying template assets...");
    await runCommand(
        "node",
        [
            cpxBin,
            "templates/**/!(tsconfig).{png,jpg,svg,css,json,htm,html,txt,js,gif}",
            "../../output/browser/templates",
        ],
        { cwd: contentRoot },
    );
    console.log("Copying appearance themes...");
    await runCommand(
        "node",
        [rimrafBin, "../../output/browser/appearanceThemes"],
        {
            cwd: contentRoot,
        },
    );
    await runCommand(
        "node",
        [
            cpxBin,
            "appearanceThemes/**/*.css",
            "../../output/browser/appearanceThemes",
        ],
        { cwd: contentRoot },
    );
    await runCommand(
        "node",
        [
            cpxBin,
            "appearanceMigrations/**",
            "../../output/browser/appearanceMigrations",
        ],
        { cwd: contentRoot },
    );

    console.log("Build complete.");
};

run().catch((error) => {
    console.error(error.message ?? error);
    process.exit(1);
});
