// Every control of the Format dialog, on all four of its tabs, and where each change lands: on the
// text of the box at once, on every box of the same style on every page, in one language or in all
// of them, and in the Talking Book tool's highlight. Also creating a style and applying it.
// Automates the manual test "All Format Controls" (Test Case ID 357).
//
// The book is Basic Book with three pages, "Basic Text & Image", "Image in Middle" and "Image on
// Bottom", which between them have four text boxes, and the collection has two languages, so once
// both are showing there are eight boxes. Which of them a change reaches is the heart of the test
// (see the header of helpers/formatDialog.ts for the rules).
//
// The tests are serial: each starts from the state the one before it left behind.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    setContentLanguages,
    typeInGroup,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    applyStyle,
    chooseAlignment,
    chooseFont,
    chooseFontSize,
    chooseHighlightBackgroundColor,
    chooseHighlightTextColor,
    chooseIndent,
    chooseLineSpacing,
    chooseParagraphSpacing,
    chooseTextColor,
    chooseWordSpacing,
    closeFormatDialog,
    createStyle,
    getFontSizeChoices,
    getFontSizeShown,
    getStyleMenuEntries,
    getTextBoxFormatting,
    getTextBoxFormattingOnPages,
    openFormatDialog,
    scrollFormatGearIntoView,
    switchFormatDialogTab,
    toggleEmphasis,
    type IBookTextBoxFormatting,
    type ITextBoxFormatting,
} from "../helpers/formatDialog";
import { getCurrentAudioHighlightColors } from "../helpers/talkingBook";
import { getOpenToolName, hideToolbox, showToolbox } from "../helpers/toolbox";

test.use({
    collectionSpec: { name: "all-format-controls", languages: ["en", "fr"] },
});

test.describe.configure({ mode: "serial" });

const L1 = "en";
const L2 = "fr";
/** Every text box on these pages is in a translation group; the tests pick a group by index. */
const GROUP = ".bloom-translationGroup";
/** The pages the manual test asks for, by the labels the Add Page dialog shows. */
const PAGE_LABELS = [
    "Basic Text & Image",
    "Image in Middle",
    "Image on Bottom",
];
/** Those pages have one, two and one text boxes: four per language. */
const BOXES_PER_LANGUAGE = 4;
/** The style every box starts with, as Bloom names it in the markup. The menu shows "Normal". */
const NORMAL = "normal";
/** The style the manual test creates. */
const TESTING = "Testing";
/** The collection's font, which every box starts with. */
const DEFAULT_FONT = "Andika";

// Colors from the pickers' palettes, as "#rrggbb" (helpers/formatDialog.ts refuses any other).
const RED = "#ff1616";
const TEAL = "#03989e";
const PURPLE = "#8c52ff";
const PALE_GREEN = "#bbf4bb";

/** What the Characters tab is set to on the L1 box in the first test that changes it. */
const L1_CHARACTERS = {
    fontFamily: "Arial",
    fontSizePt: 20,
    lineSpacing: 2,
    wordSpacingPt: 5,
    bold: true,
    italic: true,
    underline: true,
    color: RED,
};
/** What the Characters tab is then set to on the L2 box: every control different from L1's. */
const L2_CHARACTERS = {
    fontFamily: "Times New Roman",
    fontSizePt: 14,
    lineSpacing: 1.2,
    wordSpacingPt: 10,
    bold: false,
    italic: false,
    underline: false,
    color: TEAL,
};
/** What the Paragraph tab is set to, which is per style and so reaches both languages. */
const PARAGRAPH = {
    indent: "indented",
    alignment: "center",
    paragraphSpacingEm: 1,
} as const;
/** How the Testing style is changed after it is created. */
const TESTING_CHANGES = {
    fontSizePt: 24,
    italic: false,
    alignment: "right",
    indent: "none",
} as const;

/** The content pages, in order, once the first test has made them. */
let pages: IBookPage[];

/** The fields of a box's formatting that describe how it looks, without where it is. */
const looksOf = (box: ITextBoxFormatting) => {
    const { group, lang, style, ...looks } = box;
    void group;
    void lang;
    void style;
    return looks;
};

