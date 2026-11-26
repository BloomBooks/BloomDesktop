// What a reader gets of an inline image in a book with two languages, and Copy image and Paste
// image between two inline images (Test Case ID 825).
//
// Each language's block keeps its own copy of an inline image, and the CSS shows only the copy in
// the first visible language (inline-images-multilingual.spec.ts covers that in the Edit tab).
// This file follows the picture to where a reader meets it:
//
//  - A source bubble is made from a copy of the other languages' blocks, so the picture's copies
//    come along with the text unless Bloom leaves them out.
//  - The BloomPUB preview (Bloom Player), the PDF and the ePUB preview each lay the page out
//    again from the saved book, with their own style sheets. In each of them the reader must see
//    the picture once, with the English text flowing beside it.
//  - Copy image and Paste image go through Bloom's clipboard and the book folder. Under --e2e
//    Bloom's clipboard is its own, so this leaves the Windows clipboard alone (helpers/clipboard.ts).
//
// The tests run in order, and each starts from the book the one before it left, as the manual
// case does.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { activePreviewPage, goToPreviewPage } from "../helpers/bloomPlayer";
import {
    addPage,
    editablePageFrame,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    setContentLanguages,
    typeInGroup,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    openEpubPublishScreen,
    showEpubPreview,
    showEpubPreviewPage,
} from "../helpers/epubPublish";
import { type IRect, right } from "../helpers/geometry";
import { readImageFileSize } from "../helpers/images";
import {
    addInlineImage,
    changeInlineImagePicture,
    copyInlineImage,
    deleteInlineImage,
    dragInlineImageToDock,
    getInlineImage,
    getInlineImageInEveryLanguage,
    getShownInlineImageLayout,
    type IShownInlinePicture,
    type IShownTextBlock,
    pasteIntoInlineImage,
} from "../helpers/inlineImages";
import {
    type IPicturePoint,
    readImageFileColors,
} from "../helpers/paintedPixels";
import {
    choosePdfMode,
    getPdfPreviewPageCount,
    getPdfPreviewPageDrawing,
} from "../helpers/pdfPrint";
import {
    openPublishDestination,
    showBloomPubPreview,
} from "../helpers/publish";
import {
    countPicturesInSourceBubble,
    getSourceBubble,
} from "../helpers/sourceBubbles";
import { switchTab } from "../helpers/workspace";

