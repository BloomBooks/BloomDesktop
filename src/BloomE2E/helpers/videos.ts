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
    const toolbox = toolboxFrame(page);
    const importButton = toolbox.locator("#videoImport");
    // Import lives in the tool's Advanced section, which starts collapsed to zero height. Its
    // heading label opens it; the triangle beside the label has no size of its own, so a click
    // aimed there has nothing to hit. The section's height is the only honest sign that it is
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
    await importButton.click();

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
