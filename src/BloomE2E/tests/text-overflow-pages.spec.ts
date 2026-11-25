// Automates the manual test "Text Overflow Icon and Line" (Test Case ID 84): in a paper book, make
// text overflow on several kinds of content page, on the Title Page and on the Credits Page, and
// check that the page shows Bloom's overflow marks and that its thumbnail in the page list carries
// the overflow mark.
//
// Since BL-17109 the marks are no longer a red line and a warning triangle: a too-full box gets a
// label saying "Overflowing" on the line where its text is cut off, a page whose boxes together
// run past its margin gets a label saying "Page overflowing" over a band with a red top line, and
// the thumbnail of either page gets a red zigzag along its bottom. overflow-marks.spec.ts checks
// those marks in detail on one kind of page; this test checks that they appear on each kind of
// page the manual case names.
//
// The tests are serial because each one works on the book the first one builds.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    editablePageFrame,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    waitForPageToSettle,
    type IBookPage,
} from "../helpers/bookMaking";
import { goToCreditsPage } from "../helpers/creditsPage";
import {
    emptyTextBox,
    expectNoOverflowMarks,
    expectPageMarkedAsOverflowing,
    expectTextBoxMarkedAsOverflowing,
    fillTextBoxPastItsBottom,
    getThumbnailOverflow,
} from "../helpers/overflowMarks";
import { getPageSize } from "../helpers/pageSize";
import type { Locator, Page } from "@playwright/test";

