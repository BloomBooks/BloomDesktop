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
    /**
     * Shut Bloom down and start it again on the SAME collection folder, so the folder and
     * everything in it survive. `betweenStopAndStart` runs while no Bloom holds the files, which
     * is the only safe moment to rewrite the .bloomCollection: collection settings have no API,
     * and their dialog is a WinForms surface CDP cannot reach (see AUTOMATION-DEBT.md).
     *
     * The ports change, so the caller must re-attach. This object's httpPort, cdpPort and
     * bloomPid are updated in place; fixtures/bloomTest.ts reconnects over CDP.
     */
    restart: (
        betweenStopAndStart?: () => void | Promise<void>,
    ) => Promise<void>;
}

/**
 * What languages a created collection has. Bloom fills in everything else itself the first time
 * it opens the collection.
 */
export interface ICollectionSpec {
    /** The collection's folder and file name, e.g. "text-languages". */
    name: string;
    /**
     * Language tags for Language1, Language2 and Language3, in that order. Give one, two or
     * three; two entries mean the collection has no Language3.
     */
    languages: string[];
}

/** Options for launchBloom. Give exactly one of collectionName and collectionSpec. */
export interface ILaunchBloomOptions {
    /**
     * Name of a prepared folder under <testing-inputs>/collections, e.g. "basic". Use this only
     * for a fixture too expensive to build at run time, such as a collection of hundreds of
     * books. A shared fixture couples every test that uses it: whoever changes it next changes
     * the assumptions of tests they have never read. Otherwise use collectionSpec.
     */
    collectionName?: string;
    /** Create a collection for this test alone. The default choice. */
    collectionSpec?: ICollectionSpec;
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
 * build, so look in the known locations and take the MOST RECENTLY BUILT one. Recency, not list
 * order, is what matters: a machine can hold a stale build from another configuration (say, an
 * old Debug/x64 beside today's Debug/AnyCPU), and picking by fixed order silently runs the whole
 * suite against weeks-old code — a test here that depended on a same-PR C# change once launched a
 * two-week-old Bloom that way. Throws naming every folder it looked in.
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
    const existing = candidates.filter((c) => fs.existsSync(c));
    if (existing.length === 0) {
        throw new Error(
            `Could not find a built Bloom.exe (looked in: ${candidates.join(", ")}). ` +
                `Build Bloom, then re-run.`,
        );
    }
    return existing.reduce((newest, candidate) =>
        fs.statSync(candidate).mtimeMs > fs.statSync(newest).mtimeMs
            ? candidate
            : newest,
    );
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
    executablePath?: string;
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

/**
 * Write a minimal .bloomCollection for a brand-new collection, and return its folder.
 *
 * Only the fields Bloom needs in order to open the collection are written; it supplies its own
 * defaults for the rest and rewrites the file on first open.
 */
export function writeNewCollection(
    parentFolder: string,
    spec: ICollectionSpec,
): string {
    const collectionDir = Path.join(parentFolder, spec.name);
    fs.mkdirSync(collectionDir, { recursive: true });
    fs.writeFileSync(
        Path.join(collectionDir, `${spec.name}.bloomCollection`),
        makeCollectionXml(spec.languages),
        "utf8",
    );
    return collectionDir;
}

/**
 * The XML of a .bloomCollection with these languages. Exported because a test changes collection
 * settings by rewriting this file between a stop and a start (see ILaunchedBloom.restart).
 */
export function makeCollectionXml(languages: string[]): string {
    // Bloom treats Language2 as "same as Language1" when a collection names only one language,
    // which is what its own new-collection code writes.
    const [language1 = "en", language2 = languages[0] ?? "en", language3 = ""] =
        languages;
    const languageElements = [language1, language2, language3]
        .map(
            (tag, index) =>
                `  <Language${index + 1}Name>${nameForTag(tag)}</Language${index + 1}Name>\n` +
                `  <Language${index + 1}IsCustomName>false</Language${index + 1}IsCustomName>\n` +
                `  <Language${index + 1}Iso639Code>${tag}</Language${index + 1}Iso639Code>\n` +
                `  <DefaultLanguage${index + 1}FontName>Andika</DefaultLanguage${index + 1}FontName>`,
        )
        .join("\n");
    return (
        `<?xml version="1.0" encoding="utf-8"?>\n` +
        `<Collection version="0.2">\n` +
        languageElements +
        `\n  <XMatterPack>Factory</XMatterPack>\n` +
        `  <BrandingProjectName>Default</BrandingProjectName>\n` +
        `  <AllowNewBooks>True</AllowNewBooks>\n` +
        `  <PageNumberStyle>Decimal</PageNumberStyle>\n` +
        `</Collection>\n`
    );
}

/**
 * The name Bloom should show for a language tag. Bloom knows these names itself, but the
 * collection file wants one and a wrong name would show in the UI, so keep this to the languages
 * the tests use and fall back to the tag.
 */
function nameForTag(tag: string): string {
    const names: Record<string, string> = {
        "": "",
        en: "English",
        fr: "French",
        es: "Spanish",
        de: "German",
    };
    return names[tag] ?? tag;
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

/** One running Bloom process: the ports it opened and every pid worth killing. */
interface IRunningBloom {
    httpPort: number;
    cdpPort: number;
    servingPid: number;
    pids: number[];
}

/** Type guard for the pid lists below, which hold `number | undefined`. */
function isPid(pid: number | undefined): pid is number {
    return typeof pid === "number";
}

/**
 * Spawn Bloom.exe on an existing collection folder and wait until it is serving that folder.
 * Both the first launch and restart() go through here, so the two cannot drift apart.
 *
 * Throws with Bloom's own captured output when the launch fails, so a broken run says WHY instead
 * of just timing out. Cleaning up the temp folder is the caller's job: this function does not
 * know whether the folder is worth keeping.
 */
async function startBloomOn(
    collectionDir: string,
    readyTimeoutMs: number,
): Promise<IRunningBloom> {
    const exe = findBloomExe();

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

    const startTime = Date.now();
    let found: { httpPort: number; info: IInstanceInfo } | undefined;
    // When the process we spawned exits, that is USUALLY a startup crash, but Bloom can also
    // hand off to another instance of itself (which is why the serving pid can differ from the
    // spawned pid). So an exit starts a short grace window in which discovery keeps looking
    // for a successor; only when none appears do we treat the exit as a failure.
    let spawnedExitedAt: number | undefined;
    const handOffGraceMs = 10000;
    while (!found && Date.now() - startTime < readyTimeoutMs) {
        found = await findBloomServingCollection(collectionDir);
        if (found) break;
        if (exitStatus) {
            spawnedExitedAt ??= Date.now();
            if (Date.now() - spawnedExitedAt > handOffGraceMs)
                throw new Error(
                    `Bloom exited before serving the collection, and no successor ` +
                        `instance appeared within ${handOffGraceMs / 1000}s ` +
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
        killProcessTree([bloomProcess.pid].filter(isPid));
        throw new Error(
            `Bloom did not open the collection within ${readyTimeoutMs / 1000}s.\n` +
                `  exe: ${exe}\n  wanted: ${collectionDir}\n` +
                `  still running: ${exitStatus ? "no (already exited)" : "yes"}\n` +
                `  Bloom instances seen: ${seen.length ? seen.join("; ") : "none"}\n` +
                formatOutput(),
        );
    }

    if (!found.info.cdpPort) {
        killProcessTree([bloomProcess.pid, found.info.processId].filter(isPid));
        throw new Error(
            `Bloom is serving ${collectionDir} on port ${found.httpPort} but reported no CDP port, ` +
                `so tests cannot attach to its WebView2. Check that remote debugging is enabled in this build.`,
        );
    }

    return {
        httpPort: found.httpPort,
        cdpPort: found.info.cdpPort,
        servingPid: found.info.processId ?? bloomProcess.pid!,
        pids: [bloomProcess.pid, found.info.processId].filter(isPid),
    };
}

/**
 * Kill a running Bloom and wait until its HTTP port really goes dark. Shared by stop() and
 * restart(): a survivor holds file handles on the collection, which breaks both the delete and
 * the rewrite-then-relaunch.
 */
async function killAndWaitForPortToGoDark(
    running: IRunningBloom,
): Promise<void> {
    killProcessTree(running.pids);
    // Confirm rather than assume: taskkill has been seen to under-kill.
    const deadline = Date.now() + 15000;
    while (Date.now() < deadline) {
        if (!(await readInstanceInfo(running.httpPort))) return;
        await delay(500);
    }
    throw new Error(
        `A Bloom is still answering on port ${running.httpPort} after teardown. ` +
            `Kill pid ${running.servingPid} by hand before re-running.`,
    );
}

/**
 * Launch a dedicated Bloom.exe on a collection in a temp folder — one created for this test, or a
 * copy of a prepared fixture — and wait until it is serving it. The returned object carries the
 * discovered ports, a restart(), and a stop() that tears everything down.
 */
export async function launchBloom(
    options: ILaunchBloomOptions,
): Promise<ILaunchedBloom> {
    if (!options.collectionName === !options.collectionSpec)
        throw new Error(
            "launchBloom needs exactly one of collectionName (a prepared fixture) and " +
                "collectionSpec (a collection created for this test).",
        );

    // Canonicalize immediately: os.tmpdir() is an 8.3 short path on Windows but Bloom reports the
    // long form, and discovery compares the two.
    const tempRoot = canonicalPath(
        fs.mkdtempSync(Path.join(os.tmpdir(), "bloom-e2e-")),
    );

    let collectionDir: string;
    try {
        collectionDir = options.collectionSpec
            ? writeNewCollection(tempRoot, options.collectionSpec)
            : copyPreparedCollection(tempRoot, options.collectionName!);
    } catch (error) {
        fs.rmSync(tempRoot, { recursive: true, force: true });
        throw error;
    }

    const readyTimeoutMs = options.readyTimeoutMs ?? 120000;

    // The Bloom running right now. restart() replaces it, so everything that kills or reports on
    // Bloom reads this variable rather than capturing the first launch.
    let running: IRunningBloom | undefined;

    // Tear down even if the run is aborted before the fixture's teardown runs. Armed once, and it
    // reads `running`, so it still names the right pids after a restart.
    const cleanUpOnExit = () => {
        if (running) killProcessTree(running.pids);
        fs.rmSync(tempRoot, { recursive: true, force: true });
    };
    process.once("exit", cleanUpOnExit);

    try {
        running = await startBloomOn(collectionDir, readyTimeoutMs);
    } catch (error) {
        process.removeListener("exit", cleanUpOnExit);
        fs.rmSync(tempRoot, { recursive: true, force: true });
        throw error;
    }

    const launched: ILaunchedBloom = {
        httpPort: running.httpPort,
        cdpPort: running.cdpPort,
        bloomPid: running.servingPid,
        collectionDir,

        restart: async (betweenStopAndStart) => {
            await killAndWaitForPortToGoDark(running!);
            // Bloom releases its file handles slightly after it dies, and the caller is usually
            // about to rewrite one of the files it had open.
            await delay(1000);
            if (betweenStopAndStart) await betweenStopAndStart();
            running = await startBloomOn(collectionDir, readyTimeoutMs);
            launched.httpPort = running.httpPort;
            launched.cdpPort = running.cdpPort;
            launched.bloomPid = running.servingPid;
        },

        stop: async () => {
            await killAndWaitForPortToGoDark(running!);
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
        },
    };
    return launched;
}

/** A Bloom launched with no collection, showing the Choose Collection dialog. */
export interface ILaunchedChooserBloom {
    /** The HTTP port Bloom's server opened on. */
    httpPort: number;
    /** The port the WebView2 hosting the chooser dialog answers CDP on. */
    cdpPort: number;
    /** The process id of the Bloom that is showing the chooser. */
    bloomPid: number;
    /**
     * The .bloomCollection file of a collection created for this test, which the test can tell
     * the chooser to open (POST workspace/openCollection with this path as the body).
     */
    collectionToOpen: string;
    /** Kill the process tree, wait for the port to go dark, restore user.config, delete temp. */
    stop: () => Promise<void>;
}

/**
 * Where this repo's Bloom keeps (or will create) its user.config: the profile folder named for
 * the version in BloomExe.csproj, which is the version of any Bloom.exe built from this repo —
 * the only kind findBloomExe returns. Undefined only when the location cannot be computed.
 */
function versionedUserConfigPath(): string | undefined {
    if (!process.env.LOCALAPPDATA) return undefined;
    const versionMatch = fs
        .readFileSync(
            Path.join(repoRoot, "src", "BloomExe", "BloomExe.csproj"),
            "utf8",
        )
        .match(/<Version>([^<]+)<\/Version>/);
    if (!versionMatch) return undefined;
    return Path.join(
        process.env.LOCALAPPDATA,
        "SIL",
        "Bloom",
        versionMatch[1],
        "user.config",
    );
}

/**
 * The existing user.config of the Bloom this suite launches: the version-named profile when it
 * exists (see versionedUserConfigPath); otherwise fall back to the most recently written
 * profile. Undefined when no profile exists at all — then there is nothing to back up (the MRU
 * is effectively already empty), and anything the run creates is test residue.
 */
function findUserConfig(): string | undefined {
    const root = Path.join(process.env.LOCALAPPDATA ?? "", "SIL", "Bloom");
    if (!process.env.LOCALAPPDATA || !fs.existsSync(root)) return undefined;
    const versioned = versionedUserConfigPath();
    if (versioned && fs.existsSync(versioned)) return versioned;
    const configs = fs
        .readdirSync(root)
        .map((d) => Path.join(root, d, "user.config"))
        .filter((f) => fs.existsSync(f));
    if (configs.length === 0) return undefined;
    return configs.reduce((newest, candidate) =>
        fs.statSync(candidate).mtimeMs > fs.statSync(newest).mtimeMs
            ? candidate
            : newest,
    );
}

/**
 * Put the three settings the chooser test disturbs - the MRU list, the UI language, and the
 * unapproved-translations flag - back to what the original file had, while keeping whatever
 * ELSE the current file says. Bloom rewrites the whole file on many occasions, and the
 * developer's own Bloom may legitimately save settings while the test runs; restoring the
 * original bytes wholesale would silently discard those concurrent changes.
 * Falls back to the full original when the surgical splice cannot find its landmarks.
 */
function restoreDisturbedSettings(
    userConfig: string,
    originalText: string,
): void {
    const settingBlock = (name: string, text: string) =>
        text.match(
            new RegExp(`<setting name="${name}"[\\s\\S]*?</setting>`),
        )?.[0];
    let current = fs.readFileSync(userConfig, "utf8");
    let expected = 0;
    let spliced = 0;
    for (const name of [
        "MruProjects",
        "UserInterfaceLanguage",
        // Choosing a language marks it "explicitly chosen"; without restoring this flag, a
        // profile that was following the operating-system language would come out pinned to
        // one language instead.
        "UserInterfaceLanguageSetExplicitly",
        "ShowUnapprovedLocalizations",
    ]) {
        const original = settingBlock(name, originalText);
        if (!original) continue; // not in the original file: nothing to put back
        expected++;
        const currentBlock = settingBlock(name, current);
        if (currentBlock) {
            current = current.replace(currentBlock, original);
            spliced++;
        }
    }
    // All or nothing: a splice that only partly found its landmarks would leave some test
    // values behind, so an incomplete one falls back to the full original file.
    fs.writeFileSync(userConfig, spliced === expected ? current : originalText);
}

/**
 * Launch Bloom with NO collection, so it opens the Choose Collection dialog — the only way to
 * exercise that dialog's controls, since an open collection auto-reopens at startup.
 *
 * Reaching the chooser requires an empty MRU list, and the MRU lives in the developer's
 * machine-wide user.config. So this backs the file up, blanks the MRU (and normalizes the two
 * UI-language settings the tests assume), and after Bloom is dead stop() splices those settings
 * back into the file as they originally were — only those, so any concurrent saves from the
 * developer's own Bloom survive (see restoreDisturbedSettings). A process-exit hook does the
 * same when the run is aborted.
 *
 * The returned collectionToOpen names a collection created in a temp folder for this test, so
 * the test can leave the chooser by POSTing workspace/openCollection (the same call a click on
 * a collection card makes) without any native file dialog.
 */
export async function launchBloomIntoChooser(
    spec: ICollectionSpec,
): Promise<ILaunchedChooserBloom> {
    const readyTimeoutMs = 120000;
    const tempRoot = canonicalPath(
        fs.mkdtempSync(Path.join(os.tmpdir(), "bloom-e2e-chooser-")),
    );
    const collectionDir = writeNewCollection(tempRoot, spec);
    const collectionToOpen = findCollectionFile(collectionDir);

    // Back up the developer's settings file, then blank the MRU (so Bloom opens the chooser
    // rather than the last collection) and normalize the two UI-language settings the test's
    // assertions assume. <Path> elements occur only inside the MruProjects setting, so the
    // text-level removal is safe; stop() splices the developer's MRU and language settings
    // back into the file exactly as they were.
    const userConfig = findUserConfig();
    const originalUserConfig = userConfig
        ? fs.readFileSync(userConfig, "utf8")
        : undefined;
    const restoreUserConfig = () => {
        if (userConfig && originalUserConfig) {
            restoreDisturbedSettings(userConfig, originalUserConfig);
        } else {
            // No profile existed before this run, so whatever the launched Bloom created at
            // its own profile path is pure test residue (a temp collection in the MRU, the
            // test's language settings): delete it, leaving the machine as found.
            const created = versionedUserConfigPath();
            if (created && fs.existsSync(created))
                fs.rmSync(created, { force: true });
        }
    };
    if (userConfig && originalUserConfig) {
        fs.writeFileSync(
            userConfig,
            originalUserConfig
                .replace(/<Path>[\s\S]*?<\/Path>\s*/g, "")
                .replace(
                    /(<setting name="UserInterfaceLanguage"[^>]*>\s*<value>)[^<]*(<\/value>)/,
                    "$1en$2",
                )
                .replace(
                    /(<setting name="ShowUnapprovedLocalizations"[^>]*>\s*<value>)[^<]*(<\/value>)/,
                    "$1False$2",
                ),
        );
    }
    // The profile is modified (or about to be created by Bloom) from here on, so make sure it
    // gets restored even when the launch itself throws before the discovery loop arms its own
    // cleanup - findBloomExe, for one, throws when nothing is built.
    const launchSection = async <T>(work: () => Promise<T>): Promise<T> => {
        try {
            return await work();
        } catch (error) {
            cleanUpOnExit();
            process.removeListener("exit", cleanUpOnExit);
            throw error;
        }
    };

    // Armed BEFORE the launch and discovery (which can take two minutes): if the runner is
    // killed anywhere in that window, the profile still gets restored and the spawned Bloom
    // killed. pids fills in as processes become known.
    const pids: number[] = [];
    const cleanUpOnExit = () => {
        killProcessTree(pids);
        restoreUserConfig();
        fs.rmSync(tempRoot, { recursive: true, force: true });
    };
    process.once("exit", cleanUpOnExit);

    return launchSection(async () => {
        const exe = findBloomExe();
        let bloomOutput = "";
        const bloomProcess: ChildProcess = execFile(exe, [
            "--e2e",
            "--automation",
        ]);
        if (isPid(bloomProcess.pid)) pids.push(bloomProcess.pid);
        let exitStatus:
            | { code: number | null; signal: string | null }
            | undefined;
        bloomProcess.stdout?.on("data", (d) => {
            bloomOutput = (bloomOutput + String(d)).slice(-MAX_BLOOM_OUTPUT);
        });
        bloomProcess.stderr?.on("data", (d) => {
            bloomOutput = (bloomOutput + String(d)).slice(-MAX_BLOOM_OUTPUT);
        });
        bloomProcess.on("exit", (code, signal) => {
            exitStatus = { code, signal };
        });

        // Discovery cannot match on the open collection folder (there is none while the chooser
        // shows), so match the process: our spawned pid, or — because Bloom can hand off to a
        // successor process during startup — any instance with no collection open once the spawned
        // process has exited.
        const startTime = Date.now();
        let found:
            | { httpPort: number; cdpPort: number; processId: number }
            | undefined;
        while (!found && Date.now() - startTime < readyTimeoutMs) {
            for (const httpPort of CANDIDATE_PORTS) {
                const info = await readInstanceInfo(httpPort);
                if (!info?.processId || !info.cdpPort) continue;
                // Ours is the process we spawned - or, because Bloom can hand off to a successor
                // process during startup, an instance from the SAME exe with no collection open,
                // once the spawned process has exited. Requiring our exe path keeps a failed
                // startup from adopting (and later killing) some unrelated Bloom that happens to
                // be sitting at its own chooser.
                const isOurs =
                    info.processId === bloomProcess.pid ||
                    (exitStatus !== undefined &&
                        !info.editableCollectionFolder &&
                        !!info.executablePath &&
                        samePath(info.executablePath, exe));
                if (isOurs) {
                    found = {
                        httpPort,
                        cdpPort: info.cdpPort,
                        processId: info.processId,
                    };
                    break;
                }
            }
            if (!found) await delay(1000);
        }

        if (found && isPid(found.processId) && !pids.includes(found.processId))
            pids.push(found.processId);

        if (!found) {
            cleanUpOnExit();
            process.removeListener("exit", cleanUpOnExit);
            throw new Error(
                `Bloom never reached the Choose Collection dialog within ${readyTimeoutMs / 1000}s.\n` +
                    `  exe: ${exe}\n  spawned pid: ${bloomProcess.pid}, exited: ${JSON.stringify(exitStatus) || "no"}\n` +
                    (bloomOutput.trim()
                        ? `  Bloom output:\n${bloomOutput.trim()}`
                        : "  Bloom output: (none captured)"),
            );
        }

        const running: IRunningBloom = {
            httpPort: found.httpPort,
            cdpPort: found.cdpPort,
            servingPid: found.processId,
            pids,
        };
        return {
            httpPort: running.httpPort,
            cdpPort: running.cdpPort,
            bloomPid: running.servingPid,
            collectionToOpen,
            stop: async () => {
                await killAndWaitForPortToGoDark(running);
                restoreUserConfig();
                fs.rmSync(tempRoot, {
                    recursive: true,
                    force: true,
                    maxRetries: 20,
                    retryDelay: 500,
                });
                process.removeListener("exit", cleanUpOnExit);
            },
        };
    });
}

/**
 * Copy a prepared collection from the inputs repository into the temp folder, and return the
 * copy. Throws, naming the collections that do exist, when the name is not one of them.
 */
function copyPreparedCollection(
    tempRoot: string,
    collectionName: string,
): string {
    const sourceCollectionsRoot = getSourceCollectionsRoot();
    const sourceCollection = Path.join(sourceCollectionsRoot, collectionName);
    if (!fs.existsSync(sourceCollection)) {
        const available = fs
            .readdirSync(sourceCollectionsRoot)
            .filter((f) =>
                fs.statSync(Path.join(sourceCollectionsRoot, f)).isDirectory(),
            );
        throw new Error(
            `There is no test-input collection named "${collectionName}". ` +
                `Available collections: ${available.join(", ")}.`,
        );
    }
    const collectionDir = Path.join(tempRoot, collectionName);
    copyCollection(sourceCollection, collectionDir);
    return collectionDir;
}
