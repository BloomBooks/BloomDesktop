// Publishing a sign language book as a BloomPUB: a book with a video, in a collection that has a
// sign language. The BloomPUB screen has Sign Language on, the preview plays the video when the
// reader turns to its page and again when the reader taps it, and the .bloompub file carries the
// video file that its video page plays.
//
// Test Case ID 594 ("Publish SL To BloomPUB"). Two lines of the card are not checked as written.
// "Save to disk": the Save button opens a native save dialog, which no test can answer
// (AUTOMATION-DEBT.md, "Native OS dialogs hang automation"). The test makes the .bloompub with
// "Make All BloomPUBs from Collection" instead, which writes the same file with no dialog; that
// command needs an enterprise subscription, which is why the collection has one. "Verify in
// BloomPUB Viewer": that is another product, with no end-to-end suite; the test reads the
// .bloompub's own files.

import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import {
    expectPreviewVideosPlayed,
    goToPreviewPage,
    previewVideos,
    startRecordingPreviewVideos,
    tapPreviewVideo,
} from "../helpers/bloomPlayer";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import {
    getBloomPubPageVideos,
    getBloomPubVideos,
    makeAllBloomPubs,
    openMakeAllBloomPubsDialog,
    setMakeAllBloomPubsOption,
} from "../helpers/bulkBloomPub";
import {
    kEnterpriseSubscriptionCode,
    kTestBookshelves,
} from "../helpers/collectionSettings";
import {
    expectSignLanguageFeature,
    getCollectionSignLanguage,
    getSignLanguageTooltipText,
    selectPublishDestination,
    showBloomPubPreview,
} from "../helpers/publish";
import {
    addPageWithVideo,
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
        // Make All BloomPUBs from Collection, which makes the .bloompub file, needs it.
        subscriptionCode: kEnterpriseSubscriptionCode,
        // In a collection with no bookshelf, Make All BloomPUBs fails (BL-17197): it tries to make
        // a .bloomshelf file that the dialog cannot turn off. With a bookshelf the test turns it
        // off, since the case is about one book and no bookshelf.
        bookshelf: kTestBookshelves[0],
    },
});

test("a sign language book's video plays in the BloomPUB preview and goes into the .bloompub file [Test Case ID 594]", async ({
    page,
    step,
}) => {
    test.setTimeout(420000);

    const book = await step(
        "Make a book with a page that has a sign language video",
        async () => {
            const folder = await makeBookFromTemplate(page, "Basic Book");
            const videoPage = await addPageWithVideo(page, SHORT_VIDEO);
            const videoFile = await getVideoFileName(videoBoxes(page).first());
            return {
                folder,
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

    const made = await step("Make the .bloompub file", async () => {
        await openMakeAllBloomPubsDialog(page);
        await setMakeAllBloomPubsOption(page, "bookshelfFile", false);
        await setMakeAllBloomPubsOption(page, "bloomBundle", false);
        return makeAllBloomPubs(page);
    });
    if (!made) throw new Error("The .bloompub file was not made.");

    await step(
        "Check the .bloompub: the video is in it, and its video page plays it",
        async () => {
            const bloomPubName = `${Path.basename(book.folder)}.bloompub`;
            const bloomPub = made.files.get(bloomPubName);
            if (!bloomPub)
                throw new Error(
                    `Bloom made no ${bloomPubName}. It made: ${[...made.files.keys()].join(", ")}. ` +
                        `The progress box said:\n${made.log}`,
                );
            const videoInBloomPub = `video/${book.videoFile}`;
            const videos = getBloomPubVideos(bloomPub);
            expect(
                videos.map((video) => video.name),
                "The .bloompub should carry the book's one video, in its video folder.",
            ).toEqual([videoInBloomPub]);
            expect(
                videos[0].content.equals(book.video),
                "The .bloompub's video should be the book's video file, unchanged, since it was not trimmed.",
            ).toBe(true);
            expect(
                await getBloomPubPageVideos(page, bloomPub),
                "Only the video page of the .bloompub should play a video, and it should play the file the .bloompub carries.",
            ).toEqual([
                { pageId: book.videoPageId, sources: [videoInBloomPub] },
            ]);
        },
    );
});
