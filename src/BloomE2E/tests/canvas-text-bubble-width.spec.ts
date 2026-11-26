// The width of a text bubble on a Canvas page. A bubble has an oval handle in the middle of its
// left and right sides. Dragging one makes the bubble wider or narrower, and the bubble's height
// follows by itself: narrower, the text wraps onto more lines and the bubble gets taller to show
// them all; wider, the text takes fewer lines and the bubble gets shorter.
//
// That the handles land on the grid, and what the other kinds of canvas element do, is in
// canvas-resize-handles.spec.ts; the Auto Height setting is in canvas-text-auto-height.spec.ts.
//
// Automates "Text Bubble Width Handles" (Test Case ID 304).

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { addPage, makeBookFromTemplate } from "../helpers/bookMaking";
import {
    dragCanvasElementSide,
    dragPaletteItemOntoCanvas,
    getCanvasElementAutoHeight,
    getCanvasElementTextBoxes,
    getCanvasElementTextFit,
    selectCanvasElement,
    typeInCanvasElement,
    waitForCanvasElementScrollBar,
} from "../helpers/canvasElements";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { kEdgeTolerance, right } from "../helpers/geometry";
import { watchForScriptErrors } from "../helpers/scriptErrors";

test.use({
    collectionSpec: {
        name: "canvas-text-bubble-width",
        languages: ["en"],
        // Below Pro, the Canvas tool's palette sits behind a subscription notice.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** Enough text for several lines in a bubble, so that a change of width changes how many. */
const kSentences =
    "The little goat climbed the hill. The wind blew across the grass. " +
    "At the top she found her friends waiting for her.";

/** How far each drag moves a handle, in screen pixels. */
const kDragDistance = 60;

/**
 * The drags of the test, in order: each side handle outwards, which widens the bubble, then each
 * inwards, which narrows it. `distance` is as dragCanvasElementSide takes it: to the right when
 * positive.
 */
const kDrags: {
    side: "e" | "w";
    distance: number;
    makesBubble: "wider" | "narrower";
}[] = [
    { side: "e", distance: kDragDistance, makesBubble: "wider" },
    { side: "w", distance: -kDragDistance, makesBubble: "wider" },
    { side: "e", distance: -kDragDistance, makesBubble: "narrower" },
    { side: "w", distance: kDragDistance, makesBubble: "narrower" },
];

/**
 * Wait until the selected bubble's height is on the `expected` side of `than` and all of its text
 * shows. Bloom fits the height as the handle moves, and again a moment after typing, so both are
 * waited for together. `when` starts the failure message.
 */
async function waitForHeightToFitText(
    page: Page,
    expected: "taller" | "shorter",
    than: number,
    when: string,
): Promise<void> {
    await expect
        .poll(
            async () => {
                const fit = await getCanvasElementTextFit(page);
                let height = "the same";
                if (fit.elementHeight > than) height = "taller";
                else if (fit.elementHeight < than) height = "shorter";
                return {
                    height,
                    allTextShows: fit.textHeight <= fit.shownHeight,
                };
            },
            {
                timeout: 15000,
                message: `${when}, the bubble should be ${expected} than ${than}px, with all of its text showing.`,
            },
        )
        .toEqual({ height: expected, allTextShows: true });
}

/** The text that canvas element `index` is showing. */
async function textShownIn(page: Page, index: number): Promise<string> {
    const shown = (await getCanvasElementTextBoxes(page, index)).filter(
        (box) => box.shown,
    );
    expect(
        shown.length,
        "The bubble should be showing its text in one language.",
    ).toBe(1);
    return shown[0].text;
}

test("a text bubble's side handles change its width, and its height follows the text [Test Case ID 304]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Canvas");
    // In the middle, since the bubble is widened to the left as well as to the right.
    const bubble = await dragPaletteItemOntoCanvas(page, "speech", {
        xFraction: 0.5,
        yFraction: 0.3,
    });
    await selectCanvasElement(page, bubble);

    // Sanity check what the rest stands on: a new bubble fits its own height.
    expect(
        await getCanvasElementAutoHeight(page),
        "A new text bubble should have Auto Height on.",
    ).toBe(true);
    const empty = await getCanvasElementTextFit(page);

    const errors = watchForScriptErrors(page);
    try {
        await step("Type a few sentences into the bubble", async () => {
            await typeInCanvasElement(page, kSentences, { withKeys: true });
            await waitForHeightToFitText(
                page,
                "taller",
                empty.elementHeight,
                "After typing",
            );
            // Sanity check what the drags are compared with.
            expect(
                await textShownIn(page, bubble),
                "The bubble should hold exactly what was typed into it.",
            ).toBe(kSentences);
        });

        for (const drag of kDrags) {
            const inOrOut = drag.makesBubble === "wider" ? "out" : "in";
            await step(
                `Drag the bubble's ${drag.side} handle ${inOrOut}wards`,
                async () => {
                    const heightBefore = (await getCanvasElementTextFit(page))
                        .elementHeight;
                    const { before, after } = await dragCanvasElementSide(
                        page,
                        drag.side,
                        drag.distance,
                    );
                    const what = `Dragging the ${drag.side} handle ${inOrOut}wards`;
                    // More than half the distance, since the width lands on a 10-pixel grid.
                    const widthChange = after.width - before.width;
                    if (drag.makesBubble === "wider")
                        expect(
                            widthChange,
                            `${what} should make the bubble wider.`,
                        ).toBeGreaterThan(kDragDistance / 2);
                    else
                        expect(
                            widthChange,
                            `${what} should make the bubble narrower.`,
                        ).toBeLessThan(-kDragDistance / 2);
                    // The side that was not dragged stays where it was.
                    if (drag.side === "e")
                        expect(
                            Math.abs(after.x - before.x),
                            `${what} should leave the bubble's left edge where it was.`,
                        ).toBeLessThanOrEqual(kEdgeTolerance);
                    else
                        expect(
                            Math.abs(right(after) - right(before)),
                            `${what} should leave the bubble's right edge where it was.`,
                        ).toBeLessThanOrEqual(kEdgeTolerance);

                    await waitForHeightToFitText(
                        page,
                        drag.makesBubble === "wider" ? "shorter" : "taller",
                        heightBefore,
                        what,
                    );
                    await waitForCanvasElementScrollBar(page, false);
                    expect(
                        await textShownIn(page, bubble),
                        `${what} should leave the bubble's text as it was typed.`,
                    ).toBe(kSentences);
                },
            );
        }

        expect(
            errors.errors(),
            "Changing the bubble's width raised a script error.",
        ).toEqual([]);
    } finally {
        errors.stop();
    }
});
