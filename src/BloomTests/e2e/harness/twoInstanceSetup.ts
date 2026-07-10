// Shared "Alice shares a collection, approves Bob, Bob joins" setup, factored out of E2E-2 once
// E2E-3 needed the identical sequence. Every scenario below E2E-2 that needs two instances on the
// same cloud collection should use this instead of re-deriving the create/approve/pullDown/
// relaunch dance (see E2E-2's header comment for why each individual step is shaped the way it
// is -- connect-before-trigger, direct-API create, relaunch-after-pullDown).
import { expect } from "@playwright/test";
import { Page } from "@playwright/test";
import {
    createScratchCollection,
    pulledDownCollectionFilePath,
    ScratchCollection,
} from "./collectionFixture";
import { launchBloom, LaunchedBloom } from "./launch";
import { ALICE, BOB, DevUser } from "./devStack";
import { postApi, getApi, postCreateCloudTeamCollection } from "./bloomApi";

export interface SharedCloudCollection {
    alice: LaunchedBloom;
    alicePage: Page;
    aliceScratch: ScratchCollection;
    bob: LaunchedBloom;
    bobCollectionFilePath: string;
}

/** Alice creates+shares `scenarioName`'s scratch collection, approves Bob as a member, and Bob
 * pulls it down and relaunches pointed at his own local copy. Returns both live instances plus
 * Alice's already-attached Page (kept open across the create-cloud-collection reopen per the
 * connect-before-trigger finding) and the path to Bob's pulled-down .bloomCollection file. Callers
 * are responsible for killing both instances (e.g. in `afterEach`). */
export const setUpAliceAndBobOnSharedCollection = async (
    scenarioName: string,
    logDir: string,
    bobUser: DevUser = BOB,
): Promise<SharedCloudCollection> => {
    const aliceScratch = await createScratchCollection(scenarioName, "alice");
    const alice = await launchBloom({
        collectionFilePath: aliceScratch.collectionFilePath,
        user: ALICE,
        label: `${scenarioName}-alice`,
        logDir,
    });

    // Connect BEFORE triggering createCloudTeamCollection and keep reusing this same Page --
    // see E2E-2's header comment for why a fresh connectOverCDP after the reopen is unreliable.
    const { page: alicePage } = await alice.connect();

    const createResponse = await postCreateCloudTeamCollection(alice);
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
            email: bobUser.email,
            role: "member",
        }),
    );
    expect(approveResponse.status).toBe(200);

    const bobPlaceholder = await createScratchCollection(
        scenarioName,
        "bob",
        "BobPlaceholder",
    );
    let bob = await launchBloom({
        collectionFilePath: bobPlaceholder.collectionFilePath,
        user: bobUser,
        label: `${scenarioName}-bob`,
        logDir,
    });

    const pullDownResponse = await postApi(
        bob.httpPort,
        "collections/pullDown",
        JSON.stringify({ collectionId: aliceScratch.collectionId }),
    );
    expect(pullDownResponse.status).toBe(200);

    // pullDown only downloads files -- it doesn't switch what the currently-running instance has
    // open, so relaunch pointed at the freshly pulled-down collection.
    await bob.kill();
    const bobCollectionFilePath = await pulledDownCollectionFilePath(
        aliceScratch.collectionName,
    );
    bob = await launchBloom({
        collectionFilePath: bobCollectionFilePath,
        user: bobUser,
        label: `${scenarioName}-bob-joined`,
        logDir,
    });
    // POLL, don't single-shot: teamCollection/capabilities is an application-level endpoint
    // (post-batch defect 3 fix) that answers truthfully-false while the project is still
    // opening, and BLOOM_AUTOMATION_READY fires when the server starts listening — which can
    // be before the Team Collection has finished connecting.
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

    return { alice, alicePage, aliceScratch, bob, bobCollectionFilePath };
};
