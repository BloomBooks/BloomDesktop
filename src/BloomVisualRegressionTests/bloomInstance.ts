// Launching, finding, and shutting down a Bloom of our own for an end-to-end suite to drive.
//
// Every suite in this folder works the same way: copy the test collections somewhere
// throwaway, start a Bloom on the copy, wait until that Bloom is the one answering on some
// port, drive it, then kill it and delete the copy. None of that is specific to comparing
// screenshots, so it lives here rather than in index.spec.ts, and calendar.spec.ts uses it
// too.
//
// One Bloom at a time: the state below belongs to the instance this process most recently
// launched, which is all a vitest file needs.

import { execFile, execFileSync, ChildProcess } from "child_process";
import fetch from "node-fetch";
import * as fs from "fs";
import * as os from "os";
import * as Path from "path";

// Ports Bloom uses: it takes the next free block starting at 8089 (8089, 8092, 8095, ...). We
// probe these to find the port our launched instance opened on. A developer's own Bloom may
// also be on one of these ports, so we match on the open collection folder (below) rather than
// assuming a port.
export const CANDIDATE_PORTS = [8089, 8092, 8095, 8098, 8101, 8104];

/** The Bloom this process launched, and how to talk to it. */
export interface IBloomInstance {
    /** e.g. "http://localhost:8089" — Bloom's own HTTP server, which serves its UI and API. */
    origin: string;
    /** The WebView2 debugging port, for a test that has to drive Bloom's own window. */
    cdpPort: number | null;
    /** The process actually serving the collection, which is not always the one we spawned. */
    processId: number | null;
    /** The throwaway folder the collections were copied into. */
    tempCollectionsRoot: string;
    /** The .bloomCollection file this Bloom was started on. */
    collectionFile: string;
}

// The Bloom we launched, kept so we can shut it down afterwards.
let bloomProcess: ChildProcess | null = null;
// Everything Bloom writes to stdout/stderr, kept so that when a launch fails (Bloom crashed, or
// its server never came up) we can report WHY instead of an opaque "did not open within 90s"
// timeout. Capped so a chatty-but-healthy Bloom can't grow this unbounded.
let bloomOutput = "";
const MAX_BLOOM_OUTPUT = 20000;
function recordBloomOutput(chunk: string) {
    bloomOutput = (bloomOutput + chunk).slice(-MAX_BLOOM_OUTPUT);
}
// Set if the Bloom process we spawned exits before it starts serving our collection.
// Distinguishes a crash-on-startup (fail fast with the exit code) from a
// still-running-but-not-ready poll.
let bloomExit: { code: number | null; signal: NodeJS.Signals | null } | null =
    null;
// The PID of the Bloom actually serving our temp collection. Bloom can relaunch into a new
// process after startup, so the process we spawned is not necessarily the one serving — killing
// only the spawned PID left orphaned Blooms holding the temp folder open. We read the real PID
// from instanceInfo and kill that too.
let bloomServingPid: number | null = null;
// The throwaway copy of the collections, so we can delete it when the run finishes.
let tempCollectionsRoot: string | null = null;

/** The throwaway collection copy this process is working in, or null before a launch. */
export function getTempCollectionsRoot(): string | null {
    return tempCollectionsRoot;
}

/**
 * Resolve a path to its canonical on-disk form. On Windows this is essential because
 * os.tmpdir() returns an 8.3 short path (e.g. C:\Users\JOHNTH~1\...) while Bloom reports the
 * long form (C:\Users\JohnThomson\...); realpathSync.native expands the short name and fixes
 * casing so the two actually compare equal. Falls back to Path.resolve if the path does not
 * exist yet.
 */
export function canonicalPath(p: string): string {
    try {
        return fs.realpathSync.native(p);
    } catch (e) {
        return Path.resolve(p);
    }
}

/** Windows paths compare case-insensitively; canonicalize (see above) then lowercase. */
export function samePath(a: string, b: string): boolean {
    return canonicalPath(a).toLowerCase() === canonicalPath(b).toLowerCase();
}

/**
 * The running Bloom whose open editable collection is wantFolder, or null if none is found
 * yet. Matching the collection folder (rather than just the collection name) is what
 * distinguishes our temp-copy instance from a Bloom the developer may already have open on the
 * repo copy.
 */
