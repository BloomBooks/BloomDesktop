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
import {
    launchBloom,
    launchBloomIntoChooser,
    type ILaunchedBloom,
    type ILaunchedChooserBloom,
    type ICollectionSpec,
} from "./launchBloom";
import { chromium } from "@playwright/test";
import {
    describeProblems,
    startProblemDialogWatcher,
    type IProblemDialogWatcher,
} from "./problemDialogWatcher";

/** What every launched Bloom gives a test, whichever mode it started in. */
interface IBloomAppBase {
    /** The current page in the embedded WebView2 this test drives. */
    page: Page;
    /** The port Bloom's HTTP server opened on. */
    httpPort: number;
    /** The port the embedded WebView2 answers CDP on. */
    cdpPort: number;
    /** The process id of the Bloom this test drives. */
    bloomPid: number;
}

/**
 * A Bloom launched on a collection (the normal case). Every field except collectionDir is
 * replaced by restart(), so read them from this object each time rather than copying them into
 * a local.
 */
export interface ICollectionBloomApp extends IBloomAppBase {
    mode: "collection";
    /** The collection folder Bloom has open: a temp folder, never the inputs repository itself. */
    collectionDir: string;
    /**
     * Quit Bloom and start it again on the same collection folder, and return the new shell page.
     *
     * `betweenStopAndStart` runs while no Bloom holds the folder, which is the only safe moment to
     * rewrite the .bloomCollection; use it to change collection settings, which have no API and
     * live in a WinForms dialog CDP cannot reach.
     *
     * A restart invalidates the `page` fixture and the old ports. Take the page this returns, or
     * read bloomApp.page afterwards; the old Page object throws once its target is gone.
     */
    restart: (
        betweenStopAndStart?: () => void | Promise<void>,
    ) => Promise<Page>;
    /**
     * Find Bloom's shell document again after an action that made Bloom rebuild it in the same
     * process — changing the UI language, for example, reopens the whole project, which destroys
     * the WebView2 page and creates a new one on the same ports. Updates bloomApp.page and
     * returns it; the old Page object throws once its target is gone. The caller is responsible
     * for making sure the old shell page has actually closed first (wait for its "close" event),
     * or this can find the outgoing page.
     */
    reattachToShell: () => Promise<Page>;
}

/**
 * A Bloom launched with NO collection, showing the Choose Collection dialog (test.use
 * startAtChooser). `page` is the dialog's document until the test opens a collection.
 *
 * Note the two distinct collection roles: there is no open collection here (nothing like
 * collectionDir), and `collectionToOpen` is the collection card the test can "click" - the
 * argument openCollectionFromChooser posts, exactly what clicking that card in the dialog posts.
 */
export interface IChooserBloomApp extends IBloomAppBase {
    mode: "chooser";
    /** The .bloomCollection file of the collection created for this test to open from the dialog. */
    collectionToOpen: string;
    /**
     * Find the Choose Collection dialog's page again after an action that made Bloom rebuild the
     * dialog - choosing a language there does. Reconnects over CDP (a connection from before the
     * rebuild never sees the new page), updates page, and returns it. The caller makes sure the
     * old page has closed first (wait for its "close" event).
     */
    reattachToChooser: () => Promise<Page>;
    /**
     * Find the workspace shell page after the test leaves the dialog by opening a collection.
     * Reconnects over CDP, updates page, and returns it.
     */
    reattachToShell: () => Promise<Page>;
}

export type IBloomApp = ICollectionBloomApp | IChooserBloomApp;

