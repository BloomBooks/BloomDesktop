// A speech bubble holding more text than it can show (BL-7911). On a Device page a reader scrolls
// such text, so Bloom gives the bubble a scroll bar and does not mark it as overflowing; on a paper
// size it is marked. The BloomPUB preview shows the bubble with its scroll bar and without any
// mark, whatever size the book is edited at, and gives no scroll bar to a bubble whose text fits.
//
// Automates the manual test "BloomPUB Bubbles" (Test Case ID 317).
//
// The bubble is filled while its Auto Height is still on, as the case has it: Bloom then grows the
// bubble down to the bottom of the canvas, and the lines after that are the ones that do not fit.
// The second, short bubble is not in the case's steps; it is there for the case's summary ("no
// scroll bars in speech bubbles sized not to need them").

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    waitForPageToSettle,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    canvasElementTextBox,
    dragPaletteItemOntoCanvas,
    getCanvasElementAutoHeight,
    selectCanvasElement,
    setCanvasElementAutoHeight,
    typeInCanvasElement,
} from "../helpers/canvasElements";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    expectNoOverflowMarks,
    expectTextBoxMarkedAsOverflowing,
    fillTextBoxPastItsBottom,
    findOverflowDrawingInPreview,
    getTextBoxView,
    getThumbnailOverflow,
} from "../helpers/overflowMarks";
import { getPageSize, setPageSize } from "../helpers/pageSize";
import { selectPage } from "../helpers/pageThumbnails";
import {
    getPreviewTextBox,
    openPublishDestination,
    showBloomPubPreview,
    showPreviewPage,
} from "../helpers/publish";
import { waitForScrollBarOf } from "../helpers/textBoxScrollBars";
import type { Page } from "@playwright/test";

test.use({
    collectionSpec: {
        name: "bloompub-bubbles",
        languages: ["en"],
        // Below Pro, Bloom will not publish a book whose author put a canvas element in it.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** What the short bubble says. The full one holds "Line 1", "Line 2" and so on. */
const SHORT_TEXT = "Short";
const FIRST_LINE_OF_FULL_BUBBLE = "Line 1";

/**
 * Click the page in the page list, which is when Bloom brings its thumbnail up to date, and wait
 * for the thumbnail to show the overflow zigzag, or not to.
 */
async function expectThumbnailZigzag(
    page: Page,
    bookPage: IBookPage,
    zigzag: boolean,
    why: string,
): Promise<void> {
    await selectPage(page, bookPage.id);
    await expect
        .poll(
            async () => (await getThumbnailOverflow(page, bookPage.id)).zigzag,
            { timeout: 15000, message: why },
        )
        .toBe(zigzag);
}

test("a speech bubble with too much text scrolls unmarked on a Device page, is marked on A5, and scrolls unmarked in the BloomPUB preview [Test Case ID 317]", async ({
    page,
    step,
}) => {
    test.setTimeout(420000);

    const canvasPage = await step(
        "Make a Device 16x9 book with a Canvas page",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Canvas");
            const [contentPage] = await getContentPages(page);
            await goToPage(page, contentPage.id);
            await setPageSize(page, "Device16x9Landscape");
            return contentPage;
        },
    );
    if (!canvasPage) throw new Error("The book has no content page.");

    const fullBubble = await step(
        "Add a speech bubble and type more text than it can hold",
        async () => {
            const bubble = await dragPaletteItemOntoCanvas(page, "speech", {
                xFraction: 0.3,
                yFraction: 0.3,
            });
            await selectCanvasElement(page, bubble);
            expect(
                await getCanvasElementAutoHeight(page),
                "test setup: a new speech bubble should have Auto Height on",
            ).toBe(true);
            await fillTextBoxPastItsBottom(
                page,
                canvasElementTextBox(page, bubble),
            );
            return bubble;
        },
    );
    if (fullBubble === undefined) throw new Error("No bubble was added.");
    const fullBox = canvasElementTextBox(page, fullBubble);

    await step("Turn off the bubble's Auto Height", async () => {
        await selectCanvasElement(page, fullBubble);
        await setCanvasElementAutoHeight(page, false);
    });

    await step(
        "See a scroll bar on the bubble, and nothing marked as overflowing",
        async () => {
            await waitForScrollBarOf(fullBox, true, "The full bubble");
            expect(
                (await getTextBoxView(fullBox)).hidden,
                "test setup: the full bubble should have text out of sight",
            ).toBeGreaterThan(1);
            await expectNoOverflowMarks(
                page,
                "On a Device page, a bubble with too much text should not be marked as overflowing.",
            );
            await expectThumbnailZigzag(
                page,
                canvasPage,
                false,
                "On a Device page, the thumbnail should not have the overflow zigzag.",
            );
        },
    );

    await step("Add a second bubble, whose text fits", async () => {
        const bubble = await dragPaletteItemOntoCanvas(page, "speech", {
            xFraction: 0.7,
            yFraction: 0.3,
        });
        await selectCanvasElement(page, bubble);
        await typeInCanvasElement(page, SHORT_TEXT);
        await waitForScrollBarOf(
            canvasElementTextBox(page, bubble),
            false,
            "The short bubble",
        );
    });

    await step("Switch the book to A5", async () => {
        await waitForPageToSettle(page);
        await setPageSize(page, "A5Landscape");
        expect(await getPageSize(page)).toBe("A5Landscape");
    });

    await step("See the bubble marked as overflowing", async () => {
        await expectTextBoxMarkedAsOverflowing(
            page,
            fullBox,
            "the full bubble",
            false,
        );
        await waitForScrollBarOf(fullBox, true, "The full bubble");
        await expectThumbnailZigzag(
            page,
            canvasPage,
            true,
            "On an A5 page, the thumbnail should have the overflow zigzag.",
        );
    });

    const previewPage = await step(
        "Open the BloomPUB preview and go to the page with the bubbles",
        async () => {
            await waitForPageToSettle(page);
            await openPublishDestination(page, "BloomPUB");
            const player = await showBloomPubPreview(page);
            return {
                player,
                bookPage: await showPreviewPage(player, canvasPage.id),
            };
        },
    );
    if (!previewPage) throw new Error("The BloomPUB preview never opened.");

    await step(
        "See the scroll bar on the full bubble only, and no overflow mark",
        async () => {
            const fullInPreview = getPreviewTextBox(
                previewPage.bookPage,
                FIRST_LINE_OF_FULL_BUBBLE,
            );
            const shortInPreview = getPreviewTextBox(
                previewPage.bookPage,
                SHORT_TEXT,
            );
            await expect(
                fullInPreview,
                "test setup: the preview should show the full bubble",
            ).toHaveCount(1);
            await expect(
                shortInPreview,
                "test setup: the preview should show the short bubble",
            ).toHaveCount(1);
            expect(
                (await getTextBoxView(fullInPreview)).hidden,
                "test setup: the full bubble should have text out of sight in the preview too",
            ).toBeGreaterThan(1);

            await waitForScrollBarOf(
                fullInPreview,
                true,
                "The full bubble in the BloomPUB preview",
            );
            await waitForScrollBarOf(
                shortInPreview,
                false,
                "The short bubble in the BloomPUB preview",
            );
            expect(
                (await findOverflowDrawingInPreview(previewPage.player))
                    .findings,
                "The BloomPUB preview should show no overflow mark.",
            ).toEqual([]);
        },
    );
});
