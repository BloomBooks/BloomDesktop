// Draggable videos in a game: a Custom Game page gets three draggable videos from the Game tool's
// palette, each with its own video and its own target. In the Play tab of the Edit tab, and again
// in the BloomPUB preview (Bloom Player itself), each video can be dragged onto its own target or
// onto another's, and a video plays only when it is pressed to start dragging it: none plays when
// the page is shown, and pressing one plays that one alone.
//
// Automates the manual test "Draggable Video In Game in BloomPUB" (Test Case ID 781). Its last
// step, opening the saved BloomPUB in BloomPUB Viewer, is another program and is not checked here.

import type { Locator, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    activePreviewPage,
    goToPreviewPage,
    waitForPreviewTurnToFinish,
} from "../helpers/bloomPlayer";
import { goToPage } from "../helpers/bookMaking";
import {
    canvasElement,
    selectCanvasElementAt,
} from "../helpers/canvasElements";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    clickGameTab,
    dragDraggableItemOnto,
    dragGamePaletteItemOntoPage,
    gamePageInEditTab,
    getDraggableVideoEvents,
    getDraggableVideoFiles,
    makeBookWithGamePage,
    startRecordingDraggableVideos,
    waitForDraggableVideosShown,
} from "../helpers/games";
import {
    selectPublishDestination,
    showBloomPubPreview,
} from "../helpers/publish";
import { chooseVideoFileFromCanvasMenu, SHORT_VIDEO } from "../helpers/videos";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "game-draggable-videos",
        languages: ["en"],
        // Games need a subscription that includes them.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** The events of the draggable videos so far, as "<item id> <event>". */
async function videoEventsSoFar(gamePage: Locator): Promise<string[]> {
    return (await getDraggableVideoEvents(gamePage)).map(
        (e) => `${e.itemId} ${e.event}`,
    );
}

/**
 * Drag item `itemId` onto the target of `targetOf`, and check that its video, and only its video,
 * started playing while the mouse button was still down on it, and played to its end. `before` is
 * what the videos had done before this drag. Returns what they have done after it.
 */
async function dragAndExpectOnlyItsVideoPlayed(
    page: Page,
    gamePage: Locator,
    itemId: string,
    targetOf: string,
    before: string[],
    where: string,
): Promise<string[]> {
    await dragDraggableItemOnto(page, gamePage, itemId, targetOf, async () => {
        await expect
            .poll(
                async () =>
                    (await getDraggableVideoEvents(gamePage)).filter(
                        (e) => e.itemId === itemId && e.event === "playing",
                    ),
                {
                    timeout: 10000,
                    message: `In ${where}, pressing on item ${itemId} to start dragging it did not play its video.`,
                },
            )
            .toEqual([{ itemId, event: "playing", pressed: true }]);
    });
    const after = [...before, `${itemId} playing`, `${itemId} ended`];
    await expect
        .poll(() => videoEventsSoFar(gamePage), {
            timeout: 15000,
            message:
                `In ${where}, dragging item ${itemId} should have played its own video to the end, ` +
                `and no other`,
        })
        .toEqual(after);
    return after;
}

/**
 * Play the game on `gamePage` with its three draggable videos `[first, second, third]`: check that
 * no video plays until one is pressed, then drag the first onto its own target and the second and
 * third each onto the other's target, checking each time that the pressed video alone plays.
 * Recording must have started (startRecordingDraggableVideos) before the page was shown.
 */
async function playDraggableVideoGame(
    page: Page,
    gamePage: Locator,
    [first, second, third]: string[],
    where: string,
): Promise<void> {
    await waitForDraggableVideosShown(gamePage);
    expect(
        await videoEventsSoFar(gamePage),
        `In ${where}, no draggable video should play before it is pressed`,
    ).toEqual([]);

    let played = await dragAndExpectOnlyItsVideoPlayed(
        page,
        gamePage,
        first,
        first,
        [],
        where,
    );
    played = await dragAndExpectOnlyItsVideoPlayed(
        page,
        gamePage,
        second,
        third,
        played,
        where,
    );
    await dragAndExpectOnlyItsVideoPlayed(
        page,
        gamePage,
        third,
        second,
        played,
        where,
    );
}

test("draggable videos can be dragged to right and wrong targets, and play only when pressed, in Play and in the BloomPUB preview [Test Case ID 781]", async ({
    page,
    step,
}) => {
    test.setTimeout(600000);

    const book = await makeBookWithGamePage(page, "Custom Game");

    const added = await step(
        "Add three draggable videos, each with its own video",
        async () => {
            for (const xFraction of [0.15, 0.5, 0.85]) {
                const index = await dragGamePaletteItemOntoPage(page, "video", {
                    xFraction,
                    yFraction: 0.25,
                });
                const element = canvasElement(page, index);
                await selectCanvasElementAt(element, "the new video");
                await chooseVideoFileFromCanvasMenu(
                    page,
                    element.locator(".bloom-videoContainer"),
                    SHORT_VIDEO,
                );
            }
            const files = await getDraggableVideoFiles(gamePageInEditTab(page));
            const ids = Object.keys(files);
            expect(
                ids.length,
                `The page should have three draggable videos; it has ${JSON.stringify(files)}`,
            ).toBe(3);
            expect(
                new Set(Object.values(files)).size,
                `Each draggable should have its own video file: ${JSON.stringify(files)}`,
            ).toBe(3);
            for (const id of ids)
                await expect(
                    gamePageInEditTab(page).locator(`[data-target-of="${id}"]`),
                    `The draggable video ${id} should have a target of its own`,
                ).toHaveCount(1);
            return { ids, files };
        },
    );
    if (!added) throw new Error("The draggable videos were never added.");
    const itemIds = added.ids;

    await step(
        "In the Play tab, drag the videos to right and wrong targets",
        async () => {
            const gamePage = gamePageInEditTab(page);
            await startRecordingDraggableVideos(gamePage);
            await clickGameTab(page, "Play");
            await playDraggableVideoGame(
                page,
                gamePage,
                itemIds,
                "the Play tab",
            );
            await clickGameTab(page, "Start");
            // Leaving the page saves it.
            await goToPage(page, book.otherPageId);
        },
    );

    await step(
        "In the BloomPUB preview, drag the videos to right and wrong targets",
        async () => {
            await switchTab(page, "publish");
            await selectPublishDestination(page, "BloomPUB");
            const player = await showBloomPubPreview(page);
            const gamePage = activePreviewPage(player);
            await startRecordingDraggableVideos(gamePage);
            await goToPreviewPage(page, player, book.gamePageId);
            await waitForPreviewTurnToFinish(player);
            expect(
                await getDraggableVideoFiles(gamePage),
                "The preview's game page should have the same three videos",
            ).toEqual(added.files);
            await playDraggableVideoGame(
                page,
                gamePage,
                itemIds,
                "the BloomPUB preview",
            );
        },
    );
});