// The ePUB screen needs a Pro subscription or better.
test.use({
    collectionSpec: {
        name: "inline-images-publications",
        languages: ["en", "fr"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

test.describe.configure({ mode: "serial" });

const BOOK_TITLE = "Inline Images Published";
// The only translation group on a Just Text page.
const BLOCK = ".bloom-translationGroup";
const ENGLISH = "en";
const FRENCH = "fr";

// At least five lines of English, so that several lines flow beside the picture, and three of
// French, so that the French block is more than a line under the English one.
const ENGLISH_TEXT =
    "The kingfisher waits on the branch above the pool, still enough that the water forgets " +
    "it is there. It watches the shadows move under the surface. When it drops, it drops " +
    "straight, and the pool closes over the place where it went in. A moment later it is back " +
    "on the branch with a fish, and the water is still again. The children on the bank have " +
    "learned to wait as well, and they do not talk while the bird is fishing.";
const FRENCH_TEXT =
    "Le martin-pêcheur attend sur la branche au-dessus de l'étang. Quand il plonge, il plonge " +
    "tout droit, et l'eau se referme sur lui. Un instant plus tard, il est de retour avec un poisson.";

const PICTURE = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

// Set by the first test.
let imageId: string;
let pageId: string;
let bookFolder: string;

/**
 * Check what a reader sees of the Just Text page, wherever it is shown: the picture once, in the
 * English block, at its left, with English text flowing on its right. With `frenchBelow`, also the
 * French block under the English text and the picture, as wide as the English block, its lines
 * starting at its own left edge rather than beside the picture.
 */
function expectPictureOnceAtLeftWithEnglishBeside(
    layout: { pictures: IShownInlinePicture[]; blocks: IShownTextBlock[] },
    where: string,
    frenchBelow: boolean,
): void {
    expect(
        layout.pictures.map((picture) => picture.file),
        `${where} should show the picture exactly once.`,
    ).toEqual(["bird.png"]);
    const [picture] = layout.pictures;
    expect(
        picture.languageTag,
        `${where} should show the copy of the picture in the English block.`,
    ).toBe(ENGLISH);
    const english = layout.blocks.find((b) => b.languageTag === ENGLISH);
    expect(english, `${where} shows no English block.`).toBeDefined();
    const block = english!.rect;
    const pic = picture.rect;
    expect(
        pic.x + pic.width / 2,
        `${where} should show the picture in the left half of the English block.`,
    ).toBeLessThan(block.x + block.width / 2);
    expect(
        pic.x - block.x,
        `${where} should show the picture against the left edge of the English block.`,
    ).toBeLessThan(block.width * 0.1);
    const beside = english!.lines.filter(
        (line) =>
            line.y < pic.y + pic.height - 1 &&
            line.y + line.height > pic.y + 1 &&
            line.x >= right(pic) - 1,
    );
    expect(
        beside.length,
        `${where} should flow lines of English text on the right of the picture.`,
    ).toBeGreaterThan(1);
    expect(
        english!.lines.some((line) => line.x < right(pic) - 1),
        `${where} should flow the English text under the picture as well, so that it wraps ` +
            "around the picture rather than standing in a column beside it.",
    ).toBe(true);
    if (!frenchBelow) return;
    const french = layout.blocks.find((b) => b.languageTag === FRENCH);
    expect(french, `${where} shows no French block.`).toBeDefined();
    const frenchRect: IRect = french!.rect;
    expect(
        frenchRect.y,
        `${where} should show the French text below the picture.`,
    ).toBeGreaterThanOrEqual(pic.y + pic.height - 1);
    expect(
        Math.min(...french!.lines.map((line) => line.y)),
        `${where} should show the French text below the English text.`,
    ).toBeGreaterThanOrEqual(
        Math.max(...english!.lines.map((line) => line.y + line.height)) - 1,
    );
    expect(
        Math.abs(frenchRect.width - block.width),
        `${where} should show the French block as wide as the English one.`,
    ).toBeLessThan(2);
    expect(
        Math.min(...french!.lines.map((line) => line.x)),
        `${where} should start the French lines at the left of their block, not beside the picture.`,
    ).toBeLessThan(pic.x + pic.width / 2);
}

test.describe("inline images in what a reader gets [Test Case ID 825]", () => {
    test("a source bubble shows the other language's text, never the picture", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await typeInGroup(page, ".bookTitle", ENGLISH, BOOK_TITLE);
        await addPage(page, "Just Text");
        const [contentPage] = await getContentPages(page);
        pageId = contentPage.id;
        await goToPage(page, pageId);
        await setContentLanguages(page, [ENGLISH, FRENCH]);
        await typeInGroup(page, BLOCK, ENGLISH, ENGLISH_TEXT);
        await typeInGroup(page, BLOCK, FRENCH, FRENCH_TEXT);
        bookFolder = await findBookFolder(page, BOOK_TITLE);
        imageId = await addInlineImage(page, BLOCK, ENGLISH);
        await changeInlineImagePicture(page, BLOCK, ENGLISH, imageId, PICTURE);
        await dragInlineImageToDock(page, BLOCK, ENGLISH, imageId, "left");
        expectPictureOnceAtLeftWithEnglishBeside(
            await getShownInlineImageLayout(
                editablePageFrame(page).locator(".bloom-page"),
            ),
            "The Edit tab",
            true,
        );

        await setContentLanguages(page, [ENGLISH]);
        const bubble = await getSourceBubble(page, BLOCK, ENGLISH);
        expect(bubble.shown, "No source bubble opened beside the block.").toBe(
            true,
        );
        expect(
            bubble.texts.find((text) => text.lang === FRENCH)?.text,
            "The source bubble should show the French text.",
        ).toBe(FRENCH_TEXT);
        // The French block the bubble is made from holds a copy of the picture.
        expect(
            (await getInlineImageInEveryLanguage(page, BLOCK, imageId)).some(
                (copy) => copy.languageTag === FRENCH,
            ),
        ).toBe(true);
        expect(
            await countPicturesInSourceBubble(page, BLOCK),
            "The source bubble should hold no picture.",
        ).toBe(0);

        await setContentLanguages(page, [ENGLISH, FRENCH]);
    });

    test("the BloomPUB preview shows the picture once, beside the English, with the French below", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await openPublishDestination(page, "BloomPUB");
        const player = await showBloomPubPreview(page);
        await goToPreviewPage(page, player, pageId);
        expectPictureOnceAtLeftWithEnglishBeside(
            await getShownInlineImageLayout(activePreviewPage(player)),
            "The BloomPUB preview",
            true,
        );
    });

    test("the Simple PDF lays the page out as the Edit tab does", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await choosePdfMode(page, "Simple");
        const pages = await getPages(page);
        expect(
            await getPdfPreviewPageCount(page),
            "The PDF should have one page for each page of the book.",
        ).toBe(pages.length);
        const { pictures, texts } = await getPdfPreviewPageDrawing(
            page,
            pages.findIndex((p) => p.id === pageId),
        );
        // The collection's subscription stamps its branding on every page, as a picture of its
        // own, so the bird is told apart by the size of its file.
        const bird = readImageFileSize(PICTURE);
        const birds = pictures.filter(
            (p) => p.width === bird.width && p.height === bird.height,
        );
        expect(
            birds.length,
            "The PDF should draw the picture once on the Just Text page. It draws: " +
                JSON.stringify(pictures),
        ).toBe(1);
        const picture = birds[0].box;

        // Each piece of text the page draws is a line of one of the two languages.
        const squeeze = (text: string) => text.replace(/\s+/g, "");
        const english = texts.filter(
            (run) =>
                squeeze(run.text) &&
                squeeze(ENGLISH_TEXT).includes(squeeze(run.text)),
        );
        const french = texts.filter(
            (run) =>
                squeeze(run.text) &&
                squeeze(FRENCH_TEXT).includes(squeeze(run.text)),
        );
        expect(
            squeeze(english.map((run) => run.text).join("")),
            "The PDF should draw all the English text, in order.",
        ).toBe(squeeze(ENGLISH_TEXT));
        expect(
            squeeze(french.map((run) => run.text).join("")),
            "The PDF should draw all the French text, in order.",
        ).toBe(squeeze(FRENCH_TEXT));

        // Millimetres, measured from the bottom left of the paper.
        const textLeft = Math.min(...[...english, ...french].map((r) => r.x));
        expect(
            Math.abs(picture.left - textLeft),
            "The PDF should put the picture against the left edge of the text block.",
        ).toBeLessThan(2);
        expect(
            english.filter(
                (run) =>
                    run.y > picture.bottom &&
                    run.y < picture.top &&
                    run.x >= picture.right - 0.5,
            ).length,
            "The PDF should flow lines of English text on the right of the picture.",
        ).toBeGreaterThan(1);
        expect(
            english.some(
                (run) => run.y < picture.bottom && run.x < picture.right,
            ),
            "The PDF should flow the English text under the picture as well.",
        ).toBe(true);
        const lowestEnglish = Math.min(...english.map((run) => run.y));
        for (const run of french)
            expect(
                run.y,
                `The PDF should draw the French line "${run.text}" below the picture and the English text.`,
            ).toBeLessThan(Math.min(picture.bottom, lowestEnglish));
        expect(
            Math.abs(Math.min(...french.map((run) => run.x)) - textLeft),
            "The PDF should start the French lines at the left of the text block.",
        ).toBeLessThan(2);
    });

    test("the ePUB preview shows the picture once, beside the English text", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await openEpubPublishScreen(page);
        await showEpubPreview(page);
        const { frame } = await showEpubPreviewPage(page, pageId);
        expectPictureOnceAtLeftWithEnglishBeside(
            await getShownInlineImageLayout(frame.locator(".bloom-page")),
            "The ePUB preview",
            false,
        );
    });

    test("Copy image and Paste image put a copy of the picture in a second inline image", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await switchTab(page, "edit");
        await goToPage(page, pageId);
        await setContentLanguages(page, [ENGLISH]);
        const firstBefore = await getInlineImageInEveryLanguage(
            page,
            BLOCK,
            imageId,
        );

        const copied = await copyInlineImage(page, BLOCK, ENGLISH, imageId);
        const original = await readPictureSample(page, PICTURE);
        expect(
            await readPictureSample(page, copied),
            "Copy image should put the picture itself on the clipboard.",
        ).toEqual(original);

        const secondId = await addInlineImage(page, BLOCK, ENGLISH);
        const placeholder = await getInlineImage(
            page,
            BLOCK,
            ENGLISH,
            secondId,
        );
        expect(placeholder.fileName).toBe("placeHolder.png");
        expect(
            placeholder.dock,
            "Insert Image should put the placeholder at the right of the block.",
        ).toBe("right");
        expect(placeholder.shown).toBe(true);

        await pasteIntoInlineImage(page, BLOCK, ENGLISH, secondId);
        const pasted = await getInlineImageInEveryLanguage(
            page,
            BLOCK,
            secondId,
        );
        for (const copy of pasted)
            expect(
                await readPictureSample(
                    page,
                    Path.join(bookFolder, copy.fileName),
                ),
                `The ${copy.languageTag} copy of the second image should hold a copy of the first picture.`,
            ).toEqual(original);
        expect(
            (await getInlineImage(page, BLOCK, ENGLISH, secondId)).shown,
        ).toBe(true);

        const firstAfter = await getInlineImageInEveryLanguage(
            page,
            BLOCK,
            imageId,
        );
        expect(
            firstAfter.map(describePlacement),
            "The first picture should still be at the left, unchanged.",
        ).toEqual(firstBefore.map(describePlacement));
        expect(firstAfter.every((copy) => copy.dock === "left")).toBe(true);

        await deleteInlineImage(page, BLOCK, ENGLISH, secondId);
        expect(
            (await getInlineImageInEveryLanguage(page, BLOCK, imageId)).map(
                describePlacement,
            ),
        ).toEqual(firstBefore.map(describePlacement));
    });
});

