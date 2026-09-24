// E2E-11: the checkout GUID (CONTRACTS v1.9) across moved and duplicated collection folders.
//
// A cloud checkout belongs to the copy of the collection whose book folder holds the server-
// issued checkout GUID in `<book>/.checkout`; the server keeps only its hash
// (tc.books.checkout_guid_hash). These scenarios pin the behaviors that replaced the old
// path-hash "seat", which broke whenever a collection folder moved or was copied:
//   (a) a checked-out collection folder can be moved (or renamed) and still checks in;
//   (b) of two duplicated folders, the one that checks in wins; the other's checkout is
//       cancelled when it next opens, with its edits saved to Lost and Found;
//   (c) a copy without the current GUID cannot check in or re-take the checkout, and the
//       checkin-start edge function refuses a stale or missing GUID with CheckoutElsewhere;
//   (d) an administrator's force-unlock (which needs no GUID) makes the holder's copy
//       read-only at its next poll and cancels its checkout (edits to Lost and Found) at its
//       next open.
//
// Edits are made to a book's htm on disk while its Bloom is down (there is no CDP-drivable
// editor; see e2e-6), so each copy's local content provably differs from the repo's.
import { test, expect } from "@playwright/test";
import * as fs from "node:fs/promises";
import * as path from "node:path";
import { randomUUID } from "node:crypto";
import { resetStack } from "../harness/reset";
import {
    createScratchCollection,
    ScratchCollection,
} from "../harness/collectionFixture";
import { launchBloom, LaunchedBloom } from "../harness/launch";
import {
    ALICE,
    BOB,
    ANON_KEY,
    SUPABASE_URL,
    LocalStackUser,
} from "../harness/localStack";
import {
    postApi,
    postCreateCloudTeamCollection,
    waitForSharingReady,
} from "../harness/bloomApi";
import { bookStatus, pollNowViaReceiveUpdates } from "../harness/bookStatus";
import { selectBookByName } from "../harness/selectBook";
import { queryDb } from "../harness/db";
import { setUpAliceAndBobOnSharedCollection } from "../harness/twoInstanceSetup";

const LOG_DIR = "C:\\BloomE2E-logs\\e2e-11";
const WORK_PRESERVED_LOCALLY = 100; // tc.events.type, see BookHistoryEventType

const checkoutRecordPath = (collectionFolder: string, bookName: string) =>
    path.join(collectionFolder, bookName, ".checkout");

const htmPath = (collectionFolder: string, bookName: string) =>
    path.join(collectionFolder, bookName, `${bookName}.htm`);

const exists = (p: string) =>
    fs.stat(p).then(
        () => true,
        () => false,
    );

/** Appends a marker element to a book's htm (the instance using it must be down). */
const addMarker = async (
    collectionFolder: string,
    bookName: string,
    marker: string,
) => {
    const file = htmPath(collectionFolder, bookName);
    const original = await fs.readFile(file, "utf8");
    const edited = original.replace(
        "</body>",
        `<div class="bloom-editable">${marker}</div></body>`,
    );
    expect(edited, "sanity: the htm should have a </body> to edit").not.toBe(
        original,
    );
    await fs.writeFile(file, edited, "utf8");
};

const htmContains = async (
    collectionFolder: string,
    bookName: string,
    marker: string,
) =>
    (await fs.readFile(htmPath(collectionFolder, bookName), "utf8")).includes(
        marker,
    );

const lostAndFoundFiles = async (collectionFolder: string) =>
    fs
        .readdir(path.join(collectionFolder, "Lost and Found"))
        .catch(() => [] as string[]);

type BookRow = {
    id: string;
    locked_by: string | null;
    checkout_guid_hash: string | null;
    current_version_seq: string | null;
    current_version_id: string | null;
    book_instance_id: string;
};

const bookRow = async (collectionId: string, bookName: string) => {
    const rows = await queryDb<BookRow>(
        "select id, locked_by, checkout_guid_hash, current_version_seq, current_version_id, book_instance_id from tc.books where collection_id = $1 and name = $2",
        [collectionId, bookName],
    );
    expect(rows, `exactly one tc.books row for ${bookName}`).toHaveLength(1);
    return rows[0];
};

const checkOutCurrentBook = async (httpPort: number) =>
    (await (
        await postApi(httpPort, "teamCollection/attemptLockOfCurrentBook", "{}")
    ).json()) as boolean;

