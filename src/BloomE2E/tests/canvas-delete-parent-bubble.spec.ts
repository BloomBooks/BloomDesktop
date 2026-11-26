// A speech bubble can have a child bubble, joined to it by a tail that ends at the parent's middle.
// Only the parent has a tail that points at the speaker. When the parent is deleted, the child is
// left alone on the page and takes the parent's place: its joiner, which now leads nowhere, is
// replaced by the parent's tail, still pointing where the person aimed it, with its curve fitted
// to where the child is.
//
// Automates "Delete Parent Bubble" (Test Case ID 335).

import { type Locator } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    addChildBubble,
    canvasElement,
    deleteCanvasElement,
    dragPaletteItemOntoCanvas,
    getCanvasElementCount,
    getCanvasElementText,
    selectCanvasElement,
    typeInCanvasElement,
} from "../helpers/canvasElements";
import { getElementSize, type IElementSize } from "../helpers/canvasResize";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { watchForScriptErrors } from "../helpers/scriptErrors";
import {
    expectMidpointHandle,
    type ITailSpec,
    readBubbleFamilySpec,
} from "../helpers/speechBubbleTail";

test.use({
    collectionSpec: {
        name: "canvas-delete-parent-bubble",
        languages: ["en"],
        // Below Pro, the Canvas tool's palette sits behind a subscription notice.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const kParentWords = "Parent";
const kChildWords = "Child";
/** How far, in the page's own pixels, a point may be from where it is expected. */
const kPointTolerance = 1;

/**
 * Check that the one bubble left after its parent was deleted, `bubble`, has the tail the parent
 * had and no other: one tail, which is no joiner, ends where the parent's tail ended, and still has
 * its curve placed by Bloom, through a midpoint that lies between the bubble and the tail's end.
 * Also that the bubble is where the child was, `childPlace`. `when` says when, for the failure
 * messages.
 */
async function expectChildHasParentsTail(
    bubble: Locator,
    parentTail: ITailSpec,
    childPlace: IElementSize,
    when: string,
): Promise<void> {
    const place = await getElementSize(bubble);
    expect(
        Math.abs(place.left - childPlace.left) <= kPointTolerance &&
            Math.abs(place.top - childPlace.top) <= kPointTolerance,
        `${when}, the remaining bubble should be where the child was, at ` +
            `(${childPlace.left}, ${childPlace.top}); it is at (${place.left}, ${place.top}).`,
    ).toBe(true);

    const spec = await readBubbleFamilySpec(bubble);
    expect(
        spec.tails.length,
        `${when}, the remaining bubble should have exactly one tail: ${JSON.stringify(spec)}`,
    ).toBe(1);
    const tail = spec.tails[0];
    expect(
        tail.joiner,
        `${when}, the remaining bubble's tail should point at the speaker, not join the bubble ` +
            `to a parent that is gone: ${JSON.stringify(tail)}`,
    ).toBe(false);
    expect(
        Math.abs(tail.tipX - parentTail.tipX) <= kPointTolerance &&
            Math.abs(tail.tipY - parentTail.tipY) <= kPointTolerance,
        `${when}, the remaining bubble's tail should end where the parent's tail ended, ` +
            `(${parentTail.tipX}, ${parentTail.tipY}); it ends at (${tail.tipX}, ${tail.tipY}).`,
    ).toBe(true);
    expect(
        tail.autoCurve,
        `${when}, Bloom should still place the curve of the remaining bubble's tail, as it ` +
            `placed the parent's: ${JSON.stringify(tail)}`,
    ).toBe(true);

    // A tail's coordinates count from the canvas's corner, as the bubble's left and top do. The
    // curve bends through the midpoint, so a midpoint under the bubble, or farther from either
    // end of the tail than the ends are from each other, draws a tail that doubles back.
    const centre = {
        x: place.left + place.width / 2,
        y: place.top + place.height / 2,
    };
    const tailLength = Math.hypot(tail.tipX - centre.x, tail.tipY - centre.y);
    const midpointIsUnderBubble =
        tail.midpointX >= place.left &&
        tail.midpointX <= place.left + place.width &&
        tail.midpointY >= place.top &&
        tail.midpointY <= place.top + place.height;
    expect(
        !midpointIsUnderBubble &&
            Math.hypot(tail.midpointX - centre.x, tail.midpointY - centre.y) <
                tailLength &&
            Math.hypot(tail.midpointX - tail.tipX, tail.midpointY - tail.tipY) <
                tailLength,
        `${when}, the midpoint of the remaining bubble's tail should lie between the bubble ` +
            `(${JSON.stringify(place)}) and the tail's end: ${JSON.stringify(tail)}`,
    ).toBe(true);
}

test("Deleting a parent bubble leaves its child with the parent's tail [Test Case ID 335]", async ({
    page,
    step,
}) => {
    test.setTimeout(240000);
    await makeBookFromTemplate(page, "Basic Book");
    const errors = watchForScriptErrors(page);
    try {
        const before = (await getContentPages(page)).map((p) => p.id);
        await addPage(page, "Canvas");
        const canvasPage = (await getContentPages(page)).find(
            (p) => !before.includes(p.id),
        );
        if (!canvasPage)
            throw new Error("Adding a Canvas page added no content page.");
        await goToPage(page, canvasPage.id);

        const parentIndex = await dragPaletteItemOntoCanvas(page, "speech", {
            xFraction: 0.3,
            yFraction: 0.3,
        });
        const parent = canvasElement(page, parentIndex);
        await selectCanvasElement(page, parentIndex);
        await typeInCanvasElement(page, kParentWords);

        // Bloom appends the child, so it is the element after the parent.
        const childIndex = parentIndex + 1;
        await step("Give the bubble a child bubble", async () => {
            await selectCanvasElement(page, parentIndex);
            await addChildBubble(page);
            await selectCanvasElement(page, childIndex);
            await typeInCanvasElement(page, kChildWords);
        });
        const child = canvasElement(page, childIndex);

        // The start state: a parent whose one tail points at the speaker, and a child whose one
        // tail is the joiner to its parent.
        const parentSpec = await readBubbleFamilySpec(parent);
        expect(
            parentSpec.tails.map((t) => t.joiner),
            `The parent should have one tail, which is not a joiner: ${JSON.stringify(parentSpec)}`,
        ).toEqual([false]);
        const parentTail = parentSpec.tails[0];
        expect(
            parentTail.autoCurve,
            `A new speech bubble's tail should start with its curve placed by Bloom: ${JSON.stringify(parentTail)}`,
        ).toBe(true);
        const childSpec = await readBubbleFamilySpec(child);
        expect(
            childSpec.tails.map((t) => t.joiner),
            `The child should have one tail, the joiner to its parent: ${JSON.stringify(childSpec)}`,
        ).toEqual([true]);
        const childPlace = await getElementSize(child);

        await step("Delete the parent bubble", async () => {
            await selectCanvasElement(page, parentIndex);
            await deleteCanvasElement(page);
        });

        // The child is the only bubble left, and has taken the parent's index.
        expect(
            await getCanvasElementCount(page),
            "Deleting the parent should leave its child on the page.",
        ).toBe(childIndex);
        const remaining = canvasElement(page, parentIndex);
        expect(
            await getCanvasElementText(page, parentIndex),
            "The bubble that remains should be the child.",
        ).toBe(kChildWords);
        await expectChildHasParentsTail(
            remaining,
            parentTail,
            childPlace,
            "After the parent was deleted",
        );

        // Selecting the bubble makes Bloom draw its tail's handles, which it does only for a
        // tail it can draw.
        const clickRemainingBubble = async (when: string) => {
            await selectCanvasElement(page, parentIndex);
            await expectMidpointHandle(
                page,
                remaining,
                "open",
                `${when}, the remaining bubble's tail should show its midpoint handle, open, ` +
                    "when the bubble is selected.",
            );
        };
        await step("Click the remaining bubble", async () => {
            await clickRemainingBubble("After the parent was deleted");
        });

        await step("Leave the page and come back", async () => {
            const otherPage = (await getPages(page)).find(
                (p) => !p.isContentPage,
            );
            if (!otherPage)
                throw new Error("The book has no page but its content pages.");
            await goToPage(page, otherPage.id);
            await goToPage(page, canvasPage.id);
            expect(
                await getCanvasElementCount(page),
                "The saved page should hold the remaining bubble and no other.",
            ).toBe(childIndex);
            expect(
                await getCanvasElementText(page, parentIndex),
                "The saved bubble should be the child.",
            ).toBe(kChildWords);
            await expectChildHasParentsTail(
                remaining,
                parentTail,
                childPlace,
                "After the page was saved and shown again",
            );
            await clickRemainingBubble(
                "After the page was saved and shown again",
            );
        });

        expect(errors.errors(), "Bloom raised a script error.").toEqual([]);
    } finally {
        errors.stop();
    }
});
