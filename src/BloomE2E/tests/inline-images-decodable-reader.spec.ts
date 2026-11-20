// Checks that an inline image's menu still works with the Decodable Reader tool open (BL-16822).
//
// The reader tools mark up the text, and when the text loses the focus they tidy it with
// EditableDivUtils.doCkEditorCleanup, which put CKEditor's copy of the text back into the block.
// That rebuilt the inline image wrappers too, while the picture's menu, and the Image Chooser the
// menu opens, still held the old ones. So "Choose image from your computer..." changed a picture
// that was no longer on the page, and Delete deleted nothing. safelyReplaceContentWithCkEditorData
// now keeps the live wrappers. Clicking a menu command takes the focus from the text, so these
// tests go through the real menu and the real Image Chooser, which is the only route that holds a
// wrapper across that moment.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    addInlineImage,
    changeInlineImagePicture,
    chooseInlineImagePictureFromMenu,
    deleteInlineImage,
    getBlockText,
    getInlineImageInEveryLanguage,
    getInlineImages,
    resizeInlineImage,
    typeAtCaretInBlock,
    typeAtEndOfBlock,
} from "../helpers/inlineImages";
import { pressKey } from "../helpers/keys";
import { waitForUndoAvailableFrom } from "../helpers/undo";
import {
    expandReaderTool,
    makeBasicBookWithReaderTool,
    type ReaderTool,
} from "../helpers/readerTools";

test.use({
    collectionSpec: {
        name: "inline-images-decodable-reader",
        languages: ["en", "fr"],
    },
});

const BLOCK = ".bloom-translationGroup";
const LANG = "en";
const TEXT = "The cat sat on the mat. The dog sat on the log.";
// The picture put in first, and the one then chosen from the menu.
const FIRST_PICTURE = "house-landscape.png";
const CHOSEN_PICTURE = "bird.png";
const ZERO_WIDTH_SPACE = String.fromCharCode(0x200b);

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

type Page = import("@playwright/test").Page;

// The reader tool of the book that this worker's Bloom has open, once a test has made one. The
// tests add their pages to it.
let bookMadeFor: ReaderTool | undefined;

/**
 * Add a Just Text page to a book with a reader tool open (the Decodable Reader unless `tool` says
 * otherwise), type on it, insert a picture and give it FIRST_PICTURE, and return the picture's id.
 * Each test makes its own page, so any of them can run, and fail, alone.
 */
async function makeReaderPageWithPicture(
    page: Page,
    tool: ReaderTool = "decodableReader",
): Promise<string> {
    if (bookMadeFor !== tool) {
        await makeBasicBookWithReaderTool(page, tool);
        bookMadeFor = tool;
    }
    await addPage(page, "Just Text");
    const pages = await getContentPages(page);
    await goToPage(page, pages[pages.length - 1].id);
    await expandReaderTool(page, tool);
    await typeInGroup(page, BLOCK, LANG, TEXT);
    const id = await addInlineImage(page, BLOCK, LANG);
    // A placeholder draws its flower as a background and hides the img, so a picture is put in
    // first, for the right-click to land on. This route finds the img afresh, so it is not the
    // one under test.
    await changeInlineImagePicture(
        page,
        BLOCK,
        LANG,
        id,
        fixtureImage(FIRST_PICTURE),
    );
    const copies = await getInlineImageInEveryLanguage(page, BLOCK, id);
    expect(
        copies.length,
        "The picture should be in more than one language block, so that every language can be checked.",
    ).toBeGreaterThan(1);
    // Bloom adds a number to the file name when the book already has the file, as it does on
    // the second page, so this checks only that every copy shows the same picture, and that it is
    // not the placeholder.
    const names = copies.map((copy) => copy.fileName);
    expect(
        names.every((name) => name === names[0] && name !== "placeHolder.png"),
        `Every language block should show ${FIRST_PICTURE} before the test acts on it, but they ` +
            `show ${names.join(", ")}.`,
    ).toBe(true);
    return id;
}

test("with the Decodable Reader open, Choose image changes the inline image's picture in every language [Test Case ID 815]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    let imageId = "";
    await step(
        "Make a page with text and a picture, with the Decodable Reader open",
        async () => {
            imageId = await makeReaderPageWithPicture(page);
        },
    );
    // chooseInlineImagePictureFromMenu fails, naming the languages that do show the picture,
    // unless every copy ends up showing it.
    await step("Choose a picture from the picture's menu", async () => {
        await chooseInlineImagePictureFromMenu(
            page,
            BLOCK,
            LANG,
            imageId,
            fixtureImage(CHOSEN_PICTURE),
        );
    });
});

test("with the Decodable Reader open, Delete on the inline image's menu removes it from every language [Test Case ID 815]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    let imageId = "";
    await step(
        "Make a page with text and a picture, with the Decodable Reader open",
        async () => {
            imageId = await makeReaderPageWithPicture(page);
        },
    );
    // deleteInlineImage fails unless every copy goes.
    await step("Delete the picture from its menu", async () => {
        await deleteInlineImage(page, BLOCK, LANG, imageId);
    });
    expect(
        (await getInlineImages(page, BLOCK)).flatMap((block) => block.images),
        "Delete left inline images in the block.",
    ).toEqual([]);
});

// The Leveled Reader marks up the text by the same route as the Decodable Reader, and tidies it the
// same way when the text loses the focus, so the same two commands are checked with it open.

