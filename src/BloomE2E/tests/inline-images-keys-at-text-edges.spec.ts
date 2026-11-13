// Checks that Backspace at the very start of a text block's text, and Delete at the very end, leave
// the block's pictures alone (BL-16822).
//
// An inline image docked left, right or in the middle sits in the markup before the block's first
// paragraph, and one docked at the bottom sits after the last. The browser deletes such a picture
// when Backspace is pressed at the start of the paragraph after it, or Delete at the end of the
// paragraph before it. BloomField's PreventBackspaceAtStartFromRemovingParagraph treats a paragraph
// with only protected pictures (bloom-preventRemoval) before it as the first paragraph, worked out
// at each keystroke, so a picture inserted after the page was set up counts too, and
// PreventUnwantedDeletions does the same for Delete before a bottom-docked
// picture.
//
// Each test presses the key several times, as holding it down does, then types a letter. The letter
// lands only after the presses before it have been handled, so the check that follows sees what
// they did without a fixed wait.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    addInlineImage,
    changeInlineImagePicture,
    dragInlineImageToDock,
    getBlockText,
    getInlineImageInEveryLanguage,
    getInlineImages,
    putCaretAtEndOfBlockText,
    putCaretAtStartOfBlockText,
    typeAtCaretInBlock,
} from "../helpers/inlineImages";
import { pressKeys } from "../helpers/keys";

test.use({
    collectionSpec: {
        name: "inline-images-keys-at-text-edges",
        languages: ["en", "fr"],
    },
});

const BLOCK = ".bloom-translationGroup";
const LANG = "en";
const TEXT =
    "The kingfisher waits on the branch above the pool, still enough that the water forgets it " +
    "is there.";
// How many times each test presses the key, as a person holding it down would.
const PRESSES = 5;
// The letter typed after the presses, so that the check runs once they have been handled.
const MARK = "X";

let imageId: string;

/**
 * Make a book with a Just Text page, type on it, and insert a picture, which goes in docked at the
 * top right, and give it a picture. Each test makes its own, so that each runs whatever the other did.
 */
async function makePageWithTextAndPicture(
    page: import("@playwright/test").Page,
): Promise<void> {
    await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Just Text");
    await goToPage(page, (await getContentPages(page))[0].id);
    await typeInGroup(page, BLOCK, LANG, TEXT);
    imageId = await addInlineImage(page, BLOCK, LANG);
    // A placeholder hides its img and draws a flower instead, and a drag aims at the img.
    await changeInlineImagePicture(
        page,
        BLOCK,
        LANG,
        imageId,
        Path.resolve(
            Path.dirname(fileURLToPath(import.meta.url)),
            "..",
            "fixtures",
            "images",
            "bird.png",
        ),
    );
}

/** How many copies of the picture each language block holds, by language. */
async function copiesByLanguage(
    page: import("@playwright/test").Page,
): Promise<Record<string, number>> {
    return Object.fromEntries(
        (await getInlineImages(page, BLOCK)).map((block) => [
            block.languageTag,
            block.images.filter((image) => image.id === imageId).length,
        ]),
    );
}

test("Backspace held at the start of the text does not delete a picture docked at the top [Test Case ID 815]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    await step("Make a page with text and a picture", async () =>
        makePageWithTextAndPicture(page),
    );
    const before = await copiesByLanguage(page);
    expect(
        Object.values(before).every((count) => count === 1) &&
            Object.keys(before).length > 1,
        `Every language block should hold one copy of the new picture before the keys are ` +
            `pressed, or this test measures nothing. They hold: ${JSON.stringify(before)}.`,
    ).toBe(true);
    expect(
        (await getInlineImageInEveryLanguage(page, BLOCK, imageId))[0].dock,
        "A new picture should be docked at the top right, before the text in the markup.",
    ).toBe("right");

    await step(
        "Hold Backspace at the start of the text, then type a letter",
        async () => {
            await putCaretAtStartOfBlockText(page, BLOCK, LANG);
            await pressKeys(page, Array(PRESSES).fill("Backspace"));
            await typeAtCaretInBlock(page, BLOCK, LANG, MARK);
        },
    );

    expect(
        await copiesByLanguage(page),
        "Backspace at the start of the text deleted the picture before it, in some language.",
    ).toEqual(before);
    expect(
        await getBlockText(page, BLOCK, LANG),
        "Backspace at the start of the text changed the text; only the typed letter should " +
            "have been added, at the start.",
    ).toBe(MARK + TEXT);
});

test("Delete held at the end of the text does not delete a picture docked at the bottom [Test Case ID 815]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    await step("Make a page with text and a picture", async () =>
        makePageWithTextAndPicture(page),
    );
    await step("Dock the picture at the bottom", async () => {
        await dragInlineImageToDock(page, BLOCK, LANG, imageId, "bottom");
    });
    const before = await copiesByLanguage(page);
    expect(
        Object.values(before).every((count) => count === 1),
        `Every language block should still hold one copy of the picture before Delete is ` +
            `pressed. They hold: ${JSON.stringify(before)}.`,
    ).toBe(true);
    const textBefore = await getBlockText(page, BLOCK, LANG);
    expect(
        textBefore,
        "The block lost its text before the test began.",
    ).toContain("kingfisher");

    await step(
        "Hold Delete at the end of the text, then type a letter",
        async () => {
            await putCaretAtEndOfBlockText(page, BLOCK, LANG);
            await pressKeys(page, Array(PRESSES).fill("Delete"));
            await typeAtCaretInBlock(page, BLOCK, LANG, MARK);
        },
    );

    expect(
        await copiesByLanguage(page),
        "Delete at the end of the text deleted the picture docked at the bottom, in some language.",
    ).toEqual(before);
    expect(
        await getBlockText(page, BLOCK, LANG),
        "Delete at the end of the text changed the text; only the typed letter should have been " +
            "added, at the end.",
    ).toBe(textBefore + MARK);
});
