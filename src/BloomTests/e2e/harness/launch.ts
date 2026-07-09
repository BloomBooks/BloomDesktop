// Builds and launches Bloom.exe instances for the E2E harness.
//
// HARD-WON ENVIRONMENT RULES this file encodes (see
// Design/CloudTeamCollections/orchestration/09-e2e.prompt.md):
//  1. Build ONCE per test session (`buildBloomOnce`), then launch the built exe directly N
//     times with per-instance environment. Never run `dotnet watch`/go.sh concurrently with
//     the harness — concurrent rebuilds into the shared `output/` tree produce stale binaries.
//  2. Building while any Bloom.exe runs fails on the locked apphost, so this module kills all
//     harness-owned instances before rebuilding, and FAILS LOUDLY if a foreign (non-harness)
//     Bloom.exe is already running at session start instead of silently testing stale code.
//  3. Kill via the known-good pattern from .github/skills/bloom-automation: killBloomProcess.mjs
//     by exact HTTP port, then verify the port went dark, falling back to a raw taskkill (via
//     PowerShell Stop-Process, NOT `taskkill /PID` in Git Bash — that gets mangled by MSYS).
//  4. RELEASE CONFIGURATION IS MANDATORY, not merely preferred: a Debug build shows a modal,
//     blocking "Attach debugger now" MessageBox (Program.cs, `#if DEBUG`, fires whenever
///    `args.Length > 0`) on EVERY launch that passes a positional argument — which every
//     harness launch does (the .bloomCollection path). This has no on-screen owner in a
//     headless/automated session, so it hangs forever with no HTTP/CDP port ever opening.
//     Discovered empirically (see progress log) by screen-capturing the launched window with
//     PrintWindow after Bloom sat with no listening port for 7+ minutes. Building
//     `-c Release` compiles that block out entirely — the one other DEBUG-gated behavior
//     (`HARVEST_FOR_LOCALIZATION` env passthrough) is unrelated to Cloud TC and not needed here.
//  5. Bloom picks its own HTTP/CDP ports; there is no `--remote-debugging-port` flag to set.
//     Readiness + ports come from parsing the `BLOOM_AUTOMATION_READY {...}` JSON line Bloom
//     prints to stdout once `--automation` is passed (see BloomServer.WriteAutomationStartupInfo).
import { spawn, execFile, ChildProcess } from "node:child_process";
import { promisify } from "node:util";
import * as fs from "node:fs";
import * as fsp from "node:fs/promises";
import * as path from "node:path";
import { chromium, Browser, Page } from "@playwright/test";
import { repoRoot, bloomExeCsproj, bloomAutomationSkillDir } from "./paths";
import { DevUser, cloudTcEnv } from "./devStack";
import { ensureExperimentalFeatureEnabled } from "./experimentalFlag";
import { startWindowPlacementWatcher } from "./windowPlacement";

const execFileAsync = promisify(execFile);

export const releaseExePath = path.join(
    repoRoot,
    "output",
    "Release",
    "AnyCPU",
    "Bloom.exe",
);

/** Builds BloomExe.csproj in Release config exactly once. Call this a single time per test
 * session (e.g. Playwright globalSetup) — never per-scenario, and never concurrently with a
 * running instance (rule #2 above). */
export const buildBloomOnce = async (): Promise<void> => {
    await assertNoForeignBloomRunning();
    await execFileAsync("dotnet", ["build", bloomExeCsproj, "-c", "Release"], {
        cwd: repoRoot,
        timeout: 300_000,
        windowsHide: true,
        maxBuffer: 64 * 1024 * 1024,
    });
    if (!fs.existsSync(releaseExePath)) {
        throw new Error(
            `Build reported success but ${releaseExePath} does not exist. Something is wrong with the build output path.`,
        );
    }
    await ensureExperimentalFeatureEnabled();
};

/** FAILS LOUDLY (throws) if a Bloom.exe from THIS repo tree is already running at session
 * start. Never silently proceed against such an instance — the caller could end up testing
 * against a stale binary or clobbering someone else's debugging session, and its process locks
 * this tree's build output. An instance from a DIFFERENT worktree (a developer debugging in
 * e.g. BloomDesktop.worktrees/CodeReview while the harness runs here) is only WARNED about:
 * it locks its own output tree, serves its own port block, and per
 * .claude/skills/run-bloom/SKILL.md is not ours to kill. */
