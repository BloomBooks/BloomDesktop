// Formatting the text of speech bubbles on a Canvas page, the three ways Bloom offers: character
// formatting of selected words with the selection toolbar (bold, italic, underline); the Format
// dialog's Characters and Paragraph tabs, which change the bubbles' style, "Bubble", and so every
// bubble of that style, but not the ordinary text boxes of the book; and the Style Name tab, where
// a new style made on one bubble takes it out of the Bubble style, so that a change to the new
// style leaves the other bubble alone. Each change is checked on screen and in the saved book.
//
// Automates "Bubble Text Formatting" (Test Case ID 332), whose one line asks to "test character,
// paragraph, and style formatting for bubbles".
//
// The book is Basic Book with a "Just Text" page, whose one box is in the Normal style, and a
// Canvas page with two speech bubbles.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    readSavedCanvasTexts,
    readSavedStyleDeclarations,
} from "../helpers/bookHtml";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    reloadPageBeingEdited,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    canvasElementTextBox,
    clearCanvasElementSelection,
    dragPaletteItemOntoCanvas,
    selectCanvasElement,
    typeInCanvasElement,
} from "../helpers/canvasElements";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { cssColorToHex } from "../helpers/cssValues";
import {
    chooseAlignment,
    chooseFontSize,
    chooseIndent,
    chooseTextColor,
    closeFormatDialog,
    createStyle,
    getStyleIdShown,
    getTextBoxFormatting,
    openFormatDialogFromCanvasElementToolbar,
    showFormatDialogTab,
    type ITextBoxFormatting,
} from "../helpers/formatDialog";
import { watchForScriptErrors } from "../helpers/scriptErrors";
import {
    clickSelectionToolbarButton,
    getTextRunFormatting,
    selectTextIn,
} from "../helpers/selectionToolbar";

const L1 = "en";

