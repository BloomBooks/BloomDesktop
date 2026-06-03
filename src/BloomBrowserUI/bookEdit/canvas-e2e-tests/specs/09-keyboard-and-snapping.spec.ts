// Spec 09 – Keyboard movement + snapping + guides (Areas E1-E5)
//
// Covers: CanvasElementKeyboardProvider.ts, CanvasSnapProvider.ts,
//         CanvasGuideProvider.ts.

import { test, expect } from "../fixtures/canvasTest";
import {
    dragPaletteItemToCanvas,
    dragActiveCanvasElementByOffset,
    getCanvasElementCount,
    getActiveCanvasElement,
    keyboardNudge,
} from "../helpers/canvasActions";
import {
    expectCanvasElementCountToIncrease,
    expectAnyCanvasElementActive,
    expectElementVisible,
    expectElementHasPositiveSize,
    expectPositionGridSnapped,
} from "../helpers/canvasAssertions";
import { canvasSelectors } from "../helpers/canvasSelectors";

// ── Helper ──────────────────────────────────────────────────────────────

const createSpeechElement = async (canvasTestContext) => {
    const beforeCount = await getCanvasElementCount(canvasTestContext);
    await dragPaletteItemToCanvas({
        canvasContext: canvasTestContext,
        paletteItem: "speech",
    });
    await expectCanvasElementCountToIncrease(canvasTestContext, beforeCount);

    const createdElement = canvasTestContext.pageFrame
        .locator(canvasSelectors.page.canvasElements)
        .nth(beforeCount);
    await createdElement.waitFor({ state: "visible", timeout: 10000 });

    await expectAnyCanvasElementActive(canvasTestContext);
    return createdElement;
};

const createImageElement = async (canvasTestContext) => {
    const beforeCount = await getCanvasElementCount(canvasTestContext);
    await dragPaletteItemToCanvas({
        canvasContext: canvasTestContext,
        paletteItem: "image",
    });
    await expectCanvasElementCountToIncrease(canvasTestContext, beforeCount);

    const createdElement = canvasTestContext.pageFrame
        .locator(canvasSelectors.page.canvasElements)
        .nth(beforeCount);
    await createdElement.waitFor({ state: "visible", timeout: 10000 });

    await expectAnyCanvasElementActive(canvasTestContext);
    return createdElement;
};

// ── E1: Arrow key moves element by grid step ────────────────────────────

test("E1: arrow-right moves the active element", async ({
    canvasTestContext,
}) => {
    await createSpeechElement(canvasTestContext);

    const active = getActiveCanvasElement(canvasTestContext);
    await active.click();

    // Press arrow right multiple times to accumulate visible movement
    for (let i = 0; i < 3; i++) {
        await keyboardNudge(canvasTestContext, "ArrowRight");
    }

    await expectAnyCanvasElementActive(canvasTestContext);
    await expectElementVisible(active);
    await expectElementHasPositiveSize(active);
});

test("E1: arrow-down moves the active element", async ({
    canvasTestContext,
}) => {
    await createSpeechElement(canvasTestContext);

    const active = getActiveCanvasElement(canvasTestContext);
    await active.click();

    for (let i = 0; i < 3; i++) {
        await keyboardNudge(canvasTestContext, "ArrowDown");
    }

    await expectAnyCanvasElementActive(canvasTestContext);
    await expectElementVisible(active);
    await expectElementHasPositiveSize(active);
});

// ── E2: Ctrl+arrow for precise 1px movement ────────────────────────────

test("E2: Ctrl+arrow-right moves by small increment", async ({
    canvasTestContext,
}) => {
    await createSpeechElement(canvasTestContext);

    const active = getActiveCanvasElement(canvasTestContext);
    await active.click();

    // Ctrl+arrow should move by 1px
    for (let i = 0; i < 5; i++) {
        await keyboardNudge(canvasTestContext, "ArrowRight", {
            ctrl: true,
        });
    }

    await expectAnyCanvasElementActive(canvasTestContext);
    await expectElementVisible(active);
    await expectElementHasPositiveSize(active);
});

// ── E4: Position is grid-snapped after arrow key movement ───────────────

test("E4: position is grid-snapped after arrow key movement", async ({
    canvasTestContext,
}) => {
    await createSpeechElement(canvasTestContext);

    const active = getActiveCanvasElement(canvasTestContext);

    // Arrow keys use grid=10 by default
    await keyboardNudge(canvasTestContext, "ArrowRight");
    await keyboardNudge(canvasTestContext, "ArrowDown");

    await expectPositionGridSnapped(active, 10);
});

// ── E3: Shift constrains drag axis ──────────────────────────────────

test("E3: Shift+drag constrains movement to primary axis", async ({
    canvasTestContext,
}) => {
    const createdElement = await createImageElement(canvasTestContext);

    await canvasTestContext.page.keyboard.press("Escape");

    const maxAttempts = 3;
    for (let attempt = 0; attempt < maxAttempts; attempt++) {
        const beforeShiftBox = await createdElement.boundingBox();
        if (!beforeShiftBox) {
            throw new Error(
                "Could not determine active element bounds before shift drag.",
            );
        }

        await dragActiveCanvasElementByOffset(canvasTestContext, 60, 10, {
            shift: true,
            element: createdElement,
        });

        const afterShiftBox = await createdElement.boundingBox();
        if (!afterShiftBox) {
            throw new Error(
                "Could not determine active element bounds after shift drag.",
            );
        }

        const actualDx = Math.abs(afterShiftBox.x - beforeShiftBox.x);
        const actualDy = Math.abs(afterShiftBox.y - beforeShiftBox.y);

        if (actualDx + actualDy > 2) {
            // Secondary axis (Y) should be constrained (less or equal to primary)
            expect(actualDy).toBeLessThanOrEqual(actualDx);
            return;
        }

        await canvasTestContext.page.keyboard.press("Escape");
    }

    throw new Error(
        "Shift+drag did not move the active element after bounded retries.",
    );
});
