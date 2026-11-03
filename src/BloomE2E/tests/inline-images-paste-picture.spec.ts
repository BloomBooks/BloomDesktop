// Pasting a picture into a text block makes it an inline image in that block (BL-17014).
//
// Most programs that copy a picture also put text on the clipboard beside it: Bloom's own Copy
// Image puts the file's path there. Before BL-17014 a Ctrl+V in a text block was left to the
// browser and CKEditor, which pasted that text, so the person got a file path instead of their
// picture. Bloom's clipboard here holds both, as such a copy does. The paste is the event Ctrl+V
// produces (see pasteFromClipboard in helpers/clipboard.ts), so it goes through the page's own
// paste listener (inlineImagePaste.ts) and the round trip through C# that brings the picture
// into the book.

import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { copyToClipboard } from "../helpers/clipboard";
import {
    getBlockText,
    getInlineImageInEveryLanguage,
    getInlineImages,
    pastePictureIntoText,
    readSavedInlineImages,
} from "../helpers/inlineImages";

test.use({
    collectionSpec: { name: "inline-images-paste-picture", languages: ["en"] },
});

const BOOK_TITLE = "Pasted Picture";
const BLOCK = ".bloom-translationGroup";
const LANG = "en";
const TEXT =
    "The heron stands in the shallows and waits for the fish to forget it.";

const PICTURE = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

test("pasting a picture into a text block adds it as an inline image, not as its path", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
    await addPage(page, "Just Text");
    const [contentPage] = await getContentPages(page);
    await goToPage(page, contentPage.id);
    await typeInGroup(page, BLOCK, LANG, TEXT);
    const bookFolder = await findBookFolder(page, BOOK_TITLE);

    // sanity check: the block has its text and no inline image yet
    const blocksBefore = await getInlineImages(page, BLOCK);
    expect(blocksBefore.flatMap((block) => block.images)).toEqual([]);
    expect(await getBlockText(page, BLOCK, LANG)).toBe(TEXT);

    // The path goes beside the picture, as Bloom's own Copy Image puts it there.
    await copyToClipboard(page, { picture: PICTURE, text: PICTURE });
    const imageId = await pastePictureIntoText(page, BLOCK, LANG);

    // The path that came with the picture is not in the text, and the text is otherwise as it was.
    expect(await getBlockText(page, BLOCK, LANG)).toBe(TEXT);

    // One new image, in every language's block, docked where Insert Image puts one: on the right,
    // at the start of the block. Bloom leaves it selected, so a second paste would replace it.
    const copies = await getInlineImageInEveryLanguage(page, BLOCK, imageId);
    expect(copies).toHaveLength(blocksBefore.length);
    const shownCopy = copies.find((copy) => copy.languageTag === LANG)!;
    expect(shownCopy.dock).toBe("right");
    expect(shownCopy.slot.index).toBe(0);
    expect(shownCopy.selected).toBe(true);
    expect(shownCopy.shown).toBe(true);
    expect(
        shownCopy.aspectRatio,
        "The pasted picture's shape was never written on the image, so the picture did not load.",
    ).not.toBe("");
    const pictureFile = shownCopy.fileName;
    for (const copy of copies) expect(copy.fileName).toBe(pictureFile);
    expect(
        fs.existsSync(Path.join(bookFolder, pictureFile)),
        `The page shows ${pictureFile}, but Bloom did not put that file in the book folder.`,
    ).toBe(true);
    expect(
        (await getInlineImages(page, BLOCK)).flatMap((block) => block.images),
        "The paste made more than one inline image per language block.",
    ).toHaveLength(blocksBefore.length);

    // Bloom saves a page when the person leaves it, so going to another page saves this one.
    const [firstXmatter] = (await getPages(page)).filter(
        (one) => !one.isContentPage,
    );
    await goToPage(page, firstXmatter.id);
    const saved = (await readSavedInlineImages(page, bookFolder)).filter(
        (one) => one.id === imageId,
    );
    expect(
        saved.map((copy) => copy.languageTag).sort(),
        "The saved book does not have the pasted image in every language's block.",
    ).toEqual(copies.map((copy) => copy.languageTag).sort());
    for (const copy of saved)
        expect(decodeURIComponent(copy.source)).toBe(pictureFile);
});
