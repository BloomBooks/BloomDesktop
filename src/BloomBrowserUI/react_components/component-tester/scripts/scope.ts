import { spawn, spawnSync } from "node:child_process";
import { randomUUID } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { fileURLToPath } from "node:url";

type ScopeArgs = {
    useBackend: boolean;
    modulePath?: string;
    exportName?: string;
};

const componentTesterDir = path.resolve(
    path.dirname(fileURLToPath(import.meta.url)),
    "..",
);

const parseArgs = (args: string[]): ScopeArgs => {
    const parsed: ScopeArgs = { useBackend: false };
    const positional: string[] = [];

    for (const arg of args) {
        if (arg === "--detach") {
            console.error(
                "--detach is no longer supported. Start the dev server in another terminal (cd src/BloomBrowserUI/react_components/component-tester && yarn dev), then rerun yarn scope.",
            );
            process.exit(1);
        }
        if (arg === "--backend") {
            parsed.useBackend = true;
            continue;
        }
        if (arg === "--help" || arg === "-h") {
            printUsage();
            process.exit(0);
        }
        if (arg === "--") {
            continue;
        }
        if (arg.startsWith("-")) {
            console.error(`Unknown option: ${arg}`);
            printUsage();
            process.exit(1);
        }
        positional.push(arg);
    }

    if (positional.length === 1) {
        parsed.exportName = positional[0];
    } else if (positional.length > 1) {
        console.error("Expected at most one export name.");
        printUsage();
        process.exit(1);
    }

    return parsed;
};

const printUsage = (): void => {
    console.log("Usage: yarn scope [--backend] [exportName]");
    console.log("");
    console.log(
        "If exportName is omitted, the script uses the default export.",
    );
    console.log("The script searches upward for scope-harness.tsx.");
};

const findScopeHarnessPath = (startDir: string): string | undefined => {
    let current = startDir;

    while (true) {
        const candidate = path.join(current, "scope-harness.tsx");
        if (existsSync(candidate)) {
            return candidate;
        }

        const parent = path.dirname(current);
        if (parent === current) {
            return undefined;
        }
        current = parent;
    }
};

const assertExportExists = (harnessPath: string, exportName: string): void => {
    const content = readFileSync(harnessPath, "utf-8");
    if (exportName === "default") {
        if (!/export\s+default\b/.test(content)) {
            throw new Error(`No default export found in ${harnessPath}.`);
        }
        return;
    }

    const escapedName = exportName.replace(/[$()*+./?[\\\]^{|}-]/g, "\\$&");
    const direct = new RegExp(
        `export\\s+(?:const|function|class)\\s+${escapedName}\\b`,
    );
    const exportList = new RegExp(
        `export\\s*{[\\s\\S]*?\\b${escapedName}\\b[\\s\\S]*?}`,
    );
    const exportAs = new RegExp(
        `export\\s*{[\\s\\S]*?\\bas\\s+${escapedName}\\b[\\s\\S]*?}`,
    );

    if (
        !direct.test(content) &&
        !exportList.test(content) &&
        !exportAs.test(content)
    ) {
        throw new Error(
            `Export "${exportName}" was not found in ${harnessPath}.`,
        );
    }
};

const toModulePath = (harnessPath: string): string => {
    const relativePath = path.relative(componentTesterDir, harnessPath);
    const withoutExt = relativePath.replace(/\.[^.]+$/, "");
    return withoutExt.split(path.sep).join("/");
};

const resolveScopeArgs = (parsed: ScopeArgs): ScopeArgs => {
    const originCwd = process.env.INIT_CWD ?? process.cwd();
    const harnessPath = findScopeHarnessPath(originCwd);
    if (!harnessPath) {
        throw new Error(
            `Could not find scope-harness.tsx from ${originCwd}. Run from a component folder or pass an export name.`,
        );
    }

    const exportName = parsed.exportName ?? "default";
    assertExportExists(harnessPath, exportName);
    const modulePath = toModulePath(harnessPath);

    return {
        useBackend: parsed.useBackend,
        modulePath,
        exportName,
    };
};

