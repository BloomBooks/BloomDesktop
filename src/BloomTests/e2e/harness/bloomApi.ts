// Thin wrapper around Bloom's local HTTP API (`/bloom/api/...`).
//
// DISCOVERED RACE: immediately after BLOOM_AUTOMATION_READY, some API endpoints
// (`teamCollection/showCreateCloudTeamCollectionDialog` observed concretely) can still 404
// with Bloom's own "Cannot Find API Endpoint" NonFatalProblem for roughly a second — endpoint
// registration apparently isn't fully complete the instant the HTTP listener starts accepting
// connections. `postApi`/`getApi` retry on 404 specifically (a real 404 for a nonexistent
// route would retry pointlessly for the same reason, but this harness only calls known-good
// routes, so that's an acceptable tradeoff for robustness against the startup race).
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
