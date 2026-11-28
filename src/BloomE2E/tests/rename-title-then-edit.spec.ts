// Renaming a book's title and then going on editing it: clicking page thumbnails, adding a Sign
// Language video page and recording into it. Typing a new title on the cover renames the book's
// folder when the cover is left, and Bloom reloads the page list for the renamed book. Once, the
// page clicked just after that jumped to the top of the page list instead of staying where the
// person clicked it (BL-9140). So each click here is on a thumbnail lower down the list, and the
// test checks that it is still where it was once Bloom shows its page.
//
// Test Case ID 397 ("Rename Title Then Edit"), from the Dokimion case TC272: in a derivative of
// the "A Family Learns about Immunisations" shell, under a subscription, replace the cover title
// with "Test Book", click Page 9 and then Page 4, add a "Just Video" page from the Add Page dialog,
// record a video into it with the Sign Language tool, then click Page 13 and the video page. The
// camera is Chromium's fake one (test.use({ fakeCamera: true })).

import type { Page } from "@playwright/test";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import { resetViewportSize, setViewportSize } from "../fixtures/viewport";
import { addPageFromDialog, openAddPageDialog } from "../helpers/addPageDialog";
import {
    findBookFolder,
    getContentPages,
    getPages,
    getShownPageId,
    makeBookFromSourceBook,
    typeInGroup,
    waitForEditTabSettled,
    type IBookPage,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { pageListFrame } from "../helpers/pageList";
import { selectPage } from "../helpers/pageThumbnails";
import { recordVideo, videoBoxes } from "../helpers/videos";

test.use({
    collectionSpec: {
        name: "rename-title-then-edit",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
    fakeCamera: true,
});

const kShell = "A Family Learns about Immunisations";
const kNewTitle = "Test Book";

/** Where a page's thumbnail is in the page list, and whether the list can scroll at all. */
interface IThumbnailPlace {
    /** The thumbnail's top, in pixels below the top of the list's visible area. */
    top: number;
    /** How far the list is scrolled. */
    scrollTop: number;
    /** The height of the list's visible area. */
    visibleHeight: number;
    /** True when the list is longer than its visible area, so that it can scroll. */
    canScroll: boolean;
}

/**
 * Where the page's thumbnail is in the page list. With `scrollToBottom`, the list first scrolls
 * just far enough that the thumbnail is the last one in view, which leaves it as far as it can be
 * from the top of the list's visible area.
 */
async function thumbnailPlace(
    page: Page,
    pageId: string,
    scrollToBottom = false,
): Promise<IThumbnailPlace> {
    return pageListFrame(page).evaluate(
        ({ id, scroll }) => {
            const list = document.getElementById("pageGridWrapper");
            const thumbnail = document.querySelector(`.gridItem[id="${id}"]`);
            if (!list || !thumbnail)
                throw new Error(
                    `The page list has no ${list ? `thumbnail for page ${id}` : "#pageGridWrapper"}.`,
                );
            if (scroll) thumbnail.scrollIntoView({ block: "end" });
            return {
                top: Math.round(
                    thumbnail.getBoundingClientRect().top -
                        list.getBoundingClientRect().top,
                ),
                scrollTop: Math.round(list.scrollTop),
                visibleHeight: list.clientHeight,
                canScroll: list.scrollHeight > list.clientHeight + 1,
            };
        },
        { id: pageId, scroll: scrollToBottom },
    );
}

/** True when the page list is longer than its visible area, so that it can scroll. */
async function pageListCanScroll(page: Page): Promise<boolean> {
    return pageListFrame(page).evaluate(() => {
        const list = document.getElementById("pageGridWrapper");
        if (!list) throw new Error("The page list has no #pageGridWrapper.");
        return list.scrollHeight > list.clientHeight + 1;
    });
}

// The page area of the nightly CI runner, where this book's page list is too long to show at once.
const kShortPageArea = { width: 1008, height: 681 };

// Set when the test shrank the page area, so that it is put back for whatever runs next.
let shrankPageArea = false;

test.afterEach(async ({ page }) => {
    if (shrankPageArea) await resetViewportSize(page);
    shrankPageArea = false;
});

/** Page `n` of the book, as the case counts them: its nth content page, after the front matter. */
async function pageNumbered(page: Page, n: number): Promise<IBookPage> {
    const contentPages = await getContentPages(page);
    if (contentPages.length < n)
        throw new Error(
            `The book has no Page ${n}: it has ${contentPages.length} content pages.`,
        );
    return contentPages[n - 1];
}

/**
 * Bring the page's thumbnail into view at the bottom of the page list, click it, and check that
 * Bloom selects and shows that page without moving the thumbnail to the top of the list (BL-9140).
 */
async function clickLowThumbnailAndExpectItToStay(
    page: Page,
    bookPage: IBookPage,
    name: string,
): Promise<void> {
    const before = await thumbnailPlace(page, bookPage.id, true);
    expect(
        before.canScroll,
        "sanity check: the page list should be too long to show at once, or a jump to the top could not be seen",
    ).toBe(true);
    expect(
        before.top,
        `sanity check: ${name}'s thumbnail should start well below the top of the list before the click`,
    ).toBeGreaterThan(before.visibleHeight / 3);

    // selectPage waits for the thumbnail to be highlighted (gridSelected) and for the Edit tab to
    // show the page.
    await selectPage(page, bookPage.id);
    expect(
        await getShownPageId(page),
        `Clicking ${name}'s thumbnail did not open it in the Edit tab`,
    ).toBe(bookPage.id);
    await waitForEditTabSettled(page);

    const after = await thumbnailPlace(page, bookPage.id);
    expect(
        after,
        `${name}'s thumbnail moved in the page list when it was clicked (BL-9140: it jumped to the top)`,
    ).toEqual({ ...before });
}

test("after the title is renamed, clicking pages and adding a recorded video page keep the clicked thumbnail where it was [Test Case ID 397]", async ({
    page,
    step,
}) => {
    test.setTimeout(400000);

    const firstFolder = await step(
        `Make a book from the "${kShell}" shell and replace its cover title with "${kNewTitle}"`,
        async () => {
            const folder = await makeBookFromSourceBook(
                page,
                "Sample Shells",
                kShell,
            );
            // The case clicks Page 13 after adding a page, so the shell needs at least 12.
            expect(
                (await getContentPages(page)).length,
                "sanity check: the shell should have at least 12 content pages",
            ).toBeGreaterThanOrEqual(12);
            // A jump to the top of the page list can only be seen in a list that scrolls. In a
            // tall window every thumbnail of this book fits, so make the page area as short as the
            // nightly runner's, as a person would by making the window smaller.
            await waitForEditTabSettled(page);
            if (!(await pageListCanScroll(page))) {
                await setViewportSize(page, kShortPageArea);
                shrankPageArea = true;
            }
            await typeInGroup(page, ".bookTitle", "en", kNewTitle);
            return folder;
        },
    );
    if (!firstFolder) throw new Error("No book was made.");

    await step(
        "Click Page 9: it is highlighted and opens, and stays where it was in the page list",
        async () => {
            await clickLowThumbnailAndExpectItToStay(
                page,
                await pageNumbered(page, 9),
                "Page 9",
            );
        },
    );

    await step(
        "The click left the cover and so renamed the book after its new title",
        async () => {
            const folder = await findBookFolder(page, kNewTitle);
            // Without the rename, the clicks above and below would prove nothing about BL-9140.
            expect(
                Path.basename(folder),
                "typing the title should have renamed the book's folder after it",
            ).toBe(kNewTitle);
            expect(
                folder,
                "the book should have left the folder it was made in",
            ).not.toBe(firstFolder);
        },
    );

    const page4 = await step(
        "Click Page 4: it is highlighted and opens",
        async () => {
            const page4 = await pageNumbered(page, 4);
            await selectPage(page, page4.id);
            expect(
                await getShownPageId(page),
                "Clicking Page 4's thumbnail did not open it",
            ).toBe(page4.id);
            return page4;
        },
    );
    if (!page4) throw new Error("Page 4 was not found.");

    const videoPage = await step(
        'Add a "Just Video" page from the Add Page dialog: it comes after Page 4 and opens',
        async () => {
            const before = (await getPages(page)).map((p) => p.id);
            await openAddPageDialog(page);
            await addPageFromDialog(page, "Just Video", "Sign Language");
            const pages = await getPages(page);
            const added = pages.filter((p) => !before.includes(p.id));
            expect(
                added,
                'Add Page should have added exactly one page, the "Just Video" one',
            ).toHaveLength(1);
            expect(
                pages[pages.findIndex((p) => p.id === page4.id) + 1].id,
                "The new page should come straight after Page 4, the page that was selected",
            ).toBe(added[0].id);
            await expect
                .poll(() => getShownPageId(page), {
                    timeout: 30000,
                    message: "Adding the page did not open it",
                })
                .toBe(added[0].id);
            return added[0];
        },
    );
    if (!videoPage) throw new Error("No video page was added.");

    await step(
        "Record a video with the Sign Language tool: the camera comes on, recording starts and stops, and the video is on the page",
        async () => {
            const box = videoBoxes(page).first();
            await expect(
                box,
                "sanity check: the new page's video box starts empty",
            ).toHaveClass(/bloom-noVideoSelected/);
            // recordVideo waits for the record button to come on (the camera started), for the
            // tool to show it recording, and for the recording to land in the box.
            const source = await recordVideo(page, box);
            expect(source, "The video box shows no recording").not.toBe("");
            await expect(
                box.locator("video"),
                "The recording does not appear on the video page",
            ).toBeVisible();
        },
    );

    await step(
        "Click Page 13, then the video page: it is highlighted and opens, and stays where it was in the page list",
        async () => {
            const page13 = await pageNumbered(page, 13);
            await selectPage(page, page13.id);
            expect(
                await getShownPageId(page),
                "Clicking Page 13's thumbnail did not open it",
            ).toBe(page13.id);
            await clickLowThumbnailAndExpectItToStay(
                page,
                videoPage,
                "the video page",
            );
        },
    );
});
