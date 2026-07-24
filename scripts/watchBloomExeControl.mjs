import http from "node:http";
import {
    existsSync,
    mkdirSync,
    readFileSync,
    rmSync,
    writeFileSync,
} from "node:fs";
import path from "node:path";

// Machine-readable stdout marker, parallel to BLOOM_AUTOMATION_READY: lets an
// agent that just launched go.sh grab the control port from the log without
// racing the discovery-file write.
export const launcherReadyPrefix = "BLOOM_LAUNCHER_READY ";

export const discoveryFileSchemaVersion = 1;

// The discovery file advertises a running launcher for one worktree. It lives
// under output/ (gitignored). It can be left behind by a hard kill, so
// consumers must treat the HTTP probe of controlUrl as the only source of
// truth about liveness.
export const getDiscoveryFilePath = (repoRoot) =>
    path.join(repoRoot, "output", "bloom-launcher.json");

export const writeDiscoveryFile = (filePath, info) => {
    mkdirSync(path.dirname(filePath), { recursive: true });
    writeFileSync(filePath, `${JSON.stringify(info, null, 4)}\n`);
};

// Best-effort: cleanup paths must never throw (they run in exit handlers).
export const removeDiscoveryFile = (filePath) => {
    try {
        rmSync(filePath, { force: true });
    } catch {}
};

// Returns undefined for a missing or unparseable file.
export const readDiscoveryFile = (filePath) => {
    try {
        return JSON.parse(readFileSync(filePath, "utf8"));
    } catch {
        return undefined;
    }
};

// One marker per init.sh step. If any is missing, the worktree was created but
// ./init.sh has not (fully) run, and go.mjs runs it before launching — without
// this, go.sh appears to succeed but Bloom later fails on requests for files
// only a full init produces (e.g. "Cannot Find File: bookPreviewBundle.js").
const initMarkers = [
    ["src/BloomBrowserUI/node_modules", "front-end packages (pnpm install)"],
    ["src/content/node_modules", "content packages (pnpm install)"],
    [
        "lib/dotnet/PodcastUtilities.PortableDevices.dll",
        "C# binary dependencies (getDependencies)",
    ],
    [
        "output/browser/bookPreviewBundle.js",
        "static browser bundles (pnpm build)",
    ],
];

// Returns [relativePath, description] for each init.sh product that is absent.
export const getMissingInitMarkers = (repoRoot) =>
    initMarkers.filter(
        ([relativePath]) => !existsSync(path.join(repoRoot, relativePath)),
    );

// True for dotnet-watch "a file changed" lines — the signal that any imminent
// Bloom exit is the watcher rebuilding the app, not the developer closing it.
// Deliberately narrow: the idle line "Waiting for a file to change before
// restarting ..." (printed when the app exits on its own) must NOT match.
export const isDotnetWatchRestartSignal = (line) =>
    /dotnet watch/i.test(line) &&
    // The verb varies by SDK version: .NET 10's watcher says "File updated".
    /File (changed|updated|added|created|deleted|renamed)/i.test(line);

// Derives the launcher's externally visible state from the flags
// watchBloomExe.mjs already maintains. Note that during a dotnet-watch
// hot rebuild (C# file edit while Bloom runs) the monitor transiently reports
// awaiting-restart until the rebuilt Bloom announces itself; clients that need
// "Bloom is up" should poll for bloom-running rather than sampling once.
export const deriveLauncherState = ({
    restartInProgress,
    launchCompleted,
    launchFailed,
    awaitingManualRestart,
    bloomRunning,
}) => {
    if (restartInProgress) {
        return "restarting";
    }

    if (awaitingManualRestart) {
        return "awaiting-restart";
    }

    if (launchFailed) {
        return "launch-failed";
    }

    if (launchCompleted && bloomRunning) {
        return "bloom-running";
    }

    // Covers the initial build, dotnet-watch rebuilds before the ready line,
    // and the sub-second window between Bloom exiting and the monitor noticing.
    return "building";
};

