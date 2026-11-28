// Put an HTML5 widget on a widget page, or replace the one it has, the way a person does: hover
// the page's widget box, click its Choose Widget button, and pick the widget's .html file or its
// .wdgt file (bloomWidgets.ts in the Edit tab, and editView/chooseWidget in EditingViewApi.cs). The
// file picker is a BloomOpenFileDialog, so armFileChooser answers it instead of a native dialog no
// test can dismiss.

import * as fs from "node:fs";
import * as Path from "node:path";
import * as zlib from "node:zlib";
import { expect, type FrameLocator, type Page } from "@playwright/test";
import {
    editablePageFrame,
    goToPage,
    waitForEditTabSettled,
} from "./bookMaking";
import { armFileChooser } from "./talkingBook";

const kWidgetTextId = "widget-text";

/**
 * Go to the page with this id in the Edit tab, which has one empty widget box, and put on it a
 * minimal widget showing `text` (writeWidgetFolder, under `parentFolder`), chosen with the box's
 * Choose Widget button (chooseWidgetOnPage).
 */
export async function putWidgetOnPage(
    page: Page,
    pageId: string,
    parentFolder: string,
    widgetName: string,
    text: string,
): Promise<void> {
    await goToPage(page, pageId);
    await chooseWidgetOnPage(
        page,
        writeWidgetFolder(parentFolder, widgetName, text),
    );
}

/** The text that a widget written by writeWidgetFolder shows, read from the widget's document. */
export async function getWidgetText(widget: FrameLocator): Promise<string> {
    return widget.locator(`#${kWidgetTextId}`).innerText({ timeout: 30000 });
}

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
    fs.writeFileSync(indexPath, widgetIndexHtml(widgetName, text));
    return indexPath;
}

/**
 * Write a minimal widget as a .wdgt file, the zip that Bloom's widget format is, named
 * `widgetName`.wdgt under `parentFolder`, whose index.html shows `text`; return its path. Bloom
 * names a widget chosen this way after the file.
 */
export function writeWidgetFile(
    parentFolder: string,
    widgetName: string,
    text: string,
): string {
    fs.mkdirSync(parentFolder, { recursive: true });
    const widgetPath = Path.join(parentFolder, `${widgetName}.wdgt`);
    fs.writeFileSync(
        widgetPath,
        makeZip("index.html", Buffer.from(widgetIndexHtml(widgetName, text))),
    );
    return widgetPath;
}

/** The index.html of a minimal widget that shows `text`. */
function widgetIndexHtml(widgetName: string, text: string): string {
    return (
        `<!DOCTYPE html>\n<html><head><meta charset="utf-8"><title>${widgetName}</title></head>` +
        `<body><p id="${kWidgetTextId}">${text}</p></body></html>\n`
    );
}

/**
 * A zip archive holding one file, `name`, stored without compression: the simplest archive that
 * any zip reader opens, made without a zip library, which this suite does not have.
 */
function makeZip(name: string, content: Buffer): Buffer {
    const fileName = Buffer.from(name, "utf8");
    const crc = zlib.crc32(content);
    const localHeader = Buffer.alloc(30);
    localHeader.writeUInt32LE(0x04034b50, 0); // local file header signature
    localHeader.writeUInt16LE(20, 4); // version needed to extract
    localHeader.writeUInt16LE(0x0800, 6); // flags: the name is UTF-8
    localHeader.writeUInt16LE(0, 8); // method: stored
    localHeader.writeUInt32LE(0, 10); // modification time and date
    localHeader.writeUInt32LE(crc, 14);
    localHeader.writeUInt32LE(content.length, 18); // compressed size
    localHeader.writeUInt32LE(content.length, 22); // uncompressed size
    localHeader.writeUInt16LE(fileName.length, 26);
    localHeader.writeUInt16LE(0, 28); // extra field length
    const centralHeader = Buffer.alloc(46);
    centralHeader.writeUInt32LE(0x02014b50, 0); // central directory header signature
    centralHeader.writeUInt16LE(20, 4); // version made by
    centralHeader.writeUInt16LE(20, 6); // version needed to extract
    centralHeader.writeUInt16LE(0x0800, 8);
    centralHeader.writeUInt16LE(0, 10);
    centralHeader.writeUInt32LE(0, 12);
    centralHeader.writeUInt32LE(crc, 16);
    centralHeader.writeUInt32LE(content.length, 20);
    centralHeader.writeUInt32LE(content.length, 24);
    centralHeader.writeUInt16LE(fileName.length, 28);
    // extra field, comment, disk number, internal and external attributes are all zero
    centralHeader.writeUInt32LE(0, 42); // offset of the local header
    const centralOffset = localHeader.length + fileName.length + content.length;
    const centralSize = centralHeader.length + fileName.length;
    const end = Buffer.alloc(22);
    end.writeUInt32LE(0x06054b50, 0); // end of central directory signature
    end.writeUInt16LE(1, 8); // entries on this disk
    end.writeUInt16LE(1, 10); // entries in all
    end.writeUInt32LE(centralSize, 12);
    end.writeUInt32LE(centralOffset, 16);
    return Buffer.concat([
        localHeader,
        fileName,
        content,
        centralHeader,
        fileName,
        end,
    ]);
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
    await clickChooseWidgetAndPick(page, htmlPath);
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

/**
 * The src of the widget frame in the one widget box of the page the Edit tab is showing, as the
 * page has it (URL-encoded).
 */
export async function getWidgetSrcOnPage(page: Page): Promise<string> {
    const src = await editablePageFrame(page)
        .locator(".bloom-widgetContainer iframe")
        .getAttribute("src", { timeout: 30000 });
    if (src === null) throw new Error("The widget frame has no src.");
    return src;
}

/**
 * On the page the Edit tab is showing, whose one widget box already holds a widget, choose the
 * widget at `widgetPath` (a .wdgt file, or a widget's .html file) in its place with the box's
 * Choose Widget button. Returns the src that the box's frame then has, once it has changed from
 * the one it had.
 */
export async function replaceWidgetOnPage(
    page: Page,
    widgetPath: string,
): Promise<string> {
    const frame = editablePageFrame(page);
    const box = frame.locator(".bloom-widgetContainer");
    await expect(box, "The page should have one widget box.").toHaveCount(1);
    await expect(
        box,
        "Sanity check: the widget box should already hold a widget.",
    ).not.toHaveClass(/bloom-noWidgetSelected/);
    const before = await getWidgetSrcOnPage(page);
    await clickChooseWidgetAndPick(page, widgetPath);
    await expect
        .poll(() => getWidgetSrcOnPage(page), {
            timeout: 30000,
            message: `The widget box never took the new widget; its frame still shows ${before}.`,
        })
        .not.toBe(before);
    await waitForEditTabSettled(page);
    return getWidgetSrcOnPage(page);
}

/**
 * Hover the one widget box of the page the Edit tab is showing until its Choose Widget button
 * shows, and click it, with Bloom's file picker answered with `widgetPath`.
 */
async function clickChooseWidgetAndPick(
    page: Page,
    widgetPath: string,
): Promise<void> {
    const box = editablePageFrame(page).locator(".bloom-widgetContainer");
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
    await armFileChooser(page, widgetPath);
    await chooseButton.click();
}
