// The commands in the Advanced section of the Sign Language tool: Import puts a video file into the
// selected video box, Show in Folder opens the book's video folder in File Explorer with that video
// selected, and Delete moves the video to the Recycle Bin and leaves the box empty.
//
// Test Case ID 584 ("SL Advanced Import Delete"). The card's note that Import shows on every page,
// whether or not it has a video box, is BL-10459, a bug that the team lives with, so this test does
// not check it.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import { closeExplorerShowing } from "../helpers/fileExplorer";
import {
    chooseVideoFile,
    deleteVideo,
    getVideoFileName,
    SHORT_VIDEO,
    showVideoInFolder,
    videoBoxes,
} from "../helpers/videos";

test.use({
    collectionSpec: {
        name: "sign-language-advanced-import-delete",
        languages: ["en"],
    },
});

/** The files in a book's video folder, or none when it has no such folder. */
function videoFolderFiles(bookFolder: string): string[] {
    const folder = Path.join(bookFolder, "video");
    return fs.existsSync(folder) ? fs.readdirSync(folder) : [];
}

test("the Sign Language tool's Import, Show in Folder and Delete act on the selected video [Test Case ID 584]", async ({
    page,
    bloomApp,
}) => {
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    const before = (await getContentPages(page)).map((p) => p.id);
    await addPage(page, "Video Over Text");
    const added = (await getContentPages(page)).filter(
        (p) => !before.includes(p.id),
    );
    expect(
        added,
        'sanity check: adding a "Video Over Text" page added one page',
    ).toHaveLength(1);
    await goToPage(page, added[0].id);
    const videoBox = videoBoxes(page).first();
    await expect(
        videoBox,
        "sanity check: the new page's video box starts empty",
    ).toHaveClass(/bloom-noVideoSelected/);
    expect(
        videoFolderFiles(bookFolder),
        "sanity check: the new book has no videos",
    ).toEqual([]);

    // Import
    await chooseVideoFile(page, videoBox, SHORT_VIDEO);
    const fileName = await getVideoFileName(videoBox);
    expect(
        videoFolderFiles(bookFolder),
        "Import did not put exactly the box's video into the book's video folder",
    ).toEqual([fileName]);
    const videoPath = Path.join(bookFolder, "video", fileName);
    expect(
        fs.statSync(videoPath).size,
        "Import left an empty video file in the book",
    ).toBeGreaterThan(0);

    // Show in Folder
    await showVideoInFolder(page, videoBox);
    const explorer = await closeExplorerShowing(videoPath, 60000);
    expect(
        explorer.selected.map((path) => path.toLowerCase()),
        `Show in Folder opened ${explorer.folder} in File Explorer but did not select the video there`,
    ).toEqual([videoPath.toLowerCase()]);

    // Delete
    await deleteVideo(page, bloomApp.bloomPid, videoBox);
    expect(
        videoFolderFiles(bookFolder),
        "Delete emptied the video box but left the video in the book's video folder",
    ).toEqual([]);
});
