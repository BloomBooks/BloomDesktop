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
// The exception is apiPostFromNode, for a request sent while a modal dialog holds Bloom's UI
// thread: it uses node:http with the host "localhost", which sets the Host header Bloom wants.
//
// Per the UI-vs-API policy in README.md, use these for setup, navigation, and assertions. The
// behavior a test measures is always driven through the real UI.

import * as http from "node:http";
import { expect, type Page, type Request, type Route } from "@playwright/test";

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

/**
 * POST to a Bloom API endpoint from Node rather than through a page, and ignore the answer's
 * body. For a request sent while a modal dialog holds Bloom's UI thread: every page of Bloom's
 * browser shares one small pool of connections to Bloom's server, and requests waiting on that
 * thread can fill the pool, so a request sent from a page may never leave the browser. `page` is
 * any of Bloom's pages, for the port. `body`, when given, goes as text/plain. Rejects when Bloom answers with an error
 * status or does not answer in 30 s.
 */
export function apiPostFromNode(
    page: Page,
    endpoint: string,
    body?: string,
): Promise<void> {
    const port = Number(new URL(page.url()).port);
    const bytes = Buffer.from(body ?? "", "utf8");
    const headers: Record<string, string | number> = {
        "Content-Length": bytes.length,
    };
    if (body !== undefined) headers["Content-Type"] = "text/plain";
    return new Promise((resolve, reject) => {
        const req = http.request(
            {
                host: "localhost",
                port,
                path: `/bloom/api/${endpoint}`,
                method: "POST",
                headers,
                timeout: 30000,
            },
            (res) => {
                res.resume();
                res.on("end", () =>
                    res.statusCode && res.statusCode < 400
                        ? resolve()
                        : reject(
                              new Error(
                                  `${endpoint} answered ${res.statusCode}`,
                              ),
                          ),
                );
            },
        );
        req.on("timeout", () =>
            req.destroy(new Error(`${endpoint} got no answer`)),
        );
        req.on("error", reject);
        req.end(bytes);
    });
}

/**
 * The string with localization id `key` in Bloom's current UI language, as the UI shows it, or
 * `englishText` when that language has no translation of it. For finding or checking what the UI
 * says without assuming that it is in English: a test Bloom starts in the Windows UI language.
 */
export async function getUiString(
    page: Page,
    key: string,
    englishText: string,
): Promise<string> {
    const query = new URLSearchParams({ key, englishText, langId: "UI" });
    const answer = await apiGetJson<{ text: string }>(
        page,
        `i18n/translate?${query}`,
    );
    return answer.text;
}

// The pages whose Bloom has been seen to answer app/runningE2eTests with "true".
const pagesOnE2eBlooms = new WeakSet<Page>();

/**
 * Refuse an e2e/ call to a Bloom that was not launched with --e2e, before it is made. Such a Bloom
 * has none of the e2e/ endpoints, and it answers every call to a missing endpoint with a modal
 * "Bloom had a problem" dialog, so a helper that polls one (waitForCollectionReady treats a 404 as
 * "not ready yet") buries the Bloom in dialogs: 46 in 44 seconds, against a Bloom started by go.sh.
 * A 404 from the question itself means the collection is reopening, which the caller already
 * handles as it does a 404 from the e2e/ call.
 */
async function requireE2eBloom(page: Page): Promise<void> {
    if (pagesOnE2eBlooms.has(page)) return;
    // Through request(), so a reload of the shell mid-question is retried like any GET, whatever
    // the method of the e2e/ call that asked.
    const answer = await request(page, "app/runningE2eTests", {
        method: "GET",
    });
    if (answer.body !== "true")
        throw new Error(
            "This Bloom was not launched with --e2e, so it has no e2e/ endpoints, and calling one " +
                "would put up a problem dialog. Drive it with the fixture's Bloom, or launch one with --e2e.",
        );
    pagesOnE2eBlooms.add(page);
}

