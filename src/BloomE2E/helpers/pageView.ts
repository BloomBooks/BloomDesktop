// Drive and read the view in the Edit tab's page frame: the page view chooser in the top bar (One
// Page, or All Pages, which lays the rest of the book out in spreads around the page being edited;
// see bookGridView.ts in BloomBrowserUI), and where a page is in the frame's scrolled view.
//
// Scrolling goes through the page frame's own window: that frame's document is what scrolls, and
// the Edit tab around it never does.

import { expect, type Page } from "@playwright/test";
import { editablePageFrame, getPages } from "./bookMaking";

/** Where something is on screen, in the page frame's viewport. */
export interface IViewRect {
    left: number;
    top: number;
    right: number;
    bottom: number;
}

/** A page's box, and the part of the page frame's viewport not covered by the bar of controls across its top. */
export interface IPagePlacement {
    page: IViewRect;
    visible: IViewRect;
}

/**
 * Click one of the page view chooser's two segments: one page at a time, or all pages. With All
 * Pages, returns once every page of the book has its place in the page frame; with One Page, once
 * none has.
 */
export async function choosePageView(
    page: Page,
    view: "one" | "all",
): Promise<void> {
    await page
        .getByTestId(view === "all" ? "view-all-pages" : "view-one-page")
        .click();
    await expect(
        editablePageFrame(page).locator(".bloom-book-grid-cell"),
        `The page frame never showed ${view === "all" ? "all the pages" : "one page"}.`,
    ).toHaveCount(view === "all" ? (await getPages(page)).length : 0, {
        timeout: 30000,
    });
}

/**
 * Where the page with this id is in the page frame's view: the page being edited itself, or, with
 * All Pages, the place of another page.
 */
export async function getPagePlacement(
    page: Page,
    pageId: string,
): Promise<IPagePlacement> {
    return editablePageFrame(page).evaluate((id) => {
        const edited = document.querySelector(".bloom-page") as HTMLElement;
        const box =
            edited.id === id
                ? edited
                : document.querySelector(
                      `.bloom-book-grid-cell[data-page-id="${id}"]`,
                  );
        if (!box)
            throw new Error(
                `The page frame is not showing page ${id}. It is editing ${edited.id}` +
                    (document.querySelector(".bloom-book-grid-cell")
                        ? ", with all the pages showing."
                        : ", with only that page showing."),
            );
        const r = box.getBoundingClientRect();
        const bar = document.querySelector("body > .bloom-controls-bar");
        return {
            page: {
                left: r.left,
                top: r.top,
                right: r.right,
                bottom: r.bottom,
            },
            visible: {
                left: 0,
                top: bar ? bar.getBoundingClientRect().bottom : 0,
                right: document.documentElement.clientWidth,
                bottom: document.documentElement.clientHeight,
            },
        };
    }, pageId);
}

/** True when every part of the page is inside the visible part of the view. */
export function isWhollyInView(placement: IPagePlacement): boolean {
    const { page: p, visible: v } = placement;
    return (
        p.left >= v.left - 1 &&
        p.top >= v.top - 1 &&
        p.right <= v.right + 1 &&
        p.bottom <= v.bottom + 1
    );
}

/** True when no part of the page is inside the visible part of the view. */
export function isWhollyOutOfView(placement: IPagePlacement): boolean {
    const { page: p, visible: v } = placement;
    return (
        p.right <= v.left ||
        p.left >= v.right ||
        p.bottom <= v.top ||
        p.top >= v.bottom
    );
}

/**
 * Scroll the page frame as far from the page with this id as it goes (to the top or the bottom of
 * the book, whichever is further), and check the page is then entirely out of view. Throws, saying
 * where the page ended up, when the book is too short for that.
 */
export async function scrollPageOutOfView(
    page: Page,
    pageId: string,
): Promise<void> {
    const before = await getPagePlacement(page, pageId);
    const pageMiddle = (before.page.top + before.page.bottom) / 2;
    const viewMiddle = (before.visible.top + before.visible.bottom) / 2;
    await editablePageFrame(page).evaluate((toTheBottom) => {
        window.scrollTo(
            window.scrollX,
            toTheBottom ? document.documentElement.scrollHeight : 0,
        );
    }, pageMiddle <= viewMiddle);
    const after = await getPagePlacement(page, pageId);
    if (!isWhollyOutOfView(after))
        throw new Error(
            `The page frame cannot scroll page ${pageId} out of view: at the far end it runs ` +
                `from y=${after.page.top} to y=${after.page.bottom}, and the view from ` +
                `y=${after.visible.top} to y=${after.visible.bottom}. The book needs more pages ` +
                `(or a larger zoom).`,
        );
}

/**
 * Scroll the page frame so the top of the page with this id is `pixels` above the top of the
 * visible part of the view (below the bar of controls), leaving the rest of it on screen. Returns
 * where the page then is.
 */
export async function scrollPageTopAboveView(
    page: Page,
    pageId: string,
    pixels: number,
): Promise<IPagePlacement> {
    const before = await getPagePlacement(page, pageId);
    const dy = before.page.top - (before.visible.top - pixels);
    await editablePageFrame(page).evaluate((dy) => window.scrollBy(0, dy), dy);
    const after = await getPagePlacement(page, pageId);
    if (Math.abs(after.page.top - (after.visible.top - pixels)) > 1)
        throw new Error(
            `The page frame could not scroll page ${pageId} to ${pixels}px above the view: its top ` +
                `is at y=${after.page.top}, and the view starts at y=${after.visible.top}.`,
        );
    return after;
}

/**
 * With All Pages showing, click another page with the mouse, as a user does to edit it: across the
 * middle of it, `yInView` pixels below the top of the visible part of the view. Throws if that
 * point is not on the page.
 */
export async function clickPageInGrid(
    page: Page,
    pageId: string,
    yInView: number,
): Promise<void> {
    const placement = await getPagePlacement(page, pageId);
    const x = (placement.page.left + placement.page.right) / 2;
    const y = placement.visible.top + yInView;
    if (y < placement.page.top || y > placement.page.bottom)
        throw new Error(
            `The point ${yInView}px down the view (y=${y}) is not on page ${pageId}, which runs ` +
                `from y=${placement.page.top} to y=${placement.page.bottom}.`,
        );
    const frameBox = await page.locator("#page").boundingBox();
    if (!frameBox) throw new Error("The page frame has no box on screen.");
    await page.mouse.click(frameBox.x + x, frameBox.y + y);
}

/**
 * Wait until the page being edited has finished dealing with the click that opened it (when it
 * was one of the other pages, Bloom replays that click on it; see
 * replayTheClickThatOpenedThisPage in bookGridView.ts).
 */
export async function waitForClickedPageToSettle(page: Page): Promise<void> {
    await expect
        .poll(
            () =>
                editablePageFrame(page)
                    .evaluate(() =>
                        document.documentElement.classList.contains(
                            "bloom-book-grid-opening-click-done",
                        ),
                    )
                    .catch(() => false),
            {
                timeout: 30000,
                message:
                    "The page being edited never finished dealing with the click that opened it.",
            },
        )
        .toBe(true);
}

/**
 * True while the Edit tab is changing pages: from a click on another page until the new page
 * shows (the wait cursor covers the page frame, and the page being left may still be on screen).
 */
export async function isPageChangeUnderway(page: Page): Promise<boolean> {
    return page.evaluate(
        () =>
            !!document.getElementById("page-loading-cover") ||
            !!document.getElementById("page-outgoing"),
    );
}