export const assertNoForeignBloomRunning = async (): Promise<void> => {
    // This probe is fragile about HOW it is spawned (documented in the automation skills,
    // rediscovered the hard way killing three matrix runs in globalSetup):
    //  - when its stdout is a PIPE (execFile's default), it can die instantly with exit 1
    //    and ZERO output — the same run succeeds with inherited/console stdio (verified
    //    empirically 8 Jul 2026: node-parent + pipe = silent exit 1 every time, while
    //    stdio:'inherit' and a PowerShell parent both work);
    //  - it can also crash with a libuv assertion AFTER printing complete valid JSON.
    // Both are dodged the same way: never give it a pipe. Redirect its stdout to a temp
    // FILE via cmd, then read the file; parse errors after that are genuine failures.
    // Generous 60s timeout + one retry for the slow-first-run-after-idle mode.
    const runProbe = async (): Promise<string> => {
        const os = await import("node:os");
        const tempOut = path.join(
            os.tmpdir(),
            `bloom-probe-${process.pid}-${Math.random().toString(36).slice(2)}.json`,
        );
        const script = path.join(
            bloomAutomationSkillDir,
            "bloomProcessStatus.mjs",
        );
        const outHandle = await fsp.open(tempOut, "w");
        try {
            // Real file descriptor for stdout — no pipe (the crash trigger), no shell
            // (whose argument re-quoting mangled a cmd-redirect variant of this).
            await new Promise<void>((resolve) => {
                const child = spawn(
                    "node",
                    [script, "--running-bloom", "--json"],
                    {
                        cwd: repoRoot,
                        stdio: ["ignore", outHandle.fd, "ignore"],
                        windowsHide: true,
                    },
                );
                const timer = setTimeout(() => child.kill(), 60_000);
                // Exit code deliberately ignored: non-zero is fine IF the JSON landed in
                // the file (the post-print libuv crash); the parse below is the arbiter.
                child.once("exit", () => {
                    clearTimeout(timer);
                    resolve();
                });
                child.once("error", () => {
                    clearTimeout(timer);
                    resolve();
                });
            });
            await outHandle.close();
            const output = await fsp.readFile(tempOut, "utf8");
            JSON.parse(output); // throws -> caller retries/fails
            return output;
        } finally {
            await outHandle.close().catch(() => {});
            await fsp.rm(tempOut, { force: true }).catch(() => {});
        }
    };
    const normalize = (p?: string) =>
        (p ?? "")
            .replace(/[\\/]+/g, "\\")
            .replace(/\\$/, "")
            .toLowerCase();
    const thisRepo = normalize(repoRoot);

    let running: Array<{ detectedRepoRoot?: string; executablePath?: string }>;
    try {
        const status = JSON.parse(await runProbe());
        running = status.runningBloomInstances ?? [];
    } catch {
        // FALLBACK (8 Jul 2026): on an idling machine (scheduled AV scan crawling process
        // enumeration is the leading suspect) the probe never completes when spawned
        // without a console, in any stdio configuration tried — it burned four matrix-run
        // attempts. The safety property only needs "which Bloom.exe processes exist and
        // where from", which Get-Process answers directly. If even THIS fails, something
        // is genuinely wrong and we still fail loudly.
        const { stdout: psOut } = await execFileAsync(
            "powershell",
            [
                "-NoProfile",
                "-Command",
                "Get-Process Bloom -ErrorAction SilentlyContinue | ForEach-Object { $_.Path }",
            ],
            { cwd: repoRoot, timeout: 60_000, windowsHide: true },
        );
        running = psOut
            .split(/\r?\n/)
            .map((line) => line.trim())
            .filter(Boolean)
            .map((exePath) => ({ executablePath: exePath }));
        console.warn(
            `[harness] NOTE: bloomProcessStatus.mjs probe failed; used Get-Process fallback ` +
                `(${running.length} Bloom.exe found).`,
        );
    }
    const sameRepo = running.filter(
        (instance) =>
            normalize(instance.detectedRepoRoot) === thisRepo ||
            normalize(instance.executablePath).startsWith(thisRepo + "\\"),
    );
    if (sameRepo.length > 0) {
        throw new Error(
            `Refusing to build/launch: ${sameRepo.length} Bloom.exe instance(s) from THIS repo tree already running ` +
                `(${JSON.stringify(sameRepo)}). Kill them first (see .github/skills/bloom-automation/SKILL.md) ` +
                `so the harness never tests against a stale binary or fights the build for locked output files.`,
        );
    }
    const foreign = running.filter((instance) => !sameRepo.includes(instance));
    if (foreign.length > 0) {
        console.warn(
            `[harness] NOTE: ${foreign.length} Bloom.exe instance(s) from OTHER worktrees are running ` +
                `(${foreign.map((instance) => instance.detectedRepoRoot).join(", ")}). ` +
                `Leaving them alone; they hold their own port blocks, so harness instances will take later ports.`,
        );
    }
};

