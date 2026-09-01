// Launch a dedicated Bloom.exe on a throwaway copy of a test-input collection, discover the ports
// it actually opened, and shut the whole thing down again.
//
// This is the CI-proven loop from src/BloomVisualRegressionTests/index.spec.ts, factored out so
// every e2e test gets it for free. Three things about it are load-bearing and easy to get wrong:
//
//  1. We ALWAYS launch our own Bloom rather than reusing the developer's. That makes a run
//     deterministic and keeps it away from whatever book the developer has open.
//  2. Bloom rewrites the collection it opens (books are brought up to date, thumbnails are
//     regenerated, branding files are copied in), so we never point it at output/testing-inputs.
//     Each run copies the collection to a temp folder and Bloom operates on the copy.
//  3. Discovery matches on the OPEN COLLECTION FOLDER, not on a port. Bloom takes the next free
//     port block, and a developer's own Bloom may already hold 8089, so the folder is the only
//     reliable way to tell our instance from theirs.
//
// Nothing here knows about Playwright; fixtures/bloomTest.ts adds the CDP attachment on top.

import { execFile, execFileSync, type ChildProcess } from "node:child_process";
import * as fs from "node:fs";
import * as os from "node:os";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";

/** A launched Bloom instance, with everything a test needs to talk to it. */
export interface ILaunchedBloom {
    /** The HTTP port Bloom's own server opened on, e.g. 8089. */
    httpPort: number;
    /** The port the embedded WebView2 is listening on for CDP, normally httpPort + 2. */
    cdpPort: number;
    /** The process id of the Bloom actually serving the collection (see stopBloom). */
    bloomPid: number;
    /** The temp copy of the collection folder that this Bloom has open. */
    collectionDir: string;
    /** Kill the process tree, confirm the HTTP port went dark, and delete the temp copy. */
    stop: () => Promise<void>;
}

/** Options for launchBloom. Everything has a working default. */
export interface ILaunchBloomOptions {
    /** Name of a folder under <testing-inputs>/collections, e.g. "basic". */
    collectionName: string;
    /** How long to wait for Bloom to start serving the collection. Default 120 seconds. */
    readyTimeoutMs?: number;
}

// This file lives in <repoRoot>/src/BloomE2E/fixtures. Resolve paths from the file itself rather
// than from process.cwd(), so they hold however the runner was invoked.
const repoRoot = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "..",
    "..",
);

// Ports Bloom uses: it takes the next free block starting at 8089 (8089, 8092, 8095, ...). We probe
// these to find the port our instance opened on.
const CANDIDATE_PORTS = [8089, 8092, 8095, 8098, 8101, 8104];

// How much of Bloom's stdout/stderr we keep. Enough to diagnose a failed launch, bounded so a
// chatty-but-healthy Bloom cannot grow it without limit.
const MAX_BLOOM_OUTPUT = 20000;

/**
 * The root of the test-input collections: output/testing-inputs/collections, or the same folder
 * inside whatever checkout BLOOM_TESTING_INPUTS_DIR names. Throws with the fix if it is missing.
 */
export function getSourceCollectionsRoot(): string {
    const testingInputsRoot =
        process.env.BLOOM_TESTING_INPUTS_DIR ??
        Path.join(repoRoot, "output", "testing-inputs");
    const collectionsRoot = Path.join(testingInputsRoot, "collections");
    if (!fs.existsSync(collectionsRoot)) {
        throw new Error(
            `Could not find the test-input collections at ${collectionsRoot}.\n` +
                (process.env.BLOOM_TESTING_INPUTS_DIR
                    ? `BLOOM_TESTING_INPUTS_DIR is set to ${process.env.BLOOM_TESTING_INPUTS_DIR}; it must be a ` +
                      `checkout of https://github.com/BloomBooks/bloom-testing-inputs (the folder containing collections/).`
                    : `Run: node build/get-testing-inputs.mjs`),
        );
    }
    return collectionsRoot;
}

/**
 * Find the built Bloom.exe. The exe lands in a config/platform-specific folder depending on the
 * build, so try the known locations. Debug comes first for the common local case; CI builds and
 * runs Release. Throws naming every folder it looked in.
 */
