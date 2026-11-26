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
    getShownPageId,
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
            // Add Page opens the new page itself. The import below is made on that first load
            // after the rename, as a person makes it, so the test does not jump to the page again.
            await expect
                .poll(() => getShownPageId(page), {
                    timeout: 30000,
                    message: "Adding the page did not open it",
                })
                .toBe(newPages[0].id);
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

        // The page asks Bloom's server for the video by its path in the book, so a frame of the
        // video showing in the box shows that the server found the file where the page says it is.
        const video = videoBox.locator("video");
        await expect(video, "The video box has no video to show").toBeVisible();
        await expect
            .poll(
                () =>
                    video.evaluate((v: HTMLVideoElement) => {
                        const box = v.getBoundingClientRect();
                        // A video with no decoded frame draws nothing on a canvas, so an opaque
                        // pixel there is a frame the box can show.
                        let opaquePixels = 0;
                        if (v.videoWidth > 0 && v.videoHeight > 0) {
                            const canvas = document.createElement("canvas");
                            canvas.width = v.videoWidth;
                            canvas.height = v.videoHeight;
                            const context = canvas.getContext("2d")!;
                            context.drawImage(v, 0, 0);
                            const data = context.getImageData(
                                0,
                                0,
                                canvas.width,
                                canvas.height,
                            ).data;
                            for (let i = 3; i < data.length; i += 4)
                                if (data[i] === 255) opaquePixels++;
                        }
                        return {
                            error: v.error?.code ?? 0,
                            hasSize: box.width > 0 && box.height > 0,
                            // Unlike toBeVisible, this also counts a video that is hidden by
                            // opacity, its own or a container's, as not shown.
                            shown: v.checkVisibility({
                                opacityProperty: true,
                                visibilityProperty: true,
                            }),
                            hasFrame: opaquePixels > 0,
                        };
                    }),
                {
                    timeout: 30000,
                    message:
                        "The video box never showed a frame of the imported video from the book.",
                },
            )
            .toEqual({ error: 0, hasSize: true, shown: true, hasFrame: true });
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
