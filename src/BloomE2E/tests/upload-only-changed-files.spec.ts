// Re-uploading a book to Bloom Library sends only the files that changed: the server copies the
// rest from the previous upload. Automates the manual test "Re-upload Sends Only Changed Files"
// (Test Case ID 219), which came from BL-12568.
//
// The test makes a book with a picture, a narration recording and a video, uploads it to
// dev.bloomlibrary.org for real, then re-uploads it four times: after a text change, after changing
// the picture, after replacing the recording, and after replacing the video. Each time it reads which
// files Bloom sent from the Upload Progress box (see uploadBookForReal) and checks that only the
// file that changed went up again, besides the book's .htm and meta.json, which the card allows,
// and that the text change sent the .htm. A book with a video gets no PDF, as the card notes, so
// there is no PDF to allow for.
//
// The recording is imported through the Talking Book tool's Import Recording button, which needs a
// Pro subscription (the collection has an enterprise one) and makes a recording of the whole text
// box; importing again over it is how a test "records again", since recording needs a microphone.
//
// Like bulk-upload-quick-test.spec.ts, it signs in to the sandbox for real as the shared test
// account, so its book's title has a prefix of its own, and it deletes only books with that prefix
// (see findBooksUploadedBy). Each run's title is unique, and a run clears away only this test's
// books that are an hour old, so that two runs at once (the nightly and a bot's verification run)
// leave each other's book alone. The account's password comes from BLOOM_E2E_TESTER_EMAIL_BLORG_PASSWORD:
// without it the test skips locally and fails on CI.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { setCopyrightHolder } from "../helpers/copyrightAndLicense";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { chooseImageFile, getImagePlacement } from "../helpers/images";
import {
    acceptAllAgreements,
    clickToFixMissingItem,
    openPublishToWeb,
    uploadBookForReal,
} from "../helpers/libraryPublish";
import {
    signBloomIntoLibraryForReal,
    skipIfNoLibraryPassword,
} from "../helpers/bloomLibraryAccount";
import {
    deleteBooksUploadedBy,
    type IBloomLibraryLogin,
} from "../helpers/bloomLibraryServer";
import {
    importNarration,
    openToolboxWithTalkingBook,
    replaceNarrationByImport,
    writeSecondSampleNarration,
} from "../helpers/talkingBook";
import {
    expandToolByHeader,
    hideToolbox,
    showToolbox,
} from "../helpers/toolbox";
import {
    addPageWithVideo,
    chooseVideoFile,
    getVideoFileName,
    SHORT_VIDEO,
    videoBoxes,
} from "../helpers/videos";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "upload-only-changed-files",
        languages: ["en"],
        // Import Recording, which gives the book its narration, needs a Pro subscription or better.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const FIXTURES = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
);
const FIRST_PICTURE = Path.join(FIXTURES, "images", "bird.png");
const SECOND_PICTURE = Path.join(FIXTURES, "images", "house-landscape.png");
const SECOND_VIDEO = Path.join(FIXTURES, "videos", "short-2s.mp4");

// The book's title starts with a prefix that no other real-upload test's books start with, and
// ends with a part of this run's own.
const TITLE_PREFIX = "Upload Only Changed Files";
const BOOK_TITLE = `${TITLE_PREFIX} ${Date.now().toString(36)}`;
// A run deletes this test's books that are at least this old, which only a crashed run leaves.
const STALE_BOOK_MINUTES = 60;

let login: IBloomLibraryLogin | undefined;

/** The book's picture, recording and video files, by the names the upload's progress lines use. */
interface IMediaFiles {
    picture: string;
    recording: string;
    video: string;
}

/**
 * Upload the book again from Publish: Web, and return which files went up. Leaving Edit for
 * Publish is also what makes Bloom save the page that was being edited.
 */
async function reupload(page: Page): Promise<string[]> {
    await openPublishToWeb(page);
    await acceptAllAgreements(page);
    return uploadBookForReal(page);
}

/**
 * Check that the upload sent exactly the `changed` media file (nothing, when the change was to the
 * text), besides the files the card allows to go up every time: the book's .htm, which carries any
 * change, and meta.json. `current` are the media files after the change; a replaced picture or
 * video comes in under a new name.
 */
function expectOnlyChangedFileSent(
    sent: string[],
    htmFile: string,
    current: IMediaFiles,
    changed: keyof IMediaFiles | undefined,
    what: string,
): void {
    const allowedEveryTime = [htmFile, "meta.json"];
    expect(
        sent.filter((file) => !allowedEveryTime.includes(file)).sort(),
        `${what}, the upload should have sent ${changed ? `only the new ${changed} (${current[changed]})` : "no file"} ` +
            `besides ${allowedEveryTime.join(" and ")}. It sent: ${sent.join(", ")}`,
    ).toEqual(changed ? [current[changed]] : []);
}

