// A book that is already on Bloom Library gets a new cover picture and is uploaded again: the
// thumbnail that Bloom Library shows for it must then be of the new cover. Automates the manual test
// "Cover Change Updates Thumbnail" (Test Case ID 215).
//
// The test uploads a book with an all-blue cover picture to dev.bloomlibrary.org for real, waits for
// the sandbox's harvester to finish with it, and checks that the thumbnail Bloom Library shows is
// blue. Then it puts an all-red picture on the cover, uploads the book again, waits for the harvester
// to finish with that upload, and checks that the thumbnail is red, with no blue left. Which
// thumbnail Bloom Library shows is worked out from the book's record the way the website does (see
// thumbnailUrlBloomLibraryShows): the harvester's, once the harvester is done with the book. Waiting
// for the harvester each time makes the check the one a person makes on the website after an
// upload, and keeps the second upload from landing while the harvester is still on the first.
//
// Like the other real-upload tests, it signs in to the sandbox as the shared test account, so its
// book's title has a prefix of its own, and it deletes only books with that prefix (see
// findBooksUploadedBy). Each run's title is unique, and a run clears away only this test's books
// that are an hour old, so that two runs at once leave each other's book alone. The account's
// password comes from BLOOM_E2E_TESTER_EMAIL_BLORG_PASSWORD: without it the test skips locally and
// fails on CI.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { setCopyrightHolder } from "../helpers/copyrightAndLicense";
import { chooseImageFile } from "../helpers/images";
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
    fetchSandboxFile,
    findBooksUploadedBy,
    thumbnailUrlBloomLibraryShows,
    type IBloomLibraryLogin,
    type IBookOnServer,
} from "../helpers/bloomLibraryServer";
import {
    describeRgba,
    isRgbaClose,
    makeSolidColorPng,
    readEncodedImageColors,
    type IPicturePoint,
    type IRgba,
} from "../helpers/paintedPixels";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "upload-cover-change-thumbnail",
        languages: ["en"],
    },
});

// The book's title starts with a prefix that no other real-upload test's books start with, and
// ends with a part of this run's own.
const TITLE_PREFIX = "Cover Change Thumbnail";
const BOOK_TITLE = `${TITLE_PREFIX} ${Date.now().toString(36)}`;
// A run deletes this test's books that are at least this old, which only a crashed run leaves.
const STALE_BOOK_MINUTES = 60;
// How long the sandbox's harvester may take to finish with an upload.
const HARVEST_TIMEOUT_MS = 900000;

// The two cover pictures: one colour each, which no cover has otherwise, so that each is easy to
// find in a thumbnail and cannot be mistaken for the other.
const FIRST_COVER_COLOR: IRgba = { r: 0, g: 0, b: 255, a: 255 };
const SECOND_COVER_COLOR: IRgba = { r: 255, g: 0, b: 0, a: 255 };
// How far a thumbnail's pixel may be from a cover colour and still count as that colour: shrinking
// the picture and compressing it moves the colour a little.
const COLOR_TOLERANCE = 40;
// The points of the thumbnail the test reads: a 16 by 16 grid over the whole of it.
const GRID_SIZE = 16;
const GRID: IPicturePoint[] = Array.from(
    { length: GRID_SIZE * GRID_SIZE },
    (_, i) => ({
        name: `point ${i}`,
        x: ((i % GRID_SIZE) + 0.5) / GRID_SIZE,
        y: (Math.floor(i / GRID_SIZE) + 0.5) / GRID_SIZE,
    }),
);
// The fewest grid points that must show the cover picture's colour. The picture fills a good part
// of the cover's thumbnail; a few points rule out a stray pixel.
const MIN_POINTS_OF_COVER_COLOR = 8;

let login: IBloomLibraryLogin | undefined;

/** Put a picture of one colour on the book's cover, which is the first page. */
async function putCoverPicture(
    page: Page,
    color: IRgba,
    fileName: string,
): Promise<void> {
    const picture = test.info().outputPath(fileName);
    await makeSolidColorPng(page, picture, 400, 300, color);
    await goToPage(page, (await getPages(page))[0].id);
    await chooseImageFile(page, picture);
}

/**
 * Wait until the harvester has finished with the book's latest upload, and return the book's record
 * then. `previousHarvestStartedAt` is when the harvest of the upload before it started, so that the
 * record left "Done" by that one does not count; undefined for the first upload.
 */
