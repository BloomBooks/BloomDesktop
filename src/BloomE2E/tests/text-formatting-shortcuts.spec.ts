// Character formatting in a text box: bold, italic, underline, superscript and text color, put on
// words through the formatting toolbar and through Ctrl+B, Ctrl+I and Ctrl+U; taken off again with
// Ctrl+Space and the Remove Formatting button; and put back by undo. Automates the manual test
// "Text Formatting Shortcuts" (Test Case ID 364).
//
// The tests are serial: each starts from the state the one before it left behind, and the second
// one builds the formatted text that the later ones take away and restore.
//
// The manual test undoes twice: once with the top bar's Undo button, which the test clicks for
// real, and once with Ctrl+Z, which is a WinForms accelerator the shell handles before the browser
// sees it, so no test can press it. For that step the test calls helpers/workspace.ts `undo`, the
// production undo path with only the key press missing (AUTOMATION-DEBT.md: "WinForms surfaces
// are invisible to CDP").

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeParagraphsInGroup,
} from "../helpers/bookMaking";
import {
    clickFormatButton,
    expectFormatting,
    getFormattedRuns,
    paragraphTextsOf,
    pickTextColorFromToolbar,
    pressFormatShortcut,
    selectAllInGroup,
    selectTextInGroup,
    PLAIN,
    type IFormatting,
} from "../helpers/textFormatting";
import { clickUndoButton, undo } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "text-formatting-shortcuts", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

/** The one text box on the page this file builds: the box under the picture. */
const TEXT_BOX = ".bloom-translationGroup";
const LANGUAGE = "en";

/** Two paragraphs, every word different, so that each word names one place in the text. */
const PARAGRAPHS = [
    "The quick brown fox jumps over the lazy dog.",
    "Pack my box with five dozen liquor jugs.",
];

/** A color from Bloom's text palette (TextColorPalette in bloomPalette.ts). */
const RED = "#ff1616";

/**
 * How each formatted word gets its formatting, and what it should have afterwards. Between them the
 * words use every toolbar button and every shortcut, singly and in layers, in both paragraphs.
 */
const FORMATTED_WORDS: {
    word: string;
    apply: (page: Page) => Promise<void>;
    expected: IFormatting;
}[] = [
    {
        word: "quick",
        apply: (page) => clickFormatButton(page, "bold"),
        expected: { ...PLAIN, bold: true },
    },
    {
        word: "brown",
        apply: (page) => pressFormatShortcut(page, "italic"),
        expected: { ...PLAIN, italic: true },
    },
    {
        word: "fox",
        apply: (page) => clickFormatButton(page, "underline"),
        expected: { ...PLAIN, underline: true },
    },
    {
        word: "jumps",
        apply: (page) => clickFormatButton(page, "superscript"),
        expected: { ...PLAIN, superscript: true },
    },
    {
        word: "lazy",
        apply: (page) => pickTextColorFromToolbar(page, RED),
        expected: { ...PLAIN, color: RED },
    },
    {
        word: "Pack",
        apply: async (page) => {
            await pressFormatShortcut(page, "bold");
            await clickFormatButton(page, "underline");
        },
        expected: { ...PLAIN, bold: true, underline: true },
    },
    {
        word: "dozen",
        apply: async (page) => {
            await clickFormatButton(page, "italic");
            await pressFormatShortcut(page, "bold");
            await pressFormatShortcut(page, "underline");
        },
        expected: { ...PLAIN, bold: true, italic: true, underline: true },
    },
    {
        word: "liquor",
        apply: async (page) => {
            await pickTextColorFromToolbar(page, RED);
            await clickFormatButton(page, "superscript");
        },
        expected: { ...PLAIN, superscript: true, color: RED },
    },
];

/** Words that are never formatted, one per paragraph, to show that formatting stays where it was put. */
const PLAIN_WORDS = ["over", "with"];

/** Assert that every word has the formatting the table above gives it, and the plain words none. */
const expectAllFormattingInPlace = async (page: Page) => {
    for (const { word, expected } of FORMATTED_WORDS)
        await expectFormatting(page, TEXT_BOX, LANGUAGE, word, expected);
    for (const word of PLAIN_WORDS)
        await expectFormatting(page, TEXT_BOX, LANGUAGE, word, PLAIN);
};

/** Assert that formatting has changed nothing about the words themselves. */
const expectTextUnchanged = async (page: Page) => {
    expect(
        paragraphTextsOf(await getFormattedRuns(page, TEXT_BOX, LANGUAGE)),
    ).toEqual(PARAGRAPHS);
};

test.describe("Text formatting shortcuts", () => {
    test("builds a book with a text box holding two paragraphs", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Basic Text & Image");
        const [textPage] = await getContentPages(page);
        await goToPage(page, textPage.id);
        await typeParagraphsInGroup(page, TEXT_BOX, LANGUAGE, PARAGRAPHS);

        // Sanity check the state the rest of the file rests on: two paragraphs, no formatting.
        const runs = await getFormattedRuns(page, TEXT_BOX, LANGUAGE);
        expect(paragraphTextsOf(runs)).toEqual(PARAGRAPHS);
        expect(runs.map((run) => run.formatting)).toEqual([PLAIN, PLAIN]);
    });

    test("the toolbar buttons and Ctrl+B/I/U format the selected words, singly and in layers [Test Case ID 364]", async ({
        page,
    }) => {
        for (const { word, apply } of FORMATTED_WORDS) {
            await selectTextInGroup(page, TEXT_BOX, LANGUAGE, word);
            await apply(page);
        }
        await expectAllFormattingInPlace(page);
        await expectTextUnchanged(page);
    });

    test("Ctrl+A then Ctrl+Space removes all the formatting, and the Undo button puts it all back [Test Case ID 364]", async ({
        page,
    }) => {
        await selectAllInGroup(page, TEXT_BOX, LANGUAGE);
        await pressFormatShortcut(page, "removeFormat");
        for (const { word } of FORMATTED_WORDS)
            await expectFormatting(page, TEXT_BOX, LANGUAGE, word, PLAIN);
        await expectTextUnchanged(page);

        await clickUndoButton(page);
        await expectAllFormattingInPlace(page);
        await expectTextUnchanged(page);
    });

    test("the Remove Formatting button clears only the selected text, and undo (Ctrl+Z) puts it back [Test Case ID 364]", async ({
        page,
    }) => {
        // Two formatted words and the space between them: a selection that crosses formatting.
        await selectTextInGroup(page, TEXT_BOX, LANGUAGE, "brown fox");
        await clickFormatButton(page, "removeFormat");
        await expectFormatting(page, TEXT_BOX, LANGUAGE, "brown fox", PLAIN);
        for (const { word, expected } of FORMATTED_WORDS.filter(
            (f) => f.word !== "brown" && f.word !== "fox",
        ))
            await expectFormatting(page, TEXT_BOX, LANGUAGE, word, expected);
        await expectTextUnchanged(page);

        // The manual step is Ctrl+Z; see the note at the top of this file.
        await undo(page);
        await expectAllFormattingInPlace(page);
        await expectTextUnchanged(page);
    });
});
