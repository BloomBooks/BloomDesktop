// Put a video into a video box on the page being edited.
//
// The production route is: click the video box, which switches the toolbox to the Sign Language
// tool; open that tool's Advanced section; press Import; choose a file in a native file picker;
// Bloom then copies and re-encodes the file into the book's video folder behind a progress dialog,
// and puts the result in the box.
//
// A test can drive all of that except the picker, which no automation can (AUTOMATION-DEBT.md,
// "Native OS dialogs hang automation"). So instead of skipping the route, the picker alone is
// removed: e2e/nextFileToChoose arms Bloom with the path the picker would have returned, and the
// next file chooser Bloom opens (here, the Sign Language tool's) answers with it rather than
// putting up a dialog. Everything after that -- the re-encode, the progress dialog, the copy into
// the book, the update of the box, the save -- runs exactly as it does for a person. Only Bloom
// under --e2e will accept the arming call; see E2eTestingApi.cs and BloomOpenFileDialog.cs. The
// same hook answers every other file or folder chooser in Bloom, so a helper for importing a
// spreadsheet or choosing an image file arms it the same way.

import { expect, type Locator, type Page } from "@playwright/test";
import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { apiPost } from "./api";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    goToPage,
    type IBookPage,
    waitForEditablePage,
    waitForEditTabSettled,
} from "./bookMaking";
import { clickCanvasElementMenuItem } from "./canvasElements";
import { markEditablePage, waitForEditablePageReload } from "./pageThumbnails";
import { realClick } from "./realClick";
import { expectExpandedTool, toolboxFrame } from "./toolbox";
import { confirmRecycleBox } from "./winformsConfirmRecycle";

/** A one-second, 160x120 video that ships with the suite, for filling a video box. */
export const SHORT_VIDEO = Path.join(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "videos",
    "short.mp4",
);

/** Every video box on the page being edited. */
export function videoBoxes(page: Page): Locator {
    return editablePageFrame(page).locator(".bloom-videoContainer");
}

/**
 * Put the video file at `filePath` into `videoBox`, through the Sign Language tool, and wait until
 * the box shows it.
 *
 * `videoBox` is the `.bloom-videoContainer` to fill; see videoBoxes for every one on the page.
 * Bloom re-encodes the file, so this takes a few seconds even for a one-second video, and the file
 * that ends up in the book is not byte-for-byte the one passed in.
 */
export async function chooseVideoFile(
    page: Page,
    videoBox: Locator,
    filePath: string,
): Promise<string> {
    if (!fs.existsSync(filePath))
        throw new Error(`There is no video file at ${filePath}.`);
    await apiPost(
        page,
        "e2e/nextFileToChoose",
        Path.resolve(filePath),
        "text/plain",
    );

    // Clicking the box is what takes Bloom to the Sign Language tool, and it is also what tells the
    // tool which box to import into, so it cannot be skipped by opening the tool directly.
    await realClick(videoBox);
    await openSignLanguageAdvancedSection(page);
    await toolboxFrame(page).locator("#videoImport").click();
    return waitForVideoInBox(videoBox, filePath);
}

/**
 * Put the video file at `filePath` into the video box of the selected canvas element, with the
 * "Choose Video from your Computer..." command of the element's "..." menu, and wait until the box
 * shows it. This is how a person fills a video on a Canvas page, where clicking the box selects
 * the element rather than opening the Sign Language tool.
 *
 * Once the box shows the video, Bloom saves the page and rebuilds it (doVideoCommand in
 * bloomVideo.ts calls saveChangesAndRethinkPage), which replaces the page's document. This waits
 * for that rebuild to finish, because anything a test holds from the old document (an element
 * handle, a count taken before a drag) stops working when it goes away.
 */
