// E2E-2: two-instance collaboration loop (checkout visible; Send/Receive; byte-equal).
// Automates the Wave-3 manual two-instance smoke test end to end: Alice shares a collection,
// approves Bob, Bob joins ("pulls down") the same cloud collection on his own local folder,
// Alice checks out the one book (Bob must see it locked), Alice checks it back in (Send), and
// Bob receives the update (must see the lock released and end up byte-identical to Alice's copy).
//
// Setup steps (create/approve/join) go through the same direct-API path as E2E-1 for the same
// reason documented there (ReactDialog-hosted WebView2 dialogs aren't CDP-reachable). Book
// SELECTION is a real CDP click on the main Collection-tab page (the one WebView2 control
// that's reliably CDP-reachable), but checkout/check-in themselves also go through the direct
// API (see the NOTE below -- clicking the visible buttons had no effect, root cause
// undiagnosed). Bob's view of Alice's lock is forced to refresh immediately via
// `pollNowViaReceiveUpdates` rather than waiting out CloudCollectionMonitor's 60s timer.
import { test, expect } from "@playwright/test";
import * as fs from "node:fs/promises";
import { resetStack } from "../harness/reset";
import {
    createScratchCollection,
    pulledDownCollectionFilePath,
} from "../harness/collectionFixture";
import { launchBloom, LaunchedBloom } from "../harness/launch";
import { ALICE, BOB } from "../harness/devStack";
import {
    postApi,
    getApi,
    postCreateCloudTeamCollection,
} from "../harness/bloomApi";

const LOG_DIR = "C:\\BloomE2E-logs\\e2e-2";

const bookStatus = async (httpPort: number, folderName: string) => {
    const response = await getApi(
        httpPort,
        `teamCollection/bookStatus?folderName=${encodeURIComponent(folderName)}`,
    );
    expect(response.status).toBe(200);
    return response.json();
};

// CloudCollectionMonitor only polls the server every 60s by default
// (CloudCollectionMonitor.DefaultPollInterval) â€” an instance sitting idle would take up to a
// minute to notice a remote change organically. `teamCollection/receiveUpdates` internally
// calls `CloudTeamCollection.PollNow()` before doing anything else, so calling it is the
// harness's way of forcing an immediate poll instead of waiting out the timer.
const pollNowViaReceiveUpdates = async (httpPort: number) => {
    const response = await postApi(
        httpPort,
        "teamCollection/receiveUpdates",
        "{}",
    );
    expect(response.status).toBe(200);
};

