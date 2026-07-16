// Thin wrapper around Bloom's local HTTP API (`/bloom/api/...`).
//
// DISCOVERED RACE: immediately after BLOOM_AUTOMATION_READY, some API endpoints
// (`teamCollection/showCreateCloudTeamCollectionDialog` observed concretely) can still 404
// with Bloom's own "Cannot Find API Endpoint" NonFatalProblem for roughly a second — endpoint
// registration apparently isn't fully complete the instant the HTTP listener starts accepting
// connections. `postApi`/`getApi` retry on 404 specifically (a real 404 for a nonexistent
// route would retry pointlessly for the same reason, but this harness only calls known-good
// routes, so that's an acceptable tradeoff for robustness against the startup race).
import { expect } from "@playwright/test";

const DEFAULT_REGISTRATION_TIMEOUT_MS = 10_000;
// Per-attempt request timeout: some Bloom operations (e.g. createCloudTeamCollection) tear down
// and recreate the workspace's WebView2 controls on the UI thread, and a handler that needs to
// marshal onto that thread can stall until it's free. Without a client-side abort, a genuinely
// stuck server call would hang `fetch` forever and only surface as Playwright's blunt whole-test
// timeout, with no indication of which call was the culprit. 30s is generous but bounded.
const DEFAULT_REQUEST_TIMEOUT_MS = 30_000;

const fetchWithTimeout = async (
    url: string,
    init: RequestInit,
    timeoutMs: number,
): Promise<Response> => {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    try {
        return await fetch(url, { ...init, signal: controller.signal });
    } catch (error) {
        if (controller.signal.aborted) {
            throw new Error(
                `Request to ${url} did not respond within ${timeoutMs}ms.`,
            );
        }
        throw error;
    } finally {
        clearTimeout(timer);
    }
};

/** POSTs to `route` (relative to `/bloom/api/`) with an empty body (Bloom's API requires a
 * Content-Length, so a plain POST with no body 411s — pass `""` explicitly). Retries on 404 for
 * up to `registrationTimeoutMs` to ride out the post-startup endpoint-registration race
 * described above; each individual attempt is bounded by `requestTimeoutMs`. */
export const postApi = async (
    httpPort: number,
    route: string,
    body = "",
    registrationTimeoutMs = DEFAULT_REGISTRATION_TIMEOUT_MS,
    requestTimeoutMs = DEFAULT_REQUEST_TIMEOUT_MS,
): Promise<Response> => {
    const url = `http://localhost:${httpPort}/bloom/api/${route}`;
    const deadline = Date.now() + registrationTimeoutMs;
    let lastResponse: Response;
    do {
        lastResponse = await fetchWithTimeout(
            url,
            { method: "POST", body },
            requestTimeoutMs,
        );
        if (lastResponse.status !== 404) return lastResponse;
        await new Promise((resolve) => setTimeout(resolve, 300));
    } while (Date.now() < deadline);
    return lastResponse;
};

/** POSTs teamCollection/createCloudTeamCollection with a much longer per-attempt timeout
 * (120s vs the default 30s). This one call runs create_collection + the FULL initial upload +
 * a workspace-reopen on the UI thread; during the first full-matrix run it twice exceeded 30s
 * on a healthy instance (E2E-5, E2E-6) while six sibling scenarios' creates were fine —
 * sporadic slowness, not deadlock. The genuine deadlock modes we know (locked desktop,
 * pre-workspace-init call) hang FOREVER, so a 120s bound still fails loudly on those. Do not
 * retry on timeout: the server side keeps processing after a client abort, so a retry would
 * race its own first attempt.
 *
 * A timeout has also been seen ONCE at the full 120s on a warm, unlocked machine (E2E-9,
 * third full-matrix run) — an as-yet-undiagnosed intermittent. Because we can't reproduce it
 * on demand, this helper self-documents the next occurrence: before failing, it captures a
 * managed thread dump of the still-hung Bloom via `dotnet-stack` (if installed:
 * `dotnet tool install -g dotnet-stack`) next to the instance's log. That dump is exactly
 * what diagnosed the original create deadlock (see tasks/09-e2e.md finding #7). */
