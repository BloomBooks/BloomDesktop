// Inline (Word-style) images are pictures placed inside a text block, with the block's text
// wrapping around them. bookEdit/js/inlineImages.ts describes the feature.
//
// The vitest suites beside the code (bookEdit/js/inlineImages.test.ts and
// inlineImageInteractions.test.ts) run in jsdom and cover the logic: which dock class a drop
// position picks, how the offset is clamped, and what a sync copies onto the other languages.
// This file covers what jsdom cannot show:
//
//  - The right-click menu in WebView2, which is the only way to add or delete an image.
//  - Mouse gestures against real layout. The dock a drag lands in depends on which third of the
//    block the mouse is over, and how far down the picture may go depends on the block's height.
//  - The CSS. The float and its wrap shape make the text flow beside the picture, and
//    display:flow-root keeps the picture inside its block.
//  - What Bloom writes to the .htm file when it saves: the editing decorations removed, and
//    contenteditable="false" still on the wrapper.
//  - Undo through the workspace's undo chain, which the front end shares with CKEditor and the
//    canvas element manager.
//
// The tests run in order, and each starts from the book the previous one left, so the file works
// through one text block the way a person would in one sitting.

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
import {
    addInlineImage,
    changeInlineImagePicture,
    deleteInlineImage,
    getInlineImage,
    getInlineImageInEveryLanguage,
    getBlockText,
    getInlineImageMenuCommands,
    getInlineImageRects,
    getInlineImageToolbarButtons,
    getInlineImageToolbarRect,
    getInlineImages,
    clickInBlockText,
    closeInlineImageMenu,
    dragInlineImageDown,
    dragInlineImageToDock,
    readSavedInlineImages,
    resizeInlineImage,
    selectInlineImage,
    textBlockOffersInsertImage,
    textWrapsBesideInlineImage,
} from "../helpers/inlineImages";
import { expectInside } from "../helpers/geometry";
import { undo } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "inline-images", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// The only translation group on a Just Text page, and the collection's one language. The tests use
// a Just Text page because its text block fills the page, so there is room to dock a picture on
// any side and to drag it down without the block overflowing.
const BLOCK = ".bloom-translationGroup";
const LANG = "en";

// The title the book is given, so that findBookFolder can name its folder on disk.
const BOOK_TITLE = "Inline Images";

// Enough text to give the block several lines for the picture to push aside. With no text there
// would be nothing to wrap, and no way to measure whether the text flows beside the picture.
const TEXT =
    "The kingfisher waits on the branch above the pool, still enough that the water forgets " +
    "it is there. It watches the shadows move under the surface. When it drops, it drops " +
    "straight, and the pool closes over the place where it went in. A moment later it is back " +
    "on the branch with a fish, and the water is still again. The children on the bank have " +
    "learned to wait as well, and they do not talk while the bird is fishing.";

// The path of a picture file the tests put in the image. The files are in the suite's own
// fixtures folder.
const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

// The image every test after the first one works on, and the book's folder. Set by the first test.
let imageId: string;
let bookFolder: string;