export async function chooseVideoFileFromCanvasMenu(
    page: Page,
    videoBox: Locator,
    filePath: string,
): Promise<string> {
    if (!fs.existsSync(filePath))
        throw new Error(`There is no video file at ${filePath}.`);
    const pageId = await editablePageFrame(page)
        .locator(".bloom-page")
        .first()
        .getAttribute("id");
    if (!pageId)
        throw new Error(
            "The page in the Edit tab has no id, so its rebuild after the import cannot be awaited.",
        );
    await apiPost(
        page,
        "e2e/nextFileToChoose",
        Path.resolve(filePath),
        "text/plain",
    );
    await markEditablePage(page);
    await clickCanvasElementMenuItem(
        page,
        "EditTab.Toolbox.ComicTool.Options.ChooseVideo",
    );
    const src = await waitForVideoInBox(videoBox, filePath);
    await waitForEditablePageReload(page, pageId);
    await waitForEditTabSettled(page);
    await waitForEditablePage(page);
    return src;
}

/**
 * Wait until Bloom has put the imported `filePath` into `videoBox`, and return the video's src.
 */
async function waitForVideoInBox(
    videoBox: Locator,
    filePath: string,
): Promise<string> {
    const source = videoBox.locator("video source");
    await expect
        .poll(
            async () =>
                (await source.count()) > 0
                    ? ((await source.first().getAttribute("src")) ?? "")
                    : "",
            {
                timeout: 120000,
                message:
                    `Importing ${Path.basename(filePath)} never put a video in the box. Bloom ` +
                    `re-encodes the file with ffmpeg, so this is slow, but not this slow.`,
            },
        )
        .not.toBe("");
    await expect(
        videoBox,
        "The video box still says it has no video selected.",
    ).not.toHaveClass(/bloom-noVideoSelected/, { timeout: 30000 });
    return (await source.first().getAttribute("src")) ?? "";
}

/**
 * Delete the video in `videoBox` with the Delete command of the Sign Language tool, the way a
 * person does: click the box, which opens the tool on that video, open the tool's Advanced
 * section, press Delete, and answer Delete in the box where Bloom asks whether to move the video to
 * the Recycle Bin. Waits until the video box says it has no video.
 *
 * That box is a native WinForms window, which helpers/winformsConfirmRecycle.ts answers over UI
 * Automation; `bloomPid` is the Bloom that shows it (bloomApp.bloomPid).
 */
export async function deleteVideo(
    page: Page,
    bloomPid: number,
    videoBox: Locator,
): Promise<void> {
    await expect(
        videoBox,
        "There is no video in the box to delete.",
    ).not.toHaveClass(/bloom-noVideoSelected/);
    await realClick(videoBox);
    await openSignLanguageAdvancedSection(page);
    const toolbox = toolboxFrame(page);
    // The tool turns Delete on only once it has read the selected video and is idle.
    await expect(
        toolbox.locator("#deleteRecordingWrapper"),
        "The Sign Language tool never turned on its Delete command for the selected video.",
    ).not.toHaveClass(/\bdisabled\b/, { timeout: 30000 });
    await toolbox.locator("#videoDelete").click();
    await confirmRecycleBox(bloomPid, 30000);
    await expect(
        videoBox,
        "Pressing Delete never took the video out of the box.",
    ).toHaveClass(/bloom-noVideoSelected/, { timeout: 30000 });
    await expect(videoBox.locator("video")).toHaveCount(0);
}

/**
 * Press the Sign Language tool's "Show in Folder" command for the video in `videoBox`, the way a
 * person does: click the box, which opens the tool on that video, open the tool's Advanced section,
 * and press Show in Folder. Bloom then opens the book's video folder in File Explorer with the
 * video selected; helpers/fileExplorer.ts reads and closes that window.
 */
export async function showVideoInFolder(
    page: Page,
    videoBox: Locator,
): Promise<void> {
    await expect(
        videoBox,
        "There is no video in the box to show.",
    ).not.toHaveClass(/bloom-noVideoSelected/);
    await realClick(videoBox);
    await openSignLanguageAdvancedSection(page);
    const toolbox = toolboxFrame(page);
    // The tool turns Show in Folder on only once it has read the selected video and is idle.
    await expect(
        toolbox.locator("#showInFolderWrapper"),
        "The Sign Language tool never turned on its Show in Folder command for the selected video.",
    ).not.toHaveClass(/\bdisabled\b/, { timeout: 30000 });
    await toolbox.locator("#showInFolder").click();
}

