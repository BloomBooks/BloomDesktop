// Drive the page list: the strip of page thumbnails down the left of the Edit tab, and the
// Add Page / Duplicate / Delete controls under it.
//
// The list lives in its own iframe (id "pageList"), so every locator here starts from that frame.
// Its right-click menu is the one exception: the list portals the menu into the shell document so
// the narrow sidebar cannot clip it, which is why the menu is found on `page` itself.
//
// Page ORDER is asked of Bloom (getPages), not read off the thumbnails: the thumbnails only exist
// once they scroll into view, and reordering is a WinForms-side save that finishes after the
// drag ends, so polling Bloom is the only way to know when the move has really happened.

import { expect, type Frame, type Locator, type Page } from "@playwright/test";
import { apiPost } from "./api";
import {
    getPages,
    getShownPageId,
    waitForEditablePage,
    type IBookPage,
} from "./bookMaking";
import { runPageMenuCommand, selectPage } from "./pageThumbnails";

/** The Edit tab's frame holding the page thumbnails. Throws if the Edit tab is not showing. */
export function pageListFrame(page: Page): Frame {
    const frame = page.frame({ name: "pageList" });
    if (!frame)
        throw new Error(
            "There is no 'pageList' frame, so Bloom is not showing the Edit tab. " +
                `Frames: ${page
                    .frames()
                    .map((f) => f.name() || "(main)")
                    .join(", ")}.`,
        );
    return frame;
}

/** The thumbnail of one page. Every thumbnail carries the page's id, and the cover over it takes the clicks. */
function thumbnail(page: Page, pageId: string) {
    return pageListFrame(page).locator(
        `.gridItem[id="${pageId}"] .invisibleThumbnailCover`,
    );
}

/**
 * Wait until the book has one more page than `before` listed, and return the new page. Every
 * way of duplicating a page ends here, so the test learns which page is the copy.
 */
async function waitForOneNewPage(
    page: Page,
    before: IBookPage[],
    action: string,
): Promise<IBookPage> {
    let added: IBookPage | undefined;
    await expect
        .poll(
            async () => {
                const after = await getPages(page);
                added = after.find((p) => !before.some((b) => b.id === p.id));
                return after.length;
            },
            {
                timeout: 60000,
                message: `Bloom never added a page after ${action}.`,
            },
        )
        .toBe(before.length + 1);
    await waitForEditablePage(page);
    return added!;
}

/**
 * Duplicate the page Bloom is showing by clicking the Duplicate button under the page list, and
 * return the new page. The copy lands right after the page it was made from.
 */
export async function duplicatePageWithButton(page: Page): Promise<IBookPage> {
    const before = await getPages(page);
    const button = pageListFrame(page).getByTestId("duplicate-page-button");
    await button.waitFor({ state: "visible", timeout: 30000 });
    await expect(
        button,
        "The Duplicate button is disabled, so this page cannot be duplicated.",
    ).toBeEnabled({ timeout: 30000 });
    await button.click();
    return waitForOneNewPage(page, before, "clicking the Duplicate button");
}

/**
 * Duplicate a page by right-clicking its thumbnail and choosing Duplicate Page from the menu,
 * and return the new page.
 */
export async function duplicatePageWithContextMenu(
    page: Page,
    pageId: string,
): Promise<IBookPage> {
    const before = await getPages(page);
    const target = thumbnail(page, pageId);
    await target.waitFor({ state: "visible", timeout: 30000 });
    await target.click({ button: "right" });
    // The menu is portaled into the shell document, not the page list's own frame.
    const item = page.getByRole("menuitem", {
        name: "Duplicate Page",
        exact: true,
    });
    await item.waitFor({ state: "visible", timeout: 30000 });
    await expect(
        item,
        "Duplicate Page is disabled in the page's right-click menu.",
    ).toBeEnabled();
    await item.click();
    return waitForOneNewPage(
        page,
        before,
        "choosing Duplicate Page from the menu",
    );
}

/**
 * Move a page by dragging its thumbnail onto another page's slot, the way a person reorders
 * pages, and wait until Bloom lists the moved page in that slot.
 *
 * Both pages must be content pages: Bloom refuses to move front or back matter with a WinForms
 * message box, which a test cannot dismiss.
 */
