// Watch for Bloom's "Bloom had a problem" dialog and turn it into a test failure.
//
// Bloom surfaces errors (including non-fatal ones, which Debug builds raise freely) as a modal
// dialog hosted in its OWN WinForms window with its own WebView2. That makes it a SEPARATE CDP
// page target, not something inside the shell document or the edit-view `page` iframe, so we have
// to scan every page for the `.problem-dialog` root rather than look in one known place.
//
// Two rules this file exists to enforce, adapted from
// .github/skills/bloom-automation/dismissProblemDialog.mjs:
//
//  - We NEVER click Submit, and never POST problemReport/submit. Submitting sends the report, a
//    screenshot, and the book to Bloom's servers, which an automated run must not do. We close
//    the dialog with the same action as its own Close button, POST common/closeReactDialog.
//  - We never dismiss a problem silently. The dialog only shows a "What were you doing?" form;
//    the real exception and stack live behind its "Learn More" link. We click that, scrape the
//    detail, and hand it to the test as the failure message. A recurring problem is a real bug in
//    the code under test, not something to loop-dismiss.
//
// We close the dialog rather than leaving it up because it is modal: leaving it would wedge the
// test until its timeout, and the failure would say "timed out" instead of naming the exception.

import type { Browser, Page } from "@playwright/test";

/** One problem Bloom reported while a test was running. */
export interface IBloomProblem {
    /** The dialog's title and heading, e.g. "Bloom had a problem — Cannot Find File". */
    heading: string;
    /** The concise problem name from the report heading, when it could be read. */
    problem?: string;
    /** The exception and stack scraped from behind "Learn More", when they could be read. */
    detail?: string;
}

/** A running watcher. Poll takeProblems() after each test; call stop() when the worker ends. */
export interface IProblemDialogWatcher {
    /**
     * Return the problems seen since the last call, and forget them. A test that ends with any
     * problem in this list must fail, whatever else it did.
     */
    takeProblems: () => IBloomProblem[];
    /**
     * Scan for a dialog right now, instead of waiting for the next poll. Call this before the
     * final takeProblems of a test, so a dialog raised in the test's last moments is not lost
     * or blamed on the next test.
     */
    scanNow: () => Promise<void>;
    /** Stop polling. */
    stop: () => void;
}

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * Look through every CDP page for one whose DOM contains a .problem-dialog root. Returns the page
 * and its heading, or undefined when no dialog is showing.
 */
async function findProblemDialog(
    browser: Browser,
): Promise<{ page: Page; heading: string } | undefined> {
    for (const page of browser.contexts().flatMap((c) => c.pages())) {
        if (page.url().startsWith("devtools://")) continue;
        const heading = await page
            .evaluate(() => {
                const dialog = document.querySelector(".problem-dialog");
                if (!dialog) return undefined;
                const title = dialog.querySelector(".dialog-title");
                const body = dialog.querySelector(".report-heading");
                return [title?.textContent, body?.textContent]
                    .filter(Boolean)
                    .join(" — ")
                    .replace(/\s+/g, " ")
                    .trim();
            })
            // A page can navigate or close while we are asking; that is not a problem dialog.
            .catch(() => undefined);
        if (heading !== undefined) return { page, heading };
    }
    return undefined;
}

/**
 * Read what actually went wrong, before we close the dialog. The visible body is only the
 * "What were you doing?" form; the exception and stack are behind the "Learn More" link, where
 * Bloom shows exactly what it would send. We click it with Playwright because React's onClick does
 * not fire from an in-page element.click().
 */
