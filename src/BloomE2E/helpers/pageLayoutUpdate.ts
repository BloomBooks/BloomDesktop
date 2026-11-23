// The page layout update (BookProcessor.UpdatePageLayoutIfNeededThen in Bloom): Bloom brings every
// page of a book up to its current page layout, behind an "Update Book" progress box in the Edit
// tab, and then shows the page it was on again. The All Pages view runs it whenever a page is shown
// in a book that needs it.
//
// The saved book records how far it is up to date in a meta element, pageLayoutUpdateLevel, which
// is missing in a book from an older Bloom. Removing it makes a book look like one of those, which
// is what decides whether Bloom runs the update (BookProcessor.NeedsPageLayoutUpdate).

import * as fs from "node:fs";
import { expect, type Page } from "@playwright/test";
import { bookHtmlPath } from "./bookHtml";
import { holdNextRequest, type IHeldRequest } from "./heldRequest";
import { getPageSizeChoices, setPageSize } from "./pageSize";

// The title of the progress box: BookProcessor.MakeUpdateBookProgressProps.
const kUpdateBookTitle = "Update Book";

// How long the update may take: it loads every page of the book off-screen.
export const kPageLayoutUpdateTimeout = 120_000;

/**
 * The page layout update level the saved book records, "missing" when it records none, or "being
 * saved" in the moment when Bloom has moved the old file aside and not yet put the new one in its
 * place (poll around this).
 */
export function readPageLayoutUpdateLevel(bookFolder: string): string {
    let html: string;
    try {
        html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    } catch (error) {
        if ((error as NodeJS.ErrnoException).code !== "ENOENT") throw error;
        return "being saved";
    }
    const meta = /<meta\b[^>]*\bname="pageLayoutUpdateLevel"[^>]*>/.exec(html);
    const level = meta ? /\bcontent="([^"]*)"/.exec(meta[0]) : null;
    return level ? level[1] : "missing";
}

/**
 * Make the book open in the Edit tab need the page layout update, the way a user does it while
 * Bloom runs: change its page size, which Bloom records by setting the saved level to "0". It picks
 * another size of the same orientation when the book offers one, since changing the orientation
 * also rebuilds the front and back matter. Returns once the saved book records the change.
 * (makeBookLookOlder does the same job for a Bloom that is stopped.)
 */
export async function makeBookNeedPageLayoutUpdate(
    page: Page,
    bookFolder: string,
): Promise<void> {
    const { choices, currentLayoutChoiceId } = await getPageSizeChoices(page);
    const orientation = /(Portrait|Landscape|Square)$/.exec(
        currentLayoutChoiceId,
    )?.[1];
    const others = choices.filter((c) => c.id !== currentLayoutChoiceId);
    const target =
        others.find((c) => orientation && c.id.endsWith(orientation)) ??
        others[0];
    if (!target)
        throw new Error(
            `This book offers no page size other than ${currentLayoutChoiceId}.`,
        );
    await setPageSize(page, target.id);
    await expect
        .poll(() => readPageLayoutUpdateLevel(bookFolder), {
            timeout: 30000,
            message:
                "After a change of page size the saved book should record that it needs the page layout update.",
        })
        .toBe("0");
}

/**
 * Wait until the saved book records that the page layout update has run (a level above 0), for
 * instance after a Publish destination ran it.
 */
export async function waitForPageLayoutUpdateRecorded(
    bookFolder: string,
): Promise<void> {
    await expect
        .poll(() => Number(readPageLayoutUpdateLevel(bookFolder)) > 0, {
            timeout: kPageLayoutUpdateTimeout,
            message:
                "The saved book never recorded that the page layout update had run.",
        })
        .toBe(true);
}

/**
 * Make the saved book look as if an older Bloom had made it, as far as the page layout update goes:
 * remove its pageLayoutUpdateLevel. Only while Bloom is stopped (see bloomApp.restart), since a
 * running Bloom would write the book over this.
 */
