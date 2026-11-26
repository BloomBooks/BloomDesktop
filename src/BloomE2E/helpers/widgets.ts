// Put an HTML5 widget on a widget page, the way a person does: hover the page's widget box, click
// its Choose Widget button, and pick the widget's .html file (bloomWidgets.ts in the Edit tab, and
// editView/chooseWidget in EditingViewApi.cs). The file picker is a BloomOpenFileDialog, so
// armFileChooser answers it instead of a native dialog no test can dismiss.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, type Page } from "@playwright/test";
import { editablePageFrame, waitForEditTabSettled } from "./bookMaking";
import { armFileChooser } from "./talkingBook";

/**
 * Write a minimal widget, an index.html that shows `text`, into a folder of its own named
 * `widgetName` under `parentFolder`, and return the path of its index.html. Bloom zips the whole
 * folder that holds the chosen .html file into the widget, and names the widget after the folder.
 */
export function writeWidgetFolder(
    parentFolder: string,
    widgetName: string,
    text: string,
): string {
    const folder = Path.join(parentFolder, widgetName);
    fs.mkdirSync(folder, { recursive: true });
    const indexPath = Path.join(folder, "index.html");
    fs.writeFileSync(
        indexPath,
        `<!DOCTYPE html>\n<html><head><meta charset="utf-8"><title>${widgetName}</title></head>` +
            `<body><p id="widget-text">${text}</p></body></html>\n`,
    );
    return indexPath;
}

/**
 * On the page the Edit tab is showing, which has one empty widget box, choose the widget whose
 * .html file is at `htmlPath` with the box's Choose Widget button. Returns once the box holds the
 * widget's frame.
 */
export async function chooseWidgetOnPage(
    page: Page,
    htmlPath: string,
): Promise<void> {
    const frame = editablePageFrame(page);
    const box = frame.locator(".bloom-widgetContainer");
    await expect(box, "The page should have one widget box.").toHaveCount(1);
    await expect(
        box,
        "Sanity check: the widget box should be empty before a widget is chosen.",
    ).toHaveClass(/bloom-noWidgetSelected/);
    // The button is made when the pointer enters the box (a mouseenter listener), and removed when
    // it leaves. Bloom adds that listener only once its answer about the widget feature arrives
    // (SetupWidgetEditing), which leaves no mark in the page, and a pointer already inside the box
    // then (where the previous page's box was) never enters it. So the pointer leaves the page
    // and comes back into the box until the button shows.
    const chooseButton = box.locator(".chooseWidgetButton");
    await expect
        .poll(
            async () => {
                await page.mouse.move(0, 0);
                await box.hover();
                return chooseButton.isVisible();
            },
            {
                timeout: 30000,
                message:
                    "Hovering the widget box never showed its Choose Widget button.",
            },
        )
        .toBe(true);
    // Armed immediately before the click that opens the picker, because it answers one picker
    // only, and a path left armed would answer some later test's picker.
    await armFileChooser(page, htmlPath);
    await chooseButton.click();
    await expect(
        box,
        "The widget box never took the chosen widget.",
    ).not.toHaveClass(/bloom-noWidgetSelected/, { timeout: 30000 });
    await expect(
        box.locator("iframe"),
        "The widget box should show the widget in a frame.",
    ).toHaveAttribute("src", /^activities\//);
    await waitForEditTabSettled(page);
}
