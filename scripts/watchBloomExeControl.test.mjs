import assert from "node:assert/strict";
import test from "node:test";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";

import {
    buildStatusPayload,
    deriveLauncherState,
    getDiscoveryFilePath,
    getMissingInitMarkers,
    isDotnetWatchRestartSignal,
    readDiscoveryFile,
    removeDiscoveryFile,
    startControlServer,
    writeDiscoveryFile,
} from "./watchBloomExeControl.mjs";

test("isDotnetWatchRestartSignal matches file-change lines only", () => {
    const restartSignals = [
        // .NET 10's watcher (the one go.sh actually runs) says "File updated".
        "dotnet watch ⌚ File updated: D:\\bloom\\BuildServer\\src\\BloomExe\\Shell.cs",
        "dotnet watch ⌚ File changed: D:\\bloom\\BuildServer\\src\\BloomExe\\Shell.cs.",
        "[exe] dotnet watch ⌚ File added: ./src/BloomExe/New.cs",
        "dotnet watch ⌚ File deleted: ./src/BloomExe/Old.cs",
    ];
    for (const line of restartSignals) {
        assert.equal(isDotnetWatchRestartSignal(line), true, line);
    }

    const notRestartSignals = [
        // The idle line after the app exits on its own — the case that must
        // tear the stack down, so it must NOT read as a restart signal.
        "dotnet watch ⌚ Waiting for a file to change before restarting ...",
        "dotnet watch ⌚ [BloomExe (net8.0-windows)] Exited",
        "dotnet watch 🔨 Building...",
        "dotnet watch 🔥 Hot reload of changes succeeded.",
        "Bloom ready. HTTP 8089, CDP 8091, Bloom PID 123.",
        "File changed: something (a non-watcher line)",
    ];
    for (const line of notRestartSignals) {
        assert.equal(isDotnetWatchRestartSignal(line), false, line);
    }
});

test("deriveLauncherState covers each state with defined precedence", () => {
    const cases = [
        // [flags, expectedState]
        [{ restartInProgress: true }, "restarting"],
        // restarting wins even if stale flags linger from the previous launch
        [
            {
                restartInProgress: true,
                awaitingManualRestart: true,
                launchCompleted: true,
                bloomRunning: true,
            },
            "restarting",
        ],
        [{ awaitingManualRestart: true }, "awaiting-restart"],
        [
            { awaitingManualRestart: true, launchCompleted: true },
            "awaiting-restart",
        ],
        [{ launchFailed: true }, "launch-failed"],
        [{ launchCompleted: true, bloomRunning: true }, "bloom-running"],
        // launch completed but Bloom just exited and the monitor has not
        // reacted yet: transient, reported as building
        [{ launchCompleted: true, bloomRunning: false }, "building"],
        [{}, "building"],
    ];

    for (const [flags, expectedState] of cases) {
        assert.equal(
            deriveLauncherState(flags),
            expectedState,
            `flags ${JSON.stringify(flags)}`,
        );
    }
});

test("buildStatusPayload derives state and passes launcher facts through", () => {
    const payload = buildStatusPayload({
        launchCompleted: true,
        bloomRunning: true,
        watchChildAlive: true,
        launchNumber: 3,
        dotnetWatchPid: 111,
        bloomProcessId: 222,
        httpPort: 8089,
        cdpPort: 8091,
        vitePort: 5173,
        label: "/BuildServer/",
        repoRoot: "D:\\bloom\\BuildServer",
        launcherPid: 333,
        goPid: 444,
        startedAt: "2026-01-01T00:00:00.000Z",
        bloomReadyAt: 1234,
    });

    assert.equal(payload.state, "bloom-running");
    assert.equal(payload.kind, "bloom-launcher");
    assert.equal(payload.launchNumber, 3);
    assert.equal(payload.bloomProcessId, 222);
    assert.equal(payload.httpPort, 8089);
    assert.equal(payload.cdpPort, 8091);
    assert.equal(payload.vitePort, 5173);
    assert.equal(payload.watchChildAlive, true);
    assert.equal(payload.goPid, 444);
});

test("discovery file helpers roundtrip, tolerate garbage, and remove quietly", () => {
    const tempDir = mkdtempSync(path.join(os.tmpdir(), "bloom-launcher-test-"));

    try {
        const filePath = getDiscoveryFilePath(tempDir);
        assert.equal(
            filePath,
            path.join(tempDir, "output", "bloom-launcher.json"),
        );

        // Sanity: nothing there yet.
        assert.equal(readDiscoveryFile(filePath), undefined);

        const info = { schemaVersion: 1, controlPort: 4567, launcherPid: 99 };
        writeDiscoveryFile(filePath, info); // creates output/ as needed
        assert.deepEqual(readDiscoveryFile(filePath), info);

        writeFileSync(filePath, "not json at all");
        assert.equal(readDiscoveryFile(filePath), undefined);

        removeDiscoveryFile(filePath);
        assert.equal(readDiscoveryFile(filePath), undefined);
        // Removing a file that is already gone must not throw.
        removeDiscoveryFile(filePath);
    } finally {
        rmSync(tempDir, { recursive: true, force: true });
    }
});