test("with the Leveled Reader open, Choose image changes the inline image's picture in every language [Test Case ID 815]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    let imageId = "";
    await step(
        "Make a page with text and a picture, with the Leveled Reader open",
        async () => {
            imageId = await makeReaderPageWithPicture(page, "leveledReader");
        },
    );
    // chooseInlineImagePictureFromMenu fails, naming the languages that do show the picture,
    // unless every copy ends up showing it.
    await step("Choose a picture from the picture's menu", async () => {
        await chooseInlineImagePictureFromMenu(
            page,
            BLOCK,
            LANG,
            imageId,
            fixtureImage(CHOSEN_PICTURE),
        );
    });
});

test("with the Leveled Reader open, Delete on the inline image's menu removes it from every language [Test Case ID 815]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    let imageId = "";
    await step(
        "Make a page with text and a picture, with the Leveled Reader open",
        async () => {
            imageId = await makeReaderPageWithPicture(page, "leveledReader");
        },
    );
    // deleteInlineImage fails unless every copy goes.
    await step("Delete the picture from its menu", async () => {
        await deleteInlineImage(page, BLOCK, LANG, imageId);
    });
    expect(
        (await getInlineImages(page, BLOCK)).flatMap((block) => block.images),
        "Delete left inline images in the block.",
    ).toEqual([]);
});

// The reader tools have an undo of their own for typing: Ctrl+Z puts back a copy of the block's
// HTML that they kept from before, and that copy holds the block's pictures as they were then.
// Every language's block has its own copy of each picture, so putting an old copy back would undo
// a picture change in one language alone. The reader tools' undo and redo now keep the pictures
// that are on the page (replaceContentKeepingLiveInlineImages), and a picture change starts their
// history again (restartHistoryIn), as it does CKEditor's.
//
// A resize is the picture change used here because dragging a handle leaves the caret in the
// text. Clicking in the text starts the reader tools' history again whatever came before, so the
// typing straight after the resize, with no click, is the part that would put the old picture
// back.
//
// Ctrl+Y is not checked. Driven by a test, the reader tools' Ctrl+Y after their Ctrl+Z brought the
// typing back in only some runs, in a block with no picture as well, so it would fail for a reason
// that has nothing to do with pictures.
for (const tool of ["decodableReader", "leveledReader"] as const) {
    const toolName =
        tool === "decodableReader" ? "Decodable Reader" : "Leveled Reader";
    test(`with the ${toolName} open, Ctrl+Z after a picture change takes back only the typing done since, and the picture stays as it is in every language [Test Case ID 815]`, async ({
        page,
        step,
    }) => {
        test.setTimeout(300000);
        let imageId = "";
        await step(
            `Make a page with text and a picture, with the ${toolName} open`,
            async () => {
                imageId = await makeReaderPageWithPicture(page, tool);
            },
        );
        const widths = async () =>
            (await getInlineImageInEveryLanguage(page, BLOCK, imageId)).map(
                (copy) => copy.widthPercent,
            );
        // The reader tools' markup can leave a zero-width space at the end of the text, which a
        // person cannot see, so it is left out of what is compared.
        const expectText = async (ending: RegExp, why: string) =>
            expect
                .poll(
                    async () =>
                        (await getBlockText(page, BLOCK, LANG))
                            .split(ZERO_WIDTH_SPACE)
                            .join(""),
                    {
                        timeout: 30000,
                        message: why,
                    },
                )
                .toMatch(ending);
        /** Type a word with `type`, and wait until the reader tool can undo it. */
        const typeWord = async (type: () => Promise<void>) => {
            await type();
            await waitForUndoAvailableFrom(page, "readerTools");
        };

        let width = 0;
        await step(
            "Type a word, make the picture larger, and type another word without clicking",
            async () => {
                await typeWord(() =>
                    typeAtEndOfBlock(page, BLOCK, LANG, " red"),
                );
                width = await resizeInlineImage(
                    page,
                    BLOCK,
                    LANG,
                    imageId,
                    "sw",
                    40,
                );
                await typeWord(() =>
                    typeAtCaretInBlock(page, BLOCK, LANG, " hen"),
                );
            },
        );
        const widthsAfterResize = await widths();
        expect(
            widthsAfterResize.length > 1 &&
                widthsAfterResize.every((w) => w === width),
            `Every language's copy should have the new width (${width}%) before Ctrl+Z is ` +
                `pressed. They have ${widthsAfterResize.join(", ")}.`,
        ).toBe(true);

        await step("Press Ctrl+Z three times", async () => {
            for (let press = 0; press < 3; press++)
                await pressKey(page, "Control+z");
        });
        await expectText(
            /log\. red$/,
            'Ctrl+Z should take back " hen", typed after the picture change, and leave ' +
                '" red", typed before it.',
        );
        expect(
            await widths(),
            "Ctrl+Z after the picture change put an old size back on some language's copy of " +
                "the picture.",
        ).toEqual(widthsAfterResize);

        await step(
            "Click back in the text, type, and press Ctrl+Z",
            async () => {
                await typeWord(() =>
                    typeAtEndOfBlock(page, BLOCK, LANG, " pig"),
                );
                await pressKey(page, "Control+z");
                await expectText(
                    /log\. red$/,
                    'Ctrl+Z should take back " pig" and nothing else.',
                );
            },
        );
        expect(
            await widths(),
            "Undo of typing changed the size of some language's copy of the picture.",
        ).toEqual(widthsAfterResize);

        // deleteInlineImage fails unless every copy goes.
        await step("Delete the picture from its menu", async () => {
            await deleteInlineImage(page, BLOCK, LANG, imageId);
        });
    });
}
