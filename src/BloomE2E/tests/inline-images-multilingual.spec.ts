// Inline (Word-style) images in a book with more than one language. The feature is described in
// INLINE-IMAGES-PLAN.md; inline-images.spec.ts covers one language.
//
// WHAT THIS FILE COVERS, AND WHY IT IS THE E2E PART. An inline image is not one element. A float
// only wraps the text of the block it sits in, so the picture has to live inside each
// bloom-editable of the translation group, and the copies are kept the same by edit-time
// JavaScript. That design has three consequences that only a real Bloom can show:
//
//  - the copies exist in every language block at once, but the reader must see one picture, so the
//    CSS shows the first showing language's copy and hides the rest with display: none;
//  - the copy in the lang="z" prototype block is what a language added to the collection later
//    inherits, and only Bloom's own C# builds that new block out of the prototype;
//  - Bloom's language-stamping sweep (TranslationGroupManager.UpdateContentLanguageClasses) walks
//    every div inside a translation group, the picture wrapper included, so turning a language on
//    or off runs that sweep over the wrapper.
//
// The last test covers a separate collision: the Talking Book tool marks the sentences of a text
// field for recording, and the wrapper is content of that field.
//
// The tests are serial: each starts from the book the one before it left behind. The second test
// restarts Bloom on a collection with a second language, so every test after it takes the page
// from the `page` fixture again rather than holding on to an old one.

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
    setContentLanguages,
    typeInGroup,
} from "../helpers/bookMaking";
import { selectBook } from "../helpers/collection";
import { restartWithCollectionSettings } from "../helpers/collectionSettings";
import {
    addInlineImage,
    changeInlineImagePicture,
    deleteInlineImage,
    dragInlineImageToDock,
    getInlineImage,
    getInlineImageInEveryLanguage,
    getInlineImages,
    getNarrationInsideInlineImages,
} from "../helpers/inlineImages";
import {
    getNarrationSentences,
    openToolboxWithTalkingBook,
} from "../helpers/talkingBook";
import { switchTab } from "../helpers/workspace";

