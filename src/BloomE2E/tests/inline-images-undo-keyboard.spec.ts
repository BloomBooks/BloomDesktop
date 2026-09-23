// What Ctrl+Z does to an inline image, as opposed to what the top-bar Undo button does.
//
// WHAT THIS IS ABOUT. The inline-image undo stack is reached from workspaceRoot.handleUndo, and
// the only thing that calls handleUndo is the top-bar Undo button (editTopBarControls posts
// editView/topBarButtonClick, EditingViewApi passes it back to bloomEditing.topBarButtonClick) --
// plus origami's own Ctrl+Z handler, which is bound only while Change Layout mode is on. So the
// existing undo test, which calls handleUndo, takes the path a person does not take.
//
// Two claims in this harness say Ctrl+Z is a WinForms accelerator that never reaches the page
// (helpers/keys.ts and helpers/workspace.ts). Nothing in src/BloomExe binds it: there is no
// ProcessCmdKey case, no menu ShortcutKeys, and the UndoCommand's Implementer in
// WebView2Browser.SetEditingCommands is an empty lambda. So this test asks the browser rather
// than the comments, and it asks it twice: once about text, which says whether the key arrives at
// all, and once about a picture, which is what the feature cares about.

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
    getInlineImage,
    getInlineImages,
    resizeInlineImage,
    selectInlineImage,
} from "../helpers/inlineImages";
import { pressKey, typeWithKeys } from "../helpers/keys";

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

const blockLocator = (page: import("@playwright/test").Page) =>
    page
        .frameLocator("#page")
        .locator(`${BLOCK} > .bloom-editable[lang="${LANG}"]`)
        .first();

test("Ctrl+Z reaches the page, and takes back the last change to a picture [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", "Inline Images Undo Keyboard");
    await addPage(page, "Just Text");
    const [contentPage] = await getContentPages(page);
    await goToPage(page, contentPage.id);
    await typeInGroup(page, BLOCK, LANG, TEXT);

    // FIRST QUESTION: does the key arrive in the page at all? Real key presses, because CKEditor's
    // undo plugin only ever sees a keystroke, and typeInGroup inserts text without one.
    const block = blockLocator(page);
    await block.click();
    await page.keyboard.press("Control+End");
    await typeWithKeys(page, " And then a word nobody wanted.");
    const textWithTheExtraWords = (await block.textContent()) ?? "";
    expect(
        textWithTheExtraWords,
        "The typing did not land, so this test cannot say what undoing it would do.",
    ).toContain("nobody wanted");
    await pressKey(page, "Control+z");
    // CKEditor's undo plugin restores a saved snapshot of the editable, which it does after the
    // keystroke rather than during it, so this waits for the words to go rather than for a time.
    await expect
        .poll(async () => (await block.textContent()) ?? "", {
            message:
                `Ctrl+Z did not take back the typing, so the key is not reaching the page's ` +
                `CKEditor at all -- which is the premise of everything below.`,
            timeout: 10000,
        })
        .not.toContain("nobody wanted");

    // SECOND QUESTION: the one the feature cares about. Resize the picture, then press Ctrl+Z with
    // the picture selected, which is the state a person is in right after changing it.
    const imageId = await addInlineImage(page, BLOCK, LANG);
    await changeInlineImagePicture(
        page,
        BLOCK,
        LANG,
        imageId,
        fixtureImage("bird.png"),
    );
    const before = await getInlineImage(page, BLOCK, LANG, imageId);
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
    ).not.toBe(before.widthPercent);

    await selectInlineImage(page, BLOCK, LANG, imageId);
    await pressKey(page, "Control+z");

    await expect
        .poll(
            async () =>
                (await getInlineImage(page, BLOCK, LANG, imageId)).widthPercent,
            {
                message:
                    `Ctrl+Z did not take back the picture's resize (it is still ` +
                    `${widened}%, and was ${before.widthPercent}% before). The inline-image undo ` +
                    `stack is reached only from workspaceRoot.handleUndo, and the only thing that ` +
                    `calls handleUndo is the top-bar Undo button. Ctrl+Z in the page belongs to ` +
                    `CKEditor's undo plugin, and selecting a picture deliberately leaves the ` +
                    `keyboard focus on the containing editable -- so the key undoes text, or ` +
                    `nothing, and the person's last change to the picture cannot be taken back ` +
                    `by the key everyone uses.`,
                timeout: 10000,
            },
        )
        .toBe(before.widthPercent);

    // And it has to be the whole undo, not one block's worth of it. CKEditor's undo restores one
    // editable's saved HTML, so it cannot know about the wrapper's copies in the other editables
    // of the group -- including the lang="z" prototype, which is what a language added to the
    // collection later is built from. If those are left at the width the person just undid, the
    // book is in a state no operation produced, and it is the prototype that carries it forward.
    const blocks = await getInlineImages(page, BLOCK);
    const copies = blocks
        .flatMap((b) => b.images)
        .filter((i) => i.id === imageId);
    expect(
        copies.length,
        "The group holds no copies of the picture at all, so this test is measuring nothing.",
    ).toBeGreaterThan(1);
    for (const copy of copies)
        expect(
            copy.widthPercent,
            `Ctrl+Z put the "${LANG}" copy of the picture back to ${before.widthPercent}% but ` +
                `left the "${copy.languageTag}" copy at ${copy.widthPercent}%. CKEditor's undo ` +
                `restores the saved HTML of one editable, so it can only ever undo the copy in ` +
                `the block that had the focus.`,
        ).toBe(before.widthPercent);
});