export function makeBookLookOlder(bookFolder: string): void {
    const path = bookHtmlPath(bookFolder);
    const html = fs.readFileSync(path, "utf8");
    const changed = html.replace(
        /\s*<meta\b[^>]*\bname="pageLayoutUpdateLevel"[^>]*>/,
        "",
    );
    if (changed === html)
        throw new Error(
            `The book in ${bookFolder} records no pageLayoutUpdateLevel to remove.`,
        );
    fs.writeFileSync(path, changed, "utf8");
}

/**
 * From now on, count each time the Update Book progress box appears in the Edit tab, however
 * briefly. Read the count with getUpdateBookProgressCount(). The shell page is the same for the
 * whole of a Bloom session, so the count lasts until Bloom restarts.
 */
export async function startCountingUpdateBookProgress(
    page: Page,
): Promise<void> {
    await page.evaluate((title) => {
        const w = window as unknown as Record<string, unknown>;
        (
            w.__bloomE2eUpdateBookObserver as MutationObserver | undefined
        )?.disconnect();
        let showing = false;
        w.__bloomE2eUpdateBookCount = 0;
        const check = () => {
            const now = Array.from(
                document.querySelectorAll('[role="dialog"]'),
            ).some((d) => d.textContent?.includes(title));
            if (now && !showing)
                w.__bloomE2eUpdateBookCount =
                    (w.__bloomE2eUpdateBookCount as number) + 1;
            showing = now;
        };
        const observer = new MutationObserver(check);
        observer.observe(document.body, { childList: true, subtree: true });
        w.__bloomE2eUpdateBookObserver = observer;
        check();
    }, kUpdateBookTitle);
}

/** How many times the Update Book progress box has appeared since startCountingUpdateBookProgress(). */
export async function getUpdateBookProgressCount(page: Page): Promise<number> {
    return page.evaluate(
        () =>
            ((window as unknown as Record<string, unknown>)
                .__bloomE2eUpdateBookCount as number | undefined) ?? 0,
    );
}

/** Whether the Update Book progress box is showing in the Edit tab now. */
export async function isUpdateBookProgressShowing(
    page: Page,
): Promise<boolean> {
    return page.evaluate(
        (title) =>
            Array.from(document.querySelectorAll('[role="dialog"]')).some((d) =>
                d.textContent?.includes(title),
            ),
        kUpdateBookTitle,
    );
}

/** Wait until the Update Book progress box is showing. */
export async function waitForUpdateBookProgressToShow(
    page: Page,
): Promise<void> {
    await expect
        .poll(() => isUpdateBookProgressShowing(page), {
            timeout: 60000,
            message: "The Update Book progress box never appeared.",
        })
        .toBe(true);
}

/** Wait until the Update Book progress box has gone. */
export async function waitForUpdateBookProgressToClose(
    page: Page,
): Promise<void> {
    await expect
        .poll(() => isUpdateBookProgressShowing(page), {
            timeout: kPageLayoutUpdateTimeout,
            message: "The Update Book progress box never went away.",
        })
        .toBe(false);
}

/**
 * From now on, hold the next page layout update with its progress box showing, before it changes
 * anything: the box tells Bloom it is ready for progress reports, and Bloom starts the work only
 * then (BrowserProgressDialog.HandleProgressReady), so holding that request holds the update.
 * Bloom counts the update as under way from before the box opens.
 */
export async function holdPageLayoutUpdate(page: Page): Promise<IHeldRequest> {
    return holdNextRequest(page, "**/bloom/api/progress/ready*");
}

/**
 * From now on, hold the All Pages view's next request to Bloom to run the page layout update if
 * the book needs it (editView/updatePageLayoutForAllPages, which every page shown in All Pages
 * makes), so that something the person does next reaches Bloom first.
 */
export async function holdAllPagesRequestForPageLayoutUpdate(
    page: Page,
): Promise<IHeldRequest> {
    return holdNextRequest(
        page,
        "**/bloom/api/editView/updatePageLayoutForAllPages*",
    );
}
