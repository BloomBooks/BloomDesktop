// How the Edit tab shows text that does not fit (BL-17109).
//
// A text box with more text than it can show keeps its text color. A label saying "Overflowing"
// sits on the line where the text is cut off, and the last line that fits fades out. When the box
// is scrolled, the label moves up with the text, the lines below it are washed red, and the gray
// language label stays at the bottom of what the box shows. Boxes that each fit but together run
// past the page's margin get a band over what is past it and a label saying "Page overflowing".
// In the page list, the thumbnail of a page with either problem gets a red zigzag along its
// bottom. None of this is saved in the book.
//
// Whether the marks look right is for a person. These tests check that each mark is there when it
// should be, gone when it should not, and on the line of text it is about.
//
// The tests are serial because each one works on the book the first one builds, and each leaves
// the page it filled empty again.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    waitForPageToSettle,
    type IBookPage,
} from "../helpers/bookMaking";
import { goToCreditsPage } from "../helpers/creditsPage";
import {
    emptyTextBox,
    expectLanguageLabelAtBottomOf,
    expectNoOverflowMarks,
    expectPageMarkedAsOverflowing,
    expectTextBoxMarkedAsOverflowing,
    fillTextBoxPastItsBottom,
    findOverflowDrawingInPreview,
    findOverflowDrawingInSavedBook,
    getOverflowTooltipsAtPageBottom,
    getTextBoxView,
    getThumbnailOverflow,
    pressUntilTextBoxFitsOrNot,
    scrollTextBox,
    type OverflowFilling,
} from "../helpers/overflowMarks";
import {
    chooseFontSize,
    closeFormatDialog,
    getFontSizeChoices,
    getFontSizeShown,
    openFormatDialog,
    scrollFormatGearIntoView,
    showFormatDialogTab,
} from "../helpers/formatDialog";
import { getPageSize, setPageSize } from "../helpers/pageSize";
import {
    openPublishDestination,
    showBloomPubPreview,
} from "../helpers/publish";
import { getZoom, setZoom, switchTab } from "../helpers/workspace";
import type { Locator, Page } from "@playwright/test";

