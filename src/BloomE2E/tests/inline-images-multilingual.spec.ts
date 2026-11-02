// Tests inline (Word-style) images in a book with more than one language. bookEdit/js/
// inlineImages.ts describes the feature; inline-images.spec.ts covers a book with one language.
//
// A CSS float only wraps the text of the block it is in, so each bloom-editable in the
// translation group has its own copy of the picture, and JavaScript keeps the copies the same
// while editing. These tests check the parts of that design that only a running Bloom can show:
//
//  - Every language block has a copy, but the reader must see only one picture. The CSS shows the
//    copy in the first visible language and hides the others with display: none.
//  - A language added to the collection later gets its copy from the lang="z" prototype block,
//    and only Bloom's C# builds the new block from the prototype.
//  - TranslationGroupManager.UpdateContentLanguageClasses, which sets the language classes when
//    a language is turned on or off, visits every div in a translation group, including the
//    picture's wrapper.
//
// The last test checks that the Talking Book tool, which marks the sentences of a text field for
// recording, does not mark anything inside the wrapper, even though the wrapper is inside the
// field.
//
// The tests run in order, and each starts from the book the previous one left. The second test
// restarts Bloom on a collection with more languages, so every test after it gets the page from
// the `page` fixture again instead of keeping the old one.

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