test.describe("E2E-11 checkout GUID across moved and duplicated collections", () => {
    const instances: LaunchedBloom[] = [];

    /** Launches an instance and remembers it for afterEach's cleanup. */
    const launch = async (
        collectionFilePath: string,
        label: string,
        user: LocalStackUser = ALICE,
    ) => {
        const instance = await launchBloom({
            collectionFilePath,
            user,
            label,
            logDir: LOG_DIR,
        });
        instances.push(instance);
        await waitForSharingReady(instance.httpPort, 60_000);
        return instance;
    };

    const kill = async (instance: LaunchedBloom) => {
        await instance.kill();
        instances.splice(instances.indexOf(instance), 1);
    };

    /** Alice creates and shares a one-book collection, checks the book out, and quits. */
    const shareAndCheckOut = async (
        scenario: string,
    ): Promise<ScratchCollection> => {
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
        await selectBookByName(
            alice.httpPort,
            scratch.collectionFolder,
            scratch.bookName,
        );
        expect(
            await checkOutCurrentBook(alice.httpPort),
            "Alice's checkout",
        ).toBe(true);
        const record = JSON.parse(
            await fs.readFile(
                checkoutRecordPath(scratch.collectionFolder, scratch.bookName),
                "utf8",
            ),
        );
        expect(
            record.checkoutGuid,
            "the checkout wrote a GUID record",
        ).toBeTruthy();
        const row = await bookRow(scratch.collectionId, scratch.bookName);
        expect(
            row.checkout_guid_hash,
            "the server keeps the GUID's hash",
        ).toBeTruthy();
        expect(Number(row.current_version_seq)).toBe(1);
        await kill(alice);
        return scratch;
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

    test("(a) a checked-out collection folder that was moved still edits and checks in", async () => {
        const scratch = await shareAndCheckOut("e2e-11a");
        const { bookName } = scratch;

        // Move the whole collection folder somewhere else (a new parent AND a new name).
        const movedFolder = path.join(
            path.dirname(path.dirname(scratch.collectionFolder)),
            "moved",
            `${path.basename(scratch.collectionFolder)}-renamed`,
        );
        await fs.mkdir(path.dirname(movedFolder), { recursive: true });
        await fs.rename(scratch.collectionFolder, movedFolder);
        const movedCollectionFile = path.join(
            movedFolder,
            path.basename(scratch.collectionFilePath),
        );
        await addMarker(movedFolder, bookName, "E2E11A-MOVED-EDIT");

        const alice = await launch(movedCollectionFile, "e2e-11a-alice-moved");
        await selectBookByName(alice.httpPort, movedFolder, bookName);
        const status = await bookStatus(alice.httpPort, bookName);
        expect(status.who).toBe(ALICE.email);
        expect(
            status.checkedOutInThisCopy,
            "the moved copy still holds the checkout",
        ).toBe(true);

        const checkin = await postApi(
            alice.httpPort,
            "teamCollection/checkInCurrentBook",
            "{}",
        );
        expect(checkin.status, "check-in from the moved folder").toBe(200);

        const row = await bookRow(scratch.collectionId, bookName);
        expect(Number(row.current_version_seq)).toBe(2);
        expect(row.locked_by).toBeNull();
        expect(row.checkout_guid_hash).toBeNull();
        expect(await exists(checkoutRecordPath(movedFolder, bookName))).toBe(
            false,
        );
        expect(
            await htmContains(movedFolder, bookName, "E2E11A-MOVED-EDIT"),
        ).toBe(true);
        expect(await lostAndFoundFiles(movedFolder)).toEqual([]);
    });

    test("(b) of two duplicated folders, the one that checks in wins; the other's edits go to Lost and Found", async () => {
        const scratch = await shareAndCheckOut("e2e-11b");
        const { bookName } = scratch;
        const copyA = scratch.collectionFolder;
        const copyB = path.join(
            path.dirname(path.dirname(copyA)),
            "copyB",
            path.basename(copyA),
        );
        await fs.cp(copyA, copyB, { recursive: true });
        expect(
            await exists(checkoutRecordPath(copyB, bookName)),
            "sanity: the duplicate carries the same checkout record",
        ).toBe(true);
        await addMarker(copyA, bookName, "E2E11B-EDIT-IN-A");
        await addMarker(copyB, bookName, "E2E11B-EDIT-IN-B");

        // Copy A checks in.
        const a = await launch(scratch.collectionFilePath, "e2e-11b-alice-A");
        await selectBookByName(a.httpPort, copyA, bookName);
        expect(
            (
                await postApi(
                    a.httpPort,
                    "teamCollection/checkInCurrentBook",
                    "{}",
                )
            ).status,
            "check-in from copy A",
        ).toBe(200);
        await kill(a);
        const afterA = await bookRow(scratch.collectionId, bookName);
        expect(Number(afterA.current_version_seq)).toBe(2);
        expect(afterA.locked_by).toBeNull();

        // Copy B opens: its checkout is obsolete, so it is cancelled at open.
        const b = await launch(
            path.join(copyB, path.basename(scratch.collectionFilePath)),
            "e2e-11b-alice-B",
        );
        await expect
            .poll(() => exists(checkoutRecordPath(copyB, bookName)), {
                timeout: 30_000,
                message: "copy B's obsolete .checkout was never removed",
            })
            .toBe(false);
        const preserved = await lostAndFoundFiles(copyB);
        expect(
            preserved,
            "copy B's edits were saved to Lost and Found",
        ).toEqual([`${bookName}.bloomSource`]);
        await expect
            .poll(() => htmContains(copyB, bookName, "E2E11B-EDIT-IN-A"), {
                timeout: 30_000,
                message: "copy B never received copy A's check-in",
            })
            .toBe(true);
        expect(await htmContains(copyB, bookName, "E2E11B-EDIT-IN-B")).toBe(
            false,
        );
        const status = await bookStatus(b.httpPort, bookName);
        expect(status.who).toBeFalsy();
        expect(status.checkedOutInThisCopy).toBe(false);

        const incidents = await queryDb<{ message: string; by_email: string }>(
            "select message, by_email from tc.events where collection_id = $1 and type = $2",
            [scratch.collectionId, WORK_PRESERVED_LOCALLY],
        );
        expect(incidents).toEqual([
            { message: "ObsoleteCheckout", by_email: ALICE.email },
        ]);
    });

    test("(c) a copy without the current GUID cannot check in or re-take the checkout", async () => {
        const scratch = await shareAndCheckOut("e2e-11c");
        const { bookName, collectionId } = scratch;
        const copyA = scratch.collectionFolder;
        const copyB = path.join(
            path.dirname(path.dirname(copyA)),
            "copyB",
            path.basename(copyA),
        );
        await fs.cp(copyA, copyB, { recursive: true });
        await fs.rm(checkoutRecordPath(copyB, bookName));
        await addMarker(copyB, bookName, "E2E11C-EDIT-IN-B");
        const before = await bookRow(collectionId, bookName);

        // --- Through Bloom: copy B (no record) is read-only and cannot take over ---
        const b = await launch(
            path.join(copyB, path.basename(scratch.collectionFilePath)),
            "e2e-11c-alice-B",
        );
        await selectBookByName(b.httpPort, copyB, bookName);
        const status = await bookStatus(b.httpPort, bookName);
        expect(status.who).toBe(ALICE.email);
        expect(status.checkedOutInThisCopy).toBe(false);
        expect(
            await checkOutCurrentBook(b.httpPort),
            "copy B must not be able to check out a book this account holds elsewhere",
        ).toBe(false);
        await postApi(b.httpPort, "teamCollection/checkInCurrentBook", "{}");
        const afterB = await bookRow(collectionId, bookName);
        expect(
            afterB,
            "copy B's attempts changed nothing on the server (no new version, no GUID rotation)",
        ).toEqual(before);
        await kill(b);

        // --- At the edge function: a stale or missing GUID is refused ---
        const token = await signIn(ALICE);
        const [{ v: clientVersion }] = await queryDb<{ v: string }>(
            "select tc.min_supported_client_version() as v",
        );
        const checkinStart = (checkoutGuid?: string) =>
            fetch(`${SUPABASE_URL}/functions/v1/checkin-start`, {
                method: "POST",
                headers: {
                    apikey: ANON_KEY,
                    authorization: `Bearer ${token}`,
                    "content-type": "application/json",
                },
                body: JSON.stringify({
                    collectionId,
                    bookId: before.id,
                    bookInstanceId: before.book_instance_id,
                    proposedName: bookName,
                    baseVersionId: before.current_version_id,
                    checksum: "e2e-11c",
                    clientVersion,
                    files: [
                        {
                            path: `${bookName}.htm`,
                            sha256: "0".repeat(64),
                            size: 1,
                        },
                    ],
                    ...(checkoutGuid ? { checkoutGuid } : {}),
                }),
            });
        for (const [label, guid] of [
            ["stale", randomUUID()],
            ["missing", undefined],
        ] as const) {
            const response = await checkinStart(guid);
            const body = await response.text();
            expect(response.status, `${label} GUID: ${body}`).toBe(409);
            expect(JSON.parse(body).error, `${label} GUID`).toBe(
                "CheckoutElsewhere",
            );
        }
        expect(await bookRow(collectionId, bookName)).toEqual(before);

        // --- Copy A, which has the GUID, is unaffected and checks in ---
        const a = await launch(scratch.collectionFilePath, "e2e-11c-alice-A");
        await selectBookByName(a.httpPort, copyA, bookName);
        expect(
            (
                await postApi(
                    a.httpPort,
                    "teamCollection/checkInCurrentBook",
                    "{}",
                )
            ).status,
            "check-in from copy A",
        ).toBe(200);
        const afterA = await bookRow(collectionId, bookName);
        expect(Number(afterA.current_version_seq)).toBe(2);
        expect(afterA.locked_by).toBeNull();
    });

    test("(d) an admin force-unlock cancels the holder's checkout: read-only at the next poll, Lost and Found at the next open", async () => {
        const shared = await setUpAliceAndBobOnSharedCollection(
            "e2e-11d",
            LOG_DIR,
        );
        instances.push(shared.alice, shared.bob);
        const { aliceScratch, bobCollectionFilePath } = shared;
        const bookName = aliceScratch.bookName;
        const bobFolder = path.dirname(bobCollectionFilePath);

        // Bob checks the book out, quits, edits it, and reopens (still holding the checkout).
        await selectBookByName(shared.bob.httpPort, bobFolder, bookName);
        expect(
            await checkOutCurrentBook(shared.bob.httpPort),
            "Bob's checkout",
        ).toBe(true);
        await kill(shared.bob);
        await addMarker(bobFolder, bookName, "E2E11D-BOB-EDIT");
        let bob = await launch(
            bobCollectionFilePath,
            "e2e-11d-bob-editing",
            BOB,
        );
        await selectBookByName(bob.httpPort, bobFolder, bookName);
        expect(
            (await bookStatus(bob.httpPort, bookName)).checkedOutInThisCopy,
            "sanity: Bob's copy holds the checkout",
        ).toBe(true);

        // Alice (the collection's admin) force-unlocks it; she has no GUID and needs none.
        await selectBookByName(
            shared.alice.httpPort,
            aliceScratch.collectionFolder,
            bookName,
        );
        await expect
            .poll(
                async () => {
                    await pollNowViaReceiveUpdates(shared.alice.httpPort);
                    return (await bookStatus(shared.alice.httpPort, bookName))
                        .who;
                },
                { timeout: 60_000, message: "Alice never saw Bob's checkout" },
            )
            .toBe(BOB.email);
        expect(
            (
                await postApi(
                    shared.alice.httpPort,
                    "teamCollection/forceUnlock",
                    "{}",
                )
            ).status,
        ).toBe(200);
        const unlocked = await bookRow(aliceScratch.collectionId, bookName);
        expect(unlocked.locked_by).toBeNull();
        expect(unlocked.checkout_guid_hash).toBeNull();

        // Bob's next poll: the book is no longer checked out in his copy (read-only there).
        await expect
            .poll(
                async () => {
                    await pollNowViaReceiveUpdates(bob.httpPort);
                    const s = await bookStatus(bob.httpPort, bookName);
                    return `${s.who ?? ""}|${s.checkedOutInThisCopy}`;
                },
                {
                    timeout: 60_000,
                    message: "Bob's copy never noticed the force-unlock",
                },
            )
            .toBe("|false");

        // Bob's next open: the checkout is cancelled, his edits preserved in Lost and Found,
        // and his copy has the repo version again.
        await kill(bob);
        bob = await launch(bobCollectionFilePath, "e2e-11d-bob-reopened", BOB);
        await expect
            .poll(() => exists(checkoutRecordPath(bobFolder, bookName)), {
                timeout: 30_000,
                message: "Bob's obsolete .checkout was never removed",
            })
            .toBe(false);
        expect(await lostAndFoundFiles(bobFolder)).toEqual([
            `${bookName}.bloomSource`,
        ]);
        await expect
            .poll(() => htmContains(bobFolder, bookName, "E2E11D-BOB-EDIT"), {
                timeout: 30_000,
                message: "Bob's copy never went back to the repo version",
            })
            .toBe(false);
        const incidents = await queryDb<{ message: string; by_email: string }>(
            "select message, by_email from tc.events where collection_id = $1 and type = $2",
            [aliceScratch.collectionId, WORK_PRESERVED_LOCALLY],
        );
        expect(incidents).toEqual([
            { message: "ObsoleteCheckout", by_email: BOB.email },
        ]);
        const row = await bookRow(aliceScratch.collectionId, bookName);
        expect(Number(row.current_version_seq), "nothing was checked in").toBe(
            1,
        );
    });
});

/** Signs `user` in to the local GoTrue and returns the access token. */
const signIn = async (user: LocalStackUser): Promise<string> => {
    const response = await fetch(
        `${SUPABASE_URL}/auth/v1/token?grant_type=password`,
        {
            method: "POST",
            headers: { apikey: ANON_KEY, "content-type": "application/json" },
            body: JSON.stringify({
                email: user.email,
                password: user.password,
            }),
        },
    );
    expect(response.status, "local sign-in").toBe(200);
    return ((await response.json()) as { access_token: string }).access_token;
};
