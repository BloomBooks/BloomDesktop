// Draggable videos in a game: a Custom Game page gets three draggable videos from the Game tool's
// palette, each with its own video and its own target. In the Play tab of the Edit tab, and again
// in the BloomPUB preview (Bloom Player itself), each video can be dragged onto its own target or
// onto another's, and a video plays only when it is pressed to start dragging it: none plays when
// the page is shown, and pressing one plays that one alone.
//
// The three videos are clips of different lengths (1, 1.5 and 2 seconds), so the length of the
// clip that played tells which video a draggable showed, whatever its file is called.
//
// Automates the manual test "Draggable Video In Game in BloomPUB" (Test Case ID 781). Its last
// step, opening the saved BloomPUB in BloomPUB Viewer, is another program and is not checked here.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
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
    getDraggableVideoPlaybackPositions,
    getDraggableVideosPlayingAtEachRelease,
    makeBookWithGamePage,
    startRecordingDraggableVideos,
    waitForDraggableVideosShown,
} from "../helpers/games";
import {
    selectPublishDestination,
    showBloomPubPreview,
} from "../helpers/publish";
import { chooseVideoFileFromCanvasMenu } from "../helpers/videos";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "game-draggable-videos",
        languages: ["en"],
        // Games need a subscription that includes them.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const VIDEO_FIXTURES = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "videos",
);

/** The clip each of the three draggables gets, left to right, and how long it plays. */
const kClips = [
    { file: Path.join(VIDEO_FIXTURES, "short.mp4"), seconds: 1 },
    { file: Path.join(VIDEO_FIXTURES, "short-1.5s.mp4"), seconds: 1.5 },
    { file: Path.join(VIDEO_FIXTURES, "short-2s.mp4"), seconds: 2 },
];

/**
 * How far into its clip a video may be and still count as not played: two frames of the 10
 * frames-a-second clips.
 */
const kFirstFrameSeconds = 0.2;

/** A draggable video of the game page: its draggable id and the length of its clip. */
interface IDraggableVideo {
    id: string;
    seconds: number;
}

/** The events of the draggable videos so far, as "<item id> <event> <clip length>s". */
async function videoEventsSoFar(gamePage: Locator): Promise<string[]> {
    return (await getDraggableVideoEvents(gamePage)).map(
        (e) => `${e.itemId} ${e.event} ${e.seconds}s`,
    );
}

/**
 * Drag `video` onto the target of `targetOf`, and check that its own clip, and no other, played to
 * its end. With `waitWhilePressed`, the drag waits after pressing until the clip has started
 * playing, which shows that the press alone starts it; without, it moves at once, as a quick reader
 * does. `before` is what the videos had done before this drag. Returns what they have done after.
 */
async function dragAndExpectOnlyItsVideoPlayed(
    page: Page,
    gamePage: Locator,
    video: IDraggableVideo,
    targetOf: string,
    waitWhilePressed: boolean,
    before: string[],
    where: string,
): Promise<string[]> {
    const releasesBefore = (
        await getDraggableVideosPlayingAtEachRelease(gamePage)
    ).length;
    await dragDraggableItemOnto(
        page,
        gamePage,
        video.id,
        targetOf,
        waitWhilePressed
            ? async () => {
                  await expect
                      .poll(
                          async () =>
                              (await getDraggableVideoEvents(gamePage)).filter(
                                  (e) =>
                                      e.itemId === video.id &&
                                      e.event === "playing",
                              ),
                          {
                              timeout: 10000,
                              message: `In ${where}, pressing on item ${video.id} to start dragging it did not play its video.`,
                          },
                      )
                      .toEqual([
                          {
                              itemId: video.id,
                              event: "playing",
                              seconds: video.seconds,
                              pressed: true,
                          },
                      ]);
              }
            : undefined,
    );
    // The press, not the release, has to start the clip: as the button comes up it is playing,
    // however quick the drag was, or has already played to its end, however slow.
    expect(
        (await getDraggableVideosPlayingAtEachRelease(gamePage)).slice(
            releasesBefore,
        ),
        `In ${where}, item ${video.id}'s video, and only it, should have been started by the press when the drag ends`,
    ).toEqual([[video.id]]);
    const after = [
        ...before,
        `${video.id} playing ${video.seconds}s`,
        `${video.id} ended ${video.seconds}s`,
    ];
    await expect
        .poll(() => videoEventsSoFar(gamePage), {
            timeout: 15000,
            message:
                `In ${where}, dragging item ${video.id} should have played its own ` +
                `${video.seconds}-second clip to the end, and no other`,
        })
        .toEqual(after);
    return after;
}

