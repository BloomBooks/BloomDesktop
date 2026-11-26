// A video can be imported into a book made a moment ago from the Sign Language template, once the
// book has a title. Typing the title renames the book's folder when the cover is left, and Bloom
// once went on looking for the new video in the folder's old name (BL-9133: "Cannot Find File"
// as soon as the video was added), so the test makes the rename happen first and then checks that
// the video lands in the renamed folder, that the page can play it from there, and that the saved
// book points at it.
//
// Test Case ID 586 ("Add Video To Book"), steps 14 to 18 of the Dokimion case TC265 it came from:
// on a new Sign Language book titled "Test SL Video", add a "Just Video" page, press Import in the
// Sign Language tool's Advanced section, choose a video file, and see the video in the video box.
// The native Open dialog is answered by e2e/nextFileToChoose (see helpers/videos.ts).

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import { bookHtmlPath } from "../helpers/bookHtml";
import {
    addPage,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    chooseVideoFile,
    getVideoFileName,
    SHORT_VIDEO,
    videoBoxes,
} from "../helpers/videos";

const kSignLanguageTemplate = "Sign Language";
const kTitle = "Test SL Video";

test.use({
    collectionSpec: {
        name: "sign-language-video-in-new-titled-book",
        languages: ["en"],
        signLanguage: "ase",
    },
});

/** The files in a book's video folder, or none when it has no such folder. */
function videoFolderFiles(bookFolder: string): string[] {
    const folder = Path.join(bookFolder, "video");
    return fs.existsSync(folder) ? fs.readdirSync(folder) : [];
}

test("a video imported into a new Sign Language book after it got its title lands in the renamed book [Test Case ID 586]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);

    const firstFolder = await step(
        "Make a book from the Sign Language template and type its title on the cover",
        async () => {
            const folder = await makeBookFromTemplate(
                page,
                kSignLanguageTemplate,
            );
            await typeInGroup(page, ".bookTitle", "en", kTitle);
            return folder;
        },
    );
    if (!firstFolder) throw new Error("No book was made.");

    const added = await step(
        'Add a "Just Video" page, which leaves the cover and so renames the book',
        async () => {
            const before = (await getContentPages(page)).map((p) => p.id);
            await addPage(page, "Just Video", 1, kSignLanguageTemplate);
            const newPages = (await getContentPages(page)).filter(
                (p) => !before.includes(p.id),
            );
            expect(
                newPages,
                'adding a "Just Video" page should give the book one new content page',
            ).toHaveLength(1);
            await goToPage(page, newPages[0].id);
            return newPages[0];
        },
    );
    if (!added) throw new Error("No page was added.");

    const bookFolder = await step(
        "Find the book's folder under its new title",
        async () => {
            const folder = await findBookFolder(page, kTitle);
            // This is the condition BL-9133 needed: without a rename, the video would be imported
            // into the folder the page was first loaded from, and the test would prove nothing.
            expect(
                Path.basename(folder),
                "sanity check: typing the title should have renamed the book's folder after it",
            ).toBe(kTitle);
            expect(
                fs.existsSync(firstFolder),
                `sanity check: the book's first folder, ${firstFolder}, should be gone after the rename`,
            ).toBe(false);
            expect(
                videoFolderFiles(folder),
                "sanity check: the new book has no videos yet",
            ).toEqual([]);
            return folder;
        },
    );
    if (!bookFolder) throw new Error("The renamed book was not found.");

    const videoBox = videoBoxes(page).first();
    const fileName = await step(
        "Import a video into the page's video box with the Sign Language tool",
        async () => {
            await expect(
                videoBoxes(page),
                'sanity check: a "Just Video" page has one video box',
            ).toHaveCount(1);
            await expect(
                videoBox,
                "sanity check: the new page's video box starts empty",
            ).toHaveClass(/bloom-noVideoSelected/);
            await chooseVideoFile(page, videoBox, SHORT_VIDEO);
            return getVideoFileName(videoBox);
        },
    );
    if (!fileName) throw new Error("The video box shows no video file.");

    await step("The video is in the renamed book and plays there", async () => {
        expect(
            videoFolderFiles(bookFolder),
            "Import did not put exactly the box's video into the renamed book's video folder",
        ).toEqual([fileName]);
        expect(
            fs.statSync(Path.join(bookFolder, "video", fileName)).size,
            "Import left an empty video file in the book",
        ).toBeGreaterThan(0);
        expect(
            fs.existsSync(firstFolder),
            `Import put something back in the book's old folder, ${firstFolder}`,
        ).toBe(false);

        // The page asks Bloom's server for the video by its path in the book, so the video playing
        // there shows that the server found the file where the page says it is.
        const video = videoBox.locator("video");
        await expect
            .poll(
                () =>
                    video.evaluate((v: HTMLVideoElement) => ({
                        error: v.error?.code ?? 0,
                        hasMetadata: v.readyState >= 1,
                        duration: v.duration,
                    })),
                {
                    timeout: 30000,
                    message:
                        "The video box never loaded the imported video from the book.",
                },
            )
            .toMatchObject({ error: 0, hasMetadata: true });
        const duration = await video.evaluate(
            (v: HTMLVideoElement) => v.duration,
        );
        expect(
            duration,
            "The loaded video should be about as long as the one-second video imported",
        ).toBeGreaterThan(0.5);
        expect(duration).toBeLessThan(2);
    });

    await step(
        "Leave the page, and the saved book has the video in that page's video box",
        async () => {
            const cover = (await getPages(page)).find((p) => !p.isContentPage);
            await goToPage(page, cover!.id);
            const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
            const savedSources = await page.evaluate(
                ({ source, pageId }) => {
                    const dom = new DOMParser().parseFromString(
                        source,
                        "text/html",
                    );
                    return Array.from(
                        dom.querySelectorAll(
                            `.bloom-page[id="${pageId}"] .bloom-videoContainer video source`,
                        ),
                    ).map((s) => s.getAttribute("src") ?? "");
                },
                { source: html, pageId: added.id },
            );
            expect(
                savedSources.map((src) =>
                    decodeURIComponent(src.split(/[?#]/)[0]),
                ),
                "The saved book's video page does not point at the imported video",
            ).toEqual([`video/${fileName}`]);
        },
    );
});
