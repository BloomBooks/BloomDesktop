// E2E-8: Receive-during-Send coherence (mandated) -- byte-perfect old version, never a mix.
//
// The guarantee: while one teammate's Send of a new version is in flight (checkin transaction
// OPEN, changed files being/already uploaded to S3 as new object-versions, but checkin-finish
// not yet committed), any OTHER teammate who Receives must get the current committed version
// byte-for-byte -- never a mix of old files and the sender's in-flight new ones. This holds
// because `tc.version_files` (the manifest `get_book_manifest` hands the receiver, which pins
// exact per-path S3 `s3VersionId`s) is only rewritten inside the atomic `tc.checkin_finish_tx`;
// until then it still pins v1's object-versions, so even though newer S3 object-versions for the
// changed files already exist, the receiver downloads v1's pinned ones. (Migrations
// 20260706000004 + 20260707000005_tc_get_book_manifest.)
//
// TEST FRAMING: a genuinely live race ("Receive while a Send is momentarily open") is
// TOCTOU-prone -- the Send can commit between the harness confirming "open" and the receiver's
// download completing, so the receiver legitimately observes v2 and the test can't tell a real
// coherence bug from a benign timing win. Instead this FREEZES the Send in its open state: the
// v2 edit adds a large (~48 MB) incompressible asset so the upload phase is long, and the sender
// is `process.kill`ed the instant its checkin transaction opens -- reliably BEFORE checkin-finish
// (which waits for the whole 48 MB). That leaves an orphaned-but-open transaction and the book
// still committed at v1, an immutable state in which the receiver's coherence can be asserted
// deterministically and repeatedly. Surviving the sender's mid-Send death is a strictly harder
// case than a merely in-progress Send. Alice then restarts, resumes, and Bob finally gets v2.
import { test, expect } from "@playwright/test";
import * as fs from "node:fs/promises";
import * as path from "node:path";
import { randomBytes } from "node:crypto";
import { resetStack } from "../harness/reset";
import { setUpAliceAndBobOnSharedCollection } from "../harness/twoInstanceSetup";
import { launchBloom, LaunchedBloom } from "../harness/launch";
import { ALICE } from "../harness/devStack";
import { postApi, waitForSharingReady } from "../harness/bloomApi";
import { pollNowViaReceiveUpdates } from "../harness/bookStatus";
import { selectBookByName, waitForBookFile } from "../harness/selectBook";
import { queryDb, openPersistentClient } from "../harness/db";

const LOG_DIR = "C:\\BloomE2E-logs\\e2e-8";
const V2_MARKER = "ALICE-V2-INFLIGHT-MARKER";
// A large NEW book-root `.txt` widens the v2 Send's upload phase enough to reliably kill the
// sender mid-upload (before checkin-finish). It must be an extension BookFileFilter INCLUDES in
// the upload manifest (an arbitrary `.bin` is silently excluded -> zero upload time) but which
// Bloom does NOT image-process (a large fake `.png` makes external/select-book hang 30s+ as
// Bloom tries to decode it). `.txt` is in BookLevelFileExtensionsLowerCase and never decoded.
const BIG_ASSET_NAME = "big-v2-asset.txt";
// Sized so the upload phase lasts SECONDS against warm localhost MinIO, not tens of
// milliseconds: the kill must land between checkin-start's tx-open and checkin-finish, and
// its end-to-end latency (pg poll + signal delivery) is ~100ms+. 40 MB lost that race on a
// warm full-matrix run (the Send committed first); 256 MB gives an order-of-magnitude margin.
const BIG_ASSET_BYTES = 256 * 1024 * 1024;

