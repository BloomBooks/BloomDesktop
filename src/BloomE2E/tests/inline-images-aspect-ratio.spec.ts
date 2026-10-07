// Checks that every copy of a picture records the picture's shape.
//
// --inline-image-aspect-ratio gives the wrapper the shape of the picture inside it. It is set from
// the img's naturalWidth and naturalHeight once the file has loaded (setAspectRatioFromNaturalSize,
// called from wireUpImage and again from the img's load event). Each editable of the translation
// group has its own copy of the picture with its own img, and the comment on that load handler
// says each copy therefore records its own ratio and nothing needs to be synced.
//
// But a copy whose img never loaded, or loaded before its src changed, then has no ratio at all,
// and a wrapper with no ratio uses kDefaultInlineImageAspectRatio, 4 / 3. The copy in the
// lang="z" editable matters most. It is hidden, so the person never sees that it is wrong, and
// TranslationGroupManager clones it when a language is added to the collection later. If it has
// the wrong shape, every language added from then on gets the wrong shape.
//
// When only the load event set the ratio, choosing bird.png (274 x 300) left the "en" copy with
// "274 / 300" and the "z" copy with "".

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
    getInlineImages,
} from "../helpers/inlineImages";

test.use({
    collectionSpec: { name: "inline-images-aspect-ratio", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

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

test("every copy of a picture records the picture's shape, the hidden prototype included [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", "Inline Images Aspect Ratio");
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

    // The action under test: choose a real picture, which gives the wrapper a shape to record.
    // Until then it holds a placeholder, which never loads and so keeps the default ratio.
    await changeInlineImagePicture(
        page,
        BLOCK,
        LANG,
        imageId,
        fixtureImage("bird.png"),
    );

    const copies = (await getInlineImages(page, BLOCK))
        .flatMap((block) => block.images)
        .filter((image) => image.id === imageId);
    expect(
        copies.length,
        "The group holds fewer than two copies of the picture, so this test is measuring nothing.",
    ).toBeGreaterThan(1);
    const shown = copies.find((copy) => copy.languageTag === LANG)!;
    expect(
        shown.aspectRatio,
        "The copy the reader sees did not record the picture's shape at all.",
    ).not.toBe("");

    for (const copy of copies)
        expect(
            copy.aspectRatio,
            `The "${copy.languageTag}" copy of the picture records its shape as ` +
                `"${copy.aspectRatio}" while the "${LANG}" copy records "${shown.aspectRatio}". ` +
                `A copy with no ratio falls back to 4 / 3, and the lang="z" prototype is what a ` +
                `language added to the collection later is cloned from, so the wrong shape there ` +
                `is handed to every language added from then on.`,
        ).toBe(shown.aspectRatio);
});