// The collection starts with one language so that French is added later. The French block is not
// in the page's markup until Bloom builds it from the prototype.
test.use({
    collectionSpec: { name: "inline-images-multi", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// The only translation group on a Just Text page, the languages the tests use, and the language of
// the prototype block that Bloom builds each new language block from.
const BLOCK = ".bloom-translationGroup";
const FIRST_LANG = "en";
const SECOND_LANG = "fr";
const THIRD_LANG = "es";
const PROTOTYPE_LANG = "z";

const BOOK_TITLE = "Inline Images Multilingual";

// Enough text in each language to give the block several lines for the picture to push aside.
const ENGLISH_TEXT =
    "The kingfisher waits on the branch above the pool, still enough that the water forgets " +
    "it is there. When it drops, it drops straight, and the pool closes over the place where " +
    "it went in. The children on the bank have learned to wait as well.";
const FRENCH_TEXT =
    "Le martin-pecheur attend sur la branche au-dessus du bassin, si tranquille que l'eau " +
    "oublie qu'il est la. Quand il plonge, il plonge droit, et le bassin se referme sur " +
    "l'endroit ou il est entre. Les enfants sur la rive ont appris a attendre aussi.";

const SPANISH_TEXT =
    "El martin pescador espera en la rama sobre el estanque, tan quieto que el agua olvida " +
    "que esta alli. Cuando cae, cae recto, y el estanque se cierra sobre el lugar donde entro.";

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

        // THE ACTION UNDER TEST: a right-click in the English text, and a click on the menu
        // command that adds an image, in a book with one language.
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
        // The prototype block is never shown, so its copy is not drawn either.
        expect(prototype.shown).toBe(false);
        // The prototype copy needs this most of all. Without it, the Bloom code that sets the
        // language on every editable element would treat the wrapper as a text box in the new
        // language.
        expect(prototype.contentEditable).toBe("false");
    });

    test("languages added to the collection later inherit the picture from the prototype", async ({
        bloomApp,
    }) => {
        test.setTimeout(300000);
        // The restart kills Bloom, and Bloom saves a page only when the person leaves it, so the
        // test leaves the page first. Otherwise the picture would never be saved.
        const [firstXmatter] = (await getPages(bloomApp.page)).filter(
            (one) => !one.isContentPage,
        );
        await goToPage(bloomApp.page, firstXmatter.id);

        // THE ACTION UNDER TEST: add a second and a third language to the collection, as a person
        // would in the Settings dialog. Bloom restarts and builds the book's French and Spanish
        // blocks from the lang="z" prototype, copying everything in the prototype, including the
        // picture. The third language is for the later test that shows three languages at once.
        const restarted = await restartWithCollectionSettings(bloomApp, {
            languages: [FIRST_LANG, SECOND_LANG, THIRD_LANG],
        });
        await selectBook(restarted, bookFolder);
        await switchTab(restarted, "edit");
        await goToPage(restarted, contentPageId);
        await setContentLanguages(restarted, [FIRST_LANG, SECOND_LANG]);

        const blocks = await getInlineImages(restarted, BLOCK);
        expect(
            blocks.map((block) => block.languageTag).sort(),
            "Adding the languages should have given the group a French and a Spanish block.",
        ).toEqual([THIRD_LANG, FIRST_LANG, SECOND_LANG, PROTOTYPE_LANG].sort());

        const french = await getInlineImage(
            restarted,
            BLOCK,
            SECOND_LANG,
            imageId,
        );
        // The French copy has the same image id, and the same dock, width and picture as the
        // prototype copy.
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

        // Every block still has its copy; none was moved or deleted.
        for (const block of blocks) expect(block.images.length).toBe(1);

        // Exactly one copy is drawn, and it is the one in the first visible language.
        const shownCopies = blocks
            .flatMap((block) => block.images)
            .filter((image) => image.shown);
        expect(
            shownCopies.map((image) => image.languageTag),
            "The rule that shows the picture once per translation group " +
                "(inlineImages.less) should leave exactly one copy painted.",
        ).toEqual([showing[0].languageTag]);

        // The French copy is hidden by that CSS rule while its block is still visible. The French
        // block shows its text, and it contains the float like any other block with a picture.
        const french = blocks.find(
            (block) => block.languageTag === SECOND_LANG,
        );
        expect(french!.visible).toBe(true);
        expect(french!.display).toBe("flow-root");
    });

    test("with three languages showing, or only the second, the reader still sees one picture", async ({
        page,
    }) => {
        await setContentLanguages(page, [FIRST_LANG, SECOND_LANG, THIRD_LANG]);
        await typeInGroup(page, BLOCK, THIRD_LANG, SPANISH_TEXT);

        // THE STATE UNDER TEST: a trilingual page.
        const trilingual = await getInlineImages(page, BLOCK);
        expect(
            trilingual
                .filter((block) => block.visible)
                .map((block) => block.languageTag)
                .sort(),
            "All three languages should be showing, or this is not a trilingual page.",
        ).toEqual([FIRST_LANG, SECOND_LANG, THIRD_LANG].sort());
        expect(
            trilingual
                .flatMap((block) => block.images)
                .filter((image) => image.shown)
                .map((image) => image.languageTag),
            "With three languages showing, only the first language's copy should be painted.",
        ).toEqual([FIRST_LANG]);

        // THE STATE UNDER TEST: the first language turned off, so no visible block has
        // bloom-content1. The CSS still has to draw one copy, the second language's, so the
        // picture does not disappear.
        await setContentLanguages(page, [SECOND_LANG]);
        const secondOnly = await getInlineImages(page, BLOCK);
        expect(
            secondOnly
                .filter((block) => block.visible)
                .map((block) => block.languageTag),
            "Only French should be showing.",
        ).toEqual([SECOND_LANG]);
        expect(
            secondOnly
                .flatMap((block) => block.images)
                .filter((image) => image.shown)
                .map((image) => image.languageTag),
            "With the first language hidden, the second language's copy should be the one painted.",
        ).toEqual([SECOND_LANG]);

        // Go back to the two languages the later tests expect.
        await setContentLanguages(page, [FIRST_LANG, SECOND_LANG]);
        expect(
            (await getInlineImage(page, BLOCK, FIRST_LANG, imageId)).shown,
        ).toBe(true);
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

        // THE ACTION UNDER TEST: a mouse drag of the copy the reader sees, to the left third of
        // the block.
        await dragInlineImageToDock(page, BLOCK, FIRST_LANG, imageId, "left");

        const after = await getInlineImageInEveryLanguage(page, BLOCK, imageId);
        expect(after.length).toBe(4);
        // The hidden French and Spanish copies and the prototype copy all move with the visible
        // one. A copy that did not move would appear on the wrong side as soon as the person
        // changed which languages the book shows.
        expect(after.map((copy) => copy.dock)).toEqual(after.map(() => "left"));
    });

    test("turning the second language off again leaves the picture where it was", async ({
        page,
    }) => {
        // THE ACTION UNDER TEST: go back to one language. Bloom sets the language classes on the
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
        // Only English is showing now, so its copy is the one drawn.
        expect(
            (await getInlineImage(page, BLOCK, FIRST_LANG, imageId)).shown,
        ).toBe(true);
    });

    test("a second image in the same block keeps its own identity in every language", async ({
        page,
    }) => {
        // THE ACTION UNDER TEST: Insert Image again, in a block that already has an image.
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

        // Give it a picture, because the drag below has to grab something visible. A new image
        // starts with a placeholder that has no picture file in the book yet, so there is
        // nothing to grab.
        await changeInlineImagePicture(
            page,
            BLOCK,
            FIRST_LANG,
            secondId,
            fixtureImage("bird.png"),
        );

        // Moving one image leaves the other where it is, in every language.
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

        // THE ACTION UNDER TEST: delete one of the two. The other stays, in every language.
        await deleteInlineImage(page, BLOCK, FIRST_LANG, secondId);
        for (const block of await getInlineImages(page, BLOCK)) {
            expect(block.images.map((image) => image.id)).toEqual([imageId]);
        }
    });

    test("the Talking Book tool marks no sentence inside the picture", async ({
        page,
    }) => {
        // THE ACTION UNDER TEST: open the toolbox. That starts the Talking Book tool on the page,
        // and it marks the sentences to record in every text field.
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