export async function findBloomServingCollection(wantFolder: string): Promise<{
    origin: string;
    processId?: number;
    cdpPort?: number;
} | null> {
    for (const port of CANDIDATE_PORTS) {
        const origin = `http://localhost:${port}`;
        try {
            const r = await fetch(`${origin}/bloom/api/common/instanceInfo`);
            if (!r.ok) continue;
            const info = (await r.json()) as {
                editableCollectionFolder?: string;
                processId?: number;
                cdpPort?: number;
            };
            if (
                info.editableCollectionFolder &&
                samePath(info.editableCollectionFolder, wantFolder)
            )
                return {
                    origin,
                    processId: info.processId,
                    cdpPort: info.cdpPort,
                };
        } catch (e) {
            // Nothing responding on that port; keep looking.
        }
    }
    return null;
}

/**
 * Render the tail of Bloom's captured stdout/stderr for inclusion in a launch-failure error, so
 * the CI log shows what Bloom actually said.
 */
function formatBloomOutput(): string {
    const trimmed = bloomOutput.trim();
    if (!trimmed) return `  Bloom output: (none captured)`;
    return `  Bloom output (last ${MAX_BLOOM_OUTPUT} chars):\n${trimmed}`;
}

/**
 * The Bloom.exe to run. The exe lands in a config/platform-specific folder depending on the
 * build, so we try the known locations. Release is included because CI runs against Release
 * builds. Debug is listed first for the common local (go.sh) case; a clean CI checkout only
 * has the config it built.
 */
function findBloomExe(): string {
    const candidates = [
        "../../output/Debug/x64/Bloom.exe",
        "../../output/Debug/AnyCPU/Bloom.exe",
        "../../output/Debug/Bloom.exe",
        "../../output/Release/x64/Bloom.exe",
        "../../output/Release/AnyCPU/Bloom.exe",
        "../../output/Release/Bloom.exe",
    ];
    const exe = candidates.find((c) => fs.existsSync(c));
    if (!exe) {
        throw new Error(
            `Could not find a built Bloom.exe (looked in: ${candidates.join(", ")}). ` +
                `Build Bloom, then re-run.`,
        );
    }
    return exe;
}

/**
 * Populate a throwaway temp collection and launch a Bloom of our own on it, then wait until
 * that instance is serving it. We always launch our own (rather than reusing a developer's
 * Bloom) so the run is deterministic and never touches the repo collection.
 *
 * `populate` is handed the new temp root and puts the collections into it; each suite decides
 * what "the test collections" means.
 */
