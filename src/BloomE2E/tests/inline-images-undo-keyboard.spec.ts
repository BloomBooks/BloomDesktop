// Checks what Ctrl+Z and Ctrl+Y do to an inline image, as distinct from what the top-bar Undo button
// does (inline-images.spec.ts tests the button).
//
// Every inline image operation, like every other change to the page, is a step of the page
// history (bookEdit/pageHistory), and so is each run of typing. The page frame takes both keys
// (pageHistoryKeys.ts): Ctrl+Z undoes the newest step on the page and Ctrl+Y redoes the step undone
// last, whatever each changed. A change after an undo, typing included, drops what could be
// redone, so after "change a picture, Undo, type", Ctrl+Y must not bring the picture change back
// over the typing.
//
// Nothing in src/BloomExe binds either key, so both reach the page. The first test undoes a picture
// change in a block whose text was not typed with keys; the second checks that the key takes back
// typing; the third undoes a picture change after that typing.
//
// One test checks that after typing and then a picture change, Ctrl+Z takes back the picture change
// first and the typing next, each in every language that it changed.
//
// The tests run in order on one text block, each starting from what the previous one left.

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
    anyTextBlockHasFocus,
    changeInlineImagePicture,
    clickPageMargin,
    getBlockText,
    getInlineImage,
    getInlineImageInEveryLanguage,
    resizeInlineImage,
    selectInlineImage,
    typeAtEndOfBlock,
} from "../helpers/inlineImages";
import { pressKey } from "../helpers/keys";
import { canRedo } from "../helpers/undo";

test.use({
    collectionSpec: { name: "inline-images-undo-keyboard", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

const BLOCK = ".bloom-translationGroup";
const LANG = "en";

const TEXT =
    "The kingfisher waits on the branch above the pool, still enough that the water forgets it " +
    "is there. It watches the shadows move under the surface.";

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

// Set by the tests in turn: the picture they all work on, its width before the resize, and its
// width after.
let imageId: string;
let originalWidth: number;
let widenedWidth: number;

// Wait until every language's copy of the picture has this width. A copy left behind in another
// language (the lang="z" prototype included) is how an undo that restored one editable only shows.
async function expectEveryCopyWidth(
    page: import("@playwright/test").Page,
    width: number,
    gesture: string,
): Promise<void> {
    await expect
        .poll(
            async () =>
                (await getInlineImageInEveryLanguage(page, BLOCK, imageId)).map(
                    (copy) => `${copy.languageTag}: ${copy.widthPercent}%`,
                ),
            {
                timeout: 10000,
                message:
                    `After ${gesture}, not every copy of the picture was ${width}% wide. ` +
                    `A copy in the "${LANG}" block alone at ${width}% means the undo restored ` +
                    `that editable only.`,
            },
        )
        .toEqual(
            (await getInlineImageInEveryLanguage(page, BLOCK, imageId)).map(
                (copy) => `${copy.languageTag}: ${width}%`,
            ),
        );
}

// Widen the picture with its corner handle, then press Ctrl+Z with the picture selected, as it is
// right after a person changes it, and wait for every copy to be back at originalWidth. Leaves
// widenedWidth set for the Ctrl+Y tests.
async function widenAndPressCtrlZ(
    page: import("@playwright/test").Page,
): Promise<void> {
    widenedWidth = await resizeInlineImage(
        page,
        BLOCK,
        LANG,
        imageId,
        "se",
        40,
    );
    expect(
        widenedWidth,
        "The resize did not change the picture's width, so there is nothing for undo to take back.",
    ).not.toBe(originalWidth);
    await selectInlineImage(page, BLOCK, LANG, imageId);
    await pressKey(page, "Control+z");
    await expectEveryCopyWidth(page, originalWidth, "Ctrl+Z");
}

test("Ctrl+Z takes back a picture change in a block nobody has typed in [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", "Inline Images Undo Keyboard");
    await addPage(page, "Just Text");
    const [contentPage] = await getContentPages(page);
    await goToPage(page, contentPage.id);
    // typeInGroup puts the text in without key presses, as an existing book's text is when a
    // person opens it: there is no typing to undo.
    await typeInGroup(page, BLOCK, LANG, TEXT);

    imageId = await addInlineImage(page, BLOCK, LANG);
    await changeInlineImagePicture(
        page,
        BLOCK,
        LANG,
        imageId,
        fixtureImage("bird.png"),
    );
    const copies = await getInlineImageInEveryLanguage(page, BLOCK, imageId);
    expect(
        copies.length,
        "The group holds fewer than two copies of the picture, so this test cannot tell an " +
            "undo of every language from one that restores one editable only.",
    ).toBeGreaterThan(1);
    originalWidth = (await getInlineImage(page, BLOCK, LANG, imageId))
        .widthPercent;
    await widenAndPressCtrlZ(page);
});

test("Ctrl+Z reaches the page, and takes back typing [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    // Typed without a pause, so that it is one step of the page history.
    const typed = " A word nobody wanted.";
    const textBefore = await getBlockText(page, BLOCK, LANG);
    await typeAtEndOfBlock(page, BLOCK, LANG, typed);
    await pressKey(page, "Control+z");
    // The page history's undo can wait for the typing step to end, so this waits for the text
    // instead of for a fixed time.
    await expect
        .poll(async () => getBlockText(page, BLOCK, LANG), {
            message:
                `Ctrl+Z did not put the text back as it was before the typing, so the key is ` +
                `not reaching the page history.`,
            timeout: 10000,
        })
        .toBe(textBefore);
});