test.use({
    collectionSpec: { name: "text-overflow-pages", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// The content page templates the test fills, one of each.
const CONTENT_TEMPLATES = ["Just Text", "Image on Bottom", "Image in Middle"];

const TEXT_GROUP = ".bloom-translationGroup";

// The book every test works on, and its pages.
let contentPages: IBookPage[];
let titlePage: IBookPage;
let creditsPage: IBookPage;

/**
 * Go to another page after the pause a person takes before moving on. Bloom saves the page as the
 * browser last sent it, which is a moment after the last change, and the page list's thumbnail is
 * made from what Bloom saved.
 */
async function leavePage(page: Page, toPageId: string): Promise<void> {
    await waitForPageToSettle(page);
    await goToPage(page, toPageId);
}

/**
 * Check, on the page list, that the thumbnail of `pageId` shows the overflow zigzag (and not the
 * triangle Bloom used before it). Waits, because the page list redraws a thumbnail a moment after
 * Bloom saves its page.
 */
async function expectThumbnailMarked(
    page: Page,
    pageId: string,
    what: string,
): Promise<void> {
    await expect
        .poll(() => getThumbnailOverflow(page, pageId), {
            timeout: 30000,
            message: `The thumbnail of ${what} should show the overflow zigzag.`,
        })
        .toEqual({ zigzag: true, triangle: false });
}

/** Check that the thumbnail of `pageId` shows no overflow mark, as it is right now. */
async function expectThumbnailNotMarked(
    page: Page,
    pageId: string,
    why: string,
): Promise<void> {
    expect(await getThumbnailOverflow(page, pageId), why).toEqual({
        zigzag: false,
        triangle: false,
    });
}

/**
 * Type lines into a box that grows with its text, a line and Enter at a time, until the lowest
 * text on the page is at least three of the box's lines past the bottom of the page's margin.
 */
async function typePastPageMargin(page: Page, box: Locator): Promise<void> {
    await emptyTextBox(box);
    const maxLines = 120;
    for (let line = 1; line <= maxLines; line++) {
        const pastMargin = await editablePageFrame(page).evaluate(() => {
            const margin = document
                .querySelector(".bloom-page .marginBox")!
                .getBoundingClientRect();
            const editables = Array.from(
                document.querySelectorAll<HTMLElement>(
                    ".bloom-page .bloom-editable",
                ),
            ).filter((e) => e.getClientRects().length > 0);
            const lowest = Math.max(
                ...editables.map((e) => e.getBoundingClientRect().bottom),
            );
            const focused = document.activeElement as HTMLElement;
            const lineHeight = parseFloat(getComputedStyle(focused).lineHeight);
            return (lowest - margin.bottom) / lineHeight;
        });
        if (pastMargin >= 3) return;
        await page.keyboard.insertText(`Thanks ${line}`);
        // A real key, so that Bloom checks the page for overflow.
        await box.press("Enter");
    }
    throw new Error(
        `${maxLines} lines did not take the page's text past its margin.`,
    );
}

test.describe("text overflow on content pages, the Title Page and the Credits Page [Test Case ID 84]", () => {
    test("builds a paper book with one page of each content layout", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        for (const template of CONTENT_TEMPLATES) await addPage(page, template);
        contentPages = await getContentPages(page);
        expect(
            contentPages,
            "test setup: one content page per layout",
        ).toHaveLength(CONTENT_TEMPLATES.length);
        expect(
            await getPageSize(page),
            "test setup: a paper page size, not a Device one",
        ).toBe("A5Portrait");
        const pages = await getPages(page);
        const title = pages.find(
            (p) => !p.isContentPage && /title page/i.test(p.caption),
        );
        const credits = pages.find(
            (p) => !p.isContentPage && /credits/i.test(p.caption),
        );
        expect(title, "test setup: the book has a Title Page").toBeDefined();
        expect(
            credits,
            "test setup: the book has a Credits Page",
        ).toBeDefined();
        titlePage = title!;
        creditsPage = credits!;
    });

    test("a too-full text box on each content layout is marked, and so is its thumbnail [Test Case ID 84]", async ({
        page,
        step,
    }) => {
        for (const [index, contentPage] of contentPages.entries()) {
            const layout = CONTENT_TEMPLATES[index];
            await step(`Overfill the text box on "${layout}"`, async () => {
                await goToPage(page, contentPage.id);
                await expectThumbnailNotMarked(
                    page,
                    contentPage.id,
                    `test setup: the "${layout}" page's thumbnail starts with no overflow mark`,
                );
                const box = await clickInGroup(page, TEXT_GROUP, "en");
                await expectNoOverflowMarks(
                    page,
                    `test setup: the empty "${layout}" page should not be marked`,
                );
                await fillTextBoxPastItsBottom(page, box);
                await expectTextBoxMarkedAsOverflowing(
                    page,
                    box,
                    `the text box on "${layout}"`,
                );
            });
            await step(
                `Leave "${layout}" and look at its thumbnail`,
                async () => {
                    await leavePage(page, titlePage.id);
                    await expectThumbnailMarked(
                        page,
                        contentPage.id,
                        `the "${layout}" page with a too-full box`,
                    );
                },
            );
        }
    });

    test("text past the Title Page's margin marks the page, and its thumbnail [Test Case ID 84]", async ({
        page,
        step,
    }) => {
        await goToPage(page, titlePage.id);
        await expectNoOverflowMarks(
            page,
            "test setup: a new book's Title Page fits",
        );
        await expectThumbnailNotMarked(
            page,
            titlePage.id,
            "test setup: the Title Page's thumbnail starts with no overflow mark",
        );

        await step("Fill the contributions box on the Title Page", async () => {
            const box = await clickInGroup(
                page,
                "#originalContributions",
                "en",
            );
            await typePastPageMargin(page, box);
            await expectPageMarkedAsOverflowing(page);
        });

        await step(
            "Leave the Title Page and look at its thumbnail",
            async () => {
                await leavePage(page, contentPages[0].id);
                await expectThumbnailMarked(
                    page,
                    titlePage.id,
                    "the Title Page with text past its margin",
                );
            },
        );
    });

    test("text past the Credits Page's margin marks the page, and its thumbnail [Test Case ID 84]", async ({
        page,
        step,
    }) => {
        await goToCreditsPage(page);
        await expectNoOverflowMarks(
            page,
            "test setup: a new book's Credits Page fits",
        );
        await expectThumbnailNotMarked(
            page,
            creditsPage.id,
            "test setup: the Credits Page's thumbnail starts with no overflow mark",
        );

        await step(
            "Fill the acknowledgments box on the Credits Page",
            async () => {
                const box = await clickInGroup(
                    page,
                    ".versionAcknowledgments",
                    "en",
                );
                await typePastPageMargin(page, box);
                await expectPageMarkedAsOverflowing(page);
            },
        );

        await step(
            "Leave the Credits Page and look at its thumbnail",
            async () => {
                await leavePage(page, contentPages[0].id);
                await expectThumbnailMarked(
                    page,
                    creditsPage.id,
                    "the Credits Page with text past its margin",
                );
            },
        );
    });
});