test.describe("E2E-8 Receive-during-Send coherence", () => {
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

    test("a teammate Receiving while a v2 Send is in flight gets byte-perfect v1, never a mix; clean v2 only after the Send commits", async () => {
        const shared = await setUpAliceAndBobOnSharedCollection(
            "e2e-8",
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

        const bookRows = await queryDb<{
            id: string;
            current_version_seq: number;
        }>(
            "select id, current_version_seq from tc.books where collection_id = $1 and name = $2",
            [aliceScratch.collectionId, bookName],
        );
        const bookId = bookRows[0].id;
        expect(Number(bookRows[0].current_version_seq)).toBe(1);

        await pollNowViaReceiveUpdates(bob.httpPort);
        // Progressive join (batch item 7): Bob's copy of the book downloads in the background
        // after his join, so wait for the file rather than assuming it's already on disk.
        await waitForBookFile(bobHtmPath);
        const v1Bytes = await fs.readFile(bobHtmPath);
        expect(v1Bytes.includes(Buffer.from(V2_MARKER, "utf8"))).toBe(false);
        // The big asset is NEW in v2 -- confirm it's absent from Bob's v1 so its absence during
        // the open Send (and presence only after commit) is a meaningful coherence signal.
        const bobBigAssetPath = path.join(
            path.dirname(bobHtmPath),
            BIG_ASSET_NAME,
        );
        expect(
            await fs.access(bobBigAssetPath).then(
                () => true,
                () => false,
            ),
            "sanity: v1 must not already contain the big v2 asset",
        ).toBe(false);

        // Alice checks out; while down, her local content is edited to v2 (marker in the htm PLUS
        // a large new asset that widens the upload/open-transaction window).
        await selectBookByName(
            alice.httpPort,
            aliceScratch.collectionFolder,
            bookName,
        );
        expect(
            await (
                await postApi(
                    alice.httpPort,
                    "teamCollection/attemptLockOfCurrentBook",
                    "{}",
                )
            ).json(),
        ).toBe(true);
        await alice.kill();
        alice = undefined;
        const originalHtm = await fs.readFile(aliceHtmPath, "utf8");
        await fs.writeFile(
            aliceHtmPath,
            originalHtm.replace(
                "</body>",
                `<div class="bloom-editable">${V2_MARKER}</div></body>`,
            ),
            "utf8",
        );
        await fs.writeFile(
            path.join(aliceScratch.collectionFolder, bookName, BIG_ASSET_NAME),
            randomBytes(BIG_ASSET_BYTES),
        );

        // Alice reopens (still holds the checkout) and starts the v2 Send -- left in flight.
        alice = await launchBloom({
            collectionFilePath: aliceScratch.collectionFilePath,
            user: ALICE,
            label: "e2e-8-alice-sending",
            logDir: LOG_DIR,
        });
        await waitForSharingReady(alice.httpPort);
        await selectBookByName(
            alice.httpPort,
            aliceScratch.collectionFolder,
            bookName,
        );

        void postApi(
            alice.httpPort,
            "teamCollection/checkInCurrentBook",
            "{}",
        ).catch(() => undefined);

        const dbClient = await openPersistentClient();
        try {
            // Kill the sender the instant its transaction opens -- while the 48 MB asset is still
            // uploading, so this lands before checkin-finish -- freezing an orphaned open Send.
            let caught = false;
            const deadline = Date.now() + 30_000;
            while (!caught && Date.now() < deadline) {
                const rows = await dbClient.query(
                    "select 1 from tc.checkin_transactions where book_id = $1 and status = 'open'",
                    [bookId],
                );
                if (rows.rows.length > 0) {
                    caught = true;
                    break;
                }
                await new Promise((r) => setTimeout(r, 5));
            }
            expect(
                caught,
                "never observed an open checkin transaction -- could not exercise Receive-during-Send",
            ).toBe(true);
            process.kill(alice.processId);

            // Confirm the state is frozen: transaction still open, book still v1. (If the Send
            // committed before the kill landed, the big asset wasn't big enough -- re-run/enlarge.)
            const frozen = await dbClient.query(
                "select b.current_version_seq, " +
                    // started_at, NOT id: the id is gen_random_uuid(), which has no temporal
                    // order — sorting by it returned the FINISHED v1 transaction instead of
                    // the frozen-open v2 one on a literal coin flip, making this test's
                    // pass/fail random (three false failures before the post-mortem caught it).
                    "(select status from tc.checkin_transactions where book_id = b.id order by started_at desc limit 1) as tx_status " +
                    "from tc.books b where b.id = $1",
                [bookId],
            );
            expect(
                frozen.rows[0].tx_status,
                "the Send committed before the kill landed; widen BIG_ASSET_BYTES",
            ).toBe("open");
            expect(Number(frozen.rows[0].current_version_seq)).toBe(1);
        } finally {
            await dbClient.end();
        }

        // THE MANDATED ASSERTION: with the Send frozen open (state immutable -- sender is dead),
        // Bob Receives. He must get byte-perfect v1 -- no marker, no big asset, byte-identical
        // htm -- never the in-flight v2 or a mix. Twice, to be sure a repeated pull can't leak it.
        for (let i = 0; i < 2; i++) {
            await pollNowViaReceiveUpdates(bob.httpPort);
            const bobNow = await fs.readFile(bobHtmPath);
            expect(
                bobNow.includes(Buffer.from(V2_MARKER, "utf8")),
                `Receive #${i + 1} during an open Send leaked Alice's in-flight v2 htm content`,
            ).toBe(false);
            expect(
                bobNow.equals(v1Bytes),
                `Receive #${i + 1} during an open Send did not yield byte-identical v1 htm`,
            ).toBe(true);
            expect(
                await fs.access(bobBigAssetPath).then(
                    () => true,
                    () => false,
                ),
                `Receive #${i + 1} during an open Send leaked Alice's in-flight big v2 asset`,
            ).toBe(false);
        }

        // --- Alice restarts and resumes; only NOW does v2 become the committed version ---
        alice = await launchBloom({
            collectionFilePath: aliceScratch.collectionFilePath,
            user: ALICE,
            label: "e2e-8-alice-resumed",
            logDir: LOG_DIR,
        });
        await waitForSharingReady(alice.httpPort);
        await selectBookByName(
            alice.httpPort,
            aliceScratch.collectionFolder,
            bookName,
        );
        const resumeDeadline = Date.now() + 20_000;
        let resumeStatus = 0;
        while (resumeStatus !== 200 && Date.now() < resumeDeadline) {
            resumeStatus = (
                await postApi(
                    alice.httpPort,
                    "teamCollection/checkInCurrentBook",
                    "{}",
                )
            ).status;
            if (resumeStatus !== 200)
                await new Promise((r) => setTimeout(r, 500));
        }
        expect(resumeStatus).toBe(200);
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
                { timeout: 30_000, message: "Alice's Send never committed v2" },
            )
            .toBe(2);

        // Now (and only now) Bob receives byte-identical v2, big asset included.
        await expect
            .poll(
                async () => {
                    await pollNowViaReceiveUpdates(bob!.httpPort);
                    return (await fs.readFile(bobHtmPath)).includes(
                        Buffer.from(V2_MARKER, "utf8"),
                    );
                },
                {
                    timeout: 30_000,
                    message: "Bob never received the committed v2",
                },
            )
            .toBe(true);
        const [aliceV2, bobV2] = await Promise.all([
            fs.readFile(aliceHtmPath),
            fs.readFile(bobHtmPath),
        ]);
        expect(bobV2.equals(aliceV2)).toBe(true);
        expect(
            (await fs.stat(bobBigAssetPath)).size,
            "Bob should have the big v2 asset after the Send committed",
        ).toBe(BIG_ASSET_BYTES);
    });
});
