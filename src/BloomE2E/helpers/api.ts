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

/**
 * Close the React dialog (ReactDialog.cs) showing now, such as a problem dialog or a Bloom message
 * box, the way its own Close button does, with common/closeReactDialog, which never submits a
 * report. `page` is any of Bloom's pages, for the port. Fails when Bloom has not answered in 30
 * seconds.
 */
export async function closeReactDialog(page: Page): Promise<void> {
    await request(page, "common/closeReactDialog", {
        method: "POST",
        timeoutMs: 30000,
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

/**
 * The port of the Bloom that serves `page`, from its address. A closed page is refused, as any
 * other use of it would be: a test still holding the shell page that Bloom replaced has lost track
 * of Bloom, even though the replacement serves on the same port.
 */
function bloomPort(page: Page): number {
    if (page.isClosed())
        throw new Error(
            `Cannot call Bloom through a closed page (${page.url()}); use the page that replaced it.`,
        );
    const url = new URL(page.url());
    if (url.hostname !== "localhost" || !url.port)
        throw new Error(
            `Cannot tell which Bloom to call: ${page.url()} is not a page that Bloom serves.`,
        );
    return Number(url.port);
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
    const url = `http://localhost:${bloomPort(page)}/bloom/api/${endpoint}`;
    let result: IApiResponse;
    try {
        const response = await fetch(url, {
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
        result = { status: response.status, body: await response.text() };
    } catch (error) {
        throw new NoAnswerError(
            `Bloom's ${options.method} /bloom/api/${endpoint} got no answer: ${String(error)}`,
            { cause: error },
        );
    }
    if (result.status >= 400)
        throw new Error(
            `Bloom's ${options.method} /bloom/api/${endpoint} returned ${result.status}. ` +
                `Body: ${result.body.slice(0, 500)}`,
        );
    return result;
}

class NoAnswerError extends Error {}

/**
 * Whether `error`, thrown by apiGet or apiPost, says that Bloom closed the connection before it
 * had answered in full, as a Bloom that quits while answering does. A refused connection (no
 * Bloom listening) or a timeout is not that.
 */
export function isDroppedConnection(error: unknown): boolean {
    if (!(error instanceof NoAnswerError)) return false;
    for (let cause = error.cause; cause; cause = (cause as Error).cause) {
        const code = (cause as NodeJS.ErrnoException).code;
        if (code === "ECONNRESET" || code === "UND_ERR_SOCKET") return true;
    }
    return false;
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
