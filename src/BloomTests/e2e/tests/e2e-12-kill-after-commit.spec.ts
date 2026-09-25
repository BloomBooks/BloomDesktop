// E2E-12: Bloom dies right after the server commits (CONTRACTS v1.10).
//
// A database change can succeed while its response never reaches Bloom (a crash, a dropped
// connection). These scenarios kill Bloom the moment the server has committed and check that
// the copy recovers on restart without stranding a checkout or inventing a conflict:
//   (a) a checkout: the client makes the checkout GUID and writes `<book>/.checkout` BEFORE
//       calling checkout_book, so the GUID is already on disk and the copy keeps its checkout
//       (and can check in) after the restart;
//   (b) a check-in of an existing book: on restart the book shows as checked in, with no Lost
//       and Found copy (its content IS the committed version, so the database wins quietly);
//   (c) the first check-in of a new book: on restart it shows as checked in and current, not
//       as a new, editable local book.
//
// The kill uses e2e-6/e2e-9's technique (fire the API call without awaiting, watch the DB over
// a held-open connection, process.kill at once). The kill can still land a moment after Bloom
// has processed the response; for (b) and (c) the files Bloom updates only AFTER hearing back
// (the `.checkout` record, the book's TeamCollection.status, the repo cache) are therefore put
// back as they were before the call, which recreates exactly the state of a crash that came
// before Bloom heard back.
import { test, expect } from "@playwright/test";
import * as fs from "node:fs/promises";
import * as path from "node:path";
import { createHash } from "node:crypto";
import { resetStack } from "../harness/reset";
import {
    createScratchCollection,
    ScratchCollection,
} from "../harness/collectionFixture";
import { launchBloom, LaunchedBloom } from "../harness/launch";
import { ALICE } from "../harness/localStack";
import {
    postApi,
    postCreateCloudTeamCollection,
    waitForSharingReady,
} from "../harness/bloomApi";
import { bookStatus } from "../harness/bookStatus";
import { readBookInstanceId, selectBookByName } from "../harness/selectBook";
import { duplicateBook } from "../harness/duplicateBook";
import { queryDb, openPersistentClient } from "../harness/db";

const LOG_DIR = "C:\\BloomE2E-logs\\e2e-12";
const REPO_CACHE_FILE = ".bloom-cloud-repo-cache.json";
const LOCAL_STATUS_FILE = "TeamCollection.status";

const checkoutRecordPath = (collectionFolder: string, bookName: string) =>
    path.join(collectionFolder, bookName, ".checkout");

const htmPath = (collectionFolder: string, bookName: string) =>
    path.join(collectionFolder, bookName, `${bookName}.htm`);

const exists = (p: string) =>
    fs.stat(p).then(
        () => true,
        () => false,
    );

/** The file's bytes, or null when it doesn't exist. */
const snapshot = (p: string) => fs.readFile(p).catch(() => null);

/** Puts a file back exactly as {@link snapshot} found it (deleting it if it didn't exist). */
const restore = async (p: string, content: Buffer | null) => {
    if (content === null) await fs.rm(p, { force: true });
    else await fs.writeFile(p, content);
};

const lostAndFoundFiles = async (collectionFolder: string) =>
    fs
        .readdir(path.join(collectionFolder, "Lost and Found"))
        .catch(() => [] as string[]);

/** sha256 of the lowercase GUID's UTF-8 bytes, as lowercase hex (the server's hash rule). */
const hashGuid = (guid: string) =>
    createHash("sha256").update(guid.toLowerCase(), "utf8").digest("hex");

/** Watches the DB (over a held-open connection) until `sql` returns a row matching
 * `committed`, then kills the instance at once. */
const killWhenCommitted = async (
    instance: LaunchedBloom,
    fire: () => Promise<unknown>,
    sql: string,
    params: unknown[],
    committed: (row: Record<string, unknown>) => boolean,
    what: string,
) => {
    const dbClient = await openPersistentClient();
    try {
        void fire().catch(() => undefined);
        const deadline = Date.now() + 20_000;
        let sawCommit = false;
        while (!sawCommit && Date.now() < deadline) {
            const result = await dbClient.query(sql, params);
            sawCommit = result.rows.some(committed);
            if (!sawCommit) await new Promise((r) => setTimeout(r, 2));
        }
        expect(sawCommit, `the server never committed ${what}`).toBe(true);
        process.kill(instance.processId);
    } finally {
        await dbClient.end();
    }
};