export const postCreateCloudTeamCollection = async (instance: {
    httpPort: number;
    processId: number;
    logPath: string;
}): Promise<Response> => {
    try {
        return await postApi(
            instance.httpPort,
            "teamCollection/createCloudTeamCollection",
            "{}",
            DEFAULT_REGISTRATION_TIMEOUT_MS,
            120_000,
        );
    } catch (error) {
        const dumpPath = `${instance.logPath}.create-timeout-stacks.txt`;
        try {
            const { execFileSync } = await import("node:child_process");
            const dump = execFileSync(
                "dotnet-stack",
                ["report", "-p", String(instance.processId)],
                { timeout: 30_000, encoding: "utf8" },
            );
            const { writeFileSync } = await import("node:fs");
            writeFileSync(dumpPath, dump);
            throw new Error(
                `${(error as Error).message} — managed stack dump of the hung Bloom saved to ${dumpPath}`,
            );
        } catch (dumpError) {
            if (
                dumpError instanceof Error &&
                dumpError.message.includes("stack dump")
            )
                throw dumpError; // the enriched error above — pass it through
            throw error; // dotnet-stack missing/failed: report the original timeout
        }
    }
};

/** GETs `route`, retrying on 404 for the same post-startup registration race `postApi`
 * guards against, with the same per-attempt timeout. */
export const getApi = async (
    httpPort: number,
    route: string,
    registrationTimeoutMs = DEFAULT_REGISTRATION_TIMEOUT_MS,
    requestTimeoutMs = DEFAULT_REQUEST_TIMEOUT_MS,
): Promise<Response> => {
    const url = `http://localhost:${httpPort}/bloom/api/${route}`;
    const deadline = Date.now() + registrationTimeoutMs;
    let lastResponse: Response;
    do {
        lastResponse = await fetchWithTimeout(url, {}, requestTimeoutMs);
        if (lastResponse.status !== 404) return lastResponse;
        await new Promise((resolve) => setTimeout(resolve, 300));
    } while (Date.now() < deadline);
    return lastResponse;
};

/** Waits (up to `timeoutMs`, default 20s) until `teamCollection/capabilities` reports
 * `supportsSharingUi: true` — the harness's one readiness signal that the instance is genuinely
 * inside a live, connected cloud Team Collection.
 *
 * WHY this gate exists — POLL, don't single-shot: `teamCollection/capabilities` is an
 * application-level endpoint (post-batch defect 3 fix) that answers truthfully-false while the
 * project is still opening, and BLOOM_AUTOMATION_READY fires when the HTTP server starts
 * listening — which can be BEFORE the Team Collection has finished (re)connecting. UI-thread
 * endpoints gated on `_tcManager.CheckConnection()` (e.g. `teamCollection/checkInCurrentBook`)
 * 503 with an EMPTY body if called before that connection is live. This is a different
 * post-launch race than this module's 404 endpoint-registration retry (that one is about routes
 * existing at all; this one is about the cloud connection itself being live yet), so every
 * fresh launch, join, or mid-test relaunch that immediately needs a working cloud connection
 * must wait for this explicitly.
 *
 * A fetch failure counts as "not ready yet" rather than an immediate test failure: an in-place
 * collection switch (`workspace/openCollection`) reopens the workspace and can leave the
 * instance momentarily unreachable mid-poll (observed in join-auto-open.spec.ts). */
export const waitForSharingReady = async (
    httpPort: number,
    timeoutMs = 20_000,
): Promise<void> => {
    await expect
        .poll(
            async () => {
                try {
                    const caps = (await (
                        await getApi(httpPort, "teamCollection/capabilities")
                    ).json()) as { supportsSharingUi: boolean };
                    return caps.supportsSharingUi;
                } catch {
                    return false; // momentarily unreachable (e.g. during a workspace reopen)
                }
            },
            {
                timeout: timeoutMs,
                message:
                    "teamCollection/capabilities never reported supportsSharingUi: true (instance never became a connected cloud Team Collection)",
            },
        )
        .toBe(true);
};
