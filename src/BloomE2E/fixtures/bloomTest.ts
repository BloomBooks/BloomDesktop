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
// Several files can share one launch, which is worth having: launching costs the better part of a
// minute. Playwright keeps a worker, and so the Bloom that worker started, for the next file when
// the worker hash matches, and that hash is built from the IDENTITY of each worker-scoped test.use
// value rather than from its content. So files that pass the same imported object share a Bloom,
// while files that write two equal-looking literals get one each. helpers/flowText.ts's
// kFlowTextCollection is the worked example, and says what a shared Bloom requires of a test.
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
    type ILaunchedBloom,
    type ICollectionSpec,
} from "./launchBloom";
import { chromium } from "@playwright/test";
import * as fs from "node:fs";
import * as os from "node:os";
import * as Path from "node:path";
import {
    describeProblems,
    startProblemDialogWatcher,
    type IProblemDialogWatcher,
} from "./problemDialogWatcher";

/**
 * What a test gets about the Bloom it is driving. Every field except collectionDir is replaced by
 * restart(), so read them from this object each time rather than copying them into a local.
 */
export interface IBloomApp {
    /** Bloom's shell document in the embedded WebView2: the top bar, and whatever tab is showing. */
    page: Page;
    /** The port Bloom's HTTP server opened on. */
    httpPort: number;
    /** The port the embedded WebView2 answers CDP on. */
    cdpPort: number;
    /** The process id of the Bloom serving this collection. */
    bloomPid: number;
    /** The collection folder Bloom has open: a temp folder, never the inputs repository itself. */
    collectionDir: string;
    /**
     * The folder this Bloom keeps its user settings in (its user.config), beside the collection in
     * the temp folder, so nothing it saves reaches the developer's own Bloom or the next run. It
     * starts empty, and a restart keeps it. See helpers/userSettings.ts to read what is in it.
     */
    userSettingsDir: string;
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
}

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
     * Experimental features this Bloom should have on, by their ExperimentalFeatures.cs tokens
     * (e.g. ["team-collections"]). Set it with test.use(). See
     * ILaunchBloomOptions.experimentalFeatures for why this is not done the way a person does it.
     */
    experimentalFeatures: string[] | undefined;
    /** The launched Bloom. Prefer the `page` fixture unless you need a port or the folder. */
    bloomApp: IBloomApp;
    problemDialogWatcher: IProblemDialogWatcher;
}

/** Test-scoped fixtures. */
interface IBloomTestFixtures {
    /** Fails the test when Bloom raised a problem dialog while it ran. Runs automatically. */
    failOnBloomProblem: void;
    /**
     * When a test fails, keeps what explains it beside Playwright's own trace and screenshot: Bloom's
     * log, and a copy of the collection as Bloom left it. Runs automatically.
     */
    keepEvidenceOnFailure: void;
}

/**
 * Where the Bloom under test writes its log. Bloom logs to %TEMP%\SIL\Bloom\Log.txt whatever
 * folder its settings are in, so this is the developer's or the runner's temp folder, and a run's
 * successive Blooms overwrite one another there: what is there when a test fails is the log of the
 * Bloom that was running.
 */
const BLOOM_LOG_PATH = Path.join(os.tmpdir(), "SIL", "Bloom", "Log.txt");

// How long we wait for Bloom's WebView2 to expose the shell document after the HTTP server is up.
// The first navigation after launch is slow: WebView2 starts, the bundle loads, and React mounts.
const SHELL_READY_TIMEOUT_MS = 90000;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// How we recognize a candidate for Bloom's shell document: it is a page holding the top bar, which
// carries this test id (react_components/TopBar/TopBar.tsx).
const SHELL_MARKER = '[data-testid="workspace-top-bar"]';

// Bloom's own answer to "which document are you driving?". The endpoint exists only under --e2e,
// and reports the URL of the shell browser the C# side sends its commands to
// (E2eTestingApi.HandleGetShellUrl).
const SHELL_URL_ENDPOINT = "e2e/shellUrl";

/**
 * The file name part of a shell URL, e.g. "bloom45mgnfsl.htm" from
 * "http://localhost:8095/bloom/C$3A/.../bloom45mgnfsl.htm?x=1". Bloom names each shell document
 * after a temp file, so the file name identifies the document while the query string does not:
 * the workspace rewrites its own query as the user moves around (updateWorkspaceUrlParam).
 */