/** What a copy of an inline image shows, and where. */
function describePlacement(copy: {
    languageTag: string;
    dock: string;
    widthPercent: number;
    offsetPx: number;
    aspectRatio: string;
    pictureTransform: string;
    fileName: string;
}) {
    return {
        languageTag: copy.languageTag,
        dock: copy.dock,
        widthPercent: copy.widthPercent,
        offsetPx: copy.offsetPx,
        aspectRatio: copy.aspectRatio,
        pictureTransform: copy.pictureTransform,
        fileName: copy.fileName,
    };
}

/**
 * A picture file's size, and its colours on a 9 by 9 grid of points, for telling whether two files
 * hold the same picture when one may have been written again (the clipboard and the book folder
 * each keep their own file). Fully transparent points count as the same whatever colour they carry.
 */
async function readPictureSample(page: Page, file: string) {
    const points: IPicturePoint[] = [];
    for (let row = 0; row < 9; row++)
        for (let column = 0; column < 9; column++)
            points.push({
                name: `${column},${row}`,
                x: (column + 0.5) / 9,
                y: (row + 0.5) / 9,
            });
    const colors = await readImageFileColors(page, file, points);
    return {
        size: readImageFileSize(file),
        colors: colors.map((color) =>
            color.a === 0 ? { r: 0, g: 0, b: 0, a: 0 } : color,
        ),
    };
}