const ensureComponentTesterDeps = (): void => {
    const result = spawnSync("yarn", ["-s", "vite", "--version"], {
        cwd: componentTesterDir,
        stdio: "ignore",
    });
    if (result.status !== 0) {
        console.error("component-tester dependencies appear to be missing.");
        console.error(`Fix: (cd "${componentTesterDir}" && yarn install)`);
        process.exit(1);
    }
};

const sleep = (ms: number): Promise<void> =>
    new Promise((resolve) => setTimeout(resolve, ms));

const harnessMarker = "bloom-scope-harness";

type HarnessProbeResult = "no-response" | "not-harness" | "harness";

const isDevToolsListening = async (port: number): Promise<boolean> => {
    try {
        const controller = AbortSignal.timeout(400);
        const res = await fetch(`http://127.0.0.1:${port}/json/version`, {
            cache: "no-store",
            headers: { "Cache-Control": "no-store" },
            signal: controller,
        });
        return res.ok;
    } catch {
        return false;
    }
};

type DevToolsTargetInfo = {
    id?: string;
    title?: string;
    url?: string;
    webSocketDebuggerUrl?: string;
};

const openUrlInExistingDevToolsBrowser = async (
    port: number,
    url: string,
): Promise<DevToolsTargetInfo | undefined> => {
    try {
        const controller = AbortSignal.timeout(800);
        const endpoint = `http://127.0.0.1:${port}/json/new?${encodeURIComponent(url)}`;

        // Chrome currently requires PUT here (GET yields a warning response and does not open a tab).
        const putRes = await fetch(endpoint, {
            method: "PUT",
            cache: "no-store",
            headers: { "Cache-Control": "no-store" },
            signal: controller,
        });
        if (putRes.ok) {
            const text = await putRes.text();
            try {
                return JSON.parse(text) as DevToolsTargetInfo;
            } catch {
                return {};
            }
        }

        // Fallback for older Chrome versions.
        const getRes = await fetch(endpoint, {
            cache: "no-store",
            headers: { "Cache-Control": "no-store" },
            signal: controller,
        });
        if (!getRes.ok) {
            return undefined;
        }
        const text = await getRes.text();
        try {
            return JSON.parse(text) as DevToolsTargetInfo;
        } catch {
            return {};
        }
    } catch {
        return undefined;
    }
};

const probeHarness = async (
    host: string,
    port: number,
): Promise<HarnessProbeResult> => {
    try {
        const controller = AbortSignal.timeout(800);
        const res = await fetch(`http://${host}:${port}/`, {
            cache: "no-store",
            headers: { "Cache-Control": "no-store" },
            signal: controller,
        });
        if (!res.ok) {
            return "not-harness";
        }
        const text = await res.text();
        return text.includes(harnessMarker) ? "harness" : "not-harness";
    } catch {
        return "no-response";
    }
};

const findVitePort = async (
    host: string,
    preferred?: number,
): Promise<number | undefined> => {
    const ports = preferred
        ? [preferred]
        : Array.from({ length: 20 }, (_, i) => 5183 + i);
    for (const port of ports) {
        if ((await probeHarness(host, port)) === "harness") {
            return port;
        }
    }
    return undefined;
};

const findListeningPid = (port: number): number | undefined => {
    if (process.platform !== "win32") {
        return undefined;
    }

    const result = spawnSync("netstat", ["-ano", "-p", "tcp"], {
        encoding: "utf-8",
    });
    if (result.status !== 0) {
        return undefined;
    }

    const portText = `:${port}`;
    const lines = result.stdout.split(/\r?\n/);
    for (const line of lines) {
        if (!line.includes(portText) || !line.includes("LISTENING")) {
            continue;
        }
        const parts = line.trim().split(/\s+/);
        const pidText = parts.at(-1);
        const pid = pidText ? Number(pidText) : NaN;
        if (Number.isFinite(pid)) {
            return pid;
        }
    }
    return undefined;
};