/**
 * Record a video into `videoBox` with the Sign Language tool's record button, the way a person
 * does: click the box (which opens the tool and turns the camera on), press record, wait out the
 * three-second countdown and about a second of recording, press stop, and wait until the box
 * shows the recording. Returns its source.
 *
 * Needs a Bloom launched with a fake camera, test.use({ fakeCamera: true }): otherwise Bloom
 * refuses the camera under --e2e and the record button never comes on.
 */
export async function recordVideo(
    page: Page,
    videoBox: Locator,
): Promise<string> {
    await realClick(videoBox);
    return recordWithOpenSignLanguageTool(page, videoBox);
}

/**
 * Record a video into `videoBox` with the Sign Language tool that is already open on it: press
 * record, wait out the countdown and about a second of recording, press stop, and wait until the
 * box shows the recording. Returns its source. recordVideo opens the tool by clicking the box
 * first; this is for a test that opened it another way, such as a canvas video's "Record
 * yourself..." button.
 *
 * Needs a Bloom launched with a fake camera, as recordVideo does.
 */
export async function recordWithOpenSignLanguageTool(
    page: Page,
    videoBox: Locator,
): Promise<string> {
    const before = await getVideoSource(videoBox);
    const toolbox = toolboxFrame(page);
    const recordButton = toolbox.locator("#videoToggleRecording");
    await expect(
        recordButton,
        "The Sign Language tool's record button never came on: the camera did not start. Was Bloom " +
            "launched with test.use({ fakeCamera: true })?",
    ).toHaveClass(/\benabled\b/, { timeout: 30000 });
    await recordButton.click();
    await expect(
        recordButton,
        "Pressing record never started recording.",
    ).toHaveClass(/\brecordingNow\b/, { timeout: 15000 });
    // A second of recording: the tool's own clock says when.
    await expect(toolbox.locator("#timeWrapper")).toHaveText(
        /\d:(0[1-9]|[1-5]\d)\s*$/,
        { timeout: 15000 },
    );
    await recordButton.click();
    const source = videoBox.locator("video source");
    await expect
        .poll(
            async () =>
                (await source.count()) > 0
                    ? ((await source.first().getAttribute("src")) ?? "")
                    : "",
            {
                timeout: 120000,
                message: "The recording never went into the video box.",
            },
        )
        .not.toBe(before);
    await expect(videoBox).not.toHaveClass(/bloom-noVideoSelected/, {
        timeout: 30000,
    });
    return (await source.first().getAttribute("src")) ?? "";
}

/**
 * Trim the start of the video in `videoBox` with the Sign Language tool's Trim slider: click the
 * box, which opens the tool on that video, then move the slider's start handle `tenthsOfASecond`
 * steps to the right with the arrow key (one step is a tenth of a second). Waits until the box's
 * video starts that much later than it did, and returns its trim.
 */
export async function trimVideoStart(
    page: Page,
    videoBox: Locator,
    tenthsOfASecond: number,
): Promise<IVideoTrim> {
    const startBefore = (await getVideoTrim(videoBox))?.start ?? 0;
    await realClick(videoBox);
    const startHandle = toolboxFrame(page).locator(
        ".videoTrimSlider .rc-slider-handle-1",
    );
    await expect(
        startHandle,
        "The Sign Language tool never showed its Trim slider. It shows one only once it has read the selected video's length.",
    ).toBeVisible({ timeout: 30000 });
    for (let i = 0; i < tenthsOfASecond; i++)
        await startHandle.press("ArrowRight");
    const expectedStart = startBefore + tenthsOfASecond / 10;
    await expect
        .poll(async () => (await getVideoTrim(videoBox))?.start, {
            timeout: 15000,
            message: `Moving the Trim slider's start handle ${tenthsOfASecond} steps did not trim the video's start to ${expectedStart} seconds.`,
        })
        .toBeCloseTo(expectedStart, 5);
    return (await getVideoTrim(videoBox))!;
}

/** Where a trimmed video starts and ends, in seconds from the start of the whole video. */
export interface IVideoTrim {
    start: number;
    end: number;
}

/**
 * The trim of the video in a box, or undefined when the video plays whole. Bloom records a trim as
 * a `#t=start,end` fragment on the video's src.
 */
