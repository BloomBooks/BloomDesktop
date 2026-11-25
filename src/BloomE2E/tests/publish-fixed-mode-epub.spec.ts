// A comic page published as a fixed-layout ePUB is laid out as it is in the Edit tab: the picture,
// the speech bubble, the caption and the small picture over it each sit where the author put them,
// at the size the author gave them, and the words in each box fall in the same place.
//
// Automates the manual test "Publish Fixed Mode ePUB" (Test Case ID 246).
//
// The ePUB is read from the ePUB screen's preview, which shows the very files that Save... zips.
// Save... itself opens the operating system's save dialog, which no test can answer.
//
// What stays with a person: whether the page looks right, which includes how the bubble's outline
// and tail are drawn. Here they are known only to be drawn over the same area of the page.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    waitForPageToSettle,
} from "../helpers/bookMaking";
import {
    canvasElement,
    dragPaletteItemOntoCanvas,
    selectCanvasElement,
    typeInCanvasElement,
} from "../helpers/canvasElements";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    getEpubMode,
    openEpubPublishScreen,
    showEpubPreview,
    showEpubPreviewPage,
} from "../helpers/epubPublish";
import { chooseImageFile } from "../helpers/images";
import { expectSameLayout, getPageLayout } from "../helpers/pageLayout";

test.use({
    collectionSpec: {
        name: "publish-fixed-mode-epub",
        languages: ["en"],
        // Publishing an ePUB needs Pro, and so does publishing a book whose author put a canvas
        // element in it.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const IMAGES = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
);
const BACKGROUND_PICTURE = Path.join(IMAGES, "house-landscape.png");
const OVERLAY_PICTURE = Path.join(IMAGES, "bird.png");

// Long enough to wrap in its bubble, so that a different line break would show.
const BUBBLE_TEXT = "Look at the bird on the roof of our house!";
const CAPTION_TEXT = "The bird sang all morning.";

test("a comic page published as a fixed-mode ePUB is laid out as it is in Edit [Test Case ID 246]", async ({
    page,
    step,
}) => {
    test.setTimeout(420000);

    const comicPage = await step(
        "Make a book with a Canvas page and give the page a picture",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Canvas");
            const [contentPage] = await getContentPages(page);
            await goToPage(page, contentPage.id);
            await chooseImageFile(page, BACKGROUND_PICTURE);
            return contentPage;
        },
    );
    if (!comicPage) throw new Error("The book has no content page.");

    await step(
        "Add a speech bubble, a caption and a small picture over it",
        async () => {
            const bubble = await dragPaletteItemOntoCanvas(page, "speech", {
                xFraction: 0.3,
                yFraction: 0.2,
            });
            await selectCanvasElement(page, bubble);
            await typeInCanvasElement(page, BUBBLE_TEXT);

            const caption = await dragPaletteItemOntoCanvas(page, "caption", {
                xFraction: 0.65,
                yFraction: 0.7,
            });
            await selectCanvasElement(page, caption);
            await typeInCanvasElement(page, CAPTION_TEXT);

            const picture = await dragPaletteItemOntoCanvas(page, "image", {
                xFraction: 0.25,
                yFraction: 0.6,
            });
            await chooseImageFile(
                page,
                OVERLAY_PICTURE,
                canvasElement(page, picture),
            );
        },
    );

    const layoutInEdit = await step(
        "Note where everything is on the page in Edit",
        async () => {
            await waitForPageToSettle(page);
            const layout = await getPageLayout(
                editablePageFrame(page),
                comicPage.id,
            );
            expect(
                layout.items.filter((item) => item.isPicture),
                "test setup: the page in Edit should hold the picture behind the " +
                    "canvas and the small picture",
            ).toHaveLength(2);
            expect(
                layout.items
                    .filter((item) => item.name.startsWith("the text box of"))
                    .map((item) => item.text),
                "test setup: the bubble and the caption should hold what was typed",
            ).toEqual([BUBBLE_TEXT, CAPTION_TEXT]);
            expect(
                layout.items.filter(
                    (item) =>
                        item.name.startsWith("line ") &&
                        item.text === BUBBLE_TEXT,
                ).length,
                "test setup: the bubble's text should take more than one line, so that " +
                    "a different line break in the ePUB would show",
            ).toBeGreaterThan(1);
            return layout;
        },
    );
    if (!layoutInEdit) throw new Error("The page's layout was not read.");

    await step(
        "Open Publish, ePUB, and see that the mode is Fixed",
        async () => {
            await openEpubPublishScreen(page);
            expect(
                await getEpubMode(page),
                "test setup: the ePUB should be about to be made in Fixed mode, the mode the case is about",
            ).toBe("fixed");
        },
    );

    const previewPage = await step(
        "Make the ePUB preview and turn to the comic page",
        async () => {
            await showEpubPreview(page);
            return showEpubPreviewPage(page, comicPage.id);
        },
    );
    if (!previewPage) throw new Error("The ePUB preview never opened.");

    await step("See the page laid out as it is in Edit", async () => {
        expectSameLayout(
            await getPageLayout(previewPage.frame, previewPage.pageId),
            layoutInEdit,
            "The fixed-mode ePUB",
            "the Edit tab",
        );
    });
});