test.describe("re-uploading a book to dev.bloomlibrary.org", () => {
    test.afterAll(async () => {
        if (login) await deleteBooksUploadedBy(login, BOOK_TITLE);
    });

    test("a re-upload sends only the picture, recording or video that changed [Test Case ID 219]", async ({
        page,
    }) => {
        skipIfNoLibraryPassword();
        // Five real uploads, each with a thumbnail to make, plus two video re-encodings. A healthy
        // run takes about four minutes.
        test.setTimeout(1200000);

        login = await signBloomIntoLibraryForReal(page);
        // Clear away what crashed earlier runs left, leaving any run going on now alone.
        await deleteBooksUploadedBy(login, TITLE_PREFIX, STALE_BOOK_MINUTES);

        // ---- A book with a picture, a recording and a video ----------------------------------------
        await makeBookFromTemplate(page, "Basic Book");
        await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);

        await addPage(page, "Just Text");
        const [textPage] = await getContentPages(page);
        await goToPage(page, textPage.id);
        await typeInGroup(page, ".bloom-translationGroup", "en", "Hello.");
        const bookFolder = await findBookFolder(page, BOOK_TITLE);
        await openToolboxWithTalkingBook(page);
        await importNarration(
            page,
            bookFolder,
            ".bloom-translationGroup",
            "en",
        );
        // Typing with the Talking Book tool open can stall the page's updates to Bloom
        // (BL-17186), and the text change below types on this page.
        await hideToolbox(page);

        const contentPagesBefore = (await getContentPages(page)).map(
            (p) => p.id,
        );
        await addPage(page, "Just an Image");
        const picturePage = (await getContentPages(page)).find(
            (p) => !contentPagesBefore.includes(p.id),
        )!;
        await goToPage(page, picturePage.id);
        await chooseImageFile(page, FIRST_PICTURE);
        const picture = (await getImagePlacement(page)).fileName;

        const videoPage = await addPageWithVideo(page, SHORT_VIDEO);
        const video = await getVideoFileName(videoBoxes(page).first());
        await hideToolbox(page);

        // The recording is named after the text box it narrates; read the name back from the page.
        await goToPage(page, textPage.id);
        const recordingId = await editablePageFrame(page)
            .locator(".bloom-translationGroup .audio-sentence")
            .first()
            .getAttribute("id");
        expect(
            recordingId,
            "The imported recording's text box has no id.",
        ).toBeTruthy();
        const recording = `${recordingId}.mp3`;
        await goToPage(page, (await getPages(page))[0].id);

        // A copyright, which an upload needs, through Publish: Web's "Click to fix".
        await openPublishToWeb(page);
        await clickToFixMissingItem(page, "Copyright");
        await setCopyrightHolder(page, "Bloom Automated Test");

        // ---- First upload: everything goes up ------------------------------------------------------
        let media: IMediaFiles = { picture, recording, video };
        const firstUpload = await reupload(page);
        expect(
            firstUpload,
            `The first upload should have sent the book's picture, recording and video. It sent: ${firstUpload.join(", ")}`,
        ).toEqual(expect.arrayContaining(Object.values(media)));

        // ---- A text change: the .htm goes up, the media do not -------------------------------------
        await switchTab(page, "edit");
        await goToPage(page, textPage.id);
        await typeInGroup(
            page,
            ".bloom-translationGroup",
            "en",
            "Hello again.",
        );
        await goToPage(page, picturePage.id);
        const afterTextChange = await reupload(page);
        const htmFile = `${Path.basename(bookFolder)}.htm`;
        expect(
            afterTextChange,
            `After a text change, the upload should have sent ${htmFile}. It sent: ${afterTextChange.join(", ")}`,
        ).toContain(htmFile);
        expectOnlyChangedFileSent(
            afterTextChange,
            htmFile,
            media,
            undefined,
            "After a text change",
        );

        // ---- A changed picture: only it goes up ----------------------------------------------------
        await switchTab(page, "edit");
        await goToPage(page, picturePage.id);
        await chooseImageFile(page, SECOND_PICTURE);
        media = { ...media, picture: (await getImagePlacement(page)).fileName };
        await goToPage(page, textPage.id);
        expectOnlyChangedFileSent(
            await reupload(page),
            htmFile,
            media,
            "picture",
            "After the picture changed",
        );

        // ---- A changed recording: only it goes up --------------------------------------------------
        await switchTab(page, "edit");
        await goToPage(page, textPage.id);
        // Adding the video page left the Sign Language tool expanded.
        await expandToolByHeader(page, "talkingBook");
        const secondRecording = test.info().outputPath("second-recording.mp3");
        writeSecondSampleNarration(secondRecording);
        await replaceNarrationByImport(
            page,
            bookFolder,
            ".bloom-translationGroup",
            "en",
            secondRecording,
        );
        await hideToolbox(page);
        await goToPage(page, picturePage.id);
        expectOnlyChangedFileSent(
            await reupload(page),
            htmFile,
            media,
            "recording",
            "After the recording changed",
        );

        // ---- A changed video: only it goes up ------------------------------------------------------
        await switchTab(page, "edit");
        await goToPage(page, videoPage.id);
        // Clicking the video box picks the Sign Language tool, but only in an open toolbox.
        await showToolbox(page);
        await chooseVideoFile(page, videoBoxes(page).first(), SECOND_VIDEO);
        // chooseVideoFile returns once the box has a video, which the old one already is; the
        // re-encoded new one comes in under a new name.
        await expect
            .poll(() => getVideoFileName(videoBoxes(page).first()), {
                timeout: 120000,
                message: `The video box never showed a video other than ${media.video}.`,
            })
            .not.toBe(media.video);
        media = {
            ...media,
            video: await getVideoFileName(videoBoxes(page).first()),
        };
        await hideToolbox(page);
        await goToPage(page, textPage.id);
        expectOnlyChangedFileSent(
            await reupload(page),
            htmFile,
            media,
            "video",
            "After the video changed",
        );
    });
});