/**
 * Play the game on `gamePage` with its three draggable videos: check that no video plays until
 * one is pressed, then drag the first onto its own target and the second and third each onto the
 * other's target, checking each time that the pressed video alone plays. Recording must have
 * started (startRecordingDraggableVideos) before the page was shown.
 */
async function playDraggableVideoGame(
    page: Page,
    gamePage: Locator,
    [first, second, third]: IDraggableVideo[],
    where: string,
): Promise<void> {
    await waitForDraggableVideosShown(gamePage);
    expect(
        await videoEventsSoFar(gamePage),
        `In ${where}, no draggable video should play before it is pressed`,
    ).toEqual([]);
    // Showing a video's first frame plays it, silently, for an instant; a video that played on,
    // even silently, would be further in.
    const positions = await getDraggableVideoPlaybackPositions(gamePage);
    expect(
        Object.entries(positions).filter(
            ([, at]) => at === "ended" || at > kFirstFrameSeconds,
        ),
        `In ${where}, every draggable video should still be at its start before it is pressed ` +
            `(seconds into each: ${JSON.stringify(positions)})`,
    ).toEqual([]);

    let played = await dragAndExpectOnlyItsVideoPlayed(
        page,
        gamePage,
        first,
        first.id,
        true,
        [],
        where,
    );
    played = await dragAndExpectOnlyItsVideoPlayed(
        page,
        gamePage,
        second,
        third.id,
        false,
        played,
        where,
    );
    await dragAndExpectOnlyItsVideoPlayed(
        page,
        gamePage,
        third,
        second.id,
        false,
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
            const videos: IDraggableVideo[] = [];
            for (const [i, clip] of kClips.entries()) {
                const index = await dragGamePaletteItemOntoPage(page, "video", {
                    xFraction: [0.15, 0.5, 0.85][i],
                    yFraction: 0.25,
                });
                const element = canvasElement(page, index);
                await selectCanvasElementAt(element, "the new video");
                await chooseVideoFileFromCanvasMenu(
                    page,
                    element.locator(".bloom-videoContainer"),
                    clip.file,
                );
                const id = await element.getAttribute("data-draggable-id");
                expect(
                    id,
                    "The new video from the Objects to Drag palette should be draggable",
                ).toBeTruthy();
                videos.push({ id: id!, seconds: clip.seconds });
            }
            const files = await getDraggableVideoFiles(gamePageInEditTab(page));
            expect(
                Object.keys(files).sort(),
                `The page should have the three draggable videos; it has ${JSON.stringify(files)}`,
            ).toEqual(videos.map((v) => v.id).sort());
            expect(
                new Set(Object.values(files)).size,
                `Each draggable should have its own video file: ${JSON.stringify(files)}`,
            ).toBe(3);
            for (const video of videos)
                await expect(
                    gamePageInEditTab(page).locator(
                        `[data-target-of="${video.id}"]`,
                    ),
                    `The draggable video ${video.id} should have a target of its own`,
                ).toHaveCount(1);
            return { videos, files };
        },
    );
    if (!added) throw new Error("The draggable videos were never added.");

    await step(
        "In the Play tab, drag the videos to right and wrong targets",
        async () => {
            const gamePage = gamePageInEditTab(page);
            await startRecordingDraggableVideos(gamePage);
            await clickGameTab(page, "Play");
            await playDraggableVideoGame(
                page,
                gamePage,
                added.videos,
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
                added.videos,
                "the BloomPUB preview",
            );
        },
    );
});
