// E2E-6: kill mid-Send -> restart -> resume -> never a partial version (EXISTING book, v2 Send).
//
// E2E-9(b) already proved the new-book first-Send case (kill leaves no phantom; resume commits
// exactly one version). This scenario is the complementary, arguably more important one: a book
// that teammates ALREADY have at v1 must keep seeing intact v1 throughout an interrupted v2
// Send -- never a partial or mixed version -- and after Alice restarts and resumes, v2 commits
// cleanly and teammates receive exactly v2.
//
// The invariant is enforced server-side by `tc.checkin_finish_tx`
// (supabase/migrations/20260706000004): the new version row + its version_files + the book's
// current_version pointer + the events are all written in a SINGLE atomic DB transaction. A kill
// anywhere before checkin-finish leaves the book's `current_version_*` untouched (still v1) and
// only an OPEN `tc.checkin_transactions` row -- invisible to `get_changes`/`get_collection_state`,
// so no teammate can ever observe a half-written version. Resume works because
// `checkin_start_tx` finds and reuses that same open transaction for the same (book, caller).
//
// Reproducing the mid-Send window uses the E2E-9(b) technique: fire checkInCurrentBook without
// awaiting, poll the DB (held-open pg connection) for the open checkin_transactions row, then
// `process.kill` the instance immediately -- faster than alice.kill()'s subprocess path, which
// is slower than the whole small-book Send. Injecting the v2 edit is done by editing Alice's
// on-disk htm while her instance is down (there is no CDP-drivable editor -- see selectBook.ts).
import { test, expect } from "@playwright/test";
import * as fs from "node:fs/promises";
import * as path from "node:path";
import { resetStack } from "../harness/reset";
import { setUpAliceAndBobOnSharedCollection } from "../harness/twoInstanceSetup";
import { launchBloom, LaunchedBloom } from "../harness/launch";
import { ALICE } from "../harness/devStack";
import { postApi, getApi } from "../harness/bloomApi";
import { pollNowViaReceiveUpdates } from "../harness/bookStatus";
import { selectBookByName } from "../harness/selectBook";
import { queryDb, openPersistentClient } from "../harness/db";

const LOG_DIR = "C:\\BloomE2E-logs\\e2e-6";
const V2_MARKER = "ALICE-V2-EDIT-MARKER";

const waitForCloudConnectionReady = async (httpPort: number): Promise<void> => {
    await expect
        .poll(
            async () =>
                (
                    await (
                        await getApi(httpPort, "teamCollection/capabilities")
                    ).json()
                ).supportsSharingUi,
            { timeout: 20_000 },
        )
        .toBe(true);
};

const checkInWithConnectionRetry = async (
    httpPort: number,
): Promise<Response> => {
    const deadline = Date.now() + 15_000;
    let last: Response;
    do {
        last = await postApi(
            httpPort,
            "teamCollection/checkInCurrentBook",
            "{}",
        );
        if (last.status === 200) return last;
        await new Promise((r) => setTimeout(r, 500));
    } while (Date.now() < deadline);
    return last;
};

