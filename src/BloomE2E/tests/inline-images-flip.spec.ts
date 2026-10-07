// Flip and Reset Image on an inline image.
//
// WHAT THIS IS ABOUT. The image menu's Flip and Reset Image come from the canvas control registry,
// where they act on the canvas element manager's active element. An inline image is never that
// element, so the inline image menu points both commands at the image's own picture
// (withInlineImageTransforms in inlineImageInteractions.ts). The mirror is saved as the img's CSS
// transform, and it has to reach every copy of the picture: the hidden lang="z" prototype is
// what a language added to the collection later is cloned from.

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
    closeInlineImageMenu,
    flipInlineImage,
    getInlineImageInEveryLanguage,
    getInlineImageMenuCommands,
    resetInlineImage,
} from "../helpers/inlineImages";

test.use({
    collectionSpec: { name: "inline-images-flip", languages: ["en"] },
});

const BLOCK = ".bloom-translationGroup";
const LANG = "en";

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

test("Flip mirrors every copy of an inline image, and Reset Image takes the mirror away [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", "Inline Images Flip");
    await addPage(page, "Just Text");
    const [contentPage] = await getContentPages(page);
    await goToPage(page, contentPage.id);
    await typeInGroup(
        page,
        BLOCK,
        LANG,
        "Some text for the picture to sit in.",
    );
    const imageId = await addInlineImage(page, BLOCK, LANG);
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
        "The group holds fewer than two copies of the picture, so this test cannot see a copy " +
            "being left behind.",
    ).toBeGreaterThan(1);
    expect(copies.map((copy) => copy.pictureTransform)).toEqual(
        copies.map(() => ""),
    );
    const commands = await getInlineImageMenuCommands(
        page,
        BLOCK,
        LANG,
        imageId,
    );
    await closeInlineImageMenu(page);
    expect(
        commands.find((one) => one.id === "EditTab.Image.Flip")?.enabled,
        "The inline image menu should offer Flip, enabled, for a real picture.",
    ).toBe(true);
    expect(
        commands.find((one) => one.id === "EditTab.Image.Reset")?.enabled,
        "Reset Image should be on offer but disabled while there is nothing to reset.",
    ).toBe(false);

    // THE ACTION UNDER TEST: Flip horizontal, chosen from the real submenu.
    await flipInlineImage(page, BLOCK, LANG, imageId, "horizontal");
    for (const copy of await getInlineImageInEveryLanguage(
        page,
        BLOCK,
        imageId,
    ))
        expect(
            copy.pictureTransform,
            `The "${copy.languageTag}" copy was not mirrored left to right.`,
        ).toBe("scale(-1, 1)");

    // THE ACTION UNDER TEST: Flip vertical on top of it.
    await flipInlineImage(page, BLOCK, LANG, imageId, "vertical");
    for (const copy of await getInlineImageInEveryLanguage(
        page,
        BLOCK,
        imageId,
    ))
        expect(copy.pictureTransform).toBe("scale(-1, -1)");

    // THE ACTION UNDER TEST: Reset Image, which puts the picture back the way it arrived.
    await resetInlineImage(page, BLOCK, LANG, imageId);
    for (const copy of await getInlineImageInEveryLanguage(
        page,
        BLOCK,
        imageId,
    )) {
        expect(copy.pictureTransform).toBe("");
        expect(copy.fileName).toBe("bird.png");
    }
});
