// A sign language video on a canvas page: the Canvas tool's video item makes a video element, and
// the toolbar right below that element offers "Choose Video from your Computer..." and "Record
// yourself...", the second of which opens the Sign Language tool on that element (BL-9976). Notion
// test case "Sign Language Video on Canvas" (Test Case ID 336).
//
// Choosing a file goes through Bloom's own file chooser with only the native picker answered by
// e2e/nextFileToChoose (see videos.ts); recording uses the fake camera.
//
// The tests are serial because each one starts from the page the one before it left behind.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import { apiPost } from "../helpers/api";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    activeCanvasElement,
    canvasElement,
    dragCanvasElementBy,
    dragCanvasElementCorner,
    dragPaletteItemOntoCanvas,
    getCanvasElementCount,
    getCanvasElementPlacement,
    selectCanvasElement,
    waitForCanvasElementToolbar,
} from "../helpers/canvasElements";
import { editablePageFrame } from "../helpers/bookMaking";
import { realClick } from "../helpers/realClick";
import { saveScreenshotIfAsked } from "../helpers/screenshot";
import { expectExpandedTool } from "../helpers/toolbox";
import {
    getVideoFileName,
    getVideoSource,
    recordWithOpenSignLanguageTool,
    SHORT_VIDEO,
} from "../helpers/videos";

test.use({
    collectionSpec: { name: "canvas-sign-language-video", languages: ["en"] },
    // One test records a video with the Sign Language tool.
    fakeCamera: true,
});

test.describe.configure({ mode: "serial" });

// Set by the first test.
let bookFolder: string;
let canvasPage: IBookPage;
let otherPage: IBookPage;
let video: number;

/** The video box inside the video canvas element. */
const videoBox = (page: import("@playwright/test").Page) =>
    canvasElement(page, video).locator(".bloom-videoContainer");

/** One of the buttons on the toolbar below the selected canvas element, by its command. */
const toolbarButton = (
    page: import("@playwright/test").Page,
    command: string,
) =>
    editablePageFrame(page).locator(
        `#canvas-element-context-controls [data-testid="toolbar-${command}"]`,
    );

