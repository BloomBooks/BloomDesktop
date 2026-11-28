// Call Bloom's HTTP API from a test.
//
// Every call goes straight from Node to http://localhost:<httpPort>/bloom/api/..., the port taken
// from the Bloom page the caller passes. Two things make that the way:
//
//  - Bloom's HTTP server validates the Host header and accepts only "localhost": a request to
//    http://127.0.0.1:<httpPort>/... comes back 400 with a generic HTML error page. So the
//    address here is always "localhost". (The CDP connection is the other way round: WebView2's
//    debugging port answers only on 127.0.0.1; see fixtures/bloomTest.ts.)
//  - A request sent from inside one of Bloom's pages (page.evaluate + fetch) can fail to leave the
//    browser: every page of Bloom's browser shares one small pool of connections to Bloom's
//    server, and while a modal dialog holds Bloom's UI thread, requests waiting on that thread can
//    fill the pool. A request from Node has connections of its own. It also outlives a reload of
//    the page it was given the port by.
//
// Per the UI-vs-API policy in README.md, use these for setup, navigation, and assertions. The
// behavior a test measures is always driven through the real UI.

import { expect, type Page, type Request, type Route } from "@playwright/test";

/** The response of a Bloom API call: its HTTP status and its body as text. */
export interface IApiResponse {
    status: number;
    body: string;
}

/**
 * GET a Bloom API endpoint, e.g. apiGet(page, "workspace/tabs"). `endpoint` is the part after
 * /bloom/api/, and `page` is any page that the Bloom serves, for its port. Throws when Bloom
 * answers with an error status.
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
 * raw body. Throws when Bloom answers with an error status, or, when `timeoutMs` is given, when
 * Bloom has not answered within it.
 */
export async function apiPost(
    page: Page,
    endpoint: string,
    body?: string,
    contentType?: string,
    timeoutMs?: number,
): Promise<IApiResponse> {
    return request(page, endpoint, {
        method: "POST",
        body,
        contentType,
        timeoutMs,
    });
}

/** Read a Bloom API endpoint that replies with JSON, parsed into T. */
export async function apiGetJson<T>(page: Page, endpoint: string): Promise<T> {
    const response = await apiGet(page, endpoint);
    return JSON.parse(response.body) as T;
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

// The port of the Bloom that served each page, once seen. A page that Bloom is reloading can show
// about:blank for a moment, and a page that Bloom replaced is still the same Bloom's.
const bloomPorts = new WeakMap<Page, number>();

function localhostPort(url: string): number | undefined {
    try {
        const parsed = new URL(url);
        if (parsed.hostname !== "localhost" || !parsed.port) return undefined;
        return Number(parsed.port);
    } catch {
        return undefined;
    }
}

/**
 * The port of the Bloom that serves `page`: from its address, or, while it has none of
 * Bloom's (a reload in progress), the one it last had, or that of another page in the same
 * browser context that Bloom serves.
 */
function bloomPort(page: Page): number {
    const own = localhostPort(page.url());
    if (own !== undefined) {
        bloomPorts.set(page, own);
        return own;
    }
    const remembered = bloomPorts.get(page);
    if (remembered !== undefined) return remembered;
    const ports = new Set(
        page
            .context()
            .pages()
            .map((other) => localhostPort(other.url()))
            .filter((port): port is number => port !== undefined),
    );
    if (ports.size === 1) return [...ports][0];
    throw new Error(
        `Cannot tell which Bloom to call: ${page.url()} is not a page that Bloom serves, and ` +
            `the other pages of its browser come from ${ports.size} Bloom ports.`,
    );
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
    options: {
        method: string;
        body?: string;
        contentType?: string;
        timeoutMs?: number;
    },
): Promise<IApiResponse> {
    if (endpoint.startsWith("e2e/")) await requireE2eBloom(page);
    // A GET that finds nobody listening is tried again for a while: Bloom may be restarting its
    // server. Nothing else is repeated: a POST that Bloom may have carried out must not be sent twice.
    const deadline = Date.now() + 15000;
    for (;;) {
        try {
            return await attemptRequest(page, endpoint, options);
        } catch (error) {
            if (
                options.method !== "GET" ||
                !(error instanceof NoAnswerError) ||
                Date.now() > deadline
            )
                throw error;
            await new Promise((resolve) => setTimeout(resolve, 250));
        }
    }
}

class NoAnswerError extends Error {}

async function attemptRequest(
    page: Page,
    endpoint: string,
    options: {
        method: string;
        body?: string;
        contentType?: string;
        timeoutMs?: number;
    },
): Promise<IApiResponse> {
    const url = `http://localhost:${bloomPort(page)}/bloom/api/${endpoint}`;
    let response: Response;
    try {
        response = await fetch(url, {
            method: options.method,
            headers: options.contentType
                ? { "Content-Type": options.contentType }
                : undefined,
            body: options.body,
            signal:
                options.timeoutMs === undefined
                    ? undefined
                    : AbortSignal.timeout(options.timeoutMs),
        });
    } catch (error) {
        throw new NoAnswerError(
            `Bloom's ${options.method} /bloom/api/${endpoint} got no answer: ${String(error)}`,
            { cause: error },
        );
    }
    const result = { status: response.status, body: await response.text() };
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
