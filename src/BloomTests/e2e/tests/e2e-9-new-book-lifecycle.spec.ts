// E2E-9: new-book lifecycle.
//   (a) a brand-new book is invisible to teammates until its first Send, then appears;
//   (b) killing Bloom mid first-Send leaves no phantom book visible to anyone, and resuming
//       the same Send afterward completes cleanly to exactly one committed version;
//   (c) two members independently creating a book with the same display name both end up
//       shared, under distinct server-side names (CloudTeamCollection.PutBookInRepo's
//       NameConflict retry loop, driven by tc.checkin_start_tx's per-collection name
//       uniqueness check).
//
// (a)/(b) rely on a real, product-level invariant confirmed by reading the server RPCs (not
// guessed): `tc.checkin_start_tx` (supabase/migrations/20260706000004_tc_checkin_txn_functions.sql)
// inserts the book's `tc.books` row (current_version_id NULL) WITHOUT inserting any `tc.events`
// row; only `tc.checkin_finish_tx` inserts the event that makes the book visible via
// `tc.get_changes`'s delta query (which joins on `tc.events`). A book stuck between start and
// finish is therefore invisible to every other member by construction, independent of any
// server-side reaping (`tc.reap_expired_checkin_transactions`, which only runs on a 48h expiry
// and is not exercised here). What (b) actually needs to prove empirically is the CLIENT side:
// that a hard-killed Bloom, on restart, can resume and finish that same transaction (via the
// SAME bookInstanceId -- checkin_start_tx's own "resume our own never-finished row" branch) and
// end up with exactly one committed version, never two rows / a duplicate / a lost book.
//
// (c) cannot use `CollectionModel.DuplicateBook` for the two "same name" books:
// `BookStorage.Duplicate` deliberately GUID-suffixes every duplicate's folder name specifically
// so two Team Collection members' independent duplicates never collide locally (see its own
// comment) -- which means it can never produce the identical *proposed* server name we need to
// race. `harness/collectionFixture.ts`'s `seedAdditionalBookIntoCollection` seeds two unrelated
// books (distinct bookInstanceId) with the identical display name directly on each side's local
// folder instead.
import { test, expect } from "@playwright/test";
import * as path from "node:path";
import { resetStack } from "../harness/reset";
import { setUpAliceAndBobOnSharedCollection } from "../harness/twoInstanceSetup";
import { seedAdditionalBookIntoCollection } from "../harness/collectionFixture";
import { launchBloom, LaunchedBloom } from "../harness/launch";
import { ALICE, BOB } from "../harness/devStack";
import { postApi, getApi } from "../harness/bloomApi";
import { pollNowViaReceiveUpdates } from "../harness/bookStatus";
import { readBookInstanceId, selectBookByName } from "../harness/selectBook";
import { duplicateBook, listBookFolders } from "../harness/duplicateBook";
import { queryDb, openPersistentClient } from "../harness/db";

const LOG_DIR = "C:\\BloomE2E-logs\\e2e-9";

// `teamCollection/checkInCurrentBook` (and other UI-thread endpoints gated on
// `_tcManager.CheckConnection()`) 503 with an EMPTY body if called before CloudTeamCollection has
// finished (re)connecting after a fresh launch -- TeamCollectionApi.HandleCheckInCurrentBook's
// `if (!_tcManager.CheckConnection()) { request.Failed(); return; }` guard. This is a different
// post-launch race than `bloomApi.ts`'s 404 endpoint-registration retry (that one is about routes
// existing at all; this one is about the cloud connection itself being live yet), so callers that
// relaunch mid-test and immediately need a working cloud connection must wait for this explicitly
// -- the same `capabilities.supportsSharingUi` signal `twoInstanceSetup.ts` already polls after
// every fresh launch/join, factored out here since every relaunch-then-immediately-act step in
// this file needs it too.
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

