// Changing a text box's style in a trilingual book changes the whole translation group: all three
// languages of the group take the new style, whether it is a style Bloom already has ("Big Words")
// or one the user creates on the spot, and the Format dialog then shows that style on every box of
// the group. Other groups keep theirs. Automates the manual test "Style Change Applies to Group"
// (Test Case ID 90), which guards BL-13031, where only the box that was being edited changed.
//
// The book is Basic Book with two "Just Text" pages, each one group of three boxes. Each page's
// group gets one of the two changes, made from its L1 box as the manual test asks. "Just Text"
// rather than a page with a picture: Big Words in three languages outgrows a small box, and its
// lower boxes then sit under the picture, where a click cannot reach them.
//
// The tests are serial: each starts from the state the one before it left behind.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { readSavedTextBoxStyles } from "../helpers/bookHtml";
import {
    addPage,
    clickInGroup,
    getContentPages,
    getShownPageId,
    goToPage,
    makeBookFromTemplate,
    reloadPageBeingEdited,
    setContentLanguages,
    typeInGroup,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    applyStyleById,
    closeFormatDialog,
    createStyle,
    getStyleIdShown,
    getTextBoxFormatting,
    openFormatDialog,
    scrollFormatGearIntoView,
    showFormatDialogTab,
} from "../helpers/formatDialog";

const L1 = "en";
const L2 = "fr";
const L3 = "es";
const LANGUAGES = [L1, L2, L3];

test.use({
    collectionSpec: {
        name: "style-change-applies-to-group",
        languages: LANGUAGES,
    },
});

test.describe.configure({ mode: "serial" });

const GROUP = ".bloom-translationGroup";
// The tests name styles by their ids, which the markup and the Format dialog's menu both use, so
// that nothing depends on the UI language the menu shows the names in.
/** Every box starts with this style. */
const NORMAL = "normal";
/** A style Bloom offers in every book, which its menu calls "Big Words". */
const BIG_WORDS = "BigWords";
/** The style the third test creates. */
const CUSTOM = "Special";

/** The book, and its two content pages, once the first test has made them. */
let bookFolder: string;
let pages: IBookPage[];

/** The style of each language's box in the one group of the page being shown, e.g. {en: "normal"}. */
const getGroupStyles = async (page: Page): Promise<Record<string, string>> => {
    const boxes = await getTextBoxFormatting(page);
    // Sanity check: a "Just Text" page has one group.
    expect(boxes.map((b) => b.group)).toEqual(LANGUAGES.map(() => 0));
    return Object.fromEntries(boxes.map((b) => [b.lang, b.style]));
};

/** The same style for every language: what a whole group in that style looks like. */
const allLanguagesIn = (style: string): Record<string, string> =>
    Object.fromEntries(LANGUAGES.map((lang) => [lang, style]));

/** Open the Format dialog on one language's box of the page's group, on the Style Name tab. */
const openStyleNameTabOn = async (page: Page, lang: string) => {
    await clickInGroup(page, GROUP, lang);
    await scrollFormatGearIntoView(page);
    await openFormatDialog(page);
    await showFormatDialogTab(page, "styleName");
};

/** The style the Format dialog shows for one language's box. Leaves the dialog closed. */
const styleShownFor = async (page: Page, lang: string): Promise<string> => {
    await openStyleNameTabOn(page, lang);
    const shown = await getStyleIdShown(page);
    await closeFormatDialog(page);
    return shown;
};

/**
 * Expect the page's group to be in `style` in all three languages: on screen; in the book file once
 * the page is left, which saves it; on screen again when the page is opened again; and in the
 * Format dialog of each of its boxes, not just the L1 one.
 */
const expectWholeGroupIn = async (page: Page, style: string) => {
    expect(await getGroupStyles(page)).toEqual(allLanguagesIn(style));
    const pageId = (await getShownPageId(page))!;
    await reloadPageBeingEdited(page);
    expect(
        await readSavedTextBoxStyles(page, bookFolder, pageId),
        "the styles of the page's boxes in the saved book",
    ).toEqual(allLanguagesIn(style));
    expect(await getGroupStyles(page)).toEqual(allLanguagesIn(style));
    for (const lang of LANGUAGES)
        expect(
            await styleShownFor(page, lang),
            `the style the Format dialog shows for the ${lang} box`,
        ).toBe(style);
};

test.describe("Style Change Applies to Group", () => {
    test("builds two pages, each with one group of text boxes in three languages", async ({
        page,
    }) => {
        test.setTimeout(600000);
        bookFolder = await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Just Text", 2);
        await setContentLanguages(page, LANGUAGES);
        pages = await getContentPages(page);
        expect(pages).toHaveLength(2);

        for (const bookPage of pages) {
            await goToPage(page, bookPage.id);
            for (const lang of LANGUAGES)
                await typeInGroup(page, GROUP, lang, `Some ${lang} text.`);
            // Sanity check the starting point: all three languages showing, all Normal.
            expect(await getGroupStyles(page)).toEqual(allLanguagesIn(NORMAL));
        }
    });

    test("choosing Big Words for the L1 box changes all three languages of its group [Test Case ID 90]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await goToPage(page, pages[0].id);
        await openStyleNameTabOn(page, L1);
        expect(await getStyleIdShown(page)).toBe(NORMAL);
        await applyStyleById(page, BIG_WORDS);
        await closeFormatDialog(page);

        await expectWholeGroupIn(page, BIG_WORDS);
        // The group on the other page kept its style.
        await goToPage(page, pages[1].id);
        expect(await getGroupStyles(page)).toEqual(allLanguagesIn(NORMAL));
    });

    test("creating a new style on the L1 box gives it to all three languages of its group [Test Case ID 90]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await goToPage(page, pages[1].id);
        await openStyleNameTabOn(page, L1);
        expect(await getStyleIdShown(page)).toBe(NORMAL);
        await createStyle(page, CUSTOM);
        await closeFormatDialog(page);

        await expectWholeGroupIn(page, CUSTOM);
        // The group on the other page kept the style the previous test gave it.
        await goToPage(page, pages[0].id);
        expect(await getGroupStyles(page)).toEqual(allLanguagesIn(BIG_WORDS));
    });
});
