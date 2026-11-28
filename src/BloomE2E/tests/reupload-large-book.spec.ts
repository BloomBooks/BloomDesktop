// A book of more than a thousand files uploads to Bloom Library, and re-uploading it after a small
// change sends only what changed: the server copies the rest, all the recordings among them, from
// the previous upload. Automates the manual test "Re-upload Large Book" (Test Case ID 183).
//
// The manual case uses a large book kept on the team's drive ("atomic upload test"), which has over
// 1000 files. This test makes a book of that size itself: a page with RECORDING_COUNT sentences,
// each with a narration recording of its own, which Bloom uploads as one .mp3 per sentence. More
// than a thousand files matters because S3 answers a listing, a copy or a delete with at most 1000
// keys at a time, so an upload that handles its files in one batch breaks only on a book this big.
// The sentences and recordings are written into the book folder while Bloom is shut down, since
// recording 1100 sentences through the Talking Book tool would need a microphone, or 1100 imports.
//
// The test uploads the book to dev.bloomlibrary.org for real, adds a page with a picture, uploads
// it again, and checks:
// - both uploads finish without "There was a problem uploading your book";
// - the re-upload sends none of the .mp3 files, and nothing but the new picture and the files that
//   change with the new page (the book's .htm and PDF) or may change with every upload (meta.json
//   and the cover thumbnails, as upload-only-changed-files.spec.ts allows);
// - the re-uploaded .htm shows the new picture and still holds every recorded sentence;
// - the re-uploaded copy on the server holds every recording, so the server did copy the files
//   that Bloom skipped.
//
// Like the other real-upload tests, it signs in to the sandbox as the shared test account, gives
// its book a title prefix of its own, and deletes only books with that prefix. The account's
// password comes from BLOOM_E2E_TESTER_EMAIL_BLORG_PASSWORD: without it the test skips locally and
// fails on CI.

import { randomUUID } from "node:crypto";
import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { selectBook } from "../helpers/collection";
import { setCopyrightHolder } from "../helpers/copyrightAndLicense";
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
    fetchUploadedBookHtml,
    findBooksUploadedBy,
    getLatestUploadBaseUrl,
    type IBloomLibraryLogin,
    listUploadedBookFiles,
} from "../helpers/bloomLibraryServer";
import { sampleNarrationFile } from "../helpers/talkingBook";
import { getUiString } from "../helpers/api";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "reupload-large-book",
        languages: ["en"],
    },
});

const FIXTURES = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
);
const PICTURE = Path.join(FIXTURES, "images", "bird.png");

/** How many recordings the book gets: enough, with the book's other files, for over a thousand. */
const RECORDING_COUNT = 1100;

const TITLE_PREFIX = "Reupload Large Book";
const BOOK_TITLE = `${TITLE_PREFIX} ${Date.now().toString(36)}`;
// A run deletes this test's books that are at least this old, which only a crashed run leaves.
const STALE_BOOK_MINUTES = 90;
// The text typed on the page that the recorded sentences then replace in the .htm.
const MARKER_TEXT = "Large book marker.";

let login: IBloomLibraryLogin | undefined;

/**
 * Replace the marker text in the book's .htm with `count` sentences, each a narration span with an
 * id of its own, and put a copy of the suite's sample recording in the book's audio folder for each,
 * named after its id, as the Talking Book tool names a recording (by sentence). Returns the
 * recordings' file names. Bloom must not be running.
 */
function addRecordedSentences(bookFolder: string, count: number): string[] {
    const htmPath = Path.join(bookFolder, `${Path.basename(bookFolder)}.htm`);
    const html = fs.readFileSync(htmPath, "utf8");
    const occurrences = html.split(MARKER_TEXT).length - 1;
    if (occurrences !== 1)
        throw new Error(
            `The book's .htm holds "${MARKER_TEXT}" ${occurrences} times, not once: ${htmPath}`,
        );
    const audioFolder = Path.join(bookFolder, "audio");
    fs.mkdirSync(audioFolder, { recursive: true });
    const sample = fs.readFileSync(Path.resolve(sampleNarrationFile));
    const ids = Array.from({ length: count }, () => `i${randomUUID()}`);
    const spans = ids
        .map(
            (id, index) =>
                `<span id="${id}" class="audio-sentence" recordingmd5="undefined" data-duration="1.645714">Sentence ${index + 1}.</span>`,
        )
        .join(" ");
    fs.writeFileSync(htmPath, html.replace(MARKER_TEXT, spans), "utf8");
    for (const id of ids)
        fs.writeFileSync(Path.join(audioFolder, `${id}.mp3`), sample);
    return ids.map((id) => `${id}.mp3`);
}

/** Upload the book from Publish: Web, and return the files sent and the Upload Progress text. */
async function upload(
    page: Page,
    timeoutMs: number,
): Promise<{ sent: string[]; log: string }> {
    await openPublishToWeb(page);
    await acceptAllAgreements(page);
    const sent = await uploadBookForReal(page, timeoutMs);
    return {
        sent,
        log: await page.getByTestId("progress-box-log").innerText(),
    };
}