test.describe("E2E-12 kill right after the server commits", () => {
    const instances: LaunchedBloom[] = [];

    /** Launches an instance and remembers it for afterEach's cleanup. */
    const launch = async (collectionFilePath: string, label: string) => {
        const instance = await launchBloom({
            collectionFilePath,
            user: ALICE,
            label,
            logDir: LOG_DIR,
        });
        instances.push(instance);
        await waitForSharingReady(instance.httpPort, 60_000);
        return instance;
    };

    /** Forgets an instance that was killed some other way. */
    const forget = (instance: LaunchedBloom) =>
        instances.splice(instances.indexOf(instance), 1);

    /** Alice creates and shares a one-book collection; the instance stays up. */
    const share = async (
        scenario: string,
    ): Promise<{ scratch: ScratchCollection; alice: LaunchedBloom }> => {
        const scratch = await createScratchCollection(scenario, "alice");
        const alice = await launchBloom({
            collectionFilePath: scratch.collectionFilePath,
            user: ALICE,
            label: `${scenario}-alice-create`,
            logDir: LOG_DIR,
        });
        instances.push(alice);
        await alice.connect(); // connect-before-trigger (see README)
        expect((await postCreateCloudTeamCollection(alice)).status).toBe(200);
        await waitForSharingReady(alice.httpPort);
        return { scratch, alice };
    };

    const bookRow = async (collectionId: string, bookName: string) => {
        const rows = await queryDb<{
            id: string;
            locked_by: string | null;
            checkout_guid_hash: string | null;
            current_version_seq: string | null;
        }>(
            "select id, locked_by, checkout_guid_hash, current_version_seq from tc.books where collection_id = $1 and name = $2",
            [collectionId, bookName],
        );
        expect(rows, `exactly one tc.books row for ${bookName}`).toHaveLength(
            1,
        );
        return rows[0];
    };

    test.beforeEach(async () => {
        await resetStack();
    });

    test.afterEach(async () => {
        await Promise.all(
            instances.map((i) => i.kill().catch(() => undefined)),
        );
        instances.length = 0;
    });

    test("(a) a checkout's GUID is on disk before the server commits it; the copy keeps the checkout after a restart", async () => {
        const { scratch, alice } = await share("e2e-12a");
        const { bookName, collectionFolder, collectionId } = scratch;
        await selectBookByName(alice.httpPort, collectionFolder, bookName);
        const { id: bookId } = await bookRow(collectionId, bookName);

        await killWhenCommitted(
            alice,
            () =>
                postApi(
                    alice.httpPort,
                    "teamCollection/attemptLockOfCurrentBook",
                    "{}",
                ),
            "select locked_by, checkout_guid_hash from tc.books where id = $1",
            [bookId],
            (row) => row.locked_by !== null,
            "the checkout",
        );
        forget(alice);

        const row = await bookRow(collectionId, bookName);
        const record = JSON.parse(
            await fs.readFile(
                checkoutRecordPath(collectionFolder, bookName),
                "utf8",
            ),
        );
        expect(
            hashGuid(record.checkoutGuid),
            "the GUID the server locked the book under was already on disk",
        ).toBe(row.checkout_guid_hash);

        const restarted = await launch(
            scratch.collectionFilePath,
            "e2e-12a-alice-restarted",
        );
        await selectBookByName(restarted.httpPort, collectionFolder, bookName);
        const status = await bookStatus(restarted.httpPort, bookName);
        expect(status.who).toBe(ALICE.email);
        expect(
            status.checkedOutInThisCopy,
            "the copy still holds the checkout",
        ).toBe(true);

        const checkin = await postApi(
            restarted.httpPort,
            "teamCollection/checkInCurrentBook",
            "{}",
        );
        expect(checkin.status, "the recovered checkout checks in").toBe(200);
        const after = await bookRow(collectionId, bookName);
        expect(Number(after.current_version_seq)).toBe(2);
        expect(after.locked_by).toBeNull();
        expect(
            await exists(checkoutRecordPath(collectionFolder, bookName)),
        ).toBe(false);
    });

    test("(b) an existing book's check-in that committed just before a crash shows as checked in, with no Lost and Found copy", async () => {
        const { scratch, alice } = await share("e2e-12b");
        const { bookName, collectionFolder, collectionId } = scratch;
        await selectBookByName(alice.httpPort, collectionFolder, bookName);
        const locked = await (
            await postApi(
                alice.httpPort,
                "teamCollection/attemptLockOfCurrentBook",
                "{}",
            )
        ).json();
        expect(locked, "Alice's checkout").toBe(true);
        await alice.kill();
        forget(alice);

        // Edit while Bloom is down (no CDP-drivable editor; see e2e-6).
        const marker = "E2E12B-EDIT";
        const original = await fs.readFile(
            htmPath(collectionFolder, bookName),
            "utf8",
        );
        const edited = original.replace(
            "</body>",
            `<div class="bloom-editable">${marker}</div></body>`,
        );
        expect(edited, "sanity: the htm should have a </body>").not.toBe(
            original,
        );
        await fs.writeFile(htmPath(collectionFolder, bookName), edited, "utf8");

        const sending = await launch(
            scratch.collectionFilePath,
            "e2e-12b-alice-sending",
        );
        await selectBookByName(sending.httpPort, collectionFolder, bookName);
        const { id: bookId } = await bookRow(collectionId, bookName);
        const beforeCall = {
            record: await snapshot(
                checkoutRecordPath(collectionFolder, bookName),
            ),
            status: await snapshot(
                path.join(collectionFolder, bookName, LOCAL_STATUS_FILE),
            ),
            cache: await snapshot(path.join(collectionFolder, REPO_CACHE_FILE)),
        };
        expect(
            beforeCall.record,
            "sanity: checked out in this copy",
        ).not.toBeNull();

        await killWhenCommitted(
            sending,
            () =>
                postApi(
                    sending.httpPort,
                    "teamCollection/checkInCurrentBook",
                    "{}",
                ),
            "select current_version_seq from tc.books where id = $1",
            [bookId],
            (row) => Number(row.current_version_seq) === 2,
            "the check-in",
        );
        forget(sending);
        // The state of a crash before Bloom heard back (see the header comment).
        await restore(
            checkoutRecordPath(collectionFolder, bookName),
            beforeCall.record,
        );
        await restore(
            path.join(collectionFolder, bookName, LOCAL_STATUS_FILE),
            beforeCall.status,
        );
        await restore(
            path.join(collectionFolder, REPO_CACHE_FILE),
            beforeCall.cache,
        );

        const restarted = await launch(
            scratch.collectionFilePath,
            "e2e-12b-alice-restarted",
        );
        await selectBookByName(restarted.httpPort, collectionFolder, bookName);
        const status = await bookStatus(restarted.httpPort, bookName);
        expect(status.who ?? "", "shown checked in").toBe("");
        expect(status.checkedOutInThisCopy).toBe(false);
        const cloudStatus = status as unknown as {
            localVersionSeq: number | null;
            repoVersionSeq: number | null;
        };
        expect(cloudStatus.localVersionSeq).toBe(2);
        expect(cloudStatus.repoVersionSeq).toBe(2);
        expect(
            await lostAndFoundFiles(collectionFolder),
            "the local content IS the committed version: nothing to preserve",
        ).toEqual([]);
        expect(
            await exists(checkoutRecordPath(collectionFolder, bookName)),
        ).toBe(false);
        expect(
            await fs.readFile(htmPath(collectionFolder, bookName), "utf8"),
        ).toContain(marker);
        const after = await bookRow(collectionId, bookName);
        expect(Number(after.current_version_seq)).toBe(2);
        expect(after.locked_by).toBeNull();
    });

    test("(c) a new book's first check-in that committed just before a crash shows as checked in, not new", async () => {
        const { scratch, alice } = await share("e2e-12c");
        const { collectionFolder, collectionId } = scratch;
        const sourceId = await readBookInstanceId(
            collectionFolder,
            scratch.bookName,
        );
        // DuplicateBook makes a new local-only book and selects it.
        const { folderName: newBook, bookInstanceId } = await duplicateBook(
            alice.httpPort,
            collectionFolder,
            sourceId,
        );
        const newStatus = await bookStatus(alice.httpPort, newBook);
        expect(
            (newStatus as unknown as { isNewLocalBook: boolean })
                .isNewLocalBook,
            "sanity: a new local book",
        ).toBe(true);
        const beforeCall = {
            status: await snapshot(
                path.join(collectionFolder, newBook, LOCAL_STATUS_FILE),
            ),
            cache: await snapshot(path.join(collectionFolder, REPO_CACHE_FILE)),
        };

        await killWhenCommitted(
            alice,
            () =>
                postApi(
                    alice.httpPort,
                    "teamCollection/checkInCurrentBook",
                    "{}",
                ),
            "select current_version_id from tc.books where instance_id = $1",
            [bookInstanceId],
            (row) => row.current_version_id !== null,
            "the first check-in",
        );
        forget(alice);
        expect(
            await exists(checkoutRecordPath(collectionFolder, newBook)),
            "a check-in never writes a .checkout record",
        ).toBe(false);
        await restore(
            path.join(collectionFolder, newBook, LOCAL_STATUS_FILE),
            beforeCall.status,
        );
        await restore(
            path.join(collectionFolder, REPO_CACHE_FILE),
            beforeCall.cache,
        );

        const restarted = await launch(
            scratch.collectionFilePath,
            "e2e-12c-alice-restarted",
        );
        await selectBookByName(restarted.httpPort, collectionFolder, newBook);
        const status = (await bookStatus(
            restarted.httpPort,
            newBook,
        )) as unknown as {
            who: string | null;
            isNewLocalBook: boolean;
            checkedOutInThisCopy?: boolean;
            localVersionSeq: number | null;
            repoVersionSeq: number | null;
        };
        expect(status.isNewLocalBook, "no longer a new local book").toBe(false);
        expect(status.who ?? "", "shown checked in").toBe("");
        expect(status.checkedOutInThisCopy).toBe(false);
        expect(status.repoVersionSeq).toBe(1);
        expect(
            status.localVersionSeq,
            "the local copy is the committed one",
        ).toBe(1);
        expect(await lostAndFoundFiles(collectionFolder)).toEqual([]);
        const rows = await queryDb(
            "select v.seq from tc.versions v join tc.books b on b.id = v.book_id where b.instance_id = $1",
            [bookInstanceId],
        );
        expect(rows, "exactly one committed version").toHaveLength(1);
    });
});
