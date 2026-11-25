// A speech bubble's tail bends through a midpoint that ComicalJS places itself until a person drags
// the midpoint's handle. The handle, a small circle, is open while ComicalJS places the midpoint and
// filled once it has been dragged. Double-clicking the filled handle gives the midpoint back to
// ComicalJS: the tail returns to the curve it had before the drag, and the circle is open again.
//
// Automates "Tail Grab Handle Reset" (Test Case ID 330).
//
// The handle is a paper.js shape on ComicalJS's drawing surface, not an element, so the test finds
// it where the bubble's data-bubble spec says the midpoint is, and reads the drawing there to tell a
// filled circle from an open one (helpers/speechBubbleTail.ts).

import { type Locator, type Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    canvasElement,
    dragPaletteItemOntoCanvas,
    selectCanvasElement,
} from "../helpers/canvasElements";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { watchForScriptErrors } from "../helpers/scriptErrors";
import {
    doubleClickTailMidpoint,
    dragTailMidpoint,
    expectMidpointHandleFilled,
    readTail,
} from "../helpers/speechBubbleTail";

test.use({
    collectionSpec: {
        name: "canvas-tail-handle-reset",
        languages: ["en"],
        // Below Pro, the Canvas tool's palette sits behind a subscription notice.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** How far the drag moves the midpoint, in the drawing surface's pixels. */
const kDragDistance = 40;
/** How far from where it started a midpoint given back to ComicalJS may land. */
const kMidpointTolerance = 1;

/**
 * Add a Canvas page, drop a speech bubble on it and select it, so that ComicalJS shows its tail's
 * handles. Returns the bubble.
 */
async function addSelectedSpeechBubble(page: Page): Promise<Locator> {
    const before = (await getContentPages(page)).map((p) => p.id);
    await addPage(page, "Canvas");
    const added = (await getContentPages(page)).find(
        (p) => !before.includes(p.id),
    );
    if (!added) throw new Error("Adding a Canvas page added no content page.");
    await goToPage(page, added.id);
    const index = await dragPaletteItemOntoCanvas(page, "speech");
    await selectCanvasElement(page, index);
    return canvasElement(page, index);
}

/**
 * The direction, as a unit vector in the drawing surface's coordinates, from the middle of the
 * bubble towards the tail's midpoint. Dragging the midpoint that way keeps it outside the bubble,
 * where ComicalJS accepts it.
 */
async function awayFromBubble(
    bubble: Locator,
    midpoint: { x: number; y: number },
): Promise<{ x: number; y: number }> {
    // The drawing surface sits at the top left of the canvas, which is the bubble's offset parent.
    const centre = await bubble.evaluate((e) => {
        const element = e as HTMLElement;
        return {
            x: element.offsetLeft + element.offsetWidth / 2,
            y: element.offsetTop + element.offsetHeight / 2,
        };
    });
    const dx = midpoint.x - centre.x;
    const dy = midpoint.y - centre.y;
    const length = Math.hypot(dx, dy);
    if (length === 0)
        throw new Error(
            "The tail's midpoint is at the middle of its bubble, so there is no way out of it.",
        );
    return { x: dx / length, y: dy / length };
}

test("Double-clicking a dragged tail's midpoint handle gives the curve back to Bloom [Test Case ID 330]", async ({
    page,
    step,
}) => {
    test.setTimeout(240000);
    await makeBookFromTemplate(page, "Basic Book");
    const errors = watchForScriptErrors(page);
    try {
        const bubble = await addSelectedSpeechBubble(page);
        const autoTail = await readTail(bubble);
        expect(
            autoTail.autoCurve,
            `A new speech bubble's tail should start with its curve placed by Bloom: ${JSON.stringify(autoTail)}`,
        ).toBe(true);
        await expectMidpointHandleFilled(
            page,
            bubble,
            false,
            "A new speech bubble's tail midpoint handle should be drawn open.",
        );

        await step("Drag the middle grab point of the tail", async () => {
            const away = await awayFromBubble(bubble, {
                x: autoTail.midpointX,
                y: autoTail.midpointY,
            });
            const dragged = await dragTailMidpoint(
                page,
                bubble,
                away.x * kDragDistance,
                away.y * kDragDistance,
            );
            expect(
                Math.hypot(
                    dragged.midpointX - autoTail.midpointX,
                    dragged.midpointY - autoTail.midpointY,
                ),
                `Dragging the midpoint handle did not move the midpoint far: it went from ` +
                    `${JSON.stringify(autoTail)} to ${JSON.stringify(dragged)}.`,
            ).toBeGreaterThan(kDragDistance / 2);
            await expectMidpointHandleFilled(
                page,
                bubble,
                true,
                "After the tail's midpoint was dragged, its handle should be drawn filled.",
            );
        });

        await step("Double-click the filled circle", async () => {
            await doubleClickTailMidpoint(page, bubble);
            await expect
                .poll(async () => (await readTail(bubble)).autoCurve, {
                    timeout: 15000,
                    message:
                        "Double-clicking the filled midpoint handle did not give the tail's " +
                        "curve back to Bloom.",
                })
                .toBe(true);
            const reset = await readTail(bubble);
            expect(
                Math.abs(reset.midpointX - autoTail.midpointX) <=
                    kMidpointTolerance &&
                    Math.abs(reset.midpointY - autoTail.midpointY) <=
                        kMidpointTolerance,
                `The tail's midpoint should return to where Bloom first put it ` +
                    `(${autoTail.midpointX}, ${autoTail.midpointY}); it is at ` +
                    `(${reset.midpointX}, ${reset.midpointY}).`,
            ).toBe(true);
            await expectMidpointHandleFilled(
                page,
                bubble,
                false,
                "After the double-click, the tail's midpoint handle should be drawn open again.",
            );
        });

        expect(errors.errors(), "Bloom raised a script error.").toEqual([]);
    } finally {
        errors.stop();
    }
});