const resolveCommand = (command: string): string | undefined => {
    if (process.platform === "win32") {
        const result = spawnSync("where", [command], { encoding: "utf-8" });
        if (result.status !== 0) {
            return undefined;
        }
        const line = result.stdout.split(/\r?\n/).find(Boolean);
        return line ?? undefined;
    }

    const result = spawnSync("command", ["-v", command], { encoding: "utf-8" });
    if (result.status !== 0) {
        return undefined;
    }
    return result.stdout.trim() || undefined;
};

const resolveBrowserExecutable = (): string => {
    const requested = process.env.SCOPE_BROWSER_EXE ?? "chrome.exe";
    const requestedLower = requested.toLowerCase();
    const candidates: string[] = [];

    const resolved = resolveCommand(requested);
    if (resolved) {
        candidates.push(resolved);
    }

    if (process.platform === "win32") {
        const programFiles = process.env.ProgramFiles ?? "C:\\Program Files";
        const programFilesX86 =
            process.env["ProgramFiles(x86)"] ?? "C:\\Program Files (x86)";
        const localAppData = process.env.LocalAppData ?? "";
        const chromeBeta = [
            path.join(
                programFiles,
                "Google",
                "Chrome Beta",
                "Application",
                "chrome.exe",
            ),
            path.join(
                programFilesX86,
                "Google",
                "Chrome Beta",
                "Application",
                "chrome.exe",
            ),
            path.join(
                localAppData,
                "Google",
                "Chrome Beta",
                "Application",
                "chrome.exe",
            ),
        ];
        const chromeStable = [
            path.join(
                programFiles,
                "Google",
                "Chrome",
                "Application",
                "chrome.exe",
            ),
            path.join(
                programFilesX86,
                "Google",
                "Chrome",
                "Application",
                "chrome.exe",
            ),
            path.join(
                localAppData,
                "Google",
                "Chrome",
                "Application",
                "chrome.exe",
            ),
        ];
        const edge = [
            path.join(
                programFiles,
                "Microsoft",
                "Edge",
                "Application",
                "msedge.exe",
            ),
            path.join(
                programFilesX86,
                "Microsoft",
                "Edge",
                "Application",
                "msedge.exe",
            ),
            path.join(
                localAppData,
                "Microsoft",
                "Edge",
                "Application",
                "msedge.exe",
            ),
        ];

        if (requestedLower === "chrome.exe") {
            candidates.push(...chromeBeta, ...chromeStable, ...edge);
        } else {
            candidates.push(requested, ...chromeBeta, ...chromeStable, ...edge);
        }

        const found = candidates.find((candidate) => existsSync(candidate));
        if (found) {
            return found;
        }
    } else {
        if (requested) {
            candidates.push(requested);
        }
        const fallbacks = [
            "google-chrome",
            "google-chrome-stable",
            "chromium",
            "chromium-browser",
            "chrome",
            "msedge",
        ];
        for (const fallback of fallbacks) {
            const pathFromEnv = resolveCommand(fallback);
            if (pathFromEnv) {
                candidates.push(pathFromEnv);
            }
        }

        const found = candidates.find((candidate) =>
            candidate ? existsSync(candidate) : false,
        );
        if (found) {
            return found;
        }
    }

    throw new Error(
        `Could not find browser executable: ${requested}. Set SCOPE_BROWSER_EXE to chrome.exe or msedge.exe.`,
    );
};

const setDevToolsToConsole = (profileDir: string): void => {
    const defaultDir = path.join(profileDir, "Default");
    const prefsPath = path.join(defaultDir, "Preferences");
    const desired = JSON.stringify("console");

    type ChromePreferences = {
        devtools?: {
            preferences?: Record<string, string>;
        };
    };

    let prefs: ChromePreferences = {};
    if (existsSync(prefsPath)) {
        prefs = JSON.parse(
            readFileSync(prefsPath, "utf-8"),
        ) as ChromePreferences;
    } else {
        mkdirSync(defaultDir, { recursive: true });
    }

    prefs.devtools = prefs.devtools ?? {};
    prefs.devtools.preferences = prefs.devtools.preferences ?? {};
    if (prefs.devtools.preferences["panel-selected-tab"] !== desired) {
        prefs.devtools.preferences["panel-selected-tab"] = desired;
        writeFileSync(prefsPath, JSON.stringify(prefs));
    }
};

