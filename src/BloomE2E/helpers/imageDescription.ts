// The Image Description tool: a toolbox tool that, while it is the expanded one, shows beside each
// picture of the page a text box for describing that picture. The boxes are a translation group of
// their own (kImageDescriptionGroup) inside the picture's canvas, which the tool adds to a picture
// that has none yet.

import * as fs from "node:fs";
import { expect, type Locator, type Page } from "@playwright/test";
import { bookHtmlPath } from "./bookHtml";
import { editablePageFrame, goToPage } from "./bookMaking";
import { expandTool, toolboxFrame } from "./toolbox";

// A page's own pictures: its top-level canvases, not the picture canvas elements placed inside one,
// which get no description.
const kPagePicture =
    ".bloom-page .bloom-canvas:not(.bloom-canvas .bloom-canvas)";

/**
 * The class of the translation group that holds a picture's descriptions, to pass to typeInGroup
 * and clickInGroup.
 */
export const kImageDescriptionGroup = ".bloom-imageDescription";

/**
 * Expand the Image Description tool in the toolbox, the way a person does, and wait until the page
 * being edited shows a description box for a picture. The book must have the tool on
 * (enableToolForBook or setToolTurnedOn in helpers/toolbox.ts), and the page must have a picture.
 */
export async function openImageDescriptionTool(page: Page): Promise<void> {
    await expandTool(page, "imageDescription", ".imgDescLabelBlock");
    await editablePageFrame(page)
        .locator(`${kImageDescriptionGroup} .bloom-editable:visible`)
        .first()
        .waitFor({ state: "visible", timeout: 30000 });
}

/**
 * The Image Description tool's "This image should not be described." box. It speaks for the picture
 * whose description box last had focus, or else for the first picture of the page being edited.
 * Found by its class, not its localized label. The tool must be open (openImageDescriptionTool).
 */
export function imageNotDescribedBox(page: Page): Locator {
    return toolboxFrame(page).locator(
        ".imageDescriptionCheck input[type='checkbox']",
    );
}

/**
 * For each picture of the page being edited, in page order, whether Bloom has marked it as one
 * that should not be described. The tool keeps that mark as `aria-hidden="true"` on the picture's
 * canvas.
 */
export async function getPicturesMarkedNotDescribed(
    page: Page,
): Promise<boolean[]> {
    return editablePageFrame(page)
        .locator(kPagePicture)
        .evaluateAll((canvases) =>
            canvases.map((c) => c.getAttribute("aria-hidden") === "true"),
        );
}

/**
 * The same as getPicturesMarkedNotDescribed, for one page of the book saved in `bookFolder`, as
 * the saved file has it. `page` is used only as a DOM parser. Throws if the file has no page with
 * that id.
 */
export async function readSavedPicturesMarkedNotDescribed(
    page: Page,
    bookFolder: string,
    pageId: string,
): Promise<boolean[]> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return page.evaluate(
        ({ source, id, selector }) => {
            const document = new DOMParser().parseFromString(
                source,
                "text/html",
            );
            const pageDiv = document.getElementById(id);
            if (!pageDiv)
                throw new Error(`The saved book has no page with id ${id}.`);
            return Array.from(pageDiv.parentElement!.querySelectorAll(selector))
                .filter((c) => pageDiv.contains(c))
                .map((c) => c.getAttribute("aria-hidden") === "true");
        },
        { source: html, id: pageId, selector: kPagePicture },
    );
}

/**
 * Tick or untick the tool's "This image should not be described." box by clicking it, the way a
 * person does, and wait until the box shows the new state. Throws if the box already shows it,
 * since then a click would do the opposite of what the caller asked.
 */
export async function setImageNotDescribed(
    page: Page,
    notDescribed: boolean,
): Promise<void> {
    const box = imageNotDescribedBox(page);
    if ((await box.isChecked()) === notDescribed)
        throw new Error(
            `The "should not be described" box is already ${notDescribed ? "ticked" : "unticked"}.`,
        );
    await box.click();
    await expect(box).toBeChecked({ checked: notDescribed });
}

/**
 * Show a content page by way of the front cover, with the Image Description tool open, and wait
 * until the tool has taken up each page in turn. The tool greys out its "should not be described"
 * box on front matter, so the box's group losing that grey shows that the box now speaks for the
 * content page, not for whichever page was shown before it.
 */
export async function showContentPageInImageDescriptionTool(
    page: Page,
    coverId: string,
    pageId: string,
): Promise<void> {
    const boxGroup = toolboxFrame(page).locator(
        ".imgDescLabelBlock:has(.imageDescriptionCheck)",
    );
    await goToPage(page, coverId);
    await openImageDescriptionTool(page);
    await expect(boxGroup, "the box is greyed out on the cover").toHaveClass(
        /\bdisabled\b/,
    );
    await goToPage(page, pageId);
    await expect(
        boxGroup,
        "the box is no longer greyed out on a content page",
    ).not.toHaveClass(/\bdisabled\b/);
}

/**
 * Wait until the page being edited, which must have one picture, and the Image Description tool
 * both say whether that picture should not be described: the picture's own mark, and the box.
 * `where` names the moment in the failure message.
 */
export async function expectOnePictureNotDescribed(
    page: Page,
    where: string,
    notDescribed: boolean,
): Promise<void> {
    await openImageDescriptionTool(page);
    await expect
        .poll(
            async () => ({
                pictures: await getPicturesMarkedNotDescribed(page),
                box: await imageNotDescribedBox(page).isChecked(),
            }),
            {
                message: `${where}: the picture's mark and the "should not be described" box`,
                timeout: 30000,
            },
        )
        .toEqual({ pictures: [notDescribed], box: notDescribed });
}
