/* eslint-env node */
/* global clearTimeout, process, setTimeout */

// Stage the AI Image Editor for the DEFAULT `./go.sh` (i.e. when you are NOT actively
// developing the editor with `--with bloom-ai-image-tools`).
//
// The editor is a SEPARATE app (the `bloom-ai-image-tools` package). Bloom loads it in an
// <iframe> at {ServerUrl}/bloom/aiImageEditor/index.html, which BloomServer serves from
// output/browser/aiImageEditor/ (see AiImageEditorApi.GetEditorUrl).
//
// Preferred source: the prebuilt dist-app/ shipped in the INSTALLED package
// (node_modules/bloom-ai-image-tools/dist-app). That is the uniform "as installed" model.
// Until the package is published and added as a dependency, we fall back to building
// dist-app/ from a local checkout so the feature keeps working.
//
// This is best-effort: if neither source is available we log and let Bloom start anyway —
// only "Edit with AI" is affected.
//
// Editor developers who want live HMR should use `./go.sh --with bloom-ai-image-tools`,
// which runs the editor's own Vite dev server and points Bloom at it (see go.mjs).

import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { getLibrary, resolveCheckoutDir } from "./devLibraries.mjs";
import { killProcessTree } from "./processTree.mjs";

// Directory names that never contain editable editor source; skipping them keeps the
// staleness scan fast and stops generated output from looking "newer" than the build.
const SCAN_IGNORE_DIRS = new Set([
    "node_modules",
    ".git",
    "dist",
    "dist-app",
    "demo-dist",
    "coverage",
    ".vite",
    "test-results",
    "playwright-report",
]);

// Newest mtime (ms) of any source file under `dir`, skipping SCAN_IGNORE_DIRS.
const newestSourceMtimeMs = (dir) => {
    let newest = 0;
    const walk = (current) => {
        let entries;
        try {
            entries = fs.readdirSync(current, { withFileTypes: true });
        } catch {
            return;
        }
        for (const entry of entries) {
            if (entry.isDirectory()) {
                if (SCAN_IGNORE_DIRS.has(entry.name)) {
                    continue;
                }
                walk(path.join(current, entry.name));
            } else if (entry.isFile()) {
                try {
                    const { mtimeMs } = fs.statSync(
                        path.join(current, entry.name),
                    );
                    if (mtimeMs > newest) {
                        newest = mtimeMs;
                    }
                } catch {
                    // ignore unreadable files
                }
            }
        }
    };
    walk(dir);
    return newest;
};

const mtimeMsOrZero = (file) => {
    try {
        return fs.statSync(file).mtimeMs;
    } catch {
        return 0;
    }
};

// Clear `target` and copy the whole tree at `source` into it. Clearing first stops old
// content-hashed assets from a previous build piling up.
const copyTree = (source, target) => {
    fs.rmSync(target, { recursive: true, force: true });
    fs.mkdirSync(target, { recursive: true });
    fs.cpSync(source, target, { recursive: true });
};

const runBuild = (libraryDir, log) =>
    new Promise((resolve) => {
        const isWindows = process.platform === "win32";
        const vpBin = path.join(
            libraryDir,
            "node_modules",
            ".bin",
            isWindows ? "vp.cmd" : "vp",
        );
        if (!fs.existsSync(vpBin)) {
            log(
                `AI editor: vite-plus not installed in ${libraryDir} (run its package install). Skipping build.`,
            );
            resolve(false);
            return;
        }
        // Quote the binary path (it can contain spaces) and let the shell run the .cmd
        // wrapper on Windows.
        const child = spawn(`"${vpBin}" run build:app`, {
            cwd: libraryDir,
            stdio: "inherit",
            shell: true,
        });
        // go.mjs awaits this staging step before launching Bloom.exe, so a build that
        // hangs (unlike one that fails) would block Bloom's startup forever. A normal
        // editor build takes well under a minute; after a generous cap, kill it and let
        // Bloom start without the editor.
        const timeoutMs = 5 * 60 * 1000;
        const timeout = setTimeout(() => {
            log(
                "AI editor: build did not finish within 5 minutes; abandoning it so Bloom can start.",
            );
            killProcessTree(child.pid);
        }, timeoutMs);
        child.on("error", (error) => {
            clearTimeout(timeout);
            log(`AI editor: build failed to start: ${error.message}`);
            resolve(false);
        });
        child.on("exit", (code) => {
            clearTimeout(timeout);
            if (code === 0) {
                resolve(true);
            } else {
                log(`AI editor: build exited with code ${code ?? "unknown"}.`);
                resolve(false);
            }
        });
    });