const openScopeBrowser = async (
    url: string,
    waitForHarness: boolean,
): Promise<void> => {
    if (waitForHarness) {
        const deadline = Date.now() + 20000;
        while (Date.now() < deadline) {
            try {
                const res = await fetch(url, {
                    cache: "no-store",
                    headers: { "Cache-Control": "no-store" },
                });
                if (res.ok) {
                    const text = await res.text();
                    if (text.includes(harnessMarker)) {
                        break;
                    }
                }
            } catch {
                // Ignore until deadline.
            }
            await sleep(250);
        }
    }

    const browserExe = resolveBrowserExecutable();
    const debugPort = Number(process.env.SCOPE_CHROME_DEBUG_PORT ?? "9223");

    // If a debug-enabled browser is already running, open a new tab there.
    // This allows running `yarn scope` multiple times while a previous scope dev server is still running.
    if (Number.isFinite(debugPort) && (await isDevToolsListening(debugPort))) {
        const target = await openUrlInExistingDevToolsBrowser(debugPort, url);
        if (target) {
            console.log(
                `Reusing existing DevTools browser on port ${debugPort} (opened a new tab).`,
            );
            if (target.id) {
                console.log(`DevTools target id: ${target.id}`);
            }
            if (target.webSocketDebuggerUrl) {
                console.log(`DevTools ws: ${target.webSocketDebuggerUrl}`);
            }
            return;
        }

        console.warn(
            `DevTools is listening on port ${debugPort}, but the script could not open a new tab automatically.`,
        );
        console.warn(
            "Fix: close the existing debug-enabled browser and retry, or set SCOPE_CHROME_DEBUG_PORT to a different port.",
        );
    }

    const profileDir = path.join(os.tmpdir(), "bloom-scope-chrome-profile");
    mkdirSync(profileDir, { recursive: true });
    setDevToolsToConsole(profileDir);

    const args = [
        `--remote-debugging-port=${debugPort}`,
        `--user-data-dir=${profileDir}`,
        "--auto-open-devtools-for-tabs",
        "--no-first-run",
        "--no-default-browser-check",
        url,
    ];

    const child = spawn(browserExe, args, {
        stdio: "ignore",
    });
    child.unref();
};

const startForegroundServer = (
    host: string,
    port: number,
    options: { useBackend: boolean; backendUrl?: string },
): void => {
    ensureComponentTesterDeps();
    const childEnv = { ...process.env };
    if (options.useBackend) {
        childEnv.BLOOM_SCOPE_USE_BACKEND = "1";
        childEnv.BLOOM_COMPONENT_TESTER_USE_BACKEND = "1";
        if (options.backendUrl) {
            childEnv.BLOOM_SCOPE_BACKEND_URL = options.backendUrl;
            childEnv.BLOOM_COMPONENT_TESTER_BACKEND_URL = options.backendUrl;
        }
    }
    const child = spawn(
        "yarn",
        ["dev", "--", "--host", host, "--port", String(port), "--strictPort"],
        {
            cwd: componentTesterDir,
            stdio: "inherit",
            env: childEnv,
        },
    );
    child.on("exit", (code) => process.exit(code ?? 0));
};