test.describe("E2E-6 kill mid-Send / resume", () => {
    let alice: LaunchedBloom | undefined;
    let bob: LaunchedBloom | undefined;

    test.beforeEach(async () => {
        await resetStack();
    });

    test.afterEach(async () => {
        await Promise.all(
            [alice, bob]
                .filter((i): i is LaunchedBloom => !!i)
                .map((i) => i.kill().catch(() => undefined)),
        );
        alice = undefined;
        bob = undefined;
    });

    test("interrupting a v2 Send never exposes a partial version; teammates keep v1 until the resumed Send commits v2", async () => {
        const shared = await setUpAliceAndBobOnSharedCollection(
            "e2e-6",
            LOG_DIR,
        );
        alice = shared.alice;
        bob = shared.bob;
        const { aliceScratch, bobCollectionFilePath } = shared;
        const bookName = aliceScratch.bookName;
        const aliceHtmPath = path.join(
            aliceScratch.collectionFolder,
            bookName,
            `${bookName}.htm`,
        );
        const bobHtmPath = path.join(
            path.dirname(bobCollectionFilePath),
            bookName,
            `${bookName}.htm`,
        );

        // The book is already shared at v1. Record its server book id + confirm seq 1.
        const bookRows = await queryDb<{
            id: string;
            current_version_seq: number;
        }>(
            "select id, current_version_seq from tc.books where collection_id = $1 and name = $2",
            [aliceScratch.collectionId, bookName],
        );
        expect(bookRows).toHaveLength(1);
        expect(Number(bookRows[0].current_version_seq)).toBe(1);
        const bookId = bookRows[0].id;

        // Bob syncs down v1 so he has a concrete baseline to compare against later.
        await pollNowViaReceiveUpdates(bob.httpPort);
        const bobV1 = await fs.readFile(bobHtmPath);
        expect(
            bobV1.includes(Buffer.from(V2_MARKER, "utf8")),
            "sanity: Bob's v1 must not already contain the v2 marker",
        ).toBe(false);

        // --- Alice checks out, then (while down) her local content is edited to v2 ---
        await selectBookByName(
            alice.httpPort,
            aliceScratch.collectionFolder,
            bookName,
        );
        const lock = await (
            await postApi(
                alice.httpPort,
                "teamCollection/attemptLockOfCurrentBook",
                "{}",
            )
        ).json();
        expect(lock).toBe(true);

        await alice.kill();
        alice = undefined;
        const originalHtm = await fs.readFile(aliceHtmPath, "utf8");
        const editedHtm = originalHtm.replace(
            "</body>",
            `<div class="bloom-editable">${V2_MARKER}</div></body>`,
        );
        expect(editedHtm).not.toBe(originalHtm);
        await fs.writeFile(aliceHtmPath, editedHtm, "utf8");

        // --- Alice reopens (still holds the checkout server-side) and starts the v2 Send ---
        alice = await launchBloom({
            collectionFilePath: aliceScratch.collectionFilePath,
            user: ALICE,
            label: "e2e-6-alice-sending",
            logDir: LOG_DIR,
        });
        await waitForCloudConnectionReady(alice.httpPort);
        await selectBookByName(
            alice.httpPort,
            aliceScratch.collectionFolder,
            bookName,
        );

        const dbClient = await openPersistentClient();
        try {
            void postApi(
                alice.httpPort,
                "teamCollection/checkInCurrentBook",
                "{}",
            ).catch(() => undefined);

            // Catch the mid-Send window: an OPEN checkin_transactions row for this book, but
            // before checkin_finish_tx has advanced the book's version.
            let caught = false;
            const deadline = Date.now() + 15_000;
            while (!caught && Date.now() < deadline) {
                const txRows = await dbClient.query(
                    "select status from tc.checkin_transactions where book_id = $1 and status = 'open'",
                    [bookId],
                );
                if (txRows.rows.length > 0) {
                    caught = true;
                    break;
                }
                await new Promise((r) => setTimeout(r, 5));
            }
            expect(
                caught,
                "never observed an open checkin transaction -- could not exercise the mid-Send window",
            ).toBe(true);
            const pid = alice.processId;
            process.kill(pid);

            // Immediately: the book's committed version must STILL be v1 (atomic finish never ran).
            const midRows = await dbClient.query(
                "select current_version_seq from tc.books where id = $1",
                [bookId],
            );
            expect(
                Number(midRows.rows[0].current_version_seq),
                "a partial/interrupted Send must not have advanced the committed version",
            ).toBe(1);
        } finally {
            await dbClient.end();
        }

        // Bob, receiving now, still gets intact v1 -- never a mix.
        await pollNowViaReceiveUpdates(bob.httpPort);
        const bobDuringInterruption = await fs.readFile(bobHtmPath);
        expect(
            bobDuringInterruption.equals(bobV1),
            "Bob's copy changed during an interrupted Send -- he must keep byte-identical v1",
        ).toBe(true);

        // --- Alice restarts and resumes the Send; v2 commits cleanly as exactly one new version ---
        alice = await launchBloom({
            collectionFilePath: aliceScratch.collectionFilePath,
            user: ALICE,
            label: "e2e-6-alice-resumed",
            logDir: LOG_DIR,
        });
        await waitForCloudConnectionReady(alice.httpPort);
        await selectBookByName(
            alice.httpPort,
            aliceScratch.collectionFolder,
            bookName,
        );
        const resumed = await checkInWithConnectionRetry(alice.httpPort);
        expect(resumed.status).toBe(200);

        // Server: exactly v2 now (seq advanced by exactly one, no gap/duplicate), lock released.
        await expect
            .poll(
                async () =>
                    Number(
                        (
                            await queryDb<{ current_version_seq: number }>(
                                "select current_version_seq from tc.books where id = $1",
                                [bookId],
                            )
                        )[0].current_version_seq,
                    ),
                {
                    timeout: 20_000,
                    message: "the resumed Send never committed v2",
                },
            )
            .toBe(2);
        const versionCount = await queryDb(
            "select seq from tc.versions where book_id = $1",
            [bookId],
        );
        expect(
            versionCount,
            "there must be exactly two versions (v1 + the resumed v2), no partial duplicate",
        ).toHaveLength(2);

        // Bob receives v2 and ends byte-identical to Alice's committed v2.
        await expect
            .poll(
                async () => {
                    await pollNowViaReceiveUpdates(bob!.httpPort);
                    return (await fs.readFile(bobHtmPath)).includes(
                        Buffer.from(V2_MARKER, "utf8"),
                    );
                },
                {
                    timeout: 20_000,
                    message: "Bob never received the resumed v2",
                },
            )
            .toBe(true);
        const [aliceV2, bobV2] = await Promise.all([
            fs.readFile(aliceHtmPath),
            fs.readFile(bobHtmPath),
        ]);
        expect(bobV2.equals(aliceV2)).toBe(true);
    });
});
