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
import {
    addPage,
    clickInGroup,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    reloadPageBeingEdited,
    setContentLanguages,
    typeInGroup,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    applyStyle,
    closeFormatDialog,
    createStyle,
    getStyleShown,
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
/** Every box starts with this style, as Bloom names it in the markup. */
const NORMAL = "normal";
/** A style Bloom offers in every book: its name in the markup, and in the dialog's menu. */
const BIG_WORDS = { markup: "BigWords", menu: "Big Words" };
/** The style the third test creates. A created style has the same name in both places. */
const CUSTOM = "Special";

/** The two content pages, once the first test has made them. */
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
    const shown = await getStyleShown(page);
    await closeFormatDialog(page);
    return shown;
};

/**
 * Expect the page's group to be in `style` in all three languages, and still to be after the page
 * is left and opened again (so the change was saved, not only drawn), and the Format dialog of
 * each of its boxes, not just the L1 one, to name `menuName`.
 */
const expectWholeGroupIn = async (
    page: Page,
    style: string,
    menuName: string,
) => {
    expect(await getGroupStyles(page)).toEqual(allLanguagesIn(style));
    await reloadPageBeingEdited(page);
    expect(await getGroupStyles(page)).toEqual(allLanguagesIn(style));
    for (const lang of LANGUAGES)
        expect(
            await styleShownFor(page, lang),
            `the style the Format dialog shows for the ${lang} box`,
        ).toBe(menuName);
};

test.describe("Style Change Applies to Group", () => {
    test("builds two pages, each with one group of text boxes in three languages", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await makeBookFromTemplate(page, "Basic Book");
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
        expect(await getStyleShown(page)).toBe("Normal");
        await applyStyle(page, BIG_WORDS.menu);
        await closeFormatDialog(page);

        await expectWholeGroupIn(page, BIG_WORDS.markup, BIG_WORDS.menu);
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
        expect(await getStyleShown(page)).toBe("Normal");
        await createStyle(page, CUSTOM);
        await closeFormatDialog(page);

        await expectWholeGroupIn(page, CUSTOM, CUSTOM);
        // The group on the other page kept the style the previous test gave it.
        await goToPage(page, pages[0].id);
        expect(await getGroupStyles(page)).toEqual(
            allLanguagesIn(BIG_WORDS.markup),
        );
    });
});
