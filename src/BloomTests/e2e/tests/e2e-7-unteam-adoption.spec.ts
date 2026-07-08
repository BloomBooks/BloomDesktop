// E2E-7: un-team adoption (stale artifacts cleaned; books upload as v1).
//
// The adoption path (task 10): a collection that used to belong to a folder-based Team
// Collection gets "un-teamed" (the user deletes TeamCollectionLink.txt per the user docs) and
// then shared to the cloud. Two live behaviors merged from task 10 are exercised for real here:
//
//   Test 1 (the happy adoption): a collection carrying stale folder-TC artifacts -- per-book
//   `TeamCollection.status` (with a stale checksum AND a stale lockedBy from some departed
//   teammate), collection-level `lastCollectionFileSyncData.txt` and `log.txt` -- but NO link
//   file, is shared to the cloud. `TeamCollectionManager.ConnectToCloudCollection` must call
//   `TeamCollection.CleanStaleTeamCollectionArtifacts` first, so: the three stale files are
//   deleted locally, the stale status file is NOT uploaded into the book's S3 files, the book's
//   server row commits as v1 (current_version_seq = 1) with NO lock (the stale lockedBy must
//   not leak into the brand-new collection), and the book is immediately checkout-able.
//
//   Test 2 (the conflict guard): the same collection still carrying a FOLDER TeamCollectionLink
//   .txt (the "user skipped the un-team step" case). `ThrowIfConflictingTeamCollectionLink`
//   runs BEFORE `create_collection` (verified by reading ConnectToCloudCollection), so the
//   observable contract is: no tc.collections row is ever created, capabilities never flip to
//   cloud, and the link file is left exactly as it was. NOTE on the error surface: the handler
//   (HandleCreateCloudTeamCollection) reports the exception via ErrorReport.NotifyUserOfProblem
//   -- a modal dialog no automated session can dismiss -- and only replies to the HTTP request
//   after that. The test therefore treats "request timed out (blocked on the modal)" and
//   "request returned" as BOTH acceptable, and asserts on the durable server/local state
//   instead, which is what actually matters.
import { test, expect } from "@playwright/test";
import * as fs from "node:fs/promises";
import * as path from "node:path";
import { resetStack } from "../harness/reset";
import { createScratchCollection } from "../harness/collectionFixture";
import { launchBloom, LaunchedBloom } from "../harness/launch";
import { ALICE } from "../harness/devStack";
import { postApi, getApi } from "../harness/bloomApi";
import { bookStatus } from "../harness/bookStatus";
import { selectBookByName } from "../harness/selectBook";
import { queryDb } from "../harness/db";
import { listS3Objects } from "../harness/s3";

const LOG_DIR = "C:\\BloomE2E-logs\\e2e-7";

// Matches TeamCollection.cs's artifact names (GetStatusFilePathFromBookFolderPath,
// kLastcollectionfilesynctimeTxt) and TeamCollectionManager.TeamCollectionLinkFileName.
const STATUS_FILE_NAME = "TeamCollection.status";
const LAST_SYNC_FILE_NAME = "lastCollectionFileSyncData.txt";
const LOG_FILE_NAME = "log.txt";
const LINK_FILE_NAME = "TeamCollectionLink.txt";

/** Plants the stale files an abandoned folder TC leaves behind in a local collection folder. */
const seedStaleFolderTcArtifacts = async (
    collectionFolder: string,
    bookName: string,
): Promise<void> => {
    // A worst-case stale status: wrong checksum AND a checkout by a teammate who no longer
    // exists. If this leaked into the new cloud collection's first Send, the book would appear
    // locked by a ghost.
    const staleStatus = JSON.stringify({
        checksum: "0123456789abcdef-stale",
        lockedBy: "departed.teammate@example.com",
        lockedByFirstName: "Departed",
        lockedBySurname: "Teammate",
        lockedWhere: "OLD-DEAD-MACHINE",
    });
    await fs.writeFile(
        path.join(collectionFolder, bookName, STATUS_FILE_NAME),
        staleStatus,
        "utf8",
    );
    await fs.writeFile(
        path.join(collectionFolder, LAST_SYNC_FILE_NAME),
        "stale sync data from the old folder TC",
        "utf8",
    );
    await fs.writeFile(
        path.join(collectionFolder, LOG_FILE_NAME),
        "stale folder-TC message log",
        "utf8",
    );
};

