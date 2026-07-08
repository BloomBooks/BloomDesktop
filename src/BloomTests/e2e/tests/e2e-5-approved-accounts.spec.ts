// E2E-5: approved accounts on two fresh profiles ("another computer").
//
// The approved-accounts model (CONTRACTS.md; tc.members): access is granted per ACCOUNT EMAIL,
// not per machine or per Bloom registration. An admin approves an email; the row sits UNCLAIMED
// (user_id NULL) until that account's holder signs in anywhere and claims it; from then on that
// account can participate from ANY computer with no pre-existing local state. This spec drives
// that full arc against real instances:
//
//   1. Alice creates/shares and approves bob@dev.local -> a tc.members row exists with
//      user_id NULL (approved-but-unclaimed).
//   2. An UNAPPROVED account (admin@dev.local -- a seeded dev user never added to this
//      collection) on its own fresh profile does NOT see the collection in collections/mine and
//      cannot pull it down (server refuses; nothing is created locally).
//   3. Bob, on a fresh profile ("another computer": a placeholder collection unrelated to the
//      TC, no prior local copy), DOES see it in collections/mine while still UNCLAIMED (the
//      email-match rule), pulls it down, and the join claims his membership (user_id stamped
//      with his fixed seeded id, claimed_at set).
//   4. With Alice's instance killed entirely (her "computer" is off -- membership must not
//      depend on the admin being online), Bob checks out, is identified by his ACCOUNT email in
//      book status, and checks in a new version successfully.
import { test, expect } from "@playwright/test";
import * as path from "node:path";
import { resetStack } from "../harness/reset";
import {
    createScratchCollection,
    pulledDownCollectionFilePath,
} from "../harness/collectionFixture";
import { launchBloom, LaunchedBloom } from "../harness/launch";
import { ALICE, BOB, ADMIN } from "../harness/devStack";
import { postApi, getApi } from "../harness/bloomApi";
import { bookStatus } from "../harness/bookStatus";
import { selectBookByName } from "../harness/selectBook";
import { queryDb } from "../harness/db";

const LOG_DIR = "C:\\BloomE2E-logs\\e2e-5";

// Bob's fixed seeded auth user id (server/dev/seed.sql).
const BOB_USER_ID = "00000000-0000-0000-0000-000000000003";