// The collection starts with one language, so that the second one is genuinely added later: the
// French block does not exist in the page's markup until Bloom builds it out of the prototype.
test.use({
    collectionSpec: { name: "inline-images-multi", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// The only translation group of a Just Text page, the two languages, and the prototype block every
// new language block is built from.
const BLOCK = ".bloom-translationGroup";
const FIRST_LANG = "en";
const SECOND_LANG = "fr";
const PROTOTYPE_LANG = "z";

const BOOK_TITLE = "Inline Images Multilingual";

// Enough text in each language that the block has several lines for the picture to displace.
const ENGLISH_TEXT =
    "The kingfisher waits on the branch above the pool, still enough that the water forgets " +
    "it is there. When it drops, it drops straight, and the pool closes over the place where " +
    "it went in. The children on the bank have learned to wait as well.";
const FRENCH_TEXT =
    "Le martin-pecheur attend sur la branche au-dessus du bassin, si tranquille que l'eau " +
    "oublie qu'il est la. Quand il plonge, il plonge droit, et le bassin se referme sur " +
    "l'endroit ou il est entre. Les enfants sur la rive ont appris a attendre aussi.";

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

// The image the file works on, the page it is on, and the book's folder. Set by the first test.
let imageId: string;
let contentPageId: string;
let bookFolder: string;

test.describe("inline images in a book with two languages [Test Case ID 815]", () => {
    test("with one language in the collection, the picture goes in that block and in the prototype", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
        await addPage(page, "Just Text");
        const [contentPage] = await getContentPages(page);
        contentPageId = contentPage.id;
        await goToPage(page, contentPageId);
        await typeInGroup(page, BLOCK, FIRST_LANG, ENGLISH_TEXT);
        bookFolder = await findBookFolder(page, BOOK_TITLE);

        const before = await getInlineImages(page, BLOCK);
        expect(
            before.map((block) => block.languageTag).sort(),
            "The group should hold the one language and the prototype, and no French block yet, " +
                "or the test after this one proves nothing.",
        ).toEqual([FIRST_LANG, PROTOTYPE_LANG]);

        // THE ACTION UNDER TEST: a real right-click in the English text, and a real click on Add
        // Image, in a book with one language.
        imageId = await addInlineImage(page, BLOCK, FIRST_LANG);
        await changeInlineImagePicture(
            page,
            BLOCK,
            FIRST_LANG,
            imageId,
            fixtureImage("bird.png"),
        );

        const prototype = await getInlineImage(
            page,
            BLOCK,
            PROTOTYPE_LANG,
            imageId,
        );
        expect(prototype.dock).toBe("right");
        expect(prototype.fileName).toBe("bird.png");
        // The prototype block is never shown to anybody, so its copy is not painted either.
        expect(prototype.shown).toBe(false);
        // Load-bearing in the prototype above all: it is the only thing stopping Bloom's
        // language-stamping sweep from treating the wrapper as a text box of the new language.
        expect(prototype.contentEditable).toBe("false");
    });

    test("a language added to the collection later inherits the picture from the prototype", async ({
        bloomApp,
    }) => {
        test.setTimeout(300000);
        // The restart kills Bloom, and Bloom writes a page only when the book leaves it, so the
        // book has to leave the page first or the picture is never saved at all.
        const [firstXmatter] = (await getPages(bloomApp.page)).filter(
            (one) => !one.isContentPage,
        );
        await goToPage(bloomApp.page, firstXmatter.id);

        // THE ACTION UNDER TEST: a second collection language, which is what a person adds in the
        // Settings dialog. Bloom restarts, and builds the book's French block out of the lang="z"
        // prototype -- carrying whatever the prototype holds, the picture included.
        const restarted = await restartWithCollectionSettings(bloomApp, {
            languages: [FIRST_LANG, SECOND_LANG],
        });
        await selectBook(restarted, bookFolder);
        await switchTab(restarted, "edit");
        await goToPage(restarted, contentPageId);
        await setContentLanguages(restarted, [FIRST_LANG, SECOND_LANG]);

        const blocks = await getInlineImages(restarted, BLOCK);
        expect(
            blocks.map((block) => block.languageTag).sort(),
            "Adding the language should have given the group a French block.",
        ).toEqual([FIRST_LANG, SECOND_LANG, PROTOTYPE_LANG]);

        const french = await getInlineImage(
            restarted,
            BLOCK,
            SECOND_LANG,
            imageId,
        );
        // The inherited copy is the same image, not a second one, and it comes with the geometry
        // and the picture the prototype held.
        expect(french.dock).toBe("right");
        expect(french.widthPercent).toBe(40);
        expect(french.fileName).toBe("bird.png");
        expect(french.contentEditable).toBe("false");
    });

    test("the reader sees one picture: the outranked copies stay in the markup and are hidden", async ({
        page,
    }) => {
        await typeInGroup(page, BLOCK, SECOND_LANG, FRENCH_TEXT);

        const blocks = await getInlineImages(page, BLOCK);
        const showing = blocks.filter((block) => block.visible);
        expect(
            showing.length,
            "The book should be showing two languages at this point.",
        ).toBe(2);

        // Every block holds its copy, so nothing was moved or deleted.
        for (const block of blocks) expect(block.images.length).toBe(1);

        // Exactly one copy is painted, and it is the first showing language's.
        const shownCopies = blocks
            .flatMap((block) => block.images)
            .filter((image) => image.shown);
        expect(
            shownCopies.map((image) => image.languageTag),
            "The rule that shows the picture once per translation group " +
                "(inlineImages.less) should leave exactly one copy painted.",
        ).toEqual([showing[0].languageTag]);

        // The hidden copy is hidden by that rule, not by its block being hidden: the French block
        // is showing its text, and it contains the float like any other block holding a picture.
        const french = blocks.find(
            (block) => block.languageTag === SECOND_LANG,
        );
        expect(french!.visible).toBe(true);
        expect(french!.display).toBe("flow-root");
    });

    test("dragging the one picture a reader can see moves the hidden copies too", async ({
        page,
    }) => {
        const before = await getInlineImageInEveryLanguage(
            page,
            BLOCK,
            imageId,
        );
        expect(
            before.map((copy) => copy.dock),
            "Every copy should start docked right, or this test cannot tell a move from a " +
                "starting state.",
        ).toEqual(before.map(() => "right"));

        // THE ACTION UNDER TEST: a real drag of the copy the reader sees, to the left third of
        // the block.
        await dragInlineImageToDock(page, BLOCK, FIRST_LANG, imageId, "left");

        const after = await getInlineImageInEveryLanguage(page, BLOCK, imageId);
        expect(after.length).toBe(3);
        // The hidden French copy and the prototype copy follow the shown one. A copy left behind
        // would show the wrong side as soon as the book's language choice changed.
        expect(after.map((copy) => copy.dock)).toEqual(after.map(() => "left"));
    });

    test("turning the second language off again leaves the picture where it was", async ({
        page,
    }) => {
        // THE ACTION UNDER TEST: back to One Language, which runs Bloom's language sweep over the
        // wrapper again and hides the French block.
        await setContentLanguages(page, [FIRST_LANG]);

        for (const copy of await getInlineImageInEveryLanguage(
            page,
            BLOCK,
            imageId,
        )) {
            expect(copy.dock).toBe("left");
            expect(copy.widthPercent).toBe(40);
            expect(copy.fileName).toBe("bird.png");
            expect(copy.contentEditable).toBe("false");
        }
        // English is showing alone now, so its copy is the painted one.
        expect(
            (await getInlineImage(page, BLOCK, FIRST_LANG, imageId)).shown,
        ).toBe(true);
    });

    test("a second image in the same block keeps its own identity in every language", async ({
        page,
    }) => {
        // THE ACTION UNDER TEST: a second real Add Image in a block that already has one.
        const secondId = await addInlineImage(page, BLOCK, FIRST_LANG);
        expect(
            secondId,
            "The second image should have an id of its own; sharing one would make the two " +
                "images the same image in the eyes of every sync.",
        ).not.toBe(imageId);

        for (const block of await getInlineImages(page, BLOCK)) {
            expect(
                block.images.map((image) => image.id).sort(),
                `The ${block.languageTag} block should hold both images.`,
            ).toEqual([imageId, secondId].sort());
        }

        // A picture, because a drag has to grab something the reader can see: the placeholder an
        // image starts with has no picture file in the book yet, so it has nothing to grab.
        await changeInlineImagePicture(
            page,
            BLOCK,
            FIRST_LANG,
            secondId,
            fixtureImage("bird.png"),
        );

        // Moving one leaves the other where it is, in every language.
        await dragInlineImageToDock(
            page,
            BLOCK,
            FIRST_LANG,
            secondId,
            "middle",
        );
        for (const copy of await getInlineImageInEveryLanguage(
            page,
            BLOCK,
            secondId,
        ))
            expect(copy.dock).toBe("middle");
        for (const copy of await getInlineImageInEveryLanguage(
            page,
            BLOCK,
            imageId,
        ))
            expect(copy.dock).toBe("left");

        // THE ACTION UNDER TEST: deleting one of the two. The other survives it, in every
        // language.
        await deleteInlineImage(page, BLOCK, FIRST_LANG, secondId);
        for (const block of await getInlineImages(page, BLOCK)) {
            expect(block.images.map((image) => image.id)).toEqual([imageId]);
        }
    });

    test("the Talking Book tool marks no sentence inside the picture", async ({
        page,
    }) => {
        // THE ACTION UNDER TEST: opening the toolbox, which puts the Talking Book tool to work on
        // the page and makes it mark the recordable sentences of every text field.
        await openToolboxWithTalkingBook(page);

        const sentences = await getNarrationSentences(page);
        expect(
            sentences.length,
            "The tool marked nothing at all, so this test would pass whatever the wrapper held.",
        ).toBeGreaterThan(0);

        expect(
            await getNarrationInsideInlineImages(page),
            "The tool marked something inside the picture wrapper. A recorder would be asked to " +
                "read a picture, and an audio-sentence span would be saved into picture markup.",
        ).toEqual([]);

        // The tool left the image alone.
        const shown = await getInlineImage(page, BLOCK, FIRST_LANG, imageId);
        expect(shown.dock).toBe("left");
        expect(shown.fileName).toBe("bird.png");
    });
});
