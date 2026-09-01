// The fixture every e2e test builds on: `import { test, expect } from "../fixtures/bloomTest"`.
//
// It launches a dedicated Bloom.exe on a temp copy of a test-input collection, attaches to the
// embedded WebView2 over CDP, and hands the test a Playwright `page` pointed at Bloom's own shell
// document. The launch is worker-scoped, so one Bloom serves every test in a file rather than one
// per test (launching takes tens of seconds).
//
// Choose the collection per file with:
//
//     test.use({ collectionName: "basic" });
//
// See README.md for the whole story, including the UI-vs-API policy.

import {
    expect,
    test as base,
    type Browser,
    type Page,
} from "@playwright/test";
import { launchBloom, type ILaunchedBloom } from "./launchBloom";
import { chromium } from "@playwright/test";
import {
    describeProblems,
    startProblemDialogWatcher,
    type IProblemDialogWatcher,
} from "./problemDialogWatcher";

/** What a test gets about the Bloom it is driving. */
export interface IBloomApp {
    /** Bloom's shell document in the embedded WebView2: the top bar, and whatever tab is showing. */
    page: Page;
    /** The port Bloom's HTTP server opened on. */
    httpPort: number;
    /** The port the embedded WebView2 answers CDP on. */
    cdpPort: number;
    /** The process id of the Bloom serving this collection. */
    bloomPid: number;
    /** The temp copy of the collection Bloom has open. Never the inputs repository itself. */
    collectionDir: string;
}

/** Worker-scoped fixtures: one Bloom per worker. */
interface IBloomWorkerFixtures {
    /** Which folder under <testing-inputs>/collections to open. Set it with test.use(). */
    collectionName: string;
    /** The launched Bloom. Prefer the `page` fixture unless you need a port or the folder. */
    bloomApp: IBloomApp;
    problemDialogWatcher: IProblemDialogWatcher;
}

/** Test-scoped fixtures. */
interface IBloomTestFixtures {
    /** Fails the test when Bloom raised a problem dialog while it ran. Runs automatically. */
    failOnBloomProblem: void;
}

// How long we wait for Bloom's WebView2 to expose the shell document after the HTTP server is up.
// The first navigation after launch is slow: WebView2 starts, the bundle loads, and React mounts.
const SHELL_READY_TIMEOUT_MS = 90000;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// How we recognize Bloom's shell document: it is the page holding the top bar's tab strip.
const SHELL_MARKER = '[role="tablist"]';

/**
 * Connect to the WebView2's CDP endpoint, retrying while it comes up. Bloom's HTTP API reports
 * the CDP port (via instanceInfo) as soon as the server is listening, but WebView2 opens its
 * remote-debugging listener slightly later, so a single connect attempt races it and sometimes
 * gets ECONNREFUSED.
 */
async function connectOverCdpWithRetry(cdpPort: number): Promise<Browser> {
    const deadline = Date.now() + SHELL_READY_TIMEOUT_MS;
    let lastError: unknown;
    while (Date.now() < deadline) {
        try {
            return await chromium.connectOverCDP(`http://127.0.0.1:${cdpPort}`);
        } catch (error) {
            lastError = error;
            await delay(500);
        }
    }
    throw new Error(
        `Could not connect to Bloom's WebView2 CDP endpoint on 127.0.0.1:${cdpPort} within ` +
            `${SHELL_READY_TIMEOUT_MS / 1000}s. Last error: ${lastError}`,
    );
}

/**
 * Find Bloom's shell document among the CDP page targets. We identify it by the top bar's tab
 * strip rather than by URL, which also excludes the separately-hosted problem dialog and any
 * DevTools target. Polling matters: the WebView2 target exists, as about:blank, for a second or
 * two before Bloom navigates it to the shell, and the React top bar mounts later still.
 */
async function findShellPage(browser: Browser): Promise<Page> {
    const deadline = Date.now() + SHELL_READY_TIMEOUT_MS;
    let lastUrls: string[] = [];
    while (Date.now() < deadline) {
        const pages = browser
            .contexts()
            .flatMap((context) => context.pages())
            .filter((page) => !page.url().startsWith("devtools://"));
        lastUrls = pages.map((page) => page.url());
        for (const page of pages) {
            const hasTabs = await page
                .evaluate(
                    (marker) => !!document.querySelector(marker),
                    SHELL_MARKER,
                )
                .catch(() => false);
            if (hasTabs) return page;
        }
        await delay(500);
    }
    throw new Error(
        `Bloom's WebView2 never exposed a page containing ${SHELL_MARKER} within ` +
            `${SHELL_READY_TIMEOUT_MS / 1000}s. Targets seen: ${lastUrls.join(", ") || "none"}.`,
    );
}

export const test = base.extend<IBloomTestFixtures, IBloomWorkerFixtures>({
    collectionName: ["basic", { scope: "worker", option: true }],

    bloomApp: [
        async ({ collectionName }, use) => {
            let launched: ILaunchedBloom | undefined;
            let browser: Browser | undefined;
            try {
                launched = await launchBloom({ collectionName });
                // CDP must go to 127.0.0.1: on Windows "localhost" resolves to ::1 first, and
                // WebView2's debugging port does not answer there — you get an empty or wrong
                // target list rather than an error. (Bloom's own HTTP server is the opposite: it
                // rejects a 127.0.0.1 Host header. See helpers/api.ts.)
                browser = await connectOverCdpWithRetry(launched.cdpPort);
                const page = await findShellPage(browser);
                await use({
                    page,
                    httpPort: launched.httpPort,
                    cdpPort: launched.cdpPort,
                    bloomPid: launched.bloomPid,
                    collectionDir: launched.collectionDir,
                });
            } finally {
                // Close the CDP connection first: it keeps a socket into the process we are about
                // to kill. Then kill Bloom and delete the temp collection.
                await browser?.close();
                await launched?.stop();
            }
        },
        { scope: "worker" },
    ],

    problemDialogWatcher: [
        async ({ bloomApp }, use) => {
            const browser = bloomApp.page.context().browser();
            if (!browser)
                throw new Error(
                    "The Bloom page has no browser connection, so problem dialogs cannot be watched.",
                );
            const watcher = startProblemDialogWatcher(browser);
            await use(watcher);
            watcher.stop();
        },
        { scope: "worker" },
    ],

    // Override Playwright's own `page`, so a test that just wants to click things says `page` and
    // never launches a browser of Playwright's own.
    page: async ({ bloomApp }, use) => {
        await use(bloomApp.page);
    },

    failOnBloomProblem: [
        async ({ problemDialogWatcher }, use) => {
            // Discard anything raised before this test started, so one test's problem is not
            // reported against the next.
            problemDialogWatcher.takeProblems();
            await use();
            // One immediate scan, so a dialog raised in the test's last moments is charged to
            // this test rather than lost or blamed on the next one.
            await problemDialogWatcher.scanNow();
            const problems = problemDialogWatcher.takeProblems();
            if (problems.length > 0)
                throw new Error(describeProblems(problems));
        },
        { auto: true },
    ],
});

export { expect };
