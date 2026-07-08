// Thin wrapper around Bloom's local HTTP API (`/bloom/api/...`).
//
// DISCOVERED RACE: immediately after BLOOM_AUTOMATION_READY, some API endpoints
// (`teamCollection/showCreateCloudTeamCollectionDialog` observed concretely) can still 404
// with Bloom's own "Cannot Find API Endpoint" NonFatalProblem for roughly a second — endpoint
// registration apparently isn't fully complete the instant the HTTP listener starts accepting
// connections. `postAndRetryUntilRegistered` retries on 404 specifically (a real 404 for a
// nonexistent route would retry pointlessly for the same reason, but this harness only calls
// known-good routes, so that's an acceptable tradeoff for robustness against the startup race).
const DEFAULT_REGISTRATION_TIMEOUT_MS = 10_000;

/** POSTs to `route` (relative to `/bloom/api/`) with an empty body (Bloom's API requires a
 * Content-Length, so a plain POST with no body 411s — pass `""` explicitly). Retries on 404 for
 * up to `timeoutMs` to ride out the post-startup endpoint-registration race described above. */
export const postApi = async (
    httpPort: number,
    route: string,
    body = "",
    timeoutMs = DEFAULT_REGISTRATION_TIMEOUT_MS,
): Promise<Response> => {
    const url = `http://localhost:${httpPort}/bloom/api/${route}`;
    const deadline = Date.now() + timeoutMs;
    let lastResponse: Response;
    do {
        lastResponse = await fetch(url, { method: "POST", body });
        if (lastResponse.status !== 404) return lastResponse;
        await new Promise((resolve) => setTimeout(resolve, 300));
    } while (Date.now() < deadline);
    return lastResponse;
};

/** GETs `route`, retrying on 404 for the same post-startup registration race `postApi`
 * guards against. */
export const getApi = async (
    httpPort: number,
    route: string,
    timeoutMs = DEFAULT_REGISTRATION_TIMEOUT_MS,
): Promise<Response> => {
    const url = `http://localhost:${httpPort}/bloom/api/${route}`;
    const deadline = Date.now() + timeoutMs;
    let lastResponse: Response;
    do {
        lastResponse = await fetch(url);
        if (lastResponse.status !== 404) return lastResponse;
        await new Promise((resolve) => setTimeout(resolve, 300));
    } while (Date.now() < deadline);
    return lastResponse;
};
