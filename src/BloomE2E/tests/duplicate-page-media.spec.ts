// Duplicating a page that has narration or a video: the copy gets a recording and a video file of
// its own, so that recording again or trimming on the copy leaves the original page alone.
// Automates the audio and video sections of the manual test "Duplicate Page [Manual portion]"
// (Test Case ID 810). duplicate-page.spec.ts does the same for a picture, and
// duplicate-page-many-times.spec.ts covers that card's last line.
//
// A test cannot record from a microphone (AUTOMATION-DEBT.md, "Native OS dialogs hang
// automation"), so "recording" here is the Talking Book tool's Import Recording button, which
// writes the same file a recording would, and "redoing the recording" is importing a different
// file over it. The first recording is imported too, rather than seeded with addNarration: only
// Bloom's own import leaves the text box marked as holding a whole-text-box recording, which is
// what the copy must have for a second import to replace it.
//
// The tests are serial because each one works on the book the first one builds.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    setContentLanguages,
    typeInGroup,
    type IBookPage,
} from "../helpers/bookMaking";
import { setBranding } from "../helpers/collectionSettings";
import { duplicatePageWithButton } from "../helpers/pageList";
import {
    expectNarrationToBe,
    getNarrationSentences,
    importNarration,
    openToolboxWithTalkingBook,
    replaceNarrationByImport,
    sampleNarrationFile,
    writeSecondSampleNarration,
} from "../helpers/talkingBook";
import {
    chooseVideoFile,
    getVideoFileName,
    getVideoTrim,
    readVideoFile,
    SHORT_VIDEO,
    trimVideoStart,
    videoBoxes,
} from "../helpers/videos";

test.use({
    collectionSpec: { name: "duplicate-page-media", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

const kTextGroup = ".bloom-translationGroup";

// The book every test works on, and its two content pages. The book is given no title, so it
// stays in the folder Bloom made for it. Set by the first test.
let bookFolder: string;
let textPage: IBookPage;
let videoPage: IBookPage;

/** The ids of the recordings on the page being shown: one, for a page with one recorded text box. */
const getRecordingIds = async (page: Page) =>
    (await getNarrationSentences(page)).map((s) => s.id);

test.describe("duplicating a page with narration or a video", () => {
    test("builds a book with a text page and a video page [Test Case ID 810]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        // Import Recording, which redoes the recording, is a Pro-tier feature; see
        // import-recording.spec.ts.
        await setBranding(page, "Sample-Pro");
        bookFolder = await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Just Text");
        await addPage(page, "Video Over Text");
        await setContentLanguages(page, ["en"]);
        [textPage, videoPage] = await getContentPages(page);
        await goToPage(page, textPage.id);
        await typeInGroup(page, kTextGroup, "en", "A sentence to record.");
    });

    test("recording again on a copy leaves the original page's recording alone [Test Case ID 810]", async ({
        page,
    }, testInfo) => {
        test.setTimeout(300000);
        const secondRecordingFile = testInfo.outputPath("second-recording.mp3");
        writeSecondSampleNarration(secondRecordingFile);

        // "Do a bit of talking book recording."
        await goToPage(page, textPage.id);
        await openToolboxWithTalkingBook(page);
        await importNarration(
            page,
            bookFolder,
            kTextGroup,
            "en",
            sampleNarrationFile,
        );
        await expect
            .poll(() => getRecordingIds(page), {
                message:
                    "The imported recording should be the text box's one recording.",
            })
            .toHaveLength(1);
        const [originalId] = await getRecordingIds(page);
        await expectNarrationToBe(
            bookFolder,
            originalId,
            sampleNarrationFile,
            "sanity check: the original page plays the first recording",
        );

        // "Duplicate the page." The copy has a recording of its own, with the same sound.
        const copy = await duplicatePageWithButton(page);
        await goToPage(page, copy.id);
        await expect
            .poll(() => getRecordingIds(page), {
                message: "The copy should have one recording, like its source.",
            })
            .toHaveLength(1);
        const [copyId] = await getRecordingIds(page);
        expect(
            copyId,
            "The copy's recording must not be the original's.",
        ).not.toBe(originalId);
        await expectNarrationToBe(
            bookFolder,
            copyId,
            sampleNarrationFile,
            "The copy should start with the recording of the page it was made from.",
        );

        // "Redo the recording of the same text." THE ACTION UNDER TEST.
        const replacedId = await replaceNarrationByImport(
            page,
            bookFolder,
            kTextGroup,
            "en",
            secondRecordingFile,
        );
        expect(replacedId).toBe(copyId);

        // "Go back to the original page. Verify that the original recording is still what would
        // play": the page still names the same recording, and its file is the first one.
        await goToPage(page, textPage.id);
        await expect.poll(() => getRecordingIds(page)).toEqual([originalId]);
        await expectNarrationToBe(
            bookFolder,
            originalId,
            sampleNarrationFile,
            "Recording again on the copy changed what the original page plays.",
        );

        // And the copy kept its new recording through the save that leaving it did.
        await goToPage(page, copy.id);
        await expect.poll(() => getRecordingIds(page)).toEqual([copyId]);
        await expectNarrationToBe(
            bookFolder,
            copyId,
            secondRecordingFile,
            "The copy should play the second recording.",
        );
    });

    test("trimming the video on a copy leaves the original page's video untrimmed [Test Case ID 810]", async ({
        page,
    }) => {
        test.setTimeout(300000);

        // "Add a video."
        await goToPage(page, videoPage.id);
        await chooseVideoFile(page, videoBoxes(page).first(), SHORT_VIDEO);
        const originalFile = await getVideoFileName(videoBoxes(page).first());
        expect(
            await getVideoTrim(videoBoxes(page).first()),
            "sanity check: a video just added is not trimmed",
        ).toBeUndefined();
        const originalVideo = readVideoFile(bookFolder, originalFile);

        // "Duplicate the page." The copy has a video file of its own.
        const copy = await duplicatePageWithButton(page);
        await goToPage(page, copy.id);
        const copyFile = await getVideoFileName(videoBoxes(page).first());
        expect(copyFile, "The copy should have a video.").not.toBe("");
        expect(
            copyFile,
            "The copy's video must not be the original's file.",
        ).not.toBe(originalFile);
        expect(await getVideoTrim(videoBoxes(page).first())).toBeUndefined();

        // "Trim the video using the Sign Language Tool." THE ACTION UNDER TEST.
        const trim = await trimVideoStart(page, videoBoxes(page).first(), 3);
        expect(trim.start).toBeCloseTo(0.3, 5);

        // "Return to the original page. Verify that the original video is not trimmed."
        await goToPage(page, videoPage.id);
        expect(await getVideoFileName(videoBoxes(page).first())).toBe(
            originalFile,
        );
        expect(
            await getVideoTrim(videoBoxes(page).first()),
            "Trimming the copy's video trimmed the original's.",
        ).toBeUndefined();
        expect(
            readVideoFile(bookFolder, originalFile).equals(originalVideo),
            "Trimming the copy's video changed the original's video file.",
        ).toBe(true);

        // And the copy kept its trim through the save that leaving it did.
        await goToPage(page, copy.id);
        expect(
            (await getVideoTrim(videoBoxes(page).first()))?.start,
        ).toBeCloseTo(0.3, 5);
    });
});