test.describe("E2E-5 approved accounts on two fresh profiles", () => {
    const instances: LaunchedBloom[] = [];

    const track = (instance: LaunchedBloom): LaunchedBloom => {
        instances.push(instance);
        return instance;
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

    test("approval is per account: unclaimed member can join from a fresh profile, unapproved account cannot", async () => {
        // --- 1. Alice creates, shares, and approves Bob's EMAIL ---
        const aliceScratch = await createScratchCollection("e2e-5", "alice");
        const alice = track(
            await launchBloom({
                collectionFilePath: aliceScratch.collectionFilePath,
                user: ALICE,
                label: "e2e-5-alice",
                logDir: LOG_DIR,
            }),
        );
        await alice.connect(); // connect-before-trigger (finding #7)
        const createResponse = await postApi(
            alice.httpPort,
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
                                alice.httpPort,
                                "teamCollection/capabilities",
                            )
                        ).json()
                    ).supportsSharingUi,
                { timeout: 20_000 },
            )
            .toBe(true);
        const approveResponse = await postApi(
            alice.httpPort,
            "sharing/addApproval",
            JSON.stringify({
                collectionId: aliceScratch.collectionId,
                email: BOB.email,
                role: "member",
            }),
        );
        expect(approveResponse.status).toBe(200);

        // The membership row exists but is UNCLAIMED: approval was by email alone; Bob's
        // account has never touched this collection.
        const unclaimedRows = await queryDb<{
            email: string;
            user_id: string | null;
            claimed_at: string | null;
        }>(
            "select email, user_id, claimed_at from tc.members where collection_id = $1 and email = $2",
            [aliceScratch.collectionId, BOB.email],
        );
        expect(unclaimedRows).toHaveLength(1);
        expect(unclaimedRows[0].user_id).toBeNull();
        expect(unclaimedRows[0].claimed_at).toBeNull();

        // --- 2. An UNAPPROVED account cannot see or join the collection ---
        const adminPlaceholder = await createScratchCollection(
            "e2e-5",
            "admin",
            "AdminPlaceholder",
        );
        const adminInstance = track(
            await launchBloom({
                collectionFilePath: adminPlaceholder.collectionFilePath,
                user: ADMIN,
                label: "e2e-5-admin-unapproved",
                logDir: LOG_DIR,
            }),
        );
        const adminMineResponse = await getApi(
            adminInstance.httpPort,
            "collections/mine",
        );
        expect(adminMineResponse.status).toBe(200);
        // Wire shape per SharingApi.ToCollectionSummary: {collectionId, name, role}.
        const adminMine = (await adminMineResponse.json()) as {
            collectionId: string;
        }[];
        expect(
            adminMine.map((c) => c.collectionId),
            "an unapproved account must not see the collection in collections/mine",
        ).not.toContain(aliceScratch.collectionId);

        const adminPullDown = await postApi(
            adminInstance.httpPort,
            "collections/pullDown",
            JSON.stringify({ collectionId: aliceScratch.collectionId }),
        );
        expect(
            adminPullDown.status,
            "an unapproved account's pullDown must be refused",
        ).not.toBe(200);
        await adminInstance.kill();

        // --- 3. Bob, fresh profile: sees it while UNCLAIMED, joins, membership gets claimed ---
        const bobPlaceholder = await createScratchCollection(
            "e2e-5",
            "bob",
            "BobPlaceholder",
        );
        const bobChooser = track(
            await launchBloom({
                collectionFilePath: bobPlaceholder.collectionFilePath,
                user: BOB,
                label: "e2e-5-bob-chooser",
                logDir: LOG_DIR,
            }),
        );
        // collections/mine must list the collection by EMAIL match even though Bob's user_id
        // has never been stamped on the membership row (my_collections' unclaimed-rows rule).
        const bobMineResponse = await getApi(
            bobChooser.httpPort,
            "collections/mine",
        );
        expect(bobMineResponse.status).toBe(200);
        const bobMine = (await bobMineResponse.json()) as {
            collectionId: string;
            name: string;
        }[];
        expect(bobMine.map((c) => c.collectionId)).toContain(
            aliceScratch.collectionId,
        );

        const bobPullDown = await postApi(
            bobChooser.httpPort,
            "collections/pullDown",
            JSON.stringify({ collectionId: aliceScratch.collectionId }),
        );
        expect(bobPullDown.status).toBe(200);

        // The join claimed the membership: user_id stamped with Bob's fixed seeded account id.
        const claimedRows = await queryDb<{
            user_id: string | null;
            claimed_at: string | null;
        }>(
            "select user_id, claimed_at from tc.members where collection_id = $1 and email = $2",
            [aliceScratch.collectionId, BOB.email],
        );
        expect(claimedRows).toHaveLength(1);
        expect(claimedRows[0].user_id).toBe(BOB_USER_ID);
        expect(claimedRows[0].claimed_at).not.toBeNull();

        await bobChooser.kill();

        // --- 4. Alice's computer goes off; Bob participates fully on his own ---
        await alice.kill();

        const bookName = aliceScratch.bookName;
        const initialSeqRows = await queryDb<{ current_version_seq: number }>(
            "select current_version_seq from tc.books where collection_id = $1 and name = $2",
            [aliceScratch.collectionId, bookName],
        );
        expect(initialSeqRows).toHaveLength(1);
        const initialSeq = Number(initialSeqRows[0].current_version_seq);
        expect(initialSeq).toBeGreaterThanOrEqual(1); // sanity: initial share committed v1

        const bobCollectionFilePath = await pulledDownCollectionFilePath(
            aliceScratch.collectionName,
        );
        const bob = track(
            await launchBloom({
                collectionFilePath: bobCollectionFilePath,
                user: BOB,
                label: "e2e-5-bob-joined",
                logDir: LOG_DIR,
            }),
        );
        await expect
            .poll(
                async () =>
                    (
                        await (
                            await getApi(
                                bob.httpPort,
                                "teamCollection/capabilities",
                            )
                        ).json()
                    ).supportsSharingUi,
                { timeout: 20_000 },
            )
            .toBe(true);

        await selectBookByName(
            bob.httpPort,
            path.dirname(bobCollectionFilePath),
            bookName,
        );
        const lockResult = await (
            await postApi(
                bob.httpPort,
                "teamCollection/attemptLockOfCurrentBook",
                "{}",
            )
        ).json();
        expect(lockResult, "Bob's checkout must succeed").toBe(true);

        // Identity in a cloud TC is the ACCOUNT email (not machine, not Bloom registration) --
        // this is the "registration-vs-account" identity model the Wave-3 smoke fixed 4 sites
        // over. His own status must show his account as the holder.
        const status = await bookStatus(bob.httpPort, bookName);
        expect(status.who).toBe(BOB.email);

        const checkinResponse = await postApi(
            bob.httpPort,
            "teamCollection/checkInCurrentBook",
            "{}",
        );
        expect(checkinResponse.status).toBe(200);

        // The check-in committed a NEW version on the server (seq advanced past the initial
        // upload's), and released the lock.
        const finalRows = await queryDb<{
            current_version_seq: number;
            locked_by: string | null;
        }>(
            "select current_version_seq, locked_by from tc.books where collection_id = $1 and name = $2",
            [aliceScratch.collectionId, bookName],
        );
        expect(finalRows).toHaveLength(1);
        expect(Number(finalRows[0].current_version_seq)).toBeGreaterThan(
            initialSeq,
        );
        expect(finalRows[0].locked_by).toBeNull();
    });
});