export async function movePageToSlotOf(
    page: Page,
    pageId: string,
    targetPageId: string,
): Promise<void> {
    const before = await getPages(page);
    const targetIndex = before.findIndex((p) => p.id === targetPageId);
    if (targetIndex < 0 || !before.some((p) => p.id === pageId))
        throw new Error(
            `Cannot move page ${pageId} to the slot of ${targetPageId}: the book's pages are ` +
                before.map((p) => p.id).join(", "),
        );
    for (const id of [pageId, targetPageId]) {
        const info = before.find((p) => p.id === id)!;
        if (!info.isContentPage)
            throw new Error(
                `Page ${id} (${info.caption}) is front or back matter, which Bloom does not let ` +
                    `a drag move. Move content pages only.`,
            );
    }

    const source = thumbnail(page, pageId);
    const target = thumbnail(page, targetPageId);
    await source.waitFor({ state: "visible", timeout: 30000 });
    await target.waitFor({ state: "visible", timeout: 30000 });
    // In a small window the page list scrolls, and a thumbnail Playwright calls visible may be
    // only partly on screen. A person scrolls until both pages show, so we do too: the target
    // first, then the source, which scrolls no further than it must. The drag presses and
    // releases at the middle of each thumbnail, so that is what has to be on screen.
    await target.scrollIntoViewIfNeeded();
    await source.scrollIntoViewIfNeeded();
    for (const [name, thumb] of [
        ["dragged page", source],
        ["target page", target],
    ] as const) {
        // Empty when the middle of the thumbnail is on screen; otherwise says what hides it.
        const hiddenBy = await thumb.evaluate((element) => {
            const box = element.getBoundingClientRect();
            const middle = (box.top + box.bottom) / 2;
            const span = `its middle is at ${Math.round(middle)}`;
            for (
                let ancestor = element.parentElement;
                ancestor;
                ancestor = ancestor.parentElement
            ) {
                const overflow = getComputedStyle(ancestor).overflowY;
                if (overflow !== "auto" && overflow !== "scroll") continue;
                const area = ancestor.getBoundingClientRect();
                if (middle < area.top || middle > area.bottom)
                    return `${span}, but its scrolling ancestor <${ancestor.tagName.toLowerCase()} id="${ancestor.id}" class="${ancestor.className}"> shows only ${Math.round(area.top)}-${Math.round(area.bottom)}`;
            }
            if (middle < 0 || middle > window.innerHeight)
                return `${span}, but the page list is only ${window.innerHeight} high`;
            return "";
        });
        if (hiddenBy)
            throw new Error(
                `The ${name} is not on screen in the page list even after scrolling, so it cannot be dragged as a person would (${hiddenBy}). Choose pages that are closer together.`,
            );
    }
    const from = (await source.boundingBox())!;
    const to = (await target.boundingBox())!;
    const startX = from.x + from.width / 2;
    const startY = from.y + from.height / 2;
    const endX = to.x + to.width / 2;
    const endY = to.y + to.height / 2;
    // A real drag: press, travel in steps so the grid sees the movement, release over the target.
    await page.mouse.move(startX, startY);
    await page.mouse.down();
    await page.mouse.move(startX + 5, startY + 5, { steps: 2 });
    await page.mouse.move(endX, endY, { steps: 15 });
    await page.mouse.up();

    await expect
        .poll(async () => (await getPages(page))[targetIndex]?.id, {
            timeout: 60000,
            message: `Bloom never listed page ${pageId} in the slot of ${targetPageId}.`,
        })
        .toBe(pageId);
    await waitForEditablePage(page);
}

/**
 * Duplicate the page Bloom is showing, `times` times, through the API, and wait for the new pages
 * to appear. This is the SETUP route: it posts what the "Duplicate Page Many Times" dialog posts,
 * so a test can give a book more content pages without driving any UI. duplicatePageWithButton and
 * duplicatePageWithContextMenu are the routes a test drives when duplicating IS the action under
 * test.
 */
export async function duplicateCurrentPage(
    page: Page,
    times = 1,
): Promise<void> {
    const before = (await getPages(page)).length;
    // Bloom duplicates only the page the request names, and only if it is still the current page.
    const pageId = await getShownPageId(page);
    if (!pageId)
        throw new Error(
            "duplicateCurrentPage: the Edit tab is not showing a page.",
        );
    await apiPost(
        page,
        "editView/duplicatePageMany",
        JSON.stringify({ numberOfTimes: times, pageId }),
        "application/json",
    );
    await expect
        .poll(async () => (await getPages(page)).length, {
            timeout: 60000,
            message: "Bloom never added the duplicated page(s).",
        })
        .toBe(before + times);
    await waitForEditablePage(page);
}

/**
 * Open "Duplicate Page Many Times..." on a page from its menu, the way a person does, and return
 * the dialog. The dialog opens inside the Edit tab (duplicateManyDialog.tsx), in the shell
 * document. Finish with finishDuplicatePageManyTimes or cancelDuplicatePageManyTimes.
 */
export async function openDuplicatePageManyTimes(
    page: Page,
    pageId: string,
): Promise<Locator> {
    await selectPage(page, pageId);
    await runPageMenuCommand(page, pageId, "Duplicate Page Many Times...");
    const dialog = page.getByRole("dialog");
    await dialog.waitFor({ state: "visible", timeout: 30000 });
    return dialog;
}

/**
 * In the open Duplicate Page Many Times dialog, ask for `times` copies and press OK. Waits until
 * Bloom lists that many more pages and the dialog has closed.
 */
export async function finishDuplicatePageManyTimes(
    page: Page,
    dialog: Locator,
    times: number,
): Promise<void> {
    const before = (await getPages(page)).length;
    // The number box is the dialog's one text input (SmallNumberPicker).
    await dialog.locator("input").fill(String(times));
    await dialog.getByTestId("dialog-ok").click();
    await expect(dialog).toBeHidden({ timeout: 30000 });
    await expect
        .poll(async () => (await getPages(page)).length, {
            timeout: 60000,
            message: `Bloom never had ${times} more pages after Duplicate Page Many Times.`,
        })
        .toBe(before + times);
    await waitForEditablePage(page);
}

/** Close the open Duplicate Page Many Times dialog with Cancel, and wait for it to go away. */
export async function cancelDuplicatePageManyTimes(
    dialog: Locator,
): Promise<void> {
    await dialog.getByTestId("dialog-cancel").click();
    await expect(dialog).toBeHidden({ timeout: 30000 });
}