test.describe("E2E-2 two-instance collaboration loop", () => {
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

    test("checkout is visible cross-instance, and Send/Receive ends byte-identical", async () => {
        // --- Alice creates and shares the collection ---
        const aliceScratch = await createScratchCollection("e2e-2", "alice");
        alice = await launchBloom({
            collectionFilePath: aliceScratch.collectionFilePath,
            user: ALICE,
            label: "e2e2-alice",
            logDir: LOG_DIR,
        });

        // Connect BEFORE triggering createCloudTeamCollection and keep reusing this same Page.
        // DISCOVERED: createCloudTeamCollection's reopen-collection callback reloads the
        // workspace's WebView2 control IN PLACE (same CDP target, growing body content as it
        // reloads) if something is already attached and watching -- but a FRESH
        // `connectOverCDP` call made *after* the reopen has already happened intermittently
        // fails to find any `/bloom/` page at all (confirmed via raw `/json/list` showing only
        // an `about:blank` target in that failure mode). Attaching early and holding the
        // connection across the reopen is the reliable path; reconnecting afterward is not.
        const { page: alicePage } = await alice.connect();

        const createResponse = await postCreateCloudTeamCollection(alice);
        expect(createResponse.status).toBe(200);
        await expect
            .poll(
                async () =>
                    (
                        await (
                            await getApi(
                                alice!.httpPort,
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

        // --- Bob joins from his own machine/profile (a separate local placeholder collection) ---
        const bobPlaceholder = await createScratchCollection(
            "e2e-2",
            "bob",
            "BobPlaceholder",
        );
        bob = await launchBloom({
            collectionFilePath: bobPlaceholder.collectionFilePath,
            user: BOB,
            label: "e2e2-bob",
            logDir: LOG_DIR,
        });

        const pullDownResponse = await postApi(
            bob.httpPort,
            "collections/pullDown",
            JSON.stringify({ collectionId: aliceScratch.collectionId }),
        );
        expect(pullDownResponse.status).toBe(200);

        // Bob's instance is still showing his placeholder collection; relaunch pointed at the
        // freshly pulled-down one (pullDown only downloads files, it doesn't switch what the
        // currently-running instance has open).
        await bob.kill();
        const bobCollectionFilePath = await pulledDownCollectionFilePath(
            aliceScratch.collectionName,
        );
        bob = await launchBloom({
            collectionFilePath: bobCollectionFilePath,
            user: BOB,
            label: "e2e2-bob-joined",
            logDir: LOG_DIR,
        });
        const bobCaps = await (
            await getApi(bob.httpPort, "teamCollection/capabilities")
        ).json();
        expect(bobCaps.supportsSharingUi).toBe(true);

        // Before checkout, nobody has the book locked.
        const statusBeforeCheckout = await bookStatus(
            bob.httpPort,
            aliceScratch.bookName,
        );
        expect(statusBeforeCheckout.who).toBeFalsy();

        // --- Alice selects the book (real CDP click; sets the server-facing "current book"
        // that the checkout/check-in endpoints below operate on) then checks it out ---
        // NOTE: clicking the visible "CHECK OUT BOOK"/"CHECK IN BOOK" buttons via CDP was tried
        // first and reliably had NO effect (confirmed via before/after screenshots showing an
        // unchanged button and `bookStatus` still reporting `who: null`) despite Playwright
        // reporting the click as successful and the button being visibly present -- while
        // calling the exact same backend endpoint those buttons post to
        // (`teamCollection/attemptLockOfCurrentBook`) directly succeeded immediately. Root cause
        // not fully diagnosed (possibly an MUI ripple/overlay intercepting the synthetic click's
        // hit-test); using the direct API call here, same rationale as the ReactDialog
        // workaround in E2E-1's header comment.
        await alicePage
            .getByText(aliceScratch.bookName, { exact: true })
            .first()
            .click();
        const lockResponse = await postApi(
            alice.httpPort,
            "teamCollection/attemptLockOfCurrentBook",
            "{}",
        );
        expect(lockResponse.status).toBe(200);

        await pollNowViaReceiveUpdates(bob.httpPort);
        await expect
            .poll(
                async () =>
                    (await bookStatus(bob!.httpPort, aliceScratch.bookName))
                        .who,
                {
                    timeout: 90_000, // past the organic 60s CloudCollectionMonitor poll, in case the forced poll raced the commit
                    message: "Bob never saw Alice's checkout",
                },
            )
            .toBeTruthy();

        // --- Alice checks the book back in (Send) ---
        const checkinResponse = await postApi(
            alice.httpPort,
            "teamCollection/checkInCurrentBook",
            "{}",
        );
        expect(checkinResponse.status).toBe(200);

        await pollNowViaReceiveUpdates(bob.httpPort);
        await expect
            .poll(
                async () =>
                    (await bookStatus(bob!.httpPort, aliceScratch.bookName))
                        .who,
                {
                    timeout: 90_000, // see above
                    message:
                        "Bob still sees the book checked out after Alice's check-in",
                },
            )
            .toBeFalsy();

        // --- Bob receives the update and ends up byte-identical to Alice's copy ---
        const receiveResponse = await postApi(
            bob.httpPort,
            "teamCollection/receiveUpdates",
            "{}",
        );
        expect(receiveResponse.status).toBe(200);

        await expect
            .poll(
                async () => {
                    const status = await bookStatus(
                        bob!.httpPort,
                        aliceScratch.bookName,
                    );
                    return status.isChangedRemotely;
                },
                {
                    timeout: 20_000,
                    message: "Bob's copy never caught up after receiveUpdates",
                },
            )
            .toBeFalsy();

        const aliceBookHtmlPath = `${aliceScratch.collectionFolder}\\${aliceScratch.bookName}\\${aliceScratch.bookName}.htm`;
        const bobBookHtmlPath = `${bobCollectionFilePath.replace(/[^\\]+\.bloomCollection$/, "")}${aliceScratch.bookName}\\${aliceScratch.bookName}.htm`;
        const [aliceBytes, bobBytes] = await Promise.all([
            fs.readFile(aliceBookHtmlPath),
            fs.readFile(bobBookHtmlPath),
        ]);
        expect(bobBytes.equals(aliceBytes)).toBe(true);
    });
});