export async function launchDedicatedBloom(options: {
    tempFolderPrefix: string;
    collectionName: string;
    populate: (tempCollectionsRoot: string) => void;
}): Promise<IBloomInstance> {
    // Canonicalize immediately: os.tmpdir() is an 8.3 short path on Windows, but Bloom reports
    // the long form, so we normalize here (and in samePath) to make the discovery match work.
    tempCollectionsRoot = canonicalPath(
        fs.mkdtempSync(Path.join(os.tmpdir(), options.tempFolderPrefix)),
    );
    options.populate(tempCollectionsRoot);
    // Backstop: tidy up even if the run is aborted (e.g. by an unhandled rejection) before
    // afterAll.
    process.once("exit", cleanupOnExit);

    const collectionFile = Path.join(
        tempCollectionsRoot,
        options.collectionName,
        `${options.collectionName}.bloomCollection`,
    );
    const exe = findBloomExe();
    console.log(`Launching ${exe} on ${collectionFile}`);
    // --e2e: skip the DEBUG "Attach debugger now" prompt and suppress modal error dialogs so a
    // Bloom problem fails the test instead of hanging the run. --automation: allow this
    // instance to run alongside a Bloom the developer already has open (bypasses the
    // single-instance token).
    // BLOOM_VITE_PORT lets a developer run these tests against the working tree's TypeScript
    // instead of the bundles in output/browser, which are only as new as the last full build.
    // Start a Vite dev server (pnpm -C src/BloomBrowserUI dev) and give its port here. CI does a
    // full build first, so it sets nothing and Bloom serves output/browser as usual.
    const args = [collectionFile, "--e2e", "--automation"];
    const vitePort = process.env.BLOOM_VITE_PORT;
    if (vitePort) args.push("--vite-port", vitePort);
    console.log(
        vitePort
            ? `Using the Vite dev server on port ${vitePort} for the UI`
            : "Using the bundles in output/browser for the UI",
    );
    bloomProcess = execFile(exe, args);
    // Capture Bloom's output and watch for an early exit. Without this a launch failure (crash
    // on startup, missing WebView2 runtime, first-run dialog) is invisible: the poll below just
    // runs out the full 90s and reports "seen: none" with no clue why. Echo to our own stderr
    // too so the CI step log shows Bloom's startup output inline. execFile (no stdio:'ignore')
    // gives us pipes.
    bloomProcess.stdout?.on("data", (d) => {
        const s = d.toString();
        recordBloomOutput(s);
        process.stderr.write(`[bloom stdout] ${s}`);
    });
    bloomProcess.stderr?.on("data", (d) => {
        const s = d.toString();
        recordBloomOutput(s);
        process.stderr.write(`[bloom stderr] ${s}`);
    });
    // 'error' fires when the exe can't even be spawned (e.g. not found / not executable).
    bloomProcess.on("error", (err) =>
        recordBloomOutput(`\n[spawn error] ${err.message}\n`),
    );
    bloomProcess.on("exit", (code, signal) => {
        bloomExit = { code, signal };
    });

    // Discover which port our instance opened on by matching the collection folder it has open.
    const wantFolder = Path.join(tempCollectionsRoot, options.collectionName);
    const startTime = Date.now();
    while (Date.now() - startTime < 90000) {
        const match = await findBloomServingCollection(wantFolder);
        if (match) {
            bloomServingPid = match.processId ?? null;
            console.log(
                `Dedicated Bloom is ready at ${match.origin} (pid ${bloomServingPid ?? "?"})`,
            );
            return {
                origin: match.origin,
                cdpPort: match.cdpPort ?? null,
                processId: bloomServingPid,
                tempCollectionsRoot,
                collectionFile,
            };
        }
        // Fail fast if Bloom died on startup instead of waiting out the full timeout: the exit
        // code plus captured output tells us it crashed rather than that discovery merely
        // couldn't see it.
        if (bloomExit) {
            throw new Error(
                `The dedicated Bloom exited before serving the temp collection ` +
                    `(code ${bloomExit.code}, signal ${bloomExit.signal}).\n` +
                    `  exe: ${exe}\n` +
                    `  wanted: ${wantFolder}\n` +
                    formatBloomOutput(),
            );
        }
        await new Promise((resolve) => setTimeout(resolve, 1500));
    }
    // Timed out: report which instances we could see so a mismatch is diagnosable rather than
    // opaque.
    const seen: string[] = [];
    for (const port of CANDIDATE_PORTS) {
        try {
            const r = await fetch(
                `http://localhost:${port}/bloom/api/common/instanceInfo`,
            );
            if (!r.ok) continue;
            const info = (await r.json()) as {
                editableCollectionFolder?: string;
            };
            if (info.editableCollectionFolder)
                seen.push(`${port} -> ${info.editableCollectionFolder}`);
        } catch (e) {
            // nothing on this port
        }
    }
    throw new Error(
        `The dedicated Bloom did not open the temp collection within 90s.\n` +
            `  exe: ${exe}\n` +
            `  wanted: ${wantFolder}\n` +
            `  still running: ${bloomExit ? "no (already exited)" : "yes"}\n` +
            `  Bloom instances seen: ${seen.length ? seen.join("; ") : "none"}\n` +
            formatBloomOutput(),
    );
}

/**
 * Kill the dedicated Bloom we launched, along with its WebView2 child processes. We kill both
 * the PID we spawned and the PID actually serving our collection: Bloom can relaunch into a new
 * process after startup, so those can differ, and killing only the spawned one left an orphaned
 * Bloom holding the temp folder open (which then failed to delete). Idempotent.
 */
export function stopBloom(): void {
    const pids = [bloomProcess?.pid, bloomServingPid].filter(
        (p): p is number => typeof p === "number",
    );
    bloomProcess = null;
    bloomServingPid = null;
    for (const pid of pids) {
        try {
            if (process.platform === "win32")
                // /T kills the whole tree (Bloom spawns WebView2 child processes); /F forces it.
                execFileSync("taskkill", ["/pid", String(pid), "/t", "/f"], {
                    stdio: "ignore",
                });
            else process.kill(pid, "SIGTERM");
        } catch (e) {
            // Already gone; nothing to do.
        }
    }
}

/**
 * Delete the throwaway collection copy. Bloom may release file handles slightly after it dies,
 * so let rmSync retry a few times. Idempotent.
 */
export function cleanupTempCollections(): void {
    const dir = tempCollectionsRoot;
    tempCollectionsRoot = null;
    if (!dir) return;
    try {
        fs.rmSync(dir, {
            recursive: true,
            force: true,
            maxRetries: 20,
            retryDelay: 500,
        });
    } catch (e) {
        console.warn(`Could not remove temp collection copy at ${dir}: ${e}`);
    }
}

/**
 * Synchronous last-resort cleanup for the process 'exit' event (afterAll may not run if the run
 * is aborted). Both helpers are synchronous, as an 'exit' handler requires.
 */
function cleanupOnExit(): void {
    stopBloom();
    cleanupTempCollections();
}