function shellDocumentName(url: string): string {
    const withoutQuery = url.split(/[?#]/)[0];
    return withoutQuery
        .substring(withoutQuery.lastIndexOf("/") + 1)
        .toLowerCase();
}

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
 * Find the shell document Bloom is actually driving, among the CDP page targets.
 *
 * Two tests are applied, and both are needed. The top bar's test id finds the candidates, which
 * also excludes the separately-hosted problem dialog and any DevTools target; then Bloom itself is
 * asked which document it drives, and only that one is returned. The marker alone is not enough:
 * a run can expose more than one workspace-root document, and attaching to an undriven one costs
 * an hour, because the test's own clicks work while nothing Bloom loads ever appears (see
 * AUTOMATION-DEBT.md). The hook alone is not enough either: it answers "" until the workspace
 * view has built its browser, and an older Bloom.exe in output/Debug does not have the endpoint at
 * all, so the marker match stays as the fallback for a hook that never answers.
 *
 * Polling matters: the WebView2 target exists, as about:blank, for a second or two before Bloom
 * navigates it to the shell, and the React top bar mounts later still.
 */
async function findShellPage(
    browser: Browser,
    httpPort: number,
): Promise<Page> {
    const deadline = Date.now() + SHELL_READY_TIMEOUT_MS;
    let lastUrls: string[] = [];
    let markerOnlyMatch: Page | undefined;
    let hookEverAnswered = false;
    while (Date.now() < deadline) {
        const pages = browser
            .contexts()
            .flatMap((context) => context.pages())
            .filter((page) => !page.url().startsWith("devtools://"))
            // Keep only this Bloom's own documents. Another Bloom (another worktree, or the
            // developer's) can answer on this CDP port, and its pages carry the same marker and
            // answer the same hook, so a page from a different HTTP port must not win.
            .filter(
                (page) =>
                    !page.url().startsWith("http") ||
                    page.url().includes(`:${httpPort}/`),
            );
        lastUrls = pages.map((page) => page.url());
        for (const page of pages) {
            const hasTopBar = await page
                .evaluate(
                    (marker) => !!document.querySelector(marker),
                    SHELL_MARKER,
                )
                .catch(() => false);
            if (!hasTopBar) continue;
            if (!markerOnlyMatch) markerOnlyMatch = page;
            const drivenUrl = await page
                .evaluate(async (endpoint) => {
                    const response = await fetch(`/bloom/api/${endpoint}`);
                    return response.ok ? await response.text() : "";
                }, SHELL_URL_ENDPOINT)
                .catch(() => "");
            if (!drivenUrl) continue;
            hookEverAnswered = true;
            if (shellDocumentName(drivenUrl) === shellDocumentName(page.url()))
                return page;
        }
        await delay(500);
    }
    // Re-check it before handing it back. It was found on some earlier turn of the loop, possibly
    // ninety seconds ago, and Bloom navigates the shell target while it starts up, so by now the
    // page may be gone.
    if (markerOnlyMatch) {
        const stillThere = await markerOnlyMatch
            .evaluate(
                (marker) => !!document.querySelector(marker),
                SHELL_MARKER,
            )
            .catch(() => false);
        if (!stillThere) markerOnlyMatch = undefined;
    }
    if (markerOnlyMatch && !hookEverAnswered) {
        console.warn(
            `Bloom never answered ${SHELL_URL_ENDPOINT}, so the shell document was chosen by ` +
                `${SHELL_MARKER} alone. If this test fails oddly, check that output/Debug holds a ` +
                `Bloom.exe new enough to have that endpoint.`,
        );
        return markerOnlyMatch;
    }
    throw new Error(
        `Bloom's WebView2 never exposed the shell document it is driving within ` +
            `${SHELL_READY_TIMEOUT_MS / 1000}s. Pages carrying ${SHELL_MARKER} were ` +
            `${markerOnlyMatch ? "found" : "not found"}; ${SHELL_URL_ENDPOINT} ` +
            `${hookEverAnswered ? "answered, but named a different document" : "never answered"}. ` +
            `Targets seen: ${lastUrls.join(", ") || "none"}.`,
    );
}

export const test = base.extend<IBloomTestFixtures, IBloomWorkerFixtures>({
    collectionName: [undefined, { scope: "worker", option: true }],
    collectionSpec: [undefined, { scope: "worker", option: true }],
    experimentalFeatures: [undefined, { scope: "worker", option: true }],

    bloomApp: [
        async (
            { collectionName, collectionSpec, experimentalFeatures },
            use,
        ) => {
            let launched: ILaunchedBloom | undefined;
            // Reassigned by restart(), and read by the teardown below, so the connection we close
            // is always the current one.
            let browser: Browser | undefined;
            try {
                launched = await launchBloom({
                    collectionName,
                    collectionSpec,
                    experimentalFeatures,
                });
                // CDP must go to 127.0.0.1: on Windows "localhost" resolves to ::1 first, and
                // WebView2's debugging port does not answer there — you get an empty or wrong
                // target list rather than an error. (Bloom's own HTTP server is the opposite: it
                // rejects a 127.0.0.1 Host header. See helpers/api.ts.)
                browser = await connectOverCdpWithRetry(launched.cdpPort);
                const bloomApp: IBloomApp = {
                    page: await findShellPage(browser, launched.httpPort),
                    httpPort: launched.httpPort,
                    cdpPort: launched.cdpPort,
                    bloomPid: launched.bloomPid,
                    collectionDir: launched.collectionDir,
                    userSettingsDir: launched.userSettingsDir,
                    restart: async (betweenStopAndStart) => {
                        // Close the old CDP connection first: it holds a socket into the process
                        // that is about to be killed.
                        await browser?.close();
                        browser = undefined;
                        await launched!.restart(betweenStopAndStart);
                        browser = await connectOverCdpWithRetry(
                            launched!.cdpPort,
                        );
                        // Resolve the shell again: the restarted Bloom has a new shell document,
                        // and the old page object points at a dead target.
                        bloomApp.page = await findShellPage(
                            browser,
                            launched!.httpPort,
                        );
                        bloomApp.httpPort = launched!.httpPort;
                        bloomApp.cdpPort = launched!.cdpPort;
                        bloomApp.bloomPid = launched!.bloomPid;
                        return bloomApp.page;
                    },
                };
                await use(bloomApp);
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
            if (!bloomApp.page.context().browser())
                throw new Error(
                    "The Bloom page has no browser connection, so problem dialogs cannot be watched.",
                );
            // Read the connection through bloomApp.page each scan rather than capturing it here:
            // restart() replaces both, and a captured connection would go quietly deaf.
            const watcher = startProblemDialogWatcher(
                () => bloomApp.page.context().browser() ?? undefined,
            );
            await use(watcher);
            watcher.stop();
        },
        { scope: "worker" },
    ],

    // Override Playwright's own `page`, so a test that just wants to click things says `page` and
    // never launches a browser of Playwright's own.
    //
    // This is test-scoped, so each test reads whatever bloomApp.page is when it starts, and a test
    // after a restart — in this file or in a later one sharing the worker — gets the live page.
    // Within the test that calls bloomApp.restart(), though, this `page` points at a dead target
    // from the restart onwards: use the page restart() returns, or bloomApp.page.
    page: async ({ bloomApp }, use) => {
        await use(bloomApp.page);
    },

    // Torn down after every other test-scoped fixture (failOnBloomProblem depends on it, so it is
    // set up first), so a failure raised by one of them is still seen here. Everything goes under
    // testInfo.outputDir, which is test-results/<test>/, the folder CI already uploads on failure.
    //
    // The collection folder is copied rather than described because the failures this is for are
    // the ones where Bloom's state on disk disagrees with what the test saw: a title typed on the
    // cover that the collection never learned (AUTOMATION-DEBT.md). The book's HTML and meta.json
    // say which side lost it.
    //
    // The user-settings folder is deliberately NOT kept. These artifacts are public (the repository
    // is), and user.config is where a Bloom that signed in to Bloom Library for real
    // (helpers/bloomLibraryAccount.ts) saves its session token and account, and where the next
    // secret-shaped setting would land too. Redacting known names would protect only against the
    // ones we thought of. helpers/userSettings.ts reads that file for a test while it runs instead.
    keepEvidenceOnFailure: [
        async ({ bloomApp }, use, testInfo) => {
            await use();
            if (testInfo.status === testInfo.expectedStatus) return;
            // Bloom may still be writing; a moment lets its last save land in the copy.
            await delay(1000);
            // Best effort throughout: this runs on a test that has already failed, and a file
            // Bloom still holds open must cost only that file, never the rest of the evidence or
            // a second error on top of the real one. Each step is contained on its own, and the
            // copy filter really opens each file, because on Windows accessSync checks attributes,
            // not whether another process has the file locked.
            try {
                if (fs.existsSync(BLOOM_LOG_PATH))
                    await testInfo.attach("bloom-log", {
                        body: fs.readFileSync(BLOOM_LOG_PATH),
                        contentType: "text/plain",
                    });
            } catch (error) {
                console.warn(`Could not keep Bloom's log: ${error}`);
            }
            try {
                fs.cpSync(
                    bloomApp.collectionDir,
                    testInfo.outputPath("collection"),
                    {
                        recursive: true,
                        errorOnExist: false,
                        filter: (source) => {
                            if (fs.statSync(source).isDirectory()) return true;
                            try {
                                fs.closeSync(fs.openSync(source, "r"));
                                return true;
                            } catch {
                                console.warn(
                                    `Left out of the collection copy (locked?): ${source}`,
                                );
                                return false;
                            }
                        },
                    },
                );
            } catch (error) {
                console.warn(`Could not copy the collection folder: ${error}`);
            }
        },
        { auto: true },
    ],

    failOnBloomProblem: [
        async ({ problemDialogWatcher, keepEvidenceOnFailure }, use) => {
            void keepEvidenceOnFailure;
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
