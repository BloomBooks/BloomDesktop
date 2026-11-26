// A picture's description keeps the formatting its words were given (bold, italic, a color) when
// the book is published as a flowable ePUB with "Include image descriptions on page" ticked: the
// description is on the page, after the picture, and each word is drawn as it was in the Edit tab.
//
// Automates the manual test "Format Image Description Text" (Test Case ID 571).
//
// The ePUB is read from the ePUB screen's preview, which shows the very files that Save... zips.
// Save... itself opens the operating system's save dialog, which no test can answer.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Locator } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    chooseEpubMode,
    getEpubPreviewImageDescriptions,
    getEpubPreviewPageItems,
    openEpubPublishScreen,
    setIncludeImageDescriptionsOnPage,
    showEpubPreview,
    showEpubPreviewPage,
} from "../helpers/epubPublish";
import {
    kImageDescriptionGroup,
    openImageDescriptionTool,
} from "../helpers/imageDescription";
import { chooseImageFile } from "../helpers/images";
import {
    chooseSelectionTextColor,
    getTextRunFormatting,
    selectTextIn,
    toggleSelectionFormatting,
    type ITextRunFormatting,
} from "../helpers/selectionToolbar";
import { enableToolForBook } from "../helpers/toolbox";

test.use({
    collectionSpec: {
        name: "epub-image-description-formatting",
        languages: ["en"],
        // Publishing an ePUB needs a subscription.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const L1 = "en";
const PICTURE = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

// One word for each kind of formatting, and one left as it was typed.
const BOLD_WORD = "small";
const ITALIC_WORD = "bird";
const COLORED_WORD = "roof";
const PLAIN_WORD = "sits";
const DESCRIPTION = `A ${BOLD_WORD} ${ITALIC_WORD} ${PLAIN_WORD} on the ${COLORED_WORD}.`;
/** The page's own text, under the picture, so that the ePUB has text the description must come before. */
const PAGE_TEXT = "The bird sang all morning.";
/** A color from the text palette. */
const RED = "#ff1616";

/** How each word of the description is drawn, by word. */
type DescriptionFormatting = Record<string, ITextRunFormatting>;

/** How a box draws each of the four words of DESCRIPTION. */
async function getDescriptionFormatting(
    box: Locator,
): Promise<DescriptionFormatting> {
    const formatting: DescriptionFormatting = {};
    for (const word of [BOLD_WORD, ITALIC_WORD, PLAIN_WORD, COLORED_WORD])
        formatting[word] = await getTextRunFormatting(box, word);
    return formatting;
}

test("words of an image description keep their bold, italic and color in a flowable ePUB that shows descriptions on the page [Test Case ID 571]", async ({
    page,
    step,
}) => {
    test.setTimeout(420000);

    const picturePage = await step(
        "Make a book with a page that has a picture and some text",
        async () => {
            const bookFolder = await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Basic Text & Image");
            await enableToolForBook(page, bookFolder, "imageDescription");
            const [contentPage] = await getContentPages(page);
            await goToPage(page, contentPage.id);
            await chooseImageFile(page, PICTURE);
            await typeInGroup(page, ".bloom-translationGroup", L1, PAGE_TEXT);
            return contentPage;
        },
    );
    if (!picturePage) throw new Error("The book has no content page.");

    const formattingInEdit = await step(
        "Describe the picture, and make one word bold, one italic and one red",
        async () => {
            await openImageDescriptionTool(page);
            await typeInGroup(page, kImageDescriptionGroup, L1, DESCRIPTION);
            const box = await clickInGroup(page, kImageDescriptionGroup, L1);
            const plain = await getTextRunFormatting(box, PLAIN_WORD);
            // Sanity check: nothing is formatted yet, so what the ePUB shows later was given here.
            expect(plain).toMatchObject({ bold: false, italic: false });
            expect(plain.color).not.toBe(RED);
            expect(
                await getDescriptionFormatting(box),
                "the description as typed, before any word is formatted",
            ).toEqual({
                [BOLD_WORD]: plain,
                [ITALIC_WORD]: plain,
                [PLAIN_WORD]: plain,
                [COLORED_WORD]: plain,
            });

            await selectTextIn(page, box, BOLD_WORD);
            await toggleSelectionFormatting(page, "bold");
            await selectTextIn(page, box, ITALIC_WORD);
            await toggleSelectionFormatting(page, "italic");
            await selectTextIn(page, box, COLORED_WORD);
            await chooseSelectionTextColor(page, RED);

            const formatting = await getDescriptionFormatting(box);
            expect(
                formatting,
                "the description in the Edit tab, after its words were formatted",
            ).toEqual({
                [BOLD_WORD]: { ...plain, bold: true },
                [ITALIC_WORD]: { ...plain, italic: true },
                [PLAIN_WORD]: plain,
                [COLORED_WORD]: { ...plain, color: RED },
            });
            return formatting;
        },
    );

    await step(
        'Publish as a flowable ePUB with "Include image descriptions on page" ticked',
        async () => {
            await openEpubPublishScreen(page);
            await chooseEpubMode(page, "flowable");
            await setIncludeImageDescriptionsOnPage(page, true);
            await showEpubPreview(page);
        },
    );

    await step(
        "Check that the ePUB shows the description on the page, with each word formatted as it was",
        async () => {
            const { frame } = await showEpubPreviewPage(page, picturePage.id);
            const descriptions = getEpubPreviewImageDescriptions(frame);
            await expect(
                descriptions,
                "the page of the ePUB should show the picture's one description",
            ).toHaveCount(1);
            await expect(descriptions).toBeVisible();
            expect(
                await getEpubPreviewPageItems(frame),
                "the page of the ePUB should show the picture, then its description, then the page's text",
            ).toEqual([
                { kind: "picture", content: Path.basename(PICTURE) },
                { kind: "description", content: DESCRIPTION },
                { kind: "text", content: PAGE_TEXT },
            ]);
            await expect(descriptions).toHaveText(DESCRIPTION, {
                useInnerText: true,
            });
            expect(
                await getDescriptionFormatting(descriptions),
                "the description on the page of the ePUB",
            ).toEqual(formattingInEdit);
        },
    );
});
