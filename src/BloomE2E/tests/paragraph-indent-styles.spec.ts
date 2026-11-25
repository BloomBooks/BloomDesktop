// The Format dialog's paragraph indent belongs to a style: setting it on one style indents every
// box of that style and no other, and moving a box to another style gives it that style's indent.
// Covered for Normal, for a style Bloom offers in every book (Big Words), and for a style the user
// creates. Automates the manual test "Paragraph Indent Styles" (Test Case ID 375).
//
// The book is Basic Book with two "Just Text" pages, each one box in one language. The first page's
// box is the one that changes style; the second page's box stays Normal throughout, so that a change
// to the other styles can be seen not to reach it. Every indent set below is the opposite of what
// the box showed just before, so each one is a visible change.
//
// The tests are serial: each starts from the state the one before it left behind.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    reloadPageBeingEdited,
    typeInGroup,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    applyStyleById,
    chooseIndent,
    closeFormatDialog,
    createStyle,
    getStyleIdShown,
    getTextBoxFormatting,
    openFormatDialog,
    scrollFormatGearIntoView,
    showFormatDialogTab,
    type FormatDialogTab,
    type Indent,
} from "../helpers/formatDialog";

const L1 = "en";

test.use({
    collectionSpec: { name: "paragraph-indent-styles", languages: [L1] },
});

test.describe.configure({ mode: "serial" });

const GROUP = ".bloom-translationGroup";
// Styles are named by their ids, which the markup and the Format dialog's menu both use, so that
// nothing depends on the UI language the menu shows the names in.
/** Every box starts with this style. */
const NORMAL = "normal";
/** A style Bloom offers in every book, which its menu calls "Big Words". */
const BIG_WORDS = "BigWords";
/** The style the test creates. */
const FLUSH = "Flush";

/** The book's two content pages once the first test has made them: [the one that changes, Normal's]. */
let pages: IBookPage[];

/** The style and indent of the one box on the page being shown. */
const boxOnPage = async (
    page: Page,
): Promise<{ style: string; indent: Indent }> => {
    const boxes = await getTextBoxFormatting(page);
    // Sanity check: a "Just Text" page in a one-language book has one box.
    expect(boxes.map((b) => `${b.group}:${b.lang}`)).toEqual([`0:${L1}`]);
    return { style: boxes[0].style, indent: boxes[0].indent };
};

/** The style and indent of the one box on a page, going to that page first. */
const boxOn = async (page: Page, bookPage: IBookPage) => {
    await goToPage(page, bookPage.id);
    return boxOnPage(page);
};

/** Open the Format dialog on the box of the page being shown, on the tab wanted. */
const openFormatDialogOnBox = async (page: Page, tab: FormatDialogTab) => {
    await clickInGroup(page, GROUP, L1);
    await scrollFormatGearIntoView(page);
    await openFormatDialog(page);
    await showFormatDialogTab(page, tab);
};

/** On the box of the page being shown, choose an indent for its style, and close the dialog. */
const setIndentOfBoxStyle = async (page: Page, indent: Indent) => {
    await openFormatDialogOnBox(page, "paragraph");
    await chooseIndent(page, indent);
    await closeFormatDialog(page);
};

/** Move the box of the page being shown to another existing style, and close the dialog. */
const switchBoxTo = async (page: Page, styleId: string) => {
    await openFormatDialogOnBox(page, "styleName");
    await applyStyleById(page, styleId);
    await closeFormatDialog(page);
};

test.describe("Paragraph Indent Styles", () => {
    test("builds two pages, each with one box of two paragraphs", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Just Text", 2);
        pages = await getContentPages(page);
        expect(pages).toHaveLength(2);

        for (const bookPage of pages) {
            await goToPage(page, bookPage.id);
            // Two paragraphs, so that the indent shows on more than the box's first line.
            await typeInGroup(page, GROUP, L1, "Some text.\nMore text.");
            // Sanity check the starting point: Normal, with no indent.
            expect(await boxOnPage(page)).toEqual({
                style: NORMAL,
                indent: "none",
            });
        }
    });

    test("an indent given to Big Words takes effect on its box and not on a Normal one [Test Case ID 375]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await goToPage(page, pages[0].id);
        await switchBoxTo(page, BIG_WORDS);
        expect(await boxOnPage(page)).toEqual({
            style: BIG_WORDS,
            indent: "none",
        });

        await setIndentOfBoxStyle(page, "indented");

        expect(await boxOnPage(page)).toEqual({
            style: BIG_WORDS,
            indent: "indented",
        });
        expect(await boxOn(page, pages[1])).toEqual({
            style: NORMAL,
            indent: "none",
        });
    });

    test("a new style starts with the box's indent, and an indent given to it takes effect on its box only [Test Case ID 375]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await goToPage(page, pages[0].id);
        await openFormatDialogOnBox(page, "styleName");
        expect(await getStyleIdShown(page)).toBe(BIG_WORDS);
        await createStyle(page, FLUSH);
        await closeFormatDialog(page);
        // The new style copied the Big Words box's settings, indent included.
        expect(await boxOnPage(page)).toEqual({
            style: FLUSH,
            indent: "indented",
        });

        await setIndentOfBoxStyle(page, "none");

        expect(await boxOnPage(page)).toEqual({
            style: FLUSH,
            indent: "none",
        });
        expect(await boxOn(page, pages[1])).toEqual({
            style: NORMAL,
            indent: "none",
        });
    });

    test("an indent given to Normal takes effect on its box and not on the other style's [Test Case ID 375]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await goToPage(page, pages[1].id);
        await setIndentOfBoxStyle(page, "indented");

        expect(await boxOnPage(page)).toEqual({
            style: NORMAL,
            indent: "indented",
        });
        expect(await boxOn(page, pages[0])).toEqual({
            style: FLUSH,
            indent: "none",
        });
    });

    test("switching a box between the styles changes its indent each time [Test Case ID 375]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await goToPage(page, pages[0].id);
        expect(await boxOnPage(page)).toEqual({
            style: FLUSH,
            indent: "none",
        });

        await switchBoxTo(page, NORMAL);
        expect(await boxOnPage(page)).toEqual({
            style: NORMAL,
            indent: "indented",
        });

        await switchBoxTo(page, FLUSH);
        expect(await boxOnPage(page)).toEqual({
            style: FLUSH,
            indent: "none",
        });

        await switchBoxTo(page, BIG_WORDS);
        expect(await boxOnPage(page)).toEqual({
            style: BIG_WORDS,
            indent: "indented",
        });
    });

    test("each style keeps its indent when the book is saved and the pages are opened again [Test Case ID 375]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        // Move the first page's box back to the new style, then reload each page from the saved
        // book: the indents must come from the book's saved styles, not from the open page.
        await goToPage(page, pages[0].id);
        await switchBoxTo(page, FLUSH);
        await reloadPageBeingEdited(page);
        expect(await boxOnPage(page)).toEqual({
            style: FLUSH,
            indent: "none",
        });
        await switchBoxTo(page, BIG_WORDS);
        await reloadPageBeingEdited(page);
        expect(await boxOnPage(page)).toEqual({
            style: BIG_WORDS,
            indent: "indented",
        });

        await goToPage(page, pages[1].id);
        await reloadPageBeingEdited(page);
        expect(await boxOnPage(page)).toEqual({
            style: NORMAL,
            indent: "indented",
        });
    });
});