// DISCOVERED: even after `capabilities.supportsSharingUi` is true (proving `CurrentCollection`
// IS a live CloudTeamCollection right then), `teamCollection/checkInCurrentBook` can still 503
// with an empty body moments later. Root cause (read, not guessed):
// `TeamCollectionManager.CheckConnection()` makes a FRESH, synchronous, unretried
// `_client.MyCollections()` network round trip on EVERY call (not a cached flag), and on ANY
// failure calls `MakeDisconnected(...)`, which sets `CurrentCollection = null` -- so a single
// transient hiccup (plausible right after a relaunch, especially when racing another instance's
// simultaneous relaunch against the same local stack, as this file's tests do) can permanently
// drop the connection for the rest of that process's life, not just glitch once. Retrying the
// SAME `checkInCurrentBook` call a few times is a legitimate probe of whether that happened: if
// the underlying connection is fine and this was a one-off network blip, a later `MyCollections()`
// call succeeds and capabilities would still show connected; if `CurrentCollection` was actually
// nulled out, capabilities would flip to false and no amount of retrying `checkInCurrentBook`
// itself would recover without a fresh relaunch.
const checkInCurrentBookWithConnectionRetry = async (
    httpPort: number,
): Promise<Response> => {
    const deadline = Date.now() + 15_000;
    let lastResponse: Response;
    do {
        lastResponse = await postApi(
            httpPort,
            "teamCollection/checkInCurrentBook",
            "{}",
        );
        if (lastResponse.status === 200) return lastResponse;
        await new Promise((resolve) => setTimeout(resolve, 500));
    } while (Date.now() < deadline);
    return lastResponse;
};

