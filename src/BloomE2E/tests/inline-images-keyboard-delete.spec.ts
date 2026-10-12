// Checks what happens when the person tries to delete a picture with the keyboard instead of
// from its menu.
//
// Two pieces of code are involved, and each works correctly by itself.
//
// BloomField's PreventRemovalOfSomeElements keeps the picture from being deleted. The wrapper has
// the bloom-preventRemoval class, and if a keystroke leaves the field with fewer elements that
// have that class than it had before, the code calls document.execCommand("undo"). It is there to
// handle Ctrl+A DEL.
//
// The other is normalizeInlineImages, which runs when the page is set up and copies the images
// in the editable chosen by getCanonicalInlineImageEditable over the group's other editables.
// getCanonicalInlineImageEditable only chooses an editable that has images, so an editable the
// person emptied is never the one copied from, and the picture comes back from whichever language
// still has it. Also, nothing calls syncInlineImagesFromEditable after ordinary typing or cutting
// (only the inline image operations call it), so a deletion made with the keyboard never reaches
// the other languages.
//
// So if PreventRemovalOfSomeElements ever fails to put the picture back, the person deletes the
// picture, saves, comes back, and finds it there again, with no way to tell why. This test checks
// in the browser whether Ctrl+A DEL leaves the picture in place, and whether a deletion from the
// menu lasts through a save and a reload.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
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
    getInlineImages,
} from "../helpers/inlineImages";

test.use({
    collectionSpec: {
        name: "inline-images-keyboard-delete",
        languages: ["en"],
    },
});

test.describe.configure({ mode: "serial" });

const BLOCK = ".bloom-translationGroup";
const LANG = "en";

const TEXT =
    "The kingfisher waits on the branch above the pool, still enough that the water forgets it " +
    "is there.";

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

const countImages = async (page: import("@playwright/test").Page) =>
    (await getInlineImages(page, BLOCK)).flatMap((block) => block.images)
        .length;

let imageId: string;
let contentPageId: string;
let coverId: string;

test("Ctrl+A DEL does not take the picture out of the block [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(
        page,
        ".bookTitle",
        "en",
        "Inline Images Keyboard Delete",
    );
    await addPage(page, "Just Text");
    const pages = await getPages(page);
    coverId = pages.find((p) => !p.isContentPage)!.id;
    contentPageId = (await getContentPages(page))[0].id;
    await goToPage(page, contentPageId);
    await typeInGroup(page, BLOCK, LANG, TEXT);

    imageId = await addInlineImage(page, BLOCK, LANG);
    await changeInlineImagePicture(
        page,
        BLOCK,
        LANG,
        imageId,
        fixtureImage("bird.png"),
    );
    // Count the copies in each language's block before the keystroke. Afterwards every block must
    // still have its copy, and a count taken only afterwards could not tell that apart from a
    // whole block having gone missing.
    const copiesBefore = (await getInlineImages(page, BLOCK)).map(
        (editable) => editable.images.length,
    );
    expect(
        copiesBefore.reduce((a, b) => a + b, 0),
        "The group holds fewer than two copies of the picture, so this test is measuring nothing.",
    ).toBeGreaterThan(1);

    // The action under test: press Ctrl+A and then Delete with real keys, which is what
    // bloom-preventRemoval is there to handle.
    const block = page
        .frameLocator("#page")
        .locator(`${BLOCK} > .bloom-editable[lang="${LANG}"]`)
        .first();
    await block.click();
    await page.keyboard.press("Control+a");
    await page.keyboard.press("Delete");

    // Wait until PreventRemovalOfSomeElements has run. It undoes the deletion on keyup when it
    // sees there are fewer wrappers, and CKEditor's handling of a paste or a deletion is
    // asynchronous. So this waits for the picture to be back in every language, which is the
    // state under test, and not for a fixed time. If the undo never runs, the poll times out and
    // fails with the message below.
    await expect
        .poll(
            async () =>
                (await getInlineImages(page, BLOCK)).map(
                    (editable) => editable.images.length,
                ),
            {
                timeout: 30000,
                message:
                    `Ctrl+A DEL did not leave every language's block holding its one copy of ` +
                    `the picture. bloom-preventRemoval is supposed to undo a keystroke that ` +
                    `removes the wrapper, and nothing carries such a removal to the other ` +
                    `languages, so a block left empty here gets the picture stamped back into ` +
                    `it by normalizeInlineImages at the next page setup -- which reads as "I ` +
                    `deleted it, saved, and it came back".`,
            },
        )
        .toEqual(copiesBefore);

    const after = await getInlineImages(page, BLOCK);
    for (const editable of after)
        expect(
            editable.images.length,
            `Ctrl+A DEL left the "${editable.languageTag}" block with ` +
                `${editable.images.length} copies of the picture instead of 1. ` +
                `bloom-preventRemoval is supposed to undo a keystroke that removes the wrapper, ` +
                `and nothing carries such a removal to the other languages, so a block left ` +
                `empty here gets the picture stamped back into it by normalizeInlineImages at ` +
                `the next page setup -- which reads as "I deleted it, saved, and it came back".`,
        ).toBe(1);
});

test("a picture deleted from its own menu stays deleted through a save and a reload [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    // The picture's menu is how a person deletes it. That deletion has to reach every language,
    // or normalizeInlineImages brings the picture back from a language that still has it.
    await goToPage(page, contentPageId);
    await deleteInlineImage(page, BLOCK, LANG, imageId);
    expect(
        await countImages(page),
        "The menu's Delete left copies of the picture in the group.",
    ).toBe(0);

    // Leave the page and come back, so that Bloom saves it and sets it up again.
    await goToPage(page, coverId);
    await goToPage(page, contentPageId);

    const after = await getInlineImages(page, BLOCK);
    expect(
        after.flatMap((block) => block.images).length,
        `The picture came back after a save and a reload. normalizeInlineImages stamps the ` +
            `canonical editable's images over the others, and getCanonicalInlineImageEditable ` +
            `only considers an editable that HAS images, so one language still holding a copy ` +
            `puts it back in all of them.`,
    ).toBe(0);
});