// Build dist-app/ from the local checkout (only when its sources are newer than the last
// build) and copy it into `target`. Used only as a fallback when the package isn't installed.
const buildFromCheckoutAndStage = async ({ repoRoot, entry, target, log }) => {
    const libraryDir = resolveCheckoutDir(entry, repoRoot);
    if (!libraryDir) {
        log(
            "AI editor: not installed and no bloom-ai-image-tools checkout found; 'Edit with AI' will be unavailable. (Install the package, or use --with bloom-ai-image-tools.)",
        );
        return;
    }

    const distApp = path.join(libraryDir, "dist-app");
    const distIndex = path.join(distApp, "index.html");

    const builtMtime = mtimeMsOrZero(distIndex);
    const sourceMtime = newestSourceMtimeMs(libraryDir);
    if (builtMtime === 0 || sourceMtime > builtMtime) {
        log(
            builtMtime === 0
                ? `AI editor: building from ${libraryDir} (no prior build)...`
                : `AI editor: sources changed; rebuilding from ${libraryDir}...`,
        );
        const ok = await runBuild(libraryDir, log);
        if (!ok && !fs.existsSync(distIndex)) {
            log(
                "AI editor: no usable build available; 'Edit with AI' will be unavailable.",
            );
            return;
        }
    }

    if (
        mtimeMsOrZero(distIndex) >
        mtimeMsOrZero(path.join(target, "index.html"))
    ) {
        copyTree(distApp, target);
        log(`AI editor: staged from checkout into ${target}.`);
    } else {
        log("AI editor: staged copy is up to date.");
    }
};

// Stage the AI editor for a default (non-linked) launch: prefer the installed package's
// prebuilt dist-app/, otherwise build from a local checkout. Never throws.
export const stageAiEditorForDefault = async ({
    repoRoot,
    browserUIRoot,
    log,
}) => {
    try {
        const entry = getLibrary("bloom-ai-image-tools");
        const target = path.join(repoRoot, ...entry.stageTarget.split("/"));

        const installedDistApp = path.join(
            browserUIRoot,
            ...entry.installedDistApp.split("/"),
        );
        if (fs.existsSync(path.join(installedDistApp, "index.html"))) {
            // Skip the delete-and-recopy when the staged tree already came from this
            // package version. Mtimes can't tell us that (cpSync stamps copy time, and
            // package managers extract tarballs with arbitrary mtimes), so record the
            // version we staged in a marker file inside the target.
            const packageVersion = JSON.parse(
                fs.readFileSync(
                    path.join(installedDistApp, "..", "package.json"),
                    "utf8",
                ),
            ).version;
            const versionMarker = path.join(target, ".staged-package-version");
            let stagedVersion = null;
            try {
                stagedVersion = fs.readFileSync(versionMarker, "utf8");
            } catch {
                // never staged (or marker missing): fall through and copy
            }
            if (
                stagedVersion === packageVersion &&
                fs.existsSync(path.join(target, "index.html"))
            ) {
                log("AI editor: staged copy is up to date.");
                return;
            }
            copyTree(installedDistApp, target);
            fs.writeFileSync(versionMarker, packageVersion);
            log(`AI editor: staged from installed package into ${target}.`);
            return;
        }

        await buildFromCheckoutAndStage({ repoRoot, entry, target, log });
    } catch (error) {
        log(
            `AI editor: staging skipped due to error: ${error?.message ?? error}`,
        );
    }
};