test("getMissingInitMarkers reports each absent init.sh product", () => {
    const tempDir = mkdtempSync(path.join(os.tmpdir(), "bloom-init-test-"));

    try {
        // Empty worktree: everything is missing.
        const allMissing = getMissingInitMarkers(tempDir);
        assert.equal(allMissing.length, 4);

        // Create every marker; nothing is missing.
        for (const relativePath of [
            "src/BloomBrowserUI/node_modules",
            "src/content/node_modules",
        ]) {
            const dirPath = path.join(tempDir, relativePath);
            writeDiscoveryFile(path.join(dirPath, "placeholder.json"), {}); // mkdir -p + file
        }
        for (const relativePath of [
            "lib/dotnet/PodcastUtilities.PortableDevices.dll",
            "output/browser/bookPreviewBundle.js",
        ]) {
            writeDiscoveryFile(path.join(tempDir, relativePath), {});
        }
        assert.deepEqual(getMissingInitMarkers(tempDir), []);

        // Remove one marker; exactly that one is reported.
        rmSync(path.join(tempDir, "output/browser/bookPreviewBundle.js"));
        const oneMissing = getMissingInitMarkers(tempDir);
        assert.equal(oneMissing.length, 1);
        assert.equal(oneMissing[0][0], "output/browser/bookPreviewBundle.js");
    } finally {
        rmSync(tempDir, { recursive: true, force: true });
    }
});

// Spins up a real control server against stubbed actions and a mutable
// snapshot, so routing/guard behavior is tested over actual HTTP.
const withControlServer = async (run) => {
    const calls = [];
    const snapshot = {
        launchCompleted: true,
        bloomRunning: true,
        launchNumber: 1,
    };
    const settle = () => new Promise((resolve) => setTimeout(resolve, 100));

    const { server, port } = await startControlServer({
        actions: {
            restart: () => calls.push("restart"),
            quitBloom: () => calls.push("quitBloom"),
            shutdownStack: () => calls.push("shutdownStack"),
        },
        getSnapshot: () => ({ ...snapshot }),
        log: () => {},
    });

    try {
        const request = async (method, route) => {
            const response = await fetch(`http://127.0.0.1:${port}${route}`, {
                method,
            });
            return { status: response.status, body: await response.json() };
        };

        await run({ request, snapshot, calls, settle });
    } finally {
        server.close();
    }
};

test("GET /status reports the derived state", async () => {
    await withControlServer(async ({ request, snapshot }) => {
        let { status, body } = await request("GET", "/status");
        assert.equal(status, 200);
        assert.equal(body.state, "bloom-running");
        assert.equal(body.launchNumber, 1);

        snapshot.bloomRunning = false;
        snapshot.awaitingManualRestart = true;
        ({ status, body } = await request("GET", "/status"));
        assert.equal(body.state, "awaiting-restart");
    });
});

test("POST /restart triggers the restart action in any non-restarting state", async () => {
    await withControlServer(async ({ request, snapshot, calls, settle }) => {
        const { status, body } = await request("POST", "/restart");
        assert.equal(status, 202);
        assert.equal(body.previousState, "bloom-running");
        await settle();
        assert.deepEqual(calls, ["restart"]);

        snapshot.restartInProgress = true;
        const repeat = await request("POST", "/restart");
        assert.equal(repeat.status, 200);
        assert.match(repeat.body.note, /already in progress/);
        await settle();
        assert.deepEqual(
            calls,
            ["restart"],
            "no second restart while restarting",
        );
    });
});

test("POST /start only works when awaiting-restart", async () => {
    await withControlServer(async ({ request, snapshot, calls, settle }) => {
        const denied = await request("POST", "/start");
        assert.equal(denied.status, 409);
        assert.equal(denied.body.state, "bloom-running");

        snapshot.bloomRunning = false;
        snapshot.awaitingManualRestart = true;
        const accepted = await request("POST", "/start");
        assert.equal(accepted.status, 202);
        await settle();
        assert.deepEqual(calls, ["restart"]);
    });
});

test("POST /quit-bloom guards awaiting and restarting states", async () => {
    await withControlServer(async ({ request, snapshot, calls, settle }) => {
        const accepted = await request("POST", "/quit-bloom");
        assert.equal(accepted.status, 202);
        await settle();
        assert.deepEqual(calls, ["quitBloom"]);

        snapshot.bloomRunning = false;
        snapshot.awaitingManualRestart = true;
        const noop = await request("POST", "/quit-bloom");
        assert.equal(noop.status, 200);
        assert.match(noop.body.note, /already stopped/);

        snapshot.awaitingManualRestart = false;
        snapshot.restartInProgress = true;
        const denied = await request("POST", "/quit-bloom");
        assert.equal(denied.status, 409);
        await settle();
        assert.deepEqual(
            calls,
            ["quitBloom"],
            "guards must not re-invoke quit",
        );
    });
});

test("POST /shutdown fires shutdownStack after the response", async () => {
    await withControlServer(async ({ request, calls, settle }) => {
        const { status } = await request("POST", "/shutdown");
        assert.equal(status, 202);
        assert.deepEqual(
            calls,
            [],
            "action is deferred until response flushes",
        );
        await settle();
        assert.deepEqual(calls, ["shutdownStack"]);
    });
});

test("unknown routes get 404 and wrong methods get 405", async () => {
    await withControlServer(async ({ request, calls, settle }) => {
        const missing = await request("POST", "/nope");
        assert.equal(missing.status, 404);

        const statusPost = await request("POST", "/status");
        assert.equal(statusPost.status, 405);

        const restartGet = await request("GET", "/restart");
        assert.equal(restartGet.status, 405);

        await settle();
        assert.deepEqual(calls, [], "no actions fired for rejected requests");
    });
});
