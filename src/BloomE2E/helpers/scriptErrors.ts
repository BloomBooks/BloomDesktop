// Uncaught script errors in Bloom's web UI.
//
// When script in the page throws and nothing catches it, Bloom shows a small "A JavaScript error
// occurred" toast and carries on (CommonApi.ReportJavascriptError). The fixture fails a test on a
// "Bloom had a problem" dialog, but a toast stops nothing, so an action can complete, pass its
// assertions, and still have thrown along the way. A test whose subject is "this no longer throws"
// watches for the errors itself, with this.
//
// It watches for Bloom's own reports rather than for Playwright's "pageerror" event, which does not
// fire for an error in the frame of the page being edited. Bloom's error handler
// (lib/errorHandler.ts) posts every uncaught error to common/preliminaryError, and then once more,
// with a source-mapped stack, to common/error; either request is an error seen.

import { type Page, type Request } from "@playwright/test";

/** The API paths Bloom's error handler posts an uncaught error to. */
const ERROR_REPORT_PATHS = [
    "/bloom/api/common/preliminaryError",
    "/bloom/api/common/error",
];

/** A watch on uncaught script errors, started by watchForScriptErrors. */
export interface IScriptErrorWatch {
    /** What Bloom was told about each uncaught error seen so far, in order. */
    errors: () => string[];
    /** Stop watching. */
    stop: () => void;
}

/**
 * Start collecting the uncaught script errors Bloom's UI reports, from the shell document and from
 * every frame inside it (the page being edited, the toolbox, the page list). Call stop() when done.
 * An error usually shows up twice, once from each report Bloom makes of it.
 */
export function watchForScriptErrors(page: Page): IScriptErrorWatch {
    const seen: string[] = [];
    const listener = (request: Request) => {
        const path = new URL(request.url()).pathname;
        if (ERROR_REPORT_PATHS.some((p) => path.startsWith(p)))
            seen.push(request.postData() ?? path);
    };
    page.on("request", listener);
    return {
        errors: () => [...seen],
        stop: () => {
            page.off("request", listener);
        },
    };
}
