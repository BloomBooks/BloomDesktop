// Checks what Ctrl+Z and Ctrl+Y do to an inline image, as distinct from what the top-bar Undo button
// does (inline-images.spec.ts tests the button).
//
// Every inline image operation is an entry on Bloom's one undo stack (bookEdit/undo), beside
// CKEditor's record of the typing. The two keys reach that stack by different routes. Ctrl+Z in a
// text block belongs to CKEditor unless the stack's next undo is an inline image entry; then
// inlineImages.ts cancels CKEditor's undo command and undoes the entry through the stack instead.
// Ctrl+Y goes to the stack's redo key binding (redoKeyBinding.ts) whenever the stack has something
// to redo. Typing discards what the stack could redo (undo/ckeditorChanges.ts), so after
// "change a picture, Undo, type", Ctrl+Y must not bring the picture change back over the typing.
//
// Nothing in src/BloomExe binds either key, so both reach the page. The first test checks that with
// typing, which only CKEditor's undo plugin can take back.
//
// The tests run in order on one text block, each starting from what the previous one left.
//
// Not tested, because it is a known gap, the same one tableUndo.ts describes for tables: CKEditor
// reports only its latest change, so after "type, change a picture, type, Undo, Undo" the second
// Undo takes back the older typing instead of the picture change. It closes when typing moves onto
// the stack (Stage 3 of docs/retire-ckeditor/PLAN.md).

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
    getBlockText,
    getInlineImage,
    getInlineImageInEveryLanguage,
    resizeInlineImage,
    selectInlineImage,
    typeAtEndOfBlock,
} from "../helpers/inlineImages";
import { pressKey } from "../helpers/keys";
import {
    canRedo,
    expectUndoMechanismCalls,
    watchUndoMechanisms,
} from "../helpers/undo";

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

// Wait until every language's copy of the picture has this width. CKEditor's undo restores the
// saved HTML of one editable only, so a copy left behind in another language (the lang="z"
// prototype included) is how a key that reached CKEditor instead of the stack shows.
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
                    `A copy in the "${LANG}" block alone at ${width}% means CKEditor's undo ran ` +
                    `instead of the stack's.`,
            },
        )
        .toEqual(
            (await getInlineImageInEveryLanguage(page, BLOCK, imageId)).map(
                (copy) => `${copy.languageTag}: ${width}%`,
            ),
        );
}

test("Ctrl+Z reaches the page, and takes back typing [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", "Inline Images Undo Keyboard");
    await addPage(page, "Just Text");
    const [contentPage] = await getContentPages(page);
    await goToPage(page, contentPage.id);
    await typeInGroup(page, BLOCK, LANG, TEXT);

    await typeAtEndOfBlock(
        page,
        BLOCK,
        LANG,
        " And then a word nobody wanted.",
    );
    await pressKey(page, "Control+z");
    // CKEditor's undo plugin restores a saved snapshot of the editable after the keystroke has
    // been handled, so this waits until the words are gone instead of for a fixed time.
    await expect
        .poll(async () => getBlockText(page, BLOCK, LANG), {
            message:
                `Ctrl+Z did not take back the typing, so the key is not reaching the page's ` +
                `CKEditor at all -- which is the premise of every test below.`,
            timeout: 10000,
        })
        .not.toContain("nobody wanted");
});

test("Ctrl+Z takes back the last change to a picture, in every language [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
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
        "The group holds fewer than two copies of the picture, so this test cannot tell the " +
            "stack's undo from CKEditor's, which restores one editable only.",
    ).toBeGreaterThan(1);
    originalWidth = (await getInlineImage(page, BLOCK, LANG, imageId))
        .widthPercent;
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

    // Press Ctrl+Z with the picture selected, as it is right after a person changes it.
    await selectInlineImage(page, BLOCK, LANG, imageId);
    await pressKey(page, "Control+z");

    await expectEveryCopyWidth(page, originalWidth, "Ctrl+Z");
});

test("Ctrl+Y puts back a picture change that Ctrl+Z took back [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    expect(
        await canRedo(page),
        "After undoing the picture's resize, the undo stack has nothing to redo.",
    ).toBe(true);
    await watchUndoMechanisms(page);

    await pressKey(page, "Control+y");

    await expectEveryCopyWidth(page, widenedWidth, "Ctrl+Y");
    await expectUndoMechanismCalls(page, { stackRedo: 1 }, "pressing Ctrl+Y");
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
        "Right after Ctrl+Z took back the resize, the undo stack has nothing to redo, so this " +
            "test could not show typing discarding it.",
    ).toBe(true);

    const typed = " A heron lands.";
    await typeAtEndOfBlock(page, BLOCK, LANG, typed);
    await expect
        .poll(async () => canRedo(page), {
            timeout: 10000,
            message:
                "Typing after an undo left the picture change on the undo stack to be redone, " +
                "so Ctrl+Y would put it back on top of the new typing.",
        })
        .toBe(false);
    await watchUndoMechanisms(page);

    await pressKey(page, "Control+y");

    // The stack's Ctrl+Y binding runs in the keydown, so by now it has run if it was going to.
    await expectUndoMechanismCalls(
        page,
        { stackRedo: 0 },
        "pressing Ctrl+Y after typing",
    );
    await expectEveryCopyWidth(page, originalWidth, "Ctrl+Y after typing");
    expect(
        await getBlockText(page, BLOCK, LANG),
        "Ctrl+Y after typing took the typing away.",
    ).toContain(typed.trim());
});