export function findBloomExe(): string {
    const candidates = [
        "Debug/x64",
        "Debug/AnyCPU",
        "Debug",
        "Release/x64",
        "Release/AnyCPU",
        "Release",
    ].map((sub) => Path.join(repoRoot, "output", sub, "Bloom.exe"));
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
 * Resolve a path to its canonical on-disk form. On Windows this is essential: os.tmpdir() returns
 * an 8.3 short path (C:\Users\JOHNTH~1\...) while Bloom reports the long form, so the two would
 * never compare equal. Falls back to Path.resolve for a path that does not exist yet.
 */
function canonicalPath(p: string): string {
    try {
        return fs.realpathSync.native(p);
    } catch {
        return Path.resolve(p);
    }
}

/** Compare two paths the way Windows does: canonicalized (see above), then case-insensitively. */
function samePath(a: string, b: string): boolean {
    return canonicalPath(a).toLowerCase() === canonicalPath(b).toLowerCase();
}

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** What common/instanceInfo tells us about a running Bloom. Only the fields we use. */
interface IInstanceInfo {
    editableCollectionFolder?: string;
    processId?: number;
    cdpPort?: number;
}

/**
 * Ask the Bloom on `port` who it is. Returns undefined when nothing is listening there or it is
 * not a Bloom. Uses "localhost" deliberately: Bloom's HTTP server validates the Host header and
 * rejects "127.0.0.1" with a 400 (the CDP endpoint is the opposite way round — see bloomTest.ts).
 */
async function readInstanceInfo(
    port: number,
): Promise<IInstanceInfo | undefined> {
    try {
        const response = await fetch(
            `http://localhost:${port}/bloom/api/common/instanceInfo`,
        );
        if (!response.ok) return undefined;
        return (await response.json()) as IInstanceInfo;
    } catch {
        // Nothing responding on that port.
        return undefined;
    }
}

/**
 * Find the running Bloom whose open editable collection is `wantFolder`, or undefined if none is
 * serving it yet. Matching the folder (rather than assuming a port) is what distinguishes our
 * temp-copy instance from a Bloom the developer already has open on some other collection.
 */
async function findBloomServingCollection(
    wantFolder: string,
): Promise<{ httpPort: number; info: IInstanceInfo } | undefined> {
    for (const httpPort of CANDIDATE_PORTS) {
        const info = await readInstanceInfo(httpPort);
        if (
            info?.editableCollectionFolder &&
            samePath(info.editableCollectionFolder, wantFolder)
        )
            return { httpPort, info };
    }
    return undefined;
}

/**
 * Copy one source collection into `destination`. The screenshots/ folders are left out: they are
 * visual-regression baselines, read and written in the source tree, and copying them would only
 * slow the run down.
 */
function copyCollection(source: string, destination: string): void {
    fs.cpSync(source, destination, {
        recursive: true,
        filter: (from) => Path.basename(from) !== "screenshots",
    });
}

/** Find the .bloomCollection file in a collection folder. Throws if the folder has none. */
function findCollectionFile(collectionDir: string): string {
    const file = fs
        .readdirSync(collectionDir)
        .find((f) => f.endsWith(".bloomCollection"));
    if (!file)
        throw new Error(
            `${collectionDir} contains no .bloomCollection file, so Bloom cannot open it.`,
        );
    return Path.join(collectionDir, file);
}

/**
 * Kill a Bloom process tree. We kill both the process we spawned and the one actually serving,
 * because Bloom can relaunch itself into a new process during startup; killing only the spawned
 * pid leaves an orphan holding the temp folder open, which then refuses to delete.
 */
function killProcessTree(pids: number[]): void {
    for (const pid of pids) {
        try {
            if (process.platform === "win32")
                // /T kills the whole tree (Bloom spawns WebView2 children); /F forces it.
                execFileSync("taskkill", ["/pid", String(pid), "/t", "/f"], {
                    stdio: "ignore",
                });
            else process.kill(pid, "SIGTERM");
        } catch {
            // Already gone; nothing to do.
        }
    }
}

/**
 * Launch a dedicated Bloom.exe on a temp copy of the named collection and wait until it is serving
 * it. The returned object carries the discovered ports and a stop() that tears everything down.
 *
 * Throws with Bloom's own captured output when the launch fails, so a broken run says WHY instead
 * of just timing out.
 */
export async function launchBloom(
    options: ILaunchBloomOptions,
): Promise<ILaunchedBloom> {
    const sourceCollectionsRoot = getSourceCollectionsRoot();
    const sourceCollection = Path.join(
        sourceCollectionsRoot,
        options.collectionName,
    );
    if (!fs.existsSync(sourceCollection)) {
        const available = fs
            .readdirSync(sourceCollectionsRoot)
            .filter((f) =>
                fs.statSync(Path.join(sourceCollectionsRoot, f)).isDirectory(),
            );
        throw new Error(
            `There is no test-input collection named "${options.collectionName}". ` +
                `Available collections: ${available.join(", ")}.`,
        );
    }

    const exe = findBloomExe();
    // Canonicalize immediately: os.tmpdir() is an 8.3 short path on Windows but Bloom reports the
    // long form, and discovery below compares the two.
    const tempRoot = canonicalPath(
        fs.mkdtempSync(Path.join(os.tmpdir(), "bloom-e2e-")),
    );
    const collectionDir = Path.join(tempRoot, options.collectionName);
    copyCollection(sourceCollection, collectionDir);

    // Everything Bloom says, kept so a failed launch can report the reason.
    let bloomOutput = "";
    const recordOutput = (chunk: string) => {
        bloomOutput = (bloomOutput + chunk).slice(-MAX_BLOOM_OUTPUT);
    };
    const formatOutput = () => {
        const trimmed = bloomOutput.trim();
        return trimmed
            ? `  Bloom output (last ${MAX_BLOOM_OUTPUT} chars):\n${trimmed}`
            : `  Bloom output: (none captured)`;
    };

    // --e2e: skip the DEBUG "attach debugger now" prompt and suppress modal error dialogs.
    // --automation: let this instance run alongside a Bloom the developer already has open.
    const bloomProcess: ChildProcess = execFile(exe, [
        findCollectionFile(collectionDir),
        "--e2e",
        "--automation",
    ]);
    let exitStatus: { code: number | null; signal: string | null } | undefined;
    bloomProcess.stdout?.on("data", (d) => recordOutput(String(d)));
    bloomProcess.stderr?.on("data", (d) => recordOutput(String(d)));
    // 'error' fires when the exe cannot even be spawned.
    bloomProcess.on("error", (err) =>
        recordOutput(`\n[spawn error] ${err.message}\n`),
    );
    bloomProcess.on("exit", (code, signal) => {
        exitStatus = { code, signal };
    });

    // The pid of the Bloom actually serving our collection, read from instanceInfo once it is up.
    // It is not always the pid we spawned; see killProcessTree.
    let servingPid: number | undefined;

    // Tear down even if the run is aborted before the fixture's teardown runs.
    const cleanUpOnExit = () => {
        killProcessTree(
            [bloomProcess.pid, servingPid].filter(
                (p): p is number => typeof p === "number",
            ),
        );
        fs.rmSync(tempRoot, { recursive: true, force: true });
    };
    process.once("exit", cleanUpOnExit);

    const readyTimeoutMs = options.readyTimeoutMs ?? 120000;
    const startTime = Date.now();
    let found: { httpPort: number; info: IInstanceInfo } | undefined;
    while (!found && Date.now() - startTime < readyTimeoutMs) {
        found = await findBloomServingCollection(collectionDir);
        if (found) break;
        // Fail fast when Bloom died on startup, rather than waiting out the whole timeout: the
        // exit code plus its output says it crashed, not merely that discovery could not see it.
        if (exitStatus) {
            process.removeListener("exit", cleanUpOnExit);
            fs.rmSync(tempRoot, { recursive: true, force: true });
            throw new Error(
                `Bloom exited before serving the temp collection ` +
                    `(code ${exitStatus.code}, signal ${exitStatus.signal}).\n` +
                    `  exe: ${exe}\n  wanted: ${collectionDir}\n` +
                    formatOutput(),
            );
        }
        await delay(1000);
    }

    if (!found) {
        // Report which instances we could see, so a mismatch is diagnosable rather than opaque.
        const seen: string[] = [];
        for (const port of CANDIDATE_PORTS) {
            const info = await readInstanceInfo(port);
            if (info?.editableCollectionFolder)
                seen.push(`${port} -> ${info.editableCollectionFolder}`);
        }
        cleanUpOnExit();
        process.removeListener("exit", cleanUpOnExit);
        throw new Error(
            `Bloom did not open the temp collection within ${readyTimeoutMs / 1000}s.\n` +
                `  exe: ${exe}\n  wanted: ${collectionDir}\n` +
                `  still running: ${exitStatus ? "no (already exited)" : "yes"}\n` +
                `  Bloom instances seen: ${seen.length ? seen.join("; ") : "none"}\n` +
                formatOutput(),
        );
    }

    servingPid = found.info.processId;
    if (!found.info.cdpPort) {
        cleanUpOnExit();
        process.removeListener("exit", cleanUpOnExit);
        throw new Error(
            `Bloom is serving ${collectionDir} on port ${found.httpPort} but reported no CDP port, ` +
                `so tests cannot attach to its WebView2. Check that remote debugging is enabled in this build.`,
        );
    }

    const httpPort = found.httpPort;
    const stop = async () => {
        killProcessTree(
            [bloomProcess.pid, servingPid].filter(
                (p): p is number => typeof p === "number",
            ),
        );
        // Confirm the port really went dark before we try to delete the folder Bloom had open.
        // taskkill has been seen to under-kill, and a survivor holds file handles.
        const deadline = Date.now() + 15000;
        while (Date.now() < deadline) {
            if (!(await readInstanceInfo(httpPort))) break;
            await delay(500);
        }
        if (await readInstanceInfo(httpPort))
            throw new Error(
                `A Bloom is still answering on port ${httpPort} after teardown. ` +
                    `Kill pid ${servingPid ?? bloomProcess.pid} by hand before re-running.`,
            );
        // Bloom can release file handles slightly after it dies, so let rmSync retry.
        fs.rmSync(tempRoot, {
            recursive: true,
            force: true,
            maxRetries: 20,
            retryDelay: 500,
        });
        // Only after a fully successful teardown: if anything above threw, the exit handler
        // stays armed and still kills the survivor and deletes the temp folder at process exit.
        process.removeListener("exit", cleanUpOnExit);
    };

    return {
        httpPort,
        cdpPort: found.info.cdpPort,
        bloomPid: servingPid ?? bloomProcess.pid!,
        collectionDir,
        stop,
    };
}