test.use({
    collectionSpec: { name: "overflow-marks", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// The book every test works on, and its content pages.
let bookFolder: string;
let pages: IBookPage[];

const TEXT_GROUP = ".bloom-translationGroup";

/** Show a content page and click in its text box. */
async function textBoxOnPage(page: Page, pageId: string): Promise<Locator> {
    await goToPage(page, pageId);
    return clickInGroup(page, TEXT_GROUP, "en");
}

/**
 * Go to another page after the pause a person takes before moving on. Bloom saves the page as the
 * browser last sent it, which is a moment after the last change.
 */
async function leavePage(page: Page, toPageId: string): Promise<void> {
    await waitForPageToSettle(page);
    await goToPage(page, toPageId);
}

test.describe("marks on text that does not fit", () => {
    test("builds an A5 book with three Basic Text & Image pages", async ({
        page,
    }) => {
        test.setTimeout(300000);
        bookFolder = await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Basic Text & Image", 3);
        pages = await getContentPages(page);
        expect(pages, "test setup: three content pages").toHaveLength(3);
        expect(await getPageSize(page), "test setup: a paper page size").toBe(
            "A5Portrait",
        );
    });

    for (const filling of [
        "lines of text",
        "empty lines",
    ] satisfies OverflowFilling[]) {
        test(`a box with ${filling} out of sight is marked where its text is cut off, scrolled or not, until it fits again`, async ({
            page,
            step,
        }) => {
            const box = await textBoxOnPage(page, pages[0].id);
            await expectNoOverflowMarks(
                page,
                "test setup: an empty text box should not be marked",
            );
            const colorBefore = (await getTextBoxView(box)).textColor;

            await step(`Type until ${filling} do not fit`, async () => {
                await fillTextBoxPastItsBottom(page, box, filling);
            });

            await step(
                "Scroll to the first line and check the marks",
                async () => {
                    await scrollTextBox(page, box, "top");
                    await expectTextBoxMarkedAsOverflowing(
                        page,
                        box,
                        "the box showing its first line",
                    );
                    expect(
                        (await getTextBoxView(box)).textColor,
                        "The text of a box that is too full should keep its color.",
                    ).toBe(colorBefore);
                },
            );

            await step(
                "Scroll to the last line and check the marks",
                async () => {
                    await scrollTextBox(page, box, "end");
                    await expectTextBoxMarkedAsOverflowing(
                        page,
                        box,
                        "the box scrolled to its end",
                    );
                },
            );

            await step("Scroll back and check the marks", async () => {
                await scrollTextBox(page, box, "top");
                await expectTextBoxMarkedAsOverflowing(
                    page,
                    box,
                    "the box scrolled back to its first line",
                );
            });

            await step("Delete the text and check the marks go", async () => {
                await emptyTextBox(box);
                await expectNoOverflowMarks(
                    page,
                    "Once the text fits again, every mark should go.",
                );
            });
        });
    }

    test("undoing the lines that did not fit takes the marks away, and redoing them brings the marks back", async ({
        page,
        step,
    }) => {
        const box = await textBoxOnPage(page, pages[0].id);
        // Typed key by key, because Undo takes back key presses; the lines out of sight are empty,
        // so each is one press of Enter.
        await fillTextBoxPastItsBottom(
            page,
            box,
            "empty lines",
            3,
            "key by key",
        );
        await expectTextBoxMarkedAsOverflowing(page, box, "the box");

        await step("Undo until the text fits", async () => {
            await pressUntilTextBoxFitsOrNot(page, box, "Control+z", "fits");
            await expectNoOverflowMarks(
                page,
                "After Undo the text fits, so every mark should go.",
            );
        });
        await step("Redo until the text does not fit", async () => {
            await pressUntilTextBoxFitsOrNot(
                page,
                box,
                "Control+y",
                "does not fit",
            );
            await expectTextBoxMarkedAsOverflowing(
                page,
                box,
                "the box after Redo",
            );
        });

        await emptyTextBox(box);
        await expectNoOverflowMarks(page, "test cleanup: the box is empty");
    });

    test("the smallest font, at which the text fits, takes the marks away, and the first size brings them back", async ({
        page,
        step,
    }) => {
        const box = await textBoxOnPage(page, pages[0].id);
        await fillTextBoxPastItsBottom(page, box);
        await scrollTextBox(page, box, "top");
        await expectTextBoxMarkedAsOverflowing(page, box, "the box");

        // The Format dialog formats the box that has the focus, which scrolling took away.
        await clickInGroup(page, TEXT_GROUP, "en");
        await scrollFormatGearIntoView(page);
        await openFormatDialog(page);
        await showFormatDialogTab(page, "characters");
        const sizeBefore = await getFontSizeShown(page);
        expect(
            sizeBefore,
            "test setup: the Characters tab should show the box's font size",
        ).toBeGreaterThan(4);
        try {
            await step("Choose the smallest font size", async () => {
                // The smallest size the list offers.
                await chooseFontSize(
                    page,
                    Math.min(...(await getFontSizeChoices(page))),
                );
                await expect
                    .poll(async () => (await getTextBoxView(box)).hidden, {
                        message:
                            "test setup: at the smallest size all the text should fit",
                    })
                    .toBeLessThan(1);
                await expectNoOverflowMarks(
                    page,
                    "The text fits at the smaller size, so every mark should go.",
                );
            });
            await step("Put the font size back", async () => {
                await chooseFontSize(page, sizeBefore);
                await expect
                    .poll(async () => (await getTextBoxView(box)).hidden, {
                        message:
                            "test setup: at the first size the text should not fit again",
                    })
                    .toBeGreaterThan(1);
                // The box shows its language label only while it has the focus, which the
                // dialog has now.
                await closeFormatDialog(page);
                await clickInGroup(page, TEXT_GROUP, "en");
                await expectTextBoxMarkedAsOverflowing(
                    page,
                    box,
                    "the box back at its first font size",
                );
            });
        } finally {
            await closeFormatDialog(page);
        }

        await emptyTextBox(box);
        await expectNoOverflowMarks(page, "test cleanup: the box is empty");
    });

    test("the marks stay on the same line of text when the page is zoomed", async ({
        page,
        step,
    }) => {
        const zoomBefore = (await getZoom(page)).zoom;
        const box = await textBoxOnPage(page, pages[0].id);
        await fillTextBoxPastItsBottom(page, box);
        try {
            for (const zoom of [60, 150]) {
                await step(`Zoom to ${zoom}%, box not scrolled`, async () => {
                    await setZoom(page, zoom);
                    await scrollTextBox(page, box, "top");
                    await expectTextBoxMarkedAsOverflowing(
                        page,
                        box,
                        `the box at ${zoom}% showing its first line`,
                    );
                });
                await step(`Zoom ${zoom}%, box scrolled`, async () => {
                    await scrollTextBox(page, box, "end");
                    await expectTextBoxMarkedAsOverflowing(
                        page,
                        box,
                        `the box at ${zoom}% scrolled to its end`,
                    );
                });
            }
        } finally {
            await setZoom(page, zoomBefore);
        }
        await emptyTextBox(box);
        await expectNoOverflowMarks(page, "test cleanup: the box is empty");
    });

    test("the thumbnail of a page with a too-full box has a zigzag, and loses it when the text fits", async ({
        page,
        step,
    }) => {
        const tooFull = pages[1];
        const other = pages[2];
        const box = await textBoxOnPage(page, tooFull.id);
        expect(
            await getThumbnailOverflow(page, tooFull.id),
            "test setup: the page's thumbnail starts with no overflow mark",
        ).toEqual({ zigzag: false, triangle: false });
        await fillTextBoxPastItsBottom(page, box);
        await expectTextBoxMarkedAsOverflowing(page, box, "the box");

        await step("Leave the page and look at its thumbnail", async () => {
            await leavePage(page, other.id);
            await expect
                .poll(() => getThumbnailOverflow(page, tooFull.id), {
                    message:
                        "The thumbnail of a page with a too-full box should show the zigzag, and no triangle.",
                })
                .toEqual({ zigzag: true, triangle: false });
            expect(
                await getThumbnailOverflow(page, other.id),
                "A page whose text fits should have no zigzag on its thumbnail.",
            ).toEqual({ zigzag: false, triangle: false });
        });

        await step(
            "Come back: the marks are on the box again, and none of the drawing is in the saved book",
            async () => {
                const boxAgain = await textBoxOnPage(page, tooFull.id);
                await expectTextBoxMarkedAsOverflowing(
                    page,
                    boxAgain,
                    "the box after coming back to its page",
                );
                // Scrolled when the page is saved, so there is a scroll offset to keep out.
                await scrollTextBox(page, boxAgain, "end");
                await expectTextBoxMarkedAsOverflowing(
                    page,
                    boxAgain,
                    "the box scrolled to its end",
                );
                await leavePage(page, other.id);
                expect(
                    findOverflowDrawingInSavedBook(bookFolder),
                    "The saved book should hold nothing of the Edit tab's overflow drawing.",
                ).toEqual([]);
            },
        );

        await step("Delete the text; the zigzag goes", async () => {
            const boxAgain = await textBoxOnPage(page, tooFull.id);
            await emptyTextBox(boxAgain);
            await expectNoOverflowMarks(page, "The box is empty.");
            await leavePage(page, other.id);
            await expect
                .poll(() => getThumbnailOverflow(page, tooFull.id), {
                    message:
                        "Once the text fits, the thumbnail's zigzag should go.",
                })
                .toEqual({ zigzag: false, triangle: false });
        });
    });

    test("boxes that each fit but together run past the margin mark the page, with no tooltip", async ({
        page,
        step,
    }) => {
        await goToCreditsPage(page);
        await expectNoOverflowMarks(
            page,
            "test setup: a new book's credits page fits",
        );
        // The two acknowledgments boxes grow with their text, so neither is ever too full itself.
        const boxes = [
            await clickInGroup(page, ".versionAcknowledgments", "en"),
            await clickInGroup(page, ".originalAcknowledgments", "en"),
        ];

        await step("Fill both acknowledgments boxes", async () => {
            for (const box of boxes) {
                await emptyTextBox(box);
                for (let line = 1; line <= 16; line++) {
                    await page.keyboard.insertText(`Thanks ${line}`);
                    await box.press("Enter");
                }
            }
        });

        await step("Check the page is marked, not a box", async () => {
            await expectPageMarkedAsOverflowing(page);
            expect(
                await getOverflowTooltipsAtPageBottom(page),
                "No tooltip about overflow should appear near the bottom of the page.",
            ).toEqual([]);
        });

        await step("Empty the boxes; the marks go", async () => {
            for (const box of boxes) await emptyTextBox(box);
            await expectNoOverflowMarks(
                page,
                "Once everything is inside the margin again, every mark should go.",
            );
        });
    });

    test("on a Device layout a too-full box scrolls and is not marked, and its language label stays at the bottom", async ({
        page,
        step,
    }) => {
        await goToPage(page, pages[0].id);
        await setPageSize(page, "Device16x9Portrait");
        try {
            const box = await clickInGroup(page, TEXT_GROUP, "en");
            await fillTextBoxPastItsBottom(page, box);

            await step("Check nothing is marked", async () => {
                await expectNoOverflowMarks(
                    page,
                    "A Device page scrolls its text, so a too-full box should not be marked.",
                );
            });
            await step(
                "Scroll the box; its language label stays at the bottom",
                async () => {
                    for (const to of ["top", "end"] as const) {
                        await scrollTextBox(page, box, to);
                        await expectLanguageLabelAtBottomOf(
                            box,
                            `the box scrolled to its ${to}`,
                        );
                        await expectNoOverflowMarks(
                            page,
                            "Scrolling should not bring any mark.",
                        );
                    }
                },
            );
            await emptyTextBox(box);
        } finally {
            await setPageSize(page, "A5Portrait");
        }
    });

    test("a BloomPUB of a book with a too-full, scrolled box shows none of the marks", async ({
        page,
        step,
    }) => {
        test.setTimeout(300000);
        const box = await textBoxOnPage(page, pages[1].id);
        await fillTextBoxPastItsBottom(page, box);
        await expectTextBoxMarkedAsOverflowing(page, box, "the box");

        const player = await step("Open the BloomPUB preview", async () => {
            await waitForPageToSettle(page);
            await openPublishDestination(page, "BloomPUB");
            return showBloomPubPreview(page);
        });
        if (!player) throw new Error("The BloomPUB preview never opened.");

        await step("Check the preview has none of the marks", async () => {
            const found = await findOverflowDrawingInPreview(player);
            expect(
                found.textBoxes,
                "test setup: the preview should show the book's text boxes",
            ).toBeGreaterThan(0);
            expect(
                found.findings,
                "The published book should show nothing of the Edit tab's overflow marks.",
            ).toEqual([]);
            expect(
                findOverflowDrawingInSavedBook(bookFolder),
                "The saved book should hold nothing of the Edit tab's overflow drawing.",
            ).toEqual([]);
        });

        await step("Go back and empty the box", async () => {
            await switchTab(page, "edit");
            const boxAgain = await textBoxOnPage(page, pages[1].id);
            await emptyTextBox(boxAgain);
            await expectNoOverflowMarks(page, "The box is empty.");
            await leavePage(page, pages[2].id);
            // Leave Bloom where the next file expects to find it.
            await switchTab(page, "collection");
        });
    });
});