export async function getVideoTrim(
    videoBox: Locator,
): Promise<IVideoTrim | undefined> {
    const match = /#t=([\d.]+),([\d.]+)/.exec(await getVideoSource(videoBox));
    if (!match) return undefined;
    return { start: Number(match[1]), end: Number(match[2]) };
}

/**
 * The file name of the video in a box, or "" when the box has no video. Leaves out the trim, and
 * the timestamp query Bloom puts on a video just imported so that the browser reloads it.
 */
export async function getVideoFileName(videoBox: Locator): Promise<string> {
    const source = await getVideoSource(videoBox);
    return decodeURIComponent(source.split(/[?#]/)[0].split("/").pop() ?? "");
}

/** The bytes of one of a book's video files, by the name getVideoFileName gives. */
export function readVideoFile(bookFolder: string, fileName: string): Buffer {
    return fs.readFileSync(Path.join(bookFolder, "video", fileName));
}

/**
 * The book-relative source of the video in a box, trim fragment included, or "" when it has none.
 * Bloom records the trim as a `#t=` fragment on the src, which is why this returns the whole thing.
 */
export async function getVideoSource(videoBox: Locator): Promise<string> {
    const source = videoBox.locator("video source");
    if ((await source.count()) === 0) return "";
    return (await source.first().getAttribute("src")) ?? "";
}

/**
 * Click `videoBox` the way a person does, which expands the Sign Language tool on it, and wait
 * until the toolbox says the Sign Language tool is the expanded one.
 */
export async function openSignLanguageToolOnVideoBox(
    page: Page,
    videoBox: Locator,
): Promise<void> {
    await realClick(videoBox);
    await expectExpandedTool(
        page,
        "signLanguage",
        "Clicking the video box did not expand the Sign Language tool.",
    );
}

/**
 * Open the Advanced section of the Sign Language tool, which the toolbox must already be showing
 * (clicking a video box takes it there), and wait until the section is open and still.
 */
async function openSignLanguageAdvancedSection(page: Page): Promise<void> {
    const toolbox = toolboxFrame(page);
    // Import and Delete live in the tool's Advanced section, which starts collapsed to zero
    // height. Its heading label opens it; the triangle beside the label has no size of its own, so
    // a click aimed there has nothing to hit. The section's height is the only honest sign that it is
    // open: Playwright counts the clipped button as visible while the section is still shut.
    // Right after the tool comes up, a click on the heading can be lost (the tool renders again
    // as it settles, and a fresh render starts shut), so open it again until it stays open.
    const advancedHeading = toolbox.locator(".expandable > label");
    const advancedContent = toolbox.locator(".expandable > .contentWrap");
    await advancedHeading.waitFor({ state: "visible", timeout: 30000 });
    const contentHeight = () =>
        advancedContent.evaluate((el) => getComputedStyle(el).height);
    for (let attempt = 1; ; attempt++) {
        if ((await contentHeight()) === "0px") await advancedHeading.click();
        try {
            await expect.poll(contentHeight, { timeout: 3000 }).not.toBe("0px");
            // Let the 0.3s height animation finish, so the click below lands on a still button.
            await expect
                .poll(contentHeight, { timeout: 3000 })
                .toBe(await advancedContent.evaluate((el) => el.style.height));
            break;
        } catch (e) {
            if (attempt >= 5)
                throw new Error(
                    `The Sign Language tool's Advanced section would not stay open after ${attempt} tries.`,
                    { cause: e },
                );
        }
    }
}

/**
 * Add a "Video Over Text" page to the book being edited, put the video file at `filePath` into its
 * video box through the Sign Language tool (see chooseVideoFile), and return the new page. Bloom is
 * left showing that page.
 *
 * This is the start state of a test about a book with a sign language video.
 */
export async function addPageWithVideo(
    page: Page,
    filePath: string,
): Promise<IBookPage> {
    const before = (await getContentPages(page)).map((p) => p.id);
    await addPage(page, "Video Over Text");
    const added = (await getContentPages(page)).filter(
        (p) => !before.includes(p.id),
    );
    if (added.length !== 1)
        throw new Error(
            `Adding a "Video Over Text" page added ${added.length} content pages, not one.`,
        );
    await goToPage(page, added[0].id);
    await chooseVideoFile(page, videoBoxes(page).first(), filePath);
    return added[0];
}