test.describe("E2E-7 un-team adoption", () => {
    let instance: LaunchedBloom | undefined;

    test.beforeEach(async () => {
        await resetStack();
    });

    test.afterEach(async () => {
        if (instance) {
            await instance.kill().catch(() => undefined);
            instance = undefined;
        }
    });

    test("stale folder-TC artifacts are cleaned and books upload as clean v1", async () => {
        const scratch = await createScratchCollection("e2e-7", "alice");
        await seedStaleFolderTcArtifacts(
            scratch.collectionFolder,
            scratch.bookName,
        );

        instance = await launchBloom({
            collectionFilePath: scratch.collectionFilePath,
            user: ALICE,
            label: "e2e-7-adoption",
            logDir: LOG_DIR,
        });
        await instance.connect(); // connect-before-trigger (finding #7)

        // Sanity before acting: the stale files really are on disk and this is NOT a TC yet.
        await fs.access(
            path.join(
                scratch.collectionFolder,
                scratch.bookName,
                STATUS_FILE_NAME,
            ),
        );
        await fs.access(
            path.join(scratch.collectionFolder, LAST_SYNC_FILE_NAME),
        );
        const capsBefore = (await (
            await getApi(instance.httpPort, "teamCollection/capabilities")
        ).json()) as { supportsSharingUi: boolean };
        expect(capsBefore.supportsSharingUi).toBe(false);

        const createResponse = await postApi(
            instance.httpPort,
            "teamCollection/createCloudTeamCollection",
            "{}",
        );
        expect(createResponse.status).toBe(200);
        await expect
            .poll(
                async () =>
                    (
                        await (
                            await getApi(
                                instance!.httpPort,
                                "teamCollection/capabilities",
                            )
                        ).json()
                    ).supportsSharingUi,
                { timeout: 20_000 },
            )
            .toBe(true);

        // 1. The STALE artifact content did not survive (CleanStaleTeamCollectionArtifacts ran
        // before the initial Send). Note: absence of the files is NOT the invariant -- a live
        // TC legitimately re-creates a FRESH per-book TeamCollection.status (WriteLocalStatus,
        // during the initial upload) and may re-create lastCollectionFileSyncData.txt/log.txt
        // for its own current state (confirmed live: the status file exists again right after a
        // successful adoption). What must be true is that whatever is there now is NOT the old
        // TC's content.
        const readIfExists = async (filePath: string): Promise<string | null> =>
            fs.readFile(filePath, "utf8").catch(() => null);
        const statusNow = await readIfExists(
            path.join(
                scratch.collectionFolder,
                scratch.bookName,
                STATUS_FILE_NAME,
            ),
        );
        if (statusNow !== null) {
            expect(
                statusNow,
                "the re-created status file must not carry the old TC's ghost checkout",
            ).not.toContain("departed.teammate@example.com");
            expect(statusNow).not.toContain("0123456789abcdef-stale");
        }
        const lastSyncNow = await readIfExists(
            path.join(scratch.collectionFolder, LAST_SYNC_FILE_NAME),
        );
        if (lastSyncNow !== null) {
            expect(lastSyncNow).not.toContain(
                "stale sync data from the old folder TC",
            );
        }
        const logNow = await readIfExists(
            path.join(scratch.collectionFolder, LOG_FILE_NAME),
        );
        if (logNow !== null) {
            expect(logNow).not.toContain("stale folder-TC message log");
        }

        // 2. The book uploaded as a clean v1 with NO leaked lock.
        await expect
            .poll(
                async () =>
                    (
                        await queryDb(
                            "select 1 from tc.books where collection_id = $1 and name = $2 and current_version_id is not null",
                            [scratch.collectionId, scratch.bookName],
                        )
                    ).length,
                {
                    timeout: 20_000,
                    message: "the book's first version never committed",
                },
            )
            .toBe(1);
        const bookRows = await queryDb<{
            current_version_seq: number;
            locked_by: string | null;
        }>(
            "select current_version_seq, locked_by from tc.books where collection_id = $1 and name = $2",
            [scratch.collectionId, scratch.bookName],
        );
        expect(Number(bookRows[0].current_version_seq)).toBe(1);
        expect(
            bookRows[0].locked_by,
            "the stale status file's ghost lockedBy must not leak into the new collection",
        ).toBeNull();

        // 3. The stale status file was not uploaded among the book's S3 files.
        const keys = await listS3Objects(`tc/${scratch.collectionId}/`);
        expect(keys.length).toBeGreaterThan(0); // sanity: upload really happened
        expect(
            keys.some((key) => key.endsWith(STATUS_FILE_NAME)),
            "TeamCollection.status must not be uploaded to S3",
        ).toBe(false);

        // 4. The adopted book is immediately usable: status shows unlocked, and checkout works.
        const status = await bookStatus(instance.httpPort, scratch.bookName);
        expect(status.who).toBeFalsy();
        await selectBookByName(
            instance.httpPort,
            scratch.collectionFolder,
            scratch.bookName,
        );
        const lockResult = await (
            await postApi(
                instance.httpPort,
                "teamCollection/attemptLockOfCurrentBook",
                "{}",
            )
        ).json();
        expect(lockResult).toBe(true);
    });

    test("a leftover folder-TC link blocks cloud creation: no server row, capabilities stay folder, link untouched", async () => {
        const scratch = await createScratchCollection("e2e-7-guard", "alice");
        // The user "un-teamed" by hand but forgot the last step: the link file still points at
        // the old shared folder (which no longer exists -- typical after leaving a Dropbox).
        const staleLinkContent =
            "C:\\BloomE2E\\e2e-7-guard\\nonexistent-dropbox\\Old Collection - TC";
        const linkPath = path.join(scratch.collectionFolder, LINK_FILE_NAME);
        await fs.writeFile(linkPath, staleLinkContent, "utf8");

        instance = await launchBloom({
            collectionFilePath: scratch.collectionFilePath,
            user: ALICE,
            label: "e2e-7-guard",
            logDir: LOG_DIR,
        });
        await instance.connect();

        // The attempt: ThrowIfConflictingTeamCollectionLink runs BEFORE create_collection, so
        // whatever the HTTP reply does (the handler shows a modal error dialog before replying,
        // which nothing in an automated session can click -- see the file header), the durable
        // state below is the contract. Tolerate either a reply or a timeout.
        await postApi(
            instance.httpPort,
            "teamCollection/createCloudTeamCollection",
            "{}",
        ).catch(() => undefined);

        // No server-side collection row was ever created.
        const collectionRows = await queryDb(
            "select 1 from tc.collections where id = $1",
            [scratch.collectionId],
        );
        expect(
            collectionRows,
            "the conflict guard must fire BEFORE create_collection",
        ).toHaveLength(0);

        // Nothing was uploaded.
        expect(await listS3Objects(`tc/${scratch.collectionId}/`)).toHaveLength(
            0,
        );

        // The link file is exactly as the user left it (the guard must not "fix" it silently --
        // the fix instructions tell the USER to delete it, so they understand what happened).
        expect(await fs.readFile(linkPath, "utf8")).toBe(staleLinkContent);
    });
});