test.describe("re-uploading a large book to dev.bloomlibrary.org", () => {
    test.afterAll(async () => {
        if (login) await deleteBooksUploadedBy(login, BOOK_TITLE);
    });

    test("a book of over 1000 files uploads, and its re-upload skips the recordings [Test Case ID 183]", async ({
        bloomApp,
    }) => {
        skipIfNoLibraryPassword();
        // Two real uploads, the first of more than a thousand files.
        test.setTimeout(2400000);

        let page = bloomApp.page;
        login = await signBloomIntoLibraryForReal(page);
        await deleteBooksUploadedBy(login, TITLE_PREFIX, STALE_BOOK_MINUTES);

        // ---- A book with a page of text ------------------------------------------------------------
        await makeBookFromTemplate(page, "Basic Book");
        await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
        await addPage(page, "Just Text");
        const [textPage] = await getContentPages(page);
        await goToPage(page, textPage.id);
        await typeInGroup(page, ".bloom-translationGroup", "en", MARKER_TEXT);
        // Leaving the page saves it, and leaving the cover gave the folder the title's name.
        await goToPage(page, (await getPages(page))[0].id);
        await switchTab(page, "collection");
        const bookFolder = await findBookFolder(page, BOOK_TITLE);

        // ---- 1100 recorded sentences, written while Bloom is shut down -----------------------------
        let recordings: string[] = [];
        await bloomApp.restart(() => {
            recordings = addRecordedSentences(bookFolder, RECORDING_COUNT);
        });
        page = bloomApp.page;
        await selectBook(page, bookFolder);

        const problemNotice = await getUiString(
            page,
            "PublishTab.Upload.GenericUploadProblemNotice",
            "There was a problem uploading your book.",
        );

        // A copyright, which an upload needs, through Publish: Web's "Click to fix".
        await openPublishToWeb(page);
        await clickToFixMissingItem(page, "Copyright");
        await setCopyrightHolder(page, "Bloom Automated Test");

        // ---- First upload: every file goes up ------------------------------------------------------
        const first = await upload(page, 1800000);
        expect(first.log, "The first upload reported a problem.").not.toContain(
            problemNotice,
        );
        expect(
            recordings.filter((file) => !first.sent.includes(file)),
            `The first upload should have sent all ${recordings.length} recordings. It sent ${first.sent.length} files.`,
        ).toEqual([]);
        expect(
            first.sent.length,
            "The first upload should have sent more than 1000 files.",
        ).toBeGreaterThan(1000);

        // ---- Add a page with a picture, and upload again -------------------------------------------
        await switchTab(page, "edit");
        const contentPagesBefore = (await getContentPages(page)).map(
            (p) => p.id,
        );
        await addPage(page, "Just an Image");
        const picturePage = (await getContentPages(page)).find(
            (p) => !contentPagesBefore.includes(p.id),
        );
        expect(
            picturePage,
            "Adding a page added no content page.",
        ).toBeTruthy();
        await goToPage(page, picturePage!.id);
        await chooseImageFile(page, PICTURE);
        const picture = (await getImagePlacement(page)).fileName;
        await goToPage(page, (await getPages(page))[0].id);

        const second = await upload(page, 900000);
        expect(second.log, "The re-upload reported a problem.").not.toContain(
            problemNotice,
        );
        expect(
            second.sent.filter((file) => file.toLowerCase().endsWith(".mp3")),
            "The re-upload should have sent none of the recordings.",
        ).toEqual([]);
        const htmFile = `${Path.basename(bookFolder)}.htm`;
        // The book's PDF, which Bloom makes afresh for each upload, holds the new page.
        const pdfFile = `${Path.basename(bookFolder)}.pdf`;
        const allowedEveryTime = [
            htmFile,
            pdfFile,
            "meta.json",
            "thumbnail.png",
            "thumbnail-70.png",
            "thumbnail-256.png",
        ];
        expect(
            second.sent.filter((file) => !allowedEveryTime.includes(file)),
            `The re-upload should have sent only the new picture (${picture}) besides ${htmFile}, ` +
                `${pdfFile}, meta.json and the thumbnails. It sent: ${second.sent.join(", ")}`,
        ).toEqual([picture]);
        expect(
            second.sent,
            `The re-upload should have sent ${htmFile}, which holds the new page.`,
        ).toContain(htmFile);

        // ---- The re-uploaded copy has every recording, which the server copied ---------------------
        const [onServer] = await findBooksUploadedBy(login, BOOK_TITLE);
        expect(onServer, `"${BOOK_TITLE}" is not on the sandbox.`).toBeTruthy();
        const latestBaseUrl = await getLatestUploadBaseUrl(onServer);
        const uploadedFiles = await listUploadedBookFiles(latestBaseUrl);
        // The uploaded book's pages are the new ones: the picture page and every recorded sentence.
        const uploadedHtml = await fetchUploadedBookHtml(latestBaseUrl);
        expect(
            uploadedHtml,
            `The re-uploaded ${htmFile} should show the new picture (${picture}).`,
        ).toContain(`src="${picture}"`);
        expect(
            recordings.filter(
                (file) =>
                    !uploadedHtml.includes(
                        `id="${file.replace(/\.mp3$/, "")}"`,
                    ),
            ),
            `The re-uploaded ${htmFile} should still hold all ${recordings.length} recorded sentences.`,
        ).toEqual([]);
        expect(
            recordings.filter(
                (file) => !uploadedFiles.includes(`audio/${file}`),
            ),
            `The re-uploaded copy should hold all ${recordings.length} recordings. It holds ${uploadedFiles.length} files.`,
        ).toEqual([]);
        expect(
            uploadedFiles,
            "The re-uploaded copy should hold the new picture.",
        ).toContain(picture);
    });
});