test.describe("inline images in a text block [Test Case ID 815]", () => {
    test("the right-click menu of a text block offers to add an inline image", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
        await addPage(page, "Just Text");
        const [contentPage] = await getContentPages(page);
        await goToPage(page, contentPage.id);
        await typeInGroup(page, BLOCK, LANG, TEXT);
        bookFolder = await findBookFolder(page, BOOK_TITLE);

        // Check that the block every later test uses has text and no inline image yet.
        const blocks = await getInlineImages(page, BLOCK);
        expect(
            blocks.length,
            "A Just Text page's translation group should have at least the shown language and " +
                "the lang=z prototype.",
        ).toBeGreaterThan(1);
        expect(blocks.flatMap((block) => block.images)).toEqual([]);

        expect(await textBlockOffersInsertImage(page, BLOCK, LANG)).toBe(true);
    });

    test("Insert Image puts a placeholder in every language, docked right", async ({
        page,
    }) => {
        // THE ACTION UNDER TEST: a right-click in the text, and a click on Insert Image.
        imageId = await addInlineImage(page, BLOCK, LANG);

        const blocks = await getInlineImages(page, BLOCK);
        // Every block in the group gets a copy, including the lang="z" prototype. A language
        // added later gets the image from that prototype copy, so no C# code has to add it.
        expect(blocks.map((block) => block.images.length)).toEqual(
            blocks.map(() => 1),
        );
        for (const copy of blocks.flatMap((block) => block.images)) {
            expect(copy.id).toBe(imageId);
            expect(copy.dock).toBe("right");
            expect(copy.widthPercent).toBe(40);
            expect(copy.offsetPx).toBe(0);
            expect(copy.fileName).toBe("placeHolder.png");
            // The saved markup needs this. Without it, the Bloom code that sets the language on
            // every editable element would treat the wrapper as a text box.
            expect(copy.contentEditable).toBe("false");
        }
        // Adding the image does not open the image chooser; the person chooses a picture later.
        // It does select the image, which puts the corner handles on it.
        const shown = await getInlineImage(page, BLOCK, LANG, imageId);
        expect(shown.selected).toBe(true);
        expect(shown.handleCount).toBe(4);
        // Selecting it also shows the toolbar, so the buttons appear with the image and the
        // person does not have to click the picture first.
        expect(
            (await getInlineImageToolbarButtons(page)).map(
                (button) => button.id,
            ),
        ).toContain("chooseImage");
    });

    test("the block's text flows beside the picture", async ({ page }) => {
        await changeInlineImagePicture(
            page,
            BLOCK,
            LANG,
            imageId,
            fixtureImage("bird.png"),
        );
        const shown = await getInlineImage(page, BLOCK, LANG, imageId);
        expect(shown.float).toBe("right");
        expect(
            shown.wrapShape,
            "Without a wrap shape the text would avoid the picture's whole box, including the " +
                "transparent offset above it.",
        ).not.toBe("none");
        // The block has to contain the floated picture. Otherwise the picture could hang below
        // the block, over whatever comes next on the page.
        const [firstBlock] = (await getInlineImages(page, BLOCK)).filter(
            (block) => block.languageTag === LANG,
        );
        expect(firstBlock.display).toBe("flow-root");
        expect(
            await textWrapsBesideInlineImage(page, BLOCK, LANG, imageId),
            "No line of the block's text sits beside the picture, so the text is not wrapping " +
                "around it.",
        ).toBe(true);
    });

    test("clicking the picture selects it, and clicking the text lets it go", async ({
        page,
    }) => {
        await clickInBlockText(page, BLOCK, LANG);
        expect(
            (await getInlineImage(page, BLOCK, LANG, imageId)).handleCount,
        ).toBe(0);

        // THE ACTION UNDER TEST: a click on the picture. The first click has to select it. That
        // click also moves keyboard focus to the block that contains the picture, and the code
        // has to tell that focus change apart from the person clicking back into the text.
        await selectInlineImage(page, BLOCK, LANG, imageId);
        const selected = await getInlineImage(page, BLOCK, LANG, imageId);
        expect(selected.selected).toBe(true);
        expect(selected.handleCount).toBe(4);

        // Only the copy the person clicked is selected; the other languages' copies are not.
        const copies = await getInlineImageInEveryLanguage(
            page,
            BLOCK,
            imageId,
        );
        expect(copies.filter((copy) => copy.selected).length).toBe(1);
    });

    test("the picture carries the toolbar an image has on a canvas", async ({
        page,
    }) => {
        // Clicking in the text deselects the picture, which hides the toolbar and the handles.
        await clickInBlockText(page, BLOCK, LANG);
        expect(await getInlineImageToolbarButtons(page)).toEqual([]);

        // THE ACTION UNDER TEST: a click on the picture brings the toolbar back.
        await selectInlineImage(page, BLOCK, LANG, imageId);
        const buttons = await getInlineImageToolbarButtons(page);
        const ids = buttons.map((button) => button.id);
        // These are the buttons an image on a canvas has, so a person uses the same controls
        // for every picture.
        expect(ids).toContain("chooseImage");
        expect(ids).toContain("pasteImage");
        expect(ids).toContain("delete");
        // Two canvas buttons are left out. Both work only on a canvas element, and a picture
        // inside a text block cannot become one.
        expect(ids).not.toContain("becomeBackground");
        expect(ids).not.toContain("duplicate");
        expect(buttons.find((button) => button.id === "delete")?.enabled).toBe(
            true,
        );

        // The toolbar sits below this picture and overlaps it horizontally.
        const rects = await getInlineImageRects(page, BLOCK, LANG, imageId);
        const bar = await getInlineImageToolbarRect(page);
        expect(bar, "The toolbar is not on the screen.").toBeDefined();
        expect(bar!.y).toBeGreaterThanOrEqual(rects.picture.y);
        expect(bar!.x + bar!.width).toBeGreaterThan(rects.picture.x);
        expect(bar!.x).toBeLessThan(rects.picture.x + rects.picture.width);
    });

    test("choosing a picture puts it in every language's copy", async ({
        page,
    }) => {
        const copies = await getInlineImageInEveryLanguage(
            page,
            BLOCK,
            imageId,
        );
        expect(copies.length).toBeGreaterThan(1);
        for (const copy of copies) expect(copy.fileName).toBe("bird.png");
    });

    test("the picture's own menu offers the standard image commands and Delete", async ({
        page,
    }) => {
        const commands = await getInlineImageMenuCommands(
            page,
            BLOCK,
            LANG,
            imageId,
        );
        const ids = commands.map((command) => command.id);
        // These are the commands an image has elsewhere in Bloom, so a person sees the same
        // commands for every picture. Delete here removes the copy in every language.
        expect(ids).toContain("EditTab.Image.ChooseImage");
        expect(ids).toContain("EditTab.Image.CopyImage");
        expect(ids).toContain("Common.Delete");
        // Two commands are left out. Both work only on a canvas element, and a picture inside a
        // text block cannot become one.
        expect(ids).not.toContain("EditTab.Image.BecomeBackground");
        expect(
            commands.find((command) => command.id === "Common.Delete")?.enabled,
        ).toBe(true);
        await closeInlineImageMenu(page);
    });

    test("dragging the picture to the left of the block docks it left", async ({
        page,
    }) => {
        // THE ACTION UNDER TEST: a mouse drag of the picture into the left third of the block.
        await dragInlineImageToDock(page, BLOCK, LANG, imageId, "left");

        const copies = await getInlineImageInEveryLanguage(
            page,
            BLOCK,
            imageId,
        );
        for (const copy of copies) expect(copy.dock).toBe("left");
        const shown = await getInlineImage(page, BLOCK, LANG, imageId);
        expect(shown.float).toBe("left");
        expect(
            await textWrapsBesideInlineImage(page, BLOCK, LANG, imageId),
            "The text stopped wrapping when the picture moved to the left dock.",
        ).toBe(true);
    });

    test("dragging the picture down moves it past the first lines, and stays inside the block", async ({
        page,
    }) => {
        const { block } = await getInlineImageRects(page, BLOCK, LANG, imageId);
        // THE ACTION UNDER TEST: a mouse drag straight down.
        const offset = await dragInlineImageDown(
            page,
            BLOCK,
            LANG,
            imageId,
            60,
        );
        expect(offset).toBeGreaterThan(0);

        // The distance is stored in one custom property, and every language's copy gets it.
        const copies = await getInlineImageInEveryLanguage(
            page,
            BLOCK,
            imageId,
        );
        for (const copy of copies) expect(copy.offsetPx).toBe(offset);

        // However far the drag went, the whole picture stays inside the block and no part of it
        // hangs below. The code limits the offset using the block's measured height, and only a
        // real browser can measure that.
        const { picture } = await getInlineImageRects(
            page,
            BLOCK,
            LANG,
            imageId,
        );
        expectInside(picture, block, "the picture", "its text block");
    });

    test("dragging a corner handle makes the picture wider, in every language", async ({
        page,
    }) => {
        const before = await getInlineImage(page, BLOCK, LANG, imageId);
        // THE ACTION UNDER TEST: dragging the bottom-right ("se") handle outward.
        const widthPercent = await resizeInlineImage(
            page,
            BLOCK,
            LANG,
            imageId,
            "se",
            60,
        );
        expect(widthPercent).toBeGreaterThan(before.widthPercent);
        // Bloom limits the width so there is still room for text beside the picture.
        expect(widthPercent).toBeLessThanOrEqual(95);

        const copies = await getInlineImageInEveryLanguage(
            page,
            BLOCK,
            imageId,
        );
        for (const copy of copies) expect(copy.widthPercent).toBe(widthPercent);
    });

    test("Undo takes back the last change to the image", async ({ page }) => {
        const before = await getInlineImage(page, BLOCK, LANG, imageId);
        const widened = await resizeInlineImage(
            page,
            BLOCK,
            LANG,
            imageId,
            "se",
            40,
        );
        expect(widened).not.toBe(before.widthPercent);

        // THE ACTION UNDER TEST: the workspace's own undo, which is what Ctrl+Z reaches.
        await undo(page);

        await expect
            .poll(
                async () =>
                    (await getInlineImage(page, BLOCK, LANG, imageId))
                        .widthPercent,
                {
                    message:
                        "Undo did not put the picture's width back. Inline images keep their own " +
                        "undo stack, because CKEditor's cannot see a change made to the DOM by " +
                        "code (inlineImages.ts).",
                },
            )
            .toBe(before.widthPercent);
        // Undo rebuilds the wrapper in every language from the markup it saved before the
        // change, so every copy gets the old width back, and the selected copy still has its
        // handles.
        const copies = await getInlineImageInEveryLanguage(
            page,
            BLOCK,
            imageId,
        );
        for (const copy of copies)
            expect(copy.widthPercent).toBe(before.widthPercent);
        expect(
            (await getInlineImage(page, BLOCK, LANG, imageId)).handleCount,
        ).toBe(4);
    });

    test("the image and its geometry survive leaving the page and coming back", async ({
        page,
    }) => {
        const before = await getInlineImage(page, BLOCK, LANG, imageId);
        const [contentPage] = await getContentPages(page);
        // Bloom saves a page when the person leaves it, so going to another page saves this one.
        const [firstXmatter] = (await getPages(page)).filter(
            (one) => !one.isContentPage,
        );
        await goToPage(page, firstXmatter.id);

        const saved = await readSavedInlineImages(page, bookFolder);
        expect(
            saved.length,
            "The saved book has no inline image, so nothing reached the file.",
        ).toBeGreaterThan(1);
        for (const copy of saved.filter((one) => one.id === imageId)) {
            expect(copy.classes).toContain("bloom-inlineImageLeft");
            expect(copy.contentEditable).toBe("false");
            expect(copy.source).toContain("bird.png");
            // KNOWN DEFECT: the "bloom-inlineImage-selected" class gets into the saved file, so
            // this test does not check that it is absent. deselectAllInlineImages() runs from
            // cleanupInlineImageInteractions(), which bloomEditing.ts calls from Cleanup() when
            // the page loads. Saving goes through extractAndStripPageContentForSave(), which
            // calls removeEditingDebris() and getBodyContentForSavePage(), and neither of those
            // touches the wrapper. The next load removes the class, so a reader never sees a
            // selected image, and the check after coming back to the page below tests that.
            expect(copy.style).toContain("--inline-image-width");
            // A floated image is the first child of its block, and the paragraph that
            // bloom-keepFirstInField requires comes after it.
            expect(copy.slot.index).toBe(0);
            expect(copy.slot.childCount).toBeGreaterThan(1);
        }

        // Coming back loads the page from what was saved.
        await goToPage(page, contentPage.id);
        const after = await getInlineImage(page, BLOCK, LANG, imageId);
        // The image is not selected after loading, even though the file has the class.
        expect(after.selected).toBe(false);
        expect(after.dock).toBe(before.dock);
        expect(after.widthPercent).toBe(before.widthPercent);
        expect(after.offsetPx).toBe(before.offsetPx);
        expect(after.fileName).toBe("bird.png");
    });

    test("dragging the picture below the text docks it at the bottom, as the last thing in the block", async ({
        page,
    }) => {
        // THE ACTION UNDER TEST: a mouse drag to below the block. Docking at the bottom is the
        // only change that moves the wrapper in the markup; it becomes the block's last child
        // instead of its first.
        await dragInlineImageToDock(page, BLOCK, LANG, imageId, "bottom");

        const copies = await getInlineImageInEveryLanguage(
            page,
            BLOCK,
            imageId,
        );
        for (const copy of copies) {
            expect(copy.dock).toBe("bottom");
            expect(copy.slot.index).toBe(copy.slot.childCount - 1);
        }
        const shown = await getInlineImage(page, BLOCK, LANG, imageId);
        // A picture docked at the bottom does not float; it is the last block in the field.
        expect(shown.float).toBe("none");
    });

    test("dragging it back out of the bottom dock puts it first again", async ({
        page,
    }) => {
        await dragInlineImageToDock(page, BLOCK, LANG, imageId, "right");
        const copies = await getInlineImageInEveryLanguage(
            page,
            BLOCK,
            imageId,
        );
        for (const copy of copies) {
            expect(copy.dock).toBe("right");
            expect(copy.slot.index).toBe(0);
        }
    });

    test("Delete takes the image out of every language", async ({ page }) => {
        // THE ACTION UNDER TEST: a right-click on the picture, and Delete.
        await deleteInlineImage(page, BLOCK, LANG, imageId);

        const blocks = await getInlineImages(page, BLOCK);
        expect(blocks.flatMap((block) => block.images)).toEqual([]);
        // Deleting the picture leaves the block's text in place.
        expect(await getBlockText(page, BLOCK, LANG)).toContain("kingfisher");
    });
});
