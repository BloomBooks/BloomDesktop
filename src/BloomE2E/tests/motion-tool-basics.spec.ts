// The Motion tool's two rectangles on a page's picture: moving and resizing them, and the pan and
// zoom from rectangle 1 to rectangle 2 that its Preview plays, and that the BloomPUB preview plays
// when the book is turned sideways.
//
// Automates the manual test "Motion Tool Basics" (Test Case ID 542).
//
// "Preview works" is checked by what a reader sees: on every frame of the motion, the part of the
// picture showing through the preview's window. It must start on rectangle 1, end on rectangle 2,
// and go one way between them. Whether it looks smooth (the jitter of BL-12494, which the card says
// we live with) is not checked.
//
// Not ready to merge: until BL-17180 is fixed, the Motion tool rebuilds its rectangles 600 ms after
// it opens, and the first drag fails whenever it starts before that. Nothing in the page says when
// the rebuild is over, so the test has nothing to wait for. With the first drag started after it,
// the whole test passes.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import { apiGet } from "../helpers/api";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { chooseImageFile } from "../helpers/images";
import {
    dragMotionRectangle,
    expectMotionFromTo,
    expectSameRegion,
    getMotionRectangles,
    kRegionTolerance,
    startMotionRecording,
    waitForMotionRecording,
    waitForMotionRectangles,
    type IMotionRectangles,
    type IMotionRegion,
    type MotionGesture,
    type MotionRectangle,
} from "../helpers/motion";
import {
    selectPublishDestination,
    showBloomPubPreview,
    showPreviewPage,
} from "../helpers/publish";
import { realClick } from "../helpers/realClick";
import { expandTool, setToolTurnedOn } from "../helpers/toolbox";
import { switchTab } from "../helpers/workspace";
import type { Page } from "@playwright/test";