const run = async (): Promise<void> => {
    process.chdir(componentTesterDir);
    const parsed = parseArgs(process.argv.slice(2));

    const envUseBackend =
        process.env.BLOOM_SCOPE_USE_BACKEND === "1" ||
        process.env.BLOOM_COMPONENT_TESTER_USE_BACKEND === "1";
    const envBackendUrl =
        process.env.BLOOM_SCOPE_BACKEND_URL ??
        process.env.BLOOM_COMPONENT_TESTER_BACKEND_URL;

    const useBackend = parsed.useBackend || envUseBackend;
    const backendUrl = envBackendUrl;

    const resolved = resolveScopeArgs(parsed);
    if (!resolved.modulePath || !resolved.exportName) {
        throw new Error("Failed to resolve modulePath/exportName.");
    }

    const host = process.env.SCOPE_VITE_HOST ?? "127.0.0.1";
    const preferredPort = process.env.SCOPE_VITE_PORT
        ? Number(process.env.SCOPE_VITE_PORT)
        : undefined;
    let vitePort: number | undefined;
    let foundExistingServer = false;
    let shouldStartServer = false;

    if (preferredPort) {
        const preferredProbe = await probeHarness(host, preferredPort);
        if (preferredProbe === "harness") {
            vitePort = preferredPort;
            foundExistingServer = true;
        } else {
            const foundPort = await findVitePort(host);
            if (foundPort) {
                vitePort = foundPort;
                foundExistingServer = true;
                console.log(
                    `Note: no scope dev server found on preferred port ${preferredPort}; using the existing one on port ${foundPort}.`,
                );
            } else {
                if (preferredProbe === "not-harness") {
                    throw new Error(
                        `Port ${preferredPort} is in use, but it doesn't look like the scope dev server. Stop whatever is using that port, or set SCOPE_VITE_PORT to a different port.`,
                    );
                }

                vitePort = preferredPort;
                shouldStartServer = true;
            }
        }
    } else {
        const foundPort = await findVitePort(host);
        if (foundPort) {
            vitePort = foundPort;
            foundExistingServer = true;
        } else {
            vitePort = 5183;
            shouldStartServer = true;
        }
    }

    const encodedModulePath = encodeURIComponent(resolved.modulePath);
    const encodedExportName = encodeURIComponent(resolved.exportName);
    const scopeRunId = randomUUID();
    const encodedScopeRunId = encodeURIComponent(scopeRunId);
    const url = `http://${host}:${vitePort}/?modulePath=${encodedModulePath}&exportName=${encodedExportName}&scopeRunId=${encodedScopeRunId}`;

    if (shouldStartServer) {
        console.log("No running dev server found. Starting one now...");
        void openScopeBrowser(url, true);
        console.log("");
        console.log(
            `Starting scope dev server in this terminal on http://${host}:${vitePort}/`,
        );
        console.log("(Press Ctrl+C to stop it.)");
        if (useBackend) {
            const target = backendUrl ?? "http://localhost:8089";
            console.log(`Using Bloom backend at ${target}.`);
        }
        startForegroundServer(host, vitePort, { useBackend, backendUrl });
        return;
    }

    if (foundExistingServer && vitePort) {
        if (useBackend) {
            console.warn(
                "Note: a dev server is already running. If it was started without --backend, stop it and re-run with --backend.",
            );
        }
        const pid = findListeningPid(vitePort);
        const serverUrl = `http://${host}:${vitePort}/`;
        const pidSuffix = pid ? ` (pid ${pid})` : "";
        console.log(
            `Reusing existing scope dev server on ${serverUrl}${pidSuffix}`,
        );
    }

    await openScopeBrowser(url, false);

    console.log("");
    console.log(`Scope run id: ${scopeRunId}`);
    console.log(`Scope URL: ${url}`);
    console.log(
        `DevTools targets: http://127.0.0.1:${
            process.env.SCOPE_CHROME_DEBUG_PORT ?? "9223"
        }/json/list`,
    );
    console.log("");
    console.log(
        `This script has opened the scope on port ${
            process.env.SCOPE_CHROME_DEBUG_PORT ?? "9223"
        } (remote debugging enabled).`,
    );
    console.log("");
    console.log("Do NOT open any browsers.");
    console.log(
        "Use a DevTools-protocol tool to attach to the already-open tab instead.",
    );
    console.log("");
};

run().catch((error) => {
    console.error(error instanceof Error ? error.message : error);
    process.exit(1);
});
