// A book has one summary. The Publish tab's Web screen edits it as the summary the book gets on
// BloomLibrary.org, the ePUB screen's Book Metadata dialog edits the same one, and the ePUB that
// Bloom makes carries it as its description (<dc:description> in content.opf), so a reader app
// that shows an ePUB's description shows it (BL-7831).
//
// Automates the manual test "ePUB Metadata Summary Match" (Test Case ID 165): the summary in the
// ePUB's metadata is the one typed on the Web screen, and editing it, with the keyboard and the
// mouse, works in both places.
//
// The ePUB is read from the ePUB screen's preview, which stages the very files that Save... zips.
// Save... itself opens the operating system's save dialog, which no test can answer.

import { type Locator, type Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    bookMetadataField,
    clickBookMetadataOk,
    getBookMetadataValue,
    getEpubPackageDublinCore,
    openBookMetadataDialog,
    openEpubPublishScreen,
    readEpubPreviewPackage,
    showEpubPreview,
} from "../helpers/epubPublish";
import {
    getBookSummary,
    librarySummaryBox,
    openPublishToWeb,
} from "../helpers/libraryPublish";

test.use({
    collectionSpec: {
        name: "epub-metadata-summary",
        languages: ["en"],
        // Publishing an ePUB needs Pro.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

// Typed on the Web screen in two pieces: the second goes in front of the first, so that the
// box's own editing (moving the caret back to the start) is part of what is checked.
const WEB_SUMMARY_END = "a goat who learns to count the stars.";
const WEB_SUMMARY_START = "Kibo: ";
const WEB_SUMMARY = WEB_SUMMARY_START + WEB_SUMMARY_END;
// Typed in the Book Metadata dialog over the whole of the summary, selected with the mouse.
const EPUB_SUMMARY = "Kibo the goat counts every star in the night sky.";

test("the ePUB's description is the book's summary, edited on the Web screen or in Book Metadata [Test Case ID 165]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);

    await step("Make a book", async () => {
        await makeBookFromTemplate(page, "Basic Book");
        expect(
            await getBookMetadataValue(page, "summary"),
            "A new book should start with no summary, or the checks below prove nothing.",
        ).toBe("");
    });

    await step("Type a summary on the Publish tab's Web screen", async () => {
        await openPublishToWeb(page);
        const box = librarySummaryBox(page);
        await expect(
            box,
            "The Web screen should show an empty Summary box.",
        ).toHaveValue("");
        await box.click();
        await page.keyboard.type(WEB_SUMMARY_END);
        await page.keyboard.press("Home");
        await page.keyboard.type(WEB_SUMMARY_START);
        await expect(
            box,
            "The Summary box should hold both pieces, the second in front of the first.",
        ).toHaveValue(WEB_SUMMARY);
        await expect
            .poll(() => getBookSummary(page), {
                timeout: 15000,
                message:
                    "Bloom never saved the summary typed on the Web screen.",
            })
            .toBe(WEB_SUMMARY);
    });

    await step(
        "The ePUB screen's Book Metadata dialog shows the same summary",
        async () => {
            await openEpubPublishScreen(page);
            await openBookMetadataDialog(page);
            await expect(
                bookMetadataField(page, "summary"),
                "Book Metadata should show the summary typed on the Web screen.",
            ).toHaveValue(WEB_SUMMARY);
        },
    );

    await step(
        "Select the summary with the mouse in Book Metadata and type a new one",
        async () => {
            const field = bookMetadataField(page, "summary");
            await dragSelectAllText(page, field);
            expect(
                await field.evaluate((textArea: HTMLTextAreaElement) => [
                    textArea.selectionStart,
                    textArea.selectionEnd,
                ]),
                "Dragging the mouse across the summary should select all of it.",
            ).toEqual([0, WEB_SUMMARY.length]);
            await page.keyboard.type(EPUB_SUMMARY);
            await expect(
                field,
                "Typing over the selection should replace the whole summary.",
            ).toHaveValue(EPUB_SUMMARY);
            await clickBookMetadataOk(page);
            await expect
                .poll(() => getBookMetadataValue(page, "summary"), {
                    timeout: 15000,
                    message:
                        "Bloom never saved the summary changed in Book Metadata.",
                })
                .toBe(EPUB_SUMMARY);
        },
    );

    await step(
        "The Web screen shows the summary changed in Book Metadata",
        async () => {
            await openPublishToWeb(page);
            await expect(
                librarySummaryBox(page),
                "The Web screen's Summary box should show the summary changed in Book Metadata.",
            ).toHaveValue(EPUB_SUMMARY, { timeout: 15000 });
            expect(
                await getBookSummary(page),
                "The Web screen's summary, the one uploaded to BloomLibrary.org, should be the changed one.",
            ).toBe(EPUB_SUMMARY);
        },
    );

    await step("The ePUB carries the summary as its description", async () => {
        await openEpubPublishScreen(page);
        await showEpubPreview(page);
        const packageText = await readEpubPreviewPackage(page);
        expect(
            await getEpubPackageDublinCore(page, packageText, "description"),
            "The ePUB's content.opf should have one dc:description, the book's summary.",
        ).toEqual([EPUB_SUMMARY]);
    });
});

/**
 * Select all the text of a text box the way a person does with the mouse: press at the start of
 * its first line, drag to the far corner of the box, and let go. The box's text must fit on its
 * first line.
 */
async function dragSelectAllText(page: Page, textBox: Locator): Promise<void> {
    await textBox.scrollIntoViewIfNeeded();
    const box = await textBox.boundingBox();
    if (!box) throw new Error("The text box to select in is not showing.");
    const lineHeight = await textBox.evaluate(
        (element) => parseFloat(getComputedStyle(element).lineHeight) || 16,
    );
    await page.mouse.move(box.x + 1, box.y + lineHeight / 2);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2, {
        steps: 5,
    });
    await page.mouse.move(box.x + box.width - 1, box.y + box.height - 1, {
        steps: 5,
    });
    await page.mouse.up();
}
