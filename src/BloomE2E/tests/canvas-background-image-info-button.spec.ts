// The large image information button at the top left of a Canvas page's background picture, which
// a person clicks to set the picture's credits, copyright and license. Notion test case
// "Background Image Copyright Button" (Test Case ID 341).
//
// Its four states:
// (a) no picture chosen (the placeholder): the button never shows;
// (b) the picture has its copyright: the blue copyright icon, shown only while the pointer is over
//     the canvas and the background picture is selected;
// (c) the picture lacks its copyright and is selected: the solid red icon, always shown;
// (d) the picture lacks its copyright and is not selected: the subtler red-on-transparent icon,
//     always shown.
// Which icon shows is read as the icon file the button's style picks, since the colours are the
// icons' own drawing.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    clearCanvasElementSelection,
    dragPaletteItemOntoCanvas,
    expectChangeImageButton,
    expectImageInfoButton,
    hoverOverCanvas,
    imageInfoButton,
    movePointerOffPage,
    selectCanvasElement,
} from "../helpers/canvasElements";
import { setCopyrightHolder } from "../helpers/copyrightAndLicense";
import { chooseImageFile } from "../helpers/images";
import { realClick } from "../helpers/realClick";
import {
    getCurrentUiLanguageTag,
    setUiStateViaApi,
} from "../helpers/uiLanguage";

test.use({
    collectionSpec: {
        name: "canvas-background-info-button",
        languages: ["en"],
    },
});

const kBird = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

// A copyright holder unusual enough that finding it on the picture cannot be a coincidence.
const kHolder = "Kingfisher Pictures";

// The icons the button's style picks for each state (editMode.less).
const kKnownIcon = "imageMetaDataButton.svg";
const kMissingSelectedIcon = "imageMissingMetaData.svg";
const kMissingUnselectedIcon = "MissingInfoIndicator.svg";

// Where the pointer rests to hover the canvas: away from the button at the top left, from the
// speech bubble, and from the canvas's centre, where a click selects the background picture.
const kEmptySpot = { xFraction: 0.75, yFraction: 0.7 };
// The background picture is the first canvas element on a Canvas page.
const kBackgroundImage = 0;

/** The copyright Bloom has written onto the background picture of the page being edited. */
async function getPictureCopyright(page: Page): Promise<string> {
    return editablePageFrame(page)
        .locator(".bloom-backgroundImage .bloom-imageContainer img")
        .first()
        .evaluate((img) => img.getAttribute("data-copyright") ?? "");
}

test("the background picture's image information button in its four states [Test Case ID 341]", async ({
    bloomApp,
    step,
}) => {
    test.setTimeout(400000);

    // The Copyright and License dialog's fields and buttons are found by their English wording,
    // and a freshly launched Bloom takes its UI language from the operating system.
    if ((await getCurrentUiLanguageTag(bloomApp.page)) !== "en")
        await setUiStateViaApi(bloomApp, "en", false);
    const page = bloomApp.page;

    await step(
        "(a) With no picture chosen, the button never shows",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Canvas");
            await selectCanvasElement(page, kBackgroundImage);
            await hoverOverCanvas(page, kEmptySpot);
            // The Change Image button shows in exactly this state, so the page has finished
            // setting up its picture buttons when the information button is looked for.
            await expectChangeImageButton(
                page,
                true,
                "The Change Image button does not show over the selected placeholder picture.",
            );
            await expect(
                imageInfoButton(page),
                "The placeholder picture has an image information button.",
            ).toHaveCount(0);
        },
    );

    await step(
        "Put a picture with no copyright on the background",
        async () => {
            await chooseImageFile(page, kBird);
            expect(
                await getPictureCopyright(page),
                "Sanity check: the picture should start with no copyright.",
            ).toBe("");
        },
    );

    await step(
        "(d) With its copyright missing and the picture not selected, the subtle red icon always shows",
        async () => {
            await clearCanvasElementSelection(page);
            await movePointerOffPage(page);
            await expectImageInfoButton(
                page,
                {
                    showing: true,
                    missingInfo: true,
                    icon: kMissingUnselectedIcon,
                },
                "With the pointer off the page, the button over an unselected picture that lacks its copyright is not the subtle red icon.",
            );
            await hoverOverCanvas(page, kEmptySpot);
            await expectImageInfoButton(
                page,
                {
                    showing: true,
                    missingInfo: true,
                    icon: kMissingUnselectedIcon,
                },
                "With the pointer over the canvas, the button over an unselected picture that lacks its copyright is not the subtle red icon.",
            );
        },
    );

    await step(
        "(c) With its copyright missing and the picture selected, the solid red icon always shows",
        async () => {
            await selectCanvasElement(page, kBackgroundImage);
            await movePointerOffPage(page);
            await expectImageInfoButton(
                page,
                {
                    showing: true,
                    missingInfo: true,
                    icon: kMissingSelectedIcon,
                },
                "With the pointer off the page, the button over a selected picture that lacks its copyright is not the solid red icon.",
            );
            await hoverOverCanvas(page, kEmptySpot);
            await expectImageInfoButton(
                page,
                {
                    showing: true,
                    missingInfo: true,
                    icon: kMissingSelectedIcon,
                },
                "With the pointer over the canvas, the button over a selected picture that lacks its copyright is not the solid red icon.",
            );
        },
    );

    await step(
        "Click the button and give the picture a copyright holder",
        async () => {
            await realClick(imageInfoButton(page));
            await setCopyrightHolder(page, kHolder);
            await expect
                .poll(async () => getPictureCopyright(page), {
                    timeout: 30000,
                    message:
                        "OK in the dialog opened from the button did not put the copyright on the picture.",
                })
                .toContain(kHolder);
        },
    );

    await step(
        "(b) With its copyright known, the blue icon shows while the pointer is over the selected picture",
        async () => {
            await selectCanvasElement(page, kBackgroundImage);
            await hoverOverCanvas(page, kEmptySpot);
            await expectImageInfoButton(
                page,
                { showing: true, missingInfo: false, icon: kKnownIcon },
                "The button over a selected picture with its copyright is not the blue copyright icon while the pointer is over the canvas.",
            );
            await movePointerOffPage(page);
            await expectImageInfoButton(
                page,
                { showing: false, missingInfo: false },
                "The button over a picture with its copyright stays after the pointer left the page.",
            );
        },
    );

    await step(
        "(b) With its copyright known and another element on the canvas, it shows only while the picture is selected",
        async () => {
            const bubble = await dragPaletteItemOntoCanvas(page, "speech", {
                xFraction: 0.3,
                yFraction: 0.25,
            });
            await selectCanvasElement(page, bubble);
            await hoverOverCanvas(page, kEmptySpot);
            await expectImageInfoButton(
                page,
                { showing: false, missingInfo: false },
                "The button over a picture with its copyright shows while a speech bubble is selected.",
            );

            await clearCanvasElementSelection(page);
            await hoverOverCanvas(page, kEmptySpot);
            await expectImageInfoButton(
                page,
                { showing: false, missingInfo: false },
                "The button over a picture with its copyright shows while nothing is selected.",
            );

            await selectCanvasElement(page, kBackgroundImage);
            await hoverOverCanvas(page, kEmptySpot);
            await expectImageInfoButton(
                page,
                { showing: true, missingInfo: false, icon: kKnownIcon },
                "The button over a picture with its copyright does not show while the picture is selected and the pointer is over the canvas.",
            );
            await movePointerOffPage(page);
            await expectImageInfoButton(
                page,
                { showing: false, missingInfo: false },
                "The button over a picture with its copyright stays after the pointer left the page.",
            );
        },
    );
});
