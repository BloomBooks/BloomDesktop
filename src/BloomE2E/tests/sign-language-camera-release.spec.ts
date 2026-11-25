// Bloom gives the camera back whenever the Sign Language tool is not showing a video box to
// record into: when another tool is expanded, when the toolbox is shut, when the page being edited
// has no video box, and when the Edit tab is left. It takes the camera again when the tool is
// back on a video box, so the monitor shows the camera as soon as a person can record.
//
// Test Case ID 587, "Bloom Relinquishes Camera". The manual case checks the camera's "in use"
// light or the Windows Camera app; here the camera is the one Chromium makes up, and "given back"
// means Bloom stopped every track of every stream it got (helpers/camera.ts).

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    waitForEditablePage,
    type IBookPage,
} from "../helpers/bookMaking";
import { getCameraUse, watchCameraRequests } from "../helpers/camera";
import { realClick } from "../helpers/realClick";
import {
    enableToolForBook,
    expandToolByHeader,
    expectExpandedTool,
    hideToolbox,
    showToolbox,
    toolboxFrame,
} from "../helpers/toolbox";
import { videoBoxes } from "../helpers/videos";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "sign-language-camera", languages: ["en"] },
    fakeCamera: true,
});

let bookFolder: string;
let videoPage: IBookPage;
let textPage: IBookPage;

/** Add a page made from the template page `label`, and return it. */
async function addPageAndFindIt(page: Page, label: string): Promise<IBookPage> {
    const before = new Set((await getContentPages(page)).map((p) => p.id));
    await addPage(page, label);
    const added = (await getContentPages(page)).filter(
        (p) => !before.has(p.id),
    );
    expect(added, `test setup: adding a "${label}" page`).toHaveLength(1);
    return added[0];
}

/** Wait until Bloom holds the camera:the toolbox has a live track from getUserMedia. */
async function expectCameraHeld(page: Page, message: string): Promise<void> {
    await expect
        .poll(async () => (await getCameraUse(page)).liveTracks, {
            timeout: 30000,
            message,
        })
        .toBeGreaterThan(0);
}

/** Wait until Bloom has given the camera back: no track it got from getUserMedia is still live. */
async function expectCameraReleased(
    page: Page,
    message: string,
): Promise<void> {
    const use = await getCameraUse(page);
    expect(
        use.streamsHandedOut,
        "test setup: Bloom never asked for the camera, so its giving it back proves nothing",
    ).toBeGreaterThan(0);
    await expect
        .poll(async () => (await getCameraUse(page)).liveTracks, {
            timeout: 30000,
            message,
        })
        .toBe(0);
}

/**
 * Click the video box on the video page, which expands the Sign Language tool on it, and wait
 * until Bloom holds the camera.
 */
async function openSignLanguageToolOnVideo(page: Page): Promise<void> {
    await goToPage(page, videoPage.id);
    await waitForEditablePage(page);
    await realClick(videoBoxes(page).first());
    await expectExpandedTool(
        page,
        "signLanguage",
        "Clicking the video box did not expand the Sign Language tool.",
    );
    await expectCameraHeld(
        page,
        "The Sign Language tool, showing a video box, never took the camera.",
    );
}

test.describe("TC-587 Bloom relinquishes the camera", () => {
    test.beforeEach(async ({ page }) => {
        test.setTimeout(300000);
        bookFolder = await makeBookFromTemplate(page, "Basic Book");
        textPage = await addPageAndFindIt(page, "Just Text");
        videoPage = await addPageAndFindIt(page, "Video Over Text");
        await enableToolForBook(page, bookFolder, "talkingBook");
        await showToolbox(page);
        await watchCameraRequests(page);
        await openSignLanguageToolOnVideo(page);
    });

    test("when another tool is expanded, and takes it again when the Sign Language tool is", async ({
        page,
    }) => {
        await expandToolByHeader(page, "talkingBook");
        await expectCameraReleased(
            page,
            "Bloom kept the camera after the Talking Book tool was expanded.",
        );
        await expandToolByHeader(page, "signLanguage");
        await expectCameraHeld(
            page,
            "Expanding the Sign Language tool again on the video page did not take the camera.",
        );
    });

    test("when the toolbox is shut, and takes it again when it is opened", async ({
        page,
    }) => {
        await hideToolbox(page);
        await expectCameraReleased(
            page,
            "Bloom kept the camera after the toolbox was shut.",
        );
        await showToolbox(page);
        await expectCameraHeld(
            page,
            "Opening the toolbox again on the Sign Language tool did not take the camera.",
        );
    });

    test("when the page being edited has no video box, and takes it again on the video page", async ({
        page,
    }) => {
        await goToPage(page, textPage.id);
        await waitForEditablePage(page);
        await expectCameraReleased(
            page,
            "Bloom kept the camera on a page with no video box.",
        );
        await goToPage(page, videoPage.id);
        await waitForEditablePage(page);
        await expectCameraHeld(
            page,
            "Going back to the video page with the Sign Language tool expanded did not take the camera.",
        );
    });

    test("when the Edit tab is left", async ({ page }) => {
        // Leaving the Edit tab unloads the toolbox's document, and Chromium stops every track a
        // document holds when it goes. So the camera is given back once that document is gone.
        const toolbox = toolboxFrame(page);
        await switchTab(page, "collection");
        await expect
            .poll(() => toolbox.isDetached(), {
                timeout: 30000,
                message:
                    "The toolbox's document, which holds the camera, stayed loaded after leaving the Edit tab.",
            })
            .toBe(true);
    });
});
