// Call Bloom's HTTP API from a test.
//
// The calls go THROUGH THE PAGE (page.evaluate + fetch with a relative URL) rather than straight
// from Node, and that is not incidental. Two constraints pull in opposite directions:
//
//  - Bloom's HTTP server validates the Host header and accepts only "localhost". A fetch from Node
//    to http://127.0.0.1:<httpPort>/bloom/api/... comes back 400 with a generic HTML error page,
//    and Host is a forbidden header, so you cannot override it.
//  - Our CDP connection has to use 127.0.0.1, because WebView2's debugging port does not answer on
//    ::1, which is what "localhost" resolves to first on Windows.
//
// Issuing the request from inside the page satisfies both: the page's own origin is
// http://localhost:<httpPort>, so the Host header is right, while the Node-to-CDP link stays IPv4.
//
// Per the UI-vs-API policy in README.md, use these for setup, navigation, and assertions. The
// behavior a test measures is always driven through the real UI.

import type { Page } from "@playwright/test";

/** The response of a Bloom API call: its HTTP status and its body as text. */
export interface IApiResponse {
    status: number;
    body: string;
}

/**
 * GET a Bloom API endpoint, e.g. apiGet(page, "workspace/tabs"). `endpoint` is the part after
 * /bloom/api/. Throws when Bloom answers with an error status.
 */
export async function apiGet(
    page: Page,
    endpoint: string,
): Promise<IApiResponse> {
    return request(page, endpoint, { method: "GET" });
}

/**
 * POST to a Bloom API endpoint. `body` is sent as-is; pass a JSON string and set contentType to
 * "application/json" for the endpoints that expect JSON, or a bare string for those that read the
 * raw body. Throws when Bloom answers with an error status.
 */
export async function apiPost(
    page: Page,
    endpoint: string,
    body?: string,
    contentType?: string,
): Promise<IApiResponse> {
    return request(page, endpoint, { method: "POST", body, contentType });
}

/** Read a Bloom API endpoint that replies with JSON, parsed into T. */
export async function apiGetJson<T>(page: Page, endpoint: string): Promise<T> {
    const response = await apiGet(page, endpoint);
    return JSON.parse(response.body) as T;
}

async function request(
    page: Page,
    endpoint: string,
    options: { method: string; body?: string; contentType?: string },
): Promise<IApiResponse> {
    // Bloom reloads the shell document when it switches workspace tabs (and rebuilds it
    // entirely for things like a UI language change), so an evaluate that is in flight at that
    // moment dies with "Execution context was destroyed". That is a transient of the reload,
    // not a failure: retry briefly. A CLOSED page is a different matter - it stays closed - so
    // that error is not retried.
    const deadline = Date.now() + 15000;
    for (;;) {
        try {
            return await attemptRequest(page, endpoint, options);
        } catch (error) {
            if (
                !/Execution context was destroyed/i.test(String(error)) ||
                Date.now() > deadline
            )
                throw error;
            await new Promise((resolve) => setTimeout(resolve, 250));
        }
    }
}

async function attemptRequest(
    page: Page,
    endpoint: string,
    options: { method: string; body?: string; contentType?: string },
): Promise<IApiResponse> {
    const result = await page.evaluate(
        async (call) => {
            const response = await fetch(`/bloom/api/${call.endpoint}`, {
                method: call.method,
                headers: call.contentType
                    ? { "Content-Type": call.contentType }
                    : undefined,
                body: call.body,
            });
            return { status: response.status, body: await response.text() };
        },
        { endpoint, ...options },
    );
    if (result.status >= 400)
        throw new Error(
            `Bloom's ${options.method} /bloom/api/${endpoint} returned ${result.status}. ` +
                `Body: ${result.body.slice(0, 500)}`,
        );
    return result;
}
