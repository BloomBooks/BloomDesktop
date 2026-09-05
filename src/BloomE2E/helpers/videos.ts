// Put a video into a video box on the page being edited.
//
// The production route is: click the video box, which switches the toolbox to the Sign Language
// tool; open that tool's Advanced section; press Import; choose a file in a native file picker;
// Bloom then copies and re-encodes the file into the book's video folder behind a progress dialog,
// and puts the result in the box.
//
// A test can drive all of that except the picker, which no automation can (AUTOMATION-DEBT.md,
// "Native OS dialogs hang automation"). So instead of skipping the route, the picker alone is
// removed: e2e/nextVideoFileToChoose arms Bloom with the path the picker would have returned, and
// the next signLanguage/chooseVideo answers with it rather than putting up a dialog. Everything
// after that -- the re-encode, the progress dialog, the copy into the book, the update of the box,
// the save -- runs exactly as it does for a person. Only Bloom under --e2e will accept the arming
// call; see E2eTestingApi.cs and SignLanguageApi.cs.

import { expect, type Locator, type Page } from "@playwright/test";
import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { apiPost } from "./api";
import { editablePageFrame } from "./bookMaking";
import { realClick } from "./realClick";
import { toolboxFrame } from "./toolbox";

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
 * `videoBox` is the `.bloom-videoContainer` to fill; for a table cell, that is the container inside
 * the cell. Bloom re-encodes the file, so this takes a few seconds even for a one-second video, and
 * the file that ends up in the book is not byte-for-byte the one passed in.
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
        "e2e/nextVideoFileToChoose",
        Path.resolve(filePath),
        "text/plain",
    );

    // The Sign Language tool turns the camera on as soon as it opens, so that a person can see
    // themselves before recording, and WebView2 answers that with a permission prompt of its own
    // over the panel. Nothing in the page can dismiss it, and it takes the presses aimed at the
    // tool underneath. Granting the permission first means it is never asked for.
    await page
        .context()
        .grantPermissions(["camera"], { origin: new URL(page.url()).origin });

    // Clicking the box is what takes Bloom to the Sign Language tool, and it is also what tells the
    // tool which box to import into, so it cannot be skipped by opening the tool directly.
    await realClick(videoBox);
    const toolbox = toolboxFrame(page);
    // Inside the Sign Language tool's own panel, because other tools have an Advanced section of
    // their own and their panels stay in the document while shut.
    const tool = toolbox.locator(".signLanguageBody");
    await tool.waitFor({ state: "visible", timeout: 30000 });
    // Import lives in the tool's Advanced section, which starts collapsed to no height at all. Its
    // heading is the label: the triangle beside it sits in a wrapper of its own whose only children
    // are absolutely positioned, so that wrapper has no size and cannot be clicked.
    const importWrapper = tool.locator("#importRecordingWrapper");
    const importLabel = importWrapper.locator(".commandLabel");
    // Whether the section is open is a question about the container, not about the button inside
    // it. Closed means the container's height is nothing and its overflow is hidden, which does not
    // change the size or position the button reports: automation reads such a button as visible,
    // clicks where it says it is, and hits the heading that is drawn over it.
    const contentWrap = tool.locator(".expandable .contentWrap");
    const isOpen = async () =>
        (await contentWrap.evaluate(
            (element) => element.getBoundingClientRect().height,
        )) > 1;
    if (!(await isOpen())) {
        await tool.locator(".expandable > label").first().click();
        await expect
            .poll(isOpen, {
                timeout: 30000,
                message:
                    "Clicking the Advanced heading did not open the Sign Language tool's " +
                    "Advanced section, so Import is not reachable.",
            })
            .toBe(true);
    }
    await expect(
        importWrapper,
        "The Sign Language tool is offering Import greyed out, which it does while it is " +
            "recording or playing rather than sitting idle.",
    ).not.toHaveClass(/disabled/, { timeout: 15000 });
    // The words rather than the camera button beside them: both call importRecording, and the
    // button is drawn as a background image inside a wrapper that takes the presses aimed at it.
    await importLabel.click();

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
 * The book-relative source of the video in a box, trim fragment included, or "" when it has none.
 * Bloom records the trim as a `#t=` fragment on the src, which is why this returns the whole thing.
 */
export async function getVideoSource(videoBox: Locator): Promise<string> {
    const source = videoBox.locator("video source");
    if ((await source.count()) === 0) return "";
    return (await source.first().getAttribute("src")) ?? "";
}
