// E2E-4: forced check-in / recovery.
//
// WHAT THIS TEST COVERS (reachable, green): the admin force-unlock + steal-checkout flow and
// the victim's coherent aftermath. Alice checks a book out; an admin (Bob, promoted mid-test)
// force-unlocks her and takes the checkout; Alice's instance converges on "checked out by Bob",
// her own attempt to re-take the lock is refused by the race-free server, and Bob's lock is
// intact throughout -- no crash, no corruption, no double-lock.
//
// WHAT IS DOCUMENTED AS BLOCKED (see the task progress log for the full root-cause): the design
// doc's "unified recovery" -- preserving the victim's un-checked-in local edits as a
// `.bloomSource` in "Lost and Found" plus a WorkPreservedLocally incident -- could NOT be
// driven end-to-end through the cloud backend from this harness. Both entry points into the
// shared recovery code are gated by cloud-specific state the harness can't establish:
//   * SyncAtStartup's conflict loop is gated on `IsCheckedOutHereBy(localStatus)` reading the
//     on-disk TeamCollection.status file -- but cloud `AttemptLock` never writes `lockedBy`
//     into that file (it updates only the server row + in-memory cache + UI events), so after a
//     restart the loop treats the book as not-checked-out-here and skips recovery entirely
//     (confirmed live: the checkout-time local status showed `lockedBy: null`).
//   * The interactive `checkInCurrentBook` recovery branch (`!OkToCheckIn`) is unreachable
//     because `HandleCheckInCurrentBook` calls `Save()` first (throws once the cache knows Bob
//     holds the lock -> 503 before the branch), and if the cache has NOT yet learned of the
//     steal, `OkToCheckIn` returns true and the server rejects at checkin-start with
//     LockHeldByOther instead.
// The two real bugs this scenario nonetheless pinned WERE fixed: the recovery-path NRE in
// `TeamCollectionApi.UpdateUiForBook` (fixed under E2E-9, finding #11 -- CheckInOneBook calls it
// on both the happy AND recovery branches), and `CloudCollectionClient.LogEvent` posting
// `p_comment` instead of the RPC's real `p_message` parameter (which would have silently dropped
// the WorkPreservedLocally incident even if the path were reached) -- fixed here with unit
// coverage in CloudCollectionClientTests.
import { test, expect } from "@playwright/test";
import * as path from "node:path";
import { resetStack } from "../harness/reset";
import { setUpAliceAndBobOnSharedCollection } from "../harness/twoInstanceSetup";
import { LaunchedBloom } from "../harness/launch";
import { BOB } from "../harness/localStack";
import { postApi } from "../harness/bloomApi";
import { bookStatus, pollNowViaReceiveUpdates } from "../harness/bookStatus";
import { selectBookByName } from "../harness/selectBook";
import { queryDb } from "../harness/db";

const LOG_DIR = "C:\\BloomE2E-logs\\e2e-4";
const BOB_USER_ID = "00000000-0000-0000-0000-000000000003";

test.describe("E2E-4 forced check-in recovery", () => {
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

    test("admin force-unlock steals a checkout race-free: victim converges on the new holder, cannot re-take, and the thief's lock stays intact", async () => {
        const shared = await setUpAliceAndBobOnSharedCollection(
            "e2e-4",
            LOG_DIR,
        );
        alice = shared.alice;
        bob = shared.bob;
        const { aliceScratch, bobCollectionFilePath } = shared;
        const bookName = aliceScratch.bookName;

        // --- Alice checks the book out ---
        await selectBookByName(
            alice.httpPort,
            aliceScratch.collectionFolder,
            bookName,
        );
        const aliceLock = await (
            await postApi(
                alice.httpPort,
                "teamCollection/attemptLockOfCurrentBook",
                "{}",
            )
        ).json();
        expect(aliceLock).toBe(true);

        // Alice (the creator/admin) promotes Bob to admin so he can force-unlock. setRole is an
        // admin-only action; Bob cannot promote himself.
        const setRole = await postApi(
            alice.httpPort,
            "sharing/setRole",
            JSON.stringify({
                collectionId: aliceScratch.collectionId,
                email: BOB.email,
                role: "admin",
            }),
        );
        expect(setRole.status).toBe(200);

        // --- Bob sees Alice's lock, force-unlocks it, and takes the checkout himself ---
        await selectBookByName(
            bob.httpPort,
            path.dirname(bobCollectionFilePath),
            bookName,
        );
        await expect
            .poll(
                async () => {
                    await pollNowViaReceiveUpdates(bob!.httpPort);
                    return (await bookStatus(bob!.httpPort, bookName)).who;
                },
                { timeout: 90_000, message: "Bob never saw Alice's checkout" },
            )
            .toBeTruthy();

        const forceUnlock = await postApi(
            bob.httpPort,
            "teamCollection/forceUnlock",
            "{}",
        );
        expect(forceUnlock.status).toBe(200);
        const bobLock = await (
            await postApi(
                bob.httpPort,
                "teamCollection/attemptLockOfCurrentBook",
                "{}",
            )
        ).json();
        expect(bobLock, "Bob's checkout after force-unlock must succeed").toBe(
            true,
        );

        // --- Alice's instance converges on "checked out by Bob" ---
        await expect
            .poll(
                async () => {
                    await pollNowViaReceiveUpdates(alice!.httpPort);
                    return (await bookStatus(alice!.httpPort, bookName)).who;
                },
                {
                    timeout: 20_000,
                    message: "Alice never learned Bob now holds the lock",
                },
            )
            .toBe(BOB.email);

        // --- Alice cannot re-take the lock while Bob holds it (server is race-free) ---
        const aliceRetake = await (
            await postApi(
                alice.httpPort,
                "teamCollection/attemptLockOfCurrentBook",
                "{}",
            )
        ).json();
        expect(
            aliceRetake,
            "Alice must not be able to re-take a book the admin took from her",
        ).toBe(false);

        // --- The server lock is exactly Bob's, singular and intact ---
        const rows = await queryDb<{ locked_by: string | null }>(
            "select locked_by from tc.books where collection_id = $1 and name = $2",
            [aliceScratch.collectionId, bookName],
        );
        expect(rows).toHaveLength(1);
        expect(rows[0].locked_by).toBe(BOB_USER_ID);
    });
});
