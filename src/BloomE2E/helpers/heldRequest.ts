// Hold one request from Bloom's front end to Bloom until the test lets it go, to decide the order
// in which two things a person did reach Bloom, or to keep Bloom in the moment between two steps.
//
// Bloom's front end talks to Bloom over HTTP, and a person's two quick actions are two requests
// that race. Holding the first one until the second has gone puts them in the order the test wants,
// against the real Bloom, with nothing in Bloom changed. holdCkEditorStartup (ckeditorStartup.ts)
// is the same idea for one file.

import type { Page, Request, Route } from "@playwright/test";

/** What holdNextRequest() returns. */
export interface IHeldRequest {
    /** Resolves with the request once it has been made and is being held. */
    requested: Promise<Request>;
    /** Let the held request go on to Bloom. Call it once, after `requested` has resolved. */
    release: () => Promise<void>;
    /** Stop holding, letting go of a request that is held, if any. Safe to call more than once. */
    stop: () => Promise<void>;
}

/**
 * From now on, hold back the next request whose URL matches `urlPattern` (a Playwright glob, e.g.
 * "**\/bloom/api/progress/ready*"), from any frame of Bloom's window. Later matching requests go
 * through. Call it before the action that makes the request.
 */
export async function holdNextRequest(
    page: Page,
    urlPattern: string,
): Promise<IHeldRequest> {
    let heldRoute: Route | undefined;
    let released = false;
    let stopped = false;
    let sawRequest: (request: Request) => void = () => {};
    const requested = new Promise<Request>((resolve) => {
        sawRequest = resolve;
    });
    const handler = (route: Route) => {
        if (heldRoute || stopped) return route.continue();
        heldRoute = route;
        sawRequest(route.request());
    };
    await page.context().route(urlPattern, handler);
    const stop = async () => {
        if (!stopped) {
            stopped = true;
            await page.context().unroute(urlPattern, handler);
        }
        if (heldRoute && !released) {
            released = true;
            // The frame that made it may be gone by now (a page change replaces the page frame),
            // and then there is nothing left to answer.
            await heldRoute.continue().catch(() => {});
        }
    };
    return {
        requested,
        release: async () => {
            if (!heldRoute)
                throw new Error(
                    `No request to ${urlPattern} has been made, so there is nothing to release.`,
                );
            await stop();
        },
        stop,
    };
}