export interface LaunchedBloom {
    processId: number;
    httpPort: number;
    cdpPort: number;
    collectionFolder: string;
    logPath: string;
    kill: () => Promise<void>;
    connect: () => Promise<{ browser: Browser; page: Page }>;
}

export interface LaunchOptions {
    /** Path to the .bloomCollection file to open (skips the interactive chooser). */
    collectionFilePath: string;
    /** Dev user this instance signs in as (BLOOM_CLOUDTC_USER/_PASSWORD). Omit for
     * "not signed in yet" scenarios (e.g. exercising the in-app sign-in dialog). */
    user?: DevUser;
    /** Window-title label, also used to name the log file. Keep distinct per instance. */
    label: string;
    /** Directory to write this instance's stdout/stderr log into. */
    logDir: string;
    /** Milliseconds to wait for BLOOM_AUTOMATION_READY before failing. */
    readyTimeoutMs?: number;
}

/** Launches one Bloom.exe (Release build) instance non-interactively against the given
 * collection file, waits for the BLOOM_AUTOMATION_READY line, and returns its ports plus
 * kill()/connect() helpers. Multiple instances may run concurrently (each gets its own
 * HTTP/CDP port; the collection folder and BLOOM_CLOUDTC_USER give each its own identity). */
export const launchBloom = async (
    options: LaunchOptions,
): Promise<LaunchedBloom> => {
    if (!fs.existsSync(releaseExePath)) {
        throw new Error(
            `${releaseExePath} does not exist. Call buildBloomOnce() before launching any instance.`,
        );
    }

    await fsp.mkdir(options.logDir, { recursive: true });
    const logPath = path.join(
        options.logDir,
        `${sanitizeFileName(options.label)}.log`,
    );
    const logStream = fs.createWriteStream(logPath, { flags: "w" });

    const child: ChildProcess = spawn(
        releaseExePath,
        ["--automation", "--label", options.label, options.collectionFilePath],
        {
            cwd: repoRoot,
            env: { ...process.env, ...cloudTcEnv(options.user) },
            stdio: ["ignore", "pipe", "pipe"],
            windowsHide: false,
        },
    );

    let stdoutBuffer = "";
    let ready:
        | { processId: number; httpPort: number; cdpPort: number }
        | undefined;
    const readyPromise = new Promise<void>((resolve, reject) => {
        const onData = (chunk: Buffer) => {
            const text = chunk.toString("utf8");
            stdoutBuffer += text;
            logStream.write(chunk);
            const match = stdoutBuffer.match(/BLOOM_AUTOMATION_READY (\{.*\})/);
            if (match && !ready) {
                try {
                    ready = JSON.parse(match[1]);
                    resolve();
                } catch (error) {
                    reject(
                        new Error(
                            `Could not parse BLOOM_AUTOMATION_READY JSON for '${options.label}': ${error}`,
                        ),
                    );
                }
            }
        };
        child.stdout?.on("data", onData);
        child.stderr?.on("data", (chunk: Buffer) => logStream.write(chunk));
        child.once("exit", (code, signal) => {
            if (!ready) {
                reject(
                    new Error(
                        `Bloom instance '${options.label}' exited (code=${code}, signal=${signal}) ` +
                            `before reporting ready. Log: ${logPath}`,
                    ),
                );
            }
        });
        child.once("error", reject);
    });

    const timeoutMs = options.readyTimeoutMs ?? 90_000;
    await withTimeout(
        readyPromise,
        timeoutMs,
        `Bloom instance '${options.label}' did not print BLOOM_AUTOMATION_READY within ${timeoutMs}ms. Log: ${logPath}`,
    );

    if (!ready) {
        // Unreachable in practice (withTimeout throws first) but keeps TS happy.
        throw new Error(
            `Bloom instance '${options.label}' failed to report ready.`,
        );
    }

    const readyInfo = ready;
    // Opt-in (BLOOM_E2E_SCREEN): keep this instance's windows on a designated monitor so E2E
    // runs don't take over the developer's working screens. No-op when the variable is unset.
    const stopWindowPlacement = startWindowPlacementWatcher(
        readyInfo.processId,
    );
    return {
        processId: readyInfo.processId,
        httpPort: readyInfo.httpPort,
        cdpPort: readyInfo.cdpPort,
        collectionFolder: path.dirname(options.collectionFilePath),
        logPath,
        kill: () => {
            stopWindowPlacement();
            return killByHttpPort(readyInfo.httpPort, readyInfo.processId);
        },
        connect: () => connectOverCdp(readyInfo.cdpPort),
    };
};