const boxesInLanguage = (boxes: IBookTextBoxFormatting[], lang: string) =>
    boxes.filter((b) => b.lang === lang);

/** Expect the eight boxes of the book: four per language. */
const expectEightBoxes = (boxes: IBookTextBoxFormatting[]) => {
    expect(boxes).toHaveLength(2 * BOXES_PER_LANGUAGE);
    expect(boxesInLanguage(boxes, L1)).toHaveLength(BOXES_PER_LANGUAGE);
    expect(boxesInLanguage(boxes, L2)).toHaveLength(BOXES_PER_LANGUAGE);
};

/** Open the Format dialog on one box of the page being shown, on the tab wanted. */
const openFormatDialogOn = async (
    page: Parameters<typeof clickInGroup>[0],
    lang: string,
    groupIndex: number,
    tab: Parameters<typeof switchFormatDialogTab>[1],
) => {
    await clickInGroup(page, GROUP, lang, groupIndex);
    await scrollFormatGearIntoView(page);
    await openFormatDialog(page);
    await switchFormatDialogTab(page, tab);
};

test.describe("All Format Controls", () => {
    test("builds a book with four text boxes on three pages, in two languages", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await makeBookFromTemplate(page, "Basic Book");
        for (const label of PAGE_LABELS) await addPage(page, label);
        await setContentLanguages(page, [L1, L2]);
        pages = await getContentPages(page);
        expect(pages).toHaveLength(PAGE_LABELS.length);

        // Put two short paragraphs in every box, so that there is text to format and to highlight,
        // and a paragraph that the space-between-paragraphs setting can show under.
        for (const bookPage of pages) {
            await goToPage(page, bookPage.id);
            for (const box of await getTextBoxFormatting(page))
                await typeInGroup(
                    page,
                    GROUP,
                    box.lang,
                    `Some ${box.lang} text.\nMore ${box.lang} text.`,
                    box.group,
                );
        }

        const boxes = await getTextBoxFormattingOnPages(page, pages);
        expectEightBoxes(boxes);
        for (const box of boxes) expect(box.style).toBe(NORMAL);
        // Sanity check the starting point the rest of the file measures changes from.
        for (const box of boxes)
            expect(box).toMatchObject({
                fontFamily: DEFAULT_FONT,
                bold: false,
                italic: false,
                underline: false,
                alignment: "left",
                indent: "none",
            });
    });

    test("each Characters-tab control takes effect at once on the L1 box, and reaches all eight boxes [Test Case ID 357]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await goToPage(page, pages[0].id);
        await openFormatDialogOn(page, L1, 0, "Characters");
        const boxOnPage = async (lang: string) =>
            (await getTextBoxFormatting(page)).find((b) => b.lang === lang)!;

        // Each change shows on the box straight away, with the dialog still open.
        await chooseFont(page, L1_CHARACTERS.fontFamily);
        expect((await boxOnPage(L1)).fontFamily).toBe(L1_CHARACTERS.fontFamily);
        await chooseFontSize(page, L1_CHARACTERS.fontSizePt);
        expect((await boxOnPage(L1)).fontSizePt).toBe(L1_CHARACTERS.fontSizePt);
        await chooseLineSpacing(page, "2.0");
        expect((await boxOnPage(L1)).lineSpacing).toBe(
            L1_CHARACTERS.lineSpacing,
        );
        await chooseWordSpacing(page, "Wide");
        expect((await boxOnPage(L1)).wordSpacingPt).toBe(
            L1_CHARACTERS.wordSpacingPt,
        );
        expect(await toggleEmphasis(page, "bold")).toBe(true);
        expect((await boxOnPage(L1)).bold).toBe(true);
        expect(await toggleEmphasis(page, "italic")).toBe(true);
        expect((await boxOnPage(L1)).italic).toBe(true);
        expect(await toggleEmphasis(page, "underline")).toBe(true);
        expect((await boxOnPage(L1)).underline).toBe(true);
        await chooseTextColor(page, L1_CHARACTERS.color);
        expect((await boxOnPage(L1)).color).toBe(L1_CHARACTERS.color);
        await closeFormatDialog(page);

        // A change made on an L1 box is for the style as a whole, so every box in both
        // languages, on every page, shows it. Font is the exception: it stays per language.
        const boxes = await getTextBoxFormattingOnPages(page, pages);
        expectEightBoxes(boxes);
        const { fontFamily, ...forEveryLanguage } = L1_CHARACTERS;
        for (const box of boxesInLanguage(boxes, L1))
            expect(box, `L1 box on page ${box.pageCaption}`).toMatchObject(
                L1_CHARACTERS,
            );
        for (const box of boxesInLanguage(boxes, L2))
            expect(box, `L2 box on page ${box.pageCaption}`).toMatchObject({
                ...forEveryLanguage,
                fontFamily: DEFAULT_FONT,
            });
        void fontFamily;
    });

    test("a font size typed into the size box changes all eight boxes, shows in the control, and joins its list [Test Case ID 357]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        const customSize = 17;
        await goToPage(page, pages[1].id);
        await openFormatDialogOn(page, L1, 0, "Characters");
        expect(
            await getFontSizeChoices(page),
            "the size chosen must not already be in the list",
        ).not.toContain(customSize);

        await chooseFontSize(page, customSize);

        const onPage = await getTextBoxFormatting(page);
        expect(
            onPage.find((b) => b.lang === L1 && b.group === 0)!.fontSizePt,
        ).toBe(customSize);
        expect(await getFontSizeShown(page)).toBe(customSize);
        expect(await getFontSizeChoices(page)).toContain(customSize);
        await closeFormatDialog(page);

        const boxes = await getTextBoxFormattingOnPages(page, pages);
        expectEightBoxes(boxes);
        for (const box of boxes)
            expect(
                box.fontSizePt,
                `${box.lang} box on page ${box.pageCaption}`,
            ).toBe(customSize);
    });

    test("changes made on the L2 box change the four L2 boxes and leave the L1 boxes as they were [Test Case ID 357]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        const before = await getTextBoxFormattingOnPages(page, pages);
        await goToPage(page, pages[0].id);
        await openFormatDialogOn(page, L2, 0, "Characters");
        await chooseFont(page, L2_CHARACTERS.fontFamily);
        await chooseFontSize(page, L2_CHARACTERS.fontSizePt);
        await chooseLineSpacing(page, "1.2");
        await chooseWordSpacing(page, "Extra Wide");
        // The L1 changes turned all three on for every language, so each click turns one off.
        expect(await toggleEmphasis(page, "bold")).toBe(false);
        expect(await toggleEmphasis(page, "italic")).toBe(false);
        expect(await toggleEmphasis(page, "underline")).toBe(false);
        await chooseTextColor(page, L2_CHARACTERS.color);
        await closeFormatDialog(page);

        const after = await getTextBoxFormattingOnPages(page, pages);
        expectEightBoxes(after);
        for (const box of boxesInLanguage(after, L2))
            expect(box, `L2 box on page ${box.pageCaption}`).toMatchObject(
                L2_CHARACTERS,
            );
        expect(boxesInLanguage(after, L1)).toEqual(boxesInLanguage(before, L1));
    });

    test("Paragraph-tab changes reach all eight boxes [Test Case ID 357]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await goToPage(page, pages[0].id);
        await openFormatDialogOn(page, L2, 0, "Paragraph");
        await chooseIndent(page, PARAGRAPH.indent);
        await chooseAlignment(page, PARAGRAPH.alignment);
        await chooseParagraphSpacing(page, "1");
        await closeFormatDialog(page);

        const boxes = await getTextBoxFormattingOnPages(page, pages);
        expectEightBoxes(boxes);
        for (const box of boxes)
            expect(
                box,
                `${box.lang} box on page ${box.pageCaption}`,
            ).toMatchObject(PARAGRAPH);
    });

    test("the Highlighting tab's colors are the ones the Talking Book tool highlights with [Test Case ID 357]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await goToPage(page, pages[2].id);
        await openFormatDialogOn(page, L1, 0, "Highlighting");
        await chooseHighlightBackgroundColor(page, PALE_GREEN);
        await chooseHighlightTextColor(page, PURPLE);
        await closeFormatDialog(page);

        await showToolbox(page);
        expect(await getOpenToolName(page)).toBe("Talking Book Tool");
        expect(await getCurrentAudioHighlightColors(page)).toEqual({
            background: PALE_GREEN,
            text: PURPLE,
        });
        await hideToolbox(page);
    });

    test("creating a style keeps the box's look, applies to both languages of its group only, and joins the menu [Test Case ID 357]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await goToPage(page, pages[1].id);
        const before = await getTextBoxFormatting(page);
        // Sanity check: this page has two groups, and every box is still Normal.
        expect(before.map((b) => b.group)).toEqual([0, 0, 1, 1]);
        for (const box of before) expect(box.style).toBe(NORMAL);

        await openFormatDialogOn(page, L1, 0, "Style Name");
        expect(await getStyleMenuEntries(page)).not.toContain(TESTING);
        await createStyle(page, TESTING);

        expect(await getStyleMenuEntries(page)).toContain(TESTING);
        const after = await getTextBoxFormatting(page);
        const byBox = (
            boxes: ITextBoxFormatting[],
            group: number,
            lang: string,
        ) => boxes.find((b) => b.group === group && b.lang === lang)!;
        // The box, and its partner in the other language, are now Testing; the other group is not.
        expect(byBox(after, 0, L1).style).toBe(TESTING);
        expect(byBox(after, 0, L2).style).toBe(TESTING);
        expect(byBox(after, 1, L1).style).toBe(NORMAL);
        expect(byBox(after, 1, L2).style).toBe(NORMAL);
        // The new style copied the box's settings, font included (the box's font was set
        // explicitly on the Characters tab, so it belongs to the style), so the box looks as it did.
        expect(looksOf(byBox(after, 0, L1))).toEqual(
            looksOf(byBox(before, 0, L1)),
        );
        // The other group on the page did not change at all.
        expect(looksOf(byBox(after, 1, L1))).toEqual(
            looksOf(byBox(before, 1, L1)),
        );
        expect(looksOf(byBox(after, 1, L2))).toEqual(
            looksOf(byBox(before, 1, L2)),
        );
    });

    test("changing the Testing style changes only the Testing boxes [Test Case ID 357]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await closeFormatDialog(page);
        const before = await getTextBoxFormattingOnPages(page, pages);
        await goToPage(page, pages[1].id);
        await openFormatDialogOn(page, L1, 0, "Characters");
        await chooseFontSize(page, TESTING_CHANGES.fontSizePt);
        expect(await toggleEmphasis(page, "italic")).toBe(
            TESTING_CHANGES.italic,
        );
        await switchFormatDialogTab(page, "Paragraph");
        await chooseAlignment(page, TESTING_CHANGES.alignment);
        await chooseIndent(page, TESTING_CHANGES.indent);
        await closeFormatDialog(page);

        const after = await getTextBoxFormattingOnPages(page, pages);
        expectEightBoxes(after);
        const testingBoxes = after.filter((b) => b.style === TESTING);
        expect(testingBoxes.map((b) => b.lang).sort()).toEqual([L1, L2]);
        for (const box of testingBoxes)
            expect(box, `${box.lang} Testing box`).toMatchObject(
                TESTING_CHANGES,
            );
        expect(after.filter((b) => b.style === NORMAL)).toEqual(
            before.filter((b) => b.style === NORMAL),
        );
    });

    test("applying Testing to the L2 box of another page changes both languages of that group [Test Case ID 357]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await goToPage(page, pages[2].id);
        const before = await getTextBoxFormatting(page);
        for (const box of before) expect(box.style).toBe(NORMAL);

        await openFormatDialogOn(page, L2, 0, "Style Name");
        await applyStyle(page, TESTING);
        await closeFormatDialog(page);

        const after = await getTextBoxFormatting(page);
        expect(after).toHaveLength(2);
        for (const box of after) {
            expect(box.style, `${box.lang} box`).toBe(TESTING);
            expect(box, `${box.lang} box`).toMatchObject(TESTING_CHANGES);
        }
    });
});
