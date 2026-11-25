// A word that was given bold, italic, underline and a color with the selection toolbar keeps all
// of that when it is copied and pasted into another text box, and still has it after the book
// has saved the page and shown it again. The color was once lost on the way (BL-12357): at first
// sight the paste looked right, and the color went when the page was left.
// Automates the manual test "Copy Formatted Word" (Test Case ID 381).
//
// The keys pressed are the real Ctrl+C and Ctrl+V, because the copy and the paste are what this
// test measures: what the browser puts on the clipboard for the word, and what CKEditor's paste
// filter and Bloom's own paste handling leave of it.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { pressKey } from "../helpers/keys";
import {
    chooseSelectionTextColor,
    getTextRunFormatting,
    selectLastWordIn,
    toggleSelectionFormatting,
    type ITextRunFormatting,
} from "../helpers/selectionToolbar";

test.use({
    collectionSpec: { name: "copy-formatted-word", languages: ["en"] },
});

const L1 = "en";
const GROUP = ".bloom-translationGroup";
const PLAIN_WORDS = "Copy this ";
/**
 * The word that is formatted and copied. It differs from run to run, so that the paste can only
 * bring it if this run's copy worked: the Windows clipboard keeps what an earlier run copied.
 */
const WORD = `word${Date.now().toString(36)}`;
/** A color from the text palette. */
const RED = "#ff1616";

const FORMATTED: ITextRunFormatting = {
    bold: true,
    italic: true,
    underline: true,
    color: RED,
};

test("a word given bold, italic, underline and a color keeps them when copied to another box [Test Case ID 381]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    await step("Make a book with two pages of text", async () => {
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Just Text", 2);
    });
    const [sourcePage, destinationPage] = await getContentPages(page);

    await step(
        "Type some words, select the last one, and make it bold, italic, underlined and red",
        async () => {
            await goToPage(page, sourcePage.id);
            await typeInGroup(page, GROUP, L1, PLAIN_WORDS + WORD);
            const box = await clickInGroup(page, GROUP, L1);
            const plain = await getTextRunFormatting(box, WORD);
            // Sanity check: the word starts with none of the formatting it is about to get.
            expect(plain).toMatchObject({
                bold: false,
                italic: false,
                underline: false,
            });
            expect(plain.color).not.toBe(RED);

            expect(await selectLastWordIn(page, box)).toBe(WORD);
            await toggleSelectionFormatting(page, "bold");
            await toggleSelectionFormatting(page, "italic");
            await toggleSelectionFormatting(page, "underline");
            await chooseSelectionTextColor(page, RED);
            expect(
                await getTextRunFormatting(box, WORD),
                "the word in the box it was formatted in",
            ).toEqual(FORMATTED);
            // Only the word was formatted, not the words before it.
            expect(
                await getTextRunFormatting(box, PLAIN_WORDS.trim()),
                "the words before the formatted word",
            ).toEqual(plain);
        },
    );

    await step("Copy the word", async () => {
        const box = await clickInGroup(page, GROUP, L1);
        expect(await selectLastWordIn(page, box)).toBe(WORD);
        await pressKey(page, "Control+c");
    });

    await step("Paste it into the text box of the other page", async () => {
        await goToPage(page, destinationPage.id);
        const box = await clickInGroup(page, GROUP, L1);
        // Sanity check: the box is empty, so whatever it holds next came from the paste.
        await expect(box).toHaveText("", { useInnerText: true });
        await pressKey(page, "Control+v");
        await expect(box, "The paste did not arrive.").toHaveText(WORD, {
            timeout: 15000,
            useInnerText: true,
        });
    });

    await step(
        "Check that the pasted word looks as the copied one did",
        async () => {
            expect(
                await getTextRunFormatting(
                    await clickInGroup(page, GROUP, L1),
                    WORD,
                ),
                "the pasted word",
            ).toEqual(FORMATTED);
        },
    );

    await step(
        "Leave the page and come back, and check both words again",
        async () => {
            await goToPage(page, sourcePage.id);
            expect(
                await getTextRunFormatting(
                    await clickInGroup(page, GROUP, L1),
                    WORD,
                ),
                "the copied word, after its page was saved and shown again",
            ).toEqual(FORMATTED);
            await goToPage(page, destinationPage.id);
            const box = await clickInGroup(page, GROUP, L1);
            await expect(box).toHaveText(WORD, { useInnerText: true });
            expect(
                await getTextRunFormatting(box, WORD),
                "the pasted word, after its page was saved and shown again",
            ).toEqual(FORMATTED);
        },
    );
});