/** Kills the exact Bloom instance bound to `httpPort` using the skill's HTTP-based exact-target
 * kill (never a broad kill-everything, so concurrent harness instances aren't disturbed), then
 * verifies the port went dark AND — when the caller knows it — that `processId` itself is gone.
 * The PID check matters: a Bloom stuck showing a modal error dialog (E2E-7's guard test
 * provokes one deliberately) can have its port go dark while Bloom.exe survives holding open
 * file handles, which broke the NEXT scenario's scratch-folder wipe with EBUSY during the
 * first full-matrix run. Port-dark is necessary but not sufficient; process-dead is the real
 * postcondition. (killBloomProcess.mjs has also plain under-killed before — see
 * .claude/skills/run-bloom/SKILL.md Gotchas.) */
export const killByHttpPort = async (
    httpPort: number,
    processId?: number,
): Promise<void> => {
    let killedProcessIds: number[] = [];
    try {
        const { stdout } = await execFileAsync(
            "node",
            [
                path.join(bloomAutomationSkillDir, "killBloomProcess.mjs"),
                "--http-port",
                String(httpPort),
                "--json",
            ],
            { cwd: repoRoot, timeout: 30_000, windowsHide: true },
        );
        killedProcessIds = JSON.parse(stdout).killedProcessIds ?? [];
    } catch {
        // fall through to the direct-PID / port-dark verification below
    }

    // Belt and braces: whatever the script reported, make sure the actual Bloom PID dies.
    const pidsToEnsureDead = [
        ...(processId ? [processId] : []),
        ...killedProcessIds,
    ];
    for (const pid of pidsToEnsureDead) {
        await execFileAsync("powershell", [
            "-NoProfile",
            "-Command",
            `Stop-Process -Id ${pid} -Force -ErrorAction SilentlyContinue`,
        ]).catch(() => {});
    }

    const wentDark = await waitForPortDark(httpPort, 10_000);
    if (!wentDark) {
        throw new Error(
            `Bloom instance on HTTP port ${httpPort} would not die (pid=${processId}, killedProcessIds=${JSON.stringify(killedProcessIds)}).`,
        );
    }
    if (processId) {
        const gone = await waitForProcessGone(processId, 10_000);
        if (!gone) {
            throw new Error(
                `Bloom PID ${processId} (port ${httpPort}) survived Stop-Process — it will hold ` +
                    `file handles and break later scenarios' resets. Investigate before rerunning.`,
            );
        }
    }
};

const waitForProcessGone = async (
    pid: number,
    timeoutMs: number,
): Promise<boolean> => {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        try {
            process.kill(pid, 0); // signal 0 = existence probe, kills nothing
        } catch {
            return true; // ESRCH: no such process
        }
        await sleep(500);
    }
    return false;
};

/** Kills every Bloom.exe the harness can find (used at session start/end to guarantee a clean
 * slate, and between scenarios if a test fails mid-way and leaves instances running). */
export const killAllBloomInstances = async (): Promise<void> => {
    await execFileAsync(
        "node",
        [path.join(bloomAutomationSkillDir, "killBloomProcess.mjs"), "--json"],
        { cwd: repoRoot, timeout: 30_000, windowsHide: true },
    ).catch(() => {});
};