test.describe("Sign Language video on a canvas page [Test Case ID 336]", () => {
    test("builds a book with a canvas page and a page to leave it for", async ({
        page,
    }) => {
        test.setTimeout(300000);
        bookFolder = await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Just Text");
        await addPage(page, "Canvas");
        [otherPage, canvasPage] = await getContentPages(page);
        expect(
            canvasPage,
            "The book has no second content page.",
        ).toBeDefined();
        await goToPage(page, canvasPage.id);
        // A new canvas page holds one canvas element already: its background picture.
        expect(await getCanvasElementCount(page)).toBe(1);
    });

    test("dragging the video item onto the canvas makes an empty video element whose toolbar offers Choose and Record, and leaves the Canvas tool open", async ({
        page,
    }) => {
        video = await dragPaletteItemOntoCanvas(page, "video");
        await expect(
            videoBox(page),
            "The new canvas element holds no video box.",
        ).toHaveCount(1);
        await expect(
            videoBox(page),
            "A video element fresh from the palette should have no video yet.",
        ).toHaveClass(/bloom-noVideoSelected/);

        await selectCanvasElement(page, video);
        await waitForCanvasElementToolbar(page);
        await expect(toolbarButton(page, "chooseVideo")).toBeVisible();
        await expect(toolbarButton(page, "recordVideo")).toBeVisible();
        // Bloom no longer switches to the Sign Language tool when a video element is added or
        // selected; the toolbar's Record button is the way there.
        await expectExpandedTool(
            page,
            "canvas",
            "Adding or selecting a video element should leave the Canvas tool open.",
        );
        await saveScreenshotIfAsked(
            [activeCanvasElement(page), toolbarButton(page, "recordVideo")],
            "01-empty-video-element-and-toolbar",
        );
    });

    test("Choose Video from your Computer on the toolbar puts the chosen video in the element", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await selectCanvasElement(page, video);
        await apiPost(
            page,
            "e2e/nextFileToChoose",
            Path.resolve(SHORT_VIDEO),
            "text/plain",
        );

        // THE ACTION UNDER TEST: the toolbar's Choose button.
        await realClick(toolbarButton(page, "chooseVideo"));

        await expect
            .poll(async () => getVideoSource(videoBox(page)), {
                timeout: 120000,
                message:
                    "Choosing a video from the toolbar never put a video in the element. Bloom " +
                    "re-encodes the file with ffmpeg, so this is slow, but not this slow.",
            })
            .not.toBe("");
        await expect(videoBox(page)).not.toHaveClass(/bloom-noVideoSelected/, {
            timeout: 30000,
        });
        const fileName = await getVideoFileName(videoBox(page));
        expect(
            fs.existsSync(Path.join(bookFolder, "video", fileName)),
            `The element shows ${fileName}, but the book's video folder has no such file.`,
        ).toBe(true);
    });

    test("Record yourself on the toolbar opens the Sign Language tool on the element, and a recording replaces its video", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await selectCanvasElement(page, video);
        const chosen = await getVideoFileName(videoBox(page));
        expect(chosen, "test setup: the element should hold a video").not.toBe(
            "",
        );

        // THE ACTION UNDER TEST: the toolbar's Record button.
        await realClick(toolbarButton(page, "recordVideo"));
        await expectExpandedTool(
            page,
            "signLanguage",
            "The toolbar's Record yourself button did not open the Sign Language tool.",
        );

        await recordWithOpenSignLanguageTool(page, videoBox(page));
        // Compare file names, not sources: a source can differ only in the timestamp Bloom adds to
        // make the browser load a video afresh. Bloom gives every recording a new file name.
        const fileName = await getVideoFileName(videoBox(page));
        expect(
            fileName,
            "The recording did not replace the chosen video.",
        ).not.toBe(chosen);
        expect(
            fs.existsSync(Path.join(bookFolder, "video", fileName)),
            `The element shows ${fileName}, but the book's video folder has no such file.`,
        ).toBe(true);
    });

    test("moving and resizing the video element keeps its video, and both stay after leaving the page", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await selectCanvasElement(page, video);
        const source = await getVideoSource(videoBox(page));
        const fileName = await getVideoFileName(videoBox(page));
        expect(
            fileName,
            "test setup: the element should hold a video",
        ).not.toBe("");
        const placementBefore = await getCanvasElementPlacement(
            canvasElement(page, video),
        );

        // THE ACTIONS UNDER TEST: drag the element, then drag a corner to make it bigger.
        await dragCanvasElementBy(page, -60, -40);
        await dragCanvasElementCorner(page, "se", 50, 50);

        const placement = await getCanvasElementPlacement(
            canvasElement(page, video),
        );
        expect(
            placement.left,
            "Dragging the element left did not move it left.",
        ).toBeLessThan(placementBefore.left);
        expect(
            placement.top,
            "Dragging the element up did not move it up.",
        ).toBeLessThan(placementBefore.top);
        expect(
            placement.width,
            "Dragging the bottom-right corner out did not widen the element.",
        ).toBeGreaterThan(placementBefore.width);
        expect(
            placement.height,
            "Dragging the bottom-right corner out did not make the element taller.",
        ).toBeGreaterThan(placementBefore.height);
        expect(
            await getVideoSource(videoBox(page)),
            "Moving or resizing the element changed its video.",
        ).toBe(source);

        // Leaving the page saves it; coming back reads it from what was saved.
        await goToPage(page, otherPage.id);
        await goToPage(page, canvasPage.id);
        expect(await getCanvasElementCount(page)).toBe(2);
        await expect(videoBox(page)).not.toHaveClass(/bloom-noVideoSelected/);
        // Compare file names: the src of a video just recorded carries a timestamp, to make the
        // browser load it afresh, that Bloom does not save.
        expect(
            await getVideoFileName(videoBox(page)),
            "The element lost its video when the page was saved and opened again.",
        ).toBe(fileName);
        expect(
            fs.existsSync(Path.join(bookFolder, "video", fileName)),
            `After the page was saved and opened again, the element shows ${fileName}, but the book's video folder has no such file.`,
        ).toBe(true);
        expect(
            await getCanvasElementPlacement(canvasElement(page, video)),
            "The element's place or size changed when the page was saved and opened again.",
        ).toEqual(placement);
    });
});