async function waitForHarvest(
    previousHarvestStartedAt: string | undefined,
): Promise<IBookOnServer> {
    let record: IBookOnServer | undefined;
    let state = "no record yet";
    // A lookup that throws ends the poll at once, and its own error, with the sandbox's answer, is
    // what the test reports.
    let lookupFailed = false;
    try {
        await expect
            .poll(
                async () => {
                    let books: IBookOnServer[];
                    try {
                        books = await findBooksUploadedBy(login!, BOOK_TITLE);
                    } catch (error) {
                        lookupFailed = true;
                        throw error;
                    }
                    if (books.length !== 1) {
                        state = `${books.length} records titled "${BOOK_TITLE}"`;
                        return false;
                    }
                    record = books[0];
                    state = `harvestState ${record.harvestState}, harvestStartedAt ${record.harvestStartedAt}`;
                    return (
                        record.harvestState === "Done" &&
                        record.harvestStartedAt !== undefined &&
                        record.harvestStartedAt !== previousHarvestStartedAt
                    );
                },
                { timeout: HARVEST_TIMEOUT_MS, intervals: [10000] },
            )
            .toBe(true);
    } catch (error) {
        if (lookupFailed) throw error;
        // The poll timed out. A message given to the poll itself would be fixed before the first
        // poll; this one says what the last poll saw.
        throw new Error(
            `The sandbox's harvester never finished with the upload of "${BOOK_TITLE}"; has it stopped? Last seen: ${state}`,
        );
    }
    return record!;
}

/**
 * Check that the thumbnail Bloom Library shows for the book is of the cover picture of
 * `coverColor`, and shows nothing of `otherColor`.
 */
async function expectThumbnailShowsCover(
    page: Page,
    record: IBookOnServer,
    coverColor: IRgba,
    otherColor: IRgba,
    when: string,
): Promise<void> {
    const url = thumbnailUrlBloomLibraryShows(record);
    const colors = await readEncodedImageColors(
        page,
        await fetchSandboxFile(url),
        GRID,
    );
    const count = (color: IRgba) =>
        colors.filter((c) => isRgbaClose(c, color, COLOR_TOLERANCE)).length;
    expect(
        count(otherColor),
        `${when}, Bloom Library's thumbnail (${url}) should show nothing of the other cover ` +
            `picture, ${describeRgba(otherColor)}, at any of the ${GRID.length} points read.`,
    ).toBe(0);
    expect(
        count(coverColor),
        `${when}, Bloom Library's thumbnail (${url}) should show the cover picture, ` +
            `${describeRgba(coverColor)}, at ${MIN_POINTS_OF_COVER_COLOR} or more of the ` +
            `${GRID.length} points read.`,
    ).toBeGreaterThanOrEqual(MIN_POINTS_OF_COVER_COLOR);
}

test.describe("changing the cover of a book on dev.bloomlibrary.org", () => {
    test.afterAll(async () => {
        if (login) await deleteBooksUploadedBy(login, BOOK_TITLE);
    });

    test("uploading a new cover picture updates the thumbnail Bloom Library shows [Test Case ID 215]", async ({
        page,
    }) => {
        skipIfNoLibraryPassword();
        // Two real uploads, each with a harvest to wait for.
        test.setTimeout(2 * HARVEST_TIMEOUT_MS + 600000);

        login = await signBloomIntoLibraryForReal(page);
        // Clear away what crashed earlier runs left, leaving any run going on now alone.
        await deleteBooksUploadedBy(login, TITLE_PREFIX, STALE_BOOK_MINUTES);

        // ---- A book with a blue cover, uploaded --------------------------------------------------
        await makeBookFromTemplate(page, "Basic Book");
        await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
        await putCoverPicture(page, FIRST_COVER_COLOR, "first-cover.png");
        // Leaving Edit for Publish saves the book.
        await openPublishToWeb(page);
        await clickToFixMissingItem(page, "Copyright");
        await setCopyrightHolder(page, "Bloom Automated Test");
        await acceptAllAgreements(page);
        await uploadBookForReal(page);
        const firstHarvest = await waitForHarvest(undefined);
        await expectThumbnailShowsCover(
            page,
            firstHarvest,
            FIRST_COVER_COLOR,
            SECOND_COVER_COLOR,
            "After the first upload",
        );

        // ---- The cover changed to red, uploaded again ----------------------------------------------
        await switchTab(page, "edit");
        await putCoverPicture(page, SECOND_COVER_COLOR, "second-cover.png");
        await openPublishToWeb(page);
        await acceptAllAgreements(page);
        await uploadBookForReal(page);
        const secondHarvest = await waitForHarvest(
            firstHarvest.harvestStartedAt,
        );
        expect(
            secondHarvest.objectId,
            "Uploading the book again should update its record on Bloom Library, not replace it with a new one.",
        ).toBe(firstHarvest.objectId);
        await expectThumbnailShowsCover(
            page,
            secondHarvest,
            SECOND_COVER_COLOR,
            FIRST_COVER_COLOR,
            "After the cover changed and the book was uploaded again",
        );
    });
});
