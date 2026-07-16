// Pins the task-10 "pull-down auto-open" behavior end to end: when Bloom is told to join a
// cloud Team Collection, the user must land IN that collection without choosing it again.
//
// The join dialog itself (JoinCloudCollectionDialog, hosted in the collection chooser) is not
// CDP-reachable (see README.md finding #2), so this spec drives the exact same two calls the
// dialog makes, in order, against a REAL running instance:
//   1. POST collections/pullDown            -> replies { collectionPath: <.bloomCollection file> }
//   2. POST workspace/openCollection <path> -> Bloom switches to that collection IN PLACE
// and then asserts the instance is now inside a functioning cloud TC (capabilities flipped,
// collection name matches) WITHOUT any relaunch — the relaunch-after-pullDown dance the other
// scenarios use predates auto-open and deliberately bypasses it.
//
// The dialog's own half (that it really makes call 2 with the reply of call 1) is covered by
// JoinCloudCollectionDialog.test.tsx; this spec covers everything below it live, including the
// path-shape contract that broke once already (HandlePullDown originally returned the FOLDER,
// which Program.SwitchToCollection cannot open — caught in task-10 review, pinned here).
import { test, expect } from "@playwright/test";
import * as fs from "node:fs/promises";
import { resetStack } from "../harness/reset";
import { createScratchCollection } from "../harness/collectionFixture";
import { launchBloom, LaunchedBloom } from "../harness/launch";
import { ALICE, BOB } from "../harness/devStack";
import {
    postApi,
    getApi,
    postCreateCloudTeamCollection,
    waitForSharingReady,
} from "../harness/bloomApi";

const LOG_DIR = "C:\\BloomE2E-logs\\join-auto-open";

test.describe("Join auto-open", () => {
    const instances: LaunchedBloom[] = [];
    const track = (instance: LaunchedBloom): LaunchedBloom => {
        instances.push(instance);
        return instance;
    };

    test.beforeEach(async () => {
        await resetStack();
    });

    test.afterEach(async () => {
        await Promise.all(instances.map((i) => i.kill()));
        instances.length = 0;
    });

    test("pullDown replies with an openable .bloomCollection path, and openCollection lands in the TC without a relaunch", async () => {
        // --- Alice shares a collection and approves Bob ---
        const aliceScratch = await createScratchCollection(
            "join-auto-open",
            "alice",
        );
        const alice = track(
            await launchBloom({
                collectionFilePath: aliceScratch.collectionFilePath,
                user: ALICE,
                label: "join-auto-open-alice",
                logDir: LOG_DIR,
            }),
        );
        await alice.connect(); // connect-before-trigger (finding #7)
        expect((await postCreateCloudTeamCollection(alice)).status).toBe(200);
        await waitForSharingReady(alice.httpPort);
        expect(
            (
                await postApi(
                    alice.httpPort,
                    "sharing/addApproval",
                    JSON.stringify({
                        collectionId: aliceScratch.collectionId,
                        email: BOB.email,
                        role: "member",
                    }),
                )
            ).status,
        ).toBe(200);

        // --- Bob, on a placeholder collection, joins: the dialog's two calls, verbatim ---
        const bobPlaceholder = await createScratchCollection(
            "join-auto-open",
            "bob",
            "BobPlaceholder",
        );
        const bob = track(
            await launchBloom({
                collectionFilePath: bobPlaceholder.collectionFilePath,
                user: BOB,
                label: "join-auto-open-bob",
                logDir: LOG_DIR,
            }),
        );
        // Bob is NOT in a cloud TC yet — sanity check before asserting the switch happened.
        const bobCapsBefore = (await (
            await getApi(bob.httpPort, "teamCollection/capabilities")
        ).json()) as { supportsSharingUi: boolean };
        expect(bobCapsBefore.supportsSharingUi).toBe(false);

        // Call 1: pullDown. The reply's collectionPath is the auto-open contract: a
        // .bloomCollection FILE (what the chooser's cards pass to workspace/openCollection),
        // never the folder.
        const pullDownResponse = await postApi(
            bob.httpPort,
            "collections/pullDown",
            JSON.stringify({ collectionId: aliceScratch.collectionId }),
        );
        expect(pullDownResponse.status).toBe(200);
        const { collectionPath } = (await pullDownResponse.json()) as {
            collectionPath: string;
        };
        expect(
            collectionPath,
            "pullDown must reply with the .bloomCollection file path the dialog auto-opens",
        ).toMatch(/\.bloomCollection$/);
        await fs.access(collectionPath); // the file must really exist on disk

        // Call 2: openCollection — the same in-place switch the dialog triggers. No relaunch.
        expect(
            (
                await postApi(
                    bob.httpPort,
                    "workspace/openCollection",
                    collectionPath,
                )
            ).status,
        ).toBe(200);

        // The switch happens on Application.Idle and reopens the workspace; poll the SAME
        // instance (same port — the process survives the switch) until it reports being
        // inside the cloud TC. Much longer timeout than the default: the in-place reopen
        // includes the full initial sync of the pulled-down collection.
        await waitForSharingReady(bob.httpPort, 120_000);

        // And it is the RIGHT collection, fully functional (book visible via status API).
        const name = await (
            await getApi(bob.httpPort, "teamCollection/getCollectionName")
        ).text();
        expect(name).toContain(aliceScratch.collectionName);
        const statusResponse = await getApi(
            bob.httpPort,
            `teamCollection/bookStatus?folderName=${encodeURIComponent(aliceScratch.bookName)}`,
        );
        expect(statusResponse.status).toBe(200);
    });
});