test.use({
    collectionSpec: {
        name: "canvas-bubble-text-formatting",
        languages: [L1],
        // Below Pro, the Canvas tool's palette sits behind a subscription notice.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** The style Bloom gives the text of a new speech bubble. */
const BUBBLE = "Bubble";
/** The style of an ordinary text box. */
const NORMAL = "normal";
/** The style the test creates on the second bubble. Style names are letters only. */
const SHOUT = "Shout";

const FIRST_WORDS = "The goat ran home.";
const SECOND_WORDS = "The cow stayed.";
/** The word in the first bubble that is given character formatting. */
const FORMATTED_WORD = "goat";

/** What the Characters and Paragraph tabs are set to for the Bubble style. */
const BUBBLE_SIZE_PT = 24;
// A color from the picker's palette (helpers/formatDialog.ts refuses any other).
const BUBBLE_COLOR = "#ff1616";
const BUBBLE_ALIGNMENT = "right";
const BUBBLE_INDENT = "indented";
/** What the Characters tab then sets the new style's size to. */
const SHOUT_SIZE_PT = 32;

/** How the two bubbles' boxes and the Just Text page's box are formatted, measured on screen. */
interface IFormatting {
    first: ITextBoxFormatting;
    second: ITextBoxFormatting;
}

/** The two bubbles' boxes on the Canvas page being edited, in the order they were added. */
async function getBubbleFormatting(page: Page): Promise<IFormatting> {
    const boxes = await getTextBoxFormatting(page);
    // Sanity check: the Canvas page has no text but the two bubbles'.
    expect(
        boxes.map((b) => b.lang),
        "the language of each text box on the Canvas page",
    ).toEqual([L1, L1]);
    return { first: boxes[0], second: boxes[1] };
}

/** The one box of the Just Text page, which is in the Normal style. */
async function getJustTextFormatting(
    page: Page,
    pageId: string,
): Promise<ITextBoxFormatting> {
    await goToPage(page, pageId);
    const boxes = await getTextBoxFormatting(page);
    expect(boxes, "the text boxes of the Just Text page").toHaveLength(1);
    return boxes[0];
}

/** Select bubble `index` and open the Format dialog from the gear on its toolbar. */
async function openFormatDialogOnBubble(
    page: Page,
    index: number,
): Promise<void> {
    await clearCanvasElementSelection(page);
    await selectCanvasElement(page, index);
    await openFormatDialogFromCanvasElementToolbar(page);
}

/**
 * Select `word` in bubble `index`'s text and turn on one kind of formatting for it with the
 * selection toolbar, then wait until the word is drawn that way.
 *
 * The bubble is selected and the word selected afresh each time: a click on the selection
 * toolbar deselects the bubble, taking the focus and the selection with it, so the toolbar is gone
 * before a second button can be clicked (BL-17205). For the same reason the button's own on/off
 * state cannot be read after the click; the word's formatting is.
 */
async function formatWordInBubble(
    page: Page,
    index: number,
    word: string,
    button: "bold" | "italic" | "underline",
): Promise<void> {
    const box = canvasElementTextBox(page, index);
    await clearCanvasElementSelection(page);
    await selectCanvasElement(page, index);
    await selectTextIn(page, box, word);
    await clickSelectionToolbarButton(page, button);
    await expect
        .poll(async () => (await getTextRunFormatting(box, word))[button], {
            timeout: 15000,
            message: `Clicking the selection toolbar's ${button} button did not make "${word}" ${button}.`,
        })
        .toBe(true);
}

test("bubble text takes character, paragraph and style formatting [Test Case ID 332]", async ({
    page,
}) => {
    test.setTimeout(600000);
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Just Text");
    await addPage(page, "Canvas");
    const pages = await getContentPages(page);
    expect(pages).toHaveLength(2);
    const [justTextPage, canvasPage] = pages;

    await goToPage(page, justTextPage.id);
    await typeInGroup(page, ".bloom-translationGroup", L1, "Plain text.");
    const normalBefore = await getJustTextFormatting(page, justTextPage.id);
    expect(normalBefore.style).toBe(NORMAL);

    await goToPage(page, canvasPage.id);
    const first = await dragPaletteItemOntoCanvas(page, "speech", {
        xFraction: 0.3,
        yFraction: 0.25,
    });
    await selectCanvasElement(page, first);
    await typeInCanvasElement(page, FIRST_WORDS);
    await clearCanvasElementSelection(page);
    const second = await dragPaletteItemOntoCanvas(page, "speech", {
        xFraction: 0.6,
        yFraction: 0.7,
    });
    await selectCanvasElement(page, second);
    await typeInCanvasElement(page, SECOND_WORDS);

    const start = await getBubbleFormatting(page);
    // Sanity check what the rest stands on: both bubbles start in the Bubble style, and none of
    // the settings the test makes is there already.
    for (const box of [start.first, start.second]) {
        expect(box.style).toBe(BUBBLE);
        expect(box.fontSizePt).not.toBe(BUBBLE_SIZE_PT);
        expect(box.fontSizePt).not.toBe(SHOUT_SIZE_PT);
        expect(box.color).not.toBe(BUBBLE_COLOR);
        expect(box.alignment).not.toBe(BUBBLE_ALIGNMENT);
        expect(box.indent).not.toBe(BUBBLE_INDENT);
    }

    const errors = watchForScriptErrors(page);
    try {
        // Character formatting: the selection toolbar makes one selected word bold, italic and
        // underlined, and nothing else.
        const firstBox = canvasElementTextBox(page, first);
        for (const button of ["bold", "italic", "underline"] as const)
            await formatWordInBubble(page, first, FORMATTED_WORD, button);
        expect(
            await getTextRunFormatting(firstBox, FORMATTED_WORD),
        ).toMatchObject({ bold: true, italic: true, underline: true });
        for (const [box, plain] of [
            [firstBox, "The "],
            [firstBox, " ran home."],
            [canvasElementTextBox(page, second), SECOND_WORDS],
        ] as const)
            expect(
                await getTextRunFormatting(box, plain),
                `"${plain}", which was not selected`,
            ).toMatchObject({ bold: false, italic: false, underline: false });

        // The Format dialog's Characters and Paragraph tabs, opened from the first bubble, change
        // the Bubble style: both bubbles change, and the Normal box does not.
        await openFormatDialogOnBubble(page, first);
        expect(
            await getStyleIdShown(page),
            "the style the Format dialog shows for a bubble",
        ).toBe(BUBBLE);
        await showFormatDialogTab(page, "characters");
        await chooseFontSize(page, BUBBLE_SIZE_PT);
        await chooseTextColor(page, BUBBLE_COLOR);
        await showFormatDialogTab(page, "paragraph");
        await chooseAlignment(page, BUBBLE_ALIGNMENT);
        await chooseIndent(page, BUBBLE_INDENT);
        await closeFormatDialog(page);

        const bubbleSettings = {
            style: BUBBLE,
            fontSizePt: BUBBLE_SIZE_PT,
            color: BUBBLE_COLOR,
            alignment: BUBBLE_ALIGNMENT,
            indent: BUBBLE_INDENT,
        };
        const styled = await getBubbleFormatting(page);
        expect(styled.first, "the first bubble").toMatchObject(bubbleSettings);
        expect(
            styled.second,
            "the second bubble, which has the same style",
        ).toMatchObject(bubbleSettings);
        // The selected word kept its own formatting through the style change.
        expect(
            await getTextRunFormatting(firstBox, FORMATTED_WORD),
        ).toMatchObject({
            bold: true,
            italic: true,
            underline: true,
            color: BUBBLE_COLOR,
        });

        // Style formatting: a new style made on the second bubble takes it out of the Bubble
        // style, so a change to the new style reaches it alone.
        await openFormatDialogOnBubble(page, second);
        await showFormatDialogTab(page, "styleName");
        expect(await getStyleIdShown(page)).toBe(BUBBLE);
        await createStyle(page, SHOUT);
        await showFormatDialogTab(page, "characters");
        await chooseFontSize(page, SHOUT_SIZE_PT);
        await closeFormatDialog(page);

        const restyled = await getBubbleFormatting(page);
        expect(
            restyled.first,
            "the first bubble, which kept the Bubble style",
        ).toMatchObject(bubbleSettings);
        expect(
            restyled.second,
            "the second bubble, in the new style",
        ).toMatchObject({ style: SHOUT, fontSizePt: SHOUT_SIZE_PT });

        // All of it is saved: leaving the page writes the book. The page shown again has it too,
        // which also catches a rule for one language that the saved style rules below would miss.
        await reloadPageBeingEdited(page);
        const reloaded = await getBubbleFormatting(page);
        expect(
            reloaded.first,
            "the first bubble, after the page is opened again",
        ).toMatchObject(bubbleSettings);
        expect(
            reloaded.second,
            "the second bubble, after the page is opened again",
        ).toMatchObject({ style: SHOUT, fontSizePt: SHOUT_SIZE_PT });
        expect(
            await getTextRunFormatting(firstBox, FORMATTED_WORD),
            "the formatted word, after the page is opened again",
        ).toMatchObject({ bold: true, italic: true, underline: true });
        expect(
            await readSavedCanvasTexts(page, bookFolder, canvasPage.id, L1),
            "the bubbles' text in the saved book",
        ).toEqual([
            {
                style: BUBBLE,
                text: FIRST_WORDS,
                strong: [FORMATTED_WORD],
                em: [FORMATTED_WORD],
                u: [FORMATTED_WORD],
            },
            {
                style: SHOUT,
                text: SECOND_WORDS,
                strong: [],
                em: [],
                u: [],
            },
        ]);
        const savedBubble = await readSavedStyleDeclarations(
            page,
            bookFolder,
            BUBBLE,
        );
        expect(
            {
                fontSize: savedBubble["font-size"],
                color: savedBubble["color"]
                    ? cssColorToHex(savedBubble["color"])
                    : undefined,
                textAlign: savedBubble["text-align"],
                indented: parseFloat(savedBubble["text-indent"] ?? "0") > 0,
            },
            "the Bubble style's rules in the saved book",
        ).toEqual({
            fontSize: `${BUBBLE_SIZE_PT}pt`,
            color: BUBBLE_COLOR,
            // Bloom writes the Paragraph tab's Right as "end", which is right in left-to-right text.
            textAlign: "end",
            indented: true,
        });
        expect(
            (await readSavedStyleDeclarations(page, bookFolder, SHOUT))[
                "font-size"
            ],
            "the new style's font size in the saved book",
        ).toBe(`${SHOUT_SIZE_PT}pt`);

        // The Normal box on the other page was not touched by any of it.
        expect(
            await getJustTextFormatting(page, justTextPage.id),
            "the Just Text page's box, in the Normal style",
        ).toEqual(normalBefore);

        expect(
            errors.errors(),
            "Formatting the bubbles' text raised a script error.",
        ).toEqual([]);
    } finally {
        errors.stop();
    }
});