async function gatherProblemDetail(
    page: Page,
): Promise<{ problem?: string; detail?: string }> {
    const problem = await page
        .evaluate(() => {
            const h = document.querySelector(".problem-dialog .report-heading");
            return h?.textContent?.replace(/\s+/g, " ").trim() ?? undefined;
        })
        .catch(() => undefined);

    let detail: string | undefined;
    const learnMore = page.getByText(/learn more/i).first();
    if (await learnMore.count()) {
        await learnMore.click({ timeout: 3000 });
        const full = await page
            .locator(".problem-dialog")
            .innerText({ timeout: 3000 })
            .catch(() => undefined);
        if (full) {
            // Trim to the useful part: from "Exception Details" onward when it is there.
            const start = full.search(/exception details/i);
            detail = (start >= 0 ? full.slice(start) : full)
                .replace(/[ \t]+\n/g, "\n")
                .trim()
                .slice(0, 1500);
        }
    }
    return { problem, detail };
}

/**
 * Close the dialog the same way its own Close button does. This does NOT submit the report.
 * The API call goes through the page so the Host header is "localhost" (see helpers/api.ts).
 */
async function closeProblemDialog(page: Page): Promise<void> {
    const status = await page.evaluate(async () => {
        const response = await fetch("/bloom/api/common/closeReactDialog", {
            method: "POST",
        });
        return response.status;
    });
    if (status >= 400)
        throw new Error(
            `Could not close Bloom's problem dialog: common/closeReactDialog returned ${status}.`,
        );
}

/**
 * Start polling for problem dialogs on the given CDP connection. Each dialog found is recorded
 * (with its detail) and then closed, so the test can carry on far enough to fail with a message
 * that names the real exception.
 */
export function startProblemDialogWatcher(
    browser: Browser,
    intervalMs = 750,
): IProblemDialogWatcher {
    const problems: IBloomProblem[] = [];
    let stopped = false;

    // Pages whose dialog we recorded but could not close. The dialog is still showing there, so
    // later scans would find and record the same problem again; skip such a page instead.
    const unclosable = new WeakSet<Page>();

    // One scan: find a dialog, record it, close it. Never throws: a failure to gather the
    // detail or to close still gets recorded as a problem, and the watcher keeps running.
    const checkOnce = async (): Promise<boolean> => {
        const found = await findProblemDialog(browser).catch(() => undefined);
        if (!found) return false;
        if (unclosable.has(found.page)) return false;
        try {
            const { problem, detail } = await gatherProblemDetail(found.page);
            problems.push({ heading: found.heading, problem, detail });
            await closeProblemDialog(found.page);
        } catch (error) {
            unclosable.add(found.page);
            problems.push({
                heading: found.heading,
                problem:
                    `A problem dialog appeared, but gathering its detail or closing it ` +
                    `failed: ${error}`,
            });
        }
        return true;
    };

    // Serialize scans, so the poll loop and scanNow never process the same dialog twice.
    let chain: Promise<unknown> = Promise.resolve();
    const scanExclusively = (): Promise<boolean> => {
        const next = chain.then(checkOnce);
        chain = next.catch(() => undefined);
        return next;
    };

    const poll = async () => {
        while (!stopped) {
            await delay(intervalMs);
            if (stopped) return;
            const foundOne = await scanExclusively().catch(() => false);
            // Give a found dialog time to go away before we look again, so one problem is not
            // counted twice. Bloom queues reports and shows them one at a time, so a second poll
            // may well find the next one, which is what we want.
            if (foundOne) await delay(intervalMs);
        }
    };
    // Deliberately not awaited: this runs for the life of the worker alongside the tests.
    void poll();

    return {
        takeProblems: () => problems.splice(0, problems.length),
        scanNow: async () => {
            await scanExclusively();
        },
        stop: () => {
            stopped = true;
        },
    };
}

/** Render problems as the message of a test failure, one entry per dialog Bloom raised. */
export function describeProblems(problems: IBloomProblem[]): string {
    const lines = problems.map((p) => {
        const name = p.problem || p.heading;
        return p.detail ? `• ${name}\n${p.detail}` : `• ${name}`;
    });
    return (
        `Bloom raised ${problems.length} problem dialog(s) during this test. ` +
        `Fix the underlying error; do not dismiss it.\n${lines.join("\n\n")}`
    );
}
