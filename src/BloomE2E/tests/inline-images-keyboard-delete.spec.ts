// Checks what the keyboard does to a picture in a text block, and that deleting a picture from its
// menu lasts.
//
// Ctrl+A in a block that holds a picture selects the block's text and leaves the picture out
// (inlineImageInteractions.ts). Chrome's own Ctrl+A selects nothing in a block whose first child is
// a picture that is showing. So Ctrl+A then Delete clears the text, and the picture stays in every
// language. The first test checks that, with a picture inserted after the page was set up.
//
// A picture can only be deleted on purpose from its menu. That deletion has to reach every
// language: normalizeInlineImages, which runs when the page is set up, copies the pictures of the
// editable chosen by getCanonicalInlineImageEditable over the group's other editables, and only an
// editable that has pictures can be chosen. So a copy left in one language would bring the picture
// back in all of them. BloomField's PreventRemovalOfSomeElements also counts the pictures a block
// should have, and undoes any keystroke that leaves fewer; the menu's Delete lowers that count
// (updateProtectedPictureCounts in inlineImageInteractions.ts), or the next keystroke would put the
// picture back. The second test checks that the deletion lasts through a keystroke, a save and a
// reload.

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
    getBlockText,
    getInlineImages,
    selectAllAndDeleteInBlock,
    typeAtEndOfBlock,
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

test("Ctrl+A DEL does not take out a picture inserted after the page was set up [Test Case ID 815]", async ({
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
    expect(
        await getBlockText(page, BLOCK, LANG),
        "The block has no text, so this test could not see Delete clear it.",
    ).toContain("kingfisher");

    // The action under test: press Ctrl+A and then Delete with real keys. selectAllAndDeleteInBlock
    // checks that Ctrl+A selected all of the block's text before it presses Delete.
    await selectAllAndDeleteInBlock(page, BLOCK, LANG);

    // CKEditor handles the deletion after the keystroke, so this waits for the text to go rather
    // than for a fixed time.
    await expect
        .poll(async () => getBlockText(page, BLOCK, LANG), {
            timeout: 30000,
            message: "Ctrl+A then Delete did not clear the block's text.",
        })
        .toBe("");

    expect(
        (await getInlineImages(page, BLOCK)).map(
            (editable) => editable.images.length,
        ),
        "Ctrl+A then Delete took a copy of the picture out of a language's block. Ctrl+A is " +
            "supposed to leave pictures out of the selection, and nothing carries a removal made " +
            "with the keyboard to the other languages, so the picture would come back at the next " +
            'page setup, which reads as "I deleted it, saved, and it came back".',
    ).toEqual(copiesBefore);
});

test("a picture deleted from its own menu stays deleted through the next keystroke, a save and a reload [Test Case ID 815]", async ({
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

    // A keystroke in the block next. If the menu's Delete had not lowered the number of pictures
    // BloomField protects, this keystroke would look to it like one that removed a picture, and
    // its browser undo would put the picture back.
    await typeAtEndOfBlock(page, BLOCK, LANG, " The end.");
    expect(
        await countImages(page),
        "Typing after the menu's Delete put the picture back. BloomField still expected the " +
            "block to hold a protected picture, so it undid the keystroke.",
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
