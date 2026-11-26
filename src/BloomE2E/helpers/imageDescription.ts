// The Image Description tool: a toolbox tool that, while it is the expanded one, shows beside each
// picture of the page a text box for describing that picture. The boxes are a translation group of
// their own (kImageDescriptionGroup) inside the picture's canvas, which the tool adds to a picture
// that has none yet.

import { type Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";
import { expandTool } from "./toolbox";

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
