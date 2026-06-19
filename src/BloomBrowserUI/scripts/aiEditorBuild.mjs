/* eslint-env node */
/* global console, process */

// Build-and-stage the AI Image Editor so `./go.sh` "just works" with no separate
// dev server.
//
// The editor is a SEPARATE app (the `bloom-ai-image-tools` package). Bloom loads it
// in an <iframe> at {ServerUrl}/bloom/aiImageEditor/index.html, which BloomServer
// serves from output/browser/aiImageEditor/ (see AiImageEditorApi.GetEditorUrl). This
// module builds that app from the local bloom-ai-image-tools checkout (its `build:app`
// script emits dist-app/ with base=/bloom/aiImageEditor/, so its asset URLs are already
// correct) and copies dist-app/ into output/browser/aiImageEditor/.
//
// It is intentionally best-effort: if the library checkout can't be found or the build
// fails, we log and let Bloom start anyway — only the "Edit with AI" feature is affected.
//
// Editor developers who want HMR can skip all this and point Bloom at the editor's own
// Vite dev server by setting BLOOM_AI_EDITOR_URL (e.g. http://localhost:3000/); see
// AiImageEditorApi.GetEditorUrl.

import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";

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

// Locate the bloom-ai-image-tools checkout. Explicit env var wins; otherwise try the
// layouts we actually ship in (sibling checkout or sibling worktree), relative to the
// Bloom repo root, so the common case needs zero configuration.
const resolveLibraryDir = (repoRoot) => {
    const explicit = process.env.BLOOM_AI_IMAGE_TOOLS_DIR;
    const candidates = [];
    if (explicit) {
        candidates.push(path.resolve(explicit));
    }
    for (const rel of [
        "../bloom-ai-image-tools.worktrees/V2",
        "../../bloom-ai-image-tools.worktrees/V2",
        "../bloom-ai-image-tools",
        "../../bloom-ai-image-tools",
    ]) {
        candidates.push(path.resolve(repoRoot, rel));
    }
    for (const dir of candidates) {
        if (fs.existsSync(path.join(dir, "package.json"))) {
            return dir;
        }
    }
    return null;
};

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
        child.on("error", (error) => {
            log(`AI editor: build failed to start: ${error.message}`);
            resolve(false);
        });
        child.on("exit", (code) => {
            if (code === 0) {
                resolve(true);
            } else {
                log(`AI editor: build exited with code ${code ?? "unknown"}.`);
                resolve(false);
            }
        });
    });

// Build the editor from its local checkout (only when its sources are newer than the
// last build) and copy the result into output/browser/aiImageEditor/. Never throws.
export const ensureAiEditorBuilt = async ({ repoRoot, log }) => {
    try {
        const libraryDir = resolveLibraryDir(repoRoot);
        if (!libraryDir) {
            log(
                "AI editor: bloom-ai-image-tools checkout not found; skipping (set BLOOM_AI_IMAGE_TOOLS_DIR to enable). 'Edit with AI' will be unavailable.",
            );
            return;
        }

        const distApp = path.join(libraryDir, "dist-app");
        const distIndex = path.join(distApp, "index.html");

        // Rebuild only when the editor sources are newer than the existing dist-app, or
        // there is no dist-app yet.
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
        } else {
            log("AI editor: build is up to date.");
        }

        // Copy dist-app/ -> output/browser/aiImageEditor/ when the staged copy is missing
        // or older than the build. Clear the target first so old content-hashed assets
        // from a previous build don't pile up.
        const target = path.join(
            repoRoot,
            "output",
            "browser",
            "aiImageEditor",
        );
        const targetIndex = path.join(target, "index.html");
        if (mtimeMsOrZero(distIndex) > mtimeMsOrZero(targetIndex)) {
            fs.rmSync(target, { recursive: true, force: true });
            fs.mkdirSync(target, { recursive: true });
            fs.cpSync(distApp, target, { recursive: true });
            log(`AI editor: staged into ${target}.`);
        } else {
            log("AI editor: staged copy is up to date.");
        }
    } catch (error) {
        log(
            `AI editor: staging skipped due to error: ${error?.message ?? error}`,
        );
    }
};
