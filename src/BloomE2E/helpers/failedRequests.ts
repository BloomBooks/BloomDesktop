// Watch what Bloom's window asks for and fails to get. Under --e2e Bloom does not show the problem
// reports a failed request would raise (NonFatalProblem writes them to stderr and returns), so the
// problem dialog watcher never sees them; the failed responses themselves are what a test can see.

import * as Path from "node:path";
import type { Page, Response } from "@playwright/test";

/**
 * Record every request that failed and named `folder` (for example a book's folder before it was
 * renamed) while `body` runs, and return them, each with the document that asked for it. The
 * listener is removed afterwards, because the page outlives the test.
 */
export async function failedRequestsUnder(
    page: Page,
    folder: string,
    body: () => Promise<void>,
): Promise<string[]> {
    const folderName = Path.basename(folder);
    const failed: string[] = [];
    const listener = (response: Response) => {
        const url = decodeURI(response.url());
        if (response.status() >= 400 && url.includes(folderName)) {
            // Which document asked says what was left on the old folder.
            const frame = response.request().frame();
            failed.push(
                `${response.status()} ${url} (asked for by the '${frame.name()}' frame, ${decodeURI(frame.url())})`,
            );
        }
    };
    page.on("response", listener);
    try {
        await body();
    } finally {
        page.off("response", listener);
    }
    return failed;
}