const waitForPortDark = async (
    httpPort: number,
    timeoutMs: number,
): Promise<boolean> => {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        const alive = await probeHttp(httpPort);
        if (!alive) return true;
        await sleep(500);
    }
    return !(await probeHttp(httpPort));
};

const probeHttp = async (httpPort: number): Promise<boolean> => {
    try {
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), 1500);
        const response = await fetch(
            `http://localhost:${httpPort}/bloom/api/common/instanceInfo`,
            {
                signal: controller.signal,
            },
        );
        clearTimeout(timer);
        return response.ok;
    } catch {
        return false;
    }
};

/** Connects Playwright over CDP to a specific instance's CDP port and returns its Bloom page
 * (the top-level `/bloom/...` document — NOT a devtools:// page). Mirrors
 * react_components/component-tester/bloomExeCdp.ts but parameterized per-instance instead of
 * reading a single global env var, since this harness runs several instances at once. */
export const connectOverCdp = async (
    cdpPort: number,
): Promise<{ browser: Browser; page: Page }> => {
    const endpoint = `http://127.0.0.1:${cdpPort}`;
    // BLOOM_AUTOMATION_READY can print a beat before the WebView2/Edge remote-debugging
    // listener actually accepts connections (observed empirically: immediate connectOverCDP
    // right after the ready line sometimes gets ECONNREFUSED). Retry briefly rather than
    // failing on that harmless startup race.
    const browser = await retry(
        () => chromium.connectOverCDP(endpoint),
        15_000,
        `Could not connect over CDP to ${endpoint}`,
    );
    // Actions that make Bloom tear down and recreate its workspace WebView2 controls (e.g.
    // createCloudTeamCollection's reopen-collection callback) leave a window where no matching
    // page exists yet even though the browser-level CDP connection itself succeeds. Retry the
    // page lookup rather than failing on the first miss.
    const findPage = () => {
        const pages = browser.contexts().flatMap((context) => context.pages());
        return pages.find(
            (candidate) =>
                candidate.url().includes("/bloom/") &&
                !candidate.url().startsWith("devtools://"),
        );
    };
    let page = findPage();
    // Generous: under load (multiple Bloom instances + a live local Supabase/MinIO stack, and
    // possibly other processes competing for the machine's CPU/disk in a shared dev
    // environment) both fresh-launch startup and the workspace-reopen churn a cloud-collection
    // action triggers have been observed taking well over a minute before the main page
    // finishes navigating away from `about:blank` and becomes attachable.
    const deadline = Date.now() + 120_000;
    while (!page && Date.now() < deadline) {
        await sleep(500);
        page = findPage();
    }
    if (!page) {
        await browser.close();
        // Diagnostic: dump the raw CDP target list (bypassing Playwright entirely) so a
        // failure here shows whether the target is genuinely gone or Playwright just isn't
        // attaching to it.
        const rawTargets = await fetch(`${endpoint}/json/list`)
            .then((response) => response.json())
            .catch((error) => `<raw /json/list fetch failed: ${error}>`);
        throw new Error(
            `Could not find a Bloom WebView2 target on ${endpoint}. Raw CDP targets: ${JSON.stringify(rawTargets)}`,
        );
    }
    await page.waitForLoadState("domcontentloaded");
    return { browser, page };
};

const sanitizeFileName = (name: string): string =>
    name.replace(/[^a-zA-Z0-9_-]+/g, "_");

const sleep = (ms: number): Promise<void> =>
    new Promise((resolve) => setTimeout(resolve, ms));

const retry = async <T>(
    fn: () => Promise<T>,
    timeoutMs: number,
    message: string,
): Promise<T> => {
    const deadline = Date.now() + timeoutMs;
    let lastError: unknown;
    while (Date.now() < deadline) {
        try {
            return await fn();
        } catch (error) {
            lastError = error;
            await sleep(500);
        }
    }
    throw new Error(`${message}: ${lastError}`);
};

const withTimeout = async <T>(
    promise: Promise<T>,
    ms: number,
    message: string,
): Promise<T> => {
    let timer: NodeJS.Timeout;
    const timeout = new Promise<never>((_, reject) => {
        timer = setTimeout(() => reject(new Error(message)), ms);
    });
    try {
        return await Promise.race([promise, timeout]);
    } finally {
        clearTimeout(timer!);
    }
};