test.describe("E2E-9 new-book lifecycle", () => {
    let alice: LaunchedBloom | undefined;
    let bob: LaunchedBloom | undefined;

    test.beforeEach(async () => {
        await resetStack();
    });

    test.afterEach(async () => {
        await Promise.all(
            [alice, bob]
                .filter((i): i is LaunchedBloom => !!i)
                .map((i) => i.kill()),
        );
        alice = undefined;
        bob = undefined;
    });

    test("a new book is invisible to teammates until the first Send, then appears", async () => {
        const shared = await setUpAliceAndBobOnSharedCollection(
            "e2e-9-lifecycle",
            LOG_DIR,
        );
        alice = shared.alice;
        bob = shared.bob;
        const { aliceScratch, bobCollectionFilePath } = shared;
        const bobCollectionFolder = path.dirname(bobCollectionFilePath);

        const sourceId = await readBookInstanceId(
            aliceScratch.collectionFolder,
            aliceScratch.bookName,
        );
        const { folderName: newBookFolder } = await duplicateBook(
            alice.httpPort,
            aliceScratch.collectionFolder,
            sourceId,
        );

        // Sanity: before any Send, this book has never been registered server-side at all, so
        // Bob's local folder (unrelated to the local-only duplicate) has never heard of it.
        await pollNowViaReceiveUpdates(bob.httpPort);
        expect(await listBookFolders(bobCollectionFolder)).not.toContain(
            newBookFolder,
        );

        // Send (checkInCurrentBook operates on the current selection, which DuplicateBook
        // already switched to the new book -- CollectionModel.DuplicateBook calls SelectBook).
        const checkinResponse = await postApi(
            alice.httpPort,
            "teamCollection/checkInCurrentBook",
            "{}",
        );
        expect(checkinResponse.status).toBe(200);

        // Bob must now see it once he refreshes (re-issuing receiveUpdates each iteration --
        // see E2E-3 finding #10 on why a single reading right after one receiveUpdates call
        // isn't guaranteed to reflect its result yet).
        await expect
            .poll(
                async () => {
                    await pollNowViaReceiveUpdates(bob!.httpPort);
                    return listBookFolders(bobCollectionFolder);
                },
                {
                    timeout: 20_000,
                    message: "Bob never received the newly-committed book",
                },
            )
            .toContain(newBookFolder);
    });

    test("killing Bloom mid first-Send leaves no phantom, and resuming completes cleanly to one version", async () => {
        const shared = await setUpAliceAndBobOnSharedCollection(
            "e2e-9-kill-mid-send",
            LOG_DIR,
        );
        alice = shared.alice;
        bob = shared.bob;
        const { aliceScratch, bobCollectionFilePath } = shared;
        const bobCollectionFolder = path.dirname(bobCollectionFilePath);

        const sourceId = await readBookInstanceId(
            aliceScratch.collectionFolder,
            aliceScratch.bookName,
        );
        const { folderName: newBookFolder, bookInstanceId } =
            await duplicateBook(
                alice.httpPort,
                aliceScratch.collectionFolder,
                sourceId,
            );

        const dbClient = await openPersistentClient();
        try {
            // Fire the Send without awaiting its HTTP response -- we want to interrupt it
            // mid-flight, not after it completes. Swallow the eventual connection-reset error
            // from killing the process out from under the request.
            void postApi(
                alice.httpPort,
                "teamCollection/checkInCurrentBook",
                "{}",
            ).catch(() => undefined);

            // Poll the DB directly with a held-open connection (a fresh connect()/end() per
            // query, as harness/db.ts's queryDb does, is tens of ms of overhead on its own --
            // enough to blow straight through the narrow window between checkin_start_tx's
            // row-insert and checkin_finish_tx's version-commit on a fast local stack) for the
            // checkin-start row, then kill Alice as fast as possible afterward.
            let sawRow: { current_version_id: string | null } | undefined;
            const deadline = Date.now() + 10_000;
            while (!sawRow && Date.now() < deadline) {
                const result = await dbClient.query(
                    "select current_version_id from tc.books where instance_id = $1",
                    [bookInstanceId],
                );
                if (result.rows.length > 0) {
                    sawRow = result.rows[0];
                } else {
                    await new Promise((resolve) => setTimeout(resolve, 5));
                }
            }
            if (!sawRow) {
                throw new Error(
                    "checkin_start_tx's tc.books row for the new book never appeared within 10s " +
                        "-- cannot exercise the kill-mid-Send race.",
                );
            }
            // A direct `process.kill()` (a synchronous OS call from right here in this process,
            // which Node maps to TerminateProcess on Windows) rather than `alice.kill()`'s full
            // killBloomProcess.mjs-subprocess-plus-port-verification dance: that path's own
            // overhead (spawning a whole separate Node process, then polling every 500ms for the
            // port to go dark) is easily slower than this entire Send (a handful of small files
            // to local MinIO) takes to finish end-to-end -- confirmed empirically, this test
            // reliably found a fully-committed book (tc.events already populated) by the time
            // `alice.kill()`'s slower path had finished "interrupting" it.
            process.kill(alice.processId);

            expect(
                sawRow.current_version_id,
                "the Send completed (current_version_id was already set) before the kill could " +
                    "land -- this run did not actually exercise a mid-Send interruption; re-run " +
                    "or tighten the poll interval",
            ).toBeNull();
        } finally {
            await dbClient.end();
        }

        // No phantom: Bob (an unrelated, already-joined member) must never see this book while
        // its transaction sits interrupted, and no event should exist for it at all.
        await pollNowViaReceiveUpdates(bob.httpPort);
        expect(await listBookFolders(bobCollectionFolder)).not.toContain(
            newBookFolder,
        );
        const eventRows = await queryDb(
            "select e.id from tc.events e join tc.books b on b.id = e.book_id where b.instance_id = $1",
            [bookInstanceId],
        );
        expect(
            eventRows,
            "an interrupted checkin_start_tx should never have produced a tc.events row",
        ).toHaveLength(0);

        // Resume: relaunch Alice pointed at the SAME collection file. Her local book folder
        // (with its content and the still-local-only lock) is untouched by the kill -- only the
        // process died. Re-select the book explicitly rather than relying on whatever Bloom
        // happens to auto-select on startup.
        alice = await launchBloom({
            collectionFilePath: aliceScratch.collectionFilePath,
            user: ALICE,
            label: "e2e-9-kill-mid-send-alice-resumed",
            logDir: LOG_DIR,
        });
        await waitForCloudConnectionReady(alice.httpPort);
        await selectBookByName(
            alice.httpPort,
            aliceScratch.collectionFolder,
            newBookFolder,
        );
        const resumedCheckin = await checkInCurrentBookWithConnectionRetry(
            alice.httpPort,
        );
        if (resumedCheckin.status !== 200) {
            // Surface Bloom's own message log on failure -- this is exactly how a real bug was
            // found and fixed here (see TeamCollectionApi.UpdateUiForBook's doc comment): the
            // resumed check-in was silently succeeding SERVER-SIDE, then reporting 503 to the
            // caller because of a NullReferenceException in post-checkin UI-refresh code that
            // only reproduces when no window is the OS's "active form" (true for every instance
            // this harness launches, since none of them ever receive real focus).
            // eslint-disable-next-line no-console
            console.log(
                "resumedCheckin failed; teamCollection/getLog:",
                await (
                    await getApi(alice.httpPort, "teamCollection/getLog")
                ).text(),
            );
        }
        expect(resumedCheckin.status).toBe(200);

        // Exactly one committed version, exactly one book row -- never two, never zero.
        const finalRows = await queryDb<{
            id: string;
            current_version_id: string | null;
        }>(
            "select id, current_version_id from tc.books where instance_id = $1",
            [bookInstanceId],
        );
        expect(finalRows).toHaveLength(1);
        expect(finalRows[0].current_version_id).not.toBeNull();

        // And Bob now receives exactly one copy of it too.
        await expect
            .poll(
                async () => {
                    await pollNowViaReceiveUpdates(bob!.httpPort);
                    return (await listBookFolders(bobCollectionFolder)).filter(
                        (name) => name === newBookFolder,
                    ).length;
                },
                {
                    timeout: 90_000, // past the organic 60s poll
                    message:
                        "Bob never received the resumed book (or received it more than once)",
                },
            )
            .toBe(1);
    });

    test("two members creating a same-named book concurrently both end up shared under distinct names", async () => {
        const shared = await setUpAliceAndBobOnSharedCollection(
            "e2e-9-name-race",
            LOG_DIR,
        );
        alice = shared.alice;
        bob = shared.bob;
        const { aliceScratch, bobCollectionFilePath } = shared;
        const bobCollectionFolder = path.dirname(bobCollectionFilePath);
        const raceBookName = "RaceBook";

        // Seed on disk (Bloom instances still running, but each seeds only its OWN local
        // collection folder, which neither instance is watching for externally-added files --
        // see seedAdditionalBookIntoCollection's doc comment) then relaunch to pick it up via
        // the normal collection-load scan, exactly like collections/pullDown's own
        // kill-then-relaunch pattern.
        const aliceBook = await seedAdditionalBookIntoCollection(
            aliceScratch.collectionFolder,
            raceBookName,
        );
        const bobBook = await seedAdditionalBookIntoCollection(
            bobCollectionFolder,
            raceBookName,
        );
        expect(aliceBook.bookInstanceId).not.toBe(bobBook.bookInstanceId);

        await Promise.all([alice.kill(), bob.kill()]);
        [alice, bob] = await Promise.all([
            launchBloom({
                collectionFilePath: aliceScratch.collectionFilePath,
                user: ALICE,
                label: "e2e-9-name-race-alice",
                logDir: LOG_DIR,
            }),
            launchBloom({
                collectionFilePath: bobCollectionFilePath,
                user: BOB,
                label: "e2e-9-name-race-bob",
                logDir: LOG_DIR,
            }),
        ]);

        await Promise.all([
            waitForCloudConnectionReady(alice.httpPort),
            waitForCloudConnectionReady(bob.httpPort),
        ]);

        // Select via the direct API on both sides (no CDP dependency -- see harness/selectBook.ts).
        await Promise.all([
            selectBookByName(
                alice.httpPort,
                aliceScratch.collectionFolder,
                raceBookName,
            ),
            selectBookByName(bob.httpPort, bobCollectionFolder, raceBookName),
        ]);

        // The race: both Sends fire at once. CloudTeamCollection.PutBookInRepo's retry loop
        // means the loser's OWN HTTP call still succeeds -- it just resolves to a numeric-
        // suffixed name transparently before replying -- so both requests should report 200.
        const [aliceCheckin, bobCheckin] = await Promise.all([
            checkInCurrentBookWithConnectionRetry(alice.httpPort),
            checkInCurrentBookWithConnectionRetry(bob.httpPort),
        ]);
        expect(
            aliceCheckin.status,
            "Alice's Send should still succeed even if she lost the name race (the client " +
                "retries with a numeric suffix transparently)",
        ).toBe(200);
        expect(bobCheckin.status).toBe(200);

        // Exactly two distinct, fully-committed book rows sharing the same base name.
        const rows = await queryDb<{
            name: string;
            current_version_id: string | null;
        }>(
            "select b.name, b.current_version_id from tc.books b " +
                "where b.instance_id = any($1::uuid[])",
            [[aliceBook.bookInstanceId, bobBook.bookInstanceId]],
        );
        expect(rows).toHaveLength(2);
        for (const row of rows) {
            expect(
                row.current_version_id,
                `both racing books must end up committed, got name=${row.name}`,
            ).not.toBeNull();
        }
        const names = rows.map((row) => row.name).sort();
        expect(
            names[0],
            `expected the two racing books to resolve to distinct names, got ${JSON.stringify(names)}`,
        ).not.toBe(names[1]);
        // Exactly one side keeps the plain name; the loser's suffix-resolved name still starts
        // with it (PutBookInRepo's "name2" convention). This is what proves the race actually
        // collided at the SAME proposed name rather than both sides having quietly diverged
        // before Send (which is exactly what happened before seedAdditionalBookIntoCollection
        // stamped the book title -- see its doc comment).
        expect(
            names.filter((name) => name === raceBookName),
            `exactly one book should keep the plain name '${raceBookName}', got ${JSON.stringify(names)}`,
        ).toHaveLength(1);
        for (const name of names) {
            expect(
                name.startsWith(raceBookName),
                `both final names should be derived from '${raceBookName}', got ${JSON.stringify(names)}`,
            ).toBe(true);
        }
    });
});