test("after typing, Ctrl+Z still takes back the last change to a picture, in every language [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await widenAndPressCtrlZ(page);
});

test("Ctrl+Y puts back a picture change that Ctrl+Z took back [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    expect(
        await canRedo(page),
        "After undoing the picture's resize, the page history has nothing to redo.",
    ).toBe(true);
    await pressKey(page, "Control+y");

    await expectEveryCopyWidth(page, widenedWidth, "Ctrl+Y");
});

test("typing after Ctrl+Z means Ctrl+Y no longer brings the picture change back [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await selectInlineImage(page, BLOCK, LANG, imageId);
    await pressKey(page, "Control+z");
    await expectEveryCopyWidth(page, originalWidth, "Ctrl+Z");
    expect(
        await canRedo(page),
        "Right after Ctrl+Z took back the resize, the page history has nothing to redo, so " +
            "this test could not show typing discarding it.",
    ).toBe(true);

    const typed = " A heron lands.";
    await typeAtEndOfBlock(page, BLOCK, LANG, typed);
    await expect
        .poll(async () => canRedo(page), {
            timeout: 10000,
            message:
                "Typing after an undo left the picture change in the page history to be redone, " +
                "so Ctrl+Y would put it back on top of the new typing.",
        })
        .toBe(false);
    await pressKey(page, "Control+y");

    await expectEveryCopyWidth(page, originalWidth, "Ctrl+Y after typing");
    expect(
        await getBlockText(page, BLOCK, LANG),
        "Ctrl+Y after typing took the typing away.",
    ).toContain(typed.trim());
});

test("after typing and then a picture change, Ctrl+Z takes back the picture change and then the typing [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    const typed = " Two ducks swim past.";
    await typeAtEndOfBlock(page, BLOCK, LANG, typed);
    const widthBefore = (await getInlineImage(page, BLOCK, LANG, imageId))
        .widthPercent;
    const widened = await resizeInlineImage(
        page,
        BLOCK,
        LANG,
        imageId,
        "se",
        40,
    );
    expect(
        widened,
        "The resize did not change the picture's width, so there is nothing for undo to take back.",
    ).not.toBe(widthBefore);

    await selectInlineImage(page, BLOCK, LANG, imageId);
    await pressKey(page, "Control+z");
    await expectEveryCopyWidth(page, widthBefore, "the first Ctrl+Z");
    expect(
        await getBlockText(page, BLOCK, LANG),
        "The first Ctrl+Z took back the typing as well as the picture change.",
    ).toContain(typed.trim());

    // The typing is the step before the picture change, so the second Ctrl+Z takes it back, and
    // leaves the picture as the first one left it.
    await pressKey(page, "Control+z");
    await expect
        .poll(async () => getBlockText(page, BLOCK, LANG), {
            timeout: 10000,
            message:
                "The second Ctrl+Z did not take back the typing done before the picture change.",
        })
        .not.toContain(typed.trim());
    await expectEveryCopyWidth(page, widthBefore, "the second Ctrl+Z");
});

test("Ctrl+Z takes back a picture change when the caret is not in the picture's block [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    // A person who clicks the page's margin and then works only on the picture never puts the
    // caret in its block, and clicking a picture does not move the focus there either.
    await clickPageMargin(page);
    widenedWidth = await resizeInlineImage(
        page,
        BLOCK,
        LANG,
        imageId,
        "se",
        40,
    );
    expect(
        widenedWidth,
        "The resize did not change the picture's width, so there is nothing for undo to take back.",
    ).not.toBe(originalWidth);
    await selectInlineImage(page, BLOCK, LANG, imageId);
    expect(
        await anyTextBlockHasFocus(page),
        "Changing and selecting the picture put the caret in a text block, so this test cannot " +
            "show Ctrl+Z working without it there.",
    ).toBe(false);

    await pressKey(page, "Control+z");

    await expectEveryCopyWidth(
        page,
        originalWidth,
        "Ctrl+Z with no caret in the block",
    );
});