// Shapes the GET /status response from a snapshot of launcher state.
export const buildStatusPayload = (snapshot) => ({
    schemaVersion: discoveryFileSchemaVersion,
    kind: "bloom-launcher",
    state: deriveLauncherState(snapshot),
    awaitingManualRestart: !!snapshot.awaitingManualRestart,
    watchChildAlive: !!snapshot.watchChildAlive,
    launchNumber: snapshot.launchNumber,
    dotnetWatchPid: snapshot.dotnetWatchPid,
    bloomProcessId: snapshot.bloomProcessId,
    httpPort: snapshot.httpPort,
    cdpPort: snapshot.cdpPort,
    vitePort: snapshot.vitePort,
    sourceChangedSinceReady: !!snapshot.sourceChangedSinceReady,
    label: snapshot.label,
    repoRoot: snapshot.repoRoot,
    launcherPid: snapshot.launcherPid,
    goPid: snapshot.goPid,
    logPath: snapshot.logPath,
    startedAt: snapshot.startedAt,
    bloomReadyAt: snapshot.bloomReadyAt,
});

const sendJson = (response, statusCode, body) => {
    const text = `${JSON.stringify(body, null, 4)}\n`;
    response.writeHead(statusCode, {
        "Content-Type": "application/json",
        "Content-Length": Buffer.byteLength(text),
    });
    response.end(text);
};

// Starts the loopback-only control server through which agents drive the
// launcher. `actions` supplies {restart, quitBloom, shutdownStack};
// `getSnapshot` returns the current launcher state for /status and routing
// guards; `log` prints human-visible lines to the launcher's terminal.
export const startControlServer = ({
    host = "127.0.0.1",
    actions,
    getSnapshot,
    log = console.log,
}) => {
    const runAction = (name, action) => {
        Promise.resolve()
            .then(action)
            .catch((error) => {
                log(
                    `[control] ${name} failed: ${error instanceof Error ? error.message : String(error)}`,
                );
            });
    };

    const server = http.createServer((request, response) => {
        const url = new URL(request.url, `http://${host}`);
        const route = url.pathname.replace(/\/+$/, "") || "/";
        const snapshot = getSnapshot();
        const state = deriveLauncherState(snapshot);

        if (route === "/status") {
            if (request.method !== "GET") {
                sendJson(response, 405, {
                    ok: false,
                    error: "/status only supports GET.",
                });
                return;
            }

            sendJson(response, 200, buildStatusPayload(snapshot));
            return;
        }

        const isActionRoute = [
            "/restart",
            "/start",
            "/quit-bloom",
            "/shutdown",
        ].includes(route);

        if (!isActionRoute) {
            sendJson(response, 404, {
                ok: false,
                error: `Unknown endpoint ${route}. Supported: GET /status, POST /restart, POST /start, POST /quit-bloom, POST /shutdown.`,
            });
            return;
        }

        if (request.method !== "POST") {
            sendJson(response, 405, {
                ok: false,
                error: `${route} only supports POST.`,
            });
            return;
        }

        if (route === "/restart") {
            if (state === "restarting") {
                sendJson(response, 200, {
                    ok: true,
                    state,
                    note: "A restart is already in progress.",
                });
                return;
            }

            log("[control] restart requested via control API");
            sendJson(response, 202, {
                ok: true,
                action: "restart",
                previousState: state,
            });
            runAction("restart", actions.restart);
            return;
        }

        if (route === "/start") {
            if (state !== "awaiting-restart") {
                sendJson(response, 409, {
                    ok: false,
                    state,
                    error: `start is only valid in the awaiting-restart state (current: ${state}). Use /restart to force a rebuild+relaunch.`,
                });
                return;
            }

            log("[control] start requested via control API");
            sendJson(response, 202, {
                ok: true,
                action: "start",
                previousState: state,
            });
            runAction("start", actions.restart);
            return;
        }

        if (route === "/quit-bloom") {
            if (state === "awaiting-restart") {
                sendJson(response, 200, {
                    ok: true,
                    state,
                    note: "Bloom is already stopped.",
                });
                return;
            }

            if (state === "restarting") {
                sendJson(response, 409, {
                    ok: false,
                    state,
                    error: "A restart is in progress; retry quit-bloom once it settles.",
                });
                return;
            }

            log("[control] quit-bloom requested via control API");
            sendJson(response, 202, {
                ok: true,
                action: "quit-bloom",
                previousState: state,
            });
            runAction("quit-bloom", actions.quitBloom);
            return;
        }

        // /shutdown: valid in every state. Delay the action slightly so the
        // response flushes before the process starts tearing itself down.
        log("[control] shutdown requested via control API");
        sendJson(response, 202, {
            ok: true,
            action: "shutdown",
            previousState: state,
        });
        setTimeout(() => runAction("shutdown", actions.shutdownStack), 50);
    });

    return new Promise((resolve, reject) => {
        server.once("error", reject);
        server.listen(0, host, () => {
            server.removeListener("error", reject);
            resolve({ server, port: server.address().port });
        });
    });
};