// Motion is a Pro feature: below it the tool shows only an upgrade notice.
test.use({
    collectionSpec: {
        name: "motion-tool-basics",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

// A picture twice as wide as it is tall, shipped with the suite.
const PICTURE_FILE = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "house-landscape.png",
);

/** The element the Motion tool's Preview shows the picture through, in the page being edited. */
const EDIT_PREVIEW_WINDOW = ".bloom-animationPreview > div";
/** The element bloom-player shows a motion page's picture through, on the page it is showing. */
const PLAYER_WINDOW = ".swiper-slide-active .bloom-ui-animationWrapper";

/** The rectangles must stay inside the picture and keep its shape, as the tool promises. */
function expectRectangleFitsPicture(
    rects: IMotionRectangles,
    rectangle: MotionRectangle,
    what: string,
): void {
    const shown = rects.shown[rectangle];
    // Fractions of the canvas's own width and height: the same shape as the canvas means the two
    // fractions are equal.
    expect(
        Math.abs(shown.width - shown.height),
        `${what}: rectangle ${rectangle} is not the shape of the picture (width ${shown.width.toFixed(3)}, height ${shown.height.toFixed(3)} of the picture's).`,
    ).toBeLessThanOrEqual(kRegionTolerance);
    expect(
        shown.left,
        `${what}: rectangle ${rectangle} sticks out on the left.`,
    ).toBeGreaterThanOrEqual(-kRegionTolerance);
    expect(
        shown.top,
        `${what}: rectangle ${rectangle} sticks out at the top.`,
    ).toBeGreaterThanOrEqual(-kRegionTolerance);
    expect(
        shown.left + shown.width,
        `${what}: rectangle ${rectangle} sticks out on the right.`,
    ).toBeLessThanOrEqual(1 + kRegionTolerance);
    expect(
        shown.top + shown.height,
        `${what}: rectangle ${rectangle} sticks out at the bottom.`,
    ).toBeLessThanOrEqual(1 + kRegionTolerance);
}

/** What Bloom saved for each rectangle is where the page shows it. */
function expectSavedAsShown(rects: IMotionRectangles, what: string): void {
    for (const rectangle of ["start", "end"] as const) {
        const saved = rects.saved[rectangle];
        expect(
            saved,
            `${what}: Bloom saved nothing for rectangle ${rectangle}.`,
        ).toBeTruthy();
        expectSameRegion(
            rects.shown[rectangle],
            saved!,
            `${what}: rectangle ${rectangle} on the page, against what Bloom saved`,
        );
        expectRectangleFitsPicture(rects, rectangle, what);
    }
}

/**
 * Drag one rectangle the way a person does, by `fx`, `fy` of the picture's width and height, and
 * check that it moved or grew that way, the other rectangle stayed put, and Bloom saved both
 * where the page shows them.
 */
async function dragAndCheck(
    page: Page,
    rectangle: MotionRectangle,
    gesture: MotionGesture,
    fx: number,
    fy: number,
): Promise<IMotionRectangles> {
    const what = `${gesture === "move" ? "Moving" : "Resizing"} rectangle ${rectangle} by ${fx},${fy} of the picture`;
    {
        const canvasBox = await editablePageFrame(page)
            .locator(".bloom-canvas")
            .first()
            .boundingBox();
        expect(canvasBox, "The picture has no box on the screen.").toBeTruthy();
        const { before, after } = await dragMotionRectangle(
            page,
            rectangle,
            gesture,
            fx * canvasBox!.width,
            fy * canvasBox!.height,
        );
        const was = before.shown[rectangle];
        const now = after.shown[rectangle];
        if (gesture === "move") {
            expect(
                Math.sign(now.left - was.left),
                `${what}: it did not go ${fx > 0 ? "right" : "left"}.`,
            ).toBe(Math.sign(fx));
            expect(
                Math.sign(now.top - was.top),
                `${what}: it did not go ${fy > 0 ? "down" : "up"}.`,
            ).toBe(Math.sign(fy));
            expect(
                Math.abs(now.width - was.width),
                `${what}: moving it changed its size.`,
            ).toBeLessThanOrEqual(kRegionTolerance);
        } else {
            expect(
                Math.sign(now.width - was.width),
                `${what}: it did not get ${fx > 0 ? "bigger" : "smaller"}.`,
            ).toBe(Math.sign(fx));
            expectSameRegion(
                { ...now, width: 0, height: 0 },
                { ...was, width: 0, height: 0 },
                `${what}: dragging its bottom right corner moved its top left corner`,
            );
        }
        const other: MotionRectangle = rectangle === "start" ? "end" : "start";
        expectSameRegion(
            after.shown[other],
            before.shown[other],
            `${what}: rectangle ${other} moved too`,
        );
        expectSavedAsShown(after, what);
        return after;
    }
}

/** Click the Motion tool's Preview and check that it pans and zooms from rectangle 1 to 2. */
async function previewInTool(
    page: Page,
    rects: IMotionRectangles,
    what: string,
): Promise<void> {
    {
        const frame = editablePageFrame(page);
        await startMotionRecording(frame, EDIT_PREVIEW_WINDOW);
        const toolbox = await expandTool(page, "motion", "#motion-preview");
        await realClick(toolbox.locator("#motion-preview"));
        const recording = await waitForMotionRecording(
            frame,
            "The Motion tool's Preview",
        );
        expectMotionFromTo(
            recording,
            rects.saved.start!,
            rects.saved.end!,
            `The Motion tool's Preview (${what})`,
        );
        // The preview puts the page back when it is done: the rectangles where they were.
        await expect(
            frame.locator(".bloom-animationPreview"),
            "The Motion tool's Preview never went away.",
        ).toHaveCount(0, { timeout: 30000 });
        await waitForMotionRectangles(page);
        const afterwards = await getMotionRectangles(page);
        for (const rectangle of ["start", "end"] as const)
            expectSameRegion(
                afterwards.shown[rectangle],
                rects.shown[rectangle],
                `After the Preview (${what}), rectangle ${rectangle}`,
            );
    }
}

test("moving and resizing the Motion tool's rectangles, and previewing the motion [Test Case ID 542]", async ({
    page,
    step,
}) => {
    test.setTimeout(400000);

    const motionPage = await step(
        "Make a book with a page holding a picture",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Basic Text & Image");
            const [contentPage] = await getContentPages(page);
            await goToPage(page, contentPage.id);
            await chooseImageFile(page, PICTURE_FILE);
            return contentPage;
        },
    );
    if (!motionPage) throw new Error("The book has no content page.");

    let rects: IMotionRectangles | undefined = await step(
        "Turn on the Motion tool and open it",
        async () => {
            await setToolTurnedOn(page, "motion", true);
            await expandTool(page, "motion", "#motion-preview");
            await waitForMotionRectangles(page);
            const shown = await getMotionRectangles(page);
            expectSavedAsShown(shown, "When the Motion tool opened");
            return shown;
        },
    );

    // Try a few times, as the card says: each round moves and resizes both rectangles, then
    // previews the motion they now describe.
    const rounds: {
        name: string;
        gestures: [MotionRectangle, MotionGesture, number, number][];
    }[] = [
        {
            name: "first round",
            gestures: [
                ["start", "resize", -0.3, -0.3],
                ["start", "move", 0.1, 0.15],
                ["end", "resize", -0.25, -0.25],
                ["end", "move", -0.2, -0.1],
            ],
        },
        {
            name: "second round",
            gestures: [
                ["end", "move", 0.4, 0.3],
                ["start", "move", -0.1, -0.15],
                ["start", "resize", 0.2, 0.2],
                ["end", "resize", -0.1, -0.1],
            ],
        },
    ];
    for (const round of rounds) {
        for (const [rectangle, gesture, fx, fy] of round.gestures)
            rects = await step(
                `${gesture === "move" ? "Move" : "Resize"} rectangle ${rectangle === "start" ? 1 : 2} (${round.name})`,
                () => dragAndCheck(page, rectangle, gesture, fx, fy),
            );
        const previewed = rects;
        if (!previewed) throw new Error("A step lost the rectangles.");
        await step(
            `Preview the motion in the Motion tool (${round.name})`,
            () => previewInTool(page, previewed, round.name),
        );
    }
    if (!rects) throw new Error("A step lost the rectangles.");
    const start: IMotionRegion = rects.saved.start!;
    const end: IMotionRegion = rects.saved.end!;

    await step(
        "Preview the book in BloomPUB, turned sideways, and watch the motion page",
        async () => {
            await switchTab(page, "publish");
            await selectPublishDestination(page, "BloomPUB");
            expect(
                (await apiGet(page, "publish/motionBookMode")).body,
                "BloomPUB's Motion Book feature is not on for a book with a motion page.",
            ).toBe("true");
            const player = await showBloomPubPreview(page);
            // The orientation buttons are offered only for a Motion Book. Turned before Preview,
            // the device goes back upright when the preview loads, so it is turned afterwards.
            await realClick(page.locator(".orientation-button.landscape"));
            await page
                .locator(".deviceFrame.fullSize.landscape")
                .waitFor({ state: "visible", timeout: 30000 });
            await startMotionRecording(player, PLAYER_WINDOW);
            await showPreviewPage(player, motionPage.id);
            const recording = await waitForMotionRecording(
                player,
                "The BloomPUB preview",
            );
            expectMotionFromTo(
                recording,
                start,
                end,
                "The BloomPUB preview's motion page",
            );
        },
    );
});
