#!/usr/bin/env node
const { spawn } = require("child_process");
const path = require("path");

const args = new Set(process.argv.slice(2));
const verbose = args.has("--verbose");

const browserUIRoot = path.resolve(__dirname, "..");
const contentRoot = path.resolve(browserUIRoot, "..", "content");

const env = { ...process.env };

const viteBin = path.join(
    browserUIRoot,
    "node_modules",
    "vite",
    "bin",
    "vite.js",
);
const tsNodeBin = path.join(
    contentRoot,
    "node_modules",
    "ts-node",
    "dist",
    "bin.js",
);
const cpxBin = path.join(contentRoot, "node_modules", "cpx", "bin", "index.js");
const rimrafBin = path.join(
    contentRoot,
    "node_modules",
    "rimraf",
    "dist",
    "esm",
    "bin.mjs",
);

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

    console.log("Vite build...");
    await runCommand("node", [viteBin, "build", "--logLevel", "warn"], {
        cwd: browserUIRoot,
        showOutput: true,
    });

    console.log("Building content assets...");
    await runCommand("node", ["checkForNodeModules.js"], { cwd: contentRoot });
    await runCommand("node", [tsNodeBin, "pageSizes.ts"], {
        cwd: contentRoot,
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