async function request(
    page: Page,
    endpoint: string,
    options: { method: string; body?: string; contentType?: string },
): Promise<IApiResponse> {
    // Bloom reloads the shell document when it switches workspace tabs (and rebuilds it
    // entirely for things like a UI language change), so a call that is in flight at that
    // moment dies - as "Execution context was destroyed" when the evaluate loses the race, or
    // as the fetch's own "TypeError: Failed to fetch" when the navigation aborts the request.
    // Both are transients of the reload, so GETs retry briefly (a genuinely dead server also
    // reads "Failed to fetch"; the deadline keeps that from hiding long). POSTs never retry
    // here: the server may have processed the request before the reload killed the reply, and
    // repeating a non-idempotent action would, say, make a second book - a call site that
    // knowingly races a reload tolerates the lost reply itself and confirms the effect
    // instead (see makeBookFromTemplate). A CLOSED page stays closed - not retried.
    if (endpoint.startsWith("e2e/")) await requireE2eBloom(page);
    const deadline = Date.now() + 15000;
    for (;;) {
        try {
            return await attemptRequest(page, endpoint, options);
        } catch (error) {
            if (
                options.method !== "GET" ||
                !/Execution context was destroyed|Failed to fetch/i.test(
                    String(error),
                ) ||
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

/** Some of Bloom's answers to its own front end, held back until `release` is called. */
export interface IHeldBackAnswers {
    /**
     * Wait until the front end has asked the held-back question at least once since the hold
     * began, and every OTHER question matching `others` that it asked since then has been
     * answered. From then on the held-back answer is the only one of them the page lacks.
     */
    onlyTheseAreMissing: () => Promise<void>;
    /** Let the held-back answers through, and stop holding. */
    release: () => Promise<void>;
}

/**
 * Hold back Bloom's answers to some of the API calls its own front end makes, so that a test can
 * look at the UI in the moment before an answer arrives, a moment that otherwise lasts a few
 * milliseconds and cannot be aimed at. Nothing is changed or made up: each request goes on to
 * Bloom untouched when `release` is called, and Bloom's real answer comes back.
 *
 * `endpoint` matches the URLs to watch; `isHeld` picks which of those requests to hold, and
 * `isOther` picks the ones `onlyTheseAreMissing` waits to see answered. Always call `release`,
 * in a `finally`, or the page being edited is left waiting.
 */
export async function holdBackAnswers(
    page: Page,
    endpoint: RegExp,
    isHeld: (request: Request) => boolean,
    others: RegExp,
    isOther: (request: Request) => boolean,
): Promise<IHeldBackAnswers> {
    const waiting: Route[] = [];
    let holding = true;
    const othersAsked = new Set<Request>();
    const othersAnswered = new Set<Request>();
    const noteAsked = (request: Request) => {
        if (others.test(request.url()) && isOther(request))
            othersAsked.add(request);
    };
    const noteAnswered = (request: Request) => {
        if (othersAsked.has(request)) othersAnswered.add(request);
    };
    const hold = async (route: Route) => {
        if (holding && isHeld(route.request())) waiting.push(route);
        else await route.fallback();
    };
    page.on("request", noteAsked);
    page.on("requestfinished", noteAnswered);
    page.on("requestfailed", noteAnswered);
    await page.route(endpoint, hold);
    return {
        onlyTheseAreMissing: async () => {
            await expect
                .poll(
                    () =>
                        waiting.length > 0 &&
                        othersAsked.size > 0 &&
                        othersAnswered.size === othersAsked.size,
                    {
                        timeout: 30000,
                        message:
                            `Waiting for the page to have asked ${endpoint} and to have had ` +
                            `every answer from ${others}.`,
                    },
                )
                .toBe(true);
        },
        release: async () => {
            holding = false;
            page.off("request", noteAsked);
            page.off("requestfinished", noteAnswered);
            page.off("requestfailed", noteAnswered);
            await page.unroute(endpoint, hold);
            // A request from a page that has since been left has nobody waiting for it, and
            // letting it through fails; that is not a problem for the page showing now.
            for (const route of waiting.splice(0))
                await route.continue().catch(() => undefined);
        },
    };
}