/** Worker-scoped fixtures: one Bloom per worker. */
interface IBloomWorkerFixtures {
    /**
     * Which prepared folder under <testing-inputs>/collections to open. Set it with test.use().
     * Use it only for a fixture too expensive to build at run time; otherwise set collectionSpec,
     * so the test owns its collection and nobody else's change can move its assumptions.
     */
    collectionName: string | undefined;
    /** A collection to create for this test alone. Set it with test.use(). Preferred. */
    collectionSpec: ICollectionSpec | undefined;
    /**
     * Set with test.use() to launch Bloom with NO collection, at the Choose Collection dialog;
     * the test then uses the chooserApp fixture instead of bloomApp. collectionSpec still names
     * the collection the test can open FROM the dialog (chooserApp.collectionToOpen).
     *
     * CAUTION: reaching the chooser requires an empty MRU list, so this launch mode backs up,
     * edits, and restores the developer's machine-wide user.config - see launchBloomIntoChooser
     * for exactly what is touched and how it is put back.
     */
    startAtChooser: boolean;
    /** The launched Bloom, whichever mode. Internal: tests use bloomApp or chooserApp. */
    _launchedApp: IBloomApp;
    /** The Bloom launched on a collection. Prefer the `page` fixture unless you need more. */
    bloomApp: ICollectionBloomApp;
    /** The Bloom sitting at the Choose Collection dialog (test.use startAtChooser). */
    chooserApp: IChooserBloomApp;
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
export async function connectOverCdpWithRetry(
    cdpPort: number,
): Promise<Browser> {
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
    return findPageByMarker(
        browser,
        (marker) => !!document.querySelector(marker),
        SHELL_MARKER,
        "the workspace shell (the top bar's tab strip)",
    );
}

// How we recognize the Choose Collection dialog's document: a dialog title bar with no
// workspace tab strip (the shell has the tab strip; the problem dialog has neither an h1
// title of this shape nor tabs).
const CHOOSER_MARKER = "#draggable-dialog-title h1";

/**
 * Find the Choose Collection dialog's document among the CDP page targets. Same polling logic
 * as findShellPage; the dialog is rebuilt from scratch when its UI language changes, so this
 * runs against a fresh connection each time (see IChooserBloomApp.reattachToChooser).
 */
async function findChooserPage(browser: Browser): Promise<Page> {
    return findPageByMarker(
        browser,
        (marker) =>
            !!document.querySelector(marker) &&
            !document.querySelector('[role="tablist"]'),
        CHOOSER_MARKER,
        "the Choose Collection dialog",
    );
}

/** The shared scan-and-poll behind findShellPage and findChooserPage. */
async function findPageByMarker(
    browser: Browser,
    matches: (marker: string) => boolean,
    marker: string,
    description: string,
): Promise<Page> {
    const deadline = Date.now() + SHELL_READY_TIMEOUT_MS;
    let lastUrls: string[] = [];
    while (Date.now() < deadline) {
        const pages = browser
            .contexts()
            .flatMap((context) => context.pages())
            .filter((page) => !page.url().startsWith("devtools://"));
        lastUrls = pages.map((page) => page.url());
        for (const page of pages) {
            const found = await page
                .evaluate(matches, marker)
                .catch(() => false);
            if (found) return page;
        }
        await delay(500);
    }
    throw new Error(
        `Bloom's WebView2 never exposed ${description} within ` +
            `${SHELL_READY_TIMEOUT_MS / 1000}s. Targets seen: ${lastUrls.join(", ") || "none"}.`,
    );
}

export const test = base.extend<IBloomTestFixtures, IBloomWorkerFixtures>({
    collectionName: [undefined, { scope: "worker", option: true }],
    collectionSpec: [undefined, { scope: "worker", option: true }],
    startAtChooser: [false, { scope: "worker", option: true }],

    _launchedApp: [
        async ({ collectionName, collectionSpec, startAtChooser }, use) => {
            // Reassigned by restart() and the reattach methods, and read by the teardown below,
            // so the connection we close is always the current one.
            let browser: Browser | undefined;
            // CDP must go to 127.0.0.1: on Windows "localhost" resolves to ::1 first, and
            // WebView2's debugging port does not answer there — you get an empty or wrong
            // target list rather than an error. (Bloom's own HTTP server is the opposite: it
            // rejects a 127.0.0.1 Host header. See helpers/api.ts.) Reconnect rather than reuse
            // after anything that rebuilds the document: a connection from before the rebuild
            // never learns about the new page - its target list just stays empty.
            const reconnectAndFind = async (
                cdpPort: number,
                find: (browser: Browser) => Promise<Page>,
            ): Promise<Page> => {
                await browser?.close();
                browser = await connectOverCdpWithRetry(cdpPort);
                return find(browser);
            };

            if (startAtChooser) {
                if (!collectionSpec || collectionName)
                    throw new Error(
                        "startAtChooser needs collectionSpec (the collection the test will open " +
                            "from the dialog) and cannot be combined with collectionName.",
                    );
                let launched: ILaunchedChooserBloom | undefined;
                try {
                    launched = await launchBloomIntoChooser(collectionSpec);
                    const app: IChooserBloomApp = {
                        mode: "chooser",
                        page: await reconnectAndFind(
                            launched.cdpPort,
                            findChooserPage,
                        ),
                        httpPort: launched.httpPort,
                        cdpPort: launched.cdpPort,
                        bloomPid: launched.bloomPid,
                        collectionToOpen: launched.collectionToOpen,
                        reattachToChooser: async () => {
                            app.page = await reconnectAndFind(
                                launched!.cdpPort,
                                findChooserPage,
                            );
                            return app.page;
                        },
                        reattachToShell: async () => {
                            app.page = await reconnectAndFind(
                                launched!.cdpPort,
                                findShellPage,
                            );
                            return app.page;
                        },
                    };
                    await use(app);
                } finally {
                    // Close the CDP connection first: it keeps a socket into the process we are
                    // about to kill. stop() also restores the developer's user.config.
                    await browser?.close();
                    await launched?.stop();
                }
                return;
            }

            let launched: ILaunchedBloom | undefined;
            try {
                launched = await launchBloom({
                    collectionName,
                    collectionSpec,
                });
                const app: ICollectionBloomApp = {
                    mode: "collection",
                    page: await reconnectAndFind(
                        launched.cdpPort,
                        findShellPage,
                    ),
                    httpPort: launched.httpPort,
                    cdpPort: launched.cdpPort,
                    bloomPid: launched.bloomPid,
                    collectionDir: launched.collectionDir,
                    restart: async (betweenStopAndStart) => {
                        // Close the old CDP connection first: it holds a socket into the process
                        // that is about to be killed.
                        await browser?.close();
                        browser = undefined;
                        await launched!.restart(betweenStopAndStart);
                        app.page = await reconnectAndFind(
                            launched!.cdpPort,
                            findShellPage,
                        );
                        app.httpPort = launched!.httpPort;
                        app.cdpPort = launched!.cdpPort;
                        app.bloomPid = launched!.bloomPid;
                        return app.page;
                    },
                    reattachToShell: async () => {
                        app.page = await reconnectAndFind(
                            launched!.cdpPort,
                            findShellPage,
                        );
                        return app.page;
                    },
                };
                await use(app);
            } finally {
                // Close the CDP connection first: it keeps a socket into the process we are about
                // to kill. Then kill Bloom and delete the temp collection.
                await browser?.close();
                await launched?.stop();
            }
        },
        { scope: "worker" },
    ],

    // The typed views of _launchedApp. Using the one that does not match the launch mode fails
    // immediately with the fix, instead of silently launching a second Bloom.
    bloomApp: [
        async ({ _launchedApp }, use) => {
            if (_launchedApp.mode !== "collection")
                throw new Error(
                    "This file sets startAtChooser, so Bloom is at the Choose Collection " +
                        "dialog: use the chooserApp fixture instead of bloomApp.",
                );
            await use(_launchedApp);
        },
        { scope: "worker" },
    ],

    chooserApp: [
        async ({ _launchedApp }, use) => {
            if (_launchedApp.mode !== "chooser")
                throw new Error(
                    "chooserApp needs test.use({ startAtChooser: true }); this file launched " +
                        "Bloom on a collection, so use bloomApp (or page).",
                );
            await use(_launchedApp);
        },
        { scope: "worker" },
    ],

    problemDialogWatcher: [
        async ({ _launchedApp }, use) => {
            if (!_launchedApp.page.context().browser())
                throw new Error(
                    "The Bloom page has no browser connection, so problem dialogs cannot be watched.",
                );
            // Read the connection through the app's page each scan rather than capturing it here:
            // restart() and the reattach methods replace both, and a captured connection would go
            // quietly deaf.
            const watcher = startProblemDialogWatcher(
                () => _launchedApp.page.context().browser() ?? undefined,
            );
            await use(watcher);
            watcher.stop();
        },
        { scope: "worker" },
    ],

    // Override Playwright's own `page`, so a test that just wants to click things says `page` and
    // never launches a browser of Playwright's own.
    //
    // This is bound once per worker, so a test that calls bloomApp.restart() (or any reattach)
    // must use the page that returns (or the app's .page) from then on: this one points at a
    // dead target.
    page: async ({ _launchedApp }, use) => {
        await use(_launchedApp.page);
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
