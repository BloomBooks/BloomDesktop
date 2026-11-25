// Publishing a sign language book as a BloomPUB: a book with a video, in a collection that has a
// sign language. The BloomPUB screen has Sign Language on, the preview plays the video when the
// reader turns to its page and again when the reader taps it, and the BloomPUB carries the video
// file that its page plays.
//
// Test Case ID 594 ("Publish SL To BloomPUB"). Two lines of the card are not checked here. "Save to
// disk": the Save button opens a native save dialog, which no test can answer (AUTOMATION-DEBT.md,
// "Native OS dialogs hang automation"), so the test reads the files Bloom stages for the BloomPUB
// instead, which are the files an unzipped .bloompub holds (see stageBloomPub). "Verify in BloomPUB
// Viewer": that is another product, with no end-to-end suite.

import { expect, test } from "../fixtures/bloomTest";
import {
    expectPreviewVideosPlayed,
    goToPreviewPage,
    previewVideos,
    startRecordingPreviewVideos,
    tapPreviewVideo,
} from "../helpers/bloomPlayer";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    expectSignLanguageFeature,
    getCollectionSignLanguage,
    getSignLanguageTooltipText,
    getStagedVideoSources,
    getStagedVideos,
    selectPublishDestination,
    showBloomPubPreview,
    stageBloomPub,
} from "../helpers/publish";
import {
    chooseVideoFile,
    getVideoFileName,
    readVideoFile,
    SHORT_VIDEO,
    videoBoxes,
} from "../helpers/videos";

const kSignLanguage = "ase";

test.use({
    collectionSpec: {
        name: "publish-sign-language-bloompub",
        languages: ["en"],
        signLanguage: kSignLanguage,
    },
});

test("a sign language book's video plays in the BloomPUB preview and goes into the BloomPUB [Test Case ID 594]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);

    const book = await step(
        "Make a book with a page that has a sign language video",
        async () => {
            const folder = await makeBookFromTemplate(page, "Basic Book");
            const before = (await getContentPages(page)).map((p) => p.id);
            await addPage(page, "Video Over Text");
            const videoPage = (await getContentPages(page)).find(
                (p) => !before.includes(p.id),
            )!;
            await goToPage(page, videoPage.id);
            await chooseVideoFile(page, videoBoxes(page).first(), SHORT_VIDEO);
            const videoFile = await getVideoFileName(videoBoxes(page).first());
            return {
                videoPageId: videoPage.id,
                videoFile,
                video: readVideoFile(folder, videoFile),
            };
        },
    );
    if (!book) throw new Error("The book was not made.");
    expect(
        book.video.length,
        "sanity check: the book's video file should not be empty",
    ).toBeGreaterThan(0);

    await step("Go to Publish, BloomPUB: Sign Language is on", async () => {
        await selectPublishDestination(page, "BloomPUB");
        const signLanguage = await getCollectionSignLanguage(page);
        expect(
            signLanguage.tag,
            "sanity check: the collection should have the sign language it was made with",
        ).toBe(kSignLanguage);
        await expectSignLanguageFeature(page, {
            enabled: true,
            checked: true,
            tooltip: await getSignLanguageTooltipText(page, {
                signLanguageName: signLanguage.name,
            }),
        });
    });

    const player = await step("Show the preview", async () => {
        const frame = await showBloomPubPreview(page);
        await startRecordingPreviewVideos(frame);
        return frame;
    });
    if (!player) throw new Error("The preview was not shown.");

    // The book's video, each time the preview plays it to its end.
    const playedOnce = { pageId: book.videoPageId, fileName: book.videoFile };

    await step(
        "Turn to the video page: the video shows, and plays to its end",
        async () => {
            await goToPreviewPage(page, player, book.videoPageId);
            await expect(
                previewVideos(player),
                "The preview's video page should show its one video.",
            ).toBeVisible();
            await expectPreviewVideosPlayed(
                player,
                [playedOnce],
                "The preview should play the page's video to its end, once, when the reader turns to the page.",
            );
        },
    );

    await step("Tap the video: it plays to its end again", async () => {
        await tapPreviewVideo(player);
        await expectPreviewVideosPlayed(
            player,
            [playedOnce, playedOnce],
            "Tapping the video after it has ended should play it to its end once more.",
        );
    });

    await step(
        "Make the BloomPUB's files: the video is there, and the page plays it",
        async () => {
            const stagedFolder = await stageBloomPub(page);
            const stagedVideos = getStagedVideos(stagedFolder);
            expect(
                stagedVideos.map((video) => video.fileName),
                "The BloomPUB should carry the book's one video.",
            ).toEqual([book.videoFile]);
            expect(
                stagedVideos[0].content.equals(book.video),
                "The BloomPUB's video should be the book's video file, unchanged, since it was not trimmed.",
            ).toBe(true);
            expect(
                getStagedVideoSources(stagedFolder),
                "The BloomPUB's video page should play the video file the BloomPUB carries.",
            ).toEqual([book.videoFile]);
        },
    );
});
