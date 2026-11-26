// The Format dialog's paragraph indent belongs to a style: setting it on one style indents every
// box of that style and no other, and moving a box to another style gives it that style's indent.
// Covered for Normal, for a style Bloom offers in every book (Big Words), and for a style the user
// creates. Automates the manual test "Paragraph Indent Styles" (Test Case ID 375).
//
// The book is Basic Book with two "Just Text" pages, each one box of two paragraphs in one
// language. The first page's box is the one that changes style; the second page's box stays Normal
// throughout, so that a change to another style can be seen not to reach it, and a change to Normal
// made on the first page can be seen to reach it. Every indent set below is the opposite of what
// the box showed just before, so each one is a visible change.
//
// The tests are serial: each starts from the state the one before it left behind.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { readSavedParagraphIndents } from "../helpers/bookHtml";
import {
    addPage,
    clickInGroup,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    applyStyleById,
    chooseIndent,
    closeFormatDialog,
    createStyle,
    getStyleIdShown,
    openFormatDialog,
    scrollFormatGearIntoView,
    showFormatDialogTab,
    type FormatDialogTab,
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

/** The indent choices on the Format dialog's Paragraph tab. */
type Indent = "none" | "indented";

/** The book's folder, once the first test has made it. */
let bookFolder: string;
/** The book's two content pages once the first test has made them: [the one that changes, Normal's]. */
let pages: IBookPage[];

/**
 * A CSS text-indent as the Paragraph tab names it: "none" for zero, "indented" for a positive
 * indent. Anything else (a negative, hanging indent; no value at all) is returned as it is, so that
 * it fails a comparison with either name.
 */
const indentName = (textIndent: string): string => {
    const value = parseFloat(textIndent);
    if (value === 0) return "none";
    if (value > 0) return "indented";
    return textIndent;
};

/**
 * The style of the one box on the page being shown, and the indent of each of its paragraphs, as
 * drawn.
 */
const boxOnPage = async (
    page: Page,
): Promise<{ style: string; indents: string[] }> => {
    const boxes = await editablePageFrame(page).evaluate(
        (groupSelector) =>
            Array.from(
                document.querySelectorAll(
                    `.bloom-page ${groupSelector} .bloom-editable`,
                ),
            )
                .filter((box) => getComputedStyle(box).display !== "none")
                .map((box) => ({
                    lang: box.getAttribute("lang") ?? "",
                    style:
                        Array.from(box.classList)
                            .find((c) => c.endsWith("-style"))
                            ?.replace(/-style$/, "") ?? "",
                    textIndents: Array.from(box.querySelectorAll("p")).map(
                        (p) => getComputedStyle(p).textIndent,
                    ),
                })),
        GROUP,
    );
    // Sanity check: a "Just Text" page in a one-language book shows one box, and it holds the two
    // paragraphs the first test typed.
    expect(boxes.map((b) => b.lang)).toEqual([L1]);
    expect(boxes[0].textIndents).toHaveLength(2);
    return {
        style: boxes[0].style,
        indents: boxes[0].textIndents.map(indentName),
    };
};

/** What boxOnPage reports for a box in `style` whose paragraphs are all indented `indent`. */
const boxIn = (style: string, indent: Indent) => ({
    style,
    indents: [indent, indent],
});

/** boxOnPage for a page, going to that page first. */
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
    test("builds two pages, each with one box of two paragraphs [Test Case ID 375]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        bookFolder = await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Just Text", 2);
        pages = await getContentPages(page);
        expect(pages).toHaveLength(2);

        for (const bookPage of pages) {
            await goToPage(page, bookPage.id);
            await typeInGroup(page, GROUP, L1, "Some text.\nMore text.");
            // Sanity check the starting point: Normal, with no indent.
            expect(await boxOnPage(page)).toEqual(boxIn(NORMAL, "none"));
        }
    });

    test("an indent given to Big Words takes effect on its box and not on a Normal one [Test Case ID 375]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await goToPage(page, pages[0].id);
        await switchBoxTo(page, BIG_WORDS);
        expect(await boxOnPage(page)).toEqual(boxIn(BIG_WORDS, "none"));

        await setIndentOfBoxStyle(page, "indented");

        expect(await boxOnPage(page)).toEqual(boxIn(BIG_WORDS, "indented"));
        expect(await boxOn(page, pages[1])).toEqual(boxIn(NORMAL, "none"));
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
        expect(await boxOnPage(page)).toEqual(boxIn(FLUSH, "indented"));

        await setIndentOfBoxStyle(page, "none");

        expect(await boxOnPage(page)).toEqual(boxIn(FLUSH, "none"));
        expect(await boxOn(page, pages[1])).toEqual(boxIn(NORMAL, "none"));
    });

    test("an indent given to Normal on one page reaches the Normal box on the other page [Test Case ID 375]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await goToPage(page, pages[0].id);
        await switchBoxTo(page, NORMAL);
        expect(await boxOnPage(page)).toEqual(boxIn(NORMAL, "none"));

        await setIndentOfBoxStyle(page, "indented");

        expect(await boxOnPage(page)).toEqual(boxIn(NORMAL, "indented"));
        // The second page's box was not touched; it is Normal, so it takes Normal's new indent.
        expect(await boxOn(page, pages[1])).toEqual(boxIn(NORMAL, "indented"));
    });

    test("switching a box between the styles changes its indent each time [Test Case ID 375]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await goToPage(page, pages[0].id);
        expect(await boxOnPage(page)).toEqual(boxIn(NORMAL, "indented"));

        await switchBoxTo(page, FLUSH);
        expect(await boxOnPage(page)).toEqual(boxIn(FLUSH, "none"));

        await switchBoxTo(page, BIG_WORDS);
        expect(await boxOnPage(page)).toEqual(boxIn(BIG_WORDS, "indented"));

        await switchBoxTo(page, FLUSH);
        expect(await boxOnPage(page)).toEqual(boxIn(FLUSH, "none"));

        await switchBoxTo(page, NORMAL);
        expect(await boxOnPage(page)).toEqual(boxIn(NORMAL, "indented"));
    });

    test("the saved book keeps each style's indent [Test Case ID 375]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        // Leaving a page saves the book.
        await goToPage(page, pages[1].id);
        await expect
            .poll(
                async () =>
                    Object.fromEntries(
                        Object.entries(
                            await readSavedParagraphIndents(page, bookFolder, [
                                NORMAL,
                                BIG_WORDS,
                                FLUSH,
                            ]),
                        ).map(([style, textIndent]) => [
                            style,
                            indentName(textIndent),
                        ]),
                    ),
                {
                    timeout: 30000,
                    message:
                        "the paragraph indent of each style in the saved book",
                },
            )
            .toEqual({
                [NORMAL]: "indented",
                [BIG_WORDS]: "indented",
                [FLUSH]: "none",
            });
    });
});
