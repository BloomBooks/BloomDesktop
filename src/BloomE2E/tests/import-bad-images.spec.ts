// Bad pictures: what Bloom says when what a person pastes or imports is not a picture it can use
// (Test Case ID 279).
//
// Pasting: when the clipboard holds something that was clearly meant to be a picture (a damaged
// picture, a copied picture file that is damaged or not a picture at all, text that is a path or
// web address of a picture) Bloom says it could not make a picture of it; when it holds nothing
// like a picture (ordinary text, or nothing) Bloom says there is no picture to paste. Either way
// the picture on the page stays as it was. The test fills Bloom's own clipboard with each of
// these (helpers/clipboard.ts) and chooses Paste image on the cover picture, as a person does.
//
// Importing: the manual case tries the files in a folder of bad pictures on the team's drive. The
// test makes the same kinds of file at run time: a PNG cut off halfway, a text file named .png,
// and a JPEG named .png. It opens each in the Image Chooser and clicks "Use this image". Bloom
// should keep the dialog open, say why, and leave the picture as it was, and should simply take
// the JPEG, whose only fault is its name. What the message says is not checked: Bloom tells the
// user to check their internet connection, which is wrong for a file on this computer (BL-17195).

import * as fs from "node:fs";
import type { Frame, Locator, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { getUiString } from "../helpers/api";
import { editablePageFrame, makeBookFromTemplate } from "../helpers/bookMaking";
import {
    clickCanvasElementMenuItem,
    selectCanvasElementAt,
} from "../helpers/canvasElements";
import { copyToClipboard, type IClipboardContents } from "../helpers/clipboard";
import {
    cancelImageChooser,
    pickFileInImageChooser,
} from "../helpers/imageChooser";
import { getImagePlacement, makeJpegFile } from "../helpers/images";
import { watchForScriptErrors } from "../helpers/scriptErrors";

test.use({
    collectionSpec: { name: "import-bad-images", languages: ["en"] },
});

const PASTE_IMAGE_COMMAND = "EditTab.Image.PasteImage";
const CHOOSE_IMAGE_COMMAND = "EditTab.Image.ChooseImage";
const PLACEHOLDER = "placeHolder.png";

/** The bad picture files the test offers Bloom, made at run time. */
interface IBadFiles {
    /** A PNG cut off halfway through. */
    truncatedPng: string;
    /** A text file with a picture's name. */
    textNamedPng: string;
    /** A good JPEG named as if it were a PNG. */
    jpegNamedPng: string;
    /** A good JPEG, for the paste that should work. */
    goodJpeg: string;
}

/**
 * Write a small PNG, drawn by the browser in `page` because Node has no PNG encoder, and return
 * its bytes.
 */
async function makePngBytes(page: Page): Promise<Buffer> {
    const base64 = await page.evaluate(async () => {
        const canvas = new OffscreenCanvas(400, 300);
        const context = canvas.getContext("2d")!;
        context.fillStyle = "#2a9d8f";
        context.fillRect(0, 0, 400, 300);
        context.fillStyle = "#9d2a5c";
        context.fillRect(50, 50, 200, 100);
        const blob = await canvas.convertToBlob({ type: "image/png" });
        const bytes = new Uint8Array(await blob.arrayBuffer());
        let binary = "";
        for (let i = 0; i < bytes.length; i += 0x8000)
            binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
        return btoa(binary);
    });
    return Buffer.from(base64, "base64");
}

async function makeBadFiles(
    page: Page,
    outputPath: (name: string) => string,
): Promise<IBadFiles> {
    const files: IBadFiles = {
        truncatedPng: outputPath("truncated.png"),
        textNamedPng: outputPath("not-a-picture.png"),
        jpegNamedPng: outputPath("jpeg-named.png"),
        goodJpeg: outputPath("good.jpg"),
    };
    const png = await makePngBytes(page);
    expect(
        png.subarray(0, 8).toString("hex"),
        "test setup: the browser did not make a PNG",
    ).toBe("89504e470d0a1a0a");
    fs.writeFileSync(
        files.truncatedPng,
        png.subarray(0, Math.floor(png.length / 2)),
    );
    fs.writeFileSync(files.textNamedPng, "This is not a picture.\r\n");
    await makeJpegFile(page, files.goodJpeg, 400, 300);
    fs.copyFileSync(files.goodJpeg, files.jpegNamedPng);
    return files;
}

/** The cover picture's box, which the test pastes into and opens the Image Chooser on. */
function coverPicture(page: Page): Locator {
    return editablePageFrame(page)
        .locator(".bloom-canvas .bloom-canvas-element.bloom-backgroundImage")
        .first();
}

/**
 * Wait for Bloom's message box showing `message` in `within` (the shell or a frame), then click
 * its OK button and wait until it has gone.
 */
async function expectMessageAndClose(
    page: Page,
    within: Page | Frame,
    message: string,
    what: string,
): Promise<void> {
    const box = within.getByRole("dialog").filter({ hasText: message });
    await expect(
        box,
        `Bloom did not say "${message}" after ${what}.`,
    ).toBeVisible({ timeout: 30000 });
    await box
        .getByRole("button", {
            name: await getUiString(page, "Common.OK", "OK"),
        })
        .click();
    await expect(
        box,
        `Bloom's message after ${what} did not close with OK.`,
    ).toHaveCount(0, { timeout: 15000 });
}

/** The cover picture's file is still the placeholder a new book starts with. */
async function expectCoverUnchanged(page: Page, what: string): Promise<void> {
    expect(
        (await getImagePlacement(page)).fileName,
        `The cover picture changed after ${what}.`,
    ).toBe(PLACEHOLDER);
}

/** Put `contents` on Bloom's clipboard and choose Paste image on the cover picture. */
async function pasteOnCover(
    page: Page,
    contents: IClipboardContents,
): Promise<void> {
    await copyToClipboard(page, contents);
    await selectCanvasElementAt(coverPicture(page), "the cover picture");
    await clickCanvasElementMenuItem(page, PASTE_IMAGE_COMMAND);
}

test("pasting what is not a usable picture says why and leaves the picture alone [Test Case ID 279]", async ({
    page,
}, testInfo) => {
    test.setTimeout(240000);
    const files = await makeBadFiles(page, (name) => testInfo.outputPath(name));
    await makeBookFromTemplate(page, "Basic Book");
    await expectCoverUnchanged(page, "making the book (test setup)");
    const errors = watchForScriptErrors(page);

    const couldNotUnderstand = await getUiString(
        page,
        "EditTab.NoValidImageFoundOnClipboard",
        "Bloom failed to interpret the clipboard contents as an image. Possibly it was a damaged file, or too large. Try copying something else.",
    );
    const noPicture = await getUiString(
        page,
        "EditTab.NoImageFoundOnClipboard",
        "Bloom did not find an image on your clipboard. Copy one first, then paste again.",
    );

    const meantAsPictures: [string, IClipboardContents][] = [
        ["a damaged picture", { picture: files.truncatedPng }],
        [
            "a copied picture file that is damaged",
            { files: [files.truncatedPng] },
        ],
        [
            "a copied .png file that is not a picture",
            { files: [files.textNamedPng] },
        ],
        [
            "the path of a picture file that does not exist",
            { text: "C:\\No Such Folder\\picture.png" },
        ],
        [
            "the web address of a picture",
            { text: "https://example.com/pictures/cat.jpg" },
        ],
    ];
    for (const [what, contents] of meantAsPictures) {
        const pasting = `pasting ${what}`;
        await pasteOnCover(page, contents);
        await expectMessageAndClose(
            page,
            editablePageFrame(page),
            couldNotUnderstand,
            pasting,
        );
        await expectCoverUnchanged(page, pasting);
    }

    const notPictures: [string, IClipboardContents][] = [
        ["ordinary text", { text: "Hello there" }],
        ["an empty clipboard", {}],
    ];
    for (const [what, contents] of notPictures) {
        const pasting = `pasting ${what}`;
        await pasteOnCover(page, contents);
        await expectMessageAndClose(
            page,
            editablePageFrame(page),
            noPicture,
            pasting,
        );
        await expectCoverUnchanged(page, pasting);
    }

    // The same route does paste a good picture, so the messages above are about the clipboard.
    await pasteOnCover(page, { picture: files.goodJpeg });
    await expect
        .poll(
            async () => {
                const placement = await getImagePlacement(page);
                return placement.fileName !== PLACEHOLDER && placement.loaded;
            },
            {
                timeout: 60000,
                message: "Pasting a good picture never put it on the cover.",
            },
        )
        .toBe(true);

    errors.stop();
    expect(errors.errors(), "Bloom's UI threw script errors.").toEqual([]);
});

test("importing a bad picture file keeps the Image Chooser open and leaves the picture alone [Test Case ID 279]", async ({
    page,
}, testInfo) => {
    test.setTimeout(240000);
    const files = await makeBadFiles(page, (name) => testInfo.outputPath(name));
    await makeBookFromTemplate(page, "Basic Book");
    await expectCoverUnchanged(page, "making the book (test setup)");
    const errors = watchForScriptErrors(page);

    const unreadable: [string, string][] = [
        ["a PNG cut off halfway", files.truncatedPng],
        ["a text file named .png", files.textNamedPng],
    ];
    for (const [what, file] of unreadable) {
        const importing = `importing ${what}`;
        await selectCanvasElementAt(coverPicture(page), "the cover picture");
        await clickCanvasElementMenuItem(page, CHOOSE_IMAGE_COMMAND);
        const { chooser, use } = await pickFileInImageChooser(page, file);
        await use.click();
        // Bloom's message box, which is not the Image Chooser. Its words are not checked
        // (BL-17195).
        const message = page.getByRole("dialog").filter({
            hasNot: page.getByRole("button", { name: "Open File..." }),
        });
        await expect(
            message,
            `Bloom said nothing after ${importing}.`,
        ).toBeVisible({ timeout: 30000 });
        await message
            .getByRole("button", {
                name: await getUiString(page, "Common.OK", "OK"),
            })
            .click();
        await expect(message).toHaveCount(0, { timeout: 15000 });
        await expect(
            chooser,
            `The Image Chooser closed after ${importing}, as if Bloom had used it.`,
        ).toBeVisible();
        await cancelImageChooser(chooser);
        await expectCoverUnchanged(page, importing);
    }

    // A good picture whose only fault is its name is simply used.
    await selectCanvasElementAt(coverPicture(page), "the cover picture");
    await clickCanvasElementMenuItem(page, CHOOSE_IMAGE_COMMAND);
    const { chooser, use } = await pickFileInImageChooser(
        page,
        files.jpegNamedPng,
    );
    await use.click();
    await expect(
        chooser,
        "The Image Chooser stayed open after choosing a JPEG named .png.",
    ).toHaveCount(0, { timeout: 30000 });
    await expect
        .poll(
            async () => {
                const placement = await getImagePlacement(page);
                return placement.fileName !== PLACEHOLDER && placement.loaded;
            },
            {
                timeout: 60000,
                message: "The cover never showed the JPEG named .png.",
            },
        )
        .toBe(true);

    errors.stop();
    expect(errors.errors(), "Bloom's UI threw script errors.").toEqual([]);
});
