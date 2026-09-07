// Deleting a picture with the keyboard rather than from its menu.
//
// WHAT THIS IS ABOUT. Two mechanisms meet here, and each is fine on its own.
//
// BloomField's PreventRemovalOfSomeElements is the guard: the wrapper carries
// bloom-preventRemoval, and a keystroke that leaves the field with fewer of those than it had
// before the keystroke gets document.execCommand("undo"). Ctrl+A DEL is what it exists for.
//
// The other is normalizeInlineImages at page setup, which stamps the canonical editable's images
// over the group's other editables. getCanonicalInlineImageEditable only ever considers an
// editable that HAS images, so an editable the person emptied is never the authority: the picture
// comes back from whichever language still holds one. And nothing calls
// syncInlineImagesFromEditable after ordinary typing or cutting -- its callers are all
// inline-image operations -- so a keyboard deletion is never carried to the other languages in
// the first place.
//
// So if the guard ever fails to restore, the person gets "I deleted the picture, saved, came back,
// and it is here again", with no way to tell why. This test asks the browser which it is: does
// Ctrl+A DEL leave the picture in place, and does it survive a save and a reload either way.

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
    // One copy per language's block, counted before the keystroke: what has to be true afterwards
    // is that every one of them still has its copy, and a count taken afterwards could not tell
    // that from a block having gone missing altogether.
    const copiesBefore = (await getInlineImages(page, BLOCK)).map(
        (editable) => editable.images.length,
    );
    expect(
        copiesBefore.reduce((a, b) => a + b, 0),
        "The group holds fewer than two copies of the picture, so this test is measuring nothing.",
    ).toBeGreaterThan(1);

    // THE ACTION UNDER TEST: the gesture bloom-preventRemoval exists for, with real keys.
    const block = page
        .frameLocator("#page")
        .locator(`${BLOCK} > .bloom-editable[lang="${LANG}"]`)
        .first();
    await block.click();
    await page.keyboard.press("Control+a");
    await page.keyboard.press("Delete");

    // What we are waiting for is the protection having run: BloomField takes the deletion back
    // when it sees the wrapper count has dropped (PreventRemovalOfSomeElements, on keyup), and
    // CKEditor's own work around a paste or a deletion is asynchronous. So the wait is for the
    // picture to be there in every language -- the state under test -- rather than for a length
    // of time. A protection that never runs polls to the timeout and fails with the message
    // below.
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
    console.log(
        `[probe] after Ctrl+A DEL: ${JSON.stringify(after.map((b) => [b.languageTag, b.images.length]))}`,
    );
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
    // The menu is the way to delete a picture, so this is the other half: the deletion has to
    // reach every language, or normalizeInlineImages brings it back from the one it missed.
    await goToPage(page, contentPageId);
    await deleteInlineImage(page, BLOCK, LANG, imageId);
    expect(
        await countImages(page),
        "The menu's Delete left copies of the picture in the group.",
    ).toBe(0);

    // Leave the page and come back, which is a save and a fresh page setup.
    await goToPage(page, coverId);
    await goToPage(page, contentPageId);

    const after = await getInlineImages(page, BLOCK);
    console.log(
        `[probe] after the round trip: ${JSON.stringify(after.map((b) => [b.languageTag, b.images.length]))}`,
    );
    expect(
        after.flatMap((block) => block.images).length,
        `The picture came back after a save and a reload. normalizeInlineImages stamps the ` +
            `canonical editable's images over the others, and getCanonicalInlineImageEditable ` +
            `only considers an editable that HAS images, so one language still holding a copy ` +
            `puts it back in all of them.`,
    ).toBe(0);
});
